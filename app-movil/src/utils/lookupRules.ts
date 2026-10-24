// Reglas puras de las consultas del personal (Lote 217): clientes, consignatarios y almacén en lectura. Aquí se decide qué se muestra y cómo se vuelve un enlace.
// Lo que NO sale en el teléfono (decidido por Luis): límite de crédito, identificación fiscal, condiciones de pago, contratos, facturación, costos ni precios.

export interface AddressParts {
  line1?: string | null;
  line2?: string | null;
  city?: string | null;
  state?: string | null;
  postalCode?: string | null;
}

export interface ContactPointLike {
  contactType: string;
  value: string;
  extension?: string | null;
  label?: string | null;
  isPrimary?: boolean;
  isActive?: boolean;
  countryCallingCode?: string | null;
}

/** La dirección en una línea: calle, complemento, ciudad, estado y código postal; sin huecos. */
export function addressLine(a: AddressParts): string {
  const cityState = [a.city, a.state].map((s) => (s ?? '').trim()).filter(Boolean).join(', ');
  const last = [cityState, (a.postalCode ?? '').trim()].filter(Boolean).join(' ');
  return [a.line1, a.line2, last].map((s) => (s ?? '').trim()).filter(Boolean).join(', ');
}

/** Enlace de «cómo llegar» con la dirección (el teléfono abre su aplicación de mapas). Sin dirección no hay enlace. */
export function mapsUrl(address: string): string | null {
  const q = address.trim();
  return q ? `https://www.google.com/maps/search/?api=1&query=${encodeURIComponent(q)}` : null;
}

function digitsOf(raw: string): string {
  return raw.replace(/\D/g, '');
}

/** El enlace de un medio de contacto: teléfono/móvil/fax → tel:, correo → mailto:, WhatsApp → wa.me. Otro tipo o valor vacío → null. */
export function contactHref(cp: ContactPointLike): string | null {
  const value = cp.value.trim();
  if (!value) return null;
  const type = cp.contactType.toUpperCase();
  if (type === 'EMAIL') return `mailto:${value}`;
  const digits = digitsOf(value);
  if (digits.length < 7) return null;
  const cc = digitsOf(cp.countryCallingCode ?? '');
  const full = value.trim().startsWith('+') ? digits : digits.length === 10 ? `${cc || '1'}${digits}` : digits.length === 11 && digits.startsWith('1') ? digits : `${cc}${digits}`;
  if (type === 'WHATSAPP') return `https://wa.me/${full}`;
  if (type === 'PHONE' || type === 'MOBILE' || type === 'FAX') return `tel:+${full}`;
  return null;
}

/** El medio de contacto como texto: «Móvil: 787-555-1234 ext. 2». */
export function contactText(cp: ContactPointLike, typeLabel?: string): string {
  const head = (cp.label ?? '').trim() || typeLabel || '';
  const ext = (cp.extension ?? '').trim();
  const value = ext ? `${cp.value.trim()} ext. ${ext}` : cp.value.trim();
  return head ? `${head}: ${value}` : value;
}

/** Los medios activos con el principal primero; los inactivos no se ofrecen para llamar. */
export function usableContacts<T extends ContactPointLike>(list: T[]): T[] {
  return list.filter((c) => c.isActive !== false).sort((a, b) => Number(!!b.isPrimary) - Number(!!a.isPrimary));
}

export interface ClientFicha {
  publicId: string;
  code: string;
  name: string;
  legalName: string | null;
  status: string;
  isActive: boolean;
  address: string | null;
  pickupAddress: string | null;
  contacts: { name: string; role: string | null; points: { text: string; href: string | null }[] }[];
  points: { text: string; href: string | null }[];
}

interface AddressDtoLike extends AddressParts { name?: string }
interface ClientDetailLike {
  publicId: string;
  code: string;
  name: string;
  legalName?: string | null;
  statusLabel: string;
  isActive: boolean;
  physicalAddress?: AddressDtoLike | null;
  postalAddress?: AddressDtoLike | null;
  pickupAddress?: AddressDtoLike | null;
  contacts?: { fullName: string; role?: string | null; isActive?: boolean; isPrimary?: boolean; contactPoints?: (ContactPointLike & { contactTypeLabel?: string })[] }[];
  contactPoints?: (ContactPointLike & { contactTypeLabel?: string })[];
}

/** De la ficha completa del servidor deja solo lo que el personal consulta: nunca crédito, identificación fiscal, pago ni contratos. */
export function toClientFicha(d: ClientDetailLike): ClientFicha {
  const point = (c: ContactPointLike & { contactTypeLabel?: string }) => ({ text: contactText(c, c.contactTypeLabel), href: contactHref(c) });
  const physical = d.physicalAddress ? addressLine(d.physicalAddress) : '';
  const pickup = d.pickupAddress ? addressLine(d.pickupAddress) : '';
  return {
    publicId: d.publicId,
    code: d.code,
    name: d.name,
    legalName: d.legalName ?? null,
    status: d.statusLabel,
    isActive: d.isActive,
    address: physical || null,
    pickupAddress: pickup || null,
    contacts: (d.contacts ?? [])
      .filter((c) => c.isActive !== false)
      .sort((a, b) => Number(!!b.isPrimary) - Number(!!a.isPrimary))
      .map((c) => ({ name: c.fullName, role: c.role ?? null, points: usableContacts(c.contactPoints ?? []).map(point) })),
    points: usableContacts(d.contactPoints ?? []).map(point),
  };
}

export interface StockRow {
  warehouseCode: string;
  binCode: string | null;
  lotNumber: string | null;
  qtyOnHand: number;
  qtyReserved: number;
  qtyAvailable: number;
}

export interface WarehouseStock {
  warehouseCode: string;
  onHand: number;
  available: number;
  bins: { binCode: string | null; lotNumber: string | null; onHand: number; available: number }[];
}

/** Las existencias de un producto agrupadas por almacén (con sus posiciones); las filas en cero no se muestran. */
export function groupStock(rows: StockRow[]): WarehouseStock[] {
  const by = new Map<string, WarehouseStock>();
  for (const r of rows) {
    if (r.qtyOnHand === 0 && r.qtyAvailable === 0) continue;
    let w = by.get(r.warehouseCode);
    if (!w) {
      w = { warehouseCode: r.warehouseCode, onHand: 0, available: 0, bins: [] };
      by.set(r.warehouseCode, w);
    }
    w.onHand += r.qtyOnHand;
    w.available += r.qtyAvailable;
    w.bins.push({ binCode: r.binCode, lotNumber: r.lotNumber, onHand: r.qtyOnHand, available: r.qtyAvailable });
  }
  return [...by.values()].sort((a, b) => a.warehouseCode.localeCompare(b.warehouseCode));
}

/** Un número sin ceros de más: 12 → «12», 2.50 → «2.5». */
export function qty(n: number): string {
  return String(Math.round(n * 1000) / 1000);
}

/** Lo que se teclea o se escanea: si parece un código (sin espacios) se prueba primero como código de barras o SKU exacto. */
export function looksLikeCode(text: string): boolean {
  const s = text.trim();
  return s.length >= 3 && !/\s/.test(s);
}
