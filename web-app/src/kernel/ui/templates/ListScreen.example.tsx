// PLANTILLA — pantalla de lista. Copiar a features/<modulo>/<Pantalla>.tsx y ajustar. No está montada en rutas.
// Patrón: Panel + Filters (estructurados, van al API o filtran lo cargado) + QBox (libre, sobre lo que se muestra,
// DESPUÉS de los filtros) + DataTable (orden y página; acciones por fila con guardas) + "Nuevo" con <Can> + modal de alta.
// Ejemplo real: clientes (GET/POST /api/v1/clients, módulo CATALOG, permisos clients.read/create/update).
import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useMemo, useState } from 'react'
import { useForm } from 'react-hook-form'
import { useNavigate } from 'react-router-dom'
import { z } from 'zod'
import { Can } from '../../access/Can'
import { api, unwrap } from '../../api/client'
import type { components } from '../../api/schema'
import { StatusChip, useStatuses } from '../../catalogs'
import { useT } from '../../i18n/useT'
import { Chip } from '../Chip'
import { ConfirmDialog } from '../ConfirmDialog'
import { DataTable, type DataColumn, type RowAction } from '../DataTable'
import { Filters, SelectFilter } from '../Filters'
import { Field, Form, NumberInput, TextInput } from '../Form'
import { matchesQ } from '../matchesQ'
import { Modal } from '../Modal'
import { Panel } from '../Panel'
import { QBox } from '../QBox'
import { SearchSelect } from '../SearchSelect'
import { toast } from '../toast'
import { IconUsers } from '../screenIcons'

type Client = components['schemas']['ClientListItemDto']

// ---- API del módulo (en una pantalla real va en features/<modulo>/api.ts) ----
const LIST_KEY = '/api/v1/clients'
/** Dominio de estatus del cliente (CatalogDomains.ClientStatus). */
const STATUS_DOMAIN = 'ClientStatus'

function useClients(includeInactive: boolean) {
  const query = { includeInactive }
  return useQuery({
    queryKey: [LIST_KEY, query],
    queryFn: () => unwrap(api.GET('/api/v1/clients', { params: { query } })),
  })
}

function useCreateClient() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (body: components['schemas']['ClientCreateRequest']) => unwrap(api.POST('/api/v1/clients', { body })),
    onSuccess: () => qc.invalidateQueries({ queryKey: [LIST_KEY] }),
  })
}

function useDeactivateClient() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (publicId: string) => unwrap(api.POST('/api/v1/clients/{publicId}/deactivate', { params: { path: { publicId } } })),
    onSuccess: () => qc.invalidateQueries({ queryKey: [LIST_KEY] }),
  })
}

// ---- Modal de alta ----
function CreateClientModal({ open, onClose }: { open: boolean; onClose: () => void }) {
  const t = useT()
  const create = useCreateClient()
  const schema = useMemo(
    () =>
      z.object({
        code: z.string().trim().max(30, t('examples.clients.errors.codeMax')),
        name: z.string().trim().min(1, t('examples.clients.errors.nameRequired')),
        legalName: z.string().trim(),
        taxId: z.string().trim(),
        creditLimit: z.number(t('examples.clients.errors.creditLimitNumber')).min(0, t('examples.clients.errors.creditLimitMin')).nullable(),
      }),
    [t],
  )
  const form = useForm({
    resolver: zodResolver(schema),
    defaultValues: { code: '', name: '', legalName: '', taxId: '', creditLimit: null },
  })
  const formId = 'example-client-create'

  const close = () => {
    form.reset()
    onClose()
  }

  return (
    <Modal
      open={open}
      title={t('examples.clients.new')}
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
          // strings vacíos → null: el API distingue "no enviado" de "vacío"
          await create.mutateAsync({
            code: v.code || null,
            name: v.name,
            legalName: v.legalName || null,
            taxId: v.taxId || null,
            creditLimit: v.creditLimit,
          })
          toast.success(t('examples.clients.created'))
          close()
        }}
      >
        <div className="r2">
          <Field name="code" label={t('examples.clients.code')} help={t('examples.clients.codeHelp')}>
            <TextInput maxLength={30} />
          </Field>
          <Field name="name" label={t('examples.clients.name')} required>
            <TextInput />
          </Field>
        </div>
        <Field name="legalName" label={t('examples.clients.legalName')}>
          <TextInput />
        </Field>
        <div className="r2">
          <Field name="taxId" label={t('examples.clients.taxId')}>
            <TextInput />
          </Field>
          <Field name="creditLimit" label={t('examples.clients.creditLimit')}>
            <NumberInput min={0} />
          </Field>
        </div>
      </Form>
    </Modal>
  )
}

// ---- Pantalla ----
export default function ClientsListExample() {
  const t = useT()
  const navigate = useNavigate()
  const [show, setShow] = useState<'active' | 'all'>('active')
  const [statuses, setStatuses] = useState<string[]>([])
  const [q, setQ] = useState('')
  const [creating, setCreating] = useState(false)
  const [toDeactivate, setToDeactivate] = useState<Client | null>(null)
  const deactivate = useDeactivateClient()

  // Filtro estructurado que va al API: includeInactive
  const { data, isLoading, error } = useClients(show === 'all')
  const all = data

  // Opciones del filtro de estatus: catálogo del tenant (etiquetas ya traducidas), nunca strings sueltos
  const { data: clientStatuses } = useStatuses(STATUS_DOMAIN)
  const statusOptions = useMemo(() => (clientStatuses ?? []).map((s) => ({ value: s.code, label: s.label })), [clientStatuses])

  // Filtros estructurados locales y, DESPUÉS, la búsqueda libre sobre lo que se muestra
  const rows = useMemo(
    () =>
      (all ?? []).filter(
        (c) =>
          (statuses.length === 0 || statuses.includes(c.status ?? '')) &&
          matchesQ(q, c.code, c.name, c.statusLabel, c.billingSummary),
      ),
    [all, statuses, q],
  )

  const columns = useMemo<DataColumn<Client>[]>(
    () => [
      { id: 'code', header: t('examples.clients.code'), cell: (c) => <span className="ref">{c.code}</span>, sortValue: (c) => c.code, card: 'title' },
      { id: 'name', header: t('examples.clients.name'), cell: (c) => c.name, sortValue: (c) => c.name },
      {
        id: 'status',
        header: t('examples.clients.status'),
        cell: (c) => (
          <>
            <StatusChip domain={STATUS_DOMAIN} code={c.status} label={c.statusLabel} />
            {!c.isActive && <> <Chip tone="fail">{t('examples.clients.inactive')}</Chip></>}
          </>
        ),
        sortValue: (c) => c.statusLabel,
      },
      { id: 'billing', header: t('examples.clients.billing'), cell: (c) => c.billingSummary, card: 'hidden' },
      { id: 'contracts', header: t('examples.clients.contracts'), cell: (c) => c.contractsCount, sortValue: (c) => c.contractsCount, align: 'end' },
    ],
    [t],
  )

  const actions = useMemo<RowAction<Client>[]>(
    () => [
      { key: 'open', label: t('examples.clients.view'), onClick: (c) => navigate(`/clients/${c.publicId}`) },
      {
        key: 'deactivate',
        label: t('examples.clients.deactivate'),
        perm: 'clients.update',
        visible: (c) => c.isActive === true, // guarda de estatus: solo los activos se dan de baja
        onClick: (c) => setToDeactivate(c),
        tone: 'danger',
      },
    ],
    [t, navigate],
  )

  return (
    <div className="wrap">
      <div className="head">
        <div>
          <h1>{t('examples.clients.title')}</h1>
          <p>{t('examples.clients.subtitle')}</p>
        </div>
        <div className="act">
          <Can perm="clients.create">
            <button type="button" className="btn flow" onClick={() => setCreating(true)}>
              {t('examples.clients.new')}
            </button>
          </Can>
        </div>
      </div>

      <Filters
        onClear={() => {
          setShow('active')
          setStatuses([])
          setQ('')
        }}
      >
        <SelectFilter
          label={t('examples.clients.show')}
          value={show}
          allLabel={null}
          onChange={(v) => setShow(v === 'all' ? 'all' : 'active')}
          options={[
            { value: 'active', label: t('examples.clients.onlyActive') },
            { value: 'all', label: t('examples.clients.includeInactive') },
          ]}
        />
        <SearchSelect label={t('examples.clients.status')} options={statusOptions} value={statuses} onChange={setStatuses} />
      </Filters>

      <Panel flush icon={<IconUsers />} title={t('examples.clients.title')} badge={data ? rows.length : undefined}>
        <div className="qrow">
          <QBox value={q} onChange={setQ} />
        </div>
        {error ? (
          <p className="pb ferr" role="alert">
            {error.message}
          </p>
        ) : (
          <DataTable
            label={t('examples.clients.title')}
            columns={columns}
            rows={rows}
            rowKey={(c) => c.publicId ?? String(c.id)}
            defaultSort={{ id: 'code', desc: false }}
            pageSize={25}
            loading={isLoading}
            rowActions={actions}
            onRowClick={(c) => navigate(`/clients/${c.publicId}`)}
          />
        )}
      </Panel>

      <CreateClientModal open={creating} onClose={() => setCreating(false)} />

      <ConfirmDialog
        open={toDeactivate !== null}
        tone="danger"
        title={t('examples.clients.deactivateTitle')}
        message={t('examples.clients.deactivateBody', { name: toDeactivate?.name ?? '' })}
        confirmLabel={t('examples.clients.deactivate')}
        onConfirm={async () => {
          if (!toDeactivate?.publicId) return
          await deactivate.mutateAsync(toDeactivate.publicId)
          toast.success(t('examples.clients.deactivated'))
        }}
        onClose={() => setToDeactivate(null)}
      />
    </div>
  )
}
