import { api } from '@config/axios';
import type { Company } from '@services/driverApi';
import type { RouteStopRow } from '@utils/streetRules';

// Lote 215 — rutas de hoy (los endpoints de la web: GET /trips y GET /trips/{id}, con trips.view) y asignar en la calle (/mobile/street/*, con trips.streetassign).
// Todo en vivo: el personal no trabaja sin señal.

export interface TripRow {
  publicId: string;
  code: string;
  kind: string;
  statusCode: string;
  status: string;
  driverName: string | null;
  vehicleCode: string | null;
  stopCount: number;
  plannedStartUtc: string | null;
}

export async function fetchTodayTrips(c: Company): Promise<TripRow[]> {
  const { data } = await api.get<TripRow[]>('trips', { tenantId: c.tenantId });
  return data
    .filter((t) => t.statusCode !== 'CANCELLED')
    .map((t) => ({ publicId: t.publicId, code: t.code, kind: t.kind, statusCode: t.statusCode, status: t.status, driverName: t.driverName ?? null, vehicleCode: t.vehicleCode ?? null, stopCount: t.stopCount, plannedStartUtc: t.plannedStartUtc ?? null }));
}

export interface TripDetail extends TripRow {
  stops: RouteStopRow[];
  lastPing: { lat: number; lng: number; capturedAtUtc: string } | null;
  totalDistanceKm: number | null;
}

interface TripDetailDto extends Omit<TripRow, 'driverName' | 'vehicleCode' | 'plannedStartUtc'> {
  driverName?: string | null;
  vehicleCode?: string | null;
  plannedStartUtc?: string | null;
  totalDistanceKm?: number | null;
  lastPing?: { point: { lat: number; lng: number }; capturedAtUtc: string } | null;
  stops: { orderPublicId: string; orderNumber: string; consigneeName?: string | null; line1: string; city: string; postalCode?: string | null; statusCode: string; status: string; sequence: number; pieces?: number | null; stopKind?: string }[];
}

export async function fetchTripDetail(c: Company, publicId: string): Promise<TripDetail> {
  const { data } = await api.get<TripDetailDto>(`trips/${publicId}`, { tenantId: c.tenantId });
  return {
    publicId: data.publicId, code: data.code, kind: data.kind, statusCode: data.statusCode, status: data.status, driverName: data.driverName ?? null, vehicleCode: data.vehicleCode ?? null,
    stopCount: data.stopCount, plannedStartUtc: data.plannedStartUtc ?? null, totalDistanceKm: data.totalDistanceKm ?? null,
    lastPing: data.lastPing ? { lat: data.lastPing.point.lat, lng: data.lastPing.point.lng, capturedAtUtc: data.lastPing.capturedAtUtc } : null,
    stops: data.stops.map((s) => ({ ...s, consigneeName: s.consigneeName ?? null })),
  };
}

export interface StreetTrip {
  publicId: string;
  code: string;
  kind: string;
  statusCode: string;
  status: string;
  accepted: boolean;
  driverPublicId: string | null;
  driverName: string | null;
  vehicleCode: string | null;
  stopCount: number;
  isCurrent: boolean;
  driverPingAtUtc: string | null;
  distanceKm: number | null;
}

export interface StreetTargets {
  orderPublicId: string;
  orderNumber: string;
  kind: string;
  statusCode: string;
  status: string;
  movable: boolean;
  reason: string | null;
  currentTripPublicId: string | null;
  currentTripCode: string | null;
  trips: StreetTrip[];
  drivers: { publicId: string; name: string; hasOpenTrip: boolean }[];
  vehicles: { publicId: string; code: string; plate: string | null }[];
}

export async function fetchStreetTargets(tenantId: number, orderId: string): Promise<StreetTargets> {
  const { data } = await api.get<StreetTargets>(`mobile/street/orders/${orderId}/targets`, { tenantId });
  return data;
}

export interface StreetResult {
  action: 'ASSIGNED' | 'MOVED' | 'UNASSIGNED' | 'QUICK_ROUTE';
  orderNumber: string | null;
  orderStatus: string | null;
  tripCode: string | null;
  driverNotified: boolean;
}

export async function streetAssign(tenantId: number, orderPublicId: string, tripPublicId: string): Promise<StreetResult> {
  const { data } = await api.post<StreetResult>('mobile/street/assign', { orderPublicId, tripPublicId }, { tenantId });
  return data;
}

export async function streetUnassign(tenantId: number, orderPublicId: string): Promise<StreetResult> {
  const { data } = await api.post<StreetResult>('mobile/street/unassign', { orderPublicId }, { tenantId });
  return data;
}

export async function streetQuickRoute(tenantId: number, orderPublicIds: string[], driverPublicId: string, vehiclePublicId: string): Promise<StreetResult> {
  const { data } = await api.post<StreetResult>('mobile/street/quick-route', { orderPublicIds, driverPublicId, vehiclePublicId }, { tenantId });
  return data;
}
