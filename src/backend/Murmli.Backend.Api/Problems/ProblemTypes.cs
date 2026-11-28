namespace Murmli.Backend.Api.Problems;

internal static class ProblemTypes
{
    public const string AccountGone = "tag:murmli.de,2026:account-gone";

    public const string InvalidRequest = "tag:murmli.de,2026:invalid-request";

    public const string RateLimited = "tag:murmli.de,2026:rate-limited";

    public const string RequestTooLarge = "tag:murmli.de,2026:request-too-large";

    public const string ServiceUnavailable = "tag:murmli.de,2026:service-unavailable";
}
