using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Murmli.Backend.Data;
using Murmli.Backend.Data.Products;
using Npgsql;
using NpgsqlTypes;

namespace Murmli.Backend.ProductImport.Synchronization;

public sealed class ProductSynchronizer(MurmliDbContext context)
{
    private const string CreateImportTableSql = "CREATE TEMP TABLE product_import (LIKE products.products) ON COMMIT DROP";

    private const string CopySql = """
        COPY product_import (
            code,
            name,
            is_per_hundred_milliliters,
            energy_kilojoules,
            energy_kilojoules_is_estimated,
            energy_kilocalories,
            energy_kilocalories_is_estimated,
            fat,
            fat_is_estimated,
            saturated_fat,
            saturated_fat_is_estimated,
            carbohydrates,
            carbohydrates_is_estimated,
            sugars,
            sugars_is_estimated,
            added_sugars,
            added_sugars_is_estimated,
            proteins,
            proteins_is_estimated
        ) FROM STDIN (FORMAT BINARY)
        """;

    private const string AnalyzeImportTableSql = "ANALYZE product_import";

    private const string MergeSql = """
        WITH merged AS (
            MERGE INTO products.products AS target
            USING product_import AS source
            ON target.code = source.code
            WHEN MATCHED AND (
                target.name,
                target.is_per_hundred_milliliters,
                target.energy_kilojoules,
                target.energy_kilojoules_is_estimated,
                target.energy_kilocalories,
                target.energy_kilocalories_is_estimated,
                target.fat,
                target.fat_is_estimated,
                target.saturated_fat,
                target.saturated_fat_is_estimated,
                target.carbohydrates,
                target.carbohydrates_is_estimated,
                target.sugars,
                target.sugars_is_estimated,
                target.added_sugars,
                target.added_sugars_is_estimated,
                target.proteins,
                target.proteins_is_estimated
            ) IS DISTINCT FROM (
                source.name,
                source.is_per_hundred_milliliters,
                source.energy_kilojoules,
                source.energy_kilojoules_is_estimated,
                source.energy_kilocalories,
                source.energy_kilocalories_is_estimated,
                source.fat,
                source.fat_is_estimated,
                source.saturated_fat,
                source.saturated_fat_is_estimated,
                source.carbohydrates,
                source.carbohydrates_is_estimated,
                source.sugars,
                source.sugars_is_estimated,
                source.added_sugars,
                source.added_sugars_is_estimated,
                source.proteins,
                source.proteins_is_estimated
            ) THEN
                UPDATE SET
                    name = source.name,
                    is_per_hundred_milliliters = source.is_per_hundred_milliliters,
                    energy_kilojoules = source.energy_kilojoules,
                    energy_kilojoules_is_estimated = source.energy_kilojoules_is_estimated,
                    energy_kilocalories = source.energy_kilocalories,
                    energy_kilocalories_is_estimated = source.energy_kilocalories_is_estimated,
                    fat = source.fat,
                    fat_is_estimated = source.fat_is_estimated,
                    saturated_fat = source.saturated_fat,
                    saturated_fat_is_estimated = source.saturated_fat_is_estimated,
                    carbohydrates = source.carbohydrates,
                    carbohydrates_is_estimated = source.carbohydrates_is_estimated,
                    sugars = source.sugars,
                    sugars_is_estimated = source.sugars_is_estimated,
                    added_sugars = source.added_sugars,
                    added_sugars_is_estimated = source.added_sugars_is_estimated,
                    proteins = source.proteins,
                    proteins_is_estimated = source.proteins_is_estimated
            WHEN NOT MATCHED BY TARGET THEN
                INSERT (
                    code,
                    name,
                    is_per_hundred_milliliters,
                    energy_kilojoules,
                    energy_kilojoules_is_estimated,
                    energy_kilocalories,
                    energy_kilocalories_is_estimated,
                    fat,
                    fat_is_estimated,
                    saturated_fat,
                    saturated_fat_is_estimated,
                    carbohydrates,
                    carbohydrates_is_estimated,
                    sugars,
                    sugars_is_estimated,
                    added_sugars,
                    added_sugars_is_estimated,
                    proteins,
                    proteins_is_estimated
                ) VALUES (
                    source.code,
                    source.name,
                    source.is_per_hundred_milliliters,
                    source.energy_kilojoules,
                    source.energy_kilojoules_is_estimated,
                    source.energy_kilocalories,
                    source.energy_kilocalories_is_estimated,
                    source.fat,
                    source.fat_is_estimated,
                    source.saturated_fat,
                    source.saturated_fat_is_estimated,
                    source.carbohydrates,
                    source.carbohydrates_is_estimated,
                    source.sugars,
                    source.sugars_is_estimated,
                    source.added_sugars,
                    source.added_sugars_is_estimated,
                    source.proteins,
                    source.proteins_is_estimated
                )
            WHEN NOT MATCHED BY SOURCE AND @delete_missing THEN
                DELETE
            RETURNING merge_action() AS action
        )
        SELECT action, count(*) FROM merged GROUP BY action
        """;

    // The database runs on a burstable server, so a step may take minutes
    private static readonly TimeSpan StepTimeout = TimeSpan.FromMinutes(10);

    public async Task<ProductSyncResult> SynchronizeAsync(
        IReadOnlyCollection<Product> products,
        bool deleteMissing,
        CancellationToken cancellationToken = default
    )
    {
        if (context.Database.CurrentTransaction?.GetDbTransaction() is not NpgsqlTransaction transaction)
        {
            throw new InvalidOperationException("Synchronizing products requires an active transaction.");
        }

        var connection = (NpgsqlConnection)context.Database.GetDbConnection();

        await ExecuteAsync(connection, transaction, CreateImportTableSql, cancellationToken);
        await CopyAsync(connection, products, cancellationToken);
        await ExecuteAsync(connection, transaction, AnalyzeImportTableSql, cancellationToken);
        return await MergeAsync(connection, transaction, deleteMissing, cancellationToken);
    }

    private static async Task CopyAsync(
        NpgsqlConnection connection,
        IReadOnlyCollection<Product> products,
        CancellationToken cancellationToken
    )
    {
        await using var importer = await connection.BeginBinaryImportAsync(CopySql, cancellationToken);
        importer.Timeout = StepTimeout;

        foreach (var product in products)
        {
            await importer.StartRowAsync(cancellationToken);
            await importer.WriteAsync(product.Code, NpgsqlDbType.Text, cancellationToken);
            await importer.WriteAsync(product.Name, NpgsqlDbType.Text, cancellationToken);
            await importer.WriteAsync(product.IsPerHundredMilliliters, NpgsqlDbType.Boolean, cancellationToken);
            await importer.WriteAsync(product.EnergyKilojoules, NpgsqlDbType.Double, cancellationToken);
            await importer.WriteAsync(product.EnergyKilojoulesIsEstimated, NpgsqlDbType.Boolean, cancellationToken);
            await importer.WriteAsync(product.EnergyKilocalories, NpgsqlDbType.Double, cancellationToken);
            await importer.WriteAsync(product.EnergyKilocaloriesIsEstimated, NpgsqlDbType.Boolean, cancellationToken);
            await importer.WriteAsync(product.Fat, NpgsqlDbType.Double, cancellationToken);
            await importer.WriteAsync(product.FatIsEstimated, NpgsqlDbType.Boolean, cancellationToken);
            await importer.WriteAsync(product.SaturatedFat, NpgsqlDbType.Double, cancellationToken);
            await importer.WriteAsync(product.SaturatedFatIsEstimated, NpgsqlDbType.Boolean, cancellationToken);
            await importer.WriteAsync(product.Carbohydrates, NpgsqlDbType.Double, cancellationToken);
            await importer.WriteAsync(product.CarbohydratesIsEstimated, NpgsqlDbType.Boolean, cancellationToken);
            await importer.WriteAsync(product.Sugars, NpgsqlDbType.Double, cancellationToken);
            await importer.WriteAsync(product.SugarsIsEstimated, NpgsqlDbType.Boolean, cancellationToken);
            await importer.WriteAsync(product.AddedSugars, NpgsqlDbType.Double, cancellationToken);
            await importer.WriteAsync(product.AddedSugarsIsEstimated, NpgsqlDbType.Boolean, cancellationToken);
            await importer.WriteAsync(product.Proteins, NpgsqlDbType.Double, cancellationToken);
            await importer.WriteAsync(product.ProteinsIsEstimated, NpgsqlDbType.Boolean, cancellationToken);
        }

        await importer.CompleteAsync(cancellationToken);
    }

    private static async Task<ProductSyncResult> MergeAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        bool deleteMissing,
        CancellationToken cancellationToken
    )
    {
        await using var command = CreateCommand(connection, transaction, MergeSql);
        command.Parameters.Add(new NpgsqlParameter<bool>("delete_missing", deleteMissing));

        var counts = new Dictionary<string, int>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            counts[reader.GetString(0)] = checked((int)reader.GetInt64(1));
        }

        return new ProductSyncResult(
            InsertedCount: counts.GetValueOrDefault("INSERT"),
            UpdatedCount: counts.GetValueOrDefault("UPDATE"),
            DeletedCount: counts.GetValueOrDefault("DELETE")
        );
    }

    private static async Task ExecuteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string commandText,
        CancellationToken cancellationToken
    )
    {
        await using var command = CreateCommand(connection, transaction, commandText);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static NpgsqlCommand CreateCommand(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string commandText
    ) => new(commandText, connection, transaction) { CommandTimeout = (int)StepTimeout.TotalSeconds };
}
