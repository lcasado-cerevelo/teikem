// Lote 221 (P1) — «Es promoción» del kit: marca de oferta, vigencia y foto, dentro del editor del kit (`PUT /products/{id}/promo`, foto con `/promo/image`).
// Editar exige sales.pricelist; con solo sales.view / inventory.view se ve. Un kit solo es oferta si es kit (el servidor responde 422 si no).
import { useEffect, useRef, useState } from 'react'
import { Can } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useT } from '../../kernel/i18n'
import { DateField, Spinner, toast } from '../../kernel/ui'
import { useProductPromo, useProductPromoActions } from './priceListApi'
import { promoDraftError, toPromoRequest, type PromoDraft } from './priceListRules'
import { StoredImage } from './StoredImage'

export function KitPromoSection({ publicId }: { publicId: string }) {
  const t = useT()
  const { data: promo, isLoading } = useProductPromo(publicId)
  const actions = useProductPromoActions(publicId)
  const fileRef = useRef<HTMLInputElement>(null)
  const [draft, setDraft] = useState<PromoDraft>({ isPromotion: false, promoFrom: '', promoTo: '' })
  const [error, setError] = useState<string | null>(null)
  const busy = actions.save.isPending || actions.uploadImage.isPending || actions.removeImage.isPending

  useEffect(() => {
    if (promo) setDraft({ isPromotion: promo.isPromotion, promoFrom: promo.promoFrom ?? '', promoTo: promo.promoTo ?? '' })
  }, [promo])

  if (isLoading) return <Spinner block />

  const run = async (fn: () => Promise<unknown>, okKey: string) => {
    setError(null)
    try {
      await fn()
      toast.success(t(okKey))
    } catch (e) {
      setError(applyProblemDetails(e).title)
    }
  }
  const save = () => {
    const problem = promoDraftError(draft)
    if (problem) return setError(t(`warehouse.products.kit.promo.err.${problem}`))
    void run(() => actions.save.mutateAsync(toPromoRequest(draft)), 'warehouse.products.kit.promo.saved')
  }

  return (
    <Can
      perm="sales.pricelist"
      fallback={
        promo?.isPromotion ? (
          <p>
            {t('warehouse.products.kit.promo.isPromotion')}
          </p>
        ) : null
      }
    >
      <fieldset className="pe-fields" style={{ marginTop: 14 }}>
        <legend>
          <b>{t('warehouse.products.kit.promo.title')}</b>
        </legend>
        <p>{t('warehouse.products.kit.promo.help')}</p>
        <label style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
          <input type="checkbox" checked={draft.isPromotion} onChange={(e) => setDraft({ ...draft, isPromotion: e.target.checked })} />
          {t('warehouse.products.kit.promo.isPromotion')}
        </label>
        {draft.isPromotion && (
          <>
            <div className="r2" style={{ marginTop: 8 }}>
              <div className="f">
                <label htmlFor={`${publicId}-pf`}>{t('warehouse.products.kit.promo.from')}</label>
                <DateField id={`${publicId}-pf`} value={draft.promoFrom} onChange={(v) => setDraft({ ...draft, promoFrom: v })} />
              </div>
              <div className="f">
                <label htmlFor={`${publicId}-pt`}>{t('warehouse.products.kit.promo.to')}</label>
                <DateField id={`${publicId}-pt`} value={draft.promoTo} onChange={(v) => setDraft({ ...draft, promoTo: v })} />
              </div>
            </div>
            <div style={{ marginTop: 8 }}>
              <StoredImage publicId={promo?.imagePublicId} alt={t('warehouse.products.kit.promo.photoAlt')} className="promo-photo" emptyClassName="pl-nophoto" emptyText={t('warehouse.products.kit.promo.noPhoto')} />
              <p className="pl-bar">
                <input
                  ref={fileRef}
                  type="file"
                  accept="image/jpeg,image/png,image/webp,image/gif"
                  hidden
                  aria-label={t('warehouse.products.kit.promo.photoFile')}
                  onChange={(e) => {
                    const file = e.target.files?.[0]
                    e.target.value = ''
                    if (file) void run(() => actions.uploadImage.mutateAsync(file), 'warehouse.products.kit.promo.photoSaved')
                  }}
                />
                <button type="button" className="btn" disabled={busy || !promo?.isPromotion} title={promo?.isPromotion ? undefined : t('warehouse.products.kit.promo.photoNeedsSave')} onClick={() => fileRef.current?.click()}>
                  {promo?.imagePublicId ? t('warehouse.products.kit.promo.changePhoto') : t('warehouse.products.kit.promo.addPhoto')}
                </button>
                {promo?.imagePublicId && (
                  <button type="button" className="btn" disabled={busy} onClick={() => void run(() => actions.removeImage.mutateAsync(), 'warehouse.products.kit.promo.photoRemoved')}>
                    {t('warehouse.products.kit.promo.removePhoto')}
                  </button>
                )}
              </p>
            </div>
          </>
        )}
        <p>
          <button type="button" className="btn flow" disabled={busy} onClick={save}>
            {actions.save.isPending ? t('common.loading') : t('warehouse.products.kit.promo.save')}
          </button>
        </p>
        {error && (
          <p role="alert" className="err">
            {error}
          </p>
        )}
      </fieldset>
    </Can>
  )
}
