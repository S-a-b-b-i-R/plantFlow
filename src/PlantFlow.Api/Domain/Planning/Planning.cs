using PlantFlow.Api.Domain.Common;
using PlantFlow.Api.Domain.MasterData;
using PlantFlow.Api.Domain.Orders;

namespace PlantFlow.Api.Domain.Planning;

public enum SurveyResult { Ok, NotOk }

// A plant manager of the order's plant reviews every survey. Only an approved survey counts for planning.
public enum SurveyStatus { Pending, Approved, Rejected }

// A site visit that says whether concrete can be poured there, and how to get to it and set up.
public class SiteSurvey : ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }

    public Guid SiteId { get; set; }
    public Site Site { get; set; } = null!;

    // The order whose request this survey answered; its plant decides who may approve it.
    // Empty only on surveys recorded before surveys came from orders.
    public Guid? OrderId { get; set; }
    public Order? Order { get; set; }

    public DateOnly SurveyedOn { get; set; }
    public SurveyResult Result { get; set; }
    public string? Notes { get; set; }

    // From the surveyor's device. Required for new surveys, empty only on surveys made before this existed.
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public decimal? AccuracyMeters { get; set; }

    // Delivery instructions.
    public decimal? PipingMetres { get; set; }
    public string? PumpPosition { get; set; }
    public string? TruckConnectionPoint { get; set; }

    // How to find the place, for example "turn left after the tall blue building".
    public string? LocationNote { get; set; }

    public Guid? SurveyedBy { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // A reviewed survey is never edited: a rejected one stays as history and a new survey replaces it.
    public SurveyStatus Status { get; set; } = SurveyStatus.Pending;
    public Guid? ReviewedBy { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    // The reason it was sent back.
    public string? ReviewComment { get; set; }

    public List<SiteSurveyImage> Images { get; set; } = [];
}

// A photo of the site, kept in the database. A survey has at most three.
public class SiteSurveyImage : ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }

    public Guid SurveyId { get; set; }
    public SiteSurvey Survey { get; set; } = null!;

    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public int SizeBytes { get; set; }
    public string? Caption { get; set; }
    public byte[] Content { get; set; } = [];
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public enum ShiftPlanStatus { Draft, Submitted, Approved }

// Everything one plant produces and pours in one shift on one date. The plant head approves it as a whole.
public class ShiftPlan : ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }

    public Guid PlantId { get; set; }
    public Plant Plant { get; set; } = null!;

    // The service day the shift starts on.
    public DateOnly Date { get; set; }

    public Guid ShiftId { get; set; }
    public PlantShift Shift { get; set; } = null!;

    public ShiftPlanStatus Status { get; set; } = ShiftPlanStatus.Draft;

    // Why the plant head sent it back or reopened it.
    public string? ReturnReason { get; set; }

    public Guid? SubmittedBy { get; set; }
    public DateTime? SubmittedAtUtc { get; set; }
    public Guid? ApprovedBy { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }

    public List<Pour> Pours { get; set; } = [];
}

// A planned pour: part of an order line, at a time, with a pump if it is poured by pump.
// All times are plant-local.
public class Pour : ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }

    public Guid ShiftPlanId { get; set; }
    public ShiftPlan ShiftPlan { get; set; } = null!;

    public Guid OrderLineId { get; set; }
    public OrderLine OrderLine { get; set; } = null!;

    public decimal PlannedCft { get; set; }
    public DateTime PlannedStart { get; set; }

    public Guid? PumpId { get; set; }
    public Vehicle? Pump { get; set; }

    // The operator leading this pour, from the pump's crew. Optional.
    public Guid? OperatorId { get; set; }
    public PumpOperator? Operator { get; set; }

    public string? CrewNotes { get; set; }

    public DateTime? PumpLeaveAt { get; set; }
    public DateTime FirstTruckLeaveAt { get; set; }
    public int EstimatedTruckLoads { get; set; }

    // Set when a plant manager approved a pump that had not rested long enough.
    public string? RestOverrideReason { get; set; }

    public string? Notes { get; set; }

    // Pump timestamps recorded by operations (UTC).
    public DateTime? PumpDispatchedAtUtc { get; set; }
    public DateTime? PumpArrivedAtUtc { get; set; }
    public DateTime? PumpAssembledAtUtc { get; set; }
}

public enum LoadStep { Arrived, PouringStarted, PouringEnded, Returned }

public enum PumpStep { Dispatched, Arrived, Assembled }

// One truck carrying part of a pour. Produced by the batching operator, then followed to the site and back.
// All times are UTC.
public class TruckLoad : ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }

    public Guid PourId { get; set; }
    public Pour Pour { get; set; } = null!;

    // The truck, as a vehicle.
    public Guid TruckId { get; set; }
    public Vehicle Truck { get; set; } = null!;

    // 1, 2, 3 ... within the pour. Cancelled loads keep their number.
    public int Number { get; set; }

    public decimal QuantityCft { get; set; }

    // Why the load is smaller than usual, or the plant manager's reason for going over plan.
    public string? QuantityReason { get; set; }

    public DateTime ProducedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid? ProducedBy { get; set; }

    public DateTime? DispatchedAtUtc { get; set; }
    public Guid? DispatchedBy { get; set; }

    public DateTime? ArrivedAtUtc { get; set; }
    public DateTime? PouringStartedAtUtc { get; set; }
    public DateTime? PouringEndedAtUtc { get; set; }
    public DateTime? ReturnedAtUtc { get; set; }

    public DateTime? CancelledAtUtc { get; set; }
    public string? CancelReason { get; set; }

    // The part of the load that came back after pouring. The return itself is in ReturnedConcrete.
    public decimal ReturnedCft { get; set; }

    // Set when the whole load was refused before pouring started.
    public DateTime? RejectedAtUtc { get; set; }
    public string? RejectReason { get; set; }

    // The returned concrete this load was made from, if any.
    public Guid? SourceReturnId { get; set; }
}

public enum ReturnKind { PartLoad, RejectedAtSite, RejectedAtPlant }

public enum ReturnFault { Company, Customer }

public enum ReturnStatus { AwaitingInspection, Reusable, NeedsTreatment, Treated, Used, Dumped }

public enum ReturnDecision { Reusable, NeedsTreatment, Treated, Dumped }

// Concrete that came back to the plant. It is inspected, then reused, treated or dumped.
public class ReturnedConcrete : ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }

    public Guid PlantId { get; set; }
    public Plant Plant { get; set; } = null!;

    // The shift it came from. Reuse is limited to the same shift.
    public Guid ShiftPlanId { get; set; }
    public ShiftPlan ShiftPlan { get; set; } = null!;

    public Guid ProductId { get; set; }

    // The load it came from. A load has at most one return.
    public Guid SourceLoadId { get; set; }
    public TruckLoad SourceLoad { get; set; } = null!;

    public ReturnKind Kind { get; set; }
    public ReturnFault Fault { get; set; }
    public string Reason { get; set; } = "";

    public decimal QuantityCft { get; set; }

    // What has not been used or dumped yet.
    public decimal RemainingCft { get; set; }

    // What was dumped. A customer-fault dump is billed to the customer.
    public decimal DumpedCft { get; set; }

    public ReturnStatus Status { get; set; } = ReturnStatus.AwaitingInspection;

    // The note from the latest decision.
    public string? Note { get; set; }

    public Guid? CreatedBy { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid? DecidedBy { get; set; }
    public DateTime? DecidedAtUtc { get; set; }
}