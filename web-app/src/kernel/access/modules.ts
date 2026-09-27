// Espejo de Teikem.Domain.Constants.ModuleKeys: los códigos exactos que devuelve /api/v1/me → enabledModules.
export const ModuleKeys = {
  LtlGround: 'LTL_GROUND',
  Cod: 'COD',
  WmsLotSerial: 'WMS_LOTSERIAL',
  CrossDock: 'CROSSDOCK',
  RentalEquipment: 'RENTAL_EQUIPMENT',
  RentalBilling: 'RENTAL_BILLING',
  Maritime: 'MARITIME',
  ClientPortal: 'CLIENT_PORTAL',
  CustomFields: 'CUSTOM_FIELDS',
  Purchasing: 'PURCHASING',
  Catalog: 'CATALOG',
  Analytics: 'ANALYTICS',
  System: 'SYSTEM',
} as const

export type ModuleKey = (typeof ModuleKeys)[keyof typeof ModuleKeys]
