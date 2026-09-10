using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
namespace ERP.NSQuell.Controllers;
public sealed partial class ProduccionController
{
    [HttpGet]
    public async Task<IActionResult> CatalogoPersonalInicioV9()
    {
        if (!UsuarioEnSesion()) return Unauthorized();
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync();
        const string sql = @"
;WITH R AS
(
 SELECT p.PersonaID,N'OPERADOR' Rol FROM dbo.Persona p WHERE ISNULL(p.EsColaboradorActivo,1)=1 AND UPPER(ISNULL(p.Puesto,N'')) COLLATE Modern_Spanish_CI_AI LIKE N'%OPERADOR%'
 UNION SELECT p.PersonaID,N'AUXILIAR' FROM dbo.Persona p WHERE ISNULL(p.EsColaboradorActivo,1)=1 AND (UPPER(ISNULL(p.Puesto,N'')) COLLATE Modern_Spanish_CI_AI LIKE N'%AUXILIAR%' OR EXISTS(SELECT 1 FROM dbo.Produccion_PersonalRolesPermitidos x WHERE x.PersonaID=p.PersonaID AND x.TipoRol=N'AUXILIAR' AND x.Activo=1))
 UNION SELECT p.PersonaID,N'TECNICO' FROM dbo.Persona p WHERE ISNULL(p.EsColaboradorActivo,1)=1 AND (UPPER(ISNULL(p.Puesto,N'')) COLLATE Modern_Spanish_CI_AI LIKE N'%TECN%' OR EXISTS(SELECT 1 FROM dbo.Produccion_PersonalRolesPermitidos x WHERE x.PersonaID=p.PersonaID AND x.TipoRol=N'TECNICO' AND x.Activo=1))
 UNION SELECT p.PersonaID,N'SMED' FROM dbo.Persona p WHERE ISNULL(p.EsColaboradorActivo,1)=1 AND (UPPER(ISNULL(p.Puesto,N'')) COLLATE Modern_Spanish_CI_AI LIKE N'%SMED%' OR EXISTS(SELECT 1 FROM dbo.Produccion_PersonalRolesPermitidos x WHERE x.PersonaID=p.PersonaID AND x.TipoRol=N'SMED' AND x.Activo=1))
 UNION SELECT p.PersonaID,v.Rol FROM dbo.Persona p CROSS JOIN(VALUES(N'OPERADOR'),(N'AUXILIAR'),(N'TECNICO'),(N'SMED'))v(Rol) WHERE ISNULL(p.EsColaboradorActivo,1)=1 AND UPPER(ISNULL(p.Puesto,N'')) COLLATE Modern_Spanish_CI_AI LIKE N'%ENCARGADO%PRODUC%'
)
SELECT DISTINCT R.Rol,p.PersonaID,ISNULL(p.NumeroControl,N'') NumeroControl,LTRIM(RTRIM(CONCAT(ISNULL(p.Nombre,N''),N' ',ISNULL(p.ApellidoPaterno,N''),N' ',ISNULL(p.ApellidoMaterno,N'')))) Nombre,ISNULL(p.Puesto,N'') Puesto
FROM R INNER JOIN dbo.Persona p ON p.PersonaID=R.PersonaID
ORDER BY R.Rol,Nombre;";
        var operadores=new List<object>();var auxiliares=new List<object>();var tecnicos=new List<object>();var smed=new List<object>();
        await using var cmd=new SqlCommand(sql,cn);await using var rd=await cmd.ExecuteReaderAsync();
        while(await rd.ReadAsync()){
            var item=new{personaID=Convert.ToInt32(rd["PersonaID"]),numeroControl=rd["NumeroControl"]?.ToString()?.Trim()??"",nombre=rd["Nombre"]?.ToString()?.Trim()??"",puesto=rd["Puesto"]?.ToString()?.Trim()??""};
            switch((rd["Rol"]?.ToString()??"").Trim().ToUpperInvariant()){case "OPERADOR":operadores.Add(item);break;case "AUXILIAR":auxiliares.Add(item);break;case "TECNICO":tecnicos.Add(item);break;case "SMED":smed.Add(item);break;}
        }
        return Json(new{ok=true,operadores,auxiliares,tecnicos,smed});
    }
}