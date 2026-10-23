// Centro de Puerto Rico — fallback when an address can't be geocoded.
export const defaultLocation = { lat: 18.220833, lng: -66.590149 };

// Username (email) validation.
export const EmailRegex =
  /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

// Status filters used by the manifest query. Server normalizes quotes, so we
// pass a plain comma-separated list (no hardcoded literal string scattered around).
export const labelStatuses = {
  manifest: ['Our for Delivery', 'Assigned', 'Created'],
  pickup: ['PickedUp'],
};

// Comma-separated status string for the manifest endpoint.
export const manifestStatusParam = labelStatuses.manifest.join(',');

// Centralized react-query keys.
export const queryKeys = {
  driver: (id: number | string) => ['driver', id] as const,
  company: (id: number | string) => ['company', id] as const,
  statusIds: () => ['status', 'ids'] as const,
  status: (id: number) => ['status', id] as const,
  manifestIds: (driverId: number, createdDate: string | undefined, status: string) =>
    ['manifests', 'ids', 'open', driverId, createdDate ?? 'all', status] as const,
  manifest: (id: number | string) => ['manifest', id] as const,
  shipmentIds: (manifest: string) => ['shipments', 'ids', manifest] as const,
  pieceIds: (shipmentId: number) => ['pieces', 'ids', shipmentId] as const,
  piece: (id: number) => ['piece', id] as const,
};

// (Se retiró ORDERS_PAGE_SIZE = 20.) OrdersList cortaba el listado a las
// primeras 20 órdenes "matching original behavior", lo que además de esconderle
// entregas a un chofer con más de 20, hacía que el buscador solo encontrara
// dentro de ese primer tramo. Ahora carga todas: el detalle de cada orden ya
// viene en la caché persistida desde la sincronización, así que no cuesta red,
// y la FlatList virtualiza el render.

// --- Phase 3: manifest date ---------------------------------------------------
// The old app hardcoded "2024-05-20" so dev data would show. We use TODAY by
// default; set an override here if your dev DB only has data for a specific day.
export const MANIFEST_DATE_OVERRIDE: string | null = null;
export function manifestDate(): string {
  return MANIFEST_DATE_OVERRIDE ?? new Date().toLocaleDateString();
}
