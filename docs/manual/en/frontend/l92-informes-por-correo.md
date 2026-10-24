# L92 — Send a view by email (screen)

Where: **Analytics → Views & reports → New view / Edit view**, **“Send by email”** block (between “Visibility” and “Preview”). Requires `analytics.manage`. How the sending works (job, validations, result) is in [chapter 01, §8.2.1](../01-plataforma-y-seguridad.md).

## What you see
1. **“Email this report automatically”** checkbox. Off, the block only shows an explanation. When turned on, a daily schedule at 07:00 is set up with the period “Last 7 days”.
2. **Frequency:** Every day / Once a week (**Day of the week** appears) / Once a month (**Day of the month** appears, 1 to 28) and **Time (company time)**.
3. **Period of the report sent:** Last 7 days, Last 30 days, This month, All history.
4. **Recipients:** text box with emails separated by comma or semicolon. Below, a live notice: how many recipients there are, or the first misspelled email, or that they are missing (maximum 10).
5. In an **already saved** view: a line with the **last sending** (Sent / Skipped / Failed, date and message) and the **“Send now”** button. With unsaved changes in the block the button turns off and says “Save your changes to send the new ones.”.
6. A schedule the screen does not know how to build (set through the API) is kept and a notice is shown; unchecking the box removes it.

Server errors (invalid schedule, emails or period) appear below the block with the exact message. When you uncheck the box and save, the schedule, the recipients and the period are deleted.

## Files
`ReportScheduleParts.tsx` (block), `reportSchedule.ts` (cron ⇄ form, emails), `reportsApi.ts` (`useDeliverReport`), changes in `ViewBuilderModal.tsx` and `viewRules.ts`; tests `ReportScheduleParts.test.tsx` and `reportSchedule.test.ts`. Texts in `analytics.reports.schedule.*` (es/en).

No screenshots in this batch: the actual sending depends on the email provider, which is not available in development.
