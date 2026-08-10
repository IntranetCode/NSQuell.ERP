using ERP.NSQuell.Models;
using ERP.NSQuell.Models.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace ERP.NSQuell.Controllers
{
    public partial class CalidadController
    {
        private async Task<CalidadDetalleViewModel?> ConstruirDetalleFlujoAsync(int id)
        {
            var inspeccion = await _context.CalidadInspecciones
                .AsNoTracking()
                .Include(x => x.Historial)
                .FirstOrDefaultAsync(x => x.InspeccionID == id);

            if (inspeccion == null)
                return null;

            var model = new CalidadDetalleViewModel
            {
                InspeccionID = inspeccion.InspeccionID,
                ProgramaProduccionID = inspeccion.ProgramaProduccionID,
                EjecucionProduccionID = inspeccion.EjecucionProduccionID,
                ChecklistArranqueID = inspeccion.ChecklistArranqueID,
                SolicitudProduccionID = inspeccion.SolicitudProduccionID,
                SolicitudProduccionDetalleID = inspeccion.SolicitudProduccionDetalleID,
                ReleaseID = inspeccion.ReleaseID,
                ReleaseDetalleID = inspeccion.ReleaseDetalleID,
                ClienteID = inspeccion.ClienteID,
                ParteID = inspeccion.ParteID,
                MaquinaID = inspeccion.MaquinaID,
                MoldeID = inspeccion.MoldeID,
                MaterialID = inspeccion.MaterialID,
                CodigoBarras = inspeccion.CodigoBarras,
                OrdenTrabajo = inspeccion.OrdenTrabajo,
                ClienteNombre = inspeccion.ClienteNombre,
                NumeroParte = inspeccion.NumeroParte,
                Material = inspeccion.Material,
                Proceso = inspeccion.Proceso,
                Maquina = inspeccion.Maquina,
                Molde = inspeccion.Molde,
                OperadorPrincipalPersonaID = inspeccion.OperadorPrincipalPersonaID,
                OperadorPrincipalNombre = inspeccion.OperadorPrincipalNombre,
                OperadorAuxiliarPersonaID = inspeccion.OperadorAuxiliarPersonaID,
                OperadorAuxiliarNombre = inspeccion.OperadorAuxiliarNombre,
                TecnicoInyeccionPersonaID = inspeccion.TecnicoInyeccionPersonaID,
                TecnicoInyeccionNombre = inspeccion.TecnicoInyeccionNombre,
                FechaInicioProgramada = inspeccion.FechaInicioProgramada,
                FechaFinProgramada = inspeccion.FechaFinProgramada,
                CantidadTotal = inspeccion.CantidadTotal,
                CantidadRevisada = inspeccion.CantidadRevisada,
                CantidadPendiente = inspeccion.CantidadPendiente,
                ChecklistValidado = inspeccion.ChecklistValidado,
                HojaInspeccionProducto = inspeccion.HojaInspeccionProducto,
                HojaValidacionCalidad = inspeccion.HojaValidacionCalidad,
                AyudaVisualColocada = inspeccion.AyudaVisualColocada,
                AlertaCalidadAplica = inspeccion.AlertaCalidadAplica,
                AlertaCalidadColocada = inspeccion.AlertaCalidadColocada,
                HIPColocada = inspeccion.HIPColocada,
                HCCColocada = inspeccion.HCCColocada,
                MatrizPolivalenciaValidada = inspeccion.MatrizPolivalenciaValidada,
                FechaNotificacionCalidad = inspeccion.FechaNotificacionCalidad,
                UsuarioNotificoID = inspeccion.UsuarioNotificoID,
                FechaInicioValidacionPrearranque = inspeccion.FechaInicioValidacionPrearranque,
                FechaFinValidacionPrearranque = inspeccion.FechaFinValidacionPrearranque,
                MinutosLiberacionInicial = inspeccion.MinutosLiberacionInicial,
                CumplioTiempoObjetivoInicial = inspeccion.CumplioTiempoObjetivoInicial,
                FechaAutorizacionPrearranque = inspeccion.FechaAutorizacionPrearranque,
                UsuarioAutorizacionPrearranqueID = inspeccion.UsuarioAutorizacionPrearranqueID,
                MotivoDevolucion = inspeccion.MotivoDevolucion,
                CincoDisparosSegregados = inspeccion.CincoDisparosSegregados,
                CantidadDisparosConformes = inspeccion.CantidadDisparosConformes,
                ValidacionDimensional = inspeccion.ValidacionDimensional,
                ValidacionApariencia = inspeccion.ValidacionApariencia,
                ValidacionGauge = inspeccion.ValidacionGauge,
                ValidacionConductividad = inspeccion.ValidacionConductividad,
                FechaValidacionPrimerasPiezas = inspeccion.FechaValidacionPrimerasPiezas,
                UsuarioValidacionPrimerasPiezasID = inspeccion.UsuarioValidacionPrimerasPiezasID,
                ResultadoCalidad = inspeccion.ResultadoCalidad,
                Etiqueta = inspeccion.Etiqueta,
                Liberado = inspeccion.Liberado,
                RequiereGP12 = inspeccion.RequiereGP12,
                EnContencion = inspeccion.EnContencion,
                EsScrap = inspeccion.EsScrap,
                FechaLiberacionProduccion = inspeccion.FechaLiberacionProduccion,
                UsuarioLiberacionProduccionID = inspeccion.UsuarioLiberacionProduccionID,
                RequiereReliberacion = inspeccion.RequiereReliberacion,
                ConfiguracionInvalidada = inspeccion.ConfiguracionInvalidada,
                FechaInvalidacion = inspeccion.FechaInvalidacion,
                UsuarioInvalidacionID = inspeccion.UsuarioInvalidacionID,
                MotivoInvalidacion = inspeccion.MotivoInvalidacion,
                Observaciones = inspeccion.Observaciones,
                Estado = inspeccion.Estado,
                UsuarioCreacionID = inspeccion.UsuarioCreacionID,
                FechaCreacion = inspeccion.FechaCreacion,
                UsuarioModificacionID = inspeccion.UsuarioModificacionID,
                FechaModificacion = inspeccion.FechaModificacion,
                Historial = inspeccion.Historial
                    .OrderByDescending(x => x.FechaMovimiento)
                    .Select(x => new CalidadHistorialItemViewModel
                    {
                        HistorialID = x.HistorialID,
                        Movimiento = x.Movimiento,
                        EstadoAnterior = x.EstadoAnterior,
                        EstadoNuevo = x.EstadoNuevo,
                        ResultadoCalidad = x.ResultadoCalidad,
                        Etiqueta = x.Etiqueta,
                        Comentario = x.Comentario,
                        UsuarioID = x.UsuarioID,
                        FechaMovimiento = x.FechaMovimiento
                    })
                    .ToList()
            };

            model.IntentosPrimerasPiezas = await _context.CalidadPrimerasPiezasIntentos
                .AsNoTracking()
                .Where(x => x.InspeccionID == id && x.Activo)
                .OrderByDescending(x => x.NumeroIntento)
                .Select(x => new CalidadPrimeraPiezaIntentoItemViewModel
                {
                    IntentoID = x.IntentoID,
                    NumeroIntento = x.NumeroIntento,
                    FechaInicio = x.FechaInicio,
                    FechaFin = x.FechaFin,
                    CincoDisparosSegregados = x.CincoDisparosSegregados,
                    CantidadDisparosPresentados = x.CantidadDisparosPresentados,
                    ValidacionDimensional = x.ValidacionDimensional,
                    ValidacionApariencia = x.ValidacionApariencia,
                    ValidacionGauge = x.ValidacionGauge,
                    ValidacionConductividad = x.ValidacionConductividad,
                    Resultado = x.Resultado,
                    AjusteSolicitado = x.AjusteSolicitado,
                    Observaciones = x.Observaciones,
                    UsuarioCalidadID = x.UsuarioCalidadID
                })
                .ToListAsync();

            model.Monitoreos = await CargarMonitoreosDetalleAsync(id);

            model.Disposiciones = await _context.CalidadDisposicionesMaterial
                .AsNoTracking()
                .Where(x => x.InspeccionID == id && x.Activo)
                .OrderByDescending(x => x.FechaInicio)
                .Select(x => new CalidadDisposicionItemViewModel
                {
                    DisposicionID = x.DisposicionID,
                    MonitoreoID = x.MonitoreoID,
                    NumeroHora = x.Monitoreo == null
                        ? (int?)null
                        : x.Monitoreo.NumeroHora,
                    FechaHoraRevision = x.Monitoreo != null
                        ? x.Monitoreo.FechaHoraRevision
                        : null,
                    ResultadoMonitoreoOrigen = x.Monitoreo != null
                        ? x.Monitoreo.Resultado
                        : null,
                    DefectoCodigo = x.Monitoreo != null
                        ? x.Monitoreo.DefectoCodigo
                        : null,
                    DefectoDescripcion = x.Monitoreo != null
                        ? x.Monitoreo.DefectoDescripcion
                        : null,
                    TipoMaterial = x.TipoMaterial,
                    CantidadAfectada = x.CantidadAfectada,
                    Etiqueta = x.Etiqueta,
                    Disposicion = x.Disposicion,
                    Responsable = x.Responsable,
                    FechaInicio = x.FechaInicio,
                    FechaFin = x.FechaFin,
                    CantidadLiberada = x.CantidadLiberada,
                    CantidadScrap = x.CantidadScrap,
                    ResultadoFinal = x.ResultadoFinal,
                    Observaciones = x.Observaciones
                })
                .ToListAsync();

            model.Cajas = await _context.CalidadCajasLiberadas
                .AsNoTracking()
                .Where(x => x.InspeccionID == id && x.Activo)
                .OrderByDescending(x => x.FechaCreacion)
                .Select(x => new CalidadCajaItemViewModel
                {
                    CajaLiberadaID = x.CajaLiberadaID,
                    CajaProduccionID = x.CajaProduccionID,
                    FolioCaja = x.FolioCaja,
                    CantidadPiezas = x.CantidadPiezas,
                    EstandarPackCumple = x.EstandarPackCumple,
                    EtiquetaProductoCorrecta = x.EtiquetaProductoCorrecta,
                    NumeroOperadorEtiqueta = x.NumeroOperadorEtiqueta,
                    TecnicoConfirmoInformacion = x.TecnicoConfirmoInformacion,
                    FechaValidacionCalidad = x.FechaValidacionCalidad,
                    Tarima = x.Tarima,
                    Destino = x.Destino,
                    Estado = x.Estado
                })
                .ToListAsync();

            model.CajasProduccion = await CargarCajasProduccionInspeccionAsync(
                id,
                inspeccion.EjecucionProduccionID);

            model.RegistrosGP12 = await CargarRegistrosGP12Async(id);

            model.Reliberaciones = await CargarReliberacionesDetalleAsync(id);

            model.MuestrasResguardo = await CargarMuestrasResguardoAsync(id);
            model.Cierre = await CargarEstadoCierreAsync(id);

            model.PreguntasChecklistCalidad = await ObtenerPreguntasChecklistCalidadAsync(
                inspeccion.ChecklistArranqueID);

            model.CatalogoDefectos = await _context.CalidadCatalogoDefectos
                .AsNoTracking()
                .Where(x => x.Activo)
                .OrderBy(x => x.Codigo)
                .Select(x => new CalidadCatalogoDefectoItemViewModel
                {
                    CatalogoDefectoID = x.CatalogoDefectoID,
                    Codigo = x.Codigo,
                    Nombre = x.Nombre
                })
                .ToListAsync();

            return model;
        }

        private async Task<List<CalidadReliberacionItemViewModel>>
            CargarReliberacionesDetalleAsync(int inspeccionId)
        {
            var lista = new List<CalidadReliberacionItemViewModel>();

            const string sql = @"
SELECT
    r.ReliberacionID,
    r.ParoID,
    r.NumeroReliberacion,
    r.Motivo,
    r.FechaSolicitud,
    r.FechaValidacion,
    r.Resultado,
    r.Observaciones,
    r.UsuarioSolicitudID,
    r.UsuarioCalidadID,
    p.FechaInicioParo,
    p.FechaFinParo,
    ISNULL(p.DuracionMinutos, 0) AS DuracionMinutos,
    p.MotivoParoTexto,
    p.Descripcion AS DescripcionParo,
    ISNULL(p.EsMayorA15Minutos, 0) AS EsMayorA15Minutos
FROM dbo.Calidad_Reliberaciones r
LEFT JOIN dbo.Produccion_Paros p
    ON p.ParoID = r.ParoID
   AND p.Activo = 1
WHERE r.InspeccionID = @InspeccionID
  AND r.Activo = 1
ORDER BY r.NumeroReliberacion DESC, r.ReliberacionID DESC;";

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@InspeccionID", SqlDbType.Int).Value = inspeccionId;

            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                lista.Add(new CalidadReliberacionItemViewModel
                {
                    ReliberacionID = Convert.ToInt32(rd["ReliberacionID"]),
                    ParoID = Convert.ToInt32(rd["ParoID"]),
                    NumeroReliberacion = Convert.ToInt32(rd["NumeroReliberacion"]),
                    Motivo = rd["Motivo"] as string,
                    FechaSolicitud = Convert.ToDateTime(rd["FechaSolicitud"]),
                    FechaValidacion = rd["FechaValidacion"] == DBNull.Value
                        ? null
                        : Convert.ToDateTime(rd["FechaValidacion"]),
                    Resultado = rd["Resultado"] as string
                        ?? CalidadResultadoReliberacion.Pendiente,
                    Observaciones = rd["Observaciones"] as string,
                    UsuarioSolicitudID = rd["UsuarioSolicitudID"] == DBNull.Value
                        ? null
                        : Convert.ToInt32(rd["UsuarioSolicitudID"]),
                    UsuarioCalidadID = rd["UsuarioCalidadID"] == DBNull.Value
                        ? null
                        : Convert.ToInt32(rd["UsuarioCalidadID"]),
                    FechaInicioParo = rd["FechaInicioParo"] == DBNull.Value
                        ? null
                        : Convert.ToDateTime(rd["FechaInicioParo"]),
                    FechaFinParo = rd["FechaFinParo"] == DBNull.Value
                        ? null
                        : Convert.ToDateTime(rd["FechaFinParo"]),
                    DuracionMinutos = Convert.ToInt32(rd["DuracionMinutos"]),
                    MotivoParoTexto = rd["MotivoParoTexto"] as string,
                    DescripcionParo = rd["DescripcionParo"] as string,
                    EsMayorA15Minutos = Convert.ToBoolean(rd["EsMayorA15Minutos"])
                });
            }

            return lista;
        }

        private async Task<List<CalidadMuestraResguardoItemViewModel>>
            CargarMuestrasResguardoAsync(int inspeccionId)
        {
            var lista = new List<CalidadMuestraResguardoItemViewModel>();

            const string sql = @"
SELECT
    MuestraResguardoID,
    InspeccionID,
    EjecucionProduccionID,
    Momento,
    CantidadDisparos,
    MuestraCalidadConfirmada,
    MuestraProduccionConfirmada,
    UbicacionCalidad,
    UbicacionProduccion,
    FechaResguardo,
    UsuarioResponsableID,
    Observaciones,
    FechaCreacion,
    FechaModificacion
FROM dbo.Calidad_MuestrasResguardo
WHERE InspeccionID = @InspeccionID
  AND Activo = 1
ORDER BY
    CASE WHEN Momento = 'FIN_PRODUCCION' THEN 0 ELSE 1 END,
    ISNULL(FechaModificacion, FechaCreacion) DESC,
    MuestraResguardoID DESC;";

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@InspeccionID", SqlDbType.Int).Value = inspeccionId;

            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                lista.Add(new CalidadMuestraResguardoItemViewModel
                {
                    MuestraResguardoID = Convert.ToInt32(rd["MuestraResguardoID"]),
                    InspeccionID = Convert.ToInt32(rd["InspeccionID"]),
                    EjecucionProduccionID = Convert.ToInt32(rd["EjecucionProduccionID"]),
                    Momento = rd["Momento"] as string
                        ?? CalidadMomentoMuestra.FinProduccion,
                    CantidadDisparos = Convert.ToInt32(rd["CantidadDisparos"]),
                    MuestraCalidadConfirmada = Convert.ToBoolean(rd["MuestraCalidadConfirmada"]),
                    MuestraProduccionConfirmada = Convert.ToBoolean(rd["MuestraProduccionConfirmada"]),
                    UbicacionCalidad = rd["UbicacionCalidad"] as string,
                    UbicacionProduccion = rd["UbicacionProduccion"] as string,
                    FechaResguardo = rd["FechaResguardo"] == DBNull.Value
                        ? null
                        : Convert.ToDateTime(rd["FechaResguardo"]),
                    UsuarioResponsableID = rd["UsuarioResponsableID"] == DBNull.Value
                        ? null
                        : Convert.ToInt32(rd["UsuarioResponsableID"]),
                    Observaciones = rd["Observaciones"] as string,
                    FechaCreacion = Convert.ToDateTime(rd["FechaCreacion"]),
                    FechaModificacion = rd["FechaModificacion"] == DBNull.Value
                        ? null
                        : Convert.ToDateTime(rd["FechaModificacion"])
                });
            }

            return lista;
        }

        private async Task<CalidadCierreEstadoViewModel>
            CargarEstadoCierreAsync(int inspeccionId)
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            return await LeerEstadoCierreAsync(
                inspeccionId,
                cn,
                null);
        }

        private static async Task<CalidadCierreEstadoViewModel> LeerEstadoCierreAsync(int inspeccionId, SqlConnection cn, SqlTransaction? tx)
        {
            if (inspeccionId <= 0) return new CalidadCierreEstadoViewModel();

            const string sql = @"
SELECT TOP (1)
    CAST(CASE WHEN UPPER(LTRIM(RTRIM(ISNULL(i.Estado,N''))))=@EstadoCerrado THEN 1 ELSE 0 END AS BIT) AS YaCerrada,
    CAST(ISNULL(i.ConfiguracionInvalidada,0) AS BIT) AS ConfiguracionInvalidada,
    CAST(CASE WHEN e.EjecucionProduccionID IS NOT NULL THEN 1 ELSE 0 END AS BIT) AS EjecucionProduccionExiste,
    CAST
    (
        CASE
            WHEN e.EjecucionProduccionID IS NOT NULL
             AND e.FechaFinReal IS NOT NULL
             AND ISNULL(e.EstatusID,0) NOT IN (@EnPreparacion,@EnProduccion,@Pausado)
                THEN 1
            ELSE 0
        END AS BIT
    ) AS EjecucionProduccionTerminada,
    CAST(CASE WHEN e.FechaFinReal IS NOT NULL THEN 1 ELSE 0 END AS BIT) AS FechaFinProduccionRegistrada,
    (
        SELECT COUNT(1)
        FROM dbo.Produccion_Paros p
        WHERE p.EjecucionProduccionID=i.EjecucionProduccionID
          AND p.Activo=1
          AND p.FechaFinParo IS NULL
    ) AS ParosAbiertos,
    (
        SELECT COUNT(1)
        FROM dbo.Calidad_MonitoreosProceso m
        WHERE m.InspeccionID=i.InspeccionID
          AND m.Activo=1
          AND UPPER(LTRIM(RTRIM(ISNULL(m.Resultado,N''))))=@MonitoreoPendiente
    ) AS MonitoreosPendientes,
    (
        SELECT COUNT(1)
        FROM dbo.Calidad_DisposicionesMaterial d
        WHERE d.InspeccionID=i.InspeccionID
          AND d.Activo=1
          AND UPPER(LTRIM(RTRIM(ISNULL(d.ResultadoFinal,N''))))=@DisposicionPendiente
    ) AS DisposicionesPendientes,
    (
        SELECT COUNT(1)
        FROM dbo.Produccion_Cajas pc
        WHERE pc.EjecucionProduccionID=i.EjecucionProduccionID
          AND pc.Activo=1
          AND ISNULL(pc.EstadoCajaID,1)=@CajaPendienteCalidad
    ) AS CajasPendientesCalidad,
    (
        SELECT COUNT(1)
        FROM dbo.Produccion_Cajas pc
        WHERE pc.EjecucionProduccionID=i.EjecucionProduccionID
          AND pc.Activo=1
          AND ISNULL(pc.EstadoCajaID,1)=@CajaFormadaProduccion
          AND
          (
              UPPER(LTRIM(RTRIM(ISNULL(pc.EstatusCalidad,N''))))=N'DEVUELTA'
              OR UPPER(LTRIM(RTRIM(ISNULL(pc.ResultadoCalidad,N''))))=N'DEVUELTA'
          )
    ) AS CajasDevueltasSinResolver,
    (
        SELECT COUNT(1)
        FROM dbo.Produccion_Cajas pc
        WHERE pc.EjecucionProduccionID=i.EjecucionProduccionID
          AND pc.Activo=1
          AND ISNULL(pc.EstadoCajaID,@CajaFormadaProduccion)<@CajaSalidaProduccion
    ) AS CajasSinSalidaProduccion,
    (
    SELECT COUNT(1)
    FROM dbo.GP12_Solicitudes g
    WHERE g.CalidadInspeccionID=i.InspeccionID
      AND UPPER(LTRIM(RTRIM(ISNULL(g.Origen,N''))))=N'CALIDAD'
      AND g.Activo=1
      AND g.EstatusID NOT IN (@GP12Cerrado,@GP12Cancelado)
) AS GP12Abiertos,
    (
        SELECT COUNT(1)
        FROM dbo.Calidad_Reliberaciones r
        WHERE r.InspeccionID=i.InspeccionID
          AND r.Activo=1
          AND UPPER(LTRIM(RTRIM(ISNULL(r.Resultado,N''))))=@ReliberacionPendiente
    ) AS ReliberacionesPendientes,
    CAST
    (
        CASE WHEN EXISTS
        (
            SELECT 1
            FROM
            (
                SELECT TOP (1)
                    mr.CantidadDisparos,
                    mr.MuestraCalidadConfirmada,
                    mr.MuestraProduccionConfirmada,
                    mr.UbicacionCalidad,
                    mr.UbicacionProduccion,
                    mr.FechaResguardo
                FROM dbo.Calidad_MuestrasResguardo mr
                WHERE mr.InspeccionID=i.InspeccionID
                  AND mr.Activo=1
                  AND UPPER(LTRIM(RTRIM(ISNULL(mr.Momento,N''))))=@MomentoFinProduccion
                ORDER BY mr.MuestraResguardoID DESC
            ) muestraFinal
            WHERE ISNULL(muestraFinal.CantidadDisparos,0)>0
              AND ISNULL(muestraFinal.MuestraCalidadConfirmada,0)=1
              AND ISNULL(muestraFinal.MuestraProduccionConfirmada,0)=1
              AND NULLIF(LTRIM(RTRIM(muestraFinal.UbicacionCalidad)),N'') IS NOT NULL
              AND NULLIF(LTRIM(RTRIM(muestraFinal.UbicacionProduccion)),N'') IS NOT NULL
              AND muestraFinal.FechaResguardo IS NOT NULL
        ) THEN 1 ELSE 0 END AS BIT
    ) AS MuestraFinProduccionCompleta
FROM dbo.Calidad_Inspecciones i WITH (UPDLOCK,HOLDLOCK)
LEFT JOIN dbo.Produccion_Ejecucion e WITH (UPDLOCK,HOLDLOCK)
    ON e.EjecucionProduccionID=i.EjecucionProduccionID
   AND e.Activo=1
WHERE i.InspeccionID=@InspeccionID;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@InspeccionID", SqlDbType.Int).Value = inspeccionId;
            cmd.Parameters.Add("@EstadoCerrado", SqlDbType.NVarChar, 50).Value = CalidadEstados.Cerrada;
            cmd.Parameters.Add("@MonitoreoPendiente", SqlDbType.NVarChar, 20).Value = CalidadResultadoMonitoreo.Pendiente;
            cmd.Parameters.Add("@DisposicionPendiente", SqlDbType.NVarChar, 20).Value = CalidadResultadoDisposicion.Pendiente;
            cmd.Parameters.Add("@GP12Cerrado", SqlDbType.Int).Value = GP12Estatus.Cerrado;
            cmd.Parameters.Add("@GP12Cancelado", SqlDbType.Int).Value = GP12Estatus.Cancelado;
            cmd.Parameters.Add("@ReliberacionPendiente", SqlDbType.NVarChar, 20).Value = CalidadResultadoReliberacion.Pendiente;
            cmd.Parameters.Add("@MomentoFinProduccion", SqlDbType.NVarChar, 30).Value = CalidadMomentoMuestra.FinProduccion;
            cmd.Parameters.Add("@EnPreparacion", SqlDbType.Int).Value = ProduccionEstatus.EnPreparacion;
            cmd.Parameters.Add("@EnProduccion", SqlDbType.Int).Value = ProduccionEstatus.EnProduccion;
            cmd.Parameters.Add("@Pausado", SqlDbType.Int).Value = ProduccionEstatus.Pausado;
            cmd.Parameters.Add("@CajaFormadaProduccion", SqlDbType.Int).Value = ProduccionCajaEstatus.FormadaProduccion;
            cmd.Parameters.Add("@CajaPendienteCalidad", SqlDbType.Int).Value = ProduccionCajaEstatus.PendienteCalidad;
            cmd.Parameters.Add("@CajaSalidaProduccion", SqlDbType.Int).Value = ProduccionCajaEstatus.SalidaProduccion;

            await using var rd = await cmd.ExecuteReaderAsync();
            if (!await rd.ReadAsync()) return new CalidadCierreEstadoViewModel();

            return new CalidadCierreEstadoViewModel
            {
                YaCerrada = Convert.ToBoolean(rd["YaCerrada"]),
                ConfiguracionInvalidada = Convert.ToBoolean(rd["ConfiguracionInvalidada"]),
                EjecucionProduccionExiste = Convert.ToBoolean(rd["EjecucionProduccionExiste"]),
                EjecucionProduccionTerminada = Convert.ToBoolean(rd["EjecucionProduccionTerminada"]),
                FechaFinProduccionRegistrada = Convert.ToBoolean(rd["FechaFinProduccionRegistrada"]),
                ParosAbiertos = Convert.ToInt32(rd["ParosAbiertos"]),
                MonitoreosPendientes = Convert.ToInt32(rd["MonitoreosPendientes"]),
                DisposicionesPendientes = Convert.ToInt32(rd["DisposicionesPendientes"]),
                CajasPendientesCalidad = Convert.ToInt32(rd["CajasPendientesCalidad"]),
                CajasDevueltasSinResolver = Convert.ToInt32(rd["CajasDevueltasSinResolver"]),
                CajasSinSalidaProduccion = Convert.ToInt32(rd["CajasSinSalidaProduccion"]),
                GP12Abiertos = Convert.ToInt32(rd["GP12Abiertos"]),
                ReliberacionesPendientes = Convert.ToInt32(rd["ReliberacionesPendientes"]),
                MuestraFinProduccionCompleta = Convert.ToBoolean(rd["MuestraFinProduccionCompleta"])
            };
        }
        private async Task<List<CalidadChecklistPreguntaViewModel>> ObtenerPreguntasChecklistCalidadAsync(
            int? checklistArranqueId)
        {
            var lista = new List<CalidadChecklistPreguntaViewModel>();
            if (!checklistArranqueId.HasValue) return lista;

            const string sql = @"
SELECT
    d.ChecklistArranqueDetalleID,
    d.PreguntaID,
    p.Seccion,
    p.OrdenSeccion,
    p.OrdenPregunta,
    p.TextoPregunta,
    p.RequiereObservacionSiNOK,
    d.Resultado,
    d.Observaciones
FROM dbo.Produccion_ChecklistArranqueDetalle d
INNER JOIN dbo.ERP_ChecklistArranquePreguntas p
    ON p.PreguntaID = d.PreguntaID
WHERE d.ChecklistArranqueID = @ChecklistArranqueID
  AND d.Activo = 1
  AND p.Activo = 1
  AND
  (
      UPPER(ISNULL(p.Seccion, '')) LIKE '%CALIDAD%'
      OR UPPER(ISNULL(p.ResponsableSugerido, '')) LIKE '%CALIDAD%'
  )
ORDER BY p.OrdenSeccion, p.OrdenPregunta;";

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@ChecklistArranqueID", SqlDbType.Int).Value = checklistArranqueId.Value;
            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                lista.Add(new CalidadChecklistPreguntaViewModel
                {
                    ChecklistArranqueDetalleID = Convert.ToInt32(rd["ChecklistArranqueDetalleID"]),
                    PreguntaID = Convert.ToInt32(rd["PreguntaID"]),
                    Seccion = rd["Seccion"] as string ?? string.Empty,
                    OrdenSeccion = Convert.ToInt32(rd["OrdenSeccion"]),
                    OrdenPregunta = Convert.ToInt32(rd["OrdenPregunta"]),
                    TextoPregunta = rd["TextoPregunta"] as string ?? string.Empty,
                    RequiereObservacionSiNOK = rd["RequiereObservacionSiNOK"] != DBNull.Value && Convert.ToBoolean(rd["RequiereObservacionSiNOK"]),
                    Resultado = rd["Resultado"] as string,
                    Observaciones = rd["Observaciones"] as string
                });
            }

            return lista;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GuardarChecklistCalidad(
            CalidadChecklistGuardarViewModel model)
        {
            if (!ModelState.IsValid)
            {
                TempData["Error"] = "No se recibio correctamente el checklist de Calidad.";
                return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
            }

            var usuarioId = ObtenerUsuarioIdActual();
            if (!usuarioId.HasValue)
                return Unauthorized();

            var inspeccion = await _context.CalidadInspecciones
                .FirstOrDefaultAsync(x =>
                    x.InspeccionID == model.InspeccionID &&
                    x.ChecklistArranqueID == model.ChecklistArranqueID);

            if (inspeccion == null)
                return NotFound();

            if (!CalidadEstados.PuedeAutorizarPrearranque(inspeccion.Estado))
            {
                TempData["Error"] = "El checklist ya no se encuentra disponible para revision de prearranque.";
                return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
            }

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();

            try
            {
                foreach (var respuesta in model.Respuestas ?? new List<CalidadChecklistRespuestaViewModel>())
                {
                    var resultado = NormalizarResultadoCalidad(respuesta.Resultado);
                    if (resultado == "__INVALIDO__")
                        throw new InvalidOperationException("Se recibio una respuesta invalida en el checklist de Calidad.");

                    if (resultado == "NOK" && string.IsNullOrWhiteSpace(respuesta.Observaciones))
                        throw new InvalidOperationException("Toda respuesta NOK del auditor de Calidad requiere observacion.");

                    const string sqlUpdate = @"
UPDATE d
SET
    d.Resultado = @Resultado,
    d.Observaciones = @Observaciones,
    d.UsuarioRespuestaID = @UsuarioID,
    d.FechaRespuesta = GETDATE(),
    d.UsuarioModificacionID = @UsuarioID,
    d.FechaModificacion = GETDATE()
FROM dbo.Produccion_ChecklistArranqueDetalle d
INNER JOIN dbo.ERP_ChecklistArranquePreguntas p
    ON p.PreguntaID = d.PreguntaID
WHERE d.ChecklistArranqueDetalleID = @DetalleID
  AND d.ChecklistArranqueID = @ChecklistArranqueID
  AND d.Activo = 1
  AND p.Activo = 1
  AND
  (
      UPPER(ISNULL(p.Seccion, '')) LIKE '%CALIDAD%'
      OR UPPER(ISNULL(p.ResponsableSugerido, '')) LIKE '%CALIDAD%'
  );";

                    await using var cmd = new SqlCommand(sqlUpdate, cn, tx);
                    cmd.Parameters.Add("@Resultado", SqlDbType.NVarChar, 10).Value = (object?)resultado ?? DBNull.Value;
                    cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 500).Value =
                        string.IsNullOrWhiteSpace(respuesta.Observaciones)
                            ? DBNull.Value
                            : respuesta.Observaciones.Trim();
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId.Value;
                    cmd.Parameters.Add("@DetalleID", SqlDbType.Int).Value = respuesta.ChecklistArranqueDetalleID;
                    cmd.Parameters.Add("@ChecklistArranqueID", SqlDbType.Int).Value = model.ChecklistArranqueID;
                    await cmd.ExecuteNonQueryAsync();
                }

                const string sqlHeader = @"
UPDATE dbo.Produccion_ChecklistArranque
SET
    UsuarioCalidadID = @UsuarioID,
    FechaValidacionCalidad = GETDATE(),
    ObservacionesCalidad = @ObservacionesCalidad,
    UsuarioModificacionID = @UsuarioID,
    FechaModificacion = GETDATE()
WHERE ChecklistArranqueID = @ChecklistArranqueID
  AND Activo = 1;";

                await using (var cmd = new SqlCommand(sqlHeader, cn, tx))
                {
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId.Value;
                    cmd.Parameters.Add("@ObservacionesCalidad", SqlDbType.NVarChar, 1000).Value =
                        string.IsNullOrWhiteSpace(model.ObservacionesCalidad)
                            ? DBNull.Value
                            : model.ObservacionesCalidad.Trim();
                    cmd.Parameters.Add("@ChecklistArranqueID", SqlDbType.Int).Value = model.ChecklistArranqueID;
                    await cmd.ExecuteNonQueryAsync();
                }

                await tx.CommitAsync();

                inspeccion.FechaInicioValidacionPrearranque ??= DateTime.Now;
                MarcarModificacion(inspeccion, usuarioId);
                AgregarHistorial(
                    inspeccion,
                    CalidadMovimientos.ChecklistCalidadCapturado,
                    inspeccion.Estado,
                    inspeccion.Estado,
                    inspeccion.ResultadoCalidad,
                    inspeccion.Etiqueta,
                    "El auditor guardo su seccion del checklist de arranque.",
                    usuarioId);
                await _context.SaveChangesAsync();

                TempData["Mensaje"] = "Seccion de Calidad guardada correctamente.";
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                TempData["Error"] = "No fue posible guardar el checklist de Calidad: " + ex.Message;
            }

            return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AutorizarPrearranqueFlujo(
            CalidadPrearranqueViewModel model)
        {
            if (!ModelState.IsValid)
            {
                TempData["Error"] = "No se recibio una inspeccion valida.";
                return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
            }

            if (!model.AyudaVisualColocada ||
                !model.HIPColocada ||
                !model.HCCColocada ||
                !model.MatrizPolivalenciaValidada)
            {
                TempData["Error"] = "Confirma ayuda visual, HIP, HCC y matriz de polivalencia antes de autorizar.";
                return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
            }

            if (model.AlertaCalidadAplica == true && model.AlertaCalidadColocada != true)
            {
                TempData["Error"] = "La alerta de Calidad aplica y debe confirmarse como colocada.";
                return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
            }

            var inspeccion = await _context.CalidadInspecciones
                .FirstOrDefaultAsync(x => x.InspeccionID == model.InspeccionID);

            if (inspeccion == null)
                return NotFound();

            if (!CalidadEstados.PuedeAutorizarPrearranque(inspeccion.Estado))
            {
                TempData["Error"] = "La inspeccion ya no esta pendiente de prearranque.";
                return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
            }

            if (!inspeccion.ChecklistArranqueID.HasValue)
            {
                TempData["Error"] = "La inspeccion no tiene un checklist de Produccion relacionado.";
                return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
            }

            var validacionConfiguracion = await ValidarConfiguracionActualAsync(inspeccion);
            if (!validacionConfiguracion.Valida)
            {
                await InvalidarConfiguracionAsync(inspeccion, validacionConfiguracion.Motivo);
                TempData["Error"] = validacionConfiguracion.Motivo;
                return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
            }

            var validacionChecklist = await ValidarChecklistCalidadCompletoAsync(inspeccion.ChecklistArranqueID.Value);
            if (!validacionChecklist.Valido)
            {
                TempData["Error"] = validacionChecklist.Mensaje;
                return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
            }

            var usuarioId = ObtenerUsuarioIdActual();
            if (!usuarioId.HasValue) return Unauthorized();

            await using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                var estadoAnterior = inspeccion.Estado;
                var ahora = DateTime.Now;

                inspeccion.AyudaVisualColocada = model.AyudaVisualColocada;
                inspeccion.AlertaCalidadAplica = model.AlertaCalidadAplica;
                inspeccion.AlertaCalidadColocada = model.AlertaCalidadColocada;
                inspeccion.HIPColocada = model.HIPColocada;
                inspeccion.HCCColocada = model.HCCColocada;
                inspeccion.MatrizPolivalenciaValidada = model.MatrizPolivalenciaValidada;
                inspeccion.ChecklistValidado = true;
                inspeccion.HojaInspeccionProducto = true;
                inspeccion.HojaValidacionCalidad = true;
                inspeccion.FechaInicioValidacionPrearranque ??= ahora;
                inspeccion.FechaFinValidacionPrearranque = ahora;
                inspeccion.FechaAutorizacionPrearranque = ahora;
                inspeccion.UsuarioAutorizacionPrearranqueID = usuarioId;
                inspeccion.MotivoDevolucion = null;
                inspeccion.Estado = CalidadEstados.ArranqueAutorizado;
                MarcarModificacion(inspeccion, usuarioId);

                AgregarHistorial(
                    inspeccion,
                    CalidadMovimientos.PrearranqueAutorizado,
                    estadoAnterior,
                    inspeccion.Estado,
                    inspeccion.ResultadoCalidad,
                    inspeccion.Etiqueta,
                    string.IsNullOrWhiteSpace(model.Motivo)
                        ? "Calidad autorizo el arranque controlado."
                        : model.Motivo.Trim(),
                    usuarioId);

                await _context.SaveChangesAsync();

                await _context.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE dbo.Produccion_ChecklistArranque
SET
    EstatusID = {ProduccionChecklistEstatus.ValidadoPorCalidad},
    UsuarioCalidadID = {usuarioId.Value},
    FechaValidacionCalidad = {ahora},
    ObservacionesCalidad = {model.Motivo},
    UsuarioModificacionID = {usuarioId.Value},
    FechaModificacion = {ahora}
WHERE ChecklistArranqueID = {inspeccion.ChecklistArranqueID.Value}
  AND Activo = 1;");

                await tx.CommitAsync();
                TempData["Mensaje"] = "Prearranque autorizado. Produccion puede generar las primeras piezas, pero aun no iniciar la serie.";
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                TempData["Error"] = "No fue posible autorizar el prearranque: " + ex.Message;
            }

            return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DevolverPrearranqueFlujo(
            CalidadPrearranqueViewModel model)
        {
            model.Motivo = model.Motivo?.Trim();
            if (string.IsNullOrWhiteSpace(model.Motivo))
            {
                TempData["Error"] = "Captura el motivo de la devolucion.";
                return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
            }

            var inspeccion = await _context.CalidadInspecciones
                .FirstOrDefaultAsync(x => x.InspeccionID == model.InspeccionID);

            if (inspeccion == null) return NotFound();
            if (!CalidadEstados.PuedeAutorizarPrearranque(inspeccion.Estado))
            {
                TempData["Error"] = "La inspeccion ya no esta pendiente de prearranque.";
                return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
            }

            var usuarioId = ObtenerUsuarioIdActual();
            if (!usuarioId.HasValue) return Unauthorized();

            await using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                var estadoAnterior = inspeccion.Estado;
                inspeccion.Estado = CalidadEstados.DevueltoPrearranque;
                inspeccion.MotivoDevolucion = model.Motivo;
                inspeccion.ChecklistValidado = false;
                inspeccion.Liberado = false;
                MarcarModificacion(inspeccion, usuarioId);

                AgregarHistorial(
                    inspeccion,
                    CalidadMovimientos.PrearranqueDevuelto,
                    estadoAnterior,
                    inspeccion.Estado,
                    inspeccion.ResultadoCalidad,
                    inspeccion.Etiqueta,
                    model.Motivo,
                    usuarioId);

                await _context.SaveChangesAsync();

                if (inspeccion.ChecklistArranqueID.HasValue)
                {
                    await _context.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE dbo.Produccion_ChecklistArranque
SET
    EstatusID = {ProduccionChecklistEstatus.RechazadoRequiereAjuste},
    UsuarioCalidadID = {usuarioId.Value},
    FechaValidacionCalidad = {DateTime.Now},
    ObservacionesCalidad = {model.Motivo},
    UsuarioModificacionID = {usuarioId.Value},
    FechaModificacion = {DateTime.Now}
WHERE ChecklistArranqueID = {inspeccion.ChecklistArranqueID.Value}
  AND Activo = 1;");
                }

                await tx.CommitAsync();
                TempData["Mensaje"] = "La revision fue devuelta a Produccion para correccion.";
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                TempData["Error"] = "No fue posible devolver la revision: " + ex.Message;
            }

            return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RegistrarPrimerasPiezasFlujo(
     CalidadPrimerasPiezasViewModel model)
        {
            if (!ModelState.IsValid ||
                !model.CincoDisparosSegregados)
            {
                TempData["Error"] =
                    "Confirma la segregación de los primeros cinco disparos y revisa los datos.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id = model.InspeccionID });
            }

            var inspeccion =
                await _context.CalidadInspecciones
                    .FirstOrDefaultAsync(x =>
                        x.InspeccionID == model.InspeccionID);

            if (inspeccion == null)
                return NotFound();

            /*
             * Se conserva el estado actual antes de modificar
             * cualquier información de la inspección.
             */
            var estadoActual =
                (inspeccion.Estado ?? string.Empty)
                    .Trim()
                    .ToUpperInvariant();

            /*
             * El método general PuedeValidarPrimerasPiezas
             * contempla el flujo normal.
             *
             * PENDIENTE_RELIBERACION también debe permitir
             * registrar nuevas primeras piezas después de un
             * paro que requiere una nueva autorización de Calidad.
             */
            var puedeRegistrarPrimerasPiezas =
                CalidadEstados.PuedeValidarPrimerasPiezas(
                    inspeccion.Estado) ||
                estadoActual ==
                    CalidadEstados.PendienteReliberacion;

            if (!puedeRegistrarPrimerasPiezas)
            {
                TempData["Error"] =
                    "La inspección no permite registrar primeras piezas en su estado actual: " +
                    (string.IsNullOrWhiteSpace(inspeccion.Estado)
                        ? "SIN ESTADO"
                        : inspeccion.Estado) +
                    ".";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id = model.InspeccionID });
            }

            if (inspeccion.ConfiguracionInvalidada)
            {
                TempData["Error"] =
                    "La configuración de la inspección fue invalidada y no permite registrar primeras piezas.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id = model.InspeccionID });
            }

            var usuarioId =
                ObtenerUsuarioIdActual();

            if (!usuarioId.HasValue ||
                usuarioId.Value <= 0)
            {
                return Unauthorized();
            }

            /*
             * Identificamos la reliberación ANTES de cambiar
             * el estado de la inspección.
             */
            var esReliberacion =
                inspeccion.RequiereReliberacion ||
                estadoActual ==
                    CalidadEstados.PendienteReliberacion ||
                CalidadTipoProceso.EsReliberacion(
                    inspeccion.Proceso);

            /*
             * Obtiene el intento pendiente actual o crea
             * uno nuevo si ya no existe uno pendiente.
             */
            var intento =
                await ObtenerOCrearIntentoPendienteAsync(
                    inspeccion.InspeccionID,
                    usuarioId.Value);

            AplicarDatosIntento(
                intento,
                model,
                usuarioId.Value);

            var reliberacionReactivada = false;

            /*
             * Si estamos trabajando una reliberación,
             * verificamos que exista su registro asociado.
             */
            if (esReliberacion)
            {
                var reliberacion =
                    await _context.CalidadReliberaciones
                        .Where(x =>
                            x.InspeccionID ==
                                inspeccion.InspeccionID &&
                            x.Activo)
                        .OrderByDescending(x =>
                            x.NumeroReliberacion)
                        .FirstOrDefaultAsync();

                if (reliberacion == null)
                {
                    TempData["Error"] =
                        "No se encontró la solicitud de reliberación asociada a esta inspección.";

                    return RedirectToAction(
                        nameof(Detalle),
                        new { id = model.InspeccionID });
                }

                /*
                 * Si la reliberación anterior había sido
                 * rechazada y Producción presenta nuevas piezas,
                 * vuelve a quedar pendiente para una nueva
                 * decisión de Calidad.
                 */
                if (reliberacion.Resultado ==
                    CalidadResultadoReliberacion.Rechazada)
                {
                    reliberacion.Resultado =
                        CalidadResultadoReliberacion.Pendiente;

                    reliberacion.FechaValidacion =
                        null;

                    reliberacion.UsuarioCalidadID =
                        null;

                    reliberacion.Observaciones =
                        UnirObservaciones(
                            reliberacion.Observaciones,
                            $"Se inició una nueva validación con el intento {intento.NumeroIntento} de primeras piezas.");

                    reliberacion.UsuarioModificacionID =
                        usuarioId.Value;

                    reliberacion.FechaModificacion =
                        DateTime.Now;

                    reliberacionReactivada =
                        true;
                }
            }

            /*
             * Guarda el resumen de la nueva validación
             * también en Calidad_Inspecciones.
             */
            AplicarResumenPrimerasPiezas(
                inspeccion,
                model,
                usuarioId.Value);

            var estadoAnterior =
                inspeccion.Estado;

            /*
             * IMPORTANTE:
             *
             * En el flujo normal:
             * ARRANQUE_AUTORIZADO
             *      ->
             * PENDIENTE_PRIMERAS_PIEZAS
             *
             * En una reliberación:
             * PENDIENTE_RELIBERACION
             *      ->
             * PENDIENTE_RELIBERACION
             *
             * No debemos perder el estado de reliberación
             * hasta que Calidad autorice o rechace.
             */
            inspeccion.Estado =
                esReliberacion
                    ? CalidadEstados.PendienteReliberacion
                    : CalidadEstados.PendientePrimerasPiezas;

            /*
             * Registrar primeras piezas NO significa todavía
             * que Producción haya sido liberada.
             */
            inspeccion.Liberado =
                false;

            if (esReliberacion)
            {
                inspeccion.RequiereReliberacion =
                    true;
            }

            MarcarModificacion(
                inspeccion,
                usuarioId.Value);

            AgregarHistorial(
                inspeccion,
                CalidadMovimientos.PrimerasPiezasRecibidas,
                estadoAnterior,
                inspeccion.Estado,
                inspeccion.ResultadoCalidad,
                inspeccion.Etiqueta,
                string.IsNullOrWhiteSpace(
                    model.Observaciones)
                    ? esReliberacion
                        ? $"Se registró el intento {intento.NumeroIntento} de primeras piezas para reliberación."
                        : $"Se registró el intento {intento.NumeroIntento} de primeras piezas."
                    : model.Observaciones.Trim(),
                usuarioId.Value);

            await _context.SaveChangesAsync();

            if (esReliberacion)
            {
                TempData["Mensaje"] =
                    reliberacionReactivada
                        ? $"Intento {intento.NumeroIntento} guardado. La reliberación volvió a quedar pendiente de decisión de Calidad."
                        : $"Intento {intento.NumeroIntento} de primeras piezas guardado. Ya puedes autorizar o rechazar la reliberación.";
            }
            else
            {
                TempData["Mensaje"] =
                    $"Intento {intento.NumeroIntento} de primeras piezas guardado.";
            }

            return RedirectToAction(
                nameof(Detalle),
                new { id = model.InspeccionID });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SolicitarAjustesFlujo(
            CalidadPrimerasPiezasViewModel model)
        {
            model.Observaciones = model.Observaciones?.Trim();

            if (string.IsNullOrWhiteSpace(model.Observaciones))
            {
                TempData["Error"] = "Describe los ajustes requeridos.";
                return RedirectToAction(
                    nameof(Detalle),
                    new { id = model.InspeccionID });
            }

            var inspeccion = await _context.CalidadInspecciones
                .FirstOrDefaultAsync(x => x.InspeccionID == model.InspeccionID);

            if (inspeccion == null)
                return NotFound();

            if (!CalidadEstados.PuedeValidarPrimerasPiezas(inspeccion.Estado))
            {
                TempData["Error"] =
                    "La inspección no permite solicitar ajustes en su estado actual.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id = model.InspeccionID });
            }

            var usuarioId = ObtenerUsuarioIdActual();
            if (!usuarioId.HasValue || usuarioId.Value <= 0)
                return Unauthorized();

            var ahora = DateTime.Now;
            var intento = await ObtenerOCrearIntentoPendienteAsync(
                inspeccion.InspeccionID,
                usuarioId.Value);

            AplicarDatosIntento(intento, model, usuarioId.Value);
            intento.Resultado = CalidadResultadoIntento.Nok;
            intento.AjusteSolicitado = true;
            intento.FechaFin = ahora;

            AplicarResumenPrimerasPiezas(inspeccion, model, usuarioId.Value);

            var esReliberacion =
                inspeccion.RequiereReliberacion ||
                CalidadTipoProceso.EsReliberacion(inspeccion.Proceso);

            CalidadReliberacion? reliberacionRechazada = null;

            if (esReliberacion)
            {
                reliberacionRechazada = await _context.CalidadReliberaciones
                    .Where(x =>
                        x.InspeccionID == inspeccion.InspeccionID &&
                        x.Activo)
                    .OrderByDescending(x => x.NumeroReliberacion)
                    .FirstOrDefaultAsync();

                if (reliberacionRechazada == null)
                {
                    TempData["Error"] =
                        "No se encontró la solicitud de reliberación asociada al paro.";

                    return RedirectToAction(
                        nameof(Detalle),
                        new { id = model.InspeccionID });
                }

                reliberacionRechazada.Resultado =
                    CalidadResultadoReliberacion.Rechazada;
                reliberacionRechazada.FechaValidacion = ahora;
                reliberacionRechazada.UsuarioCalidadID = usuarioId;
                reliberacionRechazada.Observaciones = UnirObservaciones(
                    reliberacionRechazada.Observaciones,
                    $"Intento {intento.NumeroIntento} rechazado. {model.Observaciones}");
                reliberacionRechazada.UsuarioModificacionID = usuarioId;
                reliberacionRechazada.FechaModificacion = ahora;
            }

            var estadoAnterior = inspeccion.Estado;
            inspeccion.ResultadoCalidad = "NOK";
            inspeccion.Etiqueta = null;
            inspeccion.Liberado = false;
            inspeccion.Estado = CalidadEstados.AjustesSolicitados;
            inspeccion.Observaciones = model.Observaciones;
            MarcarModificacion(inspeccion, usuarioId);

            AgregarHistorial(
                inspeccion,
                esReliberacion
                    ? CalidadMovimientos.ReliberacionRechazada
                    : CalidadMovimientos.AjustesSolicitados,
                estadoAnterior,
                inspeccion.Estado,
                inspeccion.ResultadoCalidad,
                inspeccion.Etiqueta,
                esReliberacion
                    ? $"Reliberación {reliberacionRechazada?.NumeroReliberacion} rechazada. Intento {intento.NumeroIntento} NOK. {model.Observaciones}"
                    : $"Intento {intento.NumeroIntento} NOK. {model.Observaciones}",
                usuarioId);

            await _context.SaveChangesAsync();

            TempData["Mensaje"] = esReliberacion
                ? "Reliberación rechazada. Producción debe corregir y presentar nuevas primeras piezas."
                : "Ajustes solicitados a Producción. El intento quedó registrado como NOK.";

            return RedirectToAction(
                nameof(Detalle),
                new { id = model.InspeccionID });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LiberarProduccionFlujo(int id)
        {
            if (id <= 0) return NotFound();

            var usuarioId = ObtenerUsuarioIdActual();
            if (!usuarioId.HasValue || usuarioId.Value <= 0) return Unauthorized();

            await using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                var inspeccion = await _context.CalidadInspecciones
                    .FirstOrDefaultAsync(x => x.InspeccionID == id);

                if (inspeccion == null)
                {
                    await tx.RollbackAsync();
                    return NotFound();
                }

                if (inspeccion.ConfiguracionInvalidada)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "La configuración fue invalidada y no puede liberarse.";
                    return RedirectToAction(nameof(Detalle), new { id });
                }

                var validacionConfiguracion = await ValidarConfiguracionActualAsync(inspeccion);
                if (!validacionConfiguracion.Valida)
                {
                    await InvalidarConfiguracionAsync(inspeccion, validacionConfiguracion.Motivo);
                    await tx.CommitAsync();
                    TempData["Error"] = validacionConfiguracion.Motivo;
                    return RedirectToAction(nameof(Detalle), new { id });
                }

                var intento = await _context.CalidadPrimerasPiezasIntentos
                    .Where(x => x.InspeccionID == id && x.Activo)
                    .OrderByDescending(x => x.NumeroIntento)
                    .FirstOrDefaultAsync();

                if (intento == null)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "Primero registra la validación de las primeras piezas.";
                    return RedirectToAction(nameof(Detalle), new { id });
                }

                if (!intento.CincoDisparosSegregados ||
                    intento.CantidadDisparosPresentados < 3 ||
                    intento.ValidacionDimensional != true ||
                    intento.ValidacionApariencia != true ||
                    intento.ValidacionGauge == false ||
                    intento.ValidacionConductividad == false)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "El último intento no cumple los requisitos para liberar la producción.";
                    return RedirectToAction(nameof(Detalle), new { id });
                }

                var eraReliberacion = inspeccion.RequiereReliberacion ||
                                      CalidadTipoProceso.EsReliberacion(inspeccion.Proceso);

                CalidadReliberacion? reliberacionPendiente = null;
                if (eraReliberacion)
                {
                    reliberacionPendiente = await _context.CalidadReliberaciones
                        .Where(x =>
                            x.InspeccionID == id &&
                            x.Activo &&
                            x.Resultado == CalidadResultadoReliberacion.Pendiente)
                        .OrderByDescending(x => x.NumeroReliberacion)
                        .FirstOrDefaultAsync();

                    if (reliberacionPendiente == null)
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] = "La reliberación no está pendiente. Guarda una nueva validación después de corregir las primeras piezas.";
                        return RedirectToAction(nameof(Detalle), new { id });
                    }
                }

                var ahora = DateTime.Now;
                var estadoAnterior = inspeccion.Estado;

                intento.Resultado = CalidadResultadoIntento.Ok;
                intento.AjusteSolicitado = false;
                intento.FechaFin = ahora;
                intento.UsuarioModificacionID = usuarioId.Value;
                intento.FechaModificacion = ahora;

                inspeccion.CincoDisparosSegregados = intento.CincoDisparosSegregados;
                inspeccion.CantidadDisparosConformes = intento.CantidadDisparosPresentados;
                inspeccion.ValidacionDimensional = intento.ValidacionDimensional;
                inspeccion.ValidacionApariencia = intento.ValidacionApariencia;
                inspeccion.ValidacionGauge = intento.ValidacionGauge;
                inspeccion.ValidacionConductividad = intento.ValidacionConductividad;
                inspeccion.ResultadoCalidad = "VERDE";
                inspeccion.Etiqueta = "VERDE";
                inspeccion.Liberado = true;
                inspeccion.RequiereGP12 = false;
                inspeccion.EnContencion = false;
                inspeccion.EsScrap = false;
                inspeccion.RequiereReliberacion = false;
                inspeccion.Estado = CalidadEstados.ProduccionLiberada;
                inspeccion.FechaLiberacionProduccion = ahora;
                inspeccion.UsuarioLiberacionProduccionID = usuarioId.Value;
                inspeccion.FechaValidacionPrimerasPiezas = ahora;
                inspeccion.UsuarioValidacionPrimerasPiezasID = usuarioId.Value;

                if (inspeccion.FechaNotificacionCalidad.HasValue)
                {
                    var minutos = (int)Math.Max(0, Math.Round((ahora - inspeccion.FechaNotificacionCalidad.Value).TotalMinutes));
                    inspeccion.MinutosLiberacionInicial = minutos;
                    inspeccion.CumplioTiempoObjetivoInicial = minutos >= 10 && minutos <= 20;
                }

                if (reliberacionPendiente != null)
                {
                    reliberacionPendiente.Resultado = CalidadResultadoReliberacion.Autorizada;
                    reliberacionPendiente.FechaValidacion = ahora;
                    reliberacionPendiente.UsuarioCalidadID = usuarioId.Value;
                    reliberacionPendiente.Observaciones = UnirObservaciones(
                        reliberacionPendiente.Observaciones,
                        $"Reliberación autorizada con el intento {intento.NumeroIntento} de primeras piezas conformes.");
                    reliberacionPendiente.UsuarioModificacionID = usuarioId.Value;
                    reliberacionPendiente.FechaModificacion = ahora;
                }

                MarcarModificacion(inspeccion, usuarioId.Value);

                AgregarHistorial(
                    inspeccion,
                    eraReliberacion
                        ? CalidadMovimientos.ReliberacionAutorizada
                        : CalidadMovimientos.ProduccionLiberada,
                    estadoAnterior,
                    inspeccion.Estado,
                    inspeccion.ResultadoCalidad,
                    inspeccion.Etiqueta,
                    eraReliberacion
                        ? $"Reliberación {reliberacionPendiente?.NumeroReliberacion} autorizada con etiqueta verde. Producción puede reiniciar la serie."
                        : $"Intento {intento.NumeroIntento} conforme. Calidad asignó etiqueta verde. Producción debe confirmar el inicio de serie.",
                    usuarioId.Value);

                await _context.SaveChangesAsync();
                await tx.CommitAsync();

                TempData["Mensaje"] = eraReliberacion
                    ? "Reliberación autorizada con etiqueta verde. Producción puede reiniciar la serie."
                    : "Producción liberada con etiqueta verde. Producción debe confirmar el inicio de serie.";
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                TempData["Error"] = "No fue posible liberar la producción: " + ex.Message;
            }

            return RedirectToAction(nameof(Detalle), new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SincronizarMonitoreosCalidad(int id)
        {
            if (id <= 0) return NotFound();

            var usuarioId = ObtenerUsuarioIdActual();
            if (!usuarioId.HasValue || usuarioId.Value <= 0) return Unauthorized();

            var inspeccion = await _context.CalidadInspecciones
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.InspeccionID == id);

            if (inspeccion == null) return NotFound();

            if (inspeccion.Estado != CalidadEstados.MonitoreoActivo)
            {
                TempData["Error"] = inspeccion.Estado == CalidadEstados.ProduccionLiberada
                    ? "Calidad ya liberó la producción, pero Producción todavía no confirma el inicio de serie."
                    : "La inspección no se encuentra en monitoreo horario activo.";

                return RedirectToAction(nameof(Detalle), new { id });
            }

            if (!inspeccion.EjecucionProduccionID.HasValue)
            {
                TempData["Error"] = "La inspección no tiene una ejecución relacionada y no puede sincronizar periodos.";
                return RedirectToAction(nameof(Detalle), new { id });
            }

            try
            {
                await ReconciliarMonitoreosConProduccionAsync(id, usuarioId);

                var total = await _context.CalidadMonitoreosProceso
                    .AsNoTracking()
                    .CountAsync(x => x.InspeccionID == id && x.Activo);

                var vinculados = await _context.CalidadMonitoreosProceso
                    .AsNoTracking()
                    .CountAsync(x =>
                        x.InspeccionID == id &&
                        x.Activo &&
                        x.RegistroHoraID.HasValue);

                var pendientesProduccion = await _context.CalidadMonitoreosProceso
                    .AsNoTracking()
                    .CountAsync(x =>
                        x.InspeccionID == id &&
                        x.Activo &&
                        !x.RegistroHoraID.HasValue);

                TempData["Mensaje"] =
                    $"Seguimiento actualizado. Periodos: {total}; vinculados con Producción: {vinculados}; pendientes de captura: {pendientesProduccion}.";
            }
            catch (Exception ex)
            {
                TempData["Error"] = "No fue posible sincronizar los monitoreos de Calidad: " + ex.Message;
            }

            return RedirectToAction(nameof(Detalle), new { id });
        }
        private async Task<int> AsegurarMonitoreosActivosAsync(
            int inspeccionId,
            int? usuarioId)
        {
            var inspeccion = await _context.CalidadInspecciones
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.InspeccionID == inspeccionId);

            if (inspeccion == null)
                throw new InvalidOperationException("No se encontró la inspección de Calidad.");

            if (inspeccion.Estado != CalidadEstados.MonitoreoActivo)
                return 0;

            if (!inspeccion.EjecucionProduccionID.HasValue)
            {
                throw new InvalidOperationException(
                    "La inspección no tiene una ejecución de Producción relacionada.");
            }

            var horas = 9;

            if (inspeccion.FechaInicioProgramada.HasValue &&
                inspeccion.FechaFinProgramada.HasValue)
            {
                var duracion =
                    (inspeccion.FechaFinProgramada.Value -
                     inspeccion.FechaInicioProgramada.Value).TotalHours;

                if (duracion > 0)
                    horas = Math.Clamp((int)Math.Ceiling(duracion), 1, 9);
            }

            var primerMonitoreo = await _context.CalidadMonitoreosProceso
                .AsNoTracking()
                .Where(x => x.InspeccionID == inspeccionId && x.Activo)
                .OrderBy(x => x.NumeroHora)
                .Select(x => new
                {
                    x.NumeroHora,
                    x.FechaHoraProgramada
                })
                .FirstOrDefaultAsync();

            DateTime fechaBase;

            if (primerMonitoreo != null)
            {
                fechaBase = primerMonitoreo.FechaHoraProgramada
                    .AddHours(-primerMonitoreo.NumeroHora);
            }
            else
            {
                /*
                 * Solo se consulta la fecha real de inicio. Calidad no
                 * modifica la ejecución ni ninguna captura de Producción.
                 */
                const string sqlInicioReal = @"
SELECT TOP (1) FechaInicioReal
FROM dbo.Produccion_Ejecucion
WHERE EjecucionProduccionID = @EjecucionProduccionID
  AND Activo = 1;";

                await using var cn = new SqlConnection(ConnectionString);
                await cn.OpenAsync();

                await using var cmd = new SqlCommand(sqlInicioReal, cn);
                cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value =
                    inspeccion.EjecucionProduccionID.Value;

                var fechaInicioReal = await cmd.ExecuteScalarAsync();
                fechaBase = fechaInicioReal == null || fechaInicioReal == DBNull.Value
                    ? DateTime.Now
                    : Convert.ToDateTime(fechaInicioReal);
            }

            int? usuarioRegistro = usuarioId ??
                inspeccion.UsuarioModificacionID ??
                inspeccion.UsuarioCreacionID;

            var creados = await _context.Database.ExecuteSqlInterpolatedAsync($@"
;WITH Numeros AS
(
    SELECT NumeroHora
    FROM (VALUES (1),(2),(3),(4),(5),(6),(7),(8),(9)) n(NumeroHora)
    WHERE NumeroHora <= {horas}
)
INSERT INTO dbo.Calidad_MonitoreosProceso
(
    InspeccionID,
    EjecucionProduccionID,
    NumeroHora,
    FechaHoraProgramada,
    CantidadProducidaPeriodo,
    CantidadRevisadaMuestra,
    Resultado,
    CantidadSospechosa,
    CantidadNoRecuperable,
    RequiereSeleccion,
    RequiereRetrabajo,
    UsuarioCreacionID,
    FechaCreacion,
    Activo
)
SELECT
    {inspeccionId},
    {inspeccion.EjecucionProduccionID.Value},
    n.NumeroHora,
    DATEADD(HOUR, n.NumeroHora, {fechaBase}),
    0,
    0,
    {CalidadResultadoMonitoreo.Pendiente},
    0,
    0,
    0,
    0,
    {usuarioRegistro},
    GETDATE(),
    1
FROM Numeros n
WHERE NOT EXISTS
(
    SELECT 1
    FROM dbo.Calidad_MonitoreosProceso m WITH (UPDLOCK, HOLDLOCK)
    WHERE m.InspeccionID = {inspeccionId}
      AND m.NumeroHora = n.NumeroHora
      AND m.Activo = 1
);");

            return creados;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RegistrarMonitoreo(
            CalidadMonitoreoGuardarViewModel model)
        {
            if (!ModelState.IsValid)
            {
                TempData["Error"] =
                    "Los datos del monitoreo son incompletos o no son válidos.";

                return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
            }

            var usuarioId = ObtenerUsuarioIdActual();
            if (!usuarioId.HasValue || usuarioId.Value <= 0)
                return Unauthorized();

            var resultado = NormalizarResultadoMonitoreo(model.Resultado);
            if (resultado == null)
            {
                TempData["Error"] = "Selecciona un resultado válido para el monitoreo.";
                return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
            }

            model.DefectoCodigo = model.DefectoCodigo?.Trim().ToUpperInvariant();
            model.DefectoDescripcion = model.DefectoDescripcion?.Trim();
            model.Observaciones = model.Observaciones?.Trim();
            model.ResponsableRetrabajo =
                model.ResponsableRetrabajo?.Trim().ToUpperInvariant();

            await using var tx = await _context.Database.BeginTransactionAsync();

            try
            {
                var monitor = await _context.CalidadMonitoreosProceso
                    .FirstOrDefaultAsync(x =>
                        x.MonitoreoID == model.MonitoreoID &&
                        x.InspeccionID == model.InspeccionID &&
                        x.Activo);

                if (monitor == null)
                    return NotFound();

                var inspeccion = await _context.CalidadInspecciones
                    .FirstOrDefaultAsync(x => x.InspeccionID == model.InspeccionID);

                if (inspeccion == null)
                    return NotFound();

                if (inspeccion.Estado != CalidadEstados.MonitoreoActivo)
                {
                    TempData["Error"] =
                        "La inspección no se encuentra en monitoreo horario activo.";

                    return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
                }

                if (monitor.Resultado != CalidadResultadoMonitoreo.Pendiente)
                {
                    TempData["Error"] =
                        "Este monitoreo ya fue capturado y no puede sobrescribirse.";

                    return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
                }

                if (!monitor.RegistroHoraID.HasValue)
                {
                    TempData["Error"] =
                        "Producción aún no ha registrado las cantidades de este periodo.";

                    return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
                }

                if (monitor.CantidadProducidaPeriodo <= 0)
                {
                    TempData["Error"] =
                        "El periodo vinculado no tiene piezas producidas para revisar.";

                    return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
                }

                if (model.CantidadRevisadaMuestra <= 0)
                {
                    TempData["Error"] =
                        "La cantidad revisada como muestra debe ser mayor a cero.";

                    return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
                }

                if (model.CantidadRevisadaMuestra > monitor.CantidadProducidaPeriodo)
                {
                    TempData["Error"] =
                        "La muestra no puede superar la cantidad producida en el periodo.";

                    return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
                }

                if (model.CantidadSospechosa < 0 ||
                    model.CantidadNoRecuperable < 0)
                {
                    TempData["Error"] = "Las cantidades afectadas no pueden ser negativas.";
                    return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
                }

                var cantidadAfectada =
                    model.CantidadSospechosa + model.CantidadNoRecuperable;

                if (cantidadAfectada > monitor.CantidadProducidaPeriodo)
                {
                    TempData["Error"] =
                        "La cantidad afectada no puede superar lo producido en el periodo.";

                    return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
                }

                if (resultado == CalidadResultadoMonitoreo.Conforme)
                {
                    model.CantidadSospechosa = 0;
                    model.CantidadNoRecuperable = 0;
                    model.RequiereSeleccion = false;
                    model.RequiereRetrabajo = false;
                    model.ResponsableRetrabajo = null;
                    model.DefectoCodigo = null;
                    model.DefectoDescripcion = null;
                    cantidadAfectada = 0;
                }
                else
                {
                    if (cantidadAfectada <= 0)
                    {
                        TempData["Error"] = "Captura la cantidad de material afectado.";
                        return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
                    }

                    if (string.IsNullOrWhiteSpace(model.DefectoCodigo) &&
                        string.IsNullOrWhiteSpace(model.DefectoDescripcion))
                    {
                        TempData["Error"] = "Selecciona o describe el defecto detectado.";
                        return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
                    }

                    /*
                     * La tabla guarda una sola disposición principal. Por eso
                     * no se permite seleccionar simultáneamente selección y
                     * retrabajo para el mismo hallazgo.
                     */
                    if (model.RequiereSeleccion == model.RequiereRetrabajo)
                    {
                        TempData["Error"] =
                            "Selecciona un solo tratamiento: selección o retrabajo.";

                        return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
                    }

                    if (model.RequiereRetrabajo &&
                        model.ResponsableRetrabajo != CalidadResponsable.Produccion &&
                        model.ResponsableRetrabajo != CalidadResponsable.Calidad)
                    {
                        TempData["Error"] = "Selecciona al responsable del retrabajo.";
                        return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
                    }

                    if (resultado == CalidadResultadoMonitoreo.NoConforme &&
                        string.IsNullOrWhiteSpace(model.Observaciones))
                    {
                        TempData["Error"] =
                            "Describe el hallazgo antes de guardar un resultado no conforme.";

                        return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
                    }
                }

                var defectoDescripcion = model.DefectoDescripcion;
                var defectoCodigo = model.DefectoCodigo;

                if (!string.IsNullOrWhiteSpace(defectoCodigo))
                {
                    var catalogo = await _context.CalidadCatalogoDefectos
                        .AsNoTracking()
                        .FirstOrDefaultAsync(x =>
                            x.Activo &&
                            x.Codigo == defectoCodigo);

                    if (catalogo == null)
                    {
                        TempData["Error"] =
                            "El código de defecto seleccionado ya no se encuentra activo.";

                        return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
                    }

                    defectoDescripcion = string.IsNullOrWhiteSpace(defectoDescripcion)
                        ? catalogo.Nombre
                        : catalogo.Nombre + ". " + defectoDescripcion;
                }

                var ahora = DateTime.Now;

                monitor.FechaHoraRevision = ahora;
                monitor.CantidadRevisadaMuestra = model.CantidadRevisadaMuestra;
                monitor.Resultado = resultado;
                monitor.DefectoCodigo = defectoCodigo;
                monitor.DefectoDescripcion = defectoDescripcion;
                monitor.CantidadSospechosa = model.CantidadSospechosa;
                monitor.CantidadNoRecuperable = model.CantidadNoRecuperable;
                monitor.RequiereSeleccion = model.RequiereSeleccion;
                monitor.RequiereRetrabajo = model.RequiereRetrabajo;
                monitor.ResponsableRetrabajo = model.RequiereRetrabajo
                    ? model.ResponsableRetrabajo
                    : null;
                monitor.Observaciones = model.Observaciones;
                monitor.UsuarioCalidadID = usuarioId;
                monitor.UsuarioModificacionID = usuarioId;
                monitor.FechaModificacion = ahora;

                if (resultado == CalidadResultadoMonitoreo.Sospechoso ||
                    resultado == CalidadResultadoMonitoreo.NoConforme)
                {
                    var disposicion = await _context.CalidadDisposicionesMaterial
                        .FirstOrDefaultAsync(x =>
                            x.MonitoreoID == monitor.MonitoreoID &&
                            x.Activo &&
                            x.ResultadoFinal == CalidadResultadoDisposicion.Pendiente);

                    if (disposicion == null)
                    {
                        disposicion = new CalidadDisposicionMaterial
                        {
                            InspeccionID = inspeccion.InspeccionID,
                            MonitoreoID = monitor.MonitoreoID,
                            UsuarioCreacionID = usuarioId,
                            FechaCreacion = ahora,
                            FechaInicio = ahora,
                            Activo = true
                        };

                        _context.CalidadDisposicionesMaterial.Add(disposicion);
                    }

                    disposicion.TipoMaterial =
                        resultado == CalidadResultadoMonitoreo.NoConforme
                            ? CalidadTipoMaterial.NoConforme
                            : CalidadTipoMaterial.Sospechoso;

                    disposicion.CantidadAfectada = cantidadAfectada;
                    disposicion.Etiqueta =
                        resultado == CalidadResultadoMonitoreo.NoConforme
                            ? "ROJA"
                            : "AMARILLA";

                    disposicion.Disposicion = model.RequiereRetrabajo
                        ? CalidadTipoDisposicion.Retrabajo
                        : CalidadTipoDisposicion.Seleccion;

                    disposicion.Responsable = model.RequiereRetrabajo
                        ? model.ResponsableRetrabajo
                        : CalidadResponsable.Calidad;

                    disposicion.ResultadoFinal = CalidadResultadoDisposicion.Pendiente;
                    disposicion.Observaciones = model.Observaciones;
                    disposicion.UsuarioModificacionID = usuarioId;
                    disposicion.FechaModificacion = ahora;
                }

                MarcarModificacion(inspeccion, usuarioId);

                AgregarHistorial(
                    inspeccion,
                    CalidadMovimientos.MonitoreoRegistrado,
                    inspeccion.Estado,
                    inspeccion.Estado,
                    resultado,
                    resultado == CalidadResultadoMonitoreo.Conforme
                        ? "VERDE"
                        : resultado == CalidadResultadoMonitoreo.Sospechoso
                            ? "AMARILLA"
                            : "ROJA",
                    $"Monitoreo hora {monitor.NumeroHora}. " +
                    $"Muestra: {model.CantidadRevisadaMuestra}. " +
                    $"Afectado: {cantidadAfectada}. " +
                    (model.Observaciones ?? string.Empty),
                    usuarioId);

                await _context.SaveChangesAsync();
                await tx.CommitAsync();

                TempData["Mensaje"] =
                    resultado == CalidadResultadoMonitoreo.Conforme
                        ? "Monitoreo registrado como conforme."
                        : "Monitoreo registrado y material separado para disposición.";
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                TempData["Error"] =
                    "No fue posible registrar el monitoreo: " + ex.Message;
            }

            return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResolverDisposicion(
            CalidadDisposicionResolverViewModel model)
        {
            if (!ModelState.IsValid)
            {
                TempData["Error"] = "Los datos de la disposición no son válidos.";
                return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
            }

            model.Observaciones = model.Observaciones?.Trim();

            var usuarioId = ObtenerUsuarioIdActual();
            if (!usuarioId.HasValue || usuarioId.Value <= 0)
                return Unauthorized();

            await using var tx = await _context.Database.BeginTransactionAsync();

            try
            {
                var disposicion = await _context.CalidadDisposicionesMaterial
                    .FirstOrDefaultAsync(x =>
                        x.DisposicionID == model.DisposicionID &&
                        x.InspeccionID == model.InspeccionID &&
                        x.Activo);

                if (disposicion == null)
                    return NotFound();

                if (disposicion.ResultadoFinal != CalidadResultadoDisposicion.Pendiente)
                {
                    TempData["Error"] = "Esta disposición ya fue resuelta.";
                    return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
                }

                if (model.CantidadLiberada < 0 || model.CantidadScrap < 0)
                {
                    TempData["Error"] = "Las cantidades no pueden ser negativas.";
                    return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
                }

                if (model.CantidadLiberada + model.CantidadScrap !=
                    disposicion.CantidadAfectada)
                {
                    TempData["Error"] =
                        "La suma de material liberado y scrap debe ser igual a la cantidad afectada.";

                    return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
                }

                if (model.CantidadScrap > 0 &&
                    string.IsNullOrWhiteSpace(model.Observaciones))
                {
                    TempData["Error"] =
                        "Documenta el motivo o resultado cuando la disposición incluya scrap.";

                    return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
                }

                var inspeccion = await _context.CalidadInspecciones
                    .FirstOrDefaultAsync(x => x.InspeccionID == model.InspeccionID);

                if (inspeccion == null)
                    return NotFound();

                if (disposicion.CantidadAfectada <= 0)
                {
                    TempData["Error"] =
                        "La disposición no contiene una cantidad afectada válida.";

                    return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
                }

                var ahora = DateTime.Now;
                var liberacionTotal =
                    model.CantidadLiberada == disposicion.CantidadAfectada &&
                    model.CantidadScrap == 0;
                var liberacionParcial =
                    model.CantidadLiberada > 0 &&
                    model.CantidadScrap > 0;
                var scrapTotal =
                    model.CantidadLiberada == 0 &&
                    model.CantidadScrap == disposicion.CantidadAfectada;

                disposicion.CantidadLiberada = model.CantidadLiberada;
                disposicion.CantidadScrap = model.CantidadScrap;
                disposicion.FechaFin = ahora;
                disposicion.ResultadoFinal = model.CantidadLiberada > 0
                    ? CalidadResultadoDisposicion.Liberado
                    : CalidadResultadoDisposicion.Scrap;

                /*
                 * Disposicion conserva el tratamiento aplicado
                 * (SELECCION o RETRABAJO). El resultado final se
                 * guarda por separado en ResultadoFinal y cantidades.
                 */
                disposicion.Etiqueta = liberacionTotal
                    ? "VERDE"
                    : scrapTotal
                        ? "ROJA"
                        : "AMARILLA";

                disposicion.Observaciones = UnirObservaciones(
                    disposicion.Observaciones,
                    model.Observaciones);
                disposicion.UsuarioModificacionID = usuarioId;
                disposicion.FechaModificacion = ahora;

                if (disposicion.MonitoreoID.HasValue)
                {
                    var monitor = await _context.CalidadMonitoreosProceso
                        .FirstOrDefaultAsync(x =>
                            x.MonitoreoID == disposicion.MonitoreoID.Value &&
                            x.Activo);

                    if (monitor != null)
                    {
                        monitor.Resultado = model.CantidadLiberada > 0
                            ? CalidadResultadoMonitoreo.Reinspeccion
                            : CalidadResultadoMonitoreo.NoConforme;

                        monitor.Observaciones = UnirObservaciones(
                            monitor.Observaciones,
                            "Disposición concluida. Material liberado: " +
                            model.CantidadLiberada + ". Scrap: " +
                            model.CantidadScrap + ". " +
                            (model.Observaciones ?? string.Empty));
                        monitor.UsuarioModificacionID = usuarioId;
                        monitor.FechaModificacion = ahora;
                    }
                }

                MarcarModificacion(inspeccion, usuarioId);

                var resultadoDisposicionTexto = liberacionTotal
                    ? "Liberación total"
                    : liberacionParcial
                        ? "Liberación parcial con scrap"
                        : "Scrap total";

                AgregarHistorial(
                    inspeccion,
                    "DISPOSICION_RESUELTA",
                    inspeccion.Estado,
                    inspeccion.Estado,
                    disposicion.ResultadoFinal,
                    disposicion.Etiqueta,
                    $"Disposición {disposicion.DisposicionID} resuelta: " +
                    $"{resultadoDisposicionTexto}. " +
                    $"Tratamiento: {disposicion.Disposicion}. " +
                    $"Liberado: {model.CantidadLiberada}. " +
                    $"Scrap: {model.CantidadScrap}. " +
                    (model.Observaciones ?? string.Empty),
                    usuarioId);

                await _context.SaveChangesAsync();
                await tx.CommitAsync();

                TempData["Mensaje"] = liberacionTotal
                    ? "La reinspección concluyó con liberación total del material."
                    : liberacionParcial
                        ? "La reinspección concluyó con liberación parcial y scrap documentado."
                        : "La disposición concluyó como scrap total documentado.";
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                TempData["Error"] =
                    "No fue posible resolver la disposición: " + ex.Message;
            }

            return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GuardarMuestraResguardo(
            CalidadMuestraResguardoGuardarViewModel model)
        {
            if (!ModelState.IsValid)
            {
                TempData["Error"] =
                    "Revisa la cantidad, las ubicaciones y las confirmaciones de la muestra.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id = model.InspeccionID });
            }

            var momento = NormalizarMomentoMuestra(model.Momento);
            if (momento == null)
            {
                TempData["Error"] = "El momento de resguardo no es válido.";
                return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
            }

            model.UbicacionCalidad = model.UbicacionCalidad?.Trim();
            model.UbicacionProduccion = model.UbicacionProduccion?.Trim();
            model.Observaciones = model.Observaciones?.Trim();

            if (!model.MuestraCalidadConfirmada &&
                !model.MuestraProduccionConfirmada)
            {
                TempData["Error"] =
                    "Confirma al menos una de las dos muestras antes de guardar.";

                return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
            }

            if (model.MuestraCalidadConfirmada &&
                string.IsNullOrWhiteSpace(model.UbicacionCalidad))
            {
                TempData["Error"] =
                    "Captura la ubicación de la muestra resguardada por Calidad.";

                return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
            }

            if (model.MuestraProduccionConfirmada &&
                string.IsNullOrWhiteSpace(model.UbicacionProduccion))
            {
                TempData["Error"] =
                    "Captura la ubicación de la muestra resguardada por Producción.";

                return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
            }

            if (!model.MuestraCalidadConfirmada)
                model.UbicacionCalidad = null;

            if (!model.MuestraProduccionConfirmada)
                model.UbicacionProduccion = null;

            var usuarioId = ObtenerUsuarioIdActual();
            if (!usuarioId.HasValue || usuarioId.Value <= 0)
                return Unauthorized();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();

            try
            {
                const string sqlInspeccion = @"
SELECT TOP (1)
    EjecucionProduccionID,
    Estado,
    ISNULL(ConfiguracionInvalidada, 0) AS ConfiguracionInvalidada,
    ResultadoCalidad,
    Etiqueta
FROM dbo.Calidad_Inspecciones WITH (UPDLOCK, HOLDLOCK)
WHERE InspeccionID = @InspeccionID;";

                int? ejecucionProduccionId = null;
                string estadoInspeccion = string.Empty;
                string? resultadoCalidad = null;
                string? etiqueta = null;
                bool configuracionInvalidada = false;

                await using (var cmd = new SqlCommand(sqlInspeccion, cn, tx))
                {
                    cmd.Parameters.Add("@InspeccionID", SqlDbType.Int).Value =
                        model.InspeccionID;

                    await using var rd = await cmd.ExecuteReaderAsync();
                    if (!await rd.ReadAsync())
                    {
                        await tx.RollbackAsync();
                        return NotFound();
                    }

                    ejecucionProduccionId = rd["EjecucionProduccionID"] == DBNull.Value
                        ? null
                        : Convert.ToInt32(rd["EjecucionProduccionID"]);
                    estadoInspeccion = rd["Estado"]?.ToString()?.Trim() ?? string.Empty;
                    configuracionInvalidada = Convert.ToBoolean(rd["ConfiguracionInvalidada"]);
                    resultadoCalidad = rd["ResultadoCalidad"] as string;
                    etiqueta = rd["Etiqueta"] as string;
                }

                if (estadoInspeccion == CalidadEstados.Cerrada)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] =
                        "La inspección está cerrada y la muestra ya no puede modificarse.";
                    return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
                }

                if (configuracionInvalidada)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] =
                        "La configuración está invalidada y no permite registrar el resguardo final.";
                    return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
                }

                if (!ejecucionProduccionId.HasValue || ejecucionProduccionId.Value <= 0)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] =
                        "La inspección no tiene una ejecución de Producción relacionada.";
                    return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
                }

                var ahora = DateTime.Now;
                var muestraCompleta =
                    model.MuestraCalidadConfirmada &&
                    model.MuestraProduccionConfirmada &&
                    !string.IsNullOrWhiteSpace(model.UbicacionCalidad) &&
                    !string.IsNullOrWhiteSpace(model.UbicacionProduccion);

                const string sqlGuardar = @"
DECLARE @MuestraResguardoID INT;

SELECT TOP (1)
    @MuestraResguardoID = MuestraResguardoID
FROM dbo.Calidad_MuestrasResguardo WITH (UPDLOCK, HOLDLOCK)
WHERE InspeccionID = @InspeccionID
  AND Momento = @Momento
  AND Activo = 1
ORDER BY MuestraResguardoID DESC;

IF @MuestraResguardoID IS NULL
BEGIN
    INSERT INTO dbo.Calidad_MuestrasResguardo
    (
        InspeccionID,
        EjecucionProduccionID,
        Momento,
        CantidadDisparos,
        MuestraCalidadConfirmada,
        MuestraProduccionConfirmada,
        UbicacionCalidad,
        UbicacionProduccion,
        FechaResguardo,
        UsuarioResponsableID,
        Observaciones,
        UsuarioCreacionID,
        FechaCreacion,
        Activo
    )
    VALUES
    (
        @InspeccionID,
        @EjecucionProduccionID,
        @Momento,
        @CantidadDisparos,
        @MuestraCalidadConfirmada,
        @MuestraProduccionConfirmada,
        @UbicacionCalidad,
        @UbicacionProduccion,
        @FechaResguardo,
        @UsuarioID,
        @Observaciones,
        @UsuarioID,
        @Ahora,
        1
    );

    SET @MuestraResguardoID = CONVERT(INT, SCOPE_IDENTITY());
END
ELSE
BEGIN
    UPDATE dbo.Calidad_MuestrasResguardo
    SET
        EjecucionProduccionID = @EjecucionProduccionID,
        CantidadDisparos = @CantidadDisparos,
        MuestraCalidadConfirmada = @MuestraCalidadConfirmada,
        MuestraProduccionConfirmada = @MuestraProduccionConfirmada,
        UbicacionCalidad = @UbicacionCalidad,
        UbicacionProduccion = @UbicacionProduccion,
        FechaResguardo = @FechaResguardo,
        UsuarioResponsableID = @UsuarioID,
        Observaciones = @Observaciones,
        UsuarioModificacionID = @UsuarioID,
        FechaModificacion = @Ahora
    WHERE MuestraResguardoID = @MuestraResguardoID;
END;

SELECT @MuestraResguardoID;";

                int muestraResguardoId;

                await using (var cmd = new SqlCommand(sqlGuardar, cn, tx))
                {
                    cmd.Parameters.Add("@InspeccionID", SqlDbType.Int).Value =
                        model.InspeccionID;
                    cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value =
                        ejecucionProduccionId.Value;
                    cmd.Parameters.Add("@Momento", SqlDbType.VarChar, 30).Value = momento;
                    cmd.Parameters.Add("@CantidadDisparos", SqlDbType.Int).Value =
                        model.CantidadDisparos;
                    cmd.Parameters.Add("@MuestraCalidadConfirmada", SqlDbType.Bit).Value =
                        model.MuestraCalidadConfirmada;
                    cmd.Parameters.Add("@MuestraProduccionConfirmada", SqlDbType.Bit).Value =
                        model.MuestraProduccionConfirmada;
                    cmd.Parameters.Add("@UbicacionCalidad", SqlDbType.NVarChar, 250).Value =
                        (object?)model.UbicacionCalidad ?? DBNull.Value;
                    cmd.Parameters.Add("@UbicacionProduccion", SqlDbType.NVarChar, 250).Value =
                        (object?)model.UbicacionProduccion ?? DBNull.Value;
                    cmd.Parameters.Add("@FechaResguardo", SqlDbType.DateTime2).Value =
                        muestraCompleta ? (object)ahora : DBNull.Value;
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId.Value;
                    cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 1000).Value =
                        (object?)model.Observaciones ?? DBNull.Value;
                    cmd.Parameters.Add("@Ahora", SqlDbType.DateTime2).Value = ahora;

                    var result = await cmd.ExecuteScalarAsync();
                    if (result == null || result == DBNull.Value)
                        throw new InvalidOperationException(
                            "No fue posible registrar la muestra de resguardo.");

                    muestraResguardoId = Convert.ToInt32(result);
                }

                const string sqlModificarInspeccion = @"
UPDATE dbo.Calidad_Inspecciones
SET
    UsuarioModificacionID = @UsuarioID,
    FechaModificacion = @Ahora
WHERE InspeccionID = @InspeccionID;";

                await using (var cmd = new SqlCommand(sqlModificarInspeccion, cn, tx))
                {
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId.Value;
                    cmd.Parameters.Add("@Ahora", SqlDbType.DateTime2).Value = ahora;
                    cmd.Parameters.Add("@InspeccionID", SqlDbType.Int).Value = model.InspeccionID;
                    await cmd.ExecuteNonQueryAsync();
                }

                var comentario =
                    $"Muestra de {momento.Replace("_", " ").ToLowerInvariant()} " +
                    $"#{muestraResguardoId}. Disparos: {model.CantidadDisparos}. " +
                    $"Calidad: {(model.MuestraCalidadConfirmada ? "confirmada" : "pendiente")}. " +
                    $"Producción: {(model.MuestraProduccionConfirmada ? "confirmada" : "pendiente")}.";

                if (!string.IsNullOrWhiteSpace(model.Observaciones))
                    comentario += " " + model.Observaciones;

                await InsertarHistorialCalidadSqlAsync(
                    model.InspeccionID,
                    "MUESTRA_RESGUARDO_GUARDADA",
                    estadoInspeccion,
                    estadoInspeccion,
                    resultadoCalidad,
                    etiqueta,
                    comentario,
                    usuarioId.Value,
                    ahora,
                    cn,
                    tx);

                await tx.CommitAsync();

                TempData["Mensaje"] = muestraCompleta
                    ? "Muestras de fin de producción confirmadas y resguardadas."
                    : "Avance del resguardo guardado; todavía falta confirmar una de las muestras.";
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                TempData["Error"] =
                    "No fue posible guardar la muestra de resguardo: " + ex.Message;
            }

            return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
        }

        private async Task<IActionResult> CerrarInspeccionCalidadAsync(int id, string? observaciones)
        {
            observaciones = observaciones?.Trim();

            if (id <= 0) return NotFound();

            if (!string.IsNullOrWhiteSpace(observaciones) && observaciones.Length > 1000)
            {
                TempData["Error"] = "Las observaciones del cierre no pueden superar 1000 caracteres.";
                return RedirectToAction(nameof(Detalle), new { id });
            }

            var usuarioId = ObtenerUsuarioIdActual();
            if (!usuarioId.HasValue || usuarioId.Value <= 0) return Unauthorized();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);

            try
            {
                const string sqlInspeccion = @"
SELECT TOP (1)
    i.EjecucionProduccionID,
    UPPER(LTRIM(RTRIM(ISNULL(i.Estado,N'')))) AS Estado,
    i.ResultadoCalidad,
    i.Etiqueta
FROM dbo.Calidad_Inspecciones i WITH (UPDLOCK,HOLDLOCK)
WHERE i.InspeccionID=@InspeccionID;";

                int ejecucionProduccionId;
                string estadoAnterior;
                string? resultadoCalidad;
                string? etiqueta;

                await using (var cmd = new SqlCommand(sqlInspeccion, cn, tx))
                {
                    cmd.Parameters.Add("@InspeccionID", SqlDbType.Int).Value = id;
                    await using var rd = await cmd.ExecuteReaderAsync();

                    if (!await rd.ReadAsync())
                    {
                        await tx.RollbackAsync();
                        return NotFound();
                    }

                    if (rd["EjecucionProduccionID"] == DBNull.Value)
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] = "La inspección no tiene una ejecución de Producción relacionada.";
                        return RedirectToAction(nameof(Detalle), new { id });
                    }

                    ejecucionProduccionId = Convert.ToInt32(rd["EjecucionProduccionID"]);
                    estadoAnterior = rd["Estado"]?.ToString()?.Trim() ?? string.Empty;
                    resultadoCalidad = rd["ResultadoCalidad"] == DBNull.Value ? null : rd["ResultadoCalidad"].ToString();
                    etiqueta = rd["Etiqueta"] == DBNull.Value ? null : rd["Etiqueta"].ToString();
                }

                if (estadoAnterior == CalidadEstados.Cerrada)
                {
                    await tx.CommitAsync();
                    TempData["Mensaje"] = "La inspección ya se encontraba cerrada.";
                    return RedirectToAction(nameof(Detalle), new { id });
                }

                var cierre = await LeerEstadoCierreAsync(id, cn, tx);
                if (!cierre.PuedeCerrar)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "La inspección todavía no puede cerrarse. " + string.Join(" ", cierre.Bloqueos);
                    return RedirectToAction(nameof(Detalle), new { id });
                }

                var ahora = DateTime.Now;
                var comentario = "Inspección cerrada después de confirmar que Producción terminó, no existen paros abiertos, todos los monitoreos y disposiciones fueron resueltos, todas las cajas registraron salida de Producción, GP12 y reliberaciones están concluidos y las muestras finales fueron resguardadas.";
                if (!string.IsNullOrWhiteSpace(observaciones)) comentario += " " + observaciones;
                if (comentario.Length > 1000) comentario = comentario[..1000];

                const string sqlCerrar = @"
UPDATE dbo.Calidad_Inspecciones
SET Estado=@EstadoCerrado,
    Liberado=0,
    RequiereReliberacion=0,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=@Ahora,
    Observaciones=
        CASE
            WHEN @ObservacionesCierre IS NULL THEN Observaciones
            WHEN Observaciones IS NULL OR LTRIM(RTRIM(Observaciones))=N'' THEN @ObservacionesCierre
            WHEN Observaciones LIKE N'%'+@MarcaCierre+N'%' THEN Observaciones
            ELSE Observaciones+CHAR(13)+CHAR(10)+@ObservacionesCierre
        END
WHERE InspeccionID=@InspeccionID
  AND EjecucionProduccionID=@EjecucionProduccionID
  AND UPPER(LTRIM(RTRIM(ISNULL(Estado,N''))))<>@EstadoCerrado;

IF @@ROWCOUNT<>1
    THROW 51200,'La inspección cambió de estado mientras se intentaba cerrar.',1;

INSERT INTO dbo.Calidad_InspeccionHistorial
(
    InspeccionID,
    Movimiento,
    EstadoAnterior,
    EstadoNuevo,
    ResultadoCalidad,
    Etiqueta,
    Comentario,
    UsuarioID,
    FechaMovimiento
)
SELECT
    @InspeccionID,
    @Movimiento,
    @EstadoAnterior,
    @EstadoCerrado,
    @ResultadoCalidad,
    @Etiqueta,
    @Comentario,
    @UsuarioID,
    @Ahora
WHERE NOT EXISTS
(
    SELECT 1
    FROM dbo.Calidad_InspeccionHistorial h
    WHERE h.InspeccionID=@InspeccionID
      AND h.Movimiento=@Movimiento
      AND UPPER(LTRIM(RTRIM(ISNULL(h.EstadoNuevo,N''))))=@EstadoCerrado
);";

                await using (var cmd = new SqlCommand(sqlCerrar, cn, tx))
                {
                    cmd.Parameters.Add("@InspeccionID", SqlDbType.Int).Value = id;
                    cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
                    cmd.Parameters.Add("@EstadoCerrado", SqlDbType.NVarChar, 50).Value = CalidadEstados.Cerrada;
                    cmd.Parameters.Add("@Movimiento", SqlDbType.NVarChar, 100).Value = CalidadMovimientos.Cierre;
                    cmd.Parameters.Add("@EstadoAnterior", SqlDbType.NVarChar, 50).Value = string.IsNullOrWhiteSpace(estadoAnterior) ? DBNull.Value : estadoAnterior;
                    cmd.Parameters.Add("@ResultadoCalidad", SqlDbType.NVarChar, 30).Value = string.IsNullOrWhiteSpace(resultadoCalidad) ? DBNull.Value : resultadoCalidad;
                    cmd.Parameters.Add("@Etiqueta", SqlDbType.NVarChar, 30).Value = string.IsNullOrWhiteSpace(etiqueta) ? DBNull.Value : etiqueta;
                    cmd.Parameters.Add("@Comentario", SqlDbType.NVarChar, 1000).Value = comentario;
                    cmd.Parameters.Add("@ObservacionesCierre", SqlDbType.NVarChar, 1000).Value = string.IsNullOrWhiteSpace(observaciones) ? DBNull.Value : $"Cierre de Calidad: {observaciones}";
                    cmd.Parameters.Add("@MarcaCierre", SqlDbType.NVarChar, 100).Value = "Cierre de Calidad:";
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId.Value;
                    cmd.Parameters.Add("@Ahora", SqlDbType.DateTime2).Value = ahora;
                    await cmd.ExecuteNonQueryAsync();
                }

                await tx.CommitAsync();
                TempData["Mensaje"] = "Inspección de Calidad cerrada correctamente. La ejecución de Producción permanece terminada y conserva su trazabilidad.";
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                TempData["Error"] = "No fue posible cerrar la inspección: " + ex.Message;
            }

            return RedirectToAction(nameof(Detalle), new { id });
        }

        private static async Task InsertarHistorialCalidadSqlAsync(
            int inspeccionId,
            string movimiento,
            string? estadoAnterior,
            string? estadoNuevo,
            string? resultadoCalidad,
            string? etiqueta,
            string comentario,
            int usuarioId,
            DateTime fechaMovimiento,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
INSERT INTO dbo.Calidad_InspeccionHistorial
(
    InspeccionID,
    Movimiento,
    EstadoAnterior,
    EstadoNuevo,
    ResultadoCalidad,
    Etiqueta,
    Comentario,
    UsuarioID,
    FechaMovimiento
)
VALUES
(
    @InspeccionID,
    @Movimiento,
    @EstadoAnterior,
    @EstadoNuevo,
    @ResultadoCalidad,
    @Etiqueta,
    @Comentario,
    @UsuarioID,
    @FechaMovimiento
);";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@InspeccionID", SqlDbType.Int).Value = inspeccionId;
            cmd.Parameters.Add("@Movimiento", SqlDbType.NVarChar, 100).Value = movimiento;
            cmd.Parameters.Add("@EstadoAnterior", SqlDbType.NVarChar, 50).Value =
                (object?)estadoAnterior ?? DBNull.Value;
            cmd.Parameters.Add("@EstadoNuevo", SqlDbType.NVarChar, 50).Value =
                (object?)estadoNuevo ?? DBNull.Value;
            cmd.Parameters.Add("@ResultadoCalidad", SqlDbType.NVarChar, 30).Value =
                (object?)resultadoCalidad ?? DBNull.Value;
            cmd.Parameters.Add("@Etiqueta", SqlDbType.NVarChar, 30).Value =
                (object?)etiqueta ?? DBNull.Value;
            cmd.Parameters.Add("@Comentario", SqlDbType.NVarChar, 1000).Value =
                comentario.Length <= 1000 ? comentario : comentario[..1000];
            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
            cmd.Parameters.Add("@FechaMovimiento", SqlDbType.DateTime2).Value = fechaMovimiento;

            await cmd.ExecuteNonQueryAsync();
        }

        private async Task<List<CalidadMonitoreoItemViewModel>> CargarMonitoreosDetalleAsync(
            int inspeccionId)
        {
            var lista = new List<CalidadMonitoreoItemViewModel>();

            const string sql = @"
SELECT
    m.MonitoreoID,
    m.RegistroHoraID,
    m.NumeroHora,
    m.FechaHoraProgramada,
    m.FechaHoraRevision,
    m.CantidadProducidaPeriodo,
    m.CantidadRevisadaMuestra,
    m.Resultado,
    m.DefectoCodigo,
    m.DefectoDescripcion,
    m.CantidadSospechosa,
    m.CantidadNoRecuperable,
    m.RequiereSeleccion,
    m.RequiereRetrabajo,
    m.ResponsableRetrabajo,
    m.Observaciones,

    rh.FechaProduccion,
    rh.HoraInicio,
    rh.HoraFin,
    ISNULL(rh.CantidadOK, 0) AS CantidadOKProduccion,
    ISNULL(rh.CantidadSospechosa, 0) AS CantidadSospechosaProduccion,
    ISNULL(rh.CantidadScrap, 0) AS CantidadScrapProduccion,
    rh.Observaciones AS ObservacionesProduccion
FROM dbo.Calidad_MonitoreosProceso m
LEFT JOIN dbo.Produccion_RegistroHora rh
    ON rh.RegistroHoraID = m.RegistroHoraID
   AND rh.Activo = 1
WHERE m.InspeccionID = @InspeccionID
  AND m.Activo = 1
ORDER BY m.NumeroHora;";

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@InspeccionID", SqlDbType.Int).Value = inspeccionId;

            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                lista.Add(new CalidadMonitoreoItemViewModel
                {
                    MonitoreoID = Convert.ToInt32(rd["MonitoreoID"]),
                    RegistroHoraID = rd["RegistroHoraID"] == DBNull.Value
                        ? null
                        : Convert.ToInt32(rd["RegistroHoraID"]),
                    NumeroHora = Convert.ToInt32(rd["NumeroHora"]),
                    FechaHoraProgramada = Convert.ToDateTime(rd["FechaHoraProgramada"]),
                    FechaHoraRevision = rd["FechaHoraRevision"] == DBNull.Value
                        ? null
                        : Convert.ToDateTime(rd["FechaHoraRevision"]),
                    CantidadProducidaPeriodo = Convert.ToInt32(rd["CantidadProducidaPeriodo"]),
                    CantidadRevisadaMuestra = Convert.ToInt32(rd["CantidadRevisadaMuestra"]),
                    Resultado = rd["Resultado"] as string ?? CalidadResultadoMonitoreo.Pendiente,
                    DefectoCodigo = rd["DefectoCodigo"] as string,
                    DefectoDescripcion = rd["DefectoDescripcion"] as string,
                    CantidadSospechosa = Convert.ToInt32(rd["CantidadSospechosa"]),
                    CantidadNoRecuperable = Convert.ToInt32(rd["CantidadNoRecuperable"]),
                    RequiereSeleccion = Convert.ToBoolean(rd["RequiereSeleccion"]),
                    RequiereRetrabajo = Convert.ToBoolean(rd["RequiereRetrabajo"]),
                    ResponsableRetrabajo = rd["ResponsableRetrabajo"] as string,
                    Observaciones = rd["Observaciones"] as string,
                    FechaProduccion = rd["FechaProduccion"] == DBNull.Value
                        ? null
                        : Convert.ToDateTime(rd["FechaProduccion"]),
                    HoraInicioProduccion = rd["HoraInicio"] == DBNull.Value
                        ? null
                        : (TimeSpan?)rd["HoraInicio"],
                    HoraFinProduccion = rd["HoraFin"] == DBNull.Value
                        ? null
                        : (TimeSpan?)rd["HoraFin"],
                    CantidadOKProduccion = Convert.ToInt32(rd["CantidadOKProduccion"]),
                    CantidadSospechosaProduccion = Convert.ToInt32(rd["CantidadSospechosaProduccion"]),
                    CantidadScrapProduccion = Convert.ToInt32(rd["CantidadScrapProduccion"]),
                    ObservacionesProduccion = rd["ObservacionesProduccion"] as string
                });
            }

            return lista;
        }

        private static string? NormalizarResultadoMonitoreo(string? resultado)
        {
            if (string.IsNullOrWhiteSpace(resultado))
                return null;

            var valor = resultado.Trim().ToUpperInvariant();

            return valor switch
            {
                CalidadResultadoMonitoreo.Conforme => CalidadResultadoMonitoreo.Conforme,
                CalidadResultadoMonitoreo.Sospechoso => CalidadResultadoMonitoreo.Sospechoso,
                CalidadResultadoMonitoreo.NoConforme => CalidadResultadoMonitoreo.NoConforme,
                _ => null
            };
        }

        private static string? UnirObservaciones(string? anterior, string? nueva)
        {
            anterior = anterior?.Trim();
            nueva = nueva?.Trim();

            if (string.IsNullOrWhiteSpace(anterior))
                return string.IsNullOrWhiteSpace(nueva) ? null : nueva;

            if (string.IsNullOrWhiteSpace(nueva))
                return anterior;

            return anterior + Environment.NewLine + nueva;
        }

        private async Task<(bool Valido, string Mensaje)> ValidarChecklistCalidadCompletoAsync(
            int checklistArranqueId)
        {
            const string sql = @"
SELECT
    SUM(CASE WHEN d.Resultado IS NULL OR LTRIM(RTRIM(d.Resultado)) = '' THEN 1 ELSE 0 END) AS SinRespuesta,
    SUM(CASE WHEN d.Resultado = 'NOK' THEN 1 ELSE 0 END) AS TotalNOK,
    SUM(CASE WHEN d.Resultado = 'NOK' AND (d.Observaciones IS NULL OR LTRIM(RTRIM(d.Observaciones)) = '') THEN 1 ELSE 0 END) AS NokSinObservacion,
    COUNT(1) AS TotalPreguntas
FROM dbo.Produccion_ChecklistArranqueDetalle d
INNER JOIN dbo.ERP_ChecklistArranquePreguntas p
    ON p.PreguntaID = d.PreguntaID
WHERE d.ChecklistArranqueID = @ChecklistArranqueID
  AND d.Activo = 1
  AND p.Activo = 1
  AND
  (
      UPPER(ISNULL(p.Seccion, '')) LIKE '%CALIDAD%'
      OR UPPER(ISNULL(p.ResponsableSugerido, '')) LIKE '%CALIDAD%'
  );";

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@ChecklistArranqueID", SqlDbType.Int).Value = checklistArranqueId;
            await using var rd = await cmd.ExecuteReaderAsync();

            if (!await rd.ReadAsync() || Convert.ToInt32(rd["TotalPreguntas"]) == 0)
                return (false, "No se encontraron preguntas asignadas al auditor de Calidad.");

            if (Convert.ToInt32(rd["SinRespuesta"]) > 0)
                return (false, "Responde todas las preguntas del auditor de Calidad antes de autorizar.");

            if (Convert.ToInt32(rd["NokSinObservacion"]) > 0)
                return (false, "Existen respuestas NOK sin observacion.");

            if (Convert.ToInt32(rd["TotalNOK"]) > 0)
                return (false, "El checklist contiene resultados NOK. Debe devolverse a Produccion.");

            return (true, string.Empty);
        }

        private async Task<CalidadPrimeraPiezaIntento> ObtenerOCrearIntentoPendienteAsync(
            int inspeccionId,
            int usuarioId)
        {
            var existente = await _context.CalidadPrimerasPiezasIntentos
                .Where(x =>
                    x.InspeccionID == inspeccionId &&
                    x.Activo &&
                    x.Resultado == CalidadResultadoIntento.Pendiente)
                .OrderByDescending(x => x.NumeroIntento)
                .FirstOrDefaultAsync();

            if (existente != null)
                return existente;

            var numero = (await _context.CalidadPrimerasPiezasIntentos
                .Where(x => x.InspeccionID == inspeccionId)
                .MaxAsync(x => (int?)x.NumeroIntento) ?? 0) + 1;

            var intento = new CalidadPrimeraPiezaIntento
            {
                InspeccionID = inspeccionId,
                NumeroIntento = numero,
                FechaInicio = DateTime.Now,
                Resultado = CalidadResultadoIntento.Pendiente,
                UsuarioCalidadID = usuarioId,
                UsuarioCreacionID = usuarioId,
                FechaCreacion = DateTime.Now,
                Activo = true
            };

            _context.CalidadPrimerasPiezasIntentos.Add(intento);
            return intento;
        }

        private static void AplicarDatosIntento(
            CalidadPrimeraPiezaIntento intento,
            CalidadPrimerasPiezasViewModel model,
            int usuarioId)
        {
            intento.CincoDisparosSegregados = model.CincoDisparosSegregados;
            intento.CantidadDisparosPresentados = model.CantidadDisparosConformes;
            intento.ValidacionDimensional = model.ValidacionDimensional;
            intento.ValidacionApariencia = model.ValidacionApariencia;
            intento.ValidacionGauge = model.ValidacionGauge;
            intento.ValidacionConductividad = model.ValidacionConductividad;
            intento.Observaciones = model.Observaciones?.Trim();
            intento.UsuarioCalidadID = usuarioId;
            intento.UsuarioModificacionID = usuarioId;
            intento.FechaModificacion = DateTime.Now;
        }

        private static void AplicarResumenPrimerasPiezas(
            CalidadInspeccion inspeccion,
            CalidadPrimerasPiezasViewModel model,
            int usuarioId)
        {
            inspeccion.CincoDisparosSegregados = model.CincoDisparosSegregados;
            inspeccion.CantidadDisparosConformes = model.CantidadDisparosConformes;
            inspeccion.ValidacionDimensional = model.ValidacionDimensional;
            inspeccion.ValidacionApariencia = model.ValidacionApariencia;
            inspeccion.ValidacionGauge = model.ValidacionGauge;
            inspeccion.ValidacionConductividad = model.ValidacionConductividad;
            inspeccion.FechaValidacionPrimerasPiezas = DateTime.Now;
            inspeccion.UsuarioValidacionPrimerasPiezasID = usuarioId;
            if (!string.IsNullOrWhiteSpace(model.Observaciones))
                inspeccion.Observaciones = model.Observaciones.Trim();
        }

      

        private static string? NormalizarMomentoMuestra(string? momento)
        {
            if (string.IsNullOrWhiteSpace(momento))
                return null;

            var valor = momento.Trim().ToUpperInvariant();

            return valor switch
            {
                CalidadMomentoMuestra.FinProduccion => CalidadMomentoMuestra.FinProduccion,
                CalidadMomentoMuestra.CambioMolde => CalidadMomentoMuestra.CambioMolde,
                _ => null
            };
        }

        private static string? NormalizarResultadoCalidad(string? resultado)
        {
            if (string.IsNullOrWhiteSpace(resultado)) return null;
            var valor = resultado.Trim().ToUpperInvariant();
            if (valor == "OK") return "OK";
            if (valor == "NOK") return "NOK";
            if (valor == "NA" || valor == "N/A") return "NA";
            return "__INVALIDO__";
        }
    }
}
