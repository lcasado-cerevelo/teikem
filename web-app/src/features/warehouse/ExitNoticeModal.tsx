// Lote 144 — la ventana de un aviso de salida: encabezado (cliente dueño, almacén, referencia, destino, fechas, estatus), las líneas con lo pedido, reservado, pendiente y despachado, y los
// despachos hechos. Acciones (exitnotice.create): cambiar la cantidad de una línea (aviso solicitado y sin despachar), reservar el pendiente de una línea, cerrar una línea con faltante y
// cancelar el aviso. Despachar se hace desde Recolección / Despacho manual («Contra: aviso de salida», lote 145).
import { zodResolver } from '@hookform/resolvers/zod'
import { useMemo, useState } from 'react'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { Can } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { Chip, ConfirmDialog, DataTable, Field, Form, IconCheckCircle, IconEdit, Modal, NumberInput, Spinner, toast, type DataColumn, type RowAction } from '../../kernel/ui'
import { useExitNotice, useExitNoticeMutations, type ExitNoticeLineDto } from './exitNoticeApi'
import { ExitNoticeDispatchModal } from './ExitNoticeDispatchModal'
import { AssignDocumentModal } from './AssignDocumentModal'
import { canDispatch, canHandlePending, canModifyLine, exitQuantityIssue } from './exitNoticeRules'

function QuantityModal({ publicId, line, onClose }: { publicId: string; line: ExitNoticeLineDto; onClose: () => void }) {
  const t = useT()
  const { modifyLine } = useExitNoticeMutations()
  const schema = useMemo(
    () =>
      z.object({ quantity: z.number().nullable() }).superRefine((v, ctx) => {
        const issue = exitQuantityIssue(v.quantity)
        if (issue) ctx.addIssue({ code: 'custom', path: ['quantity'], message: t(issue) })
      }),
    [t],
  )
  const form = useForm({ resolver: zodResolver(schema), defaultValues: { quantity: line.qtyOrdered ?? null } })
  const formId = 'exit-notice-qty'
  return (
    <Modal
      open
      size="sm"
      title={t('warehouse.exitNotices.modifyTitle', { sku: line.sku ?? '' })}
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
          await modifyLine.mutateAsync({ id: publicId, lineId: line.id!, quantity: v.quantity! })
          toast.success(t('warehouse.exitNotices.qtyUpdated'))
          onClose()
        }}
      >
        <Field name="quantity" label={t('warehouse.exitNotices.modifyLabel')} help={t('warehouse.exitNotices.modifyHelp')} required>
          <NumberInput min={0} step="0.001" />
        </Field>
      </Form>
    </Modal>
  )
}

export function ExitNoticeModal({ publicId, onClose }: { publicId: string; onClose: () => void }) {
  const t = useT()
  const f = useFormat()
  const q = useExitNotice(publicId)
  const { fulfillLine, closeLine, cancel, assign, take } = useExitNoticeMutations()
  const [editing, setEditing] = useState<ExitNoticeLineDto | null>(null)
  const [cancelling, setCancelling] = useState(false)
  const [dispatching, setDispatching] = useState(false)
  const [assigning, setAssigning] = useState(false)   // Lote 171
  const notice = q.data

  const run = async (p: Promise<unknown>, ok: string) => {
    try {
      await p
      toast.success(ok)
    } catch (e) {
      toast.error(applyProblemDetails(e).title)
    }
  }

  const columns = useMemo<DataColumn<ExitNoticeLineDto>[]>(
    () => [
      {
        id: 'product',
        header: t('warehouse.exitNotices.col.product'),
        cell: (l) => (
          <>
            <b>{l.sku}</b>
            <div className="help">{l.productName}</div>
          </>
        ),
        exportValue: (l) => `${l.sku} ${l.productName}`,
        sortValue: (l) => l.sku ?? '',
        card: 'title',
      },
      { id: 'lot', header: t('warehouse.exitNotices.col.lot'), cell: (l) => l.lotNumber ?? '—', sortValue: (l) => l.lotNumber ?? '' },
      { id: 'ordered', header: t('warehouse.exitNotices.col.ordered'), cell: (l) => f.number(l.qtyOrdered ?? 0), sortValue: (l) => l.qtyOrdered ?? 0, align: 'end' },
      { id: 'reserved', header: t('warehouse.exitNotices.col.reserved'), cell: (l) => f.number(l.qtyReserved ?? 0), sortValue: (l) => l.qtyReserved ?? 0, align: 'end' },
      {
        id: 'pending',
        header: t('warehouse.exitNotices.col.pending'),
        cell: (l) => ((l.qtyBackordered ?? 0) > 0 ? <Chip tone="warn">{f.number(l.qtyBackordered ?? 0)}</Chip> : l.isClosed ? <Chip>{t('warehouse.exitNotices.lineClosed')}</Chip> : '—'),
        exportValue: (l) => l.qtyBackordered ?? 0,
        sortValue: (l) => l.qtyBackordered ?? 0,
        align: 'end',
      },
      { id: 'shipped', header: t('warehouse.exitNotices.col.shipped'), cell: (l) => f.number(l.qtyShipped ?? 0), sortValue: (l) => l.qtyShipped ?? 0, align: 'end' },
    ],
    [t, f],
  )

  const actions = useMemo<RowAction<ExitNoticeLineDto>[]>(
    () =>
      notice
        ? [
            {
              key: 'modify',
              label: t('warehouse.exitNotices.modify'),
              perm: 'exitnotice.create',
              visible: (l) => canModifyLine(notice, l),
              onClick: (l) => setEditing(l),
              icon: <IconEdit />,
            },
            {
              key: 'fulfill',
              label: t('warehouse.exitNotices.fulfill'),
              perm: 'exitnotice.create',
              visible: (l) => canHandlePending(notice, l),
              onClick: (l) => void run(fulfillLine.mutateAsync({ id: publicId, lineId: l.id! }), t('warehouse.exitNotices.fulfilled', { sku: l.sku ?? '' })),
            },
            {
              key: 'close',
              label: t('warehouse.exitNotices.closeLine'),
              perm: 'exitnotice.create',
              tone: 'danger',
              visible: (l) => canHandlePending(notice, l),
              onClick: (l) => void run(closeLine.mutateAsync({ id: publicId, lineId: l.id! }), t('warehouse.exitNotices.lineClosedDone', { sku: l.sku ?? '' })),
              icon: <IconCheckCircle />,
            },
          ]
        : [],
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [notice, t, publicId],
  )

  return (
    <>
      <Modal
        open
        size="lg"
        title={notice ? t('warehouse.exitNotices.title1', { number: notice.number ?? '' }) : t('warehouse.exitNotices.title')}
        onClose={onClose}
        footer={
          <>
            {notice?.canCancel && (
              <Can perm="exitnotice.create">
                <button type="button" className="btn danger" onClick={() => setCancelling(true)}>
                  {t('warehouse.exitNotices.cancelNotice')}
                </button>
              </Can>
            )}
            {notice && (notice.statusCode === 'REQUESTED' || notice.statusCode === 'PARTIAL') && (
              <>
                {notice.assignedToUserId == null && (
                  <Can perm="warehouse.pick">
                    <button type="button" className="btn" onClick={() => void run(take.mutateAsync(publicId), t('warehouse.assign.taken'))}>
                      {t('warehouse.assign.take')}
                    </button>
                  </Can>
                )}
                <Can perm="warehouse.manage">
                  <button type="button" className="btn" onClick={() => setAssigning(true)}>
                    {t('warehouse.assign.action')}
                  </button>
                </Can>
              </>
            )}
            {notice && canDispatch(notice) && (
              <Can perm="warehouse.pick">
                <button type="button" className="btn flow" onClick={() => setDispatching(true)}>
                  {t('warehouse.exitNotices.dispatch')}
                </button>
              </Can>
            )}
            <button type="button" className="btn" onClick={onClose}>
              {t('common.close')}
            </button>
          </>
        }
      >
        {q.isError ? (
          <p className="ferr" role="alert">
            {applyProblemDetails(q.error).title}
          </p>
        ) : !notice ? (
          <Spinner block />
        ) : (
          <>
            <dl className="rcp-meta">
              <div>
                <dt>{t('warehouse.exitNotices.col.status')}</dt>
                <dd>
                  <Chip color={notice.statusColor ?? undefined}>{notice.status}</Chip> {notice.hasBackorder && <Chip tone="warn">{t('warehouse.exitNotices.withPending')}</Chip>}
                </dd>
              </div>
              <div>
                <dt>{t('warehouse.exitNotices.col.client')}</dt>
                <dd>{notice.clientName}</dd>
              </div>
              <div>
                <dt>{t('warehouse.exitNotices.col.warehouse')}</dt>
                <dd>{notice.warehouseName}</dd>
              </div>
              <div>
                <dt>{t('warehouse.assign.column')}</dt>
                <dd>{notice.assignedToName ?? '—'}</dd>
              </div>
              <div>
                <dt>{t('warehouse.exitNotices.col.reference')}</dt>
                <dd>{notice.reference ?? '—'}</dd>
              </div>
              <div>
                <dt>{t('warehouse.exitNotices.col.destination')}</dt>
                <dd>{notice.destination ?? '—'}</dd>
              </div>
              <div>
                <dt>{t('warehouse.exitNotices.col.date')}</dt>
                <dd>{f.date(notice.requestedDate)}</dd>
              </div>
              <div>
                <dt>{t('warehouse.exitNotices.col.required')}</dt>
                <dd>{notice.requiredDate ? f.date(notice.requiredDate) : '—'}</dd>
              </div>
              <div>
                <dt>{t('warehouse.exitNotices.col.by')}</dt>
                <dd>{notice.requestedByName ?? '—'}</dd>
              </div>
            </dl>
            {notice.notes && <p className="note">{notice.notes}</p>}
            <DataTable
              label={t('warehouse.exitNotices.lines.title')}
              columns={columns}
              rows={notice.lines ?? []}
              rowKey={(l) => l.id ?? 0}
              rowActions={actions}
              empty={t('warehouse.exitNotices.noLines')}
            />
            <p className="help">{t('warehouse.exitNotices.detailHelp')}</p>
            <h3 className="rcp-h3">{t('warehouse.exitNotices.dispatches')}</h3>
            {(notice.dispatches ?? []).length === 0 ? (
              <p className="help">{t('warehouse.exitNotices.noDispatches')}</p>
            ) : (
              <ul className="rcp-dispatches">
                {(notice.dispatches ?? []).map((d) => (
                  <li key={d.pickBatchPublicId}>
                    <b>{d.number}</b> · {f.date(d.collectedAtUtc)} · {f.number(d.quantity ?? 0)}
                    {d.statusCode === 'CANCELLED' && <Chip>{t('warehouse.exitNotices.dispatchUndone')}</Chip>}
                  </li>
                ))}
              </ul>
            )}
          </>
        )}
      </Modal>
      {dispatching && notice && <ExitNoticeDispatchModal notice={notice} onClose={() => setDispatching(false)} />}
      {assigning && notice && (
        <AssignDocumentModal
          title={t('warehouse.assign.title', { number: notice.number ?? '' })}
          currentUserId={notice.assignedToUserId}
          onSave={(userId) => assign.mutateAsync({ id: publicId, userId })}
          onClose={() => setAssigning(false)}
        />
      )}
      {editing && <QuantityModal publicId={publicId} line={editing} onClose={() => setEditing(null)} />}
      <ConfirmDialog
        open={cancelling}
        tone="danger"
        title={t('warehouse.exitNotices.cancelTitle')}
        message={t('warehouse.exitNotices.cancelBody', { number: notice?.number ?? '' })}
        confirmLabel={t('warehouse.exitNotices.cancelNotice')}
        onConfirm={async () => {
          await cancel.mutateAsync({ id: publicId })
          toast.success(t('warehouse.exitNotices.cancelled'))
        }}
        onClose={() => setCancelling(false)}
      />
    </>
  )
}
