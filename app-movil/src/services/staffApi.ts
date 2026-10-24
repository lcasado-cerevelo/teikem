import { api } from '@config/axios';
import type { Company } from '@services/driverApi';
import type { Reason, Shipment, ShipmentDetail } from '@/types/models';
import { cardToDetail, rowToShipment, type StaffOrderCardDto, type StaffOrderRow } from '@utils/staffOrders';

// Lo que el personal ve en el inicio (Lote 213): «Necesita tu atención» y el Pulso del día, de cada compañía con permiso. Son los endpoints de la web; el servidor ya filtra por permisos.

export interface AttentionItem {
  code: string;
  module: string;
  tone: string;
  count: number;
  params: Record<string, string>;
  sinceUtc: string | null;
  summary: boolean;
}

export interface AttentionGroup {
  code: string;
  total: number;
}

export interface CompanyAttention {
  tenantId: number;
  companyName: string;
  total: number;
  items: AttentionItem[];
  groups: AttentionGroup[];
}

interface AttentionDto {
  total: number;
  items: { code: string; module: string; tone: string; count: number; params?: Record<string, string> | null; sinceUtc?: string | null; summary?: boolean }[];
  groups: { code: string; total: number }[];
}

export async function fetchAttention(c: Company): Promise<CompanyAttention> {
  const { data } = await api.get<AttentionDto>('analytics/attention', { tenantId: c.tenantId });
  return {
    tenantId: c.tenantId,
    companyName: c.name,
    total: data.total,
    items: data.items.map((i) => ({ code: i.code, module: i.module, tone: i.tone, count: i.count, params: i.params ?? {}, sinceUtc: i.sinceUtc ?? null, summary: !!i.summary })),
    groups: data.groups.map((g) => ({ code: g.code, total: g.total })),
  };
}

export interface Indicator {
  id: number;
  name: string;
  value: number | null;
  isMoney: boolean;
}

export interface CompanyPulse {
  tenantId: number;
  companyName: string;
  indicators: Indicator[];
}

interface PulseDto {
  indicators: { id: number; name: string; value?: number | null; isMoney: boolean; isVisible: boolean }[];
}

export async function fetchPulse(c: Company): Promise<CompanyPulse> {
  const { data } = await api.get<PulseDto>('analytics/pulse', { tenantId: c.tenantId });
  return { tenantId: c.tenantId, companyName: c.name, indicators: data.indicators.filter((i) => i.isVisible).map((i) => ({ id: i.id, name: i.name, value: i.value ?? null, isMoney: i.isMoney })) };
}

// ---------------------------------------------------------------------------
// Lote 214 — órdenes: buscar (número, conduce, empaque, factura, paquete: lo que lee el escáner primero), la ficha y los motivos de «no entregado» para el personal.
// ---------------------------------------------------------------------------

export interface StaffOrderSearch {
  /** WAYBILL, ORDER_NUMBER, PACK_BATCH, INVOICE o PACKAGE si hubo coincidencia exacta; null si fue búsqueda libre. */
  matchedBy: string | null;
  items: Shipment[];
}

export async function searchOrders(c: Company, q: string): Promise<StaffOrderSearch> {
  const { data } = await api.get<{ query: string; matchedBy?: string | null; items: StaffOrderRow[] }>('mobile/orders', { tenantId: c.tenantId, params: { q } });
  return { matchedBy: data.matchedBy ?? null, items: data.items.map((r) => rowToShipment(c, r)) };
}

export async function fetchStaffShipmentDetail(base: Shipment): Promise<ShipmentDetail> {
  const { data } = await api.get<StaffOrderCardDto>(`mobile/orders/${base.id}`, { tenantId: base.tenantId });
  return cardToDetail(base, data);
}

export async function fetchStaffFailureReasons(tenantId: number): Promise<Reason[]> {
  const { data } = await api.get<Reason[]>('mobile/failure-reasons', { tenantId });
  return data;
}
