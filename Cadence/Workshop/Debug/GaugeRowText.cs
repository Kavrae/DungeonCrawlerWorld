namespace Workshop.Debug;

/// <summary>One gauge's row in the gauge list, formatted: its latest value and its minimum, maximum and average over the window shown.</summary>
public sealed record GaugeRowText(string Value, string Minimum, string Maximum, string Average);
