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
            get => InicioPeriodo;
            set => InicioPeriodo = value;
        }

        public DateTime FinSemana
        {
            get => FinPeriodo;
            set => FinPeriodo = value;
        }

        public DateTime Ahora { get; set; }
        public List<PlaneacionCalendarioMaquinaVm> Maquinas { get; set; }

        public int TotalMaquinas => Maquinas.Count;
        public int TotalBloques => Maquinas.Sum(x => x.Bloques.Count);

        public int TrabajandoAhora =>
            Maquinas.Count(x => x.Bloques.Any(b => b.EstaEnLinea));

        public string VistaNormalizada =>
            string.IsNullOrWhiteSpace(Vista)
                ? "semana"
                : Vista.Trim().ToLowerInvariant();

        public bool EsVistaDia => VistaNormalizada == "dia";
        public bool EsVistaSemana => VistaNormalizada == "semana";
        public bool EsVistaMes => VistaNormalizada == "mes";
        public bool EsVistaRango => VistaNormalizada == "rango";

        public int TotalDiasPeriodo
        {
            get
            {
                if (FinPeriodo <= InicioPeriodo)
                    return 1;

                return Math.Max(
                    1,
                    (int)Math.Ceiling(
                        (FinPeriodo.Date - InicioPeriodo.Date).TotalDays));
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

                if (EsVistaDia)
                    return $"Día {InicioPeriodo:dd/MM/yyyy}";

                if (EsVistaMes)
                    return "Mes " + InicioPeriodo.ToString("MMMM yyyy", cultura);

                if (EsVistaRango)
                {
                    return
                        $"Rango {InicioPeriodo:dd/MM/yyyy} al " +
                        $"{FinPeriodo.AddDays(-1):dd/MM/yyyy}";
                }

                return
                    $"Semana {InicioPeriodo:dd/MM/yyyy} al " +
                    $"{FinPeriodo.AddDays(-1):dd/MM/yyyy}";
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

    public sealed class PlaneacionInterrupcionUrgenteRequest
    {
        public int ProgramaUrgenteID { get; set; }
        public int MaquinaID { get; set; }
        public string Motivo { get; set; } = string.Empty;
        public bool TrabajarDomingo { get; set; }
    }
    public sealed class PlaneacionCalendarioBloqueVm
    {
        public PlaneacionCalendarioBloqueVm()
        {
            MaquinaCodigo = string.Empty;
            ClienteNombre = string.Empty;
            NumeroParte = string.Empty;
            ReferenciaSAP = string.Empty;
            Descripcion = string.Empty;
            MoldeCodigo = string.Empty;
            MaquinaPrincipalCodigo = string.Empty;
            MaquinaPrincipalNombre = string.Empty;
            MaquinaSustitutaCodigo = string.Empty;
            MaquinaSustitutaNombre = string.Empty;
            EstatusProduccionNombre = string.Empty;
            OperadorProgramadoNombre = string.Empty;
            OperadorAuxiliarProgramadoNombre = string.Empty;
            OperadorRealNombre = string.Empty;
            TurnoProgramadoNombre = string.Empty;
            TurnoProgramadoColor = string.Empty;
            EstadoCalidad = string.Empty;
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

        public int CantidadPendiente =>
            Math.Max(0, CantidadProgramada - CantidadProducida);

        public DateTime Inicio { get; set; }
        public DateTime Fin { get; set; }
        public decimal HorasProgramadas { get; set; }
        public TimeSpan? Cambio { get; set; }
        public TimeSpan? Arranque { get; set; }

        public DateTime? InicioProyectado { get; set; }
        public DateTime? FinProyectado { get; set; }
        public bool EsProgramaRaizInterrupcion { get; set; }
        public int? ParoProyeccionID { get; set; }
        public string TipoInterrupcionProyectada { get; set; } = string.Empty;
        public string MotivoInterrupcionProyectada { get; set; } = string.Empty;
        public int MinutosImpactoInterrupcion { get; set; }
        public int MinutosDesplazamientoProyectado { get; set; }
        public bool TieneProyeccionInterrupcion => InicioProyectado.HasValue || FinProyectado.HasValue;
        public DateTime InicioVisual => InicioProyectado ?? Inicio;
        public DateTime FinVisual => FinProyectado ?? Fin;

        public int EstatusID { get; set; }
        public int Carril { get; set; }

        public bool EstaEnLinea { get; set; }


        public bool DentroHorarioProgramado { get; set; }

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

        public int? OperadorAuxiliarProgramadoID { get; set; }
        public string OperadorAuxiliarProgramadoNombre { get; set; }

        public int? OperadorRealID { get; set; }
        public string OperadorRealNombre { get; set; }

        public string TurnoProgramadoNombre { get; set; }
        public string TurnoProgramadoColor { get; set; }
        public int? EscalaAsignacionID { get; set; }

        public int? InspeccionCalidadID { get; set; }
        public string EstadoCalidad { get; set; }
        public bool ConfiguracionCalidadInvalidada { get; set; }
        public bool RequiereReliberacion { get; set; }

        public bool MostrarAlertaNoInicio { get; set; }
        public bool AlertaNoInicioCritica { get; set; }
        public int MinutosAtrasoInicio { get; set; }
        public string TextoAlertaNoInicio { get; set; } = string.Empty;

        public bool TieneOperadorProgramado =>
            OperadorProgramadoID.HasValue &&
            !string.IsNullOrWhiteSpace(OperadorProgramadoNombre);

        public bool TieneOperadorReal =>
            OperadorRealID.HasValue ||
            !string.IsNullOrWhiteSpace(OperadorRealNombre);

        public bool TieneEjecucionProduccion =>
            EjecucionProduccionID.HasValue;

        public bool TieneProcesoCalidad =>
            InspeccionCalidadID.HasValue;

        public string TextoOperadorProgramado =>
            TieneOperadorProgramado
                ? OperadorProgramadoNombre
                : "Sin operador planeado";

        public string TextoOperadorAuxiliarProgramado =>
            string.IsNullOrWhiteSpace(OperadorAuxiliarProgramadoNombre)
                ? "Sin auxiliar planeado"
                : OperadorAuxiliarProgramadoNombre;

        public string TextoOperadorReal =>
            TieneOperadorReal
                ? OperadorRealNombre
                : "Sin operador real registrado";

        public string TextoOperadorVisible =>
            TieneOperadorReal
                ? TextoOperadorReal
                : TextoOperadorProgramado;

        public string TextoTurnoProgramado =>
            string.IsNullOrWhiteSpace(TurnoProgramadoNombre)
                ? "Sin turno"
                : TurnoProgramadoNombre;

        public bool YaProducido =>
            CantidadProgramada > 0 &&
            CantidadProducida >= CantidadProgramada;

        public bool EstaPreparacionOPausado =>
            EstatusID == ProduccionEstatus.EnPreparacion ||
            EstatusID == ProduccionEstatus.Pausado ||
            EstatusProduccionID == ProduccionEstatus.EnPreparacion ||
            EstatusProduccionID == ProduccionEstatus.Pausado;

        public bool EstaProduciendo =>
            EstaEnLinea ||
            EstatusID == ProduccionEstatus.EnProduccion ||
            EstatusProduccionID == ProduccionEstatus.EnProduccion;

        public bool EstaTerminadoOCerrado =>
            EstatusID == ProduccionEstatus.TerminadoParcial ||
            EstatusID == ProduccionEstatus.Terminado ||
            EstatusID == ProduccionEstatus.Cerrado ||
            EstatusID == ProduccionEstatus.Cancelado ||
            EstatusProduccionID == ProduccionEstatus.TerminadoParcial ||
            EstatusProduccionID == ProduccionEstatus.Terminado ||
            EstatusProduccionID == ProduccionEstatus.Cerrado ||
            EstatusProduccionID == ProduccionEstatus.Cancelado;

        /// <summary>
        /// La vista aplica este bloqueo, pero el controlador vuelve a validar
        /// Producción y Calidad dentro de la transacción.
        /// </summary>
        public bool PuedeMoverCalendario =>
            EstatusID == ProduccionEstatus.Pendiente &&
            !TieneEjecucionProduccion &&
            !TieneProcesoCalidad &&
            !YaProducido;

        public string ClaseSemaforoCalendario
        {
            get
            {
                if (EstatusID == ProduccionEstatus.Cancelado ||
                    EstatusID == ProduccionEstatus.Cerrado)
                {
                    return "bloque-cerrado";
                }

                if (YaProducido ||
                    EstatusID == ProduccionEstatus.TerminadoParcial ||
                    EstatusID == ProduccionEstatus.Terminado)
                {
                    return "bloque-producido";
                }

                if (EstaProduciendo || EstaPreparacionOPausado)
                    return "bloque-produciendo";

                if (TieneProcesoCalidad)
                    return "bloque-calidad";

                return "bloque-timeline";
            }
        }

        public string MaquinaSugeridaTexto
        {
            get
            {
                var principal = string.IsNullOrWhiteSpace(MaquinaPrincipalCodigo)
                    ? "Sin máquina principal"
                    : MaquinaPrincipalCodigo;

                var sustituta = string.IsNullOrWhiteSpace(MaquinaSustitutaCodigo)
                    ? "Sin máquina sustituta"
                    : MaquinaSustitutaCodigo;

                return $"Principal: {principal} | Sustituta: {sustituta}";
            }
        }

        public string OFTexto =>
            SolicitudProduccionID.HasValue
                ? $"OF {SolicitudProduccionID.Value}"
                : $"Programa {ProgramaProduccionID}";

        public string ParteTexto
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(ReferenciaSAP))
                    return ReferenciaSAP;

                if (!string.IsNullOrWhiteSpace(NumeroParte))
                    return NumeroParte;

                return "Sin parte";
            }
        }

        public string EstatusTexto
        {
            get
            {
                if (YaProducido)
                    return "Ya producido";

                return EstatusID switch
                {
                    ProduccionEstatus.Pendiente => "Programado",
                    ProduccionEstatus.EnPreparacion => "En preparación",
                    ProduccionEstatus.EnProduccion => "En producción",
                    ProduccionEstatus.Pausado => "Pausado",
                    ProduccionEstatus.TerminadoParcial => "Terminado parcial",
                    ProduccionEstatus.Terminado => "Terminado",
                    ProduccionEstatus.Cerrado => "Cerrado",
                    ProduccionEstatus.Cancelado => "Cancelado",
                    _ => $"Estatus {EstatusID}"
                };
            }
        }

        public string EstadoCalidadTexto =>
            string.IsNullOrWhiteSpace(EstadoCalidad)
                ? "Sin proceso de Calidad"
                : EstadoCalidad.Replace("_", " ");

        public string EstadoSemaforoTexto
        {
            get
            {
                if (EstatusID == ProduccionEstatus.Cancelado ||
                    EstatusID == ProduccionEstatus.Cerrado)
                {
                    return "Cerrado / cancelado";
                }

                if (YaProducido ||
                    EstatusID == ProduccionEstatus.TerminadoParcial ||
                    EstatusID == ProduccionEstatus.Terminado)
                {
                    return "Ya producido";
                }

                if (EstaPreparacionOPausado)
                    return "En preparación / pausado";

                if (EstaProduciendo)
                    return "Produciendo";

                if (TieneProcesoCalidad)
                    return "En proceso de Calidad";

                if (DentroHorarioProgramado)
                    return "Dentro del horario planeado";

                return "Timeline";
            }
        }

        public string MotivoBloqueoMovimiento
        {
            get
            {
                if (TieneEjecucionProduccion)
                {
                    return
                        "Este programa ya tiene una ejecución de Producción. " +
                        "La máquina y las fechas deben conservarse.";
                }

                if (TieneProcesoCalidad)
                {
                    return
                        $"Este programa ya tiene un proceso de Calidad " +
                        $"({EstadoCalidadTexto}). No puede reprogramarse.";
                }

                if (EstatusID != ProduccionEstatus.Pendiente)
                {
                    return
                        "Solo los programas en estado Programado pueden " +
                        "moverse desde Planeación.";
                }

                if (YaProducido)
                    return "Este programa ya fue producido.";

                return string.Empty;
            }
        }

        public string ClaseEstado
        {
            get
            {
                return EstatusID switch
                {
                    ProduccionEstatus.EnPreparacion => "bloque-preparacion",
                    ProduccionEstatus.EnProduccion => "bloque-produccion",
                    ProduccionEstatus.Pausado => "bloque-pausado",
                    ProduccionEstatus.TerminadoParcial => "bloque-terminado",
                    ProduccionEstatus.Terminado => "bloque-terminado",
                    ProduccionEstatus.Cerrado => "bloque-terminado",
                    ProduccionEstatus.Cancelado => "bloque-terminado",
                    _ => "bloque-programado"
                };
            }
        }
    }
}
