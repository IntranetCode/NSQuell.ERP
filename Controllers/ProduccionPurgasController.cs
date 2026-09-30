using ERP.NSQuell.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;

namespace ERP.NSQuell.Controllers;

[Route("ProduccionPurgas")]
public sealed class ProduccionPurgasController : Controller
{
    private readonly IConfiguration _configuration;
    public ProduccionPurgasController(IConfiguration configuration) => _configuration = configuration;
    private string ConnectionString => _configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Falta DefaultConnection.");
    private int UsuarioID => HttpContext.Session.GetInt32("UsuarioID") ?? 0;
    private bool UsuarioEnSesion() => UsuarioID > 0;

    [HttpGet("")]
    [HttpGet("Index")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Index()
    {
        if (!UsuarioEnSesion()) return RedirectToAction("Login", "Login");
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync();
        var vm = new ProduccionPurgaIndexVm { FechaConsulta = DateTime.Now };
        if (!await ExisteTablaAsync(cn))
        {
            vm.Configurado = false;
            return View(vm);
        }
        vm.PuedeEntregarAlmacen = await PuedeEntregarAlmacenAsync(UsuarioID, cn, null);
        vm.Maquinas = await CargarMaquinasAsync(cn);
        vm.Programas = await CargarProgramasAsync(cn);
        vm.Materiales = await CargarMaterialesAsync(cn);
        vm.Purgas = await CargarPurgasAsync(cn);
        return View(vm);
    }

    [HttpPost("Solicitar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Solicitar(int maquinaId, int programaProduccionId, int materialId, string tipoMP, string lote, decimal cantidadKg, string motivo, string? observaciones)
    {
        if (!UsuarioEnSesion()) return RedirectToAction("Login", "Login");
        tipoMP = NormalizarTipo(tipoMP); lote=(lote??string.Empty).Trim(); motivo=(motivo??string.Empty).Trim(); observaciones=observaciones?.Trim();
        if (maquinaId<=0 || programaProduccionId<=0 || materialId<=0) return Error("Selecciona máquina, programa/OF y material.");
        if (cantidadKg<=0m || cantidadKg>2.000m) return Error("La purga debe ser mayor a 0 y no puede superar 2.000 kg por cambio y máquina.");
        if (lote.Length==0) return Error("Captura el lote de materia prima previsto para la purga.");
        if (motivo.Length<3) return Error("Captura el motivo de la purga.");
        await using var cn=new SqlConnection(ConnectionString); await cn.OpenAsync();
        await using var tx=(SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            const string contexto=@"SELECT TOP(1) pp.MaquinaID,pp.SolicitudProduccionID FROM dbo.Planeacion_ProgramaProduccion pp WITH(UPDLOCK,HOLDLOCK) WHERE pp.ProgramaProduccionID=@Programa AND pp.Activo=1;";
            int? maqPrograma=null; int? solicitud=null;
            await using(var cmd=new SqlCommand(contexto,cn,tx)){cmd.Parameters.Add("@Programa",SqlDbType.Int).Value=programaProduccionId; await using var rd=await cmd.ExecuteReaderAsync(); if(!await rd.ReadAsync()) throw new InvalidOperationException("El programa seleccionado ya no está activo."); maqPrograma=rd["MaquinaID"]==DBNull.Value?null:Convert.ToInt32(rd["MaquinaID"]); solicitud=rd["SolicitudProduccionID"]==DBNull.Value?null:Convert.ToInt32(rd["SolicitudProduccionID"]);}
            if (maqPrograma.HasValue && maqPrograma.Value!=maquinaId) throw new InvalidOperationException("El programa ya no pertenece a la máquina seleccionada.");
            const string suma=@"SELECT ISNULL(SUM(CantidadSolicitadaKg),0) FROM dbo.Produccion_Purgas WITH(UPDLOCK,HOLDLOCK) WHERE MaquinaID=@Maquina AND ProgramaProduccionID=@Programa AND Activo=1 AND Estado<>N'CANCELADA';";
            decimal acumulado; await using(var cmd=new SqlCommand(suma,cn,tx)){cmd.Parameters.Add("@Maquina",SqlDbType.Int).Value=maquinaId;cmd.Parameters.Add("@Programa",SqlDbType.Int).Value=programaProduccionId;acumulado=Convert.ToDecimal(await cmd.ExecuteScalarAsync());}
            if (acumulado+cantidadKg>2.000m) throw new InvalidOperationException($"Este cambio/máquina ya acumula {acumulado:N3} kg. El máximo total es 2.000 kg.");
            const string ins=@"INSERT dbo.Produccion_Purgas(MaquinaID,ProgramaProduccionID,SolicitudProduccionID,MaterialID,TipoMP,Lote,CantidadSolicitadaKg,Motivo,Observaciones,Estado,UsuarioSolicitudID,FechaSolicitud,Activo) OUTPUT INSERTED.PurgaID VALUES(@Maquina,@Programa,@Solicitud,@Material,@Tipo,@Lote,@Cantidad,@Motivo,@Obs,N'SOLICITADA',@Usuario,SYSDATETIME(),1);";
            long id; await using(var cmd=new SqlCommand(ins,cn,tx)){cmd.Parameters.Add("@Maquina",SqlDbType.Int).Value=maquinaId;cmd.Parameters.Add("@Programa",SqlDbType.Int).Value=programaProduccionId;cmd.Parameters.Add("@Solicitud",SqlDbType.Int).Value=(object?)solicitud??DBNull.Value;cmd.Parameters.Add("@Material",SqlDbType.Int).Value=materialId;cmd.Parameters.Add("@Tipo",SqlDbType.NChar,1).Value=tipoMP;cmd.Parameters.Add("@Lote",SqlDbType.NVarChar,120).Value=lote;var pc=cmd.Parameters.Add("@Cantidad",SqlDbType.Decimal);pc.Precision=18;pc.Scale=3;pc.Value=cantidadKg;cmd.Parameters.Add("@Motivo",SqlDbType.NVarChar,250).Value=motivo;cmd.Parameters.Add("@Obs",SqlDbType.NVarChar,800).Value=(object?)observaciones??DBNull.Value;cmd.Parameters.Add("@Usuario",SqlDbType.Int).Value=UsuarioID;id=Convert.ToInt64(await cmd.ExecuteScalarAsync());}
            await HistorialAsync(id,"SOLICITADA",$"Purga solicitada por {cantidadKg:N3} kg.",UsuarioID,cn,tx);
            await tx.CommitAsync(); TempData["Success"]=$"Purga #{id} solicitada. Almacén MP debe entregar {cantidadKg:N3} kg.";
        }
        catch(Exception ex){await tx.RollbackAsync();TempData["Error"]="No fue posible solicitar la purga: "+ex.Message;}
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("Entregar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Entregar(long purgaId, string loteEntrega)
    {
        if (!UsuarioEnSesion()) return RedirectToAction("Login", "Login");
        loteEntrega=(loteEntrega??string.Empty).Trim(); if(loteEntrega.Length==0) return Error("Almacén debe capturar el lote realmente entregado.");
        await using var cn=new SqlConnection(ConnectionString); await cn.OpenAsync(); await using var tx=(SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            if(!await PuedeEntregarAlmacenAsync(UsuarioID,cn,tx)) throw new InvalidOperationException("Solo Almacén o un administrador puede confirmar la entrega de purga.");
            const string sel=@"SELECT TOP(1) p.PurgaID,p.MaterialID,p.TipoMP,p.CantidadSolicitadaKg,p.SolicitudProduccionID,p.ProgramaProduccionID,COALESCE(s.NumeroOFRecibida,s.FolioSolicitud,N'') NumeroOF FROM dbo.Produccion_Purgas p WITH(UPDLOCK,HOLDLOCK) LEFT JOIN dbo.SolicitudesProduccion s ON s.SolicitudProduccionID=p.SolicitudProduccionID WHERE p.PurgaID=@ID AND p.Activo=1 AND p.Estado=N'SOLICITADA';";
            int material,programa; string tipo,numeroOF; decimal cantidad; int? solicitud;
            await using(var cmd=new SqlCommand(sel,cn,tx)){cmd.Parameters.Add("@ID",SqlDbType.BigInt).Value=purgaId;await using var rd=await cmd.ExecuteReaderAsync();if(!await rd.ReadAsync()) throw new InvalidOperationException("La purga no está pendiente de entrega.");material=Convert.ToInt32(rd["MaterialID"]);tipo=rd["TipoMP"]?.ToString()?.Trim()??"V";cantidad=Convert.ToDecimal(rd["CantidadSolicitadaKg"]);solicitud=rd["SolicitudProduccionID"]==DBNull.Value?null:Convert.ToInt32(rd["SolicitudProduccionID"]);programa=Convert.ToInt32(rd["ProgramaProduccionID"]);numeroOF=rd["NumeroOF"]?.ToString()?.Trim()??string.Empty;}
            const string stock=@"SELECT ISNULL(SUM(Disponible),0) FROM dbo.vw_AlmacenMPInventario WHERE MaterialID=@Material AND TipoMP=@Tipo;";
            decimal disponible; await using(var cmd=new SqlCommand(stock,cn,tx)){cmd.Parameters.Add("@Material",SqlDbType.Int).Value=material;cmd.Parameters.Add("@Tipo",SqlDbType.NChar,1).Value=tipo;disponible=Convert.ToDecimal(await cmd.ExecuteScalarAsync());}
            if(disponible+0.0005m<cantidad) throw new InvalidOperationException($"Stock MP insuficiente para la purga. Disponible {disponible:N3} kg; solicitado {cantidad:N3} kg.");
            const string mov=@"INSERT dbo.AlmacenMP_Movimientos(FechaMovimiento,MaterialID,TipoMovimiento,Lote,Cantidad,Unidad,EstatusCalidad,Seguimiento,FechaCreacion,CreadoPor,Activo,RequiereValidacionProduccion,ValidadoProduccion,NumeroOF,ResponsableUsuarioID,ReferenciaOperacion,TipoMP,SolicitudProduccionID,MaterialSolicitadoID) OUTPUT INSERTED.MovimientoID VALUES(SYSDATETIME(),@Material,N'Salida',@Lote,@Cantidad,N'KG',N'Liberado',@Seguimiento,SYSDATETIME(),@CreadoPor,1,0,1,@OF,@Usuario,@Ref,@Tipo,@Solicitud,@Material);";
            int movimiento; await using(var cmd=new SqlCommand(mov,cn,tx)){cmd.Parameters.Add("@Material",SqlDbType.Int).Value=material;cmd.Parameters.Add("@Lote",SqlDbType.NVarChar,120).Value=loteEntrega;var pc=cmd.Parameters.Add("@Cantidad",SqlDbType.Decimal);pc.Precision=18;pc.Scale=3;pc.Value=cantidad;cmd.Parameters.Add("@Seguimiento",SqlDbType.NVarChar,800).Value=$"Entrega de MP para purga #{purgaId}, programa {programa}.";cmd.Parameters.Add("@CreadoPor",SqlDbType.NVarChar,120).Value=$"Usuario {UsuarioID}";cmd.Parameters.Add("@OF",SqlDbType.NVarChar,80).Value=(object?)numeroOF??DBNull.Value;cmd.Parameters.Add("@Usuario",SqlDbType.Int).Value=UsuarioID;cmd.Parameters.Add("@Ref",SqlDbType.NVarChar,120).Value=$"PURGA-{purgaId}";cmd.Parameters.Add("@Tipo",SqlDbType.NChar,1).Value=tipo;cmd.Parameters.Add("@Solicitud",SqlDbType.Int).Value=(object?)solicitud??DBNull.Value;movimiento=Convert.ToInt32(await cmd.ExecuteScalarAsync());}
            const string upd=@"UPDATE dbo.Produccion_Purgas SET Estado=N'ENTREGADA',CantidadEntregadaKg=CantidadSolicitadaKg,Lote=@Lote,MovimientoAlmacenID=@Movimiento,UsuarioEntregaID=@Usuario,FechaEntrega=SYSDATETIME(),UsuarioModificacionID=@Usuario,FechaModificacion=SYSDATETIME() WHERE PurgaID=@ID AND Estado=N'SOLICITADA' AND Activo=1; IF @@ROWCOUNT<>1 THROW 51051,'La purga cambió de estado.',1;";
            await using(var cmd=new SqlCommand(upd,cn,tx)){cmd.Parameters.Add("@ID",SqlDbType.BigInt).Value=purgaId;cmd.Parameters.Add("@Lote",SqlDbType.NVarChar,120).Value=loteEntrega;cmd.Parameters.Add("@Movimiento",SqlDbType.Int).Value=movimiento;cmd.Parameters.Add("@Usuario",SqlDbType.Int).Value=UsuarioID;await cmd.ExecuteNonQueryAsync();}
            await HistorialAsync(purgaId,"ENTREGADA",$"Almacén entregó {cantidad:N3} kg. Movimiento MP {movimiento}.",UsuarioID,cn,tx); await tx.CommitAsync(); TempData["Success"]=$"Purga #{purgaId}: MP entregada correctamente.";
        }
        catch(Exception ex){await tx.RollbackAsync();TempData["Error"]="No fue posible entregar la purga: "+ex.Message;}
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("Aplicar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Aplicar(long purgaId)
    {
        if(!UsuarioEnSesion()) return RedirectToAction("Login","Login"); await using var cn=new SqlConnection(ConnectionString);await cn.OpenAsync();await using var tx=(SqlTransaction)await cn.BeginTransactionAsync();
        try{const string q=@"UPDATE dbo.Produccion_Purgas SET Estado=N'APLICADA',UsuarioAplicacionID=@U,FechaAplicacion=SYSDATETIME(),UsuarioModificacionID=@U,FechaModificacion=SYSDATETIME() WHERE PurgaID=@ID AND Activo=1 AND Estado=N'ENTREGADA'; IF @@ROWCOUNT<>1 THROW 51052,'La purga debe estar entregada por Almacén antes de aplicarse.',1;";await using(var c=new SqlCommand(q,cn,tx)){c.Parameters.Add("@ID",SqlDbType.BigInt).Value=purgaId;c.Parameters.Add("@U",SqlDbType.Int).Value=UsuarioID;await c.ExecuteNonQueryAsync();}await HistorialAsync(purgaId,"APLICADA","Producción confirmó la aplicación de la purga.",UsuarioID,cn,tx);await tx.CommitAsync();TempData["Success"]=$"Purga #{purgaId} aplicada.";}catch(Exception ex){await tx.RollbackAsync();TempData["Error"]=ex.Message;}return RedirectToAction(nameof(Index));
    }

    private IActionResult Error(string m){TempData["Error"]=m;return RedirectToAction(nameof(Index));}
    private static string NormalizarTipo(string? v)=>string.Equals(v?.Trim(),"M",StringComparison.OrdinalIgnoreCase)?"M":"V";
    private static async Task<bool> ExisteTablaAsync(SqlConnection cn){await using var c=new SqlCommand("SELECT CASE WHEN OBJECT_ID(N'dbo.Produccion_Purgas',N'U') IS NULL THEN 0 ELSE 1 END",cn);return Convert.ToInt32(await c.ExecuteScalarAsync())==1;}
    private static async Task HistorialAsync(long id,string evento,string comentario,int usuario,SqlConnection cn,SqlTransaction tx){await using var c=new SqlCommand("INSERT dbo.Produccion_PurgasHistorial(PurgaID,Evento,Comentario,UsuarioID,FechaEvento) VALUES(@I,@E,@C,@U,SYSDATETIME())",cn,tx);c.Parameters.Add("@I",SqlDbType.BigInt).Value=id;c.Parameters.Add("@E",SqlDbType.NVarChar,50).Value=evento;c.Parameters.Add("@C",SqlDbType.NVarChar,1000).Value=comentario;c.Parameters.Add("@U",SqlDbType.Int).Value=usuario;await c.ExecuteNonQueryAsync();}
    private static async Task<bool> PuedeEntregarAlmacenAsync(int usuario,SqlConnection cn,SqlTransaction? tx){const string q=@"SELECT CASE WHEN u.RolID=1 OR UPPER(ISNULL(d.NombreDepartamento,N'')) LIKE N'%ALMAC%' THEN 1 ELSE 0 END FROM dbo.Usuarios u LEFT JOIN dbo.Departamentos d ON d.DepartamentoID=u.DepartamentoID WHERE u.UsuarioID=@U AND ISNULL(u.Activo,1)=1;";await using var c=tx==null?new SqlCommand(q,cn):new SqlCommand(q,cn,tx);c.Parameters.Add("@U",SqlDbType.Int).Value=usuario;return Convert.ToInt32((await c.ExecuteScalarAsync())??0)==1;}
    private static async Task<List<ProduccionPurgaMaquinaVm>> CargarMaquinasAsync(SqlConnection cn){var l=new List<ProduccionPurgaMaquinaVm>();await using var c=new SqlCommand("SELECT MaquinaID,Codigo,Nombre FROM dbo.ERP_Maquinas WHERE Activo=1 ORDER BY Codigo",cn);await using var r=await c.ExecuteReaderAsync();while(await r.ReadAsync())l.Add(new(){MaquinaID=Convert.ToInt32(r["MaquinaID"]),Texto=$"{r["Codigo"]} - {r["Nombre"]}"});return l;}
    private static async Task<List<ProduccionPurgaProgramaVm>> CargarProgramasAsync(SqlConnection cn){var l=new List<ProduccionPurgaProgramaVm>();const string q=@"SELECT TOP(300) pp.ProgramaProduccionID,pp.MaquinaID,COALESCE(s.NumeroOFRecibida,s.FolioSolicitud,CONCAT(N'Programa ',pp.ProgramaProduccionID)) OF,pp.NumeroParte FROM dbo.Planeacion_ProgramaProduccion pp LEFT JOIN dbo.SolicitudesProduccion s ON s.SolicitudProduccionID=pp.SolicitudProduccionID WHERE pp.Activo=1 AND ISNULL(pp.EstatusID,1)<>99 AND ISNULL(pp.FechaFinProgramada,DATEADD(DAY,1,GETDATE()))>=DATEADD(DAY,-7,GETDATE()) ORDER BY pp.FechaInicioProgramada DESC;";await using var c=new SqlCommand(q,cn);await using var r=await c.ExecuteReaderAsync();while(await r.ReadAsync())l.Add(new(){ProgramaProduccionID=Convert.ToInt32(r["ProgramaProduccionID"]),MaquinaID=r["MaquinaID"]==DBNull.Value?null:Convert.ToInt32(r["MaquinaID"]),Texto=$"{r["OF"]} · {r["NumeroParte"]}"});return l;}
    private static async Task<List<ProduccionPurgaMaterialVm>> CargarMaterialesAsync(SqlConnection cn){var l=new List<ProduccionPurgaMaterialVm>();const string q=@"SELECT MaterialID,TipoMP,Codigo,Nombre,Disponible FROM dbo.vw_AlmacenMPInventario WHERE Disponible>0 ORDER BY Codigo,TipoMP;";await using var c=new SqlCommand(q,cn);await using var r=await c.ExecuteReaderAsync();while(await r.ReadAsync())l.Add(new(){MaterialID=Convert.ToInt32(r["MaterialID"]),TipoMP=r["TipoMP"]?.ToString()?.Trim()??"V",DisponibleKg=Convert.ToDecimal(r["Disponible"]),Texto=$"{r["Codigo"]} · {r["Nombre"]} · {r["TipoMP"]} · {Convert.ToDecimal(r["Disponible"]):N3} kg"});return l;}
    private static async Task<List<ProduccionPurgaItemVm>> CargarPurgasAsync(SqlConnection cn){var l=new List<ProduccionPurgaItemVm>();const string q=@"SELECT TOP(200) p.PurgaID,p.MaquinaID,COALESCE(m.Codigo,m.Nombre,N'') Maquina,p.ProgramaProduccionID,COALESCE(s.NumeroOFRecibida,s.FolioSolicitud,CONCAT(N'Programa ',p.ProgramaProduccionID)) OF,pp.NumeroParte,p.MaterialID,COALESCE(mat.Codigo,N'')+N' - '+COALESCE(mat.Nombre,N'') Material,p.TipoMP,p.Lote,p.CantidadSolicitadaKg,p.CantidadEntregadaKg,p.Motivo,p.Observaciones,p.Estado,p.FechaSolicitud,p.FechaEntrega,p.FechaAplicacion FROM dbo.Produccion_Purgas p LEFT JOIN dbo.ERP_Maquinas m ON m.MaquinaID=p.MaquinaID LEFT JOIN dbo.Planeacion_ProgramaProduccion pp ON pp.ProgramaProduccionID=p.ProgramaProduccionID LEFT JOIN dbo.SolicitudesProduccion s ON s.SolicitudProduccionID=p.SolicitudProduccionID LEFT JOIN dbo.ERP_Materiales mat ON mat.MaterialID=p.MaterialID WHERE p.Activo=1 ORDER BY p.PurgaID DESC;";await using var c=new SqlCommand(q,cn);await using var r=await c.ExecuteReaderAsync();while(await r.ReadAsync())l.Add(new(){PurgaID=Convert.ToInt64(r["PurgaID"]),MaquinaID=Convert.ToInt32(r["MaquinaID"]),Maquina=r["Maquina"]?.ToString()??"",ProgramaProduccionID=Convert.ToInt32(r["ProgramaProduccionID"]),OF=r["OF"]?.ToString()??"",Parte=r["NumeroParte"]?.ToString()??"",MaterialID=Convert.ToInt32(r["MaterialID"]),Material=r["Material"]?.ToString()??"",TipoMP=r["TipoMP"]?.ToString()?.Trim()??"V",Lote=r["Lote"]?.ToString()??"",CantidadSolicitadaKg=Convert.ToDecimal(r["CantidadSolicitadaKg"]),CantidadEntregadaKg=r["CantidadEntregadaKg"]==DBNull.Value?null:Convert.ToDecimal(r["CantidadEntregadaKg"]),Motivo=r["Motivo"]?.ToString()??"",Observaciones=r["Observaciones"]==DBNull.Value?null:r["Observaciones"]?.ToString(),Estado=r["Estado"]?.ToString()??"",FechaSolicitud=Convert.ToDateTime(r["FechaSolicitud"]),FechaEntrega=r["FechaEntrega"]==DBNull.Value?null:Convert.ToDateTime(r["FechaEntrega"]),FechaAplicacion=r["FechaAplicacion"]==DBNull.Value?null:Convert.ToDateTime(r["FechaAplicacion"])});return l;}
}
