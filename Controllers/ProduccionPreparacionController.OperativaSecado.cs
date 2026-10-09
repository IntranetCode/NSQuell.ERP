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
        [HttpGet]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> SecadoOperativa(int programaProduccionId)
        {
            if (!UsuarioEnSesion())
                return Unauthorized(new { ok = false, sesionExpirada = true, mensaje = "La sesión terminó. Vuelve a iniciar sesión." });

            if (programaProduccionId <= 0)
                return BadRequest(new { ok = false, mensaje = "El programa de Producción no es válido." });

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
                    await SincronizarSecadoParametrosTecnicosV12Async(usuarioId, cn, tx);
                    await ConsolidarSecadosPendientesSinIniciarAsync(usuarioId, cn, tx);
                    await tx.CommitAsync();
                }
                catch (Exception ex)
                {
                    try { await tx.RollbackAsync(); } catch { }
                    return StatusCode(
                        StatusCodes.Status500InternalServerError,
                        new
                        {
                            ok = false,
                            mensaje = "No fue posible sincronizar la información de Secado: " + ex.Message
                        });
                }
            }

            var ahora = await ObtenerFechaServidorSecadoAsync(cn);
            var configuracion = await CargarConfiguracionSecadoAsync(cn);
            var tolvas = await CargarTolvasSecadoAsync(cn, incluirInactivas: true);
            var maquinas = await CargarMaquinasPreparacionAsync(cn);
            var equiposSinConfig = await CargarEquiposSecadoSinConfigAsync(cn);
            var materiales = await CargarMaterialesSecadoAsync(null, null, ahora, configuracion, cn);

            await EnriquecerSecadoLhRhAsync(materiales, ahora, configuracion, cn);

            var materialOrigen = materiales
                .FirstOrDefault(x => x.ProgramaProduccionID == programaProduccionId);

            List<ProduccionSecadoMaterialVm> materialesPrograma;

            if (materialOrigen?.GrupoLhRh.HasValue == true)
            {
                var grupo = materialOrigen.GrupoLhRh.Value;

                materialesPrograma = materiales
                    .Where(x => x.GrupoLhRh == grupo)
                    .OrderBy(x => x.LadoLhRh)
                    .ThenBy(x => x.SecadoMaterialID)
                    .ToList();
            }
            else
            {
                materialesPrograma = materiales
                    .Where(x => x.ProgramaProduccionID == programaProduccionId)
                    .OrderBy(x => x.SecadoMaterialID)
                    .ToList();
            }

            var programasRelacionados = materialesPrograma
                .Where(x => x.ProgramaProduccionID.HasValue)
                .Select(x => x.ProgramaProduccionID!.Value)
                .ToHashSet();

            programasRelacionados.Add(programaProduccionId);

            var tareasPlaneadas = await CargarPreparacionAnticipadaAsync(
                ProduccionPreparacionTipo.SecadoMaterial,
                null,
                null,
                ahora,
                false,
                cn);

            var programasConMaterial = materialesPrograma
                .Where(x => x.ProgramaProduccionID.HasValue)
                .Select(x => x.ProgramaProduccionID!.Value)
                .ToHashSet();

            var pendientesPlaneacion = tareasPlaneadas
                .Where(x =>
                    programasRelacionados.Contains(x.ProgramaProduccionID) &&
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
                PuedeGestionarSecado = permisos.PuedeGestionarSecado,
                Configuracion = configuracion,
                Tolvas = tolvas,
                Maquinas = maquinas,
                Materiales = materialesPrograma,
                PendientesPlaneacion = pendientesPlaneacion
            };

            ViewData["ProgramaProduccionID"] = programaProduccionId;
            ViewData["EquiposSecadoSinConfig"] = equiposSinConfig;

            return PartialView(
                "~/Views/ProduccionOperativa/Acciones/_SecadoContenido.cshtml",
                vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> IniciarSecadoOperativa(ProduccionIniciarSecadoVm vm)
        {
            if (!UsuarioEnSesion())
                return Unauthorized(new { ok = false, sesionExpirada = true, mensaje = "La sesión terminó. Vuelve a iniciar sesión." });

            LimpiarMensajesSecadoOperativa();

            var resultado = await IniciarSecado(vm);

            return ConvertirResultadoSecadoOperativa(
                resultado,
                "Secado iniciado correctamente.");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CambiarTolvaSecadoOperativa(ProduccionCambiarTolvaVm vm)
        {
            if (!UsuarioEnSesion())
                return Unauthorized(new { ok = false, sesionExpirada = true, mensaje = "La sesión terminó. Vuelve a iniciar sesión." });

            LimpiarMensajesSecadoOperativa();

            var resultado = await CambiarTolvaSecado(vm);

            return ConvertirResultadoSecadoOperativa(
                resultado,
                "La tolva fue actualizada correctamente.");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> FinalizarSecadoOperativa(ProduccionFinalizarSecadoVm vm)
        {
            if (!UsuarioEnSesion())
                return Unauthorized(new { ok = false, sesionExpirada = true, mensaje = "La sesión terminó. Vuelve a iniciar sesión." });

            LimpiarMensajesSecadoOperativa();

            var resultado = await FinalizarSecado(vm);

            return ConvertirResultadoSecadoOperativa(
                resultado,
                "La carga de secado fue finalizada correctamente.");
        }

        private static async Task<List<ProduccionPreparacionMaquinaVm>> CargarEquiposSecadoSinConfigAsync(SqlConnection cn)
        {
            var equipos = new List<ProduccionPreparacionMaquinaVm>();
            const string sql = @"
SELECT m.MaquinaID,m.Codigo,m.Nombre
FROM dbo.ERP_Maquinas m
WHERE m.Activo=1
  AND (UPPER(m.Area) LIKE N'%SECADO%' OR UPPER(m.Codigo) LIKE N'%TOLVA%' OR UPPER(m.Nombre) LIKE N'%TOLVA%')
  AND NOT EXISTS(SELECT 1 FROM dbo.Produccion_SecadoTolvas t WHERE t.MaquinaID=m.MaquinaID)
ORDER BY m.Codigo,m.MaquinaID;";
            await using var cmd = new SqlCommand(sql, cn);
            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
                equipos.Add(new ProduccionPreparacionMaquinaVm
                {
                    MaquinaID = Convert.ToInt32(rd["MaquinaID"]),
                    Codigo = rd["Codigo"]?.ToString()?.Trim() ?? string.Empty,
                    Nombre = rd["Nombre"]?.ToString()?.Trim() ?? string.Empty
                });
            return equipos;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfigurarEquipoSecadoOperativa(int maquinaId, decimal capacidadKg, string? tipoProcesoPermitido)
        {
            if (!UsuarioEnSesion())
                return Unauthorized(new { ok = false, sesionExpirada = true, mensaje = "La sesión terminó." });
            var proceso = (tipoProcesoPermitido ?? string.Empty).Trim().ToUpperInvariant();
            if (!ModelState.IsValid || maquinaId <= 0 || capacidadKg <= 0 || capacidadKg > 99999999999999.9999m ||
                decimal.Round(capacidadKg, 4) != capacidadKg ||
                (proceso != "SECADO" && proceso != "DESHUMIDIFICADO" && proceso != "AMBOS"))
                return BadRequest(new { ok = false, mensaje = "Selecciona el equipo, su capacidad real en KG (hasta cuatro decimales) y los procesos que permite." });

            var usuarioId = ObtenerUsuarioID();
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            var permisos = await ObtenerPermisosPreparacionUsuarioAsync(usuarioId, cn);
            if (!permisos.PuedeGestionarSecado) return StatusCode(StatusCodes.Status403Forbidden);
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                // Se configura un equipo real existente; nunca se crean capacidades supuestas.
                const string sql = @"
DECLARE @Codigo nvarchar(50),@Nombre nvarchar(150);
SELECT @Codigo=Codigo,@Nombre=Nombre
FROM dbo.ERP_Maquinas WITH(UPDLOCK,HOLDLOCK)
WHERE MaquinaID=@MaquinaID AND Activo=1
  AND (UPPER(Area) LIKE N'%SECADO%' OR UPPER(Codigo) LIKE N'%TOLVA%' OR UPPER(Nombre) LIKE N'%TOLVA%');
IF @Codigo IS NULL THROW 51321,'El equipo no está activo o no pertenece al catálogo de equipos de secado.',1;
IF EXISTS(SELECT 1 FROM dbo.Produccion_SecadoTolvas WITH(UPDLOCK,HOLDLOCK) WHERE MaquinaID=@MaquinaID)
    THROW 51322,'Este equipo ya está configurado. Actualiza Secado para ver su estado.',1;
IF EXISTS(SELECT 1 FROM dbo.Produccion_SecadoTolvas WITH(UPDLOCK,HOLDLOCK) WHERE Codigo=@Codigo)
    THROW 51323,'El código ya pertenece a otra tolva. Revisa el catálogo antes de continuar.',1;
INSERT dbo.Produccion_SecadoTolvas
(MaquinaID,Codigo,Nombre,CapacidadKg,TipoProcesoPermitido,DisponibleOperativamente,EsDatoTemporal,Observaciones,Activo,FechaCreacion,FechaModificacion)
VALUES(@MaquinaID,@Codigo,@Nombre,@CapacidadKg,@Proceso,1,0,
    N'Configurado desde Secado operativo por usuario '+CONVERT(nvarchar(20),@UsuarioID)+N'. Capacidad y proceso capturados por Producción.',1,SYSDATETIME(),NULL);";
                await using var cmd = new SqlCommand(sql, cn, tx);
                cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = maquinaId;
                var capacidad = cmd.Parameters.Add("@CapacidadKg", SqlDbType.Decimal);
                capacidad.Precision = 18; capacidad.Scale = 4; capacidad.Value = capacidadKg;
                cmd.Parameters.Add("@Proceso", SqlDbType.NVarChar, 30).Value = proceso;
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                await cmd.ExecuteNonQueryAsync();
                await tx.CommitAsync();
                return Json(new { ok = true, mensaje = "Equipo configurado. Ya puedes seleccionarlo para iniciar o cambiar una carga de secado." });
            }
            catch (Exception ex)
            {
                try { await tx.RollbackAsync(); } catch { }
                return BadRequest(new { ok = false, mensaje = "No fue posible configurar el equipo: " + ex.Message });
            }
        }

        private IActionResult ConvertirResultadoSecadoOperativa(
            IActionResult resultado,
            string mensajePredeterminado)
        {
            if (resultado is UnauthorizedResult)
            {
                return Unauthorized(new
                {
                    ok = false,
                    sesionExpirada = true,
                    mensaje = "La sesión terminó. Vuelve a iniciar sesión."
                });
            }

            if (resultado is ForbidResult)
            {
                return StatusCode(
                    StatusCodes.Status403Forbidden,
                    new
                    {
                        ok = false,
                        mensaje = "No tienes permiso para realizar esta operación de Secado."
                    });
            }

            if (resultado is StatusCodeResult statusCode &&
                statusCode.StatusCode >= 400)
            {
                return StatusCode(
                    statusCode.StatusCode,
                    new
                    {
                        ok = false,
                        mensaje = statusCode.StatusCode == StatusCodes.Status403Forbidden
                            ? "No tienes permiso para realizar esta operación de Secado."
                            : "No fue posible completar la operación de Secado."
                    });
            }

            if (resultado is RedirectToActionResult redirect &&
                string.Equals(
                    redirect.ControllerName,
                    "Login",
                    StringComparison.OrdinalIgnoreCase))
            {
                return Unauthorized(new
                {
                    ok = false,
                    sesionExpirada = true,
                    mensaje = "La sesión terminó. Vuelve a iniciar sesión."
                });
            }

            var error = ConsumirTempDataSecadoOperativa("Error");
            var warning = ConsumirTempDataSecadoOperativa("Warning");
            var success = ConsumirTempDataSecadoOperativa("Success");

            if (!string.IsNullOrWhiteSpace(error))
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje = error
                });
            }

            return Json(new
            {
                ok = true,
                mensaje = !string.IsNullOrWhiteSpace(success)
                    ? success
                    : !string.IsNullOrWhiteSpace(warning)
                        ? warning
                        : mensajePredeterminado,
                advertencia = !string.IsNullOrWhiteSpace(warning),
                warning
            });
        }

        private string? ConsumirTempDataSecadoOperativa(string clave)
        {
            if (!TempData.ContainsKey(clave))
                return null;

            var valor = TempData[clave]?.ToString();
            TempData.Remove(clave);
            return valor;
        }

        private void LimpiarMensajesSecadoOperativa()
        {
            TempData.Remove("Success");
            TempData.Remove("Warning");
            TempData.Remove("Error");
        }
    }
}
