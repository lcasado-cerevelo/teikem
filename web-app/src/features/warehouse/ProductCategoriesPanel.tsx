// Pantalla B (Lote F6) — subpestaña Categorías de productos (árbol simple con indentación por nivel).
// GET/POST /api/v1/product-categories, PATCH /{id} (mover/renombrar), POST /{id}/deactivate|reactivate.
// Permiso: inventory.view (listar); inventory.manage (alta/edición/baja/reactivación).
import { zodResolver } from '@hookform/resolvers/zod'
import { useMemo, useState } from 'react'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { Can } from '../../kernel/access'
import { useT } from '../../kernel/i18n'
import { Chip, ConfirmDialog, EmptyState, Field, Filters, Form, Modal, Panel, Select, TextInput, toast } from '../../kernel/ui'
import { useProductCategories, useSaveProductCategory, type ProductCategoryDto } from './api'
import { IconLayers } from '../../kernel/ui/screenIcons'

/** Nivel de indentación a partir de la ruta ("Raíz / Hija / Nieta") que arma el servidor. */
function levelOf(path: string | null | undefined): number {
  if (!path) return 0
  return path.split('/').length - 1
}

interface CategoryFormValues {
  name: string
  parentId: string
}

function CategoryModal({
  open,
  onClose,
  categories,
  editing,
}: {
  open: boolean
  onClose: () => void
  categories: ProductCategoryDto[]
  editing: ProductCategoryDto | null
}) {
  const t = useT()
  const save = useSaveProductCategory()
  const schema = useMemo(
    () =>
      z.object({
        name: z
          .string()
          .trim()
          .min(1, t('warehouse.categories.errors.nameRequired'))
          .max(150, t('warehouse.categories.errors.nameMax')),
        parentId: z.string(),
      }),
    [t],
  )
  const form = useForm<CategoryFormValues>({
    resolver: zodResolver(schema),
    values: { name: editing?.name ?? '', parentId: editing?.parentId != null ? String(editing.parentId) : '' },
  })
  const formId = 'category-save'

  // No se puede elegir a sí misma ni a una de sus descendientes como padre (evita ciclos en cliente; el servidor
  // igual valida "Una categoría no puede ser su propia ascendente.").
  const parentOptions = useMemo(() => {
    const excluded = new Set<number>()
    if (editing?.id != null) {
      excluded.add(editing.id)
      const byParent = new Map<number, ProductCategoryDto[]>()
      for (const c of categories) {
        if (c.parentId != null) byParent.set(c.parentId, [...(byParent.get(c.parentId) ?? []), c])
      }
      const stack = [editing.id]
      while (stack.length > 0) {
        const id = stack.pop()!
        for (const child of byParent.get(id) ?? []) {
          if (!excluded.has(child.id ?? -1)) {
            excluded.add(child.id ?? -1)
            stack.push(child.id ?? -1)
          }
        }
      }
    }
    return categories
      .filter((c) => c.isActive && c.id != null && !excluded.has(c.id))
      .map((c) => ({ value: String(c.id), label: '  '.repeat(levelOf(c.path)) + (c.name ?? '') }))
  }, [categories, editing])

  const close = () => {
    form.reset()
    onClose()
  }

  return (
    <Modal
      open={open}
      title={editing ? t('warehouse.categories.editTitle') : t('warehouse.categories.new')}
      onClose={close}
      dismissible={!form.formState.isSubmitting}
      footer={
        <>
          <button type="button" className="btn" onClick={close}>
            {t('common.cancel')}
          </button>
          <button type="submit" form={formId} className="btn flow" disabled={form.formState.isSubmitting}>
            {form.formState.isSubmitting ? t('common.loading') : t('ui.form.save')}
          </button>
        </>
      }
    >
      <Form
        id={formId}
        form={form}
        onSubmit={async (v) => {
          const parentId = v.parentId ? Number(v.parentId) : null
          if (editing?.id != null) {
            await save.mutateAsync({
              action: 'update',
              id: editing.id,
              body: { name: v.name, parentId, clearParent: parentId === null ? true : null },
            })
            toast.success(t('warehouse.categories.saved'))
          } else {
            await save.mutateAsync({ action: 'create', body: { name: v.name, parentId } })
            toast.success(t('warehouse.categories.created'))
          }
          close()
        }}
      >
        <Field name="name" label={t('warehouse.categories.fields.name')} required>
          <TextInput maxLength={150} />
        </Field>
        <Field name="parentId" label={t('warehouse.categories.fields.parent')}>
          <Select options={parentOptions} placeholder={t('warehouse.categories.fields.none')} />
        </Field>
      </Form>
    </Modal>
  )
}

/** Filtro booleano fuera de un <Form> (no usa react-hook-form). */
function ToggleFilter({ label, checked, onChange }: { label: string; checked: boolean; onChange: (v: boolean) => void }) {
  return (
    <div className="f">
      <label className="sw">
        <input type="checkbox" role="switch" checked={checked} onChange={(e) => onChange(e.target.checked)} />
        <span className="tk" aria-hidden="true" />
        <span>{label}</span>
      </label>
    </div>
  )
}

export function ProductCategoriesPanel() {
  const t = useT()
  const [includeInactive, setIncludeInactive] = useState(false)
  const { data: categories = [], isLoading, error } = useProductCategories({ includeInactive })
  const save = useSaveProductCategory()
  const [editing, setEditing] = useState<ProductCategoryDto | 'new' | null>(null)
  const [toToggle, setToToggle] = useState<{ category: ProductCategoryDto; active: boolean } | null>(null)

  return (
    <>
      <div className="head">
        <div>
          <h1>{t('warehouse.categories.title')}</h1>
          <p>{t('warehouse.categories.subtitle')}</p>
        </div>
        <div className="act">
          <Can perm="inventory.manage">
            <button type="button" className="btn flow" onClick={() => setEditing('new')}>
              {t('warehouse.categories.new')}
            </button>
          </Can>
        </div>
      </div>

      <Filters onClear={() => setIncludeInactive(false)}>
        <ToggleFilter label={t('warehouse.categories.includeInactive')} checked={includeInactive} onChange={setIncludeInactive} />
      </Filters>

      <Panel flush icon={<IconLayers />} title={t('warehouse.categories.title')}>
        {error ? (
          <p className="pb ferr" role="alert">
            {error.message}
          </p>
        ) : isLoading ? (
          <div className="spin" role="status" aria-label={t('common.loading')} />
        ) : categories.length === 0 ? (
          <EmptyState title={t('warehouse.categories.empty')} />
        ) : (
          <ul className="lst" role="list" style={{ listStyle: 'none', margin: 0, padding: 0 }}>
            {categories.map((c) => (
              <li key={c.id} className="row" style={{ display: 'flex', alignItems: 'center', gap: 8, padding: '8px 16px', flexWrap: 'wrap' }}>
                <span style={{ paddingLeft: levelOf(c.path) * 20 }}>{c.name}</span>
                <span className="tag">{t('warehouse.categories.productCount', { count: c.productCount ?? 0 })}</span>
                {!c.isActive && <Chip tone="fail">{t('warehouse.categories.inactive')}</Chip>}
                <span style={{ marginLeft: 'auto', display: 'flex', gap: 8 }}>
                  <Can perm="inventory.manage">
                    <button type="button" className="btn sm" onClick={() => setEditing(c)}>
                      {t('warehouse.categories.edit')}
                    </button>
                    {c.isActive ? (
                      <button type="button" className="btn sm danger" onClick={() => setToToggle({ category: c, active: false })}>
                        {t('warehouse.categories.deactivate')}
                      </button>
                    ) : (
                      <button type="button" className="btn sm" onClick={() => setToToggle({ category: c, active: true })}>
                        {t('warehouse.categories.reactivate')}
                      </button>
                    )}
                  </Can>
                </span>
              </li>
            ))}
          </ul>
        )}
      </Panel>

      <CategoryModal
        open={editing !== null}
        onClose={() => setEditing(null)}
        categories={categories}
        editing={editing && editing !== 'new' ? editing : null}
      />

      <ConfirmDialog
        open={toToggle !== null}
        tone={toToggle?.active ? 'flow' : 'danger'}
        title={toToggle?.active ? t('warehouse.categories.reactivateTitle') : t('warehouse.categories.deactivateTitle')}
        message={t(toToggle?.active ? 'warehouse.categories.reactivateBody' : 'warehouse.categories.deactivateBody', {
          name: toToggle?.category.name ?? '',
        })}
        confirmLabel={toToggle?.active ? t('warehouse.categories.reactivate') : t('warehouse.categories.deactivate')}
        onConfirm={async () => {
          if (!toToggle || toToggle.category.id == null) return
          await save.mutateAsync({ action: toToggle.active ? 'reactivate' : 'deactivate', id: toToggle.category.id })
          toast.success(toToggle.active ? t('warehouse.categories.reactivated') : t('warehouse.categories.deactivated'))
        }}
        onClose={() => setToToggle(null)}
      />
    </>
  )
}

export default ProductCategoriesPanel
