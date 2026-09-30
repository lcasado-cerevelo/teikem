// Lote 14 (P7, D5) — detalle de un descuadre Kárdex ↔ saldo (`GET /inventory/discrepancies/{publicId}`, inventory.view) y su
// resolución (`POST .../resolve`, inventory.adjust): "Corregir el saldo según el Kárdex" (REBUILD_BALANCE, solo descuadres
// por posición: el saldo toma lo que da el Kárdex, sin movimiento nuevo) o "Descartar" (DISMISS, nota obligatoria). Si al
// corregir ya cuadraba, el API lo cierra como "Se corrigió solo" y aquí se avisa. Después de corregir se ofrece "Crear
// conteo de esa posición" (`POST /cycle-counts` con `binIds`, warehouse.count.capture) para verificar lo físico. Los
// mensajes del API (400/409/422) se muestran tal cual. Lo abre `?discrepancy=<publicId>` en la pestaña Conciliación del
// Kárdex (así llega el "Revisar" de "Necesita tu atención").
import { useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { Can, useCan } from '../../kernel/access'
import type { components } from '../../kernel/api/schema'
import { StatusChip } from '../../kernel/catalogs'
import { useLang, useT } from '../../kernel/i18n'
import { DataTable, Modal, Spinner, toast } from '../../kernel/ui'
import { useCreateCycleCount, useInventoryDiscrepancy, useResolveDiscrepancy, type InventoryDiscrepancyDto, type KardexRowDto } from './api'
import { useKardexColumns } from './kardexColumns'
import { formatSignedQty, qtyClass, sideText } from './kardexView'
import { formatDateTime, formatNumber } from './lineRules'
import { DISCREPANCY_NOTES_MAX, problemText } from './problemText'
import './warehouse.css'

type StatusHistoryDto = components['schemas']['StatusHistoryDto']

export type DiscrepancyAction = 'REBUILD_BALANCE' | 'DISMISS'

export interface DiscrepancyModalProps {
  publicId: string | null
  onClose: () => void
  /** Clic en un movimiento reciente: abre su detalle. */
  onOpenTxn?: (id: number) => void
}

export function DiscrepancyModal({ publicId, onClose, onOpenTxn }: DiscrepancyModalProps) {
  const t = useT()
  const { data, isLoading, error } = useInventoryDiscrepancy(publicId, { handleAccessDenied: false })
  const d = data?.discrepancy
  return (
    <Modal
      open={publicId != null}
      size="lg"
      title={t('warehouse.discrepancies.detail.title', { sku: d?.sku ?? '' })}
      onClose={onClose}
      footer={
        <button type="button" className="btn" onClick={onClose}>
          {t('warehouse.kardexDetail.close')}
        </button>
      }
    >
      {isLoading && <Spinner block label={t('common.loading')} />}
      {error && (
        <p className="ferr" role="alert">
          {error.message}
        </p>
      )}
      {data && d && (
        <DiscrepancyBody
          key={`${d.publicId}-${d.statusCode}`}
          d={d}
          reserved={data.currentReserved ?? null}
          movements={data.recentMovements ?? []}
          history={data.history ?? []}
          onOpenTxn={onOpenTxn}
        />
      )}
    </Modal>
  )
}

function DiscrepancyBody({
  d,
  reserved,
  movements,
  history,
  onOpenTxn,
}: {
  d: InventoryDiscrepancyDto
  reserved: number | null
  movements: KardexRowDto[]
  history: StatusHistoryDto[]
  onOpenTxn?: (id: number) => void
}) {
  const t = useT()
  const lang = useLang()
  const canAdjust = useCan('inventory.adjust')
  const resolve = useResolveDiscrepancy()
  const createCount = useCreateCycleCount()
  const [notes, setNotes] = useState('')
  const [problem, setProblem] = useState<string | null>(null)
  const [busy, setBusy] = useState<DiscrepancyAction | 'count' | null>(null)
  const [countCreated, setCountCreated] = useState<{ id: number; number: string } | null>(null)
  const columns = useKardexColumns(['date', 'time', 'type', 'qty', 'position', 'lotSerial', 'reason', 'user'])

  const isOpen = d.statusCode === 'OPEN'
  const isBalance = d.kindCode === 'BALANCE'
  const where = sideText(d.warehouseCode, d.binCode) || t('warehouse.discrepancies.productTotal')
  const dash = '—'

  const facts = useMemo(
    () => [
      { key: 'product', label: t('warehouse.discrepancies.columns.product'), value: `${d.sku ?? ''} · ${d.productName ?? ''}` },
      { key: 'where', label: t('warehouse.discrepancies.columns.where'), value: where },
      { key: 'lot', label: t('warehouse.discrepancies.columns.lot'), value: d.lotNumber || dash },
      { key: 'kind', label: t('warehouse.discrepancies.columns.kind'), value: d.kind ?? d.kindCode ?? dash },
      { key: 'ledger', label: t('warehouse.discrepancies.columns.ledgerQty'), value: formatNumber(d.ledgerQty, lang) },
      { key: 'balance', label: t('warehouse.discrepancies.columns.balanceQty'), value: formatNumber(d.balanceQty, lang) },
      { key: 'reserved', label: t('warehouse.discrepancies.detail.reserved'), value: reserved != null ? formatNumber(reserved, lang) : dash },
      { key: 'trigger', label: t('warehouse.discrepancies.columns.trigger'), value: d.trigger ?? d.triggerCode ?? dash },
      { key: 'detected', label: t('warehouse.discrepancies.columns.detected'), value: formatDateTime(d.detectedAtUtc, lang) },
      {
        key: 'checked',
        label: t('warehouse.discrepancies.detail.lastChecked'),
        value: t('warehouse.discrepancies.detail.checks', { date: formatDateTime(d.lastCheckedAtUtc, lang), count: d.checkCount ?? 1 }),
      },
    ],
    [d, t, lang, where, reserved],
  )

  const run = async (action: DiscrepancyAction) => {
    setProblem(null)
    const text = notes.trim()
    // mismo mensaje que el API (400 notes): descartar exige nota
    if (action === 'DISMISS' && !text) {
      setProblem(t('warehouse.discrepancies.errors.dismissNotes'))
      return
    }
    if (text.length > DISCREPANCY_NOTES_MAX) {
      setProblem(t('warehouse.discrepancies.errors.notesMax'))
      return
    }
    setBusy(action)
    try {
      const result = await resolve.mutateAsync({ publicId: d.publicId ?? '', body: { action, notes: text || null, rowVersion: d.rowVersion } })
      const status = result.discrepancy?.statusCode
      if (status === 'SELF_CORRECTED') toast.info(t('warehouse.discrepancies.selfCorrected'))
      else if (action === 'DISMISS') toast.success(t('warehouse.discrepancies.dismissed'))
      else toast.success(t('warehouse.discrepancies.rebuilt'))
    } catch (err) {
      setProblem(problemText(err))
    } finally {
      setBusy(null)
    }
  }

  const createPositionCount = async () => {
    if (d.binId == null) return
    setProblem(null)
    setBusy('count')
    try {
      const count = await createCount.mutateAsync({ warehousePublicId: d.warehousePublicId ?? null, binIds: [d.binId] })
      const header = count.count
      setCountCreated({ id: header?.id ?? 0, number: header?.number ?? '' })
      toast.success(t('warehouse.discrepancies.countCreated', { number: header?.number ?? '' }))
    } catch (err) {
      setProblem(problemText(err))
    } finally {
      setBusy(null)
    }
  }

  return (
    <>
      <div className="kx-doc">
        <div className="kx-doc-main">
          <small className="kx-ref">{t('warehouse.discrepancies.columns.difference')}</small>
          <b className={`mono ${qtyClass(d.difference)}`}>{formatSignedQty(d.difference, lang)}</b>
          <span className="kx-doc-meta">{t('warehouse.discrepancies.detail.differenceHelp')}</span>
        </div>
        <StatusChip domain="InventoryDiscrepancyStatus" code={d.statusCode} label={d.status} />
      </div>
      <dl className="kx-facts">
        {facts.map((f) => (
          <div key={f.key}>
            <dt>{f.label}</dt>
            <dd>{f.value}</dd>
          </div>
        ))}
      </dl>
      <p className="note">{t('warehouse.discrepancies.detail.ledgerRules')}</p>

      {!isOpen && (
        <dl className="kx-facts">
          <div>
            <dt>{t('warehouse.discrepancies.detail.closedAt')}</dt>
            <dd>{d.closedAtUtc ? formatDateTime(d.closedAtUtc, lang) : dash}</dd>
          </div>
          <div>
            <dt>{t('warehouse.discrepancies.detail.resolvedBy')}</dt>
            <dd>{d.resolvedByName || t('warehouse.kardexDetail.system')}</dd>
          </div>
          {d.correctedToQty != null && (
            <div>
              <dt>{t('warehouse.discrepancies.detail.corrected')}</dt>
              <dd className="mono">
                {formatNumber(d.correctedFromQty, lang)} → {formatNumber(d.correctedToQty, lang)}
              </dd>
            </div>
          )}
          <div style={{ gridColumn: '1 / -1' }}>
            <dt>{t('warehouse.discrepancies.detail.notes')}</dt>
            <dd>{d.resolutionNotes || dash}</dd>
          </div>
        </dl>
      )}

      {problem && (
        <p className="ferr" role="alert">
          {problem}
        </p>
      )}

      {isOpen && canAdjust && (
        <div className="f">
          <label htmlFor="disc-notes">{t('warehouse.discrepancies.detail.notesLabel')}</label>
          <textarea
            id="disc-notes"
            rows={2}
            maxLength={DISCREPANCY_NOTES_MAX}
            value={notes}
            placeholder={t('warehouse.discrepancies.detail.notesPlaceholder')}
            onChange={(e) => setNotes(e.target.value)}
          />
          <div className="disc-actions">
            {isBalance && (
              <button type="button" className="btn flow" disabled={busy !== null} onClick={() => run('REBUILD_BALANCE')}>
                {busy === 'REBUILD_BALANCE' ? t('common.loading') : t('warehouse.discrepancies.rebuild')}
              </button>
            )}
            <button type="button" className="btn" disabled={busy !== null} onClick={() => run('DISMISS')}>
              {busy === 'DISMISS' ? t('common.loading') : t('warehouse.discrepancies.dismiss')}
            </button>
          </div>
          {!isBalance && <p className="help">{t('warehouse.discrepancies.detail.totalHelp')}</p>}
        </div>
      )}

      {d.statusCode === 'RESOLVED' && d.binId != null && (
        <Can perm="warehouse.count.capture">
          <div className="note">
            <p>{t('warehouse.discrepancies.detail.countOffer')}</p>
            <div className="disc-actions">
              {countCreated ? (
                <Link className="btn sm flow" to={`/warehouse/cycle-counts?count=${countCreated.id}`}>
                  {t('warehouse.discrepancies.openCount', { number: countCreated.number })}
                </Link>
              ) : (
                <button type="button" className="btn sm flow" disabled={busy !== null} onClick={createPositionCount}>
                  {busy === 'count' ? t('common.loading') : t('warehouse.discrepancies.createCount')}
                </button>
              )}
            </div>
          </div>
        </Can>
      )}

      <h3 className="kx-h3">{t('warehouse.discrepancies.detail.movements')}</h3>
      <DataTable
        label={t('warehouse.discrepancies.detail.movements')}
        columns={columns}
        rows={movements}
        rowKey={(r) => r.id ?? 0}
        pageSize={10}
        exportable={false}
        onRowClick={onOpenTxn ? (r) => r.id != null && onOpenTxn(r.id) : undefined}
        dense
      />
      <h3 className="kx-h3">{t('warehouse.discrepancies.detail.history')}</h3>
      {history.length > 0 ? (
        <ul className="disc-history">
          {history.map((h) => (
            <li key={h.id}>
              {formatDateTime(h.changedAtUtc, lang)} — {h.fromLabel ?? h.fromCode ?? '—'} → {h.toLabel ?? h.toCode} ({h.changedByName || t('warehouse.kardexDetail.system')})
              {h.comment ? ` · ${h.comment}` : ''}
            </li>
          ))}
        </ul>
      ) : (
        <p className="help">—</p>
      )}
    </>
  )
}
