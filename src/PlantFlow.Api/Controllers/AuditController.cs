using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlantFlow.Api.Data;
using PlantFlow.Api.Domain.Auditing;
using PlantFlow.Api.Domain.Security;
using PlantFlow.Api.Infrastructure;
using PlantFlow.Api.Services;

namespace PlantFlow.Api.Controllers;

// UserName is null for changes made by PlantFlow itself (no signed-in user) and for people outside the company.
public record AuditLogDto(
    Guid Id, DateTime OccurredAtUtc, Guid? UserId, string Action, string EntityType, string EntityId, string? ChangesJson,
    string? UserName = null);

public record AuditPersonDto(Guid Id, string Name);

public record AuditFiltersDto(List<string> EntityTypes, List<AuditPersonDto> People);

// From and To are days in the company's time zone, both included.
public class AuditQuery : PageQuery
{
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public Guid? UserId { get; init; }
    public string? EntityType { get; init; }
    public string? EntityId { get; init; }
}

[ApiController]
[Route("api/audit")]
[HasPermission(Permissions.AuditView)]
public class AuditController(PlantFlowDbContext db, ICurrentUser currentUser, AuditWriter audit) : ControllerBase
{
    public const int ExportLimit = 10_000;
    public const string ExportTooLarge =
        "That is more than 10,000 entries. Narrow the dates or filters and export again.";

    // Newest first. Search looks in the action, the record id and the recorded changes.
    [HttpGet]
    public async Task<ActionResult<PagedResult<AuditLogDto>>> List(
        [FromQuery] AuditQuery query, CancellationToken cancellationToken)
    {
        var entries = await FilterAsync(query, cancellationToken);
        var page = await entries
            .OrderByDescending(a => a.OccurredAtUtc).ThenByDescending(a => a.Id)
            .Select(a => new AuditLogDto(a.Id, a.OccurredAtUtc, a.UserId, a.Action, a.EntityType, a.EntityId, a.ChangesJson, null))
            .ToPagedAsync(query, cancellationToken);

        var names = await NamesAsync(page.Items.Select(i => i.UserId), cancellationToken);
        return page with
        {
            Items = page.Items.Select(i => i with { UserName = NameOf(i.UserId, names) }).ToList()
        };
    }

    // The choices for the filter boxes: record types and people that appear in this company's log.
    [HttpGet("filters")]
    public async Task<ActionResult<AuditFiltersDto>> Filters(CancellationToken cancellationToken)
    {
        var types = await db.AuditLogs.AsNoTracking().Select(a => a.EntityType).Distinct().OrderBy(t => t)
            .ToListAsync(cancellationToken);
        var people = await db.Users.AsNoTracking()
            .Where(u => db.AuditLogs.Any(a => a.UserId == u.Id))
            .Select(u => new AuditPersonDto(u.Id, u.DisplayName ?? u.Email))
            .ToListAsync(cancellationToken);
        return new AuditFiltersDto(types, people.OrderBy(p => p.Name).ToList());
    }

    // The entries matching the filters as CSV, newest first. The export is itself recorded.
    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] AuditQuery query, CancellationToken cancellationToken)
    {
        var entries = await FilterAsync(query, cancellationToken);
        var count = await entries.CountAsync(cancellationToken);
        if (count > ExportLimit) return Problem(title: ExportTooLarge, statusCode: 400);

        var rows = await entries.AsNoTracking()
            .OrderByDescending(a => a.OccurredAtUtc).ThenByDescending(a => a.Id)
            .ToListAsync(cancellationToken);
        var names = await NamesAsync(rows.Select(r => r.UserId), cancellationToken);
        var offset = TimeSpan.FromMinutes(await OffsetMinutesAsync(cancellationToken));

        var csv = new StringBuilder("When (company time),Who,Action,Record type,Record id,Changes\r\n");
        foreach (var row in rows)
        {
            csv.AppendJoin(',',
                Cell((row.OccurredAtUtc + offset).ToString("yyyy-MM-dd HH:mm:ss")),
                Cell(NameOf(row.UserId, names) ?? (row.UserId is null ? "PlantFlow" : "Someone outside the company")),
                Cell(row.Action), Cell(row.EntityType), Cell(row.EntityId), Cell(row.ChangesJson ?? ""));
            csv.Append("\r\n");
        }

        audit.Record(currentUser.TenantId!.Value, "audit.exported", "AuditLog", "export", new
        {
            query.From, query.To, query.UserId, query.EntityType, query.EntityId, query.Search, Rows = count
        });
        await db.SaveChangesAsync(cancellationToken);

        // A byte-order mark so Excel reads names in any script correctly.
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
        return File(bytes, "text/csv; charset=utf-8", $"audit-log-{DateTime.UtcNow + offset:yyyy-MM-dd}.csv");
    }

    private async Task<IQueryable<AuditLog>> FilterAsync(AuditQuery query, CancellationToken cancellationToken)
    {
        var entries = db.AuditLogs.AsNoTracking();
        if (query.From is not null || query.To is not null)
        {
            var offset = TimeSpan.FromMinutes(await OffsetMinutesAsync(cancellationToken));
            if (query.From is { } from)
            {
                var start = from.ToDateTime(TimeOnly.MinValue) - offset;
                entries = entries.Where(a => a.OccurredAtUtc >= start);
            }
            if (query.To is { } to)
            {
                var end = to.AddDays(1).ToDateTime(TimeOnly.MinValue) - offset;
                entries = entries.Where(a => a.OccurredAtUtc < end);
            }
        }

        if (query.UserId is not null) entries = entries.Where(a => a.UserId == query.UserId);
        if (!string.IsNullOrWhiteSpace(query.EntityType)) entries = entries.Where(a => a.EntityType == query.EntityType);
        if (!string.IsNullOrWhiteSpace(query.EntityId)) entries = entries.Where(a => a.EntityId == query.EntityId.Trim());
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            entries = entries.Where(a => a.Action.Contains(search) || a.EntityId.Contains(search)
                                         || (a.ChangesJson != null && a.ChangesJson.Contains(search)));
        }
        return entries;
    }

    private Task<int> OffsetMinutesAsync(CancellationToken cancellationToken) =>
        db.Tenants.AsNoTracking().Where(t => t.Id == currentUser.TenantId)
            .Select(t => t.UtcOffsetMinutes).FirstAsync(cancellationToken);

    private async Task<Dictionary<Guid, string>> NamesAsync(IEnumerable<Guid?> userIds, CancellationToken cancellationToken)
    {
        var ids = userIds.OfType<Guid>().Distinct().ToList();
        return await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName ?? u.Email, cancellationToken);
    }

    private static string? NameOf(Guid? userId, Dictionary<Guid, string> names) =>
        userId is { } id && names.TryGetValue(id, out var name) ? name : null;

    // Quotes every cell, and stops a spreadsheet from running text that starts like a formula.
    private static string Cell(string value)
    {
        if (value.Length > 0 && "=+-@\t\r".Contains(value[0])) value = "'" + value;
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
