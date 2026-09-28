import { AndroidConfig, type ConfigPlugin, withAndroidManifest } from 'expo/config-plugins'

/**
 * Lote 8A-app: el módulo nativo `modules/datawedge` se enlaza solo (autolinking local de Expo Modules); este plugin solo
 * agrega el permiso VIBRATE, usado por kernel/ui/feedback.ts para las señales de éxito/error del escaneo (docs/mobile/
 * app-almacen-plan.md §2, "sonido y vibración distintos para ok y error").
 */
export const withDataWedge: ConfigPlugin = (config) =>
  withAndroidManifest(config, (mod) => {
    AndroidConfig.Permissions.addPermission(mod.modResults, 'android.permission.VIBRATE')
    return mod
  })

export default withDataWedge
