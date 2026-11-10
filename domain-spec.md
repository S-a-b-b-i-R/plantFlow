# PlantFlow domain spec (draft v1)

Clean-room design from general ready-mix industry practice. Nothing here comes from any employer's system. Put this file at `docs/domain-spec.md` in the repo.

## 1. Glossary

- **Ready-mix concrete (RMC):** concrete batched at a plant and delivered in a wet state.
- **Grade / strength class:** the specified strength, for example C25 or 3000 psi.
- **Slump:** a workability measure in millimetres.
- **Mix design (BOM):** the recipe, as material quantities per cubic metre (m³): cement, sand, aggregates, water, admixtures.
- **Batch:** one mixing cycle at the plant. A delivery may be one or several batches.
- **Transit mixer:** the rotating-drum truck that carries concrete to site.
- **Pour:** placing concrete at site, by chute, pump or crane. A **pump** is a separate machine, sometimes a separate booking.
- **Site:** the construction location where concrete is delivered.

## 2. Roles

| Role                    | Does                                                                                                                   |
| ----------------------- | ---------------------------------------------------------------------------------------------------------------------- |
| Admin                   | Approves users, assigns roles and permissions, configures the tenant, basically admin can do anything and everything   |
| Sales representative    | Creates orders for their customers, sees their own targets                                                             |
| Accounts                | Approves prices and discounts, maintains price master for recipes, fixed overhead costs, plant wise product prices etc |
| Sales manager           | Approves Sale orders with provision to update BOQ                                                                      |
| Technical / quality     | Approves custom mix designs, maintains standard mixes                                                                  |
| Credit / finance        | Approves credit for orders (optional stage)                                                                            |
| Planner                 | Plans deliveries: schedules, assigns plant, vehicle and driver                                                         |
| Dispatcher              | Runs the live tracking board, records status changes                                                                   |
| Batching Plant Operator | Produces concrete and dispatches trucks                                                                                |
| Plant manager           | Sees plant performance, batch records and utilization                                                                  |
| Management              | Dashboards and targets only                                                                                            |

## 3. Data model (own design)

Every tenant-owned table has `TenantId`, `CreatedAtUtc`, `CreatedBy`, `UpdatedAtUtc` and `UpdatedBy`. Orders and deliveries also have a `RowVersion`.

### 3.1 Tenancy, identity and access

| Table          | Key columns                                                                                                            |
| -------------- | ---------------------------------------------------------------------------------------------------------------------- |
| Tenant         | Id, Name, Slug, Status                                                                                                 |
| User           | Id, TenantId, FirebaseUid, Email, DisplayName, Status (Pending, Active, Rejected, Disabled), ApprovedBy, ApprovedAtUtc |
| Role           | Id, TenantId, Name                                                                                                     |
| RolePermission | RoleId, PermissionKey                                                                                                  |
| UserRole       | UserId, RoleId                                                                                                         |
| UserPermission | UserId, PermissionKey, IsGranted (a grant or an explicit deny)                                                         |
| AuditLog       | Id, TenantId, UserId, Action, EntityType, EntityId, ChangesJson, OccurredAtUtc                                         |

### 3.2 Master data

| Table         | Key columns                                                                                                                           |
| ------------- | ------------------------------------------------------------------------------------------------------------------------------------- |
| Plant         | Id, Code, Name, Address, TimeZone, DailyCapacityCFT, Status                                                                           |
| Customer      | Id, Code, Name, Type (Contractor, Developer, Individual), Phone, Email, SalesRepId, Status                                            |
| Site          | Id, CustomerId, Name, Address, Latitude, Longitude, ContactName, ContactPhone, AccessNotes                                            |
| SalesRep      | Id, UserId (optional), Name, Region, ManagerId (self-reference), Status                                                               |
| Vehicle       | Id, PlantId, Code, Type (TransitMixer, Pump), CapacityCFT, PlateNumber, Status (Available, InService, Maintenance, Retired), DriverId |
| Driver        | Id, Name, Phone, Status                                                                                                               |
| Material      | Id, Code, Name, Category (Cement, Sand, Stone, Water, Admixture, Additive), Unit                                                      |
| Product       | Id, Code, Name, StrengthClass, MaxAggregateMm, DefaultSlumpMm, Status                                                                 |
| ServiceCharge | Id, Code, Name (Pump, NightDelivery, ShortLoad, ExtraDistance), Basis (PerM3, PerLoad, Fixed), DefaultAmount                          |

### 3.3 Mix designs and pricing

| Table         | Key columns                                                                                                                                          |
| ------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------- |
| MixDesign     | Id, ProductId, PlantId (optional), Version, Kind (Standard, Custom), Status (Draft, PendingApproval, Approved, Retired), EffectiveFrom, ApprovedBy   |
| MixDesignItem | Id, MixDesignId, MaterialId, QuantityPerM3                                                                                                           |
| Price         | Id, ProductId, PlantId (optional), CustomerId (optional), PricePerM3, Currency, ValidFrom, ValidTo, Status (Draft, PendingApproval, Active, Expired) |

Rules:

- A **custom recipe** is a `MixDesign` of kind Custom, requested for an order line and approved by Technical before the order can proceed.
- Mix designs are **versioned**. An approved version is never edited; a change creates a new version.
- **Price lookup** picks the most specific active price: customer plus plant, then customer, then plant, then general. The chosen price is copied to the order line (`UnitPrice`, `PriceId`) so later price changes don't alter old orders.

### 3.4 Orders

| Table              | Key columns                                                                                                                        |
| ------------------ | ---------------------------------------------------------------------------------------------------------------------------------- |
| Order              | Id, Number, CustomerId, SiteId, SalesRepId, PlantId, RequestedDate, Status, PaymentTerms, Notes                                    |
| OrderLine          | Id, OrderId, ProductId, MixDesignId, QuantityCFT, UnitPrice, PriceId, SlumpMm, PourElement, PourMethod (Chute, Pump, Crane), Notes |
| OrderCharge        | Id, OrderId, ServiceChargeId, Amount                                                                                               |
| OrderApproval      | Id, OrderId, Stage (Pricing, Technical, Credit), Decision (Pending, Approved, Rejected), DecidedBy, DecidedAtUtc, Comment          |
| OrderStatusHistory | Id, OrderId, FromStatus, ToStatus, ChangedBy, ChangedAtUtc, Reason                                                                 |

### 3.5 Planning, batching and delivery

| Table               | Key columns                                                                                                                            |
| ------------------- | -------------------------------------------------------------------------------------------------------------------------------------- |
| Delivery            | Id, OrderId, OrderLineId, DeliveryNumber, PlantId, VehicleId, DriverId, PumpVehicleId (optional), PlannedStartUtc, QuantityCFT, Status |
| Batch               | Id, DeliveryId, BatchNumber, MixDesignId, PlannedQuantityM3, ActualQuantityM3, Status (Planned, Batching, Completed), BatchedAtUtc     |
| DeliveryStatusEvent | Id, DeliveryId, Status, OccurredAtUtc, RecordedBy, Note                                                                                |

### 3.6 Targets

| Table  | Key columns                                                                                                                                         |
| ------ | --------------------------------------------------------------------------------------------------------------------------------------------------- |
| Target | Id, Level (Company, Plant, SalesRep), PlantId, SalesRepId, ParentTargetId, PeriodType (Month, Quarter), PeriodStart, TargetVolumeCFT, TargetRevenue |

Rule: child targets (reps) sum to no more than their parent (plant), and plant targets sum to no more than the company target. Achievement is computed from completed deliveries in the period.

## 4. Workflows

### 4.1 Order status

`Draft` → `Submitted` → `InApproval` → `Approved` → `Confirmed` → `InProgress` → `Completed`

Also allowed: `OnHold`, `Rejected`, `Cancelled` (before `InProgress` only).

- **Submit:** requires a customer, site, at least one line and a requested date.
- **Approvals:** `Pricing` is required when the price is below the list price or discounted beyond a configurable limit. `Technical` is required for any custom mix. `Credit` is optional per tenant. The order becomes `Approved` when every required stage is approved. Any rejection returns the order to `Draft` with a reason.
- **Confirm:** records the customer's confirmation (by whom, when, how) and locks quantities and prices.
- **InProgress** starts at the first delivery dispatch. **Completed** happens when delivered volume meets the ordered volume and the order is closed by a planner.
- Once confirmed, changes need a controlled amendment that is logged.

### 4.2 Delivery status

`Planned` → `Batching` → `Loaded` → `Dispatched` → `OnSite` → `Pouring` → `Completed` → `Returned`

Also allowed: `Cancelled`, and `Rejected` (load refused at site).

- Each transition records a timestamp and who made it. Transitions must follow this order. Skipping is not allowed except to `Cancelled` or `Rejected`.
- Compute durations: batching to loaded, loaded to on site, on site to pouring, pouring to completed, and the whole **cycle time**.
- **Time-limit alert:** the time from batching to pouring should stay under a configurable limit (default 90 minutes). Flag deliveries that approach or exceed it.
- A rejected load records a reason and the volume, and can trigger a replacement delivery.

### 4.3 Planning rules

- A vehicle or driver cannot have overlapping deliveries (allow a configurable turnaround gap).
- A delivery's quantity must not exceed the vehicle's capacity. Larger quantities are split into several deliveries.
- Planned volume per plant per day must not exceed `DailyCapacityM3`; warn, don't block.
- Deliveries for one pour should be spaced to match the pour rate. Support a "start time plus interval between trucks" scheduling helper.
- Pump bookings are scheduled alongside the first delivery for pump-method order lines.

## 5. Dashboards

1. **Operations overview:** today's planned, in-progress and completed volume and deliveries per plant, trucks by status, pending approvals.
2. **Live tracking board:** deliveries as cards or rows grouped by status, with elapsed time and time-limit warnings.
3. **Bottlenecks:** average and 90th-percentile time per delivery stage by plant, orders waiting longest in each approval stage, idle vehicles.
4. **Targets vs achievement:** by company, plant and rep, per month or quarter, with percentage and trend.
5. **Commercial:** volume and revenue by customer, product and rep; average price per m³.
6. **Utilization:** vehicle and plant capacity used versus available.

## 6. Permission keys

`users.manage`, `roles.manage`, `masterdata.view`, `masterdata.edit`, `mixdesigns.view`, `mixdesigns.edit`, `mixdesigns.approve`, `prices.view`, `prices.edit`, `prices.approve`, `orders.view`, `orders.create`, `orders.edit`, `orders.approve.pricing`, `orders.approve.technical`, `orders.approve.credit`, `orders.confirm`, `deliveries.plan`, `deliveries.dispatch`, `deliveries.track`, `batches.manage`, `targets.view`, `targets.manage`, `dashboards.view`, `audit.view`.

## 7. Screens

Login and pending-approval screen, user and role admin, master data lists and forms, mix design editor, price master, order list and order form with approval panel, planning board (calendar plus vehicle lanes), dispatch tracking board, batch records, targets editor, dashboards.

## 8. Open questions (answer from general industry knowledge)

1. In typical ready-mix businesses, is billing based on the volume ordered or the volume actually delivered?
2. What is the common maximum time from batching to pouring, and does it vary with weather?
3. Are pumps usually owned by the plant or hired from third parties?
4. Which approval stages do independent ready-mix companies commonly need: pricing, technical, credit?
5. Are sales targets usually monthly, quarterly, or both, and are they set in volume, revenue, or both?
6. Is a plant usually the unit of daily capacity, or a specific batching machine?
7. What are the common reasons a load gets rejected or returned?
