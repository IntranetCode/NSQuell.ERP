using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;
using EscalaVM = ERP.NSQuell.Models.EscalaPersonal;

namespace ERP.NSQuell.Controllers;

public partial class EscalaPersonalController
{
    // NSQ_RRHH_ESCALA_OPERADORES_RAPIDA_V1_1
    // RRHH programa OPERADORES por semana. La fuente de operadores es la
    // misma matriz de Polivalencia que consume ProduccionPersonal.
    // MaquinaID es preferencia, NO candado.

    public sealed class OperadorSemanaRapidaGuardarFilaV11
    {
        public int PersonalID { get; set; }
        public int? EscalaTurnoID { get; set; }
        public int? MaquinaID { get; set; }
    }

    public sealed class OperadoresSemanaRapidaGuardarV11
    {
        public int EscalaID { get; set; }
        public List<OperadorSemanaRapidaGuardarFilaV11> Operadores { get; set; } = new();
    }

    private sealed class EscalaRapidaV11
    {
        public int EscalaID { get; set; }
        public string Folio { get; set; } = string.Empty;
        public string Estado { get; set; } = string.Empty;
        public int Anio { get; set; }
        public int NumeroSemana { get; set; }
        public DateTime FechaInicio { get; set; }
        public DateTime FechaFin { get; set; }
    }

    private sealed class TurnoRapidoV11
    {
        public int EscalaTurnoID { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public TimeSpan? HoraInicio { get; set; }
        public TimeSpan? HoraFin { get; set; }
        public bool CruzaDiaSiguiente { get; set; }
        public bool EsFlexible { get; set; }
        public string Color { get; set; } = "#64748B";
        public int Orden { get; set; }

        public string HorarioTexto =>
            EsFlexible || !HoraInicio.HasValue || !HoraFin.HasValue
                ? Nombre
                : $"{HoraInicio.Value:hh\\:mm} - {HoraFin.Value:hh\\:mm}" +
                  (CruzaDiaSiguiente ? " (+1)" : string.Empty);
    }

    private sealed class OperadorRapidoV11
    {
        public int PersonaID { get; set; }
        public string NumeroControl { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public int Piezas { get; set; }
    }

    private sealed class AsignacionRapidaV11
    {
        public int AsignacionID { get; set; }
        public int PersonalID { get; set; }
        public int EscalaTurnoID { get; set; }
        public int? MaquinaID { get; set; }
        public DateTime FechaInicio { get; set; }
        public DateTime FechaFin { get; set; }
        public string TurnoNombre { get; set; } = string.Empty;
    }

    private static string ClaveTurnoRapidaV11(string? value) =>
        (value ?? string.Empty).Trim().ToUpperInvariant();

    [HttpGet]
    public async Task<IActionResult> OperadoresSemanaRapidaDatosV11(int escalaId)
    {
        if (escalaId <= 0)
            return BadRequest(new { ok = false, mensaje = "Escala no valida." });

        await using var cn = new SqlConnection(_connectionString);
        await cn.OpenAsync();

        EscalaRapidaV11? escala = null;

        const string sqlEscala = @"
SELECT TOP(1)
    EscalaID,Folio,Estado,Anio,NumeroSemana,FechaInicio,FechaFin
FROM dbo.RRHH_EscalasPersonal
WHERE EscalaID=@EscalaID
  AND Activo=1;";

        await using (var cmd = new SqlCommand(sqlEscala,cn))
        {
            cmd.Parameters.Add("@EscalaID",SqlDbType.Int).Value=escalaId;
            await using var rd = await cmd.ExecuteReaderAsync();
            if (await rd.ReadAsync())
            {
                escala = new EscalaRapidaV11
                {
                    EscalaID=Convert.ToInt32(rd["EscalaID"]),
                    Folio=rd["Folio"]?.ToString()?.Trim() ?? string.Empty,
                    Estado=rd["Estado"]?.ToString()?.Trim() ?? string.Empty,
                    Anio=Convert.ToInt32(rd["Anio"]),
                    NumeroSemana=Convert.ToInt32(rd["NumeroSemana"]),
                    FechaInicio=Convert.ToDateTime(rd["FechaInicio"]).Date,
                    FechaFin=Convert.ToDateTime(rd["FechaFin"]).Date
                };
            }
        }

        if (escala == null)
            return NotFound(new { ok = false, mensaje = "La escala ya no existe." });

        const string sqlConfig = @"
SELECT CONVERT(bit,CASE WHEN
       OBJECT_ID(N'dbo.RRHH_PolivalenciaCompetencias',N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.RRHH_EscalaAsignaciones',N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.RRHH_EscalaTurnos',N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.RRHH_FuncionesPersonal',N'U') IS NOT NULL
THEN 1 ELSE 0 END);";

        await using (var cmd = new SqlCommand(sqlConfig,cn))
        {
            var configurado=Convert.ToBoolean(await cmd.ExecuteScalarAsync() ?? false);
            if (!configurado)
            {
                return StatusCode(500,new
                {
                    ok=false,
                    mensaje="Falta estructura de Escala/Polivalencia requerida para programar operadores."
                });
            }
        }

        var turnos = new List<TurnoRapidoV11>();

        const string sqlTurnos = @"
SELECT
    EscalaTurnoID,Nombre,HoraInicio,HoraFin,CruzaDiaSiguiente,
    EsFlexible,Color,Orden
FROM dbo.RRHH_EscalaTurnos
WHERE EscalaID=@EscalaID
  AND Activo=1
ORDER BY Orden,Nombre,EscalaTurnoID;";

        await using (var cmd = new SqlCommand(sqlTurnos,cn))
        {
            cmd.Parameters.Add("@EscalaID",SqlDbType.Int).Value=escalaId;
            await using var rd=await cmd.ExecuteReaderAsync();
            while(await rd.ReadAsync())
            {
                turnos.Add(new TurnoRapidoV11
                {
                    EscalaTurnoID=Convert.ToInt32(rd["EscalaTurnoID"]),
                    Nombre=rd["Nombre"]?.ToString()?.Trim() ?? string.Empty,
                    HoraInicio=rd["HoraInicio"]==DBNull.Value ? null : (TimeSpan?)rd["HoraInicio"],
                    HoraFin=rd["HoraFin"]==DBNull.Value ? null : (TimeSpan?)rd["HoraFin"],
                    CruzaDiaSiguiente=Convert.ToBoolean(rd["CruzaDiaSiguiente"]),
                    EsFlexible=Convert.ToBoolean(rd["EsFlexible"]),
                    Color=rd["Color"]?.ToString()?.Trim() ?? "#64748B",
                    Orden=Convert.ToInt32(rd["Orden"])
                });
            }
        }

        var operadores = new List<OperadorRapidoV11>();

        const string sqlOperadores = @"
SELECT
    p.PersonaID,
    ISNULL(p.NumeroControl,N'') AS NumeroControl,
    LTRIM(RTRIM(CONCAT(
        ISNULL(p.Nombre,N''),N' ',
        ISNULL(p.ApellidoPaterno,N''),N' ',
        ISNULL(p.ApellidoMaterno,N'')))) AS Nombre,
    COUNT(DISTINCT CASE WHEN c.ParteID IS NOT NULL THEN c.ParteID END) AS Piezas
FROM dbo.RRHH_PolivalenciaCompetencias c
INNER JOIN dbo.Persona p
    ON p.PersonaID=c.PersonalID
   AND ISNULL(p.EsColaboradorActivo,1)=1
WHERE c.Activo=1
  AND UPPER(LTRIM(RTRIM(ISNULL(c.PuestoMatriz,N''))))=N'OPERADOR'
GROUP BY
    p.PersonaID,p.NumeroControl,p.Nombre,p.ApellidoPaterno,p.ApellidoMaterno
ORDER BY Nombre,p.PersonaID;";

        await using (var cmd = new SqlCommand(sqlOperadores,cn))
        await using (var rd=await cmd.ExecuteReaderAsync())
        {
            while(await rd.ReadAsync())
            {
                operadores.Add(new OperadorRapidoV11
                {
                    PersonaID=Convert.ToInt32(rd["PersonaID"]),
                    NumeroControl=rd["NumeroControl"]?.ToString()?.Trim() ?? string.Empty,
                    Nombre=rd["Nombre"]?.ToString()?.Trim() ?? string.Empty,
                    Piezas=Convert.ToInt32(rd["Piezas"])
                });
            }
        }

        var maquinas = new List<object>();

        const string sqlMaquinas = @"
SELECT MaquinaID,Codigo,Nombre
FROM dbo.ERP_Maquinas
WHERE Activo=1
ORDER BY Codigo,Nombre,MaquinaID;";

        await using (var cmd = new SqlCommand(sqlMaquinas,cn))
        await using (var rd=await cmd.ExecuteReaderAsync())
        {
            while(await rd.ReadAsync())
            {
                var codigo=rd["Codigo"]?.ToString()?.Trim() ?? string.Empty;
                var nombre=rd["Nombre"]?.ToString()?.Trim() ?? string.Empty;
                maquinas.Add(new
                {
                    id=Convert.ToInt32(rd["MaquinaID"]),
                    codigo,
                    nombre,
                    texto=string.IsNullOrWhiteSpace(codigo)
                        ? nombre
                        : string.Equals(codigo,nombre,StringComparison.OrdinalIgnoreCase)
                            ? codigo
                            : $"{codigo} - {nombre}"
                });
            }
        }

        var asignaciones = new List<AsignacionRapidaV11>();

        const string sqlAsignaciones = @"
SELECT
    a.AsignacionID,a.PersonalID,a.EscalaTurnoID,a.MaquinaID,
    a.FechaInicio,a.FechaFin,et.Nombre AS TurnoNombre
FROM dbo.RRHH_EscalaAsignaciones a
INNER JOIN dbo.RRHH_FuncionesPersonal f
    ON f.FuncionID=a.FuncionID
   AND f.Activo=1
INNER JOIN dbo.RRHH_EscalaTurnos et
    ON et.EscalaTurnoID=a.EscalaTurnoID
   AND et.EscalaID=a.EscalaID
   AND et.Activo=1
WHERE a.EscalaID=@EscalaID
  AND a.Activo=1
  AND UPPER(LTRIM(RTRIM(f.Nombre)))=N'OPERADOR'
ORDER BY a.PersonalID,a.FechaInicio,a.AsignacionID;";

        await using (var cmd = new SqlCommand(sqlAsignaciones,cn))
        {
            cmd.Parameters.Add("@EscalaID",SqlDbType.Int).Value=escalaId;
            await using var rd=await cmd.ExecuteReaderAsync();
            while(await rd.ReadAsync())
            {
                asignaciones.Add(new AsignacionRapidaV11
                {
                    AsignacionID=Convert.ToInt32(rd["AsignacionID"]),
                    PersonalID=Convert.ToInt32(rd["PersonalID"]),
                    EscalaTurnoID=Convert.ToInt32(rd["EscalaTurnoID"]),
                    MaquinaID=rd["MaquinaID"]==DBNull.Value ? null : Convert.ToInt32(rd["MaquinaID"]),
                    FechaInicio=Convert.ToDateTime(rd["FechaInicio"]).Date,
                    FechaFin=Convert.ToDateTime(rd["FechaFin"]).Date,
                    TurnoNombre=rd["TurnoNombre"]?.ToString()?.Trim() ?? string.Empty
                });
            }
        }

        var filas = operadores.Select(op =>
        {
            var existentes=asignaciones
                .Where(x=>x.PersonalID==op.PersonaID)
                .OrderBy(x=>x.FechaInicio)
                .ThenBy(x=>x.AsignacionID)
                .ToList();

            var simple=
                existentes.Count==1 &&
                existentes[0].FechaInicio.Date==escala.FechaInicio.Date &&
                existentes[0].FechaFin.Date==escala.FechaFin.Date;

            var actual=simple ? existentes[0] : null;

            return new
            {
                personalId=op.PersonaID,
                numeroControl=op.NumeroControl,
                nombre=op.Nombre,
                piezas=op.Piezas,
                asignado=existentes.Count>0,
                configuracionEspecial=existentes.Count>0 && !simple,
                escalaTurnoId=actual?.EscalaTurnoID,
                maquinaId=actual?.MaquinaID,
                turnoNombre=actual?.TurnoNombre ?? string.Empty,
                asignacionesActivas=existentes.Count
            };
        }).ToList();

        int? escalaAnteriorId=null;
        string escalaAnteriorFolio=string.Empty;

        const string sqlAnterior = @"
SELECT TOP(1) EscalaID,Folio
FROM dbo.RRHH_EscalasPersonal
WHERE Activo=1
  AND EscalaID<>@EscalaID
  AND FechaInicio<@FechaInicio
  AND Estado IN(N'Publicada',N'Borrador')
ORDER BY FechaInicio DESC,
         CASE WHEN Estado=N'Publicada' THEN 0 ELSE 1 END,
         EscalaID DESC;";

        await using (var cmd=new SqlCommand(sqlAnterior,cn))
        {
            cmd.Parameters.Add("@EscalaID",SqlDbType.Int).Value=escalaId;
            cmd.Parameters.Add("@FechaInicio",SqlDbType.Date).Value=escala.FechaInicio;
            await using var rd=await cmd.ExecuteReaderAsync();
            if(await rd.ReadAsync())
            {
                escalaAnteriorId=Convert.ToInt32(rd["EscalaID"]);
                escalaAnteriorFolio=rd["Folio"]?.ToString()?.Trim() ?? string.Empty;
            }
        }

        var sugerencias = new List<object>();

        if(escalaAnteriorId.HasValue)
        {
            var turnosActualesPorNombre=turnos
                .GroupBy(x=>ClaveTurnoRapidaV11(x.Nombre))
                .ToDictionary(g=>g.Key,g=>g.First().EscalaTurnoID,StringComparer.OrdinalIgnoreCase);

            const string sqlPrev = @"
SELECT
    a.PersonalID,a.MaquinaID,et.Nombre AS TurnoNombre,
    a.FechaInicio,a.FechaFin
FROM dbo.RRHH_EscalaAsignaciones a
INNER JOIN dbo.RRHH_FuncionesPersonal f
    ON f.FuncionID=a.FuncionID
   AND f.Activo=1
INNER JOIN dbo.RRHH_EscalaTurnos et
    ON et.EscalaTurnoID=a.EscalaTurnoID
   AND et.EscalaID=a.EscalaID
   AND et.Activo=1
WHERE a.EscalaID=@EscalaID
  AND a.Activo=1
  AND UPPER(LTRIM(RTRIM(f.Nombre)))=N'OPERADOR'
ORDER BY a.PersonalID,a.AsignacionID;";

            var anteriores=new List<AsignacionRapidaV11>();

            await using(var cmd=new SqlCommand(sqlPrev,cn))
            {
                cmd.Parameters.Add("@EscalaID",SqlDbType.Int).Value=escalaAnteriorId.Value;
                await using var rd=await cmd.ExecuteReaderAsync();
                while(await rd.ReadAsync())
                {
                    anteriores.Add(new AsignacionRapidaV11
                    {
                        PersonalID=Convert.ToInt32(rd["PersonalID"]),
                        MaquinaID=rd["MaquinaID"]==DBNull.Value ? null : Convert.ToInt32(rd["MaquinaID"]),
                        TurnoNombre=rd["TurnoNombre"]?.ToString()?.Trim() ?? string.Empty,
                        FechaInicio=Convert.ToDateTime(rd["FechaInicio"]).Date,
                        FechaFin=Convert.ToDateTime(rd["FechaFin"]).Date
                    });
                }
            }

            foreach(var g in anteriores.GroupBy(x=>x.PersonalID))
            {
                if(g.Count()!=1) continue;
                var a=g.First();
                if(!turnosActualesPorNombre.TryGetValue(ClaveTurnoRapidaV11(a.TurnoNombre),out var turnoActualId))
                    continue;

                sugerencias.Add(new
                {
                    personalId=g.Key,
                    escalaTurnoId=turnoActualId,
                    maquinaId=a.MaquinaID
                });
            }
        }

        return Json(new
        {
            ok=true,
            escala=new
            {
                id=escala.EscalaID,
                folio=escala.Folio,
                estado=escala.Estado,
                anio=escala.Anio,
                semana=escala.NumeroSemana,
                inicio=escala.FechaInicio.ToString("yyyy-MM-dd"),
                fin=escala.FechaFin.ToString("yyyy-MM-dd"),
                editable=string.Equals(
                    escala.Estado,
                    EscalaVM.Estados.Borrador,
                    StringComparison.OrdinalIgnoreCase)
            },
            turnos=turnos.Select(x=>new
            {
                id=x.EscalaTurnoID,
                nombre=x.Nombre,
                horario=x.HorarioTexto,
                color=x.Color,
                orden=x.Orden
            }),
            maquinas,
            operadores=filas,
            semanaAnterior=new
            {
                existe=escalaAnteriorId.HasValue,
                escalaId=escalaAnteriorId,
                folio=escalaAnteriorFolio,
                sugerencias
            }
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GuardarOperadoresSemanaRapidaV11(
        OperadoresSemanaRapidaGuardarV11 modelo)
    {
        if(modelo.EscalaID<=0)
            return BadRequest(new { ok=false,mensaje="Escala no valida." });

        var usuarioId=ObtenerUsuarioID();
        if(!usuarioId.HasValue)
            return Unauthorized(new { ok=false,mensaje="La sesion no tiene un UsuarioID valido." });

        modelo.Operadores ??= new();

        var repetidos=modelo.Operadores
            .Where(x=>x.PersonalID>0)
            .GroupBy(x=>x.PersonalID)
            .Where(g=>g.Count()>1)
            .Select(g=>g.Key)
            .ToList();

        if(repetidos.Count>0)
            return BadRequest(new
            {
                ok=false,
                mensaje="Hay operadores repetidos en el envio: "+string.Join(", ",repetidos)
            });

        await using var cn=new SqlConnection(_connectionString);
        await cn.OpenAsync();
        await using var tx=(SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);

        try
        {
            EscalaRapidaV11? escala=null;

            const string sqlEscala = @"
SELECT TOP(1)
    EscalaID,Folio,Estado,Anio,NumeroSemana,FechaInicio,FechaFin
FROM dbo.RRHH_EscalasPersonal WITH(UPDLOCK,HOLDLOCK)
WHERE EscalaID=@EscalaID
  AND Activo=1;";

            await using(var cmd=new SqlCommand(sqlEscala,cn,tx))
            {
                cmd.Parameters.Add("@EscalaID",SqlDbType.Int).Value=modelo.EscalaID;
                await using var rd=await cmd.ExecuteReaderAsync();
                if(await rd.ReadAsync())
                {
                    escala=new EscalaRapidaV11
                    {
                        EscalaID=Convert.ToInt32(rd["EscalaID"]),
                        Folio=rd["Folio"]?.ToString()?.Trim() ?? string.Empty,
                        Estado=rd["Estado"]?.ToString()?.Trim() ?? string.Empty,
                        Anio=Convert.ToInt32(rd["Anio"]),
                        NumeroSemana=Convert.ToInt32(rd["NumeroSemana"]),
                        FechaInicio=Convert.ToDateTime(rd["FechaInicio"]).Date,
                        FechaFin=Convert.ToDateTime(rd["FechaFin"]).Date
                    };
                }
            }

            if(escala==null)
                throw new InvalidOperationException("La escala ya no existe.");

            if(!string.Equals(
                escala.Estado,
                EscalaVM.Estados.Borrador,
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "La programacion rapida solo puede modificarse mientras la escala este en Borrador.");
            }

            int funcionOperadorId;

            const string sqlFuncion = @"
SELECT TOP(1) FuncionID
FROM dbo.RRHH_FuncionesPersonal WITH(UPDLOCK,HOLDLOCK)
WHERE Activo=1
  AND UPPER(LTRIM(RTRIM(Nombre)))=N'OPERADOR'
ORDER BY Orden,FuncionID;";

            await using(var cmd=new SqlCommand(sqlFuncion,cn,tx))
            {
                var value=await cmd.ExecuteScalarAsync();
                if(value==null || value==DBNull.Value)
                    throw new InvalidOperationException(
                        "No existe una funcion activa llamada OPERADOR en RRHH_FuncionesPersonal.");
                funcionOperadorId=Convert.ToInt32(value);
            }

            var oficiales=new HashSet<int>();

            const string sqlOficiales = @"
SELECT DISTINCT p.PersonaID
FROM dbo.RRHH_PolivalenciaCompetencias c
INNER JOIN dbo.Persona p
    ON p.PersonaID=c.PersonalID
   AND ISNULL(p.EsColaboradorActivo,1)=1
WHERE c.Activo=1
  AND UPPER(LTRIM(RTRIM(ISNULL(c.PuestoMatriz,N''))))=N'OPERADOR';";

            await using(var cmd=new SqlCommand(sqlOficiales,cn,tx))
            await using(var rd=await cmd.ExecuteReaderAsync())
            {
                while(await rd.ReadAsync())
                    oficiales.Add(Convert.ToInt32(rd["PersonaID"]));
            }

            var turnosValidos=new HashSet<int>();

            const string sqlTurnos = @"
SELECT EscalaTurnoID
FROM dbo.RRHH_EscalaTurnos
WHERE EscalaID=@EscalaID
  AND Activo=1;";

            await using(var cmd=new SqlCommand(sqlTurnos,cn,tx))
            {
                cmd.Parameters.Add("@EscalaID",SqlDbType.Int).Value=modelo.EscalaID;
                await using var rd=await cmd.ExecuteReaderAsync();
                while(await rd.ReadAsync())
                    turnosValidos.Add(Convert.ToInt32(rd["EscalaTurnoID"]));
            }

            var maquinasValidas=new HashSet<int>();

            const string sqlMaquinas = @"
SELECT MaquinaID
FROM dbo.ERP_Maquinas
WHERE Activo=1;";

            await using(var cmd=new SqlCommand(sqlMaquinas,cn,tx))
            await using(var rd=await cmd.ExecuteReaderAsync())
            {
                while(await rd.ReadAsync())
                    maquinasValidas.Add(Convert.ToInt32(rd["MaquinaID"]));
            }

            var procesadas=0;
            var asignadas=0;
            var sinHorario=0;

            foreach(var item in modelo.Operadores)
            {
                if(item.PersonalID<=0)
                    throw new InvalidOperationException("Se recibio un PersonalID invalido.");

                if(!oficiales.Contains(item.PersonalID))
                    throw new InvalidOperationException(
                        $"PersonaID {item.PersonalID} ya no pertenece a la matriz vigente de OPERADORES.");

                if(item.EscalaTurnoID.HasValue &&
                   !turnosValidos.Contains(item.EscalaTurnoID.Value))
                {
                    throw new InvalidOperationException(
                        $"El horario seleccionado para PersonaID {item.PersonalID} no pertenece a esta escala.");
                }

                if(item.MaquinaID.HasValue &&
                   !maquinasValidas.Contains(item.MaquinaID.Value))
                {
                    throw new InvalidOperationException(
                        $"La maquina seleccionada para PersonaID {item.PersonalID} ya no esta activa.");
                }

                const string sqlDesactivar = @"
UPDATE a
SET a.Activo=0,
    a.FechaModificacion=SYSDATETIME(),
    a.ActualizadoPor=@UsuarioID
FROM dbo.RRHH_EscalaAsignaciones a
INNER JOIN dbo.RRHH_FuncionesPersonal f
    ON f.FuncionID=a.FuncionID
WHERE a.EscalaID=@EscalaID
  AND a.PersonalID=@PersonalID
  AND a.Activo=1
  AND UPPER(LTRIM(RTRIM(f.Nombre)))=N'OPERADOR';";

                await using(var cmd=new SqlCommand(sqlDesactivar,cn,tx))
                {
                    cmd.Parameters.Add("@UsuarioID",SqlDbType.Int).Value=usuarioId.Value;
                    cmd.Parameters.Add("@EscalaID",SqlDbType.Int).Value=modelo.EscalaID;
                    cmd.Parameters.Add("@PersonalID",SqlDbType.Int).Value=item.PersonalID;
                    await cmd.ExecuteNonQueryAsync();
                }

                procesadas++;

                if(!item.EscalaTurnoID.HasValue)
                {
                    sinHorario++;
                    continue;
                }

                const string sqlInsertar = @"
INSERT INTO dbo.RRHH_EscalaAsignaciones
(
    EscalaID,PersonalID,DepartamentoID,FuncionID,MaquinaID,
    EscalaTurnoID,FechaInicio,FechaFin,Observaciones,
    Activo,CreadoPor
)
VALUES
(
    @EscalaID,@PersonalID,NULL,@FuncionID,@MaquinaID,
    @EscalaTurnoID,@FechaInicio,@FechaFin,
    N'Programacion semanal rapida de operadores por RRHH.',
    1,@UsuarioID
);";

                await using(var cmd=new SqlCommand(sqlInsertar,cn,tx))
                {
                    cmd.Parameters.Add("@EscalaID",SqlDbType.Int).Value=modelo.EscalaID;
                    cmd.Parameters.Add("@PersonalID",SqlDbType.Int).Value=item.PersonalID;
                    cmd.Parameters.Add("@FuncionID",SqlDbType.Int).Value=funcionOperadorId;
                    cmd.Parameters.Add("@MaquinaID",SqlDbType.Int).Value=
                        item.MaquinaID.HasValue ? (object)item.MaquinaID.Value : DBNull.Value;
                    cmd.Parameters.Add("@EscalaTurnoID",SqlDbType.Int).Value=item.EscalaTurnoID.Value;
                    cmd.Parameters.Add("@FechaInicio",SqlDbType.Date).Value=escala.FechaInicio;
                    cmd.Parameters.Add("@FechaFin",SqlDbType.Date).Value=escala.FechaFin;
                    cmd.Parameters.Add("@UsuarioID",SqlDbType.Int).Value=usuarioId.Value;
                    await cmd.ExecuteNonQueryAsync();
                }

                asignadas++;
            }

            if(procesadas>0)
            {
                await InsertarHistorialAsync(
                    escala.EscalaID,
                    escala.Estado,
                    escala.Estado,
                    $"Programacion rapida RH: {procesadas} operador(es) actualizados; {asignadas} con horario; {sinHorario} sin horario.",
                    usuarioId.Value,
                    cn,
                    tx);
            }

            await tx.CommitAsync();

            return Json(new
            {
                ok=true,
                mensaje=procesadas==0
                    ? "No habia cambios pendientes por guardar."
                    : $"Programacion semanal guardada. {asignadas} operador(es) con horario; {sinHorario} sin horario.",
                procesadas,
                asignadas,
                sinHorario
            });
        }
        catch(Exception ex)
        {
            try { await tx.RollbackAsync(); } catch { }
            return BadRequest(new
            {
                ok=false,
                mensaje="No fue posible guardar la programacion semanal de operadores: "+ex.Message
            });
        }
    }
}