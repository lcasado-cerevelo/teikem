// Filtro «Cliente» de varias opciones (2026-11-09, filtros de Órdenes de venta): el mismo desplegable con búsqueda y casillas que los demás
// filtros (`SearchSelect` en modo remoto), con la búsqueda en el servidor: GET /api/v1/clients?search=&includeInactive= (permiso clients.read),
// 250 ms entre teclas y solo con el desplegable abierto. Opciones «Código · Nombre»; lo elegido se queda arriba con su etiqueta.
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { useEffect, useMemo, useState } from 'react'
import { api, unwrap } from '../api/client'
import type { components } from '../api/schema'
import { SearchSelect } from './SearchSelect'

type ClientRow = components['schemas']['ClientListItemDto']

/** Cliente elegido en el filtro (se guarda la etiqueta para no volver a pedirla). */
export interface ClientFilterItem {
  publicId: string
  label: string
}

export interface ClientSearchSelectProps {
  label: string
  /** Clientes elegidos; vacío = todos. */
  value: readonly ClientFilterItem[]
  onChange: (value: ClientFilterItem[]) => void
  /** Deja elegir clientes dados de baja (listas de historial: sus documentos siguen ahí). */
  includeInactive?: boolean
  /** Texto del botón sin nada elegido (por defecto «Todos»). */
  placeholder?: string
  disabled?: boolean
}

const NO_CLIENTS: ClientRow[] = []
/** Tope de opciones mostradas: si el servidor trae más, se avisa que acote la búsqueda. */
const FILTER_TAKE = 200

/** «Código · Nombre» (o lo que haya). */
function clientFilterLabel(c: { code?: string | null; name?: string | null } | null | undefined): string {
  if (!c) return ''
  return [c.code, c.name].filter(Boolean).join(' · ')
}

export function ClientSearchSelect({ label, value, onChange, includeInactive, placeholder, disabled }: ClientSearchSelectProps) {
  const [open, setOpen] = useState(false)
  const [text, setText] = useState('')
  const [search, setSearch] = useState('')
  useEffect(() => {
    const h = setTimeout(() => setSearch(text.trim()), 250)
    return () => clearTimeout(h)
  }, [text])
  const query = { search: search || undefined, includeInactive: includeInactive || undefined }
  const list = useQuery({
    queryKey: ['/api/v1/clients', query],
    queryFn: () => unwrap(api.GET('/api/v1/clients', { params: { query } })),
    enabled: open && !disabled, // solo consulta con el desplegable abierto
    placeholderData: keepPreviousData,
    meta: { handleAccessDenied: false },
  })
  const found = list.data ?? NO_CLIENTS
  const options = useMemo(
    () => found.filter((c) => c.publicId).slice(0, FILTER_TAKE).map((c) => ({ value: c.publicId!, label: clientFilterLabel(c) })),
    [found],
  )
  const chosen = useMemo(() => value.map((v) => ({ value: v.publicId, label: v.label })), [value])
  return (
    <SearchSelect
      label={label}
      options={options}
      chosen={chosen}
      value={value.map((v) => v.publicId)}
      onOpenChange={setOpen}
      placeholder={placeholder}
      disabled={disabled}
      loading={list.isFetching}
      capped={found.length > FILTER_TAKE}
      onSearch={setText}
      onChange={(ids) =>
        onChange(
          ids.map((id) => {
            const known = value.find((v) => v.publicId === id)
            if (known) return known
            const c = found.find((x) => x.publicId === id)
            return { publicId: id, label: c ? clientFilterLabel(c) : id }
          }),
        )
      }
    />
  )
}
