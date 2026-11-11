// Preferencias propias de la persona guardadas en el servidor (`GET/PUT /api/v1/me/preferences/{key}`), por compañía. Claves permitidas: las del API
// (`UserPreferenceKeys`): `sales.entryMode` («wizard» | «full») y `warehouse.last` (PublicId del último almacén usado).
import { useCallback } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, unwrap } from '../api/client'

export type PreferenceKey = 'sales.entryMode' | 'warehouse.last'

const keyOf = (key: PreferenceKey) => ['me', 'preferences', key] as const

/** `const [value, setValue, ready] = useUserPreference('warehouse.last')` — `value` es null si nunca se guardó; `ready` es true cuando ya respondió el servidor. */
export function useUserPreference(key: PreferenceKey, enabled = true): [string | null, (value: string) => void, boolean] {
  const qc = useQueryClient()
  const q = useQuery({
    queryKey: keyOf(key),
    queryFn: async () => (await unwrap(api.GET('/api/v1/me/preferences/{key}', { params: { path: { key } } }))).value ?? null,
    enabled,
    staleTime: Infinity,
  })
  const save = useMutation({
    mutationFn: (value: string) => unwrap(api.PUT('/api/v1/me/preferences/{key}', { params: { path: { key } }, body: { value } })),
  })
  const { mutate } = save
  const set = useCallback(
    (value: string) => {
      qc.setQueryData(keyOf(key), value) // se ve al instante; si el servidor falla, la próxima lectura lo corrige
      mutate(value)
    },
    [qc, key, mutate],
  )
  return [q.data ?? null, set, !enabled || q.isSuccess || q.isError]
}
