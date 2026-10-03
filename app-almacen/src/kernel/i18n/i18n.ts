// Diccionarios y estado del idioma de la interfaz (mismo patrón que web-app/src/kernel/i18n/i18n.ts, adaptado a la base
// local en vez de localStorage). Sin dependencias de React: kernel/api/client.ts lee el idioma de aquí para la cabecera
// Accept-Language, y los componentes se suscriben con useT() (i18n/useT.ts).
import { getKv, KvKeys, setKv } from '../db/kv'
import en from './en.json'
import es from './es.json'
import { formatQuantity } from '../format/format'

export type Lang = 'es' | 'en'
export const LANGS: readonly Lang[] = ['es', 'en']
export type TParams = Record<string, string | number>

type Dict = { [key: string]: string | Dict }

const DICTS: Record<Lang, Dict> = { es: es as Dict, en: en as Dict }

/** Parámetros que son identificadores (número de documento, id, código): se pintan tal cual, sin separador de miles. */
export const RAW_NUMBER_PARAMS: ReadonlySet<string> = new Set(['id', 'number', 'code', 'order', 'ref', 'serial', 'lot', 'sku'])

function initialLang(): Lang {
  const stored = getKv(KvKeys.lang)
  return stored === 'es' || stored === 'en' ? stored : 'es'
}

let current: Lang = initialLang()
const listeners = new Set<() => void>()

export function getLang(): Lang {
  return current
}

/** Cambia el idioma: solo el diccionario (y la cabecera del API). No recarga ni desmonta pantallas. */
export function setLang(lang: Lang): void {
  if (lang === current) return
  current = lang
  setKv(KvKeys.lang, lang)
  listeners.forEach((l) => l())
}

export function subscribeLang(listener: () => void): () => void {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

function lookup(dict: Dict, key: string): string | undefined {
  let node: string | Dict | undefined = dict
  for (const part of key.split('.')) {
    if (node === undefined || typeof node === 'string') return undefined
    node = node[part]
  }
  return typeof node === 'string' ? node : undefined
}

const warned = new Set<string>()

/**
 * Traduce `key` en `lang`: busca en el idioma pedido, luego en español; si falta en ambos devuelve la clave tal cual
 * (visible en pantalla para que se note) y avisa una vez por consola. `{nombre}` se sustituye con params.
 */
export function translate(lang: Lang, key: string, params?: TParams): string {
  let text = lookup(DICTS[lang], key) ?? lookup(DICTS.es, key)
  if (text === undefined) {
    if (!warned.has(key)) {
      warned.add(key)
      console.warn(`[i18n] clave faltante: ${key}`)
    }
    text = key
  }
  if (params) text = text.replace(/\{(\w+)\}/g, (m, name: string) => (name in params ? paramText(name, params[name]) : m))
  return text
}

/** Un número en un texto lleva los separadores de la compañía ("1,250 unidades" en Puerto Rico), salvo que el parámetro sea
 *  un identificador. Región y formatos: los separadores salen de kernel/format (antes, coma de miles fija). */
function paramText(name: string, value: TParams[string]): string {
  return typeof value === 'number' && !RAW_NUMBER_PARAMS.has(name) ? formatQuantity(value) : String(value)
}

/** Traducción con el idioma actual. Fuera de React (kernel); en componentes usa useT(). */
export function t(key: string, params?: TParams): string {
  return translate(current, key, params)
}

/** Solo para pruebas: reinicia el estado en memoria del módulo (kv ya se limpia con __resetDbForTests). */
export function __resetLangForTests(): void {
  current = initialLang()
  listeners.clear()
}
