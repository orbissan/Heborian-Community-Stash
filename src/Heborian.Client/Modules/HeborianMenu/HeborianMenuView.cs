using System;
using System.Collections.Generic;
using BepInEx.Logging;
using Comfort.Common;
using EFT.InputSystem;
using EFT.UI;
using EFT.UI.Screens;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Heborian.Client.Modules.HeborianMenu
{
    public sealed class HeborianMenuView : MonoBehaviour
    {
        private static HeborianMenuView activeView;
        public static MenuContentLifecycle Content { get; } = new MenuContentLifecycle();
        public static bool AnyOpen => activeView != null && activeView.open;
        public static RectTransform ActiveContentRoot => AnyOpen ? activeView.contentRoot : null;
        public static TMP_Text ActiveTextTemplate => AnyOpen ? activeView.tab.GetComponentInChildren<TMP_Text>(true) : null;
        private RectTransform contentRoot;
        private TMP_Text contentStatus;
        public static void SetContentStatus(string message)
        {
            if(!AnyOpen || activeView.contentStatus == null) return;
            activeView.contentStatus.text=message ?? "";
            activeView.contentStatus.gameObject.SetActive(!string.IsNullOrEmpty(message));
        }
        private MenuTaskBar bar;
        private AnimatedToggle template;
        private AnimatedToggle tab;
        private GameObject staging;
        private GameObject tabSpacer;
        private GameObject panel;
        private DefaultUIButton back;
        private InputTree inputTree;
        private HeborianMenuInput input;
        private EftScreenManager screenManager;
        private ManualLogSource log;
        private readonly Dictionary<CanvasGroup,float> coveredScreens = new Dictionary<CanvasGroup,float>();
        private EnvironmentUI tabEnvironment;
        private bool previousMainEnvironment;
        private object environmentScreen;
        private static readonly System.Reflection.FieldInfo mainEnvironmentField=AccessTools.Field(typeof(EnvironmentUI),"_isMainScreen");
        private bool open;
        private bool released;
        private Dictionary<EMenuType, AnimatedToggle> nativeToggles;
        private readonly List<AnimatedToggle> previousSelection = new List<AnimatedToggle>();

        public void Initialize(MenuTaskBar bar, ManualLogSource log, Sprite icon)
        {
            this.bar = bar;
            this.log = log;
            var toggles = AccessTools.Field(typeof(MenuTaskBar), "_toggleButtons").GetValue(bar)
                as Dictionary<EMenuType, AnimatedToggle>;
            if (toggles == null || !toggles.TryGetValue(EMenuType.Handbook, out template) || template == null)
                throw new InvalidOperationException("Native Handbook taskbar button unavailable.");
            if (!toggles.TryGetValue(EMenuType.Player, out var character) || character == null)
                throw new InvalidOperationException("Native Character taskbar button unavailable.");
            nativeToggles = toggles;

            // Clone inactive: no native ToggleGroup registration/listeners may fire before isolation.
            staging = new GameObject("Heborian.Staging", typeof(RectTransform));
            staging.SetActive(false);
            staging.transform.SetParent(bar.transform, false);
            var clone = Instantiate(character.gameObject, staging.transform, false);
            clone.name = "Heborian.Tab";
            clone.SetActive(false);
            tab = clone.GetComponent<AnimatedToggle>();
            tab.onValueChanged = new Toggle.ToggleEvent();
            var ownGroup = clone.AddComponent<ToggleGroup>();
            ownGroup.allowSwitchOff = true;
            tab.group = ownGroup; // AnimatedToggle expects a non-null group in OnPointerClick.
            tab.SetIsOnWithoutNotify(false);
            foreach (var localized in clone.GetComponentsInChildren<LocalizedText>(true))
                localized.enabled = false;
            foreach (var tooltip in clone.GetComponentsInChildren<HoverTooltipArea>(true))
                tooltip.enabled = false;
            foreach (var label in clone.GetComponentsInChildren<TMP_Text>(true)) label.text = "HEBORIAN";
            // Reuse the native icon slots and their normal/selected animation colours.
            foreach (var image in clone.GetComponentsInChildren<Image>(true))
                if (image.name.IndexOf("icon", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    image.sprite = icon;
                    image.overrideSprite = null;
                    image.preserveAspect = true;
                    image.enabled = true;
                }
            tab.onValueChanged.AddListener(OnTabChanged);
            clone.transform.SetParent(character.transform.parent, false);
            clone.transform.SetSiblingIndex(character.transform.GetSiblingIndex());
            // A transparent, non-clickable layout item separates the two backgrounds.
            // Keep this local to Heborian instead of changing every native tab's spacing.
            tabSpacer = new GameObject("Heborian.TabSpacer", typeof(RectTransform), typeof(LayoutElement));
            tabSpacer.layer = clone.layer;
            tabSpacer.transform.SetParent(character.transform.parent, false);
            tabSpacer.transform.SetSiblingIndex(character.transform.GetSiblingIndex());
            var spacerLayout = tabSpacer.GetComponent<LayoutElement>();
            spacerLayout.minWidth = 10;
            spacerLayout.preferredWidth = 10;
            spacerLayout.flexibleWidth = 0;
            var layout = clone.GetComponent<LayoutElement>();
            if (layout != null) { layout.flexibleWidth = 0; }
            clone.SetActive(true);
            var pressedField = AccessTools.Field(typeof(MenuTaskBar), "OnTaskBarButtonPressed");
            pressedField.SetValue(bar, Delegate.Combine((Delegate)pressedField.GetValue(bar), new Action<EMenuType, bool>(OnNativeTabPressed)));
            screenManager = EftScreenManager.Instance;
            screenManager.OnScreenChanged += OnScreenChanged;
            log.LogInfo("[HeborianMenu] Native-style Heborian taskbar button created.");
        }

        private void OnTabChanged(bool selected)
        {
            // Same sound and event timing as MenuTaskBar's native toggle callback.
            // Programmatic resets use SetIsOnWithoutNotify, so closing via BACK is silent here.
            if (Singleton<GUISounds>.Instantiated)
                Singleton<GUISounds>.Instance.PlayUISound(EUISoundType.ButtonBottomBarClick);
            if (selected) Open(); else Close();
        }

        private void Open()
        {
            if (open || released) return;
            try
            {
                if (!CanOpen()) { tab.SetIsOnWithoutNotify(false); return; }
                if (activeView != null && activeView != this) activeView.ClosePanel(false);
                EnsurePanel();
                inputTree = FindLiveInputTree();
                if (inputTree == null) throw new InvalidOperationException("Game input tree unavailable.");
                inputTree.Add(input);
                previousSelection.Clear();
                foreach (var native in nativeToggles.Values)
                    if (native != null && native.isOn) previousSelection.Add(native);
                open = true;
                activeView = this;
                ClearNativeSelection();
                RevealEnvironment();
                panel.SetActive(true);
                panel.transform.SetAsLastSibling();
                EventSystem.current?.SetSelectedGameObject(null);
                SetContentStatus("No content module is enabled. Check the Heborian client configuration.");
                Content.Open(ReportContentFailure);
                Content.Tick();
                log.LogInfo("[HeborianMenu] Opened screen.");
            }
            catch (Exception error)
            {
                Close();
                if (panel != null) Destroy(panel);
                panel = null; back = null; input = null;
                log.LogError("[HeborianMenu] Unable to open screen: " + error);
            }
        }

        private void ReportContentFailure(string phase, Exception error)
        {
            log.LogError("[HeborianMenu] Content " + phase + " failed: " + error);
            SetContentStatus("Heborian content could not load. See BepInEx/LogOutput.log for details.");
        }

        private static InputTree FindLiveInputTree()
        {
            // FindObjectOfType excludes hidden/DontSave objects. Resolve the tree actually
            // receiving input instead: the game's InputTree.Awake assigns this delegate.
            foreach (var manager in UnityEngine.Resources.FindObjectsOfTypeAll<InputManager>())
            {
                if (!manager.gameObject.scene.IsValid() || !manager.isActiveAndEnabled) continue;
                var handlers = manager.TranslateDelegate;
                if (handlers == null) continue;
                foreach (var handler in handlers.GetInvocationList())
                    if (handler.Target is InputTree tree && tree != null) return tree;
            }
            // A mod may wrap the delegate; retain the original tree associated with the
            // active manager. Do not pick prefab assets or inactive duplicate managers.
            foreach (var tree in UnityEngine.Resources.FindObjectsOfTypeAll<InputTree>())
                if (tree.gameObject.scene.IsValid() && tree.InputManager != null
                    && tree.InputManager.isActiveAndEnabled) return tree;
            return null;
        }

        private bool CanOpen()
        {
            if (bar == null || !bar.isActiveAndEnabled || template == null || !template.IsInteractable()) return false;
            var screen = screenManager?.CurrentScreenController;
            if (screen == null) return false;
            // No raid, hideout, loading, matchmaker or modal entry points.
            switch (screen.ScreenType)
            {
                case EEftScreenType.MainMenu:
                case EEftScreenType.Inventory: // Handbook availability still gates this outside raids.
                case EEftScreenType.Traders:
                case EEftScreenType.Trader:
                case EEftScreenType.FleaMarket:
                case EEftScreenType.Handbook:
                case EEftScreenType.EquipmentBuilds:
                case EEftScreenType.Settings:
                    return true;
                default: return false;
            }
        }

        private void EnsurePanel()
        {
            if (panel != null) return;
            var rootCanvas = bar.GetComponentInParent<Canvas>()?.rootCanvas;
            if (rootCanvas == null) throw new InvalidOperationException("Menu canvas unavailable.");
            DefaultUIButton source = null;
            foreach (var inventory in UnityEngine.Resources.FindObjectsOfTypeAll<InventoryScreen>())
            {
                if (!inventory.gameObject.scene.IsValid()) continue;
                source = AccessTools.Field(typeof(InventoryScreen), "_backButton").GetValue(inventory) as DefaultUIButton;
                if (source != null) break;
            }
            if (source == null) throw new InvalidOperationException("Native BACK button unavailable.");

            // Entire construction stays inactive until listeners, input ownership and layout are ready.
            panel = new GameObject("Heborian.Screen", typeof(RectTransform));
            panel.layer = rootCanvas.gameObject.layer;
            panel.SetActive(false);
            panel.transform.SetParent(rootCanvas.transform, false);
            var rect = (RectTransform)panel.transform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;
            var canvas = panel.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingLayerID = rootCanvas.sortingLayerID;
            canvas.sortingOrder = rootCanvas.sortingOrder + 50;
            panel.AddComponent<GraphicRaycaster>();
            input = panel.AddComponent<HeborianMenuInput>();
            input.Back = Close;

            var body = new GameObject("Content", typeof(RectTransform), typeof(Image));
            body.layer = rootCanvas.gameObject.layer;
            body.transform.SetParent(panel.transform, false);
            var bodyRect = (RectTransform)body.transform;
            contentRoot = bodyRect;
            bodyRect.anchorMin = Vector2.zero; bodyRect.anchorMax = Vector2.one;
            bodyRect.offsetMin = new Vector2(0, TaskbarHeight(rootCanvas));
            bodyRect.offsetMax = Vector2.zero;
            var background = body.GetComponent<Image>();
            background.color = new Color(0.025f, 0.032f, 0.032f, 1f);
            background.raycastTarget = true;

            var statusObject = new GameObject("Heborian.ContentStatus", typeof(RectTransform));
            statusObject.layer=body.layer;
            statusObject.transform.SetParent(body.transform,false);
            var statusRect=(RectTransform)statusObject.transform;
            statusRect.anchorMin=new Vector2(.1f,.4f); statusRect.anchorMax=new Vector2(.9f,.6f);
            statusRect.offsetMin=Vector2.zero; statusRect.offsetMax=Vector2.zero;
            contentStatus=statusObject.AddComponent<TextMeshProUGUI>();
            var textTemplate=tab.GetComponentInChildren<TMP_Text>(true);
            contentStatus.font=textTemplate.font;
            contentStatus.fontSharedMaterial=textTemplate.fontSharedMaterial;
            contentStatus.fontSize=24;
            contentStatus.color=new Color(.8f,.81f,.75f,1);
            contentStatus.alignment=TextAlignmentOptions.Center;
            contentStatus.raycastTarget=false;

            back = Instantiate(source, body.transform, false);
            back.gameObject.name = "Heborian.Back";
            ClearEvent(back.OnClick);
            back.OnClick.AddListener(Close);
            ClearEvent(back.OnMouseOver);
            ClearEvent(back.OnMouseOut);
            back.SetRawText("BACK", source.HeaderSize);
            back.Interactable = true;
            var backLayout = back.GetComponent<LayoutElement>();
            if (backLayout != null) backLayout.ignoreLayout = true;
            var backRect = (RectTransform)back.transform;
            backRect.anchorMin = Vector2.one; backRect.anchorMax = Vector2.one;
            backRect.pivot = Vector2.one;
            backRect.anchoredPosition = new Vector2(-30, -24);
            backRect.sizeDelta = new Vector2(Mathf.Max(180, source.RectTransform.rect.width), Mathf.Max(60, source.RectTransform.rect.height));
            back.gameObject.SetActive(true);
        }

        private float TaskbarHeight(Canvas rootCanvas)
        {
            var root = (RectTransform)rootCanvas.transform;
            var barRect = (RectTransform)bar.transform;
            var corners = new Vector3[4]; barRect.GetWorldCorners(corners);
            float top = root.InverseTransformPoint(corners[1]).y - root.rect.yMin;
            return Mathf.Clamp(top, 24, 100);
        }

        private static void ClearEvent(UnityEvent evt)
        {
            evt.RemoveAllListeners();
            for (int i = 0; i < evt.GetPersistentEventCount(); i++)
                evt.SetPersistentListenerState(i, UnityEventCallState.Off);
        }

        public void Close()
        {
            ClosePanel(true);
        }

        private void ClearNativeSelection()
        {
            foreach (var native in nativeToggles.Values)
            {
                if (native == null || !native.isOn) continue;
                // Unity refuses to deselect the last toggle when allowSwitchOff is false.
                // Relax that rule only for this silent update; never fire native navigation.
                var group = native.group;
                bool allowSwitchOff = group != null && group.allowSwitchOff;
                try
                {
                    if (group != null) group.allowSwitchOff = true;
                    native.SetIsOnWithoutNotify(false);
                }
                finally { if (group != null) group.allowSwitchOff = allowSwitchOff; }
                native.TriggerAnimation(false);
            }
        }

        private void ClosePanel(bool restoreSelection)
        {
            if (inputTree != null && input != null) inputTree.Remove(input);
            inputTree = null;
            RestoreEnvironment();
            RestoreCoveredScreens();
            open = false;
            if (activeView == this) { Content.Close(); activeView = null; }
            if (panel != null) panel.SetActive(false);
            if (tab != null) { tab.SetIsOnWithoutNotify(false); tab.TriggerAnimation(false); }
            // BACK reveals the same screen; navigation lets the game choose the new tab.
            if (restoreSelection)
                foreach (var native in previousSelection)
                    if (native != null)
                    {
                        native.SetIsOnWithoutNotify(true);
                        native.TriggerAnimation(true);
                    }
            previousSelection.Clear();
            if (EventSystem.current != null && back != null && EventSystem.current.currentSelectedGameObject == back.gameObject)
                EventSystem.current.SetSelectedGameObject(null);
        }

        private void OnNativeTabPressed(EMenuType type, bool selected) { ClosePanel(false); }
        private void RevealEnvironment()
        {
            // The native EnvironmentUI already owns the selected scene, camera and cursor parallax.
            // Do not reload it or change the user's environment setting.
            bool available=false;
            foreach(var environment in UnityEngine.Resources.FindObjectsOfTypeAll<EnvironmentUI>())
                if(environment.gameObject.scene.IsValid() && environment.gameObject.activeInHierarchy && environment.IsActive)
                {
                    available=true;
                    tabEnvironment=environment;
                    previousMainEnvironment=(bool)mainEnvironmentField.GetValue(environment);
                    environmentScreen=screenManager.CurrentScreenController;
                    // Same native secondary-screen mode as Character/Traders: hides MainScreenObjects
                    // (logo/foreground props) and selects the secondary camera pose.
                    environment.SetAsMain(false,false);
                    break;
                }
            contentRoot.GetComponent<Image>().color=available
                ? new Color(.025f,.032f,.032f,.12f) : new Color(.025f,.032f,.032f,1);
            if(available) HideCoveredScreens();
        }
        private void RestoreEnvironment()
        {
            // BACK returns to the covered screen; a new screen owns its own environment settings.
            if(tabEnvironment!=null && ReferenceEquals(environmentScreen,screenManager?.CurrentScreenController))
                tabEnvironment.SetAsMain(previousMainEnvironment,false);
            tabEnvironment=null; environmentScreen=null;
        }
        private void HideCoveredScreens()
        {
            // Hide presentation only: disabling the screen would also stop native menu behavior.
            foreach(var screen in UnityEngine.Resources.FindObjectsOfTypeAll<UIScreen>())
            {
                if(!screen.gameObject.scene.IsValid() || !screen.gameObject.activeInHierarchy) continue;
                if(panel.transform.IsChildOf(screen.transform) || bar.transform.IsChildOf(screen.transform)) continue;
                if(screen.GetComponentInChildren<EnvironmentUI>(true)!=null) continue;
                var group=screen.CanvasGroup;
                if(group==null) continue;
                if(!coveredScreens.ContainsKey(group)) coveredScreens.Add(group,group.alpha);
                group.alpha=0;
            }
        }
        private void RestoreCoveredScreens()
        {
            foreach(var pair in coveredScreens)
                if(pair.Key!=null) pair.Key.alpha=pair.Value;
            coveredScreens.Clear();
        }

        private void OnScreenChanged(EEftScreenType type) { ClosePanel(false); }
        private void LateUpdate()
        {
            if (released || tab == null) return;
            bool available = CanOpen();
            tab.interactable = available;
            if (open && !available) Close();
            // Native screen updates can finish asynchronously after this overlay opens.
            if (open)
            {
                ClearNativeSelection();
                if(tabEnvironment!=null && (bool)mainEnvironmentField.GetValue(tabEnvironment))
                    tabEnvironment.SetAsMain(false,false);
                foreach(var pair in coveredScreens) if(pair.Key!=null) pair.Key.alpha=0;
            }
            if (open) Content.Tick();
        }
        private void OnDisable() { Close(); }
        private void OnDestroy() { Release(); }

        public void Release()
        {
            if (released) return;
            released = true;
            Close();
            if (bar != null)
            {
                var pressedField = AccessTools.Field(typeof(MenuTaskBar), "OnTaskBarButtonPressed");
                pressedField.SetValue(bar, Delegate.Remove((Delegate)pressedField.GetValue(bar), new Action<EMenuType, bool>(OnNativeTabPressed)));
            }
            if (screenManager != null) screenManager.OnScreenChanged -= OnScreenChanged;
            if (tab != null) { tab.gameObject.SetActive(false); Destroy(tab.gameObject); }
            if (panel != null) Destroy(panel);
            if (staging != null) Destroy(staging);
            if (tabSpacer != null) { tabSpacer.SetActive(false); Destroy(tabSpacer); }
        }
    }
}
