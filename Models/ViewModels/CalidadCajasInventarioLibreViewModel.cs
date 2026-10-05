using System;
using System.Collections.Generic;

namespace ERP.NSQuell.Models.ViewModels
{
    public class CalidadCajasInventarioLibreVm
    {
        public List<CalidadCajaInventarioLibreItemVm> Items { get; set; } = new();
        public List<CalidadCajaUbicacionVm> Ubicaciones { get; set; } = new();

        public int TotalCajas { get; set; }
        public long TotalPiezas { get; set; }
    }

    public class CalidadCajaInventarioLibreItemVm
    {
        public long CalidadCajaInventarioID { get; set; }

        public string CodigoBarras { get; set; } = string.Empty;

        public int ParteID { get; set; }

        public string? NumeroOF { get; set; }

        public string NumeroParteEtiqueta { get; set; } = string.Empty;

        public string? DesignacionEtiqueta { get; set; }

        public int Cantidad { get; set; }

        public string? LoteEtiqueta { get; set; }

        public string Estado { get; set; } = string.Empty;

        public DateTime FechaEscaneoCalidad { get; set; }

        public DateTime? FechaEnvioAlmacen { get; set; }

        public int? AlmacenPTCajaID { get; set; }

        public string? NumeroParteCatalogo { get; set; }

        public string? DescripcionCatalogo { get; set; }
    }

    public class CalidadCajaUbicacionVm
    {
        public int UbicacionID { get; set; }

        public string Texto { get; set; } = string.Empty;
    }

    public class CalidadCajaConversionLibreVm
    {
        public int Renglon { get; set; }

        public string CodigoOriginal { get; set; } = string.Empty;

        public bool Valido { get; set; }

        public string Mensaje { get; set; } = string.Empty;

        public int ParteID { get; set; }

        public string NumeroOF { get; set; } = string.Empty;

        public string NumeroParte { get; set; } = string.Empty;

        public string Designacion { get; set; } = string.Empty;

        public int Cantidad { get; set; }

        public string Lote { get; set; } = string.Empty;

        public string NumeroParteCatalogo { get; set; } = string.Empty;

        public string DescripcionCatalogo { get; set; } = string.Empty;
    }
}