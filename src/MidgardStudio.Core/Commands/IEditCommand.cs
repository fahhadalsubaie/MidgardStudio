namespace MidgardStudio.Core.Commands;

/// <summary>A reversible edit. Every mutation routes through one of these for undo/redo.</summary>
public interface IEditCommand
{
    string Description { get; }

    void Do();

    void Undo();
}
