// Prueba de las reglas puras del escaneo local sin señal (Lote 216). Uso: npm run test:scan (Node 22+).
import assert from 'node:assert/strict';
import { ambiguous, applyScan, isServerProblem, normalizeCode, notFound, targetStatus } from '../src/utils/scanRules.ts';

const S = (code, sortOrder, stageKind = 'PIPELINE', isInitial = false) => ({ code, label: code.toLowerCase(), sortOrder, stageKind, isInitial, isEnabled: true });
const statuses = [S('SUBMITTED', 1, 'PIPELINE', true), S('PICKUP', 2), S('UNASSIGNED', 3), S('ASSIGNED', 4), S('IN_TRANSIT', 5), S('DELIVERED', 8, 'TERMINAL'), S('IN_WAREHOUSE', 9, 'TERMINAL'), S('FAILED', 22, 'LATERAL')];
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
assert.equal(r.nextStatus, null);
assert.deepEqual([r.result.scanned, r.result.total], [2, 2]);
assert.equal(r.result.routeNote.key, 'SCAN.ROUTE_ZONE');

// duplicado: Ok, no se manda
r = applyScan(o, 'P1', 'ROUTE', statuses);
assert.deepEqual([r.result.outcome, r.result.voice, r.result.send, r.recorded], ['DUPLICATE', 'dup', false, false]);

// no existe la pieza
assert.equal(applyScan(o, 'X', 'ROUTE', statuses).result.outcome, 'NOT_FOUND');

// final: se ignora
r = applyScan(order('DELIVERED'), 'P1', 'DEPART', statuses);
assert.deepEqual([r.result.outcome, r.result.send], ['FINAL', false]);

// ya más adelante: solo cuenta (Enrutar sobre uno En tránsito)
r = applyScan(order('IN_TRANSIT', { tripCode: 'T-9' }), 'P1', 'ROUTE', statuses);
assert.equal(r.result.outcome, 'COUNTED');
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

assert.equal(isServerProblem('APPLIED'), false);
assert.equal(isServerProblem('ALREADY_BEYOND'), false);
assert.equal(isServerProblem('NO_ROUTE'), true);
assert.equal(isServerProblem('NOT_FOUND'), true);
assert.equal(isServerProblem(null), false);

console.log('test-scan: ok');
