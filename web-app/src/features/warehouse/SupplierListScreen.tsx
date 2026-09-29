// Pantalla D (Lote F6) — Compras: proveedores. `/warehouse/suppliers`. Lectura: purchasing.view + PURCHASING (por la
// ruta). Alta/edición/baja/reactivación: purchasing.manage. Nombre único entre los activos (409 del servidor).
import { zodResolver } from '@hookform/resolvers/zod'
import { useMemo, useState } from 'react'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { Can } from '../../kernel/access'
import { useLookups } from '../../kernel/catalogs'
import { useT } from '../../kernel/i18n'
import {
  Chip,
  ConfirmDialog,
  DataTable,
  Field,
  Filters,
  Form,
  matchesQ,
  Modal,
  Panel,
  QBox,
  Select,
  SelectFilter,
  TextArea,
  TextInput,
  toast,
  type DataColumn,
  type RowAction,
} from '../../kernel/ui'
import { useSaveSupplier, useSuppliers, type SupplierDto } from './api'
import { IconLayers } from '../../kernel/ui/screenIcons'

const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

type ModalState = 'create' | SupplierDto | null

// ---- Modal de alta/edición ----
function SupplierModal({ open, onClose, supplier }: { open: boolean; onClose: () => void; supplier: SupplierDto | null }) {
  const t = useT()
  const save = useSaveSupplier()
  const { data: paymentTerms = [] } = useLookups('PaymentTerm')
  const editing = supplier !== null

  const schema = useMemo(
    () =>
      z.object({
        name: z.string().trim().min(1, t('warehouse.suppliers.errors.nameRequired')),
        contactName: z.string().trim(),
        phone: z.string().trim(),
        email: z
          .string()
          .trim()
          .refine((v) => v === '' || EMAIL_RE.test(v), t('warehouse.suppliers.errors.emailInvalid')),
        paymentTerm: z.string(),
        notes: z.string().trim(),
      }),
    [t],
  )
  const form = useForm({
    resolver: zodResolver(schema),
    values: {
      name: supplier?.name ?? '',
      contactName: supplier?.contactName ?? '',
      phone: supplier?.phone ?? '',
      email: supplier?.email ?? '',
      paymentTerm: supplier?.paymentTermCode ?? '',
      notes: supplier?.notes ?? '',
    },
  })
  const formId = 'supplier-form'

  const close = () => {
    form.reset()
    onClose()
  }

  return (
    <Modal
      open={open}
      title={editing ? t('warehouse.suppliers.edit') : t('warehouse.suppliers.new')}
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
          if (editing && supplier) {
            // PATCH: los textos se envían tal cual (nunca `|| null`); '' borra el valor (salvo el nombre).
            await save.mutateAsync({
              action: 'update',
              id: supplier.id ?? 0,
              body: {
                name: v.name,
                contactName: v.contactName,
                phone: v.phone,
                email: v.email,
                paymentTerm: v.paymentTerm,
                notes: v.notes,
                rowVersion: supplier.rowVersion ?? null,
              },
            })
            toast.success(t('warehouse.suppliers.saved'))
          } else {
            await save.mutateAsync({
              action: 'create',
              body: {
                name: v.name,
                contactName: v.contactName || null,
                phone: v.phone || null,
                email: v.email || null,
                paymentTerm: v.paymentTerm || null,
                notes: v.notes || null,
              },
            })
            toast.success(t('warehouse.suppliers.created'))
          }
          close()
        }}
      >
        <Field name="name" label={t('warehouse.suppliers.fields.name')} required>
          <TextInput maxLength={200} />
        </Field>
        <div className="r2">
          <Field name="contactName" label={t('warehouse.suppliers.fields.contactName')}>
            <TextInput maxLength={150} />
          </Field>
          <Field name="phone" label={t('warehouse.suppliers.fields.phone')}>
            <TextInput maxLength={40} />
          </Field>
        </div>
        <div className="r2">
          <Field name="email" label={t('warehouse.suppliers.fields.email')}>
            <TextInput type="email" maxLength={150} />
          </Field>
          <Field name="paymentTerm" label={t('warehouse.suppliers.fields.paymentTerm')}>
            <Select options={paymentTerms.map((o) => ({ value: o.code, label: o.label }))} placeholder={t('warehouse.suppliers.fields.none')} />
          </Field>
        </div>
        <Field name="notes" label={t('warehouse.suppliers.fields.notes')}>
          <TextArea rows={2} />
        </Field>
      </Form>
    </Modal>
  )
}

// ---- Pantalla ----
export default function SupplierListScreen() {
  const t = useT()
  const [show, setShow] = useState<'active' | 'all'>('active')
  const [q, setQ] = useState('')
  const [modal, setModal] = useState<ModalState>(null)
  const [confirm, setConfirm] = useState<{ supplier: SupplierDto; active: boolean } | null>(null)
  const save = useSaveSupplier()

  const { data = [], isLoading, error } = useSuppliers({ includeInactive: show === 'all' })
  const rows = useMemo(() => data.filter((s) => matchesQ(q, s.name, s.contactName, s.email)), [data, q])

  const columns = useMemo<DataColumn<SupplierDto>[]>(
    () => [
      { id: 'name', header: t('warehouse.suppliers.fields.name'), cell: (s) => s.name, sortValue: (s) => s.name, card: 'title' },
      { id: 'contact', header: t('warehouse.suppliers.fields.contactName'), cell: (s) => s.contactName ?? '', card: 'hidden', sortValue: (s) => s.contactName },
      { id: 'email', header: t('warehouse.suppliers.fields.email'), cell: (s) => s.email ?? '', sortValue: (s) => s.email },
      {
        id: 'active',
        header: t('warehouse.list.status'),
        cell: (s) => (
          <Chip tone={s.isActive ? 'neutral' : 'fail'}>{s.isActive ? t('warehouse.suppliers.active') : t('warehouse.suppliers.inactive')}</Chip>
        ),
        sortValue: (s) => s.isActive,
      },
    ],
    [t],
  )

  const actions = useMemo<RowAction<SupplierDto>[]>(
    () => [
      { key: 'edit', label: t('warehouse.suppliers.edit'), perm: 'purchasing.manage', onClick: (s) => setModal(s) },
      {
        key: 'deactivate',
        label: t('warehouse.suppliers.deactivate'),
        perm: 'purchasing.manage',
        visible: (s) => s.isActive === true,
        tone: 'danger',
        onClick: (s) => setConfirm({ supplier: s, active: false }),
      },
      {
        key: 'reactivate',
        label: t('warehouse.suppliers.reactivate'),
        perm: 'purchasing.manage',
        visible: (s) => s.isActive === false,
        onClick: (s) => setConfirm({ supplier: s, active: true }),
      },
    ],
    [t],
  )

  return (
    <div className="wrap">
      <div className="head">
        <div>
          <h1>{t('warehouse.suppliers.title')}</h1>
          <p>{t('warehouse.suppliers.subtitle')}</p>
        </div>
        <div className="act">
          <Can perm="purchasing.manage">
            <button type="button" className="btn flow" onClick={() => setModal('create')}>
              {t('warehouse.suppliers.new')}
            </button>
          </Can>
        </div>
      </div>

      <Filters
        onClear={() => {
          setShow('active')
          setQ('')
        }}
      >
        <SelectFilter
          label={t('warehouse.list.show')}
          value={show}
          allLabel={null}
          onChange={(v) => setShow(v === 'all' ? 'all' : 'active')}
          options={[
            { value: 'active', label: t('warehouse.list.onlyActive') },
            { value: 'all', label: t('warehouse.list.includeInactive') },
          ]}
        />
      </Filters>

      <Panel flush icon={<IconLayers />} title={t('warehouse.suppliers.title')} badge={rows.length}>
        <div className="qrow">
          <QBox value={q} onChange={setQ} />
        </div>
        {error ? (
          <p className="pb ferr" role="alert">
            {error.message}
          </p>
        ) : (
          <DataTable
            label={t('warehouse.suppliers.title')}
            columns={columns}
            rows={rows}
            rowKey={(s) => s.id ?? 0}
            defaultSort={{ id: 'name', desc: false }}
            pageSize={25}
            loading={isLoading}
            rowActions={actions}
          />
        )}
      </Panel>

      <SupplierModal open={modal !== null} onClose={() => setModal(null)} supplier={modal === 'create' ? null : modal} />

      <ConfirmDialog
        open={confirm !== null}
        tone={confirm?.active ? 'flow' : 'danger'}
        title={confirm?.active ? t('warehouse.suppliers.reactivateTitle') : t('warehouse.suppliers.deactivateTitle')}
        message={t(confirm?.active ? 'warehouse.suppliers.reactivateBody' : 'warehouse.suppliers.deactivateBody', {
          name: confirm?.supplier.name ?? '',
        })}
        confirmLabel={confirm?.active ? t('warehouse.suppliers.reactivate') : t('warehouse.suppliers.deactivate')}
        onConfirm={async () => {
          if (!confirm) return
          await save.mutateAsync({ action: confirm.active ? 'reactivate' : 'deactivate', id: confirm.supplier.id ?? 0 })
          toast.success(confirm.active ? t('warehouse.suppliers.reactivated') : t('warehouse.suppliers.deactivated'))
        }}
        onClose={() => setConfirm(null)}
      />
    </div>
  )
}
