// Reto anti-robot (Cloudflare Turnstile) en la web: carga el script de Cloudflare una vez y dibuja el widget; avisa con el token cuando se resuelve (y con null si vence).
import { useEffect, useRef } from 'react'
import { View } from 'react-native'

const SCRIPT_SRC = 'https://challenges.cloudflare.com/turnstile/v0/api.js?render=explicit'

interface TurnstileApi {
  render: (el: HTMLElement, options: { sitekey: string; callback: (token: string) => void; 'expired-callback': () => void; 'error-callback': () => void }) => string
  remove: (id: string) => void
}

function loadScript(): Promise<TurnstileApi> {
  const w = window as unknown as { turnstile?: TurnstileApi }
  if (w.turnstile) return Promise.resolve(w.turnstile)
  return new Promise((resolve, reject) => {
    const existing = document.querySelector<HTMLScriptElement>(`script[src="${SCRIPT_SRC}"]`)
    const script = existing ?? document.createElement('script')
    script.addEventListener('load', () => (w.turnstile ? resolve(w.turnstile) : reject(new Error('turnstile'))))
    script.addEventListener('error', () => reject(new Error('turnstile')))
    if (!existing) {
      script.src = SCRIPT_SRC
      script.async = true
      document.head.appendChild(script)
    }
  })
}

export function Captcha({ siteKey, onToken }: { siteKey: string; onToken: (token: string | null) => void }) {
  const host = useRef<View>(null)

  useEffect(() => {
    let widgetId: string | null = null
    let api: TurnstileApi | null = null
    let cancelled = false
    loadScript()
      .then((turnstile) => {
        const el = host.current as unknown as HTMLElement | null
        if (cancelled || !el) return
        api = turnstile
        widgetId = turnstile.render(el, { sitekey: siteKey, callback: (token) => onToken(token), 'expired-callback': () => onToken(null), 'error-callback': () => onToken(null) })
      })
      .catch(() => onToken(null))
    return () => {
      cancelled = true
      if (api && widgetId) api.remove(widgetId)
    }
  }, [siteKey, onToken])

  return <View ref={host} />
}
