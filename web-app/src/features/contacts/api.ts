// Medios de contacto (ContactPoint) de cualquier entidad dueña: lectura `GET /contacts/{ownerEntity}/{ownerId}` y escritura
// (`contacts.manage` MÁS el permiso de edición de la entidad dueña; p. ej. `orders.edit` para TRANSPORT_ORDER).
import { useMutation, useQuery } from '@tanstack/react-query'
import { api, unwrap } from '../../kernel/api/client'
import type { components } from '../../kernel/api/schema'

type Schemas = components['schemas']

export type ContactPointRequest = Schemas['ContactPointUpsertRequest']

export const contactKeys = {
  of: (ownerEntity: string, ownerId: number) => ['/api/v1/contacts/{ownerEntity}/{ownerId}', ownerEntity, ownerId] as const,
}

/** Medios de contacto activos del dueño. Consulta secundaria: un 403 no saca de la pantalla. */
export function useContactPoints(ownerEntity: string, ownerId: number | null | undefined, enabled = true) {
  return useQuery({
    queryKey: contactKeys.of(ownerEntity, ownerId ?? 0),
    queryFn: () => unwrap(api.GET('/api/v1/contacts/{ownerEntity}/{ownerId}', { params: { path: { ownerEntity, ownerId: ownerId ?? 0 } } })),
    enabled: !!ownerId && enabled,
    meta: { handleAccessDenied: false },
  })
}

export type ContactPointAction =
  | { action: 'create'; ownerEntity: string; ownerId: number; body: ContactPointRequest }
  | { action: 'update'; id: number; body: ContactPointRequest }
  | { action: 'remove'; id: number }

/** Alta, edición y baja de un medio de contacto; `onChanged` relee lo que el dueño muestra (un principal nuevo desmarca a otro). */
export function useContactPointActions(onChanged: () => Promise<unknown> | void) {
  return useMutation({
    mutationFn: async (req: ContactPointAction) => {
      if (req.action === 'create')
        return unwrap(api.POST('/api/v1/contacts/{ownerEntity}/{ownerId}', { params: { path: { ownerEntity: req.ownerEntity, ownerId: req.ownerId } }, body: req.body }))
      if (req.action === 'update') return unwrap(api.PUT('/api/v1/contacts/{id}', { params: { path: { id: req.id } }, body: req.body }))
      return unwrap(api.DELETE('/api/v1/contacts/{id}', { params: { path: { id: req.id } } }))
    },
    onSuccess: () => onChanged(),
  })
}
