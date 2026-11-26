// Lote 248 — aviso discreto al escoger el cliente y en el resumen: «El certificado de exención de IVU de este cliente vence el … (en N días)» o «venció el …: se cobrará el IVU».
// Solo informa (nunca bloquea el pedido). Sin aviso (nivel NONE, módulo de impuestos apagado o un error) no pinta nada.
import { useFormat } from '../../../kernel/format'
import { useT } from '../../../kernel/i18n'
import { useTaxNotice } from './taxNoticeApi'
import './taxNotice.css'

export function TaxNoticeBanner({ clientPublicId }: { clientPublicId: string | null | undefined }) {
  const t = useT()
  const f = useFormat()
  const notice = useTaxNotice(clientPublicId).data
  if (!clientPublicId || !notice || (notice.level !== 'EXPIRING' && notice.level !== 'EXPIRED') || !notice.validTo) return null
  const date = f.date(notice.validTo)
  const text = notice.level === 'EXPIRED' ? t('salesWizard.taxNotice.expired', { date }) : t('salesWizard.taxNotice.expiring', { date, n: notice.days ?? 0 })
  return (
    <p className={`txn-banner ${notice.level === 'EXPIRED' ? 'txn-expired' : 'txn-expiring'}`} role="status" data-testid="tax-notice">
      <span className="txn-label">{t('salesWizard.taxNotice.label')}</span> {text}
      {notice.certificateNumber && <span className="txn-cert"> {t('salesWizard.taxNotice.certificate', { number: notice.certificateNumber })}</span>}
    </p>
  )
}
