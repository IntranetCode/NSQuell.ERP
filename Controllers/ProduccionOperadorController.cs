using ERP.NSQuell.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using System.Data;

namespace ERP.NSQuell.Controllers
{
    public sealed class ProduccionOperadorController : Controller
    {
        private readonly IConfiguration _configuration;

        public ProduccionOperadorController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        private string ConnectionString =>
            _configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("No se encontró la cadena de conexión DefaultConnection.");



        [HttpGet]
        public async Task<IActionResult> Index()
        {
            if (!UsuarioEnSesion())
                return RedirectToAction("Login", "Login");

            await using var cn =
                new SqlConnection(ConnectionString);

            await cn.OpenAsync();

            var usuarioId =
                ObtenerUsuarioID();

            var esOperador =
                await UsuarioEsOperadorAsync(
                    usuarioId,
                    cn);

            if (!esOperador)
                return AccesoDenegadoOperador();

            var personaId =
                await ObtenerPersonaIDUsuarioAsync(
                    usuarioId,
                    cn);

            if (!personaId.HasValue ||
                personaId.Value <= 0)
            {
                return AccesoDenegadoOperador();
            }

            var programas =
                await ObtenerProgramasEnProduccionAsync(
                    personaId.Value,
                    cn);

            ViewBag.AlertasProximosProgramas =
                await ObtenerAlertasProximosProgramasAsync(
                    personaId.Value,
                    cn,
                    15);

            return View(programas);
        }

        [HttpGet]
        public async Task<IActionResult> Captura(int id)
        {
            if (!UsuarioEnSesion())
                return RedirectToAction("Login", "Login");

            if (id <= 0)
                return NotFound();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            var usuarioId = ObtenerUsuarioID();

            var esOperador = await UsuarioEsOperadorAsync(usuarioId, cn);

            if (!esOperador)
                return AccesoDenegadoOperador();

            var vm = await ObtenerTabletVmAsync(id, cn);

            if (vm == null)
                return NotFound();

            vm.MotivosParo = await CargarMotivosParoAsync(cn);

            vm.HorasCaptura = await ObtenerFilasCapturaHoraAsync(
                vm.EjecucionProduccionID,
                vm.ProgramaProduccionID,
                cn);

            var primeraPendiente = vm.HorasCaptura
                .Where(x => !x.Capturada)
                .OrderBy(x => x.NumeroHora)
                .FirstOrDefault();

            if (primeraPendiente != null)
            {
                vm.FechaProduccion = primeraPendiente.FechaProduccion;
                vm.HoraInicioSugerida = primeraPendiente.HoraInicio;
                vm.HoraFinSugerida = primeraPendiente.HoraFin;
            }

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RegistrarHora(
      ProduccionRegistroHoraPostVm vm)
        {
            if (!UsuarioEnSesion())
                return RedirectToAction("Login", "Login");

            if (vm.EjecucionProduccionID <= 0)
            {
                TempData["Error"] =
                    "No se recibió la ejecución de producción.";

                return RedirectToAction(nameof(Index));
            }

            if (!TimeSpan.TryParse(
                    vm.HoraInicio,
                    out var horaInicioEnviada) ||
                !TimeSpan.TryParse(
                    vm.HoraFin,
                    out var horaFinEnviada))
            {
                TempData["Error"] =
                    "El rango de hora no es válido.";

                return RedirectToAction(
                    nameof(Captura),
                    new
                    {
                        id = vm.EjecucionProduccionID
                    });
            }

           
            var horaInicio = new TimeSpan(
                horaInicioEnviada.Hours,
                horaInicioEnviada.Minutes,
                0);

            var horaFin = new TimeSpan(
                horaFinEnviada.Hours,
                horaFinEnviada.Minutes,
                0);

            var fechaInicioSolicitada =
                vm.FechaProduccion.Date.Add(horaInicio);

            var fechaFinSolicitada =
                vm.FechaProduccion.Date.Add(horaFin);

            if (fechaFinSolicitada <= fechaInicioSolicitada)
            {
                fechaFinSolicitada =
                    fechaFinSolicitada.AddDays(1);
            }

            /*
             * El bloque solamente puede capturarse después
             * de que haya terminado.
             */
            if (DateTime.Now < fechaFinSolicitada)
            {
                TempData["Error"] =
                    "La hora todavía no ha terminado. " +
                    $"Podrás capturar este bloque a partir de " +
                    $"{fechaFinSolicitada:HH:mm}.";

                return RedirectToAction(
                    nameof(Captura),
                    new
                    {
                        id = vm.EjecucionProduccionID
                    });
            }

            var duracionBloque =
                fechaFinSolicitada -
                fechaInicioSolicitada;

            if (duracionBloque <= TimeSpan.Zero ||
                duracionBloque > TimeSpan.FromMinutes(61))
            {
                TempData["Error"] =
                    "El rango de captura debe corresponder " +
                    "a un bloque de una hora.";

                return RedirectToAction(
                    nameof(Captura),
                    new
                    {
                        id = vm.EjecucionProduccionID
                    });
            }

            if (vm.CantidadOK < 0 ||
                vm.CantidadSospechosa < 0 ||
                vm.CantidadScrap < 0)
            {
                TempData["Error"] =
                    "Las cantidades no pueden ser negativas.";

                return RedirectToAction(
                    nameof(Captura),
                    new
                    {
                        id = vm.EjecucionProduccionID
                    });
            }

            if (vm.CantidadOK == 0 &&
                vm.CantidadSospechosa == 0 &&
                vm.CantidadScrap == 0 &&
                string.IsNullOrWhiteSpace(vm.Observaciones))
            {
                TempData["Error"] =
                    "Captura al menos una cantidad o explica " +
                    "en observaciones por qué no hubo producción.";

                return RedirectToAction(
                    nameof(Captura),
                    new
                    {
                        id = vm.EjecucionProduccionID
                    });
            }

            await using var cn =
                new SqlConnection(ConnectionString);

            await cn.OpenAsync();

            var usuarioId =
                ObtenerUsuarioID();

            if (!await UsuarioEsOperadorAsync(
                    usuarioId,
                    cn))
            {
                return AccesoDenegadoOperador();
            }

            await using var tx =
                (SqlTransaction)await cn.BeginTransactionAsync(
                    IsolationLevel.Serializable);

            try
            {
                var ejecucion =
                    await ObtenerEjecucionOperadorAsync(
                        vm.EjecucionProduccionID,
                        cn,
                        tx);

                if (ejecucion == null)
                {
                    await tx.RollbackAsync();
                    return NotFound();
                }

                if (ejecucion.EstatusID !=
                    ProduccionEstatus.EnProduccion)
                {
                    await tx.RollbackAsync();

                    TempData["Error"] =
                        "Solo puedes capturar piezas cuando " +
                        "la producción está en serie.";

                    return RedirectToAction(
                        nameof(Captura),
                        new
                        {
                            id = vm.EjecucionProduccionID
                        });
                }

                if (await TieneParoAbiertoAsync(
                        vm.EjecucionProduccionID,
                        cn,
                        tx))
                {
                    await tx.RollbackAsync();

                    TempData["Error"] =
                        "No puedes capturar piezas mientras " +
                        "exista un paro abierto.";

                    return RedirectToAction(
                        nameof(Captura),
                        new
                        {
                            id = vm.EjecucionProduccionID
                        });
                }

               
                var filasPermitidas =
                    await ObtenerFilasCapturaHoraAsync(
                        ejecucion.EjecucionProduccionID,
                        ejecucion.ProgramaProduccionID,
                        cn,
                        tx);

                
                var filaSolicitada =
                    filasPermitidas.FirstOrDefault(
                        x =>
                            x.FechaProduccion.Date ==
                            vm.FechaProduccion.Date &&

                            x.HoraInicio.Hours ==
                            horaInicio.Hours &&

                            x.HoraInicio.Minutes ==
                            horaInicio.Minutes);

                if (filaSolicitada == null)
                {
                    await tx.RollbackAsync();

                    TempData["Error"] =
                        "La hora enviada no pertenece a los " +
                        "bloques generados para esta producción.";

                    return RedirectToAction(
                        nameof(Captura),
                        new
                        {
                            id = vm.EjecucionProduccionID
                        });
                }

                if (filaSolicitada.Capturada)
                {
                    await tx.RollbackAsync();

                    TempData["Error"] =
                        "Esta hora ya fue capturada.";

                    return RedirectToAction(
                        nameof(Captura),
                        new
                        {
                            id = vm.EjecucionProduccionID
                        });
                }

                if (!filaSolicitada.Disponible)
                {
                    await tx.RollbackAsync();

                    TempData["Error"] =
                        "Debes capturar primero la hora " +
                        "pendiente anterior.";

                    return RedirectToAction(
                        nameof(Captura),
                        new
                        {
                            id = vm.EjecucionProduccionID
                        });
                }

                var fechaInicioFila =
                    filaSolicitada.FechaProduccion.Date
                        .Add(filaSolicitada.HoraInicio);

                var fechaFinFila =
                    filaSolicitada.FechaProduccion.Date
                        .Add(filaSolicitada.HoraFin);

                if (fechaFinFila <= fechaInicioFila)
                {
                    fechaFinFila =
                        fechaFinFila.AddDays(1);
                }

                /*
                 * Comparación tolerante de menos de un minuto.
                 */
                var diferenciaInicioSegundos =
                    Math.Abs(
                        (
                            fechaInicioFila -
                            fechaInicioSolicitada
                        ).TotalSeconds);

                if (diferenciaInicioSegundos >= 60)
                {
                    await tx.RollbackAsync();

                    TempData["Error"] =
                        "La hora inicial enviada no coincide " +
                        "con el bloque horario de producción.";

                    return RedirectToAction(
                        nameof(Captura),
                        new
                        {
                            id = vm.EjecucionProduccionID
                        });
                }

                var diferenciaFinSegundos =
                    Math.Abs(
                        (
                            fechaFinFila -
                            fechaFinSolicitada
                        ).TotalSeconds);

                if (diferenciaFinSegundos >= 60)
                {
                    await tx.RollbackAsync();

                    TempData["Error"] =
                        "El rango enviado no coincide con " +
                        "el bloque horario de producción.";

                    return RedirectToAction(
                        nameof(Captura),
                        new
                        {
                            id = vm.EjecucionProduccionID
                        });
                }

              
                if (DateTime.Now < fechaFinFila)
                {
                    await tx.RollbackAsync();

                    TempData["Error"] =
                        "La hora todavía no ha terminado. " +
                        $"Podrás capturar este bloque a partir de " +
                        $"{fechaFinFila:HH:mm}.";

                    return RedirectToAction(
                        nameof(Captura),
                        new
                        {
                            id = vm.EjecucionProduccionID
                        });
                }

              
                var fechaProduccionReal =
                    filaSolicitada.FechaProduccion.Date;

                var horaInicioReal =
                    new TimeSpan(
                        filaSolicitada.HoraInicio.Hours,
                        filaSolicitada.HoraInicio.Minutes,
                        0);

                var horaFinReal =
                    new TimeSpan(
                        filaSolicitada.HoraFin.Hours,
                        filaSolicitada.HoraFin.Minutes,
                        0);

                if (await ExisteRegistroHoraAsync(
                        vm.EjecucionProduccionID,
                        fechaProduccionReal,
                        horaInicioReal,
                        cn,
                        tx))
                {
                    await tx.RollbackAsync();

                    TempData["Error"] =
                        "La hora seleccionada ya fue capturada. " +
                        "Actualiza la pantalla para consultar el registro.";

                    return RedirectToAction(
                        nameof(Captura),
                        new
                        {
                            id = vm.EjecucionProduccionID
                        });
                }

                /*
                 * Sobrescribir los valores recibidos con los valores
                 * autorizados por el bloque encontrado.
                 */
                vm.FechaProduccion =
                    fechaProduccionReal;

                vm.HoraInicio =
                    horaInicioReal.ToString(@"hh\:mm");

                vm.HoraFin =
                    horaFinReal.ToString(@"hh\:mm");

                var personaOperador =
                    await ObtenerPersonaOperadorAsync(
                        usuarioId,
                        cn,
                        tx);

                var registroHoraId =
                    await InsertarRegistroHoraAsync(
                        ejecucion,
                        vm,
                        horaInicioReal,
                        horaFinReal,
                        personaOperador.PersonaID,
                        usuarioId,
                        cn,
                        tx);

                await VincularRegistroHoraConCalidadAsync(
                    ejecucion,
                    vm,
                    horaInicioReal,
                    horaFinReal,
                    registroHoraId,
                    usuarioId,
                    cn,
                    tx);

                await RecalcularTotalesEjecucionAsync(
                    vm.EjecucionProduccionID,
                    usuarioId,
                    cn,
                    tx);

                await tx.CommitAsync();

                TempData["Success"] =
                    vm.CantidadSospechosa > 0 ||
                    vm.CantidadScrap > 0
                        ? $"Hora {filaSolicitada.NumeroHora} guardada. " +
                          "El material reportado quedó pendiente " +
                          "de revisión de Calidad."
                        : $"Hora {filaSolicitada.NumeroHora} guardada " +
                          "y enviada a Calidad correctamente.";

                return RedirectToAction(
                    nameof(Captura),
                    new
                    {
                        id = vm.EjecucionProduccionID
                    });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();

                TempData["Error"] =
                    "No fue posible guardar la producción: " +
                    ex.Message;

                return RedirectToAction(
                    nameof(Captura),
                    new
                    {
                        id = vm.EjecucionProduccionID
                    });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> IniciarParo(ProduccionParoPostVm vm)
        {
            if (!UsuarioEnSesion())
                return RedirectToAction("Login", "Login");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            var usuarioId = ObtenerUsuarioID();

            var esOperador = await UsuarioEsOperadorAsync(usuarioId, cn);

            if (!esOperador)
                return AccesoDenegadoOperador();

            if (vm.EjecucionProduccionID <= 0)
            {
                TempData["Error"] = "No se recibió la ejecución de producción.";
                return RedirectToAction(nameof(Index));
            }

            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();

            try
            {
                var ejecucion = await ObtenerEjecucionOperadorAsync(
                    vm.EjecucionProduccionID,
                    cn,
                    tx);

                if (ejecucion == null)
                {
                    await tx.RollbackAsync();
                    return NotFound();
                }

                if (ejecucion.EstatusID != ProduccionEstatus.EnProduccion)
                {
                    await tx.RollbackAsync();

                    TempData["Error"] =
                        "Solo puedes reportar paro cuando la producción está en serie.";

                    return RedirectToAction(nameof(Captura), new { id = vm.EjecucionProduccionID });
                }

                var tieneParoAbierto = await TieneParoAbiertoAsync(
                    vm.EjecucionProduccionID,
                    cn,
                    tx);

                if (tieneParoAbierto)
                {
                    await tx.RollbackAsync();

                    TempData["Error"] =
                        "Ya existe un paro abierto para esta producción.";

                    return RedirectToAction(nameof(Captura), new { id = vm.EjecucionProduccionID });
                }

                var motivoTexto = vm.MotivoParoTexto;

                if (vm.MotivoParoID.HasValue)
                {
                    motivoTexto = await ObtenerMotivoParoNombreAsync(
                        vm.MotivoParoID.Value,
                        cn,
                        tx);
                }

                var personaOperador = await ObtenerPersonaOperadorAsync(usuarioId, cn, tx);

                const string sqlInsert = @"
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
    UsuarioCreacionID,
    FechaCreacion,
    Activo
)
VALUES
(
    @EjecucionProduccionID,
    @ProgramaProduccionID,
    @SolicitudProduccionID,
    @MaquinaID,
    @OperadorID,
    GETDATE(),
    @MotivoParoID,
    @MotivoParoTexto,
    @Descripcion,
    @UsuarioID,
    GETDATE(),
    1
);";

                await using (var cmd = new SqlCommand(sqlInsert, cn, tx))
                {
                    cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value =
                        ejecucion.EjecucionProduccionID;

                    cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value =
                        ejecucion.ProgramaProduccionID;

                    cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value =
                        (object?)ejecucion.SolicitudProduccionID ?? DBNull.Value;

                    cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value =
                        (object?)ejecucion.MaquinaID ?? DBNull.Value;

                    cmd.Parameters.Add("@OperadorID", SqlDbType.Int).Value =
                        personaOperador.PersonaID;

                    cmd.Parameters.Add("@MotivoParoID", SqlDbType.Int).Value =
                        (object?)vm.MotivoParoID ?? DBNull.Value;

                    cmd.Parameters.Add("@MotivoParoTexto", SqlDbType.NVarChar, 200).Value =
                        (object?)motivoTexto ?? DBNull.Value;

                    cmd.Parameters.Add("@Descripcion", SqlDbType.NVarChar, 500).Value =
                        string.IsNullOrWhiteSpace(vm.Descripcion)
                            ? DBNull.Value
                            : vm.Descripcion.Trim();

                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value =
                        usuarioId;

                    await cmd.ExecuteNonQueryAsync();
                }

                await CambiarEstatusEjecucionAsync(
                    ejecucion.EjecucionProduccionID,
                    ProduccionEstatus.Pausado,
                    usuarioId,
                    cn,
                    tx);

                await CambiarEstatusProgramaAsync(
                    ejecucion.ProgramaProduccionID,
                    ProgramaProduccionEstatus.Pausado,
                    usuarioId,
                    cn,
                    tx);

                await tx.CommitAsync();

                TempData["Success"] = "Paro reportado correctamente.";

                return RedirectToAction(nameof(Captura), new { id = vm.EjecucionProduccionID });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();

                TempData["Error"] =
                    "No fue posible reportar el paro: " + ex.Message;

                return RedirectToAction(nameof(Captura), new { id = vm.EjecucionProduccionID });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CerrarParo(
     ProduccionCerrarParoPostVm vm)
        {
            if (!UsuarioEnSesion())
                return RedirectToAction("Login", "Login");

            await using var cn =
                new SqlConnection(ConnectionString);

            await cn.OpenAsync();

            var usuarioId = ObtenerUsuarioID();

            var esOperador =
                await UsuarioEsOperadorAsync(usuarioId, cn);

            if (!esOperador)
                return AccesoDenegadoOperador();

            await using var tx =
                (SqlTransaction)await cn.BeginTransactionAsync();

            int ejecucionProduccionId;

            try
            {
                const string sqlLeer = @"
SELECT TOP (1)
    ParoID,
    EjecucionProduccionID,
    FechaInicioParo
FROM dbo.Produccion_Paros
WHERE ParoID = @ParoID
  AND Activo = 1
  AND FechaFinParo IS NULL;";

                DateTime fechaInicioParo;

                await using (var cmd = new SqlCommand(sqlLeer, cn, tx))
                {
                    cmd.Parameters.Add(
                        "@ParoID",
                        SqlDbType.Int).Value =
                        vm.ParoID;

                    await using var rd =
                        await cmd.ExecuteReaderAsync();

                    if (!await rd.ReadAsync())
                    {
                        await tx.RollbackAsync();

                        TempData["Error"] =
                            "No se encontró un paro abierto para cerrar.";

                        return RedirectToAction(nameof(Index));
                    }

                    ejecucionProduccionId =
                        Convert.ToInt32(
                            rd["EjecucionProduccionID"]);

                    fechaInicioParo =
                        Convert.ToDateTime(
                            rd["FechaInicioParo"]);
                }

                var ahora = DateTime.Now;

                var duracionMinutos =
                    (int)Math.Max(
                        0,
                        Math.Round(
                            (ahora - fechaInicioParo)
                                .TotalMinutes));

                var esMayorA15Minutos =
                    duracionMinutos > 15;

                const string sqlCerrar = @"
UPDATE dbo.Produccion_Paros
SET
    FechaFinParo = @FechaFinParo,
    DuracionMinutos = @DuracionMinutos,
    EsMayorA15Minutos =
        CASE
            WHEN @DuracionMinutos > 15 THEN 1
            ELSE 0
        END,
    Descripcion =
        CASE
            WHEN @ObservacionesCierre IS NULL
              OR LTRIM(RTRIM(@ObservacionesCierre)) = N''
                THEN Descripcion
            WHEN Descripcion IS NULL
              OR LTRIM(RTRIM(Descripcion)) = N''
                THEN @ObservacionesCierre
            ELSE
                Descripcion
                + CHAR(13)
                + CHAR(10)
                + N'Cierre: '
                + @ObservacionesCierre
        END,
    UsuarioModificacionID = @UsuarioID,
    FechaModificacion = GETDATE()
WHERE ParoID = @ParoID
  AND Activo = 1;";

                await using (var cmd = new SqlCommand(sqlCerrar, cn, tx))
                {
                    cmd.Parameters.Add(
                        "@ParoID",
                        SqlDbType.Int).Value =
                        vm.ParoID;

                    cmd.Parameters.Add(
                        "@FechaFinParo",
                        SqlDbType.DateTime).Value =
                        ahora;

                    cmd.Parameters.Add(
                        "@DuracionMinutos",
                        SqlDbType.Int).Value =
                        duracionMinutos;

                    cmd.Parameters.Add(
                        "@ObservacionesCierre",
                        SqlDbType.NVarChar,
                        500).Value =
                        string.IsNullOrWhiteSpace(
                            vm.ObservacionesCierre)
                            ? DBNull.Value
                            : vm.ObservacionesCierre.Trim();

                    cmd.Parameters.Add(
                        "@UsuarioID",
                        SqlDbType.Int).Value =
                        usuarioId;

                    await cmd.ExecuteNonQueryAsync();
                }

                var ejecucion =
                    await ObtenerEjecucionOperadorAsync(
                        ejecucionProduccionId,
                        cn,
                        tx);

                if (ejecucion == null)
                {
                    await tx.RollbackAsync();
                    return NotFound();
                }

                if (esMayorA15Minutos)
                {
                    await CambiarEstatusEjecucionAsync(
                        ejecucionProduccionId,
                        ProduccionEstatus.EnPreparacion,
                        usuarioId,
                        cn,
                        tx);

                    await CambiarEstatusProgramaAsync(
                        ejecucion.ProgramaProduccionID,
                        ProgramaProduccionEstatus.EnPreparacion,
                        usuarioId,
                        cn,
                        tx);

                    await CrearOActualizarSolicitudReliberacionCalidadPorParoAsync(
                        ejecucionProduccionId,
                        vm.ParoID,
                        duracionMinutos,
                        usuarioId,
                        cn,
                        tx);

                    await tx.CommitAsync();

                    TempData["Success"] =
                        "Paro cerrado. Duró más de 15 minutos, por lo que " +
                        "la producción regresó a preparación. Debe ejecutar " +
                        "nuevamente los 5 disparos de prueba y solicitar " +
                        "reliberación de Calidad.";

                    return RedirectToAction(
                        "Detalle",
                        "Produccion",
                        new { id = ejecucionProduccionId });
                }

                /*
                 * Paro corto: al cerrarlo ya conocemos toda la interrupción.
                 * Se recorre el fin programado por esos minutos.
                 */
                await DesplazarFinProgramadoParoCortoAsync(
                    ejecucion.ProgramaProduccionID,
                    ejecucionProduccionId,
                    duracionMinutos,
                    usuarioId,
                    cn,
                    tx);

                await CambiarEstatusEjecucionAsync(
                    ejecucionProduccionId,
                    ProduccionEstatus.EnProduccion,
                    usuarioId,
                    cn,
                    tx);

                await CambiarEstatusProgramaAsync(
                    ejecucion.ProgramaProduccionID,
                    ProgramaProduccionEstatus.EnProduccion,
                    usuarioId,
                    cn,
                    tx);

                await tx.CommitAsync();

                TempData["Success"] =
                    "Paro cerrado correctamente. La producción continúa en serie.";

                return RedirectToAction(
                    nameof(Captura),
                    new { id = ejecucionProduccionId });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();

                TempData["Error"] =
                    "No fue posible cerrar el paro: " +
                    ex.Message;

                return RedirectToAction(nameof(Index));
            }
        }
        [HttpGet]
        public async Task<IActionResult> Cajas(int id)
        {
            if (!UsuarioEnSesion())
                return RedirectToAction("Login", "Login");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            var usuarioId = ObtenerUsuarioID();

            var esOperador = await UsuarioEsOperadorAsync(usuarioId, cn);

            if (!esOperador)
                return AccesoDenegadoOperador();

            var vm = await ObtenerCajasOperadorVmAsync(id, cn);

            if (vm == null)
                return NotFound();

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> FormarCaja(int ejecucionProduccionId, int cantidadPiezas, string tipoCaja, string? observaciones)
        {
            if (!UsuarioEnSesion()) return RedirectToAction("Login", "Login");
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            var usuarioId = ObtenerUsuarioID();
            if (!await UsuarioEsOperadorAsync(usuarioId, cn)) return AccesoDenegadoOperador();
            if (ejecucionProduccionId <= 0)
            {
                TempData["Error"] = "No se recibió la ejecución de producción.";
                return RedirectToAction(nameof(Index));
            }
            if (cantidadPiezas <= 0)
            {
                TempData["Error"] = "La cantidad de piezas de la caja debe ser mayor a cero.";
                return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
            }
            var tipoNormalizado = NormalizarTipoCajaOperador(tipoCaja);
            if (string.IsNullOrWhiteSpace(tipoNormalizado))
            {
                TempData["Error"] = "El tipo de caja no es válido.";
                return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
            }
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                var ejecucion = await ObtenerEjecucionOperadorAsync(ejecucionProduccionId, cn, tx);
                if (ejecucion == null)
                {
                    await tx.RollbackAsync();
                    return NotFound();
                }
                if (ejecucion.EstatusID != ProduccionEstatus.EnProduccion)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "Solo puedes formar cajas cuando la producción está en serie.";
                    return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
                }
                if (await TieneParoAbiertoAsync(ejecucionProduccionId, cn, tx))
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "No puedes formar cajas mientras exista un paro abierto.";
                    return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
                }
                decimal? piezasPorEmbalaje = null;
                decimal? cantidadEmbalajes = null;
                if (ejecucion.SolicitudProduccionDetalleID.HasValue && ejecucion.SolicitudProduccionDetalleID.Value > 0)
                {
                    const string sqlEmbalaje = @"
SELECT TOP (1)
    PiezasPorEmbalaje,
    CantidadEmbalajes
FROM dbo.SolicitudesProduccionDetalle WITH (UPDLOCK,HOLDLOCK)
WHERE SolicitudProduccionDetalleID=@SolicitudProduccionDetalleID
  AND Activo=1;";
                    await using var cmdEmbalaje = new SqlCommand(sqlEmbalaje, cn, tx);
                    cmdEmbalaje.Parameters.Add("@SolicitudProduccionDetalleID", SqlDbType.Int).Value = ejecucion.SolicitudProduccionDetalleID.Value;
                    await using var rd = await cmdEmbalaje.ExecuteReaderAsync();
                    if (await rd.ReadAsync())
                    {
                        piezasPorEmbalaje = rd["PiezasPorEmbalaje"] == DBNull.Value ? null : Convert.ToDecimal(rd["PiezasPorEmbalaje"]);
                        cantidadEmbalajes = rd["CantidadEmbalajes"] == DBNull.Value ? null : Convert.ToDecimal(rd["CantidadEmbalajes"]);
                    }
                }
                if (tipoNormalizado == "OK" && piezasPorEmbalaje.HasValue && piezasPorEmbalaje.Value > 0 && cantidadPiezas > piezasPorEmbalaje.Value)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = $"La caja excede la capacidad del embalaje. Máximo permitido: {piezasPorEmbalaje.Value:N0} pieza(s) por caja.";
                    return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
                }
                var capturadoDisponible = await ObtenerCantidadDisponibleParaCajaAsync(ejecucionProduccionId, tipoNormalizado, cn, tx);
                if (cantidadPiezas > capturadoDisponible)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "No puedes formar la caja porque la cantidad excede lo capturado disponible para el tipo " + tipoNormalizado + ". Disponible: " + capturadoDisponible.ToString("N0") + " pieza(s).";
                    return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
                }
                if (tipoNormalizado == "OK")
                {
                    const string sqlTotales = @"
SELECT
    COUNT(1) AS CajasFormadas,
    ISNULL(SUM(ISNULL(CantidadPiezas,ISNULL(Cantidad,0))),0) AS PiezasEnCajas
FROM dbo.Produccion_Cajas WITH (UPDLOCK,HOLDLOCK)
WHERE EjecucionProduccionID=@EjecucionProduccionID
  AND Activo=1
  AND UPPER(LTRIM(RTRIM(ISNULL(TipoCaja,N'OK'))))=N'OK';";
                    int cajasFormadas;
                    int piezasEnCajas;
                    await using (var cmdTotales = new SqlCommand(sqlTotales, cn, tx))
                    {
                        cmdTotales.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
                        await using var rd = await cmdTotales.ExecuteReaderAsync();
                        await rd.ReadAsync();
                        cajasFormadas = Convert.ToInt32(rd["CajasFormadas"]);
                        piezasEnCajas = Convert.ToInt32(rd["PiezasEnCajas"]);
                    }
                    if (ejecucion.CantidadPlaneada.HasValue && ejecucion.CantidadPlaneada.Value > 0 && piezasEnCajas + cantidadPiezas > ejecucion.CantidadPlaneada.Value)
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] = $"La caja excedería la cantidad planeada. Planeado: {ejecucion.CantidadPlaneada.Value:N0}; actualmente en cajas: {piezasEnCajas:N0}.";
                        return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
                    }
                    if (cantidadEmbalajes.HasValue && cantidadEmbalajes.Value > 0)
                    {
                        var cajasEsperadas = Convert.ToInt32(Math.Ceiling(cantidadEmbalajes.Value));
                        if (cajasFormadas >= cajasEsperadas)
                        {
                            await tx.RollbackAsync();
                            TempData["Error"] = $"Ya se formaron las {cajasEsperadas:N0} caja(s)/embalaje(s) esperadas para esta orden.";
                            return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
                        }
                    }
                }
                var siguienteNumero = await ObtenerSiguienteNumeroCajaAsync(ejecucionProduccionId, cn, tx);
                var folioCaja = CrearFolioCajaOperador(ejecucion, siguienteNumero);
                const string sqlInsert = @"
INSERT INTO dbo.Produccion_Cajas
(
    EjecucionProduccionID,
    ProgramaProduccionID,
    SolicitudProduccionID,
    SolicitudProduccionDetalleID,
    ReleaseID,
    ReleaseDetalleID,
    NumeroCaja,
    FolioCaja,
    CantidadPiezas,
    TipoCaja,
    LoteMaterial,
    EtiquetaFolio,
    EstadoCajaID,
    EstadoCajaNombre,
    EtiquetaVerde,
    FechaFormacion,
    UsuarioFormacionID,
    Observaciones,
    Activo,
    UsuarioCreacionID,
    FechaCreacion,
    Etiqueta,
    Cantidad,
    EstatusCalidad,
    OperadorUsuarioID
)
VALUES
(
    @EjecucionProduccionID,
    @ProgramaProduccionID,
    @SolicitudProduccionID,
    @SolicitudProduccionDetalleID,
    @ReleaseID,
    @ReleaseDetalleID,
    @NumeroCaja,
    @FolioCaja,
    @CantidadPiezas,
    @TipoCaja,
    NULL,
    NULL,
    @EstadoCajaID,
    @EstadoCajaNombre,
    0,
    GETDATE(),
    @UsuarioID,
    @Observaciones,
    1,
    @UsuarioID,
    GETDATE(),
    @EtiquetaCompatibilidad,
    @CantidadCompatibilidad,
    N'FORMADA',
    @UsuarioID
);";
                await using (var cmd = new SqlCommand(sqlInsert, cn, tx))
                {
                    cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucion.EjecucionProduccionID;
                    cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = ejecucion.ProgramaProduccionID;
                    cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = (object?)ejecucion.SolicitudProduccionID ?? DBNull.Value;
                    cmd.Parameters.Add("@SolicitudProduccionDetalleID", SqlDbType.Int).Value = (object?)ejecucion.SolicitudProduccionDetalleID ?? DBNull.Value;
                    cmd.Parameters.Add("@ReleaseID", SqlDbType.Int).Value = (object?)ejecucion.ReleaseID ?? DBNull.Value;
                    cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value = (object?)ejecucion.ReleaseDetalleID ?? DBNull.Value;
                    cmd.Parameters.Add("@NumeroCaja", SqlDbType.Int).Value = siguienteNumero;
                    cmd.Parameters.Add("@FolioCaja", SqlDbType.NVarChar, 100).Value = folioCaja;
                    cmd.Parameters.Add("@CantidadPiezas", SqlDbType.Int).Value = cantidadPiezas;
                    cmd.Parameters.Add("@TipoCaja", SqlDbType.NVarChar, 30).Value = tipoNormalizado;
                    cmd.Parameters.Add("@EstadoCajaID", SqlDbType.Int).Value = ProduccionCajaEstatus.FormadaProduccion;
                    cmd.Parameters.Add("@EstadoCajaNombre", SqlDbType.NVarChar, 100).Value = ProduccionCajaEstatus.Nombre(ProduccionCajaEstatus.FormadaProduccion);
                    cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 500).Value = string.IsNullOrWhiteSpace(observaciones) ? DBNull.Value : observaciones.Trim();
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                    cmd.Parameters.Add("@EtiquetaCompatibilidad", SqlDbType.NVarChar, 100).Value = folioCaja;
                    cmd.Parameters.Add("@CantidadCompatibilidad", SqlDbType.Int).Value = cantidadPiezas;
                    await cmd.ExecuteNonQueryAsync();
                }
                await tx.CommitAsync();
                TempData["Success"] = $"Caja {siguienteNumero:N0} formada correctamente con {cantidadPiezas:N0} pieza(s).";
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                TempData["Error"] = "No fue posible formar la caja: " + ex.Message;
            }
            return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CorregirCajaDevuelta(int cajaProduccionId, string? correccionRealizada)
        {
            if (!UsuarioEnSesion()) return RedirectToAction("Login", "Login");
            if (cajaProduccionId <= 0)
            {
                TempData["Error"] = "No se recibió una caja válida.";
                return RedirectToAction(nameof(Index));
            }

            correccionRealizada = correccionRealizada?.Trim();
            if (string.IsNullOrWhiteSpace(correccionRealizada))
            {
                TempData["Error"] = "Captura la corrección realizada antes de reenviar la caja a Calidad.";
                return RedirectToAction(nameof(Index));
            }

            if (correccionRealizada.Length > 1000)
            {
                TempData["Error"] = "La descripción de la corrección no puede superar 1000 caracteres.";
                return RedirectToAction(nameof(Index));
            }

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            var usuarioId = ObtenerUsuarioID();
            if (!await UsuarioEsOperadorAsync(usuarioId, cn)) return AccesoDenegadoOperador();

            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
            var ejecucionProduccionId = 0;

            try
            {
                const string sqlObtenerCaja = @"
SELECT TOP (1)
    c.CajaProduccionID,
    c.EjecucionProduccionID,
    ISNULL(c.NumeroCaja,0) AS NumeroCaja,
    COALESCE(NULLIF(c.FolioCaja,N''),NULLIF(c.EtiquetaFolio,N''),NULLIF(c.Etiqueta,N''),CONVERT(NVARCHAR(100),c.CajaProduccionID)) AS FolioCaja,
    ISNULL(c.EstadoCajaID,1) AS EstadoCajaID,
    UPPER(LTRIM(RTRIM(ISNULL(c.EstatusCalidad,N'')))) AS EstatusCalidad,
    ISNULL(c.MotivoCalidad,N'') AS MotivoCalidad,
    ci.InspeccionID,
    UPPER(LTRIM(RTRIM(ISNULL(ci.Estado,N'')))) AS EstadoInspeccion,
    ISNULL(ci.ConfiguracionInvalidada,0) AS ConfiguracionInvalidada
FROM dbo.Produccion_Cajas c WITH (UPDLOCK,HOLDLOCK)
OUTER APPLY
(
    SELECT TOP (1)
        i.InspeccionID,
        i.Estado,
        i.ConfiguracionInvalidada
    FROM dbo.Calidad_Inspecciones i WITH (UPDLOCK,HOLDLOCK)
    WHERE i.EjecucionProduccionID=c.EjecucionProduccionID
      AND ISNULL(i.Estado,N'')<>N'CERRADA'
    ORDER BY i.InspeccionID DESC
) ci
WHERE c.CajaProduccionID=@CajaProduccionID
  AND c.Activo=1;";

                int inspeccionId;
                int estadoCajaId;
                string estatusCalidad;
                string folioCaja;
                string motivoDevolucion;
                string estadoInspeccion;
                bool configuracionInvalidada;

                await using (var cmd = new SqlCommand(sqlObtenerCaja, cn, tx))
                {
                    cmd.Parameters.Add("@CajaProduccionID", SqlDbType.BigInt).Value = cajaProduccionId;
                    await using var rd = await cmd.ExecuteReaderAsync();

                    if (!await rd.ReadAsync())
                    {
                        await tx.RollbackAsync();
                        return NotFound();
                    }

                    ejecucionProduccionId = Convert.ToInt32(rd["EjecucionProduccionID"]);
                    estadoCajaId = Convert.ToInt32(rd["EstadoCajaID"]);
                    estatusCalidad = rd["EstatusCalidad"]?.ToString()?.Trim() ?? string.Empty;
                    folioCaja = rd["FolioCaja"]?.ToString()?.Trim() ?? cajaProduccionId.ToString();
                    motivoDevolucion = rd["MotivoCalidad"]?.ToString()?.Trim() ?? string.Empty;
                    estadoInspeccion = rd["EstadoInspeccion"]?.ToString()?.Trim() ?? string.Empty;
                    configuracionInvalidada = Convert.ToBoolean(rd["ConfiguracionInvalidada"]);

                    if (rd["InspeccionID"] == DBNull.Value)
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] = "No existe una inspección activa de Calidad relacionada con esta caja.";
                        return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
                    }

                    inspeccionId = Convert.ToInt32(rd["InspeccionID"]);
                }

                if (estadoCajaId != ProduccionCajaEstatus.FormadaProduccion || estatusCalidad != "DEVUELTA")
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "Solamente pueden corregirse cajas devueltas por Calidad.";
                    return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
                }

                if (configuracionInvalidada)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "La configuración de Calidad está invalidada. Primero debe corregirse la configuración de la ejecución.";
                    return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
                }

                if (estadoInspeccion == "PENDIENTE_RELIBERACION")
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "La ejecución tiene una reliberación pendiente. La caja no puede corregirse para reenvío hasta que Calidad autorice el reinicio.";
                    return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
                }

                var comentarioCorreccion = $"Producción corrigió la caja {folioCaja}. Motivo de devolución: {(string.IsNullOrWhiteSpace(motivoDevolucion) ? "No especificado" : motivoDevolucion)}. Corrección realizada: {correccionRealizada}";
                if (comentarioCorreccion.Length > 1000) comentarioCorreccion = comentarioCorreccion[..1000];

                const string sqlActualizar = @"
UPDATE dbo.Produccion_Cajas
SET EstatusCalidad=N'CORREGIDA',
    EstadoCajaID=@EstadoCajaID,
    EstadoCajaNombre=@EstadoCajaNombre,
    EtiquetaVerde=0,
    FechaLiberacionCalidad=NULL,
    AuditorCalidadUsuarioID=NULL,
    UsuarioCalidadID=NULL,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=GETDATE()
WHERE CajaProduccionID=@CajaProduccionID
  AND EjecucionProduccionID=@EjecucionProduccionID
  AND Activo=1
  AND EstadoCajaID=@EstadoCajaID
  AND UPPER(LTRIM(RTRIM(ISNULL(EstatusCalidad,N''))))=N'DEVUELTA';

IF @@ROWCOUNT<>1
    THROW 51070,'La caja cambió de estado mientras se registraba la corrección.',1;

INSERT INTO dbo.Calidad_InspeccionHistorial
(
    InspeccionID,
    Movimiento,
    EstadoAnterior,
    EstadoNuevo,
    ResultadoCalidad,
    Etiqueta,
    Comentario,
    UsuarioID,
    FechaMovimiento
)
SELECT
    @InspeccionID,
    N'CAJA_CORREGIDA_PRODUCCION',
    @EstadoInspeccion,
    @EstadoInspeccion,
    N'CORREGIDA',
    NULL,
    @Comentario,
    @UsuarioID,
    GETDATE()
WHERE NOT EXISTS
(
    SELECT 1
    FROM dbo.Calidad_InspeccionHistorial h
    WHERE h.InspeccionID=@InspeccionID
      AND h.Movimiento=N'CAJA_CORREGIDA_PRODUCCION'
      AND h.Comentario LIKE N'%caja '+@FolioCaja+N'%'
      AND h.Comentario LIKE N'%'+@CorreccionRealizada+N'%'
);";

                await using (var cmd = new SqlCommand(sqlActualizar, cn, tx))
                {
                    cmd.Parameters.Add("@CajaProduccionID", SqlDbType.BigInt).Value = cajaProduccionId;
                    cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
                    cmd.Parameters.Add("@InspeccionID", SqlDbType.Int).Value = inspeccionId;
                    cmd.Parameters.Add("@EstadoCajaID", SqlDbType.Int).Value = ProduccionCajaEstatus.FormadaProduccion;
                    cmd.Parameters.Add("@EstadoCajaNombre", SqlDbType.NVarChar, 100).Value = ProduccionCajaEstatus.Nombre(ProduccionCajaEstatus.FormadaProduccion);
                    cmd.Parameters.Add("@EstadoInspeccion", SqlDbType.NVarChar, 50).Value = estadoInspeccion;
                    cmd.Parameters.Add("@Comentario", SqlDbType.NVarChar, 1000).Value = comentarioCorreccion;
                    cmd.Parameters.Add("@FolioCaja", SqlDbType.NVarChar, 100).Value = folioCaja;
                    cmd.Parameters.Add("@CorreccionRealizada", SqlDbType.NVarChar, 1000).Value = correccionRealizada;
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                    await cmd.ExecuteNonQueryAsync();
                }

                await tx.CommitAsync();
                TempData["Success"] = $"Corrección de la caja {folioCaja} registrada. Ya puede reenviarse a Calidad.";
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                TempData["Error"] = "No fue posible registrar la corrección de la caja: " + ex.Message;
            }

            return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SolicitarLiberacionCaja(int cajaProduccionId)
        {
            if (!UsuarioEnSesion()) return RedirectToAction("Login", "Login");
            if (cajaProduccionId <= 0)
            {
                TempData["Error"] = "No se recibió una caja válida.";
                return RedirectToAction(nameof(Index));
            }

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            var usuarioId = ObtenerUsuarioID();
            if (!await UsuarioEsOperadorAsync(usuarioId, cn)) return AccesoDenegadoOperador();

            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
            var ejecucionProduccionId = 0;

            try
            {
                var caja = await ObtenerCajaOperadorAsync(cajaProduccionId, cn, tx);
                if (caja == null)
                {
                    await tx.RollbackAsync();
                    return NotFound();
                }

                ejecucionProduccionId = caja.EjecucionProduccionID;

                if (caja.EstadoCajaID == ProduccionCajaEstatus.PendienteCalidad)
                {
                    await tx.CommitAsync();
                    TempData["Info"] = "La caja ya se encuentra pendiente de revisión de Calidad.";
                    return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
                }

                if (caja.EstadoCajaID != ProduccionCajaEstatus.FormadaProduccion)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "Solo puedes solicitar liberación de una caja formada en Producción.";
                    return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
                }

                const string sqlEstadoCaja = @"
SELECT TOP (1)
    UPPER(LTRIM(RTRIM(ISNULL(EstatusCalidad,N'')))) AS EstatusCalidad,
    ISNULL(MotivoCalidad,N'') AS MotivoCalidad
FROM dbo.Produccion_Cajas WITH (UPDLOCK,HOLDLOCK)
WHERE CajaProduccionID=@CajaProduccionID
  AND EjecucionProduccionID=@EjecucionProduccionID
  AND Activo=1;";

                string estatusCalidad;
                string motivoCalidad;

                await using (var cmd = new SqlCommand(sqlEstadoCaja, cn, tx))
                {
                    cmd.Parameters.Add("@CajaProduccionID", SqlDbType.BigInt).Value = cajaProduccionId;
                    cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
                    await using var rd = await cmd.ExecuteReaderAsync();

                    if (!await rd.ReadAsync())
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] = "No fue posible consultar el estado de Calidad de la caja.";
                        return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
                    }

                    estatusCalidad = rd["EstatusCalidad"]?.ToString()?.Trim() ?? string.Empty;
                    motivoCalidad = rd["MotivoCalidad"]?.ToString()?.Trim() ?? string.Empty;
                }

                if (estatusCalidad == "DEVUELTA")
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = string.IsNullOrWhiteSpace(motivoCalidad)
                        ? "La caja fue devuelta por Calidad. Registra la corrección realizada antes de reenviarla."
                        : $"La caja fue devuelta por Calidad: {motivoCalidad}. Registra la corrección realizada antes de reenviarla.";
                    return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
                }

                if (!string.IsNullOrWhiteSpace(estatusCalidad) && estatusCalidad != "CORREGIDA" && estatusCalidad != "FORMADA")
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = $"La caja no puede enviarse a Calidad porque actualmente tiene el estatus {estatusCalidad}.";
                    return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
                }

                var validacion = await ValidarEnvioCajaCalidadAsync(ejecucionProduccionId, cn, tx);
                if (!validacion.Permitido || !validacion.InspeccionID.HasValue)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = validacion.Mensaje;
                    return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
                }

                var esReenvio = estatusCalidad == "CORREGIDA";
                var movimiento = esReenvio ? "CAJA_REENVIADA_DESDE_PRODUCCION" : "CAJA_RECIBIDA_DESDE_PRODUCCION";
                var comentario = esReenvio
                    ? $"Producción reenvió la caja {caja.FolioCaja} después de registrar su corrección. Cantidad: {caja.CantidadPiezas} pieza(s). Tipo: {caja.TipoCaja}."
                    : $"Producción envió la caja {caja.FolioCaja} a Calidad. Cantidad: {caja.CantidadPiezas} pieza(s). Tipo: {caja.TipoCaja}.";

                const string sql = @"
UPDATE dbo.Produccion_Cajas
SET EstadoCajaID=@EstadoCajaID,
    EstadoCajaNombre=@EstadoCajaNombre,
    FechaSolicitudCalidad=GETDATE(),
    UsuarioSolicitudCalidadID=@UsuarioID,
    EstatusCalidad=N'PENDIENTE',
    EtiquetaVerde=0,
    FechaLiberacionCalidad=NULL,
    AuditorCalidadUsuarioID=NULL,
    UsuarioCalidadID=NULL,
    ResultadoCalidad=NULL,
    MotivoCalidad=NULL,
    FechaZonaVerde=NULL,
    UsuarioZonaVerdeID=NULL,
    FechaSalidaProduccion=NULL,
    UsuarioSalidaProduccionID=NULL,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=GETDATE()
WHERE CajaProduccionID=@CajaProduccionID
  AND EjecucionProduccionID=@EjecucionProduccionID
  AND Activo=1
  AND EstadoCajaID=@EstadoActual
  AND
  (
      UPPER(LTRIM(RTRIM(ISNULL(EstatusCalidad,N'')))) IN (N'',N'FORMADA',N'CORREGIDA')
  );

IF @@ROWCOUNT<>1
    THROW 51060,'La caja cambió de estado mientras se enviaba a Calidad.',1;

INSERT INTO dbo.Calidad_InspeccionHistorial
(
    InspeccionID,
    Movimiento,
    EstadoAnterior,
    EstadoNuevo,
    ResultadoCalidad,
    Etiqueta,
    Comentario,
    UsuarioID,
    FechaMovimiento
)
SELECT
    @InspeccionID,
    @Movimiento,
    N'MONITOREO_ACTIVO',
    N'MONITOREO_ACTIVO',
    NULL,
    NULL,
    @Comentario,
    @UsuarioID,
    GETDATE()
WHERE NOT EXISTS
(
    SELECT 1
    FROM dbo.Calidad_InspeccionHistorial h
    WHERE h.InspeccionID=@InspeccionID
      AND h.Movimiento=@Movimiento
      AND h.Comentario LIKE N'%'+@FolioCaja+N'%'
      AND h.FechaMovimiento>=DATEADD(SECOND,-5,GETDATE())
);";

                await using (var cmd = new SqlCommand(sql, cn, tx))
                {
                    cmd.Parameters.Add("@CajaProduccionID", SqlDbType.BigInt).Value = cajaProduccionId;
                    cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
                    cmd.Parameters.Add("@InspeccionID", SqlDbType.Int).Value = validacion.InspeccionID.Value;
                    cmd.Parameters.Add("@EstadoCajaID", SqlDbType.Int).Value = ProduccionCajaEstatus.PendienteCalidad;
                    cmd.Parameters.Add("@EstadoCajaNombre", SqlDbType.NVarChar, 100).Value = ProduccionCajaEstatus.Nombre(ProduccionCajaEstatus.PendienteCalidad);
                    cmd.Parameters.Add("@EstadoActual", SqlDbType.Int).Value = ProduccionCajaEstatus.FormadaProduccion;
                    cmd.Parameters.Add("@Movimiento", SqlDbType.NVarChar, 100).Value = movimiento;
                    cmd.Parameters.Add("@Comentario", SqlDbType.NVarChar, 1000).Value = comentario.Length > 1000 ? comentario[..1000] : comentario;
                    cmd.Parameters.Add("@FolioCaja", SqlDbType.NVarChar, 100).Value = string.IsNullOrWhiteSpace(caja.FolioCaja) ? cajaProduccionId.ToString() : caja.FolioCaja;
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                    await cmd.ExecuteNonQueryAsync();
                }

                await tx.CommitAsync();
                TempData["Success"] = esReenvio
                    ? "Caja corregida y reenviada a Calidad para una nueva revisión."
                    : "Caja enviada a Calidad para revisión.";
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                TempData["Error"] = "No fue posible solicitar liberación de la caja: " + ex.Message;
            }

            return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MoverCajaZonaVerde(
     int cajaProduccionId)
        {
            if (!UsuarioEnSesion())
                return RedirectToAction("Login", "Login");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            var usuarioId = ObtenerUsuarioID();

            var esOperador = await UsuarioEsOperadorAsync(usuarioId, cn);

            if (!esOperador)
                return AccesoDenegadoOperador();

            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();

            int ejecucionProduccionId = 0;

            try
            {
                var caja = await ObtenerCajaOperadorAsync(cajaProduccionId, cn, tx);

                if (caja == null)
                {
                    await tx.RollbackAsync();
                    return NotFound();
                }

                ejecucionProduccionId = caja.EjecucionProduccionID;

                if (caja.EstadoCajaID != ProduccionCajaEstatus.LiberadaCalidad || !caja.EtiquetaVerde)
                {
                    await tx.RollbackAsync();

                    TempData["Error"] =
                        "No puedes mover esta caja a zona verde. Primero debe estar liberada por Calidad con etiqueta verde.";

                    return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
                }

                const string sql = @"
UPDATE dbo.Produccion_Cajas
SET
    EstadoCajaID = @EstadoCajaID,
    EstadoCajaNombre = @EstadoCajaNombre,
    FechaZonaVerde = GETDATE(),
    UsuarioZonaVerdeID = @UsuarioID,
    UsuarioModificacionID = @UsuarioID,
    FechaModificacion = GETDATE()
WHERE CajaProduccionID = @CajaProduccionID
  AND Activo = 1;";

                await using var cmd = new SqlCommand(sql, cn, tx);

                cmd.Parameters.Add("@CajaProduccionID", SqlDbType.BigInt).Value =
                    cajaProduccionId;

                cmd.Parameters.Add("@EstadoCajaID", SqlDbType.Int).Value =
                    ProduccionCajaEstatus.ZonaVerde;

                cmd.Parameters.Add("@EstadoCajaNombre", SqlDbType.NVarChar, 100).Value =
                    ProduccionCajaEstatus.Nombre(ProduccionCajaEstatus.ZonaVerde);

                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value =
                    usuarioId;

                await cmd.ExecuteNonQueryAsync();

                await tx.CommitAsync();

                TempData["Success"] =
                    "Caja movida a zona verde.";
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();

                TempData["Error"] =
                    "No fue posible mover la caja a zona verde: " + ex.Message;
            }

            return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EscanearSalidaCaja(
     int cajaProduccionId,
     string? etiquetaEscaneada)
        {
            if (!UsuarioEnSesion())
                return RedirectToAction("Login", "Login");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            var usuarioId = ObtenerUsuarioID();

            var esOperador = await UsuarioEsOperadorAsync(usuarioId, cn);

            if (!esOperador)
                return AccesoDenegadoOperador();

            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();

            int ejecucionProduccionId = 0;

            try
            {
                var caja = await ObtenerCajaOperadorAsync(cajaProduccionId, cn, tx);

                if (caja == null)
                {
                    await tx.RollbackAsync();
                    return NotFound();
                }

                ejecucionProduccionId = caja.EjecucionProduccionID;

                if (caja.EstadoCajaID != ProduccionCajaEstatus.ZonaVerde)
                {
                    await tx.RollbackAsync();

                    TempData["Error"] =
                        "Solo puedes escanear salida de Producción cuando la caja ya está en zona verde.";

                    return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
                }

                if (!string.IsNullOrWhiteSpace(caja.EtiquetaFolio) &&
                    !string.IsNullOrWhiteSpace(etiquetaEscaneada) &&
                    !string.Equals(caja.EtiquetaFolio.Trim(), etiquetaEscaneada.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    await tx.RollbackAsync();

                    TempData["Error"] =
                        "La etiqueta escaneada no coincide con la etiqueta registrada en la caja.";

                    return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
                }

                const string sql = @"
UPDATE dbo.Produccion_Cajas
SET
    EstadoCajaID = @EstadoCajaID,
    EstadoCajaNombre = @EstadoCajaNombre,
    FechaSalidaProduccion = GETDATE(),
    UsuarioSalidaProduccionID = @UsuarioID,
    UsuarioModificacionID = @UsuarioID,
    FechaModificacion = GETDATE()
WHERE CajaProduccionID = @CajaProduccionID
  AND Activo = 1;";

                await using var cmd = new SqlCommand(sql, cn, tx);

                cmd.Parameters.Add("@CajaProduccionID", SqlDbType.BigInt).Value =
                    cajaProduccionId;

                cmd.Parameters.Add("@EstadoCajaID", SqlDbType.Int).Value =
                    ProduccionCajaEstatus.SalidaProduccion;

                cmd.Parameters.Add("@EstadoCajaNombre", SqlDbType.NVarChar, 100).Value =
                    ProduccionCajaEstatus.Nombre(ProduccionCajaEstatus.SalidaProduccion);

                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value =
                    usuarioId;

                await cmd.ExecuteNonQueryAsync();

                await tx.CommitAsync();

                TempData["Success"] =
                    "Salida de Producción escaneada correctamente. Pendiente recepción de Almacén PT.";
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();

                TempData["Error"] =
                    "No fue posible escanear la salida de Producción: " + ex.Message;
            }

            return RedirectToAction(nameof(Cajas), new { id = ejecucionProduccionId });
        }

        private async Task<ProduccionOperadorCajasVm?> ObtenerCajasOperadorVmAsync(int ejecucionProduccionId, SqlConnection cn)
        {
            const string sql = @"
SELECT TOP (1)
    e.EjecucionProduccionID,
    e.ProgramaProduccionID,
    e.SolicitudProduccionID,
    e.SolicitudProduccionDetalleID,
    s.FolioSolicitud,
    s.NumeroOFRecibida,
    pp.ClienteNombre,
    e.MaquinaCodigo,
    e.MaquinaNombre,
    e.NumeroParte,
    e.ReferenciaSAP,
    e.DescripcionParte,
    e.MoldeCodigo,
    COALESCE(NULLIF(d.MaterialCodigo,N''),NULLIF(pp.MaterialCodigo,N'')) AS MaterialCodigo,
    COALESCE(NULLIF(d.MaterialDescripcion,N''),NULLIF(pp.MaterialDescripcion,N'')) AS MaterialDescripcion,
    COALESCE(NULLIF(d.EmbalajeCodigo,N''),NULLIF(pp.EmbalajeCodigo,N'')) AS EmbalajeCodigo,
    COALESCE(NULLIF(d.EmbalajeDescripcion,N''),NULLIF(pp.EmbalajeDescripcion,N'')) AS EmbalajeDescripcion,
    d.PiezasPorEmbalaje,
    d.CantidadEmbalajes,
    ISNULL(e.CantidadPlaneada,0) AS CantidadPlaneada,
    ISNULL(e.CantidadOKTotal,0) AS CantidadOKTotal,
    ISNULL(e.CantidadSospechosaTotal,0) AS CantidadSospechosaTotal,
    ISNULL(e.CantidadScrapTotal,0) AS CantidadScrapTotal,
    e.EstatusID,
    CASE WHEN EXISTS
    (
        SELECT 1
        FROM dbo.Produccion_Paros p
        WHERE p.EjecucionProduccionID=e.EjecucionProduccionID
          AND p.Activo=1
          AND p.FechaFinParo IS NULL
    ) THEN 1 ELSE 0 END AS TieneParoAbierto
FROM dbo.Produccion_Ejecucion e
LEFT JOIN dbo.SolicitudesProduccion s
    ON s.SolicitudProduccionID=e.SolicitudProduccionID
   AND s.Activo=1
LEFT JOIN dbo.SolicitudesProduccionDetalle d
    ON d.SolicitudProduccionDetalleID=e.SolicitudProduccionDetalleID
   AND d.Activo=1
LEFT JOIN dbo.Planeacion_ProgramaProduccion pp
    ON pp.ProgramaProduccionID=e.ProgramaProduccionID
   AND pp.Activo=1
WHERE e.EjecucionProduccionID=@EjecucionProduccionID
  AND e.Activo=1;";
            ProduccionOperadorCajasVm? vm = null;
            await using (var cmd = new SqlCommand(sql, cn))
            {
                cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
                await using var rd = await cmd.ExecuteReaderAsync();
                if (!await rd.ReadAsync()) return null;
                vm = new ProduccionOperadorCajasVm
                {
                    EjecucionProduccionID = Entero(rd, "EjecucionProduccionID"),
                    ProgramaProduccionID = Entero(rd, "ProgramaProduccionID"),
                    SolicitudProduccionID = NullableEntero(rd, "SolicitudProduccionID"),
                    FolioSolicitud = TextoNullable(rd, "FolioSolicitud"),
                    NumeroOFRecibida = TextoNullable(rd, "NumeroOFRecibida"),
                    ClienteNombre = TextoNullable(rd, "ClienteNombre"),
                    MaquinaCodigo = TextoNullable(rd, "MaquinaCodigo"),
                    MaquinaNombre = TextoNullable(rd, "MaquinaNombre"),
                    NumeroParte = TextoNullable(rd, "NumeroParte"),
                    ReferenciaSAP = TextoNullable(rd, "ReferenciaSAP"),
                    DescripcionParte = TextoNullable(rd, "DescripcionParte"),
                    MoldeCodigo = TextoNullable(rd, "MoldeCodigo"),
                    MaterialCodigo = TextoNullable(rd, "MaterialCodigo"),
                    MaterialDescripcion = TextoNullable(rd, "MaterialDescripcion"),
                    EmbalajeCodigo = TextoNullable(rd, "EmbalajeCodigo"),
                    EmbalajeDescripcion = TextoNullable(rd, "EmbalajeDescripcion"),
                    PiezasPorEmbalaje = rd["PiezasPorEmbalaje"] == DBNull.Value ? null : Convert.ToDecimal(rd["PiezasPorEmbalaje"]),
                    CantidadEmbalajes = rd["CantidadEmbalajes"] == DBNull.Value ? null : Convert.ToDecimal(rd["CantidadEmbalajes"]),
                    CantidadPlaneada = Entero(rd, "CantidadPlaneada"),
                    CantidadOKTotal = Entero(rd, "CantidadOKTotal"),
                    CantidadSospechosaTotal = Entero(rd, "CantidadSospechosaTotal"),
                    CantidadScrapTotal = Entero(rd, "CantidadScrapTotal"),
                    EstatusID = Entero(rd, "EstatusID"),
                    TieneParoAbierto = Booleano(rd, "TieneParoAbierto")
                };
            }
            vm.Cajas = await ObtenerCajasPorEjecucionAsync(ejecucionProduccionId, cn);
            vm.CantidadOKEnCajas = vm.Cajas.Where(x => string.Equals(x.TipoCaja, "OK", StringComparison.OrdinalIgnoreCase)).Sum(x => x.CantidadPiezas);
            vm.CantidadSospechosaEnCajas = vm.Cajas.Where(x => string.Equals(x.TipoCaja, "SOSPECHOSO", StringComparison.OrdinalIgnoreCase)).Sum(x => x.CantidadPiezas);
            vm.CantidadScrapEnCajas = vm.Cajas.Where(x => string.Equals(x.TipoCaja, "SCRAP", StringComparison.OrdinalIgnoreCase)).Sum(x => x.CantidadPiezas);
            vm.CantidadRetencionEnCajas = vm.Cajas.Where(x => string.Equals(x.TipoCaja, "RETENCION", StringComparison.OrdinalIgnoreCase)).Sum(x => x.CantidadPiezas);
            vm.SiguienteNumeroCaja = vm.Cajas.Any() ? vm.Cajas.Max(x => x.NumeroCaja) + 1 : 1;
            vm.PuedeFormarCaja = vm.EstatusID == ProduccionEstatus.EnProduccion && !vm.TieneParoAbierto;
            return vm;
        }


        private async Task<List<ProduccionOperadorCajaVm>> ObtenerCajasPorEjecucionAsync(
            int ejecucionProduccionId,
            SqlConnection cn)
        {
            var lista = new List<ProduccionOperadorCajaVm>();

            const string sql = @"
SELECT
    CajaProduccionID,
    EjecucionProduccionID,
    ISNULL(ProgramaProduccionID, 0) AS ProgramaProduccionID,

    ISNULL(NumeroCaja, 0) AS NumeroCaja,
    FolioCaja,

    ISNULL(CantidadPiezas, ISNULL(Cantidad, 0)) AS CantidadPiezas,
    ISNULL(TipoCaja, N'OK') AS TipoCaja,

    LoteMaterial,
    ISNULL(EtiquetaFolio, Etiqueta) AS EtiquetaFolio,

    ISNULL(EtiquetaVerde, 0) AS EtiquetaVerde,

    ISNULL(EstadoCajaID, 1) AS EstadoCajaID,
    ISNULL(EstadoCajaNombre, N'Formada en Producción') AS EstadoCajaNombre,

    ISNULL(FechaFormacion, FechaCreacion) AS FechaFormacion,
    UsuarioFormacionID,

    FechaSolicitudCalidad,
    UsuarioSolicitudCalidadID,

    FechaLiberacionCalidad,
    UsuarioCalidadID,

    ResultadoCalidad,
    MotivoCalidad,

    FechaZonaVerde,
    UsuarioZonaVerdeID,

    FechaSalidaProduccion,
    UsuarioSalidaProduccionID,

    FechaRecepcionAlmacen,
    UsuarioAlmacenID,

    Observaciones
FROM dbo.Produccion_Cajas
WHERE EjecucionProduccionID = @EjecucionProduccionID
  AND Activo = 1
ORDER BY
    NumeroCaja,
    CajaProduccionID;";

            await using var cmd = new SqlCommand(sql, cn);

            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value =
                ejecucionProduccionId;

            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                lista.Add(MapearCajaOperador(rd));
            }

            return lista;
        }

        private async Task<ProduccionOperadorCajaVm?> ObtenerCajaOperadorAsync(
            long cajaProduccionId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
SELECT TOP (1)
    CajaProduccionID,
    EjecucionProduccionID,
    ISNULL(ProgramaProduccionID, 0) AS ProgramaProduccionID,

    ISNULL(NumeroCaja, 0) AS NumeroCaja,
    FolioCaja,

    ISNULL(CantidadPiezas, ISNULL(Cantidad, 0)) AS CantidadPiezas,
    ISNULL(TipoCaja, N'OK') AS TipoCaja,

    LoteMaterial,
    ISNULL(EtiquetaFolio, Etiqueta) AS EtiquetaFolio,

    ISNULL(EtiquetaVerde, 0) AS EtiquetaVerde,

    ISNULL(EstadoCajaID, 1) AS EstadoCajaID,
    ISNULL(EstadoCajaNombre, N'Formada en Producción') AS EstadoCajaNombre,

    ISNULL(FechaFormacion, FechaCreacion) AS FechaFormacion,
    UsuarioFormacionID,

    FechaSolicitudCalidad,
    UsuarioSolicitudCalidadID,

    FechaLiberacionCalidad,
    UsuarioCalidadID,

    ResultadoCalidad,
    MotivoCalidad,

    FechaZonaVerde,
    UsuarioZonaVerdeID,

    FechaSalidaProduccion,
    UsuarioSalidaProduccionID,

    FechaRecepcionAlmacen,
    UsuarioAlmacenID,

    Observaciones
FROM dbo.Produccion_Cajas
WHERE CajaProduccionID = @CajaProduccionID
  AND Activo = 1;";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@CajaProduccionID", SqlDbType.BigInt).Value =
                cajaProduccionId;

            await using var rd = await cmd.ExecuteReaderAsync();

            if (!await rd.ReadAsync())
                return null;

            return MapearCajaOperador(rd);
        }

        private static ProduccionOperadorCajaVm MapearCajaOperador(
            SqlDataReader rd)
        {
            return new ProduccionOperadorCajaVm
            {
                CajaProduccionID = EnteroLargo(rd, "CajaProduccionID"),
                EjecucionProduccionID = Entero(rd, "EjecucionProduccionID"),
                ProgramaProduccionID = Entero(rd, "ProgramaProduccionID"),

                NumeroCaja = Entero(rd, "NumeroCaja"),
                FolioCaja = TextoNullable(rd, "FolioCaja"),

                CantidadPiezas = Entero(rd, "CantidadPiezas"),
                TipoCaja = TextoNullable(rd, "TipoCaja") ?? "OK",

                LoteMaterial = TextoNullable(rd, "LoteMaterial"),
                EtiquetaFolio = TextoNullable(rd, "EtiquetaFolio"),

                EtiquetaVerde = Booleano(rd, "EtiquetaVerde"),

                EstadoCajaID = Entero(rd, "EstadoCajaID"),
                EstadoCajaNombre =
                    TextoNullable(rd, "EstadoCajaNombre") ?? "Formada en Producción",

                FechaFormacion = Fecha(rd, "FechaFormacion"),
                UsuarioFormacionID = NullableEntero(rd, "UsuarioFormacionID"),

                FechaSolicitudCalidad = NullableFecha(rd, "FechaSolicitudCalidad"),
                UsuarioSolicitudCalidadID = NullableEntero(rd, "UsuarioSolicitudCalidadID"),

                FechaLiberacionCalidad = NullableFecha(rd, "FechaLiberacionCalidad"),
                UsuarioCalidadID = NullableEntero(rd, "UsuarioCalidadID"),

                ResultadoCalidad = TextoNullable(rd, "ResultadoCalidad"),
                MotivoCalidad = TextoNullable(rd, "MotivoCalidad"),

                FechaZonaVerde = NullableFecha(rd, "FechaZonaVerde"),
                UsuarioZonaVerdeID = NullableEntero(rd, "UsuarioZonaVerdeID"),

                FechaSalidaProduccion = NullableFecha(rd, "FechaSalidaProduccion"),
                UsuarioSalidaProduccionID = NullableEntero(rd, "UsuarioSalidaProduccionID"),

                FechaRecepcionAlmacen = NullableFecha(rd, "FechaRecepcionAlmacen"),
                UsuarioAlmacenID = NullableEntero(rd, "UsuarioAlmacenID"),

                Observaciones = TextoNullable(rd, "Observaciones")
            };
        }

        private async Task<int> ObtenerSiguienteNumeroCajaAsync(
    int ejecucionProduccionId,
    SqlConnection cn,
    SqlTransaction tx)
        {
            const string sql = @"
SELECT ISNULL(MAX(NumeroCaja), 0) + 1
FROM dbo.Produccion_Cajas
WHERE EjecucionProduccionID = @EjecucionProduccionID
  AND Activo = 1;";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value =
                ejecucionProduccionId;

            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        private async Task<int> ObtenerCantidadDisponibleParaCajaAsync(
            int ejecucionProduccionId,
            string tipoCaja,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
SELECT
    ISNULL(e.CantidadOKTotal, 0) AS OKTotal,
    ISNULL(e.CantidadSospechosaTotal, 0) AS SospechosaTotal,
    ISNULL(e.CantidadScrapTotal, 0) AS ScrapTotal,

    ISNULL((
        SELECT SUM(ISNULL(c.CantidadPiezas, ISNULL(c.Cantidad, 0)))
        FROM dbo.Produccion_Cajas c
        WHERE c.EjecucionProduccionID = e.EjecucionProduccionID
          AND c.Activo = 1
          AND ISNULL(c.TipoCaja, N'OK') = @TipoCaja
    ), 0) AS YaEnCajas
FROM dbo.Produccion_Ejecucion e
WHERE e.EjecucionProduccionID = @EjecucionProduccionID
  AND e.Activo = 1;";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value =
                ejecucionProduccionId;

            cmd.Parameters.Add("@TipoCaja", SqlDbType.NVarChar, 30).Value =
                tipoCaja;

            await using var rd = await cmd.ExecuteReaderAsync();

            if (!await rd.ReadAsync())
                return 0;

            var totalBase = 0;

            if (tipoCaja == "OK")
                totalBase = Entero(rd, "OKTotal");
            else if (tipoCaja == "SOSPECHOSO")
                totalBase = Entero(rd, "SospechosaTotal");
            else if (tipoCaja == "SCRAP")
                totalBase = Entero(rd, "ScrapTotal");
            else if (tipoCaja == "RETENCION")
                totalBase = Entero(rd, "SospechosaTotal");

            var yaEnCajas = Entero(rd, "YaEnCajas");

            var disponible = totalBase - yaEnCajas;

            return disponible < 0 ? 0 : disponible;
        }

        private static string NormalizarTipoCajaOperador(string? tipoCaja)
        {
            var valor = string.IsNullOrWhiteSpace(tipoCaja)
                ? ""
                : tipoCaja.Trim().ToUpperInvariant();

            if (valor == "OK")
                return "OK";

            if (valor == "SOSPECHOSA" || valor == "SOSPECHOSO")
                return "SOSPECHOSO";

            if (valor == "SCRAP")
                return "SCRAP";

            if (valor == "RETENCION" || valor == "RETENCIÓN")
                return "RETENCION";

            return "";
        }

        private static string CrearFolioCajaOperador(ProduccionEjecucionVm ejecucion, int numeroCaja)
        {
            if (ejecucion == null) throw new ArgumentNullException(nameof(ejecucion));
            if (ejecucion.EjecucionProduccionID <= 0) throw new InvalidOperationException("La ejecución de Producción no es válida.");
            if (numeroCaja <= 0) throw new ArgumentOutOfRangeException(nameof(numeroCaja));
            return $"PROD-{ejecucion.EjecucionProduccionID}-C{numeroCaja:000}";
        }

        private async Task CrearOActualizarSolicitudReliberacionCalidadPorParoAsync(int ejecucionProduccionId, int paroId, int duracionMinutos, int usuarioId, SqlConnection cn, SqlTransaction tx)
        {
            if (ejecucionProduccionId <= 0) throw new ArgumentException("La ejecución de producción no es válida.", nameof(ejecucionProduccionId));
            if (paroId <= 0) throw new ArgumentException("El paro de producción no es válido.", nameof(paroId));
            if (duracionMinutos <= 15) throw new InvalidOperationException("Solo se debe solicitar reliberación cuando el paro sea mayor a 15 minutos.");
            if (usuarioId <= 0) throw new InvalidOperationException("No se pudo identificar al usuario que solicita la reliberación.");

            const string sqlObtenerInspeccion = @"
SELECT TOP (1)
    ci.InspeccionID,
    ISNULL(ci.Estado,N'') AS Estado,
    ci.ChecklistArranqueID,
    ISNULL(ci.ConfiguracionInvalidada,0) AS ConfiguracionInvalidada
FROM dbo.Calidad_Inspecciones ci WITH (UPDLOCK,HOLDLOCK)
WHERE ci.EjecucionProduccionID=@EjecucionProduccionID
  AND ISNULL(ci.Estado,N'')<>N'CERRADA'
ORDER BY ci.InspeccionID DESC;";

            int inspeccionId;
            string estadoAnterior;
            int? checklistArranqueId;
            bool configuracionInvalidada;

            await using (var cmd = new SqlCommand(sqlObtenerInspeccion, cn, tx))
            {
                cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
                await using var rd = await cmd.ExecuteReaderAsync();
                if (!await rd.ReadAsync())
                {
                    throw new InvalidOperationException("No existe una inspección activa de Calidad asociada con esta ejecución. Primero debe completarse y enviarse el checklist de arranque.");
                }

                inspeccionId = Convert.ToInt32(rd["InspeccionID"]);
                estadoAnterior = rd["Estado"]?.ToString()?.Trim() ?? string.Empty;
                checklistArranqueId = rd["ChecklistArranqueID"] == DBNull.Value ? null : Convert.ToInt32(rd["ChecklistArranqueID"]);
                configuracionInvalidada = Convert.ToBoolean(rd["ConfiguracionInvalidada"]);
            }

            if (!checklistArranqueId.HasValue || checklistArranqueId.Value <= 0)
            {
                throw new InvalidOperationException("La inspección de Calidad no tiene un checklist de arranque relacionado.");
            }

            if (configuracionInvalidada)
            {
                throw new InvalidOperationException("La configuración de la inspección fue invalidada por un cambio de Planeación. Debe corregirse esa condición antes de solicitar una reliberación por paro.");
            }

            const string sqlValidarParo = @"
SELECT COUNT(1)
FROM dbo.Produccion_Paros WITH (UPDLOCK,HOLDLOCK)
WHERE ParoID=@ParoID
  AND EjecucionProduccionID=@EjecucionProduccionID
  AND Activo=1
  AND FechaFinParo IS NOT NULL
  AND ISNULL(EsMayorA15Minutos,0)=1;";

            await using (var cmd = new SqlCommand(sqlValidarParo, cn, tx))
            {
                cmd.Parameters.Add("@ParoID", SqlDbType.Int).Value = paroId;
                cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
                var total = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                if (total <= 0)
                {
                    throw new InvalidOperationException("El paro no está cerrado, no pertenece a la ejecución o no fue marcado como mayor a 15 minutos.");
                }
            }

            var observacion = $"Solicitud automática de reliberación por paro mayor a 15 minutos. ParoID: {paroId}. Duración registrada: {duracionMinutos} minuto(s).";
            var observacionCancelacion = $"Monitoreo cancelado por interrupción del ciclo. ParoID: {paroId}. Duración: {duracionMinutos} minuto(s). Se generará un nuevo periodo cuando Producción reinicie la serie.";

            const string sqlActualizarInspeccion = @"
UPDATE dbo.Calidad_Inspecciones
SET RequiereReliberacion=1,
    Liberado=0,
    Estado=N'PENDIENTE_RELIBERACION',
    ResultadoCalidad=NULL,
    Etiqueta=NULL,
    CincoDisparosSegregados=0,
    CantidadDisparosConformes=0,
    ValidacionDimensional=NULL,
    ValidacionApariencia=NULL,
    ValidacionGauge=NULL,
    ValidacionConductividad=NULL,
    FechaNotificacionCalidad=GETDATE(),
    UsuarioNotificoID=@UsuarioID,
    MotivoDevolucion=N'Paro mayor a 15 minutos. Se requieren cinco disparos y reliberación de Calidad.',
    Observaciones=CASE
        WHEN Observaciones IS NULL OR LTRIM(RTRIM(Observaciones))=N'' THEN @Observacion
        WHEN Observaciones LIKE N'%ParoID: '+CONVERT(NVARCHAR(20),@ParoID)+N'%' THEN Observaciones
        ELSE Observaciones+CHAR(13)+CHAR(10)+@Observacion
    END,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=GETDATE()
WHERE InspeccionID=@InspeccionID
  AND EjecucionProduccionID=@EjecucionProduccionID
  AND ISNULL(Estado,N'')<>N'CERRADA';";

            await using (var cmd = new SqlCommand(sqlActualizarInspeccion, cn, tx))
            {
                cmd.Parameters.Add("@InspeccionID", SqlDbType.Int).Value = inspeccionId;
                cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
                cmd.Parameters.Add("@ParoID", SqlDbType.Int).Value = paroId;
                cmd.Parameters.Add("@Observacion", SqlDbType.NVarChar, 1000).Value = observacion;
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                var actualizadas = await cmd.ExecuteNonQueryAsync();
                if (actualizadas != 1)
                {
                    throw new InvalidOperationException("No fue posible marcar la inspección como pendiente de reliberación.");
                }
            }
            const string sqlCancelarMonitoreos = @"
UPDATE dbo.Calidad_MonitoreosProceso
SET
    /*
     * Resultado se conserva como PENDIENTE porque CANCELADO
     * no pertenece al catálogo permitido por la restricción
     * CK_CalidadMonitoreos_Resultado.
     *
     * Activo = 0 es lo que retira el periodo del flujo vigente.
     */
    Observaciones =
        CASE
            WHEN Observaciones IS NULL
              OR LTRIM(RTRIM(Observaciones)) = N''
                THEN @ObservacionCancelacion

            WHEN Observaciones LIKE
                 N'%ParoID: '
                 + CONVERT(NVARCHAR(20), @ParoID)
                 + N'%'
                THEN Observaciones

            ELSE
                Observaciones
                + CHAR(13)
                + CHAR(10)
                + @ObservacionCancelacion
        END,

    Activo = 0,
    UsuarioModificacionID = @UsuarioID,
    FechaModificacion = GETDATE()

WHERE InspeccionID = @InspeccionID
  AND EjecucionProduccionID = @EjecucionProduccionID
  AND Activo = 1
  AND RegistroHoraID IS NULL
  AND UPPER(
        LTRIM(
            RTRIM(
                ISNULL(Resultado, N'')
            )
        )
      ) = N'PENDIENTE';";

            int monitoreosCancelados;
            await using (var cmd = new SqlCommand(sqlCancelarMonitoreos, cn, tx))
            {
                cmd.Parameters.Add("@InspeccionID", SqlDbType.Int).Value = inspeccionId;
                cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
                cmd.Parameters.Add("@ParoID", SqlDbType.Int).Value = paroId;
                cmd.Parameters.Add("@ObservacionCancelacion", SqlDbType.NVarChar, 1000).Value = observacionCancelacion;
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                monitoreosCancelados = await cmd.ExecuteNonQueryAsync();
            }

            const string sqlObtenerReliberacion = @"
SELECT TOP (1) ReliberacionID
FROM dbo.Calidad_Reliberaciones WITH (UPDLOCK,HOLDLOCK)
WHERE InspeccionID=@InspeccionID
  AND EjecucionProduccionID=@EjecucionProduccionID
  AND ParoID=@ParoID
  AND Activo=1
ORDER BY ReliberacionID DESC;";

            int? reliberacionId;
            await using (var cmd = new SqlCommand(sqlObtenerReliberacion, cn, tx))
            {
                cmd.Parameters.Add("@InspeccionID", SqlDbType.Int).Value = inspeccionId;
                cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
                cmd.Parameters.Add("@ParoID", SqlDbType.Int).Value = paroId;
                var resultado = await cmd.ExecuteScalarAsync();
                reliberacionId = resultado == null || resultado == DBNull.Value ? null : Convert.ToInt32(resultado);
            }

            if (reliberacionId.HasValue)
            {
                const string sqlActualizarReliberacion = @"
UPDATE dbo.Calidad_Reliberaciones
SET Resultado=N'PENDIENTE',
    FechaSolicitud=GETDATE(),
    FechaValidacion=NULL,
    UsuarioSolicitudID=@UsuarioID,
    UsuarioCalidadID=NULL,
    Observaciones=CASE
        WHEN Observaciones IS NULL OR LTRIM(RTRIM(Observaciones))=N'' THEN @Observacion
        WHEN Observaciones LIKE N'%ParoID: '+CONVERT(NVARCHAR(20),@ParoID)+N'%' THEN Observaciones
        ELSE Observaciones+CHAR(13)+CHAR(10)+@Observacion
    END,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=GETDATE()
WHERE ReliberacionID=@ReliberacionID
  AND Activo=1;";

                await using var cmd = new SqlCommand(sqlActualizarReliberacion, cn, tx);
                cmd.Parameters.Add("@ReliberacionID", SqlDbType.Int).Value = reliberacionId.Value;
                cmd.Parameters.Add("@ParoID", SqlDbType.Int).Value = paroId;
                cmd.Parameters.Add("@Observacion", SqlDbType.NVarChar, 1000).Value = observacion;
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                var actualizadas = await cmd.ExecuteNonQueryAsync();
                if (actualizadas != 1)
                {
                    throw new InvalidOperationException("No fue posible reactivar la solicitud de reliberación.");
                }
            }
            else
            {
                const string sqlInsertarReliberacion = @"
DECLARE @NumeroReliberacion INT;

SELECT @NumeroReliberacion=ISNULL(MAX(NumeroReliberacion),0)+1
FROM dbo.Calidad_Reliberaciones WITH (UPDLOCK,HOLDLOCK)
WHERE EjecucionProduccionID=@EjecucionProduccionID;

INSERT INTO dbo.Calidad_Reliberaciones
(
    InspeccionID,
    EjecucionProduccionID,
    ParoID,
    NumeroReliberacion,
    Motivo,
    FechaSolicitud,
    UsuarioSolicitudID,
    Resultado,
    Observaciones,
    UsuarioCreacionID,
    FechaCreacion,
    Activo
)
VALUES
(
    @InspeccionID,
    @EjecucionProduccionID,
    @ParoID,
    @NumeroReliberacion,
    @Motivo,
    GETDATE(),
    @UsuarioID,
    N'PENDIENTE',
    @Observaciones,
    @UsuarioID,
    GETDATE(),
    1
);";

                await using var cmd = new SqlCommand(sqlInsertarReliberacion, cn, tx);
                cmd.Parameters.Add("@InspeccionID", SqlDbType.Int).Value = inspeccionId;
                cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
                cmd.Parameters.Add("@ParoID", SqlDbType.Int).Value = paroId;
                cmd.Parameters.Add("@Motivo", SqlDbType.NVarChar, 500).Value = $"Paro mayor a 15 minutos. Duración registrada: {duracionMinutos} minuto(s).";
                cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 1000).Value = "Producción debe ejecutar nuevamente cinco disparos de prueba y Calidad debe autorizar la reliberación antes de reiniciar la serie.";
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                await cmd.ExecuteNonQueryAsync();
            }

            const string sqlHistorial = @"
INSERT INTO dbo.Calidad_InspeccionHistorial
(
    InspeccionID,
    Movimiento,
    EstadoAnterior,
    EstadoNuevo,
    ResultadoCalidad,
    Etiqueta,
    Comentario,
    UsuarioID,
    FechaMovimiento
)
SELECT
    @InspeccionID,
    N'SOLICITUD_RELIBERACION',
    @EstadoAnterior,
    N'PENDIENTE_RELIBERACION',
    NULL,
    NULL,
    @ComentarioReliberacion,
    @UsuarioID,
    GETDATE()
WHERE NOT EXISTS
(
    SELECT 1
    FROM dbo.Calidad_InspeccionHistorial h
    WHERE h.InspeccionID=@InspeccionID
      AND h.Movimiento=N'SOLICITUD_RELIBERACION'
      AND h.Comentario LIKE N'%ParoID: '+CONVERT(NVARCHAR(20),@ParoID)+N'%'
);

INSERT INTO dbo.Calidad_InspeccionHistorial
(
    InspeccionID,
    Movimiento,
    EstadoAnterior,
    EstadoNuevo,
    ResultadoCalidad,
    Etiqueta,
    Comentario,
    UsuarioID,
    FechaMovimiento
)
SELECT
    @InspeccionID,
    N'CICLO_MONITOREO_INTERRUMPIDO',
    @EstadoAnterior,
    N'PENDIENTE_RELIBERACION',
    NULL,
    NULL,
    @ComentarioCiclo,
    @UsuarioID,
    GETDATE()
WHERE NOT EXISTS
(
    SELECT 1
    FROM dbo.Calidad_InspeccionHistorial h
    WHERE h.InspeccionID=@InspeccionID
      AND h.Movimiento=N'CICLO_MONITOREO_INTERRUMPIDO'
      AND h.Comentario LIKE N'%ParoID: '+CONVERT(NVARCHAR(20),@ParoID)+N'%'
);";

            var comentarioReliberacion = $"{observacion} Producción regresó a preparación y queda bloqueada hasta la autorización de Calidad.";
            var comentarioCiclo = $"Ciclo de monitoreo interrumpido. ParoID: {paroId}. Se cancelaron {monitoreosCancelados} monitoreo(s) pendiente(s) sin captura. Los monitoreos vinculados o revisados conservaron su trazabilidad.";

            await using (var cmd = new SqlCommand(sqlHistorial, cn, tx))
            {
                cmd.Parameters.Add("@InspeccionID", SqlDbType.Int).Value = inspeccionId;
                cmd.Parameters.Add("@ParoID", SqlDbType.Int).Value = paroId;
                cmd.Parameters.Add("@EstadoAnterior", SqlDbType.NVarChar, 50).Value = string.IsNullOrWhiteSpace(estadoAnterior) ? DBNull.Value : estadoAnterior;
                cmd.Parameters.Add("@ComentarioReliberacion", SqlDbType.NVarChar, 1000).Value = comentarioReliberacion.Length > 1000 ? comentarioReliberacion[..1000] : comentarioReliberacion;
                cmd.Parameters.Add("@ComentarioCiclo", SqlDbType.NVarChar, 1000).Value = comentarioCiclo.Length > 1000 ? comentarioCiclo[..1000] : comentarioCiclo;
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                await cmd.ExecuteNonQueryAsync();
            }
        }
        private async Task<List<ProduccionOperadorTabletVm>>
    ObtenerProgramasEnProduccionAsync(
        int personaId,
        SqlConnection cn)
        {
            var lista = new List<ProduccionOperadorTabletVm>();

            const string sql = @"
SELECT
    e.EjecucionProduccionID,
    e.ProgramaProduccionID,
    e.SolicitudProduccionID,
    s.FolioSolicitud,
    s.NumeroOFRecibida,

    e.MaquinaID,
    e.MaquinaCodigo,
    e.MaquinaNombre,

    e.ParteID,
    e.NumeroParte,
    e.ReferenciaSAP,
    e.DescripcionParte,

    e.OperadorID,
    e.OperadorNombre,

    e.CantidadPlaneada,
    e.CantidadOKTotal,
    e.CantidadSospechosaTotal,
    e.CantidadScrapTotal,

    e.EstatusID,

    CASE
        WHEN EXISTS
        (
            SELECT 1
            FROM dbo.Produccion_Paros p
            WHERE p.EjecucionProduccionID = e.EjecucionProduccionID
              AND p.Activo = 1
              AND p.FechaFinParo IS NULL
        )
        THEN 1 ELSE 0
    END AS TieneParoAbierto,

    (
        SELECT TOP (1)
            p.ParoID
        FROM dbo.Produccion_Paros p
        WHERE p.EjecucionProduccionID = e.EjecucionProduccionID
          AND p.Activo = 1
          AND p.FechaFinParo IS NULL
        ORDER BY p.ParoID DESC
    ) AS ParoAbiertoID
FROM dbo.Produccion_Ejecucion e
LEFT JOIN dbo.SolicitudesProduccion s
    ON s.SolicitudProduccionID = e.SolicitudProduccionID
LEFT JOIN dbo.Planeacion_ProgramaProduccion pp
    ON pp.ProgramaProduccionID = e.ProgramaProduccionID
   AND pp.Activo = 1
WHERE e.Activo = 1
  AND e.EstatusID IN (@EnProduccion, @Pausado)
  AND EXISTS
  (
      SELECT 1
      FROM dbo.Planeacion_ProgramaOperadores po
      WHERE po.ProgramaProduccionID = e.ProgramaProduccionID
        AND po.PersonaID = @PersonaID
        AND po.Activo = 1
        AND UPPER(LTRIM(RTRIM(ISNULL(po.RolOperador,N''))))
            IN (N'PRINCIPAL',N'AUXILIAR')
  )
ORDER BY
    e.MaquinaCodigo,
    ISNULL(pp.FechaInicioProgramada, e.FechaInicioReal),
    e.EjecucionProduccionID;";

            await using var cmd = new SqlCommand(sql, cn);

            cmd.Parameters.Add("@EnProduccion", SqlDbType.Int).Value =
                ProduccionEstatus.EnProduccion;

            cmd.Parameters.Add("@Pausado", SqlDbType.Int).Value =
                ProduccionEstatus.Pausado;

            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                var vm = MapearTabletVm(rd);
                AsignarHoraSugerida(vm);
                lista.Add(vm);
            }

            return lista;
        }

        private async Task<ProduccionOperadorTabletVm?> ObtenerTabletVmAsync(
            int ejecucionProduccionId,
            SqlConnection cn)
        {
            const string sql = @"
SELECT
    e.EjecucionProduccionID,
    e.ProgramaProduccionID,
    e.SolicitudProduccionID,
    s.FolioSolicitud,
    s.NumeroOFRecibida,

    e.MaquinaID,
    e.MaquinaCodigo,
    e.MaquinaNombre,

    e.ParteID,
    e.NumeroParte,
    e.ReferenciaSAP,
    e.DescripcionParte,

    e.OperadorID,
    e.OperadorNombre,

    e.CantidadPlaneada,
    e.CantidadOKTotal,
    e.CantidadSospechosaTotal,
    e.CantidadScrapTotal,

    e.EstatusID,

    CASE
        WHEN EXISTS
        (
            SELECT 1
            FROM dbo.Produccion_Paros p
            WHERE p.EjecucionProduccionID = e.EjecucionProduccionID
              AND p.Activo = 1
              AND p.FechaFinParo IS NULL
        )
        THEN 1 ELSE 0
    END AS TieneParoAbierto,

    (
        SELECT TOP (1)
            p.ParoID
        FROM dbo.Produccion_Paros p
        WHERE p.EjecucionProduccionID = e.EjecucionProduccionID
          AND p.Activo = 1
          AND p.FechaFinParo IS NULL
        ORDER BY p.ParoID DESC
    ) AS ParoAbiertoID
FROM dbo.Produccion_Ejecucion e
LEFT JOIN dbo.SolicitudesProduccion s
    ON s.SolicitudProduccionID = e.SolicitudProduccionID
WHERE e.EjecucionProduccionID = @EjecucionProduccionID
  AND e.Activo = 1
  AND e.EstatusID IN (@EnProduccion, @Pausado);";

            await using var cmd = new SqlCommand(sql, cn);

            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value =
                ejecucionProduccionId;

            cmd.Parameters.Add("@EnProduccion", SqlDbType.Int).Value =
                ProduccionEstatus.EnProduccion;

            cmd.Parameters.Add("@Pausado", SqlDbType.Int).Value =
                ProduccionEstatus.Pausado;

            await using var rd = await cmd.ExecuteReaderAsync();

            if (!await rd.ReadAsync())
                return null;

            var vm = MapearTabletVm(rd);
            AsignarHoraSugerida(vm);

            return vm;
        }

        private static string? UnirTextoProduccionOperador(string? codigo, string? descripcion)
        {
            var partes = new List<string>();

            if (!string.IsNullOrWhiteSpace(codigo))
                partes.Add(codigo.Trim());

            if (!string.IsNullOrWhiteSpace(descripcion))
                partes.Add(descripcion.Trim());

            return partes.Count == 0
                ? null
                : string.Join(" - ", partes);
        }

        private async Task<ProduccionEjecucionVm?> ObtenerEjecucionOperadorAsync(
            int ejecucionProduccionId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
SELECT
    EjecucionProduccionID,
    ProgramaProduccionID,
    SolicitudProduccionID,
    SolicitudProduccionDetalleID,
    ReleaseID,
    ReleaseDetalleID,

    MaquinaID,
    MaquinaCodigo,
    MaquinaNombre,

    ParteID,
    NumeroParte,
    ReferenciaSAP,
    DescripcionParte,

    MoldeID,
    MoldeCodigo,

    OperadorID,
    OperadorNombre,

    FechaInicioReal,
    FechaFinReal,

    CantidadPlaneada,
    CantidadOKTotal,
    CantidadSospechosaTotal,
    CantidadScrapTotal,

    EstatusID,
    Observaciones,

    UsuarioCreacionID,
    FechaCreacion,
    UsuarioModificacionID,
    FechaModificacion,
    Activo
FROM dbo.Produccion_Ejecucion
WHERE EjecucionProduccionID = @EjecucionProduccionID
  AND Activo = 1;";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value =
                ejecucionProduccionId;

            await using var rd = await cmd.ExecuteReaderAsync();

            if (!await rd.ReadAsync())
                return null;

            return new ProduccionEjecucionVm
            {
                EjecucionProduccionID = Entero(rd, "EjecucionProduccionID"),
                ProgramaProduccionID = Entero(rd, "ProgramaProduccionID"),
                SolicitudProduccionID = NullableEntero(rd, "SolicitudProduccionID"),
                SolicitudProduccionDetalleID = NullableEntero(rd, "SolicitudProduccionDetalleID"),
                ReleaseID = NullableEntero(rd, "ReleaseID"),
                ReleaseDetalleID = NullableEntero(rd, "ReleaseDetalleID"),

                MaquinaID = NullableEntero(rd, "MaquinaID"),
                MaquinaCodigo = TextoNullable(rd, "MaquinaCodigo"),
                MaquinaNombre = TextoNullable(rd, "MaquinaNombre"),

                ParteID = NullableEntero(rd, "ParteID"),
                NumeroParte = TextoNullable(rd, "NumeroParte"),
                ReferenciaSAP = TextoNullable(rd, "ReferenciaSAP"),
                DescripcionParte = TextoNullable(rd, "DescripcionParte"),

                MoldeID = NullableEntero(rd, "MoldeID"),
                MoldeCodigo = TextoNullable(rd, "MoldeCodigo"),

                OperadorID = NullableEntero(rd, "OperadorID"),
                OperadorNombre = TextoNullable(rd, "OperadorNombre"),

                FechaInicioReal = NullableFecha(rd, "FechaInicioReal"),
                FechaFinReal = NullableFecha(rd, "FechaFinReal"),

                CantidadPlaneada = NullableEntero(rd, "CantidadPlaneada"),
                CantidadOKTotal = Entero(rd, "CantidadOKTotal"),
                CantidadSospechosaTotal = Entero(rd, "CantidadSospechosaTotal"),
                CantidadScrapTotal = Entero(rd, "CantidadScrapTotal"),

                EstatusID = Entero(rd, "EstatusID"),
                Observaciones = TextoNullable(rd, "Observaciones"),

                UsuarioCreacionID = NullableEntero(rd, "UsuarioCreacionID"),
                FechaCreacion = Fecha(rd, "FechaCreacion"),
                UsuarioModificacionID = NullableEntero(rd, "UsuarioModificacionID"),
                FechaModificacion = NullableFecha(rd, "FechaModificacion"),
                Activo = Booleano(rd, "Activo")
            };
        }

        private async Task<int> InsertarRegistroHoraAsync(ProduccionEjecucionVm ejecucion, ProduccionRegistroHoraPostVm vm, TimeSpan horaInicio, TimeSpan horaFin, int operadorPersonaId, int usuarioId, SqlConnection cn, SqlTransaction tx)
        {
            const string sql = @"
INSERT INTO dbo.Produccion_RegistroHora
(
    EjecucionProduccionID,
    ProgramaProduccionID,
    SolicitudProduccionID,
    MaquinaID,
    OperadorID,
    FechaProduccion,
    HoraInicio,
    HoraFin,
    CantidadOK,
    CantidadSospechosa,
    CantidadScrap,
    Observaciones,
    UsuarioCreacionID,
    FechaCreacion,
    Activo
)
OUTPUT INSERTED.RegistroHoraID
VALUES
(
    @EjecucionProduccionID,
    @ProgramaProduccionID,
    @SolicitudProduccionID,
    @MaquinaID,
    @OperadorID,
    @FechaProduccion,
    @HoraInicio,
    @HoraFin,
    @CantidadOK,
    @CantidadSospechosa,
    @CantidadScrap,
    @Observaciones,
    @UsuarioID,
    GETDATE(),
    1
);";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucion.EjecucionProduccionID;
            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = ejecucion.ProgramaProduccionID;
            cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = (object?)ejecucion.SolicitudProduccionID ?? DBNull.Value;
            cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = (object?)ejecucion.MaquinaID ?? DBNull.Value;
            cmd.Parameters.Add("@OperadorID", SqlDbType.Int).Value = operadorPersonaId;
            cmd.Parameters.Add("@FechaProduccion", SqlDbType.Date).Value = vm.FechaProduccion.Date;
            cmd.Parameters.Add("@HoraInicio", SqlDbType.Time).Value = horaInicio;
            cmd.Parameters.Add("@HoraFin", SqlDbType.Time).Value = horaFin;
            cmd.Parameters.Add("@CantidadOK", SqlDbType.Int).Value = vm.CantidadOK;
            cmd.Parameters.Add("@CantidadSospechosa", SqlDbType.Int).Value = vm.CantidadSospechosa;
            cmd.Parameters.Add("@CantidadScrap", SqlDbType.Int).Value = vm.CantidadScrap;
            cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 500).Value = string.IsNullOrWhiteSpace(vm.Observaciones) ? DBNull.Value : vm.Observaciones.Trim();
            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;

            var resultado = await cmd.ExecuteScalarAsync();
            if (resultado == null || resultado == DBNull.Value) throw new InvalidOperationException("No fue posible obtener el identificador del registro horario.");
            return Convert.ToInt32(resultado);
        }

        private static async Task VincularRegistroHoraConCalidadAsync(ProduccionEjecucionVm ejecucion, ProduccionRegistroHoraPostVm vm, TimeSpan horaInicio, TimeSpan horaFin, int registroHoraId, int usuarioId, SqlConnection cn, SqlTransaction tx)
        {
            if (ejecucion == null) throw new ArgumentNullException(nameof(ejecucion));
            if (vm == null) throw new ArgumentNullException(nameof(vm));
            if (ejecucion.EjecucionProduccionID <= 0) throw new InvalidOperationException("La ejecución de Producción no es válida.");
            if (registroHoraId <= 0) throw new InvalidOperationException("El registro horario no es válido.");
            if (usuarioId <= 0) throw new InvalidOperationException("No se pudo identificar al usuario que capturó la hora.");

            var fechaHoraInicio = vm.FechaProduccion.Date.Add(horaInicio);
            var fechaHoraFin = vm.FechaProduccion.Date.Add(horaFin);
            if (fechaHoraFin <= fechaHoraInicio) fechaHoraFin = fechaHoraFin.AddDays(1);

            var cantidadPeriodo = vm.CantidadOK + vm.CantidadSospechosa + vm.CantidadScrap;
            var cantidadPendienteRevision = vm.CantidadSospechosa + vm.CantidadScrap;
            var observacionesProduccion = $"RegistroHoraID: {registroHoraId}. OK: {vm.CantidadOK}; sospechoso: {vm.CantidadSospechosa}; scrap reportado: {vm.CantidadScrap}.";
            if (!string.IsNullOrWhiteSpace(vm.Observaciones)) observacionesProduccion += " Observaciones: " + vm.Observaciones.Trim();
            if (observacionesProduccion.Length > 1000) observacionesProduccion = observacionesProduccion[..1000];

            const string sql = @"
DECLARE @InspeccionID INT;
DECLARE @EstadoInspeccion NVARCHAR(50);
DECLARE @MonitoreoID INT;
DECLARE @RegistroHoraVinculado INT;
DECLARE @DisposicionID INT;
DECLARE @Comentario NVARCHAR(1000);

SELECT TOP (1)
    @InspeccionID=ci.InspeccionID,
    @EstadoInspeccion=UPPER(LTRIM(RTRIM(ISNULL(ci.Estado,N''))))
FROM dbo.Calidad_Inspecciones ci WITH (UPDLOCK,HOLDLOCK)
WHERE ci.EjecucionProduccionID=@EjecucionProduccionID
  AND ISNULL(ci.Estado,N'')<>N'CERRADA'
ORDER BY ci.InspeccionID DESC;

IF @InspeccionID IS NULL
    THROW 51050,'No existe una inspección activa de Calidad para la ejecución.',1;

IF @EstadoInspeccion<>N'MONITOREO_ACTIVO'
    THROW 51051,'La inspección de Calidad no se encuentra en monitoreo activo.',1;

SELECT TOP (1)
    @MonitoreoID=m.MonitoreoID,
    @RegistroHoraVinculado=m.RegistroHoraID
FROM dbo.Calidad_MonitoreosProceso m WITH (UPDLOCK,HOLDLOCK)
WHERE m.InspeccionID=@InspeccionID
  AND m.EjecucionProduccionID=@EjecucionProduccionID
  AND m.RegistroHoraID=@RegistroHoraID
  AND m.Activo=1
ORDER BY m.MonitoreoID DESC;

IF @MonitoreoID IS NULL
BEGIN
    SELECT TOP (1)
        @MonitoreoID=m.MonitoreoID,
        @RegistroHoraVinculado=m.RegistroHoraID
    FROM dbo.Calidad_MonitoreosProceso m WITH (UPDLOCK,HOLDLOCK)
    WHERE m.InspeccionID=@InspeccionID
      AND m.EjecucionProduccionID=@EjecucionProduccionID
      AND m.Activo=1
      AND m.RegistroHoraID IS NULL
      AND UPPER(LTRIM(RTRIM(ISNULL(m.Resultado,N''))))=N'PENDIENTE'
    ORDER BY m.NumeroHora,m.FechaHoraProgramada,m.MonitoreoID;
END;

IF @MonitoreoID IS NULL
    THROW 51052,'No existe un monitoreo horario pendiente para vincular la captura de Producción.',1;

IF @RegistroHoraVinculado IS NOT NULL AND @RegistroHoraVinculado<>@RegistroHoraID
    THROW 51053,'El monitoreo seleccionado ya se encuentra vinculado con otra captura horaria.',1;

IF EXISTS
(
    SELECT 1
    FROM dbo.Calidad_MonitoreosProceso m WITH (UPDLOCK,HOLDLOCK)
    WHERE m.RegistroHoraID=@RegistroHoraID
      AND m.MonitoreoID<>@MonitoreoID
      AND m.Activo=1
)
    THROW 51054,'La captura horaria ya se encuentra vinculada con otro monitoreo de Calidad.',1;

UPDATE dbo.Calidad_MonitoreosProceso
SET RegistroHoraID=@RegistroHoraID,
    CantidadProducidaPeriodo=@CantidadProducidaPeriodo,
    CantidadSospechosa=@CantidadSospechosa,
    CantidadNoRecuperable=@CantidadScrap,
    Observaciones=CASE
        WHEN @ObservacionesProduccion IS NULL THEN Observaciones
        WHEN Observaciones IS NULL OR LTRIM(RTRIM(Observaciones))=N'' THEN @ObservacionesProduccion
        WHEN Observaciones LIKE N'%RegistroHoraID: '+CONVERT(NVARCHAR(20),@RegistroHoraID)+N'%' THEN Observaciones
        ELSE Observaciones+CHAR(13)+CHAR(10)+@ObservacionesProduccion
    END,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=SYSDATETIME()
WHERE MonitoreoID=@MonitoreoID
  AND InspeccionID=@InspeccionID
  AND EjecucionProduccionID=@EjecucionProduccionID
  AND Activo=1
  AND (RegistroHoraID IS NULL OR RegistroHoraID=@RegistroHoraID);

IF @@ROWCOUNT<>1
    THROW 51055,'El monitoreo cambió de estado mientras se vinculaba la captura horaria.',1;

IF @CantidadPendienteRevision>0
BEGIN
    SELECT TOP (1) @DisposicionID=d.DisposicionID
    FROM dbo.Calidad_DisposicionesMaterial d WITH (UPDLOCK,HOLDLOCK)
    WHERE d.InspeccionID=@InspeccionID
      AND d.MonitoreoID=@MonitoreoID
      AND d.Activo=1
      AND UPPER(LTRIM(RTRIM(ISNULL(d.ResultadoFinal,N''))))=N'PENDIENTE'
    ORDER BY d.DisposicionID DESC;

    SET @Comentario=CONCAT(
        N'Seguimiento automático generado desde Producción. RegistroHoraID: ',
        @RegistroHoraID,
        N'. Periodo: ',
        CONVERT(NVARCHAR(19),@FechaHoraInicio,120),
        N' a ',
        CONVERT(NVARCHAR(19),@FechaHoraFin,120),
        N'. Sospechoso: ',
        @CantidadSospechosa,
        N'. Scrap reportado: ',
        @CantidadScrap,
        N'. Calidad debe confirmar la disposición final.'
    );

    IF @DisposicionID IS NULL
    BEGIN
        INSERT INTO dbo.Calidad_DisposicionesMaterial
        (
            InspeccionID,
            MonitoreoID,
            TipoMaterial,
            CantidadAfectada,
            Etiqueta,
            Disposicion,
            Responsable,
            FechaInicio,
            ResultadoFinal,
            Observaciones,
            UsuarioCreacionID,
            FechaCreacion,
            Activo
        )
        VALUES
        (
            @InspeccionID,
            @MonitoreoID,
           CASE
    WHEN @CantidadScrap > 0
        THEN N'NO_CONFORME'
    ELSE N'SOSPECHOSO'
END,
            @CantidadPendienteRevision,
            N'AMARILLA',
             N'SELECCION',
            N'CALIDAD',
            SYSDATETIME(),
            N'PENDIENTE',
            @Comentario,
            @UsuarioID,
            SYSDATETIME(),
            1
        );
    END
    ELSE
    BEGIN
        UPDATE dbo.Calidad_DisposicionesMaterial
        SET TipoMaterial =
    CASE
        WHEN @CantidadScrap > 0
            THEN N'NO_CONFORME'
        ELSE N'SOSPECHOSO'
    END,
            CantidadAfectada=@CantidadPendienteRevision,
            Etiqueta=N'AMARILLA',
           Disposicion =
    CASE
        WHEN Disposicion IS NULL
          OR LTRIM(RTRIM(Disposicion)) = N''
            THEN N'SELECCION'
        ELSE Disposicion
    END,
            Responsable=N'CALIDAD',
            ResultadoFinal=N'PENDIENTE',
            Observaciones=@Comentario,
            UsuarioModificacionID=@UsuarioID,
            FechaModificacion=SYSDATETIME()
        WHERE DisposicionID=@DisposicionID
          AND Activo=1;
    END;
END;

INSERT INTO dbo.Calidad_InspeccionHistorial
(
    InspeccionID,
    Movimiento,
    EstadoAnterior,
    EstadoNuevo,
    ResultadoCalidad,
    Etiqueta,
    Comentario,
    UsuarioID,
    FechaMovimiento
)
SELECT
    @InspeccionID,
    N'CAPTURA_HORARIA_RECIBIDA',
    N'MONITOREO_ACTIVO',
    N'MONITOREO_ACTIVO',
    CASE WHEN @CantidadPendienteRevision>0 THEN N'PENDIENTE_REVISION' ELSE NULL END,
    CASE WHEN @CantidadPendienteRevision>0 THEN N'AMARILLA' ELSE NULL END,
    CONCAT(
        N'Captura horaria recibida desde Producción. RegistroHoraID: ',
        @RegistroHoraID,
        N'. OK: ',
        @CantidadOK,
        N'. Sospechoso: ',
        @CantidadSospechosa,
        N'. Scrap reportado: ',
        @CantidadScrap,
        N'.'
    ),
    @UsuarioID,
    SYSDATETIME()
WHERE NOT EXISTS
(
    SELECT 1
    FROM dbo.Calidad_InspeccionHistorial h
    WHERE h.InspeccionID=@InspeccionID
      AND h.Movimiento=N'CAPTURA_HORARIA_RECIBIDA'
      AND h.Comentario LIKE N'%RegistroHoraID: '+CONVERT(NVARCHAR(20),@RegistroHoraID)+N'%'
);";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucion.EjecucionProduccionID;
            cmd.Parameters.Add("@RegistroHoraID", SqlDbType.Int).Value = registroHoraId;
            cmd.Parameters.Add("@FechaHoraInicio", SqlDbType.DateTime2).Value = fechaHoraInicio;
            cmd.Parameters.Add("@FechaHoraFin", SqlDbType.DateTime2).Value = fechaHoraFin;
            cmd.Parameters.Add("@CantidadProducidaPeriodo", SqlDbType.Int).Value = cantidadPeriodo;
            cmd.Parameters.Add("@CantidadOK", SqlDbType.Int).Value = vm.CantidadOK;
            cmd.Parameters.Add("@CantidadSospechosa", SqlDbType.Int).Value = vm.CantidadSospechosa;
            cmd.Parameters.Add("@CantidadScrap", SqlDbType.Int).Value = vm.CantidadScrap;
            cmd.Parameters.Add("@CantidadPendienteRevision", SqlDbType.Int).Value = cantidadPendienteRevision;
            cmd.Parameters.Add("@ObservacionesProduccion", SqlDbType.NVarChar, 1000).Value = observacionesProduccion;
            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
            await cmd.ExecuteNonQueryAsync();
        }

        private async Task RecalcularTotalesEjecucionAsync(
            int ejecucionProduccionId,
            int usuarioId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
;WITH Totales AS
(
    SELECT
        EjecucionProduccionID,
        SUM(ISNULL(CantidadOK, 0)) AS OKTotal,
        SUM(ISNULL(CantidadSospechosa, 0)) AS SospechosaTotal,
        SUM(ISNULL(CantidadScrap, 0)) AS ScrapTotal
    FROM dbo.Produccion_RegistroHora
    WHERE EjecucionProduccionID = @EjecucionProduccionID
      AND Activo = 1
    GROUP BY EjecucionProduccionID
)
UPDATE e
SET
    e.CantidadOKTotal = ISNULL(t.OKTotal, 0),
    e.CantidadSospechosaTotal = ISNULL(t.SospechosaTotal, 0),
    e.CantidadScrapTotal = ISNULL(t.ScrapTotal, 0),
    e.UsuarioModificacionID = @UsuarioID,
    e.FechaModificacion = GETDATE()
FROM dbo.Produccion_Ejecucion e
LEFT JOIN Totales t
    ON t.EjecucionProduccionID = e.EjecucionProduccionID
WHERE e.EjecucionProduccionID = @EjecucionProduccionID;

UPDATE pp
SET
    pp.CantidadProducida = ISNULL(e.CantidadOKTotal, 0),
    pp.HorasReales =
        CASE
            WHEN e.FechaInicioReal IS NOT NULL
                THEN CONVERT(DECIMAL(18,2), DATEDIFF(MINUTE, e.FechaInicioReal, GETDATE()) / 60.0)
            ELSE pp.HorasReales
        END,
    pp.UsuarioModificacionID = @UsuarioID,
    pp.FechaModificacion = GETDATE()
FROM dbo.Planeacion_ProgramaProduccion pp
INNER JOIN dbo.Produccion_Ejecucion e
    ON e.ProgramaProduccionID = pp.ProgramaProduccionID
WHERE e.EjecucionProduccionID = @EjecucionProduccionID
  AND pp.Activo = 1;";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value =
                ejecucionProduccionId;

            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value =
                usuarioId;

            await cmd.ExecuteNonQueryAsync();
        }

        private async Task CambiarEstatusEjecucionAsync(
            int ejecucionProduccionId,
            int estatusId,
            int usuarioId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
UPDATE dbo.Produccion_Ejecucion
SET
    EstatusID = @EstatusID,
    UsuarioModificacionID = @UsuarioID,
    FechaModificacion = GETDATE()
WHERE EjecucionProduccionID = @EjecucionProduccionID
  AND Activo = 1;";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value =
                ejecucionProduccionId;

            cmd.Parameters.Add("@EstatusID", SqlDbType.Int).Value =
                estatusId;

            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value =
                usuarioId;

            await cmd.ExecuteNonQueryAsync();
        }

        private async Task CambiarEstatusProgramaAsync(
            int programaProduccionId,
            int estatusId,
            int usuarioId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
UPDATE dbo.Planeacion_ProgramaProduccion
SET
    EstatusID = @EstatusID,
    UsuarioModificacionID = @UsuarioID,
    FechaModificacion = GETDATE()
WHERE ProgramaProduccionID = @ProgramaProduccionID
  AND Activo = 1;";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value =
                programaProduccionId;

            cmd.Parameters.Add("@EstatusID", SqlDbType.Int).Value =
                estatusId;

            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value =
                usuarioId;

            await cmd.ExecuteNonQueryAsync();
        }

        // ============================================================
        // CATALOGOS
        // ============================================================

        private async Task<List<SelectListItem>> CargarMotivosParoAsync(
            SqlConnection cn)
        {
            var lista = new List<SelectListItem>
            {
                new() { Value = "", Text = "Selecciona motivo" }
            };

            const string sql = @"
SELECT
    MotivoParoID,
    Nombre
FROM dbo.ERP_MotivosParoProduccion
WHERE Activo = 1
ORDER BY Nombre;";

            await using var cmd = new SqlCommand(sql, cn);
            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                lista.Add(new SelectListItem
                {
                    Value = rd["MotivoParoID"].ToString(),
                    Text = rd["Nombre"].ToString()
                });
            }

            return lista;
        }

        private async Task<string?> ObtenerMotivoParoNombreAsync(
            int motivoParoId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
SELECT TOP (1)
    Nombre
FROM dbo.ERP_MotivosParoProduccion
WHERE MotivoParoID = @MotivoParoID
  AND Activo = 1;";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@MotivoParoID", SqlDbType.Int).Value =
                motivoParoId;

            var result = await cmd.ExecuteScalarAsync();

            return result == null || result == DBNull.Value
                ? null
                : result.ToString();
        }

        // ============================================================
        // VALIDACION OPERADOR
        // ============================================================

        private sealed class PersonaOperadorInfo
        {
            public int PersonaID { get; set; }
            public string NombreCompleto { get; set; } = string.Empty;
            public string? Puesto { get; set; }
        }

        private async Task<bool> UsuarioEsOperadorAsync(
            int usuarioId,
            SqlConnection cn)
        {
            const string sql = @"
SELECT TOP (1)
    p.Puesto
FROM dbo.Usuarios u
INNER JOIN dbo.Persona p
    ON p.PersonaID = u.PersonaID
WHERE u.UsuarioID = @UsuarioID
  AND u.Activo = 1;";

            await using var cmd = new SqlCommand(sql, cn);

            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value =
                usuarioId;

            var result = await cmd.ExecuteScalarAsync();

            if (result == null || result == DBNull.Value)
                return false;

            var puesto = result.ToString()?.Trim() ?? string.Empty;

            return puesto.Equals("OPERADOR", StringComparison.OrdinalIgnoreCase) ||
                   puesto.Contains("OPERADOR", StringComparison.OrdinalIgnoreCase);
        }


        private async Task<List<ProduccionAlertaProximoProgramaVm>> ObtenerAlertasProximosProgramasAsync(int personaId, SqlConnection cn, int minutosAntes)
        {
            var lista = new List<ProduccionAlertaProximoProgramaVm>();
            const string sql = @"
DECLARE @Ahora DATETIME = GETDATE();
DECLARE @Hasta DATETIME = DATEADD(MINUTE, @MinutosAntes, @Ahora);

;WITH ProgramasBase AS
(
    SELECT
        pp.ProgramaProduccionID,
        pe.EjecucionProduccionID,
        pp.MaquinaID,
        COALESCE(NULLIF(pp.MaquinaCodigo, ''), maq.Codigo) AS MaquinaCodigo,
        COALESCE(NULLIF(pp.MaquinaNombre, ''), maq.Nombre) AS MaquinaNombre,
        pp.ParteID,
        pp.NumeroParte,
        pp.ReferenciaSAP,
        pp.DesignacionDescripcionSAP AS DescripcionParte,
        pp.MoldeID,
        pp.MoldeCodigo,
        CONVERT(INT, ISNULL(pp.CantidadProgramada, 0)) AS CantidadProgramada,
        pp.FechaInicioProgramada,
        pp.FechaFinProgramada,
        CASE
            WHEN pp.Cambio IS NULL THEN NULL
            ELSE DATEADD
            (
                SECOND,
                DATEDIFF(SECOND, CAST('00:00:00' AS TIME), CAST(pp.Cambio AS TIME)),
                CAST(CAST(ISNULL(pp.FechaInicioProgramada, GETDATE()) AS DATE) AS DATETIME)
            )
        END AS FechaCambioMolde,
        CASE
            WHEN pp.Arranque IS NULL THEN ISNULL(pp.FechaInicioProgramada, GETDATE())
            ELSE DATEADD
            (
                SECOND,
                DATEDIFF(SECOND, CAST('00:00:00' AS TIME), CAST(pp.Arranque AS TIME)),
                CAST(CAST(ISNULL(pp.FechaInicioProgramada, GETDATE()) AS DATE) AS DATETIME)
            )
        END AS FechaArranque,
        opPrincipal.PersonaID AS OperadorPrincipalID,
        opPrincipal.NombreCompleto AS OperadorPrincipalNombre,
        opAuxiliar.PersonaID AS OperadorAuxiliarID,
        opAuxiliar.NombreCompleto AS OperadorAuxiliarNombre
    FROM dbo.Planeacion_ProgramaProduccion pp
    LEFT JOIN dbo.ERP_Maquinas maq
        ON maq.MaquinaID = pp.MaquinaID
    OUTER APPLY
    (
        SELECT TOP (1)
            e.EjecucionProduccionID
        FROM dbo.Produccion_Ejecucion e
        WHERE e.ProgramaProduccionID = pp.ProgramaProduccionID
          AND e.Activo = 1
        ORDER BY e.EjecucionProduccionID DESC
    ) pe
    OUTER APPLY
    (
        SELECT TOP (1)
            po.PersonaID,
            LTRIM(RTRIM(
                ISNULL(p.Nombre, '') + ' ' +
                ISNULL(p.ApellidoPaterno, '') + ' ' +
                ISNULL(p.ApellidoMaterno, '')
            )) AS NombreCompleto
        FROM dbo.Planeacion_ProgramaOperadores po
        LEFT JOIN dbo.Persona p
            ON p.PersonaID = po.PersonaID
        WHERE po.ProgramaProduccionID = pp.ProgramaProduccionID
          AND po.Activo = 1
          AND UPPER(LTRIM(RTRIM(ISNULL(po.RolOperador, '')))) = 'PRINCIPAL'
        ORDER BY po.ProgramaOperadorID
    ) opPrincipal
    OUTER APPLY
    (
        SELECT TOP (1)
            po.PersonaID,
            LTRIM(RTRIM(
                ISNULL(p.Nombre, '') + ' ' +
                ISNULL(p.ApellidoPaterno, '') + ' ' +
                ISNULL(p.ApellidoMaterno, '')
            )) AS NombreCompleto
        FROM dbo.Planeacion_ProgramaOperadores po
        LEFT JOIN dbo.Persona p
            ON p.PersonaID = po.PersonaID
        WHERE po.ProgramaProduccionID = pp.ProgramaProduccionID
          AND po.Activo = 1
          AND UPPER(LTRIM(RTRIM(ISNULL(po.RolOperador, '')))) = 'AUXILIAR'
        ORDER BY po.ProgramaOperadorID
    ) opAuxiliar
    WHERE pp.Activo = 1
      AND pp.MaquinaID IS NOT NULL
      AND ISNULL(pp.EstatusID, 1) IN
      (
          @EstatusPendiente,
          @EstatusEnPreparacion
      )
      AND EXISTS
      (
          SELECT 1
          FROM dbo.Planeacion_ProgramaOperadores poFiltro
          WHERE poFiltro.ProgramaProduccionID = pp.ProgramaProduccionID
            AND poFiltro.PersonaID = @PersonaID
            AND poFiltro.Activo = 1
            AND UPPER(LTRIM(RTRIM(ISNULL(poFiltro.RolOperador, ''))))
                IN ('PRINCIPAL', 'AUXILIAR')
      )
),
Alertas AS
(
    SELECT
        ProgramaProduccionID,
        EjecucionProduccionID,
        MaquinaID,
        MaquinaCodigo,
        MaquinaNombre,
        ParteID,
        NumeroParte,
        ReferenciaSAP,
        DescripcionParte,
        MoldeID,
        MoldeCodigo,
        CantidadProgramada,
        'CAMBIO_MOLDE' AS TipoAlerta,
        FechaCambioMolde AS FechaObjetivo,
        OperadorPrincipalID,
        OperadorPrincipalNombre,
        OperadorAuxiliarID,
        OperadorAuxiliarNombre
    FROM ProgramasBase
    WHERE FechaCambioMolde IS NOT NULL
      AND FechaCambioMolde <= @Hasta
      AND FechaCambioMolde >= DATEADD(MINUTE, -5, @Ahora)
      AND
      (
          FechaArranque IS NULL
          OR FechaCambioMolde < FechaArranque
      )

    UNION ALL

    SELECT
        ProgramaProduccionID,
        EjecucionProduccionID,
        MaquinaID,
        MaquinaCodigo,
        MaquinaNombre,
        ParteID,
        NumeroParte,
        ReferenciaSAP,
        DescripcionParte,
        MoldeID,
        MoldeCodigo,
        CantidadProgramada,
        'ARRANQUE' AS TipoAlerta,
        FechaArranque AS FechaObjetivo,
        OperadorPrincipalID,
        OperadorPrincipalNombre,
        OperadorAuxiliarID,
        OperadorAuxiliarNombre
    FROM ProgramasBase
    WHERE FechaArranque IS NOT NULL
      AND FechaArranque <= @Hasta
      AND FechaArranque >= DATEADD(MINUTE, -5, @Ahora)
)
SELECT
    ProgramaProduccionID,
    EjecucionProduccionID,
    MaquinaID,
    MaquinaCodigo,
    MaquinaNombre,
    ParteID,
    NumeroParte,
    ReferenciaSAP,
    DescripcionParte,
    MoldeID,
    MoldeCodigo,
    CantidadProgramada,
    TipoAlerta,
    FechaObjetivo,
    DATEDIFF(MINUTE, @Ahora, FechaObjetivo) AS MinutosRestantes,
    OperadorPrincipalID,
    OperadorPrincipalNombre,
    OperadorAuxiliarID,
    OperadorAuxiliarNombre
FROM Alertas
ORDER BY
    FechaObjetivo,
    CASE
        WHEN TipoAlerta = 'CAMBIO_MOLDE' THEN 1
        ELSE 2
    END,
    MaquinaCodigo,
    ProgramaProduccionID;";
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@PersonaID", SqlDbType.Int).Value = personaId;
            cmd.Parameters.Add("@MinutosAntes", SqlDbType.Int).Value = minutosAntes;
            cmd.Parameters.Add("@EstatusPendiente", SqlDbType.Int).Value = ProgramaProduccionEstatus.Pendiente;
            cmd.Parameters.Add("@EstatusEnPreparacion", SqlDbType.Int).Value = ProgramaProduccionEstatus.EnPreparacion;
            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                lista.Add(new ProduccionAlertaProximoProgramaVm
                {
                    ProgramaProduccionID = Entero(rd, "ProgramaProduccionID"),
                    EjecucionProduccionID = NullableEntero(rd, "EjecucionProduccionID"),
                    MaquinaID = NullableEntero(rd, "MaquinaID"),
                    MaquinaCodigo = TextoNullable(rd, "MaquinaCodigo"),
                    MaquinaNombre = TextoNullable(rd, "MaquinaNombre"),
                    ParteID = NullableEntero(rd, "ParteID"),
                    NumeroParte = TextoNullable(rd, "NumeroParte"),
                    ReferenciaSAP = TextoNullable(rd, "ReferenciaSAP"),
                    DescripcionParte = TextoNullable(rd, "DescripcionParte"),
                    MoldeID = NullableEntero(rd, "MoldeID"),
                    MoldeCodigo = TextoNullable(rd, "MoldeCodigo"),
                    CantidadProgramada = Entero(rd, "CantidadProgramada"),
                    TipoAlerta = TextoNullable(rd, "TipoAlerta") ?? "",
                    FechaObjetivo = Convert.ToDateTime(rd["FechaObjetivo"]),
                    MinutosRestantes = Entero(rd, "MinutosRestantes"),
                    OperadorPrincipalID = NullableEntero(rd, "OperadorPrincipalID"),
                    OperadorPrincipalNombre = TextoNullable(rd, "OperadorPrincipalNombre"),
                    OperadorAuxiliarID = NullableEntero(rd, "OperadorAuxiliarID"),
                    OperadorAuxiliarNombre = TextoNullable(rd, "OperadorAuxiliarNombre")
                });
            }
            return lista;
        }
        private async Task<PersonaOperadorInfo> ObtenerPersonaOperadorAsync(
            int usuarioId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
SELECT TOP (1)
    p.PersonaID,
    LTRIM(RTRIM(
        ISNULL(p.Nombre, '') + ' ' +
        ISNULL(p.ApellidoPaterno, '') + ' ' +
        ISNULL(p.ApellidoMaterno, '')
    )) AS NombreCompleto,
    p.Puesto
FROM dbo.Usuarios u
INNER JOIN dbo.Persona p
    ON p.PersonaID = u.PersonaID
WHERE u.UsuarioID = @UsuarioID
  AND u.Activo = 1;";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value =
                usuarioId;

            await using var rd = await cmd.ExecuteReaderAsync();

            if (!await rd.ReadAsync())
                throw new InvalidOperationException("No se encontró la persona vinculada al usuario.");

            return new PersonaOperadorInfo
            {
                PersonaID = Entero(rd, "PersonaID"),
                NombreCompleto = TextoNullable(rd, "NombreCompleto") ?? string.Empty,
                Puesto = TextoNullable(rd, "Puesto")
            };
        }

        private IActionResult AccesoDenegadoOperador()
        {
            Response.StatusCode = StatusCodes.Status403Forbidden;

            return Content(
                "Acceso denegado. Esta pantalla es exclusiva para usuarios con puesto OPERADOR.",
                "text/plain");
        }

      

        private static ProduccionOperadorTabletVm MapearTabletVm(
            SqlDataReader rd)
        {
            return new ProduccionOperadorTabletVm
            {
                EjecucionProduccionID = Entero(rd, "EjecucionProduccionID"),
                ProgramaProduccionID = Entero(rd, "ProgramaProduccionID"),

                SolicitudProduccionID = NullableEntero(rd, "SolicitudProduccionID"),
                FolioSolicitud = TextoNullable(rd, "FolioSolicitud"),
                NumeroOFRecibida = TextoNullable(rd, "NumeroOFRecibida"),

                MaquinaID = NullableEntero(rd, "MaquinaID"),
                MaquinaCodigo = TextoNullable(rd, "MaquinaCodigo"),
                MaquinaNombre = TextoNullable(rd, "MaquinaNombre"),

                ParteID = NullableEntero(rd, "ParteID"),
                NumeroParte = TextoNullable(rd, "NumeroParte"),
                ReferenciaSAP = TextoNullable(rd, "ReferenciaSAP"),
                DescripcionParte = TextoNullable(rd, "DescripcionParte"),

                OperadorID = NullableEntero(rd, "OperadorID"),
                OperadorNombre = TextoNullable(rd, "OperadorNombre"),

                CantidadPlaneada = NullableEntero(rd, "CantidadPlaneada"),
                CantidadOKTotal = Entero(rd, "CantidadOKTotal"),
                CantidadSospechosaTotal = Entero(rd, "CantidadSospechosaTotal"),
                CantidadScrapTotal = Entero(rd, "CantidadScrapTotal"),

                EstatusID = Entero(rd, "EstatusID"),

                TieneParoAbierto = Booleano(rd, "TieneParoAbierto"),
                ParoAbiertoID = NullableEntero(rd, "ParoAbiertoID")
            };
        }

        private async Task DesplazarFinProgramadoParoCortoAsync(
    int programaProduccionId,
    int ejecucionProduccionId,
    int minutosInterrupcion,
    int usuarioId,
    SqlConnection cn,
    SqlTransaction tx)
        {
            if (minutosInterrupcion <= 0)
                return;

            const string sql = @"
UPDATE dbo.Planeacion_ProgramaProduccion
SET
    FechaFinProgramada =
        CASE
            WHEN FechaFinProgramada IS NULL
                THEN FechaFinProgramada
            ELSE DATEADD(MINUTE, @MinutosInterrupcion, FechaFinProgramada)
        END,
    UsuarioModificacionID = @UsuarioID,
    FechaModificacion = GETDATE()
WHERE ProgramaProduccionID = @ProgramaProduccionID
  AND Activo = 1;

UPDATE dbo.Calidad_Inspecciones
SET
    FechaFinProgramada =
        CASE
            WHEN FechaFinProgramada IS NULL
                THEN FechaFinProgramada
            ELSE DATEADD(MINUTE, @MinutosInterrupcion, FechaFinProgramada)
        END,
    UsuarioModificacionID = @UsuarioID,
    FechaModificacion = GETDATE()
WHERE EjecucionProduccionID = @EjecucionProduccionID
  AND ISNULL(Estado, N'') <> N'CERRADA';";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add(
                "@ProgramaProduccionID",
                SqlDbType.Int).Value =
                programaProduccionId;

            cmd.Parameters.Add(
                "@EjecucionProduccionID",
                SqlDbType.Int).Value =
                ejecucionProduccionId;

            cmd.Parameters.Add(
                "@MinutosInterrupcion",
                SqlDbType.Int).Value =
                minutosInterrupcion;

            cmd.Parameters.Add(
                "@UsuarioID",
                SqlDbType.Int).Value =
                usuarioId;

            await cmd.ExecuteNonQueryAsync();
        }

        private static void AsignarHoraSugerida(
            ProduccionOperadorTabletVm vm)
        {
            var ahora = DateTime.Now;
            var inicio = new TimeSpan(ahora.Hour, 0, 0);
            var fin = inicio.Add(TimeSpan.FromHours(1));

            if (fin.TotalHours >= 24)
                fin = new TimeSpan(23, 59, 0);

            vm.FechaProduccion = DateTime.Today;
            vm.HoraInicioSugerida = inicio;
            vm.HoraFinSugerida = fin;
        }

        private async Task<List<ProduccionCapturaHoraFilaVm>>
    ObtenerFilasCapturaHoraAsync(
        int ejecucionProduccionId,
        int programaProduccionId,
        SqlConnection cn,
        SqlTransaction? tx = null)
        {
            var filas =
                new List<ProduccionCapturaHoraFilaVm>();

            DateTime? inicioReal = null;
            DateTime? finReal = null;

            /*
             * ============================================================
             * 1. OBTENER INICIO REAL DE PRODUCCIÓN EN SERIE
             * ============================================================
             *
             * La Hora 1 NO comienza cuando inicia preparación.
             *
             * Prioridad:
             *
             * 1) CONFIRMACION_INICIO_SERIE_PRODUCCION
             * 2) Planeacion_ProgramaProduccion.FechaInicioReal
             * 3) Produccion_Ejecucion.FechaInicioReal
             *
             * La confirmación de serie es la fuente más confiable porque
             * representa el momento en que Calidad ya liberó y Producción
             * confirmó físicamente el inicio de producción en serie.
             */
            const string sqlPrograma = @"
SELECT TOP (1)
    COALESCE
    (
        (
            SELECT TOP (1)
                h.FechaMovimiento
            FROM dbo.Calidad_InspeccionHistorial h
            INNER JOIN dbo.Calidad_Inspecciones ci
                ON ci.InspeccionID = h.InspeccionID
            WHERE ci.EjecucionProduccionID =
                  e.EjecucionProduccionID
              AND h.Movimiento =
                  N'CONFIRMACION_INICIO_SERIE_PRODUCCION'
            ORDER BY
                h.FechaMovimiento
        ),
        pp.FechaInicioReal,
        e.FechaInicioReal
    ) AS FechaInicioReal,

    COALESCE
    (
        pp.FechaFinReal,
        e.FechaFinReal
    ) AS FechaFinReal

FROM dbo.Produccion_Ejecucion e
INNER JOIN dbo.Planeacion_ProgramaProduccion pp
    ON pp.ProgramaProduccionID = e.ProgramaProduccionID
   AND pp.Activo = 1

WHERE e.EjecucionProduccionID = @EjecucionProduccionID
  AND e.ProgramaProduccionID = @ProgramaProduccionID
  AND e.Activo = 1;";

            await using (var cmd =
                new SqlCommand(
                    sqlPrograma,
                    cn,
                    tx))
            {
                cmd.Parameters.Add(
                    "@EjecucionProduccionID",
                    SqlDbType.Int).Value =
                    ejecucionProduccionId;

                cmd.Parameters.Add(
                    "@ProgramaProduccionID",
                    SqlDbType.Int).Value =
                    programaProduccionId;

                await using var rd =
                    await cmd.ExecuteReaderAsync();

                if (await rd.ReadAsync())
                {
                    inicioReal =
                        rd["FechaInicioReal"] == DBNull.Value
                            ? null
                            : Convert.ToDateTime(
                                rd["FechaInicioReal"]);

                    finReal =
                        rd["FechaFinReal"] == DBNull.Value
                            ? null
                            : Convert.ToDateTime(
                                rd["FechaFinReal"]);
                }
            }

            if (!inicioReal.HasValue)
                return filas;

            /*
             * Trabajamos a precisión de minuto porque la vista captura
             * HH:mm y los registros existentes están manejados igual.
             */
            DateTime AlMinuto(DateTime value)
            {
                return new DateTime(
                    value.Year,
                    value.Month,
                    value.Day,
                    value.Hour,
                    value.Minute,
                    0,
                    value.Kind);
            }

            var inicio =
                AlMinuto(inicioReal.Value);

            var ahora =
                DateTime.Now;

            /*
             * ============================================================
             * 2. REGISTROS HORARIOS YA CAPTURADOS
             * ============================================================
             */
            var registros =
                new List<ProduccionRegistroHoraVm>();

            const string sqlRegistros = @"
SELECT
    RegistroHoraID,
    EjecucionProduccionID,
    ProgramaProduccionID,
    SolicitudProduccionID,
    MaquinaID,
    OperadorID,
    FechaProduccion,
    HoraInicio,
    HoraFin,
    ISNULL(CantidadOK, 0) AS CantidadOK,
    ISNULL(CantidadSospechosa, 0)
        AS CantidadSospechosa,
    ISNULL(CantidadScrap, 0)
        AS CantidadScrap,
    Observaciones,
    UsuarioCreacionID,
    FechaCreacion,
    UsuarioModificacionID,
    FechaModificacion,
    Activo
FROM dbo.Produccion_RegistroHora
WHERE EjecucionProduccionID =
      @EjecucionProduccionID
  AND Activo = 1
ORDER BY
    FechaProduccion,
    HoraInicio;";

            await using (var cmd =
                new SqlCommand(
                    sqlRegistros,
                    cn,
                    tx))
            {
                cmd.Parameters.Add(
                    "@EjecucionProduccionID",
                    SqlDbType.Int).Value =
                    ejecucionProduccionId;

                await using var rd =
                    await cmd.ExecuteReaderAsync();

                while (await rd.ReadAsync())
                {
                    registros.Add(
                        new ProduccionRegistroHoraVm
                        {
                            RegistroHoraID =
                                Convert.ToInt32(
                                    rd["RegistroHoraID"]),

                            EjecucionProduccionID =
                                Convert.ToInt32(
                                    rd["EjecucionProduccionID"]),

                            ProgramaProduccionID =
                                Convert.ToInt32(
                                    rd["ProgramaProduccionID"]),

                            SolicitudProduccionID =
                                rd["SolicitudProduccionID"]
                                    == DBNull.Value
                                        ? null
                                        : Convert.ToInt32(
                                            rd[
                                                "SolicitudProduccionID"]),

                            MaquinaID =
                                rd["MaquinaID"]
                                    == DBNull.Value
                                        ? null
                                        : Convert.ToInt32(
                                            rd["MaquinaID"]),

                            OperadorID =
                                rd["OperadorID"]
                                    == DBNull.Value
                                        ? null
                                        : Convert.ToInt32(
                                            rd["OperadorID"]),

                            FechaProduccion =
                                Convert.ToDateTime(
                                    rd["FechaProduccion"]),

                            HoraInicio =
                                (TimeSpan)rd["HoraInicio"],

                            HoraFin =
                                (TimeSpan)rd["HoraFin"],

                            CantidadOK =
                                Convert.ToInt32(
                                    rd["CantidadOK"]),

                            CantidadSospechosa =
                                Convert.ToInt32(
                                    rd[
                                        "CantidadSospechosa"]),

                            CantidadScrap =
                                Convert.ToInt32(
                                    rd["CantidadScrap"]),

                            Observaciones =
                                rd["Observaciones"]
                                    == DBNull.Value
                                        ? null
                                        : rd[
                                            "Observaciones"]
                                            .ToString(),

                            UsuarioCreacionID =
                                rd["UsuarioCreacionID"]
                                    == DBNull.Value
                                        ? null
                                        : Convert.ToInt32(
                                            rd[
                                                "UsuarioCreacionID"]),

                            FechaCreacion =
                                Convert.ToDateTime(
                                    rd["FechaCreacion"]),

                            UsuarioModificacionID =
                                rd["UsuarioModificacionID"]
                                    == DBNull.Value
                                        ? null
                                        : Convert.ToInt32(
                                            rd[
                                                "UsuarioModificacionID"]),

                            FechaModificacion =
                                rd["FechaModificacion"]
                                    == DBNull.Value
                                        ? null
                                        : Convert.ToDateTime(
                                            rd[
                                                "FechaModificacion"]),

                            Activo =
                                rd["Activo"] != DBNull.Value &&
                                Convert.ToBoolean(
                                    rd["Activo"])
                        });
                }
            }

          
            var paros =
                new List<(
                    int ParoID,
                    DateTime Inicio,
                    DateTime? Fin,
                    bool MayorA15)>();

            const string sqlParos = @"
SELECT
    ParoID,
    FechaInicioParo,
    FechaFinParo,
    ISNULL(
        EsMayorA15Minutos,
        0
    ) AS EsMayorA15Minutos
FROM dbo.Produccion_Paros
WHERE EjecucionProduccionID =
      @EjecucionProduccionID
  AND Activo = 1
ORDER BY
    FechaInicioParo,
    ParoID;";

            await using (var cmd =
                new SqlCommand(
                    sqlParos,
                    cn,
                    tx))
            {
                cmd.Parameters.Add(
                    "@EjecucionProduccionID",
                    SqlDbType.Int).Value =
                    ejecucionProduccionId;

                await using var rd =
                    await cmd.ExecuteReaderAsync();

                while (await rd.ReadAsync())
                {
                    paros.Add((
                        Convert.ToInt32(
                            rd["ParoID"]),

                        Convert.ToDateTime(
                            rd["FechaInicioParo"]),

                        rd["FechaFinParo"]
                            == DBNull.Value
                                ? null
                                : Convert.ToDateTime(
                                    rd["FechaFinParo"]),

                        rd["EsMayorA15Minutos"]
                            != DBNull.Value &&
                        Convert.ToBoolean(
                            rd["EsMayorA15Minutos"])
                    ));
                }
            }

         
            var confirmacionesSerie =
                new List<DateTime>();

            const string sqlConfirmaciones = @"
SELECT
    h.FechaMovimiento
FROM dbo.Calidad_InspeccionHistorial h
INNER JOIN dbo.Calidad_Inspecciones ci
    ON ci.InspeccionID =
       h.InspeccionID
WHERE ci.EjecucionProduccionID =
      @EjecucionProduccionID
  AND h.Movimiento =
      N'CONFIRMACION_INICIO_SERIE_PRODUCCION'
ORDER BY
    h.FechaMovimiento;";

            await using (var cmd =
                new SqlCommand(
                    sqlConfirmaciones,
                    cn,
                    tx))
            {
                cmd.Parameters.Add(
                    "@EjecucionProduccionID",
                    SqlDbType.Int).Value =
                    ejecucionProduccionId;

                await using var rd =
                    await cmd.ExecuteReaderAsync();

                while (await rd.ReadAsync())
                {
                    confirmacionesSerie.Add(
                        Convert.ToDateTime(
                            rd["FechaMovimiento"]));
                }
            }

            var registrosDisponibles =
                registros.ToList();

            ProduccionRegistroHoraVm? BuscarRegistroBloque(
                DateTime bloqueInicio,
                DateTime bloqueFin)
            {
                foreach (
                    var item in
                    registrosDisponibles.ToList())
                {
                    var registroInicio =
                        item.FechaProduccion.Date
                            .Add(item.HoraInicio);

                    var registroFin =
                        item.FechaProduccion.Date
                            .Add(item.HoraFin);

                    if (registroFin <= registroInicio)
                    {
                        registroFin =
                            registroFin.AddDays(1);
                    }

                    registroInicio =
                        AlMinuto(registroInicio);

                    registroFin =
                        AlMinuto(registroFin);

                    /*
                     * Primero intenta coincidencia exacta.
                     */
                    if (registroInicio ==
                            bloqueInicio &&
                        registroFin ==
                            bloqueFin)
                    {
                        registrosDisponibles.Remove(
                            item);

                        return item;
                    }
                }

                ProduccionRegistroHoraVm?
                    mejorRegistro = null;

                var mejorTraslapeMinutos =
                    0d;

                foreach (
                    var item in
                    registrosDisponibles)
                {
                    var registroInicio =
                        item.FechaProduccion.Date
                            .Add(item.HoraInicio);

                    var registroFin =
                        item.FechaProduccion.Date
                            .Add(item.HoraFin);

                    if (registroFin <= registroInicio)
                    {
                        registroFin =
                            registroFin.AddDays(1);
                    }

                    var inicioTraslape =
                        registroInicio > bloqueInicio
                            ? registroInicio
                            : bloqueInicio;

                    var finTraslape =
                        registroFin < bloqueFin
                            ? registroFin
                            : bloqueFin;

                    var minutosTraslape =
                        Math.Max(
                            0,
                            (
                                finTraslape -
                                inicioTraslape
                            ).TotalMinutes);

                    if (minutosTraslape >
                        mejorTraslapeMinutos)
                    {
                        mejorTraslapeMinutos =
                            minutosTraslape;

                        mejorRegistro =
                            item;
                    }
                }

                var duracionBloqueMinutos =
                    Math.Max(
                        1,
                        (
                            bloqueFin -
                            bloqueInicio
                        ).TotalMinutes);

                var traslapeMinimo =
                    Math.Min(
                        30,
                        duracionBloqueMinutos /
                        2.0);

                if (mejorRegistro == null ||
                    mejorTraslapeMinutos <
                    traslapeMinimo)
                {
                    return null;
                }

                registrosDisponibles.Remove(
                    mejorRegistro);

                return mejorRegistro;
            }

            
            DateTime ObtenerSiguienteCorteHorario(
                DateTime fechaInicio,
                DateTime fechaActual)
            {
                if (fechaActual <= fechaInicio)
                {
                    return fechaInicio.AddHours(1);
                }

                var minutosTranscurridos =
                    (
                        fechaActual -
                        fechaInicio
                    ).TotalMinutes;

                var bloquesNecesarios =
                    (int)Math.Ceiling(
                        minutosTranscurridos /
                        60d);

                if (bloquesNecesarios < 1)
                {
                    bloquesNecesarios = 1;
                }

               
                var diferenciaConCorte =
                    Math.Abs(
                        minutosTranscurridos -
                        (
                            bloquesNecesarios *
                            60d
                        ));

                if (diferenciaConCorte <
                    0.01)
                {
                    bloquesNecesarios++;
                }

                return fechaInicio.AddHours(
                    bloquesNecesarios);
            }

            DateTime limite;

            if (finReal.HasValue)
            {
                
                limite =
                    AlMinuto(
                        finReal.Value);
            }
            else
            {
                
                limite =
                    ObtenerSiguienteCorteHorario(
                        inicio,
                        ahora);
            }

            if (limite <= inicio)
            {
                limite =
                    inicio.AddHours(1);
            }

            var limiteSeguridad =
                inicio.AddHours(500);

            if (limite > limiteSeguridad)
            {
                limite =
                    limiteSeguridad;
            }

            var numeroHora = 1;

            void AgregarSegmentoProductivo(
                DateTime segmentoInicio,
                DateTime segmentoFin)
            {
                segmentoInicio =
                    AlMinuto(segmentoInicio);

                segmentoFin =
                    AlMinuto(segmentoFin);

                if (segmentoFin <=
                    segmentoInicio)
                {
                    return;
                }

                var inicioBloque =
                    segmentoInicio;

                while (
                    inicioBloque <
                        segmentoFin &&
                    inicioBloque <
                        limite &&
                    numeroHora <= 500)
                {
                    
                    var finBloque =
                        inicioBloque.AddHours(1);

                
                    if (finBloque >
                        segmentoFin)
                    {
                        finBloque =
                            segmentoFin;
                    }

                    if (finBloque >
                        limite)
                    {
                        finBloque =
                            limite;
                    }

                    if (finReal.HasValue &&
                        finBloque >
                        AlMinuto(
                            finReal.Value))
                    {
                        finBloque =
                            AlMinuto(
                                finReal.Value);
                    }

                    if (finBloque <=
                        inicioBloque)
                    {
                        break;
                    }

                    var registro =
                        BuscarRegistroBloque(
                            inicioBloque,
                            finBloque);

                    var capturada =
                        registro != null;

                    var bloqueTerminado =
                        ahora >= finBloque;

                    filas.Add(
                        new ProduccionCapturaHoraFilaVm
                        {
                            NumeroHora =
                                numeroHora,

                            FechaProduccion =
                                inicioBloque.Date,

                            HoraInicio =
                                inicioBloque.TimeOfDay,

                            HoraFin =
                                finBloque.TimeOfDay,

                            RegistroHoraID =
                                registro?.RegistroHoraID,

                            CantidadOK =
                                registro?.CantidadOK ?? 0,

                            CantidadSospechosa =
                                registro?
                                    .CantidadSospechosa
                                ?? 0,

                            CantidadScrap =
                                registro?
                                    .CantidadScrap
                                ?? 0,

                            Observaciones =
                                registro?
                                    .Observaciones,

                            Capturada =
                                capturada,

                            Disponible =
                                !capturada &&
                                bloqueTerminado,

                            Vencida =
                                !capturada &&
                                bloqueTerminado
                        });

                    inicioBloque =
                        finBloque;

                    numeroHora++;
                }
            }

          
            var cursorProductivo =
                inicio;

            foreach (var paro in paros)
            {
                var inicioParo =
                    AlMinuto(
                        paro.Inicio);

                if (inicioParo >= limite)
                    break;

                /*
                 * Ignorar paros anteriores al
                 * inicio real de serie.
                 */
                if (inicioParo < inicio)
                    continue;

                if (inicioParo >
                    cursorProductivo)
                {
                    AgregarSegmentoProductivo(
                        cursorProductivo,
                        inicioParo);
                }

              
                if (!paro.Fin.HasValue)
                {
                    cursorProductivo =
                        limite;

                    break;
                }

                var finParo =
                    AlMinuto(
                        paro.Fin.Value);

                DateTime? reinicioReal =
                    null;

                if (paro.MayorA15)
                {
                   
                    reinicioReal =
                        confirmacionesSerie
                            .Where(
                                x =>
                                    AlMinuto(x) >=
                                    finParo)
                            .OrderBy(x => x)
                            .Select(AlMinuto)
                            .FirstOrDefault();

                    if (!reinicioReal.HasValue ||
                        reinicioReal.Value ==
                            DateTime.MinValue)
                    {
                        cursorProductivo =
                            limite;

                        break;
                    }
                }
                else
                {
                    
                    reinicioReal =
                        finParo;
                }

                if (reinicioReal.Value >
                    cursorProductivo)
                {
                    cursorProductivo =
                        reinicioReal.Value;
                }
            }

            if (cursorProductivo <
                limite)
            {
                AgregarSegmentoProductivo(
                    cursorProductivo,
                    limite);
            }

           
            var primeraPendiente =
                filas
                    .Where(
                        x =>
                            !x.Capturada &&
                            x.Disponible)
                    .OrderBy(
                        x =>
                            x.NumeroHora)
                    .FirstOrDefault();

            foreach (
                var fila in
                filas.Where(
                    x => !x.Capturada))
            {
                fila.Disponible =
                    primeraPendiente != null &&
                    fila.NumeroHora ==
                    primeraPendiente.NumeroHora;

                /*
                 * Vencida debe reflejar exactamente
                 * una hora que YA terminó.
                 *
                 * Una fila futura nunca debe aparecer
                 * como obligatoria.
                 */
                if (!fila.Disponible &&
                    !fila.Capturada)
                {
                    var fechaInicioFila =
                        fila.FechaProduccion.Date
                            .Add(
                                fila.HoraInicio);

                    var fechaFinFila =
                        fila.FechaProduccion.Date
                            .Add(
                                fila.HoraFin);

                    if (fechaFinFila <=
                        fechaInicioFila)
                    {
                        fechaFinFila =
                            fechaFinFila
                                .AddDays(1);
                    }

                    fila.Vencida =
                        ahora >=
                        fechaFinFila;
                }
            }

            return filas;
        }

        private bool UsuarioEnSesion()
        {
            return HttpContext.Session.GetInt32("UsuarioID").HasValue;
        }

        private int ObtenerUsuarioID()
        {
            return HttpContext.Session.GetInt32("UsuarioID") ?? 0;
        }

        private sealed class ValidacionEnvioCajaCalidad
        {
            public bool Permitido { get; set; }
            public int? InspeccionID { get; set; }
            public string Mensaje { get; set; } = string.Empty;
        }
        private static async Task<ValidacionEnvioCajaCalidad> ValidarEnvioCajaCalidadAsync(int ejecucionProduccionId, SqlConnection cn, SqlTransaction tx)
        {
            const string sql = @"
DECLARE @InspeccionID INT;
DECLARE @Estado NVARCHAR(50);
DECLARE @ConfiguracionInvalidada BIT;
DECLARE @RequiereReliberacion BIT;
DECLARE @Liberado BIT;
DECLARE @DisposicionesPendientes INT;
SELECT TOP (1)
    @InspeccionID=ci.InspeccionID,
    @Estado=UPPER(LTRIM(RTRIM(ISNULL(ci.Estado,N'')))),
    @ConfiguracionInvalidada=ISNULL(ci.ConfiguracionInvalidada,0),
    @RequiereReliberacion=ISNULL(ci.RequiereReliberacion,0),
    @Liberado=ISNULL(ci.Liberado,0)
FROM dbo.Calidad_Inspecciones ci WITH (UPDLOCK,HOLDLOCK)
WHERE ci.EjecucionProduccionID=@EjecucionProduccionID
  AND ci.Estado<>N'CERRADA'
ORDER BY ci.InspeccionID DESC;
IF @InspeccionID IS NULL
BEGIN
    SELECT CAST(0 AS BIT) Permitido,CAST(NULL AS INT) InspeccionID,N'No existe una inspección activa de Calidad relacionada con la ejecución.' Mensaje;
    RETURN;
END;
IF @ConfiguracionInvalidada=1
BEGIN
    SELECT CAST(0 AS BIT) Permitido,@InspeccionID InspeccionID,N'La configuración de la corrida fue invalidada. Debe completarse la revisión de Calidad antes de enviar cajas.' Mensaje;
    RETURN;
END;
IF @RequiereReliberacion=1 OR @Estado=N'PENDIENTE_RELIBERACION'
BEGIN
    SELECT CAST(0 AS BIT) Permitido,@InspeccionID InspeccionID,N'La corrida requiere reliberación de Calidad después de un paro. No se pueden enviar cajas mientras esté pendiente.' Mensaje;
    RETURN;
END;
IF @Liberado=0
BEGIN
    SELECT CAST(0 AS BIT) Permitido,@InspeccionID InspeccionID,N'Calidad no tiene liberada actualmente la producción.' Mensaje;
    RETURN;
END;
IF @Estado<>N'MONITOREO_ACTIVO'
BEGIN
    SELECT CAST(0 AS BIT) Permitido,@InspeccionID InspeccionID,N'La inspección debe encontrarse en monitoreo activo para recibir cajas de Producción.' Mensaje;
    RETURN;
END;
SELECT @DisposicionesPendientes=COUNT(1)
FROM dbo.Calidad_DisposicionesMaterial d WITH (UPDLOCK,HOLDLOCK)
WHERE d.InspeccionID=@InspeccionID
  AND d.Activo=1
  AND UPPER(LTRIM(RTRIM(ISNULL(d.ResultadoFinal,N''))))=N'PENDIENTE';
IF ISNULL(@DisposicionesPendientes,0)>0
BEGIN
    SELECT CAST(0 AS BIT) Permitido,@InspeccionID InspeccionID,CONCAT(N'Existen ',@DisposicionesPendientes,N' disposición(es) de material pendientes. Calidad debe resolverlas antes de recibir o liberar nuevas cajas.') Mensaje;
    RETURN;
END;
IF EXISTS
(
    SELECT 1
    FROM dbo.Calidad_MonitoreosProceso m
    WHERE m.InspeccionID=@InspeccionID
      AND m.Activo=1
      AND m.RegistroHoraID IS NOT NULL
      AND UPPER(LTRIM(RTRIM(ISNULL(m.Resultado,N''))))=N'PENDIENTE'
      AND (ISNULL(m.CantidadSospechosa,0)>0 OR ISNULL(m.CantidadNoRecuperable,0)>0)
)
BEGIN
    SELECT CAST(0 AS BIT) Permitido,@InspeccionID InspeccionID,N'Existen capturas con material sospechoso o scrap reportado que todavía no han sido evaluadas por Calidad.' Mensaje;
    RETURN;
END;
SELECT CAST(1 AS BIT) Permitido,@InspeccionID InspeccionID,N'La caja puede enviarse a Calidad.' Mensaje;";
            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
            await using var rd = await cmd.ExecuteReaderAsync();
            if (!await rd.ReadAsync()) return new ValidacionEnvioCajaCalidad { Permitido = false, Mensaje = "No fue posible validar el estado de Calidad." };
            return new ValidacionEnvioCajaCalidad
            {
                Permitido = rd["Permitido"] != DBNull.Value && Convert.ToBoolean(rd["Permitido"]),
                InspeccionID = rd["InspeccionID"] == DBNull.Value ? null : Convert.ToInt32(rd["InspeccionID"]),
                Mensaje = rd["Mensaje"]?.ToString() ?? "La caja no puede enviarse a Calidad."
            };
        }

        private async Task<bool> ExisteRegistroHoraAsync(int ejecucionProduccionId, DateTime fechaProduccion, TimeSpan horaInicio, SqlConnection cn, SqlTransaction tx)
        {
            const string sql = @"
SELECT CAST(CASE WHEN EXISTS
(
    SELECT 1
    FROM dbo.Produccion_RegistroHora
    WHERE EjecucionProduccionID=@EjecucionProduccionID AND FechaProduccion=@FechaProduccion AND HoraInicio=@HoraInicio AND Activo=1
) THEN 1 ELSE 0 END AS BIT);";
            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
            cmd.Parameters.Add("@FechaProduccion", SqlDbType.Date).Value = fechaProduccion.Date;
            cmd.Parameters.Add("@HoraInicio", SqlDbType.Time).Value = horaInicio;
            return Convert.ToBoolean(await cmd.ExecuteScalarAsync());
        }
        private async Task<bool> TieneParoAbiertoAsync(
            int ejecucionProduccionId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
SELECT COUNT(1)
FROM dbo.Produccion_Paros
WHERE EjecucionProduccionID = @EjecucionProduccionID
  AND Activo = 1
  AND FechaFinParo IS NULL;";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value =
                ejecucionProduccionId;

            return Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
        }

        private async Task<int?> ObtenerPersonaIDUsuarioAsync(
    int usuarioId,
    SqlConnection cn)
        {
            const string sql = @"
SELECT TOP (1)
    u.PersonaID
FROM dbo.Usuarios u
WHERE u.UsuarioID = @UsuarioID
  AND u.Activo = 1
  AND u.PersonaID IS NOT NULL;";

            await using var cmd = new SqlCommand(sql, cn);

            cmd.Parameters.Add(
                "@UsuarioID",
                SqlDbType.Int).Value =
                usuarioId;

            var resultado =
                await cmd.ExecuteScalarAsync();

            if (resultado == null ||
                resultado == DBNull.Value)
            {
                return null;
            }

            return Convert.ToInt32(resultado);
        }
        private static long EnteroLargo(SqlDataReader rd, string columna)
        {
            var ordinal = rd.GetOrdinal(columna);

            return rd.IsDBNull(ordinal)
                ? 0L
                : Convert.ToInt64(rd.GetValue(ordinal));
        }

        private static int Entero(SqlDataReader rd, string columna)
        {
            var ordinal = rd.GetOrdinal(columna);

            return rd.IsDBNull(ordinal)
                ? 0
                : Convert.ToInt32(rd.GetValue(ordinal));
        }

        private static int? NullableEntero(SqlDataReader rd, string columna)
        {
            var ordinal = rd.GetOrdinal(columna);

            return rd.IsDBNull(ordinal)
                ? null
                : Convert.ToInt32(rd.GetValue(ordinal));
        }

        private static DateTime Fecha(SqlDataReader rd, string columna)
        {
            var ordinal = rd.GetOrdinal(columna);

            return rd.IsDBNull(ordinal)
                ? DateTime.MinValue
                : Convert.ToDateTime(rd.GetValue(ordinal));
        }

        private static DateTime? NullableFecha(SqlDataReader rd, string columna)
        {
            var ordinal = rd.GetOrdinal(columna);

            return rd.IsDBNull(ordinal)
                ? null
                : Convert.ToDateTime(rd.GetValue(ordinal));
        }

        private static bool Booleano(SqlDataReader rd, string columna)
        {
            var ordinal = rd.GetOrdinal(columna);

            return !rd.IsDBNull(ordinal) &&
                   Convert.ToBoolean(rd.GetValue(ordinal));
        }

        private static string? TextoNullable(SqlDataReader rd, string columna)
        {
            var ordinal = rd.GetOrdinal(columna);

            return rd.IsDBNull(ordinal)
                ? null
                : rd.GetValue(ordinal)?.ToString()?.Trim();
        }
    }
}