using ERP.NSQuell.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System;
using System.Linq;
using System.Data;
using System.Threading.Tasks;

namespace ERP.NSQuell.Controllers;

public sealed partial class ProduccionOperadorController
{
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> TiempoExtraOperativo(int ejecucionProduccionId, bool soloLectura = false)
    {
        if (!UsuarioEnSesion())
            return Unauthorized(new { ok = false, sesionExpirada = true, mensaje = "La sesion termino. Vuelve a iniciar sesion." });
        if (ejecucionProduccionId <= 0)
            return BadRequest(new { ok = false, mensaje = "La ejecucion de Produccion no es valida." });

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync();

        var ejecucion = await ObtenerEjecucionOperadorLecturaAsync(ejecucionProduccionId, cn);
        if (ejecucion == null)
            return NotFound(new { ok = false, mensaje = "No se encontro la ejecucion de Produccion." });

        var usuarioId = ObtenerUsuarioID();
        var usuarioEsOperador = await UsuarioEsOperadorAsync(usuarioId, cn);
        var personaId = await ObtenerPersonaIDUsuarioAsync(usuarioId, cn);
        var asignado = personaId.HasValue && personaId.Value > 0 &&
                       await PersonaAsignadaAEjecucionAsync(ejecucionProduccionId, personaId.Value, cn);

        var sesion = await ObtenerTiempoExtraActivoAsync(ejecucionProduccionId, cn);
        var historial = await ObtenerHistorialTiempoExtraAsync(ejecucionProduccionId, cn);
        var puedeIniciar = sesion == null && await PuedeIniciarTiempoExtraAsync(ejecucionProduccionId, cn);
        var ultimoContador = await ObtenerUltimaLecturaContadorMaquinaAsync(ejecucionProduccionId, cn);

        var vm = new ProduccionTiempoExtraOperativoVm
        {
            EjecucionProduccionID = ejecucion.EjecucionProduccionID,
            ProgramaProduccionID = ejecucion.ProgramaProduccionID,
            OFTexto = ObtenerOFSeguraTiempoExtra(ejecucion),
            EstatusID = ejecucion.EstatusID,
            MaquinaID = ejecucion.MaquinaID,
            MaquinaCodigo = ejecucion.MaquinaCodigo,
            MaquinaNombre = ejecucion.MaquinaNombre,
            ParteID = ejecucion.ParteID,
            NumeroParte = ejecucion.NumeroParte,
            ReferenciaSAP = ejecucion.ReferenciaSAP,
            OperadorActualID = ejecucion.OperadorID,
            OperadorActualNombre = ejecucion.OperadorNombre,
            SoloLectura = soloLectura,
            UsuarioEsOperador = usuarioEsOperador,
            UsuarioAsignadoAEjecucion = asignado,
            PuedeGestionar = !soloLectura && usuarioEsOperador && asignado,
            PuedeIniciar = puedeIniciar,
            TiempoExtraActivo = sesion,
            Historial = historial,
            CatalogoDefectos = await CargarCatalogoDefectosAsync(cn),
            UltimoContadorMaquina = ultimoContador,
            FechaHoraServidor = DateTime.Now
        };

        var pareja = await ObtenerParejaLhRhOperadorAsync(ejecucion.ProgramaProduccionID, cn);
        if (pareja != null)
        {
            vm.EsLhRh = true;
            vm.GrupoLhRh = pareja.GrupoLhRh;
            vm.EjecucionParejaID = pareja.EjecucionParejaID;
            vm.ProgramaParejaID = pareja.ProgramaParejaID;
            vm.OFParejaTexto = ObtenerOFParejaSeguraTiempoExtra(pareja);
            vm.ParteParejaTexto = ObtenerParteParejaSeguraTiempoExtra(pareja);

            try
            {
                ValidarParejaLhRhOperador(pareja);
            }
            catch (Exception ex)
            {
                vm.ParejaConsistente = false;
                vm.MotivoInconsistenciaPareja = SanitizarMensajeParejaTiempoExtra(ex.Message, pareja);
            }

            if (pareja.EjecucionParejaID.HasValue && pareja.EjecucionParejaID.Value > 0)
            {
                var ejecucionParejaId = pareja.EjecucionParejaID.Value;
                vm.TiempoExtraParejaActivo = await ObtenerTiempoExtraActivoAsync(ejecucionParejaId, cn);
                vm.HistorialPareja = await ObtenerHistorialTiempoExtraAsync(ejecucionParejaId, cn);
                vm.UltimoContadorPareja = await ObtenerUltimaLecturaContadorMaquinaAsync(ejecucionParejaId, cn);

                if (vm.ParejaConsistente)
                {
                    if ((vm.TiempoExtraActivo == null) != (vm.TiempoExtraParejaActivo == null))
                    {
                        vm.ParejaConsistente = false;
                        vm.MotivoInconsistenciaPareja = "Las sesiones de tiempo extra LH/RH no estan sincronizadas.";
                    }
                    else if (vm.TiempoExtraActivo != null && vm.TiempoExtraParejaActivo != null)
                    {
                        try
                        {
                            ValidarSincronizacionTiempoExtraLhRh(vm.TiempoExtraActivo, vm.TiempoExtraParejaActivo);
                        }
                        catch (Exception ex)
                        {
                            vm.ParejaConsistente = false;
                            vm.MotivoInconsistenciaPareja = SanitizarMensajeParejaTiempoExtra(ex.Message, pareja);
                        }
                    }
                    else if (vm.TiempoExtraActivo == null)
                    {
                        var puedePareja = await PuedeIniciarTiempoExtraAsync(ejecucionParejaId, cn);
                        vm.PuedeIniciar = vm.PuedeIniciar && puedePareja;
                    }
                }
            }
            else
            {
                vm.ParejaConsistente = false;
                vm.MotivoInconsistenciaPareja = $"No existe una ejecucion activa para {vm.OFParejaTexto}.";
            }

            if (!vm.ParejaConsistente)
                vm.PuedeIniciar = false;
        }

        return PartialView("~/Views/ProduccionOperativa/Acciones/_TiempoExtraContenido.cshtml", vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> IniciarTiempoExtraOperativo(ProduccionTiempoExtraIniciarPostVm vm)
    {
        if (!UsuarioEnSesion())
            return Unauthorized(new { ok = false, sesionExpirada = true, mensaje = "La sesion termino. Vuelve a iniciar sesion." });
        if (vm.EjecucionProduccionID <= 0)
            return BadRequest(new { ok = false, mensaje = "No se recibio una ejecucion de Produccion valida." });

        var referencias = await ObtenerReferenciasParejaTiempoExtraAsync(vm.EjecucionProduccionID, null);
        LimpiarMensajesTiempoExtraOperativo();

        IActionResult resultado;
        try
        {
            resultado = await IniciarTiempoExtra(vm);
        }
        catch (Exception ex)
        {
            var mensaje = SanitizarTextoParejaTiempoExtra(ex.Message, referencias.ReferenciaOriginal, referencias.ReferenciaSegura);
            return BadRequest(new { ok = false, mensaje = "No fue posible iniciar el tiempo extra: " + mensaje });
        }

        return ConvertirResultadoTiempoExtraOperativo(
            resultado,
            "Tiempo extra iniciado correctamente.",
            referencias.ReferenciaOriginal,
            referencias.ReferenciaSegura);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> PrevisualizarCorteTiempoExtraOperativo(
        int tiempoExtraId,
        long? contadorMaquinaActual,
        int cantidadScrap = 0,
        int cantidadScrapPareja = 0)
    {
        return PrevisualizarCorteTiempoExtra(tiempoExtraId, contadorMaquinaActual, cantidadScrap, cantidadScrapPareja);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CapturarCorteTiempoExtraOperativo(ProduccionTiempoExtraCortePostVm vm)
    {
        if (!UsuarioEnSesion())
            return Unauthorized(new { ok = false, sesionExpirada = true, mensaje = "La sesion termino. Vuelve a iniciar sesion." });
        if (vm.TiempoExtraID <= 0)
            return BadRequest(new { ok = false, mensaje = "No se recibio una sesion de tiempo extra valida." });

        var referencias = await ObtenerReferenciasParejaTiempoExtraAsync(vm.EjecucionProduccionID, vm.TiempoExtraID);
        LimpiarMensajesTiempoExtraOperativo();

        IActionResult resultado;
        try
        {
            resultado = await CapturarCorteTiempoExtra(vm);
        }
        catch (Exception ex)
        {
            var mensaje = SanitizarTextoParejaTiempoExtra(ex.Message, referencias.ReferenciaOriginal, referencias.ReferenciaSegura);
            return BadRequest(new { ok = false, mensaje = "No fue posible guardar el corte de tiempo extra: " + mensaje });
        }

        return ConvertirResultadoTiempoExtraOperativo(
            resultado,
            "Corte de tiempo extra guardado correctamente.",
            referencias.ReferenciaOriginal,
            referencias.ReferenciaSegura);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> FinalizarTiempoExtraOperativo(ProduccionTiempoExtraCortePostVm vm)
    {
        if (!UsuarioEnSesion())
            return Unauthorized(new { ok = false, sesionExpirada = true, mensaje = "La sesion termino. Vuelve a iniciar sesion." });
        if (vm.TiempoExtraID <= 0)
            return BadRequest(new { ok = false, mensaje = "No se recibio una sesion de tiempo extra valida." });

        var referencias = await ObtenerReferenciasParejaTiempoExtraAsync(vm.EjecucionProduccionID, vm.TiempoExtraID);
        LimpiarMensajesTiempoExtraOperativo();

        IActionResult resultado;
        try
        {
            resultado = await FinalizarTiempoExtra(vm);
        }
        catch (Exception ex)
        {
            var mensaje = SanitizarTextoParejaTiempoExtra(ex.Message, referencias.ReferenciaOriginal, referencias.ReferenciaSegura);
            return BadRequest(new { ok = false, mensaje = "No fue posible finalizar el tiempo extra: " + mensaje });
        }

        return ConvertirResultadoTiempoExtraOperativo(
            resultado,
            "Tiempo extra finalizado correctamente.",
            referencias.ReferenciaOriginal,
            referencias.ReferenciaSegura);
    }

    private async Task<(string? ReferenciaOriginal, string? ReferenciaSegura)> ObtenerReferenciasParejaTiempoExtraAsync(
        int ejecucionProduccionId,
        int? tiempoExtraId)
    {
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync();

        if (ejecucionProduccionId <= 0 && tiempoExtraId.HasValue && tiempoExtraId.Value > 0)
        {
            const string sql = @"
SELECT TOP(1) EjecucionProduccionID
FROM dbo.Produccion_TiempoExtra
WHERE TiempoExtraID=@TiempoExtraID
  AND Activo=1;";
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.AddWithValue("@TiempoExtraID", tiempoExtraId.Value);
            var valor = await cmd.ExecuteScalarAsync();
            if (valor != null && valor != DBNull.Value)
                ejecucionProduccionId = Convert.ToInt32(valor);
        }

        if (ejecucionProduccionId <= 0) return (null, null);
        var ejecucion = await ObtenerEjecucionOperadorLecturaAsync(ejecucionProduccionId, cn);
        if (ejecucion == null) return (null, null);
        var pareja = await ObtenerParejaLhRhOperadorAsync(ejecucion.ProgramaProduccionID, cn);
        if (pareja == null) return (null, null);
        return (pareja.OFParejaTexto, ObtenerOFParejaSeguraTiempoExtra(pareja));
    }

    private async Task<ProduccionEjecucionVm?> ObtenerEjecucionOperadorLecturaAsync(int ejecucionProduccionId, SqlConnection cn)
    {
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        try
        {
            var ejecucion = await ObtenerEjecucionOperadorAsync(ejecucionProduccionId, cn, tx);
            await tx.CommitAsync();
            return ejecucion;
        }
        catch
        {
            try { await tx.RollbackAsync(); } catch { }
            throw;
        }
    }

    private IActionResult ConvertirResultadoTiempoExtraOperativo(
        IActionResult resultado,
        string mensajeExitoDefault,
        string? referenciaParejaOriginal,
        string? referenciaParejaSegura)
    {
        var error = TempData["Error"]?.ToString();
        var success = TempData["Success"]?.ToString();
        TempData.Remove("Error");
        TempData.Remove("Success");

        error = SanitizarTextoParejaTiempoExtra(error, referenciaParejaOriginal, referenciaParejaSegura);
        success = SanitizarTextoParejaTiempoExtra(success, referenciaParejaOriginal, referenciaParejaSegura);

        if (!string.IsNullOrWhiteSpace(error))
            return BadRequest(new { ok = false, mensaje = error });
        if (resultado is NotFoundResult)
            return NotFound(new { ok = false, mensaje = "La ejecucion o sesion de tiempo extra ya no esta disponible." });
        if (resultado is UnauthorizedResult)
            return Unauthorized(new { ok = false, mensaje = "No fue posible validar la sesion para tiempo extra." });
        if (resultado is StatusCodeResult status && status.StatusCode >= 400)
            return StatusCode(status.StatusCode, new { ok = false, mensaje = "No fue posible completar la operacion de tiempo extra." });

        return Json(new
        {
            ok = true,
            mensaje = string.IsNullOrWhiteSpace(success) ? mensajeExitoDefault : success,
            refrescarCentro = true
        });
    }

    private void LimpiarMensajesTiempoExtraOperativo()
    {
        TempData.Remove("Error");
        TempData.Remove("Success");
    }

    private static string ObtenerOFSeguraTiempoExtra(ProduccionEjecucionVm ejecucion)
    {
        if (!string.IsNullOrWhiteSpace(ejecucion.NumeroOFRecibida)) return ejecucion.NumeroOFRecibida!.Trim();
        if (!string.IsNullOrWhiteSpace(ejecucion.FolioSolicitud)) return ejecucion.FolioSolicitud!.Trim();
        return $"Programa {ejecucion.ProgramaProduccionID}";
    }

    private static string ObtenerOFParejaSeguraTiempoExtra(ProduccionParejaLhRhVm pareja)
    {
        if (!string.IsNullOrWhiteSpace(pareja.NumeroOFPareja)) return pareja.NumeroOFPareja!.Trim();
        if (!string.IsNullOrWhiteSpace(pareja.FolioSolicitudPareja)) return pareja.FolioSolicitudPareja!.Trim();
        return $"Programa {pareja.ProgramaParejaID}";
    }

    private static string ObtenerParteParejaSeguraTiempoExtra(ProduccionParejaLhRhVm pareja)
    {
        if (!string.IsNullOrWhiteSpace(pareja.ReferenciaSAPPareja)) return pareja.ReferenciaSAPPareja!.Trim();
        if (!string.IsNullOrWhiteSpace(pareja.NumeroPartePareja)) return pareja.NumeroPartePareja!.Trim();
        return pareja.ParteParejaID.HasValue ? $"Parte {pareja.ParteParejaID.Value}" : "Sin parte";
    }

    private static string SanitizarMensajeParejaTiempoExtra(string mensaje, ProduccionParejaLhRhVm pareja)
    {
        return SanitizarTextoParejaTiempoExtra(mensaje, pareja.OFParejaTexto, ObtenerOFParejaSeguraTiempoExtra(pareja)) ?? mensaje;
    }

    private static string? SanitizarTextoParejaTiempoExtra(
        string? mensaje,
        string? referenciaParejaOriginal,
        string? referenciaParejaSegura)
    {
        if (string.IsNullOrWhiteSpace(mensaje)) return mensaje;
        if (string.IsNullOrWhiteSpace(referenciaParejaOriginal) || string.IsNullOrWhiteSpace(referenciaParejaSegura)) return mensaje;
        if (string.Equals(referenciaParejaOriginal, referenciaParejaSegura, StringComparison.OrdinalIgnoreCase)) return mensaje;
        return mensaje.Replace(referenciaParejaOriginal, referenciaParejaSegura, StringComparison.OrdinalIgnoreCase);
    }
}
