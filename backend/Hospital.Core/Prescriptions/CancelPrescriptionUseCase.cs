using Hospital.Core.Application;
using Hospital.Core.Audit;
using Hospital.Core.Persistence;
using Hospital.Core.Pharmacy;
using Hospital.Core.Profiles;

using Microsoft.EntityFrameworkCore;

namespace Hospital.Core.Prescriptions;

public sealed class CancelPrescriptionUseCase(
    IApplicationDbContext applicationDbContext,
    TimeProvider timeProvider)
{
    public async Task<ApplicationResult<DoctorPrescriptionDetails>> ExecuteAsync(
        long doctorUserProfileId,
        long prescriptionId,
        uint expectedVersion,
        string? traceId,
        CancellationToken cancellationToken = default)
    {
        if (prescriptionId <= 0)
        {
            return ApplicationResult.NotFound<DoctorPrescriptionDetails>(
                "prescription_not_found",
                "The prescription was not found.");
        }

        if (expectedVersion == 0)
        {
            return ApplicationResult.Validation<DoctorPrescriptionDetails>(
                "invalid_prescription_version",
                "A valid expected prescription version is required.");
        }

        Prescription? prescription = await applicationDbContext.Prescriptions
            .Include(candidate => candidate.PrescriberClinicianProfile)
                .ThenInclude(profile => profile.UserProfile)
            .SingleOrDefaultAsync(
                candidate =>
                    candidate.Id == prescriptionId &&
                    candidate.PrescriberClinicianProfile.UserProfileId == doctorUserProfileId &&
                    candidate.PrescriberClinicianProfile.UserProfile.Status == AccountStatus.Active &&
                    candidate.PrescriberClinicianProfile.UserProfile.ProfileType == ProfileType.Doctor,
                cancellationToken);

        if (prescription is null)
        {
            return ApplicationResult.NotFound<DoctorPrescriptionDetails>(
                "prescription_not_found",
                "The prescription was not found.");
        }

        if (prescription.Version != expectedVersion)
        {
            return ApplicationResult.Conflict<DoctorPrescriptionDetails>(
                "prescription_changed",
                "This prescription changed. Refresh it before trying again.");
        }

        Fulfillment? fulfillment = await applicationDbContext.Fulfillments
            .SingleOrDefaultAsync(
                candidate => candidate.PrescriptionId == prescription.Id,
                cancellationToken);

        if (prescription.Status != PrescriptionStatus.Issued ||
            fulfillment is null ||
            fulfillment.Status == FulfillmentStatus.Cancelled)
        {
            return ApplicationResult.Conflict<DoctorPrescriptionDetails>(
                "prescription_not_cancellable",
                "Only an issued prescription with an active fulfillment can be cancelled.");
        }

        if (fulfillment.Status == FulfillmentStatus.Dispensed)
        {
            return ApplicationResult.Conflict<DoctorPrescriptionDetails>(
                "prescription_already_dispensed",
                "A dispensed prescription cannot be cancelled.");
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        if (now < prescription.IssuedAtUtc)
        {
            return ApplicationResult.Conflict<DoctorPrescriptionDetails>(
                "prescription_time_conflict",
                "The prescription cancellation time cannot be before its issue time.");
        }

        PrescriptionStatus previousPrescriptionStatus = prescription.Status;
        FulfillmentStatus previousFulfillmentStatus = fulfillment.Status;
        prescription.Status = PrescriptionStatus.Cancelled;
        prescription.CancelledAtUtc = now;
        fulfillment.Status = FulfillmentStatus.Cancelled;
        fulfillment.CancelledAtUtc = now;

        string? normalizedTraceId = AuditTrace.Normalize(traceId);
        applicationDbContext.AuditEvents.AddRange(
            new AuditEvent
            {
                ActorUserProfileId = doctorUserProfileId,
                Action = "PrescriptionCancelled",
                AffectedEntityType = nameof(Prescription),
                AffectedEntityId = prescription.Id,
                OccurredAtUtc = now,
                TraceId = normalizedTraceId,
                MetadataJson = $"{{\"from\":\"{previousPrescriptionStatus}\",\"to\":\"Cancelled\"}}",
            },
            new AuditEvent
            {
                ActorUserProfileId = doctorUserProfileId,
                Action = "FulfillmentCancelled",
                AffectedEntityType = nameof(Fulfillment),
                AffectedEntityId = fulfillment.Id,
                OccurredAtUtc = now,
                TraceId = normalizedTraceId,
                MetadataJson = $"{{\"from\":\"{previousFulfillmentStatus}\",\"to\":\"Cancelled\"}}",
            });

        try
        {
            await applicationDbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApplicationResult.Conflict<DoctorPrescriptionDetails>(
                "prescription_changed",
                "This prescription or fulfillment changed. Refresh it before trying again.");
        }

        return await PrescriptionAccess.GetAsync(
            applicationDbContext,
            prescription.Id,
            doctorUserProfileId,
            cancellationToken);
    }
}
