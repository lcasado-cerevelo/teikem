// Diccionarios y estado del idioma de la interfaz. Sin dependencias de React: el cliente del API lee el idioma
// de aquí para la cabecera Accept-Language, y los componentes se suscriben con useT()/useLang().
import en from './en.json'
import es from './es.json'
import { formatQuantity } from './numberFormat'

export type Lang = 'es' | 'en'
export const LANGS: readonly Lang[] = ['es', 'en']
export type TParams = Record<string, string | number>

type Dict = { [key: string]: string | Dict }

const DICTS: Record<Lang, Dict> = { es: es as Dict, en: en as Dict }
const STORAGE_KEY = 'teikem.lang'

function initialLang(): Lang {
  try {
    const stored = globalThis.localStorage?.getItem(STORAGE_KEY)
    if (stored === 'es' || stored === 'en') return stored
  } catch {
    // almacenamiento no disponible: se usa el idioma del navegador
  }
  const nav = globalThis.navigator?.language ?? 'es'
  return nav.toLowerCase().startsWith('en') ? 'en' : 'es'
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
  try {
    globalThis.localStorage?.setItem(STORAGE_KEY, lang)
  } catch {
    // sin almacenamiento: el cambio vale para esta pestaña
  }
  if (typeof document !== 'undefined') document.documentElement.lang = lang
  listeners.forEach((l) => l())
}

export function subscribeLang(listener: () => void): () => void {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

/** true si el idioma guardado lo eligió el usuario (tiene prioridad sobre el del servidor). */
export function hasStoredLang(): boolean {
  try {
    return globalThis.localStorage?.getItem(STORAGE_KEY) != null
  } catch {
    return false
  }
}

function lookup(dict: Dict, key: string): string | undefined {
  const flat = dict[key]
  if (typeof flat === 'string') return flat
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
 * (visible en pantalla para que se note) y avisa una vez en consola en desarrollo. `{nombre}` se sustituye con params.
 */
export function translate(lang: Lang, key: string, params?: TParams): string {
  let text = lookup(DICTS[lang], key) ?? lookup(DICTS.es, key)
  if (text === undefined) {
    if (import.meta.env?.DEV && !warned.has(key)) {
      warned.add(key)
      console.warn(`[i18n] clave faltante: ${key}`)
    }
    text = key
  }
  if (params) text = text.replace(/\{(\w+)\}/g, (m, name: string) => (name in params ? paramText(name, params[name], lang) : m))
  return text
}

/** Parámetros que son identificadores (número de documento, id, código): se pintan tal cual, sin coma de miles. */
const RAW_NUMBER_PARAMS = new Set(['id', 'number', 'code', 'order', 'ref', 'serial', 'lot', 'sku'])

/** Un número en un texto lleva coma de miles ("1,250 pendientes"), salvo que el parámetro sea un identificador. */
function paramText(name: string, value: TParams[string], lang: Lang): string {
  if (typeof value === 'number' && Number.isFinite(value) && !RAW_NUMBER_PARAMS.has(name)) return formatQuantity(value, lang)
  return String(value)
}

/** Traducción con el idioma actual. Para fuera de React (mensajes del kernel); en componentes usa useT(). */
export function t(key: string, params?: TParams): string {
  return translate(current, key, params)
}
