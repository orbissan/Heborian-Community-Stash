using System;
using System.Text.Json;

namespace Heborian.Server.Modules.CommunityWarehouse;

public sealed class WarehouseCondition
{
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string Icon { get; set; }
    public double Current { get; set; }
    public double Maximum { get; set; }

    // Capture values at transfer time, never reconstruct historical state from today's inventory.
    public static WarehouseCondition Read(string itemJson,string propertiesJson)
    {
        using var item=JsonDocument.Parse(itemJson);
        using var props=JsonDocument.Parse(propertiesJson);
        var root=props.RootElement;
        JsonElement Find(JsonElement e,string key)
        {
            if(e.ValueKind==JsonValueKind.Object)
                foreach(var p in e.EnumerateObject())
                    if(string.Equals(p.Name,key,StringComparison.OrdinalIgnoreCase)) return p.Value;
            return default;
        }
        double? Number(JsonElement e) => e.ValueKind==JsonValueKind.Number && e.TryGetDouble(out var n) && double.IsFinite(n) ? n : null;
        var upd=Find(item.RootElement,"upd");
        foreach(var spec in new[] {
            ("MedKit","HpResource","MaxHpResource"),
            ("Key","NumberOfUsages","MaximumNumberOfUsage"),
            ("RepairKit","Resource","MaxRepairResource"),
            ("FoodDrink","HpPercent","MaxResource"),
            ("Resource","Value","Resource"),
            ("Repairable","Durability","MaxDurability") })
        {
            var maximum=Number(Find(root,spec.Item3));
            if(maximum is not >0) continue;
            var state=Find(upd,spec.Item1);
            if(spec.Item1=="Repairable") maximum=Number(Find(state,"MaxDurability")) ?? maximum;
            var current=Number(Find(state,spec.Item2)) ?? (spec.Item1=="Key" ? 0 : maximum);
            if(spec.Item1=="Key") current=maximum-current;
            string icon=spec.Item1 switch { "MedKit"=>"HpResource", "FoodDrink"=>"FoodResource",
                "Resource"=>"FuelResource", "RepairKit"=>"RepairResource", _=>"Durability" };
            return new WarehouseCondition {Icon=icon,Current=Math.Max(0,current.Value),Maximum=maximum.Value};
        }
        return null;
    }
}
