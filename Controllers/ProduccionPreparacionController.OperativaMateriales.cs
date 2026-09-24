using ERP.NSQuell.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ERP.NSQuell.Controllers
{
    public sealed partial class ProduccionPreparacionController
    {
        [HttpGet]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> OperativaInsumosDatos(int programaProduccionId, string tipo)
        {
            if (!UsuarioEnSesion())
                return Unauthorized(new { ok = false, sesionExpirada = true, mensaje = "La sesión terminó. Vuelve a iniciar sesión." });

            if (programaProduccionId <= 0)
                return BadRequest(new { ok = false, mensaje = "Programa de Producción no válido." });

            var tipoCanonico = (tipo ?? string.Empty).Trim().ToUpperInvariant();
            if (tipoCanonico != ProduccionRecepcionMaterialTipo.MP && tipoCanonico != ProduccionRecepcionMaterialTipo.Embalaje)
                return BadRequest(new { ok = false, mensaje = "Tipo de insumo no válido." });

            var usuarioId = ObtenerUsuarioID();
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            var permisos = await ObtenerPermisosPreparacionUsuarioAsync(usuarioId, cn);
            if (!permisos.PuedeVerModulo)
                return StatusCode(StatusCodes.Status403Forbidden, new { ok = false, mensaje = "No tienes permiso para consultar Preparación." });

            await SincronizarRecepcionesFaltantesAsync(usuarioId, cn);

            var materialesEsperados = await CargarMaterialesEsperadosAsync(null, null, cn);
            var recepciones = await CargarRecepcionesMaterialesAsync(null, null, cn);
            var relacionesLhRh = await CargarRelacionesMaterialesLhRhAsync(cn);
            AplicarRelacionesLhRhMaterialesEsperados(materialesEsperados, relacionesLhRh);
            AplicarRelacionesLhRhRecepciones(recepciones, relacionesLhRh);

            var esperadosPrograma = materialesEsperados
                .Where(x => x.ProgramaProduccionID == programaProduccionId && string.Equals(x.TipoOrigen, tipoCanonico, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.Codigo)
                .ToList();

            var recepcionesPrograma = recepciones
                .Where(x => x.ProgramaProduccionID == programaProduccionId && string.Equals(x.TipoOrigen, tipoCanonico, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.EstaPendiente)
                .ThenByDescending(x => x.FechaEntregaAlmacen)
                .ThenByDescending(x => x.RecepcionMaterialID)
                .ToList();

            decimal cantidadRequerida;
            decimal cantidadEntregada;
            decimal cantidadConfirmada;

            if (recepcionesPrograma.Count > 0)
            {
                var grupos = recepcionesPrograma
                    .GroupBy(x =>
                    {
                        if (tipoCanonico == ProduccionRecepcionMaterialTipo.MP)
                            return x.MaterialSolicitadoID.HasValue
                                ? "ID:" + x.MaterialSolicitadoID.Value
                                : "COD:" + (x.CodigoSolicitado ?? x.CodigoEntregado ?? string.Empty).Trim().ToUpperInvariant();

                        return x.EmbalajeSolicitadoID.HasValue
                            ? "ID:" + x.EmbalajeSolicitadoID.Value
                            : "COD:" + (x.CodigoSolicitado ?? x.CodigoEntregado ?? string.Empty).Trim().ToUpperInvariant();
                    })
                    .ToList();

                cantidadRequerida = grupos.Sum(g => g.Max(x => x.CantidadRequeridaOF));
                cantidadEntregada = grupos.Sum(g => g.Max(x => x.CantidadEntregadaAcumuladaAlmacen));
                cantidadConfirmada = grupos.Sum(g => g.Max(x => x.CantidadRecibidaAcumuladaProduccion));
            }
            else
            {
                cantidadRequerida = esperadosPrograma.Sum(x => x.CantidadRequerida);
                cantidadEntregada = esperadosPrograma.Sum(x => x.CantidadEntregadaAlmacen);
                cantidadConfirmada = esperadosPrograma.Sum(x => x.CantidadConfirmadaProduccion);
            }

            const decimal tolerancia = 0.0005m;
            var pendientesConfirmacion = recepcionesPrograma.Count(x => x.EstaPendiente);
            var conDiferencia = recepcionesPrograma.Count(x => x.TieneDiferencia);
            var pendienteAlmacen = Math.Max(0m, cantidadRequerida - cantidadEntregada);
            var pendienteConfirmar = Math.Max(0m, cantidadEntregada - cantidadConfirmada);
            var requiereInsumo = cantidadRequerida > tolerancia;
            var hayCantidadConfirmada = cantidadConfirmada > tolerancia;
            var completo = requiereInsumo && cantidadConfirmada + tolerancia >= cantidadRequerida;
            var parcial = requiereInsumo && hayCantidadConfirmada && !completo;
            var disponibleParaProduccion = !requiereInsumo || hayCantidadConfirmada;
            var puedeContinuarPreparacion = tipoCanonico != ProduccionRecepcionMaterialTipo.MP || disponibleParaProduccion;

            var estadoFlujo = completo
                ? "COMPLETO"
                : parcial
                    ? "PARCIAL_DISPONIBLE"
                    : pendientesConfirmacion > 0
                        ? "PENDIENTE_CONFIRMACION"
                        : requiereInsumo
                            ? "ESPERANDO_ALMACEN"
                            : "NO_APLICA";

            return Json(new
            {
                ok = true,
                programaProduccionId,
                tipo = tipoCanonico,
                tipoTexto = tipoCanonico == ProduccionRecepcionMaterialTipo.MP ? "Materia prima" : "Embalaje",
                puedeGestionar = permisos.PuedeGestionarEmbalaje,
                puedeDevolver = permisos.PuedeGestionarEmbalaje,
                resumen = new
                {
                    cantidadRequerida,
                    cantidadEntregada,
                    cantidadConfirmada,
                    pendienteAlmacen,
                    pendienteConfirmar,
                    pendientesConfirmacion,
                    conDiferencia,
                    requiereInsumo,
                    hayCantidadConfirmada,
                    disponibleParaProduccion,
                    puedeContinuarPreparacion,
                    parcial,
                    completo,
                    estadoFlujo
                },
                esperados = esperadosPrograma.Select(x => new
                {
                    x.TipoOrigen,
                    x.CatalogoID,
                    x.Codigo,
                    x.Descripcion,
                    x.Unidad,
                    x.CantidadRequerida,
                    x.CantidadEntregadaAlmacen,
                    x.CantidadConfirmadaProduccion,
                    x.CantidadPendienteAlmacen,
                    x.EstadoAlmacen,
                    x.EstadoAlmacenTexto,
                    x.FechaArranque,
                    x.GrupoLhRh,
                    x.LadoLhRh,
                    x.ProgramaParejaID,
                    x.NumeroOFPareja
                }),
                recepciones = recepcionesPrograma.Select(x => new
                {
                    x.RecepcionMaterialID,
                    x.TipoOrigen,
                    x.MovimientoAlmacenID,
                    x.ProgramaProduccionID,
                    x.EjecucionProduccionID,
                    x.NumeroOF,
                    x.CodigoSolicitado,
                    x.DescripcionSolicitada,
                    x.CodigoEntregado,
                    x.DescripcionEntregada,
                    textoSolicitado = x.TextoSolicitado,
                    textoEntregado = x.TextoEntregado,
                    x.EsSustitucion,
                    x.TipoMP,
                    tipoMPTexto = x.TipoMPTexto,
                    x.Lote,
                    x.Unidad,
                    x.CantidadEntregadaAlmacen,
                    x.CantidadRecibidaProduccion,
                    x.CantidadDiferencia,
                    x.CantidadRequeridaOF,
                    x.CantidadEntregadaAcumuladaAlmacen,
                    x.CantidadRecibidaAcumuladaProduccion,
                    x.CantidadPendientePorEntregar,
                    x.CantidadPendientePorConfirmar,
                    x.FechaEntregaAlmacen,
                    x.UsuarioEntregaAlmacenNombre,
                    x.ReferenciaOperacion,
                    x.ObservacionesAlmacen,
                    x.EstadoRecepcion,
                    estadoRecepcionTexto = x.EstadoRecepcionTexto,
                    x.EstaPendiente,
                    x.EstaRecibidoCompleto,
                    x.EstaRecibidoParcial,
                    x.EstaNoRecibido,
                    x.MotivoDiferencia,
                    x.ObservacionesRecepcion,
                    x.UsuarioRecepcionNombre,
                    x.FechaRecepcion,
                    x.EstadoAclaracion,
                    x.AclaracionPendiente,
                    x.GrupoLhRh,
                    x.LadoLhRh,
                    x.ProgramaParejaID,
                    x.NumeroOFPareja,
                    puedeDevolver = permisos.PuedeGestionarEmbalaje && x.EstaPendiente && x.CantidadEntregadaAlmacen > tolerancia
                })
            });
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmarRecepcionMaterialOperativa(ProduccionConfirmarRecepcionMaterialVm vm)
        {
            if (!UsuarioEnSesion())
                return Unauthorized(new { ok = false, sesionExpirada = true, mensaje = "La sesión terminó. Vuelve a iniciar sesión." });

            TempData.Remove("Success");
            TempData.Remove("Warning");
            TempData.Remove("Error");

            var resultado = await ConfirmarRecepcionMaterial(vm);

            if (resultado is StatusCodeResult statusCode)
            {
                return StatusCode(statusCode.StatusCode, new
                {
                    ok = false,
                    mensaje = statusCode.StatusCode == StatusCodes.Status403Forbidden
                        ? "No tienes permiso para confirmar la recepción."
                        : "No fue posible confirmar la recepción."
                });
            }

            var error = TempData["Error"]?.ToString();
            var warning = TempData["Warning"]?.ToString();
            var success = TempData["Success"]?.ToString();

            if (!string.IsNullOrWhiteSpace(error))
                return BadRequest(new { ok = false, mensaje = error });

            return Json(new
            {
                ok = true,
                advertencia = !string.IsNullOrWhiteSpace(warning),
                mensaje = !string.IsNullOrWhiteSpace(success)
                    ? success
                    : !string.IsNullOrWhiteSpace(warning)
                        ? warning
                        : "Recepción actualizada correctamente."
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(12000000)]
        public async Task<IActionResult> DevolverMaterialOperativa(long RecepcionMaterialID, decimal CantidadDevuelta, string? MotivoDevolucion, string? ComentarioDevolucion, IFormFile? EvidenciaDevolucion)
        {
            if (!UsuarioEnSesion())
                return Unauthorized(new { ok = false, sesionExpirada = true, mensaje = "La sesión terminó. Vuelve a iniciar sesión." });

            TempData.Remove("Success");
            TempData.Remove("Warning");
            TempData.Remove("Error");

            IActionResult resultado;
            try
            {
                resultado = await DevolverMaterial(RecepcionMaterialID, CantidadDevuelta, MotivoDevolucion, ComentarioDevolucion, EvidenciaDevolucion);
            }
            catch (Exception ex)
            {
                return BadRequest(new { ok = false, mensaje = "No fue posible registrar la devolución: " + ex.Message });
            }

            if (resultado is StatusCodeResult statusCode)
            {
                TempData.Remove("Success");
                TempData.Remove("Warning");
                TempData.Remove("Error");
                return StatusCode(statusCode.StatusCode, new
                {
                    ok = false,
                    mensaje = statusCode.StatusCode == StatusCodes.Status403Forbidden
                        ? "No tienes permiso para devolver material."
                        : "No fue posible registrar la devolución."
                });
            }

            var error = TempData["Error"]?.ToString();
            var warning = TempData["Warning"]?.ToString();
            var success = TempData["Success"]?.ToString();

            TempData.Remove("Error");
            TempData.Remove("Warning");
            TempData.Remove("Success");

            if (!string.IsNullOrWhiteSpace(error))
                return BadRequest(new { ok = false, mensaje = error });

            return Json(new
            {
                ok = true,
                advertencia = !string.IsNullOrWhiteSpace(warning),
                mensaje = !string.IsNullOrWhiteSpace(success)
                    ? success
                    : !string.IsNullOrWhiteSpace(warning)
                        ? warning
                        : "Devolución registrada correctamente.",
                refrescarCentro = true,
                refrescarCalendario = true
            });
        }

    }
}
