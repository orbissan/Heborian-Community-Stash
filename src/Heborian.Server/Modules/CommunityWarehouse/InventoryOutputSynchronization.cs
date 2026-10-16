using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using SPTarkov.Server.Core.Routers;

namespace Heborian.Server.Modules.CommunityWarehouse;

// SPT 4.1.6 stores responses for every player in ordinary Dictionaries.
// Protect only their synchronous access, not network requests or profile saves.
// Monitor is reentrant because GetOutput can call ResetOutput internally.
[HarmonyPatch]
internal static class InventoryOutputSynchronization
{
    private static readonly object Sync=new();
    private static bool installed;
    public static void Install()
    {
        lock(Sync)
        {
            if(installed) return;
            new Harmony("local.heborian.server.inventory-output").CreateClassProcessor(typeof(InventoryOutputSynchronization)).Patch();
            installed=true;
        }
    }
    private static IEnumerable<MethodBase> TargetMethods()
    {
        foreach(var name in new[]{"GetOutput","ResetOutput","UpdateOutputProperties"})
            yield return AccessTools.DeclaredMethod(typeof(EventOutputHolder),name)
                ?? throw new MissingMethodException(typeof(EventOutputHolder).FullName,name);
    }
    private static void Prefix(ref bool __state) => Monitor.Enter(Sync,ref __state);
    private static Exception Finalizer(Exception __exception,bool __state)
    {
        if(__state) Monitor.Exit(Sync);
        return __exception;
    }
}
