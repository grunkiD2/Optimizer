using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.Services;
using Optimizer.WinUI.Services.Analytics;
using Optimizer.WinUI.Services.Assistant;

namespace Optimizer.WinUI.ViewModels;

public partial class AssistantViewModel : ObservableObject
{
    private readonly IAssistantService _assistant;
    private readonly IApiKeyStore _keyStore;
    private readonly ISessionPersistence _sessionPersistence;
    private readonly IAssistantFeedbackService _feedback;
    private readonly Action<Action> _dispatch;
    private string? _currentSessionId;

    /// <summary>Tools executed during the most recent turn — feedback targets these.</summary>
    private readonly List<string> _lastTurnTools = [];

    [ObservableProperty] private string input = "";
    [ObservableProperty] private bool isBusy;

    /// <summary>True once a turn has run tools, so the View can show thumbs up/down.</summary>
    [ObservableProperty] private bool canRateLastTurn;

    public ObservableCollection<ChatMessage> Messages { get; } = [];

    public bool NeedsApiKey => !_keyStore.HasKey;

    /// <summary>Set by the View to render a confirmation prompt; returns the user's choice.</summary>
    public Func<string, string, Task<bool>> ConfirmHandler { get; set; } = (_, _) => Task.FromResult(false);

    public AssistantViewModel(
        IAssistantService assistant,
        IApiKeyStore keyStore,
        ISessionPersistence sessionPersistence,
        IAssistantFeedbackService feedback,
        Action<Action> dispatch)
    {
        _assistant = assistant;
        _keyStore = keyStore;
        _sessionPersistence = sessionPersistence;
        _feedback = feedback;
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

        _lastTurnTools.Clear();
        _dispatch(() => CanRateLastTurn = false);

        var cb = new AssistantCallbacks
        {
            OnAssistantText = chunk =>
            {
                _dispatch(() => reply.Text += chunk);
            },
            OnStatus = status => _dispatch(() => Messages.Add(new ChatMessage { Role = ChatRole.Status, Text = status })),
            ConfirmAsync = (id, summary) => ConfirmHandler(id, summary),
            OnToolExecuted = (toolId, _) =>
            {
                lock (_lastTurnTools) _lastTurnTools.Add(toolId);
            },
        };

        try
        {
            await _assistant.SendAsync(text, cb, default);

            // Persist assistant response to session
            if (_currentSessionId != null)
                await _sessionPersistence.AppendEventAsync(_currentSessionId, SessionEventType.AssistantResponse, reply.Text);

            // Offer rating only if the turn actually ran tools.
            bool ran;
            lock (_lastTurnTools) ran = _lastTurnTools.Count > 0;
            _dispatch(() => CanRateLastTurn = ran);
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
        CanRateLastTurn = false;

        // Archive old sessions
        _ = _sessionPersistence.ArchiveOldSessionsAsync(30);
    }

    /// <summary>Record a thumbs-up on every tool the last turn used.</summary>
    [RelayCommand]
    private Task RateUpAsync() => RateLastTurnAsync(FeedbackVerdict.Liked);

    /// <summary>Record a thumbs-down on every tool the last turn used.</summary>
    [RelayCommand]
    private Task RateDownAsync() => RateLastTurnAsync(FeedbackVerdict.Disliked);

    private async Task RateLastTurnAsync(FeedbackVerdict verdict)
    {
        List<string> tools;
        lock (_lastTurnTools) tools = _lastTurnTools.Distinct().ToList();
        if (tools.Count == 0) return;

        foreach (var toolId in tools)
            await _feedback.RecordFeedbackAsync(_currentSessionId, toolId, verdict);

        _dispatch(() =>
        {
            CanRateLastTurn = false;
            Messages.Add(new ChatMessage
            {
                Role = ChatRole.Status,
                Text = verdict == FeedbackVerdict.Liked ? "Thanks — glad that helped." : "Thanks — I'll weight that lower next time."
            });
        });
    }
}
