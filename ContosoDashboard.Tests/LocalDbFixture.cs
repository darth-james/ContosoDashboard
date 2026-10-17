using ContosoDashboard.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace ContosoDashboard.Tests;

/// <summary>
/// Creates a uniquely named throwaway LocalDB database (ContosoDashboard_Tests_{guid}) with EnsureCreated(),
/// so the seed users 1-8, projects 1-2 and tasks 1-5 exist. The database and the temp storage root are removed on dispose.
/// Tests MUST NOT assume an empty Documents table: assert on IDs or unique titles they created themselves.
/// </summary>
public sealed class LocalDbFixture : IDisposable
{
    public LocalDbFixture()
    {
        DatabaseName = $"ContosoDashboard_Tests_{Guid.NewGuid():N}";
        ConnectionString =
            $"Server=(localdb)\\mssqllocaldb;Database={DatabaseName};Trusted_Connection=True;MultipleActiveResultSets=true";
        StorageRoot = Path.Combine(Path.GetTempPath(), "ContosoDashboardTests", Guid.NewGuid().ToString("N"));

        using var context = CreateContext();
        context.Database.EnsureCreated();
    }

    public string DatabaseName { get; }

    public string ConnectionString { get; }

    /// <summary>Per-fixture temp storage root used by LocalFileStorageService in tests.</summary>
    public string StorageRoot { get; }

    /// <summary>Creates a new context on the test database, optionally with EF interceptors attached.</summary>
    public ApplicationDbContext CreateContext(params IInterceptor[] interceptors)
    {
        var builder = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(ConnectionString);
        if (interceptors.Length > 0)
        {
            builder.AddInterceptors(interceptors);
        }

        return new ApplicationDbContext(builder.Options);
    }

    public void Dispose()
    {
        try
        {
            using var context = CreateContext();
            context.Database.EnsureDeleted();
        }
        catch (Exception)
        {
            // Best-effort cleanup of the throwaway database.
        }

        try
        {
            if (Directory.Exists(StorageRoot))
            {
                Directory.Delete(StorageRoot, recursive: true);
            }
        }
        catch (Exception)
        {
            // Best-effort cleanup of the temp storage root.
        }
    }
}

/// <summary>Tests that need the LocalDB fixture join this collection. (parallelizeTestCollections is off.)</summary>
[CollectionDefinition("LocalDb")]
public class LocalDbCollection : ICollectionFixture<LocalDbFixture>
{
}
