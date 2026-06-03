using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Optimizer.WinUI.Services.Analytics;
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

    private sealed class FakeSessionPersistence : ISessionPersistence
    {
        public Task<AssistantSession> GetOrCreateTodaySessionAsync() => Task.FromResult(new AssistantSession());
        public Task<List<SessionEvent>> LoadSessionEventsAsync(string sessionId) => Task.FromResult(new List<SessionEvent>());
        public Task AppendEventAsync(string sessionId, SessionEventType eventType, string content) => Task.CompletedTask;
        public Task<List<AssistantSession>> GetSessionsAsync(DateTime? since = null) => Task.FromResult(new List<AssistantSession>());
        public Task ArchiveOldSessionsAsync(int olderThanDays = 30) => Task.CompletedTask;
    }

    private sealed class FakeFeedback : IAssistantFeedbackService
    {
        public Task RecordFeedbackAsync(string? sessionId, string toolId, FeedbackVerdict verdict, string? comment = null) => Task.CompletedTask;
        public Task<int> GetNetScoreAsync(string toolId) => Task.FromResult(0);
        public Task<List<AssistantFeedbackEntry>> GetRecentFeedbackAsync(int count = 50) => Task.FromResult(new List<AssistantFeedbackEntry>());
    }

    private static AssistantViewModel NewVm(FakeAssistant assistant, FakeKeyStore keyStore)
        => new(assistant, keyStore, new FakeSessionPersistence(), new FakeFeedback(), a => a());

    [Fact]
    public async Task Send_adds_user_and_assistant_messages()
    {
        var vm = NewVm(new FakeAssistant { Reply = "Hello!" }, new FakeKeyStore(true));
        vm.Input = "hi";
        await vm.SendCommand.ExecuteAsync(null);
        Assert.Contains(vm.Messages, m => m.IsUser && m.Text == "hi");
        Assert.Contains(vm.Messages, m => m.IsAssistant && m.Text == "Hello!");
        Assert.Equal("", vm.Input);
    }

    [Fact]
    public void NoKey_state_is_exposed_when_store_is_empty()
    {
        var vm = NewVm(new FakeAssistant(), new FakeKeyStore(false));
        Assert.True(vm.NeedsApiKey);
    }

    [Fact]
    public async Task Empty_input_does_not_send()
    {
        var assistant = new FakeAssistant();
        var vm = NewVm(assistant, new FakeKeyStore(true));
        vm.Input = "   ";
        await vm.SendCommand.ExecuteAsync(null);
        Assert.Empty(vm.Messages);
    }
}
