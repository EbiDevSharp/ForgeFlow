using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ForgeFlow.Infrastructure.Organizations;

/// <summary>Offline model/scaffolding only. Default address cannot touch a real database.</summary>
public sealed class OrganizationDesignTimeFactory : IDesignTimeDbContextFactory<OrganizationDbContext>
{
    public OrganizationDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<OrganizationDbContext>()
        .UseSqlServer("Server=invalid.invalid;Database=ForgeFlow_DesignOnly;Integrated Security=true;Encrypt=true;Connect Timeout=1")
        .Options);
}
