namespace ForgeFlow.Domain.Common;

/// <summary>
/// Marks a domain entity that belongs to an organization tenant boundary.
/// Used by EF Core global query filters to enforce automatic tenant isolation (Guardrail FFA-010).
/// </summary>
public interface IOrganizationEntity
{
    long OrganizationId { get; }
}
