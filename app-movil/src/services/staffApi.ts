import { api } from '@config/axios';
import type { Company } from '@services/driverApi';

// Lo que el personal ve en el inicio (Lote 213): «Necesita tu atención» y el Pulso del día, de cada compañía con permiso. Son los endpoints de la web; el servidor ya filtra por permisos.

export interface AttentionItem {
  code: string;
  module: string;
  tone: string;
  count: number;
  params: Record<string, string>;
  sinceUtc: string | null;
  summary: boolean;
}

export interface AttentionGroup {
  code: string;
  total: number;
}

export interface CompanyAttention {
  tenantId: number;
  companyName: string;
  total: number;
  items: AttentionItem[];
  groups: AttentionGroup[];
}

interface AttentionDto {
  total: number;
  items: { code: string; module: string; tone: string; count: number; params?: Record<string, string> | null; sinceUtc?: string | null; summary?: boolean }[];
  groups: { code: string; total: number }[];
}

export async function fetchAttention(c: Company): Promise<CompanyAttention> {
  const { data } = await api.get<AttentionDto>('analytics/attention', { tenantId: c.tenantId });
  return {
    tenantId: c.tenantId,
    companyName: c.name,
    total: data.total,
    items: data.items.map((i) => ({ code: i.code, module: i.module, tone: i.tone, count: i.count, params: i.params ?? {}, sinceUtc: i.sinceUtc ?? null, summary: !!i.summary })),
    groups: data.groups.map((g) => ({ code: g.code, total: g.total })),
  };
}

export interface Indicator {
  id: number;
  name: string;
  value: number | null;
  isMoney: boolean;
}

export interface CompanyPulse {
  tenantId: number;
  companyName: string;
  indicators: Indicator[];
}

interface PulseDto {
  indicators: { id: number; name: string; value?: number | null; isMoney: boolean; isVisible: boolean }[];
}

export async function fetchPulse(c: Company): Promise<CompanyPulse> {
  const { data } = await api.get<PulseDto>('analytics/pulse', { tenantId: c.tenantId });
  return { tenantId: c.tenantId, companyName: c.name, indicators: data.indicators.filter((i) => i.isVisible).map((i) => ({ id: i.id, name: i.name, value: i.value ?? null, isMoney: i.isMoney })) };
}
