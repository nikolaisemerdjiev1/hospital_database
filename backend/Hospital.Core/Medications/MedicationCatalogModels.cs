using Hospital.Core.Application;

namespace Hospital.Core.Medications;

public enum MedicationCatalogStatus
{
    Live = 0,
    Cached = 1,
    Fallback = 2,
}

public sealed record MedicationCatalogItem(
    long MedicationId,
    string RxCui,
    string DisplayName,
    string? ConceptType,
    string? Strength,
    string? DoseForm,
    string Source);

public sealed record MedicationCatalogSearch(
    IReadOnlyList<MedicationCatalogItem> Items,
    MedicationCatalogStatus CatalogStatus);

public interface IMedicationCatalog
{
    Task<ApplicationResult<MedicationCatalogSearch>> SearchAsync(
        string normalizedQuery,
        int limit,
        CancellationToken cancellationToken = default);
}
