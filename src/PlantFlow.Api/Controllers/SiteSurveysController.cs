using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlantFlow.Api.Data;
using PlantFlow.Api.Domain.Orders;
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
    List<SurveyImageDto> Images, Guid? OrderId, string? OrderNumber,
    SurveyStatus Status, Guid? ReviewedBy, DateTime? ReviewedAtUtc, string? ReviewComment);

// An order waiting for a new survey, as the surveyor sees it in the work queue.
public record SurveyRequestDto(
    Guid OrderId, string OrderNumber, OrderStatus OrderStatus, string CustomerName, Guid SiteId, string SiteName,
    string? SiteAddress, decimal? SiteLatitude, decimal? SiteLongitude, string? SiteAccessNotes,
    Guid PlantId, string PlantCode, DateOnly RequestedDate, DateTime? SurveyRequestedAtUtc, bool HasPumpLines,
    string? SentBackReason);

// A survey waiting for a plant manager, with the order it was recorded for.
public record SurveyApprovalDto(
    SiteSurveyDto Survey, string CustomerName, Guid PlantId, string PlantCode, DateOnly RequestedDate);

// ApprovesForAnyPlant is false for an approver with no plants assigned, so the screen can say why the list is empty.
public record SurveyApprovalsDto(bool ApprovesForAnyPlant, List<SurveyApprovalDto> Surveys);

public class SendBackSurveyRequest
{
    [Required, StringLength(500, MinimumLength = 3)]
    public string? Reason { get; init; }
}

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
public class SiteSurveysController(
    PlantFlowDbContext db, ICurrentUser currentUser, AuditWriter audit, PlantScope plantScope) : ControllerBase
{
    private const int MaxImages = 3;
    private const int MaxImageBytes = 5 * 1024 * 1024;

    // Newest first. An order can tag one of the first three; planning uses only the tagged survey.
    [HttpGet("api/sites/{siteId:guid}/surveys")]
    [HasPermission(Permissions.SiteSurveysView)]
    public async Task<ActionResult<List<SiteSurveyDto>>> ForSite(Guid siteId, CancellationToken cancellationToken)
    {
        if (!await db.Sites.AnyAsync(s => s.Id == siteId, cancellationToken)) return NotFound();

        return await LoadAsync(db.SiteSurveys.AsNoTracking().Where(s => s.SiteId == siteId), null, cancellationToken);
    }

    // The surveyor work queue: open orders waiting for a new survey, for the plants the surveyor works for
    // (all plants when they have none assigned). The soonest pour date comes first, then the oldest request.
    [HttpGet("api/survey-requests")]
    [HasPermission(Permissions.SiteSurveysPerform)]
    public async Task<ActionResult<List<SurveyRequestDto>>> Requests(CancellationToken cancellationToken)
    {
        var plants = await plantScope.PlantsOfAsync(currentUser.UserId, cancellationToken);

        return await RequestQuery(plants).ToListAsync(cancellationToken);
    }

    [HttpGet("api/survey-requests/{orderId:guid}")]
    [HasPermission(Permissions.SiteSurveysPerform)]
    public async Task<ActionResult<SurveyRequestDto>> RequestFor(Guid orderId, CancellationToken cancellationToken)
    {
        var plants = await plantScope.PlantsOfAsync(currentUser.UserId, cancellationToken);
        var request = await RequestQuery(plants, orderId).FirstOrDefaultAsync(cancellationToken);
        return request is null ? NotFound() : request;
    }

    // Records the survey an order asked for. It is linked to the order and becomes the order's tagged survey.
    // It waits for a plant manager's approval before planning can rely on it.
    [HttpPost("api/orders/{orderId:guid}/surveys")]
    [HasPermission(Permissions.SiteSurveysPerform)]
    public async Task<ActionResult<SiteSurveyDto>> Create(Guid orderId, SiteSurveyRequest request, CancellationToken cancellationToken)
    {
        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);
        if (order is null) return NotFound();

        if (!order.SurveyRequested)
        {
            return Problem(title: "This order has not asked for a new survey. Request one on the order first.",
                statusCode: StatusCodes.Status409Conflict);
        }

        if (!OrderSurveys.OpenStatuses.Contains(order.Status))
        {
            return Problem(title: $"This order is {order.Status}, so it no longer needs a survey.",
                statusCode: StatusCodes.Status409Conflict);
        }

        if (!await plantScope.CoversAsync(currentUser.UserId, order.PlantId, cancellationToken))
        {
            return Problem(title: "This order is for a plant you do not work for.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        var siteId = order.SiteId;
        var site = await db.Sites.FirstAsync(s => s.Id == siteId, cancellationToken);

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
            OrderId = order.Id,
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

        // The request is answered: the order now relies on this survey.
        db.SiteSurveys.Add(survey);
        OrderSurveys.Apply(order, survey.Id, requestNew: false, currentUser.UserId);

        audit.Record(survey.TenantId, "sitesurvey.created", "SiteSurvey", survey.Id.ToString(),
            new { survey.SiteId, survey.OrderId, survey.SurveyedOn, Result = survey.Result.ToString(), survey.Latitude, survey.Longitude });
        await db.SaveChangesAsync(cancellationToken);

        var dto = (await LoadAsync(db.SiteSurveys.AsNoTracking().Where(s => s.Id == survey.Id), null, cancellationToken)).Single();
        return Created($"api/sites/{siteId}/surveys", dto);
    }

    // Surveys waiting for approval at the plants the user approves for, oldest first.
    [HttpGet("api/survey-approvals")]
    [HasPermission(Permissions.SiteSurveysApprove)]
    public async Task<ActionResult<SurveyApprovalsDto>> Approvals(CancellationToken cancellationToken)
    {
        var plants = await plantScope.ApprovesForAsync(currentUser.UserId, cancellationToken);
        if (plants is { Count: 0 }) return new SurveyApprovalsDto(false, []);

        var pending = db.SiteSurveys.AsNoTracking()
            .Where(s => s.Status == SurveyStatus.Pending && s.Order != null)
            .Where(s => plants == null || plants.Contains(s.Order!.PlantId));

        var orders = await pending.Select(s => new
        {
            s.Id,
            CustomerName = s.Order!.Customer.Name,
            s.Order.PlantId,
            PlantCode = s.Order.Plant.Code,
            s.Order.RequestedDate
        }).ToListAsync(cancellationToken);
        var surveys = await LoadAsync(pending, null, cancellationToken);

        var items = surveys.OrderBy(s => s.CreatedAtUtc).Select(s =>
        {
            var o = orders.Single(x => x.Id == s.Id);
            return new SurveyApprovalDto(s, o.CustomerName, o.PlantId, o.PlantCode, o.RequestedDate);
        }).ToList();
        return new SurveyApprovalsDto(true, items);
    }

    // Approves a survey for planning. The plant manager may approve a survey they recorded themselves.
    [HttpPost("api/surveys/{id:guid}/approve")]
    [HasPermission(Permissions.SiteSurveysApprove)]
    public async Task<ActionResult<SiteSurveyDto>> Approve(Guid id, CancellationToken cancellationToken)
    {
        var (survey, problem) = await ReviewableAsync(id, cancellationToken);
        if (problem is not null) return problem;

        survey!.Status = SurveyStatus.Approved;
        survey.ReviewedBy = currentUser.UserId;
        survey.ReviewedAtUtc = DateTime.UtcNow;

        audit.Record(survey.TenantId, "sitesurvey.approved", "SiteSurvey", survey.Id.ToString(),
            new { survey.OrderId, Result = survey.Result.ToString() });
        await db.SaveChangesAsync(cancellationToken);

        return (await LoadAsync(db.SiteSurveys.AsNoTracking().Where(s => s.Id == id), null, cancellationToken)).Single();
    }

    // Sends a survey back. It stays as history, and the order that asked for it returns to the surveyor work queue
    // with the reason, so a new survey replaces it.
    [HttpPost("api/surveys/{id:guid}/send-back")]
    [HasPermission(Permissions.SiteSurveysApprove)]
    public async Task<ActionResult<SiteSurveyDto>> SendBack(Guid id, SendBackSurveyRequest request, CancellationToken cancellationToken)
    {
        var (survey, problem) = await ReviewableAsync(id, cancellationToken);
        if (problem is not null) return problem;

        survey!.Status = SurveyStatus.Rejected;
        survey.ReviewedBy = currentUser.UserId;
        survey.ReviewedAtUtc = DateTime.UtcNow;
        survey.ReviewComment = request.Reason!.Trim();

        var order = await db.Orders.FirstAsync(o => o.Id == survey.OrderId, cancellationToken);
        var reopened = order.SiteSurveyId == survey.Id && OrderSurveys.OpenStatuses.Contains(order.Status);
        if (reopened)
        {
            OrderSurveys.Apply(order, null, requestNew: true, currentUser.UserId);
        }

        audit.Record(survey.TenantId, "sitesurvey.sent_back", "SiteSurvey", survey.Id.ToString(),
            new { survey.OrderId, Reason = survey.ReviewComment, RequestReopened = reopened });
        await db.SaveChangesAsync(cancellationToken);

        return (await LoadAsync(db.SiteSurveys.AsNoTracking().Where(s => s.Id == id), null, cancellationToken)).Single();
    }

    // A survey can be reviewed once, by an approver of its order's plant.
    private async Task<(SiteSurvey? Survey, ObjectResult? Problem)> ReviewableAsync(Guid id, CancellationToken cancellationToken)
    {
        var survey = await db.SiteSurveys.Include(s => s.Order).FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (survey is null)
        {
            return (null, Problem(title: "This survey was not found.", statusCode: StatusCodes.Status404NotFound));
        }

        if (survey.Status != SurveyStatus.Pending || survey.Order is null)
        {
            return (null, Problem(title: "This survey was already reviewed.", statusCode: StatusCodes.Status409Conflict));
        }

        var plants = await plantScope.ApprovesForAsync(currentUser.UserId, cancellationToken);
        if (plants is not null && !plants.Contains(survey.Order.PlantId))
        {
            return (null, Problem(title: "You do not approve surveys for this order's plant.",
                statusCode: StatusCodes.Status403Forbidden));
        }

        return (survey, null);
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

    private IQueryable<SurveyRequestDto> RequestQuery(List<Guid>? plants, Guid? orderId = null) =>
        db.Orders.AsNoTracking()
            .Where(o => orderId == null || o.Id == orderId)
            // The same states as OrderSurveys.OpenStatuses, spelled out because EF cannot translate that array here.
            .Where(o => o.SurveyRequested && (o.Status == OrderStatus.Draft || o.Status == OrderStatus.Submitted
                || o.Status == OrderStatus.InApproval || o.Status == OrderStatus.Approved
                || o.Status == OrderStatus.Confirmed || o.Status == OrderStatus.OnHold))
            .Where(o => plants == null || plants.Contains(o.PlantId))
            .OrderBy(o => o.RequestedDate).ThenBy(o => o.SurveyRequestedAtUtc)
            .Select(o => new SurveyRequestDto(
                o.Id, o.Number, o.Status, o.Customer.Name, o.SiteId, o.Site.Name,
                o.Site.Address, o.Site.Latitude, o.Site.Longitude, o.Site.AccessNotes,
                o.PlantId, o.Plant.Code, o.RequestedDate, o.SurveyRequestedAtUtc,
                o.Lines.Any(l => l.PourMethod == PourMethod.Pump),
                // Why the last survey for this order was sent back, if it was.
                db.SiteSurveys.Where(s => s.OrderId == o.Id && s.Status == SurveyStatus.Rejected)
                    .OrderByDescending(s => s.ReviewedAtUtc).Select(s => s.ReviewComment).FirstOrDefault()));

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
            s.LocationNote,
            s.OrderId,
            OrderNumber = s.Order != null ? s.Order.Number : null,
            s.Status,
            s.ReviewedBy,
            s.ReviewedAtUtc,
            s.ReviewComment
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
                .Select(i => new SurveyImageDto(i.Id, i.FileName, i.ContentType, i.SizeBytes, i.Caption)).ToList(),
            r.OrderId, r.OrderNumber, r.Status, r.ReviewedBy, r.ReviewedAtUtc, r.ReviewComment)).ToList();
    }
}