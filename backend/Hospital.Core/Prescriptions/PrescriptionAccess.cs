using Hospital.Core.Application;
using Hospital.Core.Persistence;
using Hospital.Core.Profiles;

using Microsoft.EntityFrameworkCore;

namespace Hospital.Core.Prescriptions;

internal static class PrescriptionAccess
{
    public static async Task<DoctorPrescriptionDetails?> LoadDetailsAsync(
        IApplicationDbContext applicationDbContext,
        long prescriptionId,
        long doctorUserProfileId,
        CancellationToken cancellationToken) =>
        await applicationDbContext.Prescriptions
            .AsNoTracking()
            .Where(prescription =>
                prescription.Id == prescriptionId &&
                prescription.PrescriberClinicianProfile.UserProfileId == doctorUserProfileId &&
                prescription.PrescriberClinicianProfile.UserProfile.Status == AccountStatus.Active &&
                prescription.PrescriberClinicianProfile.UserProfile.ProfileType == ProfileType.Doctor)
            .Select(prescription => new DoctorPrescriptionDetails(
                prescription.Id,
                prescription.ConsultationId,
                prescription.MedicationId,
                prescription.RxCuiSnapshot,
                prescription.MedicationDisplayNameSnapshot,
                prescription.Dose,
                prescription.Instructions,
                prescription.Quantity,
                prescription.Status.ToString(),
                prescription.IssuedAtUtc,
                prescription.CancelledAtUtc,
                applicationDbContext.Fulfillments
                    .Where(fulfillment => fulfillment.PrescriptionId == prescription.Id)
                    .Select(fulfillment => fulfillment.Status.ToString())
                    .Single(),
                prescription.Version))
            .SingleOrDefaultAsync(cancellationToken);

    public static async Task<ApplicationResult<DoctorPrescriptionDetails>> GetAsync(
        IApplicationDbContext applicationDbContext,
        long prescriptionId,
        long doctorUserProfileId,
        CancellationToken cancellationToken)
    {
        DoctorPrescriptionDetails? details = await LoadDetailsAsync(
            applicationDbContext,
            prescriptionId,
            doctorUserProfileId,
            cancellationToken);

        return details is null
            ? ApplicationResult.NotFound<DoctorPrescriptionDetails>(
                "prescription_not_found",
                "The prescription was not found.")
            : ApplicationResult.Success(details);
    }
}
