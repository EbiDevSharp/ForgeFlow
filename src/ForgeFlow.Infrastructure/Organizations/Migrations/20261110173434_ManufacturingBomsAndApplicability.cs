using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ForgeFlow.Infrastructure.Organizations.Migrations
{
    /// <inheritdoc />
    public partial class ManufacturingBomsAndApplicability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_OrganizationChange_Event",
                schema: "organization",
                table: "OrganizationChange");

            migrationBuilder.CreateTable(
                name: "Bom",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    Code = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PrimaryOutputItemId = table.Column<long>(type: "bigint", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CreatedByActorId = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    UpdatedByActorId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bom", x => x.Id);
                    table.UniqueConstraint("AK_Bom_Org_Id", x => new { x.OrganizationId, x.Id });
                    table.CheckConstraint("CK_Bom_Code", "DATALENGTH([Code]) BETWEEN 1 AND 32 AND DATALENGTH([Code]) = LEN([Code]) AND [Code] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([Code], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'");
                    table.CheckConstraint("CK_Bom_Metadata", "[CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))");
                    table.CheckConstraint("CK_Bom_Name", "DATALENGTH([Name]) BETWEEN 2 AND 400 AND DATALENGTH([Name]) = DATALENGTH(LTRIM(RTRIM([Name]))) AND DATALENGTH(LTRIM(RTRIM([Name]))) > 0");
                    table.ForeignKey(
                        name: "FK_Bom_Organization",
                        column: x => x.OrganizationId,
                        principalSchema: "organization",
                        principalTable: "Organization",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Bom_PrimaryItem",
                        columns: x => new { x.OrganizationId, x.PrimaryOutputItemId },
                        principalSchema: "organization",
                        principalTable: "Item",
                        principalColumns: new[] { "OrganizationId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "ContextDimension",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    Code = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CreatedByActorId = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    UpdatedByActorId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContextDimension", x => x.Id);
                    table.UniqueConstraint("AK_ContextDimension_Org_Id", x => new { x.OrganizationId, x.Id });
                    table.CheckConstraint("CK_ContextDimension_Code", "DATALENGTH([Code]) BETWEEN 1 AND 32 AND DATALENGTH([Code]) = LEN([Code]) AND [Code] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([Code], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'");
                    table.CheckConstraint("CK_ContextDimension_Metadata", "[CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))");
                    table.CheckConstraint("CK_ContextDimension_Name", "DATALENGTH([Name]) BETWEEN 2 AND 400 AND DATALENGTH([Name]) = DATALENGTH(LTRIM(RTRIM([Name]))) AND DATALENGTH(LTRIM(RTRIM([Name]))) > 0");
                    table.ForeignKey(
                        name: "FK_ContextDimension_Organization",
                        column: x => x.OrganizationId,
                        principalSchema: "organization",
                        principalTable: "Organization",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "BomVersion",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    BomId = table.Column<long>(type: "bigint", nullable: false),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    BaseOutputQuantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    BaseOutputUnitId = table.Column<long>(type: "bigint", nullable: false),
                    EffectiveFromDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveToDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CreatedByActorId = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    UpdatedByActorId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BomVersion", x => x.Id);
                    table.UniqueConstraint("AK_BomVersion_Org_Id", x => new { x.OrganizationId, x.Id });
                    table.CheckConstraint("CK_BomVersion_EffectiveDates", "[EffectiveToDate] IS NULL OR [EffectiveToDate] >= [EffectiveFromDate]");
                    table.CheckConstraint("CK_BomVersion_Metadata", "[CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))");
                    table.CheckConstraint("CK_BomVersion_Number", "[VersionNumber] > 0");
                    table.CheckConstraint("CK_BomVersion_PositiveBaseQty", "[BaseOutputQuantity] > 0");
                    table.CheckConstraint("CK_BomVersion_Status", "[Status] BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "FK_BomVersion_BaseUnit",
                        columns: x => new { x.OrganizationId, x.BaseOutputUnitId },
                        principalSchema: "organization",
                        principalTable: "Unit",
                        principalColumns: new[] { "OrganizationId", "Id" });
                    table.ForeignKey(
                        name: "FK_BomVersion_Bom",
                        columns: x => new { x.OrganizationId, x.BomId },
                        principalSchema: "organization",
                        principalTable: "Bom",
                        principalColumns: new[] { "OrganizationId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "BomApplicability",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    BomVersionId = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CreatedByActorId = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    UpdatedByActorId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BomApplicability", x => x.Id);
                    table.UniqueConstraint("AK_BomApplicability_Org_Id", x => new { x.OrganizationId, x.Id });
                    table.CheckConstraint("CK_BomApplicability_Metadata", "[CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))");
                    table.CheckConstraint("CK_BomApplicability_Name", "DATALENGTH([Name]) BETWEEN 2 AND 400 AND DATALENGTH([Name]) = DATALENGTH(LTRIM(RTRIM([Name]))) AND DATALENGTH(LTRIM(RTRIM([Name]))) > 0");
                    table.ForeignKey(
                        name: "FK_BomApplicability_Version",
                        columns: x => new { x.OrganizationId, x.BomVersionId },
                        principalSchema: "organization",
                        principalTable: "BomVersion",
                        principalColumns: new[] { "OrganizationId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "BomInput",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    BomVersionId = table.Column<long>(type: "bigint", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<long>(type: "bigint", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    UnitId = table.Column<long>(type: "bigint", nullable: false),
                    ScrapPercentage = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    IsOptional = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CreatedByActorId = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    UpdatedByActorId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BomInput", x => x.Id);
                    table.CheckConstraint("CK_BomInput_Metadata", "[CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))");
                    table.CheckConstraint("CK_BomInput_PositiveQty", "[Quantity] > 0");
                    table.CheckConstraint("CK_BomInput_ScrapBounds", "[ScrapPercentage] >= 0 AND [ScrapPercentage] < 100");
                    table.CheckConstraint("CK_BomInput_Sequence", "[Sequence] > 0");
                    table.ForeignKey(
                        name: "FK_BomInput_Item",
                        columns: x => new { x.OrganizationId, x.ItemId },
                        principalSchema: "organization",
                        principalTable: "Item",
                        principalColumns: new[] { "OrganizationId", "Id" });
                    table.ForeignKey(
                        name: "FK_BomInput_Unit",
                        columns: x => new { x.OrganizationId, x.UnitId },
                        principalSchema: "organization",
                        principalTable: "Unit",
                        principalColumns: new[] { "OrganizationId", "Id" });
                    table.ForeignKey(
                        name: "FK_BomInput_Version",
                        columns: x => new { x.OrganizationId, x.BomVersionId },
                        principalSchema: "organization",
                        principalTable: "BomVersion",
                        principalColumns: new[] { "OrganizationId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "BomOutput",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    BomVersionId = table.Column<long>(type: "bigint", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<long>(type: "bigint", nullable: false),
                    OutputType = table.Column<byte>(type: "tinyint", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    UnitId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CreatedByActorId = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    UpdatedByActorId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BomOutput", x => x.Id);
                    table.CheckConstraint("CK_BomOutput_Metadata", "[CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))");
                    table.CheckConstraint("CK_BomOutput_PositiveQty", "[Quantity] > 0");
                    table.CheckConstraint("CK_BomOutput_Sequence", "[Sequence] > 0");
                    table.CheckConstraint("CK_BomOutput_Type", "[OutputType] BETWEEN 1 AND 4");
                    table.ForeignKey(
                        name: "FK_BomOutput_Item",
                        columns: x => new { x.OrganizationId, x.ItemId },
                        principalSchema: "organization",
                        principalTable: "Item",
                        principalColumns: new[] { "OrganizationId", "Id" });
                    table.ForeignKey(
                        name: "FK_BomOutput_Unit",
                        columns: x => new { x.OrganizationId, x.UnitId },
                        principalSchema: "organization",
                        principalTable: "Unit",
                        principalColumns: new[] { "OrganizationId", "Id" });
                    table.ForeignKey(
                        name: "FK_BomOutput_Version",
                        columns: x => new { x.OrganizationId, x.BomVersionId },
                        principalSchema: "organization",
                        principalTable: "BomVersion",
                        principalColumns: new[] { "OrganizationId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "BomApplicabilityCondition",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    BomApplicabilityId = table.Column<long>(type: "bigint", nullable: false),
                    ContextDimensionId = table.Column<long>(type: "bigint", nullable: false),
                    Operator = table.Column<byte>(type: "tinyint", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CreatedByActorId = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    UpdatedByActorId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BomApplicabilityCondition", x => x.Id);
                    table.CheckConstraint("CK_BomApplicabilityCondition_Metadata", "[CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))");
                    table.CheckConstraint("CK_BomApplicabilityCondition_Operator", "[Operator] BETWEEN 1 AND 4");
                    table.CheckConstraint("CK_BomApplicabilityCondition_Value", "DATALENGTH([Value]) > 0");
                    table.ForeignKey(
                        name: "FK_BomApplicabilityCondition_Applicability",
                        columns: x => new { x.OrganizationId, x.BomApplicabilityId },
                        principalSchema: "organization",
                        principalTable: "BomApplicability",
                        principalColumns: new[] { "OrganizationId", "Id" });
                    table.ForeignKey(
                        name: "FK_BomApplicabilityCondition_Dimension",
                        columns: x => new { x.OrganizationId, x.ContextDimensionId },
                        principalSchema: "organization",
                        principalTable: "ContextDimension",
                        principalColumns: new[] { "OrganizationId", "Id" });
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrganizationChange_Event",
                schema: "organization",
                table: "OrganizationChange",
                sql: "DATALENGTH([EventCode]) = LEN([EventCode]) AND (([SiteId] IS NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('Organization.Created','Organization.Renamed','Organization.Activated','Organization.Deactivated','Unit.Created','Unit.Updated','Unit.Activated','Unit.Deactivated','UnitConversion.Created','UnitConversion.Updated','UnitConversion.Activated','UnitConversion.Deactivated','Item.Created','Item.Updated','Item.Activated','Item.Deactivated','ClassificationSet.Created','Classification.Created','Classification.Updated','Classification.Activated','Classification.Deactivated','ItemClassification.Updated','ItemUnitConversion.Created','ItemUnitConversion.Updated','ItemUnitConversion.Activated','ItemUnitConversion.Deactivated','AttributeDefinition.Created','AttributeDefinition.Updated','AttributeDefinition.Activated','AttributeDefinition.Deactivated','ItemAttributeValue.Updated','ContextDimension.Created','ContextDimension.Updated','ContextDimension.Activated','ContextDimension.Deactivated','Bom.Created','Bom.Renamed','Bom.Activated','Bom.Deactivated','BomVersion.Created','BomVersion.Updated','BomVersion.StatusChanged','BomVersion.LinesSaved','BomVersion.ApplicabilitySaved')) OR ([SiteId] IS NOT NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('Site.Created','Site.Renamed','Site.Activated','Site.Deactivated')))");

            migrationBuilder.CreateIndex(
                name: "IX_Bom_Org_PrimaryItem",
                schema: "organization",
                table: "Bom",
                columns: new[] { "OrganizationId", "PrimaryOutputItemId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "UX_Bom_Org_Code",
                schema: "organization",
                table: "Bom",
                columns: new[] { "OrganizationId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_BomApplicability_Org_Version",
                schema: "organization",
                table: "BomApplicability",
                columns: new[] { "OrganizationId", "BomVersionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BomApplicabilityCondition_OrganizationId_BomApplicabilityId",
                schema: "organization",
                table: "BomApplicabilityCondition",
                columns: new[] { "OrganizationId", "BomApplicabilityId" });

            migrationBuilder.CreateIndex(
                name: "IX_BomApplicabilityCondition_OrganizationId_ContextDimensionId",
                schema: "organization",
                table: "BomApplicabilityCondition",
                columns: new[] { "OrganizationId", "ContextDimensionId" });

            migrationBuilder.CreateIndex(
                name: "UX_BomApplicabilityCondition_Dim_Op",
                schema: "organization",
                table: "BomApplicabilityCondition",
                columns: new[] { "BomApplicabilityId", "ContextDimensionId", "Operator" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BomInput_Org_Ver_Seq",
                schema: "organization",
                table: "BomInput",
                columns: new[] { "OrganizationId", "BomVersionId", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_BomInput_OrganizationId_ItemId",
                schema: "organization",
                table: "BomInput",
                columns: new[] { "OrganizationId", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_BomInput_OrganizationId_UnitId",
                schema: "organization",
                table: "BomInput",
                columns: new[] { "OrganizationId", "UnitId" });

            migrationBuilder.CreateIndex(
                name: "IX_BomOutput_Org_Ver_Seq",
                schema: "organization",
                table: "BomOutput",
                columns: new[] { "OrganizationId", "BomVersionId", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_BomOutput_OrganizationId_ItemId",
                schema: "organization",
                table: "BomOutput",
                columns: new[] { "OrganizationId", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_BomOutput_OrganizationId_UnitId",
                schema: "organization",
                table: "BomOutput",
                columns: new[] { "OrganizationId", "UnitId" });

            migrationBuilder.CreateIndex(
                name: "IX_BomVersion_OrganizationId_BaseOutputUnitId",
                schema: "organization",
                table: "BomVersion",
                columns: new[] { "OrganizationId", "BaseOutputUnitId" });

            migrationBuilder.CreateIndex(
                name: "UX_BomVersion_Org_Bom_Ver",
                schema: "organization",
                table: "BomVersion",
                columns: new[] { "OrganizationId", "BomId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ContextDimension_Org_Code",
                schema: "organization",
                table: "ContextDimension",
                columns: new[] { "OrganizationId", "Code" },
                unique: true);

            migrationBuilder.Sql("""
                IF DATABASE_PRINCIPAL_ID(N'ForgeFlowOrganizationRuntime') IS NOT NULL
                BEGIN
                    GRANT SELECT, INSERT, UPDATE ON OBJECT::[organization].[Bom] TO [ForgeFlowOrganizationRuntime];
                    GRANT SELECT, INSERT, UPDATE ON OBJECT::[organization].[BomVersion] TO [ForgeFlowOrganizationRuntime];
                    GRANT SELECT, INSERT, UPDATE ON OBJECT::[organization].[ContextDimension] TO [ForgeFlowOrganizationRuntime];
                    GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[organization].[BomInput] TO [ForgeFlowOrganizationRuntime];
                    GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[organization].[BomOutput] TO [ForgeFlowOrganizationRuntime];
                    GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[organization].[BomApplicability] TO [ForgeFlowOrganizationRuntime];
                    GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[organization].[BomApplicabilityCondition] TO [ForgeFlowOrganizationRuntime];
                    DENY DELETE ON OBJECT::[organization].[Bom] TO [ForgeFlowOrganizationRuntime];
                    DENY DELETE ON OBJECT::[organization].[BomVersion] TO [ForgeFlowOrganizationRuntime];
                    DENY DELETE ON OBJECT::[organization].[ContextDimension] TO [ForgeFlowOrganizationRuntime];
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [organization].[Bom])
                   OR EXISTS (SELECT 1 FROM [organization].[BomVersion])
                   OR EXISTS (SELECT 1 FROM [organization].[ContextDimension])
                    THROW 51001, 'OrganizationSite.NonEmptyRollbackDenied', 1;
                """);

            migrationBuilder.DropTable(
                name: "BomApplicabilityCondition",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "BomInput",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "BomOutput",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "BomApplicability",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "ContextDimension",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "BomVersion",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "Bom",
                schema: "organization");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OrganizationChange_Event",
                schema: "organization",
                table: "OrganizationChange");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrganizationChange_Event",
                schema: "organization",
                table: "OrganizationChange",
                sql: "DATALENGTH([EventCode]) = LEN([EventCode]) AND (([SiteId] IS NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('Organization.Created','Organization.Renamed','Organization.Activated','Organization.Deactivated','Unit.Created','Unit.Updated','Unit.Activated','Unit.Deactivated','UnitConversion.Created','UnitConversion.Updated','UnitConversion.Activated','UnitConversion.Deactivated','Item.Created','Item.Updated','Item.Activated','Item.Deactivated','ClassificationSet.Created','Classification.Created','Classification.Updated','Classification.Activated','Classification.Deactivated','ItemClassification.Updated','ItemUnitConversion.Created','ItemUnitConversion.Updated','ItemUnitConversion.Activated','ItemUnitConversion.Deactivated','AttributeDefinition.Created','AttributeDefinition.Updated','AttributeDefinition.Activated','AttributeDefinition.Deactivated','ItemAttributeValue.Updated')) OR ([SiteId] IS NOT NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('Site.Created','Site.Renamed','Site.Activated','Site.Deactivated')))");
        }
    }
}
