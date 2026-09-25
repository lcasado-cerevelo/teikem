export const meta = {
  name: 'lote-implementar',
  description: 'Implementar y verificar un lote de Teikem a partir de un plan aprobado: piezas en paralelo, integración de archivos compartidos, build/test, revisión por 4 lentes con verificación adversarial, corrección hasta quedar limpio, y documento de decisiones.',
  whenToUse: 'Después de lote-diseno y de que el dueño apruebe el plan. args: { lote, titulo, plan: <objeto plan aprobado>, fecha: "YYYY-MM-DD" }',
  phases: [
    { title: 'Implementar', detail: 'una pieza por agente, luego integración de archivos compartidos y build' },
    { title: 'Verificar', detail: '4 lentes → refutación adversarial → corrección; hasta 2 rondas limpias' },
    { title: 'Documentar', detail: 'docs/loteN-decisiones.md y manual funcional en docs/manual/' },
  ],
}

const a = args || {}
const lote = a.lote || '?'
const titulo = a.titulo || 'módulo'
const plan = a.plan
if (!plan || !plan.piezas) throw new Error('args.plan (con piezas) es obligatorio: pásale el plan aprobado de lote-diseno.')

const RESULT = { type: 'object', properties: { archivos: { type: 'array', items: { type: 'string' } }, supuestos: { type: 'array', items: { type: 'string' } }, compilado: { type: 'boolean' }, notas: { type: 'string' } }, required: ['archivos', 'compilado'] }
const FINDINGS = { type: 'object', properties: { hallazgos: { type: 'array', items: { type: 'object', properties: { archivo: { type: 'string' }, linea: { type: 'number' }, resumen: { type: 'string' }, detalle: { type: 'string' }, severidad: { type: 'string' } }, required: ['archivo', 'resumen', 'detalle'] } } }, required: ['hallazgos'] }
const VERDICT = { type: 'object', properties: { real: { type: 'boolean' }, evidencia: { type: 'string' }, arreglo: { type: 'string' } }, required: ['real', 'evidencia'] }

const contexto = `Lote ${lote} (${titulo}). Plan aprobado:\n${JSON.stringify(plan, null, 1)}`

phase('Implementar')
const piezasParalelas = plan.piezas.filter(p => !p.tocaCompartidos)
const piezasCompartidas = plan.piezas.filter(p => p.tocaCompartidos)
log(`${piezasParalelas.length} piezas en paralelo, ${piezasCompartidas.length} de integración.`)

const hechas = (await parallel(piezasParalelas.map(p => () =>
  agent(`${contexto}\n\nImplementa SOLO la pieza "${p.nombre}": ${p.descripcion}\nArchivos: ${p.archivos.join(', ')}. No toques archivos compartidos. Otras piezas se están implementando a la vez en otros archivos: si necesitas un tipo que aún no existe, decláralo en tus propios archivos con el nombre que dice el plan.`,
    { agentType: 'implementer', label: `pieza:${p.nombre}`, schema: RESULT })))).filter(Boolean)

for (const p of piezasCompartidas) {
  await agent(`${contexto}\n\nYa se implementaron estas piezas: ${JSON.stringify(hechas.map(h => h.archivos))}.\nImplementa ahora la pieza de integración "${p.nombre}": ${p.descripcion}\nArchivos: ${p.archivos.join(', ')}. Registra DbSets, configuraciones, servicios, fuentes de datos y permisos que las otras piezas necesiten; aplica los cambios de SQL del plan en Diseño/ (estructura y seed) en el orden de capas correcto. Luego compila y prueba si hay dotnet.`,
    { agentType: 'implementer', label: `integracion:${p.nombre}`, schema: RESULT })
}

const build = await agent(`${contexto}\n\nEjecuta \`dotnet build Teikem.sln\` y \`dotnet test Teikem.sln\` si dotnet existe; corrige todo error de compilación o prueba hasta que pasen (cambios mínimos). Si no hay dotnet, revisa el diff completo (git diff) con ojo de compilador y corrige lo evidente. Devuelve compilado=true solo si viste el build pasar.`,
  { agentType: 'implementer', label: 'build+test', schema: RESULT })
log(build && build.compilado ? 'Build y pruebas en verde.' : 'Sin SDK local: la compilación se verificará en CI.')

phase('Verificar')
const LENTES = ['compile-ef', 'tenant-security', 'spec', 'tests']
const vistos = new Set()
let limpias = 0, ronda = 0, corregidos = 0
while (limpias < 2 && ronda < 4) {
  ronda++
  const encontrados = (await parallel(LENTES.map(l => () =>
    agent(`${contexto}\n\nRonda ${ronda}. Revisa el diff del lote (git diff origin/master...HEAD y archivos nuevos) con la lente "${l}". Reporta solo hallazgos verificables.`,
      { agentType: 'reviewer', label: `revisar:${l}`, phase: 'Verificar', schema: FINDINGS })))).filter(Boolean).flatMap(r => r.hallazgos)
  const frescos = encontrados.filter(h => { const k = `${h.archivo}:${h.resumen}`.toLowerCase(); if (vistos.has(k)) return false; vistos.add(k); return true })
  log(`Ronda ${ronda}: ${encontrados.length} hallazgos, ${frescos.length} nuevos.`)
  if (!frescos.length) { limpias++; continue }
  const juzgados = await parallel(frescos.map(h => () =>
    parallel([0, 1].map(v => () => agent(`Intenta refutar este hallazgo (verificador ${v + 1}):\n${JSON.stringify(h, null, 1)}\nSi no estás seguro, real=false.`, { agentType: 'verifier', label: `refutar:${h.archivo.split('/').pop()}`, phase: 'Verificar', schema: VERDICT })))
      .then(vs => ({ h, real: vs.filter(Boolean).filter(x => x.real).length >= 2, arreglo: (vs.filter(Boolean).find(x => x.real) || {}).arreglo }))))
  const reales = juzgados.filter(Boolean).filter(j => j.real)
  log(`Ronda ${ronda}: ${reales.length} hallazgos confirmados.`)
  if (!reales.length) { limpias++; continue }
  limpias = 0
  await agent(`${contexto}\n\nCorrige estos hallazgos confirmados con cambios mínimos y vuelve a compilar/probar si hay dotnet:\n${JSON.stringify(reales.map(r => ({ ...r.h, arreglo: r.arreglo })), null, 1)}`, { agentType: 'implementer', label: `corregir:ronda${ronda}`, phase: 'Verificar', schema: RESULT })
  corregidos += reales.length
}
if (ronda >= 4 && limpias < 2) log('Tope de 4 rondas alcanzado: revisar manualmente los últimos hallazgos.')

phase('Documentar')
const doc = await agent(`${contexto}\n\nEscribe docs/lote${lote}-decisiones.md con el formato de docs/lote1-decisiones.md. Fecha: ${a.fecha || 'sin fecha'}. Hallazgos corregidos en verificación: ${corregidos}. Build local: ${build && build.compilado ? 'sí' : 'no (CI)'}. Lista las decisiones abiertas del plan y todo lo que quedó fuera. También agrega al final de scripts/smoke.sh los pasos del plan ("smoke") si existen, sin romper los anteriores.`,
  { agentType: 'scribe', label: 'decisiones', schema: RESULT })

const manual = await agent(`${contexto}\n\nEscribe el MANUAL FUNCIONAL del lote ${lote} (${titulo}) en docs/manual/ siguiendo tu definición: capítulo docs/manual/${String(lote).padStart(2, '0')}-<slug-del-modulo>.md (funcionalidades, quién puede, cómo se usa, validaciones con el mensaje de error exacto del código y su código HTTP, estatus y transiciones con efectos, preguntas frecuentes), agrega las preguntas y respuestas del lote a docs/manual/faq.md (créalo si no existe; si existe, anexa una sección del lote sin borrar lo anterior) y actualiza el índice docs/manual/README.md (créalo si no existe). Lee los controladores, servicios, efectos de estatus y excepciones reales del lote antes de escribir; no inventes mensajes.`,
  { agentType: 'scribe', label: 'manual', schema: RESULT })

return { piezas: hechas.length + piezasCompartidas.length, buildLocal: !!(build && build.compilado), rondas: ronda, corregidos, doc, manual }
