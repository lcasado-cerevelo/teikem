#!/usr/bin/env node
// Lote F8a, pieza P8 — genera los iconos, el splash y el lockup de la app móvil (app-almacen/assets) a partir del
// paquete de marca de Logos/. Fuente única del símbolo: Logos/teikem-symbol.svg (hexágono + T, mismas rutas que
// teikemLogo() de la maqueta). Los lockups salen de los SVG -inv del paquete (versión para fondo oscuro), sin su
// rectángulo de fondo para que se asienten sobre el fondo de la app.
//
// Uso (desde cualquier carpeta; no depende de rutas de la máquina):
//   cd app-almacen && npm run brand:icons        (o bien: node scripts/brand-icons.mjs desde la raíz)
//   node scripts/brand-icons.mjs staging [almacen|cliente]   iconos de la variante staging (fondo morado, cinta STAGING)
//   node scripts/brand-icons.mjs test [almacen|cliente]      iconos de la variante test (fondo verde, cinta TEST) — 2026-10-19
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
// Staging (2026-10-12): fondo morado y cinta roja «STAGING», para no confundir el APK de pruebas con el de producción.
// Test (2026-10-19): fondo verde y la misma cinta roja con «TEST» (test.advancelogisticspr.com).
const STAGING_PURPLE = '#6D28D9'
const TEST_GREEN = '#047857'
const STAGING_RED = '#DC2626'
const VARIANTS = { staging: { label: 'STAGING', background: STAGING_PURPLE }, test: { label: 'TEST', background: TEST_GREEN } }
const logos = path.join(root, 'Logos')
// Carpeta de salida: app-almacen/assets por omisión; `cliente` como segundo argumento → app-cliente/assets
const appFolder = process.argv[3] === 'cliente' ? 'app-cliente' : 'app-almacen'
const out = path.join(root, appFolder, 'assets')

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

/** Lockup (símbolo + TEIKEM + lema) sin su fondo, recortado al contenido, a 1x/2x/3x con ancho base `baseWidth`. `variant` '-light' = el de letras oscuras para fondo claro (archivo brand-lockup-<lang>-light). */
async function lockup(sourceFile, lang, baseWidth, variant = '') {
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
    const file = `brand-lockup-${lang}${variant}${suffix}.png`
    const info = await sharp(big)
      .resize({ width: baseWidth * scale, height: baseHeight * scale, fit: 'fill' })
      .png({ compressionLevel: 9 })
      .toFile(path.join(out, file))
    console.log(`  ${file} (${info.width}x${info.height})`)
  }
}

/** Cinta con la etiqueta del ambiente («STAGING» o «TEST»: píldora roja con texto blanco) de w×h px centrada en x, con su borde superior en y, dentro de un lienzo `size`. */
function stagingBand(size, w, h, y, knockout, label = 'STAGING') {
  const x = Math.round((size - w) / 2)
  const font = Math.round(h * 0.56)
  const text = `<text x="${size / 2}" y="${y + h / 2}" text-anchor="middle" dominant-baseline="central" font-family="Segoe UI, Arial, Helvetica, sans-serif" font-weight="800" font-size="${font}" letter-spacing="${Math.round(font * 0.06)}">${label}</text>`
  if (knockout) {
    // monocroma: píldora blanca con el texto recortado (transparente), porque ese icono solo usa el canal alfa
    return `<svg xmlns="http://www.w3.org/2000/svg" width="${size}" height="${size}" viewBox="0 0 ${size} ${size}"><defs><mask id="m" maskUnits="userSpaceOnUse" x="0" y="0" width="${size}" height="${size}"><rect width="${size}" height="${size}" fill="#fff"/><g fill="#000">${text}</g></mask></defs><rect x="${x}" y="${y}" width="${w}" height="${h}" rx="${h / 2}" fill="#fff" mask="url(#m)"/></svg>`
  }
  return `<svg xmlns="http://www.w3.org/2000/svg" width="${size}" height="${size}" viewBox="0 0 ${size} ${size}"><rect x="${x}" y="${y}" width="${w}" height="${h}" rx="${h / 2}" fill="${STAGING_RED}" stroke="#fff" stroke-width="${Math.round(h * 0.06)}"/><g fill="#fff">${text}</g></svg>`
}

/** Iconos de una variante de pruebas (<app>/assets/staging o /test): los mismos de producción con su fondo de color y la cinta con la etiqueta. */
async function stagingIcons(variant) {
  const { label, background } = VARIANTS[variant]
  const dir = path.join(out, variant)
  await mkdir(dir, { recursive: true })
  const symbol = await readFile(path.join(logos, 'teikem-symbol.svg'), 'utf8')
  const size = 1024
  const compose = async (file, symbolSvg, ratio, background, band) => {
    const inner = Math.round(size * ratio)
    const sym = await sharp(Buffer.from(symbolSvg(inner))).png().toBuffer()
    const bg = background ?? { r: 0, g: 0, b: 0, alpha: 0 }
    await sharp({ create: { width: size, height: size, channels: 4, background: bg } })
      .composite([{ input: sym, gravity: 'center' }, { input: Buffer.from(band) }])
      .png({ compressionLevel: 9 })
      .toFile(path.join(dir, file))
    console.log(`  ${variant}/${file} (${size}x${size})`)
  }
  // Icono general (a sangre): cinta más ancha. Adaptativo: la cinta cabe en el círculo seguro de 66/108.
  await compose('icon.png', (px) => sizedSymbol(symbol, px), 0.72, background, stagingBand(size, 640, 150, 760, false, label))
  await compose('android-icon-foreground.png', (px) => sizedSymbol(symbol, px), 0.6, null, stagingBand(size, 400, 100, 690, false, label))
  await compose('android-icon-monochrome.png', (px) => monochromeSymbol(symbol, px), 0.6, null, stagingBand(size, 400, 100, 690, true, label))
  await sharp({ create: { width: size, height: size, channels: 4, background } }).png({ compressionLevel: 9 }).toFile(path.join(dir, 'android-icon-background.png'))
  console.log(`  ${variant}/android-icon-background.png (1024x1024)`)
}

async function main() {
  const variant = process.argv[2]
  if (variant === 'lockups-light') {
    // Solo los lockups para fondo claro (letras oscuras), sin tocar los iconos: node scripts/brand-icons.mjs lockups-light cliente
    console.log(`Generando los lockups para tema claro en ${appFolder}/assets:`)
    await lockup('teikem-1b-horizontal-tagline-es.svg', 'es', 240, '-light')
    await lockup('teikem-1a-horizontal-tagline-en.svg', 'en', 240, '-light')
    return
  }
  if (variant in VARIANTS) {
    console.log(`Generando los iconos de ${variant} en ${appFolder}/assets/${variant}:`)
    return stagingIcons(variant)
  }
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
