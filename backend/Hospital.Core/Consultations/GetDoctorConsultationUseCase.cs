using Hospital.Core.Application;
using Hospital.Core.Persistence;

namespace Hospital.Core.Consultations;

public sealed class GetDoctorConsultationUseCase(IApplicationDbContext applicationDbContext)
{
    public Task<ApplicationResult<DoctorConsultationDetails>> ExecuteAsync(
        long userProfileId,
        long consultationId,
        CancellationToken cancellationToken = default)
    {
        if (consultationId <= 0)
        {
            return Task.FromResult(ApplicationResult.NotFound<DoctorConsultationDetails>(
                "consultation_not_found",
                "The consultation was not found."));
        }

        return DoctorConsultationAccess.LoadDetailsAsync(
            applicationDbContext,
            consultationId,
            userProfileId,
            cancellationToken);
    }
}
