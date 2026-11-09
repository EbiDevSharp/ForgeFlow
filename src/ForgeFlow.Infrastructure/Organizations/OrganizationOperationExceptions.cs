using Microsoft.EntityFrameworkCore;

namespace ForgeFlow.Infrastructure.Organizations;

internal sealed class OrganizationAuditWriteException(DbUpdateException inner) : DbUpdateException("OrganizationSite.AuditUnavailable", inner.InnerException ?? inner);
internal sealed class OrganizationCommitOutcomeUnknownException(Exception inner) : Exception("OrganizationSite.OutcomeUnknown", inner);
