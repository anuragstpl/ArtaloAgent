using System.Collections.ObjectModel;
using ArtaloBot.Core.Interfaces;
using ArtaloBot.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArtaloBot.App.ViewModels;

public partial class SessionsViewModel : ObservableObject
{
    private readonly IChatRepository _chatRepository;
    private readonly ChatViewModel _chatViewModel;

    [ObservableProperty]
    private ObservableCollection<SessionItemViewModel> _sessions = [];

    [ObservableProperty]
    private SessionItemViewModel? _selectedSession;

    [ObservableProperty]
    private string _searchText = string.Empty;

    public SessionsViewModel(IChatRepository chatRepository, ChatViewModel chatViewModel)
    {
        _chatRepository = chatRepository;
        _chatViewModel = chatViewModel;
    }

    public async Task LoadSessionsAsync()
    {
        var sessions = await _chatRepository.GetAllSessionsAsync();
        Sessions.Clear();

        foreach (var session in sessions)
        {
            Sessions.Add(new SessionItemViewModel(session));
        }
    }

    public async Task CreateNewSessionAsync()
    {
        var session = await _chatRepository.CreateSessionAsync(new ChatSession
        {
            Title = "New Chat",
            Provider = _chatViewModel.SelectedProvider,
            Model = _chatViewModel.SelectedModel
        });

        var sessionVm = new SessionItemViewModel(session);
        Sessions.Insert(0, sessionVm);
        SelectedSession = sessionVm;

        _chatViewModel.Messages.Clear();
        _chatViewModel.CurrentSession = session;
    }

    partial void OnSelectedSessionChanged(SessionItemViewModel? value)
    {
        if (value != null)
        {
            _ = _chatViewModel.LoadSessionAsync(value.Id);
        }
    }

    [RelayCommand]
    private async Task DeleteSession(SessionItemViewModel session)
    {
        await _chatRepository.DeleteSessionAsync(session.Id);
        Sessions.Remove(session);

        if (SelectedSession == session)
        {
            SelectedSession = Sessions.FirstOrDefault();
        }
    }

    [RelayCommand]
    private async Task ArchiveSession(SessionItemViewModel session)
    {
        var fullSession = await _chatRepository.GetSessionAsync(session.Id);
        if (fullSession != null)
        {
            fullSession.IsArchived = true;
            await _chatRepository.UpdateSessionAsync(fullSession);
            Sessions.Remove(session);
        }
    }

    [RelayCommand]
    private async Task RenameSession(SessionItemViewModel session)
    {
        if (string.IsNullOrWhiteSpace(session.Title)) return;

        var fullSession = await _chatRepository.GetSessionAsync(session.Id);
        if (fullSession != null)
        {
            fullSession.Title = session.Title;
            await _chatRepository.UpdateSessionAsync(fullSession);
        }
    }
}

public partial class SessionItemViewModel : ObservableObject
{
    public int Id { get; }

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private DateTime _updatedAt;

    [ObservableProperty]
    private LLMProviderType _provider;

    [ObservableProperty]
    private int _messageCount;

    [ObservableProperty]
    private bool _isEditing;

    public string ProviderIcon => Provider switch
    {
        LLMProviderType.OpenAI => "Robot",
        LLMProviderType.Gemini => "StarFourPoints",
        LLMProviderType.Ollama => "Server",
        _ => "Chat"
    };

    public string TimeAgo
    {
        get
        {
            var diff = DateTime.UtcNow - UpdatedAt;
            return diff.TotalMinutes < 1 ? "Just now" :
                   diff.TotalHours < 1 ? $"{(int)diff.TotalMinutes}m ago" :
                   diff.TotalDays < 1 ? $"{(int)diff.TotalHours}h ago" :
                   diff.TotalDays < 7 ? $"{(int)diff.TotalDays}d ago" :
                   UpdatedAt.ToString("MMM d");
        }
    }

    public SessionItemViewModel(ChatSession session)
    {
        Id = session.Id;
        _title = session.Title;
        _updatedAt = session.UpdatedAt;
        _provider = session.Provider;
        _messageCount = session.Messages.Count;
    }
}
