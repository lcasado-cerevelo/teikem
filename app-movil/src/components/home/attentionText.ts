import type { TFunction } from 'i18next';
import type { AttentionItem } from '@services/staffApi';

// El texto de un aviso de «Necesita tu atención» (mismos códigos y textos que la web; un código que la app no conoce se muestra con su código y su cantidad).
export const ATTENTION_CODES = ['DELIVERY_FAR', 'INVENTORY_DISCREPANCY', 'RENTAL_DUE', 'SALES_BACKORDER_READY', 'EXIT_NOTICE_BACKORDER', 'ORDERS_NO_DRIVER', 'COD_UNRECONCILED', 'SLA_AT_RISK', 'ORDERS_FAILED', 'CLAIMS_OPEN', 'FLEET_DOCS_DUE', 'INVOICES_OVERDUE'] as const;

export function attentionTitle(item: AttentionItem, t: TFunction, money: (n: number) => string): string {
  const p = { ...item.params, count: item.params.count ?? String(item.count) } as Record<string, string>;
  if (item.code === 'COD_UNRECONCILED') p.amount = money(Number(p.amount ?? 0));
  if (!(ATTENTION_CODES as readonly string[]).includes(item.code)) return `${item.code} · ${item.count}`;
  if (item.code === 'INVENTORY_DISCREPANCY' && (p.binCode == null || p.binCode === '')) return t('ATTENTION.ITEMS.INVENTORY_DISCREPANCY.titleTotal', p);
  if (item.code === 'EXIT_NOTICE_BACKORDER' && p.fulfillable != null && Number(p.fulfillable) > 0) return t('ATTENTION.ITEMS.EXIT_NOTICE_BACKORDER.titleReady', p);
  if (item.code === 'RENTAL_DUE') return t('ATTENTION.ITEMS.RENTAL_DUE.group') + (p.contract ? ` · ${p.contract}` : '');
  return t(`ATTENTION.ITEMS.${item.code}.title`, p);
}

export function attentionGroup(code: string, t: TFunction): string {
  return (ATTENTION_CODES as readonly string[]).includes(code) ? t(`ATTENTION.ITEMS.${code}.group`) : code;
}
