using ForgeFlow.Application.Organizations;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class OrganizationAccessContractTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Missing_or_invalid_scope_identifiers_cannot_be_constructed(long id)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new OrganizationScope.Organization(id));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OrganizationScope.Site(id, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OrganizationScope.Site(1, id));
    }

    [Fact]
    public void Requested_scope_is_explicit_and_never_a_default_platform_grant()
    {
        var scope = new OrganizationScope.Site(10, 20);
        Assert.Equal(10, scope.OrganizationId);
        Assert.Equal(20, scope.SiteId);
        Assert.False(default(PermissionDecision).IsAllowed);
        // Constructing a scope/actor shape is not membership or permission evaluation.
        Assert.Equal("Organizations.Create", OrganizationPermissions.OrganizationsCreate);
        Assert.Equal("Sites.SetActive", OrganizationPermissions.SitesSetActive);
    }

    [Fact]
    public void Actor_contract_requires_positive_identity_and_explicit_provenance()
    {
        var reference = Guid.NewGuid();
        var correlation = Guid.NewGuid();
        Assert.Throws<ArgumentOutOfRangeException>(() => new TrustedActor(0, ActorProvenance.AuthenticatedSession, reference, correlation));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TrustedActor(-1, ActorProvenance.AuthenticatedSession, reference, correlation));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TrustedActor(1, (ActorProvenance)0, reference, correlation));
        Assert.Throws<ArgumentException>(() => new TrustedActor(1, ActorProvenance.AuthenticatedSession, Guid.Empty, correlation));
        Assert.Throws<ArgumentException>(() => new TrustedActor(1, ActorProvenance.ControlledBootstrap, reference, Guid.Empty));
    }
}
