using ApplicationServices;
using AppScaffolding;
using AudibleUtilities;
using LibationFileManager;
using Serilog;

namespace Decanterr.Api.Services;

/// <summary>
/// Initializes the Libation backend for server operation (no UI).
/// Uses LibationScaffolding for proper bootstrap, then applies server-specific config.
/// </summary>
public static class ServerBootstrapper
{
    public static void Initialize(IConfiguration configuration)
    {
        // 1. Determine LibationFiles directory from ASP.NET config or env var
        var libationFilesDir = configuration["LibationFiles"];

        if (string.IsNullOrWhiteSpace(libationFilesDir))
            libationFilesDir = Environment.GetEnvironmentVariable("LIBATION_CONFIG_DIR");

        if (string.IsNullOrWhiteSpace(libationFilesDir))
            libationFilesDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DecanterrApi");

        Directory.CreateDirectory(libationFilesDir);

        // Set the env var so LibationFiles picks it up
        Environment.SetEnvironmentVariable("LIBATION_FILES_DIR", libationFilesDir);

        // 2. Override PostgreSQL connection string from ASP.NET config if provided
        var pgConnStr = configuration.GetConnectionString("Postgres");
        if (!string.IsNullOrWhiteSpace(pgConnStr))
            Environment.SetEnvironmentVariable("LIBATION_CONNECTION_STRING", pgConnStr);

        // 3. Run Libation's standard bootstrap sequence
        var config = LibationScaffolding.RunPreConfigMigrations();

        // Ensure settings file exists for first run
        if (!File.Exists(config.LibationFiles.SettingsFilePath))
        {
            File.WriteAllText(config.LibationFiles.SettingsFilePath, "{}");
        }

        LibationScaffolding.RunPostConfigMigrations(config);
        LibationScaffolding.RunPostMigrationScaffolding(Variety.None, config);

        // 4. Configure Serilog for server use (console + file)
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console()
            .WriteTo.File(
                Path.Combine(libationFilesDir, "Logs", "decanterr-api-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14)
            .CreateLogger();

        // 5. Server auth — LoginChoiceFactory is set per-request by LoginController
        //    when interactive login is needed. Default to null (token-only auth).
        ApiExtended.LoginChoiceFactory = null;

        // 6. Override books directory from config if set
        var booksDir = configuration["BooksDirectory"];
        if (!string.IsNullOrWhiteSpace(booksDir))
        {
            Directory.CreateDirectory(booksDir);
            Configuration.Instance.Books = booksDir;
        }

        Log.Logger.Information("Decanterr API server initialized. LibationFiles: {LibationFiles}", libationFilesDir);
    }
}

