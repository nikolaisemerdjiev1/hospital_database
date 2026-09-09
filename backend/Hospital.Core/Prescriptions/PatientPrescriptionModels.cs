namespace Hospital.Core.Prescriptions;

public static class PatientPharmacyStatusLabels
{
    public const string ReceivedByPharmacy = "Received by pharmacy";
    public const string UnderPharmacistReview = "Under pharmacist review";
    public const string ReadyForPickup = "Ready for pickup";
    public const string Dispensed = "Dispensed";
    public const string Cancelled = "Cancelled";
}

public sealed record PatientPrescriptionSummary(
    long Id,
    string MedicationDisplayName,
    string Dose,
    string Instructions,
    int Quantity,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset? CancelledAtUtc,
    string PharmacyStatus);

public sealed record PatientPrescriptionPage(
    IReadOnlyList<PatientPrescriptionSummary> Items,
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages);
