using Hospital.Core.Application;
using Hospital.Core.Persistence;

using Microsoft.EntityFrameworkCore;

namespace Hospital.Core.Pharmacy;

public sealed class ListPharmacyWorkQueueUseCase(IApplicationDbContext applicationDbContext)
{
    public async Task<ApplicationResult<PharmacyWorkQueuePage>> ExecuteAsync(
        long userProfileId,
        int page,
        int pageSize,
        FulfillmentStatus? status,
        CancellationToken cancellationToken = default)
    {
        if (page < 1 || pageSize is < 1 or > 50)
        {
            return ApplicationResult.Validation<PharmacyWorkQueuePage>(
                "invalid_pagination",
                "Page must be at least 1 and pageSize must be between 1 and 50.");
        }

        long itemsToSkip = (long)(page - 1) * pageSize;
        if (itemsToSkip > int.MaxValue)
        {
            return ApplicationResult.Validation<PharmacyWorkQueuePage>(
                "invalid_pagination",
                "The requested page is outside the supported pagination range.");
        }

        long? pharmacistProfileId = await PharmacistProfileAccess.ResolveActiveProfileIdAsync(
            applicationDbContext,
            userProfileId,
            cancellationToken);
        if (!pharmacistProfileId.HasValue)
        {
            return ApplicationResult.NotFound<PharmacyWorkQueuePage>(
                "pharmacist_profile_not_found",
                "The pharmacist profile was not found.");
        }

        IQueryable<Fulfillment> query = PharmacyFulfillmentAccess.VisibleQueue(
            applicationDbContext,
            pharmacistProfileId.Value,
            status);

        int totalItems = await query.CountAsync(cancellationToken);
        bool newestFirst = status is FulfillmentStatus.Dispensed or FulfillmentStatus.Cancelled;
        IOrderedQueryable<Fulfillment> orderedQuery = newestFirst
            ? query.OrderByDescending(fulfillment => fulfillment.CreatedAtUtc)
            : query
                .OrderBy(fulfillment => fulfillment.Status == FulfillmentStatus.Ready
                    ? 0
                    : fulfillment.Status == FulfillmentStatus.InReview
                        ? 1
                        : 2)
                .ThenBy(fulfillment => fulfillment.CreatedAtUtc);

        PharmacyWorkQueueItem[] items = await orderedQuery
            .ThenBy(fulfillment => fulfillment.Id)
            .Skip((int)itemsToSkip)
            .Take(pageSize)
            .Select(fulfillment => new PharmacyWorkQueueItem(
                fulfillment.Id,
                fulfillment.PrescriptionId,
                fulfillment.Prescription.PatientProfile.UserProfile.DisplayName,
                fulfillment.Prescription.MedicationDisplayNameSnapshot,
                fulfillment.Prescription.Dose,
                fulfillment.Prescription.Quantity,
                fulfillment.Prescription.IssuedAtUtc,
                fulfillment.Status.ToString(),
                fulfillment.CreatedAtUtc,
                fulfillment.Version))
            .ToArrayAsync(cancellationToken);

        int totalPages = totalItems == 0
            ? 0
            : (int)Math.Ceiling(totalItems / (double)pageSize);

        return ApplicationResult.Success(new PharmacyWorkQueuePage(
            items,
            page,
            pageSize,
            totalItems,
            totalPages));
    }
}
