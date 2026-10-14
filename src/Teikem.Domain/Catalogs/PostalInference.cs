namespace Teikem.Domain.Catalogs;

/// <summary>
/// Lote 46 (F-B3): fila del catálogo de localidades postales ya preparada para buscar (país por código, ciudad y municipio
/// normalizados con PostalLocalityRules.Normalize). Order = PostalLocalityId (orden fijo cuando un ZIP tiene varias filas).
/// </summary>
public sealed record PostalEntry(int Order, string PostalCode, string City, string? State, string? Municipality, string Country, string? ZipClass,
    string CityKey, string? MunicipalityKey)
{
    /// <summary>Pueblo para la dirección: en Puerto Rico el municipio (con acentos); si no hay, la ciudad postal.</summary>
    public string Town => string.IsNullOrWhiteSpace(Municipality) ? City : Municipality!;

    public static PostalEntry Create(int order, string postalCode, string city, string? state, string? municipality, string country, string? zipClass)
        => new(order, postalCode.Trim(), city, state, municipality, country.Trim().ToUpperInvariant(), string.IsNullOrWhiteSpace(zipClass) ? null : zipClass.Trim().ToUpperInvariant(),
            PostalLocalityRules.Normalize(city), string.IsNullOrWhiteSpace(municipality) ? null : PostalLocalityRules.Normalize(municipality));
}

/// <summary>
/// Lote 46 (F-B3): índice en memoria del catálogo de localidades postales por (país, ZIP de 5 dígitos), (país, ciudad postal) y
/// (país, municipio). Lo arma PostalLocalityService una vez por carga del catálogo; las reglas de inferencia son puras.
/// </summary>
public sealed class PostalIndex
{
    private readonly Dictionary<(string Country, string Zip), List<PostalEntry>> _byZip = new();
    private readonly Dictionary<(string Country, string Key), List<PostalEntry>> _byCity = new();
    private readonly Dictionary<(string Country, string Key), List<PostalEntry>> _byMunicipality = new();
    private readonly HashSet<string> _countries = new(StringComparer.Ordinal);

    public PostalIndex(IEnumerable<PostalEntry> entries)
    {
        foreach (var e in entries.OrderBy(e => e.Order))
        {
            _countries.Add(e.Country);
            if (PostalInference.TryZip5(e.PostalCode, out var zip)) Add(_byZip, (e.Country, zip), e);
            if (e.CityKey.Length > 0) Add(_byCity, (e.Country, e.CityKey), e);
            if (!string.IsNullOrEmpty(e.MunicipalityKey)) Add(_byMunicipality, (e.Country, e.MunicipalityKey), e);
        }
    }

    public static readonly PostalIndex Empty = new(Array.Empty<PostalEntry>());

    private static void Add<TKey>(Dictionary<TKey, List<PostalEntry>> map, TKey key, PostalEntry e) where TKey : notnull
    {
        if (!map.TryGetValue(key, out var list)) map[key] = list = new List<PostalEntry>();
        list.Add(e);
    }

    /// <summary>¿El catálogo tiene códigos de ese país? (la inferencia solo aplica a esos países: Puerto Rico y Estados Unidos).</summary>
    public bool Covers(string? country) => country is not null && _countries.Contains(country.Trim().ToUpperInvariant());

    /// <summary>Filas del ZIP (5 dígitos) en el país, en orden fijo; vacío si no está.</summary>
    public IReadOnlyList<PostalEntry> ByZip(string country, string zip5)
        => _byZip.TryGetValue((country.Trim().ToUpperInvariant(), zip5), out var l) ? l : Array.Empty<PostalEntry>();

    /// <summary>Filas del pueblo (ya normalizado): primero por ciudad postal; si no hay, por municipio (Puerto Rico).</summary>
    public IReadOnlyList<PostalEntry> ByTown(string country, string townKey)
    {
        var c = country.Trim().ToUpperInvariant();
        if (_byCity.TryGetValue((c, townKey), out var byCity)) return byCity;
        return _byMunicipality.TryGetValue((c, townKey), out var byMunicipality) ? byMunicipality : Array.Empty<PostalEntry>();
    }

    /// <summary>
    /// ZIP de ENTREGA de las filas dadas, en orden fijo determinista: STANDARD, luego UNIQUE, luego sin clasificar; a igual tipo, el
    /// menor código. Null si ninguna es de entrega.
    /// </summary>
    public static PostalEntry? FirstDelivery(IEnumerable<PostalEntry> candidates)
        => candidates.Where(e => ZipClasses.DeliveryRank(e.ZipClass) is not null)
            .OrderBy(e => ZipClasses.DeliveryRank(e.ZipClass))
            .ThenBy(e => e.PostalCode, StringComparer.Ordinal)
            .ThenBy(e => e.Order)
            .FirstOrDefault();
}

/// <summary>Resultado de la inferencia de una fila: valores finales, qué se completó, el ZIP original si se sustituyó, avisos y error (si la fila se rechaza).</summary>
public sealed record PostalInferenceResult(
    string? City,
    string? State,
    string? PostalCode,
    IReadOnlyList<string> Inferred,
    string? OriginalPostalCode,
    IReadOnlyList<string> Warnings,
    string? ErrorField,
    string? Error)
{
    public bool IsRejected => Error is not null;
}

/// <summary>
/// Lote 46 (F-B3): completa pueblo, estado y código postal de una dirección de ENTREGA (consignatario importado) con el catálogo
/// de localidades postales, sin reescribir nada en silencio (cada ajuste deja un aviso y el campo en Inferred):
/// - ZIP → pueblo (municipio en Puerto Rico) y estado, si faltan. Los ZIP se comparan como texto de 5 dígitos (ZIP+4 vale por sus 5).
/// - Solo pueblo → el PRIMER ZIP de entrega del pueblo (STANDARD, luego UNIQUE; el menor código). Pueblos sin acentos ni mayúsculas.
/// - ZIP y pueblo → se guardan los dos; si no coinciden, aviso (no error).
/// - ZIP de apartados (PO_BOX) o militar (MILITARY) → se sustituye por un ZIP de entrega de la MISMA ciudad, con aviso «Código postal
///   ajustado» y el original; si no hay, la fila se rechaza.
/// - ZIP o pueblo que no están en el catálogo, o pueblo sin ZIP de entrega → la fila se rechaza con el mensaje del campo.
/// Países que el catálogo no cubre: no se toca nada. Nunca se inventan datos (ni coordenadas).
/// </summary>
public static class PostalInference
{
    public static string ZipInvalidMessage(string zip) => $"El código postal {zip} no es válido: use 5 dígitos (o ZIP+4, como 00901-1234).";
    public static string ZipNotFoundMessage(string zip) => $"El código postal {zip} no está en el catálogo de códigos postales; corríjalo, o déjelo vacío e indique el pueblo.";
    public static string TownNotFoundMessage(string town) => $"El pueblo '{town}' no está en el catálogo de códigos postales; corríjalo o indique el código postal.";
    public static string TownWithoutDeliveryZipMessage(string town) => $"El pueblo '{town}' no tiene un código postal de entrega (solo de apartados postales o militares); indique el código postal de la dirección física.";
    public static string TownInSeveralStatesMessage(string town, string states) => $"El pueblo '{town}' existe en varios estados ({states}); indique el estado o el código postal.";
    public static string NoDeliveryReplacementMessage(string zip, string zipClass, string town)
        => $"El código postal {zip} es de {KindLabel(zipClass)} y {town} no tiene un código postal de entrega con que sustituirlo; indique el código postal de la dirección física.";

    public static string CityFromZipWarning(string zip, string town) => $"Pueblo tomado del código postal {zip}: {town}.";
    public static string StateFromCatalogWarning(string state) => $"Estado tomado del catálogo de códigos postales: {state}.";
    public static string ZipFromTownWarning(string zip, string town) => $"Código postal asignado: {zip}, el primero de entrega de {town}.";
    public static string ZipAdjustedWarning(string original, string zipClass, string zip, string town)
        => $"Código postal ajustado: {original} es de {KindLabel(zipClass)}; se usó {zip}, de entrega en {town}.";
    public static string ZipTownMismatchWarning(string zip, string zipTown, string town) => $"El código postal {zip} es de {zipTown}, no de {town}; se guardan los dos como vienen.";
    public static string StateMismatchWarning(string state, string catalogState) => $"El estado {state} no coincide con el del código postal ({catalogState}); se guarda como viene.";

    /// <summary>Nombre del tipo de ZIP que no es de entrega, para los mensajes.</summary>
    public static string KindLabel(string zipClass) => zipClass switch
    {
        ZipClasses.PoBox => "apartados postales (P.O. Box)",
        ZipClasses.Military => "correo militar (APO/FPO)",
        _ => zipClass,
    };

    /// <summary>ZIP de 5 dígitos del texto: «00901», «00901-1234» o «009011234» → «00901». Otro formato → false.</summary>
    public static bool TryZip5(string? text, out string zip5)
    {
        zip5 = string.Empty;
        var t = text?.Trim();
        if (string.IsNullOrEmpty(t)) return false;
        if (t.Length == 5 && t.All(char.IsAsciiDigit)) { zip5 = t; return true; }
        if (t.Length == 9 && t.All(char.IsAsciiDigit)) { zip5 = t[..5]; return true; }
        if (t.Length == 10 && t[5] == '-' && t[..5].All(char.IsAsciiDigit) && t[6..].All(char.IsAsciiDigit)) { zip5 = t[..5]; return true; }
        return false;
    }

    public static PostalInferenceResult Infer(PostalIndex index, string country, string? city, string? state, string? postalCode)
    {
        ArgumentNullException.ThrowIfNull(index);
        city = string.IsNullOrWhiteSpace(city) ? null : city.Trim();
        state = string.IsNullOrWhiteSpace(state) ? null : state.Trim();
        postalCode = string.IsNullOrWhiteSpace(postalCode) ? null : postalCode.Trim();
        var inferred = new List<string>();
        var warnings = new List<string>();
        PostalInferenceResult Ok(string? c, string? s, string? z, string? original = null) => new(c, s, z, inferred, original, warnings, null, null);
        PostalInferenceResult Reject(string field, string message) => new(city, state, postalCode, Array.Empty<string>(), null, Array.Empty<string>(), field, message);

        if (!index.Covers(country) || (postalCode is null && city is null)) return Ok(city, state, postalCode);

        PostalEntry chosen;
        string? original = null;
        if (postalCode is not null)
        {
            if (!TryZip5(postalCode, out var zip5)) return Reject(Fields.PostalCode, ZipInvalidMessage(postalCode));
            var rows = index.ByZip(country, zip5);
            if (rows.Count == 0) return Reject(Fields.PostalCode, ZipNotFoundMessage(postalCode));
            chosen = rows[0];
            var zipClass = rows.Select(r => r.ZipClass).FirstOrDefault(c => c is not null);
            if (!ZipClasses.IsDelivery(zipClass))
            {
                // Misma ciudad: la ciudad postal del ZIP; si no tiene ZIP de entrega, su municipio (Puerto Rico).
                var replacement = PostalIndex.FirstDelivery(rows.SelectMany(r => index.ByTown(country, r.CityKey)))
                                  ?? PostalIndex.FirstDelivery(rows.Where(r => r.MunicipalityKey is not null).SelectMany(r => index.ByTown(country, r.MunicipalityKey!)));
                if (replacement is null) return Reject(Fields.PostalCode, NoDeliveryReplacementMessage(postalCode, zipClass!, chosen.Town));
                original = postalCode;
                chosen = replacement;
                postalCode = replacement.PostalCode;
                inferred.Add(Fields.PostalCode);
                warnings.Add(ZipAdjustedWarning(original, zipClass!, replacement.PostalCode, replacement.Town));
                rows = index.ByZip(country, replacement.PostalCode);
            }

            if (city is null)
            {
                city = chosen.Town;
                inferred.Add(Fields.City);
                warnings.Add(CityFromZipWarning(postalCode, city));
            }
            else
            {
                var key = PostalLocalityRules.Normalize(city);
                var originalRows = original is not null && TryZip5(original, out var o5) ? index.ByZip(country, o5) : Array.Empty<PostalEntry>();
                if (!rows.Concat(originalRows).Any(r => r.CityKey == key || r.MunicipalityKey == key))
                    warnings.Add(ZipTownMismatchWarning(postalCode, chosen.Town, city));
            }
        }
        else
        {
            var townKey = PostalLocalityRules.Normalize(city);
            var candidates = index.ByTown(country, townKey);
            if (candidates.Count == 0) return Reject(Fields.City, TownNotFoundMessage(city!));
            if (state is not null)
            {
                var sameState = candidates.Where(c => SameState(state, c.State)).ToList();
                if (sameState.Count > 0) candidates = sameState;
            }
            var states = candidates.Select(c => c.State ?? string.Empty).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.Ordinal).ToList();
            if (states.Count > 1) return Reject(Fields.City, TownInSeveralStatesMessage(city!, string.Join(", ", states)));
            var best = PostalIndex.FirstDelivery(candidates);
            if (best is null) return Reject(Fields.City, TownWithoutDeliveryZipMessage(city!));
            chosen = best;
            postalCode = best.PostalCode;
            inferred.Add(Fields.PostalCode);
            warnings.Add(ZipFromTownWarning(postalCode, city!));
        }

        if (state is null)
        {
            if (!string.IsNullOrWhiteSpace(chosen.State))
            {
                state = chosen.State;
                inferred.Add(Fields.State);
                warnings.Add(StateFromCatalogWarning(state));
            }
        }
        else if (!string.IsNullOrWhiteSpace(chosen.State) && !SameState(state, chosen.State))
            warnings.Add(StateMismatchWarning(state, chosen.State));

        return Ok(city, state, postalCode, original);
    }

    /// <summary>Estado igual sin acentos ni mayúsculas; «Puerto Rico» vale por «PR».</summary>
    private static bool SameState(string? a, string? b)
    {
        static string Key(string? s)
        {
            var n = PostalLocalityRules.Normalize(s);
            return n == "PUERTO RICO" ? "PR" : n;
        }
        return Key(a) == Key(b);
    }

    /// <summary>Nombres de los campos de la fila (los mismos de ConsigneeImportFields, sin depender de ese catálogo).</summary>
    public static class Fields
    {
        public const string City = "city";
        public const string State = "state";
        public const string PostalCode = "postalCode";
    }
}
