using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlantFlow.Api.Data;
using PlantFlow.Api.Domain.MasterData;
using PlantFlow.Api.Domain.Security;
using PlantFlow.Api.Infrastructure;
using PlantFlow.Api.Services;

namespace PlantFlow.Api.Controllers;

[ApiController]
[Route("api/floor-price")]
public class FloorPriceController(PlantFlowDbContext db, FloorPriceCalculator calculator) : ControllerBase
{
    // ?productId=&plantId=  and optionally  &date=2026-11-12  &distanceKm=20  &customerId=
    // Without a date, today is used. Without a customer, no AIT is added.
    [HttpGet]
    [HasPermission(Permissions.CostsManage)]
    public async Task<ActionResult<FloorPriceBreakdown>> Get(
        [FromQuery] Guid productId, [FromQuery] Guid plantId, [FromQuery] DateOnly? date,
        [FromQuery] decimal distanceKm, [FromQuery] Guid? customerId, CancellationToken cancellationToken)
    {
        if (distanceKm < 0)
        {
            return Problem(title: "The distance cannot be negative.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (!await db.Products.AnyAsync(p => p.Id == productId, cancellationToken))
        {
            return Problem(title: "Choose a product of this company.", statusCode: StatusCodes.Status400BadRequest);
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

        var result = await calculator.CalculateAsync(
            productId, plantId, date ?? DateOnly.FromDateTime(DateTime.UtcNow), distanceKm, customerType, cancellationToken);

        if (result.Breakdown is null)
        {
            return Problem(title: result.Error, statusCode: StatusCodes.Status409Conflict);
        }

        return result.Breakdown;
    }
}