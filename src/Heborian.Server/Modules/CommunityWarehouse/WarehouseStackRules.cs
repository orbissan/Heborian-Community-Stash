using System;
using System.Text.Json.Nodes;

namespace Heborian.Server.Modules.CommunityWarehouse;

public static class WarehouseStackRules
{
    public static bool Compatible(string first,string second)
    {
        JsonObject Normalize(string value)
        {
            var state=JsonNode.Parse(value) as JsonObject ?? new JsonObject();
            state.Remove("StackObjectsCount");
            if(state["SpawnedInSession"] == null || state["SpawnedInSession"]!.ToJsonString() == "false") state.Remove("SpawnedInSession");
            return state;
        }
        return JsonNode.DeepEquals(Normalize(first),Normalize(second));
    }
    public static int Amount(int source,int target,int maximum,int? requested)
    {
        if(source < 1 || target < 1 || maximum <= 1 || target >= maximum)
            throw new InvalidOperationException("Target stack is full or item cannot be stacked.");
        int amount=requested ?? source;
        if(amount < 1 || amount > source) throw new InvalidOperationException("Invalid merge amount.");
        return Math.Min(amount,maximum-target);
    }
}
