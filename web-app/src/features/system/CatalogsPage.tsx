// Catálogos de valores (Lote F8a, P6): maestro-detalle — dominios a la izquierda, valores del dominio a la derecha.
// Ruta /system/catalogs (admin.catalogs, SYSTEM). Ver docs/frontend/loteF8-plan.md §3 P6 para los mensajes exactos.
import { zodResolver } from '@hookform/resolvers/zod'
import { useMemo, useState } from 'react'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { Can } from '../../kernel/access/Can'
import { useT } from '../../kernel/i18n/useT'
import {
  CARDS_QUERY,
  Chip,
  ConfirmDialog,
  DataTable,
  EmptyState,
  Field,
  FilterScope,
  Form,
  IconEdit,
  IconPower,
  IconRotateCcw,
  Modal,
  NumberInput,
  Panel,
  QBox,
  Spinner,
  TextArea,
  TextInput,
  Toggle,
  matchesQ,
  toast,
  useMediaQuery,
  type ChipTone,
  type DataColumn,
  type RowAction,
} from '../../kernel/ui'
import {
  useCatalogDomains,
  useCatalogValues,
  useCreateList,
  useCreateValue,
  useDeactivateValue,
  useDeleteList,
  useRemoveOverride,
  useRestoreValue,
  useSetOverride,
  useUpdateValue,
  valueOrigin,
  type CatalogDomain,
  type LookupValue,
} from './api'
import { IconGear } from '../../kernel/ui/screenIcons'

const ADMIN_CATALOGS = 'admin.catalogs'

function originTone(row: LookupValue): ChipTone {
  const origin = valueOrigin(row)
  return origin === 'own' ? 'disp' : origin === 'adjusted' ? 'wh' : 'cap'
}

function originLabelKey(row: LookupValue): string {
  const origin = valueOrigin(row)
  return origin === 'own' ? 'system.catalogs.originOwn' : origin === 'adjusted' ? 'system.catalogs.originAdjusted' : 'system.catalogs.originSystem'
}

// ---------------- Modal: nueva lista ----------------

interface NewListValue {
  code: string
  labelEs: string
  labelEn: string
}

function NewListModal({ open, onClose, onCreated }: { open: boolean; onClose: () => void; onCreated: (domainKey: string) => void }) {
  const t = useT()
  const create = useCreateList()
  const schema = useMemo(
    () =>
      z.object({
        name: z.string().trim().min(1, t('system.catalogs.errors.nameRequired')),
        nameEn: z.string().trim(),
        description: z.string().trim(),
      }),
    [t],
  )
  const form = useForm({ resolver: zodResolver(schema), defaultValues: { name: '', nameEn: '', description: '' } })
  const [values, setValues] = useState<NewListValue[]>([])
  const formId = 'catalogs-new-list'

  const close = () => {
    form.reset()
    setValues([])
    onClose()
  }

  const addRow = () => setValues((v) => [...v, { code: '', labelEs: '', labelEn: '' }])
  const removeRow = (i: number) => setValues((v) => v.filter((_, idx) => idx !== i))
  const updateRow = (i: number, patch: Partial<NewListValue>) =>
    setValues((v) => v.map((row, idx) => (idx === i ? { ...row, ...patch } : row)))

  return (
    <Modal
      open={open}
      title={t('system.catalogs.newList')}
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
        onError={(p) => toast.error(p.title)}
        onSubmit={async (v) => {
          // Fila con algo mas no todo (código sin etiqueta o al revés): se avisa en vez de descartarla en silencio.
          // Una fila totalmente vacía (el usuario agregó una de más) sí se ignora.
          for (let i = 0; i < values.length; i++) {
            const row = values[i]
            const hasCode = row.code.trim() !== ''
            const hasLabel = row.labelEs.trim() !== ''
            if (!hasCode && !hasLabel) continue
            if (!hasCode) return toast.error(t('system.catalogs.errors.rowCodeRequired', { row: i + 1 }))
            if (!hasLabel) return toast.error(t('system.catalogs.errors.rowLabelRequired', { row: i + 1 }))
          }
          const domain = await create.mutateAsync({
            name: v.name,
            nameEn: v.nameEn || null,
            description: v.description || null,
            values: values
              .filter((row) => row.code.trim() && row.labelEs.trim())
              .map((row) => ({
                code: row.code,
                labels: row.labelEn.trim() ? { es: row.labelEs, en: row.labelEn } : { es: row.labelEs },
                descriptions: null,
                extraJson: null,
                sortOrder: null,
              })),
          })
          toast.success(t('system.catalogs.listCreated'))
          close()
          if (domain.domainKey) onCreated(domain.domainKey)
        }}
      >
        <div className="r2">
          <Field name="name" label={t('system.catalogs.name')} required>
            <TextInput />
          </Field>
          <Field name="nameEn" label={t('system.catalogs.nameEn')}>
            <TextInput />
          </Field>
        </div>
        <Field name="description" label={t('system.catalogs.description')}>
          <TextArea rows={2} />
        </Field>
        <div className="f">
          <label>{t('system.catalogs.initialValues')}</label>
          {values.map((row, i) => (
            <div key={i} className="r3" style={{ marginBottom: 8, alignItems: 'end' }}>
              <input
                aria-label={t('system.catalogs.code')}
                placeholder={t('system.catalogs.code')}
                value={row.code}
                onChange={(e) => updateRow(i, { code: e.target.value })}
              />
              <input
                aria-label={t('system.catalogs.labelEs')}
                placeholder={t('system.catalogs.labelEs')}
                value={row.labelEs}
                onChange={(e) => updateRow(i, { labelEs: e.target.value })}
              />
              <div style={{ display: 'flex', gap: 6 }}>
                <input
                  aria-label={t('system.catalogs.labelEn')}
                  placeholder={t('system.catalogs.labelEn')}
                  value={row.labelEn}
                  onChange={(e) => updateRow(i, { labelEn: e.target.value })}
                />
                <button type="button" className="btn sm danger" aria-label={t('system.catalogs.removeValue', { index: i + 1 })} onClick={() => removeRow(i)}>
                  ×
                </button>
              </div>
            </div>
          ))}
          <button type="button" className="btn sm" onClick={addRow}>
            {t('system.catalogs.addValue')}
          </button>
        </div>
      </Form>
    </Modal>
  )
}

// ---------------- Modal: ajustar valor de sistema (override) ----------------

function AdjustValueModal({ entity, row, onClose }: { entity: string; row: LookupValue; onClose: () => void }) {
  const t = useT()
  const setOverride = useSetOverride(entity)
  const schema = useMemo(
    () =>
      z.object({
        labelEs: z.string().trim(),
        labelEn: z.string().trim(),
        sortOverride: z.number().nullable(),
        isEnabled: z.boolean(),
      }),
    [],
  )
  const labels = row.labels ?? {}
  const form = useForm({
    resolver: zodResolver(schema),
    defaultValues: {
      labelEs: labels.es ?? row.label ?? '',
      labelEn: labels.en ?? '',
      sortOverride: row.sortOrder ?? null,
      isEnabled: row.isEnabled !== false,
    },
  })
  const formId = 'catalogs-adjust-value'

  return (
    <Modal
      open
      title={t('system.catalogs.adjustTitle', { label: row.label ?? row.code ?? '' })}
      onClose={onClose}
      dismissible={!form.formState.isSubmitting}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose}>
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
        onError={(p) => toast.error(p.title)}
        onSubmit={async (v) => {
          if (!row.code) return
          await setOverride.mutateAsync({
            code: row.code,
            body: {
              labels: v.labelEn.trim() ? { es: v.labelEs, en: v.labelEn } : { es: v.labelEs },
              isEnabled: v.isEnabled,
              sortOverride: v.sortOverride,
              extraJson: null,
            },
          })
          toast.success(t('system.catalogs.adjusted'))
          onClose()
        }}
      >
        <Field name="labelEs" label={t('system.catalogs.labelEs')}>
          <TextInput />
        </Field>
        <Field name="labelEn" label={t('system.catalogs.labelEn')}>
          <TextInput />
        </Field>
        <div className="r2">
          <Field name="sortOverride" label={t('system.catalogs.order')}>
            <NumberInput />
          </Field>
          <Field name="isEnabled" label={t('system.catalogs.enabled')}>
            <Toggle />
          </Field>
        </div>
        <p className="help">{t('system.catalogs.adjustHelp')}</p>
      </Form>
    </Modal>
  )
}

// ---------------- Modal: nuevo valor propio / editar valor propio ----------------

function ValueFormModal({
  entity,
  domainLabel,
  row,
  onClose,
}: {
  entity: string
  domainLabel: string
  row: LookupValue | null
  onClose: () => void
}) {
  const t = useT()
  const create = useCreateValue(entity)
  const update = useUpdateValue(entity)
  const editing = row !== null
  const schema = useMemo(
    () =>
      z.object({
        code: z.string().trim().min(1, t('system.catalogs.errors.codeRequired')),
        labelEs: z.string().trim().min(1, t('system.catalogs.errors.labelRequired')),
        labelEn: z.string().trim(),
        sortOrder: z.number().nullable(),
      }),
    [t],
  )
  const labels = row?.labels ?? {}
  const form = useForm({
    resolver: zodResolver(schema),
    defaultValues: {
      code: row?.code ?? '',
      labelEs: labels.es ?? row?.label ?? '',
      labelEn: labels.en ?? '',
      sortOrder: row?.sortOrder ?? null,
    },
  })
  const formId = 'catalogs-value-form'

  return (
    <Modal
      open
      title={editing ? t('system.catalogs.editValueTitle', { label: row?.label ?? row?.code ?? '' }) : t('system.catalogs.newValueTitle', { label: domainLabel })}
      onClose={onClose}
      dismissible={!form.formState.isSubmitting}
      footer={
        <>
          <button type="button" className="btn" onClick={onClose}>
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
        onError={(p) => toast.error(p.title)}
        onSubmit={async (v) => {
          const body = {
            code: v.code,
            labels: v.labelEn.trim() ? { es: v.labelEs, en: v.labelEn } : { es: v.labelEs },
            descriptions: null,
            extraJson: null,
            sortOrder: v.sortOrder,
          }
          if (editing && row?.code) {
            await update.mutateAsync({ code: row.code, body })
            toast.success(t('system.catalogs.valueUpdated'))
          } else {
            await create.mutateAsync(body)
            toast.success(t('system.catalogs.valueCreated'))
          }
          onClose()
        }}
      >
        <Field name="code" label={t('system.catalogs.code')} required>
          <TextInput maxLength={40} readOnly={editing} disabled={editing} />
        </Field>
        <div className="r2">
          <Field name="labelEs" label={t('system.catalogs.labelEs')} required>
            <TextInput />
          </Field>
          <Field name="labelEn" label={t('system.catalogs.labelEn')}>
            <TextInput />
          </Field>
        </div>
        <Field name="sortOrder" label={t('system.catalogs.order')}>
          <NumberInput />
        </Field>
      </Form>
    </Modal>
  )
}

// ---------------- Pantalla ----------------

export default function CatalogsPage() {
  const t = useT()
  const cards = useMediaQuery(CARDS_QUERY)

  const { data: domains, isLoading: domainsLoading } = useCatalogDomains()
  const [domainQ, setDomainQ] = useState('')
  const [selectedKey, setSelectedKey] = useState<string | null>(null)

  // Sin selección propia: el primer dominio de la lista (derivado en el render, sin efecto).
  const effectiveKey = selectedKey ?? domains?.[0]?.domainKey ?? null

  const filteredDomains = useMemo(
    () => (domains ?? []).filter((d) => matchesQ(domainQ, d.label, d.domainKey)),
    [domains, domainQ],
  )
  const selectedDomain: CatalogDomain | null = domains?.find((d) => d.domainKey === effectiveKey) ?? null

  const { data: values, isLoading: valuesLoading, error: valuesError } = useCatalogValues(effectiveKey)

  const [creatingList, setCreatingList] = useState(false)
  const [deletingDomain, setDeletingDomain] = useState<CatalogDomain | null>(null)
  const [adjustingValue, setAdjustingValue] = useState<LookupValue | null>(null)
  const [restoringValue, setRestoringValue] = useState<LookupValue | null>(null)
  const [valueModalRow, setValueModalRow] = useState<LookupValue | null>(null)
  const [valueModalOpen, setValueModalOpen] = useState(false)
  const [deactivatingValue, setDeactivatingValue] = useState<LookupValue | null>(null)

  const deleteList = useDeleteList()
  const removeOverride = useRemoveOverride(selectedDomain?.domainKey ?? '')
  const deactivateValue = useDeactivateValue(selectedDomain?.domainKey ?? '')
  const restoreValue = useRestoreValue(selectedDomain?.domainKey ?? '')
  const [restoringOwnValue, setRestoringOwnValue] = useState<LookupValue | null>(null)

  const rows = useMemo(() => values ?? [], [values])
  // Un valor sin dueño (tenantId null) es de un dominio global aunque no venga marcado isSystem (lo agregó el admin
  // de plataforma directo a la lista): Editar/Nuevo valor/Desactivar siguen exigiendo admin de plataforma por igual
  // (LookupService.EnsureCanEditDomain mira TenantId, no IsSystem) — por eso las acciones se deciden con
  // `valueOrigin`, no con `r.isSystem` a secas.
  const isOwn = (r: LookupValue) => valueOrigin(r) === 'own'

  const columns = useMemo<DataColumn<LookupValue>[]>(
    () => [
      { id: 'code', header: t('system.catalogs.code'), cell: (r) => <span className="ref">{r.code}</span>, sortValue: (r) => r.code, card: 'title' },
      { id: 'labelEs', header: t('system.catalogs.labelEs'), cell: (r) => r.labels?.es ?? r.label, sortValue: (r) => r.labels?.es ?? r.label },
      { id: 'labelEn', header: t('system.catalogs.labelEn'), cell: (r) => r.labels?.en ?? '—', sortValue: (r) => r.labels?.en ?? '' },
      { id: 'sortOrder', header: t('system.catalogs.order'), cell: (r) => r.sortOrder, sortValue: (r) => r.sortOrder, align: 'end' },
      {
        id: 'enabled',
        header: t('system.catalogs.enabled'),
        cell: (r) =>
          r.isActive === false ? (
            <Chip tone="fail">{t('system.catalogs.inactive')}</Chip>
          ) : (
            <Chip tone={r.isEnabled ? 'deliv' : 'warn'}>{r.isEnabled ? t('system.catalogs.yes') : t('system.catalogs.no')}</Chip>
          ),
        sortValue: (r) => (r.isActive === false ? -1 : r.isEnabled ? 1 : 0),
      },
      {
        id: 'origin',
        header: t('system.catalogs.origin'),
        cell: (r) => <Chip tone={originTone(r)}>{t(originLabelKey(r))}</Chip>,
        sortValue: (r) => valueOrigin(r),
      },
    ],
    [t],
  )

  const actions = useMemo<RowAction<LookupValue>[]>(
    () => [
      {
        key: 'adjust',
        label: t('system.catalogs.adjust'),
        perm: ADMIN_CATALOGS,
        visible: (r) => !isOwn(r),
        icon: <IconEdit />,
        onClick: (r) => setAdjustingValue(r),
      },
      {
        key: 'restoreOverride',
        label: t('system.catalogs.restore'),
        perm: ADMIN_CATALOGS,
        visible: (r) => !isOwn(r) && r.isOverridden === true,
        icon: <IconRotateCcw />,
        onClick: (r) => setRestoringValue(r),
      },
      {
        key: 'edit',
        label: t('system.catalogs.edit'),
        perm: ADMIN_CATALOGS,
        visible: (r) => isOwn(r) && r.isActive !== false,
        icon: <IconEdit />,
        onClick: (r) => {
          setValueModalRow(r)
          setValueModalOpen(true)
        },
      },
      {
        key: 'deactivate',
        label: t('system.catalogs.deactivate'),
        perm: ADMIN_CATALOGS,
        visible: (r) => isOwn(r) && r.isActive !== false,
        tone: 'danger',
        icon: <IconPower />,
        onClick: (r) => setDeactivatingValue(r),
      },
      {
        key: 'restoreOwn',
        label: t('system.catalogs.restore'),
        perm: ADMIN_CATALOGS,
        visible: (r) => isOwn(r) && r.isActive === false,
        icon: <IconRotateCcw />,
        onClick: (r) => setRestoringOwnValue(r),
      },
    ],
    [t],
  )

  return (
    <div className="wrap">
      <div className="head">
        <div>
          <h1>{t('system.catalogs.title')}</h1>
          <p>{t('system.catalogs.subtitle')}</p>
        </div>
      </div>

      <div className="md">
        {/* ámbito propio: el buscador de listas no filtra la tabla de valores (su exportación va sin línea de filtros) */}
        <FilterScope>
          <Panel flush icon={cards ? undefined : <IconGear />} title={cards ? undefined : t('system.catalogs.lists')}>
            {cards ? (
              <div className="pb">
                <div className="f">
                  <label htmlFor="catalog-domain-select">{t('system.catalogs.list')}</label>
                  <select
                    id="catalog-domain-select"
                    value={effectiveKey ?? ''}
                    onChange={(e) => setSelectedKey(e.target.value || null)}
                  >
                    {(domains ?? []).map((d) => (
                      <option key={d.domainKey} value={d.domainKey ?? ''}>
                        {d.label} {d.isSystem ? '' : `(${t('system.catalogs.own')})`}
                      </option>
                    ))}
                  </select>
                </div>
                <Can perm={ADMIN_CATALOGS}>
                  <button type="button" className="btn sm" onClick={() => setCreatingList(true)}>
                    + {t('system.catalogs.newList')}
                  </button>
                </Can>
              </div>
            ) : (
              <>
                <div className="qrow tight">
                  <QBox value={domainQ} onChange={setDomainQ} placeholder={t('system.catalogs.searchList')} />
                </div>
                <div className="domlist">
                  {domainsLoading && <Spinner label={t('common.loading')} />}
                  {filteredDomains.map((d) => (
                    <button
                      key={d.domainKey}
                      type="button"
                      className={d.domainKey === effectiveKey ? 'domit on' : 'domit'}
                      onClick={() => setSelectedKey(d.domainKey ?? null)}
                    >
                      <Chip tone={d.isSystem ? 'cap' : 'wh'}>{d.isSystem ? t('system.catalogs.system') : t('system.catalogs.own')}</Chip>
                      <span>{d.label}</span>
                    </button>
                  ))}
                  <Can perm={ADMIN_CATALOGS}>
                    <button type="button" className="domit" onClick={() => setCreatingList(true)}>
                      + {t('system.catalogs.newList')}
                    </button>
                  </Can>
                </div>
              </>
            )}
          </Panel>
        </FilterScope>

        <Panel
          flush
          icon={<IconGear />}
          title={selectedDomain ? selectedDomain.label : t('system.catalogs.selectList')}
          badge={selectedDomain ? rows.length : undefined}
          actions={
            selectedDomain && (
              <>
                {!selectedDomain.isSystem && (
                  <Can perm={ADMIN_CATALOGS}>
                    <button type="button" className="btn sm flow" onClick={() => { setValueModalRow(null); setValueModalOpen(true) }}>
                      {t('system.catalogs.newValue')}
                    </button>
                  </Can>
                )}
                {!selectedDomain.isSystem && (
                  <Can perm={ADMIN_CATALOGS}>
                    <button type="button" className="btn sm danger" onClick={() => setDeletingDomain(selectedDomain)}>
                      {t('system.catalogs.deleteList')}
                    </button>
                  </Can>
                )}
              </>
            )
          }
        >
          {!selectedDomain ? (
            <EmptyState title={t('system.catalogs.selectList')} />
          ) : valuesError ? (
            <p className="pb ferr" role="alert">
              {valuesError.message}
            </p>
          ) : (
            <DataTable
              label={selectedDomain.label ?? undefined}
              columns={columns}
              rows={rows}
              rowKey={(r) => r.id ?? r.code ?? ''}
              defaultSort={{ id: 'sortOrder', desc: false }}
              pageSize={50}
              loading={valuesLoading}
              rowActions={actions}
              empty={<EmptyState title={t('system.catalogs.empty')} />}
            />
          )}
        </Panel>
      </div>

      <NewListModal
        open={creatingList}
        onClose={() => setCreatingList(false)}
        onCreated={(domainKey) => setSelectedKey(domainKey)}
      />

      <ConfirmDialog
        open={deletingDomain !== null}
        tone="danger"
        title={t('system.catalogs.deleteListTitle')}
        message={t('system.catalogs.deleteListBody', { name: deletingDomain?.label ?? '' })}
        confirmLabel={t('system.catalogs.deleteList')}
        onConfirm={async () => {
          if (!deletingDomain?.domainKey) return
          await deleteList.mutateAsync(deletingDomain.domainKey)
          toast.success(t('system.catalogs.listDeleted'))
          if (effectiveKey === deletingDomain.domainKey) setSelectedKey(null)
        }}
        onClose={() => setDeletingDomain(null)}
      />

      <ConfirmDialog
        open={restoringValue !== null}
        tone="flow"
        title={t('system.catalogs.restoreOverrideTitle')}
        message={t('system.catalogs.restoreOverrideBody', { label: restoringValue?.label ?? restoringValue?.code ?? '' })}
        confirmLabel={t('system.catalogs.restore')}
        onConfirm={async () => {
          if (!restoringValue?.code) return
          await removeOverride.mutateAsync(restoringValue.code)
          toast.success(t('system.catalogs.restored'))
        }}
        onClose={() => setRestoringValue(null)}
      />

      <ConfirmDialog
        open={restoringOwnValue !== null}
        tone="flow"
        title={t('system.catalogs.restoreValueTitle')}
        message={t('system.catalogs.restoreValueBody', { label: restoringOwnValue?.label ?? restoringOwnValue?.code ?? '' })}
        confirmLabel={t('system.catalogs.restore')}
        onConfirm={async () => {
          if (!restoringOwnValue?.code) return
          await restoreValue.mutateAsync(restoringOwnValue.code)
          toast.success(t('system.catalogs.restoreValueDone'))
        }}
        onClose={() => setRestoringOwnValue(null)}
      />

      <ConfirmDialog
        open={deactivatingValue !== null}
        tone="danger"
        title={t('system.catalogs.deactivateTitle')}
        message={t('system.catalogs.deactivateBody', { label: deactivatingValue?.label ?? deactivatingValue?.code ?? '' })}
        confirmLabel={t('system.catalogs.deactivate')}
        onConfirm={async () => {
          if (!deactivatingValue?.code) return
          await deactivateValue.mutateAsync(deactivatingValue.code)
          toast.success(t('system.catalogs.deactivated'))
        }}
        onClose={() => setDeactivatingValue(null)}
      />

      {selectedDomain?.domainKey && adjustingValue && (
        <AdjustValueModal entity={selectedDomain.domainKey} row={adjustingValue} onClose={() => setAdjustingValue(null)} />
      )}

      {selectedDomain?.domainKey && valueModalOpen && (
        <ValueFormModal
          entity={selectedDomain.domainKey}
          domainLabel={selectedDomain.label ?? selectedDomain.domainKey}
          row={valueModalRow}
          onClose={() => {
            setValueModalOpen(false)
            setValueModalRow(null)
          }}
        />
      )}
    </div>
  )
}
