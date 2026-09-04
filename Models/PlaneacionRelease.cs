// ============================================================================
// MODELO UNIFICADO: PlaneacionRelease.cs
//
// BLOQUES INTEGRADOS EN ESTE ARCHIVO:
//   1. PlaneacionRelease.cs
//      Modelos principales de Releases, detalle, necesidades y estatus.
//
//   2. PlaneacionReleaseEditar.cs
//      ViewModel utilizado para editar Releases existentes.
//
//   3. PlaneacionReleaseImportacion.cs
//      Resultado y detalle de la importacion de documentos.
//
//   4. PlaneacionReleaseValidacion.cs
//      Lotes, documentos y estados para la validacion previa a importar.
//
//   5. PlaneacionReleaseVinculacion.cs
//      ViewModels para vincular partes pendientes a un Release.
// ============================================================================

using Microsoft.AspNetCore.Mvc.Rendering;
using System.Text.Json.Serialization;
using static ERP.NSQuell.Models.PlaneacionReleaseEstatus;

namespace ERP.NSQuell.Models
{
    #region ORIGEN: PlaneacionRelease.cs - Modelos principales

    public class PlaneacionReleaseIndexVm
    {
        public int ReleaseID { get; set; }
        public string? FolioRelease { get; set; }
        public int? ClienteID { get; set; }
        public string? ClienteNombre { get; set; }
        public DateTime FechaRecepcion { get; set; }
        public string? VersionRelease { get; set; }
        public int EstatusID { get; set; }
        public string? EstatusNombre { get; set; }
        public int TotalRenglones { get; set; }
        public int TotalPiezasRequeridas { get; set; }
        public int TotalPiezasAProducir { get; set; }
        public DateTime FechaCreacion { get; set; }

        public int TotalEntregas { get; set; }

        public string? FolioCliente { get; set; }

        public string? ArchivoOrigenNombre { get; set; }

        public string? PlantillaImportacion { get; set; }

        public bool ImportadoDesdeArchivo { get; set; }

        public DateTime? UltimaFechaRequerida { get; set; }

    }

    public class PlaneacionReleaseCrearVm
    {
        public int? ClienteID { get; set; }
        public string? ClienteNombre { get; set; }

        public string? FolioRelease { get; set; }

        public string? FolioCliente { get; set; }

        public DateTime FechaRecepcion { get; set; } = DateTime.Today;

        public string? VersionRelease { get; set; }

        public string? ArchivoOrigenNombre { get; set; }

        public IFormFile? ArchivoRelease { get; set; }

        public string? PlantillaImportacion { get; set; }

        public bool ImportadoDesdeArchivo { get; set; }

        public string? Observaciones { get; set; }

        public int EstatusID { get; set; }

        public string? NivelCriticidad { get; set; } = "NORMAL";

        public string? ComentarioCriticidad { get; set; }

        public List<PlaneacionReleaseRenglonCrearVm> Renglones { get; set; } = new();

        public List<SelectListItem> Clientes { get; set; } = new();

        public List<SelectListItem> Partes { get; set; } = new();
    }

    public class PlaneacionReleaseRenglonCrearVm
    {
        public int Renglon { get; set; }

        public int? ParteID { get; set; }

        public string? NumeroParte { get; set; }
        public string? ReferenciaSAP { get; set; }
        public string? DesignacionDescripcionSAP { get; set; }

        public string? Observaciones { get; set; }

        public string? UnidadMedidaCliente { get; set; }

        public string? ContratoCliente { get; set; }
        public List<PlaneacionReleaseEntregaCrearVm> Entregas { get; set; } = new();
    }

    public class PlaneacionReleaseEntregaCrearVm
    {
        public int SecuenciaEntrega { get; set; }

        public DateTime? FechaRequerida { get; set; }

        public int CantidadRequerida { get; set; }

        public DateTime? FechaCarga { get; set; }
    }

    public class PlaneacionReleaseDetalleCrearVm
    {
        public int? ReleaseDetalleID { get; set; }

        public int Renglon { get; set; }

        public int? ParteID { get; set; }
        public string? NumeroParte { get; set; }
        public string? ReferenciaSAP { get; set; }
        public string? DesignacionDescripcionSAP { get; set; }

        public DateTime? FechaRequerida { get; set; }
        public int CantidadRequerida { get; set; }

        public int? PTDisponibleAlCalcular { get; set; }
        public int? ProduccionProgramadaPendiente { get; set; }

        public int? PiezasDesdePT { get; set; }
        public int? PiezasAProducir { get; set; }

        public int? MaterialID { get; set; }
        public string? MaterialCodigo { get; set; }
        public string? MaterialDescripcion { get; set; }
        public decimal? PesoBrutoPieza { get; set; }

        public decimal? MPRequeridaKg { get; set; }
        public decimal? MPDisponibleKg { get; set; }

        public string? EmbalajeCodigo { get; set; }
        public string? EmbalajeDescripcion { get; set; }
        public decimal? PiezasPorEmbalaje { get; set; }
        public decimal? EmbalajeRequerido { get; set; }
        public decimal? EmbalajeDisponible { get; set; }

        public int? MoldeID { get; set; }
        public string? MoldeCodigo { get; set; }

        public int? MaquinaSugeridaID { get; set; }
        public string? MaquinaSugeridaCodigo { get; set; }
        public string? MaquinaSugeridaNombre { get; set; }

        public int? ObjetivoHora { get; set; }
        public decimal? HorasNecesarias { get; set; }

        public DateTime? FechaInicioSugerida { get; set; }
        public DateTime? FechaFinEstimada { get; set; }

        public bool? DaTiempo { get; set; }
        public string? MensajeCapacidad { get; set; }

        public int? ProgramaProduccionID { get; set; }
        public int? SolicitudProduccionID { get; set; }

        public DateTime? FechaCarga { get; set; }
        public int EstatusID { get; set; } = PlaneacionReleaseEstatus.Capturado;
    }

    public class PlaneacionReleaseDetalleVm
    {
        public int ReleaseID { get; set; }
        public string? FolioRelease { get; set; }

        public int? ClienteID { get; set; }
        public string? ClienteNombre { get; set; }

        public DateTime FechaRecepcion { get; set; }
        public string? VersionRelease { get; set; }
        public string? ArchivoOrigenNombre { get; set; }
        public string? Observaciones { get; set; }

        public int EstatusID { get; set; }
        public string? EstatusNombre { get; set; }

        public string? FolioCliente { get; set; }

        public string? PlantillaImportacion { get; set; }

        public bool ImportadoDesdeArchivo { get; set; }

        public List<PlaneacionReleaseDetalleRenglonVm> Detalles { get; set; } = new();

        public int TotalRenglones => Detalles.Count;
        public int TotalPiezasRequeridas => Detalles.Sum(x => x.CantidadRequerida);
        public int TotalPiezasDesdePT => Detalles.Sum(x => x.PiezasDesdePT ?? 0);
        public int TotalPiezasAProducir => Detalles.Sum(x => x.PiezasAProducir ?? 0);
        public decimal TotalMPRequeridaKg => Detalles.Sum(x => x.MPRequeridaKg ?? 0);
        public decimal TotalHorasNecesarias => Detalles.Sum(x => x.HorasNecesarias ?? 0);
    }

    public class PlaneacionReleaseDetalleRenglonVm
    {
        public int ReleaseDetalleID { get; set; }
        public int Renglon { get; set; }

        public int? ParteID { get; set; }
        public string? NumeroParte { get; set; }
        public string? ReferenciaSAP { get; set; }
        public string? DesignacionDescripcionSAP { get; set; }

        public DateTime FechaRequerida { get; set; }
        public int CantidadRequerida { get; set; }

        public int? PTDisponibleAlCalcular { get; set; }
        public int? ProduccionProgramadaPendiente { get; set; }

        public int? PiezasDesdePT { get; set; }
        public int? PiezasAProducir { get; set; }

        public int? MaterialID { get; set; }
        public string? MaterialCodigo { get; set; }
        public string? MaterialDescripcion { get; set; }
        public decimal? PesoBrutoPieza { get; set; }

        public decimal? MPRequeridaKg { get; set; }
        public decimal? MPDisponibleKg { get; set; }

        public string? EmbalajeCodigo { get; set; }
        public string? EmbalajeDescripcion { get; set; }
        public decimal? PiezasPorEmbalaje { get; set; }
        public decimal? EmbalajeRequerido { get; set; }
        public decimal? EmbalajeDisponible { get; set; }

        public int? MoldeID { get; set; }
        public string? MoldeCodigo { get; set; }

        public int? MaquinaSugeridaID { get; set; }
        public string? MaquinaSugeridaCodigo { get; set; }
        public string? MaquinaSugeridaNombre { get; set; }

        public int? ObjetivoHora { get; set; }
        public decimal? HorasNecesarias { get; set; }

        public DateTime? FechaInicioSugerida { get; set; }
        public DateTime? FechaFinEstimada { get; set; }

        public bool? DaTiempo { get; set; }
        public string? MensajeCapacidad { get; set; }

        public int? ProgramaProduccionID { get; set; }
        public int? SolicitudProduccionID { get; set; }

        public int EstatusID { get; set; }

        public int? ReleaseRenglonID { get; set; }
        public int? SecuenciaEntrega { get; set; }

        public DateTime? FechaCarga { get; set; }

        public string? UnidadMedidaCliente { get; set; }

        public string? ContratoCliente { get; set; }
    }


    public class PlaneacionNecesidadFiltroVm
    {
        public int? ClienteID { get; set; }
        public int? ParteID { get; set; }

        public DateTime? FechaDesde { get; set; }
        public DateTime? FechaHasta { get; set; }

        public bool SoloPendientes { get; set; }
        public bool SoloSinCapacidad { get; set; }
        public bool SoloSinMP { get; set; }

        public List<PlaneacionNecesidadVm> Necesidades { get; set; } = new();

        public List<SelectListItem> Clientes { get; set; } = new();
        public List<SelectListItem> Partes { get; set; } = new();

        public int TotalRenglones => Necesidades.Count;
        public int TotalRequerido => Necesidades.Sum(x => x.CantidadRequerida);
        public int TotalDesdePT => Necesidades.Sum(x => x.PiezasDesdePT ?? 0);
        public int TotalAProducir => Necesidades.Sum(x => x.PiezasAProducir ?? 0);
        public decimal TotalMPRequerida => Necesidades.Sum(x => x.MPRequeridaKg ?? 0);
        public decimal TotalHoras => Necesidades.Sum(x => x.HorasNecesarias ?? 0);

        public List<PlaneacionNecesidadPeriodoVm> ResumenPeriodos { get; set; } = new();
    }

    public class PlaneacionNecesidadVm
    {
        public int ReleaseID { get; set; }
        public int ReleaseDetalleID { get; set; }

        public string? FolioRelease { get; set; }

        public int? ClienteID { get; set; }
        public string? ClienteNombre { get; set; }

        public int Renglon { get; set; }

        public int? ParteID { get; set; }
        public string? NumeroParte { get; set; }
        public string? ReferenciaSAP { get; set; }
        public string? DesignacionDescripcionSAP { get; set; }

        public DateTime FechaRecepcion { get; set; }
        public DateTime FechaRequerida { get; set; }

        public int CantidadRequerida { get; set; }

        public int? PTDisponibleAlCalcular { get; set; }
        public int? ProduccionProgramadaPendiente { get; set; }

        public int? PiezasDesdePT { get; set; }
        public int? PiezasAProducir { get; set; }

        public int? MaterialID { get; set; }
        public string? MaterialCodigo { get; set; }
        public string? MaterialDescripcion { get; set; }

        public decimal? MPRequeridaKg { get; set; }
        public decimal? MPDisponibleKg { get; set; }

        public string? EmbalajeCodigo { get; set; }
        public string? EmbalajeDescripcion { get; set; }
        public decimal? EmbalajeRequerido { get; set; }
        public decimal? EmbalajeDisponible { get; set; }

        public int? MaquinaSugeridaID { get; set; }
        public string? MaquinaSugeridaCodigo { get; set; }
        public string? MaquinaSugeridaNombre { get; set; }

        public int? MoldeID { get; set; }
        public string? MoldeCodigo { get; set; }

        public int? ObjetivoHora { get; set; }
        public decimal? HorasNecesarias { get; set; }

        public DateTime? FechaInicioSugerida { get; set; }
        public DateTime? FechaFinEstimada { get; set; }

        public bool? DaTiempo { get; set; }
        public string? MensajeCapacidad { get; set; }

        public int EstatusID { get; set; }

        public int? ProgramaProduccionID { get; set; }
        public bool YaProgramado => ProgramaProduccionID.HasValue;

        public bool TieneMPInsuficiente =>
            (MPRequeridaKg ?? 0) > 0 &&
            (MPDisponibleKg ?? 0) < (MPRequeridaKg ?? 0);

        public bool TieneEmbalajeInsuficiente =>
            (EmbalajeRequerido ?? 0) > 0 &&
            (EmbalajeDisponible ?? 0) < (EmbalajeRequerido ?? 0);
    }


    public static class PlaneacionReleaseEstatus
    {
        public const int Capturado = 1;
        public const int Calculado = 2;
        public const int Programado = 3;
        public const int ConOF = 4;
        public const int Cerrado = 9;
        public const int Cancelado = 99;

        public static string Nombre(int estatusId)
        {
            return estatusId switch
            {
                Capturado => "Capturado",
                Calculado => "Calculado",
                Programado => "Programado",
                ConOF => "Con OF",
                Cerrado => "Cerrado",
                Cancelado => "Cancelado",
                _ => "Desconocido"
            };
        }

        public class PlaneacionNecesidadPeriodoVm
        {
            public string Periodo { get; set; } = string.Empty;

            public DateTime FechaDesde { get; set; }
            public DateTime FechaHasta { get; set; }

            public int Renglones { get; set; }

            public int CantidadRequerida { get; set; }
            public int PiezasDesdePT { get; set; }
            public int ProduccionProgramadaPendiente { get; set; }
            public int PiezasAProducir { get; set; }

            public decimal MPRequeridaKg { get; set; }
            public decimal HorasNecesarias { get; set; }
        }
    }

    #endregion

    #region ORIGEN: PlaneacionReleaseEditar.cs - Edicion de Releases

    public sealed class PlaneacionReleaseEditarVm : PlaneacionReleaseCrearVm
    {
        public int ReleaseID { get; set; }
        public bool ConfirmarImpacto { get; set; }

        public bool TienePlaneacionVinculada { get; set; }
        public bool TieneProgramaBloqueado { get; set; }
        public int ProgramasVinculados { get; set; }
    }

    #endregion

    #region ORIGEN: PlaneacionReleaseImportacion.cs - Importacion de documentos

    public sealed class PlaneacionReleaseImportacionResultadoVm
    {
        public DateTime FechaProceso { get; set; } = DateTime.Now;
        public string? ErrorGeneral { get; set; }
        public string? NotaGeneral { get; set; }
        public List<PlaneacionReleaseImportacionArchivoVm> Archivos { get; set; } = new();

        public int TotalArchivos => Archivos.Count;
        public int Exitosos => Archivos.Count(x => x.Estado == "CREADO");
        public int Pendientes => Archivos.Count(x => x.Estado == "PENDIENTE");
        public int Omitidos => Archivos.Count(x => x.Estado == "OMITIDO");
        public int Errores => Archivos.Count(x => x.Estado == "ERROR" || x.Estado == "NO_SOPORTADO");
        public int TotalEntregas => Archivos
            .Where(x => x.Estado == "CREADO" || x.Estado == "PENDIENTE")
            .Sum(x => x.TotalEntregas);
        public long TotalPiezas => Archivos
            .Where(x => x.Estado == "CREADO" || x.Estado == "PENDIENTE")
            .Sum(x => x.TotalPiezas);
    }

    public sealed class PlaneacionReleaseImportacionArchivoVm
    {
        public string Archivo { get; set; } = string.Empty;
        public string Estado { get; set; } = "PENDIENTE";
        public string Mensaje { get; set; } = string.Empty;
        public int? ReleaseID { get; set; }
        public string? FolioRelease { get; set; }
        public int? ClienteID { get; set; }
        public string? Cliente { get; set; }
        public string? Parte { get; set; }
        public string? Descripcion { get; set; }
        public string? Schedule { get; set; }
        public string? OrdenCliente { get; set; }
        public string? Version { get; set; }
        public string? ArchivoGuardado { get; set; }
        public bool RequiereVinculacion { get; set; }
        public int TotalEntregas { get; set; }
        public long TotalPiezas { get; set; }
        public int VersionesAnterioresCerradas { get; set; }
        public List<string> Advertencias { get; set; } = new();
    }

    #endregion

    #region ORIGEN: PlaneacionReleaseValidacion.cs - Validacion previa a importar

    public sealed class ReleaseValidacionLoteVm
    {
        public string LoteID { get; set; } = string.Empty;
        public DateTime FechaProceso { get; set; } = DateTime.Now;
        public int UsuarioID { get; set; }
        public string? ErrorGeneral { get; set; }
        public string? NotaGeneral { get; set; }
        public List<ReleaseValidacionDocumentoVm> Documentos { get; set; } = new();

        public int Total => Documentos.Count;
        public int Validados => Documentos.Count(x => x.Estado == ReleaseValidacionEstados.Validado);
        public int Pendientes => Documentos.Count(x => x.Estado == ReleaseValidacionEstados.Pendiente);
        public int Omitidos => Documentos.Count(x => x.Estado == ReleaseValidacionEstados.Omitido);
        public int Errores => Documentos.Count(x =>
            x.Estado == ReleaseValidacionEstados.Error ||
            x.Estado == ReleaseValidacionEstados.NoSoportado);
        public int Guardados => Documentos.Count(x => x.Estado == ReleaseValidacionEstados.Guardado);
    }

    public sealed class ReleaseValidacionDocumentoVm
    {
        public string DocumentoID { get; set; } = Guid.NewGuid().ToString("N");
        public string Archivo { get; set; } = string.Empty;
        public string ArchivoTemporal { get; set; } = string.Empty;
        public string Estado { get; set; } = ReleaseValidacionEstados.Pendiente;
        public string Mensaje { get; set; } = string.Empty;

        public string Plantilla { get; set; } = string.Empty;
        public string Sha256 { get; set; } = string.Empty;
        public DateTime? FechaDocumento { get; set; }

        public int? ClienteID { get; set; }
        public string? Cliente { get; set; }
        public string? FolioCliente { get; set; }
        public string? Version { get; set; }

        public int TotalEntregas { get; set; }
        public long TotalPiezas { get; set; }
        public int? ReleaseID { get; set; }
        public string? FolioRelease { get; set; }

        public List<string> Advertencias { get; set; } = new();
        public PlaneacionReleaseCrearVm ReleasePreparado { get; set; } = new();

        [JsonIgnore]
        public int PartesPendientes =>
            ReleasePreparado.Renglones.Count(x => !x.ParteID.HasValue);

        [JsonIgnore]
        public string PartesTexto =>
            string.Join(", ",
                ReleasePreparado.Renglones
                    .Select(x => x.NumeroParte ?? x.ReferenciaSAP)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    public sealed class ReleasePendientesValidarVm
    {
        public List<ReleaseValidacionLoteVm> Lotes { get; set; } = new();
        public int TotalPendientes => Lotes.Sum(x => x.Pendientes);
    }

    public static class ReleaseValidacionEstados
    {
        public const string Validado = "VALIDADO";
        public const string Pendiente = "PENDIENTE_VALIDACION";
        public const string Omitido = "OMITIDO";
        public const string Error = "ERROR";
        public const string NoSoportado = "NO_SOPORTADO";
        public const string Guardado = "GUARDADO";
    }

    #endregion

    #region ORIGEN: PlaneacionReleaseVinculacion.cs - Vinculacion de partes

    public sealed class PlaneacionReleaseVinculacionVm
    {
        public int ReleaseID { get; set; }
        public string? FolioRelease { get; set; }
        public int ClienteID { get; set; }
        public string? ClienteNombre { get; set; }
        public string? ArchivoOrigenNombre { get; set; }
        public List<PlaneacionReleaseVinculacionRenglonVm> Renglones { get; set; } = new();
        public List<SelectListItem> PartesActivas { get; set; } = new();
    }

    public sealed class PlaneacionReleaseVinculacionRenglonVm
    {
        public int ReleaseRenglonID { get; set; }
        public int Renglon { get; set; }
        public int? ParteID { get; set; }
        public bool ParteActiva { get; set; }
        public string? NumeroParteOriginal { get; set; }
        public string? ReferenciaOriginal { get; set; }
        public string? DescripcionOriginal { get; set; }
    }

    public sealed class PlaneacionReleaseVinculacionPostVm
    {
        public int ReleaseID { get; set; }
        public List<PlaneacionReleaseVinculacionItemPostVm> Renglones { get; set; } = new();
    }

    public sealed class PlaneacionReleaseVinculacionItemPostVm
    {
        public int ReleaseRenglonID { get; set; }
        public int? ParteID { get; set; }
    }

    #endregion

}
