// Ícono del grupo del menú que corresponde al módulo de negocio de una definición (Indicadores y Gráficos, Lote F8a P3).
// Mismo mapa que `PulseSections.tsx` usa para los nodos del río; se repite aquí (en vez de exportarlo de ahí) para no
// tocar un archivo ya cubierto por las pruebas de P2.
import type { ReactNode } from 'react'
import { IconBox, IconCash, IconLayers } from '../../app/icons'
import type { ModuleGroup } from './pulseLayout'

export const MODULE_GROUP_ICON: Record<ModuleGroup, () => ReactNode> = { ops: IconBox, warehouse: IconLayers, money: IconCash }
