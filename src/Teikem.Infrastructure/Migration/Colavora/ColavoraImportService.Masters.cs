using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Clients;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Contacts;
using Teikem.Domain.CustomFields;
using Teikem.Domain.Fleet;
using Teikem.Domain.Migration.Colavora;
using Teikem.Infrastructure.Contracts;

namespace Teikem.Infrastructure.Migration.Colavora;

/// <summary>Maestros: catálogos por compañía (D14), usuarios y choferes (D11), zonas (D12), clientes, consignatarios (D7), contratos y tarifas (D13), campos (D6).</summary>
public sealed partial class ColavoraImportService
{
    // ================================================================ catálogos (D14: por compañía)

    private async Task ImportCatalogsAsync(RunState s, CancellationToken ct)
    {
        const string E = ColavoraEntities.Catalogs;
        var labels = (await s.Reader.ReadTypeCodesAsync(ct))
            .GroupBy(t => (t.TableName.ToUpperInvariant(), t.ColavoraId))
            .ToDictionary(g => g.Key, g => g.ToDictionary(x => x.Language.ToLowerInvariant(), x => x.Name));

        Dictionary<string, string> Labels(string table, int id, string fallback)
        {
            var d = labels.TryGetValue((table, id.ToString(CultureInfo.InvariantCulture)), out var l) ? new Dictionary<string, string>(l) : new Dictionary<string, string>();
            if (!d.ContainsKey("es")) d["es"] = fallback;
            if (!d.ContainsKey("en")) d["en"] = d["es"];
            return d;
        }

        // tipos de servicio
        foreach (var st in await s.Reader.ReadServiceTypesAsync(s.CompanyId, ct))
        {
            s.Report.CountRead(E);
            var pickUp = ColavoraRules.IsPickupServiceType(st.Type, st.IsForPickup);
            // decisión de Luis (2026-10-20): los tipos de servicio son de cada compañía, nunca los globales; el código lleva el CompanyID
            // porque UQ_LookupCode(Entity, InternalCode) es global y dos compañías no pueden repetir el mismo código
            var extra = pickUp ? "{\"pickUp\":true,\"billable\":true}" : null;
            var id = await EnsureTenantLookupAsync(s, LookupDomains.ServiceType, ColavoraRules.CompanyCatalogCode(st.Name, s.CompanyId), Labels("SERVICETYPE", st.Id, st.Name), extra, $"servicio {st.Name}", ct);
            if (id is int sid)
            {
                s.ServiceTypes[st.Id] = (sid, st.Name, pickUp);
                if (pickUp && string.Equals(st.Type?.Trim(), "PK", StringComparison.OrdinalIgnoreCase)) s.PickupServiceTypeId = sid;
                if (string.Equals(st.Type?.Trim(), "ND", StringComparison.OrdinalIgnoreCase) || s.DefaultServiceTypeId == s.GlobalServiceTypeId) s.DefaultServiceTypeId = sid;
            }
        }

        // tipos de paquete
        foreach (var pt in await s.Reader.ReadPackageTypesAsync(s.CompanyId, ct))
        {
            s.Report.CountRead(E);
            var isInvoice = ColavoraRules.IsInvoicePackageType(pt.Type);
            var id = await EnsureTenantLookupAsync(s, LookupDomains.PackageType, ColavoraRules.CompanyCatalogCode(pt.Name, s.CompanyId), Labels("PACKAGETYPE", pt.Id, pt.Name), null, $"paquete {pt.Name}", ct);
            if (id is int pid)
            {
                s.PackageTypes[pt.Id] = (pid, pt.Name, isInvoice);
                if (string.Equals(pt.Type?.Trim(), "BX", StringComparison.OrdinalIgnoreCase) || (s.DefaultPackageTypeId == s.GlobalPackageTypeId && !isInvoice)) s.DefaultPackageTypeId = pid;
            }
        }

        // tipos de COD: los textos de la orden (None, Cash or check…) y el catálogo de cobro
        var codNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var text in new[] { "None", "Cash", "Cash Only", "Check", "Cash or check", "Credit Card", "Authorized" }) codNames[ColavoraRules.MapCodType(text, null)] = text;
        foreach (var c in await s.Reader.ReadCodTypesAsync(s.CompanyId, ct)) codNames.TryAdd(ColavoraRules.MapCodType(c.Name, null), c.Name);
        foreach (var (code, name) in codNames)
        {
            s.Report.CountRead(E);
            var id = await lookups.TryGetIdAsync(LookupDomains.CodType, code, ct);
            if (id is int gid) { s.Report.CountExisting(E); s.CodTypes[code] = gid; continue; }
            var es = code switch { "CASH_OR_CHECK" => "Efectivo o cheque", "CREDIT_CARD" => "Tarjeta de crédito", "AUTHORIZED" => "Autorizado", "MONEY_ORDER" => "Giro postal", "ATH_MOVIL" => "ATH Móvil", "MIXED" => "Pago mixto", _ => name };
            id = await EnsureTenantLookupAsync(s, LookupDomains.CodType, code, new Dictionary<string, string> { ["es"] = es, ["en"] = name }, null, $"COD {name}", ct);
            if (id is int cid) s.CodTypes[code] = cid;
        }
        s.CodTypes.TryAdd("NONE", await lookups.GetIdAsync(LookupDomains.CodType, "NONE", ct));
        s.CodTypes.TryAdd("CASH", await lookups.GetIdAsync(LookupDomains.CodType, "CASH", ct));

        // razones de entrega / no entrega → DeliveryFailureReason (las de intento fallido y las demás, para que el intento guarde su razón)
        const string R = ColavoraEntities.Reasons;
        foreach (var r in await s.Reader.ReadReasonsAsync(s.CompanyId, ct))
        {
            s.Report.CountRead(R);
            var code = ColavoraRules.ToCode(r.Description, 34);
            var id = await EnsureTenantLookupAsync(s, DeliveryAttemptCodes.FailureReasonDomain, code, new Dictionary<string, string> { ["es"] = r.Description, ["en"] = r.Description },
                $"{{\"colavoraCode\":\"{r.Code}\"}}", $"razón {r.Code} {r.Description}", ct, R);
            if (id is int rid) s.Reasons[r.Code] = (rid, r.Description);
        }
        if (!s.Reasons.ContainsKey("312")) s.Reasons["312"] = (0, "DELIVERED");
        lookups.Invalidate();
    }

    /// <summary>
    /// Valor de lista de la compañía: si ya existe uno con ese código y es global o de esta compañía, se reutiliza; si el código lo
    /// tiene otra compañía, se crea con el sufijo _{CompanyID}. En dry-run solo cuenta.
    /// </summary>
    private async Task<int?> EnsureTenantLookupAsync(RunState s, string entity, string code, IDictionary<string, string> labels, string? extraJson, string key, CancellationToken ct, string reportEntity = ColavoraEntities.Catalogs)
    {
        var tenantId = s.TenantId;
        var existing = await db.LookupCodes.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(l => l.Entity == entity && l.InternalCode == code, ct);
        if (existing is not null && (existing.TenantId is null || existing.TenantId == tenantId)) { s.Report.CountExisting(reportEntity); return existing.LookupCodeId; }
        if (existing is not null)
        {
            code = ColavoraRules.ToCode($"{code}_{s.CompanyId}");
            var again = await db.LookupCodes.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(l => l.Entity == entity && l.InternalCode == code, ct);
            if (again is not null) { s.Report.CountExisting(reportEntity); return again.LookupCodeId; }
        }
        s.Report.CountCreated(reportEntity);
        if (s.DryRun || tenantId is null) return null;
        var lc = new LookupCode
        {
            Entity = entity, InternalCode = code, LabelJson = MultilingualText.Serialize(labels), ExtraJson = extraJson,
            SortOrder = 100, IsSystem = false, IsActive = true, TenantId = tenantId,
        };
        db.LookupCodes.Add(lc);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        logger.LogInformation("Catálogo {Entity}: creado {Code} ({Key}).", entity, code, key);
        return lc.LookupCodeId;
    }

    // ================================================================ usuarios y choferes (D11)

    private async Task ImportUsersAndDriversAsync(RunState s, CancellationToken ct)
    {
        const string U = ColavoraEntities.Users;
        const string D = ColavoraEntities.Drivers;
        var users = await s.Reader.ReadUsersAsync(s.CompanyId, ct);
        var assigned = s.AssignedUserIds ??= await s.Reader.ReadAssignedUserIdsAsync(s.CompanyId, ct);
        var userIds = users.Select(x => x.UserId).ToHashSet();
        s.Report.AddInfo("Choferes = usuarios asignados en los envíos (AssignDL/AssignPK)",
            $"{assigned.Count} IDs asignados; {assigned.Count(userIds.Contains)} son usuarios de la compañía ({users.Count(x => assigned.Contains(x.UserId) && !ColavoraRules.IsDriver(x.SecGroup))} de ellos NO están en el grupo de choferes de Colavora); {users.Count(x => ColavoraRules.IsDriver(x.SecGroup) && !assigned.Contains(x.UserId))} usuarios del grupo de choferes nunca aparecen asignados y no se crean como chofer; {assigned.Count(a => !userIds.Contains(a))} IDs asignados no son usuarios de la compañía");
        var existingDrivers = s.TenantId is null ? new Dictionary<string, Driver>(StringComparer.OrdinalIgnoreCase)
            : await db.Drivers.IgnoreQueryFilters().AsNoTracking().Where(d => d.TenantId == s.TenantId).ToDictionaryAsync(d => d.EmployeeCode, d => d, StringComparer.OrdinalIgnoreCase, ct);   // también los inactivos: el índice único los cuenta
        var existingUsers = s.TenantId is null ? new Dictionary<string, (int Id, bool Member)>(StringComparer.OrdinalIgnoreCase)
            : (await db.Users.IgnoreQueryFilters().AsNoTracking().Select(u => new { u.Id, u.Email, Member = u.Memberships.Any(m => m.TenantId == s.TenantId) }).ToListAsync(ct))
                .Where(u => u.Email is not null).GroupBy(u => u.Email!, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => (g.First().Id, g.First().Member), StringComparer.OrdinalIgnoreCase);

        foreach (var u in users)
        {
            var isDriver = assigned.Contains(u.UserId);   // chofer = aparece asignado en las órdenes (decisión de Luis, 2026-10-20), no por su grupo de seguridad
            s.Report.CountRead(U);
            if (isDriver) s.Report.CountRead(D);
            var fullName = ColavoraRules.FullName(u.FirstName, u.LastName, u.Username);
            var active = u.Active ?? false;

            // correo: el username si es correo; si no, para los choferes se inventa con options.driverEmailDomain; si no, sin usuario
            string? email = ColavoraRules.LooksLikeEmail(u.Username) ? u.Username.Trim().ToLowerInvariant()
                : isDriver && !string.IsNullOrWhiteSpace(s.Config.Options.DriverEmailDomain) ? $"{ColavoraRules.DriverCode(u.Username).ToLowerInvariant()}@{s.Config.Options.DriverEmailDomain!.Trim()}"
                : null;

            int? userId = null;
            if (email is not null)
            {
                if (existingUsers.TryGetValue(email, out var ex))
                {
                    userId = ex.Id;
                    if (ex.Member) s.Report.CountExisting(U);
                    else if (s.DryRun) s.Report.CountCreated(U);
                    else
                    {
                        var created = await TryAsync(s, U, email, () => userAdmin.CreateUserCoreAsync(new UserCreateRequest(email, fullName, null, new[] { isDriver || ColavoraRules.IsDriver(u.SecGroup) ? "Dispatcher" : ColavoraRules.StaffRole(u.SecAdmin) }, UserKinds.Internal), ct));
                        if (created is not null) { userId = created.User.Id; s.Report.CountCreated(U); if (!active) await SuspendAsync(s, userId.Value, ct); }
                    }
                }
                else if (s.DryRun) { s.Report.CountCreated(U); }
                else
                {
                    var created = await TryAsync(s, U, email, () => userAdmin.CreateUserCoreAsync(new UserCreateRequest(email, fullName, null, new[] { isDriver || ColavoraRules.IsDriver(u.SecGroup) ? "Dispatcher" : ColavoraRules.StaffRole(u.SecAdmin) }, UserKinds.Internal), ct));
                    if (created is not null)
                    {
                        userId = created.User.Id;
                        existingUsers[email] = (userId.Value, true);
                        s.Report.CountCreated(U);
                        if (!active) await SuspendAsync(s, userId.Value, ct);
                    }
                }
            }
            else if (!isDriver)
            {
                s.Report.CountSkipped(U);
                s.Report.Warn(U, u.Username, "El usuario no es chofer (no aparece asignado en ningún envío) y no tiene correo como nombre de usuario; no se crea (no se le puede invitar).");
            }
            if (userId is int uid) s.Users[u.UserId] = uid;

            if (!isDriver) continue;
            var code = ColavoraRules.DriverCode(u.Username);
            if (code.Length == 0) { s.Report.Reject(D, u.Username, "El chofer no tiene código (username vacío)."); continue; }
            s.DriverCodeByUser[u.UserId] = code;
            if (existingDrivers.TryGetValue(code, out var found)) { s.Report.CountExisting(D); s.Drivers[code] = found.DriverId; continue; }
            if (s.DryRun) { s.Report.CountCreated(D); continue; }
            var dto = await TryAsync(s, D, code, () => drivers.CreateAsync(new DriverCreateRequest(code, fullName, UserId: userId), ct));
            if (dto is null) continue;
            s.Report.CountCreated(D);
            s.Drivers[code] = dto.Id;
            if (!active) await TryAsync(s, D, code, async () => { await drivers.SetActiveAsync(dto.PublicId, false, ct); return dto; });
        }
        // los choferes que ya existían pero no están en Users (los que una corrida anterior creó solo desde los eventos) también se reconocen
        foreach (var (code, d) in existingDrivers) s.Drivers.TryAdd(code, d.DriverId);
        IndexDriverAliases(s);
        db.ChangeTracker.Clear();
    }

    /// <summary>
    /// Colavora escribe el código del chofer con ceros a la izquierda en el usuario («003») y a veces sin ellos en los eventos («3»):
    /// cada código también se reconoce sin ceros. Un código real nunca se pisa con un alias.
    /// </summary>
    private static void IndexDriverAliases(RunState s)
    {
        foreach (var (code, id) in s.Drivers.ToList())
        {
            s.Drivers.TryAdd(ColavoraRules.DriverCodeAlias(code), id);
        }
    }

    private async Task SuspendAsync(RunState s, int userId, CancellationToken ct)
        => await TryAsync(s, ColavoraEntities.Users, userId.ToString(CultureInfo.InvariantCulture), () => userAdmin.SetMembershipStatusAsync(userId, new MembershipStatusRequest(MembershipStatuses.Suspended), ct));

    private async Task LoadExistingUsersAndDriversAsync(RunState s, CancellationToken ct)
    {
        if (s.TenantId is null) return;
        var users = await s.Reader.ReadUsersAsync(s.CompanyId, ct);
        var assigned = s.AssignedUserIds ??= await s.Reader.ReadAssignedUserIdsAsync(s.CompanyId, ct);
        var byEmail = (await db.Users.IgnoreQueryFilters().AsNoTracking().Where(u => u.Email != null).Select(u => new { u.Id, u.Email }).ToListAsync(ct))
            .GroupBy(u => u.Email!, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);
        var driversByCode = await db.Drivers.IgnoreQueryFilters().AsNoTracking().Where(d => d.TenantId == s.TenantId).ToDictionaryAsync(d => d.EmployeeCode, d => d.DriverId, StringComparer.OrdinalIgnoreCase, ct);
        foreach (var u in users)
        {
            var email = ColavoraRules.LooksLikeEmail(u.Username) ? u.Username.Trim()
                : assigned.Contains(u.UserId) && !string.IsNullOrWhiteSpace(s.Config.Options.DriverEmailDomain) ? $"{ColavoraRules.DriverCode(u.Username).ToLowerInvariant()}@{s.Config.Options.DriverEmailDomain!.Trim()}" : null;
            if (email is not null && byEmail.TryGetValue(email, out var id)) s.Users[u.UserId] = id;
            if (assigned.Contains(u.UserId))
            {
                var code = ColavoraRules.DriverCode(u.Username);
                s.DriverCodeByUser[u.UserId] = code;
                if (driversByCode.TryGetValue(code, out var did)) s.Drivers[code] = did;
            }
        }
        foreach (var (code, id) in driversByCode) s.Drivers.TryAdd(code, id);
        IndexDriverAliases(s);
    }

    // ================================================================ zonas de despacho (D12: zonas de ruta por ZIP)

    private async Task ImportZonesAsync(RunState s, CancellationToken ct)
    {
        const string Z = ColavoraEntities.Zones;
        var zips = await s.Reader.ReadZipsAsync(s.CompanyId, ct);
        foreach (var z in zips) if (ColavoraRules.NormalizeZip(z.Zip) is { } nz && !string.IsNullOrWhiteSpace(z.City)) s.CityByZip.TryAdd(nz, z.City!);
        var consignees = await s.Reader.ReadConsigneesAsync(s.CompanyId, ct);
        var plan = ColavoraZoneRules.Build(consignees.Select(c => new ColavoraConsigneeZone(c.Zone, c.PostalCode)),
            zips.Select(z => new ColavoraZipRow(z.Zip, z.Zone, z.Driver, z.RouteDl)));
        var planned = plan.Zones;
        s.Report.AddInfo("Zonas de ruta", $"{plan.Groups.Count} niveles ({string.Join(", ", plan.Groups.Select(g => g.Code))}); {planned.Count} zonas con {planned.Sum(p => p.PostalCodes.Count)} ZIP");

        // Lote 129: niveles METRO / ISLA con su chofer por omisión
        var existingGroups = s.TenantId is null ? new List<ZoneGroupDto>() : (await zoneGroups.ListAsync(includeInactive: true, ct)).ToList();
        foreach (var g in plan.Groups)
        {
            s.Report.CountRead(Z);
            var found = existingGroups.FirstOrDefault(x => string.Equals(x.Code, g.Code, StringComparison.OrdinalIgnoreCase));
            if (found is not null) { s.Report.CountExisting(Z); s.ZoneGroups[g.Code] = found.Id; continue; }
            if (s.DryRun) { s.Report.CountCreated(Z); continue; }
            var dto = await TryAsync(s, Z, g.Code, () => zoneGroups.CreateAsync(new ZoneGroupRequest(g.Code, g.Name, g.SortOrder), ct));
            if (dto is null) continue;
            s.Report.CountCreated(Z);
            s.ZoneGroups[g.Code] = dto.Id;
            var groupDriver = g.DriverCode ?? (g.DriverUserId is int gu && s.DriverCodeByUser.TryGetValue(gu, out var gdc) ? gdc : null);
            if (groupDriver is not null && s.Drivers.TryGetValue(groupDriver, out var gdid))
            {
                var entity = await db.Set<ZoneGroup>().FirstAsync(x => x.ZoneGroupId == dto.Id, ct);
                entity.DefaultDriverId = gdid;
                await db.SaveChangesAsync(ct);
                db.ChangeTracker.Clear();
            }
        }

        var existing = s.TenantId is null ? new List<DispatchZoneDto>() : (await dispatchZones.ListAsync(includeInactive: true, ct)).ToList();
        foreach (var p in planned)
        {
            s.Report.CountRead(Z);
            foreach (var zip in p.PostalCodes) s.ZoneIdByZip.TryAdd(zip, 0);   // se completa con el id real abajo
            var found = existing.FirstOrDefault(z => string.Equals(z.Code, p.Code, StringComparison.OrdinalIgnoreCase));
            if (found is not null) { s.Report.CountExisting(Z); s.Zones[p.Code] = found.Id; foreach (var zip in p.PostalCodes) s.ZoneIdByZip[zip] = found.Id; continue; }
            if (s.DryRun) { s.Report.CountCreated(Z); continue; }
            int? groupId = p.GroupCode is not null && s.ZoneGroups.TryGetValue(p.GroupCode, out var gid) ? gid : null;
            var dto = await TryAsync(s, Z, p.Code, () => dispatchZones.CreateAsync(new DispatchZoneRequest(p.Code, p.Name, groupId), ct));
            if (dto is null) continue;
            s.Report.CountCreated(Z);
            s.Zones[p.Code] = dto.Id;
            foreach (var zip in p.PostalCodes)
            {
                s.ZoneIdByZip[zip] = dto.Id;
                await TryAsync(s, Z, $"{p.Code} {zip}", () => dispatchZones.AddMemberAsync(dto.Id, new DispatchZoneMemberRequest(ZoneMatchTypes.PostalCode, zip), ct));
            }
            // chofer de la zona: el código de la tabla Zip o el usuario de entrega
            var driverCode = p.DriverCode ?? (p.DriverUserId is int du && s.DriverCodeByUser.TryGetValue(du, out var dc) ? dc : null);
            if (driverCode is not null && s.Drivers.TryGetValue(driverCode, out var driverId))
            {
                if (!await db.DriverZones.AnyAsync(x => x.DriverId == driverId && x.DispatchZoneId == dto.Id, ct))
                {
                    db.DriverZones.Add(new DriverZone { DriverId = driverId, DispatchZoneId = dto.Id, IsPrimary = !await db.DriverZones.AnyAsync(x => x.DriverId == driverId, ct) });
                    await db.SaveChangesAsync(ct);
                    db.ChangeTracker.Clear();
                }
            }
            else if (driverCode is not null) s.Report.Warn(Z, p.Code, $"El chofer {driverCode} de la zona no existe en Teikem; la zona queda sin chofer.");
        }
    }

    private async Task LoadExistingZonesAsync(RunState s, CancellationToken ct)
    {
        foreach (var z in await s.Reader.ReadZipsAsync(s.CompanyId, ct)) if (ColavoraRules.NormalizeZip(z.Zip) is { } nz && !string.IsNullOrWhiteSpace(z.City)) s.CityByZip.TryAdd(nz, z.City!);
        if (s.TenantId is null) return;
        foreach (var z in await db.DispatchZones.AsNoTracking().ToListAsync(ct)) s.Zones[z.Code] = z.DispatchZoneId;
        foreach (var g in await db.Set<ZoneGroup>().AsNoTracking().ToListAsync(ct)) s.ZoneGroups[g.Code] = g.ZoneGroupId;
        var postalTypeId = await lookups.GetIdAsync(LookupDomains.ZoneMatchType, ZoneMatchTypes.PostalCode, ct);
        foreach (var m in await db.DispatchZones.AsNoTracking().Where(z => z.IsActive).SelectMany(z => z.Members.Where(x => x.MatchTypeLookupId == postalTypeId).Select(x => new { z.DispatchZoneId, x.MatchValue })).ToListAsync(ct))
            if (ColavoraRules.NormalizeZip(m.MatchValue) is { } zip) s.ZoneIdByZip.TryAdd(zip, m.DispatchZoneId);
    }

    // ================================================================ clientes

    private async Task ImportClientsAsync(RunState s, CancellationToken ct)
    {
        const string C = ColavoraEntities.Clients;
        var customers = await s.Reader.ReadCustomersAsync(s.CompanyId, ct);
        var configs = (await s.Reader.ReadCustomerConfigsAsync(ct)).GroupBy(c => c.SenderId).ToDictionary(g => g.Key, g => g.First());
        var excluded = new HashSet<string>(s.Config.Options.ExcludeSenders, StringComparer.OrdinalIgnoreCase);
        await ResolveSubClientsAsync(s, customers, ct);
        var existing = s.TenantId is null ? new Dictionary<string, Client>(StringComparer.OrdinalIgnoreCase)
            : await db.Clients.AsNoTracking().ToDictionaryAsync(c => c.Code, c => c, StringComparer.OrdinalIgnoreCase, ct);
        var usedCodes = new HashSet<string>(existing.Keys, StringComparer.OrdinalIgnoreCase);

        foreach (var cu in customers)
        {
            s.Report.CountRead(C);
            s.SenderCodeById[cu.SenderId] = cu.Sender;
            if (excluded.Contains(cu.Sender) || ColavoraRules.IsTestSender(cu.Sender, cu.SenderName, s.Config.Options.ExcludeSenderPatterns))
            {
                s.Report.CountSkipped(C);
                s.Report.Warn(C, cu.Sender, "Cliente de prueba o excluido por la configuración; no se migra.");
                continue;
            }
            if (s.SubClients.ContainsKey(cu.Sender)) { s.Report.CountSkipped(C); continue; }   // «Recogido/Devolución»: se une a su cliente real (LinkSubClients)
            var code = ColavoraRules.ClientCode(cu.Sender);
            var name = ColavoraRules.Trim(s.ClientRenames.TryGetValue(cu.Sender, out var renamed) ? renamed : cu.SenderName, 200) ?? cu.Sender;
            if (existing.TryGetValue(code, out var found))
            {
                s.Report.CountExisting(C);
                s.Clients[cu.Sender] = new ClientRef(found.ClientId, found.PublicId, found.Code, found.Name, found.DefaultPickupLocationId, null);
                continue;
            }
            if (!usedCodes.Add(code)) { s.Report.Reject(C, cu.Sender, $"El código {code} (recortado a 10) choca con otro cliente ya migrado."); continue; }
            if (s.DryRun) { s.Report.CountCreated(C); s.Clients[cu.Sender] = new ClientRef(0, Guid.Empty, code, name, null, null); continue; }

            var dto = await TryAsync(s, C, cu.Sender, () => clients.CreateAsync(new ClientCreateRequest(code, name, null, null, null, "USD", null, null), ct));
            if (dto is null) continue;
            s.Report.CountCreated(C);

            // dirección de recogido del cliente (su localización por defecto)
            int? pickupId = null;
            var line1 = ColavoraRules.FirstNonEmpty(cu.Address1, cu.Address2, name);
            var zip = ColavoraRules.NormalizeZip(cu.Zip);
            var city = ColavoraRules.Trim(cu.City, 100) ?? (zip is not null && s.CityByZip.TryGetValue(zip, out var zc) ? zc : "—");
            var loc = await TryAsync(s, C, $"{cu.Sender} (recogido)", () => locations.CreateAsync(new LocationUpsertRequest(dto.PublicId, null, name, LocationTypes.Pickup,
                ColavoraRules.Trim(line1, 200)!, string.Equals(line1, cu.Address2?.Trim(), StringComparison.Ordinal) ? null : ColavoraRules.Trim(cu.Address2, 200),
                city, ColavoraRules.NormalizeState(cu.State), zip, "PR", ContactName: ColavoraRules.Trim(cu.Contact, 150)), ct));
            if (loc is not null)
            {
                pickupId = loc.Id;
                await TryAsync(s, C, cu.Sender, () => clients.UpdateProfileAsync(dto.PublicId, new ClientProfileUpdateRequest(null, null, null, null, null, loc.PublicId, false, null), ct));
            }

            // contactos: teléfono, fax y correo de avisos
            foreach (var (type, value, label) in new[] { ("PHONE", cu.Phone, (string?)null), ("FAX", cu.Fax, "Fax"), ("EMAIL", cu.NotifyEmail, "Avisos") })
            {
                if (string.IsNullOrWhiteSpace(value)) continue;
                if (type == "EMAIL")
                {
                    await TryAsync(s, C, $"{cu.Sender}: {value}", () => contacts.AddAsync(EntityTypes.Client, dto.Id, new ContactPointUpsertRequest(type, value.Trim(), null, label, false), ct, enforceOwnerWrite: false));
                    continue;
                }
                // teléfono / fax: solo dígitos + prefijo; varios números del mismo campo → uno por número (el primero principal)
                var phones = ColavoraRules.ParsePhones(value);
                foreach (var w in phones.Warnings) s.Report.Warn(C, cu.Sender, $"{label ?? "Teléfono"}: {w}");
                for (var i = 0; i < phones.Numbers.Count; i++)
                {
                    var (cc, national) = (phones.Numbers[i].CountryCallingCode, phones.Numbers[i].National);
                    var rowLabel = label ?? (i == 0 ? null : "Otro");
                    var ext = i == 0 ? phones.Extension : null;
                    var primary = type == "PHONE" && i == 0;
                    await TryAsync(s, C, $"{cu.Sender}: {value}", () => contacts.AddAsync(EntityTypes.Client, dto.Id, new ContactPointUpsertRequest(type, national, ext, rowLabel, primary, cc), ct, enforceOwnerWrite: false));
                }
            }

            // banderas de la configuración de Colavora que Teikem conoce
            if (configs.TryGetValue(cu.SenderId, out var cfgRow))
            {
                var client = await db.Clients.FirstAsync(c => c.ClientId == dto.Id, ct);
                client.AutoPrintInvoice = cfgRow.PrintInvoiceLabel == true || cfgRow.AutoGenInvoice == true;
                client.AutoPrintLabels = cfgRow.AutomaticLabelPrinting == true;
                await db.SaveChangesAsync(ct);
                db.ChangeTracker.Clear();
            }
            s.Clients[cu.Sender] = new ClientRef(dto.Id, dto.PublicId, code, name, pickupId, null);
        }
        LinkSubClients(s);
    }

    /// <summary>
    /// Decisión de Luis (2026-10-20): los clientes «… RECOGIDO …» / «… DEVOLUCION …» son un solo cliente (el que dice el primer nombre). Aquí se decide a cuál
    /// se une cada uno (el que más órdenes tiene); no se crean como clientes, y sus órdenes (y consignatarios) cuelgan del cliente real.
    /// </summary>
    private async Task ResolveSubClientsAsync(RunState s, List<CvCustomer> customers, CancellationToken ct)
    {
        var counts = await s.Reader.ReadOrderCountsBySenderAsync(s.CompanyId, ct);
        var groups = ColavoraRules.ResolveClientGroups(customers.Select(c => (c.Sender, c.SenderName)), counts);
        s.SubClients = groups.Links;
        s.ClientRenames = groups.Renames;
        foreach (var (sub, link) in s.SubClients.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var subName = customers.First(c => string.Equals(c.Sender, sub, StringComparison.OrdinalIgnoreCase)).SenderName;
            var mainName = customers.First(c => string.Equals(c.Sender, link.MainSender, StringComparison.OrdinalIgnoreCase)).SenderName;
            s.Report.AddInfo($"Cliente unido {sub}", $"«{subName}» ({counts.GetValueOrDefault(sub)} envíos) se une a «{mainName}» ({link.MainSender}, {counts.GetValueOrDefault(link.MainSender)} envíos); {(link.Kind switch { ColavoraRules.SubClientKind.Pickup => "sus órdenes se trabajan como recogido", ColavoraRules.SubClientKind.Return => "sus órdenes se trabajan como devolución", _ => "es el mismo cliente separado por servicio: sus tarifas pasan al contrato del cliente" })}.");
        }
    }

    /// <summary>Cada cliente «hijo» usa la misma ficha del cliente real; si el real no está migrado, el hijo se descarta con aviso.</summary>
    private void LinkSubClients(RunState s)
    {
        foreach (var (sub, link) in s.SubClients.ToList())
        {
            if (s.Clients.TryGetValue(link.MainSender, out var main)) s.Clients[sub] = main;
            else s.Report.Warn(ColavoraEntities.Clients, sub, $"Se une al cliente {link.MainSender}, que no está migrado; sus envíos se omiten.");
        }
    }

    private async Task LoadExistingClientsAsync(RunState s, CancellationToken ct)
    {
        var customers = await s.Reader.ReadCustomersAsync(s.CompanyId, ct);
        await ResolveSubClientsAsync(s, customers, ct);
        var existing = s.TenantId is null ? new Dictionary<string, Client>(StringComparer.OrdinalIgnoreCase)
            : await db.Clients.AsNoTracking().ToDictionaryAsync(c => c.Code, c => c, StringComparer.OrdinalIgnoreCase, ct);
        foreach (var cu in customers)
        {
            s.SenderCodeById[cu.SenderId] = cu.Sender;
            if (s.SubClients.ContainsKey(cu.Sender)) continue;
            if (existing.TryGetValue(ColavoraRules.ClientCode(cu.Sender), out var c))
                s.Clients[cu.Sender] = new ClientRef(c.ClientId, c.PublicId, c.Code, c.Name, c.DefaultPickupLocationId, null);
        }
        LinkSubClients(s);
    }

    // ================================================================ consignatarios (D7: todos tal cual, directo por EF en lotes)

    private async Task ImportConsigneesAsync(RunState s, CancellationToken ct)
    {
        const string L = ColavoraEntities.Consignees;
        var rows = await s.Reader.ReadConsigneesAsync(s.CompanyId, ct);
        var tenantId = s.TenantId ?? 0;
        // existentes: por (cliente, código) y por (cliente, nombre, línea 1, ZIP) para los que no tienen número
        var existingByCode = new Dictionary<(int, string), int>();
        var existingByAddress = new HashSet<(int, string, string, string)>();
        if (s.TenantId is not null)
            foreach (var l in await db.Locations.AsNoTracking().Where(l => l.ClientId != null && l.LocationTypeLookupId == s.LocationTypeDeliveryId)
                         .Select(l => new { l.LocationId, l.ClientId, l.Code, l.Name, l.Line1, l.PostalCode }).ToListAsync(ct))
            {
                if (!string.IsNullOrWhiteSpace(l.Code)) existingByCode.TryAdd((l.ClientId!.Value, l.Code), l.LocationId);
                existingByAddress.Add((l.ClientId!.Value, Key(l.Name), Key(l.Line1), l.PostalCode ?? string.Empty));
            }
        foreach (var ((cid, code), id) in existingByCode) s.ConsigneesByNumber[(cid, code)] = id;

        var codesSeen = new HashSet<(int, string)>(existingByCode.Keys);
        var pending = new List<(Location Loc, CvConsignee Src)>();
        var done = 0;
        foreach (var c in rows)
        {
            s.Report.CountRead(L);
            if (!s.SenderCodeById.TryGetValue(c.SenderId, out var sender) || !s.Clients.TryGetValue(sender, out var client))
            {
                s.Report.CountSkipped(L);
                s.Report.Warn(L, c.Id.ToString(CultureInfo.InvariantCulture), "El consignatario pertenece a un cliente que no se migró; se omite.");
                continue;
            }
            var number = ColavoraRules.Trim(c.Number, 40);
            var name = ColavoraRules.Trim(ColavoraRules.FirstNonEmpty(c.CompanyName, c.ContactName, number, "Consignatario"), 200)!;
            var line1 = ColavoraRules.Trim(ColavoraRules.FirstNonEmpty(c.Address1, c.Address2, name), 200)!;
            var line2 = string.Equals(line1, ColavoraRules.Trim(c.Address2, 200), StringComparison.Ordinal) ? null : ColavoraRules.Trim(c.Address2, 200);
            var zip = ColavoraRules.NormalizeZip(c.PostalCode);
            var city = ColavoraRules.Trim(c.City, 100) ?? (zip is not null && s.CityByZip.TryGetValue(zip, out var zc) ? zc : "—");

            if (number is not null && existingByCode.TryGetValue((client.ClientId, number), out var foundId)) { s.Report.CountExisting(L); s.ConsigneesByNumber[(client.ClientId, number)] = foundId; continue; }
            if (number is null && existingByAddress.Contains((client.ClientId, Key(name), Key(line1), zip ?? string.Empty))) { s.Report.CountExisting(L); continue; }
            string? code = number;
            if (code is not null && !codesSeen.Add((client.ClientId, code)))
            {
                s.Report.Warn(L, $"{sender}/{number}", "Número de consignatario repetido dentro del cliente; el segundo se crea sin código.");
                code = null;
            }
            s.Report.CountCreated(L);
            if (s.DryRun) continue;

            var loc = new Location
            {
                TenantId = tenantId, ClientId = client.ClientId, Code = code, Name = name, LocationTypeLookupId = s.LocationTypeDeliveryId,
                Line1 = line1, Line2 = line2, City = city, State = ColavoraRules.NormalizeState(c.State), PostalCode = zip, CountryLookupId = s.CountryPrId,
                DefaultServiceMinutes = 0, DeliveryNotes = ColavoraRules.Trim(c.Comments, 500), ContactName = ColavoraRules.Trim(c.ContactName, 150),
                IsActive = c.Archived != true, CreatedAtUtc = ColavoraRules.ToUtc(c.CreationDate) ?? DateTime.UtcNow,
            };
            // Lat/long de Colavora (722 consignatarios): Location no mapea GeoPoint en Teikem; se conservan en las notas de acceso.
            if (!string.IsNullOrWhiteSpace(c.Latitude) && !string.IsNullOrWhiteSpace(c.Longitude))
                loc.AccessNotes = ColavoraRules.Trim($"GPS Colavora: {c.Latitude}, {c.Longitude}", 500);
            db.Locations.Add(loc);
            pending.Add((loc, c));
            existingByAddress.Add((client.ClientId, Key(name), Key(line1), zip ?? string.Empty));
            if (pending.Count >= 500) { await FlushConsigneesAsync(s, pending, ct); done += pending.Count; pending.Clear(); logger.LogInformation("Consignatarios: {Done}.", done); }
        }
        if (pending.Count > 0) await FlushConsigneesAsync(s, pending, ct);
        db.ChangeTracker.Clear();

        static string Key(string? v) => (v ?? string.Empty).Trim().ToUpperInvariant();
    }

    /// <summary>Guarda el lote de consignatarios y sus contactos (teléfono y correo), sin auditoría.</summary>
    private async Task FlushConsigneesAsync(RunState s, List<(Location Loc, CvConsignee Src)> pending, CancellationToken ct)
    {
        db.SuppressAudit = true;
        try
        {
            await db.SaveChangesAsync(ct);
            foreach (var (loc, src) in pending)
            {
                if (loc.Code is not null) s.ConsigneesByNumber[(loc.ClientId!.Value, loc.Code)] = loc.LocationId;
                if (!string.IsNullOrWhiteSpace(src.Phone))
                {
                    var phones = ColavoraRules.ParsePhones(src.Phone);
                    foreach (var w in phones.Warnings) s.Report.Warn(ColavoraEntities.Consignees, loc.Code ?? src.Id.ToString(CultureInfo.InvariantCulture), w);
                    for (var i = 0; i < phones.Numbers.Count; i++)
                        db.ContactPoints.Add(new ContactPoint
                        {
                            TenantId = loc.TenantId, OwnerEntityLookupId = s.EntityTypeLocationId, OwnerId = loc.LocationId, ContactTypeLookupId = s.ContactPhoneId,
                            Value = phones.Numbers[i].National, CountryCallingCode = phones.Numbers[i].CountryCallingCode, Extension = i == 0 ? phones.Extension : null,
                            Label = i == 0 ? null : "Otro", IsPrimary = i == 0, IsActive = true,
                        });
                }
                if (!string.IsNullOrWhiteSpace(src.Email))
                    foreach (var email in src.Email.Split(new[] { ';', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(3))
                        db.ContactPoints.Add(new ContactPoint { TenantId = loc.TenantId, OwnerEntityLookupId = s.EntityTypeLocationId, OwnerId = loc.LocationId, ContactTypeLookupId = s.ContactEmailId, Value = ColavoraRules.Trim(email, 200)!, IsPrimary = false, IsActive = true });
            }
            await db.SaveChangesAsync(ct);
        }
        finally { db.SuppressAudit = false; db.ChangeTracker.Clear(); }
    }

    private async Task LoadExistingConsigneesAsync(RunState s, CancellationToken ct)
    {
        if (s.TenantId is null) return;
        foreach (var l in await db.Locations.AsNoTracking().Where(l => l.ClientId != null && l.Code != null && l.LocationTypeLookupId == s.LocationTypeDeliveryId).Select(l => new { l.LocationId, l.ClientId, l.Code }).ToListAsync(ct))
            s.ConsigneesByNumber.TryAdd((l.ClientId!.Value, l.Code!), l.LocationId);
    }

    // ================================================================ contratos y tarifas (D13: contrato por cliente + precio congelado por orden)

    private async Task ImportContractsAsync(RunState s, CancellationToken ct)
    {
        const string K = ColavoraEntities.Contracts;
        var prices = (await s.Reader.ReadServicePricesAsync(s.CompanyId, ct)).GroupBy(p => p.SenderId).ToDictionary(g => g.Key, g => g.ToList());
        var existingContracts = s.TenantId is null ? new Dictionary<int, Contract>()
            : (await db.Contracts.AsNoTracking().Where(c => c.IsActive).ToListAsync(ct)).GroupBy(c => c.ClientId).ToDictionary(g => g.Key, g => g.OrderBy(c => c.ContractId).First());
        var baseTypeId = await lookups.GetIdAsync(LookupDomains.RateComponentType, RateComponentTypes.BaseFreight, ct);
        var extraTypeId = await lookups.GetIdAsync(LookupDomains.RateComponentType, RateComponentTypes.ExtraPiece, ct);
        var perShipmentId = await lookups.GetIdAsync(LookupDomains.RateBasis, RateBases.PerShipment, ct);
        var perPieceId = await lookups.GetIdAsync(LookupDomains.RateBasis, RateBases.PerPiece, ct);
        var fixedId = await lookups.GetIdAsync(LookupDomains.PricingMode, PricingModes.Fixed, ct);
        var tieredId = await lookups.GetIdAsync(LookupDomains.PricingMode, PricingModes.Tiered, ct);
        var graduatedId = await lookups.GetIdAsync(LookupDomains.TierMode, TierModes.Graduated, ct);

        // tarifas de las variantes por servicio (NEXT DAY / SAME DAY): se suman a las del principal; si el mismo servicio y paquete ya está, gana el del principal
        var extraRows = new Dictionary<string, List<CvServicePrice>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (variant, vlink) in s.SubClients.Where(kv => kv.Value.Kind == ColavoraRules.SubClientKind.Variant))
        {
            var vid = s.SenderCodeById.FirstOrDefault(kv => string.Equals(kv.Value, variant, StringComparison.OrdinalIgnoreCase)).Key;
            if (!extraRows.TryGetValue(vlink.MainSender, out var list)) extraRows[vlink.MainSender] = list = new List<CvServicePrice>();
            list.AddRange(prices.GetValueOrDefault(vid) ?? new List<CvServicePrice>());
        }

        foreach (var (sender, client) in s.Clients.ToList())
        {
            if (s.SubClients.TryGetValue(sender, out var subLink))
            {
                if (subLink.Kind == ColavoraRules.SubClientKind.Variant) { s.Report.CountSkipped(K); continue; }   // sus tarifas ya se sumaron al contrato del principal (extraRows)
                // «Recogido/Devolución»: comparten el contrato del cliente real; sus tarifas NO se migran solas (precios distintos y de prueba: 1.00, 5.75, 100.00) — se avisa para decidir
                var subId = s.SenderCodeById.FirstOrDefault(kv => string.Equals(kv.Value, sender, StringComparison.OrdinalIgnoreCase)).Key;
                var subRows = prices.GetValueOrDefault(subId) ?? new List<CvServicePrice>();
                s.Report.CountSkipped(K);
                // decisión de Luis (2026-10-21): no se migran. Son marcadores (1.00 / 5.75 / 100.00) que nunca se cobraron: ningún «Recogido» tiene órdenes, y de las 4 órdenes de
                // «Devolución» una sola trae un precio (5.75); el cliente real (10532) tiene todas sus tarifas en 0.00. Las devoluciones son reclamaciones y solo llevan una nota.
                if (subRows.Count > 0)
                    s.Report.AddInfo($"Tarifas {sender}", $"cliente unido a {subLink.MainSender}: sus {subRows.Count} tarifas no se migran (" + string.Join("; ", subRows.Take(6).Select(r => $"servicio {r.ServiceType}/paquete {r.PackageType} = {r.Price:0.00}")) + ") porque nunca se cobraron.");
                continue;
            }
            s.Report.CountRead(K);
            if (existingContracts.TryGetValue(client.ClientId, out var found))
            {
                s.Report.CountExisting(K);
                s.Clients[sender] = client with { ContractId = found.ContractId };
                continue;
            }
            var senderId = s.SenderCodeById.FirstOrDefault(kv => string.Equals(kv.Value, sender, StringComparison.OrdinalIgnoreCase)).Key;
            var rows = prices.GetValueOrDefault(senderId) ?? new List<CvServicePrice>();
            if (extraRows.TryGetValue(sender, out var more))
            {
                var have = rows.Select(r => (r.ServiceType, r.PackageType)).ToHashSet();
                rows = rows.Concat(more.Where(r => !have.Contains((r.ServiceType, r.PackageType)))).ToList();
            }
            if (s.DryRun) { s.Report.CountCreated(K); continue; }

            var dto = await TryAsync(s, K, sender, () => contractService.CreateAsync(new ContractCreateRequest(client.PublicId, null, $"Contrato {client.Name}",
                new DateOnly(2023, 8, 1), null, false, "USD", null, "Migrado de Colavora Courier (tarifas de Customers_Service_Price)."), ct));
            if (dto is null) continue;
            s.Report.CountCreated(K);
            s.Clients[sender] = client with { ContractId = dto.Id };

            // tarifas: por (servicio, paquete): la primera franja = tarifa por servicio; franjas siguientes = pieza extra por tramos
            var contract = await db.Contracts.FirstAsync(c => c.ContractId == dto.Id, ct);
            var anyExtra = false;
            foreach (var g in rows.GroupBy(r => (r.ServiceType, r.PackageType)))
            {
                if (!s.ServiceTypes.TryGetValue(g.Key.ServiceType, out var st) || !s.PackageTypes.TryGetValue(g.Key.PackageType, out var pt))
                {
                    s.Report.Warn(K, $"{sender} servicio {g.Key.ServiceType}/paquete {g.Key.PackageType}", "La tarifa usa un servicio o paquete que no existe en Colavora; se omite.");
                    continue;
                }
                var ordered = g.OrderBy(r => r.ItemFrom).ToList();
                var first = ordered[0];
                db.RateComponents.Add(new RateComponent
                {
                    TenantId = contract.TenantId, ContractId = contract.ContractId, ComponentTypeLookupId = baseTypeId, BasisLookupId = perShipmentId, PricingModeLookupId = fixedId,
                    ServiceTypeLookupId = st.LookupId, PackageTypeLookupId = pt.LookupId, FlatAmount = first.Price, EffectiveFrom = new DateOnly(2023, 8, 1), IsActive = true,
                });
                if (ordered.Count > 1)
                {
                    anyExtra = true;
                    var extra = new RateComponent
                    {
                        TenantId = contract.TenantId, ContractId = contract.ContractId, ComponentTypeLookupId = extraTypeId, BasisLookupId = perPieceId, PricingModeLookupId = tieredId, TierModeLookupId = graduatedId,
                        ServiceTypeLookupId = st.LookupId, PackageTypeLookupId = pt.LookupId, EffectiveFrom = new DateOnly(2023, 8, 1), IsActive = true,
                    };
                    var sort = 0;
                    foreach (var r in ordered.Skip(1))
                        extra.Tiers.Add(new RateTier { MinValue = r.ItemFrom, MaxValue = r.ItemTo >= 9999 ? null : r.ItemTo, UnitAmount = r.Price, SortOrder = ++sort, EffectiveFrom = new DateOnly(2023, 8, 1) });
                    db.RateComponents.Add(extra);
                }
            }
            var codFee = rows.Select(r => r.CodFee).Where(f => f > 0).DefaultIfEmpty(0).Max();
            var dispatchFee = rows.Where(r => r.HasDispatchCharge && r.DispatchFee is > 0).Select(r => r.DispatchFee!.Value).DefaultIfEmpty(0).Max();
            contract.BillExtraPiece = anyExtra;
            if (codFee > 0) { contract.BillCodFee = true; contract.CodFeeTypeLookupId = await lookups.GetIdAsync(LookupDomains.PricingType, PricingTypes.Fixed, ct); contract.CodFeeValue = codFee; }
            if (dispatchFee > 0) { contract.BillDispatchFee = true; contract.DispatchFee = dispatchFee; }
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
            if (rows.Count > 0) s.Report.AddInfo($"Tarifas {client.Code}", $"{rows.Count} filas → {rows.GroupBy(r => (r.ServiceType, r.PackageType)).Count()} tarifas{(codFee > 0 ? $", cargo COD {codFee:0.00}" : "")}{(dispatchFee > 0 ? $", despacho {dispatchFee:0.00}" : "")}");
        }
        LinkSubClients(s);   // los «Recogido/Devolución» comparten el contrato del cliente real
    }

    private async Task LoadExistingContractsAsync(RunState s, CancellationToken ct)
    {
        if (s.TenantId is null) return;
        var byClient = (await db.Contracts.AsNoTracking().Where(c => c.IsActive).ToListAsync(ct)).GroupBy(c => c.ClientId).ToDictionary(g => g.Key, g => g.OrderBy(c => c.ContractId).First().ContractId);
        foreach (var (sender, client) in s.Clients.ToList())
            if (byClient.TryGetValue(client.ClientId, out var cid)) s.Clients[sender] = client with { ContractId = cid };
        LinkSubClients(s);
    }

    // ================================================================ campos personalizados (D6 división; id de Colavora para rastrear)

    /// <summary>
    /// Las divisiones de la compañía: las que define Colavora más las que traen las órdenes y no están en esa lista. Decisión de Luis (2026-10-21): si la división
    /// fuera de lista se usó hace poco (últimos <c>StaleDivisionDays</c> días) es una división real que faltaba y se agrega; si es vieja, sus órdenes son un error.
    /// </summary>
    private async Task<List<CvDivision>> LoadDivisionRulesAsync(RunState s, CancellationToken ct)
    {
        var divisions = await s.Reader.ReadDivisionsAsync(s.CompanyId, ct);
        if (s.DivisionRulesLoaded) { return divisions.Concat(s.DivisionOptions.Where(o => divisions.All(d => !string.Equals(d.Code, o, StringComparison.OrdinalIgnoreCase))).Select(o => new CvDivision(o, null))).ToList(); }
        s.DivisionRulesLoaded = true;
        foreach (var d in divisions) s.DivisionOptions.Add(d.Code);
        var limit = s.MigrationUtc.AddDays(-s.Config.Options.StaleDivisionDays);
        var extras = new List<CvDivision>();
        foreach (var u in (await s.Reader.ReadDivisionUsageAsync(s.CompanyId, ct)).OrderByDescending(u => u.Orders))
        {
            if (s.DivisionOptions.Contains(u.Code)) continue;
            if (u.LastCreated is DateTime last && ColavoraRules.ToUtc(last) >= limit)
            {
                s.DivisionOptions.Add(u.Code);
                extras.Add(new CvDivision(u.Code, u.Code));
                s.Report.AddInfo($"División {u.Code}", $"no está en la lista de Colavora pero se usa ({u.Orders:N0} envíos, el último {last:yyyy-MM-dd}); se agrega a las divisiones.");
            }
            else
            {
                s.StaleDivisions.Add(u.Code);
                s.Report.Warn(ColavoraEntities.CustomFields, u.Code, $"División fuera de la lista de Colavora y sin uso reciente ({u.Orders:N0} envíos, el último {u.LastCreated:yyyy-MM-dd}): esos envíos se reportan como error y no se migran.");
            }
        }
        return divisions.Concat(extras).ToList();
    }

    private async Task ImportCustomFieldsAsync(RunState s, CancellationToken ct)
    {
        const string F = ColavoraEntities.CustomFields;
        var divisions = await LoadDivisionRulesAsync(s, ct);
        var defs = s.TenantId is null ? new List<CustomFieldDefinitionDto>() : (await customFields.GetDefinitionsAsync(EntityTypes.TransportOrder, includeInactive: true, ct)).ToList();

        async Task<int?> Ensure(string key, string labelEs, string labelEn, string dataType, IList<CustomFieldOptionUpsert>? options, bool showInList)
        {
            s.Report.CountRead(F);
            var found = defs.FirstOrDefault(x => string.Equals(x.FieldKey, key, StringComparison.OrdinalIgnoreCase));
            if (found is not null) { s.Report.CountExisting(F); return found.Id; }
            s.Report.CountCreated(F);
            if (s.DryRun) return null;
            var dto = await TryAsync(s, F, key, () => customFields.CreateDefinitionAsync(EntityTypes.TransportOrder, new CustomFieldDefinitionUpsert(key,
                new Dictionary<string, string> { ["es"] = labelEs, ["en"] = labelEn }, null, dataType, false, false, null, null, null, showInList, null, options), ct));
            return dto?.Id;
        }

        var options = divisions.Select((d, i) => new CustomFieldOptionUpsert(d.Code, new Dictionary<string, string> { ["es"] = d.Description ?? d.Code, ["en"] = d.Description ?? d.Code }, i + 1)).ToList();
        if (divisions.Count > 0 || s.CompanyId == "10029")
            s.DivisionFieldId = await Ensure(s.Config.Options.DivisionFieldKey, "División", "Division", CustomFieldDataTypes.Select, options.Count > 0 ? options : null, true);
        s.ColavoraIdFieldId = await Ensure(s.Config.Options.ColavoraIdFieldKey, "ID en Colavora", "Colavora ID", CustomFieldDataTypes.Number, null, false);
    }
}
