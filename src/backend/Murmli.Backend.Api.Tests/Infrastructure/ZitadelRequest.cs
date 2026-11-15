namespace Murmli.Backend.Api.Tests.Infrastructure;

internal sealed record ZitadelRequest(HttpMethod Method, string Path, string? Authorization);
