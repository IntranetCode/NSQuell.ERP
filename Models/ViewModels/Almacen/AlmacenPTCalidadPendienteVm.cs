using System;

namespace ERP.NSQuell.Models.ViewModels.Almacen
{
    // NSQ_ALMACENPT_PENDIENTES_CALIDAD_V1_3
    public sealed class AlmacenPTCalidadPendienteVm
    {
        public long CalidadCajaInventarioID { get; set; }
        public string CodigoBarras { get; set; } = string.Empty;
        public string? NumeroOF { get; set; }
        public string NumeroParte { get; set; } = string.Empty;
        public string Designacion { get; set; } = string.Empty;
        public int Cantidad { get; set; }
        public string? Lote { get; set; }
        public DateTime? FechaClasificacion { get; set; }
    }
}