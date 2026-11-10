# PlantFlow domain spec (v3, frozen for build)

Clean-room design from general ready-mix industry practice. Nothing here comes from any employer's system. Save this file as `docs/domain-spec.md` in the repo. Where this spec marks something **(assumption)**, build the stated default and keep it easy to change.

## 0. What changed from v2

- **Short close and billing:** a short close is resolved by adjustment. The user can bill the delivered quantity, or **carry the undelivered volume forward** as a credit in a customer **volume ledger**.
- **Target achievement** counts **billed** volume.
- **Returned loads:** QC alone decides reuse. The fixed age limit is gone; load age is shown for information only.
- **Pump:** the free-pump threshold is one **tenant-wide** setting for all plants. Nothing else is free by size.
- **Site survey:** it is requested **after order confirmation**, performed by **Technical**, and belongs to the **site**. A site can have several orders, so a past survey can be **attached** to a new order.
- **Batches** record the batch quantity only, not per-material actuals. Material consumption is calculated from the mix design.

## 1. Glossary

- **Ready-mix concrete (RMC):** concrete batched at a plant and delivered in a wet state.
- **CFT:** cubic feet, the stored volume unit. A tenant setting can display m³ (1 m³ = 35.3147 CFT).
- **Strength class:** the specified strength, for example 3000 psi or C25. **Slump:** a workability measure.
- **Mix design (BOM):** the recipe, as material quantities per CFT.
- **BOQ:** bill of quantities, the list of products and quantities on an order.
- **Batching plant:** the machine that mixes. A **plant** can have several, each with its own make and capacity.
- **Batch:** one mixing cycle (about 7-8 minutes, configurable per batching plant).
- **Transit mixer:** the truck that carries concrete. **Pump:** a plant-owned machine that moves concrete through piping.
- **Site survey:** a technical visit that records pouring conditions (pipe length, height, access).
- **Short close:** closing an order with less volume delivered than ordered.
- **SA / SR:** sales associate or sales representative (the same role).

## 2. Roles

| Role | Does |
| --- | --- |
| Admin | Approves users, assigns roles and permissions, configures the tenant; can do everything |
| Sales representative (SA/SR) | Creates orders for their customers, sees their own targets |
| Sales manager | Approves sales orders and may revise the BOQ during approval; sets rep targets |
| Accounts | Maintains the price master (recipe costs, fixed overheads, plant-wise product prices), approves prices and discounts, resolves billing on short close, manages the volume ledger |
| Technical / quality | Approves custom mixes, maintains standard mixes, performs site surveys, performs QC on returned loads |
| Credit / finance | Approves credit for orders |
| Planner | Plans deliveries: schedules, assigns batching plant, vehicle and driver; attaches surveys to orders; closes orders |
| Dispatcher | Runs the live tracking board, records status changes |
| Batching plant operator | Produces concrete, records batches, loads and dispatches trucks |
| Plant manager | Sees plant performance, batch records and utilization |
| Management | Dashboards and targets only |

## 3. Data model (own design)

Every tenant-owned table has `TenantId`, `CreatedAtUtc`, `CreatedBy`, `UpdatedAtUtc` and `UpdatedBy`. Orders and deliveries also have a `RowVersion`.

### 3.1 Tenancy, identity and access

| Table | Key columns |
| --- | --- |
| Tenant | Id, Name, Slug, Status, VolumeUnit (CFT or M3 display), PumpFreeAboveCFT (nullable), RequireSurveyBeforePlanning (Off, PumpJobs, All), MinMarginPercent |
| User | Id, TenantId, FirebaseUid, Email, DisplayName, Status (Pending, Active, Rejected, Disabled), ApprovedBy, ApprovedAtUtc |
| Role | Id, TenantId, Name |
| RolePermission | RoleId, PermissionKey |
| UserRole | UserId, RoleId |
| UserPermission | UserId, PermissionKey, IsGranted (a grant or an explicit deny) |
| AuditLog | Id, TenantId, UserId, Action, EntityType, EntityId, ChangesJson, OccurredAtUtc |

### 3.2 Master data

| Table | Key columns |
| --- | --- |
| Plant | Id, Code, Name, Address, TimeZone, OperatingHoursPerDay, Status |
| BatchingPlant | Id, PlantId, Code, Make, Model, RatedCapacityCFTPerHour, BatchDurationMinutes, Status |
| Customer | Id, Code, Name, Type (Contractor, Developer, Individual), Phone, Email, SalesRepId, Status |
| Site | Id, CustomerId, Name, Address, Latitude, Longitude, ContactName, ContactPhone, AccessNotes |
| SalesRep | Id, UserId (optional), Name, Region, ManagerId (self-reference), Status |
| Vehicle | Id, PlantId, Code, Type (TransitMixer, Pump), CapacityCFT, PlateNumber, DefaultDriverId, Status (Available, InService, Maintenance, Retired) |
| Driver | Id, Name, Phone, Status |
| Material | Id, Code, Name, Category (Cement, Sand, Stone, Water, Admixture, Additive), Unit |
| Product | Id, Code, Name, StrengthClass, MaxAggregateMm, DefaultSlumpMm, Status |
| ServiceCharge | Id, Code, Name (Pump, NightDelivery, ShortLoad, ExtraDistance), Basis (PerCFT, PerLoad, Fixed), DefaultAmount |

Plant daily capacity is **derived**: the sum over its batching plants of rated capacity per hour times operating hours. The free-pump threshold lives on `Tenant.PumpFreeAboveCFT` and applies to every plant; no other charge has a size-based waiver.

### 3.3 Mix designs, costs and pricing

| Table | Key columns |
| --- | --- |
| MixDesign | Id, ProductId, PlantId (optional), Version, Kind (Standard, Custom), Status (Draft, PendingApproval, Approved, Retired), EffectiveFrom, ApprovedBy |
| MixDesignItem | Id, MixDesignId, MaterialId, QuantityPerCFT |
| MaterialCost | Id, MaterialId, PlantId, CostPerUnit, ValidFrom, ValidTo |
| OverheadCost | Id, PlantId, Name, CostPerCFT, ValidFrom, ValidTo |
| Price | Id, ProductId, PlantId, CustomerId (optional), PricePerCFT, Currency, ValidFrom, ValidTo, Status (Draft, PendingApproval, Active, Expired) |

Rules:

- A **custom recipe** is a `MixDesign` of kind Custom, requested for an order line and approved by Technical before the order can proceed.
- Mix designs are **versioned**. An approved version is never edited; a change creates a new version.
- **Product cost** per CFT = recipe material cost at the plant plus applicable overheads. Accounts sees cost, price and margin together.
- **Price lookup** picks the most specific active price: customer plus plant, then plant, then general. The chosen price is copied to the order line so later changes don't alter old orders.
- A price below cost plus `Tenant.MinMarginPercent` always requires Accounts approval.
- Material consumption per batch is **calculated** (mix design quantity per CFT times batch quantity). It is not entered by operators.

### 3.4 Site surveys

| Table | Key columns |
| --- | --- |
| SiteSurvey | Id, SiteId, RequestedBy, SurveyorId (Technical), SurveyedAtUtc, Status (Requested, Done, Cancelled, Superseded), PourMethodRecommended (Chute, Pump, Crane), PumpRequired, PipeLengthFt, PourHeightFt, AccessRoadWidthFt, TruckAccessNotes, EstimatedPourRateCFTPerHour, EstimatedPourDurationMinutes, Notes |
| OrderSiteSurvey | OrderId, SiteSurveyId, AttachedBy, AttachedAtUtc |

Rules:

- A survey belongs to the **site**, not to one order. Many orders can use the same survey.
- When an order is **confirmed**, the system checks the site for an existing `Done` survey. If one exists, the planner can **attach** it. If none fits (or conditions changed), the system creates a `Requested` survey for Technical.
- A new survey on a site marks the older one `Superseded`, but older orders keep their link.
- `Tenant.RequireSurveyBeforePlanning` decides whether an attached `Done` survey is needed to plan deliveries: never, for pump or long-pipe jobs, or for all orders.
- Photo attachments are on the roadmap.

### 3.5 Orders, billing and the volume ledger

| Table | Key columns |
| --- | --- |
| Order | Id, Number, CustomerId, SiteId, SalesRepId, PlantId, RequestedDate, Status, PaymentTerms, CloseType (Fulfilled, ShortClosed), CloseReason, Notes |
| OrderLine | Id, OrderId, ProductId, MixDesignId, QuantityCFT, DeliveredQuantityCFT, BilledQuantityCFT, BillingResolution (None, AdjustedToDelivered, CarriedForward, BilledAsOrdered), LedgerCreditAppliedCFT, UnitPrice, PriceId, SlumpMm, PourElement (Slab, Column, Footing, GroundLaying, Road, Other), PourMethod (Chute, Pump, Crane), Notes |
| OrderCharge | Id, OrderId, ServiceChargeId, Amount, IsWaived |
| OrderApproval | Id, OrderId, Stage (SalesManager, Pricing, Technical, Credit), Decision (Pending, Approved, Rejected), DecidedBy, DecidedAtUtc, Comment |
| OrderStatusHistory | Id, OrderId, FromStatus, ToStatus, ChangedBy, ChangedAtUtc, Reason |
| CustomerVolumeLedger | Id, CustomerId, SiteId (optional), SourceOrderLineId, EntryType (Credit, Debit), QuantityCFT, AppliedToOrderLineId (optional), Reason, CreatedAtUtc |

### 3.6 Planning, batching, delivery and returns

| Table | Key columns |
| --- | --- |
| Delivery | Id, OrderId, OrderLineId, DeliveryNumber, PlantId, BatchingPlantId, VehicleId, DriverId, PumpVehicleId (optional), PlannedStartUtc, QuantityCFT, DeliveredQuantityCFT, ReturnedQuantityCFT, Source (Fresh, ReusedReturn), Status, OutcomeReason |
| Batch | Id, DeliveryId, BatchingPlantId, BatchNumber, MixDesignId, PlannedQuantityCFT, ActualQuantityCFT, Status (Planned, Batching, Completed), BatchedAtUtc |
| DeliveryStatusEvent | Id, DeliveryId, Status, OccurredAtUtc, RecordedBy, Note |
| ReturnedLoad | Id, DeliveryId, QuantityCFT, ReasonCode, ReturnedAtUtc, AgeAtReturnMinutes, QcStatus (Pending, Passed, Failed), QcCheckedBy, QcCheckedAtUtc, ReassignedToDeliveryId (optional), DiscardedAtUtc, Notes |

Reason codes for rejection or return: `PoorQuality`, `FailedSiteQc`, `TruckDamage`, `PumpDamage`, `SiteNotReady`, `QuantityReducedAtSite`, `Other`.

### 3.7 Targets

| Table | Key columns |
| --- | --- |
| Target | Id, Level (Company, Plant, SalesRep), PlantId, SalesRepId, ParentTargetId, Year, Quarter (1-4), TargetVolumeCFT |

Rules:

- Targets are entered **per quarter, in volume**. The **yearly** target is the sum of its four quarters. The **monthly** target is one third of the quarter and is not stored.
- Child targets (reps) sum to no more than their parent (plant), and plant targets sum to no more than the company target.
- **Achievement = billed volume** (`BilledQuantityCFT`) of order lines closed in the period. Until an order closes, show a provisional figure based on completed deliveries, labelled as provisional **(assumption)**.

## 4. Workflows

### 4.1 Order status

`Draft` → `Submitted` → `InApproval` → `Approved` → `Confirmed` → `InProgress` → `Completed`

Also allowed: `OnHold`, `Rejected`, `Cancelled` (before `InProgress` only).

- **Submit:** requires a customer, site, at least one line and a requested date.
- **Approval stages:** `SalesManager` is always required and may revise the BOQ, with every change logged. Revising price or quantity restarts any `Pricing` approval. `Pricing` (Accounts) is required when the price is below list, discounted beyond a limit, or below cost plus margin. `Technical` is required for any custom mix. `Credit` is configurable per tenant. The order becomes `Approved` when every required stage is approved. Any rejection returns the order to `Draft` with a reason.
- **Confirm:** records the customer's confirmation (who, when, how) and locks quantities and prices. Confirmation then triggers the survey check (section 3.4).
- **Pump charge:** applied only to lines that use a pump, and waived when the order's pump quantity is at or above `Tenant.PumpFreeAboveCFT`.
- **Planning gate:** deliveries can be planned only for confirmed orders, and, depending on `RequireSurveyBeforePlanning`, only when a `Done` survey is attached.
- **InProgress** starts at the first delivery dispatch.
- Once confirmed, quantity or price changes need a controlled amendment that is logged.

### 4.2 Closing an order and billing

1. The planner closes the order when the client's work is finished.
2. If delivered volume meets the ordered volume, the order closes as `Fulfilled` and `BilledQuantityCFT` equals the ordered quantity. A small over-delivery is billed at the delivered quantity **(assumption)**.
3. If delivered volume is lower (ordered 500, received 450), the order closes as `ShortClosed` with a reason, and Accounts chooses a **resolution** per line:
   - **AdjustedToDelivered:** bill the delivered quantity (450).
   - **CarriedForward:** bill the delivered quantity (450) and add the undelivered 50 CFT to the customer's **volume ledger** as a `Credit`.
   - **BilledAsOrdered:** bill the full 500, which needs a logged reason.
4. A ledger credit can be **applied to a later order** of the same customer (optionally limited to the same site). Applying it creates a `Debit`, sets `LedgerCreditAppliedCFT` on the new line, and the applied volume counts as billed on that line **(assumption)**.
5. Every billing change is audit-logged.

### 4.3 Delivery status

`Planned` → `Batching` → `Loaded` → `Dispatched` → `OnSite` → `Pouring` → `Completed` → `BackAtPlant`

Also allowed: `Cancelled`, and `Rejected` (load refused at site).

- Each transition records a timestamp and who made it. Transitions follow this order. Skipping is not allowed except to `Cancelled` or `Rejected`.
- Compute durations: batching, batching to loaded, loaded to on site, on site to pouring, pouring duration, and the whole **cycle time**.
- **Alerts** are configurable per stage. Expected pour duration comes from the attached site survey. The system flags a delivery that exceeds the expectation by a configurable margin.
- A `Rejected` delivery, or a `Completed` one with a returned quantity, creates a `ReturnedLoad` with a reason code.

### 4.4 Returned loads and reuse

1. The truck brings concrete back to the plant. The system records the load's age since batching.
2. Technical performs QC and sets `Passed` or `Failed`. **QC alone decides.** Failed loads are discarded and logged.
3. A passed load can be **reassigned** to a delivery for another client with the same specification (same product and mix design). The new delivery is marked `ReusedReturn` and linked to the `ReturnedLoad`.
4. The load's age is shown to the person doing QC and the planner, but it does not block anything.

### 4.5 Planning rules

- A vehicle or driver cannot have overlapping deliveries (configurable turnaround gap).
- A delivery's quantity must not exceed the vehicle's capacity. Larger quantities are split.
- Each delivery is assigned to a **batching plant**. Planned volume per batching plant per hour must not exceed its rated capacity; warn, don't block.
- Truck spacing uses the attached survey's pour rate: "start time plus interval between trucks" helper.
- A pump is booked from the plant's own pumps when the survey or order line requires one. No pump for ground and road laying unless the survey says otherwise.

## 5. Dashboards

1. **Operations overview:** today's planned, in-progress and completed volume and deliveries per plant, trucks by status, pending approvals.
2. **Live tracking board:** deliveries grouped by status with elapsed time and alerts.
3. **Bottlenecks:** average and 90th-percentile time per stage, orders waiting longest per approval stage, idle vehicles and batching plants.
4. **Targets vs achievement (billed volume):** by company, plant and rep, per month, quarter and year, with percentage, trend and a provisional marker for open orders.
5. **Commercial:** ordered vs delivered vs billed volume, average price per CFT, margin by product, open ledger balances by customer.
6. **Utilization:** vehicle, pump and batching plant capacity used versus available.
7. **Quality and returns:** returned volume, reasons, QC pass rate, reuse rate.

## 6. Permission keys

`users.manage`, `roles.manage`, `settings.manage`, `masterdata.view`, `masterdata.edit`, `mixdesigns.view`, `mixdesigns.edit`, `mixdesigns.approve`, `costs.manage`, `prices.view`, `prices.edit`, `prices.approve`, `orders.view`, `orders.create`, `orders.edit`, `orders.approve.salesmanager`, `orders.approve.pricing`, `orders.approve.technical`, `orders.approve.credit`, `orders.confirm`, `orders.close`, `orders.billing.resolve`, `ledger.view`, `ledger.manage`, `sitesurveys.view`, `sitesurveys.perform`, `sitesurveys.attach`, `deliveries.plan`, `deliveries.dispatch`, `deliveries.track`, `batches.manage`, `returns.manage`, `returns.qc`, `targets.view`, `targets.manage`, `dashboards.view`, `audit.view`.

## 7. Screens

Login and pending-approval screen, user and role admin, tenant settings, master data lists and forms (including batching plants), mix design editor, cost and price master, order list and order form with approval panel, site survey form and survey attach panel, planning board (calendar plus vehicle and batching plant lanes), dispatch tracking board, batch records, returned loads and QC, order close and billing resolution, customer volume ledger, targets editor, dashboards.

## 8. Remaining questions (defaults apply if unanswered)

1. **Ledger credit:** should it be usable only at the **same site**, or at any site of the **same customer**? Does it expire? *Default: same customer, any site, no expiry.*
2. **Over-delivery** (delivered more than ordered): bill the extra volume? *Default: yes, billed at the delivered quantity.*
3. **Open orders and targets:** is a provisional figure from completed deliveries acceptable until the order closes, or should only closed orders count? *Default: show provisional, clearly marked.*
4. **Survey reuse:** is there a point where an old survey is too old to attach (for example, after a site changes)? *Default: Technical decides, and a new survey supersedes the old one.*
