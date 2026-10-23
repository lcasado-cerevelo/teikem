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
}

export interface Reason {
  code: string;
  label: string;
}

/** Estatus de un envío que ya no se trabaja desde la ruta (entregado, fallido, cancelado…). */
export const OPEN_STATUSES = ['ASSIGNED', 'IN_TRANSIT'];

// Geo coordinate used by the map / delivery documentation flow.
export interface LatLng {
  lat: number;
  lng: number;
}
