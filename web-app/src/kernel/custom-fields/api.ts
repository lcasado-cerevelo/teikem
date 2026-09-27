// Lecturas y guardado de campos personalizados (definiciones por tipo de entidad y valores por registro).
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useCallback } from 'react'
import type { FieldValues, Path, PathValue, UseFormReturn } from 'react-hook-form'
import { useModule } from '../access/accessContext'
import { ModuleKeys } from '../access/modules'
import { api, unwrap } from '../api/client'
import { applyProblemDetails, type AppliedProblem } from '../api/problem'
import { dataTypeOf, fromFormValue, toFormValue, type CustomFieldDefinitionDto, type CustomFieldFormValue } from './values'

/** Nombre del objeto dentro del formulario donde viven los campos personalizados: `customFields.<fieldKey>`. */
export const CUSTOM_FIELDS_NAME = 'customFields'

export const customFieldKeys = {
  definitions: (entityType: string) => ['/api/v1/custom-fields/definitions', { entityType }] as const,
  values: (entityType: string, entityId: number) => ['/api/v1/custom-fields/values/{entityType}/{entityId}', { entityType, entityId }] as const,
}

async function fetchDefinitions(entityType: string): Promise<CustomFieldDefinitionDto[]> {
  const rows = await unwrap(api.GET('/api/v1/custom-fields/definitions', { params: { query: { entityType, includeInactive: false } } }))
  return rows.filter((d) => d.isActive !== false && !!d.fieldKey).sort((a, b) => (a.sortOrder ?? 0) - (b.sortOrder ?? 0))
}

/** true si el módulo CUSTOM_FIELDS está encendido (el API responde 403 module_disabled si no). */
export function useCustomFieldsEnabled(): boolean {
  return useModule(ModuleKeys.CustomFields)
}

/**
 * Definiciones activas del tipo de entidad (`GET /api/v1/custom-fields/definitions?entityType=`), por SortOrder.
 * Con el módulo CUSTOM_FIELDS apagado no consulta.
 */
export function useCustomFieldDefinitions(entityType: string | null | undefined) {
  const on = useCustomFieldsEnabled()
  return useQuery({
    queryKey: customFieldKeys.definitions(entityType ?? ''),
    queryFn: () => fetchDefinitions(entityType ?? ''),
    enabled: !!entityType && on,
    staleTime: 10 * 60_000,
    meta: { handleAccessDenied: false },
  })
}

/**
 * Valores del registro (`GET /api/v1/custom-fields/values/{entityType}/{entityId}`), con el default si no hay valor.
 * Con el módulo CUSTOM_FIELDS apagado no consulta.
 */
export function useCustomFieldValues(entityType: string | null | undefined, entityId: number | null | undefined) {
  const on = useCustomFieldsEnabled()
  return useQuery({
    queryKey: customFieldKeys.values(entityType ?? '', entityId ?? 0),
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/custom-fields/values/{entityType}/{entityId}', {
          params: { path: { entityType: entityType ?? '', entityId: entityId ?? 0 } },
        }),
      ),
    enabled: !!entityType && typeof entityId === 'number' && entityId > 0 && on,
    meta: { handleAccessDenied: false },
  })
}

function fieldPath<T extends FieldValues>(name: string, key: string): Path<T> {
  return `${name}.${key}` as Path<T>
}

/**
 * Guardar los campos personalizados del formulario: `const { save } = useSaveCustomFields('CLIENT')` y, en el submit,
 * `const problem = await save(entityId, form)` (tras crear el registro, con su id). Devuelve null si guardó; si el API
 * rechaza, pone cada error bajo su campo (`customFields.<fieldKey>`) y devuelve { title, code, errors } para el toast.
 * Nunca lanza. Con el módulo CUSTOM_FIELDS apagado (o si las definiciones no se pueden leer) no hay nada que guardar:
 * devuelve null sin llamar al API.
 */
export function useSaveCustomFields(entityType: string) {
  const queryClient = useQueryClient()
  const on = useCustomFieldsEnabled()
  const definitions = useCustomFieldDefinitions(entityType)
  const mutation = useMutation({
    mutationFn: ({ entityId, values }: { entityId: number; values: Record<string, unknown> }) =>
      unwrap(api.PUT('/api/v1/custom-fields/values/{entityType}/{entityId}', { params: { path: { entityType, entityId } }, body: { values } })),
    onSuccess: (data, vars) => queryClient.setQueryData(customFieldKeys.values(entityType, vars.entityId), data),
  })
  const { mutateAsync } = mutation
  const defs = definitions.data

  const save = useCallback(
    async <T extends FieldValues>(entityId: number, form: UseFormReturn<T>, name: string = CUSTOM_FIELDS_NAME): Promise<AppliedProblem | null> => {
      if (!on) return null
      let list = defs
      if (!list) {
        try {
          list = await queryClient.fetchQuery({
            queryKey: customFieldKeys.definitions(entityType),
            queryFn: () => fetchDefinitions(entityType),
            meta: { handleAccessDenied: false },
          })
        } catch {
          // Sin definiciones legibles no hay campos en el formulario: nada que guardar.
          return null
        }
      }
      if (list.length === 0) return null
      const current = (form.getValues(name as Path<T>) ?? {}) as Record<string, CustomFieldFormValue | undefined>
      const values: Record<string, unknown> = {}
      for (const def of list) {
        const key = def.fieldKey ?? ''
        values[key] = fromFormValue(dataTypeOf(def), current[key])
      }
      try {
        const saved = await mutateAsync({ entityId, values })
        // Lo guardado pasa a ser el valor por defecto (el formulario deja de verlo como modificado).
        for (const def of list) {
          const key = def.fieldKey ?? ''
          const row = saved.find((v) => v.fieldKey?.toLowerCase() === key.toLowerCase())
          const value = toFormValue(dataTypeOf(def), row?.value) as PathValue<T, Path<T>>
          form.resetField(fieldPath<T>(name, key), { defaultValue: value })
        }
        return null
      } catch (error) {
        const applied = applyProblemDetails(error)
        for (const [field, messages] of Object.entries(applied.errors)) {
          const def = list.find((d) => d.fieldKey?.toLowerCase() === field.toLowerCase())
          if (def?.fieldKey && messages.length > 0) {
            form.setError(fieldPath<T>(name, def.fieldKey), { type: 'server', message: messages.join(' ') })
          }
        }
        return applied
      }
    },
    [on, defs, entityType, mutateAsync, queryClient],
  )

  return { save, isPending: mutation.isPending }
}
