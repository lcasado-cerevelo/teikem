// Lote F12 — "Confirmar conteo y ajustar" pasa primero por la VISTA PREVIA del efecto (`GET /cycle-counts/{id}/reconcile-preview`,
// warehouse.count): por posición la existencia ACTUAL, lo reservado, lo contado, el ajuste que se asentaría (+/−, color de
// semáforo), el saldo resultante y el error de la línea (contado menos que lo reservado, el mismo texto del 409); totales
// (líneas, por contar, con diferencia, movimientos, con error) y avisos: faltan líneas, "Concordancia" (nada se ajusta), saldo
// movido desde la foto. Con algo que bloquea, Confirmar queda deshabilitado con el motivo. Confirmar sigue siendo TODO O NADA
// (`POST /reconcile` con el `rowVersion` de la vista previa: si el conteo cambió después, el API responde 409 y se recalcula).
// La vista previa la calcula el servidor con el mismo plan que reconcilia: aquí no se repite ninguna regla.
import { useQueryClient } from '@tanstack/react-query'
import { useId, useMemo, useState } from 'react'
import { ApiError } from '../../kernel/api/problem'
import { useLang, useT } from '../../kernel/i18n'
import { Chip, DataTable, EmptyState, IconCheck, Modal, Spinner, SummaryBar, toast, type DataColumn, type SummaryItem } from '../../kernel/ui'
import { useCycleCountAction, useReconcilePreview, warehouseKeys, type CycleCountDetailDto, type ReconcilePreviewLineDto } from './api'
import { adjustmentClass, previewBlocker, signedQty } from './countReview'
import { formatNumber } from './lineRules'
import { problemText } from './problemText'

export interface ReconcilePreviewModalProps {
  /** Id y número del conteo. */
  count: { id: number; number?: string | null }
  onClose: () => void
  /** Tras confirmar (la ficha devuelta ya quedó en caché). */
  onConfirmed?: (dto: CycleCountDetailDto | null) => void
}

const NO_LINES: ReconcilePreviewLineDto[] = []

export function ReconcilePreviewModal({ count, onClose, onConfirmed }: ReconcilePreviewModalProps) {
  const t = useT()
  const lang = useLang()
  const qc = useQueryClient()
  const helpId = useId()
  const preview = useReconcilePreview(count.id)
  const action = useCycleCountAction()
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const data = preview.data
  const lines = data?.lines ?? NO_LINES
  const totals = data?.totals ?? {}
  const blocker = previewBlocker(data)
  const num = (n: number) => formatNumber(n, lang)
  const showLot = lines.some((l) => l.lotNumber)
  const showReserved = lines.some((l) => (l.reservedQty ?? 0) > 0)
  const showError = lines.some((l) => l.error)
  const changed = lines.filter((l) => l.systemQtyChanged).length

  const columns = useMemo<DataColumn<ReconcilePreviewLineDto>[]>(() => {
    const num = (n: number) => formatNumber(n, lang)
    return [
      {
        id: 'bin',
        header: t('warehouse.cycleCounts.preview.columns.bin'),
        cell: (l) => (
          <span className="cc-bincell">
            <span className="ref">{l.binCode ?? '—'}</span>
            {l.binIsProvisional && (
              <Chip tone="warn" title={t('warehouse.cycleCounts.provisional.help')}>
                {t('warehouse.cycleCounts.provisional.chip')}
              </Chip>
            )}
          </span>
        ),
        sortValue: (l) => l.binCode,
        exportValue: (l) => l.binCode ?? '',
        card: 'title',
      },
      {
        id: 'product',
        header: t('warehouse.cycleCounts.preview.columns.product'),
        cell: (l) => [l.sku, l.productName].filter(Boolean).join(' · ') || '—',
        sortValue: (l) => l.sku,
      },
      ...(showLot
        ? [{ id: 'lot', header: t('warehouse.cycleCounts.preview.columns.lot'), cell: (l: ReconcilePreviewLineDto) => l.lotNumber ?? '—', sortValue: (l: ReconcilePreviewLineDto) => l.lotNumber }]
        : []),
      {
        id: 'current',
        header: t('warehouse.cycleCounts.preview.columns.current'),
        align: 'end',
        cell: (l) => (
          <span className="cc-expected">
            <span className="mono">{num(l.currentQty ?? 0) || '0'}</span>
            {l.systemQtyChanged && (
              <Chip tone="warn" title={t('warehouse.cycleCounts.preview.changedHelp', { qty: num(l.systemQty ?? 0) || '0' })}>
                {t('warehouse.cycleCounts.detail.changed')}
              </Chip>
            )}
          </span>
        ),
        sortValue: (l) => l.currentQty ?? 0,
        exportValue: (l) => l.currentQty ?? 0,
      },
      ...(showReserved
        ? [
            {
              id: 'reserved',
              header: t('warehouse.cycleCounts.preview.columns.reserved'),
              align: 'end' as const,
              cell: (l: ReconcilePreviewLineDto) => <span className="mono">{num(l.reservedQty ?? 0) || '0'}</span>,
              sortValue: (l: ReconcilePreviewLineDto) => l.reservedQty ?? 0,
              exportValue: (l: ReconcilePreviewLineDto) => l.reservedQty ?? 0,
            },
          ]
        : []),
      {
        id: 'counted',
        header: t('warehouse.cycleCounts.preview.columns.counted'),
        align: 'end',
        cell: (l) =>
          l.isPending ? (
            <Chip tone="neutral">{t('warehouse.cycleCounts.preview.pendingRow')}</Chip>
          ) : (
            <span className="cc-expected">
              <span className="mono">{num(l.countedQty ?? 0) || '0'}</span>
              {l.wasCorrected && <Chip tone="disp">{t('warehouse.cycleCounts.evidence.correction')}</Chip>}
            </span>
          ),
        sortValue: (l) => (l.isPending ? undefined : (l.countedQty ?? 0)),
        exportValue: (l) => (l.isPending ? null : (l.countedQty ?? 0)),
      },
      {
        id: 'adjustment',
        header: t('warehouse.cycleCounts.preview.columns.adjustment'),
        align: 'end',
        cell: (l) =>
          l.isPending ? (
            <span className="rcp-faint">—</span>
          ) : (
            <span className="cc-adjcell">
              <span className={`mono ${adjustmentClass(l.adjustmentQty)}`}>{signedQty(l.adjustmentQty, num)}</span>
              {l.serials && (
                <span className="rcp-faint cc-serialplan">
                  {t('warehouse.cycleCounts.preview.serials', {
                    additions: l.serials.additions?.length ?? 0,
                    removals: l.serials.removals?.length ?? 0,
                    transfers: l.serials.transfers?.length ?? 0,
                  })}
                </span>
              )}
            </span>
          ),
        sortValue: (l) => (l.isPending ? undefined : (l.adjustmentQty ?? 0)),
        exportValue: (l) => (l.isPending ? null : (l.adjustmentQty ?? 0)),
      },
      {
        id: 'resulting',
        header: t('warehouse.cycleCounts.preview.columns.resulting'),
        align: 'end',
        cell: (l) => (l.isPending ? <span className="rcp-faint">—</span> : <span className="mono">{num(l.resultingQty ?? 0) || '0'}</span>),
        sortValue: (l) => (l.isPending ? undefined : (l.resultingQty ?? 0)),
        exportValue: (l) => (l.isPending ? null : (l.resultingQty ?? 0)),
      },
      ...(showError
        ? [
            {
              id: 'error',
              header: t('warehouse.cycleCounts.preview.columns.error'),
              cell: (l: ReconcilePreviewLineDto) => (l.error ? <span className="ferr cc-line-err">{l.error}</span> : <span className="rcp-faint">—</span>),
              sortValue: (l: ReconcilePreviewLineDto) => l.error ?? undefined,
            },
          ]
        : []),
    ]
  }, [t, lang, showLot, showReserved, showError])

  const summary: SummaryItem[] = [
    { key: 'lines', label: t('warehouse.cycleCounts.preview.totals.lines'), value: num(totals.lines ?? 0) || '0' },
    { key: 'pending', label: t('warehouse.cycleCounts.preview.totals.pending'), value: num(totals.pendingLines ?? 0) || '0', tone: (totals.pendingLines ?? 0) > 0 ? 'out' : 'muted' },
    { key: 'differing', label: t('warehouse.cycleCounts.preview.totals.differing'), value: num(totals.linesWithDifference ?? 0) || '0', tone: (totals.linesWithDifference ?? 0) > 0 ? 'money' : 'muted' },
    { key: 'movements', label: t('warehouse.cycleCounts.preview.totals.movements'), value: num(totals.movements ?? 0) || '0', tone: (totals.movements ?? 0) > 0 ? 'in' : 'muted' },
    { key: 'errors', label: t('warehouse.cycleCounts.preview.totals.errors'), value: num(totals.errorLines ?? 0) || '0', tone: (totals.errorLines ?? 0) > 0 ? 'out' : 'muted' },
  ]

  const confirm = async () => {
    if (!data || blocker) return
    setBusy(true)
    setError(null)
    try {
      // todo o nada, con el rowVersion de lo que se previsualizó: si el conteo cambió después, el API responde 409
      const dto = await action.mutateAsync({ id: count.id, action: 'reconcile', body: { rowVersion: data.rowVersion ?? null } })
      const code = dto?.count?.statusCode
      const adjusted = (dto?.lines ?? []).filter((l) => l.adjustedQty != null && l.adjustedQty !== 0).length
      if (code === 'RECONCILED_VARIANCE')
        toast.success(t('warehouse.cycleCounts.detail.confirmedVariance', { number: count.number ?? '', status: dto?.count?.status ?? '', n: adjusted }))
      else toast.success(t('warehouse.cycleCounts.detail.confirmedMatch', { number: count.number ?? '', status: dto?.count?.status ?? '' }))
      onConfirmed?.(dto)
      onClose()
    } catch (err) {
      setError(problemText(err))
      // otro usuario cambió el conteo o la existencia: se recalculan la ficha y la vista previa
      if (err instanceof ApiError && (err.code === 'conflict' || err.status === 409)) {
        void qc.invalidateQueries({ queryKey: warehouseKeys.cycleCount })
        void qc.invalidateQueries({ queryKey: warehouseKeys.reconcilePreview })
      }
    } finally {
      setBusy(false)
    }
  }

  let body
  if (preview.isLoading) body = <Spinner block label={t('warehouse.cycleCounts.preview.loading')} />
  else if (preview.error || !data)
    body = (
      <p className="ferr" role="alert">
        {preview.error ? problemText(preview.error) : t('errors.generic')}
      </p>
    )
  else
    body = (
      <>
        <p className="note cc-qty-where">{t('warehouse.cycleCounts.preview.intro')}</p>
        <SummaryBar label={t('warehouse.cycleCounts.preview.totalsLabel')} items={summary} loading={preview.isFetching} />
        <div className="cc-preview-notes">
          {(totals.pendingLines ?? 0) > 0 && <p className="note cc-warn">{t('warehouse.cycleCounts.preview.pendingNote', { n: totals.pendingLines ?? 0 })}</p>}
          {totals.matches && <p className="note rcp-note-money">{t('warehouse.cycleCounts.preview.matchNote')}</p>}
          {!totals.matches && totals.resultStatusCode === 'RECONCILED_VARIANCE' && (
            <p className="note">{t('warehouse.cycleCounts.preview.varianceNote', { n: totals.movements ?? 0 })}</p>
          )}
          {changed > 0 && <p className="note">{t('warehouse.cycleCounts.preview.changedNote', { n: changed })}</p>}
          {data.blockingError && (
            <p className="ferr" role="alert">
              {data.blockingError}
            </p>
          )}
        </div>
        <DataTable
          label={t('warehouse.cycleCounts.preview.linesLabel')}
          columns={columns}
          rows={lines}
          rowKey={(l) => l.lineId ?? 0}
          rowClassName={(l) => (l.error ? 'cc-row-error' : undefined)}
          exportable={false}
          empty={<EmptyState title={t('warehouse.cycleCounts.detail.noLines')} />}
        />
      </>
    )

  const blockerText = blocker
    ? blocker.key === 'blocking'
      ? blocker.message
      : t(`warehouse.cycleCounts.preview.blockers.${blocker.key}`, 'params' in blocker ? blocker.params : undefined)
    : null

  return (
    <Modal
      open
      size="lg"
      title={t('warehouse.cycleCounts.detail.confirmTitle')}
      onClose={onClose}
      dismissible={!busy}
      footer={
        <div className="cc-preview-foot">
          {blockerText && (
            <p className="help cc-preview-why" id={helpId}>
              {blockerText}
            </p>
          )}
          {error && (
            <p className="ferr" role="alert">
              {error}
            </p>
          )}
          <div className="cc-preview-btns">
            <button type="button" className="btn" onClick={onClose} disabled={busy}>
              {t('warehouse.cycleCounts.preview.back')}
            </button>
            <button
              type="button"
              className="btn flow"
              disabled={busy || !data || preview.isFetching || blocker !== null}
              aria-describedby={blockerText ? helpId : undefined}
              onClick={() => void confirm()}
            >
              <IconCheck /> {busy ? t('common.loading') : t('warehouse.cycleCounts.detail.confirm')}
            </button>
          </div>
        </div>
      }
    >
      {body}
    </Modal>
  )
}
