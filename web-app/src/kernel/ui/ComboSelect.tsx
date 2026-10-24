// Selección ÚNICA con buscador sobre una lista local (Lote 1 de cambios de Almacén): combobox accesible como ClientPicker
// (input `role="combobox"` + listbox propio, ↑/↓/Enter/Escape, ✕ para quitar, clic para reabrir), pero las opciones ya
// están en memoria (catálogos, zonas de un almacén…) y se filtran en el cliente con `matchesQ` (sin mayúsculas ni acentos,
// cada palabra en la etiqueta, el valor o `hint`). Enter con un texto igual al valor o a la etiqueta de una opción la elige
// aunque otra esté resaltada (lector de código de barras). Opcional (Lote 213): `createLabel` + `onCreate` añaden al final de la lista la
// opción «crear lo escrito» (solo con texto escrito; con cero coincidencias queda resaltada, así que escribir un nombre nuevo y Enter
// la elige) y `seedText` abre la lista con ese texto (volver de cancelar la creación sin perder lo escrito). `ComboSelectInput` es la variante para un <Field> del kit.
import { useCallback, useId, useMemo, useRef, useState, type KeyboardEvent } from 'react'
import { useController, useFormContext } from 'react-hook-form'
import { useT } from '../i18n/useT'
import { useFieldInfo } from './formContext'
import { IconClose } from './icons'
import { exactComboMatch, type ComboOption } from './comboMatch'
import { matchesQ } from './matchesQ'
import { useDismiss } from './useDismiss'
import './ui.css'

export type { ComboOption }

export interface ComboSelectProps {
  options: readonly ComboOption[]
  /** Valor elegido ('' o null = ninguno). */
  value: string | null | undefined
  onChange: (value: string, option: ComboOption | null) => void
  id?: string
  /** Texto del campo sin valor (por defecto "Seleccione…"). */
  placeholder?: string
  /** false = no se ofrece quitar el valor (✕). Por defecto true. */
  clearable?: boolean
  disabled?: boolean
  invalid?: boolean
  required?: boolean
  /** Texto de la lista mientras las opciones cargan (p. ej. `isLoading` del catálogo). */
  loading?: boolean
  'aria-label'?: string
  'aria-describedby'?: string
  onBlur?: () => void
  /** Texto de la opción «crear» a partir de lo escrito (p. ej. `Nuevo consignatario «Ana»`). Con `onCreate` activa la opción; apagada por omisión. */
  createLabel?: (text: string) => string
  /** Se llama con el texto escrito (sin espacios sobrantes) al elegir la opción «crear» (Enter o clic). La lista se cierra. */
  onCreate?: (text: string) => void
  /** Texto con el que se abre la lista al entrar al campo (por omisión vacío); para volver tras cancelar una creación. */
  seedText?: string
}

export function ComboSelect({
  options,
  value,
  onChange,
  id,
  placeholder,
  clearable = true,
  disabled,
  invalid,
  required,
  loading,
  onBlur,
  createLabel,
  onCreate,
  seedText,
  ...aria
}: ComboSelectProps) {
  const t = useT()
  const autoId = useId()
  const inputId = id ?? autoId
  const listId = `${inputId}-list`
  const boxRef = useRef<HTMLDivElement>(null)
  const [open, setOpen] = useState(false)
  const [text, setText] = useState('')
  const [active, setActive] = useState(0)
  const dismiss = useCallback(() => setOpen(false), [])
  useDismiss(boxRef, open, dismiss)

  const current = value ? options.find((o) => o.value === value) : undefined
  // un valor que no está entre las opciones (p. ej. catálogo aún cargando) se muestra tal cual
  const selectedLabel = current?.label ?? (value || '')
  const shown = useMemo(() => options.filter((o) => matchesQ(text, o.label, o.value, o.hint)), [options, text])

  // opción «crear»: solo con texto escrito y la lista ya cargada; va al final (índice = shown.length)
  const createText = text.trim()
  const canCreate = !!onCreate && !loading && createText !== ''
  const total = shown.length + (canCreate ? 1 : 0)

  let status: string | null = null
  if (loading) status = t('common.loading')
  else if (shown.length === 0 && !canCreate) status = t('ui.searchSelect.none')

  const openList = () => {
    if (seedText) {
      // volver de cancelar una creación: se abre con lo que se había escrito
      setText(seedText)
      setActive(0)
    } else {
      setText('')
      // al abrir se ven todas: se resalta la elegida
      setActive(Math.max(0, options.findIndex((o) => o.value === value)))
    }
    setOpen(true)
  }

  const choose = (o: ComboOption) => {
    setOpen(false)
    setText('')
    onChange(o.value, o)
  }

  const create = () => {
    setOpen(false)
    setText('')
    onCreate?.(createText)
  }

  const clear = () => {
    setText('')
    onChange('', null)
  }

  const onKeyDown = (e: KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'ArrowDown') {
      e.preventDefault()
      if (!open) openList()
      else setActive((i) => Math.min(i + 1, total - 1))
    } else if (e.key === 'ArrowUp') {
      e.preventDefault()
      setActive((i) => Math.max(i - 1, 0))
    } else if (e.key === 'Enter' && open) {
      e.preventDefault()
      const o = exactComboMatch(shown, text) ?? shown[active]
      if (o && !status) choose(o)
      else if (!o && canCreate && active === shown.length) create()
    } else if (e.key === 'Escape' && open) {
      // no cierra el Modal que lo contiene: solo la lista
      e.stopPropagation()
      setOpen(false)
    } else if (e.key === 'Tab' && open) {
      setOpen(false)
    }
  }

  return (
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
          aria-activedescendant={open && !status && (shown[active] || (canCreate && active === shown.length)) ? `${listId}-${active}` : undefined}
          aria-invalid={invalid || undefined}
          aria-required={required || undefined}
          {...aria}
          disabled={disabled}
          placeholder={placeholder ?? t('ui.comboSelect.placeholder')}
          value={open ? text : selectedLabel}
          onFocus={openList}
          // tras elegir, el foco se queda en el campo: un clic vuelve a abrir la lista
          onClick={() => {
            if (!open) openList()
          }}
          onChange={(e) => {
            setText(e.target.value)
            setActive(0)
            setOpen(true)
          }}
          onKeyDown={onKeyDown}
          onBlur={onBlur}
        />
        {clearable && value && !disabled && (
          <button type="button" className="iconbtn" aria-label={t('ui.comboSelect.clear')} onClick={clear}>
            <IconClose />
          </button>
        )}
      </div>
      {open && (
        <div className="mp">
          <div className="milist" id={listId} role="listbox">
            {status && <div className="mnone">{status}</div>}
            {!status &&
              shown.map((o, i) => (
                <div
                  key={o.value}
                  id={`${listId}-${i}`}
                  role="option"
                  aria-selected={o.value === value}
                  className={i === active ? 'mi on' : 'mi'}
                  onMouseDown={(e) => e.preventDefault()}
                  onMouseEnter={() => setActive(i)}
                  onClick={() => choose(o)}
                >
                  <span>{o.label}</span>
                  {o.hint && <span className="sub">{o.hint}</span>}
                </div>
              ))}
            {canCreate && (
              <div
                id={`${listId}-${shown.length}`}
                role="option"
                aria-selected={false}
                className={active === shown.length ? 'mi on mi-create' : 'mi mi-create'}
                onMouseDown={(e) => e.preventDefault()}
                onMouseEnter={() => setActive(shown.length)}
                onClick={create}
              >
                <span>{createLabel ? createLabel(createText) : t('ui.comboSelect.create', { text: createText })}</span>
              </div>
            )}
          </div>
        </div>
      )}
    </div>
  )
}

export type ComboSelectInputProps = Omit<ComboSelectProps, 'value' | 'onChange' | 'id' | 'invalid' | 'required' | 'aria-describedby' | 'onBlur'> & {
  /** Aviso con la opción elegida (o null al quitarla), además de guardar su valor en el formulario. */
  onPicked?: (option: ComboOption | null) => void
}

/** ComboSelect dentro de un <Field name="…">: el valor del formulario es el `value` de la opción ('' = ninguna). */
export function ComboSelectInput({ onPicked, ...props }: ComboSelectInputProps) {
  const info = useFieldInfo('ComboSelectInput')
  const { control } = useFormContext()
  const { field } = useController({ name: info.name, control })
  return (
    <ComboSelect
      {...props}
      id={info.id}
      value={(field.value as string | null | undefined) ?? ''}
      onChange={(v, o) => {
        field.onChange(v)
        onPicked?.(o)
      }}
      onBlur={field.onBlur}
      invalid={info.invalid}
      required={info.required}
      aria-describedby={info.describedBy}
    />
  )
}
