package expo.modules.datawedge

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.os.Bundle
import expo.modules.kotlin.modules.Module
import expo.modules.kotlin.modules.ModuleDefinition

// Lote 8A-app — puente a DataWedge (Zebra MC3300), modo "intent" (docs/mobile/app-almacen-plan.md §1). Sin EMDK ni
// licencias: DataWedge ya viene instalado de fábrica en los Zebra y expone su API de configuración y sus lecturas por
// Intent (broadcast), documentada por Zebra (https://techdocs.zebra.com/datawedge/latest/guide/api/). Este módulo:
//  1. Al arrancar la app crea (o reemplaza) un perfil de DataWedge asociado a este paquete, con el escáner encendido y la
//     salida por Intent apuntando a nuestra propia acción (createProfile).
//  2. Escucha esa acción con un BroadcastReceiver dinámico y entrega cada lectura a JavaScript como el evento "onScan".
// En un aparato sin DataWedge (emulador, teléfono normal, o el mismo Zebra si alguien lo desinstaló) las llamadas no hacen
// nada dañino: sendBroadcast a una acción sin receptor del lado de DataWedge simplemente no produce respuesta, y la app
// sigue funcionando por teclado/cámara (kernel/scanner/ScanField.tsx decide la fuente y no depende de este módulo).
//
// IMPORTANTE (ver docs/lote8A-decisiones.md, decisión de la app): este código no se pudo compilar ni probar contra un
// Zebra real en esta sesión (sin SDK de Android ni aparato); el árbitro es el job `android` de CI (compila con Gradle) y,
// para el funcionamiento real del escáner, la prueba de Luis en el MC3300. Revisar con cuidado ante el primer reporte.
private const val DATAWEDGE_CONFIG_ACTION = "com.symbol.datawedge.api.ACTION"
private const val DATAWEDGE_SEND_RESULT = "com.symbol.datawedge.api.SEND_RESULT"
private const val EXTRA_SET_CONFIG = "com.symbol.datawedge.api.SET_CONFIG"
private const val SCAN_ACTION = "com.teikem.almacen.SCAN"
private const val PROFILE_NAME = "TeikemAlmacen"

class DatawedgeModule : Module() {
  private var receiver: BroadcastReceiver? = null

  override fun definition() = ModuleDefinition {
    Name("Datawedge")

    Events("onScan")

    // true si el paquete de DataWedge está instalado en el aparato (Zebra de fábrica; ausente en emulador/teléfono normal).
    Function("isAvailable") {
      isDataWedgeInstalled()
    }

    // Crea (o reemplaza) el perfil "TeikemAlmacen": escáner encendido, salida por Intent (broadcast) a SCAN_ACTION, sin
    // interceptar el teclado (así el mismo campo de captura sirve para escaneo por DataWedge, teclado físico o cámara).
    Function("createProfile") {
      if (isDataWedgeInstalled()) sendCreateProfileBroadcast()
    }

    OnCreate {
      val filter = IntentFilter(SCAN_ACTION)
      val onReceive: (Context, Intent) -> Unit = { _, intent ->
        val data = intent.getStringExtra("com.symbol.datawedge.data_string")
        val symbology = intent.getStringExtra("com.symbol.datawedge.label_type")
        if (!data.isNullOrEmpty()) {
          sendEvent("onScan", mapOf("data" to data, "symbology" to (symbology ?: "")))
        }
      }
      val r = object : BroadcastReceiver() {
        override fun onReceive(context: Context, intent: Intent) = onReceive(context, intent)
      }
      receiver = r
      val ctx = appContext.reactContext ?: return@OnCreate
      if (android.os.Build.VERSION.SDK_INT >= android.os.Build.VERSION_CODES.TIRAMISU) {
        ctx.registerReceiver(r, filter, Context.RECEIVER_NOT_EXPORTED)
      } else {
        @Suppress("UnspecifiedRegisterReceiverFlag")
        ctx.registerReceiver(r, filter)
      }
    }

    OnDestroy {
      receiver?.let { r ->
        try {
          appContext.reactContext?.unregisterReceiver(r)
        } catch (_: IllegalArgumentException) {
          // ya estaba fuera de registro; nada que hacer.
        }
      }
      receiver = null
    }
  }

  private fun isDataWedgeInstalled(): Boolean {
    val ctx = appContext.reactContext ?: return false
    return try {
      ctx.packageManager.getPackageInfo("com.symbol.datawedge", 0)
      true
    } catch (_: Exception) {
      false
    }
  }

  private fun sendCreateProfileBroadcast() {
    val ctx = appContext.reactContext ?: return
    val packageName = ctx.packageName

    val profileConfig = Bundle().apply {
      putString("PROFILE_NAME", PROFILE_NAME)
      putString("PROFILE_ENABLED", "true")
      putString("CONFIG_MODE", "CREATE_IF_NOT_EXIST")
    }

    val appConfig = Bundle().apply {
      putString("PACKAGE_NAME", packageName)
      putStringArray("ACTIVITY_LIST", arrayOf("*"))
    }
    profileConfig.putParcelableArray("APP_LIST", arrayOf(appConfig))

    val barcodeConfig = Bundle().apply {
      putString("PLUGIN_NAME", "BARCODE")
      putString("RESET_CONFIG", "true")
      putBundle("PARAM_LIST", Bundle().apply { putString("scanner_selection", "auto") })
    }

    val intentConfig = Bundle().apply {
      putString("PLUGIN_NAME", "INTENT")
      putString("RESET_CONFIG", "true")
      putBundle(
        "PARAM_LIST",
        Bundle().apply {
          putString("intent_output_enabled", "true")
          putString("intent_action", SCAN_ACTION)
          putString("intent_delivery", "2") // 2 = broadcast intent
        },
      )
    }

    profileConfig.putParcelableArray("PLUGIN_CONFIG", arrayOf(barcodeConfig, intentConfig))

    val bIntent = Intent(DATAWEDGE_CONFIG_ACTION).apply {
      putExtra(EXTRA_SET_CONFIG, profileConfig)
      putExtra("SEND_RESULT", "false")
    }
    ctx.sendBroadcast(bIntent)
  }
}
