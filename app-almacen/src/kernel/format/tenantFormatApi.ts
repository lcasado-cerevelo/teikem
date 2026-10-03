// Trae la región y los formatos de la compañía (`GET /api/v1/tenant/settings`, solo sesión de usuario) y los guarda en la
// base local (store.ts). Lo llaman la entrada con PIN (kernel/auth/deviceAuth.ts) y cada pasada de sincronización con
// señal (kernel/sync/engine.ts). Un fallo (sin señal, servidor viejo sin esos campos) no rompe nada: se siguen usando los
// guardados o, si nunca llegaron, los de Puerto Rico.
import { api, unwrap } from '../api/client'
import { getSessionState } from '../auth/session'
import { toFormatSettings } from './settings'
import { getFormatSettings, setFormatSettings } from './store'

/** true si los trajo y los guardó; false si no había sesión o la llamada falló. */
export async function refreshTenantFormat(): Promise<boolean> {
  const { device, session } = getSessionState()
  if (!device || !session) return false
  try {
    const dto = await unwrap(api.GET('/api/v1/tenant/settings'))
    // campo ausente o inválido → se conserva el valor vigente de ese campo (no se vuelve a Puerto Rico a medias)
    setFormatSettings(toFormatSettings(dto, getFormatSettings()))
    return true
  } catch {
    return false
  }
}
