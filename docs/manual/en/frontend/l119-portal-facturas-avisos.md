# L119 — Client portal: proof of delivery, invoices, my notices and consignees (client app)

Batch 119 screens in the **client app** (web and phone). Screenshots taken by the `web-app/e2e/lote119-portal.spec.ts` walkthrough against the real API (desktop and mobile at 360 px with no horizontal scroll). Rules and API: [chapter 37](../37-portal-facturas-avisos-comprobante.md).

## Proof of delivery in the order file
Who received it and when, and that the delivery was recorded by the company's staff (the consignee did not sign). Without the user's name or internal notes.

![Proof of delivery](img/l119-comprobante.png)

## My invoices
Home has the **My invoices** button. A read-only list, with a status filter.

![Invoices](img/l119-facturas.png)

The file of an invoice: detail, totals, balance and payments received. There is no payment action.

![Invoice](img/l119-factura.png)

## My notices
The company sends: what it did not turn on is shown as “unavailable”; the person turns off what they do not want.

![My notices](img/l119-mis-avisos.png)
![Turned off](img/l119-mis-avisos-apagado.png)

## My consignees
The list of the client's consignees, with their notices and their access.

![Consignees](img/l119-mis-consignatarios.png)
