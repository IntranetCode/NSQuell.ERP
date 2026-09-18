using ERP.NSQuell.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ERP.NSQuell.Controllers;

public sealed partial class ProduccionController
{
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> InicioSerieOperativa(int ejecucionProduccionId)
    {
        if (!UsuarioEnSesion())
            return Unauthorized(new { ok = false, sesionExpirada = true, mensaje = "La sesión terminó. Vuelve a iniciar sesión." });
        if (ejecucionProduccionId <= 0)
            return BadRequest(new { ok = false, mensaje = "La ejecución de Producción no es válida." });

        var usuarioId = ObtenerUsuarioID();
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync();

        var ejecucion = await ObtenerEjecucionAsync(ejecucionProduccionId, cn);
        if (ejecucion == null)
            return NotFound(new { ok = false, mensaje = "No se encontró la ejecución de Producción." });

        var permisos = await ObtenerPermisosProduccionUsuarioAsync(usuarioId, cn);
        var puedeEjecutarUsuario = PuedeIniciarSerieOperativa(permisos);
        var pareja = await ObtenerParejaLhRhProduccionAsync(ejecucion.ProgramaProduccionID, cn);
        ProduccionEjecucionVm? ejecucionPareja = null;

        if (pareja?.EjecucionParejaID is int ejecucionParejaId && ejecucionParejaId > 0)
            ejecucionPareja = await ObtenerEjecucionAsync(ejecucionParejaId, cn);

        var configuracion = await ObtenerConfiguracionActualAsync(ejecucionProduccionId, cn);
        var calidad = await ObtenerResumenCalidadAsync(ejecucionProduccionId, cn);
        ProduccionConfiguracionCorridaVm? configuracionPareja = null;
        ProduccionCalidadResumenVm? calidadPareja = null;

        if (ejecucionPareja != null)
        {
            configuracionPareja = await ObtenerConfiguracionActualAsync(ejecucionPareja.EjecucionProduccionID, cn);
            calidadPareja = await ObtenerResumenCalidadAsync(ejecucionPareja.EjecucionProduccionID, cn);
        }

        var esReinicioActual =
            ejecucion.EstatusID == ProduccionEstatus.EnPreparacion &&
            await EsReinicioSeriePendienteAsync(ejecucionProduccionId, cn);

        var esReinicioPareja =
            ejecucionPareja != null &&
            ejecucionPareja.EstatusID == ProduccionEstatus.EnPreparacion &&
            await EsReinicioSeriePendienteAsync(ejecucionPareja.EjecucionProduccionID, cn);

        var vm = new ProduccionInicioSerieOperativaVm
        {
            Ejecucion = ejecucion,
            Configuracion = configuracion,
            Calidad = calidad,
            ParejaLhRh = pareja,
            EjecucionPareja = ejecucionPareja,
            ConfiguracionPareja = configuracionPareja,
            CalidadPareja = calidadPareja,
            EsReinicioActual = esReinicioActual,
            EsReinicioPareja = esReinicioPareja,
            PuedeEjecutarUsuario = puedeEjecutarUsuario
        };

        CompletarValidacionInicioSerieOperativa(vm);

        return PartialView("~/Views/ProduccionOperativa/Acciones/_InicioSerieContenido.cshtml", vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> IniciarSerieOperativa(int ejecucionProduccionId, long? contadorMaquinaActual = null)
    {
        if (!UsuarioEnSesion())
            return Unauthorized(new { ok = false, sesionExpirada = true, mensaje = "La sesión terminó. Vuelve a iniciar sesión." });
        if (ejecucionProduccionId <= 0)
            return BadRequest(new { ok = false, mensaje = "La ejecución de Producción no es válida." });

        await using (var cn = new SqlConnection(ConnectionString))
        {
            await cn.OpenAsync();
            var permisos = await ObtenerPermisosProduccionUsuarioAsync(ObtenerUsuarioID(), cn);
            if (!PuedeIniciarSerieOperativa(permisos))
                return StatusCode(StatusCodes.Status403Forbidden, new { ok = false, mensaje = "No tienes permiso para iniciar o reiniciar la producción en serie." });
        }

        LimpiarTempDataInicioSerieOperativa();

        IActionResult resultado;
        try
        {
            resultado = await IniciarSerie(ejecucionProduccionId, contadorMaquinaActual);
        }
        catch (Exception ex)
        {
            return BadRequest(new { ok = false, mensaje = "No fue posible iniciar o reiniciar la producción en serie: " + ex.Message });
        }

        return ConvertirResultadoInicioSerieOperativa(resultado);
    }

    private static bool PuedeIniciarSerieOperativa(ProduccionPermisosUsuario permisos)
    {
        if (permisos == null) return false;
        return permisos.PuedeVerTodo || permisos.EsTecnicoProduccion || permisos.EsOperadorProduccion;
    }

    private static void CompletarValidacionInicioSerieOperativa(ProduccionInicioSerieOperativaVm vm)
    {
        var bloqueos = new List<string>();
        var e = vm.Ejecucion;
        var pareja = vm.ParejaLhRh;
        var ep = vm.EjecucionPareja;

        var actualProduciendo = e.EstatusID == ProduccionEstatus.EnProduccion;
        var parejaProduciendo = ep?.EstatusID == ProduccionEstatus.EnProduccion;

        if (pareja == null)
        {
            vm.YaEstaEnSerie = actualProduciendo;
        }
        else if (ep != null)
        {
            vm.YaEstaEnSerie = actualProduciendo && parejaProduciendo;
            if (actualProduciendo != parejaProduciendo)
                bloqueos.Add("Se detectó una inconsistencia LH/RH: solamente una de las dos ejecuciones está en producción. No se permitirá incorporar una OF después del arranque.");
        }

        if (vm.YaEstaEnSerie)
        {
            vm.PuedeConfirmar = false;
            vm.Bloqueos = bloqueos;
            return;
        }

        if (e.EstatusID != ProduccionEstatus.EnPreparacion)
            bloqueos.Add($"La ejecución actual debe estar EN PREPARACIÓN. Estado actual: {ProduccionEstatus.Nombre(e.EstatusID)}.");

        if (e.TieneParoAbierto)
            bloqueos.Add("La OF actual tiene un paro abierto. Debes cerrarlo antes de iniciar o reiniciar serie.");

        if (!vm.ConfiguracionLista)
            bloqueos.Add("Falta una configuración técnica vigente con cavidades reales, ciclo real y contador base.");

        if (!vm.CalidadLista)
            bloqueos.Add("Calidad todavía no ha liberado la OF con resultado y etiqueta verde.");

        if (pareja != null)
        {
            if (!pareja.EsCompatibleFisicamente)
                bloqueos.Add($"La pareja LH/RH grupo {pareja.GrupoLhRh} ya no conserva la misma máquina, molde y ventana programada.");

            if (ep == null)
            {
                bloqueos.Add("La pareja LH/RH todavía no tiene una ejecución activa. Las dos ejecuciones deben existir antes del arranque conjunto.");
            }
            else
            {
                if (ep.EstatusID != ProduccionEstatus.EnPreparacion)
                    bloqueos.Add($"La OF pareja debe estar EN PREPARACIÓN. Estado actual: {ProduccionEstatus.Nombre(ep.EstatusID)}.");

                if (ep.TieneParoAbierto)
                    bloqueos.Add("La OF pareja tiene un paro abierto. No se puede arrancar una producción LH/RH parcialmente.");

                if (!CoincidenOperadoresLhRh(e, ep))
                    bloqueos.Add("Las OF LH/RH tienen operadores principales diferentes. Deben compartir el mismo operador antes del arranque.");

                if (!vm.ConfiguracionParejaLista)
                    bloqueos.Add("La OF pareja todavía no tiene una configuración técnica vigente completa.");

                if (!vm.CalidadParejaLista)
                    bloqueos.Add("Calidad todavía no ha liberado la OF pareja con resultado y etiqueta verde.");

                if (vm.ConfiguracionLista && vm.ConfiguracionParejaLista && !vm.ConfiguracionFisicaLhRhSincronizada)
                    bloqueos.Add("Las configuraciones LH/RH no comparten el mismo ciclo físico y contador base.");

                if (vm.EsReinicioActual != vm.EsReinicioPareja)
                    bloqueos.Add("La producción LH/RH tiene un reinicio inconsistente: solamente una de las dos OF está pendiente de reinicio.");
            }
        }

        if (!vm.PuedeEjecutarUsuario)
            bloqueos.Add("Tu usuario puede consultar este paso, pero no tiene permiso para confirmar el inicio o reinicio de serie.");

        vm.Bloqueos = bloqueos;
        vm.PuedeConfirmar = bloqueos.Count == 0 && vm.PuedeEjecutarUsuario;
    }

    private void LimpiarTempDataInicioSerieOperativa()
    {
        TempData.Remove("Error");
        TempData.Remove("Warning");
        TempData.Remove("Info");
        TempData.Remove("Success");
        TempData.Remove("Mensaje");
    }

    private IActionResult ConvertirResultadoInicioSerieOperativa(IActionResult resultado)
    {
        var error = TempData["Error"]?.ToString();
        var warning = TempData["Warning"]?.ToString();
        var info = TempData["Info"]?.ToString();
        var success = TempData["Success"]?.ToString();
        var mensaje = TempData["Mensaje"]?.ToString();

        if (!string.IsNullOrWhiteSpace(error))
            return BadRequest(new { ok = false, mensaje = error });

        if (resultado is NotFoundResult)
            return NotFound(new { ok = false, mensaje = "No se encontró la ejecución de Producción." });
        if (resultado is UnauthorizedResult)
            return Unauthorized(new { ok = false, sesionExpirada = true, mensaje = "La sesión terminó. Vuelve a iniciar sesión." });
        if (resultado is BadRequestObjectResult badRequest)
            return BadRequest(new { ok = false, mensaje = badRequest.Value?.ToString() ?? "La solicitud no es válida." });
        if (resultado is StatusCodeResult status && status.StatusCode >= 400)
            return StatusCode(status.StatusCode, new { ok = false, mensaje = "No fue posible confirmar el inicio de serie." });

        var texto = !string.IsNullOrWhiteSpace(success)
            ? success
            : !string.IsNullOrWhiteSpace(warning)
                ? warning
                : !string.IsNullOrWhiteSpace(info)
                    ? info
                    : !string.IsNullOrWhiteSpace(mensaje)
                        ? mensaje
                        : "La operación de inicio de serie se completó correctamente.";

        return Json(new
        {
            ok = true,
            mensaje = texto,
            advertencia = !string.IsNullOrWhiteSpace(warning),
            informativo = !string.IsNullOrWhiteSpace(info)
        });
    }
}
