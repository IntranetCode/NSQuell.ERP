using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace ERP.NSQuell.Models
{
    public class Compras
    {
        // =========================================================
        // DASHBOARD PRINCIPAL DEL MODULO
        // =========================================================
        public class DashboardViewModel
        {
            public int SolicitudesPendientes { get; set; }
            public int SolicitudesEnRevision { get; set; }
            public int SolicitudesAprobadas { get; set; }
            public int SolicitudesRechazadas { get; set; }

            public int OrdenesPendientes { get; set; }
            public int OrdenesEnEsperaPago { get; set; }
            public int OrdenesRecibidasParcial { get; set; }
            public int OrdenesCerradas { get; set; }

            public int RecepcionesPendientes { get; set; }
            public int ProveedoresActivos { get; set; }

            public List<SolicitudListadoViewModel> UltimasSolicitudes { get; set; } = new();
        }

        // =========================================================
        // LISTADO DE SOLICITUDES
        // =========================================================
        public class SolicitudListadoViewModel
        {
            public int SolicitudCompraID { get; set; }
            public string? Folio { get; set; }

            public DateTime FechaSolicitud { get; set; }

            public string? OrigenSolicitud { get; set; }
            public string? Departamento { get; set; }
            public string? Solicitante { get; set; }

            public string? Prioridad { get; set; }
            public string? TipoCompra { get; set; }

            public int EstatusID { get; set; }
            public string? Estatus { get; set; }
            public string? EstatusNombre { get; set; }
            public string? ResponsableActual { get; set; }

            public DateTime? FechaUltimoMovimiento { get; set; }
            public int DiasEnEstatus { get; set; }

            public bool Activo { get; set; }

            public string PrioridadCss => Prioridad switch
            {
                "Urgente" => "badge bg-danger",
                "Alta" => "badge bg-warning text-dark",
                "Normal" => "badge bg-primary",
                "Baja" => "badge bg-secondary",
                _ => "badge bg-secondary"
            };

            public string EstatusCss => EstatusID switch
            {
                1 => "badge bg-warning text-dark",
                2 => "badge bg-info text-dark",
                3 => "badge bg-primary",
                4 => "badge bg-info",
                5 => "badge bg-success",
                6 => "badge bg-primary",
                7 => "badge bg-primary",
                8 => "badge bg-warning text-dark",
                9 => "badge bg-info text-dark",
                10 => "badge bg-success",
                11 => "badge bg-danger",
                12 => "badge bg-secondary",
                _ => "badge bg-secondary"
            };
        }

        // =========================================================
        // CREAR SOLICITUD
        // =========================================================
        public class CrearSolicitudViewModel
        {
            public int? DepartamentoID { get; set; }
            public int? AlmacenID { get; set; }

            [Required(ErrorMessage = "El origen de la solicitud es obligatorio.")]
            public string OrigenSolicitud { get; set; } = "Almacen";

            public string? PedidoClienteReferencia { get; set; }

            [Required(ErrorMessage = "La prioridad es obligatoria.")]
            public string Prioridad { get; set; } = "Normal";

            public string? TipoCompra { get; set; } = "Materia prima";

            [Required(ErrorMessage = "El motivo de la solicitud es obligatorio.")]
            public string? Motivo { get; set; }

            public string? Observaciones { get; set; }

            public List<SolicitudDetalleItemViewModel> Materiales { get; set; } = new();

            public List<SelectListItem> Departamentos { get; set; } = new();
            public List<SelectListItem> Almacenes { get; set; } = new();
            public List<SelectListItem> OrigenesSolicitud { get; set; } = new();
            public List<SelectListItem> Prioridades { get; set; } = new();
            public List<SelectListItem> TiposCompra { get; set; } = new();
            public List<SelectListItem> MaterialesCatalogo { get; set; } = new();
        }

        // =========================================================
        // DETALLE DE MATERIAL EN SOLICITUD
        // =========================================================
        public class SolicitudDetalleItemViewModel
        {
            public int SolicitudDetalleID { get; set; }

            [Required(ErrorMessage = "Seleccione un material.")]
            public int? ProductoID { get; set; }

            public string? CodigoMaterial { get; set; }
            public string? NombreMaterial { get; set; }
            public string? DescripcionMaterial { get; set; }
            public string? UnidadMedida { get; set; }

            [Required(ErrorMessage = "La cantidad solicitada es obligatoria.")]
            [Range(0.0001, double.MaxValue, ErrorMessage = "La cantidad debe ser mayor a 0.")]
            public decimal CantidadSolicitada { get; set; }

            public decimal? StockActual { get; set; }
            public decimal? StockMinimo { get; set; }

            public DateTime? FechaRequerida { get; set; }

            public bool AceptaSustituto { get; set; }

            public string? Observaciones { get; set; }

            public bool Activo { get; set; } = true;
        }

        // =========================================================
        // DETALLE COMPLETO DE SOLICITUD
        // =========================================================
        public class DetalleSolicitudViewModel
        {
            public int SolicitudCompraID { get; set; }
            public string? Folio { get; set; }

            public int? DepartamentoID { get; set; }
            public string? Departamento { get; set; }

            public int? AlmacenID { get; set; }
            public string? Almacen { get; set; }

            public int? SolicitadoPorUsuarioID { get; set; }
            public string? Solicitante { get; set; }

            public DateTime FechaSolicitud { get; set; }

            public string? OrigenSolicitud { get; set; }
            public string? PedidoClienteReferencia { get; set; }

            public string? Prioridad { get; set; }
            public string? TipoCompra { get; set; }

            public string? Motivo { get; set; }

            public int EstatusID { get; set; }
            public string? Estatus { get; set; }
            public string? EstatusNombre { get; set; }
            public string? ResponsableActual { get; set; }

            public string? Observaciones { get; set; }

            public bool Activo { get; set; }

            public DateTime? FechaUltimoMovimiento { get; set; }
            public int DiasEnEstatus { get; set; }

            public int? AprobadoPorUsuarioID { get; set; }
            public DateTime? FechaAprobacion { get; set; }

            public int? RechazadoPorUsuarioID { get; set; }
            public DateTime? FechaRechazo { get; set; }
            public string? MotivoRechazo { get; set; }

            public int? CompradorAsignadoUsuarioID { get; set; }
            public string? CompradorAsignado { get; set; }
            public DateTime? FechaAsignacionComprador { get; set; }

            public int? CotizacionSeleccionadaID { get; set; }
            public DateTime? FechaSeleccionCotizacion { get; set; }
            public int? UsuarioSeleccionCotizacionID { get; set; }
            public string? ComentariosSeleccionCotizacion { get; set; }

            public DateTime? FechaCierre { get; set; }
            public int? CerradoPorUsuarioID { get; set; }

            public List<SolicitudDetalleItemViewModel> Materiales { get; set; } = new();
            public List<AprobacionViewModel> Aprobaciones { get; set; } = new();
            public List<MaterialSustitutoViewModel> MaterialesSustitutos { get; set; } = new();

            public List<CotizacionCompraViewModel> Cotizaciones { get; set; } = new();
            public List<SelectListItem> Proveedores { get; set; } = new();

            public OrdenCompraViewModel? OrdenCompra { get; set; }

            public RecepcionCompraViewModel? Recepcion { get; set; }

            public List<HistorialCompraViewModel> Historial { get; set; } = new();

            public bool PuedeAprobarDireccion => EstatusID == 1 || EstatusID == 4;

            public bool PuedeRechazarDireccion => EstatusID == 1 || EstatusID == 4;

            public bool PuedeCotizar => EstatusID == 2 || EstatusID == 3;

            public bool PuedeSeleccionarCotizacion => EstatusID == 4 && Cotizaciones.Any();

            public bool PuedeGenerarOrdenCompra => EstatusID == 5;

            public bool PuedeEnviarProveedor => EstatusID == 6;

            public bool PuedeRecibirAlmacen => EstatusID == 8;

            public bool EstaCerrada => EstatusID == 10;

            public bool EstaRechazada => EstatusID == 11;

            public bool EstaCancelada => EstatusID == 12;

            public string EstatusCss => EstatusID switch
            {
                1 => "badge bg-warning text-dark",
                2 => "badge bg-info text-dark",
                3 => "badge bg-primary",
                4 => "badge bg-info",
                5 => "badge bg-success",
                6 => "badge bg-primary",
                7 => "badge bg-primary",
                8 => "badge bg-warning text-dark",
                9 => "badge bg-info text-dark",
                10 => "badge bg-success",
                11 => "badge bg-danger",
                12 => "badge bg-secondary",
                _ => "badge bg-secondary"
            };
        }

        // =========================================================
        // BANDEJAS DEL FLUJO DE COMPRAS
        // =========================================================
        public class BandejaDireccionViewModel
        {
            public List<SolicitudBandejaItemViewModel> PendientesAprobacion { get; set; } = new();
            public List<SolicitudBandejaItemViewModel> PendientesCotizacion { get; set; } = new();
            public List<SolicitudBandejaItemViewModel> Historico { get; set; } = new();

            public int TotalPendientesAprobacion => PendientesAprobacion.Count;
            public int TotalPendientesCotizacion => PendientesCotizacion.Count;
            public int TotalHistorico => Historico.Count;

            public int TotalRechazadas => Historico.Count(x => x.EstatusID == 11);
            public int TotalCerradas => Historico.Count(x => x.EstatusID == 10);
        }

        public class BandejaComprasViewModel
        {
            public List<SolicitudBandejaItemViewModel> AprobadasParaCotizar { get; set; } = new();
            public List<SolicitudBandejaItemViewModel> Cotizando { get; set; } = new();
            public List<SolicitudBandejaItemViewModel> ParaOrdenCompra { get; set; } = new();
            public List<SolicitudBandejaItemViewModel> OrdenesGeneradas { get; set; } = new();
            public List<SolicitudBandejaItemViewModel> Historico { get; set; } = new();

            public int TotalAprobadasParaCotizar => AprobadasParaCotizar.Count;
            public int TotalCotizando => Cotizando.Count;
            public int TotalParaOrdenCompra => ParaOrdenCompra.Count;
            public int TotalOrdenesGeneradas => OrdenesGeneradas.Count;
            public int TotalHistorico => Historico.Count;
        }

        public class BandejaAlmacenViewModel
        {
            public List<SolicitudBandejaItemViewModel> PendientesRecepcion { get; set; } = new();
            public List<SolicitudBandejaItemViewModel> Historico { get; set; } = new();

            public int TotalPendientesRecepcion => PendientesRecepcion.Count;
            public int TotalHistorico => Historico.Count;
        }

        public class SolicitudBandejaItemViewModel
        {
            public int SolicitudCompraID { get; set; }
            public string? Folio { get; set; }

            public int? DepartamentoID { get; set; }
            public string? Departamento { get; set; }

            public int? SolicitadoPorUsuarioID { get; set; }
            public string? Solicitante { get; set; }

            public DateTime FechaSolicitud { get; set; }

            public string? OrigenSolicitud { get; set; }
            public string? Prioridad { get; set; }
            public string? TipoCompra { get; set; }

            public string? Motivo { get; set; }
            public string? Observaciones { get; set; }

            public int EstatusID { get; set; }
            public string? EstatusNombre { get; set; }
            public string? ResponsableActual { get; set; }

            public DateTime? FechaUltimoMovimiento { get; set; }
            public int DiasEnEstatus { get; set; }

            public int? CompradorAsignadoUsuarioID { get; set; }
            public string? CompradorAsignado { get; set; }

            public int TotalMateriales { get; set; }
            public int TotalCotizaciones { get; set; }

            public string PrioridadCss => Prioridad switch
            {
                "Urgente" => "badge bg-danger",
                "Alta" => "badge bg-warning text-dark",
                "Normal" => "badge bg-primary",
                "Baja" => "badge bg-secondary",
                _ => "badge bg-secondary"
            };

            public string EstatusCss => EstatusID switch
            {
                1 => "badge bg-warning text-dark",
                2 => "badge bg-info text-dark",
                3 => "badge bg-primary",
                4 => "badge bg-info",
                5 => "badge bg-success",
                6 => "badge bg-primary",
                7 => "badge bg-primary",
                8 => "badge bg-warning text-dark",
                9 => "badge bg-info text-dark",
                10 => "badge bg-success",
                11 => "badge bg-danger",
                12 => "badge bg-secondary",
                _ => "badge bg-secondary"
            };
        }

        // =========================================================
        // ACCIONES DEL FLUJO
        // =========================================================
        public class AprobarSolicitudViewModel
        {
            public int SolicitudCompraID { get; set; }

            public string? Comentario { get; set; }

            public int? CompradorAsignadoUsuarioID { get; set; }

            public List<SelectListItem> Compradores { get; set; } = new();
        }

        public class RechazarSolicitudViewModel
        {
            public int SolicitudCompraID { get; set; }

            [Required(ErrorMessage = "Debes capturar el motivo del rechazo.")]
            public string? MotivoRechazo { get; set; }
        }

        public class CotizacionCompraViewModel
        {
            public int CotizacionID { get; set; }

            public int SolicitudCompraID { get; set; }

            public int? ProveedorID { get; set; }

            [Required(ErrorMessage = "Captura el nombre del proveedor.")]
            public string? ProveedorNombre { get; set; }

            public decimal? Subtotal { get; set; }
            public decimal? IVA { get; set; }

            [Required(ErrorMessage = "Captura el total de la cotización.")]
            [Range(0.01, double.MaxValue, ErrorMessage = "El total debe ser mayor a cero.")]
            public decimal? Total { get; set; }

            public string? TiempoEntrega { get; set; }
            public string? CondicionesPago { get; set; }

            public IFormFile? ArchivoCotizacion { get; set; }

            public string? ArchivoCotizacionPath { get; set; }
            public string? NombreArchivoOriginal { get; set; }
            public string? ExtensionArchivo { get; set; }
            public string? ContentType { get; set; }
            public long? TamanoBytes { get; set; }

            public bool EsSeleccionada { get; set; }
            public bool EsRecomendada { get; set; }

            public string? Estatus { get; set; }
            public string? Observaciones { get; set; }

            public int? SubidaPorUsuarioID { get; set; }
            public string? SubidaPor { get; set; }

            public DateTime FechaCotizacion { get; set; }
            public bool Activo { get; set; } = true;
        }

        public class SeleccionarCotizacionViewModel
        {
            public int SolicitudCompraID { get; set; }

            [Required(ErrorMessage = "Selecciona una cotización.")]
            public int CotizacionID { get; set; }

            public string? ComentariosSeleccion { get; set; }
        }

        public class OrdenCompraViewModel
        {
            public int OrdenCompraID { get; set; }

            public int SolicitudCompraID { get; set; }

            public int? CotizacionID { get; set; }

            [Required(ErrorMessage = "Captura el número de orden de compra.")]
            public string? NumeroOC { get; set; }

            public string? Folio { get; set; }

            public int? ProveedorID { get; set; }
            public string? ProveedorNombre { get; set; }

            public int? CreadoPorUsuarioID { get; set; }
            public string? CreadoPor { get; set; }

            public DateTime FechaOrden { get; set; }

            public DateTime? FechaEnvioProveedor { get; set; }
            public int? EnviadoProveedorPorUsuarioID { get; set; }

            public DateTime? FechaEntregaEstimada { get; set; }

            public decimal? Subtotal { get; set; }
            public decimal? IVA { get; set; }
            public decimal? Total { get; set; }

            public string? Estatus { get; set; }
            public string? Observaciones { get; set; }

            public bool Activo { get; set; } = true;
        }

        public class EnviarOrdenProveedorViewModel
        {
            public int SolicitudCompraID { get; set; }

            public int OrdenCompraID { get; set; }

            [Required(ErrorMessage = "Captura la fecha estimada de entrega.")]
            public DateTime? FechaEntregaEstimada { get; set; }

            public string? Observaciones { get; set; }
        }

        public class RecepcionCompraViewModel
        {
            public int RecepcionID { get; set; }

            public int SolicitudCompraID { get; set; }

            public int? OrdenCompraID { get; set; }

            public int? RecibidoPorUsuarioID { get; set; }
            public string? RecibidoPor { get; set; }

            public DateTime FechaRecepcion { get; set; }

            public string? EstatusRecepcion { get; set; }

            public string? DocumentoRemision { get; set; }

            public IFormFile? EvidenciaRecepcion { get; set; }

            public string? EvidenciaRecepcionPath { get; set; }
            public string? NombreArchivoEvidencia { get; set; }
            public string? ExtensionArchivoEvidencia { get; set; }
            public string? ContentTypeEvidencia { get; set; }
            public long? TamanoBytesEvidencia { get; set; }

            public string? Observaciones { get; set; }

            public bool Activo { get; set; } = true;
        }

        // =========================================================
        // HISTORIAL Y SEGUIMIENTO
        // =========================================================
        public class HistorialCompraViewModel
        {
            public int HistorialID { get; set; }

            public int SolicitudCompraID { get; set; }

            public int EstatusID { get; set; }

            public string? EstatusNombre { get; set; }

            public string? Comentario { get; set; }

            public int? UsuarioID { get; set; }

            public string? UsuarioNombre { get; set; }

            public DateTime FechaMovimiento { get; set; }
        }

        public class SeguimientoComprasViewModel
        {
            public List<SolicitudBandejaItemViewModel> Solicitudes { get; set; } = new();

            public int TotalSolicitudes => Solicitudes.Count;

            public int TotalActivas => Solicitudes.Count(x =>
                x.EstatusID != 10 &&
                x.EstatusID != 11 &&
                x.EstatusID != 12
            );

            public int TotalCerradas => Solicitudes.Count(x => x.EstatusID == 10);
            public int TotalRechazadas => Solicitudes.Count(x => x.EstatusID == 11);
            public int TotalCanceladas => Solicitudes.Count(x => x.EstatusID == 12);

            public int TotalRetrasadas => Solicitudes.Count(x =>
                x.DiasEnEstatus >= 3 &&
                x.EstatusID != 10 &&
                x.EstatusID != 11 &&
                x.EstatusID != 12
            );
        }

        // =========================================================
        // APROBACIONES
        // =========================================================
        public class AprobacionViewModel
        {
            public int AprobacionID { get; set; }
            public int SolicitudCompraID { get; set; }

            public string? TipoAprobacion { get; set; }
            public int? AprobadoPorUsuarioID { get; set; }
            public string? AprobadoPor { get; set; }

            public DateTime? FechaAprobacion { get; set; }

            public string? Estatus { get; set; }
            public string? Comentario { get; set; }

            public bool Activo { get; set; }
        }

        // =========================================================
        // MATERIALES SUSTITUTOS
        // =========================================================
        public class MaterialSustitutoViewModel
        {
            public int SustitutoID { get; set; }
            public int SolicitudDetalleID { get; set; }

            public int? ProductoSustitutoID { get; set; }

            [Required(ErrorMessage = "La descripción del sustituto es obligatoria.")]
            public string? DescripcionSustituto { get; set; }

            public int? PropuestoPorUsuarioID { get; set; }
            public string? PropuestoPor { get; set; }

            public DateTime FechaPropuesta { get; set; }

            public string? Motivo { get; set; }

            public string? Estatus { get; set; }

            public int? AprobadoPorUsuarioID { get; set; }
            public string? AprobadoPor { get; set; }

            public DateTime? FechaAprobacion { get; set; }

            public string? Observaciones { get; set; }

            public bool Activo { get; set; }
        }

        // =========================================================
        // PROVEEDORES
        // =========================================================
        public class ProveedorViewModel
        {
            public int ProveedorID { get; set; }

            [Required(ErrorMessage = "El nombre del proveedor es obligatorio.")]
            public string? Nombre { get; set; }

            public string? RFC { get; set; }
            public string? Telefono { get; set; }

            [EmailAddress(ErrorMessage = "El correo no tiene un formato válido.")]
            public string? Correo { get; set; }

            public string? Contacto { get; set; }
            public string? Direccion { get; set; }

            public int? DepartamentoPrincipalID { get; set; }
            public string? DepartamentoPrincipal { get; set; }

            public string? TipoProveedor { get; set; }
            public string? Observaciones { get; set; }

            public bool Activo { get; set; }
            public DateTime FechaRegistro { get; set; }
        }

        // =========================================================
        // ORDENES DE COMPRA
        // =========================================================
        public class OrdenListadoViewModel
        {
            public int OrdenCompraID { get; set; }
            public string? Folio { get; set; }

            public int SolicitudCompraID { get; set; }
            public string? FolioSolicitud { get; set; }

            public int? ProveedorID { get; set; }
            public string? Proveedor { get; set; }

            public int? CreadoPorUsuarioID { get; set; }
            public string? CreadoPor { get; set; }

            public DateTime FechaOrden { get; set; }
            public DateTime? FechaEntregaEstimada { get; set; }
            public DateTime? FechaEnvioProveedor { get; set; }

            public decimal? Subtotal { get; set; }
            public decimal? IVA { get; set; }
            public decimal? Total { get; set; }

            public string? Estatus { get; set; }
            public bool Activo { get; set; }
        }

        public class CrearOrdenViewModel
        {
            public int SolicitudCompraID { get; set; }

            public int? ProveedorID { get; set; }

            public DateTime? FechaEntregaEstimada { get; set; }

            public string? Observaciones { get; set; }

            public List<OrdenDetalleItemViewModel> Materiales { get; set; } = new();

            public List<SelectListItem> Proveedores { get; set; } = new();
        }

        public class OrdenDetalleItemViewModel
        {
            public int OrdenDetalleID { get; set; }
            public int? SolicitudDetalleID { get; set; }

            public int? ProductoID { get; set; }

            public string? DescripcionMaterial { get; set; }
            public string? UnidadMedida { get; set; }

            public decimal CantidadOrdenada { get; set; }
            public decimal? PrecioUnitario { get; set; }
            public decimal? Importe { get; set; }

            public string? Observaciones { get; set; }

            public bool Activo { get; set; } = true;
        }

        // =========================================================
        // PAGOS / COMPROBANTES
        // =========================================================
        public class PagoViewModel
        {
            public int PagoID { get; set; }
            public int OrdenCompraID { get; set; }

            [Required(ErrorMessage = "El tipo de pago es obligatorio.")]
            public string? TipoPago { get; set; }

            [Required(ErrorMessage = "El monto es obligatorio.")]
            [Range(0.01, double.MaxValue, ErrorMessage = "El monto debe ser mayor a 0.")]
            public decimal Monto { get; set; }

            public DateTime FechaPago { get; set; }

            public string? ComprobanteArchivo { get; set; }

            public int? RegistradoPorUsuarioID { get; set; }
            public string? RegistradoPor { get; set; }

            public string? Estatus { get; set; }
            public string? Observaciones { get; set; }

            public bool Activo { get; set; }
        }

        // =========================================================
        // RECEPCIONES
        // =========================================================
        public class RecepcionListadoViewModel
        {
            public int RecepcionID { get; set; }

            public int? SolicitudCompraID { get; set; }

            public int? OrdenCompraID { get; set; }

            public string? FolioSolicitud { get; set; }
            public string? FolioOrden { get; set; }
            public string? Proveedor { get; set; }

            public int? RecibidoPorUsuarioID { get; set; }
            public string? RecibidoPor { get; set; }

            public DateTime FechaRecepcion { get; set; }

            public string? DocumentoRemision { get; set; }

            public string? EstatusRecepcion { get; set; }
            public string? ValidacionCalidad { get; set; }

            public string? EvidenciaRecepcionPath { get; set; }
            public string? NombreArchivoEvidencia { get; set; }
            public string? ExtensionArchivoEvidencia { get; set; }

            public string? Observaciones { get; set; }

            public bool Activo { get; set; }
        }

        public class RegistrarRecepcionViewModel
        {
            public int OrdenCompraID { get; set; }

            public int? SolicitudCompraID { get; set; }

            public string? DocumentoRemision { get; set; }

            public string EstatusRecepcion { get; set; } = "Pendiente";

            public string ValidacionCalidad { get; set; } = "Pendiente";

            public IFormFile? EvidenciaRecepcion { get; set; }

            public string? Observaciones { get; set; }

            public List<RecepcionDetalleItemViewModel> Materiales { get; set; } = new();
        }

        public class RecepcionDetalleItemViewModel
        {
            public int RecepcionDetalleID { get; set; }
            public int OrdenDetalleID { get; set; }

            public int? ProductoID { get; set; }

            public string? DescripcionMaterial { get; set; }

            public decimal CantidadOrdenada { get; set; }

            [Range(0, double.MaxValue, ErrorMessage = "La cantidad recibida no puede ser negativa.")]
            public decimal CantidadRecibida { get; set; }

            public decimal? CantidadPendiente { get; set; }

            public string? EstadoMaterial { get; set; }

            public string? Observaciones { get; set; }

            public bool Activo { get; set; } = true;
        }

        public class RegistrarCotizacionesViewModel
        {
            public int SolicitudCompraID { get; set; }

            public string? Folio { get; set; }
            public int EstatusID { get; set; }
            public string? EstatusNombre { get; set; }

            public List<SelectListItem> Proveedores { get; set; } = new();

            public List<CotizacionCompraViewModel> Cotizaciones { get; set; } = new();

            public List<CotizacionCompraViewModel> CotizacionesExistentes { get; set; } = new();

            public List<SolicitudDetalleItemViewModel> Materiales { get; set; } = new();
        }
    }
}