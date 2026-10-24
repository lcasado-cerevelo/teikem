// Lote 216 — «Atención: el envío ya va más adelante». El servidor no contó nada y pide decidir: «Cancelar» no hace nada; «Regresar el estatus» regresa el envío al estatus de la acción (escanear hacia atrás es la forma de regresarlo).
import { useT } from '../../kernel/i18n'
import { Modal } from '../../kernel/ui'

export function ScanBeyondConfirm({ message, busy, onConfirm, onCancel }: { message: string; busy: boolean; onConfirm: () => void; onCancel: () => void }) {
  const t = useT()
  return (
    <Modal
      open
      title={t('scan.beyond.title')}
      onClose={onCancel}
      dismissible={!busy}
      footer={
        <>
          <button type="button" className="btn" onClick={onCancel} disabled={busy}>{t('scan.beyond.cancel')}</button>
          <button type="button" className="btn flow" onClick={onConfirm} disabled={busy} data-testid="beyond-confirm">{busy ? t('common.loading') : t('scan.beyond.confirm')}</button>
        </>
      }
    >
      <div className="pb">
        <p data-testid="beyond-message"><b>{message}</b></p>
        <p>{t('scan.beyond.help')}</p>
      </div>
    </Modal>
  )
}
