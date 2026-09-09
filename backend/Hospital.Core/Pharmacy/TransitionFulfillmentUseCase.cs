using System.Diagnostics;

using Hospital.Core.Application;
using Hospital.Core.Audit;
using Hospital.Core.Persistence;
using Hospital.Core.Prescriptions;

using Microsoft.EntityFrameworkCore;

namespace Hospital.Core.Pharmacy;

public sealed class TransitionFulfillmentUseCase(
    IApplicationDbContext applicationDbContext,
    TimeProvider timeProvider)
{
    public async Task<ApplicationResult<PharmacyFulfillmentDetails>> ExecuteAsync(
        long userProfileId,
        long fulfillmentId,
        FulfillmentStatus targetStatus,
        uint expectedVersion,
        string? traceId,
        CancellationToken cancellationToken = default)
    {
        if (fulfillmentId <= 0)
        {
            return ApplicationResult.NotFound<PharmacyFulfillmentDetails>(
                "fulfillment_not_found",
                "The fulfillment was not found.");
        }

        if (targetStatus is not FulfillmentStatus.InReview and
            not FulfillmentStatus.Ready and
            not FulfillmentStatus.Dispensed)
        {
            return ApplicationResult.Validation<PharmacyFulfillmentDetails>(
                "invalid_fulfillment_transition_target",
                "Target status must be InReview, Ready, or Dispensed.");
        }

        if (expectedVersion == 0)
        {
            return ApplicationResult.Validation<PharmacyFulfillmentDetails>(
                "invalid_fulfillment_version",
                "A valid expected fulfillment version is required.");
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

        Fulfillment? fulfillment = await applicationDbContext.Fulfillments
            .Include(candidate => candidate.Prescription)
            .SingleOrDefaultAsync(
                candidate =>
                    candidate.Id == fulfillmentId &&
                    ((candidate.Status == FulfillmentStatus.Pending &&
                        candidate.AssignedPharmacistProfileId == null) ||
                    candidate.AssignedPharmacistProfileId == pharmacistProfileId.Value),
                cancellationToken);

        if (fulfillment is null)
        {
            return ApplicationResult.NotFound<PharmacyFulfillmentDetails>(
                "fulfillment_not_found",
                "The fulfillment was not found.");
        }

        if (fulfillment.Version != expectedVersion)
        {
            return Changed();
        }

        if (fulfillment.Prescription.Status != PrescriptionStatus.Issued ||
            !CanTransition(fulfillment, pharmacistProfileId.Value, targetStatus))
        {
            return ApplicationResult.Conflict<PharmacyFulfillmentDetails>(
                "fulfillment_transition_invalid",
                $"A {fulfillment.Status} fulfillment cannot move to {targetStatus}.");
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        DateTimeOffset earliestTransitionTime = targetStatus switch
        {
            FulfillmentStatus.InReview => fulfillment.CreatedAtUtc,
            FulfillmentStatus.Ready => fulfillment.ReviewStartedAtUtc!.Value,
            FulfillmentStatus.Dispensed => fulfillment.ReadyAtUtc!.Value,
            _ => throw new UnreachableException(),
        };
        if (now < earliestTransitionTime)
        {
            return ApplicationResult.Conflict<PharmacyFulfillmentDetails>(
                "fulfillment_time_conflict",
                "The fulfillment transition time cannot precede its current workflow state.");
        }

        FulfillmentStatus previousStatus = fulfillment.Status;
        ApplyTransition(fulfillment, pharmacistProfileId.Value, targetStatus, now);
        applicationDbContext.AuditEvents.Add(new AuditEvent
        {
            ActorUserProfileId = userProfileId,
            Action = GetAuditAction(targetStatus),
            AffectedEntityType = nameof(Fulfillment),
            AffectedEntityId = fulfillment.Id,
            OccurredAtUtc = now,
            TraceId = AuditTrace.Normalize(traceId),
            MetadataJson = $"{{\"from\":\"{previousStatus}\",\"to\":\"{targetStatus}\"}}",
        });

        try
        {
            await applicationDbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Changed();
        }

        return await PharmacyFulfillmentAccess.LoadDetailsAsync(
            applicationDbContext,
            fulfillment.Id,
            pharmacistProfileId.Value,
            cancellationToken);
    }

    private static bool CanTransition(
        Fulfillment fulfillment,
        long pharmacistProfileId,
        FulfillmentStatus targetStatus) =>
        targetStatus switch
        {
            FulfillmentStatus.InReview =>
                fulfillment.Status == FulfillmentStatus.Pending &&
                fulfillment.AssignedPharmacistProfileId is null,
            FulfillmentStatus.Ready =>
                fulfillment.Status == FulfillmentStatus.InReview &&
                fulfillment.AssignedPharmacistProfileId == pharmacistProfileId,
            FulfillmentStatus.Dispensed =>
                fulfillment.Status == FulfillmentStatus.Ready &&
                fulfillment.AssignedPharmacistProfileId == pharmacistProfileId,
            _ => false,
        };

    private static void ApplyTransition(
        Fulfillment fulfillment,
        long pharmacistProfileId,
        FulfillmentStatus targetStatus,
        DateTimeOffset occurredAtUtc)
    {
        fulfillment.Status = targetStatus;
        switch (targetStatus)
        {
            case FulfillmentStatus.InReview:
                fulfillment.AssignedPharmacistProfileId = pharmacistProfileId;
                fulfillment.ReviewStartedAtUtc = occurredAtUtc;
                break;
            case FulfillmentStatus.Ready:
                fulfillment.ReadyAtUtc = occurredAtUtc;
                break;
            case FulfillmentStatus.Dispensed:
                fulfillment.DispensedAtUtc = occurredAtUtc;
                break;
            default:
                throw new UnreachableException();
        }
    }

    private static string GetAuditAction(FulfillmentStatus targetStatus) => targetStatus switch
    {
        FulfillmentStatus.InReview => "FulfillmentReviewStarted",
        FulfillmentStatus.Ready => "FulfillmentMarkedReady",
        FulfillmentStatus.Dispensed => "FulfillmentDispensed",
        _ => throw new UnreachableException(),
    };

    private static ApplicationResult<PharmacyFulfillmentDetails> Changed() =>
        ApplicationResult.Conflict<PharmacyFulfillmentDetails>(
            "fulfillment_changed",
            "This fulfillment changed. Refresh it before trying again.");
}
