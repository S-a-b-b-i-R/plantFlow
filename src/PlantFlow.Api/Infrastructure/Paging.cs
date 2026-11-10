using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace PlantFlow.Api.Infrastructure;

// Query-string parameters shared by every list endpoint: ?page=1&pageSize=25&search=abc&sort=-name
public class PageQuery
{
    [Range(1, int.MaxValue)]
    public int Page { get; init; } = 1;

    [Range(1, 100)]
    public int PageSize { get; init; } = 25;

    public string? Search { get; init; }

    // A field name, with a leading "-" for descending. Each endpoint decides which fields it allows.
    public string? Sort { get; init; }
}

public record PagedResult<T>(List<T> Items, int Page, int PageSize, int TotalCount);

public static class PagingExtensions
{
    public static async Task<PagedResult<T>> ToPagedAsync<T>(
        this IQueryable<T> query, PageQuery page, CancellationToken cancellationToken)
    {
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page.Page - 1) * page.PageSize)
            .Take(page.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<T>(items, page.Page, page.PageSize, total);
    }
}