// En la web los archivos se bajan directo (exportFiles.web.ts y csv.ts); este módulo existe para que la app compile y no carga nada nativo.
export const MIME = { xlsx: '', csv: '', pdf: '' } as const
export type ShareKind = keyof typeof MIME

const unsupported = (): never => {
  throw new Error('Compartir archivos solo existe en la app instalada.')
}

export async function shareFile(_fileName: string, _content: string | Uint8Array, _kind: ShareKind, _dialogTitle?: string): Promise<string> {
  return unsupported()
}

export async function sharePdfAt(_uri: string, _fileName: string, _dialogTitle?: string): Promise<string> {
  return unsupported()
}

export async function readBase64(_uri: string): Promise<string | null> {
  return null
}
