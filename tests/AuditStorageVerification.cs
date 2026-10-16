using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Heborian.Server.Modules.CommunityWarehouse;

public static class AuditStorageVerification
{
    public static void Run(string directory)
    {
        Directory.CreateDirectory(directory);
        var failures = new List<string>();
        void Check(bool value, string label)
        {
            Console.WriteLine((value ? "PASS: " : "FAIL: ") + label);
            if (!value) failures.Add(label);
        }
        string NewPath() => Path.Combine(directory, Guid.NewGuid().ToString("N") + ".json");
        WarehouseTransfer Deposit(string id) => new WarehouseTransfer {
            RequestId=id, Direction="deposit", Entry=new WarehouseEntry {Id=id,Width=1,Height=1,Json="{}"}
        };

        var beginPath=NewPath();
        var begin=new WarehouseStore(beginPath);
        using (var locked=new FileStream(beginPath+".tmp",FileMode.Create,FileAccess.Write,FileShare.None))
        {
            bool failed=false;
            try { begin.Begin(Deposit("failed-begin")); } catch(IOException) { failed=true; }
            Check(failed && begin.State.Pending==null && begin.State.Revision==0,
                "failed journal write leaves no in-memory pending transaction");
        }

        var commitPath=NewPath();
        var commit=new WarehouseStore(commitPath);
        commit.Begin(Deposit("commit-recovery"));
        string durable=File.ReadAllText(commitPath);
        using(var locked=new FileStream(commitPath+".tmp",FileMode.OpenOrCreate,FileAccess.Write,FileShare.None))
        {
            bool failed=false;
            try { commit.Commit(); } catch(IOException) { failed=true; }
            Check(failed && commit.State.Pending!=null && commit.State.Items.Count==0
                && commit.State.History.Count==0 && commit.State.Revision==0 && File.ReadAllText(commitPath)==durable,
                "failed commit preserves durable journal and matching in-memory ownership");
        }
        var recovered=new WarehouseStore(commitPath); recovered.Load(); recovered.Commit();
        Check(recovered.State.Pending==null && recovered.State.Items.Count==1 && recovered.State.Revision==1,
            "failed commit recovers exactly once from disk");

        var movePath=NewPath();
        var moving=new WarehouseStore(movePath); moving.Begin(Deposit("moving")); moving.Commit();
        durable=File.ReadAllText(movePath);
        using(var locked=new FileStream(movePath+".tmp",FileMode.OpenOrCreate,FileAccess.Write,FileShare.None))
        {
            bool failed=false;
            try { moving.Reposition("moving",1,0,"failed-move","test"); } catch(IOException) {failed=true;}
            Check(failed && moving.State.Items.Single().X==0 && moving.State.Revision==1 && File.ReadAllText(movePath)==durable,
                "failed relocation preserves original position on disk and in memory");
        }

        for(int i=0;i<1200;i++) moving.State.History.Add(new WarehouseTransfer {RequestId="old-"+i});
        moving.Reposition("moving",1,0,"newest","test");
        var trimmed=new WarehouseStore(movePath); trimmed.Load();
        Check(trimmed.State.History.Count<=1024 && trimmed.State.History.Last().RequestId=="newest"
            && trimmed.State.Items.Single().Id=="moving" && trimmed.State.Revision==2,
            "operation history is bounded without discarding stored items or changing revision semantics");
        bool replay=false;
        try {trimmed.Begin(new WarehouseTransfer {RequestId="newest"});} catch(InvalidOperationException) {replay=true;}
        Check(replay,"recent committed request remains protected against replay after history trimming");

        if(failures.Count>0) throw new Exception(string.Join("; ",failures));
    }
}
