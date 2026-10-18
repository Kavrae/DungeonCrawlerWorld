namespace Workshop;

/// <summary>The shell's status line, on the tab strip: one short message at a time, replaced by the next.</summary>
public interface IWorkshopStatus
{
    void Show(string message);
}
