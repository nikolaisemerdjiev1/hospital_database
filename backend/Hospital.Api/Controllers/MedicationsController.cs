using Hospital.Api.Authentication;
using Hospital.Api.Contracts;
using Hospital.Core.Application;
using Hospital.Core.Medications;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hospital.Api.Controllers;

[ApiController]
[Authorize(Policy = AuthorizationPolicies.Doctor)]
[Route("api/v1/medications")]
public sealed class MedicationsController(
    SearchMedicationCatalogUseCase searchMedicationCatalog) : ApplicationControllerBase
{
    [HttpGet]
    [ProducesResponseType<MedicationCatalogSearchResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<MedicationCatalogSearchResponse>> Search(
        [FromQuery] string? query,
        [FromQuery] int limit = SearchMedicationCatalogUseCase.DefaultLimit,
        CancellationToken cancellationToken = default)
    {
        ApplicationResult<MedicationCatalogSearch> result = await searchMedicationCatalog
            .ExecuteAsync(query, limit, cancellationToken);
        if (!result.IsSuccess)
        {
            return ApplicationProblem(result);
        }

        MedicationCatalogSearch search = result.Value!;
        return Ok(new MedicationCatalogSearchResponse(
            search.Items.Select(static medication => new MedicationCatalogItemResponse(
                medication.MedicationId,
                medication.RxCui,
                medication.DisplayName,
                medication.ConceptType,
                medication.Strength,
                medication.DoseForm,
                medication.Source))
                .ToArray(),
            search.CatalogStatus.ToString()));
    }
}
