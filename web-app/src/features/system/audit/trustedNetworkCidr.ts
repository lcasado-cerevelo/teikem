// IP preferida (2026-10-12; docs/ip-preferida-mfa-diseno.md §5): lectura de una IP o rango (CIDR) para la ayuda en pantalla: vista previa en vivo,
// cuántas direcciones cubre, avisos y «¿está cubierta esta IP?». Espejo de `TrustedNetworkRules` del servidor (que es quien manda: valida y normaliza al
// guardar); aquí solo se muestra lo que el servidor va a entender. IPv4 e IPv6 con BigInt. Lógica pura, sin React.

export type IpFamily = 4 | 6

export interface ParsedIp {
  family: IpFamily
  value: bigint
}

/** Claves de error (textos `system.audit.trusted.errors.*`), las mismas situaciones que el servidor rechaza. */
export type CidrErrorKey = 'required' | 'invalid' | 'prefix' | 'tooWide' | 'notRoutable' | 'mapped'

export interface ParsedCidr {
  family: IpFamily
  /** Dirección de red (bits de host en cero). */
  network: bigint
  prefix: number
  /** El texto traía bits de host y se llevó a la dirección de red (192.168.10.5/24 → 192.168.10.0/24). */
  adjusted: boolean
  /** Una sola dirección (prefijo completo). */
  single: boolean
  /** Cuántas direcciones cubre. */
  count: bigint
  /** Texto normalizado: «203.0.113.10/32», «192.168.10.0/24». */
  text: string
  /** Primera y última dirección del rango, ya escritas. */
  first: string
  last: string
}

export type CidrParse = { cidr: ParsedCidr; error?: undefined } | { cidr?: undefined; error: CidrErrorKey }

/** Rango más ancho que se acepta: IPv4 /16 (65,536 direcciones) e IPv6 /48 (igual que el servidor). */
export const MIN_PREFIX: Record<IpFamily, number> = { 4: 16, 6: 48 }

const MAX_BITS: Record<IpFamily, bigint> = { 4: 32n, 6: 128n }

function parseV4(text: string): bigint | null {
  if (!/^\d{1,3}(\.\d{1,3}){3}$/.test(text)) return null
  let value = 0n
  for (const part of text.split('.')) {
    const n = Number(part)
    if (n > 255) return null
    value = (value << 8n) | BigInt(n)
  }
  return value
}

function parseV6(text: string): bigint | null {
  if (!text.includes(':') || !/^[0-9a-fA-F:.]+$/.test(text)) return null
  let s = text
  if (s.includes('.')) {
    // cola IPv4 (::ffff:203.0.113.10) → dos grupos hex
    const cut = s.lastIndexOf(':')
    const v4 = parseV4(s.slice(cut + 1))
    if (v4 === null) return null
    s = `${s.slice(0, cut + 1)}${((v4 >> 16n) & 0xffffn).toString(16)}:${(v4 & 0xffffn).toString(16)}`
  }
  const halves = s.split('::')
  if (halves.length > 2) return null
  const groups = (part: string) => (part === '' ? [] : part.split(':'))
  const head = groups(halves[0])
  const tail = halves.length === 2 ? groups(halves[1]) : []
  let all: string[]
  if (halves.length === 1) {
    if (head.length !== 8) return null
    all = head
  } else {
    if (head.length + tail.length > 7) return null // «::» sustituye al menos un grupo
    all = [...head, ...Array<string>(8 - head.length - tail.length).fill('0'), ...tail]
  }
  let value = 0n
  for (const g of all) {
    if (!/^[0-9a-fA-F]{1,4}$/.test(g)) return null
    value = (value << 16n) | BigInt(`0x${g}`)
  }
  return value
}

/** IP suelta → familia y valor; una IPv6 mapeada (::ffff:a.b.c.d) pasa a IPv4. null si no es una IP (se rechazan formas sueltas como «127.1»). */
export function parseIp(text: string): ParsedIp | null {
  const s = text.trim()
  if (!s || s.includes('%')) return null
  if (s.includes(':')) {
    const v = parseV6(s)
    if (v === null) return null
    if (v >> 32n === 0xffffn) return { family: 4, value: v & 0xffffffffn }
    return { family: 6, value: v }
  }
  const v = parseV4(s)
  return v === null ? null : { family: 4, value: v }
}

export function formatIp(value: bigint, family: IpFamily): string {
  if (family === 4) return [24n, 16n, 8n, 0n].map((shift) => String((value >> shift) & 0xffn)).join('.')
  const groups = Array.from({ length: 8 }, (_, i) => Number((value >> BigInt((7 - i) * 16)) & 0xffffn))
  // comprime la racha más larga de ceros (de 2 o más grupos)
  let bestStart = -1
  let bestLen = 0
  for (let i = 0; i < 8; ) {
    if (groups[i] !== 0) {
      i++
      continue
    }
    let j = i
    while (j < 8 && groups[j] === 0) j++
    if (j - i > bestLen) {
      bestStart = i
      bestLen = j - i
    }
    i = j
  }
  const hex = groups.map((g) => g.toString(16))
  if (bestLen < 2) return hex.join(':')
  const left = hex.slice(0, bestStart).join(':')
  const right = hex.slice(bestStart + bestLen).join(':')
  return `${left}::${right}`
}

function isUnusable(ip: ParsedIp): boolean {
  if (ip.value === 0n) return true // sin especificar
  if (ip.family === 4) return ip.value >> 24n === 127n || ip.value >= 0xe0000000n // loopback, multidifusión y reservadas
  return ip.value === 1n || ip.value >> 120n === 0xffn // ::1 y multidifusión IPv6
}

/** Lee una IP («203.0.113.10» = /32) o un rango («192.168.10.0/24»). Mismas reglas y mismo orden de errores que el servidor. */
export function parseCidr(text: string): CidrParse {
  const s = text.trim()
  if (!s) return { error: 'required' }
  const slash = s.indexOf('/')
  const addressText = slash >= 0 ? s.slice(0, slash) : s
  let prefixValue: number | null = null
  if (slash >= 0) {
    const p = s.slice(slash + 1)
    if (!/^\d+$/.test(p)) return { error: 'prefix' }
    prefixValue = Number(p)
  }
  const ip = parseIp(addressText)
  if (!ip) return { error: 'invalid' }
  if (prefixValue !== null && addressText.includes(':') && ip.family === 4) return { error: 'mapped' }
  const maxBits = Number(MAX_BITS[ip.family])
  const prefix = prefixValue ?? maxBits
  if (prefix > maxBits) return { error: 'prefix' }
  if (prefix < MIN_PREFIX[ip.family]) return { error: 'tooWide' }
  if (isUnusable(ip)) return { error: 'notRoutable' }
  const hostBits = BigInt(maxBits - prefix)
  const network = (ip.value >> hostBits) << hostBits
  const lastValue = network | ((1n << hostBits) - 1n)
  return {
    cidr: {
      family: ip.family,
      network,
      prefix,
      adjusted: network !== ip.value,
      single: prefix === maxBits,
      count: 1n << hostBits,
      text: `${formatIp(network, ip.family)}/${prefix}`,
      first: formatIp(network, ip.family),
      last: formatIp(lastValue, ip.family),
    },
  }
}

/** ¿La IP cae dentro del rango? Misma familia (IPv4 con IPv4). */
export function cidrContains(cidr: ParsedCidr, ip: ParsedIp | null): boolean {
  if (!ip || ip.family !== cidr.family) return false
  const hostBits = MAX_BITS[cidr.family] - BigInt(cidr.prefix)
  return ip.value >> hostBits === cidr.network >> hostBits
}

const PRIVATE_V4: [bigint, number][] = [
  [0x0a000000n, 8], // 10.0.0.0/8
  [0xac100000n, 12], // 172.16.0.0/12
  [0xc0a80000n, 16], // 192.168.0.0/16
  [0xa9fe0000n, 16], // 169.254.0.0/16 (enlace local)
  [0x64400000n, 10], // 100.64.0.0/10 (CGNAT: no es una IP pública propia)
]

/** ¿Es un rango privado / no enrutable por internet (solo sirve si la conexión llega por una red interna)? */
export function isPrivateRange(cidr: ParsedCidr): boolean {
  if (cidr.family === 4) return PRIVATE_V4.some(([base, bits]) => cidr.network >> BigInt(32 - bits) === base >> BigInt(32 - bits))
  return cidr.network >> 121n === 0x7en /* fc00::/7 */ || cidr.network >> 118n === 0x3fan /* fe80::/10 */
}

/** Tamaños comunes (IPv4) para escoger sin calcular: prefijo y cuántas direcciones cubre. */
export const PREFIX_SHORTCUTS: { prefix: number; count: number }[] = [32, 29, 28, 27, 26, 25, 24, 20, 16].map((prefix) => ({ prefix, count: 2 ** (32 - prefix) }))

/** Pone (o cambia) el «/NN» al texto escrito: «192.168.10.5» + 24 → «192.168.10.5/24». */
export function withPrefix(text: string, prefix: number): string {
  const address = text.trim().split('/')[0].trim()
  return address ? `${address}/${prefix}` : ''
}

/** Muestra un conteo grande con separador de miles del idioma (en IPv6 pasa de 2^64). */
export function formatCount(count: bigint, locale: string): string {
  return count.toLocaleString(locale)
}
