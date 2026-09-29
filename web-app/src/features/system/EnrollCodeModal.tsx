// Modal "Código de registro" (Aparatos móviles, F8a P5): el código se muestra UNA sola vez (alta o regeneración).
import { useT } from '../../kernel/i18n/useT'
import { Modal, toast } from '../../kernel/ui'

export interface EnrollCodeModalProps {
  /** null = cerrado. */
  enrollCode: string | null
  onClose: () => void
}

/** Copia `code` al portapapeles (o avisa que falló); comparte el patrón con la contraseña temporal de alta de usuario. */
export async function copyCode(code: string, okMessage: string, failMessage: string) {
  try {
    if (!navigator.clipboard) throw new Error('clipboard')
    await navigator.clipboard.writeText(code)
    toast.success(okMessage)
  } catch {
    toast.error(failMessage)
  }
}

/** Muestra `enrollCode` en grande y monoespaciado con botón Copiar; se usa tras crear un aparato o regenerar su código. */
export function EnrollCodeModal({ enrollCode, onClose }: EnrollCodeModalProps) {
  const t = useT()
  return (
    <Modal
      open={enrollCode !== null}
      title={t('system.devices.enrollCodeTitle')}
      onClose={onClose}
      size="sm"
      dismissible={false}
      footer={
        <button type="button" className="btn flow" onClick={onClose}>
          {t('common.done')}
        </button>
      }
    >
      <p style={{ margin: '0 0 4px', color: 'var(--muted)', fontSize: 13 }}>{t('system.devices.enrollCodeHint')}</p>
      {enrollCode && (
        <div className="secret" style={{ fontSize: 22, fontWeight: 700, letterSpacing: '.08em', textAlign: 'center' }}>
          {enrollCode}
        </div>
      )}
      <button
        type="button"
        className="btn sm"
        onClick={() => enrollCode && void copyCode(enrollCode, t('system.devices.copied'), t('system.devices.copyFailed'))}
      >
        {t('common.copy')}
      </button>
    </Modal>
  )
}
