using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Heborian.Server.Modules.CommunityWarehouse;

public sealed class WarehouseHistoryOptions
{
    public bool Enabled { get; set; } = true;
    // Private mode exposes files to the server owner, never an unauthenticated admin API.
    public bool Public { get; set; } = true;
    public int RetentionDays { get; set; } = 30;
}
public sealed class WarehouseAuditItem
{
    public string Id { get; set; }
    public string Template { get; set; }
    public string Name { get; set; }
    public int Count { get; set; }
}
public sealed class WarehouseAuditEvent
{
    public string RequestId { get; set; }
    public long Revision { get; set; }
    public DateTime TimeUtc { get; set; }
    public string PlayerId { get; set; }
    public string PlayerName { get; set; }
    public string Action { get; set; }
    public string ItemId { get; set; }
    public string Template { get; set; }
    public string ItemName { get; set; }
    public int Count { get; set; }
    public string TargetId { get; set; }
    public int? FromX { get; set; }
    public int? FromY { get; set; }
    public int? ToX { get; set; }
    public int? ToY { get; set; }
    public bool? FromRotated { get; set; }
    public bool? ToRotated { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public WarehouseCondition Condition { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public WarehousePaidFee Fee { get; set; }
    public List<WarehouseAuditItem> Contents { get; set; } = new();
}

// Each event is a small atomic file. WarehouseState's durable outbox is the source
// of truth until archiving succeeds; replay overwrites no files and creates no duplicates.
public sealed class WarehouseAudit
{
    public const int PageSize=12;
    private readonly string directory;
    private readonly int days;
    private List<WarehouseAuditEvent> cache;
    private DateTime prunedDay;
    public WarehouseAudit(string directory,int retentionDays)
    {
        if(retentionDays<1 || retentionDays>3650) throw new ArgumentOutOfRangeException(nameof(retentionDays));
        this.directory=directory; days=retentionDays;
    }
    private DateTime Cutoff => DateTime.UtcNow.Date.AddDays(1-days);
    private string FileName(WarehouseAuditEvent entry) => entry.TimeUtc.ToString("yyyyMMdd",CultureInfo.InvariantCulture)
        +"-"+entry.Revision.ToString("D20",CultureInfo.InvariantCulture)+"-"
        +Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(entry.RequestId)))+".json";
    public void Append(WarehouseAuditEvent entry)
    {
        Prune();
        if(entry.TimeUtc<Cutoff) return;
        Directory.CreateDirectory(directory);
        var path=Path.Combine(directory,FileName(entry));
        var bytes=JsonSerializer.SerializeToUtf8Bytes(entry);
        if(File.Exists(path))
        {
            if(!File.ReadAllBytes(path).SequenceEqual(bytes)) throw new InvalidDataException("Conflicting warehouse history event.");
            return;
        }
        using(var stream=new FileStream(path+".tmp",FileMode.Create,FileAccess.Write,FileShare.None))
        { stream.Write(bytes); stream.Flush(true); }
        File.Move(path+".tmp",path);
        cache?.Add(entry);
    }
    private void Prune()
    {
        if(prunedDay==DateTime.UtcNow.Date) return;
        if(Directory.Exists(directory)) foreach(var file in Directory.EnumerateFiles(directory,"*.json"))
        {
            var name=Path.GetFileName(file);
            // Only our own date/revision/hash filenames are subject to retention.
            if(name.Length==99 && name[8]=='-' && name[29]=='-'
                && DateTime.TryParseExact(name.Substring(0,8),"yyyyMMdd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var date)
                && date<Cutoff) File.Delete(file);
        }
        cache?.RemoveAll(x => x.TimeUtc<Cutoff);
        prunedDay=DateTime.UtcNow.Date;
    }
    public object Read(int page,string filter,string search,long throughRevision,Func<string,string> category=null)
    {
        if(page<0 || page>1000000 || throughRevision<0) throw new ArgumentException("Invalid history page.");
        if(filter!="all" && filter!="deposit" && filter!="withdraw" && filter!="arrange") throw new ArgumentException("Invalid history filter.");
        search=(search ?? "").Trim();
        if(search.Length>80) throw new ArgumentException("Search is limited to 80 characters.");
        Prune();
        if(cache==null)
        {
            var loaded=new List<WarehouseAuditEvent>();
            if(Directory.Exists(directory)) foreach(var file in Directory.EnumerateFiles(directory,"*.json"))
            {
                var entry=JsonSerializer.Deserialize<WarehouseAuditEvent>(File.ReadAllText(file))
                    ?? throw new InvalidDataException("Unreadable warehouse history event.");
                if(entry.TimeUtc>=Cutoff) loaded.Add(entry);
            }
            cache=loaded;
        }
        if(throughRevision==0) throughRevision=cache.Count==0 ? 0 : cache.Max(x=>x.Revision);
        bool Match(string value) => value?.Contains(search,StringComparison.OrdinalIgnoreCase)==true;
        var matching=cache.Where(x=>x.Revision<=throughRevision &&
            (filter=="all" ? x.Action=="deposit" || x.Action=="withdraw" : filter=="arrange" ? x.Action!="deposit" && x.Action!="withdraw" : x.Action==filter)
            && (search.Length==0 || Match(x.PlayerName) || Match(HistoryPresentation.ItemName(x.ItemName)) || x.Contents.Any(c=>Match(c.Name))))
            .OrderByDescending(x=>x.Revision).ToList();
        int pages=Math.Max(1,(matching.Count+PageSize-1)/PageSize);
        page=Math.Min(page,pages-1);
        // Technical profile/item/request IDs and contents remain server-side.
        return new {error="",page,pages,total=matching.Count,throughRevision,
            rows=matching.Skip(page*PageSize).Take(PageSize).Select(x=>new {
                timeUtc=x.TimeUtc,player=x.PlayerName,action=x.Action,item=HistoryPresentation.ItemName(x.ItemName),template=x.Template,category=category?.Invoke(x.Template) ?? "Other",count=x.Count,condition=x.Condition,fee=x.Fee,
                contents=x.Contents.Count,fromX=x.FromX,fromY=x.FromY,toX=x.ToX,toY=x.ToY
            })};
    }
}
