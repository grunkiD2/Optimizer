using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.Services;
using Optimizer.WinUI.Services.Assistant;

namespace Optimizer.WinUI.ViewModels;

public partial class AssistantViewModel : ObservableObject
{
    private readonly IAssistantService _assistant;
    private readonly IApiKeyStore _keyStore;
    private readonly ISessionPersistence _sessionPersistence;
    private readonly Action<Action> _dispatch;
    private string? _currentSessionId;

    [ObservableProperty] private string input = "";
    [ObservableProperty] private bool isBusy;

    public ObservableCollection<ChatMessage> Messages { get; } = [];

    public bool NeedsApiKey => !_keyStore.HasKey;

    /// <summary>Set by the View to render a confirmation prompt; returns the user's choice.</summary>
    public Func<string, string, Task<bool>> ConfirmHandler { get; set; } = (_, _) => Task.FromResult(false);

    public AssistantViewModel(
        IAssistantService assistant,
        IApiKeyStore keyStore,
        ISessionPersistence sessionPersistence,
        Action<Action> dispatch)
    {
        _assistant = assistant;
        _keyStore = keyStore;
        _sessionPersistence = sessionPersistence;
        _dispatch = dispatch;
    }

    /// <summary>Load today's session history (called by MainWindow or View).</summary>
    public async Task LoadSessionAsync()
    {
        try
        {
            var session = await _sessionPersistence.GetOrCreateTodaySessionAsync();
            _currentSessionId = session.Id;

            var events = await _sessionPersistence.LoadSessionEventsAsync(session.Id);
            _dispatch(() =>
            {
                Messages.Clear();
                foreach (var evt in events)
                {
                    var role = evt.EventType switch
                    {
                        SessionEventType.UserMessage => ChatRole.User,
                        SessionEventType.ToolCall => ChatRole.Status,
                        SessionEventType.Error => ChatRole.Status,
                        _ => ChatRole.Assistant
                    };
                    Messages.Add(new ChatMessage { Role = role, Text = evt.Content });
                }
            });
        }
        catch (Exception ex)
        {
            EngineLog.Error("Failed to load session", ex);
        }
    }

    [RelayCommand]
    private async Task SendAsync()
    {
        var text = Input.Trim();
        if (text.Length == 0 || IsBusy) return;

        Input = "";
        Messages.Add(new ChatMessage { Role = ChatRole.User, Text = text });

        // Persist user message to session
        if (_currentSessionId != null)
            await _sessionPersistence.AppendEventAsync(_currentSessionId, SessionEventType.UserMessage, text);

        var reply = new ChatMessage { Role = ChatRole.Assistant, Text = "" };
        Messages.Add(reply);
        IsBusy = true;

        var cb = new AssistantCallbacks
        {
            OnAssistantText = chunk =>
            {
                _dispatch(() => reply.Text += chunk);
            },
            OnStatus = status => _dispatch(() => Messages.Add(new ChatMessage { Role = ChatRole.Status, Text = status })),
            ConfirmAsync = (id, summary) => ConfirmHandler(id, summary),
        };

        try
        {
            await _assistant.SendAsync(text, cb, default);

            // Persist assistant response to session
            if (_currentSessionId != null)
                await _sessionPersistence.AppendEventAsync(_currentSessionId, SessionEventType.AssistantResponse, reply.Text);
        }
        catch (Exception ex)
        {
            var errorMsg = $"Error: {ex.Message}";
            _dispatch(() => reply.Text = errorMsg);

            // Persist error to session
            if (_currentSessionId != null)
                await _sessionPersistence.AppendEventAsync(_currentSessionId, SessionEventType.Error, errorMsg);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ClearAsync()
    {
        Messages.Clear();
        _assistant.Reset();

        // Archive old sessions
        _ = _sessionPersistence.ArchiveOldSessionsAsync(30);
    }
}
