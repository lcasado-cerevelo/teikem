// Cinta de ambiente (2026-10-12): en un ambiente que no es producción (staging) la web muestra SIEMPRE, en todas las pantallas, una franja
// roja arriba y una etiqueta abajo («STAGING · Ambiente de pruebas: no es producción»), y antepone la etiqueta al título de la pestaña.
// La etiqueta la informa el API en `GET /health` (`environment`, de Hosting:EnvironmentLabel; el instalador la pone en los sitios que no son
// «Teikem»). En producción es null y no se pinta nada; si /health no responde con JSON (p. ej. el servidor de desarrollo) tampoco.
import { useEffect, useState } from 'react'
import { useT } from '../i18n'

let cached: Promise<string | null> | undefined

/** Etiqueta del ambiente (una sola consulta por carga de la página); null = producción o desconocido. */
export function loadEnvironmentLabel(): Promise<string | null> {
  cached ??= fetch('/health', { headers: { Accept: 'application/json' } })
    .then((r) => (r.ok ? r.json() : null))
    .then((j: { environment?: unknown } | null) => (typeof j?.environment === 'string' && j.environment.trim() ? j.environment.trim() : null))
    .catch(() => null)
  return cached
}

export function EnvironmentRibbon() {
  const t = useT()
  const [label, setLabel] = useState<string | null>(null)
  useEffect(() => {
    let alive = true
    void loadEnvironmentLabel().then((l) => {
      if (!alive || !l) return
      setLabel(l)
      if (!document.title.startsWith(`[${l}]`)) document.title = `[${l}] ${document.title}`
    })
    return () => {
      alive = false
    }
  }, [])
  if (!label) return null
  return (
    <>
      <div className="env-strip" aria-hidden="true" />
      <div className="env-ribbon" role="status">
        <strong>{label}</strong> · {t('environment.notProduction')}
      </div>
    </>
  )
}
