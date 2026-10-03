export type ScanEvent = {
  data: string
  symbology: string
}

/**
 * Estado del perfil "TeikemAlmacen" en DataWedge, según lo que el propio DataWedge contestó (docs/mobile/mejoras-ux-zebra.md §2):
 * - `ready`: DataWedge dice que el perfil activo de la app es TeikemAlmacen (salida por intent, sin teclas).
 * - `noProfile`: DataWedge rechazó crear el perfil, o el perfil activo es otro (p. ej. "Profile0", el de fábrica).
 * - `unconfirmed`: se pidió crear el perfil pero DataWedge todavía no contestó (o no contesta).
 * - `unavailable`: el aparato no tiene DataWedge (no es un Zebra): se trabaja con el teclado.
 */
export type ProfileState = 'ready' | 'noProfile' | 'unconfirmed' | 'unavailable'

export type ProfileStatus = {
  state: ProfileState
  /** Lo que contestó DataWedge (perfil activo, código de error), para soporte; '' si nada. */
  detail: string
}

export type DatawedgeModuleEvents = {
  onScan: (event: ScanEvent) => void
  onProfileStatus: (event: ProfileStatus) => void
}
