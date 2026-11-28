using Microsoft.Xna.Framework;
using UIEngine.Rendering;
using UIEngine.UI;
using UIEngine.UI.ColorPalettes;

namespace Presentation.UI.Targeting;

/// <summary>Draws a planned TargetSchematic's strokes: the caster in gold, what it does in body ink, an aura's bands in its glow, the drafting lines in border grey.</summary>
/// <remarks>The labels are not drawn here: each is a stat line its row places at the schematic's label slots, so it keeps its tooltip and comparison colour.</remarks>
public sealed class TargetSchematicElement(ElementServices services)
    : Element(services)
{
    private static readonly Color CasterColor = Color.Gold;
    private static readonly Color InkColor = Color.White;
    private static readonly Color DraftingColor = WindowPalette.BorderColor;

    private TargetSchematic? _schematic;

    public void Configure(TargetSchematic schematic) => _schematic = schematic;

    public override void DrawContent(GameTime gameTime)
    {
        if (_schematic is null)
        {
            return;
        }

        var batch = Render.Batch;
        foreach (var stroke in _schematic.Strokes)
        {
            DrawStroke(batch, stroke, stroke.Role == TargetSchematicStrokeRole.AuraBand ? stroke.Fill : ColorOf(stroke.Role));
        }
    }

    private static Color ColorOf(TargetSchematicStrokeRole role) => role switch
    {
        TargetSchematicStrokeRole.Caster => CasterColor,
        TargetSchematicStrokeRole.Aim or TargetSchematicStrokeRole.Path or TargetSchematicStrokeRole.Shape => InkColor,
        TargetSchematicStrokeRole.FaintShape => InkColor * FaintShapeOpacity,
        _ => DraftingColor,
    };

    private const float FaintShapeOpacity = 0.35f;

    private static void DrawStroke(SpriteEffectBatch batch, TargetSchematicStroke stroke, Color color)
    {
        var style = SpriteDrawStyle.Plain;
        switch (stroke.Kind)
        {
            case TargetSchematicStrokeKind.Line when stroke.Pattern.Dash > 0f:
                StrokeRenderer.PatternedLine(batch, stroke.Start, stroke.End, stroke.Thickness, stroke.Pattern, color, style);
                break;
            case TargetSchematicStrokeKind.Line:
                StrokeRenderer.Line(batch, stroke.Start, stroke.End, stroke.Thickness, color, style);
                break;
            case TargetSchematicStrokeKind.Arc when stroke.Pattern.Dash > 0f:
                StrokeRenderer.PatternedArcOutline(batch, stroke.Start, stroke.Radius, stroke.StartRadians, stroke.SweepRadians, stroke.Thickness, stroke.Pattern, color, style);
                break;
            case TargetSchematicStrokeKind.Arc:
                StrokeRenderer.ArcOutline(batch, stroke.Start, stroke.Radius, stroke.StartRadians, stroke.SweepRadians, stroke.Thickness, color, style);
                break;
            case TargetSchematicStrokeKind.FillDiamond:
                StrokeRenderer.FillDiamond(batch, stroke.Start, stroke.Radius, color, style);
                break;
            case TargetSchematicStrokeKind.DiamondOutline:
                StrokeRenderer.DiamondOutline(batch, stroke.Start, stroke.Radius, stroke.Thickness, color, style);
                break;
            case TargetSchematicStrokeKind.Hatch:
                StrokeRenderer.HatchRectangle(batch, stroke.Start, stroke.HatchSize, stroke.Radius, stroke.Thickness, color, style);
                break;
            case TargetSchematicStrokeKind.FillCircle:
                StrokeRenderer.FillCircle(batch, stroke.Start, stroke.Radius, color, style);
                break;
            case TargetSchematicStrokeKind.Arrowhead:
                StrokeRenderer.OpenArrowhead(batch, stroke.Start, stroke.StartRadians, stroke.Radius, stroke.SweepRadians, stroke.Thickness, color, style);
                break;
            case TargetSchematicStrokeKind.CentreMark:
                StrokeRenderer.CentreMark(batch, stroke.Start, stroke.Radius, stroke.Thickness, color, style);
                break;
            case TargetSchematicStrokeKind.BreakMark:
                StrokeRenderer.BreakMark(batch, stroke.Start, stroke.StartRadians, stroke.Radius, stroke.BreakSpacing, stroke.Thickness, color, style);
                break;
        }
    }
}
