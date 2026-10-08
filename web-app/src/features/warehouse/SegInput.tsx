// Grupo segmentado de opciones (`.seg` con radios) dentro de un <Field name="...">: el valor del formulario es el código elegido. Igual que
// `AdjustDirectionInput`, pero con las opciones que se le den (daños: Recibo / Almacén y Cuarentena / Desechar).
import type { KeyboardEvent } from 'react'
import { useController, useFormContext } from 'react-hook-form'
import { useFieldInfo } from '../../kernel/ui/formContext'
import './warehouse.css'

export interface SegOption {
  value: string
  label: string
}

export interface SegInputProps {
  /** Nombre accesible del grupo (el mismo texto que la etiqueta del Field). */
  label: string
  options: readonly SegOption[]
  disabled?: boolean
  onPicked?: (value: string) => void
}

export function SegInput({ label, options, disabled, onPicked }: SegInputProps) {
  const info = useFieldInfo('SegInput')
  const { control } = useFormContext()
  const { field } = useController({ name: info.name, control })
  const value = (field.value as string) ?? ''

  const pick = (v: string) => {
    field.onChange(v)
    onPicked?.(v)
  }
  // ←/→ cambian de opción, como un grupo de radios nativo
  const onKeyDown = (e: KeyboardEvent<HTMLButtonElement>) => {
    if (!['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown'].includes(e.key)) return
    e.preventDefault()
    const i = Math.max(0, options.findIndex((o) => o.value === value))
    const step = e.key === 'ArrowLeft' || e.key === 'ArrowUp' ? -1 : 1
    const next = options[(i + step + options.length) % options.length]
    pick(next.value)
    e.currentTarget.parentElement?.querySelector<HTMLButtonElement>(`button[data-value="${next.value}"]`)?.focus()
  }

  return (
    <div
      id={info.id}
      role="radiogroup"
      aria-label={label}
      aria-invalid={info.invalid || undefined}
      aria-required={info.required || undefined}
      aria-describedby={info.describedBy}
      className="seg"
    >
      {options.map((o, i) => {
        const on = value === o.value
        return (
          <button
            key={o.value}
            type="button"
            role="radio"
            data-value={o.value}
            aria-checked={on}
            tabIndex={on || (!value && i === 0) ? 0 : -1}
            className={on ? 'on' : undefined}
            disabled={disabled}
            onClick={() => pick(o.value)}
            onKeyDown={onKeyDown}
            onBlur={field.onBlur}
          >
            {o.label}
          </button>
        )
      })}
    </div>
  )
}
