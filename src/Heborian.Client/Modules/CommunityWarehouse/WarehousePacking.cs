using System;
using System.Collections.Generic;

namespace Heborian.Client.Modules.CommunityWarehouse
{
    public struct WarehousePosition { public int X,Y; }
    public static class WarehousePacking
    {
        // Native IntSlider instantiates count-1 notch objects. StepSlider caps this at 24.
        public static bool UseStepSplit(int count) => count>100;
        public static bool ValidSplitAmount(int amount,int count) => amount>=1 && amount<=count;
        public static bool CanMerge(WarehouseItem source,WarehouseItem target)
        {
            if(source.id == target.id || source.template != target.template || source.count < 1 || target.count < 1
                || target.maxStack <= 1 || target.count >= target.maxStack) return false;
            Newtonsoft.Json.Linq.JObject Normalize(Newtonsoft.Json.Linq.JObject value)
            {
                var state=value == null ? new Newtonsoft.Json.Linq.JObject() : (Newtonsoft.Json.Linq.JObject)value.DeepClone();
                state.Remove("StackObjectsCount");
                if(state["SpawnedInSession"] == null || state["SpawnedInSession"].Type == Newtonsoft.Json.Linq.JTokenType.Null
                    || state["SpawnedInSession"].ToString() == "False") state.Remove("SpawnedInSession");
                return state;
            }
            return Newtonsoft.Json.Linq.JToken.DeepEquals(Normalize(source.upd),Normalize(target.upd));
        }
        // Coordinates are local to the grid's top-left, with Y increasing downward.
        // Floor changes cells only at their borders, independently of the grab point.
        public static WarehousePosition DropPosition(double x,double y,double cell,int grabColumn,int grabRow)
        {
            return new WarehousePosition {X=(int)Math.Floor(x/cell)-grabColumn,Y=(int)Math.Floor(y/cell)-grabRow};
        }
        // A compact view of transferable items only; these are not personal stash coordinates.
        public static List<WarehousePosition> Pack(IReadOnlyList<WarehouseItem> items)
        {
            var result=new List<WarehousePosition>();
            var rows=new List<bool[]>();
            foreach(var item in items)
            {
                if(item.width<1 || item.width>8 || item.height<1 || item.height>8)
                    throw new InvalidOperationException("Unsupported item dimensions.");
                bool placed=false;
                for(int y=0;!placed;y++)
                {
                    while(rows.Count<y+item.height) rows.Add(new bool[8]);
                    for(int x=0;x<=8-item.width;x++)
                    {
                        bool fits=true;
                        for(int dy=0;dy<item.height;dy++) for(int dx=0;dx<item.width;dx++)
                            if(rows[y+dy][x+dx]) fits=false;
                        if(!fits) continue;
                        for(int dy=0;dy<item.height;dy++) for(int dx=0;dx<item.width;dx++) rows[y+dy][x+dx]=true;
                        result.Add(new WarehousePosition {X=x,Y=y}); placed=true; break;
                    }
                }
            }
            return result;
        }
        public static bool Fits(IReadOnlyList<WarehouseItem> items,int x,int y,int w,int h,string ignoreId=null,int rows=WarehouseDimensions.Height)
        {
            if(w<1 || h<1 || w>WarehouseDimensions.Width || h>rows || x<0 || y<0 || x>WarehouseDimensions.Width-w || y>rows-h) return false;
            foreach(var item in items)
                if((ignoreId == null || item.id != ignoreId) && x<item.x+item.width && x+w>item.x && y<item.y+item.height && y+h>item.y) return false;
            return true;
        }
    }
}
