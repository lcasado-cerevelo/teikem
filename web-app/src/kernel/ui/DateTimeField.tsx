// Fecha y hora en un solo valor («YYYY-MM-DDTHH:mm», '' = vacío; la hora de la COMPAÑÍA, como el `<input type="datetime-local">` que reemplaza).
// La fecha es el `DateField` (calendario que abre solo, máscara de la compañía) y la hora va aparte, a su lado: al completar la fecha el foco pasa a
// la hora. Solo entran números; con hora de 12 (Región y formatos) hay un selector a. m./p. m. Una hora pegada se cambia al formato o se rechaza.
import { useEffect, useRef, useState, type ClipboardEvent } from 'react'
import { maskTime, parsePastedTime, parseTimeText, timeToText, type DayPeriod } from '../format/dateInput'
import { useFormatSettings } from '../format/useFormat'
import { useT } from '../i18n/useT'
import { DateField } from './DateField'
import './dateField.css'

export interface DateTimeFieldProps {
  id?: string
  /** «YYYY-MM-DDTHH:mm» ('' = vacío). */
  value: string
  onChange: (value: string) => void
  /** Límites por día (ISO «YYYY-MM-DD», o «YYYY-MM-DDTHH:mm»: se usa solo el día). */
  min?: string | null
  max?: string | null
  disabled?: boolean
  name?: string
  className?: string
  onBlur?: () => void
  'aria-label'?: string
  'aria-describedby'?: string
  'aria-invalid'?: boolean
  'aria-required'?: boolean
}

const day = (v?: string | null) => (v ? v.slice(0, 10) : null)

export function DateTimeField({ id, value, onChange, min, max, disabled, name, className, onBlur, ...aria }: DateTimeFieldProps) {
  const t = useT()
  const s = useFormatSettings()
  const [date, setDate] = useState(() => value.slice(0, 10))
  const initialTime = timeToText(value.slice(11, 16), s.timeFormat)
  const [timeText, setTimeText] = useState(initialTime.text)
  const [period, setPeriod] = useState<DayPeriod>(initialTime.period ?? 'AM')
  const [rejected, setRejected] = useState(false)
  const timeRef = useRef<HTMLInputElement>(null)

  const compose = (d: string, text: string, p: DayPeriod) => {
    const hhmm = parseTimeText(text, s.timeFormat === 12 ? p : null, s.timeFormat)
    return d && hhmm ? `${d}T${hhmm}` : ''
  }

  // un valor que cambia desde afuera se vuelve a pintar; el que sale de lo que se escribe no
  useEffect(() => {
    if (value === compose(date, timeText, period)) return
    if (value === '' && (!date || parseTimeText(timeText, s.timeFormat === 12 ? period : null, s.timeFormat) === null)) return
    setDate(value.slice(0, 10))
    const tt = timeToText(value.slice(11, 16), s.timeFormat)
    setTimeText(tt.text)
    if (tt.period) setPeriod(tt.period)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [value, s.timeFormat])

  const emit = (d: string, text: string, p: DayPeriod) => {
    const next = compose(d, text, p)
    if (next !== value) onChange(next)
  }

  const onDate = (iso: string) => {
    setDate(iso)
    emit(iso, timeText, period)
  }

  const onTime = (raw: string) => {
    setRejected(false)
    const masked = maskTime(raw, s.timeFormat)
    setTimeText(masked)
    emit(date, masked, period)
  }

  const onPeriod = (p: DayPeriod) => {
    setPeriod(p)
    emit(date, timeText, p)
  }

  const onPaste = (e: ClipboardEvent<HTMLInputElement>) => {
    e.preventDefault()
    const got = parsePastedTime(e.clipboardData.getData('text'), s.timeFormat)
    if (!got) return setRejected(true)
    const tt = timeToText(got.hhmm, s.timeFormat)
    setRejected(false)
    setTimeText(tt.text)
    if (tt.period) setPeriod(tt.period)
    emit(date, tt.text, tt.period ?? period)
  }

  const timeComplete = parseTimeText(timeText, s.timeFormat === 12 ? period : null, s.timeFormat) !== null
  const timeInvalid = rejected || (timeText.replace(/\D/g, '').length === 4 && !timeComplete)

  return (
    <span className={`datetime-row${className ? ` ${className}` : ''}`}>
      <DateField
        id={id}
        name={name}
        value={date}
        min={day(min)}
        max={day(max)}
        disabled={disabled}
        onChange={onDate}
        onComplete={() => timeRef.current?.focus({ preventScroll: true })}
        onBlur={onBlur}
        aria-label={aria['aria-label']}
        aria-describedby={aria['aria-describedby']}
        aria-required={aria['aria-required']}
        aria-invalid={aria['aria-invalid']}
      />
      <span className="datetime-time">
        <input
          ref={timeRef}
          type="text"
          inputMode="numeric"
          autoComplete="off"
          maxLength={5}
          placeholder={s.timeFormat === 12 ? 'hh:mm' : 'HH:mm'}
          disabled={disabled}
          value={timeText}
          aria-label={t('ui.date.time')}
          aria-invalid={timeInvalid || undefined}
          onChange={(e) => onTime(e.target.value)}
          onPaste={onPaste}
          onBlur={onBlur}
        />
        {s.timeFormat === 12 && (
          <select value={period} disabled={disabled} aria-label={t('ui.date.period')} onChange={(e) => onPeriod(e.target.value as DayPeriod)}>
            <option value="AM">AM</option>
            <option value="PM">PM</option>
          </select>
        )}
      </span>
    </span>
  )
}
