using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Configuration;
using BepInEx.Logging;
using Heborian.Client;
using Heborian.Client.Modules;
using Heborian.Client.Modules.HeborianMenu;
using Heborian.Client.Modules.CommunityWarehouse;
using Heborian.Client.Resources;

public static class ClientVerification
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    public static string Run(string temp)
    {
        Directory.CreateDirectory(temp);
        var config = new ConfigFile(Path.Combine(temp, "test.cfg"), false);
        config.SaveOnConfigSet = false;
        config.Bind("Modules.Disabled", "Enabled", false, "test");
        var order = new List<string>();
        var disabled = new Fake("Disabled", order, false);
        var bad = new Fake("Bad", order, true);
        var first = new Fake("First", order, false);
        var second = new Fake("Second", order, false);
        var duplicate = new Fake("Second", order, false);
        using (var log = new ManualLogSource("HeborianTests"))
        {
            log.LogEvent += (sender, entry) => Console.WriteLine(entry.ToString());
            var host = new ClientModuleHost(config, log, temp);
            var modules = new IClientModule[] { disabled, bad, first, second, duplicate };
            host.Start(modules);
            host.Start(modules);
            Check(disabled.Calls == 0 && bad.Calls == 1 && first.Calls == 1 && second.Calls == 1 && duplicate.Calls == 0, "Client module isolation failed");
            Check(disabled.Cleanups == 1 && bad.Cleanups == 1 && duplicate.Cleanups == 1, "Partial cleanup failed");
            Check(config.ContainsKey(new ConfigDefinition("Modules.First", "Value")) && config.ContainsKey(new ConfigDefinition("Modules.Second", "Value")), "Config sections not independent");
            host.Dispose();
            host.Dispose();
            Check(first.Cleanups == 1 && second.Cleanups == 1, "Cleanup not idempotent");
            Check(order[order.Count - 2] == "Second" && order[order.Count - 1] == "First", "Cleanup not in reverse order");
        }
        // Path and byte loading do not require a Unity scene. Texture upload does.
        var assetPath = Path.Combine(temp, "images");
        Directory.CreateDirectory(assetPath);
        File.WriteAllBytes(Path.Combine(assetPath, "sample.dat"), new byte[] { 1, 2, 3 });
        using (var assets = new ModuleAssets(assetPath))
        {
            Check(assets.ReadBytes("sample.dat").Length == 3, "Asset bytes not loaded");
            foreach (var escape in new[] { "../outside.dat", Path.GetFullPath(temp) })
            {
                bool rejected = false;
                try { assets.Resolve(escape); } catch (ArgumentException) { rejected = true; }
                Check(rejected, "Asset path escaped module directory");
            }
        }
        Check(Compatibility.IsSupported(new Version("4.1.6")) && Compatibility.IsSupported(new Version("4.1.6.0")), "Supported version rejected");
        Check(!Compatibility.IsSupported(new Version("4.1.7")) && !Compatibility.IsSupported(new Version("4.0.0")), "Unsupported version accepted");
        VerifyMenuContentLifecycle();
        VerifyPacking();
        return "PASS: client configuration, enable/disable, isolated failures, duplicate IDs, reverse cleanup, asset paths/bytes and version guard (outside Unity).";
    }

    private static void VerifyPacking()
    {
        var items=new List<WarehouseItem>();
        var random=new Random(416);
        for(int i=0;i<150;i++) items.Add(new WarehouseItem {width=random.Next(1,5),height=random.Next(1,5)});
        var positions=WarehousePacking.Pack(items);
        var cells=new HashSet<string>();
        Check(positions.Count==items.Count,"Packed item lost");
        for(int i=0;i<items.Count;i++)
        {
            var p=positions[i]; var item=items[i];
            Check(p.X>=0 && p.Y>=0 && p.X+item.width<=8,"Personal view outside grid width");
            for(int y=p.Y;y<p.Y+item.height;y++) for(int x=p.X;x<p.X+item.width;x++)
                Check(cells.Add(x+","+y),"Personal view overlap");
        }
        var shared=new List<WarehouseItem> {new WarehouseItem {id="moving",x=2,y=3,width=2,height=2}};
        Check(WarehousePacking.Fits(shared,3,3,2,2,"moving"),"Move preview collides with its own old cells");
        Check(!WarehousePacking.Fits(shared,3,3,2,2,"other"),"Move preview ignores another item");
        Check(!WarehousePacking.Fits(shared,3,4,1,1),"Occupied drop preview accepted");
        Check(WarehousePacking.Fits(shared,4,3,2,2),"Adjacent valid drop preview rejected");
        Check(!WarehousePacking.Fits(shared,15,15,2,1) && !WarehousePacking.Fits(shared,int.MaxValue,0,1,1),"Out-of-bounds preview accepted");
        Console.WriteLine("PASS: 150 mixed-size native item placements without overlap; drop previews reject occupied/out-of-bounds cells.");
        foreach(double pitch in new[]{30.0,60.0,63.0})
        foreach(double edgeX in new[]{0.001,0.1,0.49,0.51,0.9,0.999})
        foreach(double edgeY in new[]{0.001,0.1,0.49,0.51,0.9,0.999})
        {
            var single=WarehousePacking.DropPosition((3+edgeX)*pitch,(4+edgeY)*pitch,pitch,0,0);
            Check(single.X==3 && single.Y==4,"Preview jumps inside a cell");
            var multi=WarehousePacking.DropPosition((3+edgeX)*pitch,(4+edgeY)*pitch,pitch,1,2);
            Check(multi.X==2 && multi.Y==2,"Multi-cell grab anchor changed");
            var rotated=WarehousePacking.DropPosition((3+edgeX)*pitch,(4+edgeY)*pitch,pitch,2,1);
            Check(rotated.X==1 && rotated.Y==3,"Rotated grab anchor changed");
        }
        var border=WarehousePacking.DropPosition(240,300,60,0,0);
        Check(border.X==4 && border.Y==5,"Exact boundary does not enter next cell");
        var outside=WarehousePacking.DropPosition(-0.01,-0.01,60,0,0);
        Check(outside.X==-1 && outside.Y==-1,"Outside edge snaps into grid");
        var overflow=WarehousePacking.DropPosition(960,960,60,0,0);
        Check(!WarehousePacking.Fits(shared,overflow.X,overflow.Y,1,1),"Outer edge accepted as valid cell");
        foreach(int count in new[] {101,500,500000,16777217,int.MaxValue})
            Check(WarehousePacking.UseStepSplit(count),"Large stack would allocate unbounded native notches");
        foreach(int count in new[] {2,60,100})
            Check(!WarehousePacking.UseStepSplit(count),"Small stack dialog changed");
        Check(WarehousePacking.ValidSplitAmount(16777217,20000000)
            && WarehousePacking.ValidSplitAmount(int.MaxValue,int.MaxValue)
            && !WarehousePacking.ValidSplitAmount(0,500000)
            && !WarehousePacking.ValidSplitAmount(500001,500000),"Split quantity validation failed");
        var hiddenFootprint=new List<WarehouseItem> {new WarehouseItem {x=0,y=1,width=1,height=1}};
        Check(!WarehousePacking.Fits(hiddenFootprint,0,1,1,1,rows:2),"Hidden footprint preview permits overlap");
        Check(WarehousePacking.Fits(hiddenFootprint,15,1,1,1,rows:2),"Configured last cell rejected");
        Check(!WarehousePacking.Fits(hiddenFootprint,15,2,1,1,rows:2),"Configured height ignored");
        Check(WarehousePacking.Fits(hiddenFootprint,15,63,1,1,rows:64),"Larger configured height ignored");
        Check(WarehousePacking.Fits(shared,15,31,1,1),"Expanded bottom-right preview rejected");
        Check(!WarehousePacking.Fits(shared,15,32,1,1),"Row below expanded grid accepted");
        Check(!WarehousePacking.Fits(shared,15,31,1,2),"Tall item crosses expanded bottom edge");
        Console.WriteLine("PASS: stable cursor-cell drop preview, internal edges, multi-cell/rotated anchors and grid bounds.");
        var ammo=new WarehouseItem {id="a",template="ammo",count=20,maxStack=60};
        var targetAmmo=new WarehouseItem {id="b",template="ammo",count=55,maxStack=60};
        Check(WarehousePacking.CanMerge(ammo,targetAmmo),"compatible merge preview rejected");
        targetAmmo.count=60; Check(!WarehousePacking.CanMerge(ammo,targetAmmo),"full stack preview accepted");
        targetAmmo.count=55; targetAmmo.upd=Newtonsoft.Json.Linq.JObject.Parse("{\"SpawnedInSession\":true}");
        Check(!WarehousePacking.CanMerge(ammo,targetAmmo),"FIR mismatch preview accepted");
        targetAmmo.upd=null; targetAmmo.template="other";
        Check(!WarehousePacking.CanMerge(ammo,targetAmmo) && !WarehousePacking.CanMerge(ammo,ammo),"different item or self merge accepted");
        Console.WriteLine("PASS: merge previews check capacity, template, identity and Found in Raid.");
    }

    private static void VerifyMenuContentLifecycle()
    {
        var lifecycle = new MenuContentLifecycle();
        var calls = new List<string>();
        var errors = new List<string>();
        Action open = () => calls.Add("open");
        Action update = () => calls.Add("update");
        Action close = () => calls.Add("close");
        // First subscriber fails: other content and BACK must still function.
        lifecycle.Opened += () => { throw new InvalidOperationException("Expected open failure"); };
        lifecycle.Updated += () => { throw new InvalidOperationException("Expected tick failure"); };
        lifecycle.Closed += () => { throw new InvalidOperationException("Expected close failure"); };
        lifecycle.Opened += open;
        lifecycle.Updated += update;
        lifecycle.Closed += close;
        lifecycle.Tick();
        lifecycle.Close();
        Check(calls.Count == 0, "Closed screen invoked content");
        Action<string, Exception> report = (phase, error) => errors.Add(phase);
        lifecycle.Open(report);
        Check(lifecycle.IsOpen && calls.Count == 1 && calls[0] == "open", "Content not created synchronously on open");
        lifecycle.Open(report);
        Check(calls.Count == 1, "Duplicate open recreated content");
        lifecycle.Tick(); lifecycle.Tick();
        Check(calls.Count == 3 && errors.Count == 2, "Content tick failed or repeated errors flooded log");
        lifecycle.Close(); lifecycle.Close(); lifecycle.Tick();
        Check(!lifecycle.IsOpen && calls.Count == 4 && calls[3] == "close" && errors.Count == 3, "Close did not clean up once");
        lifecycle.Opened -= open; lifecycle.Updated -= update; lifecycle.Closed -= close;
        lifecycle.Open(report); lifecycle.Tick(); lifecycle.Close();
        Check(calls.Count == 4, "Unsubscribed module still invoked");
        Check(errors.Count == 6, "Reopen failed to report fresh errors");
        Console.WriteLine("PASS: screen-driven content opens synchronously, ticks only while open, closes once, isolates failures and unsubscribes cleanly (no startup scene required).");
    }

    private sealed class Fake : IClientModule
    {
        public string Id { get; private set; }
        private readonly List<string> order;
        private readonly bool fail;
        public int Calls;
        public int Cleanups;
        public Fake(string id, List<string> order, bool fail) { Id = id; this.order = order; this.fail = fail; }
        public void Initialize(ClientModuleContext context)
        {
            Calls++;
            context.Bind("Value", Id, "Independent config test");
            if (fail) throw new InvalidOperationException("Expected test failure");
        }
        public void Dispose() { Cleanups++; order.Add(Id); }
    }
}
