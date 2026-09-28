import { NativeModule, requireNativeModule } from 'expo'

import type { DatawedgeModuleEvents } from './Datawedge.types'

declare class DatawedgeModule extends NativeModule<DatawedgeModuleEvents> {
  isAvailable(): boolean
  createProfile(): void
}

export default requireNativeModule<DatawedgeModule>('Datawedge')
