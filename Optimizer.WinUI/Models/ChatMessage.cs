using CommunityToolkit.Mvvm.ComponentModel;

namespace Optimizer.WinUI.Models;

public enum ChatRole { User, Assistant, Status }

public partial class ChatMessage : ObservableObject
{
    [ObservableProperty] private string text = "";
    public ChatRole Role { get; init; }
    public bool IsUser => Role == ChatRole.User;
    public bool IsAssistant => Role == ChatRole.Assistant;
    public bool IsStatus => Role == ChatRole.Status;
}
