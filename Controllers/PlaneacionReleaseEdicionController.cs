using ERP.NSQuell.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;

namespace ERP.NSQuell.Controllers;

public partial class PlaneacionReleaseController
{
    // RELEASE_EDICION_FLUJO_V1_0
    [HttpGet]
    public async Task<IActionResult> Editar(int id)
    {
        var detalle = await ObtenerReleaseDetalleAsync(id);
        if (detalle == null)
            return NotFound();

        var vm = new PlaneacionReleaseEditarVm
        {
            ReleaseID = detalle.ReleaseID,
            ClienteID = detalle.ClienteID,
            ClienteNombre = detalle.ClienteNombre,
            FolioRelease = detalle.FolioRelease,
            FolioCliente = detalle.FolioCliente,
            FechaRecepcion = detalle.FechaRecepcion,
            VersionRelease = detalle.VersionRelease,
            ArchivoOrigenNombre = detalle.ArchivoOrigenNombre,
            PlantillaImportacion = detalle.PlantillaImportacion,
            ImportadoDesdeArchivo = detalle.ImportadoDesdeArchivo,
            Observaciones = detalle.Observaciones,
            EstatusID = detalle.EstatusID
        };

        vm.Renglones = detalle.Detalles
            .GroupBy(x => new
            {
                x.ReleaseRenglonID,
                x.Renglon,
                x.ParteID,
                x.NumeroParte,
                x.ReferenciaSAP,
                x.DesignacionDescripcionSAP,
                x.UnidadMedidaCliente,
                x.ContratoCliente
            })
            .OrderBy(x => x.Key.Renglon)
            .Select(group => new PlaneacionReleaseRenglonCrearVm
            {
                Renglon = group.Key.Renglon,
                ParteID = group.Key.ParteID,
                NumeroParte = group.Key.NumeroParte,
                ReferenciaSAP = group.Key.ReferenciaSAP,
                DesignacionDescripcionSAP =
                    group.Key.DesignacionDescripcionSAP,
                UnidadMedidaCliente = group.Key.UnidadMedidaCliente,
                ContratoCliente = group.Key.ContratoCliente,
                Entregas = group
                    .OrderBy(x => x.SecuenciaEntrega ?? int.MaxValue)
                    .ThenBy(x => x.FechaRequerida)
                    .Select((x, index) =>
                        new PlaneacionReleaseEntregaCrearVm
                        {
                            SecuenciaEntrega =
                                x.SecuenciaEntrega ?? index + 1,
                            FechaCarga = x.FechaCarga,
                            FechaRequerida = x.FechaRequerida,
                            CantidadRequerida = x.CantidadRequerida
                        })
                    .ToList()
            })
            .ToList();

        if (vm.Renglones.Count == 0)
        {
            vm.Renglones.Add(new PlaneacionReleaseRenglonCrearVm
            {
                Renglon = 1,
                Entregas = new List<PlaneacionReleaseEntregaCrearVm>
                {
                    new()
                    {
                        SecuenciaEntrega = 1,
                        FechaRequerida = DateTime.Today
                    }
                }
            });
        }

        var impacto =
            await ObtenerImpactoEdicionReleaseAsync(id);

        vm.TienePlaneacionVinculada =
            impacto.ProgramasVinculados > 0;

        vm.TieneProgramaBloqueado =
            impacto.ProgramasBloqueados > 0;

        vm.ProgramasVinculados =
            impacto.ProgramasVinculados;

        await CargarCatalogosAsync(vm);
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Editar(
        PlaneacionReleaseEditarVm vm)
    {
        var usuarioId = ObtenerUsuarioID();

        LimpiarModeloEdicionRelease(vm);

        if (!vm.ConfirmarImpacto)
        {
            ModelState.AddModelError(
                string.Empty,
                "Debes confirmar que comprendes el impacto sobre la planeación.");
        }

        if (!vm.ClienteID.HasValue &&
            string.IsNullOrWhiteSpace(vm.ClienteNombre))
        {
            ModelState.AddModelError(
                string.Empty,
                "Selecciona el cliente del Release.");
        }

        if (vm.Renglones.Count == 0)
        {
            ModelState.AddModelError(
                string.Empty,
                "Debes conservar al menos un renglón.");
        }

        foreach (var renglon in vm.Renglones)
        {
            if (!renglon.ParteID.HasValue)
            {
                ModelState.AddModelError(
                    string.Empty,
                    $"El renglón {renglon.Renglon} no tiene parte seleccionada.");
            }

            if (renglon.Entregas.Count == 0)
            {
                ModelState.AddModelError(
                    string.Empty,
                    $"El renglón {renglon.Renglon} debe tener al menos una entrega.");
            }

            foreach (var entrega in renglon.Entregas)
            {
                if (!entrega.FechaRequerida.HasValue)
                {
                    ModelState.AddModelError(
                        string.Empty,
                        $"El renglón {renglon.Renglon} tiene una entrega sin fecha requerida.");
                }

                if (entrega.CantidadRequerida <= 0)
                {
                    ModelState.AddModelError(
                        string.Empty,
                        $"El renglón {renglon.Renglon} tiene una cantidad menor o igual a cero.");
                }
            }
        }

        var impacto =
            await ObtenerImpactoEdicionReleaseAsync(vm.ReleaseID);

        vm.TienePlaneacionVinculada =
            impacto.ProgramasVinculados > 0;

        vm.TieneProgramaBloqueado =
            impacto.ProgramasBloqueados > 0;

        vm.ProgramasVinculados =
            impacto.ProgramasVinculados;

        if (vm.TieneProgramaBloqueado)
        {
            ModelState.AddModelError(
                string.Empty,
                "Este Release tiene producción en proceso, terminada o cerrada. No puede editarse porque alteraría información productiva ya ejecutada.");
        }

        if (!ModelState.IsValid)
        {
            await CargarCatalogosAsync(vm);
            return View(vm);
        }

        await using var cn =
            new SqlConnection(ConnectionString);

        await cn.OpenAsync();

        await using var tx =
            (SqlTransaction)await cn.BeginTransactionAsync();

        try
        {
            const string sqlLock = @"
SELECT EstatusID
FROM dbo.Planeacion_Releases WITH (UPDLOCK, ROWLOCK)
WHERE ReleaseID = @ReleaseID
  AND Activo = 1;";

            await using (var cmd =
                new SqlCommand(sqlLock, cn, tx))
            {
                cmd.Parameters.Add(
                    "@ReleaseID",
                    SqlDbType.Int).Value = vm.ReleaseID;

                var exists = await cmd.ExecuteScalarAsync();
                if (exists == null || exists == DBNull.Value)
                {
                    await tx.RollbackAsync();
                    return NotFound();
                }
            }

            var bloqueados =
                await ContarProgramasBloqueadosEdicionAsync(
                    vm.ReleaseID,
                    cn,
                    tx);

            if (bloqueados > 0)
            {
                await tx.RollbackAsync();

                ModelState.AddModelError(
                    string.Empty,
                    "La edición fue cancelada porque el Release ya tiene producción en proceso, terminada o cerrada.");

                vm.TieneProgramaBloqueado = true;
                await CargarCatalogosAsync(vm);
                return View(vm);
            }

            await InvalidarPlaneacionPendienteReleaseAsync(
                vm.ReleaseID,
                usuarioId,
                cn,
                tx);

            const string sqlDesactivarFuente = @"
UPDATE dbo.Planeacion_ReleaseDetalle
SET Activo = 0
WHERE ReleaseID = @ReleaseID
  AND Activo = 1;

UPDATE dbo.Planeacion_ReleaseRenglones
SET Activo = 0
WHERE ReleaseID = @ReleaseID
  AND Activo = 1;";

            await using (var cmd =
                new SqlCommand(sqlDesactivarFuente, cn, tx))
            {
                cmd.Parameters.Add(
                    "@ReleaseID",
                    SqlDbType.Int).Value = vm.ReleaseID;

                await cmd.ExecuteNonQueryAsync();
            }

            var clienteNombre = vm.ClienteNombre;

            if (vm.ClienteID.HasValue)
            {
                clienteNombre =
                    await ObtenerClienteNombreAsync(
                        vm.ClienteID.Value,
                        cn,
                        tx);
            }

            const string sqlActualizarRelease = @"
UPDATE dbo.Planeacion_Releases
SET
    FolioRelease = @FolioRelease,
    FolioCliente = @FolioCliente,
    ClienteID = @ClienteID,
    ClienteNombre = @ClienteNombre,
    FechaRecepcion = @FechaRecepcion,
    VersionRelease = @VersionRelease,
    Observaciones = @Observaciones,
    EstatusID = @EstatusID,
    UsuarioModificacionID = @UsuarioID,
    FechaModificacion = GETDATE()
WHERE ReleaseID = @ReleaseID
  AND Activo = 1;";

            await using (var cmd =
                new SqlCommand(sqlActualizarRelease, cn, tx))
            {
                cmd.Parameters.Add(
                    "@ReleaseID",
                    SqlDbType.Int).Value = vm.ReleaseID;

                cmd.Parameters.Add(
                    "@FolioRelease",
                    SqlDbType.NVarChar,
                    40).Value =
                    (object?)vm.FolioRelease ?? DBNull.Value;

                cmd.Parameters.Add(
                    "@FolioCliente",
                    SqlDbType.NVarChar,
                    100).Value =
                    (object?)vm.FolioCliente ?? DBNull.Value;

                cmd.Parameters.Add(
                    "@ClienteID",
                    SqlDbType.Int).Value =
                    (object?)vm.ClienteID ?? DBNull.Value;

                cmd.Parameters.Add(
                    "@ClienteNombre",
                    SqlDbType.NVarChar,
                    200).Value =
                    (object?)clienteNombre ?? DBNull.Value;

                cmd.Parameters.Add(
                    "@FechaRecepcion",
                    SqlDbType.Date).Value =
                    vm.FechaRecepcion.Date;

                cmd.Parameters.Add(
                    "@VersionRelease",
                    SqlDbType.NVarChar,
                    50).Value =
                    (object?)vm.VersionRelease ?? DBNull.Value;

                cmd.Parameters.Add(
                    "@Observaciones",
                    SqlDbType.NVarChar,
                    500).Value =
                    (object?)vm.Observaciones ?? DBNull.Value;

                cmd.Parameters.Add(
                    "@EstatusID",
                    SqlDbType.Int).Value =
                    PlaneacionReleaseEstatus.Calculado;

                cmd.Parameters.Add(
                    "@UsuarioID",
                    SqlDbType.Int).Value = usuarioId;

                await cmd.ExecuteNonQueryAsync();
            }

            var renglonNumero = 1;

            foreach (var renglon in vm.Renglones)
            {
                renglon.Renglon = renglonNumero;

                await CompletarRenglonDesdeParteAsync(
                    renglon,
                    cn,
                    tx);

                var releaseRenglonId =
                    await InsertarReleaseRenglonAsync(
                        vm.ReleaseID,
                        renglon,
                        usuarioId,
                        cn,
                        tx);

                var secuencia = 1;

                foreach (var entrega in renglon.Entregas)
                {
                    // RELEASE_FECHA_CARGA_AUTO_EDICION_V1_0
                    if (vm.ImportadoDesdeArchivo &&
                        !entrega.FechaCarga.HasValue &&
                        entrega.FechaRequerida.HasValue)
                    {
                        entrega.FechaCarga =
                            entrega.FechaRequerida.Value.Date.AddDays(-1);
                    }
                    entrega.SecuenciaEntrega = secuencia;

                    var detalle =
                        CrearDetalleDesdeRenglonEntrega(
                            renglon,
                            entrega);

                    await CompletarDetalleDesdeParteAsync(
                        detalle,
                        cn,
                        tx);

                    await CalcularNecesidadAsync(
                        detalle,
                        cn,
                        tx);

                    await InsertarReleaseDetalleAsync(
                        vm.ReleaseID,
                        releaseRenglonId,
                        secuencia,
                        detalle,
                        usuarioId,
                        cn,
                        tx);

                    secuencia++;
                }

                renglonNumero++;
            }

            await ActualizarEstatusReleaseAsync(
                vm.ReleaseID,
                PlaneacionReleaseEstatus.Calculado,
                usuarioId,
                cn,
                tx);

            await tx.CommitAsync();

            TempData["Success"] =
                impacto.ProgramasVinculados > 0
                    ? "Release actualizado. La programación pendiente relacionada fue cancelada para que el flujo se genere nuevamente con los datos corregidos."
                    : "Release actualizado y recalculado correctamente.";

            return RedirectToAction(
                nameof(Detalle),
                new { id = vm.ReleaseID });
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();

            ModelState.AddModelError(
                string.Empty,
                "No fue posible editar el Release: " +
                ex.Message);

            await CargarCatalogosAsync(vm);
            return View(vm);
        }
    }

    private static void LimpiarModeloEdicionRelease(
        PlaneacionReleaseEditarVm vm)
    {
        vm.Renglones ??=
            new List<PlaneacionReleaseRenglonCrearVm>();

        vm.Renglones = vm.Renglones
            .Where(x =>
                x.ParteID.HasValue ||
                !string.IsNullOrWhiteSpace(x.NumeroParte) ||
                !string.IsNullOrWhiteSpace(x.ReferenciaSAP))
            .ToList();

        foreach (var renglon in vm.Renglones)
        {
            renglon.Entregas ??=
                new List<PlaneacionReleaseEntregaCrearVm>();

            renglon.Entregas = renglon.Entregas
                .Where(x =>
                    x.FechaRequerida.HasValue ||
                    x.CantidadRequerida > 0)
                .ToList();
        }

        vm.Renglones = vm.Renglones
            .Where(x => x.Entregas.Count > 0)
            .ToList();

        for (var i = 0; i < vm.Renglones.Count; i++)
        {
            vm.Renglones[i].Renglon = i + 1;

            for (var j = 0;
                 j < vm.Renglones[i].Entregas.Count;
                 j++)
            {
                vm.Renglones[i]
                    .Entregas[j]
                    .SecuenciaEntrega = j + 1;
            }
        }
    }

    private async Task<(
        int ProgramasVinculados,
        int ProgramasBloqueados)>
        ObtenerImpactoEdicionReleaseAsync(int releaseId)
    {
        await using var cn =
            new SqlConnection(ConnectionString);

        await cn.OpenAsync();

        const string sql = @"
SELECT
    COUNT(DISTINCT pp.ProgramaProduccionID) AS TotalProgramas,
    COUNT(DISTINCT CASE
        WHEN pp.EstatusID IN (
            @EnProduccion,
            @Terminado,
            @Cerrado
        )
        THEN pp.ProgramaProduccionID
    END) AS Bloqueados
FROM dbo.Planeacion_ReleaseDetalle d
LEFT JOIN dbo.Planeacion_ProgramaProduccion pp
    ON pp.ReleaseDetalleID = d.ReleaseDetalleID
   AND pp.Activo = 1
WHERE d.ReleaseID = @ReleaseID
  AND d.Activo = 1;";

        await using var cmd =
            new SqlCommand(sql, cn);

        cmd.Parameters.Add(
            "@ReleaseID",
            SqlDbType.Int).Value = releaseId;

        cmd.Parameters.Add(
            "@EnProduccion",
            SqlDbType.Int).Value =
            PlaneacionProgramaEstatus.EnProduccion;

        cmd.Parameters.Add(
            "@Terminado",
            SqlDbType.Int).Value =
            PlaneacionProgramaEstatus.Terminado;

        cmd.Parameters.Add(
            "@Cerrado",
            SqlDbType.Int).Value =
            PlaneacionProgramaEstatus.Cerrado;

        await using var rd =
            await cmd.ExecuteReaderAsync();

        if (!await rd.ReadAsync())
            return (0, 0);

        return (
            rd["TotalProgramas"] == DBNull.Value
                ? 0
                : Convert.ToInt32(rd["TotalProgramas"]),

            rd["Bloqueados"] == DBNull.Value
                ? 0
                : Convert.ToInt32(rd["Bloqueados"]));
    }

    private static async Task<int>
        ContarProgramasBloqueadosEdicionAsync(
            int releaseId,
            SqlConnection cn,
            SqlTransaction tx)
    {
        const string sql = @"
SELECT COUNT(DISTINCT pp.ProgramaProduccionID)
FROM dbo.Planeacion_ReleaseDetalle d
INNER JOIN dbo.Planeacion_ProgramaProduccion pp
    ON pp.ReleaseDetalleID = d.ReleaseDetalleID
   AND pp.Activo = 1
WHERE d.ReleaseID = @ReleaseID
  AND d.Activo = 1
  AND pp.EstatusID IN (
      @EnProduccion,
      @Terminado,
      @Cerrado
  );";

        await using var cmd =
            new SqlCommand(sql, cn, tx);

        cmd.Parameters.Add(
            "@ReleaseID",
            SqlDbType.Int).Value = releaseId;

        cmd.Parameters.Add(
            "@EnProduccion",
            SqlDbType.Int).Value =
            PlaneacionProgramaEstatus.EnProduccion;

        cmd.Parameters.Add(
            "@Terminado",
            SqlDbType.Int).Value =
            PlaneacionProgramaEstatus.Terminado;

        cmd.Parameters.Add(
            "@Cerrado",
            SqlDbType.Int).Value =
            PlaneacionProgramaEstatus.Cerrado;

        return Convert.ToInt32(
            await cmd.ExecuteScalarAsync());
    }

    private static async Task
        InvalidarPlaneacionPendienteReleaseAsync(
            int releaseId,
            int usuarioId,
            SqlConnection cn,
            SqlTransaction tx)
    {
        const string sql = @"
DECLARE @SolicitudesDetalle TABLE
(
    SolicitudProduccionDetalleID INT PRIMARY KEY
);

INSERT INTO @SolicitudesDetalle
(
    SolicitudProduccionDetalleID
)
SELECT DISTINCT pp.SolicitudProduccionDetalleID
FROM dbo.Planeacion_ProgramaProduccion pp
INNER JOIN dbo.Planeacion_ReleaseDetalle d
    ON d.ReleaseDetalleID = pp.ReleaseDetalleID
WHERE d.ReleaseID = @ReleaseID
  AND d.Activo = 1
  AND pp.Activo = 1
  AND pp.SolicitudProduccionDetalleID IS NOT NULL
  AND pp.EstatusID NOT IN (
      @EnProduccion,
      @Terminado,
      @Cerrado
  );

UPDATE pp
SET
    pp.EstatusID = @Cancelado,
    pp.Activo = 0,
    pp.UsuarioModificacionID = @UsuarioID,
    pp.FechaModificacion = GETDATE()
FROM dbo.Planeacion_ProgramaProduccion pp
INNER JOIN dbo.Planeacion_ReleaseDetalle d
    ON d.ReleaseDetalleID = pp.ReleaseDetalleID
WHERE d.ReleaseID = @ReleaseID
  AND d.Activo = 1
  AND pp.Activo = 1
  AND pp.EstatusID NOT IN (
      @EnProduccion,
      @Terminado,
      @Cerrado
  );

IF OBJECT_ID(
    'dbo.SolicitudesProduccionAsignacionMaquina',
    'U') IS NOT NULL
BEGIN
    UPDATE asignacion
    SET asignacion.Activo = 0
    FROM dbo.SolicitudesProduccionAsignacionMaquina asignacion
    INNER JOIN @SolicitudesDetalle ids
        ON ids.SolicitudProduccionDetalleID =
           asignacion.SolicitudProduccionDetalleID
    WHERE asignacion.Activo = 1;
END;

IF OBJECT_ID(
    'dbo.SolicitudesProduccionDetalle',
    'U') IS NOT NULL
BEGIN
    UPDATE detalle
    SET detalle.Activo = 0
    FROM dbo.SolicitudesProduccionDetalle detalle
    INNER JOIN @SolicitudesDetalle ids
        ON ids.SolicitudProduccionDetalleID =
           detalle.SolicitudProduccionDetalleID
    WHERE detalle.Activo = 1;
END;";

        await using var cmd =
            new SqlCommand(sql, cn, tx);

        cmd.Parameters.Add(
            "@ReleaseID",
            SqlDbType.Int).Value = releaseId;

        cmd.Parameters.Add(
            "@UsuarioID",
            SqlDbType.Int).Value = usuarioId;

        cmd.Parameters.Add(
            "@EnProduccion",
            SqlDbType.Int).Value =
            PlaneacionProgramaEstatus.EnProduccion;

        cmd.Parameters.Add(
            "@Terminado",
            SqlDbType.Int).Value =
            PlaneacionProgramaEstatus.Terminado;

        cmd.Parameters.Add(
            "@Cerrado",
            SqlDbType.Int).Value =
            PlaneacionProgramaEstatus.Cerrado;

        cmd.Parameters.Add(
            "@Cancelado",
            SqlDbType.Int).Value =
            PlaneacionProgramaEstatus.Cancelado;

        await cmd.ExecuteNonQueryAsync();
    }
}