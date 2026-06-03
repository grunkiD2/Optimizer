namespace Optimizer.WinUI.Services.Assistant;

/// <summary>Encrypted-at-rest storage for the user's Anthropic API key.</summary>
public interface IApiKeyStore
{
    bool HasKey { get; }
    void SetKey(string apiKey);
    string? GetKey();
    void Clear();
}
