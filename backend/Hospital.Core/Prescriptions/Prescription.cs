using Hospital.Core.Consultations;
using Hospital.Core.Medications;
using Hospital.Core.Profiles;

namespace Hospital.Core.Prescriptions;

public sealed class Prescription
{
    public long Id { get; private set; }

    public long ConsultationId { get; init; }

    public long MedicationId { get; init; }

    public long PrescriberClinicianProfileId { get; init; }

    public long PatientProfileId { get; init; }

    public required string RxCuiSnapshot { get; init; }

    public required string MedicationDisplayNameSnapshot { get; init; }

    public required string Dose { get; init; }

    public required string Instructions { get; init; }

    public int Quantity { get; init; }

    public required PrescriptionStatus Status { get; set; }

    public required DateTimeOffset IssuedAtUtc { get; init; }

    public DateTimeOffset? CancelledAtUtc { get; set; }

    public uint Version { get; private set; }

    public Consultation Consultation { get; private set; } = null!;

    public Medication Medication { get; private set; } = null!;

    public ClinicianProfile PrescriberClinicianProfile { get; private set; } = null!;

    public PatientProfile PatientProfile { get; private set; } = null!;
}
