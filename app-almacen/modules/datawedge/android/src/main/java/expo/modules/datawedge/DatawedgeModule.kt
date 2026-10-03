package expo.modules.datawedge

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.os.Bundle
import android.util.Log
import expo.modules.kotlin.modules.Module
import expo.modules.kotlin.modules.ModuleDefinition

// Lote 8A-app — puente a DataWedge (Zebra MC3300), modo "intent" (docs/mobile/app-almacen-plan.md §1). Sin EMDK ni
// licencias: DataWedge ya viene instalado de fábrica en los Zebra y expone su API de configuración y sus lecturas por
// Intent (broadcast), documentada por Zebra (https://techdocs.zebra.com/datawedge/latest/guide/api/). Este módulo:
//  1. Al arrancar la app crea (o actualiza) un perfil de DataWedge asociado a este paquete, con el escáner encendido, la
//     salida por Intent apuntando a nuestra propia acción y la salida por TECLAS APAGADA (createProfile).
//  2. Escucha esa acción con un BroadcastReceiver dinámico y entrega cada lectura a JavaScript como el evento "onScan".
//  3. (Mejoras de uso del Zebra, docs/mobile/mejoras-ux-zebra.md §2) Pide a DataWedge el resultado de crear el perfil
//     (SEND_RESULT) y cuál es el perfil activo (GET_ACTIVE_PROFILE); lo registra en logcat (etiqueta "TeikemDataWedge") y
//     lo expone a JavaScript (getProfileStatus + evento "onProfileStatus") para el indicador "Lector" de Sincronización.
// En un aparato sin DataWedge (emulador, teléfono normal, o el mismo Zebra si alguien lo desinstaló) las llamadas no hacen
// nada dañino: sendBroadcast a una acción sin receptor del lado de DataWedge simplemente no produce respuesta, y la app
// sigue funcionando por teclado (kernel/ui/ScanField.tsx no depende de este módulo).
//
// Hallazgos que corrige esta versión (2026-10-03, el lector escribía como teclas y no avanzaba solo):
//  - Un perfil nuevo de DataWedge trae el plugin KEYSTROKE ENCENDIDO por defecto; antes no se configuraba. Ahora se apaga.
//  - Los receptores se registraban con RECEIVER_NOT_EXPORTED: en Android 13+ eso solo deja pasar broadcasts de la propia
//    app o del sistema, y DataWedge es otra app; ahora se registran EXPORTED (ver decisión en docs/mobile/loteA3-decisiones.md).
//  - La creación del perfil se mandaba con SEND_RESULT=false (nadie se enteraba si DataWedge la rechazaba).
//
// IMPORTANTE: este código no se pudo compilar ni probar contra un Zebra real en esta sesión (sin SDK de Android ni
// aparato); el árbitro es el job `android` de CI (compila con Gradle) y, para el funcionamiento real del escáner, la
// lista de comprobación de Luis en el MC3300 (docs/mobile/loteA3-decisiones.md).
private const val TAG = "TeikemDataWedge"
private const val DATAWEDGE_PACKAGE = "com.symbol.datawedge"
private const val DATAWEDGE_CONFIG_ACTION = "com.symbol.datawedge.api.ACTION"
private const val DATAWEDGE_RESULT_ACTION = "com.symbol.datawedge.api.RESULT_ACTION"
private const val EXTRA_SET_CONFIG = "com.symbol.datawedge.api.SET_CONFIG"
private const val EXTRA_GET_ACTIVE_PROFILE = "com.symbol.datawedge.api.GET_ACTIVE_PROFILE"
private const val EXTRA_RESULT_GET_ACTIVE_PROFILE = "com.symbol.datawedge.api.RESULT_GET_ACTIVE_PROFILE"
private const val CMD_ID_CREATE = "teikem-create-profile"
private const val CMD_ID_ACTIVE = "teikem-active-profile"
private const val SCAN_ACTION = "com.teikem.almacen.SCAN"
private const val PROFILE_NAME = "TeikemAlmacen"

class DatawedgeModule : Module() {
  private var scanReceiver: BroadcastReceiver? = null
  private var resultReceiver: BroadcastReceiver? = null

  // Último estado conocido del perfil: unconfirmed hasta que DataWedge conteste.
  @Volatile private var profileState: String = "unconfirmed"
  @Volatile private var profileDetail: String = ""

  override fun definition() = ModuleDefinition {
    Name("Datawedge")

    Events("onScan", "onProfileStatus")

    // true si el paquete de DataWedge está instalado en el aparato (Zebra de fábrica; ausente en emulador/teléfono normal).
    Function("isAvailable") {
      isDataWedgeInstalled()
    }

    // Crea (o actualiza) el perfil "TeikemAlmacen": escáner encendido, salida por Intent (broadcast) a SCAN_ACTION y salida
    // por teclas apagada. Pide el resultado (llega a resultReceiver).
    Function("createProfile") {
      if (isDataWedgeInstalled()) sendCreateProfileBroadcast()
    }

    Function("getProfileStatus") {
      if (!isDataWedgeInstalled()) {
        mapOf("state" to "unavailable", "detail" to "")
      } else {
        mapOf("state" to profileState, "detail" to profileDetail)
      }
    }

    // Pregunta a DataWedge el perfil activo de la app en primer plano (la respuesta llega a resultReceiver).
    Function("refreshProfileStatus") {
      if (isDataWedgeInstalled()) sendGetActiveProfile()
    }

    OnCreate {
      val ctx = appContext.reactContext ?: return@OnCreate

      val scan = object : BroadcastReceiver() {
        override fun onReceive(context: Context, intent: Intent) {
          val data = intent.getStringExtra("com.symbol.datawedge.data_string")
          val symbology = intent.getStringExtra("com.symbol.datawedge.label_type")
          if (!data.isNullOrEmpty()) {
            sendEvent("onScan", mapOf("data" to data, "symbology" to (symbology ?: "")))
          }
        }
      }
      scanReceiver = scan
      register(ctx, scan, IntentFilter(SCAN_ACTION).apply { addCategory(Intent.CATEGORY_DEFAULT) })

      val result = object : BroadcastReceiver() {
        override fun onReceive(context: Context, intent: Intent) = onDataWedgeResult(intent)
      }
      resultReceiver = result
      register(ctx, result, IntentFilter(DATAWEDGE_RESULT_ACTION).apply { addCategory(Intent.CATEGORY_DEFAULT) })
    }

    OnDestroy {
      val ctx = appContext.reactContext
      listOfNotNull(scanReceiver, resultReceiver).forEach { r ->
        try {
          ctx?.unregisterReceiver(r)
        } catch (_: IllegalArgumentException) {
          // ya estaba fuera de registro; nada que hacer.
        }
      }
      scanReceiver = null
      resultReceiver = null
    }
  }

  // DataWedge es OTRA app: en Android 13+ el receptor tiene que ser EXPORTED para recibir sus broadcasts.
  private fun register(ctx: Context, receiver: BroadcastReceiver, filter: IntentFilter) {
    if (android.os.Build.VERSION.SDK_INT >= android.os.Build.VERSION_CODES.TIRAMISU) {
      ctx.registerReceiver(receiver, filter, Context.RECEIVER_EXPORTED)
    } else {
      @Suppress("UnspecifiedRegisterReceiverFlag")
      ctx.registerReceiver(receiver, filter)
    }
  }

  private fun isDataWedgeInstalled(): Boolean {
    val ctx = appContext.reactContext ?: return false
    return try {
      ctx.packageManager.getPackageInfo(DATAWEDGE_PACKAGE, 0)
      true
    } catch (_: Exception) {
      false
    }
  }

  private fun setStatus(state: String, detail: String) {
    profileState = state
    profileDetail = detail
    Log.i(TAG, "perfil $PROFILE_NAME: $state ${if (detail.isNotEmpty()) "($detail)" else ""}")
    try {
      sendEvent("onProfileStatus", mapOf("state" to state, "detail" to detail))
    } catch (e: Exception) {
      Log.w(TAG, "no se pudo avisar el estado a JavaScript: ${e.message}")
    }
  }

  /** Texto plano de un Bundle de resultado (RESULT_INFO): "RESULT_CODE=APP_ALREADY_ASSOCIATED; ...". */
  private fun describe(bundle: Bundle?): String {
    if (bundle == null) return ""
    return bundle.keySet().joinToString("; ") { key ->
      @Suppress("DEPRECATION")
      val value = bundle.get(key)
      val text = when (value) {
        is Array<*> -> value.joinToString(",")
        else -> value?.toString() ?: ""
      }
      "$key=$text"
    }
  }

  private fun onDataWedgeResult(intent: Intent) {
    // Respuesta a GET_ACTIVE_PROFILE: el nombre del perfil que DataWedge aplica a la app en primer plano.
    if (intent.hasExtra(EXTRA_RESULT_GET_ACTIVE_PROFILE)) {
      val active = intent.getStringExtra(EXTRA_RESULT_GET_ACTIVE_PROFILE) ?: ""
      if (active == PROFILE_NAME) setStatus("ready", active) else setStatus("noProfile", "activo: $active")
      return
    }
    // Respuesta a SET_CONFIG (creación del perfil): SUCCESS / FAILURE con RESULT_INFO.
    val command = intent.getStringExtra("COMMAND") ?: ""
    val commandId = intent.getStringExtra("COMMAND_IDENTIFIER") ?: ""
    if (commandId != CMD_ID_CREATE && !command.endsWith("SET_CONFIG")) return
    val result = intent.getStringExtra("RESULT") ?: ""
    @Suppress("DEPRECATION")
    val info = describe(intent.getBundleExtra("RESULT_INFO"))
    Log.i(TAG, "SET_CONFIG $PROFILE_NAME: $result $info")
    if (result.equals("SUCCESS", ignoreCase = true)) {
      // Creado o actualizado: falta confirmar que es el perfil activo de la app.
      sendGetActiveProfile()
    } else {
      setStatus("noProfile", listOf(result, info).filter { it.isNotEmpty() }.joinToString(" "))
    }
  }

  private fun sendGetActiveProfile() {
    val ctx = appContext.reactContext ?: return
    val i = Intent(DATAWEDGE_CONFIG_ACTION).apply {
      setPackage(DATAWEDGE_PACKAGE)
      putExtra(EXTRA_GET_ACTIVE_PROFILE, "")
      putExtra("SEND_RESULT", "true")
      putExtra("COMMAND_IDENTIFIER", CMD_ID_ACTIVE)
    }
    ctx.sendBroadcast(i)
  }

  private fun sendCreateProfileBroadcast() {
    val ctx = appContext.reactContext ?: return
    val packageName = ctx.packageName

    val profileConfig = Bundle().apply {
      putString("PROFILE_NAME", PROFILE_NAME)
      putString("PROFILE_ENABLED", "true")
      // Crea el perfil si no existe y, si ya existe, le aplica igual los plugins de abajo (corrige un perfil viejo con
      // las teclas encendidas).
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
      putBundle("PARAM_LIST", Bundle().apply {
        putString("scanner_selection", "auto")
        putString("scanner_input_enabled", "true")
      })
    }

    val intentConfig = Bundle().apply {
      putString("PLUGIN_NAME", "INTENT")
      putString("RESET_CONFIG", "true")
      putBundle(
        "PARAM_LIST",
        Bundle().apply {
          putString("intent_output_enabled", "true")
          putString("intent_action", SCAN_ACTION)
          putString("intent_category", Intent.CATEGORY_DEFAULT)
          putString("intent_delivery", "2") // 2 = broadcast intent
        },
      )
    }

    // Sin esto, el perfil nuevo escribe cada lectura como teclas en el campo enfocado (y abre el teclado en pantalla).
    val keystrokeConfig = Bundle().apply {
      putString("PLUGIN_NAME", "KEYSTROKE")
      putString("RESET_CONFIG", "true")
      putBundle("PARAM_LIST", Bundle().apply { putString("keystroke_output_enabled", "false") })
    }

    profileConfig.putParcelableArray("PLUGIN_CONFIG", arrayOf(barcodeConfig, intentConfig, keystrokeConfig))

    val bIntent = Intent(DATAWEDGE_CONFIG_ACTION).apply {
      setPackage(DATAWEDGE_PACKAGE)
      putExtra(EXTRA_SET_CONFIG, profileConfig)
      putExtra("SEND_RESULT", "true")
      putExtra("COMMAND_IDENTIFIER", CMD_ID_CREATE)
    }
    Log.i(TAG, "SET_CONFIG $PROFILE_NAME enviado (paquete $packageName)")
    ctx.sendBroadcast(bIntent)
  }
}
