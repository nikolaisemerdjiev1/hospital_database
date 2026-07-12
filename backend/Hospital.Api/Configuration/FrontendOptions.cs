using System.ComponentModel.DataAnnotations;

namespace Hospital.Api.Configuration;

internal sealed class FrontendOptions
{
    public const string SectionName = "Frontend";

    [Required]
    [Url]
    public string Origin { get; init; } = string.Empty;

    public static bool HasCanonicalHttpOrigin(FrontendOptions options)
    {
        if (!Uri.TryCreate(options.Origin, UriKind.Absolute, out Uri? origin))
        {
            return false;
        }

        bool hasHttpScheme =
            string.Equals(origin.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(origin.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        bool hasOnlyOriginComponents =
            origin.AbsolutePath == "/" &&
            string.IsNullOrEmpty(origin.Query) &&
            string.IsNullOrEmpty(origin.Fragment) &&
            string.IsNullOrEmpty(origin.UserInfo);

        return hasHttpScheme &&
            hasOnlyOriginComponents &&
            !options.Origin.EndsWith('/');
    }
}
