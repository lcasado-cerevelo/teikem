using System.Reflection;
using Microsoft.AspNetCore.Mvc.Routing;
using Teikem.Api.Auth;
using Teikem.Api.Controllers;
using Teikem.Domain.Constants;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 6 / P10: el módulo y el permiso de cada acción de los 13 controladores de Inventario y almacén quedan fijados por
/// reflexión (patrón de TripControllerSecurityTests). Quitar un [RequireModule] o un [RequirePermission], cambiar el permiso
/// de una acción o agregar una acción sin mapearla rompe CI aunque el smoke no pase por ella. Ninguna acción recibe un
/// 'tenantId' (sale del principal).
/// Nota: start/complete de la cola piden inventory.view en el controlador; el permiso del handler (D41) lo exige
/// WarehouseTaskService (lo fija WarehouseTaskServiceTests). Igual con orders.create/orders.cancel al empacar/eliminar una
/// recolección, purchasing.manage en REORDER y purchasing.receive al recibir contra una OC (WmsServicePermissionTests).
/// </summary>
public class WmsControllerSecurityTests
{
    private static readonly Dictionary<Type, string> ModuleOf = new()
    {
        [typeof(WarehousesController)] = ModuleKeys.WmsLotSerial,
        [typeof(ProductCategoriesController)] = ModuleKeys.WmsLotSerial,
        [typeof(ProductsController)] = ModuleKeys.WmsLotSerial,
        [typeof(InventoryController)] = ModuleKeys.WmsLotSerial,
        [typeof(InventoryDiscrepanciesController)] = ModuleKeys.WmsLotSerial,   // Lote 14
        [typeof(ReceiptsController)] = ModuleKeys.WmsLotSerial,
        [typeof(AsnsController)] = ModuleKeys.WmsLotSerial,
        [typeof(WarehouseTasksController)] = ModuleKeys.WmsLotSerial,
        [typeof(CycleCountsController)] = ModuleKeys.WmsLotSerial,
        [typeof(PickBatchesController)] = ModuleKeys.WmsLotSerial,
        [typeof(SuppliersController)] = ModuleKeys.Purchasing,
        [typeof(PurchaseOrdersController)] = ModuleKeys.Purchasing,
        [typeof(DockAppointmentsController)] = ModuleKeys.CrossDock,
        [typeof(CrossDockPlansController)] = ModuleKeys.CrossDock,
    };

    /// <summary>(controlador, acción) → permiso esperado. Exactamente uno por acción.</summary>
    private static readonly Dictionary<(Type Controller, string Action), string> Expected = new()
    {
        [(typeof(WarehousesController), nameof(WarehousesController.List))] = PermissionCatalog.InventoryView,
        [(typeof(WarehousesController), nameof(WarehousesController.Create))] = PermissionCatalog.WarehouseManage,
        [(typeof(WarehousesController), nameof(WarehousesController.Get))] = PermissionCatalog.InventoryView,
        [(typeof(WarehousesController), nameof(WarehousesController.Update))] = PermissionCatalog.WarehouseManage,
        [(typeof(WarehousesController), nameof(WarehousesController.Deactivate))] = PermissionCatalog.WarehouseManage,
        [(typeof(WarehousesController), nameof(WarehousesController.Zones))] = PermissionCatalog.InventoryView,
        [(typeof(WarehousesController), nameof(WarehousesController.CreateZone))] = PermissionCatalog.WarehouseManage,
        [(typeof(WarehousesController), nameof(WarehousesController.UpdateZone))] = PermissionCatalog.WarehouseManage,
        [(typeof(WarehousesController), nameof(WarehousesController.DeactivateZone))] = PermissionCatalog.WarehouseManage,
        [(typeof(WarehousesController), nameof(WarehousesController.ReactivateZone))] = PermissionCatalog.WarehouseManage,
        [(typeof(WarehousesController), nameof(WarehousesController.Bins))] = PermissionCatalog.InventoryView,
        [(typeof(WarehousesController), nameof(WarehousesController.SearchBins))] = PermissionCatalog.InventoryView,   // Lote 14
        [(typeof(WarehousesController), nameof(WarehousesController.CreateBin))] = PermissionCatalog.WarehouseManage,
        [(typeof(WarehousesController), nameof(WarehousesController.UpdateBin))] = PermissionCatalog.WarehouseManage,
        [(typeof(WarehousesController), nameof(WarehousesController.SetBinsCapacity))] = PermissionCatalog.WarehouseManage,   // Lote 11: cupo en bloque
        [(typeof(WarehousesController), nameof(WarehousesController.DeactivateBin))] = PermissionCatalog.WarehouseManage,
        [(typeof(WarehousesController), nameof(WarehousesController.ConfirmProvisionalBin))] = PermissionCatalog.WarehouseManage,   // Lote 21
        [(typeof(WarehousesController), nameof(WarehousesController.ReactivateBin))] = PermissionCatalog.WarehouseManage,
        [(typeof(WarehousesController), nameof(WarehousesController.BinProducts))] = PermissionCatalog.InventoryView,   // informe "Productos por posición"
        [(typeof(WarehousesController), nameof(WarehousesController.Docks))] = PermissionCatalog.InventoryView,
        [(typeof(WarehousesController), nameof(WarehousesController.CreateDock))] = PermissionCatalog.WarehouseManage,
        [(typeof(WarehousesController), nameof(WarehousesController.UpdateDock))] = PermissionCatalog.WarehouseManage,
        [(typeof(WarehousesController), nameof(WarehousesController.SetDockStatus))] = PermissionCatalog.WarehouseManage,
        [(typeof(WarehousesController), nameof(WarehousesController.DeactivateDock))] = PermissionCatalog.WarehouseManage,
        [(typeof(WarehousesController), nameof(WarehousesController.ReactivateDock))] = PermissionCatalog.WarehouseManage,

        [(typeof(ProductCategoriesController), nameof(ProductCategoriesController.List))] = PermissionCatalog.InventoryView,
        [(typeof(ProductCategoriesController), nameof(ProductCategoriesController.Create))] = PermissionCatalog.InventoryManage,
        [(typeof(ProductCategoriesController), nameof(ProductCategoriesController.Update))] = PermissionCatalog.InventoryManage,
        [(typeof(ProductCategoriesController), nameof(ProductCategoriesController.Deactivate))] = PermissionCatalog.InventoryManage,
        [(typeof(ProductCategoriesController), nameof(ProductCategoriesController.Reactivate))] = PermissionCatalog.InventoryManage,

        [(typeof(ProductsController), nameof(ProductsController.List))] = PermissionCatalog.InventoryView,
        [(typeof(ProductsController), nameof(ProductsController.Brands))] = PermissionCatalog.InventoryView,   // Lote 12
        [(typeof(ProductsController), nameof(ProductsController.Get))] = PermissionCatalog.InventoryView,
        [(typeof(ProductsController), nameof(ProductsController.ByBarcode))] = PermissionCatalog.InventoryView,   // Lote 8A
        [(typeof(ProductsController), nameof(ProductsController.Lots))] = PermissionCatalog.InventoryView,
        [(typeof(ProductsController), nameof(ProductsController.Serials))] = PermissionCatalog.InventoryView,
        [(typeof(ProductsController), nameof(ProductsController.Create))] = PermissionCatalog.InventoryManage,
        [(typeof(ProductsController), nameof(ProductsController.Update))] = PermissionCatalog.InventoryManage,
        [(typeof(ProductsController), nameof(ProductsController.Deactivate))] = PermissionCatalog.InventoryManage,
        [(typeof(ProductsController), nameof(ProductsController.Reactivate))] = PermissionCatalog.InventoryManage,

        [(typeof(InventoryController), nameof(InventoryController.Balances))] = PermissionCatalog.InventoryView,
        [(typeof(InventoryController), nameof(InventoryController.Transactions))] = PermissionCatalog.InventoryView,
        [(typeof(InventoryController), nameof(InventoryController.Adjust))] = PermissionCatalog.InventoryAdjust,
        [(typeof(InventoryController), nameof(InventoryController.Transfer))] = PermissionCatalog.InventoryAdjust,
        [(typeof(InventoryController), nameof(InventoryController.Genealogy))] = PermissionCatalog.InventoryView,
        [(typeof(InventoryController), nameof(InventoryController.SerialTrace))] = PermissionCatalog.InventoryView,
        [(typeof(InventoryController), nameof(InventoryController.Reconciliation))] = PermissionCatalog.InventoryAdjust,
        // Lote 14 (P0/P1): resumen y detalle del Kárdex y dueños con inventory.view; conciliación que guarda descuadres con inventory.adjust.
        [(typeof(InventoryController), nameof(InventoryController.TransactionsSummary))] = PermissionCatalog.InventoryView,
        [(typeof(InventoryController), nameof(InventoryController.Transaction))] = PermissionCatalog.InventoryView,
        [(typeof(InventoryController), nameof(InventoryController.Owners))] = PermissionCatalog.InventoryView,
        [(typeof(InventoryController), nameof(InventoryController.RunReconciliation))] = PermissionCatalog.InventoryAdjust,
        // Lote 14 (P2, D14): estado de la revisión en segundo plano, con inventory.adjust como la conciliación.
        [(typeof(InventoryController), nameof(InventoryController.ReconciliationStatus))] = PermissionCatalog.InventoryAdjust,
        // Lote 15: franja "Almacén hoy" del Pulso con inventory.view (son datos del Kárdex; el panel pide además pulse.warehouse).
        [(typeof(InventoryController), nameof(InventoryController.PulseDays))] = PermissionCatalog.InventoryView,
        [(typeof(InventoryDiscrepanciesController), nameof(InventoryDiscrepanciesController.List))] = PermissionCatalog.InventoryView,
        [(typeof(InventoryDiscrepanciesController), nameof(InventoryDiscrepanciesController.Get))] = PermissionCatalog.InventoryView,
        [(typeof(InventoryDiscrepanciesController), nameof(InventoryDiscrepanciesController.Resolve))] = PermissionCatalog.InventoryAdjust,

        [(typeof(ReceiptsController), nameof(ReceiptsController.List))] = PermissionCatalog.InventoryView,
        [(typeof(ReceiptsController), nameof(ReceiptsController.Get))] = PermissionCatalog.InventoryView,
        [(typeof(ReceiptsController), nameof(ReceiptsController.Create))] = PermissionCatalog.WarehouseReceive,
        [(typeof(ReceiptsController), nameof(ReceiptsController.UpdateHeader))] = PermissionCatalog.WarehouseReceive,   // Lote 13
        [(typeof(ReceiptsController), nameof(ReceiptsController.UpdateLine))] = PermissionCatalog.WarehouseReceive,
        [(typeof(ReceiptsController), nameof(ReceiptsController.AddLine))] = PermissionCatalog.WarehouseReceive,
        [(typeof(ReceiptsController), nameof(ReceiptsController.RemoveLine))] = PermissionCatalog.WarehouseReceive,
        [(typeof(ReceiptsController), nameof(ReceiptsController.Confirm))] = PermissionCatalog.WarehouseReceive,
        [(typeof(ReceiptsController), nameof(ReceiptsController.Delete))] = PermissionCatalog.WarehouseReceive,
        [(typeof(ReceiptsController), nameof(ReceiptsController.TargetSuggestions))] = PermissionCatalog.InventoryView,   // Lote 16
        [(typeof(ReceiptsController), nameof(ReceiptsController.ApplySuggestedTargets))] = PermissionCatalog.WarehouseReceive,   // Lote 16

        [(typeof(AsnsController), nameof(AsnsController.List))] = PermissionCatalog.InventoryView,
        [(typeof(AsnsController), nameof(AsnsController.Get))] = PermissionCatalog.InventoryView,
        [(typeof(AsnsController), nameof(AsnsController.Create))] = PermissionCatalog.WarehouseReceive,
        [(typeof(AsnsController), nameof(AsnsController.Cancel))] = PermissionCatalog.WarehouseReceive,

        [(typeof(WarehouseTasksController), nameof(WarehouseTasksController.List))] = PermissionCatalog.InventoryView,
        [(typeof(WarehouseTasksController), nameof(WarehouseTasksController.PutawaySuggestions))] = PermissionCatalog.InventoryView,
        [(typeof(WarehouseTasksController), nameof(WarehouseTasksController.Get))] = PermissionCatalog.InventoryView,
        [(typeof(WarehouseTasksController), nameof(WarehouseTasksController.Assign))] = PermissionCatalog.WarehouseManage,
        [(typeof(WarehouseTasksController), nameof(WarehouseTasksController.Start))] = PermissionCatalog.InventoryView,
        [(typeof(WarehouseTasksController), nameof(WarehouseTasksController.Complete))] = PermissionCatalog.InventoryView,
        [(typeof(WarehouseTasksController), nameof(WarehouseTasksController.Cancel))] = PermissionCatalog.WarehouseManage,
        [(typeof(WarehouseTasksController), nameof(WarehouseTasksController.RunReplenishment))] = PermissionCatalog.WarehousePick,

        [(typeof(CycleCountsController), nameof(CycleCountsController.List))] = PermissionCatalog.InventoryView,
        [(typeof(CycleCountsController), nameof(CycleCountsController.Get))] = PermissionCatalog.InventoryView,   // Lote 8A: a ciegas sin warehouse.count
        [(typeof(CycleCountsController), nameof(CycleCountsController.Create))] = PermissionCatalog.WarehouseCountCapture,   // Lote 8A: a ciegas
        [(typeof(CycleCountsController), nameof(CycleCountsController.Capture))] = PermissionCatalog.WarehouseCountCapture,   // Lote 8A: a ciegas
        [(typeof(CycleCountsController), nameof(CycleCountsController.CaptureBatch))] = PermissionCatalog.WarehouseCountCapture,   // Lote 8A
        [(typeof(CycleCountsController), nameof(CycleCountsController.AddLine))] = PermissionCatalog.WarehouseCountCapture,   // Lote 8A: a ciegas
        [(typeof(CycleCountsController), nameof(CycleCountsController.Finish))] = PermissionCatalog.WarehouseCountCapture,   // Lote 8A: a ciegas
        [(typeof(CycleCountsController), nameof(CycleCountsController.Refresh))] = PermissionCatalog.WarehouseCount,
        [(typeof(CycleCountsController), nameof(CycleCountsController.Reconcile))] = PermissionCatalog.WarehouseCount,
        [(typeof(CycleCountsController), nameof(CycleCountsController.Delete))] = PermissionCatalog.WarehouseCount,
        [(typeof(CycleCountsController), nameof(CycleCountsController.Page))] = PermissionCatalog.InventoryView,   // Lote 14: a ciegas sin warehouse.count
        [(typeof(CycleCountsController), nameof(CycleCountsController.ChangesPreview))] = PermissionCatalog.WarehouseCount,   // Lote 14 (D3)
        [(typeof(CycleCountsController), nameof(CycleCountsController.FromChanges))] = PermissionCatalog.WarehouseCount,   // Lote 14 (D3)
        [(typeof(CycleCountsController), nameof(CycleCountsController.ReconcilePreview))] = PermissionCatalog.WarehouseCount,   // Lote 21
        [(typeof(CycleCountsController), nameof(CycleCountsController.Review))] = PermissionCatalog.WarehouseCount,   // Lote 21
        [(typeof(CycleCountsController), nameof(CycleCountsController.ReconcileMatching))] = PermissionCatalog.WarehouseCount,   // Lote 21
        [(typeof(CycleCountsController), nameof(CycleCountsController.CreateProvisionalBin))] = PermissionCatalog.WarehouseCountCapture,   // Lote 21

        [(typeof(PickBatchesController), nameof(PickBatchesController.List))] = PermissionCatalog.InventoryView,
        [(typeof(PickBatchesController), nameof(PickBatchesController.Get))] = PermissionCatalog.InventoryView,
        [(typeof(PickBatchesController), nameof(PickBatchesController.Collect))] = PermissionCatalog.WarehousePick,
        [(typeof(PickBatchesController), nameof(PickBatchesController.CollectAndPack))] = PermissionCatalog.WarehousePick,   // Lote 8A
        [(typeof(PickBatchesController), nameof(PickBatchesController.Pack))] = PermissionCatalog.WarehousePick,
        [(typeof(PickBatchesController), nameof(PickBatchesController.Delete))] = PermissionCatalog.WarehousePick,

        [(typeof(SuppliersController), nameof(SuppliersController.List))] = PermissionCatalog.PurchasingView,
        [(typeof(SuppliersController), nameof(SuppliersController.Create))] = PermissionCatalog.PurchasingManage,
        [(typeof(SuppliersController), nameof(SuppliersController.Update))] = PermissionCatalog.PurchasingManage,
        [(typeof(SuppliersController), nameof(SuppliersController.Deactivate))] = PermissionCatalog.PurchasingManage,
        [(typeof(SuppliersController), nameof(SuppliersController.Reactivate))] = PermissionCatalog.PurchasingManage,

        [(typeof(PurchaseOrdersController), nameof(PurchaseOrdersController.List))] = PermissionCatalog.PurchasingView,
        [(typeof(PurchaseOrdersController), nameof(PurchaseOrdersController.Shortages))] = PermissionCatalog.PurchasingView,
        [(typeof(PurchaseOrdersController), nameof(PurchaseOrdersController.Get))] = PermissionCatalog.PurchasingView,
        [(typeof(PurchaseOrdersController), nameof(PurchaseOrdersController.ShortageLines))] = PermissionCatalog.PurchasingView,
        [(typeof(PurchaseOrdersController), nameof(PurchaseOrdersController.Create))] = PermissionCatalog.PurchasingManage,
        [(typeof(PurchaseOrdersController), nameof(PurchaseOrdersController.Update))] = PermissionCatalog.PurchasingManage,
        [(typeof(PurchaseOrdersController), nameof(PurchaseOrdersController.Send))] = PermissionCatalog.PurchasingManage,
        [(typeof(PurchaseOrdersController), nameof(PurchaseOrdersController.Cancel))] = PermissionCatalog.PurchasingManage,
        [(typeof(PurchaseOrdersController), nameof(PurchaseOrdersController.Delete))] = PermissionCatalog.PurchasingManage,
        [(typeof(PurchaseOrdersController), nameof(PurchaseOrdersController.Resolve))] = PermissionCatalog.InventoryAdjust,

        [(typeof(DockAppointmentsController), nameof(DockAppointmentsController.List))] = PermissionCatalog.InventoryView,
        [(typeof(DockAppointmentsController), nameof(DockAppointmentsController.Create))] = PermissionCatalog.WarehouseCrossdock,
        [(typeof(DockAppointmentsController), nameof(DockAppointmentsController.Reschedule))] = PermissionCatalog.WarehouseCrossdock,
        [(typeof(DockAppointmentsController), nameof(DockAppointmentsController.SetStatus))] = PermissionCatalog.WarehouseCrossdock,

        [(typeof(CrossDockPlansController), nameof(CrossDockPlansController.List))] = PermissionCatalog.InventoryView,
        [(typeof(CrossDockPlansController), nameof(CrossDockPlansController.Get))] = PermissionCatalog.InventoryView,
        [(typeof(CrossDockPlansController), nameof(CrossDockPlansController.Candidates))] = PermissionCatalog.InventoryView,
        [(typeof(CrossDockPlansController), nameof(CrossDockPlansController.Create))] = PermissionCatalog.WarehouseCrossdock,
        [(typeof(CrossDockPlansController), nameof(CrossDockPlansController.Allocate))] = PermissionCatalog.WarehouseCrossdock,
        [(typeof(CrossDockPlansController), nameof(CrossDockPlansController.CancelAllocation))] = PermissionCatalog.WarehouseCrossdock,
        [(typeof(CrossDockPlansController), nameof(CrossDockPlansController.Move))] = PermissionCatalog.WarehouseCrossdock,
        [(typeof(CrossDockPlansController), nameof(CrossDockPlansController.Complete))] = PermissionCatalog.WarehouseCrossdock,
    };

    public static IEnumerable<object[]> ActionMap() => Expected.Select(kv => new object[] { kv.Key.Controller, kv.Key.Action, kv.Value });

    private static List<MethodInfo> Actions(Type controller)
        => controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any())
            .ToList();

    [Theory]
    [MemberData(nameof(ActionMap))]
    public void Each_wms_action_requires_exactly_its_permission(Type controller, string action, string permission)
    {
        var methods = Actions(controller).Where(m => m.Name == action).ToList();
        Assert.True(methods.Count == 1, $"{controller.Name}.{action} no existe, no es una acción HTTP o está sobrecargada.");
        var policies = methods[0].GetCustomAttributes<RequirePermissionAttribute>().Select(a => a.Policy).ToList();
        Assert.True(policies.Count == 1, $"{controller.Name}.{action} debe llevar exactamente un [RequirePermission] (tiene {policies.Count}).");
        Assert.True(RequirePermissionAttribute.Prefix + permission == policies[0],
            $"{controller.Name}.{action}: se esperaba '{permission}' y tiene '{policies[0]}'.");
    }

    [Fact]
    public void The_map_covers_every_public_action_of_the_wms_controllers()
    {
        var unmapped = ModuleOf.Keys
            .SelectMany(c => Actions(c).Select(m => (c, m.Name)))
            .Where(k => !Expected.ContainsKey(k))
            .Select(k => $"{k.c.Name}.{k.Name}")
            .ToList();
        Assert.True(unmapped.Count == 0, "Acciones sin permiso esperado en el mapa: " + string.Join(", ", unmapped));
        Assert.All(Expected.Keys, k => Assert.True(ModuleOf.ContainsKey(k.Controller), $"{k.Controller.Name} no está en la lista de módulos."));
    }

    [Fact]
    public void Every_wms_controller_requires_exactly_its_module()
    {
        foreach (var (controller, module) in ModuleOf)
        {
            var modules = controller.GetCustomAttributes<RequireModuleAttribute>(inherit: true).Select(a => a.ModuleKey).ToList();
            Assert.True(modules.SequenceEqual(new[] { module }),
                $"{controller.Name} debe llevar exactamente [RequireModule({module})] (tiene: {string.Join(", ", modules)}).");
        }
    }

    [Fact]
    public void Key_decisions_are_pinned()
    {
        // D22: reconciliar un conteo lo hace quien tiene warehouse.count (el Operador de almacén ya lo tiene).
        Assert.Equal(PermissionCatalog.WarehouseCount, Expected[(typeof(CycleCountsController), nameof(CycleCountsController.Reconcile))]);
        // D41: la cola pide inventory.view; el permiso del handler lo exige el servicio.
        Assert.Equal(PermissionCatalog.InventoryView, Expected[(typeof(WarehouseTasksController), nameof(WarehouseTasksController.Complete))]);
        // R14: resolver faltantes es un ajuste de inventario.
        Assert.Equal(PermissionCatalog.InventoryAdjust, Expected[(typeof(PurchaseOrdersController), nameof(PurchaseOrdersController.Resolve))]);
        // D28: citas y planes de cruce de muelle en el módulo CROSSDOCK.
        Assert.Equal(ModuleKeys.CrossDock, ModuleOf[typeof(DockAppointmentsController)]);
        // Lote 8A: la ficha del conteo se lee con inventory.view (a ciegas sin warehouse.count); alta, captura y terminar piden
        // warehouse.count.capture (implícito en warehouse.count); refrescar, reconciliar y la baja siguen con warehouse.count.
        Assert.Equal(PermissionCatalog.InventoryView, Expected[(typeof(CycleCountsController), nameof(CycleCountsController.Get))]);
        foreach (var a in new[] { nameof(CycleCountsController.Create), nameof(CycleCountsController.Capture), nameof(CycleCountsController.CaptureBatch),
                     nameof(CycleCountsController.AddLine), nameof(CycleCountsController.Finish) })
            Assert.Equal(PermissionCatalog.WarehouseCountCapture, Expected[(typeof(CycleCountsController), a)]);
        foreach (var a in new[] { nameof(CycleCountsController.Refresh), nameof(CycleCountsController.Reconcile), nameof(CycleCountsController.Delete) })
            Assert.Equal(PermissionCatalog.WarehouseCount, Expected[(typeof(CycleCountsController), a)]);
        Assert.Contains(PermissionCatalog.WarehouseCountCapture, PermissionCatalog.Implied[PermissionCatalog.WarehouseCount]);
        // Lote 8A: recolectar y empacar en una llamada tiene su propia ruta y su propio tipo de respuesta.
        Assert.Equal(PermissionCatalog.WarehousePick, Expected[(typeof(PickBatchesController), nameof(PickBatchesController.CollectAndPack))]);
        // Lote 12: + GET /products/brands (inventory.view).
        Assert.Equal(PermissionCatalog.InventoryView, Expected[(typeof(ProductsController), nameof(ProductsController.Brands))]);
        // Lote 11 (cupo de posiciones): + POST /warehouses/{id}/bins/capacity (warehouse.manage, como editar una posición).
        Assert.Equal(PermissionCatalog.WarehouseManage, Expected[(typeof(WarehousesController), nameof(WarehousesController.SetBinsCapacity))]);
        // Lote 13: + PATCH /receipts/{publicId} (warehouse.receive, como el resto de la captura del recibo).
        Assert.Equal(PermissionCatalog.WarehouseReceive, Expected[(typeof(ReceiptsController), nameof(ReceiptsController.UpdateHeader))]);
        // Lote 14 (D5): los descuadres los ve quien ve inventario y los resuelve quien puede ajustar; + búsqueda de posiciones.
        Assert.Equal(PermissionCatalog.InventoryView, Expected[(typeof(InventoryDiscrepanciesController), nameof(InventoryDiscrepanciesController.Get))]);
        Assert.Equal(PermissionCatalog.InventoryAdjust, Expected[(typeof(InventoryDiscrepanciesController), nameof(InventoryDiscrepanciesController.Resolve))]);
        Assert.Equal(PermissionCatalog.InventoryAdjust, Expected[(typeof(InventoryController), nameof(InventoryController.ReconciliationStatus))]);
        // Lote 14 (P4): lista paginada con inventory.view (a ciegas sin warehouse.count); "lo cambiado" crea muchos conteos de una
        // vez: vista previa y alta con warehouse.count (D3), no con warehouse.count.capture.
        Assert.Equal(PermissionCatalog.InventoryView, Expected[(typeof(CycleCountsController), nameof(CycleCountsController.Page))]);
        Assert.Equal(PermissionCatalog.WarehouseCount, Expected[(typeof(CycleCountsController), nameof(CycleCountsController.ChangesPreview))]);
        Assert.Equal(PermissionCatalog.WarehouseCount, Expected[(typeof(CycleCountsController), nameof(CycleCountsController.FromChanges))]);
        // Lote 15: + GET /inventory/pulse/days (franja "Almacén hoy") con inventory.view.
        Assert.Equal(PermissionCatalog.InventoryView, Expected[(typeof(InventoryController), nameof(InventoryController.PulseDays))]);
        // Lote 16: + sugerencias de posición destino (inventory.view) y "Usar posiciones sugeridas" (warehouse.receive).
        Assert.Equal(PermissionCatalog.InventoryView, Expected[(typeof(ReceiptsController), nameof(ReceiptsController.TargetSuggestions))]);
        Assert.Equal(PermissionCatalog.WarehouseReceive, Expected[(typeof(ReceiptsController), nameof(ReceiptsController.ApplySuggestedTargets))]);
        // Lote 21 (conteo por producto): vista previa, "Por revisar" y cierre en bloque con warehouse.count; la posición provisional
        // la crea quien captura (warehouse.count.capture) y la confirma el supervisor (warehouse.manage).
        Assert.Equal(PermissionCatalog.WarehouseCount, Expected[(typeof(CycleCountsController), nameof(CycleCountsController.ReconcilePreview))]);
        Assert.Equal(PermissionCatalog.WarehouseCount, Expected[(typeof(CycleCountsController), nameof(CycleCountsController.Review))]);
        Assert.Equal(PermissionCatalog.WarehouseCount, Expected[(typeof(CycleCountsController), nameof(CycleCountsController.ReconcileMatching))]);
        Assert.Equal(PermissionCatalog.WarehouseCountCapture, Expected[(typeof(CycleCountsController), nameof(CycleCountsController.CreateProvisionalBin))]);
        Assert.Equal(PermissionCatalog.WarehouseManage, Expected[(typeof(WarehousesController), nameof(WarehousesController.ConfirmProvisionalBin))]);
        // Informe "Productos por posición": se ve con inventory.view (sin permiso nuevo).
        Assert.Equal(PermissionCatalog.InventoryView, Expected[(typeof(WarehousesController), nameof(WarehousesController.BinProducts))]);
        Assert.Equal(129, Expected.Count);
    }

    [Fact]
    public void No_wms_action_receives_a_tenant_id()
    {
        foreach (var controller in ModuleOf.Keys)
        foreach (var action in Actions(controller))
        foreach (var p in action.GetParameters())
        {
            Assert.False(string.Equals(p.Name, "tenantId", StringComparison.OrdinalIgnoreCase),
                $"{controller.Name}.{action.Name} recibe un parámetro tenantId: el tenant sale del principal.");
            var props = p.ParameterType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            Assert.DoesNotContain(props, pr => string.Equals(pr.Name, "TenantId", StringComparison.OrdinalIgnoreCase));
        }
    }
}
