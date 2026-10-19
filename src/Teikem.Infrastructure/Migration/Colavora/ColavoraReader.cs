using System.Data;
using Microsoft.Data.SqlClient;

namespace Teikem.Infrastructure.Migration.Colavora;

// ---------------------------------------------------------------- filas de la fuente (nombres de Colavora)

public sealed record CvCompany(string CompanyId, string Name, string? Tel, string? Address1, string? City, string? State, string? Zip, string? Contact, string? ContactEmail, string? CompanyEmail);
public sealed record CvUser(int UserId, string CompanyId, string Username, string? FirstName, string? LastName, bool? SecAdmin, bool? Active, int? SecGroup, int? TypeId, DateTime? CreatedDate);
public sealed record CvUserSender(int UserId, int SenderId);
public sealed record CvCustomer(string CompanyId, int SenderId, string Sender, string? SenderName, string? Address1, string? Address2, string? City, string? State, string? Zip,
    string? Contact, string? Phone, string? Fax, string? NotifyEmail, int? AssignPk, bool? Invoice, bool? PhotoOnDelivery, bool? SignatureOnDelivery, bool? ExcludeFromApp);
public sealed record CvCustomerConfig(int SenderId, bool? PrintInvoiceLabel, bool? AutoGenInvoice, bool? AutomaticLabelPrinting, int DefaultPackageType, int DefaultServiceType, bool DuplicateInvoice, bool UseNumberInConsName);
public sealed record CvDivision(string Code, string? Description);
public sealed record CvServiceType(int Id, string Name, bool IsForPickup, string? Type);
public sealed record CvPackageType(int Id, string Name, string? Type, bool HasItems);
public sealed record CvCodType(int Id, string Name);
public sealed record CvTypeCode(string TableName, string ColavoraId, string Name, string Language);
public sealed record CvReason(int Id, string? CustomerId, string Code, string Description, bool CompleteOrder);
public sealed record CvZip(string Zip, string? City, string? State, int? RoutePk, int? RouteDl, string? Zone, string? Driver);
public sealed record CvConsignee(int Id, int SenderId, string? Number, string? ContactName, string? CompanyName, string Address1, string? Address2, string? City, string? State,
    string PostalCode, string? Zone, string? Country, string? Latitude, string? Longitude, string? Comments, string? Email, string? Phone, DateTime? CreationDate, bool? Archived);
public sealed record CvServicePrice(int Id, int SenderId, int ServiceType, int PackageType, decimal Price, short ItemFrom, short ItemTo, decimal CodFee, bool IsForPickup, bool HasDispatchCharge, decimal? DispatchFee);
public sealed record CvShipment(int ShipmentId, string Waybill, int? ServiceType, int? PackageType, DateTime? ReadyDate, DateTime? DueDate, string? CodType, decimal? CodAmount,
    string? Sender, string? SenderName, string? SenderAddress1, string? SenderAddress2, string? SenderZip, string? SenderPhone, string? SenderContact, string? OrderNotes,
    string? ConsigneeNum, string? ConsigneeName, string? AddressLine1, string? AddressLine2, string? Zip, string? Phone, string? ContactPerson,
    int? CreatedUserId, DateTime? CreatedDate, string? Status, int? Qty, int? Items, string? ManifestDl, string? ManifestPk, int? AssignPk, int? AssignDl,
    string? Division, string? LastEventComment, string? Reason, string? Barcode, string? ReferenceNo, DateTime? ManifestDlDate, DateTime? ManifestPkDate, string? Weight, string? PoNumber);
public sealed record CvPiece(int PieceId, int ShipmentId, string? Barcode, int? PackageType, string? Comments);
public sealed record CvEvent(int EventId, int ShipmentId, string? EventType, string? Event, int? CreatedUser, DateTime? CreatedDate, string? CreatedSource, decimal CodAmount, string? CodCheck, int? CodType);
public sealed record CvPod(int PodId, int ShipmentId, string? Name, int? Type, string? Signature, string? Image, int? CreatedUser, DateTime? CreatedDate, decimal? Cod, int? CodTypeId, string? Latitude, string? Longitude);

/// <summary>
/// Lector de SOLO LECTURA de las bases de Colavora Courier (colavora_courier y colavora_courier_login). Solo SELECT
/// parametrizados por CompanyID; nunca escribe. Las órdenes, piezas, eventos y comprobantes se leen por rangos de ShipmentID
/// para no cargar 1.26 M de órdenes en memoria.
/// </summary>
public sealed class ColavoraReader(string courierConnectionString, string loginConnectionString)
{
    public const int CommandTimeoutSeconds = 600;

    // ---------------------------------------------------------------- login

    public Task<CvCompany?> ReadCompanyAsync(string companyId, CancellationToken ct)
        => OneAsync(loginConnectionString,
            "SELECT RTRIM(CompanyID), CompanyName, Tel1, Address1, City, State, Zip, Contact, ContactEmail, CompanyEmail FROM dbo.Company WHERE RTRIM(CompanyID) = @c",
            r => new CvCompany(r.GetString(0), S(r, 1) ?? string.Empty, S(r, 2), S(r, 3), S(r, 4), S(r, 5), S(r, 6), S(r, 7), S(r, 8), S(r, 9)), ct, ("@c", companyId));

    public Task<List<CvUser>> ReadUsersAsync(string companyId, CancellationToken ct)
        => ListAsync(loginConnectionString,
            "SELECT UserID, RTRIM(CompanyID), Username, FirstName, LastName, SecAdmin, Active, SecGroup, TypeID, CreatedDate FROM dbo.Users WHERE RTRIM(CompanyID) = @c ORDER BY UserID",
            r => new CvUser(r.GetInt32(0), r.GetString(1), S(r, 2) ?? string.Empty, S(r, 3), S(r, 4), B(r, 5), B(r, 6), I(r, 7), I(r, 8), D(r, 9)), ct, ("@c", companyId));

    public Task<List<CvUserSender>> ReadUserSendersAsync(CancellationToken ct)
        => ListAsync(loginConnectionString, "SELECT UserId, SenderId FROM dbo.UsersSender", r => new CvUserSender(r.GetInt32(0), r.GetInt32(1)), ct);

    // ---------------------------------------------------------------- catálogos y maestros

    public Task<List<CvCustomer>> ReadCustomersAsync(string companyId, CancellationToken ct)
        => ListAsync(courierConnectionString,
            "SELECT CompanyID, SenderID, Sender, SenderName, Address1, Address2, City, State, Zip, Contact, Phone, FaxPhone, NotifyEmail, AssignPk, Invoice, PhotoOnDelivery, SignatureOnDelivery, ExcludeFromApp FROM dbo.Customers WHERE RTRIM(CompanyID) = @c ORDER BY SenderID",
            r => new CvCustomer(r.GetString(0).Trim(), r.GetInt32(1), S(r, 2) ?? string.Empty, S(r, 3), S(r, 4), S(r, 5), S(r, 6), S(r, 7), S(r, 8), S(r, 9), S(r, 10), S(r, 11), S(r, 12), I(r, 13), B(r, 14), B(r, 15), B(r, 16), B(r, 17)), ct, ("@c", companyId));

    public Task<List<CvCustomerConfig>> ReadCustomerConfigsAsync(CancellationToken ct)
        => ListAsync(courierConnectionString,
            "SELECT SenderID, PrintInvoiceLabel, AutoGenInvoice, AutomaticLabelPrinting, DefaultPackageType, DefaultServiceType, DuplicateInvoice, UseNumberInConsName FROM dbo.Customers_Configuration",
            r => new CvCustomerConfig(r.GetInt32(0), B(r, 1), B(r, 2), B(r, 3), r.GetInt32(4), r.GetInt32(5), r.GetBoolean(6), r.GetBoolean(7)), ct);

    public Task<List<CvDivision>> ReadDivisionsAsync(string companyId, CancellationToken ct)
        => ListAsync(courierConnectionString, "SELECT Division, Description FROM dbo.Customers_Divisions WHERE RTRIM(CompanyID) = @c AND Division IS NOT NULL ORDER BY ID",
            r => new CvDivision(r.GetString(0).Trim(), S(r, 1)), ct, ("@c", companyId));

    public Task<List<CvServiceType>> ReadServiceTypesAsync(string companyId, CancellationToken ct)
        => ListAsync(courierConnectionString, "SELECT ServiceTypeID, ServiceType, IsForPickup, Type FROM dbo.ServiceType WHERE RTRIM(CompanyID) = @c ORDER BY ServiceTypeID",
            r => new CvServiceType(r.GetInt32(0), S(r, 1) ?? string.Empty, B(r, 2) ?? false, S(r, 3)), ct, ("@c", companyId));

    public Task<List<CvPackageType>> ReadPackageTypesAsync(string companyId, CancellationToken ct)
        => ListAsync(courierConnectionString, "SELECT PackageTypeID, PackageType, Type, HasItems FROM dbo.PackageType WHERE RTRIM(CompanyID) = @c ORDER BY PackageTypeID",
            r => new CvPackageType(r.GetInt32(0), S(r, 1) ?? string.Empty, S(r, 2), B(r, 3) ?? false), ct, ("@c", companyId));

    public Task<List<CvCodType>> ReadCodTypesAsync(string companyId, CancellationToken ct)
        => ListAsync(courierConnectionString, "SELECT CODTypeID, CODType FROM dbo.CODType WHERE RTRIM(CompanyID) = @c ORDER BY CODTypeID",
            r => new CvCodType(r.GetInt32(0), S(r, 1) ?? string.Empty), ct, ("@c", companyId));

    /// <summary>Etiquetas es/en de los catálogos (TypeCode): TableName (CODType, PackageType, ServiceType) + ColavoraId + idioma.</summary>
    public Task<List<CvTypeCode>> ReadTypeCodesAsync(CancellationToken ct)
        => ListAsync(courierConnectionString, "SELECT TableName, RTRIM(ColavoraId), Name, RTRIM(Language) FROM dbo.TypeCode WHERE IsActive = 1 AND Name IS NOT NULL",
            r => new CvTypeCode(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3)), ct);

    public Task<List<CvReason>> ReadReasonsAsync(string companyId, CancellationToken ct)
        => ListAsync(courierConnectionString, "SELECT ReasonID, CustomerID, RTRIM(ReasonCode), ReasonDesc, CompleteOrder FROM dbo.Shipments_Reasons WHERE RTRIM(CompanyID) = @c ORDER BY ReasonID",
            r => new CvReason(r.GetInt32(0), S(r, 1), r.GetString(2), S(r, 3) ?? string.Empty, B(r, 4) ?? false), ct, ("@c", companyId));

    public Task<List<CvZip>> ReadZipsAsync(string companyId, CancellationToken ct)
        => ListAsync(courierConnectionString, "SELECT Zip, City, State, RoutePK, RouteDL, Zone, Driver FROM dbo.Zip WHERE RTRIM(CompanyID) = @c ORDER BY ID",
            r => new CvZip(S(r, 0) ?? string.Empty, S(r, 1), S(r, 2), I(r, 3), I(r, 4), S(r, 5), S(r, 6)), ct, ("@c", companyId));

    /// <summary>Consignatarios de los clientes (SenderId) de la compañía.</summary>
    public Task<List<CvConsignee>> ReadConsigneesAsync(string companyId, CancellationToken ct)
        => ListAsync(courierConnectionString,
            "SELECT c.ID, c.SenderId, c.ConsigneeNumber, c.ContactName, c.CompanyName, c.Address1, c.Address2, c.City, c.State, c.PostalCode, c.Zone, c.Country, c.Latitude, c.Longitude, c.Comments, c.Email, c.Phone, c.CreationDate, c.Archived "
            + "FROM dbo.Consignees c JOIN dbo.Customers cu ON cu.SenderID = c.SenderId WHERE RTRIM(cu.CompanyID) = @c ORDER BY c.ID",
            r => new CvConsignee(r.GetInt32(0), r.GetInt32(1), S(r, 2), S(r, 3), S(r, 4), S(r, 5) ?? string.Empty, S(r, 6), S(r, 7), S(r, 8), S(r, 9) ?? string.Empty, S(r, 10), S(r, 11), S(r, 12), S(r, 13), S(r, 14), S(r, 15), S(r, 16), D(r, 17), B(r, 18)),
            ct, ("@c", companyId));

    public Task<List<CvServicePrice>> ReadServicePricesAsync(string companyId, CancellationToken ct)
        => ListAsync(courierConnectionString,
            "SELECT p.ID, p.SenderID, p.ServiceType, p.PackageType, p.Price, p.ItemFrom, p.ItemTo, p.CodFee, p.IsForPickup, p.HasDispatchCharge, p.DispatchFee "
            + "FROM dbo.Customers_Service_Price p JOIN dbo.Customers cu ON cu.SenderID = p.SenderID WHERE RTRIM(cu.CompanyID) = @c ORDER BY p.SenderID, p.ServiceType, p.PackageType, p.ItemFrom",
            r => new CvServicePrice(r.GetInt32(0), r.GetInt32(1), r.GetInt32(2), r.GetInt32(3), r.GetDecimal(4), r.GetInt16(5), r.GetInt16(6), r.GetDecimal(7), r.GetBoolean(8), r.GetBoolean(9), r.IsDBNull(10) ? null : r.GetDecimal(10)),
            ct, ("@c", companyId));

    // ---------------------------------------------------------------- órdenes (por rangos)

    public async Task<(int Min, int Max, long Count)> ShipmentRangeAsync(string companyId, CancellationToken ct)
    {
        var row = await OneAsync(courierConnectionString, "SELECT ISNULL(MIN(ShipmentID),0), ISNULL(MAX(ShipmentID),0), COUNT_BIG(*) FROM dbo.Shipments WHERE RTRIM(CompanyID) = @c",
            r => (r.GetInt32(0), r.GetInt32(1), r.GetInt64(2)), ct, ("@c", companyId));
        return row;
    }

    /// <summary>Lote de órdenes: las siguientes <paramref name="take"/> con ShipmentID &gt;= <paramref name="fromId"/> (y &lt;= toId si se indica), en orden.</summary>
    public Task<List<CvShipment>> ReadShipmentsAsync(string companyId, int fromId, int? toId, int take, CancellationToken ct)
        => ListAsync(courierConnectionString,
            "SELECT TOP (@take) ShipmentID, Waybill, ServiceType, PackageType, ReadyDate, DueDate, CODType, CODAmount, Sender, SenderName, SenderAddressLine1, SenderAddressLine2, SenderZip, SenderPhoneNumber, SenderContactPerson, OrderNotes, "
            + "ConsigneeNum, ConsigneeName, AddressLine1, AddressLine2, Zip, PhoneNumber, ContactPerson, CreatedUserID, CreatedDate, Status, Qty, Items, ManifestDL, ManifestPk, AssignPK, AssignDL, Division, LastEventComment, Reason, Barcode, ReferenceNo, ManifestDL_Date, ManifestPk_Date, Weight, PONumber "
            + "FROM dbo.Shipments WHERE RTRIM(CompanyID) = @c AND ShipmentID >= @from AND (@to IS NULL OR ShipmentID <= @to) AND Waybill IS NOT NULL ORDER BY ShipmentID",
            r => new CvShipment(r.GetInt32(0), r.GetString(1).Trim(), I(r, 2), I(r, 3), D(r, 4), D(r, 5), S(r, 6), M(r, 7), S(r, 8), S(r, 9), S(r, 10), S(r, 11), S(r, 12), S(r, 13), S(r, 14), S(r, 15),
                S(r, 16), S(r, 17), S(r, 18), S(r, 19), S(r, 20), S(r, 21), S(r, 22), I(r, 23), D(r, 24), S(r, 25), I(r, 26), I(r, 27), S(r, 28), S(r, 29), I(r, 30), I(r, 31), S(r, 32), S(r, 33), S(r, 34), S(r, 35), S(r, 36), D(r, 37), D(r, 38), S(r, 39), S(r, 40)),
            ct, ("@c", companyId), ("@from", fromId), ("@to", (object?)toId ?? DBNull.Value), ("@take", take));

    public Task<List<CvPiece>> ReadPiecesAsync(int fromId, int toId, CancellationToken ct)
        => ListAsync(courierConnectionString, "SELECT PieceID, ShipmentID, Barcode, PackageType, Comments FROM dbo.Shipments_Pieces WHERE ShipmentID BETWEEN @from AND @to ORDER BY ShipmentID, PieceID",
            r => new CvPiece(r.GetInt32(0), r.GetInt32(1), S(r, 2), I(r, 3), S(r, 4)), ct, ("@from", fromId), ("@to", toId));

    /// <summary>Eventos del rango (ShipmentID es nvarchar en Colavora: se convierte; los no numéricos se ignoran).</summary>
    public Task<List<CvEvent>> ReadEventsAsync(int fromId, int toId, CancellationToken ct)
        => ListAsync(courierConnectionString,
            "SELECT ShipmentEventID, TRY_CAST(ShipmentID AS int), EventType, Event, CreatedUser, CreatedDate, CreatedSource, CODAmount, CODCheck, CODType FROM dbo.Shipments_Events "
            + "WHERE TRY_CAST(ShipmentID AS int) BETWEEN @from AND @to ORDER BY TRY_CAST(ShipmentID AS int), CreatedDate, ShipmentEventID",
            r => new CvEvent(r.GetInt32(0), r.GetInt32(1), S(r, 2), S(r, 3), I(r, 4), D(r, 5), S(r, 6), r.IsDBNull(7) ? 0 : r.GetDecimal(7), S(r, 8), I(r, 9)), ct, ("@from", fromId), ("@to", toId));

    public Task<List<CvPod>> ReadPodsAsync(int fromId, int toId, bool withImages, CancellationToken ct)
        => ListAsync(courierConnectionString,
            "SELECT PODID, ShipmentID, PODName, PODType, " + (withImages ? "Signature, Image" : "NULL, NULL") + ", CreatedUser, CreatedDate, COD, CODTypeID, Latitude, Longitude FROM dbo.Shipments_POD WHERE ShipmentID BETWEEN @from AND @to ORDER BY ShipmentID, PODID",
            r => new CvPod(r.GetInt32(0), r.GetInt32(1), S(r, 2), I(r, 3), S(r, 4), S(r, 5), I(r, 6), D(r, 7), M(r, 8), I(r, 9), S(r, 10), S(r, 11)), ct, ("@from", fromId), ("@to", toId));

    // ---------------------------------------------------------------- plomería

    private static string? S(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i).Trim() is { Length: > 0 } s ? s : null;
    private static int? I(SqlDataReader r, int i) => r.IsDBNull(i) ? null : Convert.ToInt32(r.GetValue(i));
    private static bool? B(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetBoolean(i);
    private static DateTime? D(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetDateTime(i);
    private static decimal? M(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetDecimal(i);

    private static async Task<List<T>> ListAsync<T>(string cs, string sql, Func<SqlDataReader, T> map, CancellationToken ct, params (string Name, object Value)[] args)
    {
        await using var cn = new SqlConnection(cs);
        await cn.OpenAsync(ct);
        await using var cmd = Command(cn, sql, args);
        await using var r = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, ct);
        var list = new List<T>();
        while (await r.ReadAsync(ct)) list.Add(map(r));
        return list;
    }

    private static async Task<T?> OneAsync<T>(string cs, string sql, Func<SqlDataReader, T> map, CancellationToken ct, params (string Name, object Value)[] args)
    {
        await using var cn = new SqlConnection(cs);
        await cn.OpenAsync(ct);
        await using var cmd = Command(cn, sql, args);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? map(r) : default;
    }

    private static SqlCommand Command(SqlConnection cn, string sql, (string Name, object Value)[] args)
    {
        var cmd = cn.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = CommandTimeoutSeconds;
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd;
    }
}
