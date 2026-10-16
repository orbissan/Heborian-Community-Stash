using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EFT;
using BepInEx.Logging;
using Heborian.Client.Modules.HeborianMenu;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SPT.Common.Http;
using UnityEngine;

namespace Heborian.Client.Modules.CommunityWarehouse
{
    public sealed class WarehouseClientModule : IClientModule
    {
        public string Id => "CommunityWarehouse";
        private WarehouseController controller;
        public void Initialize(ClientModuleContext context)
        {
            var cells=context.Assets.LoadTexture("cell_full_border.png");
            cells.wrapMode=TextureWrapMode.Repeat;
            cells.filterMode=FilterMode.Bilinear;
            var profileIcon=context.Assets.LoadTexture("icon_profile.png");
            profileIcon.filterMode=FilterMode.Bilinear;
            var stashIcon=context.Assets.LoadTexture("icon_stash.png");
            stashIcon.filterMode=FilterMode.Bilinear;
            controller=new WarehouseController(context.Log,cells,profileIcon,stashIcon);
            HeborianMenuView.Content.Opened += controller.Open;
            HeborianMenuView.Content.Updated += controller.Tick;
            HeborianMenuView.Content.Closed += controller.Close;
            context.Log.LogInfo("[CommunityWarehouse] Bound to Heborian screen lifecycle.");
            if(HeborianMenuView.AnyOpen) controller.Open();
        }
        public void Dispose()
        {
            if(controller == null) return;
            HeborianMenuView.Content.Opened -= controller.Open;
            HeborianMenuView.Content.Updated -= controller.Tick;
            HeborianMenuView.Content.Closed -= controller.Close;
            controller.Dispose();
            controller=null;
        }
    }
    public sealed class WarehouseItem
    {
        public string id, template, name;
        public int x, y, width, height, count, maxStack;
        public bool rotated;
        public bool transferable, canReceive;
        public JArray tree;
        public JObject upd;
    }
    public sealed class WarehouseFees { public bool enabled; public string currency; public int deposit,withdrawal; public long? balance; }
    public sealed class WarehouseSnapshot
    {
        public long revision;
        public int width, height, personalWidth, personalHeight;
        public bool historyAvailable;
        public WarehouseFees fees;
        public string error;
        public List<WarehouseItem> items = new List<WarehouseItem>();
        public List<WarehouseItem> blocked = new List<WarehouseItem>();
        public List<WarehouseItem> personal = new List<WarehouseItem>();
    }
    internal sealed class WarehouseController : IDisposable
    {
        private WarehouseSnapshot snapshot;
        private Task<string> refresh;
        private float nextRefresh;
        private float messageUntil;
        private bool busy, awaitingSnapshot, opened, disposed;
        private float transferStarted;
        private bool transferWaitReported;
        private string pendingRequest;
        private string message = "", selectedId;
        private bool selectedPersonal;
        private WarehousePanel panel;
        private ManualLogSource log;
        private string lastReadError;

        private readonly Texture2D cells,profileIcon,stashIcon;
        public WarehouseController(ManualLogSource logger,Texture2D cells,Texture2D profileIcon,Texture2D stashIcon) { log=logger; this.cells=cells; this.profileIcon=profileIcon; this.stashIcon=stashIcon; }

        public void Open()
        {
            if(disposed) return;
            log.LogInfo("[CommunityWarehouse] Screen opened; creating warehouse content.");
            opened=true;
            nextRefresh=0;
            selectedId=null;
            HeborianMenuView.SetContentStatus("Opening community warehouse...");
            var root=HeborianMenuView.ActiveContentRoot;
            if(panel == null || !panel.IsAttachedTo(root))
            {
                panel?.Dispose();
                panel=new WarehousePanel(root,HeborianMenuView.ActiveTextTemplate,cells,profileIcon,stashIcon,
                    (id, personal) => { selectedId=id; selectedPersonal=personal; },
                    (id, personal, x, y, rotated, amount) => {
                        selectedId=id; selectedPersonal=personal;
                        Transfer(personal ? "deposit" : x.HasValue ? "move" : "withdraw",x,y,rotated,amount);
                    }, () => { nextRefresh=0; }, (id,personal,target,targetPersonal,amount) => {
                        selectedId=id; selectedPersonal=personal;
                        Transfer(personal ? "deposit" : "withdraw",amount:amount,targetId:target,targetPersonal:targetPersonal);
                    });
            }
            Canvas.ForceUpdateCanvases();
            panel.Resize();
            panel.Render(snapshot,message,busy,selectedId,selectedPersonal);
            HeborianMenuView.SetContentStatus(null);
            log.LogInfo("[CommunityWarehouse] Canvas UI created inside Heborian screen (" + WarehouseDimensions.Width + " x " + WarehouseDimensions.Height + ").");
            log.LogInfo("[CommunityWarehouse] Content area: " + root.rect.width + " x " + root.rect.height
                + "; active=" + root.gameObject.activeInHierarchy + "; scale=" + root.lossyScale);
        }
        public void Close()
        {
            opened=false;
            // Personal inventory may change on other screens without changing the shared revision.
            snapshot=null;
            refresh=null;
            panel?.Dispose();
            panel=null;
            selectedId=null;
        }
        public void Tick()
        {
            if(disposed || !opened || panel == null) return;
            if(busy && !awaitingSnapshot && !transferWaitReported && Time.unscaledTime-transferStarted>20)
            {
                transferWaitReported=true;
                message="Waiting for game inventory confirmation. If this persists, reconnect before moving more items.";
                log.LogWarning("[CommunityWarehouse] Inventory callback pending for request " + pendingRequest
                    + ". Do not retry automatically; server may already have committed the transfer.");
            }
            if(panel.HasModal) { panel.TickModal(); return; }
            // Do not recycle native item views or replace the drag's revision mid-gesture.
            if(panel.IsDragging) { panel.UpdateDrag(); return; }
            if(refresh != null && refresh.IsCompleted)
            {
                try
                {
                    var result = JsonConvert.DeserializeObject<WarehouseSnapshot>(refresh.GetAwaiter().GetResult());
                    if(result == null || !string.IsNullOrEmpty(result.error))
                    { ReadFailed(result?.error ?? "Invalid server response."); }
                    else if(result.width != WarehouseDimensions.Width || (result.height<1 || result.height>WarehouseDimensions.MaxRows) || result.items == null || result.personal == null || result.personalWidth<1 || result.personalHeight<1)
                    { ReadFailed("Unsupported warehouse protocol."); }
                    else
                    {
                        snapshot=result; lastReadError=null;
                        if(awaitingSnapshot) { awaitingSnapshot=false; busy=false; }
                        if(!busy && Time.unscaledTime >= messageUntil) message="";
                    }
                }
                catch(Exception error) { ReadFailed("Connection error: " + error.Message); }
                refresh=null;
            }
            panel.Resize();
            panel.Render(snapshot,message,busy,selectedId,selectedPersonal);
            if((!busy || awaitingSnapshot) && refresh==null && Time.unscaledTime >= nextRefresh)
            {
                nextRefresh=Time.unscaledTime+2;
                try { refresh=RequestHandler.GetJsonAsync("/heborian/warehouse"); }
                catch(Exception error) { ReadFailed("Connection error: " + error.Message); }
            }
        }
        private void ReadFailed(string error)
        {
            if(awaitingSnapshot) { awaitingSnapshot=false; busy=false; }
            snapshot=null; message=error;
            if(lastReadError != error) log?.LogWarning("[CommunityWarehouse] " + error);
            lastReadError=error;
        }
        private void Transfer(string direction, int? x=null, int? y=null, bool? rotated=null, int? amount=null,string targetId=null,bool targetPersonal=false)
        {
            if(busy || snapshot==null || selectedId==null) return;
            var app=UnityEngine.Resources.FindObjectsOfTypeAll<TarkovApplication>().FirstOrDefault(x => x.gameObject.scene.IsValid());
            var session=app?.GetClientBackEndSession();
            if(session==null) { message="Game session unavailable."; return; }
            // Use the game's inventory queue and profile-delta handlers, never mutate client items directly.
            var operation=new Dictionary<string, object> {
                {"Action","HeborianWarehouse"}, {"requestId",Guid.NewGuid().ToString("N")},
                {"direction",direction}, {"itemId",selectedId}, {"revision",snapshot.revision}
            };
            if(x.HasValue) { operation["x"]=x.Value; operation["y"]=y.Value; }
            if(rotated.HasValue && direction != "withdraw") operation["rotated"]=rotated.Value;
            if(amount.HasValue) operation["amount"]=amount.Value;
            if(targetId != null) { operation["targetId"]=targetId; operation["targetPersonal"]=targetPersonal; }
            // A read started before this mutation must not repopulate an obsolete snapshot.
            refresh=null;
            busy=true; message="";
            transferStarted=Time.unscaledTime; transferWaitReported=false;
            pendingRequest=(string)operation["requestId"];
            // Capture before callbacks/refresh can dispose or replace the source view.
            string itemSound=panel?.ItemSound(selectedId,selectedPersonal);
            log.LogInfo("[CommunityWarehouse] Sending " + direction + " item=" + selectedId + " request=" + pendingRequest);
            try
            {
                session.SendOperationRightNow(operation, result => {
                    log.LogInfo("[CommunityWarehouse] Inventory callback request=" + operation["requestId"] + " success=" + result.Succeed);
                    if(disposed) return;
                    if(result.Succeed) NativeWarehouseItem.PlaySound(itemSound,EFT.UI.EInventorySoundType.drop);
                    // Keep the previous views until the authoritative snapshot arrives.
                    // Still block gestures so no operation can use the old revision.
                    awaitingSnapshot=true; busy=true; selectedId=null;
                    message=result.Succeed ? "" : "Operation failed: " + result.Error;
                    messageUntil=Time.unscaledTime+8;
                    nextRefresh=0;
                });
            }
            catch(Exception error) { busy=false; snapshot=null; message="Transfer error: " + error.Message; messageUntil=Time.unscaledTime+8; nextRefresh=0; }
        }
        public void Dispose() { disposed=true; Close(); }
    }
}



