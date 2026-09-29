// Lote F6 — Pantalla A: lista de almacenes (GET /api/v1/warehouses?includeInactive=). Alta con warehouse.manage.
import { zodResolver } from '@hookform/resolvers/zod'
import { useMemo, useState } from 'react'
import { useForm } from 'react-hook-form'
import { useNavigate } from 'react-router-dom'
import { z } from 'zod'
import { Can } from '../../kernel/access'
import { StatusChip, useLookups } from '../../kernel/catalogs'
import { useT } from '../../kernel/i18n'
import { DataTable, type DataColumn, Field, Filters, Form, matchesQ, Modal, Panel, QBox, Select, SelectFilter, TextInput, toast } from '../../kernel/ui'
import { useCreateWarehouse, useWarehouses, type WarehouseDto } from './api'
import { IconWarehouse } from '../../kernel/ui/screenIcons'

/** Dominio de estatus del almacén (CatalogDomains.WarehouseStatus). */
const STATUS_DOMAIN = 'WarehouseStatus'
/** Código solo permite letras, números, guion y guion bajo (WarehouseCreateRequest.Code, máx. 30). */
const CODE_PATTERN = /^[A-Za-z0-9_-]+$/

function CreateWarehouseModal({ open, onClose }: { open: boolean; onClose: () => void }) {
  const t = useT()
  const create = useCreateWarehouse()
  const { data: countries = [] } = useLookups('Country')
  const schema = useMemo(
    () =>
      z.object({
        code: z
          .string()
          .trim()
          .min(1, t('warehouse.list.errors.codeRequired'))
          .max(30, t('warehouse.list.errors.codePattern'))
          .regex(CODE_PATTERN, t('warehouse.list.errors.codePattern')),
        name: z.string().trim().min(1, t('warehouse.list.errors.nameRequired')),
        line1: z.string().trim(),
        city: z.string().trim(),
        country: z.string().trim(),
      }),
    [t],
  )
  const form = useForm({
    resolver: zodResolver(schema),
    defaultValues: { code: '', name: '', line1: '', city: '', country: 'PR' },
  })
  const formId = 'warehouse-create'

  const close = () => {
    form.reset()
    onClose()
  }

  return (
    <Modal
      open={open}
      title={t('warehouse.list.new')}
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
          await create.mutateAsync({
            code: v.code,
            name: v.name,
            line1: v.line1 || null,
            city: v.city || null,
            country: v.country || null,
          })
          toast.success(t('warehouse.list.created'))
          close()
        }}
      >
        <div className="r2">
          <Field name="code" label={t('warehouse.list.code')} required>
            <TextInput maxLength={30} />
          </Field>
          <Field name="name" label={t('warehouse.list.name')} required>
            <TextInput />
          </Field>
        </div>
        <Field name="line1" label={t('warehouse.list.line1')}>
          <TextInput />
        </Field>
        <div className="r2">
          <Field name="city" label={t('warehouse.list.city')}>
            <TextInput />
          </Field>
          <Field name="country" label={t('warehouse.list.country')}>
            <Select options={countries.map((c) => ({ value: c.code, label: c.label }))} placeholder="" />
          </Field>
        </div>
      </Form>
    </Modal>
  )
}

export default function WarehouseListScreen() {
  const t = useT()
  const navigate = useNavigate()
  const [show, setShow] = useState<'active' | 'all'>('active')
  const [q, setQ] = useState('')
  const [creating, setCreating] = useState(false)

  const { data, isLoading, error } = useWarehouses({ includeInactive: show === 'all' })

  const rows = useMemo(() => (data ?? []).filter((w) => w.code || w.name), [data])
  const filtered = useMemo(
    () => rows.filter((w) => matchesQ(q, w.code, w.name, w.city)),
    [rows, q],
  )

  const columns = useMemo<DataColumn<WarehouseDto>[]>(
    () => [
      { id: 'code', header: t('warehouse.list.code'), cell: (w) => <span className="ref">{w.code}</span>, sortValue: (w) => w.code, card: 'title' },
      { id: 'name', header: t('warehouse.list.name'), cell: (w) => w.name, sortValue: (w) => w.name },
      { id: 'city', header: t('warehouse.list.city'), cell: (w) => w.city, sortValue: (w) => w.city },
      { id: 'zoneCount', header: t('warehouse.list.zones'), cell: (w) => w.zoneCount, sortValue: (w) => w.zoneCount, align: 'end' },
      { id: 'binCount', header: t('warehouse.list.bins'), cell: (w) => w.binCount, sortValue: (w) => w.binCount, align: 'end' },
      { id: 'dockCount', header: t('warehouse.list.docks'), cell: (w) => w.dockCount, sortValue: (w) => w.dockCount, align: 'end' },
      { id: 'qtyOnHand', header: t('warehouse.list.onHand'), cell: (w) => w.qtyOnHand, sortValue: (w) => w.qtyOnHand, align: 'end' },
      {
        id: 'status',
        header: t('warehouse.list.status'),
        cell: (w) => <StatusChip domain={STATUS_DOMAIN} code={w.statusCode} label={w.status} />,
        sortValue: (w) => w.status,
      },
    ],
    [t],
  )

  return (
    <div className="wrap">
      <div className="head">
        <div>
          <h1>{t('warehouse.list.title')}</h1>
          <p>{t('warehouse.list.subtitle')}</p>
        </div>
        <div className="act">
          <Can perm="warehouse.manage">
            <button type="button" className="btn flow" onClick={() => setCreating(true)}>
              {t('warehouse.list.new')}
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

      <Panel flush icon={<IconWarehouse />} title={t('warehouse.list.title')} badge={data ? filtered.length : undefined}>
        <div className="qrow">
          <QBox value={q} onChange={setQ} />
        </div>
        {error ? (
          <p className="pb ferr" role="alert">
            {error.message}
          </p>
        ) : (
          <DataTable
            label={t('warehouse.list.title')}
            columns={columns}
            rows={filtered}
            rowKey={(w) => w.publicId ?? String(w.id)}
            defaultSort={{ id: 'code', desc: false }}
            pageSize={25}
            loading={isLoading}
            onRowClick={(w) => navigate(`/warehouse/warehouses/${w.publicId}`)}
          />
        )}
      </Panel>

      <CreateWarehouseModal open={creating} onClose={() => setCreating(false)} />
    </div>
  )
}
