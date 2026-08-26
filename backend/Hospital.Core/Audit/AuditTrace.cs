namespace Hospital.Core.Audit;

internal static class AuditTrace
{
    public static string? Normalize(string? traceId)
    {
        string? normalized = string.IsNullOrWhiteSpace(traceId) ? null : traceId.Trim();
        return normalized?.Length > 64 ? normalized[..64] : normalized;
    }
}
