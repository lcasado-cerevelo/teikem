// Modelos de la app del chofer, ya en los términos de Teikem (contratos de /api/v1/driver-app).
// El chofer puede trabajar en varias compañías con una sola sesión: todo lo que baja lleva la compañía (tenantId + nombre) de donde salió.

export type ManifestKind = 'DELIVERY' | 'PICKUP';

/** Un manifiesto (ruta) del chofer. `accepted` = ya salió (se aceptó); mientras no, el chofer decide qué envíos acepta. */
export interface Manifest {
  id: string;
  tenantId: number;
  companyName: string;
  code: string;
  kind: ManifestKind;
  /** yyyy-MM-dd */
  planDate: string;
  statusCode: string;
  status: string;
  accepted: boolean;
  shipmentCount: number;
  rowVersion: string | null;
}

/** Un envío dentro de un manifiesto. */
export interface Shipment {
  id: string;
  tenantId: number;
  companyName: string;
  manifestId: string;
  manifestCode: string;
  kind: ManifestKind;
  /** El manifiesto ya se aceptó: solo entonces se puede entregar o marcar «no entregado». */
  manifestAccepted: boolean;
  orderNumber: string;
  waybill: string;
  statusCode: string;
  status: string;
  sequence: number;
  consigneeName: string | null;
  address: string;
  city: string | null;
  postalCode: string | null;
  pieces: number | null;
  codAmount: number | null;
  notes: string | null;
  /** Lote 214: de dónde salió. 'driver' (omitido) = de un manifiesto del chofer (/driver-app); 'staff' = una orden buscada por el personal (/mobile/orders). Decide a qué endpoint se envía lo que se cierra. */
  source?: ShipmentSource;
}

export type ShipmentSource = 'driver' | 'staff';

/** Lote 214: lo que la ficha de una orden del personal trae además de lo del chofer. */
export interface StaffOrderExtras {
  packBatchNumber: string;
  clientInvoiceNumber: string | null;
  serviceTypeLabel: string;
  createdAtUtc: string;
  tripCode: string | null;
  tripStatus: string | null;
  driverName: string | null;
  latitude: number | null;
  longitude: number | null;
  canDeliver: boolean;
  canFail: boolean;
  canPickup: boolean;
  proof: { deliveredAtUtc: string; recipientName: string; notes: string | null; source: string; distanceFromStopMeters: number | null; hasSignature: boolean; photos: number } | null;
  attempts: Array<{ number: number; attemptedAtUtc: string; delivered: boolean; reason: string | null; notes: string | null; source: string; driver: string | null; distanceFromStopMeters: number | null }>;
}

/** El envío con lo que se necesita en la puerta (teléfono, cliente…). */
export interface ShipmentDetail extends Shipment {
  contactPerson: string | null;
  phone: string | null;
  clientName: string | null;
  canClose: boolean;
  /** El cliente exige firma / foto al entregar. */
  signatureRequired: boolean;
  photoRequired: boolean;
  /** Lo que falta por cobrar del COD (null = no tiene COD). */
  codPending: number | null;
  /** Solo en una orden del personal (source 'staff'). */
  staff?: StaffOrderExtras;
}

export interface Reason {
  code: string;
  label: string;
}

/** Estatus de un envío que ya no se trabaja desde la ruta (entregado, fallido, cancelado…). */
export const OPEN_STATUSES = ['ASSIGNED', 'IN_TRANSIT'];
/** Una recogida sigue abierta mientras el envío está «Sometido» (el chofer aún no lo recoge). */
export const PICKUP_OPEN_STATUSES = ['SUBMITTED'];
export const isOpenShipment = (s: { kind: ManifestKind; statusCode: string }) =>
  (s.kind === 'PICKUP' ? PICKUP_OPEN_STATUSES : OPEN_STATUSES).includes(s.statusCode);

// Geo coordinate used by the map / delivery documentation flow.
export interface LatLng {
  lat: number;
  lng: number;
}
