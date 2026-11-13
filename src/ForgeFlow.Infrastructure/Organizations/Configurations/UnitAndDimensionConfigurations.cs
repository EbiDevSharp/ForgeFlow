using ForgeFlow.Domain.Boms;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Domain.Units;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using static ForgeFlow.Infrastructure.Organizations.Configurations.OrganizationModelConventions;

namespace ForgeFlow.Infrastructure.Organizations.Configurations;

internal sealed class UnitConfiguration : IEntityTypeConfiguration<Unit>
{
    public void Configure(EntityTypeBuilder<Unit> unit)
    {
        unit.ToTable("Unit", Schema, table =>
        {
            table.HasCheckConstraint("CK_Unit_Code", CodeCheck);
            table.HasCheckConstraint("CK_Unit_Name", NameCheck);
            table.HasCheckConstraint("CK_Unit_Symbol", SymbolCheck);
            table.HasCheckConstraint("CK_Unit_Dimension", "[Dimension] BETWEEN 1 AND 6");
            table.HasCheckConstraint("CK_Unit_PrecisionScale", "[PrecisionScale] BETWEEN 0 AND 6");
            table.HasCheckConstraint("CK_Unit_Metadata", MetadataCheck);
        });
        unit.HasKey(u => u.Id).HasName("PK_Unit");
        unit.Property(u => u.Id).UseIdentityColumn();
        unit.Property(u => u.OrganizationId).IsRequired();
        unit.Property(u => u.Code).HasMaxLength(32).IsUnicode(false).UseCollation(CodeCollation).IsRequired();
        unit.Property(u => u.Name).HasMaxLength(200).IsRequired();
        unit.Property(u => u.Symbol).HasMaxLength(32).IsRequired();
        unit.Property(u => u.Dimension).HasConversion<byte>().IsRequired();
        unit.Property(u => u.PrecisionScale).IsRequired();
        unit.Property(u => u.IsActive).IsRequired();
        AddAuditMetadata(unit);
        unit.HasAlternateKey(u => new { u.OrganizationId, u.Id }).HasName("AK_Unit_Organization_Id");
        unit.HasIndex(u => new { u.OrganizationId, u.Code }).IsUnique().HasDatabaseName("UX_Unit_Organization_Code");
        unit.HasIndex(u => new { u.OrganizationId, u.IsActive, u.Code, u.Id }).HasDatabaseName("IX_Unit_Organization_IsActive_Code");
        unit.HasOne<Organization>().WithMany().HasForeignKey(u => u.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_Unit_Organization");
    }
}

internal sealed class UnitConversionConfiguration : IEntityTypeConfiguration<UnitConversion>
{
    public void Configure(EntityTypeBuilder<UnitConversion> conversion)
    {
        conversion.ToTable("UnitConversion", Schema, table =>
        {
            table.HasCheckConstraint("CK_UnitConversion_DifferentUnits", "[FromUnitId] <> [ToUnitId]");
            table.HasCheckConstraint("CK_UnitConversion_PositiveFactor", "[Factor] > 0");
            table.HasCheckConstraint("CK_UnitConversion_Metadata", MetadataCheck);
        });
        conversion.HasKey(c => c.Id).HasName("PK_UnitConversion");
        conversion.Property(c => c.Id).UseIdentityColumn();
        conversion.Property(c => c.OrganizationId).IsRequired();
        conversion.Property(c => c.FromUnitId).IsRequired();
        conversion.Property(c => c.ToUnitId).IsRequired();
        conversion.Property(c => c.Factor).HasPrecision(18, 6).IsRequired();
        conversion.Property(c => c.IsActive).IsRequired();
        AddAuditMetadata(conversion);
        conversion.HasIndex(c => new { c.OrganizationId, c.FromUnitId, c.ToUnitId }).IsUnique().HasDatabaseName("UX_UnitConversion_Organization_From_To");
        conversion.HasOne<Unit>().WithMany().HasForeignKey(c => new { c.OrganizationId, c.FromUnitId })
            .HasPrincipalKey(u => new { u.OrganizationId, u.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_UnitConversion_FromUnit");
        conversion.HasOne<Unit>().WithMany().HasForeignKey(c => new { c.OrganizationId, c.ToUnitId })
            .HasPrincipalKey(u => new { u.OrganizationId, u.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_UnitConversion_ToUnit");
    }
}

internal sealed class CurrencyConfiguration : IEntityTypeConfiguration<Currency>
{
    public void Configure(EntityTypeBuilder<Currency> currency)
    {
        currency.ToTable("Currency", Schema, table =>
        {
            table.HasCheckConstraint("CK_Currency_Code", CurrencyCodeCheck);
            table.HasCheckConstraint("CK_Currency_Name", NameCheck);
            table.HasCheckConstraint("CK_Currency_Symbol", SymbolCheck);
            table.HasCheckConstraint("CK_Currency_PrecisionScale", "[PrecisionScale] BETWEEN 0 AND 6");
            table.HasCheckConstraint("CK_Currency_Metadata", MetadataCheck);
        });
        currency.HasKey(c => c.Id).HasName("PK_Currency");
        currency.Property(c => c.Id).UseIdentityColumn();
        currency.Property(c => c.OrganizationId).IsRequired();
        currency.Property(c => c.Code).HasMaxLength(3).IsUnicode(false).UseCollation(CodeCollation).IsRequired();
        currency.Property(c => c.Name).HasMaxLength(200).IsRequired();
        currency.Property(c => c.Symbol).HasMaxLength(32).IsRequired();
        currency.Property(c => c.PrecisionScale).IsRequired();
        currency.Property(c => c.IsActive).IsRequired();
        AddAuditMetadata(currency);
        currency.HasAlternateKey(c => new { c.OrganizationId, c.Id }).HasName("AK_Currency_Organization_Id");
        currency.HasAlternateKey(c => new { c.OrganizationId, c.Code }).HasName("AK_Currency_Organization_Code");
        currency.HasIndex(c => new { c.OrganizationId, c.Code }).IsUnique().HasDatabaseName("UX_Currency_Organization_Code");
        currency.HasIndex(c => new { c.OrganizationId, c.IsActive, c.Code, c.Id }).HasDatabaseName("IX_Currency_Organization_IsActive_Code");
        currency.HasOne<Organization>().WithMany().HasForeignKey(c => c.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_Currency_Organization");
    }
}

internal sealed class ContextDimensionConfiguration : IEntityTypeConfiguration<ContextDimension>
{
    public void Configure(EntityTypeBuilder<ContextDimension> dim)
    {
        dim.ToTable("ContextDimension", Schema, table =>
        {
            table.HasCheckConstraint("CK_ContextDimension_Code", CodeCheck);
            table.HasCheckConstraint("CK_ContextDimension_Name", NameCheck);
            table.HasCheckConstraint("CK_ContextDimension_Metadata", MetadataCheck);
        });
        dim.HasKey(d => d.Id).HasName("PK_ContextDimension");
        dim.Property(d => d.Id).UseIdentityColumn();
        dim.Property(d => d.OrganizationId).IsRequired();
        dim.Property(d => d.Code).HasMaxLength(32).IsUnicode(false).UseCollation(CodeCollation).IsRequired();
        dim.Property(d => d.Name).HasMaxLength(200).IsRequired();
        dim.Property(d => d.IsActive).IsRequired();
        AddAuditMetadata(dim);
        dim.HasAlternateKey(d => new { d.OrganizationId, d.Id }).HasName("AK_ContextDimension_Org_Id");
        dim.HasIndex(d => new { d.OrganizationId, d.Code }).IsUnique().HasDatabaseName("UX_ContextDimension_Org_Code");
        dim.HasOne<Organization>().WithMany().HasForeignKey(d => d.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_ContextDimension_Organization");
    }
}
