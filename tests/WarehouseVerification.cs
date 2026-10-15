using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Heborian.Server.Modules.CommunityWarehouse;

public static class WarehouseVerification
{
    private static void Check(bool ok, string name) { if(!ok) throw new Exception(name); }
    public static void Run(string root)
    {
        Directory.CreateDirectory(root);
        var resizePath=Path.Combine(root,Guid.NewGuid()+".json");
        var full=new WarehouseStore(resizePath);
        var hidden=new WarehouseEntry {Id="hidden",Width=1,Height=1,X=15,Y=31,Json="keep-root",ChildrenJson=new() {"keep-child"}};
        var crossing=new WarehouseEntry {Id="crossing",Width=1,Height=2,X=0,Y=1,Json="keep-crossing"};
        foreach(var entry in new[] {hidden,crossing}) { full.Begin(new WarehouseTransfer {RequestId=entry.Id,Direction="deposit",Entry=entry}); full.Commit(); }
        string beforeShrink=File.ReadAllText(resizePath);
        var small=new WarehouseStore(resizePath,2); small.Load();
        Check(File.ReadAllText(resizePath)==beforeShrink && small.State.Items.Count==2,"shrink must not rewrite/delete items");
        Check(!small.Visible(hidden) && !small.Visible(crossing),"whole item hidden when crossing boundary");
        Check(!small.PlaceAt(new WarehouseEntry {Width=1,Height=1},0,1),"hidden item's visible footprint stays reserved");
        Check(!small.Reposition("hidden",2,0,"hidden-move","test"),"hidden source cannot move");
        Check(!small.PlaceAt(new WarehouseEntry {Width=1,Height=1},1,2),"new placement outside active rows rejected");
        Check(!small.Place(new WarehouseEntry {Width=16,Height=3}),"automatic placement respects active rows");
        var added=new WarehouseEntry {Id="added",Width=1,Height=1};
        Check(small.Place(added),"new visible placement works");
        small.Begin(new WarehouseTransfer {RequestId="add-small",Direction="deposit",Entry=added}); small.Commit();
        var restored=new WarehouseStore(resizePath,64); restored.Load();
        Check(restored.State.Items.Count==3 && restored.State.Items.Single(i=>i.Id=="hidden").ChildrenJson.Single()=="keep-child","hidden tree survives intervening writes");
        Check(restored.State.Items.All(restored.Visible) && restored.State.Items.Single(i=>i.Id=="hidden").Y==31,"growing restores original positions");
        Check(restored.PlaceAt(new WarehouseEntry {Width=1,Height=1},15,63),"growth beyond default rows supported");
        foreach(int invalid in new[] {0,-1,Heborian.WarehouseDimensions.MaxRows+1}) {
            bool rejected=false; try {new WarehouseStore(resizePath,invalid);} catch(ArgumentOutOfRangeException){rejected=true;}
            Check(rejected,"invalid configured row count accepted");
        }
        Console.WriteLine("PASS: configurable rows, hidden-tree preservation, partial-boundary reservation and shrink/grow roundtrip.");
        var legacyPath=Path.Combine(root,Guid.NewGuid()+".json");
        File.WriteAllText(legacyPath,"{\"Schema\":1,\"Revision\":9,\"Items\":[{\"Id\":\"legacy\",\"Width\":1,\"Height\":1,\"X\":7,\"Y\":7,\"Count\":12,\"Json\":\"original\"}],\"History\":[]}");
        var expanded=new WarehouseStore(legacyPath); expanded.Load();
        Check(expanded.State.Items.Single().X==7 && expanded.State.Revision==9,"expansion must preserve old 8x8 layout");
        Check(expanded.Reposition("legacy",15,31,"expand-move","test"),"new last cell reachable");
        var expandedReload=new WarehouseStore(legacyPath); expandedReload.Load();
        Check(expandedReload.State.Items.Single().Count==12 && expandedReload.State.Items.Single().Json=="original","expansion preserves item state");
        Check(!expandedReload.PlaceAt(new WarehouseEntry {Width=1,Height=1},16,31),"column 17 rejected");
        Check(!expandedReload.PlaceAt(new WarehouseEntry {Width=1,Height=1},15,32),"row 33 rejected");
        Console.WriteLine("PASS: existing 8x8 storage loads unchanged; expanded corner persists, 16x32 bounds enforced.");
        var path=Path.Combine(root,Guid.NewGuid()+".json");
        var store=new WarehouseStore(path); store.Load();
        // Pack different shapes; reject overlap and full capacity.
        var large=new WarehouseEntry { Id="large",Width=16,Height=31,Json="{}" };
        Check(store.Place(large),"large placement");
        var t=new WarehouseTransfer { RequestId="one",Direction="deposit",Entry=large };
        store.Begin(t);
        var crash=new WarehouseStore(path); crash.Load();
        Check(crash.State.Pending.RequestId=="one" && crash.State.Items.Count==0,"write ahead journal survives restart");
        crash.Commit(); store=crash;
        var row=new WarehouseEntry { Id="row",Width=16,Height=1,Json="{}" };
        Check(store.Place(row) && row.Y==31,"last row placement");
        store.Begin(new WarehouseTransfer { RequestId="two",Direction="deposit",Entry=row }); store.Commit();
        Check(!store.Place(new WarehouseEntry { Width=1,Height=1 }),"full storage rejected");
        bool duplicate=false;
        try { store.Begin(t); } catch(InvalidOperationException) { duplicate=true; }
        Check(duplicate,"duplicate request rejected");
        store.Begin(new WarehouseTransfer { RequestId="three",Direction="withdraw",Entry=row });
        crash=new WarehouseStore(path); crash.Load(); crash.Commit();
        Check(crash.State.Items.Count==1 && crash.State.Revision==3 && crash.State.History.Count==3,"withdraw recovery and audit");
        var loaded=new WarehouseStore(path); loaded.Load();
        Check(loaded.State.Pending==null && loaded.State.Items.Single().Id=="large","committed state survives restart");
        Check(File.Exists(path+".bak"),"backup retained");
        var placed=new WarehouseEntry { Id="target",Width=1,Height=1,Json="{}" };
        Check(!loaded.PlaceAt(placed,0,0),"occupied target rejected");
        Check(!loaded.PlaceAt(placed,int.MaxValue,31) && !loaded.PlaceAt(placed,15,-1),"invalid/overflow coordinates rejected");
        Check(loaded.PlaceAt(placed,15,31),"explicit last cell accepted");
        loaded.Begin(new WarehouseTransfer { RequestId="target-position",Direction="deposit",Entry=placed }); loaded.Commit();
        var positioned=new WarehouseStore(path); positioned.Load();
        Check(positioned.State.Items.Single(x=>x.Id=="target").X==15 && positioned.State.Items.Single(x=>x.Id=="target").Y==31,"target survives persistence");
        Check(!positioned.PlaceAt(new WarehouseEntry {Width=2,Height=1},14,31),"multi-cell overlap rejected");
        long revision=positioned.State.Revision;
        Check(!positioned.Reposition("target",0,0,"blocked","test"),"move cannot overlap another item");
        Check(!positioned.Reposition("target",int.MaxValue,0,"overflow","test"),"move rejects extreme coordinates");
        Check(!positioned.Reposition("missing",0,31,"missing","test"),"move rejects missing item");
        Check(positioned.State.Revision==revision,"invalid move preserves revision");
        Check(positioned.Reposition("target",14,31,"move-one","test"),"move to free cell");
        Check(positioned.State.History.Single(x=>x.RequestId=="target-position").Entry.X==15,"move does not rewrite deposit history");
        Check(positioned.Reposition("large",0,0,"move-self","test"),"own occupied cells are allowed");
        var movedReload=new WarehouseStore(path); movedReload.Load();
        Check(movedReload.State.Revision==revision+2 && movedReload.State.Items.Count==2 && movedReload.State.Pending==null,"moves persist without ownership journal");
        Check(movedReload.State.Items.Single(x=>x.Id=="target").X==14,"relocation survives reload");
        bool replay=false;
        try { movedReload.Reposition("target",5,31,"move-one","test"); } catch(InvalidOperationException) { replay=true; }
        Check(replay,"relocation replay rejected");
        Console.WriteLine("PASS: shared relocation, own-cell overlap, collision/bounds, audit isolation, replay and reload.");
        var rotationPath=Path.Combine(root,Guid.NewGuid()+".json");
        var rotating=new WarehouseStore(rotationPath); rotating.Load();
        var tall=new WarehouseEntry {Id="tall",Width=1,Height=2,Json="original",X=0,Y=0};
        rotating.Begin(new WarehouseTransfer {RequestId="rot-deposit",Direction="deposit",Entry=tall}); rotating.Commit();
        Check(rotating.Reposition("tall",0,0,"rot-turn","test",true),"rotate through original cells");
        var r=rotating.State.Items.Single();
        Check(r.Rotated && r.Width==2 && r.Height==1 && r.Json=="original","rotation preserves item data and swaps footprint");
        Check(!rotating.Reposition("tall",15,15,"rot-edge","test",true),"rotated footprint cannot cross right edge");
        var blocker=new WarehouseEntry {Id="block",Width=1,Height=1,X=0,Y=1};
        rotating.Begin(new WarehouseTransfer {RequestId="rot-block",Direction="deposit",Entry=blocker}); rotating.Commit();
        Check(!rotating.Reposition("tall",0,0,"rot-collision","test",false),"rotation into neighbor rejected");
        Check(rotating.State.Items.Single(i=>i.Id=="tall").Rotated,"invalid rotation leaves orientation unchanged");
        var rotationReload=new WarehouseStore(rotationPath); rotationReload.Load();
        Check(rotationReload.State.Items.Single(i=>i.Id=="tall").Rotated,"rotation survives restart");
        Check(rotationReload.Reposition("tall",3,3,"rot-back","test",false),"rotate back");
        Check(rotationReload.State.Items.Single(i=>i.Id=="tall").Width==1,"second turn restores dimensions");
        Check(!rotationReload.State.History.Single(t=>t.RequestId=="rot-deposit").Entry.Rotated,"rotation preserves old audit orientation");
        Console.WriteLine("PASS: rotation footprint, self overlap, collision/edge rejection, cancellation, audit and restart.");
        for(int size=1;size<=3;size++)
        foreach(bool legacyRotation in new[]{false,true})
        {
            var squarePath=Path.Combine(root,Guid.NewGuid()+".json");
            var squares=new WarehouseStore(squarePath); squares.Load();
            squares.Begin(new WarehouseTransfer {RequestId="square-seed",Direction="deposit",
                Entry=new WarehouseEntry {Id="square",Width=size,Height=size,Rotated=legacyRotation,Count=7,Json="unchanged"}});
            squares.Commit();
            Check(squares.Reposition("square",4,4,"square-move","test",!legacyRotation),"square can still move");
            var squareReload=new WarehouseStore(squarePath); squareReload.Load();
            var square=squareReload.State.Items.Single();
            Check(square.Rotated==legacyRotation && square.Width==size && square.Height==size,"square rotation request ignored after restart");
            Check(square.X==4 && square.Y==4 && square.Count==7 && square.Json=="unchanged","square move preserves item state");
        }
        Console.WriteLine("PASS: square items 1x1/2x2/3x3 reject orientation changes while preserving relocation and legacy state.");
        var splitPath=Path.Combine(root,Guid.NewGuid()+".json");
        var splitStore=new WarehouseStore(splitPath); splitStore.Load();
        splitStore.Begin(new WarehouseTransfer {RequestId="split-seed",Direction="deposit",
            Entry=new WarehouseEntry {Id="stack",Width=1,Height=1,Count=30,Json="original"}}); splitStore.Commit();
        splitStore.Begin(new WarehouseTransfer {RequestId="split-local",Direction="split",
            Entry=new WarehouseEntry {Id="part",Width=1,Height=1,X=1,Count=10,Json="part"},
            SharedRemainder=new WarehouseEntry {Id="stack",Width=1,Height=1,Count=20,Json="remainder"}});
        var splitCrash=new WarehouseStore(splitPath); splitCrash.Load(); splitCrash.Commit();
        Check(splitCrash.State.Items.Sum(i=>i.Count)==30 && splitCrash.State.Items.Count==2,"local split recovery conserves quantity");
        splitCrash.Begin(new WarehouseTransfer {RequestId="split-withdraw",Direction="withdraw",
            Entry=new WarehouseEntry {Id="taken",Width=1,Height=1,Count=4},
            SharedRemainder=new WarehouseEntry {Id="stack",Width=1,Height=1,Count=16}});
        splitStore=new WarehouseStore(splitPath); splitStore.Load(); splitStore.Commit();
        Check(splitStore.State.Items.Sum(i=>i.Count)==26 && splitStore.State.Items.All(i=>i.Id!="taken"),"partial withdrawal keeps remainder only");
        Check(splitStore.State.History.Single(t=>t.RequestId=="split-seed").Entry.Count==30,"split does not mutate previous journal entry");
        Console.WriteLine("PASS: partial-stack journal, restart recovery, unique IDs and quantity conservation.");
        Check(WarehouseStackRules.Compatible("{\"StackObjectsCount\":20}","{\"SpawnedInSession\":false,\"StackObjectsCount\":50}"),"counts do not prevent merge");
        Check(!WarehouseStackRules.Compatible("{\"SpawnedInSession\":true}","{}"),"FIR mismatch rejected");
        Check(!WarehouseStackRules.Compatible("{\"MedKit\":{\"HpResource\":10}}","{\"MedKit\":{\"HpResource\":20}}"),"item state mismatch rejected");
        Check(WarehouseStackRules.Amount(20,55,60,null)==5 && WarehouseStackRules.Amount(20,10,60,3)==3,"merge capacity and requested quantity");
        foreach(int invalid in new[]{0,-1,21})
        {
            bool rejected=false; try {WarehouseStackRules.Amount(20,10,60,invalid);} catch(InvalidOperationException){rejected=true;}
            Check(rejected,"invalid merge amount rejected");
        }
        var mergePath=Path.Combine(root,Guid.NewGuid()+".json");
        var mergeStore=new WarehouseStore(mergePath); mergeStore.Load();
        foreach(var entry in new[]{new WarehouseEntry {Id="source",Width=1,Height=1,Count=20},new WarehouseEntry {Id="target",Width=1,Height=1,X=1,Count=55}})
        {mergeStore.Begin(new WarehouseTransfer {RequestId=entry.Id,Direction="deposit",Entry=entry});mergeStore.Commit();}
        mergeStore.Begin(new WarehouseTransfer {RequestId="merge",Direction="merge",ProfileChanges=new(),SharedRemoved=new(){"source","target"},
            SharedUpdated=new(){new WarehouseEntry {Id="source",Width=1,Height=1,Count=15},new WarehouseEntry {Id="target",Width=1,Height=1,X=1,Count=60}}});
        var mergeRecovery=new WarehouseStore(mergePath); mergeRecovery.Load(); mergeRecovery.Commit();
        Check(mergeRecovery.State.Items.Sum(i=>i.Count)==75 && mergeRecovery.State.Items.Single(i=>i.Id=="source").Count==15,"merge restart conserves remainder");
        Check(mergeRecovery.State.History.First().Entry.Count==20,"merge preserves audit history");
        Console.WriteLine("PASS: merge compatibility/FIR, capacity, invalid quantities and durable shared remainder recovery.");
        File.WriteAllText(path,"{broken");
        bool corruption=false;
        try { new WarehouseStore(path).Load(); } catch { corruption=true; }
        Check(corruption,"corrupt storage fails closed, never resets inventory");
        Console.WriteLine("PASS: warehouse packing/full capacity, durable pending journal, deposit/withdraw commit, restart, duplicate requests, backup and corrupt-file rejection.");
        Console.WriteLine("PASS: explicit drop coordinates, occupied cells, bounds/overflow, multi-cell overlap and persisted target.");
    }
}
