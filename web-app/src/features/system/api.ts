// API compartida de `features/system` (Sistema): Roles y usuarios + PIN de otros usuarios (P4) y Catálogos de
// valores (P6) en un mismo archivo (dos piezas del mismo lote, mismo módulo de pantallas).
//
// --- P4: Roles y usuarios (SecurityControllers.cs: RolesController, UsersController) ---
// Escrituras sensibles (PUT/DELETE roles, roles/permisos/PIN de un usuario) llevan [RequireAal2]: la pantalla pide
// `reauth()` antes de llamarlas (mismo patrón que `account/MfaTab.tsx`).
//
// --- P6: Catálogos de valores ---
// Comparte el prefijo de clave de consulta con `kernel/catalogs` (`useLookups`) para que una escritura aquí invalide
// también los selects/chips que ya tienen ese catálogo en caché en cualquier otra pantalla.
import { useMutation, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../../kernel/api/client'
import type { components } from '../../kernel/api/schema'

// ---- P4: Roles y usuarios ----
export type RoleDto = components['schemas']['RoleDto']
export type RoleUpsertRequest = components['schemas']['RoleUpsertRequest']
export type PermissionDto = components['schemas']['PermissionDto']
export type UserSummaryDto = components['schemas']['UserSummaryDto']
export type UserCreateRequest = components['schemas']['UserCreateRequest']
export type UserUpdateRequest = components['schemas']['UserUpdateRequest']
export type MembershipStatusRequest = components['schemas']['MembershipStatusRequest']
export type PinStatusDto = components['schemas']['PinStatusDto']
export type PinAdminSetRequest = components['schemas']['PinAdminSetRequest']
export type UserCreateResponseDto = components['schemas']['UserCreateResponseDto']

export const ROLES_KEY = ['/api/v1/roles'] as const
export const PERMISSIONS_KEY = ['/api/v1/permissions'] as const
export const USERS_KEY = ['/api/v1/users'] as const

/** Catálogo de permisos (sin permiso propio: cualquier autenticado). */
export function usePermissions() {
  return useQuery({ queryKey: PERMISSIONS_KEY, queryFn: () => unwrap(api.GET('/api/v1/permissions')) })
}

/** Roles del tenant (sin permiso propio: se usa también para asignar roles a un usuario). */
export function useRoles() {
  return useQuery({ queryKey: ROLES_KEY, queryFn: () => unwrap(api.GET('/api/v1/roles')) })
}

/** Plantillas de sistema (`TenantId` NULL) de las que se clonaron los roles que trae cada compañía: `GET /roles?includeTemplates=true`
 *  filtrado a `isTemplate`. Solo para la nota de la pestaña Roles (qué roles "vienen ya armados"); un 403 no saca de la pantalla. */
export function useRoleTemplates() {
  const query = { includeTemplates: true }
  return useQuery({
    queryKey: [...ROLES_KEY, query],
    queryFn: () => unwrap(api.GET('/api/v1/roles', { params: { query } })),
    select: (roles) => roles.filter((r) => r.isTemplate),
    staleTime: 10 * 60 * 1000,
    meta: { handleAccessDenied: false },
  })
}

/** Crear (`id: null`) o editar (`PUT`, AAL2) un rol. */
export function useSaveRole() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, body }: { id: number | null; body: RoleUpsertRequest }) =>
      id
        ? unwrap(api.PUT('/api/v1/roles/{id}', { params: { path: { id } }, body }))
        : unwrap(api.POST('/api/v1/roles', { body })),
    onSuccess: () => qc.invalidateQueries({ queryKey: ROLES_KEY }),
  })
}

/** `DELETE /api/v1/roles/{id}` (AAL2). "El rol tiene usuarios asignados; reasígnelos antes de eliminarlo." si está en uso. */
export function useDeleteRole() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: number) => unwrap(api.DELETE('/api/v1/roles/{id}', { params: { path: { id } } })),
    onSuccess: () => qc.invalidateQueries({ queryKey: ROLES_KEY }),
  })
}

/** `GET /api/v1/users` (admin.users): sin paginación de servidor; la pantalla pagina y busca en cliente. */
export function useUsers() {
  return useQuery({ queryKey: USERS_KEY, queryFn: () => unwrap(api.GET('/api/v1/users')) })
}

/** 2026-10-01: otras compañías a las que quien crea el usuario puede agregarlo (`GET /users/assignable-companies`). */
export function useAssignableCompanies(enabled = true) {
  return useQuery({
    queryKey: ['users', 'assignable-companies'],
    queryFn: () => unwrap(api.GET('/api/v1/users/assignable-companies')),
    enabled,
  })
}

export function useCreateUser() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (body: UserCreateRequest) => unwrap(api.POST('/api/v1/users', { body })),
    onSuccess: () => qc.invalidateQueries({ queryKey: USERS_KEY }),
  })
}

/** Nombre y activo (`PUT /api/v1/users/{id}`). "No puede desactivarse a sí mismo." si aplica sobre el propio usuario. */
export function useUpdateUser() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, body }: { id: number; body: UserUpdateRequest }) =>
      unwrap(api.PUT('/api/v1/users/{id}', { params: { path: { id } }, body })),
    onSuccess: () => qc.invalidateQueries({ queryKey: USERS_KEY }),
  })
}

/** `PUT /api/v1/users/{id}/roles` (AAL2). "Roles desconocidos: …" si alguno no existe. */
export function useSetUserRoles() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, roles }: { id: number; roles: string[] }) =>
      unwrap(api.PUT('/api/v1/users/{id}/roles', { params: { path: { id } }, body: { roles } })),
    onSuccess: () => qc.invalidateQueries({ queryKey: USERS_KEY }),
  })
}

/** `PUT /api/v1/users/{id}/permissions` (AAL2): permisos extra, solo para esta persona. */
export function useSetUserExtraPermissions() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, permissions }: { id: number; permissions: string[] }) =>
      unwrap(api.PUT('/api/v1/users/{id}/permissions', { params: { path: { id } }, body: { permissions } })),
    onSuccess: () => qc.invalidateQueries({ queryKey: USERS_KEY }),
  })
}

/** `PUT /api/v1/users/{id}/membership`. "No puede cambiar su propia membresía." si aplica sobre el propio usuario. */
export function useSetMembership() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, status }: { id: number; status: string }) =>
      unwrap(api.PUT('/api/v1/users/{id}/membership', { params: { path: { id } }, body: { status } })),
    onSuccess: () => qc.invalidateQueries({ queryKey: USERS_KEY }),
  })
}

/** `DELETE /api/v1/users/{id}/sessions`. */
export function useCloseUserSessions() {
  return useMutation({
    mutationFn: (id: number) => unwrap(api.DELETE('/api/v1/users/{id}/sessions', { params: { path: { id } } })),
  })
}

/** `PUT /api/v1/users/{id}/pin` (AAL2). Errores por campo `pin` (formato, secuencia trivial: manual 08). */
export function useSetUserPin() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, pin }: { id: number; pin: string }) =>
      unwrap(api.PUT('/api/v1/users/{id}/pin', { params: { path: { id } }, body: { pin } })),
    onSuccess: () => qc.invalidateQueries({ queryKey: USERS_KEY }),
  })
}

/** `DELETE /api/v1/users/{id}/pin` (AAL2). */
export function useRemoveUserPin() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: number) => unwrap(api.DELETE('/api/v1/users/{id}/pin', { params: { path: { id } } })),
    onSuccess: () => qc.invalidateQueries({ queryKey: USERS_KEY }),
  })
}

/** `PUT /api/v1/users/{id}/mfa` (AAL2): exige (o deja de exigir) MFA a este usuario en la compañía activa. */
export function useSetMfaRequired() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, required }: { id: number; required: boolean }) =>
      unwrap(api.PUT('/api/v1/users/{id}/mfa', { params: { path: { id } }, body: { required } })),
    onSuccess: () => qc.invalidateQueries({ queryKey: USERS_KEY }),
  })
}

/** `DELETE /api/v1/users/{id}/mfa` (AAL2): resetea el MFA de otro usuario que perdió su dispositivo. */
export function useResetUserMfa() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: number) => unwrap(api.DELETE('/api/v1/users/{id}/mfa', { params: { path: { id } } })),
    onSuccess: () => qc.invalidateQueries({ queryKey: USERS_KEY }),
  })
}

// ---- P6: Catálogos de valores ----
// Comparte el prefijo de clave de consulta con `kernel/catalogs` (`useLookups`) para que una escritura aquí invalide
// también los selects/chips que ya tienen ese catálogo en caché en cualquier otra pantalla.
export type CatalogDomain = components['schemas']['CatalogDomainDto']
export type LookupValue = components['schemas']['LookupValueDto']
export type LookupCodeUpsertRequest = components['schemas']['LookupCodeUpsertRequest']
export type LookupOverrideRequest = components['schemas']['LookupOverrideRequest']
export type CatalogListCreateRequest = components['schemas']['CatalogListCreateRequest']

/** Prefijo de ruta de todo el módulo de catálogos: se usa para invalidar por igual esta pantalla y `kernel/catalogs`. */
export const CATALOG_QUERY_PREFIX = '/api/v1/catalogs'

export function useCatalogDomains() {
  return useQuery({
    queryKey: [`${CATALOG_QUERY_PREFIX}/domains`],
    queryFn: () => unwrap(api.GET('/api/v1/catalogs/domains')),
  })
}

/** Valores de un dominio con los deshabilitados incluidos (para la administración; distinto de `useLookups`). */
export function useCatalogValues(entity: string | null) {
  return useQuery({
    queryKey: [`${CATALOG_QUERY_PREFIX}/{entity}`, { entity, admin: true }],
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/catalogs/{entity}', {
          params: { path: { entity: entity ?? '' }, query: { includeDisabled: true } },
        }),
      ),
    enabled: !!entity,
  })
}

function invalidateCatalogs(qc: QueryClient) {
  return qc.invalidateQueries({
    predicate: (q) => typeof q.queryKey[0] === 'string' && q.queryKey[0].startsWith(CATALOG_QUERY_PREFIX),
  })
}

export function useCreateList() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (body: CatalogListCreateRequest) => unwrap(api.POST('/api/v1/catalogs/lists', { body })),
    onSuccess: () => invalidateCatalogs(qc),
  })
}

export function useDeleteList() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (domainKey: string) =>
      unwrap(api.DELETE('/api/v1/catalogs/lists/{domainKey}', { params: { path: { domainKey } } })),
    onSuccess: () => invalidateCatalogs(qc),
  })
}

export function useCreateValue(entity: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (body: LookupCodeUpsertRequest) =>
      unwrap(api.POST('/api/v1/catalogs/{entity}', { params: { path: { entity } }, body })),
    onSuccess: () => invalidateCatalogs(qc),
  })
}

export function useUpdateValue(entity: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ code, body }: { code: string; body: LookupCodeUpsertRequest }) =>
      unwrap(api.PUT('/api/v1/catalogs/{entity}/{code}', { params: { path: { entity, code } }, body })),
    onSuccess: () => invalidateCatalogs(qc),
  })
}

export function useDeactivateValue(entity: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (code: string) => unwrap(api.DELETE('/api/v1/catalogs/{entity}/{code}', { params: { path: { entity, code } } })),
    onSuccess: () => invalidateCatalogs(qc),
  })
}

/** `POST {entity}/{code}/restore`: restaura un valor propio desactivado. Ver KIT.md/decisiones: la lectura del dominio
 * (`GET {entity}`) excluye siempre los valores con `IsActive=false`, así que hoy no hay dato en la pantalla para ofrecer
 * este botón; se deja disponible para cuando el backend permita listarlos. */
export function useRestoreValue(entity: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (code: string) =>
      unwrap(api.POST('/api/v1/catalogs/{entity}/{code}/restore', { params: { path: { entity, code } } })),
    onSuccess: () => invalidateCatalogs(qc),
  })
}

export function useSetOverride(entity: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ code, body }: { code: string; body: LookupOverrideRequest }) =>
      unwrap(api.PUT('/api/v1/catalogs/{entity}/{code}/override', { params: { path: { entity, code } }, body })),
    onSuccess: () => invalidateCatalogs(qc),
  })
}

export function useRemoveOverride(entity: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (code: string) =>
      unwrap(api.DELETE('/api/v1/catalogs/{entity}/{code}/override', { params: { path: { entity, code } } })),
    onSuccess: () => invalidateCatalogs(qc),
  })
}

/**
 * Origen resuelto de un valor para la columna "Origen" de la tabla. Se decide por `tenantId` (como el servidor:
 * `LookupService.EnsureCanEditDomain` mira `domain.TenantId`, no `IsSystem`), no por `row.isSystem`: un valor sin
 * dueño (`tenantId == null`) es de un dominio global aunque no esté marcado `isSystem` (lo agregó el administrador
 * de plataforma directo a la lista global) — editarlo/desactivarlo lo sigue exigiendo igual, solo el admin de
 * plataforma puede.
 */
export function valueOrigin(row: LookupValue): 'system' | 'adjusted' | 'own' {
  if (row.tenantId != null) return 'own'
  return row.isOverridden ? 'adjusted' : 'system'
}
