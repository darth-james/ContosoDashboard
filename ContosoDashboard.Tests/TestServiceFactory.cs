using ContosoDashboard.Data;
using ContosoDashboard.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ContosoDashboard.Tests;

/// <summary>
/// Builds real infrastructure and services against a <see cref="LocalDbFixture"/>.
/// Later stories add document-service factory methods to this file.
/// </summary>
public static class TestServiceFactory
{
    /// <summary>LocalFileStorageService rooted at the fixture's temp storage root.</summary>
    public static LocalFileStorageService CreateFileStorage(LocalDbFixture fixture)
    {
        return CreateFileStorage(fixture.StorageRoot);
    }

    /// <summary>LocalFileStorageService rooted at an explicit absolute folder.</summary>
    public static LocalFileStorageService CreateFileStorage(string absoluteRootPath)
    {
        var options = Options.Create(new DocumentStorageOptions { RootPath = absoluteRootPath });
        return new LocalFileStorageService(options, absoluteRootPath);
    }

    public static SimulatedEicarMalwareScanner CreateScanner()
    {
        return new SimulatedEicarMalwareScanner();
    }

    public static NotificationService CreateNotificationService(ApplicationDbContext context)
    {
        return new NotificationService(context);
    }

    public static TaskService CreateTaskService(ApplicationDbContext context)
    {
        return new TaskService(context, CreateNotificationService(context));
    }

    /// <summary>
    /// DocumentService over <paramref name="ctx"/> with the real notification and task services. Storage and scanner
    /// default to a real <see cref="LocalFileStorageService"/> (under a unique temp root; pass the fixture's storage to
    /// inspect files) and <see cref="SimulatedEicarMalwareScanner"/>.
    /// </summary>
    public static DocumentService CreateDocumentService(
        ApplicationDbContext ctx, IFileStorageService? storage = null, IMalwareScanner? scanner = null)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        storage ??= CreateFileStorage(
            Path.Combine(Path.GetTempPath(), "ContosoDashboardTests", Guid.NewGuid().ToString("N")));

        return new DocumentService(
            ctx,
            storage,
            scanner ?? CreateScanner(),
            CreateNotificationService(ctx),
            CreateTaskService(ctx),
            NullLogger<DocumentService>.Instance);
    }
}
