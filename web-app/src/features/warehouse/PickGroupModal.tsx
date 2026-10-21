// Lote 177 — ficha de una recolección MAESTRA (Lote 176): varios documentos del mismo cliente recolectados juntos y UN envío. Arriba el resumen (cliente, almacén, recolectada, envío),
// los documentos que reúne y TODOS los movimientos (de qué posición salió qué, con su lote y para cuál documento, agrupados por documento). Acciones: Empacar (un envío para todo; abre el
// formulario de empaque de siempre) y Eliminar (deshace todas las recolecciones). Manual 06 «Recolección maestra».
import { useState } from 'react'
import { Can } from '../../kernel/access'
import { ApiError } from '../../kernel/api/problem'
import { useQueryClient } from '@tanstack/react-query'
import { useFormat } from '../../kernel/format'
import { useLang, useT } from '../../kernel/i18n'
import { Chip, ConfirmDialog, EmptyState, Modal, Spinner, toast } from '../../kernel/ui'
import { useDeletePickGroup, usePickGroup, warehouseKeys } from './api'
import { formatDateTime } from './lineRules'
import { PackModal } from './PackModal'
import { movementsByDocument } from './pickGroupView'

export function PickGroupModal({ publicId, onClose }: { publicId: string | null; onClose: () => void }) {
  const t = useT()
  const lang = useLang()
  const f = useFormat()
  const qc = useQueryClient()
  const { data: g, isLoading, error } = usePickGroup(publicId)
  const del = useDeletePickGroup()
  const [packing, setPacking] = useState(false)
  const [deleting, setDeleting] = useState(false)
  const busy = packing || deleting
  const notFound = error instanceof ApiError && error.code === 'not_found'

  const title = g ? (
    <span className="collect-dtitle">
      <span className="ref">{g.number}</span> <Chip tone="cap">{t('warehouse.pickBatches.groups.badge')}</Chip> <Chip>{t(`warehouse.pickBatches.groups.status.${g.statusCode}`)}</Chip>
    </span>
  ) : (
    t('warehouse.pickBatches.groups.detailTitle')
  )

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
              {t('warehouse.pickBatches.detail.close')}
            </button>
            {g && g.canDelete && (
              <Can perm="warehouse.pick">
                <button type="button" className="btn" onClick={() => setDeleting(true)}>
                  {t('warehouse.pickBatches.detail.delete')}
                </button>
              </Can>
            )}
            {g && g.canPack && (
              <Can perm={['warehouse.pick', 'orders.create']}>
                <button type="button" className="btn flow" onClick={() => setPacking(true)}>
                  {t('warehouse.pickBatches.detail.pack')}
                </button>
              </Can>
            )}
          </>
        }
      >
        {isLoading ? (
          <Spinner block />
        ) : error || !g ? (
          <EmptyState title={notFound ? t('warehouse.pickBatches.groups.notFound') : (error?.message ?? t('errors.generic'))} />
        ) : (
          <>
            <dl className="rcp-meta">
              <div>
                <dt>{t('warehouse.pickBatches.docs.columns.client')}</dt>
                <dd>{g.clientName}</dd>
              </div>
              <div>
                <dt>{t('warehouse.pickBatches.groups.warehouse')}</dt>
                <dd>{g.warehouseCode}</dd>
              </div>
              <div>
                <dt>{t('warehouse.pickBatches.groups.collected')}</dt>
                <dd>
                  {formatDateTime(g.collectedAtUtc, lang)}
                  {g.collectedBy ? ` · ${g.collectedBy}` : ''}
                </dd>
              </div>
              <div>
                <dt>{t('warehouse.pickBatches.groups.total')}</dt>
                <dd>{f.number(g.totalQty ?? 0)}</dd>
              </div>
              {g.orderNumber && (
                <div>
                  <dt>{t('warehouse.pickBatches.groups.shipment')}</dt>
                  <dd>
                    <span className="ref">{g.orderNumber}</span>
                  </dd>
                </div>
              )}
              {g.note && (
                <div>
                  <dt>{t('warehouse.pickBatches.groups.note')}</dt>
                  <dd>{g.note}</dd>
                </div>
              )}
            </dl>

            <h3 className="collect-sub">{t('warehouse.pickBatches.groups.documents', { count: g.documents?.length ?? 0 })}</h3>
            <ul className="collect-moves">
              {(g.documents ?? []).map((d) => (
                <li key={d.batchPublicId}>
                  <span className="ref">{d.number}</span> <Chip tone="cap">{t(`warehouse.pickBatches.docs.kinds.${d.kind}`)}</Chip> · {d.batchNumber} · {f.number(d.quantity ?? 0)}
                </li>
              ))}
            </ul>

            <h3 className="collect-sub">{t('warehouse.pickBatches.groups.movements')}</h3>
            {movementsByDocument(g.movements ?? []).map((grp) => (
              <div key={`${grp.kind}:${grp.document}`} className="pgroup-doc">
                <div className="pgroup-dochead">
                  <span className="ref">{grp.document}</span> <span className="help">{t('warehouse.pickBatches.groups.docTotal', { n: f.number(grp.total) })}</span>
                </div>
                <table className="pgroup-moves">
                  <thead>
                    <tr>
                      <th>{t('warehouse.pickBatches.groups.cols.product')}</th>
                      <th>{t('warehouse.pickBatches.groups.cols.bin')}</th>
                      <th>{t('warehouse.pickBatches.groups.cols.lot')}</th>
                      <th className="num">{t('warehouse.pickBatches.groups.cols.qty')}</th>
                    </tr>
                  </thead>
                  <tbody>
                    {grp.rows.map((m, i) => (
                      <tr key={i}>
                        <td>
                          <span className="ref">{m.sku}</span> {m.productName}
                        </td>
                        <td>{m.binCode}</td>
                        <td>{m.lotNumber ?? '—'}</td>
                        <td className="num">{f.number(m.quantity ?? 0)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            ))}
          </>
        )}
      </Modal>
      {packing && g && <PackModal group batch={{ publicId: g.publicId, number: g.number, clientName: g.clientName, rowVersion: g.rowVersion }} onClose={() => setPacking(false)} />}
      <ConfirmDialog
        open={deleting}
        tone="danger"
        title={t('warehouse.pickBatches.groups.deleteTitle')}
        message={g?.orderNumber ? t('warehouse.pickBatches.groups.deletePackedBody', { number: g.number, order: g.orderNumber }) : t('warehouse.pickBatches.groups.deleteBody', { number: g?.number ?? '' })}
        confirmLabel={t('warehouse.pickBatches.detail.delete')}
        onConfirm={async () => {
          if (!g) return
          try {
            await del.mutateAsync({ publicId: g.publicId ?? '', body: { rowVersion: g.rowVersion ?? null } })
          } catch (err) {
            if (err instanceof ApiError && err.code === 'conflict') void qc.invalidateQueries({ queryKey: warehouseKeys.pickBatches })
            throw err
          }
          toast.success(t('warehouse.pickBatches.groups.deleted', { number: g.number ?? '' }))
          onClose()
        }}
        onClose={() => setDeleting(false)}
      />
    </>
  )
}
