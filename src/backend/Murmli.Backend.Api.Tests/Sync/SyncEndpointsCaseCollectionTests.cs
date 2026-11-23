using System.Text.Json;
using System.Text.Json.Nodes;
using Murmli.Backend.Api.Tests.Infrastructure;
using Murmli.Backend.Api.Tests.Infrastructure.SyncCases;
using Murmli.Backend.Sync.Protocol;

namespace Murmli.Backend.Api.Tests.Sync;

public class SyncEndpointsCaseCollectionTests(SyncApiFixture fixture) : IClassFixture<SyncApiFixture>
{
    private const string PersonalScopeName = "$personal";

    private static readonly string CasesDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "SyncCases");

    private static readonly JsonSerializerOptions CaseSerializerOptions = new(JsonSerializerOptions.Strict)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static TheoryData<string> CaseFiles =>
        new(
            Directory
                .EnumerateFiles(Path.Combine(CasesDirectory, "core"), "*.json", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(CasesDirectory, path).Replace(Path.DirectorySeparatorChar, '/'))
                .Order(StringComparer.Ordinal)
        );

    [Theory]
    [MemberData(nameof(CaseFiles))]
    public async Task Sync_Case_EndsAsExpectedInBothOrders(string caseFile)
    {
        await using var stream = File.OpenRead(Path.Combine(CasesDirectory, caseFile));
        var syncCase = Assert.IsType<SyncCase>(
            await JsonSerializer.DeserializeAsync<SyncCase>(stream, CaseSerializerOptions, TestContext.Current.CancellationToken)
        );
        AssertStructure(syncCase, caseFile);

        await RunAsync(syncCase, syncCase.Race[0], syncCase.Race[1]);
        await RunAsync(syncCase, syncCase.Race[1], syncCase.Race[0]);
    }

    private static void AssertStructure(SyncCase syncCase, string caseFile)
    {
        var caseId = syncCase.Id;
        Assert.True(syncCase.Format == 1, $"{caseId}: the format {syncCase.Format} is not 1");
        Assert.True(caseId == Path.ChangeExtension(caseFile, null), $"{caseId}: the id does not match the file {caseFile}");
        var labels = syncCase.Race.Select(command => command.Label ?? "").ToArray();
        Assert.True(
            labels.Length == 2 && labels.All(label => label.Length > 0) && labels[0] != labels[1],
            $"{caseId}: the race needs exactly two commands with distinct non-empty labels"
        );
        Assert.True(syncCase.Given.All(command => command.Label is null), $"{caseId}: a given command has a label");
        Assert.True(
            !syncCase.Devices.Keys.Concat(syncCase.Devices.Values.Select(device => device.Account)).Concat(labels).Contains(PersonalScopeName),
            $"{caseId}: {PersonalScopeName} names a device, an account or a label"
        );
        foreach (var command in syncCase.Given.Concat(syncCase.Race))
        {
            Assert.True(syncCase.Devices.ContainsKey(command.Device), $"{caseId}: the device '{command.Device}' is not in devices");
            Assert.True(command.Record != PersonalScopeName, $"{caseId}: {PersonalScopeName} names the record of a command");
        }

        var overrides = syncCase.ExpectByOrder ?? new Dictionary<string, SyncCaseExpectationOverride>();
        string[] orders = [$"{labels[0]}>{labels[1]}", $"{labels[1]}>{labels[0]}"];
        foreach (var order in overrides.Keys)
        {
            Assert.True(orders.Contains(order), $"{caseId}: the expectByOrder key '{order}' is no order of the two labels");
        }

        var resultMaps = overrides.Values.Select(entry => entry.Results ?? syncCase.Expect.Results).Prepend(syncCase.Expect.Results);
        foreach (var results in resultMaps)
        {
            Assert.True(
                results.Count == 2 && labels.All(results.ContainsKey),
                $"{caseId}: a results map does not name exactly the labels '{labels[0]}' and '{labels[1]}'"
            );
        }

        var expectedRecords = overrides.Values.SelectMany(entry => entry.Records ?? []).Concat(syncCase.Expect.Records);
        var absentRecords = overrides.Values.SelectMany(entry => entry.Absent ?? []).Concat(syncCase.Expect.Absent);
        var references = expectedRecords.Select(record => new SyncCaseRecordReference(record.DataType, record.Record)).Concat(absentRecords);
        foreach (var reference in references)
        {
            Assert.True(
                syncCase.DataTypes.Contains(reference.DataType),
                $"{caseId}: the data type '{reference.DataType}' of the expected record '{reference.Record}' is not in dataTypes"
            );
            Assert.True(reference.Record != PersonalScopeName, $"{caseId}: {PersonalScopeName} names an expected record");
        }
    }

    private static bool HasFields(JsonElement fields, JsonObject expectedFields) =>
        expectedFields.All(field =>
            fields.TryGetProperty(field.Key, out var value) && JsonNode.DeepEquals(field.Value, JsonNode.Parse(value.GetRawText()))
        );

    private async Task RunAsync(SyncCase syncCase, SyncCaseCommand first, SyncCaseCommand second)
    {
        var order = $"{first.Label}>{second.Label}";
        var run = $"{syncCase.Id} ({order})";
        var api = fixture.Api;
        var subjects = new Dictionary<string, string>(StringComparer.Ordinal);
        var devices = new Dictionary<string, SyncTestDevice>(StringComparer.Ordinal);
        foreach (var (deviceName, caseDevice) in syncCase.Devices)
        {
            if (!subjects.TryGetValue(caseDevice.Account, out var subject))
            {
                subject = SyncTestDevice.CreateAuthUserId();
                subjects[caseDevice.Account] = subject;
            }

            var device = await SyncTestDevice.SignInAsync(api, subject, firstSync: false);
            await device.SyncAsync(syncCase.DataTypes, []);
            devices[deviceName] = device;
        }

        var ids = new Dictionary<string, Guid>(StringComparer.Ordinal);
        var start = api.TimeProvider.GetUtcNow();
        foreach (var command in syncCase.Given)
        {
            var result = await SendAsync(command);
            Assert.True(result.Status == "applied", $"{run}: the given {command.Kind} of '{command.Record}' was {result}");
        }

        var results = new Dictionary<string, SyncCommandResult>(StringComparer.Ordinal);
        foreach (var command in new[] { first, second })
        {
            results[command.Label!] = await SendAsync(command);
        }

        var expectation = syncCase.Expect;
        if (syncCase.ExpectByOrder?.GetValueOrDefault(order) is { } byOrder)
        {
            expectation = expectation with
            {
                Results = byOrder.Results ?? expectation.Results,
                Records = byOrder.Records ?? expectation.Records,
                Absent = byOrder.Absent ?? expectation.Absent,
            };
        }

        foreach (var (label, expectedResult) in expectation.Results)
        {
            var expected = new SyncCommandResult(null, expectedResult.Status, expectedResult.Reason, expectedResult.Cause, expectedResult.Original);
            var actual = results[label] with { CommandId = null };
            Assert.True(expected == actual, $"{run}: result of '{label}' was {actual}, expected {expected}");
        }

        var records = new List<SyncRecordEntry>();
        foreach (var subject in subjects.Values)
        {
            var device = await SyncTestDevice.SignInAsync(api, subject, firstSync: false);
            var response = await device.SyncAsync(syncCase.DataTypes, []);
            records.AddRange(response.Scopes.SelectMany(block => block.Records));
        }

        foreach (var expectedRecord in expectation.Records)
        {
            var found = FindRecords(expectedRecord.DataType, expectedRecord.Record);
            Assert.True(
                found.Any(record => HasFields(record.Fields, expectedRecord.Fields)),
                $"{run}: no {expectedRecord.DataType} '{expectedRecord.Record}' with {expectedRecord.Fields.ToJsonString()}, found [{string.Join(", ", found.Select(record => record.Fields.GetRawText()))}]"
            );
        }

        foreach (var absent in expectation.Absent)
        {
            Assert.True(FindRecords(absent.DataType, absent.Record).Count == 0, $"{run}: the {absent.DataType} '{absent.Record}' still exists");
        }

        Guid GetOrCreateId(string name)
        {
            if (!ids.TryGetValue(name, out var id))
            {
                id = Guid.NewGuid();
                ids[name] = id;
            }

            return id;
        }

        async Task<SyncCommandResult> SendAsync(SyncCaseCommand command)
        {
            var device = devices[command.Device];
            var scopeId = command.Scope == PersonalScopeName ? device.PersonalScopeId : GetOrCreateId(command.Scope);
            var response = await device.SyncAsync(
                syncCase.DataTypes,
                [
                    device.CreateCommand(
                        command.Kind,
                        command.DataType,
                        scopeId,
                        GetOrCreateId(command.Record),
                        start.AddMilliseconds(command.At),
                        command.Fields?.DeepClone().AsObject()
                    ),
                ]
            );
            Assert.True(response.Results.Count == 1, $"{run}: the {command.Kind} of '{command.Record}' got {response.Results.Count} results");
            return response.Results[0];
        }

        List<SyncRecordEntry> FindRecords(string dataType, string name)
        {
            Assert.True(ids.TryGetValue(name, out var recordId), $"{run}: no command names the record '{name}'");
            return [.. records.Where(record => record.DataType == dataType && record.Id == recordId)];
        }
    }
}
