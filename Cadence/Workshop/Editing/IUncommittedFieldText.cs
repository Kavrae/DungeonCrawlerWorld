namespace Workshop.Editing;

/// <summary>Text typed into a field and not yet committed as an edit, which undo puts back and a save commits first.</summary>
public interface IUncommittedFieldText
{
    /// <summary>Puts back what was typed; true when there was some.</summary>
    bool RevertUncommitted();

    /// <summary>Commits what was typed, as Enter would; true when there was some.</summary>
    bool CommitUncommitted();
}
