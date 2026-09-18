using System;
using System.Collections.Generic;

namespace ERP.NSQuell.Models;

public sealed class ProduccionCambioTurnoOperativoVm
{
    public int EjecucionProduccionID { get; set; }
    public int ProgramaProduccionID { get; set; }
    public string OFTexto { get; set; } = "Sin OF capturada";

    public int EstatusID { get; set; }
    public int? MaquinaID { get; set; }
    public string? MaquinaCodigo { get; set; }
    public string? MaquinaNombre { get; set; }
    public int? ParteID { get; set; }
    public string? NumeroParte { get; set; }
    public string? ReferenciaSAP { get; set; }

    public int? OperadorActualID { get; set; }
    public string? OperadorActualNombre { get; set; }

    public DateTime FechaConsulta { get; set; } = DateTime.Now;

    public bool RequiereCambioTurno { get; set; }
    public bool CambioProgramadoIniciado { get; set; }
    public int MinutosParaCambio { get; set; }
    public int? TurnoProgramadoID { get; set; }
    public string? TurnoProgramadoNombre { get; set; }
    public DateTime? InicioTurnoProgramado { get; set; }
    public DateTime? FinTurnoProgramado { get; set; }
    public int? OperadorProgramadoID { get; set; }
    public string? OperadorProgramadoNombre { get; set; }

    public ProduccionCambioTurnoResumenVm? Resumen { get; set; }
    public ProduccionCambioTurnoResumenVm? ResumenPareja { get; set; }
    public ProduccionCambioTurnoHistorialVm? CambioPendiente { get; set; }
    public List<ProduccionCambioTurnoHistorialVm> HistorialReciente { get; set; } = new();

    public bool EsLhRh { get; set; }
    public int? GrupoLhRh { get; set; }
    public int? EjecucionParejaID { get; set; }
    public int? ProgramaParejaID { get; set; }
    public string? OFParejaTexto { get; set; }
    public string? ParteParejaTexto { get; set; }
    public bool ParejaConsistente { get; set; } = true;
    public string? MotivoInconsistenciaPareja { get; set; }

    public int UsuarioID { get; set; }
    public int? PersonaUsuarioID { get; set; }
    public bool UsuarioEsOperador { get; set; }
    public bool SoloLecturaSolicitado { get; set; }

    public bool TieneCambioPendiente => CambioPendiente != null;

    public bool UsuarioEsOperadorActual =>
        PersonaUsuarioID.HasValue &&
        OperadorActualID.HasValue &&
        PersonaUsuarioID.Value == OperadorActualID.Value;

    public bool UsuarioEsEntrantePendiente =>
        PersonaUsuarioID.HasValue &&
        CambioPendiente != null &&
        PersonaUsuarioID.Value == CambioPendiente.OperadorEntranteID;

    public bool PuedeEntregar =>
        !SoloLecturaSolicitado &&
        UsuarioEsOperador &&
        UsuarioEsOperadorActual &&
        RequiereCambioTurno &&
        CambioPendiente == null &&
        Resumen?.PuedeEntregar == true;

    public bool PuedeRecibir =>
        !SoloLecturaSolicitado &&
        UsuarioEsOperador &&
        UsuarioEsEntrantePendiente &&
        CambioPendiente != null;

    public string EstatusTexto => ProduccionEstatus.Nombre(EstatusID);

    public string MaquinaTexto =>
        string.IsNullOrWhiteSpace(MaquinaCodigo)
            ? "Sin máquina"
            : string.IsNullOrWhiteSpace(MaquinaNombre)
                ? MaquinaCodigo!
                : $"{MaquinaCodigo} - {MaquinaNombre}";

    public string ParteTexto =>
        !string.IsNullOrWhiteSpace(ReferenciaSAP)
            ? ReferenciaSAP!
            : !string.IsNullOrWhiteSpace(NumeroParte)
                ? NumeroParte!
                : "Sin parte";

    public string OperadorActualTexto =>
        string.IsNullOrWhiteSpace(OperadorActualNombre)
            ? "Sin operador actual"
            : OperadorActualNombre!;

    public string OperadorProgramadoTexto =>
        string.IsNullOrWhiteSpace(OperadorProgramadoNombre)
            ? "Sin operador programado"
            : OperadorProgramadoNombre!;

    public string TurnoProgramadoTexto =>
        string.IsNullOrWhiteSpace(TurnoProgramadoNombre)
            ? "Turno por definir"
            : TurnoProgramadoNombre!;
}
