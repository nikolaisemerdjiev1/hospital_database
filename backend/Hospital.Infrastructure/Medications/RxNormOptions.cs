using System.ComponentModel.DataAnnotations;

namespace Hospital.Infrastructure.Medications;

public sealed class RxNormOptions
{
    public const string SectionName = "MedicationCatalog:RxNorm";

    [Required]
    public string BaseAddress { get; init; } = "https://rxnav.nlm.nih.gov/REST/";

    [Range(1, 24)]
    public int CacheTtlHours { get; init; } = 12;

    [Range(1, 10)]
    public int RetryAttempts { get; init; } = 2;

    [Range(1, 30)]
    public int AttemptTimeoutSeconds { get; init; } = 3;

    [Range(1, 60)]
    public int TotalRequestTimeoutSeconds { get; init; } = 10;

    public static bool HasValidBaseAddress(RxNormOptions options) =>
        Uri.TryCreate(options.BaseAddress, UriKind.Absolute, out Uri? uri) &&
        uri.Scheme == Uri.UriSchemeHttps &&
        uri.UserInfo.Length == 0 &&
        string.IsNullOrEmpty(uri.Query) &&
        string.IsNullOrEmpty(uri.Fragment);

    public static bool HasValidTimeoutBudget(RxNormOptions options) =>
        options.TotalRequestTimeoutSeconds > options.AttemptTimeoutSeconds;
}
