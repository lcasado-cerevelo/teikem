// Idioma de la interfaz (es/en): diccionarios en JSON, `t('clave', { param })`, cambio sin reiniciar. Sin dependencias de React (la cabecera Accept-Language del API lee el idioma de aquí).
import { StoreKeys, storeGet, storeSet } from '../storage'
import en from './en.json'
import es from './es.json'

export type Lang = 'es' | 'en'
export const LANGS: readonly Lang[] = ['es', 'en']
export type TParams = Record<string, string | number>

type Dict = { [key: string]: string | Dict }
const DICTS: Record<Lang, Dict> = { es: es as Dict, en: en as Dict }

let current: Lang = 'es'
const listeners = new Set<() => void>()

export function getLang(): Lang {
  return current
}

export function subscribeLang(listener: () => void): () => void {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

/** Carga el idioma guardado (al arrancar). */
export async function initLang(): Promise<void> {
  const stored = await storeGet(StoreKeys.lang)
  if ((stored === 'es' || stored === 'en') && stored !== current) {
    current = stored
    listeners.forEach((l) => l())
  }
}

export function setLang(lang: Lang): void {
  if (lang === current) return
  current = lang
  void storeSet(StoreKeys.lang, lang)
  listeners.forEach((l) => l())
}

function lookup(dict: Dict, key: string): string | undefined {
  let node: string | Dict | undefined = dict
  for (const part of key.split('.')) {
    if (node === undefined || typeof node === 'string') return undefined
    node = node[part]
  }
  return typeof node === 'string' ? node : undefined
}

/** Traduce `key`: primero el idioma pedido, luego español; si falta en ambos devuelve la clave (se nota en pantalla). `{nombre}` se sustituye con `params`. */
export function translate(lang: Lang, key: string, params?: TParams): string {
  const text = lookup(DICTS[lang], key) ?? lookup(DICTS.es, key) ?? key
  if (!params) return text
  return text.replace(/\{(\w+)\}/g, (_, name: string) => (name in params ? String(params[name]) : `{${name}}`))
}

export function t(key: string, params?: TParams): string {
  return translate(current, key, params)
}
