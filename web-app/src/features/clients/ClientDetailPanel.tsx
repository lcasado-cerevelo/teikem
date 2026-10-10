// Ficha / expediente del cliente (derecha de la pantalla): paneles apilados, en el orden de la maqueta —
// cabecera (nombre, código, pipeline de estatus, baja/reactivación), perfil, teléfonos y correos, personas de contacto,
// numeración, campos personalizados, contratos (solo lectura) e historial de estatus.
import { Can, useCan } from '../../kernel/access'
import { applyProblemDetails } from '../../kernel/api/problem'
import { StatusHistory, StatusPipeline } from '../../kernel/catalogs'
import { useT } from '../../kernel/i18n'
import { Chip, ConfirmDialog, EmptyState, Panel, Spinner, toast } from '../../kernel/ui'
import { IconPower, IconRotateCcw } from '../../kernel/ui/actionIcons'
import { IconClock, IconUsers } from '../../kernel/ui/screenIcons'
import { useClient, useSetClientActive, useTransitionClient } from './api'
import { ClientContactPointsPanel } from './ClientContactPointsPanel'
import { ClientContractsPanel } from './ClientContractsPanel'
import { ClientCustomFieldsPanel } from './ClientCustomFieldsPanel'
import { ClientNumberingPanel } from './ClientNumberingPanel'
import { ClientPeoplePanel } from './ClientPeoplePanel'
import { ClientProfilePanel } from './ClientProfilePanel'
import { CLIENT_ENTITY_TYPE, CLIENT_STATUS_DOMAIN, type ClientDetail } from './clientRules'
import { useState } from 'react'

function ClientHeaderPanel({ client }: { client: ClientDetail }) {
  const t = useT()
  const publicId = client.publicId ?? ''
  const canUpdate = useCan('clients.update')
  const transition = useTransitionClient(publicId)
  const setActive = useSetClientActive(publicId)
  const [confirm, setConfirm] = useState<'deactivate' | 'reactivate' | null>(null)

  return (
    <Panel
      icon={<IconUsers />}
      title={client.name ?? client.code ?? ''}
      badge={client.code ?? undefined}
      actions={
        <Can perm="clients.update">
          {client.isActive ? (
            <button type="button" className="btn sm danger" onClick={() => setConfirm('deactivate')}>
              <IconPower /> {t('clients.deactivate')}
            </button>
          ) : (
            <button type="button" className="btn sm flow" onClick={() => setConfirm('reactivate')}>
              <IconRotateCcw /> {t('clients.reactivate')}
            </button>
          )}
        </Can>
      }
    >
      <div className="cl-head">
        {!client.isActive && (
          <p className="note cl-inactive-note">
            <Chip tone="cap">{t('clients.inactive')}</Chip> {t('clients.inactiveNote')}
          </p>
        )}
        <StatusPipeline
          domain={CLIENT_STATUS_DOMAIN}
          entityType={CLIENT_ENTITY_TYPE}
          entityId={client.id}
          currentCode={client.status}
          disabled={!canUpdate}
          onTransition={async (toCode, comment) => {
            await transition.mutateAsync({ toCode, comment: comment || null })
            toast.success(t('clients.statusChanged'))
          }}
        />
      </div>

      <ConfirmDialog
        open={confirm !== null}
        tone={confirm === 'deactivate' ? 'danger' : 'flow'}
        title={confirm === 'deactivate' ? t('clients.deactivateTitle') : t('clients.reactivateTitle')}
        message={t(confirm === 'deactivate' ? 'clients.deactivateBody' : 'clients.reactivateBody', { name: client.name ?? '' })}
        confirmLabel={confirm === 'deactivate' ? t('clients.deactivate') : t('clients.reactivate')}
        onConfirm={async () => {
          const active = confirm === 'reactivate'
          await setActive.mutateAsync(active)
          toast.success(t(active ? 'clients.reactivated' : 'clients.deactivated'))
        }}
        onClose={() => setConfirm(null)}
      />
    </Panel>
  )
}

export function ClientDetailPanel({ publicId }: { publicId: string }) {
  const t = useT()
  const { data: client, isPending, error } = useClient(publicId)

  if (isPending) {
    return (
      <Panel>
        <Spinner block />
      </Panel>
    )
  }
  if (error || !client) {
    return (
      <Panel>
        <EmptyState icon={<IconUsers />} title={error ? applyProblemDetails(error).title : t('clients.notFound')} />
      </Panel>
    )
  }

  return (
    <div className="cl-stack">
      <ClientHeaderPanel client={client} />
      <ClientProfilePanel client={client} />
      <ClientContactPointsPanel client={client} />
      <ClientPeoplePanel client={client} />
      <ClientNumberingPanel client={client} />
      <ClientCustomFieldsPanel client={client} />
      <ClientContractsPanel client={client} />
      <Panel icon={<IconClock />} title={t('status.history')}>
        <StatusHistory entityType={CLIENT_ENTITY_TYPE} entityId={client.id} domain={CLIENT_STATUS_DOMAIN} />
      </Panel>
    </div>
  )
}
