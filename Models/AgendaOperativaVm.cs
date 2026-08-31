using System;
using System.Collections.Generic;
using System.Linq;

namespace ERP.NSQuell.Models
{
    public static class AgendaOperativaPrioridad
    {
        public const string Critica = "CRITICA";
        public const string Alta = "ALTA";
        public const string Media = "MEDIA";
        public const string Normal = "NORMAL";
        public const string Baja = "BAJA";

        public static int Orden(string? prioridad)
        {
            return (prioridad ?? string.Empty).Trim().ToUpperInvariant() switch
            {
                Critica => 1,
                Alta => 2,
                Media => 3,
                Normal => 4,
                Baja => 5,
                _ => 99
            };
        }

        public static string Nombre(string? prioridad)
        {
            return (prioridad ?? string.Empty).Trim().ToUpperInvariant() switch
            {
                Critica => "Crítica",
                Alta => "Alta",
                Media => "Media",
                Normal => "Normal",
                Baja => "Baja",
                _ => "Sin prioridad"
            };
        }
    }

    public static class AgendaOperativaEstadoPaso
    {
        public const string NoAplica = "NO_APLICA";
        public const string Pendiente = "PENDIENTE";
        public const string EnProceso = "EN_PROCESO";
        public const string Esperando = "ESPERANDO";
        public const string Bloqueado = "BLOQUEADO";
        public const string Listo = "LISTO";
        public const string Completado = "COMPLETADO";
        public const string Vencido = "VENCIDO";

        public static string Nombre(string? estado)
        {
            return (estado ?? string.Empty).Trim().ToUpperInvariant() switch
            {
                NoAplica => "No aplica",
                Pendiente => "Pendiente",
                EnProceso => "En proceso",
                Esperando => "Esperando",
                Bloqueado => "Bloqueado",
                Listo => "Listo",
                Completado => "Completado",
                Vencido => "Vencido",
                _ => "Sin estado"
            };
        }
    }

    public static class AgendaOperativaEstadoGeneral
    {
        public const string Programada = "PROGRAMADA";
        public const string Preparacion = "PREPARACION";
        public const string EsperandoCalidad = "ESPERANDO_CALIDAD";
        public const string ListaParaSerie = "LISTA_PARA_SERIE";
        public const string Produciendo = "PRODUCIENDO";
        public const string Pausada = "PAUSADA";
        public const string InterrumpidaUrgente = "INTERRUMPIDA_URGENTE";
        public const string Reliberacion = "RELIBERACION";
        public const string MaquinaLiberada = "MAQUINA_LIBERADA";
        public const string Terminada = "TERMINADA";
        public const string Bloqueada = "BLOQUEADA";

        public static string Nombre(string? estado)
        {
            return (estado ?? string.Empty).Trim().ToUpperInvariant() switch
            {
                Programada => "Programada",
                Preparacion => "En preparación",
                EsperandoCalidad => "Esperando Calidad",
                ListaParaSerie => "Lista para iniciar serie",
                Produciendo => "En producción",
                Pausada => "Pausada",
                InterrumpidaUrgente => "Interrumpida por prioridad",
                Reliberacion => "En reliberación",
                MaquinaLiberada => "Máquina liberada",
                Terminada => "Terminada",
                Bloqueada => "Bloqueada",
                _ => "Sin estado"
            };
        }
    }

    public static class AgendaOperativaArea
    {
        public const string Planeacion = "PLANEACION";
        public const string Produccion = "PRODUCCION";
        public const string TecnicoProduccion = "TECNICO_PRODUCCION";
        public const string Smed = "SMED";
        public const string Calidad = "CALIDAD";
        public const string Almacen = "ALMACEN";
        public const string Materiales = "MATERIALES";
        public const string Secado = "SECADO";
        public const string Embalaje = "EMBALAJE";
        public const string Operador = "OPERADOR";
        public const string Mantenimiento = "MANTENIMIENTO";
        public const string Sistema = "SISTEMA";

        public static string Nombre(string? area)
        {
            return (area ?? string.Empty).Trim().ToUpperInvariant() switch
            {
                Planeacion => "Planeación",
                Produccion => "Producción",
                TecnicoProduccion => "Técnico de Producción",
                Smed => "SMED",
                Calidad => "Calidad",
                Almacen => "Almacén",
                Materiales => "Materiales",
                Secado => "Secado",
                Embalaje => "Embalaje",
                Operador => "Operador",
                Mantenimiento => "Mantenimiento",
                Sistema => "Sistema",
                _ => "Por definir"
            };
        }
    }

    public static class AgendaOperativaPasoClave
    {
        public const string Planeacion = "PLANEACION";
        public const string Personal = "PERSONAL";
        public const string Material = "MATERIAL";
        public const string Secado = "SECADO";
        public const string Embalaje = "EMBALAJE";
        public const string PreparacionMolde = "PREPARACION_MOLDE";
        public const string CambioMolde = "CAMBIO_MOLDE";
        public const string ChecklistArranque = "CHECKLIST_ARRANQUE";
        public const string ConfiguracionCorrida = "CONFIGURACION_CORRIDA";
        public const string PrimerasPiezas = "PRIMERAS_PIEZAS";
        public const string Calidad = "CALIDAD";
        public const string InicioSerie = "INICIO_SERIE";
        public const string Produccion = "PRODUCCION";
        public const string Paro = "PARO";
        public const string Capturas = "CAPTURAS";
        public const string Cajas = "CAJAS";
        public const string CalidadFinal = "CALIDAD_FINAL";
        public const string LiberacionMaquina = "LIBERACION_MAQUINA";
        public const string Cierre = "CIERRE";
    }

    public sealed class AgendaOperativaVm
    {
        public DateTime FechaConsulta { get; set; } = DateTime.Now;
        public AgendaOperativaFiltroVm Filtros { get; set; } = new();
        public AgendaOperativaResumenVm Resumen { get; set; } = new();
        public List<AgendaOperativaItemVm> Items { get; set; } = new();
        public List<AgendaOperativaOpcionVm> Maquinas { get; set; } = new();
        public List<AgendaOperativaOpcionVm> Areas { get; set; } = new();
        public bool TieneResultados => Items.Count > 0;
        public IEnumerable<AgendaOperativaItemVm> AtencionInmediata => Items.Where(x => x.RequiereAtencionInmediata).OrderBy(x => x.OrdenPrioridad).ThenBy(x => x.FechaOrden);
        public IEnumerable<AgendaOperativaItemVm> Bloqueadas => Items.Where(x => x.EstaBloqueada).OrderBy(x => x.OrdenPrioridad).ThenBy(x => x.FechaOrden);
        public IEnumerable<AgendaOperativaItemVm> EnProduccion => Items.Where(x => x.EstaProduciendo).OrderBy(x => x.MaquinaCodigo);
        public IEnumerable<AgendaOperativaItemVm> Proximas => Items.Where(x => x.EsProxima && !x.RequiereAtencionInmediata).OrderBy(x => x.FechaOrden);
    }

    public sealed class AgendaOperativaFiltroVm
    {
        public int? MaquinaID { get; set; }
        public string? Busqueda { get; set; }
        public string? Area { get; set; }
        public string? Estado { get; set; }
        public bool SoloAtencion { get; set; }
        public bool SoloBloqueadas { get; set; }
        public bool IncluirProduciendo { get; set; } = true;
        public int VentanaHoras { get; set; } = 8;
    }

    public sealed class AgendaOperativaResumenVm
    {
        public int Total { get; set; }
        public int AtencionInmediata { get; set; }
        public int Bloqueadas { get; set; }
        public int Proximas { get; set; }
        public int EnPreparacion { get; set; }
        public int EsperandoCalidad { get; set; }
        public int Produciendo { get; set; }
        public int Pausadas { get; set; }
        public int InterrumpidasUrgente { get; set; }
        public int Reliberaciones { get; set; }
        public int MaquinasLiberadas { get; set; }
    }

    public sealed class AgendaOperativaOpcionVm
    {
        public string Valor { get; set; } = string.Empty;
        public string Texto { get; set; } = string.Empty;
        public bool Seleccionado { get; set; }
    }

    public sealed class AgendaOperativaItemVm
    {
        public int ProgramaProduccionID { get; set; }
        public int? EjecucionProduccionID { get; set; }
        public int? SolicitudProduccionID { get; set; }
        public int? SolicitudProduccionDetalleID { get; set; }
        public int? ReleaseDetalleID { get; set; }
        public string? FolioSolicitud { get; set; }
        public string? NumeroOF { get; set; }
        public int? ParteID { get; set; }
        public string? NumeroParte { get; set; }
        public string? ReferenciaSAP { get; set; }
        public string? DescripcionParte { get; set; }
        public int? MaquinaID { get; set; }
        public string? MaquinaCodigo { get; set; }
        public string? MaquinaNombre { get; set; }
        public int? MoldeID { get; set; }
        public string? MoldeCodigo { get; set; }
        public string? MoldeNombre { get; set; }
        public int CantidadProgramada { get; set; }
        public int CantidadProducida { get; set; }
        public int CantidadPendiente => Math.Max(0, CantidadProgramada - CantidadProducida);
        public DateTime? FechaInicioProgramada { get; set; }
        public DateTime? FechaFinProgramada { get; set; }
        public DateTime? FechaInicioReal { get; set; }
        public DateTime? FechaFinReal { get; set; }
        public int EstatusProgramaID { get; set; }
        public int? EstatusEjecucionID { get; set; }
        public string EstadoGeneral { get; set; } = AgendaOperativaEstadoGeneral.Programada;
        public string? EstadoGeneralDetalle { get; set; }
        public string Prioridad { get; set; } = AgendaOperativaPrioridad.Normal;
        public int OrdenPrioridad => AgendaOperativaPrioridad.Orden(Prioridad);
        public bool EsUrgente { get; set; }
        public bool TieneParoAbierto { get; set; }
        public bool TieneInterrupcionUrgente { get; set; }
        public bool MaquinaLiberada { get; set; }
        public bool RequiereAtencionInmediata { get; set; }
        public bool EstaBloqueada { get; set; }
        public bool EsProxima { get; set; }
        public string? MotivoBloqueo { get; set; }
        public int MinutosDesfase { get; set; }
        public AgendaOperativaAccionVm? AccionActual { get; set; }
        public AgendaOperativaAccionVm? SiguienteAccion { get; set; }
        public List<AgendaOperativaPasoVm> Pasos { get; set; } = new();
        public List<AgendaOperativaResponsableVm> Responsables { get; set; } = new();
        public AgendaOperativaLhRhVm? ProduccionLhRh { get; set; }
        public AgendaOperativaInterrupcionVm? Interrupcion { get; set; }

        public string OFTexto
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(NumeroOF)) return NumeroOF.Trim();
                if (!string.IsNullOrWhiteSpace(FolioSolicitud)) return FolioSolicitud.Trim();
                if (SolicitudProduccionID.HasValue) return $"OF {SolicitudProduccionID.Value}";
                return $"Programa {ProgramaProduccionID}";
            }
        }

        public string ParteTexto
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(ReferenciaSAP)) return ReferenciaSAP.Trim();
                if (!string.IsNullOrWhiteSpace(NumeroParte)) return NumeroParte.Trim();
                return ParteID.HasValue ? $"Parte {ParteID.Value}" : "Sin parte";
            }
        }

        public string MaquinaTexto
        {
            get
            {
                if (string.IsNullOrWhiteSpace(MaquinaCodigo)) return "Sin máquina";
                return string.IsNullOrWhiteSpace(MaquinaNombre) ? MaquinaCodigo.Trim() : $"{MaquinaCodigo.Trim()} - {MaquinaNombre.Trim()}";
            }
        }

        public string EstadoGeneralTexto => AgendaOperativaEstadoGeneral.Nombre(EstadoGeneral);
        public string PrioridadTexto => AgendaOperativaPrioridad.Nombre(Prioridad);
        public bool EsLhRh => ProduccionLhRh?.EsPareja == true;
        public bool EstaProduciendo => string.Equals(EstadoGeneral, AgendaOperativaEstadoGeneral.Produciendo, StringComparison.OrdinalIgnoreCase);
        public DateTime FechaOrden => AccionActual?.FechaObjetivo ?? FechaInicioProgramada ?? DateTime.MaxValue;
        public decimal PorcentajeAvance => CantidadProgramada <= 0 ? 0m : Math.Min(100m, Math.Round((decimal)CantidadProducida / CantidadProgramada * 100m, 2));
    }

    public sealed class AgendaOperativaAccionVm
    {
        public string Clave { get; set; } = string.Empty;
        public string Titulo { get; set; } = string.Empty;
        public string? Descripcion { get; set; }
        public string AreaResponsable { get; set; } = AgendaOperativaArea.Produccion;
        public string? ResponsableNombre { get; set; }
        public int? ResponsableUsuarioID { get; set; }
        public string Prioridad { get; set; } = AgendaOperativaPrioridad.Normal;
        public DateTime? FechaObjetivo { get; set; }
        public DateTime? FechaDisponibleDesde { get; set; }
        public bool EstaVencida { get; set; }
        public int MinutosDesfase { get; set; }
        public bool BloqueaFlujo { get; set; }
        public bool EsEjecutable { get; set; }
        public bool RequiereConfirmacion { get; set; }
        public string? TextoConfirmacion { get; set; }
        public string TextoBoton { get; set; } = "Atender";
        public string Icono { get; set; } = "bi-arrow-right-circle";
        public string? Controlador { get; set; }
        public string? Accion { get; set; }
        public string? ParametroId { get; set; }
        public int? IdDestino { get; set; }
        public Dictionary<string, string?> ParametrosRuta { get; set; } = new();
        public string AreaResponsableTexto => AgendaOperativaArea.Nombre(AreaResponsable);
        public string PrioridadTexto => AgendaOperativaPrioridad.Nombre(Prioridad);
    }

    public sealed class AgendaOperativaPasoVm
    {
        public int Orden { get; set; }
        public string Clave { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public string? Descripcion { get; set; }
        public string AreaResponsable { get; set; } = AgendaOperativaArea.Produccion;
        public string Estado { get; set; } = AgendaOperativaEstadoPaso.Pendiente;
        public bool Aplica { get; set; } = true;
        public bool Completado { get; set; }
        public bool EnProceso { get; set; }
        public bool Bloqueado { get; set; }
        public bool BloqueaFlujo { get; set; }
        public string? MotivoBloqueo { get; set; }
        public string? Detalle { get; set; }
        public DateTime? FechaObjetivo { get; set; }
        public DateTime? FechaInicioReal { get; set; }
        public DateTime? FechaFinReal { get; set; }
        public int MinutosDesfase { get; set; }
        public bool EstaVencido { get; set; }
        public string? Controlador { get; set; }
        public string? Accion { get; set; }
        public int? IdDestino { get; set; }
        public string EstadoTexto => AgendaOperativaEstadoPaso.Nombre(Estado);
        public string AreaResponsableTexto => AgendaOperativaArea.Nombre(AreaResponsable);
        public bool EsPendienteOperativo => Aplica && !Completado && !string.Equals(Estado, AgendaOperativaEstadoPaso.NoAplica, StringComparison.OrdinalIgnoreCase);
    }

    public sealed class AgendaOperativaResponsableVm
    {
        public int? UsuarioID { get; set; }
        public string Area { get; set; } = string.Empty;
        public string Rol { get; set; } = string.Empty;
        public string? Nombre { get; set; }
        public bool Asignado { get; set; }
        public bool Disponible { get; set; }
        public string AreaTexto => AgendaOperativaArea.Nombre(Area);
        public string NombreTexto => string.IsNullOrWhiteSpace(Nombre) ? "Sin asignar" : Nombre.Trim();
    }

    public sealed class AgendaOperativaLhRhVm
    {
        public bool EsPareja { get; set; }
        public int? GrupoLhRh { get; set; }
        public string? LadoActual { get; set; }
        public string? LadoPareja { get; set; }
        public int ProgramaActualID { get; set; }
        public int ProgramaParejaID { get; set; }
        public int? EjecucionActualID { get; set; }
        public int? EjecucionParejaID { get; set; }
        public int? SolicitudParejaID { get; set; }
        public string? OFPareja { get; set; }
        public string? NumeroPartePareja { get; set; }
        public string? ReferenciaSAPPareja { get; set; }
        public string? EstadoPareja { get; set; }
        public int CantidadProgramadaPareja { get; set; }
        public int CantidadProducidaPareja { get; set; }
        public bool ParejaConsistente { get; set; }
        public string? MotivoInconsistencia { get; set; }
        public string ParteParejaTexto => !string.IsNullOrWhiteSpace(ReferenciaSAPPareja) ? ReferenciaSAPPareja.Trim() : !string.IsNullOrWhiteSpace(NumeroPartePareja) ? NumeroPartePareja.Trim() : "Sin parte";
        public decimal PorcentajeAvancePareja => CantidadProgramadaPareja <= 0 ? 0m : Math.Min(100m, Math.Round((decimal)CantidadProducidaPareja / CantidadProgramadaPareja * 100m, 2));
    }

    public sealed class AgendaOperativaInterrupcionVm
    {
        public int ParoID { get; set; }
        public bool EsInterrupcionUrgente { get; set; }
        public bool EsParoLhRh { get; set; }
        public Guid? GrupoParoLhRh { get; set; }
        public int? ProgramaUrgenteID { get; set; }
        public string? OFUrgente { get; set; }
        public DateTime FechaInicio { get; set; }
        public DateTime? FechaFin { get; set; }
        public int DuracionMinutos { get; set; }
        public string? Motivo { get; set; }
        public bool EstaAbierta => !FechaFin.HasValue;
        public bool PendienteReinicio { get; set; }
        public bool RequiereReliberacion { get; set; }
        public bool RequiereCambioMoldeRetorno { get; set; }
        public string TipoTexto => EsInterrupcionUrgente ? "Interrupción urgente" : "Paro de Producción";
    }
}