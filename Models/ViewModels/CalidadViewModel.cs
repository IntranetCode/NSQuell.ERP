using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ERP.NSQuell.Models.ViewModels
{
    public class CalidadIndexViewModel
    {
        public string? Busqueda { get; set; }
        public string? EstadoFiltro { get; set; }

        public int TotalMostrados { get; set; }
        public int TotalAbiertas { get; set; }
        public int TotalLiberadas { get; set; }
        public int TotalGPI2 { get; set; }
        public int TotalContencion { get; set; }
        public int TotalScrap { get; set; }
        public int TotalCerradas { get; set; }

        public List<CalidadListadoItemViewModel> Inspecciones { get; set; } = new();
    }

    public class CalidadListadoItemViewModel
    {
        public int InspeccionID { get; set; }

        public string? CodigoBarras { get; set; }
        public string? OrdenTrabajo { get; set; }
        public string? NumeroParte { get; set; }
        public string? Material { get; set; }
        public string? Proceso { get; set; }
        public string? Maquina { get; set; }

        public decimal CantidadTotal { get; set; }
        public decimal CantidadRevisada { get; set; }
        public decimal CantidadPendiente { get; set; }

        public string? ResultadoCalidad { get; set; }
        public string? Etiqueta { get; set; }
        public string Estado { get; set; } = "ABIERTA";

        public DateTime FechaCreacion { get; set; }
    }

    public class CalidadFormViewModel
    {
        [StringLength(150, ErrorMessage = "El código de barras no puede superar los 150 caracteres.")]
        public string? CodigoBarras { get; set; }

        [StringLength(120, ErrorMessage = "La orden de trabajo no puede superar los 120 caracteres.")]
        public string? OrdenTrabajo { get; set; }

        [StringLength(120, ErrorMessage = "El número de parte no puede superar los 120 caracteres.")]
        public string? NumeroParte { get; set; }

        [StringLength(250, ErrorMessage = "El material no puede superar los 250 caracteres.")]
        public string? Material { get; set; }

        [StringLength(200, ErrorMessage = "El proceso no puede superar los 200 caracteres.")]
        public string? Proceso { get; set; }

        [StringLength(150, ErrorMessage = "La máquina no puede superar los 150 caracteres.")]
        public string? Maquina { get; set; }

        [Range(0, 999999999, ErrorMessage = "La cantidad total no puede ser negativa.")]
        public decimal CantidadTotal { get; set; }

        [Range(0, 999999999, ErrorMessage = "La cantidad revisada no puede ser negativa.")]
        public decimal CantidadRevisada { get; set; }

        public bool ChecklistValidado { get; set; }
        public bool HojaInspeccionProducto { get; set; }
        public bool HojaValidacionCalidad { get; set; }

        [StringLength(30)]
        public string? ResultadoCalidad { get; set; }

        [StringLength(1000, ErrorMessage = "Las observaciones no pueden superar los 1000 caracteres.")]
        public string? Observaciones { get; set; }
    }

    public class CalidadDetalleViewModel
    {
        public int InspeccionID { get; set; }

        public string? CodigoBarras { get; set; }
        public string? OrdenTrabajo { get; set; }
        public string? NumeroParte { get; set; }
        public string? Material { get; set; }
        public string? Proceso { get; set; }
        public string? Maquina { get; set; }

        public decimal CantidadTotal { get; set; }
        public decimal CantidadRevisada { get; set; }
        public decimal CantidadPendiente { get; set; }

        public bool ChecklistValidado { get; set; }
        public bool HojaInspeccionProducto { get; set; }
        public bool HojaValidacionCalidad { get; set; }

        public string? ResultadoCalidad { get; set; }
        public string? Etiqueta { get; set; }

        public bool Liberado { get; set; }
        public bool RequiereGPI2 { get; set; }
        public bool EnContencion { get; set; }
        public bool EsScrap { get; set; }

        public string? Observaciones { get; set; }
        public string Estado { get; set; } = "ABIERTA";

        public int? UsuarioCreacionID { get; set; }
        public DateTime FechaCreacion { get; set; }

        public int? UsuarioModificacionID { get; set; }
        public DateTime? FechaModificacion { get; set; }

        public List<CalidadHistorialItemViewModel> Historial { get; set; } = new();
    }

    public class CalidadHistorialItemViewModel
    {
        public int HistorialID { get; set; }

        public string Movimiento { get; set; } = string.Empty;
        public string? EstadoAnterior { get; set; }
        public string? EstadoNuevo { get; set; }

        public string? ResultadoCalidad { get; set; }
        public string? Etiqueta { get; set; }

        public string? Comentario { get; set; }

        public int? UsuarioID { get; set; }
        public DateTime FechaMovimiento { get; set; }
    }
}