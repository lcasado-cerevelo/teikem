import { api } from '@config/axios';
import type { Manifest, ManifestKind, Reason, Shipment, ShipmentDetail } from '@/types/models';

// Llamadas a /api/v1/driver-app/*. Cada una lleva `tenantId`: decide con qué token de acceso se firma (un token por compañía).

interface ManifestDto {
  publicId: string;
  code: string;
  kind: ManifestKind;
  planDate: string;
  statusCode: string;
  status: string;
  accepted: boolean;
  shipmentCount: number;
  rowVersion?: string | null;
}

interface ShipmentDto {
  publicId: string;
  orderNumber: string;
  waybillNumber: string;
  statusCode: string;
  status: string;
  sequence: number;
  consigneeName?: string | null;
  address: string;
  city?: string | null;
  postalCode?: string | null;
  pieces?: number | null;
  codAmount?: number | null;
  notes?: string | null;
}

interface ManifestDetailDto extends ManifestDto {
  shipments: ShipmentDto[];
}

interface ShipmentDetailDto {
  publicId: string;
  manifestPublicId: string;
  orderNumber: string;
  waybillNumber: string;
  statusCode: string;
  status: string;
  consigneeName?: string | null;
  contactPerson?: string | null;
  phone?: string | null;
  address: string;
  city?: string | null;
  postalCode?: string | null;
  pieces?: number | null;
  codAmount?: number | null;
  notes?: string | null;
  clientName?: string | null;
  canClose: boolean;
}

export interface Company {
  tenantId: number;
  name: string;
}

const toManifest = (c: Company, m: ManifestDto): Manifest => ({
  id: m.publicId, tenantId: c.tenantId, companyName: c.name, code: m.code, kind: m.kind, planDate: m.planDate, statusCode: m.statusCode, status: m.status,
  accepted: m.accepted, shipmentCount: m.shipmentCount, rowVersion: m.rowVersion ?? null,
});

const toShipment = (c: Company, m: ManifestDto, s: ShipmentDto): Shipment => ({
  id: s.publicId, tenantId: c.tenantId, companyName: c.name, manifestId: m.publicId, manifestCode: m.code, kind: m.kind, manifestAccepted: m.accepted,
  orderNumber: s.orderNumber, waybill: s.waybillNumber, statusCode: s.statusCode, status: s.status, sequence: s.sequence, consigneeName: s.consigneeName ?? null,
  address: s.address, city: s.city ?? null, postalCode: s.postalCode ?? null, pieces: s.pieces ?? null, codAmount: s.codAmount ?? null, notes: s.notes ?? null,
});

/** Todo el trabajo de UNA compañía: sus manifiestos con sus envíos (uno por manifiesto). */
export async function fetchCompanyWork(c: Company): Promise<{ manifests: Manifest[]; shipments: Shipment[] }> {
  const { data: list } = await api.get<ManifestDto[]>('driver-app/manifests', { tenantId: c.tenantId });
  const details = await Promise.all(list.map((m) => api.get<ManifestDetailDto>(`driver-app/manifests/${m.publicId}`, { tenantId: c.tenantId }).then((r) => r.data)));
  return {
    manifests: details.map((d) => toManifest(c, d)),
    shipments: details.flatMap((d) => d.shipments.map((s) => toShipment(c, d, s))),
  };
}

export async function fetchDeclineReasons(tenantId: number): Promise<Reason[]> {
  const { data } = await api.get<Array<{ code: string; label: string }>>('driver-app/decline-reasons', { tenantId });
  return data;
}

export async function fetchFailureReasons(tenantId: number): Promise<Reason[]> {
  const { data } = await api.get<Array<{ code: string; label: string }>>('driver-app/failure-reasons', { tenantId });
  return data;
}

export async function fetchShipmentDetail(s: Shipment): Promise<ShipmentDetail> {
  const { data } = await api.get<ShipmentDetailDto>(`driver-app/shipments/${s.id}`, { tenantId: s.tenantId });
  return {
    ...s, consigneeName: data.consigneeName ?? s.consigneeName, contactPerson: data.contactPerson ?? null, phone: data.phone ?? null, address: data.address || s.address,
    city: data.city ?? s.city, postalCode: data.postalCode ?? s.postalCode, pieces: data.pieces ?? s.pieces, codAmount: data.codAmount ?? s.codAmount, notes: data.notes ?? s.notes,
    statusCode: data.statusCode, status: data.status, clientName: data.clientName ?? null, canClose: data.canClose,
  };
}

export interface AcceptResult {
  accepted: number;
  declined: number;
  started: boolean;
}

/** Acepta el manifiesto (la salida). Los envíos de `declines` no se aceptan y llevan su motivo. */
export async function acceptManifest(m: Manifest, declines: Array<{ shipmentPublicId: string; reasonCode: string; note?: string }>): Promise<AcceptResult> {
  const { data } = await api.post<AcceptResult>(`driver-app/manifests/${m.id}/accept`, { declines, rowVersion: m.rowVersion }, { tenantId: m.tenantId });
  return data;
}
