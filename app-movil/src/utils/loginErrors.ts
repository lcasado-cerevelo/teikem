// Qué le decimos a la persona cuando no pudo entrar (Lote 219: «mensajes de error claros»). El servidor responde SIEMPRE lo mismo ante un correo que no existe, una contraseña mala o una cuenta
// bloqueada («Credenciales inválidas.», a propósito: no se le dice a un desconocido qué correos existen), así que aquí se decide por el estado HTTP y se le dice qué revisar, en su idioma.

export type LoginErrorKind = 'network' | 'invalid' | 'tooMany' | 'server' | 'other';

/** `status` undefined = no hubo respuesta (sin señal o servidor caído). */
export function loginErrorKind(status: number | undefined): LoginErrorKind {
  if (status === undefined || status === 0) return 'network';
  if (status === 401) return 'invalid';
  if (status === 429) return 'tooMany';
  if (status >= 500) return 'server';
  return 'other';
}

/** El mensaje propio del servidor (403 sin acceso, «Confirme que no es un robot…») solo se usa cuando no es uno de los casos de arriba. */
export const KEY_BY_KIND: Record<Exclude<LoginErrorKind, 'other'>, { title: string; body: string }> = {
  network: { title: 'LOGIN.NETWORK_ERROR_TITLE', body: 'LOGIN.NETWORK_ERROR' },
  invalid: { title: 'LOGIN.ERROR_TITLE', body: 'LOGIN.INVALID_CREDENTIALS' },
  tooMany: { title: 'LOGIN.ERROR_TITLE', body: 'LOGIN.TOO_MANY' },
  server: { title: 'LOGIN.ERROR_TITLE', body: 'LOGIN.SERVER_ERROR' },
};
