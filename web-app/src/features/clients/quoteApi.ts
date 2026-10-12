// Cotizador del cliente (F-A3, manual 02 cap. 5.5): POST /api/v1/billing/contract-rate-quote (contracts.read, módulo CATALOG).
// No crea nada: solo calcula. Cada intento lleva su propia Idempotency-Key.
import { useMutation } from '@tanstack/react-query'
import { api, unwrap } from '../../kernel/api/client'
import type { RateQuoteRequest } from './quoteRules'

export function useQuote() {
  return useMutation({
    mutationFn: (body: RateQuoteRequest) => unwrap(api.POST('/api/v1/billing/contract-rate-quote', { body, headers: { 'Idempotency-Key': crypto.randomUUID() } })),
  })
}
