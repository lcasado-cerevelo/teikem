// Ajustes → Operación (maqueta: `tenantDefaultsPanelHtml` + `tenantPipelinePanelHtml` + `tenantRecvSummaryHtml`):
// - Valores por defecto: tipo de servicio y de paquete (catálogo; '' = sin valor) y máximo de paradas por ruta (≥ 1).
// - Pipeline de estatus de órdenes: matriz estatus × acción (`StatusCapability` de TRANSPORT_ORDER / OrderStatus); se lee
//   con sesión y se cambia con `admin.statusconfig` (una celda por clic). Solo con el módulo de órdenes (LTL_GROUND).
// - Recepción por almacén (solo lectura, módulo WMS_LOTSERIAL): modo, posición de recepción, recibos abiertos y acomodos
//   pendientes, aviso rojo si es con acomodo y no tiene posición de recepción, y "Abrir almacén" (ficha del almacén).
import { useQueries } from '@tanstack/react-query'
import { useMemo } from 'react'
import { useForm } from 'react-hook-form'
import { useNavigate } from 'react-router-dom'
import { ModuleKeys, useCan, useModule } from '../../../kernel/access'
import { api, unwrap } from '../../../kernel/api/client'
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
  toast,
  type DataColumn,
  type RowAction,
} from '../../../kernel/ui'
import { useWarehouses, warehouseKeys } from '../../warehouse/api'
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
  const phases = ['OPEN', 'PENDING_PUTAWAY'] as const
  const countQueries = useQueries({
    queries: list.flatMap((w) =>
      phases.map((phase) => {
        const query = { warehousePublicId: w.publicId, phase, take: 1 }
        return {
          queryKey: [warehouseKeys.receipts[0], query],
          queryFn: () => unwrap(api.GET('/api/v1/receipts', { params: { query } })),
          meta: { handleAccessDenied: false },
        }
      }),
    ),
  })
  const counts = useMemo(() => {
    const m = new Map<string, { open: number | null; pending: number | null }>()
    list.forEach((w, i) => {
      const open = countQueries[i * 2]?.data?.total
      const pending = countQueries[i * 2 + 1]?.data?.total
      m.set(w.publicId ?? '', { open: open ?? null, pending: pending ?? null })
    })
    return m
  }, [list, countQueries])
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
      {wms && <RecvSummaryPanel />}
    </>
  )
}
