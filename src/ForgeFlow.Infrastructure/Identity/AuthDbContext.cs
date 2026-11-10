using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.Roles;
using ForgeFlow.Application.Users;
using ForgeFlow.Domain.Boms;
using ForgeFlow.Domain.Items;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Domain.ProductionOrders;
using ForgeFlow.Domain.Units;
using ForgeFlow.Infrastructure.Organizations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ForgeFlow.Infrastructure.Identity;

public sealed class AuthDbContext(DbContextOptions<AuthDbContext> options) : IdentityUserContext<ApplicationAccount, long>(options)
{
    private static readonly DateTime SystemSeedUtc = new(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc);

    public DbSet<RoleDefinition> RoleDefinitions => Set<RoleDefinition>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserPermissionGrant> UserPermissionGrants => Set<UserPermissionGrant>();
    public DbSet<OrganizationMembership> OrganizationMemberships => Set<OrganizationMembership>();
    public DbSet<SiteMembership> SiteMemberships => Set<SiteMembership>();
    public DbSet<ScopedRoleAssignment> ScopedRoleAssignments => Set<ScopedRoleAssignment>();
    public DbSet<AccountSession> Sessions => Set<AccountSession>();
    public DbSet<BootstrapState> BootstrapStates => Set<BootstrapState>();
    public DbSet<SecurityEvent> SecurityEvents => Set<SecurityEvent>();
    private static readonly ValueConverter<DateTime, DateTime> Utc = new(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
    // Approved password-only support shape; no passkey tables/features in this increment.
    protected override Version SchemaVersion => IdentitySchemaVersions.Version1;

    protected override void OnModelCreating(ModelBuilder model)
    {
        base.OnModelCreating(model);
        model.HasDefaultSchema("auth");
        OrganizationModel.Configure(model);
        model.Ignore<OrganizationChange>();
        model.Ignore<Unit>();
        model.Ignore<UnitConversion>();
        model.Ignore<ClassificationSet>();
        model.Ignore<Classification>();
        model.Ignore<Item>();
        model.Ignore<ItemClassification>();
        model.Ignore<ItemUnitConversion>();
        model.Ignore<AttributeDefinition>();
        model.Ignore<ItemAttributeValue>();
        model.Ignore<ContextDimension>();
        model.Ignore<Bom>();
        model.Ignore<BomVersion>();
        model.Ignore<BomInput>();
        model.Ignore<BomOutput>();
        model.Ignore<BomApplicability>();
        model.Ignore<BomApplicabilityCondition>();
        model.Ignore<ProductionOrder>();
        model.Ignore<ProductionOrderInput>();
        model.Ignore<ProductionOrderOutput>();
        model.Ignore<ProductionOrderContext>();
        model.Entity<Organization>().ToTable("Organization", "organization", t => t.ExcludeFromMigrations());
        model.Entity<Site>().ToTable("Site", "organization", t => t.ExcludeFromMigrations());

        var user = model.Entity<ApplicationAccount>();
        user.ToTable("User", "auth", t =>
        {
            t.HasCheckConstraint("CK_User_Name", "DATALENGTH([UserName]) BETWEEN 2 AND 256 AND DATALENGTH([UserName]) = 2 * LEN([UserName]) AND [UserName] COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^A-Za-z0-9._@+-]%' AND DATALENGTH([NormalizedUserName]) = DATALENGTH([UserName]) AND [NormalizedUserName] COLLATE Latin1_General_100_BIN2 = UPPER([UserName] COLLATE Latin1_General_100_BIN2)");
            t.HasCheckConstraint("CK_User_Culture", "[PreferredCulture] IS NULL OR (DATALENGTH([PreferredCulture]) = 5 AND [PreferredCulture] COLLATE Latin1_General_100_BIN2 IN ('fa-IR','en-US'))");
            t.HasCheckConstraint("CK_User_Metadata", "[AccessFailedCount] >= 0 AND ([UpdatedAtUtc] IS NULL OR [UpdatedAtUtc] >= [CreatedAtUtc]) AND ([LockoutEnd] IS NULL OR DATEPART(TZOFFSET,[LockoutEnd]) = 0)");
        });
        user.Property(u => u.Id).UseIdentityColumn();
        user.Property(u => u.UserName).HasMaxLength(128).IsRequired();
        user.Property(u => u.NormalizedUserName).HasMaxLength(128).UseCollation("Latin1_General_100_BIN2").IsRequired();
        user.HasIndex(u => u.NormalizedUserName).IsUnique().HasFilter(null).HasDatabaseName("UX_User_NormalizedUserName");
        user.Property(u => u.Email).HasMaxLength(256); user.Property(u => u.NormalizedEmail).HasMaxLength(256);
        user.Property(u => u.PhoneNumber).HasMaxLength(64);
        user.Property(u => u.LockoutEnd).HasColumnType("datetimeoffset(7)");
        user.Property(u => u.PasswordHash).IsRequired();
        user.Property(u => u.SecurityStamp).HasMaxLength(256).IsRequired();
        user.Property(u => u.ConcurrencyStamp).HasMaxLength(256).IsRequired().IsConcurrencyToken();
        user.Property(u => u.PreferredCulture).HasMaxLength(5).IsUnicode(false);
        user.Property(u => u.CreatedAtUtc).HasColumnType("datetime2(7)").HasConversion(Utc);
        user.Property(u => u.UpdatedAtUtc).HasColumnType("datetime2(7)").HasConversion(Utc);
        Version(user);
        model.Entity<IdentityUserClaim<long>>(e => { e.ToTable("UserClaim", "auth"); e.Property(c => c.ClaimType).HasMaxLength(256); });
        model.Entity<IdentityUserLogin<long>>(e => { e.ToTable("UserLogin", "auth"); e.Property(c => c.LoginProvider).HasMaxLength(128); e.Property(c => c.ProviderKey).HasMaxLength(128); e.Property(c => c.ProviderDisplayName).HasMaxLength(256); });
        model.Entity<IdentityUserToken<long>>(e => { e.ToTable("UserToken", "auth"); e.Property(c => c.LoginProvider).HasMaxLength(128); e.Property(c => c.Name).HasMaxLength(128); });

        var roleDef = model.Entity<RoleDefinition>();
        roleDef.ToTable("RoleDefinition", "auth", t =>
        {
            t.HasCheckConstraint("CK_RoleDefinition_Invariant", "([IsSystem]=1 AND [OrganizationId] IS NULL AND [Id] IN (1,2,3) AND [IsActive]=1) OR ([IsSystem]=0 AND [OrganizationId] IS NOT NULL AND [Id] >= 100 AND [ScopeKind] IN (1,2))");
            t.HasCheckConstraint("CK_RoleDefinition_Metadata", "(([IsSystem]=1 AND [CreatedByActorId]=0) OR ([IsSystem]=0 AND [CreatedByActorId]>0)) AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId]>0 AND [UpdatedAtUtc]>=[CreatedAtUtc]))");
        });
        roleDef.HasKey(r => r.Id);
        roleDef.Property(r => r.Id).UseIdentityColumn(seed: 100, increment: 1);
        roleDef.Property(r => r.Name).HasMaxLength(100).IsRequired();
        roleDef.Property(r => r.NormalizedName).HasMaxLength(100).UseCollation("Latin1_General_100_BIN2").IsRequired();
        roleDef.HasOne<Organization>().WithMany().HasForeignKey(r => r.OrganizationId);
        roleDef.HasIndex(r => new { r.OrganizationId, r.NormalizedName }).IsUnique().HasFilter("[IsSystem] = 0").HasDatabaseName("UX_RoleDefinition_Org_Name");
        roleDef.HasIndex(r => new { r.OrganizationId, r.IsActive, r.ScopeKind });
        roleDef.Property<DateTime>("CreatedAtUtc").HasColumnType("datetime2(7)").HasConversion(Utc);
        roleDef.Property<long>("CreatedByActorId");
        roleDef.Property<DateTime?>("UpdatedAtUtc").HasColumnType("datetime2(7)").HasConversion(Utc);
        roleDef.Property<long?>("UpdatedByActorId");
        Version(roleDef);
        roleDef.HasData(
            new { Id = FixedAccessRoles.OrganizationAdministrator, Name = "OrganizationAdministrator", NormalizedName = "ORGANIZATIONADMINISTRATOR", IsSystem = true, ScopeKind = (byte)1, OrganizationId = (long?)null, IsActive = true, CreatedAtUtc = SystemSeedUtc, CreatedByActorId = 0L },
            new { Id = FixedAccessRoles.SiteOperator, Name = "SiteOperator", NormalizedName = "SITEOPERATOR", IsSystem = true, ScopeKind = (byte)2, OrganizationId = (long?)null, IsActive = true, CreatedAtUtc = SystemSeedUtc, CreatedByActorId = 0L },
            new { Id = FixedAccessRoles.DeploymentProvisioner, Name = "DeploymentProvisioner", NormalizedName = "DEPLOYMENTPROVISIONER", IsSystem = true, ScopeKind = (byte)0, OrganizationId = (long?)null, IsActive = true, CreatedAtUtc = SystemSeedUtc, CreatedByActorId = 0L });

        var rolePerm = model.Entity<RolePermission>();
        rolePerm.ToTable("RolePermission", "auth", t =>
        {
            t.HasCheckConstraint("CK_RolePermission_Code", "LEN([PermissionCode]) > 0 AND DATALENGTH([PermissionCode]) = LEN([PermissionCode])");
            t.HasCheckConstraint("CK_RolePermission_Metadata", "[CreatedByActorId] >= 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId]>0 AND [UpdatedAtUtc]>=[CreatedAtUtc]))");
        });
        rolePerm.HasKey(p => new { p.RoleId, p.PermissionCode });
        rolePerm.Property(p => p.PermissionCode).HasMaxLength(64).IsUnicode(false).IsRequired();
        rolePerm.HasOne<RoleDefinition>().WithMany().HasForeignKey(p => p.RoleId);
        rolePerm.HasIndex(p => new { p.PermissionCode, p.IsActive, p.RoleId });
        rolePerm.Property<DateTime>("CreatedAtUtc").HasColumnType("datetime2(7)").HasConversion(Utc);
        rolePerm.Property<long>("CreatedByActorId");
        rolePerm.Property<DateTime?>("UpdatedAtUtc").HasColumnType("datetime2(7)").HasConversion(Utc);
        rolePerm.Property<long?>("UpdatedByActorId");
        Version(rolePerm);
        rolePerm.HasData(SystemRolePermissionSeeds());

        var membership = model.Entity<OrganizationMembership>();
        Access(membership, "OrganizationMembership");
        membership.HasKey(m => new { m.UserId, m.OrganizationId });
        membership.HasOne<ApplicationAccount>().WithMany().HasForeignKey(m => m.UserId);
        membership.HasOne<Organization>().WithMany().HasForeignKey(m => m.OrganizationId);
        membership.HasIndex(m => new { m.OrganizationId, m.IsActive, m.UserId });
        var siteMembership = model.Entity<SiteMembership>();
        Access(siteMembership, "SiteMembership");
        siteMembership.HasKey(m => new { m.UserId, m.OrganizationId, m.SiteId });
        siteMembership.HasOne<OrganizationMembership>().WithMany().HasForeignKey(m => new { m.UserId, m.OrganizationId });
        siteMembership.HasOne<Site>().WithMany().HasForeignKey(m => new { m.OrganizationId, m.SiteId }).HasPrincipalKey(s => new { s.OrganizationId, s.Id });
        siteMembership.HasIndex(m => new { m.OrganizationId, m.SiteId, m.IsActive, m.UserId });

        var role = model.Entity<ScopedRoleAssignment>();
        Access(role, "ScopedRoleAssignment");
        role.HasKey(r => r.Id); role.Property(r => r.Id).UseIdentityColumn();
        role.ToTable("ScopedRoleAssignment", "auth", t => t.HasCheckConstraint(
            "CK_Role_Scope",
            "([RoleId]=3 AND [ScopeKind]=0 AND [OrganizationId] IS NULL AND [SiteId] IS NULL) OR ([RoleId]=1 AND [ScopeKind]=1 AND [OrganizationId] IS NOT NULL AND [SiteId] IS NULL) OR ([RoleId]=2 AND [ScopeKind]=2 AND [OrganizationId] IS NOT NULL AND [SiteId] IS NOT NULL) OR ([RoleId]>=100 AND (([ScopeKind]=1 AND [OrganizationId] IS NOT NULL AND [SiteId] IS NULL) OR ([ScopeKind]=2 AND [OrganizationId] IS NOT NULL AND [SiteId] IS NOT NULL)))"));
        role.HasOne<ApplicationAccount>().WithMany().HasForeignKey(r => r.UserId);
        role.HasOne<RoleDefinition>().WithMany().HasForeignKey(r => r.RoleId);
        role.HasOne<OrganizationMembership>().WithMany().HasForeignKey(r => new { r.UserId, r.OrganizationId });
        role.HasOne<SiteMembership>().WithMany().HasForeignKey(r => new { r.UserId, r.OrganizationId, r.SiteId });
        role.HasIndex(r => new { r.UserId, r.RoleId }).IsUnique().HasFilter("[ScopeKind] = 0");
        role.HasIndex(r => new { r.UserId, r.RoleId, r.OrganizationId }).IsUnique().HasFilter("[ScopeKind] = 1");
        role.HasIndex(r => new { r.UserId, r.RoleId, r.OrganizationId, r.SiteId }).IsUnique().HasFilter("[ScopeKind] = 2");
        role.HasIndex(r => new { r.UserId, r.IsActive, r.ScopeKind, r.OrganizationId, r.SiteId });

        var grant = model.Entity<UserPermissionGrant>();
        Access(grant, "UserPermissionGrant");
        grant.HasKey(g => g.Id); grant.Property(g => g.Id).UseIdentityColumn();
        grant.Property(g => g.PermissionCode).HasMaxLength(64).IsUnicode(false).IsRequired();
        grant.ToTable("UserPermissionGrant", "auth", t => t.HasCheckConstraint(
            "CK_UserPermissionGrant_Scope",
            "LEN([PermissionCode]) > 0 AND DATALENGTH([PermissionCode]) = LEN([PermissionCode]) AND (([ScopeKind]=1 AND [SiteId] IS NULL) OR ([ScopeKind]=2 AND [SiteId] IS NOT NULL))"));
        grant.HasOne<ApplicationAccount>().WithMany().HasForeignKey(g => g.UserId);
        grant.HasOne<OrganizationMembership>().WithMany().HasForeignKey(g => new { g.UserId, g.OrganizationId });
        grant.HasOne<SiteMembership>().WithMany().HasForeignKey(g => new { g.UserId, g.OrganizationId, g.SiteId });
        grant.HasIndex(g => new { g.UserId, g.OrganizationId, g.PermissionCode }).IsUnique().HasFilter("[ScopeKind] = 1");
        grant.HasIndex(g => new { g.UserId, g.OrganizationId, g.SiteId, g.PermissionCode }).IsUnique().HasFilter("[ScopeKind] = 2");
        grant.HasIndex(g => new { g.UserId, g.IsActive, g.ScopeKind, g.OrganizationId, g.SiteId, g.PermissionCode });

        var session = model.Entity<AccountSession>();
        session.ToTable("Session", "auth", t => t.HasCheckConstraint("CK_Session_Lifecycle", "[ExpiresAtUtc] > [IssuedAtUtc] AND (([RevokedAtUtc] IS NULL AND [RevokeReason] IS NULL) OR ([RevokedAtUtc] IS NOT NULL AND [RevokedAtUtc] >= [IssuedAtUtc] AND [RevokeReason] IS NOT NULL AND DATALENGTH([RevokeReason]) = LEN([RevokeReason]) AND [RevokeReason] COLLATE Latin1_General_100_BIN2 IN ('Logout','PasswordReset','AccountDisabled','AccountLocked','StampChanged')))"));
        session.HasKey(s => s.Id); session.Property(s => s.Id).ValueGeneratedNever();
        session.Property(s => s.SecurityStampSnapshot).HasMaxLength(256).IsRequired();
        session.Property(s => s.RevokeReason).HasMaxLength(32).IsUnicode(false);
        session.Property(s => s.IssuedAtUtc).HasColumnType("datetime2(7)").HasConversion(Utc);
        session.Property(s => s.ExpiresAtUtc).HasColumnType("datetime2(7)").HasConversion(Utc);
        session.Property(s => s.RevokedAtUtc).HasColumnType("datetime2(7)").HasConversion(Utc);
        session.HasOne<ApplicationAccount>().WithMany().HasForeignKey(s => s.UserId);
        session.HasIndex(s => new { s.UserId, s.RevokedAtUtc, s.ExpiresAtUtc }); Version(session);

        var state = model.Entity<BootstrapState>();
        state.ToTable("BootstrapState", "auth", t =>
        {
            t.HasCheckConstraint("CK_Bootstrap_Singleton", "[Id] = 1");
            t.HasCheckConstraint("CK_Bootstrap_State", "([State]=0 AND [OperationId] IS NULL AND [CompletedAtUtc] IS NULL AND [FirstUserId] IS NULL AND [OrganizationId] IS NULL AND [SiteId] IS NULL AND [HostOperatorReference] IS NULL) OR ([State]=1 AND [OperationId] IS NOT NULL AND [CompletedAtUtc] IS NOT NULL AND [FirstUserId] IS NOT NULL AND [OrganizationId] IS NOT NULL AND [SiteId] IS NOT NULL AND [HostOperatorReference] IS NOT NULL)");
        });
        state.HasKey(s => s.Id); state.Property(s => s.Id).ValueGeneratedNever();
        state.Property(s => s.HostOperatorReference).HasMaxLength(256);
        state.Property(s => s.CompletedAtUtc).HasColumnType("datetime2(7)").HasConversion(Utc);
        state.HasOne<ApplicationAccount>().WithMany().HasForeignKey(s => s.FirstUserId);
        state.HasOne<Organization>().WithMany().HasForeignKey(s => s.OrganizationId);
        state.HasOne<Site>().WithMany().HasForeignKey(s => new { s.OrganizationId, s.SiteId }).HasPrincipalKey(s => new { s.OrganizationId, s.Id });
        state.HasIndex(s => s.OperationId).IsUnique().HasFilter("[OperationId] IS NOT NULL");
        state.HasData(new { Id = (byte)1, State = (byte)0 }); Version(state);

        var security = model.Entity<SecurityEvent>();
        security.ToTable("SecurityEvent", "auth", t =>
        {
            t.HasCheckConstraint("CK_SecurityEvent_Actor", "([ActorKind]=0 AND [ActorUserId] IS NULL AND [HostOperatorReference] IS NULL) OR ([ActorKind]=1 AND [ActorUserId] IS NOT NULL AND [HostOperatorReference] IS NULL) OR ([ActorKind]=2 AND [ActorUserId] IS NULL AND [HostOperatorReference] IS NOT NULL AND LEN([HostOperatorReference]) > 0)");
            t.HasCheckConstraint("CK_SecurityEvent_Data", "ISJSON([DataJson])=1 AND [Outcome] IN (0,1,2) AND ([SiteId] IS NULL OR [OrganizationId] IS NOT NULL)");
            t.HasCheckConstraint("CK_SecurityEvent_Code", "DATALENGTH([EventCode])=LEN([EventCode]) AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('BootstrapCompleted','AccountCreated','SignInFailed','AccountLocked','SessionIssued','SessionRevoked','AccessDenied','PasswordResetCompleted','AccountEligibilityChanged','MembershipChanged','ScopedRoleChanged','RoleDefinitionChanged','DirectPermissionChanged','CulturePreferenceChanged')");
        });
        security.HasKey(e => e.Id); security.Property(e => e.Id).UseIdentityColumn();
        security.Property(e => e.EventCode).HasMaxLength(64).IsUnicode(false).IsRequired();
        security.Property(e => e.HostOperatorReference).HasMaxLength(256);
        security.Property(e => e.DataJson).HasMaxLength(2000).IsRequired();
        security.Property(e => e.OccurredAtUtc).HasColumnType("datetime2(7)").HasConversion(Utc);
        security.HasOne<ApplicationAccount>().WithMany().HasForeignKey(e => e.ActorUserId);
        security.HasOne<ApplicationAccount>().WithMany().HasForeignKey(e => e.SubjectUserId);
        security.HasOne<AccountSession>().WithMany().HasForeignKey(e => e.SessionId);
        security.HasOne<Organization>().WithMany().HasForeignKey(e => e.OrganizationId);
        security.HasOne<Site>().WithMany().HasForeignKey(e => new { e.OrganizationId, e.SiteId }).HasPrincipalKey(s => new { s.OrganizationId, s.Id });
        security.HasIndex(e => new { e.OccurredAtUtc, e.Id }); security.HasIndex(e => new { e.SubjectUserId, e.OccurredAtUtc, e.Id });
        security.HasIndex(e => e.CorrelationId); security.HasIndex(e => new { e.OrganizationId, e.OccurredAtUtc, e.Id });
        foreach (var fk in model.Model.GetEntityTypes().SelectMany(e => e.GetForeignKeys())) fk.DeleteBehavior = DeleteBehavior.NoAction;
    }

    private static object[] SystemRolePermissionSeeds()
    {
        var orgAdmin = new[]
        {
            OrganizationPermissions.OrganizationsRead, OrganizationPermissions.OrganizationsUpdate, OrganizationPermissions.OrganizationsSetActive,
            OrganizationPermissions.SitesRead, OrganizationPermissions.SitesCreate, OrganizationPermissions.SitesUpdate, OrganizationPermissions.SitesSetActive,
            UserPermissions.UsersRead, UserPermissions.UsersCreate, UserPermissions.UsersSetActive, UserPermissions.UsersUnlock, UserPermissions.UsersResetPassword,
            UserPermissions.MembershipsManage, UserPermissions.RolesAssignFixed,
            ForgeFlow.Application.Roles.RolePermissions.RolesRead,
            ForgeFlow.Application.Roles.RolePermissions.RolesManage,
            ForgeFlow.Application.Roles.RolePermissions.PermissionsAssign
        };
        var siteOperator = new[]
        {
            OrganizationPermissions.SitesRead, OrganizationPermissions.SitesUpdate, OrganizationPermissions.SitesSetActive
        };
        var provisioner = new[]
        {
            OrganizationPermissions.OrganizationsCreate, UserPermissions.UsersAttachOrganization, UserPermissions.UsersSetActive,
            UserPermissions.UsersUnlock, UserPermissions.UsersResetPassword
        };

        return orgAdmin.Select(c => Seed(FixedAccessRoles.OrganizationAdministrator, c))
            .Concat(siteOperator.Select(c => Seed(FixedAccessRoles.SiteOperator, c)))
            .Concat(provisioner.Select(c => Seed(FixedAccessRoles.DeploymentProvisioner, c)))
            .ToArray();

        static object Seed(long roleId, string code)
            => new { RoleId = roleId, PermissionCode = code, IsActive = true, CreatedAtUtc = SystemSeedUtc, CreatedByActorId = 0L };
    }

    private static void Version<T>(EntityTypeBuilder<T> entity) where T : class => entity.Property<byte[]>("RowVersion").IsRowVersion().IsRequired();
    private static void Access<T>(EntityTypeBuilder<T> entity, string name) where T : class
    {
        entity.ToTable(name, "auth", t => t.HasCheckConstraint("CK_" + name + "_Metadata", "[CreatedByActorId]>0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId]>0 AND [UpdatedAtUtc]>=[CreatedAtUtc]))"));
        entity.Property<DateTime>("CreatedAtUtc").HasColumnType("datetime2(7)").HasConversion(Utc);
        entity.Property<long>("CreatedByActorId");
        entity.Property<DateTime?>("UpdatedAtUtc").HasColumnType("datetime2(7)").HasConversion(Utc);
        entity.Property<long?>("UpdatedByActorId"); Version(entity);
    }
    private static bool ValueChanged(Microsoft.EntityFrameworkCore.ChangeTracking.PropertyEntry property)
        => property.IsModified && !Equals(property.OriginalValue, property.CurrentValue);

    private void Guard()
    {
        ChangeTracker.DetectChanges();
        if (ChangeTracker.Entries().Any(e => e.State == EntityState.Deleted ||
            (e.Entity is SecurityEvent && e.State == EntityState.Modified) ||
            (e.Entity is BootstrapState && e.State == EntityState.Modified && (byte)e.OriginalValues[nameof(BootstrapState.State)]! == 1) ||
            (e.Entity is ApplicationAccount && e.State == EntityState.Modified && e.Properties.Any(p => p.Metadata.Name is "UserName" or "NormalizedUserName" && ValueChanged(p))) ||
            (e.Entity is ScopedRoleAssignment && e.State == EntityState.Modified && e.Properties.Any(p => p.Metadata.Name is "UserId" or "RoleId" or "ScopeKind" or "OrganizationId" or "SiteId" && ValueChanged(p))) ||
            (e.Entity is RoleDefinition && e.State == EntityState.Modified &&
                ((bool)e.OriginalValues[nameof(RoleDefinition.IsSystem)]!
                 || e.Properties.Any(p => p.Metadata.Name is "IsSystem" or "ScopeKind" or "OrganizationId" && ValueChanged(p)))) ||
            (e.Entity is RolePermission && e.State == EntityState.Modified && e.Properties.Any(p => p.Metadata.Name is "RoleId" or "PermissionCode" && ValueChanged(p))) ||
            (e.Entity is UserPermissionGrant && e.State == EntityState.Modified && e.Properties.Any(p => p.Metadata.Name is "UserId" or "ScopeKind" or "OrganizationId" or "SiteId" or "PermissionCode" && ValueChanged(p)))))
            throw new InvalidOperationException("Auth.ImmutableHistory");
    }
    public override int SaveChanges(bool acceptAllChangesOnSuccess) { Guard(); return base.SaveChanges(acceptAllChangesOnSuccess); }
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    { Guard(); return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken); }
}
