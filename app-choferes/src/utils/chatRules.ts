// Reglas puras del chat del chofer (sin pantallas): vista previa, fotos por mensaje y orden de los hilos.

export const MAX_PHOTOS = 4;

/** El texto de la lista cuando el hilo no tiene vista previa. */
export function previewOf(preview: string | null | undefined, empty: string): string {
  const p = (preview ?? '').trim();
  return p.length === 0 ? empty : p;
}

/** Los hilos de todas las compañías en una sola lista: el más reciente primero. */
export function sortThreads<T extends { lastMessageAtUtc: string }>(threads: readonly T[]): T[] {
  return [...threads].sort((a, b) => b.lastMessageAtUtc.localeCompare(a.lastMessageAtUtc));
}

/** ¿Se puede enviar? Hay texto o al menos una foto, y no pasa del máximo de fotos. */
export function canSend(text: string, photos: number): boolean {
  return (text.trim().length > 0 || photos > 0) && photos <= MAX_PHOTOS;
}

/** La referencia de un mensaje a un envío abierto del chofer: el envío cuyo número de orden es el título de la tarjeta. */
export function findShipmentForCard<T extends { orderNumber: string; tenantId: number }>(shipments: readonly T[], tenantId: number, title: string): T | undefined {
  return shipments.find((s) => s.tenantId === tenantId && s.orderNumber === title);
}
