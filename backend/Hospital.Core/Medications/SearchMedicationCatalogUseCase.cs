using Hospital.Core.Application;

namespace Hospital.Core.Medications;

public sealed class SearchMedicationCatalogUseCase(IMedicationCatalog medicationCatalog)
{
    public const int DefaultLimit = 10;
    public const int MaximumLimit = 20;
    public const int MinimumQueryLength = 2;
    public const int MaximumQueryLength = 100;

    public Task<ApplicationResult<MedicationCatalogSearch>> ExecuteAsync(
        string? query,
        int limit = DefaultLimit,
        CancellationToken cancellationToken = default)
    {
        string normalizedQuery = NormalizeQuery(query);

        if (normalizedQuery.Length is < MinimumQueryLength or > MaximumQueryLength)
        {
            return Task.FromResult(ApplicationResult.Validation<MedicationCatalogSearch>(
                "invalid_medication_query",
                $"The medication query must contain between {MinimumQueryLength} and {MaximumQueryLength} characters."));
        }

        if (limit is < 1 or > MaximumLimit)
        {
            return Task.FromResult(ApplicationResult.Validation<MedicationCatalogSearch>(
                "invalid_medication_limit",
                $"The medication limit must be between 1 and {MaximumLimit}."));
        }

        return medicationCatalog.SearchAsync(normalizedQuery, limit, cancellationToken);
    }

    private static string NormalizeQuery(string? query) =>
        string.Join(' ', (query ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
