// Prueba de las reglas puras del seguimiento de ruta. Uso: npm run test:tracking (Node 22+).
import { shouldKeep, pushBounded, chunk, DEFAULT_TRACKING_CONFIG as C, MAX_BUFFER } from '../src/tracking/trackingCore.ts';
import assert from 'node:assert/strict';
const t0 = Date.parse('2026-10-23T15:00:00Z');
const last = { latitude: 18.4, longitude: -66.1, capturedAtUtc: new Date(t0).toISOString() };
assert.equal(shouldKeep(null, 18.4, -66.1, t0, C), true);
assert.equal(shouldKeep(last, 18.4, -66.1, t0 + 5 * 60_000, C), false, 'quieto: nada aunque pase el tiempo');
assert.equal(shouldKeep(last, 18.4003, -66.1, t0 + 10_000, C), false, '33 m en 10 s: ni distancia ni tiempo');
assert.equal(shouldKeep(last, 18.4003, -66.1, t0 + 100_000, C), true, 'se mueve (33 m) y pasaron 90 s');
assert.equal(shouldKeep(last, 18.402, -66.1, t0 + 5_000, C), true, '222 m: por distancia');
assert.equal(shouldKeep(last, 0, 0, t0, C), false);
let b = []; for (let i = 0; i < MAX_BUFFER + 5; i++) b = pushBounded(b, { latitude: 1, longitude: 1, capturedAtUtc: String(i) });
assert.equal(b.length, MAX_BUFFER); assert.equal(b[0].capturedAtUtc, '5');
assert.deepEqual(chunk([1,2,3,4,5], 2), [[1,2],[3,4],[5]]);
console.log('trackingCore OK');
