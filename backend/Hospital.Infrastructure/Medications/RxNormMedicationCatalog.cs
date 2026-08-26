using Hospital.Core.Application;
using Hospital.Core.Medications;
using Hospital.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Hospital.Infrastructure.Medications;

internal sealed class RxNormMedicationCatalog(
    ApplicationDbContext applicationDbContext,
    RxNormClient rxNormClient,
    IMemoryCache memoryCache,
    IOptions<RxNormOptions> options,
    TimeProvider timeProvider) : IMedicationCatalog
{
    private readonly RxNormOptions options = options.Value;

    public async Task<ApplicationResult<MedicationCatalogSearch>> SearchAsync(
        string normalizedQuery,
        int limit,
        CancellationToken cancellationToken = default)
    {
        string cacheKey = $"rxnorm:{normalizedQuery.ToUpperInvariant()}:{limit}";
        if (memoryCache.TryGetValue(
                cacheKey,
                out IReadOnlyList<MedicationCatalogItem>? memoryItems) &&
            memoryItems is not null)
        {
            return ApplicationResult.Success(new MedicationCatalogSearch(
                memoryItems,
                MedicationCatalogStatus.Cached));
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        Medication[] freshMedications = await LoadLocalAsync(
            normalizedQuery,
            limit,
            now.AddHours(-options.CacheTtlHours),
            cancellationToken);
        if (freshMedications.Length > 0)
        {
            MedicationCatalogItem[] freshItems = freshMedications
                .Select(ToCatalogItem)
                .ToArray();
            Cache(cacheKey, freshItems);
            return ApplicationResult.Success(new MedicationCatalogSearch(
                freshItems,
                MedicationCatalogStatus.Cached));
        }

        RxNormSearchOutcome liveSearch = await rxNormClient.SearchAsync(
            normalizedQuery,
            cancellationToken);
        if (liveSearch.IsAvailable)
        {
            if (liveSearch.Concepts.Count == 0)
            {
                return ApplicationResult.Success(new MedicationCatalogSearch(
                    [],
                    MedicationCatalogStatus.Live));
            }

            Medication[] persisted = await PersistLiveConceptsAsync(
                liveSearch.Concepts.Take(limit),
                now,
                cancellationToken);
            MedicationCatalogItem[] liveItems = persisted
                .Select(ToCatalogItem)
                .ToArray();
            Cache(cacheKey, liveItems);
            return ApplicationResult.Success(new MedicationCatalogSearch(
                liveItems,
                MedicationCatalogStatus.Live));
        }

        Medication[] fallbackMedications = await LoadLocalAsync(
            normalizedQuery,
            limit,
            verifiedSinceUtc: null,
            cancellationToken);
        if (fallbackMedications.Length == 0)
        {
            return ApplicationResult.DependencyUnavailable<MedicationCatalogSearch>(
                "medication_catalog_unavailable",
                "The medication catalog is temporarily unavailable and no local matches were found.");
        }

        return ApplicationResult.Success(new MedicationCatalogSearch(
            fallbackMedications.Select(ToCatalogItem).ToArray(),
            MedicationCatalogStatus.Fallback));
    }

    private async Task<Medication[]> LoadLocalAsync(
        string query,
        int limit,
        DateTimeOffset? verifiedSinceUtc,
        CancellationToken cancellationToken)
    {
        string escapedQuery = EscapeLikePattern(query);
        string containsPattern = $"%{escapedQuery}%";
        string prefixPattern = $"{escapedQuery}%";

        IQueryable<Medication> medications = applicationDbContext.Medications
            .AsNoTracking()
            .Where(medication =>
                medication.RxCui == query ||
                EF.Functions.ILike(medication.DisplayName, containsPattern, "\\"));

        if (verifiedSinceUtc.HasValue)
        {
            medications = medications.Where(medication =>
                medication.Source == MedicationSource.RxNorm &&
                medication.LastVerifiedAtUtc >= verifiedSinceUtc.Value);
        }

        return await medications
            .OrderByDescending(medication => medication.RxCui == query)
            .ThenByDescending(medication =>
                EF.Functions.ILike(medication.DisplayName, prefixPattern, "\\"))
            .ThenBy(medication => medication.DisplayName)
            .ThenBy(medication => medication.RxCui)
            .Take(limit)
            .ToArrayAsync(cancellationToken);
    }

    private async Task<Medication[]> PersistLiveConceptsAsync(
        IEnumerable<RxNormConcept> concepts,
        DateTimeOffset verifiedAtUtc,
        CancellationToken cancellationToken)
    {
        RxNormConcept[] liveConcepts = concepts.ToArray();
        {
            await using IDbContextTransaction transaction = await applicationDbContext.Database
                .BeginTransactionAsync(cancellationToken);
            foreach (RxNormConcept concept in liveConcepts.OrderBy(
                         static concept => concept.RxCui,
                         StringComparer.Ordinal))
            {
                await applicationDbContext.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO medication
                        (rx_cui, display_name, classification, source,
                         last_verified_at_utc, created_at_utc)
                    VALUES
                        ({concept.RxCui}, {concept.DisplayName}, {concept.ConceptType},
                         {MedicationSource.RxNorm.ToString()}, {verifiedAtUtc}, {verifiedAtUtc})
                    ON CONFLICT (rx_cui) DO UPDATE SET
                        display_name = EXCLUDED.display_name,
                        classification = EXCLUDED.classification,
                        source = EXCLUDED.source,
                        last_verified_at_utc = EXCLUDED.last_verified_at_utc
                    """, cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }

        string[] rxCuis = liveConcepts.Select(static concept => concept.RxCui).ToArray();
        Dictionary<string, Medication> persisted = await applicationDbContext.Medications
            .AsNoTracking()
            .Where(medication => rxCuis.Contains(medication.RxCui))
            .ToDictionaryAsync(
                medication => medication.RxCui,
                StringComparer.Ordinal,
                cancellationToken);
        return liveConcepts.Select(concept => persisted[concept.RxCui]).ToArray();
    }

    private void Cache(string cacheKey, IReadOnlyList<MedicationCatalogItem> items) =>
        memoryCache.Set(
            cacheKey,
            items,
            new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(options.CacheTtlHours),
                Size = 1,
            });

    private static MedicationCatalogItem ToCatalogItem(Medication medication) =>
        new(
            medication.Id,
            medication.RxCui,
            medication.DisplayName,
            medication.Classification,
            medication.Strength,
            medication.DoseForm,
            medication.Source.ToString());

    private static string EscapeLikePattern(string value) =>
        value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
}
