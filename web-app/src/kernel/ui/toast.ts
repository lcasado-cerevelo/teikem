// API de avisos: `toast.success(t('clients.saved'))`, `toast.error(applyProblemDetails(err).title)`.
// El contenedor se monta solo (portal propio en <body>) la primera vez que se usa: no depende del shell.
import { createElement } from 'react'
import { createRoot } from 'react-dom/client'
import { Toaster } from './Toaster'
import { pushToast, type ToastKind } from './toastStore'

let mounted = false

function ensureHost(): void {
  if (mounted || typeof document === 'undefined') return
  mounted = true
  const host = document.createElement('div')
  host.id = 'toast-root'
  document.body.appendChild(host)
  createRoot(host).render(createElement(Toaster))
}

function show(kind: ToastKind, message: string): void {
  ensureHost()
  pushToast(kind, message)
}

export const toast = {
  success: (message: string) => show('success', message),
  error: (message: string) => show('error', message),
  info: (message: string) => show('info', message),
}
