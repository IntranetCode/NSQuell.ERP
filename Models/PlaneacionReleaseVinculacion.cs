using Microsoft.AspNetCore.Mvc.Rendering;

namespace ERP.NSQuell.Models;

public sealed class PlaneacionReleaseVinculacionVm
{
    public int ReleaseID { get; set; }
    public string? FolioRelease { get; set; }
    public int ClienteID { get; set; }
    public string? ClienteNombre { get; set; }
    public string? ArchivoOrigenNombre { get; set; }
    public List<PlaneacionReleaseVinculacionRenglonVm> Renglones { get; set; } = new();
    public List<SelectListItem> PartesActivas { get; set; } = new();
}

public sealed class PlaneacionReleaseVinculacionRenglonVm
{
    public int ReleaseRenglonID { get; set; }
    public int Renglon { get; set; }
    public int? ParteID { get; set; }
    public bool ParteActiva { get; set; }
    public string? NumeroParteOriginal { get; set; }
    public string? ReferenciaOriginal { get; set; }
    public string? DescripcionOriginal { get; set; }
}

public sealed class PlaneacionReleaseVinculacionPostVm
{
    public int ReleaseID { get; set; }
    public List<PlaneacionReleaseVinculacionItemPostVm> Renglones { get; set; } = new();
}

public sealed class PlaneacionReleaseVinculacionItemPostVm
{
    public int ReleaseRenglonID { get; set; }
    public int? ParteID { get; set; }
}