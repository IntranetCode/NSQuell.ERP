using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ERP.NSQuell.Models.ViewModels
{
    public class ParteIndexViewModel
    {
        public string? Busqueda { get; set; }

        public string? EstadoFiltro { get; set; }

        public int TotalMostradas { get; set; }

        public int TotalActivas { get; set; }

        public int TotalInactivas { get; set; }

        public int TotalStockConfigurado { get; set; }

        public List<SelectListItem> Maquinas { get; set; } = new();
        public List<SelectListItem> Moldes { get; set; } = new();

        public List<ParteListadoItemViewModel> Partes { get; set; } = new();
    }

    public class ParteListadoItemViewModel
    {
        public int ParteID { get; set; }

        public string NumeroParte { get; set; } = string.Empty;

        public string? ReferenciaSAP { get; set; }

        public string Descripcion { get; set; } = string.Empty;

        public string? ClienteNombre { get; set; }

        public string? MaquinaPrincipal { get; set; }

        public string? MoldePrincipal { get; set; }

        public int StockMinimo { get; set; }

        public int StockAviso { get; set; }

        public bool StockConfigurado { get; set; }

        public bool Activo { get; set; }

        public bool TieneDatosTecnicos { get; set; }

        public string? Color { get; set; }

        public int? Cavidades { get; set; }

        public decimal? ObjetivoHora { get; set; }

        public string? Material { get; set; }
    }

    public class ParteFormViewModel
    {
        public int? ParteID { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "Debe seleccionar un cliente.")]
        public int ClienteID { get; set; }

        [Required(ErrorMessage = "El numero de parte es obligatorio.")]
        [StringLength(120)]
        public string NumeroParte { get; set; } = string.Empty;

        [StringLength(120)]
        public string? ReferenciaSAP { get; set; }

        [Required(ErrorMessage = "La descripcion es obligatoria.")]
        [StringLength(300)]
        public string Descripcion { get; set; } = string.Empty;

        [StringLength(200)]
        public string? Designacion { get; set; }

      

        public bool RequiereGP12 { get; set; }

        public bool RequiereCertificado { get; set; }

     

        public bool Activo { get; set; } = true;

        [Range(0, int.MaxValue, ErrorMessage = "El stock mínimo no puede ser negativo.")]
        public int StockMinimo { get; set; }

        [Range(0, int.MaxValue, ErrorMessage = "El stock de aviso no puede ser negativo.")]
        public int StockAviso { get; set; }

        public bool StockConfigurado { get; set; }

        [ValidateNever]
        public List<SelectListItem> Clientes { get; set; } = new();


        [StringLength(1000, ErrorMessage = "Las notas no pueden superar los 1000 caracteres.")]
        public string? Notas { get; set; }

        [Range(typeof(decimal), "0", "9999999999999999",
            ErrorMessage = "El precio de venta no puede ser negativo.")]
        public decimal? PrecioVentaUnitario { get; set; }

        [StringLength(10)]
        public string? MonedaPrecioVenta { get; set; }

        [StringLength(50)]
        public string? UnidadPrecioVenta { get; set; }

        [StringLength(50)]
        public string? FuentePrecioVenta { get; set; }

        [StringLength(100)]
        public string? ClavePrecioVentaOrigen { get; set; }

        [StringLength(250)]
        public string? DescripcionPrecioVentaOrigen { get; set; }

        [DataType(DataType.Date)]
        public DateTime? FechaPrecioVenta { get; set; }

        public bool EsModoCrear => !ParteID.HasValue || ParteID.Value <= 0;
    }

    public class ParteDatoTecnicoModalViewModel
    {
        public int? ParteDatoTecnicoID { get; set; }

        [Required]
        public int ParteID { get; set; }

        public string? NumeroParte { get; set; }

        public string? DescripcionParte { get; set; }

        [StringLength(80)]
        public string? Ciclo { get; set; }

        [StringLength(100)]
        public string? TipoSecado { get; set; }

        public decimal? HorasSecado { get; set; }

        public decimal? PesoBrutoPieza { get; set; }
        public decimal? PesoNetoPieza { get; set; }

        [StringLength(100)]
        public string? EmbalajeCodigo { get; set; }

        [StringLength(250)]
        public string? EmbalajeDescripcion { get; set; }

        public decimal? PiezasPorEmbalaje { get; set; }

        [StringLength(100)]
        public string? MaterialCodigo { get; set; }

        [StringLength(250)]
        public string? MaterialDescripcion { get; set; }

        public int? MaterialID { get; set; }

        public bool Activo { get; set; } = true;


        public string? Color { get; set; }

        [Range(0, int.MaxValue)]
        public int? Cavidades { get; set; }

        [Range(0, double.MaxValue)]
        public decimal? ObjetivoHora { get; set; }

        [Range(0, int.MaxValue)]
        public int? PiezasPorCaja { get; set; }

        public int? MaquinaPrincipalID { get; set; }
        public int? MaquinaSustitutaID { get; set; }
        public int? MoldePrincipalID { get; set; }


    }
}
