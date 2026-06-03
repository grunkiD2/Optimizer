namespace Optimizer.WinUI.Services.Commands;

public sealed class CommandRegistry : ICommandRegistry
{
    private readonly List<IAppCommand> _commands = [];
    private readonly Dictionary<string, IAppCommand> _byId = new(StringComparer.Ordinal);

    public IReadOnlyList<IAppCommand> Commands => _commands;

    public void Register(IAppCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!_byId.TryAdd(command.Id, command))
            throw new InvalidOperationException($"Duplicate command id '{command.Id}'.");
        _commands.Add(command);
    }

    public IAppCommand? Find(string id) => _byId.GetValueOrDefault(id);
}
