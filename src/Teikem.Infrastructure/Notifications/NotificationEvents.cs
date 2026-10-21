using Teikem.Domain.Notifications;

namespace Teikem.Infrastructure.Notifications;

/// <summary>Una variable que un evento ofrece a las plantillas: {nombre}. Sample = valor de ejemplo para «Enviar prueba».</summary>
public sealed record NotificationVariable(string Name, string LabelEs, string LabelEn, string Sample);

/// <summary>
/// Lote 51 — un evento de aviso definido en código. Cada módulo registra los suyos con un <see cref="INotificationEventProvider"/>.
/// IsOptional = el usuario puede silenciarlo en su bandeja. LinkTemplate = ruta de la web con {variables} (p. ej. «/orders/{orderNumber}»).
/// Los textos por omisión rellenan la regla nueva (el administrador los puede cambiar).
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
    string DefaultBodyEn);

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

/// <summary>Catálogo de eventos: la unión de todos los proveedores registrados. Un código repetido es un error de programación.</summary>
public sealed class NotificationEventCatalog
{
    private readonly Dictionary<string, NotificationEventDef> _events;

    public NotificationEventCatalog(IEnumerable<INotificationEventProvider> providers)
    {
        _events = new Dictionary<string, NotificationEventDef>(StringComparer.Ordinal);
        foreach (var e in providers.SelectMany(p => p.Events))
            if (!_events.TryAdd(e.Code, e)) throw new InvalidOperationException($"El evento de aviso '{e.Code}' está definido dos veces.");
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
