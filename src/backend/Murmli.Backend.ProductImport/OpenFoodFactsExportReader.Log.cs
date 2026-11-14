namespace Murmli.Backend.ProductImport;

public sealed partial class OpenFoodFactsExportReader
{
    [LoggerMessage(
        EventId = LogEvents.UnknownSchemaVersion,
        EventName = nameof(LogEvents.UnknownSchemaVersion),
        Level = LogLevel.Warning,
        Message = "The Open Food Facts export uses the unknown schema version {"
            + LogProperties.SchemaVersion
            + "}, nutrient values may be missing"
    )]
    private partial void LogUnknownSchemaVersion(int schemaVersion);
}
