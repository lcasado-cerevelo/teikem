// Lote 248 — aviso de la exención de IVU del cliente al vender (GET /api/v1/sales-orders/tax-notice?client=): no bloquea nada. El servidor manda el nivel y los datos
// (fecha, días, certificado); el texto se arma en la web con t() para que salga en el idioma de la interfaz.
import { useQuery } from '@tanstack/react-query'
import { api, unwrap } from '../../../kernel/api/client'
import type { components } from '../../../kernel/api/schema'

export type SalesTaxNoticeDto = components['schemas']['SalesTaxNoticeDto']

export const taxNoticeKeys = { one: (client: string) => ['/api/v1/sales-orders/tax-notice', client] as const }

export function useTaxNotice(clientPublicId: string | null | undefined) {
  return useQuery({
    queryKey: taxNoticeKeys.one(clientPublicId ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/sales-orders/tax-notice', { params: { query: { client: clientPublicId ?? undefined } } })),
    enabled: !!clientPublicId,
    meta: { handleAccessDenied: false },
  })
}
