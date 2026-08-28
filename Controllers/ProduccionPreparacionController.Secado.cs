using ERP.NSQuell.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace ERP.NSQuell.Controllers
{
    public sealed partial class ProduccionPreparacionController
    {
        private async Task<IActionResult> ConstruirSecadoOperativoAsync(string? filtro, int? maquinaId)
        {
            if (!UsuarioEnSesion())
                return RedirectToAction("Login", "Login");

            filtro = string.IsNullOrWhiteSpace(filtro) ? null : filtro.Trim();

            if (maquinaId.HasValue && maquinaId.Value <= 0)
                maquinaId = null;

            var usuarioId = ObtenerUsuarioID();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            var permisos = await ObtenerPermisosPreparacionUsuarioAsync(usuarioId, cn);

            if (!permisos.PuedeVerModulo)
                return StatusCode(StatusCodes.Status403Forbidden);

            await using (var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable))
            {
                try
                {
                    await SincronizarPreparacionAnticipadaAsync(usuarioId, cn, tx);
                    await SincronizarSecadoMaterialConPlaneacionAsync(usuarioId, cn, tx);
                    await ConsolidarSecadosPendientesSinIniciarAsync(usuarioId, cn, tx);
                    await tx.CommitAsync();
                }
                catch (Exception ex)
                {
                    try { await tx.RollbackAsync(); } catch { }
                    TempData["Error"] = "No fue posible sincronizar Secado con Planeación: " + ex.Message;
                }
            }

            var ahora = await ObtenerFechaServidorSecadoAsync(cn);
            var configuracion = await CargarConfiguracionSecadoAsync(cn);
            var maquinas = await CargarMaquinasPreparacionAsync(cn);
            var tolvas = await CargarTolvasSecadoAsync(cn);
            var materiales = await CargarMaterialesSecadoAsync(filtro, maquinaId, ahora, configuracion, cn);

            var tareasPlaneadas = await CargarPreparacionAnticipadaAsync(ProduccionPreparacionTipo.SecadoMaterial, filtro, maquinaId, ahora, false, cn);

            var programasConMaterial = materiales
                .Where(x => x.ProgramaProduccionID.HasValue)
                .Select(x => x.ProgramaProduccionID!.Value)
                .ToHashSet();

            var pendientesPlaneacion = tareasPlaneadas
                .Where(x =>
                    (x.EstaPendiente || x.EstaEnProceso) &&
                    (
                        x.CantidadMpKg.GetValueOrDefault() > ProduccionSecadoReglas.ToleranciaCantidad
                            ? x.CantidadMpPendienteRecepcionKg > ProduccionSecadoReglas.ToleranciaCantidad
                            : !programasConMaterial.Contains(x.ProgramaProduccionID)
                    ))
                .OrderBy(x => x.FechaAviso)
                .ThenBy(x => x.FechaObjetivo)
                .ThenBy(x => x.ProgramaProduccionID)
                .ToList();

            var vm = new ProduccionSecadoIndexVm
            {
                FechaConsulta = ahora,
                Filtro = filtro,
                MaquinaID = maquinaId,
                PuedeGestionarSecado = permisos.PuedeGestionarSecado,
                Configuracion = configuracion,
                Maquinas = maquinas,
                Tolvas = tolvas,
                Materiales = materiales,
                PendientesPlaneacion = pendientesPlaneacion
            };

            return View("Secado", vm);
        }
        private async Task SincronizarSecadoMaterialConPlaneacionAsync(
    int usuarioId,
    SqlConnection cn,
    SqlTransaction tx)
        {
            const string sql = @"
;WITH Datos AS
(
    SELECT
        sm.SecadoMaterialID,
        pp.MaquinaID,
        pp.FechaInicioProgramada,
        pp.Arranque,
        d.TipoSecado,
        d.HorasSecado,

        CASE
            WHEN EXISTS
            (
                SELECT 1
                FROM dbo.Produccion_SecadoCargas c
                WHERE c.SecadoMaterialID=sm.SecadoMaterialID
                  AND c.Activo=1
                  AND c.Estado=N'EN_PROCESO'
            )
            THEN CAST(1 AS bit)
            ELSE CAST(0 AS bit)
        END AS TieneCargaActiva,

        ejecucion.EjecucionProduccionID

    FROM dbo.Produccion_SecadoMaterial sm
    INNER JOIN dbo.Planeacion_ProgramaProduccion pp
        ON pp.ProgramaProduccionID=sm.ProgramaProduccionID

    LEFT JOIN dbo.SolicitudesProduccionDetalle d
        ON d.SolicitudProduccionDetalleID=pp.SolicitudProduccionDetalleID
       AND d.Activo=1

    OUTER APPLY
    (
        SELECT TOP(1)
            e.EjecucionProduccionID
        FROM dbo.Produccion_Ejecucion e
        WHERE e.ProgramaProduccionID=pp.ProgramaProduccionID
          AND e.Activo=1
        ORDER BY e.EjecucionProduccionID DESC
    ) ejecucion

    WHERE sm.Activo=1
      AND sm.Estado<>N'CANCELADO'
      AND sm.Estado<>N'FINALIZADO'
      AND pp.Activo=1
      AND ISNULL(pp.EstatusID,1) NOT IN(5,6,9,99)
),
Fechas AS
(
    SELECT
        d.*,

        CASE
            WHEN d.FechaInicioProgramada IS NULL
                THEN NULL

            WHEN d.Arranque IS NULL
                THEN d.FechaInicioProgramada

            ELSE
                CASE
                    WHEN DATEADD
                    (
                        SECOND,
                        DATEDIFF
                        (
                            SECOND,
                            CAST('00:00:00' AS time),
                            d.Arranque
                        ),
                        CAST(
                            CAST(d.FechaInicioProgramada AS date)
                            AS datetime2
                        )
                    ) < d.FechaInicioProgramada
                    THEN DATEADD
                    (
                        DAY,
                        1,
                        DATEADD
                        (
                            SECOND,
                            DATEDIFF
                            (
                                SECOND,
                                CAST('00:00:00' AS time),
                                d.Arranque
                            ),
                            CAST(
                                CAST(d.FechaInicioProgramada AS date)
                                AS datetime2
                            )
                        )
                    )

                    ELSE DATEADD
                    (
                        SECOND,
                        DATEDIFF
                        (
                            SECOND,
                            CAST('00:00:00' AS time),
                            d.Arranque
                        ),
                        CAST(
                            CAST(d.FechaInicioProgramada AS date)
                            AS datetime2
                        )
                    )
                END
        END AS FechaArranqueCalculada

    FROM Datos d
),
Valores AS
(
    SELECT
        f.*,

        CASE
            WHEN f.HorasSecado IS NOT NULL
             AND f.HorasSecado>0
                THEN CONVERT
                (
                    int,
                    CEILING(
                        CONVERT(decimal(18,4),f.HorasSecado)*60
                    )
                )
            ELSE NULL
        END AS NuevosMinutosSecado,

        CASE
            WHEN UPPER(
                LTRIM(
                    RTRIM(
                        ISNULL(f.TipoSecado,N'')
                    )
                )
            ) LIKE N'%DESHUM%'
              OR UPPER(
                LTRIM(
                    RTRIM(
                        ISNULL(f.TipoSecado,N'')
                    )
                )
            ) LIKE N'%DESUM%'
                THEN N'DESHUMIDIFICADO'

            ELSE N'SECADO'
        END AS NuevoTipoProceso

    FROM Fechas f
)

UPDATE sm
SET
    sm.MaquinaProgramadaID=v.MaquinaID,

    sm.EjecucionProduccionID=
        COALESCE(
            v.EjecucionProduccionID,
            sm.EjecucionProduccionID
        ),

    sm.TipoSecadoOrigen=
        CASE
            WHEN v.TieneCargaActiva=1
                THEN sm.TipoSecadoOrigen
            ELSE NULLIF(
                LTRIM(RTRIM(v.TipoSecado)),
                N''
            )
        END,

    sm.TipoProceso=
        CASE
            WHEN v.TieneCargaActiva=1
                THEN sm.TipoProceso
            WHEN v.HorasSecado IS NOT NULL
             AND v.HorasSecado>0
                THEN v.NuevoTipoProceso
            ELSE sm.TipoProceso
        END,

    sm.HorasSecadoRequeridas=
        CASE
            WHEN v.TieneCargaActiva=1
                THEN sm.HorasSecadoRequeridas
            WHEN v.HorasSecado IS NOT NULL
             AND v.HorasSecado>0
                THEN v.HorasSecado
            ELSE sm.HorasSecadoRequeridas
        END,

    sm.MinutosSecadoRequeridos=
        CASE
            WHEN v.TieneCargaActiva=1
                THEN sm.MinutosSecadoRequeridos
            WHEN v.NuevosMinutosSecado IS NOT NULL
             AND v.NuevosMinutosSecado>0
                THEN v.NuevosMinutosSecado
            ELSE sm.MinutosSecadoRequeridos
        END,

    sm.FechaArranqueProduccion=
        v.FechaArranqueCalculada,

    sm.FechaInicioSecadoObjetivo=
        CASE
            WHEN v.FechaArranqueCalculada IS NULL
                THEN NULL

            WHEN v.TieneCargaActiva=1
                THEN DATEADD(
                    MINUTE,
                    -sm.MinutosSecadoRequeridos,
                    v.FechaArranqueCalculada
                )

            WHEN v.NuevosMinutosSecado IS NOT NULL
             AND v.NuevosMinutosSecado>0
                THEN DATEADD(
                    MINUTE,
                    -v.NuevosMinutosSecado,
                    v.FechaArranqueCalculada
                )

            ELSE DATEADD(
                MINUTE,
                -sm.MinutosSecadoRequeridos,
                v.FechaArranqueCalculada
            )
        END,

    sm.FechaLimiteEntregaMaterial=
        CASE
            WHEN v.FechaArranqueCalculada IS NULL
                THEN NULL

            WHEN v.TieneCargaActiva=1
                THEN DATEADD(
                    MINUTE,
                    -sm.MargenEntregaAntesSecadoMinutos,
                    DATEADD(
                        MINUTE,
                        -sm.MinutosSecadoRequeridos,
                        v.FechaArranqueCalculada
                    )
                )

            WHEN v.NuevosMinutosSecado IS NOT NULL
             AND v.NuevosMinutosSecado>0
                THEN DATEADD(
                    MINUTE,
                    -sm.MargenEntregaAntesSecadoMinutos,
                    DATEADD(
                        MINUTE,
                        -v.NuevosMinutosSecado,
                        v.FechaArranqueCalculada
                    )
                )

            ELSE DATEADD(
                MINUTE,
                -sm.MargenEntregaAntesSecadoMinutos,
                DATEADD(
                    MINUTE,
                    -sm.MinutosSecadoRequeridos,
                    v.FechaArranqueCalculada
                )
            )
        END,

    sm.FechaObjetivoFinSecado=
        v.FechaArranqueCalculada,

    sm.UsuarioModificacionID=@UsuarioID,
    sm.FechaModificacion=SYSDATETIME()

FROM dbo.Produccion_SecadoMaterial sm
INNER JOIN Valores v
    ON v.SecadoMaterialID=sm.SecadoMaterialID;


UPDATE sm
SET
    sm.Estado=N'CANCELADO',
    sm.Activo=0,
    sm.UsuarioModificacionID=@UsuarioID,
    sm.FechaModificacion=SYSDATETIME(),
    sm.Observaciones=
        LEFT(
            CASE
                WHEN sm.Observaciones IS NULL
                  OR LTRIM(RTRIM(sm.Observaciones))=N''
                    THEN N'Secado cancelado automáticamente porque Planeación ya no requiere secado para este programa.'

                ELSE
                    sm.Observaciones+
                    CHAR(13)+CHAR(10)+
                    N'Secado cancelado automáticamente porque Planeación ya no requiere secado para este programa.'
            END,
            1000
        )

FROM dbo.Produccion_SecadoMaterial sm
INNER JOIN dbo.Planeacion_ProgramaProduccion pp
    ON pp.ProgramaProduccionID=sm.ProgramaProduccionID

LEFT JOIN dbo.SolicitudesProduccionDetalle d
    ON d.SolicitudProduccionDetalleID=pp.SolicitudProduccionDetalleID
   AND d.Activo=1

WHERE sm.Activo=1
  AND sm.Estado IN(N'PENDIENTE',N'PARCIAL')
  AND ISNULL(sm.CantidadAsignadaKg,0)<=0.0005
  AND ISNULL(sm.CantidadFinalizadaKg,0)<=0.0005

  AND
  (
      pp.Activo=0
      OR ISNULL(pp.EstatusID,1) IN(5,6,9,99)
      OR ISNULL(d.HorasSecado,0)<=0
  )

  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.Produccion_SecadoCargas c
      WHERE c.SecadoMaterialID=sm.SecadoMaterialID
        AND c.Activo=1
        AND c.Estado=N'EN_PROCESO'
  );";

            await using var cmd =
                new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add(
                "@UsuarioID",
                SqlDbType.Int).Value = usuarioId;

            await cmd.ExecuteNonQueryAsync();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> IniciarSecado(ProduccionIniciarSecadoVm vm)
        {
            if (!UsuarioEnSesion()) return RedirectToAction("Login", "Login");
            if (vm.SecadoMaterialID <= 0 || vm.TolvaID <= 0)
            {
                TempData["Error"] = "No se recibió correctamente el material o la tolva.";
                return RedirectToAction(nameof(Secado));
            }
            if (vm.CantidadKg <= ProduccionSecadoReglas.ToleranciaCantidad)
            {
                TempData["Error"] = "La cantidad a secar debe ser mayor a cero.";
                return RedirectToAction(nameof(Secado));
            }

            var observaciones = string.IsNullOrWhiteSpace(vm.Observaciones) ? null : vm.Observaciones.Trim();
            if (observaciones?.Length > 1000)
            {
                TempData["Error"] = "Las observaciones no pueden superar 1000 caracteres.";
                return RedirectToAction(nameof(Secado));
            }

            var usuarioId = ObtenerUsuarioID();
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            var permisos = await ObtenerPermisosPreparacionUsuarioAsync(usuarioId, cn);
            if (!permisos.PuedeGestionarSecado) return StatusCode(StatusCodes.Status403Forbidden);
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);

            try
            {
                const string sqlMaterial = @"
SELECT TOP(1)
    SecadoMaterialID,
    ProgramaProduccionID,
    NumeroOFSnapshot,
    MaterialCodigoSnapshot,
    CantidadRecibidaKg,
    CantidadAsignadaKg,
    CantidadFinalizadaKg,
    TipoProceso,
    MinutosSecadoRequeridos,
    FechaRecepcionProduccion,
    Estado
FROM dbo.Produccion_SecadoMaterial WITH(UPDLOCK,HOLDLOCK)
WHERE SecadoMaterialID=@SecadoMaterialID
  AND Activo=1;";

                int? programaProduccionId;
                string numeroOF;
                string materialCodigo;
                decimal cantidadRecibida;
                decimal cantidadAsignada;
                string tipoProceso;
                int duracionRequeridaMinutos;
                DateTime fechaRecepcion;
                string estadoMaterial;

                await using (var cmd = new SqlCommand(sqlMaterial, cn, tx))
                {
                    cmd.Parameters.Add("@SecadoMaterialID", SqlDbType.BigInt).Value = vm.SecadoMaterialID;
                    await using var rd = await cmd.ExecuteReaderAsync();
                    if (!await rd.ReadAsync())
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] = "El material pendiente de secado ya no existe.";
                        return RedirectToAction(nameof(Secado));
                    }

                    programaProduccionId = rd["ProgramaProduccionID"] == DBNull.Value ? null : Convert.ToInt32(rd["ProgramaProduccionID"]);
                    numeroOF = rd["NumeroOFSnapshot"]?.ToString()?.Trim() ?? string.Empty;
                    materialCodigo = rd["MaterialCodigoSnapshot"]?.ToString()?.Trim() ?? string.Empty;
                    cantidadRecibida = Convert.ToDecimal(rd["CantidadRecibidaKg"]);
                    cantidadAsignada = Convert.ToDecimal(rd["CantidadAsignadaKg"]);
                    tipoProceso = rd["TipoProceso"]?.ToString()?.Trim() ?? ProduccionSecadoTipoProceso.Secado;
                    duracionRequeridaMinutos = Convert.ToInt32(rd["MinutosSecadoRequeridos"]);
                    fechaRecepcion = Convert.ToDateTime(rd["FechaRecepcionProduccion"]);
                    estadoMaterial = rd["Estado"]?.ToString()?.Trim() ?? ProduccionSecadoEstadoMaterial.Pendiente;
                }

                if (string.Equals(estadoMaterial, ProduccionSecadoEstadoMaterial.Finalizado, StringComparison.OrdinalIgnoreCase) || string.Equals(estadoMaterial, ProduccionSecadoEstadoMaterial.Cancelado, StringComparison.OrdinalIgnoreCase))
                {
                    await tx.RollbackAsync();
                    TempData["Warning"] = "El material ya no admite nuevas cargas de secado.";
                    return RedirectToAction(nameof(Secado));
                }

                if (duracionRequeridaMinutos <= 0) throw new InvalidOperationException("El material no tiene un tiempo de secado válido.");

                const string sqlCargaActiva = @"
SELECT COUNT(1)
FROM dbo.Produccion_SecadoCargas WITH(UPDLOCK,HOLDLOCK)
WHERE SecadoMaterialID=@SecadoMaterialID
  AND Activo=1
  AND Estado=N'EN_PROCESO';";

                await using (var cmd = new SqlCommand(sqlCargaActiva, cn, tx))
                {
                    cmd.Parameters.Add("@SecadoMaterialID", SqlDbType.BigInt).Value = vm.SecadoMaterialID;
                    if (Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0)
                    {
                        await tx.RollbackAsync();
                        TempData["Warning"] = "Este material ya tiene una carga de secado en proceso. Finalízala antes de iniciar otra.";
                        return RedirectToAction(nameof(Secado));
                    }
                }

                var cantidadPendienteAsignar = Math.Max(0m, cantidadRecibida - cantidadAsignada);
                if (cantidadPendienteAsignar <= ProduccionSecadoReglas.ToleranciaCantidad)
                {
                    await tx.RollbackAsync();
                    TempData["Warning"] = "Toda la cantidad recibida ya fue asignada a cargas de secado.";
                    return RedirectToAction(nameof(Secado));
                }

                if (vm.CantidadKg - cantidadPendienteAsignar > ProduccionSecadoReglas.ToleranciaCantidad)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = $"Solo quedan {cantidadPendienteAsignar:0.####} KG pendientes por asignar.";
                    return RedirectToAction(nameof(Secado));
                }

                const string sqlTolva = @"
SELECT TOP(1)
    TolvaID,
    MaquinaID,
    Codigo,
    Nombre,
    CapacidadKg,
    TipoProcesoPermitido,
    DisponibleOperativamente,
    Activo
FROM dbo.Produccion_SecadoTolvas WITH(UPDLOCK,HOLDLOCK)
WHERE TolvaID=@TolvaID;";

                decimal capacidadTolva;
                string tipoProcesoPermitido;
                string tolvaCodigo;
                bool tolvaDisponible;
                bool tolvaActiva;

                await using (var cmd = new SqlCommand(sqlTolva, cn, tx))
                {
                    cmd.Parameters.Add("@TolvaID", SqlDbType.Int).Value = vm.TolvaID;
                    await using var rd = await cmd.ExecuteReaderAsync();
                    if (!await rd.ReadAsync())
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] = "La tolva seleccionada ya no existe.";
                        return RedirectToAction(nameof(Secado));
                    }

                    capacidadTolva = Convert.ToDecimal(rd["CapacidadKg"]);
                    tipoProcesoPermitido = rd["TipoProcesoPermitido"]?.ToString()?.Trim() ?? "AMBOS";
                    tolvaCodigo = rd["Codigo"]?.ToString()?.Trim() ?? string.Empty;
                    tolvaDisponible = rd["DisponibleOperativamente"] != DBNull.Value && Convert.ToBoolean(rd["DisponibleOperativamente"]);
                    tolvaActiva = rd["Activo"] != DBNull.Value && Convert.ToBoolean(rd["Activo"]);
                }

                if (!tolvaActiva || !tolvaDisponible)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "La tolva seleccionada no está disponible operativamente.";
                    return RedirectToAction(nameof(Secado));
                }

                if (vm.CantidadKg - capacidadTolva > ProduccionSecadoReglas.ToleranciaCantidad)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = $"La tolva {tolvaCodigo} tiene capacidad temporal de {capacidadTolva:0.####} KG. Reduce la cantidad de esta carga.";
                    return RedirectToAction(nameof(Secado));
                }

                if (!string.Equals(tipoProcesoPermitido, "AMBOS", StringComparison.OrdinalIgnoreCase) && !string.Equals(tipoProcesoPermitido, tipoProceso, StringComparison.OrdinalIgnoreCase))
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = $"La tolva seleccionada no está habilitada para el proceso {tipoProceso}.";
                    return RedirectToAction(nameof(Secado));
                }

                const string sqlTolvaOcupada = @"
SELECT COUNT(1)
FROM dbo.Produccion_SecadoCargaSegmentos WITH(UPDLOCK,HOLDLOCK)
WHERE TolvaID=@TolvaID
  AND Activo=1
  AND FechaFin IS NULL;";

                await using (var cmd = new SqlCommand(sqlTolvaOcupada, cn, tx))
                {
                    cmd.Parameters.Add("@TolvaID", SqlDbType.Int).Value = vm.TolvaID;
                    if (Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0)
                    {
                        await tx.RollbackAsync();
                        TempData["Warning"] = "La tolva seleccionada está ocupada por otra carga.";
                        return RedirectToAction(nameof(Secado));
                    }
                }

                var ahora = await ObtenerFechaServidorSecadoAsync(cn, tx);
                var fechaDisponibleDesde = fechaRecepcion;

                const string sqlUltimaCarga = @"
SELECT MAX(FechaFinReal)
FROM dbo.Produccion_SecadoCargas
WHERE SecadoMaterialID=@SecadoMaterialID
  AND Activo=1
  AND Estado=N'FINALIZADA';";

                await using (var cmd = new SqlCommand(sqlUltimaCarga, cn, tx))
                {
                    cmd.Parameters.Add("@SecadoMaterialID", SqlDbType.BigInt).Value = vm.SecadoMaterialID;
                    var valor = await cmd.ExecuteScalarAsync();
                    if (valor != null && valor != DBNull.Value)
                    {
                        var ultimaFecha = Convert.ToDateTime(valor);
                        if (ultimaFecha > fechaDisponibleDesde) fechaDisponibleDesde = ultimaFecha;
                    }
                }

                var minutosEspera = Math.Max(0, (int)Math.Floor((ahora - fechaDisponibleDesde).TotalMinutes));
                var fechaFinEsperada = ahora.AddMinutes(duracionRequeridaMinutos);
                var numeroCarga = 1;

                const string sqlNumeroCarga = @"
SELECT ISNULL(MAX(NumeroCarga),0)+1
FROM dbo.Produccion_SecadoCargas WITH(UPDLOCK,HOLDLOCK)
WHERE SecadoMaterialID=@SecadoMaterialID;";

                await using (var cmd = new SqlCommand(sqlNumeroCarga, cn, tx))
                {
                    cmd.Parameters.Add("@SecadoMaterialID", SqlDbType.BigInt).Value = vm.SecadoMaterialID;
                    numeroCarga = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                }

                const string sqlInsertarCarga = @"
INSERT dbo.Produccion_SecadoCargas
(
    SecadoMaterialID,NumeroCarga,TolvaIDActual,CantidadKg,CapacidadTolvaKgSnapshot,DuracionRequeridaMinutos,Estado,
    FechaDisponibleDesde,FechaAsignacionTolva,FechaInicioReal,FechaFinEsperada,FechaFinReal,MinutosEsperaAntesInicio,
    DuracionRealMinutos,MinutosExcesoSecado,ExcedioTiempo,FinalizoAntesTiempo,MotivoFinalizacionAnticipada,
    UsuarioInicioID,UsuarioFinID,Observaciones,Activo,UsuarioCreacionID,FechaCreacion,UsuarioModificacionID,FechaModificacion
)
OUTPUT INSERTED.SecadoCargaID
VALUES
(
    @SecadoMaterialID,@NumeroCarga,@TolvaID,@CantidadKg,@CapacidadTolvaKg,@DuracionRequeridaMinutos,N'EN_PROCESO',
    @FechaDisponibleDesde,@Ahora,@Ahora,@FechaFinEsperada,NULL,@MinutosEspera,
    NULL,NULL,0,0,NULL,@UsuarioID,NULL,@Observaciones,1,@UsuarioID,@Ahora,NULL,NULL
);";

                long secadoCargaId;
                await using (var cmd = new SqlCommand(sqlInsertarCarga, cn, tx))
                {
                    cmd.Parameters.Add("@SecadoMaterialID", SqlDbType.BigInt).Value = vm.SecadoMaterialID;
                    cmd.Parameters.Add("@NumeroCarga", SqlDbType.Int).Value = numeroCarga;
                    cmd.Parameters.Add("@TolvaID", SqlDbType.Int).Value = vm.TolvaID;

                    var pCantidad = cmd.Parameters.Add("@CantidadKg", SqlDbType.Decimal);
                    pCantidad.Precision = 18;
                    pCantidad.Scale = 4;
                    pCantidad.Value = vm.CantidadKg;

                    var pCapacidad = cmd.Parameters.Add("@CapacidadTolvaKg", SqlDbType.Decimal);
                    pCapacidad.Precision = 18;
                    pCapacidad.Scale = 4;
                    pCapacidad.Value = capacidadTolva;

                    cmd.Parameters.Add("@DuracionRequeridaMinutos", SqlDbType.Int).Value = duracionRequeridaMinutos;
                    cmd.Parameters.Add("@FechaDisponibleDesde", SqlDbType.DateTime2).Value = fechaDisponibleDesde;
                    cmd.Parameters.Add("@Ahora", SqlDbType.DateTime2).Value = ahora;
                    cmd.Parameters.Add("@FechaFinEsperada", SqlDbType.DateTime2).Value = fechaFinEsperada;
                    cmd.Parameters.Add("@MinutosEspera", SqlDbType.Int).Value = minutosEspera;
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                    cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 1000).Value = string.IsNullOrWhiteSpace(observaciones) ? DBNull.Value : observaciones;

                    var valor = await cmd.ExecuteScalarAsync();
                    if (valor == null || valor == DBNull.Value) throw new InvalidOperationException("No fue posible crear la carga de secado.");
                    secadoCargaId = Convert.ToInt64(valor);
                }

                const string sqlSegmento = @"
INSERT dbo.Produccion_SecadoCargaSegmentos
(
    SecadoCargaID,TolvaID,NumeroSegmento,FechaInicio,FechaFin,MinutosSegmento,EsCambioTolva,ReiniciaTiempoRequerido,
    MotivoCambio,UsuarioInicioID,UsuarioFinID,Activo,FechaCreacion
)
VALUES
(
    @SecadoCargaID,@TolvaID,1,@Ahora,NULL,NULL,0,0,NULL,@UsuarioID,NULL,1,@Ahora
);";

                await using (var cmd = new SqlCommand(sqlSegmento, cn, tx))
                {
                    cmd.Parameters.Add("@SecadoCargaID", SqlDbType.BigInt).Value = secadoCargaId;
                    cmd.Parameters.Add("@TolvaID", SqlDbType.Int).Value = vm.TolvaID;
                    cmd.Parameters.Add("@Ahora", SqlDbType.DateTime2).Value = ahora;
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                    await cmd.ExecuteNonQueryAsync();
                }

                const string sqlActualizarMaterial = @"
UPDATE dbo.Produccion_SecadoMaterial
SET
    CantidadAsignadaKg=CantidadAsignadaKg+@CantidadKg,
    FechaPrimerInicioSecado=COALESCE(FechaPrimerInicioSecado,@Ahora),
    MinutosEsperaInicio=COALESCE(MinutosEsperaInicio,DATEDIFF(MINUTE,FechaRecepcionProduccion,@Ahora)),
    Estado=N'EN_PROCESO',
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=@Ahora
WHERE SecadoMaterialID=@SecadoMaterialID
  AND Activo=1
  AND Estado<>N'FINALIZADO'
  AND Estado<>N'CANCELADO'
  AND CantidadAsignadaKg+@CantidadKg<=CantidadRecibidaKg+0.0005;

IF @@ROWCOUNT<>1
    THROW 51301,'La cantidad disponible de material cambió mientras se iniciaba el secado.',1;";

                await using (var cmd = new SqlCommand(sqlActualizarMaterial, cn, tx))
                {
                    var pCantidad = cmd.Parameters.Add("@CantidadKg", SqlDbType.Decimal);
                    pCantidad.Precision = 18;
                    pCantidad.Scale = 4;
                    pCantidad.Value = vm.CantidadKg;

                    cmd.Parameters.Add("@Ahora", SqlDbType.DateTime2).Value = ahora;
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                    cmd.Parameters.Add("@SecadoMaterialID", SqlDbType.BigInt).Value = vm.SecadoMaterialID;
                    await cmd.ExecuteNonQueryAsync();
                }

                await AgregarHistorialSecadoAsync(vm.SecadoMaterialID, secadoCargaId, "INICIO_SECADO", estadoMaterial, ProduccionSecadoEstadoMaterial.EnProceso, null, vm.TolvaID, vm.CantidadKg, $"Carga {numeroCarga} iniciada en {tolvaCodigo}. Fin esperado: {fechaFinEsperada:dd/MM/yyyy HH:mm}. Espera previa: {minutosEspera} min.", usuarioId, ahora, cn, tx);

                if (programaProduccionId.HasValue && programaProduccionId.Value > 0)
                    await ActualizarPreparacionSecadoInicioAsync(programaProduccionId.Value, usuarioId, ahora, cn, tx);

                await tx.CommitAsync();
                TempData["Success"] = $"Secado iniciado. Carga {numeroCarga}: {vm.CantidadKg:0.####} KG en {tolvaCodigo}. Fin esperado {fechaFinEsperada:dd/MM/yyyy HH:mm}.";
                if (minutosEspera > 0) TempData["Success"] += $" El material esperó {minutosEspera} minuto(s) antes de iniciar esta carga.";
                return RedirectToAction(nameof(Secado));
            }
            catch (Exception ex)
            {
                try { await tx.RollbackAsync(); } catch { }
                TempData["Error"] = "No fue posible iniciar el secado: " + ex.Message;
                return RedirectToAction(nameof(Secado));
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CambiarTolvaSecado(ProduccionCambiarTolvaVm vm)
        {
            if (!UsuarioEnSesion()) return RedirectToAction("Login", "Login");
            if (vm.SecadoCargaID <= 0 || vm.TolvaNuevaID <= 0)
            {
                TempData["Error"] = "No se recibió correctamente la carga o la nueva tolva.";
                return RedirectToAction(nameof(Secado));
            }

            var motivo = string.IsNullOrWhiteSpace(vm.MotivoCambio) ? null : vm.MotivoCambio.Trim();
            if (string.IsNullOrWhiteSpace(motivo))
            {
                TempData["Error"] = "Debes indicar el motivo del cambio de tolva.";
                return RedirectToAction(nameof(Secado));
            }
            if (motivo.Length > 500)
            {
                TempData["Error"] = "El motivo del cambio de tolva no puede superar 500 caracteres.";
                return RedirectToAction(nameof(Secado));
            }

            var usuarioId = ObtenerUsuarioID();
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            var permisos = await ObtenerPermisosPreparacionUsuarioAsync(usuarioId, cn);
            if (!permisos.PuedeGestionarSecado) return StatusCode(StatusCodes.Status403Forbidden);
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);

            try
            {
                const string sqlCarga = @"
SELECT TOP(1)
    c.SecadoCargaID,
    c.SecadoMaterialID,
    c.TolvaIDActual,
    c.CantidadKg,
    c.Estado,
    sm.TipoProceso
FROM dbo.Produccion_SecadoCargas c WITH(UPDLOCK,HOLDLOCK)
INNER JOIN dbo.Produccion_SecadoMaterial sm WITH(UPDLOCK,HOLDLOCK)
    ON sm.SecadoMaterialID=c.SecadoMaterialID
WHERE c.SecadoCargaID=@SecadoCargaID
  AND c.Activo=1
  AND sm.Activo=1;";

                long secadoMaterialId;
                int tolvaAnteriorId;
                decimal cantidadKg;
                string estadoCarga;
                string tipoProceso;

                await using (var cmd = new SqlCommand(sqlCarga, cn, tx))
                {
                    cmd.Parameters.Add("@SecadoCargaID", SqlDbType.BigInt).Value = vm.SecadoCargaID;
                    await using var rd = await cmd.ExecuteReaderAsync();
                    if (!await rd.ReadAsync())
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] = "La carga de secado ya no existe.";
                        return RedirectToAction(nameof(Secado));
                    }

                    secadoMaterialId = Convert.ToInt64(rd["SecadoMaterialID"]);
                    tolvaAnteriorId = Convert.ToInt32(rd["TolvaIDActual"]);
                    cantidadKg = Convert.ToDecimal(rd["CantidadKg"]);
                    estadoCarga = rd["Estado"]?.ToString()?.Trim() ?? string.Empty;
                    tipoProceso = rd["TipoProceso"]?.ToString()?.Trim() ?? ProduccionSecadoTipoProceso.Secado;
                }

                if (!string.Equals(estadoCarga, ProduccionSecadoEstadoCarga.EnProceso, StringComparison.OrdinalIgnoreCase))
                {
                    await tx.RollbackAsync();
                    TempData["Warning"] = "Solo puedes cambiar de tolva una carga que se encuentre en proceso.";
                    return RedirectToAction(nameof(Secado));
                }

                if (tolvaAnteriorId == vm.TolvaNuevaID)
                {
                    await tx.RollbackAsync();
                    TempData["Warning"] = "La carga ya se encuentra en esa tolva.";
                    return RedirectToAction(nameof(Secado));
                }

                const string sqlTolva = @"
SELECT TOP(1)
    Codigo,
    Nombre,
    CapacidadKg,
    TipoProcesoPermitido,
    DisponibleOperativamente,
    Activo
FROM dbo.Produccion_SecadoTolvas WITH(UPDLOCK,HOLDLOCK)
WHERE TolvaID=@TolvaID;";

                string tolvaNuevaCodigo;
                decimal capacidadNueva;
                string procesoPermitido;
                bool disponible;
                bool activa;

                await using (var cmd = new SqlCommand(sqlTolva, cn, tx))
                {
                    cmd.Parameters.Add("@TolvaID", SqlDbType.Int).Value = vm.TolvaNuevaID;
                    await using var rd = await cmd.ExecuteReaderAsync();
                    if (!await rd.ReadAsync())
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] = "La nueva tolva ya no existe.";
                        return RedirectToAction(nameof(Secado));
                    }

                    tolvaNuevaCodigo = rd["Codigo"]?.ToString()?.Trim() ?? string.Empty;
                    capacidadNueva = Convert.ToDecimal(rd["CapacidadKg"]);
                    procesoPermitido = rd["TipoProcesoPermitido"]?.ToString()?.Trim() ?? "AMBOS";
                    disponible = rd["DisponibleOperativamente"] != DBNull.Value && Convert.ToBoolean(rd["DisponibleOperativamente"]);
                    activa = rd["Activo"] != DBNull.Value && Convert.ToBoolean(rd["Activo"]);
                }

                if (!activa || !disponible)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "La nueva tolva no está disponible operativamente.";
                    return RedirectToAction(nameof(Secado));
                }

                if (cantidadKg - capacidadNueva > ProduccionSecadoReglas.ToleranciaCantidad)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = $"No puedes mover {cantidadKg:0.####} KG a {tolvaNuevaCodigo}; su capacidad es {capacidadNueva:0.####} KG.";
                    return RedirectToAction(nameof(Secado));
                }

                if (!string.Equals(procesoPermitido, "AMBOS", StringComparison.OrdinalIgnoreCase) && !string.Equals(procesoPermitido, tipoProceso, StringComparison.OrdinalIgnoreCase))
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = $"La nueva tolva no está habilitada para el proceso {tipoProceso}.";
                    return RedirectToAction(nameof(Secado));
                }

                const string sqlOcupada = @"
SELECT COUNT(1)
FROM dbo.Produccion_SecadoCargaSegmentos WITH(UPDLOCK,HOLDLOCK)
WHERE TolvaID=@TolvaID
  AND Activo=1
  AND FechaFin IS NULL;";

                await using (var cmd = new SqlCommand(sqlOcupada, cn, tx))
                {
                    cmd.Parameters.Add("@TolvaID", SqlDbType.Int).Value = vm.TolvaNuevaID;
                    if (Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0)
                    {
                        await tx.RollbackAsync();
                        TempData["Warning"] = "La nueva tolva está ocupada por otra carga.";
                        return RedirectToAction(nameof(Secado));
                    }
                }

                var ahora = await ObtenerFechaServidorSecadoAsync(cn, tx);

                const string sqlCerrarSegmento = @"
UPDATE dbo.Produccion_SecadoCargaSegmentos
SET
    FechaFin=@Ahora,
    MinutosSegmento=CASE WHEN DATEDIFF(MINUTE,FechaInicio,@Ahora)<0 THEN 0 ELSE DATEDIFF(MINUTE,FechaInicio,@Ahora) END,
    UsuarioFinID=@UsuarioID
WHERE SecadoCargaID=@SecadoCargaID
  AND TolvaID=@TolvaAnteriorID
  AND Activo=1
  AND FechaFin IS NULL;

IF @@ROWCOUNT<>1
    THROW 51302,'No se encontró el segmento activo de la carga en la tolva actual.',1;";

                await using (var cmd = new SqlCommand(sqlCerrarSegmento, cn, tx))
                {
                    cmd.Parameters.Add("@Ahora", SqlDbType.DateTime2).Value = ahora;
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                    cmd.Parameters.Add("@SecadoCargaID", SqlDbType.BigInt).Value = vm.SecadoCargaID;
                    cmd.Parameters.Add("@TolvaAnteriorID", SqlDbType.Int).Value = tolvaAnteriorId;
                    await cmd.ExecuteNonQueryAsync();
                }

                const string sqlNumeroSegmento = @"
SELECT ISNULL(MAX(NumeroSegmento),0)+1
FROM dbo.Produccion_SecadoCargaSegmentos WITH(UPDLOCK,HOLDLOCK)
WHERE SecadoCargaID=@SecadoCargaID;";

                int numeroSegmento;
                await using (var cmd = new SqlCommand(sqlNumeroSegmento, cn, tx))
                {
                    cmd.Parameters.Add("@SecadoCargaID", SqlDbType.BigInt).Value = vm.SecadoCargaID;
                    numeroSegmento = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                }

                const string sqlNuevoSegmento = @"
INSERT dbo.Produccion_SecadoCargaSegmentos
(
    SecadoCargaID,TolvaID,NumeroSegmento,FechaInicio,FechaFin,MinutosSegmento,EsCambioTolva,ReiniciaTiempoRequerido,
    MotivoCambio,UsuarioInicioID,UsuarioFinID,Activo,FechaCreacion
)
VALUES
(
    @SecadoCargaID,@TolvaNuevaID,@NumeroSegmento,@Ahora,NULL,NULL,1,0,@Motivo,@UsuarioID,NULL,1,@Ahora
);";

                await using (var cmd = new SqlCommand(sqlNuevoSegmento, cn, tx))
                {
                    cmd.Parameters.Add("@SecadoCargaID", SqlDbType.BigInt).Value = vm.SecadoCargaID;
                    cmd.Parameters.Add("@TolvaNuevaID", SqlDbType.Int).Value = vm.TolvaNuevaID;
                    cmd.Parameters.Add("@NumeroSegmento", SqlDbType.Int).Value = numeroSegmento;
                    cmd.Parameters.Add("@Ahora", SqlDbType.DateTime2).Value = ahora;
                    cmd.Parameters.Add("@Motivo", SqlDbType.NVarChar, 500).Value = motivo;
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                    await cmd.ExecuteNonQueryAsync();
                }

                const string sqlActualizarCarga = @"
UPDATE dbo.Produccion_SecadoCargas
SET
    TolvaIDActual=@TolvaNuevaID,
    CapacidadTolvaKgSnapshot=@CapacidadNueva,
    FechaAsignacionTolva=@Ahora,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=@Ahora
WHERE SecadoCargaID=@SecadoCargaID
  AND Activo=1
  AND Estado=N'EN_PROCESO';

IF @@ROWCOUNT<>1
    THROW 51303,'La carga cambió de estado mientras se realizaba el cambio de tolva.',1;";

                await using (var cmd = new SqlCommand(sqlActualizarCarga, cn, tx))
                {
                    cmd.Parameters.Add("@TolvaNuevaID", SqlDbType.Int).Value = vm.TolvaNuevaID;

                    var pCapacidad = cmd.Parameters.Add("@CapacidadNueva", SqlDbType.Decimal);
                    pCapacidad.Precision = 18;
                    pCapacidad.Scale = 4;
                    pCapacidad.Value = capacidadNueva;

                    cmd.Parameters.Add("@Ahora", SqlDbType.DateTime2).Value = ahora;
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                    cmd.Parameters.Add("@SecadoCargaID", SqlDbType.BigInt).Value = vm.SecadoCargaID;
                    await cmd.ExecuteNonQueryAsync();
                }

                await AgregarHistorialSecadoAsync(secadoMaterialId, vm.SecadoCargaID, "CAMBIO_TOLVA", ProduccionSecadoEstadoMaterial.EnProceso, ProduccionSecadoEstadoMaterial.EnProceso, tolvaAnteriorId, vm.TolvaNuevaID, cantidadKg, $"Cambio de tolva sin reiniciar el tiempo requerido. Motivo: {motivo}", usuarioId, ahora, cn, tx);

                await tx.CommitAsync();
                TempData["Success"] = $"La carga fue movida a {tolvaNuevaCodigo}. El cronómetro de secado continúa sin reiniciarse.";
                return RedirectToAction(nameof(Secado));
            }
            catch (Exception ex)
            {
                try { await tx.RollbackAsync(); } catch { }
                TempData["Error"] = "No fue posible cambiar la tolva: " + ex.Message;
                return RedirectToAction(nameof(Secado));
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> FinalizarSecado(ProduccionFinalizarSecadoVm vm)
        {
            if (!UsuarioEnSesion()) return RedirectToAction("Login", "Login");
            if (vm.SecadoCargaID <= 0)
            {
                TempData["Error"] = "No se recibió correctamente la carga de secado.";
                return RedirectToAction(nameof(Secado));
            }

            var observaciones = string.IsNullOrWhiteSpace(vm.Observaciones) ? null : vm.Observaciones.Trim();
            if (observaciones?.Length > 1000)
            {
                TempData["Error"] = "Las observaciones no pueden superar 1000 caracteres.";
                return RedirectToAction(nameof(Secado));
            }

            var usuarioId = ObtenerUsuarioID();
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            var permisos = await ObtenerPermisosPreparacionUsuarioAsync(usuarioId, cn);
            if (!permisos.PuedeGestionarSecado) return StatusCode(StatusCodes.Status403Forbidden);
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);

            try
            {
                const string sqlCarga = @"
SELECT TOP(1)
    c.SecadoCargaID,
    c.SecadoMaterialID,
    c.TolvaIDActual,
    c.CantidadKg,
    c.Estado,
    c.FechaInicioReal,
    c.FechaFinEsperada,
    sm.ProgramaProduccionID,
    sm.CantidadRecibidaKg,
    sm.CantidadFinalizadaKg,
    sm.Estado AS EstadoMaterial
FROM dbo.Produccion_SecadoCargas c WITH(UPDLOCK,HOLDLOCK)
INNER JOIN dbo.Produccion_SecadoMaterial sm WITH(UPDLOCK,HOLDLOCK)
    ON sm.SecadoMaterialID=c.SecadoMaterialID
WHERE c.SecadoCargaID=@SecadoCargaID
  AND c.Activo=1
  AND sm.Activo=1;";

                long secadoMaterialId;
                int tolvaId;
                decimal cantidadKg;
                string estadoCarga;
                DateTime fechaInicio;
                DateTime fechaFinEsperada;
                int? programaProduccionId;
                decimal cantidadRecibida;
                decimal cantidadFinalizadaAnterior;
                string estadoMaterialAnterior;

                await using (var cmd = new SqlCommand(sqlCarga, cn, tx))
                {
                    cmd.Parameters.Add("@SecadoCargaID", SqlDbType.BigInt).Value = vm.SecadoCargaID;
                    await using var rd = await cmd.ExecuteReaderAsync();
                    if (!await rd.ReadAsync())
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] = "La carga de secado ya no existe.";
                        return RedirectToAction(nameof(Secado));
                    }

                    secadoMaterialId = Convert.ToInt64(rd["SecadoMaterialID"]);
                    tolvaId = Convert.ToInt32(rd["TolvaIDActual"]);
                    cantidadKg = Convert.ToDecimal(rd["CantidadKg"]);
                    estadoCarga = rd["Estado"]?.ToString()?.Trim() ?? string.Empty;

                    if (rd["FechaInicioReal"] == DBNull.Value || rd["FechaFinEsperada"] == DBNull.Value)
                        throw new InvalidOperationException("La carga no tiene correctamente registradas sus horas de inicio y fin esperado.");

                    fechaInicio = Convert.ToDateTime(rd["FechaInicioReal"]);
                    fechaFinEsperada = Convert.ToDateTime(rd["FechaFinEsperada"]);
                    programaProduccionId = rd["ProgramaProduccionID"] == DBNull.Value ? null : Convert.ToInt32(rd["ProgramaProduccionID"]);
                    cantidadRecibida = Convert.ToDecimal(rd["CantidadRecibidaKg"]);
                    cantidadFinalizadaAnterior = Convert.ToDecimal(rd["CantidadFinalizadaKg"]);
                    estadoMaterialAnterior = rd["EstadoMaterial"]?.ToString()?.Trim() ?? ProduccionSecadoEstadoMaterial.EnProceso;
                }

                if (!string.Equals(estadoCarga, ProduccionSecadoEstadoCarga.EnProceso, StringComparison.OrdinalIgnoreCase))
                {
                    await tx.RollbackAsync();
                    TempData["Warning"] = "Esta carga ya no se encuentra en proceso.";
                    return RedirectToAction(nameof(Secado));
                }

                var ahora = await ObtenerFechaServidorSecadoAsync(cn, tx);

                if (ahora < fechaFinEsperada)
                {
                    var minutosFaltantes = Math.Max(1, (int)Math.Ceiling((fechaFinEsperada - ahora).TotalMinutes));
                    await tx.RollbackAsync();
                    TempData["Warning"] = $"La carga todavía no cumple el tiempo requerido. Faltan aproximadamente {minutosFaltantes} minuto(s).";
                    return RedirectToAction(nameof(Secado));
                }

                var duracionRealMinutos = Math.Max(0, (int)Math.Floor((ahora - fechaInicio).TotalMinutes));
                var minutosExceso = Math.Max(0, (int)Math.Floor((ahora - fechaFinEsperada).TotalMinutes));

                const string sqlCerrarSegmento = @"
UPDATE dbo.Produccion_SecadoCargaSegmentos
SET
    FechaFin=@Ahora,
    MinutosSegmento=CASE WHEN DATEDIFF(MINUTE,FechaInicio,@Ahora)<0 THEN 0 ELSE DATEDIFF(MINUTE,FechaInicio,@Ahora) END,
    UsuarioFinID=@UsuarioID
WHERE SecadoCargaID=@SecadoCargaID
  AND TolvaID=@TolvaID
  AND Activo=1
  AND FechaFin IS NULL;

IF @@ROWCOUNT<>1
    THROW 51304,'No se encontró el segmento activo de la carga que se desea finalizar.',1;";

                await using (var cmd = new SqlCommand(sqlCerrarSegmento, cn, tx))
                {
                    cmd.Parameters.Add("@Ahora", SqlDbType.DateTime2).Value = ahora;
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                    cmd.Parameters.Add("@SecadoCargaID", SqlDbType.BigInt).Value = vm.SecadoCargaID;
                    cmd.Parameters.Add("@TolvaID", SqlDbType.Int).Value = tolvaId;
                    await cmd.ExecuteNonQueryAsync();
                }

                const string sqlFinalizarCarga = @"
UPDATE dbo.Produccion_SecadoCargas
SET
    Estado=N'FINALIZADA',
    FechaFinReal=@Ahora,
    DuracionRealMinutos=@DuracionRealMinutos,
    MinutosExcesoSecado=@MinutosExceso,
    ExcedioTiempo=CASE WHEN @MinutosExceso>0 THEN 1 ELSE 0 END,
    FinalizoAntesTiempo=0,
    MotivoFinalizacionAnticipada=NULL,
    UsuarioFinID=@UsuarioID,
    Observaciones=CASE
        WHEN @Observaciones IS NULL THEN Observaciones
        WHEN Observaciones IS NULL OR LTRIM(RTRIM(Observaciones))=N'' THEN @Observaciones
        ELSE LEFT(Observaciones+CHAR(13)+CHAR(10)+@Observaciones,1000)
    END,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=@Ahora
WHERE SecadoCargaID=@SecadoCargaID
  AND Activo=1
  AND Estado=N'EN_PROCESO';

IF @@ROWCOUNT<>1
    THROW 51305,'La carga cambió de estado mientras se finalizaba.',1;";

                await using (var cmd = new SqlCommand(sqlFinalizarCarga, cn, tx))
                {
                    cmd.Parameters.Add("@Ahora", SqlDbType.DateTime2).Value = ahora;
                    cmd.Parameters.Add("@DuracionRealMinutos", SqlDbType.Int).Value = duracionRealMinutos;
                    cmd.Parameters.Add("@MinutosExceso", SqlDbType.Int).Value = minutosExceso;
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                    cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 1000).Value = string.IsNullOrWhiteSpace(observaciones) ? DBNull.Value : observaciones;
                    cmd.Parameters.Add("@SecadoCargaID", SqlDbType.BigInt).Value = vm.SecadoCargaID;
                    await cmd.ExecuteNonQueryAsync();
                }

                var nuevaCantidadFinalizada = Math.Min(cantidadRecibida, cantidadFinalizadaAnterior + cantidadKg);
                var materialCompleto = nuevaCantidadFinalizada + ProduccionSecadoReglas.ToleranciaCantidad >= cantidadRecibida;
                var nuevoEstadoMaterial = materialCompleto ? ProduccionSecadoEstadoMaterial.Finalizado : ProduccionSecadoEstadoMaterial.Parcial;

                const string sqlActualizarMaterial = @"
UPDATE dbo.Produccion_SecadoMaterial
SET
    CantidadFinalizadaKg=CASE WHEN CantidadFinalizadaKg+@CantidadKg>CantidadRecibidaKg THEN CantidadRecibidaKg ELSE CantidadFinalizadaKg+@CantidadKg END,
    FechaUltimoFinSecado=@Ahora,
    Estado=@Estado,
    MinutosRetrasoFinal=CASE
        WHEN @Estado=N'FINALIZADO' AND FechaObjetivoFinSecado IS NOT NULL AND @Ahora>FechaObjetivoFinSecado
            THEN DATEDIFF(MINUTE,FechaObjetivoFinSecado,@Ahora)
        WHEN @Estado=N'FINALIZADO' THEN 0
        ELSE NULL
    END,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=@Ahora
WHERE SecadoMaterialID=@SecadoMaterialID
  AND Activo=1
  AND Estado<>N'CANCELADO';

IF @@ROWCOUNT<>1
    THROW 51306,'No fue posible actualizar el avance total del material secado.',1;";

                await using (var cmd = new SqlCommand(sqlActualizarMaterial, cn, tx))
                {
                    var pCantidad = cmd.Parameters.Add("@CantidadKg", SqlDbType.Decimal);
                    pCantidad.Precision = 18;
                    pCantidad.Scale = 4;
                    pCantidad.Value = cantidadKg;

                    cmd.Parameters.Add("@Ahora", SqlDbType.DateTime2).Value = ahora;
                    cmd.Parameters.Add("@Estado", SqlDbType.NVarChar, 30).Value = nuevoEstadoMaterial;
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                    cmd.Parameters.Add("@SecadoMaterialID", SqlDbType.BigInt).Value = secadoMaterialId;
                    await cmd.ExecuteNonQueryAsync();
                }

                await AgregarHistorialSecadoAsync(secadoMaterialId, vm.SecadoCargaID, "FIN_SECADO", estadoMaterialAnterior, nuevoEstadoMaterial, tolvaId, tolvaId, cantidadKg, minutosExceso > 0 ? $"Carga finalizada con {minutosExceso} minuto(s) adicionales al tiempo requerido." : "Carga finalizada al cumplir el tiempo requerido.", usuarioId, ahora, cn, tx);

                if (programaProduccionId.HasValue && programaProduccionId.Value > 0)
                    await ActualizarPreparacionSecadoFinalAsync(programaProduccionId.Value, usuarioId, ahora, cn, tx);

                await tx.CommitAsync();

                if (materialCompleto)
                {
                    TempData["Success"] = $"Secado finalizado. Se completaron {nuevaCantidadFinalizada:0.####} KG del material.";
                    if (minutosExceso > 0) TempData["Warning"] = $"La carga terminó {minutosExceso} minuto(s) después del tiempo requerido.";
                }
                else
                {
                    var restante = Math.Max(0m, cantidadRecibida - nuevaCantidadFinalizada);
                    TempData["Success"] = $"Carga finalizada correctamente. Quedan {restante:0.####} KG por completar en una nueva carga.";
                    if (minutosExceso > 0) TempData["Warning"] = $"La carga terminó {minutosExceso} minuto(s) después del tiempo requerido.";
                }

                return RedirectToAction(nameof(Secado));
            }
            catch (Exception ex)
            {
                try { await tx.RollbackAsync(); } catch { }
                TempData["Error"] = "No fue posible finalizar el secado: " + ex.Message;
                return RedirectToAction(nameof(Secado));
            }
        }

        [HttpGet]
        public async Task<IActionResult> EstadoSecado(long id)
        {
            if (!UsuarioEnSesion()) return Unauthorized();
            if (id <= 0) return BadRequest();

            var usuarioId = ObtenerUsuarioID();
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            var permisos = await ObtenerPermisosPreparacionUsuarioAsync(usuarioId, cn);
            if (!permisos.PuedeVerModulo) return StatusCode(StatusCodes.Status403Forbidden);

            var configuracion = await CargarConfiguracionSecadoAsync(cn);

            const string sql = @"
SELECT TOP(1)
    c.SecadoCargaID,
    c.SecadoMaterialID,
    c.Estado,
    c.FechaInicioReal,
    c.FechaFinEsperada,
    c.FechaFinReal
FROM dbo.Produccion_SecadoCargas c
INNER JOIN dbo.Produccion_SecadoMaterial sm ON sm.SecadoMaterialID=c.SecadoMaterialID
WHERE c.SecadoCargaID=@SecadoCargaID
  AND c.Activo=1
  AND sm.Activo=1;";

            string estado;
            DateTime? inicio;
            DateTime? finEsperado;
            DateTime? finReal;

            await using (var cmd = new SqlCommand(sql, cn))
            {
                cmd.Parameters.Add("@SecadoCargaID", SqlDbType.BigInt).Value = id;
                await using var rd = await cmd.ExecuteReaderAsync();
                if (!await rd.ReadAsync()) return NotFound();

                estado = rd["Estado"]?.ToString()?.Trim() ?? string.Empty;
                inicio = rd["FechaInicioReal"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaInicioReal"]);
                finEsperado = rd["FechaFinEsperada"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaFinEsperada"]);
                finReal = rd["FechaFinReal"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaFinReal"]);
            }

            var ahora = await ObtenerFechaServidorSecadoAsync(cn);
            var enProceso = string.Equals(estado, ProduccionSecadoEstadoCarga.EnProceso, StringComparison.OrdinalIgnoreCase);
            var minutosRestantes = enProceso && finEsperado.HasValue && ahora < finEsperado.Value ? Math.Max(0, (int)Math.Ceiling((finEsperado.Value - ahora).TotalMinutes)) : 0;
            var minutosRetraso = enProceso && finEsperado.HasValue && ahora > finEsperado.Value ? Math.Max(0, (int)Math.Floor((ahora - finEsperado.Value).TotalMinutes)) : 0;
            var tiempoCumplido = enProceso && finEsperado.HasValue && ahora >= finEsperado.Value;
            var proximoFin = enProceso && finEsperado.HasValue && ahora < finEsperado.Value && minutosRestantes <= configuracion.MinutosAvisoProximoFin;
            var retrasada = enProceso && finEsperado.HasValue && ahora > finEsperado.Value.AddMinutes(configuracion.MinutosToleranciaFin);

            return Json(new
            {
                id,
                estado,
                fechaInicioReal = inicio,
                fechaFinEsperada = finEsperado,
                fechaFinReal = finReal,
                ahoraServidor = ahora,
                minutosRestantes,
                minutosRetraso,
                tiempoCumplido,
                proximoFin,
                retrasada
            });
        }

        [HttpGet]
        public async Task<IActionResult> AlertasSecado()
        {
            if (!UsuarioEnSesion()) return Unauthorized();

            var usuarioId = ObtenerUsuarioID();
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            var permisos = await ObtenerPermisosPreparacionUsuarioAsync(usuarioId, cn);
            if (!permisos.PuedeVerModulo) return StatusCode(StatusCodes.Status403Forbidden);

            var configuracion = await CargarConfiguracionSecadoAsync(cn);
            var ahora = await ObtenerFechaServidorSecadoAsync(cn);
            var alertas = new List<object>();

            const string sqlEspera = @"
SELECT
    sm.SecadoMaterialID,
    sm.NumeroOFSnapshot,
    sm.MaterialCodigoSnapshot,
    CASE
        WHEN sm.FechaPrimerInicioSecado IS NULL THEN sm.FechaRecepcionProduccion
        ELSE ISNULL(ultima.FechaUltimaFinalizacion,sm.FechaRecepcionProduccion)
    END AS FechaDesde
FROM dbo.Produccion_SecadoMaterial sm
OUTER APPLY
(
    SELECT MAX(c.FechaFinReal) AS FechaUltimaFinalizacion
    FROM dbo.Produccion_SecadoCargas c
    WHERE c.SecadoMaterialID=sm.SecadoMaterialID
      AND c.Activo=1
      AND c.Estado=N'FINALIZADA'
) ultima
WHERE sm.Activo=1
  AND sm.Estado IN(N'PENDIENTE',N'PARCIAL')
  AND sm.CantidadFinalizadaKg<sm.CantidadRecibidaKg-0.0005
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.Produccion_SecadoCargas activa
      WHERE activa.SecadoMaterialID=sm.SecadoMaterialID
        AND activa.Activo=1
        AND activa.Estado=N'EN_PROCESO'
  );";

            await using (var cmd = new SqlCommand(sqlEspera, cn))
            await using (var rd = await cmd.ExecuteReaderAsync())
            {
                while (await rd.ReadAsync())
                {
                    var fechaDesde = Convert.ToDateTime(rd["FechaDesde"]);
                    var minutos = Math.Max(0, (int)Math.Floor((ahora - fechaDesde).TotalMinutes));
                    if (minutos < configuracion.MinutosAlertaEsperaInicio) continue;

                    alertas.Add(new
                    {
                        tipo = "ESPERA_INICIO",
                        nivel = "ADVERTENCIA",
                        secadoMaterialId = Convert.ToInt64(rd["SecadoMaterialID"]),
                        secadoCargaId = (long?)null,
                        numeroOF = rd["NumeroOFSnapshot"]?.ToString()?.Trim(),
                        material = rd["MaterialCodigoSnapshot"]?.ToString()?.Trim(),
                        minutos,
                        mensaje = $"El material lleva {minutos} minuto(s) disponible y todavía no inicia su siguiente carga de secado."
                    });
                }
            }

            const string sqlCargas = @"
SELECT
    c.SecadoCargaID,
    c.SecadoMaterialID,
    c.FechaFinEsperada,
    sm.NumeroOFSnapshot,
    sm.MaterialCodigoSnapshot
FROM dbo.Produccion_SecadoCargas c
INNER JOIN dbo.Produccion_SecadoMaterial sm ON sm.SecadoMaterialID=c.SecadoMaterialID
WHERE c.Activo=1
  AND sm.Activo=1
  AND c.Estado=N'EN_PROCESO'
  AND c.FechaFinEsperada IS NOT NULL;";

            await using (var cmd = new SqlCommand(sqlCargas, cn))
            await using (var rd = await cmd.ExecuteReaderAsync())
            {
                while (await rd.ReadAsync())
                {
                    var fin = Convert.ToDateTime(rd["FechaFinEsperada"]);
                    var minutosRestantes = ahora < fin ? Math.Max(0, (int)Math.Ceiling((fin - ahora).TotalMinutes)) : 0;
                    var minutosRetraso = ahora > fin ? Math.Max(0, (int)Math.Floor((ahora - fin).TotalMinutes)) : 0;
                    string? tipo = null;
                    string? nivel = null;
                    string? mensaje = null;

                    if (ahora > fin.AddMinutes(configuracion.MinutosToleranciaFin))
                    {
                        tipo = "SECADO_RETRASADO";
                        nivel = "CRITICO";
                        mensaje = $"La carga lleva {minutosRetraso} minuto(s) después de su fin esperado.";
                    }
                    else if (ahora >= fin)
                    {
                        tipo = "TIEMPO_CUMPLIDO";
                        nivel = "ADVERTENCIA";
                        mensaje = "La carga ya cumplió el tiempo requerido y puede finalizarse.";
                    }
                    else if (minutosRestantes <= configuracion.MinutosAvisoProximoFin)
                    {
                        tipo = "PROXIMO_FIN";
                        nivel = "INFORMATIVA";
                        mensaje = $"Faltan aproximadamente {minutosRestantes} minuto(s) para completar el secado.";
                    }

                    if (tipo != null)
                    {
                        alertas.Add(new
                        {
                            tipo,
                            nivel,
                            secadoMaterialId = Convert.ToInt64(rd["SecadoMaterialID"]),
                            secadoCargaId = Convert.ToInt64(rd["SecadoCargaID"]),
                            numeroOF = rd["NumeroOFSnapshot"]?.ToString()?.Trim(),
                            material = rd["MaterialCodigoSnapshot"]?.ToString()?.Trim(),
                            minutos = tipo == "SECADO_RETRASADO" ? minutosRetraso : minutosRestantes,
                            mensaje
                        });
                    }
                }
            }

            return Json(new { ahoraServidor = ahora, total = alertas.Count, alertas });
        }

        private async Task<bool> RegistrarMaterialPendienteSecadoDesdeRecepcionAsync(long recepcionMaterialId, int usuarioId, SqlConnection cn, SqlTransaction tx)
        {
            if (recepcionMaterialId <= 0)
                return false;

            const string sqlProcesada = @"
SELECT CASE
    WHEN EXISTS
    (
        SELECT 1
        FROM dbo.Produccion_SecadoMaterial sm WITH(UPDLOCK,HOLDLOCK)
        WHERE sm.RecepcionMaterialID=@RecepcionMaterialID
    )
    OR EXISTS
    (
        SELECT 1
        FROM dbo.Produccion_RecepcionMaterialesHistorial h WITH(UPDLOCK,HOLDLOCK)
        WHERE h.RecepcionMaterialID=@RecepcionMaterialID
          AND h.Evento IN(N'SECADO_LOTE_CREADO',N'SECADO_ACUMULADO')
    )
    THEN 1
    ELSE 0
END;";

            await using (var cmd = new SqlCommand(sqlProcesada, cn, tx))
            {
                cmd.Parameters.Add("@RecepcionMaterialID", SqlDbType.BigInt).Value = recepcionMaterialId;
                if (Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0)
                    return false;
            }

            const string sqlDatos = @"
SELECT TOP(1)
    r.RecepcionMaterialID,
    r.SolicitudProduccionID,
    r.EjecucionProduccionID,
    r.MaterialSolicitadoID,
    r.MaterialEntregadoID,
    r.NumeroOFSnapshot,
    r.CodigoEntregadoSnapshot,
    r.DescripcionEntregadaSnapshot,
    r.TipoMP,
    r.Lote,
    r.CantidadRecibidaProduccion,
    r.FechaRecepcion,
    candidato.ProgramaProduccionID,
    candidato.SolicitudProduccionDetalleID,
    candidato.MaquinaID,
    candidato.FechaInicioProgramada,
    candidato.Arranque,
    candidato.TipoSecado,
    candidato.HorasSecado
FROM dbo.Produccion_RecepcionMateriales r
LEFT JOIN dbo.ERP_Materiales materialSolicitado
    ON materialSolicitado.MaterialID=r.MaterialSolicitadoID
OUTER APPLY
(
    SELECT TOP(1)
        pp.ProgramaProduccionID,
        d.SolicitudProduccionDetalleID,
        pp.MaquinaID,
        pp.FechaInicioProgramada,
        pp.Arranque,
        d.TipoSecado,
        d.HorasSecado
    FROM dbo.Planeacion_ProgramaProduccion pp
    INNER JOIN dbo.SolicitudesProduccionDetalle d
        ON d.SolicitudProduccionDetalleID=pp.SolicitudProduccionDetalleID
       AND d.Activo=1
    WHERE pp.Activo=1
      AND pp.SolicitudProduccionID=r.SolicitudProduccionID
      AND
      (
          (r.ProgramaProduccionID IS NOT NULL AND pp.ProgramaProduccionID=r.ProgramaProduccionID)
          OR
          (r.ProgramaProduccionID IS NULL AND r.SolicitudProduccionDetalleID IS NOT NULL AND d.SolicitudProduccionDetalleID=r.SolicitudProduccionDetalleID)
          OR
          (
              r.ProgramaProduccionID IS NULL
              AND r.SolicitudProduccionDetalleID IS NULL
              AND
              (
                  d.MaterialID=r.MaterialSolicitadoID
                  OR
                  (
                      d.MaterialID IS NULL
                      AND UPPER(LTRIM(RTRIM(ISNULL(d.MaterialCodigo,N''))))=UPPER(LTRIM(RTRIM(ISNULL(materialSolicitado.Codigo,N''))))
                  )
              )
          )
      )
    ORDER BY
        CASE WHEN r.ProgramaProduccionID IS NOT NULL AND pp.ProgramaProduccionID=r.ProgramaProduccionID THEN 0 ELSE 1 END,
        CASE WHEN r.SolicitudProduccionDetalleID IS NOT NULL AND d.SolicitudProduccionDetalleID=r.SolicitudProduccionDetalleID THEN 0 ELSE 1 END,
        pp.FechaInicioProgramada,
        pp.ProgramaProduccionID
) candidato
WHERE r.RecepcionMaterialID=@RecepcionMaterialID
  AND r.Activo=1
  AND r.TipoOrigen=N'MP'
  AND r.EstadoRecepcion IN(N'RECIBIDO_COMPLETO',N'RECIBIDO_PARCIAL')
  AND ISNULL(r.CantidadRecibidaProduccion,0)>0;";

            int solicitudProduccionId;
            int? solicitudProduccionDetalleId;
            int? programaProduccionId;
            int? ejecucionProduccionId;
            int? maquinaProgramadaId;
            int materialId;
            string numeroOF;
            string materialCodigo;
            string? materialDescripcion;
            string? tipoMP;
            string? lote;
            decimal cantidadRecibidaKg;
            DateTime fechaRecepcionProduccion;
            DateTime? fechaInicioProgramada;
            TimeSpan? arranque;
            string? tipoSecadoOrigen;
            decimal horasSecado;

            await using (var cmd = new SqlCommand(sqlDatos, cn, tx))
            {
                cmd.Parameters.Add("@RecepcionMaterialID", SqlDbType.BigInt).Value = recepcionMaterialId;
                await using var rd = await cmd.ExecuteReaderAsync();

                if (!await rd.ReadAsync())
                    return false;

                if (rd["HorasSecado"] == DBNull.Value)
                    return false;

                horasSecado = Convert.ToDecimal(rd["HorasSecado"]);

                if (horasSecado <= 0)
                    return false;

                if (rd["MaterialEntregadoID"] != DBNull.Value)
                    materialId = Convert.ToInt32(rd["MaterialEntregadoID"]);
                else if (rd["MaterialSolicitadoID"] != DBNull.Value)
                    materialId = Convert.ToInt32(rd["MaterialSolicitadoID"]);
                else
                    throw new InvalidOperationException("La recepción de materia prima no tiene identificado el material entregado ni el solicitado.");

                solicitudProduccionId = Convert.ToInt32(rd["SolicitudProduccionID"]);
                solicitudProduccionDetalleId = rd["SolicitudProduccionDetalleID"] == DBNull.Value ? null : Convert.ToInt32(rd["SolicitudProduccionDetalleID"]);
                programaProduccionId = rd["ProgramaProduccionID"] == DBNull.Value ? null : Convert.ToInt32(rd["ProgramaProduccionID"]);
                ejecucionProduccionId = rd["EjecucionProduccionID"] == DBNull.Value ? null : Convert.ToInt32(rd["EjecucionProduccionID"]);
                maquinaProgramadaId = rd["MaquinaID"] == DBNull.Value ? null : Convert.ToInt32(rd["MaquinaID"]);
                numeroOF = rd["NumeroOFSnapshot"]?.ToString()?.Trim() ?? string.Empty;
                materialCodigo = rd["CodigoEntregadoSnapshot"]?.ToString()?.Trim() ?? string.Empty;
                materialDescripcion = rd["DescripcionEntregadaSnapshot"] == DBNull.Value ? null : rd["DescripcionEntregadaSnapshot"]?.ToString()?.Trim();
                tipoMP = rd["TipoMP"] == DBNull.Value ? null : rd["TipoMP"]?.ToString()?.Trim();
                lote = rd["Lote"] == DBNull.Value ? null : rd["Lote"]?.ToString()?.Trim();
                cantidadRecibidaKg = Convert.ToDecimal(rd["CantidadRecibidaProduccion"]);

                if (rd["FechaRecepcion"] == DBNull.Value)
                    throw new InvalidOperationException("La recepción de materia prima no tiene fecha de confirmación en Producción.");

                fechaRecepcionProduccion = Convert.ToDateTime(rd["FechaRecepcion"]);
                fechaInicioProgramada = rd["FechaInicioProgramada"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaInicioProgramada"]);
                arranque = rd["Arranque"] == DBNull.Value ? null : (TimeSpan)rd["Arranque"];
                tipoSecadoOrigen = rd["TipoSecado"] == DBNull.Value ? null : rd["TipoSecado"]?.ToString()?.Trim();
            }

            if (cantidadRecibidaKg <= ProduccionSecadoReglas.ToleranciaCantidad)
                return false;

            if (!programaProduccionId.HasValue || programaProduccionId.Value <= 0)
                throw new InvalidOperationException("No fue posible identificar el programa de Producción relacionado con la materia prima recibida.");

            if (!solicitudProduccionDetalleId.HasValue || solicitudProduccionDetalleId.Value <= 0)
                throw new InvalidOperationException("No fue posible identificar el detalle de la OF relacionado con la materia prima recibida.");

            var minutosSecado = Convert.ToInt32(Math.Ceiling(horasSecado * 60m));

            if (minutosSecado <= 0)
                return false;

            var tipoSecadoNormalizado = (tipoSecadoOrigen ?? string.Empty).Trim().ToUpperInvariant();
            var tipoProceso = tipoSecadoNormalizado.Contains("DESHUM") || tipoSecadoNormalizado.Contains("DESUM")
                ? ProduccionSecadoTipoProceso.Deshumidificado
                : ProduccionSecadoTipoProceso.Secado;

            var configuracion = await CargarConfiguracionSecadoAsync(cn, tx);
            DateTime? fechaArranqueProduccion = null;
            DateTime? fechaInicioSecadoObjetivo = null;
            DateTime? fechaLimiteEntregaMaterial = null;
            DateTime? fechaObjetivoFinSecado = null;

            if (fechaInicioProgramada.HasValue)
            {
                fechaArranqueProduccion = ConstruirFechaPreparacion(fechaInicioProgramada.Value, arranque);
                fechaInicioSecadoObjetivo = fechaArranqueProduccion.Value.AddMinutes(-minutosSecado);
                fechaLimiteEntregaMaterial = fechaInicioSecadoObjetivo.Value.AddMinutes(-configuracion.MargenEntregaAntesSecadoMinutos);
                fechaObjetivoFinSecado = fechaArranqueProduccion.Value;
            }

            var ahora = await ObtenerFechaServidorSecadoAsync(cn, tx);

            const string sqlAcumulable = @"
SELECT TOP(1)
    sm.SecadoMaterialID
FROM dbo.Produccion_SecadoMaterial sm WITH(UPDLOCK,HOLDLOCK)
WHERE sm.Activo=1
  AND sm.ProgramaProduccionID=@ProgramaProduccionID
  AND sm.MaterialID=@MaterialID
  AND ISNULL(LTRIM(RTRIM(sm.TipoMP)),N'')=ISNULL(LTRIM(RTRIM(@TipoMP)),N'')
  AND sm.Estado=N'PENDIENTE'
  AND ISNULL(sm.CantidadAsignadaKg,0)<=0.0005
  AND ISNULL(sm.CantidadFinalizadaKg,0)<=0.0005
  AND sm.FechaPrimerInicioSecado IS NULL
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.Produccion_SecadoCargas c WITH(UPDLOCK,HOLDLOCK)
      WHERE c.SecadoMaterialID=sm.SecadoMaterialID
  )
ORDER BY sm.SecadoMaterialID DESC;";

            long? secadoMaterialAcumulableId = null;

            await using (var cmd = new SqlCommand(sqlAcumulable, cn, tx))
            {
                cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaProduccionId.Value;
                cmd.Parameters.Add("@MaterialID", SqlDbType.Int).Value = materialId;
                cmd.Parameters.Add("@TipoMP", SqlDbType.NChar, 1).Value = string.IsNullOrWhiteSpace(tipoMP) ? DBNull.Value : tipoMP;
                var valor = await cmd.ExecuteScalarAsync();
                if (valor != null && valor != DBNull.Value)
                    secadoMaterialAcumulableId = Convert.ToInt64(valor);
            }

            if (secadoMaterialAcumulableId.HasValue)
            {
                const string sqlAcumular = @"
UPDATE dbo.Produccion_SecadoMaterial
SET
    CantidadRecibidaKg=CantidadRecibidaKg+@CantidadRecibidaKg,
    EjecucionProduccionID=COALESCE(@EjecucionProduccionID,EjecucionProduccionID),
    MaquinaProgramadaID=COALESCE(@MaquinaProgramadaID,MaquinaProgramadaID),
    MaterialCodigoSnapshot=CASE WHEN @MaterialCodigo IS NULL THEN MaterialCodigoSnapshot ELSE @MaterialCodigo END,
    MaterialDescripcionSnapshot=COALESCE(@MaterialDescripcion,MaterialDescripcionSnapshot),
    Lote=CASE
        WHEN @Lote IS NULL THEN Lote
        WHEN Lote IS NULL OR LTRIM(RTRIM(Lote))=N'' THEN @Lote
        WHEN UPPER(LTRIM(RTRIM(Lote)))=UPPER(LTRIM(RTRIM(@Lote))) THEN Lote
        WHEN UPPER(LTRIM(RTRIM(Lote)))=N'VARIOS' THEN Lote
        ELSE N'VARIOS'
    END,
    TipoSecadoOrigen=@TipoSecadoOrigen,
    TipoProceso=@TipoProceso,
    HorasSecadoRequeridas=@HorasSecadoRequeridas,
    MinutosSecadoRequeridos=@MinutosSecadoRequeridos,
    MargenEntregaAntesSecadoMinutos=@MargenEntregaAntesSecadoMinutos,
    FechaRecepcionProduccion=CASE WHEN FechaRecepcionProduccion>@FechaRecepcionProduccion THEN @FechaRecepcionProduccion ELSE FechaRecepcionProduccion END,
    FechaArranqueProduccion=@FechaArranqueProduccion,
    FechaInicioSecadoObjetivo=@FechaInicioSecadoObjetivo,
    FechaLimiteEntregaMaterial=@FechaLimiteEntregaMaterial,
    FechaObjetivoFinSecado=@FechaObjetivoFinSecado,
    Estado=N'PENDIENTE',
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=@Ahora
WHERE SecadoMaterialID=@SecadoMaterialID
  AND Activo=1
  AND Estado=N'PENDIENTE'
  AND ISNULL(CantidadAsignadaKg,0)<=0.0005
  AND ISNULL(CantidadFinalizadaKg,0)<=0.0005
  AND FechaPrimerInicioSecado IS NULL
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.Produccion_SecadoCargas c
      WHERE c.SecadoMaterialID=@SecadoMaterialID
  );

IF @@ROWCOUNT<>1
    THROW 51320,N'El lote de secado comenzó mientras se intentaba acumular una nueva recepción. Intenta nuevamente.',1;";

                await using (var cmd = new SqlCommand(sqlAcumular, cn, tx))
                {
                    var pCantidad = cmd.Parameters.Add("@CantidadRecibidaKg", SqlDbType.Decimal);
                    pCantidad.Precision = 18;
                    pCantidad.Scale = 4;
                    pCantidad.Value = cantidadRecibidaKg;

                    var pHoras = cmd.Parameters.Add("@HorasSecadoRequeridas", SqlDbType.Decimal);
                    pHoras.Precision = 10;
                    pHoras.Scale = 2;
                    pHoras.Value = horasSecado;

                    cmd.Parameters.Add("@SecadoMaterialID", SqlDbType.BigInt).Value = secadoMaterialAcumulableId.Value;
                    cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId.HasValue && ejecucionProduccionId.Value > 0 ? ejecucionProduccionId.Value : DBNull.Value;
                    cmd.Parameters.Add("@MaquinaProgramadaID", SqlDbType.Int).Value = maquinaProgramadaId.HasValue && maquinaProgramadaId.Value > 0 ? maquinaProgramadaId.Value : DBNull.Value;
                    cmd.Parameters.Add("@MaterialCodigo", SqlDbType.NVarChar, 100).Value = string.IsNullOrWhiteSpace(materialCodigo) ? DBNull.Value : materialCodigo;
                    cmd.Parameters.Add("@MaterialDescripcion", SqlDbType.NVarChar, 300).Value = string.IsNullOrWhiteSpace(materialDescripcion) ? DBNull.Value : materialDescripcion;
                    cmd.Parameters.Add("@Lote", SqlDbType.NVarChar, 100).Value = string.IsNullOrWhiteSpace(lote) ? DBNull.Value : lote;
                    cmd.Parameters.Add("@TipoSecadoOrigen", SqlDbType.NVarChar, 100).Value = string.IsNullOrWhiteSpace(tipoSecadoOrigen) ? DBNull.Value : tipoSecadoOrigen;
                    cmd.Parameters.Add("@TipoProceso", SqlDbType.NVarChar, 30).Value = tipoProceso;
                    cmd.Parameters.Add("@MinutosSecadoRequeridos", SqlDbType.Int).Value = minutosSecado;
                    cmd.Parameters.Add("@MargenEntregaAntesSecadoMinutos", SqlDbType.Int).Value = configuracion.MargenEntregaAntesSecadoMinutos;
                    cmd.Parameters.Add("@FechaRecepcionProduccion", SqlDbType.DateTime2).Value = fechaRecepcionProduccion;
                    cmd.Parameters.Add("@FechaArranqueProduccion", SqlDbType.DateTime2).Value = fechaArranqueProduccion.HasValue ? fechaArranqueProduccion.Value : DBNull.Value;
                    cmd.Parameters.Add("@FechaInicioSecadoObjetivo", SqlDbType.DateTime2).Value = fechaInicioSecadoObjetivo.HasValue ? fechaInicioSecadoObjetivo.Value : DBNull.Value;
                    cmd.Parameters.Add("@FechaLimiteEntregaMaterial", SqlDbType.DateTime2).Value = fechaLimiteEntregaMaterial.HasValue ? fechaLimiteEntregaMaterial.Value : DBNull.Value;
                    cmd.Parameters.Add("@FechaObjetivoFinSecado", SqlDbType.DateTime2).Value = fechaObjetivoFinSecado.HasValue ? fechaObjetivoFinSecado.Value : DBNull.Value;
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                    cmd.Parameters.Add("@Ahora", SqlDbType.DateTime2).Value = ahora;
                    await cmd.ExecuteNonQueryAsync();
                }

                await AgregarHistorialSecadoAsync(secadoMaterialAcumulableId.Value, null, "RECEPCION_MP_ACUMULADA", ProduccionSecadoEstadoMaterial.Pendiente, ProduccionSecadoEstadoMaterial.Pendiente, null, null, cantidadRecibidaKg, $"Se acumularon {cantidadRecibidaKg:0.####} KG adicionales antes de iniciar el secado. Recepción #{recepcionMaterialId}.", usuarioId, ahora, cn, tx);
                await RegistrarRecepcionProcesadaSecadoAsync(recepcionMaterialId, secadoMaterialAcumulableId.Value, "SECADO_ACUMULADO", cantidadRecibidaKg, usuarioId, ahora, cn, tx);
                return true;
            }

            const string sqlInsertar = @"
INSERT dbo.Produccion_SecadoMaterial
(
    RecepcionMaterialID,SolicitudProduccionID,SolicitudProduccionDetalleID,ProgramaProduccionID,EjecucionProduccionID,
    MaquinaProgramadaID,MaterialID,NumeroOFSnapshot,MaterialCodigoSnapshot,MaterialDescripcionSnapshot,TipoMP,Lote,
    CantidadRecibidaKg,CantidadAsignadaKg,CantidadFinalizadaKg,TipoSecadoOrigen,TipoProceso,HorasSecadoRequeridas,
    MinutosSecadoRequeridos,MargenEntregaAntesSecadoMinutos,FechaRecepcionProduccion,FechaArranqueProduccion,
    FechaInicioSecadoObjetivo,FechaLimiteEntregaMaterial,FechaObjetivoFinSecado,FechaPrimerInicioSecado,FechaUltimoFinSecado,
    MinutosEsperaInicio,MinutosRetrasoFinal,Estado,Observaciones,Activo,UsuarioCreacionID,FechaCreacion,UsuarioModificacionID,FechaModificacion
)
OUTPUT INSERTED.SecadoMaterialID
VALUES
(
    @RecepcionMaterialID,@SolicitudProduccionID,@SolicitudProduccionDetalleID,@ProgramaProduccionID,@EjecucionProduccionID,
    @MaquinaProgramadaID,@MaterialID,@NumeroOF,@MaterialCodigo,@MaterialDescripcion,@TipoMP,@Lote,
    @CantidadRecibidaKg,0,0,@TipoSecadoOrigen,@TipoProceso,@HorasSecadoRequeridas,@MinutosSecadoRequeridos,
    @MargenEntregaAntesSecadoMinutos,@FechaRecepcionProduccion,@FechaArranqueProduccion,@FechaInicioSecadoObjetivo,
    @FechaLimiteEntregaMaterial,@FechaObjetivoFinSecado,NULL,NULL,NULL,NULL,N'PENDIENTE',
    N'Lote de secado generado automáticamente desde la confirmación física de recepción en Producción.',
    1,@UsuarioID,@Ahora,NULL,NULL
);";

            long secadoMaterialId;

            await using (var cmd = new SqlCommand(sqlInsertar, cn, tx))
            {
                cmd.Parameters.Add("@RecepcionMaterialID", SqlDbType.BigInt).Value = recepcionMaterialId;
                cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = solicitudProduccionId;
                cmd.Parameters.Add("@SolicitudProduccionDetalleID", SqlDbType.Int).Value = solicitudProduccionDetalleId.Value;
                cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaProduccionId.Value;
                cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId.HasValue && ejecucionProduccionId.Value > 0 ? ejecucionProduccionId.Value : DBNull.Value;
                cmd.Parameters.Add("@MaquinaProgramadaID", SqlDbType.Int).Value = maquinaProgramadaId.HasValue && maquinaProgramadaId.Value > 0 ? maquinaProgramadaId.Value : DBNull.Value;
                cmd.Parameters.Add("@MaterialID", SqlDbType.Int).Value = materialId;
                cmd.Parameters.Add("@NumeroOF", SqlDbType.NVarChar, 80).Value = string.IsNullOrWhiteSpace(numeroOF) ? "SIN OF" : numeroOF;
                cmd.Parameters.Add("@MaterialCodigo", SqlDbType.NVarChar, 100).Value = string.IsNullOrWhiteSpace(materialCodigo) ? "SIN CODIGO" : materialCodigo;
                cmd.Parameters.Add("@MaterialDescripcion", SqlDbType.NVarChar, 300).Value = string.IsNullOrWhiteSpace(materialDescripcion) ? DBNull.Value : materialDescripcion;
                cmd.Parameters.Add("@TipoMP", SqlDbType.NChar, 1).Value = string.IsNullOrWhiteSpace(tipoMP) ? DBNull.Value : tipoMP;
                cmd.Parameters.Add("@Lote", SqlDbType.NVarChar, 100).Value = string.IsNullOrWhiteSpace(lote) ? DBNull.Value : lote;

                var pCantidad = cmd.Parameters.Add("@CantidadRecibidaKg", SqlDbType.Decimal);
                pCantidad.Precision = 18;
                pCantidad.Scale = 4;
                pCantidad.Value = cantidadRecibidaKg;

                var pHoras = cmd.Parameters.Add("@HorasSecadoRequeridas", SqlDbType.Decimal);
                pHoras.Precision = 10;
                pHoras.Scale = 2;
                pHoras.Value = horasSecado;

                cmd.Parameters.Add("@TipoSecadoOrigen", SqlDbType.NVarChar, 100).Value = string.IsNullOrWhiteSpace(tipoSecadoOrigen) ? DBNull.Value : tipoSecadoOrigen;
                cmd.Parameters.Add("@TipoProceso", SqlDbType.NVarChar, 30).Value = tipoProceso;
                cmd.Parameters.Add("@MinutosSecadoRequeridos", SqlDbType.Int).Value = minutosSecado;
                cmd.Parameters.Add("@MargenEntregaAntesSecadoMinutos", SqlDbType.Int).Value = configuracion.MargenEntregaAntesSecadoMinutos;
                cmd.Parameters.Add("@FechaRecepcionProduccion", SqlDbType.DateTime2).Value = fechaRecepcionProduccion;
                cmd.Parameters.Add("@FechaArranqueProduccion", SqlDbType.DateTime2).Value = fechaArranqueProduccion.HasValue ? fechaArranqueProduccion.Value : DBNull.Value;
                cmd.Parameters.Add("@FechaInicioSecadoObjetivo", SqlDbType.DateTime2).Value = fechaInicioSecadoObjetivo.HasValue ? fechaInicioSecadoObjetivo.Value : DBNull.Value;
                cmd.Parameters.Add("@FechaLimiteEntregaMaterial", SqlDbType.DateTime2).Value = fechaLimiteEntregaMaterial.HasValue ? fechaLimiteEntregaMaterial.Value : DBNull.Value;
                cmd.Parameters.Add("@FechaObjetivoFinSecado", SqlDbType.DateTime2).Value = fechaObjetivoFinSecado.HasValue ? fechaObjetivoFinSecado.Value : DBNull.Value;
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                cmd.Parameters.Add("@Ahora", SqlDbType.DateTime2).Value = ahora;

                var valor = await cmd.ExecuteScalarAsync();
                if (valor == null || valor == DBNull.Value)
                    throw new InvalidOperationException("No fue posible generar el lote pendiente de Secado.");

                secadoMaterialId = Convert.ToInt64(valor);
            }

            await AgregarHistorialSecadoAsync(secadoMaterialId, null, "RECEPCION_MP_CONFIRMADA", null, ProduccionSecadoEstadoMaterial.Pendiente, null, null, cantidadRecibidaKg, $"Producción confirmó físicamente {cantidadRecibidaKg:0.####} KG. Se abrió un nuevo lote de {tipoProceso.ToLowerInvariant()} porque no existía un lote acumulable sin iniciar.", usuarioId, ahora, cn, tx);
            await RegistrarRecepcionProcesadaSecadoAsync(recepcionMaterialId, secadoMaterialId, "SECADO_LOTE_CREADO", cantidadRecibidaKg, usuarioId, ahora, cn, tx);
            return true;
        }

        private static async Task RegistrarRecepcionProcesadaSecadoAsync(long recepcionMaterialId, long secadoMaterialId, string evento, decimal cantidadKg, int usuarioId, DateTime ahora, SqlConnection cn, SqlTransaction tx)
        {
            const string sql = @"
INSERT dbo.Produccion_RecepcionMaterialesHistorial
(
    RecepcionMaterialID,Evento,EstadoRecepcionAnterior,EstadoRecepcionNuevo,
    CantidadRecibidaAnterior,CantidadRecibidaNueva,
    EstadoAclaracionAnterior,EstadoAclaracionNuevo,
    Comentario,UsuarioID,FechaEvento
)
SELECT
    r.RecepcionMaterialID,
    @Evento,
    r.EstadoRecepcion,
    r.EstadoRecepcion,
    r.CantidadRecibidaProduccion,
    r.CantidadRecibidaProduccion,
    r.EstadoAclaracion,
    r.EstadoAclaracion,
    LEFT(N'Recepción incorporada al lote de secado #'+CONVERT(NVARCHAR(30),@SecadoMaterialID)+N'. Cantidad: '+CONVERT(NVARCHAR(50),@CantidadKg)+N' KG.',1000),
    @UsuarioID,
    @Ahora
FROM dbo.Produccion_RecepcionMateriales r
WHERE r.RecepcionMaterialID=@RecepcionMaterialID
  AND r.Activo=1
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.Produccion_RecepcionMaterialesHistorial h WITH(UPDLOCK,HOLDLOCK)
      WHERE h.RecepcionMaterialID=r.RecepcionMaterialID
        AND h.Evento IN(N'SECADO_LOTE_CREADO',N'SECADO_ACUMULADO')
  );";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@RecepcionMaterialID", SqlDbType.BigInt).Value = recepcionMaterialId;
            cmd.Parameters.Add("@SecadoMaterialID", SqlDbType.BigInt).Value = secadoMaterialId;
            cmd.Parameters.Add("@Evento", SqlDbType.NVarChar, 60).Value = evento;

            var pCantidad = cmd.Parameters.Add("@CantidadKg", SqlDbType.Decimal);
            pCantidad.Precision = 18;
            pCantidad.Scale = 4;
            pCantidad.Value = cantidadKg;

            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
            cmd.Parameters.Add("@Ahora", SqlDbType.DateTime2).Value = ahora;
            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task<ProduccionSecadoConfiguracionVm> CargarConfiguracionSecadoAsync(SqlConnection cn, SqlTransaction? tx = null)
        {
            var vm = new ProduccionSecadoConfiguracionVm();

            const string sql = @"
SELECT TOP(1)
    ConfiguracionID,Codigo,Nombre,MargenEntregaAntesSecadoMinutos,MinutosAlertaEsperaInicio,MinutosAvisoProximoFin,MinutosToleranciaFin
FROM dbo.Produccion_SecadoConfiguracion
WHERE Activo=1
ORDER BY CASE WHEN Codigo=N'GENERAL' THEN 0 ELSE 1 END,ConfiguracionID DESC;";

            await using var cmd = tx == null ? new SqlCommand(sql, cn) : new SqlCommand(sql, cn, tx);
            await using var rd = await cmd.ExecuteReaderAsync();

            if (!await rd.ReadAsync()) return vm;

            vm.ConfiguracionID = Convert.ToInt32(rd["ConfiguracionID"]);
            vm.Codigo = rd["Codigo"]?.ToString()?.Trim() ?? "GENERAL";
            vm.Nombre = rd["Nombre"]?.ToString()?.Trim() ?? string.Empty;
            vm.MargenEntregaAntesSecadoMinutos = Convert.ToInt32(rd["MargenEntregaAntesSecadoMinutos"]);
            vm.MinutosAlertaEsperaInicio = Convert.ToInt32(rd["MinutosAlertaEsperaInicio"]);
            vm.MinutosAvisoProximoFin = Convert.ToInt32(rd["MinutosAvisoProximoFin"]);
            vm.MinutosToleranciaFin = Convert.ToInt32(rd["MinutosToleranciaFin"]);
            return vm;
        }

        private static async Task<List<ProduccionSecadoTolvaVm>> CargarTolvasSecadoAsync(SqlConnection cn)
        {
            var lista = new List<ProduccionSecadoTolvaVm>();

            const string sql = @"
SELECT
    t.TolvaID,
    t.MaquinaID,
    m.Codigo AS MaquinaCodigo,
    m.Nombre AS MaquinaNombre,
    t.Codigo,
    t.Nombre,
    t.CapacidadKg,
    t.TipoProcesoPermitido,
    t.DisponibleOperativamente,
    t.EsDatoTemporal,
    t.Activo,
    CASE WHEN ocupacion.SecadoCargaID IS NULL THEN CAST(0 AS bit) ELSE CAST(1 AS bit) END AS EstaOcupada,
    ocupacion.SecadoCargaID,
    ocupacion.NumeroOF,
    ocupacion.MaterialCodigo,
    ocupacion.FechaFinEsperada
FROM dbo.Produccion_SecadoTolvas t
INNER JOIN dbo.ERP_Maquinas m ON m.MaquinaID=t.MaquinaID
OUTER APPLY
(
    SELECT TOP(1)
        c.SecadoCargaID,
        sm.NumeroOFSnapshot AS NumeroOF,
        sm.MaterialCodigoSnapshot AS MaterialCodigo,
        c.FechaFinEsperada
    FROM dbo.Produccion_SecadoCargaSegmentos s
    INNER JOIN dbo.Produccion_SecadoCargas c
        ON c.SecadoCargaID=s.SecadoCargaID
       AND c.Activo=1
       AND c.Estado=N'EN_PROCESO'
    INNER JOIN dbo.Produccion_SecadoMaterial sm
        ON sm.SecadoMaterialID=c.SecadoMaterialID
       AND sm.Activo=1
    WHERE s.TolvaID=t.TolvaID
      AND s.Activo=1
      AND s.FechaFin IS NULL
    ORDER BY s.SecadoCargaSegmentoID DESC
) ocupacion
WHERE t.Activo=1
ORDER BY m.Codigo,t.Codigo;";

            await using var cmd = new SqlCommand(sql, cn);
            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                lista.Add(new ProduccionSecadoTolvaVm
                {
                    TolvaID = Convert.ToInt32(rd["TolvaID"]),
                    MaquinaID = Convert.ToInt32(rd["MaquinaID"]),
                    MaquinaCodigo = rd["MaquinaCodigo"]?.ToString()?.Trim() ?? string.Empty,
                    MaquinaNombre = rd["MaquinaNombre"] == DBNull.Value ? null : rd["MaquinaNombre"]?.ToString()?.Trim(),
                    Codigo = rd["Codigo"]?.ToString()?.Trim() ?? string.Empty,
                    Nombre = rd["Nombre"]?.ToString()?.Trim() ?? string.Empty,
                    CapacidadKg = Convert.ToDecimal(rd["CapacidadKg"]),
                    TipoProcesoPermitido = rd["TipoProcesoPermitido"]?.ToString()?.Trim() ?? "AMBOS",
                    DisponibleOperativamente = rd["DisponibleOperativamente"] != DBNull.Value && Convert.ToBoolean(rd["DisponibleOperativamente"]),
                    EsDatoTemporal = rd["EsDatoTemporal"] != DBNull.Value && Convert.ToBoolean(rd["EsDatoTemporal"]),
                    Activo = rd["Activo"] != DBNull.Value && Convert.ToBoolean(rd["Activo"]),
                    EstaOcupada = rd["EstaOcupada"] != DBNull.Value && Convert.ToBoolean(rd["EstaOcupada"]),
                    SecadoCargaIDActiva = rd["SecadoCargaID"] == DBNull.Value ? null : Convert.ToInt64(rd["SecadoCargaID"]),
                    NumeroOFActiva = rd["NumeroOF"] == DBNull.Value ? null : rd["NumeroOF"]?.ToString()?.Trim(),
                    MaterialCodigoActivo = rd["MaterialCodigo"] == DBNull.Value ? null : rd["MaterialCodigo"]?.ToString()?.Trim(),
                    FechaFinEsperadaActiva = rd["FechaFinEsperada"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaFinEsperada"])
                });
            }

            return lista;
        }

        private static async Task<List<ProduccionSecadoMaterialVm>> CargarMaterialesSecadoAsync(string? filtro, int? maquinaId, DateTime ahora, ProduccionSecadoConfiguracionVm configuracion, SqlConnection cn)
        {
            var lista = new List<ProduccionSecadoMaterialVm>();

            const string sql = @"
SELECT
    sm.SecadoMaterialID,
    sm.RecepcionMaterialID,
    sm.SolicitudProduccionID,
    sm.SolicitudProduccionDetalleID,
    sm.ProgramaProduccionID,
    sm.EjecucionProduccionID,
    sm.MaquinaProgramadaID,
    maq.Codigo AS MaquinaProgramadaCodigo,
    maq.Nombre AS MaquinaProgramadaNombre,
    sm.MaterialID,
    sm.NumeroOFSnapshot,
    sm.MaterialCodigoSnapshot,
    sm.MaterialDescripcionSnapshot,
    sm.TipoMP,
    sm.Lote,
    sm.CantidadRecibidaKg,
    sm.CantidadAsignadaKg,
    sm.CantidadFinalizadaKg,
    sm.TipoSecadoOrigen,
    sm.TipoProceso,
    sm.HorasSecadoRequeridas,
    sm.MinutosSecadoRequeridos,
    sm.MargenEntregaAntesSecadoMinutos,
    sm.FechaRecepcionProduccion,
    sm.FechaArranqueProduccion,
    sm.FechaInicioSecadoObjetivo,
    sm.FechaLimiteEntregaMaterial,
    sm.FechaObjetivoFinSecado,
    sm.FechaPrimerInicioSecado,
    sm.FechaUltimoFinSecado,
    sm.MinutosEsperaInicio,
    sm.MinutosRetrasoFinal,
    sm.Estado,
    sm.Observaciones,
    tolva.TolvaID AS TolvaSugeridaID,
    tolva.Codigo AS TolvaSugeridaCodigo,
    tolva.Nombre AS TolvaSugeridaNombre,
    tolva.CapacidadKg AS TolvaSugeridaCapacidadKg
FROM dbo.Produccion_SecadoMaterial sm
LEFT JOIN dbo.ERP_Maquinas maq ON maq.MaquinaID=sm.MaquinaProgramadaID
OUTER APPLY
(
    SELECT TOP(1)
        t.TolvaID,
        t.Codigo,
        t.Nombre,
        t.CapacidadKg
    FROM dbo.Produccion_SecadoTolvas t
    WHERE t.MaquinaID=sm.MaquinaProgramadaID
      AND t.Activo=1
      AND t.DisponibleOperativamente=1
      AND
      (
          UPPER(LTRIM(RTRIM(ISNULL(t.TipoProcesoPermitido,N'AMBOS'))))=N'AMBOS'
          OR UPPER(LTRIM(RTRIM(ISNULL(t.TipoProcesoPermitido,N'AMBOS'))))=UPPER(LTRIM(RTRIM(ISNULL(sm.TipoProceso,N'SECADO'))))
      )
      AND NOT EXISTS
      (
          SELECT 1
          FROM dbo.Produccion_SecadoCargaSegmentos seg
          WHERE seg.TolvaID=t.TolvaID
            AND seg.Activo=1
            AND seg.FechaFin IS NULL
      )
    ORDER BY
        CASE
            WHEN t.CapacidadKg>=
                CASE
                    WHEN ISNULL(sm.CantidadRecibidaKg,0)-ISNULL(sm.CantidadAsignadaKg,0)>0
                        THEN ISNULL(sm.CantidadRecibidaKg,0)-ISNULL(sm.CantidadAsignadaKg,0)
                    ELSE 0
                END
            THEN 0
            ELSE 1
        END,
        CASE
            WHEN t.CapacidadKg>=
                CASE
                    WHEN ISNULL(sm.CantidadRecibidaKg,0)-ISNULL(sm.CantidadAsignadaKg,0)>0
                        THEN ISNULL(sm.CantidadRecibidaKg,0)-ISNULL(sm.CantidadAsignadaKg,0)
                    ELSE 0
                END
            THEN t.CapacidadKg
            ELSE NULL
        END ASC,
        t.CapacidadKg DESC,
        t.TolvaID
) tolva
WHERE sm.Activo=1
  AND sm.Estado<>N'CANCELADO'
  AND (@MaquinaID IS NULL OR sm.MaquinaProgramadaID=@MaquinaID)
  AND
  (
      @Filtro IS NULL
      OR sm.NumeroOFSnapshot LIKE N'%'+@Filtro+N'%'
      OR sm.MaterialCodigoSnapshot LIKE N'%'+@Filtro+N'%'
      OR sm.MaterialDescripcionSnapshot LIKE N'%'+@Filtro+N'%'
      OR maq.Codigo LIKE N'%'+@Filtro+N'%'
      OR maq.Nombre LIKE N'%'+@Filtro+N'%'
  )
  AND
  (
      sm.Estado<>N'FINALIZADO'
      OR sm.FechaUltimoFinSecado>=DATEADD(DAY,-30,SYSDATETIME())
  )
ORDER BY
    CASE sm.Estado WHEN N'EN_PROCESO' THEN 0 WHEN N'PENDIENTE' THEN 1 WHEN N'PARCIAL' THEN 2 ELSE 3 END,
    COALESCE(sm.FechaInicioSecadoObjetivo,sm.FechaArranqueProduccion,sm.FechaRecepcionProduccion),
    sm.SecadoMaterialID;";

            await using (var cmd = new SqlCommand(sql, cn))
            {
                cmd.Parameters.Add("@Filtro", SqlDbType.NVarChar, 200).Value = string.IsNullOrWhiteSpace(filtro) ? DBNull.Value : filtro.Trim();
                cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = maquinaId.HasValue && maquinaId.Value > 0 ? maquinaId.Value : DBNull.Value;

                await using var rd = await cmd.ExecuteReaderAsync();

                while (await rd.ReadAsync())
                {
                    lista.Add(new ProduccionSecadoMaterialVm
                    {
                        SecadoMaterialID = Convert.ToInt64(rd["SecadoMaterialID"]),
                        RecepcionMaterialID = Convert.ToInt64(rd["RecepcionMaterialID"]),
                        SolicitudProduccionID = Convert.ToInt32(rd["SolicitudProduccionID"]),
                        SolicitudProduccionDetalleID = rd["SolicitudProduccionDetalleID"] == DBNull.Value ? null : Convert.ToInt32(rd["SolicitudProduccionDetalleID"]),
                        ProgramaProduccionID = rd["ProgramaProduccionID"] == DBNull.Value ? null : Convert.ToInt32(rd["ProgramaProduccionID"]),
                        EjecucionProduccionID = rd["EjecucionProduccionID"] == DBNull.Value ? null : Convert.ToInt32(rd["EjecucionProduccionID"]),
                        MaquinaProgramadaID = rd["MaquinaProgramadaID"] == DBNull.Value ? null : Convert.ToInt32(rd["MaquinaProgramadaID"]),
                        MaquinaProgramadaCodigo = rd["MaquinaProgramadaCodigo"] == DBNull.Value ? null : rd["MaquinaProgramadaCodigo"]?.ToString()?.Trim(),
                        MaquinaProgramadaNombre = rd["MaquinaProgramadaNombre"] == DBNull.Value ? null : rd["MaquinaProgramadaNombre"]?.ToString()?.Trim(),
                        MaterialID = Convert.ToInt32(rd["MaterialID"]),
                        NumeroOF = rd["NumeroOFSnapshot"]?.ToString()?.Trim() ?? string.Empty,
                        MaterialCodigo = rd["MaterialCodigoSnapshot"]?.ToString()?.Trim() ?? string.Empty,
                        MaterialDescripcion = rd["MaterialDescripcionSnapshot"] == DBNull.Value ? null : rd["MaterialDescripcionSnapshot"]?.ToString()?.Trim(),
                        TipoMP = rd["TipoMP"] == DBNull.Value ? null : rd["TipoMP"]?.ToString()?.Trim(),
                        Lote = rd["Lote"] == DBNull.Value ? null : rd["Lote"]?.ToString()?.Trim(),
                        CantidadRecibidaKg = Convert.ToDecimal(rd["CantidadRecibidaKg"]),
                        CantidadAsignadaKg = Convert.ToDecimal(rd["CantidadAsignadaKg"]),
                        CantidadFinalizadaKg = Convert.ToDecimal(rd["CantidadFinalizadaKg"]),
                        TipoSecadoOrigen = rd["TipoSecadoOrigen"] == DBNull.Value ? null : rd["TipoSecadoOrigen"]?.ToString()?.Trim(),
                        TipoProceso = rd["TipoProceso"]?.ToString()?.Trim() ?? ProduccionSecadoTipoProceso.Secado,
                        HorasSecadoRequeridas = Convert.ToDecimal(rd["HorasSecadoRequeridas"]),
                        MinutosSecadoRequeridos = Convert.ToInt32(rd["MinutosSecadoRequeridos"]),
                        MargenEntregaAntesSecadoMinutos = Convert.ToInt32(rd["MargenEntregaAntesSecadoMinutos"]),
                        FechaRecepcionProduccion = Convert.ToDateTime(rd["FechaRecepcionProduccion"]),
                        FechaArranqueProduccion = rd["FechaArranqueProduccion"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaArranqueProduccion"]),
                        FechaInicioSecadoObjetivo = rd["FechaInicioSecadoObjetivo"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaInicioSecadoObjetivo"]),
                        FechaLimiteEntregaMaterial = rd["FechaLimiteEntregaMaterial"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaLimiteEntregaMaterial"]),
                        FechaObjetivoFinSecado = rd["FechaObjetivoFinSecado"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaObjetivoFinSecado"]),
                        FechaPrimerInicioSecado = rd["FechaPrimerInicioSecado"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaPrimerInicioSecado"]),
                        FechaUltimoFinSecado = rd["FechaUltimoFinSecado"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaUltimoFinSecado"]),
                        MinutosEsperaInicio = rd["MinutosEsperaInicio"] == DBNull.Value ? null : Convert.ToInt32(rd["MinutosEsperaInicio"]),
                        MinutosRetrasoFinal = rd["MinutosRetrasoFinal"] == DBNull.Value ? null : Convert.ToInt32(rd["MinutosRetrasoFinal"]),
                        Estado = rd["Estado"]?.ToString()?.Trim() ?? ProduccionSecadoEstadoMaterial.Pendiente,
                        Observaciones = rd["Observaciones"] == DBNull.Value ? null : rd["Observaciones"]?.ToString()?.Trim(),
                        TolvaSugeridaID = rd["TolvaSugeridaID"] == DBNull.Value ? null : Convert.ToInt32(rd["TolvaSugeridaID"]),
                        TolvaSugeridaCodigo = rd["TolvaSugeridaCodigo"] == DBNull.Value ? null : rd["TolvaSugeridaCodigo"]?.ToString()?.Trim(),
                        TolvaSugeridaNombre = rd["TolvaSugeridaNombre"] == DBNull.Value ? null : rd["TolvaSugeridaNombre"]?.ToString()?.Trim(),
                        TolvaSugeridaCapacidadKg = rd["TolvaSugeridaCapacidadKg"] == DBNull.Value ? null : Convert.ToDecimal(rd["TolvaSugeridaCapacidadKg"]),
                        Ahora = ahora,
                        MinutosAlertaEsperaInicio = configuracion.MinutosAlertaEsperaInicio
                    });
                }
            }

            if (!lista.Any())
                return lista;

            var porId = lista.ToDictionary(x => x.SecadoMaterialID);

            const string sqlCargas = @"
SELECT
    c.SecadoCargaID,
    c.SecadoMaterialID,
    c.NumeroCarga,
    c.TolvaIDActual,
    t.MaquinaID AS MaquinaTolvaID,
    maq.Codigo AS MaquinaTolvaCodigo,
    maq.Nombre AS MaquinaTolvaNombre,
    t.Codigo AS TolvaCodigo,
    t.Nombre AS TolvaNombre,
    c.CantidadKg,
    c.CapacidadTolvaKgSnapshot,
    c.DuracionRequeridaMinutos,
    c.Estado,
    c.FechaDisponibleDesde,
    c.FechaAsignacionTolva,
    c.FechaInicioReal,
    c.FechaFinEsperada,
    c.FechaFinReal,
    c.MinutosEsperaAntesInicio,
    c.DuracionRealMinutos,
    c.MinutosExcesoSecado,
    c.ExcedioTiempo,
    c.FinalizoAntesTiempo,
    c.MotivoFinalizacionAnticipada,
    c.UsuarioInicioID,
    c.UsuarioFinID,
    c.Observaciones
FROM dbo.Produccion_SecadoCargas c
INNER JOIN dbo.Produccion_SecadoTolvas t ON t.TolvaID=c.TolvaIDActual
LEFT JOIN dbo.ERP_Maquinas maq ON maq.MaquinaID=t.MaquinaID
WHERE c.Activo=1
ORDER BY c.SecadoMaterialID,c.NumeroCarga;";

            var cargasPorId = new Dictionary<long, ProduccionSecadoCargaVm>();

            await using (var cmd = new SqlCommand(sqlCargas, cn))
            await using (var rd = await cmd.ExecuteReaderAsync())
            {
                while (await rd.ReadAsync())
                {
                    var materialId = Convert.ToInt64(rd["SecadoMaterialID"]);

                    if (!porId.TryGetValue(materialId, out var material))
                        continue;

                    var carga = new ProduccionSecadoCargaVm
                    {
                        SecadoCargaID = Convert.ToInt64(rd["SecadoCargaID"]),
                        SecadoMaterialID = materialId,
                        NumeroCarga = Convert.ToInt32(rd["NumeroCarga"]),
                        TolvaIDActual = Convert.ToInt32(rd["TolvaIDActual"]),
                        MaquinaTolvaID = rd["MaquinaTolvaID"] == DBNull.Value ? null : Convert.ToInt32(rd["MaquinaTolvaID"]),
                        MaquinaTolvaCodigo = rd["MaquinaTolvaCodigo"] == DBNull.Value ? null : rd["MaquinaTolvaCodigo"]?.ToString()?.Trim(),
                        MaquinaTolvaNombre = rd["MaquinaTolvaNombre"] == DBNull.Value ? null : rd["MaquinaTolvaNombre"]?.ToString()?.Trim(),
                        TolvaCodigo = rd["TolvaCodigo"]?.ToString()?.Trim() ?? string.Empty,
                        TolvaNombre = rd["TolvaNombre"]?.ToString()?.Trim() ?? string.Empty,
                        CantidadKg = Convert.ToDecimal(rd["CantidadKg"]),
                        CapacidadTolvaKgSnapshot = Convert.ToDecimal(rd["CapacidadTolvaKgSnapshot"]),
                        DuracionRequeridaMinutos = Convert.ToInt32(rd["DuracionRequeridaMinutos"]),
                        Estado = rd["Estado"]?.ToString()?.Trim() ?? ProduccionSecadoEstadoCarga.Pendiente,
                        FechaDisponibleDesde = Convert.ToDateTime(rd["FechaDisponibleDesde"]),
                        FechaAsignacionTolva = Convert.ToDateTime(rd["FechaAsignacionTolva"]),
                        FechaInicioReal = rd["FechaInicioReal"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaInicioReal"]),
                        FechaFinEsperada = rd["FechaFinEsperada"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaFinEsperada"]),
                        FechaFinReal = rd["FechaFinReal"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaFinReal"]),
                        MinutosEsperaAntesInicio = rd["MinutosEsperaAntesInicio"] == DBNull.Value ? null : Convert.ToInt32(rd["MinutosEsperaAntesInicio"]),
                        DuracionRealMinutos = rd["DuracionRealMinutos"] == DBNull.Value ? null : Convert.ToInt32(rd["DuracionRealMinutos"]),
                        MinutosExcesoSecado = rd["MinutosExcesoSecado"] == DBNull.Value ? null : Convert.ToInt32(rd["MinutosExcesoSecado"]),
                        ExcedioTiempo = rd["ExcedioTiempo"] != DBNull.Value && Convert.ToBoolean(rd["ExcedioTiempo"]),
                        FinalizoAntesTiempo = rd["FinalizoAntesTiempo"] != DBNull.Value && Convert.ToBoolean(rd["FinalizoAntesTiempo"]),
                        MotivoFinalizacionAnticipada = rd["MotivoFinalizacionAnticipada"] == DBNull.Value ? null : rd["MotivoFinalizacionAnticipada"]?.ToString()?.Trim(),
                        UsuarioInicioID = rd["UsuarioInicioID"] == DBNull.Value ? null : Convert.ToInt32(rd["UsuarioInicioID"]),
                        UsuarioFinID = rd["UsuarioFinID"] == DBNull.Value ? null : Convert.ToInt32(rd["UsuarioFinID"]),
                        Observaciones = rd["Observaciones"] == DBNull.Value ? null : rd["Observaciones"]?.ToString()?.Trim(),
                        Ahora = ahora,
                        MinutosAvisoProximoFin = configuracion.MinutosAvisoProximoFin,
                        MinutosToleranciaFin = configuracion.MinutosToleranciaFin
                    };

                    material.Cargas.Add(carga);
                    cargasPorId[carga.SecadoCargaID] = carga;
                }
            }

            if (!cargasPorId.Any())
                return lista;

            const string sqlSegmentos = @"
SELECT
    s.SecadoCargaSegmentoID,
    s.SecadoCargaID,
    s.TolvaID,
    t.Codigo AS TolvaCodigo,
    t.Nombre AS TolvaNombre,
    s.NumeroSegmento,
    s.FechaInicio,
    s.FechaFin,
    s.MinutosSegmento,
    s.EsCambioTolva,
    s.ReiniciaTiempoRequerido,
    s.MotivoCambio,
    s.UsuarioInicioID,
    s.UsuarioFinID
FROM dbo.Produccion_SecadoCargaSegmentos s
INNER JOIN dbo.Produccion_SecadoTolvas t ON t.TolvaID=s.TolvaID
WHERE s.Activo=1
ORDER BY s.SecadoCargaID,s.NumeroSegmento;";

            await using (var cmd = new SqlCommand(sqlSegmentos, cn))
            await using (var rd = await cmd.ExecuteReaderAsync())
            {
                while (await rd.ReadAsync())
                {
                    var cargaId = Convert.ToInt64(rd["SecadoCargaID"]);

                    if (!cargasPorId.TryGetValue(cargaId, out var carga))
                        continue;

                    carga.Segmentos.Add(new ProduccionSecadoSegmentoVm
                    {
                        SecadoCargaSegmentoID = Convert.ToInt64(rd["SecadoCargaSegmentoID"]),
                        SecadoCargaID = cargaId,
                        TolvaID = Convert.ToInt32(rd["TolvaID"]),
                        TolvaCodigo = rd["TolvaCodigo"] == DBNull.Value ? null : rd["TolvaCodigo"]?.ToString()?.Trim(),
                        TolvaNombre = rd["TolvaNombre"] == DBNull.Value ? null : rd["TolvaNombre"]?.ToString()?.Trim(),
                        NumeroSegmento = Convert.ToInt32(rd["NumeroSegmento"]),
                        FechaInicio = Convert.ToDateTime(rd["FechaInicio"]),
                        FechaFin = rd["FechaFin"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaFin"]),
                        MinutosSegmento = rd["MinutosSegmento"] == DBNull.Value ? null : Convert.ToInt32(rd["MinutosSegmento"]),
                        EsCambioTolva = rd["EsCambioTolva"] != DBNull.Value && Convert.ToBoolean(rd["EsCambioTolva"]),
                        ReiniciaTiempoRequerido = rd["ReiniciaTiempoRequerido"] != DBNull.Value && Convert.ToBoolean(rd["ReiniciaTiempoRequerido"]),
                        MotivoCambio = rd["MotivoCambio"] == DBNull.Value ? null : rd["MotivoCambio"]?.ToString()?.Trim(),
                        UsuarioInicioID = rd["UsuarioInicioID"] == DBNull.Value ? null : Convert.ToInt32(rd["UsuarioInicioID"]),
                        UsuarioFinID = rd["UsuarioFinID"] == DBNull.Value ? null : Convert.ToInt32(rd["UsuarioFinID"])
                    });
                }
            }

            return lista;
        }
        private static async Task<DateTime> ObtenerFechaServidorSecadoAsync(SqlConnection cn, SqlTransaction? tx = null)
        {
            await using var cmd = tx == null ? new SqlCommand("SELECT SYSDATETIME();", cn) : new SqlCommand("SELECT SYSDATETIME();", cn, tx);
            return Convert.ToDateTime(await cmd.ExecuteScalarAsync());
        }

        private static async Task AgregarHistorialSecadoAsync(long secadoMaterialId, long? secadoCargaId, string evento, string? estadoAnterior, string? estadoNuevo, int? tolvaAnteriorId, int? tolvaNuevaId, decimal? cantidadKg, string? comentario, int usuarioId, DateTime fechaEvento, SqlConnection cn, SqlTransaction tx)
        {
            const string sql = @"
INSERT dbo.Produccion_SecadoHistorial
(
    SecadoMaterialID,SecadoCargaID,Evento,EstadoAnterior,EstadoNuevo,TolvaAnteriorID,TolvaNuevaID,CantidadKg,Comentario,UsuarioID,FechaEvento
)
VALUES
(
    @SecadoMaterialID,@SecadoCargaID,@Evento,@EstadoAnterior,@EstadoNuevo,@TolvaAnteriorID,@TolvaNuevaID,@CantidadKg,@Comentario,@UsuarioID,@FechaEvento
);";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@SecadoMaterialID", SqlDbType.BigInt).Value = secadoMaterialId;
            cmd.Parameters.Add("@SecadoCargaID", SqlDbType.BigInt).Value = secadoCargaId.HasValue ? secadoCargaId.Value : DBNull.Value;
            cmd.Parameters.Add("@Evento", SqlDbType.NVarChar, 60).Value = evento;
            cmd.Parameters.Add("@EstadoAnterior", SqlDbType.NVarChar, 30).Value = string.IsNullOrWhiteSpace(estadoAnterior) ? DBNull.Value : estadoAnterior;
            cmd.Parameters.Add("@EstadoNuevo", SqlDbType.NVarChar, 30).Value = string.IsNullOrWhiteSpace(estadoNuevo) ? DBNull.Value : estadoNuevo;
            cmd.Parameters.Add("@TolvaAnteriorID", SqlDbType.Int).Value = tolvaAnteriorId.HasValue ? tolvaAnteriorId.Value : DBNull.Value;
            cmd.Parameters.Add("@TolvaNuevaID", SqlDbType.Int).Value = tolvaNuevaId.HasValue ? tolvaNuevaId.Value : DBNull.Value;

            var pCantidad = cmd.Parameters.Add("@CantidadKg", SqlDbType.Decimal);
            pCantidad.Precision = 18;
            pCantidad.Scale = 4;
            pCantidad.Value = cantidadKg.HasValue ? cantidadKg.Value : DBNull.Value;

            cmd.Parameters.Add("@Comentario", SqlDbType.NVarChar, 1000).Value = string.IsNullOrWhiteSpace(comentario) ? DBNull.Value : comentario;
            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
            cmd.Parameters.Add("@FechaEvento", SqlDbType.DateTime2).Value = fechaEvento;
            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task ActualizarPreparacionSecadoInicioAsync(int programaProduccionId, int usuarioId, DateTime ahora, SqlConnection cn, SqlTransaction tx)
        {
            const string sql = @"
UPDATE dbo.Produccion_PreparacionAnticipada
SET
    Estado=@EstadoEnProceso,
    UsuarioInicioID=COALESCE(UsuarioInicioID,@UsuarioID),
    FechaInicioReal=COALESCE(FechaInicioReal,@Ahora),
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=@Ahora
WHERE ProgramaProduccionID=@ProgramaProduccionID
  AND TipoTarea=@TipoTarea
  AND Activo=1
  AND Estado IN(@EstadoPendiente,@EstadoEnProceso);";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@EstadoEnProceso", SqlDbType.NVarChar, 30).Value = ProduccionPreparacionEstado.EnProceso;
            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
            cmd.Parameters.Add("@Ahora", SqlDbType.DateTime2).Value = ahora;
            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaProduccionId;
            cmd.Parameters.Add("@TipoTarea", SqlDbType.NVarChar, 40).Value = ProduccionPreparacionTipo.SecadoMaterial;
            cmd.Parameters.Add("@EstadoPendiente", SqlDbType.NVarChar, 30).Value = ProduccionPreparacionEstado.Pendiente;
            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task ConsolidarSecadosPendientesSinIniciarAsync(int usuarioId, SqlConnection cn, SqlTransaction tx)
        {
            const string sql = @"
;WITH Candidatos AS
(
    SELECT
        sm.SecadoMaterialID,
        sm.ProgramaProduccionID,
        sm.MaterialID,
        ISNULL(LTRIM(RTRIM(sm.TipoMP)),N'') AS TipoMP,
        sm.CantidadRecibidaKg,
        sm.FechaRecepcionProduccion,
        NULLIF(LTRIM(RTRIM(sm.Lote)),N'') AS Lote,
        ROW_NUMBER() OVER
        (
            PARTITION BY sm.ProgramaProduccionID,sm.MaterialID,ISNULL(LTRIM(RTRIM(sm.TipoMP)),N'')
            ORDER BY sm.SecadoMaterialID
        ) AS NumeroFila,
        COUNT(*) OVER
        (
            PARTITION BY sm.ProgramaProduccionID,sm.MaterialID,ISNULL(LTRIM(RTRIM(sm.TipoMP)),N'')
        ) AS TotalGrupo
    FROM dbo.Produccion_SecadoMaterial sm WITH(UPDLOCK,HOLDLOCK)
    WHERE sm.Activo=1
      AND sm.Estado=N'PENDIENTE'
      AND ISNULL(sm.CantidadAsignadaKg,0)<=0.0005
      AND ISNULL(sm.CantidadFinalizadaKg,0)<=0.0005
      AND sm.FechaPrimerInicioSecado IS NULL
      AND NOT EXISTS
      (
          SELECT 1
          FROM dbo.Produccion_SecadoCargas c
          WHERE c.SecadoMaterialID=sm.SecadoMaterialID
      )
),
Grupos AS
(
    SELECT
        ProgramaProduccionID,
        MaterialID,
        TipoMP,
        MIN(CASE WHEN NumeroFila=1 THEN SecadoMaterialID END) AS SecadoMaterialPrincipalID,
        SUM(CantidadRecibidaKg) AS CantidadTotal,
        MIN(FechaRecepcionProduccion) AS PrimeraRecepcion,
        COUNT(DISTINCT ISNULL(Lote,N'')) AS TotalLotes,
        MAX(Lote) AS LoteUnico
    FROM Candidatos
    WHERE TotalGrupo>1
    GROUP BY ProgramaProduccionID,MaterialID,TipoMP
)
UPDATE principal
SET
    principal.CantidadRecibidaKg=g.CantidadTotal,
    principal.FechaRecepcionProduccion=g.PrimeraRecepcion,
    principal.Lote=CASE
        WHEN g.TotalLotes=0 THEN NULL
        WHEN g.TotalLotes=1 THEN g.LoteUnico
        ELSE N'VARIOS'
    END,
    principal.Observaciones=LEFT
    (
        CASE
            WHEN principal.Observaciones IS NULL OR LTRIM(RTRIM(principal.Observaciones))=N''
                THEN N'Recepciones pendientes consolidadas automáticamente antes de iniciar secado.'
            ELSE principal.Observaciones+CHAR(13)+CHAR(10)+N'Recepciones pendientes consolidadas automáticamente antes de iniciar secado.'
        END,
        1000
    ),
    principal.UsuarioModificacionID=@UsuarioID,
    principal.FechaModificacion=@Ahora
FROM dbo.Produccion_SecadoMaterial principal
INNER JOIN Grupos g
    ON g.SecadoMaterialPrincipalID=principal.SecadoMaterialID;

;WITH Candidatos AS
(
    SELECT
        sm.SecadoMaterialID,
        sm.ProgramaProduccionID,
        sm.MaterialID,
        ISNULL(LTRIM(RTRIM(sm.TipoMP)),N'') AS TipoMP,
        ROW_NUMBER() OVER
        (
            PARTITION BY sm.ProgramaProduccionID,sm.MaterialID,ISNULL(LTRIM(RTRIM(sm.TipoMP)),N'')
            ORDER BY sm.SecadoMaterialID
        ) AS NumeroFila,
        COUNT(*) OVER
        (
            PARTITION BY sm.ProgramaProduccionID,sm.MaterialID,ISNULL(LTRIM(RTRIM(sm.TipoMP)),N'')
        ) AS TotalGrupo
    FROM dbo.Produccion_SecadoMaterial sm WITH(UPDLOCK,HOLDLOCK)
    WHERE sm.Activo=1
      AND sm.Estado=N'PENDIENTE'
      AND ISNULL(sm.CantidadAsignadaKg,0)<=0.0005
      AND ISNULL(sm.CantidadFinalizadaKg,0)<=0.0005
      AND sm.FechaPrimerInicioSecado IS NULL
      AND NOT EXISTS
      (
          SELECT 1
          FROM dbo.Produccion_SecadoCargas c
          WHERE c.SecadoMaterialID=sm.SecadoMaterialID
      )
)
UPDATE duplicado
SET
    duplicado.Estado=N'CANCELADO',
    duplicado.Activo=0,
    duplicado.Observaciones=LEFT
    (
        CASE
            WHEN duplicado.Observaciones IS NULL OR LTRIM(RTRIM(duplicado.Observaciones))=N''
                THEN N'Registro consolidado automáticamente en otro lote pendiente de la misma OF y material.'
            ELSE duplicado.Observaciones+CHAR(13)+CHAR(10)+N'Registro consolidado automáticamente en otro lote pendiente de la misma OF y material.'
        END,
        1000
    ),
    duplicado.UsuarioModificacionID=@UsuarioID,
    duplicado.FechaModificacion=@Ahora
FROM dbo.Produccion_SecadoMaterial duplicado
INNER JOIN Candidatos c
    ON c.SecadoMaterialID=duplicado.SecadoMaterialID
WHERE c.TotalGrupo>1
  AND c.NumeroFila>1;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
            cmd.Parameters.Add("@Ahora", SqlDbType.DateTime2).Value = await ObtenerFechaServidorSecadoAsync(cn, tx);
            await cmd.ExecuteNonQueryAsync();
        }
        private static async Task ActualizarPreparacionSecadoFinalAsync(int programaProduccionId, int usuarioId, DateTime ahora, SqlConnection cn, SqlTransaction tx)
        {
            const string sql = @"
DECLARE @CantidadRequerida DECIMAL(18,4)=0;
DECLARE @CantidadSecada DECIMAL(18,4)=0;
DECLARE @HayMaterialSecado BIT=0;
DECLARE @HayPendiente BIT=0;

SELECT TOP(1)
    @CantidadRequerida=ISNULL(d.CantidadMpKg,0)
FROM dbo.Planeacion_ProgramaProduccion pp
INNER JOIN dbo.SolicitudesProduccionDetalle d
    ON d.SolicitudProduccionDetalleID=pp.SolicitudProduccionDetalleID
WHERE pp.ProgramaProduccionID=@ProgramaProduccionID;

SELECT
    @CantidadSecada=ISNULL(SUM(CantidadFinalizadaKg),0),
    @HayMaterialSecado=CASE WHEN COUNT(*)>0 THEN 1 ELSE 0 END
FROM dbo.Produccion_SecadoMaterial
WHERE ProgramaProduccionID=@ProgramaProduccionID
  AND Activo=1
  AND Estado<>N'CANCELADO';

IF EXISTS
(
    SELECT 1
    FROM dbo.Produccion_SecadoMaterial
    WHERE ProgramaProduccionID=@ProgramaProduccionID
      AND Activo=1
      AND Estado NOT IN(N'FINALIZADO',N'CANCELADO')
)
    SET @HayPendiente=1;

IF @HayMaterialSecado=1
   AND @HayPendiente=0
   AND (@CantidadRequerida<=0.0005 OR @CantidadSecada+0.0005>=@CantidadRequerida)
BEGIN
    UPDATE dbo.Produccion_PreparacionAnticipada
    SET
        Estado=@EstadoConfirmada,
        UsuarioConfirmacionID=@UsuarioID,
        FechaConfirmacion=@Ahora,
        FechaFinReal=@Ahora,
        DuracionRealMinutos=CASE WHEN FechaInicioReal IS NULL THEN NULL ELSE DATEDIFF(MINUTE,FechaInicioReal,@Ahora) END,
        ExcedioLimite=CASE WHEN FechaObjetivo IS NOT NULL AND @Ahora>FechaObjetivo THEN 1 ELSE 0 END,
        UsuarioModificacionID=@UsuarioID,
        FechaModificacion=@Ahora
    WHERE ProgramaProduccionID=@ProgramaProduccionID
      AND TipoTarea=@TipoTarea
      AND Activo=1
      AND Estado<>@EstadoCancelada;
END;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaProduccionId;
            cmd.Parameters.Add("@EstadoConfirmada", SqlDbType.NVarChar, 30).Value = ProduccionPreparacionEstado.Confirmada;
            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
            cmd.Parameters.Add("@Ahora", SqlDbType.DateTime2).Value = ahora;
            cmd.Parameters.Add("@TipoTarea", SqlDbType.NVarChar, 40).Value = ProduccionPreparacionTipo.SecadoMaterial;
            cmd.Parameters.Add("@EstadoCancelada", SqlDbType.NVarChar, 30).Value = ProduccionPreparacionEstado.Cancelada;
            await cmd.ExecuteNonQueryAsync();
        }
    }
}