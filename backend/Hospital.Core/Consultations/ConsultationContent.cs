namespace Hospital.Core.Consultations;

internal sealed record ConsultationContent(
    string? Outcome,
    string? ClinicalNotes,
    string? PatientSummary,
    string? CareInstructions)
{
    public static bool TryCreate(
        string? outcome,
        string? clinicalNotes,
        string? patientSummary,
        string? careInstructions,
        bool requireComplete,
        out ConsultationContent? content,
        out string? errorCode,
        out string? errorMessage)
    {
        content = new ConsultationContent(
            Normalize(outcome),
            Normalize(clinicalNotes),
            Normalize(patientSummary),
            Normalize(careInstructions));

        if (content.Outcome?.Length > 500)
        {
            return Fail(
                "invalid_consultation_outcome",
                "The consultation outcome cannot exceed 500 characters.",
                out content,
                out errorCode,
                out errorMessage);
        }

        if (content.ClinicalNotes?.Length > 4000)
        {
            return Fail(
                "invalid_clinical_notes",
                "Clinical notes cannot exceed 4,000 characters.",
                out content,
                out errorCode,
                out errorMessage);
        }

        if (content.PatientSummary?.Length > 2000)
        {
            return Fail(
                "invalid_patient_summary",
                "The patient summary cannot exceed 2,000 characters.",
                out content,
                out errorCode,
                out errorMessage);
        }

        if (content.CareInstructions?.Length > 2000)
        {
            return Fail(
                "invalid_care_instructions",
                "Care instructions cannot exceed 2,000 characters.",
                out content,
                out errorCode,
                out errorMessage);
        }

        if (requireComplete &&
            (content.Outcome is null ||
                content.ClinicalNotes is null ||
                content.PatientSummary is null ||
                content.CareInstructions is null))
        {
            return Fail(
                "incomplete_consultation",
                "Outcome, clinical notes, patient summary, and care instructions are required to complete a consultation.",
                out content,
                out errorCode,
                out errorMessage);
        }

        errorCode = null;
        errorMessage = null;
        return true;
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool Fail(
        string code,
        string message,
        out ConsultationContent? content,
        out string? errorCode,
        out string? errorMessage)
    {
        content = null;
        errorCode = code;
        errorMessage = message;
        return false;
    }
}
