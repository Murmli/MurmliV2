using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Murmli.Backend.Api.Products;

internal static class ProductEndpoints
{
    public static async Task<Results<Ok<IReadOnlyList<ProductResponse>>, ValidationProblem>> SearchAsync(
        string? search,
        ProductSearch productSearch,
        CancellationToken cancellationToken
    )
    {
        if (!ProductSearch.IsSearchable(search))
        {
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]> { ["search"] = ["At least one word of the search term needs 3 characters."] }
            );
        }

        return TypedResults.Ok(await productSearch.SearchAsync(search, cancellationToken));
    }
}
