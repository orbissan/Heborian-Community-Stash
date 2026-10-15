using System;
using System.Collections.Generic;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using EFT.UI.DragAndDrop;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Heborian.Client.Modules.CommunityWarehouse
{
    // Real EFT grid_layout prefab and item rendering; detached snapshot models only.
    // Native inventory operations must never run against the shared warehouse.
    internal sealed class NativeWarehouseItem : IDisposable
    {
        // EFT includes a shared one-pixel outer border: 1 cell = 64, 2 cells = 127.
        // Grid spacing must use the pitch (63), not the bordered one-cell extent (64).
        public static float CellPitch => ItemViewFactory.GetCellPixelSize(new IntVec2(2,1)).X
            - ItemViewFactory.GetCellPixelSize(new IntVec2(1,1)).X;
        public GridItemView View { get; private set; }
        private readonly List<KeyValuePair<Graphic,bool>> raycasts=new List<KeyValuePair<Graphic,bool>>();
        private readonly List<KeyValuePair<GameObject,int>> layers=new List<KeyValuePair<GameObject,int>>();
        private readonly List<KeyValuePair<Graphic,bool>> dragDecorations=new List<KeyValuePair<Graphic,bool>>();
        public void ShowSourceIconOnly()
        {
            if(dragDecorations.Count == 0)
            {
                var icon=(Image)mainImage.GetValue(View);
                foreach(var graphic in View.GetComponentsInChildren<Graphic>(true))
                    if(graphic != icon) dragDecorations.Add(new KeyValuePair<Graphic,bool>(graphic,graphic.enabled));
            }
            foreach(var pair in dragDecorations) if(pair.Key != null) pair.Key.enabled=false;
        }
        public void RestoreSourcePresentation()
        {
            foreach(var pair in dragDecorations) if(pair.Key != null) pair.Key.enabled=pair.Value;
            dragDecorations.Clear();
        }
        private SnapshotContext context;
        private static readonly System.Reflection.FieldInfo foundSprite=AccessTools.Field(typeof(QuestItemViewPanel),"_foundInRaidSprite");
        private static readonly System.Reflection.FieldInfo questImage=AccessTools.Field(typeof(QuestItemViewPanel),"_questIconImage");
        private static readonly System.Reflection.FieldInfo questLabel=AccessTools.Field(typeof(QuestItemViewPanel),"_questItemLabel");
        private static readonly System.Reflection.FieldInfo mainImage=AccessTools.Field(typeof(ItemView),"MainImage");
        private Image indicatorImage;
        private Sprite originalIndicatorSprite;
        private GameObject indicatorLabel;
        private bool originalLabelActive;
        public NativeWarehouseItem(WarehouseItem data,RectTransform owner,float cell)
        {
            if(!Singleton<ItemFactory>.Instantiated) throw new InvalidOperationException("Game item factory is not ready.");
            var state=data.upd ?? new JObject { ["StackObjectsCount"]=data.count };
            Item item;
            if(data.tree != null && data.tree.Count>0)
            {
                var tree=(JArray)data.tree.DeepClone();
                var root=(JObject)tree[0];
                root.Remove("parentId"); root.Remove("slotId"); root.Remove("location");
                var result=Singleton<ItemFactory>.Instance.FlatItemsToTree(tree.ToObject<JsonType.FlatItem[]>(),false,null);
                if(result.DeserializationErrors.Count>0 || !result.Items.TryGetValue(data.id,out item))
                    throw new InvalidOperationException("Cannot render stash item tree: " + data.id);
            }
            else item=Singleton<ItemFactory>.Instance.CreateItem(data.id,data.template,new UnparsedData { JToken=state.DeepClone() });
            var controller=new ItemController(item,"heborian-"+data.id,"Heborian",false,EOwnerType.Mail);
            context=new SnapshotContext(item,null);
            // Acquire explicitly so a failure in initialization can release the borrowed prefab.
            View=ItemViewFactory.CreateFromPool<GridItemView>("grid_layout");
            try
            {
                View.NewGridItemView(item,context,data.rotated ? ItemRotation.Vertical : ItemRotation.Horizontal,controller,controller,null,null,null,null,null);
                View.Init();
                // ItemView normally hides this panel for our detached ItemController.
                // Reuse its original sprite/placement, without personal quest state.
                var indicator=View.QuestsItemViewPanel;
                if(indicator != null)
                {
                    indicator.HideGameObject();
                    var label=(Component)questLabel.GetValue(indicator);
                    if(label != null)
                    {
                        indicatorLabel=label.gameObject;
                        originalLabelActive=indicatorLabel.activeSelf;
                        indicatorLabel.SetActive(false);
                    }
                    if(item.MarkedAsSpawnedInSession)
                    {
                        var icon=(Image)questImage.GetValue(indicator);
                        indicatorImage=icon; originalIndicatorSprite=icon.sprite;
                        icon.sprite=(Sprite)foundSprite.GetValue(indicator);
                        indicator.ShowGameObject();
                    }
                }
                View.transform.SetParent(owner,false);
                var rect=View.RectTransform;
                rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(0,1);
                rect.anchoredPosition=Vector2.zero;
                rect.localScale=Vector3.one*(cell/CellPitch);
                foreach(var graphic in View.GetComponentsInChildren<Graphic>(true))
                {
                    raycasts.Add(new KeyValuePair<Graphic,bool>(graphic,graphic.raycastTarget));
                    graphic.raycastTarget=false;
                }
                foreach(var transform in View.GetComponentsInChildren<Transform>(true))
                {
                    layers.Add(new KeyValuePair<GameObject,int>(transform.gameObject,transform.gameObject.layer));
                    transform.gameObject.layer=owner.gameObject.layer;
                }
            }
            catch { Dispose(); throw; }
        }
        public void Highlight(bool selected,bool hovered)
        {
            if(View != null) View.BorderColor=selected ? new Color(.82f,.78f,.56f,1) :
                hovered ? new Color(.65f,.73f,.73f,1) : new Color(.25f,.29f,.29f,1);
        }
        public void Rotate(bool rotated)
        {
            View.ItemRotation=rotated ? ItemRotation.Vertical : ItemRotation.Horizontal;
        }
        public void UpdateDragIcon(Image icon,bool rotated)
        {
            // Copy only the rendered image, never the native item's scripts, labels or overlays.
            // Refresh while dragging because EFT may finish loading the sprite asynchronously.
            var source=(Image)mainImage.GetValue(View);
            icon.sprite=source.sprite;
            icon.color=source.color;
            icon.material=source.material;
            icon.preserveAspect=source.preserveAspect;
            icon.enabled=source.enabled && source.sprite != null;
            var scale=source.rectTransform.lossyScale;
            var ownerScale=icon.rectTransform.parent.lossyScale;
            icon.rectTransform.sizeDelta=new Vector2(source.rectTransform.rect.width*Mathf.Abs(scale.x/ownerScale.x),
                source.rectTransform.rect.height*Mathf.Abs(scale.y/ownerScale.y));
            icon.rectTransform.rotation=rotated ? ItemViewFactory.VerticalRotation : ItemViewFactory.HorizontalRotation;
        }
        public void Dispose()
        {
            if(View != null)
            {
                RestoreSourcePresentation();
                if(indicatorImage != null) indicatorImage.sprite=originalIndicatorSprite;
                if(indicatorLabel != null) indicatorLabel.SetActive(originalLabelActive);
                foreach(var pair in raycasts) if(pair.Key != null) pair.Key.raycastTarget=pair.Value;
                foreach(var pair in layers) if(pair.Key != null) pair.Key.layer=pair.Value;
                View.transform.localScale=Vector3.one;
                View.Kill(); // Native cleanup removes icon subscriptions and returns to UiPools.
                View=null;
            }
            context?.Dispose(); context=null;
        }
        private sealed class SnapshotContext : ItemContext
        {
            public SnapshotContext(Item item,ItemContext source) : base(item,EItemViewType.Inventory,source) { }
            public override bool DragAvailable => false;
            public override ItemContext CreateChild(Item item) => new SnapshotContext(item,this);
        }
    }
}
