using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ForgeFlow.Infrastructure.Organizations.Configurations;

internal static class OrganizationModelConventions
{
    internal const string Schema = "organization";
    internal const string CodeCollation = "Latin1_General_100_BIN2";
    internal static readonly ValueConverter<DateTime, DateTime> Utc = new(
        value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
    internal const string CodeCheck = "DATALENGTH([Code]) BETWEEN 1 AND 32 AND DATALENGTH([Code]) = LEN([Code]) AND [Code] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([Code], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'";
    internal const string NameCheck = "DATALENGTH([Name]) BETWEEN 2 AND 400 AND DATALENGTH([Name]) = DATALENGTH(LTRIM(RTRIM([Name]))) AND DATALENGTH(LTRIM(RTRIM([Name]))) > 0";
    internal const string SymbolCheck = "DATALENGTH([Symbol]) BETWEEN 2 AND 64 AND DATALENGTH([Symbol]) = DATALENGTH(LTRIM(RTRIM([Symbol]))) AND DATALENGTH(LTRIM(RTRIM([Symbol]))) > 0";
    internal const string MetadataCheck = "[CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))";
    internal const string CurrencyCodeCheck = "DATALENGTH([Code]) = 3 AND DATALENGTH([Code]) = LEN([Code]) AND [Code] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z]%'";
    internal const string DocNumberCheck = "DATALENGTH([DocumentNumber]) BETWEEN 1 AND 32 AND DATALENGTH([DocumentNumber]) = LEN([DocumentNumber]) AND [DocumentNumber] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([DocumentNumber], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'";
    internal const string LotCodeCheck = "([LotCode] = '' OR (DATALENGTH([LotCode]) BETWEEN 1 AND 32 AND DATALENGTH([LotCode]) = LEN([LotCode]) AND [LotCode] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([LotCode], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'))";
    internal const string RequiredLotCodeCheck = "DATALENGTH([LotCode]) BETWEEN 1 AND 32 AND DATALENGTH([LotCode]) = LEN([LotCode]) AND [LotCode] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([LotCode], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'";
    internal const string InspectionNumCheck = "DATALENGTH([InspectionNumber]) BETWEEN 1 AND 32 AND DATALENGTH([InspectionNumber]) = LEN([InspectionNumber]) AND [InspectionNumber] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([InspectionNumber], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'";

    internal static void Business<T>(EntityTypeBuilder<T> entity, string tableName) where T : class
    {
        entity.ToTable(tableName, Schema, table =>
        {
            table.HasCheckConstraint($"CK_{tableName}_Code", CodeCheck);
            table.HasCheckConstraint($"CK_{tableName}_Name", NameCheck);
            table.HasCheckConstraint($"CK_{tableName}_Metadata", MetadataCheck);
        });
        entity.Property<string>("Code").HasMaxLength(32).IsUnicode(false).UseCollation(CodeCollation).IsRequired();
        entity.Property<string>("Name").HasMaxLength(200).IsRequired();
        entity.Property<bool>("IsActive").IsRequired();
        AddAuditMetadata(entity);
    }

    internal static void AddAuditMetadata<T>(EntityTypeBuilder<T> entity) where T : class
    {
        entity.Property<DateTime>("CreatedAtUtc").HasColumnType("datetime2(7)").HasConversion(Utc).IsRequired();
        entity.Property<long>("CreatedByActorId").IsRequired();
        entity.Property<DateTime?>("UpdatedAtUtc").HasColumnType("datetime2(7)").HasConversion(Utc);
        entity.Property<long?>("UpdatedByActorId");
        entity.Property<byte[]>("RowVersion").IsRowVersion().IsRequired();
    }
}
