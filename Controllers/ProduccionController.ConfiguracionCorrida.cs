using ERP.NSQuell.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;

namespace ERP.NSQuell.Controllers;

public sealed partial class ProduccionController
{
    private sealed class ProduccionConfiguracionCorridaContexto
    {
        public int EjecucionProduccionID { get; set; }
        public int ProgramaProduccionID { get; set; }

        public int? MaquinaID { get; set; }
        public string? MaquinaCodigo { get; set; }
        public string? MaquinaNombre { get; set; }

        public int? ParteID { get; set; }
        public string? NumeroParte { get; set; }
        public string? ReferenciaSAP { get; set; }

        public int EstatusID { get; set; }

        public DateTime? FechaLiberacionMaquina { get; set; }

        public int? TecnicoProduccionID { get; set; }
        public string? TecnicoProduccionNombre { get; set; }

        public int? CavidadesBD { get; set; }
        public decimal? TiempoCicloBD { get; set; }
    }

    private sealed class ProduccionUltimaLecturaContador
    {
        public long LecturaContadorID { get; set; }
        public long ValorContador { get; set; }
        public DateTime FechaLectura { get; set; }
        public string TipoLectura { get; set; } = string.Empty;
        public bool EsReinicioContador { get; set; }
    }

    // ============================================================
    // CONSULTAR CONFIGURACIÓN ACTUAL
    // Puede consultarla cualquier usuario autenticado.
    // Solo Técnico de Producción / Encargado / Administrador
    // podrá modificarla.
    // ============================================================
    [HttpGet]
    public async Task<IActionResult> ObtenerConfiguracionCorrida(
        int ejecucionProduccionId)
    {
        if (!UsuarioEnSesion())
            return Unauthorized();

        if (ejecucionProduccionId <= 0)
        {
            return BadRequest(new
            {
                ok = false,
                mensaje = "La ejecución de Producción no es válida."
            });
        }

        await using var cn =
            new SqlConnection(ConnectionString);

        await cn.OpenAsync();

        var contexto =
            await ObtenerContextoConfiguracionCorridaAsync(
                ejecucionProduccionId,
                cn);

        if (contexto == null)
        {
            return NotFound(new
            {
                ok = false,
                mensaje = "No se encontró la ejecución de Producción."
            });
        }

        var vm =
            await ConstruirConfiguracionTecnicoAsync(
                contexto,
                cn);

        var permisos =
            await ObtenerPermisosProduccionUsuarioAsync(
                ObtenerUsuarioID(),
                cn);

        var puedeModificar =
            PuedeModificarConfiguracionCorrida(
                permisos,
                contexto);

        return Json(new
        {
            ok = true,
            puedeModificar,
            configuracion = vm
        });
    }

    // ============================================================
    // CONFIGURACIÓN INICIAL
    //
    // IMPORTANTE:
    // Los valores de ERP_ParteDatosTecnicos son solamente sugerencia.
    // El técnico DEBE confirmar:
    //
    // - Cavidades realmente utilizadas.
    // - Ciclo realmente utilizado.
    // - Contador actual de la máquina.
    //
    // No se crea automáticamente desde Planeación.
    // ============================================================
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GuardarConfiguracionCorrida(
        ProduccionConfiguracionTecnicoPostVm vm)
    {
        if (!UsuarioEnSesion())
            return RedirectToAction("Login", "Login");

        if (vm.EjecucionProduccionID <= 0)
        {
            TempData["Error"] =
                "No se recibió correctamente la ejecución de Producción.";

            return RedirectToAction(nameof(Index));
        }

        var errorValidacion =
            ValidarDatosConfiguracionCorrida(
                vm,
                requiereMotivo: false);

        if (!string.IsNullOrWhiteSpace(errorValidacion))
        {
            TempData["Error"] = errorValidacion;

            return RedirectToAction(
                nameof(Detalle),
                new { id = vm.EjecucionProduccionID });
        }

        await using var cn =
            new SqlConnection(ConnectionString);

        await cn.OpenAsync();

        await using var tx =
            (SqlTransaction)await cn.BeginTransactionAsync(
                IsolationLevel.Serializable);

        try
        {
            var contexto =
                await ObtenerContextoConfiguracionCorridaAsync(
                    vm.EjecucionProduccionID,
                    cn,
                    tx);

            if (contexto == null)
            {
                await tx.RollbackAsync();

                TempData["Error"] =
                    "No se encontró la ejecución de Producción.";

                return RedirectToAction(nameof(Index));
            }

            var usuarioId =
                ObtenerUsuarioID();

            var permisos =
                await ObtenerPermisosProduccionUsuarioAsync(
                    usuarioId,
                    cn,
                    tx);

            if (!PuedeModificarConfiguracionCorrida(
                    permisos,
                    contexto))
            {
                await tx.RollbackAsync();

                TempData["Error"] =
                    "Solo el Técnico de Producción asignado, el Encargado de Producción o un Administrador pueden definir las cavidades y el ciclo real.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id = vm.EjecucionProduccionID });
            }

            if (!EjecucionPermiteConfiguracionCorrida(contexto))
            {
                await tx.RollbackAsync();

                TempData["Error"] =
                    "La configuración de cavidades y ciclo solo puede modificarse mientras la ejecución esté activa.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id = vm.EjecucionProduccionID });
            }

            var configuracionActual =
                await ObtenerConfiguracionActualAsync(
                    vm.EjecucionProduccionID,
                    cn,
                    tx);

            if (configuracionActual != null)
            {
                await tx.RollbackAsync();

                TempData["Error"] =
                    "Esta ejecución ya tiene una configuración real activa. Utiliza la opción Cambiar configuración.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id = vm.EjecucionProduccionID });
            }

            var fechaRegistro =
                DateTime.Now;

            var tecnicoRegistroId =
                ResolverTecnicoConfiguracion(
                    permisos,
                    contexto);

            var configuracionCorridaId =
                await InsertarConfiguracionCorridaAsync(
                    vm.EjecucionProduccionID,
                    vm.CavidadesUsadas,
                    vm.TiempoCicloSegundos,
                    vm.ContadorMaquinaActual!.Value,
                    fechaRegistro,
                    esConfiguracionInicial: true,
                    vm.MotivoCambio,
                    tecnicoRegistroId,
                    usuarioId,
                    cn,
                    tx);

            await RegistrarLecturaContadorConfiguracionAsync(
                vm.EjecucionProduccionID,
                configuracionCorridaId,
                contexto.MaquinaID,
                ProduccionTipoLecturaContador.InicioCorrida,
                vm.ContadorMaquinaActual.Value,
                fechaRegistro,
                esReinicioContador: false,
                motivoReinicio: null,
                observaciones:
                    "Contador base confirmado al registrar la configuración inicial de Producción.",
                usuarioId,
                cn,
                tx);

            await tx.CommitAsync();

            var objetivoHora =
                CalcularObjetivoHoraConfiguracion(
                    vm.TiempoCicloSegundos,
                    vm.CavidadesUsadas);

            TempData["Success"] =
                $"Configuración inicial confirmada: " +
                $"{vm.CavidadesUsadas:N0} cavidad(es), " +
                $"{vm.TiempoCicloSegundos:0.####} s de ciclo, " +
                $"objetivo aproximado {objetivoHora:N0} pzas/h. " +
                $"Contador base: {vm.ContadorMaquinaActual.Value:N0}.";

            return RedirectToAction(
                nameof(Detalle),
                new { id = vm.EjecucionProduccionID });
        }
        catch (Exception ex)
        {
            try
            {
                await tx.RollbackAsync();
            }
            catch
            {
            }

            TempData["Error"] =
                "No fue posible guardar la configuración real de Producción: " +
                ex.Message;

            return RedirectToAction(
                nameof(Detalle),
                new { id = vm.EjecucionProduccionID });
        }
    }

    // ============================================================
    // CAMBIAR CONFIGURACIÓN DURANTE LA CORRIDA
    //
    // Ejemplo:
    // 10:00 -> 4 cavidades
    // 10:25 -> técnico cambia a 3 cavidades
    //
    // El técnico debe indicar el contador actual para cerrar
    // matemáticamente el tramo anterior.
    // ============================================================
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CambiarConfiguracionCorrida(
        ProduccionConfiguracionTecnicoPostVm vm)
    {
        if (!UsuarioEnSesion())
            return RedirectToAction("Login", "Login");

        if (vm.EjecucionProduccionID <= 0)
        {
            TempData["Error"] =
                "No se recibió correctamente la ejecución de Producción.";

            return RedirectToAction(nameof(Index));
        }

        var errorValidacion =
            ValidarDatosConfiguracionCorrida(
                vm,
                requiereMotivo: true);

        if (!string.IsNullOrWhiteSpace(errorValidacion))
        {
            TempData["Error"] = errorValidacion;

            return RedirectToAction(
                nameof(Detalle),
                new { id = vm.EjecucionProduccionID });
        }

        await using var cn =
            new SqlConnection(ConnectionString);

        await cn.OpenAsync();

        await using var tx =
            (SqlTransaction)await cn.BeginTransactionAsync(
                IsolationLevel.Serializable);

        try
        {
            var contexto =
                await ObtenerContextoConfiguracionCorridaAsync(
                    vm.EjecucionProduccionID,
                    cn,
                    tx);

            if (contexto == null)
            {
                await tx.RollbackAsync();

                TempData["Error"] =
                    "No se encontró la ejecución de Producción.";

                return RedirectToAction(nameof(Index));
            }

            var usuarioId =
                ObtenerUsuarioID();

            var permisos =
                await ObtenerPermisosProduccionUsuarioAsync(
                    usuarioId,
                    cn,
                    tx);

            if (!PuedeModificarConfiguracionCorrida(
                    permisos,
                    contexto))
            {
                await tx.RollbackAsync();

                TempData["Error"] =
                    "Solo el Técnico de Producción asignado, el Encargado de Producción o un Administrador pueden cambiar las cavidades y el ciclo real.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id = vm.EjecucionProduccionID });
            }

            if (!EjecucionPermiteConfiguracionCorrida(contexto))
            {
                await tx.RollbackAsync();

                TempData["Error"] =
                    "La configuración de cavidades y ciclo no puede modificarse porque la ejecución ya no está activa.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id = vm.EjecucionProduccionID });
            }

            var configuracionActual =
                await ObtenerConfiguracionActualAsync(
                    vm.EjecucionProduccionID,
                    cn,
                    tx);

            if (configuracionActual == null)
            {
                await tx.RollbackAsync();

                TempData["Error"] =
                    "La ejecución todavía no tiene configuración inicial. Primero confirma las cavidades, ciclo y contador base.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id = vm.EjecucionProduccionID });
            }

            var mismasCavidades =
                configuracionActual.CavidadesUsadas ==
                vm.CavidadesUsadas;

            var mismoCiclo =
                Math.Abs(
                    configuracionActual.TiempoCicloSegundos -
                    vm.TiempoCicloSegundos) < 0.0001m;

            if (mismasCavidades && mismoCiclo)
            {
                await tx.RollbackAsync();

                TempData["Info"] =
                    "Las cavidades y el tiempo de ciclo capturados son iguales a la configuración actual.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id = vm.EjecucionProduccionID });
            }

            var ultimaLectura =
                await ObtenerUltimaLecturaContadorAsync(
                    vm.EjecucionProduccionID,
                    cn,
                    tx);

            var contadorActual =
                vm.ContadorMaquinaActual!.Value;

            var huboReinicioContador =
                ultimaLectura != null &&
                contadorActual < ultimaLectura.ValorContador;

            var fechaCambio =
                DateTime.Now;

            /*
             * Si el contador bajó, significa que físicamente fue
             * reiniciado en algún momento.
             *
             * No asignamos un ContadorFinVigencia menor al inicial.
             * El reinicio queda explícitamente registrado.
             */
            long? contadorFinConfiguracionAnterior =
                huboReinicioContador
                    ? null
                    : contadorActual;

            await CerrarConfiguracionActualAsync(
                configuracionActual.ConfiguracionCorridaID,
                contadorFinConfiguracionAnterior,
                fechaCambio,
                usuarioId,
                cn,
                tx);

            var tecnicoRegistroId =
                ResolverTecnicoConfiguracion(
                    permisos,
                    contexto);

            var nuevaConfiguracionId =
                await InsertarConfiguracionCorridaAsync(
                    vm.EjecucionProduccionID,
                    vm.CavidadesUsadas,
                    vm.TiempoCicloSegundos,
                    contadorActual,
                    fechaCambio,
                    esConfiguracionInicial: false,
                    vm.MotivoCambio,
                    tecnicoRegistroId,
                    usuarioId,
                    cn,
                    tx);

            var tipoLectura =
                huboReinicioContador
                    ? ProduccionTipoLecturaContador.ReinicioContador
                    : ProduccionTipoLecturaContador.CambioConfiguracion;

            var motivoReinicio =
                huboReinicioContador
                    ? "Se detectó que el contador actual es menor a la última lectura registrada. " +
                      (vm.MotivoCambio ?? string.Empty)
                    : null;

            await RegistrarLecturaContadorConfiguracionAsync(
                vm.EjecucionProduccionID,
                nuevaConfiguracionId,
                contexto.MaquinaID,
                tipoLectura,
                contadorActual,
                fechaCambio,
                huboReinicioContador,
                motivoReinicio,
                vm.MotivoCambio,
                usuarioId,
                cn,
                tx);

            await tx.CommitAsync();

            var objetivoAnterior =
                configuracionActual.ObjetivoHoraOperativo;

            var objetivoNuevo =
                CalcularObjetivoHoraConfiguracion(
                    vm.TiempoCicloSegundos,
                    vm.CavidadesUsadas);

            var mensaje =
                $"Configuración actualizada. " +
                $"{configuracionActual.CavidadesUsadas:N0} → {vm.CavidadesUsadas:N0} cavidad(es), " +
                $"{configuracionActual.TiempoCicloSegundos:0.####} → {vm.TiempoCicloSegundos:0.####} s. " +
                $"Objetivo: {objetivoAnterior:N0} → {objetivoNuevo:N0} pzas/h.";

            if (huboReinicioContador)
            {
                mensaje +=
                    $" Se detectó reinicio del contador; la nueva base quedó en {contadorActual:N0}.";
            }
            else
            {
                mensaje +=
                    $" Contador al cambio: {contadorActual:N0}.";
            }

            TempData["Success"] = mensaje;

            return RedirectToAction(
                nameof(Detalle),
                new { id = vm.EjecucionProduccionID });
        }
        catch (Exception ex)
        {
            try
            {
                await tx.RollbackAsync();
            }
            catch
            {
            }

            TempData["Error"] =
                "No fue posible cambiar la configuración real de Producción: " +
                ex.Message;

            return RedirectToAction(
                nameof(Detalle),
                new { id = vm.EjecucionProduccionID });
        }
    }

    // ============================================================
    // UTILIDAD PARA CARGAR ProduccionDetalleVm
    //
    // En el siguiente paso la llamaremos desde Detalle().
    // ============================================================
    private async Task CargarConfiguracionTiempoRealDetalleAsync(
        ProduccionDetalleVm vm,
        SqlConnection cn,
        SqlTransaction? tx = null)
    {
        if (vm == null)
            throw new ArgumentNullException(nameof(vm));

        if (vm.Ejecucion == null ||
            vm.Ejecucion.EjecucionProduccionID <= 0)
        {
            vm.ConfiguracionTiempoReal = null;
            return;
        }

        vm.ConfiguracionTiempoReal =
            await ConstruirConfiguracionTecnicoAsync(
                vm.Ejecucion.EjecucionProduccionID,
                cn,
                tx);
    }

    // ============================================================
    // CONSTRUIR VM DEL TÉCNICO
    // ============================================================
    private async Task<ProduccionConfiguracionTecnicoVm?>
        ConstruirConfiguracionTecnicoAsync(
            int ejecucionProduccionId,
            SqlConnection cn,
            SqlTransaction? tx = null)
    {
        var contexto =
            await ObtenerContextoConfiguracionCorridaAsync(
                ejecucionProduccionId,
                cn,
                tx);

        if (contexto == null)
            return null;

        return await ConstruirConfiguracionTecnicoAsync(
            contexto,
            cn,
            tx);
    }

    private async Task<ProduccionConfiguracionTecnicoVm>
        ConstruirConfiguracionTecnicoAsync(
            ProduccionConfiguracionCorridaContexto contexto,
            SqlConnection cn,
            SqlTransaction? tx = null)
    {
        var vm =
            new ProduccionConfiguracionTecnicoVm
            {
                EjecucionProduccionID =
                    contexto.EjecucionProduccionID,

                ProgramaProduccionID =
                    contexto.ProgramaProduccionID,

                MaquinaID =
                    contexto.MaquinaID,

                MaquinaCodigo =
                    contexto.MaquinaCodigo,

                MaquinaNombre =
                    contexto.MaquinaNombre,

                ParteID =
                    contexto.ParteID,

                NumeroParte =
                    contexto.NumeroParte,

                ReferenciaSAP =
                    contexto.ReferenciaSAP,

                CavidadesBD =
                    contexto.CavidadesBD,

                TiempoCicloBD =
                    contexto.TiempoCicloBD
            };

        vm.ConfiguracionActual =
            await ObtenerConfiguracionActualAsync(
                contexto.EjecucionProduccionID,
                cn,
                tx);

        vm.HistorialConfiguraciones =
            await ObtenerHistorialConfiguracionesAsync(
                contexto.EjecucionProduccionID,
                cn,
                tx);

        var ultimaLectura =
            await ObtenerUltimaLecturaContadorAsync(
                contexto.EjecucionProduccionID,
                cn,
                tx);

        vm.UltimoContadorMaquina =
            ultimaLectura?.ValorContador;

        return vm;
    }

    // ============================================================
    // CONTEXTO DE EJECUCIÓN + DATOS TÉCNICOS DE REFERENCIA
    // ============================================================
    private static async Task<ProduccionConfiguracionCorridaContexto?>
        ObtenerContextoConfiguracionCorridaAsync(
            int ejecucionProduccionId,
            SqlConnection cn,
            SqlTransaction? tx = null)
    {
        const string sql = @"
SELECT TOP(1)
    e.EjecucionProduccionID,
    e.ProgramaProduccionID,

    e.MaquinaID,
    e.MaquinaCodigo,
    e.MaquinaNombre,

    e.ParteID,
    e.NumeroParte,
    e.ReferenciaSAP,

    e.EstatusID,
    e.FechaLiberacionMaquina,

    e.TecnicoProduccionID,
    e.TecnicoProduccionNombre,

    TRY_CONVERT(INT,dt.Cavidades) AS CavidadesBD,

    CONVERT(NVARCHAR(100),dt.Ciclo) AS CicloBDTexto
FROM dbo.Produccion_Ejecucion e
OUTER APPLY
(
    SELECT TOP(1)
        dt0.Cavidades,
        dt0.Ciclo
    FROM dbo.ERP_ParteDatosTecnicos dt0
    WHERE dt0.ParteID=e.ParteID
      AND dt0.Activo=1
    ORDER BY dt0.ParteDatoTecnicoID DESC
) dt
WHERE e.EjecucionProduccionID=@EjecucionProduccionID
  AND e.Activo=1;";

        await using var cmd =
            tx == null
                ? new SqlCommand(sql, cn)
                : new SqlCommand(sql, cn, tx);

        cmd.Parameters.Add(
            "@EjecucionProduccionID",
            SqlDbType.Int).Value =
            ejecucionProduccionId;

        await using var rd =
            await cmd.ExecuteReaderAsync();

        if (!await rd.ReadAsync())
            return null;

        var cicloTexto =
            rd["CicloBDTexto"] == DBNull.Value
                ? null
                : rd["CicloBDTexto"]?.ToString();

        return new ProduccionConfiguracionCorridaContexto
        {
            EjecucionProduccionID =
                Convert.ToInt32(
                    rd["EjecucionProduccionID"]),

            ProgramaProduccionID =
                Convert.ToInt32(
                    rd["ProgramaProduccionID"]),

            MaquinaID =
                rd["MaquinaID"] == DBNull.Value
                    ? null
                    : Convert.ToInt32(
                        rd["MaquinaID"]),

            MaquinaCodigo =
                rd["MaquinaCodigo"] == DBNull.Value
                    ? null
                    : rd["MaquinaCodigo"]?.ToString(),

            MaquinaNombre =
                rd["MaquinaNombre"] == DBNull.Value
                    ? null
                    : rd["MaquinaNombre"]?.ToString(),

            ParteID =
                rd["ParteID"] == DBNull.Value
                    ? null
                    : Convert.ToInt32(
                        rd["ParteID"]),

            NumeroParte =
                rd["NumeroParte"] == DBNull.Value
                    ? null
                    : rd["NumeroParte"]?.ToString(),

            ReferenciaSAP =
                rd["ReferenciaSAP"] == DBNull.Value
                    ? null
                    : rd["ReferenciaSAP"]?.ToString(),

            EstatusID =
                Convert.ToInt32(
                    rd["EstatusID"]),

            FechaLiberacionMaquina =
                rd["FechaLiberacionMaquina"] == DBNull.Value
                    ? null
                    : Convert.ToDateTime(
                        rd["FechaLiberacionMaquina"]),

            TecnicoProduccionID =
                rd["TecnicoProduccionID"] == DBNull.Value
                    ? null
                    : Convert.ToInt32(
                        rd["TecnicoProduccionID"]),

            TecnicoProduccionNombre =
                rd["TecnicoProduccionNombre"] == DBNull.Value
                    ? null
                    : rd["TecnicoProduccionNombre"]?.ToString(),

            CavidadesBD =
                rd["CavidadesBD"] == DBNull.Value
                    ? null
                    : Convert.ToInt32(
                        rd["CavidadesBD"]),

            TiempoCicloBD =
                ConvertirDecimalFlexibleConfiguracion(
                    cicloTexto)
        };
    }

    // ============================================================
    // CONFIGURACIÓN ACTUAL
    // ============================================================
    private static async Task<ProduccionConfiguracionCorridaVm?>
        ObtenerConfiguracionActualAsync(
            int ejecucionProduccionId,
            SqlConnection cn,
            SqlTransaction? tx = null)
    {
        var sql =
            tx == null
                ? @"
SELECT TOP(1)
    c.ConfiguracionCorridaID,
    c.EjecucionProduccionID,
    c.CavidadesUsadas,
    c.TiempoCicloSegundos,
    c.ObjetivoHoraCalculado,
    c.ContadorInicioVigencia,
    c.ContadorFinVigencia,
    c.FechaInicioVigencia,
    c.FechaFinVigencia,
    c.EsConfiguracionInicial,
    c.MotivoCambio,
    c.TecnicoProduccionID,
    NULLIF(
        LTRIM(RTRIM(
            CONCAT(
                ISNULL(p.Nombre,N''),
                N' ',
                ISNULL(p.ApellidoPaterno,N''),
                N' ',
                ISNULL(p.ApellidoMaterno,N'')
            )
        )),
        N''
    ) AS TecnicoProduccionNombre,
    c.UsuarioCreacionID,
    c.FechaCreacion,
    c.UsuarioModificacionID,
    c.FechaModificacion,
    c.Activo
FROM dbo.Produccion_ConfiguracionCorrida c
LEFT JOIN dbo.Persona p
    ON p.PersonaID=c.TecnicoProduccionID
WHERE c.EjecucionProduccionID=@EjecucionProduccionID
  AND c.Activo=1
  AND c.FechaFinVigencia IS NULL
ORDER BY c.ConfiguracionCorridaID DESC;"
                : @"
SELECT TOP(1)
    c.ConfiguracionCorridaID,
    c.EjecucionProduccionID,
    c.CavidadesUsadas,
    c.TiempoCicloSegundos,
    c.ObjetivoHoraCalculado,
    c.ContadorInicioVigencia,
    c.ContadorFinVigencia,
    c.FechaInicioVigencia,
    c.FechaFinVigencia,
    c.EsConfiguracionInicial,
    c.MotivoCambio,
    c.TecnicoProduccionID,
    NULLIF(
        LTRIM(RTRIM(
            CONCAT(
                ISNULL(p.Nombre,N''),
                N' ',
                ISNULL(p.ApellidoPaterno,N''),
                N' ',
                ISNULL(p.ApellidoMaterno,N'')
            )
        )),
        N''
    ) AS TecnicoProduccionNombre,
    c.UsuarioCreacionID,
    c.FechaCreacion,
    c.UsuarioModificacionID,
    c.FechaModificacion,
    c.Activo
FROM dbo.Produccion_ConfiguracionCorrida c WITH(UPDLOCK,HOLDLOCK)
LEFT JOIN dbo.Persona p
    ON p.PersonaID=c.TecnicoProduccionID
WHERE c.EjecucionProduccionID=@EjecucionProduccionID
  AND c.Activo=1
  AND c.FechaFinVigencia IS NULL
ORDER BY c.ConfiguracionCorridaID DESC;";

        await using var cmd =
            tx == null
                ? new SqlCommand(sql, cn)
                : new SqlCommand(sql, cn, tx);

        cmd.Parameters.Add(
            "@EjecucionProduccionID",
            SqlDbType.Int).Value =
            ejecucionProduccionId;

        await using var rd =
            await cmd.ExecuteReaderAsync();

        return await rd.ReadAsync()
            ? MapearConfiguracionCorrida(rd)
            : null;
    }

    // ============================================================
    // HISTORIAL DE CONFIGURACIONES
    // ============================================================
    private static async Task<List<ProduccionConfiguracionCorridaVm>>
        ObtenerHistorialConfiguracionesAsync(
            int ejecucionProduccionId,
            SqlConnection cn,
            SqlTransaction? tx = null)
    {
        const string sql = @"
SELECT
    c.ConfiguracionCorridaID,
    c.EjecucionProduccionID,
    c.CavidadesUsadas,
    c.TiempoCicloSegundos,
    c.ObjetivoHoraCalculado,
    c.ContadorInicioVigencia,
    c.ContadorFinVigencia,
    c.FechaInicioVigencia,
    c.FechaFinVigencia,
    c.EsConfiguracionInicial,
    c.MotivoCambio,
    c.TecnicoProduccionID,
    NULLIF(
        LTRIM(RTRIM(
            CONCAT(
                ISNULL(p.Nombre,N''),
                N' ',
                ISNULL(p.ApellidoPaterno,N''),
                N' ',
                ISNULL(p.ApellidoMaterno,N'')
            )
        )),
        N''
    ) AS TecnicoProduccionNombre,
    c.UsuarioCreacionID,
    c.FechaCreacion,
    c.UsuarioModificacionID,
    c.FechaModificacion,
    c.Activo
FROM dbo.Produccion_ConfiguracionCorrida c
LEFT JOIN dbo.Persona p
    ON p.PersonaID=c.TecnicoProduccionID
WHERE c.EjecucionProduccionID=@EjecucionProduccionID
  AND c.Activo=1
ORDER BY
    c.FechaInicioVigencia DESC,
    c.ConfiguracionCorridaID DESC;";

        var lista =
            new List<ProduccionConfiguracionCorridaVm>();

        await using var cmd =
            tx == null
                ? new SqlCommand(sql, cn)
                : new SqlCommand(sql, cn, tx);

        cmd.Parameters.Add(
            "@EjecucionProduccionID",
            SqlDbType.Int).Value =
            ejecucionProduccionId;

        await using var rd =
            await cmd.ExecuteReaderAsync();

        while (await rd.ReadAsync())
        {
            lista.Add(
                MapearConfiguracionCorrida(rd));
        }

        return lista;
    }

    private static ProduccionConfiguracionCorridaVm
        MapearConfiguracionCorrida(
            SqlDataReader rd)
    {
        return new ProduccionConfiguracionCorridaVm
        {
            ConfiguracionCorridaID =
                Convert.ToInt32(
                    rd["ConfiguracionCorridaID"]),

            EjecucionProduccionID =
                Convert.ToInt32(
                    rd["EjecucionProduccionID"]),

            CavidadesUsadas =
                Convert.ToInt32(
                    rd["CavidadesUsadas"]),

            TiempoCicloSegundos =
                Convert.ToDecimal(
                    rd["TiempoCicloSegundos"]),

            ObjetivoHoraCalculado =
                Convert.ToDecimal(
                    rd["ObjetivoHoraCalculado"]),

            ContadorInicioVigencia =
                rd["ContadorInicioVigencia"] == DBNull.Value
                    ? null
                    : Convert.ToInt64(
                        rd["ContadorInicioVigencia"]),

            ContadorFinVigencia =
                rd["ContadorFinVigencia"] == DBNull.Value
                    ? null
                    : Convert.ToInt64(
                        rd["ContadorFinVigencia"]),

            FechaInicioVigencia =
                Convert.ToDateTime(
                    rd["FechaInicioVigencia"]),

            FechaFinVigencia =
                rd["FechaFinVigencia"] == DBNull.Value
                    ? null
                    : Convert.ToDateTime(
                        rd["FechaFinVigencia"]),

            EsConfiguracionInicial =
                Convert.ToBoolean(
                    rd["EsConfiguracionInicial"]),

            MotivoCambio =
                rd["MotivoCambio"] == DBNull.Value
                    ? null
                    : rd["MotivoCambio"]?.ToString(),

            TecnicoProduccionID =
                rd["TecnicoProduccionID"] == DBNull.Value
                    ? null
                    : Convert.ToInt32(
                        rd["TecnicoProduccionID"]),

            TecnicoProduccionNombre =
                rd["TecnicoProduccionNombre"] == DBNull.Value
                    ? null
                    : rd["TecnicoProduccionNombre"]?.ToString(),

            UsuarioCreacionID =
                rd["UsuarioCreacionID"] == DBNull.Value
                    ? null
                    : Convert.ToInt32(
                        rd["UsuarioCreacionID"]),

            FechaCreacion =
                Convert.ToDateTime(
                    rd["FechaCreacion"]),

            UsuarioModificacionID =
                rd["UsuarioModificacionID"] == DBNull.Value
                    ? null
                    : Convert.ToInt32(
                        rd["UsuarioModificacionID"]),

            FechaModificacion =
                rd["FechaModificacion"] == DBNull.Value
                    ? null
                    : Convert.ToDateTime(
                        rd["FechaModificacion"]),

            Activo =
                Convert.ToBoolean(
                    rd["Activo"])
        };
    }

    // ============================================================
    // INSERTAR CONFIGURACIÓN
    // ============================================================
    private static async Task<int>
        InsertarConfiguracionCorridaAsync(
            int ejecucionProduccionId,
            int cavidadesUsadas,
            decimal tiempoCicloSegundos,
            long contadorInicio,
            DateTime fechaInicio,
            bool esConfiguracionInicial,
            string? motivoCambio,
            int? tecnicoProduccionId,
            int usuarioId,
            SqlConnection cn,
            SqlTransaction tx)
    {
        const string sql = @"
INSERT INTO dbo.Produccion_ConfiguracionCorrida
(
    EjecucionProduccionID,
    CavidadesUsadas,
    TiempoCicloSegundos,
    ContadorInicioVigencia,
    ContadorFinVigencia,
    FechaInicioVigencia,
    FechaFinVigencia,
    EsConfiguracionInicial,
    MotivoCambio,
    TecnicoProduccionID,
    UsuarioCreacionID,
    FechaCreacion,
    Activo
)
OUTPUT INSERTED.ConfiguracionCorridaID
VALUES
(
    @EjecucionProduccionID,
    @CavidadesUsadas,
    @TiempoCicloSegundos,
    @ContadorInicioVigencia,
    NULL,
    @FechaInicioVigencia,
    NULL,
    @EsConfiguracionInicial,
    @MotivoCambio,
    @TecnicoProduccionID,
    @UsuarioID,
    @FechaInicioVigencia,
    1
);";

        await using var cmd =
            new SqlCommand(
                sql,
                cn,
                tx);

        cmd.Parameters.Add(
            "@EjecucionProduccionID",
            SqlDbType.Int).Value =
            ejecucionProduccionId;

        cmd.Parameters.Add(
            "@CavidadesUsadas",
            SqlDbType.Int).Value =
            cavidadesUsadas;

        var pCiclo =
            cmd.Parameters.Add(
                "@TiempoCicloSegundos",
                SqlDbType.Decimal);

        pCiclo.Precision = 18;
        pCiclo.Scale = 4;
        pCiclo.Value = tiempoCicloSegundos;

        cmd.Parameters.Add(
            "@ContadorInicioVigencia",
            SqlDbType.BigInt).Value =
            contadorInicio;

        cmd.Parameters.Add(
            "@FechaInicioVigencia",
            SqlDbType.DateTime2).Value =
            fechaInicio;

        cmd.Parameters.Add(
            "@EsConfiguracionInicial",
            SqlDbType.Bit).Value =
            esConfiguracionInicial;

        cmd.Parameters.Add(
            "@MotivoCambio",
            SqlDbType.NVarChar,
            500).Value =
            string.IsNullOrWhiteSpace(motivoCambio)
                ? DBNull.Value
                : motivoCambio.Trim();

        cmd.Parameters.Add(
            "@TecnicoProduccionID",
            SqlDbType.Int).Value =
            (object?)tecnicoProduccionId ??
            DBNull.Value;

        cmd.Parameters.Add(
            "@UsuarioID",
            SqlDbType.Int).Value =
            usuarioId;

        var resultado =
            await cmd.ExecuteScalarAsync();

        if (resultado == null ||
            resultado == DBNull.Value)
        {
            throw new InvalidOperationException(
                "No fue posible recuperar el identificador de la configuración creada.");
        }

        return Convert.ToInt32(resultado);
    }

    // ============================================================
    // CERRAR CONFIGURACIÓN VIGENTE
    // ============================================================
    private static async Task
        CerrarConfiguracionActualAsync(
            int configuracionCorridaId,
            long? contadorFin,
            DateTime fechaFin,
            int usuarioId,
            SqlConnection cn,
            SqlTransaction tx)
    {
        const string sql = @"
UPDATE dbo.Produccion_ConfiguracionCorrida
SET
    ContadorFinVigencia=@ContadorFinVigencia,
    FechaFinVigencia=@FechaFinVigencia,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=@FechaFinVigencia
WHERE ConfiguracionCorridaID=@ConfiguracionCorridaID
  AND Activo=1
  AND FechaFinVigencia IS NULL;";

        await using var cmd =
            new SqlCommand(
                sql,
                cn,
                tx);

        cmd.Parameters.Add(
            "@ConfiguracionCorridaID",
            SqlDbType.Int).Value =
            configuracionCorridaId;

        cmd.Parameters.Add(
            "@ContadorFinVigencia",
            SqlDbType.BigInt).Value =
            contadorFin.HasValue
                ? contadorFin.Value
                : DBNull.Value;

        cmd.Parameters.Add(
            "@FechaFinVigencia",
            SqlDbType.DateTime2).Value =
            fechaFin;

        cmd.Parameters.Add(
            "@UsuarioID",
            SqlDbType.Int).Value =
            usuarioId;

        var filas =
            await cmd.ExecuteNonQueryAsync();

        if (filas != 1)
        {
            throw new InvalidOperationException(
                "La configuración anterior ya no estaba disponible para ser cerrada.");
        }
    }

    // ============================================================
    // ÚLTIMA LECTURA DEL CONTADOR
    // ============================================================
    private static async Task<ProduccionUltimaLecturaContador?>
        ObtenerUltimaLecturaContadorAsync(
            int ejecucionProduccionId,
            SqlConnection cn,
            SqlTransaction? tx = null)
    {
        var sql =
            tx == null
                ? @"
SELECT TOP(1)
    LecturaContadorID,
    ValorContador,
    FechaLectura,
    TipoLectura,
    EsReinicioContador
FROM dbo.Produccion_ContadorMaquinaLecturas
WHERE EjecucionProduccionID=@EjecucionProduccionID
  AND Activo=1
ORDER BY
    FechaLectura DESC,
    LecturaContadorID DESC;"
                : @"
SELECT TOP(1)
    LecturaContadorID,
    ValorContador,
    FechaLectura,
    TipoLectura,
    EsReinicioContador
FROM dbo.Produccion_ContadorMaquinaLecturas WITH(UPDLOCK,HOLDLOCK)
WHERE EjecucionProduccionID=@EjecucionProduccionID
  AND Activo=1
ORDER BY
    FechaLectura DESC,
    LecturaContadorID DESC;";

        await using var cmd =
            tx == null
                ? new SqlCommand(sql, cn)
                : new SqlCommand(sql, cn, tx);

        cmd.Parameters.Add(
            "@EjecucionProduccionID",
            SqlDbType.Int).Value =
            ejecucionProduccionId;

        await using var rd =
            await cmd.ExecuteReaderAsync();

        if (!await rd.ReadAsync())
            return null;

        return new ProduccionUltimaLecturaContador
        {
            LecturaContadorID =
                Convert.ToInt64(
                    rd["LecturaContadorID"]),

            ValorContador =
                Convert.ToInt64(
                    rd["ValorContador"]),

            FechaLectura =
                Convert.ToDateTime(
                    rd["FechaLectura"]),

            TipoLectura =
                rd["TipoLectura"]?.ToString() ??
                string.Empty,

            EsReinicioContador =
                rd["EsReinicioContador"] != DBNull.Value &&
                Convert.ToBoolean(
                    rd["EsReinicioContador"])
        };
    }

    // ============================================================
    // GUARDAR LECTURA DEL CONTADOR
    //
    // OperadorID queda NULL porque esta lectura particular es
    // realizada como parte de una acción técnica.
    //
    // Las capturas horarias del operador sí tendrán OperadorID.
    // ============================================================
    private static async Task
        RegistrarLecturaContadorConfiguracionAsync(
            int ejecucionProduccionId,
            int configuracionCorridaId,
            int? maquinaId,
            string tipoLectura,
            long valorContador,
            DateTime fechaLectura,
            bool esReinicioContador,
            string? motivoReinicio,
            string? observaciones,
            int usuarioId,
            SqlConnection cn,
            SqlTransaction tx)
    {
        const string sql = @"
INSERT INTO dbo.Produccion_ContadorMaquinaLecturas
(
    EjecucionProduccionID,
    ConfiguracionCorridaID,
    MaquinaID,
    OperadorID,
    TipoLectura,
    ValorContador,
    FechaLectura,
    EsReinicioContador,
    MotivoReinicio,
    Observaciones,
    RegistroHoraID,
    UsuarioCreacionID,
    FechaCreacion,
    Activo
)
VALUES
(
    @EjecucionProduccionID,
    @ConfiguracionCorridaID,
    @MaquinaID,
    NULL,
    @TipoLectura,
    @ValorContador,
    @FechaLectura,
    @EsReinicioContador,
    @MotivoReinicio,
    @Observaciones,
    NULL,
    @UsuarioID,
    @FechaLectura,
    1
);";

        await using var cmd =
            new SqlCommand(
                sql,
                cn,
                tx);

        cmd.Parameters.Add(
            "@EjecucionProduccionID",
            SqlDbType.Int).Value =
            ejecucionProduccionId;

        cmd.Parameters.Add(
            "@ConfiguracionCorridaID",
            SqlDbType.Int).Value =
            configuracionCorridaId;

        cmd.Parameters.Add(
            "@MaquinaID",
            SqlDbType.Int).Value =
            (object?)maquinaId ??
            DBNull.Value;

        cmd.Parameters.Add(
            "@TipoLectura",
            SqlDbType.NVarChar,
            50).Value =
            tipoLectura;

        cmd.Parameters.Add(
            "@ValorContador",
            SqlDbType.BigInt).Value =
            valorContador;

        cmd.Parameters.Add(
            "@FechaLectura",
            SqlDbType.DateTime2).Value =
            fechaLectura;

        cmd.Parameters.Add(
            "@EsReinicioContador",
            SqlDbType.Bit).Value =
            esReinicioContador;

        cmd.Parameters.Add(
            "@MotivoReinicio",
            SqlDbType.NVarChar,
            500).Value =
            string.IsNullOrWhiteSpace(motivoReinicio)
                ? DBNull.Value
                : motivoReinicio.Trim();

        cmd.Parameters.Add(
            "@Observaciones",
            SqlDbType.NVarChar,
            500).Value =
            string.IsNullOrWhiteSpace(observaciones)
                ? DBNull.Value
                : observaciones.Trim();

        cmd.Parameters.Add(
            "@UsuarioID",
            SqlDbType.Int).Value =
            usuarioId;

        await cmd.ExecuteNonQueryAsync();
    }

    // ============================================================
    // PERMISOS
    //
    // Técnico:
    //   puede configurar si es el técnico asignado.
    //
    // Encargado / Administrador:
    //   pueden intervenir como excepción administrativa.
    //
    // Operador / Auxiliar / SMED:
    //   NO pueden cambiar cavidades ni ciclo.
    // ============================================================
    private static bool PuedeModificarConfiguracionCorrida(
        ProduccionPermisosUsuario permisos,
        ProduccionConfiguracionCorridaContexto contexto)
    {
        if (permisos.PuedeVerTodo)
            return true;

        if (!permisos.EsTecnicoProduccion)
            return false;

        if (!permisos.PersonaID.HasValue ||
            permisos.PersonaID.Value <= 0)
        {
            return false;
        }

        if (contexto.TecnicoProduccionID.HasValue &&
            contexto.TecnicoProduccionID.Value > 0 &&
            contexto.TecnicoProduccionID.Value !=
            permisos.PersonaID.Value)
        {
            return false;
        }

        return true;
    }

    private static int? ResolverTecnicoConfiguracion(
        ProduccionPermisosUsuario permisos,
        ProduccionConfiguracionCorridaContexto contexto)
    {
        if (permisos.EsTecnicoProduccion &&
            permisos.PersonaID.HasValue &&
            permisos.PersonaID.Value > 0)
        {
            return permisos.PersonaID.Value;
        }

        if (contexto.TecnicoProduccionID.HasValue &&
            contexto.TecnicoProduccionID.Value > 0)
        {
            return contexto.TecnicoProduccionID.Value;
        }

        if (permisos.PersonaID.HasValue &&
            permisos.PersonaID.Value > 0)
        {
            return permisos.PersonaID.Value;
        }

        return null;
    }

    // ============================================================
    // ESTADO PERMITIDO
    // ============================================================
    private static bool EjecucionPermiteConfiguracionCorrida(
        ProduccionConfiguracionCorridaContexto contexto)
    {
        if (contexto.FechaLiberacionMaquina.HasValue)
            return false;

        return
            contexto.EstatusID ==
                ProduccionEstatus.EnPreparacion ||
            contexto.EstatusID ==
                ProduccionEstatus.EnProduccion ||
            contexto.EstatusID ==
                ProduccionEstatus.Pausado;
    }

    // ============================================================
    // VALIDACIONES
    // ============================================================
    private static string?
        ValidarDatosConfiguracionCorrida(
            ProduccionConfiguracionTecnicoPostVm vm,
            bool requiereMotivo)
    {
        if (vm.CavidadesUsadas <= 0)
        {
            return
                "El técnico debe indicar cuántas cavidades se están utilizando realmente.";
        }

        if (vm.TiempoCicloSegundos <= 0)
        {
            return
                "El técnico debe indicar el tiempo de ciclo real en segundos.";
        }

        if (!vm.ContadorMaquinaActual.HasValue)
        {
            return
                "El técnico debe indicar el contador actual de la máquina para establecer el punto de inicio de la configuración.";
        }

        if (vm.ContadorMaquinaActual.Value < 0)
        {
            return
                "El contador de la máquina no puede ser negativo.";
        }

        if (!string.IsNullOrWhiteSpace(vm.MotivoCambio) &&
            vm.MotivoCambio.Trim().Length > 500)
        {
            return
                "El motivo u observaciones no pueden superar 500 caracteres.";
        }

        if (requiereMotivo &&
            string.IsNullOrWhiteSpace(vm.MotivoCambio))
        {
            return
                "Debes indicar el motivo por el que cambian las cavidades o el ciclo.";
        }

        return null;
    }

    // ============================================================
    // FÓRMULA DE OBJETIVO
    //
    // Objetivo por hora =
    // (3600 / ciclo segundos) * cavidades reales
    // ============================================================
    private static int CalcularObjetivoHoraConfiguracion(
        decimal tiempoCicloSegundos,
        int cavidadesUsadas)
    {
        if (tiempoCicloSegundos <= 0 ||
            cavidadesUsadas <= 0)
        {
            return 0;
        }

        var objetivo =
            (3600m / tiempoCicloSegundos) *
            cavidadesUsadas;

        return (int)Math.Round(
            objetivo,
            0,
            MidpointRounding.AwayFromZero);
    }

   
    private static decimal?
        ConvertirDecimalFlexibleConfiguracion(
            string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
            return null;

        var texto =
            valor.Trim();

        if (decimal.TryParse(
                texto,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var directoInvariant))
        {
            return directoInvariant;
        }

        if (decimal.TryParse(
                texto,
                NumberStyles.Number,
                CultureInfo.CurrentCulture,
                out var directoActual))
        {
            return directoActual;
        }

        var match =
            Regex.Match(
                texto,
                @"[-+]?\d+(?:[\.,]\d+)?");

        if (!match.Success)
            return null;

        var numero =
            match.Value.Replace(',', '.');

        return decimal.TryParse(
                numero,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var convertido)
            ? convertido
            : null;
    }
}