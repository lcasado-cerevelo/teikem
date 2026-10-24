import { storage } from './db';
import type { SnapRoute } from '@services/scanApi';
import type { SnapStatus } from '@utils/scanRules';

// Lote 216 — datos chicos de la estación de escaneo (cuándo se bajó, y los escaneos que el servidor no aceptó). Módulo aparte para que la cola pueda anotar problemas sin importar al almacén de escaneo.

export interface ScanMeta {
  generatedAtUtc: string;
  downloadedAtUtc: string;
  total: number;
  capped: boolean;
  statuses: SnapStatus[];
  routes: SnapRoute[];
}

export interface ScanProblem { id: string; at: number; code: string; action: string; outcome: string; message: string; orderNumber: string | null }

export const metaKey = (t: number) => `scan.meta.${t}`;
const problemsKey = (t: number) => `scan.problems.${t}`;

export async function readMeta(tenantId: number): Promise<ScanMeta | null> {
  const raw = await storage.kvGet(metaKey(tenantId));
  if (!raw) return null;
  try { return JSON.parse(raw) as ScanMeta; } catch { return null; }
}

export async function readProblems(tenantId: number): Promise<ScanProblem[]> {
  const raw = await storage.kvGet(problemsKey(tenantId));
  if (!raw) return [];
  try { return JSON.parse(raw) as ScanProblem[]; } catch { return []; }
}

export async function addProblem(tenantId: number, p: ScanProblem): Promise<void> {
  const list = (await readProblems(tenantId)).filter((x) => x.id !== p.id);
  list.unshift(p);
  await storage.kvSet(problemsKey(tenantId), JSON.stringify(list.slice(0, 200)));
}

export async function dismissProblem(tenantId: number, id: string | 'all'): Promise<void> {
  if (id === 'all') await storage.kvDel(problemsKey(tenantId));
  else await storage.kvSet(problemsKey(tenantId), JSON.stringify((await readProblems(tenantId)).filter((x) => x.id !== id)));
}

