// Lote 229 — editor de una OFERTA (`/api/v1/offers/{id}`): datos (nombre, desde/hasta —«hasta» en blanco = sin fecha de fin, con aviso claro—, a quién aplica,
// notas), foto de la oferta, clientes escogidos y renglones: (a) escoger un producto que ya existe + precio de oferta (el buscador de productos ya no trae
// kits de oferta: lote 231), o un kit de oferta (`OfferKitModal`) por uno de sus dos caminos: (b) «Usar un kit anterior» de la biblioteca (composición fija,
// precio editable con el último sugerido) o (c) «Crear un kit nuevo» (queda en la biblioteca). Cada renglón con su foto (por omisión la del producto; se puede cambiar o quitar para volver a la del
// producto). Lote 234: la oferta nace publicada (sin borrador): solo desactivar / reactivar («Publicar» únicamente si quedó en borrador de antes). Los `warnings` del servidor (otra oferta más barata, oferta más cara que la lista general)
// se muestran como avisos, no como errores. Editar exige sales.pricelist; con solo sales.view todo queda de lectura.
import { useEffect, useMemo, useRef, useState } from 'react'
import { useCan } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { Chip, ClientPicker, ConfirmDialog, DataTable, DateField, EmptyState, Modal, Spinner, toast, type DataColumn } from '../../kernel/ui'
import { OfferKitModal, type OfferKitMode } from './OfferKitModal'
import { useOffer, useOfferActions, type OfferDto, type OfferItemDto, type OfferWarningDto } from './offerApi'
import { ChainPicker } from '../chains/ChainPicker'
import {
  audienceBoxDisabled,
  EMPTY_OFFER_DRAFT,
  isLastMember,
  modeBeforeAdd,
  offerAudienceKey,
  offerDraftError,
  offerPublishBlocker,
  offerStateKey,
  offerStateTone,
  OFFER_NAME_MAX,
  OFFER_NOTES_MAX,
  toOfferRequest,
  type OfferAudience,
  type OfferDraft,
} from './offerRules'
import { parsePrice } from './priceListRules'
import { ProductPicker } from './pickers'
import { StoredImage, useProductPhotosEnabled } from './StoredImage'
import './offers.css'

type Run = (fn: () => Promise<unknown>, okKey?: string, params?: Record<string, string>) => Promise<boolean>
const P = 'warehouse.products.prices'
const O = 'warehouse.products.prices.offer'

/** Avisos del servidor sobre la oferta (no impiden guardar). */
export function OfferWarnings({ warnings }: { warnings: OfferWarningDto[] | null | undefined }) {
  const t = useT()
  if (!warnings?.length) return null
  return (
    <section className="of-warns" role="status" aria-label={t(`${O}.warningsTitle`)}>
      <b>{t(`${O}.warningsTitle`)}</b>
      <ul>
        {warnings.map((w, i) => (
          <li key={`${w.code}-${w.productPublicId}-${i}`}>{w.message}</li>
        ))}
      </ul>
    </section>
  )
}

/** Oferta abierta: `publicId` null = nueva (al crearla pasa a la oferta creada para agregar foto, clientes y renglones). */
export function OfferEditorModal({ publicId: initialId, onClose }: { publicId: string | null; onClose: () => void }) {
  const t = useT()
  const f = useFormat()
  const canEdit = useCan('sales.pricelist')
  const [publicId, setPublicId] = useState<string | null>(initialId)
  const { data: offer, isLoading } = useOffer(publicId)
  const actions = useOfferActions()
  const [draft, setDraft] = useState<OfferDraft>(() => EMPTY_OFFER_DRAFT(f.today()))
  const [error, setError] = useState<string | null>(null)
  const [deactivating, setDeactivating] = useState(false)

  useEffect(() => {
    if (offer) setDraft({ name: offer.name ?? '', validFrom: offer.validFrom ?? '', validTo: offer.validTo ?? '', audienceMode: offerAudienceKey(offer), notes: offer.notes ?? '' })
  }, [offer])

  const busy = Object.values(actions).some((m) => m.isPending)
  const editable = canEdit && offer?.isActive !== false
  const readOnly = !!publicId && !editable

  const run: Run = async (fn, okKey, params) => {
    setError(null)
    try {
      await fn()
      if (okKey) toast.success(t(okKey, params))
      return true
    } catch (e) {
      const p = applyProblemDetails(e)
      const fields = Object.values(p.errors ?? {}).flat()
      setError([p.title, ...fields.filter((m) => m !== p.title)].join(' '))
      return false
    }
  }

  const saveData = async () => {
    const problem = offerDraftError(draft)
    if (problem) return setError(t(`${O}.err.${problem}`))
    await run(
      async () => {
        if (publicId) await actions.update.mutateAsync({ publicId, body: toOfferRequest(draft) })
        else setPublicId((await actions.create.mutateAsync(toOfferRequest(draft))).publicId ?? null)
      },
      publicId ? `${O}.saved` : `${O}.created`,
    )
  }

  const title = offer?.name || t(`${O}.newTitle`)
  return (
    <Modal open size="xl" title={title} onClose={onClose} dismissible={!busy} footer={<button type="button" className="btn" onClick={onClose}>{t('common.close')}</button>}>
      <div className="pb">
        {publicId && isLoading ? (
          <Spinner block />
        ) : (
          <>
            {offer && (
              <div className="pl-chips">
                <Chip tone={offerStateTone(offer.state)}>{t(`${O}.state.${offerStateKey(offer.state)}`)}</Chip>
                {offer.inEffect && <Chip tone="deliv">{t(`${O}.inEffect`)}</Chip>}
                <AudienceChip offer={offer} />
                {offer.isActive === false && <Chip tone="fail">{t(`${O}.inactive`)}</Chip>}
              </div>
            )}
            {offer && <OfferWarnings warnings={offer.warnings} />}
            <div className="r2">
              <div className="f">
                <label htmlFor="of-name">{t(`${O}.name`)}</label>
                <input id="of-name" value={draft.name} maxLength={OFFER_NAME_MAX} disabled={readOnly} onChange={(e) => setDraft({ ...draft, name: e.target.value })} />
              </div>
              <div className="f">
                <label htmlFor="of-notes">{t(`${O}.notes`)}</label>
                <input id="of-notes" value={draft.notes} maxLength={OFFER_NOTES_MAX} disabled={readOnly} onChange={(e) => setDraft({ ...draft, notes: e.target.value })} />
              </div>
            </div>
            <div className="r2">
              <div className="f">
                <label htmlFor="of-from">{t(`${O}.from`)}</label>
                <DateField id="of-from" value={draft.validFrom} readOnly={readOnly} onChange={(v) => setDraft({ ...draft, validFrom: v })} />
              </div>
              <div className="f">
                <label htmlFor="of-to">{t(`${O}.to`)}</label>
                <DateField id="of-to" value={draft.validTo} readOnly={readOnly} onChange={(v) => setDraft({ ...draft, validTo: v })} />
              </div>
            </div>
            {/* «hasta» en blanco: la oferta no vence; se dice con claridad (decidido con Luis) */}
            {!draft.validTo && (
              <p className="of-open" role="note">
                {t(`${O}.openEndedEditor`)}
              </p>
            )}
            {offer && offer.validityText && <p className="help">{offer.validityText}</p>}
            {canEdit && !readOnly && (
              <p>
                <button type="button" className="btn flow" disabled={busy} onClick={() => void saveData()}>
                  {busy ? t('common.loading') : publicId ? t(`${O}.saveData`) : t(`${O}.create`)}
                </button>
              </p>
            )}
            {error && (
              <p role="alert" className="err ferr">
                {error}
              </p>
            )}
            {offer && publicId && (
              <>
                <OfferPhotoSection offer={offer} editable={editable} onRun={run} />
                <AudienceSection offer={offer} editable={editable} onRun={run} />
                <ItemsSection offer={offer} editable={editable} onRun={run} onFail={setError} />
                {canEdit && (
                  <div className="pl-bar">
                    {/* Lote 234: las ofertas no tienen borrador (nacen publicadas). Solo una oferta que quedó en borrador de antes muestra «Publicar» para salir de ahí. */}
                    {offer.isActive !== false && !offer.isPublished && (
                      <button
                        type="button"
                        className="btn flow"
                        disabled={busy}
                        onClick={() => {
                          const blocker = offerPublishBlocker(offer.items?.length ?? 0, offerAudienceKey(offer), (offer.clients?.length ?? 0) + (offer.chains?.length ?? 0))
                          if (blocker) return setError(t(`${O}.err.${blocker}`))
                          void run(() => actions.publish.mutateAsync(publicId), `${O}.published`)
                        }}
                      >
                        {t(`${P}.publish`)}
                      </button>
                    )}
                    {offer.isActive !== false ? (
                      <button type="button" className="btn danger" disabled={busy} onClick={() => setDeactivating(true)}>
                        {t(`${P}.deactivate`)}
                      </button>
                    ) : (
                      <button type="button" className="btn" disabled={busy} onClick={() => void run(() => actions.reactivate.mutateAsync(publicId), `${O}.reactivated`)}>
                        {t(`${P}.reactivate`)}
                      </button>
                    )}
                  </div>
                )}
                <ConfirmDialog
                  open={deactivating}
                  title={t(`${O}.deactivateTitle`, { name: offer.name ?? '' })}
                  message={t(`${O}.deactivateBody`)}
                  confirmLabel={t(`${P}.deactivate`)}
                  tone="danger"
                  onConfirm={async () => {
                    await actions.deactivate.mutateAsync(publicId)
                    toast.success(t(`${O}.deactivated`))
                    setDeactivating(false)
                  }}
                  onClose={() => setDeactivating(false)}
                />
              </>
            )}
          </>
        )}
      </div>
    </Modal>
  )
}

/** Archivo de foto escondido + su botón (JPG, PNG o WEBP). */
function PhotoPicker({ label, fileLabel, onFile }: { label: string; fileLabel: string; onFile: (file: File) => void }) {
  const ref = useRef<HTMLInputElement>(null)
  return (
    <>
      <input
        ref={ref}
        type="file"
        hidden
        accept="image/jpeg,image/png,image/webp"
        aria-label={fileLabel}
        onChange={(e) => {
          const file = e.target.files?.[0]
          e.target.value = ''
          if (file) onFile(file)
        }}
      />
      <button type="button" className="btn sm" onClick={() => ref.current?.click()}>
        {label}
      </button>
    </>
  )
}

function OfferPhotoSection({ offer, editable, onRun }: { offer: OfferDto; editable: boolean; onRun: Run }) {
  const t = useT()
  const actions = useOfferActions()
  const id = offer.publicId ?? ''
  if (!useProductPhotosEnabled()) return null // Lote 240: la compañía no maneja fotos
  return (
    <section className="of-section" aria-labelledby="of-photo-title">
      <h3 id="of-photo-title">{t(`${O}.photo`)}</h3>
      <div className="of-photo-row">
        <StoredImage publicId={offer.imagePublicId} alt={t(`${O}.photoAlt`, { name: offer.name ?? '' })} className="of-photo" emptyClassName="of-photo-empty" emptyText={t(`${P}.noPhoto`)} />
        {editable && (
          <div className="pl-bar" style={{ margin: 0 }}>
            <PhotoPicker
              label={offer.imagePublicId ? t(`${P}.changePhoto`) : t(`${P}.addPhoto`)}
              fileLabel={t(`${O}.photoFile`)}
              onFile={(file) => void onRun(() => actions.uploadImage.mutateAsync({ publicId: id, file }), `${P}.photoSaved`)}
            />
            {offer.imagePublicId && (
              <button type="button" className="btn sm" onClick={() => void onRun(() => actions.removeImage.mutateAsync(id))}>
                {t(`${P}.removePhoto`)}
              </button>
            )}
          </div>
        )}
      </div>
    </section>
  )
}

/** Chip del público de la oferta: todos, solo N escogidos o todos menos N. */
export function AudienceChip({ offer }: { offer: Pick<OfferDto, 'audienceMode' | 'appliesToAll' | 'clients' | 'chains'> | { audienceMode?: string | null; appliesToAll?: boolean | null; clientCount?: number; chainCount?: number } }) {
  const t = useT()
  const mode = offerAudienceKey(offer)
  const o = offer as { clients?: unknown[] | null; chains?: unknown[] | null; clientCount?: number; chainCount?: number }
  const count = o.clients || o.chains ? (o.clients?.length ?? 0) + (o.chains?.length ?? 0) : (o.clientCount ?? 0) + (o.chainCount ?? 0)
  if (mode === 'ALL') return <Chip tone="neutral">{t(`${P}.appliesAll`)}</Chip>
  return <Chip tone={mode === 'EXCLUDE' ? 'warn' : 'cap'}>{t(`${O}.audience.${mode}Chip`, { count })}</Chip>
}

/**
 * Público de la oferta (Lote 247): dos cajas, «Incluir» (solo para ellos) y «Excluir» (para todos menos ellos), con clientes y cadenas. Llenar una deshabilita
 * la otra; vacías, la oferta es para todos. Al agregar a una caja vacía primero se pasa el público (PATCH audienceMode) y luego se agrega; al quitar el último
 * se confirma («La oferta quedará para todos los clientes.») y se pasa a ALL.
 */
function AudienceSection({ offer, editable, onRun }: { offer: OfferDto; editable: boolean; onRun: Run }) {
  const t = useT()
  const actions = useOfferActions()
  const id = offer.publicId ?? ''
  const mode = offerAudienceKey(offer)
  const clients = offer.clients ?? []
  const chains = offer.chains ?? []
  const members = clients.length + chains.length
  const [lastRemoval, setLastRemoval] = useState<null | (() => Promise<unknown>)>(null)

  const addTo = (box: 'INCLUDE' | 'EXCLUDE', fn: () => Promise<unknown>) => {
    const before = modeBeforeAdd(box, mode)
    return onRun(async () => {
      if (before) await actions.update.mutateAsync({ publicId: id, body: { audienceMode: before } })
      await fn()
    })
  }
  const removeFrom = (fn: () => Promise<unknown>) => {
    if (isLastMember(members)) return setLastRemoval(() => fn)
    void onRun(fn)
  }

  const box = (kind: 'INCLUDE' | 'EXCLUDE') => {
    const own = mode === kind
    const disabled = !editable || audienceBoxDisabled(kind, mode, members)
    const ids = own ? chains.map((c) => c.chainPublicId ?? '') : []
    return (
      <section className={`of-aud of-aud-${kind.toLowerCase()}${disabled ? ' off' : ''}`} aria-labelledby={`of-aud-${kind}`}>
        <h4 id={`of-aud-${kind}`}>{t(`${O}.audience.${kind}`)}</h4>
        <p className="help">{t(`${O}.audience.${kind}Help`)}</p>
        <div className="pl-chips">
          {own &&
            clients.map(
              (c) =>
                c.clientPublicId && (
                  <Chip key={c.clientPublicId} tone="neutral">
                    {[c.code, c.name].filter(Boolean).join(' · ')}
                    {editable && (
                      <button
                        type="button"
                        className="btn sm"
                        aria-label={t(`${P}.removeClient`, { name: c.name ?? c.code ?? '' })}
                        onClick={() => removeFrom(() => actions.removeClient.mutateAsync({ publicId: id, clientPublicId: c.clientPublicId ?? '' }))}
                      >
                        ×
                      </button>
                    )}
                  </Chip>
                ),
            )}
          {own &&
            chains.map((c) => (
              <Chip key={c.chainPublicId} tone={c.isActive === false ? 'warn' : 'cap'}>
                {t(`${O}.audience.chainChip`, { name: c.name ?? '' })}
                {editable && (
                  <button
                    type="button"
                    className="btn sm"
                    aria-label={t(`${O}.audience.removeChain`, { name: c.name ?? '' })}
                    onClick={() => removeFrom(() => actions.removeChain.mutateAsync({ publicId: id, chainPublicId: c.chainPublicId ?? '' }))}
                  >
                    ×
                  </button>
                )}
              </Chip>
            ))}
          {(!own || members === 0) && <span className="help">{t(`${O}.audience.empty`)}</span>}
        </div>
        {!disabled && (
          <div className="of-aud-pick">
            <div className="f">
              <label htmlFor={`of-client-${kind}`}>{t(`${P}.addClient`)}</label>
              <ClientPicker id={`of-client-${kind}`} value={null} onChange={(cid) => cid && void addTo(kind, () => actions.addClients.mutateAsync({ publicId: id, clientPublicIds: [cid] }))} />
            </div>
            <ChainPicker id={`of-chain-${kind}`} label={t(`${O}.audience.addChain`)} exclude={ids} onPick={(cid) => void addTo(kind, () => actions.addChains.mutateAsync({ publicId: id, chainPublicIds: [cid] }))} />
          </div>
        )}
        {disabled && editable && <p className="help">{t(`${O}.audience.locked`)}</p>}
      </section>
    )
  }

  return (
    <section className="of-section" aria-labelledby="of-audience-title">
      <h3 id="of-audience-title">{t(`${O}.audience.title`)}</h3>
      <p className="note">{mode === 'ALL' ? t(`${O}.allClientsNote`) : t(`${O}.audience.${mode}Note`)}</p>
      <div className="of-aud-grid">
        {box('INCLUDE')}
        {box('EXCLUDE')}
      </div>
      {/* sin borrador, una oferta «solo para algunos» sin escogidos ya está publicada pero no aplica a nadie: se dice */}
      {mode === 'INCLUDE' && members === 0 && offer.isActive !== false && (
        <p className="of-open" role="note">
          {t(`${O}.noClientsYet`)}
        </p>
      )}
      <ConfirmDialog
        open={lastRemoval !== null}
        title={t(`${O}.audience.lastTitle`)}
        message={t(`${O}.audience.lastMessage`)}
        confirmLabel={t(`${O}.audience.lastConfirm`)}
        onClose={() => setLastRemoval(null)}
        onConfirm={async () => {
          const fn = lastRemoval
          if (!fn) return
          const ok = await onRun(async () => {
            await fn()
            await actions.update.mutateAsync({ publicId: id, body: { audienceMode: 'ALL' satisfies OfferAudience } })
          })
          if (!ok) throw new Error(t(`${O}.audience.lastFailed`))
        }}
      />
    </section>
  )
}

function PriceCell({ item, offerId, editable, onRun }: { item: OfferItemDto; offerId: string; editable: boolean; onRun: Run }) {
  const t = useT()
  const f = useFormat()
  const actions = useOfferActions()
  const [text, setText] = useState(String(item.price ?? ''))
  useEffect(() => setText(String(item.price ?? '')), [item.price])
  if (!editable) return <span className="mono">{f.money(item.price ?? 0)}</span>
  const commit = () => {
    const price = parsePrice(text)
    if (price == null) return setText(String(item.price ?? ''))
    if (price !== item.price) void onRun(() => actions.setItemPrice.mutateAsync({ publicId: offerId, itemId: item.id ?? 0, price }))
  }
  return (
    <input
      className="pl-price"
      inputMode="decimal"
      aria-label={t(`${O}.priceOf`, { sku: item.sku ?? '' })}
      value={text}
      onChange={(e) => setText(e.target.value)}
      onBlur={commit}
      onKeyDown={(e) => e.key === 'Enter' && e.currentTarget.blur()}
    />
  )
}

/** Foto del renglón: la propia o, si no tiene, la del producto (efectiva); quitar la propia vuelve a la del producto. */
function ItemPhotoCell({ item, offerId, editable, onRun }: { item: OfferItemDto; offerId: string; editable: boolean; onRun: Run }) {
  const t = useT()
  const actions = useOfferActions()
  const own = item.imagePublicId ?? null
  const shown = item.effectiveImagePublicId ?? own
  return (
    <span className="of-item-photo">
      <StoredImage publicId={shown} alt={t(`${P}.photoOf`, { sku: item.sku ?? '' })} className="pl-thumb" />
      {!own && shown && <span className="of-from-product">{t(`${P}.photoFromProduct`)}</span>}
      {editable && (
        <>
          <PhotoPicker
            label={own ? t(`${P}.changePhoto`) : t(`${O}.otherPhoto`)}
            fileLabel={t(`${P}.photoFile`, { sku: item.sku ?? '' })}
            onFile={(file) => void onRun(() => actions.uploadItemImage.mutateAsync({ publicId: offerId, itemId: item.id ?? 0, file }), `${P}.photoSaved`)}
          />
          {own && (
            <button type="button" className="btn sm" onClick={() => void onRun(() => actions.removeItemImage.mutateAsync({ publicId: offerId, itemId: item.id ?? 0 }))}>
              {t(`${O}.backToProductPhoto`)}
            </button>
          )}
        </>
      )}
    </span>
  )
}

function ItemsSection({ offer, editable, onRun, onFail }: { offer: OfferDto; editable: boolean; onRun: Run; onFail: (message: string) => void }) {
  const t = useT()
  const f = useFormat()
  const actions = useOfferActions()
  const offerId = offer.publicId ?? ''
  const items = useMemo(() => offer.items ?? [], [offer.items])
  const [product, setProduct] = useState<string | null>(null)
  const [price, setPrice] = useState('')
  const [pickerKey, setPickerKey] = useState(0)
  const [kit, setKit] = useState<{ mode: OfferKitMode; item?: OfferItemDto } | null>(null) // null = cerrado
  const photos = useProductPhotosEnabled() // Lote 240: sin fotos no hay columna de foto

  const columns = useMemo<DataColumn<OfferItemDto>[]>(
    () => [
      { id: 'photo', header: t(`${P}.photo`), cell: (i) => <ItemPhotoCell item={i} offerId={offerId} editable={editable} onRun={onRun} />, exportable: false },
      { id: 'sku', header: t(`${P}.sku`), cell: (i) => <b>{i.sku}</b>, sortValue: (i) => i.sku ?? '', card: 'title' },
      {
        id: 'name',
        header: t(`${P}.product`),
        cell: (i) => (
          <>
            {i.productName} {i.isOfferKit ? <Chip tone="cod">{t(`${O}.offerKitChip`)}</Chip> : i.isKit && <Chip tone="neutral">{t('warehouse.products.kit.chip')}</Chip>}{' '}
            {i.productIsActive === false && <Chip tone="fail">{t('warehouse.products.inactive')}</Chip>}
          </>
        ),
        sortValue: (i) => i.productName ?? '',
      },
      { id: 'base', header: t(`${P}.salePrice`), cell: (i) => (i.productSalePrice == null ? '—' : f.money(i.productSalePrice)), sortValue: (i) => i.productSalePrice ?? -1, align: 'end' },
      { id: 'general', header: t(`${O}.generalPrice`), cell: (i) => (i.generalListPrice == null ? '—' : f.money(i.generalListPrice)), sortValue: (i) => i.generalListPrice ?? -1, align: 'end' },
      { id: 'price', header: t(`${O}.offerPrice`), cell: (i) => <PriceCell item={i} offerId={offerId} editable={editable} onRun={onRun} />, sortValue: (i) => i.price ?? 0, align: 'end' },
      ...(editable
        ? [
            {
              id: 'actions',
              header: '',
              exportable: false,
              cell: (i: OfferItemDto) => (
                <span className="pl-bar" style={{ margin: 0 }}>
                  {i.isOfferKit && (
                    <button type="button" className="btn sm" aria-label={t(`${O}.editKitOf`, { sku: i.sku ?? '' })} onClick={() => setKit({ mode: 'edit', item: i })}>
                      {t(`${O}.editKit`)}
                    </button>
                  )}
                  <button type="button" className="btn sm" aria-label={t(`${P}.removeItem`, { sku: i.sku ?? '' })} onClick={() => void onRun(() => actions.removeItem.mutateAsync({ publicId: offerId, itemId: i.id ?? 0 }))}>
                    ×
                  </button>
                </span>
              ),
            },
          ]
        : []),
    ],
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [t, f, offerId, editable, onRun],
  )

  const add = async () => {
    const value = parsePrice(price)
    if (!product) return onFail(t(`${O}.err.itemProduct`))
    if (value == null) return onFail(t(`${O}.err.itemPrice`))
    const ok = await onRun(() => actions.addItems.mutateAsync({ publicId: offerId, items: [{ productPublicId: product, price: value }] }), `${O}.itemAdded`)
    if (ok) {
      setProduct(null)
      setPrice('')
      setPickerKey((k) => k + 1)
    }
  }

  return (
    <section className="of-section" aria-labelledby="of-items-title">
      <h3 id="of-items-title">{t(`${O}.items`)}</h3>
      {editable && (
        <>
          <p className="help">{t(`${O}.waysHelp`)}</p>
          <div className="pl-add">
            <div className="f">
              <label htmlFor="of-add-p">{t(`${O}.addExisting`)}</label>
              <ProductPicker key={pickerKey} id="of-add-p" ownOnly value={product} onChange={(id) => setProduct(id)} />
            </div>
            <div className="f">
              <label htmlFor="of-add-price">{t(`${O}.offerPrice`)}</label>
              <input id="of-add-price" inputMode="decimal" value={price} onChange={(e) => setPrice(e.target.value)} />
            </div>
            <button type="button" className="btn flow" onClick={() => void add()}>
              {t(`${P}.addItem`)}
            </button>
          </div>
          <div className="of-ways" role="group" aria-label={t(`${O}.kitWays`)}>
            <button type="button" className="btn" onClick={() => setKit({ mode: 'pick' })}>
              {t(`${O}.usePreviousKit`)}
            </button>
            <button type="button" className="btn" onClick={() => setKit({ mode: 'new' })}>
              {t(`${O}.createKit`)}
            </button>
          </div>
        </>
      )}
      <DataTable
        label={t(`${O}.items`)}
        columns={photos ? columns : columns.filter((c) => c.id !== 'photo')}
        rows={items}
        rowKey={(i) => String(i.id)}
        pageSize={25}
        exportable={false}
        rowClassName={(i) => (i.productIsActive === false ? 'dim' : undefined)}
        empty={<EmptyState title={t(`${O}.noItems`)} />}
      />
      {kit && <OfferKitModal offerId={offerId} offerName={offer.name ?? ''} mode={kit.mode} item={kit.item} offerItems={items} onClose={() => setKit(null)} />}
    </section>
  )
}
