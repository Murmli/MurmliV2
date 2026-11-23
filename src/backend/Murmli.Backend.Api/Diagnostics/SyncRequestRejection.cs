namespace Murmli.Backend.Api.Diagnostics;

internal static class SyncRequestRejection
{
    public const string AccountGone = "account_gone";

    public const string InvalidRequest = "invalid_request";

    public const string RequestTooLarge = "request_too_large";

    public const string RateLimitedDevice = "rate_limited_device";

    public const string RateLimitedAccount = "rate_limited_account";

    public const string RateLimitedCommands = "rate_limited_commands";

    public const string ServiceUnavailable = "service_unavailable";
}
