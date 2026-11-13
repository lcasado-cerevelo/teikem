// Preparación de datos de la escena 7 (Análisis de ventas), fuera de cámara, por API: rutas, representantes ficticios, líneas, importación del
// historial de demostración, enlaces de productos, cuotas y fecha de corte. Parte del estado E06 (o E00). Idempotente a medias: correr sobre un estado limpio.
import { readFileSync } from 'node:fs'
import { test, expect } from '@playwright/test'
import { tokensFueraDeCamara } from '../ayudas'
import { API_URL } from '../../staffHelpers'

test.setTimeout(300_000)
test('P07 - preparar análisis de ventas', async ({ request }) => {
  const tokens = await tokensFueraDeCamara(2)
  const h = { Authorization: `Bearer ${String(tokens.accessToken)}` }
  const A = `${API_URL}/api/v1/sales-analysis`
  const j = async <T>(r: Promise<import('@playwright/test').APIResponse>, what: string): Promise<T> => { const x = await r; expect(x.ok(), `${what}: ${x.status()} ${await x.text()}`).toBeTruthy(); const t = await x.text(); return (t ? JSON.parse(t) : null) as T }

  // 1) rutas
  const rutas = ['Cadenas', 'Norte', 'Metro', 'Este', 'Sur', 'Oeste']
  const existentes = await j<{ id: number; name: string }[]>(request.get(`${A}/routes?includeInactive=true`, { headers: h }), 'rutas')
  const rid: Record<string, number> = {}
  for (const [i, n] of rutas.entries()) {
    const e = existentes.find((x) => x.name === n)
    rid[n] = e ? e.id : (await j<{ id: number }>(request.post(`${A}/routes`, { headers: h, data: { name: n, sortOrder: (i + 1) * 10 } }), 'ruta')).id
  }
  // 2) representantes (los 7 salen del campo «Representante»; se renombran a nombres ficticios y se les da ruta)
  await j(request.post(`${A}/reps/from-custom-field`, { headers: h }), 'reps del campo')
  const reps = await j<{ id: number; name: string; clients?: number; clientCount?: number }[]>(request.get(`${A}/reps?includeInactive=false`, { headers: h }), 'reps')
  console.log('reps', reps.length, JSON.stringify(reps.slice(0, 2)))
  const nombres = ['Ana Torres', 'Luis Vega', 'Carmen Rivera', 'Pedro Santos', 'Marta Ortiz', 'Jorge Medina', 'Sofía Nieves']
  const ruta7 = ['Cadenas', 'Norte', 'Metro', 'Este', 'Sur', 'Oeste', 'Oeste']
  for (const [i, r] of reps.entries()) await j(request.patch(`${A}/reps/${r.id}`, { headers: h, data: { name: nombres[i % nombres.length], routeId: rid[ruta7[i % ruta7.length]] } }), 'rep')
  // 3) líneas y marcas
  const lineas: Record<string, string[]> = { Prodigy: ['PRODIGY'], Padimont: ['PADIMONT'], 'Global Imports': ['GLOBAL', 'MEDPRIDE', 'AMERICAN RED CROSS'], Cartones: ['CARTONES'] }
  const lex = await j<{ id: number; name: string }[]>(request.get(`${A}/lines?includeInactive=true`, { headers: h }), 'líneas')
  for (const [n, marcas] of Object.entries(lineas)) {
    const e = lex.find((x) => x.name === n)
    const id = e ? e.id : (await j<{ id: number }>(request.post(`${A}/lines`, { headers: h, data: { name: n } }), 'línea')).id
    await j(request.put(`${A}/lines/${id}/brands`, { headers: h, data: { brands: marcas } }), 'marcas')
  }
  // 4) importar el historial de demostración (simula y luego importa de verdad con fecha de corte)
  const buffer = readFileSync('F:/Download/TeikemVideo/solutions-proceso/datos/ventas-demo.csv')
  const file = { name: 'SalesByItemDetail.CSV', mimeType: 'text/csv', buffer }
  const sim = await j<Record<string, unknown>>(request.post(`${A}/import`, { headers: h, multipart: { salesFile: file, dryRun: 'true' } }), 'simulación')
  console.log('simulación', JSON.stringify(sim).slice(0, 1200))
  const real = await j<Record<string, unknown>>(request.post(`${A}/import`, { headers: h, multipart: { salesFile: file, dryRun: 'false', cutoffDate: '2026-11-06' } }), 'importación')
  console.log('importado', JSON.stringify(real).slice(0, 300))
  // 5) enlazar a mano los guantes y los underpads (fuera de cámara)
  const enlaces: [string, number][] = [['NECH 1001 (Padimont Gloves Small 1/100ct.)', 601], ['NECH 1002 (Padimont Gloves Medium 1/100ct.)', 602], ['NECH 1003 (Padimont Gloves Large 1/100ct.)', 603],
    ['NECH 1004 (Padimont Gloves X-Large 1/100ct.)', 604], ['PD-23*36/15/10 (Underpads 23*36 15/10 box 150)', 607]]
  for (const [raw, productId] of enlaces) await j(request.post(`${A}/unmatched/link`, { headers: h, data: { kind: 'PRODUCT', raw, productId } }), 'enlace')
  // 6) cuotas de agosto a octubre y noviembre: cada representante con su cuota (≈ su venta del mes con un poco de holgura)
  const ventaMes: Record<string, Record<number, number>> = {}
  for (const mes of ['2026-08', '2026-09', '2026-10']) {
    const by = await j<{ reps: { repId: number | null; sales: number }[] }>(request.get(`${A}/by-rep?month=${mes}`, { headers: h }), 'por representante')
    ventaMes[mes] = Object.fromEntries(by.reps.filter((r) => r.repId).map((r) => [r.repId as number, r.sales]))
  }
  ventaMes['2026-11'] = ventaMes['2026-10']
  for (const mes of Object.keys(ventaMes)) {
    const items = Object.entries(ventaMes[mes]).map(([repId, v]) => ({ repId: Number(repId), amount: Math.max(500, Math.round((v * 1.04) / 100) * 100) }))
    await j(request.put(`${A}/quotas/${mes}`, { headers: h, data: { items } }), 'cuotas')
  }
  // 7) sin avisos: clientes sin representante a la primera ruta y el cliente suelto enlazado
  const sin = await j<{ items?: { clientId?: number; id?: number }[] }>(request.get(`${A}/clients?unassigned=true&take=100`, { headers: h }), 'sin rep')
  const ids = (sin.items ?? []).map((c) => (c.clientId ?? c.id) as number).filter(Boolean)
  if (ids.length) await j(request.post(`${A}/clients/assign-rep`, { headers: h, data: { clientIds: ids, repId: reps[0].id } }), 'asignar')
  await j(request.post(`${A}/unmatched/link`, { headers: h, data: { kind: 'CLIENT', raw: 'CLIENTE EJEMPLO 1', clientId: 3 } }), 'enlace cliente')
  const st = await j<Record<string, unknown>>(request.get(`${A}/status`, { headers: h }), 'estado')
  console.log('estado', JSON.stringify(st).slice(0, 700))
})
