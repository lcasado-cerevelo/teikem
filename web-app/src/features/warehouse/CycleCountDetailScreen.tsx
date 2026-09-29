// Pieza "Conteo cíclico" (Lote F6) — ficha del conteo (modo informado). `/warehouse/cycle-counts/:id`.
// Todo con warehouse.count (la ruta lo exige: GET /cycle-counts/{id} ya lo pide). Captura por línea (cantidad en NONE/LOT,
// series en SERIAL) y línea agregada a mano mientras no esté RECONCILED; terminar (OPEN → COUNTED, exige todas las líneas
// capturadas) y eliminar solo OPEN; refrescar re-fotografía las líneas con foto vieja; reconciliar (COUNTED → RECONCILED)
// asienta los ajustes contra el saldo actual. Sin transición manual en el pipeline. Manual 06 §6.
import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useMemo, useState } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { z } from 'zod'
import { Can } from '../../kernel/access'
import { ApiError, applyProblemDetails } from '../../kernel/api/problem'
import type { components } from '../../kernel/api/schema'
import { StatusChip, StatusPipeline } from '../../kernel/catalogs'
import { useLang, useT } from '../../kernel/i18n'
import {
  Chip,
  ConfirmDialog,
  DataTable,
  EmptyState,
  Field,
  Form,
  Modal,
  NumberInput,
  Panel,
  QBox,
  Select,
  Spinner,
  TextArea,
  TextInput,
  Toggle,
  matchesQ,
  toast,
  type DataColumn,
  type RowAction,
} from '../../kernel/ui'
import { productLabel, useCycleCount, useCycleCountAction, useProductLots, warehouseKeys, type CycleCountDetailDto } from './api'
import { countLineIssues, countLotIssue, formatDateTime, formatNumber, parseSerials, remapProblemFields, type LineIssue } from './lineRules'
import { BinPickerInput, ProductPickerInput } from './pickers'
import { IconClip } from '../../kernel/ui/screenIcons'

type CountLine = components['schemas']['CycleCountLineDto']

const STATUS_DOMAIN = 'CycleCountStatus'
const ENTITY_TYPE = 'CYCLE_COUNT'

function useIssueText() {
  const t = useT()
  return (issue: LineIssue) => t(`warehouse.lineRules.${issue.code}`, issue.params)
}

function ToggleFilter({ label, checked, onChange }: { label: string; checked: boolean; onChange: (v: boolean) => void }) {
  return (
    <label className="sw">
      <input type="checkbox" role="switch" checked={checked} onChange={(e) => onChange(e.target.checked)} />
      <span className="tk" aria-hidden="true" />
      <span>{label}</span>
    </label>
  )
}

/** Quita el prefijo `lines[0].` de los errores de la captura (se envía una sola línea). */
const stripLinePrefix = (k: string) => k.replace(/^(\$\.)?lines\[\d+\]\./i, '')

// =====================================================================================================================
// Captura de una línea (PUT /{id}/lines)
// =====================================================================================================================
function CaptureModal({ detail, line, onClose }: { detail: CycleCountDetailDto; line: CountLine; onClose: () => void }) {
  const t = useT()
  const lang = useLang()
  const qc = useQueryClient()
  const issueText = useIssueText()
  const action = useCycleCountAction()
  const serial = line.trackingTypeCode === 'SERIAL'
  const sku = line.sku ?? ''
  const schema = useMemo(
    () =>
      z
        .object({ countedQty: z.number().nullable(), serialNumbers: z.string(), nothingFound: z.boolean() })
        .superRefine((v, ctx) => {
          for (const issue of countLineIssues({
            sku,
            trackingTypeCode: line.trackingTypeCode,
            countedQty: serial ? null : v.countedQty,
            serials: serial && v.nothingFound ? [] : parseSerials(v.serialNumbers),
          })) {
            ctx.addIssue({ code: 'custom', path: [issue.field], message: issueText(issue) })
          }
        }),
    [sku, line.trackingTypeCode, serial, issueText],
  )
  const form = useForm({
    resolver: zodResolver(schema),
    defaultValues: {
      countedQty: line.countedQty ?? null,
      serialNumbers: (line.countedSerials ?? []).join('\n'),
      nothingFound: serial && line.countedQty === 0,
    },
  })
  const nothingFound = useWatch({ control: form.control, name: 'nothingFound' })
  const formId = 'cycle-count-capture'

  return (
    <Modal
      open
      title={t('warehouse.cycleCounts.detail.captureTitle', { product: productLabel({ sku: line.sku, name: line.productName }) })}
      onClose={onClose}
      dismissible={!form.formState.isSubmitting}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose}>
            {t('common.cancel')}
          </button>
          <button type="submit" form={formId} className="btn flow" disabled={form.formState.isSubmitting}>
            {form.formState.isSubmitting ? t('common.loading') : t('ui.form.save')}
          </button>
        </>
      }
    >
      <p className="note" style={{ marginBottom: 10 }}>
        {t('warehouse.cycleCounts.detail.where', { bin: line.binCode ?? '', lot: line.lotNumber ?? '—' })} ·{' '}
        {t('warehouse.cycleCounts.detail.systemQty')}: {formatNumber(line.systemQty, lang)}
      </p>
      <Form
        id={formId}
        form={form}
        onSubmit={async (v) => {
          const serials = parseSerials(v.serialNumbers)
          try {
            await action.mutateAsync({
              id: detail.count?.id ?? 0,
              action: 'capture',
              body: {
                rowVersion: detail.rowVersion ?? null,
                lines: [
                  {
                    lineId: line.id ?? 0,
                    // NONE/LOT: la cantidad (vacía = borrar la captura); SERIAL: las series (vacías = borrar, salvo "no se encontró ninguna")
                    countedQty: serial ? null : v.countedQty,
                    serialNumbers: serial ? (v.nothingFound ? [] : serials.length > 0 ? serials : null) : null,
                  },
                ],
              },
            })
          } catch (err) {
            if (err instanceof ApiError && err.code === 'conflict') void qc.invalidateQueries({ queryKey: warehouseKeys.cycleCount })
            throw remapProblemFields(err, stripLinePrefix)
          }
          toast.success(t('warehouse.cycleCounts.detail.captured'))
          onClose()
        }}
      >
        {serial ? (
          <>
            <Field
              name="serialNumbers"
              label={t('warehouse.cycleCounts.detail.serials')}
              help={t('warehouse.cycleCounts.detail.expectedSerials', { count: line.expectedSerials?.length ?? 0, list: (line.expectedSerials ?? []).slice(0, 20).join(', ') || '—' })}
            >
              <TextArea rows={6} readOnly={nothingFound} />
            </Field>
            <Field name="nothingFound" label={t('warehouse.cycleCounts.detail.nothingFoundLabel')}>
              <Toggle text={t('warehouse.cycleCounts.detail.nothingFound')} />
            </Field>
          </>
        ) : (
          <Field name="countedQty" label={t('warehouse.cycleCounts.detail.countedQty')} help={t('warehouse.cycleCounts.detail.countedHelp')}>
            <NumberInput min={0} step="0.001" />
          </Field>
        )}
      </Form>
    </Modal>
  )
}

// =====================================================================================================================
// Línea agregada a mano (POST /{id}/lines): lo encontrado sin foto previa
// =====================================================================================================================
function AddLineModal({ detail, onClose }: { detail: CycleCountDetailDto; onClose: () => void }) {
  const t = useT()
  const issueText = useIssueText()
  const action = useCycleCountAction()
  const schema = useMemo(
    () =>
      z
        .object({
          binId: z.string(),
          productPublicId: z.string().nullable(),
          sku: z.string(),
          trackingTypeCode: z.string(),
          lotId: z.string(),
          lotNumber: z.string().max(60, t('warehouse.lineRules.lotTooLong')),
          countedQty: z.number().nullable(),
          serialNumbers: z.string(),
        })
        .superRefine((v, ctx) => {
          if (!v.binId) ctx.addIssue({ code: 'custom', path: ['binId'], message: t('warehouse.cycleCounts.errors.binRequired') })
          if (!v.productPublicId) ctx.addIssue({ code: 'custom', path: ['productPublicId'], message: t('warehouse.receipts.errors.productRequired') })
          if (v.lotId && v.lotNumber.trim()) ctx.addIssue({ code: 'custom', path: ['lotNumber'], message: t('warehouse.cycleCounts.errors.lotAmbiguous') })
          const lotIssue = countLotIssue(v.trackingTypeCode, v.sku, Boolean(v.lotId || v.lotNumber.trim()))
          if (lotIssue) ctx.addIssue({ code: 'custom', path: ['lotNumber'], message: issueText(lotIssue) })
          const serial = v.trackingTypeCode === 'SERIAL'
          for (const issue of countLineIssues({ sku: v.sku, trackingTypeCode: v.trackingTypeCode, countedQty: serial ? null : v.countedQty, serials: parseSerials(v.serialNumbers) })) {
            ctx.addIssue({ code: 'custom', path: [issue.field], message: issueText(issue) })
          }
          if (!serial && v.countedQty == null) ctx.addIssue({ code: 'custom', path: ['countedQty'], message: t('warehouse.cycleCounts.errors.countedRequired') })
        }),
    [t, issueText],
  )
  const form = useForm({
    resolver: zodResolver(schema),
    defaultValues: { binId: '', productPublicId: null as string | null, sku: '', trackingTypeCode: '', lotId: '', lotNumber: '', countedQty: null as number | null, serialNumbers: '' },
  })
  const productPublicId = useWatch({ control: form.control, name: 'productPublicId' })
  const tracking = useWatch({ control: form.control, name: 'trackingTypeCode' })
  const lots = useProductLots(productPublicId, { enabled: tracking === 'LOT' || tracking === 'SERIAL', handleAccessDenied: false })
  const lotOptions = useMemo(
    () => (lots.data ?? []).filter((l) => l.isActive !== false).map((l) => ({ value: String(l.id), label: [l.lotNumber, l.expiryDate].filter(Boolean).join(' · ') })),
    [lots.data],
  )
  const formId = 'cycle-count-add-line'

  return (
    <Modal
      open
      title={t('warehouse.cycleCounts.detail.addLine')}
      onClose={onClose}
      dismissible={!form.formState.isSubmitting}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose}>
            {t('common.cancel')}
          </button>
          <button type="submit" form={formId} className="btn flow" disabled={form.formState.isSubmitting}>
            {form.formState.isSubmitting ? t('common.loading') : t('ui.form.save')}
          </button>
        </>
      }
    >
      <Form
        id={formId}
        form={form}
        onSubmit={async (v) => {
          const serials = parseSerials(v.serialNumbers)
          const lotNumber = v.lotNumber.trim()
          try {
            await action.mutateAsync({
              id: detail.count?.id ?? 0,
              action: 'addLine',
              body: {
                binId: Number(v.binId),
                productPublicId: v.productPublicId,
                lotId: v.lotId ? Number(v.lotId) : null,
                lot: lotNumber ? { number: lotNumber } : undefined,
                countedQty: v.trackingTypeCode === 'SERIAL' ? null : v.countedQty,
                serialNumbers: v.trackingTypeCode === 'SERIAL' ? serials : null,
              },
            })
          } catch (err) {
            throw remapProblemFields(err, (k) => (k === 'lot' || k.startsWith('lot.') ? 'lotNumber' : null))
          }
          toast.success(t('warehouse.cycleCounts.detail.lineAdded'))
          onClose()
        }}
      >
        <div className="r2">
          <Field name="binId" label={t('warehouse.cycleCounts.detail.bin')} required>
            <BinPickerInput warehousePublicId={detail.count?.warehousePublicId} />
          </Field>
          <Field name="productPublicId" label={t('warehouse.receipts.fields.product')} required>
            <ProductPickerInput
              onPicked={(p) => {
                form.setValue('sku', p?.sku ?? '')
                form.setValue('trackingTypeCode', p?.trackingTypeCode ?? '')
                form.setValue('lotId', '')
              }}
            />
          </Field>
        </div>
        {(tracking === 'LOT' || tracking === 'SERIAL') && (
          <div className="r2">
            <Field name="lotId" label={t('warehouse.cycleCounts.detail.existingLot')}>
              <Select options={lotOptions} placeholder="—" />
            </Field>
            <Field name="lotNumber" label={t('warehouse.cycleCounts.detail.newLot')} required={tracking === 'LOT'}>
              <TextInput maxLength={60} />
            </Field>
          </div>
        )}
        {tracking === 'SERIAL' ? (
          <Field name="serialNumbers" label={t('warehouse.cycleCounts.detail.serials')} help={t('warehouse.receipts.fields.serialsHelp')}>
            <TextArea rows={4} />
          </Field>
        ) : (
          <Field name="countedQty" label={t('warehouse.cycleCounts.detail.countedQty')} required>
            <NumberInput min={0} step="0.001" />
          </Field>
        )}
      </Form>
    </Modal>
  )
}

// =====================================================================================================================
// Pantalla
// =====================================================================================================================
export default function CycleCountDetailScreen() {
  const t = useT()
  const lang = useLang()
  const navigate = useNavigate()
  const qc = useQueryClient()
  const params = useParams()
  const id = Number(params.id)
  const validId = Number.isInteger(id) && id > 0
  const [onlyPending, setOnlyPending] = useState(false)
  const [onlyVariance, setOnlyVariance] = useState(false)
  const [q, setQ] = useState('')
  const [capturing, setCapturing] = useState<CountLine | null>(null)
  const [adding, setAdding] = useState(false)
  const [confirm, setConfirm] = useState<null | 'finish' | 'reconcile' | 'delete'>(null)
  const { data: detail, isLoading, error } = useCycleCount(validId ? id : null, {
    onlyPending: onlyPending || undefined,
    onlyVariance: onlyVariance || undefined,
  })
  const action = useCycleCountAction()
  const refresh = useMutation({
    mutationFn: () => action.mutateAsync({ id, action: 'refresh' }),
    onSuccess: () => toast.success(t('warehouse.cycleCounts.detail.refreshed')),
    onError: (err) => toast.error(applyProblemDetails(err).title),
  })

  const count = detail?.count
  const statusCode = count?.statusCode
  const editable = Boolean(statusCode) && statusCode !== 'RECONCILED'
  const reconciled = statusCode === 'RECONCILED'
  const allLines = useMemo(() => detail?.lines ?? [], [detail?.lines])
  const lines = useMemo(
    () => allLines.filter((l) => matchesQ(q, l.binCode, l.zoneCode, l.sku, l.productName, l.categoryName, l.lotNumber)),
    [allLines, q],
  )
  const anyStale = allLines.some((l) => l.isStale)

  const columns = useMemo<DataColumn<CountLine>[]>(
    () => [
      { id: 'product', header: t('warehouse.cycleCounts.detail.product'), cell: (l) => productLabel({ sku: l.sku, name: l.productName }), sortValue: (l) => l.sku, card: 'title' },
      { id: 'bin', header: t('warehouse.cycleCounts.detail.bin'), cell: (l) => [l.binCode, l.zoneCode].filter(Boolean).join(' · '), sortValue: (l) => l.binCode },
      { id: 'lot', header: t('warehouse.cycleCounts.detail.lot'), cell: (l) => l.lotNumber ?? '—', sortValue: (l) => l.lotNumber },
      { id: 'system', header: t('warehouse.cycleCounts.detail.systemQty'), cell: (l) => formatNumber(l.systemQty, lang), sortValue: (l) => l.systemQty ?? 0, align: 'end' },
      {
        id: 'counted',
        header: t('warehouse.cycleCounts.detail.countedQty'),
        cell: (l) => (l.countedQty == null ? <Chip tone="neutral">{t('warehouse.cycleCounts.detail.pending')}</Chip> : formatNumber(l.countedQty, lang)),
        sortValue: (l) => l.countedQty ?? -1,
        align: 'end',
      },
      {
        id: 'variance',
        header: t('warehouse.cycleCounts.detail.variance'),
        cell: (l) => (l.varianceQty == null ? '—' : l.varianceQty !== 0 ? <Chip tone="warn">{formatNumber(l.varianceQty, lang)}</Chip> : '0'),
        sortValue: (l) => l.varianceQty ?? 0,
        align: 'end',
      },
      {
        id: 'current',
        header: t('warehouse.cycleCounts.detail.currentQty'),
        cell: (l) => (
          <>
            {formatNumber(reconciled ? (l.reconciledSystemQty ?? l.currentQty) : l.currentQty, lang)}
            {!reconciled && l.isStale && (
              <>
                {' '}
                <Chip tone="warn" title={t('warehouse.cycleCounts.detail.staleHelp')}>
                  {t('warehouse.cycleCounts.detail.stale')}
                </Chip>
              </>
            )}
            {reconciled && l.systemQtyChanged && (
              <>
                {' '}
                <Chip tone="warn" title={t('warehouse.cycleCounts.detail.changedHelp')}>
                  {t('warehouse.cycleCounts.detail.changed')}
                </Chip>
              </>
            )}
          </>
        ),
        sortValue: (l) => l.currentQty ?? 0,
        align: 'end',
      },
      {
        id: 'adjusted',
        header: t('warehouse.cycleCounts.detail.adjusted'),
        cell: (l) => (l.adjustedQty != null ? formatNumber(l.adjustedQty, lang) : '—'),
        sortValue: (l) => l.adjustedQty ?? 0,
        align: 'end',
        card: reconciled ? undefined : 'hidden',
      },
    ],
    [t, lang, reconciled],
  )

  const actions = useMemo<RowAction<CountLine>[]>(
    () => [{ key: 'capture', label: t('warehouse.cycleCounts.detail.capture'), perm: 'warehouse.count', visible: () => editable, onClick: (l) => setCapturing(l), tone: 'flow' }],
    [t, editable],
  )

  if (!validId) return <EmptyState title={t('warehouse.cycleCounts.notFound')} action={<Link className="btn" to="/warehouse/cycle-counts">{t('warehouse.cycleCounts.back')}</Link>} />
  if (isLoading) return <Spinner block />
  if (error || !detail || !count) {
    const notFound = error instanceof ApiError && error.code === 'not_found'
    return (
      <EmptyState
        title={notFound ? t('warehouse.cycleCounts.notFound') : (error?.message ?? t('errors.generic'))}
        action={
          <Link className="btn" to="/warehouse/cycle-counts">
            {t('warehouse.cycleCounts.back')}
          </Link>
        }
      />
    )
  }

  const runConfirm = async () => {
    const body = { rowVersion: detail.rowVersion ?? null }
    try {
      if (confirm === 'finish') {
        await action.mutateAsync({ id, action: 'finish', body })
        toast.success(t('warehouse.cycleCounts.detail.finished'))
      } else if (confirm === 'reconcile') {
        await action.mutateAsync({ id, action: 'reconcile', body })
        toast.success(t('warehouse.cycleCounts.detail.reconciled'))
      } else if (confirm === 'delete') {
        await action.mutateAsync({ id, action: 'delete' })
        toast.success(t('warehouse.cycleCounts.detail.deleted', { number: count.number ?? '' }))
        navigate('/warehouse/cycle-counts')
      }
    } catch (err) {
      if (err instanceof ApiError && err.code === 'conflict') void qc.invalidateQueries({ queryKey: warehouseKeys.cycleCount })
      throw err
    }
  }

  return (
    <div className="wrap">
      <div className="head">
        <div style={{ minWidth: 0 }}>
          <h1>
            <span className="ref">{count.number}</span> · {count.warehouseCode}
          </h1>
          <p>
            <StatusChip domain={STATUS_DOMAIN} code={count.statusCode} label={count.status} />{' '}
            {t('warehouse.cycleCounts.detail.progress', { counted: count.countedLines ?? 0, total: count.lineCount ?? 0 })} ·{' '}
            {formatDateTime(count.createdAtUtc, lang)}
          </p>
        </div>
        <div className="act">
          <Can perm="warehouse.count">
            {statusCode === 'OPEN' && (
              <button type="button" className="btn" onClick={() => setConfirm('delete')}>
                {t('warehouse.cycleCounts.detail.delete')}
              </button>
            )}
            {editable && anyStale && (
              <button type="button" className="btn" disabled={refresh.isPending} onClick={() => refresh.mutate()}>
                {refresh.isPending ? t('common.loading') : t('warehouse.cycleCounts.detail.refresh')}
              </button>
            )}
            {statusCode === 'OPEN' && (
              <button type="button" className="btn flow" onClick={() => setConfirm('finish')}>
                {t('warehouse.cycleCounts.detail.finish')}
              </button>
            )}
            {statusCode === 'COUNTED' && (
              <button type="button" className="btn flow" onClick={() => setConfirm('reconcile')}>
                {t('warehouse.cycleCounts.detail.reconcile')}
              </button>
            )}
          </Can>
        </div>
      </div>

      <div style={{ marginBottom: 14 }}>
        {/* Sin transición manual: COUNTED y RECONCILED se disparan con Terminar y Reconciliar. */}
        <StatusPipeline domain={STATUS_DOMAIN} entityType={ENTITY_TYPE} entityId={count.id} currentCode={count.statusCode} />
      </div>

      <Panel icon={<IconClip />} title={t('warehouse.cycleCounts.detail.summary')}>
        <div className="r3">
          <div className="f">
            <label>{t('warehouse.cycleCounts.columns.progress')}</label>
            <p>
              {count.countedLines ?? 0} / {count.lineCount ?? 0}
            </p>
          </div>
          <div className="f">
            <label>{t('warehouse.cycleCounts.detail.varianceLines')}</label>
            <p>{count.varianceLines ?? '—'}</p>
          </div>
          <div className="f">
            <label>{t('warehouse.cycleCounts.columns.netVariance')}</label>
            <p>{count.netVariance == null ? '—' : count.netVariance !== 0 ? <Chip tone="warn">{formatNumber(count.netVariance, lang)}</Chip> : '0'}</p>
          </div>
          {count.reconciledAtUtc && (
            <div className="f">
              <label>{t('warehouse.cycleCounts.detail.reconciledAt')}</label>
              <p>{formatDateTime(count.reconciledAtUtc, lang)}</p>
            </div>
          )}
        </div>
        {anyStale && editable && <p className="note">{t('warehouse.cycleCounts.detail.staleNote')}</p>}
      </Panel>

      <Panel
        flush
        icon={<IconClip />}
        title={t('warehouse.cycleCounts.detail.lines')}
        badge={lines.length}
        actions={
          editable ? (
            <Can perm="warehouse.count">
              <button type="button" className="btn sm" onClick={() => setAdding(true)}>
                {t('warehouse.cycleCounts.detail.addLine')}
              </button>
            </Can>
          ) : undefined
        }
      >
        <div className="qrow" style={{ flexWrap: 'wrap', gap: 12 }}>
          <QBox value={q} onChange={setQ} />
          <ToggleFilter label={t('warehouse.cycleCounts.detail.onlyPending')} checked={onlyPending} onChange={setOnlyPending} />
          <ToggleFilter label={t('warehouse.cycleCounts.detail.onlyVariance')} checked={onlyVariance} onChange={setOnlyVariance} />
        </div>
        <DataTable
          label={t('warehouse.cycleCounts.detail.lines')}
          columns={columns}
          rows={lines}
          rowKey={(l) => l.id ?? 0}
          pageSize={50}
          rowActions={actions}
          empty={<EmptyState title={t('warehouse.cycleCounts.detail.noLines')} />}
        />
      </Panel>

      {capturing && <CaptureModal detail={detail} line={capturing} onClose={() => setCapturing(null)} />}
      {adding && <AddLineModal detail={detail} onClose={() => setAdding(false)} />}

      <ConfirmDialog
        open={confirm !== null}
        tone={confirm === 'delete' ? 'danger' : 'flow'}
        title={confirm ? t(`warehouse.cycleCounts.detail.${confirm}Title`) : ''}
        message={confirm ? t(`warehouse.cycleCounts.detail.${confirm}Body`, { number: count.number ?? '' }) : ''}
        confirmLabel={confirm ? t(`warehouse.cycleCounts.detail.${confirm}`) : undefined}
        onConfirm={runConfirm}
        onClose={() => setConfirm(null)}
      />
    </div>
  )
}
