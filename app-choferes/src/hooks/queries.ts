import { useCallback, useEffect, useRef, useState } from 'react';
import { useQueries, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query';
import {
  fetchCodTypeById,
  fetchCodTypeIds,
  fetchComments,
  fetchCompany,
  fetchConsignee,
  fetchManifestById,
  fetchManifestIds,
  fetchMe,
  fetchMySenders,
  fetchPieceById,
  fetchPieceIds,
  fetchReasonById,
  fetchReasonIds,
  fetchShipmentById,
  fetchShipmentIds,
  fetchShipmentIdsByDriver,
  fetchShipmentsFullByDriver,
  type ManifestQuery,
} from '@services/custom-api/custom-api';
import { queryKeys } from '@constants/index';
import { drainOutbox, pendingClosedShipmentIds } from '@/offline';
import { useAppStore } from '@/stores';
import type { CodType, Manifest, Piece, Reason, Shipment } from '@/types/models';

const READ_OPTS = { staleTime: 5 * 60_000, gcTime: 24 * 60 * 60_000 } as const;

// Current logged-in driver (from the token). Drives everything else.
export function useMe() {
  return useQuery({ queryKey: ['me'], queryFn: fetchMe, ...READ_OPTS });
}

// Senders que el usuario puede consultar (UsersSender). Lista vacía = chofer:
// la pantalla de órdenes esconde el panel de filtros por completo.
// Se cachea con las mismas READ_OPTS (y por tanto se persiste), así que un
// usuario de oficina que ya la cargó sigue viendo el panel sin señal — aunque
// la búsqueda en sí sí requiera conexión.
export function useMySenders() {
  return useQuery({ queryKey: ['senders', 'mine'], queryFn: fetchMySenders, ...READ_OPTS });
}

// Stored geo of the delivery address (to decide whether to backfill coords).
export function useConsignee(id: number | undefined) {
  return useQuery({
    queryKey: ['consignee', id ?? 0],
    queryFn: () => fetchConsignee(id as number),
    enabled: Boolean(id),
    ...READ_OPTS,
  });
}

export function useCompany(companyId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.company(companyId ?? ''),
    queryFn: () => fetchCompany(companyId as string),
    enabled: Boolean(companyId),
    ...READ_OPTS,
  });
}

export function useManifestIds(params: ManifestQuery | null) {
  return useQuery({
    queryKey: params
      ? queryKeys.manifestIds(params.driverId, params.createdDate, params.status)
      : ['manifests', 'ids', 'disabled'],
    queryFn: () => fetchManifestIds(params as ManifestQuery),
    enabled: Boolean(params?.driverId),
    ...READ_OPTS,
  });
}

// Resolve each manifest id to its detail, in parallel.
export function useManifestsByIds(ids: string[]) {
  return useQueries({
    queries: ids.map((id) => ({
      queryKey: queryKeys.manifest(id),
      queryFn: () => fetchManifestById(id),
      ...READ_OPTS,
    })),
    combine: (results) => ({
      data: results.map((r) => r.data).filter((m): m is Manifest => Boolean(m)),
      pending: results.some((r) => r.isPending),
    }),
  });
}

// ---------------------------------------------------------------------------
// Listas de envíos abiertos: LO LOCAL MANDA. No importa quién dispare el
// refetch (montaje de pantalla, invalidación, sync manual), la respuesta del
// servidor pasa por dos guardas antes de tocar la caché:
//  1. drainOutbox() — intenta enviar los cierres pendientes ANTES de preguntar.
//  2. pendingClosedShipmentIds() — si algún cierre AÚN no pudo enviarse, se
//     excluye del resultado. El servidor todavía cree que esa orden está
//     abierta; dejarla pasar "revive" una entrega ya completada en el celular.
// ---------------------------------------------------------------------------
async function fetchShipmentsIndividually(driverId: number): Promise<Shipment[]> {
  const ids = await fetchShipmentIdsByDriver(driverId);
  return Promise.all((ids ?? []).map((id) => fetchShipmentById(id)));
}

async function fetchOpenShipmentIds(manifest: string): Promise<number[]> {
  await drainOutbox();
  const ids = await fetchShipmentIds(manifest);
  const closed = await pendingClosedShipmentIds();
  return closed.size === 0 ? ids : (ids ?? []).filter((id) => !closed.has(id));
}

// TODAS las entregas y recogidas de hoy del chofer, en UNA llamada de red: el
// SP ya trae las filas completas, y de aquí Home, Manifiestos y OrdersList
// (flujo chofer) sacan lo suyo filtrando en memoria — ninguno vuelve a pedir
// detalle por-orden. Antes cada pantalla reconstruía el detalle con un
// useQuery por ID (bien con 55 órdenes; con las ~1,600 de un usuario de
// oficina eso solo, sin tocar la red, colgaba la UI armando 1,600 queries).
//
// FALLBACK deliberado: si el endpoint bulk no existe (API sin desplegar) o el
// SP todavía devuelve solo IDs (filas sin waybill), se cae al camino viejo de
// IDs + un GET por orden. Así el APK nuevo no depende del orden en que Luis
// despliegue las piezas — solo se vuelve lento, no se rompe.
async function loadDriverShipments(driverId: number): Promise<Shipment[]> {
  await drainOutbox();
  let rows: Shipment[];
  try {
    rows = await fetchShipmentsFullByDriver(driverId);
    if (rows.length > 0 && !rows[0].waybill) {
      rows = await fetchShipmentsIndividually(driverId);
    }
  } catch {
    rows = await fetchShipmentsIndividually(driverId);
  }
  const closed = await pendingClosedShipmentIds();
  return closed.size === 0 ? rows : rows.filter((s) => !closed.has(s.shipmentID));
}

// Query única compartida: Home, Manifiestos y OrdersList (flujo chofer) leen
// de aquí y filtran en memoria (por status/assignPK/manifestDL/manifestPk, o
// por el subconjunto de ids que Home/Manifiestos dejó en el store) — nadie
// vuelve a pedir la red por-orden.
export function useDriverShipments(driverId: number | undefined) {
  return useQuery({
    queryKey: ['shipments', 'driver', driverId ?? 0, 'full'],
    queryFn: () => loadDriverShipments(driverId as number),
    enabled: Boolean(driverId),
    ...READ_OPTS,
  });
}

// ---------------------------------------------------------------------------
// Sincronización: baja TODAS las entregas del chofer (IDs + detalle de cada
// envío) a la caché persistida. Esto alimenta el Home, el listado de
// manifiestos (agrupado por manifestDL) y deja los datos disponibles offline.
// Se ejecuta al autenticar y desde el botón "Sincronizar" del perfil.
// ---------------------------------------------------------------------------
async function downloadDriverData(
  qc: QueryClient,
  driverId: number,
  force: boolean,
): Promise<number> {
  const staleTime = force ? 0 : READ_OPTS.staleTime;
  // UNA llamada baja IDs + detalles completos (loadDriverShipments). Antes esto
  // era "lista de IDs + un GET por orden" (55 requests para un chofer; el caso
  // oficina, ~1,600, directamente se saltaba el detalle). Ahora todos los
  // usuarios quedan con su lista completa y navegable offline con un request.
  const shipments = await qc.fetchQuery({
    queryKey: ['shipments', 'driver', driverId, 'full'],
    queryFn: () => loadDriverShipments(driverId),
    staleTime,
    gcTime: READ_OPTS.gcTime,
  });

  // Catálogos que las Acciones necesitan estando offline: motivos de excepción
  // y tipos de COD. Sin este prefetch, un chofer que nunca abrió esas pantallas
  // con señal encuentra los dropdowns VACÍOS en el momento que más los
  // necesita (registrar una excepción en una zona muerta). Best-effort: si
  // esto falla no debe tumbar la sincronización de entregas.
  try {
    const reasonIds = await qc.fetchQuery({
      queryKey: ['reasons', 'ids'],
      queryFn: fetchReasonIds,
      staleTime,
      gcTime: READ_OPTS.gcTime,
    });
    await Promise.all(
      (reasonIds ?? []).map((id) =>
        qc.fetchQuery({
          queryKey: ['reason', id],
          queryFn: () => fetchReasonById(id),
          staleTime,
          gcTime: READ_OPTS.gcTime,
        }),
      ),
    );
    const codTypeIds = await qc.fetchQuery({
      queryKey: ['codTypes', 'ids'],
      queryFn: fetchCodTypeIds,
      staleTime,
      gcTime: READ_OPTS.gcTime,
    });
    await Promise.all(
      (codTypeIds ?? []).map((id) =>
        qc.fetchQuery({
          queryKey: ['codType', id],
          queryFn: () => fetchCodTypeById(id),
          staleTime,
          gcTime: READ_OPTS.gcTime,
        }),
      ),
    );
  } catch {
    // sin señal o catálogo caído — se reintenta en el próximo sync
  }

  return shipments?.length ?? 0;
}

// Auto-sync al autenticar: corre una vez cuando ya se conoce el chofer.
// (Ya no espera a useMySenders: con el fetch bulk, chofer y oficina bajan su
// lista completa en una sola llamada — no hay caso especial que decidir.)
export function useAuthSync() {
  const qc = useQueryClient();
  const { data: me } = useMe();
  const driverId = me?.userID;
  const syncedFor = useRef<number | null>(null);
  useEffect(() => {
    if (!driverId || syncedFor.current === driverId) return;
    syncedFor.current = driverId;
    void downloadDriverData(qc, driverId, false);
  }, [qc, driverId]);
}

// Sync manual (botón "Sincronizar"): fuerza datos frescos.
export function useSync() {
  const qc = useQueryClient();
  const { data: me } = useMe();
  const driverId = me?.userID;
  const [isSyncing, setIsSyncing] = useState(false);
  const [count, setCount] = useState<number | null>(null);

  const sync = useCallback(async (): Promise<number | undefined> => {
    if (!driverId || isSyncing) return undefined;
    setIsSyncing(true);
    try {
      await qc.invalidateQueries({ queryKey: ['me'] });
      const n = await downloadDriverData(qc, driverId, true);
      setCount(n);
      return n;
    } finally {
      setIsSyncing(false);
    }
  }, [qc, driverId, isSyncing]);

  return { sync, isSyncing, count };
}

// Autosincronización periódica: repite el mismo downloadDriverData del botón
// manual cada `intervalMinutes`, en segundo plano, mientras haya sesión.
// intervalMinutes <= 0 desactiva el timer (por si el usuario quisiera apagarlo
// en el futuro). Los errores (sin señal, etc.) se tragan a propósito: es un
// timer de fondo, no debe reventar ni mostrar nada — simplemente lo intenta
// de nuevo en el próximo ciclo.
export function useAutoSync(intervalMinutes: number) {
  const qc = useQueryClient();
  const { data: me } = useMe();
  const driverId = me?.userID;

  useEffect(() => {
    if (!driverId || !intervalMinutes || intervalMinutes <= 0) return;
    const ms = intervalMinutes * 60_000;
    const id = setInterval(() => {
      downloadDriverData(qc, driverId, true).catch(() => {
        // sin señal u otro error transitorio — se reintenta en el próximo ciclo
      });
    }, ms);
    return () => clearInterval(id);
  }, [qc, driverId, intervalMinutes]);
}

// Al completar (o exceptuar) una entrega, la orden queda cerrada: sácala de
// inmediato de todos los listados cacheados (funciona aun sin conexión) y
// dispara el refetch para que el servidor confirme el estado. También se saca
// del snapshot en el store (OrdersList lee de ahí, no de la caché — sin esto
// la pantalla abierta no se actualizaba hasta salir y volver a entrar).
export function useRemoveShipmentFromLists() {
  const qc = useQueryClient();
  const removeShipmentId = useAppStore((s) => s.removeShipmentId);
  return useCallback(
    (shipmentId: number) => {
      const dropShipment = (old: Shipment[] | undefined) =>
        old?.filter((s) => s.shipmentID !== shipmentId);
      const dropId = (old: number[] | undefined) => old?.filter((id) => id !== shipmentId);
      qc.setQueriesData<Shipment[]>({ queryKey: ['shipments', 'driver'] }, dropShipment);
      qc.setQueriesData<number[]>({ queryKey: ['shipments', 'ids'] }, dropId);
      void qc.invalidateQueries({ queryKey: ['shipments'] });
      removeShipmentId(shipmentId);
    },
    [qc, removeShipmentId],
  );
}

export function useShipmentIds(manifest: string | number) {
  return useQuery({
    queryKey: queryKeys.shipmentIds(String(manifest)),
    queryFn: () => fetchOpenShipmentIds(String(manifest)),
    ...READ_OPTS,
  });
}

// ---- Pieces ----
export function usePieceIds(shipmentId: number | undefined) {
  return useQuery({
    queryKey: queryKeys.pieceIds(shipmentId ?? 0),
    queryFn: () => fetchPieceIds(shipmentId as number),
    enabled: Boolean(shipmentId),
    ...READ_OPTS,
  });
}

export function usePiecesByIds(ids: number[]) {
  return useQueries({
    queries: ids.map((id) => ({
      queryKey: queryKeys.piece(id),
      queryFn: () => fetchPieceById(id),
      ...READ_OPTS,
    })),
    combine: (results) => ({
      data: results.map((r) => r.data).filter((p): p is Piece => Boolean(p)),
      pending: results.some((r) => r.isPending),
    }),
  });
}

// ---- Comments ----
export function useComments(shipmentId: number | undefined) {
  return useQuery({
    queryKey: ['comments', shipmentId ?? 0],
    queryFn: () => fetchComments(shipmentId as number),
    enabled: Boolean(shipmentId),
    ...READ_OPTS,
  });
}

// ---- Reasons (exception catalog) ----
// ids -> detalle de cada motivo, en paralelo (como piezas).
export function useReasons() {
  const idsQuery = useQuery({ queryKey: ['reasons', 'ids'], queryFn: fetchReasonIds, ...READ_OPTS });
  const ids = idsQuery.data ?? [];
  const details = useQueries({
    queries: ids.map((id) => ({
      queryKey: ['reason', id],
      queryFn: () => fetchReasonById(id),
      ...READ_OPTS,
    })),
    combine: (results) => ({
      data: results.map((r) => r.data).filter((x): x is Reason => Boolean(x)),
      pending: results.some((r) => r.isPending),
    }),
  });
  return { data: details.data, isLoading: idsQuery.isLoading || (ids.length > 0 && details.pending) };
}

// ---- COD types (Cash-On-Delivery catalog) ----
export function useCodTypes() {
  const idsQuery = useQuery({ queryKey: ['codTypes', 'ids'], queryFn: fetchCodTypeIds, ...READ_OPTS });
  const ids = idsQuery.data ?? [];
  const details = useQueries({
    queries: ids.map((id) => ({
      queryKey: ['codType', id],
      queryFn: () => fetchCodTypeById(id),
      ...READ_OPTS,
    })),
    combine: (results) => ({
      data: results.map((r) => r.data).filter((x): x is CodType => Boolean(x)),
      pending: results.some((r) => r.isPending),
    }),
  });
  return { data: details.data, isLoading: idsQuery.isLoading || (ids.length > 0 && details.pending) };
}
