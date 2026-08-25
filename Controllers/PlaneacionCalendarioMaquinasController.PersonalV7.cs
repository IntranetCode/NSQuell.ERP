using ERP.NSQuell.Models;
using Microsoft.Data.SqlClient;
using System.Data;

namespace ERP.NSQuell.Controllers;

public sealed partial class PlaneacionCalendarioMaquinasController
{
    private sealed class PersonalCalendarioV7
    {
        public int ProgramaID { get; set; }
        public int? OperadorID { get; set; }
        public string Operador { get; set; } = string.Empty;
        public string Turno { get; set; } = string.Empty;
        public DateTime Inicio { get; set; }
    }

    private static async Task AplicarPersonalV7CalendarioAsync(List<PlaneacionCalendarioMaquinaVm> maquinas,DateTime desde,DateTime hasta,SqlConnection cn)
    {
        if(maquinas.Count==0)return;
        const string existe="SELECT CONVERT(bit,CASE WHEN OBJECT_ID(N'dbo.Produccion_ProgramaPersonalAsignaciones',N'U') IS NULL THEN 0 ELSE 1 END);";
        await using(var e=new SqlCommand(existe,cn))if(!Convert.ToBoolean(await e.ExecuteScalarAsync()??false))return;
        const string sql=@"
SELECT a.ProgramaProduccionID,a.OperadorID,ISNULL(a.TurnoNombre,N'') TurnoNombre,a.Inicio,
 LTRIM(RTRIM(CONCAT(ISNULL(p.Nombre,N''),N' ',ISNULL(p.ApellidoPaterno,N''),N' ',ISNULL(p.ApellidoMaterno,N'')))) Operador
FROM dbo.Produccion_ProgramaPersonalAsignaciones a
LEFT JOIN dbo.Persona p ON p.PersonaID=a.OperadorID
WHERE a.Activo=1 AND a.Inicio<@Hasta AND a.Fin>@Desde
ORDER BY a.ProgramaProduccionID,a.Inicio,a.AsignacionPersonalID;";
        var lista=new List<PersonalCalendarioV7>();await using(var cmd=new SqlCommand(sql,cn)){cmd.Parameters.Add("@Desde",SqlDbType.DateTime2).Value=desde;cmd.Parameters.Add("@Hasta",SqlDbType.DateTime2).Value=hasta;await using var rd=await cmd.ExecuteReaderAsync();while(await rd.ReadAsync())lista.Add(new PersonalCalendarioV7{ProgramaID=Convert.ToInt32(rd["ProgramaProduccionID"]),OperadorID=rd["OperadorID"]==DBNull.Value?null:Convert.ToInt32(rd["OperadorID"]),Operador=rd["Operador"]?.ToString()?.Trim()??string.Empty,Turno=rd["TurnoNombre"]?.ToString()?.Trim()??string.Empty,Inicio=Convert.ToDateTime(rd["Inicio"])});}
        foreach(var b in maquinas.SelectMany(x=>x.Bloques))
        {
            var rows=lista.Where(x=>x.ProgramaID==b.ProgramaProduccionID).OrderBy(x=>x.Inicio).ToList();
            if(rows.Count==0)continue;
            var asignados=rows.Where(x=>x.OperadorID.HasValue).ToList();
            b.OperadorProgramadoID=asignados.FirstOrDefault()?.OperadorID;
            b.OperadorProgramadoNombre=string.Join(" / ",rows.Select(x=>$"{x.Turno}: {(x.OperadorID.HasValue ? x.Operador : "SIN ASIGNAR")}").Distinct());
            b.TurnoProgramadoNombre=string.Join(" / ",rows.Select(x=>x.Turno).Distinct());
        }
    }
}
