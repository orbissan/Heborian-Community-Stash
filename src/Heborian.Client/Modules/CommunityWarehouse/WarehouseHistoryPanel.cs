using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Heborian.Client.Modules.HeborianMenu;
using Newtonsoft.Json;
using SPT.Common.Http;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Heborian.Client.Modules.CommunityWarehouse
{
    internal sealed partial class WarehousePanel
    {
        private GameObject historyOverlay;
        private RectTransform historyWindow,historyRows;
        private readonly List<NativeWarehouseItem> historyPreviews=new List<NativeWarehouseItem>();
        private ScrollRect historyScroll;
        private TMP_Text historyStatus,historyPageLabel;
        private TMP_InputField historySearch;
        private Button historyPrevious,historyNext;
        private Task<string> historyRequest;
        private readonly List<Button> historyControls=new List<Button>();
        private Func<bool> cancelHistory;
        private string historyFilter="all";
        private int historyPage,historyPages=1;
        private long historyThrough;
        private sealed class HistoryRow
        {
            public DateTime timeUtc { get; set; }
            public string player { get; set; }
            public string action { get; set; }
            public string item { get; set; }
            public string template { get; set; }
            public string category { get; set; }
            public HistoryCondition condition { get; set; }
            public HistoryFee fee { get; set; }
            public int count { get; set; }
            public int contents { get; set; }
        }
        private sealed class HistoryFee { public string Currency { get; set; } public int Amount { get; set; } }
        private sealed class HistoryCondition
        {
            public string Icon { get; set; }
            public double Current { get; set; }
            public double Maximum { get; set; }
        }
        private sealed class HistoryResponse
        {
            public string error { get; set; }
            public int page { get; set; }
            public int pages { get; set; }
            public int total { get; set; }
            public long throughRevision { get; set; }
            public List<HistoryRow> rows { get; set; }
        }
        private void OpenHistory()
        {
            if(busy || IsDragging || HasModal || lastSnapshot?.historyAvailable!=true) return;
            try
            {
                var overlay=Rect("HistoryOverlay",parent,0,0,0,0);
                overlay.anchorMin=Vector2.zero; overlay.anchorMax=Vector2.one;
                overlay.offsetMin=overlay.offsetMax=Vector2.zero;
                historyOverlay=overlay.gameObject;
                Paint(overlay,new Color(0,0,0,.8f));
                historyWindow=Rect("HistoryWindow",overlay,0,0,1100,650);
                historyWindow.anchorMin=historyWindow.anchorMax=historyWindow.pivot=new Vector2(.5f,.5f);
                historyWindow.anchoredPosition=Vector2.zero;
                Paint(historyWindow,new Color(.055f,.065f,.068f,1));
                Frame(historyWindow,0,0,1100,650);
                var title=Label(historyWindow,"COMMUNITY STASH / HISTORY",22,16,950,30,24);
                title.fontStyle=FontStyles.Bold;
                Button(historyWindow,"X",1046,12,36,32,CloseHistory);
                historyControls.Clear();
                string[] names={"ALL TRANSFERS","DEPOSITS","WITHDRAWALS","ARRANGEMENT"};
                string[] filters={"all","deposit","withdraw","arrange"};
                for(int i=0;i<names.Length;i++)
                {
                    string filter=filters[i];
                    historyControls.Add(Button(historyWindow,names[i],22+i*215,60,205,32,()=> {
                        historyFilter=filter; historyThrough=0; LoadHistory(0);
                    }));
                }
                historyControls.Add(Button(historyWindow,"REFRESH",900,60,178,32,()=>{historyThrough=0;LoadHistory(0);}));
                var input=Rect("Search",historyWindow,22,104,850,34);
                // TMP creates its caret/selection renderer in OnEnable only when textComponent is assigned.
                input.gameObject.SetActive(false);
                var image=Paint(input,new Color(.1f,.12f,.13f,1));
                historySearch=input.gameObject.AddComponent<TMP_InputField>();
                historySearch.targetGraphic=image;
                var viewport=Rect("TextViewport",input,8,3,834,28);
                viewport.gameObject.AddComponent<RectMask2D>();
                var text=Label(viewport,"",0,0,834,28,18);
                text.richText=false; text.enableWordWrapping=false;
                text.alignment=TextAlignmentOptions.Left;
                text.overflowMode=TextOverflowModes.Overflow;
                var placeholder=Label(viewport,"Search player or item...",0,0,834,28,18);
                placeholder.color=new Color(.5f,.53f,.51f,1);
                placeholder.alignment=TextAlignmentOptions.Left;
                placeholder.enableWordWrapping=false;
                historySearch.textViewport=viewport;
                historySearch.textComponent=text; historySearch.placeholder=placeholder;
                historySearch.fontAsset=template.font;
                historySearch.characterLimit=80; historySearch.lineType=TMP_InputField.LineType.SingleLine;
                historySearch.customCaretColor=true;
                historySearch.caretColor=new Color(.9f,.91f,.85f,1);
                historySearch.caretWidth=2; historySearch.caretBlinkRate=.85f;
                historySearch.selectionColor=new Color(.48f,.57f,.62f,.65f);
                input.gameObject.SetActive(true);
                historyControls.Add(Button(historyWindow,"SEARCH",900,104,178,34,()=>{historyThrough=0;LoadHistory(0);}));
                historySearch.onSubmit.AddListener(_=>{historyThrough=0;LoadHistory(0);});
                HistoryCells(historyWindow,150,"LOCAL TIME","PLAYER","ACTION","ITEM","",true);
                var list=Rect("HistoryList",historyWindow,0,184,1100,380);
                historyScroll=list.gameObject.AddComponent<ScrollRect>();
                historyScroll.horizontal=false; historyScroll.vertical=true;
                historyScroll.movementType=ScrollRect.MovementType.Clamped;
                historyScroll.scrollSensitivity=NativeWarehouseItem.CellPitch;
                var listViewport=Rect("Viewport",list,0,0,1078,380);
                Paint(listViewport,Color.clear);
                listViewport.gameObject.AddComponent<RectMask2D>();
                historyRows=Rect("Rows",listViewport,0,0,1078,380);
                historyScroll.viewport=listViewport; historyScroll.content=historyRows;
                var bar=CreateScrollbar(historyScroll,0);
                bar.SetParent(list,false); bar.anchoredPosition=new Vector2(1082,0);
                bar.sizeDelta=new Vector2(14,380);
                historyStatus=Label(historyWindow,"",22,574,1050,28,16);
                historyPrevious=Button(historyWindow,"PREVIOUS",22,609,150,28,()=>LoadHistory(historyPage-1));
                historyPageLabel=Label(historyWindow,"",190,609,700,28,16);
                historyPageLabel.alignment=TextAlignmentOptions.Center;
                historyNext=Button(historyWindow,"NEXT",928,609,150,28,()=>LoadHistory(historyPage+1));
                historyFilter="all"; historyThrough=0;
                cancelHistory=()=>{CloseHistory();return true;};
                HeborianMenuInput.CancelModal=cancelHistory;
                LoadHistory(0); TickHistory();
            }
            catch(Exception error) { CloseHistory(); modalError="Cannot open history: "+error.Message; }
        }
        private void HistoryCells(Transform owner,float y,string time,string player,string action,string item,string count,bool header=false,float height=30)
        {
            string[] values={time,player,action,item,count};
            float[] x={890,header ? 34 : 62,240,380,998},width={180,header ? 190 : 162,134,498,80};
            for(int i=0;i<values.Length;i++)
            {
                var label=Label(owner,values[i],x[i],y,width[i],height,header?15:16);
                label.richText=false; label.enableWordWrapping=false;
                if(header) label.fontStyle=FontStyles.Bold;
                label.alignment=i==4 ? TextAlignmentOptions.Center : TextAlignmentOptions.MidlineLeft;
            }
        }
        private void LoadHistory(int page)
        {
            if(historyRequest!=null || historyOverlay==null) return;
            historyStatus.text="Loading history...";
            ClearHistoryPreviews();
            foreach(Transform child in historyRows) { child.gameObject.SetActive(false); UnityEngine.Object.Destroy(child.gameObject); }
            try
            {
                historyRequest=RequestHandler.PostJsonAsync("/heborian/warehouse/history",JsonConvert.SerializeObject(new {
                    page=Math.Max(0,page),filter=historyFilter,search=historySearch.text,throughRevision=historyThrough
                }));
            }
            catch(Exception error) { historyStatus.text="History unavailable: "+error.Message; }
            SetHistoryControls();
        }
        private void TickHistory()
        {
            historyWindow.localScale=Vector3.one*Mathf.Max(.1f,Mathf.Min(1,parent.rect.width/1120,parent.rect.height/670));
            if(historyRequest==null || !historyRequest.IsCompleted) return;
            try
            {
                var response=JsonConvert.DeserializeObject<HistoryResponse>(historyRequest.GetAwaiter().GetResult());
                if(response==null || !string.IsNullOrEmpty(response.error)) throw new InvalidOperationException(response?.error ?? "Empty response.");
                if(response.rows==null || response.rows.Count>12 || response.pages<1 || response.page<0 || response.page>=response.pages)
                    throw new InvalidOperationException("Invalid history response.");
                historyPage=response.page; historyPages=response.pages; historyThrough=response.throughRevision;
                float y=0;
                for(int i=0;i<response.rows.Count;i++)
                {
                    var row=response.rows[i];
                    var rowRoot=Rect("Row",historyRows,0,y,1078,80);
                    var tint=row.action=="deposit" ? new Color(.07f,.15f,.105f,1)
                        : row.action=="withdraw" ? new Color(.17f,.075f,.075f,1)
                        : new Color(.10f,.12f,.13f,1);
                    Paint(Rect("RowGaps",rowRoot,22,0,1056,80),new Color(.055f,.065f,.068f,1)).raycastTarget=false;
                    float[] starts={22,234,374,884}, widths={210,138,508,194};
                    for(int column=0;column<starts.Length;column++)
                        Paint(Rect("Column",rowRoot,starts[column],0,widths[column],80),tint).raycastTarget=false;
                    var previewHost=Rect("ItemPreview",rowRoot,380,8,64,64);
                    Paint(previewHost,new Color(.13f,.15f,.15f,1)).raycastTarget=false;
                    Frame(previewHost,0,0,64,64);
                    try
                    {
                        if(!string.IsNullOrEmpty(row.template))
                        {
                            var preview=new NativeWarehouseItem(new WarehouseItem {
                                id=Guid.NewGuid().ToString("N").Substring(0,24),template=row.template,count=1
                            },previewHost,NativeWarehouseItem.CellPitch);
                            historyPreviews.Add(preview);
                            preview.ShowSourceIconOnly();
                            var size=preview.View.RectTransform.rect.size;
                            // Fit the entire icon in a single cell without stretching its aspect ratio.
                            float scale=Mathf.Min(1,56/Mathf.Max(1,Mathf.Max(size.x,size.y)));
                            preview.View.RectTransform.localScale=Vector3.one*scale;
                            preview.View.RectTransform.anchoredPosition=new Vector2((64-size.x*scale)/2,-(64-size.y*scale)/2);
                        }
                    }
                    catch(Exception) { /* Removed templates must not hide the audit trail. */ }
                    const float height=80;
                    var profile=Rect("PlayerIcon",rowRoot,34,31,18,18).gameObject.AddComponent<RawImage>();
                    profile.texture=profileIcon; profile.raycastTarget=false;

                    HistoryCells(rowRoot,0,row.timeUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss"),row.player,
                        row.action=="deposit" ? "DEPOSIT" : row.action=="withdraw" ? "WITHDRAW" : row.action.ToUpperInvariant(),
                        "","",false,height);
                    var amount=Label(previewHost,row.count.ToString("N0"),3,44,58,18,12);
                    amount.richText=false; amount.enableWordWrapping=false;
                    amount.alignment=TextAlignmentOptions.BottomRight;
                    amount.enableAutoSizing=true; amount.fontSizeMin=8; amount.fontSizeMax=12;
                    amount.color=Color.white;
                    var name=Label(rowRoot,HistoryPresentation.ItemName(row.item),458,8,416,25,16);
                    name.richText=false; name.enableWordWrapping=false; name.alignment=TextAlignmentOptions.MidlineLeft;
                    name.fontStyle=FontStyles.Bold;
                    var total=Label(rowRoot,"Total "+row.count.ToString("N0"),458,32,416,21,15);
                    total.richText=false; total.enableWordWrapping=false;
                    total.alignment=TextAlignmentOptions.MidlineLeft;
                    if(row.condition!=null && row.condition.Maximum>0)
                    {
                        var c=row.condition;
                        float left=458+total.GetPreferredValues(total.text).x+12;
                        string iconName=c.Icon ?? (row.category=="Medication" ? "HpResource"
                            : row.category=="Provisions" ? "FoodResource" : "Durability");
                        if(System.Enum.TryParse(iconName,out EFT.InventoryLogic.EItemAttributeId attribute))
                        {
                            var settings=EFTHardSettings.Instance;
                            var sprite=settings!=null && settings.StaticIcons!=null ? settings.StaticIcons.GetAttributeIcon(attribute) : null;
                            if(sprite!=null)
                            {
                                var icon=Rect("ConditionIcon",rowRoot,left,34,17,17).gameObject.AddComponent<Image>();
                                icon.sprite=sprite; icon.preserveAspect=true; icon.raycastTarget=false;
                                icon.color=new Color(.60f,.64f,.66f,1); left+=21;
                            }
                        }
                        string current=c.Current.ToString("0.##",System.Globalization.CultureInfo.InvariantCulture);
                        if(c.Current<c.Maximum) current="<color=#E04444>"+current+"</color>";
                        var condition=Label(rowRoot,current+"/"+c.Maximum.ToString("0.##",System.Globalization.CultureInfo.InvariantCulture),
                            left,32,Mathf.Max(1,874-left),21,15);
                        condition.richText=true; condition.enableWordWrapping=false;
                        condition.alignment=TextAlignmentOptions.MidlineLeft;
                    }
                    var category=Label(rowRoot,(row.category ?? "Other")+(row.fee!=null ? "  |  Fee: "+row.fee.Amount.ToString("N0")+" "+row.fee.Currency : ""),458,53,416,20,13);
                    category.richText=false; category.enableWordWrapping=false;
                    category.color=new Color(.52f,.58f,.56f,1);
                    category.alignment=TextAlignmentOptions.MidlineLeft;
                    y+=height+2;
                }
                historyRows.sizeDelta=new Vector2(1078,Mathf.Max(380,y));
                historyRows.anchoredPosition=Vector2.zero;
                Canvas.ForceUpdateCanvases();
                historyScroll.verticalNormalizedPosition=1;
                historyStatus.text=response.total==0 ? "No matching events. History starts when this version is installed."
                    : (historyFilter=="all" ? "Deposits and withdrawals" : historyFilter.ToUpperInvariant())+" / "+response.total+" events";
                historyPageLabel.text="PAGE "+(historyPage+1)+" / "+historyPages;
            }
            catch(Exception error) { historyStatus.text="History unavailable: "+error.Message; historyPages=1; historyPage=0; historyPageLabel.text=""; }
            finally { historyRequest=null; SetHistoryControls(); }
        }
        private void SetHistoryControls()
        {
            bool available=historyRequest==null;
            foreach(var button in historyControls) button.interactable=available;
            historySearch.interactable=available;
            historyPrevious.interactable=available && historyPage>0;
            historyNext.interactable=available && historyPage+1<historyPages;
        }
        private void ClearHistoryPreviews()
        {
            foreach(var preview in historyPreviews) preview.Dispose();
            historyPreviews.Clear();
        }
        private void CloseHistory()
        {
            if(HeborianMenuInput.CancelModal==cancelHistory) HeborianMenuInput.CancelModal=null;
            cancelHistory=null;
            if(historyRequest!=null) historyRequest.ContinueWith(t=>{var ignored=t.Exception;},TaskContinuationOptions.OnlyOnFaulted);
            historyRequest=null;
            ClearHistoryPreviews();
            if(historyOverlay!=null) { historyOverlay.SetActive(false); UnityEngine.Object.Destroy(historyOverlay); }
            historyOverlay=null; historyControls.Clear();
        }
    }
}
