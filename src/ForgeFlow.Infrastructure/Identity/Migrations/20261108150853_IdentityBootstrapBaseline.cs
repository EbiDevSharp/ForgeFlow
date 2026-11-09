using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ForgeFlow.Infrastructure.Identity.Migrations
{
    /// <inheritdoc />
    public partial class IdentityBootstrapBaseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "auth");

            migrationBuilder.CreateTable(
                name: "User",
                schema: "auth",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    PreferredCulture = table.Column<string>(type: "varchar(5)", unicode: false, maxLength: 5, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    NormalizedUserName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SecurityStamp = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    PhoneNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "bit", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "bit", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_User", x => x.Id);
                    table.CheckConstraint("CK_User_Culture", "[PreferredCulture] IS NULL OR (DATALENGTH([PreferredCulture]) = 5 AND [PreferredCulture] COLLATE Latin1_General_100_BIN2 IN ('fa-IR','en-US'))");
                    table.CheckConstraint("CK_User_Metadata", "[AccessFailedCount] >= 0 AND ([UpdatedAtUtc] IS NULL OR [UpdatedAtUtc] >= [CreatedAtUtc]) AND ([LockoutEnd] IS NULL OR DATEPART(TZOFFSET,[LockoutEnd]) = 0)");
                    table.CheckConstraint("CK_User_Name", "DATALENGTH([UserName]) BETWEEN 2 AND 256 AND DATALENGTH([UserName]) = 2 * LEN([UserName]) AND [UserName] COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^A-Za-z0-9._@+-]%' AND DATALENGTH([NormalizedUserName]) = DATALENGTH([UserName]) AND [NormalizedUserName] COLLATE Latin1_General_100_BIN2 = UPPER([UserName] COLLATE Latin1_General_100_BIN2)");
                });

            migrationBuilder.CreateTable(
                name: "BootstrapState",
                schema: "auth",
                columns: table => new
                {
                    Id = table.Column<byte>(type: "tinyint", nullable: false),
                    State = table.Column<byte>(type: "tinyint", nullable: false),
                    OperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    FirstUserId = table.Column<long>(type: "bigint", nullable: true),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: true),
                    SiteId = table.Column<long>(type: "bigint", nullable: true),
                    HostOperatorReference = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BootstrapState", x => x.Id);
                    table.CheckConstraint("CK_Bootstrap_Singleton", "[Id] = 1");
                    table.CheckConstraint("CK_Bootstrap_State", "([State]=0 AND [OperationId] IS NULL AND [CompletedAtUtc] IS NULL AND [FirstUserId] IS NULL AND [OrganizationId] IS NULL AND [SiteId] IS NULL AND [HostOperatorReference] IS NULL) OR ([State]=1 AND [OperationId] IS NOT NULL AND [CompletedAtUtc] IS NOT NULL AND [FirstUserId] IS NOT NULL AND [OrganizationId] IS NOT NULL AND [SiteId] IS NOT NULL AND [HostOperatorReference] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_BootstrapState_Organization_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "organization",
                        principalTable: "Organization",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_BootstrapState_Site_OrganizationId_SiteId",
                        columns: x => new { x.OrganizationId, x.SiteId },
                        principalSchema: "organization",
                        principalTable: "Site",
                        principalColumns: new[] { "OrganizationId", "Id" });
                    table.ForeignKey(
                        name: "FK_BootstrapState_User_FirstUserId",
                        column: x => x.FirstUserId,
                        principalSchema: "auth",
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "OrganizationMembership",
                schema: "auth",
                columns: table => new
                {
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CreatedByActorId = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    UpdatedByActorId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationMembership", x => new { x.UserId, x.OrganizationId });
                    table.CheckConstraint("CK_OrganizationMembership_Metadata", "[CreatedByActorId]>0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId]>0 AND [UpdatedAtUtc]>=[CreatedAtUtc]))");
                    table.ForeignKey(
                        name: "FK_OrganizationMembership_Organization_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "organization",
                        principalTable: "Organization",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrganizationMembership_User_UserId",
                        column: x => x.UserId,
                        principalSchema: "auth",
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Session",
                schema: "auth",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    SecurityStampSnapshot = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    IssuedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    RevokeReason = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Session", x => x.Id);
                    table.CheckConstraint("CK_Session_Lifecycle", "[ExpiresAtUtc] > [IssuedAtUtc] AND (([RevokedAtUtc] IS NULL AND [RevokeReason] IS NULL) OR ([RevokedAtUtc] IS NOT NULL AND [RevokedAtUtc] >= [IssuedAtUtc] AND [RevokeReason] IS NOT NULL AND DATALENGTH([RevokeReason]) = LEN([RevokeReason]) AND [RevokeReason] COLLATE Latin1_General_100_BIN2 IN ('Logout','PasswordReset','AccountDisabled','AccountLocked','StampChanged')))");
                    table.ForeignKey(
                        name: "FK_Session_User_UserId",
                        column: x => x.UserId,
                        principalSchema: "auth",
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "UserClaim",
                schema: "auth",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ClaimValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserClaim", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserClaim_User_UserId",
                        column: x => x.UserId,
                        principalSchema: "auth",
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "UserLogin",
                schema: "auth",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ProviderKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    UserId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserLogin", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_UserLogin_User_UserId",
                        column: x => x.UserId,
                        principalSchema: "auth",
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "UserToken",
                schema: "auth",
                columns: table => new
                {
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    LoginProvider = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserToken", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_UserToken_User_UserId",
                        column: x => x.UserId,
                        principalSchema: "auth",
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "SiteMembership",
                schema: "auth",
                columns: table => new
                {
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    SiteId = table.Column<long>(type: "bigint", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CreatedByActorId = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    UpdatedByActorId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteMembership", x => new { x.UserId, x.OrganizationId, x.SiteId });
                    table.CheckConstraint("CK_SiteMembership_Metadata", "[CreatedByActorId]>0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId]>0 AND [UpdatedAtUtc]>=[CreatedAtUtc]))");
                    table.ForeignKey(
                        name: "FK_SiteMembership_OrganizationMembership_UserId_OrganizationId",
                        columns: x => new { x.UserId, x.OrganizationId },
                        principalSchema: "auth",
                        principalTable: "OrganizationMembership",
                        principalColumns: new[] { "UserId", "OrganizationId" });
                    table.ForeignKey(
                        name: "FK_SiteMembership_Site_OrganizationId_SiteId",
                        columns: x => new { x.OrganizationId, x.SiteId },
                        principalSchema: "organization",
                        principalTable: "Site",
                        principalColumns: new[] { "OrganizationId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "SecurityEvent",
                schema: "auth",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EventCode = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorKind = table.Column<byte>(type: "tinyint", nullable: false),
                    ActorUserId = table.Column<long>(type: "bigint", nullable: true),
                    SubjectUserId = table.Column<long>(type: "bigint", nullable: true),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    HostOperatorReference = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: true),
                    SiteId = table.Column<long>(type: "bigint", nullable: true),
                    Outcome = table.Column<byte>(type: "tinyint", nullable: false),
                    DataJson = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecurityEvent", x => x.Id);
                    table.CheckConstraint("CK_SecurityEvent_Actor", "([ActorKind]=0 AND [ActorUserId] IS NULL AND [HostOperatorReference] IS NULL) OR ([ActorKind]=1 AND [ActorUserId] IS NOT NULL AND [HostOperatorReference] IS NULL) OR ([ActorKind]=2 AND [ActorUserId] IS NULL AND [HostOperatorReference] IS NOT NULL AND LEN([HostOperatorReference]) > 0)");
                    table.CheckConstraint("CK_SecurityEvent_Code", "DATALENGTH([EventCode])=LEN([EventCode]) AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('BootstrapCompleted','AccountCreated','SignInFailed','AccountLocked','SessionIssued','SessionRevoked','AccessDenied','PasswordResetCompleted','AccountEligibilityChanged','MembershipChanged','ScopedRoleChanged','CulturePreferenceChanged')");
                    table.CheckConstraint("CK_SecurityEvent_Data", "ISJSON([DataJson])=1 AND [Outcome] IN (0,1,2) AND ([SiteId] IS NULL OR [OrganizationId] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_SecurityEvent_Organization_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "organization",
                        principalTable: "Organization",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SecurityEvent_Session_SessionId",
                        column: x => x.SessionId,
                        principalSchema: "auth",
                        principalTable: "Session",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SecurityEvent_Site_OrganizationId_SiteId",
                        columns: x => new { x.OrganizationId, x.SiteId },
                        principalSchema: "organization",
                        principalTable: "Site",
                        principalColumns: new[] { "OrganizationId", "Id" });
                    table.ForeignKey(
                        name: "FK_SecurityEvent_User_ActorUserId",
                        column: x => x.ActorUserId,
                        principalSchema: "auth",
                        principalTable: "User",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SecurityEvent_User_SubjectUserId",
                        column: x => x.SubjectUserId,
                        principalSchema: "auth",
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ScopedRoleAssignment",
                schema: "auth",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    RoleId = table.Column<byte>(type: "tinyint", nullable: false),
                    ScopeKind = table.Column<byte>(type: "tinyint", nullable: false),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: true),
                    SiteId = table.Column<long>(type: "bigint", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CreatedByActorId = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    UpdatedByActorId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScopedRoleAssignment", x => x.Id);
                    table.CheckConstraint("CK_Role_Scope", "([RoleId]=1 AND [ScopeKind]=1 AND [OrganizationId] IS NOT NULL AND [SiteId] IS NULL) OR ([RoleId]=2 AND [ScopeKind]=2 AND [OrganizationId] IS NOT NULL AND [SiteId] IS NOT NULL) OR ([RoleId]=3 AND [ScopeKind]=0 AND [OrganizationId] IS NULL AND [SiteId] IS NULL)");
                    table.CheckConstraint("CK_ScopedRoleAssignment_Metadata", "[CreatedByActorId]>0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId]>0 AND [UpdatedAtUtc]>=[CreatedAtUtc]))");
                    table.ForeignKey(
                        name: "FK_ScopedRoleAssignment_OrganizationMembership_UserId_OrganizationId",
                        columns: x => new { x.UserId, x.OrganizationId },
                        principalSchema: "auth",
                        principalTable: "OrganizationMembership",
                        principalColumns: new[] { "UserId", "OrganizationId" });
                    table.ForeignKey(
                        name: "FK_ScopedRoleAssignment_SiteMembership_UserId_OrganizationId_SiteId",
                        columns: x => new { x.UserId, x.OrganizationId, x.SiteId },
                        principalSchema: "auth",
                        principalTable: "SiteMembership",
                        principalColumns: new[] { "UserId", "OrganizationId", "SiteId" });
                    table.ForeignKey(
                        name: "FK_ScopedRoleAssignment_User_UserId",
                        column: x => x.UserId,
                        principalSchema: "auth",
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.InsertData(
                schema: "auth",
                table: "BootstrapState",
                columns: new[] { "Id", "CompletedAtUtc", "FirstUserId", "HostOperatorReference", "OperationId", "OrganizationId", "SiteId", "State" },
                values: new object[] { (byte)1, null, null, null, null, null, null, (byte)0 });

            migrationBuilder.CreateIndex(
                name: "IX_BootstrapState_FirstUserId",
                schema: "auth",
                table: "BootstrapState",
                column: "FirstUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BootstrapState_OperationId",
                schema: "auth",
                table: "BootstrapState",
                column: "OperationId",
                unique: true,
                filter: "[OperationId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BootstrapState_OrganizationId_SiteId",
                schema: "auth",
                table: "BootstrapState",
                columns: new[] { "OrganizationId", "SiteId" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationMembership_OrganizationId_IsActive_UserId",
                schema: "auth",
                table: "OrganizationMembership",
                columns: new[] { "OrganizationId", "IsActive", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_ScopedRoleAssignment_UserId_IsActive_ScopeKind_OrganizationId_SiteId",
                schema: "auth",
                table: "ScopedRoleAssignment",
                columns: new[] { "UserId", "IsActive", "ScopeKind", "OrganizationId", "SiteId" });

            migrationBuilder.CreateIndex(
                name: "IX_ScopedRoleAssignment_UserId_OrganizationId_SiteId",
                schema: "auth",
                table: "ScopedRoleAssignment",
                columns: new[] { "UserId", "OrganizationId", "SiteId" });

            migrationBuilder.CreateIndex(
                name: "IX_ScopedRoleAssignment_UserId_RoleId",
                schema: "auth",
                table: "ScopedRoleAssignment",
                columns: new[] { "UserId", "RoleId" },
                unique: true,
                filter: "[ScopeKind] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_ScopedRoleAssignment_UserId_RoleId_OrganizationId",
                schema: "auth",
                table: "ScopedRoleAssignment",
                columns: new[] { "UserId", "RoleId", "OrganizationId" },
                unique: true,
                filter: "[ScopeKind] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_ScopedRoleAssignment_UserId_RoleId_OrganizationId_SiteId",
                schema: "auth",
                table: "ScopedRoleAssignment",
                columns: new[] { "UserId", "RoleId", "OrganizationId", "SiteId" },
                unique: true,
                filter: "[ScopeKind] = 2");

            migrationBuilder.CreateIndex(
                name: "IX_SecurityEvent_ActorUserId",
                schema: "auth",
                table: "SecurityEvent",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SecurityEvent_CorrelationId",
                schema: "auth",
                table: "SecurityEvent",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_SecurityEvent_OccurredAtUtc_Id",
                schema: "auth",
                table: "SecurityEvent",
                columns: new[] { "OccurredAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_SecurityEvent_OrganizationId_OccurredAtUtc_Id",
                schema: "auth",
                table: "SecurityEvent",
                columns: new[] { "OrganizationId", "OccurredAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_SecurityEvent_OrganizationId_SiteId",
                schema: "auth",
                table: "SecurityEvent",
                columns: new[] { "OrganizationId", "SiteId" });

            migrationBuilder.CreateIndex(
                name: "IX_SecurityEvent_SessionId",
                schema: "auth",
                table: "SecurityEvent",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_SecurityEvent_SubjectUserId_OccurredAtUtc_Id",
                schema: "auth",
                table: "SecurityEvent",
                columns: new[] { "SubjectUserId", "OccurredAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Session_UserId_RevokedAtUtc_ExpiresAtUtc",
                schema: "auth",
                table: "Session",
                columns: new[] { "UserId", "RevokedAtUtc", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SiteMembership_OrganizationId_SiteId_IsActive_UserId",
                schema: "auth",
                table: "SiteMembership",
                columns: new[] { "OrganizationId", "SiteId", "IsActive", "UserId" });

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                schema: "auth",
                table: "User",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "UX_User_NormalizedUserName",
                schema: "auth",
                table: "User",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserClaim_UserId",
                schema: "auth",
                table: "UserClaim",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserLogin_UserId",
                schema: "auth",
                table: "UserLogin",
                column: "UserId");

            // No SQL login/user or membership is provisioned. This is not a bootstrap authority.
            migrationBuilder.Sql("""
                CREATE ROLE [ForgeFlowAuthRuntime] AUTHORIZATION [dbo];
                GRANT SELECT, INSERT, UPDATE ON OBJECT::[auth].[User] TO [ForgeFlowAuthRuntime];
                GRANT SELECT, INSERT, UPDATE ON OBJECT::[auth].[Session] TO [ForgeFlowAuthRuntime];
                GRANT SELECT ON OBJECT::[auth].[UserClaim] TO [ForgeFlowAuthRuntime];
                GRANT SELECT ON OBJECT::[auth].[UserLogin] TO [ForgeFlowAuthRuntime];
                GRANT SELECT ON OBJECT::[auth].[UserToken] TO [ForgeFlowAuthRuntime];
                GRANT SELECT ON OBJECT::[auth].[OrganizationMembership] TO [ForgeFlowAuthRuntime];
                GRANT SELECT ON OBJECT::[auth].[SiteMembership] TO [ForgeFlowAuthRuntime];
                GRANT SELECT ON OBJECT::[auth].[ScopedRoleAssignment] TO [ForgeFlowAuthRuntime];
                GRANT SELECT ON OBJECT::[auth].[BootstrapState] TO [ForgeFlowAuthRuntime];
                GRANT SELECT, INSERT ON OBJECT::[auth].[SecurityEvent] TO [ForgeFlowAuthRuntime];
                DENY DELETE ON OBJECT::[auth].[User] TO [ForgeFlowAuthRuntime];
                DENY DELETE ON OBJECT::[auth].[Session] TO [ForgeFlowAuthRuntime];
                DENY INSERT, UPDATE, DELETE ON OBJECT::[auth].[UserClaim] TO [ForgeFlowAuthRuntime];
                DENY INSERT, UPDATE, DELETE ON OBJECT::[auth].[UserLogin] TO [ForgeFlowAuthRuntime];
                DENY INSERT, UPDATE, DELETE ON OBJECT::[auth].[UserToken] TO [ForgeFlowAuthRuntime];
                DENY INSERT, UPDATE, DELETE ON OBJECT::[auth].[OrganizationMembership] TO [ForgeFlowAuthRuntime];
                DENY INSERT, UPDATE, DELETE ON OBJECT::[auth].[SiteMembership] TO [ForgeFlowAuthRuntime];
                DENY INSERT, UPDATE, DELETE ON OBJECT::[auth].[ScopedRoleAssignment] TO [ForgeFlowAuthRuntime];
                DENY INSERT, UPDATE, DELETE ON OBJECT::[auth].[BootstrapState] TO [ForgeFlowAuthRuntime];
                DENY UPDATE, DELETE ON OBJECT::[auth].[SecurityEvent] TO [ForgeFlowAuthRuntime];
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [auth].[User]) OR EXISTS (SELECT 1 FROM [auth].[UserClaim])
                 OR EXISTS (SELECT 1 FROM [auth].[UserLogin]) OR EXISTS (SELECT 1 FROM [auth].[UserToken])
                 OR EXISTS (SELECT 1 FROM [auth].[OrganizationMembership]) OR EXISTS (SELECT 1 FROM [auth].[SiteMembership])
                 OR EXISTS (SELECT 1 FROM [auth].[ScopedRoleAssignment]) OR EXISTS (SELECT 1 FROM [auth].[Session])
                 OR EXISTS (SELECT 1 FROM [auth].[SecurityEvent]) OR EXISTS (SELECT 1 FROM [auth].[BootstrapState] WHERE [State] <> 0)
                    THROW 51001, 'Auth.NonEmptyRollbackDenied', 1;
                DROP ROLE [ForgeFlowAuthRuntime];
                """);
            migrationBuilder.DropTable(
                name: "BootstrapState",
                schema: "auth");

            migrationBuilder.DropTable(
                name: "ScopedRoleAssignment",
                schema: "auth");

            migrationBuilder.DropTable(
                name: "SecurityEvent",
                schema: "auth");

            migrationBuilder.DropTable(
                name: "UserClaim",
                schema: "auth");

            migrationBuilder.DropTable(
                name: "UserLogin",
                schema: "auth");

            migrationBuilder.DropTable(
                name: "UserToken",
                schema: "auth");

            migrationBuilder.DropTable(
                name: "SiteMembership",
                schema: "auth");

            migrationBuilder.DropTable(
                name: "Session",
                schema: "auth");

            migrationBuilder.DropTable(
                name: "OrganizationMembership",
                schema: "auth");

            migrationBuilder.DropTable(
                name: "User",
                schema: "auth");
        }
    }
}
