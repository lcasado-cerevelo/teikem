// El cerebro de Kemi en el portal: busca en el índice del CLIENTE (solo el manual y la FAQ marcados para el portal; lo interno del personal no está). Es el mismo cerebro (shared/kemi/brain.ts) que
// usa la web del personal con su propio índice; cuando Kemi tenga IA será UNA sola, detrás de un endpoint del servidor que decide el público por la sesión.
import { createSearchBrain } from '../../../../shared/kemi/brain'
import type { HelpEntry } from '../../../../shared/kemi/helpSearch'

export const portalBrain = createSearchBrain('portal', async () => (await import('./helpIndex.json')).default as HelpEntry[])
