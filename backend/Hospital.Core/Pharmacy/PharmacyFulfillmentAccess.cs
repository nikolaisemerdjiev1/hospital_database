using Hospital.Core.Application;
using Hospital.Core.Persistence;
using Hospital.Core.Prescriptions;

using Microsoft.EntityFrameworkCore;

namespace Hospital.Core.Pharmacy;

internal static class PharmacyFulfillmentAccess
{
    public static IQueryable<Fulfillment> VisibleQueue(
        IApplicationDbContext applicationDbContext,
        long pharmacistProfileId,
        FulfillmentStatus? status)
    {
        IQueryable<Fulfillment> query = applicationDbContext.Fulfillments
            .AsNoTracking();

        if (status == FulfillmentStatus.Pending)
        {
            return query.Where(fulfillment =>
                fulfillment.Status == FulfillmentStatus.Pending &&
                fulfillment.AssignedPharmacistProfileId == null &&
                fulfillment.Prescription.Status == PrescriptionStatus.Issued);
        }

        if (status.HasValue)
        {
            return query.Where(fulfillment =>
                fulfillment.Status == status.Value &&
                fulfillment.AssignedPharmacistProfileId == pharmacistProfileId);
        }

        return query.Where(fulfillment =>
            (fulfillment.Status == FulfillmentStatus.Pending &&
                fulfillment.AssignedPharmacistProfileId == null &&
                fulfillment.Prescription.Status == PrescriptionStatus.Issued) ||
            (fulfillment.AssignedPharmacistProfileId == pharmacistProfileId &&
                (fulfillment.Status == FulfillmentStatus.InReview ||
                    fulfillment.Status == FulfillmentStatus.Ready)));
    }

    public static async Task<ApplicationResult<PharmacyFulfillmentDetails>> LoadDetailsAsync(
        IApplicationDbContext applicationDbContext,
        long fulfillmentId,
        long pharmacistProfileId,
        CancellationToken cancellationToken)
    {
        PharmacyFulfillmentDetails? details = await applicationDbContext.Fulfillments
            .AsNoTracking()
            .Where(fulfillment =>
                fulfillment.Id == fulfillmentId &&
                ((fulfillment.Status == FulfillmentStatus.Pending &&
                    fulfillment.AssignedPharmacistProfileId == null &&
                    fulfillment.Prescription.Status == PrescriptionStatus.Issued) ||
                fulfillment.AssignedPharmacistProfileId == pharmacistProfileId))
            .Select(fulfillment => new PharmacyFulfillmentDetails(
                fulfillment.Id,
                fulfillment.PrescriptionId,
                fulfillment.Status.ToString(),
                fulfillment.CreatedAtUtc,
                fulfillment.ReviewStartedAtUtc,
                fulfillment.ReadyAtUtc,
                fulfillment.DispensedAtUtc,
                fulfillment.CancelledAtUtc,
                fulfillment.Version,
                fulfillment.Prescription.PatientProfile.UserProfile.DisplayName,
                fulfillment.Prescription.PatientProfile.AllergySummary,
                fulfillment.Prescription.PrescriberClinicianProfile.UserProfile.DisplayName,
                fulfillment.Prescription.RxCuiSnapshot,
                fulfillment.Prescription.MedicationDisplayNameSnapshot,
                fulfillment.Prescription.Dose,
                fulfillment.Prescription.Instructions,
                fulfillment.Prescription.Quantity,
                fulfillment.Prescription.Status.ToString(),
                fulfillment.Prescription.IssuedAtUtc,
                fulfillment.Prescription.CancelledAtUtc,
                fulfillment.AssignedPharmacistProfileId == pharmacistProfileId))
            .SingleOrDefaultAsync(cancellationToken);

        return details is null
            ? ApplicationResult.NotFound<PharmacyFulfillmentDetails>(
                "fulfillment_not_found",
                "The fulfillment was not found.")
            : ApplicationResult.Success(details);
    }
}
