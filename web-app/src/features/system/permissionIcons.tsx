// Ícono del grupo del menú por categoría de permiso (`PermissionCategory`), para agrupar casillas en el editor de rol
// y en "Permisos de {name}". Reusa los íconos del shell (`app/icons.tsx`); no inventa unos nuevos.
import type { ComponentType } from 'react'
import { IconBox, IconCash, IconChart, IconGear, IconLayers, IconUsers } from '../../app/icons'

const CATEGORY_ICON: Record<string, ComponentType> = {
  ORDERS: IconBox,
  TRIPS: IconBox,
  WAREHOUSE: IconLayers,
  BILLING: IconCash,
  COD: IconCash,
  RENTAL: IconCash,
  PURCHASING: IconCash,
  FLEET: IconUsers,
  CLIENTS: IconUsers,
  ANALYTICS: IconChart,
  PULSE: IconChart,
  ADMIN: IconGear,
  SECURITY: IconGear,
}

/** Ícono del grupo del menú que corresponde a la categoría de un permiso; `IconGear` si no se reconoce. */
export function categoryIcon(category: string): ComponentType {
  return CATEGORY_ICON[category] ?? IconGear
}
