using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.Extensions.Logging;

namespace Hospital.Infrastructure.Medications;

internal sealed record RxNormConcept(
    string RxCui,
    string DisplayName,
    string ConceptType);

internal sealed record RxNormSearchOutcome(
    bool IsAvailable,
    IReadOnlyList<RxNormConcept> Concepts);

internal sealed class RxNormClient(
    HttpClient httpClient,
    ILogger<RxNormClient> logger)
{
    private static readonly Action<ILogger, int, Exception?> LogUnexpectedStatus =
        LoggerMessage.Define<int>(
            LogLevel.Warning,
            new EventId(1, nameof(LogUnexpectedStatus)),
            "RxNorm returned HTTP {StatusCode}; the local medication catalog will be used.");

    private static readonly Action<ILogger, Exception?> LogUnavailable =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(2, nameof(LogUnavailable)),
            "RxNorm could not be reached; the local medication catalog will be used.");

    private static readonly HashSet<string> SupportedConceptTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "SBD",
            "SCD",
        };

    public async Task<RxNormSearchOutcome> SearchAsync(
        string query,
        CancellationToken cancellationToken)
    {
        try
        {
            string requestPath =
                $"drugs.json?name={Uri.EscapeDataString(query)}&expand=psn";
            using HttpResponseMessage response = await httpClient.GetAsync(
                requestPath,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                LogUnexpectedStatus(logger, (int)response.StatusCode, null);
                return new RxNormSearchOutcome(false, []);
            }

            RxNormResponse? payload = await response.Content
                .ReadFromJsonAsync<RxNormResponse>(cancellationToken);
            return new RxNormSearchOutcome(true, ParseConcepts(payload));
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException or JsonException ||
            exception.GetType().Namespace?.StartsWith("Polly", StringComparison.Ordinal) == true)
        {
            LogUnavailable(logger, exception);
            return new RxNormSearchOutcome(false, []);
        }
    }

    private static RxNormConcept[] ParseConcepts(RxNormResponse? response)
    {
        if (response?.DrugGroup?.ConceptGroup is null)
        {
            return [];
        }

        Dictionary<string, RxNormConcept> concepts = new(StringComparer.Ordinal);
        foreach (RxNormConceptGroup group in response.DrugGroup.ConceptGroup)
        {
            string? groupType = Normalize(group.Tty);
            if (groupType is null || !SupportedConceptTypes.Contains(groupType))
            {
                continue;
            }

            foreach (RxNormConceptProperty property in group.ConceptProperties ?? [])
            {
                string? rxCui = Normalize(property.RxCui);
                string? displayName = Normalize(property.Psn) ??
                    Normalize(property.Synonym) ??
                    Normalize(property.Name);
                string conceptType = Normalize(property.Tty) ?? groupType;

                if (rxCui is null ||
                    rxCui.Length > 20 ||
                    displayName is null ||
                    !SupportedConceptTypes.Contains(conceptType) ||
                    string.Equals(property.Suppress, "Y", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                concepts.TryAdd(
                    rxCui,
                    new RxNormConcept(
                        rxCui,
                        Truncate(displayName, 200),
                        Truncate(conceptType, 100)));
            }
        }

        return concepts.Values
            .OrderBy(static concept => concept.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static concept => concept.RxCui, StringComparer.Ordinal)
            .ToArray();
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Truncate(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];

    private sealed record RxNormResponse(RxNormDrugGroup? DrugGroup);

    private sealed record RxNormDrugGroup(IReadOnlyList<RxNormConceptGroup>? ConceptGroup);

    private sealed record RxNormConceptGroup(
        string? Tty,
        IReadOnlyList<RxNormConceptProperty>? ConceptProperties);

    private sealed record RxNormConceptProperty(
        string? RxCui,
        string? Name,
        string? Synonym,
        string? Psn,
        string? Tty,
        string? Suppress);
}
