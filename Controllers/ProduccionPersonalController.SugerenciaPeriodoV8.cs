using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;
using ERP.NSQuell.Models;

namespace ERP.NSQuell.Controllers;

public sealed partial class ProduccionPersonalController
{
    // NSQ_PRODUCCION_PERSONAL_V8_PERIODO
    // Regla IMPORTANTE:
    // - Solo la sugerencia automatica evita dos turnos distintos por persona/dia.
    // - GuardarTurnoV7 manual NO se modifica y conserva sus advertencias actuales.

    [HttpPost("SugerirPeriodoV8")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SugerirPeriodoV8(
        string? vista,
        DateTime fechaDesde,
        DateTime? fechaHasta,
        string? panel)
    {
        if (!UsuarioEnSesion())
            return RedirectToAction("Login", "Login");

        var periodo = ResolverPeriodoV7(vista, fechaDesde, fechaHasta);
        var ahora = DateTime.Now;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync();

        await using var tx =
            (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);

        try
        {
            if (!await ConfiguradoV7Async(cn, tx))
                throw new InvalidOperationException(
                    "Falta completar la estructura V7 en esta base.");

            var programas =
                await CargarProgramasPeriodoV7Async(
                    periodo.Inicio,
                    periodo.Fin,
                    cn,
                    tx);

            var asignaciones =
                await CargarAsignacionesPeriodoV7Async(
                    periodo.Inicio.AddDays(-1),
                    periodo.Fin.AddDays(1),
                    cn,
                    tx);

            var turnos =
                (await CargarTurnosV2Async(cn, tx))
                .Where(EsTurnoOperadorV7)
                .OrderBy(x => x.Orden)
                .ThenBy(x => x.Inicio)
                .ToList();

            var oficiales = await CargarOperadoresOficialesV2Async(cn, tx);
            var niveles = await CargarNivelesV2Async(cn, tx);
            var uid = UsuarioID();

            var segmentos =
                new List<(ProgramaBaseV7 Programa, ProduccionPersonalV7SegmentoVm Segmento)>();

            foreach (var programa in programas)
            {
                foreach (var seg in ConstruirSegmentosV7(
                    programa,
                    turnos,
                    periodo.Inicio,
                    periodo.Fin))
                {
                    var guardada =
                        asignaciones
                        .Where(x =>
                            x.ProgramaID == programa.ProgramaID &&
                            x.TurnoID == seg.TurnoID &&
                            x.FechaTrabajo.Date == seg.FechaTrabajo.Date)
                        .OrderByDescending(x => x.AsignacionID)
                        .FirstOrDefault();

                    if (guardada != null)
                    {
                        seg.AsignacionPersonalID = guardada.AsignacionID;
                        seg.TieneAsignacionEspecifica = true;
                        seg.OperadorAsignadoID = guardada.OperadorID;
                        seg.OperadorAsignadoNombre = guardada.OperadorNombre;
                    }

                    segmentos.Add((programa, seg));
                }
            }

            // Tramos ya iniciados/terminados y programas actualmente en produccion
            // se conservan. Sirven tambien como ocupacion real para la sugerencia.
            var preservados =
                segmentos
                .Where(x =>
                    x.Segmento.ProduccionActiva ||
                    x.Segmento.Inicio <= ahora)
                .Select(x => x.Segmento)
                .ToList();

            var agenda = new List<ProduccionPersonalV7SegmentoVm>(preservados);

            // (dia, persona) -> turnos usados.
            // Esta estructura SOLO vive dentro del motor automatico.
            var turnosPorDiaPersona =
                new Dictionary<(DateTime Dia, int PersonaID), HashSet<int>>();

            void RegistrarTurno(DateTime dia, int personaId, int turnoId)
            {
                var key = (dia.Date, personaId);
                if (!turnosPorDiaPersona.TryGetValue(key, out var set))
                {
                    set = new HashSet<int>();
                    turnosPorDiaPersona[key] = set;
                }

                set.Add(turnoId);
            }

            foreach (var seg in preservados.Where(x => x.OperadorEfectivoID.HasValue))
            {
                RegistrarTurno(
                    seg.FechaTrabajo.Date,
                    seg.OperadorEfectivoID!.Value,
                    seg.TurnoID);
            }

            var escalaOpsCache =
                new Dictionary<DateTime, List<EscalaOperadorV2>>();

            var semanasConEscala =
                new Dictionary<DateTime, bool>();

            async Task<List<EscalaOperadorV2>> EscalaSemanaAsync(DateTime momento)
            {
                var semana = InicioSemanaV2(momento);

                if (escalaOpsCache.TryGetValue(semana, out var cached))
                    return cached;

                var escala = await CargarEscalaSemanaV2Async(semana, cn, tx);
                semanasConEscala[semana] = escala != null;

                var lista = escala == null
                    ? new List<EscalaOperadorV2>()
                    : await CargarEscalaOperadoresV2Async(
                        escala.EscalaID,
                        cn,
                        tx);

                escalaOpsCache[semana] = lista;
                return lista;
            }

            var modificados = 0;
            var sinCambio = 0;
            var sinCandidato = 0;
            var omitidosIniciados = segmentos.Count - segmentos.Count(x =>
                !x.Segmento.ProduccionActiva &&
                x.Segmento.Inicio > ahora);

            var objetivos =
                segmentos
                .Where(x =>
                    !x.Segmento.ProduccionActiva &&
                    x.Segmento.Inicio > ahora)
                .OrderBy(x => x.Segmento.Inicio)
                .ThenBy(x => x.Segmento.TurnoID)
                .ThenBy(x => x.Segmento.MaquinaCodigo)
                .ThenBy(x => x.Segmento.OF)
                .ToList();

            foreach (var item in objetivos)
            {
                var programa = item.Programa;
                var seg = item.Segmento;
                var anterior = seg.OperadorEfectivoID;

                var semana = InicioSemanaV2(seg.Inicio);
                var escalaOps = await EscalaSemanaAsync(seg.Inicio);
                var existeEscala = semanasConEscala.TryGetValue(semana, out var tieneEscala) && tieneEscala;

                var tieneMatriz =
                    programa.ParteID.HasValue &&
                    niveles.Keys.Any(x => x.ParteID == programa.ParteID.Value);

                var candidatos =
                    new List<(
                        OperadorOficialV2 Op,
                        int? Nivel,
                        bool EnEscala,
                        bool MismaMaquina,
                        decimal HorasSemana)>();

                foreach (var op in oficiales)
                {
                    // REGLA V8: automatizacion = un solo turno por persona y por dia.
                    // La edicion manual NO usa esta validacion.
                    if (turnosPorDiaPersona.TryGetValue(
                            (seg.FechaTrabajo.Date, op.PersonaID),
                            out var turnosPersona) &&
                        turnosPersona.Any(x => x != seg.TurnoID))
                    {
                        continue;
                    }

                    int? nivel = null;
                    if (programa.ParteID.HasValue &&
                        niveles.TryGetValue(
                            (programa.ParteID.Value, op.PersonaID),
                            out var n))
                    {
                        nivel = n;
                    }

                    if (tieneMatriz && !nivel.HasValue)
                        continue;

                    // La sugerencia automatica nunca crea traslapes.
                    var conflicto =
                        agenda.Any(x =>
                            x.OperadorEfectivoID == op.PersonaID &&
                            x.ProgramaProduccionID != seg.ProgramaProduccionID &&
                            x.Inicio < seg.Fin &&
                            x.Fin > seg.Inicio);

                    if (conflicto)
                        continue;

                    var enEscala =
                        !existeEscala ||
                        escalaOps.Any(x =>
                            x.PersonaID == op.PersonaID &&
                            EscalaCubreMomentoV2(x, seg.Inicio));

                    var mismaMaquina =
                        escalaOps.Any(x =>
                            x.PersonaID == op.PersonaID &&
                            x.MaquinaID == programa.MaquinaID &&
                            EscalaCubreMomentoV2(x, seg.Inicio));

                    var horasSemana =
                        Convert.ToDecimal(
                            agenda
                            .Where(x =>
                                x.OperadorEfectivoID == op.PersonaID &&
                                x.Inicio >= semana &&
                                x.Inicio < semana.AddDays(7))
                            .Sum(x => Math.Max(
                                0,
                                (x.Fin - x.Inicio).TotalHours)));

                    candidatos.Add((
                        op,
                        nivel,
                        enEscala,
                        mismaMaquina,
                        Math.Round(horasSemana, 2)));
                }

                var elegido =
                    candidatos
                    .OrderByDescending(x => x.EnEscala)
                    .ThenByDescending(x => x.Nivel ?? 0)
                    .ThenByDescending(x => x.MismaMaquina)
                    .ThenBy(x => x.HorasSemana)
                    .ThenBy(x => x.Op.Nombre)
                    .FirstOrDefault();

                int? nuevo =
                    elegido.Op == null
                        ? null
                        : elegido.Op.PersonaID;

                var asignacionId =
                    await UpsertSugerenciaPeriodoV8Async(
                        programa,
                        seg,
                        nuevo,
                        uid,
                        cn,
                        tx);

                if (anterior != nuevo)
                {
                    await RegistrarHistorialSugerenciaPeriodoV8Async(
                        asignacionId,
                        programa,
                        seg,
                        anterior,
                        nuevo,
                        periodo.Vista,
                        uid,
                        cn,
                        tx);

                    modificados++;
                }
                else
                {
                    sinCambio++;
                }

                if (!nuevo.HasValue)
                {
                    sinCandidato++;
                    continue;
                }

                seg.TieneAsignacionEspecifica = true;
                seg.AsignacionPersonalID = asignacionId;
                seg.OperadorAsignadoID = nuevo;
                seg.OperadorAsignadoNombre = elegido.Op.Nombre;

                agenda.Add(seg);
                RegistrarTurno(seg.FechaTrabajo.Date, nuevo.Value, seg.TurnoID);
            }

            await tx.CommitAsync();

            var nombrePeriodo =
                periodo.Vista == "dia" ? "día" :
                periodo.Vista == "semana" ? "semana" :
                periodo.Vista == "mes" ? "mes" : "rango";

            TempData["Success"] =
                $"Sugerencia automática del {nombrePeriodo}: " +
                $"{modificados} horario(s) redistribuidos, " +
                $"{sinCambio} sin cambio, " +
                $"{sinCandidato} sin candidato seguro y " +
                $"{omitidosIniciados} horario(s) ya iniciados/en producción conservados. " +
                "La automatización no asigna una misma persona a dos turnos distintos del mismo día.";
        }
        catch (Exception ex)
        {
            try { await tx.RollbackAsync(); } catch { }

            TempData["Error"] =
                "No fue posible sugerir operadores para el periodo: " +
                ex.Message;
        }

        return RedirectToAction(
            nameof(Index),
            new
            {
                vista = periodo.Vista,
                fechaDesde = periodo.Inicio.ToString("yyyy-MM-dd"),
                fechaHasta =
                    periodo.Vista == "rango"
                        ? periodo.Fin.AddDays(-1).ToString("yyyy-MM-dd")
                        : null,
                panel = string.IsNullOrWhiteSpace(panel)
                    ? "planner"
                    : panel
            });
    }

    private static async Task<int> UpsertSugerenciaPeriodoV8Async(
        ProgramaBaseV7 programa,
        ProduccionPersonalV7SegmentoVm seg,
        int? operadorId,
        int usuarioId,
        SqlConnection cn,
        SqlTransaction tx)
    {
        const string sel = @"
SELECT TOP(1) AsignacionPersonalID
FROM dbo.Produccion_ProgramaPersonalAsignaciones WITH(UPDLOCK,HOLDLOCK)
WHERE ProgramaProduccionID=@ProgramaID
  AND TurnoID=@TurnoID
  AND FechaTrabajo=@Fecha
  AND Activo=1
ORDER BY AsignacionPersonalID DESC;";

        int? asignacionId = null;

        await using (var cmd = new SqlCommand(sel, cn, tx))
        {
            cmd.Parameters.Add("@ProgramaID", SqlDbType.Int).Value =
                programa.ProgramaID;
            cmd.Parameters.Add("@TurnoID", SqlDbType.Int).Value =
                seg.TurnoID;
            cmd.Parameters.Add("@Fecha", SqlDbType.Date).Value =
                seg.FechaTrabajo.Date;

            var raw = await cmd.ExecuteScalarAsync();
            if (raw != null && raw != DBNull.Value)
                asignacionId = Convert.ToInt32(raw);
        }

        if (asignacionId.HasValue)
        {
            const string up = @"
UPDATE dbo.Produccion_ProgramaPersonalAsignaciones
SET FechaTrabajo=@Fecha,
    TurnoNombre=@Turno,
    Inicio=@Inicio,
    Fin=@Fin,
    OperadorID=@Operador,
    Observaciones=CASE
        WHEN @Operador IS NULL THEN N'SIN_OPERADOR_SUGERENCIA_PERIODO'
        ELSE N'SUGERENCIA AUTOMATICA PERIODO'
    END,
    UsuarioModificacionID=@Usuario,
    FechaModificacion=SYSDATETIME(),
    Activo=1
WHERE AsignacionPersonalID=@ID;";

            await using var cmd = new SqlCommand(up, cn, tx);
            cmd.Parameters.Add("@Fecha", SqlDbType.Date).Value =
                seg.FechaTrabajo.Date;
            cmd.Parameters.Add("@Turno", SqlDbType.NVarChar, 100).Value =
                seg.TurnoNombre;
            cmd.Parameters.Add("@Inicio", SqlDbType.DateTime2).Value =
                seg.Inicio;
            cmd.Parameters.Add("@Fin", SqlDbType.DateTime2).Value =
                seg.Fin;
            cmd.Parameters.Add("@Operador", SqlDbType.Int).Value =
                (object?)operadorId ?? DBNull.Value;
            cmd.Parameters.Add("@Usuario", SqlDbType.Int).Value =
                usuarioId;
            cmd.Parameters.Add("@ID", SqlDbType.Int).Value =
                asignacionId.Value;

            await cmd.ExecuteNonQueryAsync();
            return asignacionId.Value;
        }

        const string ins = @"
INSERT dbo.Produccion_ProgramaPersonalAsignaciones
(
    ProgramaProduccionID,
    FechaTrabajo,
    TurnoID,
    TurnoNombre,
    Inicio,
    Fin,
    OperadorID,
    AuxiliarID,
    TecnicoProduccionID,
    Observaciones,
    UsuarioCreacionID,
    FechaCreacion,
    Activo
)
OUTPUT INSERTED.AsignacionPersonalID
VALUES
(
    @ProgramaID,
    @Fecha,
    @TurnoID,
    @Turno,
    @Inicio,
    @Fin,
    @Operador,
    NULL,
    NULL,
    CASE
        WHEN @Operador IS NULL THEN N'SIN_OPERADOR_SUGERENCIA_PERIODO'
        ELSE N'SUGERENCIA AUTOMATICA PERIODO'
    END,
    @Usuario,
    SYSDATETIME(),
    1
);";

        await using (var cmd = new SqlCommand(ins, cn, tx))
        {
            cmd.Parameters.Add("@ProgramaID", SqlDbType.Int).Value =
                programa.ProgramaID;
            cmd.Parameters.Add("@Fecha", SqlDbType.Date).Value =
                seg.FechaTrabajo.Date;
            cmd.Parameters.Add("@TurnoID", SqlDbType.Int).Value =
                seg.TurnoID;
            cmd.Parameters.Add("@Turno", SqlDbType.NVarChar, 100).Value =
                seg.TurnoNombre;
            cmd.Parameters.Add("@Inicio", SqlDbType.DateTime2).Value =
                seg.Inicio;
            cmd.Parameters.Add("@Fin", SqlDbType.DateTime2).Value =
                seg.Fin;
            cmd.Parameters.Add("@Operador", SqlDbType.Int).Value =
                (object?)operadorId ?? DBNull.Value;
            cmd.Parameters.Add("@Usuario", SqlDbType.Int).Value =
                usuarioId;

            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }
    }

    private static async Task RegistrarHistorialSugerenciaPeriodoV8Async(
        int asignacionId,
        ProgramaBaseV7 programa,
        ProduccionPersonalV7SegmentoVm seg,
        int? anterior,
        int? nuevo,
        string vista,
        int usuarioId,
        SqlConnection cn,
        SqlTransaction tx)
    {
        const string sql = @"
INSERT dbo.Produccion_PersonalAsignacionHistorial
(
    AsignacionPersonalID,
    ProgramaProduccionID,
    FechaTrabajo,
    TurnoID,
    TurnoNombre,
    Inicio,
    Fin,
    Rol,
    PersonaAnteriorID,
    PersonaNuevaID,
    Motivo,
    Justificacion,
    Origen,
    ProduccionActiva,
    UsuarioID,
    FechaMovimiento
)
VALUES
(
    @Asignacion,
    @Programa,
    @Fecha,
    @TurnoID,
    @Turno,
    @Inicio,
    @Fin,
    N'OPERADOR',
    @Anterior,
    @Nuevo,
    N'SUGERENCIA_AUTOMATICA_PERIODO',
    @Justificacion,
    N'PRODUCCION_PERSONAL_V8_SUGERIR_PERIODO',
    0,
    @Usuario,
    SYSDATETIME()
);";

        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@Asignacion", SqlDbType.Int).Value =
            asignacionId;
        cmd.Parameters.Add("@Programa", SqlDbType.Int).Value =
            programa.ProgramaID;
        cmd.Parameters.Add("@Fecha", SqlDbType.Date).Value =
            seg.FechaTrabajo.Date;
        cmd.Parameters.Add("@TurnoID", SqlDbType.Int).Value =
            seg.TurnoID;
        cmd.Parameters.Add("@Turno", SqlDbType.NVarChar, 100).Value =
            seg.TurnoNombre;
        cmd.Parameters.Add("@Inicio", SqlDbType.DateTime2).Value =
            seg.Inicio;
        cmd.Parameters.Add("@Fin", SqlDbType.DateTime2).Value =
            seg.Fin;
        cmd.Parameters.Add("@Anterior", SqlDbType.Int).Value =
            (object?)anterior ?? DBNull.Value;
        cmd.Parameters.Add("@Nuevo", SqlDbType.Int).Value =
            (object?)nuevo ?? DBNull.Value;
        cmd.Parameters.Add("@Justificacion", SqlDbType.NVarChar, 500).Value =
            $"Redistribución automática de vista {vista}. " +
            "Regla automática: una persona no se asigna a dos turnos distintos del mismo día; " +
            "la edición manual conserva la posibilidad con advertencia.";
        cmd.Parameters.Add("@Usuario", SqlDbType.Int).Value =
            usuarioId;

        await cmd.ExecuteNonQueryAsync();
    }
}