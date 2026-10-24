import type { Shipment, ShipmentDetail, ShipmentSource, StaffOrderExtras } from '@/types/models';

/** El personal cierra órdenes por /mobile/orders (cualquier orden, con orders.deliver); el chofer por /driver-app/shipments (las de sus manifiestos). Misma evidencia, mismo cuerpo. */
export function closeEndpoint(source: ShipmentSource | undefined, shipmentId: string, action: 'deliver' | 'fail' | 'pickup'): string {
  return source === 'staff' ? `mobile/orders/${shipmentId}/${action}` : `driver-app/shipments/${shipmentId}/${action}`;
}

// Lote 214 — mapeos puros de las órdenes del personal (/api/v1/mobile/orders) a los modelos del envío que ya usan la ficha y las acciones del chofer.
// Sin React ni red: se prueban con scripts/test-staff-orders.mjs.

export interface StaffOrderRow {
  publicId: string;
  orderNumber: string;
  waybillNumber?: string | null;
  packBatchNumber: string;
  clientName: string;
  consigneeName: string;
  consigneeCity: string;
  statusCode: string;
  status: string;
  isInitialStatus: boolean;
  totalPieces: number;
  codAmount?: number | null;
}

export interface StaffOrderCardDto {
  shipment: {
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
    signatureRequired?: boolean;
    photoRequired?: boolean;
    codPending?: number | null;
    kind?: string;
  };
  clientPublicId: string;
  packBatchNumber: string;
  clientInvoiceNumber?: string | null;
  serviceTypeLabel: string;
  createdAtUtc: string;
  isInitialStatus: boolean;
  tripCode?: string | null;
  tripStatus?: string | null;
  driverName?: string | null;
  latitude?: number | null;
  longitude?: number | null;
  canDeliver: boolean;
  canFail: boolean;
  canPickup: boolean;
  proof?: { deliveredAtUtc: string; recipientName: string; notes?: string | null; source: string; distanceFromStopMeters?: number | null; hasSignature: boolean; photos: number } | null;
  attempts: { number: number; attemptedAtUtc: string; delivered: boolean; reason?: string | null; notes?: string | null; source: string; driver?: string | null; distanceFromStopMeters?: number | null }[];
}

const EMPTY_GUID = '00000000-0000-0000-0000-000000000000';

/** Una fila del buscador como «envío» de la app (lo mínimo; la ficha completa la trae el detalle). Sin ruta: una orden en su etapa inicial se trata como recogida. */
export function rowToShipment(c: { tenantId: number; name: string }, r: StaffOrderRow): Shipment {
  return {
    id: r.publicId, tenantId: c.tenantId, companyName: c.name, manifestId: '', manifestCode: '', kind: r.isInitialStatus ? 'PICKUP' : 'DELIVERY', manifestAccepted: true,
    orderNumber: r.orderNumber, waybill: r.waybillNumber ?? '', statusCode: r.statusCode, status: r.status, sequence: 0, consigneeName: r.consigneeName || null,
    address: '', city: r.consigneeCity || null, postalCode: null, pieces: r.totalPieces, codAmount: r.codAmount ?? null, notes: null, source: 'staff',
  };
}

/** La ficha del servidor sobre el envío base: lo del chofer más lo del personal (ruta, chofer, comprobante, intentos y qué acción cabe). */
export function cardToDetail(base: Shipment, d: StaffOrderCardDto): ShipmentDetail {
  const s = d.shipment;
  const staff: StaffOrderExtras = {
    packBatchNumber: d.packBatchNumber, clientInvoiceNumber: d.clientInvoiceNumber || null, serviceTypeLabel: d.serviceTypeLabel, createdAtUtc: d.createdAtUtc,
    tripCode: d.tripCode ?? null, tripStatus: d.tripStatus ?? null, driverName: d.driverName ?? null, latitude: d.latitude ?? null, longitude: d.longitude ?? null,
    canDeliver: d.canDeliver, canFail: d.canFail, canPickup: d.canPickup,
    proof: d.proof ? { ...d.proof, notes: d.proof.notes ?? null, distanceFromStopMeters: d.proof.distanceFromStopMeters ?? null } : null,
    attempts: d.attempts.map((a) => ({ ...a, reason: a.reason ?? null, notes: a.notes ?? null, driver: a.driver ?? null, distanceFromStopMeters: a.distanceFromStopMeters ?? null })),
  };
  return {
    ...base,
    source: 'staff',
    kind: s.kind === 'PICKUP' ? 'PICKUP' : 'DELIVERY',
    manifestId: s.manifestPublicId && s.manifestPublicId !== EMPTY_GUID ? s.manifestPublicId : '',
    manifestCode: d.tripCode ?? '',
    manifestAccepted: true,
    orderNumber: s.orderNumber, waybill: s.waybillNumber, statusCode: s.statusCode, status: s.status,
    consigneeName: s.consigneeName ?? base.consigneeName, contactPerson: s.contactPerson ?? null, phone: s.phone ?? null, address: s.address || base.address,
    city: s.city ?? base.city, postalCode: s.postalCode ?? base.postalCode, pieces: s.pieces ?? base.pieces, codAmount: s.codAmount ?? base.codAmount, notes: s.notes ?? base.notes,
    clientName: s.clientName ?? null,
    // Para el personal «se puede cerrar» = cabe alguna acción por estatus y tipo (el permiso lo decide la app con orders.deliver).
    canClose: d.canDeliver || d.canFail || d.canPickup,
    signatureRequired: s.signatureRequired ?? false, photoRequired: s.photoRequired ?? false, codPending: s.codPending ?? null,
    staff,
  };
}

/** Qué botones mostrar en Acciones para una orden del personal: ninguno si no cabe nada. */
export function staffActionsOf(x: Pick<StaffOrderExtras, 'canDeliver' | 'canFail' | 'canPickup'>): { complete: boolean; pickup: boolean; exception: boolean } {
  return { complete: x.canDeliver, pickup: x.canPickup, exception: x.canFail };
}
