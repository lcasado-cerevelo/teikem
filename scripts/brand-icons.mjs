#!/usr/bin/env node
// Lote F8a, pieza P8 — genera los iconos, el splash y el lockup de la app móvil (app-almacen/assets) a partir del
// paquete de marca de Logos/. Fuente única del símbolo: Logos/teikem-symbol.svg (hexágono + T, mismas rutas que
// teikemLogo() de la maqueta). Los lockups salen de los SVG -inv del paquete (versión para fondo oscuro), sin su
// rectángulo de fondo para que se asienten sobre el fondo de la app.
//
// Uso (desde cualquier carpeta; no depende de rutas de la máquina):
//   cd app-almacen && npm run brand:icons        (o bien: node scripts/brand-icons.mjs desde la raíz)
// `sharp` es devDependency de app-almacen; se resuelve desde ahí aunque el script viva en scripts/.
//
// Nota: el texto del lockup ("TEIKEM" + lema) usa 'Segoe UI' con respaldo system-ui/sans-serif; en Linux sin Segoe UI
// el render cae a la fuente sans del sistema. Los PNG versionados se generaron en Windows.

import { createRequire } from 'node:module'
import { mkdir, readFile } from 'node:fs/promises'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const require = createRequire(path.join(root, 'app-almacen', 'package.json'))
const sharp = require('sharp')

const BRAND_BLUE = '#0B2C66'
const logos = path.join(root, 'Logos')
const out = path.join(root, 'app-almacen', 'assets')

// Caja del símbolo dentro del viewBox 0 0 124 124 de teikem-symbol.svg: el hexágono con su trazo ocupa x 18..106 y
// 8..110 (centro 62,59). Un cuadrado de 102 centrado ahí deja el símbolo ajustado y centrado de verdad.
const SYMBOL_BOX = '11 8 102 102'

/** El SVG del símbolo con el viewBox ajustado y el tamaño en píxeles pedido. */
function sizedSymbol(svg, px) {
  return svg
    .replace(/viewBox="[^"]*"/, `viewBox="${SYMBOL_BOX}"`)
    .replace(/ width="\d+"/, ` width="${px}"`)
    .replace(/ height="\d+"/, ` height="${px}"`)
}

/**
 * Versión monocroma (blanco puro sobre transparente, para el icono temático de Android 13+): los azules desaparecen
 * y quedan el contorno del hexágono, la T y el banderín. El banderín lleva un filo transparente alrededor para que no
 * se funda con el brazo de la T. Se construye como máscara de luminancia sobre un rectángulo blanco.
 */
function monochromeSymbol(svg, px) {
  const inner = sizedSymbol(svg, px)
    .replace(/<\/?svg[^>]*>/g, '')
    .replace(/<title>[\s\S]*?<\/title>|<desc>[\s\S]*?<\/desc>/g, '')
    .replace(/#0B2C66|#2E7CF6|#123A85/gi, '#000000')
    .replace(/fill="#FF6A1A"/i, 'fill="#FFFFFF" stroke="#000000" stroke-width="4" stroke-linejoin="round" paint-order="stroke"')
  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="${SYMBOL_BOX}" width="${px}" height="${px}">
<defs><mask id="mono" maskUnits="userSpaceOnUse" x="0" y="0" width="124" height="124">${inner}</mask></defs>
<rect x="0" y="0" width="124" height="124" fill="#FFFFFF" mask="url(#mono)"/></svg>`
}

/** Lienzo cuadrado `size` (transparente o de color) con `symbolSvg` centrado ocupando `ratio` del lado. */
async function composeSquare(file, size, symbolSvg, ratio, background) {
  const inner = Math.round(size * ratio)
  const symbol = await sharp(Buffer.from(symbolSvg(inner))).png().toBuffer()
  const bg = background ?? { r: 0, g: 0, b: 0, alpha: 0 }
  await sharp({ create: { width: size, height: size, channels: 4, background: bg } })
    .composite([{ input: symbol, gravity: 'center' }])
    .png({ compressionLevel: 9 })
    .toFile(path.join(out, file))
  console.log(`  ${file} (${size}x${size})`)
}

/** Lockup (símbolo + TEIKEM + lema) -inv sin su fondo, recortado al contenido, a 1x/2x/3x con ancho base `baseWidth`. */
async function lockup(sourceFile, lang, baseWidth) {
  const svg = (await readFile(path.join(logos, sourceFile), 'utf8'))
    // El rectángulo de fondo de todo el lienzo (480x150); el resto del dibujo no se toca.
    .replace(/<rect x="0" y="0" width="480" height="150"[^>]*\/>/, '')
  // Se rasteriza grande (densidad alta) y se recorta el margen transparente antes de escalar a cada densidad.
  const big = await sharp(Buffer.from(svg), { density: 72 * 6 }).png().trim({ threshold: 1 }).toBuffer()
  const meta = await sharp(big).metadata()
  // Alto base redondeado una sola vez: @2x y @3x son múltiplos exactos del 1x (Metro espera esa proporción).
  const baseHeight = Math.round((baseWidth * meta.height) / meta.width)
  for (const scale of [1, 2, 3]) {
    const suffix = scale === 1 ? '' : `@${scale}x`
    const file = `brand-lockup-${lang}${suffix}.png`
    const info = await sharp(big)
      .resize({ width: baseWidth * scale, height: baseHeight * scale, fit: 'fill' })
      .png({ compressionLevel: 9 })
      .toFile(path.join(out, file))
    console.log(`  ${file} (${info.width}x${info.height})`)
  }
}

async function main() {
  await mkdir(out, { recursive: true })
  const symbol = await readFile(path.join(logos, 'teikem-symbol.svg'), 'utf8')
  const color = (px) => sizedSymbol(symbol, px)
  const white = (px) => monochromeSymbol(symbol, px)
  const blue = BRAND_BLUE

  console.log(`Generando la marca de app-almacen en ${path.relative(root, out)}:`)
  // Icono general (iOS y Android anterior a 8): símbolo sobre el azul de marca.
  await composeSquare('icon.png', 1024, color, 0.72, blue)
  // Icono adaptativo de Android: 20 % de margen de seguridad por lado (el símbolo cabe en el círculo seguro de 66/108).
  await composeSquare('android-icon-foreground.png', 1024, color, 0.6)
  await composeSquare('android-icon-monochrome.png', 1024, white, 0.6)
  await sharp({ create: { width: 1024, height: 1024, channels: 4, background: blue } })
    .png({ compressionLevel: 9 })
    .toFile(path.join(out, 'android-icon-background.png'))
  console.log('  android-icon-background.png (1024x1024)')
  // Splash: solo el símbolo; el fondo #0B2C66 lo pone app.config.ts (expo-splash-screen).
  await composeSquare('splash-icon.png', 1024, color, 0.96)
  // Favicon (build web de Expo).
  await composeSquare('favicon.png', 48, color, 1)
  // Lockups para las pantallas Registrar y Entrar (tema oscuro → variante -inv), por idioma.
  await lockup('teikem-1b-horizontal-tagline-es-inv.svg', 'es', 240)
  await lockup('teikem-1a-horizontal-tagline-en-inv.svg', 'en', 240)
}

main().catch((err) => {
  console.error(err)
  process.exit(1)
})
