import { NativeModule } from 'expo'

import type { DatawedgeModuleEvents } from './Datawedge.types'

// Sin DataWedge en web (ni en iOS, no soportado en este lote): kernel/scanner/useScanner.ts no llama a isAvailable() en
// web, pero esta implementación existe para que el bundler no falle al resolver el módulo.
class DatawedgeModuleWeb extends NativeModule<DatawedgeModuleEvents> {
  isAvailable(): boolean {
    return false
  }

  createProfile(): void {
    // no-op
  }
}

export default new DatawedgeModuleWeb()
