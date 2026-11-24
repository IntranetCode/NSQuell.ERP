namespace ERP.NSQuell.Servicios.AutoAuditoria;
public sealed record AaProveedor(int Id,string Nombre,string Correo,string Direccion,string Contacto);
public sealed record AaSolicitud(long Id,string Folio,string Empresa,string Correo,DateTime Creada,DateTime Vence,DateTime? Respondida,DateTime? Revocada,int Verde,int Amarillo,int Rojo,int Nb)
{
    public string Estado => Respondida.HasValue?"Respondida":Revocada.HasValue?"Revocada":Vence<=DateTime.UtcNow?"Vencida":"Pendiente";
}
public sealed class AaPanel
{
    public List<AaProveedor> Proveedores {get;}=new();
    public List<AaSolicitud> Solicitudes {get;}=new();
    public string? Enlace {get;set;}
    public string? Aviso {get;set;}
    public string? Error {get;set;}
}
public sealed record AaRespuesta(string Punto,bool Asterisco,string Texto,string Valoracion,string Observacion);
public sealed record AaEvento(string Evento,DateTime Fecha,int? Usuario);
public sealed class AaDetalle
{
    public AaSolicitud Solicitud {get;set;}=null!;
    public string Direccion {get;set;}="";
    public string Responsable {get;set;}="";
    public string CorreoRespuesta {get;set;}="";
    public DateTime? FechaAuditoria {get;set;}
    public List<AaRespuesta> Respuestas {get;}=new();
    public List<AaEvento> Historial {get;}=new();
    public List<AaAviso> Avisos {get;}=new();
}

public sealed record AaAviso(string Destinatario,string Estado,int Intentos,DateTime? UltimoIntento,DateTime? Enviado);
public sealed record AaPuntoEstadistica(string Punto,string Texto,bool Asterisco,int Verde,int Amarillo,int Rojo,int Nb)
{
    public int Total => Verde+Amarillo+Rojo+Nb;
}
public sealed record AaAcceso(int Id,string Usuario,string Nombre,string Departamento,string Rol);
public sealed class AaEstadisticas
{
    public List<AaSolicitud> Solicitudes {get;}=new();
    public List<AaPuntoEstadistica> Puntos {get;}=new();
    public List<AaAcceso> Accesos {get;}=new();
    public bool EsAdministrador {get;set;}
}
