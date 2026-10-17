using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using ContosoDashboard.Data;
using ContosoDashboard.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();

// Add authentication state provider for Blazor
builder.Services.AddScoped<AuthenticationStateProvider, CustomAuthenticationStateProvider>();

// Configure Database
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Configure Mock Authentication (Cookie-based for training purposes)
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.LogoutPath = "/logout";
        options.AccessDeniedPath = "/login";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });

// Add authorization
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Employee", policy => policy.RequireRole("Employee", "TeamLead", "ProjectManager", "Administrator"));
    options.AddPolicy("TeamLead", policy => policy.RequireRole("TeamLead", "ProjectManager", "Administrator"));
    options.AddPolicy("ProjectManager", policy => policy.RequireRole("ProjectManager", "Administrator"));
    options.AddPolicy("Administrator", policy => policy.RequireRole("Administrator"));
});

// Register application services
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<ITaskService, TaskService>();
builder.Services.AddScoped<IProjectService, ProjectService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();

// Document infrastructure (Principle II): business code depends only on the interfaces.
builder.Services.Configure<DocumentStorageOptions>(builder.Configuration.GetSection("DocumentStorage"));
builder.Services.AddScoped<IFileStorageService, LocalFileStorageService>();
builder.Services.AddSingleton<IMalwareScanner, SimulatedEicarMalwareScanner>();

// Add HttpContextAccessor for accessing user claims
builder.Services.AddHttpContextAccessor();

var app = builder.Build();

// Initialize database
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<ApplicationDbContext>();
        context.Database.EnsureCreated(); // For development - use migrations in production

        // Development-only schema guard (research R-03). EnsureCreated() does nothing when the database
        // already exists, so a database created before the Documents feature has none of the new tables.
        var schemaLogger = services.GetRequiredService<ILogger<Program>>();
        var documentsSchemaPresent = true;
        try
        {
            // Touch each new table and the new Notifications.DocumentId column.
            _ = context.Documents.Any();
            _ = context.DocumentTags.Any();
            _ = context.DocumentShares.Any();
            _ = context.TaskDocuments.Any();
            _ = context.DocumentActivities.Any();
            _ = context.Notifications.Select(n => n.DocumentId).Any();
        }
        catch (SqlException)
        {
            documentsSchemaPresent = false;
        }

        if (!documentsSchemaPresent)
        {
            if (app.Environment.IsDevelopment())
            {
                schemaLogger.LogWarning("Database schema predates the Documents feature; recreating training database");
                context.Database.EnsureDeleted();
                context.Database.EnsureCreated();

                // No Documents row survives the recreate, so uploaded files would be orphaned: clear them too.
                try
                {
                    var contentRoot = Path.GetFullPath(app.Environment.ContentRootPath)
                        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    var configuredRoot = builder.Configuration["DocumentStorage:RootPath"];
                    if (string.IsNullOrWhiteSpace(configuredRoot))
                    {
                        configuredRoot = "AppData/uploads";
                    }

                    var uploadsPath = Path.GetFullPath(Path.Combine(contentRoot, configuredRoot))
                        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    var webRoot = Path.Combine(contentRoot, "wwwroot");

                    var insideContentRoot = uploadsPath.StartsWith(contentRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
                    var underWebRoot = uploadsPath.Equals(webRoot, StringComparison.OrdinalIgnoreCase)
                        || uploadsPath.StartsWith(webRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

                    if (Directory.Exists(uploadsPath) && insideContentRoot && !underWebRoot)
                    {
                        Directory.Delete(uploadsPath, recursive: true);
                        schemaLogger.LogWarning("Cleared uploaded files in {Path} because the training database was recreated", uploadsPath);
                    }
                }
                catch (Exception clearEx)
                {
                    schemaLogger.LogWarning(clearEx, "Could not clear the uploads folder after recreating the training database");
                }
            }
            else
            {
                schemaLogger.LogError(
                    "Database schema predates the Documents feature. Nothing was deleted. " +
                    "Follow the database reset instructions in the README (stop and delete the LocalDB instance or drop the database, " +
                    "and delete the files under AppData/uploads), then restart the application.");
            }
        }
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred creating the database.");
    }
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}
else
{
    // Use HSTS even in development for training purposes
    app.UseHsts();
}

// Add security headers
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["X-XSS-Protection"] = "1; mode=block";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    
    // Content Security Policy for Blazor Server
    context.Response.Headers["Content-Security-Policy"] = 
        "default-src 'self'; " +
        "script-src 'self' 'unsafe-inline' 'unsafe-eval' https://cdn.jsdelivr.net; " +
        "style-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net; " +
        "font-src 'self' https://cdn.jsdelivr.net; " +
        "img-src 'self' data: https:; " +
        "connect-src 'self' wss: ws:;";
    
    await next();
});

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

// Enable authentication and authorization
app.UseAuthentication();
app.UseAuthorization();

// Razor Pages endpoints (login, logout, and the document download/preview endpoints). Harmless alongside the
// fallback below; it makes the routing of parameterized pages such as /documents/{id:int}/download explicit (R-06).
app.MapRazorPages();
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();
