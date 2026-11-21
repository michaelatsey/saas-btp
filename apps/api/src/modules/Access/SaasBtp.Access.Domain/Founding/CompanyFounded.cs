using MicroKit.Domain.Events;
using SaasBtp.Access.Domain.Organizations;
using SaasBtp.Access.Domain.Profiles;
using SaasBtp.Access.Domain.Workspaces;

namespace SaasBtp.Access.Domain.Founding;

/// <summary>
/// The domain event "Company founded" (BP-001, TSK-2), raised by the founded organization. A
/// domain event of the Access model, not an integration event: no other module consumes it
/// (contract.md §1, ADR-ARCH-034 §4).
/// </summary>
/// <param name="OrganizationId">The organization founded.</param>
/// <param name="WorkspaceId">Its first workspace.</param>
/// <param name="FounderProfileId">The founder, through their profile.</param>
/// <param name="OccurredAt">The instant of the founding.</param>
public sealed record CompanyFounded(
    OrganizationId OrganizationId, WorkspaceId WorkspaceId, ProfileId FounderProfileId, DateTimeOffset OccurredAt)
    : DomainEvent(OccurredAt);
