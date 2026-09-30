// Lote 11 — "Asignar cupo": cupo máximo en bloque de las posiciones de un almacén (`warehouse.manage`,
// `POST /api/v1/warehouses/{publicId}/bins/capacity`). Lo abren Posiciones (`/warehouse/locations`, junto a "Nueva
// posición", sobre el almacén elegido y con la zona de `?zone=`) y la pestaña Posiciones de la ficha del almacén (con su
// filtro de Zona y sus textos de Pasillo/Rack/Nivel/Posición). Quien lo muestra lo envuelve en `<Can perm="warehouse.manage">`.
// - Alcance: Zona (zonas activas, con buscador), Pasillo, Rack, Nivel, Posición ("contiene"), "Solo posiciones sin cupo" y
//   "Todo el almacén" (solo visible sin filtros: el API exige un filtro o `allBins: true`). Siempre solo posiciones activas.
// - Acción: exactamente una, "Cupo máximo" (entero > 0) o "Quitar cupo" (radio). Al quitar, "Solo sin cupo" no aplica.
// - Vista previa en vivo (300 ms de pausa, `useBinCapacityPreview`): "Se aplicará a N posiciones"; con 0 el botón se
//   deshabilita. Más de 100 posiciones o "Todo el almacén" → `ConfirmDialog` antes de aplicar.
// - Al aplicar: toast "Cupo aplicado a {changed} de {matched} posiciones" (las que ya tenían ese valor no cuentan), la
//   mutación invalida posiciones, zonas y almacenes, y el modal se cierra. Los errores del API se quedan en el modal.
// Lógica pura en `binCapacity.ts`.
import { useId, useMemo, useRef, useState } from 'react'
import { applyProblemDetails, type AppliedProblem } from '../../kernel/api/problem'
import { useLang, useT } from '../../kernel/i18n'
import { ConfirmDialog, Modal, SearchSelect, toast } from '../../kernel/ui'
import { useBinCapacityPreview, useSetBinsCapacity, useWarehouseZones, type WarehouseZoneDto } from './api'
import {
  EMPTY_BIN_CAPACITY_SCOPE,
  capacityRequestBody,
  hasScopeFilter,
  needsCapacityConfirmation,
  parseCapacityQty,
  sanitizeZoneIds,
  scopeReady,
  sendsAllBins,
  type BinCapacityAction,
  type BinCapacityInitial,
  type BinCapacityScope,
} from './binCapacity'
import { TextFilter } from './filterControls'
import { formatNumber, useDebounced } from './lineRules'
import './warehouse.css'

const NO_ZONES: WarehouseZoneDto[] = []

export interface BinCapacityModalProps {
  /** publicId del almacén. */
  publicId: string
  open: boolean
  onClose: () => void
  /** Alcance ya elegido en la pantalla que lo abre (se lee al abrir; ids de zona de otro almacén se descartan). */
  initial?: BinCapacityInitial
}

/** Casilla con el estilo `.sw` del kit (interruptor), con estado deshabilitado y ayuda opcional. */
function SwitchField({ label, checked, onChange, disabled, help }: { label: string; checked: boolean; onChange: (v: boolean) => void; disabled?: boolean; help?: string }) {
  const helpId = useId()
  return (
    <div className="bcap-switch">
      <label className="sw">
        <input
          type="checkbox"
          role="switch"
          checked={checked}
          disabled={disabled}
          aria-describedby={help ? helpId : undefined}
          onChange={(e) => onChange(e.target.checked)}
        />
        <span className="tk" aria-hidden="true" />
        <span>{label}</span>
      </label>
      {help && (
        <p id={helpId} className="help">
          {help}
        </p>
      )}
    </div>
  )
}

export function BinCapacityModal(props: BinCapacityModalProps) {
  // montado solo mientras está abierto: cada apertura empieza con el alcance que manda la pantalla
  if (!props.open) return null
  return <BinCapacityDialog {...props} />
}

function BinCapacityDialog({ publicId, onClose, initial }: BinCapacityModalProps) {
  const t = useT()
  const lang = useLang()
  const fmt = (n: number) => formatNumber(n, lang)
  const qtyId = useId()
  const qtyRef = useRef<HTMLInputElement>(null)
  const mutation = useSetBinsCapacity()

  const [scope, setScope] = useState<BinCapacityScope>(() => ({ ...EMPTY_BIN_CAPACITY_SCOPE, ...initial, zoneIds: [...(initial?.zoneIds ?? [])] }))
  const [mode, setMode] = useState<'set' | 'clear'>('set')
  const [qtyText, setQtyText] = useState('')
  const [qtyTouched, setQtyTouched] = useState(false)
  const [confirming, setConfirming] = useState(false)
  const [busy, setBusy] = useState(false)
  const [problem, setProblem] = useState<AppliedProblem | null>(null)

  const zonesQ = useWarehouseZones(publicId, { includeInactive: false })
  const zones = useMemo(() => (zonesQ.data ?? NO_ZONES).filter((z) => z.isActive !== false), [zonesQ.data])
  const zoneOptions = useMemo(() => zones.map((z) => ({ value: String(z.id), label: [z.code, z.name].filter(Boolean).join(' · ') })), [zones])

  // Alcance efectivo: zonas que existen en este almacén; "Solo sin cupo" no aplica al quitar; allBins solo sin filtros.
  const effective = useMemo<BinCapacityScope>(() => {
    const zoneIds = zonesQ.isLoading ? scope.zoneIds : sanitizeZoneIds(scope.zoneIds, zones)
    const s = { ...scope, zoneIds, onlyWithoutCapacity: mode === 'set' && scope.onlyWithoutCapacity }
    return { ...s, allBins: sendsAllBins(s) }
  }, [scope, zones, zonesQ.isLoading, mode])
  const filtered = hasScopeFilter(effective)
  const ready = scopeReady(effective)

  // vista previa con pausa: el objeto `effective` cambia solo cuando cambia el alcance
  const debounced = useDebounced(effective, 300)
  const preview = useBinCapacityPreview(publicId, debounced, zones, zonesQ.isLoading)
  const pending = ready && (debounced !== effective || preview.loading)

  const qty = parseCapacityQty(qtyText)
  const qtyCode = 'code' in qty ? qty.code : null
  const qtyError = mode === 'set' && qtyTouched && qtyCode ? t(`warehouse.binCapacity.errors.${qtyCode}`) : null
  const serverQtyError = problem?.errors.maxCapacityQty?.join(' ')
  const otherServerErrors = problem
    ? [...new Set(Object.entries(problem.errors).filter(([k]) => k !== 'maxCapacityQty').flatMap(([, v]) => v))].filter((m) => m !== problem.title)
    : []

  const set = <K extends keyof BinCapacityScope>(key: K) => (value: BinCapacityScope[K]) => setScope((prev) => ({ ...prev, [key]: value }))

  const action: BinCapacityAction | null = mode === 'clear' ? { kind: 'clear' } : qty.ok ? { kind: 'set', qty: qty.value } : null
  const count = preview.count
  const canApply = ready && !pending && !preview.error && count != null && count > 0 && !busy

  const run = async () => {
    if (!action) return
    setBusy(true)
    setProblem(null)
    try {
      const r = await mutation.mutateAsync({ publicId, body: capacityRequestBody(effective, action) })
      const matched = r.matched ?? 0
      const params = { changed: fmt(r.changed ?? 0), matched: fmt(matched) }
      const key = mode === 'set' ? 'appliedSet' : 'appliedClear'
      toast.success(t(`warehouse.binCapacity.${key}${matched === 1 ? 'One' : ''}`, params))
      onClose()
    } catch (err) {
      // el error se queda en el modal (no un toast que se pierde): título arriba y el del cupo bajo su campo
      setProblem(applyProblemDetails(err))
    } finally {
      setBusy(false)
    }
  }

  const apply = () => {
    if (mode === 'set' && !qty.ok) {
      setQtyTouched(true)
      qtyRef.current?.focus()
      return
    }
    if (!canApply || count == null) return
    if (needsCapacityConfirmation(count, effective)) setConfirming(true)
    else void run()
  }

  const countText = count == null ? '' : preview.exact ? fmt(count) : t('warehouse.binCapacity.upToCount', { count: fmt(count) })
  let previewText: string
  let previewTone = ''
  if (!ready) previewText = t('warehouse.binCapacity.preview.needScope')
  else if (pending) previewText = t('warehouse.binCapacity.preview.pending')
  else if (preview.error) {
    previewText = t('warehouse.binCapacity.preview.error', { message: applyProblemDetails(preview.error).title })
    previewTone = ' bad'
  } else if (count === 0) {
    previewText = t('warehouse.binCapacity.preview.none')
    previewTone = ' warn'
  } else if (count != null && !preview.exact) previewText = t('warehouse.binCapacity.preview.upTo', { count: fmt(count) })
  else if (count === 1) previewText = t('warehouse.binCapacity.preview.one')
  else previewText = t('warehouse.binCapacity.preview.many', { count: fmt(count ?? 0) })

  const applyLabel = mode === 'set' ? t('warehouse.binCapacity.applySet') : t('warehouse.binCapacity.applyClear')
  const qtyDescribedBy = [`${qtyId}-help`, qtyError || serverQtyError ? `${qtyId}-err` : null].filter(Boolean).join(' ')

  return (
    <>
      <Modal
        open
        title={t('warehouse.binCapacity.title')}
        onClose={onClose}
        dismissible={!busy && !confirming}
        footer={
          <>
            <button type="button" className="btn" onClick={onClose} disabled={busy}>
              {t('common.cancel')}
            </button>
            <button type="button" className="btn flow" onClick={apply} disabled={!canApply}>
              {busy ? t('common.loading') : applyLabel}
            </button>
          </>
        }
      >
        {problem && (
          <div className="form-alert" role="alert">
            {problem.title}
            {otherServerErrors.length > 0 && (
              <ul className="bcap-errs">
                {otherServerErrors.map((m) => (
                  <li key={m}>{m}</li>
                ))}
              </ul>
            )}
          </div>
        )}
        <p className="bcap-intro">{t('warehouse.binCapacity.intro')}</p>

        <fieldset className="bcap-set">
          <legend>{t('warehouse.binCapacity.scope')}</legend>
          <SearchSelect
            label={t('warehouse.binCapacity.zone')}
            options={zoneOptions}
            value={effective.zoneIds}
            onChange={set('zoneIds')}
            placeholder={t('warehouse.binCapacity.anyZone')}
          />
          <div className="r2">
            <TextFilter label={t('warehouse.binCapacity.aisle')} value={scope.aisle} onChange={set('aisle')} placeholder={t('warehouse.binCapacity.containsPlaceholder')} />
            <TextFilter label={t('warehouse.binCapacity.rack')} value={scope.rack} onChange={set('rack')} placeholder={t('warehouse.binCapacity.containsPlaceholder')} />
          </div>
          <div className="r2">
            <TextFilter label={t('warehouse.binCapacity.level')} value={scope.level} onChange={set('level')} placeholder={t('warehouse.binCapacity.containsPlaceholder')} />
            <TextFilter
              label={t('warehouse.binCapacity.position')}
              value={scope.position}
              onChange={set('position')}
              placeholder={t('warehouse.binCapacity.containsPlaceholder')}
            />
          </div>
          <div className="bcap-switches">
            <SwitchField
              label={t('warehouse.binCapacity.onlyWithoutCapacity')}
              checked={effective.onlyWithoutCapacity}
              onChange={set('onlyWithoutCapacity')}
              disabled={mode === 'clear'}
              help={mode === 'clear' ? t('warehouse.binCapacity.onlyWithoutCapacityClear') : undefined}
            />
            {/* "Todo el almacén" solo sin filtros: con alguno, el API no lo necesita y se ignora */}
            {!filtered && <SwitchField label={t('warehouse.binCapacity.allBins')} checked={scope.allBins} onChange={set('allBins')} />}
          </div>
        </fieldset>

        <fieldset className="bcap-set">
          <legend>{t('warehouse.binCapacity.action')}</legend>
          <div className="bcap-mode">
            <label className="bcap-radio">
              <input type="radio" name={`${qtyId}-mode`} value="set" checked={mode === 'set'} onChange={() => setMode('set')} />
              <span>{t('warehouse.binCapacity.modeSet')}</span>
            </label>
            <label className="bcap-radio">
              <input type="radio" name={`${qtyId}-mode`} value="clear" checked={mode === 'clear'} onChange={() => setMode('clear')} />
              <span>{t('warehouse.binCapacity.modeClear')}</span>
            </label>
          </div>
          {mode === 'set' && (
            <div className="f bcap-qty">
              <label htmlFor={qtyId}>
                {t('warehouse.binCapacity.qty')}
                <span className="req" aria-hidden="true">
                  *
                </span>
              </label>
              <input
                ref={qtyRef}
                id={qtyId}
                type="number"
                inputMode="numeric"
                min={1}
                step={1}
                required
                value={qtyText}
                aria-invalid={qtyError || serverQtyError ? true : undefined}
                aria-describedby={qtyDescribedBy}
                onChange={(e) => {
                  setQtyText(e.target.value)
                  // el error del servidor sobre el cupo se descarta en cuanto el usuario lo corrige
                  if (problem?.errors.maxCapacityQty) setProblem(null)
                }}
                onBlur={() => setQtyTouched(true)}
              />
              <p id={`${qtyId}-help`} className="help">
                {t('warehouse.binCapacity.qtyHelp')}
              </p>
              {(qtyError || serverQtyError) && (
                <p id={`${qtyId}-err`} className="ferr">
                  {qtyError ?? serverQtyError}
                </p>
              )}
            </div>
          )}
        </fieldset>

        <p className={`note bcap-preview${previewTone}`} role="status" aria-live="polite">
          {previewText}
        </p>
      </Modal>

      <ConfirmDialog
        open={confirming}
        title={t('warehouse.binCapacity.confirmTitle')}
        message={
          <>
            <p className="bcap-confirm">
              {mode === 'set'
                ? t('warehouse.binCapacity.confirmSet', { qty: qty.ok ? fmt(qty.value) : '', count: countText })
                : t('warehouse.binCapacity.confirmClear', { count: countText })}
            </p>
            {effective.allBins && <p className="bcap-confirm">{t('warehouse.binCapacity.confirmAll')}</p>}
            <p className="bcap-confirm help">{t('warehouse.binCapacity.confirmUnchanged')}</p>
          </>
        }
        confirmLabel={applyLabel}
        onConfirm={run}
        onClose={() => setConfirming(false)}
      />
    </>
  )
}
