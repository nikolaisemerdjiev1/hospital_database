using System.ComponentModel.DataAnnotations;

namespace Hospital.Api.Authentication;

internal sealed class Auth0Options
{
    public const string SectionName = "Authentication:Auth0";

    [Required]
    public string Domain { get; init; } = string.Empty;

    [Required]
    public string Audience { get; init; } = string.Empty;

    [Required]
    public string RoleClaim { get; init; } = string.Empty;

    public string Authority => $"https://{Domain}/";

    public static bool HasCanonicalDomain(Auth0Options options) =>
        options.Domain == options.Domain.Trim() &&
        Uri.CheckHostName(options.Domain) == UriHostNameType.Dns;

    public static bool HasCanonicalAudience(Auth0Options options) =>
        IsCanonicalHttpsUri(options.Audience);

    public static bool HasCanonicalRoleClaim(Auth0Options options) =>
        IsCanonicalHttpsUri(options.RoleClaim);

    private static bool IsCanonicalHttpsUri(string value)
    {
        if (value != value.Trim() ||
            !Uri.TryCreate(value, UriKind.Absolute, out Uri? uri))
        {
            return false;
        }

        return string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrEmpty(uri.Host) &&
            string.IsNullOrEmpty(uri.UserInfo) &&
            string.IsNullOrEmpty(uri.Query) &&
            string.IsNullOrEmpty(uri.Fragment);
    }
}
