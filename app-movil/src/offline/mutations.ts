import { enqueue } from './outbox';
import type { ShipmentSource } from '@/types/models';
import { closeEndpoint } from '@utils/staffOrders';

// Lo que el chofer cierra en la calle se guarda en la cola (SQLite) y sale cuando hay señal. Cada elemento lleva la compañía (tenantId): decide con qué token de acceso se envía.
// El servidor es idempotente (un reintento de algo ya hecho responde «alreadyDone»), así que reenviar nunca duplica una entrega.

export interface DeliverInput {
  tenantId: number;
  shipmentId: string;
  source?: ShipmentSource;
  recipientName: string;
  notes?: string;
  deliveredAtUtc: string;
  latitude?: number;
  longitude?: number;
  /** Base64 sin el prefijo data-URL. */
  signatureBase64?: string;
  photosBase64?: string[];
  /** Cobros contra entrega: `clientId` único por cobro (un reintento no cobra dos veces). */
  cods?: Array<{ clientId: string; method: string; amount: number; checkNumber?: string }>;
}

/** Entrega: quién recibió, firma, fotos y ubicación del teléfono. */
export function enqueueDeliver(input: DeliverInput): Promise<string> {
  return enqueue({
    kind: 'deliver',
    endpoint: closeEndpoint(input.source, input.shipmentId, 'deliver'),
    method: 'POST',
    payload: {
      tenantId: input.tenantId,
      shipmentId: input.shipmentId,
      body: {
        recipientName: input.recipientName,
        notes: input.notes,
        deliveredAtUtc: input.deliveredAtUtc,
        latitude: input.latitude,
        longitude: input.longitude,
        signaturePngBase64: input.signatureBase64 || undefined,
        photosBase64: input.photosBase64?.length ? input.photosBase64 : undefined,
        cods: input.cods?.length ? input.cods : undefined,
      },
    },
  });
}

export interface FailInput {
  tenantId: number;
  shipmentId: string;
  source?: ShipmentSource;
  reasonCode: string;
  notes?: string;
  attemptedAtUtc: string;
  photoBase64?: string;
  latitude?: number;
  longitude?: number;
}

/** No entregado: el motivo de la lista «Razón de fallo», una nota y una foto opcional. */
export function enqueueFail(input: FailInput): Promise<string> {
  return enqueue({
    kind: 'fail',
    endpoint: closeEndpoint(input.source, input.shipmentId, 'fail'),
    method: 'POST',
    payload: {
      tenantId: input.tenantId,
      shipmentId: input.shipmentId,
      body: { reasonCode: input.reasonCode, notes: input.notes, attemptedAtUtc: input.attemptedAtUtc, photoBase64: input.photoBase64 || undefined, latitude: input.latitude, longitude: input.longitude },
    },
  });
}

export interface PickupInput {
  tenantId: number;
  shipmentId: string;
  source?: ShipmentSource;
  /** Quién entregó el paquete al chofer. */
  giverName?: string;
  notes?: string;
  pickedAtUtc: string;
  latitude?: number;
  longitude?: number;
  photosBase64?: string[];
}

/** Recogida: el chofer recibe el paquete (quién lo entregó, fotos y ubicación del teléfono). */
export function enqueuePickup(input: PickupInput): Promise<string> {
  return enqueue({
    kind: 'pickup',
    endpoint: closeEndpoint(input.source, input.shipmentId, 'pickup'),
    method: 'POST',
    payload: {
      tenantId: input.tenantId,
      shipmentId: input.shipmentId,
      body: {
        giverName: input.giverName,
        notes: input.notes,
        pickedAtUtc: input.pickedAtUtc,
        latitude: input.latitude,
        longitude: input.longitude,
        photosBase64: input.photosBase64?.length ? input.photosBase64 : undefined,
      },
    },
  });
}
