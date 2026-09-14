using ERP.NSQuell.Servicios.Produccion;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System;
using System.Data;
using System.Threading.Tasks;

namespace ERP.NSQuell.Controllers
{
    public sealed partial class ProduccionController
    {
        // NSQ_FIN_ANTICIPADO_V1
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> FinalizarAnticipadamente(
            int ejecucionProduccionId,
            string? motivo)
        {
            if (!UsuarioEnSesion())
                return RedirectToAction("Login", "Login");

            if (ejecucionProduccionId <= 0)
                return NotFound();

            motivo = motivo?.Trim();

            if (string.IsNullOrWhiteSpace(motivo) || motivo.Length < 10)
            {
                TempData["Error"] =
                    "Para finalizar anticipadamente debes escribir una justificación de al menos 10 caracteres.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id = ejecucionProduccionId });
            }

            if (motivo.Length > 500)
                motivo = motivo[..500];

            var usuarioId = ObtenerUsuarioID();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            var permisos =
                await ObtenerPermisosProduccionUsuarioAsync(usuarioId, cn);

            var autorizado =
                permisos.EsAdministradorERP ||
                permisos.EsEncargadoProduccion ||
                permisos.EsAuxiliarProduccion ||
                permisos.EsTecnicoProduccion;

            if (!autorizado)
            {
                TempData["Error"] =
                    "La finalización anticipada sólo puede ejecutarla Administrador ERP, Encargado, Auxiliar o Técnico de Producción.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id = ejecucionProduccionId });
            }

            await using var tx =
                (SqlTransaction)await cn.BeginTransactionAsync(
                    IsolationLevel.Serializable);

            try
            {
                var resultado =
                    await TerminacionAnticipadaProduccionService.EjecutarAsync(
                        ejecucionProduccionId,
                        motivo,
                        usuarioId,
                        cn,
                        tx);

                await tx.CommitAsync();

                TempData["Success"] =
                    $"Producción finalizada anticipadamente. " +
                    $"Totales reales conservados: OK {resultado.CantidadOK:N0}, " +
                    $"sospechoso {resultado.CantidadSospechosa:N0}, " +
                    $"scrap {resultado.CantidadScrap:N0}. " +
                    $"Se cancelaron {resultado.MonitoreosCancelados:N0} monitoreo(s) futuro(s) pendiente(s).";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id = ejecucionProduccionId });
            }
            catch (Exception ex)
            {
                try { await tx.RollbackAsync(); } catch { }

                TempData["Error"] =
                    "No fue posible finalizar anticipadamente la producción: " +
                    ex.Message;

                return RedirectToAction(
                    nameof(Detalle),
                    new { id = ejecucionProduccionId });
            }
        }
    }
}
