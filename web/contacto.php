<?php
/* ============================================================================
   Recibe el formulario de solicitud de demostración y manda el correo.

   Vive en el mismo hosting que el sitio (GoDaddy, Linux con cPanel), así que
   no hay que montar nada aparte ni pagar un servicio externo. El navegador
   nunca ve credenciales: todo pasa de este lado.

   El guion assets/js/contacto.js manda aquí un POST y espera JSON de vuelta.
   Si alguien llega sin JavaScript, el formulario cae al mailto: y este archivo
   ni se entera.

   ── SI EL CORREO NO LLEGA ─────────────────────────────────────────────────
   Lo primero: que el buzón info@cerevelo.com EXISTA en GoDaddy. Si no existe,
   el correo se manda y se pierde.
   Lo segundo: mail() usa el servidor del propio hosting. Entrega bien cuando
   el destino está en el mismo dominio, que es este caso. Si algún día hiciera
   falta mandar a un Gmail de fuera y cayera en spam, la salida es cambiar a
   SMTP con PHPMailer — está explicado al final del archivo.
   ========================================================================== */

declare(strict_types=1);

// ── Configuración ───────────────────────────────────────────────────────────
$DESTINO   = 'info@cerevelo.com';   // a dónde llegan los mensajes
$REMITENTE = 'info@cerevelo.com';   // DEBE ser un buzón del propio dominio (ver nota SPF abajo)
$SITIO     = 'teikem.com';
$MAXIMO_POR_HORA = 5;               // mensajes por dirección IP

header('Content-Type: application/json; charset=utf-8');

/** Contesta y termina. El guion del navegador solo mira el código HTTP. */
function responder(int $codigo, string $mensaje): void {
    http_response_code($codigo);
    echo json_encode(['ok' => $codigo === 200, 'mensaje' => $mensaje], JSON_UNESCAPED_UNICODE);
    exit;
}

// ── Solo POST ───────────────────────────────────────────────────────────────
if (($_SERVER['REQUEST_METHOD'] ?? '') !== 'POST') {
    responder(405, 'Método no permitido.');
}

// ── La trampa para robots ───────────────────────────────────────────────────
// El campo está escondido con CSS; una persona no lo ve ni lo puede enfocar.
// Si viene lleno, es un robot. Se contesta 200 a propósito: si le decimos que
// falló, vuelve a intentar con otra táctica.
if (trim((string)($_POST['Apellido2'] ?? '')) !== '') {
    responder(200, 'Recibido.');
}

// ── Freno por IP ────────────────────────────────────────────────────────────
// Sin base de datos: un archivito en el directorio temporal del hosting con la
// cuenta de la última hora. No es infalible, pero corta el envío repetido, que
// es de lo que se trata.
$ip = (string)($_SERVER['REMOTE_ADDR'] ?? 'desconocida');
$marcador = sys_get_temp_dir() . '/cerevelo-contacto-' . sha1($ip) . '.txt';
$ahora = time();
$sellos = [];
if (is_readable($marcador)) {
    $sellos = array_filter(
        array_map('intval', explode(',', (string)file_get_contents($marcador))),
        static fn(int $t): bool => $t > $ahora - 3600
    );
}
if (count($sellos) >= $MAXIMO_POR_HORA) {
    responder(429, 'Ha enviado varios mensajes seguidos. Inténtelo más tarde o escriba directamente a ' . $DESTINO . '.');
}
$sellos[] = $ahora;
@file_put_contents($marcador, implode(',', $sellos), LOCK_EX);

// ── Los datos ───────────────────────────────────────────────────────────────
/** Recorta, normaliza y limita el largo. Nada de lo que llega es de fiar. */
function campo(string $nombre, int $largo = 200): string {
    $v = (string)($_POST[$nombre] ?? '');
    $v = trim(str_replace(["\r", "\0"], '', $v));
    return mb_substr($v, 0, $largo);
}

$nombre   = campo('Nombre');
$empresa  = campo('Empresa');
$correo   = campo('Correo');
$telefono = campo('Telefono', 40);
$flota    = campo('Flota', 80);
$entregas = campo('Entregas', 40);
$mensaje  = campo('Mensaje', 5000);

if ($nombre === '' || $correo === '' || $empresa === '' || $mensaje === '') {
    responder(400, 'Faltan datos obligatorios.');
}
if (!filter_var($correo, FILTER_VALIDATE_EMAIL)) {
    responder(400, 'La dirección de correo no parece válida.');
}

// ── Cabeceras ───────────────────────────────────────────────────────────────
// AQUÍ ESTÁ EL ÚNICO PELIGRO REAL DE UN FORMULARIO ASÍ. Si se mete texto del
// visitante en una cabecera sin limpiar los saltos de línea, cualquiera puede
// añadir un «Bcc:» y convertir el formulario en una máquina de spam a nombre
// de Cerevelo. Por eso:
//   · el From es SIEMPRE un buzón nuestro, nunca el del visitante —además así
//     el SPF del dominio sigue cuadrando y el correo no cae en spam—;
//   · la dirección del visitante va en Reply-To, ya validada como correo, que
//     es lo que uno quiere igual: se le contesta con «responder».
$de = mb_encode_mimeheader($nombre, 'UTF-8', 'B') . ' <' . $REMITENTE . '>';
$cabeceras = [
    'From: ' . $de,
    'Reply-To: ' . $correo,
    'MIME-Version: 1.0',
    'Content-Type: text/plain; charset=UTF-8',
    'X-Mailer: PHP/' . phpversion(),
];

// La empresa va en el asunto: quien recibe estas solicitudes necesita saber de
// quién es antes de abrirla, no después.
$asunto = mb_encode_mimeheader('Demostración · ' . ($empresa !== '' ? $empresa : $nombre), 'UTF-8', 'B');

$cuerpo = implode("\n", [
    'Solicitud de demostración desde ' . $SITIO,
    str_repeat('-', 52),
    'Nombre:          ' . $nombre,
    'Empresa:         ' . $empresa,
    'Correo:          ' . $correo,
    'Teléfono:        ' . ($telefono !== '' ? $telefono : '—'),
    'Flota:           ' . ($flota !== '' ? $flota : '—'),
    'Entregas al día: ' . ($entregas !== '' ? $entregas : '—'),
    str_repeat('-', 52),
    '',
    $mensaje,
    '',
    str_repeat('-', 52),
    'Recibido: ' . date('d/m/Y g:i a'),
    'IP: ' . $ip,
]);

// El quinto parámetro le dice al servidor de correo quién es el remitente de
// sobre. Sin esto, GoDaddy manda como el usuario de Linux del hosting y el
// correo llega con cara de sospechoso.
$enviado = @mail($DESTINO, $asunto, $cuerpo, implode("\r\n", $cabeceras), '-f' . $REMITENTE);

if (!$enviado) {
    // Sin «no pudimos enviar»: el guion ya lo dice y quedaría repetido.
    responder(500, 'El servidor de correo del hosting rechazó el envío.');
}
responder(200, 'Mensaje enviado.');

/* ── SI HUBIERA QUE PASAR A SMTP ────────────────────────────────────────────
   mail() entrega a través del servidor del hosting. Si alguna vez los mensajes
   dejaran de llegar o cayeran en spam, el arreglo es mandarlos autenticado
   contra el buzón de verdad, con PHPMailer:

     1. Bajar PHPMailer y subir la carpeta src/ al hosting.
     2. Sustituir la llamada a mail() por:

        require 'PHPMailer/src/PHPMailer.php';
        require 'PHPMailer/src/SMTP.php';
        require 'PHPMailer/src/Exception.php';
        $m = new PHPMailer\PHPMailer\PHPMailer(true);
        $m->isSMTP();
        $m->Host       = 'smtpout.secureserver.net';  // el de GoDaddy
        $m->SMTPAuth   = true;
        $m->Username   = 'info@cerevelo.com';
        $m->Password   = '...';                        // ver la nota de abajo
        $m->SMTPSecure = 'ssl';
        $m->Port       = 465;
        $m->CharSet    = 'UTF-8';

   LA CONTRASEÑA NO VA EN ESTE ARCHIVO. Se pone en un archivo aparte fuera de
   public_html (por ejemplo /home/usuario/config-correo.php) y se incluye. Si
   se deja aquí dentro, un error de configuración del servidor puede llegar a
   servir el .php como texto y enseñarla.
   ========================================================================= */
