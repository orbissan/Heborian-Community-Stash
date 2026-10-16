using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Heborian.Server.Modules.CommunityWarehouse;

public static class WarehouseHistoryVerification
{
    private static void Check(bool value,string message) { if(!value) throw new Exception(message); }
    private static WarehouseAuditEvent Event(string action="deposit",long revision=0) => new() {
        RequestId=Guid.NewGuid().ToString("N"),Revision=revision,TimeUtc=DateTime.UtcNow,
        PlayerId="private-profile-id",PlayerName="Alice",Action=action,ItemId="private-item-id",Template="ammo",ItemName="Ammunition",Count=3
    };
    private static JsonElement Read(WarehouseAudit archive,int page=0,string filter="all",string search="",long through=0)
        => JsonSerializer.SerializeToElement(archive.Read(page,filter,search,through));
    public static void Run(string root)
    {
        root=Path.Combine(root,Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var dir=Path.Combine(root,"history"); File.WriteAllText(dir,"Simulate archive failure");
        var archive=new WarehouseAudit(dir,30);
        var path=Path.Combine(root,"warehouse.json");
        var store=new WarehouseStore(path,32,archive);
        var record=Event();
        var entry=new WarehouseEntry {Id="item",Template="ammo",Name="Ammunition",Json="{}",Width=2,Height=1,Count=3};
        var transfer=new WarehouseTransfer {Direction="deposit",RequestId=record.RequestId,Entry=entry,Audit=record};
        store.Begin(transfer);
        Check(store.State.AuditOutbox.Count==0,"Uncommitted operation became public history");
        store.Commit();
        Check(store.State.Revision==1 && store.State.Items.Count==1 && store.State.AuditOutbox.Count==1,"Archive failure lost committed event or reverted ownership");
        Check(JsonDocument.Parse(File.ReadAllText(path)).RootElement.GetProperty("AuditOutbox").GetArrayLength()==1,"Outbox not durable");
        File.Delete(dir);
        store=new WarehouseStore(path,32,new WarehouseAudit(dir,30));store.Load();
        Check(store.State.AuditOutbox.Count==0 && Directory.GetFiles(dir,"*.json").Length==1,"Outbox did not recover");
        store=new WarehouseStore(path,32,new WarehouseAudit(dir,30));store.Load();
        Check(Directory.GetFiles(dir,"*.json").Length==1,"Restart duplicated event");
        var first=JsonSerializer.Deserialize<WarehouseAuditEvent>(File.ReadAllText(Directory.GetFiles(dir,"*.json")[0]));
        Check(first.Revision==1 && first.Count==3 && first.PlayerName=="Alice" && first.TimeUtc>DateTime.UtcNow.AddMinutes(-1),"Committed metadata wrong");
        int before=Directory.GetFiles(dir,"*.json").Length;
        Check(!store.Reposition("item",16,0,Guid.NewGuid().ToString("N"),"profile",null,Event("move")),"Invalid move accepted");
        Check(Directory.GetFiles(dir,"*.json").Length==before,"Rejected move was logged");
        var movement=Event("move");
        Check(store.Reposition("item",4,5,movement.RequestId,"profile",true,movement),"Rotation failed");
        var arranged=Read(new WarehouseAudit(dir,30),filter:"arrange");
        Check(arranged.GetProperty("total").GetInt32()==1 && arranged.GetProperty("rows")[0].GetProperty("action").GetString()=="rotate","Arrangement filter missing rotation");
        Check(Read(new WarehouseAudit(dir,30)).GetProperty("total").GetInt32()==1,"Default transfer list includes rearrangements");
        Console.WriteLine("PASS: only completed operations logged; archive failure keeps durable outbox; restart replay is idempotent; rejected moves excluded.");

        var failedDir=Path.Combine(root,"failed-history");
        var failedPath=Path.Combine(root,"failed.json");
        var failed=new WarehouseStore(failedPath,32,new WarehouseAudit(failedDir,30));
        failed.Begin(new WarehouseTransfer {Direction="deposit",Entry=entry,RequestId=Guid.NewGuid().ToString("N"),Audit=Event()});
        using(var locked=new FileStream(failedPath+".tmp",FileMode.Create,FileAccess.Write,FileShare.None))
        { bool threw=false;try{failed.Commit();}catch(IOException){threw=true;}Check(threw,"Fault injection did not fail"); }
        Check(!Directory.Exists(failedDir),"Failed warehouse commit generated success history");
        failed.Commit();Check(Directory.GetFiles(failedDir,"*.json").Length==1,"Retry did not log exactly once");

        archive=new WarehouseAudit(dir,30);
        for(int i=0;i<30;i++) archive.Append(Event(i%2==0 ? "deposit" : "withdraw",100+i));
        var page=Read(archive);long anchor=page.GetProperty("throughRevision").GetInt64();
        Check(page.GetProperty("rows").GetArrayLength()==12 && page.GetProperty("pages").GetInt32()==3,"History not paginated");
        var oldPage=page.GetRawText();archive.Append(Event("withdraw",200));
        Check(Read(archive,through:anchor).GetRawText()==oldPage,"New operation shifted an anchored page");
        Check(Read(archive,filter:"withdraw",search:"aLiCe").GetProperty("total").GetInt32()==16,"Player/action filter incorrect");
        Check(Read(archive,search:"missing").GetProperty("total").GetInt32()==0,"Search ignored");
        Check(!Read(archive).GetRawText().Contains("private-profile-id") && !Read(archive).GetRawText().Contains("private-item-id"),"Technical IDs leaked to public history");
        bool invalid=false;try{archive.Read(-1,"all","",0);}catch(ArgumentException){invalid=true;}Check(invalid,"Invalid page accepted");
        var decorated=Event("deposit",201);
        decorated.ItemName="<b><color=#9F5ACF>5.56x45mm M855 [54/31] ★</color></b>";
        archive.Append(decorated);
        var saved=Directory.GetFiles(dir,"*.json").ToDictionary(f=>f,File.ReadAllText);
        var clean=Read(new WarehouseAudit(dir,30),search:"M855").GetProperty("rows")[0];
        Check(clean.GetProperty("item").GetString()=="5.56x45mm M855","Decorated legacy item name not cleaned");
        Check(clean.GetProperty("template").GetString()=="ammo","Preview template missing");
        var categorized=JsonSerializer.SerializeToElement(archive.Read(0,"all","M855",0,t=>t=="ammo" ? "Ammo > Rounds" : "Other"));
        Check(categorized.GetProperty("rows")[0].GetProperty("category").GetString()=="Ammo > Rounds","Category resolver missing from history response");
        archive.Append(decorated);
        Check(saved.All(x=>File.ReadAllText(x.Key)==x.Value),"Presentation changed archive bytes or replay");
        Check(Heborian.HistoryPresentation.ItemName("<b>Construction measuring tape ☆</b>")=="Construction measuring tape","Star/markup remained");
        Check(Heborian.HistoryPresentation.ItemName("M4A1 (5.56x45)")=="M4A1 (5.56x45)","Real item name damaged");
        Console.WriteLine("PASS: legacy decorated names cleaned, preview templates available, archive bytes/replay unchanged.");
        var med=WarehouseCondition.Read("{\"upd\":{\"MedKit\":{\"HpResource\":17}}}","{\"MaxHpResource\":220}");
        Check(med.Current==17 && med.Maximum==220,"Medication condition wrong");
        var food=WarehouseCondition.Read("{\"upd\":{\"FoodDrink\":{\"HpPercent\":2}}}","{\"MaxResource\":40}");
        Check(food.Current==2 && food.Maximum==40,"Food condition wrong");
        Check(WarehouseCondition.Read("{}","{\"MaxHpResource\":4}").Current==4,"Default uses wrong");
        Check(WarehouseCondition.Read("{}","{}")==null,"Money/ammo gained invented condition");
        Check(!JsonSerializer.Serialize(Event()).Contains("Condition"),"Legacy null condition changes archive serialization");
        var conditionEvent=Event("deposit",202);conditionEvent.Condition=med;archive.Append(conditionEvent);
        Check(Read(new WarehouseAudit(dir,30)).GetProperty("rows")[0].GetProperty("condition").GetProperty("Current").GetDouble()==17,"Condition not preserved through archive reload");
        Console.WriteLine("PASS: historical medication/food condition, missing-state defaults, legacy serialization and reload.");
        var key=WarehouseCondition.Read("{\"upd\":{\"Key\":{\"NumberOfUsages\":3}}}","{\"MaximumNumberOfUsage\":40}");
        Check(key.Current==37 && key.Maximum==40 && key.Icon=="Durability","Key remaining uses/icon wrong");
        var kit=WarehouseCondition.Read("{\"upd\":{\"RepairKit\":{\"Resource\":20}}}","{\"MaxRepairResource\":1000}");
        Check(kit.Current==20 && kit.Icon=="RepairResource","Repair kit icon/resource wrong");
        Check(med.Icon=="HpResource" && food.Icon=="FoodResource","Native condition icon mapping wrong");
        var legacyCondition=JsonSerializer.Deserialize<WarehouseCondition>("{\"Current\":2,\"Maximum\":40}");
        Check(JsonSerializer.Serialize(legacyCondition)=="{\"Current\":2,\"Maximum\":40}","Legacy condition replay serialization changed");
        Check(!JsonSerializer.Serialize(Event()).Contains("Fee"),"Legacy null fee changed archive bytes");
        var paid=Event("withdraw",203);paid.Fee=new WarehousePaidFee {Currency="EUR",Amount=100000};
        archive.Append(paid);archive.Append(paid);
        var paidRow=Read(new WarehouseAudit(dir,30)).GetProperty("rows")[0].GetProperty("fee");
        Check(paidRow.GetProperty("Amount").GetInt32()==100000 && paidRow.GetProperty("Currency").GetString()=="EUR","Historical fee lost on public read/reload");
        Console.WriteLine("PASS: paid fee archive replay/public read and legacy fee serialization.");
        var retention=Path.Combine(root,"retention");var writer=new WarehouseAudit(retention,3650);
        var old=Event("deposit",1);old.TimeUtc=DateTime.UtcNow.AddDays(-2);writer.Append(old);writer.Append(Event("deposit",2));
        Check(Read(new WarehouseAudit(retention,1)).GetProperty("total").GetInt32()==1 && Directory.GetFiles(retention,"*.json").Length==1,"Retention did not remove only expired history");
        Console.WriteLine("PASS: commit failure does not log success; paging anchor, filters, public ID redaction and history-only retention.");
    }
}
