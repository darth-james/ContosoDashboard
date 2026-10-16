<!--
Sync Impact Report
==================
Version change: (unversioned template) → 1.0.0
Bump rationale: Initial ratification. Every template placeholder replaced with
project-specific governance, so this is the first MAJOR baseline.

Modified principles (template placeholder → new title):
- [PRINCIPLE_1_NAME] → I. Offline-First, Local-Only Operation (NON-NEGOTIABLE)
- [PRINCIPLE_2_NAME] → II. Infrastructure Abstraction for Cloud Migration
- [PRINCIPLE_3_NAME] → III. Security by Default (Defense in Depth)
- [PRINCIPLE_4_NAME] → IV. Layered Architecture & Pattern Consistency
- [PRINCIPLE_5_NAME] → V. Training-Grade Simplicity & Transparency

Added sections:
- Technology & Platform Constraints (was [SECTION_2_NAME])
- Development Workflow & Quality Gates (was [SECTION_3_NAME])
- Governance (filled from [GOVERNANCE_RULES])

Removed sections: None

Dependent artifacts: Not modified by this command. The plan, spec, and tasks
templates and the command files read this constitution at runtime (for example,
the "Constitution Check" gate in plan-template.md).

Follow-up TODOs: None. RATIFICATION_DATE is set to the date of first adoption
(2026-10-16) because no earlier ratified constitution exists.
-->
# ContosoDashboard Constitution

## Core Principles

### I. Offline-First, Local-Only Operation (NON-NEGOTIABLE)

ContosoDashboard MUST run fully offline on a single developer workstation with no
cloud subscription, internet connection, or external service.

- Persistence MUST use SQL Server LocalDB through Entity Framework Core.
- File-based features MUST use the local filesystem. Uploaded or user-generated files
  MUST be stored outside `wwwroot` (for example, `AppData/uploads`).
- Authentication MUST use the existing cookie-based mock authentication. Features
  MUST NOT require a real identity provider.
- Features MUST NOT add runtime dependencies on cloud SDKs (for example, Azure Storage
  or Azure Identity clients), hosted APIs, or network-only resources. Static UI assets
  already loaded from the CDN allowed by the Content Security Policy are tolerated, but
  core functionality MUST NOT depend on them.
- `dotnet run` from the `ContosoDashboard` directory MUST create, seed, and start the
  application without manual infrastructure setup.

**Rationale**: This is a training codebase. Learners must be able to complete every
exercise without Azure accounts, costs, or connectivity.

### II. Infrastructure Abstraction for Cloud Migration

Every infrastructure dependency MUST sit behind an interface and be registered through
ASP.NET Core dependency injection in `Program.cs`.

- Storage, file, messaging, and similar concerns MUST expose an interface (for example,
  `IFileStorageService` with `UploadAsync`, `DeleteAsync`, `DownloadAsync`, and
  `GetUrlAsync`) with a local implementation (for example, `LocalFileStorageService`).
- Business logic, Razor components, and pages MUST depend only on the interface, never
  on a concrete infrastructure type.
- Persisted references to infrastructure resources (such as file paths) MUST be
  relative and portable, so a future cloud implementation (for example, Azure Blob
  Storage) can be swapped in without schema, UI, or business-logic changes.
- Moving to the cloud MUST require only configuration and implementation swaps.

**Rationale**: Learners practice production-grade separation of concerns while the
training build stays offline.

### III. Security by Default (Defense in Depth)

Every feature MUST enforce authorization at several independent layers.

- Every non-public Razor page or component MUST carry `[Authorize]` (or a named role
  policy: `Employee`, `TeamLead`, `ProjectManager`, or `Administrator`).
- Service methods that return or change user-scoped data MUST accept the requesting
  user's ID and verify ownership, membership, or role before data access. This
  prevents insecure direct object references (IDOR). UI-only checks are insufficient.
- Users MUST see only the data they are authorized to access, including in lists,
  search results, dashboards, and notifications.
- File handling MUST validate extensions against an allow-list, enforce size limits,
  use GUID-based server-side file names, and MUST NOT build paths from user-supplied
  file names. Protected files MUST be served through an authorization-checked endpoint.
- Existing security headers (CSP, X-Frame-Options, X-Content-Type-Options,
  Referrer-Policy) and HTTPS/HSTS configuration MUST NOT be weakened without a
  documented justification.
- Login MUST issue all claims that features rely on (NameIdentifier, Name, Email,
  Role, Department).

**Rationale**: The application teaches secure-by-design patterns. A feature that
bypasses service-level authorization teaches the wrong lesson, even with mock
authentication.

### IV. Layered Architecture & Pattern Consistency

New work MUST follow the established structure and conventions. Major rewrites are
out of scope.

- Code MUST be placed in the existing layers: `Models/` (entities), `Data/`
  (`ApplicationDbContext`, relationships, indexes, seed data), `Services/` (interface
  plus implementation, which holds business rules and authorization), and
  `Pages/` and `Shared/` (Blazor Server and Razor Pages UI).
- Pages MUST call services. They MUST NOT query `ApplicationDbContext` directly.
- I/O-bound operations MUST be `async` end to end. EF Core queries MUST eager-load
  related data (`.Include()`/`.ThenInclude()`) to avoid N+1 queries. Frequently
  filtered columns MUST be indexed.
- New entity primary keys MUST be `int`, consistent with existing `User` and `Project`
  keys. Category-like values that stakeholders specify as text MUST be stored as
  text. Column lengths MUST accommodate real-world values (for example, 255
  characters for MIME types).
- Operations that write both files and database rows MUST order the steps so failures
  cannot leave orphaned or duplicate records: generate a unique path, write the file,
  then save the metadata.
- Blazor file uploads MUST copy `IBrowserFile` content into a `MemoryStream`
  immediately, capture metadata first, and clear the file reference afterward.

**Rationale**: Consistent structure keeps the codebase teachable and makes each
feature reviewable against a known pattern.

### V. Training-Grade Simplicity & Transparency

Solutions MUST be the simplest design that meets the specification and these
principles.

- Apply YAGNI. Features listed as out of scope in stakeholder documents (for example,
  version history, soft delete, external integrations, or storage quotas) MUST NOT be
  implemented unless a specification explicitly adds them.
- Any new abstraction, project, package, or pattern beyond Principles II and IV MUST
  be justified in the plan's Complexity Tracking table.
- Intentional training-only shortcuts (for example, mock authentication or the absence
  of rate limiting) MUST be documented as known limitations in `README.md` or the
  feature documentation. They MUST NOT be presented as production-ready.
- The "TRAINING PURPOSES ONLY" disclaimer MUST remain in `README.md`.

**Rationale**: Learners need to understand the code fully. Hidden complexity or
undocumented shortcuts weaken the training.

## Technology & Platform Constraints

- **Runtime**: .NET 8 (`net8.0`) with nullable reference types and implicit usings
  enabled.
- **Web framework**: ASP.NET Core 8 with Blazor Server for interactive pages and
  Razor Pages for HTTP-bound flows such as login and logout.
- **Data**: Entity Framework Core 8 (SQL Server provider) against SQL Server LocalDB.
  The database is created with `EnsureCreated()` and seeded in
  `ApplicationDbContext`. Schema changes MUST keep seeding working on a freshly
  created database.
- **UI**: Bootstrap 5.3 and Bootstrap Icons. New UI MUST reuse existing layout
  components (`MainLayout`, `NavMenu`) and style conventions.
- **Authentication and authorization**: Cookie authentication with an 8-hour sliding
  expiration, claims-based identity, and the four hierarchical roles (Employee <
  TeamLead < ProjectManager < Administrator).
- **Performance targets**: Stakeholder-specified targets in feature specifications
  (for example, list pages and search returning within 2 seconds) are binding
  acceptance criteria and MUST be measurable.
- **Dependencies**: New NuGet packages MUST work offline at runtime and MUST be
  justified in the implementation plan.

## Development Workflow & Quality Gates

- **Spec-Driven Development**: Feature work MUST follow the GitHub Spec Kit flow:
  `/speckit.specify` → (`/speckit.clarify`) → `/speckit.plan` → `/speckit.tasks` →
  (`/speckit.analyze`) → `/speckit.implement`. Stakeholder documents in
  `StakeholderDocs/` are the source requirements for specifications.
- **Constitution Check**: Every `plan.md` MUST include a Constitution Check that
  evaluates Principles I–V before Phase 0 research and again after Phase 1 design.
  Violations MUST be resolved or justified in Complexity Tracking.
- **Specifications**: Specifications MUST state user stories with independently
  testable acceptance scenarios, role-based permissions for each operation, and
  measurable success criteria. Unresolved ambiguity MUST be marked
  `[NEEDS CLARIFICATION]` and resolved before planning.
- **Build gate**: `dotnet build` MUST succeed with no new compiler errors or new
  nullable warnings before a task is marked complete.
- **Verification gate**: Each user story MUST be verifiable through documented
  quickstart steps that use the seeded mock users. Verification MUST include at least
  one negative authorization case (an unauthorized role or a non-member user is
  denied). When automated tests are added, they MUST cover service-layer
  authorization and the data-integrity rules in Principle IV.
- **Review**: Changes MUST be reviewed against this constitution before merge, with
  particular attention to service-level authorization, offline compliance, and the
  file and database write order.

## Governance

- This constitution supersedes all other development practices for ContosoDashboard.
  If a specification, plan, or task list conflicts with it, the constitution wins
  unless it is formally amended.
- **Amendments**: Changes MUST be made through `/speckit.constitution`, MUST include an
  updated Sync Impact Report, and MUST be committed separately with a message that
  describes the version change. Dependent templates MUST be reviewed for consistency
  when principles change.
- **Versioning**: This document follows semantic versioning. MAJOR means principles
  or governance are removed or redefined in a backward-incompatible way. MINOR means a
  principle or section is added or guidance is materially expanded. PATCH means
  clarifications, wording changes, or typo fixes.
- **Compliance review**: `/speckit.plan` Constitution Check gates, `/speckit.analyze`
  consistency reports, and code reviews MUST verify compliance. Unjustified violations
  block merge. Approved exceptions MUST be recorded in the relevant plan's Complexity
  Tracking table.
- **Runtime guidance**: `README.md` and the agent context files generated by
  `update-agent-context.ps1` provide day-to-day guidance and MUST NOT contradict this
  constitution.

**Version**: 1.0.0 | **Ratified**: 2026-10-16 | **Last Amended**: 2026-10-16
