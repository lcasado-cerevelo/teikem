import type { Shipment } from '@/types/models';

// Lote 215 — reglas puras de «Rutas de hoy» y «Asignar en la calle» (sin React ni red; se prueban con scripts/test-street.mjs).

/** «2.3 km» / «350 m»; null si no hay distancia. */
export function distanceLabel(km: number | null | undefined): string | null {
  if (km == null || !Number.isFinite(km) || km < 0) return null;
  if (km < 1) return `${Math.round(km * 1000)} m`;
  return `${km.toFixed(1)} km`;
}

/** Minutos desde la última ubicación del chofer (null si no hay o la fecha es inválida). */
export function minutesSince(iso: string | null | undefined, now: Date = new Date()): number | null {
  if (!iso) return null;
  const t = new Date(iso.endsWith('Z') ? iso : `${iso}Z`).getTime();
  if (Number.isNaN(t)) return null;
  return Math.max(0, Math.round((now.getTime() - t) / 60000));
}

/** Qué hace el botón de una ruta para la orden: ya está en ella, la pasa de otra ruta, o la manda por primera vez. */
export type TripButton = 'current' | 'move' | 'assign';
export function buttonFor(isCurrent: boolean, orderHasTrip: boolean): TripButton {
  if (isCurrent) return 'current';
  return orderHasTrip ? 'move' : 'assign';
}

/** Una parada de la ruta (la ficha de ruta de la web) convertida en el «envío» que abre la ficha de la orden. */
export interface RouteStopRow {
  orderPublicId: string;
  orderNumber: string;
  consigneeName?: string | null;
  line1: string;
  city: string;
  postalCode?: string | null;
  statusCode: string;
  status: string;
  sequence: number;
  pieces?: number | null;
  stopKind?: string;
}

export function stopToShipment(c: { tenantId: number; name: string }, tripKind: string, tripCode: string, s: RouteStopRow): Shipment {
  return {
    id: s.orderPublicId, tenantId: c.tenantId, companyName: c.name, manifestId: '', manifestCode: tripCode, kind: tripKind === 'PICKUP' ? 'PICKUP' : 'DELIVERY', manifestAccepted: true,
    orderNumber: s.orderNumber, waybill: '', statusCode: s.statusCode, status: s.status, sequence: s.sequence, consigneeName: s.consigneeName ?? null,
    address: s.line1, city: s.city || null, postalCode: s.postalCode ?? null, pieces: s.pieces ?? null, codAmount: null, notes: null, source: 'staff',
  };
}

/** Fecha local yyyy-MM-dd (para pedir «hoy» sin depender de la zona del servidor). */
export function localDate(d: Date = new Date()): string {
  const p = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}`;
}
