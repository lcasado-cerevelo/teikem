// Convierte un texto en trozos: texto normal, teléfono (tel:), correo (mailto:) o enlace de internet (https:). Se usa para que en el chat y en los avisos
// los teléfonos, correos y direcciones web se puedan tocar. El mensaje se guarda tal cual: el enlace es solo cómo se pinta.

export type LinkPart = { type: 'text'; text: string } | { type: 'phone' | 'email' | 'url'; text: string; href: string };

// URL: http(s):// o www. ; correo: algo@algo.tld ; teléfono: 7 a 15 dígitos con separadores comunes (+1 (787) 555-1234, 787-555-1234, 7875551234).
const PATTERN = /(https?:\/\/[^\s<>"']+|www\.[^\s<>"']+)|([A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,})|((?:\+?\d{1,3}[\s.-]?)?(?:\(\d{3}\)|\d{3})[\s.-]?\d{3}[\s.-]?\d{4}|\+?\d{7,15})/g;

/** Quita la puntuación que cierra una frase («…www.x.com.») y no es parte del enlace. */
function trimTrailing(s: string): { core: string; rest: string } {
  const m = s.match(/[.,;:!?)\]]+$/);
  if (!m) return { core: s, rest: '' };
  return { core: s.slice(0, s.length - m[0].length), rest: m[0] };
}

/** El número listo para marcar: solo dígitos (con + inicial si lo traía); 10 dígitos se toman como de Estados Unidos y Puerto Rico (+1). */
export function toDialable(raw: string): string {
  const plus = raw.trim().startsWith('+');
  const digits = raw.replace(/\D/g, '');
  if (!plus && digits.length === 10) return `+1${digits}`;
  if (!plus && digits.length === 11 && digits.startsWith('1')) return `+${digits}`;
  return plus ? `+${digits}` : digits;
}

export function splitLinks(text: string): LinkPart[] {
  const out: LinkPart[] = [];
  let last = 0;
  for (const m of text.matchAll(PATTERN)) {
    const start = m.index ?? 0;
    const { core, rest } = trimTrailing(m[0]);
    if (core.length === 0) continue;
    if (start > last) out.push({ type: 'text', text: text.slice(last, start) });
    if (m[1]) out.push({ type: 'url', text: core, href: core.startsWith('www.') ? `https://${core}` : core });
    else if (m[2]) out.push({ type: 'email', text: core, href: `mailto:${core}` });
    else {
      const digits = core.replace(/\D/g, '');
      if (digits.length < 7) out.push({ type: 'text', text: core });   // un número corto no es un teléfono
      else out.push({ type: 'phone', text: core, href: `tel:${toDialable(core)}` });
    }
    if (rest) out.push({ type: 'text', text: rest });
    last = start + m[0].length;
  }
  if (last < text.length) out.push({ type: 'text', text: text.slice(last) });
  return out.length === 0 ? [{ type: 'text', text }] : out;
}
