// Lote F-G1 — configuración de la estación (localStorage por compañía) y resolución estación → compañía → integrado.
import { afterEach, describe, expect, it } from 'vitest'
import { BUILT_IN_INVOICE_LAYOUT, BUILT_IN_LAYOUT } from '../../kernel/ui/orderLabelLayout'
import type { StationOptions } from './printingApi'
import {
  BUILT_IN,
  COMPANY_DEFAULT,
  DEFAULT_STATION,
  mediaInches,
  parseStationConfig,
  PDF_ONLY,
  readStationConfig,
  resolvePrinter,
  resolveInvoiceTemplate,
  resolveTemplate,
  stationKey,
  writeStationConfig,
  type StationConfig,
} from './stationConfig'

const OPTIONS: StationOptions = {
  printers: [
    { publicId: 'zd', name: 'Zebra ZD420 — Mostrador 1', purpose: 'LABEL', connectionType: 'QZ_TRAY', mediaSize: '4X6', rotateMedia: false, isDefault: true, systemName: 'ZDesigner ZD420-203dpi' },
    { publicId: 'zt', name: 'Zebra ZT230 — Almacén', purpose: 'LABEL', connectionType: 'BROWSER', mediaSize: '4X4', rotateMedia: true, isDefault: false, systemName: null },
    { publicId: 'hp', name: 'HP LaserJet', purpose: 'DOCUMENT', connectionType: 'BROWSER', mediaSize: 'LETTER', rotateMedia: false, isDefault: false, systemName: null },
  ],
  templates: [
    { publicId: 'cod', name: '4×6 — COD', isDefault: true, isActive: true, layout: { ...BUILT_IN_LAYOUT, sender: { on: true, pos: 'tc', size: 'sm', asBarcode: false } } },
    { publicId: 'std', name: '4×6 — Estándar', isDefault: false, isActive: true, layout: BUILT_IN_LAYOUT },
  ],
  builtInLayout: BUILT_IN_LAYOUT,
  printLabelsDirect: false,
  qzSigningConfigured: false,
}

const withLabel = (patch: Partial<StationConfig['label']>): StationConfig => ({ ...DEFAULT_STATION, label: { ...DEFAULT_STATION.label, ...patch } })

afterEach(() => localStorage.clear())

describe('guardar y leer', () => {
  it('por compañía; dañado u otra forma = valores por defecto', () => {
    expect(stationKey(7)).toBe('teikem.print.station.v1.7')
    expect(readStationConfig(7)).toEqual(DEFAULT_STATION)
    const cfg = withLabel({ printer: 'zt', template: 'std', confirm: true })
    expect(writeStationConfig(7, cfg)).toBe(true)
    expect(readStationConfig(7)).toEqual(cfg)
    expect(readStationConfig(8)).toEqual(DEFAULT_STATION) // otra compañía no ve la configuración
    expect(parseStationConfig('{no')).toEqual(DEFAULT_STATION)
    expect(parseStationConfig('[]')).toEqual(DEFAULT_STATION)
    expect(parseStationConfig(JSON.stringify({ label: { printer: 5, confirm: 'si' } }))).toEqual(DEFAULT_STATION)
  })
})

describe('resolvePrinter', () => {
  it('sin elegir: la predeterminada de la compañía (QZ Tray por su nombre en el sistema)', () => {
    const r = resolvePrinter('label', DEFAULT_STATION, OPTIONS)
    expect(r).toMatchObject({ source: 'company', connection: 'QZ_TRAY', systemName: 'ZDesigner ZD420-203dpi', mediaSize: '4X6', rotate: false, notice: null })
  })

  it('la de la estación manda; el nombre de esta computadora gana al de la impresora', () => {
    expect(resolvePrinter('label', withLabel({ printer: 'zt' }), OPTIONS)).toMatchObject({ source: 'station', connection: 'BROWSER', mediaSize: '4X4', rotate: true })
    expect(resolvePrinter('label', withLabel({ systemPrinter: 'ZD420 (Copiar 1)' }), OPTIONS).systemName).toBe('ZD420 (Copiar 1)')
  })

  it('elegida pero ya inactiva: la de la compañía con aviso; PDF del navegador = sin impresora', () => {
    expect(resolvePrinter('label', withLabel({ printer: 'vieja' }), OPTIONS)).toMatchObject({ source: 'company', notice: 'printerInactive' })
    expect(resolvePrinter('label', withLabel({ printer: PDF_ONLY }), OPTIONS)).toMatchObject({ printer: null, source: 'builtIn', connection: 'BROWSER', mediaSize: '4X6' })
  })

  it('documentos sin predeterminada: el integrado en carta', () => {
    expect(resolvePrinter('doc', DEFAULT_STATION, OPTIONS)).toMatchObject({ printer: null, source: 'builtIn', mediaSize: 'LETTER' })
    expect(resolvePrinter('doc', { ...DEFAULT_STATION, doc: { ...DEFAULT_STATION.doc, printer: 'hp' } }, OPTIONS).printer?.name).toBe('HP LaserJet')
    expect(mediaInches('LETTER')).toEqual({ widthIn: 8.5, heightIn: 11 })
    expect(mediaInches('4X2')).toEqual({ widthIn: 4, heightIn: 2 })
  })
})

describe('resolveTemplate', () => {
  it('estación → compañía → integrado', () => {
    expect(resolveTemplate(DEFAULT_STATION, OPTIONS)).toMatchObject({ source: 'company', template: { publicId: 'cod' } })
    expect(resolveTemplate(DEFAULT_STATION, OPTIONS).layout.sender.on).toBe(true)
    expect(resolveTemplate(withLabel({ template: 'std' }), OPTIONS)).toMatchObject({ source: 'station', template: { publicId: 'std' } })
    expect(resolveTemplate(withLabel({ template: BUILT_IN }), OPTIONS)).toMatchObject({ source: 'builtIn', template: null })
    expect(resolveTemplate(withLabel({ template: 'borrada' }), OPTIONS)).toMatchObject({ source: 'company', notice: 'templateInactive' })
    expect(resolveTemplate(withLabel({ template: COMPANY_DEFAULT }), { ...OPTIONS, templates: [] })).toMatchObject({ source: 'builtIn', layout: BUILT_IN_LAYOUT })
  })
})

describe('label de factura', () => {
  const INVOICE_TPL = { publicId: 'inv', name: 'Factura 4×6', kind: 'INVOICE', isDefault: true, isActive: true, layout: BUILT_IN_INVOICE_LAYOUT }
  it('la plantilla de piezas no toma la predeterminada de facturas (y al revés); sin plantilla de factura sale el integrado', () => {
    const options: StationOptions = { ...OPTIONS, templates: [...(OPTIONS.templates ?? []), INVOICE_TPL] }
    expect(resolveTemplate(DEFAULT_STATION, options)).toMatchObject({ template: { publicId: 'cod' } })
    expect(resolveInvoiceTemplate(options)).toMatchObject({ source: 'company', template: { publicId: 'inv' } })
    expect(resolveInvoiceTemplate(OPTIONS)).toMatchObject({ source: 'builtIn', template: null, layout: BUILT_IN_INVOICE_LAYOUT })
    expect(resolveInvoiceTemplate(undefined).layout).toEqual(BUILT_IN_INVOICE_LAYOUT)
  })
})
