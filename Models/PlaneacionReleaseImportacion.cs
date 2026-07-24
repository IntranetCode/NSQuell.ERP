namespace ERP.NSQuell.Models;

public sealed class PlaneacionReleaseImportacionResultadoVm
{
    public DateTime FechaProceso { get; set; } = DateTime.Now;
    public string? ErrorGeneral { get; set; }
    public string? NotaGeneral { get; set; }
    public List<PlaneacionReleaseImportacionArchivoVm> Archivos { get; set; } = new();

    public int TotalArchivos => Archivos.Count;
    public int Exitosos => Archivos.Count(x => x.Estado == "CREADO");
    public int Pendientes => Archivos.Count(x => x.Estado == "PENDIENTE");
    public int Omitidos => Archivos.Count(x => x.Estado == "OMITIDO");
    public int Errores => Archivos.Count(x => x.Estado == "ERROR" || x.Estado == "NO_SOPORTADO");
    public int TotalEntregas => Archivos
        .Where(x => x.Estado == "CREADO" || x.Estado == "PENDIENTE")
        .Sum(x => x.TotalEntregas);
    public int TotalPiezas => Archivos
        .Where(x => x.Estado == "CREADO" || x.Estado == "PENDIENTE")
        .Sum(x => x.TotalPiezas);
}

public sealed class PlaneacionReleaseImportacionArchivoVm
{
    public string Archivo { get; set; } = string.Empty;
    public string Estado { get; set; } = "PENDIENTE";
    public string Mensaje { get; set; } = string.Empty;
    public int? ReleaseID { get; set; }
    public string? FolioRelease { get; set; }
    public int? ClienteID { get; set; }
    public string? Cliente { get; set; }
    public string? Parte { get; set; }
    public string? Descripcion { get; set; }
    public string? Schedule { get; set; }
    public string? OrdenCliente { get; set; }
    public string? Version { get; set; }
    public string? ArchivoGuardado { get; set; }
    public bool RequiereVinculacion { get; set; }
    public int TotalEntregas { get; set; }
    public int TotalPiezas { get; set; }
    public int VersionesAnterioresCerradas { get; set; }
    public List<string> Advertencias { get; set; } = new();
}