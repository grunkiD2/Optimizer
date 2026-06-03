using System;
using System.IO;
using Optimizer.WinUI.Services.Assistant;
using Xunit;

namespace Optimizer.WinUI.Tests;

public class ApiKeyStoreTests
{
    private static string TempFile() => Path.Combine(Path.GetTempPath(), $"optimizer-key-test-{Guid.NewGuid():N}.bin");

    [Fact]
    public void Set_Get_roundtrips_the_key()
    {
        var path = TempFile();
        try
        {
            var store = new DpapiApiKeyStore(path);
            Assert.False(store.HasKey);
            store.SetKey("sk-ant-test-123");
            Assert.True(store.HasKey);
            Assert.Equal("sk-ant-test-123", store.GetKey());
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Stored_bytes_are_not_plaintext()
    {
        var path = TempFile();
        try
        {
            new DpapiApiKeyStore(path).SetKey("sk-ant-secret");
            var raw = File.ReadAllText(path);
            Assert.DoesNotContain("sk-ant-secret", raw);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Clear_removes_the_key()
    {
        var path = TempFile();
        try
        {
            var store = new DpapiApiKeyStore(path);
            store.SetKey("sk-ant-x");
            store.Clear();
            Assert.False(store.HasKey);
            Assert.Null(store.GetKey());
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
