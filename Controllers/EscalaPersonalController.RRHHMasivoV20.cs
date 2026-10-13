﻿// NSQ_RRHH_ESCALA_SEMANAL_INTEGRAL_V2_4
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;

namespace ERP.NSQuell.Controllers;

public partial class EscalaPersonalController
{
    public sealed class RRHHAsignacionMasivaV20Vm
    {
        public int EscalaID { get; set; }
        public int FuncionID { get; set; }
        public int EscalaTurnoID { get; set; }
        public int? MaquinaID { get; set; }
        public List<int> PersonalIDs { get; set; } = new();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GuardarAsignacionesMasivasV20(
        [FromForm] RRHHAsignacionMasivaV20Vm vm)
    {
        var usuarioID = ObtenerUsuarioID();
        if (!usuarioID.HasValue)
            return Unauthorized(new { ok = false, mensaje = "La sesión terminó." });

        vm.PersonalIDs = (vm.PersonalIDs ?? new List<int>())
            .Where(x => x > 0)
            .Distinct()
            .ToList();

        if (vm.EscalaID <= 0)
            return BadRequest(new { ok = false, mensaje = "Escala inválida." });

        if (vm.PersonalIDs.Count == 0)
            return BadRequest(new { ok = false, mensaje = "Selecciona por lo menos una persona." });

        await using var cn = new SqlConnection(_connectionString);
        await cn.OpenAsync();

        await using var tx =
            (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);

        try
        {
            DateTime fechaInicio;
            DateTime fechaFin;
            string estadoEscala;

            const string sqlEscala = @"
SELECT FechaInicio,FechaFin,Estado
FROM dbo.RRHH_EscalasPersonal WITH(UPDLOCK,HOLDLOCK)
WHERE EscalaID=@EscalaID
  AND Activo=1;";

            await using (var cmd = new SqlCommand(sqlEscala,cn,tx))
            {
                cmd.Parameters.Add("@EscalaID",SqlDbType.Int).Value = vm.EscalaID;
                await using var rd = await cmd.ExecuteReaderAsync();

                if (!await rd.ReadAsync())
                    throw new InvalidOperationException("La escala ya no existe.");

                fechaInicio = Convert.ToDateTime(rd["FechaInicio"]).Date;
                fechaFin = Convert.ToDateTime(rd["FechaFin"]).Date;
                estadoEscala = rd["Estado"]?.ToString()?.Trim() ?? string.Empty;
            }

            if (!string.Equals(
                    estadoEscala,
                    ERP.NSQuell.Models.EscalaPersonal.Estados.Borrador,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "La escala debe estar en Borrador para modificar horarios.");
            }

            var idsValidos = new HashSet<int>();

            const string sqlPersona = @"
SELECT PersonaID
FROM dbo.Persona WITH(UPDLOCK,HOLDLOCK)
WHERE PersonaID=@PersonaID
  AND ISNULL(EsColaboradorActivo,1)=1;";

            foreach (var personaID in vm.PersonalIDs)
            {
                await using var cmd = new SqlCommand(sqlPersona,cn,tx);
                cmd.Parameters.Add("@PersonaID",SqlDbType.Int).Value = personaID;
                var valor = await cmd.ExecuteScalarAsync();

                if (valor != null && valor != DBNull.Value)
                    idsValidos.Add(personaID);
            }

            if (idsValidos.Count == 0)
                throw new InvalidOperationException(
                    "Ninguna de las personas seleccionadas continúa activa.");

            // EscalaTurnoID <= 0 se interpreta como "Sin horario".
            if (vm.EscalaTurnoID <= 0)
            {
                const string sqlQuitar = @"
UPDATE dbo.RRHH_EscalaAsignaciones
SET Activo=0,
    FechaModificacion=SYSDATETIME(),
    ActualizadoPor=@UsuarioID
WHERE EscalaID=@EscalaID
  AND PersonalID=@PersonalID
  AND Activo=1;";

                foreach (var personaID in idsValidos)
                {
                    await using var cmd = new SqlCommand(sqlQuitar,cn,tx);
                    cmd.Parameters.Add("@UsuarioID",SqlDbType.Int).Value = usuarioID.Value;
                    cmd.Parameters.Add("@EscalaID",SqlDbType.Int).Value = vm.EscalaID;
                    cmd.Parameters.Add("@PersonalID",SqlDbType.Int).Value = personaID;
                    await cmd.ExecuteNonQueryAsync();
                }

                await tx.CommitAsync();

                return Json(new
                {
                    ok = true,
                    personalIDs = idsValidos.OrderBy(x => x).ToArray(),
                    mensaje = $"Se quitó el horario semanal a {idsValidos.Count} persona(s)."
                });
            }

            const string sqlTurno = @"
SELECT COUNT(1)
FROM dbo.RRHH_EscalaTurnos
WHERE EscalaTurnoID=@EscalaTurnoID
  AND EscalaID=@EscalaID
  AND Activo=1;";

            await using (var cmd = new SqlCommand(sqlTurno,cn,tx))
            {
                cmd.Parameters.Add("@EscalaTurnoID",SqlDbType.Int).Value = vm.EscalaTurnoID;
                cmd.Parameters.Add("@EscalaID",SqlDbType.Int).Value = vm.EscalaID;

                if (Convert.ToInt32(await cmd.ExecuteScalarAsync()) != 1)
                    throw new InvalidOperationException(
                        "El horario seleccionado no pertenece a esta semana.");
            }

            const string sqlFuncion = @"
SELECT COUNT(1)
FROM dbo.RRHH_FuncionesPersonal
WHERE FuncionID=@FuncionID
  AND Activo=1;";

            await using (var cmd = new SqlCommand(sqlFuncion,cn,tx))
            {
                cmd.Parameters.Add("@FuncionID",SqlDbType.Int).Value = vm.FuncionID;

                if (vm.FuncionID <= 0 ||
                    Convert.ToInt32(await cmd.ExecuteScalarAsync()) != 1)
                {
                    throw new InvalidOperationException(
                        "Selecciona un tipo de personal válido.");
                }
            }

            if (vm.MaquinaID.HasValue)
            {
                const string sqlMaquina = @"
SELECT COUNT(1)
FROM dbo.ERP_Maquinas
WHERE MaquinaID=@MaquinaID
  AND ISNULL(Activo,1)=1;";

                await using var cmd = new SqlCommand(sqlMaquina,cn,tx);
                cmd.Parameters.Add("@MaquinaID",SqlDbType.Int).Value = vm.MaquinaID.Value;

                if (Convert.ToInt32(await cmd.ExecuteScalarAsync()) != 1)
                    throw new InvalidOperationException(
                        "La máquina seleccionada ya no está activa.");
            }

            const string sqlDesactivar = @"
UPDATE dbo.RRHH_EscalaAsignaciones
SET Activo=0,
    FechaModificacion=SYSDATETIME(),
    ActualizadoPor=@UsuarioID
WHERE EscalaID=@EscalaID
  AND PersonalID=@PersonalID
  AND Activo=1;";

            const string sqlInsertar = @"
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
    FechaRegistro,
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
    NULL,
    1,
    SYSDATETIME(),
    @UsuarioID
);";

            foreach (var personaID in idsValidos)
            {
                await using (var cmd = new SqlCommand(sqlDesactivar,cn,tx))
                {
                    cmd.Parameters.Add("@UsuarioID",SqlDbType.Int).Value = usuarioID.Value;
                    cmd.Parameters.Add("@EscalaID",SqlDbType.Int).Value = vm.EscalaID;
                    cmd.Parameters.Add("@PersonalID",SqlDbType.Int).Value = personaID;
                    await cmd.ExecuteNonQueryAsync();
                }

                await using (var cmd = new SqlCommand(sqlInsertar,cn,tx))
                {
                    cmd.Parameters.Add("@EscalaID",SqlDbType.Int).Value = vm.EscalaID;
                    cmd.Parameters.Add("@PersonalID",SqlDbType.Int).Value = personaID;
                    cmd.Parameters.Add("@FuncionID",SqlDbType.Int).Value = vm.FuncionID;
                    cmd.Parameters.Add("@MaquinaID",SqlDbType.Int).Value =
                        vm.MaquinaID.HasValue
                            ? vm.MaquinaID.Value
                            : DBNull.Value;
                    cmd.Parameters.Add("@EscalaTurnoID",SqlDbType.Int).Value = vm.EscalaTurnoID;
                    cmd.Parameters.Add("@FechaInicio",SqlDbType.Date).Value = fechaInicio;
                    cmd.Parameters.Add("@FechaFin",SqlDbType.Date).Value = fechaFin;
                    cmd.Parameters.Add("@UsuarioID",SqlDbType.Int).Value = usuarioID.Value;
                    await cmd.ExecuteNonQueryAsync();
                }
            }

            await tx.CommitAsync();

            return Json(new
            {
                ok = true,
                personalIDs = idsValidos.OrderBy(x => x).ToArray(),
                mensaje = $"Horario aplicado a {idsValidos.Count} persona(s)."
            });
        }
        catch (Exception ex)
        {
            try { await tx.RollbackAsync(); } catch { }

            return BadRequest(new
            {
                ok = false,
                mensaje = ex.Message
            });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CopiarSemanaAnteriorV20(int escalaID)
    {
        var usuarioID = ObtenerUsuarioID();
        if (!usuarioID.HasValue)
            return Unauthorized(new { ok = false, mensaje = "La sesión terminó." });

        if (escalaID <= 0)
            return BadRequest(new { ok = false, mensaje = "Escala inválida." });

        await using var cn = new SqlConnection(_connectionString);
        await cn.OpenAsync();

        await using var tx =
            (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);

        try
        {
            DateTime actualInicio;
            DateTime actualFin;
            string estadoActual;

            const string sqlActual = @"
SELECT FechaInicio,FechaFin,Estado
FROM dbo.RRHH_EscalasPersonal WITH(UPDLOCK,HOLDLOCK)
WHERE EscalaID=@EscalaID
  AND Activo=1;";

            await using (var cmd = new SqlCommand(sqlActual,cn,tx))
            {
                cmd.Parameters.Add("@EscalaID",SqlDbType.Int).Value = escalaID;
                await using var rd = await cmd.ExecuteReaderAsync();

                if (!await rd.ReadAsync())
                    throw new InvalidOperationException("La escala actual ya no existe.");

                actualInicio = Convert.ToDateTime(rd["FechaInicio"]).Date;
                actualFin = Convert.ToDateTime(rd["FechaFin"]).Date;
                estadoActual = rd["Estado"]?.ToString()?.Trim() ?? string.Empty;
            }

            if (!string.Equals(
                    estadoActual,
                    ERP.NSQuell.Models.EscalaPersonal.Estados.Borrador,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "La semana debe estar en Borrador para copiar una escala anterior.");
            }

            int? escalaAnteriorID = null;

            const string sqlAnterior = @"
SELECT TOP(1) EscalaID
FROM dbo.RRHH_EscalasPersonal
WHERE Activo=1
  AND EscalaID<>@EscalaID
  AND FechaInicio<@ActualInicio
  AND Estado IN(N'Publicada',N'Borrador')
ORDER BY
    FechaInicio DESC,
    CASE WHEN Estado=N'Publicada' THEN 0 ELSE 1 END,
    EscalaID DESC;";

            await using (var cmd = new SqlCommand(sqlAnterior,cn,tx))
            {
                cmd.Parameters.Add("@EscalaID",SqlDbType.Int).Value = escalaID;
                cmd.Parameters.Add("@ActualInicio",SqlDbType.Date).Value = actualInicio;
                var valor = await cmd.ExecuteScalarAsync();

                if (valor != null && valor != DBNull.Value)
                    escalaAnteriorID = Convert.ToInt32(valor);
            }

            if (!escalaAnteriorID.HasValue)
                throw new InvalidOperationException(
                    "No se encontró una escala anterior para copiar.");

            const string sqlLimpiar = @"
UPDATE dbo.RRHH_EscalaAsignaciones
SET Activo=0,
    FechaModificacion=SYSDATETIME(),
    ActualizadoPor=@UsuarioID
WHERE EscalaID=@EscalaID
  AND Activo=1;";

            await using (var cmd = new SqlCommand(sqlLimpiar,cn,tx))
            {
                cmd.Parameters.Add("@UsuarioID",SqlDbType.Int).Value = usuarioID.Value;
                cmd.Parameters.Add("@EscalaID",SqlDbType.Int).Value = escalaID;
                await cmd.ExecuteNonQueryAsync();
            }

            const string sqlCopiar = @"
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
    FechaRegistro,
    CreadoPor
)
SELECT
    @EscalaActualID,
    a.PersonalID,
    NULL,
    a.FuncionID,
    NULL,
    turnoActual.EscalaTurnoID,
    @FechaInicio,
    @FechaFin,
    CASE
        WHEN NULLIF(LTRIM(RTRIM(a.Observaciones)),N'') IS NULL
            THEN N'Copiado de escala anterior'
        ELSE LEFT(a.Observaciones+N' | Copiado de escala anterior',500)
    END,
    1,
    SYSDATETIME(),
    @UsuarioID
FROM dbo.RRHH_EscalaAsignaciones a
INNER JOIN dbo.Persona p
    ON p.PersonaID=a.PersonalID
   AND ISNULL(p.EsColaboradorActivo,1)=1
INNER JOIN dbo.RRHH_EscalaTurnos turnoAnterior
    ON turnoAnterior.EscalaTurnoID=a.EscalaTurnoID
   AND turnoAnterior.EscalaID=a.EscalaID
   AND turnoAnterior.Activo=1
CROSS APPLY
(
    SELECT TOP(1) t.EscalaTurnoID
    FROM dbo.RRHH_EscalaTurnos t
    WHERE t.EscalaID=@EscalaActualID
      AND t.Activo=1
      AND
      (
          (
              turnoAnterior.TurnoOrigenID IS NOT NULL
              AND t.TurnoOrigenID=turnoAnterior.TurnoOrigenID
          )
          OR
          (
              UPPER(LTRIM(RTRIM(t.Nombre)))=
              UPPER(LTRIM(RTRIM(turnoAnterior.Nombre)))
          )
      )
    ORDER BY
        CASE
            WHEN turnoAnterior.TurnoOrigenID IS NOT NULL
             AND t.TurnoOrigenID=turnoAnterior.TurnoOrigenID
                THEN 0
            ELSE 1
        END,
        t.Orden,
        t.EscalaTurnoID
) turnoActual
WHERE a.EscalaID=@EscalaAnteriorID
  AND a.Activo=1
  AND EXISTS
  (
      SELECT 1
      FROM dbo.RRHH_FuncionesPersonal f
      WHERE f.FuncionID=a.FuncionID
        AND f.Activo=1
  );";

            int copiadas;

            await using (var cmd = new SqlCommand(sqlCopiar,cn,tx))
            {
                cmd.Parameters.Add("@EscalaActualID",SqlDbType.Int).Value = escalaID;
                cmd.Parameters.Add("@EscalaAnteriorID",SqlDbType.Int).Value = escalaAnteriorID.Value;
                cmd.Parameters.Add("@FechaInicio",SqlDbType.Date).Value = actualInicio;
                cmd.Parameters.Add("@FechaFin",SqlDbType.Date).Value = actualFin;
                cmd.Parameters.Add("@UsuarioID",SqlDbType.Int).Value = usuarioID.Value;
                copiadas = await cmd.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();

            return Json(new
            {
                ok = true,
                mensaje = $"Se copiaron {copiadas} asignación(es) de la semana anterior."
            });
        }
        catch (Exception ex)
        {
            try { await tx.RollbackAsync(); } catch { }

            return BadRequest(new
            {
                ok = false,
                mensaje = ex.Message
            });
        }
    }
}