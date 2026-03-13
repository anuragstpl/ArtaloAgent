namespace ArtaloBot.Core.Models;

/// <summary>
/// Pre-configured skill templates that users can select from.
/// </summary>
public class SkillPreset
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Icon { get; set; } = "Puzzle";

    // Configuration
    public string Command { get; set; } = string.Empty;
    public string Arguments { get; set; } = "[]";
    public string EnvironmentVariables { get; set; } = "{}";
    public string WorkingDirectory { get; set; } = string.Empty;

    // Environment variable keys that user needs to provide
    public List<SkillPresetEnvVar> RequiredEnvVars { get; set; } = [];

    // Documentation
    public string DocumentationUrl { get; set; } = string.Empty;
    public string SetupInstructions { get; set; } = string.Empty;
}

public class SkillPresetEnvVar
{
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Placeholder { get; set; } = string.Empty;
    public bool IsSecret { get; set; } = true;
}

/// <summary>
/// Static collection of available skill presets.
/// </summary>
public static class SkillPresetRegistry
{
    public static List<SkillPreset> GetAllPresets() =>
    [
        // Web & Data
        new SkillPreset
        {
            Id = "brightdata",
            Name = "Bright Data",
            Description = "Web scraping, data collection, and SERP API access",
            Category = "Web & Data",
            Icon = "Web",
            Command = "npx.cmd",
            Arguments = "[\"@brightdata/mcp\"]",
            RequiredEnvVars =
            [
                new SkillPresetEnvVar
                {
                    Key = "API_TOKEN",
                    DisplayName = "API Token",
                    Description = "Your Bright Data API token",
                    Placeholder = "Enter your Bright Data API token"
                }
            ],
            DocumentationUrl = "https://github.com/anthropics/anthropic-cookbook/tree/main/misc/mcp/brightdata",
            SetupInstructions = "Get your API token from https://brightdata.com"
        },

        new SkillPreset
        {
            Id = "brave-search",
            Name = "Brave Search",
            Description = "Web search using Brave Search API",
            Category = "Web & Data",
            Icon = "Magnify",
            Command = "npx.cmd",
            Arguments = "[\"-y\", \"@anthropic/mcp-server-brave-search\"]",
            RequiredEnvVars =
            [
                new SkillPresetEnvVar
                {
                    Key = "BRAVE_API_KEY",
                    DisplayName = "Brave API Key",
                    Description = "Your Brave Search API key",
                    Placeholder = "Enter your Brave API key"
                }
            ],
            DocumentationUrl = "https://github.com/anthropics/anthropic-cookbook/tree/main/misc/mcp",
            SetupInstructions = "Get your API key from https://brave.com/search/api/"
        },

        new SkillPreset
        {
            Id = "fetch",
            Name = "Fetch",
            Description = "Fetch and read web pages, convert HTML to markdown",
            Category = "Web & Data",
            Icon = "Download",
            Command = "npx.cmd",
            Arguments = "[\"-y\", \"@anthropic/mcp-server-fetch\"]",
            RequiredEnvVars = [],
            DocumentationUrl = "https://github.com/anthropics/anthropic-cookbook/tree/main/misc/mcp"
        },

        // Time & Utilities
        new SkillPreset
        {
            Id = "time",
            Name = "Time",
            Description = "Get current time in any timezone",
            Category = "Utilities",
            Icon = "Clock",
            Command = "npx.cmd",
            Arguments = "[\"-y\", \"@anthropic/mcp-server-time\"]",
            RequiredEnvVars = [],
            DocumentationUrl = "https://github.com/anthropics/anthropic-cookbook/tree/main/misc/mcp"
        },

        // File System
        new SkillPreset
        {
            Id = "filesystem",
            Name = "File System",
            Description = "Read, write, and manage files on your computer",
            Category = "File System",
            Icon = "Folder",
            Command = "npx.cmd",
            Arguments = "[\"-y\", \"@anthropic/mcp-server-filesystem\", \"C:/Users\"]",
            RequiredEnvVars = [],
            DocumentationUrl = "https://github.com/anthropics/anthropic-cookbook/tree/main/misc/mcp",
            SetupInstructions = "Edit the path in Arguments to specify which directories to allow access to"
        },

        // Database
        new SkillPreset
        {
            Id = "sqlite",
            Name = "SQLite",
            Description = "Query and manage SQLite databases",
            Category = "Database",
            Icon = "Database",
            Command = "npx.cmd",
            Arguments = "[\"-y\", \"@anthropic/mcp-server-sqlite\", \"--db-path\", \"C:/path/to/database.db\"]",
            RequiredEnvVars = [],
            DocumentationUrl = "https://github.com/anthropics/anthropic-cookbook/tree/main/misc/mcp",
            SetupInstructions = "Edit the --db-path in Arguments to point to your SQLite database file"
        },

        new SkillPreset
        {
            Id = "postgres",
            Name = "PostgreSQL",
            Description = "Query and manage PostgreSQL databases",
            Category = "Database",
            Icon = "Database",
            Command = "npx.cmd",
            Arguments = "[\"-y\", \"@anthropic/mcp-server-postgres\"]",
            RequiredEnvVars =
            [
                new SkillPresetEnvVar
                {
                    Key = "POSTGRES_CONNECTION_STRING",
                    DisplayName = "Connection String",
                    Description = "PostgreSQL connection string",
                    Placeholder = "postgresql://user:password@localhost:5432/dbname"
                }
            ],
            DocumentationUrl = "https://github.com/anthropics/anthropic-cookbook/tree/main/misc/mcp"
        },

        // Development
        new SkillPreset
        {
            Id = "github",
            Name = "GitHub",
            Description = "Manage GitHub repositories, issues, and pull requests",
            Category = "Development",
            Icon = "Github",
            Command = "npx.cmd",
            Arguments = "[\"-y\", \"@anthropic/mcp-server-github\"]",
            RequiredEnvVars =
            [
                new SkillPresetEnvVar
                {
                    Key = "GITHUB_TOKEN",
                    DisplayName = "GitHub Token",
                    Description = "GitHub personal access token",
                    Placeholder = "ghp_xxxxxxxxxxxx"
                }
            ],
            DocumentationUrl = "https://github.com/anthropics/anthropic-cookbook/tree/main/misc/mcp",
            SetupInstructions = "Create a personal access token at https://github.com/settings/tokens"
        },

        new SkillPreset
        {
            Id = "gitlab",
            Name = "GitLab",
            Description = "Manage GitLab repositories, issues, and merge requests",
            Category = "Development",
            Icon = "Gitlab",
            Command = "npx.cmd",
            Arguments = "[\"-y\", \"@anthropic/mcp-server-gitlab\"]",
            RequiredEnvVars =
            [
                new SkillPresetEnvVar
                {
                    Key = "GITLAB_TOKEN",
                    DisplayName = "GitLab Token",
                    Description = "GitLab personal access token",
                    Placeholder = "glpat-xxxxxxxxxxxx"
                },
                new SkillPresetEnvVar
                {
                    Key = "GITLAB_URL",
                    DisplayName = "GitLab URL",
                    Description = "GitLab instance URL (optional, defaults to gitlab.com)",
                    Placeholder = "https://gitlab.com",
                    IsSecret = false
                }
            ],
            DocumentationUrl = "https://github.com/anthropics/anthropic-cookbook/tree/main/misc/mcp"
        },

        // Memory & Knowledge
        new SkillPreset
        {
            Id = "memory",
            Name = "Memory",
            Description = "Persistent memory storage for conversations",
            Category = "Knowledge",
            Icon = "Brain",
            Command = "npx.cmd",
            Arguments = "[\"-y\", \"@anthropic/mcp-server-memory\"]",
            RequiredEnvVars = [],
            DocumentationUrl = "https://github.com/anthropics/anthropic-cookbook/tree/main/misc/mcp"
        },

        // Cloud Services
        new SkillPreset
        {
            Id = "aws-kb",
            Name = "AWS Knowledge Base",
            Description = "Access AWS Bedrock Knowledge Bases",
            Category = "Cloud",
            Icon = "Cloud",
            Command = "npx.cmd",
            Arguments = "[\"-y\", \"@anthropic/mcp-server-aws-kb-retrieval\"]",
            RequiredEnvVars =
            [
                new SkillPresetEnvVar
                {
                    Key = "AWS_ACCESS_KEY_ID",
                    DisplayName = "AWS Access Key ID",
                    Description = "Your AWS access key",
                    Placeholder = "AKIA..."
                },
                new SkillPresetEnvVar
                {
                    Key = "AWS_SECRET_ACCESS_KEY",
                    DisplayName = "AWS Secret Access Key",
                    Description = "Your AWS secret key",
                    Placeholder = "Enter your AWS secret key"
                },
                new SkillPresetEnvVar
                {
                    Key = "AWS_REGION",
                    DisplayName = "AWS Region",
                    Description = "AWS region (e.g., us-east-1)",
                    Placeholder = "us-east-1",
                    IsSecret = false
                }
            ],
            DocumentationUrl = "https://github.com/anthropics/anthropic-cookbook/tree/main/misc/mcp"
        },

        new SkillPreset
        {
            Id = "google-drive",
            Name = "Google Drive",
            Description = "Access and search Google Drive files",
            Category = "Cloud",
            Icon = "GoogleDrive",
            Command = "npx.cmd",
            Arguments = "[\"-y\", \"@anthropic/mcp-server-gdrive\"]",
            RequiredEnvVars =
            [
                new SkillPresetEnvVar
                {
                    Key = "GOOGLE_CLIENT_ID",
                    DisplayName = "Google Client ID",
                    Description = "OAuth 2.0 Client ID from Google Cloud Console",
                    Placeholder = "xxxxx.apps.googleusercontent.com"
                },
                new SkillPresetEnvVar
                {
                    Key = "GOOGLE_CLIENT_SECRET",
                    DisplayName = "Google Client Secret",
                    Description = "OAuth 2.0 Client Secret",
                    Placeholder = "Enter your client secret"
                }
            ],
            DocumentationUrl = "https://github.com/anthropics/anthropic-cookbook/tree/main/misc/mcp",
            SetupInstructions = "Create OAuth credentials at https://console.cloud.google.com"
        },

        new SkillPreset
        {
            Id = "google-maps",
            Name = "Google Maps",
            Description = "Search places, get directions, and location info",
            Category = "Location",
            Icon = "MapMarker",
            Command = "npx.cmd",
            Arguments = "[\"-y\", \"@anthropic/mcp-server-google-maps\"]",
            RequiredEnvVars =
            [
                new SkillPresetEnvVar
                {
                    Key = "GOOGLE_MAPS_API_KEY",
                    DisplayName = "Google Maps API Key",
                    Description = "Your Google Maps API key",
                    Placeholder = "AIza..."
                }
            ],
            DocumentationUrl = "https://github.com/anthropics/anthropic-cookbook/tree/main/misc/mcp",
            SetupInstructions = "Get API key from https://console.cloud.google.com"
        },

        // Communication
        new SkillPreset
        {
            Id = "slack",
            Name = "Slack",
            Description = "Read and send messages in Slack workspaces",
            Category = "Communication",
            Icon = "Slack",
            Command = "npx.cmd",
            Arguments = "[\"-y\", \"@anthropic/mcp-server-slack\"]",
            RequiredEnvVars =
            [
                new SkillPresetEnvVar
                {
                    Key = "SLACK_BOT_TOKEN",
                    DisplayName = "Slack Bot Token",
                    Description = "Slack Bot User OAuth Token",
                    Placeholder = "xoxb-..."
                },
                new SkillPresetEnvVar
                {
                    Key = "SLACK_TEAM_ID",
                    DisplayName = "Slack Team ID",
                    Description = "Your Slack workspace/team ID",
                    Placeholder = "T01234567"
                }
            ],
            DocumentationUrl = "https://github.com/anthropics/anthropic-cookbook/tree/main/misc/mcp"
        },

        // AI & Images
        new SkillPreset
        {
            Id = "everart",
            Name = "EverArt",
            Description = "Generate images using AI",
            Category = "AI & Images",
            Icon = "Image",
            Command = "npx.cmd",
            Arguments = "[\"-y\", \"@anthropic/mcp-server-everart\"]",
            RequiredEnvVars =
            [
                new SkillPresetEnvVar
                {
                    Key = "EVERART_API_KEY",
                    DisplayName = "EverArt API Key",
                    Description = "Your EverArt API key",
                    Placeholder = "Enter your EverArt API key"
                }
            ],
            DocumentationUrl = "https://github.com/anthropics/anthropic-cookbook/tree/main/misc/mcp"
        },

        // Automation
        new SkillPreset
        {
            Id = "puppeteer",
            Name = "Puppeteer",
            Description = "Browser automation - take screenshots, interact with web pages",
            Category = "Automation",
            Icon = "Robot",
            Command = "npx.cmd",
            Arguments = "[\"-y\", \"@anthropic/mcp-server-puppeteer\"]",
            RequiredEnvVars = [],
            DocumentationUrl = "https://github.com/anthropics/anthropic-cookbook/tree/main/misc/mcp"
        }
    ];

    public static List<string> GetCategories() =>
        GetAllPresets()
            .Select(p => p.Category)
            .Distinct()
            .OrderBy(c => c)
            .ToList();

    public static SkillPreset? GetPreset(string id) =>
        GetAllPresets().FirstOrDefault(p => p.Id == id);
}
