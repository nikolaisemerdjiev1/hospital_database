using Hospital.Api.Contracts;
using Hospital.Core.Pharmacy;

namespace Hospital.Api.Controllers;

public abstract class PharmacyControllerBase : ApplicationControllerBase
{
    protected static PharmacyFulfillmentResponse ToResponse(
        PharmacyFulfillmentDetails fulfillment) =>
        new(
            fulfillment.Id,
            fulfillment.Status,
            fulfillment.CreatedAtUtc,
            fulfillment.ReviewStartedAtUtc,
            fulfillment.ReadyAtUtc,
            fulfillment.DispensedAtUtc,
            fulfillment.CancelledAtUtc,
            fulfillment.Version,
            fulfillment.AssignedToCurrentPharmacist,
            new PharmacyPatientContextResponse(
                fulfillment.PatientDisplayName,
                fulfillment.AllergySummary),
            new PharmacyPrescriptionContextResponse(
                fulfillment.PrescriptionId,
                fulfillment.RxCui,
                fulfillment.MedicationDisplayName,
                fulfillment.Dose,
                fulfillment.Instructions,
                fulfillment.Quantity,
                fulfillment.PrescriptionStatus,
                fulfillment.IssuedAtUtc,
                fulfillment.PrescriptionCancelledAtUtc,
                fulfillment.PrescriberDisplayName));
}
