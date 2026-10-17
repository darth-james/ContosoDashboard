# Contract: Infrastructure Interfaces

**Feature**: `001-document-upload-management` | **Layer**: `ContosoDashboard/Services/` | **Registered in**: `Program.cs`

These are the swap points that Constitution Principle II requires. Business services, Razor components, and pages depend **only** on these interfaces.

---

## IFileStorageService

```csharp
namespace ContosoDashboard.Services;

/// Stores and retrieves file content by a relative, portable storage key.
/// Keys use forward slashes and are valid both as local relative paths and as Azure blob names.
public interface IFileStorageService
{
    /// Writes content to the caller-generated storage key and returns that key.
    /// The key is created by DocumentService as "{userId}/{projectId|personal}/{guid:N}{ext}".
    /// It is NEVER derived from a user-supplied file name.
    Task<string> UploadAsync(Stream fileStream, string storageKey, string contentType);

    /// Deletes the content at the key. Idempotent: a missing key is not an error.
    Task DeleteAsync(string storageKey);

    /// Opens a read-only stream for the key. Throws FileNotFoundException if the key is missing.
    /// The caller owns and disposes the stream.
    Task<Stream> DownloadAsync(string storageKey);

    /// Returns a time-limited direct URL (future cloud implementation, for example a blob SAS).
    /// Local implementation: throws NotSupportedException. Local files are served only through
    /// the authorization-checked endpoints in http-endpoints.md. No feature code calls this method.
    Task<string> GetUrlAsync(string storageKey, TimeSpan expiration);
}
```

### Behavioral contract

| Aspect | Requirement |
|--------|-------------|
| Key validation | Reject (`ArgumentException`) keys that are null or empty, rooted, contain `..`, contain `\`, or contain invalid path characters. |
| Containment (local) | `Path.GetFullPath(Path.Combine(root, key))` MUST start with the full root path, or the call throws. |
| Overwrite | `UploadAsync` MUST NOT overwrite an existing key (local: `FileMode.CreateNew`). Replace always uses a new key. |
| Partial writes | If writing fails, the implementation deletes any partial file before rethrowing. |
| Location (local) | Root = `DocumentStorage:RootPath` (default `AppData/uploads`), resolved against `ContentRootPath`. It's outside `wwwroot` and created on demand. |
| Offline | No network I/O. No cloud SDK references in the training build. |

### Implementations

| Class | Build | Notes |
|-------|-------|-------|
| `LocalFileStorageService` | Training (registered) | `System.IO`; `AddScoped<IFileStorageService, LocalFileStorageService>()` |
| `AzureBlobStorageService` | Future (not built) | Same keys as blob names. Swap by DI registration and configuration only. |

---

## IMalwareScanner

```csharp
namespace ContosoDashboard.Services;

public enum MalwareScanStatus { Clean, Infected, Error }

public sealed record MalwareScanResult(MalwareScanStatus Status, string? Detail = null);

/// Scans file content before it is stored. Implementations MUST work offline.
public interface IMalwareScanner
{
    /// Reads the stream from its current position. MUST restore the stream's Position before returning.
    /// MUST NOT throw for infected content: return Infected. MAY throw or return Error on scanner failure.
    Task<MalwareScanResult> ScanAsync(Stream content, string fileName, CancellationToken cancellationToken = default);
}
```

### Behavioral contract

| Aspect | Requirement |
|--------|-------------|
| Fail closed | Callers MUST treat `Infected`, `Error`, any exception, and cancellation or timeout (about 10 s) as a rejection. Nothing is written to storage. |
| Ordering | The scan happens on the in-memory stream **before** `IFileStorageService.UploadAsync`. |
| Training implementation | `SimulatedEicarMalwareScanner` returns `Infected` if the content contains the standard 68-byte EICAR test string anywhere. Otherwise it returns `Clean`. |
| Signature handling | The signature MUST NOT appear as a single contiguous literal in source, binaries, or docs. Build it at runtime from parts or decode it from Base64 (research R-08). |
| Lifetime | Stateless: `AddSingleton<IMalwareScanner, SimulatedEicarMalwareScanner>()`. |
| Documentation | README "Known Limitations": the simulated scanner detects only EICAR and is not a production control (FR-038). |

---

## Configuration contract (`appsettings.json`)

```json
"DocumentStorage": {
  "RootPath": "AppData/uploads"
}
```

The maximum file size (26,214,400 bytes) and the extension allow-list are business rules from the spec, so they're constants in `DocumentFileRules`, not configuration.
