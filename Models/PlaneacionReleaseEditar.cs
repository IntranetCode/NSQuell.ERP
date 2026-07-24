namespace ERP.NSQuell.Models;

public sealed class PlaneacionReleaseEditarVm : PlaneacionReleaseCrearVm
{
    public int ReleaseID { get; set; }
    public bool ConfirmarImpacto { get; set; }

    public bool TienePlaneacionVinculada { get; set; }
    public bool TieneProgramaBloqueado { get; set; }
    public int ProgramasVinculados { get; set; }
}