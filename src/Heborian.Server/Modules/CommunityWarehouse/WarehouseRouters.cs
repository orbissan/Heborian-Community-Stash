using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.DI.Routing;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Utils;

namespace Heborian.Server.Modules.CommunityWarehouse;


[Injectable]
public sealed class WarehouseReadRouter : StaticRouter
{
    public WarehouseReadRouter(JsonUtil json, WarehouseModule warehouse) : base(json, new RouteAction[] {
        new RouteAction<EmptyRequestData>("/heborian/warehouse", (url, request, session, output, cancellation) => warehouse.Read(session)),
        new RouteAction<WarehouseHistoryRequest>("/heborian/warehouse/history", (url, request, session, output, cancellation) => warehouse.ReadHistory(session,request))
    }) { }
}
[Injectable]
public sealed class WarehouseItemRouter : ItemEventRouter
{
    public WarehouseItemRouter(WarehouseModule warehouse) : base(new ItemRouteAction[] {
        new ItemRouteAction<WarehouseRequest>("HeborianWarehouse", (url, pmc, request, session, output, cancellation) => warehouse.Transfer(pmc, request, session, output))
    }) { }
}
