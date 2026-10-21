namespace Teikem.Infrastructure.Contracts;

public sealed record ContactPointDto(int Id, string OwnerEntity, int OwnerId, string ContactType, string ContactTypeLabel, string Value, string? Extension, string? Label, bool IsPrimary, bool IsActive, string? CountryCallingCode = null);
public sealed record ContactPointUpsertRequest(string ContactType, string Value, string? Extension, string? Label, bool IsPrimary, string? CountryCallingCode = null);
