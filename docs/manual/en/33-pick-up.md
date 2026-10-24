# 33 — “Pick up” service type (collect and bring)

Batch 114 (2026-10-18, dictated by Luis). A service type can be **pick up**: it is collected and brought to the warehouse. It is different from the pickup-and-delivery ones and is treated differently.

**Who configures it:** `admin.catalogs` (System → Lists → *Service type* → **Adjust**). **Who scans:** `trips.scan`, module **LTL_GROUND**.

## 1. The two marks of the service type
In **Lists → Service type → Adjust** a value has two switches (per company):
| Mark | What it does |
|---|---|
| **Pick up (collect and bring)** | The shipment ends in **In warehouse** (final status) and **does not go out on a route or to a driver**. |
| **Billable** | On = it is quoted and charged. **Off = it is neither quoted nor charged**: the shipment is submitted without requiring a contract or rate, it stays quoted at zero and does not count against the client's credit. |

The company receives the **Pick up** type seeded (code `PICK_UP`, pick up and billable). The marks are stored in the value's `ExtraJson`: `{"pickUp":true,"billable":true}`; without marks, a service type is not pick up and is billable.

## 2. The shipment's path
**Submitted → Pickup → In warehouse** (final). At the scan station:
- **Pick up** takes it to Pickup (as always).
- **Arrived** takes it to **In warehouse** (instead of “Unassigned”); if it was Submitted, Pickup is recorded as “skipped”. The **following pieces** with “Arrived” are **counted** (“2 of 3 pieces”) even if the shipment is already In warehouse.
- **Route** and **Depart** respond “No”: *Shipment {n} is a pick up: it stays in the warehouse. To send it out, create another shipment.* (If it is to be sent out, it is another shipment or the client comes to get it.)
- **Close count** of a shipment In warehouse lists the pieces that never arrived and, on confirming, they become “To verify” (Claims, chapter 31).

## 3. Billing
A **billable** pick up is charged on reaching **In warehouse**, whatever the contract's charging model (per pickup, per delivery or mixed). A **non-billable** one (quoted at zero) is never invoiced.

## 4. Messages
| Message | HTTP | When |
|---|---|---|
| Shipment {n} is a pick up: it stays in the warehouse. To send it out, create another shipment. | 200 (result “No”) | Scanning a pick up shipment with Route or Depart. |
| There is no rate in force for PICK_UP/… ; configure the rate in the client's contract. | 422 | Submitting a **billable** pick up without a rate in the contract (turn off “Billable” if it is not charged). |

## 5. Frequent cases
- *I want a pick up that is not charged:* in Lists → Service type → Pick up → Adjust, turn off **Billable**.
- *I want my own pick up service type:* create a value in the company's service type list and turn on **Pick up**.
- *A pick up shipment stayed in Pickup:* scan **Arrived** to take it to In warehouse.

## 6. Round trip (2026-10-22)
A third switch of the service type, **Round trip**, marks the shipments that are **collected at the store and brought back** (in Colavora, the “Roundtrip” type, RT). It changes which address the driver goes to:

- **The zone** of the shipment comes from the postal code (or town) of the **pickup**, not the delivery. That way the zone route groups what the driver actually travels.
- **The route stop and the map** are those of the pickup: address, pin and estimated time are the store's. The delivery (the client's warehouse, always the same) does not appear as a stop.
- **In Dispatch you see what is visited and of which type it is:** each stop and each “Unassigned” shipment carries a **Pick up** (round trip) or **Deliver** label, and the **service type** (e.g. ROUNDTRIP). Hovering over **Pick up** explains that it is a round-trip shipment.
- **Where it is turned on:** Lists → Service type → Adjust (or Edit) → **Round trip**. The Colavora migration turns it on by itself for the RT type.
- **If the shipment changes** consignee, pickup or service type, the zone is recalculated with the rule above. “Recalculate zones” (Lists → Dispatch zones) also respects it.
- **What is already loaded** is fixed with `import-colavora --solo reparar,paradas`: it sets the pickup's zone on round-trip shipments and moves the stops of the current routes from the delivery to the pickup. The routes that already had those shipments keep their grouping (the route of the delivery zone); to regroup them by the pickup's zone, take them out of the route in Draft and “Plan the day” again.

Frequent cases: *“All the shipments of a route seem to go to the same client”:* they were round trips shown by their delivery; now the store where they are collected is shown. *“A round-trip shipment does not appear in the zone I expect”:* its zone is the pickup's; check the postal code of the pickup stop.
