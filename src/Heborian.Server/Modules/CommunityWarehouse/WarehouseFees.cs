using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
namespace Heborian.Server.Modules.CommunityWarehouse;

public sealed class WarehouseFeeOptions
{
    public bool Enabled { get; set; }
    public string Currency { get; set; } = "RUB";
    public int Deposit { get; set; } = 100;
    public int Withdrawal { get; set; } = 100;
    public void Validate()
    {
        if(Currency != "RUB" && Currency != "EUR" && Currency != "USD")
            throw new InvalidDataException("TransferFees.Currency must be RUB, EUR or USD.");
        if(Deposit<0 || Withdrawal<0) throw new InvalidDataException("Transfer fees must be non-negative whole numbers.");
    }
}
public sealed class WarehousePaidFee
{
    public string Currency { get; set; }
    public int Amount { get; set; }
}
public sealed partial class WarehouseModule
{
    private WarehouseFeeOptions fees=new();
    private static bool ManuallyLocked(Item item) => (int?)(item.Upd?.PinLockState)==2;
    private string FeeCurrencyTemplate => fees.Currency=="RUB" ? "5449016a4bdc2d6f028b456f"
        : fees.Currency=="EUR" ? "569668774bdc2da2298b4568" : "5696686a4bdc2da3298b456a";
    private bool InPersonalStash(Item item,Dictionary<string,Item> all,string stashId,bool unlockedOnly)
    {
        var seen=new HashSet<string>();
        while(item.ParentId!=null && seen.Add(item.Id.ToString()))
        {
            if(unlockedOnly && ManuallyLocked(item)) return false;
            if(item.ParentId==stashId) return item.SlotId=="hideout";
            if(!all.TryGetValue(item.ParentId,out item)) return false;
        }
        return false;
    }

    private long CurrencyBalance(PmcData pmc)
    {
        var all=pmc.Inventory.Items.ToDictionary(i=>i.Id.ToString());
        return pmc.Inventory.Items.Where(i=>i.Template.ToString()==FeeCurrencyTemplate
            && InPersonalStash(i,all,pmc.Inventory.Stash?.ToString(),false))
            .Sum(i=>Math.Max(0,checked((long)(i.Upd?.StackObjectsCount ?? 1))));
    }
    private List<WarehouseProfileChange> PrepareFee(PmcData pmc,WarehouseRequest request,WarehouseTransfer transfer,string direction)
    {
        var result=new List<WarehouseProfileChange>();
        int amount=!fees.Enabled ? 0 : direction=="deposit" ? fees.Deposit : direction=="withdraw" ? fees.Withdrawal : 0;
        if(amount==0) return result;
        string template=FeeCurrencyTemplate;
        var all=pmc.Inventory.Items.ToDictionary(i=>i.Id.ToString());
        var children=pmc.Inventory.Items.ToLookup(i=>i.ParentId);
        var excluded=new HashSet<string>(transfer.ProfileChanges.Select(c=>c.Id));
        // Do not pay out of transferred items or the receiving merge stack. This also
        // keeps fee deltas disjoint from transfer deltas and preserves their native ordering.
        foreach(var id in new[]{request.ItemId,request.TargetId})
            if(id!=null && all.TryGetValue(id,out var root))
                foreach(var child in ItemTree(root,pmc,children)) excluded.Add(child.Id.ToString());
        var candidates=pmc.Inventory.Items.Where(i=>i.Template.ToString()==template && !excluded.Contains(i.Id.ToString())
            && !children[i.Id.ToString()].Any() && InPersonalStash(i,all,pmc.Inventory.Stash?.ToString(),true)).OrderBy(i=>i.Id.ToString(),StringComparer.Ordinal).ToList();
        long available=candidates.Sum(i=>Math.Max(0,checked((long)(i.Upd?.StackObjectsCount ?? 1))));
        if(available<amount) throw new InvalidOperationException($"Transfer fee: {amount:N0} {fees.Currency} required; available {available:N0}. Transferred items and the receiving stack cannot pay the fee.");
        long remaining=amount;
        foreach(var item in candidates)
        {
            if(remaining==0) break;
            long count=checked((long)(item.Upd?.StackObjectsCount ?? 1));
            if(count<=0) continue;
            long take=Math.Min(count,remaining); remaining-=take;
            var after=json.Deserialize<Item>(json.Serialize(item))!;
            after.Upd ??= new(); after.Upd.StackObjectsCount=count-take;
            result.Add(new WarehouseProfileChange {Id=item.Id.ToString(),Before=json.Serialize(item),After=count==take ? null : json.Serialize(after)});
        }
        transfer.ProfileChanges.AddRange(result);
        if(transfer.Audit!=null) transfer.Audit.Fee=new WarehousePaidFee {Currency=fees.Currency,Amount=amount};
        return result;
    }
    private void FeeDelta(ItemEventRouterResponse output,MongoId session,IEnumerable<WarehouseProfileChange> changes)
    {
        foreach(var change in changes)
            if(change.After==null) output.ProfileChanges[session].Items.DeletedItems.Add(new DeletedItem {Id=change.Id});
            else output.ProfileChanges[session].Items.ChangedItems.Add(json.Deserialize<Item>(change.After)!);
    }
}
