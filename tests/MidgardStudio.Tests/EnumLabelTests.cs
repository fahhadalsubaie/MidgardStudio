using MidgardStudio.Core.Schema;
using MidgardStudio.Core.Schemas;

namespace MidgardStudio.Tests;

public class EnumLabelTests
{
    [Fact]
    public void Labeled_enum_keeps_raw_values_and_order()
    {
        var src = EnumSource.Labeled("X", ("Head_Low", "Lower Headgear"), ("Armor", "Armor"));

        Assert.Equal(new[] { "Head_Low", "Armor" }, src.Values);
        Assert.Equal("Lower Headgear", src.Label("Head_Low"));
        // Unknown values fall back to the raw value (so serialization stays raw).
        Assert.Equal("Unmapped", src.Label("Unmapped"));
    }

    [Fact]
    public void Item_locations_and_classes_have_friendly_labels()
    {
        // The UI shows these labels; the YAML still uses the raw value.
        Assert.Equal("Lower Headgear", ItemEnums.Locations.Label("Head_Low"));
        Assert.Equal("Upper Headgear", ItemEnums.Locations.Label("Head_Top"));
        Assert.Equal("Transcendent", ItemEnums.Classes.Label("Upper"));
        Assert.Equal("Fourth Class", ItemEnums.Classes.Label("Fourth"));

        // Every raw value is still present and label-resolvable.
        foreach (var v in ItemEnums.Locations.Values)
            Assert.False(string.IsNullOrEmpty(ItemEnums.Locations.Label(v)));
    }
}
