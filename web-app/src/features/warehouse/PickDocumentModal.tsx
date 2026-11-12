// Lote 173 (C1) — ficha de un documento de «Recolección y empaque»: su avance arriba y sus recolecciones como movimientos (la más reciente primero); tocar una abre la ficha de la recolección
// (`PickBatchDetailModal`, con sus acciones Empacar y Eliminar).
import { useState } from 'react'
import { Can } from '../../kernel/access'
import { useLang, useT } from '../../kernel/i18n'
import { Chip, Modal } from '../../kernel/ui'
import { useFormat } from '../../kernel/format'
import type { PickBatchSummaryDto, PickDocumentDto } from './api'
import { formatDateTime } from './lineRules'
import { SalesDispatchFlow } from '../sales/SalesDispatchFlow'
import { useExitNotice } from './exitNoticeApi'
import { ExitNoticeDispatchModal } from './ExitNoticeDispatchModal'
import { PickBatchDetailModal } from './PickBatchDetailModal'
import { documentProgress, preloadFromDocument } from './pickDocumentView'

export function PickDocumentModal({ document: d, onClose, onCollect }: { document: PickDocumentDto; onClose: () => void; onCollect?: (d: PickDocumentDto) => void }) {
  const t = useT()
  const lang = useLang()
  const f = useFormat()
  const [batch, setBatch] = useState<string | null>(null)
  const [dispatching, setDispatching] = useState(false)
  const p = documentProgress(d)
  const moves = d.batches ?? []
  const lines = d.lines ?? []
  const canCollect = Boolean(onCollect) && preloadFromDocument(d, 0) !== null
  // Lote 242: despachar (sacar contra el documento) desde aquí; solo órdenes de venta y avisos de salida con algo reservado por sacar
  const canDispatch = (d.kind === 'SALES_ORDER' || d.kind === 'EXIT_NOTICE') && p.toCollect > 0

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
        dismissible={batch === null && !dispatching}
        footer={
          <>
            <button type="button" className="btn" onClick={onClose}>
              {t('warehouse.pickBatches.detail.close')}
            </button>
            {canCollect && (
              <button type="button" className="btn" onClick={() => onCollect?.(d)}>
                {t('warehouse.pickBatches.docs.collectThis')}
              </button>
            )}
            {canDispatch && (
              <Can perm="warehouse.pick">
                <button type="button" className="btn flow" onClick={() => setDispatching(true)}>
                  {t('warehouse.pickBatches.docs.dispatch')}
                </button>
              </Can>
            )}
          </>
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

        {d.kind !== 'ORDER' && (
          <>
            <h3 className="pdoc-h">{t('warehouse.pickBatches.docs.linesTitle')}</h3>
            {lines.length === 0 ? (
              <p className="help">{t('warehouse.pickBatches.docs.noLines')}</p>
            ) : (
              <table className="lst">
                <thead>
                  <tr>
                    <th>{t('warehouse.pickBatches.docs.colProduct')}</th>
                    <th className="r">{t('warehouse.pickBatches.docs.ordered')}</th>
                    <th className="r">{t('warehouse.pickBatches.docs.shipped')}</th>
                    <th className="r">{t('warehouse.pickBatches.docs.colToCollect')}</th>
                    <th>{t('warehouse.pickBatches.docs.colFrom')}</th>
                  </tr>
                </thead>
                <tbody>
                  {lines.map((l, i) => (
                    <tr key={`${l.productPublicId}-${i}`}>
                      <td>
                        <span className="ref">{l.sku}</span> {l.productName}
                        {l.kitSku && <> <Chip tone="cap">{t('warehouse.pickBatches.docs.kitOf', { sku: l.kitSku })}</Chip></>}
                      </td>
                      <td className="r">{l.ordered != null ? f.number(l.ordered) : '—'}</td>
                      <td className="r">{f.number(l.shipped ?? 0)}</td>
                      <td className="r">{f.number(l.reserved ?? 0)}</td>
                      <td>
                        {(l.locations ?? []).length === 0 ? '—' : (l.locations ?? []).map((x) => (
                          <div key={`${x.binId}-${x.lotId ?? 0}`}>
                            <span className="ref">{x.binCode}</span>
                            {x.lotNumber ? ` · ${t('warehouse.pickBatches.docs.lot', { lot: x.lotNumber })}` : ''} · {f.number(x.quantity ?? 0)}
                          </div>
                        ))}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </>
        )}

        {canDispatch && lines.length > 0 && <p className="help">{t('warehouse.pickBatches.docs.dispatchHelp')}</p>}
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
                    {m.groupNumber && <> <Chip tone="cap">{t('warehouse.pickBatches.docs.master', { number: m.groupNumber })}</Chip></>}
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
      {dispatching && d.kind === 'SALES_ORDER' && <SalesDispatchFlow orderPublicId={d.publicId} onClose={() => setDispatching(false)} onDone={() => { setDispatching(false); onClose() }} />}
      {dispatching && d.kind === 'EXIT_NOTICE' && <ExitNoticeDispatchFromDocument publicId={d.publicId} onClose={() => setDispatching(false)} onDone={() => { setDispatching(false); onClose() }} />}
    </>
  )
}

/** Lote 242: el aviso de salida del documento, ya cargado, en su ventana de despacho de siempre (Llegadas y salidas → Salidas → Despachar). */
function ExitNoticeDispatchFromDocument({ publicId, onClose, onDone }: { publicId: string; onClose: () => void; onDone: () => void }) {
  const notice = useExitNotice(publicId)
  if (!notice.data) return null
  return <ExitNoticeDispatchModal notice={notice.data} onClose={onClose} onDone={onDone} />
}
