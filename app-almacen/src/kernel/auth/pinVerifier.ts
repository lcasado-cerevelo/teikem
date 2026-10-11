// 2026-10-11 — Desbloqueo sin señal. El PIN nunca se guarda tal cual: al entrar con señal se guarda aquí una huella con sal
// (SHA-256 repetido) en expo-secure-store (cifrado por el sistema, igual que el secreto del aparato y los tokens), y la pantalla
// de bloqueo la usa SOLO cuando el servidor no responde. Con señal, el servidor sigue siendo quien valida el PIN.
// Tope de intentos fallidos sin señal (OFFLINE_MAX_FAILS): al llegar, hace falta señal para desbloquear (o cerrar sesión).
import * as SecureStore from 'expo-secure-store'

export const OFFLINE_MAX_FAILS = 5
const ROUNDS = 2000

const K = new Uint32Array([
  0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5, 0xd807aa98, 0x12835b01, 0x243185be,
  0x550c7dc3, 0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174, 0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa,
  0x5cb0a9dc, 0x76f988da, 0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967, 0x27b70a85,
  0x2e1b2138, 0x4d2c6dfc, 0x53380d13, 0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85, 0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3,
  0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070, 0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f,
  0x682e6ff3, 0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208, 0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2,
])

const rotr = (x: number, n: number) => (x >>> n) | (x << (32 - n))

/** SHA-256 en JavaScript puro (Hermes no trae crypto.subtle). Entrada y salida: bytes. */
export function sha256(data: Uint8Array): Uint8Array {
  const h = new Uint32Array([0x6a09e667, 0xbb67ae85, 0x3c6ef372, 0xa54ff53a, 0x510e527f, 0x9b05688c, 0x1f83d9ab, 0x5be0cd19])
  const padded = new Uint8Array(((data.length + 9 + 63) >> 6) << 6)
  padded.set(data)
  padded[data.length] = 0x80
  const view = new DataView(padded.buffer)
  view.setUint32(padded.length - 8, Math.floor((data.length * 8) / 0x100000000))
  view.setUint32(padded.length - 4, (data.length * 8) >>> 0)
  const w = new Uint32Array(64)
  for (let off = 0; off < padded.length; off += 64) {
    for (let i = 0; i < 16; i++) w[i] = view.getUint32(off + i * 4)
    for (let i = 16; i < 64; i++) {
      const s0 = rotr(w[i - 15], 7) ^ rotr(w[i - 15], 18) ^ (w[i - 15] >>> 3)
      const s1 = rotr(w[i - 2], 17) ^ rotr(w[i - 2], 19) ^ (w[i - 2] >>> 10)
      w[i] = (w[i - 16] + s0 + w[i - 7] + s1) >>> 0
    }
    let a = h[0]
    let b = h[1]
    let c = h[2]
    let d = h[3]
    let e = h[4]
    let f = h[5]
    let g = h[6]
    let hh = h[7]
    for (let i = 0; i < 64; i++) {
      const t1 = (hh + (rotr(e, 6) ^ rotr(e, 11) ^ rotr(e, 25)) + ((e & f) ^ (~e & g)) + K[i] + w[i]) >>> 0
      const t2 = ((rotr(a, 2) ^ rotr(a, 13) ^ rotr(a, 22)) + ((a & b) ^ (a & c) ^ (b & c))) >>> 0
      hh = g
      g = f
      f = e
      e = (d + t1) >>> 0
      d = c
      c = b
      b = a
      a = (t1 + t2) >>> 0
    }
    h[0] += a
    h[1] += b
    h[2] += c
    h[3] += d
    h[4] += e
    h[5] += f
    h[6] += g
    h[7] += hh
  }
  const out = new Uint8Array(32)
  const ov = new DataView(out.buffer)
  for (let i = 0; i < 8; i++) ov.setUint32(i * 4, h[i])
  return out
}

const toHex = (b: Uint8Array) => Array.from(b, (x) => x.toString(16).padStart(2, '0')).join('')
const utf8 = (s: string) => Uint8Array.from(unescape(encodeURIComponent(s)), (c) => c.charCodeAt(0))

function digest(salt: string, pin: string): string {
  let cur: Uint8Array = utf8(`${salt}:${pin}`)
  for (let i = 0; i < ROUNDS; i++) cur = sha256(cur)
  return toHex(cur)
}

function randomSalt(): string {
  let s = ''
  for (let i = 0; i < 4; i++) s += Math.floor(Math.random() * 0x100000000).toString(16).padStart(8, '0')
  return s
}

const keyFor = (devicePublicId: string, userId: number) => `teikem.pin.verifier.${devicePublicId.replace(/[^A-Za-z0-9._-]/g, '')}.${userId}`

interface Stored {
  salt: string
  hash: string
  fails: number
}

/** Guarda la huella del PIN recién validado por el servidor (se reinicia el contador de fallos). */
export async function saveOfflinePinVerifier(devicePublicId: string, userId: number, pin: string): Promise<void> {
  const salt = randomSalt()
  const stored: Stored = { salt, hash: digest(salt, pin.trim()), fails: 0 }
  await SecureStore.setItemAsync(keyFor(devicePublicId, userId), JSON.stringify(stored))
}

export async function clearOfflinePinVerifier(devicePublicId: string, userId: number): Promise<void> {
  await SecureStore.deleteItemAsync(keyFor(devicePublicId, userId))
}

export type OfflinePinResult = 'ok' | 'wrong' | 'blocked' | 'none'

/** Revisa el PIN contra la huella guardada. 'none' = nunca se entró con señal en este aparato (no se puede desbloquear sin señal);
 *  'blocked' = ya se agotaron los intentos sin señal. */
export async function checkOfflinePin(devicePublicId: string, userId: number, pin: string): Promise<OfflinePinResult> {
  const key = keyFor(devicePublicId, userId)
  const raw = await SecureStore.getItemAsync(key)
  if (!raw) return 'none'
  let stored: Stored
  try {
    stored = JSON.parse(raw) as Stored
  } catch {
    return 'none'
  }
  if (stored.fails >= OFFLINE_MAX_FAILS) return 'blocked'
  if (digest(stored.salt, pin.trim()) === stored.hash) {
    if (stored.fails > 0) await SecureStore.setItemAsync(key, JSON.stringify({ ...stored, fails: 0 }))
    return 'ok'
  }
  const fails = stored.fails + 1
  await SecureStore.setItemAsync(key, JSON.stringify({ ...stored, fails }))
  return fails >= OFFLINE_MAX_FAILS ? 'blocked' : 'wrong'
}
