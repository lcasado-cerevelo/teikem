// Reto anti-robot (Cloudflare Turnstile) en el teléfono: todavía no está integrado (haría falta una vista web dentro de la app). Mientras, se pide entrar desde el navegador. En la web existe Captcha.web.tsx.
import { Notice } from '../../kernel/ui/ui'
import { useT } from '../../kernel/i18n/useT'

export function Captcha(_props: { siteKey: string; onToken: (token: string | null) => void }) {
  const { t } = useT()
  return <Notice tone="warn">{t('captcha.nativeUnavailable')}</Notice>
}
