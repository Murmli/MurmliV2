namespace Murmli.Backend.Api.Tests.Infrastructure;

internal sealed record AuthServiceRequest(HttpMethod Method, string Path, string? Authorization, string? Body);
