using System;
using System.Collections.Generic;

namespace ERP.NSQuell.Models.ViewModels
{
    // NSQ_GP12_RECEPCION_CALIDAD_V1_3
    public sealed class GP12RecepcionCalidadVm
    {
        public List<GP12RecepcionCalidadItemVm> Items { get; set; } = new();

        public int Pendientes =>
            Items.FindAll(x => x.Estado == "PENDIENTE_GP12").Count;

        public int EnGP12 =>
            Items.FindAll(x => x.Estado == "EN_GP12").Count;
    }

    public sealed class GP12RecepcionCalidadItemVm
    {
        public long CalidadCajaInventarioID { get; set; }
        public int SolicitudGP12ID { get; set; }
        public string CodigoBarras { get; set; } = string.Empty;
        public string? NumeroOF { get; set; }
        public string NumeroParte { get; set; } = string.Empty;
        public string Designacion { get; set; } = string.Empty;
        public int Cantidad { get; set; }
        public string? Lote { get; set; }
        public string Estado { get; set; } = string.Empty;
        public DateTime? FechaClasificacion { get; set; }
        public DateTime? FechaRecepcionGP12 { get; set; }
        public decimal CantidadRecibida { get; set; }
        public decimal CantidadProcesada { get; set; }
        public bool TieneActividad { get; set; }

        public bool PuedeRecibir =>
            Estado == "PENDIENTE_GP12" &&
            CantidadRecibida <= 0;

        public bool PuedeIniciarRevision =>
            Estado == "EN_GP12" &&
            CantidadRecibida > CantidadProcesada &&
            !TieneActividad;
    }
}