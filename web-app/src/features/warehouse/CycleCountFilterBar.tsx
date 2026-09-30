// Lote 14 (P8) — filtros de 'Conteo cíclico' (Cambios.pdf p. 15: almacén, zona, posición, fecha de creación desde/hasta,
// producto y estatus, todos desplegables con buscador; más el origen del conteo y un buscador de texto). Van arriba de los
// dos paneles y TODOS al API (`countFilterQuery` de countView.ts): Almacén (`SearchSelect`), Zona (zonas activas de los
// almacenes elegidos —o de todos—, "Código · Almacén"), Posición (`BinMultiFilter`, entre almacenes), Producto
// (`ProductMultiFilter` con inactivos: es historial), Estatus (Pendiente/Contado/Concordancia/Diferencia del catálogo),
// Origen (Selección / Lo cambiado), Creado (días locales de la compañía) y Buscar (número, SKU o producto; con pausa).
import { useQueries } from '@tanstack/react-query'
import { useEffect, useMemo } from 'react'
import { api, unwrap } from '../../kernel/api/client'
import { useLookups, useStatuses } from '../../kernel/catalogs'
import { useT } from '../../kernel/i18n'
import { DateRangeFilter, Filters, SearchSelect } from '../../kernel/ui'
import { useWarehouses, warehouseKeys, warehouseLabel, type WarehouseZoneDto } from './api'
import { COUNT_ORIGIN_DOMAIN, COUNT_STATUS_DOMAIN, EMPTY_COUNT_FILTERS, keepZones, type CountFilterState } from './countView'
import { TextFilter } from './filterControls'
import { BinMultiFilter, ProductMultiFilter } from './pickers'

type ZoneResult = { data?: WarehouseZoneDto[]; isLoading: boolean }

/** Resultado combinado de las zonas (función estable: TanStack la vuelve a correr solo si cambian los resultados). */
function combineZones(results: ZoneResult[]) {
  return { data: results.map((r) => r.data), loading: results.some((r) => r.isLoading) }
}

export interface CycleCountFilterBarProps {
  value: CountFilterState
  onChange: (next: CountFilterState) => void
  /** Texto del buscador tal como se teclea (la pantalla lo aplica con pausa en `value.search`). */
  q: string
  onQ: (q: string) => void
}

export function CycleCountFilterBar({ value, onChange, q, onQ }: CycleCountFilterBarProps) {
  const t = useT()
  const { data: warehouses = [] } = useWarehouses({ includeInactive: false }, { handleAccessDenied: false })
  const { data: statuses = [] } = useStatuses(COUNT_STATUS_DOMAIN)
  const { data: origins = [] } = useLookups(COUNT_ORIGIN_DOMAIN)

  const warehouseOptions = useMemo(() => warehouses.map((w) => ({ value: w.publicId ?? '', label: warehouseLabel(w) })), [warehouses])
  const statusOptions = useMemo(() => statuses.map((s) => ({ value: s.code, label: s.label })), [statuses])
  const originOptions = useMemo(() => origins.map((o) => ({ value: o.code, label: o.label })), [origins])

  // zonas de los almacenes elegidos (sin elección: de todos los activos); misma clave que useWarehouseZones (caché compartida)
  const zoneSources = useMemo(
    () => (value.warehousePublicIds.length > 0 ? warehouses.filter((w) => value.warehousePublicIds.includes(w.publicId ?? '')) : warehouses),
    [warehouses, value.warehousePublicIds],
  )
  const zones = useQueries({
    queries: zoneSources.map((w) => ({
      queryKey: [warehouseKeys.zones[0], { publicId: w.publicId }],
      queryFn: () => unwrap(api.GET('/api/v1/warehouses/{publicId}/zones', { params: { path: { publicId: w.publicId ?? '' } } })),
      meta: { handleAccessDenied: false },
    })),
    combine: combineZones,
  })
  const zoneOptions = useMemo(
    () =>
      zoneSources.flatMap((w, i) =>
        (zones.data[i] ?? [])
          .filter((z) => z.isActive !== false)
          .map((z) => ({ value: String(z.id), label: [z.code, w.code].filter(Boolean).join(' · ') })),
      ),
    [zoneSources, zones.data],
  )

  // una zona de un almacén que ya no está elegido se quita del filtro (cuando las zonas ya llegaron)
  const zonesLoaded = !zones.loading
  useEffect(() => {
    if (!zonesLoaded || value.zoneIds.length === 0) return
    const kept = keepZones(value.zoneIds, zoneOptions.map((o) => o.value))
    if (kept.length !== value.zoneIds.length) onChange({ ...value, zoneIds: kept })
  }, [zonesLoaded, zoneOptions, value, onChange])

  const set = <K extends keyof CountFilterState>(key: K, v: CountFilterState[K]) => onChange({ ...value, [key]: v })

  return (
    <Filters
      label={t('warehouse.cycleCounts.filters.aria')}
      onClear={() => {
        onQ('')
        onChange(EMPTY_COUNT_FILTERS)
      }}
    >
      <SearchSelect label={t('warehouse.cycleCounts.filters.warehouses')} options={warehouseOptions} value={value.warehousePublicIds} onChange={(v) => set('warehousePublicIds', v)} />
      <SearchSelect label={t('warehouse.cycleCounts.filters.zones')} options={zoneOptions} value={value.zoneIds} onChange={(v) => set('zoneIds', v)} />
      <BinMultiFilter
        label={t('warehouse.cycleCounts.filters.bins')}
        value={value.bins}
        onChange={(v) => set('bins', v)}
        warehousePublicIds={value.warehousePublicIds}
        includeInactive
      />
      <ProductMultiFilter label={t('warehouse.cycleCounts.filters.product')} value={value.products} onChange={(v) => set('products', v)} includeInactive />
      <SearchSelect label={t('warehouse.cycleCounts.filters.status')} options={statusOptions} value={value.status} onChange={(v) => set('status', v)} />
      <SearchSelect label={t('warehouse.cycleCounts.filters.origin')} options={originOptions} value={value.origins} onChange={(v) => set('origins', v)} />
      <DateRangeFilter label={t('warehouse.cycleCounts.filters.created')} value={value.created} onChange={(v) => set('created', v)} />
      <TextFilter label={t('warehouse.cycleCounts.filters.search')} value={q} onChange={onQ} placeholder={t('warehouse.cycleCounts.searchPlaceholder')} />
    </Filters>
  )
}
