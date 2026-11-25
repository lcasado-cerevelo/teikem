using Teikem.Infrastructure.Abstractions;

namespace Teikem.Infrastructure.Notifications;

// ======================================================================================================================
// Lote 243 — contenido que se arma AL ENVIAR un aviso (p. ej. la orden de venta completa en el correo). El motor de avisos
// (Lote 51) no conoce los documentos: le pide a un INotificationContentProvider registrado para el evento las variables que
// faltan, los bloques ({orderDetail}), los adjuntos y los destinatarios que salen del propio aviso (Kind CONTEXT).
// ======================================================================================================================

/// <summary>A quién va dirigido el texto: al personal de la compañía (Staff) o al cliente (Customer: sin enlaces ni marcas internas).</summary>
public enum NotificationAudience
{
    Staff,
    Customer,
}

/// <summary>Códigos de texto de <see cref="NotificationAudience"/> en el API (STAFF | CUSTOMER).</summary>
public static class NotificationAudienceCodes
{
    public const string Staff = "STAFF";
    public const string Customer = "CUSTOMER";

    public static string ToCode(NotificationAudience audience) => audience == NotificationAudience.Customer ? Customer : Staff;

    /// <summary>STAFF o CUSTOMER (sin distinguir mayúsculas); null o vacío = Staff; cualquier otro valor = null (lo valida quien llama).</summary>
    public static NotificationAudience? Parse(string? code)
    {
        var c = (code ?? string.Empty).Trim().ToUpperInvariant();
        return c switch
        {
            "" or Staff => NotificationAudience.Staff,
            Customer => NotificationAudience.Customer,
            _ => null,
        };
    }
}

/// <summary>De dónde salió la plantilla de una vista previa: la mandada en la petición, la de la regla activa o la de omisión del evento.</summary>
public static class NotificationTemplateSources
{
    public const string Request = "REQUEST";
    public const string Rule = "RULE";
    public const string Default = "DEFAULT";
}

/// <summary>
/// Una variable bloque ya armada: <paramref name="Html"/> se inserta tal cual en el correo (el proveedor es responsable de
/// codificar cada valor); <paramref name="Text"/> es la versión de una línea para la campanita y el SMS.
/// </summary>
public sealed record NotificationBlock(string Html, string Text);

/// <summary>
/// Una persona que resolvió un destinatario CONTEXT. UserId = usuario de Teikem (para la campanita y el push); null si solo
/// tiene correo o teléfono. Lang = idioma del texto ("es" | "en"); null = el de la compañía.
/// </summary>
public sealed record NotificationContextPerson(int? UserId, string? Name, string? Email, string? Phone, NotificationAudience Audience, string? Lang);

/// <summary>
/// Resultado de resolver un destinatario CONTEXT. Sin personas, <paramref name="EmptyReason"/> explica por qué (se anota en el
/// Registro de avisos como fila SKIPPED, p. ej. «La orden no tiene un vendedor con usuario activo en la compañía.»).
/// </summary>
public sealed record NotificationContextResolution(IReadOnlyList<NotificationContextPerson> People, string? EmptyReason)
{
    public static NotificationContextResolution Empty(string? reason) => new(Array.Empty<NotificationContextPerson>(), reason);
}

/// <summary>
/// Lo que el motor le pide al proveedor al enviar: la compañía (explícita: el procesador corre sin usuario), el evento, las
/// variables que trae el aviso (payload) y para quién y en qué idioma se arma el texto.
/// IncludeClientCard = false omite los datos de la ficha del cliente (dirección, teléfono, registro de comerciante): la vista previa y
/// «Enviar prueba» con un documento real lo apagan cuando quien la pide no tiene clients.read. El procesador de avisos lo deja en true.
/// </summary>
public sealed record NotificationContentRequest(
    int TenantId,
    string EventCode,
    IReadOnlyDictionary<string, string> Values,
    NotificationAudience Audience,
    string Lang,
    bool IncludeClientCard = true);

/// <summary>
/// Contenido armado al enviar. Values = variables (las del payload más las que agrega el proveedor; gana el proveedor);
/// Blocks = variables bloque por nombre; Attachments = adjuntos del correo (solo se mandan si el cuerpo usó un bloque).
/// SkipReason no nulo = no se debe mandar (p. ej. «La orden OV-00012 se canceló antes de enviar el aviso.»): cada
/// destinatario de la regla queda SKIPPED con ese motivo.
/// </summary>
public sealed record NotificationContent(
    IReadOnlyDictionary<string, string> Values,
    IReadOnlyDictionary<string, NotificationBlock> Blocks,
    IReadOnlyList<EmailAttachment> Attachments,
    string? SkipReason = null)
{
    /// <summary>Contenido que solo dice que no se manda, con su motivo.</summary>
    public static NotificationContent Skip(string reason)
        => new(new Dictionary<string, string>(), new Dictionary<string, NotificationBlock>(), Array.Empty<EmailAttachment>(), reason);
}

/// <summary>
/// Proveedor del contenido de uno o más eventos de aviso (se registra en DI como <c>INotificationContentProvider</c>).
/// <para>
/// CONTRATO — SOLO LECTURA: el proveedor NUNCA modifica ni rastrea entidades en el DbContext de quien lo llama (el procesador
/// hace SaveChanges sobre el suyo para el Registro de avisos). Cada llamada abre su PROPIO scope
/// (<c>IServiceScopeFactory</c> + <c>TenantContext.AsAnonymous(tenantId)</c>), lee con <c>AsNoTracking</c> y filtra
/// <c>TenantId == tenantId</c> además del filtro global de tenant (defensa en profundidad: el procesador corre sin usuario).
/// </para>
/// <para>
/// El motor lo resuelve de forma perezosa: solo si la plantilla usa un bloque o una variable que el aviso no trae, o si la regla
/// tiene destinatarios CONTEXT. Las reglas que no lo necesitan nunca lo llaman.
/// </para>
/// </summary>
public interface INotificationContentProvider
{
    /// <summary>Códigos de evento que atiende (p. ej. SALES_ORDER_REQUESTED).</summary>
    IReadOnlyCollection<string> EventCodes { get; }

    /// <summary>Permiso que exige ver un documento real en la vista previa o la prueba (p. ej. sales.view); null = ninguno extra.</summary>
    string? ReferencePermission { get; }

    /// <summary>Módulo que debe estar encendido para usar un documento real (p. ej. SALES_ORDERS); null = ninguno.</summary>
    string? ReferenceModule { get; }

    /// <summary>
    /// Variables base del aviso para un documento real (número de documento o PublicId), como si el evento se hubiera
    /// publicado. Lanza <c>NotFoundException</c> si no existe en la compañía.
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> ValuesForReferenceAsync(int tenantId, string eventCode, string reference, CancellationToken ct);

    /// <summary>
    /// Arma el contenido para un grupo (Audience, Lang). null = el documento ya no existe (o es de otra compañía): el aviso sale
    /// con el payload y los bloques vacíos. Una excepción = falla transitoria (el motor reintenta).
    /// </summary>
    Task<NotificationContent?> BuildAsync(NotificationContentRequest req, CancellationToken ct);

    /// <summary>Resuelve un destinatario CONTEXT (clave declarada por el evento, p. ej. SELLER o CLIENT) a personas.</summary>
    Task<NotificationContextResolution> RecipientsAsync(int tenantId, string eventCode, string contextKey, IReadOnlyDictionary<string, string> values, CancellationToken ct);

    /// <summary>Contenido de ejemplo (sin base de datos) para «Enviar prueba» y la vista previa sin documento real.</summary>
    NotificationContent Sample(string eventCode, NotificationAudience audience, string lang);
}
