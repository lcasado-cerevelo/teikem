// Genera la hoja de códigos de barras de la demostración a Dani (docs/demo-dani.md): por escenario, los productos y las posiciones que se escanean.
// Usa el codificador zxing-wasm que ya trae app-almacen (sin descargar nada). Código 128 para todo: lo lee el lector del Zebra y el campo de la app
// acepta «código de barras o SKU» (si el producto no tiene código de barras, se imprime su SKU).
// Uso:  node scripts/demo/generar-codigos-demo.mjs        → escribe docs/demo/codigos-demo-dani.html (y el PDF si encuentra Chrome o Edge)
import { execFileSync } from 'node:child_process'
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'

const aqui = dirname(fileURLToPath(import.meta.url))
const repo = resolve(aqui, '..', '..')
const zx = join(repo, 'app-almacen', 'node_modules', 'zxing-wasm')
const { prepareZXingModule, writeBarcode } = await import(pathToFileURL(join(zx, 'dist', 'es', 'writer', 'index.js')).href)
await prepareZXingModule({ overrides: { wasmBinary: readFileSync(join(zx, 'dist', 'writer', 'zxing_writer.wasm')) }, fireImmediately: true })

// Productos (código = su código de barras si lo tiene; si no, su SKU) y posiciones, por escenario. Datos de la base local (2026-10-07).
const P = {
  guante: { tipo: 'Producto', nombre: '171-AC-201-M · GLOVE NITRILE PF N/ST MEDIUM', codigo: '3022123181425', nota: 'Depot · sin existencia: se recibe 100 con recibo ciego' },
  tourniquet: { tipo: 'Producto', nombre: '171-AC-100-A · TOURNIQUET', codigo: '3726918104001', nota: 'Depot · ya repartido en 7 posiciones (60, 60 y 5 de 20): sirve para despachar de muchas posiciones' },
  underpad: { tipo: 'Producto', nombre: '171-DU-1724 · UNDERPAD 17X24 3PK/100EA', codigo: '1201804326993', nota: 'Depot · 60 en 13-C-20 y 60 en 14-C-20' },
  colchon: { tipo: 'Producto', nombre: '56-CM-100F-42 · COMFORD ZONE FOAM MATRESS 6X42', codigo: '+B676CM100F420+', nota: 'Depot · 8 en cada una de 6 posiciones' },
  alta: { tipo: 'Producto', nombre: '53350 · PRODIGY CONTROL SOLUTION HIGH 4ML', codigo: '53350', nota: 'Solutions · sin código de barras: se imprime el SKU · 20 en GENERAL' },
  baja: { tipo: 'Producto', nombre: '53310 · PRODIGY CONTROL SOLUTION LOW 4ML', codigo: '53310', nota: 'Solutions · sin código de barras: se imprime el SKU · 19 en GENERAL' },
  hisopos: { tipo: 'Producto', nombre: '00050-7 · GLOBAL COTTON SWABS 300CT.', codigo: '00050-7', nota: 'Solutions · sin código de barras: se imprime el SKU · 50 en GENERAL' },
}
const pos = (codigo, nota = '') => ({ tipo: 'Posición', nombre: codigo, codigo, nota })

const escenarios = [
  {
    id: 'A',
    titulo: 'Depot: recibo ciego con acomodo y reparto por posición',
    texto: 'Recibir → Recibo ciego (sin orden de compra), modo «Con acomodo»: se reciben 100 y en Acomodar se escanean las posiciones; en cada una se dejan 20.',
    items: [
      P.guante,
      pos('01-E-03', 'cupo 40 · vacía'),
      pos('01-E-04', 'cupo 40 · vacía'),
      pos('01-E-05', 'cupo 40 · vacía'),
      pos('01-E-06', 'cupo 40 · vacía'),
      pos('01-E-09', 'cupo 40 · vacía'),
      pos('09-A-08', 'cupo 10 · vacía · para mostrar el aviso de cupo'),
    ],
  },
  {
    id: 'B',
    titulo: 'Depot: despacho que sale de varias posiciones',
    texto: 'Se escanea el producto, se piden 100 y salen 60 de una posición y 40 de la otra. Con el TOURNIQUET (7 posiciones) se puede pedir 150 y salen de cuatro.',
    items: [P.underpad, pos('13-C-20', '60 disponibles'), pos('14-C-20', '60 disponibles'), P.tourniquet],
    aviso: 'TOURNIQUET: 09-C-15 y 10-C-15 con 60; 09-A-01, 09-A-03, 09-A-07, 09-A-09 y 09-A-15 con 20 (todas se muestran en «Posiciones con existencia»).',
  },
  {
    id: 'C',
    titulo: 'Solutions: contar mal, segunda oportunidad y coincide (Por posición, como Contador)',
    texto: 'Se escanea la posición GENERAL y el producto; se cuenta 15 (no coincide, vuelve a contar) y luego 20 (coincide).',
    items: [pos('GENERAL', 'posición de Solutions'), P.alta],
  },
  {
    id: 'D',
    titulo: 'Solutions: dos intentos mal y se acabó (Por posición, como Contador)',
    texto: 'Se escanea GENERAL y el producto; se cuenta 10 y luego 12: la línea se cierra y queda para revisión.',
    items: [pos('GENERAL', 'posición de Solutions'), P.baja],
  },
  {
    id: 'E',
    titulo: 'Solutions: conteo por producto con el producto en varias posiciones',
    texto: 'Primero se recibe 20 de este producto directo a A-01 (Recibir → Directo a posición → Recibo ciego). Luego, Conteo → Por producto: la app pide elegir la posición; contar 50 en GENERAL y 20 en A-01.',
    items: [P.hisopos, pos('GENERAL', '50 existentes'), pos('A-01', 'posición nueva · recibir 20 aquí (directo)')],
  },
  {
    id: 'F',
    titulo: 'Depot: conteo por producto con el producto en varias posiciones',
    texto: 'Se escanea el producto; la app lista las seis posiciones y se cuenta una línea por posición (07-D-14 con 6 para dejar una diferencia).',
    items: [
      P.colchon,
      pos('07-B-14', '8 · contar 8'),
      pos('07-C-14', '8 · contar 8'),
      pos('07-D-14', '8 · contar 6 (diferencia −2)'),
      pos('08-B-14', '8'),
      pos('08-C-14', '8'),
      pos('08-D-14', '8'),
    ],
  },
]

const esc = (s) => String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')

const lector = await import(pathToFileURL(join(zx, 'dist', 'es', 'reader', 'index.js')).href)
await lector.prepareZXingModule({ overrides: { wasmBinary: readFileSync(join(zx, 'dist', 'reader', 'zxing_reader.wasm')) }, fireImmediately: true })

/** Dibuja las barras del SVG en una imagen en blanco y negro y la lee con el decodificador: lo impreso tiene que leerse igual que el código. */
async function verificar(codigo, svg, ancho, alto) {
  const datos = new Uint8ClampedArray(ancho * alto * 4).fill(255)
  for (const m of svg.matchAll(/M(\d+) 0h(\d+)v(\d+)h-\d+Z/g)) {
    const x0 = Number(m[1]), w = Number(m[2]), h = Number(m[3])
    for (let y = 0; y < Math.min(h, alto); y++) for (let x = x0; x < x0 + w && x < ancho; x++) datos.fill(0, (y * ancho + x) * 4, (y * ancho + x) * 4 + 3)
  }
  const leidos = await lector.readBarcodes({ data: datos, width: ancho, height: alto, colorSpace: 'srgb' }, { formats: ['Code128'], tryHarder: true })
  if (leidos.length !== 1 || leidos[0].text !== codigo) throw new Error(`El código de barras de «${codigo}» no se lee igual: ${JSON.stringify(leidos.map((l) => l.text))}`)
}

async function barras(codigo) {
  const r = await writeBarcode(codigo, { format: 'Code128', withQuietZones: true, withHRT: false, scale: 3 })
  if (r.error) throw new Error(`No se pudo codificar «${codigo}»: ${r.error}`)
  const ancho = Number(/<svg[^>]* width="(\d+)"/.exec(r.svg)[1])
  const alto = Number(/<svg[^>]* height="(\d+)"/.exec(r.svg)[1])
  await verificar(codigo, r.svg, ancho, alto)
  // el SVG viene sin viewBox: se le pone para que se estire a un tamaño fijo (mismo alto en toda la hoja) sin recortarse
  const cabecera = `<svg viewBox="0 0 ${ancho} ${alto}" preserveAspectRatio="none" width="78mm" height="15mm" version="1.1" xmlns="http://www.w3.org/2000/svg">`
  return r.svg.replace(/<svg[^>]*>/, cabecera)
}

let cuerpo = ''
for (const e of escenarios) {
  cuerpo += `<section><h2><span class="id">Escenario ${e.id}</span> ${esc(e.titulo)}</h2><p class="texto">${esc(e.texto)}</p><table>`
  for (const it of e.items) {
    cuerpo += `<tr><td class="tipo ${it.tipo === 'Producto' ? 'prod' : 'pos'}">${it.tipo}</td><td class="dato"><div class="nombre">${esc(it.nombre)}</div><div class="cod">${esc(it.codigo)}</div><div class="nota">${esc(it.nota)}</div></td><td class="barras">${await barras(it.codigo)}</td></tr>`
  }
  cuerpo += `</table>${e.aviso ? `<p class="aviso">${esc(e.aviso)}</p>` : ''}</section>`
}

const html = `<!doctype html><html lang="es"><head><meta charset="utf-8"><title>Códigos de barras de la demostración</title><style>
@page { size: A4; margin: 12mm }
* { box-sizing: border-box }
body { font-family: Segoe UI, Arial, sans-serif; color: #111; margin: 0; font-size: 11pt }
h1 { font-size: 18pt; margin: 0 0 2mm }
.sub { color: #555; margin: 0 0 6mm; font-size: 10pt }
section { break-inside: avoid; margin: 0 0 7mm }
h2 { font-size: 13pt; margin: 0 0 1mm; border-bottom: 2px solid #111; padding-bottom: 1mm }
.id { background: #111; color: #fff; padding: 0 2.5mm; border-radius: 2mm; margin-right: 2mm }
.texto { margin: 1mm 0 2mm; color: #333; font-size: 10pt }
table { width: 100%; border-collapse: collapse }
tr { break-inside: avoid; border-bottom: 1px solid #ccc }
td { vertical-align: middle; padding: 1.5mm 1mm }
.tipo { width: 20mm; font-weight: 700; font-size: 9pt; text-transform: uppercase }
.prod { color: #0a5 }
.pos { color: #05a }
.nombre { font-weight: 700 }
.cod { font-family: Consolas, monospace; font-size: 14pt; letter-spacing: .5px }
.nota { color: #666; font-size: 9pt }
.barras { width: 82mm; text-align: right; padding-right: 3mm }
.barras svg { display: block; margin-left: auto }
.aviso { color: #555; font-size: 9pt; margin: 1.5mm 0 0; font-style: italic }
</style></head><body>
<h1>Códigos de barras de la demostración</h1>
<p class="sub">Todos en Código 128. Un producto sin código de barras en el sistema lleva su SKU. Datos de la base local, 2026-10-07; ver docs/demo-dani.md.</p>
${cuerpo}
</body></html>`

const salida = join(repo, 'docs', 'demo')
mkdirSync(salida, { recursive: true })
const archivoHtml = join(salida, 'codigos-demo-dani.html')
writeFileSync(archivoHtml, html, 'utf8')
console.log(`HTML: ${archivoHtml}`)

const navegadores = ['C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe', 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe']
const navegador = navegadores.find((n) => existsSync(n))
if (navegador) {
  const pdf = join(salida, 'codigos-demo-dani.pdf')
  execFileSync(navegador, ['--headless=new', '--disable-gpu', '--no-pdf-header-footer', `--print-to-pdf=${pdf}`, pathToFileURL(archivoHtml).href], { stdio: 'ignore' })
  console.log(`PDF:  ${pdf}`)
} else console.log('No encontré Chrome ni Edge: abra el HTML y use Imprimir → Guardar como PDF.')
