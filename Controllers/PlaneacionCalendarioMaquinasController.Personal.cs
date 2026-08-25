using ERP.NSQuell.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;

namespace ERP.NSQuell.Controllers;

public sealed partial class PlaneacionCalendarioMaquinasController
{
    private sealed class TurnoCambioOperadorInterno
    {
        public int TurnoID { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public TimeSpan Inicio { get; set; }
        public TimeSpan Fin { get; set; }
        public bool Cruza { get; set; }
        public int Orden { get; set; }
    }

    private sealed class ProgramaCambioOperadorInterno
    {
        public int ProgramaID { get; set; }
        public int MaquinaID { get; set; }
        public int? ParteID { get; set; }
        public DateTime Inicio { get; set; }
        public DateTime Fin { get; set; }
        public int? OperadorPlaneadoID { get; set; }
        public string OperadorPlaneadoNombre { get; set; } = string.Empty;
        public int? EjecucionID { get; set; }
        public int? OperadorRealID { get; set; }
        public string OperadorRealNombre { get; set; } = string.Empty;
    }

    [HttpGet("PersonalPrograma")]
    public async Task<IActionResult> PersonalPrograma(int programaProduccionId)
    {
        if (!UsuarioEnSesion())
            return Unauthorized(new { ok = false, mensaje = "La sesión terminó." });

        if (programaProduccionId <= 0)
            return Json(new { ok = false, mensaje = "Programa no válido." });

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync();

        if (!await TablaExcepcionesOperadorDisponibleAsync(cn, null))
        {
            return Json(new
            {
                ok = false,
                mensaje = "Falta instalar Produccion_OperadorExcepciones."
            });
        }

        var programa = await ObtenerProgramaCambioOperadorAsync(
            programaProduccionId, cn, null, false);

        if (programa == null)
            return Json(new { ok = false, mensaje = "No se encontró el programa." });

        var candidatos = await CargarCandidatosCambioOperadorAsync(
            programa.ParteID, cn, null);

        var turno = await ResolverTurnoCambioOperadorAsync(
            programa.Inicio, cn, null);

        return Json(new
        {
            ok = true,
            programaProduccionID = programa.ProgramaID,
            maquinaID = programa.MaquinaID,
            inicio = programa.Inicio,
            fin = programa.Fin,
            operadorPlaneadoID = programa.OperadorPlaneadoID,
            operadorPlaneadoNombre = programa.OperadorPlaneadoNombre,
            operadorRealID = programa.OperadorRealID,
            operadorRealNombre = programa.OperadorRealNombre,
            turnoID = turno?.TurnoID,
            turno = turno?.Nombre ?? "Sin turno",
            candidatos
        });
    }

    [HttpPost("CambiarOperador")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CambiarOperador(
        [FromBody] ProduccionPersonalCambioOperadorRequest request)
    {
        if (!UsuarioEnSesion())
            return Unauthorized(new { ok = false, mensaje = "La sesión terminó." });

        if (request == null ||
            request.ProgramaProduccionID <= 0 ||
            request.OperadorSustitutoID <= 0)
        {
            return Json(new { ok = false, mensaje = "Datos incompletos." });
        }

        request.Alcance = (request.Alcance ?? string.Empty).Trim().ToUpperInvariant();
        request.Motivo = (request.Motivo ?? string.Empty).Trim();
        request.Justificacion = (request.Justificacion ?? string.Empty).Trim();

        if (request.Alcance is not ("SOLO_OF" or "RESTO_TURNO"))
            return Json(new { ok = false, mensaje = "Alcance no válido." });

        if (string.IsNullOrWhiteSpace(request.Motivo))
            return Json(new { ok = false, mensaje = "Selecciona un motivo." });

        if (request.Justificacion.Length < 5)
        {
            return Json(new
            {
                ok = false,
                mensaje = "La justificación es obligatoria y debe explicar el cambio."
            });
        }

        if (request.Justificacion.Length > 500)
            return Json(new { ok = false, mensaje = "Máximo 500 caracteres." });

        var usuarioId = ObtenerUsuarioID();
        if (usuarioId <= 0)
            return Json(new { ok = false, mensaje = "No se identificó al usuario." });

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync();

        if (!await TablaExcepcionesOperadorDisponibleAsync(cn, null))
        {
            return Json(new
            {
                ok = false,
                mensaje = "Falta ejecutar el SQL de Producción Personal Semanal."
            });
        }

        await using var tx =
            (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);

        try
        {
            var programa = await ObtenerProgramaCambioOperadorAsync(
                request.ProgramaProduccionID, cn, tx, true);

            if (programa == null)
                throw new InvalidOperationException("El programa ya no está disponible.");

            var turno = await ResolverTurnoCambioOperadorAsync(
                programa.Inicio, cn, tx);

            if (turno == null)
                throw new InvalidOperationException("No se pudo resolver el turno.");

            if (!await ValidarCandidatoCambioOperadorAsync(
                    request.OperadorSustitutoID,
                    programa.ParteID,
                    cn,
                    tx))
            {
                throw new InvalidOperationException(
                    "El operador sustituto no está activo o no cumple la polivalencia N1-N4.");
            }

            var fechaTrabajo = FechaTrabajoCambioOperador(programa.Inicio, turno);
            var semana = InicioSemanaCambioOperador(fechaTrabajo);
            var ventana = VentanaTurnoCambioOperador(fechaTrabajo, turno);

            var inicioVigencia =
                request.Alcance == "RESTO_TURNO" &&
                DateTime.Now > ventana.Inicio &&
                DateTime.Now < ventana.Fin
                    ? DateTime.Now
                    : programa.Inicio;

            var finVigencia =
                request.Alcance == "RESTO_TURNO"
                    ? ventana.Fin
                    : programa.Fin;

            if (finVigencia <= inicioVigencia)
                finVigencia = inicioVigencia.AddMinutes(1);

            await CancelarExcepcionProgramaCambioOperadorAsync(
                programa.ProgramaID, usuarioId, cn, tx);

            await InsertarExcepcionCambioOperadorAsync(
                programa,
                turno,
                semana,
                fechaTrabajo,
                request,
                inicioVigencia,
                finVigencia,
                usuarioId,
                cn,
                tx);

            var objetivos = new List<ProgramaCambioOperadorInterno> { programa };

            if (request.Alcance == "RESTO_TURNO")
            {
                objetivos.AddRange(
                    await CargarProgramasRestoTurnoCambioOperadorAsync(
                        programa.MaquinaID,
                        programa.ProgramaID,
                        inicioVigencia,
                        finVigencia,
                        cn,
                        tx));
            }

            var aplicados = 0;
            var omitidos = 0;

            foreach (var objetivo in objetivos
                .GroupBy(x => x.ProgramaID)
                .Select(x => x.First()))
            {
                if (!await ValidarCandidatoCambioOperadorAsync(
                        request.OperadorSustitutoID,
                        objetivo.ParteID,
                        cn,
                        tx))
                {
                    omitidos++;
                    continue;
                }

                await ReemplazarPrincipalCambioOperadorAsync(
                    objetivo.ProgramaID,
                    request.OperadorSustitutoID,
                    usuarioId,
                    cn,
                    tx);

                if (objetivo.EjecucionID.HasValue)
                {
                    await ActualizarEjecucionCambioOperadorAsync(
                        objetivo.EjecucionID.Value,
                        request.OperadorSustitutoID,
                        request.Motivo,
                        request.Justificacion,
                        usuarioId,
                        cn,
                        tx);
                }

                aplicados++;
            }

            await tx.CommitAsync();

            return Json(new
            {
                ok = true,
                mensaje =
                    request.Alcance == "RESTO_TURNO"
                        ? $"Cambio aplicado a {aplicados} programa(s) del turno." +
                          (omitidos > 0
                              ? $" {omitidos} se omitieron por polivalencia."
                              : "")
                        : "Operador cambiado para esta OF con trazabilidad.",
                aplicados,
                omitidosPolivalencia = omitidos
            });
        }
        catch (Exception ex)
        {
            try { await tx.RollbackAsync(); } catch { }

            return Json(new
            {
                ok = false,
                mensaje = "No fue posible cambiar el operador: " + ex.Message
            });
        }
    }

    private static DateTime InicioSemanaCambioOperador(DateTime fecha)
    {
        var d = fecha.Date;
        var delta = ((int)d.DayOfWeek + 6) % 7;
        return d.AddDays(-delta);
    }

    private static DateTime FechaTrabajoCambioOperador(
        DateTime momento,
        TurnoCambioOperadorInterno turno)
    {
        return turno.Cruza && momento.TimeOfDay < turno.Fin
            ? momento.Date.AddDays(-1)
            : momento.Date;
    }

    private static (DateTime Inicio, DateTime Fin) VentanaTurnoCambioOperador(
        DateTime fechaTrabajo,
        TurnoCambioOperadorInterno turno)
    {
        var inicio = fechaTrabajo.Date.Add(turno.Inicio);
        var fin = fechaTrabajo.Date.Add(turno.Fin);

        if (turno.Cruza || fin <= inicio)
            fin = fin.AddDays(1);

        return (inicio, fin);
    }

    private static async Task<bool> TablaExcepcionesOperadorDisponibleAsync(
        SqlConnection cn,
        SqlTransaction? tx)
    {
        const string sql = @"
SELECT CONVERT(bit,CASE WHEN
    OBJECT_ID(N'dbo.Produccion_OperadorExcepciones',N'U') IS NOT NULL
THEN 1 ELSE 0 END);";

        await using var cmd =
            tx == null ? new SqlCommand(sql, cn) : new SqlCommand(sql, cn, tx);

        return Convert.ToBoolean(await cmd.ExecuteScalarAsync() ?? false);
    }

    private static async Task<TurnoCambioOperadorInterno?>
        ResolverTurnoCambioOperadorAsync(
            DateTime momento,
            SqlConnection cn,
            SqlTransaction? tx)
    {
        const string sql = @"
SELECT TurnoID,ISNULL(Nombre,N'') AS Nombre,HoraInicio,HoraFin,
       ISNULL(CruzaDiaSiguiente,0) AS CruzaDiaSiguiente,
       ISNULL(Orden,999) AS Orden
FROM dbo.RRHH_Turnos
WHERE Activo=1
  AND ISNULL(EsFlexible,0)=0
  AND HoraInicio IS NOT NULL
  AND HoraFin IS NOT NULL
ORDER BY ISNULL(Orden,999),HoraInicio,TurnoID;";

        var todos = new List<TurnoCambioOperadorInterno>();

        await using var cmd =
            tx == null ? new SqlCommand(sql, cn) : new SqlCommand(sql, cn, tx);

        await using var rd = await cmd.ExecuteReaderAsync();

        while (await rd.ReadAsync())
        {
            todos.Add(new TurnoCambioOperadorInterno
            {
                TurnoID = Convert.ToInt32(rd["TurnoID"]),
                Nombre = rd["Nombre"]?.ToString()?.Trim() ?? "Turno",
                Inicio = (TimeSpan)rd["HoraInicio"],
                Fin = (TimeSpan)rd["HoraFin"],
                Cruza = Convert.ToBoolean(rd["CruzaDiaSiguiente"]),
                Orden = Convert.ToInt32(rd["Orden"])
            });
        }

        var principales = new List<TurnoCambioOperadorInterno>();

        foreach (var hora in new[]
        {
            new TimeSpan(7,0,0),
            new TimeSpan(15,0,0),
            new TimeSpan(22,30,0)
        })
        {
            var t = todos.FirstOrDefault(x => x.Inicio == hora);
            if (t != null && principales.All(x => x.TurnoID != t.TurnoID))
                principales.Add(t);
        }

        foreach (var t in todos)
        {
            if (principales.Count >= 3) break;
            if (t.Nombre.Contains("MIXT", StringComparison.OrdinalIgnoreCase))
                continue;
            if (principales.All(x => x.TurnoID != t.TurnoID))
                principales.Add(t);
        }

        var horaActual = momento.TimeOfDay;

        return principales
            .Take(3)
            .OrderBy(x => x.Orden)
            .FirstOrDefault(t =>
                t.Cruza
                    ? horaActual >= t.Inicio || horaActual < t.Fin
                    : horaActual >= t.Inicio && horaActual < t.Fin);
    }

    private static async Task<ProgramaCambioOperadorInterno?>
        ObtenerProgramaCambioOperadorAsync(
            int programaId,
            SqlConnection cn,
            SqlTransaction? tx,
            bool bloquear)
    {
        var hint = bloquear ? " WITH (UPDLOCK,HOLDLOCK)" : string.Empty;

        var sql = $@"
SELECT TOP (1)
    pp.ProgramaProduccionID,
    pp.MaquinaID,
    pp.ParteID,
    pp.FechaInicioProgramada,
    ISNULL(
        pp.FechaFinProgramada,
        DATEADD(
            MINUTE,
            CONVERT(INT,CEILING(ISNULL(pp.HorasProgramadas,1)*60)),
            pp.FechaInicioProgramada
        )
    ) AS FechaFinProgramada,
    po.PersonaID AS OperadorPlaneadoID,
    LTRIM(RTRIM(CONCAT(
        ISNULL(pop.Nombre,N''),N' ',
        ISNULL(pop.ApellidoPaterno,N''),N' ',
        ISNULL(pop.ApellidoMaterno,N'')))) AS OperadorPlaneadoNombre,
    e.EjecucionProduccionID,
    e.OperadorID AS OperadorRealID,
    ISNULL(e.OperadorNombre,N'') AS OperadorRealNombre
FROM dbo.Planeacion_ProgramaProduccion pp{hint}
OUTER APPLY
(
    SELECT TOP (1) x.PersonaID
    FROM dbo.Planeacion_ProgramaOperadores x
    WHERE x.ProgramaProduccionID=pp.ProgramaProduccionID
      AND x.Activo=1
      AND UPPER(ISNULL(x.RolOperador,N''))=N'PRINCIPAL'
    ORDER BY x.ProgramaOperadorID DESC
) po
LEFT JOIN dbo.Persona pop
    ON pop.PersonaID=po.PersonaID
OUTER APPLY
(
    SELECT TOP (1)
        pe.EjecucionProduccionID,
        pe.OperadorID,
        pe.OperadorNombre
    FROM dbo.Produccion_Ejecucion pe
    WHERE pe.ProgramaProduccionID=pp.ProgramaProduccionID
      AND pe.Activo=1
      AND pe.EstatusID NOT IN(6,9,99)
    ORDER BY pe.EjecucionProduccionID DESC
) e
WHERE pp.ProgramaProduccionID=@ProgramaID
  AND pp.Activo=1
  AND pp.MaquinaID IS NOT NULL
  AND pp.FechaInicioProgramada IS NOT NULL;";

        await using var cmd =
            tx == null ? new SqlCommand(sql, cn) : new SqlCommand(sql, cn, tx);

        cmd.Parameters.Add("@ProgramaID", SqlDbType.Int).Value = programaId;

        await using var rd = await cmd.ExecuteReaderAsync();
        if (!await rd.ReadAsync())
            return null;

        return new ProgramaCambioOperadorInterno
        {
            ProgramaID = Convert.ToInt32(rd["ProgramaProduccionID"]),
            MaquinaID = Convert.ToInt32(rd["MaquinaID"]),
            ParteID = rd["ParteID"] == DBNull.Value
                ? null : Convert.ToInt32(rd["ParteID"]),
            Inicio = Convert.ToDateTime(rd["FechaInicioProgramada"]),
            Fin = Convert.ToDateTime(rd["FechaFinProgramada"]),
            OperadorPlaneadoID = rd["OperadorPlaneadoID"] == DBNull.Value
                ? null : Convert.ToInt32(rd["OperadorPlaneadoID"]),
            OperadorPlaneadoNombre =
                rd["OperadorPlaneadoNombre"]?.ToString()?.Trim() ?? string.Empty,
            EjecucionID = rd["EjecucionProduccionID"] == DBNull.Value
                ? null : Convert.ToInt32(rd["EjecucionProduccionID"]),
            OperadorRealID = rd["OperadorRealID"] == DBNull.Value
                ? null : Convert.ToInt32(rd["OperadorRealID"]),
            OperadorRealNombre =
                rd["OperadorRealNombre"]?.ToString()?.Trim() ?? string.Empty
        };
    }

    private static async Task<List<object>> CargarCandidatosCambioOperadorAsync(
        int? parteId,
        SqlConnection cn,
        SqlTransaction? tx)
    {
        var tieneVista = false;

        const string vistaSql = @"
SELECT CONVERT(bit,CASE WHEN
    OBJECT_ID(N'dbo.vw_RRHH_PolivalenciaOperadoresParte',N'V') IS NOT NULL
THEN 1 ELSE 0 END);";

        await using (var vista =
            tx == null ? new SqlCommand(vistaSql, cn) : new SqlCommand(vistaSql, cn, tx))
        {
            tieneVista = Convert.ToBoolean(await vista.ExecuteScalarAsync() ?? false);
        }

        var lista = new List<object>();

        if (!tieneVista)
        {
            const string simple = @"
SELECT
    p.PersonaID,
    LTRIM(RTRIM(CONCAT(
        ISNULL(p.Nombre,N''),N' ',
        ISNULL(p.ApellidoPaterno,N''),N' ',
        ISNULL(p.ApellidoMaterno,N'')))) AS Nombre
FROM dbo.Persona p
WHERE ISNULL(p.EsColaboradorActivo,1)=1
  AND
  (
      UPPER(LTRIM(RTRIM(ISNULL(p.Puesto,N''))))
          COLLATE Modern_Spanish_CI_AI LIKE N'%OPERADOR%'
      OR EXISTS
      (
          SELECT 1
          FROM dbo.RRHH_EscalaAsignaciones a
          INNER JOIN dbo.RRHH_FuncionesPersonal f
              ON f.FuncionID=a.FuncionID
             AND f.Activo=1
          WHERE a.PersonalID=p.PersonaID
            AND a.Activo=1
            AND UPPER(LTRIM(RTRIM(f.Nombre)))
                COLLATE Modern_Spanish_CI_AI LIKE N'%OPERADOR%'
      )
  )
ORDER BY Nombre;";

            await using var cmd =
                tx == null ? new SqlCommand(simple, cn) : new SqlCommand(simple, cn, tx);

            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                lista.Add(new
                {
                    personaID = Convert.ToInt32(rd["PersonaID"]),
                    nombre = rd["Nombre"]?.ToString()?.Trim() ?? string.Empty,
                    nivel = (int?)null
                });
            }

            return lista;
        }

        const string sql = @"
DECLARE @TieneMatriz BIT=
    CASE WHEN @ParteID IS NOT NULL AND EXISTS
    (
        SELECT 1
        FROM dbo.vw_RRHH_PolivalenciaOperadoresParte
        WHERE ParteID=@ParteID
    )
    THEN 1 ELSE 0 END;

SELECT
    p.PersonaID,
    LTRIM(RTRIM(CONCAT(
        ISNULL(p.Nombre,N''),N' ',
        ISNULL(p.ApellidoPaterno,N''),N' ',
        ISNULL(p.ApellidoMaterno,N'')))) AS Nombre,
    pol.Nivel
FROM dbo.Persona p
OUTER APPLY
(
    SELECT TOP (1) TRY_CONVERT(INT,Nivel) AS Nivel
    FROM dbo.vw_RRHH_PolivalenciaOperadoresParte
    WHERE @TieneMatriz=1
      AND ParteID=@ParteID
      AND PersonalID=p.PersonaID
    ORDER BY TRY_CONVERT(INT,Nivel) DESC
) pol
WHERE ISNULL(p.EsColaboradorActivo,1)=1
  AND
  (
      UPPER(LTRIM(RTRIM(ISNULL(p.Puesto,N''))))
          COLLATE Modern_Spanish_CI_AI LIKE N'%OPERADOR%'
      OR EXISTS
      (
          SELECT 1
          FROM dbo.RRHH_EscalaAsignaciones a
          INNER JOIN dbo.RRHH_FuncionesPersonal f
              ON f.FuncionID=a.FuncionID
             AND f.Activo=1
          WHERE a.PersonalID=p.PersonaID
            AND a.Activo=1
            AND UPPER(LTRIM(RTRIM(f.Nombre)))
                COLLATE Modern_Spanish_CI_AI LIKE N'%OPERADOR%'
      )
  )
  AND (@TieneMatriz=0 OR pol.Nivel BETWEEN 1 AND 4)
ORDER BY
    CASE WHEN pol.Nivel IS NULL THEN 99 ELSE 4-pol.Nivel END,
    Nombre;";

        await using (var cmd =
            tx == null ? new SqlCommand(sql, cn) : new SqlCommand(sql, cn, tx))
        {
            cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value =
                parteId.HasValue ? parteId.Value : DBNull.Value;

            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                lista.Add(new
                {
                    personaID = Convert.ToInt32(rd["PersonaID"]),
                    nombre = rd["Nombre"]?.ToString()?.Trim() ?? string.Empty,
                    nivel = rd["Nivel"] == DBNull.Value
                        ? (int?)null : Convert.ToInt32(rd["Nivel"])
                });
            }
        }

        return lista;
    }

    private static async Task<bool> ValidarCandidatoCambioOperadorAsync(
        int personaId,
        int? parteId,
        SqlConnection cn,
        SqlTransaction tx)
    {
        const string personaSql = @"
SELECT CONVERT(bit,CASE WHEN EXISTS
(
    SELECT 1 FROM dbo.Persona
    WHERE PersonaID=@PersonaID
      AND ISNULL(EsColaboradorActivo,1)=1
) THEN 1 ELSE 0 END);";

        await using (var persona = new SqlCommand(personaSql, cn, tx))
        {
            persona.Parameters.Add("@PersonaID", SqlDbType.Int).Value = personaId;

            if (!Convert.ToBoolean(await persona.ExecuteScalarAsync() ?? false))
                return false;
        }

        const string vistaSql = @"
SELECT CONVERT(bit,CASE WHEN
    OBJECT_ID(N'dbo.vw_RRHH_PolivalenciaOperadoresParte',N'V') IS NOT NULL
THEN 1 ELSE 0 END);";

        await using (var vista = new SqlCommand(vistaSql, cn, tx))
        {
            if (!Convert.ToBoolean(await vista.ExecuteScalarAsync() ?? false))
                return true;
        }

        if (!parteId.HasValue)
            return true;

        const string sql = @"
IF NOT EXISTS
(
    SELECT 1
    FROM dbo.vw_RRHH_PolivalenciaOperadoresParte
    WHERE ParteID=@ParteID
)
BEGIN
    SELECT CAST(1 AS bit);
    RETURN;
END;

SELECT CONVERT(bit,CASE WHEN EXISTS
(
    SELECT 1
    FROM dbo.vw_RRHH_PolivalenciaOperadoresParte
    WHERE ParteID=@ParteID
      AND PersonalID=@PersonaID
      AND TRY_CONVERT(INT,Nivel) BETWEEN 1 AND 4
) THEN 1 ELSE 0 END);";

        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@PersonaID", SqlDbType.Int).Value = personaId;
        cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value = parteId.Value;

        return Convert.ToBoolean(await cmd.ExecuteScalarAsync() ?? false);
    }

    private static async Task CancelarExcepcionProgramaCambioOperadorAsync(
        int programaId,
        int usuarioId,
        SqlConnection cn,
        SqlTransaction tx)
    {
        const string sql = @"
UPDATE dbo.Produccion_OperadorExcepciones
SET
    Activo=0,
    UsuarioCancelacionID=@UsuarioID,
    FechaCancelacion=SYSDATETIME()
WHERE Activo=1
  AND ProgramaProduccionID=@ProgramaID;";

        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
        cmd.Parameters.Add("@ProgramaID", SqlDbType.Int).Value = programaId;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task InsertarExcepcionCambioOperadorAsync(
        ProgramaCambioOperadorInterno programa,
        TurnoCambioOperadorInterno turno,
        DateTime semana,
        DateTime fechaTrabajo,
        ProduccionPersonalCambioOperadorRequest request,
        DateTime inicioVigencia,
        DateTime finVigencia,
        int usuarioId,
        SqlConnection cn,
        SqlTransaction tx)
    {
        const string sql = @"
INSERT INTO dbo.Produccion_OperadorExcepciones
(
    ProgramaProduccionID,
    SemanaInicio,
    FechaTrabajo,
    TurnoID,
    MaquinaID,
    OperadorPlaneadoID,
    OperadorSustitutoID,
    Alcance,
    Motivo,
    Justificacion,
    InicioVigencia,
    FinVigencia,
    UsuarioCreacionID,
    FechaCreacion,
    Activo
)
VALUES
(
    @ProgramaID,@SemanaInicio,@FechaTrabajo,@TurnoID,@MaquinaID,
    @OperadorPlaneadoID,@OperadorSustitutoID,@Alcance,@Motivo,@Justificacion,
    @InicioVigencia,@FinVigencia,@UsuarioID,SYSDATETIME(),1
);";

        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@ProgramaID", SqlDbType.Int).Value = programa.ProgramaID;
        cmd.Parameters.Add("@SemanaInicio", SqlDbType.Date).Value = semana.Date;
        cmd.Parameters.Add("@FechaTrabajo", SqlDbType.Date).Value = fechaTrabajo.Date;
        cmd.Parameters.Add("@TurnoID", SqlDbType.Int).Value = turno.TurnoID;
        cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = programa.MaquinaID;
        cmd.Parameters.Add("@OperadorPlaneadoID", SqlDbType.Int).Value =
            programa.OperadorPlaneadoID.HasValue
                ? programa.OperadorPlaneadoID.Value : DBNull.Value;
        cmd.Parameters.Add("@OperadorSustitutoID", SqlDbType.Int).Value =
            request.OperadorSustitutoID;
        cmd.Parameters.Add("@Alcance", SqlDbType.NVarChar, 20).Value =
            request.Alcance;
        cmd.Parameters.Add("@Motivo", SqlDbType.NVarChar, 100).Value =
            request.Motivo;
        cmd.Parameters.Add("@Justificacion", SqlDbType.NVarChar, 500).Value =
            request.Justificacion;
        cmd.Parameters.Add("@InicioVigencia", SqlDbType.DateTime2).Value =
            inicioVigencia;
        cmd.Parameters.Add("@FinVigencia", SqlDbType.DateTime2).Value =
            finVigencia;
        cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;

        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<List<ProgramaCambioOperadorInterno>>
        CargarProgramasRestoTurnoCambioOperadorAsync(
            int maquinaId,
            int programaExcluir,
            DateTime inicio,
            DateTime fin,
            SqlConnection cn,
            SqlTransaction tx)
    {
        const string sql = @"
SELECT
    pp.ProgramaProduccionID,
    pp.MaquinaID,
    pp.ParteID,
    pp.FechaInicioProgramada,
    ISNULL(
        pp.FechaFinProgramada,
        DATEADD(
            MINUTE,
            CONVERT(INT,CEILING(ISNULL(pp.HorasProgramadas,1)*60)),
            pp.FechaInicioProgramada
        )
    ) AS FechaFinProgramada
FROM dbo.Planeacion_ProgramaProduccion pp WITH (UPDLOCK,HOLDLOCK)
WHERE pp.Activo=1
  AND pp.MaquinaID=@MaquinaID
  AND pp.ProgramaProduccionID<>@Excluir
  AND pp.FechaInicioProgramada>=@Inicio
  AND pp.FechaInicioProgramada<@Fin
  AND ISNULL(pp.EstatusID,1) NOT IN(6,9,99)
ORDER BY pp.FechaInicioProgramada,pp.ProgramaProduccionID;";

        var lista = new List<ProgramaCambioOperadorInterno>();

        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = maquinaId;
        cmd.Parameters.Add("@Excluir", SqlDbType.Int).Value = programaExcluir;
        cmd.Parameters.Add("@Inicio", SqlDbType.DateTime2).Value = inicio;
        cmd.Parameters.Add("@Fin", SqlDbType.DateTime2).Value = fin;

        await using var rd = await cmd.ExecuteReaderAsync();

        while (await rd.ReadAsync())
        {
            lista.Add(new ProgramaCambioOperadorInterno
            {
                ProgramaID = Convert.ToInt32(rd["ProgramaProduccionID"]),
                MaquinaID = Convert.ToInt32(rd["MaquinaID"]),
                ParteID = rd["ParteID"] == DBNull.Value
                    ? null : Convert.ToInt32(rd["ParteID"]),
                Inicio = Convert.ToDateTime(rd["FechaInicioProgramada"]),
                Fin = Convert.ToDateTime(rd["FechaFinProgramada"])
            });
        }

        return lista;
    }

    private static async Task ReemplazarPrincipalCambioOperadorAsync(
        int programaId,
        int personaId,
        int usuarioId,
        SqlConnection cn,
        SqlTransaction tx)
    {
        const string sql = @"
UPDATE dbo.Planeacion_ProgramaOperadores
SET
    Activo=0,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=GETDATE()
WHERE ProgramaProduccionID=@ProgramaID
  AND Activo=1
  AND UPPER(ISNULL(RolOperador,N''))=N'PRINCIPAL';

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
    @ProgramaID,@PersonaID,N'PRINCIPAL',1,@UsuarioID,GETDATE()
);";

        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@ProgramaID", SqlDbType.Int).Value = programaId;
        cmd.Parameters.Add("@PersonaID", SqlDbType.Int).Value = personaId;
        cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task ActualizarEjecucionCambioOperadorAsync(
        int ejecucionId,
        int personaId,
        string motivo,
        string justificacion,
        int usuarioId,
        SqlConnection cn,
        SqlTransaction tx)
    {
        const string sql = @"
DECLARE @Nombre NVARCHAR(250)=
(
    SELECT TOP (1)
        LTRIM(RTRIM(CONCAT(
            ISNULL(Nombre,N''),N' ',
            ISNULL(ApellidoPaterno,N''),N' ',
            ISNULL(ApellidoMaterno,N''))))
    FROM dbo.Persona
    WHERE PersonaID=@PersonaID
);

UPDATE dbo.Produccion_Ejecucion
SET
    OperadorID=@PersonaID,
    OperadorNombre=@Nombre,
    OperadoresModificadosManual=1,
    MotivoCambioOperadores=@MotivoCompleto,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=GETDATE()
WHERE EjecucionProduccionID=@EjecucionID
  AND Activo=1
  AND EstatusID NOT IN(6,9,99);";

        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@EjecucionID", SqlDbType.Int).Value = ejecucionId;
        cmd.Parameters.Add("@PersonaID", SqlDbType.Int).Value = personaId;
        cmd.Parameters.Add("@MotivoCompleto", SqlDbType.NVarChar, 500).Value =
            $"{motivo}: {justificacion}";
        cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
        await cmd.ExecuteNonQueryAsync();
    }
}
