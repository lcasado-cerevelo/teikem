import { ApiError } from '../../kernel/api/problem'

/** 403 `forbidden` o `module_disabled` en una consulta secundaria (se avisa sin sacar al usuario de la pantalla). */
export function isAccessDenied(error: unknown): boolean {
  return error instanceof ApiError && (error.code === 'forbidden' || error.code === 'module_disabled')
}
