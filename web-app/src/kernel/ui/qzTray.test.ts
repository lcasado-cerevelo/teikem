// Lote F-G1 — cliente de QZ Tray sobre una librería falsa: estados (sin librería, sin QZ Tray abierto, conectado), certificado y firma
// del servidor (SHA512), impresoras detectadas y el PDF base64 al tamaño exacto.
import { describe, expect, it, vi } from 'vitest'
import { createQzClient, type QzApi, type QzSecurity } from './qzTray'

function fakeQz(opts: { connectFails?: boolean } = {}) {
  let active = false
  const calls: { cert?: () => Promise<string | undefined>; sign?: (s: string) => Promise<string | undefined>; algorithm?: string; printed?: unknown[] } = {}
  const api: QzApi = {
    websocket: {
      isActive: () => active,
      connect: vi.fn(async () => {
        if (opts.connectFails) throw new Error('Unable to establish connection with QZ')
        active = true
      }),
    },
    printers: { find: vi.fn(async () => ['ZDesigner ZD420', 'HP LaserJet']) },
    configs: { create: vi.fn((printer: string, o?: Record<string, unknown>) => ({ printer, ...o })) },
    print: vi.fn(async (config: unknown, data: unknown[]) => {
      calls.printed = [config, data]
    }),
    security: {
      setCertificatePromise: (fn) => {
        calls.cert = () => new Promise((res, rej) => fn(res, rej))
      },
      setSignatureAlgorithm: (a) => {
        calls.algorithm = a
      },
      setSignaturePromise: (fn) => {
        calls.sign = (s) => new Promise((res, rej) => fn(s)(res, rej))
      },
    },
  }
  return { api, calls }
}

const security: QzSecurity = { certificate: async () => '-----BEGIN CERTIFICATE-----\nX\n-----END CERTIFICATE-----\n', sign: async (s) => `firma(${s})` }

describe('createQzClient', () => {
  it('sin librería: noLibrary; QZ Tray cerrado: notRunning', async () => {
    expect(await createQzClient(security, async () => null).connect()).toBe('noLibrary')
    const { api } = fakeQz({ connectFails: true })
    const client = createQzClient(security, async () => api)
    expect(await client.connect()).toBe('notRunning')
    await expect(client.findPrinters()).rejects.toThrow('notRunning')
  })

  it('conectado: usa el certificado y la firma del servidor con SHA512, detecta impresoras e imprime el PDF al tamaño exacto', async () => {
    const { api, calls } = fakeQz()
    const client = createQzClient(security, async () => api)
    expect(await client.connect()).toBe('connected')
    expect(calls.algorithm).toBe('SHA512')
    expect(await calls.cert!()).toContain('BEGIN CERTIFICATE')
    expect(await calls.sign!('abc')).toBe('firma(abc)')
    expect(await client.findPrinters()).toEqual(['HP LaserJet', 'ZDesigner ZD420'])
    await client.printPdf('ZDesigner ZD420', 'QUJD', { widthIn: 4, heightIn: 6, rotate: false })
    expect(calls.printed).toEqual([
      { printer: 'ZDesigner ZD420', size: { width: 4, height: 6 }, units: 'in', margins: 0, scaleContent: false, rotation: 0, copies: 1 },
      [{ type: 'pixel', format: 'pdf', flavor: 'base64', data: 'QUJD' }],
    ])
    // ya conectado: no vuelve a abrir la conexión
    await client.connect()
    expect(api.websocket.connect).toHaveBeenCalledTimes(1)
  })

  it('sin certificado en el servidor: QZ Tray recibe vacío (preguntará «Permitir»)', async () => {
    const { api, calls } = fakeQz()
    await createQzClient({ certificate: async () => null, sign: async () => null }, async () => api).connect()
    expect(await calls.cert!()).toBeUndefined()
    expect(await calls.sign!('x')).toBeUndefined()
  })
})
