using System;
using System.Collections.Generic;
using System.Linq;
using EFT.UI;
using HarmonyLib;
using Heborian.Client.Modules.HeborianMenu;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Heborian.Client.Modules.CommunityWarehouse
{
    internal sealed partial class WarehousePanel : IDisposable
    {
        private const float Cell=60;
        private const float ScrollbarWidth=14;
        private readonly Texture2D cells,profileIcon,stashIcon;
        private readonly RectTransform parent,root,shared,personalViewport,personalContent,personalBackground;
        private readonly RectTransform sharedViewport,sharedBar,personalBar,sharedBackground;
        private readonly TMP_Text template,status,sharedTitle,feeLabel,personalTitle,balanceLabel;
        private readonly RectTransform balanceHeader,feeHeader;
        private readonly Button refresh;
        private readonly Button historyButton;
        private readonly Action<string,bool> select;
        private readonly Action<string,bool,int?,int?,bool?,int?> move;
        private readonly Action<string,bool,string,bool,int?> merge;
        private WarehouseItemInput mergeTarget;
        private readonly List<Tile> tiles=new List<Tile>();
        private readonly List<PositionedItem> personalItems=new List<PositionedItem>();
        private int renderedFirstRow=-1;
        private int personalColumns=10,personalRows=1,sharedRows=WarehouseDimensions.Height;
        private int renderedVisibleRows=-1,lastScrollFrame=-1;
        private readonly Image dropHighlight;
        private readonly ScrollRect scroll;
        private readonly ScrollRect sharedScroll;
        private WarehouseSnapshot lastSnapshot;
        private string renderError,modalError;
        private bool busy,disposed;
        private WarehouseItemInput dragging;
        private RectTransform dragGhost;
        private Image dragIcon;
        private Vector2 grabOffset;
        private PointerEventData dragEvent;
        private int? dropX,dropY;
        private bool validDrop;
        private bool dragRotated;
        private int dragWidth,dragHeight,lastRotationFrame=-1;
        private int grabColumn,grabRow;
        private NativeWarehouseItem draggedNative;
        private SplitDialog splitDialog;
        private GameObject splitOverlay;
        private bool splitRequested;
        private Func<bool> cancelModal;
        public bool HasModal => splitOverlay != null || historyOverlay != null;
        public bool IsDragging => dragging != null;
        public string ItemSound(string id,bool personal)
        {
            return tiles.Find(tile => tile.Input.Data.id==id && tile.Input.Personal==personal)?.Native?.SoundKey;
        }
        private sealed class Tile
        {
            public WarehouseItemInput Input;
            public NativeWarehouseItem Native;
        }
        private sealed class PositionedItem { public WarehouseItem Item; public int X,Y; }

        public WarehousePanel(RectTransform parent,TMP_Text template,Texture2D cells,Texture2D profileIcon,Texture2D stashIcon,Action<string,bool> select,
            Action<string,bool,int?,int?,bool?,int?> move,UnityAction refreshAction,Action<string,bool,string,bool,int?> merge)
        {
            if(parent == null || template == null || template.font == null)
                throw new InvalidOperationException("Menu content or native font unavailable.");
            this.parent=parent; this.template=template; this.select=select; this.move=move; this.cells=cells;
            this.merge=merge; this.profileIcon=profileIcon; this.stashIcon=stashIcon;
            root=Rect("Heborian.Warehouse",parent,0,0,1510,800);
            root.anchorMin=root.anchorMax=root.pivot=new Vector2(0,1);
            root.anchoredPosition=Vector2.zero;
            root.SetAsFirstSibling();
            root.gameObject.SetActive(false);
            try
            {
                sharedTitle=Header("COMMUNITY STASH   " + WarehouseDimensions.Width + " x " + WarehouseDimensions.Height,8);
                feeHeader=Rect("TransferFees",root,320,8,350,30);
                Paint(feeHeader,new Color(.72f,.73f,.68f,1)).raycastTarget=false;
                feeLabel=Label(feeHeader,"",8,3,334,24,16);
                feeLabel.enableAutoSizing=true; feeLabel.fontSizeMin=10; feeLabel.fontSizeMax=16;
                feeLabel.enableWordWrapping=false; feeLabel.alignment=TextAlignmentOptions.Midline;
                feeLabel.color=new Color(.08f,.09f,.09f,1); feeLabel.fontStyle=FontStyles.Bold;
                feeHeader.gameObject.SetActive(false);
                historyButton=Button(root,"HISTORY",682,8,110,30,OpenHistory);
                historyButton.gameObject.SetActive(false);
                // Long fee/recovery errors need a readable full-width row, not the
                // narrow space left after the header cells and HISTORY button.
                status=Label(root,"",8,42,960,32,14);
                status.enableAutoSizing=true; status.fontSizeMin=11; status.fontSizeMax=14;
                status.richText=false;
                personalTitle=Header("YOUR STASH",1008,260);
                balanceHeader=Rect("PersonalBalance",root,1278,8,210,30);
                Paint(balanceHeader,new Color(.72f,.73f,.68f,1)).raycastTarget=false;
                balanceLabel=Label(balanceHeader,"",8,3,194,24,18);
                balanceLabel.enableAutoSizing=true; balanceLabel.fontSizeMin=10; balanceLabel.fontSizeMax=18;
                balanceLabel.enableWordWrapping=false;
                balanceLabel.alignment=TextAlignmentOptions.Midline;
                balanceLabel.color=new Color(.08f,.09f,.09f,1); balanceLabel.fontStyle=FontStyles.Bold;
                balanceHeader.gameObject.SetActive(false);
                sharedViewport=Rect("SharedViewport",root,8,46,Cell*WarehouseDimensions.Width,Cell*8);
                Paint(sharedViewport,new Color(.04f,.04f,.04f,1));
                sharedViewport.gameObject.AddComponent<RectMask2D>();
                shared=Rect("SharedGrid",sharedViewport,0,0,Cell*WarehouseDimensions.Width,Cell*WarehouseDimensions.Height);
                sharedBackground=Rect("Cells",shared,0,0,Cell*WarehouseDimensions.Width,Cell*sharedRows);
                DrawGrid(sharedBackground,sharedRows,WarehouseDimensions.Width);
                sharedScroll=sharedViewport.gameObject.AddComponent<ScrollRect>();
                sharedScroll.viewport=sharedViewport; sharedScroll.content=shared;
                sharedScroll.horizontal=false; sharedScroll.vertical=true;
                sharedScroll.movementType=ScrollRect.MovementType.Clamped;
                sharedScroll.scrollSensitivity=Cell; sharedScroll.inertia=false;
                sharedBar=CreateScrollbar(sharedScroll,976);
                personalViewport=Rect("PersonalViewport",root,1008,46,Cell*8,Cell*8);
                Paint(personalViewport,new Color(.04f,.04f,.04f,1));
                personalViewport.gameObject.AddComponent<RectMask2D>();
                personalContent=Rect("PersonalGrid",personalViewport,0,0,Cell*8,Cell*8);
                personalBackground=Rect("Cells",personalContent,0,0,Cell*8,Cell*8);
                DrawGrid(personalBackground,8);
                scroll=personalViewport.gameObject.AddComponent<ScrollRect>();
                scroll.viewport=personalViewport; scroll.content=personalContent;
                scroll.horizontal=false; scroll.vertical=true;
                scroll.movementType=ScrollRect.MovementType.Clamped;
                scroll.scrollSensitivity=Cell; scroll.inertia=false;
                personalBar=CreateScrollbar(scroll,1496);
                dropHighlight=Paint(Rect("DropHighlight",root,0,0,Cell,Cell),Color.clear);
                dropHighlight.raycastTarget=false;
                dropHighlight.gameObject.SetActive(false);
                refresh=Button(root,"REFRESH",1378,8,110,30,() => {
                    renderError=null; lastSnapshot=null; refreshAction();
                });
                Resize(); root.gameObject.SetActive(true);
            }
            catch { Dispose(); throw; }
        }
        private TMP_Text Header(string text,float x,float width=300)
        {
            var header=Rect("Header",root,x,8,width,30);
            Paint(header,new Color(.72f,.73f,.68f,1)).raycastTarget=false;
            var icon=Rect("StashIcon",header,9,4,22,22).gameObject.AddComponent<RawImage>();
            icon.texture=stashIcon; icon.color=new Color(.08f,.09f,.09f,1); icon.raycastTarget=false;
            var label=Label(header,text,39,4,width-49,24,18);
            label.enableAutoSizing=true; label.fontSizeMin=width>300 ? 12 : 15; label.fontSizeMax=18;
            label.enableWordWrapping=false;
            label.color=new Color(.08f,.09f,.09f,1); label.fontStyle=FontStyles.Bold;
            return label;
        }
        public bool IsAttachedTo(RectTransform target) => root != null && parent != null && parent == target;
        private static string CurrencySymbol(string currency) => currency=="RUB" ? "\u20BD"
            : currency=="EUR" ? "\u20AC" : currency=="USD" ? "$" : currency;
        public void Resize()
        {
            if(IsDragging) return;
            // Inherit EFT's Canvas scale, but never enlarge cells to fill the screen.
            // The authored 60-unit layout maps to the game's actual cell pitch.
            float nativeScale=NativeWarehouseItem.CellPitch/Cell;
            float scale=Mathf.Max(.01f,Mathf.Min(nativeScale,parent.rect.width/(1030+Cell*personalColumns)));
            root.localScale=Vector3.one*scale;
            // The parent already excludes the bottom taskbar: keep only an 8-unit bottom gap.
            float height=Mathf.Max(Cell,parent.rect.height/scale-54);
            root.sizeDelta=new Vector2(1030+Cell*personalColumns,parent.rect.height/scale);
            float statusHeight=string.IsNullOrEmpty(status.text) ? 0 : 34;
            sharedViewport.anchoredPosition=new Vector2(8,-46-statusHeight);
            sharedBar.anchoredPosition=new Vector2(976,-46-statusHeight);
            sharedViewport.sizeDelta=new Vector2(Cell*WarehouseDimensions.Width,Mathf.Min(Cell*sharedRows,Mathf.Max(Cell,height-statusHeight)));
            personalViewport.sizeDelta=new Vector2(Cell*personalColumns,height);
            personalBar.anchoredPosition=new Vector2(1016+Cell*personalColumns,-46);
            ((RectTransform)refresh.transform).anchoredPosition=new Vector2(898+Cell*personalColumns,-8);
            // Reserve the refresh button and equal ten-unit gaps between header cells.
            float headerSpace=Cell*personalColumns-120;
            float titleWidth=(headerSpace-10)*.55f;
            float balanceWidth=headerSpace-10-titleWidth;
            ((RectTransform)personalTitle.transform.parent).sizeDelta=new Vector2(titleWidth,30);
            personalTitle.rectTransform.sizeDelta=new Vector2(titleWidth-49,24);
            personalTitle.fontSizeMin=10;
            balanceHeader.anchoredPosition=new Vector2(1008+titleWidth+10,-8);
            balanceHeader.sizeDelta=new Vector2(balanceWidth,30);
            balanceLabel.rectTransform.sizeDelta=new Vector2(balanceWidth-16,24);
            sharedBar.sizeDelta=new Vector2(ScrollbarWidth,sharedViewport.rect.height); personalBar.sizeDelta=new Vector2(ScrollbarWidth,height);
            shared.anchoredPosition=new Vector2(0,Mathf.Clamp(shared.anchoredPosition.y,0,Mathf.Max(0,shared.rect.height-sharedViewport.rect.height)));
            personalContent.anchoredPosition=new Vector2(0,Mathf.Clamp(personalContent.anchoredPosition.y,0,Mathf.Max(0,personalContent.rect.height-height)));
        }
        private RectTransform CreateScrollbar(ScrollRect owner,float x)
        {
            var bar=Rect("Scrollbar",root,x,46,ScrollbarWidth,Cell*8);
            Paint(bar,new Color(.08f,.09f,.09f,1));
            var handle=Rect("Handle",bar,0,0,0,0);
            // Scrollbar drives the anchors to encode the visible fraction. A fixed
            // sizeDelta would be ADDED to that fraction, extending the handle offscreen.
            handle.anchorMin=Vector2.zero; handle.anchorMax=Vector2.one;
            handle.pivot=new Vector2(.5f,.5f);
            handle.offsetMin=handle.offsetMax=Vector2.zero;
            var image=Paint(handle,new Color(.72f,.75f,.73f,1));
            var scrollbar=bar.gameObject.AddComponent<Scrollbar>();
            scrollbar.handleRect=handle; scrollbar.targetGraphic=image;
            scrollbar.direction=Scrollbar.Direction.BottomToTop; owner.verticalScrollbar=scrollbar;
            scrollbar.navigation=new Navigation {mode=Navigation.Mode.None};
            return bar;
        }
        public void Render(WarehouseSnapshot snapshot,string message,bool busy,string selectedId,bool personal)
        {
            this.busy=busy;
            var feeInfo=snapshot?.fees;
            personalTitle.text=snapshot==null ? "YOUR STASH"
                : "YOUR STASH  "+snapshot.personalWidth+" x "+snapshot.personalHeight;
            balanceHeader.gameObject.SetActive(feeInfo?.balance!=null);
            balanceLabel.text=feeInfo?.balance!=null
                ? CurrencySymbol(feeInfo.currency)
                    +"  "+feeInfo.balance.Value.ToString("N0") : "";
            string feeText=feeInfo?.enabled==true ? "Deposit: "+CurrencySymbol(feeInfo.currency)+" "+feeInfo.deposit.ToString("N0")
                +" | Withdrawal: "+CurrencySymbol(feeInfo.currency)+" "+feeInfo.withdrawal.ToString("N0") : "";
            feeLabel.text=feeText;
            bool showFees=feeInfo?.enabled==true;
            feeHeader.gameObject.SetActive(showFees);
            ((RectTransform)historyButton.transform).anchoredPosition=new Vector2(showFees ? 682 : 320,-8);
            status.text=renderError ?? modalError ?? message;
            Resize();
            if(IsDragging) return;
            if(!ReferenceEquals(lastSnapshot,snapshot))
            {
                try { renderError=null; Rebuild(snapshot); }
                catch(Exception error)
                {
                    ClearTiles(); renderError="Inventory view unavailable: " + error.Message + " (see client log).";
                    throw;
                }
                finally { lastSnapshot=snapshot; }
            }
            SyncVisiblePersonal();
            foreach(var tile in tiles)
            {
                bool selected=tile.Input.Data.id==selectedId && tile.Input.Personal==personal;
                tile.Native?.Highlight(selected, tile.Input.Hovered && (!tile.Input.Personal || tile.Input.Data.transferable));
            }
            refresh.interactable=!busy;
            historyButton.gameObject.SetActive(snapshot?.historyAvailable==true);
            historyButton.interactable=!busy;
        }
        private void Rebuild(WarehouseSnapshot snapshot)
        {
            personalItems.Clear(); renderedFirstRow=-1;
            if(snapshot==null) { ClearTiles(); return; }
            sharedRows=snapshot?.height ?? sharedRows;
            sharedTitle.text="COMMUNITY STASH   " + WarehouseDimensions.Width + " x " + sharedRows;
            shared.sizeDelta=new Vector2(Cell*WarehouseDimensions.Width,Cell*sharedRows);
            ResizeGrid(sharedBackground,sharedRows,WarehouseDimensions.Width);
            personalColumns=snapshot?.personalWidth ?? personalColumns;
            personalRows=snapshot?.personalHeight ?? personalRows;
            if(snapshot != null)
            {
                ReconcileTiles(snapshot.items,false,shared);
                foreach(var item in snapshot.personal)
                    personalItems.Add(new PositionedItem {Item=item,X=item.x,Y=item.y});
            }
            personalContent.sizeDelta=new Vector2(Cell*personalColumns,Cell*personalRows);
            ResizeGrid(personalBackground,personalRows,personalColumns);
            Resize();
            SyncVisiblePersonal();
        }
        private void SyncVisiblePersonal()
        {
            if(renderError != null) return;
            int first=Math.Max(0,Mathf.FloorToInt(personalContent.anchoredPosition.y/Cell)-1);
            int visibleRows=Mathf.CeilToInt(personalViewport.rect.height/Cell)+3;
            if(first==renderedFirstRow && visibleRows==renderedVisibleRows) return;
            renderedVisibleRows=visibleRows;
            var visible=personalItems.FindAll(p => p.Y+p.Item.height>first && p.Y<first+visibleRows);
            try { ReconcileTiles(visible.Select(p => p.Item),true,personalContent); }
            catch(Exception error) { renderError="Inventory view unavailable: " + error.Message + " (see client log)."; throw; }
            finally { renderedFirstRow=first; }
        }
        private void ReconcileTiles(IEnumerable<WarehouseItem> desired,bool personal,RectTransform owner)
        {
            var pending=desired.ToDictionary(item => item.id);
            for(int i=tiles.Count-1;i>=0;i--)
            {
                var tile=tiles[i];
                if(tile.Input.Personal != personal) continue;
                // Scrolling while dragging must preserve the source icon, even offscreen.
                if(tile.Input==dragging) { pending.Remove(tile.Input.Data.id); continue; }
                if(pending.TryGetValue(tile.Input.Data.id,out var next) && WarehouseItemPresentation.Same(tile.Input.Data,next))
                {
                    tile.Input.Data=next; pending.Remove(next.id);
                }
                else { ReleaseTile(tile); tiles.RemoveAt(i); }
            }
            foreach(var item in pending.Values) Add(item,personal,owner,item.x,item.y);
        }
        private void ResizeGrid(RectTransform owner,int rows,int columns)
        {
            var size=new Vector2(Cell*columns,Cell*rows);
            if(owner.sizeDelta==size) return;
            owner.sizeDelta=size;
            foreach(Transform child in owner) { child.gameObject.SetActive(false); UnityEngine.Object.Destroy(child.gameObject); }
            DrawGrid(owner,rows,columns);
        }
        private void Add(WarehouseItem data,bool personal,RectTransform owner,int x,int y)
        {
            var tile=Rect("WarehouseItem",owner,x*Cell,y*Cell,data.width*Cell,data.height*Cell);
            Paint(tile,Color.clear);
            var group=tile.gameObject.AddComponent<CanvasGroup>();
            if(personal && !data.transferable) group.alpha=.35f;
            var input=tile.gameObject.AddComponent<WarehouseItemInput>();
            input.Panel=this; input.Data=data; input.Personal=personal;
            try { tiles.Add(new Tile {Input=input,Native=new NativeWarehouseItem(data,tile,Cell)}); }
            catch(Exception error)
            {
                input.Invalid=true;
                tiles.Add(new Tile {Input=input,Native=null});
                Paint(Rect("InvalidItem",tile,0,0,data.width*Cell,data.height*Cell),new Color(.35f,.12f,.12f,.8f));
                Label(tile,"Unavailable item",4,4,data.width*Cell-8,data.height*Cell-8,14);
                UnityEngine.Debug.LogError("[CommunityWarehouse] Cannot display item " + data.id + ": " + error);
            }
        }
        private void DrawGrid(RectTransform owner,int rows,int columns=8)
        {
            // One quad repeats the supplied full-border tile once per cell, also when scrolling.
            var fill=Rect("CellTexture",owner,0,0,Cell*columns,Cell*rows).gameObject.AddComponent<RawImage>();
            fill.texture=cells; fill.color=Color.white;
            fill.uvRect=new Rect(0,0,columns,rows); fill.raycastTarget=false;
        }
        private void Frame(Transform owner,float x,float y,float width,float height)
        {
            var color=new Color(.32f,.34f,.34f,.85f);
            Paint(Rect("TopBorder",owner,x,y,width,1),color).raycastTarget=false;
            Paint(Rect("BottomBorder",owner,x,y+height-1,width,1),color).raycastTarget=false;
            Paint(Rect("LeftBorder",owner,x,y,1,height),color).raycastTarget=false;
            Paint(Rect("RightBorder",owner,x+width-1,y,1,height),color).raycastTarget=false;
        }
        public void Click(WarehouseItemInput input,bool quick)
        {
            if(input.Invalid || (input.Personal && !input.Data.transferable) || busy || renderError != null || lastSnapshot == null || IsDragging || HasModal) return;
            select(input.Data.id,input.Personal);
            if(quick) move(input.Data.id,input.Personal,null,null,null,null);
        }
        public bool BeginDrag(WarehouseItemInput input,PointerEventData e)
        {
            try { return BeginDragCore(input,e); }
            catch(Exception error) { FailDrag(error); return false; }
        }
        private bool BeginDragCore(WarehouseItemInput input,PointerEventData e)
        {
            if(input.Invalid || (input.Personal && !input.Data.transferable) || busy || renderError != null || lastSnapshot == null || IsDragging || HasModal) return false;
            select(input.Data.id,input.Personal);
            dragging=input; dragEvent=e;
            splitRequested=input.Data.count>1 && ControlHeld;
            dragRotated=input.Data.rotated; dragWidth=input.Data.width; dragHeight=input.Data.height;
            draggedNative=tiles.Find(tile => tile.Input == input).Native;
            CenterDragOnCursor();
            dragGhost=Rect("DraggedIcon",root,0,0,dragWidth*Cell,dragHeight*Cell);
            dragGhost.SetAsLastSibling();
            var iconRect=Rect("Image",dragGhost,0,0,0,0);
            iconRect.anchorMin=iconRect.anchorMax=iconRect.pivot=new Vector2(.5f,.5f);
            iconRect.anchoredPosition=Vector2.zero;
            dragIcon=Paint(iconRect,Color.white); dragIcon.raycastTarget=false;
            scroll.enabled=false; sharedScroll.enabled=false;
            DragCore(e);
            NativeWarehouseItem.PlaySound(draggedNative.SoundKey,EInventorySoundType.pickup);
            return true;
        }
        public void Drag(PointerEventData e)
        {
            try { DragCore(e); }
            catch(Exception error) { FailDrag(error); }
        }
        private void DragCore(PointerEventData e)
        {
            dragEvent=e;
            if(!IsDragging) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(root,e.position,e.pressEventCamera,out var point);
            dragGhost.localPosition=(Vector3)(point-grabOffset);
            UpdateDragCore();
        }
        public void UpdateDrag()
        {
            try { UpdateDragCore(); }
            catch(Exception error) { FailDrag(error); }
        }
        private void UpdateDragCore()
        {
            if(!IsDragging || dragEvent == null) return;
            splitRequested |= dragging.Data.count>1 && ControlHeld;
            if(dragWidth != dragHeight && Input.GetKeyDown(KeyCode.R) && lastRotationFrame != Time.frameCount)
            {
                lastRotationFrame=Time.frameCount;
                dragRotated=!dragRotated;
                int oldWidth=dragWidth; dragWidth=dragHeight; dragHeight=oldWidth;
                dragGhost.sizeDelta=new Vector2(dragWidth*Cell,dragHeight*Cell);
                CenterDragOnCursor();
                RectTransformUtility.ScreenPointToLocalPointInRectangle(root,dragEvent.position,dragEvent.pressEventCamera,out var pointer);
                dragGhost.localPosition=(Vector3)(pointer-grabOffset);
            }
            draggedNative.UpdateDragIcon(dragIcon,dragRotated);
            draggedNative.ShowSourceIconOnly();
            bool overShared=RectTransformUtility.RectangleContainsScreenPoint(sharedViewport,dragEvent.position,dragEvent.pressEventCamera);
            bool overPersonal=RectTransformUtility.RectangleContainsScreenPoint(personalViewport,dragEvent.position,dragEvent.pressEventCamera);
            // Update once per frame, including when the cursor is held still at an edge.
            if(lastScrollFrame != Time.frameCount)
            {
                lastScrollFrame=Time.frameCount;
                if(overShared) ScrollDuringDrag(shared,sharedViewport);
                else if(overPersonal)
                {
                    ScrollDuringDrag(personalContent,personalViewport);
                    // A previous merge target may leave the virtualized region.
                    // Detach its highlight before releasing views; retain the drag source.
                    dropHighlight.gameObject.SetActive(false);
                    dropHighlight.transform.SetParent(root,false);
                    mergeTarget=null;
                    SyncVisiblePersonal();
                }
            }
            SyncDragScrollbar(sharedScroll);
            SyncDragScrollbar(scroll);
            mergeTarget=tiles.Select(t => t.Input).FirstOrDefault(t => (t.Personal ? overPersonal : overShared)
                && RectTransformUtility.RectangleContainsScreenPoint((RectTransform)t.transform,dragEvent.position,dragEvent.pressEventCamera));
            if(mergeTarget != null)
            {
                dropX=dropY=null;
                validDrop=!mergeTarget.Invalid && (!mergeTarget.Personal || mergeTarget.Data.canReceive) && WarehousePacking.CanMerge(dragging.Data,mergeTarget.Data);
                dropHighlight.transform.SetParent(mergeTarget.transform,false);
                var highlight=dropHighlight.rectTransform;
                highlight.anchorMin=highlight.anchorMax=highlight.pivot=new Vector2(0,1);
                highlight.anchoredPosition=Vector2.zero;
                highlight.sizeDelta=new Vector2(mergeTarget.Data.width*Cell,mergeTarget.Data.height*Cell);
                dropHighlight.color=validDrop ? new Color(.25f,.55f,.3f,.4f) : new Color(.7f,.18f,.15f,.4f);
                dropHighlight.gameObject.SetActive(true);
                return;
            }
            var target=dragging.Personal || overShared ? sharedViewport : personalViewport;
            validDrop=RectTransformUtility.RectangleContainsScreenPoint(target,dragEvent.position,dragEvent.pressEventCamera);
            dropX=dropY=null;
            if(target == sharedViewport && validDrop)
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(shared,dragEvent.position,dragEvent.pressEventCamera,out var gridPointer);
                var position=WarehousePacking.DropPosition(gridPointer.x,-gridPointer.y,Cell,grabColumn,grabRow);
                dropX=position.X; dropY=position.Y;
                validDrop=WarehousePacking.Fits(lastSnapshot.items.Concat(lastSnapshot.blocked).ToList(),dropX.Value,dropY.Value,dragWidth,dragHeight,
                    dragging.Personal || splitRequested ? null : dragging.Data.id,sharedRows);
                dropHighlight.transform.SetParent(shared,false);
                var rect=dropHighlight.rectTransform;
                rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(0,1);
                rect.anchoredPosition=new Vector2(dropX.Value*Cell,-dropY.Value*Cell);
                rect.sizeDelta=new Vector2(dragWidth*Cell,dragHeight*Cell);
                dropHighlight.color=validDrop ? new Color(.25f,.55f,.3f,.4f) : new Color(.7f,.18f,.15f,.4f);
                dropHighlight.gameObject.SetActive(true);
            }
            else dropHighlight.gameObject.SetActive(false);
        }
        private static void SyncDragScrollbar(ScrollRect owner)
        {
            // ScrollRect is disabled during item drags, so LateUpdate cannot sync the handle.
            // Our grids are top-anchored: offset zero is the top (scrollbar value one).
            var content=owner.content;
            var viewport=owner.viewport;
            var bar=owner.verticalScrollbar;
            float range=Mathf.Max(0,content.rect.height-viewport.rect.height);
            bar.size=content.rect.height>0 ? Mathf.Clamp01(viewport.rect.height/content.rect.height) : 1;
            bar.SetValueWithoutNotify(range>0 ? 1-Mathf.Clamp01(content.anchoredPosition.y/range) : 1);
        }
        private void ScrollDuringDrag(RectTransform content,RectTransform viewport)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(viewport,dragEvent.position,
                dragEvent.pressEventCamera,out var pointer);
            float edge=Mathf.Min(Cell,viewport.rect.height*.25f);
            float fromTop=viewport.rect.yMax-pointer.y;
            float fromBottom=pointer.y-viewport.rect.yMin;
            float speed=fromTop<edge ? -(1-fromTop/edge) : fromBottom<edge ? 1-fromBottom/edge : 0;
            float delta=speed*Cell*6*Mathf.Min(Time.unscaledDeltaTime,.05f);
            delta-=Input.mouseScrollDelta.y*Cell;
            content.anchoredPosition=new Vector2(0,Mathf.Clamp(content.anchoredPosition.y+delta,
                0,Mathf.Max(0,content.rect.height-viewport.rect.height)));
        }
        private void CenterDragOnCursor()
        {
            // Item wrappers pivot at the top-left; the cursor holds their geometric center.
            grabOffset=new Vector2(dragWidth*Cell*.5f,-dragHeight*Cell*.5f);
            // A fixed center-cell anchor keeps the target stable within each grid cell.
            // For even dimensions the center lies on a border: choose right/bottom consistently.
            grabColumn=dragWidth/2; grabRow=dragHeight/2;
        }
        public void EndDrag(PointerEventData e)
        {
            try { EndDragCore(e); }
            catch(Exception error) { FailDrag(error); }
        }
        private void EndDragCore(PointerEventData e)
        {
            if(!IsDragging) return;
            DragCore(e);
            var input=dragging; bool accept=validDrop; var x=dropX; var y=dropY; bool rotation=dragRotated;
            var targetStack=mergeTarget;
            bool split=splitRequested;
            CancelDrag();
            if(!accept) return;
            if(targetStack != null)
            {
                if(split) ShowSplit(input.Data,input.Personal,null,null,rotation,
                    amount => merge(input.Data.id,input.Personal,targetStack.Data.id,targetStack.Personal,amount));
                else merge(input.Data.id,input.Personal,targetStack.Data.id,targetStack.Personal,null);
                return;
            }
            if(split) ShowSplit(input.Data,input.Personal,x,y,rotation);
            else move(input.Data.id,input.Personal,x,y,rotation,null);
        }
        private static bool ControlHeld => Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        private void ShowSplit(WarehouseItem data,bool personal,int? x,int? y,bool rotation,Action<int> mergeAmount=null)
        {
            modalError=null;
            try
            {
                // EFT creates the scene instance lazily on the first native split.
                // Clone the registered prefab directly so this also works after a fresh launch.
                var ui=ItemUiContext.Instance;
                if(ui == null) throw new InvalidOperationException("Game item UI is not ready.");
                var source=(SplitDialog)AccessTools.Field(typeof(ItemUiContext),"_splitDialogTemplate").GetValue(ui);
                if(source == null) throw new InvalidOperationException("Native split dialog template unavailable.");
                var overlay=Rect("SplitOverlay",parent,0,0,0,0);
                overlay.anchorMin=Vector2.zero; overlay.anchorMax=Vector2.one;
                overlay.offsetMin=overlay.offsetMax=Vector2.zero;
                splitOverlay=overlay.gameObject;
                Paint(overlay,new Color(0,0,0,.65f));
                splitDialog=UnityEngine.Object.Instantiate(source,overlay,false);
                foreach(var child in splitDialog.GetComponentsInChildren<Transform>(true)) child.gameObject.layer=parent.gameObject.layer;
                var rect=(RectTransform)splitDialog.transform;
                rect.anchorMin=Vector2.zero; rect.anchorMax=Vector2.one;
                rect.offsetMin=rect.offsetMax=Vector2.zero; rect.localScale=Vector3.one;
                cancelModal=() => { CloseSplit(); return true; };
                HeborianMenuInput.CancelModal=cancelModal;
                bool useStep=WarehousePacking.UseStepSplit(data.count);
                splitDialog.Show("SPLIT STACK",data.count,(Vector2)parent.TransformPoint(parent.rect.center),
                    amount => {
                        if(!HasModal) return;
                        if(useStep)
                        {
                            var slider=AccessTools.Field(typeof(SplitDialog),"_stepSlider").GetValue(splitDialog);
                            amount=(int)AccessTools.Field(typeof(StepSlider),"_currentValue").GetValue(slider);
                        }
                        if(!WarehousePacking.ValidSplitAmount(amount,data.count)) return;
                        CloseSplit();
                        if(mergeAmount != null) mergeAmount(amount);
                        else move(data.id,personal,x,y,rotation,amount);
                    },CloseSplit,useStep ? SplitDialog.ESplitDialogType.Step : SplitDialog.ESplitDialogType.Int);
            }
            catch(Exception error)
            {
                CloseSplit();
                modalError="Cannot open split dialog: " + error.Message;
                status.text=modalError;
                UnityEngine.Debug.LogException(error);
            }
        }
        public void TickModal()
        {
            if(historyOverlay!=null) { TickHistory(); return; }
            if(splitDialog!=null && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))) splitDialog.Accept();
        }
        private void CloseSplit()
        {
            if(HeborianMenuInput.CancelModal == cancelModal) HeborianMenuInput.CancelModal=null;
            cancelModal=null;
            if(splitDialog != null) splitDialog.Hide();
            splitDialog=null;
            if(splitOverlay != null) { splitOverlay.SetActive(false); UnityEngine.Object.Destroy(splitOverlay); }
            splitOverlay=null;
        }
        private void FailDrag(Exception error)
        {
            CancelDrag();
            modalError="Drag cancelled: " + error.Message + " (see client log).";
            status.text=modalError;
            UnityEngine.Debug.LogException(error);
        }
        private void CancelDrag()
        {
            // Clear the logical lock before touching Unity objects, including partial BeginDrag failures.
            var native=draggedNative; var ghost=dragGhost;
            draggedNative=null; dragGhost=null; dragIcon=null;
            dragging=null; dragEvent=null; validDrop=false; renderedFirstRow=-1; mergeTarget=null;
            if(scroll != null) scroll.enabled=true;
            if(sharedScroll != null) sharedScroll.enabled=true;
            if(dropHighlight != null)
            {
                dropHighlight.gameObject.SetActive(false);
                dropHighlight.transform.SetParent(root,false);
            }
            if(ghost != null) { ghost.gameObject.SetActive(false); UnityEngine.Object.Destroy(ghost.gameObject); }
            try { native?.RestoreSourcePresentation(); }
            catch(Exception error) { UnityEngine.Debug.LogException(error); }
        }
        private void ClearTiles()
        {
            CancelDrag();
            foreach(var tile in tiles)
            {
                ReleaseTile(tile);
            }
            tiles.Clear();
        }
        private void ReleaseTile(Tile tile)
        {
            tile.Native?.Dispose();
            if(tile.Input != null) { tile.Input.gameObject.SetActive(false); UnityEngine.Object.Destroy(tile.Input.gameObject); }
        }
        private RectTransform Rect(string name,Transform owner,float x,float y,float width,float height)
        {
            var go=new GameObject(name,typeof(RectTransform)); go.layer=owner.gameObject.layer;
            var rect=(RectTransform)go.transform; rect.SetParent(owner,false);
            rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(0,1);
            rect.anchoredPosition=new Vector2(x,-y); rect.sizeDelta=new Vector2(width,height); return rect;
        }
        private Image Paint(RectTransform rect,Color color)
        {
            var image=rect.gameObject.AddComponent<Image>(); image.color=color; return image;
        }
        private TMP_Text Label(Transform owner,string text,float x,float y,float width,float height,float size)
        {
            var label=Rect("Label",owner,x,y,width,height).gameObject.AddComponent<TextMeshProUGUI>();
            label.font=template.font; label.fontSharedMaterial=template.fontSharedMaterial;
            label.fontSize=size; label.color=new Color(.8f,.81f,.75f,1); label.text=text;
            label.raycastTarget=false; label.enableWordWrapping=true; label.overflowMode=TextOverflowModes.Ellipsis; return label;
        }
        private Button Button(Transform owner,string text,float x,float y,float width,float height,UnityAction action)
        {
            var rect=Rect("Button",owner,x,y,width,height); var image=Paint(rect,Color.white);
            var button=rect.gameObject.AddComponent<Button>(); button.targetGraphic=image;
            var colors=button.colors;
            colors.normalColor=new Color(.12f,.14f,.15f,1);
            colors.highlightedColor=new Color(.27f,.29f,.28f,1);
            colors.pressedColor=new Color(.34f,.35f,.32f,1);
            colors.selectedColor=colors.normalColor;
            colors.disabledColor=new Color(.06f,.07f,.07f,1);
            colors.fadeDuration=.08f; button.colors=colors;
            Frame(rect,0,0,width,height);
            button.navigation=new Navigation {mode=Navigation.Mode.None}; button.onClick.AddListener(action);
            Label(rect,text,4,2,width-8,height-4,15).alignment=TextAlignmentOptions.Center; return button;
        }
        public void Dispose()
        {
            if(disposed) return; disposed=true; CloseHistory(); CloseSplit(); ClearTiles();
            if(root != null) { root.gameObject.SetActive(false); UnityEngine.Object.Destroy(root.gameObject); }
        }
    }
}





