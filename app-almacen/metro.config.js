// Metro con la configuración de Expo. Con VISUAL_WEB=1 (solo la verificación visual con Playwright, `npm run export:visual`) la web usa sql.js en lugar de expo-sqlite y una entrada que lo inicia antes
// de arrancar la app; en el aparato (y sin esa variable) no cambia nada.
const path = require('path')
const { getDefaultConfig } = require('expo/metro-config')

const config = getDefaultConfig(__dirname)

if (process.env.VISUAL_WEB === '1') {
  const original = config.resolver.resolveRequest
  config.resolver.resolveRequest = (context, moduleName, platform) => {
    if (platform === 'web' && moduleName === 'expo-sqlite') return { type: 'sourceFile', filePath: path.join(__dirname, 'scripts', 'visual', 'expo-sqlite-web.js') }
    if (platform === 'web' && moduleName === 'expo-secure-store') return { type: 'sourceFile', filePath: path.join(__dirname, 'scripts', 'visual', 'expo-secure-store-web.js') }
    if (platform === 'web' && moduleName === './node_modules/expo-router/entry') return { type: 'sourceFile', filePath: path.join(__dirname, 'scripts', 'visual', 'visual-entry.js') }
    return original ? original(context, moduleName, platform) : context.resolveRequest(context, moduleName, platform)
  }
}

module.exports = config
