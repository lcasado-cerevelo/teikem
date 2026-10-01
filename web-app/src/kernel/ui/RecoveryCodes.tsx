import { useT } from '../i18n/useT'
import { toast } from './toast'

/** Texto del archivo descargable: encabezado con la cuenta y la fecha, un código por línea. */
export function recoveryCodesFile(codes: readonly string[], account: string | null | undefined, now = new Date()): string {
  return [`Teikem — códigos de recuperación${account ? ` (${account})` : ''}`, `Generados: ${now.toISOString().slice(0, 10)}`, '', ...codes, ''].join('\r\n')
}

/**
 * Códigos de recuperación del MFA (se muestran una sola vez): Copiar, Descargar (.txt) y la casilla obligatoria "Ya los guardé"
 * (2026-10-01: nadie pasa esta pantalla sin darse cuenta). El que la usa decide qué se habilita con `saved`.
 */
export function RecoveryCodes({
  codes,
  account,
  saved,
  onSavedChange,
}: {
  codes: readonly string[]
  account?: string | null
  saved: boolean
  onSavedChange: (saved: boolean) => void
}) {
  const t = useT()
  const text = codes.join('\n')
  const copy = async () => {
    try {
      if (!navigator.clipboard) throw new Error('clipboard')
      await navigator.clipboard.writeText(text)
      toast.success(t('account.mfa.copied'))
    } catch {
      toast.error(t('account.mfa.copyFailed'))
    }
  }
  const download = () => {
    const blob = new Blob([recoveryCodesFile(codes, account)], { type: 'text/plain;charset=utf-8' })
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = 'teikem-codigos-de-recuperacion.txt'
    document.body.appendChild(a)
    a.click()
    a.remove()
    URL.revokeObjectURL(url)
  }
  return (
    <>
      <div className="codes" data-testid="recovery-codes">
        {codes.map((c) => (
          <span key={c}>{c}</span>
        ))}
      </div>
      <div className="form-acts">
        <button type="button" className="btn" onClick={() => void copy()}>
          {t('common.copy')}
        </button>
        <button type="button" className="btn" onClick={download}>
          {t('account.mfa.download')}
        </button>
      </div>
      <label className="recovery-saved">
        <input type="checkbox" checked={saved} onChange={(e) => onSavedChange(e.target.checked)} />
        <span>{t('account.mfa.savedCheck')}</span>
      </label>
    </>
  )
}
