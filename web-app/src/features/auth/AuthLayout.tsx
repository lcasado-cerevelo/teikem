import type { ReactNode } from 'react'
import { LangSelect } from '../../app/LangSelect'
import { useSession } from '../../app/session'
import { BrandLockup } from '../../kernel/ui/Brand'
import { usePublicBrand } from '../../kernel/ui/publicBrand'

/** Tarjeta centrada de las pantallas sin sesión (login, MFA, selección de compañía), con el lockup centrado arriba (por idioma y tema):
 *  el de la última compañía usada o el de la compañía de la dirección (dominio o subdominio) y, si no hay, el de Teikem. Funciona a 360 px. */
export function AuthLayout({ title, subtitle, children }: { title: string; subtitle?: string; children: ReactNode }) {
  const { lang, setLang } = useSession()
  usePublicBrand()
  return (
    <div className="auth">
      <div className="auth-lang">
        <LangSelect lang={lang} onChange={setLang} />
      </div>
      <main className="auth-card">
        <div className="auth-brand">
          <BrandLockup />
        </div>
        <h1>{title}</h1>
        {subtitle && <p className="subtle">{subtitle}</p>}
        {children}
      </main>
    </div>
  )
}
