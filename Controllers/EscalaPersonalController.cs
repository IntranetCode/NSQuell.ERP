using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using ERP.NSQuell.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using EscalaVM = ERP.NSQuell.Models.EscalaPersonal;

namespace ERP.NSQuell.Controllers
{
    [Authorize]
    public class EscalaPersonalController : Controller
    {
        private readonly string _connectionString;

        public EscalaPersonalController(IConfiguration configuration)
        {
            _connectionString =
                configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException(
                    "No se encontró la cadena de conexión 'DefaultConnection'.");
        }

        // ============================================================
        // INICIO Y CONSULTA
        // ============================================================

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var modelo = new EscalaVM.IndexVM
            {
                PuedeAdministrar = true
            };

            const string sql = @"
SELECT
    e.EscalaID,
    e.Folio,
    e.CodigoDocumento,
    e.VersionDocumento,
    e.FechaElaboracion,
    e.Anio,
    e.NumeroSemana,
    e.FechaInicio,
    e.FechaFin,
    e.PeriodoTrabajo,
    e.Estado,
    e.Observaciones,
    e.ElaboradoPor,
    e.FechaRegistro,
    e.FechaModificacion,
    e.ActualizadoPor,
    e.FechaPublicacion,
    e.PublicadoPor,
    e.Activo,
    ue.Username AS ElaboradoPorNombre,
    up.Username AS PublicadoPorNombre,
    (
        SELECT COUNT(*)
        FROM dbo.RRHH_EscalaAsignaciones a
        WHERE a.EscalaID = e.EscalaID
          AND a.Activo = 1
    ) AS TotalAsignaciones,
    (
        SELECT COUNT(DISTINCT a.PersonalID)
        FROM dbo.RRHH_EscalaAsignaciones a
        WHERE a.EscalaID = e.EscalaID
          AND a.Activo = 1
    ) AS TotalPersonas,
    (
        SELECT COUNT(*)
        FROM dbo.RRHH_NovedadesPersonal n
        WHERE n.EscalaID = e.EscalaID
          AND n.Activo = 1
    ) AS TotalNovedades
FROM dbo.RRHH_EscalasPersonal e
INNER JOIN dbo.Usuarios ue
    ON ue.UsuarioID = e.ElaboradoPor
LEFT JOIN dbo.Usuarios up
    ON up.UsuarioID = e.PublicadoPor
WHERE e.Activo = 1
ORDER BY e.Anio DESC, e.NumeroSemana DESC, e.EscalaID DESC;";

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            await using var command = new SqlCommand(sql, connection);
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                modelo.Escalas.Add(MapearEscala(reader));
            }

            modelo.TotalBorradores = modelo.Escalas.Count(
                x => x.Estado == EscalaVM.Estados.Borrador);
            modelo.TotalPublicadas = modelo.Escalas.Count(
                x => x.Estado == EscalaVM.Estados.Publicada);
            modelo.TotalCanceladas = modelo.Escalas.Count(
                x => x.Estado == EscalaVM.Estados.Cancelada);

            var hoy = DateTime.Today;
            modelo.EscalaSemanaActual = modelo.Escalas.FirstOrDefault(
                x => x.FechaInicio.Date <= hoy && x.FechaFin.Date >= hoy);

            return View(modelo);
        }

        [HttpGet]
        public async Task<IActionResult> Detalle(int id)
        {
            var escala = await ObtenerEscalaAsync(id);
            if (escala == null)
            {
                return NotFound();
            }

            var modelo = new EscalaVM.DetalleVM
            {
                Escala = escala,
                Turnos = await ObtenerTurnosEscalaAsync(id),
                Novedades = await ObtenerNovedadesAsync(id),
                Historial = await ObtenerHistorialAsync(id)
            };

            var asignaciones = await ObtenerAsignacionesAsync(id);
            modelo.Grupos = ConstruirGrupos(
                asignaciones,
                modelo.Turnos);

            return View(modelo);
        }

        // ============================================================
        // CREAR ESCALA
        // ============================================================

        [HttpGet]
        public async Task<IActionResult> Crear(int? anio, int? semana)
        {
            var fechaReferencia = DateTime.Today;
            var anioDestino = anio ?? ISOWeek.GetYear(fechaReferencia);
            var semanaDestino = semana ?? ISOWeek.GetWeekOfYear(fechaReferencia);
            var fechaInicio = ObtenerLunesSemanaISO(anioDestino, semanaDestino);
            var turnosBase = await ObtenerTurnosCatalogoAsync();

            var modelo = new EscalaVM.CrearVM
            {
                Escala = new EscalaVM.Escala
                {
                    Folio = GenerarFolio(anioDestino, semanaDestino),
                    CodigoDocumento = "BQ-F-PR01-10",
                    VersionDocumento = "01",
                    FechaElaboracion = DateTime.Today,
                    Anio = anioDestino,
                    NumeroSemana = semanaDestino,
                    FechaInicio = fechaInicio,
                    FechaFin = fechaInicio.AddDays(5),
                    PeriodoTrabajo = "Semanal",
                    Estado = EscalaVM.Estados.Borrador
                },
                Horarios = turnosBase
                    .Where(x => x.Activo)
                    .OrderBy(x => x.Orden)
                    .ThenBy(x => x.Nombre)
                    .Select((x, index) => new EscalaVM.HorarioCrearVM
                    {
                        TurnoOrigenID = x.TurnoID,
                        Nombre = x.Nombre,
                        HoraInicio = x.HoraInicio,
                        HoraFin = x.HoraFin,
                        CruzaDiaSiguiente = x.CruzaDiaSiguiente,
                        EsFlexible = x.EsFlexible,
                        TipoTurno = x.TipoTurno,
                        Color = x.Color,
                        Orden = index + 1
                    })
                    .ToList()
            };

            return View(modelo);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Crear(EscalaVM.CrearVM modelo)
        {
            modelo.Horarios ??= new List<EscalaVM.HorarioCrearVM>();

            for (var index = 0; index < modelo.Horarios.Count; index++)
            {
                var horario = modelo.Horarios[index];
                horario.Nombre = horario.Nombre?.Trim() ?? string.Empty;
                horario.TipoTurno = horario.TipoTurno?.Trim()
                    ?? EscalaVM.TiposTurno.Regular;
                horario.Color = string.IsNullOrWhiteSpace(horario.Color)
                    ? "#6C757D"
                    : horario.Color.Trim();
                horario.Orden = index + 1;

                if (horario.EsFlexible)
                {
                    horario.HoraInicio = null;
                    horario.HoraFin = null;
                    horario.CruzaDiaSiguiente = false;
                }
                else if (horario.HoraInicio.HasValue
                    && horario.HoraFin.HasValue)
                {
                    horario.CruzaDiaSiguiente =
                        horario.HoraFin.Value < horario.HoraInicio.Value;
                }
            }

            ValidarSemana(modelo.Escala);

            if (!ModelState.IsValid)
            {
                return View(modelo);
            }

            var usuarioID = ObtenerUsuarioID();
            if (!usuarioID.HasValue)
            {
                return Unauthorized();
            }

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var transaction =
                (SqlTransaction)await connection.BeginTransactionAsync();

            try
            {
                if (await ExisteEscalaSemanaAsync(
                        modelo.Escala.Anio,
                        modelo.Escala.NumeroSemana,
                        null,
                        connection,
                        transaction))
                {
                    ModelState.AddModelError(
                        "Escala.NumeroSemana",
                        "Ya existe una escala activa para ese año y semana.");

                    await transaction.RollbackAsync();
                    return View(modelo);
                }

                modelo.Escala.Folio = GenerarFolio(
                    modelo.Escala.Anio,
                    modelo.Escala.NumeroSemana);

                const string insertarEscala = @"
INSERT INTO dbo.RRHH_EscalasPersonal
(
    Folio,
    CodigoDocumento,
    VersionDocumento,
    FechaElaboracion,
    Anio,
    NumeroSemana,
    FechaInicio,
    FechaFin,
    PeriodoTrabajo,
    Estado,
    Observaciones,
    ElaboradoPor,
    Activo
)
OUTPUT INSERTED.EscalaID
VALUES
(
    @Folio,
    @CodigoDocumento,
    @VersionDocumento,
    @FechaElaboracion,
    @Anio,
    @NumeroSemana,
    @FechaInicio,
    @FechaFin,
    @PeriodoTrabajo,
    N'Borrador',
    @Observaciones,
    @ElaboradoPor,
    1
);";

                await using var insertar = new SqlCommand(
                    insertarEscala,
                    connection,
                    transaction);

                insertar.Parameters.AddWithValue("@Folio", modelo.Escala.Folio);
                insertar.Parameters.AddWithValue(
                    "@CodigoDocumento",
                    modelo.Escala.CodigoDocumento.Trim());
                insertar.Parameters.AddWithValue(
                    "@VersionDocumento",
                    modelo.Escala.VersionDocumento.Trim());
                insertar.Parameters.AddWithValue(
                    "@FechaElaboracion",
                    modelo.Escala.FechaElaboracion.Date);
                insertar.Parameters.AddWithValue("@Anio", modelo.Escala.Anio);
                insertar.Parameters.AddWithValue(
                    "@NumeroSemana",
                    modelo.Escala.NumeroSemana);
                insertar.Parameters.AddWithValue(
                    "@FechaInicio",
                    modelo.Escala.FechaInicio.Date);
                insertar.Parameters.AddWithValue(
                    "@FechaFin",
                    modelo.Escala.FechaFin.Date);
                insertar.Parameters.AddWithValue(
                    "@PeriodoTrabajo",
                    modelo.Escala.PeriodoTrabajo.Trim());
                insertar.Parameters.AddWithValue(
                    "@Observaciones",
                    DbValue(modelo.Escala.Observaciones));
                insertar.Parameters.AddWithValue("@ElaboradoPor", usuarioID.Value);

                var escalaID = Convert.ToInt32(
                    await insertar.ExecuteScalarAsync());

                await InsertarHorariosEscalaAsync(
                    escalaID,
                    modelo.Horarios,
                    connection,
                    transaction);

                await InsertarHistorialAsync(
                    escalaID,
                    null,
                    EscalaVM.Estados.Borrador,
                    "Creación de la escala.",
                    usuarioID.Value,
                    connection,
                    transaction);

                await transaction.CommitAsync();

                TempData["Exito"] =
                    $"La escala {modelo.Escala.Folio} se creó correctamente.";

                return RedirectToAction(nameof(Editor), new { id = escalaID });
            }
            catch (SqlException ex) when (ex.Number is 2601 or 2627)
            {
                await transaction.RollbackAsync();

                ModelState.AddModelError(
                    string.Empty,
                    "Ya existe una escala o folio con los mismos datos.");
                return View(modelo);
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        // ============================================================
        // EDITOR DE ASIGNACIONES
        // ============================================================

        [HttpGet]
        public async Task<IActionResult> Editor(int id)
        {
            var escala = await ObtenerEscalaAsync(id);
            if (escala == null)
            {
                return NotFound();
            }

            var turnos = await ObtenerTurnosEscalaAsync(id);
            var asignaciones = await ObtenerAsignacionesAsync(id);

            var modelo = new EscalaVM.EditorVM
            {
                Escala = escala,
                Turnos = turnos,
                Grupos = ConstruirGrupos(asignaciones, turnos),
                Novedades = await ObtenerNovedadesAsync(id),
                Personas = await ObtenerPersonasAsync(),
                Funciones = await ObtenerFuncionesAsync(true),
                Maquinas = await ObtenerMaquinasAsync(),
                PuedeEditar = escala.Estado == EscalaVM.Estados.Borrador,
                PuedePublicar =
                    escala.Estado == EscalaVM.Estados.Borrador
                    && asignaciones.Count > 0
            };

            return View(modelo);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GuardarAsignacion(
            EscalaVM.GuardarAsignacionVM modelo)
        {
            if (!ModelState.IsValid)
            {
                TempData["Error"] = PrimerErrorModelo();
                return RedirectToAction(nameof(Editor), new { id = modelo.EscalaID });
            }

            var usuarioID = ObtenerUsuarioID();
            if (!usuarioID.HasValue)
            {
                return Unauthorized();
            }

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var transaction =
                (SqlTransaction)await connection.BeginTransactionAsync();

            try
            {
                var escala = await ObtenerEscalaParaEdicionAsync(
                    modelo.EscalaID,
                    connection,
                    transaction);

                if (escala == null)
                {
                    await transaction.RollbackAsync();
                    return NotFound();
                }

                if (escala.Estado != EscalaVM.Estados.Borrador)
                {
                    TempData["Error"] =
                        "Solo se pueden modificar escalas en borrador.";
                    await transaction.RollbackAsync();
                    return RedirectToAction(
                        nameof(Editor),
                        new { id = modelo.EscalaID });
                }

                if (modelo.FechaInicio.Date < escala.FechaInicio.Date
                    || modelo.FechaFin.Date > escala.FechaFin.Date)
                {
                    TempData["Error"] =
                        "Las fechas de la asignación deben estar dentro del periodo de la escala.";
                    await transaction.RollbackAsync();
                    return RedirectToAction(
                        nameof(Editor),
                        new { id = modelo.EscalaID });
                }

                var errorCatalogos = await ValidarCatalogosAsignacionAsync(
                    modelo,
                    connection,
                    transaction);

                if (errorCatalogos != null)
                {
                    TempData["Error"] = errorCatalogos;
                    await transaction.RollbackAsync();
                    return RedirectToAction(
                        nameof(Editor),
                        new { id = modelo.EscalaID });
                }

                if (await ExisteCruceAsignacionAsync(
                        modelo,
                        connection,
                        transaction))
                {
                    TempData["Error"] =
                        "La persona ya tiene otra asignación en ese intervalo.";
                    await transaction.RollbackAsync();
                    return RedirectToAction(
                        nameof(Editor),
                        new { id = modelo.EscalaID });
                }

                var errorPersona = await ValidarDisponibilidadPersonaAsync(
                    modelo,
                    connection,
                    transaction);

                if (errorPersona != null)
                {
                    TempData["Error"] = errorPersona;
                    await transaction.RollbackAsync();
                    return RedirectToAction(
                        nameof(Editor),
                        new { id = modelo.EscalaID });
                }

                if (modelo.AsignacionID.HasValue)
                {
                    const string actualizar = @"
UPDATE dbo.RRHH_EscalaAsignaciones
SET
    PersonalID = @PersonalID,
    DepartamentoID = NULL,
    FuncionID = @FuncionID,
    MaquinaID = @MaquinaID,
    EscalaTurnoID = @EscalaTurnoID,
    FechaInicio = @FechaInicio,
    FechaFin = @FechaFin,
    Observaciones = @Observaciones,
    FechaModificacion = SYSDATETIME(),
    ActualizadoPor = @UsuarioID
WHERE AsignacionID = @AsignacionID
  AND EscalaID = @EscalaID
  AND Activo = 1;";

                    await using var command = new SqlCommand(
                        actualizar,
                        connection,
                        transaction);
                    AgregarParametrosAsignacion(
                        command,
                        modelo,
                        usuarioID.Value);
                    command.Parameters.AddWithValue(
                        "@AsignacionID",
                        modelo.AsignacionID.Value);

                    if (await command.ExecuteNonQueryAsync() == 0)
                    {
                        TempData["Error"] =
                            "No se encontró la asignación que intentas modificar.";
                        await transaction.RollbackAsync();
                        return RedirectToAction(
                            nameof(Editor),
                            new { id = modelo.EscalaID });
                    }
                }
                else
                {
                    const string insertar = @"
INSERT INTO dbo.RRHH_EscalaAsignaciones
(
    EscalaID,
    PersonalID,
    DepartamentoID,
    FuncionID,
    MaquinaID,
    EscalaTurnoID,
    FechaInicio,
    FechaFin,
    Observaciones,
    Activo,
    CreadoPor
)
VALUES
(
    @EscalaID,
    @PersonalID,
    NULL,
    @FuncionID,
    @MaquinaID,
    @EscalaTurnoID,
    @FechaInicio,
    @FechaFin,
    @Observaciones,
    1,
    @UsuarioID
);";

                    await using var command = new SqlCommand(
                        insertar,
                        connection,
                        transaction);
                    AgregarParametrosAsignacion(
                        command,
                        modelo,
                        usuarioID.Value);
                    await command.ExecuteNonQueryAsync();
                }

                await transaction.CommitAsync();
                TempData["Exito"] = modelo.AsignacionID.HasValue
                    ? "La asignación se actualizó correctamente."
                    : "La persona se agregó a la escala.";
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }

            return RedirectToAction(nameof(Editor), new { id = modelo.EscalaID });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EliminarAsignacion(
            int asignacionID,
            int escalaID)
        {
            var usuarioID = ObtenerUsuarioID();
            if (!usuarioID.HasValue)
            {
                return Unauthorized();
            }

            const string sql = @"
UPDATE a
SET
    a.Activo = 0,
    a.FechaModificacion = SYSDATETIME(),
    a.ActualizadoPor = @UsuarioID
FROM dbo.RRHH_EscalaAsignaciones a
INNER JOIN dbo.RRHH_EscalasPersonal e
    ON e.EscalaID = a.EscalaID
WHERE a.AsignacionID = @AsignacionID
  AND a.EscalaID = @EscalaID
  AND a.Activo = 1
  AND e.Activo = 1
  AND e.Estado = N'Borrador';";

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@AsignacionID", asignacionID);
            command.Parameters.AddWithValue("@EscalaID", escalaID);
            command.Parameters.AddWithValue("@UsuarioID", usuarioID.Value);

            var filas = await command.ExecuteNonQueryAsync();
            TempData[filas > 0 ? "Exito" : "Error"] = filas > 0
                ? "La asignación se eliminó."
                : "No fue posible eliminar la asignación.";

            return RedirectToAction(nameof(Editor), new { id = escalaID });
        }

        // ============================================================
        // NOVEDADES
        // ============================================================

        [HttpGet]
        public async Task<IActionResult> Novedades(int id)
        {
            var escala = await ObtenerEscalaAsync(id);
            if (escala == null)
            {
                return NotFound();
            }

            var modelo = new EscalaVM.NovedadesVM
            {
                Escala = escala,
                Novedades = await ObtenerNovedadesAsync(id),
                Personas = await ObtenerPersonasAsync(),
                PuedeEditar = escala.Estado == EscalaVM.Estados.Borrador
            };

            return View(modelo);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GuardarNovedad(
            EscalaVM.GuardarNovedadVM modelo)
        {
            if (!ModelState.IsValid)
            {
                TempData["Error"] = PrimerErrorModelo();
                return RedirectToAction(
                    nameof(Novedades),
                    new { id = modelo.EscalaID });
            }

            var tiposPermitidos = new[]
            {
                EscalaVM.TiposNovedad.Ingreso,
                EscalaVM.TiposNovedad.Baja,
                EscalaVM.TiposNovedad.Incapacidad,
                EscalaVM.TiposNovedad.Vacaciones,
                EscalaVM.TiposNovedad.Otra
            };

            if (!tiposPermitidos.Contains(modelo.TipoNovedad))
            {
                TempData["Error"] = "El tipo de novedad no es válido.";
                return RedirectToAction(
                    nameof(Novedades),
                    new { id = modelo.EscalaID });
            }

            var usuarioID = ObtenerUsuarioID();
            if (!usuarioID.HasValue)
            {
                return Unauthorized();
            }

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var transaction =
                (SqlTransaction)await connection.BeginTransactionAsync();

            try
            {
                var escala = await ObtenerEscalaParaEdicionAsync(
                    modelo.EscalaID,
                    connection,
                    transaction);

                if (escala == null)
                {
                    await transaction.RollbackAsync();
                    return NotFound();
                }

                if (escala.Estado != EscalaVM.Estados.Borrador)
                {
                    TempData["Error"] =
                        "Solo se pueden modificar novedades en una escala en borrador.";
                    await transaction.RollbackAsync();
                    return RedirectToAction(
                        nameof(Novedades),
                        new { id = modelo.EscalaID });
                }

                var fechaFin = modelo.FechaFin ?? modelo.FechaInicio;
                if (modelo.FechaInicio.Date < escala.FechaInicio.Date
                    || fechaFin.Date > escala.FechaFin.Date)
                {
                    TempData["Error"] =
                        "Las fechas de la novedad deben estar dentro del periodo de la escala.";
                    await transaction.RollbackAsync();
                    return RedirectToAction(
                        nameof(Novedades),
                        new { id = modelo.EscalaID });
                }

                if (modelo.TipoNovedad != EscalaVM.TiposNovedad.Ingreso
                    && await ExisteAsignacionEnFechasAsync(
                        modelo.EscalaID,
                        modelo.PersonalID,
                        modelo.FechaInicio,
                        fechaFin,
                        connection,
                        transaction))
                {
                    TempData["Error"] =
                        "La persona tiene una asignación en esas fechas. Elimínala o ajusta su intervalo antes de registrar la novedad.";
                    await transaction.RollbackAsync();
                    return RedirectToAction(
                        nameof(Novedades),
                        new { id = modelo.EscalaID });
                }

                if (modelo.NovedadID.HasValue)
                {
                    const string actualizar = @"
UPDATE dbo.RRHH_NovedadesPersonal
SET
    PersonalID = @PersonalID,
    TipoNovedad = @TipoNovedad,
    FechaInicio = @FechaInicio,
    FechaFin = @FechaFin,
    Motivo = @Motivo,
    Observaciones = @Observaciones,
    FechaModificacion = SYSDATETIME(),
    ActualizadoPor = @UsuarioID
WHERE NovedadID = @NovedadID
  AND EscalaID = @EscalaID
  AND Activo = 1;";

                    await using var command = new SqlCommand(
                        actualizar,
                        connection,
                        transaction);
                    AgregarParametrosNovedad(command, modelo, usuarioID.Value);
                    command.Parameters.AddWithValue(
                        "@NovedadID",
                        modelo.NovedadID.Value);

                    if (await command.ExecuteNonQueryAsync() == 0)
                    {
                        TempData["Error"] =
                            "No se encontró la novedad que intentas modificar.";
                        await transaction.RollbackAsync();
                        return RedirectToAction(
                            nameof(Novedades),
                            new { id = modelo.EscalaID });
                    }
                }
                else
                {
                    const string insertar = @"
INSERT INTO dbo.RRHH_NovedadesPersonal
(
    EscalaID,
    PersonalID,
    TipoNovedad,
    FechaInicio,
    FechaFin,
    Motivo,
    Observaciones,
    Activo,
    CreadoPor
)
VALUES
(
    @EscalaID,
    @PersonalID,
    @TipoNovedad,
    @FechaInicio,
    @FechaFin,
    @Motivo,
    @Observaciones,
    1,
    @UsuarioID
);";

                    await using var command = new SqlCommand(
                        insertar,
                        connection,
                        transaction);
                    AgregarParametrosNovedad(command, modelo, usuarioID.Value);
                    await command.ExecuteNonQueryAsync();
                }

                await transaction.CommitAsync();
                TempData["Exito"] = modelo.NovedadID.HasValue
                    ? "La novedad se actualizó correctamente."
                    : "La novedad se registró correctamente.";
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }

            return RedirectToAction(
                nameof(Novedades),
                new { id = modelo.EscalaID });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EliminarNovedad(
            int novedadID,
            int escalaID)
        {
            var usuarioID = ObtenerUsuarioID();
            if (!usuarioID.HasValue)
            {
                return Unauthorized();
            }

            const string sql = @"
UPDATE n
SET
    n.Activo = 0,
    n.FechaModificacion = SYSDATETIME(),
    n.ActualizadoPor = @UsuarioID
FROM dbo.RRHH_NovedadesPersonal n
INNER JOIN dbo.RRHH_EscalasPersonal e
    ON e.EscalaID = n.EscalaID
WHERE n.NovedadID = @NovedadID
  AND n.EscalaID = @EscalaID
  AND n.Activo = 1
  AND e.Activo = 1
  AND e.Estado = N'Borrador';";

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@NovedadID", novedadID);
            command.Parameters.AddWithValue("@EscalaID", escalaID);
            command.Parameters.AddWithValue("@UsuarioID", usuarioID.Value);

            var filas = await command.ExecuteNonQueryAsync();
            TempData[filas > 0 ? "Exito" : "Error"] = filas > 0
                ? "La novedad se eliminó."
                : "No fue posible eliminar la novedad.";

            return RedirectToAction(nameof(Novedades), new { id = escalaID });
        }

        // ============================================================
        // CAMBIOS DE ESTADO
        // ============================================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Publicar(int escalaID, string? comentario)
        {
            return await CambiarEstadoInternoAsync(
                escalaID,
                EscalaVM.Estados.Publicada,
                comentario);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RegresarABorrador(
            int escalaID,
            string? comentario)
        {
            return await CambiarEstadoInternoAsync(
                escalaID,
                EscalaVM.Estados.Borrador,
                comentario);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Cancelar(int escalaID, string? comentario)
        {
            return await CambiarEstadoInternoAsync(
                escalaID,
                EscalaVM.Estados.Cancelada,
                comentario);
        }

        // ============================================================
        // CATÁLOGO DE TURNOS
        // ============================================================

        [HttpGet]
        public async Task<IActionResult> Turnos()
        {
            return View(new EscalaVM.CatalogoTurnosVM
            {
                Turnos = await ObtenerTurnosCatalogoAsync(false),
                TiposTurno = CrearTiposTurno()
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GuardarTurno(
            EscalaVM.CatalogoTurnosVM modelo)
        {
            var turno = modelo.Formulario;

            if (turno.EsFlexible)
            {
                turno.HoraInicio = null;
                turno.HoraFin = null;
            }
            else if (!turno.HoraInicio.HasValue || !turno.HoraFin.HasValue)
            {
                ModelState.AddModelError(
                    "Formulario.HoraInicio",
                    "Indica la hora inicial y final del turno.");
            }

            if (!ModelState.IsValid)
            {
                modelo.Turnos = await ObtenerTurnosCatalogoAsync(false);
                modelo.TiposTurno = CrearTiposTurno();
                return View("Turnos", modelo);
            }

            var usuarioID = ObtenerUsuarioID();
            if (!usuarioID.HasValue)
            {
                return Unauthorized();
            }

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            const string validar = @"
SELECT COUNT(*)
FROM dbo.RRHH_Turnos
WHERE Nombre = @Nombre
  AND Activo = 1
  AND TurnoID <> @TurnoID;";

            await using (var validarCommand = new SqlCommand(validar, connection))
            {
                validarCommand.Parameters.AddWithValue(
                    "@Nombre",
                    turno.Nombre.Trim());
                validarCommand.Parameters.AddWithValue(
                    "@TurnoID",
                    turno.TurnoID);

                if (Convert.ToInt32(
                        await validarCommand.ExecuteScalarAsync()) > 0)
                {
                    ModelState.AddModelError(
                        "Formulario.Nombre",
                        "Ya existe un turno activo con ese nombre.");
                    modelo.Turnos = await ObtenerTurnosCatalogoAsync(false);
                    modelo.TiposTurno = CrearTiposTurno();
                    return View("Turnos", modelo);
                }
            }

            if (turno.TurnoID > 0)
            {
                const string actualizar = @"
UPDATE dbo.RRHH_Turnos
SET
    Nombre = @Nombre,
    HoraInicio = @HoraInicio,
    HoraFin = @HoraFin,
    CruzaDiaSiguiente = @CruzaDiaSiguiente,
    EsFlexible = @EsFlexible,
    TipoTurno = @TipoTurno,
    Color = @Color,
    Orden = @Orden,
    FechaModificacion = SYSDATETIME(),
    ActualizadoPor = @UsuarioID
WHERE TurnoID = @TurnoID;";

                await using var command = new SqlCommand(actualizar, connection);
                AgregarParametrosTurno(command, turno, usuarioID.Value);
                command.Parameters.AddWithValue("@TurnoID", turno.TurnoID);
                await command.ExecuteNonQueryAsync();
            }
            else
            {
                const string insertar = @"
INSERT INTO dbo.RRHH_Turnos
(
    Nombre,
    HoraInicio,
    HoraFin,
    CruzaDiaSiguiente,
    EsFlexible,
    TipoTurno,
    Color,
    Orden,
    Activo,
    CreadoPor
)
VALUES
(
    @Nombre,
    @HoraInicio,
    @HoraFin,
    @CruzaDiaSiguiente,
    @EsFlexible,
    @TipoTurno,
    @Color,
    @Orden,
    1,
    @UsuarioID
);";

                await using var command = new SqlCommand(insertar, connection);
                AgregarParametrosTurno(command, turno, usuarioID.Value);
                await command.ExecuteNonQueryAsync();
            }

            TempData["Exito"] = turno.TurnoID > 0
                ? "El turno se actualizó correctamente."
                : "El turno se creó correctamente.";

            return RedirectToAction(nameof(Turnos));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CambiarEstadoTurno(int turnoID)
        {
            var usuarioID = ObtenerUsuarioID();
            if (!usuarioID.HasValue)
            {
                return Unauthorized();
            }

            const string sql = @"
UPDATE dbo.RRHH_Turnos
SET
    Activo = CASE WHEN Activo = 1 THEN 0 ELSE 1 END,
    FechaModificacion = SYSDATETIME(),
    ActualizadoPor = @UsuarioID
WHERE TurnoID = @TurnoID;";

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@TurnoID", turnoID);
            command.Parameters.AddWithValue("@UsuarioID", usuarioID.Value);
            await command.ExecuteNonQueryAsync();

            TempData["Exito"] = "El estado del turno se actualizó.";
            return RedirectToAction(nameof(Turnos));
        }

        // ============================================================
        // CATÁLOGO DE FUNCIONES
        // ============================================================

        [HttpGet]
        public async Task<IActionResult> Funciones()
        {
            return View(new EscalaVM.CatalogoFuncionesVM
            {
                Funciones = await ObtenerFuncionesAsync(false),
                Departamentos = (await ObtenerDepartamentosAsync())
                    .Select(x => new SelectListItem
                    {
                        Value = x.DepartamentoID.ToString(),
                        Text = x.NombreDepartamento
                    })
                    .Prepend(new SelectListItem
                    {
                        Value = string.Empty,
                        Text = "Todos los departamentos"
                    })
                    .ToList()
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GuardarFuncion(
            EscalaVM.CatalogoFuncionesVM modelo)
        {
            var funcion = modelo.Formulario;

            if (!ModelState.IsValid)
            {
                modelo.Funciones = await ObtenerFuncionesAsync(false);
                modelo.Departamentos = await CrearSelectDepartamentosAsync();
                return View("Funciones", modelo);
            }

            var usuarioID = ObtenerUsuarioID();
            if (!usuarioID.HasValue)
            {
                return Unauthorized();
            }

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            const string validar = @"
SELECT COUNT(*)
FROM dbo.RRHH_FuncionesPersonal
WHERE Nombre = @Nombre
  AND
  (
      DepartamentoID = @DepartamentoID
      OR (DepartamentoID IS NULL AND @DepartamentoID IS NULL)
  )
  AND Activo = 1
  AND FuncionID <> @FuncionID;";

            await using (var validarCommand = new SqlCommand(validar, connection))
            {
                validarCommand.Parameters.AddWithValue(
                    "@Nombre",
                    funcion.Nombre.Trim());
                validarCommand.Parameters.AddWithValue(
                    "@DepartamentoID",
                    DbValue(funcion.DepartamentoID));
                validarCommand.Parameters.AddWithValue(
                    "@FuncionID",
                    funcion.FuncionID);

                if (Convert.ToInt32(
                        await validarCommand.ExecuteScalarAsync()) > 0)
                {
                    ModelState.AddModelError(
                        "Formulario.Nombre",
                        "Ya existe esa función activa para el departamento seleccionado.");
                    modelo.Funciones = await ObtenerFuncionesAsync(false);
                    modelo.Departamentos = await CrearSelectDepartamentosAsync();
                    return View("Funciones", modelo);
                }
            }

            if (funcion.FuncionID > 0)
            {
                const string actualizar = @"
UPDATE dbo.RRHH_FuncionesPersonal
SET
    Nombre = @Nombre,
    Descripcion = @Descripcion,
    DepartamentoID = @DepartamentoID,
    RequiereMaquina = @RequiereMaquina,
    Orden = @Orden,
    FechaModificacion = SYSDATETIME(),
    ActualizadoPor = @UsuarioID
WHERE FuncionID = @FuncionID;";

                await using var command = new SqlCommand(actualizar, connection);
                AgregarParametrosFuncion(command, funcion, usuarioID.Value);
                command.Parameters.AddWithValue("@FuncionID", funcion.FuncionID);
                await command.ExecuteNonQueryAsync();
            }
            else
            {
                const string insertar = @"
INSERT INTO dbo.RRHH_FuncionesPersonal
(
    Nombre,
    Descripcion,
    DepartamentoID,
    RequiereMaquina,
    Orden,
    Activo,
    CreadoPor
)
VALUES
(
    @Nombre,
    @Descripcion,
    @DepartamentoID,
    @RequiereMaquina,
    @Orden,
    1,
    @UsuarioID
);";

                await using var command = new SqlCommand(insertar, connection);
                AgregarParametrosFuncion(command, funcion, usuarioID.Value);
                await command.ExecuteNonQueryAsync();
            }

            TempData["Exito"] = funcion.FuncionID > 0
                ? "La función se actualizó correctamente."
                : "La función se creó correctamente.";

            return RedirectToAction(nameof(Funciones));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CambiarEstadoFuncion(int funcionID)
        {
            var usuarioID = ObtenerUsuarioID();
            if (!usuarioID.HasValue)
            {
                return Unauthorized();
            }

            const string sql = @"
UPDATE dbo.RRHH_FuncionesPersonal
SET
    Activo = CASE WHEN Activo = 1 THEN 0 ELSE 1 END,
    FechaModificacion = SYSDATETIME(),
    ActualizadoPor = @UsuarioID
WHERE FuncionID = @FuncionID;";

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@FuncionID", funcionID);
            command.Parameters.AddWithValue("@UsuarioID", usuarioID.Value);
            await command.ExecuteNonQueryAsync();

            TempData["Exito"] = "El estado de la función se actualizó.";
            return RedirectToAction(nameof(Funciones));
        }

        // ============================================================
        // MÉTODOS PRIVADOS: ESTADOS Y VALIDACIONES
        // ============================================================

        private async Task<IActionResult> CambiarEstadoInternoAsync(
            int escalaID,
            string estadoNuevo,
            string? comentario)
        {
            var usuarioID = ObtenerUsuarioID();
            if (!usuarioID.HasValue)
            {
                return Unauthorized();
            }

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var transaction =
                (SqlTransaction)await connection.BeginTransactionAsync();

            try
            {
                var escala = await ObtenerEscalaParaEdicionAsync(
                    escalaID,
                    connection,
                    transaction);

                if (escala == null)
                {
                    await transaction.RollbackAsync();
                    return NotFound();
                }

                var transicionValida =
                    estadoNuevo == EscalaVM.Estados.Cancelada
                    || (escala.Estado == EscalaVM.Estados.Borrador
                        && estadoNuevo == EscalaVM.Estados.Publicada)
                    || (escala.Estado == EscalaVM.Estados.Publicada
                        && estadoNuevo == EscalaVM.Estados.Borrador);

                if (!transicionValida || escala.Estado == estadoNuevo)
                {
                    TempData["Error"] =
                        $"No es posible cambiar de {escala.Estado} a {estadoNuevo}.";
                    await transaction.RollbackAsync();
                    return RedirectToAction(nameof(Detalle), new { id = escalaID });
                }

                if (estadoNuevo == EscalaVM.Estados.Publicada
                    && !await TieneAsignacionesAsync(
                        escalaID,
                        connection,
                        transaction))
                {
                    TempData["Error"] =
                        "No se puede publicar una escala sin personal asignado.";
                    await transaction.RollbackAsync();
                    return RedirectToAction(nameof(Editor), new { id = escalaID });
                }

                const string actualizar = @"
UPDATE dbo.RRHH_EscalasPersonal
SET
    Estado = @EstadoNuevo,
    FechaPublicacion =
        CASE WHEN @EstadoNuevo = N'Publicada'
             THEN SYSDATETIME()
             ELSE NULL
        END,
    PublicadoPor =
        CASE WHEN @EstadoNuevo = N'Publicada'
             THEN @UsuarioID
             ELSE NULL
        END,
    FechaModificacion = SYSDATETIME(),
    ActualizadoPor = @UsuarioID
WHERE EscalaID = @EscalaID
  AND Activo = 1;";

                await using var command = new SqlCommand(
                    actualizar,
                    connection,
                    transaction);
                command.Parameters.AddWithValue("@EstadoNuevo", estadoNuevo);
                command.Parameters.AddWithValue("@UsuarioID", usuarioID.Value);
                command.Parameters.AddWithValue("@EscalaID", escalaID);
                await command.ExecuteNonQueryAsync();

                await InsertarHistorialAsync(
                    escalaID,
                    escala.Estado,
                    estadoNuevo,
                    comentario,
                    usuarioID.Value,
                    connection,
                    transaction);

                await transaction.CommitAsync();
                TempData["Exito"] =
                    $"La escala cambió al estado {estadoNuevo}.";
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }

            return RedirectToAction(nameof(Detalle), new { id = escalaID });
        }

        private void ValidarSemana(EscalaVM.Escala escala)
        {
            if (escala.FechaFin.Date < escala.FechaInicio.Date)
            {
                ModelState.AddModelError(
                    "Escala.FechaFin",
                    "La fecha final no puede ser anterior a la fecha inicial.");
            }

            try
            {
                var lunes = ObtenerLunesSemanaISO(
                    escala.Anio,
                    escala.NumeroSemana);
                var semanaDeFecha = ISOWeek.GetWeekOfYear(
                    escala.FechaInicio.Date);
                var anioDeFecha = ISOWeek.GetYear(escala.FechaInicio.Date);

                if (semanaDeFecha != escala.NumeroSemana
                    || anioDeFecha != escala.Anio)
                {
                    ModelState.AddModelError(
                        "Escala.FechaInicio",
                        $"La fecha inicial no pertenece a la semana {escala.NumeroSemana} de {escala.Anio}. El lunes correspondiente es {lunes:dd/MM/yyyy}.");
                }
            }
            catch (ArgumentOutOfRangeException)
            {
                ModelState.AddModelError(
                    "Escala.NumeroSemana",
                    "La semana indicada no existe para el año seleccionado.");
            }
        }

        private async Task<string?> ValidarCatalogosAsignacionAsync(
            EscalaVM.GuardarAsignacionVM modelo,
            SqlConnection connection,
            SqlTransaction transaction)
        {
            const string sql = @"
SELECT
    CAST(CASE WHEN EXISTS
    (
        SELECT 1
        FROM dbo.RRHH_EscalaTurnos
        WHERE EscalaTurnoID = @EscalaTurnoID
          AND EscalaID = @EscalaID
          AND Activo = 1
    ) THEN 1 ELSE 0 END AS bit) AS TurnoValido,
    CAST(CASE WHEN EXISTS
    (
        SELECT 1
        FROM dbo.ERP_Maquinas
        WHERE MaquinaID = @MaquinaID
          AND Activo = 1
    ) THEN 1 ELSE 0 END AS bit) AS MaquinaValida,
    (
        SELECT TOP (1) RequiereMaquina
        FROM dbo.RRHH_FuncionesPersonal
        WHERE FuncionID = @FuncionID
          AND Activo = 1
    ) AS RequiereMaquina;";

            await using var command = new SqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue(
                "@EscalaTurnoID",
                modelo.EscalaTurnoID);
            command.Parameters.AddWithValue("@EscalaID", modelo.EscalaID);
            command.Parameters.AddWithValue(
                "@MaquinaID",
                DbValue(modelo.MaquinaID));
            command.Parameters.AddWithValue("@FuncionID", modelo.FuncionID);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                return "No fue posible validar la asignación.";
            }

            if (!GetBooleanValue(reader, "TurnoValido"))
            {
                return "El turno seleccionado no pertenece a esta escala.";
            }

            if (reader.IsDBNull(reader.GetOrdinal("RequiereMaquina")))
            {
                return "La función seleccionada no está activa.";
            }

            var requiereMaquina =
                GetBooleanValue(reader, "RequiereMaquina");

            if (requiereMaquina && !modelo.MaquinaID.HasValue)
            {
                return "La función seleccionada requiere una máquina.";
            }

            if (modelo.MaquinaID.HasValue
                && !GetBooleanValue(reader, "MaquinaValida"))
            {
                return "La máquina seleccionada no está activa.";
            }

            return null;
        }

        private async Task<string?> ValidarDisponibilidadPersonaAsync(
            EscalaVM.GuardarAsignacionVM modelo,
            SqlConnection connection,
            SqlTransaction transaction)
        {
            const string sql = @"
SELECT TOP (1)
    p.FechaIngreso,
    p.FechaBaja,
    p.EsColaboradorActivo
FROM dbo.Persona p
WHERE p.PersonaID = @PersonalID;

SELECT TOP (1)
    n.TipoNovedad,
    n.FechaInicio,
    n.FechaFin
FROM dbo.RRHH_NovedadesPersonal n
WHERE n.EscalaID = @EscalaID
  AND n.PersonalID = @PersonalID
  AND n.Activo = 1
  AND n.TipoNovedad IN (N'Baja', N'Incapacidad', N'Vacaciones')
  AND n.FechaInicio <= @FechaFin
  AND ISNULL(n.FechaFin, n.FechaInicio) >= @FechaInicio
ORDER BY n.FechaInicio;";

            await using var command = new SqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("@PersonalID", modelo.PersonalID);
            command.Parameters.AddWithValue("@EscalaID", modelo.EscalaID);
            command.Parameters.AddWithValue("@FechaInicio", modelo.FechaInicio.Date);
            command.Parameters.AddWithValue("@FechaFin", modelo.FechaFin.Date);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                return "La persona seleccionada no existe.";
            }

            var fechaIngreso = GetNullableDateTime(reader, "FechaIngreso");
            var fechaBaja = GetNullableDateTime(reader, "FechaBaja");

            if (!GetBooleanValue(reader, "EsColaboradorActivo"))
            {
                return "La persona seleccionada no está activa.";
            }

            if (fechaIngreso.HasValue
                && modelo.FechaInicio.Date < fechaIngreso.Value.Date)
            {
                return "No se puede asignar a la persona antes de su fecha de ingreso.";
            }

            if (fechaBaja.HasValue
                && modelo.FechaFin.Date >= fechaBaja.Value.Date)
            {
                return "No se puede asignar a la persona en o después de su fecha de baja.";
            }

            if (await reader.NextResultAsync() && await reader.ReadAsync())
            {
                var tipo = reader.GetString(reader.GetOrdinal("TipoNovedad"));
                return $"La persona tiene una novedad de {tipo} que se cruza con esas fechas.";
            }

            return null;
        }

        // ============================================================
        // MÉTODOS PRIVADOS: CONSULTAS
        // ============================================================

        private async Task<EscalaVM.Escala?> ObtenerEscalaAsync(int escalaID)
        {
            const string sql = @"
SELECT
    e.EscalaID,
    e.Folio,
    e.CodigoDocumento,
    e.VersionDocumento,
    e.FechaElaboracion,
    e.Anio,
    e.NumeroSemana,
    e.FechaInicio,
    e.FechaFin,
    e.PeriodoTrabajo,
    e.Estado,
    e.Observaciones,
    e.ElaboradoPor,
    e.FechaRegistro,
    e.FechaModificacion,
    e.ActualizadoPor,
    e.FechaPublicacion,
    e.PublicadoPor,
    e.Activo,
    ue.Username AS ElaboradoPorNombre,
    up.Username AS PublicadoPorNombre,
    (
        SELECT COUNT(*)
        FROM dbo.RRHH_EscalaAsignaciones a
        WHERE a.EscalaID = e.EscalaID
          AND a.Activo = 1
    ) AS TotalAsignaciones,
    (
        SELECT COUNT(DISTINCT a.PersonalID)
        FROM dbo.RRHH_EscalaAsignaciones a
        WHERE a.EscalaID = e.EscalaID
          AND a.Activo = 1
    ) AS TotalPersonas,
    (
        SELECT COUNT(*)
        FROM dbo.RRHH_NovedadesPersonal n
        WHERE n.EscalaID = e.EscalaID
          AND n.Activo = 1
    ) AS TotalNovedades
FROM dbo.RRHH_EscalasPersonal e
INNER JOIN dbo.Usuarios ue
    ON ue.UsuarioID = e.ElaboradoPor
LEFT JOIN dbo.Usuarios up
    ON up.UsuarioID = e.PublicadoPor
WHERE e.EscalaID = @EscalaID
  AND e.Activo = 1;";

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@EscalaID", escalaID);
            await using var reader = await command.ExecuteReaderAsync();

            return await reader.ReadAsync() ? MapearEscala(reader) : null;
        }

        private static async Task<EscalaVM.Escala?>
            ObtenerEscalaParaEdicionAsync(
                int escalaID,
                SqlConnection connection,
                SqlTransaction transaction)
        {
            const string sql = @"
SELECT
    EscalaID,
    Folio,
    CodigoDocumento,
    VersionDocumento,
    FechaElaboracion,
    Anio,
    NumeroSemana,
    FechaInicio,
    FechaFin,
    PeriodoTrabajo,
    Estado,
    Observaciones,
    ElaboradoPor,
    FechaRegistro,
    FechaModificacion,
    ActualizadoPor,
    FechaPublicacion,
    PublicadoPor,
    Activo
FROM dbo.RRHH_EscalasPersonal WITH (UPDLOCK, ROWLOCK)
WHERE EscalaID = @EscalaID
  AND Activo = 1;";

            await using var command = new SqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("@EscalaID", escalaID);
            await using var reader = await command.ExecuteReaderAsync();

            return await reader.ReadAsync() ? MapearEscala(reader) : null;
        }

        private async Task<List<EscalaVM.Turno>> ObtenerTurnosCatalogoAsync(
            bool soloActivos = true)
        {
            const string sql = @"
SELECT
    TurnoID,
    Nombre,
    HoraInicio,
    HoraFin,
    CruzaDiaSiguiente,
    EsFlexible,
    TipoTurno,
    Color,
    Orden,
    Activo,
    FechaRegistro,
    CreadoPor,
    FechaModificacion,
    ActualizadoPor
FROM dbo.RRHH_Turnos
WHERE (@SoloActivos = 0 OR Activo = 1)
ORDER BY Activo DESC, Orden, Nombre;";

            var turnos = new List<EscalaVM.Turno>();

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@SoloActivos", soloActivos);
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                turnos.Add(new EscalaVM.Turno
                {
                    TurnoID = reader.GetInt32(reader.GetOrdinal("TurnoID")),
                    Nombre = reader.GetString(reader.GetOrdinal("Nombre")),
                    HoraInicio = GetNullableTimeSpan(reader, "HoraInicio"),
                    HoraFin = GetNullableTimeSpan(reader, "HoraFin"),
                    CruzaDiaSiguiente =
                        reader.GetBoolean(
                            reader.GetOrdinal("CruzaDiaSiguiente")),
                    EsFlexible =
                        reader.GetBoolean(reader.GetOrdinal("EsFlexible")),
                    TipoTurno =
                        reader.GetString(reader.GetOrdinal("TipoTurno")),
                    Color = reader.GetString(reader.GetOrdinal("Color")),
                    Orden = reader.GetInt32(reader.GetOrdinal("Orden")),
                    Activo = reader.GetBoolean(reader.GetOrdinal("Activo")),
                    FechaRegistro =
                        reader.GetDateTime(reader.GetOrdinal("FechaRegistro")),
                    CreadoPor = GetNullableInt(reader, "CreadoPor"),
                    FechaModificacion =
                        GetNullableDateTime(reader, "FechaModificacion"),
                    ActualizadoPor =
                        GetNullableInt(reader, "ActualizadoPor")
                });
            }

            return turnos;
        }

        private async Task<List<EscalaVM.EscalaTurno>>
            ObtenerTurnosEscalaAsync(int escalaID)
        {
            const string sql = @"
SELECT
    EscalaTurnoID,
    EscalaID,
    TurnoOrigenID,
    Nombre,
    HoraInicio,
    HoraFin,
    CruzaDiaSiguiente,
    EsFlexible,
    TipoTurno,
    Color,
    Orden,
    Activo
FROM dbo.RRHH_EscalaTurnos
WHERE EscalaID = @EscalaID
  AND Activo = 1
ORDER BY Orden, EscalaTurnoID;";

            var turnos = new List<EscalaVM.EscalaTurno>();

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@EscalaID", escalaID);
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                turnos.Add(new EscalaVM.EscalaTurno
                {
                    EscalaTurnoID =
                        reader.GetInt32(reader.GetOrdinal("EscalaTurnoID")),
                    EscalaID = reader.GetInt32(reader.GetOrdinal("EscalaID")),
                    TurnoOrigenID =
                        GetNullableInt(reader, "TurnoOrigenID"),
                    Nombre = reader.GetString(reader.GetOrdinal("Nombre")),
                    HoraInicio = GetNullableTimeSpan(reader, "HoraInicio"),
                    HoraFin = GetNullableTimeSpan(reader, "HoraFin"),
                    CruzaDiaSiguiente =
                        reader.GetBoolean(
                            reader.GetOrdinal("CruzaDiaSiguiente")),
                    EsFlexible =
                        reader.GetBoolean(reader.GetOrdinal("EsFlexible")),
                    TipoTurno =
                        reader.GetString(reader.GetOrdinal("TipoTurno")),
                    Color = reader.GetString(reader.GetOrdinal("Color")),
                    Orden = reader.GetInt32(reader.GetOrdinal("Orden")),
                    Activo = reader.GetBoolean(reader.GetOrdinal("Activo"))
                });
            }

            return turnos;
        }

        private async Task<List<EscalaVM.Asignacion>>
            ObtenerAsignacionesAsync(int escalaID)
        {
            const string sql = @"
SELECT
    a.AsignacionID,
    a.EscalaID,
    a.PersonalID,
    a.DepartamentoID,
    a.FuncionID,
    a.MaquinaID,
    a.EscalaTurnoID,
    a.FechaInicio,
    a.FechaFin,
    a.Observaciones,
    a.Activo,
    a.FechaRegistro,
    a.CreadoPor,
    a.FechaModificacion,
    a.ActualizadoPor,
    CONVERT(VARCHAR(20), p.PersonaID) AS NumeroEmpleado,
    CONCAT_WS(' ', p.Nombre, p.ApellidoPaterno, p.ApellidoMaterno)
        AS NombrePersona,
    ISNULL(d.NombreDepartamento, N'Sin departamento asignado')
        AS NombreDepartamento,
    f.Nombre AS NombreFuncion,
    ISNULL(m.Nombre, N'') AS NombreMaquina,
    t.Nombre AS NombreTurno,
    t.Color AS ColorTurno
FROM dbo.RRHH_EscalaAsignaciones a
INNER JOIN dbo.Persona p
    ON p.PersonaID = a.PersonalID
LEFT JOIN dbo.Departamentos d
    ON d.DepartamentoID = a.DepartamentoID
INNER JOIN dbo.RRHH_FuncionesPersonal f
    ON f.FuncionID = a.FuncionID
LEFT JOIN dbo.ERP_Maquinas m
    ON m.MaquinaID = a.MaquinaID
INNER JOIN dbo.RRHH_EscalaTurnos t
    ON t.EscalaID = a.EscalaID
   AND t.EscalaTurnoID = a.EscalaTurnoID
WHERE a.EscalaID = @EscalaID
  AND a.Activo = 1
ORDER BY
    f.Orden,
    m.Codigo,
    t.Orden,
    p.ApellidoPaterno,
    p.ApellidoMaterno,
    p.Nombre;";

            var asignaciones = new List<EscalaVM.Asignacion>();

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@EscalaID", escalaID);
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                asignaciones.Add(new EscalaVM.Asignacion
                {
                    AsignacionID =
                        reader.GetInt32(reader.GetOrdinal("AsignacionID")),
                    EscalaID = reader.GetInt32(reader.GetOrdinal("EscalaID")),
                    PersonalID =
                        reader.GetInt32(reader.GetOrdinal("PersonalID")),
                    DepartamentoID =
                        GetNullableInt(reader, "DepartamentoID"),
                    FuncionID =
                        reader.GetInt32(reader.GetOrdinal("FuncionID")),
                    MaquinaID = GetNullableInt(reader, "MaquinaID"),
                    EscalaTurnoID =
                        reader.GetInt32(reader.GetOrdinal("EscalaTurnoID")),
                    FechaInicio =
                        reader.GetDateTime(reader.GetOrdinal("FechaInicio")),
                    FechaFin =
                        reader.GetDateTime(reader.GetOrdinal("FechaFin")),
                    Observaciones =
                        GetNullableString(reader, "Observaciones"),
                    Activo = reader.GetBoolean(reader.GetOrdinal("Activo")),
                    FechaRegistro =
                        reader.GetDateTime(reader.GetOrdinal("FechaRegistro")),
                    CreadoPor =
                        reader.GetInt32(reader.GetOrdinal("CreadoPor")),
                    FechaModificacion =
                        GetNullableDateTime(reader, "FechaModificacion"),
                    ActualizadoPor =
                        GetNullableInt(reader, "ActualizadoPor"),
                    NumeroEmpleado =
                        reader.GetString(reader.GetOrdinal("NumeroEmpleado")),
                    NombrePersona =
                        reader.GetString(reader.GetOrdinal("NombrePersona")),
                    NombreDepartamento =
                        GetNullableString(reader, "NombreDepartamento")
                            ?? "Sin departamento asignado",
                    NombreFuncion =
                        reader.GetString(reader.GetOrdinal("NombreFuncion")),
                    NombreMaquina =
                        reader.GetString(reader.GetOrdinal("NombreMaquina")),
                    NombreTurno =
                        reader.GetString(reader.GetOrdinal("NombreTurno")),
                    ColorTurno =
                        reader.GetString(reader.GetOrdinal("ColorTurno"))
                });
            }

            return asignaciones;
        }

        private async Task<List<EscalaVM.Novedad>>
            ObtenerNovedadesAsync(int escalaID)
        {
            const string sql = @"
SELECT
    n.NovedadID,
    n.EscalaID,
    n.PersonalID,
    n.TipoNovedad,
    n.FechaInicio,
    n.FechaFin,
    n.Motivo,
    n.Observaciones,
    n.Activo,
    n.FechaRegistro,
    n.CreadoPor,
    n.FechaModificacion,
    n.ActualizadoPor,
    CONVERT(VARCHAR(20), p.PersonaID) AS NumeroEmpleado,
    CONCAT_WS(' ', p.Nombre, p.ApellidoPaterno, p.ApellidoMaterno)
        AS NombrePersona
FROM dbo.RRHH_NovedadesPersonal n
INNER JOIN dbo.Persona p
    ON p.PersonaID = n.PersonalID
WHERE n.EscalaID = @EscalaID
  AND n.Activo = 1
ORDER BY n.TipoNovedad, n.FechaInicio, p.ApellidoPaterno, p.Nombre;";

            var novedades = new List<EscalaVM.Novedad>();

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@EscalaID", escalaID);
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                novedades.Add(new EscalaVM.Novedad
                {
                    NovedadID =
                        reader.GetInt32(reader.GetOrdinal("NovedadID")),
                    EscalaID = reader.GetInt32(reader.GetOrdinal("EscalaID")),
                    PersonalID =
                        reader.GetInt32(reader.GetOrdinal("PersonalID")),
                    TipoNovedad =
                        reader.GetString(reader.GetOrdinal("TipoNovedad")),
                    FechaInicio =
                        reader.GetDateTime(reader.GetOrdinal("FechaInicio")),
                    FechaFin = GetNullableDateTime(reader, "FechaFin"),
                    Motivo = GetNullableString(reader, "Motivo"),
                    Observaciones =
                        GetNullableString(reader, "Observaciones"),
                    Activo = reader.GetBoolean(reader.GetOrdinal("Activo")),
                    FechaRegistro =
                        reader.GetDateTime(reader.GetOrdinal("FechaRegistro")),
                    CreadoPor =
                        reader.GetInt32(reader.GetOrdinal("CreadoPor")),
                    FechaModificacion =
                        GetNullableDateTime(reader, "FechaModificacion"),
                    ActualizadoPor =
                        GetNullableInt(reader, "ActualizadoPor"),
                    NumeroEmpleado =
                        reader.GetString(reader.GetOrdinal("NumeroEmpleado")),
                    NombrePersona =
                        reader.GetString(reader.GetOrdinal("NombrePersona"))
                });
            }

            return novedades;
        }

        private async Task<List<EscalaVM.Historial>>
            ObtenerHistorialAsync(int escalaID)
        {
            const string sql = @"
SELECT
    h.HistorialID,
    h.EscalaID,
    h.EstadoAnterior,
    h.EstadoNuevo,
    h.Comentario,
    h.FechaMovimiento,
    h.UsuarioID,
    u.Username AS NombreUsuario
FROM dbo.RRHH_EscalaHistorial h
INNER JOIN dbo.Usuarios u
    ON u.UsuarioID = h.UsuarioID
WHERE h.EscalaID = @EscalaID
ORDER BY h.FechaMovimiento DESC, h.HistorialID DESC;";

            var historial = new List<EscalaVM.Historial>();

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@EscalaID", escalaID);
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                historial.Add(new EscalaVM.Historial
                {
                    HistorialID =
                        reader.GetInt32(reader.GetOrdinal("HistorialID")),
                    EscalaID = reader.GetInt32(reader.GetOrdinal("EscalaID")),
                    EstadoAnterior =
                        GetNullableString(reader, "EstadoAnterior"),
                    EstadoNuevo =
                        reader.GetString(reader.GetOrdinal("EstadoNuevo")),
                    Comentario =
                        GetNullableString(reader, "Comentario"),
                    FechaMovimiento =
                        reader.GetDateTime(reader.GetOrdinal("FechaMovimiento")),
                    UsuarioID =
                        reader.GetInt32(reader.GetOrdinal("UsuarioID")),
                    NombreUsuario =
                        reader.GetString(reader.GetOrdinal("NombreUsuario"))
                });
            }

            return historial;
        }

        private async Task<List<EscalaVM.PersonaOpcion>>
            ObtenerPersonasAsync()
        {
            const string sql = @"
SELECT
    p.PersonaID AS PersonalID,
    CAST(NULL AS INT) AS DepartamentoID,
    CAST(N'' AS NVARCHAR(200)) AS NombreDepartamento,
    CONVERT(VARCHAR(20), p.PersonaID) AS NumeroEmpleado,
    CONCAT_WS(' ', p.Nombre, p.ApellidoPaterno, p.ApellidoMaterno)
        AS NombreCompleto,
    ISNULL(p.Puesto, N'') AS Puesto,
    p.FechaIngreso,
    p.FechaBaja,
    p.EsColaboradorActivo AS Activo
FROM dbo.Persona p
WHERE p.EsColaboradorActivo = 1
ORDER BY p.ApellidoPaterno, p.ApellidoMaterno, p.Nombre;";

            var personas = new List<EscalaVM.PersonaOpcion>();

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                personas.Add(new EscalaVM.PersonaOpcion
                {
                    PersonalID =
                        reader.GetInt32(reader.GetOrdinal("PersonalID")),
                    DepartamentoID =
                        GetNullableInt(reader, "DepartamentoID"),
                    NombreDepartamento =
                        reader.GetString(reader.GetOrdinal("NombreDepartamento")),
                    NumeroEmpleado =
                        reader.GetString(reader.GetOrdinal("NumeroEmpleado")),
                    NombreCompleto =
                        reader.GetString(reader.GetOrdinal("NombreCompleto")),
                    Puesto = reader.GetString(reader.GetOrdinal("Puesto")),
                    FechaIngreso =
                        GetNullableDateTime(reader, "FechaIngreso"),
                    FechaBaja = GetNullableDateTime(reader, "FechaBaja"),
                    Activo = reader.GetBoolean(reader.GetOrdinal("Activo"))
                });
            }

            return personas;
        }

        private async Task<List<EscalaVM.DepartamentoOpcion>>
            ObtenerDepartamentosAsync()
        {
            const string sql = @"
SELECT DepartamentoID, NombreDepartamento
FROM dbo.Departamentos
WHERE Activo = 1
ORDER BY NombreDepartamento;";

            var departamentos = new List<EscalaVM.DepartamentoOpcion>();

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                departamentos.Add(new EscalaVM.DepartamentoOpcion
                {
                    DepartamentoID =
                        reader.GetInt32(reader.GetOrdinal("DepartamentoID")),
                    NombreDepartamento =
                        reader.GetString(
                            reader.GetOrdinal("NombreDepartamento"))
                });
            }

            return departamentos;
        }

        private async Task<List<EscalaVM.Funcion>> ObtenerFuncionesAsync(
            bool soloActivas)
        {
            const string sql = @"
SELECT
    f.FuncionID,
    f.Nombre,
    f.Descripcion,
    f.DepartamentoID,
    f.RequiereMaquina,
    f.Orden,
    f.Activo,
    f.FechaRegistro,
    f.CreadoPor,
    f.FechaModificacion,
    f.ActualizadoPor,
    ISNULL(d.NombreDepartamento, N'Todos los departamentos')
        AS NombreDepartamento
FROM dbo.RRHH_FuncionesPersonal f
LEFT JOIN dbo.Departamentos d
    ON d.DepartamentoID = f.DepartamentoID
WHERE (@SoloActivas = 0 OR f.Activo = 1)
ORDER BY f.Activo DESC, f.Orden, f.Nombre;";

            var funciones = new List<EscalaVM.Funcion>();

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@SoloActivas", soloActivas);
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                funciones.Add(new EscalaVM.Funcion
                {
                    FuncionID =
                        reader.GetInt32(reader.GetOrdinal("FuncionID")),
                    Nombre = reader.GetString(reader.GetOrdinal("Nombre")),
                    Descripcion =
                        GetNullableString(reader, "Descripcion"),
                    DepartamentoID =
                        GetNullableInt(reader, "DepartamentoID"),
                    RequiereMaquina =
                        reader.GetBoolean(
                            reader.GetOrdinal("RequiereMaquina")),
                    Orden = reader.GetInt32(reader.GetOrdinal("Orden")),
                    Activo = reader.GetBoolean(reader.GetOrdinal("Activo")),
                    FechaRegistro =
                        reader.GetDateTime(reader.GetOrdinal("FechaRegistro")),
                    CreadoPor = GetNullableInt(reader, "CreadoPor"),
                    FechaModificacion =
                        GetNullableDateTime(reader, "FechaModificacion"),
                    ActualizadoPor =
                        GetNullableInt(reader, "ActualizadoPor"),
                    NombreDepartamento =
                        reader.GetString(
                            reader.GetOrdinal("NombreDepartamento"))
                });
            }

            return funciones;
        }

        private async Task<List<EscalaVM.MaquinaOpcion>>
            ObtenerMaquinasAsync()
        {
            const string sql = @"
SELECT MaquinaID, Codigo, Nombre, Area, Activo
FROM dbo.ERP_Maquinas
WHERE Activo = 1
ORDER BY Codigo, Nombre;";

            var maquinas = new List<EscalaVM.MaquinaOpcion>();

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                maquinas.Add(new EscalaVM.MaquinaOpcion
                {
                    MaquinaID =
                        reader.GetInt32(reader.GetOrdinal("MaquinaID")),
                    Codigo = reader.GetString(reader.GetOrdinal("Codigo")),
                    Nombre = reader.GetString(reader.GetOrdinal("Nombre")),
                    Area = reader.GetString(reader.GetOrdinal("Area")),
                    Activo = reader.GetBoolean(reader.GetOrdinal("Activo"))
                });
            }

            return maquinas;
        }

        // ============================================================
        // MÉTODOS PRIVADOS: OPERACIONES SQL
        // ============================================================

        private static async Task<bool> ExisteEscalaSemanaAsync(
            int anio,
            int semana,
            int? excluirEscalaID,
            SqlConnection connection,
            SqlTransaction transaction)
        {
            const string sql = @"
SELECT COUNT(*)
FROM dbo.RRHH_EscalasPersonal
WHERE Anio = @Anio
  AND NumeroSemana = @NumeroSemana
  AND Activo = 1
  AND (@ExcluirEscalaID IS NULL OR EscalaID <> @ExcluirEscalaID);";

            await using var command = new SqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("@Anio", anio);
            command.Parameters.AddWithValue("@NumeroSemana", semana);
            command.Parameters.AddWithValue(
                "@ExcluirEscalaID",
                DbValue(excluirEscalaID));

            return Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
        }

        private static async Task InsertarHorariosEscalaAsync(
            int escalaID,
            IReadOnlyCollection<EscalaVM.HorarioCrearVM> horarios,
            SqlConnection connection,
            SqlTransaction transaction)
        {
            const string sql = @"
INSERT INTO dbo.RRHH_EscalaTurnos
(
    EscalaID,
    TurnoOrigenID,
    Nombre,
    HoraInicio,
    HoraFin,
    CruzaDiaSiguiente,
    EsFlexible,
    TipoTurno,
    Color,
    Orden,
    Activo
)
VALUES
(
    @EscalaID,
    CASE
        WHEN EXISTS
        (
            SELECT 1
            FROM dbo.RRHH_Turnos
            WHERE TurnoID = @TurnoOrigenID
        )
        THEN @TurnoOrigenID
        ELSE NULL
    END,
    @Nombre,
    @HoraInicio,
    @HoraFin,
    @CruzaDiaSiguiente,
    @EsFlexible,
    @TipoTurno,
    @Color,
    @Orden,
    1
);";

            foreach (var horario in horarios.OrderBy(x => x.Orden))
            {
                await using var command = new SqlCommand(
                    sql,
                    connection,
                    transaction);

                command.Parameters.AddWithValue("@EscalaID", escalaID);
                var turnoOrigen = command.Parameters.Add(
                    "@TurnoOrigenID",
                    SqlDbType.Int);
                turnoOrigen.Value = DbValue(horario.TurnoOrigenID);
                command.Parameters.AddWithValue("@Nombre", horario.Nombre);

                var horaInicio = command.Parameters.Add(
                    "@HoraInicio",
                    SqlDbType.Time);
                horaInicio.Value = DbValue(horario.HoraInicio);

                var horaFin = command.Parameters.Add(
                    "@HoraFin",
                    SqlDbType.Time);
                horaFin.Value = DbValue(horario.HoraFin);

                command.Parameters.AddWithValue(
                    "@CruzaDiaSiguiente",
                    horario.CruzaDiaSiguiente);
                command.Parameters.AddWithValue(
                    "@EsFlexible",
                    horario.EsFlexible);
                command.Parameters.AddWithValue(
                    "@TipoTurno",
                    horario.TipoTurno);
                command.Parameters.AddWithValue("@Color", horario.Color);
                command.Parameters.AddWithValue("@Orden", horario.Orden);

                await command.ExecuteNonQueryAsync();
            }
        }

        private static async Task InsertarHistorialAsync(
            int escalaID,
            string? estadoAnterior,
            string estadoNuevo,
            string? comentario,
            int usuarioID,
            SqlConnection connection,
            SqlTransaction transaction)
        {
            const string sql = @"
INSERT INTO dbo.RRHH_EscalaHistorial
(
    EscalaID,
    EstadoAnterior,
    EstadoNuevo,
    Comentario,
    UsuarioID
)
VALUES
(
    @EscalaID,
    @EstadoAnterior,
    @EstadoNuevo,
    @Comentario,
    @UsuarioID
);";

            await using var command = new SqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("@EscalaID", escalaID);
            command.Parameters.AddWithValue(
                "@EstadoAnterior",
                DbValue(estadoAnterior));
            command.Parameters.AddWithValue("@EstadoNuevo", estadoNuevo);
            command.Parameters.AddWithValue(
                "@Comentario",
                DbValue(comentario));
            command.Parameters.AddWithValue("@UsuarioID", usuarioID);
            await command.ExecuteNonQueryAsync();
        }

        private static async Task<bool> ExisteCruceAsignacionAsync(
            EscalaVM.GuardarAsignacionVM modelo,
            SqlConnection connection,
            SqlTransaction transaction)
        {
            const string sql = @"
SELECT COUNT(*)
FROM dbo.RRHH_EscalaAsignaciones
WHERE EscalaID = @EscalaID
  AND PersonalID = @PersonalID
  AND Activo = 1
  AND FechaInicio <= @FechaFin
  AND FechaFin >= @FechaInicio
  AND (@AsignacionID IS NULL OR AsignacionID <> @AsignacionID);";

            await using var command = new SqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("@EscalaID", modelo.EscalaID);
            command.Parameters.AddWithValue("@PersonalID", modelo.PersonalID);
            command.Parameters.AddWithValue("@FechaInicio", modelo.FechaInicio.Date);
            command.Parameters.AddWithValue("@FechaFin", modelo.FechaFin.Date);
            command.Parameters.AddWithValue(
                "@AsignacionID",
                DbValue(modelo.AsignacionID));

            return Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
        }

        private static async Task<bool> ExisteAsignacionEnFechasAsync(
            int escalaID,
            int personalID,
            DateTime fechaInicio,
            DateTime fechaFin,
            SqlConnection connection,
            SqlTransaction transaction)
        {
            const string sql = @"
SELECT COUNT(*)
FROM dbo.RRHH_EscalaAsignaciones
WHERE EscalaID = @EscalaID
  AND PersonalID = @PersonalID
  AND Activo = 1
  AND FechaInicio <= @FechaFin
  AND FechaFin >= @FechaInicio;";

            await using var command = new SqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("@EscalaID", escalaID);
            command.Parameters.AddWithValue("@PersonalID", personalID);
            command.Parameters.AddWithValue("@FechaInicio", fechaInicio.Date);
            command.Parameters.AddWithValue("@FechaFin", fechaFin.Date);

            return Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
        }

        private static async Task<bool> TieneAsignacionesAsync(
            int escalaID,
            SqlConnection connection,
            SqlTransaction transaction)
        {
            const string sql = @"
SELECT CASE WHEN EXISTS
(
    SELECT 1
    FROM dbo.RRHH_EscalaAsignaciones
    WHERE EscalaID = @EscalaID
      AND Activo = 1
)
THEN 1 ELSE 0 END;";

            await using var command = new SqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("@EscalaID", escalaID);

            return Convert.ToBoolean(await command.ExecuteScalarAsync());
        }

        // ============================================================
        // MÉTODOS PRIVADOS: MAPEO Y UTILIDADES
        // ============================================================

        private int? ObtenerUsuarioID()
        {
            var sessionID = HttpContext.Session.GetInt32("UsuarioID");
            if (sessionID.HasValue)
            {
                return sessionID.Value;
            }

            var valores = new[]
            {
                User.FindFirstValue("UsuarioID"),
                User.FindFirstValue(ClaimTypes.NameIdentifier)
            };

            foreach (var valor in valores)
            {
                if (int.TryParse(valor, out var usuarioID))
                {
                    return usuarioID;
                }
            }

            return null;
        }

        private string PrimerErrorModelo()
        {
            return ModelState.Values
                .SelectMany(x => x.Errors)
                .Select(x => x.ErrorMessage)
                .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))
                ?? "Revisa la información capturada.";
        }

        private static EscalaVM.Escala MapearEscala(SqlDataReader reader)
        {
            return new EscalaVM.Escala
            {
                EscalaID = reader.GetInt32(reader.GetOrdinal("EscalaID")),
                Folio = reader.GetString(reader.GetOrdinal("Folio")),
                CodigoDocumento =
                    reader.GetString(reader.GetOrdinal("CodigoDocumento")),
                VersionDocumento =
                    reader.GetString(reader.GetOrdinal("VersionDocumento")),
                FechaElaboracion =
                    reader.GetDateTime(reader.GetOrdinal("FechaElaboracion")),
                Anio = reader.GetInt16(reader.GetOrdinal("Anio")),
                NumeroSemana =
                    reader.GetByte(reader.GetOrdinal("NumeroSemana")),
                FechaInicio =
                    reader.GetDateTime(reader.GetOrdinal("FechaInicio")),
                FechaFin = reader.GetDateTime(reader.GetOrdinal("FechaFin")),
                PeriodoTrabajo =
                    reader.GetString(reader.GetOrdinal("PeriodoTrabajo")),
                Estado = reader.GetString(reader.GetOrdinal("Estado")),
                Observaciones =
                    GetNullableString(reader, "Observaciones"),
                ElaboradoPor =
                    reader.GetInt32(reader.GetOrdinal("ElaboradoPor")),
                FechaRegistro =
                    reader.GetDateTime(reader.GetOrdinal("FechaRegistro")),
                FechaModificacion =
                    GetNullableDateTime(reader, "FechaModificacion"),
                ActualizadoPor =
                    GetNullableInt(reader, "ActualizadoPor"),
                FechaPublicacion =
                    GetNullableDateTime(reader, "FechaPublicacion"),
                PublicadoPor =
                    GetNullableInt(reader, "PublicadoPor"),
                Activo = reader.GetBoolean(reader.GetOrdinal("Activo")),
                ElaboradoPorNombre =
                    TieneColumna(reader, "ElaboradoPorNombre")
                        ? GetNullableString(reader, "ElaboradoPorNombre")
                            ?? string.Empty
                        : string.Empty,
                PublicadoPorNombre =
                    TieneColumna(reader, "PublicadoPorNombre")
                        ? GetNullableString(reader, "PublicadoPorNombre")
                            ?? string.Empty
                        : string.Empty,
                TotalAsignaciones =
                    TieneColumna(reader, "TotalAsignaciones")
                        ? reader.GetInt32(
                            reader.GetOrdinal("TotalAsignaciones"))
                        : 0,
                TotalPersonas =
                    TieneColumna(reader, "TotalPersonas")
                        ? reader.GetInt32(reader.GetOrdinal("TotalPersonas"))
                        : 0,
                TotalNovedades =
                    TieneColumna(reader, "TotalNovedades")
                        ? reader.GetInt32(reader.GetOrdinal("TotalNovedades"))
                        : 0
            };
        }

        private static List<EscalaVM.GrupoAsignacionVM> ConstruirGrupos(
            IReadOnlyCollection<EscalaVM.Asignacion> asignaciones,
            IReadOnlyCollection<EscalaVM.EscalaTurno> turnos)
        {
            return asignaciones
                .GroupBy(x => new
                {
                    x.FuncionID,
                    x.NombreFuncion,
                    x.MaquinaID,
                    x.NombreMaquina
                })
                .Select(grupo => new EscalaVM.GrupoAsignacionVM
                {
                    DepartamentoID = null,
                    NombreDepartamento = string.Empty,
                    FuncionID = grupo.Key.FuncionID,
                    NombreFuncion = grupo.Key.NombreFuncion,
                    MaquinaID = grupo.Key.MaquinaID,
                    NombreMaquina = grupo.Key.NombreMaquina,
                    Celdas = turnos
                        .OrderBy(x => x.Orden)
                        .Select(turno => new EscalaVM.CeldaTurnoVM
                        {
                            EscalaTurnoID = turno.EscalaTurnoID,
                            NombreTurno = turno.Nombre,
                            Color = turno.Color,
                            Asignaciones = grupo
                                .Where(x =>
                                    x.EscalaTurnoID
                                    == turno.EscalaTurnoID)
                                .OrderBy(x => x.NombrePersona)
                                .ToList()
                        })
                        .ToList()
                })
                .OrderBy(x => x.NombreFuncion)
                .ThenBy(x => x.NombreMaquina)
                .ToList();
        }

        private static string GenerarFolio(int anio, int semana)
        {
            return $"EP-{anio}-S{semana:00}";
        }

        private static DateTime ObtenerLunesSemanaISO(int anio, int semana)
        {
            return ISOWeek.ToDateTime(anio, semana, DayOfWeek.Monday);
        }

        private static void AgregarParametrosAsignacion(
            SqlCommand command,
            EscalaVM.GuardarAsignacionVM modelo,
            int usuarioID)
        {
            command.Parameters.AddWithValue("@EscalaID", modelo.EscalaID);
            command.Parameters.AddWithValue("@PersonalID", modelo.PersonalID);
            command.Parameters.AddWithValue("@FuncionID", modelo.FuncionID);
            command.Parameters.AddWithValue(
                "@MaquinaID",
                DbValue(modelo.MaquinaID));
            command.Parameters.AddWithValue(
                "@EscalaTurnoID",
                modelo.EscalaTurnoID);
            command.Parameters.AddWithValue(
                "@FechaInicio",
                modelo.FechaInicio.Date);
            command.Parameters.AddWithValue("@FechaFin", modelo.FechaFin.Date);
            command.Parameters.AddWithValue(
                "@Observaciones",
                DbValue(modelo.Observaciones));
            command.Parameters.AddWithValue("@UsuarioID", usuarioID);
        }

        private static void AgregarParametrosNovedad(
            SqlCommand command,
            EscalaVM.GuardarNovedadVM modelo,
            int usuarioID)
        {
            command.Parameters.AddWithValue("@EscalaID", modelo.EscalaID);
            command.Parameters.AddWithValue("@PersonalID", modelo.PersonalID);
            command.Parameters.AddWithValue(
                "@TipoNovedad",
                modelo.TipoNovedad);
            command.Parameters.AddWithValue(
                "@FechaInicio",
                modelo.FechaInicio.Date);
            command.Parameters.AddWithValue(
                "@FechaFin",
                modelo.TipoNovedad == EscalaVM.TiposNovedad.Baja
                    ? DBNull.Value
                    : DbValue(modelo.FechaFin?.Date));
            command.Parameters.AddWithValue(
                "@Motivo",
                DbValue(modelo.Motivo));
            command.Parameters.AddWithValue(
                "@Observaciones",
                DbValue(modelo.Observaciones));
            command.Parameters.AddWithValue("@UsuarioID", usuarioID);
        }

        private static void AgregarParametrosTurno(
            SqlCommand command,
            EscalaVM.Turno turno,
            int usuarioID)
        {
            command.Parameters.AddWithValue("@Nombre", turno.Nombre.Trim());
            command.Parameters.AddWithValue(
                "@HoraInicio",
                DbValue(turno.HoraInicio));
            command.Parameters.AddWithValue(
                "@HoraFin",
                DbValue(turno.HoraFin));
            command.Parameters.AddWithValue(
                "@CruzaDiaSiguiente",
                turno.CruzaDiaSiguiente);
            command.Parameters.AddWithValue("@EsFlexible", turno.EsFlexible);
            command.Parameters.AddWithValue("@TipoTurno", turno.TipoTurno);
            command.Parameters.AddWithValue("@Color", turno.Color);
            command.Parameters.AddWithValue("@Orden", turno.Orden);
            command.Parameters.AddWithValue("@UsuarioID", usuarioID);
        }

        private static void AgregarParametrosFuncion(
            SqlCommand command,
            EscalaVM.Funcion funcion,
            int usuarioID)
        {
            command.Parameters.AddWithValue("@Nombre", funcion.Nombre.Trim());
            command.Parameters.AddWithValue(
                "@Descripcion",
                DbValue(funcion.Descripcion));
            command.Parameters.AddWithValue(
                "@DepartamentoID",
                DbValue(funcion.DepartamentoID));
            command.Parameters.AddWithValue(
                "@RequiereMaquina",
                funcion.RequiereMaquina);
            command.Parameters.AddWithValue("@Orden", funcion.Orden);
            command.Parameters.AddWithValue("@UsuarioID", usuarioID);
        }

        private static List<SelectListItem> CrearTiposTurno()
        {
            return new List<SelectListItem>
            {
                new()
                {
                    Value = EscalaVM.TiposTurno.Regular,
                    Text = "Regular"
                },
                new()
                {
                    Value = EscalaVM.TiposTurno.Mixto,
                    Text = "Mixto"
                },
                new()
                {
                    Value = EscalaVM.TiposTurno.DocePorDoce,
                    Text = "12 x 12"
                },
                new()
                {
                    Value = EscalaVM.TiposTurno.Especial,
                    Text = "Especial"
                }
            };
        }

        private async Task<List<SelectListItem>>
            CrearSelectDepartamentosAsync()
        {
            return (await ObtenerDepartamentosAsync())
                .Select(x => new SelectListItem
                {
                    Value = x.DepartamentoID.ToString(),
                    Text = x.NombreDepartamento
                })
                .Prepend(new SelectListItem
                {
                    Value = string.Empty,
                    Text = "Todos los departamentos"
                })
                .ToList();
        }

        private static object DbValue(object? value)
        {
            if (value is string text && string.IsNullOrWhiteSpace(text))
            {
                return DBNull.Value;
            }

            return value ?? DBNull.Value;
        }

        private static bool TieneColumna(IDataRecord reader, string nombre)
        {
            for (var i = 0; i < reader.FieldCount; i++)
            {
                if (string.Equals(
                        reader.GetName(i),
                        nombre,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string? GetNullableString(
            SqlDataReader reader,
            string columna)
        {
            var ordinal = reader.GetOrdinal(columna);
            return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
        }

        private static bool GetBooleanValue(
            SqlDataReader reader,
            string columna)
        {
            var ordinal = reader.GetOrdinal(columna);
            if (reader.IsDBNull(ordinal))
            {
                return false;
            }

            var value = reader.GetValue(ordinal);
            return value switch
            {
                bool booleanValue => booleanValue,
                byte byteValue => byteValue != 0,
                short shortValue => shortValue != 0,
                int intValue => intValue != 0,
                long longValue => longValue != 0,
                decimal decimalValue => decimalValue != 0,
                string stringValue when int.TryParse(
                    stringValue,
                    out var numericValue) => numericValue != 0,
                string stringValue => bool.TryParse(
                    stringValue,
                    out var parsedValue) && parsedValue,
                _ => Convert.ToBoolean(value, CultureInfo.InvariantCulture)
            };
        }

        private static int? GetNullableInt(
            SqlDataReader reader,
            string columna)
        {
            var ordinal = reader.GetOrdinal(columna);
            return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
        }

        private static DateTime? GetNullableDateTime(
            SqlDataReader reader,
            string columna)
        {
            var ordinal = reader.GetOrdinal(columna);
            return reader.IsDBNull(ordinal)
                ? null
                : reader.GetDateTime(ordinal);
        }

        private static TimeSpan? GetNullableTimeSpan(
            SqlDataReader reader,
            string columna)
        {
            var ordinal = reader.GetOrdinal(columna);
            return reader.IsDBNull(ordinal)
                ? null
                : reader.GetTimeSpan(ordinal);
        }
    }
}
