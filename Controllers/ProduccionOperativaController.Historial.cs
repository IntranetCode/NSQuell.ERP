using ERP.NSQuell.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ERP.NSQuell.Controllers;

public sealed partial class ProduccionOperativaController
{
    [HttpGet("Historial/{programaProduccionId:int}")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> HistorialOperativo(int programaProduccionId, CancellationToken cancellationToken = default)
    {
        if (!UsuarioEnSesion()) return Unauthorized(new { ok = false, sesionExpirada = true, mensaje = "La sesión terminó. Vuelve a iniciar sesión." });
        if (programaProduccionId <= 0) return BadRequest(new { ok = false, mensaje = "El programa de Producción no es válido." });
        try
        {
            var centro = await ConstruirCentroOperativoAsync(programaProduccionId, ProduccionOperativaOrigen.Calendario, true, cancellationToken);
            if (centro == null) return NotFound(new { ok = false, mensaje = "No se encontró el programa de Producción solicitado." });
            if (!centro.Permisos.PuedeVerCentroOperativo) return StatusCode(StatusCodes.Status403Forbidden, new { ok = false, mensaje = "No tienes permiso para consultar esta producción." });
            var vm = new ProduccionHistorialOperativoVm
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
                EsParejaLhRh = centro.EsParejaLhRh,
                GrupoLhRh = centro.LhRh.GrupoLhRh,
                LadoActual = centro.LhRh.LadoActual,
                ProgramaParejaID = centro.LhRh.ProgramaParejaID,
                EjecucionParejaID = centro.LhRh.EjecucionParejaID,
                NumeroOFPareja = centro.LhRh.NumeroOFPareja,
                LadoPareja = centro.LhRh.LadoPareja,
                Pasos = centro.Pasos
                    .Where(x => x.Aplica)
                    .OrderBy(x => x.Orden)
                    .Select(x => new ProduccionHistorialOperativoPasoVm
                    {
                        Orden = x.Orden,
                        Clave = x.Clave,
                        Nombre = x.Nombre,
                        Etapa = x.Etapa,
                        AreaResponsable = x.AreaResponsable,
                        Estado = x.Estado,
                        Aplica = x.Aplica,
                        Completado = x.Completado,
                        EnProceso = x.EnProceso,
                        Bloqueado = x.Bloqueado,
                        Detalle = x.Detalle,
                        MotivoBloqueo = x.MotivoBloqueo,
                        FechaObjetivo = x.FechaObjetivo,
                        FechaInicioReal = x.FechaInicioReal,
                        FechaFinReal = x.FechaFinReal
                    }).ToList()
            };
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(cancellationToken);
            var contextos = new List<HistorialContextoPrograma>
            {
                new()
                {
                    ProgramaProduccionID=centro.ProgramaProduccionID,
                    EjecucionProduccionID=centro.EjecucionProduccionID,
                    NumeroOF=centro.OFTexto,
                    LadoLhRh=centro.EsParejaLhRh?centro.LhRh.LadoActual:null
                }
            };
            if (centro.EsParejaLhRh && centro.LhRh.ProgramaParejaID.HasValue && centro.LhRh.ProgramaParejaID.Value > 0)
            {
                contextos.Add(new HistorialContextoPrograma
                {
                    ProgramaProduccionID = centro.LhRh.ProgramaParejaID.Value,
                    EjecucionProduccionID = centro.LhRh.EjecucionParejaID,
                    NumeroOF = string.IsNullOrWhiteSpace(centro.LhRh.NumeroOFPareja) ? $"Programa {centro.LhRh.ProgramaParejaID.Value}" : centro.LhRh.NumeroOFPareja!,
                    LadoLhRh = centro.LhRh.LadoPareja
                });
            }
            foreach (var contexto in contextos)
            {
                if (contexto.EjecucionProduccionID.HasValue && contexto.EjecucionProduccionID.Value > 0)
                {
                    vm.CapturasHora.AddRange(await ObtenerCapturasHistorialOperativoAsync(contexto, cancellationToken, cn));
                }
            }
            vm.CapturasHora = vm.CapturasHora
                .GroupBy(x => x.RegistroHoraID)
                .Select(x => x.First())
                .OrderByDescending(x => x.FechaProduccion)
                .ThenByDescending(x => x.HoraInicio)
                .ThenByDescending(x => x.RegistroHoraID)
                .ToList();
            vm.Checklists = await ObtenerChecklistsHistorialOperativoAsync(contextos, cancellationToken, cn);
            return PartialView("~/Views/ProduccionOperativa/Acciones/_HistorialContenido.cshtml", vm);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al cargar historial operativo. ProgramaProduccionID: {ProgramaProduccionID}. UsuarioID: {UsuarioID}", programaProduccionId, ObtenerUsuarioID());
            return StatusCode(StatusCodes.Status500InternalServerError, new { ok = false, mensaje = "No fue posible cargar el historial operativo: " + ex.Message });
        }
    }

    private async Task<List<ProduccionHistorialOperativoCapturaVm>> ObtenerCapturasHistorialOperativoAsync(HistorialContextoPrograma contexto, CancellationToken cancellationToken, SqlConnection cn)
    {
        var lista = new List<ProduccionHistorialOperativoCapturaVm>();
        if (!contexto.EjecucionProduccionID.HasValue || contexto.EjecucionProduccionID.Value <= 0) return lista;
        const string sql = @"
SELECT
    rh.RegistroHoraID,
    rh.EjecucionProduccionID,
    rh.ProgramaProduccionID,
    rh.OperadorID,
    rh.FechaProduccion,
    rh.HoraInicio,
    rh.HoraFin,
    ISNULL(rh.CantidadOK,0) AS CantidadOK,
    ISNULL(rh.CantidadSospechosa,0) AS CantidadSospechosa,
    ISNULL(rh.CantidadScrap,0) AS CantidadScrap,
    rh.ObjetivoHora,
    rh.ObjetivoBloque,
    rh.CumplioObjetivo,
    rh.DiferenciaObjetivo,
    rh.PorcentajeCumplimiento,
    rh.PiezasCalculadasContador,
    rh.MinutosProductivos,
    ISNULL(rh.EsTiempoExtra,0) AS EsTiempoExtra,
    rh.TipoBloque,
    ISNULL(rh.TieneCambioConfiguracion,0) AS TieneCambioConfiguracion,
    ISNULL(rh.TieneReinicioContador,0) AS TieneReinicioContador,
    ISNULL(rh.AjustadoPorCalidad,0) AS AjustadoPorCalidad,
    rh.Observaciones,
    rh.FechaCreacion,
    LTRIM(RTRIM(CONCAT(ISNULL(p.Nombre,N''),N' ',ISNULL(p.ApellidoPaterno,N''),N' ',ISNULL(p.ApellidoMaterno,N'')))) AS OperadorNombre
FROM dbo.Produccion_RegistroHora rh
LEFT JOIN dbo.Persona p ON p.PersonaID=rh.OperadorID
WHERE rh.EjecucionProduccionID=@EjecucionProduccionID
  AND rh.Activo=1
ORDER BY rh.FechaProduccion DESC,rh.HoraInicio DESC,rh.RegistroHoraID DESC;";
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = contexto.EjecucionProduccionID.Value;
        await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await rd.ReadAsync(cancellationToken))
        {
            lista.Add(new ProduccionHistorialOperativoCapturaVm
            {
                RegistroHoraID = Convert.ToInt32(rd["RegistroHoraID"]),
                EjecucionProduccionID = Convert.ToInt32(rd["EjecucionProduccionID"]),
                ProgramaProduccionID = Convert.ToInt32(rd["ProgramaProduccionID"]),
                NumeroOF = contexto.NumeroOF,
                LadoLhRh = contexto.LadoLhRh,
                OperadorID = HistorialNullableInt(rd, "OperadorID"),
                OperadorNombre = HistorialTexto(rd, "OperadorNombre"),
                FechaProduccion = Convert.ToDateTime(rd["FechaProduccion"]),
                HoraInicio = (TimeSpan)rd["HoraInicio"],
                HoraFin = (TimeSpan)rd["HoraFin"],
                CantidadOK = Convert.ToInt32(rd["CantidadOK"]),
                CantidadSospechosa = Convert.ToInt32(rd["CantidadSospechosa"]),
                CantidadScrap = Convert.ToInt32(rd["CantidadScrap"]),
                ObjetivoHora = HistorialNullableInt(rd, "ObjetivoHora"),
                ObjetivoBloque = HistorialNullableInt(rd, "ObjetivoBloque"),
                CumplioObjetivo = HistorialNullableBool(rd, "CumplioObjetivo"),
                DiferenciaObjetivo = HistorialNullableInt(rd, "DiferenciaObjetivo"),
                PorcentajeCumplimiento = HistorialNullableDecimal(rd, "PorcentajeCumplimiento"),
                PiezasCalculadasContador = HistorialNullableInt(rd, "PiezasCalculadasContador"),
                MinutosProductivos = HistorialNullableDecimal(rd, "MinutosProductivos"),
                EsTiempoExtra = Convert.ToBoolean(rd["EsTiempoExtra"]),
                TipoBloque = HistorialTexto(rd, "TipoBloque"),
                TieneCambioConfiguracion = Convert.ToBoolean(rd["TieneCambioConfiguracion"]),
                TieneReinicioContador = Convert.ToBoolean(rd["TieneReinicioContador"]),
                AjustadoPorCalidad = Convert.ToBoolean(rd["AjustadoPorCalidad"]),
                Observaciones = HistorialTexto(rd, "Observaciones"),
                FechaCreacion = Convert.ToDateTime(rd["FechaCreacion"])
            });
        }
        return lista;
    }

    private async Task<List<ProduccionHistorialOperativoChecklistVm>> ObtenerChecklistsHistorialOperativoAsync(List<HistorialContextoPrograma> contextos, CancellationToken cancellationToken, SqlConnection cn)
    {
        var lista = new List<ProduccionHistorialOperativoChecklistVm>();
        var tablaVinculo = await ResolverTablaVinculoHistorialChecklistAsync(cancellationToken, cn);
        foreach (var contexto in contextos)
        {
            var sql = tablaVinculo == null
                ? @"
SELECT DISTINCT TOP(30)
    c.ChecklistArranqueID,c.ProgramaProduccionID,c.EjecucionProduccionID,c.CodigoFormato,c.VersionFormato,c.TipoChecklist,c.MomentoProceso,
    c.EstadoFlujo,c.EstatusID,c.ObservacionesGenerales,c.ObservacionesCalidad,c.FechaCapturaProduccion,c.FechaValidacionCalidad,c.FechaCreacion,c.FechaModificacion
FROM dbo.Produccion_ChecklistArranque c
WHERE c.Activo=1
  AND (c.ProgramaProduccionID=@ProgramaProduccionID OR (@EjecucionProduccionID IS NOT NULL AND c.EjecucionProduccionID=@EjecucionProduccionID))
ORDER BY c.ChecklistArranqueID DESC;"
                : $@"
SELECT DISTINCT TOP(30)
    c.ChecklistArranqueID,c.ProgramaProduccionID,c.EjecucionProduccionID,c.CodigoFormato,c.VersionFormato,c.TipoChecklist,c.MomentoProceso,
    c.EstadoFlujo,c.EstatusID,c.ObservacionesGenerales,c.ObservacionesCalidad,c.FechaCapturaProduccion,c.FechaValidacionCalidad,c.FechaCreacion,c.FechaModificacion
FROM dbo.Produccion_ChecklistArranque c
LEFT JOIN {tablaVinculo} cp ON cp.ChecklistArranqueID=c.ChecklistArranqueID AND cp.Activo=1
WHERE c.Activo=1
  AND
  (
      c.ProgramaProduccionID=@ProgramaProduccionID
      OR (@EjecucionProduccionID IS NOT NULL AND c.EjecucionProduccionID=@EjecucionProduccionID)
      OR cp.ProgramaProduccionID=@ProgramaProduccionID
      OR (@EjecucionProduccionID IS NOT NULL AND cp.EjecucionProduccionID=@EjecucionProduccionID)
  )
ORDER BY c.ChecklistArranqueID DESC;";
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = contexto.ProgramaProduccionID;
            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = contexto.EjecucionProduccionID.HasValue ? contexto.EjecucionProduccionID.Value : DBNull.Value;
            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await rd.ReadAsync(cancellationToken))
            {
                var id = Convert.ToInt32(rd["ChecklistArranqueID"]);
                if (lista.Any(x => x.ChecklistArranqueID == id)) continue;
                lista.Add(new ProduccionHistorialOperativoChecklistVm
                {
                    ChecklistArranqueID = id,
                    ProgramaProduccionID = HistorialNullableInt(rd, "ProgramaProduccionID"),
                    EjecucionProduccionID = HistorialNullableInt(rd, "EjecucionProduccionID"),
                    CodigoFormato = HistorialTexto(rd, "CodigoFormato") ?? string.Empty,
                    VersionFormato = HistorialTexto(rd, "VersionFormato"),
                    TipoChecklist = HistorialTexto(rd, "TipoChecklist"),
                    MomentoProceso = HistorialTexto(rd, "MomentoProceso"),
                    EstadoFlujo = HistorialTexto(rd, "EstadoFlujo"),
                    EstatusID = rd["EstatusID"] == DBNull.Value ? 0 : Convert.ToInt32(rd["EstatusID"]),
                    ObservacionesGenerales = HistorialTexto(rd, "ObservacionesGenerales"),
                    ObservacionesCalidad = HistorialTexto(rd, "ObservacionesCalidad"),
                    FechaCapturaProduccion = HistorialNullableDate(rd, "FechaCapturaProduccion"),
                    FechaValidacionCalidad = HistorialNullableDate(rd, "FechaValidacionCalidad"),
                    FechaCreacion = Convert.ToDateTime(rd["FechaCreacion"]),
                    FechaModificacion = HistorialNullableDate(rd, "FechaModificacion")
                });
            }
        }
        foreach (var checklist in lista)
        {
            if (tablaVinculo != null)
            {
                checklist.ProgramasVinculados = await ContarProgramasChecklistHistorialAsync(checklist.ChecklistArranqueID, tablaVinculo, cancellationToken, cn);
                checklist.EsCompartidoLhRh = checklist.ProgramasVinculados > 1;
            }
            checklist.Preguntas = await CargarPreguntasHistorialChecklistAsync(checklist, cancellationToken, cn);
        }
        return lista.OrderByDescending(x => x.FechaCreacion).ThenByDescending(x => x.ChecklistArranqueID).ToList();
    }

    private static async Task<List<ProduccionHistorialOperativoPreguntaVm>> CargarPreguntasHistorialChecklistAsync(ProduccionHistorialOperativoChecklistVm checklist, CancellationToken cancellationToken, SqlConnection cn)
    {
        var lista = new List<ProduccionHistorialOperativoPreguntaVm>();
        const string sql = @"
SELECT
    p.PreguntaID,p.Seccion,p.OrdenSeccion,p.OrdenPregunta,p.TextoPregunta,p.TipoRespuesta,p.ResponsableSugerido,
    d.Resultado,d.ValorCapturado,d.Observaciones,d.Confirmado,d.UsuarioRespuestaID,d.FechaRespuesta,d.Unidad,d.Especificacion,d.Tolerancia,
    LTRIM(RTRIM(CONCAT(ISNULL(per.Nombre,N''),N' ',ISNULL(per.ApellidoPaterno,N''),N' ',ISNULL(per.ApellidoMaterno,N'')))) AS UsuarioRespuestaNombre
FROM dbo.ERP_ChecklistArranquePreguntas p
OUTER APPLY
(
    SELECT TOP(1)
        det.Resultado,det.ValorCapturado,det.Observaciones,det.Confirmado,det.UsuarioRespuestaID,det.FechaRespuesta,det.Unidad,det.Especificacion,det.Tolerancia
    FROM dbo.Produccion_ChecklistArranqueDetalle det
    WHERE det.ChecklistArranqueID=@ChecklistArranqueID
      AND det.PreguntaID=p.PreguntaID
      AND det.Activo=1
    ORDER BY det.ChecklistArranqueDetalleID DESC
) d
LEFT JOIN dbo.Usuarios u ON u.UsuarioID=d.UsuarioRespuestaID
LEFT JOIN dbo.Persona per ON per.PersonaID=u.PersonaID
WHERE p.Activo=1
  AND p.CodigoFormato=@CodigoFormato
  AND ISNULL(p.VersionFormato,N'')=ISNULL(@VersionFormato,N'')
ORDER BY p.OrdenSeccion,p.OrdenPregunta,p.PreguntaID;";
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add("@ChecklistArranqueID", SqlDbType.Int).Value = checklist.ChecklistArranqueID;
        cmd.Parameters.Add("@CodigoFormato", SqlDbType.NVarChar, 100).Value = checklist.CodigoFormato;
        cmd.Parameters.Add("@VersionFormato", SqlDbType.NVarChar, 60).Value = string.IsNullOrWhiteSpace(checklist.VersionFormato) ? DBNull.Value : checklist.VersionFormato;
        await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await rd.ReadAsync(cancellationToken))
        {
            lista.Add(new ProduccionHistorialOperativoPreguntaVm
            {
                PreguntaID = Convert.ToInt32(rd["PreguntaID"]),
                Seccion = HistorialTexto(rd, "Seccion") ?? "General",
                OrdenSeccion = rd["OrdenSeccion"] == DBNull.Value ? 0 : Convert.ToInt32(rd["OrdenSeccion"]),
                OrdenPregunta = rd["OrdenPregunta"] == DBNull.Value ? 0 : Convert.ToInt32(rd["OrdenPregunta"]),
                TextoPregunta = HistorialTexto(rd, "TextoPregunta") ?? string.Empty,
                TipoRespuesta = HistorialTexto(rd, "TipoRespuesta"),
                ResponsableSugerido = HistorialTexto(rd, "ResponsableSugerido"),
                Resultado = HistorialTexto(rd, "Resultado"),
                ValorCapturado = HistorialTexto(rd, "ValorCapturado"),
                Observaciones = HistorialTexto(rd, "Observaciones"),
                Confirmado = rd["Confirmado"] != DBNull.Value && Convert.ToBoolean(rd["Confirmado"]),
                UsuarioRespuestaID = HistorialNullableInt(rd, "UsuarioRespuestaID"),
                UsuarioRespuestaNombre = HistorialTexto(rd, "UsuarioRespuestaNombre"),
                FechaRespuesta = HistorialNullableDate(rd, "FechaRespuesta"),
                Unidad = HistorialTexto(rd, "Unidad"),
                Especificacion = HistorialTexto(rd, "Especificacion"),
                Tolerancia = HistorialTexto(rd, "Tolerancia")
            });
        }
        return lista;
    }

    private static async Task<string?> ResolverTablaVinculoHistorialChecklistAsync(CancellationToken cancellationToken, SqlConnection cn)
    {
        const string sql = @"
SELECT CASE
    WHEN OBJECT_ID(N'dbo.Produccion_ChecklistArranqueProgramas',N'U') IS NOT NULL THEN N'dbo.Produccion_ChecklistArranqueProgramas'
    WHEN OBJECT_ID(N'dbo.Produccion_ChecklistArranquePrograma',N'U') IS NOT NULL THEN N'dbo.Produccion_ChecklistArranquePrograma'
    WHEN OBJECT_ID(N'dbo.Produccion_ChecklistProgramas',N'U') IS NOT NULL THEN N'dbo.Produccion_ChecklistProgramas'
    ELSE NULL
END;";
        await using var cmd = new SqlCommand(sql, cn);
        var value = await cmd.ExecuteScalarAsync(cancellationToken);
        return value == null || value == DBNull.Value ? null : value.ToString();
    }

    private static async Task<int> ContarProgramasChecklistHistorialAsync(int checklistArranqueId, string tablaVinculo, CancellationToken cancellationToken, SqlConnection cn)
    {
        var sql = $@"
SELECT COUNT(DISTINCT ProgramaProduccionID)
FROM {tablaVinculo}
WHERE ChecklistArranqueID=@ChecklistArranqueID
  AND Activo=1
  AND ProgramaProduccionID IS NOT NULL;";
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add("@ChecklistArranqueID", SqlDbType.Int).Value = checklistArranqueId;
        var value = await cmd.ExecuteScalarAsync(cancellationToken);
        return value == null || value == DBNull.Value ? 0 : Convert.ToInt32(value);
    }

    private static string? HistorialTexto(SqlDataReader rd, string column)
    {
        var value = rd[column];
        return value == DBNull.Value ? null : value.ToString()?.Trim();
    }

    private static int? HistorialNullableInt(SqlDataReader rd, string column)
    {
        var value = rd[column];
        return value == DBNull.Value ? null : Convert.ToInt32(value);
    }

    private static bool? HistorialNullableBool(SqlDataReader rd, string column)
    {
        var value = rd[column];
        return value == DBNull.Value ? null : Convert.ToBoolean(value);
    }

    private static decimal? HistorialNullableDecimal(SqlDataReader rd, string column)
    {
        var value = rd[column];
        return value == DBNull.Value ? null : Convert.ToDecimal(value);
    }

    private static DateTime? HistorialNullableDate(SqlDataReader rd, string column)
    {
        var value = rd[column];
        return value == DBNull.Value ? null : Convert.ToDateTime(value);
    }

    private sealed class HistorialContextoPrograma
    {
        public int ProgramaProduccionID { get; set; }
        public int? EjecucionProduccionID { get; set; }
        public string NumeroOF { get; set; } = string.Empty;
        public string? LadoLhRh { get; set; }
    }
}
