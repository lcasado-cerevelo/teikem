// Carga de datos de una pantalla: llama a `load` al montar (y cuando cambian las dependencias) y devuelve datos, error y `reload`. Ignora respuestas de cargas ya viejas.
import { useCallback, useEffect, useRef, useState } from 'react'

export interface AsyncState<T> {
  data: T | null
  error: unknown
  loading: boolean
  reload: () => void
}

export function useAsync<T>(load: () => Promise<T>, deps: readonly unknown[]): AsyncState<T> {
  const [data, setData] = useState<T | null>(null)
  const [error, setError] = useState<unknown>(null)
  const [loading, setLoading] = useState(true)
  const [tick, setTick] = useState(0)
  const seq = useRef(0)

  useEffect(() => {
    const mine = ++seq.current
    setLoading(true)
    load()
      .then((value) => {
        if (mine !== seq.current) return
        setData(value)
        setError(null)
      })
      .catch((e: unknown) => {
        if (mine !== seq.current) return
        setError(e)
      })
      .finally(() => {
        if (mine === seq.current) setLoading(false)
      })
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [...deps, tick])

  const reload = useCallback(() => setTick((n) => n + 1), [])
  return { data, error, loading, reload }
}
