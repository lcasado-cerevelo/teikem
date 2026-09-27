// Selector de cliente con buscador: GET /api/v1/clients?search=&includeInactive= (permiso clients.read, módulo CATALOG).
// Muestra "Code · Name"; el valor es el publicId del cliente (lo que piden los requests: clientPublicId).
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { useCallback, useEffect, useId, useRef, useState, type KeyboardEvent } from 'react'
import { useController, useFormContext } from 'react-hook-form'
import { api, unwrap } from '../api/client'
import { ApiError } from '../api/problem'
import type { components } from '../api/schema'
import { useT } from '../i18n/useT'
import { Chip } from './Chip'
import { useFieldInfo } from './formContext'
import { IconClose } from './icons'
import { useDismiss } from './useDismiss'
import './ui.css'

export type ClientOption = components['schemas']['ClientListItemDto']

const MAX_SHOWN = 50

/** "Code · Name" (o lo que haya). */
function clientLabel(c: { code?: string | null; name?: string | null } | null | undefined): string {
  if (!c) return ''
  return [c.code, c.name].filter(Boolean).join(' · ')
}

export interface ClientPickerProps {
  /** publicId del cliente elegido (null = ninguno). */
  value: string | null | undefined
  onChange: (publicId: string | null, client: ClientOption | null) => void
  /** Incluir clientes inactivos en la búsqueda (por defecto no: solo se eligen activos). */
  includeInactive?: boolean
  id?: string
  placeholder?: string
  disabled?: boolean
  invalid?: boolean
  required?: boolean
  /** Etiqueta accesible si no hay un <label htmlFor> externo. */
  'aria-label'?: string
  'aria-describedby'?: string
  onBlur?: () => void
}

export function ClientPicker({
  value,
  onChange,
  includeInactive = false,
  id,
  placeholder,
  disabled,
  invalid,
  required,
  onBlur,
  ...aria
}: ClientPickerProps) {
  const t = useT()
  const autoId = useId()
  const inputId = id ?? autoId
  const listId = `${inputId}-list`
  const boxRef = useRef<HTMLDivElement>(null)
  const [open, setOpen] = useState(false)
  const [text, setText] = useState('')
  const [search, setSearch] = useState('')
  const [active, setActive] = useState(0)
  const [picked, setPicked] = useState<ClientOption | null>(null)
  const dismiss = useCallback(() => setOpen(false), [])
  useDismiss(boxRef, open, dismiss)

  // búsqueda con pausa de 250 ms entre teclas
  useEffect(() => {
    const h = setTimeout(() => setSearch(text.trim()), 250)
    return () => clearTimeout(h)
  }, [text])

  const query = { search: search || undefined, includeInactive }
  const list = useQuery({
    queryKey: ['/api/v1/clients', query],
    queryFn: () => unwrap(api.GET('/api/v1/clients', { params: { query } })),
    enabled: open && !disabled,
    placeholderData: keepPreviousData, // mientras llega la nueva búsqueda se ven los resultados anteriores
    meta: { handleAccessDenied: false },
  })

  // valor que llega de fuera (formulario de edición): se pide la ficha para mostrar "Code · Name"
  const known = picked && picked.publicId === value ? picked : null
  const detail = useQuery({
    queryKey: ['/api/v1/clients/{publicId}', value],
    queryFn: () => unwrap(api.GET('/api/v1/clients/{publicId}', { params: { path: { publicId: value ?? '' } } })),
    enabled: Boolean(value) && !known,
    meta: { handleAccessDenied: false },
  })
  const selectedLabel = known ? clientLabel(known) : value ? clientLabel(detail.data) : ''

  const options = (list.data ?? []).slice(0, MAX_SHOWN)

  const choose = (c: ClientOption) => {
    setPicked(c)
    setOpen(false)
    setText('')
    onChange(c.publicId ?? null, c)
  }

  const clear = () => {
    setPicked(null)
    setText('')
    onChange(null, null)
  }

  const onKeyDown = (e: KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'ArrowDown') {
      e.preventDefault()
      if (!open) setOpen(true)
      else setActive((i) => Math.min(i + 1, options.length - 1))
    } else if (e.key === 'ArrowUp') {
      e.preventDefault()
      setActive((i) => Math.max(i - 1, 0))
    } else if (e.key === 'Enter' && open) {
      e.preventDefault()
      const c = options[active]
      if (c) choose(c)
    } else if (e.key === 'Escape' && open) {
      e.stopPropagation()
      setOpen(false)
    }
  }

  let status: string | null = null
  if (list.isLoading) status = t('common.loading')
  else if (list.error instanceof ApiError && (list.error.code === 'forbidden' || list.error.code === 'module_disabled'))
    status = t('ui.clientPicker.noAccess')
  else if (list.error) status = t('errors.generic')
  else if (options.length === 0) status = t('ui.clientPicker.none')

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
          aria-activedescendant={open && options[active] ? `${listId}-${active}` : undefined}
          aria-invalid={invalid || undefined}
          aria-required={required || undefined}
          {...aria}
          disabled={disabled}
          placeholder={placeholder ?? t('ui.clientPicker.placeholder')}
          value={open ? text : selectedLabel}
          onFocus={() => {
            setText('')
            setActive(0)
            setOpen(true)
          }}
          onChange={(e) => {
            setText(e.target.value)
            setActive(0)
            setOpen(true)
          }}
          onKeyDown={onKeyDown}
          onBlur={onBlur}
        />
        {value && !disabled && (
          <button type="button" className="iconbtn" aria-label={t('ui.clientPicker.clear')} onClick={clear}>
            <IconClose />
          </button>
        )}
      </div>
      {open && (
        <div className="mp">
          <div className="milist" id={listId} role="listbox">
            {status && <div className="mnone">{status}</div>}
            {!status &&
              options.map((c, i) => (
                <div
                  key={c.publicId ?? c.id}
                  id={`${listId}-${i}`}
                  role="option"
                  aria-selected={c.publicId === value}
                  className={i === active ? 'mi on' : 'mi'}
                  onMouseDown={(e) => e.preventDefault()}
                  onMouseEnter={() => setActive(i)}
                  onClick={() => choose(c)}
                >
                  <span>
                    <span className="code">{c.code}</span> · {c.name}
                  </span>
                  {c.isActive === false && <Chip tone="warn">{t('ui.clientPicker.inactive')}</Chip>}
                </div>
              ))}
          </div>
        </div>
      )}
    </div>
  )
}

export interface ClientPickerInputProps {
  includeInactive?: boolean
  placeholder?: string
  disabled?: boolean
}

/** ClientPicker dentro de un <Field name="clientPublicId">: el valor del formulario es el publicId (o null). */
export function ClientPickerInput(props: ClientPickerInputProps) {
  const info = useFieldInfo('ClientPickerInput')
  const { control } = useFormContext()
  const { field } = useController({ name: info.name, control })
  return (
    <ClientPicker
      {...props}
      id={info.id}
      value={(field.value as string | null | undefined) ?? null}
      onChange={(publicId) => field.onChange(publicId)}
      onBlur={field.onBlur}
      invalid={info.invalid}
      required={info.required}
      aria-describedby={info.describedBy}
    />
  )
}
