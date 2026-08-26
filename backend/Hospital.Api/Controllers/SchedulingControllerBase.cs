using Hospital.Api.Contracts;
using Hospital.Core.Scheduling;

using Microsoft.AspNetCore.Mvc;

namespace Hospital.Api.Controllers;

public abstract class SchedulingControllerBase : ApplicationControllerBase
{
    protected static AppointmentResponse ToResponse(AppointmentSummary appointment) =>
        new(
            appointment.Id,
            appointment.ClinicianId,
            appointment.ClinicianDisplayName,
            appointment.ClinicianSpecialty,
            appointment.StartsAtUtc,
            appointment.EndsAtUtc,
            appointment.Reason,
            appointment.Status,
            appointment.CancelledAtUtc,
            appointment.CancellationReason,
            appointment.Version);
}
