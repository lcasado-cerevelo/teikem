// Lote 249 — paso «Líneas» de la modificación: por cada línea de la orden, su posición (lo recolectado, lo empacado, lo que ya salió y lo facturado), el MÍNIMO al que
// se puede bajar y, si algo la bloquea, el motivo EXACTO; cantidad, precio (sales.price, con el de lista al lado) e IVU manual (sales.tax, con motivo); agregar y quitar.
import { useCan } from '../../../kernel/access'
import { useFormat } from '../../../kernel/format'
import { useLang, useT } from '../../../kernel/i18n'
import { Chip } from '../../../kernel/ui'
import { ProductPicker } from '../../warehouse/pickers'
import type { SalesOrderDto } from '../salesApi'
import { isLineLocked, lineError, minQuantity, serverText, type AddedLine, type ChangeDraft, type LineEdit } from './changeRules'
import type { EditOptionsDto } from './changesApi'
import './changes.css'

let nextAddedKey = 1

export function ChangeLinesEditor({ order, opts, draft, onDraft, reopen, allowBackorder, onAllowBackorder }: {
  order: SalesOrderDto
  opts: EditOptionsDto
  draft: ChangeDraft
  onDraft: (next: (d: ChangeDraft) => ChangeDraft) => void
  /** Se va a reabrir el empaque: el mínimo baja a lo que ya salió. */
  reopen: boolean
  allowBackorder: boolean
  onAllowBackorder: (v: boolean) => void
}) {
  const t = useT()
  const lang = useLang()
  const f = useFormat()
  const canPrice = useCan('sales.price')
  const canTax = useCan('sales.tax')
  const orderLines = order.lines ?? []
  const edit = (lineId: number, patch: Partial<LineEdit>) =>
    onDraft((d) => ({ ...d, edits: { ...d.edits, [lineId]: { ...d.edits[lineId], ...patch } } }))
  const setAdded = (key: number, patch: Partial<AddedLine>) => onDraft((d) => ({ ...d, added: d.added.map((a) => (a.key === key ? { ...a, ...patch } : a)) }))

  return (
    <div className="soc-form">
      <ul className="soc-lines" aria-label={t('salesChanges.lines.title')}>
        {(opts.lines ?? []).map((pos) => {
          const id = pos.lineId ?? 0
          const e = draft.edits[id]
          const orderLine = orderLines.find((l) => l.id === id)
          const locked = isLineLocked(pos)
          const priceLocked = Boolean(pos.priceLockedReason)
          const removed = e?.removed === true
          const err = lineError(pos, e, reopen)
          const listPrice = orderLine?.resolvedUnitPrice ?? null
          const hasTax = pos.hasTaxOverride === true && !e?.clearTax
          const taxOpen = e?.taxPct !== undefined
          return (
            <li key={id} className={`soc-line${removed || pos.isRemoved ? ' removed' : ''}${locked ? ' locked' : ''}`}>
              <div className="soc-line-head">
                <span className="soc-line-name">
                  <b className="mono">{pos.sku}</b> <span className={removed || pos.isRemoved ? 'soc-struck' : undefined}>{pos.name}</span>
                </span>
                <span>
                  {pos.isRemoved && <Chip tone="neutral">{t('salesChanges.line.removed')}</Chip>}
                  {pos.hasTaxOverride && <Chip tone="cod">{t('salesChanges.line.taxManual', { pct: f.number(pos.taxRatePct ?? 0) })}</Chip>}
                </span>
              </div>
              <div className="soc-line-pos">
                <span>{t('salesChanges.line.ordered')} <b>{f.number(pos.qtyOrdered ?? 0)}</b></span>
                <span>{t('salesChanges.line.collected')} <b>{f.number(pos.inCollected ?? 0)}</b></span>
                <span>{t('salesChanges.line.packed')} <b>{f.number(pos.inPacked ?? 0)}</b></span>
                <span>{t('salesChanges.line.out')} <b>{f.number(pos.out ?? 0)}</b></span>
                <span>{t('salesChanges.line.invoiced')} <b>{f.number(pos.invoiced ?? 0)}</b></span>
              </div>
              {locked ? (
                <p className="help" role="note">{serverText(t, lang, pos.lockedReason) || t('salesChanges.line.removedHelp')}</p>
              ) : (
                <>
                  <div className="soc-line-grid">
                    <div className="f">
                      <label htmlFor={`soc-qty-${id}`}>{t('salesChanges.line.qty')}</label>
                      <input
                        id={`soc-qty-${id}`}
                        inputMode="decimal"
                        value={e?.quantity ?? String(pos.qtyOrdered ?? '')}
                        disabled={removed}
                        aria-invalid={err === 'quantity' || err === 'belowMin'}
                        onChange={(ev) => edit(id, { quantity: ev.target.value })}
                      />
                      <p className={err === 'belowMin' || err === 'quantity' ? 'help ferr' : 'help'}>
                        {err === 'quantity' ? t('salesChanges.line.errors.quantity') : t('salesChanges.line.min', { min: f.number(minQuantity(pos, reopen)) })}
                      </p>
                    </div>
                    {canPrice && !priceLocked && (
                      <div className="f">
                        <label htmlFor={`soc-price-${id}`}>{t('salesChanges.line.price')}</label>
                        <input
                          id={`soc-price-${id}`}
                          inputMode="decimal"
                          value={e?.price ?? String(pos.unitPrice ?? '')}
                          disabled={removed}
                          aria-invalid={err === 'price'}
                          onChange={(ev) => edit(id, { price: ev.target.value })}
                        />
                        <p className={err === 'price' ? 'help ferr' : 'soc-listprice'}>
                          {err === 'price' ? t('salesChanges.line.errors.price') : listPrice !== null ? t('salesChanges.line.listPrice', { price: f.money(listPrice) }) : ' '}
                        </p>
                      </div>
                    )}
                    {canTax && !priceLocked && taxOpen && (
                      <>
                        <div className="f">
                          <label htmlFor={`soc-tax-${id}`}>{t('salesChanges.line.taxPct')}</label>
                          <input
                            id={`soc-tax-${id}`}
                            inputMode="decimal"
                            value={e?.taxPct ?? ''}
                            placeholder={hasTax ? String(pos.taxRatePct ?? '') : ''}
                            disabled={removed}
                            aria-invalid={err === 'taxPct'}
                            onChange={(ev) => edit(id, { taxPct: ev.target.value })}
                          />
                          {err === 'taxPct' && <p className="help ferr">{t('salesChanges.line.errors.taxPct')}</p>}
                        </div>
                        <div className="f">
                          <label htmlFor={`soc-taxnote-${id}`}>{t('salesChanges.line.taxNote')}</label>
                          <input id={`soc-taxnote-${id}`} value={e?.taxNote ?? ''} maxLength={250} autoComplete="off" disabled={removed} aria-invalid={err === 'taxNote'} onChange={(ev) => edit(id, { taxNote: ev.target.value })} />
                          {err === 'taxNote' && <p className="help ferr">{t('salesChanges.line.errors.taxNote')}</p>}
                        </div>
                      </>
                    )}
                  </div>
                  {priceLocked && canPrice && <p className="help">{serverText(t, lang, pos.priceLockedReason)}</p>}
                  <div className="soc-line-acts">
                    {canTax && !priceLocked && !removed && (
                      taxOpen ? (
                        <button type="button" className="btn sm" onClick={() => edit(id, { taxPct: undefined, taxNote: undefined })}>{t('salesChanges.line.taxCancel')}</button>
                      ) : e?.clearTax ? (
                        <button type="button" className="btn sm" onClick={() => edit(id, { clearTax: false })}>{t('salesChanges.line.taxClearUndo')}</button>
                      ) : (
                        <>
                          <button type="button" className="btn sm" onClick={() => edit(id, { taxPct: '', taxNote: '' })}>{hasTax ? t('salesChanges.line.taxChange') : t('salesChanges.line.taxSet')}</button>
                          {hasTax && <button type="button" className="btn sm" onClick={() => edit(id, { clearTax: true })}>{t('salesChanges.line.taxClear')}</button>}
                        </>
                      )
                    )}
                    {(pos.out ?? 0) > 0 ? (
                      <span className="help">{t('salesChanges.line.cannotRemove')}</span>
                    ) : (
                      <button type="button" className="btn sm danger" onClick={() => edit(id, { removed: !removed })}>
                        {removed ? t('salesChanges.line.undoRemove') : t('salesChanges.line.remove')}
                      </button>
                    )}
                  </div>
                </>
              )}
            </li>
          )
        })}
      </ul>

      {draft.added.length > 0 && (
        <ul className="soc-lines" aria-label={t('salesChanges.lines.added')}>
          {draft.added.map((a) => (
            <li key={a.key} className="soc-line">
              <div className="soc-line-head">
                <span className="soc-line-name"><Chip tone="deliv">{t('salesChanges.diff.ADDED')}</Chip> <b className="mono">{a.sku}</b> {a.name}</span>
                <button type="button" className="btn sm" aria-label={t('salesChanges.lines.removeAdded', { sku: a.sku })} onClick={() => onDraft((d) => ({ ...d, added: d.added.filter((x) => x.key !== a.key) }))}>✕</button>
              </div>
              <div className="soc-line-grid">
                <div className="f">
                  <label htmlFor={`soc-addqty-${a.key}`}>{t('salesChanges.line.qty')}</label>
                  <input id={`soc-addqty-${a.key}`} inputMode="decimal" value={a.quantity} onChange={(ev) => setAdded(a.key, { quantity: ev.target.value })} />
                </div>
                {canPrice && (
                  <div className="f">
                    <label htmlFor={`soc-addprice-${a.key}`}>{t('salesChanges.line.price')}</label>
                    <input id={`soc-addprice-${a.key}`} inputMode="decimal" value={a.price} placeholder={a.listPrice != null ? String(a.listPrice) : t('salesChanges.lines.priceToday')} onChange={(ev) => setAdded(a.key, { price: ev.target.value })} />
                    <p className="soc-listprice">{a.listPrice != null ? t('salesChanges.line.listPrice', { price: f.money(a.listPrice) }) : t('salesChanges.lines.priceTodayHelp')}</p>
                  </div>
                )}
              </div>
            </li>
          ))}
        </ul>
      )}

      <div className="f">
        <label htmlFor="soc-add-product">{t('salesChanges.lines.addProduct')}</label>
        <ProductPicker
          id="soc-add-product"
          ownOnly
          value={null}
          placeholder={t('salesChanges.lines.addPlaceholder')}
          onChange={(productId, p) => {
            if (!productId || !p) return
            onDraft((d) => ({
              ...d,
              added: d.added.some((a) => a.productPublicId === productId)
                ? d.added
                : [...d.added, { key: nextAddedKey++, productPublicId: productId, sku: p.sku ?? '', name: p.name ?? '', listPrice: p.salePrice ?? null, quantity: '1', price: '' }],
            }))
          }}
        />
        <p className="help">{t('salesChanges.lines.addHelp')}</p>
      </div>

      <label className="sw">
        <input type="checkbox" role="switch" checked={allowBackorder} onChange={(e) => onAllowBackorder(e.target.checked)} />
        <span className="tk" aria-hidden="true" />
        <span>{t('salesChanges.lines.allowBackorder')}</span>
      </label>
    </div>
  )
}
