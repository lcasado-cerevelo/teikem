import type { ReactNode } from 'react'
import { LangSelect } from '../../app/LangSelect'
import { useSession } from '../../app/session'

/** Tarjeta centrada de las pantallas sin sesión (login, MFA, selección de compañía). Funciona a 360 px. */
export function AuthLayout({ title, subtitle, children }: { title: string; subtitle?: string; children: ReactNode }) {
  const { lang, setLang } = useSession()
  return (
    <div className="auth">
      <div className="auth-lang">
        <LangSelect lang={lang} onChange={setLang} />
      </div>
      <main className="auth-card">
        <div className="rbrand">
          <span className="logo" aria-hidden="true">
            T
          </span>
          <span className="name">Teikem</span>
        </div>
        <h1>{title}</h1>
        {subtitle && <p className="subtle">{subtitle}</p>}
        {children}
      </main>
    </div>
  )
}
