using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlantFlow.Api.Data;
using PlantFlow.Api.Domain.MasterData;
using PlantFlow.Api.Domain.Security;
using PlantFlow.Api.Infrastructure;
using PlantFlow.Api.Services;

namespace PlantFlow.Api.Controllers;

public record CostMarginRowDto(
    Guid ProductId, string ProductCode, string ProductName,
    decimal? TotalCost, decimal? FloorPrice, decimal? ActivePrice, PriceScope? PriceScope,
    decimal? MarginAmount, decimal? MarginPercent, bool? BelowFloor, string? Note);

[ApiController]
[Route("api/cost-margin")]
public class CostMarginController(PlantFlowDbContext db, FloorPriceCalculator calculator, PriceFinder finder) : ControllerBase
{
    // ?plantId=  and optionally  &date=2026-11-12  &distanceKm=20  &customerId=
    [HttpGet]
    [HasPermission(Permissions.CostsManage)]
    public async Task<ActionResult<List<CostMarginRowDto>>> Get(
        [FromQuery] Guid plantId, [FromQuery] DateOnly? date, [FromQuery] decimal distanceKm,
        [FromQuery] Guid? customerId, CancellationToken cancellationToken)
    {
        if (distanceKm < 0)
        {
            return Problem(title: "The distance cannot be negative.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (!await db.Plants.AnyAsync(p => p.Id == plantId, cancellationToken))
        {
            return Problem(title: "Choose a plant of this company.", statusCode: StatusCodes.Status400BadRequest);
        }

        CustomerType? customerType = null;
        if (customerId is { } id)
        {
            customerType = await db.Customers.Where(c => c.Id == id).Select(c => (CustomerType?)c.Type)
                .FirstOrDefaultAsync(cancellationToken);

            if (customerType is null)
            {
                return Problem(title: "Choose a customer of this company.", statusCode: StatusCodes.Status400BadRequest);
            }
        }

        var day = date ?? DateOnly.FromDateTime(DateTime.UtcNow);

        var products = await db.Products.AsNoTracking()
            .Where(p => p.Status == ActiveStatus.Active)
            .OrderBy(p => p.Code)
            .Select(p => new { p.Id, p.Code, p.Name })
            .Take(200)
            .ToListAsync(cancellationToken);

        // One product at a time: a DbContext cannot run two queries at once.
        var rows = new List<CostMarginRowDto>();
        foreach (var product in products)
        {
            var floor = await calculator.CalculateAsync(product.Id, plantId, day, distanceKm, customerType, cancellationToken);
            var price = await finder.FindAsync(product.Id, plantId, customerId, day, cancellationToken);

            var notes = new List<string>();
            if (floor.Error is not null) notes.Add(floor.Error);
            if (price is null) notes.Add("There is no active price on that date.");

            decimal? marginAmount = null, marginPercent = null;
            bool? belowFloor = null;

            if (floor.Breakdown is { } b && price is { } p)
            {
                // What the company keeps: the price less the AIT the customer deducts, less all costs.
                marginAmount = Math.Round(p.PricePerCft * (1m - b.AitPercent / 100m) - b.TotalCost, 2);
                marginPercent = Math.Round(marginAmount.Value / p.PricePerCft * 100m, 2);
                belowFloor = p.PricePerCft < b.FloorPrice;
            }

            rows.Add(new CostMarginRowDto(
                product.Id, product.Code, product.Name,
                floor.Breakdown?.TotalCost, floor.Breakdown?.FloorPrice, price?.PricePerCft, price?.Scope,
                marginAmount, marginPercent, belowFloor,
                notes.Count == 0 ? null : string.Join(" ", notes)));
        }

        return rows;
    }
}