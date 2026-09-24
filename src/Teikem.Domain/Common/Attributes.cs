namespace Teikem.Domain.Common;

/// <summary>
/// Marca una entidad como auditable en AuditLog y declara el código de EntityType (catálogo 'EntityType')
/// con el que se registra. Entidades sin este atributo no generan filas de AuditLog.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class AuditEntityAttribute : Attribute
{
    public AuditEntityAttribute(string entityTypeCode) => EntityTypeCode = entityTypeCode;
    public string EntityTypeCode { get; }
}

/// <summary>Propiedad excluida del diff de AuditLog por convención de mapeo (secretos, hashes, tokens).</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class SensitiveDataAttribute : Attribute { }

/// <summary>Propiedad que no aporta al diff (RowVersion, timestamps técnicos).</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class NotAuditedAttribute : Attribute { }
