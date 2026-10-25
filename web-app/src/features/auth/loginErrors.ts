// Mensajes del inicio de sesión (Lote 219: «mensajes de error claros»). El servidor responde SIEMPRE lo mismo ante un correo que no existe, una contraseña mala o una cuenta bloqueada («Credenciales inválidas.»,
// a propósito: no se le dice a un desconocido qué correos existen), así que aquí se traduce a un texto que le dice a la persona QUÉ revisar y qué hacer, en su idioma. Lo demás se explica por su código.
import type { AppliedProblem } from '../../kernel/api/problem'

type T = (key: string) => string

export function loginErrorText(problem: Pick<AppliedProblem, 'title' | 'code'>, t: T): string {
  switch (problem.code) {
    case 'unauthorized':
      return t('auth.errors.invalidCredentials')
    case 'too_many_requests':
      return t('auth.errors.tooManyRequests')
    case 'internal':
      return t('auth.errors.serverDown')
    default:
      // network ya viene traducido; lo demás es un mensaje propio del servidor (ej. «Confirme que no es un robot»)
      return problem.title
  }
}
