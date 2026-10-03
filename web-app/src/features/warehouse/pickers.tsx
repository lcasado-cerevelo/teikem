// Selectores compartidos del módulo de almacén (Lote F6). Todos son combobox con buscador (mismo patrón que ClientPicker
// del kit: input `role="combobox"` + listbox propio, ↑/↓/Enter/Escape, botón de quitar) para poder llenarlos a mano o con
// un lector de código de barras (teclea el código y manda Enter: la coincidencia exacta por código se elige sola).
// - WarehousePicker: GET /api/v1/warehouses?includeInactive=false (lista completa, sin paginar ni `search`: un tenant suele
//   tener pocos almacenes), filtrada EN EL CLIENTE por código o nombre. Opciones "Code · Name"; el valor es el publicId.
// - ProductPicker: GET /api/v1/products?search=&activeOnly=true, 250 ms entre teclas, opciones "SKU · Nombre"; el valor es
//   el publicId.
// - BinPicker: GET /api/v1/warehouses/{publicId}/bins?search=&includeInactive=false&take=50 (listado paginado desde el
//   Lote 1), 250 ms entre teclas (el API compara código de posición, zona, pasillo, rack, nivel y posición), opciones
//   "Código · Zona"; el valor es el id de la posición. Las sugeridas y la posición ya elegida se piden por id (`binIds`).
//   Con `options` (lista dada, p. ej. las posiciones con disponible de un producto) no consulta el listado: filtra esa lista
//   en el cliente, muestra la pista de cada una ("Código · Zona · 2 disp.") y quita sola una posición que ya no está en ella.
// Cada uno tiene su variante `...Input` para usarse dentro de un <Field name="…"> del kit (react-hook-form).
// Si el usuario no puede consultar almacenes/productos/posiciones (403 forbidden/module_disabled) se muestra un aviso y NO
// se le saca de la pantalla (`handleAccessDenied: false`).
import { keepPreviousData, useQuery, useQueryClient } from '@tanstack/react-query'
import { useCallback, useEffect, useId, useMemo, useRef, useState, type KeyboardEvent } from 'react'
import { useController, useFormContext } from 'react-hook-form'
import { api, unwrap } from '../../kernel/api/client'
import { useT } from '../../kernel/i18n/useT'
import { Chip } from '../../kernel/ui/Chip'
import { useFieldInfo } from '../../kernel/ui/formContext'
import { IconClose } from '../../kernel/ui/icons'
import { SearchSelect } from '../../kernel/ui/SearchSelect'
import { joinFilterValues } from '../../kernel/ui/filterRegistry'
import { useRegisterFilter } from '../../kernel/ui/filterScopeContext'
import { useDismiss } from '../../kernel/ui/useDismiss'
import '../../kernel/ui/ui.css'
import './warehouse.css'
import { isAccessDenied } from './accessDenied'
import {
  binLabel,
  productLabel,
  useInventoryOwners,
  useWarehouse,
  useWarehouseZones,
  useWarehouses,
  warehouseKeys,
  warehouseLabel,
  type BinSearchItemDto,
  type GetQuery,
  type InventoryOwnerDto,
  type ProductListItemDto,
  type WarehouseBinDto,
  type WarehouseDto,
} from './api'
import { binFilterLabel, OWN_OWNER, type BinFilterItem } from './kardexView'
import { exactCodeMatch, filterBinOptions, filterWarehouses, orderBins } from './pickerMatch'

const NO_WAREHOUSES: WarehouseDto[] = []
const NO_OWNERS: InventoryOwnerDto[] = []
const NO_BINS: BinSearchItemDto[] = []

// =====================================================================================================================
// WarehousePicker
// =====================================================================================================================

export interface WarehousePickerProps {
  /** publicId del almacén elegido (null = ninguno). */
  value: string | null | undefined
  onChange: (publicId: string | null, warehouse: WarehouseDto | null) => void
  /** Texto cuando no hay almacén elegido ('' = ninguno; se puede quitar). Por defecto "Seleccione un almacén…". `null` = sin
   *  opción vacía (no se ofrece quitar el valor). */
  placeholder?: string | null
  id?: string
  disabled?: boolean
  invalid?: boolean
  required?: boolean
  'aria-label'?: string
  'aria-describedby'?: string
  onBlur?: () => void
  /** Usado como filtro de una lista: con almacén elegido se anota en el ámbito de filtros con esta etiqueta (línea
   *  "Filtros: …" de las exportaciones). Sin él (formularios) no se anota. */
  filterLabel?: string
}

/**
 * Combobox de almacenes activos con filtro en el cliente (código o nombre, sin mayúsculas ni acentos). Enter con un texto
 * igual al código de un almacén lo elige directamente (lector de código de barras). Un valor que ya no está en la lista
 * (almacén dado de baja) se conserva con su etiqueta (pide su ficha) para no perder el dato de un registro histórico.
 */
export function WarehousePicker({ value, onChange, placeholder, id, disabled, invalid, required, onBlur, filterLabel, ...aria }: WarehousePickerProps) {
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

  const { data = NO_WAREHOUSES, isLoading, error } = useWarehouses({ includeInactive: false }, { handleAccessDenied: false })
  const emptyLabel = placeholder === undefined ? t('ui.warehousePicker.placeholder') : placeholder

  let status: string | null = null
  if (isLoading) status = t('common.loading')
  else if (isAccessDenied(error)) status = t('ui.warehousePicker.noAccess')
  else if (error) status = t('errors.generic')
  else if (data.length === 0) status = t('ui.warehousePicker.none')

  // valor fuera de la lista (almacén inactivo en un registro histórico): se pide su ficha para mostrar "Code · Name"
  const current = value ? data.find((w) => w.publicId === value) : undefined
  const missing = !isLoading && Boolean(value) && !current
  const missingDetail = useWarehouse(missing ? value : null, { handleAccessDenied: false })
  let selectedLabel = ''
  if (current) selectedLabel = warehouseLabel(current)
  else if (value && isLoading) selectedLabel = t('common.loading')
  else if (value) selectedLabel = warehouseLabel(missingDetail.data?.warehouse) || (missingDetail.isLoading ? t('common.loading') : value)

  // como filtro: "Almacén ALM-DEPOT · Depósito" en la línea de filtros de las exportaciones
  useRegisterFilter(filterLabel ?? '', value ? joinFilterValues([selectedLabel || value], t) : null, boxRef, filterLabel !== undefined)

  const options = useMemo(() => filterWarehouses(data, text), [data, text])
  const listStatus = status ?? (options.length === 0 ? t('ui.warehousePicker.noMatch') : null)

  const openList = () => {
    setText('')
    // al abrir se ven todos: se resalta el elegido
    setActive(Math.max(0, data.findIndex((w) => w.publicId === value)))
    setOpen(true)
  }

  const choose = (w: WarehouseDto) => {
    setOpen(false)
    setText('')
    onChange(w.publicId ?? null, w)
  }

  const clear = () => {
    setText('')
    onChange(null, null)
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
      // el código exacto (lector de código de barras) gana a la opción resaltada
      const w = exactCodeMatch(options, text) ?? options[active]
      if (w && !listStatus) choose(w)
    } else if (e.key === 'Escape' && open) {
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
          aria-activedescendant={open && !listStatus && options[active] ? `${listId}-${active}` : undefined}
          aria-invalid={invalid || undefined}
          aria-required={required || undefined}
          {...aria}
          disabled={disabled}
          placeholder={status ?? emptyLabel ?? t('ui.warehousePicker.placeholder')}
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
        {value && !disabled && emptyLabel !== null && (
          <button type="button" className="iconbtn" aria-label={t('ui.warehousePicker.clear')} onClick={clear}>
            <IconClose />
          </button>
        )}
      </div>
      {open && (
        <div className="mp">
          <div className="milist" id={listId} role="listbox">
            {listStatus && <div className="mnone">{listStatus}</div>}
            {!listStatus &&
              options.map((w, i) => (
                <div
                  key={w.publicId ?? w.id}
                  id={`${listId}-${i}`}
                  role="option"
                  aria-selected={w.publicId === value}
                  className={i === active ? 'mi on' : 'mi'}
                  onMouseDown={(e) => e.preventDefault()}
                  onMouseEnter={() => setActive(i)}
                  onClick={() => choose(w)}
                >
                  <span>
                    <span className="code">{w.code}</span>
                    {w.name ? ` · ${w.name}` : ''}
                  </span>
                </div>
              ))}
          </div>
        </div>
      )}
    </div>
  )
}

export interface WarehousePickerInputProps {
  placeholder?: string | null
  disabled?: boolean
}

/** WarehousePicker dentro de un <Field name="warehousePublicId">: el valor del formulario es el publicId (o null al quitarlo). */
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

const MAX_SHOWN = 50

// =====================================================================================================================
// BinPicker
// =====================================================================================================================

export interface BinPickerProps {
  /** publicId del almacén dueño de las posiciones. Vacío = el control se ve deshabilitado ("Elija primero un almacén").
   *  Al cambiar de almacén el valor elegido se quita (`onChange(null, null)`): la posición ya no aplica. */
  warehousePublicId: string | null | undefined
  /** Id de la posición elegida (null = ninguna). */
  value: number | null | undefined
  /** Recibe también la fila de la lista; null al quitar o si el valor llegó de fuera. */
  onChange: (binId: number | null, bin: WarehouseBinDto | null) => void
  /** Solo posiciones de zonas de estos tipos (p. ej. STAGING/CROSSDOCK para la recepción): se piden al API las zonas del
   *  almacén y se filtra por sus ids (`zoneIds`); además se filtra en el cliente. */
  zoneTypeCodes?: readonly string[]
  /** Lote 16: sin las posiciones de zonas de estos tipos (p. ej. STAGING/CROSSDOCK para la posición destino de un recibo
   *  directo): se piden las zonas del almacén y se mandan como `zoneIds` las de los DEMÁS tipos (las zonas sin tipo
   *  cuentan como permitidas); además se filtra en el cliente. Se combina con `zoneTypeCodes`. */
  excludeZoneTypeCodes?: readonly string[]
  /** Solo posiciones con existencias (`onlyWithStock=true`), p. ej. la recolección. */
  onlyWithStock?: boolean
  /** Posiciones sugeridas (p. ej. acomodo): van primero, en este orden, con la marca "Sugerida". */
  suggestedBinIds?: readonly (number | null | undefined)[]
  /** Lista de opciones DADA (p. ej. las posiciones donde un producto tiene disponible): no se consulta el listado del API,
   *  se filtra en el cliente por código o zona y se ofrece en este orden (la de código exacto y las sugeridas primero),
   *  con su `hint` ("Código · Zona · 2 disp."). Un valor elegido que no está en la lista ya no aplica y se quita solo
   *  (`onChange(null, null)`), salvo mientras `optionsLoading`. Sin `options` = listado del API (comportamiento normal). */
  options?: readonly BinPickerOption[]
  /** La lista `options` se está cargando: muestra "Cargando…" y no quita el valor elegido. */
  optionsLoading?: boolean
  id?: string
  /** Texto cuando no hay posición elegida (p. ej. "Staging por defecto" si vacío = lo decide el servidor). */
  placeholder?: string
  disabled?: boolean
  invalid?: boolean
  required?: boolean
  'aria-label'?: string
  'aria-describedby'?: string
  onBlur?: () => void
}

/** Opción dada de BinPicker (prop `options`). */
export interface BinPickerOption {
  id: number
  code?: string | null
  zoneCode?: string | null
  zoneTypeCode?: string | null
  /** Texto tras "Código · Zona" (p. ej. "2 disp."). */
  hint?: string
}

type BinRow = WarehouseBinDto & { hint?: string }
const NO_BIN_ROWS: BinRow[] = []

/** Clave de consulta de BinPicker: misma forma `{ publicId, ...query }` que `useWarehouseBins` (comparte caché e invalidación). */
type BinQuery = { publicId: string | null | undefined } & GetQuery<'/api/v1/warehouses/{publicId}/bins'>

export function BinPicker({
  warehousePublicId,
  value,
  onChange,
  zoneTypeCodes,
  excludeZoneTypeCodes,
  onlyWithStock,
  suggestedBinIds,
  options: givenOptions,
  optionsLoading,
  id,
  placeholder,
  disabled,
  invalid,
  required,
  onBlur,
  ...aria
}: BinPickerProps) {
  const t = useT()
  const autoId = useId()
  const inputId = id ?? autoId
  const listId = `${inputId}-list`
  const boxRef = useRef<HTMLDivElement>(null)
  const [open, setOpen] = useState(false)
  const [text, setText] = useState('')
  const [search, setSearch] = useState('')
  const [active, setActive] = useState(0)
  const [picked, setPicked] = useState<WarehouseBinDto | null>(null)
  const dismiss = useCallback(() => setOpen(false), [])
  useDismiss(boxRef, open, dismiss)
  const noWarehouse = !warehousePublicId
  const off = Boolean(disabled) || noWarehouse
  /** Lista dada (`options`): no se consulta el listado del API. */
  const given = givenOptions !== undefined
  const givenRows = useMemo<BinRow[]>(
    () => givenOptions?.map((o) => ({ id: o.id, code: o.code, zoneCode: o.zoneCode, zoneTypeCode: o.zoneTypeCode, isActive: true, hint: o.hint })) ?? NO_BIN_ROWS,
    [givenOptions],
  )

  // búsqueda con pausa de 250 ms entre teclas
  useEffect(() => {
    const h = setTimeout(() => setSearch(text.trim()), 250)
    return () => clearTimeout(h)
  }, [text])

  // Enter que espera la búsqueda del texto escrito (lector de código de barras): cualquier cambio posterior lo anula
  const enterSeq = useRef(0)
  useEffect(
    () => () => {
      enterSeq.current++
    },
    [],
  )

  // al cambiar de almacén la posición elegida ya no aplica
  const lastWarehouse = useRef(warehousePublicId)
  useEffect(() => {
    if (lastWarehouse.current === warehousePublicId) return
    lastWarehouse.current = warehousePublicId
    enterSeq.current++
    setPicked(null)
    setText('')
    setSearch('')
    setOpen(false)
    if (value != null) onChange(null, null)
  }, [warehousePublicId, value, onChange])

  // con lista dada: una posición elegida que ya no está en ella (otro producto u otro lote) ya no aplica
  useEffect(() => {
    if (!given || optionsLoading || value == null) return
    if (givenRows.some((b) => b.id === value)) return
    enterSeq.current++
    onChange(null, null)
  }, [given, optionsLoading, value, givenRows, onChange])

  // El listado llega paginado (Lote 1): se piden a lo más MAX_SHOWN filas por búsqueda. El API no filtra por tipo de zona,
  // así que `zoneTypeCodes` se traduce a los ids de las zonas de esos tipos (filtrar solo en el cliente una página podía
  // dejar la lista vacía en un almacén grande). Si las zonas no se pueden leer, queda el filtro en el cliente de `shape`.
  // Lote 16: `excludeZoneTypeCodes` también se traduce a ids (las zonas de los demás tipos).
  const byZoneType = Boolean(zoneTypeCodes) || Boolean(excludeZoneTypeCodes?.length)
  const zoneTypeAllowed = (code: string | null | undefined) =>
    (!zoneTypeCodes || zoneTypeCodes.includes(code ?? '')) && !(excludeZoneTypeCodes ?? []).includes(code ?? '')
  const zonesQ = useWarehouseZones(warehousePublicId, { includeInactive: true }, { enabled: byZoneType && !noWarehouse && !given, handleAccessDenied: false })
  const allowedZones = byZoneType && zonesQ.data ? zonesQ.data.filter((z) => z.id != null && zoneTypeAllowed(z.zoneTypeCode)) : undefined
  // solo exclusión y ninguna zona excluida en el almacén: sin `zoneIds` (todas), para no mandar la lista completa
  const typedZoneIds =
    allowedZones && (zoneTypeCodes || allowedZones.length < (zonesQ.data?.length ?? 0)) ? allowedZones.map((z) => z.id as number) : undefined
  const zonesPending = byZoneType && !given && zonesQ.isLoading
  /** Ninguna zona del almacén es de los tipos pedidos: no hay nada que ofrecer (y `zoneIds` vacío sería "todas"). */
  const noTypedZone = typedZoneIds !== undefined && typedZoneIds.length === 0
  const baseQuery: GetQuery<'/api/v1/warehouses/{publicId}/bins'> = {
    includeInactive: false,
    onlyWithStock: onlyWithStock || undefined,
    zoneIds: typedZoneIds && typedZoneIds.length > 0 ? typedZoneIds : undefined,
    take: MAX_SHOWN,
  }
  /** Consulta de posiciones (misma clave que useWarehouseBins de api.ts: comparte caché e invalidación). */
  const binsQuery = (query: GetQuery<'/api/v1/warehouses/{publicId}/bins'>) => {
    const key: BinQuery = { publicId: warehousePublicId, ...query }
    return {
      queryKey: [warehouseKeys.bins[0], key],
      queryFn: () => unwrap(api.GET('/api/v1/warehouses/{publicId}/bins', { params: { path: { publicId: warehousePublicId ?? '' }, query } })),
      meta: { handleAccessDenied: false },
    }
  }
  const listQuery = (s: string) => binsQuery({ ...baseQuery, search: s || undefined })
  const qc = useQueryClient()
  const canList = open && !off && !zonesPending && !noTypedZone && !given
  const list = useQuery({
    ...listQuery(search),
    enabled: canList,
    // mientras llega la nueva búsqueda se ven los resultados anteriores, solo si son del mismo almacén
    placeholderData: (prev, prevQuery) => ((prevQuery?.queryKey[1] as BinQuery | undefined)?.publicId === warehousePublicId ? prev : undefined),
  })

  const suggested = useMemo(() => (suggestedBinIds ?? []).filter((b): b is number => b != null), [suggestedBinIds])
  // Las sugeridas se piden aparte por id (con el mismo texto y filtros): con la lista paginada podían no venir en la
  // primera página y dejar de ofrecerse primero.
  const suggestedList = useQuery({
    ...binsQuery({ ...baseQuery, search: search || undefined, binIds: suggested }),
    enabled: canList && suggested.length > 0,
    placeholderData: (prev, prevQuery) => ((prevQuery?.queryKey[1] as BinQuery | undefined)?.publicId === warehousePublicId ? prev : undefined),
  })
  /** Filas que se ofrecen: activas, del tipo de zona pedido, ordenadas (exacta, sugeridas, resto) y recortadas. */
  const shape = (rows: readonly BinRow[], q: string) =>
    orderBins(
      rows.filter((b) => b.isActive !== false && zoneTypeAllowed(b.zoneTypeCode)),
      q,
      suggested,
    ).slice(0, MAX_SHOWN)
  const rows = useMemo<BinRow[]>(() => {
    if (given) return givenRows
    const extra = suggested.length > 0 ? (suggestedList.data?.items ?? []) : []
    const seen = new Set(extra.map((b) => b.id))
    return [...extra, ...(list.data?.items ?? []).filter((b) => !seen.has(b.id))]
  }, [given, givenRows, suggested, suggestedList.data, list.data])
  // lista dada: se filtra en el cliente con el texto tal cual (sin pausa)
  const options = given ? shape(filterBinOptions(rows, text), text) : noTypedZone ? [] : shape(rows, text)
  // los resultados corresponden al texto escrito (no son los de la búsqueda anterior)
  const fresh = given || (search === text.trim() && list.isSuccess && !list.isPlaceholderData)

  // valor que llega de fuera (formulario de edición, posición dada de baja): se pide esa posición por id (`binIds`), con
  // inactivas, para mostrar "Código · Zona" (el API no tiene ficha de una sola posición)
  const known = picked && picked.id === value ? picked : value != null ? rows.find((b) => b.id === value) : undefined
  const lookup = useQuery({
    ...binsQuery({ binIds: value != null ? [value] : undefined, includeInactive: true, take: 1 }),
    // con lista dada solo mientras carga (luego un valor fuera de ella se quita)
    enabled: value != null && !known && !noWarehouse && (!given || Boolean(optionsLoading)),
  })
  const found = known ?? (value != null ? lookup.data?.items?.find((b) => b.id === value) : undefined)
  let selectedLabel = ''
  if (value != null) selectedLabel = found ? binLabel(found) : lookup.isLoading ? t('common.loading') : `#${value}`

  const choose = (b: WarehouseBinDto) => {
    enterSeq.current++
    setPicked(b)
    setOpen(false)
    setText('')
    onChange(b.id ?? null, b)
  }

  const clear = () => {
    setPicked(null)
    setText('')
    onChange(null, null)
  }

  const openList = () => {
    enterSeq.current++
    setText('')
    setActive(0)
    setOpen(true)
  }

  /** Enter antes de la pausa de 250 ms (el lector teclea y manda Enter de inmediato): se busca ya ese texto y, al llegar la
   *  respuesta, se elige el código exacto (o el único resultado). Si mientras tanto cambió algo, no se elige nada. */
  const enterNow = (q: string) => {
    setSearch(q)
    const seq = ++enterSeq.current
    if (noTypedZone) return
    qc.fetchQuery(listQuery(q))
      .then((page) => {
        if (seq !== enterSeq.current) return
        const opts = shape(page.items ?? [], q)
        const b = exactCodeMatch(opts, q) ?? (opts.length === 1 ? opts[0] : undefined)
        if (b) choose(b)
      })
      .catch(() => undefined) // el aviso (sin acceso, error) lo pinta la lista
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
      const q = text.trim()
      if (q && !fresh) {
        enterNow(q)
        return
      }
      const b = exactCodeMatch(options, q) ?? options[active]
      if (b) choose(b)
    } else if (e.key === 'Escape' && open) {
      e.stopPropagation()
      setOpen(false)
    } else if (e.key === 'Tab' && open) {
      setOpen(false)
    }
  }

  let status: string | null = null
  if (given ? optionsLoading : list.isLoading || zonesPending) status = t('common.loading')
  else if (given) status = options.length === 0 ? t('ui.binPicker.none') : null
  else if (isAccessDenied(list.error)) status = t('ui.binPicker.noAccess')
  else if (list.error) status = t('errors.generic')
  else if (options.length === 0) status = t('ui.binPicker.none')

  const suggestedSet = new Set(suggested)

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
          disabled={off}
          placeholder={noWarehouse ? t('ui.binPicker.pickWarehouseFirst') : (placeholder ?? t('ui.binPicker.placeholder'))}
          value={open ? text : selectedLabel}
          onFocus={openList}
          // tras elegir, el foco se queda en el campo: un clic vuelve a abrir la lista
          onClick={() => {
            if (!open) openList()
          }}
          onChange={(e) => {
            setText(e.target.value)
            setActive(0)
            enterSeq.current++
            setOpen(true)
          }}
          onKeyDown={onKeyDown}
          onBlur={onBlur}
        />
        {value != null && !off && (
          <button type="button" className="iconbtn" aria-label={t('ui.binPicker.clear')} onClick={clear}>
            <IconClose />
          </button>
        )}
      </div>
      {open && (
        <div className="mp">
          <div className="milist" id={listId} role="listbox">
            {status && <div className="mnone">{status}</div>}
            {!status &&
              options.map((b, i) => (
                <div
                  key={b.id}
                  id={`${listId}-${i}`}
                  role="option"
                  aria-selected={b.id === value}
                  className={i === active ? 'mi on' : 'mi'}
                  onMouseDown={(e) => e.preventDefault()}
                  onMouseEnter={() => setActive(i)}
                  onClick={() => choose(b)}
                >
                  <span>
                    <span className="code">{b.code}</span>
                    {b.zoneCode ? ` · ${b.zoneCode}` : ''}
                    {b.hint ? ` · ${b.hint}` : ''}
                  </span>
                  {b.id != null && suggestedSet.has(b.id) && <Chip tone="route">{t('ui.binPicker.suggested')}</Chip>}
                </div>
              ))}
          </div>
        </div>
      )}
    </div>
  )
}

export interface BinPickerInputProps {
  /** publicId del almacén (normalmente otro campo del formulario, leído con `useWatch`). */
  warehousePublicId: string | null | undefined
  zoneTypeCodes?: readonly string[]
  excludeZoneTypeCodes?: readonly string[]
  onlyWithStock?: boolean
  suggestedBinIds?: readonly (number | null | undefined)[]
  /** Lista de opciones dada (ver `BinPickerProps.options`). */
  options?: readonly BinPickerOption[]
  optionsLoading?: boolean
  placeholder?: string
  disabled?: boolean
  /** Aviso con la fila elegida (null al quitar). */
  onPicked?: (bin: WarehouseBinDto | null) => void
}

/** BinPicker dentro de un <Field name="binId">: el valor del formulario es el id como texto ('' = ninguna), igual que un
 *  `Select` (el request lo convierte con `Number(v.binId)`); también lee un valor numérico. */
export function BinPickerInput({ onPicked, ...props }: BinPickerInputProps) {
  const info = useFieldInfo('BinPickerInput')
  const { control } = useFormContext()
  const { field } = useController({ name: info.name, control })
  const raw = field.value as string | number | null | undefined
  const value = raw === '' || raw == null || Number.isNaN(Number(raw)) ? null : Number(raw)
  return (
    <BinPicker
      {...props}
      id={info.id}
      value={value}
      onChange={(binId, bin) => {
        field.onChange(binId == null ? '' : String(binId))
        onPicked?.(bin)
      }}
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
  /** Solo productos con existencia en mano &gt; 0 (en el almacén indicado si hay; incluye lo reservado y la cuarentena): es lo que cuenta un conteo. */
  onlyOnHand?: boolean
  /** Incluye productos dados de baja (marcados "Inactivo"), p. ej. para filtrar el historial del Kárdex. */
  includeInactive?: boolean
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
  onlyOnHand,
  includeInactive,
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
    activeOnly: !includeInactive,
    ownOnly: ownOnly || undefined,
    ownerClientPublicId: ownerClientPublicId || undefined,
    warehousePublicId: warehousePublicId || undefined,
    onlyAvailable: onlyAvailable || undefined,
    onlyOnHand: onlyOnHand || undefined,
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
                  {p.isActive === false && <Chip tone="fail">{t('ui.productPicker.inactive')}</Chip>}
                </div>
              ))}
          </div>
        </div>
      )}
    </div>
  )
}

// =====================================================================================================================
// ProductMultiFilter: filtro de varios productos que busca en el API por SKU o nombre (no solo sobre una página cargada)
// =====================================================================================================================

/** Producto elegido en el filtro (se guarda la etiqueta para no volver a pedirla). */
export interface ProductFilterItem {
  publicId: string
  sku: string
  label: string
}

export interface ProductMultiFilterProps {
  label: string
  value: ProductFilterItem[]
  onChange: (value: ProductFilterItem[]) => void
  /** Deja elegir productos dados de baja (Kárdex: su historial sigue en el ledger). */
  includeInactive?: boolean
}

/**
 * Filtro "Producto" de listas (Saldos, Kárdex): ProductPicker para agregar (busca en `GET /api/v1/products?search=`) y
 * una píldora por producto elegido con botón para quitarlo. Vacío = todos.
 */
export function ProductMultiFilter({ label, value, onChange, includeInactive }: ProductMultiFilterProps) {
  const t = useT()
  const id = useId()
  const ref = useRef<HTMLDivElement>(null)
  // en el ámbito de filtros: los SKU elegidos (como sus píldoras)
  useRegisterFilter(label, joinFilterValues(value.map((v) => v.sku || v.label), t), ref)
  return (
    <div className="f" ref={ref}>
      <label htmlFor={id}>{label}</label>
      <ProductPicker
        id={id}
        value={null}
        includeInactive={includeInactive}
        placeholder={t('warehouse.productFilter.placeholder')}
        onChange={(publicId, product) => {
          if (!publicId || value.some((v) => v.publicId === publicId)) return
          onChange([...value, { publicId, sku: product?.sku ?? '', label: productLabel(product) }])
        }}
      />
      {value.length > 0 && (
        <div className="pfilter-chips">
          {value.map((v) => (
            <Chip key={v.publicId} title={v.label}>
              {/* un SKU admite 60 caracteres sin espacios: se recorta con elipsis para no salirse a 360 px */}
              <span className="pfilter-text">{v.sku || v.label}</span>
              <button
                type="button"
                className="iconbtn"
                aria-label={t('warehouse.productFilter.remove', { sku: v.sku || v.label })}
                onClick={() => onChange(value.filter((x) => x.publicId !== v.publicId))}
              >
                <IconClose />
              </button>
            </Chip>
          ))}
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
  onlyOnHand?: boolean
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

// =====================================================================================================================
// Lote 14 — BinMultiFilter: filtro "Posición" de listas (Kárdex, Saldos, Conciliación, Conteo) que busca ENTRE almacenes
// =====================================================================================================================

export interface BinMultiFilterProps {
  label: string
  value: BinFilterItem[]
  onChange: (value: BinFilterItem[]) => void
  /** Acota la búsqueda a esos almacenes (vacío = todos). */
  warehousePublicIds?: readonly string[]
  /** Deja elegir posiciones dadas de baja (historial del Kárdex). */
  includeInactive?: boolean
}

/**
 * Combobox con buscador sobre `GET /api/v1/warehouses/bins/search?search=&warehousePublicIds=&take=50` (250 ms entre
 * teclas; opciones "Código · Zona · Almacén"); cada posición elegida queda como píldora con ✕. Enter con el código exacto la
 * elige (lector de código de barras; si la búsqueda aún no llega, se elige al llegar). Vacío = todas. 403: aviso sin sacar
 * de la pantalla.
 */
export function BinMultiFilter({ label, value, onChange, warehousePublicIds, includeInactive }: BinMultiFilterProps) {
  const t = useT()
  const inputId = useId()
  const listId = `${inputId}-list`
  const boxRef = useRef<HTMLDivElement>(null)
  const [open, setOpen] = useState(false)
  const [text, setText] = useState('')
  const [search, setSearch] = useState('')
  const [active, setActive] = useState(0)
  const qc = useQueryClient()
  const enterSeq = useRef(0)
  const dismiss = useCallback(() => setOpen(false), [])
  useDismiss(boxRef, open, dismiss)
  // en el ámbito de filtros: las posiciones elegidas (como sus píldoras)
  useRegisterFilter(label, joinFilterValues(value.map((v) => v.label), t), boxRef)

  useEffect(() => {
    const h = setTimeout(() => setSearch(text.trim()), 250)
    return () => clearTimeout(h)
  }, [text])

  const searchQuery = (q: string) => {
    const query: GetQuery<'/api/v1/warehouses/bins/search'> = {
      search: q || undefined,
      warehousePublicIds: warehousePublicIds && warehousePublicIds.length > 0 ? [...warehousePublicIds] : undefined,
      includeInactive: includeInactive || undefined,
      take: MAX_SHOWN,
    }
    return {
      queryKey: [warehouseKeys.binSearch[0], query] as const,
      queryFn: () => unwrap(api.GET('/api/v1/warehouses/bins/search', { params: { query } })),
      meta: { handleAccessDenied: false },
    }
  }
  const list = useQuery({ ...searchQuery(search), enabled: open, placeholderData: keepPreviousData })
  const chosen = useMemo(() => new Set(value.map((v) => v.id)), [value])
  const options = useMemo(() => {
    const items = (list.data ?? NO_BINS).filter((b) => b.id != null && !chosen.has(b.id))
    const exact = exactCodeMatch(items, text)
    return exact ? [exact, ...items.filter((b) => b !== exact)] : items
  }, [list.data, chosen, text])

  const choose = (b: BinSearchItemDto) => {
    enterSeq.current++
    setText('')
    setActive(0)
    if (b.id == null || value.some((v) => v.id === b.id)) return
    onChange([...value, { id: b.id, label: binFilterLabel(b) }])
  }

  /** Enter del lector antes de la pausa de 250 ms: se busca ya ese texto y, al llegar, se elige el código exacto (o el único). */
  const enterNow = (q: string) => {
    setSearch(q)
    const seq = ++enterSeq.current
    qc.fetchQuery(searchQuery(q))
      .then((items) => {
        if (seq !== enterSeq.current) return
        const rows = items.filter((b) => b.id != null && !chosen.has(b.id))
        const b = exactCodeMatch(rows, q) ?? (rows.length === 1 ? rows[0] : undefined)
        if (b) choose(b)
      })
      .catch(() => undefined) // el aviso (sin acceso, error) lo pinta la lista
  }

  const onKeyDown = (e: KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'ArrowDown') {
      e.preventDefault()
      if (!open) setOpen(true)
      else setActive((i) => Math.min(i + 1, options.length - 1))
    } else if (e.key === 'ArrowUp') {
      e.preventDefault()
      setActive((i) => Math.max(i - 1, 0))
    } else if (e.key === 'Enter') {
      e.preventDefault()
      const typed = text.trim()
      if (typed && (typed !== search || list.isPlaceholderData || !list.isSuccess)) {
        enterNow(typed)
        return
      }
      const b = exactCodeMatch(options, text) ?? (open ? options[active] : undefined)
      if (b) choose(b)
    } else if (e.key === 'Escape' && open) {
      e.stopPropagation()
      setOpen(false)
    }
  }

  let status: string | null = null
  if (list.isLoading) status = t('common.loading')
  else if (isAccessDenied(list.error)) status = t('warehouse.binFilter.noAccess')
  else if (list.error) status = t('errors.generic')
  else if (options.length === 0) status = t('warehouse.binFilter.none')

  return (
    <div className="f">
      <label htmlFor={inputId}>{label}</label>
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
            placeholder={t('warehouse.binFilter.placeholder')}
            value={text}
            onFocus={() => {
              setActive(0)
              setOpen(true)
            }}
            onChange={(e) => {
              setText(e.target.value)
              setActive(0)
              setOpen(true)
            }}
            onKeyDown={onKeyDown}
          />
        </div>
        {open && (
          <div className="mp">
            <div className="milist" id={listId} role="listbox" aria-label={label}>
              {status && <div className="mnone">{status}</div>}
              {!status &&
                options.map((b, i) => (
                  <div
                    key={b.id}
                    id={`${listId}-${i}`}
                    role="option"
                    aria-selected={false}
                    className={i === active ? 'mi on' : 'mi'}
                    onMouseDown={(e) => e.preventDefault()}
                    onMouseEnter={() => setActive(i)}
                    onClick={() => choose(b)}
                  >
                    <span>
                      <span className="code">{b.code}</span>
                      {b.zoneCode ? ` · ${b.zoneCode}` : ''} · {b.warehouseCode}
                    </span>
                    {b.isActive === false && <Chip tone="fail">{t('warehouse.binFilter.inactive')}</Chip>}
                  </div>
                ))}
            </div>
          </div>
        )}
      </div>
      {value.length > 0 && (
        <div className="pfilter-chips">
          {value.map((v) => (
            <Chip key={v.id} title={v.label}>
              <span className="pfilter-text">{v.label}</span>
              <button
                type="button"
                className="iconbtn"
                aria-label={t('warehouse.binFilter.remove', { bin: v.label })}
                onClick={() => onChange(value.filter((x) => x.id !== v.id))}
              >
                <IconClose />
              </button>
            </Chip>
          ))}
        </div>
      )}
    </div>
  )
}

// =====================================================================================================================
// Lote 14 — OwnerFilter: filtro "Dueño" (Propio y clientes dueños de inventario, `GET /api/v1/inventory/owners`)
// =====================================================================================================================

export interface OwnerFilterProps {
  label: string
  /** `OWN_OWNER` ('OWN') = Propio; el resto, clientPublicId. Vacío = todos. */
  value: string[]
  onChange: (value: string[]) => void
}

/** `SearchSelect` sobre `useInventoryOwners` ("Propio" primero). La consulta la arma `ownerQuery` de `kardexView.ts`. */
export function OwnerFilter({ label, value, onChange }: OwnerFilterProps) {
  const { data = NO_OWNERS } = useInventoryOwners({ handleAccessDenied: false })
  const options = useMemo(
    () => data.map((o) => ({ value: o.isOwn ? OWN_OWNER : (o.clientPublicId ?? ''), label: o.name ?? '' })).filter((o) => o.value),
    [data],
  )
  return <SearchSelect label={label} options={options} value={value} onChange={onChange} />
}
