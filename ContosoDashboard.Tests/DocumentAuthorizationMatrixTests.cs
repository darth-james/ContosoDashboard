using Xunit;

namespace ContosoDashboard.Tests;

// =====================================================================================================
// Permissions coverage map (SC-006): 11 operations x 4 roles = 44 cells of the spec's Permissions table.
//
// Each cell is covered by one of:
//   DENIAL   - a boundary denial test (a request just outside what the cell allows is denied), or
//   N/A      - "N/A for denial" for a full-grant cell, plus the positive test that covers the grant.
//
// This base file only holds shared constants and the map. Each story adds its tests in a partial file
// (DocumentAuthorizationMatrixTests.<Part>.cs) and fills in the rows below; T126 completes the table.
// Foundation-level access predicate truths are already covered by DocumentAccessPolicyTests.
//
// Partial files and owning story:  OwnDocuments (US1), ProjectDocuments (US2), Management (US4),
//                                  Sharing (US5), TaskAttachments (US6), TeamOversight (US8).
//
// | Operation                         | Employee               | Team Lead              | Project Manager        | Administrator          |
// |-----------------------------------|------------------------|------------------------|------------------------|------------------------|
// | 1  Upload personal document       | N/A (US1: pending)     | N/A (US1: pending)     | N/A (US1: pending)     | N/A (US1: pending)     |
// | 2  Upload to a project            | DENIAL (US2: pending)  | DENIAL (US2: pending)  | DENIAL (US2: pending)  | N/A (US2: pending)     |
// | 3  Access a document              | DENIAL (US1/US2/US5:   | DENIAL (US8: pending;  | DENIAL (US2/US8:       | N/A (positive: Access- |
// |                                   |  pending; foundation:  |  foundation: see       |  pending; foundation:  |  PolicyTests.Adminis-  |
// |                                   |  see AccessPolicyTests |  AccessPolicyTests     |  see AccessPolicyTests |  trator_CanAccessAnd-  |
// |                                   |  NonMember_/Another-   |  TeamLeadInAnother-    |  ProjectManagerOf-     |  ManageEverything...)  |
// |                                   |  Employee_Cannot...)   |  Department_...)       |  AnotherProject_...)   |                        |
// | 4  Edit metadata                  | DENIAL (US4: pending)  | DENIAL (US8: pending)  | DENIAL (US4/US8:       | N/A (US4: pending)     |
// |                                   |                        |                        |  pending)              |                        |
// | 5  Replace file                   | DENIAL (US4: pending)  | DENIAL (US4: pending)  | DENIAL (US4: pending)  | DENIAL (US4: pending)  |
// | 6  Delete                         | DENIAL (US4: pending)  | DENIAL (US8: pending)  | DENIAL (US4/US8:       | N/A (US4: pending)     |
// |                                   |                        |                        |  pending)              |                        |
// | 7  Share / remove shares          | DENIAL (US5: pending)  | DENIAL (US5: pending)  | DENIAL (US5: pending)  | DENIAL (US5: pending)  |
// | 8  Attach a document to a task    | DENIAL (US6: pending)  | DENIAL (US6: pending)  | DENIAL (US6: pending)  | N/A (US6: pending)     |
// | 9  Remove a document from a task  | DENIAL (US6: pending)  | DENIAL (US6: pending)  | DENIAL (US6: pending)  | DENIAL (US6: pending)  |
// | 10 Team Documents view            | DENIAL (US8: pending)  | N/A (US8: pending)     | N/A (US8: pending)     | N/A (US8: pending)     |
// | 11 Activity records and reports   | DENIAL (US9: pending)  | DENIAL (US9: pending)  | DENIAL (US9: pending)  | N/A (US9: pending)     |
// =====================================================================================================

/// <summary>
/// Base partial class of the role x operation authorization matrix (SC-006).
/// Put <c>[Trait("Story", "...")]</c> on each test METHOD in the partial files (this class spans stories).
/// </summary>
[Collection("LocalDb")]
public partial class DocumentAuthorizationMatrixTests
{
    // Seeded users
    protected const int Admin = 1;      // Administrator, IT
    protected const int Camille = 2;    // ProjectManager, Engineering (manages Project 1)
    protected const int Floris = 3;     // TeamLead, Engineering (member of Project 1)
    protected const int NiKang = 4;     // Employee, Engineering (member of Project 1)
    protected const int Dana = 5;       // Employee, Engineering (not on any project)
    protected const int Mateo = 6;      // Employee, Marketing (member of Project 2)
    protected const int Hana = 7;       // TeamLead, Marketing
    protected const int Rafael = 8;     // ProjectManager, Marketing (manages Project 2)

    // Seeded projects
    protected const int Project1 = 1;   // ContosoDashboard Development
    protected const int Project2 = 2;   // Marketing Website Refresh

    protected readonly LocalDbFixture Fixture;
    protected readonly DocumentTestData Data;

    public DocumentAuthorizationMatrixTests(LocalDbFixture fixture)
    {
        Fixture = fixture;
        Data = new DocumentTestData(fixture);
    }
}
