using Microsoft.AspNetCore.Mvc.Rendering;

namespace ERP.NSQuell.Models
{
    public class PlaneacionPanoramaVm
    {
        public string Vista { get; set; } = "SEMANA";

        public DateTime FechaDesde { get; set; } = DateTime.Today;
        public DateTime FechaHasta { get; set; } = DateTime.Today.AddDays(7);

        public int? ClienteID { get; set; }
        public int? MaquinaID { get; set; }
        public int? ParteID { get; set; }
        public int? EstatusID { get; set; }

        public List<SelectListItem>
    Clientes
        { get; set; } = new();
        public List<SelectListItem>
            Maquinas
        { get; set; } = new();
        public List<SelectListItem>
            Partes
        { get; set; } = new();
        public List<SelectListItem>
            Estatus
        { get; set; } = new();

        public List<PlaneacionPanoramaClienteVm>
            ClientesPanorama
        { get; set; } = new();

        public int TotalClientes => ClientesPanorama.Count;

        public int TotalPartes =>
        ClientesPanorama.Sum(c => c.Partes.Count);

        public int TotalRenglones =>
        ClientesPanorama.Sum(c => c.Partes.Sum(p => p.Renglones.Count));

        public int TotalCantidadRequerida =>
        ClientesPanorama.Sum(c => c.Partes.Sum(p => p.TotalCantidadRequerida));

        public int TotalPiezasDesdePT =>
        ClientesPanorama.Sum(c => c.Partes.Sum(p => p.TotalPiezasDesdePT));

        public int TotalCantidadProgramada =>
        ClientesPanorama.Sum(c => c.Partes.Sum(p => p.TotalCantidadProgramada));

        public int TotalCantidadProducida =>
        ClientesPanorama.Sum(c => c.Partes.Sum(p => p.TotalCantidadProducida));

        public int TotalCantidadPendiente =>
        ClientesPanorama.Sum(c => c.Partes.Sum(p => p.TotalCantidadPendiente));

        public decimal TotalHorasProgramadas =>
        ClientesPanorama.Sum(c => c.Partes.Sum(p => p.TotalHorasProgramadas));

        public decimal TotalMpKg =>
        ClientesPanorama.Sum(c => c.Partes.Sum(p => p.TotalMpKg));

        public decimal TotalEmbalajes =>
        ClientesPanorama.Sum(c => c.Partes.Sum(p => p.TotalEmbalajes));

        public decimal TotalEtiquetas =>
        ClientesPanorama.Sum(c => c.Partes.Sum(p => p.TotalEtiquetas));

        public int TotalConOF =>
        ClientesPanorama.Sum(c => c.Partes.Sum(p => p.Renglones.Count(x => x.TieneOF)));

        public int TotalSinOF =>
        ClientesPanorama.Sum(c => c.Partes.Sum(p => p.Renglones.Count(x => !x.TieneOF)));

        public decimal PorcentajeAvance
        {
            get
            {
                if (TotalCantidadRequerida <= 0)
                    return 0;

                return Math.Round(((decimal)TotalCantidadProducida / TotalCantidadRequerida) * 100, 2);
            }
        }

        public decimal PorcentajeProgramado
        {
            get
            {
                if (TotalCantidadRequerida <= 0)
                    return 0;

                return Math.Round(((decimal)TotalCantidadProgramada / TotalCantidadRequerida) * 100, 2);
            }
        }
    }

    public class PlaneacionPanoramaClienteVm
    {
        public int? ClienteID { get; set; }
        public string ClienteNombre { get; set; } = "Sin cliente";

        public List<PlaneacionPanoramaParteVm>
            Partes
        { get; set; } = new();

        public int TotalPartes => Partes.Count;

        public int TotalRenglones =>
        Partes.Sum(p => p.Renglones.Count);

        public int TotalCantidadRequerida =>
        Partes.Sum(p => p.TotalCantidadRequerida);

        public int TotalPiezasDesdePT =>
        Partes.Sum(p => p.TotalPiezasDesdePT);

        public int TotalCantidadProgramada =>
        Partes.Sum(p => p.TotalCantidadProgramada);

        public int TotalCantidadProducida =>
        Partes.Sum(p => p.TotalCantidadProducida);

        public int TotalCantidadPendiente =>
        Partes.Sum(p => p.TotalCantidadPendiente);

        public decimal TotalHorasProgramadas =>
        Partes.Sum(p => p.TotalHorasProgramadas);

        public decimal TotalMpKg =>
        Partes.Sum(p => p.TotalMpKg);

        public decimal TotalEmbalajes =>
        Partes.Sum(p => p.TotalEmbalajes);

        public decimal TotalEtiquetas =>
        Partes.Sum(p => p.TotalEtiquetas);

        public int TotalConOF =>
        Partes.Sum(p => p.Renglones.Count(x => x.TieneOF));

        public int TotalSinOF =>
        Partes.Sum(p => p.Renglones.Count(x => !x.TieneOF));

        public decimal PorcentajeAvance
        {
            get
            {
                if (TotalCantidadRequerida <= 0)
                    return 0;

                return Math.Round(((decimal)TotalCantidadProducida / TotalCantidadRequerida) * 100, 2);
            }
        }

        public decimal PorcentajeProgramado
        {
            get
            {
                if (TotalCantidadRequerida <= 0)
                    return 0;

                return Math.Round(((decimal)TotalCantidadProgramada / TotalCantidadRequerida) * 100, 2);
            }
        }
    }

    public class PlaneacionPanoramaParteVm
    {
        public int? ClienteID { get; set; }
        public string? ClienteNombre { get; set; }

        public int? ParteID { get; set; }
        public string? NumeroParte { get; set; }
        public string? ReferenciaSAP { get; set; }
        public string? DesignacionDescripcionSAP { get; set; }

        public int? MaquinaID { get; set; }
        public string? MaquinaCodigo { get; set; }
        public string? MaquinaNombre { get; set; }

        public int? MoldeID { get; set; }
        public string? MoldeCodigo { get; set; }

        public string? Color { get; set; }

        public int? Cavidades { get; set; }
        public string? Ciclo { get; set; }
        public int? ObjetivoHora { get; set; }
        public decimal? PesoBrutoPieza { get; set; }

        public int? MaterialID { get; set; }
        public string? MaterialCodigo { get; set; }
        public string? MaterialDescripcion { get; set; }

        public string? EmbalajeCodigo { get; set; }
        public string? EmbalajeDescripcion { get; set; }
        public decimal? PiezasPorEmbalaje { get; set; }

        public List<PlaneacionPanoramaRenglonVm>
            Renglones
        { get; set; } = new();

        public int TotalCantidadRequerida =>
        Renglones.Sum(x => x.CantidadRequerida);

        public int TotalPiezasDesdePT =>
        Renglones.Sum(x => x.PiezasDesdePT);

        public int TotalCantidadProgramada =>
        Renglones.Sum(x => x.CantidadProgramada);

        public int TotalCantidadProducida =>
        Renglones.Sum(x => x.CantidadProducida);

        public int TotalCantidadPendiente =>
        Renglones.Sum(x => x.CantidadPendiente);

        public decimal TotalHorasProgramadas =>
        Renglones.Sum(x => x.HorasProgramadas ?? 0);

        public decimal TotalMpKg =>
        Renglones.Sum(x => x.CantidadMpKg ?? 0);

        public decimal TotalEmbalajes =>
        Renglones.Sum(x => x.CantidadEmbalajes ?? 0);

        public decimal TotalEtiquetas =>
        TotalEmbalajes;

        public int TotalConOF =>
        Renglones.Count(x => x.TieneOF);

        public int TotalSinOF =>
        Renglones.Count(x => !x.TieneOF);

        public DateTime? PrimeraFecha =>
        Renglones
        .Where(x => x.FechaInicioProgramada.HasValue)
        .OrderBy(x => x.FechaInicioProgramada)
        .Select(x => x.FechaInicioProgramada)
        .FirstOrDefault();

        public DateTime? UltimaFecha =>
        Renglones
        .Where(x => x.FechaFinProgramada.HasValue || x.FechaInicioProgramada.HasValue)
        .OrderByDescending(x => x.FechaFinProgramada ?? x.FechaInicioProgramada)
        .Select(x => x.FechaFinProgramada ?? x.FechaInicioProgramada)
        .FirstOrDefault();

        public decimal PorcentajeAvance
        {
            get
            {
                if (TotalCantidadRequerida <= 0)
                    return 0;

                return Math.Round(((decimal)TotalCantidadProducida / TotalCantidadRequerida) * 100, 2);
            }
        }

        public decimal PorcentajeProgramado
        {
            get
            {
                if (TotalCantidadRequerida <= 0)
                    return 0;

                return Math.Round(((decimal)TotalCantidadProgramada / TotalCantidadRequerida) * 100, 2);
            }
        }
    }

    public class PlaneacionPanoramaRenglonVm
    {
        public int? ProgramaProduccionID { get; set; }

        public int? ReleaseID { get; set; }
        public int? ReleaseDetalleID { get; set; }
        public string? FolioRelease { get; set; }

        public int? SolicitudProduccionID { get; set; }
        public int? SolicitudProduccionDetalleID { get; set; }

        public int? ClienteID { get; set; }
        public string? ClienteNombre { get; set; }

        public int? ParteID { get; set; }
        public string? NumeroParte { get; set; }
        public string? ReferenciaSAP { get; set; }
        public string? DesignacionDescripcionSAP { get; set; }

        public int CantidadRequerida { get; set; }
        public int PiezasDesdePT { get; set; }
        public int CantidadProgramada { get; set; }
        public int CantidadProducida { get; set; }
        public int CantidadPendiente { get; set; }

        public int? MaquinaID { get; set; }
        public string? MaquinaCodigo { get; set; }
        public string? MaquinaNombre { get; set; }

        public int? MoldeID { get; set; }
        public string? MoldeCodigo { get; set; }

        public string? Color { get; set; }

        public int? Cavidades { get; set; }
        public string? Ciclo { get; set; }
        public int? ObjetivoHora { get; set; }
        public decimal? PesoBrutoPieza { get; set; }

        public int? MaterialID { get; set; }
        public string? MaterialCodigo { get; set; }
        public string? MaterialDescripcion { get; set; }
        public decimal? CantidadMpKg { get; set; }

        public string? EmbalajeCodigo { get; set; }
        public string? EmbalajeDescripcion { get; set; }
        public decimal? PiezasPorEmbalaje { get; set; }
        public decimal? CantidadEmbalajes { get; set; }

        public string? CondicionProduccion { get; set; }
        public int? SecuenciaMaquina { get; set; }

        public DateTime? FechaInicioProgramada { get; set; }
        public DateTime? FechaFinProgramada { get; set; }
        public decimal? HorasProgramadas { get; set; }

        public TimeSpan? Cambio { get; set; }
        public TimeSpan? Arranque { get; set; }

        public int EstatusID { get; set; }
        public string EstatusNombre => PlaneacionProgramaEstatus.Nombre(EstatusID);

        public string? Observaciones { get; set; }
        public DateTime FechaCreacion { get; set; }

        public DateTime? FechaCarga { get; set; }
        public DateTime? FechaRequerida { get; set; }

        public int? ProgramaProduccionIDRelacionado { get; set; }

        public bool EstaProgramado =>
            ProgramaProduccionIDRelacionado.HasValue || ProgramaProduccionID > 0;

        public bool EstaPendienteProgramar =>
            !EstaProgramado && CantidadPendiente > 0;

        public string EstadoPanorama
        {
            get
            {
                if (TieneOF)
                    return "Con OF";

                if (EstaProgramado)
                    return "Programado";

                if (EstaPendienteProgramar)
                    return "Pendiente programar";

                return "Sin pendiente";
            }
        }

        public string ClaseEstadoPanorama
        {
            get
            {
                return EstadoPanorama switch
                {
                    "Con OF" => "badge-ok",
                    "Programado" => "badge-info",
                    "Pendiente programar" => "badge-warn",
                    _ => "badge-done"
                };
            }
        }

        public bool TieneOF => SolicitudProduccionID.HasValue || SolicitudProduccionDetalleID.HasValue;

        public string EstadoOF => TieneOF ? "Con OF" : "Sin OF";

        public decimal PorcentajeAvance
        {
            get
            {
                if (CantidadRequerida <= 0)
                    return 0;

                return Math.Round(((decimal)CantidadProducida / CantidadRequerida) * 100, 2);
            }
        }

        public decimal PorcentajeProgramado
        {
            get
            {
                if (CantidadRequerida <= 0)
                    return 0;

                return Math.Round(((decimal)CantidadProgramada / CantidadRequerida) * 100, 2);
            }
        }
    }

    public static class PlaneacionPanoramaVista
    {
        public const string Dia = "DIA";
        public const string Semana = "SEMANA";
        public const string Mes = "MES";
        public const string Anio = "ANIO";
        public const string LargoPlazo = "LARGO";

        public static string Nombre(string? vista)
        {
            return vista switch
            {
                Dia => "Diario",
                Semana => "Semanal",
                Mes => "Mensual",
                Anio => "Anual",
                LargoPlazo => "Largo plazo",
                _ => "Semanal"
            };
        }

        public static List<SelectListItem>
            SelectList()
        {
            return new List<SelectListItem>
                                    {
                                    new SelectListItem
                                    {
                                    Value = Dia,
                                    Text = "Diario"
                                    },
                                    new SelectListItem
                                    {
                                    Value = Semana,
                                    Text = "Semanal"
                                    },
                                    new SelectListItem
                                    {
                                    Value = Mes,
                                    Text = "Mensual"
                                    },
                                    new SelectListItem
                                    {
                                    Value = Anio,
                                    Text = "Anual"
                                    },
                                    new SelectListItem
                                    {
                                    Value = LargoPlazo,
                                    Text = "Largo plazo"
                                    }
                                    };
        }
    }
}
