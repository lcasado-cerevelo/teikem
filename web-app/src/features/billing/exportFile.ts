// Descarga del archivo de exportación (Lote 78).
import type { ExportFileDto } from './billingApi'

/** Descarga el archivo armado por el servidor (con BOM para que Excel lo abra en UTF-8). */
export function downloadExportFile(file: ExportFileDto): void {
  const blob = new Blob(['﻿', file.text ?? ''], { type: file.contentType ?? 'text/plain;charset=utf-8' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = file.fileName ?? 'exportacion.csv'
  a.rel = 'noopener'
  document.body.appendChild(a)
  a.click()
  a.remove()
  setTimeout(() => URL.revokeObjectURL(url), 0)
}
