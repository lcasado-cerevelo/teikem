# 13 — Scheduled jobs

Batch 50 (P2.2, 2026-10-14). The **scheduled jobs** are the company's automatic tasks: they run by themselves at their time, without anyone
triggering them. Each company has its own (what one does never touches the others). The system runs them with its own service
inside the API (no external programs).

## 1. Who and where

- Permission **`jobs.manage`** (“Manage scheduled jobs”; it comes with the **Company admin** role and was added to the
  administrator roles that already existed). Module **System** (always on).
- Screen **System → Scheduled jobs** (`/system/jobs`). Without the permission, the entry does not appear in the menu.
- API: `GET /api/v1/jobs`, `GET /api/v1/jobs/{clave}`, `PATCH /api/v1/jobs/{clave}`, `POST /api/v1/jobs/{clave}/run`,
  `GET /api/v1/jobs/{clave}/runs`. There is no DELETE: the jobs are defined by the system.

## 2. The factory jobs

| Key | Job | Initial schedule | On |
|---|---|---|---|
| `JOB_RUN_PURGE` | **Clean up the job history**: keeps the last 100 runs of each job and deletes the older ones (history only, never business data). | Every day 3:00 a.m. | Yes |
| `INVENTORY_SWEEP` | **Daily inventory review**: compares the whole movement ledger against the balances and opens discrepancies if something does not add up (the full sweep of Batch 14). If the inventory module is off it does nothing. | Every day 2:00 a.m. | Yes |
| `JOB_TEST` | **Test job**: does nothing; it is used to test “Run now” and the history. | Every hour | No |
| `BILLING_OVERDUE_SCAN` | **Mark overdue invoices** (Batch 75): moves to “Overdue” the issued invoices whose due date has passed and that still have a balance. | Every hour | Yes |
| `KPI_SNAPSHOT` | **Daily snapshot of indicators** (Batch 90): saves the day's figures (shipments per stage and COD money) for the Pulse history and the charts; “Run now” replaces today's. | Daily 23:55 | Yes |
| `REPORT_DELIVERY` | **Send scheduled reports** (Batch 92): e-mails the views that have a schedule and recipients when their time comes (company time), run as their owner. | Every 5 minutes | Yes |
| `SALES_BACKORDER_SCAN` | **Notify backorders that can now be fulfilled** (Batch 66): reviews the sales orders with backorder and publishes the notice “Backorder that can now be fulfilled” when there is available inventory. It only notifies; it does not fulfill by itself. If the Sales orders module is off it does nothing. | Every 10 minutes | Yes |
| `FLEET_ALERT_SCAN` | **Fleet alerts** (Batch 168): reviews vehicle and driver documents and preventive maintenance and publishes “Fleet documents about to expire”, “Fleet documents expired”, “Maintenance due soon” and “Maintenance overdue” ([chapter 14 §3.2 bis](14-avisos.md)). One notice per event with the complete set; it does not repeat the same set in the same week. If the catalog module is off it does nothing. | Every day 6:15 a.m. | Yes |
| `INVENTORY_ALERT_SCAN` | **Inventory alerts** (Batch 141): reviews the inventory and publishes the notices “Lots about to expire”, “Expired lots with stock”, “Products below minimum”, “Products with no stock” and “Products with no movement” ([chapter 14 §3.2](14-avisos.md)). One notice per event with the complete set; it does not repeat the same set in the same week. If the inventory module is off it does nothing. | Every day 6:00 a.m. | Yes |
| `EXIT_NOTICE_BACKORDER_SCAN` | **Reserve the backorder of exit notices** (Batch 143): reserves the pending quantity of exit notices when goods come in (by any means), oldest notice first, and publishes the event “Backorder now reserved” ([chapter 40](40-avisos-de-salida.md)). If the inventory module is off it does nothing. | Every 10 minutes | Yes |

Daily times are **Puerto Rico time** (the company's). New jobs from future versions appear in the list by themselves, with
their factory schedule; **what you change (switch or schedule) is never overwritten**.

## 3. The screen

![Scheduled jobs](frontend/img/l50-lista.png)

Per job: **On** (switch), **Schedule**, **Next run**, **Last result** (OK, Failed, Running or Never ran, with its
time and a summary or the error) and three actions: **Run now**, **Change schedule** and **View history**.

- **Run now**: requests the run; the system picks it up **within seconds** (even if the job is off) and does not change its schedule.
  While it is requested or running, the button is not offered; the screen refreshes by itself.
- **Switch**: when **turning on**, the next run is calculated from that moment; when **turning off**, it is cleared (it does not run on schedule, but
  “Run now” is still available).
- **Change schedule**: *Every so often* (from 1 minute to 7 days; the interval counts from when the previous run **ends**) or
  *Every day at a time* (24 hours). On save, the next run is recalculated if the job is on.
- **View history**: the latest runs (start, end, origin *Scheduled* or *Run now*, result and detail).

## 4. What happens if something fails or goes down

- **A failure stops nothing**: it stays with the error in view and is **retried soon**: at 1, 2 and 5 minutes (up to 3 failures in a row); afterwards
  it goes back to its normal schedule. A good run clears the failure count.
- **It never runs twice**: if several copies of the system are running, only one picks up each run. If the one that was running goes down halfway, its
  lock expires (the job's maximum time) and another copy picks it up.
- If the system was off at the job's time, it runs when it comes back on.

## 5. Validations and messages

| Situation | Exact message | HTTP |
|---|---|---|
| The key does not exist (or belongs to an old version) | `Job not found.` | 404 |
| Empty interval, under 60 s or over 7 days | `The interval must be between 60 seconds and 7 days (604800).` | 400 (field `intervalSeconds`) |
| Daily schedule without a time | `Enter the daily time (HH:mm).` | 400 (field `dailyAt`) |
| Invalid daily time (e.g. 25:00) | `The daily time is not valid; use HH:mm (24 hours).` | 400 (field `dailyAt`) |
| Unknown schedule type | `Unknown schedule type: 'X'. Use INTERVAL or DAILY.` | 400 (field `scheduleKind`) |
| “Run now” while the job is running | `The job is already running; wait for it to finish.` | 409 |
| The job changed since you opened it (rowVersion) | `The record was modified by another user; reload and try again.` | 409 |
| Without the permission | “No permission” | 403 |

## 6. For support

- Tables `dbo.ScheduledJob` (one row per company and job: switch, schedule, next run, last result and the lock
  `LockedBy`/`LockedUntilUtc`) and `dbo.ScheduledJobRun` (history). Section **2026-10-14 (f)** of `Diseño/logistica-db-update.sql`.
- Configuration (`appsettings`): `Jobs:Enabled` (default `true`; `false` turns the service off) and `Jobs:PollSeconds` (default 30; minimum 5).
  The console commands (`db-init`, `db-update`, `import-legacy`) do not start the service.
- Each run executes **as the company, without a user** (“System”), so the log shows it that way. Switch and schedule changes
  do go to the log with the user (type “Scheduled job”).
- A row with the lock set and the system down is released by itself when the job's maximum time expires (2 min for the test, 5 min for the
  cleanup, 30 min for the inventory review).
