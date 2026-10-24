# Chapter 05 — Trips and routes (Batch 5)

This chapter describes daily route planning (Trip), the consolidation of shipments into a route, automatic
optimization, manual editing (sequence and pin per stop), dispatch and departure, dispatch zones (extends
[chapter 04](04-flota-choferes-mantenimiento.md), section 2.7), the Outbound scan station, "Plan the day" and the
route monitor. Each section states **what it does**, **who can**, **how it is used**, the **validations** with the
exact message and the HTTP code, and the **statuses** with their transitions and effects. The messages are verified
against the code (`src/Teikem.Domain/Trips/*`,
`src/Teikem.Infrastructure/Trips/*`, `src/Teikem.Infrastructure/Services/{Trip,TripOrder,RouteOptimization,
TripDispatch,TripLifecycle,TripStatusEffect,TripOrderReleaseEffect,TripDayPlanning,OutboundScan,TripMonitor,
DispatchZone}*.cs`, `src/Teikem.Api/Controllers/{Trips,TripOrders,TripRoutes,TripDispatch,TripPlanning,TripMonitor,
Scan,DispatchZones}Controller.cs`). The questions and answers for each message are in [faq.md](faq.md).

Conventions of the chapter:

- A route (`Trip`) is identified by `publicId` (GUID) in the URL; its number (`code`, `YYYY-####`) is for display
  only. A `publicId` of another company responds **404** `Route not found.` as if it did not exist (no oracle).
- Stops (`RouteStop`) have no identity of their own outside their route: they are exposed with an integer id **only
  under their parent's route**; a `routeStopId` of another route (or another tenant) also responds **404** (BOLA by child id).
- No request carries `TenantId` or internal ids (`TripId`, `RouteId`); the tenant always comes from the session.
- The whole module lives under the **LTL_GROUND** module (core, always on): if for some reason it were off, every
  endpoint of this chapter would respond `403` `code: "module_disabled"`. The services also require
  **CATALOG** to be on when the operation touches a driver or a vehicle (creating/editing a route, bulk reassignment,
  dispatch); zones and their members remain under pure **CATALOG**, as in chapter 04.
- A validation error responds `400` with `title` and, when applicable, `errors` (`{ field: [message] }`); a
  conflict (something already assigned, already exists, concurrency clash) `409`; a status or business rule `422`
  (sometimes with `errors` per order or per blocker); a foreign or nonexistent resource `404`; missing permission or
  module `403`.
- **No money**: dispatching a route does not create a `DriverTrip`; no field in this chapter exposes amounts or rates.
- **No real routing engine** (VROOM/OSRM/OR-Tools): the engine of this batch (`HEURISTIC`) and the ETAs are
  in-house, deterministic calculations with no external calls.

---

## 1. Daily planning (Trip): create, record and list

What it does: creates and looks up routes. The number (`code`, `YYYY-####`) is assigned by the system (sequential per
company, never reset by year, immutable). The route is born in **DRAFT**. If a dispatch zone is given and no driver,
the **standard** driver of that zone is used: the only active driver with that zone as primary, if available on
the date and the **CATALOG** module is on (if it is off, it is skipped without error). If no departure time is
given, **12:00 UTC (08:00 AST) of the plan date** is used, so the record always has an ETA.

Who can: `trips.view` (list and view the record), `trips.plan` (create).

How it is used:
- `GET /api/v1/trips?date=&from=&to=&status=&dispatchZoneId=&driverPublicId=&search=&includeCancelled=false`
  (without `date`/`from`/`to` = the company's today; `status` accepts several; the free-text search runs after the filters)
- `POST /api/v1/trips`
- `GET /api/v1/trips/{publicId}` — full record: stops of the current version with ETA and zone, notices and
  blockers, "did not fit" from the last optimization and the driver's last ping.

```json
{ "planDate": "2026-09-28", "dispatchZoneId": 3, "vehiclePublicId": "…", "driverPublicId": null, "plannedStartUtc": null }
```

| Field / case | Exact message | HTTP |
|---|---|---|
| `planDate` missing | `Enter the route date.` (field `planDate`) | 400 |
| `planDate` earlier than yesterday or later than today + 60 days | `The route date cannot be earlier than yesterday or later than 60 days from today.` | 400 |
| `plannedStartUtc` outside ±12 h of the plan date | `The departure time must fall on the route date (±12 h for time zone).` | 400 |
| `dispatchZoneId` of another tenant or nonexistent | `Dispatch zone not found.` | 404 |
| `dispatchZoneId` inactive | `The dispatch zone is inactive.` (field `dispatchZoneId`) | 400 |
| `CATALOG` module off, with `driverPublicId`/`vehiclePublicId` given | `The 'CATALOG' module is not enabled for this company.` (`code: module_disabled`) | 403 |
| `driverPublicId`/`vehiclePublicId` of another tenant or nonexistent | `Driver not found.` / `Vehicle not found.` | 404 |
| Driver or vehicle not available on the date | `The driver is not available for dispatch: {reasons}.` / `The vehicle is not available for dispatch: {reasons}.` (same rule as [chapter 04](04-flota-choferes-mantenimiento.md), section 4) | 409 |
| Number clash from simultaneous creation (internal retries exhausted) | `A route with that number already exists.` | 409 |
| `status` filter with unknown code | `Unknown route status: 'X'.` (field `status`) | 400 |
| `driverPublicId` of the filter, of another tenant | `Driver not found.` | 404 |
| `publicId` of another tenant or nonexistent | `Route not found.` | 404 |

Eight simultaneous creations without a zone receive eight distinct consecutive numbers of the same year, with no gaps
(the counter is secured before the transaction opens and is locked internally until the commit).

---

## 2. Editing the header and "Delete route"

What it does: edits date, zone, driver, vehicle and departure time (`null` = no change; `clearZone`/`clearDriver`/
`clearVehicle`/`clearPlannedStart` remove the value). The **content** of the route (shipments, stops, sequence) is
never touched from here. A route already dispatched only allows touching the header if the tenant enabled the
`EDIT_TRIP` capability for that status (by default it comes **denied** in `DISPATCHED`, `IN_PROGRESS`, `COMPLETED` and
`CANCELLED`); even so, the date and zone of a dispatched route never change, and the driver/vehicle/departure time
are **changed**, never removed. "Delete route" moves the route to `CANCELLED`, releases its shipments (without touching
their status) and is only possible from `DRAFT`/`PLANNED`.

Who can: `trips.plan`.

How it is used:
- `PATCH /api/v1/trips/{publicId}` — accepts `rowVersion` to detect concurrent edits
- `DELETE /api/v1/trips/{publicId}` — optional body `{ "comment": "…", "rowVersion": "…" }`, responds `204`
- `POST /api/v1/trips/reassign-zone` — bulk reassignment

| Field / case | Exact message | HTTP |
|---|---|---|
| `code`/`tripCode` in the body | `The route number is set when it is created; it cannot be changed.` | 400 |
| `status`/`statusCode`/`toCode` in the body | `The route status changes through its actions (optimize, dispatch, delete).` | 400 |
| `tenantId`/`version`/`routeVersion`/`originWarehouseId` in the body | `That field cannot be modified.` | 400 |
| `driverPublicId` + `clearDriver` together | `Choose the driver or remove it, not both.` (field `driverPublicId`) | 400 |
| `vehiclePublicId` + `clearVehicle` together | `Choose the vehicle or remove it, not both.` (field `vehiclePublicId`) | 400 |
| `dispatchZoneId` + `clearZone` together | `Choose the zone or remove it, not both.` (field `dispatchZoneId`) | 400 |
| `plannedStartUtc` + `clearPlannedStart` together | `Enter the departure time or remove it, not both.` (field `plannedStartUtc`) | 400 |
| `rowVersion` does not match | `The record was modified by another user; reload and try again.` | 409 |
| Closed route (`COMPLETED`/`CANCELLED`) | `Route {code} is closed; it can only be viewed.` | 422 |
| Route already dispatched, without `EDIT_TRIP` enabled for that status | `Route {code} was already dispatched; it cannot be edited or deleted.` | 422 |
| Dispatched route (with `EDIT_TRIP`), changes `planDate` or `dispatchZoneId` | `The date and zone of a dispatched route cannot be changed.` | 422 |
| Dispatched route (with `EDIT_TRIP`), removes the driver, vehicle or departure time it already had | `A dispatched route must keep its driver, vehicle and departure time; change them instead of removing them.` | 422 |
| New date out of range | `The route date cannot be earlier than yesterday or later than 60 days from today.` (field `planDate`) | 400 |
| New departure time out of range | `The departure time must fall on the route date (±12 h for time zone).` (field `plannedStartUtc`) | 400 |
| New driver/vehicle (or the current one, if the date changes) not available | same availability 409s as section 1 | 409 |
| `DELETE` `comment` longer than 500 characters | `The comment allows at most 500 characters.` | 400 |
| `DELETE` of a route that is not in `DRAFT`/`PLANNED` | same "route is closed" or "was already dispatched" messages as above | 422 |

### Bulk reassignment by zone

What it does: changes the driver of **all** open routes (active, `DRAFT`/`PLANNED`, with `EDIT_TRIP` enabled
for their status) of one or several zones on a date. It does not touch the driver's primary zone or dispatched routes.

```json
{ "planDate": "2026-09-28", "dispatchZoneIds": [1, 2], "driverPublicId": "…" }
```

| Field / case | Exact message | HTTP |
|---|---|---|
| `planDate` missing | `Enter the route date.` | 400 |
| `dispatchZoneIds` empty | `Choose at least one zone.` (field `dispatchZoneIds`) | 400 |
| More than 50 zones | `At most 50 zones per reassignment.` (field `dispatchZoneIds`) | 400 |
| `driverPublicId` missing | `Choose the driver.` (field `driverPublicId`) | 400 |
| Some zone of another tenant or nonexistent | `Dispatch zone not found.` | 404 |
| `driverPublicId` of another tenant or nonexistent | `Driver not found.` | 404 |
| Driver not available on the date | `The driver is not available for dispatch: {reasons}.` | 409 |
| `CATALOG` module off | `The 'CATALOG' module is not enabled for this company.` (`code: module_disabled`) | 403 |

With no routes to reassign, the response is `200` with `tripsUpdated: 0` (it is not an error). The response also
brings the `DRIVER_DOUBLE_BOOKED` notices of the updated routes (the driver ends up in more than one route that day; it does not block).

---

## 3. Shipments in the route (multi-client consolidation)

What it does: adds and removes already picked-up shipments (Pickup or Unassigned; a Submitted one does not enter) to and from the route, and lists the shipments **without a current route** ("Unassigned")
with their resolved zone. Adding is **atomic** (all or nothing) and **does not change the shipment's status**; removing
releases it without touching it either. A shipment can only be in one current route at a time.

Who can: `trips.view` ("Unassigned" list), `trips.plan` (add/remove).

How it is used:
- `GET /api/v1/trips/unassigned-orders?dispatchZoneId=&noZone=&postalCode=&city=&clientPublicId=&requestedFrom=&requestedTo=&search=&skip=0&take=100`
- `POST /api/v1/trips/{publicId}/orders` — `{ "orderPublicIds": ["…", "…"], "rowVersion": "…" }`
- `DELETE /api/v1/trips/{publicId}/orders/{orderPublicId}` — optional body `{ "rowVersion": "…" }`

### 3.1 Eligibility of a shipment for a route

A shipment is eligible if it is **active**, is **not** a special delivery, is in a **PIPELINE** stage after
the initial one and before `IN_TRANSIT` in the tenant's pipeline, its status has the `ASSIGN_TRIP` capability and it has
a pending `DELIVERY` stop. In order of precedence:

| Reason | Exact message |
|---|---|
| Inactive, or already in a terminal stage | `The shipment already left on a route or finished; it cannot be assigned to another route.` |
| It is a special delivery | `Special deliveries are assigned to the driver from the shipment; they do not go through the Dispatch room.` |
| It is in the initial stage (Submitted) | `The shipment is in “Submitted”; mark it as picked up before assigning it to a route.` |
| It is in a lateral status (`FAILED`, Failed attempt) | `The order is in '{status}'; return it to the pipeline before assigning it to a route.` |
| It is already in `IN_TRANSIT` or later | `The shipment already left on a route or finished; it cannot be assigned to another route.` |
| Its status does not have the `ASSIGN_TRIP` capability | `The current status does not allow the action 'ASSIGN_TRIP'.` |
| It has no pending `DELIVERY` stop | `The shipment has no pending delivery stop.` |

### 3.2 Add and remove

| Case | Exact message | HTTP |
|---|---|---|
| `orderPublicIds` empty | `Choose at least one shipment.` (field `orderPublicIds`) | 400 |
| More than 200 distinct shipments | `Add at most 200 shipments per request.` (field `orderPublicIds`) | 400 |
| Some id foreign, nonexistent or inactive | `Order not found.` | 404 |
| Route not editable (dispatched or closed) | same messages as section 2 (`NotEditableMessage`) | 422 |
| one or more shipments not eligible | `There are shipments that cannot be assigned to the route.` with `errors: { "{number}": ["{reason}"] }` (rejects the whole request) | 422 |
| The shipment is already in THIS route | `The shipment is already in this route.` | 409 |
| The shipment is already in ANOTHER current route | `The order is already assigned to route {code}.` | 409 |
| Exceeds the technical limit of the route | `A route allows at most 300 stops.` (field `orderPublicIds`) | 400 |
| Race (two simultaneous additions of the same shipment to different routes) | `The shipment is already assigned to another route.` | 409 |
| `orderPublicId` of `DELETE`, foreign or nonexistent | `Order not found.` | 404 |
| The shipment is not in THAT route | `The shipment is not in this route.` | 404 |

### 3.3 "Unassigned" list

Active, non-special shipments, without a current route and with a pending `DELIVERY` stop, with the zone resolved by
postal code or town (see section 5). `ASSIGN_TRIP` hides nothing from the list: it is validated only when adding.

| Case | Exact message | HTTP |
|---|---|---|
| `dispatchZoneId` and `noZone` together | `Use dispatchZoneId or noZone, not both.` (field `noZone`) | 400 |
| `requestedFrom` later than `requestedTo` | `The date range is invalid.` (field `requestedTo`) | 400 |
| `dispatchZoneId` of another tenant or nonexistent | `Dispatch zone not found.` | 404 |
| `clientPublicId` of another tenant or nonexistent | `Client not found.` | 404 |

`skip`/`take` are normalized the same as the shipment list (`take` 1..500, 100 by default; `skip` ≥ 0, without
error). The free-text search (number, package, invoice, consignee) runs **after** the other filters.

---

## 4. Optimization, manual sequence and pin per stop

### 4.1 Automatic optimization

What it does: reorders the route with the `HEURISTIC` engine (deterministic: sorts by zone, window, postal code and
town, and respects the vehicle's stops/weight/volume). It creates a **new version** of the route (`n+1`, `OPTIMIZED`),
archives the previous one intact and, if the route was in `DRAFT`, moves it to `PLANNED`. What does not fit goes back to
"unassigned", with its reason visible in the record (`lastRunUnassigned`) while it remains without a route.

It runs in **three phases** so as not to lock the route while it calculates: (1) it takes a snapshot and creates the run in
`PENDING`; (2) the engine runs without locks, with a maximum time of 60 seconds; (3) the result is applied only if
the current route and its `rowVersion` have not changed since the snapshot; if they changed, the run is left in `ERROR` and
the response is `409`.

Who can: `trips.optimize`.

How it is used: `POST /api/v1/trips/{publicId}/optimize` — optional body `{ "rowVersion": "…" }`

| Case | Exact message | HTTP |
|---|---|---|
| Route not editable (dispatched or closed) | same messages as section 2 | 422 |
| The route has no stops | `The route has no stops to optimize.` (no run is created) | 422 |
| The route changed (version or `rowVersion`) while the engine was calculating | `The route changed while it was being optimized; optimize again.` (the run is left in `ERROR`) | 409 |
| The engine threw an exception, exhausted the 60 s or returned an invalid result | `The optimizer could not calculate the route; the run was recorded with an error.` (the run is left in `ERROR`) | 409 |
| `rowVersion` does not match | `The record was modified by another user; reload and try again.` | 409 |

Two simultaneous optimizations on the same route: both create their run; the first to apply wins (`200` with
version `n+1`) and the other receives the `409` above (or, if they did not overlap, both win with `n+1` and `n+2`).
There is always a single current version.

`GET /api/v1/trips/{publicId}/optimization-runs` (`trips.view`) lists the runs of the route, from the most recent
to the oldest, with engine, status, resulting version and who launched it.

### 4.2 Manual reordering

What it does: changes the sequence of the current version by hand (**same** version, without creating a run) and recalculates
the ETAs.

Who can: `trips.plan`.

How it is used: `PUT /api/v1/trips/{publicId}/route/sequence` — `{ "routeStopIds": [12, 10, 11], "rowVersion": "…" }`

| Case | Exact message | HTTP |
|---|---|---|
| `routeStopIds` is not exactly the current version (missing, extra, repeated or ids of another route) | `The sequence must include exactly the stops of the current route, without repeating.` (field `routeStopIds`) | 400 |
| Route not editable | same messages as section 2 | 422 |

### 4.3 Manual pin per stop

What it does: sets the coordinate of a stop by hand (for example, when the postal code or the town does not give an exact pin),
marks its accuracy as `MANUAL` and recalculates the ETAs. Only on `DRAFT`/`PLANNED` routes.

Who can: `trips.plan`.

How it is used: `PUT /api/v1/trips/{publicId}/stops/{routeStopId}/location` — `{ "lat": 18.4655, "lng": -66.1057, "rowVersion": "…" }`

| Case | Exact message | HTTP |
|---|---|---|
| `lat` or `lng` missing | `Enter the latitude and the longitude.` | 400 |
| `lat` outside -90..90 | `The latitude must be between -90 and 90.` | 400 |
| `lng` outside -180..180 | `The longitude must be between -180 and 180.` | 400 |
| `routeStopId` of another route or of an archived version | `Stop not found in this route.` | 404 |
| Route not editable (already dispatched) | same messages as section 2 | 422 |

The change of geographic accuracy (`GeocodeAccuracy` → `MANUAL`) is recorded in `AuditLog` under `TRANSPORT_ORDER`; the
coordinate itself (`GEOGRAPHY`) is not audited because it is not mapped as a property of the entity.

### 4.4 How the ETA is calculated

Without a routing engine: for each leg with coordinates at both ends, distance = straight line (haversine) × 1.3
(detour factor), rounded to 3 decimals; minutes = the greater of 1 and the ceiling of (km / 35 km/h × 60). The
first leg, or a leg without coordinates at one end, uses a fixed 15 minutes and null distance. If arrival is before
the start of the stop's window, it waits until that start; departure is arrival plus the service time; the stop is
"late" if arrival passes the end of the window. Without a planned departure time, the times are left null (that is
why creation uses 12:00 UTC by default: there is always an ETA).

---

## 5. Dispatch zones: members and resolution (extends chapter 04, section 2.7)

What it does: adds and removes the criteria that define a zone's territory (exact postal code, postal range or
municipality) and resolves "postal code or town → zone" with precedence **exact postal code > postal range >
municipality** (ignoring accents and case). A tie between two zones at the same level leaves the address **ambiguous**
(no zone). There is no overlap between **active** zones: adding or reactivating a criterion that already belongs to another
active zone is `409`. Polygons are not supported yet.

Who can: `fleet.view` (list/resolve), `fleet.manage` (add/remove). `CATALOG` module.

How it is used:
- `GET /api/v1/dispatch-zones/{id}/members`
- `POST /api/v1/dispatch-zones/{id}/members` — `{ "matchType": "POSTAL_CODE", "matchValue": "00949" }`
- `DELETE /api/v1/dispatch-zones/{id}/members/{memberId}` — audited physical DELETE, `204`
- `GET /api/v1/dispatch-zones/resolve?postalCode=&city=`

| Field / case | Exact message | HTTP |
|---|---|---|
| `matchType` empty | `Enter the zone criterion (POSTAL_CODE, POSTAL_RANGE or MUNICIPALITY).` (field `matchType`) | 400 |
| `matchType` unknown | `Unknown zone criterion: 'X'.` (field `matchType`) | 400 |
| `matchType` = `POLYGON` | `Polygon zones are not supported yet; use postal code, postal range or municipality.` (field `matchValue`) | 400 |
| `matchValue` empty (`POSTAL_CODE`/`POSTAL_RANGE`) | `Enter the criterion value.` (field `matchValue`) | 400 |
| `matchValue` empty (`MUNICIPALITY`) | `Enter the municipality.` (field `matchValue`) | 400 |
| Invalid postal code | `The postal code must have 5 digits (e.g. 00949).` (field `matchValue`; ZIP+4 is accepted and trimmed to 5) | 400 |
| Invalid postal range | `The postal range must have the form 00900-00999 (start less than or equal to end).` (field `matchValue`) | 400 |
| Municipality longer than 120 characters | `The municipality allows at most 120 characters.` (field `matchValue`) | 400 |
| Zone `id` foreign or nonexistent | `Dispatch zone not found.` | 404 |
| Inactive zone | `The dispatch zone is inactive.` | 400 |
| The criterion already exists in THIS zone | `The zone already has that criterion.` | 409 |
| The criterion already belongs to ANOTHER active zone | `The value '{v}' already belongs to zone {code}.` | 409 |
| `memberId` of another zone (BOLA by child id) | `Zone criterion not found.` | 404 |
| `resolve` without `postalCode` or `city` | `Enter the postal code or the town.` | 400 |
| Deactivating a zone with open routes (`DRAFT`/`PLANNED`) | `The zone has open routes; close them or move them to another zone before deactivating it.` | 409 |
| Reactivating a zone with a member that clashes with another active zone | `The value '{v}' already belongs to zone {code}.` | 409 |

`resolve` with no match returns `200` with `dispatchZoneId: null` (it is not an error); with a tie it returns
`ambiguous: true` and the candidate codes.

---

## 6. Dispatch and departure

### 6.1 Selector and dispatch

What it does: lists the active routes in `DRAFT`/`PLANNED` of a day with their notices and blockers, and dispatches a route
(individually or in batch). When dispatching: the route is frozen (`Route` moves to `ACTIVE`) and the current shipments advance
**stage by stage** up to `ASSIGNED` (Assigned; or up to the last enabled stage before it, if the tenant disabled
`ASSIGNED`). When the route departs (`IN_PROGRESS`) they move to `IN_TRANSIT`. Dispatching is **irreversible**.

Who can: `trips.dispatch`.

How it is used:
- `GET /api/v1/trips/dispatchable?date=` — without a date = the company's today
- `POST /api/v1/trips/{publicId}/dispatch` — optional body `{ "comment": "…", "rowVersion": "…" }`
- `POST /api/v1/trips/dispatch` — batch: `{ "tripPublicIds": ["…", "…"], "comment": "…" }` (maximum 50; each route in
  its own transaction; the response is always `200`, with the result of each one)

Notices and blockers of a route (`code` → `message`; it **blocks** dispatch only if `blocking: true`):

| Code | Blocks | Message |
|---|---|---|
| `NO_DRIVER` | yes | `The route has no driver assigned.` |
| `DRIVER_UNAVAILABLE` | yes | `The driver is not available for dispatch: {reasons}.` |
| `NO_VEHICLE` | yes | `The route has no vehicle assigned.` |
| `VEHICLE_UNAVAILABLE` | yes | `The vehicle is not available for dispatch: {reasons}.` |
| `NO_STOPS` | yes | `The route has no stops.` |
| `ORDER_NOT_ELIGIBLE` | yes | `Order {number}: {reason}` (one per non-eligible order; see section 3.1) |
| `OVER_STOP_LIMIT` | no | `The route has {n} stops and the driver's maximum is {m}.` |
| `OVER_VEHICLE_STOPS` | no | `The route has {n} stops and the vehicle allows {m}.` |
| `OVER_WEIGHT` | no | `The load ({x} kg) exceeds the vehicle's weight capacity ({y} kg).` |
| `OVER_VOLUME` | no | `The volume ({x} m³) exceeds the vehicle's volume capacity ({y} m³).` |
| `LATE_WINDOWS` | no | `1 stop arrives after the end of its window.` / `{n} stops arrive after the end of their window.` |
| `NO_PLANNED_START` | no | `The route has no departure time; ETAs are not calculated.` |
| `APPROXIMATE_PINS` | no | `1 stop has an approximate location or no coordinates.` / `{n} stops have an approximate location or no coordinates.` |
| `DRIVER_DOUBLE_BOOKED` | no | `The driver is also on route {codes} that same day.` |
| `VEHICLE_DOUBLE_BOOKED` | no | `The vehicle is also on route {codes} that same day.` |

| Case | Exact message | HTTP |
|---|---|---|
| Route not in `DRAFT`/`PLANNED` | same messages as section 2 | 422 |
| One or more blockers | `Route {code} cannot be dispatched: {m1}; {m2}.` with `errors: { "{notice code}": ["{message}"] }` | 422 |
| The tenant disabled `DISPATCHED` in the `TripStatus` pipeline | `This company's route pipeline does not have the DISPATCHED stage enabled.` | 422 |
| The tenant disabled `ACTIVE` in the `RouteStatus` pipeline | `This company's route pipeline does not have the ACTIVE stage enabled.` | 422 |
| A shipment stopped being eligible between the selector and the dispatch | `Order {number}: {reason}` (section 3.1) | 422 |
| `comment` longer than 500 characters | `The comment allows at most 500 characters.` | 400 |
| `CATALOG` module off | `The 'CATALOG' module is not enabled for this company.` (`code: module_disabled`) | 403 |
| Batch `tripPublicIds`, empty | `Select at least one route.` (field `tripPublicIds`) | 400 |
| Batch `tripPublicIds`, more than 50 | `At most 50 routes per dispatch.` (field `tripPublicIds`) | 400 |
| A batch id that is not a route of the tenant | left in the item with `dispatched: false` and `error: "Route not found."` (the overall response remains `200`) | — |
| `rowVersion` does not match | `The record was modified by another user; reload and try again.` | 409 |

### 6.2 Departure

What it does: records the departure of a route already dispatched (`DISPATCHED` → `IN_PROGRESS`): sets `actualStartUtc` and
takes the current shipments to `IN_TRANSIT`. It is the same seam (`TripLifecycleService.StartTrackedAsync`) that
the driver app of Batch 7 will reuse ("the state is moved by events: departure, arrival, POD"); manual closure of the
route is in 6.2b; per-stop events and the POD arrive with the driver app.

Who can: `trips.dispatch`.

How it is used: `POST /api/v1/trips/{publicId}/start` — optional body `{ "comment": "…", "rowVersion": "…" }`

| Case | Exact message | HTTP |
|---|---|---|
| Route that is not `DISPATCHED` (e.g. `DRAFT`/`PLANNED`) | `Route {code} is not dispatched; dispatch it before recording its departure.` | 422 |
| Route that is already `IN_PROGRESS` | `Route {code} already departed.` | 422 |
| Route without a driver or without a vehicle (defense; should not happen after dispatch) | `Route {code} cannot be dispatched: The route has no driver assigned.; The route has no vehicle assigned.` | 422 |
| `comment` longer than 500 characters | `The comment allows at most 500 characters.` | 400 |
| `rowVersion` does not match | `The record was modified by another user; reload and try again.` | 409 |

### 6.2b Close a route (2026-10-11, Batch 33)

What it does: closes a route in progress (`IN_PROGRESS` → `COMPLETED`): sets `actualEndUtc` and the shipments stop being current in the
route. It can only be closed when **all the current shipments of the route have already finished**: delivered, cancelled or another terminal
stage, or with the attempt already concluded without delivery (**Failed attempt**: it goes to the inbox to be rescheduled). Those still on
the way and those that arrived without a result do not count as finished.

Who can: `trips.close` (new permission; the Dispatcher and TenantAdmin templates bring it, and it was added to the already existing roles
with those names when running `db-update`). A custom role needs to have it added.

How it is used: `POST /api/v1/trips/{publicId}/complete` — optional body `{ "comment": "…", "rowVersion": "…" }`; returns the route record.
The default comment in the history is `Ruta cerrada`.

| Case | Exact message | HTTP |
|---|---|---|
| Route that is not `IN_PROGRESS` (`DRAFT`, `PLANNED`, `DISPATCHED`, `CANCELLED`) | `Route {code} is not in progress; only a route that already departed can be closed.` | 422 |
| Route already `COMPLETED` | `Route {code} is already closed.` | 422 |
| some shipment not finished | `Route {code} cannot be closed: there are unfinished orders ({n1}, {n2}…).` (names up to 10 and adds `and N more`) | 422 |
| The route does not exist or belongs to another company | `Route not found.` | 404 |
| Without the `trips.close` permission | standard permission denied response | 403 |
| `comment` longer than 500 characters | `The comment allows at most 500 characters.` | 400 |
| `rowVersion` does not match | `The record was modified by another user; reload and try again.` | 409 |

Frequent cases: a shipment is still `IN_TRANSIT` because the driver did not record the delivery → record it (or mark it Failed attempt) and close
again. An automatic closure when the last shipment finishes will come with the
driver app (for now the closure is manual).

### 6.3 Cancel an order that is on a route

| Case | Exact message | HTTP |
|---|---|---|
| Cancel an order that is on a `DRAFT`/`PLANNED` route | it is released from the route automatically (`204`, no error) | — |
| Cancel an order that is on a `DISPATCHED`/`IN_PROGRESS` route | `The order is on route {code}, already dispatched; it cannot be cancelled while the route is in progress.` | 422 |
| Send a shipment of an open route to Failed attempt (`FAILED`) | it is released from the route automatically | — |
| Send a shipment of an already dispatched route to a lateral status | nothing changes (Batch 7 decides it) | — |

---

## 7. Outbound scan station

> **Batch 107 (2026-10-17): the station now has a chosen ACTION.** The user chooses what they are going to do —**Pick up, Arrived, Route or Depart**— and each scan takes the shipment to the status of that action (`POST /api/v1/scan/action`, described in section 7b). **Route** (the default action) does what it always did —put the shipment in the open route of its zone— and also leaves it **Assigned**; the text below (`/scan/outbound`) still describes that assigner, which Route reuses. A Submitted shipment is no longer rejected when routing it: it is taken to Assigned, skipping Pickup and Unassigned.

What it does: scans the shipment number, the package number or the client's invoice number (same criterion and
precedence as `/orders/lookup`: shipment number first, then package, then invoice and, since Batch 44, the package number
on each piece's label) and assigns the shipment to the
open route of the day for its zone. It always responds `200` with a typed result and the word the station must
speak (`found`/`dup`/`notfound`). It never creates routes, nor changes the driver, vehicle or the shipment's status.

Who can: `trips.scan`.

How it is used: `POST /api/v1/scan/outbound` — `{ "code": "PKG-000123", "planDate": null }` (without a date = today in the company's time zone; the screen always sends the chosen date, today by default)

| Case | Exact message | HTTP |
|---|---|---|
| `code` empty | `Scan or type a code.` (field `code`) | 400 |
| `code` longer than 40 characters | `The code cannot exceed 40 characters.` (field `code`) | 400 |

Results (`outcome` / `voice` / message), in order of precedence:

| `outcome` | `reasonCode` | `voice` | Message |
|---|---|---|---|
| `NOT_FOUND` | `NO_MATCH` | `notfound` | `Shipment not found.` |
| `NOT_FOUND` | `MULTIPLE_MATCHES` | `notfound` | `There are several shipments with that code; scan the package.` |
| `NOT_FOUND` | `MULTIPLE_MATCHES` (by package number, Batch 44) | `notfound` | `The package number is on {n} orders (from different clients or repeated); scan the package or the shipment number.` |
| `ALREADY_ASSIGNED` | `IN_TRIP` | `dup` | `It was already on route {code}.` (beats "not eligible": an already dispatched shipment says "already") |
| `NOT_ELIGIBLE` | `NOT_ELIGIBLE` | `notfound` | the reason from section 3.1 (e.g. "The current status does not allow the action 'ASSIGN_TRIP'.") |
| `FOUND_UNASSIGNED` | `NO_ZONE` | `found` | `The dispatch zone could not be resolved by postal code or town; it remains unassigned.` |
| `FOUND_UNASSIGNED` | `AMBIGUOUS_ZONE` | `found` | `The postal code or town belongs to several zones ({codes}); it remains unassigned.` |
| `FOUND_UNASSIGNED` | `NO_OPEN_ROUTE` | `found` | `There is no open route for zone {code} on date {yyyy-MM-dd}; it remains unassigned.` |
| `FOUND_ASSIGNED` | — | `found` | `Assigned to route {code}.` |

The scan never invents routes: if the only open route of the zone stopped being open between the scan and the lock
(for example, someone just dispatched it), it tries the next open route with the lowest id; if none is left,
the result is `NO_OPEN_ROUTE`. Eight simultaneous scans of different shipments into the same route all end up
`FOUND_ASSIGNED`, with sequences 1..N without gaps or duplicates.

---

## 7b. Scanning with a chosen action (Batch 107) and per piece (Batch 110)

> **Scanning is per PIECE** (confirmed by Luis, 2026-10-24): the piece is what says **Yes** (new for that action), **Ok** (that same piece repeated with the same action) or **No** (not found, shipment cancelled or in a final status). It applies the same in the web app and in the mobile app.
>
> **How to scan (Batch 216, 2026-10-24).** Below the actions, the station asks **“How to scan”**: **All** (default: you scan without choosing anything else), **By route**, **By city** or **By driver**. With route, city or driver a list appears with a **search box** and **multiple selection** (open routes, towns of the uncompleted shipments, active drivers; `GET /api/v1/scan/options`, `trips.scan`) and **it does not let you scan until at least one option is chosen**. The scan (`POST /scan/action`) then carries `scopeMode` (`ROUTE`, `CITY` or `DRIVER`) and `scopeIds`, and a piece of a shipment that **is not part of what was chosen** responds `NOT_IN_SCOPE` (“No”, red; message `Piece {piece} (shipment {shipment}) is not from the chosen routes / cities / drivers.`) **even if it exists**: nothing changes and it is not counted. A shipment's route is its current route; its driver, the one of that route (of any type and state; a shipment without a route has no driver); its city, the town of its delivery stop (ignoring accents and case). With **Route and ONE single route** chosen, that route is the destination (`tripPublicId`) and shipments that do not have a route yet are also valid; with several routes, only those already in one are valid. In the web app the filter is applied by the server; in the mobile app it lives on the phone.
>
> **“Status further ahead” warning and magnifier (Batch 216, 2026-10-24).** (1) If the scanned piece belongs to a shipment that **is already further ahead** than the action (for example, **Arrived** on one that is already **In transit**), the server **changes nothing** and responds `BEYOND` (voice **“Attention”**, message `Piece {piece}: shipment {shipment} is already in “{status}”, which comes after “{action}”. If you continue, the shipment will go back to “{action}”.`). The station shows the warning and the person decides: **Cancel** does nothing; **Move the status back** repeats the scan with `confirmBeyond: true` and the shipment **goes back** to the status of the action (`APPLIED`, “Yes”; message `Piece {piece} moved shipment {shipment} back from “{before}” to “{after}”.`). Scanning backwards is the way to move a status back. When going back to a status **before Assigned** the shipment **leaves its route** (if the route was already dispatched, the driver receives a notice “Stop removed from {route}”) and the **scans of later actions** of its pieces are **erased**, so that when moving forward again each piece counts anew. If the shipment was already in **that same** status there is no warning: the new piece is counted. (2) The **magnifier** (`GET /api/v1/scan/lookup?code=`, `trips.scan`) searches the number of a piece —or, if it belongs to none, the shipment, waybill, package or invoice— and shows **whatever is in the database**, even cancelled, final, written off, lost, under claim or “To verify”, with the route, the driver and the piece's scans; it does not count as a scan.
>
> **Batch 216 (2026-10-24): the staff mobile app has this station, with offline work** (chapter 45 §2d). `POST /api/v1/scan/action` and `/scan/outbound` now accept `tripPublicId` (optional): with **Route**, the shipment goes to **that** route —planned, dispatched or in progress— instead of the route of its zone; if the route is no longer open, the shipment remains unassigned with `Route {code} is no longer open; it remains unassigned.` (`NO_OPEN_ROUTE`) and Route is undone as always.

> **Batch 110 (2026-10-18): what gets scanned are the PIECES.** You scan the **number of each piece** (the one on the label); the **shipment number, the package number and the invoice number are not scanned**: the station responds “Piece not found.” (audio **“No”**). The station's audio is **“Yes”** (found), **“No”** (not found) and **“Ok”** (duplicate). Each piece is recorded **once per action**: scanning it again with the same action is a **duplicate** (`DUPLICATE`, “Piece … had already been scanned with this action.”, no changes and not counted twice).
> - **The first scan of an action moves the shipment** to that status (skipping the ones in between, as described below); the following ones of the same action **only count** (`COUNTED`, “Piece … was counted…”, or with Route “It was already on route …”). The response brings `scanned`/`total` (“4 of 5 pieces”) and `missing` (missing pieces “To verify”).
> - **Missing pieces:** a piece that was not scanned in the action the shipment was in —nor in a later one— is left as **“To verify”** (`TO_VERIFY`) **when the shipment moves to the next action without it**, or on **“Close count”**. There is only a missing piece if at least one piece was scanned in that action (if the shipment skipped the action there is no count to close). The shipment advances anyway and shows “4 of 5”. If the piece shows up later (it is scanned with any action) **it stops being missing**. Claims (next batch) continue from “To verify”.
> - **“Close count”** (button in the result box, `POST /api/v1/scan/close-count` with `{ "orderPublicId": … }`, permission `trips.scan`): marks as “To verify” what was not scanned in the action the shipment is in, without changing its status. It responds `Count closed: order … is complete.` or `Count closed: order … is missing N of M pieces (“To verify”).`
> - Route with no route to receive the shipment (`NO_ROUTE`) changes nothing and **does not record the piece**.

What it does: the user chooses the **action** and scans; each scan takes the shipment to the status of that action **skipping the statuses in between** (there is no need to scan several times). The skipped statuses are left in the history with the mark **“(skipped)”** and **the same date and time** as the status it reached; client notices for the skipped statuses are **not** sent. It does not check rate, credit or client: that was checked when the shipment was submitted.

Who can: `trips.scan` (LTL_GROUND module). How it is used: screen **Operations → Scan station** (action selector at the top) or `POST /api/v1/scan/action` — `{ "action": "ARRIVE", "code": "CND-0000123", "planDate": null }`. The code is the usual one (shipment number, package, invoice or package number).

| Action | `action` | Takes the shipment to | Who uses it |
|---|---|---|---|
| Pick up | `PICKUP` | Pickup | whoever picks up (driver or guard) |
| Arrived | `ARRIVE` | Unassigned | the warehouse guard |
| Route | `ROUTE` | Assigned **and** puts it in the open route of its zone | the warehouse guard |
| Depart | `DEPART` | In transit | the driver when accepting it |

Results (`outcome` / `voice`), always `200`:

| `outcome` | `voice` | When | Message (example) |
|---|---|---|---|
| `APPLIED` | `found` | the status changed | `Order CND-0000123 moved to Unassigned.` / `… moved to In transit (skipped 2 statuses).` / with Route: `Assigned to route 2026-0001. (skipped 2 statuses)` |
| `ALREADY_BEYOND` | `dup` | the shipment is already in that status or further ahead: it is not touched (with Route, if it is already on a route: `It was already on route 2026-0001.`) | `Order CND-0000123 is already in Assigned or further ahead: it is not changed.` |
| `FINAL` | `dup` | the shipment is in a final status (Delivered, Cancelled, In warehouse): it is ignored; reopening it requires the `orders.reopen` permission | `Order CND-0000123 is in a final status (Delivered): it is not changed.` |
| `NO_ROUTE` | `notfound` | **Route** and there is no open route to receive the shipment (no zone, ambiguous zone or no route that day): nothing changes, not even the status | the usual reason (section 7), e.g. `There is no open route for zone R-01 on date 2026-10-17; it remains unassigned.` |
| `NOT_FOUND` / `AMBIGUOUS` | `notfound` | code that does not exist or is on several shipments | `Shipment not found.` / `There are several shipments with that code; scan the package.` |

Rules:
- **From Failed attempt** (lateral): **Arrived** returns it to Unassigned (the failed attempt stays in the history); Route and Depart take it to Assigned / In transit passing through Unassigned (skipped); Pick up is already further ahead.
- **Statuses can be skipped**: from Submitted, **Depart** takes it to In transit leaving Pickup, Unassigned and Assigned skipped.
- `400` errors: action other than the four → `Choose the action: pick up, arrived, route or depart.` (field `action`); empty code or longer than 40 characters, same as section 7.
- Silent: it never asks or interrupts; each scan answers with its result in the session list.

---

## 8. Plan the day

What it does: groups by dispatch zone the eligible shipments (already picked up) **without a current route** on a date: it uses the
open route of the zone (the one with the lowest id if there are several) or creates one with the standard driver (section 1). It is **idempotent**
(repeating it does not create extra routes and only adds what remains unassigned), it is serialized per company and it does **not**
optimize, does not dispatch, does not assign a vehicle and does not change the status of the shipments.

Who can: `trips.plan`.

How it is used: `POST /api/v1/trips/plan-day` — `{ "planDate": "2026-09-28", "dispatchZoneIds": null, "createEmptyTrips": false }`
(`dispatchZoneIds` null or empty = all the tenant's active zones; `createEmptyTrips: true` creates the route of a
zone even if it has no shipments, so that Outbound scanning has a destination from early on)

| Field / case | Exact message | HTTP |
|---|---|---|
| `planDate` missing or out of range | same messages as section 1 (field `planDate`) | 400 |
| More than 50 zones in `dispatchZoneIds` | `At most 50 zones per planning run.` (field `dispatchZoneIds`) | 400 |
| Some zone of another tenant or nonexistent | `Dispatch zone not found.` | 404 |
| Some inactive zone | `The dispatch zone is inactive.` (field `dispatchZoneIds`) | 400 |

Per zone, each omitted shipment leaves a `NOT_ELIGIBLE` notice (`Order {number}: {reason}`, section 3.1) or
`CAPACITY_HARD_CAP` (`The route reached the maximum of 300 stops; the shipment remains unassigned.`) in the result; they are not
HTTP errors, they are part of the `200` response (`zones[].issues`). Shipments without a resolved zone or with an ambiguous zone
are not touched: they are only counted in `ordersWithoutZone`.

---

## 9. Route monitoring

What it does: lists the `DISPATCHED`/`IN_PROGRESS` routes of the day (and the `COMPLETED` ones, unless `includeCompleted=false`)
with their progress, next ETA, stops with an approximate pin, whether it exceeds the maximum number of stops and the driver's last
ping (if the ping does not carry the route, the driver's last ping since departure is used, marked `linkedToTrip:
false`). The **totals** are calculated over the date and the zone **before** applying the free-text search: searching never
changes the counters, it only filters the visible list.

Who can: `trips.view`.

How it is used: `GET /api/v1/trips/monitor?date=&dispatchZoneId=&search=&includeCompleted=true` (without `date` = the company's today)

The web screen (Operations → **Route monitor**, manual `frontend/fe3-monitoreo.md`) uses this endpoint with `includeCompleted=false` by default and refreshes it every 30 seconds.
Clarifications: `includeCompleted` is `true` by default in the API; the totals include the completed routes only if requested; `approximateStops` counts the stops **without a coordinate** in addition to those with an approximate pin; **today there is no endpoint that writes pings** (the driver app, Batch 7, sends them), so `lastPing` normally comes empty and `tripsWithoutPing` equals the total number of routes.

No validations of its own beyond the shared ones (permission and module). The record of each route
(`GET /api/v1/trips/{publicId}`) brings the coordinates (`point`, absent if the stop has none) and the accuracy of each stop; the front end uses them to draw a schematic map (no streets) in the Dispatch room (screen manual: `frontend/fe2a-despacho.md`).

---

## 10. Statuses and transitions

### 10.1 Trip (`TripStatus`)

| From | To | Who | What it validates | Effects | What it blocks |
|---|---|---|---|---|---|
| — | `DRAFT` (initial) | System, on creation | Unique number `YYYY-####` | Birth history | — |
| `DRAFT`/`PLANNED` | `PLANNED` (only reached by optimizing, section 4.1) | `trips.optimize`, automatic when applying an optimization | — | None of its own | — |
| `DRAFT`/`PLANNED` | `DISPATCHED` | `trips.dispatch` (`POST .../dispatch`) | No blockers (section 6.1); pipeline with `DISPATCHED` enabled | `TripStatusEffect`: freezes the route (stage by stage up to `Route.ACTIVE`) and advances the current shipments up to `PLANNED` | Route content frozen; header only editable with `EDIT_TRIP` |
| `DISPATCHED` | `IN_PROGRESS` | `trips.dispatch` (`POST .../start`) | Must be `DISPATCHED` | `TripStatusEffect`: `actualStartUtc`, current shipments in the pipeline advance up to `IN_TRANSIT` | — |
| `IN_PROGRESS` | `COMPLETED` | `trips.close` (`POST .../complete`, section 6.2b) | All current shipments finished (terminal or Failed attempt) | `TripStatusEffect`: `actualEndUtc`, `TripOrder.IsCurrent = 0` | Route closed, view only |
| `DRAFT`/`PLANNED` | `CANCELLED` ("Delete route", lateral) | `trips.plan` (`DELETE`) | Only from `DRAFT`/`PLANNED` | `TripStatusEffect`: releases all current shipments, archives the current version of the route, `Trip.IsActive = 0` | Retrying `DELETE`, editing, adding shipments, optimizing, dispatching (422 "closed") |

Attempting `CANCELLED` from `DISPATCHED`/`IN_PROGRESS` responds the 422 of the status engine ("Moving to
'CANCELLED' from '{status}' is not allowed under this company's model.") before `TripStatusEffect` rejects it
again with its own message.

### 10.2 Route (`RouteStatus`) — the cycle of the plan VERSION

A `Trip` can have several `Route` versions over time; only one is current (`IsActive = 1`).

| From | To | Who | What it validates | Effects |
|---|---|---|---|---|
| — | `DRAFT` (initial) | System, when creating the first version or when adding the first shipment | — | Birth history |
| `DRAFT`/`PLANNED` | `OPTIMIZED` | `trips.optimize` | — | New version with the engine's sequence |
| `DRAFT`/`OPTIMIZED` | `ACTIVE` | Automatic when dispatching the `Trip` (section 6.1) | — | Freezes the version (immutable content) |
| `DRAFT`/`OPTIMIZED` | `ARCHIVED` (lateral, terminal) | Automatic when replacing the version (new optimization) or on "Delete route" | — | The version remains intact, view only |

### 10.3 OptimizationRun (`OptimizationRunStatus`) — log, without its own `[AuditEntity]`

| From | To | Who | What it validates | Effects |
|---|---|---|---|---|
| — | `PENDING` (initial) | System, when starting PHASE 1 of an optimization | — | Birth history |
| `PENDING` | `OK` | System, if the route did not change and the engine returned a valid result | — | `RouteId`, plan and totals |
| `PENDING` | `ERROR` | System, if the route changed, the engine failed/timed out or the result was invalid | — | `ErrorMessage` (trimmed to 4000 characters) |

### 10.4 RouteStop (`RouteStopStatus`)

This batch only creates stops in **`PENDING`** (when adding a shipment or when optimizing/reordering). The rest of the cycle
(`ON_THE_WAY`, `ARRIVED`, `COMPLETED`, `FAILED`) is moved by the driver app of **Batch 7** (departure, arrival
and POD events); the progress shown by the monitor (section 9) is already prepared to read them.

History of any of the four entities: `GET /api/v1/status/history/{ENTITY}/{id}` with `TRIP`, `ROUTE`,
`ROUTE_STOP` or `OPTIMIZATION_RUN`. Capabilities per status: `GET/PUT /api/v1/status/capabilities/TRIP` (only `TRIP`
brings a capability of its own in this batch, `EDIT_TRIP`, denied by default in `DISPATCHED`/`IN_PROGRESS`/
`COMPLETED`/`CANCELLED`). Lateral entries: `GET/PUT /api/v1/status/lateral-entries/TRIP` (`CANCELLED` from
`DRAFT`/`PLANNED`) and `.../ROUTE` (`ARCHIVED` from `DRAFT`/`OPTIMIZED`).

---

### 10.5 Two people editing routes at the same time

Several people of the same company can work on routes at the same time (placing pins, reordering, adding shipments, optimizing, dispatching). The system serves them **one after the other** so that none steps on the other's work; most of the time you do not even notice. There are two notices worth recognizing:

| Situation | Message | Code | What to do |
|---|---|---|---|
| Another person saved the same route while you had it open | `The record was modified by another user; reload and try again.` | 409 | Reload the route, check what changed and repeat your change. |
| Two changes coincided at the same instant and the system could not serve yours | `Another person is modifying this route at the same time; try again.` (code `concurrent_edit`) | 409 | Nothing was lost. Wait a few seconds and repeat the action. If it happens often, notify support. |

This second notice is rare: it appears only when several people coincide modifying routes in the same second. Outside routes, the system uses the same notice with the text `Another person is modifying this data at the same time; try again.`

## 11. Permissions and modules

| Permission | What it enables |
|---|---|
| `trips.view` | view routes (list, record, optimization runs), the "Unassigned" list and the monitor |
| `trips.plan` | create/edit/delete routes, reassign driver in bulk, add/remove shipments, reorder, manual pin and "Plan the day" |
| `trips.optimize` | optimize a route |
| `trips.dispatch` | view the dispatch selector, dispatch (individual/batch) and record the departure; also assign/reassign the driver of a special delivery (chapter 04, section 11) |
| `trips.scan` | use the Outbound scan station |
| `fleet.view`/`fleet.manage` | view/write dispatch zones and their members (chapter 04) |

The whole module lives under **LTL_GROUND** (core); dispatch zones remain under **CATALOG**, and the services of
this chapter that touch a driver or vehicle also require **CATALOG** to be on. Factory role templates:
**Dispatcher** (`Dispatcher`) brings `trips.plan`, `trips.dispatch`, `trips.optimize`, `trips.view` and `trips.scan`;
**Warehouse operator** (`WarehouseOperator`) brings no `trips.*` permission since 2026-10-01 (before it brought
`trips.view` and `trips.scan`; Luis removed them: the guard does not see routes or scan departures); **Read only** (`ReadOnly`) brings only
`trips.view`; **Driver** (`Driver`) brings no `trips.*` permission (its own access arrives with the driver
app, Batch 7).

Contacts (`POST /api/v1/contacts/{ENTITY}/{id}`) and custom fields (`PUT /api/v1/custom-fields/values/{ENTITY}/{id}`)
are available for `TRIP`, `ROUTE` and `ROUTE_STOP` under `trips.plan` (reading with `trips.view`).
`OPTIMIZATION_RUN` does **not** accept contacts or custom fields by loose id (it always responds `404`, the same
as `DRIVER_RATE` and `FLEET_DOCUMENT`): it is a read-only log, not an editable resource.

---

## 12. Analysis sources and audit

New source **`TRIP`** ("Routes", with `PlanDate` as date field), without `Amount` or `Rate`. The Shipments source
(`TRANSPORT_ORDER`) gains `TripCode`/`TripStatusCode`, `AssignedDriverCode`/`AssignedDriverName`/
`HasAssignedDriver`, `DispatchZoneCode` and `IsException` (`FAILED`, Failed attempt; `CANCELLED` does not count).

A new tenant (and the demo one) receive from the factory: the **"Routes"** view (date, number, zone, driver, vehicle,
status, stops, over the maximum, total distance; sorted by date descending); the indicators **"Shipments without an
assigned driver"** (active in `SUBMITTED`..`ASSIGNED` without a current route with a driver and without a paid trip of their own) and
**"Shipments in exception"** (`FAILED`); the indicator **"Routes over the maximum number of stops"**; and
the donut chart **"Routes by status"** (last 7 days). Every creation or change of a route, its shipments, the
route version and the zone members is recorded in `AuditLog` (`GET /api/v1/audit/changes?entityType=TRIP|ROUTE|
DISPATCH_ZONE|TRANSPORT_ORDER`); optimization runs do not generate `AuditLog` (they are already their own log).

---

## 13. Frequently asked questions

See [faq.md](faq.md), section "Batch 5 — Trips and routes", for each error message quoted in this chapter with its
cause and what to do.

## Location of the stops on the map (2026-10-22)

The Dispatch map and the Monitoring map draw the stops that have **latitude and longitude**. That location is looked up with Google when the stop **enters a route**:

- **When:** when building the route (Plan the day, New route or Add to route). Once, **in the background**: the dispatcher does not wait; within a few seconds the pins appear on the map by themselves (the route record keeps asking while some are missing, about 2 minutes).
- **What is looked up:** the stops of the current version of **unclosed** routes from yesterday onward, without a location. The whole consignee directory and the history are not geocoded.
- **The same thing is not paid for twice:** each address (per company, ignoring accents and case) is looked up **only once**; if two stops have the same address, or it was already looked up another day, what was saved is used. If Google did not find it, it is not asked again either.
- **Monthly cap:** `GoogleMaps:MonthlyCap` (by default 9,000 per month; see chapter 36). When the cap is reached there are no more queries that month: the stops remain without a pin and are resumed the following month or when the cap is raised. The dispatcher can place the pin by hand. The counter belongs to the application, per calendar month.
- **Accuracy:** a “rooftop” result (ROOFTOP) is **Exact**; everything else shows as **Approximate** on the map. A pin placed by hand always prevails.
- **Off by default:** it requires `GoogleMaps:Enabled = true` and the key (`GoogleMaps__ApiKey`). Without them the stops remain “No pin”, as before.
- **If Google fails** (bad key, quota, network): the stop is retried after 30 minutes, and three consecutive failures stop the cycle so as not to spend queries.
