// Lote F-E1 — Operación → Estación de escaneo (`/ops/scan`, `trips.scan`, LTL_GROUND; maqueta `scan()`; manual 05 §7). Solo el modo
// «Salida» tiene servidor. La entrada recibe el lector (escribe y dispara Enter) o el teclado; cada lectura va en fila al API (una a la
// vez, para que los resultados salgan en el orden en que se escanearon) y el resultado se ve grande, se dice con voz y queda en la
// lista de la sesión. «Pendientes por escanear» (trips.view) y los filtros Zona (fleet.view) / Cliente (clients.read) se degradan solos.
import { useQueryClient } from '@tanstack/react-query'
import { useCallback, useContext, useEffect, useMemo, useRef, useState, type FormEvent } from 'react'
import { SessionContext } from '../../app/session'
import { useCan, useModule } from '../../kernel/access'
import { api, unwrap } from '../../kernel/api/client'
import { fetchAllPages } from '../../kernel/api/fetchAllPages'
import { applyProblemDetails } from '../../kernel/api/problem'
import { tenantToday } from '../../kernel/api/tenantZone'
import { useFormat } from '../../kernel/format'
import { useLang, useT } from '../../kernel/i18n'
import { ClientPicker, Filters, IconScan, IconVolume, IconVolumeOff, SelectFilter, toast, useAppliedFilters } from '../../kernel/ui'
import { IconDoc } from '../../kernel/ui/screenIcons'
import { usePrinting } from '../printing/usePrinting'
import { missingReportSpec } from './missingReport'
import { useDispatchZones } from '../drivers/api'
import { orderKeys } from '../orders/api'
import { useDebounced } from '../warehouse/lineRules'
import { fetchOrderKnown, SCAN_ACTIONS, type ScanActionKey, useCloseCount, useScanAction, useUnassignedOrders } from './scanApi'
import { ScanReappearedConfirm } from './ScanReappearedConfirm'
import { ScanOrderModal, type PeekTarget } from './ScanOrderModal'
import { ScanFeedPanel, ScanPendingPanel } from './ScanPanels'
import { ScanResultBox, type LastScan } from './ScanResultBox'
import {
  assignedOrderIds,
  PENDING_TAKE,
  buildPendingQuery,
  codeProblemKey,
  entryMatchesFilters,
  entryRef,
  knownFromPending,
  meterOf,
  NO_FILTERS,
  normalizeCode,
  voiceKind,
  hasFilters,
  type KnownOrder,
  type ScanEntry,
  type ScanFilters,
  type ScanResult,
  type UnassignedOrder,
} from './scanRules'
import { addScanEntry, nextEntryId, updateScanKnown, useScanEntries, useScanScope } from './scanSession'
import { speakWord, useScanVoice } from './scanVoice'
import './scan.css'

const NO_ITEMS: readonly UnassignedOrder[] = []

export default function ScanScreen() {
  const t = useT()
  const lang = useLang()
  const f = useFormat()
  const qc = useQueryClient()
  const canTrips = useCan('trips.view')
  const canOrders = useCan('orders.view')
  const canReturn = useCan('orders.return')   // Lote 112: la acción «Devolver»
  const canFleet = useCan('fleet.view')
  const canClients = useCan('clients.read')
  const catalogOn = useModule('CATALOG')
  const voice = useScanVoice()
  // Lote F-G2: «Informe de faltantes» (los pendientes con los mismos filtros, todas las páginas) por la impresora de documentos
  const printing = usePrinting()
  const appliedFilters = useAppliedFilters()
  const me = useContext(SessionContext)?.me
  const [missingBusy, setMissingBusy] = useState(false)

  const inputRef = useRef<HTMLInputElement>(null)
  const [text, setText] = useState('')
  const scope = useScanScope()
  const entries = useScanEntries(scope)
  const [last, setLast] = useState<LastScan | null>(null)
  const [busy, setBusy] = useState(0)
  const [planDate, setPlanDate] = useState<string | null>(null)
  const [peek, setPeek] = useState<PeekTarget | null>(null)
  const [filters, setFilters] = useState<ScanFilters>(NO_FILTERS)
  const [clientPublicId, setClientPublicId] = useState<string | null>(null)

  const scan = useScanAction()
  const closeCount = useCloseCount()
  // Lote 111b: si al cerrar el conteo aparecieron piezas perdidas, el servidor pide confirmar la orden nueva antes de hacer nada
  const [reappeared, setReappeared] = useState<{ orderPublicId: string; preview: NonNullable<Awaited<ReturnType<typeof closeCount.mutateAsync>>['preview']> } | null>(null)
  const closeOrderCount = (orderPublicId: string, confirm = false) =>
    closeCount.mutate(
      { orderPublicId, confirm, planDate },
      {
        onSuccess: (r) => {
          if (r.needsConfirmation && r.preview) return setReappeared({ orderPublicId, preview: r.preview })
          setReappeared(null)
          if ((r.missing ?? 0) > 0) toast.error(r.message ?? '')
          else toast.success(r.message ?? '')
          if (r.newOrderPublicId) void qc.invalidateQueries({ queryKey: orderKeys.list })
        },
        onError: (e) => toast.error(applyProblemDetails(e).title),
      },
    )
  const [action, setAction] = useState<ScanActionKey>('ROUTE')   // Lote 107: el usuario escoge qué va a hacer; por omisión, Enrutar (lo de siempre)
  const zones = useDispatchZones(false, canFleet && catalogOn)
  const showZoneFilter = canTrips && canFleet && catalogOn && !zones.isError && (zones.data?.length ?? 0) > 0
  const showClientFilter = canTrips && canClients && catalogOn

  // los filtros de texto esperan un instante antes de ir al servidor
  const settled = useDebounced(filters, 300)
  const settledClient = useDebounced(clientPublicId, 300)
  const zoneCode = zones.data?.find((z) => String(z.id) === settled.zone)?.code ?? null
  const matchFilters = useMemo<ScanFilters>(() => ({ ...settled, zoneCode }), [settled, zoneCode])
  const query = useMemo(() => buildPendingQuery(settled, settledClient), [settled, settledClient])
  const pending = useUnassignedOrders(query, canTrips)
  const pendingItems = pending.data?.items ?? NO_ITEMS
  const filtered = hasFilters(settled) || settledClient !== null

  // refs con lo último, para la fila de escaneos (corre fuera del ciclo de render)
  const latest = useRef({ planDate, pendingItems, voiceOn: voice.on, lang, canOrders, scope, action })
  useEffect(() => {
    latest.current = { planDate, pendingItems, voiceOn: voice.on, lang, canOrders, scope, action }
  })
  const chain = useRef<Promise<void>>(Promise.resolve())

  const focusInput = useCallback(() => inputRef.current?.focus(), [])
  useEffect(() => focusInput(), [focusInput])
  /** Al terminar un escaneo vuelve el foco a la entrada, salvo que el usuario esté escribiendo en otro campo o con una ventana abierta. */
  const focusIfIdle = useCallback(() => {
    const active = document.activeElement
    if (active?.closest('.scrim')) return
    if (!active || active === document.body || active === inputRef.current || active.tagName === 'BUTTON') focusInput()
  }, [focusInput])

  const sayFor = useCallback(
    (kind: 'found' | 'dup' | 'notfound') => {
      if (latest.current.voiceOn) speakWord(t(`scan.voice.${kind}`), latest.current.lang)
    },
    [t],
  )

  const runScan = useCallback(
    async (code: string) => {
      const date = latest.current.planDate ?? tenantToday()
      try {
        const result: ScanResult = await scan.mutateAsync({ action: latest.current.action, code, planDate: date })
        const fromPending = latest.current.pendingItems.find((o) => o.publicId && o.publicId === result.orderPublicId)
        const known: KnownOrder = { ...(fromPending ? knownFromPending(fromPending) : {}), ...(result.zoneCode ? { zoneCode: result.zoneCode } : {}) }
        const entry: ScanEntry = { id: nextEntryId(), code, at: new Date().toISOString(), result, known }
        addScanEntry(latest.current.scope, entry)
        setLast({ kind: 'result', result, ref: entryRef(entry) })
        sayFor(voiceKind(result))
        // sin datos de «Pendientes», la ficha de la orden (si el usuario puede verla) completa cliente y pueblo
        if (!fromPending && result.orderPublicId && latest.current.canOrders) {
          void fetchOrderKnown(qc, result.orderPublicId).then((more) => {
            if (more) updateScanKnown(latest.current.scope, entry.id, (k) => ({ ...definedOnly(more), ...definedOnly(k) }))
          })
        }
      } catch (error) {
        const problem = applyProblemDetails(error)
        const first = Object.values(problem.errors).flat().find(Boolean)
        setLast({ kind: 'error', message: first ?? problem.title })
        sayFor('notfound')
      } finally {
        setBusy((n) => n - 1)
      }
    },
    [qc, sayFor, scan],
  )

  const submit = (e?: FormEvent) => {
    e?.preventDefault()
    const raw = text
    setText('')
    focusInput()
    const problem = codeProblemKey(raw)
    if (problem) {
      setLast({ kind: 'error', message: t(problem) })
      if (problem === 'scan.errors.tooLong') sayFor('notfound')
      return
    }
    const code = normalizeCode(raw)
    setBusy((n) => n + 1)
    chain.current = chain.current.then(() => runScan(code)).then(focusIfIdle)
  }

  // medidor «N de M»: asignadas en la sesión (dentro del filtro) contra las que siguen pendientes con ese filtro
  const inFilter = useMemo(() => entries.filter((e) => entryMatchesFilters(e, matchFilters)), [entries, matchFilters])
  const meter = meterOf(assignedOrderIds(inFilter).size, canTrips && pending.data ? (pending.data.total ?? 0) : null)

  const zoneOptions = useMemo(
    () => [
      { value: 'none', label: t('scan.filters.noZone') },
      ...(zones.data ?? []).map((z) => ({ value: String(z.id), label: [z.code, z.name].filter(Boolean).join(' · ') })),
    ],
    [zones.data, t],
  )

  const today = tenantToday()
  const shownDate = planDate ?? today

  async function missingReport() {
    if (missingBusy) return
    setMissingBusy(true)
    try {
      const all = await fetchAllPages(
        (skip, take) => unwrap(api.GET('/api/v1/trips/unassigned-orders', { params: { query: { ...query, Skip: skip, Take: take } } })),
        { pageSize: PENDING_TAKE },
      )
      const filters = appliedFilters()   // el cliente (ClientPicker con filterLabel) ya se anota solo
      const spec = missingReportSpec(all.items, {
        t,
        locale: lang,
        company: me?.tenantName,
        user: me?.fullName ?? me?.email,
        filters,
        scanned: assignedOrderIds(inFilter).size,
        date: (iso) => (iso ? f.date(iso) : ''),
      })
      await printing.printDocument(spec, t('scan.missing.file'))
    } catch (e) {
      toast.error(applyProblemDetails(e).title)
    } finally {
      setMissingBusy(false)
    }
  }

  return (
    <div className="wrap scn">
      <div className="head">
        <div>
          <h1>{t('scan.title')}</h1>
          <p>{t('scan.sub')}</p>
        </div>
        <div className="act">
          <button
            type="button"
            className="btn sm"
            onClick={voice.toggle}
            disabled={!voice.supported}
            aria-pressed={voice.on}
            title={voice.supported ? undefined : t('scan.voice.unsupportedTitle')}
          >
            {voice.on ? <IconVolume /> : <IconVolumeOff />}{' '}
            {!voice.supported ? t('scan.voice.unsupported') : voice.on ? t('scan.voice.on') : t('scan.voice.off')}
          </button>
          {canTrips && (
            <button type="button" className="btn sm" disabled={missingBusy} onClick={() => void missingReport()}>
              <IconDoc /> {t('scan.missing.button')}
            </button>
          )}
        </div>
      </div>

      {printing.dialog}

      <div className="scanhero">
        <div className="modes" role="radiogroup" aria-label={t('scan.mode.label')}>
          {SCAN_ACTIONS.filter((a) => a !== 'RETURN' || canReturn).map((a) => (
            <button
              key={a}
              type="button"
              role="radio"
              aria-checked={action === a}
              className={action === a ? 'on' : undefined}
              onClick={() => {
                setAction(a)
                focusInput()
              }}
            >
              {t(`scan.action.${a}`)}
            </button>
          ))}
        </div>
        <div className="hint" data-testid="scan-action-help">{t(`scan.actionHelp.${action}`)}</div>
        <form className="scaninput" onSubmit={submit}>
          <IconScan />
          <input
            ref={inputRef}
            type="text"
            value={text}
            onChange={(e) => setText(e.target.value)}
            placeholder={t('scan.input.placeholder')}
            aria-label={t('scan.input.label')}
            autoComplete="off"
            autoCapitalize="off"
            autoCorrect="off"
            spellCheck={false}
            enterKeyHint="done"
          />
          <button type="submit" className="go">
            {t('scan.input.go')}
          </button>
        </form>
        <div className="hint">{t('scan.input.hint')}</div>
        <ScanResultBox last={last} busy={busy} closing={closeCount.isPending} onCloseCount={(id) => closeOrderCount(id)} />
        {reappeared && <ScanReappearedConfirm preview={reappeared.preview} busy={closeCount.isPending} onConfirm={() => closeOrderCount(reappeared.orderPublicId, true)} onCancel={() => setReappeared(null)} />}
        <div className="scn-date">
          <label htmlFor="scan-plan-date">{t('scan.planDate.label')}</label>
          <input
            id="scan-plan-date"
            type="date"
            value={shownDate}
            onChange={(e) => {
              setPlanDate(e.target.value === '' || e.target.value === today ? null : e.target.value)
              focusInput()
            }}
          />
          {planDate !== null && (
            <button type="button" className="btn sm" onClick={() => { setPlanDate(null); focusInput() }}>
              {t('scan.planDate.today')}
            </button>
          )}
          <span className="scn-date-help">{planDate === null ? t('scan.planDate.helpToday') : t('scan.planDate.helpOther', { date: f.date(planDate) })}</span>
        </div>
      </div>

      {canTrips && (
        <div className="scn-filters">
          <Filters label={t('scan.filters.label')} onClear={hasFilters(filters) || clientPublicId !== null ? () => { setFilters(NO_FILTERS); setClientPublicId(null) } : undefined}>
            {showClientFilter && (
              <div className="f">
                <label htmlFor="scan-client">{t('scan.filters.client')}</label>
                <ClientPicker
                  id="scan-client"
                  value={clientPublicId}
                  onChange={(id, c) => {
                    setClientPublicId(id)
                    setFilters((x) => ({ ...x, clientName: id ? (c?.name ?? null) : null }))
                  }}
                  placeholder={t('scan.filters.all')}
                  filterLabel={t('scan.filters.client')}
                />
              </div>
            )}
            {showZoneFilter && (
              <SelectFilter
                label={t('scan.filters.zone')}
                value={filters.zone}
                onChange={(zone) => setFilters((x) => ({ ...x, zone }))}
                options={zoneOptions}
                allLabel={t('scan.filters.all')}
              />
            )}
            <div className="f">
              <label htmlFor="scan-postal">{t('scan.filters.postal')}</label>
              <input id="scan-postal" type="text" inputMode="numeric" value={filters.postalCode} onChange={(e) => setFilters((x) => ({ ...x, postalCode: e.target.value }))} />
            </div>
            <div className="f">
              <label htmlFor="scan-city">{t('scan.filters.city')}</label>
              <input id="scan-city" type="text" value={filters.city} onChange={(e) => setFilters((x) => ({ ...x, city: e.target.value }))} />
            </div>
          </Filters>
        </div>
      )}

      <div className="scanmeter">
        <div className="nums">
          <b>{f.number(meter.done)}</b>{' '}
          {meter.total !== null && <span className="of">{t('scan.meter.of', { total: f.number(meter.total) })}</span>}
          <span className="cap">{t('scan.meter.caption')}</span>
        </div>
        {meter.total !== null && (
          <div className="track" role="progressbar" aria-label={t('scan.meter.label')} aria-valuemin={0} aria-valuemax={100} aria-valuenow={meter.percent}>
            <div className="fill" style={{ width: `${meter.percent}%` }} />
          </div>
        )}
      </div>

      <ScanFeedPanel entries={entries} filters={matchFilters} onOpen={setPeek} />

      {canTrips && (
        <ScanPendingPanel
          items={pendingItems}
          total={pending.data?.total ?? 0}
          loading={pending.isLoading}
          failed={pending.isError}
          filtered={filtered}
          onRetry={() => void pending.refetch()}
          onOpen={setPeek}
        />
      )}

      <ScanOrderModal
        target={peek}
        onClose={() => {
          setPeek(null)
          setTimeout(focusInput, 0)
        }}
      />
    </div>
  )
}

/** Solo los campos con valor (para no pisar lo ya sabido con vacíos). */
function definedOnly(k: KnownOrder): KnownOrder {
  return Object.fromEntries(Object.entries(k).filter(([, v]) => v != null && v !== '')) as KnownOrder
}
