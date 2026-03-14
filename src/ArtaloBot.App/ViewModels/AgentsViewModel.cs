using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using ArtaloBot.Core.Interfaces;
using ArtaloBot.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace ArtaloBot.App.ViewModels;

public partial class AgentsViewModel : ObservableObject
{
    private readonly IAgentService _agentService;
    private readonly IDocumentProcessor _documentProcessor;
    private readonly IDebugService? _debugService;

    [ObservableProperty]
    private ObservableCollection<AgentItemViewModel> _agents = [];

    [ObservableProperty]
    private AgentItemViewModel? _selectedAgent;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _showAgentEditor;

    [ObservableProperty]
    private bool _isNewAgent;

    // Editor fields
    [ObservableProperty]
    private string _editName = string.Empty;

    [ObservableProperty]
    private string _editDescription = string.Empty;

    [ObservableProperty]
    private string _editSystemPrompt = string.Empty;

    [ObservableProperty]
    private string _editIcon = "Robot";

    [ObservableProperty]
    private bool _editIsEnabled = true;

    // Paste text dialog fields
    [ObservableProperty]
    private bool _showPasteDialog;

    [ObservableProperty]
    private string _pasteText = string.Empty;

    [ObservableProperty]
    private string _pasteDocumentName = "Pasted Content";

    [ObservableProperty]
    private bool _pasteOverwrite; // false = append, true = overwrite

    [ObservableProperty]
    private string _pasteInstructions = string.Empty;

    public IReadOnlyList<string> AvailableIcons { get; } =
    [
        "Robot", "Account", "Package", "Cart", "Store", "Calculator",
        "FileDocument", "Database", "BookOpen", "School", "Briefcase",
        "Heart", "Star", "Lightbulb", "Cog", "Shield"
    ];

    public AgentsViewModel(
        IAgentService agentService,
        IDocumentProcessor documentProcessor,
        IDebugService? debugService = null)
    {
        _agentService = agentService;
        _documentProcessor = documentProcessor;
        _debugService = debugService;
    }

    public async Task LoadAgentsAsync()
    {
        IsLoading = true;
        StatusMessage = "Loading agents...";

        try
        {
            var agents = await _agentService.GetAllAgentsAsync();

            Agents.Clear();
            foreach (var agent in agents)
            {
                var vm = new AgentItemViewModel(agent)
                {
                    TotalDocuments = agent.Documents.Count,
                    TotalChunks = await _agentService.GetTotalChunksAsync(agent.Id)
                };

                // Set document items
                foreach (var doc in agent.Documents)
                {
                    vm.Documents.Add(new DocumentItemViewModel(doc));
                }

                Agents.Add(vm);
            }

            StatusMessage = $"Loaded {agents.Count} agent(s)";
            _debugService?.Info("AgentsVM", $"Loaded {agents.Count} agents");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            _debugService?.Error("AgentsVM", "Failed to load agents", ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void NewAgent()
    {
        IsNewAgent = true;
        EditName = "";
        EditDescription = "";
        EditSystemPrompt = GetDefaultSystemPrompt();
        EditIcon = "Robot";
        EditIsEnabled = true;
        ShowAgentEditor = true;
    }

    [RelayCommand]
    private void EditAgent(AgentItemViewModel? agent)
    {
        if (agent == null) return;

        IsNewAgent = false;
        EditName = agent.Name;
        EditDescription = agent.Description;
        EditSystemPrompt = agent.SystemPrompt;
        EditIcon = agent.Icon;
        EditIsEnabled = agent.IsEnabled;
        SelectedAgent = agent;
        ShowAgentEditor = true;
    }

    [RelayCommand]
    private async Task SaveAgent()
    {
        if (string.IsNullOrWhiteSpace(EditName))
        {
            StatusMessage = "Agent name is required";
            return;
        }

        IsLoading = true;

        try
        {
            if (IsNewAgent)
            {
                var agent = new Agent
                {
                    Name = EditName,
                    Description = EditDescription,
                    SystemPrompt = EditSystemPrompt,
                    Icon = EditIcon,
                    IsEnabled = EditIsEnabled
                };

                var created = await _agentService.CreateAgentAsync(agent);
                var vm = new AgentItemViewModel(created);
                Agents.Add(vm);
                SelectedAgent = vm;

                StatusMessage = $"Created agent: {created.Name}";
            }
            else if (SelectedAgent != null)
            {
                var agent = new Agent
                {
                    Id = SelectedAgent.Id,
                    Name = EditName,
                    Description = EditDescription,
                    SystemPrompt = EditSystemPrompt,
                    Icon = EditIcon,
                    IsEnabled = EditIsEnabled
                };

                await _agentService.UpdateAgentAsync(agent);

                SelectedAgent.Name = EditName;
                SelectedAgent.Description = EditDescription;
                SelectedAgent.SystemPrompt = EditSystemPrompt;
                SelectedAgent.Icon = EditIcon;
                SelectedAgent.IsEnabled = EditIsEnabled;

                StatusMessage = $"Updated agent: {agent.Name}";
            }

            ShowAgentEditor = false;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void CancelEdit()
    {
        ShowAgentEditor = false;
    }

    [RelayCommand]
    private async Task DeleteAgent(AgentItemViewModel? agent)
    {
        if (agent == null) return;

        var result = MessageBox.Show(
            $"Delete agent '{agent.Name}' and all its documents?",
            "Confirm Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        IsLoading = true;

        try
        {
            await _agentService.DeleteAgentAsync(agent.Id);
            Agents.Remove(agent);

            if (SelectedAgent == agent)
                SelectedAgent = null;

            StatusMessage = $"Deleted agent: {agent.Name}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task SetDefaultAgent(AgentItemViewModel? agent)
    {
        if (agent == null) return;

        try
        {
            await _agentService.SetDefaultAgentAsync(agent.Id);

            foreach (var a in Agents)
            {
                a.IsDefault = a.Id == agent.Id;
            }

            StatusMessage = $"Set default agent: {agent.Name}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task UploadDocument()
    {
        if (SelectedAgent == null)
        {
            StatusMessage = "Please select an agent first";
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Select Document to Upload",
            Filter = "All Supported|*.txt;*.md;*.csv;*.json;*.pdf;*.doc;*.docx;*.xls;*.xlsx|" +
                     "Text Files|*.txt;*.md|" +
                     "Data Files|*.csv;*.json|" +
                     "PDF Documents|*.pdf|" +
                     "Word Documents|*.doc;*.docx|" +
                     "Excel Files|*.xls;*.xlsx|" +
                     "All Files|*.*",
            Multiselect = true
        };

        if (dialog.ShowDialog() != true) return;

        IsLoading = true;
        var uploadedCount = 0;

        try
        {
            foreach (var filePath in dialog.FileNames)
            {
                var fileName = Path.GetFileName(filePath);

                if (!_documentProcessor.IsSupported(fileName))
                {
                    StatusMessage = $"Unsupported file type: {fileName}";
                    continue;
                }

                StatusMessage = $"Uploading {fileName}...";

                var document = await _agentService.AddDocumentAsync(
                    SelectedAgent.Id,
                    filePath,
                    fileName);

                var docVm = new DocumentItemViewModel(document);
                SelectedAgent.Documents.Add(docVm);
                SelectedAgent.TotalDocuments++;

                uploadedCount++;
            }

            StatusMessage = $"Uploaded {uploadedCount} document(s). Processing in background...";
            _debugService?.Success("AgentsVM", $"Uploaded {uploadedCount} documents");

            // Refresh after a delay to show processing status
            _ = RefreshDocumentsAfterDelay(SelectedAgent.Id);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            _debugService?.Error("AgentsVM", "Upload failed", ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task RefreshDocumentsAfterDelay(int agentId)
    {
        // Wait for processing to start
        await Task.Delay(2000);

        // Refresh documents
        await RefreshDocuments(agentId);

        // Continue refreshing while any documents are processing
        for (int i = 0; i < 60; i++) // Max 60 iterations (2 minutes)
        {
            var agent = Agents.FirstOrDefault(a => a.Id == agentId);
            if (agent == null) break;

            var hasProcessing = agent.Documents.Any(d =>
                d.Status == DocumentProcessingStatus.Pending ||
                d.Status == DocumentProcessingStatus.Processing);

            if (!hasProcessing) break;

            await Task.Delay(2000);
            await RefreshDocuments(agentId);
        }
    }

    private async Task RefreshDocuments(int agentId)
    {
        try
        {
            var agent = Agents.FirstOrDefault(a => a.Id == agentId);
            if (agent == null) return;

            var documents = await _agentService.GetDocumentsAsync(agentId);
            var totalChunks = await _agentService.GetTotalChunksAsync(agentId);

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                agent.Documents.Clear();
                foreach (var doc in documents)
                {
                    agent.Documents.Add(new DocumentItemViewModel(doc));
                }
                agent.TotalDocuments = documents.Count;
                agent.TotalChunks = totalChunks;
            });
        }
        catch { /* Ignore */ }
    }

    [RelayCommand]
    private async Task DeleteDocument(DocumentItemViewModel? document)
    {
        if (document == null || SelectedAgent == null) return;

        var result = MessageBox.Show(
            $"Delete document '{document.FileName}'?",
            "Confirm Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            await _agentService.DeleteDocumentAsync(document.Id);
            SelectedAgent.Documents.Remove(document);
            SelectedAgent.TotalDocuments--;

            StatusMessage = $"Deleted document: {document.FileName}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ReprocessDocument(DocumentItemViewModel? document)
    {
        if (document == null) return;

        try
        {
            document.Status = DocumentProcessingStatus.Processing;
            await _agentService.ReprocessDocumentAsync(document.Id);
            StatusMessage = $"Reprocessing: {document.FileName}";

            // Refresh after delay
            if (SelectedAgent != null)
            {
                _ = RefreshDocumentsAfterDelay(SelectedAgent.Id);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ShowPasteText()
    {
        if (SelectedAgent == null)
        {
            StatusMessage = "Please select an agent first";
            return;
        }

        PasteText = "";
        PasteDocumentName = $"Pasted_{DateTime.Now:yyyyMMdd_HHmmss}";
        PasteOverwrite = false;
        PasteInstructions = GetPasteInstructions();
        ShowPasteDialog = true;
    }

    [RelayCommand]
    private void CancelPaste()
    {
        ShowPasteDialog = false;
        PasteText = "";
    }

    [RelayCommand]
    private async Task ProcessPastedText()
    {
        if (SelectedAgent == null || string.IsNullOrWhiteSpace(PasteText))
        {
            StatusMessage = "Please enter some text";
            return;
        }

        IsLoading = true;
        ShowPasteDialog = false;

        try
        {
            // If overwrite mode, delete all existing documents first
            if (PasteOverwrite)
            {
                var confirmResult = MessageBox.Show(
                    $"This will DELETE all existing documents for '{SelectedAgent.Name}' and replace with the new content.\n\nAre you sure?",
                    "Confirm Overwrite",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (confirmResult != MessageBoxResult.Yes)
                {
                    IsLoading = false;
                    ShowPasteDialog = true;
                    return;
                }

                // Delete all existing documents
                var existingDocs = SelectedAgent.Documents.ToList();
                foreach (var doc in existingDocs)
                {
                    await _agentService.DeleteDocumentAsync(doc.Id);
                }
                SelectedAgent.Documents.Clear();
                SelectedAgent.TotalDocuments = 0;
                SelectedAgent.TotalChunks = 0;

                _debugService?.Info("AgentsVM", $"Cleared {existingDocs.Count} existing documents for overwrite");
            }

            // Save pasted text to a temp file
            var tempDir = Path.Combine(Path.GetTempPath(), "ArtaloBot");
            Directory.CreateDirectory(tempDir);

            var fileName = $"{PasteDocumentName}.txt";
            var tempPath = Path.Combine(tempDir, fileName);

            await File.WriteAllTextAsync(tempPath, PasteText);

            StatusMessage = $"Processing pasted content ({PasteText.Length} chars)...";
            _debugService?.Info("AgentsVM", $"Processing pasted text: {PasteText.Length} chars",
                $"Mode: {(PasteOverwrite ? "Overwrite" : "Append")}");

            // Add as document
            var document = await _agentService.AddDocumentAsync(
                SelectedAgent.Id,
                tempPath,
                fileName);

            var docVm = new DocumentItemViewModel(document);
            SelectedAgent.Documents.Add(docVm);
            SelectedAgent.TotalDocuments++;

            StatusMessage = $"Content added. Processing {PasteText.Length} characters...";

            // Clean up temp file
            try { File.Delete(tempPath); } catch { }

            // Refresh after processing
            _ = RefreshDocumentsAfterDelay(SelectedAgent.Id);

            PasteText = "";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            _debugService?.Error("AgentsVM", "Failed to process pasted text", ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task ClearAllDocuments()
    {
        if (SelectedAgent == null) return;

        var result = MessageBox.Show(
            $"Delete ALL documents for '{SelectedAgent.Name}'?\n\nThis will remove all knowledge from this agent.",
            "Clear All Documents",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        IsLoading = true;

        try
        {
            var docs = SelectedAgent.Documents.ToList();
            foreach (var doc in docs)
            {
                await _agentService.DeleteDocumentAsync(doc.Id);
            }

            SelectedAgent.Documents.Clear();
            SelectedAgent.TotalDocuments = 0;
            SelectedAgent.TotalChunks = 0;

            StatusMessage = $"Cleared all documents from {SelectedAgent.Name}";
            _debugService?.Info("AgentsVM", $"Cleared {docs.Count} documents");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private static string GetPasteInstructions()
    {
        return """
            📋 PASTE YOUR DATA HERE

            Supported formats:
            ─────────────────────────────────────
            • Plain text paragraphs
            • Product lists (one per line)
            • CSV data (with headers)
            • JSON data
            • Q&A pairs
            • Any structured information

            💡 TIPS FOR BEST RESULTS:
            ─────────────────────────────────────
            1. Include headers/labels for data
               Example: "Product: iPhone 15, Price: $999, Stock: 50"

            2. Separate entries with blank lines

            3. For products, include:
               - Product name
               - Description
               - Price
               - Any relevant details

            4. Keep related info together

            📌 EXAMPLE FORMAT:
            ─────────────────────────────────────
            Product: Premium Widget
            Price: $49.99
            Category: Electronics
            Description: High-quality widget with advanced features
            Stock: In Stock

            Product: Basic Widget
            Price: $19.99
            Category: Electronics
            Description: Entry-level widget for beginners
            Stock: Limited
            """;
    }

    private static string GetDefaultSystemPrompt()
    {
        return """
            You are a helpful AI assistant with access to specific knowledge documents.
            When answering questions:
            1. Use the provided context from documents to answer accurately
            2. If the answer is in the documents, cite the relevant information
            3. If you don't find the answer in the documents, say so clearly
            4. Be concise and helpful
            """;
    }
}

public partial class AgentItemViewModel : ObservableObject
{
    public int Id { get; set; }

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _systemPrompt = string.Empty;

    [ObservableProperty]
    private string _icon = "Robot";

    [ObservableProperty]
    private bool _isEnabled = true;

    [ObservableProperty]
    private bool _isDefault;

    [ObservableProperty]
    private int _totalDocuments;

    [ObservableProperty]
    private int _totalChunks;

    [ObservableProperty]
    private ObservableCollection<DocumentItemViewModel> _documents = [];

    public AgentItemViewModel()
    {
    }

    public AgentItemViewModel(Agent agent)
    {
        Id = agent.Id;
        Name = agent.Name;
        Description = agent.Description;
        SystemPrompt = agent.SystemPrompt;
        Icon = agent.Icon;
        IsEnabled = agent.IsEnabled;
        IsDefault = agent.IsDefault;
    }
}

public partial class DocumentItemViewModel : ObservableObject
{
    public int Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FileType { get; set; } = string.Empty;
    public long FileSize { get; set; }

    [ObservableProperty]
    private DocumentProcessingStatus _status;

    [ObservableProperty]
    private int _chunkCount;

    [ObservableProperty]
    private string? _errorMessage;

    public DateTime UploadedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }

    public string FileSizeFormatted => FormatFileSize(FileSize);
    public string StatusText => Status.ToString();

    public DocumentItemViewModel()
    {
    }

    public DocumentItemViewModel(AgentDocument doc)
    {
        Id = doc.Id;
        FileName = doc.FileName;
        FileType = doc.FileType;
        FileSize = doc.FileSize;
        Status = doc.Status;
        ChunkCount = doc.ChunkCount;
        ErrorMessage = doc.ErrorMessage;
        UploadedAt = doc.UploadedAt;
        ProcessedAt = doc.ProcessedAt;
    }

    private static string FormatFileSize(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB"];
        int order = 0;
        double size = bytes;

        while (size >= 1024 && order < suffixes.Length - 1)
        {
            order++;
            size /= 1024;
        }

        return $"{size:0.##} {suffixes[order]}";
    }
}
