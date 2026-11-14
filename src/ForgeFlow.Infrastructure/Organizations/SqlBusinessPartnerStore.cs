using System.Collections;
using System.Data;
using ForgeFlow.Application.BusinessPartners;
using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.BusinessPartners;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Domain.Procurement;
using ForgeFlow.Domain.Sales;
using ForgeFlow.Infrastructure.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ForgeFlow.Infrastructure.Organizations;

public sealed class SqlBusinessPartnerStore(
    Func<IDbContextFactory<OrganizationDbContext>> getFactory,
    IErrorLogRecorder? errorRecorder = null,
    IOperationContextAccessor? opContext = null)
    : IBusinessPartnerStore
{
    private IDbContextFactory<OrganizationDbContext> Factory => getFactory();

    public SqlBusinessPartnerStore(IDbContextFactory<OrganizationDbContext> factory)
        : this(() => factory) { }

    public async Task<IReadOnlyList<BusinessPartnerRoleDto>> ListRoleDefinitions(CancellationToken ct)
    {
        await using var db = await Factory.CreateDbContextAsync(ct);
        var roles = await db.BusinessPartnerRoleDefinitions.AsNoTracking()
            .Where(r => r.IsActive)
            .OrderBy(r => r.DisplayOrder)
            .ToListAsync(ct);

        if (roles.Count == 0)
        {
            return
            [
                new(BusinessPartnerRoleDefinition.RoleSupplier, "SUPPLIER", "Supplier"),
                new(BusinessPartnerRoleDefinition.RoleCustomer, "CUSTOMER", "Customer"),
                new(BusinessPartnerRoleDefinition.RoleContractor, "CONTRACTOR", "Contractor")
            ];
        }

        return roles.Select(r => new BusinessPartnerRoleDto(r.Id, r.Code, r.Name)).ToList();
    }

    public Task<OrganizationPage<BusinessPartnerSummary>> List(
        long organizationId,
        BusinessPartnerListQuery query,
        CancellationToken ct) => Read(async db =>
    {
        var baseQuery = db.BusinessPartners.AsNoTracking()
            .Where(b => b.OrganizationId == organizationId);

        if (!query.IncludeInactive)
            baseQuery = baseQuery.Where(b => b.IsActive);

        if (query.RoleFilter.HasValue && query.RoleFilter.Value > 0)
        {
            var roleId = query.RoleFilter.Value;
            baseQuery = baseQuery.Where(b => b.Roles.Any(r => r.RoleId == roleId));
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            baseQuery = baseQuery.Where(b =>
                b.Code.Contains(term)
                || b.Name.Contains(term)
                || (b.CompanyName != null && b.CompanyName.Contains(term))
                || (b.NationalCode != null && b.NationalCode.Contains(term))
                || (b.NationalLegalId != null && b.NationalLegalId.Contains(term))
                || (b.EconomicCode != null && b.EconomicCode.Contains(term))
                || (b.Mobile != null && b.Mobile.Contains(term))
                || (b.Phone != null && b.Phone.Contains(term))
                || (b.Email != null && b.Email.Contains(term))
                || (b.City != null && b.City.Contains(term)));
        }

        var total = await baseQuery.CountAsync(ct);
        var rows = await baseQuery
            .OrderBy(b => b.Code).ThenBy(b => b.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(b => new
            {
                b,
                RoleIds = b.Roles.Select(r => r.RoleId).ToList(),
                RowVersion = EF.Property<byte[]>(b, "RowVersion")
            })
            .ToListAsync(ct);

        var items = rows.Select(r => ToSummary(r.b, r.RoleIds, r.RowVersion)).ToList();
        return new OrganizationPage<BusinessPartnerSummary>(items, total, query.Page, query.PageSize);
    }, ct);

    public Task<BusinessPartnerDetail?> Get(
        long organizationId,
        long id,
        CancellationToken ct) => Read(async db =>
    {
        var row = await db.BusinessPartners.AsNoTracking()
            .Include(b => b.Roles)
            .ThenInclude(r => r.RoleDefinition)
            .Where(b => b.OrganizationId == organizationId && b.Id == id)
            .Select(b => new
            {
                b,
                Roles = b.Roles.Select(r => new BusinessPartnerRoleDto(r.RoleId, r.RoleDefinition != null ? r.RoleDefinition.Code : (r.RoleId == 1 ? "SUPPLIER" : r.RoleId == 2 ? "CUSTOMER" : "CONTRACTOR"), r.RoleDefinition != null ? r.RoleDefinition.Name : (r.RoleId == 1 ? "Supplier" : r.RoleId == 2 ? "Customer" : "Contractor"))).ToList(),
                RowVersion = EF.Property<byte[]>(b, "RowVersion")
            })
            .SingleOrDefaultAsync(ct);

        return row is null ? null : ToDetail(row.b, row.Roles, row.RowVersion);
    }, ct);

    public Task<OrganizationResult<BusinessPartnerDetail>> Create(
        CreateBusinessPartnerRequest request,
        OrganizationWriteAccess access,
        CancellationToken ct) => Write(access, async db =>
    {
        var orgCheck = await LockAndRequireActiveOrganization(db, request.OrganizationId, ct);
        if (!orgCheck.Ok) return OrganizationResult<BusinessPartnerDetail>.Failure(orgCheck.Error!);

        var code = await SqlDocumentSequenceStore.AllocateAsync(
            db,
            request.OrganizationId,
            DocumentSequenceType.BusinessPartner,
            request.Code,
            DateTime.UtcNow,
            ct);

        var partner = BusinessPartner.Create(
            request.OrganizationId,
            code,
            request.PersonType,
            request.Name,
            request.FirstName,
            request.LastName,
            request.NationalCode,
            request.CompanyName,
            request.NationalLegalId,
            request.EconomicCode,
            request.Mobile,
            request.Phone,
            request.Email,
            request.Website,
            request.Country,
            request.Province,
            request.City,
            request.Address,
            request.PostalCode,
            request.Notes,
            request.RoleIds);

        var duplicateCode = await db.BusinessPartners.AsNoTracking()
            .AnyAsync(b => b.OrganizationId == request.OrganizationId && b.Code == partner.Code, ct);
        if (duplicateCode)
            return OrganizationResult<BusinessPartnerDetail>.Failure(BusinessPartnerErrors.DuplicateCode);

        db.BusinessPartners.Add(partner);

        // Synchronize with Supplier table if Supplier role assigned
        if (partner.IsSupplier)
        {
            var existingSup = await db.Suppliers
                .SingleOrDefaultAsync(s => s.OrganizationId == request.OrganizationId && s.Code == partner.Code, ct);
            if (existingSup is null)
            {
                var sup = Supplier.Create(
                    request.OrganizationId,
                    partner.Code,
                    partner.Name,
                    partner.PersonType == PersonType.Natural ? $"{partner.FirstName} {partner.LastName}".Trim() : partner.CompanyName,
                    partner.Phone ?? partner.Mobile,
                    partner.Email,
                    partner.Address,
                    partner.Notes);
                db.Suppliers.Add(sup);
            }
        }

        // Synchronize with Customer table if Customer role assigned
        if (partner.IsCustomer)
        {
            var existingCust = await db.Customers
                .SingleOrDefaultAsync(c => c.OrganizationId == request.OrganizationId && c.Code == partner.Code, ct);
            if (existingCust is null)
            {
                var cust = Customer.Create(
                    request.OrganizationId,
                    partner.Code,
                    partner.Name,
                    partner.PersonType == PersonType.Natural ? $"{partner.FirstName} {partner.LastName}".Trim() : partner.CompanyName,
                    partner.Phone ?? partner.Mobile,
                    partner.Email,
                    partner.Address,
                    partner.EconomicCode ?? partner.NationalLegalId ?? partner.NationalCode,
                    partner.Notes);
                db.Customers.Add(cust);
            }
        }

        await db.SaveInOperation(access.Actor, ct);

        var rolesDto = partner.Roles.Select(r => new BusinessPartnerRoleDto(
            r.RoleId,
            r.RoleId == 1 ? "SUPPLIER" : r.RoleId == 2 ? "CUSTOMER" : "CONTRACTOR",
            r.RoleId == 1 ? "Supplier" : r.RoleId == 2 ? "Customer" : "Contractor")).ToList();

        var ver = (byte[])db.Entry(partner).Property("RowVersion").CurrentValue!;
        return OrganizationResult<BusinessPartnerDetail>.Success(ToDetail(partner, rolesDto, ver));
    }, ct);

    public Task<OrganizationResult<BusinessPartnerDetail>> Update(
        long organizationId,
        long id,
        byte[] version,
        UpdateBusinessPartnerRequest request,
        OrganizationWriteAccess access,
        CancellationToken ct) => Write(access, async db =>
    {
        var orgCheck = await LockAndRequireActiveOrganization(db, organizationId, ct);
        if (!orgCheck.Ok) return OrganizationResult<BusinessPartnerDetail>.Failure(orgCheck.Error!);

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT [Id] FROM [organization].[BusinessPartner] WITH (UPDLOCK, HOLDLOCK) WHERE [OrganizationId] = {organizationId} AND [Id] = {id}", ct);

        var partner = await db.BusinessPartners
            .Include(b => b.Roles)
            .SingleOrDefaultAsync(b => b.OrganizationId == organizationId && b.Id == id, ct);
        if (partner is null) return OrganizationResult<BusinessPartnerDetail>.Failure(BusinessPartnerErrors.NotFound);
        if (!Matches(db, partner, version)) return OrganizationResult<BusinessPartnerDetail>.Failure(BusinessPartnerErrors.Conflict);

        var changed = partner.Update(
            request.PersonType,
            request.Name,
            request.FirstName,
            request.LastName,
            request.NationalCode,
            request.CompanyName,
            request.NationalLegalId,
            request.EconomicCode,
            request.Mobile,
            request.Phone,
            request.Email,
            request.Website,
            request.Country,
            request.Province,
            request.City,
            request.Address,
            request.PostalCode,
            request.Notes,
            request.RoleIds);

        // Sync Supplier table
        var existingSup = await db.Suppliers
            .SingleOrDefaultAsync(s => s.OrganizationId == organizationId && s.Code == partner.Code, ct);
        if (partner.IsSupplier)
        {
            if (existingSup is null)
            {
                var sup = Supplier.Create(
                    organizationId,
                    partner.Code,
                    partner.Name,
                    partner.PersonType == PersonType.Natural ? $"{partner.FirstName} {partner.LastName}".Trim() : partner.CompanyName,
                    partner.Phone ?? partner.Mobile,
                    partner.Email,
                    partner.Address,
                    partner.Notes);
                db.Suppliers.Add(sup);
            }
            else
            {
                existingSup.Update(
                    partner.Name,
                    partner.PersonType == PersonType.Natural ? $"{partner.FirstName} {partner.LastName}".Trim() : partner.CompanyName,
                    partner.Phone ?? partner.Mobile,
                    partner.Email,
                    partner.Address,
                    partner.Notes);
            }
        }

        // Sync Customer table
        var existingCust = await db.Customers
            .SingleOrDefaultAsync(c => c.OrganizationId == organizationId && c.Code == partner.Code, ct);
        if (partner.IsCustomer)
        {
            if (existingCust is null)
            {
                var cust = Customer.Create(
                    organizationId,
                    partner.Code,
                    partner.Name,
                    partner.PersonType == PersonType.Natural ? $"{partner.FirstName} {partner.LastName}".Trim() : partner.CompanyName,
                    partner.Phone ?? partner.Mobile,
                    partner.Email,
                    partner.Address,
                    partner.EconomicCode ?? partner.NationalLegalId ?? partner.NationalCode,
                    partner.Notes);
                db.Customers.Add(cust);
            }
            else
            {
                existingCust.Update(
                    partner.Name,
                    partner.PersonType == PersonType.Natural ? $"{partner.FirstName} {partner.LastName}".Trim() : partner.CompanyName,
                    partner.Phone ?? partner.Mobile,
                    partner.Email,
                    partner.Address,
                    partner.EconomicCode ?? partner.NationalLegalId ?? partner.NationalCode,
                    partner.Notes);
            }
        }

        await db.SaveInOperation(access.Actor, ct);

        var rolesDto = partner.Roles.Select(r => new BusinessPartnerRoleDto(
            r.RoleId,
            r.RoleId == 1 ? "SUPPLIER" : r.RoleId == 2 ? "CUSTOMER" : "CONTRACTOR",
            r.RoleId == 1 ? "Supplier" : r.RoleId == 2 ? "Customer" : "Contractor")).ToList();

        var ver = (byte[])db.Entry(partner).Property("RowVersion").CurrentValue!;
        return OrganizationResult<BusinessPartnerDetail>.Success(ToDetail(partner, rolesDto, ver));
    }, ct);

    public Task<OrganizationResult<BusinessPartnerSummary>> SetActive(
        long organizationId,
        long id,
        byte[] version,
        bool isActive,
        OrganizationWriteAccess access,
        CancellationToken ct) => Write(access, async db =>
    {
        var orgCheck = await LockAndRequireActiveOrganization(db, organizationId, ct);
        if (!orgCheck.Ok) return OrganizationResult<BusinessPartnerSummary>.Failure(orgCheck.Error!);

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT [Id] FROM [organization].[BusinessPartner] WITH (UPDLOCK, HOLDLOCK) WHERE [OrganizationId] = {organizationId} AND [Id] = {id}", ct);

        var partner = await db.BusinessPartners
            .Include(b => b.Roles)
            .SingleOrDefaultAsync(b => b.OrganizationId == organizationId && b.Id == id, ct);
        if (partner is null) return OrganizationResult<BusinessPartnerSummary>.Failure(BusinessPartnerErrors.NotFound);
        if (!Matches(db, partner, version)) return OrganizationResult<BusinessPartnerSummary>.Failure(BusinessPartnerErrors.Conflict);

        if (partner.SetActive(isActive))
        {
            var existingSup = await db.Suppliers
                .SingleOrDefaultAsync(s => s.OrganizationId == organizationId && s.Code == partner.Code, ct);
            existingSup?.SetActive(isActive);

            var existingCust = await db.Customers
                .SingleOrDefaultAsync(c => c.OrganizationId == organizationId && c.Code == partner.Code, ct);
            existingCust?.SetActive(isActive);

            await db.SaveInOperation(access.Actor, ct);
        }

        var roleIds = partner.Roles.Select(r => r.RoleId).ToList();
        var ver = (byte[])db.Entry(partner).Property("RowVersion").CurrentValue!;
        return OrganizationResult<BusinessPartnerSummary>.Success(ToSummary(partner, roleIds, ver));
    }, ct);

    private static BusinessPartnerSummary ToSummary(BusinessPartner b, IReadOnlyList<int> roles, byte[] rowVersion) =>
        new(
            b.Id,
            b.OrganizationId,
            b.Code,
            b.PersonType,
            b.Name,
            b.NationalCode,
            b.CompanyName,
            b.NationalLegalId,
            b.EconomicCode,
            b.Mobile,
            b.Phone,
            b.Email,
            b.City,
            b.IsActive,
            roles,
            Convert.ToBase64String(rowVersion));

    private static BusinessPartnerDetail ToDetail(BusinessPartner b, IReadOnlyList<BusinessPartnerRoleDto> roles, byte[] rowVersion) =>
        new(
            b.Id,
            b.OrganizationId,
            b.Code,
            b.PersonType,
            b.Name,
            b.FirstName,
            b.LastName,
            b.NationalCode,
            b.CompanyName,
            b.NationalLegalId,
            b.EconomicCode,
            b.Mobile,
            b.Phone,
            b.Email,
            b.Website,
            b.Country,
            b.Province,
            b.City,
            b.Address,
            b.PostalCode,
            b.Notes,
            b.IsActive,
            roles,
            Convert.ToBase64String(rowVersion));

    private static bool Matches(OrganizationDbContext db, object entity, byte[] expectedVersion)
    {
        var current = (byte[])db.Entry(entity).Property("RowVersion").CurrentValue!;
        return StructuralComparisons.StructuralEqualityComparer.Equals(current, expectedVersion);
    }

    private static async Task<(bool Ok, string? Error)> LockAndRequireActiveOrganization(
        OrganizationDbContext db,
        long organizationId,
        CancellationToken ct)
    {
        var org = await db.Organizations
            .FromSqlInterpolated($"SELECT * FROM [organization].[Organization] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {organizationId}")
            .SingleOrDefaultAsync(ct);

        if (org is null) return (false, BusinessPartnerErrors.NotFound);
        if (!org.IsActive) return (false, "BusinessPartner.ParentOrganizationInactive");
        return (true, null);
    }

    private async Task<T> Read<T>(Func<OrganizationDbContext, Task<T>> work, CancellationToken ct)
    {
        try
        {
            await using var db = await Factory.CreateDbContextAsync(ct);
            return await work(db);
        }
        catch (SqlException ex)
        {
            await StoreDiagnosticRecorder.RecordUnavailableAsync(errorRecorder, opContext, ex, nameof(SqlBusinessPartnerStore), null);
            throw;
        }
    }

    private async Task<T> Write<T>(
        OrganizationWriteAccess access,
        Func<OrganizationDbContext, Task<T>> work,
        CancellationToken ct)
    {
        await using var db = await Factory.CreateDbContextAsync(ct);
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            try
            {
                var result = await work(db);
                await tx.CommitAsync(ct);
                return result;
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync(ct);
                await StoreDiagnosticRecorder.RecordUnavailableAsync(errorRecorder, opContext, ex, nameof(SqlBusinessPartnerStore), access.Actor.ActorId);
                throw;
            }
        });
    }
}
