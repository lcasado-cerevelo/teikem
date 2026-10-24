// Prueba de las reglas puras del chat del chofer y de la detección de enlaces. Uso: npm run test:chat (Node 22+).
import assert from 'node:assert/strict';
import { splitLinks, toDialable } from '../src/utils/links.ts';
import { canSend, findShipmentForCard, MAX_PHOTOS, previewOf, sortThreads } from '../src/utils/chatRules.ts';

const kinds = (s) => splitLinks(s).map((p) => p.type);
const hrefs = (s) => splitLinks(s).filter((p) => p.type !== 'text').map((p) => p.href);

assert.deepEqual(kinds('hola'), ['text']);
assert.deepEqual(hrefs('llámame al 787-555-1234 ahora'), ['tel:+17875551234']);
assert.deepEqual(hrefs('(787) 555 1234'), ['tel:+17875551234']);
assert.deepEqual(hrefs('7875551234'), ['tel:+17875551234']);
assert.deepEqual(hrefs('+1 787 555 1234.'), ['tel:+17875551234'], 'el punto final no es del teléfono');
assert.deepEqual(hrefs('mira https://teikem.com/ruta?id=3, gracias'), ['https://teikem.com/ruta?id=3']);
assert.deepEqual(hrefs('www.teikem.com.'), ['https://www.teikem.com']);
assert.deepEqual(hrefs('escribe a ana@empresa.com o llama 787-555-9999'), ['mailto:ana@empresa.com', 'tel:+17875559999']);
assert.deepEqual(kinds('orden 12345'), ['text'], 'un número corto no es un teléfono');
assert.deepEqual(kinds('entrega 2026-0001 lista'), ['text'], 'un código con guiones no es un teléfono');
assert.equal(splitLinks('a 787-555-1234 b').map((p) => p.text).join(''), 'a 787-555-1234 b', 'el texto se conserva completo');
assert.equal(toDialable('787.555.1234'), '+17875551234');
assert.equal(toDialable('1-787-555-1234'), '+17875551234');
assert.equal(toDialable('+44 20 7946 0958'), '+442079460958');

assert.equal(previewOf('  ', 'Sin mensajes'), 'Sin mensajes');
assert.equal(previewOf('hola', 'x'), 'hola');
const sorted = sortThreads([{ lastMessageAtUtc: '2026-10-23T10:00:00Z', n: 'a' }, { lastMessageAtUtc: '2026-10-23T12:00:00Z', n: 'b' }]);
assert.deepEqual(sorted.map((t) => t.n), ['b', 'a']);
assert.equal(canSend('', 0), false);
assert.equal(canSend('  ', 0), false);
assert.equal(canSend('hola', 0), true);
assert.equal(canSend('', 1), true);
assert.equal(canSend('', MAX_PHOTOS + 1), false);
assert.equal(findShipmentForCard([{ orderNumber: 'A-1', tenantId: 1 }, { orderNumber: 'A-1', tenantId: 2 }], 2, 'A-1')?.tenantId, 2, 'el mismo número en otra compañía no es el mismo envío');
assert.equal(findShipmentForCard([{ orderNumber: 'A-1', tenantId: 1 }], 1, 'B-9'), undefined);
console.log('chatCore OK');
