'use strict';

/* English copy. Same structure and the same section ids as `es.js` — the build
   compares them and complains if one language drifts from the other. */

module.exports = {
  title: 'Teikem — Delivery Intelligence · logistics operations platform',
  description: 'Teikem runs the whole logistics operation: orders, routes, fleet, warehouse, cross-docking, an offline driver app, proof of delivery, cash on delivery, invoicing and driver settlement. Thirteen modules, one platform.',
  lema: 'Delivery Intelligence',
  saltar: 'Skip to content',
  menu: 'Menu',
  menuAria: 'Main',
  otroIdioma: 'Español',
  cta: 'Request a demo',

  nav: [
    ['operacion', 'The operation'],
    ['plataforma', 'The platform'],
    ['modulos', 'Modules'],
    ['diferencia', 'Why Teikem'],
    ['teki', 'Teki'],
  ],

  form: {
    nombre: 'Name',
    empresa: 'Company',
    correo: 'Email',
    telefono: 'Phone',
    flota: 'Fleet size',
    entregas: 'Deliveries per day, roughly',
    entregasPista: 'e.g. 300',
    mensaje: 'What hurts today?',
    mensajePista: 'What you use now, what slips through, and when you need it.',
    enviar: 'Request a demo',
    nota: 'We answer within the next business day. If you would rather write directly:',
    trampa: 'Leave this field empty',
    temas: ['1 to 5 vehicles', '6 to 20 vehicles', '21 to 50 vehicles', 'More than 50 vehicles', 'No fleet of my own yet'],
  },

  pie: {
    resumen: 'Logistics operations platform for carriers, distributors and last-mile operators. Thirteen modules, web and mobile, with a driver app that keeps working without a signal.',
    creditos: 'Photography from Unsplash: Ashley, Blake Wisz, Claudio Schwarz, Luke Heibert, Maarten van den Heuvel, Maksym Tymchyk.',
    derechos: 'All rights reserved.',
    hecho: 'Made in Puerto Rico.',
  },

  cuerpo: ({ icono, formulario }) => `
<section class="portada">
  <div class="wrap">
    <div class="portada-texto">
      <span class="antetitulo">Logistics operations, end to end</span>
      <h1>From the order to the door, <em>and the cash back</em></h1>
      <p class="entradilla">A logistics operator does not do one thing: it picks up, stores,
      consolidates, hauls, delivers, collects at the door and invoices. Teikem covers all
      seven on one platform, without spreadsheets and phone calls as the glue.</p>
      <div class="acciones">
        <a class="boton boton--grande" href="#demo">Request a demo</a>
        <a class="boton-linea" href="#operacion">See how it works</a>
      </div>
      <p class="letra-chica">No commitment. A short call to understand your operation before
      we show you anything.</p>
    </div>
    <div class="portada-pantalla">
      <img src="assets/img/pantalla-pulso-1600.png" alt="Teikem screen: the pulse of the day with parcels on the street, COD cash coming back and the operation indicators" width="1600" height="1165" fetchpriority="high">
    </div>
  </div>
</section>

<section class="cinta" aria-hidden="true">
  <div class="wrap">
    <span>Orders</span><span>Routes</span><span>Fleet</span><span>Warehouse</span>
    <span>Cross-docking</span><span>Inventory</span><span>Driver app</span>
    <span>Proof of delivery</span><span>Cash on delivery</span><span>Invoicing</span>
    <span>Settlement</span><span>Customer portal</span><span>Integrations</span>
  </div>
</section>

<section class="seccion" id="operacion">
  <div class="wrap">
    <div class="cabeza">
      <span class="antetitulo">The operation</span>
      <h2>The day, step by step</h2>
      <p>What in most companies is split across four programs and a notebook is one single
      line here, and you can look at it any time.</p>
    </div>

    <ol class="flujo">
      <li>
        <span class="paso">01</span>
        <h3>The order comes in</h3>
        <p>From the customer portal, from a phone call or from another system through the
        API — with its contract and its rate already applied.</p>
      </li>
      <li>
        <span class="paso">02</span>
        <h3>Received and put away</h3>
        <p>Inbound scan, a location in the warehouse, or straight from dock to dock when it
        is cross-docking. Freight is never just «somewhere».</p>
      </li>
      <li>
        <span class="paso">03</span>
        <h3>Dispatched</h3>
        <p>Assigned to a driver and a vehicle, stops in order, route pushed to the phone —
        with the exceptions of a real day: nobody home, an address that does not exist.</p>
      </li>
      <li>
        <span class="paso">04</span>
        <h3>Delivered</h3>
        <p>Signature, photos and GPS on the spot. Partial delivery line by line and a coded
        reason when something fails, which is half the time.</p>
      </li>
      <li>
        <span class="paso">05</span>
        <h3>Collected and reconciled</h3>
        <p>Cash collected at the door stays open on the driver's settlement until it is
        remitted and the total ties out. Then invoicing.</p>
      </li>
    </ol>
  </div>
</section>

<section class="banda">
  <img src="assets/img/mensajero-1600.jpg" alt="" loading="lazy">
  <div class="wrap">
    <h2>Everyone looking at the same number</h2>
    <p>Dispatch, the warehouse, accounting and the owner stop arguing over different figures:
    there is one operation and one version of what happened.</p>
  </div>
</section>

<section class="seccion seccion--alt" id="plataforma">
  <div class="wrap">
    <div class="dos">
      <div>
        <span class="antetitulo">The platform</span>
        <h2>The cash coming back is part of the delivery too</h2>
        <p>With cash on delivery the hard part is not marking «delivered»: it is that the
        money the driver collected shows up in full at the end of the day. Teikem chases it —
        what was collected on the street stays open on the settlement of whoever collected it
        until it is remitted and ties out against what the system recorded.</p>
        <p>Same with the evidence. Proof of delivery is not filed so nobody looks at it: it is
        filed for the day the customer claims it never arrived.</p>
        <ul class="marca">
          <li><strong>Full COD cycle</strong>, from collection at the door to remittance.</li>
          <li><strong>Settlement by driver and by route</strong>, with what is missing in plain sight.</li>
          <li><strong>Proof of delivery filed</strong> and available in seconds.</li>
          <li><strong>Invoicing</strong> connected, with nobody retyping what was already typed.</li>
        </ul>
      </div>
      <div>
        <img class="pantalla" src="assets/img/pantalla-dia-1600.png" alt="Teikem screen for processing deliveries: to collect, collected today, reconciling and remitted" width="1600" height="722" loading="lazy">
      </div>
    </div>
  </div>
</section>

<section class="seccion" id="modulos">
  <div class="wrap">
    <div class="cabeza">
      <span class="antetitulo">Modules</span>
      <h2>Thirteen modules, and you turn on the ones you use</h2>
      <p>No two operations are alike: one charges per pickup and another per delivery, one
      cross-docks and another stores. Each company turns on what it needs and turns off the
      rest — no custom versions, no screens that are in the way.</p>
    </div>
    <div class="rejilla">
      <div class="tarjeta">${icono('orden')}<h3>Customers and contracts</h3><p>Rates, terms and service agreements per customer.</p></div>
      <div class="tarjeta">${icono('caja')}<h3>Transport orders</h3><p>From the order to the delivery, with its status at all times.</p></div>
      <div class="tarjeta">${icono('ruta')}<h3>Trips and routes</h3><p>Planning, stop sequencing and live tracking.</p></div>
      <div class="tarjeta">${icono('camion')}<h3>Fleet and drivers</h3><p>Who drives what, with which documents current and what service is due.</p></div>
      <div class="tarjeta">${icono('almacen')}<h3>Warehouses</h3><p>Locations, movements, picking and stock ledger.</p></div>
      <div class="tarjeta">${icono('cruce')}<h3>Cross-docking</h3><p>Consolidating freight that only goes from dock to dock.</p></div>
      <div class="tarjeta">${icono('lote')}<h3>Inventory and traceability</h3><p>By lot and serial, with the genealogy of what came from what.</p></div>
      <div class="tarjeta">${icono('movil')}<h3>Driver app</h3><p>The route on the phone, working without a signal.</p></div>
      <div class="tarjeta">${icono('firma')}<h3>Proof of delivery</h3><p>Signature, photos, GPS and partial delivery line by line.</p></div>
      <div class="tarjeta">${icono('portal')}<h3>Customer portal</h3><p>Your customer sees their shipments and their evidence without calling anyone.</p></div>
      <div class="tarjeta">${icono('dinero')}<h3>Invoicing and settlement</h3><p>Cash on delivery, remittance and close-out by driver.</p></div>
      <div class="tarjeta">${icono('compras')}<h3>Purchasing and equipment</h3><p>Suppliers, purchase orders and equipment on rent.</p></div>
      <div class="tarjeta">${icono('grafico')}<h3>Dashboards and indicators</h3><p>Yours: your own fields, reports and charts, without programming.</p></div>
    </div>
  </div>
</section>


<!-- Tira de fotos entre dos bloques densos. Es respiro visual, no información:
     por eso va oculta a los lectores de pantalla. -->
<section class="tira" aria-hidden="true">
  <img src="assets/img/furgoneta-800.jpg" alt="" loading="lazy">
  <img src="assets/img/nave-800.jpg" alt="" loading="lazy">
  <img src="assets/img/paquetes-800.jpg" alt="" loading="lazy">
  <img src="assets/img/clasificacion-800.jpg" alt="" loading="lazy">
</section>

<section class="seccion seccion--oscura" id="diferencia">
  <div class="wrap">
    <div class="cabeza">
      <span class="antetitulo">Why Teikem</span>
      <h2>What separates a logistics system from one that survives the street</h2>
    </div>
    <div class="rejilla rejilla--3">
      <div class="tarjeta">${icono('sinsenal')}<h3>The street has no internet</h3>
        <p>Loading docks, basements and roads with no coverage. The route downloads whole and
        what happens outside is stored on the phone and waits. A system that demands a
        connection ends up replaced by a notebook.</p></div>
      <div class="tarjeta">${icono('llave')}<h3>Retry without duplicating</h3>
        <p>When the signal returns, the queue drains in order. Every operation carries an
        idempotency key, so a retry does not charge the same shipment twice.</p></div>
      <div class="tarjeta">${icono('mapa')}<h3>Open maps</h3>
        <p>OpenStreetMap instead of a metered service: your bill does not grow with every
        stop you plan.</p></div>
      <div class="tarjeta">${icono('interruptores')}<h3>The workflow is data, not code</h3>
        <p>You turn on the stages you use and define which ones allow a cancellation.
        Changing how you work should not be a programming project.</p></div>
      <div class="tarjeta">${icono('escudo')}<h3>Permissions, MFA and audit</h3>
        <p>There is money involved. Role-based permissions, a second factor and audit on
        three planes: what changed, who changed it and what happened in security.</p></div>
      <div class="tarjeta">${icono('enchufe')}<h3>It connects to what you already have</h3>
        <p>An API to integrate with accounting, e-commerce or your customer's system. Nobody
        is throwing out their accounting because we showed up.</p></div>
    </div>
  </div>
</section>

<section class="seccion seccion--alt" id="para-quien">
  <div class="wrap">
    <div class="dos dos--invertido">
      <figure class="figura">
        <img src="assets/img/ultima-milla-800.jpg" alt="A courier walking down the sidewalk carrying parcels" width="800" height="533" loading="lazy">
      </figure>
      <div>
        <span class="antetitulo">Who it is for</span>
        <h2>If your operation touches the street, it is for you</h2>
        <ul class="marca">
          <li><strong>Carriers and couriers</strong> dispatching routes every day and settling
          drivers every week.</li>
          <li><strong>Distributors</strong> that store, pick orders and deliver with their own
          fleet.</li>
          <li><strong>Last-mile operators</strong> with cash on delivery and proof of delivery
          their customers demand.</li>
          <li><strong>Third-party logistics providers</strong> working for several companies
          that need each one's data genuinely separated.</li>
        </ul>
        <p>And if you already have a system covering part of this, Teikem can connect to it
        instead of asking you to throw it away.</p>
      </div>
    </div>
  </div>
</section>

<section class="seccion" id="teki">
  <div class="wrap">
    <div class="dos">
      <div>
        <span class="antetitulo">Artificial intelligence</span>
        <h2>Teki watches the operation when you cannot</h2>
        <p>A dashboard shows numbers; you still have to read them and remember to look.
        Teki is Teikem's assistant and does the opposite: it tells you in one sentence what
        needs your decision today, working from your operation's data — not from
        generalities off the internet.</p>
        <ul class="marca">
          <li><strong>It sums up the day.</strong> What is stuck, what is due within the
          hour and what money is still unreconciled, without you going to look for it.</li>
          <li><strong>It drafts the repetitive part.</strong> The email to the customer when
          a delivery fails, with the reason and the evidence already in it. You read it and
          you send it.</li>
          <li><strong>It sorts what comes in.</strong> Failed-delivery reasons, incidents and
          driver notes, classified on their own so the monthly report comes out of the data
          and not out of somebody's memory.</li>
          <li><strong>It answers questions about the system</strong> in plain language, so
          nobody has to build a report to find out one figure.</li>
        </ul>

        <div class="reglas">
          <p><strong>Two rules that are not up for negotiation.</strong> The final decision
          belongs to a person: Teki proposes, it never acts on its own. And your information
          stays yours — it is your company's data, not anybody's training material.</p>
        </div>
      </div>

      <div class="charla" aria-label="Example of a conversation with Teki">
        <div class="charla-cabeza">${icono('teki')}<div><b>Teki</b><span>Teikem assistant</span></div></div>
        <p class="dice-usted">How much COD is collected but not reconciled?</p>
        <p class="dice-teki"><b>$2,230</b> across 14 orders, all from yesterday and all from
        the same driver. Scanned, not remitted.<br><span>Open the settlement →</span></p>
        <p class="dice-usted">And what do I have to decide today?</p>
        <p class="dice-teki">Three things: <b>83 orders</b> are waiting for a driver,
        <b>3 deliveries</b> are due within the hour and <b>7 fleet documents</b> expire this
        week.</p>
      </div>
    </div>
  </div>
</section>

<section class="seccion seccion--alt" id="demo">
  <div class="wrap">
    <div class="dos">
      <div>
        <span class="antetitulo">Let's talk</span>
        <h2>A demo with your operation in mind</h2>
        <p>This is not a recorded presentation. Before showing you anything we would rather
        understand how you dispatch, how you collect and where your time goes — and if Teikem
        does not solve your problem, we will say so.</p>
        <ul class="marca">
          <li><strong>We answer</strong> within the next business day.</li>
          <li><strong>We talk</strong> for half an hour to understand the operation.</li>
          <li><strong>We show you</strong> the system with your cases, not ours.</li>
        </ul>
        <figure class="figura figura--pequena">
          <img src="assets/img/escritorio-800.jpg" alt="A tablet and a phone on a desk, seen from above" width="800" height="533" loading="lazy">
        </figure>
      </div>
      <div>
${formulario()}
      </div>
    </div>
  </div>
</section>
`,
};
