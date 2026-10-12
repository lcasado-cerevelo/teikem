// Cinta de ambiente (2026-10-12): en un ambiente que no es producción (staging) la web muestra SIEMPRE, en todas las pantallas, una franja
// roja arriba y una etiqueta abajo («STAGING · Ambiente de pruebas: no es producción»), y antepone la etiqueta al título de la pestaña.
// La etiqueta la informa el API en `GET /health` (`environment`, de Hosting:EnvironmentLabel; el instalador la pone en los sitios que no son
// «Teikem») junto con `database` (el nombre de la base a la que está conectado el sitio; solo en ambientes con etiqueta). La cinta dice
// «STAGING · Teikem_Staging · Ambiente de pruebas…», para ver de un vistazo contra qué base se trabaja. En producción es null y no se pinta nada; si /health no responde con JSON (p. ej. el servidor de desarrollo) tampoco.
import { useEffect, useState } from 'react'
import { useT } from '../i18n'

export interface EnvironmentInfo {
  label: string
  database: string | null
}

let cached: Promise<EnvironmentInfo | null> | undefined

const text = (v: unknown): string | null => (typeof v === 'string' && v.trim() ? v.trim() : null)

/** Ambiente (una sola consulta por carga de la página); null = producción o desconocido. */
export function loadEnvironment(): Promise<EnvironmentInfo | null> {
  cached ??= fetch('/health', { headers: { Accept: 'application/json' } })
    .then((r) => (r.ok ? r.json() : null))
    .then((j: { environment?: unknown; database?: unknown } | null) => {
      const label = text(j?.environment)
      return label ? { label, database: text(j?.database) } : null
    })
    .catch(() => null)
  return cached
}

export function EnvironmentRibbon() {
  const t = useT()
  const [env, setEnv] = useState<EnvironmentInfo | null>(null)
  useEffect(() => {
    let alive = true
    void loadEnvironment().then((e) => {
      if (!alive || !e) return
      setEnv(e)
      if (!document.title.startsWith(`[${e.label}]`)) document.title = `[${e.label}] ${document.title}`
    })
    return () => {
      alive = false
    }
  }, [])
  if (!env) return null
  return (
    <>
      <div className="env-strip" aria-hidden="true" />
      <div className="env-ribbon" role="status">
        <strong>{env.label}</strong>
        {env.database && (
          <>
            {' · '}
            <span className="env-db">{env.database}</span>
          </>
        )}
        {' · '}
        {t('environment.notProduction')}
      </div>
    </>
  )
}
