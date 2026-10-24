import { onlineManager } from '@tanstack/react-query';
import { api } from '@config/axios';
import { enqueue, getItem } from './outbox';
import { drainOutbox } from './syncEngine';
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
function buildDeliver(input: DeliverInput) {
  return {
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
  };
}

export function enqueueDeliver(input: DeliverInput): Promise<string> {
  const q = buildDeliver(input);
  return enqueue({ kind: q.kind, endpoint: q.endpoint, method: 'POST', payload: q.payload });
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
function buildFail(input: FailInput) {
  return {
    kind: 'fail',
    endpoint: closeEndpoint(input.source, input.shipmentId, 'fail'),
    method: 'POST',
    payload: {
      tenantId: input.tenantId,
      shipmentId: input.shipmentId,
      body: { reasonCode: input.reasonCode, notes: input.notes, attemptedAtUtc: input.attemptedAtUtc, photoBase64: input.photoBase64 || undefined, latitude: input.latitude, longitude: input.longitude },
    },
  };
}

export function enqueueFail(input: FailInput): Promise<string> {
  const q = buildFail(input);
  return enqueue({ kind: q.kind, endpoint: q.endpoint, method: 'POST', payload: q.payload });
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
function buildPickup(input: PickupInput) {
  return {
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
  };
}

export function enqueuePickup(input: PickupInput): Promise<string> {
  const q = buildPickup(input);
  return enqueue({ kind: q.kind, endpoint: q.endpoint, method: 'POST', payload: q.payload });
}

/**
 * Cierra y dice si ya llegó al servidor. El chofer usa la cola (sin señal queda guardado y sale después: `false`). El personal (source 'staff', Lote 214) NO trabaja sin señal:
 * se envía directo y, si no hay señal o el servidor lo rechaza, lanza el error para que la pantalla lo diga y nada quede en cola.
 */
export async function submitDeliver(input: DeliverInput & { creditToDriver?: boolean }): Promise<boolean> {
  if (input.source === 'staff') {
    const queued = buildDeliver(input);
    await api.post(queued.endpoint, { ...queued.payload.body, creditToDriver: input.creditToDriver ?? true }, { tenantId: input.tenantId });
    return true;
  }
  const id = await enqueueDeliver(input);
  if (onlineManager.isOnline()) await drainOutbox();
  return (await getItem(id))?.status === 'done';
}

/**
 * Cierra y dice si ya llegó al servidor. El chofer usa la cola (sin señal queda guardado y sale después: `false`). El personal (source 'staff', Lote 214) NO trabaja sin señal:
 * se envía directo y, si no hay señal o el servidor lo rechaza, lanza el error para que la pantalla lo diga y nada quede en cola.
 */
export async function submitFail(input: FailInput & { creditToDriver?: boolean }): Promise<boolean> {
  if (input.source === 'staff') {
    const queued = buildFail(input);
    await api.post(queued.endpoint, { ...queued.payload.body, creditToDriver: input.creditToDriver ?? true }, { tenantId: input.tenantId });
    return true;
  }
  const id = await enqueueFail(input);
  if (onlineManager.isOnline()) await drainOutbox();
  return (await getItem(id))?.status === 'done';
}

/**
 * Cierra y dice si ya llegó al servidor. El chofer usa la cola (sin señal queda guardado y sale después: `false`). El personal (source 'staff', Lote 214) NO trabaja sin señal:
 * se envía directo y, si no hay señal o el servidor lo rechaza, lanza el error para que la pantalla lo diga y nada quede en cola.
 */
export async function submitPickup(input: PickupInput & { creditToDriver?: boolean }): Promise<boolean> {
  if (input.source === 'staff') {
    const queued = buildPickup(input);
    await api.post(queued.endpoint, { ...queued.payload.body, creditToDriver: input.creditToDriver ?? true }, { tenantId: input.tenantId });
    return true;
  }
  const id = await enqueuePickup(input);
  if (onlineManager.isOnline()) await drainOutbox();
  return (await getItem(id))?.status === 'done';
}
