import { api } from '@config/axios';
import type { CodType, Company, Driver, Manifest, Piece, Reason, Shipment, Status } from '@/types/models';

// ---------------------------------------------------------------------------
// colavora API endpoints.
// The backend forces CompanyID (and DriverID for non-admins) from the token.
// List endpoints return arrays of IDs; details are fetched per ID.
// ---------------------------------------------------------------------------

// ---- Consignee (delivery recipient; holds the stored geo of the address) ----
export interface Consignee {
  latitude: string | null;
  longitude: string | null;
}
export async function fetchConsignee(id: number): Promise<Consignee> {
  const { data } = await api.get<Consignee>(`common/consignee/${id}`);
  return data;
}

// ---- Current user (resolved from the token) ----
export interface Me {
  userID: number;
  companyID: string;
  username: string;
  firstName?: string;
  lastName?: string;
  driverName: string;
}
export async function fetchMe(): Promise<Me> {
  const { data } = await api.get<Me>('common/me');
  return data;
}

// ---- Senders del usuario (UsersSender) ----
// Lista VACÍA = el usuario es chofer (no está en UsersSender) => la app no
// muestra el panel de filtros y se comporta como siempre.
export interface UserSender {
  senderID: number;
  sender: string | null;
  senderName: string | null;
}
export async function fetchMySenders(): Promise<UserSender[]> {
  const { data } = await api.get<UserSender[]>('common/sender');
  return data ?? [];
}

// ---- Búsqueda con alcance por sender (panel de filtros) ----
// A diferencia del resto del app, esto NO es offline-first: los senders de un
// usuario suman decenas de miles de órdenes, así que la búsqueda se resuelve y
// pagina en el servidor. Requiere señal.
export interface ShipmentSearchParams {
  senderIds: number[];        // obligatorio; el server intersecta con lo autorizado
  dateFrom?: string;          // yyyy-MM-dd, sobre ReadyDate
  dateTo?: string;            // yyyy-MM-dd, inclusivo
  city?: string;
  search?: string;
  offset?: number;
  pageSize?: number;
}
export interface ShipmentSearchResult {
  items: Shipment[];
  total: number;
  offset: number;
  pageSize: number;
}
export async function searchShipments(p: ShipmentSearchParams): Promise<ShipmentSearchResult> {
  const { data } = await api.get<ShipmentSearchResult>('shipment/search', {
    params: {
      senderIds: p.senderIds.join(','),
      dateFrom: p.dateFrom || undefined,
      dateTo: p.dateTo || undefined,
      city: p.city || undefined,
      search: p.search || undefined,
      offset: p.offset ?? 0,
      pageSize: p.pageSize ?? 50,
    },
  });
  return data;
}

// ---- Driver ----
export async function fetchDriver(id: number | string): Promise<Driver> {
  const { data } = await api.get<Driver>(`common/driver/${id}`);
  return data;
}

// ---- Status ----
export async function fetchStatusIds(): Promise<number[]> {
  const { data } = await api.get<number[]>('common/status');
  return data;
}
export async function fetchStatusById(id: number): Promise<Status> {
  const { data } = await api.get<Status>(`common/status/${id}`);
  return data;
}

// ---- Company ----
export async function fetchCompany(id: number | string): Promise<Company> {
  const { data } = await api.get<Company>(`company/${id}`);
  return data;
}

// ---- Manifests ----
export interface ManifestQuery {
  createdDate?: string; // omitido => el API/SP no filtra por fecha (todas las abiertas)
  driverId: number;
  status: string;
}
export async function fetchManifestIds(params: ManifestQuery): Promise<string[]> {
  // Conteo de manifiestos ABIERTOS. El API devuelve 404 si no hay; lista vacía.
  try {
    const { data } = await api.get<string[]>('shipment/manifest', { params });
    return data ?? [];
  } catch (e) {
    if ((e as import('axios').AxiosError).response?.status === 404) return [];
    throw e;
  }
}
export async function fetchManifestById(id: number | string): Promise<Manifest> {
  const { data } = await api.get<Manifest>(`shipment/manifest/${id}`);
  return data;
}

// ---- Shipments ----
// Envíos ABIERTOS de un manifiesto (el SP _New ya no se traba bajo concurrencia).
export async function fetchShipmentIds(manifest: string): Promise<number[]> {
  const { data } = await api.get<number[]>('shipment', { params: { manifest } });
  return data;
}

// Todos los envíos ABIERTOS del chofer (todas las entregas de todos los manifiestos).
export async function fetchShipmentIdsByDriver(driverId: number): Promise<number[]> {
  const { data } = await api.get<number[]>('shipment', { params: { driverId } });
  return data;
}

// La MISMA lista, pero con las filas COMPLETAS en una sola llamada (pedido de
// Luis: el SP devuelve todo y la app lo separa en las secciones donde va).
// Sustituye el patrón "IDs + un GET por orden" — 55 llamadas para un chofer,
// ~1,600 para un usuario de oficina — por UNA. Requiere el API con el endpoint
// shipment/list Y el SP full-rows aplicado; los llamadores hacen fallback al
// camino viejo si no están.
export async function fetchShipmentsFullByDriver(driverId: number): Promise<Shipment[]> {
  const { data } = await api.get<Shipment[]>('shipment/list', {
    params: { driverId },
    // Una respuesta de ~1,600 órdenes pesa varios MB: el timeout global de 20s
    // puede quedarse corto en datos móviles.
    timeout: 120_000,
  });
  return data ?? [];
}
export async function fetchShipmentById(id: number): Promise<Shipment> {
  const { data } = await api.get<Shipment>(`shipment/${id}`);
  return data;
}

// ---- Pieces ----
// El API devuelve 404 cuando un envío no tiene piezas; lo tratamos como lista vacía.
export async function fetchPieceIds(shipmentId: number): Promise<number[]> {
  try {
    const { data } = await api.get<number[]>('shipment/piece', { params: { shipmentId } });
    return data ?? [];
  } catch (e) {
    if ((e as import('axios').AxiosError).response?.status === 404) return [];
    throw e;
  }
}
export async function fetchPieceById(id: number): Promise<Piece> {
  const { data } = await api.get<Piece>(`shipment/piece/${id}`);
  return data;
}

// ---- Comments ----
// 404 (sin comentarios) -> lista vacía, sin error.
export async function fetchComments(shipmentId: number): Promise<string[]> {
  try {
    const { data } = await api.get<string[]>('shipment/comment', { params: { shipmentId } });
    return data ?? [];
  } catch (e) {
    if ((e as import('axios').AxiosError).response?.status === 404) return [];
    throw e;
  }
}

// ---- Reasons (non-delivery / exception catalog) ----
// El API devuelve 404 cuando no hay motivos; lista vacía.
export async function fetchReasonIds(): Promise<number[]> {
  try {
    const { data } = await api.get<number[]>('common/reason');
    return data ?? [];
  } catch (e) {
    if ((e as import('axios').AxiosError).response?.status === 404) return [];
    throw e;
  }
}
export async function fetchReasonById(id: number, language = 'ES'): Promise<Reason> {
  const { data } = await api.get<Reason>(`common/reason/${id}`, { params: { language } });
  return data;
}

// ---- COD types (Cash-On-Delivery catalog) ----
export async function fetchCodTypeIds(): Promise<number[]> {
  try {
    const { data } = await api.get<number[]>('common/cod');
    return data ?? [];
  } catch (e) {
    if ((e as import('axios').AxiosError).response?.status === 404) return [];
    throw e;
  }
}
export async function fetchCodTypeById(id: number, language = 'ES'): Promise<CodType> {
  const { data } = await api.get<CodType>(`common/cod/${id}`, { params: { language } });
  return data;
}
