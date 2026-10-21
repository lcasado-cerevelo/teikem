using Teikem.Domain.Orders;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Contracts;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// 2026-10-21 — Proveedores en el motor genérico de importación (2.ª entidad). Solo CREA: el nombre de un proveedor activo que ya existe, o repetido en el archivo, se rechaza. Revisar usa las reglas del
/// alta manual (<see cref="SupplierService.CheckCreateAsync"/>, sin guardar); crear usa <see cref="SupplierService.CreateAsync"/>.
/// </summary>
public sealed class SupplierImportRows(SupplierService suppliers) : IImportEntityRows
{
    public const string RepeatedInFileMessage = "El proveedor está repetido en el archivo.";

    public string Kind => ImportKinds.Supplier;

    private sealed class Context
    {
        public HashSet<string> Seen { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public Task<object> LoadContextAsync(CancellationToken ct) => Task.FromResult<object>(new Context());

    public async Task<ImportRowCheck> CheckRowAsync(object context, IReadOnlyDictionary<string, string> values, CancellationToken ct)
    {
        var ctx = (Context)context;
        string? Get(string f) => values.TryGetValue(f, out var v) && v.Length > 0 ? v : null;
        var request = new SupplierRequest(Get(SupplierImportFields.Name), Get(SupplierImportFields.ContactName), Get(SupplierImportFields.Phone), Get(SupplierImportFields.Email),
            Get(SupplierImportFields.PaymentTerm), Get(SupplierImportFields.Notes), Get(SupplierImportFields.PhoneCountryCallingCode));
        var errors = new Dictionary<string, string>(await suppliers.CheckCreateAsync(request, ct), StringComparer.Ordinal);
        if (errors.Count == 0 && !ctx.Seen.Add(request.Name!.Trim())) errors[SupplierImportFields.Name] = RepeatedInFileMessage;
        return new ImportRowCheck(errors, request.Name, request.Email, request);
    }

    public Task CommitRowAsync(object payload, CancellationToken ct) => suppliers.CreateAsync((SupplierRequest)payload, ct);
}
