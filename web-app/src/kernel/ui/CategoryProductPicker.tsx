// Selector "Categoría o producto": un solo combobox con buscador y dos secciones.
// - Categorías: el árbol completo que pasa el llamador (GET /api/v1/product-categories), con sangría por nivel y conteo
//   de productos; el texto filtra por nombre o ruta.
// - Productos: búsqueda en el API (GET /api/v1/products?search=&activeOnly=true&take=20) con 250 ms entre teclas,
//   opciones "SKU · Nombre". Sin texto no se consulta (se invita a escribir).
// El valor elegido se muestra como píldora con ✕ para quitarlo. Teclado: ↑/↓ recorren las dos secciones, Enter elige,
// Escape cierra y devuelve el foco al control. Un 403 de productos muestra un aviso y no saca de la pantalla.
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { useCallback, useEffect, useId, useMemo, useRef, useState, type KeyboardEvent } from 'react'
import { api, unwrap } from '../api/client'
import { ApiError } from '../api/problem'
import type { components } from '../api/schema'
import { useT } from '../i18n/useT'
import { categoryLabel, categoryTree, filterCategoryTree, type CategoryProductValue } from './categoryTree'
import { IconChevronDown, IconClose, IconSearch } from './icons'
import { useDismiss } from './useDismiss'
import './ui.css'

export type CategoryOption = components['schemas']['ProductCategoryDto']
export type ProductSearchOption = components['schemas']['ProductListItemDto']

/** Máximo de productos por búsqueda. */
const PRODUCT_TAKE = 20
const NO_PRODUCTS: ProductSearchOption[] = []

export interface CategoryProductPickerProps {
  /** Categoría, producto o null (todos). */
  value: CategoryProductValue
  /** `detail` trae la fila elegida (categoría o producto) para no tener que volver a pedirla. */
  onChange: (value: CategoryProductValue, detail?: { category?: CategoryOption; product?: ProductSearchOption }) => void
  /** Árbol completo de categorías (p. ej. `useProductCategories().data`). */
  categories: readonly CategoryOption[]
  categoriesLoading?: boolean
  /** Etiqueta visible del control (ya traducida). Por defecto "Categoría o producto". */
  label?: string
  id?: string
  disabled?: boolean
}

type FlatOption =
  | { kind: 'category'; key: string; category: CategoryOption; level: number }
  | { kind: 'product'; key: string; product: ProductSearchOption }

function productText(p: { sku?: string | null; name?: string | null } | null | undefined): string {
  if (!p) return ''
  return [p.sku, p.name].filter(Boolean).join(' · ')
}

export function CategoryProductPicker({ value, onChange, categories, categoriesLoading, label, id, disabled }: CategoryProductPickerProps) {
  const t = useT()
  const autoId = useId()
  const baseId = id ?? autoId
  const listId = `${baseId}-list`
  const boxRef = useRef<HTMLDivElement>(null)
  const triggerRef = useRef<HTMLButtonElement>(null)
  const [open, setOpen] = useState(false)
  const [text, setText] = useState('')
  const [search, setSearch] = useState('')
  const [active, setActive] = useState(0)
  const [picked, setPicked] = useState<ProductSearchOption | null>(null)
  const dismiss = useCallback(() => setOpen(false), [])
  useDismiss(boxRef, open, dismiss)
  const caption = label ?? t('ui.categoryProductPicker.label')

  // búsqueda de productos con pausa de 250 ms entre teclas
  useEffect(() => {
    const h = setTimeout(() => setSearch(text.trim()), 250)
    return () => clearTimeout(h)
  }, [text])

  const productQuery = { search, activeOnly: true, take: PRODUCT_TAKE }
  const products = useQuery({
    queryKey: ['/api/v1/products', productQuery],
    queryFn: () => unwrap(api.GET('/api/v1/products', { params: { query: productQuery } })),
    enabled: open && !disabled && search !== '',
    placeholderData: keepPreviousData, // mientras llega la nueva búsqueda se ven los resultados anteriores
    meta: { handleAccessDenied: false },
  })

  // producto que llega de fuera (selección guardada): se pide la ficha para mostrar "SKU · Nombre"
  const productId = value?.kind === 'product' ? value.publicId : null
  const known = picked && productId && picked.publicId === productId ? picked : null
  const detail = useQuery({
    queryKey: ['/api/v1/products/{publicId}', productId],
    queryFn: () => unwrap(api.GET('/api/v1/products/{publicId}', { params: { path: { publicId: productId ?? '' } } })),
    enabled: Boolean(productId) && !known,
    meta: { handleAccessDenied: false },
  })

  const tree = useMemo(() => categoryTree(categories), [categories])
  const shownCategories = useMemo(() => filterCategoryTree(tree, text), [tree, text])
  const foundProducts = search ? (products.data?.items ?? []) : []

  // estado de la sección Productos (sin texto no se consulta)
  let productStatus: string | null = null
  if (!search) productStatus = t('ui.categoryProductPicker.typeToSearch')
  else if (products.isLoading) productStatus = t('common.loading')
  else if (products.error instanceof ApiError && (products.error.code === 'forbidden' || products.error.code === 'module_disabled'))
    productStatus = t('ui.categoryProductPicker.noAccess')
  else if (products.error) productStatus = t('errors.generic')
  else if (foundProducts.length === 0) productStatus = t('ui.categoryProductPicker.noProducts')
  const shownProducts = productStatus ? NO_PRODUCTS : foundProducts

  let categoryStatus: string | null = null
  if (shownCategories.length === 0)
    categoryStatus = categoriesLoading ? t('common.loading') : t('ui.categoryProductPicker.noCategories')

  const options: FlatOption[] = useMemo(
    () => [
      ...shownCategories.map((r) => ({ kind: 'category' as const, key: `c-${r.category.id}`, category: r.category, level: r.level })),
      ...shownProducts.map((p) => ({ kind: 'product' as const, key: `p-${p.publicId}`, product: p })),
    ],
    [shownCategories, shownProducts],
  )

  let valueKind: string | null = null
  let valueText = ''
  if (value?.kind === 'category') {
    valueKind = t('ui.categoryProductPicker.kindCategory')
    valueText = categoryLabel(categories.find((c) => c.id === value.id)) || '…'
  } else if (value?.kind === 'product') {
    valueKind = t('ui.categoryProductPicker.kindProduct')
    valueText = productText(known ?? detail.data?.product) || '…'
  }

  const openList = () => {
    setText('')
    setSearch('')
    setActive(0)
    setOpen(true)
  }

  const close = () => {
    setOpen(false)
    triggerRef.current?.focus()
  }

  const choose = (o: FlatOption) => {
    if (o.kind === 'category') {
      if (o.category.id == null) return
      onChange({ kind: 'category', id: o.category.id }, { category: o.category })
    } else {
      if (!o.product.publicId) return
      setPicked(o.product)
      onChange({ kind: 'product', publicId: o.product.publicId }, { product: o.product })
    }
    close()
  }

  const clear = () => {
    setPicked(null)
    onChange(null)
    triggerRef.current?.focus()
  }

  const onKeyDown = (e: KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'ArrowDown') {
      e.preventDefault()
      setActive((i) => Math.min(i + 1, options.length - 1))
    } else if (e.key === 'ArrowUp') {
      e.preventDefault()
      setActive((i) => Math.max(i - 1, 0))
    } else if (e.key === 'Enter') {
      e.preventDefault()
      const o = options[active]
      if (o) choose(o)
    } else if (e.key === 'Escape') {
      e.preventDefault()
      close()
    }
  }

  const onTriggerKeyDown = (e: KeyboardEvent<HTMLButtonElement>) => {
    if (e.key === 'ArrowDown' && !open) {
      e.preventDefault()
      openList()
    }
  }

  const optionId = (i: number) => `${listId}-${i}`
  const activeOption = options[active]
  const catGroupId = `${baseId}-gc`
  const prodGroupId = `${baseId}-gp`
  const selectedKey = value?.kind === 'category' ? `c-${value.id}` : value?.kind === 'product' ? `p-${value.publicId}` : null

  const renderOption = (o: FlatOption, i: number) => (
    <div
      key={o.key}
      id={optionId(i)}
      role="option"
      aria-selected={o.key === selectedKey}
      className={['mi', 'cpp-opt', i === active ? 'on' : '', o.key === selectedKey ? 'sel' : ''].filter(Boolean).join(' ')}
      style={o.kind === 'category' ? { paddingLeft: 8 + o.level * 18 } : undefined}
      onMouseDown={(e) => e.preventDefault()}
      onMouseEnter={() => setActive(i)}
      onClick={() => choose(o)}
    >
      <span className="cpp-pic" aria-hidden="true">
        {o.kind === 'category' ? '▣' : '#'}
      </span>
      {o.kind === 'category' ? (
        <>
          <span className="cpp-txt">{o.category.name}</span>
          {o.category.productCount != null && (
            <span className="cpp-ds">
              {o.category.productCount === 1
                ? t('ui.categoryProductPicker.productCountOne')
                : t('ui.categoryProductPicker.productCount', { count: o.category.productCount })}
            </span>
          )}
        </>
      ) : (
        <span className="cpp-txt">
          <span className="code">{o.product.sku}</span> · {o.product.name}
        </span>
      )}
    </div>
  )

  const categoryCount = shownCategories.length

  return (
    <div ref={boxRef} className={open ? 'msel cpp open' : 'msel cpp'}>
      <div className="cpp-box">
        <button
          ref={triggerRef}
          id={baseId}
          type="button"
          className="cpp-trigger"
          aria-haspopup="listbox"
          aria-expanded={open}
          aria-label={valueKind ? `${caption}: ${valueKind} ${valueText}` : caption}
          disabled={disabled}
          onClick={() => (open ? setOpen(false) : openList())}
          onKeyDown={onTriggerKeyDown}
        >
          <span className="cpp-lbl">{caption}</span>
          {valueKind ? (
            <span className="cpp-val">
              <span className="tag">{valueKind}</span>
              <span className="cpp-valtx">{valueText}</span>
            </span>
          ) : (
            <span className="cpp-ph">{t('ui.categoryProductPicker.all')}</span>
          )}
          <IconChevronDown />
        </button>
        {value && !disabled && (
          <button type="button" className="iconbtn cpp-x" aria-label={t('ui.categoryProductPicker.clear')} title={t('ui.categoryProductPicker.clear')} onClick={clear}>
            <IconClose />
          </button>
        )}
      </div>
      {open && (
        <div className="mp">
          <div className="msearch">
            <IconSearch />
            <input
              type="text"
              role="combobox"
              autoFocus
              autoComplete="off"
              aria-label={t('ui.categoryProductPicker.search')}
              aria-autocomplete="list"
              aria-expanded
              aria-controls={listId}
              aria-activedescendant={activeOption ? optionId(active) : undefined}
              placeholder={t('ui.categoryProductPicker.search')}
              value={text}
              onChange={(e) => {
                setText(e.target.value)
                setActive(0)
              }}
              onKeyDown={onKeyDown}
            />
          </div>
          <div className="milist cpp-list" id={listId} role="listbox" aria-label={caption}>
            <div role="group" aria-labelledby={catGroupId}>
              <div className="cpp-g" id={catGroupId}>
                {t('ui.categoryProductPicker.categories')}
              </div>
              {categoryStatus ? <div className="mnone">{categoryStatus}</div> : options.slice(0, categoryCount).map((o, i) => renderOption(o, i))}
            </div>
            <div role="group" aria-labelledby={prodGroupId}>
              <div className="cpp-g" id={prodGroupId}>
                {t('ui.categoryProductPicker.products')}
                {search && <span className="cpp-gds"> {t('ui.categoryProductPicker.matching', { q: search })}</span>}
              </div>
              {productStatus ? (
                <div className="mnone">{productStatus}</div>
              ) : (
                options.slice(categoryCount).map((o, i) => renderOption(o, categoryCount + i))
              )}
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
