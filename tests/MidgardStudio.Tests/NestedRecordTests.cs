using System.Linq;
using MidgardStudio.Core.Model;
using MidgardStudio.Core.Schemas;
using MidgardStudio.Core.Serialization;

namespace MidgardStudio.Tests;

public class NestedRecordTests
{
    [Fact]
    public void Editing_nested_object_bubbles_dirty_to_parent()
    {
        var item = new DbRecord(ItemDbSchema.Instance);
        item.SetRaw("Id", 5000);
        item.SetRaw("AegisName", "X");
        item.SetRaw("Type", "Weapon");
        var flags = new DbRecord(ItemDbSchema.Flags);
        item.SetRaw("Flags", flags);
        item.AttachNestedOwners();
        item.IsDirty = false;
        flags.IsDirty = false;

        flags.Set("BindOnEquip", true);

        Assert.True(flags.IsDirty);
        Assert.True(item.IsDirty); // bubbled up via Owner so the import record gets saved
    }

    [Fact]
    public void Nested_object_round_trips_through_yaml()
    {
        var schema = ItemDbSchema.Instance;
        var item = new DbRecord(schema);
        item.SetRaw("Id", 5001);
        item.SetRaw("AegisName", "Y");
        item.SetRaw("Name", "Y");
        item.SetRaw("Type", "Armor");
        var trade = new DbRecord(ItemDbSchema.Trade);
        trade.SetRaw("NoDrop", true);
        trade.SetRaw("NoSell", true);
        item.SetRaw("Trade", trade);
        item.AttachNestedOwners();

        var file = new DbFile { HeaderType = "ITEM_DB", HeaderVersion = 3 };
        file.Records.Add(item);
        string yaml = new YamlDbWriter().WriteToString(schema, file);

        Assert.Contains("Trade:", yaml);
        Assert.Contains("NoDrop: true", yaml);

        var back = new YamlDbReader().Read(yaml, schema).Records.Single();
        Assert.True(back.GetObject("Trade")!.GetBool("NoDrop"));
        Assert.True(back.GetObject("Trade")!.GetBool("NoSell"));
    }
}
