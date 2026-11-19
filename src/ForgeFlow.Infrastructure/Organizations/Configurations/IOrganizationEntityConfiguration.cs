namespace ForgeFlow.Infrastructure.Organizations.Configurations;

/// <summary>
/// Marker interface for entity configurations belonging strictly to OrganizationDbContext.
/// Prevents cross-context model discovery bleed into AuthDbContext or DiagnosticsDbContext.
/// </summary>
public interface IOrganizationEntityConfiguration
{
}
