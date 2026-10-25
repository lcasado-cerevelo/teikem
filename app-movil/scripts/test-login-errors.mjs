// Prueba de los mensajes de inicio de sesión (Lote 219). Uso: npm run test:login (Node 22+).
import assert from 'node:assert/strict';
import { KEY_BY_KIND, loginErrorKind } from '../src/utils/loginErrors.ts';

assert.equal(loginErrorKind(undefined), 'network');
assert.equal(loginErrorKind(0), 'network');
assert.equal(loginErrorKind(401), 'invalid');
assert.equal(loginErrorKind(429), 'tooMany');
assert.equal(loginErrorKind(500), 'server');
assert.equal(loginErrorKind(503), 'server');
assert.equal(loginErrorKind(403), 'other', 'el 403 sin acceso usa el mensaje del servidor');
assert.equal(loginErrorKind(400), 'other');

for (const [kind, keys] of Object.entries(KEY_BY_KIND)) {
  assert.ok(keys.title.startsWith('LOGIN.') && keys.body.startsWith('LOGIN.'), `${kind} usa claves de LOGIN`);
}
console.log('test-login-errors: ok');
