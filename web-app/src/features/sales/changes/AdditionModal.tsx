// Lote 249 — «Orden interna de adición»: lo que se le agrega a una orden que ya no admite otro cambio (facturada o ya salió completa). Es una orden aparte que cuelga de la
// original, sin firma del cliente, con el mismo registro de quién lo autorizó; sale en el mismo envío y se factura con una factura complementaria.
import { useState } from 'react'
import { useCan } from '../../../kernel/access'
import { applyProblemDetails } from '../../../kernel/api/problem'
import { utcFromZonedInput, zonedInputFromUtc } from '../../../kernel/api/tenantZone'
import { useFormat } from '../../../kernel/format'
import { useLang, useT } from '../../../kernel/i18n'
import { Chip, Modal, Spinner, toast } from '../../../kernel/ui'
import { ProductPicker } from '../../warehouse/pickers'
import type { SalesOrderDto } from '../salesApi'
import { ChangeAuthorizationStep } from './ChangeAuthorizationStep'
import { authError, buildAdditionRequest, EMPTY_AUTH, serverText, type AddedLine, type AuthDraft } from './changeRules'
import { useChangeMutations, useEditOptions } from './changesApi'
import './changes.css'

let nextKey = 1

export function AdditionModal({ order, onClose }: { order: SalesOrderDto; onClose: () => void }) {
  const t = useT()
  const lang = useLang()
  const f = useFormat()
  const canPrice = useCan('sales.price')
  const canOverride = useCan('orders.credit_override')
  const opts = useEditOptions(order.publicId ?? null)
  const { addition } = useChangeMutations()
  const [lines, setLines] = useState<AddedLine[]>([])
  const [notes, setNotes] = useState('')
  const [auth, setAuth] = useState<AuthDraft>(() => ({ ...EMPTY_AUTH, at: zonedInputFromUtc(new Date(Date.now() + 59_000).toISOString()) }))
  const [allowBackorder, setAllowBackorder] = useState(false)
  const [override, setOverride] = useState(false)
  const [showErr, setShowErr] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const attachmentRequired = opts.data?.settings?.attachmentRequired === true
  const authErr = authError(auth, attachmentRequired)
  const setLine = (key: number, patch: Partial<AddedLine>) => setLines((ls) => ls.map((l) => (l.key === key ? { ...l, ...patch } : l)))
  const badQty = lines.some((l) => !(Number(l.quantity.replace(',', '.')) > 0))

  const save = async () => {
    setError(null)
    if (lines.length === 0) return setError(t('salesChanges.addition.noLines'))
    if (badQty) return setError(t('salesChanges.line.errors.quantity'))
    if (authErr) return setShowErr(true)
    try {
      const dto = await addition.mutateAsync({ orderId: order.publicId!, body: buildAdditionRequest(lines, notes, auth, utcFromZonedInput(auth.at), { allowBackorder, overrideCredit: override }) })
      toast.success(dto.item?.isPending ? t('salesChanges.addition.savedPending', { n: dto.item.changeNo ?? 0 }) : t('salesChanges.addition.saved', { number: dto.item?.additionNumber ?? '' }))
      onClose()
    } catch (e) {
      setError(serverText(t, lang, applyProblemDetails(e).title))
    }
  }

  return (
    <Modal
      open
      size="lg"
      title={t('salesChanges.addition.title', { number: order.number ?? '' })}
      onClose={onClose}
      dismissible={!addition.isPending}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose} disabled={addition.isPending}>{t('common.cancel')}</button>
          <button type="button" className="btn flow" onClick={() => void save()} disabled={addition.isPending || opts.isPending}>{addition.isPending ? t('common.loading') : t('salesChanges.addition.create')}</button>
        </>
      }
    >
      <div className="pb soc-form">
        <p className="soc-alert" role="note">{t('salesChanges.addition.help')}</p>
        {opts.isPending ? (
          <Spinner block />
        ) : (
          <>
            {lines.length > 0 && (
              <ul className="soc-lines" aria-label={t('salesChanges.lines.added')}>
                {lines.map((a) => (
                  <li key={a.key} className="soc-line">
                    <div className="soc-line-head">
                      <span className="soc-line-name"><b className="mono">{a.sku}</b> {a.name}</span>
                      <button type="button" className="btn sm" aria-label={t('salesChanges.lines.removeAdded', { sku: a.sku })} onClick={() => setLines((ls) => ls.filter((x) => x.key !== a.key))}>✕</button>
                    </div>
                    <div className="soc-line-grid">
                      <div className="f">
                        <label htmlFor={`soc-addn-qty-${a.key}`}>{t('salesChanges.line.qty')}</label>
                        <input id={`soc-addn-qty-${a.key}`} inputMode="decimal" value={a.quantity} onChange={(e) => setLine(a.key, { quantity: e.target.value })} />
                      </div>
                      {canPrice && (
                        <div className="f">
                          <label htmlFor={`soc-addn-price-${a.key}`}>{t('salesChanges.line.price')}</label>
                          <input id={`soc-addn-price-${a.key}`} inputMode="decimal" value={a.price} placeholder={a.listPrice != null ? String(a.listPrice) : t('salesChanges.lines.priceToday')} onChange={(e) => setLine(a.key, { price: e.target.value })} />
                          <p className="soc-listprice">{a.listPrice != null ? t('salesChanges.line.listPrice', { price: f.money(a.listPrice) }) : t('salesChanges.lines.priceTodayHelp')}</p>
                        </div>
                      )}
                    </div>
                  </li>
                ))}
              </ul>
            )}
            <div className="f">
              <label htmlFor="soc-addn-product">{t('salesChanges.lines.addProduct')}</label>
              <ProductPicker
                id="soc-addn-product"
                ownOnly
                value={null}
                placeholder={t('salesChanges.lines.addPlaceholder')}
                onChange={(productId, p) => {
                  if (!productId || !p) return
                  setLines((ls) => (ls.some((a) => a.productPublicId === productId) ? ls : [...ls, { key: nextKey++, productPublicId: productId, sku: p.sku ?? '', name: p.name ?? '', listPrice: p.salePrice ?? null, quantity: '1', price: '' }]))
                }}
              />
            </div>
            <div className="f">
              <label htmlFor="soc-addn-notes">{t('sales.notes')}</label>
              <input id="soc-addn-notes" value={notes} maxLength={500} autoComplete="off" onChange={(e) => setNotes(e.target.value)} />
            </div>
            <label className="sw">
              <input type="checkbox" role="switch" checked={allowBackorder} onChange={(e) => setAllowBackorder(e.target.checked)} />
              <span className="tk" aria-hidden="true" />
              <span>{t('salesChanges.lines.allowBackorder')}</span>
            </label>
            {canOverride && (
              <label className="sw">
                <input type="checkbox" role="switch" checked={override} onChange={(e) => setOverride(e.target.checked)} />
                <span className="tk" aria-hidden="true" />
                <span>{t('salesChanges.preview.creditOverride')}</span>
              </label>
            )}
            <ChangeAuthorizationStep
              orderPublicId={order.publicId!}
              clientPublicId={order.clientPublicId ?? null}
              auth={auth}
              onChange={(patch) => setAuth((a) => ({ ...a, ...patch }))}
              attachmentRequired={attachmentRequired}
              error={showErr ? authErr : null}
            />
            {opts.data?.settings?.approvalMode && opts.data.settings.approvalMode !== 'NONE' && (
              <p className="help"><Chip tone="warn">{t('salesChanges.addition.mayNeedApproval')}</Chip></p>
            )}
          </>
        )}
        {error && <p className="ferr" role="alert">{error}</p>}
      </div>
    </Modal>
  )
}
