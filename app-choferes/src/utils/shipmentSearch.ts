import type { Shipment } from '@/types/models';

// Búsqueda libre sobre los envíos YA cargados (los del chofer, que la sincronización deja en caché). Es local a propósito: funciona sin señal y
// responde en cada tecla sin ir al servidor.
//
// Barre: número de orden, guía (waybill), nombre del consignatario, dirección, ciudad, notas, manifiesto y la COMPAÑÍA (por nombre o por su número),
// porque un chofer con varias compañías ve todo junto y busca por compañía.

function textMatch(value: string | null | undefined, needle: string): boolean {
  return (value ?? '').toLowerCase().includes(needle);
}

export function matchesShipment(s: Shipment, rawQuery: string): boolean {
  const q = rawQuery.trim().toLowerCase();
  if (!q) return true;
  return (
    textMatch(s.orderNumber, q) ||
    textMatch(s.waybill, q) ||
    textMatch(s.consigneeName, q) ||
    textMatch(s.address, q) ||
    textMatch(s.city, q) ||
    textMatch(s.notes, q) ||
    textMatch(s.manifestCode, q) ||
    textMatch(s.companyName, q) ||
    String(s.tenantId) === q
  );
}

export function filterShipments(list: Shipment[], query: string): Shipment[] {
  if (!query.trim()) return list;
  return list.filter((s) => matchesShipment(s, query));
}
