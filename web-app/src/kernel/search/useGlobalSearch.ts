// `GET /api/v1/search?q=`: los resultados de la búsqueda global. Con retraso entre teclas, sin consultar con menos de dos letras y conservando los resultados
// anteriores mientras llegan los nuevos (la lista no parpadea).
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { api, unwrap } from '../api/client'
import { SEARCH_DEBOUNCE_MS, SEARCH_MIN_LENGTH, type SearchHit } from './searchRules'

export interface GlobalSearchState {
  hits: readonly SearchHit[]
  /** Se está esperando al servidor (o a que termine de escribir). */
  loading: boolean
  /** El texto con que se consultó (ya sin espera). */
  query: string
  /** Los resultados corresponden al texto actual (no hay espera ni consulta en curso). */
  settled: boolean
}

export function useGlobalSearch(text: string): GlobalSearchState {
  const typed = text.trim()
  const [debounced, setDebounced] = useState(typed)
  useEffect(() => {
    const id = setTimeout(() => setDebounced(typed), SEARCH_DEBOUNCE_MS)
    return () => clearTimeout(id)
  }, [typed])
  const enabled = debounced.length >= SEARCH_MIN_LENGTH
  const q = useQuery({
    queryKey: ['/api/v1/search', debounced],
    queryFn: () => unwrap(api.GET('/api/v1/search', { params: { query: { q: debounced } } })),
    enabled,
    staleTime: 15_000,
    placeholderData: keepPreviousData,
    meta: { handleAccessDenied: false },
  })
  const waiting = typed !== debounced
  const hits = enabled && typed.length >= SEARCH_MIN_LENGTH ? (q.data?.hits ?? []) : []
  return { hits, loading: typed.length >= SEARCH_MIN_LENGTH && (waiting || q.isFetching), query: debounced, settled: enabled && !waiting && !q.isFetching && q.isSuccess }
}
