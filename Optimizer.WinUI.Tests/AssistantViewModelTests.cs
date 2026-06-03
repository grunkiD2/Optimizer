using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Optimizer.WinUI.Services.Assistant;
using Optimizer.WinUI.ViewModels;
using Xunit;

namespace Optimizer.WinUI.Tests;

public class AssistantViewModelTests
{
    private sealed class FakeAssistant : IAssistantService
    {
        public string Reply { get; set; } = "hi";
        public Task<string> SendAsync(string userText, AssistantCallbacks cb, CancellationToken ct)
        {
            cb.OnAssistantText(Reply);
            return Task.FromResult(Reply);
        }
        public void Reset() { }
    }

    private sealed class FakeKeyStore(bool hasKey) : IApiKeyStore
    {
        public bool HasKey => hasKey;
        public void SetKey(string apiKey) { }
        public string? GetKey() => hasKey ? "k" : null;
        public void Clear() { }
    }

    [Fact]
    public async Task Send_adds_user_and_assistant_messages()
    {
        var vm = new AssistantViewModel(new FakeAssistant { Reply = "Hello!" }, new FakeKeyStore(true), a => a());
        vm.Input = "hi";
        await vm.SendCommand.ExecuteAsync(null);
        Assert.Contains(vm.Messages, m => m.IsUser && m.Text == "hi");
        Assert.Contains(vm.Messages, m => m.IsAssistant && m.Text == "Hello!");
        Assert.Equal("", vm.Input);
    }

    [Fact]
    public void NoKey_state_is_exposed_when_store_is_empty()
    {
        var vm = new AssistantViewModel(new FakeAssistant(), new FakeKeyStore(false), a => a());
        Assert.True(vm.NeedsApiKey);
    }

    [Fact]
    public async Task Empty_input_does_not_send()
    {
        var assistant = new FakeAssistant();
        var vm = new AssistantViewModel(assistant, new FakeKeyStore(true), a => a());
        vm.Input = "   ";
        await vm.SendCommand.ExecuteAsync(null);
        Assert.Empty(vm.Messages);
    }
}
