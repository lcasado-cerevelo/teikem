// Lote F-G1 — motor del label de envío (puro, sin DOM): la construcción de la maqueta (9 elementos, 9 zonas, 3 tamaños), su
// normalización (lo que llegue raro cae al label integrado) y el PLAN de un label: tres renglones (arriba, medio, abajo), cada uno
// con sus tres celdas de texto (izquierda, centro, derecha) y, debajo, sus códigos de barras A TODO EL ANCHO (una columna mide unas
// 1.27 in en una etiqueta de 4 in y un número de paquete no cabe legible ahí). La vista previa HTML (`LabelPreview`) y el PDF
// (`orderLabelPdf.ts`) pintan el MISMO plan, así lo que se ve es lo que sale.
// El código de barras del label es el NÚMERO DEL PAQUETE (un label por paquete, con su propio número; corrección de Luis del
// 2026-10-12): debajo de las barras va el número legible y «Paquete i de N». Un paquete sin número no lleva label (se avisa).
// El label es siempre blanco y negro: el COD va en caja negra con letra blanca y solo si es mayor que 0.
import { isCode128Encodable } from './code128'

export const LABEL_KEYS = ['logo', 'service', 'sender', 'consignee', 'address', 'barcode', 'waybill', 'invoice', 'cod'] as const
export type LabelKey = (typeof LABEL_KEYS)[number]
export const LABEL_POSITIONS = ['tl', 'tc', 'tr', 'ml', 'mc', 'mr', 'bl', 'bc', 'br'] as const
export type LabelPos = (typeof LABEL_POSITIONS)[number]
export const LABEL_SIZES = ['sm', 'md', 'lg'] as const
export type LabelSize = (typeof LABEL_SIZES)[number]
/** Elementos que admiten «Como código de barras» (el «Código de barras» siempre lo es). */
export const BARCODE_CAPABLE: readonly LabelKey[] = ['waybill', 'invoice']

export interface LabelElement {
  on: boolean
  pos: LabelPos
  size: LabelSize
  asBarcode: boolean
}
export type LabelLayout = Record<LabelKey, LabelElement>

/** Label integrado (LABEL_FIELDS de la maqueta): el que aplica si no hay plantilla. Igual a `LabelLayout.BuiltIn` del servidor. */
export const BUILT_IN_LAYOUT: LabelLayout = {
  logo: { on: true, pos: 'tl', size: 'md', asBarcode: false },
  service: { on: true, pos: 'tr', size: 'sm', asBarcode: false },
  sender: { on: false, pos: 'tc', size: 'sm', asBarcode: false },
  consignee: { on: true, pos: 'ml', size: 'md', asBarcode: false },
  address: { on: true, pos: 'ml', size: 'sm', asBarcode: false },
  barcode: { on: true, pos: 'mc', size: 'md', asBarcode: false },
  waybill: { on: false, pos: 'bl', size: 'sm', asBarcode: false },
  invoice: { on: true, pos: 'bl', size: 'sm', asBarcode: false },
  cod: { on: true, pos: 'br', size: 'md', asBarcode: false },
}

const isPos = (v: unknown): v is LabelPos => typeof v === 'string' && (LABEL_POSITIONS as readonly string[]).includes(v.toLowerCase())
const isSize = (v: unknown): v is LabelSize => typeof v === 'string' && (LABEL_SIZES as readonly string[]).includes(v.toLowerCase())

/**
 * Construcción segura a partir de lo que mande el API o lo que haya en memoria: cada elemento que falte o traiga una posición o un
 * tamaño desconocidos toma lo del integrado; «como código de barras» solo en waybill y factura. Nunca lanza.
 */
export function normalizeLayout(raw: unknown): LabelLayout {
  const src = raw && typeof raw === 'object' ? (raw as Record<string, unknown>) : {}
  const out = {} as LabelLayout
  for (const key of LABEL_KEYS) {
    const def = BUILT_IN_LAYOUT[key]
    const e = src[key] && typeof src[key] === 'object' ? (src[key] as Record<string, unknown>) : null
    if (!e) {
      out[key] = { ...def }
      continue
    }
    out[key] = {
      on: typeof e.on === 'boolean' ? e.on : def.on,
      pos: isPos(e.pos) ? (e.pos.toLowerCase() as LabelPos) : def.pos,
      size: isSize(e.size) ? (e.size.toLowerCase() as LabelSize) : def.size,
      asBarcode: BARCODE_CAPABLE.includes(key) && e.asBarcode === true,
    }
  }
  return out
}

/** ¿Hay algún código de barras visible? (el servidor exige uno para guardar la plantilla). */
export function hasVisibleBarcode(l: LabelLayout): boolean {
  return l.barcode.on || (l.waybill.on && l.waybill.asBarcode) || (l.invoice.on && l.invoice.asBarcode)
}

/** Iguales elemento por elemento (para saber si el constructor tiene cambios sin guardar). */
export function sameLayout(a: LabelLayout, b: LabelLayout): boolean {
  return LABEL_KEYS.every((k) => a[k].on === b[k].on && a[k].pos === b[k].pos && a[k].size === b[k].size && a[k].asBarcode === b[k].asBarcode)
}

/** Lo que va en el label de UN paquete (lo arma quien imprime: órdenes en F-G2, la prueba aquí). */
export interface OrderLabelData {
  /** Logo en texto (D-FG11): el nombre de la compañía. */
  company: string
  service?: string | null
  sender?: string | null
  consignee?: string | null
  addressLines?: readonly string[]
  /** Número del paquete: el valor del código de barras. */
  packageNumber?: string | null
  /** Paquete i de N (base 1). */
  packageIndex: number
  packageCount: number
  waybill?: string | null
  invoice?: string | null
  /** COD de la orden; solo se imprime si es mayor que 0. */
  cod?: number | null
  /** COD ya formateado con la moneda de la compañía ("$125.82"). */
  codText?: string | null
}

export type LabelRow = 't' | 'm' | 'b'
export type LabelCol = 'l' | 'c' | 'r'

/** Texto de una celda. `inverse` = caja negra con letra blanca (COD). */
export interface LabelTextItem {
  key: LabelKey
  size: LabelSize
  lines: string[]
  bold: boolean
  inverse: boolean
}

/** Código de barras a todo el ancho de su renglón; `caption` = texto legible debajo. `encodable` = false → solo texto. */
export interface LabelBarcodeItem {
  key: LabelKey
  size: LabelSize
  value: string
  caption: string
  encodable: boolean
}

export interface LabelRowPlan {
  row: LabelRow
  cells: Record<LabelCol, LabelTextItem[]>
  barcodes: LabelBarcodeItem[]
}

/** Textos fijos del label (traducidos por quien llama). */
export interface LabelTexts {
  from: string
  to: string
  waybill: string
  invoice: string
  cod: string
  /** «Paquete {i} de {n}» con {i} y {n}. */
  packageOf: string
}

const ROWS: readonly LabelRow[] = ['t', 'm', 'b']

const fill = (template: string, params: Record<string, string | number>) => template.replace(/\{(\w+)\}/g, (_, k: string) => String(params[k] ?? ''))

/**
 * Plan del label de un paquete: por renglón, los textos de cada celda en el orden del constructor y los códigos de barras. Un elemento
 * sin dato (sin factura, COD 0…) no se pinta. El «Código de barras» lleva el número del paquete con «Paquete i de N».
 */
export function planLabel(layout: LabelLayout, data: OrderLabelData, texts: LabelTexts): LabelRowPlan[] {
  const rows = ROWS.map((row): LabelRowPlan => ({ row, cells: { l: [], c: [], r: [] }, barcodes: [] }))
  const at = (pos: LabelPos) => rows[ROWS.indexOf(pos[0] as LabelRow)]
  const col = (pos: LabelPos) => pos[1] as LabelCol
  const clean = (v: string | null | undefined) => (v ?? '').trim()
  const packageOf = fill(texts.packageOf, { i: data.packageIndex, n: data.packageCount })

  for (const key of LABEL_KEYS) {
    const el = layout[key]
    if (!el.on) continue
    const row = at(el.pos)
    const text = (lines: string[], bold = false, inverse = false) => {
      const kept = lines.map((l) => l.trim()).filter(Boolean)
      if (kept.length > 0) row.cells[col(el.pos)].push({ key, size: el.size, lines: kept, bold, inverse })
    }
    const barcode = (value: string, caption: string) => row.barcodes.push({ key, size: el.size, value, caption, encodable: isCode128Encodable(value) })
    switch (key) {
      case 'logo':
        text([clean(data.company)], true)
        break
      case 'service':
        text([clean(data.service).toUpperCase()], true)
        break
      case 'sender':
        if (clean(data.sender)) text([`${texts.from} ${clean(data.sender)}`])
        break
      case 'consignee':
        if (clean(data.consignee)) text([`${texts.to} ${clean(data.consignee)}`], true)
        break
      case 'address':
        text([...(data.addressLines ?? [])])
        break
      case 'barcode': {
        const n = clean(data.packageNumber)
        if (n) barcode(n, `${n} · ${packageOf}`)
        break
      }
      case 'waybill':
      case 'invoice': {
        const v = clean(key === 'waybill' ? data.waybill : data.invoice)
        if (!v) break
        if (el.asBarcode) barcode(v, `${key === 'waybill' ? texts.waybill : texts.invoice} ${v}`)
        else text([`${key === 'waybill' ? texts.waybill : texts.invoice} ${v}`])
        break
      }
      case 'cod':
        if (data.cod != null && data.cod > 0) text([`${texts.cod} ${clean(data.codText) || String(data.cod)}`], true, true)
        break
    }
  }
  return rows
}

/** Paquetes que llevan label (con número) y cuántos se omiten por no tener número de paquete. */
export function printableLabels<T extends Pick<OrderLabelData, 'packageNumber'>>(labels: readonly T[]): { printable: T[]; withoutNumber: number } {
  const printable = labels.filter((l) => (l.packageNumber ?? '').trim() !== '')
  return { printable, withoutNumber: labels.length - printable.length }
}

/** Datos de ejemplo de «Prueba» (los de la maqueta). */
export function sampleLabelData(company: string, codText: string): OrderLabelData {
  return {
    company: company || 'TEIKEM',
    service: 'NEXT DAY',
    sender: 'Advance Solutions',
    consignee: 'Farmacia Las Marías',
    addressLines: ['Carr. 2 km 14.5', 'Toa Baja, PR 00949'],
    packageNumber: 'PQT-00012',
    packageIndex: 1,
    packageCount: 3,
    waybill: 'WB-88213',
    invoice: 'AX-48217',
    cod: 125.82,
    codText,
  }
}
