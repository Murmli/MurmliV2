using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Murmli.Backend.Api.Database;
using Murmli.Backend.Api.Diagnostics;
using Murmli.Backend.Api.Extensions;
using Murmli.Backend.Api.Problems;
using Murmli.Backend.Data;
using Murmli.Backend.Data.Extensions;

namespace Murmli.Backend.Api.Users;

internal sealed partial class UserTelemetryMiddleware(
    RequestDelegate next,
    IOptions<DatabaseOptions> databaseOptions,
    ILogger<UserTelemetryMiddleware> logger
)
{
    public async Task InvokeAsync(HttpContext context)
    {
        Guid? userId;
        try
        {
            userId = await FindUserIdAsync(context);
        }
        catch (Exception exception) when (exception.IsDatabaseDisturbance())
        {
            LogDatabaseUnavailable(exception);
            await new ServiceUnavailableProblem(databaseOptions.Value.UnavailableRetryAfter).ExecuteAsync(context);
            return;
        }

        if (userId is not { } existingUserId)
        {
            await next(context);
            return;
        }

        context.Features.Get<IHttpActivityFeature>()?.Activity.SetTag(LogProperties.UserId, existingUserId.ToString());
        using (logger.BeginScope(new Dictionary<string, object> { [LogProperties.UserId] = existingUserId }))
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
