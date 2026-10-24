// Prueba de las reglas puras de rutas de hoy y asignar en la calle (Lote 215). Uso: npm run test:street (Node 22+).
import assert from 'node:assert/strict';
import { buttonFor, distanceLabel, localDate, minutesSince, stopToShipment } from '../src/utils/streetRules.ts';

assert.equal(distanceLabel(null), null);
assert.equal(distanceLabel(-1), null);
assert.equal(distanceLabel(0.35), '350 m');
assert.equal(distanceLabel(2.34), '2.3 km');
assert.equal(distanceLabel(0), '0 m');

const now = new Date('2026-10-24T12:10:00Z');
assert.equal(minutesSince('2026-10-24T12:05:00Z', now), 5);
assert.equal(minutesSince('2026-10-24T12:05:00', now), 5, 'sin Z se toma como UTC');
assert.equal(minutesSince(null, now), null);
assert.equal(minutesSince('basura', now), null);
assert.equal(minutesSince('2026-10-24T13:00:00Z', now), 0, 'nunca negativo');

assert.equal(buttonFor(true, true), 'current');
assert.equal(buttonFor(false, true), 'move');
assert.equal(buttonFor(false, false), 'assign');

const s = stopToShipment({ tenantId: 3, name: 'Depot' }, 'PICKUP', 'T-1', { orderPublicId: 'o1', orderNumber: '2026-000001', consigneeName: 'Ana', line1: 'Calle 1', city: 'Ponce', postalCode: '00716', statusCode: 'PENDING', status: 'Pendiente', sequence: 2, pieces: 3 });
assert.equal(s.source, 'staff');
assert.equal(s.kind, 'PICKUP');
assert.equal(s.manifestCode, 'T-1');
assert.equal(s.address, 'Calle 1');
assert.equal(stopToShipment({ tenantId: 3, name: 'Depot' }, 'DELIVERY', 'T-1', { orderPublicId: 'o2', orderNumber: 'x', line1: '', city: '', statusCode: 'P', status: 'P', sequence: 1 }).city, null);

assert.equal(localDate(new Date(2026, 9, 4)), '2026-10-04');

console.log('test-street: ok');
