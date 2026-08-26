using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;

namespace ERP.NSQuell.Controllers;

public sealed partial class ProduccionController
{
    // NSQ_PERSONAL_INICIO_V8
    // Programacion de Personal es fuente de verdad.
    // Produccion solamente completa datos faltantes.

    [HttpGet]
    public async Task<IActionResult> ResponsablesApoyoInicioActivos()
    {
        if (!UsuarioEnSesion()) return Unauthorized();

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync();

        const string sql = @"
WITH Roles AS
(
    SELECT
        p.PersonaID,
        ISNULL(p.NumeroControl,N'') AS NumeroControl,
        LTRIM(RTRIM(CONCAT(
            ISNULL(p.Nombre,N''),N' ',
            ISNULL(p.ApellidoPaterno,N''),N' ',
            ISNULL(p.ApellidoMaterno,N'')))) AS Nombre,
        ISNULL(p.Puesto,N'') AS Puesto,
        MAX(CASE WHEN
            UPPER(ISNULL(p.Puesto,N'')) COLLATE Modern_Spanish_CI_AI LIKE N'%SMED%'
            OR UPPER(ISNULL(f.Nombre,N'')) COLLATE Modern_Spanish_CI_AI LIKE N'%SMED%'
            THEN 1 ELSE 0 END) AS EsSmed,
        MAX(CASE WHEN
            UPPER(ISNULL(p.Puesto,N'')) COLLATE Modern_Spanish_CI_AI LIKE N'%TECNIC%'
            OR UPPER(ISNULL(f.Nombre,N'')) COLLATE Modern_Spanish_CI_AI LIKE N'%TECNIC%'
            THEN 1 ELSE 0 END) AS EsTecnico
    FROM dbo.Persona p
    LEFT JOIN dbo.RRHH_EscalaAsignaciones a
      ON a.PersonalID=p.PersonaID AND a.Activo=1
    LEFT JOIN dbo.RRHH_FuncionesPersonal f
      ON f.FuncionID=a.FuncionID AND f.Activo=1
    WHERE ISNULL(p.EsColaboradorActivo,1)=1
    GROUP BY p.PersonaID,p.NumeroControl,p.Nombre,
             p.ApellidoPaterno,p.ApellidoMaterno,p.Puesto
)
SELECT PersonaID,NumeroControl,Nombre,Puesto,EsSmed,EsTecnico
FROM Roles
WHERE EsSmed=1 OR EsTecnico=1
ORDER BY CASE WHEN EsSmed=1 THEN 0 ELSE 1 END,Nombre;";

        var lista = new List<object>();
        await using var cmd = new SqlCommand(sql,cn);
        await using var rd = await cmd.ExecuteReaderAsync();
        while(await rd.ReadAsync())
        {
            var esSmed = Convert.ToInt32(rd["EsSmed"]) == 1;
            lista.Add(new
            {
                personaID=Convert.ToInt32(rd["PersonaID"]),
                numeroControl=rd["NumeroControl"]?.ToString()?.Trim() ?? string.Empty,
                nombre=rd["Nombre"]?.ToString()?.Trim() ?? string.Empty,
                puesto=rd["Puesto"]?.ToString()?.Trim() ?? string.Empty,
                tipo=esSmed ? "SMED" : "TECNICO"
            });
        }
        return Json(new { ok=true, opciones=lista });
    }

    private static async Task<(bool Valido,string? Nombre)>
        ValidarResponsableApoyoInicioV8Async(
            int personaId,string tipo,SqlConnection cn,SqlTransaction tx)
    {
        if(personaId<=0) return (false,null);
        tipo=(tipo??string.Empty).Trim().ToUpperInvariant();
        if(tipo is not ("TECNICO" or "SMED")) return (false,null);

        var condicion = tipo=="SMED"
            ? @"(
                UPPER(ISNULL(p.Puesto,N'')) COLLATE Modern_Spanish_CI_AI LIKE N'%SMED%'
                OR UPPER(ISNULL(f.Nombre,N'')) COLLATE Modern_Spanish_CI_AI LIKE N'%SMED%'
              )"
            : @"(
                UPPER(ISNULL(p.Puesto,N'')) COLLATE Modern_Spanish_CI_AI LIKE N'%TECNIC%'
                OR UPPER(ISNULL(f.Nombre,N'')) COLLATE Modern_Spanish_CI_AI LIKE N'%TECNIC%'
              )";

        var sql=$@"
SELECT TOP(1)
    LTRIM(RTRIM(CONCAT(
        ISNULL(p.Nombre,N''),N' ',
        ISNULL(p.ApellidoPaterno,N''),N' ',
        ISNULL(p.ApellidoMaterno,N''))))
FROM dbo.Persona p
LEFT JOIN dbo.RRHH_EscalaAsignaciones a
  ON a.PersonalID=p.PersonaID AND a.Activo=1
LEFT JOIN dbo.RRHH_FuncionesPersonal f
  ON f.FuncionID=a.FuncionID AND f.Activo=1
WHERE p.PersonaID=@PersonaID
  AND ISNULL(p.EsColaboradorActivo,1)=1
  AND {condicion};";

        await using var cmd=new SqlCommand(sql,cn,tx);
        cmd.Parameters.Add("@PersonaID",SqlDbType.Int).Value=personaId;
        var value=await cmd.ExecuteScalarAsync();
        if(value==null || value==DBNull.Value || string.IsNullOrWhiteSpace(value.ToString()))
            return (false,null);
        return (true,value.ToString()!.Trim());
    }

    private static async Task SincronizarCoberturaFaltanteInicioV8Async(
        int programaProduccionId,DateTime momento,DateTime? alterno,
        int? tecnicoId,int? smedId,int? auxiliarId,int usuarioId,
        SqlConnection cn,SqlTransaction tx)
    {
        if(!tecnicoId.HasValue && !smedId.HasValue && !auxiliarId.HasValue) return;

        const string programaSql=@"
SELECT TOP(1)
    FechaInicioProgramada,
    ISNULL(
        FechaFinProgramada,
        DATEADD(MINUTE,CONVERT(INT,CEILING(ISNULL(HorasProgramadas,1)*60)),
        FechaInicioProgramada)
    ) AS FechaFinProgramada
FROM dbo.Planeacion_ProgramaProduccion
WHERE ProgramaProduccionID=@ProgramaID AND Activo=1;";

        DateTime? inicioPrograma=null,finPrograma=null;
        await using(var cmd=new SqlCommand(programaSql,cn,tx))
        {
            cmd.Parameters.Add("@ProgramaID",SqlDbType.Int).Value=programaProduccionId;
            await using var rd=await cmd.ExecuteReaderAsync();
            if(!await rd.ReadAsync()) return;
            inicioPrograma=rd["FechaInicioProgramada"]==DBNull.Value
                ? null : Convert.ToDateTime(rd["FechaInicioProgramada"]);
            finPrograma=rd["FechaFinProgramada"]==DBNull.Value
                ? null : Convert.ToDateTime(rd["FechaFinProgramada"]);
        }

        if(!inicioPrograma.HasValue) return;

        var objetivo=momento;
        if(objetivo<inicioPrograma.Value ||
           (finPrograma.HasValue && objetivo>=finPrograma.Value))
            objetivo=alterno ?? inicioPrograma.Value;

        var turnos=await CargarTurnosResolverV2Async(cn,tx);
        var aplicable=turnos
            .Where(t=>TurnoCubreResolverV2(t,objetivo))
            .Select(t=>new { Turno=t,FechaTrabajo=FechaTrabajoResolverV2(t,objetivo) })
            .OrderBy(x=>string.Equals(x.Turno.TipoTurno,"Regular",
                StringComparison.OrdinalIgnoreCase)?0:1)
            .ThenBy(x=>x.Turno.Orden)
            .FirstOrDefault();

        if(aplicable==null) return;

        var semana=InicioSemanaResolverV2(aplicable.FechaTrabajo);

        const string sql=@"
DECLARE @ID INT=
(
    SELECT TOP(1) CoberturaTurnoID
    FROM dbo.Produccion_PersonalTurnoCobertura WITH(UPDLOCK,HOLDLOCK)
    WHERE SemanaInicio=@Semana AND TurnoID=@TurnoID AND Activo=1
    ORDER BY CoberturaTurnoID DESC
);

IF @ID IS NULL
BEGIN
    INSERT dbo.Produccion_PersonalTurnoCobertura
    (
        SemanaInicio,TurnoID,TecnicoProduccionID,SmedID,AuxiliarID,
        Fuente,UsuarioCreacionID,FechaCreacion,Activo
    )
    VALUES
    (
        @Semana,@TurnoID,@Tecnico,@Smed,@Auxiliar,
        N'PRODUCCION_INICIO',@Usuario,SYSDATETIME(),1
    );
END
ELSE
BEGIN
    UPDATE dbo.Produccion_PersonalTurnoCobertura
    SET TecnicoProduccionID=CASE WHEN TecnicoProduccionID IS NULL THEN @Tecnico ELSE TecnicoProduccionID END,
        SmedID=CASE WHEN SmedID IS NULL THEN @Smed ELSE SmedID END,
        AuxiliarID=CASE WHEN AuxiliarID IS NULL THEN @Auxiliar ELSE AuxiliarID END,
        Fuente=CASE WHEN
            (TecnicoProduccionID IS NULL AND @Tecnico IS NOT NULL)
            OR (SmedID IS NULL AND @Smed IS NOT NULL)
            OR (AuxiliarID IS NULL AND @Auxiliar IS NOT NULL)
            THEN N'PRODUCCION_INICIO' ELSE Fuente END,
        UsuarioModificacionID=@Usuario,
        FechaModificacion=SYSDATETIME()
    WHERE CoberturaTurnoID=@ID;
END;";

        await using var cmdUp=new SqlCommand(sql,cn,tx);
        cmdUp.Parameters.Add("@Semana",SqlDbType.Date).Value=semana;
        cmdUp.Parameters.Add("@TurnoID",SqlDbType.Int).Value=aplicable.Turno.TurnoID;
        cmdUp.Parameters.Add("@Tecnico",SqlDbType.Int).Value=(object?)tecnicoId??DBNull.Value;
        cmdUp.Parameters.Add("@Smed",SqlDbType.Int).Value=(object?)smedId??DBNull.Value;
        cmdUp.Parameters.Add("@Auxiliar",SqlDbType.Int).Value=(object?)auxiliarId??DBNull.Value;
        cmdUp.Parameters.Add("@Usuario",SqlDbType.Int).Value=usuarioId;
        await cmdUp.ExecuteNonQueryAsync();
    }
}