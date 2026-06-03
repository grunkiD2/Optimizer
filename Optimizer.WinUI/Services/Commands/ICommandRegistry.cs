namespace Optimizer.WinUI.Services.Commands;

public interface ICommandRegistry
{
    void Register(IAppCommand command);
    IReadOnlyList<IAppCommand> Commands { get; }
    IAppCommand? Find(string id);
}
