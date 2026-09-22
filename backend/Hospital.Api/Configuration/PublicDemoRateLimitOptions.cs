using System.ComponentModel.DataAnnotations;

namespace Hospital.Api.Configuration;

internal sealed class PublicDemoRateLimitOptions
{
    public const string SectionName = "RateLimiting";

    [Range(1, 10_000)]
    public int GeneralPermitLimit { get; init; } = 120;

    [Range(1, 3_600)]
    public int GeneralWindowSeconds { get; init; } = 60;

    [Range(1, 10_000)]
    public int BurstPermitLimit { get; init; } = 20;

    [Range(1, 3_600)]
    public int BurstWindowSeconds { get; init; } = 10;

    [Range(1, 1_000)]
    public int ConcurrentPermitLimit { get; init; } = 8;

    [Range(1, 10_000)]
    public int MutationPermitLimit { get; init; } = 30;

    [Range(1, 3_600)]
    public int MutationWindowSeconds { get; init; } = 60;

    [Range(1, 10_000)]
    public int MedicationSearchPermitLimit { get; init; } = 20;

    [Range(1, 3_600)]
    public int MedicationSearchWindowSeconds { get; init; } = 60;

    [Range(1, 10_000)]
    public int ReadinessPermitLimit { get; init; } = 30;

    [Range(1, 3_600)]
    public int ReadinessWindowSeconds { get; init; } = 60;
}
