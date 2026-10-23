import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query';
import { fetchCompanyWork, fetchDeclineReasons, fetchFailureReasons, fetchShipmentDetail, type Company } from '@services/driverApi';
import { useAuth } from '@hooks/AuthContext';
import { drainOutbox, pendingClosedShipmentIds } from '@/offline';
import { useAppStore } from '@/stores';
import { OPEN_STATUSES, type Manifest, type Shipment } from '@/types/models';

const READ_OPTS = { staleTime: 5 * 60_000, gcTime: 24 * 60 * 60_000 } as const;

export interface DriverWork {
  manifests: Manifest[];
  /** Solo los envíos abiertos (por entregar), de todas las compañías del chofer. */
  shipments: Shipment[];
  /** Compañías que no respondieron (sin señal, error): lo que se ve de ellas puede estar atrasado. */
  failedCompanies: string[];
}

// ---------------------------------------------------------------------------
// TODO el trabajo del chofer en UNA lista: sus manifiestos y envíos de TODAS sus compañías (una sola sesión, un token por compañía), cada uno marcado con su compañía.
// LO LOCAL MANDA: antes de preguntar se intenta enviar lo pendiente, y los envíos cerrados en el teléfono que aún no llegaron al servidor se excluyen
// (el servidor todavía los cree abiertos; dejarlos pasar «reviviría» una entrega ya hecha).
// Si una compañía no responde, las demás se muestran igual y la que falló se avisa.
// ---------------------------------------------------------------------------
async function loadWork(companies: Company[]): Promise<DriverWork> {
  await drainOutbox();
  const results = await Promise.allSettled(companies.map((c) => fetchCompanyWork(c)));
  const manifests: Manifest[] = [];
  let shipments: Shipment[] = [];
  const failedCompanies: string[] = [];
  results.forEach((r, i) => {
    if (r.status === 'fulfilled') {
      manifests.push(...r.value.manifests);
      shipments.push(...r.value.shipments);
    } else failedCompanies.push(companies[i].name);
  });
  if (failedCompanies.length === companies.length && companies.length > 0) throw new Error('No company answered');
  const closed = await pendingClosedShipmentIds();
  shipments = shipments.filter((s) => OPEN_STATUSES.includes(s.statusCode) && !closed.has(s.id));
  return { manifests, shipments, failedCompanies };
}

/** Las compañías con acceso en esta sesión (las bloqueadas por MFA no cuentan). */
function useUsableCompanies(): Company[] {
  const { companies } = useAuth();
  return useMemo(() => companies.filter((c) => !c.blocked).map((c) => ({ tenantId: c.tenantId, name: c.name })), [companies]);
}

const workKey = (companies: Company[]) => ['work', companies.map((c) => c.tenantId).join(',')] as const;

export function useDriverWork() {
  const companies = useUsableCompanies();
  return useQuery({ queryKey: workKey(companies), queryFn: () => loadWork(companies), enabled: companies.length > 0, ...READ_OPTS });
}

export function useDeclineReasons(tenantId: number | undefined) {
  return useQuery({ queryKey: ['reasons', 'decline', tenantId ?? 0], queryFn: () => fetchDeclineReasons(tenantId as number), enabled: tenantId != null, ...READ_OPTS });
}

export function useFailureReasons(tenantId: number | undefined) {
  return useQuery({ queryKey: ['reasons', 'failure', tenantId ?? 0], queryFn: () => fetchFailureReasons(tenantId as number), enabled: tenantId != null, ...READ_OPTS });
}

/** El envío con teléfono de contacto, cliente y si ya se puede cerrar. Sin señal queda el de la lista. */
export function useShipmentDetail(shipment: Shipment | null) {
  return useQuery({
    queryKey: ['shipment', shipment?.id ?? ''],
    queryFn: () => fetchShipmentDetail(shipment as Shipment),
    enabled: Boolean(shipment),
    ...READ_OPTS,
  });
}

// ---------------------------------------------------------------------------
// Sincronización: baja el trabajo de todas las compañías y los catálogos de motivos a la caché persistida (disponible sin señal).
// Se ejecuta al autenticar, desde el botón «Sincronizar» del Perfil y por el temporizador de autosincronización.
// ---------------------------------------------------------------------------
async function downloadDriverData(qc: QueryClient, companies: Company[], force: boolean): Promise<number> {
  const staleTime = force ? 0 : READ_OPTS.staleTime;
  const work = await qc.fetchQuery({ queryKey: workKey(companies), queryFn: () => loadWork(companies), staleTime, gcTime: READ_OPTS.gcTime });
  // Los motivos que las Acciones necesitan sin señal: un chofer en una zona muerta no debe encontrar la lista vacía. Si falla, se reintenta en la próxima.
  await Promise.allSettled(
    companies.flatMap((c) => [
      qc.fetchQuery({ queryKey: ['reasons', 'decline', c.tenantId], queryFn: () => fetchDeclineReasons(c.tenantId), staleTime, gcTime: READ_OPTS.gcTime }),
      qc.fetchQuery({ queryKey: ['reasons', 'failure', c.tenantId], queryFn: () => fetchFailureReasons(c.tenantId), staleTime, gcTime: READ_OPTS.gcTime }),
    ]),
  );
  return work.shipments.length;
}

export function useAuthSync() {
  const qc = useQueryClient();
  const companies = useUsableCompanies();
  const key = companies.map((c) => c.tenantId).join(',');
  const syncedFor = useRef<string | null>(null);
  useEffect(() => {
    if (!key || syncedFor.current === key) return;
    syncedFor.current = key;
    void downloadDriverData(qc, companies, false).catch(() => undefined);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [qc, key]);
}

export function useSync() {
  const qc = useQueryClient();
  const companies = useUsableCompanies();
  const [isSyncing, setIsSyncing] = useState(false);
  const [count, setCount] = useState<number | null>(null);

  const sync = useCallback(async (): Promise<number | undefined> => {
    if (companies.length === 0 || isSyncing) return undefined;
    setIsSyncing(true);
    try {
      const n = await downloadDriverData(qc, companies, true);
      setCount(n);
      return n;
    } finally {
      setIsSyncing(false);
    }
  }, [qc, companies, isSyncing]);

  return { sync, isSyncing, count };
}

// Autosincronización periódica en segundo plano mientras haya sesión; intervalMinutes <= 0 la apaga. Los errores (sin señal) se tragan: se reintenta en el próximo ciclo.
export function useAutoSync(intervalMinutes: number) {
  const qc = useQueryClient();
  const companies = useUsableCompanies();
  const key = companies.map((c) => c.tenantId).join(',');

  useEffect(() => {
    if (!key || !intervalMinutes || intervalMinutes <= 0) return;
    const id = setInterval(() => {
      downloadDriverData(qc, companies, true).catch(() => undefined);
    }, intervalMinutes * 60_000);
    return () => clearInterval(id);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [qc, key, intervalMinutes]);
}

// Al entregar o marcar «no entregado», el envío sale de inmediato de los listados cacheados (funciona sin conexión) y se pide al servidor que confirme.
// También sale del snapshot del store (OrdersList lee de ahí).
export function useRemoveShipmentFromLists() {
  const qc = useQueryClient();
  const removeShipmentId = useAppStore((s) => s.removeShipmentId);
  return useCallback(
    (shipmentId: string) => {
      qc.setQueriesData<DriverWork>({ queryKey: ['work'] }, (old) => (old ? { ...old, shipments: old.shipments.filter((s) => s.id !== shipmentId) } : old));
      void qc.invalidateQueries({ queryKey: ['work'] });
      removeShipmentId(shipmentId);
    },
    [qc, removeShipmentId],
  );
}
