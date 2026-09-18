using ERP.NSQuell.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ERP.NSQuell.Controllers;

public sealed partial class ProduccionOperativaController
{
    [HttpGet("Produccion/{programaProduccionId:int}")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> SeguimientoProduccionOperativa(int programaProduccionId, CancellationToken cancellationToken = default)
    {
        if (!UsuarioEnSesion()) return Unauthorized(new { ok = false, sesionExpirada = true, mensaje = "La sesión terminó. Vuelve a iniciar sesión." });
        if (programaProduccionId <= 0) return BadRequest(new { ok = false, mensaje = "El programa de Producción no es válido." });
        try
        {
            var centro = await ConstruirCentroOperativoAsync(programaProduccionId, ProduccionOperativaOrigen.Calendario, true, cancellationToken);
            if (centro == null) return NotFound(new { ok = false, mensaje = "No se encontró el programa de Producción solicitado." });
            if (!centro.Permisos.PuedeVerCentroOperativo) return StatusCode(StatusCodes.Status403Forbidden, new { ok = false, mensaje = "No tienes permiso para consultar esta producción." });

            var vm = new ProduccionSeguimientoTecnicoOperativoVm
            {
                FechaConsulta = DateTime.Now,
                ProgramaProduccionID = centro.ProgramaProduccionID,
                EjecucionProduccionID = centro.EjecucionProduccionID,
                NumeroOF = centro.OFTexto,
                NumeroParte = centro.NumeroParte ?? string.Empty,
                ReferenciaSAP = centro.ReferenciaSAP,
                DescripcionParte = centro.DescripcionParte,
                Maquina = centro.MaquinaTexto,
                Molde = centro.MoldeTexto,
                EstadoGeneral = centro.EstadoGeneral,
                EstadoGeneralTexto = centro.EstadoGeneralTexto,
                FechaInicioProgramada = centro.FechaInicioProgramada,
                FechaFinProgramada = centro.FechaFinProgramada,
                FechaInicioReal = centro.FechaInicioReal,
                FechaFinReal = centro.FechaFinReal,
                EsParejaLhRh = centro.EsParejaLhRh,
                GrupoLhRh = centro.LhRh.GrupoLhRh,
                LadoActual = centro.LhRh.LadoActual,
                ProgramaParejaID = centro.LhRh.ProgramaParejaID,
                EjecucionParejaID = centro.LhRh.EjecucionParejaID,
                NumeroOFPareja = centro.LhRh.NumeroOFPareja,
                LadoPareja = centro.LhRh.LadoPareja
            };

            var contextos = new List<SeguimientoContextoLado>
            {
                new()
                {
                    ProgramaProduccionID=centro.ProgramaProduccionID,
                    EjecucionProduccionID=centro.EjecucionProduccionID,
                    NumeroOF=centro.OFTexto,
                    LadoLhRh=centro.EsParejaLhRh?centro.LhRh.LadoActual:null,
                    NumeroParte=centro.NumeroParte,
                    ReferenciaSAP=centro.ReferenciaSAP,
                    DescripcionParte=centro.DescripcionParte
                }
            };

            if (centro.EsParejaLhRh && centro.LhRh.ProgramaParejaID.HasValue && centro.LhRh.ProgramaParejaID.Value > 0)
            {
                contextos.Add(new SeguimientoContextoLado
                {
                    ProgramaProduccionID = centro.LhRh.ProgramaParejaID.Value,
                    EjecucionProduccionID = centro.LhRh.EjecucionParejaID,
                    NumeroOF = string.IsNullOrWhiteSpace(centro.LhRh.NumeroOFPareja) ? $"Programa {centro.LhRh.ProgramaParejaID.Value}" : centro.LhRh.NumeroOFPareja!,
                    LadoLhRh = centro.LhRh.LadoPareja,
                    NumeroParte = centro.LhRh.NumeroPartePareja,
                    ReferenciaSAP = centro.LhRh.ReferenciaSAPPareja
                });
            }

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(cancellationToken);

            foreach (var contexto in contextos)
            {
                if (contexto.EjecucionProduccionID.HasValue && contexto.EjecucionProduccionID.Value > 0)
                {
                    var lado = await CargarSeguimientoTecnicoLadoAsync(contexto, cn, cancellationToken);
                    if (lado != null) vm.Lados.Add(lado);
                }
            }

            vm.CapturasRecientes = vm.Lados
                .SelectMany(x => x.CapturasRecientes)
                .GroupBy(x => x.RegistroHoraID)
                .Select(x => x.First())
                .OrderByDescending(x => x.FechaHoraFin)
                .ThenByDescending(x => x.RegistroHoraID)
                .Take(16)
                .ToList();

            vm.ParosRecientes = vm.Lados
                .SelectMany(x => x.ParosRecientes)
                .GroupBy(x => x.ParoID)
                .Select(x => x.First())
                .OrderByDescending(x => x.FechaInicioParo)
                .ThenByDescending(x => x.ParoID)
                .Take(12)
                .ToList();

            return PartialView("~/Views/ProduccionOperativa/Acciones/_ProduccionContenido.cshtml", vm);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al cargar seguimiento técnico de Producción. ProgramaProduccionID: {ProgramaProduccionID}. UsuarioID: {UsuarioID}", programaProduccionId, ObtenerUsuarioID());
            return StatusCode(StatusCodes.Status500InternalServerError, new { ok = false, mensaje = "No fue posible cargar el seguimiento de Producción: " + ex.Message });
        }
    }

    private async Task<ProduccionSeguimientoTecnicoLadoVm?> CargarSeguimientoTecnicoLadoAsync(SeguimientoContextoLado contexto, SqlConnection cn, CancellationToken cancellationToken)
    {
        if (!contexto.EjecucionProduccionID.HasValue || contexto.EjecucionProduccionID.Value <= 0) return null;
        const string sql = @"
SELECT TOP(1)
    e.EjecucionProduccionID,
    e.ProgramaProduccionID,
    e.EstatusID,
    e.MaquinaID,e.MaquinaCodigo,e.MaquinaNombre,
    e.ParteID,e.NumeroParte,e.ReferenciaSAP,e.DescripcionParte,
    e.MoldeID,e.MoldeCodigo,
    e.OperadorID,e.OperadorNombre,e.OperadorAuxiliarID,e.OperadorAuxiliarNombre,
    e.FechaInicioReal,e.FechaFinReal,
    ISNULL(e.CantidadPlaneada,0) AS CantidadPlaneada,
    ISNULL(e.CantidadOKTotal,0) AS CantidadOKTotal,
    ISNULL(e.CantidadSospechosaTotal,0) AS CantidadSospechosaTotal,
    ISNULL(e.CantidadScrapTotal,0) AS CantidadScrapTotal,
    pp.FechaInicioProgramada,pp.FechaFinProgramada,
    ISNULL(reg.CapturasRegistradas,0) AS CapturasRegistradas,
    ISNULL(reg.ObjetivoAcumulado,0) AS ObjetivoAcumulado,
    reg.FechaUltimaCaptura,

    conf.ConfiguracionCorridaID,
    conf.CavidadesUsadas,
    conf.CavidadesConfiguradas,
    conf.TiempoCicloSegundos,
    conf.ObjetivoHoraCalculado,
    conf.ContadorInicioVigencia,
    conf.FechaInicioVigencia,
    conf.EsConfiguracionInicial,
    conf.MotivoCambio,
    conf.TecnicoProduccionID,
    conf.TecnicoProduccionNombre,

    lectura.LecturaContadorID,
    lectura.ValorContador,
    lectura.FechaLectura,
    lectura.TipoLectura,
    lectura.EsReinicioContador,

    ci.InspeccionID,
    ci.Estado AS EstadoCalidad,
    ci.ResultadoCalidad,
    ci.Etiqueta,
    ci.MotivoDevolucion,
    ci.FechaNotificacionCalidad,
    ci.FechaLiberacionProduccion,
    ci.ConfiguracionInvalidada,
    ci.RequiereReliberacion,
    ci.Liberado,
    ISNULL(mon.TotalMonitoreos,0) AS TotalMonitoreos,
    ISNULL(mon.MonitoreosPendientes,0) AS MonitoreosPendientes,
    ISNULL(mon.MonitoreosVencidos,0) AS MonitoreosVencidos,
    ISNULL(mon.MonitoreosConformes,0) AS MonitoreosConformes,
    ISNULL(mon.MonitoreosConHallazgo,0) AS MonitoreosConHallazgo,
    mon.ProximoMonitoreo,
    ISNULL(disp.DisposicionesPendientes,0) AS DisposicionesPendientes,

    paro.ParoID AS ParoAbiertoID,
    paro.FechaInicioParo AS ParoAbiertoInicio,
    paro.DuracionMinutos AS ParoAbiertoDuracion,
    paro.MotivoParoTexto AS ParoAbiertoMotivo,
    paro.Descripcion AS ParoAbiertoDescripcion,
    paro.EsMayorA15Minutos AS ParoAbiertoMayor15,
    paro.EsInterrupcionUrgente AS ParoAbiertoUrgente,
    paro.ProgramaUrgenteID AS ParoAbiertoProgramaUrgenteID
FROM dbo.Produccion_Ejecucion e
LEFT JOIN dbo.Planeacion_ProgramaProduccion pp ON pp.ProgramaProduccionID=e.ProgramaProduccionID
OUTER APPLY
(
    SELECT
        COUNT(1) AS CapturasRegistradas,
        SUM(ISNULL(NULLIF(rh.ObjetivoBloque,0),ISNULL(rh.ObjetivoHora,0))) AS ObjetivoAcumulado,
        MAX(rh.FechaCreacion) AS FechaUltimaCaptura
    FROM dbo.Produccion_RegistroHora rh
    WHERE rh.EjecucionProduccionID=e.EjecucionProduccionID
      AND rh.Activo=1
      AND ISNULL(rh.EsTiempoExtra,0)=0
) reg
OUTER APPLY
(
    SELECT TOP(1)
        c.ConfiguracionCorridaID,c.CavidadesUsadas,c.CavidadesConfiguradas,c.TiempoCicloSegundos,c.ObjetivoHoraCalculado,
        c.ContadorInicioVigencia,c.FechaInicioVigencia,c.EsConfiguracionInicial,c.MotivoCambio,c.TecnicoProduccionID,
        NULLIF(LTRIM(RTRIM(CONCAT(ISNULL(pt.Nombre,N''),N' ',ISNULL(pt.ApellidoPaterno,N''),N' ',ISNULL(pt.ApellidoMaterno,N'')))),N'') AS TecnicoProduccionNombre
    FROM dbo.Produccion_ConfiguracionCorrida c
    LEFT JOIN dbo.Persona pt ON pt.PersonaID=c.TecnicoProduccionID
    WHERE c.EjecucionProduccionID=e.EjecucionProduccionID
      AND c.Activo=1
      AND c.FechaFinVigencia IS NULL
    ORDER BY c.ConfiguracionCorridaID DESC
) conf
OUTER APPLY
(
    SELECT TOP(1)
        l.LecturaContadorID,l.ValorContador,l.FechaLectura,l.TipoLectura,CAST(ISNULL(l.EsReinicioContador,0) AS BIT) AS EsReinicioContador
    FROM dbo.Produccion_ContadorMaquinaLecturas l
    WHERE l.EjecucionProduccionID=e.EjecucionProduccionID
      AND l.Activo=1
    ORDER BY l.FechaLectura DESC,l.LecturaContadorID DESC
) lectura
OUTER APPLY
(
    SELECT TOP(1)
        ci0.InspeccionID,ci0.Estado,ci0.ResultadoCalidad,ci0.Etiqueta,ci0.MotivoDevolucion,
        ci0.FechaNotificacionCalidad,ci0.FechaLiberacionProduccion,
        CAST(ISNULL(ci0.ConfiguracionInvalidada,0) AS BIT) AS ConfiguracionInvalidada,
        CAST(ISNULL(ci0.RequiereReliberacion,0) AS BIT) AS RequiereReliberacion,
        CAST(ISNULL(ci0.Liberado,0) AS BIT) AS Liberado
    FROM dbo.Calidad_Inspecciones ci0
    WHERE ci0.EjecucionProduccionID=e.EjecucionProduccionID
    ORDER BY ci0.InspeccionID DESC
) ci
OUTER APPLY
(
    SELECT
        COUNT(1) AS TotalMonitoreos,
        SUM(CASE WHEN m.Resultado=N'PENDIENTE' THEN 1 ELSE 0 END) AS MonitoreosPendientes,
        SUM(CASE WHEN m.Resultado=N'PENDIENTE' AND m.FechaHoraProgramada<GETDATE() THEN 1 ELSE 0 END) AS MonitoreosVencidos,
        SUM(CASE WHEN m.Resultado=N'CONFORME' THEN 1 ELSE 0 END) AS MonitoreosConformes,
        SUM(CASE WHEN m.Resultado IN(N'SOSPECHOSO',N'NO_CONFORME') THEN 1 ELSE 0 END) AS MonitoreosConHallazgo,
        MIN(CASE WHEN m.Resultado=N'PENDIENTE' THEN m.FechaHoraProgramada END) AS ProximoMonitoreo
    FROM dbo.Calidad_MonitoreosProceso m
    WHERE m.InspeccionID=ci.InspeccionID
      AND m.Activo=1
) mon
OUTER APPLY
(
    SELECT COUNT(1) AS DisposicionesPendientes
    FROM dbo.Calidad_DisposicionesMaterial d
    WHERE d.InspeccionID=ci.InspeccionID
      AND d.Activo=1
      AND d.ResultadoFinal=N'PENDIENTE'
) disp
OUTER APPLY
(
    SELECT TOP(1)
        p.ParoID,p.FechaInicioParo,
        DATEDIFF(MINUTE,p.FechaInicioParo,GETDATE()) AS DuracionMinutos,
        p.MotivoParoTexto,p.Descripcion,
        CAST(CASE WHEN ISNULL(p.EsMayorA15Minutos,0)=1 OR DATEDIFF(MINUTE,p.FechaInicioParo,GETDATE())>15 THEN 1 ELSE 0 END AS BIT) AS EsMayorA15Minutos,
        CAST(ISNULL(p.EsInterrupcionUrgente,0) AS BIT) AS EsInterrupcionUrgente,
        p.ProgramaUrgenteID
    FROM dbo.Produccion_Paros p
    WHERE p.EjecucionProduccionID=e.EjecucionProduccionID
      AND p.Activo=1
      AND p.FechaFinParo IS NULL
    ORDER BY p.FechaInicioParo DESC,p.ParoID DESC
) paro
WHERE e.EjecucionProduccionID=@EjecucionProduccionID
  AND e.Activo=1;";

        ProduccionSeguimientoTecnicoLadoVm? vm = null;
        await using (var cmd = new SqlCommand(sql, cn))
        {
            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = contexto.EjecucionProduccionID.Value;
            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
            if (!await rd.ReadAsync(cancellationToken)) return null;

            vm = new ProduccionSeguimientoTecnicoLadoVm
            {
                ProgramaProduccionID = Convert.ToInt32(rd["ProgramaProduccionID"]),
                EjecucionProduccionID = Convert.ToInt32(rd["EjecucionProduccionID"]),
                NumeroOF = contexto.NumeroOF,
                LadoLhRh = contexto.LadoLhRh,
                EstatusID = Convert.ToInt32(rd["EstatusID"]),
                MaquinaID = SeguimientoNullableInt(rd, "MaquinaID"),
                MaquinaCodigo = SeguimientoTexto(rd, "MaquinaCodigo"),
                MaquinaNombre = SeguimientoTexto(rd, "MaquinaNombre"),
                ParteID = SeguimientoNullableInt(rd, "ParteID"),
                NumeroParte = SeguimientoTexto(rd, "NumeroParte") ?? contexto.NumeroParte,
                ReferenciaSAP = SeguimientoTexto(rd, "ReferenciaSAP") ?? contexto.ReferenciaSAP,
                DescripcionParte = SeguimientoTexto(rd, "DescripcionParte") ?? contexto.DescripcionParte,
                MoldeID = SeguimientoNullableInt(rd, "MoldeID"),
                MoldeCodigo = SeguimientoTexto(rd, "MoldeCodigo"),
                OperadorID = SeguimientoNullableInt(rd, "OperadorID"),
                OperadorNombre = SeguimientoTexto(rd, "OperadorNombre"),
                OperadorAuxiliarID = SeguimientoNullableInt(rd, "OperadorAuxiliarID"),
                OperadorAuxiliarNombre = SeguimientoTexto(rd, "OperadorAuxiliarNombre"),
                FechaInicioProgramada = SeguimientoNullableDate(rd, "FechaInicioProgramada"),
                FechaFinProgramada = SeguimientoNullableDate(rd, "FechaFinProgramada"),
                FechaInicioReal = SeguimientoNullableDate(rd, "FechaInicioReal"),
                FechaFinReal = SeguimientoNullableDate(rd, "FechaFinReal"),
                CantidadPlaneada = Convert.ToInt32(rd["CantidadPlaneada"]),
                CantidadOK = Convert.ToInt32(rd["CantidadOKTotal"]),
                CantidadSospechosa = Convert.ToInt32(rd["CantidadSospechosaTotal"]),
                CantidadScrap = Convert.ToInt32(rd["CantidadScrapTotal"]),
                CapturasRegistradas = Convert.ToInt32(rd["CapturasRegistradas"]),
                ObjetivoAcumulado = rd["ObjetivoAcumulado"] == DBNull.Value ? 0 : Convert.ToInt32(Math.Round(Convert.ToDecimal(rd["ObjetivoAcumulado"]), 0, MidpointRounding.AwayFromZero)),
                FechaUltimaCaptura = SeguimientoNullableDate(rd, "FechaUltimaCaptura")
            };

            if (rd["ConfiguracionCorridaID"] != DBNull.Value)
            {
                vm.Configuracion = new ProduccionSeguimientoTecnicoConfiguracionVm
                {
                    ConfiguracionCorridaID = Convert.ToInt32(rd["ConfiguracionCorridaID"]),
                    EjecucionProduccionID = vm.EjecucionProduccionID,
                    CavidadesUsadas = Convert.ToInt32(rd["CavidadesUsadas"]),
                    CavidadesConfiguradas = SeguimientoTexto(rd, "CavidadesConfiguradas"),
                    TiempoCicloSegundos = Convert.ToDecimal(rd["TiempoCicloSegundos"]),
                    ObjetivoHoraCalculado = Convert.ToDecimal(rd["ObjetivoHoraCalculado"]),
                    ContadorInicioVigencia = SeguimientoNullableLong(rd, "ContadorInicioVigencia"),
                    FechaInicioVigencia = Convert.ToDateTime(rd["FechaInicioVigencia"]),
                    EsConfiguracionInicial = rd["EsConfiguracionInicial"] != DBNull.Value && Convert.ToBoolean(rd["EsConfiguracionInicial"]),
                    MotivoCambio = SeguimientoTexto(rd, "MotivoCambio"),
                    TecnicoProduccionID = SeguimientoNullableInt(rd, "TecnicoProduccionID"),
                    TecnicoProduccionNombre = SeguimientoTexto(rd, "TecnicoProduccionNombre")
                };
            }

            if (rd["LecturaContadorID"] != DBNull.Value)
            {
                vm.UltimaLecturaContador = new ProduccionSeguimientoTecnicoLecturaVm
                {
                    LecturaContadorID = Convert.ToInt64(rd["LecturaContadorID"]),
                    EjecucionProduccionID = vm.EjecucionProduccionID,
                    ValorContador = Convert.ToInt64(rd["ValorContador"]),
                    FechaLectura = Convert.ToDateTime(rd["FechaLectura"]),
                    TipoLectura = SeguimientoTexto(rd, "TipoLectura"),
                    EsReinicioContador = rd["EsReinicioContador"] != DBNull.Value && Convert.ToBoolean(rd["EsReinicioContador"])
                };
            }

            if (rd["InspeccionID"] != DBNull.Value)
            {
                vm.Calidad = new ProduccionSeguimientoTecnicoCalidadVm
                {
                    InspeccionID = Convert.ToInt32(rd["InspeccionID"]),
                    Estado = SeguimientoTexto(rd, "EstadoCalidad") ?? string.Empty,
                    ResultadoCalidad = SeguimientoTexto(rd, "ResultadoCalidad"),
                    Etiqueta = SeguimientoTexto(rd, "Etiqueta"),
                    MotivoDevolucion = SeguimientoTexto(rd, "MotivoDevolucion"),
                    FechaNotificacionCalidad = SeguimientoNullableDate(rd, "FechaNotificacionCalidad"),
                    FechaLiberacionProduccion = SeguimientoNullableDate(rd, "FechaLiberacionProduccion"),
                    ConfiguracionInvalidada = rd["ConfiguracionInvalidada"] != DBNull.Value && Convert.ToBoolean(rd["ConfiguracionInvalidada"]),
                    RequiereReliberacion = rd["RequiereReliberacion"] != DBNull.Value && Convert.ToBoolean(rd["RequiereReliberacion"]),
                    Liberado = rd["Liberado"] != DBNull.Value && Convert.ToBoolean(rd["Liberado"]),
                    TotalMonitoreos = Convert.ToInt32(rd["TotalMonitoreos"]),
                    MonitoreosPendientes = Convert.ToInt32(rd["MonitoreosPendientes"]),
                    MonitoreosVencidos = Convert.ToInt32(rd["MonitoreosVencidos"]),
                    MonitoreosConformes = Convert.ToInt32(rd["MonitoreosConformes"]),
                    MonitoreosConHallazgo = Convert.ToInt32(rd["MonitoreosConHallazgo"]),
                    DisposicionesPendientes = Convert.ToInt32(rd["DisposicionesPendientes"]),
                    ProximoMonitoreo = SeguimientoNullableDate(rd, "ProximoMonitoreo")
                };
            }

            if (rd["ParoAbiertoID"] != DBNull.Value)
            {
                vm.ParoAbierto = new ProduccionSeguimientoTecnicoParoVm
                {
                    ParoID = Convert.ToInt32(rd["ParoAbiertoID"]),
                    EjecucionProduccionID = vm.EjecucionProduccionID,
                    ProgramaProduccionID = vm.ProgramaProduccionID,
                    NumeroOF = vm.NumeroOF,
                    LadoLhRh = vm.LadoLhRh,
                    FechaInicioParo = Convert.ToDateTime(rd["ParoAbiertoInicio"]),
                    DuracionMinutos = rd["ParoAbiertoDuracion"] == DBNull.Value ? 0 : Convert.ToInt32(rd["ParoAbiertoDuracion"]),
                    MotivoParoTexto = SeguimientoTexto(rd, "ParoAbiertoMotivo"),
                    Descripcion = SeguimientoTexto(rd, "ParoAbiertoDescripcion"),
                    EsMayorA15Minutos = rd["ParoAbiertoMayor15"] != DBNull.Value && Convert.ToBoolean(rd["ParoAbiertoMayor15"]),
                    EsInterrupcionUrgente = rd["ParoAbiertoUrgente"] != DBNull.Value && Convert.ToBoolean(rd["ParoAbiertoUrgente"]),
                    ProgramaUrgenteID = SeguimientoNullableInt(rd, "ParoAbiertoProgramaUrgenteID")
                };
            }
        }

        if (vm == null) return null;
        vm.CapturasRecientes = await CargarCapturasSeguimientoTecnicoAsync(vm, cn, cancellationToken);
        vm.ParosRecientes = await CargarParosSeguimientoTecnicoAsync(vm, cn, cancellationToken);
        return vm;
    }

    private static async Task<List<ProduccionSeguimientoTecnicoCapturaVm>> CargarCapturasSeguimientoTecnicoAsync(ProduccionSeguimientoTecnicoLadoVm lado, SqlConnection cn, CancellationToken cancellationToken)
    {
        var lista = new List<ProduccionSeguimientoTecnicoCapturaVm>();
        const string sql = @"
SELECT TOP(12)
    rh.RegistroHoraID,rh.EjecucionProduccionID,rh.ProgramaProduccionID,rh.OperadorID,rh.FechaProduccion,rh.HoraInicio,rh.HoraFin,
    ISNULL(rh.CantidadOK,0) AS CantidadOK,ISNULL(rh.CantidadSospechosa,0) AS CantidadSospechosa,ISNULL(rh.CantidadScrap,0) AS CantidadScrap,
    rh.ObjetivoHora,rh.ObjetivoBloque,rh.CumplioObjetivo,rh.DiferenciaObjetivo,rh.PorcentajeCumplimiento,
    rh.PiezasCalculadasContador,rh.MinutosProductivos,
    CAST(ISNULL(rh.TieneCambioConfiguracion,0) AS BIT) AS TieneCambioConfiguracion,
    CAST(ISNULL(rh.TieneReinicioContador,0) AS BIT) AS TieneReinicioContador,
    CAST(ISNULL(rh.AjustadoPorCalidad,0) AS BIT) AS AjustadoPorCalidad,
    rh.Observaciones,rh.FechaCreacion,
    NULLIF(LTRIM(RTRIM(CONCAT(ISNULL(p.Nombre,N''),N' ',ISNULL(p.ApellidoPaterno,N''),N' ',ISNULL(p.ApellidoMaterno,N'')))),N'') AS OperadorNombre
FROM dbo.Produccion_RegistroHora rh
LEFT JOIN dbo.Persona p ON p.PersonaID=rh.OperadorID
WHERE rh.EjecucionProduccionID=@EjecucionProduccionID
  AND rh.Activo=1
  AND ISNULL(rh.EsTiempoExtra,0)=0
ORDER BY rh.FechaProduccion DESC,rh.HoraInicio DESC,rh.RegistroHoraID DESC;";
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = lado.EjecucionProduccionID;
        await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await rd.ReadAsync(cancellationToken))
        {
            lista.Add(new ProduccionSeguimientoTecnicoCapturaVm
            {
                RegistroHoraID = Convert.ToInt32(rd["RegistroHoraID"]),
                EjecucionProduccionID = Convert.ToInt32(rd["EjecucionProduccionID"]),
                ProgramaProduccionID = Convert.ToInt32(rd["ProgramaProduccionID"]),
                NumeroOF = lado.NumeroOF,
                LadoLhRh = lado.LadoLhRh,
                OperadorID = SeguimientoNullableInt(rd, "OperadorID"),
                OperadorNombre = SeguimientoTexto(rd, "OperadorNombre"),
                FechaProduccion = Convert.ToDateTime(rd["FechaProduccion"]),
                HoraInicio = (TimeSpan)rd["HoraInicio"],
                HoraFin = (TimeSpan)rd["HoraFin"],
                CantidadOK = Convert.ToInt32(rd["CantidadOK"]),
                CantidadSospechosa = Convert.ToInt32(rd["CantidadSospechosa"]),
                CantidadScrap = Convert.ToInt32(rd["CantidadScrap"]),
                ObjetivoHora = SeguimientoNullableInt(rd, "ObjetivoHora"),
                ObjetivoBloque = SeguimientoNullableInt(rd, "ObjetivoBloque"),
                CumplioObjetivo = SeguimientoNullableBool(rd, "CumplioObjetivo"),
                DiferenciaObjetivo = SeguimientoNullableInt(rd, "DiferenciaObjetivo"),
                PorcentajeCumplimiento = SeguimientoNullableDecimal(rd, "PorcentajeCumplimiento"),
                PiezasCalculadasContador = SeguimientoNullableInt(rd, "PiezasCalculadasContador"),
                MinutosProductivos = SeguimientoNullableDecimal(rd, "MinutosProductivos"),
                TieneCambioConfiguracion = rd["TieneCambioConfiguracion"] != DBNull.Value && Convert.ToBoolean(rd["TieneCambioConfiguracion"]),
                TieneReinicioContador = rd["TieneReinicioContador"] != DBNull.Value && Convert.ToBoolean(rd["TieneReinicioContador"]),
                AjustadoPorCalidad = rd["AjustadoPorCalidad"] != DBNull.Value && Convert.ToBoolean(rd["AjustadoPorCalidad"]),
                Observaciones = SeguimientoTexto(rd, "Observaciones"),
                FechaCreacion = Convert.ToDateTime(rd["FechaCreacion"])
            });
        }
        return lista;
    }

    private static async Task<List<ProduccionSeguimientoTecnicoParoVm>> CargarParosSeguimientoTecnicoAsync(ProduccionSeguimientoTecnicoLadoVm lado, SqlConnection cn, CancellationToken cancellationToken)
    {
        var lista = new List<ProduccionSeguimientoTecnicoParoVm>();
        const string sql = @"
SELECT TOP(8)
    p.ParoID,p.EjecucionProduccionID,p.ProgramaProduccionID,p.FechaInicioParo,p.FechaFinParo,
    CASE WHEN p.FechaFinParo IS NOT NULL THEN ISNULL(p.DuracionMinutos,DATEDIFF(MINUTE,p.FechaInicioParo,p.FechaFinParo)) ELSE DATEDIFF(MINUTE,p.FechaInicioParo,GETDATE()) END AS DuracionMinutos,
    p.MotivoParoTexto,p.Descripcion,
    CAST(CASE WHEN ISNULL(p.EsMayorA15Minutos,0)=1 OR DATEDIFF(MINUTE,p.FechaInicioParo,ISNULL(p.FechaFinParo,GETDATE()))>15 THEN 1 ELSE 0 END AS BIT) AS EsMayorA15Minutos,
    CAST(ISNULL(p.EsInterrupcionUrgente,0) AS BIT) AS EsInterrupcionUrgente,
    p.ProgramaUrgenteID
FROM dbo.Produccion_Paros p
WHERE p.EjecucionProduccionID=@EjecucionProduccionID
  AND p.Activo=1
ORDER BY p.FechaInicioParo DESC,p.ParoID DESC;";
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = lado.EjecucionProduccionID;
        await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await rd.ReadAsync(cancellationToken))
        {
            lista.Add(new ProduccionSeguimientoTecnicoParoVm
            {
                ParoID = Convert.ToInt32(rd["ParoID"]),
                EjecucionProduccionID = Convert.ToInt32(rd["EjecucionProduccionID"]),
                ProgramaProduccionID = Convert.ToInt32(rd["ProgramaProduccionID"]),
                NumeroOF = lado.NumeroOF,
                LadoLhRh = lado.LadoLhRh,
                FechaInicioParo = Convert.ToDateTime(rd["FechaInicioParo"]),
                FechaFinParo = SeguimientoNullableDate(rd, "FechaFinParo"),
                DuracionMinutos = rd["DuracionMinutos"] == DBNull.Value ? 0 : Convert.ToInt32(rd["DuracionMinutos"]),
                MotivoParoTexto = SeguimientoTexto(rd, "MotivoParoTexto"),
                Descripcion = SeguimientoTexto(rd, "Descripcion"),
                EsMayorA15Minutos = rd["EsMayorA15Minutos"] != DBNull.Value && Convert.ToBoolean(rd["EsMayorA15Minutos"]),
                EsInterrupcionUrgente = rd["EsInterrupcionUrgente"] != DBNull.Value && Convert.ToBoolean(rd["EsInterrupcionUrgente"]),
                ProgramaUrgenteID = SeguimientoNullableInt(rd, "ProgramaUrgenteID")
            });
        }
        return lista;
    }

    private static string? SeguimientoTexto(SqlDataReader rd, string column)
    {
        var value = rd[column];
        return value == DBNull.Value ? null : value.ToString()?.Trim();
    }

    private static int? SeguimientoNullableInt(SqlDataReader rd, string column)
    {
        var value = rd[column];
        return value == DBNull.Value ? null : Convert.ToInt32(value);
    }

    private static long? SeguimientoNullableLong(SqlDataReader rd, string column)
    {
        var value = rd[column];
        return value == DBNull.Value ? null : Convert.ToInt64(value);
    }

    private static decimal? SeguimientoNullableDecimal(SqlDataReader rd, string column)
    {
        var value = rd[column];
        return value == DBNull.Value ? null : Convert.ToDecimal(value);
    }

    private static bool? SeguimientoNullableBool(SqlDataReader rd, string column)
    {
        var value = rd[column];
        return value == DBNull.Value ? null : Convert.ToBoolean(value);
    }

    private static DateTime? SeguimientoNullableDate(SqlDataReader rd, string column)
    {
        var value = rd[column];
        return value == DBNull.Value ? null : Convert.ToDateTime(value);
    }

    private sealed class SeguimientoContextoLado
    {
        public int ProgramaProduccionID { get; set; }
        public int? EjecucionProduccionID { get; set; }
        public string NumeroOF { get; set; } = string.Empty;
        public string? LadoLhRh { get; set; }
        public string? NumeroParte { get; set; }
        public string? ReferenciaSAP { get; set; }
        public string? DescripcionParte { get; set; }
    }
}
