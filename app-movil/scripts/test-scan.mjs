// Prueba de las reglas puras del escaneo local sin señal (Lote 216). Uso: npm run test:scan (Node 22+).
import assert from 'node:assert/strict';
import { ALL_SCOPE, ambiguous, applyScan, destinationTrip, fold, inScope, isServerProblem, normalizeCode, notFound, targetStatus } from '../src/utils/scanRules.ts';

const S = (code, sortOrder, stageKind = 'PIPELINE', isInitial = false) => ({ code, label: code.toLowerCase(), sortOrder, stageKind, isInitial, isEnabled: true });
const statuses = [S('SUBMITTED', 1, 'PIPELINE', true), S('PICKUP', 2), S('UNASSIGNED', 3), S('ASSIGNED', 4), S('IN_TRANSIT', 5), S('DELIVERED', 8, 'TERMINAL'), S('IN_WAREHOUSE', 9, 'TERMINAL'), S('FAILED', 22, 'LATERAL'), S('CANCELLED', 30, 'TERMINAL')];
const order = (statusCode, over = {}) => ({ publicId: 'o1', orderNumber: '2026-000001', waybill: null, statusCode, tripCode: null, tripPublicId: null, pickUp: false, pieces: [{ number: 'P1', actions: [], missing: null }, { number: 'P2', actions: [], missing: null }], ...over });

assert.equal(targetStatus('ROUTE'), 'ASSIGNED');
assert.equal(targetStatus('DEPART'), 'IN_TRANSIT');
assert.equal(normalizeCode(' P1\r\n'), 'P1');
assert.equal(normalizeCode('   '), null);
assert.equal(normalizeCode('x'.repeat(41)), null);
assert.equal(notFound().voice, 'notfound');
assert.equal(ambiguous(2).message.params.n, 2);

// la primera pieza con una acción mueve el envío; la siguiente solo cuenta («2 de 2»)
let o = order('UNASSIGNED');
let r = applyScan(o, 'P1', 'ROUTE', statuses, { chosenTripCode: 'T-5' });
assert.equal(r.result.outcome, 'APPLIED');
assert.equal(r.nextStatus, 'ASSIGNED');
assert.deepEqual([r.result.scanned, r.result.total], [1, 2]);
assert.equal(r.result.routeNote.key, 'SCAN.ROUTE_CHOSEN');
assert.equal(r.result.send, true);
o = order('ASSIGNED', { pieces: [{ number: 'P1', actions: ['ROUTE'], missing: null }, { number: 'P2', actions: [], missing: null }] });
r = applyScan(o, 'P2', 'ROUTE', statuses);
assert.equal(r.result.outcome, 'COUNTED');
assert.equal(r.result.voice, 'found', 'el escaneo es a la PIEZA: una pieza nueva dice «Sí» aunque el envío ya estuviera en ese estatus');
assert.equal(r.result.message.key, 'SCAN.MSG_SAME_STATUS');
assert.equal(r.nextStatus, null);
assert.equal(r.recorded, true, 'aun así la pieza se cuenta y va al servidor');
assert.equal(r.result.send, true);
assert.deepEqual([r.result.scanned, r.result.total], [2, 2]);
assert.equal(r.result.routeNote.key, 'SCAN.ROUTE_ZONE');

// la MISMA pieza otra vez con la misma acción: «Ok», no se manda (y es la pieza la que lo dice)
r = applyScan(o, 'P1', 'ROUTE', statuses);
assert.deepEqual([r.result.outcome, r.result.voice, r.result.send, r.recorded], ['DUPLICATE', 'dup', false, false]);

// otra pieza del mismo envío sigue dando «Sí» y la repetida de esa pieza «Ok»
r = applyScan(o, 'P2', 'ROUTE', statuses);
assert.deepEqual([r.result.voice, r.recorded], ['found', true]);
o = order('ASSIGNED', { pieces: [{ number: 'P1', actions: ['ROUTE'], missing: null }, { number: 'P2', actions: ['ROUTE'], missing: null }] });
assert.equal(applyScan(o, 'P2', 'ROUTE', statuses).result.voice, 'dup');
o = order('ASSIGNED', { pieces: [{ number: 'P1', actions: ['ROUTE'], missing: null }, { number: 'P2', actions: [], missing: null }] });

// el número del ENVÍO no es una pieza: «No»
assert.equal(applyScan(o, '2026-000001', 'ROUTE', statuses).result.outcome, 'NOT_FOUND');
// no existe la pieza
assert.equal(applyScan(o, 'X', 'ROUTE', statuses).result.outcome, 'NOT_FOUND');

// final: se ignora
r = applyScan(order('DELIVERED'), 'P1', 'DEPART', statuses);
assert.deepEqual([r.result.outcome, r.result.send], ['FINAL', false]);

// ya MÁS ADELANTE (Enrutar sobre uno En tránsito): el sistema PARA y advierte; nada se anota ni se manda
r = applyScan(order('IN_TRANSIT', { tripCode: 'T-9' }), 'P1', 'ROUTE', statuses);
assert.deepEqual([r.result.outcome, r.result.voice, r.result.send, r.recorded, r.nextStatus], ['BEYOND', 'warn', false, false, null]);
assert.equal(r.result.message.params.current, 'in_transit');
assert.equal(r.result.message.params.target, 'assigned');
// si la persona confirma, la pieza solo se cuenta y el estatus NO retrocede
r = applyScan(order('IN_TRANSIT', { tripCode: 'T-9' }), 'P1', 'ROUTE', statuses, { confirmedBeyond: true });
assert.deepEqual([r.result.outcome, r.result.voice, r.result.send, r.recorded, r.nextStatus], ['COUNTED', 'found', true, true, null]);
assert.equal(r.result.routeNote.params.trip, 'T-9');

// Salir desde Sometido brinca los de en medio
r = applyScan(order('SUBMITTED'), 'P1', 'DEPART', statuses);
assert.deepEqual([r.result.outcome, r.nextStatus], ['APPLIED', 'IN_TRANSIT']);

// desde un lateral (Intento fallido) «Llegó» lo regresa a Sin asignar
r = applyScan(order('FAILED'), 'P1', 'ARRIVE', statuses);
assert.deepEqual([r.result.outcome, r.nextStatus], ['APPLIED', 'UNASSIGNED']);

// pick up: Llegó lo lleva a En almacén; Enrutar y Salir no valen
const pick = order('SUBMITTED', { pickUp: true });
assert.equal(applyScan(pick, 'P1', 'ARRIVE', statuses).nextStatus, 'IN_WAREHOUSE');
assert.equal(applyScan(pick, 'P1', 'ROUTE', statuses).result.outcome, 'PICKUP_ONLY');
const inWh = order('IN_WAREHOUSE', { pickUp: true });
assert.equal(applyScan(inWh, 'P1', 'ARRIVE', statuses).result.outcome, 'COUNTED');
assert.equal(applyScan(inWh, 'P1', 'ARRIVE', statuses).result.voice, 'found');

// cancelado / terminal: «No» en voz alta
for (const done of ['CANCELLED', 'DELIVERED']) {
  r = applyScan(order(done), 'P1', 'ROUTE', statuses);
  assert.deepEqual([r.result.outcome, r.result.voice, r.result.send], ['FINAL', 'notfound', false]);
}

// cómo se escanea: por ruta / ciudad / chofer
const A = order('UNASSIGNED', { tripPublicId: 'trip-A', tripCode: 'T-A', city: 'San Juan', driverPublicId: 'drv-1' });
const free = order('UNASSIGNED', { city: 'Ponce' });
assert.equal(fold('  Mayagüez '), 'mayaguez');
assert.equal(inScope(A, ALL_SCOPE, 'ROUTE'), true);
assert.equal(inScope(A, { mode: 'ROUTE', ids: ['trip-A', 'trip-B'] }, 'ROUTE'), true);
assert.equal(inScope(A, { mode: 'ROUTE', ids: ['trip-B'] }, 'ROUTE'), false);
assert.equal(inScope(free, { mode: 'ROUTE', ids: ['trip-B'] }, 'ROUTE'), true, 'Enrutar con UNA ruta: un envío libre va a esa ruta');
assert.equal(inScope(free, { mode: 'ROUTE', ids: ['trip-B', 'trip-C'] }, 'ROUTE'), false, 'con varias rutas no hay destino');
assert.equal(inScope(free, { mode: 'ROUTE', ids: ['trip-B'] }, 'DEPART'), false, 'otra acción: debe estar ya en la ruta');
assert.equal(inScope(A, { mode: 'CITY', ids: ['san juan'] }, 'ARRIVE'), true);
assert.equal(inScope(A, { mode: 'CITY', ids: ['Ponce'] }, 'ARRIVE'), false);
assert.equal(inScope(A, { mode: 'DRIVER', ids: ['drv-1'] }, 'DEPART'), true);
assert.equal(inScope(A, { mode: 'DRIVER', ids: ['drv-2'] }, 'DEPART'), false);
assert.equal(inScope(free, { mode: 'DRIVER', ids: ['drv-1'] }, 'DEPART'), false, 'sin ruta no tiene chofer');
assert.equal(destinationTrip({ mode: 'ROUTE', ids: ['trip-B'] }, 'ROUTE'), 'trip-B');
assert.equal(destinationTrip({ mode: 'ROUTE', ids: ['trip-B', 'trip-C'] }, 'ROUTE'), null);
assert.equal(destinationTrip({ mode: 'ROUTE', ids: ['trip-B'] }, 'DEPART'), null);
assert.equal(destinationTrip(ALL_SCOPE, 'ROUTE'), null);

// existe en el sistema pero no es del chofer escogido: «No»
r = applyScan(A, 'P1', 'ROUTE', statuses, { scope: { mode: 'DRIVER', ids: ['drv-2'] } });
assert.deepEqual([r.result.outcome, r.result.voice, r.result.send, r.recorded], ['NOT_IN_SCOPE', 'notfound', false, false]);
assert.equal(r.result.message.key, 'SCAN.MSG_NOT_IN_DRIVER');
// y si es de ese chofer, funciona («Sí» al mover)
r = applyScan(A, 'P1', 'ROUTE', statuses, { scope: { mode: 'DRIVER', ids: ['drv-1'] } });
assert.deepEqual([r.result.outcome, r.result.voice], ['APPLIED', 'found']);
// por ciudad / ruta, mensaje propio
assert.equal(applyScan(A, 'P1', 'ROUTE', statuses, { scope: { mode: 'CITY', ids: ['Ponce'] } }).result.message.key, 'SCAN.MSG_NOT_IN_CITY');
assert.equal(applyScan(A, 'P1', 'ROUTE', statuses, { scope: { mode: 'ROUTE', ids: ['trip-B'] } }).result.message.key, 'SCAN.MSG_NOT_IN_ROUTE');
// fuera de lo escogido gana a «final» y a «adelante»: lo que no es del chofer nunca avisa de su estatus
assert.equal(applyScan(order('IN_TRANSIT', { driverPublicId: 'drv-1', tripPublicId: 'x' }), 'P1', 'ROUTE', statuses, { scope: { mode: 'DRIVER', ids: ['drv-2'] } }).result.outcome, 'NOT_IN_SCOPE');

assert.equal(isServerProblem('APPLIED'), false);
assert.equal(isServerProblem('ALREADY_BEYOND'), false);
assert.equal(isServerProblem('NO_ROUTE'), true);
assert.equal(isServerProblem('NOT_FOUND'), true);
assert.equal(isServerProblem(null), false);

console.log('test-scan: ok');
