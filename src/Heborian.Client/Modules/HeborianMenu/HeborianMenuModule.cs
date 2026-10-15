using System;
using System.Collections.Generic;
using System.Reflection;
using EFT.UI;
using EFT.UI.Screens;
using HarmonyLib;
using UnityEngine;

namespace Heborian.Client.Modules.HeborianMenu
{
    /// <summary>Owns only Heborian objects and its narrowly scoped taskbar hook.</summary>
    public sealed class HeborianMenuModule : IClientModule
    {
        private const string PatchId = "local.heborian.client.HeborianMenu";
        private static HeborianMenuModule current;
        private readonly List<HeborianMenuView> views = new List<HeborianMenuView>();
        private Harmony harmony;
        private ClientModuleContext context;
        private Sprite icon;
        public string Id => "HeborianMenu";

        public void Initialize(ClientModuleContext context)
        {
            this.context = context;
            if (current != null) throw new InvalidOperationException("HeborianMenu already initialized.");
            current = this;
            var texture = context.Assets.LoadTexture("icon_heborian_01.png");
            icon = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100);
            icon.name = "Heborian.MenuIcon";
            // Check private fields before installing a patch; fail this module only on incompatible UI.
            if (AccessTools.Field(typeof(MenuTaskBar), "_toggleButtons") == null
                || AccessTools.Field(typeof(MenuTaskBar), "OnTaskBarButtonPressed") == null
                || AccessTools.Field(typeof(InventoryScreen), "_backButton") == null)
                throw new MissingFieldException("SPT 4.1.6 taskbar/back-button bindings unavailable.");
            harmony = new Harmony(PatchId);
            harmony.Patch(AccessTools.Method(typeof(MenuTaskBar), "Awake"),
                postfix: new HarmonyMethod(typeof(HeborianMenuModule), nameof(AfterTaskBarAwake)));
            foreach (var bar in UnityEngine.Resources.FindObjectsOfTypeAll<MenuTaskBar>())
                if (bar.gameObject.scene.IsValid()) Attach(bar);
        }

        private static void AfterTaskBarAwake(MenuTaskBar __instance) => current?.Attach(__instance);

        private void Attach(MenuTaskBar bar)
        {
            if (bar.GetComponent<HeborianMenuView>() != null) return;
            HeborianMenuView view = null;
            try
            {
                view = bar.gameObject.AddComponent<HeborianMenuView>();
                view.Initialize(bar, context.Log, icon);
                views.Add(view);
            }
            catch (Exception error)
            {
                if (view != null) { view.Release(); UnityEngine.Object.Destroy(view); }
                context.Log.LogError("[HeborianMenu] Cannot create taskbar entry: " + error);
            }
        }

        public void Dispose()
        {
            if (current == this) current = null;
            harmony?.UnpatchSelf();
            harmony = null;
            foreach (var view in views)
                if (view != null) { view.Release(); UnityEngine.Object.Destroy(view); }
            views.Clear();
            if (icon != null) UnityEngine.Object.Destroy(icon);
            icon = null;
        }
    }
}
