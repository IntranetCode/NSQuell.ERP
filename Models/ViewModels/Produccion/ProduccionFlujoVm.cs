using System.ComponentModel.DataAnnotations;

namespace ERP.NSQuell.Models.ViewModels.Produccion;

public sealed class ProduccionFlujoItemVm
{
    public int EjecucionProduccionID { get; set; }
    public string NumeroOF { get; set; } = string.Empty;
    public string Cliente { get; set; } = string.Empty;
    public string NumeroParte { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public string Maquina { get; set; } = string.Empty;
    public string Molde { get; set; } = string.Empty;
    public string Turno { get; set; } = string.Empty;
    public int CantidadPlaneada { get; set; }
    public int CantidadOK { get; set; }
    public int Sospechosa { get; set; }
    public int Scrap { get; set; }
    public decimal Avance { get; set; }
    public string Estado { get; set; } = string.Empty;
    public bool ChecklistAprobado { get; set; }
    public bool RequiereReliberacion { get; set; }
    public int PersonalAsignado { get; set; }
    public int ParosAbiertos { get; set; }
    public int CajasFormadas { get; set; }
}

public sealed class ProduccionFlujoIndexVm
{
    public string? Busqueda { get; set; }
    public string? Estado { get; set; }
    public List<ProduccionFlujoItemVm> Items { get; set; } = new();
}

public sealed class ProduccionEventoVm { public DateTime Fecha { get; set; } public string Tipo { get; set; }=string.Empty; public string Descripcion { get; set; }=string.Empty; public string Usuario { get; set; }=string.Empty; public string EstadoNuevo { get; set; }=string.Empty; }
public sealed class ProduccionHoraVm { public int RegistroHoraID { get; set; } public DateTime Fecha { get; set; } public TimeSpan Inicio { get; set; } public TimeSpan Fin { get; set; } public int OK { get; set; } public int Sospechosa { get; set; } public int Scrap { get; set; } public string Turno { get; set; }=string.Empty; public string Defecto { get; set; }=string.Empty; }
public sealed class ProduccionParoVm { public int ParoID { get; set; } public DateTime Inicio { get; set; } public DateTime? Fin { get; set; } public int? Minutos { get; set; } public string Motivo { get; set; }=string.Empty; public bool RequiereReliberacion { get; set; } }
public sealed class ProduccionCajaVm { public long CajaProduccionID { get; set; } public int NumeroCaja { get; set; } public string Etiqueta { get; set; }=string.Empty; public int Cantidad { get; set; } public string EstatusCalidad { get; set; }=string.Empty; public DateTime Fecha { get; set; } }
public sealed class ProduccionPersonalVm { public int UsuarioID { get; set; } public string Nombre { get; set; }=string.Empty; public string Puesto { get; set; }=string.Empty; }
public sealed class ProduccionMaterialPendienteVm { public string Modulo { get; set; }=string.Empty; public long MovimientoID { get; set; } public string Codigo { get; set; }=string.Empty; public string Descripcion { get; set; }=string.Empty; public string Variante { get; set; }=string.Empty; public decimal Cantidad { get; set; } public string Unidad { get; set; }=string.Empty; public string EntregadoPor { get; set; }=string.Empty; public DateTime Fecha { get; set; } }

public sealed class ProduccionFlujoDetalleVm
{
    public ProduccionFlujoItemVm Ejecucion { get; set; } = new();
    public List<ProduccionEventoVm> Eventos { get; set; } = new();
    public List<ProduccionHoraVm> Capturas { get; set; } = new();
    public List<ProduccionParoVm> Paros { get; set; } = new();
    public List<ProduccionCajaVm> Cajas { get; set; } = new();
    public List<ProduccionPersonalVm> PersonalDisponible { get; set; } = new();
    public List<ProduccionMaterialPendienteVm> MaterialPendienteRecepcion { get; set; } = new();
    public bool UsuarioEsCalidad { get; set; }
}

public sealed class ProduccionHoraFormVm { public int EjecucionID { get; set; } public DateTime Fecha { get; set; }=DateTime.Today; public TimeSpan HoraInicio { get; set; } public TimeSpan HoraFin { get; set; } [Range(0,int.MaxValue)] public int OK { get; set; } [Range(0,int.MaxValue)] public int Sospechosa { get; set; } [Range(0,int.MaxValue)] public int Scrap { get; set; } public string Turno { get; set; }="1"; public string? EtiquetaInicial { get; set; } public string? EtiquetaFinal { get; set; } public string? Defecto { get; set; } public string? Observaciones { get; set; } }
