// Pieza "Recolección y empaque" — cuerpo de la ficha de una recolección (Lote 13: extraído de la ficha para usarlo también
// en el modal de la lista). `PickBatchDetailBody`: etapas del estatus (solo lectura: COLLECTED → PACKED → CANCELLED se
// disparan desde Empacar y Eliminar), resumen (recolectada, empacada, orden, factura, cantidad, costo) y líneas.
// `PickBatchDetailActions`: botones Eliminar (warehouse.pick; si está PACKED además orders.cancel; solo con `canDelete`) y
// Empacar (warehouse.pick + orders.create; solo con `canPack`). Los usan `PickBatchDetailScreen` (ruta
// `/warehouse/pick-batches/:publicId`) y `PickBatchDetailModal` (clic en una fila de la lista). Manual 06 §7.
import { useMemo, type ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { Can } from '../../kernel/access'
import type { components } from '../../kernel/api/schema'
import { StatusPipeline } from '../../kernel/catalogs'
import { useLang, useT } from '../../kernel/i18n'
import { Chip, DataTable, Panel, type DataColumn } from '../../kernel/ui'
import { IconBasket } from '../../kernel/ui/screenIcons'
import { productLabel, type PickBatchDto } from './api'
import { formatDateTime, formatMoneyValue, formatNumber } from './lineRules'
import { PICK_BATCH_STATUS_DOMAIN, usePickBatchCanDelete } from './pickBatchView'
import './warehouse.css'

type PickLine = components['schemas']['PickBatchLineDto']

const ENTITY_TYPE = 'PICK_BATCH'

export interface PickBatchDetailActionsProps {
  batch: PickBatchDto
  onPack: () => void
  onDelete: () => void
}

/** Eliminar y Empacar, con sus guardas de permiso y de estatus. */
export function PickBatchDetailActions({ batch, onPack, onDelete }: PickBatchDetailActionsProps) {
  const t = useT()
  const canDelete = usePickBatchCanDelete()
  return (
    <>
      <Can perm="warehouse.pick">
        {canDelete(batch) && (
          <button type="button" className="btn" onClick={onDelete}>
            {t('warehouse.pickBatches.detail.delete')}
          </button>
        )}
      </Can>
      <Can perm={['warehouse.pick', 'orders.create']}>
        {batch.canPack && (
          <button type="button" className="btn flow" onClick={onPack}>
            {t('warehouse.pickBatches.detail.pack')}
          </button>
        )}
      </Can>
    </>
  )
}

/** En pantalla, un `Panel`; dentro del modal, una sección con título (no se anidan paneles en un modal). */
function Section({ variant, title, badge, flush, children }: { variant: 'screen' | 'modal'; title: string; badge?: number; flush?: boolean; children: ReactNode }) {
  if (variant === 'screen')
    return (
      <Panel flush={flush} icon={<IconBasket />} title={title} badge={badge}>
        {children}
      </Panel>
    )
  return (
    <section className="collect-sec">
      <h3>
        {title}
        {badge != null && <span className="collect-sec-n">{badge}</span>}
      </h3>
      {children}
    </section>
  )
}

export interface PickBatchDetailBodyProps {
  batch: PickBatchDto
  /** 'screen' = paneles de la ficha; 'modal' = secciones dentro del modal de la lista. */
  variant?: 'screen' | 'modal'
}

export function PickBatchDetailBody({ batch, variant = 'screen' }: PickBatchDetailBodyProps) {
  const t = useT()
  const lang = useLang()
  const lines = useMemo(() => batch.lines ?? [], [batch.lines])
  const columns = useMemo<DataColumn<PickLine>[]>(
    () => [
      { id: 'product', header: t('warehouse.pickBatches.detail.product'), cell: (l) => productLabel({ sku: l.sku, name: l.productName }), sortValue: (l) => l.sku, card: 'title' },
      { id: 'qty', header: t('warehouse.pickBatches.detail.quantity'), cell: (l) => formatNumber(l.quantity, lang), sortValue: (l) => l.quantity ?? 0, align: 'end' },
      { id: 'bin', header: t('warehouse.pickBatches.detail.bin'), cell: (l) => l.binCode ?? '—', sortValue: (l) => l.binCode },
      { id: 'lot', header: t('warehouse.pickBatches.detail.lot'), cell: (l) => l.lotNumber ?? '—', sortValue: (l) => l.lotNumber },
      { id: 'serial', header: t('warehouse.pickBatches.detail.serial'), cell: (l) => l.serialNumber ?? '—', sortValue: (l) => l.serialNumber },
      { id: 'cost', header: t('warehouse.pickBatches.detail.unitCost'), cell: (l) => (l.unitCost != null ? formatMoneyValue(l.unitCost, lang, { unitPrice: true }) : '—'), exportValue: (l) => l.unitCost, align: 'end', card: 'hidden', sortValue: (l) => l.unitCost },
      {
        id: 'reversed',
        header: t('warehouse.pickBatches.detail.reversal'),
        cell: (l) => (l.reversalTxnId != null ? <Chip tone="warn">{t('warehouse.pickBatches.detail.reversed')}</Chip> : '—'),
        sortValue: (l) => l.reversalTxnId != null,
      },
    ],
    [t, lang],
  )

  return (
    <div className={variant === 'modal' ? 'collect-detail modal' : 'collect-detail'}>
      <div className="collect-detail-pipe">
        {/* Sin transición manual: PACKED y CANCELLED se disparan desde Empacar y Eliminar. */}
        <StatusPipeline domain={PICK_BATCH_STATUS_DOMAIN} entityType={ENTITY_TYPE} entityId={batch.id} currentCode={batch.statusCode} />
      </div>

      <Section variant={variant} title={t('warehouse.pickBatches.detail.summary')}>
        <div className="r3">
          <div className="f">
            <span className="collect-lbl">{t('warehouse.pickBatches.columns.collectedAt')}</span>
            <p>
              {formatDateTime(batch.collectedAtUtc, lang)}
              {batch.collectedBy ? ` · ${batch.collectedBy}` : ''}
            </p>
          </div>
          <div className="f">
            <span className="collect-lbl">{t('warehouse.pickBatches.detail.packedAt')}</span>
            <p>{formatDateTime(batch.packedAtUtc, lang) || '—'}</p>
          </div>
          <div className="f">
            <span className="collect-lbl">{t('warehouse.pickBatches.detail.order')}</span>
            <p>
              {batch.orderPublicId ? (
                <Can perm="orders.view" fallback={<span className="ref">{batch.orderNumber}</span>}>
                  <Link className="ref" to={`/orders/${batch.orderPublicId}`}>
                    {batch.orderNumber}
                  </Link>
                </Can>
              ) : (
                '—'
              )}
              {batch.orderStatus ? ` · ${batch.orderStatus}` : ''}
            </p>
          </div>
          <div className="f">
            <span className="collect-lbl">{t('warehouse.pickBatches.detail.invoice')}</span>
            <p>{batch.clientInvoiceNumber ?? '—'}</p>
          </div>
          <div className="f">
            <span className="collect-lbl">{t('warehouse.pickBatches.columns.totalQty')}</span>
            <p>{formatNumber(batch.totalQty, lang)}</p>
          </div>
          <div className="f">
            <span className="collect-lbl">{t('warehouse.pickBatches.detail.totalCost')}</span>
            <p>{batch.totalCost != null ? formatMoneyValue(batch.totalCost, lang) : '—'}</p>
          </div>
        </div>
      </Section>

      <Section variant={variant} title={t('warehouse.pickBatches.detail.lines')} badge={lines.length} flush>
        <DataTable
          label={t('warehouse.pickBatches.detail.lines')}
          columns={columns}
          rows={lines}
          rowKey={(l) => l.id ?? 0}
          pageSize={50}
          exportable={variant === 'screen'}
        />
      </Section>
    </div>
  )
}
