using ERP.NSQuell.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using System.Data;

namespace ERP.NSQuell.Controllers
{
    public class PlaneacionProgramaController : Controller
    {
        private readonly IConfiguration _configuration;

        public PlaneacionProgramaController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        private string ConnectionString =>
            _configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("No se encontró la cadena de conexión DefaultConnection.");

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var lista = new List<PlaneacionProgramaIndexVm>();

            const string sql = @"
SELECT
    pp.ProgramaProduccionID,
   pp.ReleaseID,
pp.ReleaseDetalleID,
pp.SolicitudProduccionID,
pp.SolicitudProduccionDetalleID,
r.FolioRelease,

    pp.ClienteID,
    ISNULL(c.Nombre, pp.ClienteNombre) AS ClienteNombre,

    pp.ParteID,
    pp.NumeroParte,
    pp.ReferenciaSAP,
    pp.DesignacionDescripcionSAP,

pp.Color,

    pp.CantidadRequerida,
    pp.PiezasDesdePT,
    pp.CantidadProgramada,
    pp.CantidadProducida,
    pp.CantidadPendiente,

    pp.MaquinaID,
    pp.MaquinaCodigo,
    pp.MaquinaNombre,

    pp.MoldeID,
    pp.MoldeCodigo,

    pp.CondicionProduccion,
    pp.SecuenciaMaquina,

    pp.FechaInicioProgramada,
    pp.FechaFinProgramada,
    pp.HorasProgramadas,
pp.Cambio,
pp.Arranque,

    pp.EstatusID,
    pp.Observaciones,
    pp.FechaCreacion
FROM dbo.Planeacion_ProgramaProduccion pp
LEFT JOIN dbo.Planeacion_Releases r
    ON r.ReleaseID = pp.ReleaseID
LEFT JOIN dbo.ERP_Clientes c
    ON c.ClienteID = pp.ClienteID
WHERE pp.Activo = 1
ORDER BY
    pp.FechaInicioProgramada,
    pp.MaquinaCodigo,
    pp.SecuenciaMaquina,
    pp.ProgramaProduccionID;";

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            await using var cmd = new SqlCommand(sql, cn);
            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                lista.Add(MapPrograma(rd));
            }

            return View(lista);
        }

        [HttpGet]
        public async Task<IActionResult> Maquinas(DateTime? fechaDesde, DateTime? fechaHasta)
        {
            var desde = fechaDesde?.Date ?? DateTime.Today;
            var hasta = fechaHasta?.Date ?? desde.AddDays(7);

            if (hasta < desde)
                hasta = desde;

            var programas = await ObtenerProgramasPorRangoAsync(desde, hasta);

            var maquinas = programas
                .GroupBy(x => new
                {
                    x.MaquinaID,
                    x.MaquinaCodigo,
                    x.MaquinaNombre
                })
                .Select(g => new PlaneacionProgramaMaquinaVm
                {
                    MaquinaID = g.Key.MaquinaID,
                    MaquinaCodigo = g.Key.MaquinaCodigo,
                    MaquinaNombre = g.Key.MaquinaNombre,
                    Programas = g
                        .OrderBy(x => x.FechaInicioProgramada)
                        .ThenBy(x => x.SecuenciaMaquina)
                        .ThenBy(x => x.ProgramaProduccionID)
                        .ToList()
                })
                .OrderBy(x => x.MaquinaCodigo)
                .ToList();

            var vm = new PlaneacionProgramaMaquinasVm
            {
                FechaDesde = desde,
                FechaHasta = hasta,
                Maquinas = maquinas
            };

            return View(vm);
        }

        [HttpGet]
        public async Task<IActionResult> CrearDesdeNecesidad(int releaseDetalleId)
        {
            if (releaseDetalleId <= 0)
                return BadRequest();

            var vm = await ObtenerNecesidadParaProgramaAsync(releaseDetalleId);

            if (vm == null)
            {
                TempData["Error"] = "No se encontró la necesidad seleccionada.";
                return RedirectToAction("Calculadora", "PlaneacionRelease");
            }

            if (vm.PiezasAProducir <= 0)
            {
                TempData["Error"] = "La necesidad seleccionada no tiene piezas pendientes por producir.";
                return RedirectToAction("Calculadora", "PlaneacionRelease");
            }

            vm.CantidadProgramada = vm.PiezasAProducir;

            if (!vm.FechaInicioProgramada.HasValue)
                vm.FechaInicioProgramada = DateTime.Today.AddHours(8);

            if (!vm.Cambio.HasValue && vm.FechaInicioProgramada.HasValue)
                vm.Cambio = vm.FechaInicioProgramada.Value.TimeOfDay;

            if (!vm.Arranque.HasValue && vm.FechaInicioProgramada.HasValue)
                vm.Arranque = vm.FechaInicioProgramada.Value.TimeOfDay;

            if (vm.HorasProgramadas.HasValue && vm.HorasProgramadas.Value > 0)
                vm.FechaFinProgramada = vm.FechaInicioProgramada.Value.AddHours((double)vm.HorasProgramadas.Value);

            await CargarCatalogosAsync(vm);

            return View("Crear", vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Crear(PlaneacionProgramaCrearDesdeNecesidadVm vm)
        {
            var usuarioId = ObtenerUsuarioID();

            if (vm.ReleaseDetalleID <= 0)
                ModelState.AddModelError("", "No se recibió el renglón de release.");

            if (vm.CantidadProgramada <= 0)
                ModelState.AddModelError("", "La cantidad programada debe ser mayor a cero.");

            if (!vm.MaquinaID.HasValue)
                ModelState.AddModelError("", "Selecciona la máquina.");

            if (!vm.FechaInicioProgramada.HasValue)
                ModelState.AddModelError("", "Captura la fecha y hora de inicio programada.");

            if (!vm.Cambio.HasValue)
                ModelState.AddModelError(nameof(vm.Cambio), "Captura la hora de cambio de molde.");

            if (!vm.Arranque.HasValue)
                ModelState.AddModelError(nameof(vm.Arranque), "Captura la hora de arranque.");


            if (!vm.HorasProgramadas.HasValue || vm.HorasProgramadas.Value <= 0)
                ModelState.AddModelError("", "Las horas programadas deben ser mayores a cero.");

            if (vm.FechaInicioProgramada.HasValue &&
                vm.HorasProgramadas.HasValue &&
                vm.HorasProgramadas.Value > 0)
            {
                vm.FechaFinProgramada = vm.FechaInicioProgramada.Value.AddHours((double)vm.HorasProgramadas.Value);
            }

            if (!ModelState.IsValid)
            {
                await CargarCatalogosAsync(vm);
                return View(vm);
            }

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            await using var tx = await cn.BeginTransactionAsync();

            try
            {
                var existe = await ReleaseDetalleYaProgramadoAsync(
                    vm.ReleaseDetalleID,
                    cn,
                    (SqlTransaction)tx
                );

                if (existe)
                {
                    await tx.RollbackAsync();

                    TempData["Error"] = "Ese renglón de release ya fue programado.";
                    return RedirectToAction("Calculadora", "PlaneacionRelease");
                }

                await CompletarDatosProgramaAsync(vm, cn, (SqlTransaction)tx);

                var programaId = await InsertarProgramaAsync(
                    vm,
                    usuarioId,
                    cn,
                    (SqlTransaction)tx
                );

                await MarcarReleaseDetalleProgramadoAsync(
                    vm.ReleaseDetalleID,
                    programaId,
                    usuarioId,
                    cn,
                    (SqlTransaction)tx
                );

                await tx.CommitAsync();

                TempData["Success"] = "Producción programada correctamente.";
                return RedirectToAction(nameof(Maquinas));
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();

                ModelState.AddModelError("", "Error al programar producción: " + ex.Message);
                await CargarCatalogosAsync(vm);
                return View(vm);
            }
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GenerarOF(int programaProduccionId)
        {
            if (programaProduccionId <= 0)
            {
                TempData["Error"] = "No se recibió el programa de producción.";
                return RedirectToAction(nameof(Index));
            }

            var usuarioId = ObtenerUsuarioID();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            await using var tx = await cn.BeginTransactionAsync();

            try
            {
                var programa = await ObtenerProgramaParaGenerarOFAsync(
                    programaProduccionId,
                    cn,
                    (SqlTransaction)tx
                );

                if (programa == null)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "No se encontró el programa seleccionado.";
                    return RedirectToAction(nameof(Index));
                }

                if (programa.SolicitudProduccionID.HasValue)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "Este programa ya tiene una OF generada.";
                    return RedirectToAction(nameof(Index));
                }

                if (programa.CantidadProgramada <= 0)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "El programa no tiene cantidad programada válida.";
                    return RedirectToAction(nameof(Index));
                }

                var folioOF = await GenerarFolioOFAsync(cn, (SqlTransaction)tx);

                var solicitudProduccionId = await InsertarOFDedeProgramaAsync(
                    programa,
                    folioOF,
                    usuarioId,
                    cn,
                    (SqlTransaction)tx
                );

                var solicitudProduccionDetalleId = await InsertarDetalleOFDedeProgramaAsync(
                    solicitudProduccionId,
                    programa,
                    usuarioId,
                    cn,
                    (SqlTransaction)tx
                );

                await InsertarAsignacionMaquinaOFDedeProgramaAsync(
                    solicitudProduccionDetalleId,
                    programa,
                    usuarioId,
                    cn,
                    (SqlTransaction)tx
                );

                await MarcarProgramaConOFAsync(
                    programaProduccionId,
                    solicitudProduccionId,
                    solicitudProduccionDetalleId,
                    usuarioId,
                    cn,
                    (SqlTransaction)tx
                );

                if (programa.ReleaseDetalleID.HasValue)
                {
                    await MarcarReleaseDetalleConOFAsync(
                        programa.ReleaseDetalleID.Value,
                        solicitudProduccionId,
                        cn,
                        (SqlTransaction)tx
                    );
                }

                await tx.CommitAsync();

                TempData["Success"] = "OF generada correctamente desde el programa de producción.";
                return RedirectToAction("Detalle", "Planeacion", new { id = solicitudProduccionId });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();

                TempData["Error"] = "Error al generar la OF: " + ex.Message;
                return RedirectToAction(nameof(Index));
            }
        }


        private async Task<PlaneacionProgramaCrearDesdeNecesidadVm?> ObtenerNecesidadParaProgramaAsync(int releaseDetalleId)
        {
            const string sql = @"
SELECT
    d.ReleaseDetalleID,
    d.ReleaseID,
    r.FolioRelease,

    r.ClienteID,
    ISNULL(c.Nombre, r.ClienteNombre) AS ClienteNombre,

    d.ParteID,
    d.NumeroParte,
    d.ReferenciaSAP,
    d.DesignacionDescripcionSAP,

t.Color,

    d.CantidadRequerida,
    ISNULL(d.PiezasDesdePT, 0) AS PiezasDesdePT,
    ISNULL(d.PiezasAProducir, 0) AS PiezasAProducir,

    d.MaterialID,
    d.MaterialCodigo,
    d.MaterialDescripcion,
    d.PesoBrutoPieza,

    d.MPRequeridaKg,

    d.EmbalajeCodigo,
    d.EmbalajeDescripcion,
    d.PiezasPorEmbalaje,
    d.EmbalajeRequerido,

    d.MoldeID,
    d.MoldeCodigo,

    d.MaquinaSugeridaID,
    d.MaquinaSugeridaCodigo,
    d.MaquinaSugeridaNombre,

    d.ObjetivoHora,
    d.HorasNecesarias,
    d.FechaInicioSugerida,
    d.FechaFinEstimada
FROM dbo.Planeacion_ReleaseDetalle d
INNER JOIN dbo.Planeacion_Releases r
    ON r.ReleaseID = d.ReleaseID
LEFT JOIN dbo.ERP_Clientes c
    ON c.ClienteID = r.ClienteID
LEFT JOIN dbo.ERP_ParteDatosTecnicos t
    ON t.ParteID = d.ParteID
   AND t.Activo = 1
WHERE d.ReleaseDetalleID = @ReleaseDetalleID
  AND d.Activo = 1
  AND r.Activo = 1;";

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value = releaseDetalleId;

            await using var rd = await cmd.ExecuteReaderAsync();

            if (!await rd.ReadAsync())
                return null;

            return new PlaneacionProgramaCrearDesdeNecesidadVm
            {
                ReleaseDetalleID = Convert.ToInt32(rd["ReleaseDetalleID"]),
                ReleaseID = rd["ReleaseID"] == DBNull.Value ? null : Convert.ToInt32(rd["ReleaseID"]),
                FolioRelease = rd["FolioRelease"] as string,

                ClienteID = rd["ClienteID"] == DBNull.Value ? null : Convert.ToInt32(rd["ClienteID"]),
                ClienteNombre = rd["ClienteNombre"] as string,

                ParteID = rd["ParteID"] == DBNull.Value ? null : Convert.ToInt32(rd["ParteID"]),
                NumeroParte = rd["NumeroParte"] as string,
                ReferenciaSAP = rd["ReferenciaSAP"] as string,
                DesignacionDescripcionSAP = rd["DesignacionDescripcionSAP"] as string,


                Color = rd["Color"] as string,

                CantidadRequerida = Convert.ToInt32(rd["CantidadRequerida"]),
                PiezasDesdePT = Convert.ToInt32(rd["PiezasDesdePT"]),
                PiezasAProducir = Convert.ToInt32(rd["PiezasAProducir"]),

                MaterialID = rd["MaterialID"] == DBNull.Value ? null : Convert.ToInt32(rd["MaterialID"]),
                MaterialCodigo = rd["MaterialCodigo"] as string,
                MaterialDescripcion = rd["MaterialDescripcion"] as string,
                PesoBrutoPieza = rd["PesoBrutoPieza"] == DBNull.Value ? null : Convert.ToDecimal(rd["PesoBrutoPieza"]),

                CantidadMpKg = rd["MPRequeridaKg"] == DBNull.Value ? null : Convert.ToDecimal(rd["MPRequeridaKg"]),

                EmbalajeCodigo = rd["EmbalajeCodigo"] as string,
                EmbalajeDescripcion = rd["EmbalajeDescripcion"] as string,
                PiezasPorEmbalaje = rd["PiezasPorEmbalaje"] == DBNull.Value ? null : Convert.ToDecimal(rd["PiezasPorEmbalaje"]),
                CantidadEmbalajes = rd["EmbalajeRequerido"] == DBNull.Value ? null : Convert.ToDecimal(rd["EmbalajeRequerido"]),

                MoldeID = rd["MoldeID"] == DBNull.Value ? null : Convert.ToInt32(rd["MoldeID"]),
                MoldeCodigo = rd["MoldeCodigo"] as string,

                MaquinaID = rd["MaquinaSugeridaID"] == DBNull.Value ? null : Convert.ToInt32(rd["MaquinaSugeridaID"]),
                MaquinaCodigo = rd["MaquinaSugeridaCodigo"] as string,
                MaquinaNombre = rd["MaquinaSugeridaNombre"] as string,

                ObjetivoHora = rd["ObjetivoHora"] == DBNull.Value ? null : Convert.ToInt32(rd["ObjetivoHora"]),
                HorasProgramadas = rd["HorasNecesarias"] == DBNull.Value ? null : Convert.ToDecimal(rd["HorasNecesarias"]),
                FechaInicioProgramada = DateTime.Today.AddHours(8),
                FechaFinProgramada = rd["FechaFinEstimada"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaFinEstimada"])


            };
        }

        private async Task CompletarDatosProgramaAsync(
            PlaneacionProgramaCrearDesdeNecesidadVm vm,
            SqlConnection cn,
            SqlTransaction tx)
        {
            if (vm.MaquinaID.HasValue)
            {
                const string sqlMaq = @"
SELECT TOP 1
    Codigo,
    Nombre
FROM dbo.ERP_Maquinas
WHERE MaquinaID = @MaquinaID;";

                await using var cmd = new SqlCommand(sqlMaq, cn, tx);
                cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = vm.MaquinaID.Value;

                await using var rd = await cmd.ExecuteReaderAsync();

                if (await rd.ReadAsync())
                {
                    vm.MaquinaCodigo = rd["Codigo"] as string;
                    vm.MaquinaNombre = rd["Nombre"] as string;
                }
            }

            if (vm.MoldeID.HasValue)
            {
                const string sqlMolde = @"
SELECT TOP 1
    CodigoMolde
FROM dbo.ERP_Moldes
WHERE MoldeID = @MoldeID;";

                await using var cmd = new SqlCommand(sqlMolde, cn, tx);
                cmd.Parameters.Add("@MoldeID", SqlDbType.Int).Value = vm.MoldeID.Value;

                await using var rd = await cmd.ExecuteReaderAsync();

                if (await rd.ReadAsync())
                {
                    vm.MoldeCodigo = rd["CodigoMolde"] as string;
                }
            }

            if (vm.FechaInicioProgramada.HasValue &&
                vm.HorasProgramadas.HasValue &&
                vm.HorasProgramadas.Value > 0)
            {
                vm.FechaFinProgramada = vm.FechaInicioProgramada.Value.AddHours((double)vm.HorasProgramadas.Value);
            }
        }

        private async Task<int> InsertarProgramaAsync(
            PlaneacionProgramaCrearDesdeNecesidadVm vm,
            int usuarioId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            var secuencia = await ObtenerSiguienteSecuenciaMaquinaAsync(
                vm.MaquinaID,
                cn,
                tx
            );

            const string sql = @"
INSERT INTO dbo.Planeacion_ProgramaProduccion
(
    ReleaseID,
    ReleaseDetalleID,

    ClienteID,
    ClienteNombre,

    ParteID,
    NumeroParte,
    ReferenciaSAP,
    DesignacionDescripcionSAP,

Color,

    CantidadRequerida,
    PiezasDesdePT,
    CantidadProgramada,
    CantidadProducida,

    MaquinaID,
    MaquinaCodigo,
    MaquinaNombre,

    MoldeID,
    MoldeCodigo,

    CondicionProduccion,
    SecuenciaMaquina,

    FechaInicioProgramada,
    FechaFinProgramada,
    HorasProgramadas,
Cambio,
Arranque,

    ObjetivoHora,
    Ciclo,
    Cavidades,
    PesoBrutoPieza,

    MaterialID,
    MaterialCodigo,
    MaterialDescripcion,
    CantidadMpKg,

    EmbalajeCodigo,
    EmbalajeDescripcion,
    PiezasPorEmbalaje,
    CantidadEmbalajes,

    EstatusID,
    Observaciones,
    UsuarioCreacionID,
    FechaCreacion,
    Activo
)
OUTPUT INSERTED.ProgramaProduccionID
VALUES
(
    @ReleaseID,
    @ReleaseDetalleID,

    @ClienteID,
    @ClienteNombre,

    @ParteID,
    @NumeroParte,
    @ReferenciaSAP,
    @DesignacionDescripcionSAP,

@Color,

    @CantidadRequerida,
    @PiezasDesdePT,
    @CantidadProgramada,
    0,

    @MaquinaID,
    @MaquinaCodigo,
    @MaquinaNombre,

    @MoldeID,
    @MoldeCodigo,

    @CondicionProduccion,
    @SecuenciaMaquina,

    @FechaInicioProgramada,
    @FechaFinProgramada,
    @HorasProgramadas,
@Cambio,
@Arranque,

    @ObjetivoHora,
    @Ciclo,
    @Cavidades,
    @PesoBrutoPieza,

    @MaterialID,
    @MaterialCodigo,
    @MaterialDescripcion,
    @CantidadMpKg,

    @EmbalajeCodigo,
    @EmbalajeDescripcion,
    @PiezasPorEmbalaje,
    @CantidadEmbalajes,

    @EstatusID,
    @Observaciones,
    @UsuarioCreacionID,
    GETDATE(),
    1
);";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@ReleaseID", SqlDbType.Int).Value = (object?)vm.ReleaseID ?? DBNull.Value;
            cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value = vm.ReleaseDetalleID;

            cmd.Parameters.Add("@ClienteID", SqlDbType.Int).Value = (object?)vm.ClienteID ?? DBNull.Value;
            cmd.Parameters.Add("@ClienteNombre", SqlDbType.NVarChar, 200).Value = (object?)vm.ClienteNombre ?? DBNull.Value;

            cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value = (object?)vm.ParteID ?? DBNull.Value;
            cmd.Parameters.Add("@NumeroParte", SqlDbType.NVarChar, 120).Value = (object?)vm.NumeroParte ?? DBNull.Value;
            cmd.Parameters.Add("@ReferenciaSAP", SqlDbType.NVarChar, 150).Value = (object?)vm.ReferenciaSAP ?? DBNull.Value;
            cmd.Parameters.Add("@DesignacionDescripcionSAP", SqlDbType.NVarChar, 300).Value = (object?)vm.DesignacionDescripcionSAP ?? DBNull.Value;

            cmd.Parameters.Add("@Color", SqlDbType.NVarChar, 100).Value =
    (object?)vm.Color ?? DBNull.Value;

            cmd.Parameters.Add("@CantidadRequerida", SqlDbType.Int).Value = vm.CantidadRequerida;
            cmd.Parameters.Add("@PiezasDesdePT", SqlDbType.Int).Value = vm.PiezasDesdePT;
            cmd.Parameters.Add("@CantidadProgramada", SqlDbType.Int).Value = vm.CantidadProgramada;

            cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = (object?)vm.MaquinaID ?? DBNull.Value;
            cmd.Parameters.Add("@MaquinaCodigo", SqlDbType.NVarChar, 100).Value = (object?)vm.MaquinaCodigo ?? DBNull.Value;
            cmd.Parameters.Add("@MaquinaNombre", SqlDbType.NVarChar, 200).Value = (object?)vm.MaquinaNombre ?? DBNull.Value;

            cmd.Parameters.Add("@MoldeID", SqlDbType.Int).Value = (object?)vm.MoldeID ?? DBNull.Value;
            cmd.Parameters.Add("@MoldeCodigo", SqlDbType.NVarChar, 100).Value = (object?)vm.MoldeCodigo ?? DBNull.Value;

            cmd.Parameters.Add("@CondicionProduccion", SqlDbType.NVarChar, 20).Value = (object?)vm.CondicionProduccion ?? DBNull.Value;
            cmd.Parameters.Add("@SecuenciaMaquina", SqlDbType.Int).Value = (object?)secuencia ?? DBNull.Value;

            cmd.Parameters.Add("@FechaInicioProgramada", SqlDbType.DateTime).Value = (object?)vm.FechaInicioProgramada ?? DBNull.Value;
            cmd.Parameters.Add("@FechaFinProgramada", SqlDbType.DateTime).Value = (object?)vm.FechaFinProgramada ?? DBNull.Value;

            AddDecimal(cmd, "@HorasProgramadas", vm.HorasProgramadas, 18, 2);
            cmd.Parameters.Add("@Cambio", SqlDbType.Time).Value =
    (object?)vm.Cambio ?? DBNull.Value;

            cmd.Parameters.Add("@Arranque", SqlDbType.Time).Value =
                (object?)vm.Arranque ?? DBNull.Value;

            cmd.Parameters.Add("@ObjetivoHora", SqlDbType.Int).Value = (object?)vm.ObjetivoHora ?? DBNull.Value;
            cmd.Parameters.Add("@Ciclo", SqlDbType.NVarChar, 50).Value = (object?)vm.Ciclo ?? DBNull.Value;
            cmd.Parameters.Add("@Cavidades", SqlDbType.Int).Value = (object?)vm.Cavidades ?? DBNull.Value;

            AddDecimal(cmd, "@PesoBrutoPieza", vm.PesoBrutoPieza, 18, 6);

            cmd.Parameters.Add("@MaterialID", SqlDbType.Int).Value = (object?)vm.MaterialID ?? DBNull.Value;
            cmd.Parameters.Add("@MaterialCodigo", SqlDbType.NVarChar, 100).Value = (object?)vm.MaterialCodigo ?? DBNull.Value;
            cmd.Parameters.Add("@MaterialDescripcion", SqlDbType.NVarChar, 250).Value = (object?)vm.MaterialDescripcion ?? DBNull.Value;

            AddDecimal(cmd, "@CantidadMpKg", vm.CantidadMpKg, 18, 4);

            cmd.Parameters.Add("@EmbalajeCodigo", SqlDbType.NVarChar, 100).Value = (object?)vm.EmbalajeCodigo ?? DBNull.Value;
            cmd.Parameters.Add("@EmbalajeDescripcion", SqlDbType.NVarChar, 250).Value = (object?)vm.EmbalajeDescripcion ?? DBNull.Value;

            AddDecimal(cmd, "@PiezasPorEmbalaje", vm.PiezasPorEmbalaje, 18, 4);
            AddDecimal(cmd, "@CantidadEmbalajes", vm.CantidadEmbalajes, 18, 4);

            cmd.Parameters.Add("@EstatusID", SqlDbType.Int).Value = PlaneacionProgramaEstatus.Programado;
            cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 500).Value = (object?)vm.Observaciones ?? DBNull.Value;
            cmd.Parameters.Add("@UsuarioCreacionID", SqlDbType.Int).Value = usuarioId;

            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        private async Task<int?> ObtenerSiguienteSecuenciaMaquinaAsync(
            int? maquinaId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            if (!maquinaId.HasValue)
                return null;

            const string sql = @"
SELECT ISNULL(MAX(SecuenciaMaquina), 0) + 1
FROM dbo.Planeacion_ProgramaProduccion
WHERE MaquinaID = @MaquinaID
  AND Activo = 1
  AND EstatusID NOT IN (5, 9, 99);";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = maquinaId.Value;

            var result = await cmd.ExecuteScalarAsync();

            return result == null || result == DBNull.Value
                ? 1
                : Convert.ToInt32(result);
        }

        private async Task<bool> ReleaseDetalleYaProgramadoAsync(
            int releaseDetalleId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
SELECT COUNT(1)
FROM dbo.Planeacion_ProgramaProduccion
WHERE ReleaseDetalleID = @ReleaseDetalleID
  AND Activo = 1
  AND EstatusID NOT IN (99);";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value = releaseDetalleId;

            var result = Convert.ToInt32(await cmd.ExecuteScalarAsync());

            return result > 0;
        }

        private async Task MarcarReleaseDetalleProgramadoAsync(
            int releaseDetalleId,
            int programaId,
            int usuarioId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
UPDATE dbo.Planeacion_ReleaseDetalle
SET
    ProgramaProduccionID = @ProgramaProduccionID,
    FechaProgramado = GETDATE(),
    UsuarioProgramoID = @UsuarioProgramoID,
    EstatusID = 3,
    UsuarioModificacionID = @UsuarioProgramoID,
    FechaModificacion = GETDATE()
WHERE ReleaseDetalleID = @ReleaseDetalleID;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaId;
            cmd.Parameters.Add("@UsuarioProgramoID", SqlDbType.Int).Value = usuarioId;
            cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value = releaseDetalleId;

            await cmd.ExecuteNonQueryAsync();
        }

        private async Task<List<PlaneacionProgramaMaquinaVm>> ObtenerMaquinasAsync(SqlConnection cn)
        {
            var lista = new List<PlaneacionProgramaMaquinaVm>();

            const string sql = @"
SELECT
    MaquinaID,
    Codigo,
    Nombre
FROM dbo.ERP_Maquinas
WHERE Activo = 1
ORDER BY Codigo;";

            await using var cmd = new SqlCommand(sql, cn);
            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                lista.Add(new PlaneacionProgramaMaquinaVm
                {
                    MaquinaID = rd["MaquinaID"] == DBNull.Value ? null : Convert.ToInt32(rd["MaquinaID"]),
                    MaquinaCodigo = rd["Codigo"] as string ?? "",
                    MaquinaNombre = rd["Nombre"] as string ?? ""
                });
            }

            return lista;
        }

        private async Task<List<PlaneacionProgramaIndexVm>> ObtenerProgramasPorRangoAsync(
    DateTime fechaDesde,
    DateTime fechaHasta)
        {
            var lista = new List<PlaneacionProgramaIndexVm>();

            const string sql = @"
SELECT
    pp.ProgramaProduccionID,
    pp.ReleaseID,
pp.ReleaseDetalleID,
pp.SolicitudProduccionID,
pp.SolicitudProduccionDetalleID,
r.FolioRelease,

    pp.ClienteID,
    ISNULL(c.Nombre, pp.ClienteNombre) AS ClienteNombre,

    pp.ParteID,
    pp.NumeroParte,
    pp.ReferenciaSAP,
    pp.DesignacionDescripcionSAP,

pp.Color,

    pp.CantidadRequerida,
    pp.PiezasDesdePT,
    pp.CantidadProgramada,
    pp.CantidadProducida,
    pp.CantidadPendiente,

    pp.MaquinaID,
    pp.MaquinaCodigo,
    pp.MaquinaNombre,

    pp.MoldeID,
    pp.MoldeCodigo,

    pp.CondicionProduccion,
    pp.SecuenciaMaquina,

    pp.FechaInicioProgramada,
    pp.FechaFinProgramada,
    pp.HorasProgramadas,
pp.Cambio,
pp.Arranque,

    pp.EstatusID,
    pp.Observaciones,
    pp.FechaCreacion
FROM dbo.Planeacion_ProgramaProduccion pp
LEFT JOIN dbo.Planeacion_Releases r
    ON r.ReleaseID = pp.ReleaseID
LEFT JOIN dbo.ERP_Clientes c
    ON c.ClienteID = pp.ClienteID
WHERE pp.Activo = 1
  AND (
        pp.FechaInicioProgramada < DATEADD(DAY, 1, @FechaHasta)
    AND ISNULL(pp.FechaFinProgramada, pp.FechaInicioProgramada) >= @FechaDesde
)
ORDER BY
    pp.MaquinaCodigo,
    pp.FechaInicioProgramada,
    pp.SecuenciaMaquina;";

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@FechaDesde", SqlDbType.Date).Value = fechaDesde.Date;
            cmd.Parameters.Add("@FechaHasta", SqlDbType.Date).Value = fechaHasta.Date;

            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                lista.Add(MapPrograma(rd));
            }

            return lista;
        }
        
        private static PlaneacionProgramaIndexVm MapPrograma(SqlDataReader rd)
        {
            return new PlaneacionProgramaIndexVm
            {
                ProgramaProduccionID = Convert.ToInt32(rd["ProgramaProduccionID"]),

                ReleaseID = rd["ReleaseID"] == DBNull.Value ? null : Convert.ToInt32(rd["ReleaseID"]),
                ReleaseDetalleID = rd["ReleaseDetalleID"] == DBNull.Value ? null : Convert.ToInt32(rd["ReleaseDetalleID"]),

                SolicitudProduccionID = rd["SolicitudProduccionID"] == DBNull.Value ? null : Convert.ToInt32(rd["SolicitudProduccionID"]),
                SolicitudProduccionDetalleID = rd["SolicitudProduccionDetalleID"] == DBNull.Value ? null : Convert.ToInt32(rd["SolicitudProduccionDetalleID"]),

                FolioRelease = rd["FolioRelease"] as string,

                ClienteID = rd["ClienteID"] == DBNull.Value ? null : Convert.ToInt32(rd["ClienteID"]),
                ClienteNombre = rd["ClienteNombre"] as string,

                ParteID = rd["ParteID"] == DBNull.Value ? null : Convert.ToInt32(rd["ParteID"]),
                NumeroParte = rd["NumeroParte"] as string,
                ReferenciaSAP = rd["ReferenciaSAP"] as string,
                DesignacionDescripcionSAP = rd["DesignacionDescripcionSAP"] as string,

                Color = rd["Color"] as string,

                CantidadRequerida = Convert.ToInt32(rd["CantidadRequerida"]),
                PiezasDesdePT = Convert.ToInt32(rd["PiezasDesdePT"]),
                CantidadProgramada = Convert.ToInt32(rd["CantidadProgramada"]),
                CantidadProducida = Convert.ToInt32(rd["CantidadProducida"]),
                CantidadPendiente = Convert.ToInt32(rd["CantidadPendiente"]),

                MaquinaID = rd["MaquinaID"] == DBNull.Value ? null : Convert.ToInt32(rd["MaquinaID"]),
                MaquinaCodigo = rd["MaquinaCodigo"] as string,
                MaquinaNombre = rd["MaquinaNombre"] as string,

                MoldeID = rd["MoldeID"] == DBNull.Value ? null : Convert.ToInt32(rd["MoldeID"]),
                MoldeCodigo = rd["MoldeCodigo"] as string,

                CondicionProduccion = rd["CondicionProduccion"] as string,
                SecuenciaMaquina = rd["SecuenciaMaquina"] == DBNull.Value ? null : Convert.ToInt32(rd["SecuenciaMaquina"]),

                FechaInicioProgramada = rd["FechaInicioProgramada"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaInicioProgramada"]),
                FechaFinProgramada = rd["FechaFinProgramada"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaFinProgramada"]),
                HorasProgramadas = rd["HorasProgramadas"] == DBNull.Value ? null : Convert.ToDecimal(rd["HorasProgramadas"]),
                Cambio = rd["Cambio"] == DBNull.Value ? null : (TimeSpan)rd["Cambio"],
                Arranque = rd["Arranque"] == DBNull.Value ? null : (TimeSpan)rd["Arranque"],

                EstatusID = Convert.ToInt32(rd["EstatusID"]),
                Observaciones = rd["Observaciones"] as string,
                FechaCreacion = Convert.ToDateTime(rd["FechaCreacion"])
            };
        }

        private async Task CargarCatalogosAsync(PlaneacionProgramaCrearDesdeNecesidadVm vm)
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            vm.Maquinas = await CargarSelectAsync(
                cn,
                @"SELECT 
                    MaquinaID AS Id,
                    Codigo + ' | ' + ISNULL(Nombre, '') AS Texto
                  FROM dbo.ERP_Maquinas
                  WHERE Activo = 1
                  ORDER BY Codigo;"
            );

            vm.Moldes = await CargarSelectAsync(
                cn,
                @"SELECT 
                    MoldeID AS Id,
                    CodigoMolde AS Texto
                  FROM dbo.ERP_Moldes
                  WHERE Activo = 1
                  ORDER BY CodigoMolde;"
            );

            vm.Condiciones = PlaneacionProgramaCondicion.SelectList();
        }

        private static async Task<List<SelectListItem>> CargarSelectAsync(SqlConnection cn, string sql)
        {
            var lista = new List<SelectListItem>();

            await using var cmd = new SqlCommand(sql, cn);
            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                lista.Add(new SelectListItem
                {
                    Value = rd["Id"].ToString(),
                    Text = rd["Texto"].ToString()
                });
            }

            return lista;
        }

        private int ObtenerUsuarioID()
        {
            return HttpContext.Session.GetInt32("UsuarioID") ?? 0;
        }

        private static void AddDecimal(
            SqlCommand cmd,
            string name,
            decimal? value,
            byte precision,
            byte scale)
        {
            var p = cmd.Parameters.Add(name, SqlDbType.Decimal);
            p.Precision = precision;
            p.Scale = scale;
            p.Value = value.HasValue ? value.Value : DBNull.Value;
        }


        private async Task<ProgramaParaOFVm?> ObtenerProgramaParaGenerarOFAsync(
    int programaProduccionId,
    SqlConnection cn,
    SqlTransaction tx)
        {
            const string sql = @"
SELECT
    pp.ProgramaProduccionID,

    pp.ReleaseID,
    pp.ReleaseDetalleID,

    pp.SolicitudProduccionID,
    pp.SolicitudProduccionDetalleID,

    pp.ClienteID,
    pp.ClienteNombre,

    pp.ParteID,
    pp.NumeroParte,
    pp.ReferenciaSAP,
    pp.DesignacionDescripcionSAP,

    pp.CantidadRequerida,
    pp.PiezasDesdePT,
    pp.CantidadProgramada,

    pp.MaquinaID,
    pp.MaquinaCodigo,
    pp.MaquinaNombre,

    pp.MoldeID,
    pp.MoldeCodigo,

    pp.CondicionProduccion,
    pp.SecuenciaMaquina,

    pp.FechaInicioProgramada,
    pp.FechaFinProgramada,
    pp.HorasProgramadas,
pp.Cambio,
pp.Arranque,

    COALESCE(pp.ObjetivoHora, t.ObjetivoHora) AS ObjetivoHora,
    COALESCE(pp.Ciclo, t.Ciclo) AS Ciclo,
    COALESCE(pp.Cavidades, t.Cavidades) AS Cavidades,
    COALESCE(pp.PesoBrutoPieza, t.PesoBrutoPieza) AS PesoBrutoPieza,

   COALESCE(NULLIF(pp.Color, ''), t.Color) AS Color,
    t.PiezasPorCaja,
    t.TipoSecado,
    t.HorasSecado,

    COALESCE(pp.MaterialID, t.MaterialID) AS MaterialID,
    COALESCE(pp.MaterialCodigo, t.MaterialCodigo) AS MaterialCodigo,
    COALESCE(pp.MaterialDescripcion, t.MaterialDescripcion) AS MaterialDescripcion,
    pp.CantidadMpKg,

    COALESCE(pp.EmbalajeCodigo, t.EmbalajeCodigo) AS EmbalajeCodigo,
    COALESCE(pp.EmbalajeDescripcion, t.EmbalajeDescripcion) AS EmbalajeDescripcion,
    COALESCE(pp.PiezasPorEmbalaje, t.PiezasPorEmbalaje) AS PiezasPorEmbalaje,
    pp.CantidadEmbalajes,

    pp.Observaciones
FROM dbo.Planeacion_ProgramaProduccion pp
LEFT JOIN dbo.ERP_ParteDatosTecnicos t
    ON t.ParteID = pp.ParteID
   AND t.Activo = 1
WHERE pp.ProgramaProduccionID = @ProgramaProduccionID
  AND pp.Activo = 1;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaProduccionId;

            await using var rd = await cmd.ExecuteReaderAsync();

            if (!await rd.ReadAsync())
                return null;

            return new ProgramaParaOFVm
            {
                ProgramaProduccionID = Convert.ToInt32(rd["ProgramaProduccionID"]),

                ReleaseID = rd["ReleaseID"] == DBNull.Value ? null : Convert.ToInt32(rd["ReleaseID"]),
                ReleaseDetalleID = rd["ReleaseDetalleID"] == DBNull.Value ? null : Convert.ToInt32(rd["ReleaseDetalleID"]),

                SolicitudProduccionID = rd["SolicitudProduccionID"] == DBNull.Value ? null : Convert.ToInt32(rd["SolicitudProduccionID"]),
                SolicitudProduccionDetalleID = rd["SolicitudProduccionDetalleID"] == DBNull.Value ? null : Convert.ToInt32(rd["SolicitudProduccionDetalleID"]),

                ClienteID = rd["ClienteID"] == DBNull.Value ? null : Convert.ToInt32(rd["ClienteID"]),
                ClienteNombre = rd["ClienteNombre"] as string,

                ParteID = rd["ParteID"] == DBNull.Value ? null : Convert.ToInt32(rd["ParteID"]),
                NumeroParte = rd["NumeroParte"] as string,
                ReferenciaSAP = rd["ReferenciaSAP"] as string,
                DesignacionDescripcionSAP = rd["DesignacionDescripcionSAP"] as string,

                CantidadRequerida = Convert.ToInt32(rd["CantidadRequerida"]),
                PiezasDesdePT = Convert.ToInt32(rd["PiezasDesdePT"]),
                CantidadProgramada = Convert.ToInt32(rd["CantidadProgramada"]),

                MaquinaID = rd["MaquinaID"] == DBNull.Value ? null : Convert.ToInt32(rd["MaquinaID"]),
                MaquinaCodigo = rd["MaquinaCodigo"] as string,
                MaquinaNombre = rd["MaquinaNombre"] as string,

                MoldeID = rd["MoldeID"] == DBNull.Value ? null : Convert.ToInt32(rd["MoldeID"]),
                MoldeCodigo = rd["MoldeCodigo"] as string,

                CondicionProduccion = rd["CondicionProduccion"] as string,
                SecuenciaMaquina = rd["SecuenciaMaquina"] == DBNull.Value ? null : Convert.ToInt32(rd["SecuenciaMaquina"]),

                FechaInicioProgramada = rd["FechaInicioProgramada"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaInicioProgramada"]),
                FechaFinProgramada = rd["FechaFinProgramada"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaFinProgramada"]),
                HorasProgramadas = rd["HorasProgramadas"] == DBNull.Value ? null : Convert.ToDecimal(rd["HorasProgramadas"]),
                Cambio = rd["Cambio"] == DBNull.Value ? null : (TimeSpan)rd["Cambio"],
                Arranque = rd["Arranque"] == DBNull.Value ? null : (TimeSpan)rd["Arranque"],

                ObjetivoHora = rd["ObjetivoHora"] == DBNull.Value ? null : Convert.ToInt32(rd["ObjetivoHora"]),
                Ciclo = rd["Ciclo"] as string,
                Cavidades = rd["Cavidades"] == DBNull.Value ? null : Convert.ToInt32(rd["Cavidades"]),
                PesoBrutoPieza = rd["PesoBrutoPieza"] == DBNull.Value ? null : Convert.ToDecimal(rd["PesoBrutoPieza"]),

                MaterialID = rd["MaterialID"] == DBNull.Value ? null : Convert.ToInt32(rd["MaterialID"]),
                MaterialCodigo = rd["MaterialCodigo"] as string,
                MaterialDescripcion = rd["MaterialDescripcion"] as string,
                CantidadMpKg = rd["CantidadMpKg"] == DBNull.Value ? null : Convert.ToDecimal(rd["CantidadMpKg"]),

                EmbalajeCodigo = rd["EmbalajeCodigo"] as string,
                EmbalajeDescripcion = rd["EmbalajeDescripcion"] as string,
                PiezasPorEmbalaje = rd["PiezasPorEmbalaje"] == DBNull.Value ? null : Convert.ToDecimal(rd["PiezasPorEmbalaje"]),
                CantidadEmbalajes = rd["CantidadEmbalajes"] == DBNull.Value ? null : Convert.ToDecimal(rd["CantidadEmbalajes"]),

                Observaciones = rd["Observaciones"] as string,

                Color = rd["Color"] as string,

                PiezasPorCaja = rd["PiezasPorCaja"] == DBNull.Value
    ? null
    : Convert.ToInt32(rd["PiezasPorCaja"]),

                TipoSecado = rd["TipoSecado"] as string,

                HorasSecado = rd["HorasSecado"] == DBNull.Value
    ? null
    : Convert.ToDecimal(rd["HorasSecado"])
            };
        }

        private async Task<string> GenerarFolioOFAsync(SqlConnection cn, SqlTransaction tx)
        {
            var yy = DateTime.Today.ToString("yy");

            const string sql = @"
SELECT ISNULL(MAX(TRY_CONVERT(INT, SUBSTRING(NumeroOFRecibida, 4, 4))), 0) + 1
FROM dbo.SolicitudesProduccion
WHERE NumeroOFRecibida LIKE 'OF-[0-9][0-9][0-9][0-9]/' + @YY;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@YY", SqlDbType.VarChar, 2).Value = yy;

            var consecutivo = Convert.ToInt32(await cmd.ExecuteScalarAsync());

            return $"OF-{consecutivo:0000}/{yy}";
        }

        private async Task<int> InsertarOFDedeProgramaAsync(
    ProgramaParaOFVm p,
    string folioOF,
    int usuarioId,
    SqlConnection cn,
    SqlTransaction tx)
        {
            const string sql = @"
INSERT INTO dbo.SolicitudesProduccion
(
    FolioSolicitud,
    NumeroOFRecibida,
    FechaSolicitud,
    FechaRequerida,
    ClienteID,
    ClienteNombre,
    OrigenSolicitud,
    Prioridad,
    EstatusID,
    NotasGenerales,
    UsuarioCreacionID,
    FechaCreacion,
    Activo,
    FechaInicioPlaneada,
    FechaFinPlaneada,
    ResponsablePlaneacionUsuarioID,
    ResponsablePlaneacionNombre,
    CostoMPTotal,
    CostoEmbalajeTotal,
    CostoTotalOF,
    VentaTotalOF,
    UtilidadEstimadaOF,
    MonedaCosto,
    ReleaseID,
    ReleaseDetalleID,
    ProgramaProduccionID,
    OrigenOF
)
OUTPUT INSERTED.SolicitudProduccionID
VALUES
(
    @FolioSolicitud,
    @NumeroOFRecibida,
    GETDATE(),
    @FechaRequerida,
    @ClienteID,
    @ClienteNombre,
    @OrigenSolicitud,
    @Prioridad,
    @EstatusID,
    @NotasGenerales,
    @UsuarioCreacionID,
    GETDATE(),
    1,
    @FechaInicioPlaneada,
    @FechaFinPlaneada,
    @ResponsablePlaneacionUsuarioID,
    @ResponsablePlaneacionNombre,
    0,
    0,
    0,
    0,
    0,
    @MonedaCosto,
    @ReleaseID,
    @ReleaseDetalleID,
    @ProgramaProduccionID,
    @OrigenOF
);";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@FolioSolicitud", SqlDbType.NVarChar, 40).Value = folioOF;
            cmd.Parameters.Add("@NumeroOFRecibida", SqlDbType.NVarChar, 80).Value = folioOF;

            cmd.Parameters.Add("@FechaRequerida", SqlDbType.Date).Value =
                (object?)p.FechaFinProgramada?.Date ?? DBNull.Value;

            cmd.Parameters.Add("@ClienteID", SqlDbType.Int).Value =
                (object?)p.ClienteID ?? DBNull.Value;

            cmd.Parameters.Add("@ClienteNombre", SqlDbType.NVarChar, 200).Value =
                (object?)p.ClienteNombre ?? DBNull.Value;

            cmd.Parameters.Add("@OrigenSolicitud", SqlDbType.NVarChar, 50).Value = "Planeación Programa";
            cmd.Parameters.Add("@Prioridad", SqlDbType.NVarChar, 30).Value = "Normal";

            cmd.Parameters.Add("@EstatusID", SqlDbType.Int).Value = PlaneacionOFEstatus.PendienteValidacionMP;

            cmd.Parameters.Add("@NotasGenerales", SqlDbType.NVarChar, 500).Value =
                (object?)$"OF generada desde Programa de Producción ID {p.ProgramaProduccionID}. {p.Observaciones}" ?? DBNull.Value;

            cmd.Parameters.Add("@UsuarioCreacionID", SqlDbType.Int).Value = usuarioId;

            cmd.Parameters.Add("@FechaInicioPlaneada", SqlDbType.DateTime).Value =
                (object?)p.FechaInicioProgramada ?? DBNull.Value;

            cmd.Parameters.Add("@FechaFinPlaneada", SqlDbType.DateTime).Value =
                (object?)p.FechaFinProgramada ?? DBNull.Value;

            cmd.Parameters.Add("@ResponsablePlaneacionUsuarioID", SqlDbType.Int).Value = usuarioId;

            cmd.Parameters.Add("@ResponsablePlaneacionNombre", SqlDbType.NVarChar, 200).Value =
                User?.Identity?.Name ?? "Sistema";

            cmd.Parameters.Add("@MonedaCosto", SqlDbType.NVarChar, 10).Value = "MXN";

            cmd.Parameters.Add("@ReleaseID", SqlDbType.Int).Value =
                (object?)p.ReleaseID ?? DBNull.Value;

            cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value =
                (object?)p.ReleaseDetalleID ?? DBNull.Value;

            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = p.ProgramaProduccionID;

            cmd.Parameters.Add("@OrigenOF", SqlDbType.NVarChar, 30).Value = "PROGRAMA";

            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        private async Task<int> InsertarDetalleOFDedeProgramaAsync(
    int solicitudProduccionId,
    ProgramaParaOFVm p,
    int usuarioId,
    SqlConnection cn,
    SqlTransaction tx)
        {
            const string sql = @"
INSERT INTO dbo.SolicitudesProduccionDetalle
(
    SolicitudProduccionID,
    Renglon,
    ParteID,
    MoldeID,
    MaquinaSugeridaID,
    DesignacionDescripcionSAP,
    ReferenciaSAP,
    CantidadPiezas,
    HorasPlaneadas,
    NumeroMoldeTexto,
    MaquinaSugeridaTexto,
    Color,
    Cavidades,
    ObjetivoHora,
    PiezasPorCaja,
    Notas,
    EstatusID,
    Activo,
    MaterialID,
    OrigenSurtido,
    PTDisponibleAlCrear,
    MPDisponibleKgAlCrear,
    AlmacenValidado,
    MensajeAlmacen,
    Ciclo,
    TipoSecado,
    HorasSecado,
    PesoBrutoPieza,
    MaterialCodigo,
    MaterialDescripcion,
    EmbalajeCodigo,
    EmbalajeDescripcion,
    PiezasPorEmbalaje,
    CantidadEmbalajes,
    CantidadMpKg,
    Cambio,
    Arranque,
    CostoMPUnitario,
    CostoMPTotal,
    MonedaCostoMP,
    UnidadCostoMP,
    CostoEmbalajeUnitario,
    CostoEmbalajeTotal,
    MonedaCostoEmbalaje,
    UnidadCostoEmbalaje,
    CostoTotalRenglon,
    PrecioVentaUnitario,
    VentaTotalRenglon,
    UtilidadEstimadaRenglon
)
OUTPUT INSERTED.SolicitudProduccionDetalleID
VALUES
(
    @SolicitudProduccionID,
    1,
    @ParteID,
    @MoldeID,
    @MaquinaSugeridaID,
    @DesignacionDescripcionSAP,
    @ReferenciaSAP,
    @CantidadPiezas,
    @HorasPlaneadas,
    @NumeroMoldeTexto,
    @MaquinaSugeridaTexto,
   @Color,
@Cavidades,
@ObjetivoHora,
@PiezasPorCaja,
    @Notas,
    @EstatusID,
    1,
    @MaterialID,
    @OrigenSurtido,
    @PTDisponibleAlCrear,
    @MPDisponibleKgAlCrear,
    0,
    @MensajeAlmacen,
    @Ciclo,
@TipoSecado,
@HorasSecado,
@PesoBrutoPieza,
    @MaterialCodigo,
    @MaterialDescripcion,
    @EmbalajeCodigo,
    @EmbalajeDescripcion,
    @PiezasPorEmbalaje,
    @CantidadEmbalajes,
    @CantidadMpKg,
    @Cambio,
@Arranque,
    0,
    0,
    'MXN',
    NULL,
    0,
    0,
    'MXN',
    NULL,
    0,
    0,
    0,
    0
);";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = solicitudProduccionId;

            cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value =
                (object?)p.ParteID ?? DBNull.Value;

            cmd.Parameters.Add("@MoldeID", SqlDbType.Int).Value =
                (object?)p.MoldeID ?? DBNull.Value;

            cmd.Parameters.Add("@MaquinaSugeridaID", SqlDbType.Int).Value =
                (object?)p.MaquinaID ?? DBNull.Value;

            cmd.Parameters.Add("@DesignacionDescripcionSAP", SqlDbType.NVarChar, 300).Value =
                (object?)p.DesignacionDescripcionSAP ?? DBNull.Value;

            cmd.Parameters.Add("@ReferenciaSAP", SqlDbType.NVarChar, 150).Value =
    !string.IsNullOrWhiteSpace(p.ReferenciaSAP)
        ? p.ReferenciaSAP
        : !string.IsNullOrWhiteSpace(p.NumeroParte)
            ? p.NumeroParte
            : DBNull.Value;

            cmd.Parameters.Add("@CantidadPiezas", SqlDbType.Int).Value = p.CantidadProgramada;

            AddDecimal(cmd, "@HorasPlaneadas", p.HorasProgramadas, 18, 2);

            cmd.Parameters.Add("@NumeroMoldeTexto", SqlDbType.NVarChar, 100).Value =
                (object?)p.MoldeCodigo ?? DBNull.Value;

            cmd.Parameters.Add("@MaquinaSugeridaTexto", SqlDbType.NVarChar, 200).Value =
                (object?)($"{p.MaquinaCodigo} {p.MaquinaNombre}".Trim()) ?? DBNull.Value;

            cmd.Parameters.Add("@Cavidades", SqlDbType.Int).Value =
                (object?)p.Cavidades ?? DBNull.Value;

            cmd.Parameters.Add("@ObjetivoHora", SqlDbType.Int).Value =
                (object?)p.ObjetivoHora ?? DBNull.Value;

            cmd.Parameters.Add("@Notas", SqlDbType.NVarChar, 500).Value =
                (object?)$"Generado desde programa ID {p.ProgramaProduccionID}. Condición: {p.CondicionProduccion}. {p.Observaciones}" ?? DBNull.Value;

            cmd.Parameters.Add("@EstatusID", SqlDbType.Int).Value = PlaneacionOFEstatus.PendienteValidacionMP;

            cmd.Parameters.Add("@MaterialID", SqlDbType.Int).Value =
                (object?)p.MaterialID ?? DBNull.Value;

            cmd.Parameters.Add("@OrigenSurtido", SqlDbType.NVarChar, 30).Value =
                p.PiezasDesdePT > 0 ? "MIXTO" : "MP";

            cmd.Parameters.Add("@PTDisponibleAlCrear", SqlDbType.Int).Value = p.PiezasDesdePT;

            AddDecimal(cmd, "@MPDisponibleKgAlCrear", null, 18, 4);

            cmd.Parameters.Add("@MensajeAlmacen", SqlDbType.NVarChar, 500).Value =
                "OF generada desde programa. Validar surtido de MP/PT en almacén.";

            cmd.Parameters.Add("@Ciclo", SqlDbType.NVarChar, 50).Value =
                (object?)p.Ciclo ?? DBNull.Value;

            AddDecimal(cmd, "@PesoBrutoPieza", p.PesoBrutoPieza, 18, 6);

            cmd.Parameters.Add("@MaterialCodigo", SqlDbType.NVarChar, 100).Value =
                (object?)p.MaterialCodigo ?? DBNull.Value;

            cmd.Parameters.Add("@MaterialDescripcion", SqlDbType.NVarChar, 250).Value =
                (object?)p.MaterialDescripcion ?? DBNull.Value;

            cmd.Parameters.Add("@EmbalajeCodigo", SqlDbType.NVarChar, 100).Value =
                (object?)p.EmbalajeCodigo ?? DBNull.Value;

            cmd.Parameters.Add("@EmbalajeDescripcion", SqlDbType.NVarChar, 250).Value =
                (object?)p.EmbalajeDescripcion ?? DBNull.Value;

            cmd.Parameters.Add("@Color", SqlDbType.NVarChar, 100).Value =
    (object?)p.Color ?? DBNull.Value;

            cmd.Parameters.Add("@PiezasPorCaja", SqlDbType.Int).Value =
                (object?)p.PiezasPorCaja ?? DBNull.Value;

            cmd.Parameters.Add("@TipoSecado", SqlDbType.NVarChar, 100).Value =
                (object?)p.TipoSecado ?? DBNull.Value;

            AddDecimal(cmd, "@HorasSecado", p.HorasSecado, 18, 2);

            AddDecimal(cmd, "@PiezasPorEmbalaje", p.PiezasPorEmbalaje, 18, 4);
            AddDecimal(cmd, "@CantidadEmbalajes", p.CantidadEmbalajes, 18, 4);
            AddDecimal(cmd, "@CantidadMpKg", p.CantidadMpKg, 18, 4);

            cmd.Parameters.Add("@Cambio", SqlDbType.Time).Value =
    (object?)p.Cambio ?? DBNull.Value;

            cmd.Parameters.Add("@Arranque", SqlDbType.Time).Value =
                (object?)p.Arranque ?? DBNull.Value;


            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }
        private async Task InsertarAsignacionMaquinaOFDedeProgramaAsync(
    int solicitudProduccionDetalleId,
    ProgramaParaOFVm p,
    int usuarioId,
    SqlConnection cn,
    SqlTransaction tx)
        {
            if (!p.MaquinaID.HasValue)
                return;

            const string sql = @"
INSERT INTO dbo.SolicitudesProduccionAsignacionMaquina
(
    SolicitudProduccionDetalleID,
    MaquinaID,
    MoldeID,
    CantidadAsignada,
    HorasEstimadas,
    Secuencia,
    CondicionProduccion,
    FechaProgramadaTentativa,
    HoraInicioTentativa,
    HoraFinTentativa,
    EstatusID,
    Observaciones,
    UsuarioCreacionID,
    FechaCreacion,
    Activo
)
VALUES
(
    @SolicitudProduccionDetalleID,
    @MaquinaID,
    @MoldeID,
    @CantidadAsignada,
    @HorasEstimadas,
    @Secuencia,
    @CondicionProduccion,
    @FechaProgramadaTentativa,
    @HoraInicioTentativa,
    @HoraFinTentativa,
    @EstatusID,
    @Observaciones,
    @UsuarioCreacionID,
    GETDATE(),
    1
);";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@SolicitudProduccionDetalleID", SqlDbType.Int).Value = solicitudProduccionDetalleId;
            cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = p.MaquinaID.Value;

            cmd.Parameters.Add("@MoldeID", SqlDbType.Int).Value =
                (object?)p.MoldeID ?? DBNull.Value;

            cmd.Parameters.Add("@CantidadAsignada", SqlDbType.Int).Value = p.CantidadProgramada;

            AddDecimal(cmd, "@HorasEstimadas", p.HorasProgramadas, 18, 2);

            cmd.Parameters.Add("@Secuencia", SqlDbType.Int).Value =
                (object?)p.SecuenciaMaquina ?? 1;

            cmd.Parameters.Add("@CondicionProduccion", SqlDbType.NVarChar, 20).Value =
                (object?)p.CondicionProduccion ?? DBNull.Value;

            cmd.Parameters.Add("@FechaProgramadaTentativa", SqlDbType.Date).Value =
                (object?)p.FechaInicioProgramada?.Date ?? DBNull.Value;

            cmd.Parameters.Add("@HoraInicioTentativa", SqlDbType.Time).Value =
                (object?)p.FechaInicioProgramada?.TimeOfDay ?? DBNull.Value;

            cmd.Parameters.Add("@HoraFinTentativa", SqlDbType.Time).Value =
                (object?)p.FechaFinProgramada?.TimeOfDay ?? DBNull.Value;

            cmd.Parameters.Add("@EstatusID", SqlDbType.Int).Value = PlaneacionOFEstatus.PendienteValidacionMP;

            cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 500).Value =
                (object?)$"Asignación generada desde programa ID {p.ProgramaProduccionID}." ?? DBNull.Value;

            cmd.Parameters.Add("@UsuarioCreacionID", SqlDbType.Int).Value = usuarioId;

            await cmd.ExecuteNonQueryAsync();
        }

        private async Task MarcarProgramaConOFAsync(
    int programaProduccionId,
    int solicitudProduccionId,
    int solicitudProduccionDetalleId,
    int usuarioId,
    SqlConnection cn,
    SqlTransaction tx)
        {
            const string sql = @"
UPDATE dbo.Planeacion_ProgramaProduccion
SET
    SolicitudProduccionID = @SolicitudProduccionID,
    SolicitudProduccionDetalleID = @SolicitudProduccionDetalleID,
    FechaGeneracionOF = GETDATE(),
    UsuarioGeneroOFID = @UsuarioGeneroOFID,
    UsuarioModificacionID = @UsuarioGeneroOFID,
    FechaModificacion = GETDATE()
WHERE ProgramaProduccionID = @ProgramaProduccionID;";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = solicitudProduccionId;
            cmd.Parameters.Add("@SolicitudProduccionDetalleID", SqlDbType.Int).Value = solicitudProduccionDetalleId;
            cmd.Parameters.Add("@UsuarioGeneroOFID", SqlDbType.Int).Value = usuarioId;
            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaProduccionId;

            await cmd.ExecuteNonQueryAsync();
        }

        private async Task MarcarReleaseDetalleConOFAsync(
    int releaseDetalleId,
    int solicitudProduccionId,
    SqlConnection cn,
    SqlTransaction tx)
        {
            const string sql = @"
UPDATE dbo.Planeacion_ReleaseDetalle
SET
    SolicitudProduccionID = @SolicitudProduccionID,
    EstatusID = 4,
    FechaModificacion = GETDATE()
WHERE ReleaseDetalleID = @ReleaseDetalleID;";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = solicitudProduccionId;
            cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value = releaseDetalleId;

            await cmd.ExecuteNonQueryAsync();
        }



        private class ProgramaParaOFVm
        {
            public int ProgramaProduccionID { get; set; }

            public int? ReleaseID { get; set; }
            public int? ReleaseDetalleID { get; set; }

            public int? SolicitudProduccionID { get; set; }
            public int? SolicitudProduccionDetalleID { get; set; }

            public int? ClienteID { get; set; }
            public string? ClienteNombre { get; set; }

            public int? ParteID { get; set; }
            public string? NumeroParte { get; set; }
            public string? ReferenciaSAP { get; set; }
            public string? DesignacionDescripcionSAP { get; set; }

            public int CantidadRequerida { get; set; }
            public int PiezasDesdePT { get; set; }
            public int CantidadProgramada { get; set; }

            public int? MaquinaID { get; set; }
            public string? MaquinaCodigo { get; set; }
            public string? MaquinaNombre { get; set; }

            public int? MoldeID { get; set; }
            public string? MoldeCodigo { get; set; }

            public string? CondicionProduccion { get; set; }
            public int? SecuenciaMaquina { get; set; }

            public DateTime? FechaInicioProgramada { get; set; }
            public DateTime? FechaFinProgramada { get; set; }
            public decimal? HorasProgramadas { get; set; }

            public int? ObjetivoHora { get; set; }
            public string? Ciclo { get; set; }
            public int? Cavidades { get; set; }
            public decimal? PesoBrutoPieza { get; set; }

            public int? MaterialID { get; set; }
            public string? MaterialCodigo { get; set; }
            public string? MaterialDescripcion { get; set; }
            public decimal? CantidadMpKg { get; set; }

            public string? EmbalajeCodigo { get; set; }
            public string? EmbalajeDescripcion { get; set; }
            public decimal? PiezasPorEmbalaje { get; set; }
            public decimal? CantidadEmbalajes { get; set; }

            public string? Observaciones { get; set; }

            public string? Color { get; set; }
            public int? PiezasPorCaja { get; set; }

            public string? TipoSecado { get; set; }
            public decimal? HorasSecado { get; set; }

            public TimeSpan? Cambio { get; set; }
            public TimeSpan? Arranque { get; set; }


        }
    }





}