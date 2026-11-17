using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Murmli.Backend.Api.Diagnostics;
using Murmli.Backend.Api.Extensions;
using Murmli.Backend.Data;

namespace Murmli.Backend.Api.Users;

internal sealed class UserTelemetryMiddleware(RequestDelegate next, ILogger<UserTelemetryMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (await FindUserIdAsync(context) is not { } userId)
        {
            await next(context);
            return;
        }

        context.Features.Get<IHttpActivityFeature>()?.Activity.SetTag(LogProperties.UserId, userId.ToString());
        using (logger.BeginScope(new Dictionary<string, object> { [LogProperties.UserId] = userId }))
        {
            await next(context);
        }
    }

    // A client disconnecting during the lookup must not drop a started account deletion
    private static async Task<Guid?> FindUserIdAsync(HttpContext context) =>
        context.User.FindAuthUserId() is { } authUserId
            ? await context.RequestServices.GetRequiredService<MurmliDbContext>().Users.FindIdAsync(authUserId, CancellationToken.None)
            : null;
}
