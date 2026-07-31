using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ERP.NSQuell.Models
{
    public sealed class PlaneacionCalendarioMaquinasVm
    {
        public PlaneacionCalendarioMaquinasVm()
        {
            Vista = "semana";
            Ahora = DateTime.Now;
            Maquinas = new List<PlaneacionCalendarioMaquinaVm>();
        }

        public string Vista { get; set; }
        public DateTime InicioPeriodo { get; set; }
        public DateTime FinPeriodo { get; set; }
        public DateTime? FechaReferencia { get; set; }
        public DateTime? RangoInicio { get; set; }
        public DateTime? RangoFin { get; set; }

        public DateTime InicioSemana
        {
            get { return InicioPeriodo; }
            set { InicioPeriodo = value; }
        }

        public DateTime FinSemana
        {
            get { return FinPeriodo; }
            set { FinPeriodo = value; }
        }

        public DateTime Ahora { get; set; }
        public List<PlaneacionCalendarioMaquinaVm> Maquinas { get; set; }

        public int TotalMaquinas { get { return Maquinas.Count; } }
        public int TotalBloques { get { return Maquinas.Sum(x => x.Bloques.Count); } }
        public int TrabajandoAhora { get { return Maquinas.Count(x => x.Bloques.Any(b => b.EstaEnLinea)); } }

        public string VistaNormalizada
        {
            get { return string.IsNullOrWhiteSpace(Vista) ? "semana" : Vista.Trim().ToLowerInvariant(); }
        }

        public bool EsVistaDia { get { return VistaNormalizada == "dia"; } }
        public bool EsVistaSemana { get { return VistaNormalizada == "semana"; } }
        public bool EsVistaMes { get { return VistaNormalizada == "mes"; } }
        public bool EsVistaRango { get { return VistaNormalizada == "rango"; } }

        public int TotalDiasPeriodo
        {
            get
            {
                if (FinPeriodo <= InicioPeriodo) return 1;
                return Math.Max(1, (int)Math.Ceiling((FinPeriodo.Date - InicioPeriodo.Date).TotalDays));
            }
        }

        public DateTime PeriodoAnterior
        {
            get
            {
                if (EsVistaDia) return InicioPeriodo.AddDays(-1);
                if (EsVistaMes) return InicioPeriodo.AddMonths(-1);
                if (EsVistaRango) return InicioPeriodo.AddDays(-TotalDiasPeriodo);
                return InicioPeriodo.AddDays(-7);
            }
        }

        public DateTime PeriodoSiguiente
        {
            get
            {
                if (EsVistaDia) return InicioPeriodo.AddDays(1);
                if (EsVistaMes) return InicioPeriodo.AddMonths(1);
                if (EsVistaRango) return InicioPeriodo.AddDays(TotalDiasPeriodo);
                return InicioPeriodo.AddDays(7);
            }
        }

        public string TituloPeriodo
        {
            get
            {
                var cultura = new CultureInfo("es-MX");
                if (EsVistaDia) return string.Format("Día {0:dd/MM/yyyy}", InicioPeriodo);
                if (EsVistaMes) return "Mes " + InicioPeriodo.ToString("MMMM yyyy", cultura);
                if (EsVistaRango) return string.Format("Rango {0:dd/MM/yyyy} al {1:dd/MM/yyyy}", InicioPeriodo, FinPeriodo.AddDays(-1));
                return string.Format("Semana {0:dd/MM/yyyy} al {1:dd/MM/yyyy}", InicioPeriodo, FinPeriodo.AddDays(-1));
            }
        }

        public string TextoVista
        {
            get
            {
                if (EsVistaDia) return "Día";
                if (EsVistaMes) return "Mes";
                if (EsVistaRango) return "Rango";
                return "Semana";
            }
        }
    }

    public sealed class PlaneacionCalendarioMaquinaVm
    {
        public PlaneacionCalendarioMaquinaVm()
        {
            Codigo = string.Empty;
            Nombre = string.Empty;
            Carriles = 1;
            Bloques = new List<PlaneacionCalendarioBloqueVm>();
        }

        public int MaquinaID { get; set; }
        public string Codigo { get; set; }
        public string Nombre { get; set; }
        public int Carriles { get; set; }
        public List<PlaneacionCalendarioBloqueVm> Bloques { get; set; }
    }

    public sealed class PlaneacionCalendarioBloqueVm
    {
        public PlaneacionCalendarioBloqueVm()
        {
            MaquinaCodigo = string.Empty;
        }

        public int ProgramaProduccionID { get; set; }
        public int? SolicitudProduccionID { get; set; }
        public int MaquinaID { get; set; }
        public string MaquinaCodigo { get; set; }
        public string ClienteNombre { get; set; }
        public string NumeroParte { get; set; }
        public string ReferenciaSAP { get; set; }
        public string Descripcion { get; set; }
        public string MoldeCodigo { get; set; }
        public int CantidadProgramada { get; set; }
        public int CantidadProducida { get; set; }
        public int CantidadPendiente { get { return Math.Max(0, CantidadProgramada - CantidadProducida); } }
        public DateTime Inicio { get; set; }
        public DateTime Fin { get; set; }
        public decimal HorasProgramadas { get; set; }
        public TimeSpan? Cambio { get; set; }
        public TimeSpan? Arranque { get; set; }
        public int EstatusID { get; set; }
        public int Carril { get; set; }
        public bool EstaEnLinea { get; set; }
        public int? MaquinaPrincipalID { get; set; }
        public string MaquinaPrincipalCodigo { get; set; }
        public string MaquinaPrincipalNombre { get; set; }
        public int? MaquinaSustitutaID { get; set; }
        public string MaquinaSustitutaCodigo { get; set; }
        public string MaquinaSustitutaNombre { get; set; }
        public int? EjecucionProduccionID { get; set; }
        public int? EstatusProduccionID { get; set; }
        public string EstatusProduccionNombre { get; set; }
        public int? OperadorProgramadoID { get; set; }
        public string OperadorProgramadoNombre { get; set; }
        public string TurnoProgramadoNombre { get; set; }
        public string TurnoProgramadoColor { get; set; }
        public int? EscalaAsignacionID { get; set; }

        public bool TieneOperadorProgramado { get { return OperadorProgramadoID.HasValue && !string.IsNullOrWhiteSpace(OperadorProgramadoNombre); } }
        public string TextoOperadorProgramado { get { return TieneOperadorProgramado ? OperadorProgramadoNombre : "Sin operador asignado en escala"; } }
        public string TextoTurnoProgramado { get { return string.IsNullOrWhiteSpace(TurnoProgramadoNombre) ? "Sin turno" : TurnoProgramadoNombre; } }
        public bool YaProducido { get { return CantidadProgramada > 0 && CantidadProducida >= CantidadProgramada; } }
        public bool EstaTerminadoOCerrado { get { return EstatusID == PlaneacionProgramaEstatus.Terminado || EstatusID == PlaneacionProgramaEstatus.Cerrado || EstatusID == 99; } }
        public bool EstaPreparacionOPausado { get { return EstatusID == 2 || EstatusID == 4 || EstatusProduccionID == 2 || EstatusProduccionID == 4; } }
        public bool EstaProduciendo { get { return EstaEnLinea || EstatusID == PlaneacionProgramaEstatus.EnProduccion || EstatusProduccionID == 3; } }
        public bool PuedeMoverCalendario { get { return !EstaPreparacionOPausado && !EstaProduciendo && !YaProducido && !EstaTerminadoOCerrado; } }

        public string ClaseSemaforoCalendario
        {
            get
            {
                if (EstatusID == 99 || EstatusID == PlaneacionProgramaEstatus.Cerrado) return "bloque-cerrado";
                if (YaProducido || EstatusID == PlaneacionProgramaEstatus.Terminado) return "bloque-producido";
                if (EstaProduciendo || EstaPreparacionOPausado) return "bloque-produciendo";
                return "bloque-timeline";
            }
        }

        public string MaquinaSugeridaTexto
        {
            get
            {
                var principal = string.IsNullOrWhiteSpace(MaquinaPrincipalCodigo) ? "Sin máquina principal" : MaquinaPrincipalCodigo;
                var sustituta = string.IsNullOrWhiteSpace(MaquinaSustitutaCodigo) ? "Sin máquina sustituta" : MaquinaSustitutaCodigo;
                return "Principal: " + principal + " | Sustituta: " + sustituta;
            }
        }

        public string OFTexto { get { return SolicitudProduccionID.HasValue ? "OF " + SolicitudProduccionID.Value : "Programa " + ProgramaProduccionID; } }
        public string ParteTexto
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(ReferenciaSAP)) return ReferenciaSAP;
                if (!string.IsNullOrWhiteSpace(NumeroParte)) return NumeroParte;
                return "Sin parte";
            }
        }

        public string EstatusTexto
        {
            get
            {
                if (YaProducido) return "Ya producido";
                if (EstaEnLinea) return "En línea";
                switch (EstatusID)
                {
                    case 1: return "Programado";
                    case 2: return "En preparación";
                    case 3: return "En producción";
                    case 4: return "Pausado";
                    case 5: return "Terminado";
                    case 9: return "Cerrado";
                    case 99: return "Cancelado";
                    default: return "Estatus " + EstatusID;
                }
            }
        }

        public string EstadoSemaforoTexto
        {
            get
            {
                if (EstatusID == 99 || EstatusID == PlaneacionProgramaEstatus.Cerrado) return "Cerrado / cancelado";
                if (YaProducido || EstatusID == PlaneacionProgramaEstatus.Terminado) return "Ya producido";
                if (EstaPreparacionOPausado) return "En preparación / pausado";
                if (EstaProduciendo) return "Produciendo";
                return "Timeline";
            }
        }

        public string MotivoBloqueoMovimiento
        {
            get
            {
                if (EstaPreparacionOPausado) return "Este programa ya está en preparación o pausado. No puede moverse desde Planeación.";
                if (EstaProduciendo) return "Este programa está en línea o producción. No puede moverse hasta que esté listo el módulo de Producción.";
                if (YaProducido) return "Este programa ya fue producido. No puede moverse desde el calendario.";
                if (EstaTerminadoOCerrado) return "Este programa está terminado, cerrado o cancelado. No puede moverse.";
                return string.Empty;
            }
        }

        public string ClaseEstado
        {
            get
            {
                switch (EstatusID)
                {
                    case 2: return "bloque-preparacion";
                    case 3: return "bloque-produccion";
                    case 4: return "bloque-pausado";
                    case 5: return "bloque-terminado";
                    case 9: return "bloque-terminado";
                    case 99: return "bloque-terminado";
                    default: return "bloque-programado";
                }
            }
        }
    }
}
