// Marca secreta de ESTA computadora (navegador) para las «computadoras de confianza» (Lote 100). La crea la web la primera vez y la manda en cada ingreso; el servidor solo guarda su resumen.
// Si el navegador no deja guardar (modo privado, datos bloqueados) vive en memoria mientras la pestaña esté abierta: en ese caso cada ingreso nuevo pedirá confirmar.
const KEY = 'teikem.browser'
let memory: string | null = null

function randomHex(bytes: number): string {
  const buf = new Uint8Array(bytes)
  if (globalThis.crypto?.getRandomValues) globalThis.crypto.getRandomValues(buf)
  else for (let i = 0; i < bytes; i++) buf[i] = Math.floor(Math.random() * 256)
  return Array.from(buf, (b) => b.toString(16).padStart(2, '0')).join('')
}

/** La marca de esta computadora (64 caracteres hexadecimales); se crea si no existe. */
export function getBrowserToken(): string {
  try {
    const saved = localStorage.getItem(KEY)
    if (saved && saved.length >= 32) return saved
    const fresh = randomHex(32)
    localStorage.setItem(KEY, fresh)
    return fresh
  } catch {
    memory ??= randomHex(32)
    return memory
  }
}
