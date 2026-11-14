using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlantFlow.Api.Data;
using PlantFlow.Api.Domain.Planning;
using PlantFlow.Api.Domain.Security;
using PlantFlow.Api.Infrastructure;
using PlantFlow.Api.Services;

namespace PlantFlow.Api.Controllers;

public record SurveyImageDto(Guid Id, string FileName, string ContentType, int SizeBytes, string? Caption);

public record SiteSurveyDto(
    Guid Id, Guid SiteId, string SiteName, DateOnly SurveyedOn, SurveyResult Result, string? Notes,
    Guid? SurveyedBy, DateTime CreatedAtUtc, decimal? Latitude, decimal? Longitude, decimal? AccuracyMeters,
    decimal? PipingMetres, string? PumpPosition, string? TruckConnectionPoint, string? LocationNote,
    List<SurveyImageDto> Images);

public class SiteSurveyRequest
{
    [Required]
    public DateOnly? SurveyedOn { get; init; }

    [Required]
    public SurveyResult? Result { get; init; }

    [StringLength(1000)]
    public string? Notes { get; init; }

    // From the surveyor's device, at most 6 decimals.
    [Required, Range(-90.0, 90.0)]
    public decimal? Latitude { get; init; }

    [Required, Range(-180.0, 180.0)]
    public decimal? Longitude { get; init; }

    // How accurate the position is, in metres.
    [Required, Range(0.0, 100000.0)]
    public decimal? AccuracyMeters { get; init; }

    // How much piping is needed, at most 1 decimal.
    [Range(0.0, 10000.0)]
    public decimal? PipingMetres { get; init; }

    [StringLength(500)]
    public string? PumpPosition { get; init; }

    [StringLength(500)]
    public string? TruckConnectionPoint { get; init; }

    // How to find the place.
    [StringLength(1000)]
    public string? LocationNote { get; init; }
}

[ApiController]
public class SiteSurveysController(PlantFlowDbContext db, ICurrentUser currentUser, AuditWriter audit) : ControllerBase
{
    private const int MaxImages = 3;
    private const int MaxImageBytes = 5 * 1024 * 1024;

    // Newest first. Planning looks at the latest one unless an order tags another.
    [HttpGet("api/sites/{siteId:guid}/surveys")]
    [HasPermission(Permissions.SiteSurveysView)]
    public async Task<ActionResult<List<SiteSurveyDto>>> ForSite(Guid siteId, CancellationToken cancellationToken)
    {
        if (!await db.Sites.AnyAsync(s => s.Id == siteId, cancellationToken)) return NotFound();

        return await LoadAsync(db.SiteSurveys.AsNoTracking().Where(s => s.SiteId == siteId), null, cancellationToken);
    }

    // The customer's last three surveys across all their sites. A new order can tag one of them.
    [HttpGet("api/customers/{customerId:guid}/surveys")]
    [HasPermission(Permissions.SiteSurveysView)]
    public async Task<ActionResult<List<SiteSurveyDto>>> ForCustomer(Guid customerId, CancellationToken cancellationToken)
    {
        if (!await db.Customers.AnyAsync(c => c.Id == customerId, cancellationToken)) return NotFound();

        return await LoadAsync(
            db.SiteSurveys.AsNoTracking().Where(s => s.Site.CustomerId == customerId), 3, cancellationToken);
    }

    [HttpPost("api/sites/{siteId:guid}/surveys")]
    [HasPermission(Permissions.SiteSurveysPerform)]
    public async Task<ActionResult<SiteSurveyDto>> Create(Guid siteId, SiteSurveyRequest request, CancellationToken cancellationToken)
    {
        var site = await db.Sites.FirstOrDefaultAsync(s => s.Id == siteId, cancellationToken);
        if (site is null) return NotFound();

        var latitude = request.Latitude!.Value;
        var longitude = request.Longitude!.Value;
        if (latitude != Math.Round(latitude, 6) || longitude != Math.Round(longitude, 6)
            || (request.PipingMetres is { } piping && piping != Math.Round(piping, 1))
            || request.AccuracyMeters!.Value != Math.Round(request.AccuracyMeters.Value, 1))
        {
            return Problem(title: "Coordinates can have at most 6 decimals, and metres at most 1.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var survey = new SiteSurvey
        {
            TenantId = site.TenantId,
            SiteId = siteId,
            SurveyedOn = request.SurveyedOn!.Value,
            Result = request.Result!.Value,
            Notes = request.Notes?.Trim(),
            Latitude = latitude,
            Longitude = longitude,
            AccuracyMeters = request.AccuracyMeters,
            PipingMetres = request.PipingMetres,
            PumpPosition = request.PumpPosition?.Trim(),
            TruckConnectionPoint = request.TruckConnectionPoint?.Trim(),
            LocationNote = request.LocationNote?.Trim(),
            SurveyedBy = currentUser.UserId
        };

        // A site with no position of its own takes the one from its first survey.
        if (site.Latitude is null && site.Longitude is null)
        {
            site.Latitude = latitude;
            site.Longitude = longitude;
        }

        db.SiteSurveys.Add(survey);
        audit.Record(survey.TenantId, "sitesurvey.created", "SiteSurvey", survey.Id.ToString(),
            new { survey.SiteId, survey.SurveyedOn, Result = survey.Result.ToString(), survey.Latitude, survey.Longitude });
        await db.SaveChangesAsync(cancellationToken);

        var dto = (await LoadAsync(db.SiteSurveys.AsNoTracking().Where(s => s.Id == survey.Id), null, cancellationToken)).Single();
        return Created($"api/sites/{siteId}/surveys", dto);
    }

    // Up to three photos per survey: JPEG, PNG or WebP, at most 5 MB each. Send it as form data with a "file" part.
    [HttpPost("api/surveys/{id:guid}/images")]
    [HasPermission(Permissions.SiteSurveysAttach)]
    [RequestSizeLimit(6_000_000)]
    public async Task<IActionResult> AddImage(Guid id, IFormFile file, [FromForm] string? caption, CancellationToken cancellationToken)
    {
        var survey = await db.SiteSurveys.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (survey is null) return NotFound();

        if (await db.SiteSurveyImages.CountAsync(i => i.SurveyId == id, cancellationToken) >= MaxImages)
        {
            return Problem(title: $"A survey can have at most {MaxImages} photos.", statusCode: StatusCodes.Status409Conflict);
        }

        if (file.Length == 0 || file.Length > MaxImageBytes)
        {
            return Problem(title: "A photo must be between 1 byte and 5 MB.", statusCode: StatusCodes.Status400BadRequest);
        }

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();

        // The type comes from the file itself, not from what the client says it is.
        var contentType = DetectImageType(bytes);
        if (contentType is null)
        {
            return Problem(title: "Only JPEG, PNG or WebP photos are accepted.", statusCode: StatusCodes.Status400BadRequest);
        }

        var name = Path.GetFileName(file.FileName);
        var image = new SiteSurveyImage
        {
            TenantId = survey.TenantId,
            SurveyId = id,
            FileName = string.IsNullOrWhiteSpace(name) ? "photo" : name.Trim()[..Math.Min(name.Trim().Length, 200)],
            ContentType = contentType,
            SizeBytes = bytes.Length,
            Caption = string.IsNullOrWhiteSpace(caption) ? null : caption.Trim()[..Math.Min(caption.Trim().Length, 200)],
            Content = bytes
        };

        db.SiteSurveyImages.Add(image);
        audit.Record(survey.TenantId, "sitesurvey.image_added", "SiteSurvey", id.ToString(), new { image.FileName, image.SizeBytes });
        await db.SaveChangesAsync(cancellationToken);

        return Created($"api/surveys/{id}/images/{image.Id}",
            new SurveyImageDto(image.Id, image.FileName, image.ContentType, image.SizeBytes, image.Caption));
    }

    [HttpGet("api/surveys/{id:guid}/images/{imageId:guid}")]
    [HasPermission(Permissions.SiteSurveysView)]
    public async Task<IActionResult> GetImage(Guid id, Guid imageId, CancellationToken cancellationToken)
    {
        var image = await db.SiteSurveyImages.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == imageId && i.SurveyId == id, cancellationToken);
        if (image is null) return NotFound();

        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(image.Content, image.ContentType);
    }

    [HttpDelete("api/surveys/{id:guid}/images/{imageId:guid}")]
    [HasPermission(Permissions.SiteSurveysAttach)]
    public async Task<IActionResult> RemoveImage(Guid id, Guid imageId, CancellationToken cancellationToken)
    {
        var image = await db.SiteSurveyImages.FirstOrDefaultAsync(i => i.Id == imageId && i.SurveyId == id, cancellationToken);
        if (image is null) return NotFound();

        db.SiteSurveyImages.Remove(image);
        audit.Record(image.TenantId, "sitesurvey.image_removed", "SiteSurvey", id.ToString(), new { image.FileName });
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private static string? DetectImageType(byte[] b)
    {
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return "image/jpeg";

        if (b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47
            && b[4] == 0x0D && b[5] == 0x0A && b[6] == 0x1A && b[7] == 0x0A) return "image/png";

        if (b.Length >= 12 && b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F'
            && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P') return "image/webp";

        return null;
    }

    // Newest first. The photos come back as details only. Each one is fetched on its own.
    private async Task<List<SiteSurveyDto>> LoadAsync(IQueryable<SiteSurvey> query, int? take, CancellationToken cancellationToken)
    {
        query = query.OrderByDescending(s => s.SurveyedOn).ThenByDescending(s => s.CreatedAtUtc);
        if (take is { } limit) query = query.Take(limit);

        var rows = await query.Select(s => new
        {
            s.Id,
            s.SiteId,
            SiteName = s.Site.Name,
            s.SurveyedOn,
            s.Result,
            s.Notes,
            s.SurveyedBy,
            s.CreatedAtUtc,
            s.Latitude,
            s.Longitude,
            s.AccuracyMeters,
            s.PipingMetres,
            s.PumpPosition,
            s.TruckConnectionPoint,
            s.LocationNote
        }).ToListAsync(cancellationToken);

        var ids = rows.Select(r => r.Id).ToList();
        var images = await db.SiteSurveyImages.AsNoTracking().Where(i => ids.Contains(i.SurveyId))
            .OrderBy(i => i.CreatedAtUtc)
            .Select(i => new { i.SurveyId, i.Id, i.FileName, i.ContentType, i.SizeBytes, i.Caption })
            .ToListAsync(cancellationToken);

        return rows.Select(r => new SiteSurveyDto(
            r.Id, r.SiteId, r.SiteName, r.SurveyedOn, r.Result, r.Notes, r.SurveyedBy, r.CreatedAtUtc,
            r.Latitude, r.Longitude, r.AccuracyMeters, r.PipingMetres, r.PumpPosition, r.TruckConnectionPoint, r.LocationNote,
            images.Where(i => i.SurveyId == r.Id)
                .Select(i => new SurveyImageDto(i.Id, i.FileName, i.ContentType, i.SizeBytes, i.Caption)).ToList())).ToList();
    }
}