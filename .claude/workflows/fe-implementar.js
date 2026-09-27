export const meta = {
  name: 'fe-implementar',
  description: 'Implementar un lote del frontend web con el modelo de bajo consumo: núcleo primero, pantallas en paralelo, compuerta determinista (npm run check), revisión de 2 lentes, refutación solo de hallazgos altos, máximo 2 rondas, Playwright y documentación.',
  whenToUse: 'Después de que el dueño apruebe el plan de un lote de frontend (docs/frontend/loteFN-plan.json). args: { lote: "F1", titulo, plan, fecha }',
  phases: [
    { title: 'Implementar', detail: 'grupos por orden: núcleo (opus) y pantallas (sonnet), cada pieza pasa npm run check' },
    { title: 'Verificar', detail: '2 lentes → refutar solo severidad alta → corregir; hasta que no queden hallazgos altos' },
    { title: 'Recorrido', detail: 'Playwright contra el API real, con capturas para el manual' },
    { title: 'Documentar', detail: 'docs/frontend/loteFN-decisiones.md y capítulo del manual' },
  ],
}

const a = args || {}
const lote = a.lote || 'F?'
const titulo = a.titulo || 'lote de frontend'
const plan = a.plan
if (!plan || !plan.piezas) throw new Error('args.plan (con piezas) es obligatorio.')

const RESULT = { type: 'object', properties: { archivos: { type: 'array', items: { type: 'string' } }, supuestos: { type: 'array', items: { type: 'string' } }, checkVerde: { type: 'boolean' }, notas: { type: 'string' } }, required: ['archivos', 'checkVerde'] }
const FINDINGS = { type: 'object', properties: { hallazgos: { type: 'array', items: { type: 'object', properties: { archivo: { type: 'string' }, linea: { type: 'number' }, resumen: { type: 'string' }, detalle: { type: 'string' }, severidad: { type: 'string', enum: ['alta', 'media', 'baja'] } }, required: ['archivo', 'resumen', 'detalle', 'severidad'] } } }, required: ['hallazgos'] }
const VERDICT = { type: 'object', properties: { real: { type: 'boolean' }, evidencia: { type: 'string' }, arreglo: { type: 'string' } }, required: ['real', 'evidencia'] }

// Contexto mínimo: el agente lee KIT.md y su pieza; el plan completo queda en disco.
const cabecera = `Lote ${lote} del frontend (${titulo}). Trabajas en web-app/. Lee primero web-app/KIT.md. El plan completo está en docs/frontend/lote${lote}-plan.md: consulta solo lo que tu pieza necesite.`
// Los agentes fe-* de .claude/agents solo se cargan al iniciar la sesión; mientras tanto se usan los agentes base con
// modelo y esfuerzo explícitos y sus reglas en el prompt (reglas = el contenido de .claude/agents/fe-*.md).
const REGLAS_FE = `Reglas del implementador de frontend: lee primero web-app/KIT.md; usa SIEMPRE el cliente generado (src/kernel/api) y sus tipos, nunca DTOs a mano ni any; copia los patrones del kit; textos con t('clave') en src/kernel/i18n/{es,en}.json; permisos y módulos con <Can perm> / <ModuleGate module> con los códigos exactos del API; errores del servidor con applyProblemDetails; responsive a 360 px sin scroll horizontal; identificadores en inglés, comentarios en español; termina con "npm run check" en verde (tsc, oxlint, vitest, build). Si tocas src/kernel, documenta en KIT.md y agrega una prueba.`
const optsPieza = (p) => (p.agente === 'core'
  ? { agentType: 'implementer', model: 'opus', effort: 'high' }
  : { agentType: 'implementer', model: 'sonnet', effort: 'medium' })
const REGLAS_REV = { paridad: 'Lente PARIDAD: las pantallas usan los tipos generados (schema.d.ts), los permisos, módulos, códigos de estatus y mensajes reales del API (web-app/openapi.json y controladores); severidad alta = permiso o módulo mal aplicado, dato del API con nombre/tipo incorrecto, acción que el API rechazaría siempre, pantalla rota en móvil.', pruebas: 'Lente PRUEBAS Y CONVENCIONES: convenciones de interfaz del documento maestro (ordenar columnas, buscador libre QBox aplicado después de los filtros, sin scroll horizontal, chips sin envolver, idioma sin reinicio, responsive 360 px), pruebas unitarias existentes y suficientes, recorrido Playwright; severidad media = convención incumplida; baja = estilo.' }

phase('Implementar')
const ordenes = [...new Set(plan.piezas.map(p => p.orden || 1))].sort((x, y) => x - y)
const hechas = []
for (const o of ordenes) {
  const grupo = plan.piezas.filter(p => (p.orden || 1) === o)
  log(`Grupo ${o}: ${grupo.map(p => p.nombre).join(' | ')}`)
  const res = (await parallel(grupo.map(p => () =>
    agent(`${cabecera}\n${REGLAS_FE}\n\nImplementa SOLO la pieza "${p.nombre}":\n${p.descripcion}\nArchivos previstos: ${(p.archivos || []).join(', ')}.\nOtras piezas del mismo grupo se implementan a la vez en otros archivos: no toques archivos fuera de los tuyos salvo KIT.md, i18n (agrega claves, no borres) y routes.tsx (agrega tu ruta). Termina con npm run check en verde.`,
      { ...optsPieza(p), label: `pieza:${p.nombre}`, schema: RESULT })))).filter(Boolean)
  hechas.push(...res)
}

const check = await agent(`${cabecera}\n\nIntegración: ejecuta "npm run check" en web-app/ y corrige lo que falle (conflictos entre piezas, rutas duplicadas, claves i18n faltantes en es/en, tipos). Cambios mínimos. Devuelve checkVerde=true solo si viste pasar tsc, lint, vitest y build.`,
  { agentType: 'implementer', model: 'opus', effort: 'high', label: 'integrar+check', schema: RESULT })
log(check && check.checkVerde ? 'npm run check en verde.' : 'ATENCIÓN: npm run check no quedó en verde.')

phase('Verificar')
const LENTES = ['paridad', 'pruebas']
const vistos = new Set()
// Decisión de Luis (2026-09-27): se revisa hasta que no queden hallazgos altos confirmados; el tope (12) es solo un freno de seguridad.
const maxRondasFe = Number(a.maxRondas) || 12
let ronda = 0, corregidos = 0, huboAltas = true
while (ronda < maxRondasFe && huboAltas) {
  ronda++
  const encontrados = (await parallel(LENTES.map(l => () =>
    agent(`${cabecera}\n\nRonda ${ronda}. Revisa SOLO el frontend: el diff del lote (git diff HEAD -- web-app y archivos nuevos bajo web-app/). ${REGLAS_REV[l]} No reportes lo que npm run check ya atrapa. Máximo 15 hallazgos, los más graves primero, con severidad alta/media/baja. No edites.`,
      { agentType: 'reviewer', model: 'opus', effort: 'high', label: `revisar:${l}`, phase: 'Verificar', schema: FINDINGS })))).filter(Boolean).flatMap(r => r.hallazgos)
  const frescos = encontrados.filter(h => { const k = `${h.archivo}:${h.resumen}`.toLowerCase(); if (vistos.has(k)) return false; vistos.add(k); return true })
  const altas = frescos.filter(h => h.severidad === 'alta')
  const medias = frescos.filter(h => h.severidad === 'media')
  log(`Ronda ${ronda}: ${frescos.length} hallazgos nuevos (${altas.length} altas, ${medias.length} medias).`)
  const juzgadas = await parallel(altas.map(h => () =>
    agent(`Intenta refutar este hallazgo de severidad alta del frontend:\n${JSON.stringify(h, null, 1)}\nSi no estás seguro, real=false.`, { agentType: 'verifier', model: 'opus', effort: 'medium', label: `refutar:${h.archivo.split('/').pop()}`, phase: 'Verificar', schema: VERDICT })
      .then(v => ({ h, real: !!(v && v.real), arreglo: v && v.arreglo }))))
  const realesAltas = juzgadas.filter(Boolean).filter(j => j.real)
  const aCorregir = [...realesAltas.map(j => ({ ...j.h, arreglo: j.arreglo })), ...medias]
  // Se sigue revisando mientras haya algo que corregir (alta confirmada o media): la ronda limpia es la que no corrige nada.
  huboAltas = aCorregir.length > 0
  if (!aCorregir.length) { log('Ronda limpia: nada que corregir.'); break }
  await agent(`${cabecera}\n${REGLAS_FE}\n\nCorrige estos hallazgos con cambios mínimos (las 'alta' están confirmadas; las 'media' son convenciones de interfaz) y termina con npm run check en verde:\n${JSON.stringify(aCorregir, null, 1)}`,
    { agentType: 'implementer', model: 'opus', effort: 'high', label: `corregir:ronda${ronda}`, phase: 'Verificar', schema: RESULT })
  corregidos += aCorregir.length
}

phase('Recorrido')
const e2e = await agent(`${cabecera}\n${REGLAS_FE}\n\nEscribe o actualiza el recorrido Playwright del lote en web-app/e2e/${String(lote).toLowerCase()}.spec.ts según la sección "recorrido" del plan: ${JSON.stringify(plan.recorrido || [], null, 1)}\nEl API real ya corre en http://localhost:5000 con la BD inicializada (usuarios demo del README). Usa sufijos de tiempo en los datos que crees. Guarda capturas de cada pantalla en docs/manual/frontend/img/${String(lote).toLowerCase()}-<pantalla>.png (proyecto escritorio) para el manual. Ejecuta "npx playwright test" (ambos proyectos) y corrige la aplicación o el recorrido hasta que pase. Devuelve checkVerde=true solo si viste pasar Playwright.`,
  { agentType: 'implementer', model: 'opus', effort: 'high', label: 'playwright', schema: RESULT })
log(e2e && e2e.checkVerde ? 'Playwright en verde.' : 'ATENCIÓN: Playwright no quedó en verde.')

phase('Documentar')
const doc = await agent(`${cabecera}\n\nEscribe docs/frontend/lote${lote}-decisiones.md (formato de docs/lote1-decisiones.md: qué se construyó por pantalla con su ruta, cómo se prueba, decisiones a revisar, qué quedó fuera; fecha ${a.fecha || 'sin fecha'}; hallazgos corregidos: ${corregidos}; npm run check: ${check && check.checkVerde ? 'verde' : 'no verificado'}; Playwright: ${e2e && e2e.checkVerde ? 'verde' : 'no verificado'}) y el capítulo del manual de usuario docs/manual/frontend/${String(lote).toLowerCase()}-<slug>.md para el usuario final (una sección por pantalla: para qué sirve, cómo se llega desde el menú, qué se ve, qué hace cada botón, qué permiso exige, mensajes que puede ver y qué hacer), incrustando las capturas de docs/manual/frontend/img/. Actualiza docs/manual/README.md con la sección "Manual de pantallas". Lee los componentes reales antes de escribir; no inventes textos.`,
  { agentType: 'scribe', label: 'docs+manual', schema: RESULT })

return { piezas: hechas.length, checkVerde: !!(check && check.checkVerde), rondas: ronda, corregidos, playwright: !!(e2e && e2e.checkVerde), doc }
