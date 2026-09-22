namespace Hospital.Api.Configuration;

internal sealed class ReverseProxyOptions
{
    public const string SectionName = "ReverseProxy";

    public bool TrustForwardedHeaders { get; init; }
}
