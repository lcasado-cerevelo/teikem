// Lote 229/231 — agregar o cambiar un KIT DE OFERTA desde el editor de la oferta. El kit de oferta vive en la BIBLIOTECA de kits de oferta (lote 231): una
// oferta lo usa con un renglón y su precio, y el mismo kit se puede volver a usar en otras ofertas. Dos caminos claros para agregarlo (pestañas):
// - «Usar un kit anterior» (`mode = 'pick'`): buscador de la biblioteca por SKU o nombre (`GET /offers/kits?q=`); cada kit muestra sus piezas, la oferta
//   donde se usó por última vez y su último precio. Al escogerlo, la composición se ve en solo lectura (con la nota de que no se cambia) y el precio de
//   oferta es editable, prellenado con el último. Se agrega con `POST /offers/{id}/items`. Un kit que ya está en esta oferta no se puede escoger otra vez.
//   «Crear un kit nuevo con estas piezas» pasa al otro camino con las mismas piezas.
// - «Crear un kit nuevo» (`mode = 'new'`): SKU, nombre, precio, piezas con cantidad y «No sustituible» (`KitPiecesEditor`) y «Ajustable al pedir»; si se
//   conoce el precio de venta de todas las piezas muestra la suma a precio normal y el ahorro. `POST /offers/{id}/kits`: queda en la biblioteca y con su
//   renglón en esta oferta.
// - Cambiar un kit de un renglón (`mode = 'edit'`): lee el kit de la biblioteca; el nombre siempre se cambia y las piezas solo si nunca se usó
//   (`compositionLocked`: en solo lectura con la nota). `PATCH /offers/kits/{id}`. El precio se cambia en el renglón.
// Los mensajes del servidor (composición bloqueada, SKU repetido, kit no encontrado…) salen tal cual. sales.pricelist (lo garantiza quien lo abre).
import { useEffect, useId, useMemo, useRef, useState } from 'react'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { Chip, EmptyState, Modal, Spinner, Tabs, toast } from '../../kernel/ui'
import { KitPiecesEditor } from './KitPiecesEditor'
import { newKitPiece } from './kitPieces'
import { useOfferActions, useOfferKitLibrary, useOfferKitLibraryItem, type OfferItemDto, type OfferKitLibraryItemDto } from './offerApi'
import {
  isPiecesProblem,
  kitInOffer,
  libraryKitItem,
  offerKitDraftError,
  offerKitSavings,
  piecesFromKit,
  suggestedKitPrice,
  toOfferKitRequest,
  toOfferKitUpdate,
  type OfferKitDraft,
  type OfferKitProblem,
} from './offerRules'
import { StoredImage } from './StoredImage'
import './offers.css'

const K = 'warehouse.products.prices.offer.kit'
const PK = 'warehouse.products.kit'

export type OfferKitMode = 'pick' | 'new' | 'edit'

export interface OfferKitModalProps {
  offerId: string
  offerName: string
  /** Camino con que se abre: usar un kit anterior, crear uno nuevo o cambiar el kit de `item`. */
  mode: OfferKitMode
  /** Renglón del kit a cambiar (solo `mode = 'edit'`). */
  item?: OfferItemDto | null
  /** Renglones de la oferta (para no escoger dos veces el mismo kit). */
  offerItems?: readonly OfferItemDto[] | null
  onClose: () => void
}

/** Mensaje del servidor tal cual: título y, si los hay, los de cada campo. */
function serverMessage(e: unknown): string {
  const p = applyProblemDetails(e)
  const fields = Object.values(p.errors ?? {}).flat()
  return [p.title, ...fields.filter((m) => m !== p.title)].join(' ')
}

export function OfferKitModal({ offerId, offerName, mode: initialMode, item, offerItems, onClose }: OfferKitModalProps) {
  const t = useT()
  const actions = useOfferActions()
  const [mode, setMode] = useState<OfferKitMode>(initialMode)
  const [seed, setSeed] = useState<OfferKitDraft | null>(null) // piezas traídas de un kit anterior a «Crear un kit nuevo»
  const busy = actions.createKit.isPending || actions.updateKit.isPending || actions.addItems.isPending
  // botón principal del pie: lo pone el camino abierto (su texto y qué hace)
  const runRef = useRef<(() => void) | null>(null)
  const [primary, setPrimary] = useState<string | null>(null)
  const footer = useMemo<Footer>(
    () => ({
      register: (run) => {
        runRef.current = run
      },
      setLabel: setPrimary,
    }),
    [],
  )

  const title = mode === 'edit' ? t(`${K}.editTitle`, { sku: item?.sku ?? '' }) : t(`${K}.title`, { name: offerName })
  return (
    <Modal
      open
      size="lg"
      title={title}
      onClose={onClose}
      dismissible={!busy}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose} disabled={busy}>
            {t('common.cancel')}
          </button>
          {primary && (
            <button type="button" className="btn flow" onClick={() => runRef.current?.()} disabled={busy}>
              {busy ? t('common.loading') : primary}
            </button>
          )}
        </>
      }
    >
      <div className="pb">
        {mode !== 'edit' && (
          <div style={{ marginBottom: 10 }}>
            <Tabs<'pick' | 'new'>
              label={t(`${K}.ways`)}
              value={mode}
              onChange={(next) => setMode(next)}
              tabs={[
                { key: 'pick', label: t(`${K}.usePrevious`) },
                { key: 'new', label: t(`${K}.createNew`) },
              ]}
            />
          </div>
        )}
        {mode === 'pick' && (
          <PickKit
            offerId={offerId}
            offerItems={offerItems}
            footer={footer}
            onDone={onClose}
            onCopy={(draft) => {
              setSeed(draft)
              setMode('new')
            }}
          />
        )}
        {mode === 'new' && <NewKit key={seed ? 'seeded' : 'empty'} offerId={offerId} seed={seed} footer={footer} onDone={onClose} />}
        {mode === 'edit' && item?.productPublicId && <EditKit item={item} footer={footer} onDone={onClose} />}
      </div>
    </Modal>
  )
}

interface Footer {
  register: (run: () => void) => void
  setLabel: (label: string | null) => void
}

/** El camino abierto pone el botón principal del pie (`label` null = sin botón) y lo quita al cerrarse. */
function useFooter(footer: Footer, label: string | null, run: () => void) {
  useEffect(() => {
    footer.register(run)
  })
  useEffect(() => footer.setLabel(label), [footer, label])
  useEffect(() => () => footer.setLabel(null), [footer])
}

function ErrorLine({ error }: { error: string | null }) {
  if (!error) return null
  return (
    <p role="alert" className="err ferr">
      {error}
    </p>
  )
}

/** Texto del problema detectado antes de enviar (las piezas tienen sus textos en el editor de piezas). */
function useProblemText() {
  const t = useT()
  return (p: OfferKitProblem) => (isPiecesProblem(p) ? t(`${PK}.err.${p}`) : t(`${K}.err.${p}`))
}

// ---- «Usar un kit anterior» ----
function PickKit({
  offerId,
  offerItems,
  footer,
  onDone,
  onCopy,
}: {
  offerId: string
  offerItems: readonly OfferItemDto[] | null | undefined
  footer: Footer
  onDone: () => void
  onCopy: (draft: OfferKitDraft) => void
}) {
  const t = useT()
  const f = useFormat()
  const uid = useId()
  const actions = useOfferActions()
  const [text, setText] = useState('')
  const [q, setQ] = useState('')
  const [chosen, setChosen] = useState<OfferKitLibraryItemDto | null>(null)
  const [price, setPrice] = useState('')
  const [error, setError] = useState<string | null>(null)
  const library = useOfferKitLibrary({ q: q || undefined }, !chosen)

  // búsqueda con pausa de 250 ms entre teclas (la hace el servidor: SKU o nombre)
  useEffect(() => {
    const h = setTimeout(() => setQ(text.trim()), 250)
    return () => clearTimeout(h)
  }, [text])

  const add = async () => {
    if (!chosen) return
    const row = libraryKitItem(chosen, price)
    if (!row) return setError(t(`${K}.err.priceRequired`))
    setError(null)
    try {
      await actions.addItems.mutateAsync({ publicId: offerId, items: [row] })
      toast.success(t(`${K}.added`, { sku: chosen.sku ?? '' }))
      onDone()
    } catch (e) {
      setError(serverMessage(e))
    }
  }

  useFooter(footer, chosen ? t(`${K}.addToOffer`) : null, () => void add())

  if (chosen) {
    const pieces = piecesFromKit(chosen.components, (i) => `${uid}-c${i}`)
    return (
      <div>
        <div className="of-lib-head">
          <StoredImage publicId={chosen.effectiveImagePublicId} alt={t(`${K}.photoOf`, { sku: chosen.sku ?? '' })} className="pl-thumb" />
          <div style={{ minWidth: 0 }}>
            <b className="ref">{chosen.sku}</b> <span>{chosen.name}</span>
            <div className="of-chips">
              {chosen.adjustable && <Chip tone="neutral">{t(`${PK}.adjustableChip`)}</Chip>}
              <Chip tone="neutral">{t(`${K}.offerCount`, { count: chosen.offerCount ?? 0 })}</Chip>
            </div>
          </div>
        </div>
        <KitPiecesEditor pieces={pieces} readOnly idPrefix={`${uid}-ro`} />
        <p className="of-lock" role="note">
          {t(`${K}.lockedNote`)}
        </p>
        <div className="r2">
          <div className="f">
            <label htmlFor={`${uid}-price`}>{t(`${K}.price`)}</label>
            <input id={`${uid}-price`} inputMode="decimal" value={price} aria-describedby={`${uid}-last`} onChange={(e) => setPrice(e.target.value)} />
            <p className="help" id={`${uid}-last`}>
              {chosen.lastPrice != null
                ? t(`${K}.lastPriceHelp`, { amount: f.money(chosen.lastPrice), offer: chosen.lastOfferName ?? '' })
                : t(`${K}.neverUsedHelp`)}
            </p>
          </div>
        </div>
        <div className="pl-bar">
          <button type="button" className="btn" onClick={() => { setChosen(null); setError(null) }}>
            {t(`${K}.chooseOther`)}
          </button>
          <button
            type="button"
            className="btn"
            onClick={() => onCopy({ sku: '', name: chosen.name ?? '', price, adjustable: chosen.adjustable === true, pieces: piecesFromKit(chosen.components, (i) => `${uid}-copy${i}`) })}
          >
            {t(`${K}.copyPieces`)}
          </button>
        </div>
        <ErrorLine error={error} />
      </div>
    )
  }

  const kits = library.data ?? []
  return (
    <div>
      <p className="help">{t(`${K}.pickHelp`)}</p>
      <div className="f">
        <label htmlFor={`${uid}-q`}>{t(`${K}.search`)}</label>
        <input id={`${uid}-q`} type="search" autoComplete="off" value={text} onChange={(e) => setText(e.target.value)} />
      </div>
      {library.isLoading ? (
        <Spinner block />
      ) : library.isError ? (
        <ErrorLine error={serverMessage(library.error)} />
      ) : kits.length === 0 ? (
        <EmptyState title={q ? t(`${K}.noMatches`) : t(`${K}.emptyLibrary`)} body={t(`${K}.emptyLibraryBody`)} />
      ) : (
        <ul className="of-lib" aria-label={t(`${K}.library`)}>
          {kits.map((k) => {
            const here = kitInOffer(offerItems, k.productPublicId)
            return (
              <li key={k.productPublicId} className="of-lib-item">
                <StoredImage publicId={k.effectiveImagePublicId} alt={t(`${K}.photoOf`, { sku: k.sku ?? '' })} className="pl-thumb" />
                <div className="of-lib-body">
                  <span>
                    <b className="ref">{k.sku}</b> {k.name}
                  </span>
                  <span className="help">
                    {(k.components ?? []).map((c) => `${c.sku ?? ''} × ${f.number(c.quantity ?? 0)}`).join(' · ')}
                  </span>
                  <span className="help">
                    {k.lastPrice != null
                      ? t(`${K}.lastUse`, { offer: k.lastOfferName ?? '', amount: f.money(k.lastPrice) })
                      : t(`${K}.neverUsed`)}
                  </span>
                  {here && <Chip tone="warn">{t(`${K}.alreadyHere`)}</Chip>}
                </div>
                <button
                  type="button"
                  className="btn sm"
                  disabled={here}
                  aria-label={t(`${K}.useKitOf`, { sku: k.sku ?? '' })}
                  onClick={() => {
                    setChosen(k)
                    setPrice(suggestedKitPrice(k))
                    setError(null)
                  }}
                >
                  {t(`${K}.useKit`)}
                </button>
              </li>
            )
          })}
        </ul>
      )}
    </div>
  )
}

// ---- «Crear un kit nuevo» ----
function NewKit({ offerId, seed, footer, onDone }: { offerId: string; seed: OfferKitDraft | null; footer: Footer; onDone: () => void }) {
  const t = useT()
  const f = useFormat()
  const uid = useId()
  const actions = useOfferActions()
  const problemText = useProblemText()
  const [draft, setDraft] = useState<OfferKitDraft>(() => seed ?? { sku: '', name: '', price: '', adjustable: false, pieces: [newKitPiece(`${uid}-0`)] })
  const [error, setError] = useState<string | null>(null)
  const savings = offerKitSavings(draft)

  const save = async () => {
    const problem = offerKitDraftError(draft)
    if (problem) return setError(problemText(problem))
    setError(null)
    try {
      const created = await actions.createKit.mutateAsync({ publicId: offerId, body: toOfferKitRequest(draft) })
      toast.success(t(`${K}.created`, { sku: created.sku ?? draft.sku.trim() }))
      onDone()
    } catch (e) {
      setError(serverMessage(e))
    }
  }

  useFooter(footer, t(`${K}.create`), () => void save())

  return (
    <div>
      <p className="help">{t(`${K}.help`)}</p>
      <div className="r3">
        <div className="f">
          <label htmlFor={`${uid}-sku`}>{t(`${K}.sku`)}</label>
          <input id={`${uid}-sku`} className="mono" maxLength={60} autoComplete="off" value={draft.sku} onChange={(e) => setDraft({ ...draft, sku: e.target.value })} />
        </div>
        <div className="f">
          <label htmlFor={`${uid}-name`}>{t(`${K}.name`)}</label>
          <input id={`${uid}-name`} maxLength={200} value={draft.name} onChange={(e) => setDraft({ ...draft, name: e.target.value })} />
        </div>
        <div className="f">
          <label htmlFor={`${uid}-price`}>{t(`${K}.price`)}</label>
          <input id={`${uid}-price`} inputMode="decimal" value={draft.price} onChange={(e) => setDraft({ ...draft, price: e.target.value })} />
        </div>
      </div>
      <KitPiecesEditor pieces={draft.pieces} onChange={(pieces) => setDraft((d) => ({ ...d, pieces }))} idPrefix={uid} ownOnly />
      <label className="of-check">
        <input type="checkbox" checked={draft.adjustable === true} onChange={(e) => setDraft({ ...draft, adjustable: e.target.checked })} />
        {t(`${PK}.adjustable`)}
      </label>
      <div className="of-sum" aria-live="polite">
        {savings ? (
          <>
            <span>{t(`${K}.normalSum`, { amount: f.money(savings.normal) })}</span>
            {savings.saving != null && savings.saving >= 0 && <span className="of-save">{t(`${K}.saving`, { amount: f.money(savings.saving) })}</span>}
            {savings.saving != null && savings.saving < 0 && <span className="of-loss">{t(`${K}.loss`, { amount: f.money(-savings.saving) })}</span>}
          </>
        ) : (
          <span className="help">{t(`${K}.sumUnknown`)}</span>
        )}
      </div>
      <ErrorLine error={error} />
    </div>
  )
}

// ---- Cambiar el kit de un renglón ----
function EditKit({ item, footer, onDone }: { item: OfferItemDto; footer: Footer; onDone: () => void }) {
  const kit = useOfferKitLibraryItem(item.productPublicId)
  if (kit.isError) return <ErrorLine error={serverMessage(kit.error)} />
  if (!kit.data) return <Spinner block />
  return <EditKitForm kit={kit.data} footer={footer} onDone={onDone} />
}

function EditKitForm({ kit, footer, onDone }: { kit: OfferKitLibraryItemDto; footer: Footer; onDone: () => void }) {
  const t = useT()
  const actions = useOfferActions()
  const problemText = useProblemText()
  const uid = useId()
  const [draft, setDraft] = useState<OfferKitDraft>(() => ({
    sku: kit.sku ?? '',
    name: kit.name ?? '',
    price: '',
    adjustable: kit.adjustable === true,
    pieces: piecesFromKit(kit.components, (i) => `${uid}-${i}`),
  }))
  const [error, setError] = useState<string | null>(null)
  const locked = kit.compositionLocked !== false

  const save = async () => {
    const problem = offerKitDraftError(draft, true, locked)
    if (problem) return setError(problemText(problem))
    setError(null)
    try {
      await actions.updateKit.mutateAsync({ productPublicId: kit.productPublicId ?? '', body: toOfferKitUpdate(draft, locked) })
      toast.success(t(`${K}.saved`))
      onDone()
    } catch (e) {
      setError(serverMessage(e))
    }
  }

  useFooter(footer, t(`${K}.save`), () => void save())

  return (
    <div>
      <div className="r2">
        <div className="f">
          <label htmlFor={`${uid}-sku`}>{t(`${K}.sku`)}</label>
          <input id={`${uid}-sku`} className="mono" value={draft.sku} disabled />
        </div>
        <div className="f">
          <label htmlFor={`${uid}-name`}>{t(`${K}.name`)}</label>
          <input id={`${uid}-name`} maxLength={200} value={draft.name} onChange={(e) => setDraft({ ...draft, name: e.target.value })} />
        </div>
      </div>
      <p className="help">{t(`${K}.priceNote`)}</p>
      {locked ? (
        <>
          <KitPiecesEditor pieces={draft.pieces} readOnly idPrefix={`${uid}-ro`} />
          {draft.adjustable && <Chip tone="neutral">{t(`${PK}.adjustableChip`)}</Chip>}
          <p className="of-lock" role="note">
            {t(`${K}.lockedNote`)}
          </p>
        </>
      ) : (
        <>
          <KitPiecesEditor pieces={draft.pieces} onChange={(pieces) => setDraft((d) => ({ ...d, pieces }))} idPrefix={uid} ownOnly />
          <label className="of-check">
            <input type="checkbox" checked={draft.adjustable === true} onChange={(e) => setDraft({ ...draft, adjustable: e.target.checked })} />
            {t(`${PK}.adjustable`)}
          </label>
        </>
      )}
      <ErrorLine error={error} />
    </div>
  )
}
