// Servicios/SftpStorage.cs
using Microsoft.Extensions.Configuration;
using Renci.SshNet;
using System.Security.Cryptography;
using System.Text;

public interface ISftpStorage
{
    // Todo en español (sin alias)
    void SubirTexto(string rutaRelativa, string texto);
    string DescargarTexto(string rutaRelativa);

    void SubirStream(System.IO.Stream origen, string rutaRelativa);
    System.Collections.Generic.IEnumerable<(string Nombre, bool EsCarpeta, long Tamano, System.DateTime UltimaModUtc)>
        Listar(string rutaRelativaDirectorio);

    void AsegurarDirectorio(string rutaRelativaDirectorio);
    void EliminarRecursivo(string rutaRelativa);
    byte[] DescargarBytes(string rutaRelativa);
}

public sealed class SftpStorage : ISftpStorage
{
    private readonly string host;
    private readonly int port;
    private readonly string user;
    private readonly string remoteBase;
    private readonly string password;    // Prod: SFTP_PWD; Dev: Sftp:Password (user-secrets)
    private readonly string? hostFp;     // Opcional: huella SHA256 Base64 (sin '=')
    private readonly IConfiguration cfg;

    public SftpStorage(IConfiguration configuration)
    {
        cfg = configuration;

        host = cfg["Sftp:Host"] ?? throw new System.InvalidOperationException("Sftp:Host ausente");
        port = int.TryParse(cfg["Sftp:Port"], out var p) ? p : 2222;
        user = cfg["Sftp:Username"] ?? throw new System.InvalidOperationException("Sftp:Username ausente");
        remoteBase = (cfg["Sftp:RemoteBase"] ?? "/INTRANET").TrimEnd('/');

        password = System.Environment.GetEnvironmentVariable("SFTP_PWD")
                   ?? cfg["Sftp:Password"]
                   ?? throw new System.InvalidOperationException("No hay contraseña SFTP (SFTP_PWD o Sftp:Password)");

        hostFp = System.Environment.GetEnvironmentVariable("SFTP_HOST_FP"); // opcional
    }

    private SftpClient Conectar()
    {
        var c = new SftpClient(host, port, user, password);

        if (!string.IsNullOrWhiteSpace(hostFp))
        {
            c.HostKeyReceived += (s, e) =>
            {
                var sha256 = Convert.ToBase64String(SHA256.HashData(e.HostKey)).TrimEnd('=');
                e.CanTrust = string.Equals(sha256, hostFp, System.StringComparison.Ordinal);
            };
        }

        c.Connect();
        c.ChangeDirectory(remoteBase); // p. ej. /INTRANET
        return c;
    }

    //  Helpers de ruta POSIX 
    private static string UnirPosix(params string[] parts)
        => string.Join('/', parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim('/')));

    private static string Normalizar(string? ruta)
    {
        ruta ??= "";
        ruta = ruta.Replace('\\', '/').Trim('/');
        if (ruta.Contains("..")) throw new System.InvalidOperationException("Ruta inválida.");
        return ruta;
    }

    //  Implementación 
    public void SubirTexto(string rutaRelativa, string texto)
    {
        rutaRelativa = Normalizar(rutaRelativa);
        using var c = Conectar();
        using var ms = new System.IO.MemoryStream(Encoding.UTF8.GetBytes(texto));
        c.UploadFile(ms, rutaRelativa, true);
    }

    public string DescargarTexto(string rutaRelativa)
    {
        rutaRelativa = Normalizar(rutaRelativa);
        using var c = Conectar();
        using var ms = new System.IO.MemoryStream();
        c.DownloadFile(rutaRelativa, ms);
        ms.Position = 0;
        using var r = new System.IO.StreamReader(ms, Encoding.UTF8);
        return r.ReadToEnd();
    }

    public void SubirStream(System.IO.Stream origen, string rutaRelativa)
    {
        rutaRelativa = Normalizar(rutaRelativa);
        var dir = System.IO.Path.GetDirectoryName(rutaRelativa)?.Replace('\\', '/') ?? "";
        using var c = Conectar();

        if (!string.IsNullOrEmpty(dir))
            AsegurarDirectorioInterno(c, dir);

        c.UploadFile(origen, rutaRelativa, true);
    }

    public System.Collections.Generic.IEnumerable<(string Nombre, bool EsCarpeta, long Tamano, System.DateTime UltimaModUtc)>
        Listar(string rutaRelativaDirectorio)
    {
        rutaRelativaDirectorio = Normalizar(rutaRelativaDirectorio);
        using var c = Conectar();

        var dir = string.IsNullOrEmpty(rutaRelativaDirectorio) ? "." : rutaRelativaDirectorio;
        foreach (var f in c.ListDirectory(dir))
        {
            if (f.Name is "." or "..") continue;
            yield return (f.Name, f.IsDirectory, f.Attributes.Size, f.Attributes.LastWriteTimeUtc);
        }
    }

    public void AsegurarDirectorio(string rutaRelativaDirectorio)
    {
        rutaRelativaDirectorio = Normalizar(rutaRelativaDirectorio);
        using var c = Conectar();
        if (string.IsNullOrEmpty(rutaRelativaDirectorio)) return;
        AsegurarDirectorioInterno(c, rutaRelativaDirectorio);
    }

    public void EliminarRecursivo(string rutaRelativa)
    {
        rutaRelativa = Normalizar(rutaRelativa);
        using var c = Conectar();
        if (!c.Exists(rutaRelativa)) return;

        var attr = c.GetAttributes(rutaRelativa);
        if (attr.IsDirectory)
        {
            foreach (var f in c.ListDirectory(rutaRelativa))
            {
                if (f.Name is "." or "..") continue;
                EliminarRecursivo(UnirPosix(rutaRelativa, f.Name));
            }
            c.DeleteDirectory(rutaRelativa);
        }
        else
        {
            c.DeleteFile(rutaRelativa);
        }
    }

    public byte[] DescargarBytes(string rutaRelativa)
    {
        rutaRelativa = Normalizar(rutaRelativa);
        using var c = Conectar();
        using var ms = new System.IO.MemoryStream();
        c.DownloadFile(rutaRelativa, ms);
        return ms.ToArray();
    }

    private static void AsegurarDirectorioInterno(SftpClient c, string dirPosix)
    {
        var path = "";
        foreach (var p in dirPosix.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            path = string.IsNullOrEmpty(path) ? p : $"{path}/{p}";
            if (!c.Exists(path)) c.CreateDirectory(path);
        }
    }
}
