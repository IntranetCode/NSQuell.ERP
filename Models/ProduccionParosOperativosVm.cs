using System;
using System.Collections.Generic;
using System.Linq;

namespace ERP.NSQuell.Models;

public sealed class ProduccionParosOperativosVm
{
    public DateTime FechaConsulta { get; set; } = DateTime.Now;
    public int ProgramaProduccionID { get; set; }
    public ProduccionParoLadoOperativoVm Actual { get; set; } = new();
    public ProduccionParoLadoOperativoVm? Pareja { get; set; }
    public int? GrupoLhRh { get; set; }
    public bool ParejaConsistente { get; set; } = true;
    public string? MotivoInconsistenciaPareja { get; set; }
    public bool PuedeGestionar { get; set; }
    public string? UsuarioNombre { get; set; }
    public string? UsuarioPuesto { get; set; }
    public List<ProduccionParoMotivoOperativoVm> Motivos { get; set; } = new();
    public List<ProduccionParoOperativoItemVm> Paros { get; set; } = new();
    public bool EsParejaLhRh => Pareja != null;
    public ProduccionParoOperativoItemVm? ParoAbierto => Paros
        .Where(x => x.EstaAbierto)
        .OrderByDescending(x => x.FechaInicioParo)
        .ThenByDescending(x => x.ParoID)
        .FirstOrDefault();
    public bool TieneParoAbierto => ParoAbierto != null;
    public bool TieneInterrupcionUrgente => ParoAbierto?.EsInterrupcionUrgente == true;
    public bool PuedeIniciarParo =>
        PuedeGestionar &&
        ParejaConsistente &&
        !TieneParoAbierto &&
        Actual.EjecucionProduccionID > 0 &&
        Actual.EstatusID == ProduccionEstatus.EnProduccion &&
        (Pareja == null ||
         (Pareja.EjecucionProduccionID > 0 && Pareja.EstatusID == ProduccionEstatus.EnProduccion));
    public bool PuedeCerrarParo =>
        PuedeGestionar &&
        ParoAbierto?.PuedeCerrarManual == true;
}

public sealed class ProduccionParoLadoOperativoVm
{
    public int ProgramaProduccionID { get; set; }
    public int EjecucionProduccionID { get; set; }
    public string NumeroOF { get; set; } = "Sin OF";
    public string? LadoLhRh { get; set; }
    public int? MaquinaID { get; set; }
    public string? MaquinaCodigo { get; set; }
    public string? MaquinaNombre { get; set; }
    public int? ParteID { get; set; }
    public string? NumeroParte { get; set; }
    public string? ReferenciaSAP { get; set; }
    public string? DescripcionParte { get; set; }
    public int? MoldeID { get; set; }
    public string? MoldeCodigo { get; set; }
    public string? OperadorNombre { get; set; }
    public string? OperadorAuxiliarNombre { get; set; }
    public int EstatusID { get; set; }
    public string EstatusNombre => ProduccionEstatus.Nombre(EstatusID);
    public string ParteTexto =>
        !string.IsNullOrWhiteSpace(ReferenciaSAP)
            ? ReferenciaSAP!
            : !string.IsNullOrWhiteSpace(NumeroParte)
                ? NumeroParte!
                : "Sin parte";
    public string MaquinaTexto =>
        string.IsNullOrWhiteSpace(MaquinaCodigo)
            ? "Sin máquina"
            : string.IsNullOrWhiteSpace(MaquinaNombre)
                ? MaquinaCodigo!
                : $"{MaquinaCodigo} - {MaquinaNombre}";
}

public sealed class ProduccionParoMotivoOperativoVm
{
    public int MotivoParoID { get; set; }
    public string Nombre { get; set; } = string.Empty;
}

public sealed class ProduccionParoOperativoItemVm
{
    public int ParoID { get; set; }
    public int EjecucionProduccionID { get; set; }
    public int ProgramaProduccionID { get; set; }
    public string NumeroOF { get; set; } = "Sin OF";
    public string? LadoLhRh { get; set; }
    public int? EjecucionParejaID { get; set; }
    public int? ProgramaParejaID { get; set; }
    public string? NumeroOFPareja { get; set; }
    public string? LadoParejaLhRh { get; set; }
    public DateTime FechaInicioParo { get; set; }
    public DateTime? FechaFinParo { get; set; }
    public int DuracionMinutos { get; set; }
    public int? MotivoParoID { get; set; }
    public string? MotivoParoTexto { get; set; }
    public string? Descripcion { get; set; }
    public bool EsMayorA15Minutos { get; set; }
    public bool EsInterrupcionUrgente { get; set; }
    public int? ProgramaUrgenteID { get; set; }
    public string? NumeroOFUrgente { get; set; }
    public bool EsParoLhRh { get; set; }
    public Guid? GrupoParoLhRh { get; set; }
    public bool EstaAbierto => !FechaFinParo.HasValue;
    public bool PuedeCerrarManual => EstaAbierto && !EsInterrupcionUrgente;
    public string DuracionTexto
    {
        get
        {
            var minutos = Math.Max(0, DuracionMinutos);
            if (minutos < 60) return $"{minutos} min";
            return $"{minutos / 60} h {minutos % 60} min";
        }
    }
    public string OFsTexto =>
        string.IsNullOrWhiteSpace(NumeroOFPareja)
            ? NumeroOF
            : $"{NumeroOF} + {NumeroOFPareja}";
}

public sealed class ProduccionParoOperativoIniciarPostVm
{
    public int ProgramaProduccionID { get; set; }
    public int EjecucionProduccionID { get; set; }
    public int? MotivoParoID { get; set; }
    public string? MotivoParoTexto { get; set; }
    public string? Descripcion { get; set; }
}

public sealed class ProduccionParoOperativoCerrarPostVm
{
    public int ProgramaProduccionID { get; set; }
    public int ParoID { get; set; }
    public string? ObservacionesCierre { get; set; }
}
