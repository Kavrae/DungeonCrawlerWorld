namespace Presentation.UI.Content;

/// <summary>A button at the right end of a CurrencyRowContent.</summary>
/// <param name="Label">The text it shows.</param>
/// <param name="Width">Its width, taken from the row's currency amounts.</param>
/// <param name="CanPress">Whether it is enabled, asked every update.</param>
/// <param name="Pressed">What a press does.</param>
public sealed record CurrencyRowButton(string Label, float Width, Func<bool> CanPress, Action Pressed);
