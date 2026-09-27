import { useT } from '../kernel/i18n/useT'

/** Pantalla provisional de una ruta cuya pieza aún no se integra (P3 Pulso, P4 Mi cuenta la sustituyen en routes.tsx). */
export default function Placeholder() {
  const t = useT()
  return (
    <div className="empty">
      <div>
        <h2>{t('shell.placeholder.title')}</h2>
        <p>{t('shell.placeholder.body')}</p>
      </div>
    </div>
  )
}
