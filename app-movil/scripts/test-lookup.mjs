// Prueba de las reglas puras de las consultas del personal (Lote 217). Uso: npm run test:lookup (Node 22+).
import assert from 'node:assert/strict';
import { addressLine, contactHref, contactText, groupStock, looksLikeCode, mapsUrl, qty, toClientFicha, usableContacts } from '../src/utils/lookupRules.ts';

assert.equal(addressLine({ line1: 'Calle 1', line2: ' ', city: 'Ponce', state: 'PR', postalCode: '00716' }), 'Calle 1, Ponce, PR 00716');
assert.equal(addressLine({ line1: 'Calle 1', city: 'Ponce' }), 'Calle 1, Ponce');
assert.equal(addressLine({}), '');
assert.equal(mapsUrl('  '), null);
assert.equal(mapsUrl('Calle 1, Ponce'), 'https://www.google.com/maps/search/?api=1&query=Calle%201%2C%20Ponce');

assert.equal(contactHref({ contactType: 'EMAIL', value: 'a@b.com' }), 'mailto:a@b.com');
assert.equal(contactHref({ contactType: 'PHONE', value: '(787) 555-1234' }), 'tel:+17875551234');
assert.equal(contactHref({ contactType: 'MOBILE', value: '787-555-1234', countryCallingCode: '1' }), 'tel:+17875551234');
assert.equal(contactHref({ contactType: 'PHONE', value: '+34 912 345 678' }), 'tel:+34912345678');
assert.equal(contactHref({ contactType: 'WHATSAPP', value: '7875551234' }), 'https://wa.me/17875551234');
assert.equal(contactHref({ contactType: 'PHONE', value: '123' }), null, 'un número corto no se marca');
assert.equal(contactHref({ contactType: 'OTHER', value: '7875551234' }), null);
assert.equal(contactHref({ contactType: 'EMAIL', value: ' ' }), null);

assert.equal(contactText({ contactType: 'PHONE', value: '787-555-1234', extension: '2' }, 'Teléfono'), 'Teléfono: 787-555-1234 ext. 2');
assert.equal(contactText({ contactType: 'PHONE', value: '787', label: 'Oficina' }, 'Teléfono'), 'Oficina: 787');
assert.equal(contactText({ contactType: 'PHONE', value: '787' }), '787');

const list = usableContacts([{ contactType: 'PHONE', value: '1', isActive: false }, { contactType: 'PHONE', value: '2' }, { contactType: 'PHONE', value: '3', isPrimary: true }]);
assert.deepEqual(list.map((c) => c.value), ['3', '2']);

// la ficha del cliente NUNCA lleva crédito, identificación fiscal, pago ni contratos aunque el servidor los mande
const ficha = toClientFicha({
  publicId: 'p1', code: 'C-1', name: 'Acme', legalName: 'Acme Inc', statusLabel: 'Activo', isActive: true,
  physicalAddress: { line1: 'Calle 1', city: 'Ponce', postalCode: '00716' },
  contacts: [{ fullName: 'Ana', role: 'Compras', isPrimary: true, contactPoints: [{ contactType: 'EMAIL', value: 'ana@acme.com' }] }, { fullName: 'Baja', isActive: false }],
  contactPoints: [{ contactType: 'PHONE', value: '787-555-1234' }],
  // @ts-expect-error campos que el servidor manda y la app descarta
  creditLimit: 5000, taxId: '66-123', paymentTerm: 'NET30', contracts: [{}],
});
assert.equal(ficha.address, 'Calle 1, Ponce 00716');
assert.equal(ficha.contacts.length, 1);
assert.equal(ficha.contacts[0].points[0].href, 'mailto:ana@acme.com');
assert.equal(ficha.points[0].href, 'tel:+17875551234');
const keys = JSON.stringify(ficha);
for (const secret of ['5000', '66-123', 'NET30', 'creditLimit', 'taxId']) assert.ok(!keys.includes(secret), `no debe salir ${secret}`);

const stock = groupStock([
  { warehouseCode: 'B', binCode: 'A-1', lotNumber: null, qtyOnHand: 5, qtyReserved: 1, qtyAvailable: 4 },
  { warehouseCode: 'A', binCode: 'X-1', lotNumber: 'L1', qtyOnHand: 2, qtyReserved: 0, qtyAvailable: 2 },
  { warehouseCode: 'B', binCode: 'A-2', lotNumber: null, qtyOnHand: 3, qtyReserved: 0, qtyAvailable: 3 },
  { warehouseCode: 'C', binCode: 'Z', lotNumber: null, qtyOnHand: 0, qtyReserved: 0, qtyAvailable: 0 },
]);
assert.deepEqual(stock.map((w) => w.warehouseCode), ['A', 'B'], 'ordenado y sin los almacenes en cero');
assert.equal(stock[1].onHand, 8);
assert.equal(stock[1].available, 7);
assert.equal(stock[1].bins.length, 2);

assert.equal(qty(12), '12');
assert.equal(qty(2.5), '2.5');
assert.equal(qty(0.1 + 0.2), '0.3');

assert.equal(looksLikeCode('7501234567890'), true);
assert.equal(looksLikeCode('caja grande'), false);
assert.equal(looksLikeCode('ab'), false);

console.log('test-lookup: ok');
