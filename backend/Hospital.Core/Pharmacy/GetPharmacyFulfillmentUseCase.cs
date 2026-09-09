using Hospital.Core.Application;
using Hospital.Core.Persistence;

namespace Hospital.Core.Pharmacy;

public sealed class GetPharmacyFulfillmentUseCase(IApplicationDbContext applicationDbContext)
{
    public async Task<ApplicationResult<PharmacyFulfillmentDetails>> ExecuteAsync(
        long userProfileId,
        long fulfillmentId,
        CancellationToken cancellationToken = default)
    {
        if (fulfillmentId <= 0)
        {
            return ApplicationResult.NotFound<PharmacyFulfillmentDetails>(
                "fulfillment_not_found",
                "The fulfillment was not found.");
        }

        long? pharmacistProfileId = await PharmacistProfileAccess.ResolveActiveProfileIdAsync(
            applicationDbContext,
            userProfileId,
            cancellationToken);
        if (!pharmacistProfileId.HasValue)
        {
            return ApplicationResult.NotFound<PharmacyFulfillmentDetails>(
                "pharmacist_profile_not_found",
                "The pharmacist profile was not found.");
        }

        return await PharmacyFulfillmentAccess.LoadDetailsAsync(
            applicationDbContext,
            fulfillmentId,
            pharmacistProfileId.Value,
            cancellationToken);
    }
}
