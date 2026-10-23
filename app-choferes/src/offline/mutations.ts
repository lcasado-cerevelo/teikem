import { enqueue } from './outbox';

// Lo que el chofer cierra en la calle se guarda en la cola (SQLite) y sale cuando hay señal. Cada elemento lleva la compañía (tenantId): decide con qué token de acceso se envía.
// El servidor es idempotente (un reintento de algo ya hecho responde «alreadyDone»), así que reenviar nunca duplica una entrega.

export interface DeliverInput {
  tenantId: number;
  shipmentId: string;
  recipientName: string;
  notes?: string;
  deliveredAtUtc: string;
  latitude?: number;
  longitude?: number;
  /** Base64 sin el prefijo data-URL. */
  signatureBase64?: string;
  photosBase64?: string[];
}

/** Entrega: quién recibió, firma, fotos y ubicación del teléfono. */
export function enqueueDeliver(input: DeliverInput): Promise<string> {
  return enqueue({
    kind: 'deliver',
    endpoint: `driver-app/shipments/${input.shipmentId}/deliver`,
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
      },
    },
  });
}

export interface FailInput {
  tenantId: number;
  shipmentId: string;
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
    endpoint: `driver-app/shipments/${input.shipmentId}/fail`,
    method: 'POST',
    payload: {
      tenantId: input.tenantId,
      shipmentId: input.shipmentId,
      body: { reasonCode: input.reasonCode, notes: input.notes, attemptedAtUtc: input.attemptedAtUtc, photoBase64: input.photoBase64 || undefined, latitude: input.latitude, longitude: input.longitude },
    },
  });
}
