using Newtonsoft.Json.Linq;

namespace Heborian.Client.Modules.CommunityWarehouse
{
    // Compare only the item represented by a native view. A server revision change
    // alone must not recreate every other view in the player's inventory.
    public static class WarehouseItemPresentation
    {
        public static bool Same(WarehouseItem a,WarehouseItem b)
        {
            if(ReferenceEquals(a,b)) return true;
            if(a==null || b==null) return false;
            return a.id==b.id && a.template==b.template && a.name==b.name
                && a.x==b.x && a.y==b.y && a.width==b.width && a.height==b.height
                && a.count==b.count && a.maxStack==b.maxStack && a.rotated==b.rotated
                && a.transferable==b.transferable && a.canReceive==b.canReceive
                && JToken.DeepEquals(a.upd,b.upd) && JToken.DeepEquals(a.tree,b.tree);
        }
    }
}
