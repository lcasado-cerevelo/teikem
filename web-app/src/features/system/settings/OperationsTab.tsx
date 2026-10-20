// Ajustes → Operación (maqueta: `tenantDefaultsPanelHtml` + `tenantPipelinePanelHtml` + `tenantRecvSummaryHtml`):
// - Valores por defecto: tipo de servicio y de paquete (catálogo; '' = sin valor) y máximo de paradas por ruta (≥ 1).
// - Pipeline de estatus de órdenes: matriz estatus × acción (`StatusCapability` de TRANSPORT_ORDER / OrderStatus); se lee
//   con sesión y se cambia con `admin.statusconfig` (una celda por clic). Solo con el módulo de órdenes (LTL_GROUND).
// - Productos nuevos (módulo WMS_LOTSERIAL, 2026-10-11 c): categoría por defecto que la pantalla de Productos preselecciona al crear.
// - Despacho manual (módulo WMS_LOTSERIAL, 2026-10-11 b): motivo por default (preseleccionado al despachar; sigue obligatorio).
// - Conteo cíclico (módulo WMS_LOTSERIAL, tarea 25): quién ve lo esperado al contar, margen de reconteo y si se muestra el número.
// - Recepción por almacén (solo lectura, módulo WMS_LOTSERIAL): modo, posición de recepción, recibos abiertos y acomodos
//   pendientes, aviso rojo si es con acomodo y no tiene posición de recepción, y "Abrir almacén" (ficha del almacén).
import { useMemo } from 'react'
import { useForm } from 'react-hook-form'
import { useNavigate } from 'react-router-dom'
import { ModuleKeys, useCan, useModule } from '../../../kernel/access'
import { isAccessDenied } from '../../warehouse/accessDenied'
import { useLookups, useStatuses, type StatusOption } from '../../../kernel/catalogs'
import { useT } from '../../../kernel/i18n'
import {
  Chip,
  DataTable,
  Field,
  Form,
  IconCheckin,
  IconGear,
  IconRoute,
  IconWarehouse,
  NumberInput,
  Panel,
  Select,
  Toggle,
  toast,
  type DataColumn,
  type RowAction,
} from '../../../kernel/ui'
import { useProductCategories, useReceiptSummaryByWarehouse, useWarehouses } from '../../warehouse/api'
import { problemText } from '../../warehouse/problemText'
import { useCapabilities, useSaveTenantSettings, useSetCapabilities, type TenantSettingsDto } from '../tenantSettingsApi'
import { capabilityAllowed, ORDER_CAPABILITIES, recvSummaryRows, type RecvRow } from './operations'

// ------------------------------------------------------------------ valores por defecto

interface DefaultsValues {
  defaultServiceType: string
  defaultPackageType: string
  maxStopsPerRouteDefault: number | null
}

function DefaultsPanel({ settings, canEdit }: { settings: TenantSettingsDto; canEdit: boolean }) {
  const t = useT()
  const save = useSaveTenantSettings()
  const { data: services = [] } = useLookups('ServiceType', { includeDisabled: true })
  const { data: packages = [] } = useLookups('PackageType', { includeDisabled: true })
  const form = useForm<DefaultsValues>({
    values: {
      defaultServiceType: settings.defaultServiceType ?? '',
      defaultPackageType: settings.defaultPackageType ?? '',
      maxStopsPerRouteDefault: settings.maxStopsPerRouteDefault ?? 1,
    },
  })
  const dirty = form.formState.isDirty
  const withCurrent = (opts: { code: string; label: string }[], current: string) =>
    (current && !opts.some((o) => o.code.toUpperCase() === current.toUpperCase()) ? [...opts, { code: current, label: current }] : opts).map((o) => ({
      value: o.code,
      label: o.label,
    }))

  return (
    <Panel icon={<IconGear />} title={t('system.settings.ops.defaultsTitle')}>
      <Form
        form={form}
        onSubmit={async (v) => {
          const n = v.maxStopsPerRouteDefault
          if (n == null || !Number.isInteger(n) || n < 1) {
            form.setError('maxStopsPerRouteDefault', { message: t('system.settings.ops.maxStopsMin') })
            return
          }
          // '' = quitar el valor por defecto (el API lo entiende así)
          await save.mutateAsync({ defaultServiceType: v.defaultServiceType, defaultPackageType: v.defaultPackageType, maxStopsPerRouteDefault: n })
          toast.success(t('system.settings.saved'))
        }}
      >
        <fieldset className="set-fs" disabled={!canEdit}>
          <div className="r2">
            <Field name="defaultServiceType" label={t('system.settings.ops.svcDefaultLabel')}>
              <Select options={withCurrent(services, settings.defaultServiceType ?? '')} placeholder={t('system.settings.ops.noDefault')} />
            </Field>
            <Field name="defaultPackageType" label={t('system.settings.ops.pkgDefaultLabel')}>
              <Select options={withCurrent(packages, settings.defaultPackageType ?? '')} placeholder={t('system.settings.ops.noDefault')} />
            </Field>
          </div>
          <p className="set-d" style={{ marginBottom: 16 }}>
            {t('system.settings.ops.defaultsHint')}
          </p>
          <Field name="maxStopsPerRouteDefault" label={t('system.settings.ops.maxStopsLabel')} help={t('system.settings.ops.maxStopsHint')}>
            <NumberInput min={1} step={1} className="mono" />
          </Field>
        </fieldset>
        {canEdit && (
          <div className="set-actions">
            <button type="button" className="btn" disabled={!dirty || form.formState.isSubmitting} onClick={() => form.reset()}>
              {t('system.settings.discard')}
            </button>
            <button type="submit" className="btn flow" disabled={!dirty || form.formState.isSubmitting}>
              {form.formState.isSubmitting ? t('system.settings.saving') : t('system.settings.save')}
            </button>
          </div>
        )}
      </Form>
    </Panel>
  )
}

// ------------------------------------------------------------------ despacho manual: motivo por default (2026-10-11 b)

/**
 * «Motivo por default del despacho manual» (`defaultManualIssueReason`, admin.tenant): llega preseleccionado al despachar en
 * Recolección y empaque y en la app; el motivo sigue siendo obligatorio. '' = sin default. Los motivos son los activos del
 * catálogo `ManualIssueReason`; el 400 del servidor ("El motivo X no existe o está inactivo.") sale bajo el selector.
 */
function ManualReasonPanel({ settings, canEdit }: { settings: TenantSettingsDto; canEdit: boolean }) {
  const t = useT()
  const save = useSaveTenantSettings()
  const { data: reasons = [] } = useLookups('ManualIssueReason')
  const current = settings.defaultManualIssueReason ?? ''
  const form = useForm<{ defaultManualIssueReason: string }>({ values: { defaultManualIssueReason: current } })
  const dirty = form.formState.isDirty
  // un default guardado que ya no está entre los activos se sigue mostrando (con su código) para poder cambiarlo o quitarlo
  const options = (current && !reasons.some((r) => r.code.toUpperCase() === current.toUpperCase()) ? [...reasons, { code: current, label: current }] : reasons).map((r) => ({
    value: r.code,
    label: r.label,
  }))

  return (
    <Panel className="set-gap" icon={<IconGear />} title={t('system.settings.ops.manualReasonTitle')}>
      <Form
        form={form}
        onSubmit={async (v) => {
          // '' = quitar el default (el API lo entiende así)
          await save.mutateAsync({ defaultManualIssueReason: v.defaultManualIssueReason })
          toast.success(t('system.settings.saved'))
        }}
      >
        <fieldset className="set-fs" disabled={!canEdit}>
          <Field name="defaultManualIssueReason" label={t('system.settings.ops.manualReasonLabel')} help={t('system.settings.ops.manualReasonHelp')}>
            <Select options={options} placeholder={t('system.settings.ops.manualReasonNone')} />
          </Field>
        </fieldset>
        {canEdit && (
          <div className="set-actions">
            <button type="button" className="btn" disabled={!dirty || form.formState.isSubmitting} onClick={() => form.reset()}>
              {t('system.settings.discard')}
            </button>
            <button type="submit" className="btn flow" disabled={!dirty || form.formState.isSubmitting}>
              {form.formState.isSubmitting ? t('system.settings.saving') : t('system.settings.save')}
            </button>
          </div>
        )}
      </Form>
    </Panel>
  )
}

// ------------------------------------------------------------------ productos nuevos: categoría por defecto (2026-10-11 c)

/**
 * «Categoría por defecto de los productos nuevos» (`defaultProductCategoryId`, admin.tenant): la pantalla de Productos la preselecciona
 * al crear un producto (se puede cambiar o quitar ahí); la API no la aplica sola. '' = sin default (se manda 0 = quitar). Solo salen las
 * categorías activas; el 400 del servidor ("La categoría por defecto no existe o está inactiva.") sale bajo el selector.
 */
function ProductCategoryPanel({ settings, canEdit }: { settings: TenantSettingsDto; canEdit: boolean }) {
  const t = useT()
  const save = useSaveTenantSettings()
  const { data: categories = [] } = useProductCategories()
  const current = settings.defaultProductCategoryId != null ? String(settings.defaultProductCategoryId) : ''
  const form = useForm<{ defaultProductCategoryId: string }>({ values: { defaultProductCategoryId: current } })
  const dirty = form.formState.isDirty
  // la ruta completa ("Raíz / Hija") como etiqueta, igual que en la ficha del producto
  const options = categories.filter((c) => c.isActive).map((c) => ({ value: String(c.id), label: c.path || c.name || '' }))

  return (
    <Panel className="set-gap" icon={<IconGear />} title={t('system.settings.ops.productCategoryTitle')}>
      <Form
        form={form}
        onSubmit={async (v) => {
          await save.mutateAsync({ defaultProductCategoryId: v.defaultProductCategoryId === '' ? 0 : Number(v.defaultProductCategoryId) })
          toast.success(t('system.settings.saved'))
        }}
      >
        <fieldset className="set-fs" disabled={!canEdit}>
          <Field name="defaultProductCategoryId" label={t('system.settings.ops.productCategoryLabel')} help={t('system.settings.ops.productCategoryHelp')}>
            <Select options={options} placeholder={t('system.settings.ops.productCategoryNone')} />
          </Field>
        </fieldset>
        {canEdit && (
          <div className="set-actions">
            <button type="button" className="btn" disabled={!dirty || form.formState.isSubmitting} onClick={() => form.reset()}>
              {t('system.settings.discard')}
            </button>
            <button type="submit" className="btn flow" disabled={!dirty || form.formState.isSubmitting}>
              {form.formState.isSubmitting ? t('system.settings.saving') : t('system.settings.save')}
            </button>
          </div>
        )}
      </Form>
    </Panel>
  )
}

// ------------------------------------------------------------------ rotación del inventario: FEFO / FIFO (2026-10-11, Lote 34)

/**
 * «Método de rotación de la compañía» (`rotationMethod`, admin.tenant): FEFO (vence primero, el de siempre; todas las compañías existentes) o FIFO (entra primero). Cada producto puede
 * tener su propia excepción en su ficha. El 400 del servidor («El método de rotación debe ser FEFO o FIFO.») sale bajo el selector.
 */
function RotationPanel({ settings, canEdit }: { settings: TenantSettingsDto; canEdit: boolean }) {
  const t = useT()
  const save = useSaveTenantSettings()
  const current = settings.rotationMethod ?? 'FEFO'
  const form = useForm<{ rotationMethod: string; rotationMethodNoLot: string; rotationEnforcement: string; dispatchRequirement: string; valuationMethod: string }>({
    values: { rotationMethod: current, rotationMethodNoLot: settings.rotationMethodNoLot ?? 'FIFO', rotationEnforcement: settings.rotationEnforcement ?? 'SUGGEST', dispatchRequirement: settings.dispatchRequirement ?? 'FREE', valuationMethod: settings.valuationMethod ?? 'AVERAGE' },
  })
  const dirty = form.formState.isDirty
  // 2026-10-20: dos métodos — con lote (FEFO o FIFO) y sin lote ni series (FIFO o por posición; no vencen)
  const options = [
    { value: 'FEFO', label: t('system.settings.ops.rotationFefo') },
    { value: 'FIFO', label: t('system.settings.ops.rotationFifo') },
  ]
  const noLotOptions = [
    { value: 'FIFO', label: t('system.settings.ops.rotationFifo') },
    { value: 'POSITION', label: t('system.settings.ops.rotationPosition') },
  ]
  // 2026-10-20: cómo se aplica en las salidas: sugerir (se propone y se puede cambiar, con advertencia) u obligar (el sistema escoge solo)
  const enforcementOptions = [
    { value: 'SUGGEST', label: t('system.settings.ops.rotationSuggest') },
    { value: 'ENFORCE', label: t('system.settings.ops.rotationEnforce') },
  ]

  // 2026-10-20 (Lote 145): el despacho exige documento
  const requirementOptions = ['FREE', 'ANY_DOCUMENT', 'EXIT_NOTICE', 'SALES_ORDER'].map((value) => ({ value, label: t(`system.settings.ops.requirement.${value}`) }))

  // 2026-10-20 (Lote 148): método de valuación del inventario
  const valuationOptions = ['AVERAGE', 'LAYERS'].map((value) => ({ value, label: t(`system.settings.ops.valuation.${value}`) }))

  return (
    <Panel className="set-gap" icon={<IconGear />} title={t('system.settings.ops.rotationTitle')}>
      <Form
        form={form}
        onSubmit={async (v) => {
          await save.mutateAsync({ rotationMethod: v.rotationMethod, rotationMethodNoLot: v.rotationMethodNoLot, rotationEnforcement: v.rotationEnforcement, dispatchRequirement: v.dispatchRequirement, valuationMethod: v.valuationMethod })
          toast.success(t('system.settings.saved'))
        }}
      >
        <fieldset className="set-fs" disabled={!canEdit}>
          <Field name="rotationMethod" label={t('system.settings.ops.rotationLabel')} help={t('system.settings.ops.rotationHelp')}>
            <Select options={options} />
          </Field>
          <Field name="rotationMethodNoLot" label={t('system.settings.ops.rotationNoLotLabel')} help={t('system.settings.ops.rotationNoLotHelp')}>
            <Select options={noLotOptions} />
          </Field>
          <Field name="rotationEnforcement" label={t('system.settings.ops.rotationEnforcementLabel')} help={t('system.settings.ops.rotationEnforcementHelp')}>
            <Select options={enforcementOptions} />
          </Field>
          <Field name="dispatchRequirement" label={t('system.settings.ops.requirementLabel')} help={t('system.settings.ops.requirementHelp')}>
            <Select options={requirementOptions} />
          </Field>
          <Field name="valuationMethod" label={t('system.settings.ops.valuationLabel')} help={t('system.settings.ops.valuationHelp')}>
            <Select options={valuationOptions} />
          </Field>
        </fieldset>
        {canEdit && (
          <div className="set-actions">
            <button type="button" className="btn" disabled={!dirty || form.formState.isSubmitting} onClick={() => form.reset()}>
              {t('system.settings.discard')}
            </button>
            <button type="submit" className="btn flow" disabled={!dirty || form.formState.isSubmitting}>
              {form.formState.isSubmitting ? t('system.settings.saving') : t('system.settings.save')}
            </button>
          </div>
        )}
      </Form>
    </Panel>
  )
}

// ------------------------------------------------------------------ avisos de inventario: umbrales (2026-10-20, Lote 141)

interface InventoryAlertValues {
  inventoryExpiryWarnDays: number | null
  inventoryNoMovementDays: number | null
}

/**
 * «Avisos de inventario»: con cuántos días de anticipación avisa «Lotes por vencer» (1 a 365) y a partir de cuántos días sin movimientos avisa «Productos sin movimiento» (1 a 3650).
 * A quién le llega cada aviso se arma en Sistema → Reglas de aviso (eventos de inventario). Los 400 del servidor salen bajo su campo.
 */
function InventoryAlertsPanel({ settings, canEdit }: { settings: TenantSettingsDto; canEdit: boolean }) {
  const t = useT()
  const save = useSaveTenantSettings()
  const form = useForm<InventoryAlertValues>({
    values: { inventoryExpiryWarnDays: settings.inventoryExpiryWarnDays ?? 30, inventoryNoMovementDays: settings.inventoryNoMovementDays ?? 90 },
  })
  const dirty = form.formState.isDirty
  return (
    <Panel className="set-gap" icon={<IconGear />} title={t('system.settings.ops.invAlertsTitle')}>
      <Form
        form={form}
        onSubmit={async (v) => {
          const expiry = v.inventoryExpiryWarnDays
          const idle = v.inventoryNoMovementDays
          if (expiry == null || !Number.isInteger(expiry) || expiry < 1 || expiry > 365) {
            form.setError('inventoryExpiryWarnDays', { message: t('system.settings.ops.invExpiryRange') })
            return
          }
          if (idle == null || !Number.isInteger(idle) || idle < 1 || idle > 3650) {
            form.setError('inventoryNoMovementDays', { message: t('system.settings.ops.invIdleRange') })
            return
          }
          await save.mutateAsync({ inventoryExpiryWarnDays: expiry, inventoryNoMovementDays: idle })
          toast.success(t('system.settings.saved'))
        }}
      >
        <fieldset className="set-fs" disabled={!canEdit}>
          <div className="r2">
            <Field name="inventoryExpiryWarnDays" label={t('system.settings.ops.invExpiryLabel')} help={t('system.settings.ops.invExpiryHelp')}>
              <NumberInput min={1} max={365} step={1} className="mono" />
            </Field>
            <Field name="inventoryNoMovementDays" label={t('system.settings.ops.invIdleLabel')} help={t('system.settings.ops.invIdleHelp')}>
              <NumberInput min={1} max={3650} step={1} className="mono" />
            </Field>
          </div>
          <p className="set-d">{t('system.settings.ops.invAlertsHint')}</p>
        </fieldset>
        {canEdit && (
          <div className="set-actions">
            <button type="button" className="btn" disabled={!dirty || form.formState.isSubmitting} onClick={() => form.reset()}>
              {t('system.settings.discard')}
            </button>
            <button type="submit" className="btn flow" disabled={!dirty || form.formState.isSubmitting}>
              {form.formState.isSubmitting ? t('system.settings.saving') : t('system.settings.save')}
            </button>
          </div>
        )}
      </Form>
    </Panel>
  )
}

// ------------------------------------------------------------------ conteo cíclico: lo esperado al contar (tarea 25)

interface CountRevealValues {
  countExpectedReveal: string
  countRecountTolerancePct: number | null
  countRevealShowsNumber: boolean
  countAutoCloseMatching: boolean
}

/** Quién ve lo esperado al contar (después de capturar cada línea), cuánto margen hay antes de pedir reconteo y si se muestra el número. */
function CountRevealPanel({ settings, canEdit }: { settings: TenantSettingsDto; canEdit: boolean }) {
  const t = useT()
  const save = useSaveTenantSettings()
  const form = useForm<CountRevealValues>({
    values: {
      countExpectedReveal: settings.countExpectedReveal ?? 'MARKED',
      countRecountTolerancePct: settings.countRecountTolerancePct ?? 0,
      countRevealShowsNumber: settings.countRevealShowsNumber ?? true,
      countAutoCloseMatching: settings.countAutoCloseMatching ?? false,
    },
  })
  const dirty = form.formState.isDirty
  const options = ['NONE', 'MARKED', 'ALL'].map((v) => ({ value: v, label: t(`system.settings.ops.countReveal.${v}`) }))
  const mode = form.watch('countExpectedReveal')

  return (
    <Panel className="set-gap" icon={<IconCheckin />} title={t('system.settings.ops.countRevealTitle')}>
      <Form
        form={form}
        onSubmit={async (v) => {
          const pct = v.countRecountTolerancePct
          if (pct == null || pct < 0 || pct > 100) {
            form.setError('countRecountTolerancePct', { message: t('system.settings.ops.countTolRange') })
            return
          }
          await save.mutateAsync({ countExpectedReveal: v.countExpectedReveal, countRecountTolerancePct: pct, countRevealShowsNumber: v.countRevealShowsNumber, countAutoCloseMatching: v.countAutoCloseMatching })
          toast.success(t('system.settings.saved'))
        }}
      >
        <fieldset className="set-fs" disabled={!canEdit}>
          <Field name="countExpectedReveal" label={t('system.settings.ops.countRevealLabel')} help={t(`system.settings.ops.countRevealHelp.${mode}`)}>
            <Select options={options} />
          </Field>
          <div className="r2">
            <Field name="countRecountTolerancePct" label={t('system.settings.ops.countTolLabel')} help={t('system.settings.ops.countTolHelp')}>
              <NumberInput min={0} max={100} step={0.01} className="mono" />
            </Field>
            <Field name="countRevealShowsNumber" label={t('system.settings.ops.countNumberLabel')} help={t('system.settings.ops.countNumberHelp')}>
              <Toggle text={t('system.settings.ops.countNumberText')} />
            </Field>
          </div>
          <Field name="countAutoCloseMatching" label={t('system.settings.ops.countAutoCloseLabel')} help={t('system.settings.ops.countAutoCloseHelp')}>
            <Toggle text={t('system.settings.ops.countAutoCloseText')} />
          </Field>
          <p className="set-d">{t('system.settings.ops.countRevealHint')}</p>
        </fieldset>
        {canEdit && (
          <div className="set-actions">
            <button type="button" className="btn" disabled={!dirty || form.formState.isSubmitting} onClick={() => form.reset()}>
              {t('system.settings.discard')}
            </button>
            <button type="submit" className="btn flow" disabled={!dirty || form.formState.isSubmitting}>
              {form.formState.isSubmitting ? t('system.settings.saving') : t('system.settings.save')}
            </button>
          </div>
        )}
      </Form>
    </Panel>
  )
}

// ------------------------------------------------------------------ pipeline de estatus

const ORDER_ENTITY = 'TRANSPORT_ORDER'
const ORDER_STATUS_DOMAIN = 'OrderStatus'

function PipelinePanel() {
  const t = useT()
  const canEdit = useCan('admin.statusconfig')
  const statuses = useStatuses(ORDER_STATUS_DOMAIN)
  const caps = useCapabilities(ORDER_ENTITY)
  const setCaps = useSetCapabilities(ORDER_ENTITY, ORDER_STATUS_DOMAIN)
  const rows = useMemo(() => (statuses.data ?? []).filter((s) => s.isEnabled), [statuses.data])

  const allowed = (status: string, cap: string) => capabilityAllowed(caps.data, status, cap)

  const columns = useMemo<DataColumn<StatusOption>[]>(
    () => [
      {
        id: 'stage',
        header: t('system.settings.ops.colStage'),
        card: 'title',
        sortValue: (s) => s.sortOrder,
        exportValue: (s) => s.label,
        cell: (s) => (
          <Chip color={s.color ?? undefined} tone="neutral">
            {s.label}
          </Chip>
        ),
      },
      ...ORDER_CAPABILITIES.map<DataColumn<StatusOption>>((cap) => ({
        id: cap,
        header: t(`system.settings.ops.caps.${cap}`),
        sortValue: (s) => (allowed(s.code, cap) ? 1 : 0),
        exportValue: (s) => (allowed(s.code, cap) ? '✓' : ''),
        cell: (s) => (
          <input
            type="checkbox"
            className="set-cap"
            checked={allowed(s.code, cap)}
            disabled={!canEdit || setCaps.isPending}
            aria-label={t('system.settings.ops.capToggle', { cap: t(`system.settings.ops.caps.${cap}`), stage: s.label })}
            onChange={(e) =>
              setCaps
                .mutateAsync([{ statusCode: s.code, capability: cap, isAllowed: e.target.checked }])
                .then(() => toast.success(t('system.settings.ops.capSaved')))
                .catch((err) => toast.error(problemText(err)))
            }
          />
        ),
      })),
    ],
    // `allowed` lee `caps.data`
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [t, caps.data, canEdit, setCaps.isPending],
  )

  const denied = isAccessDenied(caps.error) || isAccessDenied(statuses.error)
  return (
    <Panel flush icon={<IconRoute />} title={t('system.settings.ops.pipelineTitle')}>
      {denied ? (
        <p className="note" style={{ margin: 16 }}>
          {t('system.settings.ops.pipelineNoAccess')}
        </p>
      ) : (
        <DataTable
          columns={columns}
          rows={rows}
          rowKey={(s) => s.code}
          loading={statuses.isPending || caps.isPending}
          pagination={false}
          label={t('system.settings.ops.pipelineTitle')}
        />
      )}
      <p className="note" style={{ margin: 0, border: 'none', borderRadius: 0 }}>
        {t('system.settings.ops.pipelineHint')} {!canEdit && t('system.settings.ops.pipelineReadOnly')}
      </p>
    </Panel>
  )
}

// ------------------------------------------------------------------ recepción por almacén

function RecvSummaryPanel() {
  const t = useT()
  const navigate = useNavigate()
  const warehouses = useWarehouses({ includeInactive: true }, { handleAccessDenied: false })
  const list = useMemo(() => warehouses.data ?? [], [warehouses.data])
  // Una sola llamada para todos los almacenes (antes: dos consultas de lista por almacén). Sin permiso o con error, los conteos quedan en null (se muestran como «—»).
  const summary = useReceiptSummaryByWarehouse({ handleAccessDenied: false })
  const counts = useMemo(() => {
    const m = new Map<string, { open: number | null; pending: number | null }>()
    const byWarehouse = new Map((summary.data ?? []).map((s) => [s.warehousePublicId, s] as const))
    list.forEach((w) => {
      const s = byWarehouse.get(w.publicId ?? '')
      m.set(w.publicId ?? '', { open: s?.openCount ?? null, pending: s?.pendingPutawayCount ?? null })
    })
    return m
  }, [list, summary.data])
  const rows = useMemo(() => recvSummaryRows(list, counts), [list, counts])

  const columns = useMemo<DataColumn<RecvRow>[]>(
    () => [
      {
        id: 'name',
        header: t('system.settings.ops.recvSumWh'),
        card: 'title',
        sortValue: (r) => r.w.code,
        exportValue: (r) => `${r.w.code} · ${r.w.name}`,
        cell: (r) => (
          <span>
            <span className="ref">{r.w.code}</span> {r.w.name}
            {r.w.isActive === false && (
              <>
                {' '}
                <Chip tone="cap">{t('system.settings.ops.inactive')}</Chip>
              </>
            )}
          </span>
        ),
      },
      {
        id: 'mode',
        header: t('system.settings.ops.recvSumMode'),
        sortValue: (r) => (r.direct ? 1 : 0),
        exportValue: (r) => (r.direct ? t('system.settings.ops.recvSumDirect') : t('system.settings.ops.recvSumPutaway')),
        cell: (r) => <Chip tone={r.direct ? 'deliv' : 'wh'}>{r.direct ? t('system.settings.ops.recvSumDirect') : t('system.settings.ops.recvSumPutaway')}</Chip>,
      },
      {
        id: 'bin',
        header: t('system.settings.ops.recvSumBin'),
        sortValue: (r) => (r.direct ? '' : r.noBin ? '!' : r.w.defaultReceivingBinCode),
        exportValue: (r) => (r.direct ? '' : r.noBin ? t('system.settings.ops.recvSumNoBin') : r.w.defaultReceivingBinCode),
        cell: (r) =>
          r.direct ? '—' : r.noBin ? <Chip tone="fail">{t('system.settings.ops.recvSumNoBin')}</Chip> : <span className="ref">{r.w.defaultReceivingBinCode ?? r.w.defaultReceivingBinId}</span>,
      },
      { id: 'open', header: t('system.settings.ops.recvSumOpen'), align: 'end', sortValue: (r) => r.open, cell: (r) => (r.open == null ? '—' : r.open) },
      { id: 'pending', header: t('system.settings.ops.recvSumPending'), align: 'end', sortValue: (r) => r.pending, cell: (r) => (r.pending == null ? '—' : r.pending) },
    ],
    [t],
  )
  const rowActions = useMemo<RowAction<RecvRow>[]>(
    () => [
      {
        key: 'open',
        label: t('system.settings.ops.recvSumOpenWh'),
        icon: <IconWarehouse />,
        onClick: (r) => navigate(`/warehouse/warehouses/${r.w.publicId}`),
      },
    ],
    [t, navigate],
  )

  return (
    <Panel flush className="set-gap" icon={<IconCheckin />} title={t('system.settings.ops.recvSumTitle')} badge={warehouses.data ? rows.length : undefined}>
      {isAccessDenied(warehouses.error) ? (
        <p className="note" style={{ margin: 16 }}>
          {t('system.settings.ops.recvNoAccess')}
        </p>
      ) : (
        <DataTable
          columns={columns}
          rows={rows}
          rowKey={(r) => r.w.publicId ?? r.w.code ?? ''}
          defaultSort={{ id: 'name', desc: false }}
          rowActions={rowActions}
          onRowClick={(r) => navigate(`/warehouse/warehouses/${r.w.publicId}`)}
          loading={warehouses.isPending}
          empty={t('system.settings.ops.recvEmpty')}
          label={t('system.settings.ops.recvSumTitle')}
        />
      )}
      <p className="note" style={{ margin: 0, border: 'none', borderRadius: 0 }}>
        {t('system.settings.ops.recvSumHint')}
      </p>
    </Panel>
  )
}

export function OperationsTab({ settings, canEdit }: { settings: TenantSettingsDto; canEdit: boolean }) {
  const orders = useModule(ModuleKeys.LtlGround)
  const wms = useModule(ModuleKeys.WmsLotSerial)
  return (
    <>
      <div className="set-cols">
        <DefaultsPanel settings={settings} canEdit={canEdit} />
        {orders && <PipelinePanel />}
      </div>
      {wms && <ProductCategoryPanel settings={settings} canEdit={canEdit} />}
      {wms && <RotationPanel settings={settings} canEdit={canEdit} />}
      {wms && <InventoryAlertsPanel settings={settings} canEdit={canEdit} />}
      {wms && <ManualReasonPanel settings={settings} canEdit={canEdit} />}
      {wms && <CountRevealPanel settings={settings} canEdit={canEdit} />}
      {wms && <RecvSummaryPanel />}
    </>
  )
}
