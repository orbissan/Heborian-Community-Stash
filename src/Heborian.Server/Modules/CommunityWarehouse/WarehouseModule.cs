using System;
using System.IO;
using Path = System.IO.Path;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Heborian.Server.Configuration;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Common.Request;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Utils;

namespace Heborian.Server.Modules.CommunityWarehouse;

public sealed class WarehouseOptions
{
    public bool Enabled { get; set; } = true;
    public int Rows { get; set; } = WarehouseDimensions.Height;
    public WarehouseCategories AllowedDeposits { get; set; } = new();
}
public sealed class WarehouseCategories
{
    public bool Weapons { get; set; }
    public bool Gear { get; set; }
    public bool Tools { get; set; } = true;
    public bool Food { get; set; } = true;
    public bool Medicine { get; set; } = true;
    public bool Ammo { get; set; } = true;
    public bool Barter { get; set; } = true;
    public bool WeaponMods { get; set; }
    public bool Keys { get; set; }
    public bool Money { get; set; }
    public bool Containers { get; set; }
    public bool Other { get; set; }
}
public record WarehouseRequest : BaseInteractionRequestData
{
    [JsonPropertyName("requestId")] public string RequestId { get; set; } = "";
    [JsonPropertyName("direction")] public string Direction { get; set; } = "";
    [JsonPropertyName("itemId")] public string ItemId { get; set; } = "";
    [JsonPropertyName("revision")] public long Revision { get; set; }
    [JsonPropertyName("x")] public int? X { get; set; }
    [JsonPropertyName("y")] public int? Y { get; set; }
    [JsonPropertyName("rotated")] public bool? Rotated { get; set; }
    [JsonPropertyName("amount")] public int? Amount { get; set; }
    [JsonPropertyName("targetId")] public string TargetId { get; set; }
    [JsonPropertyName("targetPersonal")] public bool TargetPersonal { get; set; }
}

[Injectable(InjectionType.Singleton)]
public sealed class WarehouseModule : IServerModule
{
    private readonly SaveServer saves;
    private readonly ItemHelper items;
    private readonly InventoryHelper inventory;
    private readonly JsonUtil json;
    private readonly HttpResponseUtil responses;
    private readonly SemaphoreSlim gate = new(1,1);
    private WarehouseStore store;
    private bool ready;
    private WarehouseCategories allowed = new();
    public string Id => "CommunityWarehouse";
    public bool IsEnabled(ServerConfiguration config) => config.CommunityWarehouse?.Enabled == true;
    public WarehouseModule(SaveServer saves, ItemHelper items, InventoryHelper inventory, JsonUtil json, HttpResponseUtil responses)
    {
        this.saves = saves; this.items = items; this.inventory = inventory; this.json = json; this.responses = responses;
        store = new WarehouseStore(Path.Combine(Path.GetDirectoryName(typeof(WarehouseModule).Assembly.Location)!, "data", "warehouse.json"));
    }
    public async Task InitializeAsync(ServerConfiguration config, CancellationToken cancellationToken)
    {
        if(!IsEnabled(config)) return;
        allowed=config.CommunityWarehouse.AllowedDeposits ?? throw new InvalidDataException("AllowedDeposits must be an object.");
        store=new WarehouseStore(Path.Combine(Path.GetDirectoryName(typeof(WarehouseModule).Assembly.Location)!, "data", "warehouse.json"),config.CommunityWarehouse.Rows);
        store.Load();
        if(store.State.Pending != null)
        {
            await ApplyProfile(store.State.Pending, true);
            store.Commit();
            Console.WriteLine("[CommunityWarehouse] Recovered pending transfer.");
        }
        ready = true;
    }
    private PmcData Profile(MongoId session)
    {
        if(!saves.ProfileExists(session)) throw new InvalidOperationException("Unknown session.");
        var profile = saves.GetProfile(session);
        if(profile.CharacterData?.PmcData?.Inventory?.Items == null) throw new InvalidOperationException("PMC inventory unavailable.");
        // Raid outcomes replace inventory; never allow a transfer during a raid.
        var location = profile.InraidData?.Location;
        if(!string.IsNullOrEmpty(location) && location != "none") throw new InvalidOperationException("Warehouse is unavailable during a raid.");
        return profile.CharacterData.PmcData;
    }
    private WarehouseEntry Describe(Item item)
    {
        var template = items.GetItem(item.Template).Value;
        return new WarehouseEntry { Id = item.Id.ToString(), Template = item.Template.ToString(), Name = items.GetItemName(item.Template),
            Width = template.Properties.Width ?? 1, Height = template.Properties.Height ?? 1,
            Count = (int)(item.Upd?.StackObjectsCount ?? 1), Json = json.Serialize(item) };
    }
    private List<Item> ItemTree(Item root,PmcData pmc)
    {
        var tree=new List<Item> {root};
        var ids=new HashSet<string> {root.Id.ToString()};
        for(int i=0;i<tree.Count;i++)
            foreach(var child in pmc.Inventory.Items.Where(x => x.ParentId == tree[i].Id.ToString()))
                if(ids.Add(child.Id.ToString())) tree.Add(child);
        return tree;
    }
    private bool CategoryAllowed(Item item)
    {
        bool Is(params MongoId[] types) => items.IsOfBaseclasses(item.Template,types);
        if(Is(BaseClasses.WEAPON)) return allowed.Weapons;
        if(Is(BaseClasses.TOOL,BaseClasses.MULTITOOLS)) return allowed.Tools;
        if(Is(BaseClasses.FOOD_DRINK)) return allowed.Food;
        if(Is(BaseClasses.MEDS)) return allowed.Medicine;
        if(Is(BaseClasses.AMMO,BaseClasses.AMMO_BOX)) return allowed.Ammo;
        if(Is(BaseClasses.KEY)) return allowed.Keys;
        if(Is(BaseClasses.MONEY)) return allowed.Money;
        if(Is(BaseClasses.ARMOR,BaseClasses.ARMOR_PLATE,BaseClasses.ARMORED_EQUIPMENT,
            BaseClasses.BACKPACK,BaseClasses.VEST,BaseClasses.HEADWEAR,BaseClasses.FACE_COVER,BaseClasses.HEADPHONES)) return allowed.Gear;
        if(Is(BaseClasses.MOD)) return allowed.WeaponMods;
        if(Is(BaseClasses.SIMPLE_CONTAINER,BaseClasses.LOCKABLE_CONTAINER)) return allowed.Containers;
        if(Is(BaseClasses.BARTER_ITEM)) return allowed.Barter;
        return allowed.Other;
    }
    private bool PersonalStack(Item item,PmcData pmc) =>
        item.ParentId == pmc.Inventory.Stash?.ToString() && item.SlotId == "hideout"
        && !pmc.Inventory.Items.Any(x => x.ParentId == item.Id.ToString())
        && !(pmc.InsuredItems?.Any(x => x.ItemId == item.Id) ?? false)
        && items.GetItem(item.Template).Value.Properties.QuestItem != true;
    private bool Eligible(Item item,PmcData pmc)
    {
        if(item.ParentId != pmc.Inventory.Stash?.ToString() || item.SlotId != "hideout") return false;
        // Check every descendant too: containers cannot bypass a disabled category.
        return ItemTree(item,pmc).All(x => CategoryAllowed(x)
            && items.GetItem(x.Template).Value.Properties.QuestItem != true
            && !(pmc.InsuredItems?.Any(i => i.ItemId == x.Id) ?? false));
    }
    public async ValueTask<string> Read(MongoId session)
    {
        await gate.WaitAsync();
        try
        {
            if(!ready) return json.Serialize(new { error = "Warehouse unavailable. Check server log." });
            var pmc = Profile(session);
            // Reuse SPT sizing, including stash upgrades and extra-row bonuses.
            var stashSize=((int,int))typeof(InventoryHelper).GetMethod("GetPlayerStashSize",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(inventory,new object[] {pmc})!;
            return json.Serialize(new { revision = store.State.Revision, width = WarehouseDimensions.Width, height = store.Rows,
                personalWidth=stashSize.Item1, personalHeight=stashSize.Item2,
                items = store.State.Items.Where(store.Visible).Select(PublicItem),
                blocked = store.State.Items.Where(x => !store.Visible(x) && x.Y<store.Rows).Select(x => new {x=x.X,y=x.Y,width=x.Width,height=store.Rows-x.Y}),
                personal = pmc.Inventory.Items.Where(x => x.ParentId == pmc.Inventory.Stash?.ToString() && x.SlotId == "hideout").Select(x => PersonalItem(x,pmc)), error = "" });
        }
        catch(Exception error) { return json.Serialize(new { error = error.Message }); }
        finally { gate.Release(); }
    }
    private object PersonalItem(Item item,PmcData pmc)
    {
        var location=item.Location as ItemLocation ?? json.Deserialize<ItemLocation>(json.Serialize(item.Location))
            ?? throw new InvalidOperationException("Stash item location unavailable.");
        var size=inventory.GetItemSize(item.Template,item.Id,pmc.Inventory.Items);
        bool rotated=location.R == ItemRotation.Vertical;
        var tree=new List<Item> {item};
        var ids=new HashSet<string> {item.Id.ToString()};
        for(int i=0;i<tree.Count;i++)
            foreach(var child in pmc.Inventory.Items.Where(x => x.ParentId == tree[i].Id.ToString()))
                if(ids.Add(child.Id.ToString())) tree.Add(child);
        return new { id=item.Id.ToString(), template=item.Template.ToString(), name=items.GetItemName(item.Template),
            x=location.X ?? 0, y=location.Y ?? 0, width=rotated ? size.Item2 : size.Item1,
            height=rotated ? size.Item1 : size.Item2, rotated, count=(int)(item.Upd?.StackObjectsCount ?? 1),
            maxStack=items.GetItem(item.Template).Value.Properties.StackMaxSize ?? 1,
            transferable=Eligible(item,pmc), canReceive=PersonalStack(item,pmc), upd=item.Upd, tree };
    }
    private object PublicItem(WarehouseEntry entry) => new { id=entry.Id, template=entry.Template, name=entry.Name,
        x=entry.X, y=entry.Y, width=entry.Width, height=entry.Height, count=entry.Count, rotated=entry.Rotated,
        maxStack=items.GetItem(entry.Template).Value.Properties.StackMaxSize ?? 1,
        upd=json.Deserialize<Item>(entry.Json)!.Upd, tree=StoredTree(entry) };

    private List<Item> StoredTree(WarehouseEntry entry) =>
        new[] {entry.Json}.Concat(entry.ChildrenJson ?? new List<string>()).Select(x => json.Deserialize<Item>(x)!).ToList();

    public async ValueTask<ItemEventRouterResponse> Transfer(PmcData pmc, WarehouseRequest request, MongoId session, ItemEventRouterResponse output)
    {
        await gate.WaitAsync();
        bool journalStarted = false;
        try
        {
            if(!ready) throw new InvalidOperationException("Warehouse unavailable. Check server log.");
            if(!ReferenceEquals(Profile(session), pmc)) throw new InvalidOperationException("PMC profile required.");
            if(!Guid.TryParseExact(request.RequestId, "N", out _)) throw new InvalidOperationException("Invalid request ID.");
            if(store.State.History.Any(x => x.RequestId == request.RequestId)) throw new InvalidOperationException("This request was already committed. Refresh your inventory.");
            if(store.State.Revision != request.Revision) throw new InvalidOperationException("Warehouse changed. Refresh and try again.");
            if(request.Direction != "deposit" && request.Direction != "withdraw" && request.Direction != "move") throw new InvalidOperationException("Invalid direction.");
            // Hidden rows are retained, but cannot be addressed through stale or crafted requests.
            if(request.Direction != "deposit" && !store.State.Items.Any(x => x.Id == request.ItemId && store.Visible(x)))
                throw new InvalidOperationException("Item is outside the active warehouse rows.");
            if(!string.IsNullOrEmpty(request.TargetId) && !request.TargetPersonal
                && !store.State.Items.Any(x => x.Id == request.TargetId && store.Visible(x)))
                throw new InvalidOperationException("Target is outside the active warehouse rows.");
            if(!string.IsNullOrEmpty(request.TargetId)) return await Merge(pmc,request,session,output);
            if(request.X.HasValue != request.Y.HasValue || (request.Direction == "withdraw" && request.X.HasValue)
                || (request.Direction == "move" && !request.X.HasValue))
                throw new InvalidOperationException("Invalid target position.");
            if(request.Amount.HasValue)
            {
                var source=request.Direction == "deposit"
                    ? pmc.Inventory.Items.SingleOrDefault(i => i.Id.ToString() == request.ItemId)
                    : store.State.Items.Where(i => i.Id == request.ItemId).Select(i => json.Deserialize<Item>(i.Json)).SingleOrDefault();
                if(source == null) throw new InvalidOperationException("Source item is no longer available.");
                int available=checked((int)(source.Upd?.StackObjectsCount ?? 1));
                if(request.Amount.Value < 1 || request.Amount.Value > available)
                    throw new InvalidOperationException("Invalid stack amount. Refresh and try again.");
                if(request.Amount.Value < available)
                    return await TransferPartial(pmc,request,session,output,source,available);
            }
            if(request.Direction == "move")
            {
                bool moved;
                try { moved=store.Reposition(request.ItemId,request.X!.Value,request.Y!.Value,request.RequestId,session.ToString(),request.Rotated); }
                catch(Exception error)
                {
                    ready=false;
                    Console.WriteLine("[CommunityWarehouse] Position save failed; restart to reload durable state: " + error);
                    throw;
                }
                if(!moved) throw new InvalidOperationException("Item missing or target position occupied. Refresh and try again.");
                Console.WriteLine($"[CommunityWarehouse] move {request.ItemId} by {session}, revision {store.State.Revision}");
                return output; // No personal inventory delta for a warehouse-only move.
            }
            WarehouseEntry entry;
            Item profileItem;
            if(request.Direction == "deposit")
            {
                profileItem = pmc.Inventory.Items.SingleOrDefault(x => x.Id.ToString() == request.ItemId)
                    ?? throw new InvalidOperationException("Item is no longer in your inventory.");
                if(!Eligible(profileItem, pmc)) throw new InvalidOperationException("This item or its contents cannot be deposited under the server rules.");
                if(store.State.Items.Any(x => x.Id == request.ItemId)) throw new InvalidOperationException("This item ID already exists in the warehouse.");
                entry = Describe(profileItem);
                var tree=ItemTree(profileItem,pmc);
                entry.ChildrenJson=tree.Skip(1).Select(x => json.Serialize(x)).ToList();
                var size=inventory.GetItemSize(profileItem.Template,profileItem.Id,pmc.Inventory.Items);
                entry.Width=size.Item1; entry.Height=size.Item2;
                if(entry.Width != entry.Height && request.Rotated == true)
                {
                    (entry.Width,entry.Height)=(entry.Height,entry.Width);
                    entry.Rotated=true;
                }
                if(request.X.HasValue)
                {
                    if(!store.PlaceAt(entry,request.X.Value,request.Y!.Value))
                        throw new InvalidOperationException("Item does not fit at that position. Refresh and try again.");
                }
                else if(!store.Place(entry)) throw new InvalidOperationException("Community warehouse is full.");
            }
            else
            {
                entry = store.State.Items.SingleOrDefault(x => x.Id == request.ItemId)
                    ?? throw new InvalidOperationException("Item has already been taken.");
                if(pmc.Inventory.Items.Any(x => x.Id.ToString() == entry.Id)) throw new InvalidOperationException("Duplicate item ID in profile.");
                profileItem = json.Deserialize<Item>(entry.Json)!;
                var stash = pmc.Inventory.Items.Single(x => x.Id == pmc.Inventory.Stash);
                var grid = items.GetItem(stash.Template).Value.Properties.Grids.First().Properties;
                var map = inventory.GetContainerMap(grid.CellsH!.Value, grid.CellsV!.Value, pmc.Inventory.Items, stash.Id);
                var slot = inventory.PlaceItemInContainer(map, StoredTree(entry), stash.Id.ToString(), "hideout");
                if(slot.Success != true) throw new InvalidOperationException("Your personal stash has no free space.");
                profileItem.ParentId = stash.Id.ToString(); profileItem.SlotId = "hideout";
                profileItem.Location = new ItemLocation { X=slot.X, Y=slot.Y, R=slot.Rotation == true ? ItemRotation.Vertical : ItemRotation.Horizontal, IsSearched=true };
            }
            var transfer = new WarehouseTransfer { RequestId=request.RequestId, Profile=session.ToString(), Direction=request.Direction,
                Entry=entry, ProfileItemJson=json.Serialize(profileItem) };
            var transferred=StoredTree(entry);
            if(request.Direction == "withdraw") transferred[0]=profileItem;
            if(request.Direction == "withdraw" && transferred.Any(i => pmc.Inventory.Items.Any(x => x.Id == i.Id)))
                throw new InvalidOperationException("Duplicate item ID in profile.");
            if(request.Direction == "deposit" && transferred.Any(i => store.State.Items.Any(e => StoredTree(e).Any(x => x.Id == i.Id))))
                throw new InvalidOperationException("Duplicate item ID in warehouse.");
            transfer.ProfileChanges=transferred.Select(i => new WarehouseProfileChange {
                Id=i.Id.ToString(), Before=request.Direction == "deposit" ? json.Serialize(i) : null,
                After=request.Direction == "withdraw" ? json.Serialize(i) : null }).ToList();
            // Once the journal exists, do not cancel half a transfer when the client disconnects.
            journalStarted = true;
            store.Begin(transfer);
            await ApplyProfile(transfer, false);
            store.Commit();
            var changes = output.ProfileChanges[session].Items;
            foreach(var item in transferred)
                if(request.Direction == "deposit") changes.DeletedItems.Add(new DeletedItem { Id=item.Id });
                else changes.NewItems.Add(item);
            Console.WriteLine($"[CommunityWarehouse] {request.Direction} {entry.Id} by {session}, revision {store.State.Revision}");
            return output;
        }
        catch(Exception error)
        {
            if(journalStarted) { ready=false; Console.WriteLine("[CommunityWarehouse] Transfer interrupted; restart for journal recovery: " + error); }
            return responses.AppendErrorToOutput(output, journalStarted ? "Transfer interrupted. Reconnect after server recovery; do not retry." : error.Message);
        }
        finally { gate.Release(); }
    }
    private async Task<ItemEventRouterResponse> Merge(PmcData pmc,WarehouseRequest request,MongoId session,ItemEventRouterResponse output)
    {
        if(request.ItemId == request.TargetId) throw new InvalidOperationException("Choose a different stack.");
        bool sourcePersonal=request.Direction == "deposit";
        Item Resolve(string id,bool personal)
        {
            var value=personal ? pmc.Inventory.Items.SingleOrDefault(i => i.Id.ToString() == id)
                : store.State.Items.Where(i => i.Id == id).Select(i => json.Deserialize<Item>(i.Json)).SingleOrDefault();
            if(value == null || (personal && !PersonalStack(value,pmc))) throw new InvalidOperationException("Stack unavailable. Refresh and try again.");
            return value;
        }
        if((!sourcePersonal && (store.State.Items.Single(i => i.Id == request.ItemId).ChildrenJson?.Count ?? 0)>0)
            || (!request.TargetPersonal && (store.State.Items.Single(i => i.Id == request.TargetId).ChildrenJson?.Count ?? 0)>0))
            throw new InvalidOperationException("Composite items cannot be merged.");
        var source=Resolve(request.ItemId,sourcePersonal);
        var target=Resolve(request.TargetId,request.TargetPersonal);
        if(sourcePersonal && !request.TargetPersonal && !Eligible(source,pmc))
            throw new InvalidOperationException("This category cannot be deposited.");
        if(source.Template != target.Template || !WarehouseStackRules.Compatible(json.Serialize(source.Upd),json.Serialize(target.Upd)))
            throw new InvalidOperationException("Stacks must have identical item properties and Found in Raid status.");
        int sourceCount=checked((int)(source.Upd?.StackObjectsCount ?? 1));
        int targetCount=checked((int)(target.Upd?.StackObjectsCount ?? 1));
        int maximum=checked((int)(items.GetItem(target.Template).Value.Properties.StackMaxSize ?? 1));
        int amount=WarehouseStackRules.Amount(sourceCount,targetCount,maximum,request.Amount);
        var transfer=new WarehouseTransfer {RequestId=request.RequestId,Profile=session.ToString(),Direction="merge",
            ProfileChanges=new(),SharedRemoved=new(),SharedUpdated=new()};
        void Change(Item before,bool personal,int count)
        {
            var after=json.Deserialize<Item>(json.Serialize(before))!;
            after.Upd ??= new(); after.Upd.StackObjectsCount=count;
            if(personal) transfer.ProfileChanges.Add(new WarehouseProfileChange {Id=before.Id.ToString(),Before=json.Serialize(before),After=count == 0 ? null : json.Serialize(after)});
            else
            {
                var old=store.State.Items.Single(i => i.Id == before.Id.ToString());
                transfer.SharedRemoved.Add(old.Id);
                if(count > 0)
                {
                    var updated=Describe(after);
                    updated.X=old.X; updated.Y=old.Y; updated.Width=old.Width; updated.Height=old.Height; updated.Rotated=old.Rotated;
                    transfer.SharedUpdated.Add(updated);
                }
            }
        }
        Change(source,sourcePersonal,sourceCount-amount);
        Change(target,request.TargetPersonal,targetCount+amount);
        try { store.Begin(transfer); await ApplyProfile(transfer,false); store.Commit(); }
        catch(Exception error)
        {
            ready=false;
            Console.WriteLine("[CommunityWarehouse] Merge interrupted; restart for recovery: " + error);
            throw new InvalidOperationException("Merge interrupted. Reconnect after server recovery; do not retry.",error);
        }
        foreach(var change in transfer.ProfileChanges)
        {
            if(change.After == null) output.ProfileChanges[session].Items.DeletedItems.Add(new DeletedItem {Id=change.Id});
            else output.ProfileChanges[session].Items.ChangedItems.Add(json.Deserialize<Item>(change.After)!);
        }
        Console.WriteLine($"[CommunityWarehouse] merge {amount} from {request.ItemId} to {request.TargetId}, revision {store.State.Revision}");
        return output;
    }
    private async Task ApplyProfile(WarehouseTransfer transfer, bool recovery)
    {
        if(transfer.ProfileChanges != null)
        {
            if(transfer.ProfileChanges.Count == 0) return;
            MongoId mergeSession=transfer.Profile;
            var profile=Profile(mergeSession);
            // Validate every before/after image before applying any changes.
            foreach(var change in transfer.ProfileChanges)
            {
                var current=profile.Inventory.Items.SingleOrDefault(i => i.Id.ToString() == change.Id);
                var actual=current == null ? null : json.Serialize(current);
                if(actual != change.Before && (!recovery || actual != change.After))
                    throw new InvalidDataException("Merge recovery conflict: item changed.");
            }
            foreach(var change in transfer.ProfileChanges)
            {
                profile.Inventory.Items.RemoveAll(i => i.Id.ToString() == change.Id);
                if(change.After != null) profile.Inventory.Items.Add(json.Deserialize<Item>(change.After)!);
            }
            await saves.SaveProfileAsync(mergeSession,CancellationToken.None);
            return;
        }
        if(transfer.Direction == "split") return;
        MongoId session = transfer.Profile;
        var pmc = Profile(session);
        if(transfer.ProfileBeforeJson != null)
        {
            var before=json.Deserialize<Item>(transfer.ProfileBeforeJson)!;
            var after=json.Deserialize<Item>(transfer.ProfileAfterJson)!;
            var current=pmc.Inventory.Items.SingleOrDefault(x => x.Id == before.Id)
                ?? throw new InvalidDataException("Split recovery conflict: source item missing.");
            string actual=json.Serialize(current);
            if(actual == transfer.ProfileBeforeJson)
                pmc.Inventory.Items[pmc.Inventory.Items.IndexOf(current)]=after;
            else if(!recovery || actual != transfer.ProfileAfterJson)
                throw new InvalidDataException("Split recovery conflict: source item changed.");
            await saves.SaveProfileAsync(session,CancellationToken.None);
            return;
        }
        var existing = pmc.Inventory.Items.SingleOrDefault(x => x.Id.ToString() == transfer.Entry.Id);
        var expected = json.Deserialize<Item>(transfer.ProfileItemJson)!;
        if(transfer.Direction == "deposit")
        {
            if(existing != null)
            {
                if(json.Serialize(existing) != transfer.ProfileItemJson) throw new InvalidDataException("Recovery conflict: source item changed.");
                pmc.Inventory.Items.Remove(existing);
            }
            else if(!recovery) throw new InvalidDataException("Source item missing.");
        }
        else if(existing == null) pmc.Inventory.Items.Add(expected);
        else if(!recovery || json.Serialize(existing) != transfer.ProfileItemJson) throw new InvalidDataException("Recovery conflict: target item changed.");
        await saves.SaveProfileAsync(session, CancellationToken.None);
    }

    private async Task<ItemEventRouterResponse> TransferPartial(PmcData pmc,WarehouseRequest request,MongoId session,
        ItemEventRouterResponse output,Item source,int available)
    {
        if((items.GetItem(source.Template).Value.Properties.StackMaxSize ?? 1) <= 1)
            throw new InvalidOperationException("This item cannot be split into stacks.");
        if(request.Direction == "deposit" && !Eligible(source,pmc))
            throw new InvalidOperationException("This item or its contents cannot be deposited under the server rules.");
        if(request.Direction == "deposit" ? ItemTree(source,pmc).Count>1
            : (store.State.Items.Single(i => i.Id == request.ItemId).ChildrenJson?.Count ?? 0)>0)
            throw new InvalidOperationException("Composite items cannot be split.");
        int amount=request.Amount!.Value;
        var part=json.Deserialize<Item>(json.Serialize(source))!;
        part.Id=new MongoId();
        part.Upd!.StackObjectsCount=amount;
        var remainder=json.Deserialize<Item>(json.Serialize(source))!;
        remainder.Upd!.StackObjectsCount=available-amount;
        var entry=Describe(part);
        var transfer=new WarehouseTransfer {RequestId=request.RequestId,Profile=session.ToString(),
            Direction=request.Direction == "move" ? "split" : request.Direction,Entry=entry};
        if(request.Direction == "deposit")
        {
            transfer.ProfileBeforeJson=json.Serialize(source);
            transfer.ProfileAfterJson=json.Serialize(remainder);
        }
        else
        {
            var original=store.State.Items.Single(i => i.Id == request.ItemId);
            var remaining=Describe(remainder);
            remaining.X=original.X; remaining.Y=original.Y;
            remaining.Width=original.Width; remaining.Height=original.Height; remaining.Rotated=original.Rotated;
            transfer.SharedRemainder=remaining;
        }
        if(request.Direction == "withdraw")
        {
            var stash=pmc.Inventory.Items.Single(i => i.Id == pmc.Inventory.Stash);
            var grid=items.GetItem(stash.Template).Value.Properties.Grids.First().Properties;
            var map=inventory.GetContainerMap(grid.CellsH!.Value,grid.CellsV!.Value,pmc.Inventory.Items,stash.Id);
            var slot=inventory.PlaceItemInContainer(map,new List<Item>{part},stash.Id.ToString(),"hideout");
            if(slot.Success != true) throw new InvalidOperationException("Your personal stash has no free space.");
            part.ParentId=stash.Id.ToString(); part.SlotId="hideout";
            part.Location=new ItemLocation {X=slot.X,Y=slot.Y,R=slot.Rotation == true ? ItemRotation.Vertical : ItemRotation.Horizontal,IsSearched=true};
        }
        else
        {
            bool rotated=request.Rotated ?? (request.Direction == "move" && store.State.Items.Single(i => i.Id == request.ItemId).Rotated);
            if(entry.Width != entry.Height && rotated) { (entry.Width,entry.Height)=(entry.Height,entry.Width); entry.Rotated=true; }
            bool fits=request.X.HasValue ? store.PlaceAt(entry,request.X.Value,request.Y!.Value) : store.Place(entry);
            if(!fits) throw new InvalidOperationException("No free space for the split stack. The original stack stays in place.");
        }
        transfer.ProfileItemJson=json.Serialize(part);
        try
        {
            store.Begin(transfer);
            await ApplyProfile(transfer,false);
            store.Commit();
        }
        catch(Exception error)
        {
            ready=false;
            Console.WriteLine("[CommunityWarehouse] Split interrupted; restart for recovery: " + error);
            throw new InvalidOperationException("Split interrupted. Reconnect after server recovery; do not retry.",error);
        }
        if(request.Direction == "deposit") output.ProfileChanges[session].Items.ChangedItems.Add(remainder);
        else if(request.Direction == "withdraw") output.ProfileChanges[session].Items.NewItems.Add(part);
        Console.WriteLine($"[CommunityWarehouse] {transfer.Direction} {amount}/{available} from {source.Id}, revision {store.State.Revision}");
        return output;
    }
}
