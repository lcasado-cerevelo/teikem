// Campo de fecha ÚNICO de la web (dictado de Luis, 2026-10-21). Al entrar (mouse, dedo o teclado) se abre el calendario solo, pero se puede
// escribir: solo entran números y el campo los va acomodando con el formato que la compañía eligió (Región y formatos), igual que el teléfono.
// Una fecha pegada se cambia a ese formato si se entiende y se rechaza si no. El valor que sale es ISO («YYYY-MM-DD», '' = vacía), igual que el
// `<input type="date">` que reemplaza, así que ninguna pantalla cambia su manera de guardar o enviar.
import { useCallback, useEffect, useId, useLayoutEffect, useMemo, useRef, useState, type ClipboardEvent, type KeyboardEvent } from 'react'
import { createPortal } from 'react-dom'
import { datePlaceholder, dateToText, DATE_DIGITS, inRange, isIsoDate, isoFromDigits, maskDate, monthGrid, onlyDigits, parsePastedDate, shiftDay, shiftMonth } from '../format/dateInput'
import { useFormat, useFormatSettings } from '../format/useFormat'
import { useLang, useT } from '../i18n/useT'
import './dateField.css'

export interface DateFieldProps {
  id?: string
  /** ISO «YYYY-MM-DD» ('' = vacía). */
  value: string
  onChange: (iso: string) => void
  min?: string | null
  max?: string | null
  disabled?: boolean
  /** Solo lectura: se ve la fecha, no se escribe ni se abre el calendario. */
  readOnly?: boolean
  name?: string
  className?: string
  placeholder?: string
  /** Texto al pasar el mouse. */
  title?: string
  /** Sin calendario automático (queda el botón del calendario); por omisión se abre al entrar. */
  autoOpen?: boolean
  onBlur?: () => void
  /** Se llamó al completarse una fecha (tecleada, pegada o elegida en el calendario). */
  onComplete?: () => void
  'aria-label'?: string
  'aria-describedby'?: string
  'aria-invalid'?: boolean
  'aria-required'?: boolean
}

type Pos = { top: number; left: number; width: number; flip: boolean }

/**
 * Todo lo que se sabe de un texto escrito: el ISO que da (si es una fecha que existe) y si no existe (`invalid`). Una fecha fuera de min/max SÍ se entrega
 * (como el `<input type="date">` nativo): cada formulario muestra su propio mensaje («no puede ser futura»); el campo solo la marca en rojo (`outOfRange`).
 */
function readText(text: string, min?: string | null, max?: string | null, s?: ReturnType<typeof useFormatSettings>): { iso: string | null; invalid: boolean; outOfRange: boolean } {
  const digits = onlyDigits(text)
  if (digits.length < DATE_DIGITS) return { iso: null, invalid: false, outOfRange: false }
  const iso = isoFromDigits(digits, s)
  if (!iso) return { iso: null, invalid: true, outOfRange: false }
  return { iso, invalid: false, outOfRange: !inRange(iso, min, max) }
}

export function DateField({ id, value, onChange, min, max, disabled, readOnly, name, className, placeholder, title, autoOpen = true, onBlur, onComplete, ...aria }: DateFieldProps) {
  const t = useT()
  const lang = useLang()
  const f = useFormat()
  const s = useFormatSettings()
  const [text, setText] = useState(() => dateToText(value, s))
  const [open, setOpen] = useState(false)
  const [rejected, setRejected] = useState(false)
  const [view, setView] = useState(() => viewOf(value || f.today()))
  const [focusIso, setFocusIso] = useState<string | null>(null)
  const [pos, setPos] = useState<Pos | null>(null)
  const inputRef = useRef<HTMLInputElement>(null)
  const wrapRef = useRef<HTMLSpanElement>(null)
  const popRef = useRef<HTMLDivElement>(null)
  const popId = useId()
  const skipOpen = useRef(false)   // al devolver el foco tras elegir un día, el campo no vuelve a abrir el calendario

  // un valor que cambia desde afuera (cargar un registro, «Hoy», limpiar el formulario) se vuelve a pintar; el que viene de lo que se escribe no
  useEffect(() => {
    const typed = readText(text, min, max, s).iso
    if (value === (typed ?? '')) return
    if (value === '' && typed === null) return   // texto a medias o inválido mientras la pantalla ya lo tiene vacío
    setText(dateToText(value, s))
    if (value) setView(viewOf(value))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [value, s.dateOrder, s.dateSeparator])

  const info = readText(text, min, max, s)
  const invalid = rejected || info.invalid || info.outOfRange || aria['aria-invalid'] === true
  const placeholderText = placeholder ?? datePlaceholder(s)
  const locked = !!disabled || !!readOnly

  // ---- posición del calendario (fijo, para que ningún panel con scroll lo recorte)
  const place = useCallback(() => {
    const el = inputRef.current
    if (!el) return
    const r = el.getBoundingClientRect()
    const room = window.innerHeight - r.bottom
    setPos({ top: r.bottom + 6, left: Math.max(8, Math.min(r.left, window.innerWidth - 292)), width: r.width, flip: room < 330 && r.top > room })
  }, [])
  useLayoutEffect(() => {
    if (!open) return
    place()
    const on = () => place()
    window.addEventListener('resize', on)
    window.addEventListener('scroll', on, true)
    return () => {
      window.removeEventListener('resize', on)
      window.removeEventListener('scroll', on, true)
    }
  }, [open, place])

  // ---- cerrar al hacer clic o tocar afuera (el calendario va en un portal, así que se revisan los dos)
  useEffect(() => {
    if (!open) return
    const outside = (e: MouseEvent | TouchEvent) => {
      const n = e.target as Node
      if (!wrapRef.current?.contains(n) && !popRef.current?.contains(n)) setOpen(false)
    }
    document.addEventListener('mousedown', outside)
    document.addEventListener('touchstart', outside)
    return () => {
      document.removeEventListener('mousedown', outside)
      document.removeEventListener('touchstart', outside)
    }
  }, [open])

  // el día con el foco del calendario (teclado)
  useEffect(() => {
    if (open && focusIso) popRef.current?.querySelector<HTMLButtonElement>(`button[data-iso="${focusIso}"]`)?.focus()
  }, [open, focusIso, view])

  const commit = (iso: string) => {
    setText(dateToText(iso, s))
    setRejected(false)
    onChange(iso)
    if (iso) {
      setView(viewOf(iso))
      onComplete?.()
    }
  }

  const choose = (iso: string) => {
    commit(iso)
    setOpen(false)
    setFocusIso(null)
    skipOpen.current = true
    inputRef.current?.focus({ preventScroll: true })
    skipOpen.current = false
  }

  const onType = (raw: string) => {
    setRejected(false)
    // un ISO completo (autocompletado, pruebas): se acepta tal cual
    if (isIsoDate(raw)) return commit(raw)
    const masked = maskDate(raw, s)
    setText(masked)
    const read = readText(masked, min, max, s)
    if (read.iso) {
      onChange(read.iso)
      setView(viewOf(read.iso))
      onComplete?.()
    } else if (value !== '') onChange('')
  }

  const onPaste = (e: ClipboardEvent<HTMLInputElement>) => {
    e.preventDefault()
    const iso = parsePastedDate(e.clipboardData.getData('text'), s)
    if (iso) {
      commit(iso)
      setOpen(false)
    } else setRejected(true)
  }

  const onKeyDown = (e: KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'Escape' && open) {
      e.stopPropagation()
      setOpen(false)
    } else if (e.key === 'ArrowDown') {
      e.preventDefault()
      setOpen(true)
      setFocusIso(value || f.today())
    } else if (e.key === 'Tab') setOpen(false)
  }

  const onGridKey = (e: KeyboardEvent<HTMLButtonElement>, iso: string) => {
    const move = (to: string) => {
      e.preventDefault()
      setView(viewOf(to))
      setFocusIso(to)
    }
    if (e.key === 'ArrowLeft') move(shiftDay(iso, -1))
    else if (e.key === 'ArrowRight') move(shiftDay(iso, 1))
    else if (e.key === 'ArrowUp') move(shiftDay(iso, -7))
    else if (e.key === 'ArrowDown') move(shiftDay(iso, 7))
    else if (e.key === 'PageUp') move(shiftMonthIso(iso, -1))
    else if (e.key === 'PageDown') move(shiftMonthIso(iso, 1))
    else if (e.key === 'Escape') {
      e.preventDefault()
      setOpen(false)
      inputRef.current?.focus()
    }
  }

  const grid = useMemo(() => monthGrid(view.year, view.month, s.weekStartDay), [view, s.weekStartDay])
  const monthLabel = useMemo(() => new Intl.DateTimeFormat(lang, { month: 'long', year: 'numeric', timeZone: 'UTC' }).format(new Date(Date.UTC(view.year, view.month, 1))), [lang, view])
  const weekdays = useMemo(() => {
    const fmt = new Intl.DateTimeFormat(lang, { weekday: 'short', timeZone: 'UTC' })
    // 2026-10-04 es domingo
    return Array.from({ length: 7 }, (_, i) => fmt.format(new Date(Date.UTC(2026, 9, 4 + ((i + s.weekStartDay) % 7)))))
  }, [lang, s.weekStartDay])
  const today = f.today()

  const prev = shiftMonth(view.year, view.month, -1)
  const next = shiftMonth(view.year, view.month, 1)

  return (
    <span className={`datefield${className ? ` ${className}` : ''}`} ref={wrapRef}>
      <input
        ref={inputRef}
        id={id}
        name={name}
        type="text"
        inputMode="numeric"
        autoComplete="off"
        spellCheck={false}
        maxLength={12}
        placeholder={placeholderText}
        disabled={disabled}
        readOnly={readOnly}
        value={text}
        aria-label={aria['aria-label']}
        aria-describedby={aria['aria-describedby']}
        aria-required={aria['aria-required']}
        aria-invalid={invalid || undefined}
        aria-haspopup="true"
        aria-expanded={open}
        aria-controls={open ? popId : undefined}
        onFocus={() => autoOpen && !locked && !skipOpen.current && setOpen(true)}
        onClick={() => !locked && setOpen(true)}
        onBlur={() => onBlur?.()}
        onChange={(e) => onType(e.target.value)}
        onPaste={onPaste}
        onKeyDown={onKeyDown}
        title={rejected ? t('ui.date.pasteRejected', { format: placeholderText }) : title}
      />
      <button
        type="button"
        className="datefield-btn"
        tabIndex={-1}
        disabled={locked}
        aria-label={t('ui.date.openCalendar')}
        onMouseDown={(e) => e.preventDefault()}
        onClick={() => {
          setOpen((o) => !o)
          inputRef.current?.focus({ preventScroll: true })
        }}
      >
        <svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
          <rect x="3.5" y="5" width="17" height="15.5" rx="2.5" />
          <path d="M3.5 10h17M8 3v4M16 3v4" />
        </svg>
      </button>
      {rejected && (
        <span className="datefield-err" role="alert">
          {t('ui.date.pasteRejected', { format: placeholderText })}
        </span>
      )}
      {open &&
        !locked &&
        pos &&
        createPortal(
          <div
            ref={popRef}
            id={popId}
            role="group"
            aria-label={t('ui.date.calendar')}
            className="datepop"
            style={{ left: pos.left, ...(pos.flip ? { bottom: window.innerHeight - (pos.top - 12) } : { top: pos.top }) }}
            // el foco se queda en el campo (se puede seguir escribiendo) mientras se toca el calendario
            onMouseDown={(e) => {
              if (!(e.target instanceof HTMLButtonElement)) e.preventDefault()
            }}
          >
            <div className="datepop-head">
              <button type="button" className="datepop-nav" aria-label={t('ui.date.prevMonth')} onClick={() => setView(prev)}>
                ‹
              </button>
              <span className="datepop-title" aria-live="polite">
                {monthLabel}
              </span>
              <button type="button" className="datepop-nav" aria-label={t('ui.date.nextMonth')} onClick={() => setView(next)}>
                ›
              </button>
            </div>
            <div className="datepop-week" aria-hidden="true">
              {weekdays.map((w, i) => (
                <span key={i}>{w}</span>
              ))}
            </div>
            <div className="datepop-grid" role="grid">
              {grid.flat().map((iso, i) =>
                iso === null ? (
                  <span key={i} className="datepop-pad" />
                ) : (
                  <button
                    key={iso}
                    type="button"
                    data-iso={iso}
                    tabIndex={iso === (focusIso ?? value) || (!focusIso && !value && iso === today) ? 0 : -1}
                    className={`datepop-day${iso === value ? ' on' : ''}${iso === today ? ' today' : ''}`}
                    disabled={!inRange(iso, min, max)}
                    aria-label={iso}
                    aria-pressed={iso === value}
                    onClick={() => choose(iso)}
                    onKeyDown={(e) => onGridKey(e, iso)}
                  >
                    {Number(iso.slice(8))}
                  </button>
                ),
              )}
            </div>
            <div className="datepop-foot">
              <button type="button" className="btn sm" disabled={!inRange(today, min, max)} onClick={() => choose(today)}>
                {t('ui.date.today')}
              </button>
              <button
                type="button"
                className="btn sm"
                onClick={() => {
                  commit('')
                  setOpen(false)
                }}
              >
                {t('ui.date.clear')}
              </button>
            </div>
          </div>,
          document.body,
        )}
    </span>
  )
}

/** Mes (0–11) y año de un ISO para abrir el calendario ahí. */
function viewOf(iso: string): { year: number; month: number } {
  const m = /^(\d{4})-(\d{2})/.exec(iso)
  return m ? { year: Number(m[1]), month: Number(m[2]) - 1 } : { year: new Date().getUTCFullYear(), month: new Date().getUTCMonth() }
}

/** Un día movido `delta` meses (para AvPág/RePág): mismo día del mes, o el último si no existe. */
function shiftMonthIso(iso: string, delta: number): string {
  const [y, m, d] = iso.split('-').map(Number)
  const to = shiftMonth(y, m - 1, delta)
  const last = new Date(Date.UTC(to.year, to.month + 1, 0)).getUTCDate()
  return `${String(to.year).padStart(4, '0')}-${String(to.month + 1).padStart(2, '0')}-${String(Math.min(d, last)).padStart(2, '0')}`
}
