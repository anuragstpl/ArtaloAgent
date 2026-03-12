using System.IO;
using System.Net.Http;
using System.Windows;
using ArtaloBot.App.Services;
using ArtaloBot.App.ViewModels;
using ArtaloBot.Core.Interfaces;
using ArtaloBot.Data;
using ArtaloBot.Data.Repositories;
using ArtaloBot.Services.Channels;
using ArtaloBot.Services.LLM;
using ArtaloBot.Services.Debug;
using ArtaloBot.Services.MCP;
using ArtaloBot.Services.Memory;
using ArtaloBot.Services.Skills;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ArtaloBot.App;

public partial class App : Application
{
    private readonly IHost _host;

    public App()
    {
        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                ConfigureServices(services);
            })
            .Build();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Database
        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ArtaloBot",
            "artalobot.db");

        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        // Register both DbContext and DbContextFactory (needed by VectorMemoryService)
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite($"Data Source={dbPath}"));

        services.AddDbContextFactory<AppDbContext>(options =>
            options.UseSqlite($"Data Source={dbPath}"), ServiceLifetime.Singleton);

        // HTTP Client
        services.AddHttpClient();

        // Debug Service (must be registered early)
        services.AddSingleton<IDebugService, DebugService>();

        // Repositories
        services.AddScoped<IChatRepository, ChatRepository>();
        services.AddScoped<ISettingsService, SettingsRepository>();

        // LLM Services
        services.AddSingleton<ILLMServiceFactory>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var debugService = sp.GetRequiredService<IDebugService>();
            return new LLMServiceFactory(httpClientFactory, debugService);
        });

        // Memory Service
        services.AddSingleton<IMemoryService>(sp =>
        {
            var dbFactory = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
            var settingsService = sp.CreateScope().ServiceProvider.GetRequiredService<ISettingsService>();
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient("memory");
            var debugService = sp.GetRequiredService<IDebugService>();
            return new VectorMemoryService(dbFactory, settingsService, httpClient, debugService);
        });

        // MCP Service
        services.AddSingleton<IMCPService>(sp =>
        {
            var dbFactory = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
            var debugService = sp.GetRequiredService<IDebugService>();
            return new MCPClientService(dbFactory, debugService);
        });

        // Skills
        services.AddSingleton<ISkillRegistry, SkillRegistry>();
        services.AddSingleton<CalculatorSkill>();
        services.AddSingleton<DateTimeSkill>();
        services.AddSingleton<WebSearchSkill>();
        services.AddSingleton<WeatherSkill>();

        // Channels
        services.AddSingleton<IChannelManager, ChannelManager>();
        services.AddSingleton<WhatsAppChannel>();

        // ViewModels
        services.AddTransient<MainViewModel>();
        services.AddTransient<ChatViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<SessionsViewModel>();
        services.AddTransient<ChannelsViewModel>();
        services.AddTransient<MCPViewModel>();

        // Services
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IThemeService, ThemeService>();

        // Debug ViewModel
        services.AddTransient<DebugViewModel>();

        // Main Window
        services.AddSingleton<MainWindow>();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        await _host.StartAsync();

        // Ensure database is created and all tables exist
        using var scope = _host.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await dbContext.Database.EnsureCreatedAsync();

        // Ensure new tables exist (in case db was created before these tables were added)
        await EnsureMemoryEntriesTableExistsAsync(dbContext);
        await EnsureMCPServersTableExistsAsync(dbContext);

        // Register skills
        var skillRegistry = _host.Services.GetRequiredService<ISkillRegistry>();
        skillRegistry.Register(_host.Services.GetRequiredService<CalculatorSkill>());
        skillRegistry.Register(_host.Services.GetRequiredService<DateTimeSkill>());
        skillRegistry.Register(_host.Services.GetRequiredService<WebSearchSkill>());
        skillRegistry.Register(_host.Services.GetRequiredService<WeatherSkill>());

        // Register channel providers
        var channelManager = _host.Services.GetRequiredService<IChannelManager>() as ChannelManager;
        channelManager?.RegisterProvider(_host.Services.GetRequiredService<WhatsAppChannel>());

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();

        base.OnStartup(e);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        using (_host)
        {
            await _host.StopAsync();
        }

        base.OnExit(e);
    }

    public static T GetService<T>() where T : class
    {
        var app = (App)Current;
        return app._host.Services.GetRequiredService<T>();
    }

    private static async Task EnsureMemoryEntriesTableExistsAsync(AppDbContext dbContext)
    {
        try
        {
            // Check if the table exists
            var connection = dbContext.Database.GetDbConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='MemoryEntries'";
            var result = await command.ExecuteScalarAsync();

            if (result == null)
            {
                // Table doesn't exist, create it
                using var createCommand = connection.CreateCommand();
                createCommand.CommandText = """
                    CREATE TABLE IF NOT EXISTS MemoryEntries (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        SessionId INTEGER NULL,
                        Role INTEGER NOT NULL,
                        Content TEXT NOT NULL,
                        EmbeddingJson TEXT NOT NULL,
                        EmbeddingModel TEXT NOT NULL,
                        CreatedAt TEXT NOT NULL,
                        Tag TEXT NOT NULL
                    );
                    CREATE INDEX IF NOT EXISTS IX_MemoryEntries_SessionId ON MemoryEntries (SessionId);
                    CREATE INDEX IF NOT EXISTS IX_MemoryEntries_CreatedAt ON MemoryEntries (CreatedAt);
                    """;
                await createCommand.ExecuteNonQueryAsync();
            }

            await connection.CloseAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error ensuring MemoryEntries table exists: {ex.Message}");
        }
    }

    private static async Task EnsureMCPServersTableExistsAsync(AppDbContext dbContext)
    {
        try
        {
            var connection = dbContext.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
                await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='MCPServers'";
            var result = await command.ExecuteScalarAsync();

            if (result == null)
            {
                using var createCommand = connection.CreateCommand();
                createCommand.CommandText = """
                    CREATE TABLE IF NOT EXISTS MCPServers (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Name TEXT NOT NULL,
                        Description TEXT,
                        ServerType TEXT NOT NULL DEFAULT 'stdio',
                        Command TEXT,
                        Arguments TEXT DEFAULT '[]',
                        WorkingDirectory TEXT,
                        EnvironmentVariables TEXT DEFAULT '{}',
                        Url TEXT,
                        IsEnabled INTEGER NOT NULL DEFAULT 1,
                        AutoStart INTEGER NOT NULL DEFAULT 0,
                        CachedTools TEXT DEFAULT '[]',
                        CreatedAt TEXT NOT NULL,
                        UpdatedAt TEXT NOT NULL
                    );
                    CREATE INDEX IF NOT EXISTS IX_MCPServers_Name ON MCPServers (Name);
                    CREATE INDEX IF NOT EXISTS IX_MCPServers_IsEnabled ON MCPServers (IsEnabled);
                    """;
                await createCommand.ExecuteNonQueryAsync();
            }

            await connection.CloseAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error ensuring MCPServers table exists: {ex.Message}");
        }
    }
}
