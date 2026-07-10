using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Collections.Generic;

namespace ERP.NSQuell.Models.ERP
{
    //ENTIDADES DE LAS TABLAS
    public class SolicitudProduccion
    {
        public int SolicitudProduccionID { get; set; }

        public string? FolioSolicitud { get; set; }
        public string? NumeroOFRecibida { get; set; }

        public DateTime FechaSolicitud { get; set; }
        public DateTime? FechaRequerida { get; set; }

        public int? ClienteID { get; set; }
        public string? ClienteNombre { get; set; }

        public string? OrigenSolicitud { get; set; }
        public string Prioridad { get; set; } = "Normal";

        public int EstatusID { get; set; } = 1;

        public string? NotasGenerales { get; set; }

        public int UsuarioCreacionID { get; set; }
        public DateTime FechaCreacion { get; set; }

        public int? UsuarioModificacionID { get; set; }
        public DateTime? FechaModificacion { get; set; }

        public bool Activo { get; set; } = true;

        public List<SolicitudProduccionDetalle> Detalles { get; set; } = new();
    }

    public class SolicitudProduccionDetalle
    {
        public int SolicitudProduccionDetalleID { get; set; }

        public int SolicitudProduccionID { get; set; }
        public int Renglon { get; set; }

        public int? ParteID { get; set; }
        public int? MoldeID { get; set; }
        public int? MaquinaSugeridaID { get; set; }

        public string DesignacionDescripcionSAP { get; set; } = string.Empty;
        public string ReferenciaSAP { get; set; } = string.Empty;

        public int CantidadPiezas { get; set; }
        public decimal? HorasPlaneadas { get; set; }

        public string? NumeroMoldeTexto { get; set; }
        public string? MaquinaSugeridaTexto { get; set; }

        public string? Color { get; set; }
        public int? Cavidades { get; set; }
        public int? ObjetivoHora { get; set; }
        public int? PiezasPorCaja { get; set; }

        public string? Notas { get; set; }

        public int EstatusID { get; set; } = 1;

        public bool Activo { get; set; } = true;

        public DateTime FechaCreacion { get; set; }

        public int? UsuarioModificacionID { get; set; }
        public DateTime? FechaModificacion { get; set; }

        public List<SolicitudProduccionAsignacionMaquina> AsignacionesMaquina { get; set; } = new();
    }

    public class SolicitudProduccionAsignacionMaquina
    {
        public int AsignacionMaquinaID { get; set; }

        public int SolicitudProduccionDetalleID { get; set; }

        public int MaquinaID { get; set; }
        public int? MoldeID { get; set; }

        public int CantidadAsignada { get; set; }
        public decimal? HorasEstimadas { get; set; }

        public int Secuencia { get; set; } = 1;

        public string? CondicionProduccion { get; set; }

        public DateTime? FechaProgramadaTentativa { get; set; }

        public TimeSpan? HoraInicioTentativa { get; set; }
        public TimeSpan? HoraFinTentativa { get; set; }

        public int EstatusID { get; set; } = 1;

        public string? Observaciones { get; set; }

        public int UsuarioCreacionID { get; set; }
        public DateTime FechaCreacion { get; set; }

        public int? UsuarioModificacionID { get; set; }
        public DateTime? FechaModificacion { get; set; }

        public bool Activo { get; set; } = true;
    }

    public class SolicitudProduccionHistorial
    {
        public int HistorialID { get; set; }

        public int SolicitudProduccionID { get; set; }

        public int? EstatusAnteriorID { get; set; }
        public int EstatusNuevoID { get; set; }

        public string Movimiento { get; set; } = string.Empty;
        public string? Comentario { get; set; }

        public int UsuarioID { get; set; }

        public DateTime FechaMovimiento { get; set; }
    }


  //VIEW MODELS
    public class SolicitudProduccionIndexVm
    {
        public int SolicitudProduccionID { get; set; }

        public string? FolioSolicitud { get; set; }
        public string? NumeroOFRecibida { get; set; }

        public DateTime FechaSolicitud { get; set; }
        public DateTime? FechaRequerida { get; set; }

        public string? Cliente { get; set; }

        public string Prioridad { get; set; } = "Normal";

        public int EstatusID { get; set; }
        public string EstatusNombre { get; set; } = string.Empty;

        public int TotalRenglones { get; set; }
        public int TotalPiezas { get; set; }
    }

    public class SolicitudProduccionCrearVm
    {
        public string? FolioSolicitud { get; set; }
        public string? NumeroOFRecibida { get; set; }

        public DateTime FechaSolicitud { get; set; } = DateTime.Today;
        public DateTime? FechaRequerida { get; set; }

        public int? ClienteID { get; set; }
        public string? ClienteNombre { get; set; }

        public string? OrigenSolicitud { get; set; }
        public string Prioridad { get; set; } = "Normal";

        public string? NotasGenerales { get; set; }

        public List<SolicitudProduccionDetalleCrearVm> Detalles { get; set; } = new();

        public List<SelectListItem> Clientes { get; set; } = new();
        public List<SelectListItem> Partes { get; set; } = new();
        public List<SelectListItem> Moldes { get; set; } = new();
        public List<SelectListItem> Maquinas { get; set; } = new();
    }

    public class SolicitudProduccionDetalleCrearVm
    {
        public int Renglon { get; set; }

        public int? ParteID { get; set; }
        public int? MoldeID { get; set; }

        public string ReferenciaSAP { get; set; } = string.Empty;
        public string DesignacionDescripcionSAP { get; set; } = string.Empty;

        public int CantidadPiezas { get; set; }

        public decimal? HorasPlaneadas { get; set; }

        public string? NumeroMoldeTexto { get; set; }

        public string? Color { get; set; }
        public int? Cavidades { get; set; }
        public int? ObjetivoHora { get; set; }
        public int? PiezasPorCaja { get; set; }

        public string? Notas { get; set; }

        public List<SolicitudProduccionAsignacionMaquinaCrearVm> AsignacionesMaquina { get; set; } = new();
    }

    public class SolicitudProduccionAsignacionMaquinaCrearVm
    {
        public int MaquinaID { get; set; }
        public int? MoldeID { get; set; }

        public int CantidadAsignada { get; set; }

        public decimal? HorasEstimadas { get; set; }

        public int Secuencia { get; set; } = 1;

        public string? CondicionProduccion { get; set; }

        public DateTime? FechaProgramadaTentativa { get; set; }

        public TimeSpan? HoraInicioTentativa { get; set; }
        public TimeSpan? HoraFinTentativa { get; set; }

        public string? Observaciones { get; set; }
    }

    public class SolicitudProduccionDetalleVistaVm
    {
        public int SolicitudProduccionID { get; set; }

        public string? FolioSolicitud { get; set; }
        public string? NumeroOFRecibida { get; set; }

        public DateTime FechaSolicitud { get; set; }
        public DateTime? FechaRequerida { get; set; }

        public string? Cliente { get; set; }
        public string Prioridad { get; set; } = "Normal";

        public int EstatusID { get; set; }
        public string EstatusNombre { get; set; } = string.Empty;

        public string? NotasGenerales { get; set; }

        public List<SolicitudProduccionDetalleVistaRenglonVm> Detalles { get; set; } = new();
        public List<SolicitudProduccionHistorialVistaVm> Historial { get; set; } = new();
    }

    public class SolicitudProduccionDetalleVistaRenglonVm
    {
        public int SolicitudProduccionDetalleID { get; set; }

        public int Renglon { get; set; }

        public string ReferenciaSAP { get; set; } = string.Empty;
        public string DesignacionDescripcionSAP { get; set; } = string.Empty;

        public int CantidadPiezas { get; set; }

        public string? Molde { get; set; }
        public string? Color { get; set; }

        public int? Cavidades { get; set; }
        public int? ObjetivoHora { get; set; }
        public int? PiezasPorCaja { get; set; }

        public string? Notas { get; set; }

        public List<SolicitudProduccionAsignacionMaquinaVistaVm> AsignacionesMaquina { get; set; } = new();
    }

    public class SolicitudProduccionAsignacionMaquinaVistaVm
    {
        public int AsignacionMaquinaID { get; set; }

        public string Maquina { get; set; } = string.Empty;
        public string? Molde { get; set; }

        public int CantidadAsignada { get; set; }
        public decimal? HorasEstimadas { get; set; }

        public int Secuencia { get; set; }

        public string? CondicionProduccion { get; set; }

        public DateTime? FechaProgramadaTentativa { get; set; }

        public TimeSpan? HoraInicioTentativa { get; set; }
        public TimeSpan? HoraFinTentativa { get; set; }

        public string? Observaciones { get; set; }
    }

    public class SolicitudProduccionHistorialVistaVm
    {
        public DateTime FechaMovimiento { get; set; }

        public string Movimiento { get; set; } = string.Empty;
        public string? Comentario { get; set; }

        public int? EstatusAnteriorID { get; set; }
        public int EstatusNuevoID { get; set; }

        public string Usuario { get; set; } = string.Empty;
    }



    public static class SolicitudProduccionEstatus
    {
        public const int Capturada = 1;
        public const int EnRevisionPlaneacion = 2;
        public const int PendienteValidacionMP = 3;
        public const int MPInsuficiente = 4;
        public const int CompraMPEnProceso = 5;
        public const int MPRecibida = 6;
        public const int MPLiberadaCalidad = 7;
        public const int ListaProduccion = 8;
        public const int EnProduccion = 9;
        public const int Cerrada = 10;
        public const int Cancelada = 11;

        public static string Nombre(int estatusID)
        {
            return estatusID switch
            {
                Capturada => "Capturada",
                EnRevisionPlaneacion => "En revisión de Planeación",
                PendienteValidacionMP => "Pendiente validación de MP",
                MPInsuficiente => "MP insuficiente / Requiere compra",
                CompraMPEnProceso => "Compra de MP en proceso",
                MPRecibida => "MP recibida en almacén",
                MPLiberadaCalidad => "MP liberada por Calidad",
                ListaProduccion => "Lista para producción",
                EnProduccion => "En producción",
                Cerrada => "Cerrada",
                Cancelada => "Cancelada",
                _ => "Sin estatus"
            };
        }
    }

    public static class SolicitudProduccionCondicion
    {
        public const string TerminarProduccion = "T.P";
        public const string InterrumpirProduccion = "I.P";

        public static string Nombre(string? condicion)
        {
            return condicion switch
            {
                TerminarProduccion => "T.P - Terminar producción actual",
                InterrumpirProduccion => "I.P - Interrumpir producción actual",
                _ => "Sin condición"
            };
        }
    }
}