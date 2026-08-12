/* ============================================================================
   Menú, año del pie y formulario de demostración.

   Sin dependencias. Lo primero que hace es marcar el <body>: hasta entonces la
   hoja de estilo deja el menú visible, para que quien navegue sin JavaScript no
   se quede con un botón que no abre nada.
   ========================================================================== */
'use strict';

document.body.dataset.js = 'si';

// ── Menú en pantalla estrecha ───────────────────────────────────────────────
(function () {
  const boton = document.querySelector('.alterna');
  const menu = document.getElementById('menu');
  if (!boton || !menu) return;

  const abrir = (si) => {
    menu.dataset.abierto = si ? 'si' : 'no';
    boton.setAttribute('aria-expanded', String(si));
  };
  boton.addEventListener('click', () => abrir(menu.dataset.abierto !== 'si'));
  // Al pulsar un ancla el menú estorba: la sección a la que lleva queda debajo.
  menu.addEventListener('click', (e) => { if (e.target.closest('a')) abrir(false); });
  document.addEventListener('keydown', (e) => { if (e.key === 'Escape') abrir(false); });
})();

// ── Año del pie ─────────────────────────────────────────────────────────────
for (const n of document.querySelectorAll('[data-anio]')) n.textContent = String(new Date().getFullYear());

// ── Formulario ──────────────────────────────────────────────────────────────
/* Dos caminos. Si hay `data-endpoint`, el mensaje se manda ahí de verdad y la
   pantalla dice si llegó. Si no lo hay —o el servidor no responde— se abre el
   programa de correo del visitante y se le avisa, porque mucha gente usa
   webmail y entonces no se abre nada: un botón que parezca enviar y se trague
   el mensaje es peor que no tener formulario. */
(function () {
  const form = document.getElementById('formulario-demo');
  if (!form) return;

  const destino = (form.getAttribute('action') || '').replace('mailto:', '');
  const endpoint = (form.dataset.endpoint || '').trim();
  const boton = form.querySelector('button[type=submit]');
  const ingles = document.documentElement.lang === 'en';

  const T = ingles ? {
    enviando: 'Sending…',
    bien: '<strong>Message sent.</strong> We answer within the next business day.',
    mal: '<strong>We could not send the message.</strong> Please write to',
    ojo: '<strong>We opened your email program with the message ready.</strong> If nothing opened — which happens with webmail — copy the message and send it to',
    copiar: 'Copy the message',
    copiado: 'Copied',
    sinCopiar: 'Your browser will not allow copying',
    asunto: 'Demo request from teikem.com',
  } : {
    enviando: 'Enviando…',
    bien: '<strong>Mensaje enviado.</strong> Le contestamos dentro del próximo día laborable.',
    mal: '<strong>No pudimos enviar el mensaje.</strong> Escríbanos a',
    ojo: '<strong>Abrimos su programa de correo con el mensaje listo.</strong> Si no se abrió nada —pasa cuando se usa webmail—, copie el mensaje y mándelo a',
    copiar: 'Copiar el mensaje',
    copiado: 'Copiado',
    sinCopiar: 'Su navegador no deja copiar',
    asunto: 'Solicitud de demostración desde teikem.com',
  };

  const aviso = document.createElement('div');
  aviso.className = 'aviso-envio';
  aviso.hidden = true;
  aviso.setAttribute('role', 'status');
  form.appendChild(aviso);

  const mostrar = (html, tono) => {
    aviso.className = 'aviso-envio aviso-envio--' + tono;
    aviso.innerHTML = html;
    aviso.hidden = false;
  };

  const texto = () => {
    const d = new FormData(form);
    const l = (c) => `${c}: ${d.get(c) || '—'}`;
    return [l('Nombre'), l('Empresa'), l('Correo'), l('Telefono'), l('Flota'), l('Entregas'), '', String(d.get('Mensaje') || '')].join('\n');
  };

  async function enviar(ev) {
    ev.preventDefault();
    const original = boton.textContent;
    boton.disabled = true;
    boton.textContent = T.enviando;
    try {
      const r = await fetch(endpoint, { method: 'POST', headers: { Accept: 'application/json' }, body: new FormData(form) });
      let dice = null;
      try { dice = await r.json(); } catch { /* contestó algo que no es JSON */ }
      if (!r.ok) throw new Error((dice && dice.mensaje) || 'respuesta ' + r.status);
      form.reset();
      mostrar(T.bien, 'bien');
    } catch (e) {
      const motivo = e && e.message && !/^respuesta /.test(e.message) ? `<br>${e.message}` : '';
      mostrar(`${T.mal} <a href="mailto:${destino}">${destino}</a>.${motivo}`, 'mal');
    } finally {
      boton.disabled = false;
      boton.textContent = original;
    }
  }

  function abrirCorreo(ev) {
    ev.preventDefault();
    const cuerpo = texto();
    // Un enlace de verdad y no `location.href`: si el navegador no sabe abrir
    // mailto:, un enlace no hace nada en vez de dejar la página en un estado raro.
    const a = document.createElement('a');
    a.href = `mailto:${destino}?subject=${encodeURIComponent(T.asunto)}&body=${encodeURIComponent(cuerpo)}`;
    a.click();
    mostrar(`${T.ojo} <a href="mailto:${destino}">${destino}</a>.
      <button type="button" class="boton" data-copiar>${T.copiar}</button>`, 'ojo');
    const copiar = aviso.querySelector('[data-copiar]');
    copiar.addEventListener('click', async () => {
      try {
        await navigator.clipboard.writeText(`${destino}\n${T.asunto}\n\n${cuerpo}`);
        copiar.textContent = T.copiado;
      } catch { copiar.textContent = T.sinCopiar; }
    });
  }

  form.addEventListener('submit', endpoint ? enviar : abrirCorreo);
})();
