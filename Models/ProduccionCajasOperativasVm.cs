using System;
using System.Collections.Generic;
using System.Linq;

namespace ERP.NSQuell.Models;

public sealed class ProduccionCajasOperativasVm
{
    public ProduccionOperadorCajasVm Actual { get; set; } = new();
    public ProduccionOperadorCajasVm? Pareja { get; set; }

    public bool SoloLectura { get; set; }
    public bool UsuarioEsOperador { get; set; }
    public bool PuedeGestionar => !SoloLectura && UsuarioEsOperador;

    public bool EsLhRh => Pareja != null;
    public int? GrupoLhRh { get; set; }
    public string? LadoActual { get; set; }
    public string? LadoPareja { get; set; }

    public DateTime FechaHoraServidor { get; set; } = DateTime.Now;

    public Dictionary<long, string> EstatusCalidadPorCaja { get; set; } = new();

    public IEnumerable<ProduccionOperadorCajasVm> Lados
    {
        get
        {
            yield return Actual;
            if (Pareja != null) yield return Pareja;
        }
    }

    public IEnumerable<ProduccionOperadorCajaVm> TodasLasCajas =>
        Lados.SelectMany(x => x.Cajas ?? new List<ProduccionOperadorCajaVm>());

    public int TotalCajas => TodasLasCajas.Count();
    public int PendientesCalidad => TodasLasCajas.Count(x => x.EstadoCajaID == ProduccionCajaEstatus.PendienteCalidad);
    public int RetenidasCalidadGp12 => TodasLasCajas.Count(x => x.EstadoCajaID == ProduccionCajaEstatus.RetenidaGp12Scrap);
    public int EnZonaVerde => TodasLasCajas.Count(x => x.EstadoCajaID == ProduccionCajaEstatus.ZonaVerde);
    public int PendientesAlmacen => TodasLasCajas.Count(x => x.EstadoCajaID == ProduccionCajaEstatus.SalidaProduccion);
    public int RecibidasAlmacen => TodasLasCajas.Count(x => x.EstadoCajaID == ProduccionCajaEstatus.RecibidaAlmacenPt);
    public int ProductoIncompleto => TodasLasCajas.Count(x => x.EsCajaIncompleta);

    public bool RequiereAutoRefresco =>
        PendientesCalidad > 0 ||
        RetenidasCalidadGp12 > 0 ||
        PendientesAlmacen > 0;

    public string ObtenerEstatusCalidad(long cajaProduccionId)
    {
        return EstatusCalidadPorCaja.TryGetValue(cajaProduccionId, out var estado)
            ? estado
            : string.Empty;
    }

    public bool CajaDevuelta(ProduccionOperadorCajaVm caja) =>
        string.Equals(ObtenerEstatusCalidad(caja.CajaProduccionID), "DEVUELTA", StringComparison.OrdinalIgnoreCase);

    public bool CajaCorregida(ProduccionOperadorCajaVm caja) =>
        string.Equals(ObtenerEstatusCalidad(caja.CajaProduccionID), "CORREGIDA", StringComparison.OrdinalIgnoreCase);
}
