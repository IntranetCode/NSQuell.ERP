using ERP.NSQuell.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
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

        private static class ProgramaProduccionEstatus
        {
            public const int EnProduccion = 3;
            public const int Pausado = 4;
        }

        // ============================================================
        // KIOSKO OPERADOR - INICIO
        // ============================================================

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            if (!UsuarioEnSesion())
                return RedirectToAction("Login", "Login");

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            var usuarioId = ObtenerUsuarioID();

            var esOperador = await UsuarioEsOperadorAsync(usuarioId, cn);

            if (!esOperador)
                return AccesoDenegadoOperador();

            var programas = await ObtenerProgramasEnProduccionAsync(cn);

            return View(programas);
        }

        // ============================================================
        // KIOSKO OPERADOR - CAPTURA
        // ============================================================

        [HttpGet]
        public async Task<IActionResult> Captura(int id)
        {
            if (!UsuarioEnSesion())
                return RedirectToAction("Login", "Login");

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

            return View(vm);
        }

        // ============================================================
        // GUARDAR PRODUCCION POR HORA
        // ============================================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RegistrarHora(ProduccionRegistroHoraPostVm vm)
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

            if (!TimeSpan.TryParse(vm.HoraInicio, out var horaInicio) ||
                !TimeSpan.TryParse(vm.HoraFin, out var horaFin))
            {
                TempData["Error"] = "El rango de hora no es válido.";
                return RedirectToAction(nameof(Captura), new { id = vm.EjecucionProduccionID });
            }

            if (horaFin <= horaInicio)
            {
                TempData["Error"] = "La hora fin debe ser mayor que la hora inicio.";
                return RedirectToAction(nameof(Captura), new { id = vm.EjecucionProduccionID });
            }

            if (vm.CantidadOK < 0 ||
                vm.CantidadSospechosa < 0 ||
                vm.CantidadScrap < 0)
            {
                TempData["Error"] = "Las cantidades no pueden ser negativas.";
                return RedirectToAction(nameof(Captura), new { id = vm.EjecucionProduccionID });
            }

            if (vm.CantidadOK == 0 &&
                vm.CantidadSospechosa == 0 &&
                vm.CantidadScrap == 0 &&
                string.IsNullOrWhiteSpace(vm.Observaciones))
            {
                TempData["Error"] = "Captura al menos una cantidad u observación.";
                return RedirectToAction(nameof(Captura), new { id = vm.EjecucionProduccionID });
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
                        "Solo puedes capturar piezas cuando la producción está en serie.";

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
                        "No puedes capturar piezas mientras exista un paro abierto.";

                    return RedirectToAction(nameof(Captura), new { id = vm.EjecucionProduccionID });
                }

                var personaOperador = await ObtenerPersonaOperadorAsync(usuarioId, cn, tx);

                await InsertarRegistroHoraAsync(
                    ejecucion,
                    vm,
                    horaInicio,
                    horaFin,
                    personaOperador.PersonaID,
                    usuarioId,
                    cn,
                    tx);

                await RecalcularTotalesEjecucionAsync(
                    vm.EjecucionProduccionID,
                    usuarioId,
                    cn,
                    tx);

                await tx.CommitAsync();

                TempData["Success"] = "Producción guardada correctamente.";

                return RedirectToAction(nameof(Captura), new { id = vm.EjecucionProduccionID });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();

                TempData["Error"] =
                    "No fue posible guardar la producción: " + ex.Message;

                return RedirectToAction(nameof(Captura), new { id = vm.EjecucionProduccionID });
            }
        }

        // ============================================================
        // INICIAR PARO DESDE TABLET
        // ============================================================

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

        // ============================================================
        // CERRAR PARO DESDE TABLET
        // ============================================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CerrarParo(ProduccionCerrarParoPostVm vm)
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
                    cmd.Parameters.Add("@ParoID", SqlDbType.Int).Value =
                        vm.ParoID;

                    await using var rd = await cmd.ExecuteReaderAsync();

                    if (!await rd.ReadAsync())
                    {
                        await tx.RollbackAsync();

                        TempData["Error"] =
                            "No se encontró un paro abierto para cerrar.";

                        return RedirectToAction(nameof(Index));
                    }

                    ejecucionProduccionId =
                        Convert.ToInt32(rd["EjecucionProduccionID"]);

                    fechaInicioParo =
                        Convert.ToDateTime(rd["FechaInicioParo"]);
                }

                var duracionMinutos =
                    (int)Math.Max(0, (DateTime.Now - fechaInicioParo).TotalMinutes);

                const string sqlCerrar = @"
UPDATE dbo.Produccion_Paros
SET
    FechaFinParo = GETDATE(),
    DuracionMinutos = @DuracionMinutos,
    EsMayorA15Minutos = CASE WHEN @DuracionMinutos > 15 THEN 1 ELSE 0 END,
    Descripcion =
        CASE
            WHEN @ObservacionesCierre IS NULL OR LTRIM(RTRIM(@ObservacionesCierre)) = ''
                THEN Descripcion
            WHEN Descripcion IS NULL OR LTRIM(RTRIM(Descripcion)) = ''
                THEN @ObservacionesCierre
            ELSE Descripcion + CHAR(13) + CHAR(10) + 'Cierre: ' + @ObservacionesCierre
        END,
    UsuarioModificacionID = @UsuarioID,
    FechaModificacion = GETDATE()
WHERE ParoID = @ParoID
  AND Activo = 1;";

                await using (var cmd = new SqlCommand(sqlCerrar, cn, tx))
                {
                    cmd.Parameters.Add("@ParoID", SqlDbType.Int).Value =
                        vm.ParoID;

                    cmd.Parameters.Add("@DuracionMinutos", SqlDbType.Int).Value =
                        duracionMinutos;

                    cmd.Parameters.Add("@ObservacionesCierre", SqlDbType.NVarChar, 500).Value =
                        string.IsNullOrWhiteSpace(vm.ObservacionesCierre)
                            ? DBNull.Value
                            : vm.ObservacionesCierre.Trim();

                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value =
                        usuarioId;

                    await cmd.ExecuteNonQueryAsync();
                }

                var ejecucion = await ObtenerEjecucionOperadorAsync(
                    ejecucionProduccionId,
                    cn,
                    tx);

                if (ejecucion == null)
                {
                    await tx.RollbackAsync();
                    return NotFound();
                }

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
                    duracionMinutos > 15
                        ? "Paro cerrado. Duró más de 15 minutos y quedó registrado para seguimiento."
                        : "Paro cerrado correctamente.";

                return RedirectToAction(nameof(Captura), new { id = ejecucionProduccionId });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();

                TempData["Error"] =
                    "No fue posible cerrar el paro: " + ex.Message;

                return RedirectToAction(nameof(Index));
            }
        }

        // ============================================================
        // LECTURAS KIOSKO
        // ============================================================

        private async Task<List<ProduccionOperadorTabletVm>> ObtenerProgramasEnProduccionAsync(
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
WHERE e.Activo = 1
  AND e.EstatusID IN (@EnProduccion, @Pausado)
ORDER BY
    e.MaquinaCodigo,
    e.FechaInicioReal DESC,
    e.EjecucionProduccionID DESC;";

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

        // ============================================================
        // ESCRITURAS
        // ============================================================

        private async Task InsertarRegistroHoraAsync(
            ProduccionEjecucionVm ejecucion,
            ProduccionRegistroHoraPostVm vm,
            TimeSpan horaInicio,
            TimeSpan horaFin,
            int operadorPersonaId,
            int usuarioId,
            SqlConnection cn,
            SqlTransaction tx)
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

            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value =
                ejecucion.EjecucionProduccionID;

            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value =
                ejecucion.ProgramaProduccionID;

            cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value =
                (object?)ejecucion.SolicitudProduccionID ?? DBNull.Value;

            cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value =
                (object?)ejecucion.MaquinaID ?? DBNull.Value;

            cmd.Parameters.Add("@OperadorID", SqlDbType.Int).Value =
                operadorPersonaId;

            cmd.Parameters.Add("@FechaProduccion", SqlDbType.Date).Value =
                vm.FechaProduccion.Date;

            cmd.Parameters.Add("@HoraInicio", SqlDbType.Time).Value =
                horaInicio;

            cmd.Parameters.Add("@HoraFin", SqlDbType.Time).Value =
                horaFin;

            cmd.Parameters.Add("@CantidadOK", SqlDbType.Int).Value =
                vm.CantidadOK;

            cmd.Parameters.Add("@CantidadSospechosa", SqlDbType.Int).Value =
                vm.CantidadSospechosa;

            cmd.Parameters.Add("@CantidadScrap", SqlDbType.Int).Value =
                vm.CantidadScrap;

            cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 500).Value =
                string.IsNullOrWhiteSpace(vm.Observaciones)
                    ? DBNull.Value
                    : vm.Observaciones.Trim();

            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value =
                usuarioId;

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

        // ============================================================
        // MAPEO
        // ============================================================

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

        // ============================================================
        // HELPERS
        // ============================================================

        private bool UsuarioEnSesion()
        {
            return HttpContext.Session.GetInt32("UsuarioID").HasValue;
        }

        private int ObtenerUsuarioID()
        {
            return HttpContext.Session.GetInt32("UsuarioID") ?? 0;
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