// El «cerebro» de Kemi (Lote 218): UNA sola interfaz que usan la web del personal y el portal del cliente. Hoy el cerebro BUSCA en el manual y la FAQ (sin IA); el día que Kemi
// tenga inteligencia artificial, esa IA implementa esta misma interfaz detrás de UN solo endpoint del servidor (el público —personal o portal— lo decide el servidor por la sesión,
// nunca el cliente), y las dos pantallas de Kemi la usan sin cambiar: ni caja de pregunta, ni micrófono, ni globos. Lo que cambia entre personal y portal es el índice (audience).
import { buildIndex, type HelpEntry, type HelpIndex } from './helpSearch'

/** Quién pregunta: el personal de la compañía (web) o una persona del portal del cliente. Cada uno tiene su propio índice de ayuda. */
export type KemiAudience = 'staff' | 'portal'

export interface KemiQuestion {
  text: string
  /** Idioma de la pantalla (el manual está en español; en inglés se busca con el glosario). */
  lang: string
  /** Temas de la pantalla donde está la persona: lo documentado de ella sube en el ranking. */
  topics: string[]
  /** Lo que ya se le mostró (para «no me sirvió, busca por otro lado»). */
  exclude?: ReadonlySet<HelpEntry>
  /** Cuántas opciones traer. */
  limit?: number
}

export interface KemiBrain {
  readonly audience: KemiAudience
  /** Hasta `limit` opciones (5 por omisión), de la mejor a la peor; vacío = Kemi no sabe. */
  ask(q: KemiQuestion): Promise<HelpEntry[]>
}

/** El cerebro de hoy: busca las palabras de la pregunta en el índice de su público. El índice se carga la primera vez que alguien pregunta (y se vuelve a intentar si falló). */
export function createSearchBrain(audience: KemiAudience, loadEntries: () => Promise<HelpEntry[]>): KemiBrain {
  let index: Promise<HelpIndex> | null = null
  const load = () => {
    index ??= loadEntries().then(buildIndex)
    index.catch(() => (index = null))
    return index
  }
  return {
    audience,
    async ask(q) {
      const idx = await load()
      return idx.search(q.text, q.limit ?? 5, q.exclude, q.topics).hits.map((h) => h.entry)
    },
  }
}
