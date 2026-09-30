// Lote 1 (cambios de Almacén) — Ciudad o código postal del almacén en UN solo combobox (alta en la lista de almacenes y
// pestaña Datos de la ficha). Mismo patrón que ClientPicker del kit: input `role="combobox"` + listbox propio,
// ↑/↓/Enter/Escape, ✕ para quitar, 250 ms entre teclas y los resultados anteriores visibles mientras llega la búsqueda.
// Busca en `GET /api/v1/postal-localities?search=` (catálogo USPS de ZIP de EE. UU. y Puerto Rico, PR primero): por ciudad
// postal, municipio o prefijo de código postal. Cada opción es "ZIP · CIUDAD POSTAL (Municipio), Estado[ · País]"
// (`localityOptionLabel`); al elegirla se llenan Ciudad (el municipio si lo hay —PR, con acentos—, si no la ciudad postal:
// `localityCity`), Código postal, Estado y País (el país no se escribe: sale de la localidad; sin elegir, el backend usa PR).
// El campo muestra "Ciudad · ZIP" con lo guardado en el almacén aunque no venga del catálogo.
// - `PostalLocalityPicker`: control suelto (`city`/`postalCode` = lo que muestra; `onChange(localidad | null)`).
// - `PostalLocalityPickerInput`: dentro de `<Field name="city">`; llena con `setValue` los otros tres campos del formulario.
// - `DerivedLocalityFields`: Estado y País de solo lectura (se ven, no se escriben), leídos del formulario.
import { useCallback, useEffect, useId, useRef, useState, type KeyboardEvent } from 'react'
import { useController, useFormContext, useWatch } from 'react-hook-form'
import { useLookups } from '../../kernel/catalogs'
import { useT } from '../../kernel/i18n'
import { useFieldInfo } from '../../kernel/ui/formContext'
import { IconClose } from '../../kernel/ui/icons'
import { useDismiss } from '../../kernel/ui/useDismiss'
import '../../kernel/ui/ui.css'
import { isAccessDenied } from './accessDenied'
import { ReadOnlyField } from './BinModal'
import { usePostalLocalities, type PostalLocalityDto } from './api'
import { localityCity, localityOptionLabel } from './warehouseFilters'

/** "Ciudad · ZIP" guardados en el formulario (o lo que haya). */
function localityLabel(l: { city?: string | null; postalCode?: string | null } | null | undefined): string {
  if (!l) return ''
  return [l.city, l.postalCode].filter(Boolean).join(' · ')
}

export interface PostalLocalityPickerProps {
  /** Ciudad actual (para mostrar el valor elegido). */
  city: string | null | undefined
  /** Código postal actual. */
  postalCode: string | null | undefined
  /** Localidad elegida (null = se quitó). */
  onChange: (locality: PostalLocalityDto | null) => void
  id?: string
  placeholder?: string
  disabled?: boolean
  invalid?: boolean
  required?: boolean
  'aria-label'?: string
  'aria-describedby'?: string
  onBlur?: () => void
}

export function PostalLocalityPicker({ city, postalCode, onChange, id, placeholder, disabled, invalid, required, onBlur, ...aria }: PostalLocalityPickerProps) {
  const t = useT()
  const autoId = useId()
  const inputId = id ?? autoId
  const listId = `${inputId}-list`
  const boxRef = useRef<HTMLDivElement>(null)
  const [open, setOpen] = useState(false)
  const [text, setText] = useState('')
  const [search, setSearch] = useState('')
  const [active, setActive] = useState(0)
  const dismiss = useCallback(() => setOpen(false), [])
  useDismiss(boxRef, open, dismiss)

  // búsqueda con pausa de 250 ms entre teclas
  useEffect(() => {
    const h = setTimeout(() => setSearch(text.trim()), 250)
    return () => clearTimeout(h)
  }, [text])

  const list = usePostalLocalities(search, { enabled: open && !disabled })
  const options = list.data ?? []
  const selectedLabel = localityLabel({ city, postalCode })

  let status: string | null = null
  if (list.isLoading) status = t('common.loading')
  else if (isAccessDenied(list.error)) status = t('warehouse.postalPicker.noAccess')
  else if (list.error) status = t('errors.generic')
  else if (options.length === 0) status = t('warehouse.postalPicker.none')

  const openList = () => {
    setText('')
    setActive(0)
    setOpen(true)
  }

  const choose = (l: PostalLocalityDto) => {
    setOpen(false)
    setText('')
    onChange(l)
  }

  const clear = () => {
    setText('')
    onChange(null)
  }

  const onKeyDown = (e: KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'ArrowDown') {
      e.preventDefault()
      if (!open) openList()
      else setActive((i) => Math.min(i + 1, options.length - 1))
    } else if (e.key === 'ArrowUp') {
      e.preventDefault()
      setActive((i) => Math.max(i - 1, 0))
    } else if (e.key === 'Enter' && open) {
      e.preventDefault()
      // un código postal completo gana a la opción resaltada
      const typed = text.trim()
      const l = options.find((o) => typed !== '' && o.postalCode === typed) ?? options[active]
      if (l && !status) choose(l)
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
          aria-activedescendant={open && !status && options[active] ? `${listId}-${active}` : undefined}
          aria-invalid={invalid || undefined}
          aria-required={required || undefined}
          {...aria}
          disabled={disabled}
          placeholder={placeholder ?? t('warehouse.postalPicker.placeholder')}
          value={open ? text : selectedLabel}
          onFocus={openList}
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
        {(city || postalCode) && !disabled && (
          <button type="button" className="iconbtn" aria-label={t('warehouse.postalPicker.clear')} onClick={clear}>
            <IconClose />
          </button>
        )}
      </div>
      {open && (
        <div className="mp">
          <div className="milist" id={listId} role="listbox">
            {status && <div className="mnone">{status}</div>}
            {!status &&
              options.map((l, i) => (
                <div
                  key={l.id ?? `${l.city}-${l.postalCode}`}
                  id={`${listId}-${i}`}
                  role="option"
                  aria-selected={localityCity(l) === city && l.postalCode === postalCode}
                  className={i === active ? 'mi on' : 'mi'}
                  onMouseDown={(e) => e.preventDefault()}
                  onMouseEnter={() => setActive(i)}
                  onClick={() => choose(l)}
                >
                  <span>{localityOptionLabel(l)}</span>
                </div>
              ))}
          </div>
        </div>
      )}
    </div>
  )
}

/** Nombres de los campos del formulario que llena la localidad elegida (además del del `Field`, que es la ciudad). */
export interface LocalityFieldNames {
  postalCode?: string
  state?: string
  country?: string
}

export interface PostalLocalityPickerInputProps {
  fields?: LocalityFieldNames
  placeholder?: string
  disabled?: boolean
}

/**
 * PostalLocalityPicker dentro de `<Field name="city">`: al elegir pone la ciudad en el campo del Field y, con `setValue`
 * (marcando el formulario como modificado), el código postal, el estado y el país (código del lookup Country). Quitar el
 * valor vacía ciudad, código postal y estado (el país se queda: sin localidad el backend usa PR).
 */
export function PostalLocalityPickerInput({ fields, placeholder, disabled }: PostalLocalityPickerInputProps) {
  const info = useFieldInfo('PostalLocalityPickerInput')
  const { control, setValue } = useFormContext()
  const { field } = useController({ name: info.name, control })
  const postalName = fields?.postalCode ?? 'postalCode'
  const stateName = fields?.state ?? 'state'
  const countryName = fields?.country ?? 'country'
  const postalCode = useWatch({ control, name: postalName }) as string | null | undefined
  const opts = { shouldDirty: true, shouldValidate: false }
  return (
    <PostalLocalityPicker
      id={info.id}
      city={(field.value as string | null | undefined) ?? ''}
      postalCode={postalCode ?? ''}
      placeholder={placeholder}
      disabled={disabled}
      invalid={info.invalid}
      required={info.required}
      aria-describedby={info.describedBy}
      onBlur={field.onBlur}
      onChange={(l) => {
        field.onChange(l ? localityCity(l) : '')
        setValue(postalName, l?.postalCode ?? '', opts)
        setValue(stateName, l?.state ?? '', opts)
        if (l?.countryCode) setValue(countryName, l.countryCode, opts)
      }}
    />
  )
}

/**
 * Estado y País derivados de la localidad elegida, de solo lectura (`.r2`). El país se muestra con su etiqueta del
 * catálogo `Country` (el formulario guarda el código).
 */
export function DerivedLocalityFields({ fields }: { fields?: LocalityFieldNames }) {
  const t = useT()
  const { control } = useFormContext()
  const state = useWatch({ control, name: fields?.state ?? 'state' }) as string | null | undefined
  const country = useWatch({ control, name: fields?.country ?? 'country' }) as string | null | undefined
  const { data: countries = [] } = useLookups('Country')
  const countryLabel = countries.find((c) => c.code === country)?.label ?? country ?? ''
  return (
    <div className="r2">
      <ReadOnlyField label={t('warehouse.detail.state')} value={state ?? ''} help={t('warehouse.postalPicker.derivedHelp')} />
      <ReadOnlyField label={t('warehouse.detail.country')} value={countryLabel} help={t('warehouse.postalPicker.derivedHelp')} />
    </div>
  )
}
