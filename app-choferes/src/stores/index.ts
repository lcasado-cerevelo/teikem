import { create } from 'zustand';
import type { Shipment } from '@/types/models';
import { trackingStore } from '@/tracking/trackingStore';
import { DEFAULT_SYNC_INTERVAL_MINUTES, saveSyncIntervalMinutes, loadSyncIntervalMinutes } from '@hooks/useSyncSettings';

// Almacén «puente» global: lleva la selección entre pantallas sin volver a pedirla.

// Búsqueda del listado de órdenes. Vive en el store (y no en el useState de la pantalla) porque debe SOBREVIVIR a entrar al detalle de una orden y volver:
// expo-router desmonta la pantalla de la lista al navegar, así que un estado local se perdería.
export interface OrderFilters {
  search: string;
}

const emptyFilters: OrderFilters = { search: '' };

interface AppState {
  /** Los envíos que muestra el listado (los de un manifiesto o los de «Entregas para hoy»). */
  shipmentIds: string[];
  shipment: Shipment | null;
  orderFilters: OrderFilters;

  setOrderFilters: (patch: Partial<OrderFilters>) => void;
  resetOrderFilters: () => void;

  addShipmentIds: (ids: string[]) => void;
  removeShipmentId: (id: string) => void;
  resetShipmentIds: () => void;

  addShipment: (s: Shipment) => void;
  resetShipment: () => void;

  /** El chofer puede apagar el envío de su ubicación durante la ruta (la oficina recibe el aviso). */
  trackingEnabled: boolean;
  setTrackingEnabled: (v: boolean) => void;
  hydrateTrackingEnabled: () => Promise<void>;

  // Autosincronización: valor «vivo» único compartido entre el layout (que arma el temporizador) y Perfil (que lo cambia).
  syncIntervalMinutes: number;
  setSyncIntervalMinutes: (minutes: number) => void;
  hydrateSyncIntervalMinutes: () => Promise<void>;
}

export const useAppStore = create<AppState>((set) => ({
  shipmentIds: [],
  shipment: null,
  orderFilters: emptyFilters,

  setOrderFilters: (patch) => set((state) => ({ orderFilters: { ...state.orderFilters, ...patch } })),
  resetOrderFilters: () => set({ orderFilters: emptyFilters }),

  addShipmentIds: (ids) => set({ shipmentIds: ids }),
  removeShipmentId: (id) => set((state) => ({ shipmentIds: state.shipmentIds.filter((x) => x !== id) })),
  resetShipmentIds: () => set({ shipmentIds: [] }),

  addShipment: (s) => set({ shipment: s }),
  resetShipment: () => set({ shipment: null }),

  trackingEnabled: true,
  setTrackingEnabled: (v) => {
    set({ trackingEnabled: v });
    void trackingStore.setEnabled(v);
  },
  hydrateTrackingEnabled: async () => set({ trackingEnabled: await trackingStore.getEnabled() }),

  syncIntervalMinutes: DEFAULT_SYNC_INTERVAL_MINUTES,
  setSyncIntervalMinutes: (minutes) => {
    set({ syncIntervalMinutes: minutes });
    void saveSyncIntervalMinutes(minutes);
  },
  hydrateSyncIntervalMinutes: async () => {
    const minutes = await loadSyncIntervalMinutes();
    set({ syncIntervalMinutes: minutes });
  },
}));
