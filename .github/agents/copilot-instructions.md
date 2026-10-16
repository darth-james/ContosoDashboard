# ContosoDashboard Development Guidelines

Auto-generated from all feature plans. Last updated: 2026-10-16

## Active Technologies

- C# 12 on .NET 8 (`net8.0`, nullable and implicit usings enabled; SDK 8.0.425 installed) + ASP.NET Core 8 Blazor Server + Razor Pages, Entity Framework Core 8.0.0 (SqlServer provider), Bootstrap 5.3 + Bootstrap Icons (CDN, already allowed by CSP); no new runtime NuGet packages (001-document-upload-management)

## Project Structure

```text
ContosoDashboard/            # single ASP.NET Core 8 Blazor Server + Razor Pages project
├── Models/                  # EF Core entities (int keys)
├── Data/                    # ApplicationDbContext: relationships, indexes, seed data (EnsureCreated)
├── Services/                # interface + implementation; business rules and authorization (requestingUserId)
├── Pages/                   # Blazor pages (.razor) and Razor Pages (.cshtml) for HTTP-bound flows
├── Shared/                  # layout and reusable components
└── AppData/uploads/         # runtime document storage (git-ignored, outside wwwroot)
ContosoDashboard.Tests/      # optional xUnit project (LocalDB) for service-level authorization tests
specs/                       # Spec Kit feature artifacts
```

## Commands

```powershell
cd ContosoDashboard; dotnet build                    # build gate: no new errors or nullable warnings
cd ContosoDashboard; dotnet run --launch-profile https  # creates and seeds LocalDB on first run
dotnet test .\ContosoDashboard.Tests                 # if the optional test project exists
sqllocaldb stop mssqllocaldb; sqllocaldb delete mssqllocaldb   # reset the training database
```

## Code Style

C# 12 / .NET 8: Follow the existing conventions and `.specify/memory/constitution.md`:

- Pages call services, never `ApplicationDbContext`.
- Async I/O end to end.
- Every service method that touches user data takes `requestingUserId` and authorizes from live DB data.
- File writes follow the order: GUID key → write file → save metadata.

## Recent Changes

- 001-document-upload-management: Added C# 12 on .NET 8 (`net8.0`, nullable and implicit usings enabled; SDK 8.0.425 installed) + ASP.NET Core 8 Blazor Server + Razor Pages, Entity Framework Core 8.0.0 (SqlServer provider), Bootstrap 5.3 + Bootstrap Icons (CDN, already allowed by CSP); no new runtime NuGet packages

<!-- MANUAL ADDITIONS START -->
<!-- MANUAL ADDITIONS END -->
