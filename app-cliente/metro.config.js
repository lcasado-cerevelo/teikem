// Metro del portal del cliente. Lote 218: el motor de ayuda y las reglas de Kemi se comparten con la web del personal desde ../shared (fuera de esta carpeta), así que Metro tiene que vigilar esa
// carpeta y buscar los paquetes (react, @babel/runtime…) en los node_modules de esta app y no junto a los archivos compartidos.
const path = require('path')
const { getDefaultConfig } = require('expo/metro-config')

const config = getDefaultConfig(__dirname)
const shared = path.resolve(__dirname, '..', 'shared')

config.watchFolders = [...(config.watchFolders ?? []), shared]
config.resolver.nodeModulesPaths = [path.resolve(__dirname, 'node_modules'), ...(config.resolver.nodeModulesPaths ?? [])]

module.exports = config
