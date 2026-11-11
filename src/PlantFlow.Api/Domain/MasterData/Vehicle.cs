using PlantFlow.Api.Domain.Common;

namespace PlantFlow.Api.Domain.MasterData;

public enum VehicleType { TransitMixer, Pump }

// InService will be set by dispatch once deliveries exist. For now it is edited by hand.
public enum VehicleStatus { Available, InService, Maintenance, Retired }

// A transit mixer (carries concrete) or a pump (moves it through piping). Both belong to a plant.
public class Vehicle : AuditableEntity, ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }

    public Guid PlantId { get; set; }
    public Plant Plant { get; set; } = null!;

    public required string Code { get; set; }
    public VehicleType Type { get; set; } = VehicleType.TransitMixer;

    // Required for transit mixers (a delivery cannot exceed it). Optional for pumps.
    public decimal? CapacityCft { get; set; }

    public string? PlateNumber { get; set; }

    public Guid? DefaultDriverId { get; set; }
    public Driver? DefaultDriver { get; set; }

    public VehicleStatus Status { get; set; } = VehicleStatus.Available;
}