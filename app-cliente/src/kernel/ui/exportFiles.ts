// Excel y PDF de las tablas del portal: SOLO en la web (exportFiles.web.ts). En el teléfono no existen (allí se exporta CSV y se comparte); esta versión nunca se llama porque `runExport` manda todo a CSV
// fuera de la web, y existe para que el resto de la app compile y para no cargar jsPDF ni SheetJS en el teléfono.
import type { ExportData, ExportHeading } from './tableExport'

const unsupported = (): never => {
  throw new Error('Excel y PDF solo están disponibles en la web.')
}

export async function saveXlsx(_data: ExportData, _fileName: string, _heading: ExportHeading): Promise<void> {
  unsupported()
}

export async function savePdf(_data: ExportData, _fileName: string, _heading: ExportHeading): Promise<void> {
  unsupported()
}
