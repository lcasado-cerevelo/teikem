import { useSyncExternalStore } from 'react'
import { useT } from '../i18n/useT'
import { IconAlert, IconCheck, IconClose } from './icons'
import { dismissToast, getToast, subscribeToast } from './toastStore'
import './ui.css'

/** Contenedor de avisos. `toast.*` lo monta solo la primera vez; no hace falta ponerlo en el árbol. */
export function Toaster() {
  const t = useT()
  const item = useSyncExternalStore(subscribeToast, getToast, getToast)
  const kindCls = item?.kind === 'error' ? ' err' : item?.kind === 'success' ? ' ok' : ''
  return (
    <div
      className={`toast${item ? ' on' : ''}${kindCls}`}
      role={item?.kind === 'error' ? 'alert' : 'status'}
      aria-live={item?.kind === 'error' ? 'assertive' : 'polite'}
      style={item ? { pointerEvents: 'auto' } : undefined}
      data-testid="toast"
    >
      {item && (
        <>
          {item.kind === 'error' ? <IconAlert /> : <IconCheck />}
          <span className="tx">{item.message}</span>
          <button type="button" className="iconbtn" aria-label={t('ui.modal.close')} onClick={dismissToast}>
            <IconClose />
          </button>
        </>
      )}
    </div>
  )
}
