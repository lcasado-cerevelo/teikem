// Lote 14 (P8) — buscador/escáner de líneas del conteo elegido (Cambios.pdf p. 14: "un drop down searchable autocomplete
// para buscar los productos según la tarea de conteo… que pueda recibir el escaneo"). Combobox como `ComboSelect` (↑/↓,
// Enter, Escape; clases `.msel.cpick`) sobre las líneas del conteo: filtra por SKU, producto, posición, lote o código de
// barras. Enter con un código EXACTO (SKU, código de barras, lote o serie: `matchCountLine`) elige la línea aunque la lista
// no esté abierta; si coinciden varias (mismo producto en otra posición o lote) la lista muestra solo esas para elegir; si
// ninguna, se ofrece agregar el producto al conteo (Lote 24: `onUnknown` lo busca por código y abre "Agregar lo encontrado" con
// el producto puesto y la posición opcional); si no es un producto, el aviso 'Ese código no está en este conteo. Use "Agregar lo
// encontrado" si el producto está en la posición.'.
// Al elegir, el texto se limpia y el foco se queda aquí (listo para el siguiente escaneo).
import { useCallback, useId, useMemo, useRef, useState, type KeyboardEvent } from 'react'
import { useT } from '../../kernel/i18n'
import { matchesQ } from '../../kernel/ui'
import { useDismiss } from '../../kernel/ui/useDismiss'
import type { CycleCountLineDto } from './api'
import { matchCountLine, type CountLineMatch } from './countView'

const MAX_SHOWN = 50

export interface CountScanBoxProps {
  lines: readonly CycleCountLineDto[]
  onPick: (match: CountLineMatch) => void
  /** Código que no es de ninguna línea: true = se atendió (se abrió "Agregar lo encontrado" con ese producto); false = no es un producto. */
  onUnknown?: (code: string) => Promise<boolean>
  disabled?: boolean
}

export function CountScanBox({ lines, onPick, onUnknown, disabled }: CountScanBoxProps) {
  const t = useT()
  const inputId = useId()
  const listId = `${inputId}-list`
  const helpId = `${inputId}-help`
  const boxRef = useRef<HTMLDivElement>(null)
  const [text, setText] = useState('')
  const [open, setOpen] = useState(false)
  const [active, setActive] = useState(0)
  /** Varias líneas con el mismo código exacto: se elige entre ellas. */
  const [choices, setChoices] = useState<CountLineMatch[] | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const dismiss = useCallback(() => {
    setOpen(false)
    setChoices(null)
  }, [])
  useDismiss(boxRef, open, dismiss)

  const shown: CountLineMatch[] = useMemo(() => {
    if (choices) return choices
    return lines
      .filter((l) => matchesQ(text, l.sku, l.productName, l.binCode, l.lotNumber, l.barcode))
      .slice(0, MAX_SHOWN)
      .map((line) => ({ line, by: 'sku' as const }))
  }, [lines, text, choices])

  const pick = (m: CountLineMatch) => {
    setOpen(false)
    setChoices(null)
    setText('')
    setMessage(null)
    onPick(m)
  }

  const onKeyDown = (e: KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'ArrowDown') {
      e.preventDefault()
      if (!open) setOpen(true)
      else setActive((i) => Math.min(i + 1, shown.length - 1))
    } else if (e.key === 'ArrowUp') {
      e.preventDefault()
      setActive((i) => Math.max(i - 1, 0))
    } else if (e.key === 'Enter') {
      e.preventDefault()
      if (choices) {
        if (shown[active]) pick(shown[active])
        return
      }
      const exact = matchCountLine(lines, text)
      if (exact.length === 1) return pick(exact[0])
      if (exact.length > 1) {
        setChoices(exact)
        setActive(0)
        setOpen(true)
        setMessage(t('warehouse.cycleCounts.scan.many', { n: exact.length }))
        return
      }
      if (text.trim() && open && shown[active]) return pick(shown[active])
      if (text.trim()) {
        setOpen(false)
        const code = text.trim()
        const notFound = () => setMessage(t('warehouse.cycleCounts.scan.notFound'))
        if (!onUnknown) return notFound()
        void onUnknown(code).then((handled) => {
          if (handled) {
            setText('')
            setMessage(null)
          } else notFound()
        })
      }
    } else if (e.key === 'Escape' && open) {
      e.stopPropagation()
      setOpen(false)
      setChoices(null)
    } else if (e.key === 'Tab' && open) {
      setOpen(false)
      setChoices(null)
    }
  }

  return (
    <div className="f cc-scan">
      <label htmlFor={inputId}>{t('warehouse.cycleCounts.scan.label')}</label>
      <div ref={boxRef} className={open ? 'msel cpick open' : 'msel cpick'}>
        <div className="cpin">
          <input
            id={inputId}
            type="text"
            role="combobox"
            autoComplete="off"
            aria-autocomplete="list"
            aria-expanded={open}
            aria-controls={listId}
            aria-describedby={helpId}
            aria-activedescendant={open && shown[active] ? `${listId}-${active}` : undefined}
            disabled={disabled}
            placeholder={t('warehouse.cycleCounts.scan.placeholder')}
            value={text}
            onChange={(e) => {
              setText(e.target.value)
              setActive(0)
              setChoices(null)
              setMessage(null)
              setOpen(true)
            }}
            onClick={() => setOpen(true)}
            onKeyDown={onKeyDown}
          />
        </div>
        {open && (
          <div className="mp">
            <div className="milist" id={listId} role="listbox" aria-label={t('warehouse.cycleCounts.scan.label')}>
              {shown.length === 0 && <div className="mnone">{t('ui.searchSelect.none')}</div>}
              {shown.map((m, i) => (
                <div
                  key={m.line.id}
                  id={`${listId}-${i}`}
                  role="option"
                  aria-selected={i === active}
                  className={i === active ? 'mi on' : 'mi'}
                  onMouseDown={(e) => e.preventDefault()}
                  onMouseEnter={() => setActive(i)}
                  onClick={() => pick(m)}
                >
                  <span>{[m.line.sku, m.line.productName].filter(Boolean).join(' · ')}</span>
                  <span className="sub">{[m.line.binCode, m.line.lotNumber, m.line.barcode].filter(Boolean).join(' · ')}</span>
                </div>
              ))}
            </div>
          </div>
        )}
      </div>
      <p className={message ? 'help cc-scan-msg' : 'help'} id={helpId} role={message ? 'alert' : undefined}>
        {message ?? t('warehouse.cycleCounts.scan.help')}
      </p>
    </div>
  )
}
