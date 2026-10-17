// Insignia de conversaciones sin leer junto al ítem «Mensajes de clientes» del menú (sondeo cada ~30 s). Solo se monta cuando el ítem
// ya es visible (permiso clientmessages.reply y módulo CLIENT_PORTAL), así que la consulta no se hace sin acceso.
import { useT } from '../../kernel/i18n'
import { useStaffUnread } from './api'
import { badgeText } from './rules'
import './clientMessages.css'

export function NavUnreadBadge() {
  const t = useT()
  const { data } = useStaffUnread()
  const n = data?.conversations ?? 0
  if (n <= 0) return null
  return (
    <span className="cm-nav-badge" aria-label={t('clientMessages.unreadBadge', { n })}>
      {badgeText(n)}
    </span>
  )
}
