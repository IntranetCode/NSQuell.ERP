using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Globalization;

namespace ERP.NSQuell.Controllers;

public sealed partial class ProduccionPreparacionController
{
    // Un recibo por entrega, incluso si varios programas LH/RH comparten la OF.
    private async Task<int?> ProgramaParejaPurgaAsync(int programaId,SqlConnection cn)
    {
        var relaciones=await CargarRelacionesMaterialesLhRhAsync(cn);
        return relaciones.TryGetValue(programaId,out var relacion)?relacion.ProgramaParejaID:null;
    }

    private async Task<List<object>> CargarPurgasOperativasAsync(int programaId,SqlConnection cn)
    {
        var filas=new List<object>();
        var pareja=await ProgramaParejaPurgaAsync(programaId,cn);
        const string sql="""
SELECT p.PurgaID,p.CantidadKg,p.FechaEntrega,p.CantidadRecibidaProduccion,p.FechaRecepcionProduccion,p.MotivoRecepcion,
 m.TipoMP,m.Lote,CONCAT(mat.Codigo,N' · ',mat.Nombre) Material
FROM dbo.NSQ_AlmacenMP_Purgas p
INNER JOIN dbo.AlmacenMP_Movimientos m ON m.MovimientoID=p.MovimientoID AND m.Activo=1
INNER JOIN dbo.ERP_Materiales mat ON mat.MaterialID=m.MaterialID
WHERE p.Activo=1 AND EXISTS(SELECT 1 FROM dbo.Planeacion_ProgramaProduccion pp
 WHERE (pp.ProgramaProduccionID=@ProgramaID OR pp.ProgramaProduccionID=@ParejaID) AND pp.Activo=1
 AND pp.SolicitudProduccionID=p.SolicitudProduccionID)
ORDER BY CASE WHEN p.FechaRecepcionProduccion IS NULL THEN 0 ELSE 1 END,p.FechaEntrega DESC,p.PurgaID DESC;
""";
        await using var cmd=new SqlCommand(sql,cn);
        cmd.Parameters.Add("@ProgramaID",SqlDbType.Int).Value=programaId;
        cmd.Parameters.Add("@ParejaID",SqlDbType.Int).Value=(object?)pareja??DBNull.Value;
        await using var rd=await cmd.ExecuteReaderAsync();
        while(await rd.ReadAsync())filas.Add(new {
            purgaId=Convert.ToInt32(rd["PurgaID"]),cantidadKg=Convert.ToDecimal(rd["CantidadKg"]),
            fechaEntrega=Convert.ToDateTime(rd["FechaEntrega"]),material=Convert.ToString(rd["Material"]),
            tipoMP=Convert.ToString(rd["TipoMP"]),lote=Convert.ToString(rd["Lote"]),
            cantidadRecibida=rd["CantidadRecibidaProduccion"] is DBNull?(decimal?)null:Convert.ToDecimal(rd["CantidadRecibidaProduccion"]),
            fechaRecepcion=rd["FechaRecepcionProduccion"] is DBNull?(DateTime?)null:Convert.ToDateTime(rd["FechaRecepcionProduccion"]),
            motivo=Convert.ToString(rd["MotivoRecepcion"])
        });
        return filas;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmarPurgaOperativa(int purgaID,int programaProduccionID,string? cantidadRecibida,string? motivo)
    {
        if(!UsuarioEnSesion())return Unauthorized(new{ok=false,mensaje="La sesión terminó."});
        if(purgaID<=0||programaProduccionID<=0||!decimal.TryParse(cantidadRecibida,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var kg)
            ||kg<0||decimal.Round(kg,3)!=kg)return BadRequest(new{ok=false,mensaje="Escribe KG válidos con máximo tres decimales."});
        motivo=(motivo??"").Trim();
        if(motivo.Length>500)return BadRequest(new{ok=false,mensaje="El motivo no puede superar 500 caracteres."});
        await using var cn=new SqlConnection(ConnectionString);await cn.OpenAsync();
        var usuario=ObtenerUsuarioID();var permisos=await ObtenerPermisosPreparacionUsuarioAsync(usuario,cn);
        if(!permisos.PuedeGestionarEmbalaje)return StatusCode(403,new{ok=false,mensaje="No tienes permiso para recibir materiales."});
        var pareja=await ProgramaParejaPurgaAsync(programaProduccionID,cn);
        await using var tx=(SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            int movimiento;decimal entregado;decimal? previo;string motivoPrevio;
            const string consulta="""
SELECT p.MovimientoID,p.CantidadKg,p.CantidadRecibidaProduccion,p.MotivoRecepcion
FROM dbo.NSQ_AlmacenMP_Purgas p WITH(UPDLOCK,HOLDLOCK)
INNER JOIN dbo.AlmacenMP_Movimientos m ON m.MovimientoID=p.MovimientoID AND m.Activo=1
WHERE p.PurgaID=@PurgaID AND p.Activo=1 AND EXISTS(SELECT 1 FROM dbo.Planeacion_ProgramaProduccion pp
 WHERE (pp.ProgramaProduccionID=@ProgramaID OR pp.ProgramaProduccionID=@ParejaID) AND pp.Activo=1
 AND pp.SolicitudProduccionID=p.SolicitudProduccionID);
""";
            await using(var cmd=new SqlCommand(consulta,cn,tx))
            {
                cmd.Parameters.Add("@PurgaID",SqlDbType.Int).Value=purgaID;
                cmd.Parameters.Add("@ProgramaID",SqlDbType.Int).Value=programaProduccionID;
                cmd.Parameters.Add("@ParejaID",SqlDbType.Int).Value=(object?)pareja??DBNull.Value;
                await using var rd=await cmd.ExecuteReaderAsync();
                if(!await rd.ReadAsync()){await tx.RollbackAsync();return NotFound(new{ok=false,mensaje="La purga no pertenece a esta OF o ya no está activa."});}
                movimiento=Convert.ToInt32(rd[0]);entregado=Convert.ToDecimal(rd[1]);
                previo=rd[2] is DBNull?null:Convert.ToDecimal(rd[2]);motivoPrevio=Convert.ToString(rd[3])??"";
            }
            if(kg>entregado||(kg<entregado&&motivo.Length==0))
            {await tx.RollbackAsync();return BadRequest(new{ok=false,mensaje="No recibas más de lo entregado; justifica cualquier diferencia."});}
            if(previo.HasValue)
            {
                await tx.RollbackAsync();
                return previo==kg&&motivoPrevio==motivo?Json(new{ok=true,mensaje="La recepción ya estaba registrada."}):Conflict(new{ok=false,mensaje="Esta purga ya fue recibida. Actualiza la consulta."});
            }
            const string guardar="""
UPDATE dbo.NSQ_AlmacenMP_Purgas SET CantidadRecibidaProduccion=@Kg,FechaRecepcionProduccion=SYSDATETIME(),
 UsuarioRecepcionProduccionID=@Usuario,ProgramaRecepcionID=@Programa,MotivoRecepcion=NULLIF(@Motivo,N'')
WHERE PurgaID=@Purga AND FechaRecepcionProduccion IS NULL AND Activo=1;
-- Confirmar no genera otra salida: Almacén ya descontó el inventario al entregar.
UPDATE dbo.AlmacenMP_Movimientos SET ValidadoProduccion=1 WHERE MovimientoID=@Movimiento AND Activo=1;
""";
            await using(var cmd=new SqlCommand(guardar,cn,tx))
            {
                var q=cmd.Parameters.Add("@Kg",SqlDbType.Decimal);q.Precision=18;q.Scale=3;q.Value=kg;
                cmd.Parameters.Add("@Usuario",SqlDbType.Int).Value=usuario;cmd.Parameters.Add("@Programa",SqlDbType.Int).Value=programaProduccionID;
                cmd.Parameters.Add("@Motivo",SqlDbType.NVarChar,500).Value=motivo;cmd.Parameters.Add("@Purga",SqlDbType.Int).Value=purgaID;
                cmd.Parameters.Add("@Movimiento",SqlDbType.Int).Value=movimiento;await cmd.ExecuteNonQueryAsync();
            }
            await tx.CommitAsync();return Json(new{ok=true,mensaje=kg==entregado?"Purga recibida. La cantidad de la OF se conserva.":"Recepción de purga con diferencia registrada. La cantidad de la OF se conserva."});
        }
        catch{await tx.RollbackAsync();throw;}
    }
}
