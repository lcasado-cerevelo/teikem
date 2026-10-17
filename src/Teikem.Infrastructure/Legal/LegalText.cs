using Teikem.Domain.Security;

namespace Teikem.Infrastructure.Legal;

/// <summary>Datos del operador que se insertan en el documento (Legal:* en la configuración). Los valores por omisión son marcas [ENTRE CORCHETES] que el dueño debe llenar antes de producción.</summary>
public sealed class LegalOptions
{
    /// <summary>Exigir la aceptación (por omisión sí). Solo se apaga en desarrollo para correr recorridos que no son de este tema.</summary>
    public bool RequireAcceptance { get; set; } = true;
    public string OperatorName { get; set; } = "[RAZÓN SOCIAL DEL OPERADOR]";
    public string OperatorAddress { get; set; } = "[DIRECCIÓN POSTAL DEL OPERADOR]";
    public string ContactEmail { get; set; } = "[CORREO DE PRIVACIDAD]";
    public string Jurisdiction { get; set; } = "Puerto Rico";
}

public sealed record LegalSectionDto(string Id, string Part, string Heading, IReadOnlyList<string> Paragraphs);

/// <summary>El documento único: título, versión vigente, fecha y secciones (primero la política de privacidad, después los términos de uso).</summary>
public sealed record LegalDocumentDto(string Version, string Lang, string Title, string Intro, string PartPrivacy, string PartTerms, IReadOnlyList<LegalSectionDto> Sections, bool Required);

public sealed record LegalStatusDto(string CurrentVersion, bool Required, bool Accepted, DateTime? AcceptedAtUtc, string? AcceptedVersion);

public sealed record LegalAcceptRequest(string? Version, string? Lang);

/// <summary>Una fila de «quién aceptó»: AcceptedVersion es la última versión que aceptó (puede ser una anterior a la vigente).</summary>
public sealed record LegalAcceptanceRowDto(string? FullName, string? Email, bool Accepted, DateTime? AcceptedAtUtc, string? LastVersion);

/// <summary>
/// Texto del documento «Política de privacidad y términos de uso» (Lote 99), en español e inglés. BORRADOR para revisión de un abogado antes de producción:
/// los datos del operador salen de Legal:* y, al cambiar el texto, se cambia <see cref="LegalRules.CurrentVersion"/> para que todos vuelvan a aceptar.
/// </summary>
public static class LegalText
{
    public static LegalDocumentDto Build(string lang, LegalOptions o)
    {
        var en = lang == "en";
        string F(string s) => s.Replace("{op}", o.OperatorName).Replace("{addr}", o.OperatorAddress).Replace("{mail}", o.ContactEmail).Replace("{law}", o.Jurisdiction);
        var src = en ? En : Es;
        var sections = src.Select(s => new LegalSectionDto(s.Id, s.Part, s.Heading, s.Paragraphs.Select(F).ToList())).ToList();
        return en
            ? new LegalDocumentDto(LegalRules.CurrentVersion, "en", "Privacy Policy and Terms of Use", F("Teikem is a logistics platform operated by {op}. Please read this document before using it. It has two parts: how we handle personal data (Privacy Policy) and the rules for using the platform (Terms of Use)."), "Part 1 — Privacy Policy", "Part 2 — Terms of Use", sections, o.RequireAcceptance)
            : new LegalDocumentDto(LegalRules.CurrentVersion, "es", "Política de privacidad y términos de uso", F("Teikem es una plataforma de logística operada por {op}. Lea este documento antes de usarla. Tiene dos partes: cómo tratamos los datos personales (Política de privacidad) y las reglas para usar la plataforma (Términos de uso)."), "Parte 1 — Política de privacidad", "Parte 2 — Términos de uso", sections, o.RequireAcceptance);
    }

    private const string P = "privacy";
    private const string T = "terms";

    private static LegalSectionDto S(string id, string part, string heading, params string[] paragraphs) => new(id, part, heading, paragraphs);

    private static readonly LegalSectionDto[] Es =
    [
        S("p1", P, "1. Quiénes somos", "La plataforma Teikem la opera {op}, con domicilio en {addr} (en adelante, «nosotros»). Teikem sirve a compañías de transporte y logística (las «compañías»), a sus clientes (los «clientes» o remitentes) y a los destinatarios de la mercancía (los «consignatarios»). Este documento explica qué datos personales tratamos, para qué, con quién los compartimos y qué derechos tiene usted."),
        S("p2", P, "2. Qué datos recopilamos",
            "Datos de su cuenta: nombre, correo electrónico, teléfono (si lo da), usuario, contraseña (guardada solo de forma cifrada e irreversible), rol y las compañías a las que pertenece.",
            "Datos de seguridad y uso: fecha y hora de cada ingreso, dirección IP, tipo de navegador y de dispositivo, identificador de las computadoras que usted marca como de confianza, intentos fallidos, cambios de contraseña y las acciones que realiza dentro de la plataforma (bitácora de auditoría).",
            "Datos operativos que la compañía o el cliente cargan: órdenes de entrega, direcciones, nombres y teléfonos de consignatarios, contenido de las cajas a nivel de resumen, pruebas de entrega (fotos, firmas, hora y lugar), rutas, vehículos y cobros contra entrega (COD). Estos datos los introducen y controlan la compañía y el cliente, no nosotros.",
            "Sus preferencias: idioma, y, si usted es consignatario, si desea recibir avisos por correo o por mensaje de texto, junto con la fecha en que lo aceptó."),
        S("p3", P, "3. Para qué usamos los datos",
            "Para darle acceso a su cuenta y mantenerla segura (verificar quién es, detectar accesos sospechosos, evitar ataques de fuerza bruta).",
            "Para prestar el servicio: gestionar órdenes, rutas, entregas, cobros, facturas y los avisos que la compañía o el cliente configuraron.",
            "Para enviarle comunicaciones del servicio (invitaciones, confirmaciones de computadora nueva, restablecimiento de contraseña, avisos de órdenes) y, solo si usted lo aceptó, avisos por mensaje de texto.",
            "Para cumplir obligaciones legales, resolver disputas, auditar y mejorar el servicio. No vendemos sus datos ni los usamos para publicidad de terceros."),
        S("p4", P, "4. Quién es responsable de los datos",
            "Respecto a los datos de su cuenta y de seguridad, somos responsables del tratamiento. Respecto a los datos operativos (órdenes, consignatarios, entregas), la compañía y el cliente que los cargan son los responsables y nosotros los tratamos por su cuenta y siguiendo sus instrucciones. Si desea ejercer un derecho sobre datos operativos, también puede dirigirse a la compañía o al cliente que le atiende."),
        S("p5", P, "5. Con quién los compartimos",
            "Dentro de la plataforma, sus datos solo los ven las personas autorizadas de su misma compañía o cliente según sus permisos; un cliente solo ve sus propias órdenes y un consignatario solo ve lo suyo.",
            "Con proveedores que nos ayudan a operar, bajo contrato y solo para ese fin: alojamiento y base de datos, envío de correo electrónico, envío de mensajes de texto, mapas y herramientas de seguridad.",
            "Con autoridades cuando una ley u orden judicial lo exija, o para proteger derechos, seguridad y prevenir fraude.",
            "Si la empresa se fusiona o transfiere la plataforma, sus datos podrán pasar al nuevo operador, que quedará sujeto a este documento."),
        S("p6", P, "6. Avisos por correo y mensajes de texto",
            "Los avisos por correo electrónico que la compañía o el cliente configuraron se envían como parte del servicio. Los mensajes de texto (SMS) se envían solo si usted lo aceptó expresamente e indicó y confirmó su número de teléfono.",
            "Puede cambiar o retirar este consentimiento en cualquier momento desde su perfil, y cada mensaje de texto indicará cómo darse de baja (por ejemplo, respondiendo STOP). Al darse de baja dejamos de enviarle mensajes de texto. Su operador telefónico puede cobrarle por mensajes y datos.",
            "Los consignatarios eligen por sí mismos si reciben avisos; la compañía y el cliente no pueden activarlos por ellos."),
        S("p7", P, "7. Cuánto tiempo los conservamos",
            "Conservamos los datos de la cuenta mientras esté activa y, después, el tiempo necesario para cumplir obligaciones legales, contables y de auditoría. La bitácora de seguridad y auditoría se conserva el tiempo necesario para investigar incidentes y cumplir la ley. Los registros históricos de órdenes y entregas se conservan por el tiempo que la compañía los requiera; las cuentas se desactivan, no se borran, para no romper ese historial."),
        S("p8", P, "8. Cómo los protegemos",
            "Usamos conexiones cifradas, contraseñas guardadas con algoritmos de resguardo irreversible, verificación en dos pasos para cuentas con más acceso, bloqueo temporal tras intentos fallidos, confirmación por correo al entrar desde una computadora nueva, permisos por rol y separación estricta de los datos entre compañías y entre clientes, y una bitácora de auditoría. Ningún sistema es infalible: si ocurre una brecha que le afecte, se lo notificaremos y a la compañía responsable según la ley aplicable."),
        S("p9", P, "9. Sus derechos",
            "Según la ley aplicable, usted puede pedir acceso a sus datos personales, corregirlos, que se eliminen o anonimicen cuando proceda, limitar u oponerse a ciertos usos, retirar un consentimiento y obtener una copia. Para ejercerlos escriba a {mail}. Podemos pedirle que verifique su identidad. Si no está conforme con la respuesta, puede acudir a la autoridad de protección de datos que corresponda."),
        S("p10", P, "10. Menores de edad", "La plataforma es para uso profesional y no está dirigida a menores de 18 años. No recopilamos datos de menores a sabiendas; si cree que lo hicimos, avísenos."),
        S("p11", P, "11. Cambios en esta política", "Podemos actualizar este documento. Cuando el cambio sea importante, la plataforma le pedirá leerlo y aceptarlo de nuevo en su siguiente ingreso. La versión vigente y su fecha aparecen siempre al inicio."),
        S("p12", P, "12. Contacto de privacidad", "Para preguntas o solicitudes sobre sus datos: {mail}. Domicilio: {addr}."),

        S("t1", T, "13. Aceptación y quién puede usar la plataforma",
            "Al marcar «Acepto» o al usar la plataforma usted declara que leyó y acepta estos términos y la política de privacidad. Si usa la plataforma en nombre de una compañía, cliente o consignatario, declara tener autoridad para obligarlo. La plataforma es de uso profesional y solo para mayores de 18 años. Si no está de acuerdo, no debe usarla."),
        S("t2", T, "14. Su cuenta y sus credenciales",
            "Su usuario es personal e intransferible. Usted es responsable de mantener su contraseña en secreto, de no compartirla y de todo lo que se haga con su cuenta. Elija una contraseña larga y única.",
            "Por seguridad, la primera vez que entra desde una computadora o navegador nuevo le pediremos confirmar por correo electrónico, y esa computadora quedará guardada como de confianza por 30 días sin uso. No marque una computadora compartida o pública como de confianza. Usted puede ver y quitar sus computadoras de confianza.",
            "Avísenos de inmediato si sospecha que alguien usó su cuenta. Podemos bloquear temporalmente una cuenta tras varios intentos fallidos para protegerla."),
        S("t3", T, "15. Uso aceptable",
            "Usted se compromete a usar la plataforma de forma lícita y a no: (a) intentar acceder a datos o cuentas que no le corresponden; (b) probar contraseñas, vulnerar o evadir medidas de seguridad, ni hacer pruebas de intrusión sin autorización escrita; (c) sobrecargar el servicio o usarlo con programas automáticos no autorizados; (d) cargar contenido ilícito, difamatorio o que viole derechos de terceros, ni códigos maliciosos; (e) usar los datos de consignatarios o de otras personas para fines distintos de la operación de entregas; (f) hacerse pasar por otra persona."),
        S("t4", T, "16. Datos y contenido que usted carga",
            "Los datos que usted o su organización cargan siguen siendo suyos (o de su organización). Nos otorga el permiso limitado necesario para almacenarlos y tratarlos con el fin de prestar el servicio.",
            "Usted garantiza que tiene derecho a cargar esos datos, en particular los nombres, teléfonos, direcciones y demás datos de consignatarios y terceros, y que ha cumplido con informarles y obtener los consentimientos que la ley exija. Es responsable de la exactitud de lo que carga."),
        S("t5", T, "17. Órdenes creadas por consignatarios",
            "Cuando el cliente lo permite, un consignatario puede solicitar órdenes de entrega desde su acceso. Esa orden nace como una solicitud y no se ejecuta hasta que el cliente la confirme. El consignatario responde por la veracidad de lo solicitado; el cliente decide si la confirma y responde por las órdenes que confirma."),
        S("t6", T, "18. Disponibilidad y cambios del servicio", "Procuramos que la plataforma esté disponible de forma continua, pero no garantizamos que no habrá interrupciones, errores o mantenimientos. Podemos modificar, añadir o retirar funciones. Los avisos por correo o mensaje de texto dependen de terceros y pueden retrasarse o no llegar."),
        S("t7", T, "19. Propiedad intelectual", "La plataforma, su código, diseño, marcas y documentación son de {op} o de sus licenciantes. Se le concede un derecho limitado, personal, revocable y no exclusivo de uso mientras tenga una cuenta activa. No puede copiar, revender, descompilar ni crear obras derivadas de la plataforma."),
        S("t8", T, "20. Suspensión y terminación", "Podemos suspender o cerrar una cuenta que incumpla estos términos, ponga en riesgo la seguridad o cuando la compañía u organización a la que pertenece lo solicite o termine su relación con nosotros. Usted puede dejar de usar la plataforma en cualquier momento; sus datos se tratarán según la política de privacidad."),
        S("t9", T, "21. Garantías y limitación de responsabilidad",
            "La plataforma se ofrece «tal cual» y «según disponibilidad», sin garantías de ningún tipo en la medida que la ley lo permita, incluidas las de comerciabilidad e idoneidad para un fin particular.",
            "En la medida permitida por la ley, {op} no responde por daños indirectos, lucro cesante, pérdida de datos o de negocio, ni por la pérdida, retraso o daño de mercancía, que son responsabilidad de la compañía de transporte que presta el servicio logístico. Nuestra responsabilidad total frente a usted por cualquier reclamación se limita al monto que haya pagado por el uso de la plataforma en los 12 meses anteriores, o a USD 100 si no pagó nada. Nada de esto limita responsabilidades que la ley no permita limitar."),
        S("t10", T, "22. Ley aplicable y controversias", "Estos términos se rigen por las leyes de {law}, sin tener en cuenta sus normas sobre conflicto de leyes. Las controversias se someterán a los tribunales competentes de {law}, salvo que la ley aplicable disponga otra cosa a favor del consumidor."),
        S("t11", T, "23. Cambios y contacto", "Podemos modificar estos términos; el cambio importante se le pedirá aceptar de nuevo. Si alguna parte resultara inválida, el resto seguirá vigente. Preguntas: {mail}."),
    ];

    private static readonly LegalSectionDto[] En =
    [
        S("p1", P, "1. Who we are", "The Teikem platform is operated by {op}, located at {addr} (\"we\"). Teikem serves transportation and logistics companies (the \"companies\"), their customers (\"clients\" or senders) and the recipients of goods (\"consignees\"). This document explains what personal data we process, why, who we share it with and what rights you have."),
        S("p2", P, "2. What data we collect",
            "Account data: name, email address, phone (if provided), username, password (stored only in encrypted, irreversible form), role and the companies you belong to.",
            "Security and usage data: date and time of each sign-in, IP address, browser and device type, identifier of the computers you mark as trusted, failed attempts, password changes and the actions you take in the platform (audit log).",
            "Operational data uploaded by the company or client: delivery orders, addresses, consignee names and phone numbers, summarized package contents, proof of delivery (photos, signatures, time and place), routes, vehicles and cash on delivery (COD). The company and the client enter and control this data, not us.",
            "Your preferences: language and, if you are a consignee, whether you want notices by email or text message, together with the date you accepted."),
        S("p3", P, "3. How we use the data",
            "To give you access to your account and keep it secure (verify who you are, detect suspicious access, prevent brute-force attacks).",
            "To provide the service: manage orders, routes, deliveries, collections, invoices and the notices the company or client configured.",
            "To send you service communications (invitations, new-computer confirmations, password resets, order notices) and, only if you opted in, text message notices.",
            "To comply with legal obligations, resolve disputes, audit and improve the service. We do not sell your data or use it for third-party advertising."),
        S("p4", P, "4. Who is responsible for the data",
            "For your account and security data we are the controller. For operational data (orders, consignees, deliveries) the company and the client that upload it are responsible, and we process it on their behalf and under their instructions. To exercise a right over operational data you may also contact the company or client that serves you."),
        S("p5", P, "5. Who we share it with",
            "Inside the platform, your data is visible only to authorized people of your own company or client according to their permissions; a client sees only its own orders and a consignee sees only its own.",
            "With providers that help us operate, under contract and only for that purpose: hosting and database, email delivery, text message delivery, maps and security tools.",
            "With authorities when required by law or court order, or to protect rights and safety and prevent fraud.",
            "If the business merges or transfers the platform, your data may pass to the new operator, which will be bound by this document."),
        S("p6", P, "6. Email and text message notices",
            "Email notices that the company or client configured are sent as part of the service. Text messages (SMS) are sent only if you expressly agreed and provided and confirmed your phone number.",
            "You can change or withdraw this consent at any time from your profile, and every text message will say how to opt out (for example, replying STOP). Once you opt out we stop sending you text messages. Your carrier may charge for messages and data.",
            "Consignees choose for themselves whether to receive notices; the company and the client cannot turn them on for you."),
        S("p7", P, "7. How long we keep it",
            "We keep account data while the account is active and afterwards for as long as needed to meet legal, accounting and audit obligations. The security and audit log is kept as long as needed to investigate incidents and comply with the law. Historical order and delivery records are kept as long as the company requires them; accounts are deactivated, not deleted, so that history is not broken."),
        S("p8", P, "8. How we protect it",
            "We use encrypted connections, passwords stored with irreversible hashing, two-step verification for accounts with broader access, temporary lockout after failed attempts, email confirmation when signing in from a new computer, role-based permissions, strict separation of data between companies and between clients, and an audit log. No system is infallible: if a breach affecting you occurs, we will notify you and the responsible company as required by applicable law."),
        S("p9", P, "9. Your rights",
            "Under applicable law you may request access to your personal data, correct it, have it deleted or anonymized where appropriate, restrict or object to certain uses, withdraw a consent and obtain a copy. To exercise them write to {mail}. We may ask you to verify your identity. If you are not satisfied with our answer you may contact the competent data protection authority."),
        S("p10", P, "10. Minors", "The platform is for professional use and is not directed at people under 18. We do not knowingly collect data from minors; if you believe we did, let us know."),
        S("p11", P, "11. Changes to this policy", "We may update this document. When a change is significant, the platform will ask you to read and accept it again on your next sign-in. The current version and its date always appear at the top."),
        S("p12", P, "12. Privacy contact", "For questions or requests about your data: {mail}. Address: {addr}."),

        S("t1", T, "13. Acceptance and who may use the platform",
            "By checking \"I accept\" or by using the platform you state that you have read and accept these terms and the privacy policy. If you use the platform on behalf of a company, client or consignee, you state that you have authority to bind it. The platform is for professional use and only for people over 18. If you do not agree, you must not use it."),
        S("t2", T, "14. Your account and credentials",
            "Your username is personal and non-transferable. You are responsible for keeping your password secret, not sharing it and everything done with your account. Choose a long, unique password.",
            "For security, the first time you sign in from a new computer or browser we will ask you to confirm by email, and that computer will be saved as trusted for 30 days without use. Do not mark a shared or public computer as trusted. You can see and remove your trusted computers.",
            "Tell us immediately if you suspect someone used your account. We may temporarily lock an account after several failed attempts to protect it."),
        S("t3", T, "15. Acceptable use",
            "You agree to use the platform lawfully and not to: (a) try to access data or accounts that are not yours; (b) guess passwords, breach or bypass security measures, or run intrusion tests without written authorization; (c) overload the service or use it with unauthorized automated programs; (d) upload unlawful or defamatory content, content that violates third-party rights, or malicious code; (e) use consignee or other people's data for purposes other than running deliveries; (f) impersonate another person."),
        S("t4", T, "16. Data and content you upload",
            "The data you or your organization upload remains yours (or your organization's). You grant us the limited permission needed to store and process it to provide the service.",
            "You warrant that you have the right to upload that data, in particular the names, phone numbers, addresses and other data of consignees and third parties, and that you have informed them and obtained the consents the law requires. You are responsible for the accuracy of what you upload."),
        S("t5", T, "17. Orders created by consignees",
            "When the client allows it, a consignee may request delivery orders from its access. That order starts as a request and is not carried out until the client confirms it. The consignee is responsible for the truthfulness of the request; the client decides whether to confirm it and is responsible for the orders it confirms."),
        S("t6", T, "18. Availability and changes to the service", "We aim to keep the platform continuously available but do not guarantee there will be no interruptions, errors or maintenance. We may modify, add or remove features. Email and text message notices depend on third parties and may be delayed or not delivered."),
        S("t7", T, "19. Intellectual property", "The platform, its code, design, trademarks and documentation belong to {op} or its licensors. You are granted a limited, personal, revocable, non-exclusive right to use it while you have an active account. You may not copy, resell, decompile or create derivative works of the platform."),
        S("t8", T, "20. Suspension and termination", "We may suspend or close an account that breaches these terms, endangers security, or when the company or organization it belongs to asks us to or ends its relationship with us. You may stop using the platform at any time; your data will be handled under the privacy policy."),
        S("t9", T, "21. Warranties and limitation of liability",
            "The platform is provided \"as is\" and \"as available\", without warranties of any kind to the extent the law allows, including merchantability and fitness for a particular purpose.",
            "To the extent permitted by law, {op} is not liable for indirect damages, lost profits, loss of data or business, or for loss, delay or damage to goods, which are the responsibility of the transportation company that provides the logistics service. Our total liability to you for any claim is limited to the amount you paid for using the platform in the previous 12 months, or USD 100 if you paid nothing. Nothing here limits liability that the law does not allow to be limited."),
        S("t10", T, "22. Governing law and disputes", "These terms are governed by the laws of {law}, without regard to its conflict-of-law rules. Disputes will be submitted to the competent courts of {law}, unless applicable law provides otherwise in favor of the consumer."),
        S("t11", T, "23. Changes and contact", "We may modify these terms; a significant change will require you to accept again. If any part is found invalid, the rest remains in force. Questions: {mail}."),
    ];
}
