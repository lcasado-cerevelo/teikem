import { NativeModule, requireNativeModule } from 'expo'

import type { DatawedgeModuleEvents, ProfileStatus } from './Datawedge.types'

declare class DatawedgeModule extends NativeModule<DatawedgeModuleEvents> {
  isAvailable(): boolean
  createProfile(): void
  /** Último estado conocido del perfil (sin preguntar de nuevo a DataWedge). */
  getProfileStatus(): ProfileStatus
  /** Vuelve a preguntar a DataWedge cuál es el perfil activo; la respuesta llega por el evento "onProfileStatus". */
  refreshProfileStatus(): void
}

export default requireNativeModule<DatawedgeModule>('Datawedge')
