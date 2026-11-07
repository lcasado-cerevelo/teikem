namespace Teikem.Domain.Sales;

/// <summary>
/// Lote 223 (P3 del pedido de vendedores) — los pasos del pedido guiado. Cada paso guarda el borrador de la orden de venta (POST / PATCH con `step`): si el
/// celular se apaga, el pedido está en Órdenes de venta como borrador. El paso NO se guarda en la base: solo dice qué validar al guardar.
/// </summary>
public static class SalesDraftSteps
{
    /// <summary>Paso 1: el cliente (urgente, nota, forma de pago). El borrador puede no tener productos todavía.</summary>
    public const string Client = "CLIENT";
    /// <summary>Paso 2: los productos (carrito). Desde aquí la orden necesita al menos un producto.</summary>
    public const string Products = "PRODUCTS";
    /// <summary>Paso 3: entrega o recogido, consignatario, consignación, fecha requerida.</summary>
    public const string Delivery = "DELIVERY";
    /// <summary>Paso 4: nombre y firma de quien autoriza (la firma se sube aparte: POST /sales-orders/{id}/signature).</summary>
    public const string Authorization = "AUTHORIZATION";

    public static readonly string[] All = { Client, Products, Delivery, Authorization };
}

/// <summary>Lote 223 — reglas puras y mensajes del pedido guiado (borrador por pasos, firma, enviar con reautenticación). El manual y la FAQ los citan tal cual.</summary>
public static class SalesDraftStepRules
{
    public const string StepInvalid = "El paso del pedido debe ser CLIENT, PRODUCTS, DELIVERY o AUTHORIZATION.";
    public const string ConsigneeRequired = "Escoja a dónde se entrega el pedido o márquelo como recogido.";
    public const string SignatureRequiredFile = "Dibuje la firma de quien autoriza el pedido.";
    public const string SignatureNotImage = "La firma debe ser una imagen PNG o JPG.";
    public const string SubmitAuthRequired = "Indique cómo se confirmó el envío del pedido: PASSWORD (contraseña) o BIOMETRIC (huella o rostro en la app).";
    public const string PasswordRequired = "Escriba su contraseña para enviar el pedido.";
    /// <summary>Los mensajes de AuthService.ReauthAsync que el envío responde como 400 (no 401: un 401 haría que el cliente del API refresque y repita el intento).</summary>
    public const string PasswordWrong = "Contraseña incorrecta.";
    public const string MfaWrong = "Código MFA inválido.";

    /// <summary>Firma: solo PNG o JPG (lo que dibuja el lienzo de la web y de la app).</summary>
    public static readonly string[] SignatureTypes = { "image/png", "image/jpeg" };

    /// <summary>CLIENT | PRODUCTS | DELIVERY | AUTHORIZATION (sin distinguir mayúsculas); vacío = null (guardado normal, sin pasos). Devuelve (paso, error).</summary>
    public static (string? Step, string? Error) ParseStep(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return (null, null);
        var v = value.Trim().ToUpperInvariant();
        return SalesDraftSteps.All.Contains(v) ? (v, null) : (null, StepInvalid);
    }

    /// <summary>¿Puede el borrador quedar sin productos? Solo en el paso del cliente (el guardado normal, sin paso, sigue exigiendo líneas).</summary>
    public static bool LinesOptional(string? step) => step == SalesDraftSteps.Client;

    /// <summary>¿Exige el paso al menos un producto en la orden? Desde PRODUCTS en adelante.</summary>
    public static bool NeedsLines(string? step) => step is SalesDraftSteps.Products or SalesDraftSteps.Delivery or SalesDraftSteps.Authorization;

    /// <summary>
    /// En la entrega (y al enviar) un pedido marcado «Entrega» necesita a dónde: con DeliveryMode = DELIVERY y sin consignatario, error. NULL o PICKUP no lo exigen
    /// (NULL se deduce del consignatario).
    /// </summary>
    public static bool MissingConsignee(string? deliveryMode, bool hasConsignee)
        => string.Equals(deliveryMode, SalesDeliveryModes.Delivery, StringComparison.OrdinalIgnoreCase) && !hasConsignee;

    /// <summary>¿Valida el paso a dónde se entrega? En DELIVERY y AUTHORIZATION.</summary>
    public static bool ChecksDelivery(string? step) => step is SalesDraftSteps.Delivery or SalesDraftSteps.Authorization;

    /// <summary>La firma por su contenido real (no por lo que diga el navegador): solo PNG o JPG.</summary>
    public static bool IsSignatureType(string? sniffedContentType)
        => sniffedContentType is not null && SignatureTypes.Contains(sniffedContentType, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// BIOMETRIC: el teléfono valida la huella o el rostro, el servidor no la ve. Vale solo si la sesión trae una verificación reciente (AAL2: contraseña y MFA al entrar o
    /// al reautenticarse) dentro de la ventana de la compañía (Tenant.Aal2WindowMinutes, 30 si no tiene). Así la huella no se salta el MFA.
    /// </summary>
    public static bool RecentAuth(DateTime? aal2VerifiedAtUtc, int windowMinutes, DateTime nowUtc)
    {
        if (aal2VerifiedAtUtc is not DateTime at) return false;
        var window = windowMinutes <= 0 ? 30 : windowMinutes;
        return at.AddMinutes(window) >= nowUtc;
    }
}
