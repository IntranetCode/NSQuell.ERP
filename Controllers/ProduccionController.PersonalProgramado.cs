using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;

namespace ERP.NSQuell.Controllers;

// NSQ_PRODUCCION_PERSONAL_INTEGRACION_V30
public sealed partial class ProduccionController
{
    private sealed class PersonalProgramadoProduccionInterno
    {
        public int AsignacionPersonalID { get; set; }
        public int? OperadorID { get; set; }
        public string? OperadorNombre { get; set; }
        public int? AuxiliarID { get; set; }
        public string? AuxiliarNombre { get; set; }
        public int? TecnicoID { get; set; }
        public string? TecnicoNombre { get; set; }
        public string? TurnoNombre { get; set; }
        public DateTime Inicio { get; set; }
        public DateTime Fin { get; set; }
    }

    private static async Task<PersonalProgramadoProduccionInterno?>
        ObtenerPersonalProgramadoProduccionAsync(
            int programaProduccionId,
            DateTime momento,
            DateTime? momentoAlterno,
            SqlConnection cn,
            SqlTransaction? tx)
    {
        if (programaProduccionId <= 0)
            return null;

        const string sql = @"
IF OBJECT_ID(N'dbo.Produccion_ProgramaPersonalAsignaciones',N'U') IS NULL
BEGIN
    SELECT TOP (0)
        CAST(NULL AS INT) AS AsignacionPersonalID,
        CAST(NULL AS INT) AS OperadorID,
        CAST(NULL AS NVARCHAR(200)) AS OperadorNombre,
        CAST(NULL AS INT) AS AuxiliarID,
        CAST(NULL AS NVARCHAR(200)) AS AuxiliarNombre,
        CAST(NULL AS INT) AS TecnicoProduccionID,
        CAST(NULL AS NVARCHAR(200)) AS TecnicoProduccionNombre,
        CAST(NULL AS NVARCHAR(100)) AS TurnoNombre,
        CAST(NULL AS DATETIME2) AS Inicio,
        CAST(NULL AS DATETIME2) AS Fin;
    RETURN;
END;

SELECT
    COALESCE(op.AsignacionPersonalID,aux.AsignacionPersonalID,tec.AsignacionPersonalID) AS AsignacionPersonalID,
    op.OperadorID,
    op.OperadorNombre,
    aux.AuxiliarID,
    aux.AuxiliarNombre,
    tec.TecnicoProduccionID,
    tec.TecnicoProduccionNombre,
    COALESCE(op.TurnoNombre,aux.TurnoNombre,tec.TurnoNombre,N'') AS TurnoNombre,
    COALESCE(op.Inicio,aux.Inicio,tec.Inicio) AS Inicio,
    COALESCE(op.Fin,aux.Fin,tec.Fin) AS Fin
FROM (SELECT 1 AS X) base
OUTER APPLY
(
    SELECT TOP (1)
        a.AsignacionPersonalID,
        a.OperadorID,
        LTRIM(RTRIM(CONCAT(ISNULL(p.Nombre,N''),N' ',ISNULL(p.ApellidoPaterno,N''),N' ',ISNULL(p.ApellidoMaterno,N'')))) AS OperadorNombre,
        a.TurnoNombre,
        a.Inicio,
        a.Fin
    FROM dbo.Produccion_ProgramaPersonalAsignaciones a
    LEFT JOIN dbo.Persona p ON p.PersonaID=a.OperadorID
    WHERE a.Activo=1
      AND a.ProgramaProduccionID=@ProgramaProduccionID
      AND a.OperadorID IS NOT NULL
      AND
      (
          (@Momento>=a.Inicio AND @Momento<a.Fin)
          OR (@MomentoAlterno IS NOT NULL AND @MomentoAlterno>=a.Inicio AND @MomentoAlterno<a.Fin)
      )
    ORDER BY CASE WHEN @Momento>=a.Inicio AND @Momento<a.Fin THEN 0 ELSE 1 END,a.Inicio,a.AsignacionPersonalID DESC
) op
OUTER APPLY
(
    SELECT TOP (1)
        a.AsignacionPersonalID,
        a.AuxiliarID,
        LTRIM(RTRIM(CONCAT(ISNULL(p.Nombre,N''),N' ',ISNULL(p.ApellidoPaterno,N''),N' ',ISNULL(p.ApellidoMaterno,N'')))) AS AuxiliarNombre,
        a.TurnoNombre,
        a.Inicio,
        a.Fin
    FROM dbo.Produccion_ProgramaPersonalAsignaciones a
    LEFT JOIN dbo.Persona p ON p.PersonaID=a.AuxiliarID
    WHERE a.Activo=1
      AND a.ProgramaProduccionID=@ProgramaProduccionID
      AND a.AuxiliarID IS NOT NULL
      AND
      (
          (@Momento>=a.Inicio AND @Momento<a.Fin)
          OR (@MomentoAlterno IS NOT NULL AND @MomentoAlterno>=a.Inicio AND @MomentoAlterno<a.Fin)
      )
    ORDER BY CASE WHEN @Momento>=a.Inicio AND @Momento<a.Fin THEN 0 ELSE 1 END,a.Inicio,a.AsignacionPersonalID DESC
) aux
OUTER APPLY
(
    SELECT TOP (1)
        a.AsignacionPersonalID,
        a.TecnicoProduccionID,
        LTRIM(RTRIM(CONCAT(ISNULL(p.Nombre,N''),N' ',ISNULL(p.ApellidoPaterno,N''),N' ',ISNULL(p.ApellidoMaterno,N'')))) AS TecnicoProduccionNombre,
        a.TurnoNombre,
        a.Inicio,
        a.Fin
    FROM dbo.Produccion_ProgramaPersonalAsignaciones a
    LEFT JOIN dbo.Persona p ON p.PersonaID=a.TecnicoProduccionID
    WHERE a.Activo=1
      AND a.ProgramaProduccionID=@ProgramaProduccionID
      AND a.TecnicoProduccionID IS NOT NULL
      AND
      (
          (@Momento>=a.Inicio AND @Momento<a.Fin)
          OR (@MomentoAlterno IS NOT NULL AND @MomentoAlterno>=a.Inicio AND @MomentoAlterno<a.Fin)
      )
    ORDER BY CASE WHEN @Momento>=a.Inicio AND @Momento<a.Fin THEN 0 ELSE 1 END,a.Inicio,a.AsignacionPersonalID DESC
) tec
WHERE op.AsignacionPersonalID IS NOT NULL
   OR aux.AsignacionPersonalID IS NOT NULL
   OR tec.AsignacionPersonalID IS NOT NULL;";

        await using var cmd =
            tx == null
                ? new SqlCommand(sql, cn)
                : new SqlCommand(sql, cn, tx);

        cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value =
            programaProduccionId;
        cmd.Parameters.Add("@Momento", SqlDbType.DateTime2).Value = momento;
        cmd.Parameters.Add("@MomentoAlterno", SqlDbType.DateTime2).Value =
            momentoAlterno.HasValue ? momentoAlterno.Value : DBNull.Value;

        await using var rd = await cmd.ExecuteReaderAsync();
        if (!await rd.ReadAsync())
            return null;

        return new PersonalProgramadoProduccionInterno
        {
            AsignacionPersonalID = Convert.ToInt32(rd["AsignacionPersonalID"]),
            OperadorID = rd["OperadorID"] == DBNull.Value ? null : Convert.ToInt32(rd["OperadorID"]),
            OperadorNombre = rd["OperadorNombre"] == DBNull.Value ? null : rd["OperadorNombre"]?.ToString()?.Trim(),
            AuxiliarID = rd["AuxiliarID"] == DBNull.Value ? null : Convert.ToInt32(rd["AuxiliarID"]),
            AuxiliarNombre = rd["AuxiliarNombre"] == DBNull.Value ? null : rd["AuxiliarNombre"]?.ToString()?.Trim(),
            TecnicoID = rd["TecnicoProduccionID"] == DBNull.Value ? null : Convert.ToInt32(rd["TecnicoProduccionID"]),
            TecnicoNombre = rd["TecnicoProduccionNombre"] == DBNull.Value ? null : rd["TecnicoProduccionNombre"]?.ToString()?.Trim(),
            TurnoNombre = rd["TurnoNombre"] == DBNull.Value ? null : rd["TurnoNombre"]?.ToString()?.Trim(),
            Inicio = Convert.ToDateTime(rd["Inicio"]),
            Fin = Convert.ToDateTime(rd["Fin"])
        };
    }

    [HttpGet]
    public async Task<IActionResult> PersonalProgramadoPrograma(
        int programaProduccionId)
    {
        if (!UsuarioEnSesion())
            return Unauthorized();

        if (programaProduccionId <= 0)
            return Json(new { ok = false, mensaje = "Programa no válido." });

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync();

        DateTime? inicioPrograma = null;
        const string sqlPrograma = @"
SELECT TOP (1) FechaInicioProgramada
FROM dbo.Planeacion_ProgramaProduccion
WHERE ProgramaProduccionID=@ProgramaProduccionID
  AND Activo=1;";

        await using (var cmd = new SqlCommand(sqlPrograma, cn))
        {
            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaProduccionId;
            var value = await cmd.ExecuteScalarAsync();
            if (value != null && value != DBNull.Value)
                inicioPrograma = Convert.ToDateTime(value);
        }

        var personal = await ObtenerPersonalProgramadoProduccionAsync(
            programaProduccionId,
            DateTime.Now,
            inicioPrograma,
            cn,
            null);

        if (personal == null)
        {
            return Json(new
            {
                ok = true,
                asignado = false
            });
        }

        return Json(new
        {
            ok = true,
            asignado = true,
            asignacionPersonalID = personal.AsignacionPersonalID,
            operadorID = personal.OperadorID,
            operadorNombre = personal.OperadorNombre,
            auxiliarID = personal.AuxiliarID,
            auxiliarNombre = personal.AuxiliarNombre,
            tecnicoProduccionID = personal.TecnicoID,
            tecnicoProduccionNombre = personal.TecnicoNombre,
            turno = personal.TurnoNombre,
            inicio = personal.Inicio,
            fin = personal.Fin
        });
    }
}
