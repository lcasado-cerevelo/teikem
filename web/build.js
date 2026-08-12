/* ============================================================================
   Generador del landing de Teikem.

   Una sola página por idioma: español en `index.html` e inglés en
   `en/index.html`. El armazón —cabecera, pie, formulario— vive aquí y los
   textos en `content/es.js` y `content/en.js`, para que traducir sea cambiar
   una cadena y no duplicar HTML. Duplicar HTML es la garantía de que un día el
   inglés diga una cosa y el español otra.

   Correr:  node build.js

   Sale HTML estático: sube a cualquier hosting. Lo único que necesita servidor
   es `contacto.php`, y si no está, el formulario lo dice en pantalla en vez de
   tragarse el mensaje.
   ========================================================================== */
'use strict';

const fs = require('fs');
const path = require('path');
const crypto = require('crypto');

const OUT = __dirname;

/* A DÓNDE LLEGAN LAS SOLICITUDES DE DEMOSTRACIÓN.
   Está aquí arriba y no enterrado: es el dato que hay que cambiar el día que
   Teikem tenga su propio buzón. También hay que cambiarlo en contacto.php. */
const CORREO = 'info@cerevelo.com';

const IDIOMAS = [
  { codigo: 'es', dir: '',    archivo: 'index.html',    otro: 'en', rutaOtro: 'en/index.html' },
  { codigo: 'en', dir: 'en',  archivo: 'index.html',    otro: 'es', rutaOtro: '../index.html' },
];

// ── Versión de los recursos ─────────────────────────────────────────────────
// Cambia cuando cambia el archivo, para que nadie se quede con la hoja de
// estilo vieja después de una actualización.
const versiones = {};
function version(rel) {
  if (versiones[rel] === undefined) {
    try {
      const buf = fs.readFileSync(path.join(OUT, rel));
      versiones[rel] = '?v=' + crypto.createHash('sha256').update(buf).digest('hex').slice(0, 8);
    } catch { versiones[rel] = ''; }
  }
  return versiones[rel];
}

// ── Iconos ──────────────────────────────────────────────────────────────────
const ICONOS = {
  orden:    '<path d="M7 4h10v16H7z"/><path d="M10 8h4M10 12h4M10 16h2"/>',
  ruta:     '<circle cx="6" cy="6" r="2.5"/><circle cx="18" cy="18" r="2.5"/><path d="M6 8.5v4a4 4 0 0 0 4 4h5.5"/>',
  camion:   '<path d="M2 7h11v9H2z"/><path d="M13 10h4l3 3v3h-7z"/><circle cx="6" cy="18" r="1.8"/><circle cx="17" cy="18" r="1.8"/>',
  almacen:  '<path d="M3 10 12 4l9 6v10H3z"/><path d="M8 20v-6h8v6"/>',
  caja:     '<path d="m12 3 8 4.5v9L12 21l-8-4.5v-9z"/><path d="m4 7.5 8 4.5 8-4.5M12 12v9"/>',
  movil:    '<rect x="7" y="2.5" width="10" height="19" rx="2"/><path d="M10.5 18.5h3"/>',
  firma:    '<path d="M3 18c3-1 4-9 7-9s2 7 5 7 3-4 6-4"/>',
  portal:   '<rect x="3" y="4" width="18" height="15" rx="2"/><path d="M3 9h18M7 6.5h.01M10 6.5h.01"/>',
  dinero:   '<circle cx="12" cy="12" r="8.5"/><path d="M12 7v10M14.5 9.5c0-1-1.1-1.8-2.5-1.8s-2.5.8-2.5 1.8 1.1 1.6 2.5 1.9 2.5.9 2.5 1.9-1.1 1.8-2.5 1.8-2.5-.8-2.5-1.8"/>',
  grafico:  '<path d="M4 20V10M10 20V4M16 20v-7M22 20H2"/>',
  enchufe:  '<path d="M9 3v6M15 3v6"/><path d="M6 9h12v3a6 6 0 0 1-12 0z"/><path d="M12 18v3"/>',
  compras:  '<path d="M3 4h2l2.5 11h10L20 7H6"/><circle cx="9" cy="19" r="1.6"/><circle cx="17" cy="19" r="1.6"/>',
  escudo:   '<path d="M12 3 5 6v6c0 4.4 3 8.2 7 9 4-.8 7-4.6 7-9V6Z"/><path d="m9.2 12 2 2 3.6-3.8"/>',
  sinsenal: '<path d="M3 3l18 18"/><path d="M5 13a10 10 0 0 1 4-2.4M2 9a15 15 0 0 1 5-3.1M12 20h.01"/><path d="M19 13a10 10 0 0 0-3-2.2M22 9a15 15 0 0 0-6-3.4"/>',
  mapa:     '<path d="m9 5 6 2 6-2v13l-6 2-6-2-6 2V7z"/><path d="M9 5v13M15 7v13"/>',
  interruptores: '<rect x="3" y="4.5" width="18" height="6" rx="3"/><circle cx="8" cy="7.5" r="1.6"/><rect x="3" y="13.5" width="18" height="6" rx="3"/><circle cx="16" cy="16.5" r="1.6"/>',
  cruce:    '<path d="M3 7h5l8 10h5"/><path d="M3 17h5l8-10h5"/><path d="m18 4 3 3-3 3M18 14l3 3-3 3"/>',
  lote:     '<path d="M4 5v14M7.5 5v14M11 5v9M14.5 5v14M18 5v9M21 5v14"/>',
  teki:     '<circle cx="12" cy="13" r="7.5"/><path d="M12 5.5V3M9.5 13h.01M14.5 13h.01M9 16.5c1.8 1.2 4.2 1.2 6 0"/><path d="M4 12h-.5M20 12h.5"/>',
  llave:    '<circle cx="8" cy="12" r="4"/><path d="M12 12h9M18 12v3M21 12v2"/>',
};
const icono = (n) => `<span class="icono" aria-hidden="true"><svg viewBox="0 0 24 24">${ICONOS[n] || ICONOS.caja}</svg></span>`;

// ── Armazón ─────────────────────────────────────────────────────────────────

function cabecera(t, idioma) {
  const enlaces = t.nav.map(([ancla, texto]) => `        <li><a href="#${ancla}">${texto}</a></li>`).join('\n');
  return `<header class="cabecera">
  <div class="wrap">
    <a class="marca" href="#" aria-label="Teikem — ${t.lema}">
      <img src="assets/img/logo-${idioma.codigo}.svg" alt="Teikem. ${t.lema}." width="422" height="104">
    </a>
    <button class="alterna" type="button" aria-expanded="false" aria-controls="menu">
      <span></span> ${t.menu}
    </button>
    <nav class="menu" id="menu" data-abierto="no" aria-label="${t.menuAria}">
      <ul>
${enlaces}
        <li class="idioma"><a href="${idioma.rutaOtro}" hreflang="${idioma.otro}">${t.otroIdioma}</a></li>
        <li><a class="boton" href="#demo">${t.cta}</a></li>
      </ul>
    </nav>
  </div>
</header>`;
}

function formulario(t) {
  const opciones = t.form.temas.map((o) => `              <option>${o}</option>`).join('\n');
  return `        <form class="formulario" id="formulario-demo"
              action="mailto:${CORREO}" method="post" enctype="text/plain"
              data-endpoint="contacto.php">
          <!-- Campo trampa: ninguna persona lo ve ni lo puede enfocar. El robot
               lo llena porque llena todo, y ahí se delata. -->
          <div class="trampa" aria-hidden="true">
            <label for="apellido2">${t.form.trampa}</label>
            <input id="apellido2" name="Apellido2" type="text" tabindex="-1" autocomplete="off">
          </div>
          <div class="doble">
            <div class="campo">
              <label for="nombre">${t.form.nombre}</label>
              <input id="nombre" name="Nombre" type="text" autocomplete="name" required>
            </div>
            <div class="campo">
              <label for="empresa">${t.form.empresa}</label>
              <input id="empresa" name="Empresa" type="text" autocomplete="organization" required>
            </div>
          </div>
          <div class="doble">
            <div class="campo">
              <label for="correo">${t.form.correo}</label>
              <input id="correo" name="Correo" type="email" autocomplete="email" required>
            </div>
            <div class="campo">
              <label for="telefono">${t.form.telefono}</label>
              <input id="telefono" name="Telefono" type="tel" autocomplete="tel">
            </div>
          </div>
          <div class="doble">
            <div class="campo">
              <label for="flota">${t.form.flota}</label>
              <select id="flota" name="Flota">
${opciones}
              </select>
            </div>
            <div class="campo">
              <label for="entregas">${t.form.entregas}</label>
              <input id="entregas" name="Entregas" type="text" placeholder="${t.form.entregasPista}">
            </div>
          </div>
          <div class="campo">
            <label for="mensaje">${t.form.mensaje}</label>
            <textarea id="mensaje" name="Mensaje" required placeholder="${t.form.mensajePista}"></textarea>
          </div>
          <div>
            <button class="boton boton--grande" type="submit">${t.form.enviar}</button>
            <p class="letra-chica">${t.form.nota} <a href="mailto:${CORREO}">${CORREO}</a>.</p>
          </div>
        </form>`;
}

function pie(t, idioma) {
  return `<footer class="pie">
  <div class="wrap">
    <div class="pie-alto">
      <img src="assets/img/logo-${idioma.codigo}-inv.svg" alt="Teikem. ${t.lema}." width="422" height="104">
      <p>${t.pie.resumen}</p>
    </div>
    <p class="creditos">${t.pie.creditos}</p>
    <div class="pie-legal">
      <span>© <span data-anio>2026</span> Teikem. ${t.pie.derechos}</span>
      <span>${t.pie.hecho}</span>
    </div>
  </div>
</footer>`;
}

function pagina(t, idioma) {
  const raiz = idioma.dir ? '../' : '';
  return `<!doctype html>
<html lang="${idioma.codigo}">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>${t.title}</title>
<meta name="description" content="${t.description}">
<meta name="theme-color" content="#0B2C66">
<link rel="icon" type="image/png" href="${raiz}assets/img/icono.png">
<link rel="alternate" hreflang="${idioma.otro}" href="${idioma.rutaOtro}">
<meta property="og:type" content="website">
<meta property="og:title" content="${t.title}">
<meta property="og:description" content="${t.description}">
<meta property="og:image" content="${raiz}assets/img/pantalla-pulso-1600.png">
<link rel="stylesheet" href="${raiz}assets/css/teikem.css${version('assets/css/teikem.css')}">
</head>
<body>
<a class="saltar" href="#contenido">${t.saltar}</a>
${cabecera(t, idioma).replaceAll('assets/', raiz + 'assets/')}
<main id="contenido">
${t.cuerpo({ icono, formulario: () => formulario(t) }).replaceAll('assets/', raiz + 'assets/')}
</main>
${pie(t, idioma).replaceAll('assets/', raiz + 'assets/')}
<script src="${raiz}assets/js/landing.js${version('assets/js/landing.js')}" defer></script>
</body>
</html>
`;
}

// ── Escritura ───────────────────────────────────────────────────────────────

const avisos = [];
let escritas = 0;

for (const idioma of IDIOMAS) {
  const t = require(`./content/${idioma.codigo}`);
  const html = pagina(t, idioma);

  // Un ancla que no lleva a ninguna parte es un enlace roto que nadie reporta.
  const ids = new Set([...html.matchAll(/id="([^"]+)"/g)].map((m) => m[1]));
  for (const m of html.matchAll(/href="#([^"]+)"/g)) {
    if (m[1] && !ids.has(m[1])) avisos.push(`${idioma.codigo}: el enlace #${m[1]} no lleva a ninguna sección`);
  }

  // Imágenes que se nombran pero no existen.
  for (const m of new Set([...html.matchAll(/assets\/img\/([a-zA-Z0-9._-]+\.(?:jpg|png|svg))/g)].map((x) => x[1]))) {
    if (!fs.existsSync(path.join(OUT, 'assets', 'img', m))) avisos.push(`${idioma.codigo}: falta la imagen ${m}`);
  }

  const destino = path.join(OUT, idioma.dir, idioma.archivo);
  fs.mkdirSync(path.dirname(destino), { recursive: true });
  fs.writeFileSync(destino, html, 'utf8');
  escritas++;
}

// Las dos versiones tienen que tener las mismas secciones: si una traducción se
// queda corta, el menú del otro idioma apunta a un sitio que no existe.
const secciones = IDIOMAS.map((i) => {
  const html = fs.readFileSync(path.join(OUT, i.dir, i.archivo), 'utf8');
  return [i.codigo, [...html.matchAll(/<section[^>]+id="([^"]+)"/g)].map((m) => m[1]).join(',')];
});
if (secciones[0][1] !== secciones[1][1]) {
  avisos.push(`las secciones no coinciden entre idiomas:\n      es: ${secciones[0][1]}\n      en: ${secciones[1][1]}`);
}

console.log(`${escritas} páginas escritas en ${OUT}`);
if (avisos.length) {
  console.log('\nAvisos:');
  for (const a of [...new Set(avisos)]) console.log('  · ' + a);
  process.exitCode = 1;
} else {
  console.log('Sin anclas rotas, sin imágenes que falten y las dos versiones tienen las mismas secciones.');
}
