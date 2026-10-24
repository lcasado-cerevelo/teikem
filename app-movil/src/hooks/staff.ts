import { useQuery } from '@tanstack/react-query';
import { useAccess } from '@hooks/access';
import { fetchAttention, fetchPulse } from '@services/staffApi';

// Inicio del personal (Lote 213): atención y pulso de TODAS las compañías donde tiene el permiso. Si una compañía falla (sin señal, 403), las demás se muestran igual.

export function useAttention() {
  const companies = useAccess().companiesWith('pulse.attention');
  return useQuery({
    queryKey: ['attention', companies.map((c) => c.tenantId).join(',')],
    queryFn: async () => {
      const results = await Promise.allSettled(companies.map((c) => fetchAttention({ tenantId: c.tenantId, name: c.name })));
      return results.flatMap((r) => (r.status === 'fulfilled' ? [r.value] : []));
    },
    enabled: companies.length > 0,
    staleTime: 60_000,
    refetchInterval: 5 * 60_000,
  });
}

export function usePulse() {
  const companies = useAccess().companiesWith('analytics.view');
  return useQuery({
    queryKey: ['pulse', companies.map((c) => c.tenantId).join(',')],
    queryFn: async () => {
      const results = await Promise.allSettled(companies.map((c) => fetchPulse({ tenantId: c.tenantId, name: c.name })));
      return results.flatMap((r) => (r.status === 'fulfilled' ? [r.value] : []));
    },
    enabled: companies.length > 0,
    staleTime: 60_000,
    refetchInterval: 5 * 60_000,
  });
}
