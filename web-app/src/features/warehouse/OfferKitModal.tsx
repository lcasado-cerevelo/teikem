// Lote 229 — «Crear kit para esta oferta» (`POST /api/v1/offers/{id}/kits`) y cambiar uno ya creado (`PUT /offers/{id}/kits/{productId}`): SKU, nombre, piezas con
// cantidad y casilla «No sustituible» por pieza (por omisión toda pieza es sustituible, §9 del diseño) y su precio de oferta. Si se conoce el precio de venta
// de todas las piezas (al escogerlas en el buscador), muestra la suma a precio normal y el ahorro. El kit queda atado a la oferta (solo se vende mientras
// esté vigente) y nace no ajustable; al crearlo el servidor agrega su renglón con el precio. sales.pricelist.
import { useId, useRef, useState } from 'react'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { Modal, toast } from '../../kernel/ui'
import { useOfferActions, type OfferItemDto } from './offerApi'
import { newOfferKitPiece, offerKitDraftError, offerKitSavings, piecesFromKit, toOfferKitRequest, toOfferKitUpdate, type OfferKitDraft, type OfferKitPiece } from './offerRules'
import { ProductPicker } from './pickers'
import './offers.css'

const K = 'warehouse.products.prices.offer.kit'

/** `item` null = kit nuevo; un renglón de kit de oferta = cambiar ese kit (nombre y piezas; el precio se cambia en el renglón). */
export function OfferKitModal({ offerId, offerName, item, onClose }: { offerId: string; offerName: string; item: OfferItemDto | null; onClose: () => void }) {
  const t = useT()
  const f = useFormat()
  const uid = useId()
  const seq = useRef(0)
  const actions = useOfferActions()
  const editing = item != null
  const [draft, setDraft] = useState<OfferKitDraft>(() => ({
    sku: item?.sku ?? '',
    name: item?.productName ?? '',
    price: item?.price != null ? String(item.price) : '',
    pieces: editing ? piecesFromKit(item?.kit?.components, (i) => `${uid}-${i}`) : [newOfferKitPiece(`${uid}-0`)],
  }))
  const [error, setError] = useState<string | null>(null)
  const busy = actions.createKit.isPending || actions.updateKit.isPending
  const nextKey = () => `${uid}-n${++seq.current}`
  const setPiece = (key: string, patch: Partial<OfferKitPiece>) => setDraft((d) => ({ ...d, pieces: d.pieces.map((p) => (p.key === key ? { ...p, ...patch } : p)) }))
  const savings = offerKitSavings(draft)

  const save = async () => {
    const problem = offerKitDraftError(draft, editing)
    if (problem) return setError(t(`${K}.err.${problem}`))
    setError(null)
    try {
      if (editing) {
        await actions.updateKit.mutateAsync({ publicId: offerId, productPublicId: item?.productPublicId ?? '', body: toOfferKitUpdate(draft) })
        toast.success(t(`${K}.saved`))
      } else {
        const created = await actions.createKit.mutateAsync({ publicId: offerId, body: toOfferKitRequest(draft) })
        toast.success(t(`${K}.created`, { sku: created.sku ?? draft.sku.trim() }))
      }
      onClose()
    } catch (e) {
      const p = applyProblemDetails(e)
      const fields = Object.values(p.errors ?? {}).flat()
      setError([p.title, ...fields.filter((m) => m !== p.title)].join(' '))
    }
  }

  return (
    <Modal
      open
      size="lg"
      title={editing ? t(`${K}.editTitle`, { sku: item?.sku ?? '' }) : t(`${K}.title`, { name: offerName })}
      onClose={onClose}
      dismissible={!busy}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose} disabled={busy}>
            {t('common.cancel')}
          </button>
          <button type="button" className="btn flow" onClick={() => void save()} disabled={busy}>
            {busy ? t('common.loading') : editing ? t(`${K}.save`) : t(`${K}.create`)}
          </button>
        </>
      }
    >
      <div className="pb">
        <p className="help">{t(`${K}.help`)}</p>
        <div className="r3">
          <div className="f">
            <label htmlFor={`${uid}-sku`}>{t(`${K}.sku`)}</label>
            <input id={`${uid}-sku`} className="mono" maxLength={60} autoComplete="off" value={draft.sku} disabled={editing} onChange={(e) => setDraft({ ...draft, sku: e.target.value })} />
          </div>
          <div className="f">
            <label htmlFor={`${uid}-name`}>{t(`${K}.name`)}</label>
            <input id={`${uid}-name`} maxLength={200} value={draft.name} onChange={(e) => setDraft({ ...draft, name: e.target.value })} />
          </div>
          <div className="f">
            <label htmlFor={`${uid}-price`}>{t(`${K}.price`)}</label>
            <input id={`${uid}-price`} inputMode="decimal" value={draft.price} disabled={editing} onChange={(e) => setDraft({ ...draft, price: e.target.value })} />
          </div>
        </div>
        {editing && <p className="help">{t(`${K}.priceNote`)}</p>}
        <fieldset className="pe-fields" style={{ marginTop: 10 }}>
          <legend>
            <b>{t(`${K}.pieces`)}</b>
          </legend>
          {draft.pieces.map((p, i) => (
            <div className="of-piece" key={p.key}>
              <div className="f">
                <label htmlFor={`${p.key}-p`}>{t(`${K}.piece`, { n: i + 1 })}</label>
                <ProductPicker
                  id={`${p.key}-p`}
                  ownOnly
                  value={p.productPublicId}
                  onChange={(id, prod) => setPiece(p.key, { productPublicId: id, sku: prod?.sku ?? '', name: prod?.name ?? '', salePrice: prod?.salePrice ?? null })}
                />
              </div>
              <div className="f">
                <label htmlFor={`${p.key}-q`}>{t(`${K}.quantity`)}</label>
                <input id={`${p.key}-q`} inputMode="decimal" value={p.quantity} onChange={(e) => setPiece(p.key, { quantity: e.target.value })} />
              </div>
              <label className="of-ns">
                <input type="checkbox" checked={p.notSubstitutable} onChange={(e) => setPiece(p.key, { notSubstitutable: e.target.checked })} aria-label={t(`${K}.notSubstitutableOf`, { n: i + 1 })} />
                <span aria-hidden="true">{t(`${K}.notSubstitutable`)}</span>
              </label>
              <button
                type="button"
                className="btn"
                aria-label={t(`${K}.removePiece`, { n: i + 1 })}
                disabled={draft.pieces.length === 1}
                onClick={() => setDraft((d) => ({ ...d, pieces: d.pieces.filter((x) => x.key !== p.key) }))}
              >
                ×
              </button>
            </div>
          ))}
          <p>
            <button type="button" className="btn" onClick={() => setDraft((d) => ({ ...d, pieces: [...d.pieces, newOfferKitPiece(nextKey())] }))}>
              {t(`${K}.addPiece`)}
            </button>
          </p>
        </fieldset>
        <div className="of-sum" aria-live="polite">
          {savings ? (
            <>
              <span>{t(`${K}.normalSum`, { amount: f.money(savings.normal) })}</span>
              {!editing && savings.saving != null && savings.saving >= 0 && <span className="of-save">{t(`${K}.saving`, { amount: f.money(savings.saving) })}</span>}
              {!editing && savings.saving != null && savings.saving < 0 && <span className="of-loss">{t(`${K}.loss`, { amount: f.money(-savings.saving) })}</span>}
            </>
          ) : (
            <span className="help">{t(`${K}.sumUnknown`)}</span>
          )}
        </div>
        {error && (
          <p role="alert" className="err ferr">
            {error}
          </p>
        )}
      </div>
    </Modal>
  )
}
