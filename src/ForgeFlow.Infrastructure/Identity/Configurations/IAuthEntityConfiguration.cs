namespace ForgeFlow.Infrastructure.Identity.Configurations;

/// <summary>
/// Marker interface for entity configurations belonging strictly to AuthDbContext.
/// Prevents cross-context model discovery bleed into OrganizationDbContext or DiagnosticsDbContext.
/// </summary>
public interface IAuthEntityConfiguration
{
}
