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

    private sealed class ProduccionRecalculoConfiguracionContexto
    {
        public int CantidadProgramada { get; set; }
        public int CantidadProducida { get; set; }
        public decimal HorasProgramadas { get; set; }
        public DateTime FechaFinProgramada { get; set; }
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
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GuardarConfiguracionCorrida(ProduccionConfiguracionTecnicoPostVm vm)
    {
        if (!UsuarioEnSesion()) return RedirectToAction("Login", "Login");
        if (vm.EjecucionProduccionID <= 0)
        {
            TempData["Error"] = "No se recibió correctamente la ejecución de Producción.";
            return RedirectToAction(nameof(Index));
        }
        var errorValidacion = ValidarDatosConfiguracionCorrida(vm, false);
        if (!string.IsNullOrWhiteSpace(errorValidacion))
        {
            TempData["Error"] = errorValidacion;
            return RedirectToAction(nameof(Detalle), new { id = vm.EjecucionProduccionID });
        }
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var contexto = await ObtenerContextoConfiguracionCorridaAsync(vm.EjecucionProduccionID, cn, tx);
            if (contexto == null) throw new InvalidOperationException("No se encontró la ejecución de Producción.");
            var usuarioId = ObtenerUsuarioID();
            var permisos = await ObtenerPermisosProduccionUsuarioAsync(usuarioId, cn, tx);
            if (!PuedeModificarConfiguracionCorrida(permisos, contexto)) throw new InvalidOperationException("Solo el Técnico de Producción asignado, el Encargado de Producción o un Administrador pueden definir las cavidades y el ciclo real.");
            if (!EjecucionPermiteConfiguracionCorrida(contexto)) throw new InvalidOperationException("La configuración de cavidades y ciclo solo puede modificarse mientras la ejecución esté activa.");
            var pareja = await ObtenerParejaLhRhProduccionAsync(contexto.ProgramaProduccionID, cn, tx);
            ProduccionConfiguracionCorridaContexto? contextoPareja = null;
            if (pareja != null)
            {
                if (!pareja.TieneEjecucionPareja) throw new InvalidOperationException($"La OF pertenece a la pareja LH/RH grupo {pareja.GrupoLhRh}, pero todavía no existe una ejecución activa para {pareja.OFParejaTexto}. Primero deben existir las dos ejecuciones.");
                if (!pareja.EsCompatibleFisicamente) throw new InvalidOperationException("La pareja LH/RH ya no conserva la misma máquina, molde y ventana programada. Corrige Planeación antes de configurar la corrida.");
                contextoPareja = await ObtenerContextoConfiguracionCorridaAsync(pareja.EjecucionParejaID!.Value, cn, tx);
                if (contextoPareja == null || contextoPareja.ProgramaProduccionID != pareja.ProgramaParejaID) throw new InvalidOperationException("No fue posible recuperar correctamente la ejecución pareja LH/RH.");
                if (contexto.MaquinaID != contextoPareja.MaquinaID) throw new InvalidOperationException("Las dos ejecuciones LH/RH no están relacionadas con la misma máquina física.");
                if (!PuedeModificarConfiguracionCorrida(permisos, contextoPareja)) throw new InvalidOperationException("No tienes permiso para definir la configuración de la ejecución pareja LH/RH.");
                if (!EjecucionPermiteConfiguracionCorrida(contextoPareja)) throw new InvalidOperationException("La ejecución pareja LH/RH ya no permite modificar su configuración.");
            }
            var configuracionActual = await ObtenerConfiguracionActualAsync(contexto.EjecucionProduccionID, cn, tx);
            if (configuracionActual != null) throw new InvalidOperationException("Esta ejecución ya tiene una configuración real activa. Utiliza la opción Cambiar configuración.");
            ProduccionConfiguracionCorridaVm? configuracionActualPareja = null;
            if (contextoPareja != null)
            {
                configuracionActualPareja = await ObtenerConfiguracionActualAsync(contextoPareja.EjecucionProduccionID, cn, tx);
                if (configuracionActualPareja != null) throw new InvalidOperationException($"La pareja {pareja!.OFParejaTexto} ya tiene una configuración activa. No se creará una configuración LH/RH parcial.");
            }
            var cavidades = await ResolverCavidadesConfiguracionAsync(vm.CavidadesUsadas, vm.CavidadesConfiguradas, contexto.ParteID, contexto.CavidadesBD, null, false, "la OF actual", cn, tx);
            vm.CavidadesUsadas = cavidades.Cantidad;
            vm.CavidadesConfiguradas = cavidades.Detalle;
            (int Cantidad, string? Detalle)? cavidadesPareja = null;
            if (contextoPareja != null)
            {
                cavidadesPareja = await ResolverCavidadesConfiguracionAsync(vm.CavidadesUsadasPareja ?? 0, vm.CavidadesConfiguradasPareja, contextoPareja.ParteID, contextoPareja.CavidadesBD, null, false, $"la pareja {pareja!.OFParejaTexto}", cn, tx);
                vm.CavidadesUsadasPareja = cavidadesPareja.Value.Cantidad;
                vm.CavidadesConfiguradasPareja = cavidadesPareja.Value.Detalle;
            }
            var fechaRegistro = DateTime.Now;
            var contador = vm.ContadorMaquinaActual!.Value;
            var tecnicoRegistroId = ResolverTecnicoConfiguracion(permisos, contexto);
            var configuracionId = await InsertarConfiguracionCorridaAsync(contexto.EjecucionProduccionID, vm.CavidadesUsadas, vm.CavidadesConfiguradas, vm.TiempoCicloSegundos, contador, fechaRegistro, true, vm.MotivoCambio, tecnicoRegistroId, usuarioId, cn, tx);
            await RegistrarLecturaContadorConfiguracionAsync(contexto.EjecucionProduccionID, configuracionId, contexto.MaquinaID, ProduccionTipoLecturaContador.InicioCorrida, contador, fechaRegistro, false, null, pareja == null ? "Contador base confirmado al registrar la configuración inicial de Producción." : $"Contador físico base compartido de la pareja LH/RH grupo {pareja.GrupoLhRh}.", usuarioId, cn, tx);
            int? configuracionParejaId = null;
            if (contextoPareja != null && cavidadesPareja.HasValue)
            {
                var tecnicoParejaId = ResolverTecnicoConfiguracion(permisos, contextoPareja);
                configuracionParejaId = await InsertarConfiguracionCorridaAsync(contextoPareja.EjecucionProduccionID, cavidadesPareja.Value.Cantidad, cavidadesPareja.Value.Detalle, vm.TiempoCicloSegundos, contador, fechaRegistro, true, vm.MotivoCambio, tecnicoParejaId, usuarioId, cn, tx);
                await RegistrarLecturaContadorConfiguracionAsync(contextoPareja.EjecucionProduccionID, configuracionParejaId.Value, contextoPareja.MaquinaID, ProduccionTipoLecturaContador.InicioCorrida, contador, fechaRegistro, false, null, $"Contador físico base compartido de la pareja LH/RH grupo {pareja!.GrupoLhRh}.", usuarioId, cn, tx);
            }
            await tx.CommitAsync();
            var objetivo = CalcularObjetivoHoraConfiguracion(vm.TiempoCicloSegundos, vm.CavidadesUsadas);
            if (contextoPareja == null)
            {
                TempData["Success"] = $"Configuración inicial confirmada: {vm.CavidadesUsadas:N0} cavidad(es), {vm.TiempoCicloSegundos:0.####} s de ciclo, objetivo aproximado {objetivo:N0} pzas/h. Contador base: {contador:N0}.";
            }
            else
            {
                var objetivoPareja = CalcularObjetivoHoraConfiguracion(vm.TiempoCicloSegundos, cavidadesPareja!.Value.Cantidad);
                TempData["Success"] = $"Configuración LH/RH confirmada como una sola operación física. OF actual: {vm.CavidadesUsadas:N0} cavidad(es), objetivo {objetivo:N0} pzas/h. {pareja!.OFParejaTexto}: {cavidadesPareja.Value.Cantidad:N0} cavidad(es), objetivo {objetivoPareja:N0} pzas/h. Ciclo compartido: {vm.TiempoCicloSegundos:0.####} s. Contador físico base: {contador:N0}.";
            }
            return RedirectToAction(nameof(Detalle), new { id = vm.EjecucionProduccionID });
        }
        catch (Exception ex)
        {
            try { await tx.RollbackAsync(); } catch { }
            TempData["Error"] = "No fue posible guardar la configuración real de Producción: " + ex.Message;
            return RedirectToAction(nameof(Detalle), new { id = vm.EjecucionProduccionID });
        }
    }

    [HttpGet]
    public async Task<IActionResult> PrevisualizarCambioConfiguracion(int ejecucionProduccionId, int cavidadesUsadas, string? tiempoCicloSegundos, string? cavidadesConfiguradas = null)
    {
        if (!UsuarioEnSesion())
        {
            Response.StatusCode = 401;
            return Json(new { ok = false, mensaje = "La sesión terminó. Vuelve a iniciar sesión." });
        }

        if (ejecucionProduccionId <= 0)
            return BadRequest(new { ok = false, mensaje = "La ejecución de Producción no es válida." });

        var nuevasCavidades = ParsearCavidadesConfiguracion(cavidadesConfiguradas);
        if (!string.IsNullOrWhiteSpace(cavidadesConfiguradas))
        {
            if (nuevasCavidades.Count == 0)
                return BadRequest(new { ok = false, mensaje = "La selección de cavidades no es válida." });

            cavidadesUsadas = nuevasCavidades.Count;
            cavidadesConfiguradas = NormalizarCavidadesConfiguracion(nuevasCavidades);
        }

        if (cavidadesUsadas <= 0)
            return BadRequest(new { ok = false, mensaje = "Las cavidades utilizadas deben ser mayores a cero." });

        var cicloNuevo = ConvertirDecimalFlexibleConfiguracion(tiempoCicloSegundos);
        if (!cicloNuevo.HasValue || cicloNuevo.Value <= 0)
            return BadRequest(new { ok = false, mensaje = "El tiempo de ciclo debe ser mayor a cero." });

        try
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            var contexto = await ObtenerContextoConfiguracionCorridaAsync(ejecucionProduccionId, cn);
            if (contexto == null)
                return NotFound(new { ok = false, mensaje = "No se encontró la ejecución de Producción." });

            var usuarioId = ObtenerUsuarioID();
            var permisos = await ObtenerPermisosProduccionUsuarioAsync(usuarioId, cn);

            if (!PuedeModificarConfiguracionCorrida(permisos, contexto))
            {
                Response.StatusCode = 403;
                return Json(new { ok = false, mensaje = "No tienes permiso para modificar la configuración real de esta ejecución." });
            }

            if (!EjecucionPermiteConfiguracionCorrida(contexto))
                return BadRequest(new { ok = false, mensaje = "La configuración ya no puede modificarse porque la ejecución no está activa." });

            var configuracionActual = await ObtenerConfiguracionActualAsync(ejecucionProduccionId, cn);
            if (configuracionActual == null)
                return BadRequest(new { ok = false, mensaje = "La ejecución todavía no tiene una configuración inicial activa." });

            var cavidadesActuales = ParsearCavidadesConfiguracion(configuracionActual.CavidadesConfiguradas);

            if (nuevasCavidades.Count == 0 && cavidadesActuales.Count > 0)
            {
                if (cavidadesUsadas != configuracionActual.CavidadesUsadas)
                    return BadRequest(new { ok = false, mensaje = "Para cambiar la cantidad de cavidades debes indicar específicamente cuáles cavidades quedarán activas." });

                nuevasCavidades = cavidadesActuales;
                cavidadesConfiguradas = configuracionActual.CavidadesConfiguradas;
            }

            if (nuevasCavidades.Count > 0)
            {
                var disponibles = await ObtenerCavidadesDisponiblesConfiguracionAsync(contexto.ParteID, contexto.CavidadesBD, configuracionActual.CavidadesConfiguradas, cn);
                var errorCavidades = ValidarCavidadesContraDisponibles(nuevasCavidades, disponibles);
                if (!string.IsNullOrWhiteSpace(errorCavidades))
                    return BadRequest(new { ok = false, mensaje = errorCavidades });

                cavidadesUsadas = nuevasCavidades.Count;
                cavidadesConfiguradas = NormalizarCavidadesConfiguracion(nuevasCavidades);
            }

            var comparaDetalleCavidades = cavidadesActuales.Count > 0 || nuevasCavidades.Count > 0;
            var mismasCavidades = comparaDetalleCavidades
                ? string.Equals(NormalizarCavidadesConfiguracion(cavidadesActuales), NormalizarCavidadesConfiguracion(nuevasCavidades), StringComparison.Ordinal)
                : configuracionActual.CavidadesUsadas == cavidadesUsadas;

            var mismoCiclo = Math.Abs(configuracionActual.TiempoCicloSegundos - cicloNuevo.Value) < 0.0001m;
            var hayCambioConfiguracion = !mismasCavidades || !mismoCiclo;

            var planeacion = await ObtenerContextoRecalculoConfiguracionLecturaAsync(contexto.ProgramaProduccionID, ejecucionProduccionId, cn);
            if (planeacion == null)
                return NotFound(new { ok = false, mensaje = "No fue posible recuperar la programación relacionada con esta ejecución." });

            var objetivoAnterior = configuracionActual.ObjetivoHoraOperativo;
            var objetivoNuevo = CalcularObjetivoHoraConfiguracion(cicloNuevo.Value, cavidadesUsadas);
            var cantidadPendiente = Math.Max(0, planeacion.CantidadProgramada - planeacion.CantidadProducida);

            if (cantidadPendiente <= 0)
            {
                return Json(new
                {
                    ok = true,
                    hayCambioConfiguracion,
                    modificaCalendario = false,
                    extiendeProgramacion = false,
                    reduceProgramacion = false,
                    configuracionActual = new
                    {
                        cavidades = configuracionActual.CavidadesUsadas,
                        cavidadesConfiguradas = configuracionActual.CavidadesConfiguradas,
                        cicloSegundos = configuracionActual.TiempoCicloSegundos,
                        objetivoHora = objetivoAnterior
                    },
                    configuracionNueva = new
                    {
                        cavidades = cavidadesUsadas,
                        cavidadesConfiguradas,
                        cicloSegundos = cicloNuevo.Value,
                        objetivoHora = objetivoNuevo
                    },
                    cantidadProgramada = planeacion.CantidadProgramada,
                    cantidadProducida = planeacion.CantidadProducida,
                    cantidadPendiente = 0,
                    horasProgramadasActuales = planeacion.HorasProgramadas,
                    horasRestantesActuales = 0m,
                    horasRestantesNuevas = 0m,
                    deltaHoras = 0m,
                    deltaMinutos = 0,
                    fechaFinActual = planeacion.FechaFinProgramada,
                    fechaFinProyectada = planeacion.FechaFinProgramada,
                    mensaje = "La OF ya no tiene piezas pendientes. El cambio técnico no modificará el calendario."
                });
            }

            if (objetivoAnterior <= 0)
                return BadRequest(new { ok = false, mensaje = "La configuración actual no tiene un objetivo por hora válido." });

            if (objetivoNuevo <= 0)
                return BadRequest(new { ok = false, mensaje = "La nueva configuración no genera un objetivo por hora válido." });

            var horasRestantesActuales = cantidadPendiente / (decimal)objetivoAnterior;
            var horasRestantesNuevas = cantidadPendiente / (decimal)objetivoNuevo;
            var deltaHoras = horasRestantesNuevas - horasRestantesActuales;
            var deltaMinutos = (int)Math.Round(deltaHoras * 60m, 0, MidpointRounding.AwayFromZero);
            var horasBase = planeacion.HorasProgramadas > 0 ? planeacion.HorasProgramadas : horasRestantesActuales;
            var horasProgramadasNuevas = Math.Max(0.0167m, Math.Round(horasBase + deltaHoras, 4, MidpointRounding.AwayFromZero));
            var fechaFinProyectada = Math.Abs(deltaHoras) < 0.0001m
                ? planeacion.FechaFinProgramada
                : _planeacionSecuenciaService.AjustarFechaFinOperativa(planeacion.FechaFinProgramada, deltaHoras, false);

            var extiendeProgramacion = deltaHoras > 0.0001m;
            var reduceProgramacion = deltaHoras < -0.0001m;
            var modificaCalendario = Math.Abs(deltaHoras) >= 0.0001m;

            string mensaje;
            if (!modificaCalendario)
                mensaje = hayCambioConfiguracion
                    ? "La configuración física cambia, pero conserva el mismo rendimiento por hora. No se proyecta un cambio en el calendario."
                    : "La nueva configuración es equivalente a la configuración vigente. No se proyecta un cambio en el calendario.";
            else if (extiendeProgramacion)
                mensaje = $"La nueva configuración agregará aproximadamente {Math.Abs(deltaMinutos):N0} minuto(s) a la OF. Al confirmar el cambio, la programación posterior podrá recorrerse automáticamente.";
            else
                mensaje = $"La nueva configuración reduce aproximadamente {Math.Abs(deltaMinutos):N0} minuto(s) de la OF. Las órdenes posteriores no se adelantarán automáticamente; el espacio quedará disponible para Planeación.";

            return Json(new
            {
                ok = true,
                hayCambioConfiguracion,
                modificaCalendario,
                extiendeProgramacion,
                reduceProgramacion,
                configuracionActual = new
                {
                    cavidades = configuracionActual.CavidadesUsadas,
                    cavidadesConfiguradas = configuracionActual.CavidadesConfiguradas,
                    cicloSegundos = configuracionActual.TiempoCicloSegundos,
                    objetivoHora = objetivoAnterior
                },
                configuracionNueva = new
                {
                    cavidades = cavidadesUsadas,
                    cavidadesConfiguradas,
                    cicloSegundos = cicloNuevo.Value,
                    objetivoHora = objetivoNuevo
                },
                cantidadProgramada = planeacion.CantidadProgramada,
                cantidadProducida = planeacion.CantidadProducida,
                cantidadPendiente,
                horasProgramadasActuales = planeacion.HorasProgramadas,
                horasProgramadasNuevas,
                horasRestantesActuales = Math.Round(horasRestantesActuales, 4, MidpointRounding.AwayFromZero),
                horasRestantesNuevas = Math.Round(horasRestantesNuevas, 4, MidpointRounding.AwayFromZero),
                deltaHoras = Math.Round(deltaHoras, 4, MidpointRounding.AwayFromZero),
                deltaMinutos,
                fechaFinActual = planeacion.FechaFinProgramada,
                fechaFinProyectada,
                mensaje
            });
        }
        catch (Exception ex)
        {
            Response.StatusCode = 500;
            return Json(new { ok = false, mensaje = "No fue posible calcular la vista previa del cambio de configuración: " + ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CambiarConfiguracionCorrida(ProduccionConfiguracionTecnicoPostVm vm)
    {
        if (!UsuarioEnSesion()) return RedirectToAction("Login", "Login");
        if (vm.EjecucionProduccionID <= 0)
        {
            TempData["Error"] = "No se recibió correctamente la ejecución de Producción.";
            return RedirectToAction(nameof(Index));
        }
        var errorValidacion = ValidarDatosConfiguracionCorrida(vm, true);
        if (!string.IsNullOrWhiteSpace(errorValidacion))
        {
            TempData["Error"] = errorValidacion;
            return RedirectToAction(nameof(Detalle), new { id = vm.EjecucionProduccionID });
        }
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var contexto = await ObtenerContextoConfiguracionCorridaAsync(vm.EjecucionProduccionID, cn, tx);
            if (contexto == null) throw new InvalidOperationException("No se encontró la ejecución de Producción.");
            var usuarioId = ObtenerUsuarioID();
            var permisos = await ObtenerPermisosProduccionUsuarioAsync(usuarioId, cn, tx);
            if (!PuedeModificarConfiguracionCorrida(permisos, contexto)) throw new InvalidOperationException("Solo el Técnico de Producción asignado, el Encargado de Producción o un Administrador pueden cambiar las cavidades y el ciclo real.");
            if (!EjecucionPermiteConfiguracionCorrida(contexto)) throw new InvalidOperationException("La configuración de cavidades y ciclo no puede modificarse porque la ejecución ya no está activa.");
            var pareja = await ObtenerParejaLhRhProduccionAsync(contexto.ProgramaProduccionID, cn, tx);
            ProduccionConfiguracionCorridaContexto? contextoPareja = null;
            if (pareja != null)
            {
                if (!pareja.TieneEjecucionPareja) throw new InvalidOperationException($"La OF pertenece a la pareja LH/RH grupo {pareja.GrupoLhRh}, pero no se encontró la ejecución de {pareja.OFParejaTexto}.");
                if (!pareja.EsCompatibleFisicamente) throw new InvalidOperationException("La pareja LH/RH ya no conserva la misma máquina, molde y ventana programada.");
                contextoPareja = await ObtenerContextoConfiguracionCorridaAsync(pareja.EjecucionParejaID!.Value, cn, tx);
                if (contextoPareja == null || contextoPareja.ProgramaProduccionID != pareja.ProgramaParejaID) throw new InvalidOperationException("No fue posible recuperar correctamente la ejecución pareja LH/RH.");
                if (contexto.MaquinaID != contextoPareja.MaquinaID) throw new InvalidOperationException("Las dos ejecuciones LH/RH ya no utilizan la misma máquina física.");
                if (!PuedeModificarConfiguracionCorrida(permisos, contextoPareja)) throw new InvalidOperationException("No tienes permiso para modificar la configuración de la ejecución pareja LH/RH.");
                if (!EjecucionPermiteConfiguracionCorrida(contextoPareja)) throw new InvalidOperationException("La ejecución pareja LH/RH ya no permite modificar su configuración.");
            }
            var configuracionActual = await ObtenerConfiguracionActualAsync(contexto.EjecucionProduccionID, cn, tx);
            if (configuracionActual == null) throw new InvalidOperationException("La ejecución todavía no tiene configuración inicial. Primero confirma las cavidades, ciclo y contador base.");
            ProduccionConfiguracionCorridaVm? configuracionPareja = null;
            if (contextoPareja != null)
            {
                configuracionPareja = await ObtenerConfiguracionActualAsync(contextoPareja.EjecucionProduccionID, cn, tx);
                if (configuracionPareja == null) throw new InvalidOperationException($"{pareja!.OFParejaTexto} no tiene una configuración activa. No se realizará un cambio parcial LH/RH.");
                if (string.IsNullOrWhiteSpace(vm.CavidadesConfiguradasPareja) && (!vm.CavidadesUsadasPareja.HasValue || vm.CavidadesUsadasPareja.Value <= 0))
                {
                    vm.CavidadesUsadasPareja = configuracionPareja.CavidadesUsadas;
                    vm.CavidadesConfiguradasPareja = configuracionPareja.CavidadesConfiguradas;
                }
            }
            var cavidades = await ResolverCavidadesConfiguracionAsync(vm.CavidadesUsadas, vm.CavidadesConfiguradas, contexto.ParteID, contexto.CavidadesBD, configuracionActual, true, "la OF actual", cn, tx);
            vm.CavidadesUsadas = cavidades.Cantidad;
            vm.CavidadesConfiguradas = cavidades.Detalle;
            (int Cantidad, string? Detalle)? cavidadesPareja = null;
            if (contextoPareja != null && configuracionPareja != null)
            {
                cavidadesPareja = await ResolverCavidadesConfiguracionAsync(vm.CavidadesUsadasPareja ?? 0, vm.CavidadesConfiguradasPareja, contextoPareja.ParteID, contextoPareja.CavidadesBD, configuracionPareja, true, $"la pareja {pareja!.OFParejaTexto}", cn, tx);
                vm.CavidadesUsadasPareja = cavidadesPareja.Value.Cantidad;
                vm.CavidadesConfiguradasPareja = cavidadesPareja.Value.Detalle;
            }
            var cambioActual = !SonMismasCavidadesConfiguracion(configuracionActual, vm.CavidadesUsadas, vm.CavidadesConfiguradas) || Math.Abs(configuracionActual.TiempoCicloSegundos - vm.TiempoCicloSegundos) >= 0.0001m;
            var cambioPareja = configuracionPareja != null && cavidadesPareja.HasValue && (!SonMismasCavidadesConfiguracion(configuracionPareja, cavidadesPareja.Value.Cantidad, cavidadesPareja.Value.Detalle) || Math.Abs(configuracionPareja.TiempoCicloSegundos - vm.TiempoCicloSegundos) >= 0.0001m);
            if (!cambioActual && !cambioPareja)
            {
                await tx.RollbackAsync();
                TempData["Info"] = pareja == null ? "Las cavidades activas y el tiempo de ciclo capturados son iguales a la configuración actual." : "Las configuraciones LH/RH capturadas son iguales a las configuraciones vigentes.";
                return RedirectToAction(nameof(Detalle), new { id = vm.EjecucionProduccionID });
            }
            var contadorActual = vm.ContadorMaquinaActual!.Value;
            var ultimaLectura = await ObtenerUltimaLecturaContadorAsync(contexto.EjecucionProduccionID, cn, tx);
            ProduccionUltimaLecturaContador? ultimaLecturaPareja = null;
            if (contextoPareja != null) ultimaLecturaPareja = await ObtenerUltimaLecturaContadorAsync(contextoPareja.EjecucionProduccionID, cn, tx);
            var huboReinicioContador = (ultimaLectura != null && contadorActual < ultimaLectura.ValorContador) || (ultimaLecturaPareja != null && contadorActual < ultimaLecturaPareja.ValorContador);
            var fechaCambio = DateTime.Now;
            long? contadorFinAnterior = huboReinicioContador ? null : contadorActual;
            await CerrarConfiguracionActualAsync(configuracionActual.ConfiguracionCorridaID, contadorFinAnterior, fechaCambio, usuarioId, cn, tx);
            if (configuracionPareja != null) await CerrarConfiguracionActualAsync(configuracionPareja.ConfiguracionCorridaID, contadorFinAnterior, fechaCambio, usuarioId, cn, tx);
            var tecnicoRegistroId = ResolverTecnicoConfiguracion(permisos, contexto);
            var nuevaConfiguracionId = await InsertarConfiguracionCorridaAsync(contexto.EjecucionProduccionID, vm.CavidadesUsadas, vm.CavidadesConfiguradas, vm.TiempoCicloSegundos, contadorActual, fechaCambio, false, vm.MotivoCambio, tecnicoRegistroId, usuarioId, cn, tx);
            var tipoLectura = huboReinicioContador ? ProduccionTipoLecturaContador.ReinicioContador : ProduccionTipoLecturaContador.CambioConfiguracion;
            var motivoReinicio = huboReinicioContador ? "Se detectó que el contador físico actual es menor a una de las últimas lecturas LH/RH. " + (vm.MotivoCambio ?? string.Empty) : null;
            await RegistrarLecturaContadorConfiguracionAsync(contexto.EjecucionProduccionID, nuevaConfiguracionId, contexto.MaquinaID, tipoLectura, contadorActual, fechaCambio, huboReinicioContador, motivoReinicio, vm.MotivoCambio, usuarioId, cn, tx);
            int? nuevaConfiguracionParejaId = null;
            if (contextoPareja != null && configuracionPareja != null && cavidadesPareja.HasValue)
            {
                var tecnicoParejaId = ResolverTecnicoConfiguracion(permisos, contextoPareja);
                nuevaConfiguracionParejaId = await InsertarConfiguracionCorridaAsync(contextoPareja.EjecucionProduccionID, cavidadesPareja.Value.Cantidad, cavidadesPareja.Value.Detalle, vm.TiempoCicloSegundos, contadorActual, fechaCambio, false, vm.MotivoCambio, tecnicoParejaId, usuarioId, cn, tx);
                await RegistrarLecturaContadorConfiguracionAsync(contextoPareja.EjecucionProduccionID, nuevaConfiguracionParejaId.Value, contextoPareja.MaquinaID, tipoLectura, contadorActual, fechaCambio, huboReinicioContador, motivoReinicio, vm.MotivoCambio, usuarioId, cn, tx);
            }
            var planeacion = await ObtenerContextoRecalculoConfiguracionAsync(contexto.ProgramaProduccionID, contexto.EjecucionProduccionID, cn, tx);
            if (planeacion == null) throw new InvalidOperationException("No fue posible recuperar la programación relacionada con esta ejecución.");
            var objetivoAnterior = configuracionActual.ObjetivoHoraOperativo;
            var objetivoNuevo = CalcularObjetivoHoraConfiguracion(vm.TiempoCicloSegundos, vm.CavidadesUsadas);
            var pendiente = Math.Max(0, planeacion.CantidadProgramada - planeacion.CantidadProducida);
            var programasRecorridos = 0;
            var horasRestantesAnteriores = CalcularHorasRestantesConfiguracion(pendiente, objetivoAnterior, "la OF actual");
            var horasRestantesNuevas = CalcularHorasRestantesConfiguracion(pendiente, objetivoNuevo, "la OF actual");
            decimal horasFisicasAnteriores = horasRestantesAnteriores;
            decimal horasFisicasNuevas = horasRestantesNuevas;
            ProduccionRecalculoConfiguracionContexto? planeacionPareja = null;
            int objetivoAnteriorPareja = 0;
            int objetivoNuevoPareja = 0;
            int pendientePareja = 0;
            decimal horasRestantesAnterioresPareja = 0m;
            decimal horasRestantesNuevasPareja = 0m;
            if (contextoPareja != null && configuracionPareja != null && cavidadesPareja.HasValue)
            {
                planeacionPareja = await ObtenerContextoRecalculoConfiguracionAsync(contextoPareja.ProgramaProduccionID, contextoPareja.EjecucionProduccionID, cn, tx);
                if (planeacionPareja == null) throw new InvalidOperationException("No fue posible recuperar la programación de la ejecución pareja LH/RH.");
                if (Math.Abs(planeacion.HorasProgramadas - planeacionPareja.HorasProgramadas) > 0.0001m) throw new InvalidOperationException("Las dos OF LH/RH tienen horas programadas diferentes. Corrige Planeación antes de cambiar la configuración física.");
                objetivoAnteriorPareja = configuracionPareja.ObjetivoHoraOperativo;
                objetivoNuevoPareja = CalcularObjetivoHoraConfiguracion(vm.TiempoCicloSegundos, cavidadesPareja.Value.Cantidad);
                pendientePareja = Math.Max(0, planeacionPareja.CantidadProgramada - planeacionPareja.CantidadProducida);
                horasRestantesAnterioresPareja = CalcularHorasRestantesConfiguracion(pendientePareja, objetivoAnteriorPareja, pareja!.OFParejaTexto);
                horasRestantesNuevasPareja = CalcularHorasRestantesConfiguracion(pendientePareja, objetivoNuevoPareja, pareja.OFParejaTexto);
                horasFisicasAnteriores = Math.Max(horasRestantesAnteriores, horasRestantesAnterioresPareja);
                horasFisicasNuevas = Math.Max(horasRestantesNuevas, horasRestantesNuevasPareja);
            }
            var deltaHoras = horasFisicasNuevas - horasFisicasAnteriores;
            DateTime? nuevoFinProgramado = null;
            if ((pendiente > 0 || pendientePareja > 0) && Math.Abs(deltaHoras) >= 0.0001m)
            {
                if (contextoPareja == null)
                {
                    var horasBase = planeacion.HorasProgramadas > 0 ? planeacion.HorasProgramadas : horasFisicasAnteriores;
                    var horasProgramadasNuevas = Math.Max(0.0167m, Math.Round(horasBase + deltaHoras, 4, MidpointRounding.AwayFromZero));
                    nuevoFinProgramado = _planeacionSecuenciaService.AjustarFechaFinOperativa(planeacion.FechaFinProgramada, deltaHoras, false);
                    var motivoRecalculo = $"Recalculo automático por cambio de configuración real. Cavidades {TextoCavidadesConfiguracion(configuracionActual)} → {TextoCavidadesConfiguracion(vm.CavidadesUsadas, vm.CavidadesConfiguradas)}. Ciclo {configuracionActual.TiempoCicloSegundos:0.####} → {vm.TiempoCicloSegundos:0.####} s. Objetivo {objetivoAnterior} → {objetivoNuevo} pzas/h. Motivo técnico: {vm.MotivoCambio}";
                    programasRecorridos = await _planeacionSecuenciaService.ReacomodarPorCambioDuracionAsync(contexto.ProgramaProduccionID, contexto.EjecucionProduccionID, nuevoFinProgramado.Value, horasProgramadasNuevas, usuarioId, motivoRecalculo, cn, tx, false);
                }
                else
                {
                    var actualEsRaiz = contexto.ProgramaProduccionID <= contextoPareja.ProgramaProduccionID;
                    var contextoRaiz = actualEsRaiz ? contexto : contextoPareja;
                    var contextoSecundario = actualEsRaiz ? contextoPareja : contexto;
                    var planeacionRaiz = actualEsRaiz ? planeacion : planeacionPareja!;
                    var horasBase = planeacionRaiz.HorasProgramadas > 0 ? planeacionRaiz.HorasProgramadas : horasFisicasAnteriores;
                    var horasProgramadasNuevas = Math.Max(0.0167m, Math.Round(horasBase + deltaHoras, 4, MidpointRounding.AwayFromZero));
                    nuevoFinProgramado = _planeacionSecuenciaService.AjustarFechaFinOperativa(planeacionRaiz.FechaFinProgramada, deltaHoras, false);
                    var motivoRecalculo = $"Recalculo automático de pareja LH/RH grupo {pareja!.GrupoLhRh} por cambio de configuración física. Tiempo restante físico {horasFisicasAnteriores:0.####} → {horasFisicasNuevas:0.####} h. OF actual objetivo {objetivoAnterior} → {objetivoNuevo} pzas/h. Pareja objetivo {objetivoAnteriorPareja} → {objetivoNuevoPareja} pzas/h. Ciclo compartido {vm.TiempoCicloSegundos:0.####} s. Motivo técnico: {vm.MotivoCambio}";
                    programasRecorridos = await _planeacionSecuenciaService.ReacomodarPorCambioDuracionAsync(contextoRaiz.ProgramaProduccionID, contextoRaiz.EjecucionProduccionID, nuevoFinProgramado.Value, horasProgramadasNuevas, usuarioId, motivoRecalculo, cn, tx, false);
                    await SincronizarDuracionParejaConfiguracionAsync(contextoRaiz.ProgramaProduccionID, contextoSecundario.ProgramaProduccionID, usuarioId, cn, tx);
                }
            }
            await tx.CommitAsync();
            string mensaje;
            if (contextoPareja == null)
            {
                mensaje = $"Configuración actualizada. Cavidades activas: {TextoCavidadesConfiguracion(configuracionActual)} → {TextoCavidadesConfiguracion(vm.CavidadesUsadas, vm.CavidadesConfiguradas)}. Ciclo: {configuracionActual.TiempoCicloSegundos:0.####} → {vm.TiempoCicloSegundos:0.####} s. Objetivo: {objetivoAnterior:N0} → {objetivoNuevo:N0} pzas/h.";
            }
            else
            {
                mensaje = $"Configuración LH/RH actualizada conjuntamente. OF actual: {TextoCavidadesConfiguracion(configuracionActual)} → {TextoCavidadesConfiguracion(vm.CavidadesUsadas, vm.CavidadesConfiguradas)}, objetivo {objetivoAnterior:N0} → {objetivoNuevo:N0} pzas/h. {pareja!.OFParejaTexto}: {TextoCavidadesConfiguracion(configuracionPareja!)} → {TextoCavidadesConfiguracion(cavidadesPareja!.Value.Cantidad, cavidadesPareja.Value.Detalle)}, objetivo {objetivoAnteriorPareja:N0} → {objetivoNuevoPareja:N0} pzas/h. Ciclo físico compartido: {vm.TiempoCicloSegundos:0.####} s.";
            }
            if (pendiente > 0 || pendientePareja > 0)
            {
                mensaje += $" Tiempo restante físico estimado: {horasFisicasAnteriores:0.##} → {horasFisicasNuevas:0.##} h.";
                if (nuevoFinProgramado.HasValue) mensaje += $" Nuevo fin programado: {nuevoFinProgramado.Value:dd/MM/yyyy HH:mm}. Programas posteriores recorridos: {programasRecorridos}.";
                else mensaje += " El cambio no modifica la duración física restante de la máquina.";
            }
            else mensaje += " La producción ya no tiene cantidad pendiente, por lo que no fue necesario modificar el calendario.";
            mensaje += huboReinicioContador ? $" Se detectó reinicio del contador físico; la nueva base quedó en {contadorActual:N0}." : $" Contador físico al cambio: {contadorActual:N0}.";
            TempData["Success"] = mensaje;
            return RedirectToAction(nameof(Detalle), new { id = vm.EjecucionProduccionID });
        }
        catch (Exception ex)
        {
            try { await tx.RollbackAsync(); } catch { }
            TempData["Error"] = "No fue posible cambiar la configuración real de Producción: " + ex.Message;
            return RedirectToAction(nameof(Detalle), new { id = vm.EjecucionProduccionID });
        }
    }
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

    private async Task<ProduccionConfiguracionTecnicoVm> ConstruirConfiguracionTecnicoAsync(ProduccionConfiguracionCorridaContexto contexto, SqlConnection cn, SqlTransaction? tx = null)
    {
        var vm = new ProduccionConfiguracionTecnicoVm
        {
            EjecucionProduccionID = contexto.EjecucionProduccionID,
            ProgramaProduccionID = contexto.ProgramaProduccionID,
            MaquinaID = contexto.MaquinaID,
            MaquinaCodigo = contexto.MaquinaCodigo,
            MaquinaNombre = contexto.MaquinaNombre,
            ParteID = contexto.ParteID,
            NumeroParte = contexto.NumeroParte,
            ReferenciaSAP = contexto.ReferenciaSAP,
            CavidadesBD = contexto.CavidadesBD,
            TiempoCicloBD = contexto.TiempoCicloBD
        };
        vm.ConfiguracionActual = await ObtenerConfiguracionActualAsync(contexto.EjecucionProduccionID, cn, tx);
        vm.CavidadesDisponibles = await ObtenerCavidadesDisponiblesConfiguracionAsync(contexto.ParteID, contexto.CavidadesBD, vm.ConfiguracionActual?.CavidadesConfiguradas, cn, tx);
        vm.HistorialConfiguraciones = await ObtenerHistorialConfiguracionesAsync(contexto.EjecucionProduccionID, cn, tx);
        var ultimaLectura = await ObtenerUltimaLecturaContadorAsync(contexto.EjecucionProduccionID, cn, tx);
        vm.UltimoContadorMaquina = ultimaLectura?.ValorContador;
        var pareja = await ObtenerParejaLhRhProduccionAsync(contexto.ProgramaProduccionID, cn, tx);
        vm.ParejaLhRh = pareja;
        if (pareja == null) return vm;
        var ladoActual = DeterminarLadoLhRhConfiguracion(contexto.ReferenciaSAP, contexto.NumeroParte);
        var ladoPareja = DeterminarLadoLhRhConfiguracion(pareja.ReferenciaSAPPareja, pareja.NumeroPartePareja, pareja.DescripcionPartePareja);
        if (string.IsNullOrWhiteSpace(ladoActual) && string.Equals(ladoPareja, "LH", StringComparison.OrdinalIgnoreCase)) ladoActual = "RH";
        else if (string.IsNullOrWhiteSpace(ladoActual) && string.Equals(ladoPareja, "RH", StringComparison.OrdinalIgnoreCase)) ladoActual = "LH";
        if (string.IsNullOrWhiteSpace(ladoPareja) && string.Equals(ladoActual, "LH", StringComparison.OrdinalIgnoreCase)) ladoPareja = "RH";
        else if (string.IsNullOrWhiteSpace(ladoPareja) && string.Equals(ladoActual, "RH", StringComparison.OrdinalIgnoreCase)) ladoPareja = "LH";
        vm.LadoLhRh = ladoActual;
        vm.LadoParejaLhRh = ladoPareja;
        if (!pareja.EjecucionParejaID.HasValue || pareja.EjecucionParejaID.Value <= 0) return vm;
        var contextoPareja = await ObtenerContextoConfiguracionCorridaAsync(pareja.EjecucionParejaID.Value, cn, tx);
        if (contextoPareja == null || contextoPareja.ProgramaProduccionID != pareja.ProgramaParejaID) return vm;
        vm.CavidadesBDPareja = contextoPareja.CavidadesBD;
        vm.TiempoCicloBDPareja = contextoPareja.TiempoCicloBD;
        vm.ConfiguracionActualPareja = await ObtenerConfiguracionActualAsync(contextoPareja.EjecucionProduccionID, cn, tx);
        vm.CavidadesDisponiblesPareja = await ObtenerCavidadesDisponiblesConfiguracionAsync(contextoPareja.ParteID, contextoPareja.CavidadesBD, vm.ConfiguracionActualPareja?.CavidadesConfiguradas, cn, tx);
        vm.HistorialConfiguracionesPareja = await ObtenerHistorialConfiguracionesAsync(contextoPareja.EjecucionProduccionID, cn, tx);
        var ultimaLecturaPareja = await ObtenerUltimaLecturaContadorAsync(contextoPareja.EjecucionProduccionID, cn, tx);
        vm.UltimoContadorMaquinaPareja = ultimaLecturaPareja?.ValorContador;
        return vm;
    }


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


    private static async Task<ProduccionConfiguracionCorridaVm?> ObtenerConfiguracionActualAsync(int ejecucionProduccionId, SqlConnection cn, SqlTransaction? tx = null)
    {
        var sql = tx == null
            ? @"
SELECT TOP(1)
    c.ConfiguracionCorridaID,
    c.EjecucionProduccionID,
    c.CavidadesUsadas,
    c.CavidadesConfiguradas,
    c.TiempoCicloSegundos,
    c.ObjetivoHoraCalculado,
    c.ContadorInicioVigencia,
    c.ContadorFinVigencia,
    c.FechaInicioVigencia,
    c.FechaFinVigencia,
    c.EsConfiguracionInicial,
    c.MotivoCambio,
    c.TecnicoProduccionID,
    NULLIF(LTRIM(RTRIM(CONCAT(ISNULL(p.Nombre,N''),N' ',ISNULL(p.ApellidoPaterno,N''),N' ',ISNULL(p.ApellidoMaterno,N'')))),N'') AS TecnicoProduccionNombre,
    c.UsuarioCreacionID,
    c.FechaCreacion,
    c.UsuarioModificacionID,
    c.FechaModificacion,
    c.Activo
FROM dbo.Produccion_ConfiguracionCorrida c
LEFT JOIN dbo.Persona p ON p.PersonaID=c.TecnicoProduccionID
WHERE c.EjecucionProduccionID=@EjecucionProduccionID
  AND c.Activo=1
  AND c.FechaFinVigencia IS NULL
ORDER BY c.ConfiguracionCorridaID DESC;"
            : @"
SELECT TOP(1)
    c.ConfiguracionCorridaID,
    c.EjecucionProduccionID,
    c.CavidadesUsadas,
    c.CavidadesConfiguradas,
    c.TiempoCicloSegundos,
    c.ObjetivoHoraCalculado,
    c.ContadorInicioVigencia,
    c.ContadorFinVigencia,
    c.FechaInicioVigencia,
    c.FechaFinVigencia,
    c.EsConfiguracionInicial,
    c.MotivoCambio,
    c.TecnicoProduccionID,
    NULLIF(LTRIM(RTRIM(CONCAT(ISNULL(p.Nombre,N''),N' ',ISNULL(p.ApellidoPaterno,N''),N' ',ISNULL(p.ApellidoMaterno,N'')))),N'') AS TecnicoProduccionNombre,
    c.UsuarioCreacionID,
    c.FechaCreacion,
    c.UsuarioModificacionID,
    c.FechaModificacion,
    c.Activo
FROM dbo.Produccion_ConfiguracionCorrida c WITH(UPDLOCK,HOLDLOCK)
LEFT JOIN dbo.Persona p ON p.PersonaID=c.TecnicoProduccionID
WHERE c.EjecucionProduccionID=@EjecucionProduccionID
  AND c.Activo=1
  AND c.FechaFinVigencia IS NULL
ORDER BY c.ConfiguracionCorridaID DESC;";

        await using var cmd = tx == null ? new SqlCommand(sql, cn) : new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;

        await using var rd = await cmd.ExecuteReaderAsync();
        return await rd.ReadAsync() ? MapearConfiguracionCorrida(rd) : null;
    }
    private static async Task<List<ProduccionConfiguracionCorridaVm>>       ObtenerHistorialConfiguracionesAsync(           int ejecucionProduccionId,            SqlConnection cn,            SqlTransaction? tx = null)
    {
        const string sql = @"
SELECT
    c.ConfiguracionCorridaID,
    c.EjecucionProduccionID,
    c.CavidadesUsadas,
c.CavidadesConfiguradas,
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

    private static ProduccionConfiguracionCorridaVm       MapearConfiguracionCorrida(           SqlDataReader rd)
    {
        return new ProduccionConfiguracionCorridaVm
        {
            ConfiguracionCorridaID =               Convert.ToInt32(                   rd["ConfiguracionCorridaID"]),

            EjecucionProduccionID =               Convert.ToInt32(                    rd["EjecucionProduccionID"]),

            CavidadesUsadas =                Convert.ToInt32(                    rd["CavidadesUsadas"]),
            CavidadesConfiguradas =
    rd["CavidadesConfiguradas"] == DBNull.Value
        ? null
        : rd["CavidadesConfiguradas"]?
            .ToString()?
            .Trim(),

            TiempoCicloSegundos =               Convert.ToDecimal(                   rd["TiempoCicloSegundos"]),

            ObjetivoHoraCalculado =               Convert.ToDecimal(                   rd["ObjetivoHoraCalculado"]),

            ContadorInicioVigencia =               rd["ContadorInicioVigencia"] == DBNull.Value                    ? null                   : Convert.ToInt64(                        rd["ContadorInicioVigencia"]),

            ContadorFinVigencia =                rd["ContadorFinVigencia"] == DBNull.Value                    ? null                    : Convert.ToInt64(                        rd["ContadorFinVigencia"]),

            FechaInicioVigencia =                Convert.ToDateTime(                    rd["FechaInicioVigencia"]),

            FechaFinVigencia =               rd["FechaFinVigencia"] == DBNull.Value                    ? null                    : Convert.ToDateTime(                        rd["FechaFinVigencia"]),

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
    private static async Task<int> InsertarConfiguracionCorridaAsync(int ejecucionProduccionId, int cavidadesUsadas, string? cavidadesConfiguradas, decimal tiempoCicloSegundos, long contadorInicio, DateTime fechaInicio, bool esConfiguracionInicial, string? motivoCambio, int? tecnicoProduccionId, int usuarioId, SqlConnection cn, SqlTransaction tx)
    {
        const string sql = @"
INSERT INTO dbo.Produccion_ConfiguracionCorrida
(
    EjecucionProduccionID,
    CavidadesUsadas,
    CavidadesConfiguradas,
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
    @CavidadesConfiguradas,
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

        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
        cmd.Parameters.Add("@CavidadesUsadas", SqlDbType.Int).Value = cavidadesUsadas;
        cmd.Parameters.Add("@CavidadesConfiguradas", SqlDbType.NVarChar, 500).Value = string.IsNullOrWhiteSpace(cavidadesConfiguradas) ? DBNull.Value : cavidadesConfiguradas.Trim();

        var pCiclo = cmd.Parameters.Add("@TiempoCicloSegundos", SqlDbType.Decimal);
        pCiclo.Precision = 18;
        pCiclo.Scale = 4;
        pCiclo.Value = tiempoCicloSegundos;

        cmd.Parameters.Add("@ContadorInicioVigencia", SqlDbType.BigInt).Value = contadorInicio;
        cmd.Parameters.Add("@FechaInicioVigencia", SqlDbType.DateTime2).Value = fechaInicio;
        cmd.Parameters.Add("@EsConfiguracionInicial", SqlDbType.Bit).Value = esConfiguracionInicial;
        cmd.Parameters.Add("@MotivoCambio", SqlDbType.NVarChar, 500).Value = string.IsNullOrWhiteSpace(motivoCambio) ? DBNull.Value : motivoCambio.Trim();
        cmd.Parameters.Add("@TecnicoProduccionID", SqlDbType.Int).Value = (object?)tecnicoProduccionId ?? DBNull.Value;
        cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;

        var resultado = await cmd.ExecuteScalarAsync();
        if (resultado == null || resultado == DBNull.Value)
            throw new InvalidOperationException("No fue posible recuperar el identificador de la configuración creada.");

        return Convert.ToInt32(resultado);
    }

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

    private static async Task<ProduccionRecalculoConfiguracionContexto?> ObtenerContextoRecalculoConfiguracionAsync(int programaProduccionId, int ejecucionProduccionId, SqlConnection cn, SqlTransaction tx)
    {
        const string sql = @"
SELECT TOP(1)
    ISNULL(pp.CantidadProgramada,0) AS CantidadProgramada,
    ISNULL(e.CantidadOKTotal,ISNULL(pp.CantidadProducida,0)) AS CantidadProducida,
    ISNULL(pp.HorasProgramadas,0) AS HorasProgramadas,
    ISNULL
    (
        pp.FechaFinProgramada,
        DATEADD
        (
            MINUTE,
            CAST(CEILING(ISNULL(pp.HorasProgramadas,0)*60) AS INT),
            pp.FechaInicioProgramada
        )
    ) AS FechaFinProgramada
FROM dbo.Planeacion_ProgramaProduccion pp WITH(UPDLOCK,HOLDLOCK)
LEFT JOIN dbo.Produccion_Ejecucion e WITH(UPDLOCK,HOLDLOCK)
    ON e.EjecucionProduccionID=@EjecucionProduccionID
   AND e.ProgramaProduccionID=pp.ProgramaProduccionID
   AND e.Activo=1
WHERE pp.ProgramaProduccionID=@ProgramaProduccionID
  AND pp.Activo=1;";

        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaProduccionId;
        cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;

        await using var rd = await cmd.ExecuteReaderAsync();
        if (!await rd.ReadAsync())
            return null;

        return new ProduccionRecalculoConfiguracionContexto
        {
            CantidadProgramada = Convert.ToInt32(rd["CantidadProgramada"]),
            CantidadProducida = Convert.ToInt32(rd["CantidadProducida"]),
            HorasProgramadas = Convert.ToDecimal(rd["HorasProgramadas"]),
            FechaFinProgramada = Convert.ToDateTime(rd["FechaFinProgramada"])
        };
    }
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
    private static async Task<ProduccionRecalculoConfiguracionContexto?> ObtenerContextoRecalculoConfiguracionLecturaAsync(int programaProduccionId, int ejecucionProduccionId, SqlConnection cn)
    {
        if (programaProduccionId <= 0 || ejecucionProduccionId <= 0)
            return null;

        const string sql = @"
SELECT TOP(1)
    ISNULL(pp.CantidadProgramada,0) AS CantidadProgramada,
    ISNULL(e.CantidadOKTotal,ISNULL(pp.CantidadProducida,0)) AS CantidadProducida,
    ISNULL(pp.HorasProgramadas,0) AS HorasProgramadas,
    ISNULL
    (
        pp.FechaFinProgramada,
        DATEADD
        (
            MINUTE,
            CAST
            (
                CEILING(
                    ISNULL(pp.HorasProgramadas,0) * 60
                )
                AS INT
            ),
            pp.FechaInicioProgramada
        )
    ) AS FechaFinProgramada
FROM dbo.Planeacion_ProgramaProduccion pp
LEFT JOIN dbo.Produccion_Ejecucion e
    ON e.EjecucionProduccionID=@EjecucionProduccionID
   AND e.ProgramaProduccionID=pp.ProgramaProduccionID
   AND e.Activo=1
WHERE pp.ProgramaProduccionID=@ProgramaProduccionID
  AND pp.Activo=1;";

        await using var cmd =
            new SqlCommand(sql, cn);

        cmd.Parameters.Add(
            "@ProgramaProduccionID",
            SqlDbType.Int).Value =
            programaProduccionId;

        cmd.Parameters.Add(
            "@EjecucionProduccionID",
            SqlDbType.Int).Value =
            ejecucionProduccionId;

        await using var rd =
            await cmd.ExecuteReaderAsync();

        if (!await rd.ReadAsync())
            return null;

        return new ProduccionRecalculoConfiguracionContexto
        {
            CantidadProgramada =
                Convert.ToInt32(
                    rd["CantidadProgramada"]),

            CantidadProducida =
                Convert.ToInt32(
                    rd["CantidadProducida"]),

            HorasProgramadas =
                Convert.ToDecimal(
                    rd["HorasProgramadas"]),

            FechaFinProgramada =
                Convert.ToDateTime(
                    rd["FechaFinProgramada"])
        };
    }
    private static string? ValidarDatosConfiguracionCorrida(ProduccionConfiguracionTecnicoPostVm vm, bool requiereMotivo)
    {
        if (!string.IsNullOrWhiteSpace(vm.CavidadesConfiguradas))
        {
            var cavidades = ParsearCavidadesConfiguracion(vm.CavidadesConfiguradas);
            if (cavidades.Count == 0) return "La selección de cavidades activas no es válida.";
            vm.CavidadesUsadas = cavidades.Count;
            vm.CavidadesConfiguradas = NormalizarCavidadesConfiguracion(cavidades);
        }
        else if (vm.CavidadesUsadas <= 0) return "El técnico debe indicar cuántas cavidades se están utilizando realmente.";
        if (!string.IsNullOrWhiteSpace(vm.CavidadesConfiguradasPareja))
        {
            var cavidadesPareja = ParsearCavidadesConfiguracion(vm.CavidadesConfiguradasPareja);
            if (cavidadesPareja.Count == 0) return "La selección de cavidades activas de la pareja LH/RH no es válida.";
            vm.CavidadesUsadasPareja = cavidadesPareja.Count;
            vm.CavidadesConfiguradasPareja = NormalizarCavidadesConfiguracion(cavidadesPareja);
        }
        if (vm.TiempoCicloSegundos <= 0) return "El técnico debe indicar el tiempo de ciclo real en segundos.";
        if (!vm.ContadorMaquinaActual.HasValue) return "El técnico debe indicar el contador actual de la máquina para establecer el punto de inicio de la configuración.";
        if (vm.ContadorMaquinaActual.Value < 0) return "El contador de la máquina no puede ser negativo.";
        if (!string.IsNullOrWhiteSpace(vm.MotivoCambio) && vm.MotivoCambio.Trim().Length > 500) return "El motivo u observaciones no pueden superar 500 caracteres.";
        if (requiereMotivo && string.IsNullOrWhiteSpace(vm.MotivoCambio)) return "Debes indicar el motivo por el que cambia la configuración técnica.";
        return null;
    }

    private static async Task<(int Cantidad, string? Detalle)> ResolverCavidadesConfiguracionAsync(int cavidadesUsadas, string? cavidadesConfiguradas, int? parteId, int? cavidadesBD, ProduccionConfiguracionCorridaVm? configuracionActual, bool esCambio, string etiqueta, SqlConnection cn, SqlTransaction tx)
    {
        var seleccionadas = ParsearCavidadesConfiguracion(cavidadesConfiguradas);
        if (!string.IsNullOrWhiteSpace(cavidadesConfiguradas) && seleccionadas.Count == 0) throw new InvalidOperationException($"La selección de cavidades activas de {etiqueta} no es válida.");
        var actuales = ParsearCavidadesConfiguracion(configuracionActual?.CavidadesConfiguradas);
        if (esCambio && seleccionadas.Count == 0 && actuales.Count > 0)
        {
            if (configuracionActual == null) throw new InvalidOperationException($"No se encontró la configuración vigente de {etiqueta}.");
            if (cavidadesUsadas != configuracionActual.CavidadesUsadas) throw new InvalidOperationException($"Para cambiar la cantidad de cavidades de {etiqueta} debes indicar específicamente cuáles cavidades quedarán activas.");
            seleccionadas = actuales;
            cavidadesConfiguradas = configuracionActual.CavidadesConfiguradas;
        }
        if (seleccionadas.Count > 0)
        {
            var disponibles = await ObtenerCavidadesDisponiblesConfiguracionAsync(parteId, cavidadesBD, configuracionActual?.CavidadesConfiguradas, cn, tx);
            var error = ValidarCavidadesContraDisponibles(seleccionadas, disponibles);
            if (!string.IsNullOrWhiteSpace(error)) throw new InvalidOperationException($"{etiqueta}: {error}");
            return (seleccionadas.Count, NormalizarCavidadesConfiguracion(seleccionadas));
        }
        if (cavidadesUsadas <= 0) throw new InvalidOperationException($"Debes indicar las cavidades utilizadas realmente para {etiqueta}.");
        return (cavidadesUsadas, null);
    }
    private static bool SonMismasCavidadesConfiguracion(ProduccionConfiguracionCorridaVm actual, int cantidadNueva, string? detalleNuevo)
    {
        var anteriores = ParsearCavidadesConfiguracion(actual.CavidadesConfiguradas);
        var nuevas = ParsearCavidadesConfiguracion(detalleNuevo);
        if (anteriores.Count > 0 || nuevas.Count > 0) return string.Equals(NormalizarCavidadesConfiguracion(anteriores), NormalizarCavidadesConfiguracion(nuevas), StringComparison.Ordinal);
        return actual.CavidadesUsadas == cantidadNueva;
    }
    private static decimal CalcularHorasRestantesConfiguracion(int cantidadPendiente, int objetivoHora, string etiqueta)
    {
        if (cantidadPendiente <= 0) return 0m;
        if (objetivoHora <= 0) throw new InvalidOperationException($"La configuración vigente de {etiqueta} no genera un objetivo por hora válido.");
        return cantidadPendiente / (decimal)objetivoHora;
    }
    private static string TextoCavidadesConfiguracion(ProduccionConfiguracionCorridaVm configuracion)
    {
        return string.IsNullOrWhiteSpace(configuracion.CavidadesConfiguradas) ? configuracion.CavidadesUsadas.ToString(CultureInfo.InvariantCulture) : configuracion.CavidadesConfiguradas;
    }
    private static string TextoCavidadesConfiguracion(int cantidad, string? detalle)
    {
        return string.IsNullOrWhiteSpace(detalle) ? cantidad.ToString(CultureInfo.InvariantCulture) : detalle;
    }
    private static string? DeterminarLadoLhRhConfiguracion(params string?[] valores)
    {
        var tieneLh = false;
        var tieneRh = false;
        foreach (var valor in valores)
        {
            if (string.IsNullOrWhiteSpace(valor)) continue;
            var texto = valor.Trim().ToUpperInvariant();
            if (texto.Contains("LH/RH", StringComparison.Ordinal) || texto.Contains("RH/LH", StringComparison.Ordinal)) continue;
            if (Regex.IsMatch(texto, @"(?<![A-Z0-9])LH(?![A-Z0-9])", RegexOptions.CultureInvariant)) tieneLh = true;
            if (Regex.IsMatch(texto, @"(?<![A-Z0-9])RH(?![A-Z0-9])", RegexOptions.CultureInvariant)) tieneRh = true;
        }
        if (tieneLh == tieneRh) return null;
        return tieneLh ? "LH" : "RH";
    }
    private static async Task SincronizarDuracionParejaConfiguracionAsync(int programaFuenteId, int programaParejaId, int usuarioId, SqlConnection cn, SqlTransaction tx)
    {
        const string sql = @"
UPDATE pareja
SET pareja.FechaFinProgramada=fuente.FechaFinProgramada,
    pareja.HorasProgramadas=fuente.HorasProgramadas,
    pareja.UsuarioModificacionID=@UsuarioID,
    pareja.FechaModificacion=GETDATE()
FROM dbo.Planeacion_ProgramaProduccion pareja
INNER JOIN dbo.Planeacion_ProgramaProduccion fuente
    ON fuente.ProgramaProduccionID=@ProgramaFuenteID
   AND fuente.Activo=1
WHERE pareja.ProgramaProduccionID=@ProgramaParejaID
  AND pareja.Activo=1;
IF @@ROWCOUNT<>1 THROW 51671,'No fue posible sincronizar la duración programada de la pareja LH/RH.',1;";
        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@ProgramaFuenteID", SqlDbType.Int).Value = programaFuenteId;
        cmd.Parameters.Add("@ProgramaParejaID", SqlDbType.Int).Value = programaParejaId;
        cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
        await cmd.ExecuteNonQueryAsync();
    }
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

    private const int MaximoCavidadesConfigurablesProduccion = 64;

    private static List<int> ParsearCavidadesConfiguracion(
        string? cavidadesConfiguradas)
    {
        var resultado = new SortedSet<int>();

        if (string.IsNullOrWhiteSpace(cavidadesConfiguradas))
            return resultado.ToList();

        var tokens = Regex.Split(
            cavidadesConfiguradas.Trim(),
            @"[\s,;|]+");

        foreach (var token in tokens)
        {
            if (string.IsNullOrWhiteSpace(token))
                continue;

            if (!int.TryParse(
                    token,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var cavidad))
            {
                return new List<int>();
            }

            if (cavidad <= 0 ||
                cavidad > MaximoCavidadesConfigurablesProduccion)
            {
                return new List<int>();
            }

            resultado.Add(cavidad);
        }

        return resultado.ToList();
    }

    private static string NormalizarCavidadesConfiguracion(
        IEnumerable<int> cavidades)
    {
        return string.Join(
            ",",
            cavidades
                .Where(x => x > 0)
                .Distinct()
                .OrderBy(x => x));
    }

    private static string? ValidarCavidadesContraDisponibles(
        IReadOnlyCollection<int> seleccionadas,
        IReadOnlyCollection<int> disponibles)
    {
        if (seleccionadas.Count == 0)
        {
            return
                "Selecciona al menos una cavidad activa para la corrida.";
        }

        if (disponibles.Count == 0)
        {
            return
                "No fue posible determinar las cavidades disponibles para esta parte. Revisa los datos técnicos de la parte.";
        }

        var noPermitidas =
            seleccionadas
                .Where(x => !disponibles.Contains(x))
                .Distinct()
                .OrderBy(x => x)
                .ToList();

        if (noPermitidas.Count > 0)
        {
            return
                "Las siguientes cavidades no están disponibles para esta parte: " +
                string.Join(", ", noPermitidas) +
                ".";
        }

        return null;
    }

    private static async Task<List<int>>
        ObtenerCavidadesDisponiblesConfiguracionAsync(
            int? parteId,
            int? cavidadesBD,
            string? cavidadesConfiguradasActuales,
            SqlConnection cn,
            SqlTransaction? tx = null)
    {
        var resultado = new SortedSet<int>();

        static void AgregarRango(
            SortedSet<int> destino,
            int? cantidad)
        {
            if (!cantidad.HasValue ||
                cantidad.Value <= 0)
            {
                return;
            }

            var limite =
                Math.Min(
                    cantidad.Value,
                    MaximoCavidadesConfigurablesProduccion);

            for (var numero = 1;
                 numero <= limite;
                 numero++)
            {
                destino.Add(numero);
            }
        }

       
        AgregarRango(
            resultado,
            cavidadesBD);

        
        foreach (var cavidad in
            ParsearCavidadesConfiguracion(
                cavidadesConfiguradasActuales))
        {
            resultado.Add(cavidad);
        }

        if (!parteId.HasValue ||
            parteId.Value <= 0)
        {
            return resultado.ToList();
        }

       
        const string sqlPlantilla = @"
SELECT TOP(1)
    h.PlantillaHCCID,
    h.CavidadesDeclaradas
FROM dbo.Calidad_HCC_PlantillaPartes pp
INNER JOIN dbo.Calidad_HCC_Plantillas h
    ON h.PlantillaHCCID=pp.PlantillaHCCID
   AND h.Activo=1
WHERE pp.ParteID=@ParteID
  AND pp.Activo=1
ORDER BY
    h.EsVigente DESC,
    pp.EsPrincipal DESC,
    h.FechaModificacionFormato DESC,
    h.PlantillaHCCID DESC;";

        int? plantillaId = null;
        int? cavidadesDeclaradas = null;

        await using (var cmd =
            tx == null
                ? new SqlCommand(
                    sqlPlantilla,
                    cn)
                : new SqlCommand(
                    sqlPlantilla,
                    cn,
                    tx))
        {
            cmd.Parameters.Add(
                "@ParteID",
                SqlDbType.Int).Value =
                parteId.Value;

            await using var rd =
                await cmd.ExecuteReaderAsync();

            if (await rd.ReadAsync())
            {
                plantillaId =
                    Convert.ToInt32(
                        rd["PlantillaHCCID"]);

                cavidadesDeclaradas =
                    rd["CavidadesDeclaradas"] == DBNull.Value
                        ? null
                        : Convert.ToInt32(
                            rd["CavidadesDeclaradas"]);
            }
        }

        AgregarRango(
            resultado,
            cavidadesDeclaradas);

        if (!plantillaId.HasValue)
            return resultado.ToList();

        const string sqlCavidades = @"
SELECT DISTINCT
    cc.NumeroCavidad
FROM dbo.Calidad_HCC_CaracteristicaCavidades cc
INNER JOIN dbo.Calidad_HCC_Caracteristicas c
    ON c.CaracteristicaHCCID=cc.CaracteristicaHCCID
WHERE c.PlantillaHCCID=@PlantillaHCCID
  AND c.Activo=1
  AND cc.Activo=1
  AND cc.NumeroCavidad>0
ORDER BY
    cc.NumeroCavidad;";

        await using (var cmd =
            tx == null
                ? new SqlCommand(
                    sqlCavidades,
                    cn)
                : new SqlCommand(
                    sqlCavidades,
                    cn,
                    tx))
        {
            cmd.Parameters.Add(
                "@PlantillaHCCID",
                SqlDbType.Int).Value =
                plantillaId.Value;

            await using var rd =
                await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                var numero =
                    Convert.ToInt32(
                        rd["NumeroCavidad"]);

                if (numero > 0 &&
                    numero <=
                    MaximoCavidadesConfigurablesProduccion)
                {
                    resultado.Add(numero);
                }
            }
        }

        return resultado.ToList();
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