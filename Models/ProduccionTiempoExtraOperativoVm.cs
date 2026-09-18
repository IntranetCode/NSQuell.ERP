using System;
using System.Collections.Generic;

namespace ERP.NSQuell.Models;

public sealed class ProduccionTiempoExtraOperativoVm
{
    public int EjecucionProduccionID { get; set; }
    public int ProgramaProduccionID { get; set; }
    public string OFTexto { get; set; } = string.Empty;

    public int EstatusID { get; set; }
    public string EstatusTexto => ProduccionEstatus.Nombre(EstatusID);

    public int? MaquinaID { get; set; }
    public string? MaquinaCodigo { get; set; }
    public string? MaquinaNombre { get; set; }

    public int? ParteID { get; set; }
    public string? NumeroParte { get; set; }
    public string? ReferenciaSAP { get; set; }

    public int? OperadorActualID { get; set; }
    public string? OperadorActualNombre { get; set; }

    public bool SoloLectura { get; set; }
    public bool UsuarioEsOperador { get; set; }
    public bool UsuarioAsignadoAEjecucion { get; set; }
    public bool PuedeGestionar { get; set; }
    public bool PuedeIniciar { get; set; }

    public ProduccionTiempoExtraVm? TiempoExtraActivo { get; set; }
    public List<ProduccionTiempoExtraVm> Historial { get; set; } = new();
    public List<ProduccionCatalogoDefectoVm> CatalogoDefectos { get; set; } = new();

    public long? UltimoContadorMaquina { get; set; }
    public DateTime FechaHoraServidor { get; set; } = DateTime.Now;

    public bool EsLhRh { get; set; }
    public int? GrupoLhRh { get; set; }
    public int? EjecucionParejaID { get; set; }
    public int? ProgramaParejaID { get; set; }
    public string? OFParejaTexto { get; set; }
    public string? ParteParejaTexto { get; set; }
    public bool ParejaConsistente { get; set; } = true;
    public string? MotivoInconsistenciaPareja { get; set; }

    public ProduccionTiempoExtraVm? TiempoExtraParejaActivo { get; set; }
    public List<ProduccionTiempoExtraVm> HistorialPareja { get; set; } = new();
    public long? UltimoContadorPareja { get; set; }

    public bool TieneSesionAbierta =>
        TiempoExtraActivo != null &&
        TiempoExtraActivo.Activo &&
        !TiempoExtraActivo.FechaHoraFin.HasValue &&
        (string.Equals(TiempoExtraActivo.Estado, ProduccionTiempoExtraEstado.EnCurso, StringComparison.OrdinalIgnoreCase) ||
         string.Equals(TiempoExtraActivo.Estado, ProduccionTiempoExtraEstado.Pausado, StringComparison.OrdinalIgnoreCase));

    public bool SesionEnCurso =>
        TiempoExtraActivo?.EstaEnCurso == true;

    public bool ParejaTieneSesionAbierta =>
        TiempoExtraParejaActivo != null &&
        TiempoExtraParejaActivo.Activo &&
        !TiempoExtraParejaActivo.FechaHoraFin.HasValue;

    public bool Corte60Disponible =>
        SesionEnCurso &&
        TiempoExtraActivo != null &&
        FechaHoraServidor >= TiempoExtraActivo.FechaHoraProximoCorte;

    public DateTime? ProximoCorte =>
        TiempoExtraActivo?.FechaHoraProximoCorte;

    public int MinutosDesdeUltimoCorte
    {
        get
        {
            if (TiempoExtraActivo == null) return 0;
            var fin = TiempoExtraActivo.FechaHoraFin ?? FechaHoraServidor;
            return Math.Max(0, (int)Math.Floor((fin - TiempoExtraActivo.FechaHoraUltimoCorte).TotalMinutes));
        }
    }

    public int MinutosParaCorte
    {
        get
        {
            if (TiempoExtraActivo == null || Corte60Disponible) return 0;
            return Math.Max(0, (int)Math.Ceiling((TiempoExtraActivo.FechaHoraProximoCorte - FechaHoraServidor).TotalMinutes));
        }
    }

    public string MaquinaTexto =>
        !string.IsNullOrWhiteSpace(MaquinaCodigo) && !string.IsNullOrWhiteSpace(MaquinaNombre)
            ? $"{MaquinaCodigo} - {MaquinaNombre}"
            : !string.IsNullOrWhiteSpace(MaquinaCodigo)
                ? MaquinaCodigo!
                : !string.IsNullOrWhiteSpace(MaquinaNombre)
                    ? MaquinaNombre!
                    : "Sin maquina";

    public string ParteTexto =>
        !string.IsNullOrWhiteSpace(ReferenciaSAP)
            ? ReferenciaSAP!
            : !string.IsNullOrWhiteSpace(NumeroParte)
                ? NumeroParte!
                : ParteID.HasValue
                    ? $"Parte {ParteID.Value}"
                    : "Sin parte";

    public string OperadorTexto =>
        !string.IsNullOrWhiteSpace(OperadorActualNombre)
            ? OperadorActualNombre!
            : "Sin operador asignado";

    public string MotivoNoDisponible
    {
        get
        {
            if (TieneSesionAbierta) return string.Empty;
            if (EstatusID != ProduccionEstatus.EnProduccion)
                return "La corrida no se encuentra en Produccion.";
            if (!PuedeIniciar)
                return "El tiempo normal planeado aun no esta completamente capturado o existe un bloqueo operativo. El controlador actual volvera a validar todas las reglas antes de iniciar tiempo extra.";
            if (!PuedeGestionar)
                return "La sesion puede consultarse desde este Centro Operativo, pero el inicio y los cortes corresponden al operador autorizado.";
            return string.Empty;
        }
    }
}
