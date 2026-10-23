import type { Shipment } from '@/types/models';

// Búsqueda libre sobre las órdenes YA cargadas (las del chofer, que la
// sincronización deja en caché). Es local a propósito: funciona sin señal y
// responde en cada tecla sin ir al servidor.
//
// Campos que barre, según lo pedido:
//   - número de referencia -> referenceNo
//   - número de orden      -> poNumber (Shipments.PONumber)
//   - número de waybill    -> waybill
//   - número de pieza      -> barcode (Shipments.Barcode es la lista de códigos
//                             de pieza separada por comas, así que un LIKE aquí
//                             cubre las piezas sin cargarlas una por una)
//   - nombre de la persona -> consigneeName + contactPerson
//   - número de teléfono   -> phoneNumber (comparado solo por dígitos)

const digitsOnly = (s: string) => s.replace(/\D/g, '');

function textMatch(value: string | null | undefined, needle: string): boolean {
  return (value ?? '').toLowerCase().includes(needle);
}

// El teléfono se compara dígito a dígito para que "787555" encuentre
// "(787) 555-1234": el formato guardado varía y nadie escribe los paréntesis.
function phoneMatch(value: string | null | undefined, needleDigits: string): boolean {
  if (!needleDigits) return false;
  return digitsOnly(value ?? '').includes(needleDigits);
}

export function matchesShipment(s: Shipment, rawQuery: string): boolean {
  const q = rawQuery.trim().toLowerCase();
  if (!q) return true;

  if (
    textMatch(s.referenceNo, q) ||
    textMatch(s.poNumber, q) ||
    textMatch(s.waybill, q) ||
    textMatch(s.barcode, q) ||
    textMatch(s.consigneeName, q) ||
    textMatch(s.contactPerson, q)
  ) {
    return true;
  }

  return phoneMatch(s.phoneNumber, digitsOnly(q));
}

export function filterShipments(list: Shipment[], query: string): Shipment[] {
  if (!query.trim()) return list;
  return list.filter((s) => matchesShipment(s, query));
}
