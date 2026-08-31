using ERP.NSQuell.Models;
using ERP.NSQuell.Servicios.Planeacion;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Globalization;

namespace ERP.NSQuell.Controllers
{
    [Route("PlaneacionCalendarioMaquinas")]
    public sealed partial class PlaneacionCalendarioMaquinasController : Controller // NSQ_TODO_PLANEACION_PRODUCCION_V1
    {
        private readonly IConfiguration _configuration;
        private readonly IPlaneacionSecuenciaService _planeacionSecuenciaService;

        public PlaneacionCalendarioMaquinasController(IConfiguration configuration, IPlaneacionSecuenciaService planeacionSecuenciaService)
        {
            _configuration = configuration;
            _planeacionSecuenciaService = planeacionSecuenciaService;
        }
        private string ConnectionString =>
            _configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "No se encontró la cadena de conexión DefaultConnection.");

        private static class EstatusPrograma
        {
            public const int Programado = 1;
            public const int EnPreparacion = 2;
            public const int EnProduccion = 3;
            public const int Pausado = 4;
            public const int TerminadoParcial = 5;
            public const int Terminado = 6;
            public const int Cerrado = 9;
            public const int Cancelado = 99;
        }

        [HttpGet("")]
        [HttpGet("Index")]
        [HttpGet("/Produccion/Calendario", Name = "ProduccionCalendario")]
        public async Task<IActionResult> Index(string? vista, DateTime? fecha, DateTime? rangoInicio, DateTime? rangoFin)
        {
            if (!UsuarioEnSesion()) return RedirectToAction("Login", "Login");

            var modoProduccion =
                Request.Path.Value?.StartsWith("/Produccion/Calendario", StringComparison.OrdinalIgnoreCase) == true ||
                string.Equals(Request.Query["modo"].ToString(), "produccion", StringComparison.OrdinalIgnoreCase);

            ViewBag.ModoProduccion = modoProduccion;

            var periodo = ResolverPeriodo(vista, fecha, rangoInicio, rangoFin);
            var ahora = DateTime.Now;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            var maquinas = Excluir1200TCalendario(
                await ObtenerMaquinasCalendarioAsync(periodo.Inicio, periodo.Fin, cn));

            await AplicarPersonalV7CalendarioAsync(maquinas, periodo.Inicio, periodo.Fin, cn);

            var proyeccion =
                await _planeacionSecuenciaService.ProyectarInterrupcionesActivasAsync(
                    ahora,
                    trabajarDomingo: false,
                    cn);

            AplicarProyeccionInterrupcionesCalendario(maquinas, proyeccion);

            var solicitudesReprogramacion = modoProduccion
                ? new List<SolicitudReprogramacionCalendarioVm>()
                : await ObtenerSolicitudesReprogramacionPendientesAsync(cn);

            var vm = new PlaneacionCalendarioMaquinasVm
            {
                Vista = periodo.Vista,
                InicioPeriodo = periodo.Inicio,
                FinPeriodo = periodo.Fin,
                FechaReferencia = fecha,
                RangoInicio = periodo.RangoInicio,
                RangoFin = periodo.RangoFin,
                Ahora = ahora,
                Maquinas = maquinas
            };

            ViewBag.SolicitudesReprogramacion = solicitudesReprogramacion;
            ViewBag.TotalSolicitudesReprogramacion = solicitudesReprogramacion.Count;
            ViewBag.HayInterrupcionesActivas = proyeccion.HayInterrupcionesActivas;
            ViewBag.TotalInterrupcionesActivas = proyeccion.TotalInterrupcionesActivas;
            ViewBag.FechaCalculoProyeccion = proyeccion.FechaCalculo;

            return View(vm);
        }

        private static void AplicarProyeccionInterrupcionesCalendario(
    List<PlaneacionCalendarioMaquinaVm> maquinas,
    PlaneacionProyeccionInterrupcionesResultado proyeccion)
        {
            if (maquinas == null || maquinas.Count == 0 || proyeccion.Programas == null || proyeccion.Programas.Count == 0)
                return;

            var proyecciones = proyeccion.Programas
                .GroupBy(x => x.ProgramaProduccionID)
                .ToDictionary(x => x.Key, x => x.First());

            foreach (var maquina in maquinas)
            {
                foreach (var bloque in maquina.Bloques)
                {
                    if (!proyecciones.TryGetValue(bloque.ProgramaProduccionID, out var programa))
                        continue;

                    bloque.InicioProyectado = programa.InicioProyectado;
                    bloque.FinProyectado = programa.FinProyectado;
                    bloque.EsProgramaRaizInterrupcion = programa.EsProgramaRaizInterrupcion;
                    bloque.ParoProyeccionID = programa.ParoID;
                    bloque.TipoInterrupcionProyectada = programa.TipoInterrupcion ?? string.Empty;
                    bloque.MotivoInterrupcionProyectada = programa.MotivoParo ?? string.Empty;
                    bloque.MinutosImpactoInterrupcion = programa.MinutosImpactoInterrupcion;
                    bloque.MinutosDesplazamientoProyectado = programa.MinutosDesplazamiento;
                }
            }
        }

        [HttpGet("ProyeccionInterrupcionesActivas")]
        public async Task<IActionResult> ProyeccionInterrupcionesActivas(DateTime? rangoInicio = null, DateTime? rangoFin = null, bool trabajarDomingo = false)
        {
            if (!UsuarioEnSesion())
            {
                return Json(new
                {
                    ok = false,
                    sesionExpirada = true,
                    mensaje = "La sesión terminó. Vuelve a iniciar sesión para continuar."
                });
            }

            try
            {
                await using var cn = new SqlConnection(ConnectionString);
                await cn.OpenAsync();

                var proyeccion =
                    await _planeacionSecuenciaService.ProyectarInterrupcionesActivasAsync(
                        DateTime.Now,
                        trabajarDomingo,
                        cn);

                var programas = proyeccion.Programas.AsEnumerable();

                if (rangoInicio.HasValue)
                    programas = programas.Where(x => x.FinProyectado > rangoInicio.Value);

                if (rangoFin.HasValue)
                    programas = programas.Where(x => x.InicioProyectado < rangoFin.Value);

                var lista = programas
                    .OrderBy(x => x.InicioProyectado)
                    .ThenBy(x => x.MaquinaID)
                    .ThenBy(x => x.ProgramaProduccionID)
                    .Select(x => new
                    {
                        programaProduccionID = x.ProgramaProduccionID,
                        maquinaID = x.MaquinaID,
                        moldeID = x.MoldeID,
                        ejecucionProduccionID = x.EjecucionProduccionID,
                        paroID = x.ParoID,
                        esProgramaRaizInterrupcion = x.EsProgramaRaizInterrupcion,
                        tipoInterrupcion = x.TipoInterrupcion,
                        motivoParo = x.MotivoParo,
                        inicioOriginal = x.InicioOriginal,
                        finOriginal = x.FinOriginal,
                        inicioProyectado = x.InicioProyectado,
                        finProyectado = x.FinProyectado,
                        cambioOriginal = x.CambioOriginal,
                        arranqueOriginal = x.ArranqueOriginal,
                        cambioProyectado = x.CambioProyectado,
                        arranqueProyectado = x.ArranqueProyectado,
                        minutosImpactoInterrupcion = x.MinutosImpactoInterrupcion,
                        minutosDesplazamiento = x.MinutosDesplazamiento
                    })
                    .ToList();

                return Json(new
                {
                    ok = true,
                    fechaCalculo = proyeccion.FechaCalculo,
                    hayInterrupcionesActivas = proyeccion.HayInterrupcionesActivas,
                    totalInterrupcionesActivas = proyeccion.TotalInterrupcionesActivas,
                    programas = lista
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    ok = false,
                    sesionExpirada = false,
                    mensaje = "No fue posible calcular la proyección actual de Producción: " + ex.Message
                });
            }
        }

        [HttpGet("PrevisualizarInterrupcionUrgente")]
        public async Task<IActionResult> PrevisualizarInterrupcionUrgente(int programaUrgenteId, int maquinaId, bool trabajarDomingo = false)
        {
            if (!UsuarioEnSesion())
                return Unauthorized(new { ok = false, sesionExpirada = true, mensaje = "La sesión terminó. Vuelve a iniciar sesión." });

            if (programaUrgenteId <= 0 || maquinaId <= 0)
                return BadRequest(new { ok = false, mensaje = "El programa urgente y la máquina son obligatorios." });

            try
            {
                await using var cn = new SqlConnection(ConnectionString);
                await cn.OpenAsync();

                var programaUrgente = await ObtenerProgramaBaseAsync(programaUrgenteId, cn, null, bloquear: false);
                if (programaUrgente == null)
                    return NotFound(new { ok = false, mensaje = "No se encontró el programa que se desea declarar urgente." });

                var motivoBloqueo = await ObtenerMotivoBloqueoMovimientoAsync(programaUrgenteId, cn, null, bloquear: false);
                if (!string.IsNullOrWhiteSpace(motivoBloqueo))
                    return BadRequest(new { ok = false, mensaje = "La OF seleccionada no puede utilizarse como interrupción urgente. " + motivoBloqueo });

                var maquinasCompatibles = await ObtenerMaquinasCompatiblesAsync(programaUrgente, cn, null);
                var maquinaDestino = maquinasCompatibles.FirstOrDefault(x => x.MaquinaID == maquinaId);
                if (maquinaDestino == null)
                {
                    return BadRequest(new
                    {
                        ok = false,
                        mensaje = "La OF urgente no es compatible con la máquina seleccionada.",
                        maquinasPermitidas = maquinasCompatibles.Select(x => new { maquinaID = x.MaquinaID, codigo = x.Codigo, nombre = x.Nombre }).ToList()
                    });
                }

                var programaActual = await ObtenerProduccionActivaParaInterrupcionUrgenteAsync(maquinaId, cn);
                if (programaActual == null)
                {
                    return BadRequest(new
                    {
                        ok = false,
                        mensaje = "La máquina seleccionada no tiene una OF en producción que pueda ser interrumpida. Si la máquina está libre, utiliza la reprogramación normal del calendario."
                    });
                }

                if (programaActual.ProgramaProduccionID == programaUrgenteId)
                    return BadRequest(new { ok = false, mensaje = "La OF urgente y la OF actualmente producida no pueden ser la misma." });

                if (programaActual.EstatusProduccionID != EstatusPrograma.EnProduccion)
                {
                    return BadRequest(new
                    {
                        ok = false,
                        mensaje = $"La OF actual está en estado {NombreEstatusProduccion(programaActual.EstatusProduccionID)}. Solo se permitirá iniciar una interrupción urgente cuando la máquina esté produciendo en serie."
                    });
                }

                var proyeccionInterrupciones = await _planeacionSecuenciaService.ProyectarInterrupcionesActivasAsync(DateTime.Now, trabajarDomingo, cn);
                var yaTieneInterrupcion = proyeccionInterrupciones.Programas.Any(x => x.EsProgramaRaizInterrupcion && x.ProgramaProduccionID == programaActual.ProgramaProduccionID);
                if (yaTieneInterrupcion)
                {
                    return BadRequest(new
                    {
                        ok = false,
                        mensaje = "La OF que se encuentra produciendo ya tiene una interrupción activa. No se permiten interrupciones anidadas."
                    });
                }

                var ahora = NormalizarFecha(DateTime.Now);
                var fechaInterrupcion = SiguienteAperturaOperativa(ahora, trabajarDomingo);
                var cambiaMolde = programaActual.MoldeID != programaUrgente.MoldeID;
                var horasCambioIda = cambiaMolde ? 1m : 0m;
                var horasCambioRegreso = cambiaMolde ? 1m : 0m;
                var horasProduccionUrgente = programaUrgente.HorasProgramadas > 0 ? programaUrgente.HorasProgramadas : 1m;

                var fechaCambioUrgente = fechaInterrupcion;
                var fechaArranqueUrgente = SumarHorasOperativas(fechaCambioUrgente, horasCambioIda, trabajarDomingo);
                var fechaFinUrgente = SumarHorasOperativas(fechaArranqueUrgente, horasProduccionUrgente, trabajarDomingo);
                var fechaPreparacionRegreso = fechaFinUrgente;
                var fechaReinicioOriginal = SumarHorasOperativas(fechaPreparacionRegreso, horasCambioRegreso, trabajarDomingo);

                var horasImpactoTotal = horasCambioIda + horasProduccionUrgente + horasCambioRegreso;
                var finOriginalBase = programaActual.FechaFinProgramada > fechaInterrupcion ? programaActual.FechaFinProgramada : fechaInterrupcion;
                var finOriginalProyectado = SumarHorasOperativas(finOriginalBase, horasImpactoTotal, trabajarDomingo);

                var programasPosterioresImpactados = await ContarProgramasImpactadosPorInterrupcionUrgenteAsync(maquinaId, programaUrgenteId, programaActual.ProgramaProduccionID, fechaInterrupcion, cn);

                var parejaActualId = await ObtenerProgramaParejaLhRhAsync(programaActual.ProgramaProduccionID, cn);
                var parejaUrgenteId = await ObtenerProgramaParejaLhRhAsync(programaUrgenteId, cn);

                var horarioActualVencido = programaActual.FechaFinProgramada <= ahora;

                var advertencias = new List<string>();
                if (cambiaMolde)
                    advertencias.Add("La OF urgente utiliza un molde diferente. Se contempla 1 hora de preparación antes de la urgente y 1 hora para preparar nuevamente la OF interrumpida.");
                if (parejaActualId.HasValue)
                    advertencias.Add($"La OF que está produciendo pertenece a una pareja LH/RH. Su programa relacionado es {parejaActualId.Value} y deberá tratarse como parte de la misma interrupción.");
                if (parejaUrgenteId.HasValue)
                    advertencias.Add($"La OF urgente pertenece a una pareja LH/RH. Su programa relacionado es {parejaUrgenteId.Value} y deberá programarse junto con ella.");
                if (horarioActualVencido)
                    advertencias.Add("La OF actual ya superó su fin programado. La proyección utiliza la hora actual como base mínima; el tiempo restante real deberá conservarse desde Producción.");

                var motivoResumen = cambiaMolde
                    ? "La interrupción requiere cambio de molde y posteriormente nueva preparación de la OF original."
                    : "La interrupción conserva el molde actual; no se agrega una hora de cambio de molde.";

                return Json(new
                {
                    ok = true,
                    requiereConfirmacion = true,
                    fechaCalculo = DateTime.Now,
                    maquina = new
                    {
                        maquinaID = maquinaDestino.MaquinaID,
                        codigo = maquinaDestino.Codigo,
                        nombre = maquinaDestino.Nombre
                    },
                    programaInterrumpido = new
                    {
                        programaProduccionID = programaActual.ProgramaProduccionID,
                        ejecucionProduccionID = programaActual.EjecucionProduccionID,
                        solicitudProduccionID = programaActual.SolicitudProduccionID,
                        parte = programaActual.ParteTexto,
                        descripcion = programaActual.DescripcionParte,
                        moldeID = programaActual.MoldeID,
                        molde = programaActual.MoldeTexto,
                        inicioProgramado = programaActual.FechaInicioProgramada,
                        finProgramado = programaActual.FechaFinProgramada,
                        finProyectado = finOriginalProyectado,
                        parejaLhRhProgramaID = parejaActualId
                    },
                    programaUrgente = new
                    {
                        programaProduccionID = programaUrgente.ProgramaProduccionID,
                        solicitudProduccionID = programaUrgente.SolicitudProduccionID,
                        parte = ObtenerTextoParte(programaUrgente),
                        descripcion = programaUrgente.DescripcionParte,
                        moldeID = programaUrgente.MoldeID,
                        molde = string.IsNullOrWhiteSpace(programaUrgente.MoldeCodigo) ? "Sin molde" : programaUrgente.MoldeCodigo,
                        inicioProgramadoActual = programaUrgente.FechaInicioProgramada,
                        finProgramadoActual = programaUrgente.FechaFinProgramada,
                        horasProduccion = Math.Round(horasProduccionUrgente, 4),
                        parejaLhRhProgramaID = parejaUrgenteId
                    },
                    interrupcion = new
                    {
                        fechaInterrupcion,
                        cambiaMolde,
                        horasCambioIda,
                        fechaArranqueUrgente,
                        fechaFinUrgente,
                        horasCambioRegreso,
                        fechaReinicioOriginal,
                        horasImpactoTotal = Math.Round(horasImpactoTotal, 4),
                        minutosImpactoTotal = (int)Math.Round(horasImpactoTotal * 60m, 0, MidpointRounding.AwayFromZero),
                        programasPosterioresImpactados
                    },
                    horarioActualVencido,
                    motivoResumen,
                    advertencias,
                    mensaje = programasPosterioresImpactados > 0
                        ? $"La interrupción urgente afectará la OF actualmente producida y puede recorrer {programasPosterioresImpactados} programa(s) posterior(es)."
                        : "La interrupción urgente afectará la OF actualmente producida. No se detectaron programas posteriores reacomodables en esta máquina."
                });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { ok = false, mensaje = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { ok = false, mensaje = "No fue posible calcular la interrupción urgente: " + ex.Message });
            }
        }


        [HttpPost("ConfirmarInterrupcionUrgente")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmarInterrupcionUrgente([FromBody] PlaneacionInterrupcionUrgenteRequest request)
        {
            if (!UsuarioEnSesion()) return Unauthorized(new { ok = false, sesionExpirada = true, mensaje = "La sesión terminó. Vuelve a iniciar sesión." });
            if (request == null || request.ProgramaUrgenteID <= 0 || request.MaquinaID <= 0) return BadRequest(new { ok = false, mensaje = "La OF urgente y la máquina son obligatorias." });
            request.Motivo = (request.Motivo ?? string.Empty).Trim();
            if (request.Motivo.Length < 5) return BadRequest(new { ok = false, mensaje = "Escribe un motivo claro para justificar la interrupción urgente." });
            if (request.Motivo.Length > 500) return BadRequest(new { ok = false, mensaje = "El motivo no puede superar 500 caracteres." });
            var usuarioId = ObtenerUsuarioID();
            if (usuarioId <= 0) return Unauthorized(new { ok = false, mensaje = "No fue posible identificar al usuario." });
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                await TomarCandadoCalendarioAsync(cn, tx);
                await ActivarReacomodoPlaneacionAsync(cn, tx);
                var programaUrgente = await ObtenerProgramaBaseAsync(request.ProgramaUrgenteID, cn, tx, bloquear: true);
                if (programaUrgente == null) throw new InvalidOperationException("No se encontró la OF urgente.");
                var motivoBloqueo = await ObtenerMotivoBloqueoMovimientoAsync(programaUrgente.ProgramaProduccionID, cn, tx, bloquear: true);
                if (!string.IsNullOrWhiteSpace(motivoBloqueo)) throw new InvalidOperationException("La OF urgente ya no está disponible. " + motivoBloqueo);
                var compatibles = await ObtenerMaquinasCompatiblesAsync(programaUrgente, cn, tx);
                var maquinaDestino = compatibles.FirstOrDefault(x => x.MaquinaID == request.MaquinaID);
                if (maquinaDestino == null) throw new InvalidOperationException("La máquina seleccionada ya no es compatible con la OF urgente.");
                var programaActual = await ObtenerProduccionActivaParaInterrupcionUrgenteAsync(request.MaquinaID, cn, tx, bloquear: true);
                if (programaActual == null) throw new InvalidOperationException("La máquina ya no tiene una OF activa en Producción. Actualiza el calendario y vuelve a intentarlo.");
                if (programaActual.EstatusProduccionID != EstatusPrograma.EnProduccion) throw new InvalidOperationException($"La OF actual ya no se encuentra produciendo. Estado actual: {NombreEstatusProduccion(programaActual.EstatusProduccionID)}.");
                var parejaActualId = await ObtenerProgramaParejaLhRhAsync(programaActual.ProgramaProduccionID, cn, tx);
                var parejaUrgenteId = await ObtenerProgramaParejaLhRhAsync(programaUrgente.ProgramaProduccionID, cn, tx);
                ProgramaActivoInterrupcionUrgente? programaActualPareja = null;
                ProgramaBase? programaUrgentePareja = null;
                if (programaActual.ProgramaProduccionID == programaUrgente.ProgramaProduccionID || parejaUrgenteId == programaActual.ProgramaProduccionID || parejaActualId == programaUrgente.ProgramaProduccionID || (parejaActualId.HasValue && parejaUrgenteId.HasValue && parejaActualId.Value == parejaUrgenteId.Value))
                    throw new InvalidOperationException("La OF urgente no puede ser la misma OF que actualmente se encuentra produciendo ni su contraparte LH/RH.");
                if (await ExisteParoAbiertoEjecucionAsync(programaActual.EjecucionProduccionID, cn, tx))
                    throw new InvalidOperationException("La ejecución actual ya tiene un paro abierto. No se permiten interrupciones anidadas.");
                if (parejaActualId.HasValue)
                {
                    programaActualPareja = await ObtenerProduccionActivaProgramaInterrupcionUrgenteAsync(parejaActualId.Value, request.MaquinaID, cn, tx, bloquear: true);
                    if (programaActualPareja == null) throw new InvalidOperationException($"La OF que está produciendo pertenece a una pareja LH/RH, pero no se encontró una ejecución activa para el Programa {parejaActualId.Value}. No se realizará una interrupción parcial.");
                    if (programaActualPareja.EstatusProduccionID != EstatusPrograma.EnProduccion) throw new InvalidOperationException($"La pareja LH/RH del programa actual no está produciendo. Programa {programaActualPareja.ProgramaProduccionID}, estado {NombreEstatusProduccion(programaActualPareja.EstatusProduccionID)}.");
                    if (programaActualPareja.MaquinaID != programaActual.MaquinaID) throw new InvalidOperationException("Las OF LH/RH actuales ya no están ejecutándose en la misma máquina.");
                    if (!CoincidenMoldesInterrupcionUrgente(programaActual.MoldeID, programaActual.MoldeCodigo, programaActualPareja.MoldeID, programaActualPareja.MoldeCodigo)) throw new InvalidOperationException("Las OF LH/RH actuales ya no conservan el mismo molde.");
                    if (programaActual.FechaInicioProgramada != programaActualPareja.FechaInicioProgramada || programaActual.FechaFinProgramada != programaActualPareja.FechaFinProgramada) throw new InvalidOperationException("Las OF LH/RH actuales ya no conservan la misma ventana programada.");
                    if (await ExisteParoAbiertoEjecucionAsync(programaActualPareja.EjecucionProduccionID, cn, tx)) throw new InvalidOperationException("La pareja LH/RH de la ejecución actual ya tiene un paro abierto. No se realizará una interrupción parcial.");
                }
                if (await ExisteInterrupcionUrgenteActivaParaProgramaAsync(programaUrgente.ProgramaProduccionID, cn, tx))
                    throw new InvalidOperationException("Esta OF ya participa en otra interrupción urgente activa.");
                if (parejaUrgenteId.HasValue)
                {
                    programaUrgentePareja = await ObtenerProgramaBaseAsync(parejaUrgenteId.Value, cn, tx, bloquear: true);
                    if (programaUrgentePareja == null) throw new InvalidOperationException($"La OF urgente pertenece a una pareja LH/RH, pero no se encontró el Programa {parejaUrgenteId.Value}.");
                    var bloqueoPareja = await ObtenerMotivoBloqueoMovimientoAsync(programaUrgentePareja.ProgramaProduccionID, cn, tx, bloquear: true);
                    if (!string.IsNullOrWhiteSpace(bloqueoPareja)) throw new InvalidOperationException($"La pareja LH/RH de la OF urgente no está disponible. {bloqueoPareja}");
                    var compatiblesPareja = await ObtenerMaquinasCompatiblesAsync(programaUrgentePareja, cn, tx);
                    if (!compatiblesPareja.Any(x => x.MaquinaID == request.MaquinaID)) throw new InvalidOperationException("La máquina seleccionada no es compatible con ambas OF de la pareja LH/RH urgente.");
                    if (programaUrgentePareja.MaquinaID != programaUrgente.MaquinaID) throw new InvalidOperationException("La pareja LH/RH urgente ya no conserva la misma máquina programada antes de la interrupción.");
                    if (!CoincidenMoldesInterrupcionUrgente(programaUrgente.MoldeID, programaUrgente.MoldeCodigo, programaUrgentePareja.MoldeID, programaUrgentePareja.MoldeCodigo)) throw new InvalidOperationException("La pareja LH/RH urgente ya no conserva el mismo molde.");
                    if (programaUrgente.FechaInicioProgramada != programaUrgentePareja.FechaInicioProgramada || programaUrgente.FechaFinProgramada != programaUrgentePareja.FechaFinProgramada) throw new InvalidOperationException("La pareja LH/RH urgente ya no conserva la misma ventana programada.");
                    if (Math.Abs(programaUrgente.HorasProgramadas - programaUrgentePareja.HorasProgramadas) > 0.0001m) throw new InvalidOperationException("La pareja LH/RH urgente tiene horas programadas diferentes. Corrige Planeación antes de interrumpir Producción.");
                    if (await ExisteInterrupcionUrgenteActivaParaProgramaAsync(programaUrgentePareja.ProgramaProduccionID, cn, tx)) throw new InvalidOperationException("La pareja LH/RH de la OF urgente ya participa en otra interrupción.");
                }
                var fechaInterrupcion = SiguienteAperturaOperativa(NormalizarFecha(DateTime.Now), request.TrabajarDomingo);
                var cambiaMolde = !CoincidenMoldesInterrupcionUrgente(programaActual.MoldeID, programaActual.MoldeCodigo, programaUrgente.MoldeID, programaUrgente.MoldeCodigo);
                var horasCambio = cambiaMolde ? 1m : 0m;
                var horasProduccion = programaUrgente.HorasProgramadas > 0 ? programaUrgente.HorasProgramadas : 1m;
                var fechaArranque = SumarHorasOperativas(fechaInterrupcion, horasCambio, request.TrabajarDomingo);
                var fechaFin = SumarHorasOperativas(fechaArranque, horasProduccion, request.TrabajarDomingo);
                if (programaUrgente.MoldeID.HasValue)
                {
                    var finConflictoMolde = await ObtenerFinCruceMoldeInterrupcionUrgenteAsync(
                        programaUrgente.MoldeID.Value,
                        programaUrgente.ProgramaProduccionID,
                        programaUrgentePareja?.ProgramaProduccionID,
                        programaActual.ProgramaProduccionID,
                        programaActualPareja?.ProgramaProduccionID,
                        fechaInterrupcion,
                        fechaFin,
                        cn,
                        tx);
                    if (finConflictoMolde.HasValue) throw new InvalidOperationException($"El molde de la OF urgente está ocupado por otra programación hasta {finConflictoMolde.Value:dd/MM/yyyy HH:mm}. La interrupción no puede confirmarse en este momento.");
                }
                var nuevaSecuencia = await ObtenerSiguienteSecuenciaAsync(maquinaDestino.MaquinaID, programaUrgente.ProgramaProduccionID, cn, tx);
                var esInterrupcionLhRh = programaActualPareja != null;
                Guid? grupoParoLhRh = esInterrupcionLhRh ? Guid.NewGuid() : null;
                var paroId = await CrearParoInterrupcionUrgenteAsync(programaActual, programaUrgente.ProgramaProduccionID, fechaInterrupcion, request.Motivo, esInterrupcionLhRh, grupoParoLhRh, usuarioId, cn, tx);
                int? paroParejaId = null;
                if (programaActualPareja != null)
                {
                    paroParejaId = await CrearParoInterrupcionUrgenteAsync(programaActualPareja, programaUrgente.ProgramaProduccionID, fechaInterrupcion, request.Motivo, true, grupoParoLhRh, usuarioId, cn, tx);
                }
                await PausarProduccionPorInterrupcionUrgenteAsync(programaActual, usuarioId, cn, tx);
                if (programaActualPareja != null) await PausarProduccionPorInterrupcionUrgenteAsync(programaActualPareja, usuarioId, cn, tx);
                await ActualizarProgramaUrgenteParaInterrupcionAsync(programaUrgente, maquinaDestino, fechaInterrupcion, fechaArranque, fechaFin, horasProduccion, nuevaSecuencia, usuarioId, cn, tx);
                if (programaUrgentePareja != null)
                    await ActualizarProgramaUrgenteParaInterrupcionAsync(programaUrgentePareja, maquinaDestino, fechaInterrupcion, fechaArranque, fechaFin, horasProduccion, nuevaSecuencia + 1, usuarioId, cn, tx);
                await SincronizarDocumentosRelacionadosAsync(programaUrgente, maquinaDestino, fechaInterrupcion, fechaArranque, fechaFin, horasProduccion, cn, tx);
                await SincronizarSecadoDesdeReprogramacionAsync(programaUrgente.ProgramaProduccionID, usuarioId, cn, tx);
                if (programaUrgentePareja != null)
                {
                    await SincronizarDocumentosRelacionadosAsync(programaUrgentePareja, maquinaDestino, fechaInterrupcion, fechaArranque, fechaFin, horasProduccion, cn, tx);
                    await SincronizarSecadoDesdeReprogramacionAsync(programaUrgentePareja.ProgramaProduccionID, usuarioId, cn, tx);
                }
                await InsertarHistorialInterrupcionUrgenteAsync(programaUrgente, programaActual, maquinaDestino, fechaInterrupcion, fechaArranque, fechaFin, horasProduccion, usuarioId, request.Motivo, cn, tx);
                if (programaUrgentePareja != null)
                    await InsertarHistorialInterrupcionUrgenteAsync(programaUrgentePareja, programaActualPareja ?? programaActual, maquinaDestino, fechaInterrupcion, fechaArranque, fechaFin, horasProduccion, usuarioId, request.Motivo, cn, tx);
                await ReordenarSecuenciasAsync(programaUrgente.MaquinaID, maquinaDestino.MaquinaID, cn, tx);
                await DesactivarReacomodoPlaneacionAsync(cn, tx);
                await tx.CommitAsync();
                var mensaje = programaActualPareja != null
                    ? $"Interrupción urgente registrada. Los Programas {programaActual.ProgramaProduccionID} y {programaActualPareja.ProgramaProduccionID} quedaron pausados conjuntamente."
                    : $"Interrupción urgente registrada. El Programa {programaActual.ProgramaProduccionID} quedó pausado.";
                mensaje += programaUrgentePareja != null
                    ? $" Los Programas urgentes LH/RH {programaUrgente.ProgramaProduccionID} y {programaUrgentePareja.ProgramaProduccionID} quedaron juntos en preparación en la máquina {maquinaDestino.Codigo}."
                    : $" El Programa urgente {programaUrgente.ProgramaProduccionID} quedó en preparación en la máquina {maquinaDestino.Codigo}.";
                return Json(new
                {
                    ok = true,
                    paroID = paroId,
                    paroParejaID = paroParejaId,
                    grupoParoLhRh,
                    programaInterrumpidoID = programaActual.ProgramaProduccionID,
                    programaInterrumpidoParejaID = programaActualPareja?.ProgramaProduccionID,
                    ejecucionInterrumpidaID = programaActual.EjecucionProduccionID,
                    ejecucionInterrumpidaParejaID = programaActualPareja?.EjecucionProduccionID,
                    programaUrgenteID = programaUrgente.ProgramaProduccionID,
                    programaUrgenteParejaID = programaUrgentePareja?.ProgramaProduccionID,
                    maquinaID = maquinaDestino.MaquinaID,
                    maquinaCodigo = maquinaDestino.Codigo,
                    cambiaMolde,
                    fechaInterrupcion,
                    fechaArranqueUrgente = fechaArranque,
                    fechaFinUrgente = fechaFin,
                    mensaje
                });
            }
            catch (SqlException ex) when (ex.Number == 51010 || ex.Number == 51620 || ex.Number == 51621 || ex.Number == 51622)
            {
                await RollbackSeguroAsync(tx);
                return BadRequest(new { ok = false, mensaje = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                await RollbackSeguroAsync(tx);
                return BadRequest(new { ok = false, mensaje = ex.Message });
            }
            catch (Exception ex)
            {
                await RollbackSeguroAsync(tx);
                return StatusCode(500, new { ok = false, mensaje = "No fue posible confirmar la interrupción urgente: " + ex.Message });
            }
            finally
            {
                await LimpiarContextoSinTransaccionAsync(cn);
            }
        }

        [HttpGet("MaquinasCompatibles")]
        public async Task<IActionResult> MaquinasCompatibles(int programaProduccionId)
        {
            if (programaProduccionId <= 0)
            {
                return Json(new
                {
                    ok = false,
                    mensaje = "No se recibió el programa de producción."
                });
            }

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            try
            {
                var programa = await ObtenerProgramaBaseAsync(
                    programaProduccionId,
                    cn,
                    null,
                    bloquear: false);

                if (programa == null)
                {
                    return Json(new
                    {
                        ok = false,
                        mensaje = "No se encontró el programa."
                    });
                }

                var motivoBloqueo = await ObtenerMotivoBloqueoMovimientoAsync(
                    programaProduccionId,
                    cn,
                    null,
                    bloquear: false);

                if (!string.IsNullOrWhiteSpace(motivoBloqueo))
                {
                    return Json(new
                    {
                        ok = false,
                        mensaje = motivoBloqueo
                    });
                }

                var maquinas = await ObtenerMaquinasCompatiblesAsync(
                    programa,
                    cn,
                    null);

                return Json(new
                {
                    ok = true,
                    maquinas = maquinas.Select(x => new
                    {
                        maquinaID = x.MaquinaID,
                        codigo = x.Codigo,
                        nombre = x.Nombre
                    })
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    ok = false,
                    mensaje = "No fue posible consultar máquinas compatibles: " + ex.Message
                });
            }
        }

        [HttpGet("AlertasReprogramacion")]
        public async Task<IActionResult> AlertasReprogramacion(int? programaProduccionId = null, int? maquinaId = null)
        {
            if (!UsuarioEnSesion()) return Unauthorized(new { ok = false, mensaje = "La sesión terminó. Vuelve a iniciar sesión." });
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            const string sql = @"
DECLARE @Ahora DATETIME2=GETDATE();
DECLARE @LimiteMuyReciente DATETIME2=DATEADD(HOUR,-2,@Ahora);
DECLARE @LimiteReciente DATETIME2=DATEADD(HOUR,-24,@Ahora);
SELECT
    h.ReprogramacionHistorialID,
    h.ProgramaProduccionID,
    h.MaquinaAnteriorID,
    ISNULL(ma.Codigo,N'SIN MÁQUINA') AS MaquinaAnteriorCodigo,
    ISNULL(ma.Nombre,N'') AS MaquinaAnteriorNombre,
    h.MaquinaNuevaID,
    ISNULL(mn.Codigo,N'SIN MÁQUINA') AS MaquinaNuevaCodigo,
    ISNULL(mn.Nombre,N'') AS MaquinaNuevaNombre,
    h.InicioAnterior,
    h.InicioNuevo,
    h.FinAnterior,
    h.FinNuevo,
    h.HorasAnteriores,
    h.HorasNuevas,
    h.CambioAnterior,
    h.CambioNuevo,
    h.ArranqueAnterior,
    h.ArranqueNuevo,
    h.ReleaseDetalleID,
    h.SolicitudProduccionID,
    h.SolicitudProduccionDetalleID,
    h.DaTiempoDespues,
    h.FechaRequeridaCliente,
    h.TipoMovimiento,
    ISNULL(h.EsMovimientoAutomatico,0) AS EsMovimientoAutomatico,
    h.ProgramaOrigenMovimientoID,
    h.UsuarioID,
    h.FechaCambio,
    h.Motivo,
    pp.NumeroParte,
    pp.ReferenciaSAP,
    pp.DesignacionDescripcionSAP AS DescripcionParte,
    pp.MoldeCodigo,
    CASE WHEN h.FechaCambio>=@LimiteMuyReciente THEN N'MUY_RECIENTE' ELSE N'RECIENTE' END AS NivelAlerta,
    CASE WHEN h.FechaCambio>=@LimiteMuyReciente THEN 1 ELSE 2 END AS OrdenAlerta,
    DATEDIFF(MINUTE,h.FechaCambio,@Ahora) AS MinutosDesdeCambio,
    CASE WHEN h.MaquinaAnteriorID<>h.MaquinaNuevaID THEN 1 ELSE 0 END AS CambioMaquina,
    CASE WHEN h.InicioAnterior<>h.InicioNuevo THEN 1 ELSE 0 END AS CambioInicio,
    CASE WHEN h.FinAnterior<>h.FinNuevo THEN 1 ELSE 0 END AS CambioFin
FROM dbo.Planeacion_ProgramaReprogramacionHistorial h
INNER JOIN dbo.Planeacion_ProgramaProduccion pp ON pp.ProgramaProduccionID=h.ProgramaProduccionID
LEFT JOIN dbo.ERP_Maquinas ma ON ma.MaquinaID=h.MaquinaAnteriorID
LEFT JOIN dbo.ERP_Maquinas mn ON mn.MaquinaID=h.MaquinaNuevaID
WHERE h.FechaCambio>=@LimiteReciente
  AND (@ProgramaProduccionID IS NULL OR h.ProgramaProduccionID=@ProgramaProduccionID)
  AND (@MaquinaID IS NULL OR h.MaquinaAnteriorID=@MaquinaID OR h.MaquinaNuevaID=@MaquinaID)
ORDER BY OrdenAlerta,h.FechaCambio DESC,h.ReprogramacionHistorialID DESC;";
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaProduccionId.HasValue && programaProduccionId.Value > 0 ? programaProduccionId.Value : DBNull.Value;
            cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = maquinaId.HasValue && maquinaId.Value > 0 ? maquinaId.Value : DBNull.Value;
            var alertas = new List<object>();
            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                var inicioAnterior = rd["InicioAnterior"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(rd["InicioAnterior"]);
                var inicioNuevo = rd["InicioNuevo"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(rd["InicioNuevo"]);
                var finAnterior = rd["FinAnterior"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(rd["FinAnterior"]);
                var finNuevo = rd["FinNuevo"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(rd["FinNuevo"]);
                var maquinaAnteriorCodigo = rd["MaquinaAnteriorCodigo"]?.ToString()?.Trim() ?? "SIN MÁQUINA";
                var maquinaNuevaCodigo = rd["MaquinaNuevaCodigo"]?.ToString()?.Trim() ?? "SIN MÁQUINA";
                var cambioMaquina = rd["CambioMaquina"] != DBNull.Value && Convert.ToBoolean(rd["CambioMaquina"]);
                var cambioInicio = rd["CambioInicio"] != DBNull.Value && Convert.ToBoolean(rd["CambioInicio"]);
                var cambioFin = rd["CambioFin"] != DBNull.Value && Convert.ToBoolean(rd["CambioFin"]);
                var esMovimientoAutomatico = rd["EsMovimientoAutomatico"] != DBNull.Value && Convert.ToBoolean(rd["EsMovimientoAutomatico"]);
                var tipoMovimiento = rd["TipoMovimiento"]?.ToString()?.Trim();
                if (string.IsNullOrWhiteSpace(tipoMovimiento)) tipoMovimiento = esMovimientoAutomatico ? "RECORRIDO_POR_COLA" : "MOVIDO_MANUAL";
                var cambios = new List<string>();
                if (cambioMaquina) cambios.Add($"Máquina: {maquinaAnteriorCodigo} → {maquinaNuevaCodigo}");
                if (cambioInicio) cambios.Add($"Inicio: {FormatearFechaAlerta(inicioAnterior)} → {FormatearFechaAlerta(inicioNuevo)}");
                if (cambioFin) cambios.Add($"Fin: {FormatearFechaAlerta(finAnterior)} → {FormatearFechaAlerta(finNuevo)}");
                if (!cambios.Any()) cambios.Add("Se actualizó la programación del programa.");
                var numeroParte = rd["ReferenciaSAP"]?.ToString()?.Trim();
                if (string.IsNullOrWhiteSpace(numeroParte)) numeroParte = rd["NumeroParte"]?.ToString()?.Trim();
                var usuarioId = rd["UsuarioID"] == DBNull.Value ? (int?)null : Convert.ToInt32(rd["UsuarioID"]);
                var fechaCambio = Convert.ToDateTime(rd["FechaCambio"]);
                var nivelAlerta = rd["NivelAlerta"]?.ToString()?.Trim() ?? "RECIENTE";
                alertas.Add(new
                {
                    reprogramacionHistorialID = Convert.ToInt32(rd["ReprogramacionHistorialID"]),
                    programaProduccionID = Convert.ToInt32(rd["ProgramaProduccionID"]),
                    programaOrigenMovimientoID = rd["ProgramaOrigenMovimientoID"] == DBNull.Value ? (int?)null : Convert.ToInt32(rd["ProgramaOrigenMovimientoID"]),
                    tipoMovimiento,
                    esMovimientoAutomatico,
                    nivelAlerta,
                    esMuyReciente = string.Equals(nivelAlerta, "MUY_RECIENTE", StringComparison.OrdinalIgnoreCase),
                    minutosDesdeCambio = Convert.ToInt32(rd["MinutosDesdeCambio"]),
                    fechaCambio,
                    fechaCambioTexto = fechaCambio.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture),
                    maquinaAnteriorID = rd["MaquinaAnteriorID"] == DBNull.Value ? (int?)null : Convert.ToInt32(rd["MaquinaAnteriorID"]),
                    maquinaAnteriorCodigo,
                    maquinaAnteriorNombre = rd["MaquinaAnteriorNombre"]?.ToString()?.Trim(),
                    maquinaNuevaID = rd["MaquinaNuevaID"] == DBNull.Value ? (int?)null : Convert.ToInt32(rd["MaquinaNuevaID"]),
                    maquinaNuevaCodigo,
                    maquinaNuevaNombre = rd["MaquinaNuevaNombre"]?.ToString()?.Trim(),
                    inicioAnterior,
                    inicioNuevo,
                    finAnterior,
                    finNuevo,
                    inicioAnteriorTexto = FormatearFechaAlerta(inicioAnterior),
                    inicioNuevoTexto = FormatearFechaAlerta(inicioNuevo),
                    finAnteriorTexto = FormatearFechaAlerta(finAnterior),
                    finNuevoTexto = FormatearFechaAlerta(finNuevo),
                    numeroParte,
                    descripcionParte = rd["DescripcionParte"]?.ToString()?.Trim(),
                    moldeCodigo = rd["MoldeCodigo"]?.ToString()?.Trim(),
                    motivo = rd["Motivo"]?.ToString()?.Trim(),
                    usuarioID = usuarioId,
                    usuarioNombre = usuarioId.HasValue ? $"Usuario {usuarioId.Value}" : "Sistema",
                    cambioMaquina,
                    cambioInicio,
                    cambioFin,
                    cambios,
                    titulo = esMovimientoAutomatico ? "Programa recorrido automáticamente" : "Programa reprogramado",
                    mensaje = string.Join(" · ", cambios)
                });
            }
            return Json(new
            {
                ok = true,
                total = alertas.Count,
                muyRecientes = alertas.Count(x =>
                {
                    var propiedad = x.GetType().GetProperty("esMuyReciente");
                    return propiedad != null && Convert.ToBoolean(propiedad.GetValue(x));
                }),
                recientes = alertas.Count(x =>
                {
                    var propiedad = x.GetType().GetProperty("esMuyReciente");
                    return propiedad == null || !Convert.ToBoolean(propiedad.GetValue(x));
                }),
                consultadoEn = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture),
                alertas
            });
        }


        [HttpPost("ReprogramarCalendario")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReprogramarCalendario(
            [FromBody] CalendarioMaquinasMoverRequest request)
        {
            if (!UsuarioEnSesion())
            {
                return Json(new
                {
                    ok = false,
                    mensaje = "La sesión terminó. Vuelve a iniciar sesión para continuar."
                });
            }

            if (request == null ||
                request.ProgramaProduccionID <= 0 ||
                request.MaquinaID <= 0)
            {
                return Json(new
                {
                    ok = false,
                    mensaje = "Los datos del movimiento están incompletos."
                });
            }

            var usuarioId = ObtenerUsuarioID();

            if (usuarioId <= 0)
            {
                return Json(new
                {
                    ok = false,
                    mensaje = "No se pudo identificar al usuario."
                });
            }

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            await using var tx =
                (SqlTransaction)await cn.BeginTransactionAsync(
                    IsolationLevel.Serializable);

            try
            {
                await TomarCandadoCalendarioAsync(cn, tx);
                await ActivarReacomodoPlaneacionAsync(cn, tx);

                var programa = await ObtenerProgramaBaseAsync(
                    request.ProgramaProduccionID,
                    cn,
                    tx,
                    bloquear: true);

                if (programa == null)
                {
                    await tx.RollbackAsync();

                    return Json(new
                    {
                        ok = false,
                        mensaje = "No se encontró el programa."
                    });
                }

                var motivoBloqueo = await ObtenerMotivoBloqueoMovimientoAsync(
                    programa.ProgramaProduccionID,
                    cn,
                    tx,
                    bloquear: true);

                if (!string.IsNullOrWhiteSpace(motivoBloqueo))
                {
                    await tx.RollbackAsync();

                    return Json(new
                    {
                        ok = false,
                        mensaje = motivoBloqueo
                    });
                }

                var compatibles = await ObtenerMaquinasCompatiblesAsync(
                    programa,
                    cn,
                    tx);

                var maquinaDestino = compatibles.FirstOrDefault(
                    x => x.MaquinaID == request.MaquinaID);

                if (maquinaDestino == null)
                {
                    await tx.RollbackAsync();

                    var maquinasPermitidas = compatibles.Any()
                        ? string.Join(", ", compatibles.Select(x => x.Codigo))
                        : "sin máquinas compatibles configuradas";

                    return Json(new
                    {
                        ok = false,
                        mensaje =
                            "No se puede mover este programa a esa máquina. " +
                            "La máquina destino no está configurada como principal ni sustituta directa para esta parte. " +
                            "Máquinas permitidas: " + maquinasPermitidas + ".",
                        maquinasPermitidas = compatibles.Select(x => new
                        {
                            maquinaID = x.MaquinaID,
                            codigo = x.Codigo,
                            nombre = x.Nombre
                        })
                    });
                }

                                /* NSQ_CALENDARIO_MOVIMIENTO_EXACTO_V30
                 * La hora soltada por el usuario es la hora solicitada.
                 * No se convierte silenciosamente en una posicion de cola.
                 * Los programas futuros movibles se recorren DESPUES de fijar esta OF.
                 */
                var puntoCola = HoraExactaCalendario(
                    NormalizarFecha(request.Inicio));

                var horasProduccion = programa.HorasProgramadas > 0
                    ? programa.HorasProgramadas
                    : 1m;

                var anteriorCola = await ObtenerProgramaAnteriorPorPuntoAsync(
                    maquinaDestino.MaquinaID,
                    programa.ProgramaProduccionID,
                    puntoCola,
                    cn,
                    tx);

                var desdeReacomodo = puntoCola;

                var calculoCola = await CalcularPosicionExactaCalendarioAsync(
                    maquinaDestino.MaquinaID,
                    programa.ProgramaProduccionID,
                    programa.ParteID,
                    programa.MoldeID,
                    anteriorCola == null ? null : anteriorCola.ParteID,
                    anteriorCola == null ? null : anteriorCola.MoldeID,
                    puntoCola,
                    horasProduccion,
                    cn,
                    tx,
                    request.TrabajarDomingo);

                var fechaCambio = calculoCola.Cambio;
                var fechaArranque = calculoCola.Arranque;
                var fechaFin = calculoCola.Fin;
                var horasCambio = calculoCola.HorasCambio;

                var nuevaSecuencia = await ObtenerSiguienteSecuenciaAsync(
                    maquinaDestino.MaquinaID,
                    programa.ProgramaProduccionID,
                    cn,
                    tx);

                var programasQueSeRecorreran =
                    await ContarProgramasPosterioresReacomodablesAsync(
                        maquinaDestino.MaquinaID,
                        programa.ProgramaProduccionID,
                        desdeReacomodo,
                        cn,
                        tx);

                var resumen =
                    ConstruirResumenMovimientoCompacto(
                        programa,
                        maquinaDestino,
                        anteriorCola,
                        fechaCambio,
                        fechaArranque,
                        fechaFin,
                        calculoCola.MoldeLiberado,
                        horasCambio,
                        programasQueSeRecorreran);

                var anteriorOrigenCola = programa.MaquinaID.HasValue
                    ? await ObtenerProgramaAnteriorPorPuntoAsync(
                        programa.MaquinaID.Value,
                        programa.ProgramaProduccionID,
                        programa.FechaInicioProgramada,
                        cn,
                        tx)
                    : null;

                if (!request.ConfirmarMovimiento)
                {
                    await tx.RollbackAsync();

                    return Json(new
                    {
                        ok = true,
                        requiereConfirmacion = true,
                        mensaje = "Confirma el movimiento calculado.",
                        resumen,
                        maquinaID = maquinaDestino.MaquinaID,
                        maquinaCodigo = maquinaDestino.Codigo,
                        maquinaNombre = maquinaDestino.Nombre,
                        cambio = fechaCambio.ToString(
                            "yyyy-MM-ddTHH:mm:ss",
                            CultureInfo.InvariantCulture),
                        cambioTexto = fechaCambio.ToString("dd/MM/yyyy HH:mm"),
                        arranque = fechaArranque.ToString(
                            "yyyy-MM-ddTHH:mm:ss",
                            CultureInfo.InvariantCulture),
                        arranqueTexto = fechaArranque.ToString("dd/MM/yyyy HH:mm"),
                        fin = fechaFin.ToString(
                            "yyyy-MM-ddTHH:mm:ss",
                            CultureInfo.InvariantCulture),
                        finTexto = fechaFin.ToString("dd/MM/yyyy HH:mm"),
                        horasProgramadas = Math.Round(horasProduccion, 2)
                    });
                }

                await ActualizarProgramaAsync(
                    programa,
                    maquinaDestino,
                    fechaCambio,
                    fechaArranque,
                    fechaFin,
                    horasProduccion,
                    nuevaSecuencia,
                    usuarioId,
                    cn,
                    tx);

                /*
                    CORRECCIÓN IMPORTANTE:

                    Antes se buscaban los posteriores desde fechaFin.
                    Eso podía brincar programas que estaban entre desdeReacomodo y fechaFin.

                    Ahora:
                    - desdeSeleccion = desdeReacomodo  -> desde dónde buscar posteriores.
                    - cursorInicial  = fechaFin        -> desde dónde empezar a acomodarlos.
                */
                var programasReacomodados = await ReacomodarColaPosteriorAsync(
                    maquinaDestino.MaquinaID,
                    programa.ProgramaProduccionID,
                    desdeReacomodo,
                    fechaFin,
                    programa.ParteID,
                    programa.MoldeID,
                    usuarioId,
                    cn,
                    tx,
                    request.TrabajarDomingo);

                /*
                    Si cambió de máquina, también compactamos la cola de la máquina origen
                    para cerrar el hueco que deja el programa movido.
                */
                if (programa.MaquinaID.HasValue &&
                    programa.MaquinaID.Value != maquinaDestino.MaquinaID)
                {
                    var desdeSeleccionOrigen = programa.FechaInicioProgramada;

                    var cursorOrigen = anteriorOrigenCola == null
                        ? programa.FechaInicioProgramada
                        : anteriorOrigenCola.Fin;

                    programasReacomodados += await ReacomodarColaPosteriorAsync(
                        programa.MaquinaID.Value,
                        programa.ProgramaProduccionID,
                        desdeSeleccionOrigen,
                        cursorOrigen,
                        anteriorOrigenCola == null ? null : anteriorOrigenCola.ParteID,
                        anteriorOrigenCola == null ? null : anteriorOrigenCola.MoldeID,
                        usuarioId,
                        cn,
                        tx,
                        request.TrabajarDomingo);
                }

                await SincronizarDocumentosRelacionadosAsync(
                    programa,
                    maquinaDestino,
                    fechaCambio,
                    fechaArranque,
                    fechaFin,
                    horasProduccion,
                    cn,
                    tx);

                await SincronizarSecadoDesdeReprogramacionAsync(programa.ProgramaProduccionID, usuarioId, cn, tx);
                // NSQ_LHRH_CALENDARIO_HOOK_V1
                var programaParejaMovidoId =
                    await SincronizarParejaLhRhCalendarioAsync(
                        programa,
                        maquinaDestino,
                        fechaCambio,
                        fechaArranque,
                        fechaFin,
                        horasProduccion,
                        nuevaSecuencia,
                        usuarioId,
                        cn,
                        tx);

                // DDP / operadores se resuelven exclusivamente en Produccion.

                // NSQ_DDP_PRODUCCION_V1 - calendario no reasigna operadores.

                await InsertarHistorialMovimientoAsync(
                    programa,
                    maquinaDestino,
                    fechaCambio,
                    fechaArranque,
                    fechaFin,
                    horasProduccion,
                    usuarioId,
                    resumen,
                    cn,
                    tx);

                await ReordenarSecuenciasAsync(
                    programa.MaquinaID,
                    maquinaDestino.MaquinaID,
                    cn,
                    tx);

                // Temporalmente no validamos cruces globales viejos.
                // REACTIVAR_CANDADO_CRUCES:
                // await ValidarProgramaSinCrucesAsync(cn, tx);

                await DesactivarReacomodoPlaneacionAsync(cn, tx);

                await tx.CommitAsync();

                return Json(new
                {
                    ok = true,
                    requiereConfirmacion = false,
                    mensaje = programasReacomodados > 0
                        ? "Programa movido correctamente. Se reacomodaron " +
                          programasReacomodados +
                          " programa(s) posterior(es) para cerrar espacios de cola."
                        : "Programa movido correctamente a la hora seleccionada.",
                    resumen,
                    maquinaID = maquinaDestino.MaquinaID,
                    maquinaCodigo = maquinaDestino.Codigo,
                    maquinaNombre = maquinaDestino.Nombre,
                    cambioTexto = fechaCambio.ToString("dd/MM/yyyy HH:mm"),
                    arranqueTexto = fechaArranque.ToString("dd/MM/yyyy HH:mm"),
                    finTexto = fechaFin.ToString("dd/MM/yyyy HH:mm"),
                    horasProgramadas = Math.Round(horasProduccion, 2)
                });
            }
            catch (SqlException ex) when (
                ex.Number == 51001 ||
                ex.Number == 51002 ||
                ex.Number == 51003 ||
                ex.Number == 51010)
            {
                await RollbackSeguroAsync(tx);

                return Json(new
                {
                    ok = false,
                    mensaje = ex.Message
                });
            }
            catch (InvalidOperationException ex)
            {
                await RollbackSeguroAsync(tx);

                return Json(new
                {
                    ok = false,
                    mensaje = ex.Message
                });
            }
            catch (Exception ex)
            {
                await RollbackSeguroAsync(tx);

                return Json(new
                {
                    ok = false,
                    mensaje = "No fue posible mover el programa: " + ex.Message
                });
            }
            finally
            {
                await LimpiarContextoSinTransaccionAsync(cn);
            }
        }


        private async Task<List<PlaneacionCalendarioMaquinaVm>> ObtenerMaquinasCalendarioAsync(DateTime inicio, DateTime fin, SqlConnection cn)
        {
            var maquinas = new List<PlaneacionCalendarioMaquinaVm>();

            const string sqlMaquinas = @"
SELECT MaquinaID,Codigo,Nombre
FROM dbo.ERP_Maquinas
WHERE Activo=1
  AND UPPER(REPLACE(ISNULL(Codigo,N''),N' ',N'')) <> N'1200T'
  AND UPPER(REPLACE(ISNULL(Nombre,N''),N' ',N'')) NOT LIKE N'%1200T%'
ORDER BY Codigo,Nombre;";

            await using (var cmd = new SqlCommand(sqlMaquinas, cn))
            await using (var rd = await cmd.ExecuteReaderAsync())
            {
                while (await rd.ReadAsync())
                {
                    maquinas.Add(new PlaneacionCalendarioMaquinaVm
                    {
                        MaquinaID = Entero(rd, "MaquinaID"),
                        Codigo = Texto(rd, "Codigo") ?? "-",
                        Nombre = Texto(rd, "Nombre") ?? "-",
                        Bloques = new List<PlaneacionCalendarioBloqueVm>()
                    });
                }
            }

            const string sqlProgramas = @"
SELECT
    pp.ProgramaProduccionID,
    pp.MaquinaID,
    pp.MaquinaCodigo,
    pp.MaquinaNombre,
    pp.ParteID,
    pp.NumeroParte,
    pp.ReferenciaSAP,
    pp.DesignacionDescripcionSAP AS DescripcionParte,
    pp.MoldeID,
    pp.MoldeCodigo,
    pp.ReleaseDetalleID,
    pp.SolicitudProduccionID,
    pp.SolicitudProduccionDetalleID,
    pp.FechaInicioProgramada,
    ISNULL(pp.FechaFinProgramada,DATEADD(MINUTE,CAST(CEILING(ISNULL(pp.HorasProgramadas,1)*60) AS INT),pp.FechaInicioProgramada)) AS FechaFinProgramada,
    ISNULL(pp.HorasProgramadas,0) AS HorasProgramadas,
    pp.Cambio,
    pp.Arranque,
    ISNULL(pp.CantidadProgramada,0) AS CantidadProgramada,
    ISNULL(pp.CantidadProducida,0) AS CantidadProducida,
    ISNULL(pp.EstatusID,1) AS EstatusID,
    ISNULL(c.Nombre,r.ClienteNombre) AS ClienteNombre,
    ISNULL(NULLIF(r.FolioRelease,''),'Programa') AS FolioRelease,
    t.MaquinaPrincipalID,
    mp.Codigo AS MaquinaPrincipalCodigo,
    mp.Nombre AS MaquinaPrincipalNombre,
    t.MaquinaSustitutaID,
    ms.Codigo AS MaquinaSustitutaCodigo,
    ms.Nombre AS MaquinaSustitutaNombre,
    pe.EjecucionProduccionID,
    pe.EstatusID AS EstatusProduccionID,
    pe.OperadorID AS OperadorRealID,
    pe.OperadorNombre AS OperadorRealNombre,
    pe.FechaInicioReal,
    opPrincipal.PersonaID AS OperadorProgramadoID,
    opPrincipal.NombreCompleto AS OperadorProgramadoNombre,
    opAuxiliar.PersonaID AS OperadorAuxiliarProgramadoID,
    opAuxiliar.NombreCompleto AS OperadorAuxiliarProgramadoNombre,
    turno.EscalaAsignacionID,
    turno.TurnoProgramadoNombre,
    turno.TurnoProgramadoColor,
    ci.InspeccionID AS InspeccionCalidadID,
    ci.Estado AS EstadoCalidad,
    ISNULL(ci.ConfiguracionInvalidada,0) AS ConfiguracionCalidadInvalidada,
    ISNULL(ci.RequiereReliberacion,0) AS RequiereReliberacion
FROM dbo.Planeacion_ProgramaProduccion pp
LEFT JOIN dbo.Planeacion_ReleaseDetalle rd ON rd.ReleaseDetalleID=pp.ReleaseDetalleID
LEFT JOIN dbo.Planeacion_Releases r ON r.ReleaseID=rd.ReleaseID
LEFT JOIN dbo.ERP_Clientes c ON c.ClienteID=r.ClienteID
LEFT JOIN dbo.ERP_ParteDatosTecnicos t ON t.ParteID=pp.ParteID AND t.Activo=1
LEFT JOIN dbo.ERP_Maquinas mp ON mp.MaquinaID=t.MaquinaPrincipalID
LEFT JOIN dbo.ERP_Maquinas ms ON ms.MaquinaID=t.MaquinaSustitutaID
OUTER APPLY
(
    SELECT TOP (1)
        e.EjecucionProduccionID,
        e.EstatusID,
        e.OperadorID,
        e.OperadorNombre,
        e.FechaInicioReal
    FROM dbo.Produccion_Ejecucion e
    WHERE e.ProgramaProduccionID=pp.ProgramaProduccionID
      AND e.Activo=1
    ORDER BY e.EjecucionProduccionID DESC
) pe
OUTER APPLY
(
    SELECT TOP (1)
        po.PersonaID,
        LTRIM(RTRIM(ISNULL(p.Nombre,'') + ' ' + ISNULL(p.ApellidoPaterno,'') + ' ' + ISNULL(p.ApellidoMaterno,''))) AS NombreCompleto
    FROM dbo.Planeacion_ProgramaOperadores po
    LEFT JOIN dbo.Persona p ON p.PersonaID=po.PersonaID
    WHERE po.ProgramaProduccionID=pp.ProgramaProduccionID
      AND po.Activo=1
      AND UPPER(ISNULL(po.RolOperador,''))='PRINCIPAL'
    ORDER BY po.ProgramaOperadorID
) opPrincipal
OUTER APPLY
(
    SELECT TOP (1)
        po.PersonaID,
        LTRIM(RTRIM(ISNULL(p.Nombre,'') + ' ' + ISNULL(p.ApellidoPaterno,'') + ' ' + ISNULL(p.ApellidoMaterno,''))) AS NombreCompleto
    FROM dbo.Planeacion_ProgramaOperadores po
    LEFT JOIN dbo.Persona p ON p.PersonaID=po.PersonaID
    WHERE po.ProgramaProduccionID=pp.ProgramaProduccionID
      AND po.Activo=1
      AND UPPER(ISNULL(po.RolOperador,''))='AUXILIAR'
    ORDER BY po.ProgramaOperadorID
) opAuxiliar
OUTER APPLY
(
    SELECT TOP (1)
        a.AsignacionID AS EscalaAsignacionID,
        et.Nombre AS TurnoProgramadoNombre,
        et.Color AS TurnoProgramadoColor
    FROM dbo.RRHH_EscalaAsignaciones a
    INNER JOIN dbo.RRHH_EscalasPersonal esc ON esc.EscalaID=a.EscalaID AND esc.Activo=1 AND esc.Estado=N'Publicada'
    INNER JOIN dbo.RRHH_EscalaTurnos et ON et.EscalaID=a.EscalaID AND et.EscalaTurnoID=a.EscalaTurnoID
    WHERE a.Activo=1
      AND a.PersonalID=opPrincipal.PersonaID
      AND a.MaquinaID=pp.MaquinaID
      AND CAST(pp.FechaInicioProgramada AS date)>=CAST(a.FechaInicio AS date)
      AND CAST(pp.FechaInicioProgramada AS date)<=CAST(a.FechaFin AS date)
      AND
      (
           ISNULL(et.EsFlexible,0)=1
        OR et.HoraInicio IS NULL
        OR et.HoraFin IS NULL
        OR (ISNULL(et.CruzaDiaSiguiente,0)=0 AND CAST(pp.FechaInicioProgramada AS time)>=et.HoraInicio AND CAST(pp.FechaInicioProgramada AS time)<et.HoraFin)
        OR (ISNULL(et.CruzaDiaSiguiente,0)=1 AND (CAST(pp.FechaInicioProgramada AS time)>=et.HoraInicio OR CAST(pp.FechaInicioProgramada AS time)<et.HoraFin))
      )
    ORDER BY et.Orden,a.AsignacionID DESC
) turno
OUTER APPLY
(
    SELECT TOP (1)
        cins.InspeccionID,
        cins.Estado,
        cins.ConfiguracionInvalidada,
        cins.RequiereReliberacion
    FROM dbo.Calidad_Inspecciones cins
    WHERE cins.ProgramaProduccionID=pp.ProgramaProduccionID
    ORDER BY cins.InspeccionID DESC
) ci

WHERE pp.Activo = 1
  AND pp.SolicitudProduccionID IS NOT NULL
  AND pp.SolicitudProduccionDetalleID IS NOT NULL
  AND ISNULL(pp.EstatusID,1) <> @EstatusCancelado
  AND pp.MaquinaID IS NOT NULL
  AND pp.FechaInicioProgramada IS NOT NULL

  AND pp.FechaInicioProgramada < @Fin

  AND ISNULL
  (
      pp.FechaFinProgramada,
      DATEADD
      (
          MINUTE,
          CAST
          (
              CEILING(
                  ISNULL(pp.HorasProgramadas, 1) * 60
              )
              AS INT
          ),
          pp.FechaInicioProgramada
      )
  ) > @Inicio

ORDER BY pp.MaquinaID,pp.FechaInicioProgramada,pp.SecuenciaMaquina,pp.ProgramaProduccionID;";

            var bloques = new List<PlaneacionCalendarioBloqueVm>();
            var ahora = DateTime.Now;

            await using (var cmd = new SqlCommand(sqlProgramas, cn))
            {
                cmd.Parameters.Add("@Inicio", SqlDbType.DateTime).Value = inicio;
                cmd.Parameters.Add("@Fin", SqlDbType.DateTime).Value = fin;
                cmd.Parameters.Add("@EstatusCancelado", SqlDbType.Int).Value =
                    EstatusPrograma.Cancelado;

                await using var rd = await cmd.ExecuteReaderAsync();

                while (await rd.ReadAsync())
                {
                    var programaProduccionId = Entero(rd, "ProgramaProduccionID");
                    var estatusId = Entero(rd, "EstatusID");
                    var estatusProduccionId = NullableEntero(rd, "EstatusProduccionID");
                    var inicioPrograma = Fecha(rd, "FechaInicioProgramada");
                    var finPrograma = Fecha(rd, "FechaFinProgramada");
                    var cantidadProgramada = Entero(rd, "CantidadProgramada");
                    var cantidadProducida = Entero(rd, "CantidadProducida");

                    var ordinalFechaInicioReal = rd.GetOrdinal("FechaInicioReal");
                    var fechaInicioReal = rd.IsDBNull(ordinalFechaInicioReal) ? (DateTime?)null : Convert.ToDateTime(rd.GetValue(ordinalFechaInicioReal));

                    var mostrarAlertaNoInicio = false;
                    var alertaNoInicioCritica = false;
                    var minutosAtrasoInicio = 0;
                    var textoAlertaNoInicio = string.Empty;

                    var programaCerrado = estatusId == EstatusPrograma.Terminado || estatusId == EstatusPrograma.Cerrado || estatusId == EstatusPrograma.Cancelado;

                    if (!programaCerrado && inicioPrograma <= ahora && !fechaInicioReal.HasValue)
                    {
                        var sigueSinIniciar = !estatusProduccionId.HasValue || estatusProduccionId == EstatusPrograma.Programado || estatusProduccionId == EstatusPrograma.EnPreparacion;
                        if (sigueSinIniciar)
                        {
                            minutosAtrasoInicio = Math.Max(1, (int)Math.Floor((ahora - inicioPrograma).TotalMinutes));
                            mostrarAlertaNoInicio = true;
                            alertaNoInicioCritica = minutosAtrasoInicio >= 15;
                            textoAlertaNoInicio = alertaNoInicioCritica ? $"Producción no inició. Atraso: {minutosAtrasoInicio} min." : $"Producción pendiente de iniciar. Atraso: {minutosAtrasoInicio} min.";
                        }
                    }

                    var bloque = new PlaneacionCalendarioBloqueVm
                    {
                        ProgramaProduccionID = programaProduccionId,
                        SolicitudProduccionID = NullableEntero(rd, "SolicitudProduccionID"),
                        MaquinaID = NullableEntero(rd, "MaquinaID") ?? 0,
                        MaquinaCodigo = Texto(rd, "MaquinaCodigo") ?? string.Empty,
                        ClienteNombre = Texto(rd, "ClienteNombre") ?? string.Empty,
                        NumeroParte = Texto(rd, "NumeroParte") ?? string.Empty,
                        ReferenciaSAP = Texto(rd, "ReferenciaSAP") ?? string.Empty,
                        Descripcion = Texto(rd, "DescripcionParte") ?? string.Empty,
                        MoldeCodigo = Texto(rd, "MoldeCodigo") ?? string.Empty,
                        CantidadProgramada = cantidadProgramada,
                        CantidadProducida = cantidadProducida,
                        Inicio = inicioPrograma,
                        Fin = finPrograma,
                        HorasProgramadas = Decimal(rd, "HorasProgramadas"),
                        Cambio = NullableTiempo(rd, "Cambio"),
                        Arranque = NullableTiempo(rd, "Arranque"),
                        EstatusID = estatusId,
                        EstaEnLinea = estatusProduccionId == EstatusPrograma.EnProduccion,
                        DentroHorarioProgramado = ahora >= inicioPrograma && ahora < finPrograma,
                        MaquinaPrincipalID = NullableEntero(rd, "MaquinaPrincipalID"),
                        MaquinaPrincipalCodigo = Texto(rd, "MaquinaPrincipalCodigo") ?? string.Empty,
                        MaquinaPrincipalNombre = Texto(rd, "MaquinaPrincipalNombre") ?? string.Empty,
                        MaquinaSustitutaID = NullableEntero(rd, "MaquinaSustitutaID"),
                        MaquinaSustitutaCodigo = Texto(rd, "MaquinaSustitutaCodigo") ?? string.Empty,
                        MaquinaSustitutaNombre = Texto(rd, "MaquinaSustitutaNombre") ?? string.Empty,
                        EstatusProduccionID = estatusProduccionId,
                        EstatusProduccionNombre = NombreEstatusProduccion(estatusProduccionId),
                        EjecucionProduccionID = NullableEntero(rd, "EjecucionProduccionID"),
                        OperadorProgramadoID = NullableEntero(rd, "OperadorProgramadoID"),
                        OperadorProgramadoNombre = Texto(rd, "OperadorProgramadoNombre") ?? string.Empty,
                        OperadorAuxiliarProgramadoID = NullableEntero(rd, "OperadorAuxiliarProgramadoID"),
                        OperadorAuxiliarProgramadoNombre = Texto(rd, "OperadorAuxiliarProgramadoNombre") ?? string.Empty,
                        OperadorRealID = NullableEntero(rd, "OperadorRealID"),
                        OperadorRealNombre = Texto(rd, "OperadorRealNombre") ?? string.Empty,
                        TurnoProgramadoNombre = Texto(rd, "TurnoProgramadoNombre") ?? string.Empty,
                        TurnoProgramadoColor = Texto(rd, "TurnoProgramadoColor") ?? string.Empty,
                        EscalaAsignacionID = NullableEntero(rd, "EscalaAsignacionID"),
                        InspeccionCalidadID = NullableEntero(rd, "InspeccionCalidadID"),
                        EstadoCalidad = Texto(rd, "EstadoCalidad") ?? string.Empty,
                        ConfiguracionCalidadInvalidada = Booleano(rd, "ConfiguracionCalidadInvalidada"),
                        RequiereReliberacion = Booleano(rd, "RequiereReliberacion"),
                        MostrarAlertaNoInicio = mostrarAlertaNoInicio,
                        AlertaNoInicioCritica = alertaNoInicioCritica,
                        MinutosAtrasoInicio = minutosAtrasoInicio,
                        TextoAlertaNoInicio = textoAlertaNoInicio
                    };

                    bloques.Add(bloque);
                }
            }

            foreach (var maquina in maquinas)
            {
                maquina.Bloques = bloques.Where(x => x.MaquinaID == maquina.MaquinaID).OrderBy(x => x.Inicio).ThenBy(x => x.ProgramaProduccionID).ToList();
                AsignarCarriles(maquina);
            }

            return maquinas;
        }

        private static void AsignarCarriles(
            PlaneacionCalendarioMaquinaVm maquina)
        {
            var finPorCarril = new List<DateTime>();

            foreach (var bloque in maquina.Bloques.OrderBy(x => x.Inicio))
            {
                var carril = -1;

                for (var i = 0; i < finPorCarril.Count; i++)
                {
                    if (finPorCarril[i] <= bloque.Inicio)
                    {
                        carril = i;
                        break;
                    }
                }

                if (carril < 0)
                {
                    carril = finPorCarril.Count;
                    finPorCarril.Add(bloque.Fin);
                }
                else
                {
                    finPorCarril[carril] = bloque.Fin;
                }

                bloque.Carril = carril;
            }

            maquina.Carriles = Math.Max(1, finPorCarril.Count);
        }

        private static async Task<ProgramaActivoInterrupcionUrgente?> ObtenerProduccionActivaParaInterrupcionUrgenteAsync(int maquinaId, SqlConnection cn, SqlTransaction? tx = null, bool bloquear = false)
        {
            var lockSql = bloquear ? " WITH(UPDLOCK,HOLDLOCK)" : string.Empty;
            var sql = $@"
SELECT TOP(1)
    pp.ProgramaProduccionID,
    pp.SolicitudProduccionID,
    pp.SolicitudProduccionDetalleID,
    pp.ParteID,
    pp.NumeroParte,
    pp.ReferenciaSAP,
    pp.DesignacionDescripcionSAP AS DescripcionParte,
    pp.MoldeID,
    pp.MoldeCodigo,
    pp.MaquinaID,
    pp.MaquinaCodigo,
    pp.MaquinaNombre,
    pp.FechaInicioProgramada,
    ISNULL(pp.FechaFinProgramada,DATEADD(MINUTE,CAST(CEILING(ISNULL(pp.HorasProgramadas,1)*60) AS INT),pp.FechaInicioProgramada)) AS FechaFinProgramada,
    ISNULL(pp.HorasProgramadas,0) AS HorasProgramadas,
    e.EjecucionProduccionID,
    e.EstatusID AS EstatusProduccionID,
    e.FechaInicioReal,
    e.OperadorID,
    e.OperadorNombre
FROM dbo.Produccion_Ejecucion e{lockSql}
INNER JOIN dbo.Planeacion_ProgramaProduccion pp{lockSql}
    ON pp.ProgramaProduccionID=e.ProgramaProduccionID
   AND pp.Activo=1
WHERE e.Activo=1
  AND e.MaquinaID=@MaquinaID
  AND e.FechaLiberacionMaquina IS NULL
  AND e.EstatusID IN(2,3,4)
ORDER BY CASE e.EstatusID WHEN 3 THEN 0 WHEN 4 THEN 1 ELSE 2 END,e.EjecucionProduccionID DESC;";
            await using var cmd = tx == null ? new SqlCommand(sql, cn) : new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = maquinaId;
            await using var rd = await cmd.ExecuteReaderAsync();
            if (!await rd.ReadAsync()) return null;
            return new ProgramaActivoInterrupcionUrgente
            {
                ProgramaProduccionID = Convert.ToInt32(rd["ProgramaProduccionID"]),
                EjecucionProduccionID = Convert.ToInt32(rd["EjecucionProduccionID"]),
                SolicitudProduccionID = rd["SolicitudProduccionID"] == DBNull.Value ? null : Convert.ToInt32(rd["SolicitudProduccionID"]),
                SolicitudProduccionDetalleID = rd["SolicitudProduccionDetalleID"] == DBNull.Value ? null : Convert.ToInt32(rd["SolicitudProduccionDetalleID"]),
                ParteID = rd["ParteID"] == DBNull.Value ? null : Convert.ToInt32(rd["ParteID"]),
                NumeroParte = rd["NumeroParte"]?.ToString()?.Trim(),
                ReferenciaSAP = rd["ReferenciaSAP"]?.ToString()?.Trim(),
                DescripcionParte = rd["DescripcionParte"]?.ToString()?.Trim(),
                MoldeID = rd["MoldeID"] == DBNull.Value ? null : Convert.ToInt32(rd["MoldeID"]),
                MoldeCodigo = rd["MoldeCodigo"]?.ToString()?.Trim(),
                MaquinaID = Convert.ToInt32(rd["MaquinaID"]),
                MaquinaCodigo = rd["MaquinaCodigo"]?.ToString()?.Trim(),
                MaquinaNombre = rd["MaquinaNombre"]?.ToString()?.Trim(),
                FechaInicioProgramada = Convert.ToDateTime(rd["FechaInicioProgramada"]),
                FechaFinProgramada = Convert.ToDateTime(rd["FechaFinProgramada"]),
                HorasProgramadas = Convert.ToDecimal(rd["HorasProgramadas"]),
                EstatusProduccionID = Convert.ToInt32(rd["EstatusProduccionID"]),
                FechaInicioReal = rd["FechaInicioReal"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaInicioReal"]),
                OperadorID = rd["OperadorID"] == DBNull.Value ? null : Convert.ToInt32(rd["OperadorID"]),
                OperadorNombre = rd["OperadorNombre"]?.ToString()?.Trim()
            };
        }

        private static async Task<ProgramaActivoInterrupcionUrgente?> ObtenerProduccionActivaProgramaInterrupcionUrgenteAsync(int programaProduccionId, int maquinaId, SqlConnection cn, SqlTransaction tx, bool bloquear = true)
        {
            var lockSql = bloquear ? " WITH(UPDLOCK,HOLDLOCK)" : string.Empty;
            var sql = $@"
SELECT TOP(1)
    pp.ProgramaProduccionID,
    pp.SolicitudProduccionID,
    pp.SolicitudProduccionDetalleID,
    pp.ParteID,
    pp.NumeroParte,
    pp.ReferenciaSAP,
    pp.DesignacionDescripcionSAP AS DescripcionParte,
    pp.MoldeID,
    pp.MoldeCodigo,
    pp.MaquinaID,
    pp.MaquinaCodigo,
    pp.MaquinaNombre,
    pp.FechaInicioProgramada,
    ISNULL(pp.FechaFinProgramada,DATEADD(MINUTE,CAST(CEILING(ISNULL(pp.HorasProgramadas,1)*60) AS INT),pp.FechaInicioProgramada)) AS FechaFinProgramada,
    ISNULL(pp.HorasProgramadas,0) AS HorasProgramadas,
    e.EjecucionProduccionID,
    e.EstatusID AS EstatusProduccionID,
    e.FechaInicioReal,
    e.OperadorID,
    e.OperadorNombre
FROM dbo.Produccion_Ejecucion e{lockSql}
INNER JOIN dbo.Planeacion_ProgramaProduccion pp{lockSql}
    ON pp.ProgramaProduccionID=e.ProgramaProduccionID
   AND pp.Activo=1
WHERE e.Activo=1
  AND e.ProgramaProduccionID=@ProgramaProduccionID
  AND e.MaquinaID=@MaquinaID
  AND e.FechaLiberacionMaquina IS NULL
ORDER BY e.EjecucionProduccionID DESC;";
            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaProduccionId;
            cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = maquinaId;
            await using var rd = await cmd.ExecuteReaderAsync();
            if (!await rd.ReadAsync()) return null;
            return new ProgramaActivoInterrupcionUrgente
            {
                ProgramaProduccionID = Convert.ToInt32(rd["ProgramaProduccionID"]),
                EjecucionProduccionID = Convert.ToInt32(rd["EjecucionProduccionID"]),
                SolicitudProduccionID = rd["SolicitudProduccionID"] == DBNull.Value ? null : Convert.ToInt32(rd["SolicitudProduccionID"]),
                SolicitudProduccionDetalleID = rd["SolicitudProduccionDetalleID"] == DBNull.Value ? null : Convert.ToInt32(rd["SolicitudProduccionDetalleID"]),
                ParteID = rd["ParteID"] == DBNull.Value ? null : Convert.ToInt32(rd["ParteID"]),
                NumeroParte = rd["NumeroParte"]?.ToString()?.Trim(),
                ReferenciaSAP = rd["ReferenciaSAP"]?.ToString()?.Trim(),
                DescripcionParte = rd["DescripcionParte"]?.ToString()?.Trim(),
                MoldeID = rd["MoldeID"] == DBNull.Value ? null : Convert.ToInt32(rd["MoldeID"]),
                MoldeCodigo = rd["MoldeCodigo"]?.ToString()?.Trim(),
                MaquinaID = Convert.ToInt32(rd["MaquinaID"]),
                MaquinaCodigo = rd["MaquinaCodigo"]?.ToString()?.Trim(),
                MaquinaNombre = rd["MaquinaNombre"]?.ToString()?.Trim(),
                FechaInicioProgramada = Convert.ToDateTime(rd["FechaInicioProgramada"]),
                FechaFinProgramada = Convert.ToDateTime(rd["FechaFinProgramada"]),
                HorasProgramadas = Convert.ToDecimal(rd["HorasProgramadas"]),
                EstatusProduccionID = Convert.ToInt32(rd["EstatusProduccionID"]),
                FechaInicioReal = rd["FechaInicioReal"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaInicioReal"]),
                OperadorID = rd["OperadorID"] == DBNull.Value ? null : Convert.ToInt32(rd["OperadorID"]),
                OperadorNombre = rd["OperadorNombre"]?.ToString()?.Trim()
            };
        }
        private static async Task<int> ContarProgramasImpactadosPorInterrupcionUrgenteAsync(int maquinaId, int programaUrgenteId, int programaInterrumpidoId, DateTime desde, SqlConnection cn)
        {
            const string sql = @"
SELECT COUNT(1)
FROM dbo.Planeacion_ProgramaProduccion pp
WHERE pp.Activo=1
  AND pp.MaquinaID=@MaquinaID
  AND pp.ProgramaProduccionID<>@ProgramaUrgenteID
  AND pp.ProgramaProduccionID<>@ProgramaInterrumpidoID
  AND pp.FechaInicioProgramada IS NOT NULL
  AND pp.FechaInicioProgramada>=@Desde
  AND ISNULL(pp.EstatusID,1)=1
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.Produccion_Ejecucion e
      WHERE e.ProgramaProduccionID=pp.ProgramaProduccionID
        AND e.Activo=1
  )
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.Calidad_Inspecciones ci
      WHERE ci.ProgramaProduccionID=pp.ProgramaProduccionID
  );";

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = maquinaId;
            cmd.Parameters.Add("@ProgramaUrgenteID", SqlDbType.Int).Value = programaUrgenteId;
            cmd.Parameters.Add("@ProgramaInterrumpidoID", SqlDbType.Int).Value = programaInterrumpidoId;
            cmd.Parameters.Add("@Desde", SqlDbType.DateTime).Value = desde;
            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        private static async Task<int?> ObtenerProgramaParejaLhRhAsync(int programaProduccionId, SqlConnection cn, SqlTransaction? tx = null)
        {
            const string sql = @"
SELECT TOP(1)
    pareja.ProgramaProduccionID
FROM dbo.Planeacion_ProgramaProduccion origen
CROSS APPLY
(
    SELECT CHARINDEX(N'NSQ_LHRH_PAIR:',ISNULL(origen.Observaciones,N'')) AS PosGrupo
) pos
CROSS APPLY
(
    SELECT TRY_CONVERT
    (
        INT,
        LEFT
        (
            SUBSTRING(origen.Observaciones,pos.PosGrupo+LEN(N'NSQ_LHRH_PAIR:'),50),
            CHARINDEX(N';',SUBSTRING(origen.Observaciones,pos.PosGrupo+LEN(N'NSQ_LHRH_PAIR:'),50)+N';')-1
        )
    ) AS Grupo
) grp
INNER JOIN dbo.Planeacion_ProgramaProduccion pareja
    ON pareja.Activo=1
   AND pareja.ProgramaProduccionID<>origen.ProgramaProduccionID
   AND grp.Grupo IS NOT NULL
   AND pareja.Observaciones LIKE N'%NSQ_LHRH_PAIR:'+CONVERT(NVARCHAR(20),grp.Grupo)+N';%'
WHERE origen.ProgramaProduccionID=@ProgramaProduccionID
  AND origen.Activo=1
  AND pos.PosGrupo>0
ORDER BY pareja.ProgramaProduccionID;";

            await using var cmd = tx == null ? new SqlCommand(sql, cn) : new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaProduccionId;
            var result = await cmd.ExecuteScalarAsync();
            return result == null || result == DBNull.Value ? null : Convert.ToInt32(result);
        }

        private static async Task<bool> ExisteParoAbiertoEjecucionAsync(int ejecucionProduccionId, SqlConnection cn, SqlTransaction tx)
        {
            const string sql = @"
SELECT CASE WHEN EXISTS
(
    SELECT 1
    FROM dbo.Produccion_Paros WITH(UPDLOCK,HOLDLOCK)
    WHERE EjecucionProduccionID=@EjecucionProduccionID
      AND Activo=1
      AND FechaFinParo IS NULL
) THEN 1 ELSE 0 END;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
            return Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 1;
        }

        private static async Task<bool> ExisteInterrupcionUrgenteActivaParaProgramaAsync(int programaUrgenteId, SqlConnection cn, SqlTransaction tx)
        {
            const string sql = @"
SELECT CASE WHEN EXISTS
(
    SELECT 1
    FROM dbo.Produccion_Paros WITH(UPDLOCK,HOLDLOCK)
    WHERE Activo=1
      AND FechaFinParo IS NULL
      AND ISNULL(EsInterrupcionUrgente,0)=1
      AND ProgramaUrgenteID=@ProgramaUrgenteID
) THEN 1 ELSE 0 END;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@ProgramaUrgenteID", SqlDbType.Int).Value = programaUrgenteId;
            return Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 1;
        }

        private static async Task<DateTime?> ObtenerFinCruceMoldeInterrupcionUrgenteAsync(int moldeId, int programaUrgenteId, int? programaUrgenteParejaId, int programaInterrumpidoId, int? programaInterrumpidoParejaId, DateTime inicio, DateTime fin, SqlConnection cn, SqlTransaction tx)
        {
            const string sql = @"
SELECT TOP(1)
    ISNULL
    (
        pp.FechaFinProgramada,
        DATEADD(MINUTE,CAST(CEILING(ISNULL(pp.HorasProgramadas,1)*60) AS INT),pp.FechaInicioProgramada)
    ) AS FechaFinProgramada
FROM dbo.Planeacion_ProgramaProduccion pp WITH(UPDLOCK,HOLDLOCK)
WHERE pp.Activo=1
  AND pp.MoldeID=@MoldeID
  AND pp.ProgramaProduccionID<>@ProgramaUrgenteID
  AND (@ProgramaUrgenteParejaID IS NULL OR pp.ProgramaProduccionID<>@ProgramaUrgenteParejaID)
  AND pp.ProgramaProduccionID<>@ProgramaInterrumpidoID
  AND (@ProgramaInterrumpidoParejaID IS NULL OR pp.ProgramaProduccionID<>@ProgramaInterrumpidoParejaID)
  AND pp.FechaInicioProgramada IS NOT NULL
  AND ISNULL(pp.EstatusID,1) NOT IN(5,6,9,99)
  AND pp.FechaInicioProgramada<@Fin
  AND ISNULL
  (
      pp.FechaFinProgramada,
      DATEADD(MINUTE,CAST(CEILING(ISNULL(pp.HorasProgramadas,1)*60) AS INT),pp.FechaInicioProgramada)
  )>@Inicio
ORDER BY ISNULL
(
    pp.FechaFinProgramada,
    DATEADD(MINUTE,CAST(CEILING(ISNULL(pp.HorasProgramadas,1)*60) AS INT),pp.FechaInicioProgramada)
) DESC;";
            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@MoldeID", SqlDbType.Int).Value = moldeId;
            cmd.Parameters.Add("@ProgramaUrgenteID", SqlDbType.Int).Value = programaUrgenteId;
            cmd.Parameters.Add("@ProgramaUrgenteParejaID", SqlDbType.Int).Value = (object?)programaUrgenteParejaId ?? DBNull.Value;
            cmd.Parameters.Add("@ProgramaInterrumpidoID", SqlDbType.Int).Value = programaInterrumpidoId;
            cmd.Parameters.Add("@ProgramaInterrumpidoParejaID", SqlDbType.Int).Value = (object?)programaInterrumpidoParejaId ?? DBNull.Value;
            cmd.Parameters.Add("@Inicio", SqlDbType.DateTime).Value = inicio;
            cmd.Parameters.Add("@Fin", SqlDbType.DateTime).Value = fin;
            var result = await cmd.ExecuteScalarAsync();
            return result == null || result == DBNull.Value ? null : Convert.ToDateTime(result);
        }
        private static async Task<int> CrearParoInterrupcionUrgenteAsync(ProgramaActivoInterrupcionUrgente programaActual, int programaUrgenteId, DateTime fechaInicio, string motivo, bool esParoLhRh, Guid? grupoParoLhRh, int usuarioId, SqlConnection cn, SqlTransaction tx)
        {
            const string sql = @"
INSERT INTO dbo.Produccion_Paros
(
    EjecucionProduccionID,
    ProgramaProduccionID,
    SolicitudProduccionID,
    MaquinaID,
    OperadorID,
    FechaInicioParo,
    MotivoParoID,
    MotivoParoTexto,
    Descripcion,
    EsParoLhRh,
    GrupoParoLhRh,
    EsInterrupcionUrgente,
    ProgramaUrgenteID,
    UsuarioCreacionID,
    FechaCreacion,
    Activo
)
OUTPUT INSERTED.ParoID
VALUES
(
    @EjecucionProduccionID,
    @ProgramaProduccionID,
    @SolicitudProduccionID,
    @MaquinaID,
    @OperadorID,
    @FechaInicioParo,
    NULL,
    N'Interrupción urgente de Planeación',
    @Descripcion,
    @EsParoLhRh,
    @GrupoParoLhRh,
    1,
    @ProgramaUrgenteID,
    @UsuarioID,
    GETDATE(),
    1
);";
            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = programaActual.EjecucionProduccionID;
            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaActual.ProgramaProduccionID;
            cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = (object?)programaActual.SolicitudProduccionID ?? DBNull.Value;
            cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = programaActual.MaquinaID;
            cmd.Parameters.Add("@OperadorID", SqlDbType.Int).Value = (object?)programaActual.OperadorID ?? DBNull.Value;
            cmd.Parameters.Add("@FechaInicioParo", SqlDbType.DateTime).Value = fechaInicio;
            cmd.Parameters.Add("@Descripcion", SqlDbType.NVarChar, 500).Value = motivo;
            cmd.Parameters.Add("@EsParoLhRh", SqlDbType.Bit).Value = esParoLhRh;
            cmd.Parameters.Add("@GrupoParoLhRh", SqlDbType.UniqueIdentifier).Value = (object?)grupoParoLhRh ?? DBNull.Value;
            cmd.Parameters.Add("@ProgramaUrgenteID", SqlDbType.Int).Value = programaUrgenteId;
            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
            var result = await cmd.ExecuteScalarAsync();
            if (result == null || result == DBNull.Value) throw new InvalidOperationException("No fue posible registrar el paro provocado por la interrupción urgente.");
            return Convert.ToInt32(result);
        }
        private static async Task PausarProduccionPorInterrupcionUrgenteAsync(ProgramaActivoInterrupcionUrgente programaActual, int usuarioId, SqlConnection cn, SqlTransaction tx)
        {
            const string sql = @"
UPDATE dbo.Produccion_Ejecucion
SET
    EstatusID=@Pausado,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=GETDATE()
WHERE EjecucionProduccionID=@EjecucionProduccionID
  AND ProgramaProduccionID=@ProgramaProduccionID
  AND Activo=1
  AND EstatusID=@EnProduccion;

IF @@ROWCOUNT<>1
    THROW 51620,'La ejecución cambió de estado antes de confirmar la interrupción urgente.',1;

UPDATE dbo.Planeacion_ProgramaProduccion
SET
    EstatusID=@Pausado,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=GETDATE()
WHERE ProgramaProduccionID=@ProgramaProduccionID
  AND Activo=1;

IF @@ROWCOUNT<>1
    THROW 51621,'No fue posible pausar el programa actualmente producido.',1;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = programaActual.EjecucionProduccionID;
            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaActual.ProgramaProduccionID;
            cmd.Parameters.Add("@EnProduccion", SqlDbType.Int).Value = EstatusPrograma.EnProduccion;
            cmd.Parameters.Add("@Pausado", SqlDbType.Int).Value = EstatusPrograma.Pausado;
            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task ActualizarProgramaUrgenteParaInterrupcionAsync(ProgramaBase programa, MaquinaCompatible maquinaDestino, DateTime fechaCambio, DateTime fechaArranque, DateTime fechaFin, decimal horasProduccion, int secuencia, int usuarioId, SqlConnection cn, SqlTransaction tx)
        {
            const string sql = @"
UPDATE dbo.Planeacion_ProgramaProduccion
SET
    MaquinaID=@MaquinaID,
    MaquinaCodigo=@MaquinaCodigo,
    MaquinaNombre=@MaquinaNombre,
    FechaInicioProgramada=@FechaCambio,
    FechaFinProgramada=@FechaFin,
    Cambio=@Cambio,
    Arranque=@Arranque,
    HorasProgramadas=@HorasProgramadas,
    SecuenciaMaquina=@SecuenciaMaquina,
    EstatusID=@EnPreparacion,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=GETDATE()
WHERE ProgramaProduccionID=@ProgramaProduccionID
  AND Activo=1
  AND ISNULL(EstatusID,1)=@Programado
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.Produccion_Ejecucion e
      WHERE e.ProgramaProduccionID=@ProgramaProduccionID
        AND e.Activo=1
  );

IF @@ROWCOUNT<>1
    THROW 51622,'La OF urgente cambió de estado y ya no está disponible para iniciar la interrupción.',1;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = maquinaDestino.MaquinaID;
            cmd.Parameters.Add("@MaquinaCodigo", SqlDbType.NVarChar, 100).Value = maquinaDestino.Codigo;
            cmd.Parameters.Add("@MaquinaNombre", SqlDbType.NVarChar, 200).Value = maquinaDestino.Nombre;
            cmd.Parameters.Add("@FechaCambio", SqlDbType.DateTime).Value = fechaCambio;
            cmd.Parameters.Add("@FechaFin", SqlDbType.DateTime).Value = fechaFin;
            cmd.Parameters.Add("@Cambio", SqlDbType.Time).Value = fechaCambio.TimeOfDay;
            cmd.Parameters.Add("@Arranque", SqlDbType.Time).Value = fechaArranque.TimeOfDay;

            var horas = cmd.Parameters.Add("@HorasProgramadas", SqlDbType.Decimal);
            horas.Precision = 18;
            horas.Scale = 4;
            horas.Value = horasProduccion;

            cmd.Parameters.Add("@SecuenciaMaquina", SqlDbType.Int).Value = secuencia;
            cmd.Parameters.Add("@Programado", SqlDbType.Int).Value = EstatusPrograma.Programado;
            cmd.Parameters.Add("@EnPreparacion", SqlDbType.Int).Value = EstatusPrograma.EnPreparacion;
            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programa.ProgramaProduccionID;
            await cmd.ExecuteNonQueryAsync();
        }
        private static async Task InsertarHistorialInterrupcionUrgenteAsync(ProgramaBase programaUrgente, ProgramaActivoInterrupcionUrgente programaInterrumpido, MaquinaCompatible maquinaDestino, DateTime fechaCambio, DateTime fechaArranque, DateTime fechaFin, decimal horasProduccion, int usuarioId, string motivo, SqlConnection cn, SqlTransaction tx)
        {
            const string sql = @"
IF OBJECT_ID(N'dbo.Planeacion_ProgramaReprogramacionHistorial',N'U') IS NOT NULL
BEGIN
    INSERT INTO dbo.Planeacion_ProgramaReprogramacionHistorial
    (
        ProgramaProduccionID,
        MaquinaAnteriorID,
        MaquinaNuevaID,
        InicioAnterior,
        InicioNuevo,
        FinAnterior,
        FinNuevo,
        HorasAnteriores,
        HorasNuevas,
        CambioAnterior,
        CambioNuevo,
        ArranqueAnterior,
        ArranqueNuevo,
        ReleaseDetalleID,
        SolicitudProduccionID,
        SolicitudProduccionDetalleID,
        TipoMovimiento,
        EsMovimientoAutomatico,
        ProgramaOrigenMovimientoID,
        UsuarioID,
        FechaCambio,
        Motivo
    )
    VALUES
    (
        @ProgramaProduccionID,
        @MaquinaAnteriorID,
        @MaquinaNuevaID,
        @InicioAnterior,
        @InicioNuevo,
        @FinAnterior,
        @FinNuevo,
        @HorasAnteriores,
        @HorasNuevas,
        @CambioAnterior,
        @CambioNuevo,
        @ArranqueAnterior,
        @ArranqueNuevo,
        @ReleaseDetalleID,
        @SolicitudProduccionID,
        @SolicitudProduccionDetalleID,
        N'INTERRUPCION_URGENTE',
        0,
        @ProgramaOrigenMovimientoID,
        @UsuarioID,
        GETDATE(),
        @Motivo
    );
END;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaUrgente.ProgramaProduccionID;
            cmd.Parameters.Add("@MaquinaAnteriorID", SqlDbType.Int).Value = (object?)programaUrgente.MaquinaID ?? DBNull.Value;
            cmd.Parameters.Add("@MaquinaNuevaID", SqlDbType.Int).Value = maquinaDestino.MaquinaID;
            cmd.Parameters.Add("@InicioAnterior", SqlDbType.DateTime).Value = programaUrgente.FechaInicioProgramada;
            cmd.Parameters.Add("@InicioNuevo", SqlDbType.DateTime).Value = fechaCambio;
            cmd.Parameters.Add("@FinAnterior", SqlDbType.DateTime).Value = programaUrgente.FechaFinProgramada;
            cmd.Parameters.Add("@FinNuevo", SqlDbType.DateTime).Value = fechaFin;

            var horasAnteriores = cmd.Parameters.Add("@HorasAnteriores", SqlDbType.Decimal);
            horasAnteriores.Precision = 18;
            horasAnteriores.Scale = 4;
            horasAnteriores.Value = programaUrgente.HorasProgramadas;

            var horasNuevas = cmd.Parameters.Add("@HorasNuevas", SqlDbType.Decimal);
            horasNuevas.Precision = 18;
            horasNuevas.Scale = 4;
            horasNuevas.Value = horasProduccion;

            cmd.Parameters.Add("@CambioAnterior", SqlDbType.Time).Value = (object?)programaUrgente.Cambio ?? DBNull.Value;
            cmd.Parameters.Add("@CambioNuevo", SqlDbType.Time).Value = fechaCambio.TimeOfDay;
            cmd.Parameters.Add("@ArranqueAnterior", SqlDbType.Time).Value = (object?)programaUrgente.Arranque ?? DBNull.Value;
            cmd.Parameters.Add("@ArranqueNuevo", SqlDbType.Time).Value = fechaArranque.TimeOfDay;
            cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value = (object?)programaUrgente.ReleaseDetalleID ?? DBNull.Value;
            cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = (object?)programaUrgente.SolicitudProduccionID ?? DBNull.Value;
            cmd.Parameters.Add("@SolicitudProduccionDetalleID", SqlDbType.Int).Value = (object?)programaUrgente.SolicitudProduccionDetalleID ?? DBNull.Value;
            cmd.Parameters.Add("@ProgramaOrigenMovimientoID", SqlDbType.Int).Value = programaInterrumpido.ProgramaProduccionID;
            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;

            var textoMotivo = $"Interrupción urgente solicitada desde Planeación. OF interrumpida: programa {programaInterrumpido.ProgramaProduccionID}. Motivo: {motivo}";
            if (textoMotivo.Length > 500) textoMotivo = textoMotivo[..500];
            cmd.Parameters.Add("@Motivo", SqlDbType.NVarChar, 500).Value = textoMotivo;
            await cmd.ExecuteNonQueryAsync();
        }
        private static string ObtenerTextoParte(ProgramaBase programa)
        {
            if (!string.IsNullOrWhiteSpace(programa.ReferenciaSAP))
                return programa.ReferenciaSAP.Trim();
            if (!string.IsNullOrWhiteSpace(programa.NumeroParte))
                return programa.NumeroParte.Trim();
            return "Sin parte";
        }

        private async Task<ProgramaBase?> ObtenerProgramaBaseAsync(
            int programaProduccionId,
            SqlConnection cn,
            SqlTransaction? tx,
            bool bloquear)
        {
            var lockSql = bloquear
                ? " WITH (UPDLOCK, HOLDLOCK)"
                : string.Empty;

            var sql = $@"
SELECT
    pp.ProgramaProduccionID,
    pp.MaquinaID,
    pp.MaquinaCodigo,
    pp.MaquinaNombre,

    pp.ParteID,
    pp.NumeroParte,
    pp.ReferenciaSAP,
    pp.DesignacionDescripcionSAP AS DescripcionParte,

    pp.MoldeID,
    pp.MoldeCodigo,

    pp.ReleaseDetalleID,
    pp.SolicitudProduccionID,
    pp.SolicitudProduccionDetalleID,

    pp.FechaInicioProgramada,
    ISNULL
    (
        pp.FechaFinProgramada,
        DATEADD
        (
            MINUTE,
            CAST(CEILING(ISNULL(pp.HorasProgramadas, 1) * 60) AS INT),
            pp.FechaInicioProgramada
        )
    ) AS FechaFinProgramada,

    ISNULL(pp.HorasProgramadas, 0) AS HorasProgramadas,
    pp.Cambio,
    pp.Arranque,
    ISNULL(pp.EstatusID, 1) AS EstatusID,

    t.MaquinaPrincipalID,
    t.MaquinaSustitutaID

FROM dbo.Planeacion_ProgramaProduccion pp{lockSql}

LEFT JOIN dbo.ERP_ParteDatosTecnicos t
    ON t.ParteID = pp.ParteID
   AND t.Activo = 1

WHERE pp.ProgramaProduccionID = @ProgramaProduccionID
  AND pp.Activo = 1;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add(
                "@ProgramaProduccionID",
                SqlDbType.Int).Value = programaProduccionId;

            await using var rd = await cmd.ExecuteReaderAsync();

            if (!await rd.ReadAsync())
                return null;

            return new ProgramaBase
            {
                ProgramaProduccionID =
                    Entero(rd, "ProgramaProduccionID"),

                MaquinaID =
                    NullableEntero(rd, "MaquinaID"),

                MaquinaCodigo =
                    Texto(rd, "MaquinaCodigo"),

                MaquinaNombre =
                    Texto(rd, "MaquinaNombre"),

                ParteID =
                    NullableEntero(rd, "ParteID"),

                NumeroParte =
                    Texto(rd, "NumeroParte"),

                ReferenciaSAP =
                    Texto(rd, "ReferenciaSAP"),

                DescripcionParte =
                    Texto(rd, "DescripcionParte"),

                MoldeID =
                    NullableEntero(rd, "MoldeID"),

                MoldeCodigo =
                    Texto(rd, "MoldeCodigo"),

                ReleaseDetalleID =
                    NullableEntero(rd, "ReleaseDetalleID"),

                SolicitudProduccionID =
                    NullableEntero(rd, "SolicitudProduccionID"),

                SolicitudProduccionDetalleID =
                    NullableEntero(rd, "SolicitudProduccionDetalleID"),

                FechaInicioProgramada =
                    Fecha(rd, "FechaInicioProgramada"),

                FechaFinProgramada =
                    Fecha(rd, "FechaFinProgramada"),

                HorasProgramadas =
                    Decimal(rd, "HorasProgramadas"),

                Cambio =
                    NullableTiempo(rd, "Cambio"),

                Arranque =
                    NullableTiempo(rd, "Arranque"),

                EstatusID =
                    Entero(rd, "EstatusID"),

                MaquinaPrincipalID =
                    NullableEntero(rd, "MaquinaPrincipalID"),

                MaquinaSustitutaID =
                    NullableEntero(rd, "MaquinaSustitutaID")
            };
        }

        private async Task<List<MaquinaCompatible>> ObtenerMaquinasCompatiblesAsync(
    ProgramaBase programa,
    SqlConnection cn,
    SqlTransaction? tx)
        {
            var lista = new List<MaquinaCompatible>();

            const string sql = @"
;WITH CompatiblesRaw AS
(
    -- Máquina actual del programa
    SELECT
        @MaquinaActualID AS MaquinaID,
        0 AS Prioridad
    WHERE @MaquinaActualID IS NOT NULL

    UNION ALL

    -- Máquina principal de datos técnicos
    SELECT
        @MaquinaPrincipalID AS MaquinaID,
        1 AS Prioridad
    WHERE @MaquinaPrincipalID IS NOT NULL

    UNION ALL

    -- Sustituta directa vieja de datos técnicos
    SELECT
        @MaquinaSustitutaID AS MaquinaID,
        2 AS Prioridad
    WHERE @MaquinaSustitutaID IS NOT NULL

    UNION ALL

    -- Relación directa: principal -> sustituta
    SELECT
        ms.MaquinaSustitutaID AS MaquinaID,
        ISNULL(ms.Prioridad, 999) + 10 AS Prioridad
    FROM dbo.ERP_MaquinasSustitutas ms
    WHERE ms.Activo = 1
      AND @MaquinaPrincipalID IS NOT NULL
      AND ms.MaquinaPrincipalID = @MaquinaPrincipalID

    UNION ALL

    -- Relación inversa directa: sustituta -> principal
    SELECT
        ms.MaquinaPrincipalID AS MaquinaID,
        ISNULL(ms.Prioridad, 999) + 20 AS Prioridad
    FROM dbo.ERP_MaquinasSustitutas ms
    WHERE ms.Activo = 1
      AND @MaquinaPrincipalID IS NOT NULL
      AND ms.MaquinaSustitutaID = @MaquinaPrincipalID
),
Compatibles AS
(
    SELECT
        MaquinaID,
        MIN(Prioridad) AS Prioridad
    FROM CompatiblesRaw
    WHERE MaquinaID IS NOT NULL
    GROUP BY MaquinaID
)
SELECT
    m.MaquinaID,
    ISNULL(m.Codigo, CONVERT(NVARCHAR(30), m.MaquinaID)) AS Codigo,
    ISNULL(m.Nombre, N'') AS Nombre,
    c.Prioridad
FROM Compatibles c
INNER JOIN dbo.ERP_Maquinas m
    ON m.MaquinaID = c.MaquinaID
   AND m.Activo = 1
ORDER BY
    c.Prioridad,
    m.Codigo,
    m.Nombre;";

            await using var cmd = tx == null
                ? new SqlCommand(sql, cn)
                : new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@MaquinaActualID", SqlDbType.Int).Value =
                programa.MaquinaID.HasValue
                    ? programa.MaquinaID.Value
                    : DBNull.Value;

            cmd.Parameters.Add("@MaquinaPrincipalID", SqlDbType.Int).Value =
                programa.MaquinaPrincipalID.HasValue
                    ? programa.MaquinaPrincipalID.Value
                    : DBNull.Value;

            cmd.Parameters.Add("@MaquinaSustitutaID", SqlDbType.Int).Value =
                programa.MaquinaSustitutaID.HasValue
                    ? programa.MaquinaSustitutaID.Value
                    : DBNull.Value;

            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                lista.Add(new MaquinaCompatible
                {
                    MaquinaID = Convert.ToInt32(rd["MaquinaID"]),
                    Codigo = rd["Codigo"] as string ?? "",
                    Nombre = rd["Nombre"] as string ?? ""
                });
            }

            return lista;
        }

        private async Task<DateTime?> ObtenerFinColaMaquinaAsync(
            int maquinaId,
            int programaExcluirId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
SELECT MAX
(
    ISNULL
    (
        FechaFinProgramada,
        DATEADD
        (
            MINUTE,
            CAST(CEILING(ISNULL(HorasProgramadas, 1) * 60) AS INT),
            FechaInicioProgramada
        )
    )
)
FROM dbo.Planeacion_ProgramaProduccion WITH (UPDLOCK, HOLDLOCK)
WHERE MaquinaID = @MaquinaID
  AND ProgramaProduccionID <> @ProgramaProduccionID
  AND Activo = 1
  AND ISNULL(EstatusID, 1) NOT IN (5, 6, 9, 99)
  AND FechaInicioProgramada IS NOT NULL;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = maquinaId;
            cmd.Parameters.Add(
                "@ProgramaProduccionID",
                SqlDbType.Int).Value = programaExcluirId;

            var result = await cmd.ExecuteScalarAsync();

            return result == null || result == DBNull.Value
                ? null
                : Convert.ToDateTime(result);
        }

        private async Task<DateTime?> ObtenerFinOcupacionMoldeAsync(
            int moldeId,
            int programaExcluirId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
SELECT MAX
(
    ISNULL
    (
        FechaFinProgramada,
        DATEADD
        (
            MINUTE,
            CAST(CEILING(ISNULL(HorasProgramadas, 1) * 60) AS INT),
            FechaInicioProgramada
        )
    )
)
FROM dbo.Planeacion_ProgramaProduccion WITH (UPDLOCK, HOLDLOCK)
WHERE MoldeID = @MoldeID
  AND ProgramaProduccionID <> @ProgramaProduccionID
  AND Activo = 1
  AND ISNULL(EstatusID, 1) NOT IN (5, 6, 9, 99)
  AND FechaInicioProgramada IS NOT NULL;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@MoldeID", SqlDbType.Int).Value = moldeId;
            cmd.Parameters.Add(
                "@ProgramaProduccionID",
                SqlDbType.Int).Value = programaExcluirId;

            var result = await cmd.ExecuteScalarAsync();

            return result == null || result == DBNull.Value
                ? null
                : Convert.ToDateTime(result);
        }

        private async Task<int> ObtenerSiguienteSecuenciaAsync(
            int maquinaId,
            int programaExcluirId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
SELECT ISNULL(MAX(SecuenciaMaquina), 0) + 1
FROM dbo.Planeacion_ProgramaProduccion WITH (UPDLOCK, HOLDLOCK)
WHERE MaquinaID = @MaquinaID
  AND ProgramaProduccionID <> @ProgramaProduccionID
  AND Activo = 1
  AND ISNULL(EstatusID, 1) NOT IN (5, 6, 9, 99);";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = maquinaId;
            cmd.Parameters.Add(
                "@ProgramaProduccionID",
                SqlDbType.Int).Value = programaExcluirId;

            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        private static decimal CalcularHorasCambio(ProgramaBase programa)
        {
            if (!programa.Cambio.HasValue ||
                !programa.Arranque.HasValue)
            {
                return 1m;
            }

            var cambio = programa.FechaInicioProgramada.Date
                .Add(programa.Cambio.Value);

            var arranque = programa.FechaInicioProgramada.Date
                .Add(programa.Arranque.Value);

            if (arranque < cambio)
                arranque = arranque.AddDays(1);

            var horas = (decimal)(arranque - cambio).TotalHours;

            return horas < 0
                ? 0
                : Math.Round(horas, 4);
        }


        private async Task<ProgramaCola?> ObtenerProgramaAnteriorPorPuntoAsync(
            int maquinaId,
            int programaExcluirId,
            DateTime puntoCola,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
SELECT TOP (1)
    pp.ProgramaProduccionID,
    pp.ParteID,
    pp.NumeroParte,
    pp.ReferenciaSAP,
    pp.MoldeID,
    pp.MoldeCodigo,
    pp.FechaInicioProgramada,
    ISNULL
    (
        pp.FechaFinProgramada,
        DATEADD(MINUTE, CAST(CEILING(ISNULL(pp.HorasProgramadas, 1) * 60) AS INT), pp.FechaInicioProgramada)
    ) AS FechaFinProgramada,
    ISNULL(pp.HorasProgramadas, 0) AS HorasProgramadas
FROM dbo.Planeacion_ProgramaProduccion pp WITH (UPDLOCK, HOLDLOCK)
WHERE pp.Activo = 1
  AND pp.MaquinaID = @MaquinaID
  AND pp.ProgramaProduccionID <> @ProgramaProduccionID
  AND pp.FechaInicioProgramada IS NOT NULL
  AND ISNULL(pp.EstatusID, 1) NOT IN (5, 6, 9, 99)
  AND pp.FechaInicioProgramada <= @PuntoCola
ORDER BY
    pp.FechaInicioProgramada DESC,
    pp.ProgramaProduccionID DESC;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = maquinaId;
            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaExcluirId;
            cmd.Parameters.Add("@PuntoCola", SqlDbType.DateTime).Value = puntoCola;

            await using var rd = await cmd.ExecuteReaderAsync();

            if (!await rd.ReadAsync())
                return null;

            return MapearProgramaCola(rd);
        }

        private static async Task<CalculoCola> CalcularPosicionCompactaAsync(
            int maquinaId,
            int programaExcluirId,
            int? parteId,
            int? moldeId,
            int? parteAnteriorId,
            int? moldeAnteriorId,
            DateTime cursorInicial,
            decimal horasProduccion,
            SqlConnection cn,
            SqlTransaction tx,
            bool trabajarDomingo)
        {
            var cursor = SiguienteAperturaOperativa(cursorInicial, trabajarDomingo);
            cursor = RedondearSiguienteBloqueLocal(cursor, 60);

            if (horasProduccion <= 0)
                horasProduccion = 1m;

            DateTime? moldeLiberado = null;

            for (var intento = 0; intento < 500; intento++)
            {
                cursor = SiguienteAperturaOperativa(cursor, trabajarDomingo);
                cursor = RedondearSiguienteBloqueLocal(cursor, 60);

                var mismaParte = parteId.HasValue && parteAnteriorId.HasValue && parteId.Value == parteAnteriorId.Value;
                var mismoMolde = moldeId.HasValue && moldeAnteriorId.HasValue && moldeId.Value == moldeAnteriorId.Value;

                var horasCambio = (moldeLiberado.HasValue || (!mismaParte && !mismoMolde))
                    ? 1m
                    : 0m;

                var arranque = SumarHorasOperativas(cursor, horasCambio, trabajarDomingo);
                var fin = SumarHorasOperativas(arranque, horasProduccion, trabajarDomingo);

                var finBloqueDuro = await ObtenerFinCruceMaquinaBloqueadaAsync(
                    maquinaId,
                    programaExcluirId,
                    cursor,
                    fin,
                    cn,
                    tx);

                if (finBloqueDuro.HasValue && finBloqueDuro.Value > cursor)
                {
                    cursor = finBloqueDuro.Value;
                    continue;
                }

                if (moldeId.HasValue)
                {
                    var finMolde = await ObtenerFinCruceMoldeIntervaloAsync(
                        moldeId.Value,
                        programaExcluirId,
                        cursor,
                        fin,
                        cn,
                        tx);

                    if (finMolde.HasValue && finMolde.Value > cursor)
                    {
                        moldeLiberado = finMolde.Value;
                        cursor = finMolde.Value;
                        continue;
                    }
                }

                return new CalculoCola
                {
                    Cambio = cursor,
                    Arranque = arranque,
                    Fin = fin,
                    HorasCambio = horasCambio,
                    MoldeLiberado = moldeLiberado
                };
            }

            throw new InvalidOperationException("No fue posible encontrar una posición válida de cola para este movimiento.");
        }

        private static async Task<DateTime?> ObtenerFinCruceMaquinaBloqueadaAsync(
            int maquinaId,
            int programaExcluirId,
            DateTime inicio,
            DateTime fin,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
SELECT TOP (1)
    ISNULL
    (
        pp.FechaFinProgramada,
        DATEADD(MINUTE, CAST(CEILING(ISNULL(pp.HorasProgramadas, 1) * 60) AS INT), pp.FechaInicioProgramada)
    ) AS FechaFinProgramada
FROM dbo.Planeacion_ProgramaProduccion pp WITH (UPDLOCK, HOLDLOCK)
OUTER APPLY
(
    SELECT TOP (1)
        e.EstatusID
    FROM dbo.Produccion_Ejecucion e
    WHERE e.ProgramaProduccionID = pp.ProgramaProduccionID
      AND e.Activo = 1
    ORDER BY e.EjecucionProduccionID DESC
) pe
WHERE pp.Activo = 1
  AND pp.MaquinaID = @MaquinaID
  AND pp.ProgramaProduccionID <> @ProgramaProduccionID
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.Planeacion_ProgramaProduccion origen
      CROSS APPLY
      (
          SELECT CHARINDEX(N'NSQ_LHRH_PAIR:',ISNULL(origen.Observaciones,N'')) AS PosGrupo
      ) pos
      CROSS APPLY
      (
          SELECT TRY_CONVERT
          (
              INT,
              LEFT
              (
                  SUBSTRING(origen.Observaciones,pos.PosGrupo + LEN(N'NSQ_LHRH_PAIR:'),50),
                  CHARINDEX(N';',SUBSTRING(origen.Observaciones,pos.PosGrupo + LEN(N'NSQ_LHRH_PAIR:'),50) + N';') - 1
              )
          ) AS Grupo
      ) grp
      WHERE origen.ProgramaProduccionID = @ProgramaProduccionID
        AND pos.PosGrupo > 0
        AND grp.Grupo IS NOT NULL
        AND pp.Observaciones LIKE N'%NSQ_LHRH_PAIR:' + CONVERT(NVARCHAR(20),grp.Grupo) + N';%'
  )
  AND pp.FechaInicioProgramada IS NOT NULL
  AND
  (
        ISNULL(pp.EstatusID, 1) IN (2, 3, 4, 5, 6, 9, 99)
     OR pe.EstatusID IS NOT NULL
     OR EXISTS
        (
            SELECT 1
            FROM dbo.Calidad_Inspecciones ci
            WHERE ci.ProgramaProduccionID = pp.ProgramaProduccionID
        )
     OR
        (
            GETDATE() >= pp.FechaInicioProgramada
            AND GETDATE() < ISNULL
            (
                pp.FechaFinProgramada,
                DATEADD(MINUTE, CAST(CEILING(ISNULL(pp.HorasProgramadas, 1) * 60) AS INT), pp.FechaInicioProgramada)
            )
        )
  )
  AND pp.FechaInicioProgramada < @Fin
  AND ISNULL
      (
          pp.FechaFinProgramada,
          DATEADD(MINUTE, CAST(CEILING(ISNULL(pp.HorasProgramadas, 1) * 60) AS INT), pp.FechaInicioProgramada)
      ) > @Inicio
ORDER BY
    ISNULL
    (
        pp.FechaFinProgramada,
        DATEADD(MINUTE, CAST(CEILING(ISNULL(pp.HorasProgramadas, 1) * 60) AS INT), pp.FechaInicioProgramada)
    ) DESC;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = maquinaId;
            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaExcluirId;
            cmd.Parameters.Add("@Inicio", SqlDbType.DateTime).Value = inicio;
            cmd.Parameters.Add("@Fin", SqlDbType.DateTime).Value = fin;

            var result = await cmd.ExecuteScalarAsync();

            return result == null || result == DBNull.Value
                ? null
                : Convert.ToDateTime(result);
        }

        private static async Task<DateTime?> ObtenerFinCruceMoldeIntervaloAsync(
            int moldeId,
            int programaExcluirId,
            DateTime inicio,
            DateTime fin,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
SELECT TOP (1)
    ISNULL
    (
        pp.FechaFinProgramada,
        DATEADD(MINUTE, CAST(CEILING(ISNULL(pp.HorasProgramadas, 1) * 60) AS INT), pp.FechaInicioProgramada)
    ) AS FechaFinProgramada
FROM dbo.Planeacion_ProgramaProduccion pp WITH (UPDLOCK, HOLDLOCK)
WHERE pp.Activo = 1
  AND pp.MoldeID = @MoldeID
  AND pp.ProgramaProduccionID <> @ProgramaProduccionID
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.Planeacion_ProgramaProduccion origen
      CROSS APPLY
      (
          SELECT CHARINDEX(
              N'NSQ_LHRH_PAIR:',
              ISNULL(origen.Observaciones,N'')
          ) AS PosGrupo
      ) pos
      CROSS APPLY
      (
          SELECT TRY_CONVERT(
              INT,
              LEFT(
                  SUBSTRING(
                      origen.Observaciones,
                      pos.PosGrupo + LEN(N'NSQ_LHRH_PAIR:'),
                      50
                  ),
                  CHARINDEX(
                      N';',
                      SUBSTRING(
                          origen.Observaciones,
                          pos.PosGrupo + LEN(N'NSQ_LHRH_PAIR:'),
                          50
                      ) + N';'
                  ) - 1
              )
          ) AS Grupo
      ) grp
      WHERE origen.ProgramaProduccionID = @ProgramaProduccionID
        AND pos.PosGrupo > 0
        AND grp.Grupo IS NOT NULL
        AND pp.Observaciones LIKE
            N'%NSQ_LHRH_PAIR:' +
            CONVERT(NVARCHAR(20),grp.Grupo) +
            N';%'
  )
  AND pp.FechaInicioProgramada IS NOT NULL
  AND ISNULL(pp.EstatusID, 1) NOT IN (5, 6, 9, 99)
  AND pp.FechaInicioProgramada < @Fin
  AND ISNULL
      (
          pp.FechaFinProgramada,
          DATEADD(MINUTE, CAST(CEILING(ISNULL(pp.HorasProgramadas, 1) * 60) AS INT), pp.FechaInicioProgramada)
      ) > @Inicio
ORDER BY
    pp.FechaInicioProgramada;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@MoldeID", SqlDbType.Int).Value = moldeId;
            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaExcluirId;
            cmd.Parameters.Add("@Inicio", SqlDbType.DateTime).Value = inicio;
            cmd.Parameters.Add("@Fin", SqlDbType.DateTime).Value = fin;

            var result = await cmd.ExecuteScalarAsync();

            return result == null || result == DBNull.Value
                ? null
                : Convert.ToDateTime(result);
        }

        private static async Task<int> ContarProgramasPosterioresReacomodablesAsync(
            int maquinaId,
            int programaExcluirId,
            DateTime desde,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
SELECT COUNT(1)
FROM dbo.Planeacion_ProgramaProduccion pp
OUTER APPLY
(
    SELECT TOP (1)
        e.EstatusID
    FROM dbo.Produccion_Ejecucion e
    WHERE e.ProgramaProduccionID = pp.ProgramaProduccionID
      AND e.Activo = 1
    ORDER BY e.EjecucionProduccionID DESC
) pe
WHERE pp.Activo = 1
  AND pp.MaquinaID = @MaquinaID
  AND pp.ProgramaProduccionID <> @ProgramaProduccionID
  AND pp.FechaInicioProgramada IS NOT NULL
  AND pp.FechaInicioProgramada >= @Desde
  AND ISNULL(pp.EstatusID, 1) = 1
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.Produccion_Ejecucion e2
      WHERE e2.ProgramaProduccionID = pp.ProgramaProduccionID
        AND e2.Activo = 1
  )
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.Calidad_Inspecciones ci
      WHERE ci.ProgramaProduccionID = pp.ProgramaProduccionID
  );";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = maquinaId;
            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaExcluirId;
            cmd.Parameters.Add("@Desde", SqlDbType.DateTime).Value = desde;

            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        private async Task<int> ReacomodarColaPosteriorAsync(
    int maquinaId,
    int programaInsertadoId,
    DateTime desdeSeleccion,
    DateTime cursorInicial,
    int? parteAnteriorId,
    int? moldeAnteriorId,
    int usuarioId,
    SqlConnection cn,
    SqlTransaction tx,
    bool trabajarDomingo)
        {
            /*
             * desdeSeleccion:
             *   Desde qué punto se buscan los programas posteriores.
             *
             * cursorInicial:
             *   Desde qué hora comienza el reacomodo.
             *
             * Esta versión conserva la lógica existente, pero además registra
             * historial POR CADA programa recorrido automáticamente.
             */
            var programas =
                await ObtenerProgramasPosterioresReacomodablesAsync(
                    maquinaId,
                    programaInsertadoId,
                    desdeSeleccion,
                    cn,
                    tx);

            var cursor = cursorInicial;
            var reacomodados = 0;

            foreach (var programa in programas)
            {
                var calculo =
                    await CalcularPosicionCompactaAsync(
                        maquinaId,
                        programa.ProgramaProduccionID,
                        programa.ParteID,
                        programa.MoldeID,
                        parteAnteriorId,
                        moldeAnteriorId,
                        cursor,
                        programa.HorasProgramadas <= 0
                            ? 1m
                            : programa.HorasProgramadas,
                        cn,
                        tx,
                        trabajarDomingo);

                var cambioDiferente =
                    programa.Inicio != calculo.Cambio;

                var finDiferente =
                    programa.Fin != calculo.Fin;

                if (cambioDiferente || finDiferente)
                {
                    var inicioAnterior = programa.Inicio;
                    var finAnterior = programa.Fin;

                    await ActualizarProgramaReacomodadoAsync(
                        programa,
                        maquinaId,
                        calculo.Cambio,
                        calculo.Arranque,
                        calculo.Fin,
                        usuarioId,
                        cn,
                        tx);

                    await InsertarHistorialReacomodoColaAsync(
                        programa,
                        maquinaId,
                        inicioAnterior,
                        finAnterior,
                        calculo.Cambio,
                        calculo.Arranque,
                        calculo.Fin,
                        usuarioId,
                        programaInsertadoId,
                        calculo.MoldeLiberado.HasValue
                            ? "RECORRIDO_POR_MOLDE"
                            : "RECORRIDO_POR_COLA",
                        calculo.MoldeLiberado.HasValue
                            ? "Programa recorrido automáticamente porque el molde " +
                              programa.MoldeTexto +
                              " no estaba disponible en la nueva ventana. " +
                              "El molde quedó disponible a partir de " +
                              calculo.MoldeLiberado.Value.ToString("dd/MM/yyyy HH:mm") +
                              "."
                            : "Programa recorrido automáticamente por el reacomodo " +
                              "de la cola de la máquina.",
                        cn,
                        tx);

                    reacomodados++;
                }

                cursor = calculo.Fin;
                parteAnteriorId = programa.ParteID;
                moldeAnteriorId = programa.MoldeID;
            }

            return reacomodados;
        }

        private static async Task InsertarHistorialReacomodoColaAsync(
    ProgramaCola programa,
    int maquinaId,
    DateTime inicioAnterior,
    DateTime finAnterior,
    DateTime fechaCambio,
    DateTime fechaArranque,
    DateTime fechaFin,
    int usuarioId,
    int programaOrigenMovimientoId,
    string tipoMovimiento,
    string motivo,
    SqlConnection cn,
    SqlTransaction tx)
        {
            const string sql = @"
IF OBJECT_ID
(
    N'dbo.Planeacion_ProgramaReprogramacionHistorial',
    N'U'
) IS NOT NULL
BEGIN
    INSERT INTO dbo.Planeacion_ProgramaReprogramacionHistorial
    (
        ProgramaProduccionID,
        MaquinaAnteriorID,
        MaquinaNuevaID,
        InicioAnterior,
        InicioNuevo,
        FinAnterior,
        FinNuevo,
        HorasAnteriores,
        HorasNuevas,
        CambioAnterior,
        CambioNuevo,
        ArranqueAnterior,
        ArranqueNuevo,
        TipoMovimiento,
        EsMovimientoAutomatico,
        ProgramaOrigenMovimientoID,
        UsuarioID,
        FechaCambio,
        Motivo
    )
    VALUES
    (
        @ProgramaProduccionID,
        @MaquinaID,
        @MaquinaID,
        @InicioAnterior,
        @InicioNuevo,
        @FinAnterior,
        @FinNuevo,
        @HorasProgramadas,
        @HorasProgramadas,
        CAST(@InicioAnterior AS time),
        @CambioNuevo,
        NULL,
        @ArranqueNuevo,
        @TipoMovimiento,
        1,
        @ProgramaOrigenMovimientoID,
        @UsuarioID,
        GETDATE(),
        @Motivo
    );
END;";

            await using var cmd =
                new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add(
                "@ProgramaProduccionID",
                SqlDbType.Int).Value =
                programa.ProgramaProduccionID;

            cmd.Parameters.Add(
                "@MaquinaID",
                SqlDbType.Int).Value =
                maquinaId;

            cmd.Parameters.Add(
                "@InicioAnterior",
                SqlDbType.DateTime).Value =
                inicioAnterior;

            cmd.Parameters.Add(
                "@InicioNuevo",
                SqlDbType.DateTime).Value =
                fechaCambio;

            cmd.Parameters.Add(
                "@FinAnterior",
                SqlDbType.DateTime).Value =
                finAnterior;

            cmd.Parameters.Add(
                "@FinNuevo",
                SqlDbType.DateTime).Value =
                fechaFin;

            var horas =
                cmd.Parameters.Add(
                    "@HorasProgramadas",
                    SqlDbType.Decimal);

            horas.Precision = 18;
            horas.Scale = 4;
            horas.Value =
                programa.HorasProgramadas <= 0
                    ? 1m
                    : programa.HorasProgramadas;

            cmd.Parameters.Add(
                "@CambioNuevo",
                SqlDbType.Time).Value =
                fechaCambio.TimeOfDay;

            cmd.Parameters.Add(
                "@ArranqueNuevo",
                SqlDbType.Time).Value =
                fechaArranque.TimeOfDay;

            cmd.Parameters.Add(
                "@TipoMovimiento",
                SqlDbType.NVarChar,
                60).Value =
                string.IsNullOrWhiteSpace(tipoMovimiento)
                    ? "RECORRIDO_POR_COLA"
                    : tipoMovimiento.Trim();

            cmd.Parameters.Add(
                "@ProgramaOrigenMovimientoID",
                SqlDbType.Int).Value =
                programaOrigenMovimientoId;

            cmd.Parameters.Add(
                "@UsuarioID",
                SqlDbType.Int).Value =
                usuarioId;

            cmd.Parameters.Add(
                "@Motivo",
                SqlDbType.NVarChar,
                500).Value =
                string.IsNullOrWhiteSpace(motivo)
                    ? "Programa recorrido automáticamente."
                    : motivo.Length > 500
                        ? motivo[..500]
                        : motivo;

            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task InsertarHistorialMovimientoAsync(
    ProgramaBase programa,
    MaquinaCompatible maquinaDestino,
    DateTime fechaCambio,
    DateTime fechaArranque,
    DateTime fechaFin,
    decimal horasProduccion,
    int usuarioId,
    string motivo,
    SqlConnection cn,
    SqlTransaction tx)
        {
            /*
             * Corrección:
             * AlertasReprogramacion ya consulta TipoMovimiento,
             * EsMovimientoAutomatico y ProgramaOrigenMovimientoID.
             *
             * Para un drag/drop manual dejamos:
             * TipoMovimiento = MOVIDO_MANUAL
             * EsMovimientoAutomatico = 0
             * ProgramaOrigenMovimientoID = NULL
             */
            const string sql = @"
IF OBJECT_ID
(
    N'dbo.Planeacion_ProgramaReprogramacionHistorial',
    N'U'
) IS NOT NULL
BEGIN
    INSERT INTO dbo.Planeacion_ProgramaReprogramacionHistorial
    (
        ProgramaProduccionID,
        MaquinaAnteriorID,
        MaquinaNuevaID,
        InicioAnterior,
        InicioNuevo,
        FinAnterior,
        FinNuevo,
        HorasAnteriores,
        HorasNuevas,
        CambioAnterior,
        CambioNuevo,
        ArranqueAnterior,
        ArranqueNuevo,
        ReleaseDetalleID,
        SolicitudProduccionID,
        SolicitudProduccionDetalleID,
        TipoMovimiento,
        EsMovimientoAutomatico,
        ProgramaOrigenMovimientoID,
        UsuarioID,
        FechaCambio,
        Motivo
    )
    VALUES
    (
        @ProgramaProduccionID,
        @MaquinaAnteriorID,
        @MaquinaNuevaID,
        @InicioAnterior,
        @InicioNuevo,
        @FinAnterior,
        @FinNuevo,
        @HorasAnteriores,
        @HorasNuevas,
        @CambioAnterior,
        @CambioNuevo,
        @ArranqueAnterior,
        @ArranqueNuevo,
        @ReleaseDetalleID,
        @SolicitudProduccionID,
        @SolicitudProduccionDetalleID,
        N'MOVIDO_MANUAL',
        0,
        NULL,
        @UsuarioID,
        GETDATE(),
        @Motivo
    );
END;";

            await using var cmd =
                new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add(
                "@ProgramaProduccionID",
                SqlDbType.Int).Value =
                programa.ProgramaProduccionID;

            cmd.Parameters.Add(
                "@MaquinaAnteriorID",
                SqlDbType.Int).Value =
                (object?)programa.MaquinaID ?? DBNull.Value;

            cmd.Parameters.Add(
                "@MaquinaNuevaID",
                SqlDbType.Int).Value =
                maquinaDestino.MaquinaID;

            cmd.Parameters.Add(
                "@InicioAnterior",
                SqlDbType.DateTime).Value =
                programa.FechaInicioProgramada;

            cmd.Parameters.Add(
                "@InicioNuevo",
                SqlDbType.DateTime).Value =
                fechaCambio;

            cmd.Parameters.Add(
                "@FinAnterior",
                SqlDbType.DateTime).Value =
                programa.FechaFinProgramada;

            cmd.Parameters.Add(
                "@FinNuevo",
                SqlDbType.DateTime).Value =
                fechaFin;

            var horasAntes =
                cmd.Parameters.Add(
                    "@HorasAnteriores",
                    SqlDbType.Decimal);

            horasAntes.Precision = 18;
            horasAntes.Scale = 4;
            horasAntes.Value =
                programa.HorasProgramadas;

            var horasNuevas =
                cmd.Parameters.Add(
                    "@HorasNuevas",
                    SqlDbType.Decimal);

            horasNuevas.Precision = 18;
            horasNuevas.Scale = 4;
            horasNuevas.Value =
                horasProduccion;

            cmd.Parameters.Add(
                "@CambioAnterior",
                SqlDbType.Time).Value =
                (object?)programa.Cambio ?? DBNull.Value;

            cmd.Parameters.Add(
                "@CambioNuevo",
                SqlDbType.Time).Value =
                fechaCambio.TimeOfDay;

            cmd.Parameters.Add(
                "@ArranqueAnterior",
                SqlDbType.Time).Value =
                (object?)programa.Arranque ?? DBNull.Value;

            cmd.Parameters.Add(
                "@ArranqueNuevo",
                SqlDbType.Time).Value =
                fechaArranque.TimeOfDay;

            cmd.Parameters.Add(
                "@ReleaseDetalleID",
                SqlDbType.Int).Value =
                (object?)programa.ReleaseDetalleID ?? DBNull.Value;

            cmd.Parameters.Add(
                "@SolicitudProduccionID",
                SqlDbType.Int).Value =
                (object?)programa.SolicitudProduccionID ?? DBNull.Value;

            cmd.Parameters.Add(
                "@SolicitudProduccionDetalleID",
                SqlDbType.Int).Value =
                (object?)programa.SolicitudProduccionDetalleID ?? DBNull.Value;

            cmd.Parameters.Add(
                "@UsuarioID",
                SqlDbType.Int).Value =
                usuarioId;

            cmd.Parameters.Add(
                "@Motivo",
                SqlDbType.NVarChar,
                500).Value =
                motivo.Length > 500
                    ? motivo[..500]
                    : motivo;

            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task<List<ProgramaCola>> ObtenerProgramasPosterioresReacomodablesAsync(
            int maquinaId,
            int programaExcluirId,
            DateTime desde,
            SqlConnection cn,
            SqlTransaction tx)
        {
            var lista = new List<ProgramaCola>();

            const string sql = @"
SELECT
    pp.ProgramaProduccionID,
    pp.ParteID,
    pp.NumeroParte,
    pp.ReferenciaSAP,
    pp.MoldeID,
    pp.MoldeCodigo,
    pp.FechaInicioProgramada,
    ISNULL
    (
        pp.FechaFinProgramada,
        DATEADD(MINUTE, CAST(CEILING(ISNULL(pp.HorasProgramadas, 1) * 60) AS INT), pp.FechaInicioProgramada)
    ) AS FechaFinProgramada,
    ISNULL(pp.HorasProgramadas, 0) AS HorasProgramadas
FROM dbo.Planeacion_ProgramaProduccion pp
OUTER APPLY
(
    SELECT TOP (1)
        e.EstatusID
    FROM dbo.Produccion_Ejecucion e
    WHERE e.ProgramaProduccionID = pp.ProgramaProduccionID
      AND e.Activo = 1
    ORDER BY e.EjecucionProduccionID DESC
) pe
WHERE pp.Activo = 1
  AND pp.MaquinaID = @MaquinaID
  AND pp.ProgramaProduccionID <> @ProgramaProduccionID
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.Planeacion_ProgramaProduccion origen
      CROSS APPLY
      (
          SELECT CHARINDEX(
              N'NSQ_LHRH_PAIR:',
              ISNULL(origen.Observaciones,N'')
          ) AS PosGrupo
      ) pos
      CROSS APPLY
      (
          SELECT TRY_CONVERT(
              INT,
              LEFT(
                  SUBSTRING(
                      origen.Observaciones,
                      pos.PosGrupo + LEN(N'NSQ_LHRH_PAIR:'),
                      50
                  ),
                  CHARINDEX(
                      N';',
                      SUBSTRING(
                          origen.Observaciones,
                          pos.PosGrupo + LEN(N'NSQ_LHRH_PAIR:'),
                          50
                      ) + N';'
                  ) - 1
              )
          ) AS Grupo
      ) grp
      WHERE origen.ProgramaProduccionID = @ProgramaProduccionID
        AND pos.PosGrupo > 0
        AND grp.Grupo IS NOT NULL
        AND pp.Observaciones LIKE
            N'%NSQ_LHRH_PAIR:' +
            CONVERT(NVARCHAR(20),grp.Grupo) +
            N';%'
  )
  AND pp.FechaInicioProgramada IS NOT NULL
  AND pp.FechaInicioProgramada >= @Desde
  AND ISNULL(pp.EstatusID, 1) = 1
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.Produccion_Ejecucion e2
      WHERE e2.ProgramaProduccionID = pp.ProgramaProduccionID
        AND e2.Activo = 1
  )
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.Calidad_Inspecciones ci
      WHERE ci.ProgramaProduccionID = pp.ProgramaProduccionID
  )
ORDER BY
    pp.FechaInicioProgramada,
    pp.ProgramaProduccionID;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = maquinaId;
            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaExcluirId;
            cmd.Parameters.Add("@Desde", SqlDbType.DateTime).Value = desde;

            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
                lista.Add(MapearProgramaCola(rd));

            return lista;
        }

        private async Task ActualizarProgramaReacomodadoAsync(
            ProgramaCola programa,
            int maquinaId,
            DateTime cambio,
            DateTime arranque,
            DateTime fin,
            int usuarioId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
UPDATE dbo.Planeacion_ProgramaProduccion
SET
    FechaInicioProgramada = @FechaInicio,
    FechaFinProgramada = @FechaFin,
    Cambio = @Cambio,
    Arranque = @Arranque,
    UsuarioModificacionID = @UsuarioID,
    FechaModificacion = GETDATE()
WHERE ProgramaProduccionID = @ProgramaProduccionID
  AND Activo = 1
  AND ISNULL(EstatusID, 1) = 1
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.Produccion_Ejecucion e
      WHERE e.ProgramaProduccionID = @ProgramaProduccionID
        AND e.Activo = 1
  )
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.Calidad_Inspecciones ci
      WHERE ci.ProgramaProduccionID = @ProgramaProduccionID
  );

IF @@ROWCOUNT <> 1
BEGIN
    THROW 51003,
        'Uno de los programas posteriores ya inició Producción o Calidad y no puede reacomodarse.',
        1;
END;

UPDATE d
SET
    d.HorasPlaneadas = pp.HorasProgramadas,
    d.Cambio = @Cambio,
    d.Arranque = @Arranque
FROM dbo.SolicitudesProduccionDetalle d
INNER JOIN dbo.Planeacion_ProgramaProduccion pp
    ON pp.SolicitudProduccionDetalleID = d.SolicitudProduccionDetalleID
WHERE pp.ProgramaProduccionID = @ProgramaProduccionID;

UPDATE am
SET
    am.FechaProgramadaTentativa = CAST(@FechaInicio AS date),
    am.HoraInicioTentativa = CAST(@FechaInicio AS time),
    am.HoraFinTentativa = CAST(@FechaFin AS time),
    am.HorasEstimadas = pp.HorasProgramadas
FROM dbo.SolicitudesProduccionAsignacionMaquina am
INNER JOIN dbo.Planeacion_ProgramaProduccion pp
    ON pp.SolicitudProduccionDetalleID = am.SolicitudProduccionDetalleID
WHERE pp.ProgramaProduccionID = @ProgramaProduccionID
  AND am.Activo = 1;

UPDATE s
SET
    s.FechaInicioPlaneada = @FechaInicio,
    s.FechaFinPlaneada = @FechaFin
FROM dbo.SolicitudesProduccion s
INNER JOIN dbo.Planeacion_ProgramaProduccion pp
    ON pp.SolicitudProduccionID = s.SolicitudProduccionID
WHERE pp.ProgramaProduccionID = @ProgramaProduccionID;

UPDATE rd
SET
    rd.FechaInicioSugerida = @FechaInicio,
    rd.FechaFinEstimada = @FechaFin,
    rd.DaTiempo =
        CASE
            WHEN rd.FechaRequerida IS NULL THEN NULL
            WHEN CONVERT(date, @FechaFin) <= CONVERT(date, rd.FechaRequerida)
                THEN 1
            ELSE 0
        END,
    rd.MensajeCapacidad =
        CASE
            WHEN rd.FechaRequerida IS NULL
                THEN 'Programa reacomodado. Sin fecha requerida del cliente.'
            WHEN CONVERT(date, @FechaFin) <= CONVERT(date, rd.FechaRequerida)
                THEN 'Programa reacomodado dentro de la fecha requerida.'
            ELSE 'Programa reacomodado posterior a la fecha requerida.'
        END,
    rd.FechaModificacion = GETDATE()
FROM dbo.Planeacion_ReleaseDetalle rd
INNER JOIN dbo.Planeacion_ProgramaProduccion pp
    ON pp.ReleaseDetalleID = rd.ReleaseDetalleID
WHERE pp.ProgramaProduccionID = @ProgramaProduccionID
  AND rd.Activo = 1;";

            await using (var cmd = new SqlCommand(sql, cn, tx))
            {
                cmd.Parameters.Add("@FechaInicio", SqlDbType.DateTime).Value = cambio;
                cmd.Parameters.Add("@FechaFin", SqlDbType.DateTime).Value = fin;
                cmd.Parameters.Add("@Cambio", SqlDbType.Time).Value = cambio.TimeOfDay;
                cmd.Parameters.Add("@Arranque", SqlDbType.Time).Value = arranque.TimeOfDay;
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value =
                    programa.ProgramaProduccionID;

                await cmd.ExecuteNonQueryAsync();

                await SincronizarSecadoDesdeReprogramacionAsync(programa.ProgramaProduccionID, usuarioId, cn, tx);
            }

            // NSQ_DDP_PRODUCCION_V1 - calendario no reasigna operadores.
        }

        private static async Task SincronizarSecadoDesdeReprogramacionAsync(int programaProduccionId, int usuarioId, SqlConnection cn, SqlTransaction tx)
        {
            if (programaProduccionId <= 0) return;

            const string sql = @"
DECLARE @MaquinaID INT,
        @FechaInicioProgramada DATETIME2,
        @Arranque TIME,
        @TipoSecado NVARCHAR(100),
        @HorasSecado DECIMAL(18,4),
        @MaterialCodigo NVARCHAR(100),
        @MaterialDescripcion NVARCHAR(250),
        @EstatusID INT,
        @ProgramaActivo BIT,
        @FechaArranque DATETIME2,
        @FechaInicioSecado DATETIME2,
        @MinutosSecado INT,
        @TipoProceso NVARCHAR(30),
        @RequiereSecado BIT;

SELECT TOP(1)
    @MaquinaID=pp.MaquinaID,
    @FechaInicioProgramada=pp.FechaInicioProgramada,
    @Arranque=pp.Arranque,
    @TipoSecado=COALESCE(NULLIF(LTRIM(RTRIM(d.TipoSecado)),N''),NULLIF(LTRIM(RTRIM(dt.TipoSecado)),N'')),
    @HorasSecado=COALESCE(d.HorasSecado,dt.HorasSecado),
    @MaterialCodigo=COALESCE(NULLIF(LTRIM(RTRIM(d.MaterialCodigo)),N''),NULLIF(LTRIM(RTRIM(dt.MaterialCodigo)),N'')),
    @MaterialDescripcion=COALESCE(NULLIF(LTRIM(RTRIM(d.MaterialDescripcion)),N''),NULLIF(LTRIM(RTRIM(dt.MaterialDescripcion)),N'')),
    @EstatusID=ISNULL(pp.EstatusID,1),
    @ProgramaActivo=pp.Activo
FROM dbo.Planeacion_ProgramaProduccion pp WITH(UPDLOCK,HOLDLOCK)
LEFT JOIN dbo.SolicitudesProduccionDetalle d
    ON d.SolicitudProduccionDetalleID=pp.SolicitudProduccionDetalleID
   AND d.Activo=1
LEFT JOIN dbo.ERP_ParteDatosTecnicos dt
    ON dt.ParteID=pp.ParteID
   AND dt.Activo=1
WHERE pp.ProgramaProduccionID=@ProgramaProduccionID;

IF @ProgramaActivo IS NULL RETURN;

SET @RequiereSecado=
    CASE
        WHEN @ProgramaActivo=1
         AND @EstatusID NOT IN(5,6,9,99)
         AND @FechaInicioProgramada IS NOT NULL
         AND ISNULL(@HorasSecado,0)>0
         AND (NULLIF(LTRIM(RTRIM(ISNULL(@MaterialCodigo,N''))),N'') IS NOT NULL
              OR NULLIF(LTRIM(RTRIM(ISNULL(@MaterialDescripcion,N''))),N'') IS NOT NULL)
        THEN 1
        ELSE 0
    END;

IF @RequiereSecado=1
BEGIN
    SET @MinutosSecado=CONVERT(INT,CEILING(@HorasSecado*60));
    SET @TipoProceso=
        CASE
            WHEN UPPER(ISNULL(@TipoSecado,N'')) LIKE N'%DESHUM%'
              OR UPPER(ISNULL(@TipoSecado,N'')) LIKE N'%DESUM%'
            THEN N'DESHUMIDIFICADO'
            ELSE N'SECADO'
        END;

    IF @Arranque IS NULL
        SET @FechaArranque=@FechaInicioProgramada;
    ELSE
    BEGIN
        SET @FechaArranque=DATEADD(SECOND,DATEDIFF(SECOND,CAST('00:00:00' AS TIME),@Arranque),CAST(CAST(@FechaInicioProgramada AS DATE) AS DATETIME2));
        IF @FechaArranque<@FechaInicioProgramada SET @FechaArranque=DATEADD(DAY,1,@FechaArranque);
    END;

    SET @FechaInicioSecado=DATEADD(MINUTE,-@MinutosSecado,@FechaArranque);

    UPDATE sm
    SET
        sm.MaquinaProgramadaID=@MaquinaID,
        sm.TipoSecadoOrigen=NULLIF(LTRIM(RTRIM(@TipoSecado)),N''),
        sm.TipoProceso=@TipoProceso,
        sm.HorasSecadoRequeridas=@HorasSecado,
        sm.MinutosSecadoRequeridos=@MinutosSecado,
        sm.FechaArranqueProduccion=@FechaArranque,
        sm.FechaInicioSecadoObjetivo=@FechaInicioSecado,
        sm.FechaLimiteEntregaMaterial=DATEADD(MINUTE,-ISNULL(sm.MargenEntregaAntesSecadoMinutos,0),@FechaInicioSecado),
        sm.FechaObjetivoFinSecado=@FechaArranque,
        sm.Estado=
            CASE
                WHEN sm.Estado=N'CANCELADO' THEN
                    CASE
                        WHEN EXISTS
                        (
                            SELECT 1
                            FROM dbo.Produccion_SecadoCargas c
                            WHERE c.SecadoMaterialID=sm.SecadoMaterialID
                              AND c.Activo=1
                              AND c.Estado=N'EN_PROCESO'
                        ) THEN N'EN_PROCESO'
                        WHEN ISNULL(sm.CantidadFinalizadaKg,0)>0.0005 THEN N'PARCIAL'
                        ELSE N'PENDIENTE'
                    END
                ELSE sm.Estado
            END,
        sm.Activo=1,
        sm.UsuarioModificacionID=@UsuarioID,
        sm.FechaModificacion=SYSDATETIME()
    FROM dbo.Produccion_SecadoMaterial sm
    WHERE sm.ProgramaProduccionID=@ProgramaProduccionID
      AND sm.Estado<>N'FINALIZADO';

    DECLARE @PreparacionAnticipadaID INT,
            @EstadoPreparacion NVARCHAR(30);

    SELECT TOP(1)
        @PreparacionAnticipadaID=PreparacionAnticipadaID,
        @EstadoPreparacion=Estado
    FROM dbo.Produccion_PreparacionAnticipada WITH(UPDLOCK,HOLDLOCK)
    WHERE ProgramaProduccionID=@ProgramaProduccionID
      AND TipoTarea=N'SECADO_MATERIAL'
    ORDER BY PreparacionAnticipadaID DESC;

    IF @PreparacionAnticipadaID IS NULL
    BEGIN
        INSERT INTO dbo.Produccion_PreparacionAnticipada
        (
            ProgramaProduccionID,TipoTarea,FechaObjetivo,FechaAviso,Estado,
            UsuarioConfirmacionID,FechaConfirmacion,Observaciones,Activo,
            UsuarioCreacionID,FechaCreacion,UsuarioInicioID,FechaInicioReal,
            FechaFinReal,DuracionRealMinutos,LimiteMinutosAplicado,
            ExcedioLimite,MotivoExceso
        )
        VALUES
        (
            @ProgramaProduccionID,N'SECADO_MATERIAL',@FechaArranque,@FechaInicioSecado,N'PENDIENTE',
            NULL,NULL,NULL,1,@UsuarioID,SYSDATETIME(),NULL,NULL,NULL,NULL,NULL,0,NULL
        );
    END
    ELSE IF @EstadoPreparacion=N'EN_PROCESO'
    BEGIN
        UPDATE dbo.Produccion_PreparacionAnticipada
        SET FechaObjetivo=@FechaArranque,
            FechaAviso=@FechaInicioSecado,
            Activo=1,
            UsuarioModificacionID=@UsuarioID,
            FechaModificacion=SYSDATETIME()
        WHERE PreparacionAnticipadaID=@PreparacionAnticipadaID;
    END
    ELSE IF @EstadoPreparacion<>N'CONFIRMADA'
    BEGIN
        UPDATE dbo.Produccion_PreparacionAnticipada
        SET FechaObjetivo=@FechaArranque,
            FechaAviso=@FechaInicioSecado,
            Estado=N'PENDIENTE',
            UsuarioInicioID=NULL,
            FechaInicioReal=NULL,
            FechaFinReal=NULL,
            DuracionRealMinutos=NULL,
            LimiteMinutosAplicado=NULL,
            ExcedioLimite=0,
            MotivoExceso=NULL,
            UsuarioConfirmacionID=NULL,
            FechaConfirmacion=NULL,
            Activo=1,
            UsuarioModificacionID=@UsuarioID,
            FechaModificacion=SYSDATETIME()
        WHERE PreparacionAnticipadaID=@PreparacionAnticipadaID;
    END
END
ELSE
BEGIN
    UPDATE dbo.Produccion_PreparacionAnticipada
    SET Estado=N'CANCELADA',
        Activo=0,
        UsuarioModificacionID=@UsuarioID,
        FechaModificacion=SYSDATETIME()
    WHERE ProgramaProduccionID=@ProgramaProduccionID
      AND TipoTarea=N'SECADO_MATERIAL'
      AND Estado=N'PENDIENTE'
      AND Activo=1;

    UPDATE sm
    SET sm.Estado=N'CANCELADO',
        sm.UsuarioModificacionID=@UsuarioID,
        sm.FechaModificacion=SYSDATETIME()
    FROM dbo.Produccion_SecadoMaterial sm
    WHERE sm.ProgramaProduccionID=@ProgramaProduccionID
      AND sm.Activo=1
      AND sm.Estado IN(N'PENDIENTE',N'PARCIAL')
      AND ISNULL(sm.CantidadAsignadaKg,0)<=0.0005
      AND ISNULL(sm.CantidadFinalizadaKg,0)<=0.0005
      AND NOT EXISTS
      (
          SELECT 1
          FROM dbo.Produccion_SecadoCargas c
          WHERE c.SecadoMaterialID=sm.SecadoMaterialID
            AND c.Activo=1
            AND c.Estado=N'EN_PROCESO'
      );
END;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaProduccionId;
            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
            await cmd.ExecuteNonQueryAsync();
        }
        private static ProgramaCola MapearProgramaCola(SqlDataReader rd)
        {
            return new ProgramaCola
            {
                ProgramaProduccionID = Entero(rd, "ProgramaProduccionID"),
                ParteID = NullableEntero(rd, "ParteID"),
                ParteTexto = Texto(rd, "ReferenciaSAP") ?? Texto(rd, "NumeroParte") ?? "la pieza",
                MoldeID = NullableEntero(rd, "MoldeID"),
                MoldeTexto = Texto(rd, "MoldeCodigo") ?? "el molde",
                Inicio = Fecha(rd, "FechaInicioProgramada"),
                Fin = Fecha(rd, "FechaFinProgramada"),
                HorasProgramadas = Decimal(rd, "HorasProgramadas")
            };
        }

        private static DateTime RedondearSiguienteBloqueLocal(DateTime fecha, int minutos)
        {
            if (minutos <= 0)
                minutos = 15;

            var bloqueTicks = TimeSpan.FromMinutes(minutos).Ticks;

            var ticks = fecha.Ticks % bloqueTicks == 0
                ? fecha.Ticks
                : fecha.Ticks + (bloqueTicks - fecha.Ticks % bloqueTicks);

            var redondeada = new DateTime(ticks, DateTimeKind.Unspecified);

            return new DateTime(
                redondeada.Year,
                redondeada.Month,
                redondeada.Day,
                redondeada.Hour,
                redondeada.Minute,
                0,
                DateTimeKind.Unspecified);
        }

        private static string ConstruirResumenMovimientoCompacto(
            ProgramaBase programa,
            MaquinaCompatible destino,
            ProgramaCola? anteriorCola,
            DateTime cambio,
            DateTime arranque,
            DateTime fin,
            DateTime? moldeLiberado,
            decimal horasCambio,
            int programasQueSeRecorreran)
        {
            var motivos = new List<string>
            {
                $"Se moverá de {programa.MaquinaCodigo ?? "sin máquina"} a {destino.Codigo}.",
                anteriorCola == null
                    ? "Se insertará al primer punto operativo disponible de la máquina destino."
                    : $"Se insertará en cola después de {anteriorCola.ParteTexto}, que termina el {anteriorCola.Fin:dd/MM/yyyy HH:mm}."
            };

            if (moldeLiberado.HasValue)
                motivos.Add($"El molde estaba ocupado y queda libre el {moldeLiberado:dd/MM/yyyy HH:mm}.");

            if (programasQueSeRecorreran > 0)
                motivos.Add($"Se compactará la cola y se recorrerán {programasQueSeRecorreran} programa(s) posterior(es) sin dejar huecos.");

            motivos.Add($"Cambio: {cambio:dd/MM/yyyy HH:mm}.");
            motivos.Add($"Arranque: {arranque:dd/MM/yyyy HH:mm}.");
            motivos.Add($"Tiempo considerado para cambio: {horasCambio:N2} h.");
            motivos.Add($"Fin estimado: {fin:dd/MM/yyyy HH:mm}.");

            return string.Join(" ", motivos);
        }

        // ============================================================
        // ESCRITURAS
        // ============================================================

        private static async Task ActualizarProgramaAsync(
            ProgramaBase programa,
            MaquinaCompatible maquinaDestino,
            DateTime fechaCambio,
            DateTime fechaArranque,
            DateTime fechaFin,
            decimal horasProduccion,
            int secuencia,
            int usuarioId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
UPDATE dbo.Planeacion_ProgramaProduccion
SET
    MaquinaID = @MaquinaID,
    MaquinaCodigo = @MaquinaCodigo,
    MaquinaNombre = @MaquinaNombre,

    FechaInicioProgramada = @FechaCambio,
    FechaFinProgramada = @FechaFin,

    Cambio = @Cambio,
    Arranque = @Arranque,

    HorasProgramadas = @HorasProgramadas,
    SecuenciaMaquina = @SecuenciaMaquina,

    UsuarioModificacionID = @UsuarioID,
    FechaModificacion = GETDATE()
WHERE ProgramaProduccionID = @ProgramaProduccionID
  AND Activo = 1;";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value =
                maquinaDestino.MaquinaID;

            cmd.Parameters.Add("@MaquinaCodigo", SqlDbType.NVarChar, 100).Value =
                maquinaDestino.Codigo;

            cmd.Parameters.Add("@MaquinaNombre", SqlDbType.NVarChar, 200).Value =
                maquinaDestino.Nombre;

            cmd.Parameters.Add("@FechaCambio", SqlDbType.DateTime).Value =
                fechaCambio;

            cmd.Parameters.Add("@FechaFin", SqlDbType.DateTime).Value =
                fechaFin;

            cmd.Parameters.Add("@Cambio", SqlDbType.Time).Value =
                fechaCambio.TimeOfDay;

            cmd.Parameters.Add("@Arranque", SqlDbType.Time).Value =
                fechaArranque.TimeOfDay;

            var horasParam =
                cmd.Parameters.Add("@HorasProgramadas", SqlDbType.Decimal);

            horasParam.Precision = 18;
            horasParam.Scale = 4;
            horasParam.Value = horasProduccion;

            cmd.Parameters.Add("@SecuenciaMaquina", SqlDbType.Int).Value =
                secuencia;

            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value =
                usuarioId;

            cmd.Parameters.Add(
                "@ProgramaProduccionID",
                SqlDbType.Int).Value = programa.ProgramaProduccionID;

            var afectados = await cmd.ExecuteNonQueryAsync();

            if (afectados != 1)
            {
                throw new InvalidOperationException(
                    "El programa cambió o dejó de estar disponible.");
            }
        }

        private static async Task SincronizarDocumentosRelacionadosAsync(
            ProgramaBase programa,
            MaquinaCompatible maquinaDestino,
            DateTime fechaCambio,
            DateTime fechaArranque,
            DateTime fechaFin,
            decimal horasProduccion,
            SqlConnection cn,
            SqlTransaction tx)
        {
            if (programa.SolicitudProduccionDetalleID.HasValue)
            {
                const string sqlDetalle = @"
UPDATE dbo.SolicitudesProduccionDetalle
SET
    MaquinaSugeridaID = @MaquinaID,
    HorasPlaneadas = @HorasProgramadas,
    Cambio = @Cambio,
    Arranque = @Arranque
WHERE SolicitudProduccionDetalleID = @SolicitudProduccionDetalleID;

UPDATE dbo.SolicitudesProduccionAsignacionMaquina
SET
    MaquinaID = @MaquinaID,
    FechaProgramadaTentativa = @FechaProgramada,
    HoraInicioTentativa = @HoraInicio,
    HoraFinTentativa = @HoraFin,
    HorasEstimadas = @HorasProgramadas
WHERE SolicitudProduccionDetalleID = @SolicitudProduccionDetalleID
  AND Activo = 1;";

                await using var cmd = new SqlCommand(sqlDetalle, cn, tx);

                cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value =
                    maquinaDestino.MaquinaID;

                var horasParam =
                    cmd.Parameters.Add("@HorasProgramadas", SqlDbType.Decimal);

                horasParam.Precision = 18;
                horasParam.Scale = 4;
                horasParam.Value = horasProduccion;

                cmd.Parameters.Add("@Cambio", SqlDbType.Time).Value =
                    fechaCambio.TimeOfDay;

                cmd.Parameters.Add("@Arranque", SqlDbType.Time).Value =
                    fechaArranque.TimeOfDay;

                cmd.Parameters.Add("@FechaProgramada", SqlDbType.Date).Value =
                    fechaCambio.Date;

                cmd.Parameters.Add("@HoraInicio", SqlDbType.Time).Value =
                    fechaCambio.TimeOfDay;

                cmd.Parameters.Add("@HoraFin", SqlDbType.Time).Value =
                    fechaFin.TimeOfDay;

                cmd.Parameters.Add(
                    "@SolicitudProduccionDetalleID",
                    SqlDbType.Int).Value =
                    programa.SolicitudProduccionDetalleID.Value;

                await cmd.ExecuteNonQueryAsync();
            }

            if (programa.SolicitudProduccionID.HasValue)
            {
                const string sqlOf = @"
UPDATE dbo.SolicitudesProduccion
SET
    FechaInicioPlaneada = @FechaInicio,
    FechaFinPlaneada = @FechaFin
WHERE SolicitudProduccionID = @SolicitudProduccionID;";

                await using var cmd = new SqlCommand(sqlOf, cn, tx);

                cmd.Parameters.Add("@FechaInicio", SqlDbType.DateTime).Value =
                    fechaCambio;

                cmd.Parameters.Add("@FechaFin", SqlDbType.DateTime).Value =
                    fechaFin;

                cmd.Parameters.Add(
                    "@SolicitudProduccionID",
                    SqlDbType.Int).Value =
                    programa.SolicitudProduccionID.Value;

                await cmd.ExecuteNonQueryAsync();
            }

            if (programa.ReleaseDetalleID.HasValue)
            {
                const string sqlRelease = @"
UPDATE dbo.Planeacion_ReleaseDetalle
SET
    FechaInicioSugerida = @FechaInicio,
    FechaFinEstimada = @FechaFin,
    DaTiempo =
        CASE
            WHEN FechaRequerida IS NULL THEN NULL
            WHEN CONVERT(date, @FechaFin) <= CONVERT(date, FechaRequerida)
                THEN 1
            ELSE 0
        END,
    MensajeCapacidad =
        CASE
            WHEN FechaRequerida IS NULL
                THEN 'Programa reacomodado. Sin fecha requerida del cliente.'
            WHEN CONVERT(date, @FechaFin) <= CONVERT(date, FechaRequerida)
                THEN 'Programa reacomodado dentro de la fecha requerida.'
            ELSE 'Programa reacomodado posterior a la fecha requerida.'
        END,
    FechaModificacion = GETDATE()
WHERE ReleaseDetalleID = @ReleaseDetalleID
  AND Activo = 1;";

                await using var cmd = new SqlCommand(sqlRelease, cn, tx);

                cmd.Parameters.Add("@FechaInicio", SqlDbType.DateTime).Value =
                    fechaCambio;

                cmd.Parameters.Add("@FechaFin", SqlDbType.DateTime).Value =
                    fechaFin;

                cmd.Parameters.Add(
                    "@ReleaseDetalleID",
                    SqlDbType.Int).Value =
                    programa.ReleaseDetalleID.Value;

                await cmd.ExecuteNonQueryAsync();
            }
        }

        
        private static async Task ReordenarSecuenciasAsync(
            int? maquinaAnteriorId,
            int maquinaNuevaId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
;WITH Orden AS
(
    SELECT
        ProgramaProduccionID,
        ROW_NUMBER() OVER
        (
            PARTITION BY MaquinaID
            ORDER BY
                FechaInicioProgramada,
                ProgramaProduccionID
        ) AS NuevaSecuencia
    FROM dbo.Planeacion_ProgramaProduccion
    WHERE Activo = 1
      AND ISNULL(EstatusID, 1) NOT IN (5, 6, 9, 99)
      AND
      (
          MaquinaID = @MaquinaNuevaID
          OR
          (
              @MaquinaAnteriorID IS NOT NULL
              AND MaquinaID = @MaquinaAnteriorID
          )
      )
)
UPDATE pp
SET
    SecuenciaMaquina = o.NuevaSecuencia
FROM dbo.Planeacion_ProgramaProduccion pp
INNER JOIN Orden o
    ON o.ProgramaProduccionID = pp.ProgramaProduccionID;";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@MaquinaNuevaID", SqlDbType.Int).Value =
                maquinaNuevaId;

            cmd.Parameters.Add("@MaquinaAnteriorID", SqlDbType.Int).Value =
                (object?)maquinaAnteriorId ?? DBNull.Value;

            await cmd.ExecuteNonQueryAsync();
        }

        // ============================================================
        // CANDADOS SQL
        // ============================================================

        private static async Task TomarCandadoCalendarioAsync(
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
DECLARE @Resultado INT;

EXEC @Resultado = sys.sp_getapplock
    @Resource = N'ERP_PLANEACION_CALENDARIO_MAQUINAS',
    @LockMode = N'Exclusive',
    @LockOwner = N'Transaction',
    @LockTimeout = 15000;

IF @Resultado < 0
BEGIN
    THROW 51010,
        'El calendario está siendo actualizado. Intenta nuevamente en unos segundos.',
        1;
END;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task ActivarReacomodoPlaneacionAsync(
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
EXEC sys.sp_set_session_context
    @key = N'PlaneacionPermitirReacomodo',
    @value = 1;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task DesactivarReacomodoPlaneacionAsync(
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
EXEC sys.sp_set_session_context
    @key = N'PlaneacionPermitirReacomodo',
    @value = NULL;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task ValidarProgramaSinCrucesAsync(
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
EXEC dbo.Planeacion_ValidarProgramaSinCruces;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task LimpiarContextoSinTransaccionAsync(
            SqlConnection cn)
        {
            if (cn.State != ConnectionState.Open)
                return;

            try
            {
                const string sql = @"
EXEC sys.sp_set_session_context
    @key = N'PlaneacionPermitirReacomodo',
    @value = NULL;";

                await using var cmd = new SqlCommand(sql, cn);
                await cmd.ExecuteNonQueryAsync();
            }
            catch
            {
                // La conexión se cerrará al salir del método.
            }
        }

        private static async Task RollbackSeguroAsync(
            SqlTransaction tx)
        {
            try
            {
                await tx.RollbackAsync();
            }
            catch
            {
                // La transacción puede haber sido abortada por SQL Server.
            }
        }

        private static async Task<string?> ObtenerMotivoBloqueoMovimientoAsync(
            int programaProduccionId,
            SqlConnection cn,
            SqlTransaction? tx,
            bool bloquear)
        {
            var lockSql = bloquear
                ? " WITH (UPDLOCK, HOLDLOCK)"
                : string.Empty;

            var sql = $@"
SELECT TOP (1)
    ISNULL(pp.EstatusID, 1) AS EstatusProgramaID,
    pe.EjecucionProduccionID,
    pe.EstatusID AS EstatusProduccionID,
    ci.InspeccionID,
    ci.Estado AS EstadoCalidad
FROM dbo.Planeacion_ProgramaProduccion pp{lockSql}
OUTER APPLY
(
    SELECT TOP (1)
        e.EjecucionProduccionID,
        e.EstatusID
    FROM dbo.Produccion_Ejecucion e
    WHERE e.ProgramaProduccionID = pp.ProgramaProduccionID
      AND e.Activo = 1
    ORDER BY e.EjecucionProduccionID DESC
) pe
OUTER APPLY
(
    SELECT TOP (1)
        c.InspeccionID,
        c.Estado
    FROM dbo.Calidad_Inspecciones c
    WHERE c.ProgramaProduccionID = pp.ProgramaProduccionID
    ORDER BY c.InspeccionID DESC
) ci
WHERE pp.ProgramaProduccionID = @ProgramaProduccionID
  AND pp.Activo = 1;";

            await using var cmd = tx == null
                ? new SqlCommand(sql, cn)
                : new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value =
                programaProduccionId;

            await using var rd = await cmd.ExecuteReaderAsync();

            if (!await rd.ReadAsync())
                return "No se encontró el programa de producción.";

            var estatusPrograma = Entero(rd, "EstatusProgramaID");
            var ejecucionId = NullableEntero(rd, "EjecucionProduccionID");
            var estatusProduccion = NullableEntero(rd, "EstatusProduccionID");
            var inspeccionId = NullableEntero(rd, "InspeccionID");
            var estadoCalidad = Texto(rd, "EstadoCalidad");

            if (ejecucionId.HasValue)
            {
                return
                    $"El programa ya tiene la ejecución de Producción {ejecucionId.Value} " +
                    $"en estado {NombreEstatusProduccion(estatusProduccion)}. " +
                    "La máquina y las fechas ya no pueden cambiarse desde Planeación.";
            }

            if (inspeccionId.HasValue)
            {
                return
                    $"El programa ya tiene la inspección de Calidad {inspeccionId.Value}" +
                    (string.IsNullOrWhiteSpace(estadoCalidad)
                        ? "."
                        : $" en estado {estadoCalidad.Replace("_", " ")}.") +
                    " La configuración debe conservarse para mantener la trazabilidad.";
            }

            if (estatusPrograma != EstatusPrograma.Programado)
            {
                return
                    $"Solo los programas con estatus Programado pueden moverse. " +
                    $"El estatus actual es {NombreEstatusPrograma(estatusPrograma)}.";
            }

            return null;
        }

        private static async Task RecalcularOperadoresProgramaAsync(
            int programaProduccionId,
            int maquinaId,
            DateTime fechaHora,
            int usuarioId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            var operadores = new List<OperadorEscalaPrograma>();

            const string sqlOperadores = @"
SELECT TOP (2)
    a.AsignacionID AS EscalaAsignacionID,
    a.PersonalID AS PersonaID,
    et.Nombre AS TurnoNombre,
    et.Color AS TurnoColor
FROM dbo.RRHH_EscalaAsignaciones a
INNER JOIN dbo.RRHH_EscalasPersonal esc
    ON esc.EscalaID = a.EscalaID
   AND esc.Activo = 1
   AND esc.Estado = N'Publicada'
INNER JOIN dbo.Persona p
    ON p.PersonaID = a.PersonalID
INNER JOIN dbo.RRHH_EscalaTurnos et
    ON et.EscalaID = a.EscalaID
   AND et.EscalaTurnoID = a.EscalaTurnoID
WHERE a.Activo = 1
  AND a.MaquinaID = @MaquinaID
  AND CAST(@FechaHora AS date) >= CAST(a.FechaInicio AS date)
  AND CAST(@FechaHora AS date) <= CAST(a.FechaFin AS date)
  AND ISNULL(p.EsColaboradorActivo, 1) = 1
  AND
  (
        ISNULL(et.EsFlexible, 0) = 1
     OR et.HoraInicio IS NULL
     OR et.HoraFin IS NULL
     OR
     (
            ISNULL(et.CruzaDiaSiguiente, 0) = 0
        AND CAST(@FechaHora AS time) >= et.HoraInicio
        AND CAST(@FechaHora AS time) < et.HoraFin
     )
     OR
     (
            ISNULL(et.CruzaDiaSiguiente, 0) = 1
        AND
        (
               CAST(@FechaHora AS time) >= et.HoraInicio
            OR CAST(@FechaHora AS time) < et.HoraFin
        )
     )
  )
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.RRHH_NovedadesPersonal n
      WHERE n.EscalaID = a.EscalaID
        AND n.PersonalID = a.PersonalID
        AND n.Activo = 1
        AND n.TipoNovedad IN (N'Baja', N'Incapacidad', N'Vacaciones')
        AND CAST(@FechaHora AS date) >= CAST(n.FechaInicio AS date)
        AND CAST(@FechaHora AS date) <= CAST(ISNULL(n.FechaFin, n.FechaInicio) AS date)
  )
ORDER BY et.Orden, a.AsignacionID DESC;";

            await using (var cmd = new SqlCommand(sqlOperadores, cn, tx))
            {
                cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = maquinaId;
                cmd.Parameters.Add("@FechaHora", SqlDbType.DateTime).Value = fechaHora;

                await using var rd = await cmd.ExecuteReaderAsync();

                while (await rd.ReadAsync())
                {
                    operadores.Add(new OperadorEscalaPrograma
                    {
                        PersonaID = Entero(rd, "PersonaID"),
                        EscalaAsignacionID = Entero(rd, "EscalaAsignacionID"),
                        TurnoNombre = Texto(rd, "TurnoNombre") ?? string.Empty,
                        TurnoColor = Texto(rd, "TurnoColor")
                    });
                }
            }

            const string sqlDesactivar = @"
UPDATE dbo.Planeacion_ProgramaOperadores
SET
    Activo = 0,
    UsuarioModificacionID = @UsuarioID,
    FechaModificacion = GETDATE()
WHERE ProgramaProduccionID = @ProgramaProduccionID
  AND Activo = 1;";

            await using (var cmd = new SqlCommand(sqlDesactivar, cn, tx))
            {
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value =
                    programaProduccionId;
                await cmd.ExecuteNonQueryAsync();
            }

            const string sqlInsertar = @"
INSERT INTO dbo.Planeacion_ProgramaOperadores
(
    ProgramaProduccionID,
    PersonaID,
    RolOperador,
    Activo,
    UsuarioCreacionID,
    FechaCreacion
)
VALUES
(
    @ProgramaProduccionID,
    @PersonaID,
    @RolOperador,
    1,
    @UsuarioID,
    GETDATE()
);";

            for (var i = 0; i < operadores.Count; i++)
            {
                await using var cmd = new SqlCommand(sqlInsertar, cn, tx);
                cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value =
                    programaProduccionId;
                cmd.Parameters.Add("@PersonaID", SqlDbType.Int).Value =
                    operadores[i].PersonaID;
                cmd.Parameters.Add("@RolOperador", SqlDbType.NVarChar, 30).Value =
                    i == 0 ? "PRINCIPAL" : "AUXILIAR";
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                await cmd.ExecuteNonQueryAsync();
            }
        }

        private async Task<List<SolicitudReprogramacionCalendarioVm>> ObtenerSolicitudesReprogramacionPendientesAsync(SqlConnection cn)
        {
            var lista = new List<SolicitudReprogramacionCalendarioVm>();
            const string sql = @"
SELECT
    sr.SolicitudReprogramacionID,
    sr.ProgramaProduccionID,
    sr.SolicitudProduccionID,
    sr.MaquinaID,
    sr.FechaInicioProgramadaActual,
    sr.FechaFinProgramadaActual,
    sr.Motivo,
    sr.Observaciones,
    sr.Estatus,
    sr.UsuarioSolicitanteID,
    sr.FechaSolicitud,
    pp.NumeroParte,
    pp.ReferenciaSAP,
    pp.DesignacionDescripcionSAP AS DescripcionParte,
    pp.MoldeCodigo,
    pp.CantidadProgramada,
    pp.FechaInicioProgramada,
    pp.FechaFinProgramada,
    pp.MaquinaID AS MaquinaProgramaID,
    pp.MaquinaCodigo,
    pp.MaquinaNombre,
    ISNULL(pp.EstatusID,1) AS EstatusProgramaID,
    ISNULL(NULLIF(s.NumeroOFRecibida,N''),NULLIF(s.FolioSolicitud,N'')) AS NumeroOF,
    ISNULL(c.Nombre,pp.ClienteNombre) AS ClienteNombre,
    LTRIM(RTRIM(CONCAT(
    ISNULL(pu.Nombre,N''),
    N' ',
    ISNULL(pu.ApellidoPaterno,N''),
    N' ',
    ISNULL(pu.ApellidoMaterno,N'')
))) AS UsuarioSolicitanteNombre,
    DATEDIFF(MINUTE,sr.FechaInicioProgramadaActual,sr.FechaSolicitud) AS MinutosAtrasoAlSolicitar,
    DATEDIFF(MINUTE,sr.FechaInicioProgramadaActual,GETDATE()) AS MinutosAtrasoActual
FROM dbo.Planeacion_SolicitudesReprogramacion sr
INNER JOIN dbo.Planeacion_ProgramaProduccion pp
    ON pp.ProgramaProduccionID=sr.ProgramaProduccionID
   AND pp.Activo=1
LEFT JOIN dbo.SolicitudesProduccion s
    ON s.SolicitudProduccionID=COALESCE(sr.SolicitudProduccionID,pp.SolicitudProduccionID)
   AND s.Activo=1
LEFT JOIN dbo.ERP_Clientes c
    ON c.ClienteID=pp.ClienteID
LEFT JOIN dbo.Usuarios u
    ON u.UsuarioID=sr.UsuarioSolicitanteID
LEFT JOIN dbo.Persona pu
    ON pu.PersonaID=u.PersonaID
WHERE sr.Activo=1
  AND UPPER(LTRIM(RTRIM(ISNULL(sr.Estatus,N''))))=N'PENDIENTE'
ORDER BY
    CASE
        WHEN sr.FechaInicioProgramadaActual<GETDATE() THEN 0
        ELSE 1
    END,
    sr.FechaSolicitud,
    sr.SolicitudReprogramacionID;";
            await using var cmd = new SqlCommand(sql, cn);
            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                var inicioActual = rd["FechaInicioProgramadaActual"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(rd["FechaInicioProgramadaActual"]);
                var finActual = rd["FechaFinProgramadaActual"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(rd["FechaFinProgramadaActual"]);
                var inicioPrograma = rd["FechaInicioProgramada"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(rd["FechaInicioProgramada"]);
                var finPrograma = rd["FechaFinProgramada"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(rd["FechaFinProgramada"]);
                var fechaSolicitud = Convert.ToDateTime(rd["FechaSolicitud"]);
                var minutosAtrasoActual = rd["MinutosAtrasoActual"] == DBNull.Value ? 0 : Math.Max(0, Convert.ToInt32(rd["MinutosAtrasoActual"]));
                var minutosAtrasoSolicitud = rd["MinutosAtrasoAlSolicitar"] == DBNull.Value ? 0 : Math.Max(0, Convert.ToInt32(rd["MinutosAtrasoAlSolicitar"]));
                lista.Add(new SolicitudReprogramacionCalendarioVm
                {
                    SolicitudReprogramacionID = Convert.ToInt32(rd["SolicitudReprogramacionID"]),
                    ProgramaProduccionID = Convert.ToInt32(rd["ProgramaProduccionID"]),
                    SolicitudProduccionID = rd["SolicitudProduccionID"] == DBNull.Value ? null : Convert.ToInt32(rd["SolicitudProduccionID"]),
                    MaquinaID = rd["MaquinaID"] == DBNull.Value ? null : Convert.ToInt32(rd["MaquinaID"]),
                    MaquinaProgramaID = rd["MaquinaProgramaID"] == DBNull.Value ? null : Convert.ToInt32(rd["MaquinaProgramaID"]),
                    NumeroOF = rd["NumeroOF"]?.ToString()?.Trim(),
                    ClienteNombre = rd["ClienteNombre"]?.ToString()?.Trim(),
                    NumeroParte = rd["NumeroParte"]?.ToString()?.Trim(),
                    ReferenciaSAP = rd["ReferenciaSAP"]?.ToString()?.Trim(),
                    DescripcionParte = rd["DescripcionParte"]?.ToString()?.Trim(),
                    MoldeCodigo = rd["MoldeCodigo"]?.ToString()?.Trim(),
                    CantidadProgramada = rd["CantidadProgramada"] == DBNull.Value ? 0 : Convert.ToInt32(rd["CantidadProgramada"]),
                    MaquinaCodigo = rd["MaquinaCodigo"]?.ToString()?.Trim(),
                    MaquinaNombre = rd["MaquinaNombre"]?.ToString()?.Trim(),
                    FechaInicioProgramadaActual = inicioActual,
                    FechaFinProgramadaActual = finActual,
                    FechaInicioPrograma = inicioPrograma,
                    FechaFinPrograma = finPrograma,
                    Motivo = rd["Motivo"]?.ToString()?.Trim(),
                    Observaciones = rd["Observaciones"]?.ToString()?.Trim(),
                    Estatus = rd["Estatus"]?.ToString()?.Trim() ?? "PENDIENTE",
                    UsuarioSolicitanteID = rd["UsuarioSolicitanteID"] == DBNull.Value ? null : Convert.ToInt32(rd["UsuarioSolicitanteID"]),
                    UsuarioSolicitanteNombre = rd["UsuarioSolicitanteNombre"]?.ToString()?.Trim(),
                    FechaSolicitud = fechaSolicitud,
                    EstatusProgramaID = rd["EstatusProgramaID"] == DBNull.Value ? 1 : Convert.ToInt32(rd["EstatusProgramaID"]),
                    MinutosAtrasoAlSolicitar = minutosAtrasoSolicitud,
                    MinutosAtrasoActual = minutosAtrasoActual
                });
            }
            return lista;
        }
        private static DateTime SiguienteAperturaOperativa(
            DateTime fecha,
            bool trabajarDomingo)
        {
            var value = fecha;

            while (true)
            {
                if (value.DayOfWeek == DayOfWeek.Sunday)
                {
                    if (trabajarDomingo)
                        return value;

                    value = value.Date
                        .AddDays(1)
                        .AddHours(7);

                    continue;
                }

                if (value.DayOfWeek == DayOfWeek.Monday &&
                    value.TimeOfDay < TimeSpan.FromHours(7))
                {
                    return value.Date.AddHours(7);
                }

                if (value.DayOfWeek == DayOfWeek.Saturday &&
     value.TimeOfDay >= new TimeSpan(22, 30, 0))
                {
                    value = value.Date
                        .AddDays(2)
                        .AddHours(7);

                    continue;
                }

                return value;
            }
        }

        private static DateTime FinVentanaOperativa(
            DateTime fecha,
            bool trabajarDomingo)
        {
            if (fecha.DayOfWeek == DayOfWeek.Saturday)
                return fecha.Date.AddHours(22).AddMinutes(30);

            if (fecha.DayOfWeek == DayOfWeek.Sunday)
            {
                return trabajarDomingo
                    ? fecha.Date.AddDays(1)
                    : fecha.Date;
            }

            return fecha.Date.AddDays(1);
        }

        private static DateTime SumarHorasOperativas(
            DateTime inicio,
            decimal horas,
            bool trabajarDomingo)
        {
            if (horas <= 0)
                return SiguienteAperturaOperativa(
                    inicio,
                    trabajarDomingo);

            var cursor = SiguienteAperturaOperativa(
                inicio,
                trabajarDomingo);

            var restante = horas;
            var guard = 0;

            while (restante > 0.0001m)
            {
                guard++;

                if (guard > 2000)
                {
                    throw new InvalidOperationException(
                        "No fue posible calcular el horario operativo.");
                }

                cursor = SiguienteAperturaOperativa(
                    cursor,
                    trabajarDomingo);

                var finVentana = FinVentanaOperativa(
                    cursor,
                    trabajarDomingo);

                var disponible =
                    (decimal)(finVentana - cursor).TotalHours;

                if (disponible <= 0)
                {
                    cursor = SiguienteAperturaOperativa(
                        finVentana.AddMinutes(1),
                        trabajarDomingo);

                    continue;
                }

                if (restante <= disponible)
                    return cursor.AddHours((double)restante);

                restante -= disponible;

                cursor = SiguienteAperturaOperativa(
                    finVentana.AddMinutes(1),
                    trabajarDomingo);
            }

            return cursor;
        }

        private static bool CoincidenMoldesInterrupcionUrgente(int? moldeAId, string? moldeACodigo, int? moldeBId, string? moldeBCodigo)
        {
            if (moldeAId.HasValue && moldeBId.HasValue) return moldeAId.Value == moldeBId.Value;
            var codigoA = (moldeACodigo ?? string.Empty).Trim();
            var codigoB = (moldeBCodigo ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(codigoA) && string.IsNullOrWhiteSpace(codigoB)) return true;
            if (string.IsNullOrWhiteSpace(codigoA) || string.IsNullOrWhiteSpace(codigoB)) return false;
            return string.Equals(codigoA, codigoB, StringComparison.OrdinalIgnoreCase);
        }

        private static PeriodoCalendario ResolverPeriodo(
            string? vista,
            DateTime? fecha,
            DateTime? rangoInicio,
            DateTime? rangoFin)
        {
            var vistaNormalizada =
                (vista ?? "semana").Trim().ToLowerInvariant();

            var fechaBase = (fecha ?? DateTime.Today).Date;

            if (vistaNormalizada == "dia")
            {
                return new PeriodoCalendario
                {
                    Vista = "dia",
                    TextoVista = "Día",
                    Titulo = fechaBase.ToString(
                        "dddd dd 'de' MMMM 'de' yyyy",
                        new CultureInfo("es-MX")),
                    Inicio = fechaBase,
                    Fin = fechaBase.AddDays(1),
                    Anterior = fechaBase.AddDays(-1),
                    Siguiente = fechaBase.AddDays(1)
                };
            }

            if (vistaNormalizada == "mes")
            {
                var inicioMes =
                    new DateTime(fechaBase.Year, fechaBase.Month, 1);

                var finMes = inicioMes.AddMonths(1);

                return new PeriodoCalendario
                {
                    Vista = "mes",
                    TextoVista = "Mes",
                    Titulo = inicioMes.ToString(
                        "MMMM yyyy",
                        new CultureInfo("es-MX")),
                    Inicio = inicioMes,
                    Fin = finMes,
                    Anterior = inicioMes.AddMonths(-1),
                    Siguiente = inicioMes.AddMonths(1)
                };
            }

            if (vistaNormalizada == "rango")
            {
                var inicio = (rangoInicio ?? fechaBase).Date;
                var finInclusive = (rangoFin ?? inicio.AddDays(6)).Date;

                if (finInclusive < inicio)
                    finInclusive = inicio;

                if ((finInclusive - inicio).TotalDays > 30)
                    finInclusive = inicio.AddDays(30);

                return new PeriodoCalendario
                {
                    Vista = "rango",
                    TextoVista = "Rango",
                    Titulo =
                        $"{inicio:dd/MM/yyyy} - {finInclusive:dd/MM/yyyy}",
                    Inicio = inicio,
                    Fin = finInclusive.AddDays(1),
                    Anterior = inicio.AddDays(
                        -(finInclusive - inicio).Days - 1),
                    Siguiente = inicio.AddDays(
                        (finInclusive - inicio).Days + 1),
                    RangoInicio = inicio,
                    RangoFin = finInclusive
                };
            }

            var diasDesdeLunes =
                ((int)fechaBase.DayOfWeek + 6) % 7;

            var inicioSemana =
                fechaBase.AddDays(-diasDesdeLunes);

            return new PeriodoCalendario
            {
                Vista = "semana",
                TextoVista = "Semana",
                Titulo =
                    $"{inicioSemana:dd/MM/yyyy} - {inicioSemana.AddDays(6):dd/MM/yyyy}",
                Inicio = inicioSemana,
                Fin = inicioSemana.AddDays(7),
                Anterior = inicioSemana.AddDays(-7),
                Siguiente = inicioSemana.AddDays(7)
            };
        }

        private sealed class ProgramaActivoInterrupcionUrgente
        {
            public int ProgramaProduccionID { get; set; }
            public int EjecucionProduccionID { get; set; }
            public int? SolicitudProduccionID { get; set; }
            public int? SolicitudProduccionDetalleID { get; set; }
            public int? ParteID { get; set; }
            public string? NumeroParte { get; set; }
            public string? ReferenciaSAP { get; set; }
            public string? DescripcionParte { get; set; }
            public int? MoldeID { get; set; }
            public string? MoldeCodigo { get; set; }
            public int MaquinaID { get; set; }
            public string? MaquinaCodigo { get; set; }
            public string? MaquinaNombre { get; set; }
            public DateTime FechaInicioProgramada { get; set; }
            public DateTime FechaFinProgramada { get; set; }
            public decimal HorasProgramadas { get; set; }
            public int EstatusProduccionID { get; set; }
            public DateTime? FechaInicioReal { get; set; }
            public int? OperadorID { get; set; }
            public string? OperadorNombre { get; set; }
            public string ParteTexto => !string.IsNullOrWhiteSpace(ReferenciaSAP) ? ReferenciaSAP : !string.IsNullOrWhiteSpace(NumeroParte) ? NumeroParte : "Sin parte";
            public string MoldeTexto => string.IsNullOrWhiteSpace(MoldeCodigo) ? "Sin molde" : MoldeCodigo;
        }
        private bool UsuarioEnSesion()
        {
            return HttpContext.Session
                .GetInt32("UsuarioID")
                .HasValue;
        }

        private int ObtenerUsuarioID()
        {
            return HttpContext.Session
                .GetInt32("UsuarioID") ?? 0;
        }

        private static bool PuedeMover(int estatusId)
        {
            return estatusId == EstatusPrograma.Programado;
        }

        private static DateTime NormalizarFecha(DateTime fecha)
        {
            return new DateTime(
                fecha.Year,
                fecha.Month,
                fecha.Day,
                fecha.Hour,
                fecha.Minute,
                0,
                DateTimeKind.Unspecified);
        }

        private static DateTime Maximo(
            DateTime baseFecha,
            params DateTime?[] fechas)
        {
            var resultado = baseFecha;

            foreach (var fecha in fechas)
            {
                if (fecha.HasValue && fecha.Value > resultado)
                    resultado = fecha.Value;
            }

            return resultado;
        }

        private static string ConstruirResumenMovimiento(
            ProgramaBase programa,
            MaquinaCompatible destino,
            DateTime cambio,
            DateTime arranque,
            DateTime fin,
            DateTime? finCola,
            DateTime? finMolde,
            decimal horasCambio)
        {
            var motivos = new List<string>
            {
                $"Se moverá de {programa.MaquinaCodigo ?? "sin máquina"} a {destino.Codigo}.",
                "Se colocará al final de la cola de la máquina destino."
            };

            if (finCola.HasValue)
            {
                motivos.Add(
                    $"La cola destino queda libre el {finCola:dd/MM/yyyy HH:mm}.");
            }

            if (finMolde.HasValue)
            {
                motivos.Add(
                    $"El molde queda libre el {finMolde:dd/MM/yyyy HH:mm}.");
            }

            motivos.Add(
                $"Cambio: {cambio:dd/MM/yyyy HH:mm}.");

            motivos.Add(
                $"Arranque: {arranque:dd/MM/yyyy HH:mm}.");

            motivos.Add(
                $"Tiempo considerado para cambio: {horasCambio:N2} h.");

            motivos.Add(
                $"Fin estimado: {fin:dd/MM/yyyy HH:mm}.");

            return string.Join(" ", motivos);
        }

        private static string CrearTextoOF(
            int? solicitudProduccionId,
            string? folioRelease)
        {
            if (solicitudProduccionId.HasValue)
                return $"OF {solicitudProduccionId.Value}";

            return string.IsNullOrWhiteSpace(folioRelease)
                ? "Programa"
                : folioRelease;
        }

        private static string CrearMaquinaSugeridaTexto(
            string? principal,
            string? sustituta)
        {
            var valores = new[]
            {
                principal,
                sustituta
            }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct()
            .ToList();

            return valores.Any()
                ? string.Join(" / ", valores)
                : "Sin máquina configurada";
        }

        private static string NombreEstatusPrograma(int estatusId)
        {
            return estatusId switch
            {
                1 => "Programado",
                2 => "En preparación",
                3 => "En producción",
                4 => "Pausado",
                5 => "Terminado parcial",
                6 => "Terminado",
                9 => "Cerrado",
                99 => "Cancelado",
                _ => "Sin estatus"
            };
        }

        private static string NombreEstatusProduccion(
            int? estatusId)
        {
            return estatusId switch
            {
                1 => "Pendiente",
                2 => "En preparación",
                3 => "En producción",
                4 => "Pausado",
                5 => "Terminado parcial",
                6 => "Terminado",
                9 => "Cerrado",
                99 => "Cancelado",
                _ => "Sin ejecución"
            };
        }

        private static string CrearSemaforoTexto(
            int estatusPrograma,
            int? estatusProduccion)
        {
            if (estatusProduccion == 3)
                return "Produciendo";

            if (estatusProduccion == 5 ||
                estatusPrograma == 5)
                return "Producido";

            if (estatusProduccion == 9 ||
                estatusProduccion == 99 ||
                estatusPrograma == 9 ||
                estatusPrograma == 99)
                return "Cerrado";

            return "Timeline";
        }

        private static string CrearSemaforoClase(
            int estatusPrograma,
            int? estatusProduccion)
        {
            if (estatusProduccion == 3)
                return "bloque-produciendo";

            if (estatusProduccion == 5 ||
                estatusPrograma == 5)
                return "bloque-producido";

            if (estatusProduccion == 9 ||
                estatusProduccion == 99 ||
                estatusPrograma == 9 ||
                estatusPrograma == 99)
                return "bloque-cerrado";

            return "bloque-timeline";
        }

        private static int Entero(
            SqlDataReader rd,
            string columna)
        {
            var ordinal = rd.GetOrdinal(columna);

            return rd.IsDBNull(ordinal)
                ? 0
                : Convert.ToInt32(rd.GetValue(ordinal));
        }

        private static int? NullableEntero(
            SqlDataReader rd,
            string columna)
        {
            var ordinal = rd.GetOrdinal(columna);

            return rd.IsDBNull(ordinal)
                ? null
                : Convert.ToInt32(rd.GetValue(ordinal));
        }

        private static decimal Decimal(
            SqlDataReader rd,
            string columna)
        {
            var ordinal = rd.GetOrdinal(columna);

            return rd.IsDBNull(ordinal)
                ? 0
                : Convert.ToDecimal(rd.GetValue(ordinal));
        }

        private static DateTime Fecha(
            SqlDataReader rd,
            string columna)
        {
            var ordinal = rd.GetOrdinal(columna);

            return rd.IsDBNull(ordinal)
                ? DateTime.MinValue
                : Convert.ToDateTime(rd.GetValue(ordinal));
        }

        private static TimeSpan? NullableTiempo(
            SqlDataReader rd,
            string columna)
        {
            var ordinal = rd.GetOrdinal(columna);

            return rd.IsDBNull(ordinal)
                ? null
                : (TimeSpan)rd.GetValue(ordinal);
        }

        private static string? Texto(
            SqlDataReader rd,
            string columna)
        {
            var ordinal = rd.GetOrdinal(columna);

            return rd.IsDBNull(ordinal)
                ? null
                : rd.GetValue(ordinal)?.ToString()?.Trim();
        }

        private static bool Booleano(
            SqlDataReader rd,
            string columna)
        {
            var ordinal = rd.GetOrdinal(columna);

            return !rd.IsDBNull(ordinal) &&
                   Convert.ToBoolean(rd.GetValue(ordinal));
        }

        private static string FormatearFechaAlerta(
    DateTime? fecha)
        {
            return fecha.HasValue
                ? fecha.Value.ToString(
                    "dd/MM/yyyy HH:mm",
                    CultureInfo.InvariantCulture)
                : "Sin fecha";
        }

        private sealed class ProgramaBase
        {
            public int ProgramaProduccionID { get; set; }

            public int? MaquinaID { get; set; }
            public string? MaquinaCodigo { get; set; }
            public string? MaquinaNombre { get; set; }

            public int? ParteID { get; set; }
            public string? NumeroParte { get; set; }
            public string? ReferenciaSAP { get; set; }
            public string? DescripcionParte { get; set; }

            public int? MoldeID { get; set; }
            public string? MoldeCodigo { get; set; }

            public int? ReleaseDetalleID { get; set; }
            public int? SolicitudProduccionID { get; set; }
            public int? SolicitudProduccionDetalleID { get; set; }

            public DateTime FechaInicioProgramada { get; set; }
            public DateTime FechaFinProgramada { get; set; }

            public decimal HorasProgramadas { get; set; }
            public TimeSpan? Cambio { get; set; }
            public TimeSpan? Arranque { get; set; }

            public int EstatusID { get; set; }

            public int? MaquinaPrincipalID { get; set; }
            public int? MaquinaSustitutaID { get; set; }
        }

        private sealed class MaquinaCompatible
        {
            public int MaquinaID { get; set; }
            public string Codigo { get; set; } = string.Empty;
            public string Nombre { get; set; } = string.Empty;
        }

        private sealed class ProgramaCola
        {
            public int ProgramaProduccionID { get; set; }
            public int? ParteID { get; set; }
            public string ParteTexto { get; set; } = "la pieza";
            public int? MoldeID { get; set; }
            public string MoldeTexto { get; set; } = "el molde";
            public DateTime Inicio { get; set; }
            public DateTime Fin { get; set; }
            public decimal HorasProgramadas { get; set; }
        }

        private sealed class OperadorEscalaPrograma
        {
            public int PersonaID { get; set; }
            public int EscalaAsignacionID { get; set; }
            public string TurnoNombre { get; set; } = string.Empty;
            public string? TurnoColor { get; set; }
        }

        private sealed class CalculoCola
        {
            public DateTime Cambio { get; set; }
            public DateTime Arranque { get; set; }
            public DateTime Fin { get; set; }
            public decimal HorasCambio { get; set; }
            public DateTime? MoldeLiberado { get; set; }
        }

        private sealed class PeriodoCalendario
        {
            public string Vista { get; set; } = "semana";
            public string TextoVista { get; set; } = "Semana";
            public string Titulo { get; set; } = string.Empty;

            public DateTime Inicio { get; set; }
            public DateTime Fin { get; set; }

            public DateTime Anterior { get; set; }
            public DateTime Siguiente { get; set; }

            public DateTime? RangoInicio { get; set; }
            public DateTime? RangoFin { get; set; }
        }
    }


    public sealed class SolicitudReprogramacionCalendarioVm
    {
        public int SolicitudReprogramacionID { get; set; }
        public int ProgramaProduccionID { get; set; }
        public int? SolicitudProduccionID { get; set; }
        public int? MaquinaID { get; set; }
        public int? MaquinaProgramaID { get; set; }
        public string? NumeroOF { get; set; }
        public string? ClienteNombre { get; set; }
        public string? NumeroParte { get; set; }
        public string? ReferenciaSAP { get; set; }
        public string? DescripcionParte { get; set; }
        public string? MoldeCodigo { get; set; }
        public int CantidadProgramada { get; set; }
        public string? MaquinaCodigo { get; set; }
        public string? MaquinaNombre { get; set; }
        public DateTime? FechaInicioProgramadaActual { get; set; }
        public DateTime? FechaFinProgramadaActual { get; set; }
        public DateTime? FechaInicioPrograma { get; set; }
        public DateTime? FechaFinPrograma { get; set; }
        public string? Motivo { get; set; }
        public string? Observaciones { get; set; }
        public string Estatus { get; set; } = "PENDIENTE";
        public int? UsuarioSolicitanteID { get; set; }
        public string? UsuarioSolicitanteNombre { get; set; }
        public DateTime FechaSolicitud { get; set; }
        public int EstatusProgramaID { get; set; }
        public int MinutosAtrasoAlSolicitar { get; set; }
        public int MinutosAtrasoActual { get; set; }
        public string TextoParte => !string.IsNullOrWhiteSpace(ReferenciaSAP) ? ReferenciaSAP : !string.IsNullOrWhiteSpace(NumeroParte) ? NumeroParte : "Sin parte";
        public string TextoOF => string.IsNullOrWhiteSpace(NumeroOF) ? $"Programa {ProgramaProduccionID}" : NumeroOF;
        public string TextoMaquina => !string.IsNullOrWhiteSpace(MaquinaCodigo) ? string.IsNullOrWhiteSpace(MaquinaNombre) ? MaquinaCodigo : $"{MaquinaCodigo} - {MaquinaNombre}" : "Sin máquina";
        public string TextoHorarioActual => FechaInicioProgramadaActual.HasValue ? $"{FechaInicioProgramadaActual.Value:dd/MM/yyyy HH:mm} → {(FechaFinProgramadaActual.HasValue ? FechaFinProgramadaActual.Value.ToString("dd/MM/yyyy HH:mm") : "Sin fin")}" : "Sin horario";
        public string TextoAtrasoActual => MinutosAtrasoActual >= 1440 ? $"{MinutosAtrasoActual / 1440} d {MinutosAtrasoActual % 1440 / 60} h" : MinutosAtrasoActual >= 60 ? $"{MinutosAtrasoActual / 60} h {MinutosAtrasoActual % 60} min" : $"{MinutosAtrasoActual} min";
    }
    public sealed class CalendarioMaquinasMoverRequest
    {
        public int ProgramaProduccionID { get; set; }
        public int MaquinaID { get; set; }
        public DateTime Inicio { get; set; }

        public decimal DuracionBloqueHoras { get; set; }
        public bool Redimensionado { get; set; }

        public bool ForzarMaquina { get; set; }
        public bool ConfirmarMovimiento { get; set; }
        public bool TrabajarDomingo { get; set; }
    }
}
