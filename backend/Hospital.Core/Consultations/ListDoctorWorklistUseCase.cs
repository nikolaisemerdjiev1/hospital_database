using Hospital.Core.Application;
using Hospital.Core.Persistence;
using Hospital.Core.Profiles;
using Hospital.Core.Scheduling;

using Microsoft.EntityFrameworkCore;

namespace Hospital.Core.Consultations;

public sealed class ListDoctorWorklistUseCase(IApplicationDbContext applicationDbContext)
{
    private static readonly TimeSpan MaximumWindow = TimeSpan.FromDays(62);

    public async Task<ApplicationResult<DoctorWorklistPage>> ExecuteAsync(
        long userProfileId,
        DateTimeOffset from,
        DateTimeOffset to,
        int page,
        int pageSize,
        AppointmentStatus? status,
        CancellationToken cancellationToken = default)
    {
        if (to <= from || to - from > MaximumWindow)
        {
            return ApplicationResult.Validation<DoctorWorklistPage>(
                "invalid_worklist_window",
                "The worklist window must end after it starts and cannot exceed 62 days.");
        }

        if (page < 1 || pageSize is < 1 or > 50)
        {
            return ApplicationResult.Validation<DoctorWorklistPage>(
                "invalid_pagination",
                "Page must be at least 1 and pageSize must be between 1 and 50.");
        }

        long itemsToSkip = (long)(page - 1) * pageSize;
        if (itemsToSkip > int.MaxValue)
        {
            return ApplicationResult.Validation<DoctorWorklistPage>(
                "invalid_pagination",
                "The requested page is outside the supported pagination range.");
        }

        long? clinicianProfileId = await applicationDbContext.ClinicianProfiles
            .AsNoTracking()
            .Where(profile =>
                profile.UserProfileId == userProfileId &&
                profile.UserProfile.Status == AccountStatus.Active &&
                profile.UserProfile.ProfileType == ProfileType.Doctor)
            .Select(profile => (long?)profile.Id)
            .SingleOrDefaultAsync(cancellationToken);

        if (!clinicianProfileId.HasValue)
        {
            return ApplicationResult.NotFound<DoctorWorklistPage>(
                "clinician_profile_not_found",
                "The clinician profile was not found.");
        }

        IQueryable<Appointment> query = applicationDbContext.Appointments
            .AsNoTracking()
            .Where(appointment =>
                appointment.AvailabilitySlot.ClinicianProfileId == clinicianProfileId.Value &&
                appointment.AvailabilitySlot.StartsAtUtc >= from &&
                appointment.AvailabilitySlot.StartsAtUtc < to);

        if (status.HasValue)
        {
            query = query.Where(appointment => appointment.Status == status.Value);
        }

        int totalItems = await query.CountAsync(cancellationToken);
        WorklistRow[] rows = await query
            .OrderBy(appointment => appointment.Status == AppointmentStatus.InProgress
                ? 0
                : appointment.Status == AppointmentStatus.Scheduled
                    ? 1
                    : 2)
            .ThenBy(appointment =>
                appointment.Status == AppointmentStatus.InProgress ||
                appointment.Status == AppointmentStatus.Scheduled
                    ? appointment.AvailabilitySlot.StartsAtUtc
                    : DateTimeOffset.MaxValue)
            .ThenByDescending(appointment =>
                appointment.Status == AppointmentStatus.InProgress ||
                appointment.Status == AppointmentStatus.Scheduled
                    ? DateTimeOffset.MinValue
                    : appointment.AvailabilitySlot.StartsAtUtc)
            .ThenBy(appointment => appointment.Id)
            .Skip((int)itemsToSkip)
            .Take(pageSize)
            .Select(appointment => new WorklistRow(
                appointment.Id,
                appointment.PatientProfile.UserProfile.DisplayName,
                appointment.AvailabilitySlot.StartsAtUtc,
                appointment.AvailabilitySlot.EndsAtUtc,
                appointment.Reason,
                appointment.Status,
                appointment.Version))
            .ToArrayAsync(cancellationToken);

        long[] appointmentIds = rows.Select(row => row.AppointmentId).ToArray();
        Dictionary<long, ConsultationReference> consultations = await applicationDbContext
            .Consultations
            .AsNoTracking()
            .Where(consultation => appointmentIds.Contains(consultation.AppointmentId))
            .Select(consultation => new
            {
                consultation.AppointmentId,
                Reference = new ConsultationReference(
                    consultation.Id,
                    consultation.Status.ToString(),
                    consultation.Version),
            })
            .ToDictionaryAsync(
                item => item.AppointmentId,
                item => item.Reference,
                cancellationToken);

        DoctorWorklistItem[] items = rows
            .Select(row => new DoctorWorklistItem(
                row.AppointmentId,
                row.PatientDisplayName,
                row.StartsAtUtc,
                row.EndsAtUtc,
                row.Reason,
                row.Status.ToString(),
                row.Version,
                consultations.GetValueOrDefault(row.AppointmentId)))
            .ToArray();

        int totalPages = totalItems == 0
            ? 0
            : (int)Math.Ceiling(totalItems / (double)pageSize);

        return ApplicationResult.Success(new DoctorWorklistPage(
            items,
            page,
            pageSize,
            totalItems,
            totalPages));
    }

    private sealed record WorklistRow(
        long AppointmentId,
        string PatientDisplayName,
        DateTimeOffset StartsAtUtc,
        DateTimeOffset EndsAtUtc,
        string Reason,
        AppointmentStatus Status,
        uint Version);
}
