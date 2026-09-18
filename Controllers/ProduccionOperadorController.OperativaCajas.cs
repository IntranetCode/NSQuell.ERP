using ERP.NSQuell.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

namespace ERP.NSQuell.Controllers;

public sealed partial class ProduccionOperadorController
{
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> CajasOperativo(int ejecucionProduccionId, bool soloLectura = false)
    {
        if (!UsuarioEnSesion())
            return Unauthorized(new { ok = false, sesionExpirada = true, mensaje = "La sesion termino. Vuelve a iniciar sesion." });
        if (ejecucionProduccionId <= 0)
            return BadRequest(new { ok = false, mensaje = "La ejecucion de Produccion no es valida." });

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync();

        var actual = await ObtenerCajasOperadorVmAsync(ejecucionProduccionId, cn);
        if (actual == null)
            return NotFound(new { ok = false, mensaje = "No se encontro la ejecucion de Produccion." });

        var usuarioEsOperador = await UsuarioEsOperadorAsync(ObtenerUsuarioID(), cn);
        var vm = new ProduccionCajasOperativasVm
        {
            Actual = actual,
            SoloLectura = soloLectura,
            UsuarioEsOperador = usuarioEsOperador,
            FechaHoraServidor = DateTime.Now
        };

        var pareja = await ObtenerParejaLhRhOperadorAsync(actual.ProgramaProduccionID, cn);
        if (pareja != null)
        {
            vm.GrupoLhRh = pareja.GrupoLhRh;
            var lados = ResolverLadosLhRhOperador(actual.ReferenciaSAP, actual.NumeroParte, actual.DescripcionParte, pareja);
            vm.LadoActual = lados.LadoActual;
            vm.LadoPareja = lados.LadoPareja;

            if (pareja.EjecucionParejaID.HasValue && pareja.EjecucionParejaID.Value > 0)
                vm.Pareja = await ObtenerCajasOperadorVmAsync(pareja.EjecucionParejaID.Value, cn);
        }

        var ids = new List<int> { vm.Actual.EjecucionProduccionID };
        if (vm.Pareja != null && vm.Pareja.EjecucionProduccionID > 0)
            ids.Add(vm.Pareja.EjecucionProduccionID);

        vm.EstatusCalidadPorCaja = await ObtenerEstatusCalidadCajasOperativasAsync(ids, cn);

        return PartialView("~/Views/ProduccionOperativa/Acciones/_CajasContenido.cshtml", vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> FormarCajaOperativa(
        int ejecucionProduccionId,
        int cantidadPiezas,
        string tipoCaja,
        string? observaciones)
    {
        if (!UsuarioEnSesion()) return RespuestaSesionExpiradaCajasOperativas();
        LimpiarMensajesCajasOperativas();
        IActionResult resultado;
        try
        {
            resultado = await FormarCaja(ejecucionProduccionId, cantidadPiezas, tipoCaja, observaciones);
        }
        catch (Exception ex)
        {
            return BadRequest(new { ok = false, mensaje = "No fue posible formar la caja: " + ex.Message });
        }
        return ConvertirResultadoCajasOperativas(resultado, "Caja registrada correctamente.");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EscanearCajaOperativa(ProduccionOperadorCajasVm.ProduccionEscanearCajaPostVm vm)
    {
        if (!UsuarioEnSesion()) return RespuestaSesionExpiradaCajasOperativas();
        LimpiarMensajesCajasOperativas();
        IActionResult resultado;
        try
        {
            resultado = await EscanearCaja(vm);
        }
        catch (Exception ex)
        {
            return BadRequest(new { ok = false, mensaje = "No fue posible registrar la etiqueta: " + ex.Message });
        }
        return ConvertirResultadoCajasOperativas(resultado, "Etiqueta registrada correctamente.");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CorregirCajaDevueltaOperativa(long cajaProduccionId, string? correccionRealizada)
    {
        if (!UsuarioEnSesion()) return RespuestaSesionExpiradaCajasOperativas();
        if (cajaProduccionId <= 0 || cajaProduccionId > int.MaxValue)
            return BadRequest(new { ok = false, mensaje = "La caja seleccionada no es valida." });
        LimpiarMensajesCajasOperativas();
        IActionResult resultado;
        try
        {
            resultado = await CorregirCajaDevuelta((int)cajaProduccionId, correccionRealizada);
        }
        catch (Exception ex)
        {
            return BadRequest(new { ok = false, mensaje = "No fue posible registrar la correccion: " + ex.Message });
        }
        return ConvertirResultadoCajasOperativas(resultado, "Correccion registrada. La caja puede reenviarse a Calidad.");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SolicitarLiberacionCajaOperativa(long cajaProduccionId)
    {
        if (!UsuarioEnSesion()) return RespuestaSesionExpiradaCajasOperativas();
        if (cajaProduccionId <= 0 || cajaProduccionId > int.MaxValue)
            return BadRequest(new { ok = false, mensaje = "La caja seleccionada no es valida." });
        LimpiarMensajesCajasOperativas();
        IActionResult resultado;
        try
        {
            resultado = await SolicitarLiberacionCaja((int)cajaProduccionId);
        }
        catch (Exception ex)
        {
            return BadRequest(new { ok = false, mensaje = "No fue posible enviar la caja a Calidad: " + ex.Message });
        }
        return ConvertirResultadoCajasOperativas(resultado, "Caja enviada a Calidad. Esperando liberacion desde el modulo de Calidad.");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MoverCajaZonaVerdeOperativa(long cajaProduccionId)
    {
        if (!UsuarioEnSesion()) return RespuestaSesionExpiradaCajasOperativas();
        if (cajaProduccionId <= 0 || cajaProduccionId > int.MaxValue)
            return BadRequest(new { ok = false, mensaje = "La caja seleccionada no es valida." });
        LimpiarMensajesCajasOperativas();
        IActionResult resultado;
        try
        {
            resultado = await MoverCajaZonaVerde((int)cajaProduccionId);
        }
        catch (Exception ex)
        {
            return BadRequest(new { ok = false, mensaje = "No fue posible mover la caja a zona verde: " + ex.Message });
        }
        return ConvertirResultadoCajasOperativas(resultado, "Caja movida a zona verde.");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EscanearSalidaCajaOperativa(long cajaProduccionId, string? etiquetaEscaneada)
    {
        if (!UsuarioEnSesion()) return RespuestaSesionExpiradaCajasOperativas();
        if (cajaProduccionId <= 0 || cajaProduccionId > int.MaxValue)
            return BadRequest(new { ok = false, mensaje = "La caja seleccionada no es valida." });
        LimpiarMensajesCajasOperativas();
        IActionResult resultado;
        try
        {
            resultado = await EscanearSalidaCaja((int)cajaProduccionId, etiquetaEscaneada);
        }
        catch (Exception ex)
        {
            return BadRequest(new { ok = false, mensaje = "No fue posible registrar la salida de Produccion: " + ex.Message });
        }
        return ConvertirResultadoCajasOperativas(resultado, "Salida de Produccion registrada. Esperando recepcion de Almacen PT.");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReservarCajaIncompletaOperativa(ProduccionReservarCajaIncompletaPostVm vm)
    {
        if (!UsuarioEnSesion()) return RespuestaSesionExpiradaCajasOperativas();
        LimpiarMensajesCajasOperativas();
        IActionResult resultado;
        try
        {
            resultado = await ReservarCajaIncompleta(vm);
        }
        catch (Exception ex)
        {
            return BadRequest(new { ok = false, mensaje = "No fue posible confirmar el producto incompleto: " + ex.Message });
        }
        return ConvertirResultadoCajasOperativas(resultado, "Producto incompleto confirmado para esta OF.");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CompletarCajaIncompletaOperativa(ProduccionCompletarCajaIncompletaPostVm vm)
    {
        if (!UsuarioEnSesion()) return RespuestaSesionExpiradaCajasOperativas();
        LimpiarMensajesCajasOperativas();
        IActionResult resultado;
        try
        {
            resultado = await CompletarCajaIncompleta(vm);
        }
        catch (Exception ex)
        {
            return BadRequest(new { ok = false, mensaje = "No fue posible completar el producto incompleto: " + ex.Message });
        }
        return ConvertirResultadoCajasOperativas(resultado, "Producto incompleto actualizado correctamente.");
    }

    private async Task<Dictionary<long, string>> ObtenerEstatusCalidadCajasOperativasAsync(
        IReadOnlyCollection<int> ejecuciones,
        SqlConnection cn)
    {
        var resultado = new Dictionary<long, string>();
        if (ejecuciones.Count == 0) return resultado;

        var parametros = new List<string>();
        var indice = 0;
        await using var cmd = cn.CreateCommand();
        foreach (var id in ejecuciones)
        {
            var nombre = "@E" + indice++;
            parametros.Add(nombre);
            cmd.Parameters.Add(nombre, SqlDbType.Int).Value = id;
        }

        cmd.CommandText = $@"
SELECT CajaProduccionID,
       UPPER(LTRIM(RTRIM(ISNULL(EstatusCalidad,N'')))) AS EstatusCalidad
FROM dbo.Produccion_Cajas
WHERE Activo=1
  AND EjecucionProduccionID IN ({string.Join(",", parametros)});";

        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
        {
            var id = Convert.ToInt64(rd["CajaProduccionID"]);
            var estado = rd["EstatusCalidad"] == DBNull.Value
                ? string.Empty
                : rd["EstatusCalidad"]?.ToString()?.Trim() ?? string.Empty;
            resultado[id] = estado;
        }
        return resultado;
    }

    private IActionResult ConvertirResultadoCajasOperativas(IActionResult resultado, string mensajeExitoDefault)
    {
        var error = TempData["Error"]?.ToString();
        var success = TempData["Success"]?.ToString();
        var info = TempData["Info"]?.ToString();
        TempData.Remove("Error");
        TempData.Remove("Success");
        TempData.Remove("Info");

        if (!string.IsNullOrWhiteSpace(error))
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return Json(new { ok = false, mensaje = error });
        }
        if (resultado is NotFoundResult)
            return NotFound(new { ok = false, mensaje = "La caja o ejecucion ya no esta disponible." });
        if (resultado is UnauthorizedResult)
            return Unauthorized(new { ok = false, mensaje = "No se pudo validar la sesion del usuario." });
        if (resultado is ContentResult contenido && Response.StatusCode >= 400)
        {
            var mensaje = string.IsNullOrWhiteSpace(contenido.Content)
                ? "No tienes permisos para ejecutar esta accion de cajas."
                : contenido.Content;
            return StatusCode(Response.StatusCode, new { ok = false, mensaje });
        }
        if (resultado is StatusCodeResult status && status.StatusCode >= 400)
            return StatusCode(status.StatusCode, new { ok = false, mensaje = "No fue posible completar la operacion de cajas." });

        Response.StatusCode = StatusCodes.Status200OK;
        return Json(new
        {
            ok = true,
            mensaje = !string.IsNullOrWhiteSpace(success)
                ? success
                : !string.IsNullOrWhiteSpace(info)
                    ? info
                    : mensajeExitoDefault,
            refrescarCentro = true
        });
    }

    private void LimpiarMensajesCajasOperativas()
    {
        TempData.Remove("Error");
        TempData.Remove("Success");
        TempData.Remove("Info");
    }

    private IActionResult RespuestaSesionExpiradaCajasOperativas()
    {
        return Unauthorized(new
        {
            ok = false,
            sesionExpirada = true,
            mensaje = "La sesion termino. Vuelve a iniciar sesion."
        });
    }
}
