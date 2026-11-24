using Microsoft.Data.SqlClient;
using System.Data;
namespace ERP.NSQuell.Servicios.AutoAuditoria;
internal sealed class AaEntorno
{
    public bool Prod {get;}
    public string Server {get;}
    public string PublicBase {get;}
    public string ErpBase {get;}
    public bool ExternalMail {get;}
    public bool Local => PublicBase.StartsWith("http://127.0.0.1:",StringComparison.Ordinal);
    public bool PublicConfigured => !string.IsNullOrWhiteSpace(PublicBase);
    private AaEntorno(bool prod,string server,string publicBase,string erpBase,bool externalMail){Prod=prod;Server=server;PublicBase=publicBase;ErpBase=erpBase;ExternalMail=externalMail;}
    private static string HttpsBase(string? s)
    {
        if (!Uri.TryCreate(s,UriKind.Absolute,out var u)||u.Scheme!="https"||u.IsLoopback||u.AbsolutePath!="/"||u.Query!=""||u.Fragment!=""||u.UserInfo!=""||u.Port!=443||Uri.CheckHostName(u.Host)!=UriHostNameType.Dns) throw new InvalidOperationException("URL HTTPS de Autoauditoria no válida.");
        return u.GetLeftPart(UriPartial.Authority);
    }
    public static AaEntorno From(IConfiguration cfg, IHostEnvironment environment)
    {
        var mode=cfg["AutoAuditoria:Modo"]??"TEST";
        bool prod=mode.Equals("PROD",StringComparison.OrdinalIgnoreCase);
        if(!prod&&!mode.Equals("TEST",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Modo Autoauditoria inválido.");
        var server=cfg["AutoAuditoria:ServidorSql"]??(prod?"":"WIN-FJ96BA1UR50\\ERP_TEST");
        if(!server.EndsWith(prod?"\\ERP_PROD":"\\ERP_TEST",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Servidor SQL de Autoauditoria no válido para el modo seleccionado.");
        string expectedErp=prod?"https://erp.quell.nsgroup.com.mx":"https://erpnsqt.nsgroup.com.mx";
        string? configuredPublic=cfg["AutoAuditoria:UrlPublica"];
        string? configuredErp=cfg["AutoAuditoria:UrlErp"];
        // Development alone is not sufficient for external links; controller also checks the request host.
        bool local=!prod&&environment.IsDevelopment()&&string.IsNullOrWhiteSpace(configuredPublic)&&string.IsNullOrWhiteSpace(configuredErp);
        if(local)return new(false,server,"http://127.0.0.1:5054","http://localhost:5053",false);
        var erpBase=string.IsNullOrWhiteSpace(configuredErp)?expectedErp:HttpsBase(configuredErp);
        if(!erpBase.Equals(expectedErp,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("UrlErp no corresponde al ambiente de Autoauditoria.");
        // Missing public URL leaves panel and worker available, but generation and renewal are blocked.
        // V7: reuse the existing HTTPS host, but only the isolated IIS sub-application.
        string expectedPublic=expectedErp+"/FormularioExterno";
        string publicBase="";
        if(!string.IsNullOrWhiteSpace(configuredPublic))
        {
            if(!string.Equals(configuredPublic.TrimEnd('/'),expectedPublic,StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("UrlPublica debe corresponder a /FormularioExterno del ERP de este ambiente.");
            publicBase=expectedPublic;
        }
        return new(prod,server,publicBase,erpBase,prod&&!string.IsNullOrWhiteSpace(publicBase)&&environment.IsProduction()&&string.Equals(cfg["AutoAuditoria:EnvioExternoHabilitado"],"true",StringComparison.OrdinalIgnoreCase));
    }
    public void Bind(SqlCommand cmd){cmd.Parameters.Add("@aaServer",SqlDbType.NVarChar,128).Value=Server;cmd.Parameters.Add("@aaTest",SqlDbType.Bit).Value=!Prod;}
    public const string Marker="EsTest=@aaTest AND Servidor=@aaServer AND Servidor=CONVERT(nvarchar(128),SERVERPROPERTY('ServerName')) AND BaseDatos=DB_NAME() AND DB_NAME()=N'ERP_QUELL'";
}
