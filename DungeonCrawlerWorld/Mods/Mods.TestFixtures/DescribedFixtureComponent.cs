using Engine.ECS.Properties;

namespace Mods.TestFixtures;

/// <summary>A mod's own described component: its table registers itself through the mod assembly's generated module initializer when ModuleLoader first uses the assembly.</summary>
[DescribedComponent]
public partial struct DescribedFixtureComponent
{
    public int Charges { get; set; }
}
