using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Murmli.Backend.Api.Tests.Infrastructure;
using Murmli.Backend.Api.Tests.Infrastructure.TestDataTypes;
using Murmli.Backend.Data.Identity;
using Murmli.Backend.Data.Sync;
using Murmli.Backend.Sync.Protocol;

namespace Murmli.Backend.Api.Tests.Users;

public class UserEndpointsTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private readonly string connectionString = postgres.CreateDatabaseConnectionString();

    public static TheoryData<Exception> AuthServiceTransportFailures =>
        new()
        {
            // HttpClient reports its own timeout as a TaskCanceledException wrapping a TimeoutException
            new TaskCanceledException(
                "The request was canceled due to the configured HttpClient.Timeout of 10 seconds elapsing.",
                new TimeoutException()
            ),
            new HttpRequestException(
                HttpRequestError.ConnectionError,
                "Connection refused (auth.test:80)",
                new SocketException((int)SocketError.ConnectionRefused)
            ),
        };

    [Fact]
    public async Task GetMe_WithoutToken_ReturnsUnauthorized()
    {
        await using var api = await ApiHost.StartAsync(connectionString);

        using var response = await api.HttpClient.GetAsync("/v1/users/me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetMe_TokenForOtherAudience_ReturnsUnauthorized()
    {
        await using var api = await ApiHost.StartAsync(connectionString);

        using var response = await SendGetMeAsync(api, TestTokens.Create("AuthUser0000000000000000000001", "other-audience"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetMe_FirstRequest_CreatesUser()
    {
        const string authUserId = "AuthUser0000000000000000000001";
        await using var api = await ApiHost.StartAsync(connectionString);

        var userId = await api.GetMeAsync(authUserId);

        var user = Assert.Single(await ReadUsersAsync(api));
        Assert.Equal(authUserId, user.AuthUserId);
        Assert.Equal(user.Id, userId);
    }

    [Fact]
    public async Task GetMe_ConcurrentFirstRequests_CreateOneUser()
    {
        await using var api = await ApiHost.StartAsync(connectionString);

        var userIds = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => api.GetMeAsync("AuthUser0000000000000000000001")));

        var user = Assert.Single(await ReadUsersAsync(api));
        Assert.All(userIds, userId => Assert.Equal(user.Id, userId));
    }

    [Fact]
    public async Task GetMe_RepeatedRequest_ReturnsSameUser()
    {
        await using var api = await ApiHost.StartAsync(connectionString);
        var firstUserId = await api.GetMeAsync("AuthUser0000000000000000000001");

        var secondUserId = await api.GetMeAsync("AuthUser0000000000000000000001");

        Assert.Equal(firstUserId, secondUserId);
        Assert.Single(await ReadUsersAsync(api));
    }

    [Fact]
    public async Task DeleteMe_ExistingUser_DeletesUserAndAuthUser()
    {
        const string authUserId = "AuthUser0000000000000000000001";
        await using var api = await ApiHost.StartAsync(connectionString);
        await api.GetMeAsync(authUserId);

        var statusCode = await DeleteMeAsync(api, TestTokens.Create(authUserId));

        Assert.Equal(HttpStatusCode.NoContent, statusCode);
        Assert.Empty(await ReadUsersAsync(api));
        Assert.Equal([authUserId, authUserId], (await ReadDeletedUsersAsync(api)).Select(deletedUser => deletedUser.AuthUserId));
        var request = Assert.Single(api.AuthService.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/service/delete-user", request.Path);
        Assert.Equal($"Bearer {ApiHost.AuthServiceKey}", request.Authorization);
        Assert.NotNull(request.Body);
        Assert.Equal(
            new Dictionary<string, string> { ["userId"] = authUserId },
            JsonSerializer.Deserialize<Dictionary<string, string>>(request.Body, JsonSerializerOptions.Web)
        );
    }

    [Fact]
    public async Task DeleteMe_WithOwnedSharedScopeAndForeignMembership_DeletesOwnScopesAndDetachesForeignEntry()
    {
        await using var api = await ApiHost.StartAsync(connectionString, testDataTypes: true);
        var now = api.TimeProvider.GetUtcNow();
        var deletedUser = await SyncTestDevice.SignInAsync(api);
        var otherUser = await SyncTestDevice.SignInAsync(api);
        var ownBoardId = Guid.NewGuid();
        var cardId = Guid.NewGuid();
        var foreignBoardId = Guid.NewGuid();
        await deletedUser.ApplyAllAsync(deletedUser.CreateBoard(ownBoardId, now.AddSeconds(-30), "board"));
        await deletedUser.ApplyAllAsync(deletedUser.CreateCard(ownBoardId, cardId, now.AddSeconds(-20), "milk"));
        await otherUser.ApplyAllAsync(otherUser.CreateBoard(foreignBoardId, now.AddSeconds(-30), "board"));
        var otherOwnerEntryId = Assert.Single(await SharedScopeQueries.ReadMemberEntriesAsync(api, foreignBoardId)).Id;
        var administration = new DatabaseAdministration(connectionString);
        await administration.InsertMemberEntryAsync(ownBoardId, otherUser.AccountId, now.AddMilliseconds(1));
        var deletedUserEntryId = await administration.InsertMemberEntryAsync(foreignBoardId, deletedUser.AccountId, now.AddMilliseconds(1));

        var statusCode = await DeleteMeAsync(api, TestTokens.Create(deletedUser.AuthUserId));

        Assert.Equal(HttpStatusCode.NoContent, statusCode);
        Assert.Equal(otherUser.AccountId, Assert.Single(await ReadUsersAsync(api)).Id);
        Assert.False(
            await api.QueryDatabaseAsync(context =>
                context
                    .Set<SyncScope>()
                    .AnyAsync(scope => scope.OwnerAccountId == deletedUser.AccountId, TestContext.Current.CancellationToken)
            )
        );
        Assert.Empty(await SharedScopeQueries.ReadMemberEntriesAsync(api, ownBoardId));
        Assert.Null(await SharedScopeQueries.ReadCardAsync(api, cardId));
        Assert.NotNull(await SharedScopeQueries.ReadScopeAsync(api, foreignBoardId));
        Assert.Equal(
            new (Guid, Guid?, string, SyncMemberState)[]
            {
                (otherOwnerEntryId, otherUser.AccountId, "owner", SyncMemberState.Active),
                (deletedUserEntryId, null, "member", SyncMemberState.Active),
            },
            (await SharedScopeQueries.ReadMemberEntriesAsync(api, foreignBoardId)).Select(entry =>
                (entry.Id, entry.AccountId, entry.Role, entry.State)
            )
        );
        var foreignBlock = Assert.Single((await otherUser.SyncAllAsync()).Scopes, block => block.ScopeId == foreignBoardId);
        Assert.Equal(
            new[]
            {
                new SyncMember(otherOwnerEntryId, "", "owner", "active", true),
                new SyncMember(deletedUserEntryId, "", "member", "active", false),
            },
            foreignBlock.Members
        );
    }

    [Fact]
    public async Task DeleteMe_AuthServiceFails_AllowsRetryWithSameToken()
    {
        const string authUserId = "AuthUser0000000000000000000001";
        await using var api = await ApiHost.StartAsync(connectionString);
        await api.GetMeAsync(authUserId);
        var token = TestTokens.Create(authUserId);
        api.AuthService.ResponseStatusCode = HttpStatusCode.InternalServerError;
        Assert.Equal(HttpStatusCode.BadGateway, await DeleteMeAsync(api, token));
        api.AuthService.ResponseStatusCode = HttpStatusCode.NoContent;

        var statusCode = await DeleteMeAsync(api, token);

        Assert.Equal(HttpStatusCode.NoContent, statusCode);
        Assert.Equal(2, api.AuthService.Requests.Count);
        Assert.Empty(await ReadUsersAsync(api));
    }

    [Theory]
    [MemberData(nameof(AuthServiceTransportFailures))]
    public async Task DeleteMe_AuthServiceDoesNotAnswer_AllowsRetryWithSameToken(Exception transportFailure)
    {
        const string authUserId = "AuthUser0000000000000000000001";
        await using var api = await ApiHost.StartAsync(connectionString);
        await api.GetMeAsync(authUserId);
        var token = TestTokens.Create(authUserId);
        api.AuthService.TransportFailure = transportFailure;
        Assert.Equal(HttpStatusCode.BadGateway, await DeleteMeAsync(api, token));
        api.AuthService.TransportFailure = null;

        var statusCode = await DeleteMeAsync(api, token);

        Assert.Equal(HttpStatusCode.NoContent, statusCode);
        Assert.Equal(2, api.AuthService.Requests.Count);
        Assert.Empty(await ReadUsersAsync(api));
    }

    [Theory]
    [InlineData(-1, HttpStatusCode.Unauthorized, 0)]
    [InlineData(1, HttpStatusCode.OK, 1)]
    public async Task GetMe_AfterDeletion_DependsOnIssuedAt(
        int issuedMinutesAfterDeletion,
        HttpStatusCode expectedStatusCode,
        int expectedUserCount
    )
    {
        const string authUserId = "AuthUser0000000000000000000001";
        await using var api = await ApiHost.StartAsync(connectionString);
        await api.GetMeAsync(authUserId);
        var deletedAt = api.TimeProvider.GetUtcNow();
        await DeleteMeAsync(api, TestTokens.Create(authUserId));
        api.TimeProvider.Advance(TimeSpan.FromMinutes(1));
        var token = TestTokens.Create(authUserId, issuedAt: deletedAt.AddMinutes(issuedMinutesAfterDeletion));

        using var response = await SendGetMeAsync(api, token);

        Assert.Equal(expectedStatusCode, response.StatusCode);
        Assert.Equal(expectedUserCount, (await ReadUsersAsync(api)).Length);
    }

    [Fact]
    public async Task DeleteMe_OldDeletionRecords_AreRemoved()
    {
        await using var api = await ApiHost.StartAsync(connectionString);
        await DeleteMeAsync(api, TestTokens.Create("AuthUser0000000000000000000001"));
        api.TimeProvider.Advance(TimeSpan.FromHours(25));

        await DeleteMeAsync(api, TestTokens.Create("AuthUser0000000000000000000002"));

        Assert.Equal(
            ["AuthUser0000000000000000000002", "AuthUser0000000000000000000002"],
            (await ReadDeletedUsersAsync(api)).Select(deletedUser => deletedUser.AuthUserId)
        );
    }

    [Fact]
    public async Task DeleteMe_WithSyncData_LeavesNoSyncDataOrBookkeeping()
    {
        await using var api = await ApiHost.StartAsync(connectionString, testDataTypes: true);
        var now = api.TimeProvider.GetUtcNow();
        var device = await SyncTestDevice.SignInAsync(api);
        var otherUser = await SyncTestDevice.SignInAsync(api);
        var boardId = Guid.NewGuid();
        var deletedBoardId = Guid.NewGuid();
        var deletedNoteId = Guid.NewGuid();
        var deletedCardId = Guid.NewGuid();
        var otherNoteId = Guid.NewGuid();
        await device.ApplyAllAsync(
            device.CreateNote(Guid.NewGuid(), now, "note"),
            device.CreateNote(deletedNoteId, now, "note"),
            device.CreateBoard(boardId, now, "board"),
            device.CreateCard(boardId, Guid.NewGuid(), now, "card"),
            device.CreateCard(boardId, deletedCardId, now, "card"),
            device.CreateBoard(deletedBoardId, now, "board")
        );
        await device.ApplyAllAsync(
            device.DeleteNote(deletedNoteId, now),
            device.DeleteCard(boardId, deletedCardId, now),
            device.DeleteBoard(deletedBoardId, now)
        );
        await otherUser.ApplyAllAsync(otherUser.CreateNote(otherNoteId, now, "note"));
        Guid[] scopeIds = [device.PersonalScopeId, boardId];

        var statusCode = await DeleteMeAsync(api, TestTokens.Create(device.AuthUserId));

        Assert.Equal(HttpStatusCode.NoContent, statusCode);
        Assert.Equal(otherUser.AccountId, Assert.Single(await ReadUsersAsync(api)).Id);
        var cancellationToken = TestContext.Current.CancellationToken;
        Assert.Equal(
            (0, 0, 0, 0, 0, 0, 0),
            await api.QueryDatabaseAsync(async context =>
                (
                    await context.Set<SyncScope>().CountAsync(scope => scope.OwnerAccountId == device.AccountId || scopeIds.Contains(scope.Id), cancellationToken),
                    await context
                        .Set<SyncMemberEntry>()
                        .CountAsync(entry => entry.AccountId == device.AccountId || scopeIds.Contains(entry.ScopeId), cancellationToken),
                    await context.Set<SyncTombstone>().CountAsync(tombstone => scopeIds.Contains(tombstone.ScopeId), cancellationToken),
                    await context.Set<ProcessedSyncCommand>().CountAsync(command => command.AccountId == device.AccountId, cancellationToken),
                    await context.Set<TestNote>().CountAsync(note => scopeIds.Contains(note.ScopeId), cancellationToken),
                    await context.Set<TestBoard>().CountAsync(board => scopeIds.Contains(board.ScopeId), cancellationToken),
                    await context.Set<TestCard>().CountAsync(card => scopeIds.Contains(card.ScopeId), cancellationToken)
                )
            )
        );
        Assert.Equal("note", await TestNoteQueries.ReadTitleAsync(api, otherNoteId));
        Assert.NotEmpty(await TestNoteQueries.ReadProcessedCommandsAsync(api, otherUser.AccountId));
    }

    [Fact]
    public async Task DeleteMe_AuthServiceFails_KeepsAccountMarkedWithoutOwnSharedScopes()
    {
        await using var api = await ApiHost.StartAsync(connectionString, testDataTypes: true);
        var now = api.TimeProvider.GetUtcNow();
        var device = await SyncTestDevice.SignInAsync(api);
        var boardId = Guid.NewGuid();
        var noteId = Guid.NewGuid();
        await device.ApplyAllAsync(device.CreateBoard(boardId, now, "board"), device.CreateNote(noteId, now, "note"));
        api.AuthService.ResponseStatusCode = HttpStatusCode.ServiceUnavailable;

        var statusCode = await DeleteMeAsync(api, TestTokens.Create(device.AuthUserId));

        Assert.Equal(HttpStatusCode.BadGateway, statusCode);
        var user = Assert.Single(await ReadUsersAsync(api));
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(now.ToUnixTimeMilliseconds()), user.DeletionStartedAt);
        Assert.Null(await SharedScopeQueries.ReadScopeAsync(api, boardId));
        Assert.NotNull(await SharedScopeQueries.ReadScopeAsync(api, device.PersonalScopeId));
        Assert.Equal("note", await TestNoteQueries.ReadTitleAsync(api, noteId));
        Assert.Single(await ReadDeletedUsersAsync(api));
        Assert.Single(api.AuthService.Requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeleteMe_SecondDeviceSyncs_GetsAccountGoneAndNoAccountIsCreated(bool authServiceFails)
    {
        await using var api = await ApiHost.StartAsync(connectionString, testDataTypes: true);
        var device = await SyncTestDevice.SignInAsync(api);
        var secondDevice = device.OtherDevice();
        await secondDevice.SyncAllAsync();
        var boardId = Guid.NewGuid();
        var create = secondDevice.CreateBoard(boardId, api.TimeProvider.GetUtcNow(), "board");
        if (authServiceFails)
        {
            api.AuthService.ResponseStatusCode = HttpStatusCode.ServiceUnavailable;
        }

        Assert.Equal(
            authServiceFails ? HttpStatusCode.BadGateway : HttpStatusCode.NoContent,
            await DeleteMeAsync(api, TestTokens.Create(device.AuthUserId))
        );

        using var response = await secondDevice.SendAsync(
            JsonContent.Create(secondDevice.CreateBody(SyncTestDevice.AllDataTypes, [create])),
            secondDevice.DeviceId.ToString()
        );

        await SyncTestDevice.AssertProblemAsync(response, HttpStatusCode.Gone, TestProblemTypes.AccountGone);
        Assert.Equal(authServiceFails ? [device.AccountId] : [], (await ReadUsersAsync(api)).Select(user => user.Id));
        Assert.Null(await SharedScopeQueries.ReadScopeAsync(api, boardId));
    }

    [Fact]
    public async Task DeleteMe_RootCreateDuringDeletion_CreatesNoScope()
    {
        await using var api = await ApiHost.StartAsync(
            connectionString,
            new Dictionary<string, string?> { ["Sync:LockTimeout"] = "00:00:30" },
            testDataTypes: true
        );
        var now = api.TimeProvider.GetUtcNow();
        var device = await SyncTestDevice.SignInAsync(api);
        var boardId = Guid.NewGuid();
        await device.ApplyAllAsync(device.CreateBoard(boardId, now, "board"));
        var newBoardId = Guid.NewGuid();
        var create = device.CreateBoard(newBoardId, now, "new board");
        var administration = new DatabaseAdministration(connectionString);
        Task<HttpStatusCode> deletion;
        await using (await administration.LockScopeRowAsync(boardId))
        {
            Task<HttpResponseMessage> rootCreate;
            await using (await administration.LockProcessedCommandsTableAsync())
            {
                rootCreate = device.SendAsync(
                    JsonContent.Create(device.CreateBody(SyncTestDevice.AllDataTypes, [create])),
                    device.DeviceId.ToString()
                );
                await administration.WaitForLockWaitersAsync(1);
                deletion = DeleteMeAsync(api, TestTokens.Create(device.AuthUserId));
                await administration.WaitForLockWaitersAsync(2);
            }

            using var response = await rootCreate;

            await SyncTestDevice.AssertProblemAsync(response, HttpStatusCode.Gone, TestProblemTypes.AccountGone);
            Assert.Null(await SharedScopeQueries.ReadScopeAsync(api, newBoardId));
            Assert.DoesNotContain(
                SyncTestCommands.GetId(create),
                (await TestNoteQueries.ReadProcessedCommandsAsync(api, device.AccountId)).Select(command => command.CommandId)
            );
            Assert.False(deletion.IsCompleted);
        }

        Assert.Equal(HttpStatusCode.NoContent, await deletion);
        Assert.Empty(await ReadUsersAsync(api));
        Assert.Null(await SharedScopeQueries.ReadScopeAsync(api, boardId));
    }

    [Fact]
    public async Task DeleteMe_RootCreateHoldingPersonalLock_DeletesItsScopeBeforeAuth()
    {
        await using var api = await ApiHost.StartAsync(
            connectionString,
            new Dictionary<string, string?> { ["Sync:LockTimeout"] = "00:00:30" },
            testDataTypes: true
        );
        var device = await SyncTestDevice.SignInAsync(api);
        var boardId = Guid.NewGuid();
        var create = device.CreateBoard(boardId, api.TimeProvider.GetUtcNow(), "board");
        api.AuthService.ResponseStatusCode = HttpStatusCode.ServiceUnavailable;
        var administration = new DatabaseAdministration(connectionString);
        Task<HttpResponseMessage> rootCreate;
        Task<HttpStatusCode> deletion;
        await using (await administration.LockScopesTableAsync())
        {
            rootCreate = device.SendAsync(
                JsonContent.Create(device.CreateBody(SyncTestDevice.AllDataTypes, [create])),
                device.DeviceId.ToString()
            );
            await administration.WaitForLockWaitersAsync(1);
            deletion = DeleteMeAsync(api, TestTokens.Create(device.AuthUserId));
            await administration.WaitForLockWaitersAsync(2);
        }

        using var response = await rootCreate;

        Assert.Equal(HttpStatusCode.BadGateway, await deletion);
        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Gone });
        Assert.Equal(
            [SyncResultStatus.Applied],
            (await TestNoteQueries.ReadProcessedCommandsAsync(api, device.AccountId))
                .Where(command => command.CommandId == SyncTestCommands.GetId(create))
                .Select(command => command.Status)
        );
        Assert.NotNull(Assert.Single(await ReadUsersAsync(api)).DeletionStartedAt);
        Assert.Null(await SharedScopeQueries.ReadScopeAsync(api, boardId));
    }

    [Fact]
    public async Task DeleteMe_RootCreateQueuedBehindMarker_CreatesNoScope()
    {
        await using var api = await ApiHost.StartAsync(
            connectionString,
            new Dictionary<string, string?> { ["Sync:LockTimeout"] = "00:00:30" },
            testDataTypes: true
        );
        var device = await SyncTestDevice.SignInAsync(api);
        var boardId = Guid.NewGuid();
        var create = device.CreateBoard(boardId, api.TimeProvider.GetUtcNow(), "board");
        api.AuthService.ResponseStatusCode = HttpStatusCode.ServiceUnavailable;
        var administration = new DatabaseAdministration(connectionString);
        Task<HttpStatusCode> deletion;
        Task<HttpResponseMessage> rootCreate;
        await using (await administration.BeginLastRequestUpdateAsync(device.AccountId, new DateOnly(2026, 1, 1)))
        {
            deletion = DeleteMeAsync(api, TestTokens.Create(device.AuthUserId));
            await administration.WaitForLockWaitersAsync(1);
            rootCreate = device.SendAsync(
                JsonContent.Create(device.CreateBody(SyncTestDevice.AllDataTypes, [create])),
                device.DeviceId.ToString()
            );
            await administration.WaitForLockWaitersAsync(2);
        }

        using var response = await rootCreate;

        await SyncTestDevice.AssertProblemAsync(response, HttpStatusCode.Gone, TestProblemTypes.AccountGone);
        Assert.Equal(HttpStatusCode.BadGateway, await deletion);
        Assert.NotNull(Assert.Single(await ReadUsersAsync(api)).DeletionStartedAt);
        Assert.Null(await SharedScopeQueries.ReadScopeAsync(api, boardId));
        Assert.DoesNotContain(
            SyncTestCommands.GetId(create),
            (await TestNoteQueries.ReadProcessedCommandsAsync(api, device.AccountId)).Select(command => command.CommandId)
        );
    }

    [Fact]
    public async Task GetMe_MarkedAccount_CompletesDeletionAndReturnsUnauthorized()
    {
        await using var api = await ApiHost.StartAsync(
            connectionString,
            new Dictionary<string, string?> { ["Sync:LockTimeout"] = "00:00:01", ["Database:MaxRetryDelay"] = "00:00:00.100" },
            testDataTypes: true
        );
        var (device, boardId) = await CreateMarkedAccountWithOwnedBoardAsync(api);
        api.TimeProvider.Advance(TimeSpan.FromMinutes(1));

        using var response = await SendGetMeAsync(api, TestTokens.Create(device.AuthUserId, issuedAt: api.TimeProvider.GetUtcNow()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(await ReadUsersAsync(api));
        Assert.Null(await SharedScopeQueries.ReadScopeAsync(api, boardId));
        Assert.Null(await SharedScopeQueries.ReadScopeAsync(api, device.PersonalScopeId));
        Assert.Equal(
            0,
            await api.QueryDatabaseAsync(context =>
                context
                    .Set<ProcessedSyncCommand>()
                    .CountAsync(command => command.AccountId == device.AccountId, TestContext.Current.CancellationToken)
            )
        );
        Assert.Single(api.AuthService.Requests);
        Assert.Equal(
            api.TimeProvider.GetUtcNow(),
            (await ReadDeletedUsersAsync(api)).Max(deletedUser => deletedUser.DeletedAt)
        );
    }

    [Fact]
    public async Task GetMe_MarkedAccountAuthServiceFails_ReturnsUnauthorizedAndKeepsAccountMarked()
    {
        await using var api = await ApiHost.StartAsync(
            connectionString,
            new Dictionary<string, string?> { ["Sync:LockTimeout"] = "00:00:01", ["Database:MaxRetryDelay"] = "00:00:00.100" },
            testDataTypes: true
        );
        api.AuthService.ResponseStatusCode = HttpStatusCode.ServiceUnavailable;
        var (device, boardId) = await CreateMarkedAccountWithOwnedBoardAsync(api);
        Assert.NotNull(Assert.Single(await ReadUsersAsync(api)).DeletionStartedAt);
        Assert.NotNull(await SharedScopeQueries.ReadScopeAsync(api, boardId));
        Assert.Empty(api.AuthService.Requests);
        api.TimeProvider.Advance(TimeSpan.FromMinutes(1));

        using var response = await SendGetMeAsync(api, TestTokens.Create(device.AuthUserId, issuedAt: api.TimeProvider.GetUtcNow()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotNull(Assert.Single(await ReadUsersAsync(api)).DeletionStartedAt);
        Assert.Null(await SharedScopeQueries.ReadScopeAsync(api, boardId));
        Assert.Single(api.AuthService.Requests);
    }

    [Fact]
    public async Task GetMe_TokenIssuedWhileMarked_AfterCompletion_ReturnsUnauthorizedAndCreatesNoAccount()
    {
        const string authUserId = "AuthUser0000000000000000000001";
        await using var api = await ApiHost.StartAsync(connectionString);
        await api.GetMeAsync(authUserId);
        api.AuthService.ResponseStatusCode = HttpStatusCode.ServiceUnavailable;
        Assert.Equal(HttpStatusCode.BadGateway, await DeleteMeAsync(api, TestTokens.Create(authUserId)));
        api.AuthService.ResponseStatusCode = HttpStatusCode.NoContent;
        api.TimeProvider.Advance(TimeSpan.FromMinutes(1));
        var tokenIssuedWhileMarked = TestTokens.Create(authUserId, issuedAt: api.TimeProvider.GetUtcNow());
        api.TimeProvider.Advance(TimeSpan.FromMinutes(1));
        using (var completion = await SendGetMeAsync(api, tokenIssuedWhileMarked))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, completion.StatusCode);
        }

        api.TimeProvider.Advance(TimeSpan.FromMinutes(1));

        using var response = await SendGetMeAsync(api, tokenIssuedWhileMarked);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(await ReadUsersAsync(api));
    }

    [Fact]
    public async Task UserRowDeletion_OwnedSharedScopeWithMemberEntries_CascadesWithoutError()
    {
        await using var api = await ApiHost.StartAsync(connectionString, testDataTypes: true);
        var now = api.TimeProvider.GetUtcNow();
        var deletedUser = await SyncTestDevice.SignInAsync(api);
        var otherUser = await SyncTestDevice.SignInAsync(api);
        var ownBoardId = Guid.NewGuid();
        var cardId = Guid.NewGuid();
        await deletedUser.ApplyAllAsync(deletedUser.CreateBoard(ownBoardId, now, "board"), deletedUser.CreateCard(ownBoardId, cardId, now, "milk"));
        var administration = new DatabaseAdministration(connectionString);
        await administration.InsertMemberEntryAsync(ownBoardId, otherUser.AccountId, now.AddMilliseconds(1));

        await using (var deletion = await administration.BeginUserDeletionAsync(deletedUser.AccountId))
        {
            await deletion.CommitAsync();
        }

        Assert.Equal(otherUser.AccountId, Assert.Single(await ReadUsersAsync(api)).Id);
        Assert.Null(await SharedScopeQueries.ReadScopeAsync(api, ownBoardId));
        Assert.Empty(await SharedScopeQueries.ReadMemberEntriesAsync(api, ownBoardId));
        Assert.Null(await SharedScopeQueries.ReadCardAsync(api, cardId));
    }

    private async Task<(SyncTestDevice Device, Guid BoardId)> CreateMarkedAccountWithOwnedBoardAsync(ApiHost api)
    {
        var device = await SyncTestDevice.SignInAsync(api);
        var boardId = Guid.NewGuid();
        await device.ApplyAllAsync(device.CreateBoard(boardId, api.TimeProvider.GetUtcNow(), "board"));
        await using (await new DatabaseAdministration(connectionString).LockScopeRowAsync(boardId))
        {
            Assert.Equal(HttpStatusCode.InternalServerError, await DeleteMeAsync(api, TestTokens.Create(device.AuthUserId)));
        }

        return (device, boardId);
    }

    private static Task<HttpResponseMessage> SendGetMeAsync(ApiHost api, string token) => SendAsync(api, HttpMethod.Get, token);

    private static async Task<HttpStatusCode> DeleteMeAsync(ApiHost api, string token)
    {
        using var response = await SendAsync(api, HttpMethod.Delete, token);
        return response.StatusCode;
    }

    private static async Task<HttpResponseMessage> SendAsync(ApiHost api, HttpMethod method, string token)
    {
        using var request = new HttpRequestMessage(method, "/v1/users/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await api.HttpClient.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static Task<User[]> ReadUsersAsync(ApiHost api) =>
        api.QueryDatabaseAsync(context => context.Users.AsNoTracking().ToArrayAsync(TestContext.Current.CancellationToken));

    private static Task<DeletedUser[]> ReadDeletedUsersAsync(ApiHost api) =>
        api.QueryDatabaseAsync(context => context.DeletedUsers.AsNoTracking().ToArrayAsync(TestContext.Current.CancellationToken));
}
