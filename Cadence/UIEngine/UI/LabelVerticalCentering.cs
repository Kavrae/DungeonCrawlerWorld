namespace UIEngine.UI;

/// <summary>How a centred Label places its text down its height.</summary>
public enum LabelVerticalCentering
{
    /// <summary>Centres the font's ink band, ascenders to descenders, so words share one baseline whichever letters they hold -- a caption, a heading.</summary>
    FontInkBand,

    /// <summary>Centres the shown text's own ink, as a picture -- a title button's "X", where the font's baseline would leave it high.</summary>
    TextInk,
}
