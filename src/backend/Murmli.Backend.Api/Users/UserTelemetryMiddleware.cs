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
    TimeProvider timeProvider,
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
    private async Task<AccountFeature?> ResolveAccountAsync(HttpContext context)
    {
        if (context.User.FindAuthUserId() is not { } authUserId)
        {
            return null;
        }

        var users = context.RequestServices.GetRequiredService<MurmliDbContext>().Users;
        var account = await users
            .Where(user => user.AuthUserId == authUserId)
            .Select(user => new
            {
                user.Id,
                user.DeletionStartedAt,
                user.LastRequestOn,
            })
            .SingleOrDefaultAsync(CancellationToken.None);
        if (account is null)
        {
            return null;
        }

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        if (account.LastRequestOn is null || account.LastRequestOn < today)
        {
            // The condition lets only the first of concurrent requests on a new day write
            await users
                .Where(user => user.Id == account.Id && (user.LastRequestOn == null || user.LastRequestOn < today))
                .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.LastRequestOn, today), CancellationToken.None);
        }

        return new AccountFeature(account.Id, account.DeletionStartedAt is not null);
    }
}
