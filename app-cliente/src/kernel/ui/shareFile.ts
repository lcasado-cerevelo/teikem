// Guardar un archivo en el aparato y abrir el menú de compartir (app instalada): Excel, CSV y PDF salen como archivos de verdad (2026-10-24, pedido de Luis: el estándar es CSV, PDF y Excel en todas partes).
// El archivo se escribe en la carpeta temporal de la app y se entrega al sistema (guardar en Archivos, WhatsApp, correo, imprimir…). La web baja el archivo por su cuenta (shareFile.web.ts no se usa).
import { File, Paths } from 'expo-file-system'
import * as Sharing from 'expo-sharing'

export const MIME = {
  xlsx: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
  csv: 'text/csv',
  pdf: 'application/pdf',
} as const

const UTI = {
  xlsx: 'org.openxmlformats.spreadsheetml.sheet',
  csv: 'public.comma-separated-values-text',
  pdf: 'com.adobe.pdf',
} as const

export type ShareKind = keyof typeof MIME

/** Escribe `content` en la carpeta temporal con ese nombre y lo comparte. Devuelve la dirección del archivo. */
export async function shareFile(fileName: string, content: string | Uint8Array, kind: ShareKind, dialogTitle?: string): Promise<string> {
  const file = new File(Paths.cache, fileName)
  file.create({ overwrite: true })
  file.write(content)
  if (!(await Sharing.isAvailableAsync())) throw new Error('Este aparato no puede compartir archivos.')
  await Sharing.shareAsync(file.uri, { mimeType: MIME[kind], UTI: UTI[kind], dialogTitle: dialogTitle ?? fileName })
  return file.uri
}

/** Pasa un PDF ya creado (en cualquier carpeta) a la carpeta temporal con el nombre bueno y lo comparte. */
export async function sharePdfAt(uri: string, fileName: string, dialogTitle?: string): Promise<string> {
  const target = new File(Paths.cache, fileName)
  if (target.exists) target.delete()
  await new File(uri).copy(target)
  if (!(await Sharing.isAvailableAsync())) throw new Error('Este aparato no puede compartir archivos.')
  await Sharing.shareAsync(target.uri, { mimeType: MIME.pdf, UTI: UTI.pdf, dialogTitle: dialogTitle ?? fileName })
  return target.uri
}

/** El archivo en base64 (para meter una imagen en el HTML del PDF). null si no se pudo leer. */
export async function readBase64(uri: string): Promise<string | null> {
  try {
    return await new File(uri).base64()
  } catch {
    return null
  }
}
