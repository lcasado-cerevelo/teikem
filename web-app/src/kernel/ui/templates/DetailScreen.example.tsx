// PLANTILLA — pantalla de detalle (ficha). Copiar a features/<modulo>/<Pantalla>.tsx y ajustar. No está montada en rutas.
// Patrón: cabecera (identidad + estatus + acciones guardadas por permiso Y por estatus/capabilities del DTO) → pestañas →
// un formulario por pestaña (guarda con rowVersion; 409 = otro usuario lo cambió) o una tabla de hijos.
// Bajo el título va <StatusPipeline> (kernel/catalogs): ofrece las transiciones válidas y llama al endpoint del módulo.
// Ejemplo real: ficha de cliente (GET /api/v1/clients/{publicId}, PATCH .../profile, POST .../deactivate|reactivate).
import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useMemo, useState } from 'react'
import { useForm } from 'react-hook-form'
import { Link, useParams } from 'react-router-dom'
import { z } from 'zod'
import { Can, useCan } from '../../access'
import { api, unwrap } from '../../api/client'
import { ApiError } from '../../api/problem'
import type { components } from '../../api/schema'
import { StatusPipeline } from '../../catalogs'
import { useT } from '../../i18n/useT'
import { Chip } from '../Chip'
import { ConfirmDialog } from '../ConfirmDialog'
import { DataTable, type DataColumn } from '../DataTable'
import { EmptyState } from '../EmptyState'
import { Field, Form, NumberInput, TextInput } from '../Form'
import { Panel } from '../Panel'
import { Spinner } from '../Spinner'
import { Tabs } from '../Tabs'
import { toast } from '../toast'

type ClientDetail = components['schemas']['ClientDetailDto']
type Contact = components['schemas']['ClientContactDto']
type TabKey = 'profile' | 'contacts'

// ---- API del módulo (en una pantalla real va en features/<modulo>/api.ts) ----
const detailKey = (publicId: string) => ['/api/v1/clients/{publicId}', publicId] as const
/** Dominio de estatus y código EntityType del cliente (CatalogDomains.ClientStatus / EntityTypes.Client). */
const STATUS_DOMAIN = 'ClientStatus'
const ENTITY_TYPE = 'CLIENT'

function useClient(publicId: string) {
  return useQuery({
    queryKey: detailKey(publicId),
    queryFn: () => unwrap(api.GET('/api/v1/clients/{publicId}', { params: { path: { publicId } } })),
  })
}

/** Cambio de estatus: solo por el endpoint del módulo (StatusService en el servidor valida y escribe historial). */
function useTransition(publicId: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (body: components['schemas']['StatusChangeRequest']) =>
      unwrap(api.POST('/api/v1/clients/{publicId}/status', { params: { path: { publicId } }, body })),
    onSuccess: (updated) => {
      qc.setQueryData(detailKey(publicId), updated)
      void qc.invalidateQueries({ queryKey: ['/api/v1/clients'] })
    },
  })
}

function useSetActive(publicId: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (active: boolean) =>
      active
        ? unwrap(api.POST('/api/v1/clients/{publicId}/reactivate', { params: { path: { publicId } } }))
        : unwrap(api.POST('/api/v1/clients/{publicId}/deactivate', { params: { path: { publicId } } })),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: detailKey(publicId) })
      void qc.invalidateQueries({ queryKey: ['/api/v1/clients'] })
    },
  })
}

// ---- Pestaña Perfil: formulario con rowVersion ----
function ProfileTab({ client }: { client: ClientDetail }) {
  const t = useT()
  const qc = useQueryClient()
  const canEdit = useCan('clients.update')
  const publicId = client.publicId ?? ''
  const schema = useMemo(
    () =>
      z.object({
        legalName: z.string().trim(),
        taxId: z.string().trim(),
        creditLimit: z
          .number(t('examples.clients.errors.creditLimitNumber'))
          .min(0, t('examples.clients.errors.creditLimitMin'))
          .nullable()
          // En el PATCH, creditLimit null = 'no cambiar' (el API no tiene forma de borrarlo): si ya tenía valor, no se
          // deja vacío para no responder 'guardado' conservando el valor anterior.
          .refine((v) => v != null || client.creditLimit == null, t('examples.clients.errors.creditLimitRequired')),
      }),
    [t, client.creditLimit],
  )
  const form = useForm({
    resolver: zodResolver(schema),
    values: { legalName: client.legalName ?? '', taxId: client.taxId ?? '', creditLimit: client.creditLimit ?? null },
  })
  const save = useMutation({
    mutationFn: (body: components['schemas']['ClientProfileUpdateRequest']) =>
      unwrap(api.PATCH('/api/v1/clients/{publicId}/profile', { params: { path: { publicId } }, body })),
    onSuccess: (updated) => qc.setQueryData(detailKey(publicId), updated),
  })

  return (
    <Form
      form={form}
      onSubmit={async (v) => {
        // PATCH: null = 'no cambiar'; '' = borrar el valor. Los textos se envían tal cual (nunca `|| null`),
        // así vaciar el campo lo borra en el servidor.
        await save.mutateAsync({
          legalName: v.legalName,
          taxId: v.taxId,
          creditLimit: v.creditLimit,
          rowVersion: client.rowVersion, // 409 conflict si otro usuario guardó antes
        })
        toast.success(t('examples.clients.saved'))
      }}
      onError={(p) => {
        if (p.code === 'conflict') void qc.invalidateQueries({ queryKey: detailKey(publicId) })
      }}
    >
      <fieldset disabled={!canEdit} style={{ border: 0, padding: 0, margin: 0, minWidth: 0 }}>
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
      </fieldset>
      <Can perm="clients.update">
        <div className="form-acts">
          <button type="submit" className="btn flow" disabled={form.formState.isSubmitting || !form.formState.isDirty}>
            {form.formState.isSubmitting ? t('common.loading') : t('ui.form.save')}
          </button>
        </div>
      </Can>
    </Form>
  )
}

// ---- Pestaña Contactos: tabla de hijos (lista completa, orden local) ----
function ContactsTab({ contacts }: { contacts: readonly Contact[] }) {
  const t = useT()
  const columns = useMemo<DataColumn<Contact>[]>(
    () => [
      { id: 'fullName', header: t('examples.clients.contactName'), cell: (c) => c.fullName, sortValue: (c) => c.fullName, card: 'title' },
      { id: 'role', header: t('examples.clients.contactRole'), cell: (c) => c.role, sortValue: (c) => c.role },
      {
        id: 'flags',
        header: t('examples.clients.status'),
        cell: (c) => (
          <>
            {c.isPrimary && <Chip tone="route">{t('examples.clients.primary')}</Chip>}{' '}
            <Chip tone={c.isActive ? 'deliv' : 'warn'}>{c.isActive ? t('examples.clients.active') : t('examples.clients.inactive')}</Chip>
          </>
        ),
      },
    ],
    [t],
  )
  return (
    <DataTable
      label={t('examples.clients.tabContacts')}
      columns={columns}
      rows={contacts}
      rowKey={(c) => c.id ?? 0}
      empty={<EmptyState title={t('examples.clients.noContacts')} />}
    />
  )
}

// ---- Pantalla ----
const NO_CONTACTS: Contact[] = []

export default function ClientDetailExample() {
  const t = useT()
  const { publicId = '' } = useParams()
  const { data: client, isLoading, error } = useClient(publicId)
  const setActive = useSetActive(publicId)
  const transition = useTransition(publicId)
  const canUpdate = useCan('clients.update')
  const [tab, setTab] = useState<TabKey>('profile')
  const [confirm, setConfirm] = useState<'deactivate' | 'reactivate' | null>(null)

  if (isLoading) return <Spinner block />
  if (error || !client) {
    const notFound = error instanceof ApiError && error.code === 'not_found'
    return (
      <EmptyState
        title={notFound ? t('examples.clients.notFound') : (error?.message ?? t('errors.generic'))}
        action={
          <Link className="btn" to="/clients">
            {t('examples.clients.back')}
          </Link>
        }
      />
    )
  }

  return (
    <div className="wrap">
      <div className="head">
        <div style={{ minWidth: 0 }}>
          <h1>
            <span className="ref">{client.code}</span> · {client.name}
          </h1>
          <p>
            {!client.isActive && <Chip tone="fail">{t('examples.clients.inactive')}</Chip>} {client.billingSummary}
          </p>
        </div>
        <div className="act">
          {/* Acción guardada por permiso (Can) y por estado del DTO (isActive / capabilities.canX) */}
          <Can perm="clients.update">
            {client.isActive ? (
              <button type="button" className="btn danger" onClick={() => setConfirm('deactivate')}>
                {t('examples.clients.deactivate')}
              </button>
            ) : (
              <button type="button" className="btn" onClick={() => setConfirm('reactivate')}>
                {t('examples.clients.reactivate')}
              </button>
            )}
          </Can>
        </div>
      </div>

      <div style={{ marginBottom: 14 }}>
        <StatusPipeline
          domain={STATUS_DOMAIN}
          entityType={ENTITY_TYPE}
          entityId={client.id}
          currentCode={client.status}
          disabled={!canUpdate || !client.isActive}
          onTransition={(toCode, comment) => transition.mutateAsync({ toCode, comment: comment ?? null })}
        />
      </div>

      <div style={{ marginBottom: 14 }}>
        <Tabs<TabKey>
          label={t('examples.clients.title')}
          value={tab}
          onChange={setTab}
          tabs={[
            { key: 'profile', label: t('examples.clients.tabProfile') },
            { key: 'contacts', label: t('examples.clients.tabContacts') },
          ]}
        />
      </div>

      {tab === 'profile' && (
        <Panel title={t('examples.clients.tabProfile')}>
          <ProfileTab client={client} />
        </Panel>
      )}
      {tab === 'contacts' && (
        <Panel flush title={t('examples.clients.tabContacts')}>
          <ContactsTab contacts={client.contacts ?? NO_CONTACTS} />
        </Panel>
      )}

      <ConfirmDialog
        open={confirm !== null}
        tone={confirm === 'deactivate' ? 'danger' : 'flow'}
        title={confirm === 'deactivate' ? t('examples.clients.deactivateTitle') : t('examples.clients.reactivateTitle')}
        message={t(confirm === 'deactivate' ? 'examples.clients.deactivateBody' : 'examples.clients.reactivateBody', {
          name: client.name ?? '',
        })}
        confirmLabel={confirm === 'deactivate' ? t('examples.clients.deactivate') : t('examples.clients.reactivate')}
        onConfirm={async () => {
          await setActive.mutateAsync(confirm === 'reactivate')
          toast.success(confirm === 'deactivate' ? t('examples.clients.deactivated') : t('examples.clients.reactivated'))
        }}
        onClose={() => setConfirm(null)}
      />
    </div>
  )
}
