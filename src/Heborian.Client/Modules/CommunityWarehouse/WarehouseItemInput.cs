using UnityEngine;
using UnityEngine.EventSystems;

namespace Heborian.Client.Modules.CommunityWarehouse
{
    // Captures pointer input above the native presentation, routing transfers to Heborian.
    public sealed class WarehouseItemInput : MonoBehaviour, IPointerClickHandler, IPointerDownHandler,
        IPointerEnterHandler, IPointerExitHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        internal WarehousePanel Panel;
        internal WarehouseItem Data;
        internal bool Personal;
        internal bool Hovered;
        private bool dragged;
        public void OnPointerDown(PointerEventData e) { dragged=false; }
        public void OnPointerClick(PointerEventData e)
        {
            if(dragged || e.button != PointerEventData.InputButton.Left) return;
            Panel.Click(this,Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl));
        }
        public void OnPointerEnter(PointerEventData e) { Hovered=true; }
        public void OnPointerExit(PointerEventData e) { Hovered=false; }
        public void OnBeginDrag(PointerEventData e)
        {
            dragged=e.button == PointerEventData.InputButton.Left && Panel.BeginDrag(this,e);
        }
        public void OnDrag(PointerEventData e) { if(dragged) Panel.Drag(e); }
        public void OnEndDrag(PointerEventData e)
        {
            if(dragged) Panel.EndDrag(e);
            // Unity sends PointerClick after some drags. Reset at the next pointer press instead.
        }
    }
}
