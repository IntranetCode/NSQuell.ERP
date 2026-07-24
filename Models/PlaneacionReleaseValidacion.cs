using System.Text.Json.Serialization;

namespace ERP.NSQuell.Models;

public sealed class ReleaseValidacionLoteVm
{
    public string LoteID { get; set; } = string.Empty;
    public DateTime FechaProceso { get; set; } = DateTime.Now;
    public int UsuarioID { get; set; }
    public string? ErrorGeneral { get; set; }
    public string? NotaGeneral { get; set; }
    public List<ReleaseValidacionDocumentoVm> Documentos { get; set; } = new();

    public int Total => Documentos.Count;
    public int Validados => Documentos.Count(x => x.Estado == ReleaseValidacionEstados.Validado);
    public int Pendientes => Documentos.Count(x => x.Estado == ReleaseValidacionEstados.Pendiente);
    public int Omitidos => Documentos.Count(x => x.Estado == ReleaseValidacionEstados.Omitido);
    public int Errores => Documentos.Count(x =>
        x.Estado == ReleaseValidacionEstados.Error ||
        x.Estado == ReleaseValidacionEstados.NoSoportado);
    public int Guardados => Documentos.Count(x => x.Estado == ReleaseValidacionEstados.Guardado);
}

public sealed class ReleaseValidacionDocumentoVm
{
    public string DocumentoID { get; set; } = Guid.NewGuid().ToString("N");
    public string Archivo { get; set; } = string.Empty;
    public string ArchivoTemporal { get; set; } = string.Empty;
    public string Estado { get; set; } = ReleaseValidacionEstados.Pendiente;
    public string Mensaje { get; set; } = string.Empty;

    public string Plantilla { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public DateTime? FechaDocumento { get; set; }

    public int? ClienteID { get; set; }
    public string? Cliente { get; set; }
    public string? FolioCliente { get; set; }
    public string? Version { get; set; }

    public int TotalEntregas { get; set; }
    public int TotalPiezas { get; set; }
    public int? ReleaseID { get; set; }
    public string? FolioRelease { get; set; }

    public List<string> Advertencias { get; set; } = new();
    public PlaneacionReleaseCrearVm ReleasePreparado { get; set; } = new();

    [JsonIgnore]
    public int PartesPendientes =>
        ReleasePreparado.Renglones.Count(x => !x.ParteID.HasValue);

    [JsonIgnore]
    public string PartesTexto =>
        string.Join(", ",
            ReleasePreparado.Renglones
                .Select(x => x.NumeroParte ?? x.ReferenciaSAP)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase));
}

public sealed class ReleasePendientesValidarVm
{
    public List<ReleaseValidacionLoteVm> Lotes { get; set; } = new();
    public int TotalPendientes => Lotes.Sum(x => x.Pendientes);
}

public static class ReleaseValidacionEstados
{
    public const string Validado = "VALIDADO";
    public const string Pendiente = "PENDIENTE_VALIDACION";
    public const string Omitido = "OMITIDO";
    public const string Error = "ERROR";
    public const string NoSoportado = "NO_SOPORTADO";
    public const string Guardado = "GUARDADO";
}