// Impuestos → Productos (Lote 56): la categoría de impuesto de cada producto (gravable, exento o tasa cero). Gravable es lo normal; el cambio se
// guarda al escoger. Trae hasta 500 productos: se afina con el buscador o con el filtro de categoría.
import { useEffect, useMemo, useState } from 'react'
import { applyProblemDetails } from '../../kernel/api/problem'
import { useT } from '../../kernel/i18n'
import { DataTable, Panel, QBox, Spinner, toast, type DataColumn } from '../../kernel/ui'
import { IconTag } from '../../kernel/ui/screenIcons'
import { CATEGORIES } from './taxRules'
import { PRODUCTS_TAKE, useTaxMutations, useTaxProducts, type ProductTaxCategoryDto } from './taxApi'

export function ProductsTab() {
  const t = useT()
  const { setProductCategory } = useTaxMutations()
  const [q, setQ] = useState('')
  const [debounced, setDebounced] = useState('')
  const [category, setCategory] = useState('')
  useEffect(() => {
    const id = window.setTimeout(() => setDebounced(q.trim()), 300)
    return () => window.clearTimeout(id)
  }, [q])
  const list = useTaxProducts(debounced, category)

  const change = async (p: ProductTaxCategoryDto, next: string) => {
    try {
      await setProductCategory.mutateAsync({ productId: p.productId!, category: next })
      toast.success(t('tax.products.saved', { sku: p.sku ?? '', category: t(`tax.categories.${next}`) }))
    } catch (e) {
      toast.error(applyProblemDetails(e).title)
    }
  }

  const columns = useMemo<DataColumn<ProductTaxCategoryDto>[]>(
    () => [
      { id: 'sku', header: t('tax.products.sku'), cell: (p) => <b>{p.sku}</b>, exportValue: (p) => p.sku ?? '', sortValue: (p) => p.sku ?? '', card: 'title' },
      { id: 'name', header: t('tax.products.name'), cell: (p) => p.name, sortValue: (p) => p.name ?? '' },
      {
        id: 'category',
        header: t('tax.products.category'),
        cell: (p) => (
          <select
            aria-label={t('tax.products.categoryOf', { sku: p.sku ?? '' })}
            value={p.category ?? 'TAXABLE'}
            disabled={setProductCategory.isPending}
            onChange={(e) => void change(p, e.target.value)}
          >
            {CATEGORIES.map((c) => (
              <option key={c} value={c}>{t(`tax.categories.${c}`)}</option>
            ))}
          </select>
        ),
        exportValue: (p) => t(`tax.categories.${p.category ?? 'TAXABLE'}`),
        sortValue: (p) => p.category ?? 'TAXABLE',
      },
    ],
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [t, setProductCategory.isPending],
  )

  return (
    <Panel flush icon={<IconTag />} title={t('tax.products.panel')} badge={list.isPending ? undefined : list.data?.total}>
      <div className="pb">
        <div className="qrow tax-filters">
          <QBox value={q} onChange={setQ} placeholder={t('tax.products.search')} />
          <select aria-label={t('tax.products.filter')} value={category} onChange={(e) => setCategory(e.target.value)}>
            <option value="">{t('tax.products.allCategories')}</option>
            {CATEGORIES.map((c) => (
              <option key={c} value={c}>{t(`tax.categories.${c}`)}</option>
            ))}
          </select>
        </div>
        {list.isError ? (
          <p className="ferr" role="alert">{applyProblemDetails(list.error).title}</p>
        ) : list.isPending ? (
          <Spinner block />
        ) : (
          <>
            <DataTable
              label={t('tax.products.panel')}
              columns={columns}
              rows={list.data?.items ?? []}
              rowKey={(p) => String(p.productId)}
              defaultSort={{ id: 'sku', desc: false }}
              empty={t('tax.products.empty')}
            />
            {(list.data?.total ?? 0) > PRODUCTS_TAKE && <p className="help">{t('tax.products.capNote', { n: PRODUCTS_TAKE, total: list.data?.total ?? 0 })}</p>}
          </>
        )}
        <p className="help">{t('tax.products.help')}</p>
      </div>
    </Panel>
  )
}
