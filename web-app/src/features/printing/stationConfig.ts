// Lote F-G1 — configuración de impresión de ESTA estación (este navegador en esta computadora), por compañía, en localStorage
// (`teikem.print.station.v1.{tenantId}`). El servidor no la recibe (D-FG2). Lo guardado dañado o de otra versión = valores por
// defecto, sin error. Orden de resolución de la impresora y la plantilla: estación → predeterminada de la compañía → integrado
// («PDF del navegador» 4×6 o Carta y el label integrado). Si lo elegido en la estación ya no está activo, se usa la de la compañía
// con un aviso.
import { BUILT_IN_LAYOUT, normalizeLayout, type LabelLayout } from '../../kernel/ui/orderLabelLayout'
import type { LabelTemplateDto, PrinterOption, StationOptions } from './printingApi'

/** Valor de «Impresora»: '' = la predeterminada de la compañía; `PDF_ONLY` = sin impresora (PDF del navegador); si no, el PublicId. */
export const COMPANY_DEFAULT = ''
export const PDF_ONLY = 'pdf'
/** Valor de «Template de label»: '' = la predeterminada de la compañía; `BUILT_IN` = el integrado; si no, el PublicId. */
export const BUILT_IN = 'builtin'

export interface StationLabelConfig {
  printer: string
  template: string
  /** Nombre exacto de la impresora en esta computadora (QZ Tray); '' = el de la impresora de la compañía. */
  systemPrinter: string
  /** Imprimir el label al guardar una orden (si la compañía tiene encendido «Imprimir labels directo»). */
  printOnSave: boolean
  /** Ver la vista previa antes de imprimir. */
  confirm: boolean
}

export interface StationDocConfig {
  printer: string
  systemPrinter: string
  confirm: boolean
}

export interface StationConfig {
  label: StationLabelConfig
  doc: StationDocConfig
}

export const DEFAULT_STATION: StationConfig = {
  label: { printer: COMPANY_DEFAULT, template: COMPANY_DEFAULT, systemPrinter: '', printOnSave: true, confirm: false },
  doc: { printer: COMPANY_DEFAULT, systemPrinter: '', confirm: true },
}

export const stationKey = (tenantId: number | string | null | undefined) => `teikem.print.station.v1.${tenantId ?? 'none'}`

const str = (v: unknown, def: string) => (typeof v === 'string' ? v : def)
const bool = (v: unknown, def: boolean) => (typeof v === 'boolean' ? v : def)

/** Lee lo guardado (valor crudo de localStorage); cualquier cosa rara = valores por defecto. */
export function parseStationConfig(raw: string | null | undefined): StationConfig {
  if (!raw) return structuredClone(DEFAULT_STATION)
  try {
    const v = JSON.parse(raw) as { label?: Record<string, unknown>; doc?: Record<string, unknown> }
    const l = v && typeof v.label === 'object' && v.label ? v.label : {}
    const d = v && typeof v.doc === 'object' && v.doc ? v.doc : {}
    const def = DEFAULT_STATION
    return {
      label: {
        printer: str(l.printer, def.label.printer),
        template: str(l.template, def.label.template),
        systemPrinter: str(l.systemPrinter, def.label.systemPrinter),
        printOnSave: bool(l.printOnSave, def.label.printOnSave),
        confirm: bool(l.confirm, def.label.confirm),
      },
      doc: { printer: str(d.printer, def.doc.printer), systemPrinter: str(d.systemPrinter, def.doc.systemPrinter), confirm: bool(d.confirm, def.doc.confirm) },
    }
  } catch {
    return structuredClone(DEFAULT_STATION)
  }
}

export function readStationConfig(tenantId: number | string | null | undefined): StationConfig {
  try {
    return parseStationConfig(localStorage.getItem(stationKey(tenantId)))
  } catch {
    return structuredClone(DEFAULT_STATION)
  }
}

/** Guarda; false si el navegador no lo permite (modo privado lleno, bloqueado). */
export function writeStationConfig(tenantId: number | string | null | undefined, config: StationConfig): boolean {
  try {
    localStorage.setItem(stationKey(tenantId), JSON.stringify(config))
    return true
  } catch {
    return false
  }
}

export type StationSource = 'station' | 'company' | 'builtIn'
export type StationNotice = 'printerInactive' | 'templateInactive'

export interface ResolvedPrinter {
  /** null = sin impresora: PDF del navegador del tamaño integrado. */
  printer: PrinterOption | null
  source: StationSource
  notice: StationNotice | null
  connection: string
  /** Nombre en esta computadora para QZ Tray (null sin impresora). */
  systemName: string | null
  mediaSize: string
  rotate: boolean
}

/** Impresora a usar para un uso (LABEL o DOCUMENT): estación → compañía → integrado. */
export function resolvePrinter(kind: 'label' | 'doc', config: StationConfig, options: StationOptions | undefined): ResolvedPrinter {
  const purpose = kind === 'label' ? 'LABEL' : 'DOCUMENT'
  const own = kind === 'label' ? config.label : config.doc
  const list = (options?.printers ?? []).filter((p) => p.purpose === purpose)
  const companyDefault = list.find((p) => p.isDefault) ?? null
  let printer: PrinterOption | null = null
  let source: StationSource = 'builtIn'
  let notice: StationNotice | null = null
  if (own.printer === PDF_ONLY) {
    printer = null
  } else if (own.printer !== COMPANY_DEFAULT) {
    printer = list.find((p) => p.publicId === own.printer) ?? null
    if (printer) source = 'station'
    else {
      notice = 'printerInactive'
      printer = companyDefault
      source = printer ? 'company' : 'builtIn'
    }
  } else {
    printer = companyDefault
    source = printer ? 'company' : 'builtIn'
  }
  const defaultMedia = kind === 'label' ? '4X6' : 'LETTER'
  return {
    printer,
    source,
    notice,
    connection: printer?.connectionType ?? 'BROWSER',
    systemName: printer ? own.systemPrinter.trim() || printer.systemName?.trim() || printer.name || null : null,
    mediaSize: printer?.mediaSize ?? defaultMedia,
    rotate: printer?.rotateMedia ?? false,
  }
}

export interface ResolvedTemplate {
  template: LabelTemplateDto | null
  layout: LabelLayout
  source: StationSource
  notice: StationNotice | null
}

/** Plantilla de label a usar: estación → predeterminada de la compañía → integrado. */
export function resolveTemplate(config: StationConfig, options: StationOptions | undefined): ResolvedTemplate {
  const list = options?.templates ?? []
  const companyDefault = list.find((t) => t.isDefault) ?? null
  const builtIn = options?.builtInLayout ? normalizeLayout(options.builtInLayout) : BUILT_IN_LAYOUT
  const pick = (t: LabelTemplateDto | null, source: StationSource, notice: StationNotice | null = null): ResolvedTemplate =>
    t ? { template: t, layout: normalizeLayout(t.layout), source, notice } : { template: null, layout: builtIn, source: 'builtIn', notice }
  const own = config.label.template
  if (own === BUILT_IN) return pick(null, 'builtIn')
  if (own !== COMPANY_DEFAULT) {
    const t = list.find((x) => x.publicId === own) ?? null
    return t ? pick(t, 'station') : pick(companyDefault, 'company', 'templateInactive')
  }
  return pick(companyDefault, 'company')
}

/** Medidas (pulgadas) de un tamaño de impresora. */
export function mediaInches(mediaSize: string): { widthIn: number; heightIn: number } {
  switch (mediaSize) {
    case '4X4':
      return { widthIn: 4, heightIn: 4 }
    case '4X2':
      return { widthIn: 4, heightIn: 2 }
    case 'LETTER':
      return { widthIn: 8.5, heightIn: 11 }
    default:
      return { widthIn: 4, heightIn: 6 }
  }
}
