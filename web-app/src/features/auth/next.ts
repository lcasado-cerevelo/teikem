/** Destino seguro tras el login: solo rutas internas (evita redirecciones abiertas). */
export function safeNext(next: string | null): string {
  return next && next.startsWith('/') && !next.startsWith('//') ? next : '/'
}
