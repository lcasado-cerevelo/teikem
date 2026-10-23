import { useCallback, useMemo } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useAuth } from '@hooks/AuthContext';
import { fetchCompanyThreads, fetchThread, markThreadRead, type ChatThread, type ChatThreadDetail } from '@services/messagesApi';
import type { Company } from '@services/driverApi';
import { sortThreads } from '@/utils/chatRules';

// Los hilos de TODAS las compañías del chofer en una lista (el más reciente primero). Se refresca cada 30 s y al volver a la pantalla.
function useCompanies(): Company[] {
  const { companies } = useAuth();
  return useMemo(() => companies.filter((c) => !c.blocked).map((c) => ({ tenantId: c.tenantId, name: c.name })), [companies]);
}

export function useChatCompanies(): Company[] {
  return useCompanies();
}

export function useThreads() {
  const companies = useCompanies();
  const query = useQuery({
    queryKey: ['threads', companies.map((c) => c.tenantId).join(',')],
    queryFn: async (): Promise<ChatThread[]> => {
      const results = await Promise.allSettled(companies.map((c) => fetchCompanyThreads(c)));
      return sortThreads(results.flatMap((r) => (r.status === 'fulfilled' ? r.value : [])));
    },
    enabled: companies.length > 0,
    staleTime: 15_000,
    refetchInterval: 30_000,
  });
  const unread = (query.data ?? []).filter((t) => !t.isMuted).reduce((n, t) => n + t.unread, 0);
  return { ...query, unread };
}

/** Un hilo abierto: se vuelve a pedir cada 8 s mientras la pantalla está a la vista. */
export function useThreadDetail(tenantId: number | undefined, publicId: string | undefined) {
  const companies = useCompanies();
  const company = companies.find((c) => c.tenantId === tenantId);
  return useQuery<ChatThreadDetail>({
    queryKey: ['thread', tenantId ?? 0, publicId ?? ''],
    queryFn: () => fetchThread(company as Company, publicId as string),
    enabled: !!company && !!publicId,
    refetchInterval: 8_000,
    staleTime: 0,
  });
}

/** Deja leído el hilo y refresca la lista (la insignia de la pestaña baja). */
export function useMarkRead() {
  const qc = useQueryClient();
  return useCallback(
    async (tenantId: number, publicId: string) => {
      try {
        await markThreadRead(tenantId, publicId);
      } catch {
        /* sin señal: se marca la próxima vez */
      }
      void qc.invalidateQueries({ queryKey: ['threads'] });
    },
    [qc],
  );
}
