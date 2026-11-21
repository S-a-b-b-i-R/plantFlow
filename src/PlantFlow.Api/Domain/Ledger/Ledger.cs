using PlantFlow.Api.Domain.Common;
using PlantFlow.Api.Domain.MasterData;
using PlantFlow.Api.Domain.Orders;

namespace PlantFlow.Api.Domain.Ledger;

// Where a credit came from: the undelivered part of a short-closed line, or an entry made by Accounts
// (for example a balance brought over from the company's old records).
public enum LedgerCreditSource { CarriedForward, Manual }

public enum LedgerCloseReason { Refunded, NotNeeded }

public enum LedgerCloseStatus { Pending, Approved, SentBack }

// Volume a customer is owed at a locked price. It can be used on a later order of the same customer for the
// same product, at any site, and it does not expire. Rows are never deleted: uses, corrections and closes
// are kept as their own rows, and BalanceCft is what is left to use.
public class LedgerCredit : ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }

    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    // Per CFT, excluding VAT. What the customer pays for this volume when it is used.
    public decimal UnitPrice { get; set; }

    public LedgerCreditSource Source { get; set; }

    // Set for a carried-forward credit.
    public Guid? SourceOrderId { get; set; }
    public Order? SourceOrder { get; set; }
    public Guid? SourceOrderLineId { get; set; }

    public decimal OriginalCft { get; set; }

    // Original + corrections - uses - approved closes - the pending close. Kept up to date on every change,
    // and RowVersion stops two people spending the same volume at once.
    public decimal BalanceCft { get; set; }

    public string? Note { get; set; }

    public Guid? CreatedBy { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public byte[] RowVersion { get; set; } = [];

    public List<LedgerCreditUse> Uses { get; set; } = [];
    public List<LedgerCorrection> Corrections { get; set; } = [];
    public List<LedgerClose> Closes { get; set; } = [];
}

// Credit volume billed at the credit's price on a line of a later order. It can be removed while that order
// is running and is final once the order closes.
public class LedgerCreditUse : ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }

    public Guid CreditId { get; set; }
    public LedgerCredit Credit { get; set; } = null!;

    public Guid OrderId { get; set; }
    public Order Order { get; set; } = null!;

    public Guid OrderLineId { get; set; }
    public OrderLine OrderLine { get; set; } = null!;

    public decimal QuantityCft { get; set; }

    public Guid? CreatedBy { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

// A change to a credit's volume by Accounts, up or down, with a reason.
public class LedgerCorrection : ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }

    public Guid CreditId { get; set; }
    public LedgerCredit Credit { get; set; } = null!;

    // Positive adds volume, negative takes it away.
    public decimal QuantityCft { get; set; }
    public string Reason { get; set; } = "";

    public Guid? CreatedBy { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

// Closing volume the customer no longer needs. Accounts asks, someone else with ledger.approve decides.
// The volume is held back from use while the close is pending.
public class LedgerClose : ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }

    public Guid CreditId { get; set; }
    public LedgerCredit Credit { get; set; } = null!;

    public decimal QuantityCft { get; set; }
    public LedgerCloseReason Reason { get; set; }
    public string Note { get; set; } = "";

    public LedgerCloseStatus Status { get; set; } = LedgerCloseStatus.Pending;

    public Guid? RequestedBy { get; set; }
    public DateTime RequestedAtUtc { get; set; } = DateTime.UtcNow;

    public Guid? ReviewedBy { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    // Why it was sent back.
    public string? ReviewComment { get; set; }
}
