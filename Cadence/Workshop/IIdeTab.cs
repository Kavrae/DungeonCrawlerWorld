using Microsoft.Xna.Framework;

namespace Workshop;

/// <summary>One tool in the IDE: the shell shows one tab at a time in the area under its tab strip.</summary>
/// <remarks>
/// A tab owns the top-level windows it adds to the shell's layers: Open adds them, Close removes them, and nothing of a
/// closed tab is updated or drawn. The shell hands Resize the new area whenever the IDE's window changes size.
/// </remarks>
public interface IIdeTab
{
    /// <summary>The name on the tab strip, and what <c>--tab=</c> selects.</summary>
    string Title { get; }

    void Open(WorkshopTabContext context, Rectangle area);

    void Resize(Rectangle area);

    void Close();

    /// <summary>Per-frame work of the open tab's own, beyond its windows' (which the shell's layer host updates).</summary>
    void Update(GameTime gameTime) { }
}
