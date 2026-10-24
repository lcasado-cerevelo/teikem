# Product labels (Products & inventory) — 2026-10-09

The **product label** is an adhesive label for a label printer (Zebra or another thermal printer) with the **SKU barcode**, the **SKU in large print** and the product **name** (and its category). The owner's request: the same as the bin labels ([F16](f16-etiquetas-de-posicion.md)) but for products, in the same sizes 4 × 2, 4 × 4 and 4 × 6, **printing only the label**.

**Where.** Warehouse → **Products & inventory** (Products tab), **Product labels** button in the header, next to **Barcodes**.

**Who can.** Module **WMS_LOTSERIAL** and permission **`inventory.view`** (the one for the screen and for its barcode report). Without the permission the button is not shown.

**Server.** Unchanged: the usual product list is read (`GET /api/v1/products`, 200 at a time, with no row limit) and the PDF is built in the browser.

## What it prints
EXACTLY the products the table filters (warehouse, product, name, category, brand and KPI), in natural SKU order (SKU-2 before SKU-10). **One label = one PDF page of the exact label size** (no letter sheet, no margins to trim). Maximum **500 labels per PDF**.

| Part of the label | What it carries |
|---|---|
| Barcode (Code 128) | The **exact SKU** (the scanner app looks the product up by barcode or by SKU; the product's own barcode is not used, same as in the barcode report) |
| Large text | The SKU |
| Small text (up to 2 lines) | Product name · category |

## How to use it
1. Filter the Products table as you like (or leave it complete).
2. **Product labels** → choose the **size** (4 × 2 landscape, 4 × 4 square, 4 × 6 portrait, with its equivalent in cm) and the **orientation** (Automatic or Rotate 90° if the printer prints the label sideways).
3. **Generate PDF**: it downloads the file `etiquetas-de-producto-4x2-<compañía>-<día>.pdf`. The size and orientation are remembered in this browser.
4. In the printer driver use the same label size and print at 100%.

Labels **have no state**: printing them marks nothing and they can be reprinted whenever you want.

## Messages
| Situation | Message |
|---|---|
| More than 500 products in the filter | Just a warning: `That is {n}: the PDF will be large and may take a moment. All are printed; there is no limit.` (all are printed) |
| The filter returns no products | `There are no products to print with the current filters.` |
| SKU with characters Code 128 does not accept, or too long | The label is printed with the SKU in text only and, at the end: `{n} came out without a barcode (code in text only):` + the list |
| Success | `{n} labels of 4 × 2 inches were generated.` |
| Server error while reading | `The labels could not be generated. {server message}` |

## Frequently asked questions
**How is it different from “Barcodes”?** “Barcodes” is a **list on letter sheets** with many codes per sheet (to print and scan the paper during the count). The **product label** is an adhesive label per product, the size of the roll.

**How do I print only some products?** Filter the table (by name, category, brand…) until only those remain; what is filtered is printed. (There are no selection checkboxes in the products table.)

**Can I print several copies of the same label?** No: one comes out per product. Print the PDF several times from the viewer.
