using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlantFlow.Api.Data;
using PlantFlow.Api.Domain.Planning;
using PlantFlow.Api.Domain.Security;
using PlantFlow.Api.Infrastructure;
using PlantFlow.Api.Services;

namespace PlantFlow.Api.Controllers;

public record SiteSurveyDto(
    Guid Id, Guid SiteId, DateOnly SurveyedOn, SurveyResult Result, string? Notes, Guid? SurveyedBy, DateTime CreatedAtUtc);

public class SiteSurveyRequest
{
    [Required]
    public DateOnly? SurveyedOn { get; init; }

    [Required]
    public SurveyResult? Result { get; init; }

    [StringLength(1000)]
    public string? Notes { get; init; }
}

[ApiController]
[Route("api/sites/{siteId:guid}/surveys")]
public class SiteSurveysController(PlantFlowDbContext db, ICurrentUser currentUser, AuditWriter audit) : ControllerBase
{
    // Newest first. Planning looks at the latest one.
    [HttpGet]
    [HasPermission(Permissions.SiteSurveysView)]
    public async Task<ActionResult<List<SiteSurveyDto>>> List(Guid siteId, CancellationToken cancellationToken)
    {
        if (!await db.Sites.AnyAsync(s => s.Id == siteId, cancellationToken)) return NotFound();

        return await db.SiteSurveys.AsNoTracking().Where(s => s.SiteId == siteId)
            .OrderByDescending(s => s.SurveyedOn).ThenByDescending(s => s.CreatedAtUtc)
            .Select(s => new SiteSurveyDto(s.Id, s.SiteId, s.SurveyedOn, s.Result, s.Notes, s.SurveyedBy, s.CreatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    [HttpPost]
    [HasPermission(Permissions.SiteSurveysPerform)]
    public async Task<ActionResult<SiteSurveyDto>> Create(Guid siteId, SiteSurveyRequest request, CancellationToken cancellationToken)
    {
        if (!await db.Sites.AnyAsync(s => s.Id == siteId, cancellationToken)) return NotFound();

        var survey = new SiteSurvey
        {
            TenantId = currentUser.TenantId!.Value,
            SiteId = siteId,
            SurveyedOn = request.SurveyedOn!.Value,
            Result = request.Result!.Value,
            Notes = request.Notes?.Trim(),
            SurveyedBy = currentUser.UserId
        };

        db.SiteSurveys.Add(survey);
        audit.Record(survey.TenantId, "sitesurvey.created", "SiteSurvey", survey.Id.ToString(),
            new { survey.SiteId, survey.SurveyedOn, Result = survey.Result.ToString() });
        await db.SaveChangesAsync(cancellationToken);

        var dto = new SiteSurveyDto(survey.Id, survey.SiteId, survey.SurveyedOn, survey.Result, survey.Notes, survey.SurveyedBy, survey.CreatedAtUtc);
        return Created($"api/sites/{siteId}/surveys", dto);
    }
}