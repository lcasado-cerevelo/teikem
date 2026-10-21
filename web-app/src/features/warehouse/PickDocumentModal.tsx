// Lote 173 (C1) — ficha de un documento de «Recolección y empaque»: su avance arriba y sus recolecciones como movimientos (la más reciente primero); tocar una abre la ficha de la recolección
// (`PickBatchDetailModal`, con sus acciones Empacar y Eliminar).
import { useState } from 'react'
import { useLang, useT } from '../../kernel/i18n'
import { Chip, Modal } from '../../kernel/ui'
import { useFormat } from '../../kernel/format'
import type { PickBatchSummaryDto, PickDocumentDto } from './api'
import { formatDateTime } from './lineRules'
import { PickBatchDetailModal } from './PickBatchDetailModal'
import { documentProgress } from './pickDocumentView'

export function PickDocumentModal({ document: d, onClose }: { document: PickDocumentDto; onClose: () => void }) {
  const t = useT()
  const lang = useLang()
  const f = useFormat()
  const [batch, setBatch] = useState<string | null>(null)
  const p = documentProgress(d)
  const moves = d.batches ?? []

  return (
    <>
      <Modal
        open
        size="lg"
        title={
          <span className="collect-dtitle">
            <span className="ref">{d.number}</span> <Chip tone="cap">{t(`warehouse.pickBatches.docs.kinds.${d.kind}`)}</Chip> <Chip>{d.status}</Chip>
          </span>
        }
        onClose={onClose}
        dismissible={batch === null}
        footer={
          <button type="button" className="btn" onClick={onClose}>
            {t('warehouse.pickBatches.detail.close')}
          </button>
        }
      >
        <dl className="rcp-meta">
          <div>
            <dt>{t('warehouse.pickBatches.docs.columns.client')}</dt>
            <dd>{d.clientName ?? '—'}</dd>
          </div>
          {p.ordered != null && (
            <div>
              <dt>{t('warehouse.pickBatches.docs.ordered')}</dt>
              <dd>{f.number(p.ordered)}</dd>
            </div>
          )}
          <div>
            <dt>{t('warehouse.pickBatches.docs.shipped')}</dt>
            <dd>{f.number(p.shipped)}</dd>
          </div>
          {p.ordered != null && (
            <div>
              <dt>{t('warehouse.pickBatches.docs.reserved')}</dt>
              <dd>{f.number(p.toCollect)}</dd>
            </div>
          )}
          {p.unreserved > 0 && (
            <div>
              <dt>{t('warehouse.pickBatches.docs.unreservedLabel')}</dt>
              <dd>{f.number(p.unreserved)}</dd>
            </div>
          )}
        </dl>
        {p.percent != null && <progress className="pdoc-bar" max={100} value={p.percent} aria-label={t('warehouse.pickBatches.docs.percent', { n: p.percent })} />}

        <h3 className="pdoc-h">{t('warehouse.pickBatches.docs.movesTitle', { count: moves.length })}</h3>
        {moves.length === 0 ? (
          <p className="help">{t('warehouse.pickBatches.docs.noMoves')}</p>
        ) : (
          <table className="lst">
            <thead>
              <tr>
                <th>{t('warehouse.pickBatches.columns.number')}</th>
                <th>{t('warehouse.pickBatches.columns.collectedAt')}</th>
                <th>{t('warehouse.pickBatches.docs.by')}</th>
                <th className="r">{t('warehouse.pickBatches.docs.qty')}</th>
              </tr>
            </thead>
            <tbody>
              {moves.map((m: PickBatchSummaryDto) => (
                <tr key={m.publicId} className="pdoc-move" tabIndex={0} onClick={() => setBatch(m.publicId ?? null)} onKeyDown={(e) => e.key === 'Enter' && setBatch(m.publicId ?? null)}>
                  <td>
                    <span className="ref">{m.number}</span> <Chip>{m.status}</Chip>
                  </td>
                  <td>{formatDateTime(m.collectedAtUtc, lang)}</td>
                  <td>{m.collectedBy ?? '—'}</td>
                  <td className="r">{f.number(m.totalQty ?? 0)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </Modal>
      <PickBatchDetailModal publicId={batch} onClose={() => setBatch(null)} />
    </>
  )
}
