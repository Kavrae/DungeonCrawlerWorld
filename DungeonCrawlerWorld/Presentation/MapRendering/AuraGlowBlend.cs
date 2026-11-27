using Game.Views;
using Microsoft.Xna.Framework;

namespace Presentation.MapRendering;

/// <summary>The glow auras cast on a map cell: the colours of the auras reaching it, blended by each one's power there, and how opaque their glow is.</summary>
/// <remarks>
/// <para>
/// BeginPass reads the auras in the field, their colours and their glow opacity multipliers once, so a pass over many cells reads each
/// aura's definition once rather than once per cell. A pass starts whenever AuraView.Version changes, so an aura
/// whose first source has just gone in is in the next one.
/// </para>
/// <para>
/// The pulse sources reaching the pass's area -- the auras carried by whatever can move, which the field never holds -- are
/// laid out once per pass into a grid over the area, the strongest value per aura per cell, so a cell reads them as it
/// reads the field. An aura's power at a cell is the field's or a pulse source's, whichever is stronger (strongest wins).
/// </para>
/// </remarks>
public sealed class AuraGlowBlend(AuraView auraView)
{
    private readonly List<AuraInPass> _aurasInPass = [];
    private readonly Stack<int[]> _spareGrids = [];
    private bool[] _anyPulse = [];
    private int _minX;
    private int _minY;
    private int _width;
    private int _height;

    /// <summary>One aura in the pass: how it glows, whether the field holds it, and its pulse sources' strongest value per cell of the area, if any reach it.</summary>
    private readonly record struct AuraInPass(byte AuraId, Color GlowColor, float GlowOpacityMultiplier, bool InField, int[]? PulsePowers = null);

    /// <summary>Reads the field's auras only, with no area: for a pass over cells no pulse source can reach.</summary>
    public void BeginPass() => BeginPass(0, 0, 0, 0, []);

    /// <summary>Reads the auras that can reach anything, their colours and their glow opacity multipliers, and lays out the pulse sources reaching the area, for the TryGetGlow calls that follow.</summary>
    /// <param name="pulseSources">The pulse sources reaching the area (AuraView.CopyPulseSourcesReaching).</param>
    public void BeginPass(int minX, int minY, int width, int height, IReadOnlyList<PulseSourceReach> pulseSources)
    {
        foreach (var aura in _aurasInPass)
        {
            if (aura.PulsePowers is { } grid)
            {
                _spareGrids.Push(grid);
            }
        }

        _aurasInPass.Clear();
        _minX = minX;
        _minY = minY;
        _width = width;
        _height = height;

        foreach (var auraId in auraView.AuraIdsInField)
        {
            _aurasInPass.Add(new AuraInPass(auraId, auraView.GlowColorOf(auraId), Math.Clamp(auraView.GlowOpacityMultiplierOf(auraId), 0f, 1f), InField: true));
        }

        var cellCount = width * height;
        if (_anyPulse.Length < cellCount)
        {
            _anyPulse = new bool[cellCount];
        }
        else
        {
            Array.Clear(_anyPulse, 0, cellCount);
        }

        foreach (var source in pulseSources)
        {
            LayOut(source, GridOf(source.AuraId, cellCount));
        }
    }

    private int[] GridOf(byte auraId, int cellCount)
    {
        var index = 0;
        while (index < _aurasInPass.Count && _aurasInPass[index].AuraId != auraId)
        {
            index++;
        }

        if (index == _aurasInPass.Count)
        {
            _aurasInPass.Add(new AuraInPass(auraId, auraView.GlowColorOf(auraId), Math.Clamp(auraView.GlowOpacityMultiplierOf(auraId), 0f, 1f), InField: false));
        }

        if (_aurasInPass[index].PulsePowers is { } existing)
        {
            return existing;
        }

        var grid = _spareGrids.Count > 0 && _spareGrids.Peek().Length >= cellCount ? _spareGrids.Pop() : new int[cellCount];
        Array.Clear(grid, 0, cellCount);
        _aurasInPass[index] = _aurasInPass[index] with { PulsePowers = grid };
        return grid;
    }

    /// <summary>Raises each cell of the area the source reaches to its value there.</summary>
    private void LayOut(PulseSourceReach source, int[] grid)
    {
        var center = source.Center;
        for (var deltaY = -source.Size; deltaY <= source.Size; deltaY++)
        {
            var row = center.Y + deltaY - _minY;
            if (row < 0 || row >= _height)
            {
                continue;
            }

            var remaining = source.Size - Math.Abs(deltaY);
            var firstColumn = Math.Max(0, center.X - remaining - _minX);
            var lastColumn = Math.Min(_width - 1, center.X + remaining - _minX);
            for (var column = firstColumn; column <= lastColumn; column++)
            {
                var value = source.ValueAt(column + _minX, row + _minY);
                var index = row * _width + column;
                if (value > grid[index])
                {
                    grid[index] = value;
                    _anyPulse[index] = true;
                }
            }
        }
    }

    /// <summary>The glow at a cell: the colours of the auras reaching it and their glow opacity multipliers, each averaged by the aura's power there, and their summed power.</summary>
    /// <remarks>False where no aura reaches. Two auras overlapping -- from one source or several -- blend toward whichever is stronger at the cell.</remarks>
    public bool TryGetGlow(int x, int y, int mapLayer, out Color glowColor, out int totalPower, out float glowOpacityMultiplier)
    {
        glowColor = default;
        totalPower = 0;
        glowOpacityMultiplier = 0f;

        var column = x - _minX;
        var row = y - _minY;
        var index = column >= 0 && column < _width && row >= 0 && row < _height ? row * _width + column : -1;
        var fieldReaches = auraView.AnyFieldAuraReaches(x, y, mapLayer);
        if (!fieldReaches && (index < 0 || !_anyPulse[index]))
        {
            return false;
        }

        var red = 0f;
        var green = 0f;
        var blue = 0f;
        var opacityMultiplier = 0f;

        foreach (var aura in _aurasInPass)
        {
            var power = aura.InField && fieldReaches ? auraView.FieldPowerAt(x, y, mapLayer, aura.AuraId) : 0;
            if (index >= 0 && aura.PulsePowers is { } pulsePowers)
            {
                power = Math.Max(power, pulsePowers[index]);
            }

            if (power <= 0)
            {
                continue;
            }

            red += aura.GlowColor.R * power;
            green += aura.GlowColor.G * power;
            blue += aura.GlowColor.B * power;
            opacityMultiplier += aura.GlowOpacityMultiplier * power;
            totalPower += power;
        }

        if (totalPower <= 0)
        {
            return false;
        }

        glowColor = new Color((byte)(red / totalPower), (byte)(green / totalPower), (byte)(blue / totalPower));
        glowOpacityMultiplier = opacityMultiplier / totalPower;
        return true;
    }
}
