using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;


public sealed class MiddlewareContextoSolicitud
{
    private readonly RequestDelegate _siguiente;

    public MiddlewareContextoSolicitud(RequestDelegate siguiente)
    {
        _siguiente = siguiente;
    }
    public async Task Invoke(HttpContext contexto)
    {
        //Identificadoe para las solicitudes (sirve para unir todos los logs del mismo request

        var solicitudId = Activity.Current?.Id ?? Guid.NewGuid().ToString("n");
        contexto.Items["SolicitudId"] = solicitudId;


        //Ip y agente usuario

        contexto.Items["DireccionIp"] = contexto.Connection.RemoteIpAddress?.ToString();
        contexto.Items["AgenteUsuario"] = contexto.Request.Headers["User-Agent"].ToString();


        //oBTENER idusuario y idempresa desde los cliaims

        var idUsuario = contexto.User?.FindFirst("UsuarioID")?.Value;
        var idEmpresa = contexto.User?.FindFirst("EmpresaID")?.Value;
        if (!string.IsNullOrEmpty(idUsuario)) contexto.Items["IdUsuario"] = idUsuario;
        if (!string.IsNullOrEmpty(idEmpresa)) contexto.Items["IdEmpresa"] = idEmpresa;

        await _siguiente(contexto);

    }
}