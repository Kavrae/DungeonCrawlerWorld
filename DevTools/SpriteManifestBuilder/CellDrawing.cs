using System.Drawing.Drawing2D;

namespace SpriteManifestBuilder;

/// <summary>Draws one manifest cell's pixels into a destination rectangle, pixel-sharp, with its flip, rotation and tint applied (the tint multiplied in, as the game's SpriteBatch does), or a placeholder when it has none.</summary>
/// <remarks>Each Graphics transform call applies to drawn points before the earlier ones, so the calls below flip, then rotate, then move into place -- the order SpriteBatch applies them in.</remarks>
public static class CellDrawing
{
    public static void Draw(Graphics graphics, SheetImageCache sheets, ManifestCell? cell, RectangleF destination, float opacity = 1f)
    {
        if (cell is null || sheets.Get(cell.SheetPath) is not { } sheet)
        {
            DrawPlaceholder(graphics, destination);
            return;
        }

        var state = graphics.Save();
        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        graphics.PixelOffsetMode = PixelOffsetMode.Half;

        graphics.TranslateTransform(destination.X + destination.Width / 2, destination.Y + destination.Height / 2);
        graphics.RotateTransform(cell.Rotation);
        graphics.ScaleTransform(cell.FlipHorizontal ? -1 : 1, cell.FlipVertical ? -1 : 1);

        var isQuarterTurn = cell.Rotation is 90 or 270;
        var width = isQuarterTurn ? destination.Height : destination.Width;
        var height = isQuarterTurn ? destination.Width : destination.Height;
        var target = new RectangleF(-width / 2, -height / 2, width, height);
        var tint = cell.TintColor ?? Color.White;
        if (opacity >= 1f && cell.TintColor is null)
        {
            graphics.DrawImage(sheet, target, cell.SourceRectangle, GraphicsUnit.Pixel);
        }
        else
        {
            using var attributes = new System.Drawing.Imaging.ImageAttributes();
            attributes.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix
            {
                Matrix00 = tint.R / 255f,
                Matrix11 = tint.G / 255f,
                Matrix22 = tint.B / 255f,
                Matrix33 = tint.A / 255f * opacity,
            });
            PointF[] corners = [new(target.Left, target.Top), new(target.Right, target.Top), new(target.Left, target.Bottom)];
            graphics.DrawImage(sheet, corners, cell.SourceRectangle, GraphicsUnit.Pixel, attributes);
        }

        graphics.Restore(state);
    }

    /// <summary>The largest rectangle with source's aspect ratio centred in bounds.</summary>
    public static RectangleF Fit(Size source, RectangleF bounds)
    {
        var scale = Math.Min(bounds.Width / source.Width, bounds.Height / source.Height);
        var width = source.Width * scale;
        var height = source.Height * scale;
        return new RectangleF(bounds.X + (bounds.Width - width) / 2, bounds.Y + (bounds.Height - height) / 2, width, height);
    }

    private static void DrawPlaceholder(Graphics graphics, RectangleF destination)
    {
        using var pen = new Pen(Color.Magenta, 1);
        graphics.DrawRectangle(pen, destination.X, destination.Y, destination.Width - 1, destination.Height - 1);
        graphics.DrawLine(pen, destination.Left, destination.Top, destination.Right - 1, destination.Bottom - 1);
        graphics.DrawLine(pen, destination.Right - 1, destination.Top, destination.Left, destination.Bottom - 1);
    }
}
