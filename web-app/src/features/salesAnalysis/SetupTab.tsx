// Lote 241 — pestaña «Configuración» del Análisis de ventas (sección en `?sec=`): Rutas, Representantes, Cuotas, Líneas y
// marca → línea, Clientes sin representante (asignación en bloque), Ajustes (fecha de corte, días de entrega y nivel de
// servicio), Importar historial de QuickBooks y Emparejar a mano. Con `sales.analysis.manage` se edita; sin él, solo lectura.
import { useEffect, useMemo, useState, type FormEvent } from 'react'
import { useSearchParams } from 'react-router-dom'
import { useCan } from '../../kernel/access'
import { fetchAllPages } from '../../kernel/api/fetchAllPages'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useT } from '../../kernel/i18n/useT'
import { IconEdit, IconPower, IconRotateCcw } from '../../kernel/ui/actionIcons'
import { Chip } from '../../kernel/ui/Chip'
import { ConfirmDialog } from '../../kernel/ui/ConfirmDialog'
import { DataTable, type DataColumn } from '../../kernel/ui/DataTable'
import { Filters, SelectFilter } from '../../kernel/ui/Filters'
import { Modal } from '../../kernel/ui/Modal'
import { Panel } from '../../kernel/ui/Panel'
import { QBox } from '../../kernel/ui/QBox'
import { SearchMultiSelect } from '../../kernel/ui/SearchSelect'
import { Spinner } from '../../kernel/ui/Spinner'
import { Tabs } from '../../kernel/ui/Tabs'
import { toast } from '../../kernel/ui/toast'
import {
  fetchSalesClients,
  useAssignClientsRep,
  useCompanyUsers,
  useLinkUnmatched,
  useProductLines,
  useProductSearch,
  useRepsFromCustomField,
  useSalesBrands,
  useSalesClients,
  useSalesReps,
  useSalesRoutes,
  useSalesSettings,
  useSalesStatus,
  useSaveLine,
  useSaveRep,
  useSaveRoute,
  useSaveSettings,
  useSetLineActive,
  useSetLineBrands,
  useSetRepActive,
  useSetRouteActive,
  type ClientRepQuery,
} from './salesAnalysisApi'
import { SALES_ANALYSIS_MANAGE, SERVICE_LEVELS, SETUP_SECTIONS, type ProductLine, type SalesBrand, type SalesClientRep, type SalesRep, type SalesRoute, type SalesUnmatched, type SetupSection } from './salesAnalysisRules'
import { ImportHistory, UnmatchedList } from './ImportHistory'
import { QuotasGrid } from './QuotasGrid'
import { QueryError } from './SaParts'
import { useSaFormat } from './saFormat'

const NO_ROWS: never[] = []

export function SetupTab() {
  const t = useT()
  const [params, setParams] = useSearchParams()
  const raw = params.get('sec') as SetupSection | null
  const sec: SetupSection = raw && SETUP_SECTIONS.includes(raw) ? raw : 'routes'
  const setSec = (k: SetupSection) =>
    setParams(
      (prev) => {
        const next = new URLSearchParams(prev)
        next.set('tab', 'setup')
        if (k === 'routes') next.delete('sec')
        else next.set('sec', k)
        return next
      },
      { replace: true },
    )
  const canManage = useCan(SALES_ANALYSIS_MANAGE)
  return (
    <div className="sa-tab">
      {!canManage && <p className="note">{t('salesAnalysis.setup.readOnly')}</p>}
      <Tabs<SetupSection> label={t('salesAnalysis.setup.sections')} value={sec} onChange={setSec} tabs={SETUP_SECTIONS.filter((s) => canManage || (s !== 'import' && s !== 'link')).map((s) => ({ key: s, label: t(`salesAnalysis.setup.sec.${s}`) }))} />
      {sec === 'routes' && <RoutesSection />}
      {sec === 'reps' && <RepsSection />}
      {sec === 'quotas' && <QuotasGrid />}
      {sec === 'lines' && <LinesSection />}
      {sec === 'clients' && <ClientsSection />}
      {sec === 'settings' && <SettingsSection />}
      {sec === 'import' && canManage && <ImportHistory onLink={() => setSec('link')} />}
      {sec === 'link' && canManage && <LinkSection />}
    </div>
  )
}

// ---------------------------------------------------------------- formulario de nombre + orden (rutas y líneas) ----------------------------------------------------------------

interface NameSortValue {
  id?: number
  name: string
  sortOrder: number | null
}

function NameSortModal({ title, value, onClose, onSave }: { title: string; value: NameSortValue | null; onClose: () => void; onSave: (v: NameSortValue) => Promise<unknown> }) {
  const t = useT()
  const [name, setName] = useState('')
  const [sort, setSort] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  useEffect(() => {
    setName(value?.name ?? '')
    setSort(value?.sortOrder !== null && value?.sortOrder !== undefined ? String(value.sortOrder) : '')
    setError(null)
  }, [value])
  const submit = async (e: FormEvent) => {
    e.preventDefault()
    setBusy(true)
    setError(null)
    try {
      await onSave({ id: value?.id, name: name.trim(), sortOrder: sort.trim() === '' ? null : Number(sort) })
      onClose()
    } catch (err) {
      setError(applyProblemDetails(err).title)
    } finally {
      setBusy(false)
    }
  }
  return (
    <Modal
      open={value !== null}
      title={title}
      onClose={onClose}
      size="sm"
      dismissible={!busy}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose} disabled={busy}>{t('common.cancel')}</button>
          <button type="submit" form="sa-name-sort" className="btn flow" disabled={busy}>{busy ? t('salesAnalysis.saving') : t('salesAnalysis.save')}</button>
        </>
      }
    >
      <form id="sa-name-sort" onSubmit={(e) => void submit(e)}>
        {error && <p className="ferr" role="alert">{error}</p>}
        <div className="f">
          <label htmlFor="sa-ns-name">{t('salesAnalysis.setup.name')}</label>
          <input id="sa-ns-name" value={name} maxLength={100} onChange={(e) => setName(e.target.value)} />
        </div>
        <div className="f">
          <label htmlFor="sa-ns-sort">{t('salesAnalysis.setup.sortOrder')}</label>
          <input id="sa-ns-sort" type="number" inputMode="numeric" value={sort} onChange={(e) => setSort(e.target.value)} />
          <span className="sa-muted">{t('salesAnalysis.setup.sortHelp')}</span>
        </div>
      </form>
    </Modal>
  )
}

function useToggleConfirm<T extends { id?: number; name?: string | null; isActive?: boolean }>(mutate: (v: { id: number; active: boolean }) => Promise<unknown>) {
  const [row, setRow] = useState<T | null>(null)
  return { row, ask: (r: T) => setRow(r), close: () => setRow(null), run: () => mutate({ id: row?.id ?? 0, active: !row?.isActive }) }
}

// ---------------------------------------------------------------- rutas ----------------------------------------------------------------

function RoutesSection() {
  const t = useT()
  const q = useSalesRoutes()
  const save = useSaveRoute()
  const toggle = useSetRouteActive()
  const [editing, setEditing] = useState<NameSortValue | null>(null)
  const confirm = useToggleConfirm<SalesRoute>(toggle.mutateAsync)
  const canManage = useCan(SALES_ANALYSIS_MANAGE)
  const columns = useMemo<DataColumn<SalesRoute>[]>(
    () => [
      { id: 'name', header: t('salesAnalysis.setup.name'), cell: (r) => r.name ?? '', sortValue: (r) => r.name ?? '', card: 'title' },
      { id: 'sortOrder', header: t('salesAnalysis.setup.sortOrder'), cell: (r) => String(r.sortOrder ?? ''), sortValue: (r) => r.sortOrder ?? 0, align: 'end' },
      { id: 'reps', header: t('salesAnalysis.setup.repCount'), cell: (r) => String(r.repCount ?? 0), sortValue: (r) => r.repCount ?? 0, align: 'end' },
      { id: 'active', header: t('salesAnalysis.setup.status'), cell: (r) => <Chip tone={r.isActive ? 'deliv' : 'cap'}>{t(r.isActive ? 'salesAnalysis.setup.active' : 'salesAnalysis.setup.inactive')}</Chip>, sortValue: (r) => (r.isActive ? 0 : 1) },
    ],
    [t],
  )
  return (
    <Panel
      title={t('salesAnalysis.setup.sec.routes')}
      subtitle={t('salesAnalysis.setup.routesSub')}
      badge={q.data?.length}
      flush
      actions={canManage ? <button type="button" className="btn flow sm" onClick={() => setEditing({ name: '', sortOrder: null })}>{t('salesAnalysis.setup.newRoute')}</button> : undefined}
    >
      {q.isError ? <div className="pb"><QueryError error={q.error} /></div> : (
        <DataTable
          label={t('salesAnalysis.setup.sec.routes')}
          columns={columns}
          rows={q.data ?? NO_ROWS}
          rowKey={(r) => String(r.id)}
          loading={q.isPending}
          pagination={false}
          rowClassName={(r) => (r.isActive ? undefined : 'dim')}
          empty={t('salesAnalysis.setup.noRoutes')}
          rowActions={[
            { key: 'edit', label: t('salesAnalysis.setup.edit'), icon: <IconEdit />, perm: SALES_ANALYSIS_MANAGE, onClick: (r) => setEditing({ id: r.id, name: r.name ?? '', sortOrder: r.sortOrder ?? null }) },
            { key: 'off', label: t('salesAnalysis.setup.deactivate'), icon: <IconPower />, tone: 'danger', perm: SALES_ANALYSIS_MANAGE, visible: (r) => !!r.isActive, onClick: confirm.ask },
            { key: 'on', label: t('salesAnalysis.setup.reactivate'), icon: <IconRotateCcw />, perm: SALES_ANALYSIS_MANAGE, visible: (r) => !r.isActive, onClick: confirm.ask },
          ]}
        />
      )}
      <NameSortModal title={editing?.id ? t('salesAnalysis.setup.editRoute') : t('salesAnalysis.setup.newRoute')} value={editing} onClose={() => setEditing(null)} onSave={(v) => save.mutateAsync({ id: v.id, name: v.name, sortOrder: v.sortOrder })} />
      <ConfirmDialog
        open={confirm.row !== null}
        title={t(confirm.row?.isActive ? 'salesAnalysis.setup.deactivateTitle' : 'salesAnalysis.setup.reactivateTitle', { name: confirm.row?.name ?? '' })}
        message={t(confirm.row?.isActive ? 'salesAnalysis.setup.deactivateRouteMsg' : 'salesAnalysis.setup.reactivateMsg')}
        tone={confirm.row?.isActive ? 'danger' : 'flow'}
        onConfirm={confirm.run}
        onClose={confirm.close}
      />
    </Panel>
  )
}

// ---------------------------------------------------------------- representantes ----------------------------------------------------------------

interface RepForm {
  id?: number
  name: string
  routeId: string
  userId: string
}

function RepsSection() {
  const t = useT()
  const fm = useSaFormat()
  const canManage = useCan(SALES_ANALYSIS_MANAGE)
  const q = useSalesReps()
  const routes = useSalesRoutes()
  const save = useSaveRep()
  const toggle = useSetRepActive()
  const fromField = useRepsFromCustomField()
  const [editing, setEditing] = useState<RepForm | null>(null)
  const users = useCompanyUsers(editing !== null)
  const confirm = useToggleConfirm<SalesRep>(toggle.mutateAsync)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const columns = useMemo<DataColumn<SalesRep>[]>(
    () => [
      { id: 'name', header: t('salesAnalysis.setup.name'), cell: (r) => r.name ?? '', sortValue: (r) => r.name ?? '', card: 'title' },
      { id: 'route', header: t('salesAnalysis.col.route'), cell: (r) => r.routeName || <Chip tone="warn">{t('salesAnalysis.setup.noRoute')}</Chip>, sortValue: (r) => r.routeName ?? '', exportValue: (r) => r.routeName ?? '' },
      { id: 'user', header: t('salesAnalysis.setup.user'), cell: (r) => r.userName || '—', sortValue: (r) => r.userName ?? '' },
      { id: 'clients', header: t('salesAnalysis.setup.clientCount'), cell: (r) => String(r.clientCount ?? 0), sortValue: (r) => r.clientCount ?? 0, align: 'end' },
      { id: 'quota', header: t('salesAnalysis.setup.currentQuota'), cell: (r) => fm.money(r.currentQuota), sortValue: (r) => r.currentQuota ?? null, align: 'end', exportValue: (r) => r.currentQuota ?? null },
      { id: 'active', header: t('salesAnalysis.setup.status'), cell: (r) => <Chip tone={r.isActive ? 'deliv' : 'cap'}>{t(r.isActive ? 'salesAnalysis.setup.active' : 'salesAnalysis.setup.inactive')}</Chip>, sortValue: (r) => (r.isActive ? 0 : 1) },
    ],
    [t, fm],
  )
  const submit = async (e: FormEvent) => {
    e.preventDefault()
    if (!editing) return
    setBusy(true)
    setError(null)
    try {
      await save.mutateAsync({ id: editing.id, name: editing.name.trim(), routeId: editing.routeId ? Number(editing.routeId) : null, userId: editing.userId ? Number(editing.userId) : null })
      setEditing(null)
    } catch (err) {
      setError(applyProblemDetails(err).title)
    } finally {
      setBusy(false)
    }
  }
  const migrate = async () => {
    try {
      const r = await fromField.mutateAsync()
      toast.success(t('salesAnalysis.setup.fromFieldDone', { reps: r.repsCreated ?? 0, linked: r.clientsLinked ?? 0, already: r.clientsAlreadyLinked ?? 0 }))
    } catch (err) {
      toast.error(applyProblemDetails(err).title)
    }
  }
  const activeRoutes = (routes.data ?? []).filter((r) => r.isActive || String(r.id) === editing?.routeId)
  return (
    <Panel
      title={t('salesAnalysis.setup.sec.reps')}
      subtitle={t('salesAnalysis.setup.repsSub')}
      badge={q.data?.length}
      flush
      actions={
        canManage ? (
          <div className="sa-inline">
            <button type="button" className="btn sm" onClick={() => void migrate()} disabled={fromField.isPending} title={t('salesAnalysis.setup.fromFieldHelp')}>{t('salesAnalysis.setup.fromField')}</button>
            <button type="button" className="btn flow sm" onClick={() => (setError(null), setEditing({ name: '', routeId: '', userId: '' }))}>{t('salesAnalysis.setup.newRep')}</button>
          </div>
        ) : undefined
      }
    >
      {q.isError ? <div className="pb"><QueryError error={q.error} /></div> : (
        <DataTable
          label={t('salesAnalysis.setup.sec.reps')}
          columns={columns}
          rows={q.data ?? NO_ROWS}
          rowKey={(r) => String(r.id)}
          loading={q.isPending}
          pagination={false}
          rowClassName={(r) => (r.isActive ? undefined : 'dim')}
          empty={t('salesAnalysis.setup.noReps')}
          rowActions={[
            { key: 'edit', label: t('salesAnalysis.setup.edit'), icon: <IconEdit />, perm: SALES_ANALYSIS_MANAGE, onClick: (r) => (setError(null), setEditing({ id: r.id, name: r.name ?? '', routeId: r.routeId ? String(r.routeId) : '', userId: r.userId ? String(r.userId) : '' })) },
            { key: 'off', label: t('salesAnalysis.setup.deactivate'), icon: <IconPower />, tone: 'danger', perm: SALES_ANALYSIS_MANAGE, visible: (r) => !!r.isActive, onClick: confirm.ask },
            { key: 'on', label: t('salesAnalysis.setup.reactivate'), icon: <IconRotateCcw />, perm: SALES_ANALYSIS_MANAGE, visible: (r) => !r.isActive, onClick: confirm.ask },
          ]}
        />
      )}
      <Modal
        open={editing !== null}
        title={editing?.id ? t('salesAnalysis.setup.editRep') : t('salesAnalysis.setup.newRep')}
        onClose={() => setEditing(null)}
        size="sm"
        dismissible={!busy}
        footer={
          <>
            <button type="button" className="btn" onClick={() => setEditing(null)} disabled={busy}>{t('common.cancel')}</button>
            <button type="submit" form="sa-rep-form" className="btn flow" disabled={busy}>{busy ? t('salesAnalysis.saving') : t('salesAnalysis.save')}</button>
          </>
        }
      >
        {editing && (
          <form id="sa-rep-form" onSubmit={(e) => void submit(e)}>
            {error && <p className="ferr" role="alert">{error}</p>}
            <div className="f">
              <label htmlFor="sa-rep-name">{t('salesAnalysis.setup.name')}</label>
              <input id="sa-rep-name" value={editing.name} maxLength={100} onChange={(e) => setEditing({ ...editing, name: e.target.value })} />
            </div>
            <div className="f">
              <label htmlFor="sa-rep-route">{t('salesAnalysis.col.route')}</label>
              <select id="sa-rep-route" value={editing.routeId} onChange={(e) => setEditing({ ...editing, routeId: e.target.value })}>
                <option value="">{t('salesAnalysis.setup.noRoute')}</option>
                {activeRoutes.map((r) => (
                  <option key={r.id} value={String(r.id)}>{r.name}</option>
                ))}
              </select>
            </div>
            <div className="f">
              <label htmlFor="sa-rep-user">{t('salesAnalysis.setup.userOptional')}</label>
              <select id="sa-rep-user" value={editing.userId} onChange={(e) => setEditing({ ...editing, userId: e.target.value })} disabled={users.isError}>
                <option value="">{t('salesAnalysis.setup.noUser')}</option>
                {(users.data ?? []).filter((u) => u.isActive !== false || String(u.id) === editing.userId).map((u) => (
                  <option key={u.id} value={String(u.id)}>{u.fullName}{u.email ? ` · ${u.email}` : ''}</option>
                ))}
              </select>
              <span className="sa-muted">{users.isError ? t('salesAnalysis.setup.usersUnavailable') : t('salesAnalysis.setup.userHelp')}</span>
            </div>
          </form>
        )}
      </Modal>
      <ConfirmDialog
        open={confirm.row !== null}
        title={t(confirm.row?.isActive ? 'salesAnalysis.setup.deactivateTitle' : 'salesAnalysis.setup.reactivateTitle', { name: confirm.row?.name ?? '' })}
        message={t(confirm.row?.isActive ? 'salesAnalysis.setup.deactivateRepMsg' : 'salesAnalysis.setup.reactivateMsg')}
        tone={confirm.row?.isActive ? 'danger' : 'flow'}
        onConfirm={confirm.run}
        onClose={confirm.close}
      />
    </Panel>
  )
}

// ---------------------------------------------------------------- líneas y marcas ----------------------------------------------------------------

function LinesSection() {
  const t = useT()
  const canManage = useCan(SALES_ANALYSIS_MANAGE)
  const q = useProductLines()
  const brands = useSalesBrands()
  const save = useSaveLine()
  const toggle = useSetLineActive()
  const setBrands = useSetLineBrands()
  const [editing, setEditing] = useState<NameSortValue | null>(null)
  const [brandsOf, setBrandsOf] = useState<{ line: ProductLine; value: string[] } | null>(null)
  const [brandsError, setBrandsError] = useState<string | null>(null)
  const confirm = useToggleConfirm<ProductLine>(toggle.mutateAsync)
  const lines = q.data ?? NO_ROWS
  const [brandQ, setBrandQ] = useState('')
  const lineColumns = useMemo<DataColumn<ProductLine>[]>(
    () => [
      { id: 'name', header: t('salesAnalysis.setup.name'), cell: (r) => r.name ?? '', sortValue: (r) => r.name ?? '', card: 'title' },
      { id: 'sortOrder', header: t('salesAnalysis.setup.sortOrder'), cell: (r) => String(r.sortOrder ?? ''), sortValue: (r) => r.sortOrder ?? 0, align: 'end' },
      { id: 'brands', header: t('salesAnalysis.setup.brands'), cell: (r) => ((r.brands ?? []).length === 0 ? <span className="sa-muted">{t('salesAnalysis.setup.noBrands')}</span> : <span className="sa-chips">{(r.brands ?? []).map((b) => <Chip key={b}>{b}</Chip>)}</span>), sortValue: (r) => (r.brands ?? []).length, exportValue: (r) => (r.brands ?? []).join(', ') },
      { id: 'active', header: t('salesAnalysis.setup.status'), cell: (r) => <Chip tone={r.isActive ? 'deliv' : 'cap'}>{t(r.isActive ? 'salesAnalysis.setup.active' : 'salesAnalysis.setup.inactive')}</Chip>, sortValue: (r) => (r.isActive ? 0 : 1) },
    ],
    [t],
  )
  const assignBrand = async (brand: string, lineId: number) => {
    const line = lines.find((l) => l.id === lineId)
    if (!line) return
    try {
      await setBrands.mutateAsync({ id: lineId, brands: [...(line.brands ?? []), brand] })
      toast.success(t('salesAnalysis.setup.brandAssigned', { brand, line: line.name ?? '' }))
    } catch (err) {
      toast.error(applyProblemDetails(err).title)
    }
  }
  const brandRows = (brands.data ?? NO_ROWS).filter((b: SalesBrand) => !brandQ || (b.brand ?? '').toLowerCase().includes(brandQ.toLowerCase()))
  const brandColumns: DataColumn<SalesBrand>[] = [
    { id: 'brand', header: t('salesAnalysis.setup.brand'), cell: (b) => b.brand ?? '', sortValue: (b) => b.brand ?? '', card: 'title' },
    { id: 'products', header: t('salesAnalysis.setup.productCount'), cell: (b) => String(b.productCount ?? 0), sortValue: (b) => b.productCount ?? 0, align: 'end' },
    {
      id: 'line',
      header: t('salesAnalysis.col.line'),
      cell: (b) =>
        b.hasLine ? (
          b.line ?? ''
        ) : canManage ? (
          <span className="sa-inline">
            <Chip tone="warn">{t('salesAnalysis.setup.noLine')}</Chip>
            <label className="sr-only" htmlFor={`sa-bl-${b.brand}`}>{t('salesAnalysis.setup.assignLineOf', { brand: b.brand ?? '' })}</label>
            <select id={`sa-bl-${b.brand}`} value="" onChange={(e) => e.target.value && void assignBrand(b.brand ?? '', Number(e.target.value))}>
              <option value="">{t('salesAnalysis.setup.assignLine')}</option>
              {lines.filter((l) => l.isActive).map((l) => (
                <option key={l.id} value={String(l.id)}>{l.name}</option>
              ))}
            </select>
          </span>
        ) : (
          <Chip tone="warn">{t('salesAnalysis.setup.noLine')}</Chip>
        ),
      sortValue: (b) => (b.hasLine ? `1${b.line}` : '0'),
      exportValue: (b) => b.line ?? '',
    },
  ]
  const saveBrands = async () => {
    if (!brandsOf) return
    setBrandsError(null)
    try {
      await setBrands.mutateAsync({ id: brandsOf.line.id ?? 0, brands: brandsOf.value })
      setBrandsOf(null)
    } catch (err) {
      setBrandsError(applyProblemDetails(err).title)
    }
  }
  // marcas que puede tomar la línea: las sin línea y las suyas
  const brandOptions = brandsOf ? (brands.data ?? []).filter((b) => !b.hasLine || b.lineId === brandsOf.line.id || (brandsOf.line.brands ?? []).includes(b.brand ?? '')).map((b) => ({ value: b.brand ?? '', label: `${b.brand} (${b.productCount ?? 0})` })) : []
  const missing = (brands.data ?? []).filter((b) => !b.hasLine).length
  return (
    <>
      <Panel
        title={t('salesAnalysis.setup.sec.lines')}
        subtitle={t('salesAnalysis.setup.linesSub')}
        badge={lines.length}
        flush
        actions={canManage ? <button type="button" className="btn flow sm" onClick={() => setEditing({ name: '', sortOrder: null })}>{t('salesAnalysis.setup.newLine')}</button> : undefined}
      >
        {q.isError ? <div className="pb"><QueryError error={q.error} /></div> : (
          <DataTable
            label={t('salesAnalysis.setup.sec.lines')}
            columns={lineColumns}
            rows={lines}
            rowKey={(r) => String(r.id)}
            loading={q.isPending}
            pagination={false}
            rowClassName={(r) => (r.isActive ? undefined : 'dim')}
            empty={t('salesAnalysis.setup.noLines')}
            rowActions={[
              { key: 'edit', label: t('salesAnalysis.setup.edit'), icon: <IconEdit />, perm: SALES_ANALYSIS_MANAGE, onClick: (r) => setEditing({ id: r.id, name: r.name ?? '', sortOrder: r.sortOrder ?? null }) },
              { key: 'brands', label: t('salesAnalysis.setup.editBrands'), perm: SALES_ANALYSIS_MANAGE, visible: (r) => !!r.isActive, onClick: (r) => (setBrandsError(null), setBrandsOf({ line: r, value: [...(r.brands ?? [])] })) },
              { key: 'off', label: t('salesAnalysis.setup.deactivate'), icon: <IconPower />, tone: 'danger', perm: SALES_ANALYSIS_MANAGE, visible: (r) => !!r.isActive, onClick: confirm.ask },
              { key: 'on', label: t('salesAnalysis.setup.reactivate'), icon: <IconRotateCcw />, perm: SALES_ANALYSIS_MANAGE, visible: (r) => !r.isActive, onClick: confirm.ask },
            ]}
          />
        )}
      </Panel>
      <Panel title={t('salesAnalysis.setup.brandsTitle')} subtitle={missing > 0 ? t('salesAnalysis.setup.brandsMissing', { n: missing }) : t('salesAnalysis.setup.brandsSub')} badge={brandRows.length} flush>
        <div className="pb qrow">
          <QBox value={brandQ} onChange={setBrandQ} placeholder={t('salesAnalysis.setup.searchBrand')} />
        </div>
        {brands.isError ? <div className="pb"><QueryError error={brands.error} /></div> : (
          <DataTable label={t('salesAnalysis.setup.brandsTitle')} columns={brandColumns} rows={brandRows} rowKey={(b) => b.brand ?? ''} loading={brands.isPending} rowClassName={(b) => (b.hasLine ? undefined : 'sa-hl')} />
        )}
      </Panel>
      <NameSortModal title={editing?.id ? t('salesAnalysis.setup.editLine') : t('salesAnalysis.setup.newLine')} value={editing} onClose={() => setEditing(null)} onSave={(v) => save.mutateAsync({ id: v.id, name: v.name, sortOrder: v.sortOrder })} />
      <Modal
        open={brandsOf !== null}
        title={t('salesAnalysis.setup.brandsOf', { line: brandsOf?.line.name ?? '' })}
        onClose={() => setBrandsOf(null)}
        size="sm"
        footer={
          <>
            <button type="button" className="btn" onClick={() => setBrandsOf(null)}>{t('common.cancel')}</button>
            <button type="button" className="btn flow" onClick={() => void saveBrands()} disabled={setBrands.isPending}>{t('salesAnalysis.save')}</button>
          </>
        }
      >
        {brandsError && <p className="ferr" role="alert">{brandsError}</p>}
        <div className="f">
          <label htmlFor="sa-line-brands">{t('salesAnalysis.setup.brands')}</label>
          <SearchMultiSelect id="sa-line-brands" options={brandOptions} value={brandsOf?.value ?? []} onChange={(value) => brandsOf && setBrandsOf({ ...brandsOf, value })} />
          <span className="sa-muted">{t('salesAnalysis.setup.brandsHelp')}</span>
        </div>
      </Modal>
      <ConfirmDialog
        open={confirm.row !== null}
        title={t(confirm.row?.isActive ? 'salesAnalysis.setup.deactivateTitle' : 'salesAnalysis.setup.reactivateTitle', { name: confirm.row?.name ?? '' })}
        message={t(confirm.row?.isActive ? 'salesAnalysis.setup.deactivateLineMsg' : 'salesAnalysis.setup.reactivateMsg')}
        tone={confirm.row?.isActive ? 'danger' : 'flow'}
        onConfirm={confirm.run}
        onClose={confirm.close}
      />
    </>
  )
}

// ---------------------------------------------------------------- clientes ----------------------------------------------------------------

function ClientsSection() {
  const t = useT()
  const canManage = useCan(SALES_ANALYSIS_MANAGE)
  const reps = useSalesReps()
  const assign = useAssignClientsRep()
  const [scope, setScope] = useState<'unassigned' | 'all' | string>('unassigned')
  const [text, setText] = useState('')
  const [q, setQ] = useState('')
  useEffect(() => {
    const id = setTimeout(() => setQ(text), 300)
    return () => clearTimeout(id)
  }, [text])
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(25)
  const query: ClientRepQuery = { unassigned: scope === 'unassigned', repId: scope !== 'unassigned' && scope !== 'all' ? Number(scope) : null, q }
  const list = useSalesClients(query, page, pageSize)
  useEffect(() => setPage(1), [scope, q])
  const [selected, setSelected] = useState<Set<number>>(new Set())
  useEffect(() => setSelected(new Set()), [scope, q])
  const [target, setTarget] = useState('')
  const items = list.data?.items ?? NO_ROWS
  const allOnPage = items.length > 0 && items.every((c: SalesClientRep) => selected.has(c.clientId ?? 0))
  const toggle = (id: number) => setSelected((s) => {
    const n = new Set(s)
    if (n.has(id)) n.delete(id)
    else n.add(id)
    return n
  })
  const columns: DataColumn<SalesClientRep>[] = [
    ...(canManage
      ? [{
          id: 'pick',
          header: t('salesAnalysis.setup.pick'),
          exportable: false,
          cell: (c: SalesClientRep) => <input type="checkbox" aria-label={t('salesAnalysis.setup.pickClient', { name: c.name ?? '' })} checked={selected.has(c.clientId ?? 0)} onChange={() => toggle(c.clientId ?? 0)} />,
        } satisfies DataColumn<SalesClientRep>]
      : []),
    { id: 'code', header: t('salesAnalysis.setup.code'), cell: (c) => <span className="mono">{c.code ?? ''}</span>, sortValue: (c) => c.code ?? '', exportValue: (c) => c.code ?? '' },
    { id: 'name', header: t('salesAnalysis.col.client'), cell: (c) => c.name ?? '', sortValue: (c) => c.name ?? '', card: 'title' },
    { id: 'rep', header: t('salesAnalysis.col.rep'), cell: (c) => c.repName || <Chip tone="warn">{t('salesAnalysis.noRep')}</Chip>, sortValue: (c) => c.repName ?? '', exportValue: (c) => c.repName ?? '' },
    { id: 'route', header: t('salesAnalysis.col.route'), cell: (c) => c.routeName || '—', sortValue: (c) => c.routeName ?? '' },
  ]
  const doAssign = async () => {
    try {
      const r = await assign.mutateAsync({ clientIds: [...selected], repId: target === 'none' ? null : Number(target) })
      toast.success(t('salesAnalysis.setup.assigned', { n: r.updated ?? 0, rep: r.repName ?? t('salesAnalysis.noRep') }))
      setSelected(new Set())
    } catch (err) {
      toast.error(applyProblemDetails(err).title)
    }
  }
  const activeReps = (reps.data ?? []).filter((r) => r.isActive)
  return (
    <Panel title={t('salesAnalysis.setup.sec.clients')} subtitle={t('salesAnalysis.setup.clientsSub')} badge={list.data?.total} flush>
      <div className="pb">
        <Filters>
          <SelectFilter
            label={t('salesAnalysis.setup.show')}
            value={scope}
            allLabel={null}
            onChange={setScope}
            options={[{ value: 'unassigned', label: t('salesAnalysis.setup.showUnassigned') }, { value: 'all', label: t('salesAnalysis.setup.showAll') }, ...activeReps.map((r) => ({ value: String(r.id), label: t('salesAnalysis.setup.showOf', { rep: r.name ?? '' }) }))]}
          />
        </Filters>
        <div className="qrow">
          <QBox value={text} onChange={setText} placeholder={t('salesAnalysis.setup.searchClient')} />
        </div>
        {canManage && (
          <div className="sa-bulk" role="group" aria-label={t('salesAnalysis.setup.bulk')}>
            <label className="sa-check">
              <input type="checkbox" checked={allOnPage} onChange={() => setSelected((s) => {
                const n = new Set(s)
                for (const c of items) {
                  if (allOnPage) n.delete(c.clientId ?? 0)
                  else n.add(c.clientId ?? 0)
                }
                return n
              })} /> {t('salesAnalysis.setup.pickPage')}
            </label>
            <span>{t('salesAnalysis.setup.selected', { n: selected.size })}</span>
            <label htmlFor="sa-assign-rep" className="sr-only">{t('salesAnalysis.setup.assignTo')}</label>
            <select id="sa-assign-rep" value={target} onChange={(e) => setTarget(e.target.value)}>
              <option value="">{t('salesAnalysis.setup.assignTo')}</option>
              {activeReps.map((r) => (
                <option key={r.id} value={String(r.id)}>{r.name}{r.routeName ? ` · ${r.routeName}` : ''}</option>
              ))}
              <option value="none">{t('salesAnalysis.setup.removeRep')}</option>
            </select>
            <button type="button" className="btn flow sm" disabled={selected.size === 0 || !target || assign.isPending} onClick={() => void doAssign()}>
              {t('salesAnalysis.setup.assign')}
            </button>
          </div>
        )}
      </div>
      {list.isError ? <div className="pb"><QueryError error={list.error} /></div> : (
        <DataTable
          label={t('salesAnalysis.setup.sec.clients')}
          columns={columns}
          rows={items}
          rowKey={(c) => String(c.clientId)}
          loading={list.isPending}
          page={page}
          pageSize={pageSize}
          total={list.data?.total ?? 0}
          onPage={setPage}
          onPageSize={(n) => (setPageSize(n), setPage(1))}
          exportRows={() => fetchAllPages((skip, take) => fetchSalesClients(query, skip, take).then((r) => ({ items: r.items ?? [], total: r.total ?? 0 })))}
          empty={scope === 'unassigned' ? t('salesAnalysis.setup.allAssigned') : t('salesAnalysis.setup.noClients')}
        />
      )}
    </Panel>
  )
}

// ---------------------------------------------------------------- ajustes ----------------------------------------------------------------

function SettingsSection() {
  const t = useT()
  const canManage = useCan(SALES_ANALYSIS_MANAGE)
  const q = useSalesSettings()
  const save = useSaveSettings()
  const [cutoff, setCutoff] = useState('')
  const [lead, setLead] = useState('')
  const [z, setZ] = useState('')
  const [error, setError] = useState<string | null>(null)
  useEffect(() => {
    if (!q.data) return
    setCutoff(q.data.cutoffDate ?? '')
    setLead(String(q.data.leadTimeDays ?? 5))
    setZ(String(q.data.serviceLevelZ ?? 1.645))
  }, [q.data])
  const submit = async (e: FormEvent) => {
    e.preventDefault()
    setError(null)
    try {
      await save.mutateAsync({ cutoffDate: cutoff || null, clearCutoff: !cutoff, leadTimeDays: lead === '' ? null : Number(lead), serviceLevelZ: z === '' ? null : Number(z) })
      toast.success(t('salesAnalysis.setup.settingsSaved'))
    } catch (err) {
      setError(applyProblemDetails(err).title)
    }
  }
  const known = SERVICE_LEVELS.find((s) => Math.abs(s.z - Number(z)) < 0.0005)
  return (
    <Panel title={t('salesAnalysis.setup.sec.settings')} subtitle={t('salesAnalysis.setup.settingsSub')}>
      {q.isError ? <QueryError error={q.error} /> : !q.data ? <Spinner block /> : (
        <form onSubmit={(e) => void submit(e)} className="sa-settings">
          {error && <p className="ferr" role="alert">{error}</p>}
          <fieldset disabled={!canManage}>
            <div className="f">
              <label htmlFor="sa-set-cutoff">{t('salesAnalysis.setup.cutoff')}</label>
              <div className="sa-inline">
                <input id="sa-set-cutoff" type="date" value={cutoff} onChange={(e) => setCutoff(e.target.value)} aria-describedby="sa-set-cutoff-help" />
                {cutoff && <button type="button" className="btn sm" onClick={() => setCutoff('')}>{t('salesAnalysis.setup.clearCutoff')}</button>}
              </div>
              <p id="sa-set-cutoff-help" className="sa-help">{t('salesAnalysis.setup.cutoffHelp')}</p>
              {!cutoff && <p className="sa-help warn">{t('salesAnalysis.setup.noCutoffWarn')}</p>}
            </div>
            <div className="r2">
              <div className="f">
                <label htmlFor="sa-set-lead">{t('salesAnalysis.setup.leadTime')}</label>
                <input id="sa-set-lead" type="number" min={1} max={365} inputMode="numeric" value={lead} onChange={(e) => setLead(e.target.value)} aria-describedby="sa-set-lead-help" />
                <p id="sa-set-lead-help" className="sa-help">{t('salesAnalysis.setup.leadTimeHelp')}</p>
              </div>
              <div className="f">
                <label htmlFor="sa-set-z">{t('salesAnalysis.setup.serviceLevel')}</label>
                <select id="sa-set-z" value={known ? String(known.z) : 'custom'} onChange={(e) => e.target.value !== 'custom' && setZ(e.target.value)}>
                  {SERVICE_LEVELS.map((s) => (
                    <option key={s.z} value={String(s.z)}>{t('salesAnalysis.setup.serviceLevelOption', { pct: s.pct * 100, z: s.z })}</option>
                  ))}
                  <option value="custom">{t('salesAnalysis.setup.serviceLevelCustom')}</option>
                </select>
                <label htmlFor="sa-set-zv" className="sa-sub-label">{t('salesAnalysis.setup.zValue')}</label>
                <input id="sa-set-zv" type="number" step="0.001" min={0.001} max={4} inputMode="decimal" value={z} onChange={(e) => setZ(e.target.value)} aria-describedby="sa-set-z-help" />
                <p id="sa-set-z-help" className="sa-help">{t('salesAnalysis.setup.serviceLevelHelp')}</p>
              </div>
            </div>
          </fieldset>
          {canManage && (
            <div className="sa-actions-row">
              <button type="submit" className="btn flow" disabled={save.isPending}>{save.isPending ? t('salesAnalysis.saving') : t('salesAnalysis.save')}</button>
            </div>
          )}
        </form>
      )}
    </Panel>
  )
}

// ---------------------------------------------------------------- emparejar a mano ----------------------------------------------------------------

type LinkKind = 'CLIENT' | 'PRODUCT'

function LinkSection() {
  const t = useT()
  const status = useSalesStatus()
  const [linking, setLinking] = useState<{ kind: LinkKind; raw: string } | null>(null)
  const s = status.data
  const btn = (kind: LinkKind) => (r: SalesUnmatched) => (
    <button type="button" className="btn sm" onClick={() => setLinking({ kind, raw: r.raw ?? '' })} aria-label={t('salesAnalysis.link.linkOf', { raw: r.raw ?? '' })}>
      {t('salesAnalysis.link.link')}
    </button>
  )
  return (
    <Panel title={t('salesAnalysis.setup.sec.link')} subtitle={t('salesAnalysis.link.sub')}>
      {status.isError ? <QueryError error={status.error} /> : !s ? <Spinner block /> : (
        <>
          <UnmatchedList title={t('salesAnalysis.link.clients', { n: s.unmatchedClients?.length ?? 0 })} rows={s.unmatchedClients ?? NO_ROWS} action={btn('CLIENT')} />
          <UnmatchedList title={t('salesAnalysis.link.items', { n: s.unmatchedItems?.length ?? 0 })} rows={s.unmatchedItems ?? NO_ROWS} action={btn('PRODUCT')} />
          <UnmatchedList title={t('salesAnalysis.link.purchaseItems', { n: s.unmatchedPurchaseItems?.length ?? 0 })} rows={s.unmatchedPurchaseItems ?? NO_ROWS} action={btn('PRODUCT')} />
        </>
      )}
      <LinkModal value={linking} onClose={() => setLinking(null)} />
    </Panel>
  )
}

function LinkModal({ value, onClose }: { value: { kind: LinkKind; raw: string } | null; onClose: () => void }) {
  const t = useT()
  const [text, setText] = useState('')
  const [q, setQ] = useState('')
  const [picked, setPicked] = useState<{ id: number; label: string } | null>(null)
  const [error, setError] = useState<string | null>(null)
  const link = useLinkUnmatched()
  useEffect(() => {
    setText(value?.raw ?? '')
    setQ(value?.raw ?? '')
    setPicked(null)
    setError(null)
  }, [value])
  useEffect(() => {
    const id = setTimeout(() => setQ(text), 300)
    return () => clearTimeout(id)
  }, [text])
  const isClient = value?.kind === 'CLIENT'
  const clients = useSalesClients({ unassigned: false, repId: null, q }, 1, 20, !!value && isClient && q.trim().length > 0)
  const products = useProductSearch(q, !!value && !isClient && q.trim().length > 0)
  const options: { id: number; label: string; hint?: string }[] = isClient
    ? (clients.data?.items ?? []).map((c) => ({ id: c.clientId ?? 0, label: c.name ?? '', hint: c.code ?? undefined }))
    : (products.data?.items ?? []).map((p) => ({ id: p.id ?? 0, label: p.name ?? '', hint: p.sku ?? undefined }))
  const loading = isClient ? clients.isFetching : products.isFetching
  const submit = async () => {
    if (!value || !picked) return
    setError(null)
    try {
      const r = await link.mutateAsync(isClient ? { kind: 'CLIENT', raw: value.raw, clientId: picked.id } : { kind: 'PRODUCT', raw: value.raw, productId: picked.id })
      toast.success(t('salesAnalysis.link.done', { raw: value.raw, target: picked.label, n: r.relinked ?? 0 }))
      onClose()
    } catch (err) {
      setError(applyProblemDetails(err).title)
    }
  }
  return (
    <Modal
      open={value !== null}
      title={t(isClient ? 'salesAnalysis.link.titleClient' : 'salesAnalysis.link.titleProduct')}
      onClose={onClose}
      size="md"
      footer={
        <>
          <button type="button" className="btn" onClick={onClose}>{t('common.cancel')}</button>
          <button type="button" className="btn flow" disabled={!picked || link.isPending} onClick={() => void submit()}>{t('salesAnalysis.link.link')}</button>
        </>
      }
    >
      <p>{t('salesAnalysis.link.raw')}: <strong>{value?.raw}</strong></p>
      {error && <p className="ferr" role="alert">{error}</p>}
      <div className="f">
        <label htmlFor="sa-link-q">{t(isClient ? 'salesAnalysis.link.searchClient' : 'salesAnalysis.link.searchProduct')}</label>
        <input id="sa-link-q" type="search" value={text} onChange={(e) => setText(e.target.value)} autoComplete="off" />
      </div>
      <div className="sa-picklist" role="listbox" aria-label={t('salesAnalysis.link.results')}>
        {loading && options.length === 0 ? <Spinner /> : options.length === 0 ? <p className="sa-muted">{t('salesAnalysis.link.noResults')}</p> : options.map((o) => (
          <button key={o.id} type="button" role="option" aria-selected={picked?.id === o.id} className={`unrow${picked?.id === o.id ? ' on' : ''}`} onClick={() => setPicked({ id: o.id, label: o.label })}>
            <span>{o.label}</span>
            {o.hint && <span className="meta mono"> · {o.hint}</span>}
          </button>
        ))}
      </div>
    </Modal>
  )
}
