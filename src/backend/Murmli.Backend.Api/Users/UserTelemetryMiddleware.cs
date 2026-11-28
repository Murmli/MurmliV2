using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Murmli.Backend.Api.Database;
using Murmli.Backend.Api.Diagnostics;
using Murmli.Backend.Api.Extensions;
using Murmli.Backend.Api.Problems;
using Murmli.Backend.Data;
using Murmli.Backend.Data.Extensions;
using Murmli.Backend.Data.Identity;

namespace Murmli.Backend.Api.Users;

internal sealed partial class UserTelemetryMiddleware(
    RequestDelegate next,
    IOptions<DatabaseOptions> databaseOptions,
    IProblemDetailsService problemDetailsService,
    TimeProvider timeProvider,
    ILogger<UserTelemetryMiddleware> logger
)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.FindAuthUserId() is not { } authUserId)
        {
            await next(context);
            return;
        }

        var users = context.RequestServices.GetRequiredService<MurmliDbContext>().Users;
        StoredAccount? account;
        try
        {
            account = await FindAccountAsync(users, authUserId);
        }
        catch (Exception exception)
        {
            await AnswerFailureAsync(context, exception);
            return;
        }

        if (account is null)
        {
            await next(context);
            return;
        }

        context.Features.Get<IHttpActivityFeature>()?.Activity.SetTag(LogProperties.UserId, account.Id.ToString());
        using (logger.BeginScope(new Dictionary<string, object> { [LogProperties.UserId] = account.Id }))
        {
            try
            {
                await RecordLastRequestAsync(users, account);
            }
            catch (Exception exception)
            {
                await AnswerFailureAsync(context, exception);
                return;
            }

            context.Features.Set(new AccountFeature(account.Id, account.DeletionStartedAt is not null));
            await next(context);
        }
    }

    // A client disconnecting during the lookup must not drop a started account deletion
    private static Task<StoredAccount?> FindAccountAsync(DbSet<User> users, string authUserId) =>
        users
            .Where(user => user.AuthUserId == authUserId)
            .Select(user => new StoredAccount(user.Id, user.DeletionStartedAt, user.LastRequestOn))
            .SingleOrDefaultAsync(CancellationToken.None);

    // A client disconnecting during the lookup must not drop a started account deletion
    private async Task RecordLastRequestAsync(DbSet<User> users, StoredAccount account)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        if (account.LastRequestOn is null || account.LastRequestOn < today)
        {
            // The condition lets only the first of concurrent requests on a new day write
            await users
                .Where(user => user.Id == account.Id && (user.LastRequestOn == null || user.LastRequestOn < today))
                .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.LastRequestOn, today), CancellationToken.None);
        }
    }

    private async Task AnswerFailureAsync(HttpContext context, Exception exception)
    {
        if (exception.IsDatabaseDisturbance())
        {
            LogDatabaseUnavailable(exception);
            await new ServiceUnavailableProblem(databaseOptions.Value.UnavailableRetryAfter).ExecuteAsync(context);
            return;
        }

        LogAccountResolutionFailed(exception);
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        // The answer of UseExceptionHandler, which runs only after this middleware
        await problemDetailsService.TryWriteAsync(
            new ProblemDetailsContext
            {
                HttpContext = context,
                ProblemDetails = { Status = StatusCodes.Status500InternalServerError },
                Exception = exception,
            }
        );
    }

    private sealed record StoredAccount(Guid Id, DateTimeOffset? DeletionStartedAt, DateOnly? LastRequestOn);
}
