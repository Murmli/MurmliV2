using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Murmli.Backend.Api.Products;
using Murmli.Backend.Api.Tests.Infrastructure;
using Murmli.Backend.Data.Products;

namespace Murmli.Backend.Api.Tests.Products;

public class ProductEndpointsTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private readonly string connectionString = postgres.CreateDatabaseConnectionString();

    [Fact]
    public async Task Search_ExactCode_ReturnsProduct()
    {
        await using var api = await ApiHost.StartAsync(connectionString);
        await SeedAsync(
            api,
            new Product { Code = "4012345678901", Name = "Kefir" },
            new Product { Code = "4012345678902", Name = "Joghurt" }
        );

        var results = await SearchAsync(api, "4012345678901");

        Assert.Equal("4012345678901", Assert.Single(results).Code);
    }

    [Theory]
    [InlineData("401234")]
    [InlineData("2345678")]
    public async Task Search_PartOfCode_ReturnsEmptyList(string term)
    {
        await using var api = await ApiHost.StartAsync(connectionString);
        await SeedAsync(api, new Product { Code = "4012345678901", Name = "Kefir" });

        var results = await SearchAsync(api, term);

        Assert.Empty(results);
    }

    [Theory]
    [InlineData("0123456789012", "123456789012")]
    [InlineData("123456789012", "0123456789012")]
    public async Task Search_UpcCodeWithOrWithoutLeadingZero_ReturnsProduct(string storedCode, string term)
    {
        await using var api = await ApiHost.StartAsync(connectionString);
        await SeedAsync(api, new Product { Code = storedCode, Name = "Kefir" });

        var results = await SearchAsync(api, term);

        Assert.Equal(storedCode, Assert.Single(results).Code);
    }

    [Theory]
    [InlineData("kefir fettarmer")]
    [InlineData("FETTARMER KEFIR")]
    [InlineData("armer kef")]
    public async Task Search_WordsInAnyOrderAndCase_ReturnsProduct(string term)
    {
        await using var api = await ApiHost.StartAsync(connectionString);
        await SeedAsync(api, new Product { Code = "1", Name = "Fettarmer Kefir" }, new Product { Code = "2", Name = "Kefir" });

        var results = await SearchAsync(api, term);

        Assert.Equal("1", Assert.Single(results).Code);
    }

    [Fact]
    public async Task Search_WordsSplitAcrossNameAndBrand_ReturnsProduct()
    {
        await using var api = await ApiHost.StartAsync(connectionString);
        await SeedAsync(
            api,
            new Product { Code = "1", Name = "Fettarmer Kefir", Brand = "ja!" },
            new Product { Code = "2", Name = "Kefir", Brand = "Andechser" }
        );

        var results = await SearchAsync(api, "ja kefir");

        Assert.Equal("1", Assert.Single(results).Code);
    }

    [Fact]
    public async Task Search_ShortWordNotInProduct_ReturnsEmptyList()
    {
        await using var api = await ApiHost.StartAsync(connectionString);
        await SeedAsync(api, new Product { Code = "1", Name = "Kefir" });

        var results = await SearchAsync(api, "xy kefir");

        Assert.Empty(results);
    }

    [Theory]
    [InlineData("100%", "Saft 100%", "Saft 1000")]
    [InlineData("r_n", "Kefir_Natur", "Kefir Natur")]
    [InlineData("r\\n", "Kefir\\Natur", "Kefirnatur")]
    public async Task Search_LikeWildcardInTerm_MatchesLiterally(string term, string literalName, string decoyName)
    {
        await using var api = await ApiHost.StartAsync(connectionString);
        await SeedAsync(api, new Product { Code = "1", Name = literalName }, new Product { Code = "2", Name = decoyName });

        var results = await SearchAsync(api, term);

        Assert.Equal("1", Assert.Single(results).Code);
    }

    [Fact]
    public async Task Search_CodeAndNameMatches_OrdersCodeThenPrefixThenRestAlphabetically()
    {
        await using var api = await ApiHost.StartAsync(connectionString);
        await SeedAsync(
            api,
            new Product { Code = "1", Name = "3er Pack Seife 4711" },
            new Product { Code = "2", Name = "4711 Seife" },
            new Product { Code = "3", Name = "2er Pack Seife 4711" },
            new Product { Code = "4711", Name = "Zitronenwasser" },
            new Product { Code = "4", Name = "4711 Badesalz" }
        );

        var results = await SearchAsync(api, "4711");

        Assert.Equal(["4711", "4", "2", "3", "1"], results.Select(result => result.Code));
    }

    [Fact]
    public async Task Search_MoreThan25Matches_Returns25()
    {
        await using var api = await ApiHost.StartAsync(connectionString);
        await SeedAsync(
            api,
            [.. Enumerable.Range(0, 26).Select(index => new Product { Code = index.ToString(), Name = $"Kefir {(char)('A' + index)}" })]
        );

        var results = await SearchAsync(api, "kefir");

        Assert.Equal(25, results.Length);
        Assert.DoesNotContain(results, result => result.Name == "Kefir Z");
    }

    [Theory]
    [InlineData("ja")]
    [InlineData("ja ok")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task Search_NoWordWithThreeCharacters_ReturnsBadRequest(string? term)
    {
        await using var api = await ApiHost.StartAsync(connectionString);

        using var response = await SendSearchAsync(api, term);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(TestContext.Current.CancellationToken);
        Assert.Contains("search", Assert.IsType<HttpValidationProblemDetails>(problem).Errors.Keys);
    }

    [Fact]
    public async Task Search_NoMatch_ReturnsEmptyList()
    {
        await using var api = await ApiHost.StartAsync(connectionString);
        await SeedAsync(api, new Product { Code = "1", Name = "Kefir" });

        var results = await SearchAsync(api, "joghurt");

        Assert.Empty(results);
    }

    [Fact]
    public async Task Search_WithoutToken_ReturnsUnauthorized()
    {
        await using var api = await ApiHost.StartAsync(connectionString);

        using var response = await api.HttpClient.GetAsync("/v1/products?search=kefir", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Search_Product_ReturnsAllFields()
    {
        await using var api = await ApiHost.StartAsync(connectionString);
        await SeedAsync(
            api,
            new Product
            {
                Code = "4012345678901",
                Name = "Fettarmer Kefir",
                Brand = "ja!",
                IsPerHundredMilliliters = true,
                EnergyKilojoules = 207,
                EnergyKilocalories = 49,
                EnergyKilocaloriesIsEstimated = true,
                Fat = 1.5,
                SaturatedFat = 0.9,
                SaturatedFatIsEstimated = true,
                Carbohydrates = 4,
                Proteins = 3.4,
            }
        );

        var results = await SearchAsync(api, "kefir");

        var expectedProduct = new ProductResponse(
            "4012345678901",
            "Fettarmer Kefir",
            "ja!",
            true,
            new NutrientResponse(207, false),
            new NutrientResponse(49, true),
            new NutrientResponse(1.5, false),
            new NutrientResponse(0.9, true),
            new NutrientResponse(4, false),
            null,
            null,
            new NutrientResponse(3.4, false)
        );
        Assert.Equal(expectedProduct, Assert.Single(results));
    }

    private static Task SeedAsync(ApiHost api, params Product[] products) =>
        api.QueryDatabaseAsync(async context =>
        {
            context.Products.AddRange(products);
            return await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        });

    private static async Task<ProductResponse[]> SearchAsync(ApiHost api, string term)
    {
        using var response = await SendSearchAsync(api, term);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var products = await response.Content.ReadFromJsonAsync<ProductResponse[]>(TestContext.Current.CancellationToken);
        return Assert.IsType<ProductResponse[]>(products);
    }

    private static async Task<HttpResponseMessage> SendSearchAsync(ApiHost api, string? term)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            term is null ? "/v1/products" : "/v1/products?search=" + Uri.EscapeDataString(term)
        );
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create("100000000000000001"));
        return await api.HttpClient.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
