using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Heborian.Server.Modules.CommunityWarehouse;

public sealed class WarehouseEntry
{
    public string Id { get; set; } = "";
    public string Template { get; set; } = "";
    public string Name { get; set; } = "";
    public string Json { get; set; } = "";
    public List<string> ChildrenJson { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool Rotated { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Count { get; set; } = 1;
}
public sealed class WarehouseTransfer
{
    public WarehouseAuditEvent Audit { get; set; }
    public List<WarehouseProfileChange> ProfileChanges { get; set; }
    public List<string> SharedRemoved { get; set; }
    public List<WarehouseEntry> SharedUpdated { get; set; }
    public string RequestId { get; set; } = "";
    public string Profile { get; set; } = "";
    public string Direction { get; set; } = "";
    public WarehouseEntry Entry { get; set; } = new();
    public string ProfileItemJson { get; set; } = "";
    public string ProfileBeforeJson { get; set; }
    public string ProfileAfterJson { get; set; }
    public WarehouseEntry SharedRemainder { get; set; }
    public DateTime TimeUtc { get; set; } = DateTime.UtcNow;
}
public sealed class WarehouseProfileChange
{
    public string Id { get; set; } = "";
    public string Before { get; set; } = "";
    public string After { get; set; }
}
public sealed class WarehouseState
{
    public int Schema { get; set; } = 1;
    public long Revision { get; set; }
    public List<WarehouseEntry> Items { get; set; } = new();
    public List<WarehouseTransfer> History { get; set; } = new();
    public WarehouseTransfer Pending { get; set; }
    public List<WarehouseAuditEvent> AuditOutbox { get; set; } = new();
}

// Write-ahead journal: the target profile state is durable BEFORE either owner is changed.
// The caller owns one semaphore across validation, profile save and Commit.
public sealed class WarehouseStore
{
    // Global revision validation rejects stale API requests even after history pruning.
    public const int MaxHistory = 1024;
    public WarehouseState State { get; private set; } = new();
    private readonly string path;
    private readonly WarehouseAudit audit;
    private bool auditFailed;
    public int Rows { get; }
    public WarehouseStore(string path,int rows=WarehouseDimensions.Height,WarehouseAudit audit=null)
    {
        if(rows<1 || rows>WarehouseDimensions.MaxRows) throw new ArgumentOutOfRangeException(nameof(rows));
        this.path=path; Rows=rows; this.audit=audit;
    }
    public bool Visible(WarehouseEntry item) => item.Y>=0 && item.Height<=Rows && item.Y<=Rows-item.Height;
    public void Load()
    {
        if (File.Exists(path)) State = JsonSerializer.Deserialize<WarehouseState>(File.ReadAllText(path))
            ?? throw new InvalidDataException("Empty warehouse state.");
        if (State.Schema != 1 || State.Items == null || State.History == null || State.AuditOutbox == null)
            throw new InvalidDataException("Unsupported warehouse state.");
        var occupied = new bool[WarehouseDimensions.Width, WarehouseDimensions.MaxRows];
        var ids = new HashSet<string>();
        foreach (var item in State.Items)
        {
            if (!ids.Add(item.Id) || !Fits(occupied, item.X, item.Y, item.Width, item.Height))
                throw new InvalidDataException("Invalid warehouse placement or duplicate item.");
            Mark(occupied, item);
        }
        FlushAudit();
    }
    public bool Place(WarehouseEntry item)
    {
        var occupied = new bool[WarehouseDimensions.Width, Rows];
        foreach (var entry in State.Items) Mark(occupied, entry);
        for (int y = 0; y < Rows; y++) for (int x = 0; x < WarehouseDimensions.Width; x++)
            if (Fits(occupied, x, y, item.Width, item.Height)) { item.X = x; item.Y = y; return true; }
        return false;
    }
    public bool PlaceAt(WarehouseEntry item, int x, int y)
    {
        var occupied = new bool[WarehouseDimensions.Width,Rows];
        foreach(var entry in State.Items) Mark(occupied,entry);
        if(!Fits(occupied,x,y,item.Width,item.Height)) return false;
        item.X=x; item.Y=y;
        return true;
    }
    private static bool Fits(bool[,] cells, int x, int y, int w, int h)
    {
        if (x < 0 || y < 0 || w < 1 || h < 1 || w > WarehouseDimensions.Width || h > cells.GetLength(1) || x > WarehouseDimensions.Width-w || y > cells.GetLength(1)-h) return false;
        for (int dy = 0; dy < h; dy++) for (int dx = 0; dx < w; dx++) if(cells[x+dx,y+dy]) return false;
        return true;
    }
    // One-file atomic change: ownership and profile data never participate in relocation.
    public bool Reposition(string id, int x, int y, string requestId, string profile, bool? rotated=null,WarehouseAuditEvent auditEvent=null)
    {
        CheckAuditCapacity();
        if(State.Pending != null) throw new InvalidOperationException("Recovery required.");
        if(State.History.Any(t => t.RequestId == requestId)) throw new InvalidOperationException("Request already committed.");
        var source=State.Items.SingleOrDefault(item => item.Id == id);
        if(source == null || !Visible(source)) return false;
        var occupied=new bool[WarehouseDimensions.Width,Rows];
        foreach(var item in State.Items) if(item.Id != id) Mark(occupied,item);
        // Square items retain their existing orientation, including legacy saved items.
        bool orientation=source.Width == source.Height ? source.Rotated : rotated ?? source.Rotated;
        bool turn=orientation != source.Rotated;
        int width=turn ? source.Height : source.Width, height=turn ? source.Width : source.Height;
        if(!Fits(occupied,x,y,width,height)) return false;
        // Clone so moving an item cannot rewrite old audit entries sharing its reference.
        var next=CopyForWrite();
        var moved=next.Items.Single(item => item.Id == id);
        moved.X=x; moved.Y=y;
        moved.Width=width; moved.Height=height; moved.Rotated=orientation;
        next.History.Add(new WarehouseTransfer {RequestId=requestId,Profile=profile,Direction="move",Entry=moved});
        next.Revision++;
        if(auditEvent!=null)
        {
            auditEvent.FromX=source.X; auditEvent.FromY=source.Y; auditEvent.FromRotated=source.Rotated;
            auditEvent.ToX=x; auditEvent.ToY=y; auditEvent.ToRotated=orientation;
            auditEvent.Action=turn ? "rotate" : "move";
            QueueAudit(next,auditEvent);
        }
        SaveState(next);
        return true;
    }
    private static void Mark(bool[,] cells, WarehouseEntry item)
    {
        for(int y = item.Y; y < Math.Min(item.Y + item.Height,cells.GetLength(1)); y++) for(int x = item.X; x < item.X + item.Width; x++) cells[x,y] = true;
    }
    public void Begin(WarehouseTransfer transfer)
    {
        CheckAuditCapacity();
        if (State.Pending != null) throw new InvalidOperationException("Recovery required.");
        if (State.History.Any(x => x.RequestId == transfer.RequestId)) throw new InvalidOperationException("Request already committed. Refresh inventory.");
        var next=CopyForWrite();
        next.Pending = transfer;
        SaveState(next);
    }
    public void Commit()
    {
        if(State.Pending == null) throw new InvalidOperationException("No pending transfer.");
        var next=CopyForWrite();
        var transfer = next.Pending;
        if(transfer.Direction == "merge")
        {
            next.Items.RemoveAll(x => transfer.SharedRemoved.Contains(x.Id));
            next.Items.AddRange(transfer.SharedUpdated);
            next.History.Add(transfer); next.Revision++; next.Pending=null;
            QueueAudit(next,transfer.Audit); SaveState(next);
            return;
        }
        if(transfer.SharedRemainder != null)
        {
            next.Items.RemoveAll(x => x.Id == transfer.SharedRemainder.Id);
            next.Items.Add(transfer.SharedRemainder);
        }
        if (transfer.Direction == "deposit" || transfer.Direction == "split") next.Items.Add(transfer.Entry);
        else next.Items.RemoveAll(x => x.Id == transfer.Entry.Id);
        next.History.Add(transfer);
        next.Revision++;
        next.Pending = null;
        QueueAudit(next,transfer.Audit);
        SaveState(next);
    }
    private WarehouseState CopyForWrite() => JsonSerializer.Deserialize<WarehouseState>(JsonSerializer.Serialize(new WarehouseState {
        Schema=State.Schema, Revision=State.Revision, Items=State.Items,
        History=State.History.TakeLast(MaxHistory).ToList(), Pending=State.Pending, AuditOutbox=State.AuditOutbox
    }))!;
    private static void QueueAudit(WarehouseState next,WarehouseAuditEvent entry)
    {
        if(entry==null) return;
        entry.Revision=next.Revision; entry.TimeUtc=DateTime.UtcNow;
        next.AuditOutbox.Add(entry);
    }
    private void CheckAuditCapacity()
    {
        FlushAudit();
        if(State.AuditOutbox.Count>=1024) throw new IOException("History archive unavailable. Contact the server administrator.");
    }
    public void FlushAudit()
    {
        if(audit==null || State.AuditOutbox.Count==0) return;
        try
        {
            foreach(var entry in State.AuditOutbox) audit.Append(entry);
            // Disk may still contain this outbox until the next warehouse save.
            // Replaying it after a crash is safe because archive writes are idempotent.
            State.AuditOutbox.Clear(); auditFailed=false;
        }
        catch(Exception error)
        {
            if(!auditFailed) Console.WriteLine("[CommunityWarehouse] History archive delayed; durable outbox retained: "+error.Message);
            auditFailed=true;
        }
    }
    private void SaveState(WarehouseState next)
    {
        if(next.History.Count>MaxHistory) next.History.RemoveRange(0,next.History.Count-MaxHistory);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(next);
        using(var stream = new FileStream(path + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
        { stream.Write(bytes); stream.Flush(true); }
        if (File.Exists(path)) File.Replace(path + ".tmp", path, path + ".bak");
        else File.Move(path + ".tmp", path);
        // Keep in-memory ownership and the pending journal intact until disk commit succeeds.
        State=next;
        FlushAudit();
    }
}
