import { api } from '@config/axios';
import type { LocalOrder, SnapStatus } from '@utils/scanRules';

// Lote 216 — llamadas del escaneo: la descarga (GET /mobile/scan/snapshot), las rutas abiertas y los escaneos (POST /scan/action, los mismos de la web; trips.scan).

export interface SnapRoute { publicId: string; code: string; kind: string; statusCode: string; status: string; driverName: string | null; stopCount: number }

export interface SnapDriver { publicId: string; name: string }

interface SnapOrderDto { city?: string | null; driverPublicId?: string | null; publicId: string; orderNumber: string; waybillNumber?: string | null; statusCode: string; tripCode?: string | null; tripPublicId?: string | null; pickUp: boolean; pieces: { number: string; actions?: string[]; missing?: string | null }[] }

export interface SnapPage {
  total: number;
  limit: number;
  capped: boolean;
  generatedAtUtc: string;
  orders: LocalOrder[];
  statuses: SnapStatus[];
  routes: SnapRoute[];
  drivers: SnapDriver[];
}

export const PAGE_SIZE = 500;

export async function fetchSnapshotPage(tenantId: number, skip: number): Promise<SnapPage> {
  const { data } = await api.get<{ total: number; limit: number; capped: boolean; generatedAtUtc: string; orders: SnapOrderDto[]; statuses: SnapStatus[]; routes: SnapRoute[]; drivers?: SnapDriver[] }>(
    'mobile/scan/snapshot', { tenantId, params: { skip, take: PAGE_SIZE }, timeout: 60_000 });
  return {
    total: data.total, limit: data.limit, capped: data.capped, generatedAtUtc: data.generatedAtUtc, statuses: data.statuses ?? [], routes: data.routes ?? [], drivers: data.drivers ?? [],
    orders: data.orders.map((o) => ({
      publicId: o.publicId, orderNumber: o.orderNumber, waybill: o.waybillNumber ?? null, statusCode: o.statusCode, tripCode: o.tripCode ?? null, tripPublicId: o.tripPublicId ?? null, pickUp: o.pickUp, city: o.city ?? null, driverPublicId: o.driverPublicId ?? null,
      pieces: o.pieces.map((p) => ({ number: p.number, actions: p.actions ?? [], missing: p.missing ?? null })),
    })),
  };
}

export async function fetchOpenRoutes(tenantId: number): Promise<SnapRoute[]> {
  const { data } = await api.get<SnapRoute[]>('mobile/scan/routes', { tenantId });
  return data;
}

/** Respuesta de POST /scan/action (lo que la pantalla y la lista de problemas necesitan). */
export interface ServerScanResult { action: string; outcome: string; voice: string; message: string; orderNumber: string | null; scanned: number | null; total: number | null; missing: number | null }

export async function postScanAction(tenantId: number, action: string, code: string, tripPublicId?: string | null): Promise<ServerScanResult> {
  const { data } = await api.post<ServerScanResult>('scan/action', { action, code, tripPublicId: tripPublicId || undefined }, { tenantId });
  return data;
}

export interface CloseCountResult { message: string; needsConfirmation: boolean; missing: number }

export async function postCloseCount(tenantId: number, orderPublicId: string, confirm: boolean): Promise<CloseCountResult> {
  const { data } = await api.post<CloseCountResult>('scan/close-count', { orderPublicId, confirm }, { tenantId });
  return data;
}
