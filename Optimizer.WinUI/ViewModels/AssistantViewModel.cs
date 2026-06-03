using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.Services.Assistant;

namespace Optimizer.WinUI.ViewModels;

public partial class AssistantViewModel : ObservableObject
{
    private readonly IAssistantService _assistant;
    private readonly IApiKeyStore _keyStore;
    private readonly Action<Action> _dispatch;

    [ObservableProperty] private string input = "";
    [ObservableProperty] private bool isBusy;

    public ObservableCollection<ChatMessage> Messages { get; } = [];

    public bool NeedsApiKey => !_keyStore.HasKey;

    /// <summary>Set by the View to render a confirmation prompt; returns the user's choice.</summary>
    public Func<string, string, Task<bool>> ConfirmHandler { get; set; } = (_, _) => Task.FromResult(false);

    public AssistantViewModel(IAssistantService assistant, IApiKeyStore keyStore, Action<Action> dispatch)
    {
        _assistant = assistant;
        _keyStore = keyStore;
        _dispatch = dispatch;
    }

    [RelayCommand]
    private async Task SendAsync()
    {
        var text = Input.Trim();
        if (text.Length == 0 || IsBusy) return;

        Input = "";
        Messages.Add(new ChatMessage { Role = ChatRole.User, Text = text });
        var reply = new ChatMessage { Role = ChatRole.Assistant, Text = "" };
        Messages.Add(reply);
        IsBusy = true;

        var cb = new AssistantCallbacks
        {
            OnAssistantText = chunk => _dispatch(() => reply.Text += chunk),
            OnStatus = status => _dispatch(() => Messages.Add(new ChatMessage { Role = ChatRole.Status, Text = status })),
            ConfirmAsync = (id, summary) => ConfirmHandler(id, summary),
        };

        try { await _assistant.SendAsync(text, cb, default); }
        catch (Exception ex) { _dispatch(() => reply.Text = $"Error: {ex.Message}"); }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void Clear()
    {
        Messages.Clear();
        _assistant.Reset();
    }
}
