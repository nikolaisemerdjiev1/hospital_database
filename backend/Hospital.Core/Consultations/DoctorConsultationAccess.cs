using Hospital.Core.Application;
using Hospital.Core.Persistence;
using Hospital.Core.Profiles;

using Microsoft.EntityFrameworkCore;

namespace Hospital.Core.Consultations;

internal static class DoctorConsultationAccess
{
    public static async Task<Consultation?> LoadOwnedAsync(
        IApplicationDbContext applicationDbContext,
        long consultationId,
        long userProfileId,
        bool trackChanges,
        CancellationToken cancellationToken)
    {
        IQueryable<Consultation> query = applicationDbContext.Consultations
            .Include(consultation => consultation.Appointment)
                .ThenInclude(appointment => appointment.PatientProfile)
                    .ThenInclude(profile => profile.UserProfile)
            .Include(consultation => consultation.Appointment)
                .ThenInclude(appointment => appointment.AvailabilitySlot)
                    .ThenInclude(slot => slot.ClinicianProfile)
                        .ThenInclude(profile => profile.UserProfile);

        if (!trackChanges)
        {
            query = query.AsNoTracking();
        }

        return await query.SingleOrDefaultAsync(
            consultation =>
                consultation.Id == consultationId &&
                consultation.Appointment.AvailabilitySlot.ClinicianProfile.UserProfileId ==
                    userProfileId &&
                consultation.Appointment.AvailabilitySlot.ClinicianProfile.UserProfile.Status ==
                    AccountStatus.Active &&
                consultation.Appointment.AvailabilitySlot.ClinicianProfile.UserProfile.ProfileType ==
                    ProfileType.Doctor,
            cancellationToken);
    }

    public static async Task<ApplicationResult<DoctorConsultationDetails>> LoadDetailsAsync(
        IApplicationDbContext applicationDbContext,
        long consultationId,
        long userProfileId,
        CancellationToken cancellationToken)
    {
        Consultation? consultation = await LoadOwnedAsync(
            applicationDbContext,
            consultationId,
            userProfileId,
            trackChanges: false,
            cancellationToken);

        if (consultation is null)
        {
            return ApplicationResult.NotFound<DoctorConsultationDetails>(
                "consultation_not_found",
                "The consultation was not found.");
        }

        DoctorPrescriptionSummary[] prescriptions = await DoctorPrescriptionProjection.LoadAsync(
            applicationDbContext,
            consultation.Id,
            cancellationToken);

        return ApplicationResult.Success(DoctorConsultationDetails.FromEntities(
            consultation,
            prescriptions));
    }

    public static async Task<ApplicationResult<DoctorConsultationDetails>> ConcurrencyConflictAsync(
        IApplicationDbContext applicationDbContext,
        long consultationId,
        long userProfileId,
        CancellationToken cancellationToken)
    {
        var current = await applicationDbContext.Consultations
            .AsNoTracking()
            .Where(consultation =>
                consultation.Id == consultationId &&
                consultation.Appointment.AvailabilitySlot.ClinicianProfile.UserProfileId ==
                    userProfileId)
            .Select(consultation => new
            {
                consultation.Status,
                consultation.Version,
            })
            .SingleOrDefaultAsync(cancellationToken);

        return current is null
            ? ApplicationResult.NotFound<DoctorConsultationDetails>(
                "consultation_not_found",
                "The consultation was not found.")
            : ApplicationResult.Conflict<DoctorConsultationDetails>(
                "consultation_changed",
                $"This consultation is now {current.Status} at version {current.Version}. Refresh it before trying again.");
    }
}
