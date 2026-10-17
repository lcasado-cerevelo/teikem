// HTML raíz de la versión web: vínculo al manifiesto (para «Poner en mi escritorio»), color del tema y la ventana de visualización móvil.
import { ScrollViewStyleReset } from 'expo-router/html'
import type { PropsWithChildren } from 'react'

export default function Root({ children }: PropsWithChildren) {
  return (
    <html lang="es">
      <head>
        <meta charSet="utf-8" />
        <meta httpEquiv="X-UA-Compatible" content="IE=edge" />
        <meta name="viewport" content="width=device-width, initial-scale=1, shrink-to-fit=no" />
        <meta name="theme-color" content="#0B2C66" />
        <link rel="manifest" href="/manifest.json" />
        <link rel="apple-touch-icon" href="/icon-192.png" />
        <title>Teikem Clientes</title>
        <ScrollViewStyleReset />
      </head>
      <body>{children}</body>
    </html>
  )
}
