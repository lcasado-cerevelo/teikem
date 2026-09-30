// Lote 14 (D11) — control "Subir / Bajar" de un ajuste de inventario (segmentado `.seg` con dos radios), dentro de un
// <Field name="direction">: el valor del formulario es 'up' | 'down' | '' (sin elegir; zod pide elegir con
// `adjustDirectionSchema`). Lo usan InventoryAdjustModal (Transferencias y ajustes y Kárdex) y el bloque de ajuste de
// ProductEditorModal (ficha del producto). La cantidad se captura en positivo y la pantalla pone el signo
// (`signedAdjustQuantity`).
import type { KeyboardEvent } from 'react'
import { useController, useFormContext } from 'react-hook-form'
import { useT } from '../../kernel/i18n'
import { useFieldInfo } from '../../kernel/ui/formContext'
import { SearchMultiSelect } from '../../kernel/ui/SearchSelect'
import type { AdjustDirection } from './adjustmentReasons'
import './warehouse.css'

export interface AdjustDirectionInputProps {
  /** Nombre accesible del grupo (el mismo texto que la etiqueta del Field). */
  label: string
  disabled?: boolean
  /** Aviso al cambiar de dirección (p. ej. para quitar un motivo que ya no vale). */
  onPicked?: (direction: AdjustDirection) => void
}

const DIRECTIONS: readonly AdjustDirection[] = ['up', 'down']

export function AdjustDirectionInput({ label, disabled, onPicked }: AdjustDirectionInputProps) {
  const t = useT()
  const info = useFieldInfo('AdjustDirectionInput')
  const { control } = useFormContext()
  const { field } = useController({ name: info.name, control })
  const value = (field.value as string) ?? ''

  const pick = (d: AdjustDirection) => {
    field.onChange(d)
    onPicked?.(d)
  }

  // ←/→ cambian de opción, como un grupo de radios nativo
  const onKeyDown = (e: KeyboardEvent<HTMLButtonElement>) => {
    if (e.key !== 'ArrowLeft' && e.key !== 'ArrowRight' && e.key !== 'ArrowUp' && e.key !== 'ArrowDown') return
    e.preventDefault()
    const next: AdjustDirection = value === 'up' ? 'down' : 'up'
    pick(next)
    const btn = e.currentTarget.parentElement?.querySelector<HTMLButtonElement>(`button[data-dir="${next}"]`)
    btn?.focus()
  }

  return (
    <div
      id={info.id}
      role="radiogroup"
      aria-label={label}
      aria-invalid={info.invalid || undefined}
      aria-required={info.required || undefined}
      aria-describedby={info.describedBy}
      className="seg adj-dir"
    >
      {DIRECTIONS.map((d) => {
        const on = value === d
        return (
          <button
            key={d}
            type="button"
            role="radio"
            data-dir={d}
            aria-checked={on}
            // sin elegir, se llega con Tab a la primera; elegida, solo a la elegida (patrón de radios)
            tabIndex={on || (!value && d === 'up') ? 0 : -1}
            className={on ? `on ${d}` : undefined}
            disabled={disabled}
            onClick={() => pick(d)}
            onKeyDown={onKeyDown}
            onBlur={field.onBlur}
          >
            <span aria-hidden="true">{d === 'up' ? '▲' : '▼'}</span>
            {t(d === 'up' ? 'warehouse.inventory.adjustModal.up' : 'warehouse.inventory.adjustModal.down')}
          </button>
        )
      })}
    </div>
  )
}

export interface SerialsPickInputProps {
  /** Series que se pueden elegir (las disponibles en la posición de origen). */
  serials: readonly string[]
  placeholder?: string
  disabled?: boolean
}

/**
 * Series a elegir dentro de un <Field name="serials"> (valor: string[]): `SearchMultiSelect` con buscador sobre las series
 * disponibles en la posición. Lo usan la salida de un ajuste ("Bajar") y la transferencia de un producto SERIAL.
 */
export function SerialsPickInput({ serials, placeholder, disabled }: SerialsPickInputProps) {
  const info = useFieldInfo('SerialsPickInput')
  const { control } = useFormContext()
  const { field } = useController({ name: info.name, control })
  const value = (field.value as string[] | undefined) ?? []
  return (
    <SearchMultiSelect
      id={info.id}
      options={serials.map((s) => ({ value: s, label: s }))}
      value={value}
      onChange={(v) => field.onChange(v)}
      placeholder={placeholder}
      disabled={disabled}
      invalid={info.invalid}
      describedBy={info.describedBy}
      onBlur={field.onBlur}
    />
  )
}
