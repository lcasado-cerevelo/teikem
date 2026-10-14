// Panel «Teléfonos y correos» del propio cliente (ContactPoint con dueño CLIENT). Se lee de la ficha (`contactPoints`); la escritura
// exige contacts.manage y clients.update (el permiso de la entidad dueña). El panel es el compartido `ContactPointsPanel`.
import { useQueryClient } from '@tanstack/react-query'
import { ContactPointsPanel } from '../contacts/ContactPointsPanel'
import { clientKeys } from './api'
import type { ClientDetail } from './clientRules'

const WRITE_PERMS = ['contacts.manage', 'clients.update']

export function ClientContactPointsPanel({ client }: { client: ClientDetail }) {
  const queryClient = useQueryClient()
  return (
    <ContactPointsPanel
      ownerEntity="CLIENT"
      ownerId={client.id}
      points={client.contactPoints ?? []}
      perms={WRITE_PERMS}
      idPrefix="client"
      collapsible
      defaultCollapsed
      // un principal nuevo desmarca a otro del mismo tipo: se relee la ficha
      onChanged={() => queryClient.invalidateQueries({ queryKey: clientKeys.detail(client.publicId ?? '') })}
    />
  )
}
