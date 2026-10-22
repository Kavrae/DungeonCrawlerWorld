using Engine.Content;
using Engine.Math;
using Game.Content;
using Game.Modules.Core.Components;

namespace Game.Areas;

/// <summary>Codecs for areas (a file each), palettes and fixed placements (a JSON Lines table each), and what they hold; registered by AreasModule.</summary>
/// <remarks>
/// Only the shape is read here: what a key or Guid names, whether rows fit the area and every symbol is defined are
/// checked once the build's terrain and blueprints are known (AreaContentValidation), so a failure there can name the
/// row and column it is at.
/// </remarks>
internal static class AreaContentCodecs
{
    public static void Register(ContentTypeDeclarations contentTypes)
    {
        contentTypes.RegisterValue(new AreaDefinitionCodec());
        contentTypes.RegisterValue(new AreaLayersCodec());
        contentTypes.RegisterValue(new AreaCellCodec());
        contentTypes.RegisterValue(new AreaSpawnCodec());
        contentTypes.RegisterValue(new AreaWeightedBlueprintCodec());
        contentTypes.RegisterValue(new AreaEntityPlacementCodec());
        contentTypes.RegisterValue(new AreaMarkerCodec());
        contentTypes.RegisterValue(new AreaPaletteCodec());
        contentTypes.RegisterValue(new AreaPlacementCodec());
    }

    private static void WriteSize(ContentWriter writer, Vector2Byte? size)
    {
        if (size is not { } present)
        {
            return;
        }

        writer.Integer("w", present.X);
        writer.Integer("h", present.Y);
    }

    private static Vector2Byte? ReadSize(ContentObject element, string typeName)
    {
        var width = element.OptionalInteger("w");
        var height = element.OptionalInteger("h");
        if (width is null && height is null)
        {
            return null;
        }

        if (width is not { } presentWidth || height is not { } presentHeight)
        {
            throw new ContentFormatException($"{typeName}'s size needs both \"w\" and \"h\".");
        }

        return new Vector2Byte(ContentNumbers.Byte(presentWidth, "w"), ContentNumbers.Byte(presentHeight, "h"));
    }

    private sealed class AreaDefinitionCodec : ContentCodec<AreaDefinition>
    {
        public override ContentSchema Schema { get; } = new(nameof(AreaDefinition),
        [
            ContentField.Guid(ContentTable.IdKey, "Id", isRequired: true),
            ContentField.Text("n", "Name", isRequired: true),
            ContentField.Text("d", "Description"),
            ContentField.TagSet("tg", "Tags"),
            new ContentField("w", "Width", ContentFieldKind.Integer, IsRequired: true, Minimum: 1, Maximum: ushort.MaxValue),
            new ContentField("h", "Height", ContentFieldKind.Integer, IsRequired: true, Minimum: 1, Maximum: ushort.MaxValue),
            new ContentField("pal", "Palettes", ContentFieldKind.List, typeof(Guid), ContentFieldKind.Reference, ReferencedTable: MasterContent.AreaPalettesTable),
            ContentField.ListOf<AreaCell>("leg", "Legend", ContentFieldKind.Object),
            ContentField.Object<AreaLayers>("lay", "Layers", isRequired: true),
            ContentField.ListOf<AreaEntityPlacement>("ent", "Entities", ContentFieldKind.Object),
            ContentField.ListOf<AreaMarker>("mk", "Markers", ContentFieldKind.Object),
        ]);

        public override void WriteProperties(ContentWriter writer, AreaDefinition value)
        {
            writer.Guid(ContentTable.IdKey, value.Id);
            writer.String("n", value.Name);
            writer.String("d", value.Description);
            writer.Tags("tg", value.Tags);
            writer.Integer("w", value.Width);
            writer.Integer("h", value.Height);
            writer.GuidList("pal", value.Palettes);
            writer.List("leg", value.Legend);
            writer.Object("lay", value.Layers);
            writer.List("ent", value.Entities);
            writer.List("mk", value.Markers);
        }

        public override AreaDefinition Read(ContentObject element) => new(
            element.Guid(ContentTable.IdKey),
            element.String("n"),
            element.String("d"),
            element.Tags("tg"),
            ContentNumbers.Int32(element.Integer("w"), "w"),
            ContentNumbers.Int32(element.Integer("h"), "h"),
            element.Object<AreaLayers>("lay")!,
            element.List<AreaCell>("leg"),
            element.GuidList("pal"),
            element.List<AreaEntityPlacement>("ent"),
            element.List<AreaMarker>("mk"));
    }

    /// <summary>Keyed by the layer's name, so a file reads "Ground": [rows].</summary>
    private sealed class AreaLayersCodec : ContentCodec<AreaLayers>
    {
        private const string UnderGroundKey = nameof(MapLayer.UnderGround);
        private const string GroundKey = nameof(MapLayer.Ground);
        private const string FlyingKey = nameof(MapLayer.Flying);

        public override ContentSchema Schema { get; } = new(nameof(AreaLayers),
        [
            ContentField.ListOf<string>(UnderGroundKey, "UnderGround rows", ContentFieldKind.Text),
            new ContentField(GroundKey, "Ground rows", ContentFieldKind.List, typeof(string), ContentFieldKind.Text, IsRequired: true),
            ContentField.ListOf<string>(FlyingKey, "Flying rows", ContentFieldKind.Text),
        ]);

        public override void WriteProperties(ContentWriter writer, AreaLayers value)
        {
            writer.StringList(UnderGroundKey, value.UnderGround ?? []);
            writer.StringList(GroundKey, value.Ground);
            writer.StringList(FlyingKey, value.Flying ?? []);
        }

        public override AreaLayers Read(ContentObject element) => new(
            element.StringList(GroundKey),
            OptionalRows(element, UnderGroundKey),
            OptionalRows(element, FlyingKey));

        private static IReadOnlyList<string>? OptionalRows(ContentObject element, string key)
        {
            var rows = element.StringList(key);
            return rows.Count == 0 ? null : rows;
        }
    }

    private sealed class AreaCellCodec : ContentCodec<AreaCell>
    {
        public override ContentSchema Schema { get; } = new(nameof(AreaCell),
        [
            ContentField.Text("s", "Symbol", isRequired: true),
            ContentField.Text("ter", "Floor terrain"),
            ContentField.Text("st", "Wall"),
            ContentField.Boolean("clr", "Clears the wall"),
            ContentField.ListOf<AreaSpawn>("sp", "Spawns", ContentFieldKind.Object),
        ]);

        public override void WriteProperties(ContentWriter writer, AreaCell value)
        {
            writer.String("s", value.Symbol.ToString());
            writer.OptionalString("ter", value.Terrain);
            writer.OptionalString("st", value.Structure);
            writer.Boolean("clr", value.ClearsStructure);
            writer.List("sp", value.SpawnList);
        }

        public override AreaCell Read(ContentObject element)
        {
            var symbol = element.String("s");
            if (symbol.Length != 1)
            {
                throw new ContentFormatException($"A legend symbol is one character; \"{symbol}\" isn't.");
            }

            var spawns = element.List<AreaSpawn>("sp");
            return new AreaCell(symbol[0], element.OptionalString("ter"), element.OptionalString("st"), element.Boolean("clr"), spawns.Count == 0 ? null : spawns);
        }
    }

    private sealed class AreaSpawnCodec : ContentCodec<AreaSpawn>
    {
        public override ContentSchema Schema { get; } = new(nameof(AreaSpawn),
        [
            ContentField.Guid("bp", "Blueprint"),
            ContentField.Integer("w", "Width", 1, byte.MaxValue),
            ContentField.Integer("h", "Height", 1, byte.MaxValue),
            ContentField.Integer("ch", "Chance (percent)", 1, 100),
            ContentField.ListOf<AreaWeightedBlueprint>("of", "One of", ContentFieldKind.Object),
        ]);

        public override void WriteProperties(ContentWriter writer, AreaSpawn value)
        {
            writer.OptionalGuid("bp", value.Blueprint);
            WriteSize(writer, value.Size);
            writer.Integer("ch", value.Chance, AreaSpawn.AlwaysChance);
            writer.List("of", value.OneOfList);
        }

        public override AreaSpawn Read(ContentObject element)
        {
            var oneOf = element.List<AreaWeightedBlueprint>("of");
            return new AreaSpawn(
                element.OptionalGuid("bp"),
                ReadSize(element, nameof(AreaSpawn)),
                ContentNumbers.Byte(element.Integer("ch", AreaSpawn.AlwaysChance), "ch"),
                oneOf.Count == 0 ? null : oneOf);
        }
    }

    private sealed class AreaWeightedBlueprintCodec : ContentCodec<AreaWeightedBlueprint>
    {
        public override ContentSchema Schema { get; } = new(nameof(AreaWeightedBlueprint),
        [
            ContentField.Guid("bp", "Blueprint", isRequired: true),
            ContentField.Integer("wt", "Weight", 1, int.MaxValue),
        ]);

        public override void WriteProperties(ContentWriter writer, AreaWeightedBlueprint value)
        {
            writer.Guid("bp", value.Blueprint);
            writer.Integer("wt", value.Weight, AreaWeightedBlueprint.DefaultWeight);
        }

        public override AreaWeightedBlueprint Read(ContentObject element) => new(
            element.Guid("bp"),
            ContentNumbers.Int32(element.Integer("wt", AreaWeightedBlueprint.DefaultWeight), "wt"));
    }

    private sealed class AreaEntityPlacementCodec : ContentCodec<AreaEntityPlacement>
    {
        public override ContentSchema Schema { get; } = new(nameof(AreaEntityPlacement),
        [
            ContentField.Text("pid", "Placement id", isRequired: true),
            ContentField.Integer("x", "X"),
            ContentField.Integer("y", "Y"),
            ContentField.Enum<MapLayer>("l", "Layer"),
            ContentField.Guid("bp", "Blueprint", isRequired: true),
            ContentField.Integer("w", "Width", 1, byte.MaxValue),
            ContentField.Integer("h", "Height", 1, byte.MaxValue),
            ContentField.Integer("ch", "Chance (percent)", 1, 100),
            ContentField.Boolean("u", "Unique"),
            ContentField.Guid("fb", "Fallback blueprint"),
            ContentField.Text("d", "Description"),
        ]);

        public override void WriteProperties(ContentWriter writer, AreaEntityPlacement value)
        {
            writer.String("pid", value.PlacementId);
            writer.Integer("x", value.X);
            writer.Integer("y", value.Y);
            writer.Enum("l", value.Layer, MapLayer.Ground);
            writer.Guid("bp", value.Blueprint);
            WriteSize(writer, value.Size);
            writer.Integer("ch", value.Chance, AreaSpawn.AlwaysChance);
            writer.Boolean("u", value.Unique);
            writer.OptionalGuid("fb", value.Fallback);
            writer.OptionalString("d", value.Description);
        }

        public override AreaEntityPlacement Read(ContentObject element) => new(
            element.String("pid"),
            ContentNumbers.Int32(element.Integer("x"), "x"),
            ContentNumbers.Int32(element.Integer("y"), "y"),
            element.Guid("bp"),
            element.Enum("l", MapLayer.Ground),
            ReadSize(element, nameof(AreaEntityPlacement)),
            ContentNumbers.Byte(element.Integer("ch", AreaSpawn.AlwaysChance), "ch"),
            element.Boolean("u"),
            element.OptionalGuid("fb"),
            element.OptionalString("d"));
    }

    private sealed class AreaMarkerCodec : ContentCodec<AreaMarker>
    {
        public override ContentSchema Schema { get; } = new(nameof(AreaMarker),
        [
            ContentField.Text("mid", "Marker id", isRequired: true),
            ContentField.Integer("x", "X"),
            ContentField.Integer("y", "Y"),
            ContentField.Enum<MapLayer>("l", "Layer"),
            new ContentField("k", "Kind", ContentFieldKind.Enum, typeof(AreaMarkerKind), IsRequired: true),
        ]);

        public override void WriteProperties(ContentWriter writer, AreaMarker value)
        {
            writer.String("mid", value.MarkerId);
            writer.Integer("x", value.X);
            writer.Integer("y", value.Y);
            writer.Enum("l", value.Layer, MapLayer.Ground);
            writer.Enum("k", value.Kind, AreaMarkerKind.None);
        }

        public override AreaMarker Read(ContentObject element) => new(
            element.String("mid"),
            ContentNumbers.Int32(element.Integer("x"), "x"),
            ContentNumbers.Int32(element.Integer("y"), "y"),
            element.Enum("k", AreaMarkerKind.None),
            element.Enum("l", MapLayer.Ground));
    }

    private sealed class AreaPaletteCodec : ContentCodec<AreaPalette>
    {
        public override ContentSchema Schema { get; } = new(nameof(AreaPalette),
        [
            ContentField.Guid(ContentTable.IdKey, "Id", isRequired: true),
            ContentField.Text("n", "Name", isRequired: true),
            ContentField.ListOf<AreaCell>("leg", "Legend", ContentFieldKind.Object),
        ]);

        public override void WriteProperties(ContentWriter writer, AreaPalette value)
        {
            writer.Guid(ContentTable.IdKey, value.Id);
            writer.String("n", value.Name);
            writer.List("leg", value.Legend);
        }

        public override AreaPalette Read(ContentObject element) => new(
            element.Guid(ContentTable.IdKey),
            element.String("n"),
            element.List<AreaCell>("leg"));
    }

    private sealed class AreaPlacementCodec : ContentCodec<AreaPlacement>
    {
        public override ContentSchema Schema { get; } = new(nameof(AreaPlacement),
        [
            ContentField.Guid(ContentTable.IdKey, "Id", isRequired: true),
            ContentField.Reference("ar", "Area", MasterContent.AreasTable, isRequired: true),
            ContentField.Integer("nx", "Neighborhood x"),
            ContentField.Integer("ny", "Neighborhood y"),
            ContentField.Integer("ox", "Offset x", 0, Game.World.Neighborhoods.SizeTiles - 1),
            ContentField.Integer("oy", "Offset y", 0, Game.World.Neighborhoods.SizeTiles - 1),
        ]);

        public override void WriteProperties(ContentWriter writer, AreaPlacement value)
        {
            writer.Guid(ContentTable.IdKey, value.Id);
            writer.Guid("ar", value.Area);
            writer.Integer("nx", value.NeighborhoodX);
            writer.Integer("ny", value.NeighborhoodY);
            writer.Integer("ox", value.OffsetX);
            writer.Integer("oy", value.OffsetY);
        }

        public override AreaPlacement Read(ContentObject element) => new(
            element.Guid(ContentTable.IdKey),
            element.Guid("ar"),
            ContentNumbers.Int32(element.Integer("nx"), "nx"),
            ContentNumbers.Int32(element.Integer("ny"), "ny"),
            ContentNumbers.Int32(element.Integer("ox"), "ox"),
            ContentNumbers.Int32(element.Integer("oy"), "oy"));
    }
}
