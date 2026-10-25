// Mensajes del inicio de sesión del portal (Lote 219: «mensajes de error claros»). El servidor responde SIEMPRE lo mismo ante un correo que no existe, una contraseña mala o una cuenta bloqueada («Credenciales
// inválidas.», a propósito: no se le dice a un desconocido qué correos existen), así que aquí se traduce a un texto que le dice a la persona QUÉ revisar y qué hacer, en su idioma.
import { ApiError, apiErrorMessage } from './problem'

type T = (key: string) => string

export function loginErrorMessage(error: unknown, t: T): string {
  if (error instanceof ApiError) {
    switch (error.code) {
      case 'unauthorized':
        return t('login.errors.invalid')
      case 'rate_limited':
        return t('login.errors.tooMany')
      case 'internal':
        return t('login.errors.server')
      case 'network':
        return t('errors.network')
    }
  }
  return apiErrorMessage(error)
}
