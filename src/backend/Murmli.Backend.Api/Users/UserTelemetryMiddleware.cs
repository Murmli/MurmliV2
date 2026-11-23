using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
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
        AccountFeature? account;
        try
        {
            account = await ResolveAccountAsync(context);
        }
        catch (Exception exception) when (exception.IsDatabaseDisturbance())
        {
            LogDatabaseUnavailable(exception);
            await new ServiceUnavailableProblem(databaseOptions.Value.UnavailableRetryAfter).ExecuteAsync(context);
            return;
        }

        if (account is null)
        {
            await next(context);
            return;
        }

        context.Features.Set(account);
        context.Features.Get<IHttpActivityFeature>()?.Activity.SetTag(LogProperties.UserId, account.AccountId.ToString());
        using (logger.BeginScope(new Dictionary<string, object> { [LogProperties.UserId] = account.AccountId }))
        {
            await next(context);
        }
    }

    // A client disconnecting during the lookup must not drop a started account deletion
    private static async Task<AccountFeature?> ResolveAccountAsync(HttpContext context)
    {
        if (context.User.FindAuthUserId() is not { } authUserId)
        {
            return null;
        }

        var account = await context
            .RequestServices.GetRequiredService<MurmliDbContext>()
            .Users.Where(user => user.AuthUserId == authUserId)
            .Select(user => new { user.Id, user.DeletionStartedAt })
            .SingleOrDefaultAsync(CancellationToken.None);
        return account is null ? null : new AccountFeature(account.Id, account.DeletionStartedAt is not null);
    }
}
