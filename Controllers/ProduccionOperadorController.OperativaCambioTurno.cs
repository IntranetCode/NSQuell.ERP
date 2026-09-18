using ERP.NSQuell.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace ERP.NSQuell.Controllers;

public sealed partial class ProduccionOperadorController
{
    private const int VentanaPreviaCambioTurnoOperadorMinutos = 15;

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> CambioTurnoOperativo(int ejecucionProduccionId, bool soloLectura = false)
    {
        if (!UsuarioEnSesion())
            return Unauthorized(new { ok = false, sesionExpirada = true, mensaje = "La sesión terminó. Vuelve a iniciar sesión." });
        if (ejecucionProduccionId <= 0)
            return BadRequest(new { ok = false, mensaje = "La ejecución de Producción no es válida." });

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync();

        var contexto = await ObtenerContextoCambioTurnoOperativoAsync(ejecucionProduccionId, cn);
        if (contexto == null)
            return NotFound(new { ok = false, mensaje = "No se encontró la ejecución de Producción." });

        var usuarioId = ObtenerUsuarioID();
        var personaUsuarioId = await ObtenerPersonaIDUsuarioAsync(usuarioId, cn);
        var usuarioEsOperador = await UsuarioEsOperadorAsync(usuarioId, cn);
        var programacion = await ObtenerProgramacionCambioTurnoOperativoAsync(
            contexto.ProgramaProduccionID,
            contexto.OperadorActualID,
            DateTime.Now,
            cn);

        var historial = (await ObtenerHistorialCambiosTurnoAsync(ejecucionProduccionId, cn))
            .OrderByDescending(x => x.FechaEntrega)
            .ThenByDescending(x => x.CambioTurnoID)
            .ToList();

        var pendiente = historial.FirstOrDefault(x =>
            string.Equals(x.Estado, ProduccionCambioTurnoEstado.PendienteRecepcion, StringComparison.OrdinalIgnoreCase));

        var resumen = await ConstruirResumenCambioTurnoAsync(
            ejecucionProduccionId,
            contexto.OperadorActualID ?? 0,
            cn);

        var vm = new ProduccionCambioTurnoOperativoVm
        {
            EjecucionProduccionID = contexto.EjecucionProduccionID,
            ProgramaProduccionID = contexto.ProgramaProduccionID,
            OFTexto = contexto.OFTexto,
            EstatusID = contexto.EstatusID,
            MaquinaID = contexto.MaquinaID,
            MaquinaCodigo = contexto.MaquinaCodigo,
            MaquinaNombre = contexto.MaquinaNombre,
            ParteID = contexto.ParteID,
            NumeroParte = contexto.NumeroParte,
            ReferenciaSAP = contexto.ReferenciaSAP,
            OperadorActualID = contexto.OperadorActualID,
            OperadorActualNombre = contexto.OperadorActualNombre,
            FechaConsulta = DateTime.Now,
            RequiereCambioTurno = pendiente != null || programacion?.RequiereCambio == true,
            CambioProgramadoIniciado = programacion?.YaInicio == true,
            MinutosParaCambio = programacion?.MinutosParaCambio ?? 0,
            TurnoProgramadoID = programacion?.TurnoID,
            TurnoProgramadoNombre = programacion?.TurnoNombre,
            InicioTurnoProgramado = programacion?.Inicio,
            FinTurnoProgramado = programacion?.Fin,
            OperadorProgramadoID = programacion?.OperadorID,
            OperadorProgramadoNombre = programacion?.OperadorNombre,
            Resumen = resumen,
            CambioPendiente = pendiente,
            HistorialReciente = historial.Take(6).ToList(),
            UsuarioID = usuarioId,
            PersonaUsuarioID = personaUsuarioId,
            UsuarioEsOperador = usuarioEsOperador,
            SoloLecturaSolicitado = soloLectura
        };

        var pareja = await ObtenerParejaLhRhOperadorAsync(contexto.ProgramaProduccionID, cn);
        if (pareja != null)
        {
            vm.EsLhRh = true;
            vm.GrupoLhRh = pareja.GrupoLhRh;
            vm.EjecucionParejaID = pareja.EjecucionParejaID;
            vm.ProgramaParejaID = pareja.ProgramaParejaID;
            vm.OFParejaTexto = ObtenerOFParejaSeguraCambioTurno(pareja);
            vm.ParteParejaTexto = ObtenerParteParejaSeguraCambioTurno(pareja);

            try
            {
                ValidarParejaLhRhOperador(pareja);
            }
            catch (Exception ex)
            {
                vm.ParejaConsistente = false;
                vm.MotivoInconsistenciaPareja = SanitizarMensajeParejaCambioTurno(ex.Message, pareja);
            }

            if (vm.ParejaConsistente && pareja.EjecucionParejaID.HasValue && pareja.EjecucionParejaID.Value > 0)
            {
                var resumenPareja = await ConstruirResumenCambioTurnoAsync(
                    pareja.EjecucionParejaID.Value,
                    contexto.OperadorActualID ?? 0,
                    cn);
                vm.ResumenPareja = resumenPareja;
                AjustarResumenCambioTurnoLhRhOperativo(vm, resumen, resumenPareja);
            }
            else if (resumen != null)
            {
                resumen.PuedeEntregar = false;
                resumen.MotivoBloqueo = vm.MotivoInconsistenciaPareja ?? "La pareja LH/RH no está lista para entregar turno.";
            }
        }

        return PartialView("~/Views/ProduccionOperativa/Acciones/_CambioTurnoContenido.cshtml", vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EntregarTurnoOperativo(ProduccionCambioTurnoEntregaPostVm vm)
    {
        if (!UsuarioEnSesion())
            return Unauthorized(new { ok = false, sesionExpirada = true, mensaje = "La sesión terminó. Vuelve a iniciar sesión." });
        if (vm.EjecucionProduccionID <= 0 || vm.OperadorEntranteID <= 0)
            return BadRequest(new { ok = false, mensaje = "Selecciona correctamente al operador que recibirá el turno." });

        string? referenciaParejaVieja = null;
        string? referenciaParejaSegura = null;

        await using (var cn = new SqlConnection(ConnectionString))
        {
            await cn.OpenAsync();
            var usuarioId = ObtenerUsuarioID();
            if (!await UsuarioEsOperadorAsync(usuarioId, cn))
                return StatusCode(StatusCodes.Status403Forbidden, new { ok = false, mensaje = "Solo el operador responsable puede entregar el turno." });

            var personaId = await ObtenerPersonaIDUsuarioAsync(usuarioId, cn);
            if (!personaId.HasValue || personaId.Value <= 0)
                return Unauthorized(new { ok = false, mensaje = "No fue posible identificar a la persona vinculada con la sesión." });

            var contexto = await ObtenerContextoCambioTurnoOperativoAsync(vm.EjecucionProduccionID, cn);
            if (contexto == null)
                return NotFound(new { ok = false, mensaje = "No se encontró la ejecución de Producción." });
            if (contexto.OperadorActualID != personaId.Value)
                return StatusCode(StatusCodes.Status403Forbidden, new { ok = false, mensaje = "La ejecución ya no está asignada al operador conectado." });

            var programacion = await ObtenerProgramacionCambioTurnoOperativoAsync(
                contexto.ProgramaProduccionID,
                contexto.OperadorActualID,
                DateTime.Now,
                cn);
            if (programacion?.RequiereCambio != true)
                return BadRequest(new { ok = false, mensaje = "No hay un cambio de turno programado que deba atenderse en este momento. Para un relevo extraordinario utiliza el flujo de relevo operativo." });

            var pareja = await ObtenerParejaLhRhOperadorAsync(contexto.ProgramaProduccionID, cn);
            if (pareja != null)
            {
                referenciaParejaVieja = pareja.OFParejaTexto;
                referenciaParejaSegura = ObtenerOFParejaSeguraCambioTurno(pareja);
            }
        }

        LimpiarMensajesCambioTurnoOperativo();
        IActionResult resultado;
        try
        {
            resultado = await EntregarTurno(vm);
        }
        catch (Exception ex)
        {
            var mensaje = SanitizarTextoParejaCambioTurno(ex.Message, referenciaParejaVieja, referenciaParejaSegura);
            return BadRequest(new { ok = false, mensaje = "No fue posible entregar el turno: " + mensaje });
        }

        return ConvertirResultadoCambioTurnoOperativo(
            resultado,
            "Turno entregado. Está pendiente de recepción por el operador entrante.",
            referenciaParejaVieja,
            referenciaParejaSegura);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RecibirTurnoOperativo(ProduccionCambioTurnoRecepcionPostVm vm)
    {
        if (!UsuarioEnSesion())
            return Unauthorized(new { ok = false, sesionExpirada = true, mensaje = "La sesión terminó. Vuelve a iniciar sesión." });
        if (vm.CambioTurnoID <= 0)
            return BadRequest(new { ok = false, mensaje = "No se recibió correctamente la entrega de turno." });

        string? referenciaParejaVieja = null;
        string? referenciaParejaSegura = null;

        await using (var cn = new SqlConnection(ConnectionString))
        {
            await cn.OpenAsync();
            var usuarioId = ObtenerUsuarioID();
            if (!await UsuarioEsOperadorAsync(usuarioId, cn))
                return StatusCode(StatusCodes.Status403Forbidden, new { ok = false, mensaje = "Solo el operador entrante puede recibir el turno." });

            var personaId = await ObtenerPersonaIDUsuarioAsync(usuarioId, cn);
            if (!personaId.HasValue || personaId.Value <= 0)
                return Unauthorized(new { ok = false, mensaje = "No fue posible identificar a la persona vinculada con la sesión." });

            const string sql = @"
SELECT TOP(1)
    ct.EjecucionProduccionID,
    ct.ProgramaProduccionID,
    ct.OperadorEntranteID
FROM dbo.Produccion_CambiosTurno ct
WHERE ct.CambioTurnoID=@CambioTurnoID
  AND ct.EstadoCambioTurno=N'PENDIENTE_RECEPCION'
  AND ct.Activo=1;";
            int? programaId = null;
            await using (var cmd = new SqlCommand(sql, cn))
            {
                cmd.Parameters.Add("@CambioTurnoID", SqlDbType.Int).Value = vm.CambioTurnoID;
                await using var rd = await cmd.ExecuteReaderAsync();
                if (!await rd.ReadAsync())
                    return BadRequest(new { ok = false, mensaje = "Esta entrega ya fue atendida, cancelada o dejó de estar pendiente." });
                if (Convert.ToInt32(rd["OperadorEntranteID"]) != personaId.Value)
                    return StatusCode(StatusCodes.Status403Forbidden, new { ok = false, mensaje = "Esta entrega de turno está asignada a otro operador." });
                programaId = Convert.ToInt32(rd["ProgramaProduccionID"]);
            }

            if (programaId.HasValue)
            {
                var pareja = await ObtenerParejaLhRhOperadorAsync(programaId.Value, cn);
                if (pareja != null)
                {
                    referenciaParejaVieja = pareja.OFParejaTexto;
                    referenciaParejaSegura = ObtenerOFParejaSeguraCambioTurno(pareja);
                }
            }
        }

        LimpiarMensajesCambioTurnoOperativo();
        IActionResult resultado;
        try
        {
            resultado = await RecibirTurno(vm);
        }
        catch (Exception ex)
        {
            var mensaje = SanitizarTextoParejaCambioTurno(ex.Message, referenciaParejaVieja, referenciaParejaSegura);
            return BadRequest(new { ok = false, mensaje = "No fue posible recibir el turno: " + mensaje });
        }

        return ConvertirResultadoCambioTurnoOperativo(
            resultado,
            "Turno recibido correctamente. La responsabilidad quedó actualizada.",
            referenciaParejaVieja,
            referenciaParejaSegura);
    }

    private async Task<ContextoCambioTurnoOperativo?> ObtenerContextoCambioTurnoOperativoAsync(
        int ejecucionProduccionId,
        SqlConnection cn)
    {
        const string sql = @"
SELECT TOP(1)
    e.EjecucionProduccionID,
    e.ProgramaProduccionID,
    e.EstatusID,
    e.MaquinaID,
    e.MaquinaCodigo,
    e.MaquinaNombre,
    e.ParteID,
    e.NumeroParte,
    e.ReferenciaSAP,
    e.OperadorID,
    e.OperadorNombre,
    s.NumeroOFRecibida,
    s.FolioSolicitud
FROM dbo.Produccion_Ejecucion e
LEFT JOIN dbo.SolicitudesProduccion s
    ON s.SolicitudProduccionID=e.SolicitudProduccionID
   AND s.Activo=1
WHERE e.EjecucionProduccionID=@EjecucionProduccionID
  AND e.Activo=1;";

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
        await using var rd = await cmd.ExecuteReaderAsync();
        if (!await rd.ReadAsync()) return null;

        var programaId = Convert.ToInt32(rd["ProgramaProduccionID"]);
        var numeroOf = rd["NumeroOFRecibida"] == DBNull.Value ? null : rd["NumeroOFRecibida"]?.ToString()?.Trim();
        var folio = rd["FolioSolicitud"] == DBNull.Value ? null : rd["FolioSolicitud"]?.ToString()?.Trim();

        return new ContextoCambioTurnoOperativo
        {
            EjecucionProduccionID = Convert.ToInt32(rd["EjecucionProduccionID"]),
            ProgramaProduccionID = programaId,
            OFTexto = !string.IsNullOrWhiteSpace(numeroOf)
                ? numeroOf!
                : !string.IsNullOrWhiteSpace(folio)
                    ? folio!
                    : $"Programa {programaId}",
            EstatusID = Convert.ToInt32(rd["EstatusID"]),
            MaquinaID = rd["MaquinaID"] == DBNull.Value ? null : Convert.ToInt32(rd["MaquinaID"]),
            MaquinaCodigo = rd["MaquinaCodigo"] == DBNull.Value ? null : rd["MaquinaCodigo"]?.ToString()?.Trim(),
            MaquinaNombre = rd["MaquinaNombre"] == DBNull.Value ? null : rd["MaquinaNombre"]?.ToString()?.Trim(),
            ParteID = rd["ParteID"] == DBNull.Value ? null : Convert.ToInt32(rd["ParteID"]),
            NumeroParte = rd["NumeroParte"] == DBNull.Value ? null : rd["NumeroParte"]?.ToString()?.Trim(),
            ReferenciaSAP = rd["ReferenciaSAP"] == DBNull.Value ? null : rd["ReferenciaSAP"]?.ToString()?.Trim(),
            OperadorActualID = rd["OperadorID"] == DBNull.Value ? null : Convert.ToInt32(rd["OperadorID"]),
            OperadorActualNombre = rd["OperadorNombre"] == DBNull.Value ? null : rd["OperadorNombre"]?.ToString()?.Trim()
        };
    }

    private async Task<ProgramacionCambioTurnoOperativo?> ObtenerProgramacionCambioTurnoOperativoAsync(
        int programaProduccionId,
        int? operadorActualId,
        DateTime ahora,
        SqlConnection cn)
    {
        const string sqlExiste = @"
SELECT CONVERT(bit,CASE WHEN OBJECT_ID(N'dbo.Produccion_ProgramaPersonalAsignaciones',N'U') IS NULL THEN 0 ELSE 1 END);";
        await using (var cmd = new SqlCommand(sqlExiste, cn))
        {
            if (!Convert.ToBoolean(await cmd.ExecuteScalarAsync() ?? false)) return null;
        }

        const string sql = @"
SELECT TOP(1)
    a.TurnoID,
    a.TurnoNombre,
    a.Inicio,
    a.Fin,
    a.OperadorID,
    LTRIM(RTRIM(CONCAT(ISNULL(p.Nombre,N''),N' ',ISNULL(p.ApellidoPaterno,N''),N' ',ISNULL(p.ApellidoMaterno,N'')))) AS OperadorNombre
FROM dbo.Produccion_ProgramaPersonalAsignaciones a
LEFT JOIN dbo.Persona p
    ON p.PersonaID=a.OperadorID
WHERE a.ProgramaProduccionID=@ProgramaProduccionID
  AND a.Activo=1
  AND a.Fin>@Ahora
  AND a.Inicio<=DATEADD(MINUTE,@VentanaMinutos,@Ahora)
  AND
  (
      a.OperadorID IS NULL
      OR @OperadorActualID IS NULL
      OR a.OperadorID<>@OperadorActualID
  )
ORDER BY
    CASE WHEN a.Inicio<=@Ahora THEN 0 ELSE 1 END,
    a.Inicio,
    a.AsignacionPersonalID;";

        await using var consulta = new SqlCommand(sql, cn);
        consulta.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaProduccionId;
        consulta.Parameters.Add("@Ahora", SqlDbType.DateTime2).Value = ahora;
        consulta.Parameters.Add("@VentanaMinutos", SqlDbType.Int).Value = VentanaPreviaCambioTurnoOperadorMinutos;
        consulta.Parameters.Add("@OperadorActualID", SqlDbType.Int).Value = operadorActualId.HasValue ? (object)operadorActualId.Value : DBNull.Value;
        await using var rd = await consulta.ExecuteReaderAsync();
        if (!await rd.ReadAsync()) return null;

        var inicio = Convert.ToDateTime(rd["Inicio"]);
        return new ProgramacionCambioTurnoOperativo
        {
            RequiereCambio = true,
            YaInicio = inicio <= ahora,
            MinutosParaCambio = inicio <= ahora ? 0 : Math.Max(0, (int)Math.Ceiling((inicio - ahora).TotalMinutes)),
            TurnoID = rd["TurnoID"] == DBNull.Value ? null : Convert.ToInt32(rd["TurnoID"]),
            TurnoNombre = rd["TurnoNombre"] == DBNull.Value ? null : rd["TurnoNombre"]?.ToString()?.Trim(),
            Inicio = inicio,
            Fin = Convert.ToDateTime(rd["Fin"]),
            OperadorID = rd["OperadorID"] == DBNull.Value ? null : Convert.ToInt32(rd["OperadorID"]),
            OperadorNombre = rd["OperadorNombre"] == DBNull.Value ? null : rd["OperadorNombre"]?.ToString()?.Trim()
        };
    }

    private static void AjustarResumenCambioTurnoLhRhOperativo(
        ProduccionCambioTurnoOperativoVm vm,
        ProduccionCambioTurnoResumenVm? resumen,
        ProduccionCambioTurnoResumenVm? resumenPareja)
    {
        if (resumen == null) return;
        if (resumenPareja == null)
        {
            resumen.PuedeEntregar = false;
            resumen.MotivoBloqueo = $"No fue posible cargar {vm.OFParejaTexto ?? "la OF pareja"}.";
            return;
        }

        if (!resumen.PuedeEntregar || !resumenPareja.PuedeEntregar)
        {
            var motivoActual = resumen.MotivoBloqueo;
            resumen.PuedeEntregar = false;
            resumen.MotivoBloqueo = !string.IsNullOrWhiteSpace(motivoActual)
                ? motivoActual
                : resumenPareja.MotivoBloqueo ?? "La pareja LH/RH no está lista para entregar turno.";
        }

        var idsPareja = resumenPareja.Operadores.Select(x => x.PersonaID).ToHashSet();
        resumen.Operadores = resumen.Operadores.Where(x => idsPareja.Contains(x.PersonaID)).ToList();

        if (resumen.Operadores.Count == 0)
        {
            resumen.PuedeEntregar = false;
            resumen.MotivoBloqueo = "No existe un operador autorizado simultáneamente para las dos OF LH/RH.";
            resumen.OperadorSugeridoID = null;
            resumen.OperadorSugeridoNombre = null;
            resumen.SugeridoPorTecnico = false;
        }
        else if (resumen.OperadorSugeridoID.HasValue &&
                !resumen.Operadores.Any(x => x.PersonaID == resumen.OperadorSugeridoID.Value))
        {
            resumen.OperadorSugeridoID = null;
            resumen.OperadorSugeridoNombre = null;
            resumen.SugeridoPorTecnico = false;
        }
    }

    private IActionResult ConvertirResultadoCambioTurnoOperativo(
        IActionResult resultado,
        string mensajeExitoDefault,
        string? referenciaParejaVieja,
        string? referenciaParejaSegura)
    {
        var error = TempData["Error"]?.ToString();
        var success = TempData["Success"]?.ToString();
        TempData.Remove("Error");
        TempData.Remove("Success");

        error = SanitizarTextoParejaCambioTurno(error, referenciaParejaVieja, referenciaParejaSegura);
        success = SanitizarTextoParejaCambioTurno(success, referenciaParejaVieja, referenciaParejaSegura);

        if (!string.IsNullOrWhiteSpace(error))
            return BadRequest(new { ok = false, mensaje = error });

        if (resultado is NotFoundResult)
            return NotFound(new { ok = false, mensaje = "La ejecución o entrega de turno ya no está disponible." });
        if (resultado is UnauthorizedResult)
            return Unauthorized(new { ok = false, mensaje = "No fue posible validar la sesión para el cambio de turno." });
        if (resultado is StatusCodeResult status && status.StatusCode >= 400)
            return StatusCode(status.StatusCode, new { ok = false, mensaje = "No fue posible completar el cambio de turno." });

        return Json(new
        {
            ok = true,
            mensaje = string.IsNullOrWhiteSpace(success) ? mensajeExitoDefault : success,
            refrescarCentro = true
        });
    }

    private void LimpiarMensajesCambioTurnoOperativo()
    {
        TempData.Remove("Error");
        TempData.Remove("Success");
    }

    private static string ObtenerOFParejaSeguraCambioTurno(ProduccionParejaLhRhVm pareja)
    {
        if (!string.IsNullOrWhiteSpace(pareja.NumeroOFPareja)) return pareja.NumeroOFPareja!.Trim();
        if (!string.IsNullOrWhiteSpace(pareja.FolioSolicitudPareja)) return pareja.FolioSolicitudPareja!.Trim();
        return $"Programa {pareja.ProgramaParejaID}";
    }

    private static string ObtenerParteParejaSeguraCambioTurno(ProduccionParejaLhRhVm pareja)
    {
        if (!string.IsNullOrWhiteSpace(pareja.ReferenciaSAPPareja)) return pareja.ReferenciaSAPPareja!.Trim();
        if (!string.IsNullOrWhiteSpace(pareja.NumeroPartePareja)) return pareja.NumeroPartePareja!.Trim();
        return pareja.ParteParejaID.HasValue ? $"Parte {pareja.ParteParejaID.Value}" : "Sin parte";
    }

    private static string SanitizarMensajeParejaCambioTurno(string mensaje, ProduccionParejaLhRhVm pareja)
    {
        return SanitizarTextoParejaCambioTurno(
            mensaje,
            pareja.OFParejaTexto,
            ObtenerOFParejaSeguraCambioTurno(pareja)) ?? mensaje;
    }

    private static string? SanitizarTextoParejaCambioTurno(
        string? mensaje,
        string? referenciaParejaVieja,
        string? referenciaParejaSegura)
    {
        if (string.IsNullOrWhiteSpace(mensaje)) return mensaje;
        if (string.IsNullOrWhiteSpace(referenciaParejaVieja) || string.IsNullOrWhiteSpace(referenciaParejaSegura)) return mensaje;
        if (string.Equals(referenciaParejaVieja, referenciaParejaSegura, StringComparison.OrdinalIgnoreCase)) return mensaje;
        return mensaje.Replace(referenciaParejaVieja, referenciaParejaSegura, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ContextoCambioTurnoOperativo
    {
        public int EjecucionProduccionID { get; set; }
        public int ProgramaProduccionID { get; set; }
        public string OFTexto { get; set; } = string.Empty;
        public int EstatusID { get; set; }
        public int? MaquinaID { get; set; }
        public string? MaquinaCodigo { get; set; }
        public string? MaquinaNombre { get; set; }
        public int? ParteID { get; set; }
        public string? NumeroParte { get; set; }
        public string? ReferenciaSAP { get; set; }
        public int? OperadorActualID { get; set; }
        public string? OperadorActualNombre { get; set; }
    }

    private sealed class ProgramacionCambioTurnoOperativo
    {
        public bool RequiereCambio { get; set; }
        public bool YaInicio { get; set; }
        public int MinutosParaCambio { get; set; }
        public int? TurnoID { get; set; }
        public string? TurnoNombre { get; set; }
        public DateTime Inicio { get; set; }
        public DateTime Fin { get; set; }
        public int? OperadorID { get; set; }
        public string? OperadorNombre { get; set; }
    }
}
