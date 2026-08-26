using Hospital.Core.Application;
using Hospital.Core.Persistence;

namespace Hospital.Core.Prescriptions;

public sealed class GetDoctorPrescriptionUseCase(IApplicationDbContext applicationDbContext)
{
    public Task<ApplicationResult<DoctorPrescriptionDetails>> ExecuteAsync(
        long doctorUserProfileId,
        long prescriptionId,
        CancellationToken cancellationToken = default)
    {
        if (prescriptionId <= 0)
        {
            return Task.FromResult(ApplicationResult.NotFound<DoctorPrescriptionDetails>(
                "prescription_not_found",
                "The prescription was not found."));
        }

        return PrescriptionAccess.GetAsync(
            applicationDbContext,
            prescriptionId,
            doctorUserProfileId,
            cancellationToken);
    }
}
