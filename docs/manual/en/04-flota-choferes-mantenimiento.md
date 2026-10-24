# Chapter 04 — Fleet, drivers and maintenance (Batch 4)

This chapter describes the five panels of the Fleet module (Vehicles, Expiring documents, Preventive
maintenance, Work orders and Fuel log) and the master-detail **Drivers & rates** of the Lists
group (driver identity, licenses, certifications, devices, dispatch zones and the three pay
rates). It also closes the pending item of [chapter 03](03-ordenes-de-transporte.md): the special delivery with a driver.
The **Drivers & rates** web screen (Lists → Drivers & rates) is explained step by step in [Drivers & rates](frontend/fd1-choferes.md); this chapter is the server reference. The **Fleet & maintenance** screen (vehicles, expiring documents, dispatch zones and availability) is explained in [Fleet & maintenance](frontend/fd2-flota.md), and its preventive maintenance, work orders and fuel in [Maintenance, work orders and fuel](frontend/fd3-mantenimiento-combustible.md).
Each section states **what it does**, **who can**, **how it is used**, the **validations** with the exact message and the
HTTP code, and the **statuses** with their transitions and effects. The messages are verified against the code
(`src/Teikem.Domain/Fleet/*`, `src/Teikem.Infrastructure/Fleet/*`, `src/Teikem.Infrastructure/Services/{Vehicle,Driver,
DispatchZone,Fleet,Maintenance,FuelLog,DriverRate,DriverPayPolicy,DriverTrip,SpecialDelivery}*.cs`,
`src/Teikem.Api/Controllers/{Vehicles,Drivers,DispatchZones,Fleet,MaintenanceSchedules,MaintenanceWorkOrders,
FuelLogs,DriverRates,DriverPayPolicy,DriverTrips,SpecialDelivery}Controller.cs`). The questions and answers for each
message are in [faq.md](faq.md).

Conventions of the chapter:

- Resources with a status history (vehicle, driver, work order, paid trip) are identified by
  `publicId` (GUID) in the URL. A `publicId` of another company responds **404** as if it did not exist (no oracle).
- Children without an identity of their own (vehicle documents, licenses, certifications, devices, work order tasks) are
  exposed with an **integer id only under their parent's route**: an id that belongs to another parent (or another tenant) also
  responds **404** (protection against BOLA by child id).
- The whole module lives under the **CATALOG** module ("Clients and contracts, drivers and rates, fleet"): if the tenant
  turns it off, any endpoint of this chapter responds `403` `code: "module_disabled"`,
  `The 'CATALOG' module is not enabled for this company.` — except assigning/reassigning the driver of a
  special delivery itself, which lives under **LTL_GROUND** (section 12) but still requires CATALOG to be on internally.
- Fleet and driver compensation are separated by permission (R8): `fleet.view`/`fleet.manage`/`fleet.maintenance`
  for vehicles, documents and maintenance; `driverpay.view`/`driverpay.manage` for rates and paid trips.
- A validation error responds `400` with `title` and `errors` (`{ field: [message] }`); a conflict `409`; a
  status or business rule `422`; a foreign or nonexistent resource `404`; a missing permission `403`.
- Nothing is physically deleted: lists and children use `IsActive` (reversible); vehicle, driver, work
  order and paid trip also use a status cycle with a terminal stage (permanent deactivation).

---

## 1. Vehicles

### 1.1 List and record

What it does: lists the tenant's vehicles with a free-text search box (code, plate, VIN, make, model and the labels or
internal codes of type/ownership/fuel, ignoring accents and case) and multi-value filters by
list code (`vehicleType`, `ownership`, `fuelType`, `status`; accepts `?x=A&x=B` or `?x=A,B`). Each row carries
`nextDocumentExpiry`: the next expiry among the vehicle's **active and not superseded** documents (see
section 3). The record adds capacities, volume, current odometer and its documents.

Who can: `fleet.view`.

How it is used:
- `GET /api/v1/vehicles?search=&vehicleType=&ownership=&fuelType=&status=&includeInactive=false`
- `GET /api/v1/vehicles/{publicId}`

| Case | Exact message | HTTP |
|---|---|---|
| `vehicleType`/`ownership`/`fuelType` filter with an unknown code | `Unknown vehicle type: 'X'.` / `Unknown ownership: 'X'.` / `Unknown fuel type: 'X'.` | 400 |
| `status` filter with an unknown code | `Unknown status: 'X'.` (field `status`) | 400 |
| `publicId` of another tenant or nonexistent | `Vehicle not found.` | 404 |

### 1.2 Create

What it does: creates a vehicle. The code (`code`) is required, is saved in capitals and is **immutable**; the
vehicle is born in the initial status (**ACTIVE**) with `isActive: true`. The VIN, if entered, is normalized to
capitals without spaces.

Who can: `fleet.manage`.

How it is used: `POST /api/v1/vehicles`

```json
{
  "code": "V-001",
  "plateNumber": "ABC-1234",
  "vehicleType": "VAN",
  "ownership": "OWNED",
  "fuelType": "DIESEL",
  "make": "Ford",
  "model": "Transit",
  "modelYear": 2023,
  "vin": "1FTBW2CM0NKA00000",
  "maxWeightKg": 1500,
  "maxVolumeM3": 12.5,
  "maxStops": 40,
  "currentOdometerKm": 1000
}
```

| Field / case | Exact message | HTTP |
|---|---|---|
| Empty `code` | `The vehicle code is required.` (field `code`) | 400 |
| `code` longer than 30 characters | `The code cannot exceed 30 characters.` (field `code`) | 400 |
| Repeated `code` (even if the other one is retired) | `A vehicle with that code already exists.` | 409 |
| Unknown `vehicleType`/`ownership`/`fuelType` | `Unknown vehicle type: 'X'.` / `Unknown ownership: 'X'.` / `Unknown fuel type: 'X'.` | 400 |
| `vin` longer than 40 characters | `The VIN allows at most 40 characters.` (field `vin`) | 400 |
| `vin` repeated among **active** vehicles | `An active vehicle with that VIN already exists.` | 409 |
| `plateNumber`/`make`/`model` longer than 20/60/60 characters | `It cannot exceed {N} characters.` | 400 |
| Negative `maxWeightKg`/`maxVolumeM3` | `Capacity cannot be negative.` | 400 |
| `maxStops` &lt; 1 | `The stop limit must be 1 or more.` | 400 |
| Negative `currentOdometerKm` | `The odometer cannot be negative.` | 400 |
| `modelYear` outside 1900..(current year + 1) | `The model year must be between 1900 and {year+1}.` | 400 |
| `maxWeightKg`/`maxVolumeM3`/`currentOdometerKm` with too many decimals or out of the DECIMAL(12,3)/(12,4)/(12,1) range | `The value allows at most {N} decimals and must be less than {limit}.` | 400 |

### 1.3 Inline edit

What it does: `PATCH` with `null` = no change; accepts an optional `rowVersion` to detect concurrent edits. The
code is never edited. If `currentOdometerKm` arrives, the vehicle row is locked (`UPDLOCK, ROWLOCK`) and the
reading must be **greater than or equal to** the last one recorded by an active fuel fill or a **CLOSED** work
order (sections 9 and 8): this way a manual correction never leaves the odometer below a fact already
recorded, nor competes with a concurrent fill or work order closing.

Who can: `fleet.manage`.

How it is used: `PATCH /api/v1/vehicles/{publicId}`

| Case | Exact message | HTTP |
|---|---|---|
| `code`/`homeWarehouseId` in the body | `The vehicle code is set when it is created; it cannot be changed.` | 400 |
| `vin` repeated among active vehicles (excluding its own) | `An active vehicle with that VIN already exists.` | 409 |
| `currentOdometerKm` lower than the last recorded reading | `The odometer cannot be lower than the last recorded reading ({km} km on yyyy-MM-dd).` (field `currentOdometerKm`) | 400 |
| `rowVersion` does not match (someone else edited it) | `The record was modified by another user; reload and try again.` | 409 |
| Permanently retired vehicle (terminal status): view only (2026-10-12) | `The vehicle is permanently retired; only its history can be viewed.` | 409 |
| Same validations of capacities, year, VIN and precision as the create | (same as 1.2) | 400 |

### 1.4 Status and soft deactivation

What it does: changes the vehicle's status (`VehicleStatus`: **ACTIVE** ↔ **MAINTENANCE**, lateral and reversible;
**INACTIVE**, terminal = permanent retirement) and the **Active** checkbox (`IsActive`, reversible, deletes nothing). See the
transition table in section 11.1.

Who can: `fleet.manage`.

How it is used:
- `POST /api/v1/vehicles/{publicId}/status` — `{ "toCode": "MAINTENANCE", "comment": "…" }`
- `POST /api/v1/vehicles/{publicId}/deactivate` / `.../reactivate` — 204, no body

| Case | Exact message | HTTP |
|---|---|---|
| Empty `toCode` | `The target status is required.` (field `toCode`) | 400 |
| Reactivating a vehicle in terminal status (INACTIVE) | `The vehicle is permanently retired; it cannot be reactivated.` | 409 |
| Reactivating when the VIN is already used by another active vehicle | `An active vehicle with that VIN already exists.` | 409 |

### 1.5 Vehicle documents (registration, insurance, inspection, permit)

What it does: create, edit and soft-deactivate vehicle documents (`VehicleDocType`: `REGISTRATION`, `INSURANCE`,
`INSPECTION`, `PERMIT`). Each document carries its expiry state (`OK` / `EXPIRING` ≤ 30 days / `EXPIRED` /
`NO_EXPIRY` with no date) and `isSuperseded`: **true** if the same vehicle has another **active document of the same
type** with a later expiry — a superseded document does not block, does not count toward `nextDocumentExpiry` and does not
appear in "Expiring documents" (section 3), but is still visible in the record. Attachments (file) are not
exposed in this batch: there is no file provider.

Who can: `fleet.view` to list, `fleet.manage` to write.

How it is used:
- `GET /api/v1/vehicles/{publicId}/documents?includeInactive=false`
- `POST /api/v1/vehicles/{publicId}/documents` — `{ "docType": "INSURANCE", "docNumber": "...", "issuedDate": "2026-01-01", "expiryDate": "2027-01-01" }`
- `PATCH /api/v1/vehicles/{publicId}/documents/{id}` — `null` = no change; `clearIssuedDate`/`clearExpiryDate` clear the date
- `POST /api/v1/vehicles/{publicId}/documents/{id}/deactivate`

| Field / case | Exact message | HTTP |
|---|---|---|
| Empty `docType` | `The document type is required.` (field `docType`) | 400 |
| Unknown `docType` | `Unknown vehicle document type: 'X'.` (field `docType`) | 400 |
| `docNumber` longer than 80 characters | `It cannot exceed 80 characters.` (field `docNumber`) | 400 |
| `expiryDate` earlier than `issuedDate` | `The expiry date cannot be earlier than the issue date.` (field `expiryDate`) | 400 |
| `id` of a document of another vehicle or another tenant | `Document not found.` | 404 |
| Creating, editing or deactivating a document of a permanently retired vehicle (2026-10-12) | `The vehicle is permanently retired; only its history can be viewed.` | 409 |

---

## 2. Drivers (identity, licenses, certifications, devices)

### 2.1 Create and record

What it does: creates a driver. The code (`code` = `EmployeeCode`) is required, is saved in capitals and is
**immutable**; the name is required (maximum 150 characters). The driver is born in the initial status (**ACTIVE**)
**with no rate row at all** (the "empty record" is the absence of rows, not a row at $0). Optionally it accepts a
primary dispatch zone, its own stop limit and the link to an internal user (section 2.3).
`EffectiveMaxStops` in the list and the record is the driver's own limit or, if there is none, the tenant default
(`Tenant.MaxStopsPerRouteDefault`).

Who can: `fleet.view` to list and view the record; `fleet.manage` to create.

How it is used:
- `GET /api/v1/drivers?search=&status=&dispatchZoneId=&includeInactive=false`
- `GET /api/v1/drivers/{publicId}`
- `POST /api/v1/drivers`

```json
{ "code": "D-001", "fullName": "Juan Pérez", "dispatchZoneId": 1, "maxStopsPerRoute": null, "hireDate": "2024-03-01" }
```

| Field / case | Exact message | HTTP |
|---|---|---|
| Empty `code` | `The driver's code is required.` (field `code`) | 400 |
| `code` longer than 30 characters | `The code cannot exceed 30 characters.` (field `code`) | 400 |
| Repeated `code` (even if the other one is deleted) | `A driver with that code already exists.` | 409 |
| Empty `fullName` | `The driver's name is required.` (field `fullName`) | 400 |
| `fullName` longer than 150 characters | `The driver's name allows at most 150 characters.` (field `fullName`) | 400 |
| `maxStopsPerRoute` &lt; 1 | `The stop limit must be 1 or more.` (field `maxStopsPerRoute`) | 400 |
| `dispatchZoneId` of another tenant or nonexistent | `Dispatch zone not found.` | 404 |
| Inactive `dispatchZoneId` | `The dispatch zone is inactive.` (field `dispatchZoneId`) | 400 |
| `publicId`/`code` of a driver that does not exist in the tenant | `Driver not found.` | 404 |
| `status` filter with an unknown code | `Unknown driver status: 'X'.` (field `status`) | 400 |

### 2.2 Inline edit

What it does: `PATCH` with `null` = no change; `clearZone`/`clearMaxStopsPerRoute`/`clearUser` clear the corresponding
value (they win if they arrive together with the new value). The code is never edited. Editing a driver in
terminal status (deleted) responds 409 before touching anything.

Who can: `fleet.manage` (changing or removing the linked user also requires `admin.users`, section 2.3).

How it is used: `PATCH /api/v1/drivers/{publicId}`

| Case | Exact message | HTTP |
|---|---|---|
| `code`/`employeeCode` in the body | `The driver's code is set when it is created; it cannot be changed.` | 400 |
| Deleted driver (terminal status) | `The driver was deleted; only their history can be viewed.` | 409 |
| Same validations of name, stop limit and zone as the create | (same as 2.1) | 400 / 404 |
| `rowVersion` does not match | `The record was modified by another user; reload and try again.` | 409 |

### 2.3 Link to an internal user (for the future driver app)

What it does: links the driver to a user of the company (`Driver.UserId`, unique per tenant). The user must: exist
with a membership in the tenant, be of **internal** type (not portal), have an **ACTIVE** membership, and not be already
linked to another driver. The service requires the `admin.users` permission BEFORE touching anything, both to link and
to unlink (`clearUser`). The link is only revalidated when it really changes (resending the same `userId` does not
require `admin.users` again).

Who can: `fleet.manage` + `admin.users`.

How it is used: include `userId` (or `clearUser: true`) in `POST /api/v1/drivers` or `PATCH /api/v1/drivers/{publicId}`.

| Case | Exact message | HTTP |
|---|---|---|
| Missing `admin.users` | `Missing permission 'admin.users'.` | 403 |
| `userId` without a membership in the tenant (or from another company) | `User not found.` | 404 |
| `userId` is a **portal** user, not internal | `Only an internal user can be linked to a driver.` (field `userId`) | 400 |
| The user's membership is not **ACTIVE** | `The user does not have an active membership in this company.` | 409 |
| `userId` already linked to another driver | `The user is already linked to another driver.` | 409 |

### 2.4 Status, Active checkbox and "Delete driver"

What it does: three different actions.
- Status change (`DriverStatus`: **ACTIVE** ↔ **UNAVAILABLE**, lateral).
- **Active** checkbox (`IsActive`), reversible: takes the driver out of dispatch without touching anything else.
- **Delete driver** (`DELETE`): transitions to the terminal status **INACTIVE** (permanent deactivation) with no physical delete.
  Its effects (section 11.2): `IsActive=0` is set, the user is unlinked, their devices are deactivated, the zone is
  removed and their open rates are closed **today** (section 8). Their paid trips (section 10) are kept
  so what is pending can be paid. A deleted driver can only be viewed.

Who can: `fleet.manage`.

How it is used:
- `POST /api/v1/drivers/{publicId}/status` — `{ "toCode": "UNAVAILABLE" }`
- `POST /api/v1/drivers/{publicId}/deactivate` / `.../reactivate` — 204
- `DELETE /api/v1/drivers/{publicId}` — optional body `{ "comment": "…" }`

| Case | Exact message | HTTP |
|---|---|---|
| Empty `toCode` | `The target status is required.` (field `toCode`) | 400 |
| Reactivating a deleted driver | `The driver was deleted; they cannot be reactivated.` | 409 |
| Deleting an already deleted driver | `The driver was already deleted.` | 409 |

### 2.5 Licenses and certifications

What it does: create, edit and soft-deactivate the driver's licenses (`LicenseClass`) and certifications (`CertificationType`).
Same as vehicle documents: `expiryState`, `daysToExpiry` and `isSuperseded` (current per type:
same class/type, later expiry in another active row of the same driver).

Who can: `fleet.view` to list, `fleet.manage` to write.

How it is used:
- `GET/POST /api/v1/drivers/{publicId}/licenses`, `PATCH .../licenses/{id}`, `POST .../licenses/{id}/deactivate`
- Same routes under `/certifications`

| Field / case | Exact message | HTTP |
|---|---|---|
| Empty `licenseClass` | `The license class is required.` (field `licenseClass`) | 400 |
| Unknown `licenseClass` | `Unknown license class: 'X'.` (field `licenseClass`) | 400 |
| Empty `licenseNumber` | `The license number is required.` (field `licenseNumber`) | 400 |
| `licenseNumber` longer than 60 characters | `The license number allows at most 60 characters.` | 400 |
| Empty `certType` | `The certification type is required.` (field `certType`) | 400 |
| Unknown `certType` | `Unknown certification type: 'X'.` (field `certType`) | 400 |
| `certNumber` longer than 60 characters | `The certification number allows at most 60 characters.` | 400 |
| Expiry date earlier than the issue date | `The expiry date cannot be earlier than the issue date.` (field `expiryDate`) | 400 |
| License/certification `id` of another driver | `License not found.` / `Certification not found.` | 404 |
| The driver is deleted (terminal) | `The driver was deleted; only their history can be viewed.` | 409 |

### 2.6 Devices (registered by the driver app, Batch 7)

What it does: lists and deactivates the driver's devices (platform, app version, last connection). The service
**never** returns the `PushToken`; the DTO only carries `hasPushToken`.

Who can: `fleet.view` to list, `fleet.manage` to deactivate.

How it is used:
- `GET /api/v1/drivers/{publicId}/devices?includeInactive=false`
- `POST /api/v1/drivers/{publicId}/devices/{id}/deactivate`

| Case | Exact message | HTTP |
|---|---|---|
| `id` of a device of another driver | `Device not found.` | 404 |

### 2.7 Dispatch zones

What it does: minimal CRUD of zones (code, name, active). The name of a driver's **primary** zone is their
"Area" in the driver list. Zone members (municipalities/ZIP codes) and secondary zones are left for the
Dispatch module of a later batch.

Who can: `fleet.view` to list, `fleet.manage` to write.

How it is used:
- `GET /api/v1/dispatch-zones?includeInactive=false`
- `POST /api/v1/dispatch-zones` — `{ "code": "Z-01", "name": "Toa Baja · Bayamón" }`
- `PATCH /api/v1/dispatch-zones/{id}` — only `name`
- `POST /api/v1/dispatch-zones/{id}/deactivate` / `.../reactivate`

| Field / case | Exact message | HTTP |
|---|---|---|
| Empty `code` | `The zone code is required.` (field `code`) | 400 |
| `code` longer than 20 characters | `The code cannot exceed 20 characters.` | 400 |
| Repeated `code` | `A dispatch zone with that code already exists.` | 409 |
| `name` longer than 120 characters | `The zone name allows at most 120 characters.` | 400 |
| `code` in the `PATCH` | `The zone code is set when it is created; it cannot be changed.` (field `code`) | 400 |
| Deactivating a zone with active drivers assigned (as primary) | `The zone has drivers assigned; reassign them before deactivating it.` | 409 |
| Zone `id` nonexistent in the tenant | `Dispatch zone not found.` | 404 |

---

### 2.8 Shipments without a zone: why and how to fix them (2026-10-21)

A shipment takes its zone from the **postal code or the town of its delivery address**, compared against the criteria of the active zones (exact postal code, postal range or municipality; the most specific one wins). A town written as **“BARRIO, TOWN”** (for example “LA PLATA, AIBONITO”, the usual form in migrated data) is compared first as a whole and then by its last segment, the town (“AIBONITO”); when it is added to a zone, the town is added. When no zone covers it, the shipment stays “No zone” and cannot be planned by zone.

In **Fleet → Dispatch zones**, at the top, the **“Shipments without a zone”** panel appears (only if there are active shipments without a zone; visible to anyone with `fleet.view`). It groups the shipments by **postal code and town** and states the reason for each group:

| Reason | What it means | What to do |
|---|---|---|
| **No zone covers it** | That postal code and town are not in any active zone. | **“Add to a zone”** button (needs `fleet.manage`): choose the zone and whether it is added by **postal code** or by **town**; “Add and assign” saves it as a criterion of the zone and then assigns the zone to all the shipments that can already have it. |
| **Several zones cover it** | Two zones tie at the same level. Which ones are shown. | Remove the repeated criterion from one of them (through the API: `DELETE /api/v1/dispatch-zones/{id}/members/{memberId}`). |
| **The address has no postal code or town** | The delivery stop does not have that data. | It is fixed in the consignee's or the shipment's address; the panel cannot guess it. |
| **A zone covers it: assign it** | Today a zone does cover it (it was added after the shipment was created). | **“Assign zone to shipments”** button of the panel (`POST /api/v1/dispatch-zones/assign-orders`). |

`GET /api/v1/dispatch-zones/unresolved-orders?top=100` (`fleet.view`) returns the same summary: `totalOrders`, `groups` and `rows` with `postalCode`, `city`, `orders`, `reason` (`NO_MATCH`, `AMBIGUOUS`, `NO_DATA` or `RESOLVABLE`) and `candidates`. Up to 500 groups, those with the most shipments first. New shipments take the zone on their own when created or when their consignee changes; the panel is mainly for what already exists (for example, what was migrated from Colavora).

| Message | HTTP | What to do |
|---|---|---|
| “Choose the zone.” | (screen) | Pick the zone before adding. |
| The one saying the criterion clashes with another active zone | 409 | That postal code or town already belongs to another active zone: remove it from there or add it to that zone. |

## 3. Expiring documents (cross-cutting panel)

What it does: joins in a single table the vehicle documents, licenses and certifications that are expired or that
expire within a window (`withinDays`, 0..365, 30 by default), of owners that are **active and not in terminal
status**. Only the **current document of each type** counts ("current document per type" rule, the same one
used by the record, the dispatch availability and the `FLEET_DOCUMENT` analysis source): an expired document that
has already been renewed (another active one of the same type with a later expiry) does not appear here.

Who can: `fleet.view`.

How it is used: `GET /api/v1/fleet/expiring-documents?withinDays=30&docType=&entity=&includeExpired=true`
- `docType` ∈ `REGISTRATION`, `INSURANCE`, `INSPECTION`, `PERMIT` (vehicle), `LICENSE`, `CERTIFICATION` (driver).
- `entity` ∈ `VEHICLE`, `DRIVER`.
- `includeExpired=false` removes the already expired ones (leaves only those expiring within the window).

| Case | Exact message | HTTP |
|---|---|---|
| `withinDays` outside 0..365 | `withinDays must be between 0 and 365.` (field `withinDays`) | 400 |
| Unknown `docType` | `Unknown document type: 'X'.` (field `docType`) | 400 |
| Unknown `entity` | `Unknown entity: 'X'; use VEHICLE or DRIVER.` (field `entity`) | 400 |

---

## 4. Dispatch availability (R7)

What it does: evaluates, for a date (today UTC by default), whether each driver and each vehicle of the tenant is available
for dispatch, with the list of reasons. It is the same rule used internally by the assignment of a driver to a
special delivery (section 12) and the one the future Dispatch planner will use.

**Blocking** (the resource is left `available: false`):

| Code | Reason | Message |
|---|---|---|
| `DRIVER_INACTIVE` | Driver with the Active checkbox off | `The driver is inactive.` |
| `DRIVER_STATUS` | Driver in a status other than the initial one (e.g. `UNAVAILABLE`) | `Driver in status '{status}'.` |
| `NO_VALID_LICENSE` | No license registered, or all expired | `No valid license.` / `License expired on yyyy-MM-dd.` |
| `VEHICLE_INACTIVE` | Vehicle with the Active checkbox off | `The vehicle is inactive.` |
| `VEHICLE_STATUS` | Vehicle in a status other than the initial one (e.g. `MAINTENANCE`) | `Vehicle in status '{status}'.` |
| `VEHICLE_DOC_EXPIRED` | Expired vehicle document (not superseded) | `{Document type} expired on yyyy-MM-dd.` |
| `WORK_ORDER_IN_PROGRESS` | Vehicle with a work order in `IN_PROGRESS` | `Work order {number} in progress.` |

**Warning only** (they do not block; `available` can stay `true`): `CERT_EXPIRED` (expired certification),
`DOC_EXPIRING` (document or license expiring in 30 days or less), `VEHICLE_NO_DOCUMENTS` (`The vehicle has no
documents registered.`, vehicle with no document entered).

**Overdue preventive maintenance is not evaluated here** (it is a separate panel, section 6).

Who can: `fleet.view`.

How it is used: `GET /api/v1/fleet/availability?date=&onlyAvailable=false` (`date` = today UTC by default).

---

## 5. Preventive maintenance — programs

What it does: defines maintenance programs for **one vehicle** or for **one vehicle type** (not both), with a
trigger by mileage, by time or both (the worse of the two rules). A program by type is evaluated
vehicle by vehicle against its last **CLOSED** work order linked to that program; with none, it stays "No
history". There is no automatic generation of work orders (there is no job): the program only reports the state.

Who can: `fleet.view` to list and for the "due" panel; `fleet.maintenance` to write.

How it is used:
- `GET /api/v1/maintenance-schedules?includeInactive=false`
- `GET /api/v1/maintenance-schedules/due?vehiclePublicId=&status=` — state per (program, vehicle):
  `OK` (Up to date), `DUE_SOON` (Due soon, ≤ 10 % of the interval remaining), `OVERDUE` (Overdue) or `NO_BASELINE`
  (No history: the last service or the current odometer is missing, if the trigger uses mileage).
- `POST /api/v1/maintenance-schedules` — `{ "name": "...", "vehiclePublicId": "...", "trigger": "MILEAGE", "intervalKm": 5000 }`
- `PATCH /api/v1/maintenance-schedules/{id}` — `null` = no change; `clearIntervalKm`/`clearIntervalDays` remove an interval
- `POST /api/v1/maintenance-schedules/{id}/deactivate` / `.../reactivate`

| Field / case | Exact message | HTTP |
|---|---|---|
| `vehiclePublicId` and `vehicleType` together, or neither | `Choose the vehicle or the vehicle type of the program, not both.` (field `vehiclePublicId`) | 400 |
| Empty `name` | `The program name is required.` (field `name`) | 400 |
| `name` longer than 150 characters | `The program name allows at most 150 characters.` | 400 |
| Empty `trigger` | `The program trigger is required.` (field `trigger`) | 400 |
| Unknown `trigger` | `Unknown trigger: 'X'.` (field `trigger`) | 400 |
| Unknown `vehicleType` | `Unknown vehicle type: 'X'.` (field `vehicleType`) | 400 |
| `MILEAGE`/`BOTH` without `intervalKm` &gt; 0 | `A mileage program needs an interval in km greater than 0.` (field `intervalKm`) | 400 |
| `TIME`/`BOTH` without `intervalDays` &gt; 0 | `A time program needs an interval in days greater than 0.` (field `intervalDays`) | 400 |
| `intervalDays` greater than 36500 | `The interval in days allows at most 36500.` | 400 |
| `lastServiceKm`/`lastServiceDate` in a program **by type** | `The last service is only entered in single-vehicle programs; in programs by type it is taken from their closed work orders.` (field `lastServiceKm`) | 400 |
| Negative `lastServiceKm` | `The last service reading cannot be negative.` | 400 |
| Future `lastServiceDate` | `The last service date cannot be in the future.` | 400 |
| `intervalKm`/`lastServiceKm` with too many decimals or out of the DECIMAL(12,1) range | `The value allows at most 1 decimals and must be less than {limit}.` (the server always writes “decimals”, even with 1) | 400 |
| Program `id` nonexistent in the tenant | `Maintenance program not found.` | 404 |
| Unknown `status` filter in `due` | `Unknown status: 'X'.` (field `status`) | 400 |

---

## 6. Work orders

### 6.1 Create and automatic numbering

What it does: creates a work order (WO) on an active, not retired vehicle. The number (`OT-#####`) is
assigned by the system, consecutive per company; it is never typed. If a program is given, the maintenance type is
**PREVENTIVE** by default and the program must apply to that vehicle (by vehicle or by its type). The WO is born in
**OPEN**.

Who can: `fleet.maintenance`.

How it is used: `POST /api/v1/maintenance-work-orders`. The list `GET /api/v1/maintenance-work-orders` (filters `vehiclePublicId`, `status`, `maintenanceType`, `scheduleId`, `from`, `to`, `includeInactive`, `skip`, `take` from 1 to 500) returns an **array without a total**, from the most recent to the oldest; each row carries `laborCost`, `partsCost` and `totalCost` (F-D3 added labor and parts). To read them all, request 200 at a time until you receive an incomplete page.

```json
{ "vehiclePublicId": "...", "maintenanceType": "PREVENTIVE", "scheduleId": 3, "scheduledDate": "2026-10-01" }
```

| Field / case | Exact message | HTTP |
|---|---|---|
| Inactive or permanently retired vehicle | `The vehicle is inactive; reactivate it to register work orders or fuel fills.` | 409 |
| Empty or all-zeros `vehiclePublicId` (F-D3: it used to respond 404 “Vehicle not found.”) | `The work order vehicle is required.` (field `vehiclePublicId`) | 400 |
| Neither `maintenanceType` nor `scheduleId` | `The maintenance type is required.` (field `maintenanceType`) | 400 |
| Unknown `maintenanceType` | `Unknown maintenance type: 'X'.` (field `maintenanceType`) | 400 |
| `scheduleId` nonexistent in the tenant | `Maintenance program not found.` | 404 |
| Inactive `scheduleId` | `The maintenance program is inactive.` (field `scheduleId`) | 400 |
| `scheduleId` does not apply to that vehicle (neither by vehicle nor by its type) | `The maintenance program does not apply to this vehicle.` (field `scheduleId`) | 400 |
| `scheduleId` (preventive) with a `maintenanceType` other than PREVENTIVE | `A corrective order is not linked to a preventive program.` (field `maintenanceType`) | 400 |
| Negative `laborCost`/`partsCost` | `Costs cannot be negative.` | 400 |
| Negative `odometerKm` | `The odometer cannot be negative.` | 400 |
| Unknown `currency` | `Unknown currency: 'X'.` (field `currency`) | 400 |
| Costs/odometer with too many decimals or out of the DECIMAL(18,4)/(12,1) range | `The value allows at most {N} decimals and must be less than {limit}.` | 400 |
| Number clash from a concurrent create (internal retries exhausted) | `A work order with that number already exists; try again.` | 409 |

Eight simultaneous creates on the same vehicle receive eight different consecutive numbers, with no 409 or 500 (the
counter is locked internally until the commit).

### 6.2 Tasks and costs

What it does: adds, edits and removes (soft deactivation) the WO's tasks. **With** active tasks, the header costs
(`laborCost`, `partsCost`) are the **sum** of the active tasks and are not entered by hand; **without** tasks, they are
entered directly in the header. `totalCost` is always `laborCost + partsCost` (computed column in the DB).
Editing tasks requires the same capability as editing the header (`EDIT_WORK_ORDER`, section 6.3).

Who can: `fleet.maintenance`.

How it is used:
- `POST /api/v1/maintenance-work-orders/{publicId}/tasks` — `{ "description": "Cambio de aceite", "partCost": 40, "laborCost": 20 }`
- `PATCH /api/v1/maintenance-work-orders/{publicId}/tasks/{taskId}` — includes `isCompleted`
- `POST /api/v1/maintenance-work-orders/{publicId}/tasks/{taskId}/deactivate`

| Field / case | Exact message | HTTP |
|---|---|---|
| Empty `description` (create) | `The task description is required.` (field `description`) | 400 |
| `description` longer than 250 characters | `The task description allows at most 250 characters.` | 400 |
| Negative `partCost`/`laborCost` | `Costs cannot be negative.` | 400 |
| `taskId` of another WO (or another tenant) | `Task not found.` | 404 |
| Trying to enter `laborCost`/`partsCost` in the **header** when the WO already has active tasks | `The order has tasks: labor and parts costs are calculated from the sum of its tasks.` | 400 |

### 6.3 Header edit and the EDIT_WORK_ORDER capability

What it does: edits scheduled date, odometer, vendor, costs (only without tasks), currency and notes. The number and the
vehicle are immutable. Editing requires the `EDIT_WORK_ORDER` capability on the WO's current status: by default
it is **denied in CLOSED and CANCELLED** (the tenant can relax it from
`PUT /api/v1/status/capabilities/WORK_ORDER`, chapter 01).

Who can: `fleet.maintenance`.

How it is used: `PATCH /api/v1/maintenance-work-orders/{publicId}`

| Case | Exact message | HTTP |
|---|---|---|
| `number`/`vehiclePublicId` in the body | `The number and the vehicle of the work order are set when it is created.` | 400 |
| WO in CLOSED/CANCELLED (capability denied) | `The current status does not allow the action 'EDIT_WORK_ORDER'.` | 422 |
| `vendor` longer than 150 characters | `The vendor allows at most 150 characters.` | 400 |
| `rowVersion` does not match | `The record was modified by another user; reload and try again.` | 409 |

### 6.4 Status and effect on the vehicle

What it does: moves the WO through `WorkOrderStatus` (**OPEN** → **IN_PROGRESS** → **CLOSED**; **CANCELLED** terminal). When
**closing**, it requires that no incomplete active tasks remain and, if the associated program is by mileage (or
both), the odometer reading (from the body or already entered in the header). The automatic effects on the
vehicle and the program are in section 11.3.

Who can: `fleet.maintenance`.

How it is used: `POST /api/v1/maintenance-work-orders/{publicId}/status`

```json
{ "toCode": "CLOSED", "completedDate": "2026-10-05", "odometerKm": 5200 }
```

| Case | Exact message | HTTP |
|---|---|---|
| Empty `toCode` | `The target status is required.` (field `toCode`) | 400 |
| Future `completedDate` (when closing) | `The closing date cannot be in the future.` (field `completedDate`) | 400 |
| Incomplete active tasks (when closing) | `The order has {n} task(s) not completed; mark them as completed or remove them before closing it.` | 422 |
| No odometer reading and the program is by mileage or both (when closing) | `Enter the odometer reading to close an order of a mileage-based program.` (field `odometerKm`) | 400 |

`OPEN` → `CLOSED` directly is also valid (a WO without passing through `IN_PROGRESS`): the status engine allows
reaching a terminal out of order. History: `GET /api/v1/status/history/WORK_ORDER/{id}`.

---

## 7. Fuel log

What it does: records fuel fills per vehicle (liters, cost, optional odometer, optional driver,
station). Km/L and cost/km **are calculated on read**, not stored: each fill with an odometer is compared
against the previous fill with an odometer of the same vehicle (liters/cost of the intermediate fills without an odometer are
attributed to the next segment). The summary per vehicle aggregates the whole series. A fill's odometer must be
**monotonic by date** within the same vehicle (neither lower than an earlier one nor higher than a later one). Each active
fill raises `Vehicle.CurrentOdometerKm` (monotonic maximum); a deactivated fill stops counting but does not lower the
vehicle's odometer.

Who can: `fleet.view` to list, `fleet.maintenance` to write.

How it is used:
- `GET /api/v1/fuel-logs?vehiclePublicId=&driverPublicId=&fromUtc=&toUtc=&includeInactive=false&skip=0&take=100`
  (`fromUtc` inclusive, `toUtc` exclusive; `take` 1..500)
- `POST /api/v1/fuel-logs` — `{ "vehiclePublicId": "...", "fillDateUtc": "2026-09-20T10:00:00Z", "liters": 40, "totalCost": 60, "odometerKm": 5200 }`
- `PATCH /api/v1/fuel-logs/{id}` — `null` = no change; `clearOdometer`/`clearDriver` remove the value
- `POST /api/v1/fuel-logs/{id}/deactivate`

| Field / case | Exact message | HTTP |
|---|---|---|
| Inactive or permanently retired vehicle | `The vehicle is inactive; reactivate it to register work orders or fuel fills.` | 409 |
| `liters` ≤ 0 | `Liters must be greater than 0.` (field `liters`) | 400 |
| Negative `totalCost` | `The total cost cannot be negative.` (field `totalCost`) | 400 |
| Negative `odometerKm` | `The odometer cannot be negative.` (field `odometerKm`) | 400 |
| Future `fillDateUtc` (more than 5 minutes) | `The fill date cannot be in the future.` (field `fillDateUtc`) | 400 |
| Empty/default `fillDateUtc` | `The fill date is required.` (field `fillDateUtc`) | 400 |
| Empty `vehiclePublicId` (create) | `The fill vehicle is required.` (field `vehiclePublicId`) | 400 |
| Trying to change the vehicle of an existing fill | `The vehicle of a fill cannot be changed; deactivate it and register another one.` (field `vehiclePublicId`) | 400 |
| Correcting an already deactivated fill | `The fuel fill is inactive; it cannot be corrected.` | 409 |
| `odometerKm` lower than that of an earlier fill of the same vehicle | `The odometer reading ({km} km) is lower than that of an earlier fill of the same vehicle ({km} km on yyyy-MM-dd).` (field `odometerKm`) | 400 |
| `odometerKm` higher than that of a later fill of the same vehicle | `The odometer reading ({km} km) is higher than that of a later fill of the same vehicle ({km} km on yyyy-MM-dd).` (field `odometerKm`) | 400 |
| `station` longer than 150 characters | `The station allows at most 150 characters.` | 400 |
| Unknown `currency` | `Unknown currency: 'X'.` (field `currency`) | 400 |
| Negative `skip` | `skip cannot be negative.` | 400 |
| `take` outside 1..500 | `take must be between 1 and 500.` | 400 |
| Fill of another tenant (or nonexistent) | `Fuel fill not found.` | 404 |
| Concurrency clash when saving | `The fuel fill changed while it was being saved; reload and try again.` | 409 |
| DECIMAL precision clash in liters/cost/odometer | `The value allows at most {N} decimals and must be less than {limit}.` | 400 |

---

## 8. Driver rates

The three rates hang from the driver (there is no "agreement" header table): the absence of rows is the "empty
record". All are **effective-dated**: editing = closing the open row (`effectiveTo` exclusive) and opening a
new version; removing = closing with no replacement. No new date (`effectiveFrom`/`effectiveTo`) can be
before today. No rate body carries the driver: it always comes from the `/drivers/{publicId}` route.

Who can: `driverpay.view` to view, `driverpay.manage` to write (permissions separate from `fleet.*`,
R8). A **deleted** driver (terminal status) can only be viewed: every write responds 409.

How it is used (query): `GET /api/v1/drivers/{publicId}/rates?asOf=&includeHistory=false` — `asOf` (today by
default) picks the effective period shown; `includeHistory=true` adds the closed or future rows.

| Case common to the three rates | Exact message | HTTP |
|---|---|---|
| Deleted driver (terminal), any write | `The driver was deleted; their rates and trips can only be viewed.` | 409 |
| Rate `id` that does not belong to THAT driver | `Rate not found.` | 404 |
| `effectiveFrom`/`effectiveTo` before today | `The date can’t be before today: rate history isn’t rewritten.` | 400 |
| Negative `rate` | `The rate can’t be negative.` (field `rate`) | 400 |
| `rate` with too many decimals or out of the DECIMAL(18,4) range | `The value allows at most 4 decimals and must be less than {limit}.` | 400 |

### 8.1 Rate per delivery (service + package type)

What it does: pays a fixed amount for each delivery of an **exact service + package type** (there is no
"any package" wildcard).

How it is used:
- `POST /api/v1/drivers/{publicId}/delivery-rates` — `{ "serviceType": "STANDARD", "packageType": "BOX", "rate": 3.50 }`
- `PATCH /api/v1/drivers/{publicId}/delivery-rates/{id}` — only `rate`/`effectiveFrom`
- `POST /api/v1/drivers/{publicId}/delivery-rates/{id}/close`

| Field / case | Exact message | HTTP |
|---|---|---|
| Empty `serviceType`/`packageType` | `Enter the service and the package type of the rate.` (corresponding field) | 400 |
| Unknown `serviceType` | `Unknown service type: 'X'.` | 400 |
| Unknown `packageType` | `Unknown package type: 'X'.` | 400 |
| There is already a current rate for the same service + package | `The driver already has a current rate for {Service} + {Package}; edit that rate or close it before adding another.` | 409 |
| `serviceType`/`packageType` in the `PATCH` | `The service and the package are set when the rate is created; remove the row and create a new one.` | 400 |
| Editing/closing an already closed row | `The rate is already closed; add a new one if you need to pay it again.` | 409 |

### 8.2 Rate per attempt and attempt levels

What it does: pays by delivery attempt number (1, 2, 3…). The attempt levels (`AttemptLevels`, 1..20) are a
**per-company** integer (`DriverPayPolicy`); "+ Add attempt" only raises the counter, it does not create $0 rows per
driver: a level without a row shows as "no rate", distinguishable from a $0 entered on purpose. The fallback (R16): if
the requested attempt exceeds the highest level configured for the driver, the rate of that highest level is used; an
intermediate level without a row pays $0 with the note `no rate configured`.

How it is used:
- `PUT /api/v1/drivers/{publicId}/attempt-rates/{attemptNumber}` — `{ "rate": 1.50 }`
- `POST /api/v1/drivers/{publicId}/attempt-rates/{attemptNumber}/close`
- `GET /api/v1/driver-pay-policy` / `PATCH` (formula) / `POST attempt-levels`

| Field / case | Exact message | HTTP |
|---|---|---|
| `attemptNumber` outside 1..`AttemptLevels` | `Attempt {n} does not exist; the configured levels go from 1 to {N}.` | 400 |
| There is already a current rate for that attempt | `The driver already has a current rate for attempt {n}; edit that rate or close it before adding another.` | 409 |
| New effective date before the closing of a row already closed in the future | `Attempt {n} has a rate in effect until {date}; the new one must start on that date or later.` | 409 |
| `AttemptLevels` already at the limit (20) when adding a level | `The maximum of 20 attempt levels was reached.` | 409 |
| Unknown `payoutFormula` | `Unknown pay formula: 'X'.` (field `payoutFormula`) | 400 |

### 8.3 Rate per trip (special delivery)

What it does: pays a fixed amount per trip type. The "trip type" **is** the tenant's list of special services
(chapter 02, section 6): it is listed with `GET /driver-trip-types`. Deactivating a special service type that
has current trip rates in any driver responds 409 (adjustment to Batch 2).

How it is used:
- `POST /api/v1/drivers/{publicId}/trip-rates` — `{ "specialServiceTypeId": 5, "rate": 75 }`
- `PATCH /api/v1/drivers/{publicId}/trip-rates/{id}` — only `rate`/`effectiveFrom`
- `POST /api/v1/drivers/{publicId}/trip-rates/{id}/close`

| Field / case | Exact message | HTTP |
|---|---|---|
| Tenant with no active special service type | `No special services: add them in Clients & contracts before configuring trip rates.` (field `specialServiceTypeId`) | 400 |
| Missing `specialServiceTypeId` | `The trip type is required.` (field `specialServiceTypeId`) | 400 |
| Nonexistent `specialServiceTypeId` | `Special service type not found.` | 404 |
| Inactive `specialServiceTypeId` | `The special service type is inactive; reactivate it or choose another.` (field `specialServiceTypeId`) | 400 |
| There is already a current rate for that type | `The driver already has a current rate for trip type '{type}'; edit that rate or close it before adding another.` | 409 |
| `specialServiceTypeId` in the `PATCH` | `The trip type is set when the rate is created; remove the row and add one with the correct type.` | 400 |
| Deactivating a type with current trip rates | `The type has current trip rates in {n} driver(s); close them before deactivating it.` (chapter 02) | 409 |

### 8.4 Pay preview (configurable formula)

What it does: calculates, **without writing anything**, the pay of a delivery with its attempts according to the given formula (or the
company's current one if none is given): `DELIVERY_PLUS_ATTEMPTS` (the delivery, if there was one, plus each attempt including
the successful one), `DELIVERY_INCLUDES_FIRST` (the delivery plus the attempts from the 2nd on) or `FAILED_REPLACES_DELIVERY` (each
failed attempt pays its own rate; the successful one pays only the delivery). A delivery or an attempt with no rate
generates a $0 line with the note `no rate configured` (the gap stays visible, R15).

Who can: `driverpay.view`.

How it is used: `POST /api/v1/driver-pay-policy/preview`

```json
{ "attempts": [ { "number": 1, "delivered": false }, { "number": 2, "delivered": true } ], "deliveryRate": 4.00, "attemptRates": [ { "number": 1, "rate": 1.00 }, { "number": 2, "rate": 1.50 } ] }
```

| Case | Exact message | HTTP |
|---|---|---|
| No attempts | `Enter at least one attempt.` | 400 |
| More than 50 attempts | `At most 50 attempts are allowed.` | 400 |
| Attempt number outside 1..50 | `The attempt number must be between 1 and 50.` | 400 |
| Repeated attempt numbers | `There are repeated attempt numbers.` | 400 |
| More than one attempt marked as the delivery | `Only one attempt can be the delivery.` | 400 |
| The delivery is not the last attempt | `The delivery must be the last attempt.` | 400 |
| `attemptRates` with the same number repeated | `The rate for attempt {n} is repeated.` (field `attemptRates`) | 400 |
| Unknown `formula` | `Unknown pay formula: 'X'.` (field `formula`) | 400 |

---

## 9. Trips paid to the driver (DriverTrip)

What it does: records a trip paid to the driver with the **amount frozen** when it is created (the trip rate in effect
on the trip date, section 8.3); if the driver has no rate configured for that type, the trip is still born
at $0 with `rateMissing: true` (the gap stays visible, R15). Trips born from a special delivery
(section 12) also carry `orderPublicId`/`orderNumber` and **are not cancelled directly**: they are cancelled by cancelling the
order or reassigning the driver.

Who can: `driverpay.view` to list, `driverpay.manage` to create/cancel.

How it is used:
- `GET /api/v1/drivers/{publicId}/trips?from=&to=&status=&includeCancelled=false` (by default only current ones)
- `POST /api/v1/drivers/{publicId}/trips` — `{ "specialServiceTypeId": 5, "tripDate": "2026-09-25" }`
- `POST /api/v1/drivers/{publicId}/trips/{tripPublicId}/cancel` — optional body `{ "comment": "…" }`

| Field / case | Exact message | HTTP |
|---|---|---|
| Missing `specialServiceTypeId` | `The trip type is required.` (field `specialServiceTypeId`) | 400 |
| Nonexistent `specialServiceTypeId` | `Special service type not found.` | 404 |
| Inactive `specialServiceTypeId` | `The special service type is inactive; reactivate it or choose another.` (field `specialServiceTypeId`) | 400 |
| Future `tripDate` | `The trip date can’t be in the future.` (field `tripDate`) | 400 |
| `notes`/`comment` longer than 500 characters | `The trip notes allow at most 500 characters.` | 400 |
| `from` filter later than `to` | `The start date cannot be later than the end date.` (field `from`) | 400 |
| Unknown `status` filter | `Unknown trip status: 'X'.` (field `status`) | 400 |
| `tripPublicId` of another driver or another tenant | `Trip not found.` | 404 |
| Cancelling a trip born from a current special delivery | `The trip comes from a special delivery; cancel the shipment or reassign the driver.` | 409 |
| Cancelling a trip already `SETTLED`/`CANCELLED` (terminal) | `The record is already in that status.` / transition rule of the status engine (chapter 01) | 422 |

The DB guarantees at all times **a single current trip per order**: a cancelled trip is also left `isActive:
false`, so two concurrent assignments to the same order never leave two current trips.

---

## 10. Statuses and transitions

### 10.1 Vehicle (`VehicleStatus`)

| From | To | Who | What it validates | Effects | What it blocks |
|---|---|---|---|---|---|
| — | ACTIVE (initial) | System, on create | — | Birth history | — |
| ACTIVE | MAINTENANCE (lateral) | `fleet.manage`, or automatic when starting a WO (section 11.3) | — | None of its own | — |
| MAINTENANCE | ACTIVE (lateral) | `fleet.manage`, or automatic when closing/cancelling the last WO in progress | — | None of its own | — |
| ACTIVE / MAINTENANCE | INACTIVE (terminal) | `fleet.manage` | — | `VehicleStatusEffect`: `IsActive = 0` | Reactivate (409); new WOs/fuel fills (409, sections 6/7) |

### 10.2 Driver (`DriverStatus`)

| From | To | Who | What it validates | Effects | What it blocks |
|---|---|---|---|---|---|
| — | ACTIVE (initial) | System, on create | Born with no rate rows | Birth history | — |
| ACTIVE | UNAVAILABLE (lateral) | `fleet.manage` | — | None of its own (stops being available for dispatch, section 4) | — |
| UNAVAILABLE | ACTIVE (lateral) | `fleet.manage` | — | None of its own | — |
| ACTIVE / UNAVAILABLE | INACTIVE (terminal, "Delete driver") | `fleet.manage` (`DELETE`) | — | `DriverStatusEffect`: `IsActive=0`, `UserId=NULL`, devices inactive, zone removed. `DriverRatesRetirementEffect`: closes today every open rate | Reactivate (409); every rate write (409); cancelling/creating trips normally keeps working only for viewing |

### 10.3 MaintenanceWorkOrder (`WorkOrderStatus`, EntityType `WORK_ORDER`)

| From | To | Who | What it validates | Effects | What it blocks |
|---|---|---|---|---|---|
| — | OPEN (initial) | System, on create | `OT-#####` number | Birth history | — |
| OPEN | IN_PROGRESS | `fleet.maintenance` | — | `WorkOrderStatusEffect`: if the vehicle is `ACTIVE` it moves it to `MAINTENANCE` | — |
| IN_PROGRESS / OPEN | CLOSED (terminal) | `fleet.maintenance` | Active tasks complete; odometer if the program is by km/both | `WorkOrderStatusEffect`: if no other `IN_PROGRESS` WO of the vehicle remains, it returns it to `ACTIVE`; raises `CurrentOdometerKm` (monotonic maximum); if the program is by vehicle, advances `LastServiceKm`/`LastServiceDate` | `EDIT_WORK_ORDER` denied by default (422 on header and tasks) |
| any | CANCELLED (terminal) | `fleet.maintenance` | — | Same vehicle effect as CLOSED, without requiring tasks/odometer | `EDIT_WORK_ORDER` denied by default |

### 10.4 DriverTrip (`DriverTripStatus`)

| From | To | Who | What it validates | Effects | What it blocks |
|---|---|---|---|---|---|
| — | OPEN (initial) | System, on create (manual or special delivery) | Freezes the current rate (`DriverTripRules.Freeze`) | Birth history | — |
| OPEN | CANCELLED (terminal) | `driverpay.manage`, or automatic when cancelling the source order (`DriverTripOrderEffect`) | A trip linked to a current order is not cancelled directly (409) | `DriverTripStatusEffect`: `IsActive=0` (guarantees in the DB a single current trip per order) | New cancellation (422) |
| OPEN | SETTLED (terminal) | Settlement run (Batch 9, outside this batch) | — | — | New cancellation/settlement (422) |

History of any of the four entities: `GET /api/v1/status/history/{ENTITY}/{id}` with
`VEHICLE`/`DRIVER`/`WORK_ORDER`/`DRIVER_TRIP`. Capabilities per status: `GET/PUT /api/v1/status/capabilities/{ENTITY}`
(only `WORK_ORDER` carries a capability of its own in this batch, `EDIT_WORK_ORDER`).

---

## 11. Special delivery with a driver (extension of Batch 3)

What it does: completes the flow that Batch 3 left pending. Assigns (or reassigns) the driver of a special delivery already
created, or sets it when creating it (`OrderCreateRequest.driverPublicId`). The driver, their availability (section 4) and their
trip rate (section 8.3) are verified **before drawing numbers** (so a rejection leaves no gap in the order
numbering); the assignment marks the order as picked up if it is still Submitted (quotes and checks credit
as in chapter 03, section 3.2; Batch 106), advances the order **stage by stage** (no skips) up to **IN_TRANSIT** with a
history record per stage, and creates the `DriverTrip` with the frozen amount (section 9). The order record
exposes only the identity of the assigned driver (`assignedDriverPublicId/Code/Name`); **never** the trip amount.

Who can: `trips.dispatch` (the endpoint lives under the `LTL_GROUND` module, but the service also requires the
`CATALOG` module to be on, because it uses drivers and rates).

How it is used:
- When creating the order: `POST /api/v1/orders` with `"isSpecialDelivery": true`, `"specialServiceId": ...` and
  `"driverPublicId": "..."`.
- On an already created order: `POST /api/v1/orders/{publicId}/driver` — `{ "driverPublicId": "...", "overrideCredit": false, "comment": "..." }`

| Field / case | Exact message | HTTP |
|---|---|---|
| `CATALOG` module off | `The 'CATALOG' module is not enabled for this company.` (`code: module_disabled`) | 403 |
| Missing `trips.dispatch` | `Missing permission 'trips.dispatch'.` | 403 |
| `driverPublicId` of another tenant or nonexistent | `Driver not found.` | 404 |
| Driver not available for dispatch | `The driver is not available for dispatch: {blocking reasons separated by '; '}.` | 409 |
| `driverPublicId` on an order that is **not** a special delivery | `The driver is only assigned on a special delivery.` (field `driverPublicId`) | 400 |
| Credit exceeded when marking as picked up, without `orders.credit_override` | (same as chapter 03, section 3.3) `code: "credit_exceeded"` | 422 |
| Reassigning/assigning on an order that is **not** a special delivery | `Only special deliveries are assigned to a driver from here; the other shipments go through the Dispatch room.` | 422 |
| The order is already past `IN_TRANSIT` (already Delivered), or is in a lateral/terminal (in `IN_TRANSIT` it can still be reassigned) | `The special delivery already reached its destination or ended; the driver cannot be assigned or reassigned.` | 422 |
| Assigning the same driver that already has it | `The shipment is already assigned to that driver.` | 409 |
| Two concurrent assignments to the same order | `The shipment already has a current trip; reload and try again.` | 409 |

Reassigning to another driver automatically cancels the previous driver's trip (it is left `CANCELLED`/`isActive: false`)
before creating the new one. Cancelling the **order** automatically cancels its current `DriverTrip` (section 10.4).

---

## 12. Permissions and modules

| Permission | What it enables |
|---|---|
| `fleet.view` | view vehicles, drivers, zones, maintenance programs, work orders, fuel fills, expiring documents and dispatch availability |
| `fleet.manage` | create/edit vehicles, drivers, their documents/licenses/certifications/devices and dispatch zones |
| `fleet.maintenance` | create/edit maintenance programs, work orders (and their tasks) and fuel fills |
| `driverpay.view` | view driver rates, pay policy, preview and paid trips |
| `driverpay.manage` | create/edit driver rates, pay policy and paid trips |
| `trips.dispatch` | assign/reassign the driver of a special delivery (chapter 03) |
| `admin.users` | link or unlink the driver's user (in addition to `fleet.manage`) |

The whole module lives under **CATALOG**; the assignment of a driver to a special delivery lives under **LTL_GROUND**
(core) but also requires CATALOG to be on. Factory role templates: **Dispatcher** (`Dispatcher`) carries
`fleet.view`; **Billing** (`Billing`) carries `driverpay.view`; **Read-only** (`ReadOnly`) carries `fleet.view`;
**Driver** (`Driver`) carries no `fleet.*` or `driverpay.*` permission (their own access arrives with the driver
app, in a later batch). Only **Admin** carries `driverpay.manage`.

Contacts (`POST /api/v1/contacts/{ENTITY}/{id}`) and custom fields (`PUT /api/v1/custom-fields/values/{ENTITY}/{id}`,
chapter 01) are available for `VEHICLE`, `DRIVER`, `DISPATCH_ZONE`, `MAINTENANCE_SCHEDULE`, `WORK_ORDER` and
`FUEL_LOG` (under the corresponding owner permission of the table above). `DRIVER_RATE` and `FLEET_DOCUMENT` do not
accept contacts or custom fields by loose id (they always respond `404`): the three rate tables are
reached only under `/drivers/{publicId}` and "fleet document" is a virtual row that joins several tables.

---

## 13. Analysis sources and audit

The sources `VEHICLE` ("Vehicles"), `DRIVER` ("Drivers"),
`WORK_ORDER` ("Work orders"), `FUEL_LOG` ("Fuel fills") and `FLEET_DOCUMENT` ("Expiring
documents", with no `DateField`: it is a current state) remain available for views, indicators and charts. There is **no** source for rates or paid trips (`DRIVER_RATE`,
`DRIVER_TRIP`): they would expose compensation to anyone with `analytics.view`.

A new tenant (and the demo one) receives from the factory: the **"Vehicles"** view (code, plate, type, ownership,
fuel, odometer, status, active; sorted by code) and the **"Expiring documents"** view (owner, document
type, number, expiry, days left, state; filtered to `EXPIRED`/`EXPIRING`, sorted by expiry — it does not
carry a document superseded by a newer one of the same type); the **"Expiring documents"** indicator (count,
same filter, in the Pulse) and the **"Open work orders"** indicator (counts the active `OPEN`/`IN_PROGRESS`,
in the Pulse). Every create or change of a vehicle, driver, rate, paid trip or work order is recorded in `AuditLog`
(`GET /api/v1/audit/changes?entityType=VEHICLE|DRIVER|DRIVER_RATE|DRIVER_TRIP|WORK_ORDER`); the `PushToken` of the
driver's device never appears in the diff (it is sensitive data).

---

## 14. Frequently asked questions

See [faq.md](faq.md), section "Batch 4 — Fleet, drivers and maintenance", for each error message quoted in this
chapter with its cause and what to do.

## Zone levels (Island/Metro) — Batch 129

> Dictated by Luis (2026-10-19). Advance Logistics divides its operation into **Island** and **Metro**; they draw the border with their zones. Teikem models it generically: each company defines the **levels** it wants (or none).

**What a level is:** a group of dispatch zones (`ZoneGroup`): code, name, order and, optionally, a **default driver**. It is a property of the zone, not a rate.

**Where:** Fleet → **Dispatch zones**: at the top the **Zone levels** panel (create, edit, deactivate, reactivate; “Assign zone to shipments”) and in each zone the **Level** field. Read with `fleet.view`, write with `fleet.manage`, Catalogs module.

**What the level does:**
- **The order inherits the zone and the level of its delivery stop** (`DeliveryZoneId`, resolved by ZIP and town against the members of the active zones when the order is created or when its consignee changes; ambiguous or no match = no zone). They are shown in the record (Delivery zone), in the list (Zone column = zone · level; **Delivery zone** and **Zone level** filters) and in Views (“Delivery zone” and “Zone level” fields). Orders that already existed are completed with **Assign zone to shipments** (only those without one; `POST /dispatch-zones/assign-orders?all=true` recalculates them all after changing members).
- **Default driver of the level:** when creating a route for a zone that **has no driver of its own** (no primary `DriverZone`), the default driver is the one of that zone's level (Advance: all of Island to a single driver, the outside company registered as a driver). If the zone has its own driver, the zone's driver rules.
- **Driver rate per level (optional):** in Drivers → Delivery rates, **Zone level** field. A rate with a level applies only to the deliveries of that level and **wins** over the general one (no level); if the order belongs to a level without its own rate, the general one rules. A single open rate per (service, package, level). Settlement applies it per piece.
- **Contract rate per level (optional):** in the contract → Rates, **Zone level** field when adding. Same rule: the one of the order's level wins; without it, the general one. Quoting and freight billing apply it on their own (`RateQuoteRequest.zoneGroupId`).

| Method and route | Permission | What it does |
|---|---|---|
| `GET /api/v1/zone-groups?includeInactive=` · `POST` · `PATCH /{id}` · `POST /{id}/deactivate` · `/reactivate` | `fleet.view` / `fleet.manage` | Levels. 400 “Enter the level code.” · “The level code is set when it is created and cannot be changed.” · “The default driver does not exist or is inactive.” · 409 “A zone level with that code already exists.” · “The level has active zones; remove it from the zones before deactivating it.” |
| `POST/PATCH /api/v1/dispatch-zones` (`zoneGroupId`, `clearZoneGroup`) | `fleet.manage` | The zone's level. 400 “The zone level does not exist or is inactive.” |
| `POST /api/v1/dispatch-zones/assign-orders?all=` | `fleet.manage` | Delivery zone for existing orders; returns examined, with zone and without zone. |
| `GET /api/v1/orders?zoneIds=&zoneGroupIds=&noZone=` | `orders.view` | Zone, level and “no zone” filters. |
| `POST /drivers/{id}/delivery-rates` (`zoneGroup`) · `POST /contracts/{id}/rate-components` (`zoneGroup`) | `driverpay.manage` / `contracts.update` | Rate with level (code). 400 “The zone level does not exist or is inactive.”; 409 “…already has a current rate for X + Y at level Z…”. |

## Driver app: their manifests and accepting the route (Batch 203, 2026-10-23)

**What it does.** The driver sees in their app the manifests the office dispatched to them, **accepts** them before leaving and, if they are not going to carry some shipment, **declines it with a reason**. Accepting is the **departure** of the route: the accepted shipments move to **In transit** and the route to **In progress** (it is the same departure that staff record in the Dispatch room). Until they accept, they cannot record deliveries.

**Who can.** The driver's user needs the `driverapp.use` permission (“Driver app: use it”; the *Driver* role carries it) and must be **linked to a driver** (Lists → Drivers → user). Transport module (LTL_GROUND). They only see **their** manifests.

**How it is used (endpoints `/api/v1/driver-app`).**

| Action | Endpoint |
|---|---|
| Their manifests (dispatched pending acceptance and in progress) | `GET /manifests` |
| A manifest with its shipments, in route order | `GET /manifests/{publicId}` |
| Reasons for declining (editable list in Lists → “Decline reasons”) | `GET /decline-reasons` |
| Accept the manifest; the shipments in `declines` are not accepted | `POST /manifests/{publicId}/accept` with `{ "declines": [{ "shipmentPublicId", "reasonCode", "note" }], "rowVersion" }` |

**What happens to a shipment they decline.** It leaves the route (and its stop order), goes back to **Unassigned** with the comment “Declined by the driver: {reason}” and is recorded with its reason and note (`TripOrderDecline`) so the office can assign it again. If they decline **all of them**, the route does not depart: it stays dispatched and empty.

**Messages and codes.**

| Code | Message | When |
|---|---|---|
| 403 | Your user is not linked to an active driver. | The user is not a driver. |
| 404 | Manifest not found. | It does not exist or belongs to another driver. |
| 400 | Enter the reason for each shipment you decline. | The reason is missing. |
| 400 | The reason is not valid. | The code is not in the list. |
| 400 | The note cannot exceed 300 characters. | Long note. |
| 400 | A shipment appears more than once in the list of declined ones. | Repeated. |
| 400 | The shipment is not in this manifest. | Foreign shipment. |
| 422 | Manifest {code} was already accepted. | Second acceptance. |
| 422 | Manifest {code} is not ready to accept: it must be dispatched. | Not dispatched, already in progress by staff, completed or cancelled. |
| 409 | (concurrency message) | The route changed while accepting. |

**Factory decline reasons:** No room in the vehicle, The shipment is damaged, It is outside my zone, Documents are missing, Other. They are edited, deactivated or added in **Lists**.

**What comes next:** **pickup** manifests, **GPS** tracking and **notices**.

### Access to the driver app (Batch 204, 2026-10-23)

**What it does.** The driver app (`app-movil`) signs in with the **same Teikem user** as the web, with the same security rules of the company.

**Sign-in steps.**
1. **Email and password** (`POST /api/v1/auth/login`).
2. **MFA code**, if the company requires it or the person has it turned on (`POST /api/v1/auth/mfa/verify`). The first time, if they do not have their authenticator yet, the app sets it up (key → code → **recovery codes**, shown only once) and returns to sign-in.
3. **Privacy Policy and Terms of Use**: on the first sign-in and every time the document changes. “I accept” is enabled only on reaching the end of the text; “I don't accept” signs out.

**“Open with fingerprint”.** It is optional and is turned on after the first sign-in (or in Profile). **It does not replace the password or MFA**: it only opens the session already saved on the phone. The app locks on startup and after **2 minutes** in the background; with 5 failed fingerprints in a row, or with “Use my password”, the session is closed and everything is asked again. The password **is not saved** on the phone. Reason: the fingerprint is validated by the phone, not the server; if it replaced MFA, any fingerprint registered on the phone would skip the company's policy.

**Session duration.** The company sets it (System → Security: days or minutes) and the app honors it: when it expires, even with a fingerprint, it asks for password and code. A network error does not close the session; only the server rejects it.

**Several companies.** A user can belong to several. They enter their default company and change it in **Profile → Company** (before that, what is pending is sent and the other company's manifests are loaded).

**Messages.** “Invalid credentials.” (401), the server's account lockout notice, “Your user is not linked to an active driver.” (403). If the server asks to confirm the phone by email or to finish the first sign-in, the app says so (those two steps are completed outside the app for now).

### “App-only” driver, driver session and several companies (Batch 205, 2026-10-23)

**“App-only” driver.** When creating a user (System → Users → New) the type **Driver (app only)** is chosen. That user signs in **only** in the driver app: if they try on the web they get “Drivers sign in with the driver app.” (401). Afterwards they must be linked to their record in Lists → Drivers, **in each company where they work** (the same person can be a driver of several companies with a single user; when creating it they can be added to other companies at once). An internal user (staff) who also drives can use the app without being “app-only”.

**App access (`/api/v1/driver-app/auth`).** `POST login` (403 “Your user is not linked to an active driver.” if they are not an active driver in any company), `POST mfa/verify` (the MFA code; the app's challenge is accepted only by this endpoint and the web's only by the web's: 403 “Drivers sign in with the driver app.” / “This session is not from the driver app.”) and `POST company-tokens`.

**One session, all their companies.** `POST company-tokens` with the refresh token returns one access token per company where the person is a driver and a **new** refresh token (the previous one is revoked; reusing it closes the whole session due to possible theft). The session is not closed when switching from one company to another (the web's company switch does close the previous one). **Each company's MFA is always honored**: a company that requires MFA (for the whole company or for that person) only receives a token if the session passed the code; otherwise it responds `blocked: mfa_required` and the person signs in again with password and code.

**Driver session duration.** Each company can set its own in **System → Security & audit → Policy** (“Driver session in the app”, in hours; **empty** = the general session applies). The longer of the driver's and the general one applies; if the driver works in several companies, the **most permissive** of them rules. The application limit (`DriverApp:MaxSessionMinutes` in appsettings, 43200 = 30 days) caps the driver's value, not the general session. It is an **absolute** duration: renewing does not extend it; when it expires, password and code are asked again. Error 400 “The duration must be between 1 minute and 365 days (8760 hours).”.

### Deliver and mark as not delivered from the app (Batch 206, 2026-10-23)
Permission `driverapp.use`, module LTL_GROUND. Only for shipments of the driver's own **already accepted** manifests.

| Endpoint | What it does |
|---|---|
| `GET /api/v1/driver-app/shipments/{id}` | The shipment with consignee, contact phone (the order's main one), address, pieces, COD and notes. `canClose` says whether it can still be closed. |
| `POST /api/v1/driver-app/shipments/{id}/deliver` | Body `{recipientName, notes, deliveredAtUtc, latitude, longitude, signaturePngBase64, photosBase64[≤3]}`. Records the proof of delivery (origin DRIVER_APP, signature of the person who received, phone location), the order moves to **Delivered** and the delivered attempt is noted under the driver's name. |
| `POST /api/v1/driver-app/shipments/{id}/fail` | Body `{reasonCode, notes, attemptedAtUtc, photoBase64}`. The order moves to **Failed** and the failed attempt is kept with the reason (“Failure reason” list). |
| `GET /api/v1/driver-app/failure-reasons` | The reasons for “Undelivered”. |

Both actions are **idempotent**: a retry from the offline queue responds `alreadyDone: true` and only completes the missing signature or photos. Messages and HTTP codes in the FAQ (Batch 206).

### The driver app: screens (Batch 207, 2026-10-23)
- **Home**: four buttons (Pickups for today, Pickup manifest, Deliveries for today, Delivery manifest); only those with content appear. A driver of several companies sees everything together, each shipment with its company's name.
- **Manifests**: those pending acceptance go first (“To accept”). Tapping one opens **Accept manifest**: marking “Decline” on a shipment asks for the reason (that company's “Decline reasons” list) and an optional note; on confirming, the route departs (requires signal).
- **Shipment**: Details and Actions tabs. Actions → **Deliver** (who received, note, photo, signature) or **Undelivered** (reason from “Failure reason”, note, photo). With no signal it stays in the queue and goes out on its own; the shipment disappears from the list immediately.
- **Search box**: order, waybill, consignee, address, city, notes, manifest and company (name or number).

### Tracking the driver's route (Batch 208, 2026-10-23)
- **When**: only while the driver has a route **in progress** (accepted manifest with open shipments). When it ends it stops on its own.
- **What is saved**: a point every 150 m of movement (or every 90 s if moving); stopped, nothing. It is sent in batches every 2 minutes and kept for 10 days. Global values in `appsettings` (`DriverApp:Tracking`).
- **Several companies**: if the driver has routes in progress in two companies, each one saves the points.
- **Turning it off**: Profile → “Send my location during the route”. The office sees the notice “Turned off by the driver” (different from “no signal” and from “lost the permission”).
- **Endpoints**: `POST /api/v1/driver-app/tracking` (400 “Maximum 500 points per upload.” / “Maximum 50 notices per upload.” / “The notice type is not valid.”; 403 “Your user is not linked to an active driver.”), `GET /api/v1/driver-app/tracking/config`, `GET /api/v1/trips/{id}/tracking` (`trips.view`).

### Driver notices (Batch 209, 2026-10-23)
- **Inbox**: “Notices” tab of the app (bell with the unread count), from all the driver's companies. Tapping a notice marks it read.
- **Automatic**: when a route is **dispatched** the driver receives “New manifest {code}”.
- **From the office**: `POST /api/v1/driver-notices` with `{driverPublicId, title, body}` (`trips.dispatch`). 400 “Enter the notice title.” / “The title cannot exceed 120 characters.” / “The text cannot exceed 1000 characters.”; 404 if the driver does not exist.
- They are kept for 60 days. The push notification is not there yet.

**Writing a notice to the driver (web):** Catalogs → Drivers → record → “Driver notices” panel. Requires `trips.dispatch`. Title required and text optional; it goes to their notices inbox and, if Firebase is configured, to their phone.

**Seeing the track and whether the driver turned off the GPS (web):** Operations → Route monitor → choose the route. The map shows the actual track; below it, “Driver track” lists the notices (turned off, lost the permission, resumed). Points are kept for 10 days.

### Pickups: routes of type “Pickup” (Batch 211, 2026-10-23)
- **What it is**: a route with a type. When creating it in Dispatch → “New route” you choose the **Route type**: Delivery (default) or Pickup. Who: `trips.plan`; transport module.
- **Which shipments go in**: only those pending pickup. If they are not, the server responds 422 with “The shipment was already picked up (or left the pickup flow); it cannot be assigned to a pickup route.” or “The shipment has no pending pickup stop.”. An invalid type responds 400: “The route type must be DELIVERY or PICKUP.”.
- **In the driver app**: Home → “Pickups for today”. Inside the shipment: **Pick up shipment** (who handed it over, comment, photo; saves the GPS) or **Not picked up** (reason and comment). With no signal, it stays in the queue and goes out on its own.
- **Status**: departing with the pickup route **does not change** the shipments' status. Picking up: “Submitted” → “Picked up” (driver; closes the stop as completed). Not picked up: the shipment stays “Submitted” and the stop is left failed.
- **What it blocks**: delivering a shipment of a pickup route (“This shipment belongs to a pickup route: it is picked up, not delivered.”, 409/422); picking up on a delivery route (“This shipment belongs to a delivery route: it is delivered, not picked up.”); picking up a shipment that is no longer pending (“Shipment … is no longer pending pickup.”); “Picked up” status disabled in the company (“The “Picked up” status is not enabled for this company.”). The route cannot be closed with unattended shipments.

### Driver messages and tappable phone numbers (Batch 212 C, 2026-10-23)
- **“Messages” tab** of the driver app (badge with the unread count, from all their companies). It lists the conversations with the office and the groups they were added to, most recent first; tapping one opens it; “New” starts another. Requires `driverapp.use` and `messages.use` (the “Driver” role already carries it; earlier driver roles receive it with section 2026-10-23 (l) of the SQL).
- **Who appears when writing**: only the people the company lets them see (the user's “Messaging” setting; the driver is left at “Office only”: they see staff, not other drivers). See chapter 15 §8.
- **The conversation**: bubbles (theirs on the right), photos with a thumbnail (tapping the thumbnail opens it in full), card of the order being discussed (tapping it opens the shipment if it is still in their routes; if not, “That shipment is no longer in your routes.”) and the compose field with **camera** and **gallery** (up to 4 photos per message; the app shrinks them on its own).
- **No signal**: a message in a conversation that already exists stays in the phone's queue, shows “Not sent” and goes out on its own when the connection returns (without duplicating). **Starting a new conversation needs a connection** (“You need a connection to start a conversation. Try again.”).
- **Talk about this shipment**: button in the shipment detail. It opens “New conversation” with the order already attached and the **suggested** people at the top (person in charge of the route, of the zone, dispatchers, recent conversations). The first message carries the order card.
- **Push notification**: when a message arrives, tapping the notification opens that conversation. A role group (for example “Dispatchers”) is a single one per company.
- **Tappable phone numbers, emails and links**: in messages, in notices and in the consignee's phone in the shipment detail. The phone opens the dialer with the number filled in (it is normalized: 10 digits = +1); the email opens the mail app; the link opens the browser. On a tablet without calling, the system decides what to do with the link.
- **Chat photos that are gone**: “Photo deleted” / “Photo archived” (they live 30 days by default). The evidence of a delivery does not live in the chat.
