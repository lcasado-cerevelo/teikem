import { useCallback, useEffect, useId, useRef, useState, type KeyboardEvent } from 'react'
import { useT } from '../i18n/useT'
import { EXPORT_FORMATS, type ExportFormat } from './exportTable'
import { IconDownload } from './icons'
import { toast } from './toast'
import { useDismiss } from './useDismiss'
import './ui.css'

export interface ExportMenuProps {
  /** Genera y descarga el archivo; si lanza, se avisa con un toast de error. */
  onExport: (format: ExportFormat) => void | Promise<void>
  /** Filas que se van a exportar (nota informativa dentro del menú). */
  count: number
}

/** Botón "Exportar" del pie de `DataTable` con su menú Excel / CSV / PDF (abre hacia arriba). */
export function ExportMenu({ onExport, count }: ExportMenuProps) {
  const t = useT()
  const [open, setOpen] = useState(false)
  const [busy, setBusy] = useState(false)
  const ref = useRef<HTMLDivElement>(null)
  const triggerRef = useRef<HTMLButtonElement>(null)
  const menuRef = useRef<HTMLDivElement>(null)
  const menuId = useId()

  const close = useCallback(() => setOpen(false), [])
  useDismiss(ref, open, close)

  // al abrir, el foco pasa a la primera opción (teclado)
  useEffect(() => {
    if (open) menuRef.current?.querySelector<HTMLButtonElement>('[role="menuitem"]')?.focus()
  }, [open])

  const choose = async (format: ExportFormat) => {
    setOpen(false)
    triggerRef.current?.focus()
    setBusy(true)
    try {
      await onExport(format)
    } catch {
      toast.error(t('ui.table.export.error'))
    } finally {
      setBusy(false)
    }
  }

  const onMenuKey = (e: KeyboardEvent<HTMLDivElement>) => {
    const items = Array.from(menuRef.current?.querySelectorAll<HTMLButtonElement>('[role="menuitem"]') ?? [])
    const i = items.indexOf(document.activeElement as HTMLButtonElement)
    if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
      e.preventDefault()
      const next = e.key === 'ArrowDown' ? (i + 1) % items.length : (i - 1 + items.length) % items.length
      items[next]?.focus()
    } else if (e.key === 'Escape') {
      // solo cierra el menú (no el Modal que pudiera contener la tabla)
      e.stopPropagation()
      setOpen(false)
      triggerRef.current?.focus()
    } else if (e.key === 'Tab') {
      setOpen(false)
    }
  }

  return (
    <div className={open ? 'dt-export open' : 'dt-export'} ref={ref}>
      <button
        ref={triggerRef}
        type="button"
        className="btn sm"
        aria-haspopup="menu"
        aria-expanded={open}
        aria-controls={open ? menuId : undefined}
        disabled={busy}
        onClick={() => setOpen((o) => !o)}
      >
        <IconDownload />
        <span>{busy ? t('ui.table.export.working') : t('ui.table.export.button')}</span>
      </button>
      {open && (
        <div id={menuId} ref={menuRef} className="dt-export-menu" role="menu" aria-label={t('ui.table.export.menu')} onKeyDown={onMenuKey}>
          {EXPORT_FORMATS.map((f) => (
            <button key={f} type="button" role="menuitem" onClick={() => void choose(f)}>
              {t(`ui.table.export.${f}`)}
            </button>
          ))}
          <div className="dt-export-note" role="none">
            {t('ui.table.export.rows', { count })}
          </div>
        </div>
      )}
    </div>
  )
}
