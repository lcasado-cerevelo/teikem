// Pantalla D (Lote F6) — Compras: proveedores. `/warehouse/suppliers`. Lectura: purchasing.view + PURCHASING (por la
// ruta). Alta/edición/baja/reactivación: purchasing.manage. Nombre único entre los activos (409 del servidor).
// Lote 2: filtros de texto (Nombre, Contacto, Teléfono, Correo) en el cliente, clic en la fila abre el proveedor, baja/reactivación
// como ícono, estatus con el chip de Almacenes, teléfono con máscara (xxx)xxx-xxxx y término de pago con buscador.
import { zodResolver } from '@hookform/resolvers/zod'
import { useMemo, useState } from 'react'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { Can, useCan } from '../../kernel/access'
import { useLookups } from '../../kernel/catalogs'
import { useT } from '../../kernel/i18n'
import {
  Chip,
  ComboSelectInput,
  ConfirmDialog,
  DataTable,
  Field,
  Filters,
  Form,
  isValidPhone,
  matchesQ,
  Modal,
  normalizeStoredPhone,
  Panel,
  phoneDigits,
  PhoneInput,
  SelectFilter,
  TextArea,
  TextInput,
  toast,
  type DataColumn,
  type RowAction,
} from '../../kernel/ui'
import { useSaveSupplier, useSuppliers, type SupplierDto } from './api'
import { IconPower, IconRotateCcw } from '../../kernel/ui/actionIcons'
import { IconLayers } from '../../kernel/ui/screenIcons'
import { TextFilter } from './filterControls'

const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

type ModalState = 'create' | SupplierDto | null

// Colores del estatus de Almacenes (semilla de WarehouseStatus), para que el chip se vea igual.
const ACTIVE_COLOR = '#059669'
const INACTIVE_COLOR = '#6B7280'

interface SupplierFilters {
  name: string
  contact: string
  phone: string
  email: string
}
const EMPTY_FILTERS: SupplierFilters = { name: '', contact: '', phone: '', email: '' }

/** Teléfono: coincide por dígitos (cualquier formato guardado) o por el texto tal cual. */
function matchesPhone(q: string, phone: string | null | undefined): boolean {
  const qd = phoneDigits(q)
  if (qd && phoneDigits(phone ?? '').includes(qd)) return true
  return matchesQ(q, phone)
}

// ---- Modal de alta/edición ----
function SupplierModal({ open, onClose, supplier }: { open: boolean; onClose: () => void; supplier: SupplierDto | null }) {
  const t = useT()
  const save = useSaveSupplier()
  const { data: paymentTerms = [] } = useLookups('PaymentTerm')
  const editing = supplier !== null
  const paymentTermOptions = useMemo(() => paymentTerms.map((o) => ({ value: o.code, label: o.label })), [paymentTerms])

  const schema = useMemo(
    () =>
      z.object({
        name: z.string().trim().min(1, t('warehouse.suppliers.errors.nameRequired')),
        contactName: z.string().trim(),
        phone: z.string().trim().refine(isValidPhone, t('warehouse.suppliers.errors.phoneInvalid')),
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
      phone: normalizeStoredPhone(supplier?.phone),
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
            <PhoneInput />
          </Field>
        </div>
        <div className="r2">
          <Field name="email" label={t('warehouse.suppliers.fields.email')}>
            <TextInput type="email" maxLength={150} />
          </Field>
          <Field name="paymentTerm" label={t('warehouse.suppliers.fields.paymentTerm')}>
            <ComboSelectInput options={paymentTermOptions} placeholder={t('warehouse.suppliers.fields.paymentTermSearch')} />
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
  const [filters, setFilters] = useState(EMPTY_FILTERS)
  const canManage = useCan('purchasing.manage')
  const [modal, setModal] = useState<ModalState>(null)
  const [confirm, setConfirm] = useState<{ supplier: SupplierDto; active: boolean } | null>(null)
  const save = useSaveSupplier()

  const { data = [], isLoading, error } = useSuppliers({ includeInactive: show === 'all' })
  const rows = useMemo(
    () =>
      data.filter(
        (s) =>
          matchesQ(filters.name, s.name) &&
          matchesQ(filters.contact, s.contactName) &&
          matchesQ(filters.email, s.email) &&
          matchesPhone(filters.phone, s.phone),
      ),
    [data, filters],
  )
  const setFilter = (key: keyof SupplierFilters, value: string) => setFilters((f) => ({ ...f, [key]: value }))

  const columns = useMemo<DataColumn<SupplierDto>[]>(
    () => [
      { id: 'name', header: t('warehouse.suppliers.fields.name'), cell: (s) => s.name, sortValue: (s) => s.name, card: 'title' },
      { id: 'contact', header: t('warehouse.suppliers.fields.contactName'), cell: (s) => s.contactName ?? '', card: 'hidden', sortValue: (s) => s.contactName },
      { id: 'phone', header: t('warehouse.suppliers.fields.phone'), cell: (s) => normalizeStoredPhone(s.phone), sortValue: (s) => s.phone, card: 'hidden' },
      { id: 'email', header: t('warehouse.suppliers.fields.email'), cell: (s) => s.email ?? '', sortValue: (s) => s.email },
      {
        id: 'active',
        header: t('warehouse.list.status'),
        cell: (s) => (
          // Mismo chip que el estatus de Almacenes (WarehouseStatus: ACTIVE verde, INACTIVE gris).
          <Chip color={s.isActive ? ACTIVE_COLOR : INACTIVE_COLOR}>{s.isActive ? t('warehouse.suppliers.active') : t('warehouse.suppliers.inactive')}</Chip>
        ),
        sortValue: (s) => (s.isActive ? t('warehouse.suppliers.active') : t('warehouse.suppliers.inactive')),
      },
    ],
    [t],
  )

  const actions = useMemo<RowAction<SupplierDto>[]>(
    () => [
      {
        key: 'deactivate',
        label: t('warehouse.suppliers.deactivate'),
        perm: 'purchasing.manage',
        visible: (s) => s.isActive === true,
        tone: 'danger',
        icon: <IconPower />,
        onClick: (s) => setConfirm({ supplier: s, active: false }),
      },
      {
        key: 'reactivate',
        label: t('warehouse.suppliers.reactivate'),
        perm: 'purchasing.manage',
        visible: (s) => s.isActive === false,
        icon: <IconRotateCcw />,
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
          setFilters(EMPTY_FILTERS)
        }}
      >
        <TextFilter label={t('warehouse.suppliers.fields.name')} value={filters.name} onChange={(v) => setFilter('name', v)} />
        <TextFilter label={t('warehouse.suppliers.fields.contactName')} value={filters.contact} onChange={(v) => setFilter('contact', v)} />
        <TextFilter label={t('warehouse.suppliers.fields.phone')} value={filters.phone} onChange={(v) => setFilter('phone', v)} />
        <TextFilter label={t('warehouse.suppliers.fields.email')} value={filters.email} onChange={(v) => setFilter('email', v)} />
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
            onRowClick={canManage ? (s) => setModal(s) : undefined}
            rowClassName={(s) => (s.isActive === false ? 'dim' : undefined)}
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
