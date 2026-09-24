using ERP.NSQuell.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;

namespace ERP.NSQuell.Controllers;

// NSQ_PRODUCCION_PERSONAL_SUPPORT_V12_0
public sealed partial class ProduccionPersonalController
{
    private static string CondicionCargoApoyoV12(string tipo)
    {
        tipo =
            (tipo ?? string.Empty)
                .Trim()
                .ToUpperInvariant();

        return tipo switch
        {
            "TECNICO" => @"(
                UPPER(ISNULL(p.Puesto,N'')) COLLATE Modern_Spanish_CI_AI LIKE N'%TECNIC%'
                AND UPPER(ISNULL(p.Puesto,N'')) COLLATE Modern_Spanish_CI_AI LIKE N'%PRODU%'
            )",

            "SMED" => @"(
                UPPER(ISNULL(p.Puesto,N'')) COLLATE Modern_Spanish_CI_AI LIKE N'%SMED%'
            )",

            "AUXILIAR" => @"(
                UPPER(ISNULL(p.Puesto,N'')) COLLATE Modern_Spanish_CI_AI LIKE N'%AUXILIAR%'
                AND UPPER(ISNULL(p.Puesto,N'')) COLLATE Modern_Spanish_CI_AI LIKE N'%PRODU%'
            )",

            _ => "1=0"
        };
    }

    private static async Task<List<ProduccionPersonalPersonaOpcionVm>>
        CargarPersonasApoyoCuentaCargoV12Async(
            string tipo,
            SqlConnection cn,
            SqlTransaction? tx)
    {
        tipo =
            (tipo ?? string.Empty)
                .Trim()
                .ToUpperInvariant();

        var cargo =
            CondicionCargoApoyoV12(
                tipo);

        var sql = $@"
SELECT
    p.PersonaID,
    ISNULL(p.NumeroControl,N'') AS NumeroControl,
    LTRIM(RTRIM(CONCAT(
        ISNULL(p.Nombre,N''),N' ',
        ISNULL(p.ApellidoPaterno,N''),N' ',
        ISNULL(p.ApellidoMaterno,N'')))) AS Nombre,
    ISNULL(p.Puesto,N'') AS Puesto
FROM dbo.Persona p
WHERE ISNULL(p.EsColaboradorActivo,1)=1
  AND {cargo}
  AND EXISTS
  (
      SELECT 1
      FROM dbo.Usuarios u
      WHERE u.PersonaID=p.PersonaID
        AND ISNULL(u.Activo,1)=1
  )
ORDER BY Nombre,p.PersonaID;";

        var lista =
            new List<ProduccionPersonalPersonaOpcionVm>();

        await using var cmd =
            tx == null
                ? new SqlCommand(sql,cn)
                : new SqlCommand(sql,cn,tx);

        await using var rd =
            await cmd.ExecuteReaderAsync();

        while (await rd.ReadAsync())
        {
            lista.Add(
                new ProduccionPersonalPersonaOpcionVm
                {
                    PersonaID =
                        Convert.ToInt32(
                            rd["PersonaID"]),

                    NumeroControl =
                        rd["NumeroControl"]
                            ?.ToString()
                            ?.Trim()
                        ?? string.Empty,

                    Nombre =
                        rd["Nombre"]
                            ?.ToString()
                            ?.Trim()
                        ?? string.Empty,

                    Puesto =
                        rd["Puesto"]
                            ?.ToString()
                            ?.Trim()
                        ?? string.Empty,

                    TipoOpcion =
                        tipo
                });
        }

        return lista;
    }

    private static async Task<int?>
        SugerirApoyoCuentaCargoV12Async(
            int escalaId,
            int turnoId,
            string tipo,
            SqlConnection cn,
            SqlTransaction? tx)
    {
        var cargo =
            CondicionCargoApoyoV12(
                tipo);

        var sql = $@"
SELECT TOP(1)
    a.PersonalID
FROM dbo.RRHH_EscalaAsignaciones a
INNER JOIN dbo.RRHH_EscalaTurnos et
    ON et.EscalaTurnoID=a.EscalaTurnoID
   AND et.EscalaID=a.EscalaID
   AND et.Activo=1
INNER JOIN dbo.Persona p
    ON p.PersonaID=a.PersonalID
   AND ISNULL(p.EsColaboradorActivo,1)=1
WHERE a.Activo=1
  AND a.EscalaID=@EscalaID
  AND et.TurnoOrigenID=@TurnoID
  AND {cargo}
  AND EXISTS
  (
      SELECT 1
      FROM dbo.Usuarios u
      WHERE u.PersonaID=p.PersonaID
        AND ISNULL(u.Activo,1)=1
  )
ORDER BY a.AsignacionID DESC;";

        await using var cmd =
            tx == null
                ? new SqlCommand(sql,cn)
                : new SqlCommand(sql,cn,tx);

        cmd.Parameters.Add(
            "@EscalaID",
            SqlDbType.Int).Value =
            escalaId;

        cmd.Parameters.Add(
            "@TurnoID",
            SqlDbType.Int).Value =
            turnoId;

        var value =
            await cmd.ExecuteScalarAsync();

        return
            value == null ||
            value == DBNull.Value
                ? null
                : Convert.ToInt32(value);
    }

    private static async Task
        ValidarPersonaApoyoCuentaCargoV12Async(
            int? personaId,
            string tipo,
            SqlConnection cn,
            SqlTransaction tx)
    {
        if (!personaId.HasValue ||
            personaId.Value <= 0)
        {
            return;
        }

        tipo =
            (tipo ?? string.Empty)
                .Trim()
                .ToUpperInvariant();

        var cargo =
            CondicionCargoApoyoV12(
                tipo);

        var sql = $@"
SELECT CONVERT(bit,CASE WHEN EXISTS
(
    SELECT 1
    FROM dbo.Persona p
    WHERE p.PersonaID=@PersonaID
      AND ISNULL(p.EsColaboradorActivo,1)=1
      AND {cargo}
      AND EXISTS
      (
          SELECT 1
          FROM dbo.Usuarios u
          WHERE u.PersonaID=p.PersonaID
            AND ISNULL(u.Activo,1)=1
      )
)
THEN 1 ELSE 0 END);";

        await using var cmd =
            new SqlCommand(
                sql,
                cn,
                tx);

        cmd.Parameters.Add(
            "@PersonaID",
            SqlDbType.Int).Value =
            personaId.Value;

        if (!Convert.ToBoolean(
                await cmd.ExecuteScalarAsync()
                ?? false))
        {
            throw new InvalidOperationException(
                $"La persona {personaId.Value} no tiene cuenta activa y cargo valido para {tipo}.");
        }
    }

    [HttpPost("GuardarCoberturaRolV12")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult>
        GuardarCoberturaRolV12(
            string? alcance,
            DateTime fechaClave,
            int turnoID,
            string? rol,
            int? personaID,
            string? justificacion)
    {
        if (!UsuarioEnSesion())
        {
            return Unauthorized(
                new
                {
                    ok=false,
                    message="La sesion expiro."
                });
        }

        alcance =
            (alcance ?? string.Empty)
                .Trim()
                .ToUpperInvariant();

        rol =
            (rol ?? string.Empty)
                .Trim()
                .ToUpperInvariant();

        if (alcance is not ("DIA" or "SEMANA"))
        {
            return BadRequest(
                new
                {
                    ok=false,
                    message="Alcance no valido."
                });
        }

        if (rol is not ("TECNICO" or "SMED" or "AUXILIAR"))
        {
            return BadRequest(
                new
                {
                    ok=false,
                    message="Rol no valido."
                });
        }

        if (turnoID <= 0)
        {
            return BadRequest(
                new
                {
                    ok=false,
                    message="Turno no valido."
                });
        }

        fechaClave =
            fechaClave.Date;

        await using var cn =
            new SqlConnection(
                ConnectionString);

        await cn.OpenAsync();

        if (!await ConfiguradoV2Async(
                cn,
                null))
        {
            return BadRequest(
                new
                {
                    ok=false,
                    message="Programacion de Personal no esta configurada."
                });
        }

        if (alcance == "DIA")
        {
            const string schemaSql = @"
SELECT CONVERT(bit,CASE
    WHEN OBJECT_ID(
        N'dbo.Produccion_PersonalTurnoCoberturaDia',
        N'U') IS NOT NULL
     AND COL_LENGTH(
        N'dbo.Produccion_PersonalTurnoCoberturaDia',
        N'Observaciones') IS NOT NULL
    THEN 1
    ELSE 0
END);";

            await using var schemaCmd =
                new SqlCommand(
                    schemaSql,
                    cn);

            if (!Convert.ToBoolean(
                    await schemaCmd.ExecuteScalarAsync()
                    ?? false))
            {
                return BadRequest(
                    new
                    {
                        ok=false,
                        message="Falta ejecutar NSQ_PRODUCCION_PERSONAL_SUPPORT_SCHEMA_DIAGNOSTICO_V11_1.sql en esta base."
                    });
            }
        }

        await using var tx =
            (SqlTransaction)
            await cn.BeginTransactionAsync(
                IsolationLevel.Serializable);

        try
        {
            var turnos =
                await CargarTurnosV2Async(
                    cn,
                    tx);

            if (!turnos.Any(
                    x => x.TurnoID == turnoID))
            {
                await tx.RollbackAsync();

                return BadRequest(
                    new
                    {
                        ok=false,
                        message="El turno ya no esta activo."
                    });
            }

            var cobertura =
                await ConstruirCoberturaEditableV12Async(
                    alcance,
                    fechaClave,
                    turnoID,
                    cn,
                    tx);

            var anterior =
                rol switch
                {
                    "TECNICO" =>
                        cobertura.TecnicoProduccionID,

                    "SMED" =>
                        cobertura.SmedID,

                    "AUXILIAR" =>
                        cobertura.AuxiliarID,

                    _ => null
                };

            if (anterior == personaID)
            {
                await tx.RollbackAsync();

                return Json(
                    new
                    {
                        ok=true,
                        sinCambios=true
                    });
            }

            var esCambio =
                anterior.HasValue;

            var motivo =
                string.IsNullOrWhiteSpace(
                    justificacion)
                    ? null
                    : justificacion.Trim();

            if (esCambio &&
                (string.IsNullOrWhiteSpace(motivo) ||
                 motivo.Length < 5))
            {
                await tx.RollbackAsync();

                return BadRequest(
                    new
                    {
                        ok=false,
                        message="Para cambiar una asignacion existente debes escribir una justificacion de al menos 5 caracteres."
                    });
            }

            if (motivo?.Length > 500)
            {
                await tx.RollbackAsync();

                return BadRequest(
                    new
                    {
                        ok=false,
                        message="La justificacion no puede superar 500 caracteres."
                    });
            }

            if (personaID.HasValue &&
                personaID.Value > 0)
            {
                await ValidarPersonaApoyoCuentaCargoV12Async(
                    personaID,
                    rol,
                    cn,
                    tx);
            }
            else
            {
                personaID =
                    null;
            }

            var teniaResponsable =
                cobertura.TecnicoProduccionID.HasValue ||
                cobertura.SmedID.HasValue;

            switch (rol)
            {
                case "TECNICO":
                    cobertura.TecnicoProduccionID =
                        personaID;
                    break;

                case "SMED":
                    cobertura.SmedID =
                        personaID;
                    break;

                case "AUXILIAR":
                    cobertura.AuxiliarID =
                        personaID;
                    break;
            }

            var conservaResponsable =
                cobertura.TecnicoProduccionID.HasValue ||
                cobertura.SmedID.HasValue;

            if (teniaResponsable &&
                !conservaResponsable)
            {
                await tx.RollbackAsync();

                return BadRequest(
                    new
                    {
                        ok=false,
                        message="El turno debe conservar al menos un Tecnico o un SMED."
                    });
            }

            var usuarioId =
                UsuarioID();

            if (alcance == "DIA")
            {
                await UpsertCoberturaDiaV10Async(
                    fechaClave,
                    cobertura,
                    usuarioId,
                    cn,
                    tx);

                const string obsDiaSql = @"
UPDATE dbo.Produccion_PersonalTurnoCoberturaDia
SET Observaciones=@Observaciones,
    UsuarioModificacionID=@Usuario,
    FechaModificacion=SYSDATETIME()
WHERE FechaTrabajo=@Fecha
  AND TurnoID=@TurnoID
  AND Activo=1;";

                await using var obsDiaCmd =
                    new SqlCommand(
                        obsDiaSql,
                        cn,
                        tx);

                obsDiaCmd.Parameters.Add(
                    "@Observaciones",
                    SqlDbType.NVarChar,
                    500).Value =
                    string.IsNullOrWhiteSpace(motivo)
                        ? DBNull.Value
                        : motivo;

                obsDiaCmd.Parameters.Add(
                    "@Usuario",
                    SqlDbType.Int).Value =
                    usuarioId;

                obsDiaCmd.Parameters.Add(
                    "@Fecha",
                    SqlDbType.Date).Value =
                    fechaClave;

                obsDiaCmd.Parameters.Add(
                    "@TurnoID",
                    SqlDbType.Int).Value =
                    turnoID;

                await obsDiaCmd.ExecuteNonQueryAsync();
            }
            else
            {
                var semana =
                    InicioSemanaV2(
                        fechaClave);

                await UpsertCoberturaV2Async(
                    semana,
                    cobertura,
                    usuarioId,
                    cn,
                    tx);

                const string obsSemanaSql = @"
UPDATE dbo.Produccion_PersonalTurnoCobertura
SET Observaciones=@Observaciones,
    UsuarioModificacionID=@Usuario,
    FechaModificacion=SYSDATETIME()
WHERE SemanaInicio=@Semana
  AND TurnoID=@TurnoID
  AND Activo=1;";

                await using var obsSemanaCmd =
                    new SqlCommand(
                        obsSemanaSql,
                        cn,
                        tx);

                obsSemanaCmd.Parameters.Add(
                    "@Observaciones",
                    SqlDbType.NVarChar,
                    500).Value =
                    string.IsNullOrWhiteSpace(motivo)
                        ? DBNull.Value
                        : motivo;

                obsSemanaCmd.Parameters.Add(
                    "@Usuario",
                    SqlDbType.Int).Value =
                    usuarioId;

                obsSemanaCmd.Parameters.Add(
                    "@Semana",
                    SqlDbType.Date).Value =
                    semana;

                obsSemanaCmd.Parameters.Add(
                    "@TurnoID",
                    SqlDbType.Int).Value =
                    turnoID;

                await obsSemanaCmd.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();

            return Json(
                new
                {
                    ok=true,
                    personaID,
                    rol,
                    alcance,
                    cambio=esCambio
                });
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

            return BadRequest(
                new
                {
                    ok=false,
                    message=ex.Message
                });
        }
    }

    private async Task<ProduccionPersonalTurnoGuardarVm>
        ConstruirCoberturaEditableV12Async(
            string alcance,
            DateTime fecha,
            int turnoId,
            SqlConnection cn,
            SqlTransaction tx)
    {
        fecha =
            fecha.Date;

        var semana =
            InicioSemanaV2(
                fecha);

        var semanales =
            await CargarCoberturasV2Async(
                semana,
                cn,
                tx);

        var semanal =
            semanales.FirstOrDefault(
                x => x.TurnoID == turnoId);

        int? tecnico =
            semanal?.TecnicoID;

        int? smed =
            semanal?.SmedID;

        int? auxiliar =
            semanal?.AuxiliarID;

        var persistida =
            semanal != null;

        if (alcance == "DIA" &&
            await ExisteCoberturaDiaV10Async(
                cn,
                tx))
        {
            var diarias =
                await CargarCoberturasDiaV10Async(
                    fecha,
                    cn,
                    tx);

            var diaria =
                diarias.FirstOrDefault(
                    x => x.TurnoID == turnoId);

            if (diaria != null)
            {
                tecnico =
                    diaria.TecnicoID;

                smed =
                    diaria.SmedID;

                auxiliar =
                    diaria.AuxiliarID;

                persistida =
                    true;
            }
        }

        if (!persistida)
        {
            var escala =
                await CargarEscalaSemanaV2Async(
                    semana,
                    cn,
                    tx);

            if (escala != null)
            {
                tecnico =
                    await SugerirApoyoCuentaCargoV12Async(
                        escala.EscalaID,
                        turnoId,
                        "TECNICO",
                        cn,
                        tx);

                smed =
                    await SugerirApoyoCuentaCargoV12Async(
                        escala.EscalaID,
                        turnoId,
                        "SMED",
                        cn,
                        tx);

                auxiliar =
                    await SugerirApoyoCuentaCargoV12Async(
                        escala.EscalaID,
                        turnoId,
                        "AUXILIAR",
                        cn,
                        tx);
            }
        }

        return
            new ProduccionPersonalTurnoGuardarVm
            {
                TurnoID =
                    turnoId,

                TecnicoProduccionID =
                    tecnico,

                SmedID =
                    smed,

                AuxiliarID =
                    auxiliar
            };
    }
}