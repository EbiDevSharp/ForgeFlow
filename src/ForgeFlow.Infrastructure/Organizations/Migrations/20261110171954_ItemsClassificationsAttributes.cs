using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ForgeFlow.Infrastructure.Organizations.Migrations
{
    /// <inheritdoc />
    public partial class ItemsClassificationsAttributes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_OrganizationChange_Event",
                schema: "organization",
                table: "OrganizationChange");

            migrationBuilder.CreateTable(
                name: "AttributeDefinition",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    Code = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DataType = table.Column<byte>(type: "tinyint", nullable: false),
                    UnitId = table.Column<long>(type: "bigint", nullable: true),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CreatedByActorId = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    UpdatedByActorId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttributeDefinition", x => x.Id);
                    table.UniqueConstraint("AK_AttributeDefinition_Org_Id", x => new { x.OrganizationId, x.Id });
                    table.CheckConstraint("CK_AttributeDefinition_Code", "DATALENGTH([Code]) BETWEEN 1 AND 32 AND DATALENGTH([Code]) = LEN([Code]) AND [Code] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([Code], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'");
                    table.CheckConstraint("CK_AttributeDefinition_DataType", "[DataType] BETWEEN 1 AND 3");
                    table.CheckConstraint("CK_AttributeDefinition_Metadata", "[CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))");
                    table.CheckConstraint("CK_AttributeDefinition_Name", "DATALENGTH([Name]) BETWEEN 2 AND 400 AND DATALENGTH([Name]) = DATALENGTH(LTRIM(RTRIM([Name]))) AND DATALENGTH(LTRIM(RTRIM([Name]))) > 0");
                    table.ForeignKey(
                        name: "FK_AttributeDefinition_Organization",
                        column: x => x.OrganizationId,
                        principalSchema: "organization",
                        principalTable: "Organization",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AttributeDefinition_Unit",
                        columns: x => new { x.OrganizationId, x.UnitId },
                        principalSchema: "organization",
                        principalTable: "Unit",
                        principalColumns: new[] { "OrganizationId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "ClassificationSet",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    Code = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsMandatory = table.Column<bool>(type: "bit", nullable: false),
                    AllowMultiple = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CreatedByActorId = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    UpdatedByActorId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClassificationSet", x => x.Id);
                    table.UniqueConstraint("AK_ClassificationSet_Org_Id", x => new { x.OrganizationId, x.Id });
                    table.CheckConstraint("CK_ClassificationSet_Code", "DATALENGTH([Code]) BETWEEN 1 AND 32 AND DATALENGTH([Code]) = LEN([Code]) AND [Code] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([Code], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'");
                    table.CheckConstraint("CK_ClassificationSet_Metadata", "[CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))");
                    table.CheckConstraint("CK_ClassificationSet_Name", "DATALENGTH([Name]) BETWEEN 2 AND 400 AND DATALENGTH([Name]) = DATALENGTH(LTRIM(RTRIM([Name]))) AND DATALENGTH(LTRIM(RTRIM([Name]))) > 0");
                    table.ForeignKey(
                        name: "FK_ClassificationSet_Organization",
                        column: x => x.OrganizationId,
                        principalSchema: "organization",
                        principalTable: "Organization",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Item",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    Code = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    BaseUnitId = table.Column<long>(type: "bigint", nullable: false),
                    CanBePurchased = table.Column<bool>(type: "bit", nullable: false),
                    CanBeManufactured = table.Column<bool>(type: "bit", nullable: false),
                    CanBeSold = table.Column<bool>(type: "bit", nullable: false),
                    CanBeConsumed = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CreatedByActorId = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    UpdatedByActorId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Item", x => x.Id);
                    table.UniqueConstraint("AK_Item_Organization_Id", x => new { x.OrganizationId, x.Id });
                    table.CheckConstraint("CK_Item_Capability", "[CanBePurchased] = 1 OR [CanBeManufactured] = 1 OR [CanBeSold] = 1 OR [CanBeConsumed] = 1");
                    table.CheckConstraint("CK_Item_Code", "DATALENGTH([Code]) BETWEEN 1 AND 32 AND DATALENGTH([Code]) = LEN([Code]) AND [Code] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([Code], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'");
                    table.CheckConstraint("CK_Item_Metadata", "[CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))");
                    table.CheckConstraint("CK_Item_Name", "DATALENGTH([Name]) BETWEEN 2 AND 400 AND DATALENGTH([Name]) = DATALENGTH(LTRIM(RTRIM([Name]))) AND DATALENGTH(LTRIM(RTRIM([Name]))) > 0");
                    table.ForeignKey(
                        name: "FK_Item_BaseUnit",
                        columns: x => new { x.OrganizationId, x.BaseUnitId },
                        principalSchema: "organization",
                        principalTable: "Unit",
                        principalColumns: new[] { "OrganizationId", "Id" });
                    table.ForeignKey(
                        name: "FK_Item_Organization",
                        column: x => x.OrganizationId,
                        principalSchema: "organization",
                        principalTable: "Organization",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Classification",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    ClassificationSetId = table.Column<long>(type: "bigint", nullable: false),
                    ParentClassificationId = table.Column<long>(type: "bigint", nullable: true),
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
                    table.PrimaryKey("PK_Classification", x => x.Id);
                    table.UniqueConstraint("AK_Classification_Org_Id", x => new { x.OrganizationId, x.Id });
                    table.CheckConstraint("CK_Classification_Code", "DATALENGTH([Code]) BETWEEN 1 AND 32 AND DATALENGTH([Code]) = LEN([Code]) AND [Code] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([Code], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'");
                    table.CheckConstraint("CK_Classification_Metadata", "[CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))");
                    table.CheckConstraint("CK_Classification_Name", "DATALENGTH([Name]) BETWEEN 2 AND 400 AND DATALENGTH([Name]) = DATALENGTH(LTRIM(RTRIM([Name]))) AND DATALENGTH(LTRIM(RTRIM([Name]))) > 0");
                    table.CheckConstraint("CK_Classification_NoSelfParent", "[ParentClassificationId] IS NULL OR [ParentClassificationId] <> [Id]");
                    table.ForeignKey(
                        name: "FK_Classification_Parent",
                        columns: x => new { x.OrganizationId, x.ParentClassificationId },
                        principalSchema: "organization",
                        principalTable: "Classification",
                        principalColumns: new[] { "OrganizationId", "Id" });
                    table.ForeignKey(
                        name: "FK_Classification_Set",
                        columns: x => new { x.OrganizationId, x.ClassificationSetId },
                        principalSchema: "organization",
                        principalTable: "ClassificationSet",
                        principalColumns: new[] { "OrganizationId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "ItemAttributeValue",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    ItemId = table.Column<long>(type: "bigint", nullable: false),
                    AttributeDefinitionId = table.Column<long>(type: "bigint", nullable: false),
                    TextValue = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    DecimalValue = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    BooleanValue = table.Column<bool>(type: "bit", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CreatedByActorId = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    UpdatedByActorId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemAttributeValue", x => x.Id);
                    table.CheckConstraint("CK_ItemAttributeValue_Metadata", "[CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))");
                    table.ForeignKey(
                        name: "FK_ItemAttributeValue_Definition",
                        columns: x => new { x.OrganizationId, x.AttributeDefinitionId },
                        principalSchema: "organization",
                        principalTable: "AttributeDefinition",
                        principalColumns: new[] { "OrganizationId", "Id" });
                    table.ForeignKey(
                        name: "FK_ItemAttributeValue_Item",
                        columns: x => new { x.OrganizationId, x.ItemId },
                        principalSchema: "organization",
                        principalTable: "Item",
                        principalColumns: new[] { "OrganizationId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "ItemUnitConversion",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    ItemId = table.Column<long>(type: "bigint", nullable: false),
                    FromUnitId = table.Column<long>(type: "bigint", nullable: false),
                    ToUnitId = table.Column<long>(type: "bigint", nullable: false),
                    Factor = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CreatedByActorId = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    UpdatedByActorId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemUnitConversion", x => x.Id);
                    table.CheckConstraint("CK_ItemUnitConversion_DifferentUnits", "[FromUnitId] <> [ToUnitId]");
                    table.CheckConstraint("CK_ItemUnitConversion_Metadata", "[CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))");
                    table.CheckConstraint("CK_ItemUnitConversion_PositiveFactor", "[Factor] > 0");
                    table.ForeignKey(
                        name: "FK_ItemUnitConversion_FromUnit",
                        columns: x => new { x.OrganizationId, x.FromUnitId },
                        principalSchema: "organization",
                        principalTable: "Unit",
                        principalColumns: new[] { "OrganizationId", "Id" });
                    table.ForeignKey(
                        name: "FK_ItemUnitConversion_Item",
                        columns: x => new { x.OrganizationId, x.ItemId },
                        principalSchema: "organization",
                        principalTable: "Item",
                        principalColumns: new[] { "OrganizationId", "Id" });
                    table.ForeignKey(
                        name: "FK_ItemUnitConversion_ToUnit",
                        columns: x => new { x.OrganizationId, x.ToUnitId },
                        principalSchema: "organization",
                        principalTable: "Unit",
                        principalColumns: new[] { "OrganizationId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "ItemClassification",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<long>(type: "bigint", nullable: false),
                    ItemId = table.Column<long>(type: "bigint", nullable: false),
                    ClassificationId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CreatedByActorId = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    UpdatedByActorId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemClassification", x => x.Id);
                    table.CheckConstraint("CK_ItemClassification_Metadata", "[CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))");
                    table.ForeignKey(
                        name: "FK_ItemClassification_Classification",
                        columns: x => new { x.OrganizationId, x.ClassificationId },
                        principalSchema: "organization",
                        principalTable: "Classification",
                        principalColumns: new[] { "OrganizationId", "Id" });
                    table.ForeignKey(
                        name: "FK_ItemClassification_Item",
                        columns: x => new { x.OrganizationId, x.ItemId },
                        principalSchema: "organization",
                        principalTable: "Item",
                        principalColumns: new[] { "OrganizationId", "Id" });
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrganizationChange_Event",
                schema: "organization",
                table: "OrganizationChange",
                sql: "DATALENGTH([EventCode]) = LEN([EventCode]) AND (([SiteId] IS NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('Organization.Created','Organization.Renamed','Organization.Activated','Organization.Deactivated','Unit.Created','Unit.Updated','Unit.Activated','Unit.Deactivated','UnitConversion.Created','UnitConversion.Updated','UnitConversion.Activated','UnitConversion.Deactivated','Item.Created','Item.Updated','Item.Activated','Item.Deactivated','ClassificationSet.Created','Classification.Created','Classification.Updated','Classification.Activated','Classification.Deactivated','ItemClassification.Updated','ItemUnitConversion.Created','ItemUnitConversion.Updated','ItemUnitConversion.Activated','ItemUnitConversion.Deactivated','AttributeDefinition.Created','AttributeDefinition.Updated','AttributeDefinition.Activated','AttributeDefinition.Deactivated','ItemAttributeValue.Updated')) OR ([SiteId] IS NOT NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('Site.Created','Site.Renamed','Site.Activated','Site.Deactivated')))");

            migrationBuilder.CreateIndex(
                name: "IX_AttributeDefinition_OrganizationId_UnitId",
                schema: "organization",
                table: "AttributeDefinition",
                columns: new[] { "OrganizationId", "UnitId" });

            migrationBuilder.CreateIndex(
                name: "UX_AttributeDefinition_Org_Code",
                schema: "organization",
                table: "AttributeDefinition",
                columns: new[] { "OrganizationId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Classification_OrganizationId_ParentClassificationId",
                schema: "organization",
                table: "Classification",
                columns: new[] { "OrganizationId", "ParentClassificationId" });

            migrationBuilder.CreateIndex(
                name: "UX_Classification_Org_Set_Code",
                schema: "organization",
                table: "Classification",
                columns: new[] { "OrganizationId", "ClassificationSetId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ClassificationSet_Org_Code",
                schema: "organization",
                table: "ClassificationSet",
                columns: new[] { "OrganizationId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Item_Organization_IsActive_Code",
                schema: "organization",
                table: "Item",
                columns: new[] { "OrganizationId", "IsActive", "Code", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Item_OrganizationId_BaseUnitId",
                schema: "organization",
                table: "Item",
                columns: new[] { "OrganizationId", "BaseUnitId" });

            migrationBuilder.CreateIndex(
                name: "UX_Item_Organization_Code",
                schema: "organization",
                table: "Item",
                columns: new[] { "OrganizationId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ItemAttributeValue_OrganizationId_AttributeDefinitionId",
                schema: "organization",
                table: "ItemAttributeValue",
                columns: new[] { "OrganizationId", "AttributeDefinitionId" });

            migrationBuilder.CreateIndex(
                name: "UX_ItemAttributeValue_Org_Item_Def",
                schema: "organization",
                table: "ItemAttributeValue",
                columns: new[] { "OrganizationId", "ItemId", "AttributeDefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ItemClassification_OrganizationId_ClassificationId",
                schema: "organization",
                table: "ItemClassification",
                columns: new[] { "OrganizationId", "ClassificationId" });

            migrationBuilder.CreateIndex(
                name: "UX_ItemClassification_Org_Item_Class",
                schema: "organization",
                table: "ItemClassification",
                columns: new[] { "OrganizationId", "ItemId", "ClassificationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ItemUnitConversion_OrganizationId_FromUnitId",
                schema: "organization",
                table: "ItemUnitConversion",
                columns: new[] { "OrganizationId", "FromUnitId" });

            migrationBuilder.CreateIndex(
                name: "IX_ItemUnitConversion_OrganizationId_ToUnitId",
                schema: "organization",
                table: "ItemUnitConversion",
                columns: new[] { "OrganizationId", "ToUnitId" });

            migrationBuilder.CreateIndex(
                name: "UX_ItemUnitConversion_Org_Item_From_To",
                schema: "organization",
                table: "ItemUnitConversion",
                columns: new[] { "OrganizationId", "ItemId", "FromUnitId", "ToUnitId" },
                unique: true);

            migrationBuilder.Sql("""
                IF DATABASE_PRINCIPAL_ID(N'ForgeFlowOrganizationRuntime') IS NOT NULL
                BEGIN
                    GRANT SELECT, INSERT, UPDATE ON OBJECT::[organization].[ClassificationSet] TO [ForgeFlowOrganizationRuntime];
                    GRANT SELECT, INSERT, UPDATE ON OBJECT::[organization].[Classification] TO [ForgeFlowOrganizationRuntime];
                    GRANT SELECT, INSERT, UPDATE ON OBJECT::[organization].[Item] TO [ForgeFlowOrganizationRuntime];
                    GRANT SELECT, INSERT, UPDATE ON OBJECT::[organization].[ItemUnitConversion] TO [ForgeFlowOrganizationRuntime];
                    GRANT SELECT, INSERT, UPDATE ON OBJECT::[organization].[AttributeDefinition] TO [ForgeFlowOrganizationRuntime];
                    GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[organization].[ItemClassification] TO [ForgeFlowOrganizationRuntime];
                    GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[organization].[ItemAttributeValue] TO [ForgeFlowOrganizationRuntime];
                    DENY DELETE ON OBJECT::[organization].[ClassificationSet] TO [ForgeFlowOrganizationRuntime];
                    DENY DELETE ON OBJECT::[organization].[Classification] TO [ForgeFlowOrganizationRuntime];
                    DENY DELETE ON OBJECT::[organization].[Item] TO [ForgeFlowOrganizationRuntime];
                    DENY DELETE ON OBJECT::[organization].[ItemUnitConversion] TO [ForgeFlowOrganizationRuntime];
                    DENY DELETE ON OBJECT::[organization].[AttributeDefinition] TO [ForgeFlowOrganizationRuntime];
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [organization].[Item])
                   OR EXISTS (SELECT 1 FROM [organization].[ClassificationSet])
                   OR EXISTS (SELECT 1 FROM [organization].[Classification])
                   OR EXISTS (SELECT 1 FROM [organization].[AttributeDefinition])
                    THROW 51001, 'OrganizationSite.NonEmptyRollbackDenied', 1;
                """);

            migrationBuilder.DropTable(
                name: "ItemAttributeValue",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "ItemClassification",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "ItemUnitConversion",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "AttributeDefinition",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "Classification",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "Item",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "ClassificationSet",
                schema: "organization");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OrganizationChange_Event",
                schema: "organization",
                table: "OrganizationChange");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrganizationChange_Event",
                schema: "organization",
                table: "OrganizationChange",
                sql: "DATALENGTH([EventCode]) = LEN([EventCode]) AND (([SiteId] IS NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('Organization.Created','Organization.Renamed','Organization.Activated','Organization.Deactivated','Unit.Created','Unit.Updated','Unit.Activated','Unit.Deactivated','UnitConversion.Created','UnitConversion.Updated','UnitConversion.Activated','UnitConversion.Deactivated')) OR ([SiteId] IS NOT NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('Site.Created','Site.Renamed','Site.Activated','Site.Deactivated')))");
        }
    }
}
