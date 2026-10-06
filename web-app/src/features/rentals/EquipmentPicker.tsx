// Lote F17 (Rentas F-R1) — selector de equipos por serie para el alta de una renta y para "Agregar equipos" en la ficha.
// Equipo (ProductPicker: propios, con disponible en el almacén de origen) → sus series DISPONIBLES en ese almacén y en
// posiciones que se rentan (no cuarentena, cruce de muelle ni En renta; mismo criterio que el servidor), con buscador:
// Enter con la serie exacta la elige (lector de código de barras). No repite series (ni las ya elegidas ni las de la renta).
// Tarifa opcional para lo que se agrega (fija o por día/semana/mes, monto y moneda). Es un bloque de controles sueltos (no un
// <form>): vive al lado del <Form> del alta sin anidarse. Los 409 del servidor (serie en otra renta, no disponible) los
// muestra quien llama, con el mensaje exacto.
import { useId, useMemo, useState, type KeyboardEvent } from 'react'
import { useTenantSettings } from '../../kernel/catalogs'
import { useT } from '../../kernel/i18n'
import { Chip } from '../../kernel/ui'
import { useInventoryBalances, useProductSerials, type ProductListItemDto, type SerialDto } from '../warehouse/api'
import { isPickableZone } from '../warehouse/collectForm'
import { ProductPicker } from '../warehouse/pickers'
import { EMPTY_RATE, rateInput, rateIssue, scannedSerialIssue, serialKey, type PickedEquipment, type RateDraft, type RentalIssue } from './rentalRules'
import { RateFields } from './RateFields'

/** Series que se pintan como casillas (el resto se alcanza con el buscador). */
const MAX_SHOWN = 100

export interface EquipmentPickerProps {
  /** Almacén de origen (publicId); sin él no se puede elegir. */
  warehousePublicId: string | null | undefined
  /** Código del almacén (para los mensajes). */
  warehouseCode?: string
  /** Series que ya están en la renta o en la lista (clave `serialKey`): no se ofrecen. */
  excluded: ReadonlySet<string>
  /** Agrega los equipos elegidos (con la tarifa común, si se puso). Si lanza, el selector conserva lo elegido. */
  onAdd: (items: PickedEquipment[]) => Promise<void> | void
  /** Mientras guarda (en la ficha, el POST de los equipos). */
  busy?: boolean
}

export function EquipmentPicker({ warehousePublicId, warehouseCode = '', excluded, onAdd, busy }: EquipmentPickerProps) {
  const t = useT()
  const uid = useId()
  const [product, setProduct] = useState<ProductListItemDto | null>(null)
  const [selected, setSelected] = useState<string[]>([])
  const [text, setText] = useState('')
  const [scanIssue, setScanIssue] = useState<RentalIssue | null>(null)
  const [rate, setRate] = useState<RateDraft>(EMPTY_RATE)
  const [rateError, setRateError] = useState<{ field: 'frequency' | 'amount'; message: string } | null>(null)
  const { data: settings } = useTenantSettings()

  const productPublicId = product?.publicId ?? null
  const isSerial = (product?.trackingTypeCode ?? '').toUpperCase() === 'SERIAL'
  const isOwn = product ? product.isOwn !== false : true
  const enabled = Boolean(productPublicId && warehousePublicId && isSerial && isOwn)
  const serialsQuery = useProductSerials(productPublicId, { status: 'AVAILABLE' }, { enabled, handleAccessDenied: false })
  const balancesQuery = useInventoryBalances(
    { productPublicIds: productPublicId ? [productPublicId] : undefined, warehousePublicIds: warehousePublicId ? [warehousePublicId] : undefined, includeZero: false, take: 200 },
    { enabled, handleAccessDenied: false },
  )

  // posiciones del almacén que se rentan (no cuarentena, cruce de muelle ni En renta)
  const pickableBins = useMemo(() => {
    const set = new Set<number>()
    for (const b of balancesQuery.data?.items ?? []) if (b.binId != null && isPickableZone(b.zoneTypeCode)) set.add(b.binId)
    return set
  }, [balancesQuery.data])

  const { available, hidden } = useMemo(() => {
    const out: SerialDto[] = []
    let skipped = 0
    for (const s of serialsQuery.data ?? []) {
      if (s.warehousePublicId !== warehousePublicId || s.binId == null) continue
      if (excluded.has(serialKey(s.serialNumber))) continue
      if (!pickableBins.has(s.binId)) {
        skipped++
        continue
      }
      out.push(s)
    }
    out.sort((a, b) => (a.serialNumber ?? '').localeCompare(b.serialNumber ?? '', 'en', { numeric: true }))
    return { available: out, hidden: skipped }
  }, [serialsQuery.data, warehousePublicId, excluded, pickableBins])

  const byKey = useMemo(() => new Map(available.map((s) => [serialKey(s.serialNumber), s])), [available])
  const shown = useMemo(() => {
    const q = text.trim().toUpperCase()
    const list = q ? available.filter((s) => (s.serialNumber ?? '').toUpperCase().includes(q) || (s.binCode ?? '').toUpperCase().includes(q)) : available
    return list
  }, [available, text])
  const selectedSet = useMemo(() => new Set(selected), [selected])

  const toggle = (key: string) => setSelected((cur) => (cur.includes(key) ? cur.filter((k) => k !== key) : [...cur, key]))

  function onScanKey(e: KeyboardEvent<HTMLInputElement>) {
    if (e.key !== 'Enter') return
    e.preventDefault()
    const value = text.trim()
    if (!value) return
    const key = serialKey(value)
    if (byKey.has(key) && !selectedSet.has(key)) {
      setSelected((cur) => [...cur, key])
      setText('')
      setScanIssue(null)
      return
    }
    const picked = new Set([...excluded, ...selected])
    setScanIssue(scannedSerialIssue(value, { picked, available: byKey, warehouseCode }))
  }

  function pickProduct(_publicId: string | null, row: ProductListItemDto | null) {
    setProduct(row)
    setSelected([])
    setText('')
    setScanIssue(null)
  }

  async function add() {
    const problem = rateIssue(rate)
    if (problem) {
      setRateError({ field: problem.field, message: t(`rentals.errors.${problem.issue.code}`) })
      return
    }
    setRateError(null)
    if (!product || selected.length === 0) return
    const r = rateInput(rate)
    const items: PickedEquipment[] = selected
      .map((k) => byKey.get(k))
      .filter((s): s is SerialDto => Boolean(s))
      .map((s) => ({
        productPublicId: product.publicId ?? '',
        sku: product.sku ?? '',
        productName: product.name ?? '',
        serialNumber: s.serialNumber ?? '',
        binCode: s.binCode ?? '',
        rate: r,
      }))
    await onAdd(items)
    setSelected([])
    setText('')
  }

  const productIssue = product && !isOwn
    ? t('rentals.errors.notOwn', { sku: product.sku ?? '' })
    : product && !isSerial
      ? t('rentals.errors.notSerial', { sku: product.sku ?? '' })
      : null

  return (
    <div className="ren-picker" role="group" aria-labelledby={`${uid}-title`}>
      <h3 id={`${uid}-title`} className="ren-h3">
        {t('rentals.picker.title')}
      </h3>
      {!warehousePublicId ? (
        <p className="note">{t('rentals.picker.pickWarehouseFirst')}</p>
      ) : (
        <>
          <div className="f">
            <label htmlFor={`${uid}-product`}>{t('rentals.picker.product')}</label>
            <ProductPicker
              id={`${uid}-product`}
              value={productPublicId}
              onChange={pickProduct}
              ownOnly
              warehousePublicId={warehousePublicId}
              onlyAvailable
              placeholder={t('rentals.picker.productPlaceholder')}
            />
          </div>
          {productIssue && (
            <p className="ferr" role="alert">
              {productIssue} {product && isOwn && !isSerial ? t('rentals.picker.convertHint') : null}
            </p>
          )}
          {enabled && (
            <>
              <div className="f">
                <label htmlFor={`${uid}-scan`}>{t('rentals.picker.serialSearch')}</label>
                <input
                  id={`${uid}-scan`}
                  type="search"
                  autoComplete="off"
                  value={text}
                  maxLength={80}
                  aria-describedby={`${uid}-scan-help`}
                  onChange={(e) => {
                    setText(e.target.value)
                    setScanIssue(null)
                  }}
                  onKeyDown={onScanKey}
                />
                <p className="help" id={`${uid}-scan-help`}>
                  {t('rentals.picker.serialSearchHint')}
                </p>
                {scanIssue && (
                  <p className="ferr" role="alert">
                    {t(`rentals.errors.${scanIssue.code}`, scanIssue.params)}
                  </p>
                )}
              </div>
              {serialsQuery.isPending || balancesQuery.isPending ? (
                <p className="note">{t('common.loading')}</p>
              ) : available.length === 0 ? (
                <p className="note">{t('rentals.picker.none', { warehouse: warehouseCode })}</p>
              ) : (
                <fieldset className="ren-serials">
                  <legend>{t('rentals.picker.available', { warehouse: warehouseCode, count: available.length })}</legend>
                  <div className="ren-serials-bar">
                    <button type="button" className="btn sm" onClick={() => setSelected((cur) => [...new Set([...cur, ...shown.slice(0, MAX_SHOWN).map((s) => serialKey(s.serialNumber))])])}>
                      {t('rentals.picker.selectAll')}
                    </button>
                    {selected.length > 0 && (
                      <button type="button" className="btn sm" onClick={() => setSelected([])}>
                        {t('rentals.picker.clear')}
                      </button>
                    )}
                    <span className="ren-count" aria-live="polite">
                      {t('rentals.picker.selected', { count: selected.length })}
                    </span>
                  </div>
                  <ul className="ren-serial-list">
                    {shown.slice(0, MAX_SHOWN).map((s) => {
                      const key = serialKey(s.serialNumber)
                      return (
                        <li key={s.id ?? key}>
                          <label>
                            <input type="checkbox" checked={selectedSet.has(key)} onChange={() => toggle(key)} />
                            <span className="mono">{s.serialNumber}</span>
                            <Chip>{s.binCode}</Chip>
                          </label>
                        </li>
                      )
                    })}
                  </ul>
                  {shown.length > MAX_SHOWN && <p className="help">{t('rentals.picker.more', { count: shown.length - MAX_SHOWN })}</p>}
                  {hidden > 0 && <p className="help">{t('rentals.picker.hidden', { count: hidden })}</p>}
                </fieldset>
              )}
              <fieldset className="ren-rate">
                <legend>{t('rentals.picker.rate')}</legend>
                <RateFields value={rate} onChange={(r) => { setRate(r); setRateError(null) }} error={rateError} defaultCurrency={settings?.currencyCode ?? ''} />
              </fieldset>
              <div className="ren-actions">
                <button type="button" className="btn flow" disabled={selected.length === 0 || busy} onClick={() => void add().catch(() => undefined)}>
                  {busy ? t('common.loading') : t('rentals.picker.add', { count: selected.length })}
                </button>
              </div>
            </>
          )}
        </>
      )}
    </div>
  )
}
