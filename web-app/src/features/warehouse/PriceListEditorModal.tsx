// Lote 221 (P1) — editor de una lista de precios: datos (nombre, vigencia, a quién aplica), renglones producto/kit → precio con foto opcional, clientes escogidos y
// publicar / volver a borrador / desactivar. Un renglón se agrega con producto y precio; todo cambio lo valida el servidor (cruces de vigencia → 409 con los SKU).
// Editar exige sales.pricelist; con solo sales.view todo queda de lectura.
import { useEffect, useMemo, useRef, useState } from 'react'
import { useCan } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useFormat } from '../../kernel/format'
import { useT } from '../../kernel/i18n'
import { Chip, ClientPicker, ConfirmDialog, DataTable, DateField, EmptyState, Modal, Spinner, toast, type DataColumn } from '../../kernel/ui'
import { usePriceList, usePriceListActions, type PriceListItemDto } from './priceListApi'
import {
  EMPTY_LIST_DRAFT,
  listDraftError,
  parsePrice,
  publishBlocker,
  stateKey,
  stateTone,
  toListRequest,
  type PriceListDraft,
} from './priceListRules'
import { ProductPicker } from './pickers'
import { StoredImage } from './StoredImage'

/** Lista de precios abierta: `publicId` null = nueva (al guardar pasa a la lista creada para agregar renglones). */
export function PriceListEditorModal({ publicId: initialId, onClose }: { publicId: string | null; onClose: () => void }) {
  const t = useT()
  const f = useFormat()
  const canEdit = useCan('sales.pricelist')
  const [publicId, setPublicId] = useState<string | null>(initialId)
  const { data: list, isLoading } = usePriceList(publicId)
  const actions = usePriceListActions()
  const [draft, setDraft] = useState<PriceListDraft>(() => EMPTY_LIST_DRAFT(f.today()))
  const [error, setError] = useState<string | null>(null)
  const [deactivating, setDeactivating] = useState(false)

  useEffect(() => {
    if (list) setDraft({ name: list.name ?? '', validFrom: list.validFrom, validTo: list.validTo ?? '', appliesToAll: list.appliesToAll, notes: list.notes ?? '' })
  }, [list])

  const busy = Object.values(actions).some((m) => m.isPending)
  const editable = canEdit && list?.isActive !== false

  const run = async (fn: () => Promise<unknown>, okKey?: string) => {
    setError(null)
    try {
      await fn()
      if (okKey) toast.success(t(okKey))
      return true
    } catch (e) {
      const p = applyProblemDetails(e)
      const fields = Object.values(p.errors ?? {}).flat()
      setError([p.title, ...fields.filter((m) => m !== p.title)].join(' '))
      return false
    }
  }

  const saveData = async () => {
    const problem = listDraftError(draft)
    if (problem) return setError(t(`warehouse.products.prices.err.${problem}`))
    await run(async () => {
      if (publicId) await actions.update.mutateAsync({ publicId, body: toListRequest(draft) })
      else setPublicId((await actions.create.mutateAsync(toListRequest(draft))).publicId)
    }, 'warehouse.products.prices.saved')
  }

  const title = list?.name || t('warehouse.products.prices.newTitle')
  return (
    <Modal open size="xl" title={title} onClose={onClose} dismissible={!busy} footer={<button type="button" className="btn" onClick={onClose}>{t('common.close')}</button>}>
      <div className="pb">
        {publicId && isLoading ? (
          <Spinner block />
        ) : (
          <>
            {list && (
              <div className="pl-chips">
                <Chip tone={stateTone(list.state)}>{t(`warehouse.products.prices.state.${stateKey(list.state)}`)}</Chip>
                <Chip tone="neutral">{list.appliesToAll ? t('warehouse.products.prices.appliesAll') : t('warehouse.products.prices.appliesSome', { count: list.clients?.length ?? 0 })}</Chip>
                {!list.isActive && <Chip tone="fail">{t('warehouse.products.prices.inactive')}</Chip>}
              </div>
            )}
            <div className="r2">
              <div className="f">
                <label htmlFor="pl-name">{t('warehouse.products.prices.name')}</label>
                <input id="pl-name" value={draft.name} maxLength={120} disabled={!editable && !!publicId} onChange={(e) => setDraft({ ...draft, name: e.target.value })} />
              </div>
              <div className="f">
                <label htmlFor="pl-notes">{t('warehouse.products.prices.notes')}</label>
                <input id="pl-notes" value={draft.notes} maxLength={500} disabled={!editable && !!publicId} onChange={(e) => setDraft({ ...draft, notes: e.target.value })} />
              </div>
            </div>
            <div className="r2">
              <div className="f">
                <label htmlFor="pl-from">{t('warehouse.products.prices.from')}</label>
                <DateField id="pl-from" value={draft.validFrom} readOnly={!editable && !!publicId} onChange={(v) => setDraft({ ...draft, validFrom: v })} />
              </div>
              <div className="f">
                <label htmlFor="pl-to">{t('warehouse.products.prices.to')}</label>
                <DateField id="pl-to" value={draft.validTo} readOnly={!editable && !!publicId} onChange={(v) => setDraft({ ...draft, validTo: v })} />
              </div>
            </div>
            <label style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
              <input type="checkbox" checked={draft.appliesToAll} disabled={!editable && !!publicId} onChange={(e) => setDraft({ ...draft, appliesToAll: e.target.checked })} />
              {t('warehouse.products.prices.appliesAllToggle')}
            </label>
            {(editable || !publicId) && canEdit && (
              <p>
                <button type="button" className="btn flow" disabled={busy} onClick={() => void saveData()}>
                  {busy ? t('common.loading') : publicId ? t('warehouse.products.prices.saveData') : t('warehouse.products.prices.create')}
                </button>
              </p>
            )}
            {error && (
              <p role="alert" className="err">
                {error}
              </p>
            )}
            {list && publicId && (
              <>
                <ClientsSection listId={publicId} appliesToAll={list.appliesToAll} clients={list.clients ?? []} editable={editable} onRun={run} />
                <ItemsSection listId={publicId} items={list.items ?? []} editable={editable} onRun={run} onFail={setError} />
                {canEdit && (
                  <div className="pl-bar">
                    {list.isActive !== false && !list.isPublished && (
                      <button
                        type="button"
                        className="btn flow"
                        disabled={busy}
                        onClick={() => {
                          const blocker = publishBlocker(list.items?.length ?? 0, list.appliesToAll, list.clients?.length ?? 0)
                          if (blocker) return setError(t(`warehouse.products.prices.err.${blocker}`))
                          void run(() => actions.publish.mutateAsync(publicId), 'warehouse.products.prices.published')
                        }}
                      >
                        {t('warehouse.products.prices.publish')}
                      </button>
                    )}
                    {list.isActive !== false && list.isPublished && (
                      <button type="button" className="btn" disabled={busy} onClick={() => void run(() => actions.unpublish.mutateAsync(publicId), 'warehouse.products.prices.unpublished')}>
                        {t('warehouse.products.prices.unpublish')}
                      </button>
                    )}
                    {list.isActive !== false ? (
                      <button type="button" className="btn danger" disabled={busy} onClick={() => setDeactivating(true)}>
                        {t('warehouse.products.prices.deactivate')}
                      </button>
                    ) : (
                      <button type="button" className="btn" disabled={busy} onClick={() => void run(() => actions.reactivate.mutateAsync(publicId), 'warehouse.products.prices.reactivated')}>
                        {t('warehouse.products.prices.reactivate')}
                      </button>
                    )}
                  </div>
                )}
                <ConfirmDialog
                  open={deactivating}
                  title={t('warehouse.products.prices.deactivateTitle', { name: list.name ?? '' })}
                  message={t('warehouse.products.prices.deactivateBody')}
                  confirmLabel={t('warehouse.products.prices.deactivate')}
                  tone="danger"
                  onConfirm={async () => {
                    await actions.deactivate.mutateAsync(publicId)
                    toast.success(t('warehouse.products.prices.deactivated'))
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

type Run = (fn: () => Promise<unknown>, okKey?: string) => Promise<boolean>

function ClientsSection({ listId, appliesToAll, clients, editable, onRun }: { listId: string; appliesToAll: boolean; clients: { clientPublicId?: string; code?: string | null; name?: string | null }[]; editable: boolean; onRun: Run }) {
  const t = useT()
  const actions = usePriceListActions()
  if (appliesToAll) return <p className="note">{t('warehouse.products.prices.allClientsNote')}</p>
  return (
    <div style={{ marginTop: 12 }}>
      <b>{t('warehouse.products.prices.clients')}</b>
      <div className="pl-chips">
        {clients.length === 0 && <span>{t('warehouse.products.prices.noClients')}</span>}
        {clients.map((c) => c.clientPublicId && (
          <Chip key={c.clientPublicId} tone="neutral">
            {[c.code, c.name].filter(Boolean).join(' · ')}
            {editable && (
              <button
                type="button"
                className="btn sm"
                aria-label={t('warehouse.products.prices.removeClient', { name: c.name ?? c.code ?? '' })}
                onClick={() => void onRun(() => actions.removeClient.mutateAsync({ publicId: listId, clientPublicId: c.clientPublicId ?? '' }))}
              >
                ×
              </button>
            )}
          </Chip>
        ))}
      </div>
      {editable && (
        <div className="f" style={{ maxWidth: 420 }}>
          <label htmlFor="pl-client">{t('warehouse.products.prices.addClient')}</label>
          <ClientPicker
            id="pl-client"
            value={null}
            onChange={(id) => {
              if (id) void onRun(() => actions.addClients.mutateAsync({ publicId: listId, clientPublicIds: [id] }))
            }}
          />
        </div>
      )}
    </div>
  )
}

function PriceCell({ item, listId, editable, onRun }: { item: PriceListItemDto; listId: string; editable: boolean; onRun: Run }) {
  const t = useT()
  const f = useFormat()
  const actions = usePriceListActions()
  const [text, setText] = useState(String(item.price))
  useEffect(() => setText(String(item.price)), [item.price])
  if (!editable) return <span className="mono">{f.money(item.price)}</span>
  const commit = () => {
    const price = parsePrice(text)
    if (price == null) return setText(String(item.price))
    if (price !== item.price) void onRun(() => actions.setItemPrice.mutateAsync({ publicId: listId, itemId: item.id, price }))
  }
  return <input className="pl-price" inputMode="decimal" aria-label={t('warehouse.products.prices.priceOf', { sku: item.sku ?? '' })} value={text} onChange={(e) => setText(e.target.value)} onBlur={commit} onKeyDown={(e) => e.key === 'Enter' && e.currentTarget.blur()} />
}

function PhotoCell({ item, listId, editable, onRun }: { item: PriceListItemDto; listId: string; editable: boolean; onRun: Run }) {
  const t = useT()
  const actions = usePriceListActions()
  const ref = useRef<HTMLInputElement>(null)
  return (
    <span className="pl-bar" style={{ margin: 0 }}>
      <StoredImage publicId={item.imagePublicId} alt={t('warehouse.products.prices.photoOf', { sku: item.sku ?? '' })} className="pl-thumb" />
      {editable && (
        <>
          <input
            ref={ref}
            type="file"
            hidden
            accept="image/jpeg,image/png,image/webp,image/gif"
            aria-label={t('warehouse.products.prices.photoFile', { sku: item.sku ?? '' })}
            onChange={(e) => {
              const file = e.target.files?.[0]
              e.target.value = ''
              if (file) void onRun(() => actions.uploadItemImage.mutateAsync({ publicId: listId, itemId: item.id, file }), 'warehouse.products.prices.photoSaved')
            }}
          />
          <button type="button" className="btn sm" onClick={() => ref.current?.click()}>
            {item.imagePublicId ? t('warehouse.products.prices.changePhoto') : t('warehouse.products.prices.addPhoto')}
          </button>
          {item.imagePublicId && (
            <button type="button" className="btn sm" onClick={() => void onRun(() => actions.removeItemImage.mutateAsync({ publicId: listId, itemId: item.id }))}>
              {t('warehouse.products.prices.removePhoto')}
            </button>
          )}
        </>
      )}
    </span>
  )
}

function ItemsSection({ listId, items, editable, onRun, onFail }: { listId: string; items: PriceListItemDto[]; editable: boolean; onRun: Run; onFail: (message: string) => void }) {
  const t = useT()
  const f = useFormat()
  const actions = usePriceListActions()
  const [product, setProduct] = useState<string | null>(null)
  const [price, setPrice] = useState('')

  const columns = useMemo<DataColumn<PriceListItemDto>[]>(
    () => [
      { id: 'sku', header: t('warehouse.products.prices.sku'), cell: (i) => <b>{i.sku}</b>, sortValue: (i) => i.sku ?? '', card: 'title' },
      {
        id: 'name',
        header: t('warehouse.products.prices.product'),
        cell: (i) => (
          <>
            {i.productName} {i.isKit && <Chip tone="neutral">{t('warehouse.products.kit.chip')}</Chip>} {i.isPromotion && <Chip tone="warn">{t('warehouse.products.prices.offerChip')}</Chip>}{' '}
            {!i.productIsActive && <Chip tone="fail">{t('warehouse.products.inactive')}</Chip>}
          </>
        ),
        sortValue: (i) => i.productName ?? '',
      },
      { id: 'base', header: t('warehouse.products.prices.salePrice'), cell: (i) => (i.productSalePrice == null ? '—' : f.money(i.productSalePrice)), sortValue: (i) => i.productSalePrice ?? -1, align: 'end' },
      { id: 'price', header: t('warehouse.products.prices.price'), cell: (i) => <PriceCell item={i} listId={listId} editable={editable} onRun={onRun} />, sortValue: (i) => i.price, align: 'end' },
      { id: 'photo', header: t('warehouse.products.prices.photo'), cell: (i) => <PhotoCell item={i} listId={listId} editable={editable} onRun={onRun} /> },
      ...(editable
        ? [
            {
              id: 'remove',
              header: '',
              cell: (i: PriceListItemDto) => (
                <button type="button" className="btn sm" aria-label={t('warehouse.products.prices.removeItem', { sku: i.sku ?? '' })} onClick={() => void onRun(() => actions.removeItem.mutateAsync({ publicId: listId, itemId: i.id }))}>
                  ×
                </button>
              ),
            },
          ]
        : []),
    ],
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [t, f, listId, editable, onRun],
  )

  const add = async () => {
    const value = parsePrice(price)
    if (!product) return onFail(t('warehouse.products.prices.err.itemProduct'))
    if (value == null) return onFail(t('warehouse.products.prices.err.itemPrice'))
    const ok = await onRun(() => actions.addItems.mutateAsync({ publicId: listId, items: [{ productPublicId: product, price: value }] }), 'warehouse.products.prices.itemAdded')
    if (ok) {
      setProduct(null)
      setPrice('')
    }
  }

  return (
    <div style={{ marginTop: 12 }}>
      <b>{t('warehouse.products.prices.items')}</b>
      {editable && (
        <div className="pl-add">
          <div className="f">
            <label htmlFor="pl-add-p">{t('warehouse.products.prices.addProduct')}</label>
            <ProductPicker id="pl-add-p" ownOnly value={product} onChange={(id) => setProduct(id)} />
          </div>
          <div className="f">
            <label htmlFor="pl-add-price">{t('warehouse.products.prices.price')}</label>
            <input id="pl-add-price" inputMode="decimal" value={price} onChange={(e) => setPrice(e.target.value)} />
          </div>
          <button type="button" className="btn flow" onClick={() => void add()}>
            {t('warehouse.products.prices.addItem')}
          </button>
        </div>
      )}
      <DataTable label={t('warehouse.products.prices.items')} columns={columns} rows={items} rowKey={(i) => String(i.id)} pageSize={25} empty={<EmptyState title={t('warehouse.products.prices.noItems')} />} />
    </div>
  )
}
