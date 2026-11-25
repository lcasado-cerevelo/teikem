using System.Text.RegularExpressions;
using Teikem.Domain.Notifications;

namespace Teikem.Infrastructure.Notifications;

/// <summary>
/// Una variable que un evento ofrece a las plantillas: {nombre}. Sample = valor de ejemplo para «Enviar prueba».
/// IsBlock (Lote 243) = variable bloque (p. ej. {orderDetail}, la orden completa en tabla): la arma el proveedor de contenido al
/// enviar, en el correo sale como HTML y en la campanita y el SMS como una línea; nunca puede ir en el asunto.
/// </summary>
public sealed record NotificationVariable(string Name, string LabelEs, string LabelEn, string Sample, bool IsBlock = false);

/// <summary>
/// Lote 243 — un destinatario que sale del propio aviso (Kind CONTEXT, Value = <paramref name="Key"/>), p. ej. SELLER «Vendedor de
/// la orden». Key en MAYÚSCULAS con guion bajo (máximo 40). IsDefault = se agrega solo a la regla nueva. Audience = Customer
/// recibe la versión para el cliente (sin enlaces ni marcas internas).
/// </summary>
public sealed record NotificationContextRecipientDef(string Key, string LabelEs, string LabelEn, bool IsDefault, NotificationAudience Audience = NotificationAudience.Staff);

/// <summary>Lote 243 — plantilla por omisión de un canal (p. ej. el correo con {orderDetail}); los canales sin ella usan los textos generales del evento.</summary>
public sealed record NotificationChannelDefault(string Channel, string SubjectEs, string SubjectEn, string BodyEs, string BodyEn);

/// <summary>
/// Lote 51 — un evento de aviso definido en código. Cada módulo registra los suyos con un <see cref="INotificationEventProvider"/>.
/// IsOptional = el usuario puede silenciarlo en su bandeja. LinkTemplate = ruta de la web con {variables} (p. ej. «/orders/{orderNumber}»).
/// Los textos por omisión rellenan la regla nueva (el administrador los puede cambiar).
/// Lote 243: ContextRecipients = destinatarios que salen del aviso (Kind CONTEXT); ChannelDefaults = plantilla por omisión por canal.
/// </summary>
public sealed record NotificationEventDef(
    string Code,
    string? ModuleKey,
    string NameEs,
    string NameEn,
    IReadOnlyList<NotificationVariable> Variables,
    bool IsOptional,
    string? LinkTemplate,
    string DefaultSubjectEs,
    string DefaultSubjectEn,
    string DefaultBodyEs,
    string DefaultBodyEn,
    IReadOnlyList<NotificationContextRecipientDef>? ContextRecipients = null,
    IReadOnlyList<NotificationChannelDefault>? ChannelDefaults = null)
{
    /// <summary>La plantilla por omisión del canal, o null si el evento no la declara.</summary>
    public NotificationChannelDefault? ChannelDefaultFor(string? channel)
        => ChannelDefaults?.FirstOrDefault(d => string.Equals(d.Channel, channel?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>El destinatario CONTEXT con esa clave (sin distinguir mayúsculas), o null si el evento no lo tiene.</summary>
    public NotificationContextRecipientDef? ContextRecipient(string? key)
        => ContextRecipients?.FirstOrDefault(r => string.Equals(r.Key, key?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Nombres de las variables bloque del evento.</summary>
    public IReadOnlySet<string> BlockNames => Variables.Where(v => v.IsBlock).Select(v => v.Name).ToHashSet(StringComparer.Ordinal);
}

/// <summary>
/// Lote 243 — revisión de una definición de evento al construir el catálogo. Lo que rechaza es un error de PROGRAMACIÓN (el
/// API no arranca), no de usuario: clave CONTEXT mal formada o repetida, canal desconocido o repetido en las plantillas por
/// omisión, una variable bloque en el asunto de una plantilla por omisión o una variable que el evento no declara.
/// </summary>
public static partial class NotificationEventDefRules
{
    public const int MaxContextKeyLength = 40;

    [GeneratedRegex(@"^[A-Z][A-Z0-9_]*\z")]
    private static partial Regex ContextKeyRegex();

    public static bool IsValidContextKey(string? key)
        => !string.IsNullOrEmpty(key) && key.Length <= MaxContextKeyLength && ContextKeyRegex().IsMatch(key);

    public static void Validate(NotificationEventDef e)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in e.ContextRecipients ?? Array.Empty<NotificationContextRecipientDef>())
        {
            if (!IsValidContextKey(r.Key))
                throw new InvalidOperationException($"El evento de aviso '{e.Code}' tiene un destinatario del evento con clave no válida: '{r.Key}' (MAYÚSCULAS con guion bajo, máximo {MaxContextKeyLength}).");
            if (!keys.Add(r.Key))
                throw new InvalidOperationException($"El evento de aviso '{e.Code}' tiene el destinatario del evento '{r.Key}' repetido.");
        }

        var blocks = e.BlockNames;
        var declared = e.Variables.Select(v => v.Name).ToHashSet(StringComparer.Ordinal);
        var channels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in e.ChannelDefaults ?? Array.Empty<NotificationChannelDefault>())
        {
            if (!NotificationChannels.All.Contains(d.Channel))
                throw new InvalidOperationException($"El evento de aviso '{e.Code}' tiene una plantilla por omisión del canal desconocido '{d.Channel}'.");
            if (!channels.Add(d.Channel))
                throw new InvalidOperationException($"El evento de aviso '{e.Code}' tiene la plantilla por omisión del canal {d.Channel} repetida.");
            foreach (var name in NotificationRules.VariablesOf(d.SubjectEs).Concat(NotificationRules.VariablesOf(d.SubjectEn)))
                if (blocks.Contains(name))
                    throw new InvalidOperationException($"El evento de aviso '{e.Code}' usa la variable bloque {{{name}}} en el asunto de la plantilla por omisión del canal {d.Channel}.");
            foreach (var name in new[] { d.SubjectEs, d.SubjectEn, d.BodyEs, d.BodyEn }.SelectMany(NotificationRules.VariablesOf))
                if (!declared.Contains(name))
                    throw new InvalidOperationException($"El evento de aviso '{e.Code}' usa en la plantilla por omisión del canal {d.Channel} la variable {{{name}}}, que no declara.");
        }
    }
}

/// <summary>
/// Lote 197 — la categoría con que la pantalla de reglas agrupa los eventos (la lista desplegable): WAREHOUSE (almacén), SALES (ventas), TRANSPORT (transporte: flota, choferes), MAINTENANCE (mantenimiento de
/// vehículos), BILLING (facturación), INTEGRATIONS y OTHER. Se deduce del código del evento en un solo lugar; un evento nuevo cae en OTHER hasta que se le dé su prefijo aquí.
/// </summary>
public static class NotificationEventCategories
{
    public const string Warehouse = "WAREHOUSE";
    public const string Sales = "SALES";
    public const string Transport = "TRANSPORT";
    public const string Maintenance = "MAINTENANCE";
    public const string Billing = "BILLING";
    public const string Integrations = "INTEGRATIONS";
    public const string Other = "OTHER";

    /// <summary>Orden en que se muestran los grupos.</summary>
    public static readonly string[] Order = { Warehouse, Sales, Transport, Maintenance, Billing, Integrations, Other };

    public static string Of(string? code)
    {
        var c = (code ?? string.Empty).ToUpperInvariant();
        if (c.StartsWith("FLEET_MAINTENANCE_") || c.StartsWith("FLEET_WORKORDER_")) return Maintenance;
        if (c.StartsWith("FLEET_")) return Transport;
        if (c.StartsWith("ASN_") || c.StartsWith("EXIT_NOTICE_") || c.StartsWith("INVENTORY_")) return Warehouse;
        if (c.StartsWith("SALES_")) return Sales;
        if (c.StartsWith("BILLING_") || c.StartsWith("INVOICE_")) return Billing;
        if (c.StartsWith("INTEGRATION_")) return Integrations;
        return Other;
    }
}

public interface INotificationEventProvider
{
    IEnumerable<NotificationEventDef> Events { get; }
}

/// <summary>Catálogo de eventos: la unión de todos los proveedores registrados. Un código repetido (o una definición que no pasa
/// <see cref="NotificationEventDefRules"/>) es un error de programación.</summary>
public sealed class NotificationEventCatalog
{
    private readonly Dictionary<string, NotificationEventDef> _events;

    public NotificationEventCatalog(IEnumerable<INotificationEventProvider> providers)
    {
        _events = new Dictionary<string, NotificationEventDef>(StringComparer.Ordinal);
        foreach (var e in providers.SelectMany(p => p.Events))
        {
            if (!_events.TryAdd(e.Code, e)) throw new InvalidOperationException($"El evento de aviso '{e.Code}' está definido dos veces.");
            NotificationEventDefRules.Validate(e);   // Lote 243: destinatarios del evento y plantillas por omisión
        }
    }

    public IReadOnlyCollection<NotificationEventDef> All => _events.Values;
    public NotificationEventDef? Find(string? code) => code is not null && _events.TryGetValue(code.Trim().ToUpperInvariant(), out var e) ? e : null;
}

/// <summary>El evento de prueba (TEST): permite armar una regla y dispararla desde la pantalla sin esperar a un evento real.</summary>
public sealed class TestEventProvider : INotificationEventProvider
{
    public IEnumerable<NotificationEventDef> Events { get; } = new[]
    {
        new NotificationEventDef(
            NotificationCodes.TestEvent, null, "Aviso de prueba", "Test notification",
            new[]
            {
                new NotificationVariable("message", "Mensaje", "Message", "Esto es una prueba."),
                new NotificationVariable("user", "Quién lo disparó", "Who triggered it", "Administrador"),
            },
            IsOptional: true, LinkTemplate: null,
            "Aviso de prueba de Teikem", "Teikem test notification",
            "{message} (disparado por {user})", "{message} (triggered by {user})"),
    };
}
