// Lote 14 (P8, D2-D4) — "Conteo de lo cambiado": crea un conteo Pendiente por cada posición del almacén con movimientos en la
// ventana (sin los ajustes de un conteo), con todo lo que tiene y, marcado por defecto, las claves que quedaron en 0 (para
// confirmar que la posición está vacía); tope 200 posiciones. Almacén (preelegido si la compañía tiene uno solo o si la lista
// está filtrada por uno), Desde/Hasta en hora de la compañía (Puerto Rico; por defecto la ventana de la vista previa: desde la
// última generación en ese almacén o, la primera vez, desde el inicio del día; al tocarlas se mandan las dos), Zonas
// (opcional) e "Incluir posiciones vacías". Vista previa en vivo (`GET /cycle-counts/changes-preview`, 300 ms): ventana,
// movimientos, conteos y líneas que se crearían, posiciones saltadas y el `problem` (el 400 que daría crear, tal cual; con él
// no se puede crear). "Crear N conteos" → `POST /cycle-counts/from-changes`; `onCreated` recibe los conteos creados.
import { useId, useMemo, useState } from 'react'
import { useLang, useT } from '../../kernel/i18n'
import { Modal, SearchMultiSelect, toast } from '../../kernel/ui'
import { useChangesPreview, useCreateCountsFromChanges, useWarehouseZones, useWarehouses, warehouseLabel, type CycleCountDto } from './api'
import { ReadOnlyField } from './BinModal'
import { tenantTimeZone, utcFromZonedInput, zonedInputFromUtc } from '../../kernel/api/tenantZone'
import { formatDateTime } from '../../kernel/format'
import { useDebounced } from './lineRules'
import { WarehousePicker } from './pickers'
import { problemText } from './problemText'

/** Fecha y hora de un instante del API con los formatos y la zona de la compañía. */
function formatZoned(iso: string | null | undefined, lang: string): string {
  return formatDateTime(iso, lang)
}

export interface ChangedCountModalProps {
  onClose: () => void
  /** Almacén con que se abre (p. ej. el único elegido en los filtros de la lista). */
  initialWarehousePublicId?: string | null
  onCreated?: (counts: CycleCountDto[]) => void
}

export function ChangedCountModal({ onClose, initialWarehousePublicId, onCreated }: ChangedCountModalProps) {
  const t = useT()
  const lang = useLang()
  const ids = useId()
  const create = useCreateCountsFromChanges()
  const { data: warehouses = [] } = useWarehouses({ includeInactive: false }, { handleAccessDenied: false })
  const [picked, setPicked] = useState<string | null | undefined>(initialWarehousePublicId ?? undefined)
  // sin elección: el único almacén activo de la compañía
  const onlyWarehouse = warehouses.length === 1 ? warehouses[0] : null
  const warehousePublicId = picked === undefined ? (onlyWarehouse?.publicId ?? null) : picked
  const [touched, setTouched] = useState(false)
  const [fromText, setFromText] = useState('')
  const [toText, setToText] = useState('')
  const [zoneIds, setZoneIds] = useState<string[]>([])
  const [includeEmpty, setIncludeEmpty] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const zones = useWarehouseZones(warehousePublicId, {}, { handleAccessDenied: false })
  const zoneOptions = useMemo(
    () => (zones.data ?? []).filter((z) => z.isActive !== false).map((z) => ({ value: String(z.id), label: [z.code, z.name].filter(Boolean).join(' · ') })),
    [zones.data],
  )

  const query = useMemo(
    () => ({
      warehousePublicId: warehousePublicId ?? undefined,
      fromUtc: touched ? (utcFromZonedInput(fromText) ?? undefined) : undefined,
      toUtc: touched ? (utcFromZonedInput(toText) ?? undefined) : undefined,
      zoneIds: zoneIds.length > 0 ? zoneIds.map(Number) : undefined,
      includeEmpty,
    }),
    [warehousePublicId, touched, fromText, toText, zoneIds, includeEmpty],
  )
  const debounced = useDebounced(query, 300)
  const preview = useChangesPreview(debounced, { enabled: Boolean(debounced.warehousePublicId) })
  const data = preview.data
  const stale = preview.isFetching || debounced !== query

  // sin tocarlas, las fechas muestran la ventana que calculó el servidor (y no se mandan: el alta usa la suya, hasta "ahora")
  const shownFrom = touched ? fromText : zonedInputFromUtc(data?.fromUtc)
  const shownTo = touched ? toText : zonedInputFromUtc(data?.toUtc)
  const touch = (which: 'from' | 'to', value: string) => {
    if (!touched) {
      setFromText(shownFrom)
      setToText(shownTo)
      setTouched(true)
    }
    if (which === 'from') setFromText(value)
    else setToText(value)
  }

  const positions = data?.positions ?? 0
  const canCreate = Boolean(warehousePublicId) && Boolean(data) && !data?.problem && positions > 0 && !preview.error && !stale && !busy

  const submit = async () => {
    if (!canCreate) return
    setError(null)
    setBusy(true)
    try {
      const res = await create.mutateAsync({
        warehousePublicId,
        fromUtc: query.fromUtc ?? null,
        toUtc: query.toUtc ?? null,
        zoneIds: query.zoneIds ?? null,
        includeEmpty,
      })
      const counts = res.counts ?? []
      toast.success(t('warehouse.cycleCounts.changes.created', { n: counts.length }))
      onCreated?.(counts)
      onClose()
    } catch (err) {
      setError(problemText(err))
    } finally {
      setBusy(false)
    }
  }

  const whId = `${ids}-wh`
  const fromId = `${ids}-from`
  const toId = `${ids}-to`
  const zonesId = `${ids}-zones`
  const helpId = `${ids}-help`

  return (
    <Modal
      open
      title={t('warehouse.cycleCounts.changes.title')}
      onClose={onClose}
      dismissible={!busy}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose} disabled={busy}>
            {t('common.cancel')}
          </button>
          <button type="button" className="btn flow" disabled={!canCreate} onClick={() => void submit()}>
            {busy ? t('common.loading') : positions > 0 && !data?.problem ? t('warehouse.cycleCounts.changes.create', { n: positions }) : t('warehouse.cycleCounts.changes.createNone')}
          </button>
        </>
      }
    >
      <p className="note cc-qty-where">{t('warehouse.cycleCounts.changes.intro')}</p>
      {onlyWarehouse ? (
        // un solo almacén activo: no hay nada que elegir (y el foco va a las fechas, sin abrir un desplegable)
        <ReadOnlyField label={t('warehouse.cycleCounts.changes.warehouse')} value={warehouseLabel(onlyWarehouse)} />
      ) : (
        <div className="f">
          <label htmlFor={whId}>
            {t('warehouse.cycleCounts.changes.warehouse')} <span className="req" aria-hidden="true">*</span>
          </label>
          <WarehousePicker
            id={whId}
            value={warehousePublicId}
            required
            onChange={(v) => {
              setPicked(v)
              setZoneIds([])
              setTouched(false)
            }}
          />
          {!warehousePublicId && <p className="help">{t('warehouse.cycleCounts.changes.warehouseRequired')}</p>}
        </div>
      )}
      <div className="r2">
        <div className="f">
          <label htmlFor={fromId}>{t('warehouse.cycleCounts.changes.from')}</label>
          <input id={fromId} type="datetime-local" value={shownFrom} aria-describedby={helpId} disabled={!warehousePublicId} onChange={(e) => touch('from', e.target.value)} />
        </div>
        <div className="f">
          <label htmlFor={toId}>{t('warehouse.cycleCounts.changes.to')}</label>
          <input id={toId} type="datetime-local" value={shownTo} aria-describedby={helpId} disabled={!warehousePublicId} onChange={(e) => touch('to', e.target.value)} />
        </div>
      </div>
      <p className="help cc-help" id={helpId}>
        {t('warehouse.cycleCounts.changes.timeHelp', { zone: tenantTimeZone() })}
        {touched && (
          <>
            {' '}
            <button type="button" className="linkbtn" onClick={() => setTouched(false)}>
              {t('warehouse.cycleCounts.changes.resetWindow')}
            </button>
          </>
        )}
      </p>
      <div className="f">
        <label id={`${zonesId}-l`} htmlFor={zonesId}>
          {t('warehouse.cycleCounts.changes.zones')}
        </label>
        <SearchMultiSelect
          id={zonesId}
          labelledBy={`${zonesId}-l`}
          options={zoneOptions}
          value={zoneIds}
          onChange={setZoneIds}
          placeholder={t('warehouse.cycleCounts.fields.allZones')}
          disabled={!warehousePublicId}
        />
      </div>
      <div className="f">
        <label className="sw cc-sw">
          <input type="checkbox" role="switch" checked={includeEmpty} onChange={(e) => setIncludeEmpty(e.target.checked)} />
          <span className="tk" aria-hidden="true" />
          <span>{t('warehouse.cycleCounts.changes.includeEmpty')}</span>
        </label>
        <p className="help">{t('warehouse.cycleCounts.changes.includeEmptyHelp')}</p>
      </div>

      <section className="cc-preview" aria-live="polite" aria-busy={stale || undefined} aria-label={t('warehouse.cycleCounts.changes.previewTitle')}>
        <h3>{t('warehouse.cycleCounts.changes.previewTitle')}</h3>
        {!warehousePublicId ? (
          <p className="help">{t('warehouse.cycleCounts.changes.warehouseRequired')}</p>
        ) : preview.error ? (
          <p className="ferr" role="alert">
            {problemText(preview.error)}
          </p>
        ) : !data ? (
          <p className="help">{t('warehouse.cycleCounts.changes.loading')}</p>
        ) : (
          <>
            <p className="cc-preview-window">
              {t('warehouse.cycleCounts.changes.window', { from: formatZoned(data.fromUtc, lang), to: formatZoned(data.toUtc, lang) })}
            </p>
            <p className="help">
              {data.lastChangesToUtc
                ? t('warehouse.cycleCounts.changes.last', { date: formatZoned(data.lastChangesToUtc, lang) })
                : t('warehouse.cycleCounts.changes.firstTime')}
            </p>
            <ul className="cc-preview-list">
              <li>{t('warehouse.cycleCounts.changes.movements', { n: data.movements ?? 0 })}</li>
              {!data.problem && <li className="cc-preview-main">{t('warehouse.cycleCounts.changes.positions', { n: positions, lines: data.lines ?? 0 })}</li>}
              {(data.positionsWithOpenCount ?? 0) > 0 && <li>{t('warehouse.cycleCounts.changes.withOpen', { n: data.positionsWithOpenCount ?? 0 })}</li>}
              {(data.positionsInactive ?? 0) > 0 && <li>{t('warehouse.cycleCounts.changes.inactive', { n: data.positionsInactive ?? 0 })}</li>}
              {(data.positionsEmpty ?? 0) > 0 && <li>{t('warehouse.cycleCounts.changes.empty', { n: data.positionsEmpty ?? 0 })}</li>}
            </ul>
            {data.problem && (
              <p className="note cc-problem" role="alert">
                {data.problem}
              </p>
            )}
            <p className="help">{t('warehouse.cycleCounts.changes.max', { max: data.maxPositions ?? 200 })}</p>
          </>
        )}
      </section>
      {error && (
        <p className="ferr" role="alert">
          {error}
        </p>
      )}
    </Modal>
  )
}
