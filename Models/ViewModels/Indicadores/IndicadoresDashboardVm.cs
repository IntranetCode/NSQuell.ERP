namespace ERP.NSQuell.Models.ViewModels.Indicadores;

public sealed class IndicadoresDashboardVm
{
    public DateTime Desde { get; set; }
    public DateTime Hasta { get; set; }
    public DateTime GeneradoEn { get; set; } = DateTime.Now;
    public string Seccion { get; set; } = "general";
    public string Periodo { get; set; } = "semana";
    public bool PeriodoLimitado { get; set; }

    public IndicadoresProduccionKpiVm Produccion { get; set; } = new();
    public IndicadoresPlaneacionKpiVm Planeacion { get; set; } = new();
    public IndicadoresCalidadKpiVm Calidad { get; set; } = new();
    public IndicadoresGp12KpiVm GP12 { get; set; } = new();
    public IndicadoresAlmacenKpiVm Almacen { get; set; } = new();
    public IndicadoresLogisticaKpiVm Logistica { get; set; } = new();
    public IndicadoresComprasKpiVm Compras { get; set; } = new();

    public List<IndicadoresOperadorKpiVm> Operadores { get; set; } = new();
    public List<IndicadoresPersonalApoyoKpiVm> Tecnicos { get; set; } = new();
    public List<IndicadoresPersonalApoyoKpiVm> Auxiliares { get; set; } = new();
    public List<IndicadoresMaquinaKpiVm> Maquinas { get; set; } = new();
    public List<IndicadoresTendenciaDiaVm> Tendencia { get; set; } = new();
    public List<IndicadoresProgramaProduccionVm> ProgramasProduccion { get; set; } = new();
    public List<IndicadoresParoMotivoVm> ParosMotivos { get; set; } = new();
    public List<IndicadoresParoMaquinaVm> ParosMaquinas { get; set; } = new();
    public List<IndicadoresAlertaVm> Alertas { get; set; } = new();

    public int DiasPeriodo => Math.Max(1, (Hasta.Date - Desde.Date).Days + 1);

    public string TituloSeccion => Seccion switch
    {
        "produccion" => "Producción",
        "planeacion" => "Planeación",
        "calidad" => "Calidad",
        "gp12" => "GP12",
        "almacen" => "Almacén",
        "logistica" => "Logística",
        "compras" => "Compras",
        _ => "Resumen general"
    };
}

public sealed class IndicadoresProduccionKpiVm
{
    public long PiezasOK { get; set; }
    public long PiezasSospechosas { get; set; }
    public long PiezasScrap { get; set; }
    public long Objetivo { get; set; }
    public int RegistrosHora { get; set; }
    public int Operadores { get; set; }
    public int ProgramasSinEstandar { get; set; }
    public decimal MinutosProduccion { get; set; }
    public decimal MinutosParo { get; set; }

    public long TotalProducido => PiezasOK + PiezasSospechosas + PiezasScrap;
    public decimal CumplimientoPct => Objetivo <= 0 ? 0m : PiezasOK * 100m / Objetivo;
    public decimal ScrapPct => TotalProducido <= 0 ? 0m : PiezasScrap * 100m / TotalProducido;
    public decimal CalidadPct => Math.Clamp(100m - ScrapPct, 0m, 100m);
    public decimal ParosPct => MinutosProduccion <= 0 ? 0m : Math.Clamp(MinutosParo * 100m / MinutosProduccion, 0m, 100m);
    public decimal DisponibilidadPct => Math.Clamp(100m - ParosPct, 0m, 100m);
    public decimal RqtPct => CumplimientoPct;
    public decimal RqlPct => CalidadPct;
    public decimal UePct => DisponibilidadPct;
    public decimal RendimientoOeePct => Math.Clamp(RqtPct, 0m, 100m);
    public decimal OeePct => RendimientoOeePct / 100m * RqlPct / 100m * UePct / 100m * 100m;
}

public sealed class IndicadoresOperadorKpiVm
{
    public int OperadorID { get; set; }
    public string Operador { get; set; } = string.Empty;
    public string NumeroControl { get; set; } = string.Empty;
    public long PiezasOK { get; set; }
    public long PiezasSospechosas { get; set; }
    public long PiezasScrap { get; set; }
    public long Objetivo { get; set; }
    public decimal MinutosProduccion { get; set; }
    public decimal MinutosParo { get; set; }
    public int Registros { get; set; }
    public int PartesTrabajadas { get; set; }
    public int TurnosTrabajados { get; set; }
    public int MaquinasTrabajadas { get; set; }
    public int OfTrabajadas { get; set; }
    public string MejorParte { get; set; } = string.Empty;
    public string MejorParteDescripcion { get; set; } = string.Empty;
    public long MejorParteOK { get; set; }
    public decimal MejorParteRqtPct { get; set; }
    public string MejorTurno { get; set; } = string.Empty;
    public long MejorTurnoOK { get; set; }
    public decimal MejorTurnoRqtPct { get; set; }
    public string MejorMaquina { get; set; } = string.Empty;
    public decimal MejorMaquinaRqtPct { get; set; }
    public string MejorOF { get; set; } = string.Empty;
    public decimal MejorOFRqtPct { get; set; }

    public long TotalProducido => PiezasOK + PiezasSospechosas + PiezasScrap;
    public decimal CumplimientoPct => Objetivo <= 0 ? 0m : PiezasOK * 100m / Objetivo;
    public decimal ScrapPct => TotalProducido <= 0 ? 0m : PiezasScrap * 100m / TotalProducido;
    public decimal CalidadPct => Math.Clamp(100m - ScrapPct, 0m, 100m);
    public decimal ParosPct => MinutosProduccion <= 0 ? 0m : Math.Clamp(MinutosParo * 100m / MinutosProduccion,0m,100m);
    public decimal DisponibilidadPct => Math.Clamp(100m - ParosPct,0m,100m);
    public decimal RqtPct => CumplimientoPct;
    public decimal RqlPct => CalidadPct;
    public decimal UePct => DisponibilidadPct;
    public decimal OeePct => Math.Clamp(RqtPct,0m,100m) / 100m * RqlPct / 100m * UePct / 100m * 100m;
}

public sealed class IndicadoresPersonalApoyoKpiVm
{
    public string Rol { get; set; } = string.Empty;
    public int? PersonaID { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string NumeroControl { get; set; } = string.Empty;
    public long PiezasOK { get; set; }
    public long PiezasSospechosas { get; set; }
    public long PiezasScrap { get; set; }
    public long Objetivo { get; set; }
    public decimal MinutosProduccion { get; set; }
    public decimal MinutosParo { get; set; }
    public int Registros { get; set; }
    public int PartesTrabajadas { get; set; }
    public int TurnosTrabajados { get; set; }
    public int MaquinasTrabajadas { get; set; }
    public int OfTrabajadas { get; set; }
    public string MejorParte { get; set; } = string.Empty;
    public string MejorParteDescripcion { get; set; } = string.Empty;
    public long MejorParteOK { get; set; }
    public decimal MejorParteRqtPct { get; set; }
    public string MejorTurno { get; set; } = string.Empty;
    public long MejorTurnoOK { get; set; }
    public decimal MejorTurnoRqtPct { get; set; }
    public string MejorMaquina { get; set; } = string.Empty;
    public decimal MejorMaquinaRqtPct { get; set; }
    public string MejorOF { get; set; } = string.Empty;
    public decimal MejorOFRqtPct { get; set; }

    public long TotalProducido => PiezasOK + PiezasSospechosas + PiezasScrap;
    public decimal RqtPct => Objetivo <= 0 ? 0m : PiezasOK * 100m / Objetivo;
    public decimal ScrapPct => TotalProducido <= 0 ? 0m : PiezasScrap * 100m / TotalProducido;
    public decimal RqlPct => Math.Clamp(100m - ScrapPct,0m,100m);
    public decimal ParosPct => MinutosProduccion <= 0 ? 0m : Math.Clamp(MinutosParo * 100m / MinutosProduccion,0m,100m);
    public decimal UePct => Math.Clamp(100m - ParosPct,0m,100m);
    public decimal OeePct => Math.Clamp(RqtPct,0m,100m) / 100m * RqlPct / 100m * UePct / 100m * 100m;
}

public sealed class IndicadoresMaquinaKpiVm
{
    public int MaquinaID { get; set; }
    public string Maquina { get; set; } = string.Empty;
    public long PiezasOK { get; set; }
    public long PiezasSospechosas { get; set; }
    public long PiezasScrap { get; set; }
    public long Objetivo { get; set; }
    public decimal MinutosProduccion { get; set; }
    public decimal MinutosParo { get; set; }
    public int PartesTrabajadas { get; set; }
    public int TurnosTrabajados { get; set; }
    public int OperadoresTrabajados { get; set; }
    public int OfTrabajadas { get; set; }
    public string MejorParte { get; set; } = string.Empty;
    public string MejorParteDescripcion { get; set; } = string.Empty;
    public decimal MejorParteRqtPct { get; set; }
    public string MejorTurno { get; set; } = string.Empty;
    public decimal MejorTurnoRqtPct { get; set; }
    public string MejorOperador { get; set; } = string.Empty;
    public decimal MejorOperadorRqtPct { get; set; }
    public string MejorOF { get; set; } = string.Empty;
    public decimal MejorOFRqtPct { get; set; }

    public long Total => PiezasOK + PiezasSospechosas + PiezasScrap;
    public decimal CumplimientoPct => Objetivo <= 0 ? 0m : PiezasOK * 100m / Objetivo;
    public decimal ScrapPct => Total <= 0 ? 0m : PiezasScrap * 100m / Total;
    public decimal ParosPct => MinutosProduccion <= 0 ? 0m : Math.Clamp(MinutosParo * 100m / MinutosProduccion,0m,100m);
    public decimal DisponibilidadPct => Math.Clamp(100m - ParosPct,0m,100m);
    public decimal RqtPct => CumplimientoPct;
    public decimal RqlPct => Math.Clamp(100m - ScrapPct,0m,100m);
    public decimal UePct => DisponibilidadPct;
    public decimal OeePct => Math.Clamp(RqtPct,0m,100m) / 100m * RqlPct / 100m * UePct / 100m * 100m;
}

public sealed class IndicadoresTendenciaDiaVm
{
    public DateTime Fecha { get; set; }
    public long PiezasOK { get; set; }
    public long PiezasSospechosas { get; set; }
    public long PiezasScrap { get; set; }
    public long Objetivo { get; set; }
    public decimal MinutosProduccion { get; set; }
    public decimal MinutosParo { get; set; }

    public decimal CumplimientoPct => Objetivo <= 0 ? 0m : PiezasOK * 100m / Objetivo;
    public long TotalProducido => PiezasOK + PiezasSospechosas + PiezasScrap;
    public decimal ScrapPct => TotalProducido <= 0 ? 0m : PiezasScrap * 100m / TotalProducido;
    public decimal ParosPct => MinutosProduccion <= 0 ? 0m : Math.Clamp(MinutosParo * 100m / MinutosProduccion,0m,100m);
    public decimal DisponibilidadPct => Math.Clamp(100m - ParosPct,0m,100m);
    public decimal RqtPct => CumplimientoPct;
    public decimal RqlPct => Math.Clamp(100m - ScrapPct,0m,100m);
    public decimal UePct => DisponibilidadPct;
    public decimal OeePct => Math.Clamp(RqtPct,0m,100m) / 100m * RqlPct / 100m * UePct / 100m * 100m;
}

public sealed class IndicadoresProgramaProduccionVm
{
    public int ProgramaProduccionID { get; set; }
    public int EjecucionProduccionID { get; set; }
    public int? SolicitudProduccionID { get; set; }
    public string NumeroOF { get; set; } = string.Empty;
    public string Parte { get; set; } = string.Empty;
    public string DescripcionParte { get; set; } = string.Empty;
    public string Maquina { get; set; } = string.Empty;
    public int ObjetivoHoraEstandar { get; set; }
    public string CicloEstandar { get; set; } = string.Empty;
    public int? CavidadesEstandar { get; set; }
    public string FuenteEstandar { get; set; } = string.Empty;
    public long PiezasOK { get; set; }
    public long PiezasSospechosas { get; set; }
    public long PiezasScrap { get; set; }
    public long Objetivo { get; set; }
    public decimal MinutosProduccion { get; set; }
    public decimal MinutosParo { get; set; }
    public int TurnosTrabajados { get; set; }
    public int OperadoresTrabajados { get; set; }
    public string MejorTurno { get; set; } = string.Empty;
    public decimal MejorTurnoRqtPct { get; set; }
    public string MejorOperador { get; set; } = string.Empty;
    public decimal MejorOperadorRqtPct { get; set; }

    public long TotalProducido => PiezasOK + PiezasSospechosas + PiezasScrap;
    public decimal RqtPct => Objetivo <= 0 ? 0m : PiezasOK * 100m / Objetivo;
    public decimal ScrapPct => TotalProducido <= 0 ? 0m : PiezasScrap * 100m / TotalProducido;
    public decimal RqlPct => Math.Clamp(100m - ScrapPct,0m,100m);
    public decimal ParosPct => MinutosProduccion <= 0 ? 0m : Math.Clamp(MinutosParo * 100m / MinutosProduccion,0m,100m);
    public decimal UePct => Math.Clamp(100m - ParosPct,0m,100m);
    public decimal OeePct => Math.Clamp(RqtPct,0m,100m) / 100m * RqlPct / 100m * UePct / 100m * 100m;
    public bool TieneEstandar => ObjetivoHoraEstandar > 0;
}

public sealed class IndicadoresParoMotivoVm
{
    public string Motivo { get; set; } = string.Empty;
    public int Eventos { get; set; }
    public decimal Minutos { get; set; }
    public decimal PorcentajeParo { get; set; }
}
public sealed class IndicadoresParoMaquinaVm
{
    public int MaquinaID { get; set; }
    public string Maquina { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public int Eventos { get; set; }
    public decimal Minutos { get; set; }
    public decimal MinutosProduccion { get; set; }
    public decimal PorcentajeParoTotal { get; set; }
    public decimal ParosPct => MinutosProduccion <= 0m
        ? (Minutos > 0m ? 100m : 0m)
        : Math.Clamp(Minutos * 100m / MinutosProduccion,0m,100m);
}
public sealed class IndicadoresPlaneacionKpiVm
{
    public long Programado { get; set; }
    public long Producido { get; set; }
    public long Pendiente { get; set; }
    public int Programas { get; set; }
    public int ProgramasConPendiente { get; set; }
    public int ArranquesRegistrados { get; set; }
    public int ArranquesATiempo { get; set; }
    public int Reprogramados { get; set; }

    public decimal CumplimientoPct => Programado <= 0 ? 0m : Producido * 100m / Programado;
    public decimal ArranqueATiempoPct => ArranquesRegistrados <= 0 ? 0m : ArranquesATiempo * 100m / ArranquesRegistrados;
}

public sealed class IndicadoresCalidadKpiVm
{
    public int Inspecciones { get; set; }
    public int Liberadas { get; set; }
    public int Contenciones { get; set; }
    public int Scrap { get; set; }
    public decimal CantidadTotal { get; set; }
    public decimal CantidadRevisada { get; set; }
    public decimal CantidadPendiente { get; set; }
    public int RequierenGP12 { get; set; }
    public int RequierenReliberacion { get; set; }
    public int CumplieronTiempoObjetivo { get; set; }
    public int LiberacionesConTiempo { get; set; }
    public decimal MinutosLiberacionPromedio { get; set; }

    public decimal LiberacionPct => Inspecciones <= 0 ? 0m : Liberadas * 100m / Inspecciones;
    public decimal CoberturaPct => CantidadTotal <= 0 ? 0m : CantidadRevisada * 100m / CantidadTotal;
    public decimal CumplimientoTiempoPct => LiberacionesConTiempo <= 0 ? 0m : CumplieronTiempoObjetivo * 100m / LiberacionesConTiempo;
}

public sealed class IndicadoresGp12KpiVm
{
    public int Solicitudes { get; set; }
    public int SolicitudesPendientes { get; set; }
    public int Inspecciones { get; set; }
    public decimal Solicitado { get; set; }
    public decimal Procesado { get; set; }
    public decimal Pendiente { get; set; }
    public decimal Revisado { get; set; }
    public decimal OK { get; set; }
    public decimal NOK { get; set; }
    public decimal Retrabajado { get; set; }
    public decimal Scrap { get; set; }

    public decimal ConformidadPct => Revisado <= 0 ? 0m : OK * 100m / Revisado;
    public decimal ScrapPct => Revisado <= 0 ? 0m : Scrap * 100m / Revisado;
    public decimal AvancePct => Solicitado <= 0 ? 0m : Procesado * 100m / Solicitado;
}

public sealed class IndicadoresAlmacenKpiVm
{
    public int MovimientosMP { get; set; }
    public int MovimientosPT { get; set; }
    public int MovimientosEmbalajes { get; set; }
    public int EntradasMP { get; set; }
    public int SalidasMP { get; set; }
    public int EntradasPT { get; set; }
    public int SalidasPT { get; set; }
    public int ScrapPendienteRecepcion { get; set; }
    public int ScrapRecibidoPendienteMolienda { get; set; }
    public int MPBajoMinimo { get; set; }
    public int PTEnRojo { get; set; }
    public int EmbalajesEnRojo { get; set; }

    public int MovimientosTotales => MovimientosMP + MovimientosPT + MovimientosEmbalajes;
    public int AlertasInventario => MPBajoMinimo + PTEnRojo + EmbalajesEnRojo;
}

public sealed class IndicadoresLogisticaKpiVm
{
    public int Embarques { get; set; }
    public int Entregados { get; set; }
    public int EntregadosATiempo { get; set; }
    public int Incidencias { get; set; }
    public int Atrasados { get; set; }
    public int IncidenciasAbiertas { get; set; }
    public int CriticasAbiertas { get; set; }

    public decimal EntregaPct => Embarques <= 0 ? 0m : Entregados * 100m / Embarques;
    public decimal PuntualidadPct => Entregados <= 0 ? 0m : EntregadosATiempo * 100m / Entregados;
}

public sealed class IndicadoresComprasKpiVm
{
    public int Solicitudes { get; set; }
    public int PendientesFlujo { get; set; }
    public int UrgentesPendientes { get; set; }
    public int OrdenesCompra { get; set; }
    public decimal MontoOrdenes { get; set; }
    public decimal PromedioDiasEnEstatus { get; set; }
    public int Recepciones { get; set; }
    public int RecepcionesATiempo { get; set; }

    public decimal RecepcionATiempoPct => Recepciones <= 0 ? 0m : RecepcionesATiempo * 100m / Recepciones;
}

public sealed class IndicadoresAlertaVm
{
    public string Nivel { get; set; } = "info";
    public string Area { get; set; } = string.Empty;
    public string Titulo { get; set; } = string.Empty;
    public string Detalle { get; set; } = string.Empty;
}
