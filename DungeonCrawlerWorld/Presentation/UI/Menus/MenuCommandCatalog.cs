namespace Presentation.UI.Menus;

/// <summary>The session's menu commands, by Guid: what a slot bound to one shows and does.</summary>
/// <remarks>
/// Made empty at the start of the shell's build, so the hotbar and the targeting controller can hold it, and filled
/// once the windows the commands open exist. A Guid no command claims is a slot that shows nothing and does nothing.
/// </remarks>
public sealed class MenuCommandCatalog
{
    private readonly Dictionary<Guid, MenuCommand> _commandsById = [];
    private readonly List<MenuCommand> _commandsInOrder = [];

    /// <summary>Every command, in the order registered.</summary>
    public IReadOnlyList<MenuCommand> Commands => _commandsInOrder;

    /// <exception cref="InvalidOperationException">A command with command's Guid is already registered.</exception>
    public void Register(MenuCommand command)
    {
        if (!_commandsById.TryAdd(command.Id, command))
        {
            throw new InvalidOperationException($"Menu command {command.Id} ({command.Name}) is already registered.");
        }

        _commandsInOrder.Add(command);
    }

    public bool TryGet(Guid menuCommandId, out MenuCommand command) => _commandsById.TryGetValue(menuCommandId, out command!);

    /// <summary>Runs the command menuCommandId names, if there is one and it can run now.</summary>
    public void TryRun(Guid menuCommandId)
    {
        if (TryGet(menuCommandId, out var command) && command.CanRun())
        {
            command.Run();
        }
    }
}
