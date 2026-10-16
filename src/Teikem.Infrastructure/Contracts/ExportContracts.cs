namespace Teikem.Infrastructure.Contracts;

// Lote 77 (P5) — plantillas de exportación a contabilidad y archivo de una corrida de facturación.

public sealed record ExportColumnDto(string Field, string? Header = null);

public sealed record ExportTemplateDto(Guid PublicId, string Scope, string Name, string Format, string Delimiter, string DateFormat, bool IncludeHeader, IReadOnlyList<ExportColumnDto> Columns);

/// <summary>Alta y cambio de una plantilla (PUT reemplaza todo lo que se manda; lo que no viene queda como estaba). Format CSV|TXT; Delimiter «,» «;» tabulador o «|»; DateFormat YYYY-MM-DD, MM/DD/YYYY o DD/MM/YYYY.</summary>
public sealed record ExportTemplateRequest(string? Scope = null, string? Name = null, string? Format = null, string? Delimiter = null, string? DateFormat = null, bool? IncludeHeader = null,
    IReadOnlyList<ExportColumnDto>? Columns = null);

/// <summary>Un campo del catálogo de un alcance: su código, tipo (Text, Date, Money, Number), el encabezado por omisión y su etiqueta en el idioma de la compañía.</summary>
public sealed record ExportFieldDto(string Code, string Kind, string DefaultHeader, string Label);

/// <summary>El archivo armado: nombre, tipo de contenido, el texto completo y cuántas filas lleva (sin el encabezado).</summary>
public sealed record ExportFileDto(string FileName, string ContentType, string Text, int RowCount, string TemplateName);

public sealed record BillingExportRequest(Guid? TemplatePublicId = null);
