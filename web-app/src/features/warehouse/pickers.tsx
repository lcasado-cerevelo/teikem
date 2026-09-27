// Selectores compartidos del módulo de almacén (Lote F6).
// - WarehousePicker: lista simple (<select>) sobre GET /api/v1/warehouses?includeInactive=false. Sin buscador: un tenant
//   suele tener pocos almacenes. Opciones "Code · Name"; el valor es el publicId (lo que piden los requests).
// - ProductPicker: combobox con buscador (mismo patrón que ClientPicker del kit) sobre
//   GET /api/v1/products?search=&activeOnly=true, 250 ms entre teclas, opciones "SKU · Nombre"; el valor es el publicId.
// Ambos tienen su variante `...Input` para usarse dentro de un <Field name="…"> del kit (react-hook-form).
// Si el usuario no puede consultar almacenes/productos (403 forbidden/module_disabled) se muestra un aviso y NO se le saca de
// la pantalla (`handleAccessDenied: false`).
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { useCallback, useEffect, useId, useRef, useState, type KeyboardEvent } from 'react'
import { useController, useFormContext } from 'react-hook-form'
import { api, unwrap } from '../../kernel/api/client'
import { ApiError } from '../../kernel/api/problem'
import { useT } from '../../kernel/i18n/useT'
import { Chip } from '../../kernel/ui/Chip'
import { useFieldInfo } from '../../kernel/ui/formContext'
import { IconClose } from '../../kernel/ui/icons'
import { useDismiss } from '../../kernel/ui/useDismiss'
import '../../kernel/ui/ui.css'
import { productLabel, useWarehouse, useWarehouses, warehouseLabel, type ProductListItemDto, type WarehouseDto } from './api'

function isAccessDenied(error: unknown): boolean {
  return error instanceof ApiError && (error.code === 'forbidden' || error.code === 'module_disabled')
}

// =====================================================================================================================
// WarehousePicker
// =====================================================================================================================

export interface WarehousePickerProps {
  /** publicId del almacén elegido (null = ninguno). */
  value: string | null | undefined
  onChange: (publicId: string | null, warehouse: WarehouseDto | null) => void
  /** Texto de la opción vacía ('' = ninguno). Por defecto "Seleccione un almacén…". `null` = sin opción vacía. */
  placeholder?: string | null
  id?: string
  disabled?: boolean
  invalid?: boolean
  required?: boolean
  'aria-label'?: string
  'aria-describedby'?: string
  onBlur?: () => void
}

/** Lista simple de almacenes activos. Un valor que ya no está en la lista (almacén dado de baja) se conserva como opción
 *  para no perder el dato de un registro histórico. */
export function WarehousePicker({ value, onChange, placeholder, id, disabled, invalid, required, onBlur, ...aria }: WarehousePickerProps) {
  const t = useT()
  const { data = [], isLoading, error } = useWarehouses({ includeInactive: false }, { handleAccessDenied: false })
  const emptyLabel = placeholder === undefined ? t('ui.warehousePicker.placeholder') : placeholder

  let status: string | null = null
  if (isLoading) status = t('common.loading')
  else if (isAccessDenied(error)) status = t('ui.warehousePicker.noAccess')
  else if (error) status = t('errors.generic')
  else if (data.length === 0) status = t('ui.warehousePicker.none')

  // valor fuera de la lista (almacén inactivo en un registro histórico): se pide su ficha para mostrar "Code · Name"
  const missing = !isLoading && Boolean(value) && !data.some((w) => w.publicId === value)
  const missingDetail = useWarehouse(missing ? value : null, { handleAccessDenied: false })

  return (
    <select
      id={id}
      value={value ?? ''}
      disabled={disabled}
      aria-invalid={invalid || undefined}
      aria-required={required || undefined}
      {...aria}
      onBlur={onBlur}
      onChange={(e) => {
        const next = e.target.value || null
        onChange(next, data.find((w) => w.publicId === next) ?? null)
      }}
    >
      {emptyLabel !== null && <option value="">{status ?? emptyLabel}</option>}
      {emptyLabel === null && status && (
        <option value="" disabled>
          {status}
        </option>
      )}
      {missing && <option value={value ?? ''}>{warehouseLabel(missingDetail.data?.warehouse) || (missingDetail.isLoading ? t('common.loading') : value)}</option>}
      {data.map((w) => (
        <option key={w.publicId} value={w.publicId}>
          {warehouseLabel(w)}
        </option>
      ))}
    </select>
  )
}

export interface WarehousePickerInputProps {
  placeholder?: string | null
  disabled?: boolean
}

/** WarehousePicker dentro de un <Field name="warehousePublicId">: el valor del formulario es el publicId (o null). */
export function WarehousePickerInput(props: WarehousePickerInputProps) {
  const info = useFieldInfo('WarehousePickerInput')
  const { control } = useFormContext()
  const { field } = useController({ name: info.name, control })
  return (
    <WarehousePicker
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

// =====================================================================================================================
// ProductPicker
// =====================================================================================================================

const MAX_SHOWN = 50

export interface ProductPickerProps {
  /** publicId del producto elegido (null = ninguno). */
  value: string | null | undefined
  /** Recibe también la fila de la lista (trae `trackingTypeCode`, `isOwn`, existencias…); null al quitar o si el valor
   *  llegó de fuera. */
  onChange: (publicId: string | null, product: ProductListItemDto | null) => void
  /** Solo productos propios (sin dueño cliente), p. ej. para órdenes de compra. */
  ownOnly?: boolean
  /** Solo productos del dueño indicado. */
  ownerClientPublicId?: string | null
  /** Limita a productos con existencia en este almacén (junto con `onlyAvailable`). */
  warehousePublicId?: string | null
  onlyAvailable?: boolean
  id?: string
  placeholder?: string
  disabled?: boolean
  invalid?: boolean
  required?: boolean
  'aria-label'?: string
  'aria-describedby'?: string
  onBlur?: () => void
}

export function ProductPicker({
  value,
  onChange,
  ownOnly,
  ownerClientPublicId,
  warehousePublicId,
  onlyAvailable,
  id,
  placeholder,
  disabled,
  invalid,
  required,
  onBlur,
  ...aria
}: ProductPickerProps) {
  const t = useT()
  const autoId = useId()
  const inputId = id ?? autoId
  const listId = `${inputId}-list`
  const boxRef = useRef<HTMLDivElement>(null)
  const [open, setOpen] = useState(false)
  const [text, setText] = useState('')
  const [search, setSearch] = useState('')
  const [active, setActive] = useState(0)
  const [picked, setPicked] = useState<ProductListItemDto | null>(null)
  const dismiss = useCallback(() => setOpen(false), [])
  useDismiss(boxRef, open, dismiss)

  // búsqueda con pausa de 250 ms entre teclas
  useEffect(() => {
    const h = setTimeout(() => setSearch(text.trim()), 250)
    return () => clearTimeout(h)
  }, [text])

  const query = {
    search: search || undefined,
    activeOnly: true,
    ownOnly: ownOnly || undefined,
    ownerClientPublicId: ownerClientPublicId || undefined,
    warehousePublicId: warehousePublicId || undefined,
    onlyAvailable: onlyAvailable || undefined,
    take: MAX_SHOWN,
  }
  const list = useQuery({
    queryKey: ['/api/v1/products', query],
    queryFn: () => unwrap(api.GET('/api/v1/products', { params: { query } })),
    enabled: open && !disabled,
    placeholderData: keepPreviousData, // mientras llega la nueva búsqueda se ven los resultados anteriores
    meta: { handleAccessDenied: false },
  })

  // valor que llega de fuera (formulario de edición): se pide la ficha para mostrar "SKU · Nombre"
  const known = picked && picked.publicId === value ? picked : null
  const detail = useQuery({
    queryKey: ['/api/v1/products/{publicId}', value],
    queryFn: () => unwrap(api.GET('/api/v1/products/{publicId}', { params: { path: { publicId: value ?? '' } } })),
    enabled: Boolean(value) && !known,
    meta: { handleAccessDenied: false },
  })
  const selectedLabel = known ? productLabel(known) : value ? productLabel(detail.data?.product) : ''

  const options = (list.data?.items ?? []).slice(0, MAX_SHOWN)

  const choose = (p: ProductListItemDto) => {
    setPicked(p)
    setOpen(false)
    setText('')
    onChange(p.publicId ?? null, p)
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
      const p = options[active]
      if (p) choose(p)
    } else if (e.key === 'Escape' && open) {
      e.stopPropagation()
      setOpen(false)
    }
  }

  let status: string | null = null
  if (list.isLoading) status = t('common.loading')
  else if (isAccessDenied(list.error)) status = t('ui.productPicker.noAccess')
  else if (list.error) status = t('errors.generic')
  else if (options.length === 0) status = t('ui.productPicker.none')

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
          placeholder={placeholder ?? t('ui.productPicker.placeholder')}
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
          <button type="button" className="iconbtn" aria-label={t('ui.productPicker.clear')} onClick={clear}>
            <IconClose />
          </button>
        )}
      </div>
      {open && (
        <div className="mp">
          <div className="milist" id={listId} role="listbox">
            {status && <div className="mnone">{status}</div>}
            {!status &&
              options.map((p, i) => (
                <div
                  key={p.publicId ?? p.id}
                  id={`${listId}-${i}`}
                  role="option"
                  aria-selected={p.publicId === value}
                  className={i === active ? 'mi on' : 'mi'}
                  onMouseDown={(e) => e.preventDefault()}
                  onMouseEnter={() => setActive(i)}
                  onClick={() => choose(p)}
                >
                  <span>
                    <span className="code">{p.sku}</span> · {p.name}
                  </span>
                  {!p.isOwn && p.ownerName && <Chip>{p.ownerName}</Chip>}
                </div>
              ))}
          </div>
        </div>
      )}
    </div>
  )
}

export interface ProductPickerInputProps {
  ownOnly?: boolean
  ownerClientPublicId?: string | null
  warehousePublicId?: string | null
  onlyAvailable?: boolean
  placeholder?: string
  disabled?: boolean
  /** Aviso con la fila elegida (p. ej. para condicionar lote/series al `trackingTypeCode`). */
  onPicked?: (product: ProductListItemDto | null) => void
}

/** ProductPicker dentro de un <Field name="productPublicId">: el valor del formulario es el publicId (o null). */
export function ProductPickerInput({ onPicked, ...props }: ProductPickerInputProps) {
  const info = useFieldInfo('ProductPickerInput')
  const { control } = useFormContext()
  const { field } = useController({ name: info.name, control })
  return (
    <ProductPicker
      {...props}
      id={info.id}
      value={(field.value as string | null | undefined) ?? null}
      onChange={(publicId, product) => {
        field.onChange(publicId)
        onPicked?.(product)
      }}
      onBlur={field.onBlur}
      invalid={info.invalid}
      required={info.required}
      aria-describedby={info.describedBy}
    />
  )
}
