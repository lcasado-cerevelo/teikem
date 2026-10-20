using Teikem.Domain.Constants;

namespace Teikem.Domain.Wms;

/// <summary>
/// Lote 156 — lo que el cliente crea solo desde su portal (productos, avisos de llegada y avisos de salida). Cada cosa la enciende el administrador de la compañía POR CLIENTE
/// (Client.PortalCanCreateProducts / PortalCanCreateArrivals / PortalCanCreateExits; apagadas por defecto) y solo la pueden usar los accesos ADMIN u OPERADOR del cliente: el de solo lectura no.
/// Todo se crea siempre a nombre del cliente de la sesión; el cliente nunca elige a otro.
/// </summary>
public static class PortalCreateRules
{
    public const string ReadOnlyForbidden = "Su acceso es de solo lectura y no puede crear documentos.";
    public const string ProductsOff = "Su compañía no le permite crear productos desde el portal.";
    public const string ArrivalsOff = "Su compañía no le permite crear avisos de llegada desde el portal.";
    public const string ExitsOff = "Su compañía no le permite crear avisos de salida desde el portal.";

    /// <summary>¿El rol del acceso puede crear? ADMIN y OPERADOR del cliente sí; solo lectura y consignatario no.</summary>
    public static bool RoleCanCreate(string? role) => role is PortalRoles.ClientAdmin or PortalRoles.ClientOperator;
}
