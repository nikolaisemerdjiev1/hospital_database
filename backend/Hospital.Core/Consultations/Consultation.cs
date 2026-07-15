using Hospital.Core.Scheduling;

namespace Hospital.Core.Consultations;

public sealed class Consultation
{
    public long Id { get; private set; }

    public long AppointmentId { get; init; }

    public string? Outcome { get; set; }

    public string? ClinicalNotes { get; set; }

    public string? PatientSummary { get; set; }

    public string? CareInstructions { get; set; }

    public required ConsultationStatus Status { get; set; }

    public required DateTimeOffset StartedAtUtc { get; init; }

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public uint Version { get; private set; }

    public Appointment Appointment { get; private set; } = null!;
}
