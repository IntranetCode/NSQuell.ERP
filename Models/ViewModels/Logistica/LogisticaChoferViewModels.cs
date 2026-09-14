using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace ERP.NSQuell.Models.ViewModels.Logistica;

public sealed class LogisticaChoferIndexVm
{
    public int UsuarioID { get; set; }
    public string Chofer { get; set; } = string.Empty;
    public DateTime Fecha { get; set; } = DateTime.Today;
    public List<LogisticaChoferViajeResumenVm> Viajes { get; set; } = new();

    public int TotalViajes => Viajes.Count;
    public int Programados => Viajes.Count(x => x.Estatus == "Programado");
    public int EnCurso => Viajes.Count(x => x.Estatus == "En curso");
    public int Finalizados => Viajes.Count(x => x.Estatus is "Finalizado" or "Completado");
    public bool TieneViajeEnCurso => Viajes.Any(x => x.Estatus == "En curso");
}

public sealed class LogisticaChoferViajeResumenVm
{
    public int ViajeID { get; set; }
    public string Folio { get; set; } = string.Empty;
    public string Estatus { get; set; } = string.Empty;
    public string Origen { get; set; } = string.Empty;
    public string Destino { get; set; } = string.Empty;
    public string Motivo { get; set; } = string.Empty;

    public DateTime FechaProgramada { get; set; }
    public TimeSpan? HoraSalidaProgramada { get; set; }

    public DateTime? FechaSalidaReal { get; set; }
    public DateTime? FechaRegresoReal { get; set; }

    public int? UnidadID { get; set; }
    public string Unidad { get; set; } = string.Empty;
    public string Placas { get; set; } = string.Empty;

    public int TotalEmbarques { get; set; }
    public int TotalPiezas { get; set; }

    public bool EsMultiParada { get; set; }
    public int TotalParadas { get; set; }
    public int ParadasCompletadas { get; set; }
    public int ParadasPendientes { get; set; }
    public int? ProximaParadaID { get; set; }
    public int? ProximaSecuencia { get; set; }
    public string ProximaTipoParada { get; set; } = string.Empty;
    public string ProximaOperacion { get; set; } = string.Empty;
    public string ProximaParada { get; set; } = string.Empty;
    public string ProximaDireccion { get; set; } = string.Empty;
    public DateTime? ProximaLlegadaProgramada { get; set; }
    public int EvidenciasSalida { get; set; }
    public int EvidenciasTrayecto { get; set; }
    public int EvidenciasEntrega { get; set; }
    public int EvidenciasRegreso { get; set; }

    public bool PuedeIniciar =>
        Estatus == "Programado"
        && !FechaSalidaReal.HasValue;

    public bool PuedeContinuar =>
        Estatus == "En curso"
        && FechaSalidaReal.HasValue
        && !FechaRegresoReal.HasValue;

    public bool Finalizado =>
        FechaRegresoReal.HasValue
        || Estatus is "Finalizado" or "Completado" or "Cancelado";

    public string HoraTexto =>
        FechaSalidaReal.HasValue
            ? FechaSalidaReal.Value.ToString("HH:mm")
            : HoraSalidaProgramada.HasValue
                ? HoraSalidaProgramada.Value.ToString(@"hh\:mm")
                : "Sin hora";
}

public sealed class LogisticaChoferViajeDetalleVm
{
    public int ViajeID { get; set; }
    public string Folio { get; set; } = string.Empty;
    public string Estatus { get; set; } = string.Empty;

    public string Origen { get; set; } = string.Empty;
    public string Destino { get; set; } = string.Empty;
    public string Motivo { get; set; } = string.Empty;

    public DateTime FechaProgramada { get; set; }
    public TimeSpan? HoraSalidaProgramada { get; set; }

    public DateTime? FechaSalidaReal { get; set; }
    public DateTime? FechaRegresoReal { get; set; }

    public int? KilometrajeSalida { get; set; }
    public int? KilometrajeRegreso { get; set; }

    public int? UnidadID { get; set; }
    public string Unidad { get; set; } = string.Empty;
    public string Placas { get; set; } = string.Empty;

    public int OperadorUsuarioID { get; set; }
    public string Operador { get; set; } = string.Empty;

    public bool TieneIncidencia { get; set; }
    public int IncidenciasAbiertas { get; set; }

    public List<LogisticaChoferEmbarqueVm> Embarques { get; set; } = new();
    public List<LogisticaChoferEvidenciaVm> Evidencias { get; set; } = new();

    public bool EsMultiParada { get; set; }
    public int TotalParadas { get; set; }
    public int ParadasCompletadas { get; set; }
    public int ParadasPendientes { get; set; }
    public List<LogisticaChoferParadaVm> Paradas { get; set; } = new();
    public LogisticaChoferParadaVm? ProximaParada => Paradas.Where(x => x.Activo && x.Estatus is "Pendiente" or "En camino" or "En sitio").OrderBy(x => x.Secuencia).ThenBy(x => x.ViajeParadaID).FirstOrDefault();

    public string? RowVersion { get; set; }

    public bool PuedeRegistrarSalida =>
        Estatus == "Programado"
        && !FechaSalidaReal.HasValue;

    public bool PuedeRegistrarRegreso =>
        Estatus == "En curso"
        && FechaSalidaReal.HasValue
        && !FechaRegresoReal.HasValue;

    public bool ViajeActivo =>
        Estatus is "Programado" or "En curso";

    public int? KilometrosRecorridos =>
        KilometrajeSalida.HasValue
        && KilometrajeRegreso.HasValue
        && KilometrajeRegreso.Value >= KilometrajeSalida.Value
            ? KilometrajeRegreso.Value - KilometrajeSalida.Value
            : null;

    public int EvidenciasSalida => Evidencias.Count(x => x.TipoEvidencia == "Salida");
    public int EvidenciasTrayecto => Evidencias.Count(x => x.TipoEvidencia == "Trayecto");
    public int EvidenciasEntrega => Evidencias.Count(x => x.TipoEvidencia == "Entrega");
    public int EvidenciasRegreso => Evidencias.Count(x => x.TipoEvidencia == "Regreso");
}

public sealed class LogisticaChoferEmbarqueVm
{
    public int EmbarqueID { get; set; }
    public string Folio { get; set; } = string.Empty;
    public string Cliente { get; set; } = string.Empty;
    public string Destino { get; set; } = string.Empty;
    public int TotalPiezas { get; set; }
    public int? OrdenEntrega { get; set; }

    public int? ViajeParadaID { get; set; }
    public int? SecuenciaParada { get; set; }
    public string Estatus { get; set; } = string.Empty;
}

public sealed class LogisticaChoferParadaVm
{
    public int ViajeParadaID { get; set; }
    public int ViajeID { get; set; }
    public int Secuencia { get; set; }
    public string TipoParada { get; set; } = string.Empty;
    public string TipoOperacion { get; set; } = string.Empty;
    public string Lugar { get; set; } = string.Empty;
    public string Direccion { get; set; } = string.Empty;
    public string ReferenciaTipo { get; set; } = string.Empty;
    public int? ReferenciaID { get; set; }
    public string ReferenciaFolioSnapshot { get; set; } = string.Empty;
    public DateTime? FechaHoraLlegadaProgramada { get; set; }
    public DateTime? FechaHoraSalidaProgramada { get; set; }
    public DateTime? FechaLlegadaReal { get; set; }
    public DateTime? FechaSalidaReal { get; set; }
    public string Estatus { get; set; } = "Pendiente";
    public bool RequiereEvidencia { get; set; }
    public bool CierraViaje { get; set; }
    public string ContactoNombre { get; set; } = string.Empty;
    public string ContactoTelefono { get; set; } = string.Empty;
    public string Observaciones { get; set; } = string.Empty;
    public bool Activo { get; set; } = true;
    public int TotalEmbarques { get; set; }
    public int EmbarquesEntregados { get; set; }
    public int TotalEvidencias { get; set; }
    public string? RowVersion { get; set; }
    public List<LogisticaChoferEmbarqueVm> Embarques { get; set; } = new();
    public bool EnSitio => Estatus == "En sitio";
    public bool Completada => Estatus == "Completada";
    public bool Pendiente => Estatus is "Pendiente" or "En camino";
    public string TipoParadaTexto => TipoParada switch { "Recoleccion" => "Recolección", "Entrega" => "Entrega", "Origen" => "Origen", "Retorno" => "Retorno", "Traslado" => "Traslado", "Servicio" => "Servicio", _ => TipoParada };
}
public sealed class LogisticaChoferRegistrarLlegadaParadaVm
{
    [Required, Range(1, int.MaxValue)]
    public int ViajeID { get; set; }
    [Required, Range(1, int.MaxValue)]
    public int ViajeParadaID { get; set; }
    [Required]
    public DateTime FechaLlegada { get; set; } = DateTime.Now;
    [StringLength(500)]
    public string? Observaciones { get; set; }
    public string? RowVersion { get; set; }
}
public sealed class LogisticaChoferCompletarParadaVm
{
    [Required, Range(1, int.MaxValue)]
    public int ViajeID { get; set; }
    [Required, Range(1, int.MaxValue)]
    public int ViajeParadaID { get; set; }
    [Required]
    public DateTime FechaSalida { get; set; } = DateTime.Now;
    public List<IFormFile> Fotos { get; set; } = new();
    [StringLength(1000)]
    public string? Observaciones { get; set; }
    public string? RowVersion { get; set; }
}
public sealed class LogisticaChoferOmitirParadaVm
{
    [Required, Range(1, int.MaxValue)]
    public int ViajeID { get; set; }
    [Required, Range(1, int.MaxValue)]
    public int ViajeParadaID { get; set; }
    [Required, StringLength(1000)]
    public string Motivo { get; set; } = string.Empty;
    public string? RowVersion { get; set; }
}
public sealed class LogisticaChoferEvidenciaVm
{
    public int ViajeEvidenciaID { get; set; }
    public int ViajeID { get; set; }
    public string TipoEvidencia { get; set; } = string.Empty;
    public string NombreOriginal { get; set; } = string.Empty;
    public string TipoContenido { get; set; } = string.Empty;
    public long TamanoBytes { get; set; }
    public string Observaciones { get; set; } = string.Empty;
    public DateTime FechaCarga { get; set; }

    public bool EsImagen =>
        TipoContenido.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
}

public sealed class LogisticaChoferRegistrarSalidaVm
{
    [Required]
    [Range(1, int.MaxValue)]
    public int ViajeID { get; set; }

    [Required]
    [Display(Name = "Fecha y hora de salida")]
    public DateTime FechaSalida { get; set; } = DateTime.Now;

    [Required(ErrorMessage = "Captura el kilometraje inicial.")]
    [Range(0, int.MaxValue, ErrorMessage = "El kilometraje inicial no es válido.")]
    [Display(Name = "Kilometraje inicial")]
    public int? KilometrajeInicial { get; set; }

    public List<IFormFile> Fotos { get; set; } = new();

    [StringLength(500)]
    public string? Observaciones { get; set; }

    public string? RowVersion { get; set; }
}

public sealed class LogisticaChoferRegistrarRegresoVm
{
    [Required]
    [Range(1, int.MaxValue)]
    public int ViajeID { get; set; }

    [Required]
    [Display(Name = "Fecha y hora de regreso")]
    public DateTime FechaRegreso { get; set; } = DateTime.Now;

    [Required(ErrorMessage = "Captura el kilometraje final.")]
    [Range(0, int.MaxValue, ErrorMessage = "El kilometraje final no es válido.")]
    [Display(Name = "Kilometraje final")]
    public int? KilometrajeFinal { get; set; }

    public List<IFormFile> Fotos { get; set; } = new();

    [StringLength(500)]
    public string? Observaciones { get; set; }

    public string? RowVersion { get; set; }
}

public sealed class LogisticaChoferSubirFotosVm
{
    [Required]
    [Range(1, int.MaxValue)]
    public int ViajeID { get; set; }

    [Required]
    [StringLength(30)]
    public string TipoEvidencia { get; set; } = "Trayecto";

    public List<IFormFile> Fotos { get; set; } = new();

    [StringLength(500)]
    public string? Observaciones { get; set; }
}