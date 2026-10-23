import { create } from 'zustand';
import type { Manifest, Shipment } from '@/types/models';
import {
  DEFAULT_SYNC_INTERVAL_MINUTES,
  saveSyncIntervalMinutes,
  loadSyncIntervalMinutes,
} from '@hooks/useSyncSettings';

// Global "bridge" store: carries selected IDs/entities between screens
// without refetching. Mirrors the original app's 4 slices.
// Filtros del listado de órdenes. Viven en el store (y no en el useState de la
// pantalla) porque deben SOBREVIVIR a entrar al detalle de una orden y volver:
// expo-router desmonta la pantalla de la lista al navegar, así que un estado
// local se perdería y el chofer tendría que re-escribir la búsqueda cada vez.
export interface OrderFilters {
  search: string;
  // Panel avanzado (solo para usuarios en UsersSender). senderIds VACÍO = no se
  // consulta nada: el filtro de sender es obligatorio a propósito, porque sin él
  // el alcance son decenas de miles de órdenes.
  senderIds: number[];
  dateFrom: string; // yyyy-MM-dd, sobre ReadyDate
  dateTo: string;
  city: string;
  panelOpen: boolean;
}

const emptyFilters: OrderFilters = {
  search: '',
  senderIds: [],
  dateFrom: '',
  dateTo: '',
  city: '',
  panelOpen: false,
};

interface AppState {
  manifestIds: string[];
  manifests: Manifest[];
  shipmentIds: number[];
  shipment: Shipment | null;
  orderFilters: OrderFilters;

  setOrderFilters: (patch: Partial<OrderFilters>) => void;
  resetOrderFilters: () => void;

  addManifestIds: (ids: string[]) => void;
  resetManifestIds: () => void;

  addManifest: (m: Manifest) => void;
  resetManifests: () => void;

  addShipmentIds: (ids: number[]) => void;
  removeShipmentId: (id: number) => void;
  resetShipmentIds: () => void;

  addShipment: (s: Shipment) => void;
  resetShipment: () => void;

  // Autosincronización: valor "vivo" único compartido entre el layout (que
  // arma el setInterval) y Perfil (que lo cambia) — un useState por pantalla
  // se desincronizaría entre sí.
  syncIntervalMinutes: number;
  setSyncIntervalMinutes: (minutes: number) => void;
  hydrateSyncIntervalMinutes: () => Promise<void>;
}

export const useAppStore = create<AppState>((set) => ({
  manifestIds: [],
  manifests: [],
  shipmentIds: [],
  shipment: null,
  orderFilters: emptyFilters,

  setOrderFilters: (patch) => set((state) => ({ orderFilters: { ...state.orderFilters, ...patch } })),
  resetOrderFilters: () => set({ orderFilters: emptyFilters }),

  addManifestIds: (ids) => set({ manifestIds: ids }),
  resetManifestIds: () => set({ manifestIds: [] }),

  addManifest: (m) =>
    set((state) =>
      state.manifests.some((x) => x.manifestID === m.manifestID)
        ? state
        : { manifests: [...state.manifests, m] },
    ),
  resetManifests: () => set({ manifests: [] }),

  addShipmentIds: (ids) => set({ shipmentIds: ids }),
  removeShipmentId: (id) => set((state) => ({ shipmentIds: state.shipmentIds.filter((x) => x !== id) })),
  resetShipmentIds: () => set({ shipmentIds: [] }),

  addShipment: (s) => set({ shipment: s }),
  resetShipment: () => set({ shipment: null }),

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
