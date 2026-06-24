namespace MidgardStudio.Core.Commands;

/// <summary>Groups several commands into one undo step (e.g. all field edits of a single form save).</summary>
public sealed class CompositeCommand : IEditCommand
{
    private readonly List<IEditCommand> _commands = new();

    public CompositeCommand(string description) => Description = description;

    public string Description { get; }

    public int Count => _commands.Count;

    public void Add(IEditCommand command) => _commands.Add(command);

    public void Do()
    {
        foreach (var command in _commands)
            command.Do();
    }

    public void Undo()
    {
        for (int i = _commands.Count - 1; i >= 0; i--)
            _commands[i].Undo();
    }
}
