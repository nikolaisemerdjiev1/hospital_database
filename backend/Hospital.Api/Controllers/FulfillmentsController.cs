using Hospital.Api.Authentication;
using Hospital.Api.Contracts;
using Hospital.Core.Application;
using Hospital.Core.Pharmacy;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hospital.Api.Controllers;

[ApiController]
[Authorize(Policy = AuthorizationPolicies.Pharmacist)]
[Route("api/v1/fulfillments")]
public sealed class FulfillmentsController(
    ILocalUserResolver localUserResolver,
    ListPharmacyWorkQueueUseCase listWorkQueue,
    GetPharmacyFulfillmentUseCase getFulfillment,
    TransitionFulfillmentUseCase transitionFulfillment) : PharmacyControllerBase
{
    [HttpGet]
    [ProducesResponseType<PharmacyWorkQueuePageResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PharmacyWorkQueuePageResponse>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseStatus(status, out FulfillmentStatus? parsedStatus))
        {
            return Problem(
                detail: "Status must be Pending, InReview, Ready, Dispensed, or Cancelled.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "The fulfillment status is invalid.",
                type: "https://www.rfc-editor.org/rfc/rfc9110#name-400-bad-request",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "invalid_fulfillment_status",
                });
        }

        ResolvedLocalUser? localUser = await localUserResolver.ResolveAsync(
            User,
            cancellationToken);
        if (localUser is null)
        {
            return Forbid();
        }

        ApplicationResult<PharmacyWorkQueuePage> result = await listWorkQueue.ExecuteAsync(
            localUser.UserProfileId,
            page,
            pageSize,
            parsedStatus,
            cancellationToken);
        if (!result.IsSuccess)
        {
            return ApplicationProblem(result);
        }

        PharmacyWorkQueuePage workQueue = result.Value!;
        return Ok(new PharmacyWorkQueuePageResponse(
            workQueue.Items.Select(static item => new PharmacyWorkQueueItemResponse(
                item.FulfillmentId,
                item.PrescriptionId,
                item.PatientDisplayName,
                item.MedicationDisplayName,
                item.Dose,
                item.Quantity,
                item.IssuedAtUtc,
                item.FulfillmentStatus,
                item.CreatedAtUtc,
                item.Version))
                .ToArray(),
            workQueue.Page,
            workQueue.PageSize,
            workQueue.TotalItems,
            workQueue.TotalPages));
    }

    [HttpGet("{fulfillmentId:long}")]
    [ProducesResponseType<PharmacyFulfillmentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PharmacyFulfillmentResponse>> Get(
        long fulfillmentId,
        CancellationToken cancellationToken = default)
    {
        ResolvedLocalUser? localUser = await localUserResolver.ResolveAsync(
            User,
            cancellationToken);
        if (localUser is null)
        {
            return Forbid();
        }

        ApplicationResult<PharmacyFulfillmentDetails> result = await getFulfillment.ExecuteAsync(
            localUser.UserProfileId,
            fulfillmentId,
            cancellationToken);

        return result.IsSuccess
            ? Ok(ToResponse(result.Value!))
            : ApplicationProblem(result);
    }

    [HttpPost("{fulfillmentId:long}/transitions")]
    [ProducesResponseType<PharmacyFulfillmentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PharmacyFulfillmentResponse>> Transition(
        long fulfillmentId,
        TransitionFulfillmentRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseTransitionTarget(request.TargetStatus, out FulfillmentStatus targetStatus))
        {
            return Problem(
                detail: "Target status must be InReview, Ready, or Dispensed.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "The fulfillment transition target is invalid.",
                type: "https://www.rfc-editor.org/rfc/rfc9110#name-400-bad-request",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "invalid_fulfillment_transition_target",
                });
        }

        ResolvedLocalUser? localUser = await localUserResolver.ResolveAsync(
            User,
            cancellationToken);
        if (localUser is null)
        {
            return Forbid();
        }

        ApplicationResult<PharmacyFulfillmentDetails> result = await transitionFulfillment
            .ExecuteAsync(
                localUser.UserProfileId,
                fulfillmentId,
                targetStatus,
                request.ExpectedVersion,
                GetRequestTraceId(),
                cancellationToken);

        return result.IsSuccess
            ? Ok(ToResponse(result.Value!))
            : ApplicationProblem(result);
    }

    private static bool TryParseStatus(
        string? value,
        out FulfillmentStatus? fulfillmentStatus)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            fulfillmentStatus = null;
            return true;
        }

        bool parsed = Enum.TryParse(value, ignoreCase: true, out FulfillmentStatus status) &&
            Enum.IsDefined(status);
        fulfillmentStatus = parsed ? status : null;
        return parsed;
    }

    private static bool TryParseTransitionTarget(
        string? value,
        out FulfillmentStatus fulfillmentStatus)
    {
        if (string.Equals(value, nameof(FulfillmentStatus.InReview), StringComparison.OrdinalIgnoreCase))
        {
            fulfillmentStatus = FulfillmentStatus.InReview;
            return true;
        }

        if (string.Equals(value, nameof(FulfillmentStatus.Ready), StringComparison.OrdinalIgnoreCase))
        {
            fulfillmentStatus = FulfillmentStatus.Ready;
            return true;
        }

        if (string.Equals(value, nameof(FulfillmentStatus.Dispensed), StringComparison.OrdinalIgnoreCase))
        {
            fulfillmentStatus = FulfillmentStatus.Dispensed;
            return true;
        }

        fulfillmentStatus = default;
        return false;
    }
}
