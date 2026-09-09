using Hospital.Core.Application;
using Hospital.Core.Persistence;
using Hospital.Core.Pharmacy;
using Hospital.Core.Profiles;

using Microsoft.EntityFrameworkCore;

namespace Hospital.Core.Prescriptions;

public sealed class ListPatientPrescriptionsUseCase(IApplicationDbContext applicationDbContext)
{
    public async Task<ApplicationResult<PatientPrescriptionPage>> ExecuteAsync(
        long userProfileId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (page < 1 || pageSize is < 1 or > 50)
        {
            return ApplicationResult.Validation<PatientPrescriptionPage>(
                "invalid_pagination",
                "Page must be at least 1 and pageSize must be between 1 and 50.");
        }

        long itemsToSkip = (long)(page - 1) * pageSize;
        if (itemsToSkip > int.MaxValue)
        {
            return ApplicationResult.Validation<PatientPrescriptionPage>(
                "invalid_pagination",
                "The requested page is outside the supported pagination range.");
        }

        long? patientProfileId = await applicationDbContext.PatientProfiles
            .AsNoTracking()
            .Where(profile =>
                profile.UserProfileId == userProfileId &&
                profile.UserProfile.Status == AccountStatus.Active &&
                profile.UserProfile.ProfileType == ProfileType.Patient)
            .Select(profile => (long?)profile.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (!patientProfileId.HasValue)
        {
            return ApplicationResult.NotFound<PatientPrescriptionPage>(
                "patient_profile_not_found",
                "The patient profile was not found.");
        }

        IQueryable<Fulfillment> query = applicationDbContext.Fulfillments
            .AsNoTracking()
            .Where(fulfillment =>
                fulfillment.Prescription.PatientProfileId == patientProfileId.Value);

        int totalItems = await query.CountAsync(cancellationToken);
        PatientPrescriptionSummary[] items = await query
            .OrderByDescending(fulfillment => fulfillment.Prescription.IssuedAtUtc)
            .ThenByDescending(fulfillment => fulfillment.PrescriptionId)
            .Skip((int)itemsToSkip)
            .Take(pageSize)
            .Select(fulfillment => new PatientPrescriptionSummary(
                fulfillment.PrescriptionId,
                fulfillment.Prescription.MedicationDisplayNameSnapshot,
                fulfillment.Prescription.Dose,
                fulfillment.Prescription.Instructions,
                fulfillment.Prescription.Quantity,
                fulfillment.Prescription.IssuedAtUtc,
                fulfillment.Prescription.CancelledAtUtc,
                fulfillment.Status == FulfillmentStatus.Pending
                    ? PatientPharmacyStatusLabels.ReceivedByPharmacy
                    : fulfillment.Status == FulfillmentStatus.InReview
                        ? PatientPharmacyStatusLabels.UnderPharmacistReview
                        : fulfillment.Status == FulfillmentStatus.Ready
                            ? PatientPharmacyStatusLabels.ReadyForPickup
                            : fulfillment.Status == FulfillmentStatus.Dispensed
                                ? PatientPharmacyStatusLabels.Dispensed
                                : PatientPharmacyStatusLabels.Cancelled))
            .ToArrayAsync(cancellationToken);

        int totalPages = totalItems == 0
            ? 0
            : (int)Math.Ceiling(totalItems / (double)pageSize);

        return ApplicationResult.Success(new PatientPrescriptionPage(
            items,
            page,
            pageSize,
            totalItems,
            totalPages));
    }
}
