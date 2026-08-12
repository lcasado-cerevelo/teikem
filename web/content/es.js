'use strict';

/* Textos del landing en español. El inglés vive en `en.js` con la misma
   estructura: si aquí se añade una sección, allí también, o el build avisa. */

module.exports = {
  title: 'Teikem — Inteligencia de Entregas · plataforma de operación logística',
  description: 'Teikem cubre la operación logística completa: órdenes, rutas, flota, almacén, cross-docking, app de conductor sin conexión, prueba de entrega, cobro contra entrega, facturación y liquidación. Trece módulos, una sola plataforma.',
  lema: 'Inteligencia de Entregas',
  saltar: 'Saltar al contenido',
  menu: 'Menú',
  menuAria: 'Principal',
  otroIdioma: 'English',
  cta: 'Pedir demostración',

  nav: [
    ['operacion', 'La operación'],
    ['plataforma', 'La plataforma'],
    ['modulos', 'Módulos'],
    ['diferencia', 'Por qué Teikem'],
    ['teki', 'Teki'],
  ],

  form: {
    nombre: 'Nombre',
    empresa: 'Empresa',
    correo: 'Correo electrónico',
    telefono: 'Teléfono',
    flota: 'Tamaño de la flota',
    entregas: 'Entregas al día, más o menos',
    entregasPista: 'p. ej. 300',
    mensaje: '¿Qué le duele hoy?',
    mensajePista: 'Qué usa ahora, qué se le escapa y para cuándo lo necesita.',
    enviar: 'Pedir demostración',
    nota: 'Le contestamos dentro del próximo día laborable. Si prefiere escribir directamente:',
    trampa: 'No llene este campo',
    temas: ['1 a 5 vehículos', '6 a 20 vehículos', '21 a 50 vehículos', 'Más de 50 vehículos', 'Todavía no tengo flota propia'],
  },

  pie: {
    resumen: 'Plataforma de operación logística para transportistas, distribuidores y operadores de última milla. Trece módulos, web y móvil, con la app del conductor funcionando sin señal.',
    creditos: 'Fotografías de Unsplash: Ashley, Blake Wisz, Claudio Schwarz, Luke Heibert, Maarten van den Heuvel, Maksym Tymchyk.',
    derechos: 'Todos los derechos reservados.',
    hecho: 'Hecho en Puerto Rico.',
  },

  cuerpo: ({ icono, formulario }) => `
<section class="portada">
  <div class="wrap">
    <div class="portada-texto">
      <span class="antetitulo">Operación logística, de punta a punta</span>
      <h1>Del pedido a la puerta, <em>y del dinero de vuelta</em></h1>
      <p class="entradilla">Un operador de logística no hace una sola cosa: recoge, almacena,
      consolida, transporta, entrega, cobra en la puerta y factura. Teikem cubre las siete,
      en la misma plataforma, sin que el pegamento sean hojas de cálculo y llamadas.</p>
      <div class="acciones">
        <a class="boton boton--grande" href="#demo">Pedir demostración</a>
        <a class="boton-linea" href="#operacion">Ver cómo funciona</a>
      </div>
      <p class="letra-chica">Sin compromiso. Una llamada corta para entender su operación
      antes de enseñarle nada.</p>
    </div>
    <div class="portada-pantalla">
      <img src="assets/img/pantalla-pulso-1600.png" alt="Pantalla de Teikem: el pulso del día con los paquetes en la calle, el dinero COD de regreso y los indicadores de la operación" width="1600" height="1165" fetchpriority="high">
    </div>
  </div>
</section>

<section class="cinta" aria-hidden="true">
  <div class="wrap">
    <span>Órdenes</span><span>Rutas</span><span>Flota</span><span>Almacén</span>
    <span>Cross-docking</span><span>Inventario</span><span>App del conductor</span>
    <span>Prueba de entrega</span><span>Cobro en la puerta</span><span>Facturación</span>
    <span>Liquidación</span><span>Portal del cliente</span><span>Integraciones</span>
  </div>
</section>

<section class="seccion" id="operacion">
  <div class="wrap">
    <div class="cabeza">
      <span class="antetitulo">La operación</span>
      <h2>El día, paso a paso</h2>
      <p>Lo que en la mayoría de las empresas está repartido entre cuatro programas y una
      libreta, aquí es una sola línea que se puede mirar en cualquier momento.</p>
    </div>

    <ol class="flujo">
      <li>
        <span class="paso">01</span>
        <h3>Entra la orden</h3>
        <p>Del cliente por su portal, de una llamada o de otro sistema por el API. Con su
        contrato y su tarifa ya aplicadas.</p>
      </li>
      <li>
        <span class="paso">02</span>
        <h3>Se recibe y se ubica</h3>
        <p>Escaneo de entrada, ubicación en almacén o paso directo de muelle a muelle si es
        cross-docking. La mercancía nunca está «por ahí».</p>
      </li>
      <li>
        <span class="paso">03</span>
        <h3>Se despacha</h3>
        <p>Asignación a chofer y vehículo, ordenamiento de paradas y la ruta al teléfono.
        Con las excepciones del día real: el cliente que no estaba, la dirección que no existe.</p>
      </li>
      <li>
        <span class="paso">04</span>
        <h3>Se entrega</h3>
        <p>Firma, fotos y GPS en el sitio. Entrega parcial línea por línea y razón tipificada
        cuando algo falla, que es la mitad de las veces.</p>
      </li>
      <li>
        <span class="paso">05</span>
        <h3>Se cobra y se cuadra</h3>
        <p>El efectivo cobrado en la puerta queda abierto en la liquidación del chofer hasta
        que se remesa y el total cuadra. Después, facturación.</p>
      </li>
    </ol>
  </div>
</section>

<section class="banda">
  <img src="assets/img/mensajero-1600.jpg" alt="" loading="lazy">
  <div class="wrap">
    <h2>Todo el mundo mirando el mismo número</h2>
    <p>El despachador, el de almacén, contabilidad y el dueño dejan de discutir cifras
    distintas: hay una sola operación y una sola versión de lo que pasó.</p>
  </div>
</section>

<section class="seccion seccion--alt" id="plataforma">
  <div class="wrap">
    <div class="dos">
      <div>
        <span class="antetitulo">La plataforma</span>
        <h2>El dinero de regreso también es parte de la entrega</h2>
        <p>En cobro contra entrega, el problema no es marcar «entregado»: es que el efectivo
        que recogió el chofer aparezca completo al final del día. Teikem lo persigue: lo que
        se cobró en la calle queda abierto en la liquidación de quien lo cobró hasta que se
        remesa y cuadra contra lo que registró el sistema.</p>
        <p>Lo mismo con la evidencia. La prueba de entrega no se archiva para que nadie la
        mire: se archiva para el día que el cliente reclame que no le llegó.</p>
        <ul class="marca">
          <li><strong>Ciclo COD completo</strong>, del cobro en la puerta a la remesa.</li>
          <li><strong>Liquidación por chofer y por ruta</strong>, con lo que falta a la vista.</li>
          <li><strong>Prueba de entrega archivada</strong> y disponible en segundos.</li>
          <li><strong>Facturación</strong> conectada, sin volver a teclear lo mismo.</li>
        </ul>
      </div>
      <div>
        <img class="pantalla" src="assets/img/pantalla-dia-1600.png" alt="Pantalla de Teikem para procesar entregas: por cobrar, cobrado hoy, en cuadre y remitido" width="1600" height="722" loading="lazy">
      </div>
    </div>
  </div>
</section>

<section class="seccion" id="modulos">
  <div class="wrap">
    <div class="cabeza">
      <span class="antetitulo">Módulos</span>
      <h2>Trece módulos, y usted enciende los que usa</h2>
      <p>No hay dos operaciones iguales: una cobra por recogido y otra por entrega, una hace
      cross-docking y otra almacena. Cada empresa enciende lo suyo y apaga el resto — sin
      versiones a medida ni pantallas que sobran.</p>
    </div>
    <div class="rejilla">
      <div class="tarjeta">${icono('orden')}<h3>Clientes y contratos</h3><p>Tarifas, condiciones y acuerdos de servicio por cliente.</p></div>
      <div class="tarjeta">${icono('caja')}<h3>Órdenes de transporte</h3><p>Del pedido a la entrega, con su estado en todo momento.</p></div>
      <div class="tarjeta">${icono('ruta')}<h3>Viajes y rutas</h3><p>Planificación, ordenamiento de paradas y seguimiento en vivo.</p></div>
      <div class="tarjeta">${icono('camion')}<h3>Flota y choferes</h3><p>Quién conduce qué, con qué vigencias y qué mantenimiento le toca.</p></div>
      <div class="tarjeta">${icono('almacen')}<h3>Almacenes</h3><p>Ubicaciones, movimientos, recolección y kárdex.</p></div>
      <div class="tarjeta">${icono('cruce')}<h3>Cross-docking</h3><p>Consolidación de carga que solo pasa de muelle a muelle.</p></div>
      <div class="tarjeta">${icono('lote')}<h3>Inventario y trazabilidad</h3><p>Por lote y serie, con la genealogía de qué salió de qué.</p></div>
      <div class="tarjeta">${icono('movil')}<h3>App del conductor</h3><p>La ruta en el teléfono, funcionando sin señal.</p></div>
      <div class="tarjeta">${icono('firma')}<h3>Prueba de entrega</h3><p>Firma, fotos, GPS y entrega parcial línea por línea.</p></div>
      <div class="tarjeta">${icono('portal')}<h3>Portal de clientes</h3><p>Su cliente ve sus envíos y su evidencia sin llamar a nadie.</p></div>
      <div class="tarjeta">${icono('dinero')}<h3>Facturación y liquidación</h3><p>Cobro contra entrega, remesa y cierre por chofer.</p></div>
      <div class="tarjeta">${icono('compras')}<h3>Compras y equipos</h3><p>Suplidores, órdenes de compra y equipo en alquiler.</p></div>
      <div class="tarjeta">${icono('grafico')}<h3>Tableros e indicadores</h3><p>Los suyos: campos, informes y gráficos propios, sin programar.</p></div>
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
      <span class="antetitulo">Por qué Teikem</span>
      <h2>Lo que separa un sistema de logística de uno que aguanta la calle</h2>
    </div>
    <div class="rejilla rejilla--3">
      <div class="tarjeta">${icono('sinsenal')}<h3>La calle no tiene internet</h3>
        <p>Muelles de carga, sótanos y carreteras sin cobertura. La ruta se descarga entera y
        lo que pasa afuera se guarda en el teléfono y espera. Un sistema que exija conexión
        acaba sustituido por una libreta.</p></div>
      <div class="tarjeta">${icono('llave')}<h3>Reintentar sin duplicar</h3>
        <p>Cuando vuelve la señal, la cola se vacía en orden. Cada operación viaja con su
        clave de idempotencia, así que un reintento no cobra dos veces el mismo envío.</p></div>
      <div class="tarjeta">${icono('mapa')}<h3>Mapas abiertos</h3>
        <p>OpenStreetMap en vez de un servicio por consumo: su factura no crece con cada
        parada que planifica.</p></div>
      <div class="tarjeta">${icono('interruptores')}<h3>El flujo es dato, no código</h3>
        <p>Usted enciende las etapas que usa y define desde cuál se puede cancelar. Cambiar
        cómo trabaja no debería ser un proyecto de programación.</p></div>
      <div class="tarjeta">${icono('escudo')}<h3>Permisos, doble factor y auditoría</h3>
        <p>Hay dinero de por medio. Permisos por rol, segundo factor y auditoría en tres
        planos: qué cambió, quién lo cambió y qué pasó en seguridad.</p></div>
      <div class="tarjeta">${icono('enchufe')}<h3>Se conecta con lo que ya tiene</h3>
        <p>API para integrar con contabilidad, comercio electrónico o el sistema del cliente.
        Nadie va a botar su contabilidad porque llegamos nosotros.</p></div>
    </div>
  </div>
</section>

<section class="seccion seccion--alt" id="para-quien">
  <div class="wrap">
    <div class="dos dos--invertido">
      <figure class="figura">
        <img src="assets/img/ultima-milla-800.jpg" alt="Repartidor caminando por la acera con paquetes bajo el brazo" width="800" height="533" loading="lazy">
      </figure>
      <div>
        <span class="antetitulo">Para quién es</span>
        <h2>Si su operación toca la calle, es para usted</h2>
        <ul class="marca">
          <li><strong>Transportistas y couriers</strong> que despachan rutas todos los días y
          liquidan choferes todas las semanas.</li>
          <li><strong>Distribuidores</strong> que almacenan, preparan pedidos y entregan con
          flota propia.</li>
          <li><strong>Operadores de última milla</strong> con cobro contra entrega y prueba
          de entrega que el cliente exige.</li>
          <li><strong>Operadores logísticos</strong> que trabajan para varias empresas y
          necesitan los datos de cada una separados de verdad.</li>
        </ul>
        <p>Y si ya tiene un sistema que cubre una parte, Teikem se puede conectar con él en
        vez de pedirle que lo bote.</p>
      </div>
    </div>
  </div>
</section>

<section class="seccion" id="teki">
  <div class="wrap">
    <div class="dos">
      <div>
        <span class="antetitulo">Inteligencia artificial</span>
        <h2>Teki mira la operación cuando usted no puede</h2>
        <p>Un tablero enseña números; hay que saber leerlos y hay que acordarse de
        mirarlos. Teki es el asistente de Teikem y hace lo contrario: le dice en una
        frase qué necesita su decisión hoy, y trabaja con los datos de su operación —no
        con generalidades de internet.</p>
        <ul class="marca">
          <li><strong>Le resume el día.</strong> Qué está atascado, qué vence en una hora
          y qué dinero falta por cuadrar, sin que usted tenga que buscarlo.</li>
          <li><strong>Redacta lo repetitivo.</strong> El correo al cliente cuando una
          entrega falla, con la razón y la evidencia ya puestas. Usted lo lee y lo manda.</li>
          <li><strong>Clasifica lo que llega.</strong> Razones de no entrega, incidencias
          y notas del chofer, ordenadas solas para que el informe del mes salga del dato
          y no de la memoria.</li>
          <li><strong>Contesta preguntas del sistema</strong> en lenguaje normal, sin que
          nadie tenga que armar un informe para averiguar una cifra.</li>
        </ul>

        <div class="reglas">
          <p><strong>Dos reglas que no se negocian.</strong> La decisión final es de una
          persona: Teki propone, nunca ejecuta por su cuenta. Y su información no sale a
          pasear: son los datos de su empresa, no material de entrenamiento de nadie.</p>
        </div>
      </div>

      <div class="charla" aria-label="Ejemplo de conversación con Teki">
        <div class="charla-cabeza">${icono('teki')}<div><b>Teki</b><span>Asistente de Teikem</span></div></div>
        <p class="dice-usted">¿Cuánto COD tengo cobrado y sin cuadrar?</p>
        <p class="dice-teki"><b>$2,230</b> de 14 órdenes, todas de ayer y todas del mismo
        chofer. Está escaneado pero no remesado.<br><span>Abrir la liquidación →</span></p>
        <p class="dice-usted">¿Y qué tengo que decidir hoy?</p>
        <p class="dice-teki">Tres cosas: <b>83 órdenes</b> esperan chofer, <b>3 entregas</b>
        vencen en menos de una hora y <b>7 documentos</b> de flota vencen esta semana.</p>
      </div>
    </div>
  </div>
</section>

<section class="seccion seccion--alt" id="demo">
  <div class="wrap">
    <div class="dos">
      <div>
        <span class="antetitulo">Hablemos</span>
        <h2>Una demostración con su operación en la cabeza</h2>
        <p>No es una presentación grabada. Antes de enseñarle nada preferimos entender cómo
        despacha, cómo cobra y dónde se le va el tiempo — y si Teikem no le resuelve el
        problema, se lo decimos.</p>
        <ul class="marca">
          <li><strong>Contestamos</strong> dentro del próximo día laborable.</li>
          <li><strong>Conversamos</strong> media hora para entender la operación.</li>
          <li><strong>Le enseñamos</strong> el sistema con sus casos, no con los nuestros.</li>
        </ul>
        <figure class="figura figura--pequena">
          <img src="assets/img/escritorio-800.jpg" alt="Tableta y teléfono sobre un escritorio, vistos desde arriba" width="800" height="533" loading="lazy">
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
