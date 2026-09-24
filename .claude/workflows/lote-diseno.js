export const meta = {
  name: 'lote-diseno',
  description: 'Entender y diseñar un lote de Teikem: lectores de especificación en paralelo, panel de 3 diseños juzgados y síntesis. Devuelve el plan para revisión humana; no escribe código.',
  whenToUse: 'Al arrancar un lote (módulo del documento maestro) antes de implementar. args: { lote, titulo, seccionesDoc: ["## 1. Clientes y contratos"], tablas: ["Client","Contract",...] }',
  phases: [
    { title: 'Entender', detail: '3 lectores (documento, SQL, código existente) + crítico de completitud' },
    { title: 'Diseñar', detail: '3 diseños independientes → 2 jueces → síntesis' },
  ],
}

const a = args || {}
const lote = a.lote || '?'
const titulo = a.titulo || 'módulo'
const secciones = (a.seccionesDoc || []).join(', ')
const tablas = (a.tablas || []).join(', ')

const SPEC = {
  type: 'object',
  properties: {
    entidades: { type: 'array', items: { type: 'object', properties: { tabla: { type: 'string' }, existeEnSql: { type: 'boolean' }, notas: { type: 'string' } }, required: ['tabla', 'existeEnSql'] } },
    reglas: { type: 'array', items: { type: 'object', properties: { id: { type: 'string' }, regla: { type: 'string' }, fuente: { type: 'string' }, esRequisitoReal: { type: 'boolean' } }, required: ['id', 'regla', 'fuente', 'esRequisitoReal'] } },
    endpoints: { type: 'array', items: { type: 'object', properties: { metodo: { type: 'string' }, ruta: { type: 'string' }, permiso: { type: 'string' }, modulo: { type: 'string' } }, required: ['metodo', 'ruta'] } },
    estatus: { type: 'array', items: { type: 'object', properties: { dominio: { type: 'string' }, efectos: { type: 'array', items: { type: 'string' } } }, required: ['dominio'] } },
    vacios: { type: 'array', items: { type: 'string' } },
    reutilizar: { type: 'array', items: { type: 'string' } },
  },
  required: ['entidades', 'reglas', 'endpoints', 'vacios'],
}

const PLAN = {
  type: 'object',
  properties: {
    enfoque: { type: 'string' },
    resumen: { type: 'string' },
    cambiosSql: { type: 'array', items: { type: 'string' } },
    piezas: { type: 'array', items: { type: 'object', properties: {
      nombre: { type: 'string' }, descripcion: { type: 'string' },
      archivos: { type: 'array', items: { type: 'string' } },
      tocaCompartidos: { type: 'boolean' },
      dependeDe: { type: 'array', items: { type: 'string' } },
    }, required: ['nombre', 'descripcion', 'archivos', 'tocaCompartidos'] } },
    decisiones: { type: 'array', items: { type: 'object', properties: { decision: { type: 'string' }, alternativa: { type: 'string' } }, required: ['decision'] } },
    pruebas: { type: 'array', items: { type: 'string' } },
    smoke: { type: 'array', items: { type: 'string' } },
  },
  required: ['enfoque', 'resumen', 'piezas', 'decisiones', 'pruebas'],
}

const SCORE = {
  type: 'object',
  properties: { puntajes: { type: 'array', items: { type: 'object', properties: { indice: { type: 'number' }, puntaje: { type: 'number' }, razon: { type: 'string' } }, required: ['indice', 'puntaje'] } }, mejoresIdeas: { type: 'array', items: { type: 'string' } } },
  required: ['puntajes'],
}

phase('Entender')
log(`Lote ${lote}: ${titulo}. Leyendo especificación desde 3 ángulos.`)
const lecturas = (await parallel([
  () => agent(`Extrae la especificación del Lote ${lote} (${titulo}) leyendo SOLO el documento maestro Diseño/logistica-funcionalidades-maestro.md: las secciones ${secciones} y toda entrada de la bitácora final que las modifique. Devuelve entidades, reglas (con línea del documento), endpoints implícitos, dominios de estatus y vacíos.`, { agentType: 'spec-reader', label: 'leer:documento', schema: SPEC }),
  () => agent(`Extrae la especificación del Lote ${lote} (${titulo}) leyendo SOLO el SQL: tablas ${tablas} en Diseño/logistica-db-estructura.sql (columnas, FKs, índices, comentarios) y sus catálogos/estatus en Diseño/logistica-db-seed.sql. Devuelve entidades (existeEnSql=true), reglas implícitas en el esquema (unicidad, computadas, defaults) y vacíos (columnas que el documento pide y no están).`, { agentType: 'spec-reader', label: 'leer:sql', schema: SPEC }),
  () => agent(`Para el Lote ${lote} (${titulo}) lee el código existente en src/ y devuelve qué se reutiliza tal cual (StatusService, ContactPointService, CustomFieldService, IDataSource, PermissionCatalog, patrones de controlador/DTO) en el campo "reutilizar", más los endpoints y permisos ya existentes que el módulo debe respetar. Entidades y reglas pueden ir vacías.`, { agentType: 'spec-reader', label: 'leer:codigo', schema: SPEC }),
])).filter(Boolean)

const spec = {
  entidades: lecturas.flatMap(l => l.entidades || []),
  reglas: lecturas.flatMap(l => l.reglas || []),
  endpoints: lecturas.flatMap(l => l.endpoints || []),
  estatus: lecturas.flatMap(l => l.estatus || []),
  vacios: lecturas.flatMap(l => l.vacios || []),
  reutilizar: lecturas.flatMap(l => l.reutilizar || []),
}
const critica = await agent(`Esta es la especificación consolidada del Lote ${lote} (${titulo}):\n${JSON.stringify(spec, null, 1)}\n\nRevisa contra Diseño/logistica-funcionalidades-maestro.md (secciones ${secciones} y la bitácora) y el SQL: ¿qué regla, tabla, endpoint o decisión del dueño falta o está mal citada? Devuelve solo los faltantes en "vacios" y correcciones en "reglas".`, { agentType: 'spec-reader', label: 'critico:completitud', schema: SPEC })
if (critica) { spec.vacios.push(...(critica.vacios || [])); spec.reglas.push(...(critica.reglas || [])) }
log(`Especificación: ${spec.entidades.length} entidades, ${spec.reglas.length} reglas, ${spec.vacios.length} vacíos.`)

phase('Diseñar')
const enfoques = [
  'MÍNIMO VIABLE: lo estrictamente necesario para cumplir el documento con el menor número de piezas, reutilizando al máximo lo del Lote 1.',
  'RIESGO PRIMERO: diseña alrededor de lo que más puede fallar (integridad multi-tenant, transiciones de estatus, cálculos de dinero, concurrencia) y protege eso con guardas y pruebas.',
  'EXTENSIBILIDAD: diseña pensando en los lotes siguientes que dependen de este (Órdenes, Facturación, Portal) para que no haya que rehacer nada, sin sobre-diseñar.',
]
const disenos = (await parallel(enfoques.map((e, i) => () =>
  agent(`Diseña el plan de implementación del Lote ${lote} (${titulo}) con este enfoque: ${e}\n\nEspecificación:\n${JSON.stringify(spec, null, 1)}\n\nLas piezas deben ser implementables en paralelo por agentes distintos: archivos disjuntos por pieza; los archivos compartidos (TeikemDbContext, DependencyInjection, PermissionCatalog, scripts SQL de Diseño) van en UNA pieza marcada tocaCompartidos=true. Incluye pruebas unitarias y pasos del smoke test.`,
    { agentType: 'architect', label: `diseno:${i + 1}`, schema: PLAN })))).filter(Boolean)

const jueces = (await parallel([0, 1].map(j => () =>
  agent(`Eres el juez ${j + 1}. Puntúa (0-10) estos ${disenos.length} planes para el Lote ${lote} (${titulo}) según: cumplimiento del documento maestro, reutilización correcta del Lote 1, seguridad multi-tenant, implementabilidad en paralelo, cobertura de pruebas. Lista además las mejores ideas de los planes no ganadores que valdría la pena injertar.\n\n${disenos.map((d, i) => `PLAN ${i}:\n${JSON.stringify(d, null, 1)}`).join('\n\n')}`,
    { agentType: 'reviewer', label: `juez:${j + 1}`, schema: SCORE })))).filter(Boolean)

const totales = disenos.map((_, i) => jueces.reduce((s, j) => s + ((j.puntajes.find(p => p.indice === i) || {}).puntaje || 0), 0))
const ganador = totales.indexOf(Math.max(...totales))
log(`Puntajes: ${totales.join(' / ')} → gana el plan ${ganador + 1}.`)

const plan = await agent(`Sintetiza el plan final del Lote ${lote} (${titulo}) a partir del plan ganador (índice ${ganador}) injertando las mejores ideas que señalaron los jueces. Mantén piezas con archivos disjuntos y una sola pieza tocaCompartidos=true. Conserva todas las "decisiones" abiertas para que el dueño las revise.\n\nPLAN GANADOR:\n${JSON.stringify(disenos[ganador], null, 1)}\n\nIDEAS A INJERTAR:\n${JSON.stringify(jueces.flatMap(j => j.mejoresIdeas || []), null, 1)}\n\nVACÍOS DE LA ESPECIFICACIÓN (deben quedar como decisiones si no se resuelven):\n${JSON.stringify(spec.vacios, null, 1)}`,
  { agentType: 'architect', label: 'sintesis', schema: PLAN })

return { spec, plan, puntajes: totales }
