using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Murmli.Backend.Data;

namespace Murmli.Backend.Api.Products;

internal sealed class ProductSearch(MurmliDbContext context)
{
    public static bool IsSearchable([NotNullWhen(true)] string? term) =>
        term is not null && SplitIntoWords(term).Any(word => word.Length >= 3);

    public async Task<IReadOnlyList<ProductResponse>> SearchAsync(string term, CancellationToken cancellationToken = default)
    {
        var words = SplitIntoWords(term);
        var codes = GetCodeCandidates(term.Trim());

        var codeMatches = context.Products.Where(product => codes.Contains(product.Code));

        var wordMatches = context.Products.AsQueryable();
        foreach (var word in words)
        {
            var pattern = "%" + EscapeLikePattern(word) + "%";
            wordMatches = wordMatches.Where(product =>
                EF.Functions.ILike(product.Name, pattern, "\\") || EF.Functions.ILike(product.Brand!, pattern, "\\")
            );
        }

        var namePrefixPattern = EscapeLikePattern(string.Join(' ', words)) + "%";

        return await codeMatches
            .Union(wordMatches)
            .OrderBy(product => codes.Contains(product.Code) ? 0 : 1)
            .ThenBy(product => product.EnergyKilocalories == null ? 1 : 0)
            .ThenByDescending(product =>
                (product.EnergyKilojoules == null ? 0 : 1)
                + (product.EnergyKilocalories == null ? 0 : 1)
                + (product.Fat == null ? 0 : 1)
                + (product.SaturatedFat == null ? 0 : 1)
                + (product.Carbohydrates == null ? 0 : 1)
                + (product.Sugars == null ? 0 : 1)
                + (product.AddedSugars == null ? 0 : 1)
                + (product.Proteins == null ? 0 : 1)
            )
            .ThenBy(product => EF.Functions.ILike(product.Name, namePrefixPattern, "\\") ? 0 : 1)
            .ThenByDescending(product => product.PopularityKey ?? 0)
            .ThenBy(product => product.Name)
            .ThenBy(product => product.Code)
            .Take(25)
            .Select(product => new ProductResponse(
                product.Code,
                product.Name,
                product.Brand,
                product.GenericName,
                product.IsPerHundredMilliliters,
                ToNutrientResponse(product.EnergyKilojoules, product.EnergyKilojoulesIsEstimated),
                ToNutrientResponse(product.EnergyKilocalories, product.EnergyKilocaloriesIsEstimated),
                ToNutrientResponse(product.Fat, product.FatIsEstimated),
                ToNutrientResponse(product.SaturatedFat, product.SaturatedFatIsEstimated),
                ToNutrientResponse(product.Carbohydrates, product.CarbohydratesIsEstimated),
                ToNutrientResponse(product.Sugars, product.SugarsIsEstimated),
                ToNutrientResponse(product.AddedSugars, product.AddedSugarsIsEstimated),
                ToNutrientResponse(product.Proteins, product.ProteinsIsEstimated)
            ))
            .ToArrayAsync(cancellationToken);
    }

    private static string[] SplitIntoWords(string term) => term.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    private static string[] GetCodeCandidates(string code) =>
        code.Length switch
        {
            12 when code.All(char.IsAsciiDigit) => [code, "0" + code],
            13 when code[0] == '0' && code.All(char.IsAsciiDigit) => [code, code[1..]],
            _ => [code],
        };

    private static string EscapeLikePattern(string text) => text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    private static NutrientResponse? ToNutrientResponse(double? value, bool isEstimated) =>
        value is null ? null : new NutrientResponse(value.Value, isEstimated);
}
