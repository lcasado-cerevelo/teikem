// Lote 183 (C4) — ficha de una recepción maestra: el dueño, el almacén, quién y cuándo la abrió, y los recibos que reúne (uno por documento) con su avance. Cada recibo se abre desde aquí en su
// pantalla de siempre para capturar; «Confirmar todo» los confirma juntos y «Eliminar» los elimina mientras ninguno esté confirmado. Manual 06 «Recepción maestra».
import { useState } from 'react'
import { Can } from '../../kernel/access'
import { ApiError, applyProblemDetails } from '../../kernel/api/problem'
import { useFormat } from '../../kernel/format'
import { useLang, useT } from '../../kernel/i18n'
import { Chip, ConfirmDialog, EmptyState, Modal, Spinner, toast } from '../../kernel/ui'
import { useConfirmReceiptGroup, useDeleteReceiptGroup, useReceiptGroup } from './api'
import { formatDateTime } from './lineRules'

export function ReceiptGroupModal({ publicId, onClose, onOpenReceipt }: { publicId: string | null; onClose: () => void; onOpenReceipt: (receiptPublicId: string) => void }) {
  const t = useT()
  const lang = useLang()
  const f = useFormat()
  const { data: g, isLoading, error } = useReceiptGroup(publicId)
  const confirm = useConfirmReceiptGroup()
  const del = useDeleteReceiptGroup()
  const [confirming, setConfirming] = useState(false)
  const [deleting, setDeleting] = useState(false)
  const [actionError, setActionError] = useState<string | null>(null)
  const busy = confirm.isPending || del.isPending
  const notFound = error instanceof ApiError && error.code === 'not_found'

  const title = g ? (
    <span className="collect-dtitle">
      <span className="ref">{g.number}</span> <Chip tone="cap">{t('warehouse.receipts.groups.badge')}</Chip> <Chip>{t(`warehouse.receipts.groups.status.${g.statusCode}`)}</Chip>
    </span>
  ) : (
    t('warehouse.receipts.groups.detailTitle')
  )

  async function doConfirm() {
    if (!g) return
    setActionError(null)
    try {
      await confirm.mutateAsync({ publicId: g.publicId ?? '' })
      toast.success(t('warehouse.receipts.groups.confirmedToast', { number: g.number ?? '' }))
      setConfirming(false)
    } catch (e) {
      setConfirming(false)
      setActionError(applyProblemDetails(e).title)
    }
  }

  return (
    <>
      <Modal
        open={publicId !== null}
        size="lg"
        title={title}
        onClose={onClose}
        dismissible={!busy}
        footer={
          <>
            <button type="button" className="btn" onClick={onClose} disabled={busy}>
              {t('warehouse.receipts.groups.close')}
            </button>
            {g && g.canDelete && (
              <Can perm="warehouse.receive">
                <button type="button" className="btn" onClick={() => setDeleting(true)} disabled={busy}>
                  {t('warehouse.receipts.groups.delete')}
                </button>
              </Can>
            )}
            {g && g.canConfirm && (
              <Can perm="warehouse.receive">
                <button type="button" className="btn flow" onClick={() => setConfirming(true)} disabled={busy}>
                  {t('warehouse.receipts.groups.confirmAll')}
                </button>
              </Can>
            )}
          </>
        }
      >
        {isLoading ? (
          <Spinner block />
        ) : error || !g ? (
          <EmptyState title={notFound ? t('warehouse.receipts.groups.notFound') : (error?.message ?? t('errors.generic'))} />
        ) : (
          <>
            <dl className="rcp-meta">
              <div>
                <dt>{t('warehouse.receipts.groups.columns.owner')}</dt>
                <dd>{g.ownerName ?? t('warehouse.receipts.groups.own')}</dd>
              </div>
              <div>
                <dt>{t('warehouse.receipts.groups.warehouse')}</dt>
                <dd>{g.warehouseCode}</dd>
              </div>
              <div>
                <dt>{t('warehouse.receipts.groups.columns.created')}</dt>
                <dd>
                  {formatDateTime(g.createdAtUtc, lang)}
                  {g.createdBy ? ` · ${g.createdBy}` : ''}
                </dd>
              </div>
              <div>
                <dt>{t('warehouse.receipts.groups.progress')}</dt>
                <dd>
                  {t('warehouse.receipts.groups.confirmedOf', { done: g.confirmedCount ?? 0, total: g.memberCount ?? 0 })} · {f.number(g.receivedQty ?? 0)} / {f.number(g.expectedQty ?? 0)}
                </dd>
              </div>
              {g.note && (
                <div>
                  <dt>{t('warehouse.receipts.groups.note')}</dt>
                  <dd>{g.note}</dd>
                </div>
              )}
            </dl>
            <h3 className="collect-sub">{t('warehouse.receipts.groups.members', { count: g.members?.length ?? 0 })}</h3>
            <table className="pgroup-moves">
              <thead>
                <tr>
                  <th>{t('warehouse.receipts.groups.cols.receipt')}</th>
                  <th>{t('warehouse.receipts.groups.cols.document')}</th>
                  <th>{t('warehouse.receipts.groups.cols.status')}</th>
                  <th className="num">{t('warehouse.receipts.groups.cols.lines')}</th>
                  <th className="num">{t('warehouse.receipts.groups.cols.qty')}</th>
                  <th aria-label={t('warehouse.receipts.groups.cols.open')} />
                </tr>
              </thead>
              <tbody>
                {(g.members ?? []).map((m) => (
                  <tr key={m.receiptPublicId}>
                    <td>
                      <span className="ref">{m.receiptNumber}</span>
                    </td>
                    <td>
                      <Chip tone="cap">{t(`warehouse.receipts.groups.origin.${m.originKind}`)}</Chip> {m.originRef}
                    </td>
                    <td>{m.status}</td>
                    <td className="num">{m.lineCount}</td>
                    <td className="num">
                      {f.number(m.receivedQty ?? 0)} / {f.number(m.expectedQty ?? 0)}
                    </td>
                    <td>
                      <button type="button" className="btn sm" onClick={() => onOpenReceipt(m.receiptPublicId ?? '')}>
                        {t('warehouse.receipts.groups.openReceipt')}
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
            {actionError && (
              <p className="ferr" role="alert">
                {actionError}
              </p>
            )}
          </>
        )}
      </Modal>
      <ConfirmDialog
        open={confirming}
        title={t('warehouse.receipts.groups.confirmTitle')}
        message={<p>{t('warehouse.receipts.groups.confirmBody', { number: g?.number ?? '' })}</p>}
        confirmLabel={t('warehouse.receipts.groups.confirmAll')}
        onConfirm={doConfirm}
        onClose={() => setConfirming(false)}
      />
      <ConfirmDialog
        open={deleting}
        tone="danger"
        title={t('warehouse.receipts.groups.deleteTitle')}
        message={<p>{t('warehouse.receipts.groups.deleteBody', { number: g?.number ?? '' })}</p>}
        confirmLabel={t('warehouse.receipts.groups.delete')}
        onConfirm={async () => {
          if (!g) return
          await del.mutateAsync(g.publicId ?? '')
          toast.success(t('warehouse.receipts.groups.deleted', { number: g.number ?? '' }))
          onClose()
        }}
        onClose={() => setDeleting(false)}
      />
    </>
  )
}
