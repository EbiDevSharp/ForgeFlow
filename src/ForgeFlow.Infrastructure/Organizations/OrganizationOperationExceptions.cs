using ForgeFlow.Application.Organizations;
using Microsoft.EntityFrameworkCore;

namespace ForgeFlow.Infrastructure.Organizations;

internal sealed class OrganizationAuditWriteException(DbUpdateException inner) : DbUpdateException(OrganizationAccessErrors.AuditUnavailable, inner.InnerException ?? inner);
internal sealed class OrganizationCommitOutcomeUnknownException(Exception inner) : Exception(OrganizationAccessErrors.OutcomeUnknown, inner);
