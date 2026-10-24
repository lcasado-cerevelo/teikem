// Prueba de los mapeos puros de las órdenes del personal (Lote 214). Uso: npm run test:staff (Node 22+).
import assert from 'node:assert/strict';
import { cardToDetail, closeEndpoint, rowToShipment, staffActionsOf } from '../src/utils/staffOrders.ts';

const c = { tenantId: 3, name: 'Depot' };
const row = { publicId: 'o1', orderNumber: '2026-000001', waybillNumber: 'W-1', packBatchNumber: 'PB-1', clientName: 'Cliente', consigneeName: 'Ana', consigneeCity: 'Ponce', statusCode: 'SUBMITTED', status: 'Sometida', isInitialStatus: true, totalPieces: 2, codAmount: null };
const s = rowToShipment(c, row);
assert.equal(s.source, 'staff');
assert.equal(s.kind, 'PICKUP', 'sin ruta, en la etapa inicial se recoge');
assert.equal(s.manifestAccepted, true, 'el personal no acepta manifiestos: nunca se le pide «acepte primero»');
assert.equal(rowToShipment(c, { ...row, isInitialStatus: false, statusCode: 'IN_TRANSIT' }).kind, 'DELIVERY');

const card = {
  shipment: { publicId: 'o1', manifestPublicId: '00000000-0000-0000-0000-000000000000', orderNumber: '2026-000001', waybillNumber: 'W-1', statusCode: 'IN_TRANSIT', status: 'En tránsito', consigneeName: 'Ana', phone: '787-555-1234', address: 'Calle 1', city: 'Ponce', postalCode: '00716', pieces: 2, codAmount: 10, notes: null, clientName: 'Cliente', canClose: false, codPending: 10, kind: 'DELIVERY' },
  clientPublicId: 'c1', packBatchNumber: 'PB-1', clientInvoiceNumber: 'F-9', serviceTypeLabel: 'Regular', createdAtUtc: '2026-10-24T10:00:00Z', isInitialStatus: false,
  tripCode: 'T-5', tripStatus: 'En curso', driverName: 'Chofer Siete', latitude: 18.01, longitude: -66.61, canDeliver: true, canFail: true, canPickup: false, proof: null,
  attempts: [{ number: 1, attemptedAtUtc: '2026-10-24T11:00:00Z', delivered: false, reason: 'Ausente', source: 'APP' }],
};
const d = cardToDetail(s, card);
assert.equal(d.kind, 'DELIVERY', 'el tipo lo dice el servidor, no la fila');
assert.equal(d.manifestId, '', 'un GUID vacío es «sin ruta»');
assert.equal(d.manifestCode, 'T-5');
assert.equal(d.canClose, true, 'cabe alguna acción');
assert.equal(d.staff.attempts[0].driver, null);
assert.equal(d.staff.attempts[0].reason, 'Ausente');
assert.deepEqual(staffActionsOf(d.staff), { complete: true, pickup: false, exception: true });
assert.deepEqual(staffActionsOf({ canDeliver: true, canFail: false, canPickup: false }), { complete: true, pickup: false, exception: false }, 'fallida: solo volver a entregar');
assert.equal(cardToDetail(s, { ...card, canDeliver: false, canFail: false, canPickup: false }).canClose, false);

assert.equal(closeEndpoint('staff', 'o1', 'deliver'), 'mobile/orders/o1/deliver');
assert.equal(closeEndpoint(undefined, 'o1', 'fail'), 'driver-app/shipments/o1/fail');
assert.equal(closeEndpoint('driver', 'o1', 'pickup'), 'driver-app/shipments/o1/pickup');

console.log('test-staff-orders: ok');
