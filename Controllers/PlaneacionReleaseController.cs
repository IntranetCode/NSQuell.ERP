
using ERP.NSQuell.Models;
using ERP.NSQuell.Servicios.Releases;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using System.Data;
using UglyToad.PdfPig;
using System.Text.RegularExpressions;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using static ERP.NSQuell.Models.PlaneacionReleaseEstatus;

namespace ERP.NSQuell.Controllers
{
    public partial class PlaneacionReleaseController : Controller
    {

        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _environment;

        public PlaneacionReleaseController(
            IConfiguration configuration,
            IWebHostEnvironment environment)
        {
            _configuration = configuration;
            _environment = environment;
        }

        private string ConnectionString =>
            _configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("No se encontró la cadena de conexión DefaultConnection.");

        [HttpGet]
        public async Task<IActionResult> Index(int? clienteId = null)
        {
            // RELEASE_FECHA_CARGA_AUTO_V1_0
            await AutocompletarFechasCargaImportadasAsync();

            var lista = new List<PlaneacionReleaseIndexVm>();

            const string sql = @"
SELECT
    r.ReleaseID,
    r.FolioRelease,
    r.FolioCliente,
    r.ClienteID,
    ISNULL(c.Nombre, r.ClienteNombre) AS ClienteNombre,
    r.FechaRecepcion,
    r.VersionRelease,
    r.ArchivoOrigenNombre,
    r.PlantillaImportacion,
    ISNULL(r.ImportadoDesdeArchivo, 0) AS ImportadoDesdeArchivo,
    r.EstatusID,
    r.FechaCreacion,

    COUNT(DISTINCT rr.ReleaseRenglonID) AS TotalRenglones,
    COUNT(d.ReleaseDetalleID) AS TotalEntregas,

    ISNULL(SUM(d.CantidadRequerida), 0) AS TotalPiezasRequeridas,
    ISNULL(SUM(ISNULL(d.PiezasAProducir, 0)), 0) AS TotalPiezasAProducir,
    -- RELEASE_HISTORICO_V1_0_1
    MAX(d.FechaRequerida) AS UltimaFechaRequerida
FROM dbo.Planeacion_Releases r
LEFT JOIN dbo.ERP_Clientes c
    ON c.ClienteID = r.ClienteID
LEFT JOIN dbo.Planeacion_ReleaseRenglones rr
    ON rr.ReleaseID = r.ReleaseID
   AND rr.Activo = 1
LEFT JOIN dbo.Planeacion_ReleaseDetalle d
    ON d.ReleaseID = r.ReleaseID
   AND d.Activo = 1
WHERE r.Activo = 1
  AND (@ClienteID IS NULL OR r.ClienteID = @ClienteID)
GROUP BY
    r.ReleaseID,
    r.FolioRelease,
    r.FolioCliente,
    r.ClienteID,
    ISNULL(c.Nombre, r.ClienteNombre),
    r.FechaRecepcion,
    r.VersionRelease,
    r.ArchivoOrigenNombre,
    r.PlantillaImportacion,
    ISNULL(r.ImportadoDesdeArchivo, 0),
    r.EstatusID,
    r.FechaCreacion
ORDER BY r.FechaCreacion DESC;";

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@ClienteID", SqlDbType.Int).Value =
                (object?)clienteId ?? DBNull.Value;
            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                var estatusId = Convert.ToInt32(rd["EstatusID"]);

                lista.Add(new PlaneacionReleaseIndexVm
                {
                    ReleaseID = Convert.ToInt32(rd["ReleaseID"]),
                    FolioRelease = rd["FolioRelease"] as string,
                    FolioCliente = rd["FolioCliente"] as string,
                    ClienteID = rd["ClienteID"] == DBNull.Value ? null : Convert.ToInt32(rd["ClienteID"]),
                    ClienteNombre = rd["ClienteNombre"] as string,
                    FechaRecepcion = Convert.ToDateTime(rd["FechaRecepcion"]),
                    VersionRelease = rd["VersionRelease"] as string,
                    ArchivoOrigenNombre = rd["ArchivoOrigenNombre"] as string,
                    PlantillaImportacion = rd["PlantillaImportacion"] as string,
                    ImportadoDesdeArchivo = rd["ImportadoDesdeArchivo"] != DBNull.Value && Convert.ToBoolean(rd["ImportadoDesdeArchivo"]),
                    EstatusID = estatusId,
                    EstatusNombre = PlaneacionReleaseEstatus.Nombre(estatusId),
                    FechaCreacion = Convert.ToDateTime(rd["FechaCreacion"]),
                    TotalRenglones = Convert.ToInt32(rd["TotalRenglones"]),
                    TotalEntregas = Convert.ToInt32(rd["TotalEntregas"]),
                    TotalPiezasRequeridas = Convert.ToInt32(rd["TotalPiezasRequeridas"]),
                    TotalPiezasAProducir = Convert.ToInt32(rd["TotalPiezasAProducir"]),
                    UltimaFechaRequerida = rd["UltimaFechaRequerida"] == DBNull.Value
                        ? null
                        : Convert.ToDateTime(rd["UltimaFechaRequerida"])
                });
            }

            return View(lista);
        }

        [HttpGet]
        public async Task<IActionResult> Crear()
        {
            var vm = new PlaneacionReleaseCrearVm
            {
                FolioRelease = await GenerarFolioReleaseSugeridoAsync(),
                FechaRecepcion = DateTime.Today,
                EstatusID = PlaneacionReleaseEstatus.Capturado,
                NivelCriticidad = "NORMAL"
            };

            vm.Renglones.Add(new PlaneacionReleaseRenglonCrearVm
            {
                Renglon = 1,
                Entregas = new List<PlaneacionReleaseEntregaCrearVm>
    {
        new PlaneacionReleaseEntregaCrearVm
        {
            SecuenciaEntrega = 1,
            FechaRequerida = DateTime.Today
        }
    }
            });

            await CargarCatalogosAsync(vm);

            return View(vm);
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Crear(PlaneacionReleaseCrearVm vm)
        {
            var usuarioId = ObtenerUsuarioID();

            vm.NivelCriticidad = "NORMAL";
            vm.ComentarioCriticidad = null;

            

            vm.Renglones = vm.Renglones
                .Where(r =>
                    r.ParteID.HasValue ||
                    !string.IsNullOrWhiteSpace(r.ReferenciaSAP) ||
                    !string.IsNullOrWhiteSpace(r.DesignacionDescripcionSAP))
                .ToList();

            foreach (var r in vm.Renglones)
            {
                r.Entregas = r.Entregas
                    .Where(e =>
                        e.CantidadRequerida > 0 &&
                        e.FechaRequerida.HasValue)
                    .ToList();
            }

            vm.Renglones = vm.Renglones
                .Where(r => r.Entregas.Any())
                .ToList();

            if (!vm.ClienteID.HasValue && string.IsNullOrWhiteSpace(vm.ClienteNombre))
            {
                ModelState.AddModelError("", "Selecciona o captura el cliente.");
            }

            if (!vm.Renglones.Any())
            {
                ModelState.AddModelError("", "Debes capturar al menos un renglón del release.");
            }

            foreach (var r in vm.Renglones)
            {
                if (!r.ParteID.HasValue &&
                    string.IsNullOrWhiteSpace(r.ReferenciaSAP) &&
                    string.IsNullOrWhiteSpace(r.DesignacionDescripcionSAP))
                {
                    ModelState.AddModelError("", $"El renglón {r.Renglon} no tiene parte seleccionada.");
                }

                if (!r.Entregas.Any())
                {
                    ModelState.AddModelError("", $"El renglón {r.Renglon} debe tener al menos una entrega.");
                }

                foreach (var e in r.Entregas)
                {
                    if (!e.FechaRequerida.HasValue)
                        ModelState.AddModelError("", $"El renglón {r.Renglon}, entrega {e.SecuenciaEntrega}, no tiene fecha requerida.");

                    if (e.CantidadRequerida <= 0)
                        ModelState.AddModelError("", $"El renglón {r.Renglon}, entrega {e.SecuenciaEntrega}, debe tener cantidad mayor a cero.");
                }
            }

            if (!ModelState.IsValid)
            {
                await CargarCatalogosAsync(vm);
                return View(vm);
            }

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            await using var tx = await cn.BeginTransactionAsync();

            try
            {
                if (string.IsNullOrWhiteSpace(vm.FolioRelease))
                {
                    vm.FolioRelease = await GenerarFolioReleaseAsync(cn, (SqlTransaction)tx);
                }

                var clienteNombre = vm.ClienteNombre;

                if (vm.ClienteID.HasValue)
                {
                    clienteNombre = await ObtenerClienteNombreAsync(vm.ClienteID.Value, cn, (SqlTransaction)tx);
                }

                var releaseId = await InsertarReleaseAsync(
                    vm,
                    clienteNombre,
                    usuarioId,
                    cn,
                    (SqlTransaction)tx
                );

                var renglonNumero = 1;

                foreach (var renglon in vm.Renglones)
                {
                    renglon.Renglon = renglonNumero;

                    await CompletarRenglonDesdeParteAsync(renglon, cn, (SqlTransaction)tx);

                    var releaseRenglonId = await InsertarReleaseRenglonAsync(
                        releaseId,
                        renglon,
                        usuarioId,
                        cn,
                        (SqlTransaction)tx
                    );

                    var secuenciaEntrega = 1;

                    foreach (var entrega in renglon.Entregas)
                    {
                        entrega.SecuenciaEntrega = secuenciaEntrega;

                        var detalle = CrearDetalleDesdeRenglonEntrega(
                            renglon,
                            entrega
                        );



                        await InsertarReleaseDetalleAsync(
                            releaseId,
                            releaseRenglonId,
                            secuenciaEntrega,
                            detalle,
                            usuarioId,
                            cn,
                            (SqlTransaction)tx
                        );

                        secuenciaEntrega++;
                    }

                    renglonNumero++;
                }



                await tx.CommitAsync();

                TempData["Success"] = "Release guardado correctamente en bandeja.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();

                ModelState.AddModelError("", "Error al guardar el release: " + ex.Message);
                await CargarCatalogosAsync(vm);
                return View(vm);
            }
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LeerArchivoRelease(PlaneacionReleaseCrearVm vm)
        {
            if (vm.ArchivoRelease == null || vm.ArchivoRelease.Length <= 0)
            {
                ModelState.AddModelError("", "Selecciona un archivo de Release para leer.");
                await CargarCatalogosAsync(vm);
                return View("Crear", vm);
            }

            var extension = Path.GetExtension(vm.ArchivoRelease.FileName).ToLowerInvariant();

            if (extension != ".pdf" && extension != ".xlsx" && extension != ".xls" && extension != ".csv")
            {
                ModelState.AddModelError("", "El archivo debe ser PDF, Excel o CSV.");
                await CargarCatalogosAsync(vm);
                return View("Crear", vm);
            }

            try
            {
                vm.ArchivoOrigenNombre = Path.GetFileName(vm.ArchivoRelease.FileName);
                vm.ImportadoDesdeArchivo = true;

                if (string.IsNullOrWhiteSpace(vm.FolioRelease))
                {
                    vm.FolioRelease = await GenerarFolioReleaseSugeridoAsync();
                }

                if (vm.FechaRecepcion == default)
                {
                    vm.FechaRecepcion = DateTime.Today;
                }

                if (extension == ".pdf")
                {
                    vm = await LeerReleasePdfPorPlantillaAsync(vm);
                }
                else
                {
                    vm = await LeerReleaseExcelPorPlantillaAsync(vm);
                }

                await CargarCatalogosAsync(vm);

                TempData["Success"] = "Archivo leído correctamente. Revisa la información antes de guardar.";

                return View("Crear", vm);
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "No fue posible leer el archivo: " + ex.Message);
                await CargarCatalogosAsync(vm);
                return View("Crear", vm);
            }
        }

        // RELEASE_IMPORT_SAFE_PENDING_V1_3
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(268435456)]
        public async Task<IActionResult> ImportarDocumentos(List<IFormFile>? archivos)
        {
            var resultado = new PlaneacionReleaseImportacionResultadoVm();
            var usuarioId = ObtenerUsuarioID();

            if (usuarioId <= 0)
            {
                resultado.ErrorGeneral = "No se pudo identificar el usuario de sesion. Inicia sesion nuevamente antes de importar.";
                return View("Importacion", resultado);
            }

            if (archivos == null || archivos.Count == 0)
            {
                resultado.ErrorGeneral = "Selecciona al menos un documento para importar.";
                return View("Importacion", resultado);
            }

            const int maxFiles = 25;
            const long maxFileBytes = 10L * 1024L * 1024L;

            if (archivos.Count > maxFiles)
                resultado.NotaGeneral = $"Solo se procesaron los primeros {maxFiles} documentos del lote.";

            foreach (var archivo in archivos.Take(maxFiles))
            {
                var item = new PlaneacionReleaseImportacionArchivoVm
                {
                    Archivo = Path.GetFileName(archivo.FileName)
                };
                resultado.Archivos.Add(item);

                try
                {
                    if (archivo.Length <= 0)
                        throw new InvalidOperationException("El archivo esta vacio.");

                    if (archivo.Length > maxFileBytes)
                        throw new InvalidOperationException("El archivo supera el limite de 10 MB para un Release.");

                    using var memory = new MemoryStream();
                    await archivo.CopyToAsync(memory);
                    var bytes = memory.ToArray();

                    item.ArchivoGuardado = await GuardarDocumentoOriginalReleaseAsync(
                        bytes,
                        archivo.FileName);

                    var extension = Path.GetExtension(archivo.FileName).ToLowerInvariant();

                    if (extension == ".pdf")
                    {
                        var pdfTemplate = ReleasePdfDocumentDetector.Detect(bytes);

                        switch (pdfTemplate)
                        {
                            case ReleasePdfTemplate.HufSupplierSchedule:
                                await ImportarDocumentoHufSeguroAsync(bytes, item, usuarioId);
                                break;

                            case ReleasePdfTemplate.VeritasSchedule:
                                await ImportarDocumentoVeritasSeguroAsync(bytes, item, usuarioId);
                                break;

                            default:
                                item.Estado = "NO_SOPORTADO";
                                item.Mensaje = "El PDF original fue conservado, pero no se reconocio su estructura. Actualmente se detectan HUF Supplier Schedule Report y VERITAS Schedule.";
                                break;
                        }

                        continue;
                    }

                    if (extension == ".xlsx" || extension == ".xls" || extension == ".xlsm")
                    {
                        var excelTemplate = ReleaseExcelDocumentDetector.Detect(bytes);

                        switch (excelTemplate)
                        {
                            case ReleaseExcelTemplate.GoldeMexicoWeeklyMatrix:
                                await ImportarDocumentoGoldeMexicoSeguroAsync(bytes, item, usuarioId);
                                break;

                            case ReleaseExcelTemplate.GoldeWeeklyMatrix:
                                await ImportarDocumentoGoldeSeguroAsync(bytes, item, usuarioId);
                                break;

                            case ReleaseExcelTemplate.NormaWeeklyMatrix:
                                await ImportarDocumentoNormaSeguroAsync(bytes, item, usuarioId);
                                break;

                            case ReleaseExcelTemplate.AirThermalMaterialRelease:
                                await ImportarDocumentoAirThermalSeguroAsync(bytes, item, usuarioId);
                                break;

                            default:
                                item.Estado = "NO_SOPORTADO";
                                item.Mensaje = "El Excel original fue conservado, pero no se reconocio su estructura. Actualmente se detectan las matrices GOLDE, NORMA y AIR THERMAL.";
                                break;
                        }

                        continue;
                    }

                    item.Estado = "NO_SOPORTADO";
                    item.Mensaje = "El archivo original fue conservado. Actualmente se admiten PDF HUF/VERITAS y Excel GOLDE/NORMA/AIR THERMAL; CSV y otras plantillas quedan pendientes.";
                }
                catch (Exception ex)
                {
                    item.Estado = "ERROR";
                    item.Mensaje = ex.Message;
                }
            }

            return View("Importacion", resultado);
        }

        private async Task ImportarDocumentoHufSeguroAsync(
            byte[] bytes,
            PlaneacionReleaseImportacionArchivoVm item,
            int usuarioId)
        {
            var document = HufReleasePdfParser.Parse(bytes, DateTime.Today);

            item.Cliente = document.ClienteNombre;
            item.Parte = document.PartNumber;
            item.Descripcion = document.PartDescription;
            item.Schedule = document.ScheduleNumber;
            item.OrdenCliente = document.OrderNumber;
            item.Version = document.VersionText;
            item.TotalEntregas = document.Deliveries.Count;
            item.TotalPiezas = document.Deliveries.Sum(x => x.RequiredQuantity);
            item.Advertencias.AddRange(document.Warnings);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();

            try
            {
                var clienteId = await ObtenerClienteHufParaImportacionAsync(cn, tx);
                if (!clienteId.HasValue)
                    throw new InvalidOperationException("No existe un cliente activo que contenga HUF en ERP_Clientes. El PDF original quedo conservado.");

                item.ClienteID = clienteId.Value;

                var clienteNombre = await ObtenerClienteNombreAsync(clienteId.Value, cn, tx)
                    ?? document.ClienteNombre;

                var match = await BuscarParteImportacionIncluyendoInactivasAsync(
                    document.PartNumber!,
                    clienteId.Value,
                    cn,
                    tx);

                var parteIdActiva = match?.Activa == true ? match.ParteID : (int?)null;
                var tienePendientes = !parteIdActiva.HasValue;

                if (match == null)
                {
                    item.Advertencias.Add($"La parte HUF '{document.PartNumber}' no existe para el cliente. El Release se guardo pendiente de vinculacion.");
                }
                else if (!match.Activa)
                {
                    item.Advertencias.Add($"La parte HUF '{document.PartNumber}' existe como '{match.NumeroParte}', pero esta INACTIVA. El Release se guardo pendiente de vinculacion.");
                }

                if (await ExisteDocumentoHufImportadoAsync(document.Sha256, cn, tx))
                {
                    await tx.RollbackAsync();
                    item.Estado = "OMITIDO";
                    item.Mensaje = "El mismo documento HUF ya fue importado anteriormente. El original tambien quedo conservado y no se creo un duplicado.";
                    return;
                }

                if (!tienePendientes)
                {
                    var fechaVersionActiva = await ObtenerFechaVersionHufActivaAsync(
                        clienteId.Value,
                        parteIdActiva!.Value,
                        cn,
                        tx);

                    if (document.DocumentDate.HasValue &&
                        fechaVersionActiva.HasValue &&
                        document.DocumentDate.Value.Date < fechaVersionActiva.Value.Date)
                    {
                        await tx.RollbackAsync();
                        item.Estado = "OMITIDO";
                        item.Mensaje = $"El documento es anterior a la version HUF activa ({fechaVersionActiva.Value:dd/MM/yyyy}). No se reemplazo la planeacion vigente; el PDF quedo conservado.";
                        return;
                    }

                    item.VersionesAnterioresCerradas = await CerrarVersionesHufAnterioresAsync(
                        clienteId.Value,
                        parteIdActiva.Value,
                        usuarioId,
                        cn,
                        tx);
                }

                var categorias = string.Join(", ", document.Deliveries
                    .Select(x => x.Category)
                    .Distinct(StringComparer.OrdinalIgnoreCase));

                var vm = new PlaneacionReleaseCrearVm
                {
                    FolioRelease = await GenerarFolioReleaseAsync(cn, tx),
                    FolioCliente = document.ScheduleNumber,
                    ClienteID = clienteId.Value,
                    ClienteNombre = clienteNombre,
                    FechaRecepcion = DateTime.Today,
                    VersionRelease = document.VersionText,
                    ArchivoOrigenNombre = item.Archivo,
                    PlantillaImportacion = "HUF_SUPPLIER_SCHEDULE",
                    ImportadoDesdeArchivo = true,
                    EstatusID = PlaneacionReleaseEstatus.Capturado,
                    Observaciones = ConstruirObservacionImportacion(
                        "HUF",
                        document.Sha256,
                        item.ArchivoGuardado,
                        $"Orden:{document.OrderNumber};Parte:{document.PartNumber};Categorias:{categorias}")
                };

                vm.Renglones.Add(new PlaneacionReleaseRenglonCrearVm
                {
                    Renglon = 1,
                    ParteID = parteIdActiva,
                    NumeroParte = document.PartNumber,
                    ReferenciaSAP = document.PartNumber,
                    DesignacionDescripcionSAP = document.PartDescription,
                    UnidadMedidaCliente = document.Uom,
                    ContratoCliente = document.OrderNumber,
                    Observaciones = tienePendientes
                        ? "Importado desde HUF. Pendiente de vincular una parte activa."
                        : $"Importado automaticamente desde HUF. Categorias detectadas: {categorias}.",
                    Entregas = document.Deliveries.Select(x => new PlaneacionReleaseEntregaCrearVm
                    {
                        SecuenciaEntrega = x.Sequence,
                        FechaCarga = x.LoadingDate,
                        FechaRequerida = x.RequiredDate,
                        CantidadRequerida = x.RequiredQuantity
                    }).ToList()
                });

                var releaseId = await GuardarReleaseImportadoFlexibleAsync(
                    vm,
                    clienteNombre,
                    usuarioId,
                    cn,
                    tx,
                    tienePendientes);

                await tx.CommitAsync();

                item.ReleaseID = releaseId;
                item.FolioRelease = vm.FolioRelease;
                item.Cliente = clienteNombre;
                item.RequiereVinculacion = tienePendientes;
                item.Estado = tienePendientes ? "PENDIENTE" : "CREADO";
                item.Mensaje = tienePendientes
                    ? "Release HUF conservado en estado Capturado. Falta vincular una parte activa antes de incorporarlo a Planeacion."
                    : "Release HUF creado, vinculado al cliente y a la parte, y calculado correctamente.";
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        private async Task ImportarDocumentoVeritasSeguroAsync(
            byte[] bytes,
            PlaneacionReleaseImportacionArchivoVm item,
            int usuarioId)
        {
            var document = VeritasReleasePdfParser.Parse(bytes);
            var groupedDeliveries = document.Deliveries
                .GroupBy(x => x.PartNumber, StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x.Key)
                .ToList();

            item.Cliente = document.ClienteNombre;
            item.Parte = string.Join(", ", groupedDeliveries.Select(x => x.Key));
            item.Descripcion = groupedDeliveries.Count == 1
                ? groupedDeliveries[0].First().PartDescription
                : $"{groupedDeliveries.Count} partes VERITAS";
            item.Schedule = document.ScheduleNumber;
            item.OrdenCliente = document.ContractNumber;
            item.Version = document.VersionText;
            item.TotalEntregas = document.Deliveries.Count;
            item.TotalPiezas = document.Deliveries.Sum(x => x.RequiredQuantity);
            item.Advertencias.AddRange(document.Warnings);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();

            try
            {
                var clienteId = await ObtenerClienteVeritasParaImportacionAsync(cn, tx);
                if (!clienteId.HasValue)
                    throw new InvalidOperationException("No existe un cliente activo que contenga VERITAS en ERP_Clientes. El PDF original quedo conservado.");

                item.ClienteID = clienteId.Value;

                var clienteNombre = await ObtenerClienteNombreAsync(clienteId.Value, cn, tx)
                    ?? document.ClienteNombre;

                var partMap = new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase);
                var tienePendientes = false;

                foreach (var group in groupedDeliveries)
                {
                    var match = await BuscarParteImportacionIncluyendoInactivasAsync(
                        group.Key,
                        clienteId.Value,
                        cn,
                        tx);

                    if (match == null)
                    {
                        partMap[group.Key] = null;
                        tienePendientes = true;
                        item.Advertencias.Add($"La parte VERITAS '{group.Key}' no existe para el cliente. Se conservo como pendiente de vinculacion.");
                    }
                    else if (!match.Activa)
                    {
                        partMap[group.Key] = null;
                        tienePendientes = true;
                        item.Advertencias.Add($"La parte VERITAS '{group.Key}' existe como '{match.NumeroParte}', pero esta INACTIVA. Se conservo como pendiente de vinculacion.");
                    }
                    else
                    {
                        partMap[group.Key] = match.ParteID;
                    }
                }

                if (await ExisteDocumentoImportadoAsync(
                    "VERITAS_SCHEDULE",
                    document.Sha256,
                    cn,
                    tx))
                {
                    await tx.RollbackAsync();
                    item.Estado = "OMITIDO";
                    item.Mensaje = "El mismo documento VERITAS ya fue importado anteriormente. El original quedo conservado y no se creo un duplicado.";
                    return;
                }

                if (!tienePendientes)
                {
                    var fechaVersionActiva = await ObtenerFechaVersionVeritasActivaAsync(
                        clienteId.Value,
                        document.ContractNumber!,
                        cn,
                        tx);

                    if (document.DocumentDate.HasValue &&
                        fechaVersionActiva.HasValue &&
                        document.DocumentDate.Value.Date < fechaVersionActiva.Value.Date)
                    {
                        await tx.RollbackAsync();
                        item.Estado = "OMITIDO";
                        item.Mensaje = $"El documento es anterior a la version VERITAS activa ({fechaVersionActiva.Value:dd/MM/yyyy}) para el contrato {document.ContractNumber}. El PDF quedo conservado.";
                        return;
                    }

                    item.VersionesAnterioresCerradas = await CerrarVersionesVeritasAnterioresAsync(
                        clienteId.Value,
                        document.ContractNumber!,
                        usuarioId,
                        cn,
                        tx);
                }

                var vm = new PlaneacionReleaseCrearVm
                {
                    FolioRelease = await GenerarFolioReleaseAsync(cn, tx),
                    FolioCliente = document.ContractNumber,
                    ClienteID = clienteId.Value,
                    ClienteNombre = clienteNombre,
                    FechaRecepcion = DateTime.Today,
                    VersionRelease = document.VersionText,
                    ArchivoOrigenNombre = item.Archivo,
                    PlantillaImportacion = "VERITAS_SCHEDULE",
                    ImportadoDesdeArchivo = true,
                    EstatusID = PlaneacionReleaseEstatus.Capturado,
                    Observaciones = ConstruirObservacionImportacion(
                        "VERITAS",
                        document.Sha256,
                        item.ArchivoGuardado,
                        $"Schedule:{document.ScheduleNumber};Contrato:{document.ContractNumber};Supplier:{document.SupplierNumber}")
                };

                var rowNumber = 1;
                foreach (var group in groupedDeliveries)
                {
                    var first = group.First();
                    var parteId = partMap[group.Key];
                    vm.Renglones.Add(new PlaneacionReleaseRenglonCrearVm
                    {
                        Renglon = rowNumber,
                        ParteID = parteId,
                        NumeroParte = group.Key,
                        ReferenciaSAP = group.Key,
                        DesignacionDescripcionSAP = first.PartDescription,
                        UnidadMedidaCliente = first.Uom,
                        ContratoCliente = document.ContractNumber,
                        Observaciones = parteId.HasValue
                            ? $"Importado automaticamente desde VERITAS. Schedule {document.ScheduleNumber}; contrato {document.ContractNumber}."
                            : $"Importado desde VERITAS. Referencia original {group.Key}; pendiente de vincular parte activa.",
                        Entregas = group
                            .OrderBy(x => x.RequiredDate)
                            .ThenBy(x => x.ItemNumber)
                            .Select((x, index) => new PlaneacionReleaseEntregaCrearVm
                            {
                                SecuenciaEntrega = index + 1,
                                FechaCarga = null,
                                FechaRequerida = x.RequiredDate,
                                CantidadRequerida = x.RequiredQuantity
                            })
                            .ToList()
                    });
                    rowNumber++;
                }

                var releaseId = await GuardarReleaseImportadoFlexibleAsync(
                    vm,
                    clienteNombre,
                    usuarioId,
                    cn,
                    tx,
                    tienePendientes);

                await tx.CommitAsync();

                item.ReleaseID = releaseId;
                item.FolioRelease = vm.FolioRelease;
                item.Cliente = clienteNombre;
                item.RequiereVinculacion = tienePendientes;
                item.Estado = tienePendientes ? "PENDIENTE" : "CREADO";
                item.Mensaje = tienePendientes
                    ? "Release VERITAS conservado en estado Capturado. Falta vincular una o mas partes activas antes de incorporarlo a Planeacion."
                    : "Release VERITAS creado, vinculado al cliente y a sus partes, y calculado correctamente.";
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        // RELEASE_EXCEL_GOLDE_NORMA_V1_4
        // RELEASE_EXCEL_AIR_THERMAL_V1_8
        private async Task ImportarDocumentoAirThermalSeguroAsync(
            byte[] bytes,
            PlaneacionReleaseImportacionArchivoVm item,
            int usuarioId)
        {
            var document = ReleaseExcelDocumentDetector.ParseAirThermal(
                bytes,
                item.Archivo);

            await ImportarDocumentoExcelMatrizSeguroAsync(
                document,
                item,
                usuarioId);
        }
        private async Task ImportarDocumentoGoldeMexicoSeguroAsync(
            byte[] bytes,
            PlaneacionReleaseImportacionArchivoVm item,
            int usuarioId)
        {
            var document = ReleaseExcelDocumentDetector.ParseGoldeMexico(bytes, item.Archivo);
            await ImportarDocumentoExcelMatrizSeguroAsync(document, item, usuarioId);
        }
        private async Task ImportarDocumentoGoldeSeguroAsync(
            byte[] bytes,
            PlaneacionReleaseImportacionArchivoVm item,
            int usuarioId)
        {
            var document = ReleaseExcelDocumentDetector.ParseGolde(bytes, item.Archivo);
            await ImportarDocumentoExcelMatrizSeguroAsync(document, item, usuarioId);
        }

        private async Task ImportarDocumentoNormaSeguroAsync(
            byte[] bytes,
            PlaneacionReleaseImportacionArchivoVm item,
            int usuarioId)
        {
            var document = ReleaseExcelDocumentDetector.ParseNorma(bytes);
            await ImportarDocumentoExcelMatrizSeguroAsync(document, item, usuarioId);
        }

        private async Task ImportarDocumentoExcelMatrizSeguroAsync(
            ReleaseExcelDocument document,
            PlaneacionReleaseImportacionArchivoVm item,
            int usuarioId)
        {
            item.Cliente = document.ClienteNombre;
            item.Parte = document.Rows.Count == 1
                ? document.Rows[0].PartNumber
                : $"{document.Rows.Count} partes";
            item.Descripcion = document.TemplateCode switch
            {
                "GOLDE_MEXICO_WEEKLY_RELEASE" => "Matriz semanal GOLDE MEXICO",
                "GOLDEN_WEEKLY_RELEASE" => "Matriz semanal GOLDE USA",
                "NORMA_WEEKLY_RELEASE" => "Matriz semanal NORMA",
                "AIR_THERMAL_MATERIAL_RELEASE" => "Material Release AIR THERMAL",
                _ => "Release Excel"
            };
            item.Schedule = document.VersionText;
            item.OrdenCliente = document.FolioCliente;
            item.Version = document.VersionText;
            item.TotalEntregas = document.Rows.Sum(x => x.Deliveries.Count);
            item.TotalPiezas = document.Rows.Sum(x => x.Deliveries.Sum(d => d.RequiredQuantity));
            item.Advertencias.AddRange(document.Warnings);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();

            try
            {
                var clienteId = await ObtenerClienteExcelParaImportacionAsync(
                    document.TemplateCode,
                    cn,
                    tx);

                if (!clienteId.HasValue)
                {
                    throw new InvalidOperationException(
                        $"No existe un cliente activo compatible con {document.ClienteNombre} en ERP_Clientes. El Excel original quedo conservado.");
                }

                item.ClienteID = clienteId.Value;

                var clienteNombre = await ObtenerClienteNombreAsync(clienteId.Value, cn, tx)
                    ?? document.ClienteNombre;

                var partMap = new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase);
                var tienePendientes = false;

                foreach (var row in document.Rows)
                {
                    var match = document.TemplateCode == "AIR_THERMAL_MATERIAL_RELEASE"
                        ? await BuscarParteAirThermalIncluyendoRevisionesAsync(
                            row.PartNumber,
                            clienteId.Value,
                            cn,
                            tx)
                        : EsPlantillaGolde(document.TemplateCode)
                            ? await BuscarParteGoldeIncluyendoRevisionesAsync(
                                row.PartNumber,
                                clienteId.Value,
                                cn,
                                tx)
                            : await BuscarParteImportacionIncluyendoInactivasAsync(
                                row.PartNumber,
                                clienteId.Value,
                                cn,
                                tx);

                    if (match == null)
                    {
                        partMap[row.PartNumber] = null;
                        tienePendientes = true;
                        item.Advertencias.Add(
                            $"La parte {row.PartNumber} no existe para el cliente. Se conservo como pendiente de vinculacion.");
                    }
                    else if (!match.Activa)
                    {
                        partMap[row.PartNumber] = null;
                        tienePendientes = true;
                        item.Advertencias.Add(
                            $"La parte {row.PartNumber} existe como {match.NumeroParte}, pero esta INACTIVA. Se conservo como pendiente de vinculacion.");
                    }
                    else
                    {
                        partMap[row.PartNumber] = match.ParteID;
                    }
                }

                if (await ExisteDocumentoImportadoAsync(
                    document.TemplateCode,
                    document.Sha256,
                    cn,
                    tx))
                {
                    await tx.RollbackAsync();
                    item.Estado = "OMITIDO";
                    item.Mensaje = "El mismo Excel ya fue importado anteriormente. El original tambien quedo conservado y no se creo un duplicado.";
                    return;
                }

                if (!tienePendientes)
                {
                    item.VersionesAnterioresCerradas = await CerrarVersionesExcelAnterioresAsync(
                        clienteId.Value,
                        document.TemplateCode,
                        usuarioId,
                        cn,
                        tx);
                }

                var vm = new PlaneacionReleaseCrearVm
                {
                    FolioRelease = await GenerarFolioReleaseAsync(cn, tx),
                    FolioCliente = document.FolioCliente,
                    ClienteID = clienteId.Value,
                    ClienteNombre = clienteNombre,
                    FechaRecepcion = DateTime.Today,
                    VersionRelease = document.VersionText,
                    ArchivoOrigenNombre = item.Archivo,
                    PlantillaImportacion = document.TemplateCode,
                    ImportadoDesdeArchivo = true,
                    EstatusID = PlaneacionReleaseEstatus.Capturado,
                    Observaciones = ConstruirObservacionImportacion(
                        document.TemplateCode,
                        document.Sha256,
                        item.ArchivoGuardado,
                        $"Version:{document.VersionText};Renglones:{document.Rows.Count};Entregas:{item.TotalEntregas}")
                };

                var rowNumber = 1;
                foreach (var sourceRow in document.Rows)
                {
                    var parteId = partMap[sourceRow.PartNumber];
                    vm.Renglones.Add(new PlaneacionReleaseRenglonCrearVm
                    {
                        Renglon = rowNumber,
                        ParteID = parteId,
                        NumeroParte = sourceRow.PartNumber,
                        ReferenciaSAP = sourceRow.PartNumber,
                        DesignacionDescripcionSAP = sourceRow.PartDescription,
                        UnidadMedidaCliente = sourceRow.Uom,
                        ContratoCliente = sourceRow.SourceReference,
                        Observaciones = parteId.HasValue
                            ? $"Importado automaticamente desde {NombrePlantillaExcel(document.TemplateCode)}."
                            : $"Referencia original {sourceRow.PartNumber}; pendiente de vincular parte activa.",
                        Entregas = sourceRow.Deliveries
                            .OrderBy(x => x.RequiredDate)
                            .ThenBy(x => x.Sequence)
                            .Select((x, index) => new PlaneacionReleaseEntregaCrearVm
                            {
                                SecuenciaEntrega = index + 1,
                                FechaCarga = null,
                                FechaRequerida = x.RequiredDate,
                                CantidadRequerida = x.RequiredQuantity
                            })
                            .ToList()
                    });
                    rowNumber++;
                }

                var releaseId = await GuardarReleaseImportadoFlexibleAsync(
                    vm,
                    clienteNombre,
                    usuarioId,
                    cn,
                    tx,
                    tienePendientes);

                await tx.CommitAsync();

                item.ReleaseID = releaseId;
                item.FolioRelease = vm.FolioRelease;
                item.Cliente = clienteNombre;
                item.RequiereVinculacion = tienePendientes;
                item.Estado = tienePendientes ? "PENDIENTE" : "CREADO";
                item.Mensaje = tienePendientes
                    ? $"Release {NombrePlantillaExcel(document.TemplateCode)} conservado en estado Capturado. Vincula las partes faltantes antes de incorporarlo a Planeacion."
                    : $"Release {NombrePlantillaExcel(document.TemplateCode)} creado, vinculado al cliente y calculado correctamente.";
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        private static string NombrePlantillaExcel(string templateCode)
        {
            return templateCode switch
            {
                "GOLDE_MEXICO_WEEKLY_RELEASE" => "GOLDE MEXICO",
                "GOLDEN_WEEKLY_RELEASE" => "GOLDE USA",
                "NORMA_WEEKLY_RELEASE" => "NORMA",
                "AIR_THERMAL_MATERIAL_RELEASE" => "AIR THERMAL",
                _ => templateCode
            };
        }
        private static async Task<int?> ObtenerClienteExcelParaImportacionAsync(
            string templateCode,
            SqlConnection cn,
            SqlTransaction tx)
        {
            string sql;

            if (templateCode == "GOLDE_MEXICO_WEEKLY_RELEASE")
            {
                sql = @"
SELECT TOP (1) ClienteID
FROM dbo.ERP_Clientes
WHERE Activo = 1
  AND (Codigo = N'GOLDE_MEXICO' OR UPPER(LTRIM(RTRIM(Nombre))) = N'GOLDE MEXICO')
ORDER BY ClienteID;";
            }
            else if (templateCode == "GOLDEN_WEEKLY_RELEASE")
            {
                sql = @"
SELECT TOP (1) ClienteID
FROM dbo.ERP_Clientes
WHERE Activo = 1
  AND
  (
        UPPER(ISNULL(Nombre, '')) LIKE '%GOLDE%'
     OR UPPER(ISNULL(Nombre, '')) LIKE '%AUBURN%'
  )
ORDER BY
    CASE WHEN UPPER(ISNULL(Nombre, '')) LIKE '%AUBURN%' THEN 0 ELSE 1 END,
    ClienteID;";
            }
            else if (templateCode == "NORMA_WEEKLY_RELEASE")
            {
                sql = @"
SELECT TOP (1) ClienteID
FROM dbo.ERP_Clientes
WHERE Activo = 1
  AND UPPER(ISNULL(Nombre, '')) LIKE '%NORMA%'
ORDER BY ClienteID;";
            }
            else if (templateCode == "AIR_THERMAL_MATERIAL_RELEASE")
            {
                sql = @"
SELECT TOP (1) ClienteID
FROM dbo.ERP_Clientes
WHERE Activo = 1
  AND UPPER(ISNULL(Nombre, '')) LIKE '%AIR%THERMAL%'
ORDER BY
    CASE WHEN UPPER(LTRIM(RTRIM(ISNULL(Nombre, '')))) = 'AIR THERMAL SYSTEMS' THEN 0 ELSE 1 END,
    ClienteID;";
            }
            else
            {
                return null;
            }

            await using var cmd = new SqlCommand(sql, cn, tx);
            var result = await cmd.ExecuteScalarAsync();
            return result == null || result == DBNull.Value
                ? null
                : Convert.ToInt32(result);
        }

        private static async Task<int> CerrarVersionesExcelAnterioresAsync(
            int clienteId,
            string templateCode,
            int usuarioId,
            SqlConnection cn,
            SqlTransaction tx,
            int? releaseIdExcluir = null)
        {
            const string sql = @"
UPDATE dbo.Planeacion_Releases
SET
    EstatusID = @Cerrado,
    UsuarioModificacionID = @UsuarioID,
    FechaModificacion = GETDATE()
WHERE ClienteID = @ClienteID
  AND PlantillaImportacion = @Plantilla
  AND Activo = 1
  AND EstatusID NOT IN (@Cerrado, @Cancelado)
  AND (@ReleaseIDExcluir IS NULL OR ReleaseID <> @ReleaseIDExcluir);

SELECT @@ROWCOUNT;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@Cerrado", SqlDbType.Int).Value = PlaneacionReleaseEstatus.Cerrado;
            cmd.Parameters.Add("@Cancelado", SqlDbType.Int).Value = PlaneacionReleaseEstatus.Cancelado;
            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
            cmd.Parameters.Add("@ClienteID", SqlDbType.Int).Value = clienteId;
            cmd.Parameters.Add("@Plantilla", SqlDbType.NVarChar, 100).Value = templateCode;
            cmd.Parameters.Add("@ReleaseIDExcluir", SqlDbType.Int).Value =
                (object?)releaseIdExcluir ?? DBNull.Value;

            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        private static async Task CerrarVersionExcelAlCompletarVinculacionAsync(
            int releaseId,
            int usuarioId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
SELECT ClienteID, PlantillaImportacion
FROM dbo.Planeacion_Releases
WHERE ReleaseID = @ReleaseID
  AND Activo = 1;";

            int? clienteId = null;
            string? templateCode = null;

            await using (var cmd = new SqlCommand(sql, cn, tx))
            {
                cmd.Parameters.Add("@ReleaseID", SqlDbType.Int).Value = releaseId;
                await using var rd = await cmd.ExecuteReaderAsync();
                if (await rd.ReadAsync())
                {
                    clienteId = rd["ClienteID"] == DBNull.Value
                        ? null
                        : Convert.ToInt32(rd["ClienteID"]);
                    templateCode = rd["PlantillaImportacion"] as string;
                }
            }

            if (!clienteId.HasValue ||
                (templateCode != "GOLDE_MEXICO_WEEKLY_RELEASE" &&
                 templateCode != "GOLDEN_WEEKLY_RELEASE" &&
                 templateCode != "NORMA_WEEKLY_RELEASE" &&
                 templateCode != "AIR_THERMAL_MATERIAL_RELEASE"))
            {
                return;
            }

            await CerrarVersionesExcelAnterioresAsync(
                clienteId.Value,
                templateCode,
                usuarioId,
                cn,
                tx,
                releaseId);
        }
        private async Task<int> GuardarReleaseImportadoFlexibleAsync(
            PlaneacionReleaseCrearVm vm,
            string clienteNombre,
            int usuarioId,
            SqlConnection cn,
            SqlTransaction tx,
            bool tienePendientes)
        {
            var releaseId = await InsertarReleaseAsync(
                vm,
                clienteNombre,
                usuarioId,
                cn,
                tx);

            var renglonNumero = 1;
            foreach (var renglon in vm.Renglones)
            {
                renglon.Renglon = renglonNumero;
                await CompletarRenglonDesdeParteAsync(renglon, cn, tx);

                var releaseRenglonId = await InsertarReleaseRenglonAsync(
                    releaseId,
                    renglon,
                    usuarioId,
                    cn,
                    tx);

                var secuencia = 1;
                foreach (var entrega in renglon.Entregas)
                {
                    // Solo para Releases importados:
                    // conservar una fecha real si el documento la trae;
                    // si falta, usar un día calendario antes de la requerida.
                    if (!entrega.FechaCarga.HasValue &&
                        entrega.FechaRequerida.HasValue)
                    {
                        entrega.FechaCarga =
                            entrega.FechaRequerida.Value.Date.AddDays(-1);
                    }
                    entrega.SecuenciaEntrega = secuencia;
                    var detalle = CrearDetalleDesdeRenglonEntrega(renglon, entrega);

                    if (detalle.ParteID.HasValue)
                    {
                        await CompletarDetalleDesdeParteAsync(detalle, cn, tx);
                        await CalcularNecesidadAsync(detalle, cn, tx);
                    }
                    else
                    {
                        PrepararDetallePendienteVinculacion(detalle);
                    }

                    await InsertarReleaseDetalleAsync(
                        releaseId,
                        releaseRenglonId,
                        secuencia,
                        detalle,
                        usuarioId,
                        cn,
                        tx);
                    secuencia++;
                }

                renglonNumero++;
            }

            await ActualizarEstatusReleaseAsync(
                releaseId,
                tienePendientes
                    ? PlaneacionReleaseEstatus.Capturado
                    : PlaneacionReleaseEstatus.Calculado,
                usuarioId,
                cn,
                tx);

            return releaseId;
        }

        private static void PrepararDetallePendienteVinculacion(
            PlaneacionReleaseDetalleCrearVm detalle)
        {
            detalle.PTDisponibleAlCalcular = null;
            detalle.ProduccionProgramadaPendiente = null;
            detalle.PiezasDesdePT = null;
            detalle.PiezasAProducir = null;
            detalle.MPRequeridaKg = null;
            detalle.MPDisponibleKg = null;
            detalle.EmbalajeRequerido = null;
            detalle.EmbalajeDisponible = null;
            detalle.HorasNecesarias = null;
            detalle.FechaInicioSugerida = null;
            detalle.FechaFinEstimada = null;
            detalle.DaTiempo = null;
            detalle.MensajeCapacidad = "Pendiente de vincular una parte activa. No se calculo PT, MP, embalaje ni capacidad.";
            detalle.EstatusID = PlaneacionReleaseEstatus.Capturado;
        }

        private async Task<string> GuardarDocumentoOriginalReleaseAsync(
            byte[] bytes,
            string fileName)
        {
            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            var baseName = Path.GetFileNameWithoutExtension(fileName);
            var safeName = new string(baseName
                .Select(ch => char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' ? ch : '_')
                .Take(80)
                .ToArray());

            if (string.IsNullOrWhiteSpace(safeName))
                safeName = "release";

            var relative = Path.Combine(
                DateTime.Now.ToString("yyyy"),
                DateTime.Now.ToString("MM"),
                $"{DateTime.Now:yyyyMMdd_HHmmssfff}_{Guid.NewGuid():N}_{safeName}{extension}");

            var root = Path.Combine(
                _environment.ContentRootPath,
                "App_Data",
                "Releases",
                "Originales");

            var rootFull = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            var fullPath = Path.GetFullPath(Path.Combine(root, relative));
            if (!fullPath.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("No fue posible determinar una ruta segura para conservar el documento.");

            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            await System.IO.File.WriteAllBytesAsync(fullPath, bytes);
            return relative.Replace('\\', '/');
        }

        private static string ConstruirObservacionImportacion(
            string plantilla,
            string sha256,
            string? archivoGuardado,
            string datos)
        {
            var value = $"Importacion {plantilla};ARCHIVO_GUARDADO:{archivoGuardado};SHA256:{sha256};{datos}";
            return value.Length <= 500 ? value : value.Substring(0, 500);
        }

        private sealed class ParteImportacionMatch
        {
            public int ParteID { get; init; }
            public bool Activa { get; init; }
            public string? NumeroParte { get; init; }
            public string? ReferenciaSAP { get; init; }
        }
        // RELEASE_PART_ALIAS_MASTER_V1_1
        private static async Task<ParteImportacionMatch?> BuscarParteActivaPorAliasMaestroAsync(
            string referencia,
            int clienteId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
IF OBJECT_ID(N'dbo.vw_ERP_ParteIdentificadores', N'V') IS NULL
BEGIN
    SELECT TOP (0)
        p.ParteID,
        p.Activo,
        p.NumeroParte,
        p.ReferenciaSAP
    FROM dbo.ERP_Partes p;
    RETURN;
END;

DECLARE @Normalizada NVARCHAR(150) = UPPER(
    REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
        LTRIM(RTRIM(@Referencia)),
        CHAR(13), ''), CHAR(10), ''), ' ', ''), '.', ''), '-', ''), '_', ''), '/', ''), '?', '')
);

;WITH Candidatos AS
(
    SELECT DISTINCT i.ParteID
    FROM dbo.vw_ERP_ParteIdentificadores i
    WHERE i.ClienteID = @ClienteID
      AND i.IdentificadorNormalizado = @Normalizada
)
SELECT
    p.ParteID,
    p.Activo,
    p.NumeroParte,
    p.ReferenciaSAP
FROM Candidatos c
INNER JOIN dbo.ERP_Partes p
    ON p.ParteID = c.ParteID
WHERE p.Activo = 1
  AND (SELECT COUNT(1) FROM Candidatos) = 1;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@Referencia", SqlDbType.NVarChar, 150).Value = referencia.Trim();
            cmd.Parameters.Add("@ClienteID", SqlDbType.Int).Value = clienteId;

            await using var rd = await cmd.ExecuteReaderAsync();
            if (!await rd.ReadAsync())
                return null;

            return new ParteImportacionMatch
            {
                ParteID = Convert.ToInt32(rd["ParteID"]),
                Activa = Convert.ToBoolean(rd["Activo"]),
                NumeroParte = rd["NumeroParte"] as string,
                ReferenciaSAP = rd["ReferenciaSAP"] as string
            };
        }

        private static async Task<ParteImportacionMatch?> BuscarParteAirThermalIncluyendoRevisionesAsync(
            string referencia,
            int clienteId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            var referenciaBase = Regex.Replace(
                referencia.Trim(),
                @"[._-]\d{3}$",
                string.Empty,
                RegexOptions.CultureInvariant);

            const string sql = @"
DECLARE @Normalizada NVARCHAR(150) = UPPER(
    REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(@Referencia)), '-', ''), '.', ''), ' ', ''), '/', ''), '_', '')
);

DECLARE @BaseNormalizada NVARCHAR(150) = UPPER(
    REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(@ReferenciaBase)), '-', ''), '.', ''), ' ', ''), '/', ''), '_', '')
);

SELECT TOP (1)
    ParteID,
    ISNULL(Activo, 0) AS Activo,
    NumeroParte,
    ReferenciaSAP
FROM dbo.ERP_Partes
WHERE ClienteID = @ClienteID
  AND
  (
        UPPER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(ISNULL(NumeroParte, ''), '-', ''), '.', ''), ' ', ''), '/', ''), '_', '')) = @Normalizada
     OR UPPER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(ISNULL(ReferenciaSAP, ''), '-', ''), '.', ''), ' ', ''), '/', ''), '_', '')) = @Normalizada
     OR (
            @BaseNormalizada <> @Normalizada
        AND
        (
              UPPER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(ISNULL(NumeroParte, ''), '-', ''), '.', ''), ' ', ''), '/', ''), '_', '')) = @BaseNormalizada
           OR UPPER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(ISNULL(ReferenciaSAP, ''), '-', ''), '.', ''), ' ', ''), '/', ''), '_', '')) = @BaseNormalizada
        )
     )
  )
ORDER BY
    CASE WHEN ISNULL(Activo, 0) = 1 THEN 0 ELSE 1 END,
    CASE
        WHEN UPPER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(ISNULL(ReferenciaSAP, ''), '-', ''), '.', ''), ' ', ''), '/', ''), '_', '')) = @Normalizada THEN 0
        WHEN UPPER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(ISNULL(NumeroParte, ''), '-', ''), '.', ''), ' ', ''), '/', ''), '_', '')) = @Normalizada THEN 1
        WHEN UPPER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(ISNULL(ReferenciaSAP, ''), '-', ''), '.', ''), ' ', ''), '/', ''), '_', '')) = @BaseNormalizada THEN 2
        ELSE 3
    END,
    ParteID;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@Referencia", SqlDbType.NVarChar, 150).Value = referencia.Trim();
            cmd.Parameters.Add("@ReferenciaBase", SqlDbType.NVarChar, 150).Value = referenciaBase;
            cmd.Parameters.Add("@ClienteID", SqlDbType.Int).Value = clienteId;

            await using var rd = await cmd.ExecuteReaderAsync();
            if (!await rd.ReadAsync())
                return null;

            return new ParteImportacionMatch
            {
                ParteID = Convert.ToInt32(rd["ParteID"]),
                Activa = Convert.ToBoolean(rd["Activo"]),
                NumeroParte = rd["NumeroParte"] as string,
                ReferenciaSAP = rd["ReferenciaSAP"] as string
            };
        }
        private static async Task<ParteImportacionMatch?> BuscarParteImportacionIncluyendoInactivasAsync(
            string referencia,
            int clienteId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            var aliasMatch = await BuscarParteActivaPorAliasMaestroAsync(
                referencia,
                clienteId,
                cn,
                tx);

            if (aliasMatch != null)
                return aliasMatch;

            const string sql = @"
DECLARE @Normalizada NVARCHAR(150) = UPPER(
    REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(@Referencia)), '-', ''), '.', ''), ' ', ''), '/', ''), '_', '')
);

SELECT TOP (1)
    ParteID,
    ISNULL(Activo, 0) AS Activo,
    NumeroParte,
    ReferenciaSAP
FROM dbo.ERP_Partes
WHERE ClienteID = @ClienteID
  AND
  (
        UPPER(LTRIM(RTRIM(ISNULL(NumeroParte, '')))) = UPPER(LTRIM(RTRIM(@Referencia)))
     OR UPPER(LTRIM(RTRIM(ISNULL(ReferenciaSAP, '')))) = UPPER(LTRIM(RTRIM(@Referencia)))
     OR UPPER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(ISNULL(NumeroParte, ''), '-', ''), '.', ''), ' ', ''), '/', ''), '_', '')) = @Normalizada
     OR UPPER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(ISNULL(ReferenciaSAP, ''), '-', ''), '.', ''), ' ', ''), '/', ''), '_', '')) = @Normalizada
  )
ORDER BY
    CASE WHEN ISNULL(Activo, 0) = 1 THEN 0 ELSE 1 END,
    CASE
        WHEN UPPER(LTRIM(RTRIM(ISNULL(ReferenciaSAP, '')))) = UPPER(LTRIM(RTRIM(@Referencia))) THEN 0
        WHEN UPPER(LTRIM(RTRIM(ISNULL(NumeroParte, '')))) = UPPER(LTRIM(RTRIM(@Referencia))) THEN 1
        ELSE 2
    END,
    ParteID;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@Referencia", SqlDbType.NVarChar, 150).Value = referencia.Trim();
            cmd.Parameters.Add("@ClienteID", SqlDbType.Int).Value = clienteId;

            await using var rd = await cmd.ExecuteReaderAsync();
            if (!await rd.ReadAsync())
                return null;

            return new ParteImportacionMatch
            {
                ParteID = Convert.ToInt32(rd["ParteID"]),
                Activa = Convert.ToBoolean(rd["Activo"]),
                NumeroParte = rd["NumeroParte"] as string,
                ReferenciaSAP = rd["ReferenciaSAP"] as string
            };
        }

        [HttpGet]
        public async Task<IActionResult> DescargarDocumentoImportado(int id)
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            const string sql = @"
SELECT ArchivoOrigenNombre, Observaciones
FROM dbo.Planeacion_Releases
WHERE ReleaseID = @ReleaseID
  AND Activo = 1;";

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@ReleaseID", SqlDbType.Int).Value = id;

            await using var rd = await cmd.ExecuteReaderAsync();
            if (!await rd.ReadAsync())
                return NotFound();

            var fileName = rd["ArchivoOrigenNombre"] as string ?? $"Release_{id}.pdf";
            var observations = rd["Observaciones"] as string ?? string.Empty;
            var match = Regex.Match(
                observations,
                @"(?:ARCHIVO_GUARDADO|ARCHIVO):(?<path>[^;]+)",
                RegexOptions.IgnoreCase);

            if (!match.Success)
                return NotFound("Este Release no tiene una ruta de archivo original registrada.");

            var relative = match.Groups["path"].Value.Trim()
                .Replace('/', Path.DirectorySeparatorChar);
            var root = Path.Combine(
                _environment.ContentRootPath,
                "App_Data",
                "Releases",
                "Originales");

            var rootFull = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            var fullPath = Path.GetFullPath(Path.Combine(root, relative));

            if (!fullPath.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase) ||
                !System.IO.File.Exists(fullPath))
            {
                return NotFound("No se encontro el documento original conservado.");
            }

            var contentType = Path.GetExtension(fileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase)
                ? "application/pdf"
                : "application/octet-stream";


            // RELEASE_ORIGINAL_INLINE_V1_7
            if (contentType == "application/pdf")
            {
                var safeFileName = Path.GetFileName(fileName).Replace("\"", string.Empty);
                Response.Headers["Content-Disposition"] =
                    $"inline; filename=\"{safeFileName}\"";

                return PhysicalFile(
                    fullPath,
                    contentType,
                    enableRangeProcessing: true);
            }

            return PhysicalFile(
                fullPath,
                contentType,
                fileName,
                enableRangeProcessing: true);
        }

        [HttpGet]
        public async Task<IActionResult> VincularPartes(int id)
        {
            var vm = new PlaneacionReleaseVinculacionVm();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            const string sqlRelease = @"
SELECT
    r.ReleaseID,
    r.FolioRelease,
    r.ClienteID,
    ISNULL(c.Nombre, r.ClienteNombre) AS ClienteNombre,
    r.ArchivoOrigenNombre
FROM dbo.Planeacion_Releases r
LEFT JOIN dbo.ERP_Clientes c ON c.ClienteID = r.ClienteID
WHERE r.ReleaseID = @ReleaseID
  AND r.Activo = 1;";

            await using (var cmd = new SqlCommand(sqlRelease, cn))
            {
                cmd.Parameters.Add("@ReleaseID", SqlDbType.Int).Value = id;
                await using var rd = await cmd.ExecuteReaderAsync();
                if (!await rd.ReadAsync())
                    return NotFound();

                if (rd["ClienteID"] == DBNull.Value)
                {
                    TempData["Error"] = "El Release no tiene un cliente vinculado.";
                    return RedirectToAction(nameof(Detalle), new { id });
                }

                vm.ReleaseID = Convert.ToInt32(rd["ReleaseID"]);
                vm.FolioRelease = rd["FolioRelease"] as string;
                vm.ClienteID = Convert.ToInt32(rd["ClienteID"]);
                vm.ClienteNombre = rd["ClienteNombre"] as string;
                vm.ArchivoOrigenNombre = rd["ArchivoOrigenNombre"] as string;
            }

            const string sqlRows = @"
SELECT
    rr.ReleaseRenglonID,
    rr.Renglon,
    rr.ParteID,
    rr.NumeroParte,
    rr.ReferenciaSAP,
    rr.DesignacionDescripcionSAP,
    ISNULL(p.Activo, 0) AS ParteActiva
FROM dbo.Planeacion_ReleaseRenglones rr
LEFT JOIN dbo.ERP_Partes p ON p.ParteID = rr.ParteID
WHERE rr.ReleaseID = @ReleaseID
  AND rr.Activo = 1
ORDER BY rr.Renglon;";

            await using (var cmd = new SqlCommand(sqlRows, cn))
            {
                cmd.Parameters.Add("@ReleaseID", SqlDbType.Int).Value = id;
                await using var rd = await cmd.ExecuteReaderAsync();
                while (await rd.ReadAsync())
                {
                    vm.Renglones.Add(new PlaneacionReleaseVinculacionRenglonVm
                    {
                        ReleaseRenglonID = Convert.ToInt32(rd["ReleaseRenglonID"]),
                        Renglon = Convert.ToInt32(rd["Renglon"]),
                        ParteID = rd["ParteID"] == DBNull.Value ? null : Convert.ToInt32(rd["ParteID"]),
                        ParteActiva = Convert.ToBoolean(rd["ParteActiva"]),
                        NumeroParteOriginal = rd["NumeroParte"] as string,
                        ReferenciaOriginal = rd["ReferenciaSAP"] as string,
                        DescripcionOriginal = rd["DesignacionDescripcionSAP"] as string
                    });
                }
            }

            vm.PartesActivas = await CargarSelectAsync(
                cn,
                $@"SELECT
                        ParteID AS Id,
                        NumeroParte + ' | ' + ISNULL(NULLIF(ReferenciaSAP, ''), NumeroParte) + ' | ' + ISNULL(NULLIF(Designacion, ''), Descripcion) AS Texto
                    FROM dbo.ERP_Partes
                    WHERE Activo = 1
                      AND ClienteID = {vm.ClienteID}
                    ORDER BY NumeroParte;");

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VincularPartes(PlaneacionReleaseVinculacionPostVm vm)
        {
            var usuarioId = ObtenerUsuarioID();
            if (usuarioId <= 0)
            {
                TempData["Error"] = "No se pudo identificar el usuario de sesion.";
                return RedirectToAction(nameof(VincularPartes), new { id = vm.ReleaseID });
            }

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();

            try
            {
                const string sqlCliente = @"
SELECT ClienteID
FROM dbo.Planeacion_Releases
WHERE ReleaseID = @ReleaseID
  AND Activo = 1;";

                int clienteId;
                await using (var cmd = new SqlCommand(sqlCliente, cn, tx))
                {
                    cmd.Parameters.Add("@ReleaseID", SqlDbType.Int).Value = vm.ReleaseID;
                    var result = await cmd.ExecuteScalarAsync();
                    if (result == null || result == DBNull.Value)
                    {
                        await tx.RollbackAsync();
                        return NotFound();
                    }
                    clienteId = Convert.ToInt32(result);
                }

                foreach (var row in vm.Renglones.Where(x => x.ParteID.HasValue))
                {
                    const string sqlPart = @"
SELECT
    ParteID,
    NumeroParte,
    ReferenciaSAP,
    COALESCE(NULLIF(Designacion, ''), Descripcion) AS Descripcion
FROM dbo.ERP_Partes
WHERE ParteID = @ParteID
  AND ClienteID = @ClienteID
  AND Activo = 1;";

                    int parteId;
                    string numeroParte;
                    string referencia;
                    string descripcion;

                    await using (var cmd = new SqlCommand(sqlPart, cn, tx))
                    {
                        cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value = row.ParteID!.Value;
                        cmd.Parameters.Add("@ClienteID", SqlDbType.Int).Value = clienteId;
                        await using var rd = await cmd.ExecuteReaderAsync();
                        if (!await rd.ReadAsync())
                            throw new InvalidOperationException("La parte seleccionada no esta activa o no pertenece al cliente del Release.");

                        parteId = Convert.ToInt32(rd["ParteID"]);
                        numeroParte = rd["NumeroParte"] as string ?? string.Empty;
                        referencia = rd["ReferenciaSAP"] as string ?? numeroParte;
                        descripcion = rd["Descripcion"] as string ?? string.Empty;
                    }

                    const string sqlUpdate = @"
UPDATE dbo.Planeacion_ReleaseRenglones
SET
    ParteID = @ParteID,
    NumeroParte = @NumeroParte,
    ReferenciaSAP = @ReferenciaSAP,
    DesignacionDescripcionSAP = @Descripcion
WHERE ReleaseID = @ReleaseID
  AND ReleaseRenglonID = @ReleaseRenglonID
  AND Activo = 1;

UPDATE dbo.Planeacion_ReleaseDetalle
SET
    ParteID = @ParteID,
    NumeroParte = @NumeroParte,
    ReferenciaSAP = @ReferenciaSAP,
    DesignacionDescripcionSAP = @Descripcion,
    UsuarioModificacionID = @UsuarioID,
    FechaModificacion = GETDATE()
WHERE ReleaseID = @ReleaseID
  AND ReleaseRenglonID = @ReleaseRenglonID
  AND Activo = 1;";

                    await using var update = new SqlCommand(sqlUpdate, cn, tx);
                    update.Parameters.Add("@ParteID", SqlDbType.Int).Value = parteId;
                    update.Parameters.Add("@NumeroParte", SqlDbType.NVarChar, 120).Value = numeroParte;
                    update.Parameters.Add("@ReferenciaSAP", SqlDbType.NVarChar, 150).Value = referencia;
                    update.Parameters.Add("@Descripcion", SqlDbType.NVarChar, 300).Value = descripcion;
                    update.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                    update.Parameters.Add("@ReleaseID", SqlDbType.Int).Value = vm.ReleaseID;
                    update.Parameters.Add("@ReleaseRenglonID", SqlDbType.Int).Value = row.ReleaseRenglonID;
                    await update.ExecuteNonQueryAsync();
                }

                const string sqlPending = @"
SELECT COUNT(1)
FROM dbo.Planeacion_ReleaseRenglones rr
LEFT JOIN dbo.ERP_Partes p
    ON p.ParteID = rr.ParteID
   AND p.Activo = 1
WHERE rr.ReleaseID = @ReleaseID
  AND rr.Activo = 1
  AND p.ParteID IS NULL;";

                int pendientes;
                await using (var cmd = new SqlCommand(sqlPending, cn, tx))
                {
                    cmd.Parameters.Add("@ReleaseID", SqlDbType.Int).Value = vm.ReleaseID;
                    pendientes = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                }

                if (pendientes > 0)
                {
                    await tx.CommitAsync();
                    TempData["Error"] = $"La vinculacion fue guardada, pero todavia quedan {pendientes} renglon(es) sin una parte activa.";
                    return RedirectToAction(nameof(VincularPartes), new { id = vm.ReleaseID });
                }

                await CerrarVersionExcelAlCompletarVinculacionAsync(
                    vm.ReleaseID,
                    usuarioId,
                    cn,
                    tx);

                var detalles = await ObtenerDetallesParaRecalculoAsync(vm.ReleaseID, cn, tx);
                foreach (var detalle in detalles)
                {
                    await CompletarDetalleDesdeParteAsync(detalle, cn, tx);
                    await CalcularNecesidadAsync(detalle, cn, tx);
                    await ActualizarReleaseDetalleCalculoAsync(
                        detalle,
                        usuarioId,
                        cn,
                        tx);
                }

                await ActualizarEstatusReleaseAsync(
                    vm.ReleaseID,
                    PlaneacionReleaseEstatus.Calculado,
                    usuarioId,
                    cn,
                    tx);

                await tx.CommitAsync();
                TempData["Success"] = "Partes vinculadas y Release recalculado correctamente.";
                return RedirectToAction(nameof(Detalle), new { id = vm.ReleaseID });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                TempData["Error"] = "No fue posible completar la vinculacion: " + ex.Message;
                return RedirectToAction(nameof(VincularPartes), new { id = vm.ReleaseID });
            }
        }

        [HttpGet]
        public async Task<IActionResult> Detalle(int id)
        {
            var vm = await ObtenerReleaseDetalleAsync(id);

            if (vm == null)
                return NotFound();

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Recalcular(int id)
        {
            var usuarioId = ObtenerUsuarioID();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            await using var tx = await cn.BeginTransactionAsync();

            try
            {
                var detalles = await ObtenerDetallesParaRecalculoAsync(id, cn, (SqlTransaction)tx);

                if (!detalles.Any())
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "No hay renglones activos para recalcular.";
                    return RedirectToAction(nameof(Detalle), new { id });
                }

                foreach (var detalle in detalles)
                {
                    await CompletarDetalleDesdeParteAsync(detalle, cn, (SqlTransaction)tx);
                    await CalcularNecesidadAsync(detalle, cn, (SqlTransaction)tx);

                    await ActualizarReleaseDetalleCalculoAsync(
                        detalle,
                        usuarioId,
                        cn,
                        (SqlTransaction)tx
                    );
                }

                await ActualizarEstatusReleaseAsync(
                    id,
                    PlaneacionReleaseEstatus.Calculado,
                    usuarioId,
                    cn,
                    (SqlTransaction)tx
                );

                await tx.CommitAsync();

                TempData["Success"] = "Release recalculado correctamente.";
                return RedirectToAction(nameof(Detalle), new { id });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();

                TempData["Error"] = "Error al recalcular: " + ex.Message;
                return RedirectToAction(nameof(Detalle), new { id });
            }
        }

        private async Task<int> InsertarReleaseAsync(
     PlaneacionReleaseCrearVm vm,
     string? clienteNombre,
     int usuarioId,
     SqlConnection cn,
     SqlTransaction tx)
        {
            const string sql = @"
INSERT INTO dbo.Planeacion_Releases
(
    FolioRelease,
    FolioCliente,
    ClienteID,
    ClienteNombre,
    FechaRecepcion,
    VersionRelease,
    ArchivoOrigenNombre,
    PlantillaImportacion,
    ImportadoDesdeArchivo,
    NivelCriticidad,
    ComentarioCriticidad,
    Observaciones,
    EstatusID,
    UsuarioCreacionID,
    FechaCreacion,
    Activo
)
OUTPUT INSERTED.ReleaseID
VALUES
(
    @FolioRelease,
    @FolioCliente,
    @ClienteID,
    @ClienteNombre,
    @FechaRecepcion,
    @VersionRelease,
    @ArchivoOrigenNombre,
    @PlantillaImportacion,
    @ImportadoDesdeArchivo,
    @NivelCriticidad,
    @ComentarioCriticidad,
    @Observaciones,
    @EstatusID,
    @UsuarioCreacionID,
    GETDATE(),
    1
);";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@FolioRelease", SqlDbType.NVarChar, 40).Value =
                (object?)vm.FolioRelease ?? DBNull.Value;

            cmd.Parameters.Add("@FolioCliente", SqlDbType.NVarChar, 100).Value =
                (object?)vm.FolioCliente ?? DBNull.Value;

            cmd.Parameters.Add("@ClienteID", SqlDbType.Int).Value =
                (object?)vm.ClienteID ?? DBNull.Value;

            cmd.Parameters.Add("@ClienteNombre", SqlDbType.NVarChar, 200).Value =
                (object?)clienteNombre ?? DBNull.Value;

            cmd.Parameters.Add("@FechaRecepcion", SqlDbType.Date).Value =
                vm.FechaRecepcion.Date;

            cmd.Parameters.Add("@VersionRelease", SqlDbType.NVarChar, 50).Value =
                (object?)vm.VersionRelease ?? DBNull.Value;

            cmd.Parameters.Add("@ArchivoOrigenNombre", SqlDbType.NVarChar, 255).Value =
                (object?)vm.ArchivoOrigenNombre ?? DBNull.Value;

            cmd.Parameters.Add("@PlantillaImportacion", SqlDbType.NVarChar, 100).Value =
                (object?)vm.PlantillaImportacion ?? DBNull.Value;

            cmd.Parameters.Add("@ImportadoDesdeArchivo", SqlDbType.Bit).Value =
                vm.ImportadoDesdeArchivo;

            cmd.Parameters.Add("@NivelCriticidad", SqlDbType.NVarChar, 20).Value =
                string.IsNullOrWhiteSpace(vm.NivelCriticidad)
                    ? "NORMAL"
                    : vm.NivelCriticidad.Trim().ToUpperInvariant();

            cmd.Parameters.Add("@ComentarioCriticidad", SqlDbType.NVarChar, 300).Value =
                (object?)vm.ComentarioCriticidad ?? DBNull.Value;

            cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 500).Value =
                (object?)vm.Observaciones ?? DBNull.Value;

            cmd.Parameters.Add("@EstatusID", SqlDbType.Int).Value =
                PlaneacionReleaseEstatus.Capturado;

            cmd.Parameters.Add("@UsuarioCreacionID", SqlDbType.Int).Value =
                usuarioId;

            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        private async Task InsertarReleaseDetalleAsync(
    int releaseId,
    int releaseRenglonId,
    int secuenciaEntrega,
    PlaneacionReleaseDetalleCrearVm d,
    int usuarioId,
    SqlConnection cn,
    SqlTransaction tx)
        {
            const string sql = @"
INSERT INTO dbo.Planeacion_ReleaseDetalle
(
    ReleaseID,
ReleaseRenglonID,
SecuenciaEntrega,
    Renglon,
    ParteID,
    NumeroParte,
    ReferenciaSAP,
    DesignacionDescripcionSAP,
FechaCarga,
    FechaRequerida,
    CantidadRequerida,

    PTDisponibleAlCalcular,
    ProduccionProgramadaPendiente,
    PiezasDesdePT,
    PiezasAProducir,

    MaterialID,
    MaterialCodigo,
    MaterialDescripcion,
    PesoBrutoPieza,
    MPRequeridaKg,
    MPDisponibleKg,

    EmbalajeCodigo,
    EmbalajeDescripcion,
    PiezasPorEmbalaje,
    EmbalajeRequerido,
    EmbalajeDisponible,

    MoldeID,
    MoldeCodigo,
    MaquinaSugeridaID,
    MaquinaSugeridaCodigo,
    MaquinaSugeridaNombre,
    ObjetivoHora,
    HorasNecesarias,
    FechaInicioSugerida,
    FechaFinEstimada,
    DaTiempo,
    MensajeCapacidad,

    EstatusID,
    UsuarioCreacionID,
    FechaCreacion,
    Activo
)
VALUES
(
    @ReleaseID,
@ReleaseRenglonID,
@SecuenciaEntrega,
    @Renglon,
    @ParteID,
    @NumeroParte,
    @ReferenciaSAP,
    @DesignacionDescripcionSAP,
@FechaCarga,
    @FechaRequerida,
    @CantidadRequerida,

    @PTDisponibleAlCalcular,
    @ProduccionProgramadaPendiente,
    @PiezasDesdePT,
    @PiezasAProducir,

    @MaterialID,
    @MaterialCodigo,
    @MaterialDescripcion,
    @PesoBrutoPieza,
    @MPRequeridaKg,
    @MPDisponibleKg,

    @EmbalajeCodigo,
    @EmbalajeDescripcion,
    @PiezasPorEmbalaje,
    @EmbalajeRequerido,
    @EmbalajeDisponible,

    @MoldeID,
    @MoldeCodigo,
    @MaquinaSugeridaID,
    @MaquinaSugeridaCodigo,
    @MaquinaSugeridaNombre,
    @ObjetivoHora,
    @HorasNecesarias,
    @FechaInicioSugerida,
    @FechaFinEstimada,
    @DaTiempo,
    @MensajeCapacidad,

    @EstatusID,
    @UsuarioCreacionID,
    GETDATE(),
    1
);";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@FechaCarga", SqlDbType.Date).Value =
    (object?)d.FechaCarga ?? DBNull.Value;
            cmd.Parameters.Add("@ReleaseRenglonID", SqlDbType.Int).Value = releaseRenglonId;
            cmd.Parameters.Add("@SecuenciaEntrega", SqlDbType.Int).Value = secuenciaEntrega;

            AgregarParametrosDetalle(cmd, releaseId, d, usuarioId);

            await cmd.ExecuteNonQueryAsync();
        }

        private async Task CompletarDetalleDesdeParteAsync(
            PlaneacionReleaseDetalleCrearVm d,
            SqlConnection cn,
            SqlTransaction tx)
        {
            if (!d.ParteID.HasValue)
                return;

            const string sql = @"
SELECT
    p.NumeroParte,
    p.ReferenciaSAP,
    p.Descripcion,
    p.Designacion,

    t.MaterialID,
    t.MaterialCodigo,
    t.MaterialDescripcion,
    t.PesoBrutoPieza,
    t.PiezasPorEmbalaje,
    t.EmbalajeCodigo,
    t.EmbalajeDescripcion,
    t.ObjetivoHora,
    t.MoldePrincipalID,
    mol.CodigoMolde AS MoldeCodigo,
    t.MaquinaPrincipalID,
    maq.Codigo AS MaquinaCodigo,
    maq.Nombre AS MaquinaNombre
FROM dbo.ERP_Partes p
LEFT JOIN dbo.ERP_ParteDatosTecnicos t
    ON t.ParteID = p.ParteID
   AND t.Activo = 1
LEFT JOIN dbo.ERP_Moldes mol
    ON mol.MoldeID = t.MoldePrincipalID
LEFT JOIN dbo.ERP_Maquinas maq
    ON maq.MaquinaID = t.MaquinaPrincipalID
WHERE p.ParteID = @ParteID
  AND p.Activo = 1;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value = d.ParteID.Value;

            await using var rd = await cmd.ExecuteReaderAsync();

            if (!await rd.ReadAsync())
                return;
            var numeroParte = rd["NumeroParte"] as string;
            var referenciaSap = rd["ReferenciaSAP"] as string;
            var descripcion = rd["Descripcion"] as string;
            var designacion = rd["Designacion"] as string;

            if (string.IsNullOrWhiteSpace(d.NumeroParte))
                d.NumeroParte = numeroParte;

            if (string.IsNullOrWhiteSpace(d.ReferenciaSAP))
                d.ReferenciaSAP = !string.IsNullOrWhiteSpace(referenciaSap)
                    ? referenciaSap
                    : numeroParte;

            if (string.IsNullOrWhiteSpace(d.DesignacionDescripcionSAP))
                d.DesignacionDescripcionSAP = !string.IsNullOrWhiteSpace(designacion)
                    ? designacion
                    : descripcion;

            if (!d.MaterialID.HasValue && rd["MaterialID"] != DBNull.Value)
                d.MaterialID = Convert.ToInt32(rd["MaterialID"]);

            d.MaterialCodigo ??= rd["MaterialCodigo"] as string;
            d.MaterialDescripcion ??= rd["MaterialDescripcion"] as string;

            if (!d.PesoBrutoPieza.HasValue && rd["PesoBrutoPieza"] != DBNull.Value)
                d.PesoBrutoPieza = Convert.ToDecimal(rd["PesoBrutoPieza"]);

            d.EmbalajeCodigo ??= rd["EmbalajeCodigo"] as string;
            d.EmbalajeDescripcion ??= rd["EmbalajeDescripcion"] as string;

            if (!d.PiezasPorEmbalaje.HasValue && rd["PiezasPorEmbalaje"] != DBNull.Value)
                d.PiezasPorEmbalaje = Convert.ToDecimal(rd["PiezasPorEmbalaje"]);

            if (!d.ObjetivoHora.HasValue && rd["ObjetivoHora"] != DBNull.Value)
                d.ObjetivoHora = Convert.ToInt32(rd["ObjetivoHora"]);

            if (!d.MoldeID.HasValue && rd["MoldePrincipalID"] != DBNull.Value)
                d.MoldeID = Convert.ToInt32(rd["MoldePrincipalID"]);

            d.MoldeCodigo ??= rd["MoldeCodigo"] as string;

            if (!d.MaquinaSugeridaID.HasValue && rd["MaquinaPrincipalID"] != DBNull.Value)
                d.MaquinaSugeridaID = Convert.ToInt32(rd["MaquinaPrincipalID"]);

            d.MaquinaSugeridaCodigo ??= rd["MaquinaCodigo"] as string;
            d.MaquinaSugeridaNombre ??= rd["MaquinaNombre"] as string;
        }

        private async Task CalcularNecesidadAsync(
            PlaneacionReleaseDetalleCrearVm d,
            SqlConnection cn,
            SqlTransaction tx)
        {
            var cantidad = d.CantidadRequerida;

            var ptDisponible = 0;

            if (d.ParteID.HasValue)
            {
                const string sqlPT = @"
SELECT TOP 1 ISNULL(Disponible, 0)
FROM dbo.vw_AlmacenPTInventario
WHERE ParteID = @ParteID;";

                await using (var cmd = new SqlCommand(sqlPT, cn, tx))
                {
                    cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value = d.ParteID.Value;

                    var result = await cmd.ExecuteScalarAsync();
                    if (result != null && result != DBNull.Value)
                        ptDisponible = Convert.ToInt32(result);
                }
            }

            var programadoPendiente = await ObtenerProduccionProgramadaPendienteAsync(d.ParteID, cn, tx);

            var ptUsable = Math.Min(ptDisponible, cantidad);

            d.PTDisponibleAlCalcular = ptDisponible;
            d.ProduccionProgramadaPendiente = programadoPendiente;

            d.PiezasDesdePT = ptUsable;
            d.PiezasAProducir = Math.Max(0, cantidad - ptUsable - programadoPendiente);

            if (d.PiezasAProducir < 0)
                d.PiezasAProducir = 0;

            if (d.PiezasAProducir > 0 && d.PesoBrutoPieza.HasValue && d.PesoBrutoPieza.Value > 0)
            {
                d.MPRequeridaKg = Math.Round(d.PiezasAProducir.Value * d.PesoBrutoPieza.Value, 4);
            }
            else
            {
                d.MPRequeridaKg = 0;
            }

            d.MPDisponibleKg = await ObtenerMPDisponibleAsync(d.MaterialID, cn, tx);

            if (d.PiezasAProducir > 0 && d.PiezasPorEmbalaje.HasValue && d.PiezasPorEmbalaje.Value > 0)
            {
                d.EmbalajeRequerido = Math.Ceiling(d.PiezasAProducir.Value / d.PiezasPorEmbalaje.Value);
            }
            else
            {
                d.EmbalajeRequerido = 0;
            }

            d.EmbalajeDisponible = await ObtenerEmbalajeDisponibleAsync(d.EmbalajeCodigo, cn, tx);

            if (d.PiezasAProducir > 0 && d.ObjetivoHora.HasValue && d.ObjetivoHora.Value > 0)
            {
                d.HorasNecesarias = Math.Round(d.PiezasAProducir.Value / (decimal)d.ObjetivoHora.Value, 2);
            }
            else
            {
                d.HorasNecesarias = 0;
            }

            d.FechaInicioSugerida = DateTime.Now;

            if (d.HorasNecesarias.HasValue && d.HorasNecesarias.Value > 0)
                d.FechaFinEstimada = DateTime.Now.AddHours((double)d.HorasNecesarias.Value);
            else
                d.FechaFinEstimada = DateTime.Now;

            d.DaTiempo = d.FechaRequerida.HasValue
                ? d.FechaFinEstimada?.Date <= d.FechaRequerida.Value.Date
                : null;

            d.MensajeCapacidad = ConstruirMensajeCapacidad(d);
            d.EstatusID = PlaneacionReleaseEstatus.Calculado;
        }

        private string ConstruirMensajeCapacidad(PlaneacionReleaseDetalleCrearVm d)
        {
            if (d.CantidadRequerida <= 0)
                return "Sin cantidad requerida.";

            if ((d.PiezasAProducir ?? 0) <= 0)
                return "La necesidad queda cubierta con PT disponible y/o producción ya programada.";

            if (!d.MaterialID.HasValue)
                return "La parte no tiene material relacionado. Revisar catálogo técnico.";

            if ((d.MPDisponibleKg ?? 0) < (d.MPRequeridaKg ?? 0))
                return "No hay MP suficiente para cubrir la necesidad calculada.";

            if (!d.ObjetivoHora.HasValue || d.ObjetivoHora.Value <= 0)
                return "La parte no tiene objetivo por hora configurado. No se puede estimar capacidad.";

            if (d.DaTiempo == true)
                return "Con el cálculo inicial sí da tiempo contra la fecha requerida.";

            if (d.DaTiempo == false)
                return "Con el cálculo inicial no da tiempo contra la fecha requerida. Revisar máquina, turnos o prioridad.";

            return "Necesidad calculada.";
        }

        private async Task<int> ObtenerProduccionProgramadaPendienteAsync(
    int? parteId,
    SqlConnection cn,
    SqlTransaction tx)
        {
            if (!parteId.HasValue)
                return 0;

            const string sql = @"
SELECT ISNULL(SUM(d.CantidadPiezas), 0)
FROM dbo.SolicitudesProduccionDetalle d
INNER JOIN dbo.SolicitudesProduccion s
    ON s.SolicitudProduccionID = d.SolicitudProduccionID
WHERE d.ParteID = @ParteID
  AND d.Activo = 1
  AND s.Activo = 1
  AND ISNULL(s.EstatusID, 1) NOT IN (9, 99);";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value = parteId.Value;

            var result = await cmd.ExecuteScalarAsync();

            return result == null || result == DBNull.Value
                ? 0
                : Convert.ToInt32(result);
        }

        private async Task<decimal> ObtenerMPDisponibleAsync(
            int? materialId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            if (!materialId.HasValue)
                return 0;

            const string sql = @"
-- NSQ_RELEASE_DISPONIBLE_RESERVAS_V1
SELECT TOP 1 ISNULL(Disponible, 0)
FROM dbo.vw_AlmacenMPInventario
WHERE MaterialID = @MaterialID
  AND TipoMP = N'V'
ORDER BY OrdenTipo;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@MaterialID", SqlDbType.Int).Value = materialId.Value;

            var result = await cmd.ExecuteScalarAsync();
            return result == null || result == DBNull.Value ? 0 : Convert.ToDecimal(result);
        }

        private async Task<decimal> ObtenerEmbalajeDisponibleAsync(
            string? embalajeCodigo,
            SqlConnection cn,
            SqlTransaction tx)
        {
            if (string.IsNullOrWhiteSpace(embalajeCodigo))
                return 0;

            const string sql = @"
SELECT TOP 1 ISNULL(Disponible, 0)
FROM dbo.vw_AlmacenEmbalajesInventario
WHERE Codigo = @Codigo;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@Codigo", SqlDbType.NVarChar, 100).Value = embalajeCodigo;

            var result = await cmd.ExecuteScalarAsync();
            return result == null || result == DBNull.Value ? 0 : Convert.ToDecimal(result);
        }

        private async Task<PlaneacionReleaseDetalleVm?> ObtenerReleaseDetalleAsync(int releaseId)
        {
            PlaneacionReleaseDetalleVm? vm = null;

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            const string sqlRelease = @"
SELECT
    r.ReleaseID,
    r.FolioRelease,
    r.FolioCliente,
    r.ClienteID,
    ISNULL(c.Nombre, r.ClienteNombre) AS ClienteNombre,
    r.FechaRecepcion,
    r.VersionRelease,
    r.ArchivoOrigenNombre,
    r.PlantillaImportacion,
    ISNULL(r.ImportadoDesdeArchivo, 0) AS ImportadoDesdeArchivo,
    r.Observaciones,
    r.EstatusID
FROM dbo.Planeacion_Releases r
LEFT JOIN dbo.ERP_Clientes c
    ON c.ClienteID = r.ClienteID
WHERE r.ReleaseID = @ReleaseID
  AND r.Activo = 1;";

            await using (var cmd = new SqlCommand(sqlRelease, cn))
            {
                cmd.Parameters.Add("@ReleaseID", SqlDbType.Int).Value = releaseId;

                await using var rd = await cmd.ExecuteReaderAsync();

                if (!await rd.ReadAsync())
                    return null;

                var estatusId = Convert.ToInt32(rd["EstatusID"]);

                vm = new PlaneacionReleaseDetalleVm
                {
                    ReleaseID = Convert.ToInt32(rd["ReleaseID"]),
                    FolioRelease = rd["FolioRelease"] as string,
                    FolioCliente = rd["FolioCliente"] as string,
                    ClienteID = rd["ClienteID"] == DBNull.Value ? null : Convert.ToInt32(rd["ClienteID"]),
                    ClienteNombre = rd["ClienteNombre"] as string,
                    FechaRecepcion = Convert.ToDateTime(rd["FechaRecepcion"]),
                    VersionRelease = rd["VersionRelease"] as string,
                    ArchivoOrigenNombre = rd["ArchivoOrigenNombre"] as string,
                    PlantillaImportacion = rd["PlantillaImportacion"] as string,
                    ImportadoDesdeArchivo = rd["ImportadoDesdeArchivo"] != DBNull.Value && Convert.ToBoolean(rd["ImportadoDesdeArchivo"]),
                    Observaciones = rd["Observaciones"] as string,
                    EstatusID = estatusId,
                    EstatusNombre = PlaneacionReleaseEstatus.Nombre(estatusId)
                };
            }

            vm.Detalles = await ObtenerDetalleRenglonesAsync(releaseId, cn);

            return vm;
        }

        private async Task<List<PlaneacionReleaseDetalleRenglonVm>> ObtenerDetalleRenglonesAsync(
            int releaseId,
            SqlConnection cn)
        {
            var lista = new List<PlaneacionReleaseDetalleRenglonVm>();

            const string sql = @"
SELECT
    d.ReleaseDetalleID,
    d.ReleaseRenglonID,
    d.SecuenciaEntrega,
    d.Renglon,
    d.ParteID,
    d.NumeroParte,
    d.ReferenciaSAP,
    d.DesignacionDescripcionSAP,
    rr.UnidadMedidaCliente,
    rr.ContratoCliente,
    d.FechaCarga,
    d.FechaRequerida,
    d.CantidadRequerida,
    d.PTDisponibleAlCalcular,
    d.ProduccionProgramadaPendiente,
    d.PiezasDesdePT,
    d.PiezasAProducir,
    d.MaterialID,
    d.MaterialCodigo,
    d.MaterialDescripcion,
    d.PesoBrutoPieza,
    d.MPRequeridaKg,
    d.MPDisponibleKg,
    d.EmbalajeCodigo,
    d.EmbalajeDescripcion,
    d.PiezasPorEmbalaje,
    d.EmbalajeRequerido,
    d.EmbalajeDisponible,
    d.MoldeID,
    d.MoldeCodigo,
    d.MaquinaSugeridaID,
    d.MaquinaSugeridaCodigo,
    d.MaquinaSugeridaNombre,
    d.ObjetivoHora,
    d.HorasNecesarias,
    d.FechaInicioSugerida,
    d.FechaFinEstimada,
    d.DaTiempo,
    d.MensajeCapacidad,
    programaActivo.ProgramaProduccionID AS ProgramaProduccionID,
    d.SolicitudProduccionID,
    d.EstatusID
FROM dbo.Planeacion_ReleaseDetalle d
LEFT JOIN dbo.Planeacion_ReleaseRenglones rr
    ON rr.ReleaseRenglonID = d.ReleaseRenglonID
   AND rr.Activo = 1
OUTER APPLY
(
    SELECT TOP (1)
        pp.ProgramaProduccionID
    FROM dbo.Planeacion_ProgramaProduccion pp
    WHERE pp.ReleaseDetalleID = d.ReleaseDetalleID
      AND pp.Activo = 1
      AND ISNULL(pp.EstatusID, 1) NOT IN (9, 99)
    ORDER BY pp.ProgramaProduccionID DESC
) programaActivo
WHERE d.ReleaseID = @ReleaseID
  AND d.Activo = 1
ORDER BY d.Renglon, ISNULL(d.SecuenciaEntrega, 9999), d.FechaRequerida;";

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@ReleaseID", SqlDbType.Int).Value = releaseId;

            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                lista.Add(new PlaneacionReleaseDetalleRenglonVm
                {
                    ReleaseDetalleID = Convert.ToInt32(rd["ReleaseDetalleID"]),
                    ReleaseRenglonID = rd["ReleaseRenglonID"] == DBNull.Value ? null : Convert.ToInt32(rd["ReleaseRenglonID"]),
                    SecuenciaEntrega = rd["SecuenciaEntrega"] == DBNull.Value ? null : Convert.ToInt32(rd["SecuenciaEntrega"]),
                    Renglon = Convert.ToInt32(rd["Renglon"]),
                    ParteID = rd["ParteID"] == DBNull.Value ? null : Convert.ToInt32(rd["ParteID"]),
                    NumeroParte = rd["NumeroParte"] as string,
                    ReferenciaSAP = rd["ReferenciaSAP"] as string,
                    DesignacionDescripcionSAP = rd["DesignacionDescripcionSAP"] as string,
                    UnidadMedidaCliente = rd["UnidadMedidaCliente"] as string,
                    ContratoCliente = rd["ContratoCliente"] as string,
                    FechaCarga = rd["FechaCarga"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaCarga"]),
                    FechaRequerida = Convert.ToDateTime(rd["FechaRequerida"]),
                    CantidadRequerida = Convert.ToInt32(rd["CantidadRequerida"]),
                    PTDisponibleAlCalcular = rd["PTDisponibleAlCalcular"] == DBNull.Value ? null : Convert.ToInt32(rd["PTDisponibleAlCalcular"]),
                    ProduccionProgramadaPendiente = rd["ProduccionProgramadaPendiente"] == DBNull.Value ? null : Convert.ToInt32(rd["ProduccionProgramadaPendiente"]),
                    PiezasDesdePT = rd["PiezasDesdePT"] == DBNull.Value ? null : Convert.ToInt32(rd["PiezasDesdePT"]),
                    PiezasAProducir = rd["PiezasAProducir"] == DBNull.Value ? null : Convert.ToInt32(rd["PiezasAProducir"]),
                    MaterialID = rd["MaterialID"] == DBNull.Value ? null : Convert.ToInt32(rd["MaterialID"]),
                    MaterialCodigo = rd["MaterialCodigo"] as string,
                    MaterialDescripcion = rd["MaterialDescripcion"] as string,
                    PesoBrutoPieza = rd["PesoBrutoPieza"] == DBNull.Value ? null : Convert.ToDecimal(rd["PesoBrutoPieza"]),
                    MPRequeridaKg = rd["MPRequeridaKg"] == DBNull.Value ? null : Convert.ToDecimal(rd["MPRequeridaKg"]),
                    MPDisponibleKg = rd["MPDisponibleKg"] == DBNull.Value ? null : Convert.ToDecimal(rd["MPDisponibleKg"]),
                    EmbalajeCodigo = rd["EmbalajeCodigo"] as string,
                    EmbalajeDescripcion = rd["EmbalajeDescripcion"] as string,
                    PiezasPorEmbalaje = rd["PiezasPorEmbalaje"] == DBNull.Value ? null : Convert.ToDecimal(rd["PiezasPorEmbalaje"]),
                    EmbalajeRequerido = rd["EmbalajeRequerido"] == DBNull.Value ? null : Convert.ToDecimal(rd["EmbalajeRequerido"]),
                    EmbalajeDisponible = rd["EmbalajeDisponible"] == DBNull.Value ? null : Convert.ToDecimal(rd["EmbalajeDisponible"]),
                    MoldeID = rd["MoldeID"] == DBNull.Value ? null : Convert.ToInt32(rd["MoldeID"]),
                    MoldeCodigo = rd["MoldeCodigo"] as string,
                    MaquinaSugeridaID = rd["MaquinaSugeridaID"] == DBNull.Value ? null : Convert.ToInt32(rd["MaquinaSugeridaID"]),
                    MaquinaSugeridaCodigo = rd["MaquinaSugeridaCodigo"] as string,
                    MaquinaSugeridaNombre = rd["MaquinaSugeridaNombre"] as string,
                    ObjetivoHora = rd["ObjetivoHora"] == DBNull.Value ? null : Convert.ToInt32(rd["ObjetivoHora"]),
                    HorasNecesarias = rd["HorasNecesarias"] == DBNull.Value ? null : Convert.ToDecimal(rd["HorasNecesarias"]),
                    FechaInicioSugerida = rd["FechaInicioSugerida"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaInicioSugerida"]),
                    FechaFinEstimada = rd["FechaFinEstimada"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaFinEstimada"]),
                    DaTiempo = rd["DaTiempo"] == DBNull.Value ? null : Convert.ToBoolean(rd["DaTiempo"]),
                    MensajeCapacidad = rd["MensajeCapacidad"] as string,
                    ProgramaProduccionID = rd["ProgramaProduccionID"] == DBNull.Value ? null : Convert.ToInt32(rd["ProgramaProduccionID"]),
                    SolicitudProduccionID = rd["SolicitudProduccionID"] == DBNull.Value ? null : Convert.ToInt32(rd["SolicitudProduccionID"]),
                    EstatusID = Convert.ToInt32(rd["EstatusID"])
                });
            }

            return lista;
        }

        private async Task<List<PlaneacionReleaseDetalleCrearVm>> ObtenerDetallesParaRecalculoAsync(
            int releaseId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            var lista = new List<PlaneacionReleaseDetalleCrearVm>();

            const string sql = @"
SELECT
    ReleaseDetalleID,
    Renglon,
    ParteID,
    NumeroParte,
    ReferenciaSAP,
    DesignacionDescripcionSAP,
    FechaCarga,
    FechaRequerida,
    CantidadRequerida
FROM dbo.Planeacion_ReleaseDetalle
WHERE ReleaseID = @ReleaseID
  AND Activo = 1
ORDER BY Renglon;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@ReleaseID", SqlDbType.Int).Value = releaseId;

            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                lista.Add(new PlaneacionReleaseDetalleCrearVm
                {
                    ReleaseDetalleID = Convert.ToInt32(rd["ReleaseDetalleID"]),
                    Renglon = Convert.ToInt32(rd["Renglon"]),
                    ParteID = rd["ParteID"] == DBNull.Value ? null : Convert.ToInt32(rd["ParteID"]),
                    NumeroParte = rd["NumeroParte"] as string,
                    ReferenciaSAP = rd["ReferenciaSAP"] as string,
                    DesignacionDescripcionSAP = rd["DesignacionDescripcionSAP"] as string,
                    FechaCarga = rd["FechaCarga"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaCarga"]),
                    FechaRequerida = Convert.ToDateTime(rd["FechaRequerida"]),
                    CantidadRequerida = Convert.ToInt32(rd["CantidadRequerida"])
                });
            }

            return lista;
        }

        private async Task ActualizarReleaseDetalleCalculoAsync(
            PlaneacionReleaseDetalleCrearVm d,
            int usuarioId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
UPDATE dbo.Planeacion_ReleaseDetalle
SET
    NumeroParte = @NumeroParte,
    ReferenciaSAP = @ReferenciaSAP,
    DesignacionDescripcionSAP = @DesignacionDescripcionSAP,

    PTDisponibleAlCalcular = @PTDisponibleAlCalcular,
    ProduccionProgramadaPendiente = @ProduccionProgramadaPendiente,
    PiezasDesdePT = @PiezasDesdePT,
    PiezasAProducir = @PiezasAProducir,

    MaterialID = @MaterialID,
    MaterialCodigo = @MaterialCodigo,
    MaterialDescripcion = @MaterialDescripcion,
    PesoBrutoPieza = @PesoBrutoPieza,
    MPRequeridaKg = @MPRequeridaKg,
    MPDisponibleKg = @MPDisponibleKg,

    EmbalajeCodigo = @EmbalajeCodigo,
    EmbalajeDescripcion = @EmbalajeDescripcion,
    PiezasPorEmbalaje = @PiezasPorEmbalaje,
    EmbalajeRequerido = @EmbalajeRequerido,
    EmbalajeDisponible = @EmbalajeDisponible,

    MoldeID = @MoldeID,
    MoldeCodigo = @MoldeCodigo,
    MaquinaSugeridaID = @MaquinaSugeridaID,
    MaquinaSugeridaCodigo = @MaquinaSugeridaCodigo,
    MaquinaSugeridaNombre = @MaquinaSugeridaNombre,
    ObjetivoHora = @ObjetivoHora,
    HorasNecesarias = @HorasNecesarias,
    FechaInicioSugerida = @FechaInicioSugerida,
    FechaFinEstimada = @FechaFinEstimada,
    DaTiempo = @DaTiempo,
    MensajeCapacidad = @MensajeCapacidad,
    EstatusID = @EstatusID,
    UsuarioModificacionID = @UsuarioModificacionID,
    FechaModificacion = GETDATE()
WHERE ReleaseDetalleID = @ReleaseDetalleID;";

            await using var cmd = new SqlCommand(sql, cn, tx);

            AgregarParametrosCalculoDetalle(cmd, d, usuarioId);
            // RELEASE_UPDATE_PARAM_FIX_V1_1
            cmd.Parameters.Add("@NumeroParte", SqlDbType.NVarChar, 120).Value =
                (object?)d.NumeroParte ?? DBNull.Value;

            cmd.Parameters.Add("@ReferenciaSAP", SqlDbType.NVarChar, 150).Value =
                (object?)d.ReferenciaSAP ?? DBNull.Value;

            cmd.Parameters.Add("@DesignacionDescripcionSAP", SqlDbType.NVarChar, 300).Value =
                (object?)d.DesignacionDescripcionSAP ?? DBNull.Value;

            cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value = d.ReleaseDetalleID!.Value;

            await cmd.ExecuteNonQueryAsync();
        }

        private async Task ActualizarEstatusReleaseAsync(
            int releaseId,
            int estatusId,
            int usuarioId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
UPDATE dbo.Planeacion_Releases
SET
    EstatusID = @EstatusID,
    UsuarioModificacionID = @UsuarioModificacionID,
    FechaModificacion = GETDATE()
WHERE ReleaseID = @ReleaseID;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@EstatusID", SqlDbType.Int).Value = estatusId;
            cmd.Parameters.Add("@UsuarioModificacionID", SqlDbType.Int).Value = usuarioId;
            cmd.Parameters.Add("@ReleaseID", SqlDbType.Int).Value = releaseId;

            await cmd.ExecuteNonQueryAsync();
        }

        private static void AgregarParametrosDetalle(
            SqlCommand cmd,
            int releaseId,
            PlaneacionReleaseDetalleCrearVm d,
            int usuarioId)
        {
            cmd.Parameters.Add("@ReleaseID", SqlDbType.Int).Value = releaseId;
            cmd.Parameters.Add("@Renglon", SqlDbType.Int).Value = d.Renglon;
            cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value = (object?)d.ParteID ?? DBNull.Value;
            cmd.Parameters.Add("@NumeroParte", SqlDbType.NVarChar, 120).Value = (object?)d.NumeroParte ?? DBNull.Value;
            cmd.Parameters.Add("@ReferenciaSAP", SqlDbType.NVarChar, 150).Value = (object?)d.ReferenciaSAP ?? DBNull.Value;
            cmd.Parameters.Add("@DesignacionDescripcionSAP", SqlDbType.NVarChar, 300).Value = (object?)d.DesignacionDescripcionSAP ?? DBNull.Value;
            cmd.Parameters.Add("@FechaRequerida", SqlDbType.Date).Value = d.FechaRequerida!.Value.Date;
            cmd.Parameters.Add("@CantidadRequerida", SqlDbType.Int).Value = d.CantidadRequerida;

            AgregarParametrosCalculoDetalle(cmd, d, usuarioId);
        }

        private static void AgregarParametrosCalculoDetalle(
            SqlCommand cmd,
            PlaneacionReleaseDetalleCrearVm d,
            int usuarioId)
        {
            cmd.Parameters.Add("@PTDisponibleAlCalcular", SqlDbType.Int).Value = (object?)d.PTDisponibleAlCalcular ?? DBNull.Value;
            cmd.Parameters.Add("@ProduccionProgramadaPendiente", SqlDbType.Int).Value = (object?)d.ProduccionProgramadaPendiente ?? DBNull.Value;
            cmd.Parameters.Add("@PiezasDesdePT", SqlDbType.Int).Value = (object?)d.PiezasDesdePT ?? DBNull.Value;
            cmd.Parameters.Add("@PiezasAProducir", SqlDbType.Int).Value = (object?)d.PiezasAProducir ?? DBNull.Value;

            cmd.Parameters.Add("@MaterialID", SqlDbType.Int).Value = (object?)d.MaterialID ?? DBNull.Value;
            cmd.Parameters.Add("@MaterialCodigo", SqlDbType.NVarChar, 100).Value = (object?)d.MaterialCodigo ?? DBNull.Value;
            cmd.Parameters.Add("@MaterialDescripcion", SqlDbType.NVarChar, 250).Value = (object?)d.MaterialDescripcion ?? DBNull.Value;

            AddDecimal(cmd, "@PesoBrutoPieza", d.PesoBrutoPieza, 18, 6);
            AddDecimal(cmd, "@MPRequeridaKg", d.MPRequeridaKg, 18, 4);
            AddDecimal(cmd, "@MPDisponibleKg", d.MPDisponibleKg, 18, 4);

            cmd.Parameters.Add("@EmbalajeCodigo", SqlDbType.NVarChar, 100).Value = (object?)d.EmbalajeCodigo ?? DBNull.Value;
            cmd.Parameters.Add("@EmbalajeDescripcion", SqlDbType.NVarChar, 250).Value = (object?)d.EmbalajeDescripcion ?? DBNull.Value;

            AddDecimal(cmd, "@PiezasPorEmbalaje", d.PiezasPorEmbalaje, 18, 4);
            AddDecimal(cmd, "@EmbalajeRequerido", d.EmbalajeRequerido, 18, 4);
            AddDecimal(cmd, "@EmbalajeDisponible", d.EmbalajeDisponible, 18, 4);

            cmd.Parameters.Add("@MoldeID", SqlDbType.Int).Value = (object?)d.MoldeID ?? DBNull.Value;
            cmd.Parameters.Add("@MoldeCodigo", SqlDbType.NVarChar, 100).Value = (object?)d.MoldeCodigo ?? DBNull.Value;

            cmd.Parameters.Add("@MaquinaSugeridaID", SqlDbType.Int).Value = (object?)d.MaquinaSugeridaID ?? DBNull.Value;
            cmd.Parameters.Add("@MaquinaSugeridaCodigo", SqlDbType.NVarChar, 100).Value = (object?)d.MaquinaSugeridaCodigo ?? DBNull.Value;
            cmd.Parameters.Add("@MaquinaSugeridaNombre", SqlDbType.NVarChar, 200).Value = (object?)d.MaquinaSugeridaNombre ?? DBNull.Value;

            cmd.Parameters.Add("@ObjetivoHora", SqlDbType.Int).Value = (object?)d.ObjetivoHora ?? DBNull.Value;

            AddDecimal(cmd, "@HorasNecesarias", d.HorasNecesarias, 18, 2);

            cmd.Parameters.Add("@FechaInicioSugerida", SqlDbType.DateTime).Value = (object?)d.FechaInicioSugerida ?? DBNull.Value;
            cmd.Parameters.Add("@FechaFinEstimada", SqlDbType.DateTime).Value = (object?)d.FechaFinEstimada ?? DBNull.Value;
            cmd.Parameters.Add("@DaTiempo", SqlDbType.Bit).Value = (object?)d.DaTiempo ?? DBNull.Value;
            cmd.Parameters.Add("@MensajeCapacidad", SqlDbType.NVarChar, 500).Value = (object?)d.MensajeCapacidad ?? DBNull.Value;

            cmd.Parameters.Add("@EstatusID", SqlDbType.Int).Value = d.EstatusID;
            cmd.Parameters.Add("@UsuarioCreacionID", SqlDbType.Int).Value = usuarioId;
            cmd.Parameters.Add("@UsuarioModificacionID", SqlDbType.Int).Value = usuarioId;
        }

        private async Task CargarCatalogosAsync(PlaneacionReleaseCrearVm vm)
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            vm.Clientes = await CargarSelectAsync(
                cn,
                "SELECT ClienteID AS Id, Nombre AS Texto FROM dbo.ERP_Clientes WHERE Activo = 1 ORDER BY Nombre;"
            );

            vm.Partes = await CargarSelectAsync(
                cn,
                @"SELECT 
                    ParteID AS Id,
                    NumeroParte + ' | ' + ISNULL(NULLIF(ReferenciaSAP, ''), NumeroParte) + ' | ' + ISNULL(NULLIF(Designacion, ''), Descripcion) AS Texto
                  FROM dbo.ERP_Partes
                  WHERE Activo = 1
                  ORDER BY NumeroParte;"
            );
        }

        private static async Task<List<SelectListItem>> CargarSelectAsync(SqlConnection cn, string sql)
        {
            var lista = new List<SelectListItem>();

            await using var cmd = new SqlCommand(sql, cn);
            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                lista.Add(new SelectListItem
                {
                    Value = rd["Id"].ToString(),
                    Text = rd["Texto"].ToString()
                });
            }

            return lista;
        }

        private async Task<string?> ObtenerClienteNombreAsync(int clienteId, SqlConnection cn, SqlTransaction tx)
        {
            const string sql = "SELECT Nombre FROM dbo.ERP_Clientes WHERE ClienteID = @ClienteID;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@ClienteID", SqlDbType.Int).Value = clienteId;

            var result = await cmd.ExecuteScalarAsync();

            return result == null || result == DBNull.Value ? null : result.ToString();
        }

        private async Task<string> GenerarFolioReleaseSugeridoAsync()
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            return await GenerarFolioReleaseAsync(cn, null);
        }

        private async Task<string> GenerarFolioReleaseAsync(SqlConnection cn, SqlTransaction? tx)
        {
            var anio = DateTime.Today.Year;

            const string sql = @"
SELECT ISNULL(MAX(ReleaseID), 0) + 1
FROM dbo.Planeacion_Releases;";

            await using var cmd = tx == null
                ? new SqlCommand(sql, cn)
                : new SqlCommand(sql, cn, tx);

            var consecutivo = Convert.ToInt32(await cmd.ExecuteScalarAsync());

            return $"REL-{consecutivo:000000}/{anio}";
        }

        private int ObtenerUsuarioID()
        {
            return HttpContext.Session.GetInt32("UsuarioID") ?? 0;
        }

        private static void AddDecimal(
            SqlCommand cmd,
            string name,
            decimal? value,
            byte precision,
            byte scale)
        {
            var p = cmd.Parameters.Add(name, SqlDbType.Decimal);
            p.Precision = precision;
            p.Scale = scale;
            p.Value = value.HasValue ? value.Value : DBNull.Value;
        }


        [HttpGet]
        public async Task<IActionResult> Calculadora(
    int? clienteId,
    int? parteId,
    DateTime? fechaDesde,
    DateTime? fechaHasta,
    bool soloPendientes = false,
    bool soloSinCapacidad = false,
    bool soloSinMP = false)
        {
            var vm = new PlaneacionNecesidadFiltroVm
            {
                ClienteID = clienteId,
                ParteID = parteId,
                FechaDesde = fechaDesde,
                FechaHasta = fechaHasta,
                SoloPendientes = soloPendientes,
                SoloSinCapacidad = soloSinCapacidad,
                SoloSinMP = soloSinMP
            };

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            vm.Clientes = await CargarSelectAsync(
                cn,
                "SELECT ClienteID AS Id, Nombre AS Texto FROM dbo.ERP_Clientes WHERE Activo = 1 ORDER BY Nombre;"
            );

            vm.Partes = await CargarSelectAsync(
                cn,
                @"SELECT 
            ParteID AS Id,
            NumeroParte + ' | ' + ISNULL(NULLIF(ReferenciaSAP, ''), NumeroParte) + ' | ' + ISNULL(NULLIF(Designacion, ''), Descripcion) AS Texto
          FROM dbo.ERP_Partes
          WHERE Activo = 1
          ORDER BY NumeroParte;"
            );

            var sql = @"
SELECT
    r.ReleaseID,
    r.FolioRelease,
    r.ClienteID,
    ISNULL(c.Nombre, r.ClienteNombre) AS ClienteNombre,
    r.FechaRecepcion,

    d.ReleaseDetalleID,
    d.Renglon,
    d.ParteID,
    d.NumeroParte,
    d.ReferenciaSAP,
    d.DesignacionDescripcionSAP,
    d.FechaRequerida,
    d.CantidadRequerida,

    d.PTDisponibleAlCalcular,
    d.ProduccionProgramadaPendiente,
    d.PiezasDesdePT,
    d.PiezasAProducir,

    d.MaterialID,
    d.MaterialCodigo,
    d.MaterialDescripcion,
    d.MPRequeridaKg,
    d.MPDisponibleKg,

    d.EmbalajeCodigo,
    d.EmbalajeDescripcion,
    d.EmbalajeRequerido,
    d.EmbalajeDisponible,

    d.MaquinaSugeridaID,
    d.MaquinaSugeridaCodigo,
    d.MaquinaSugeridaNombre,

    d.MoldeID,
    d.MoldeCodigo,

    d.ObjetivoHora,
    d.HorasNecesarias,
    d.FechaInicioSugerida,
    d.FechaFinEstimada,
    d.DaTiempo,
    d.MensajeCapacidad,
d.ProgramaProduccionID,
d.EstatusID
FROM dbo.Planeacion_ReleaseDetalle d
INNER JOIN dbo.Planeacion_Releases r
    ON r.ReleaseID = d.ReleaseID
LEFT JOIN dbo.ERP_Clientes c
    ON c.ClienteID = r.ClienteID
WHERE r.Activo = 1
  AND d.Activo = 1
  AND r.EstatusID NOT IN (9, 99)
  AND (@ClienteID IS NULL OR r.ClienteID = @ClienteID)
  AND (@ParteID IS NULL OR d.ParteID = @ParteID)
  AND (@FechaDesde IS NULL OR d.FechaRequerida >= @FechaDesde)
  AND (@FechaHasta IS NULL OR d.FechaRequerida <= @FechaHasta)
  AND (
      @SoloPendientes = 0
      OR (
            ISNULL(d.PiezasAProducir, 0) > 0
            AND d.ProgramaProduccionID IS NULL
         )
    )
  AND (
        @SoloSinCapacidad = 0
        OR ISNULL(d.DaTiempo, 0) = 0
      )
  AND (
        @SoloSinMP = 0
        OR ISNULL(d.MPDisponibleKg, 0) < ISNULL(d.MPRequeridaKg, 0)
      )
ORDER BY
    d.FechaRequerida,
    ISNULL(c.Nombre, r.ClienteNombre),
    d.ReferenciaSAP,
    d.Renglon;";

            await using var cmd = new SqlCommand(sql, cn);

            cmd.Parameters.Add("@ClienteID", SqlDbType.Int).Value =
                (object?)clienteId ?? DBNull.Value;

            cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value =
                (object?)parteId ?? DBNull.Value;

            cmd.Parameters.Add("@FechaDesde", SqlDbType.Date).Value =
                (object?)fechaDesde?.Date ?? DBNull.Value;

            cmd.Parameters.Add("@FechaHasta", SqlDbType.Date).Value =
                (object?)fechaHasta?.Date ?? DBNull.Value;

            cmd.Parameters.Add("@SoloPendientes", SqlDbType.Bit).Value = soloPendientes;
            cmd.Parameters.Add("@SoloSinCapacidad", SqlDbType.Bit).Value = soloSinCapacidad;
            cmd.Parameters.Add("@SoloSinMP", SqlDbType.Bit).Value = soloSinMP;

            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                vm.Necesidades.Add(new PlaneacionNecesidadVm
                {
                    ReleaseID = Convert.ToInt32(rd["ReleaseID"]),
                    ReleaseDetalleID = Convert.ToInt32(rd["ReleaseDetalleID"]),

                    FolioRelease = rd["FolioRelease"] as string,

                    ClienteID = rd["ClienteID"] == DBNull.Value ? null : Convert.ToInt32(rd["ClienteID"]),
                    ClienteNombre = rd["ClienteNombre"] as string,

                    FechaRecepcion = Convert.ToDateTime(rd["FechaRecepcion"]),
                    Renglon = Convert.ToInt32(rd["Renglon"]),

                    ParteID = rd["ParteID"] == DBNull.Value ? null : Convert.ToInt32(rd["ParteID"]),
                    NumeroParte = rd["NumeroParte"] as string,
                    ReferenciaSAP = rd["ReferenciaSAP"] as string,
                    DesignacionDescripcionSAP = rd["DesignacionDescripcionSAP"] as string,

                    FechaRequerida = Convert.ToDateTime(rd["FechaRequerida"]),
                    CantidadRequerida = Convert.ToInt32(rd["CantidadRequerida"]),

                    PTDisponibleAlCalcular = rd["PTDisponibleAlCalcular"] == DBNull.Value ? null : Convert.ToInt32(rd["PTDisponibleAlCalcular"]),
                    ProduccionProgramadaPendiente = rd["ProduccionProgramadaPendiente"] == DBNull.Value ? null : Convert.ToInt32(rd["ProduccionProgramadaPendiente"]),
                    PiezasDesdePT = rd["PiezasDesdePT"] == DBNull.Value ? null : Convert.ToInt32(rd["PiezasDesdePT"]),
                    PiezasAProducir = rd["PiezasAProducir"] == DBNull.Value ? null : Convert.ToInt32(rd["PiezasAProducir"]),

                    MaterialID = rd["MaterialID"] == DBNull.Value ? null : Convert.ToInt32(rd["MaterialID"]),
                    MaterialCodigo = rd["MaterialCodigo"] as string,
                    MaterialDescripcion = rd["MaterialDescripcion"] as string,

                    MPRequeridaKg = rd["MPRequeridaKg"] == DBNull.Value ? null : Convert.ToDecimal(rd["MPRequeridaKg"]),
                    MPDisponibleKg = rd["MPDisponibleKg"] == DBNull.Value ? null : Convert.ToDecimal(rd["MPDisponibleKg"]),

                    EmbalajeCodigo = rd["EmbalajeCodigo"] as string,
                    EmbalajeDescripcion = rd["EmbalajeDescripcion"] as string,
                    EmbalajeRequerido = rd["EmbalajeRequerido"] == DBNull.Value ? null : Convert.ToDecimal(rd["EmbalajeRequerido"]),
                    EmbalajeDisponible = rd["EmbalajeDisponible"] == DBNull.Value ? null : Convert.ToDecimal(rd["EmbalajeDisponible"]),

                    MaquinaSugeridaID = rd["MaquinaSugeridaID"] == DBNull.Value ? null : Convert.ToInt32(rd["MaquinaSugeridaID"]),
                    MaquinaSugeridaCodigo = rd["MaquinaSugeridaCodigo"] as string,
                    MaquinaSugeridaNombre = rd["MaquinaSugeridaNombre"] as string,

                    MoldeID = rd["MoldeID"] == DBNull.Value ? null : Convert.ToInt32(rd["MoldeID"]),
                    MoldeCodigo = rd["MoldeCodigo"] as string,

                    ObjetivoHora = rd["ObjetivoHora"] == DBNull.Value ? null : Convert.ToInt32(rd["ObjetivoHora"]),
                    HorasNecesarias = rd["HorasNecesarias"] == DBNull.Value ? null : Convert.ToDecimal(rd["HorasNecesarias"]),

                    FechaInicioSugerida = rd["FechaInicioSugerida"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaInicioSugerida"]),
                    FechaFinEstimada = rd["FechaFinEstimada"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaFinEstimada"]),

                    DaTiempo = rd["DaTiempo"] == DBNull.Value ? null : Convert.ToBoolean(rd["DaTiempo"]),
                    MensajeCapacidad = rd["MensajeCapacidad"] as string,
                    ProgramaProduccionID = rd["ProgramaProduccionID"] == DBNull.Value ? null : Convert.ToInt32(rd["ProgramaProduccionID"]),
                    EstatusID = Convert.ToInt32(rd["EstatusID"])
                });
            }

            vm.ResumenPeriodos = ConstruirResumenPeriodos(vm.Necesidades);

            return View(vm);
        }



        [HttpGet]
        public async Task<IActionResult> ObtenerParteInfoRelease(int parteId)
        {
            if (parteId <= 0)
            {
                return BadRequest(new { ok = false, mensaje = "La parte es obligatoria." });
            }

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            const string sql = @"
SELECT
    p.ParteID,
    p.NumeroParte,
    p.ReferenciaSAP,
    p.Descripcion,
    p.Designacion,

    t.MaterialID,
    t.MaterialCodigo,
    t.MaterialDescripcion,
    t.PesoBrutoPieza,
    t.PiezasPorEmbalaje,
    t.EmbalajeCodigo,
    t.EmbalajeDescripcion,
    t.ObjetivoHora,
    t.MoldePrincipalID,
    mol.CodigoMolde AS MoldeCodigo,
    t.MaquinaPrincipalID,
    maq.Codigo AS MaquinaCodigo,
    maq.Nombre AS MaquinaNombre
FROM dbo.ERP_Partes p
LEFT JOIN dbo.ERP_ParteDatosTecnicos t
    ON t.ParteID = p.ParteID
   AND t.Activo = 1
LEFT JOIN dbo.ERP_Moldes mol
    ON mol.MoldeID = t.MoldePrincipalID
LEFT JOIN dbo.ERP_Maquinas maq
    ON maq.MaquinaID = t.MaquinaPrincipalID
WHERE p.ParteID = @ParteID
  AND p.Activo = 1;";

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value = parteId;

            await using var rd = await cmd.ExecuteReaderAsync();

            if (!await rd.ReadAsync())
            {
                return Json(new { ok = false, mensaje = "No se encontró la parte seleccionada." });
            }

            var numeroParte = rd["NumeroParte"] as string;
            var referenciaSap = rd["ReferenciaSAP"] as string;
            var descripcion = rd["Descripcion"] as string;
            var designacion = rd["Designacion"] as string;

            return Json(new
            {
                ok = true,

                parteID = Convert.ToInt32(rd["ParteID"]),
                numeroParte,

                referenciaSAP = !string.IsNullOrWhiteSpace(referenciaSap)
                    ? referenciaSap
                    : numeroParte,

                designacionDescripcionSAP = !string.IsNullOrWhiteSpace(designacion)
                    ? designacion
                    : descripcion,

                materialID = rd["MaterialID"] == DBNull.Value ? null : rd["MaterialID"],
                materialCodigo = rd["MaterialCodigo"] == DBNull.Value ? null : rd["MaterialCodigo"],
                materialDescripcion = rd["MaterialDescripcion"] == DBNull.Value ? null : rd["MaterialDescripcion"],

                pesoBrutoPieza = rd["PesoBrutoPieza"] == DBNull.Value ? null : rd["PesoBrutoPieza"],
                piezasPorEmbalaje = rd["PiezasPorEmbalaje"] == DBNull.Value ? null : rd["PiezasPorEmbalaje"],

                embalajeCodigo = rd["EmbalajeCodigo"] == DBNull.Value ? null : rd["EmbalajeCodigo"],
                embalajeDescripcion = rd["EmbalajeDescripcion"] == DBNull.Value ? null : rd["EmbalajeDescripcion"],

                objetivoHora = rd["ObjetivoHora"] == DBNull.Value ? null : rd["ObjetivoHora"],

                moldeID = rd["MoldePrincipalID"] == DBNull.Value ? null : rd["MoldePrincipalID"],
                moldeCodigo = rd["MoldeCodigo"] == DBNull.Value ? null : rd["MoldeCodigo"],

                maquinaID = rd["MaquinaPrincipalID"] == DBNull.Value ? null : rd["MaquinaPrincipalID"],
                maquinaCodigo = rd["MaquinaCodigo"] == DBNull.Value ? null : rd["MaquinaCodigo"],
                maquinaNombre = rd["MaquinaNombre"] == DBNull.Value ? null : rd["MaquinaNombre"]
            });
        }


        private async Task CompletarRenglonDesdeParteAsync(
    PlaneacionReleaseRenglonCrearVm r,
    SqlConnection cn,
    SqlTransaction tx)
        {
            if (!r.ParteID.HasValue)
                return;

            const string sql = @"
SELECT
    p.NumeroParte,
    p.ReferenciaSAP,
    p.Descripcion,
    p.Designacion
FROM dbo.ERP_Partes p
WHERE p.ParteID = @ParteID
  AND p.Activo = 1;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value = r.ParteID.Value;

            await using var rd = await cmd.ExecuteReaderAsync();

            if (!await rd.ReadAsync())
                return;

            var numeroParte = rd["NumeroParte"] as string;
            var referenciaSap = rd["ReferenciaSAP"] as string;
            var descripcion = rd["Descripcion"] as string;
            var designacion = rd["Designacion"] as string;

            if (string.IsNullOrWhiteSpace(r.NumeroParte))
                r.NumeroParte = numeroParte;

            if (string.IsNullOrWhiteSpace(r.ReferenciaSAP))
                r.ReferenciaSAP = !string.IsNullOrWhiteSpace(referenciaSap)
                    ? referenciaSap
                    : numeroParte;

            if (string.IsNullOrWhiteSpace(r.DesignacionDescripcionSAP))
                r.DesignacionDescripcionSAP = !string.IsNullOrWhiteSpace(designacion)
                    ? designacion
                    : descripcion;
        }


        private static PlaneacionReleaseDetalleCrearVm CrearDetalleDesdeRenglonEntrega(
    PlaneacionReleaseRenglonCrearVm renglon,
    PlaneacionReleaseEntregaCrearVm entrega)
        {
            return new PlaneacionReleaseDetalleCrearVm
            {
                Renglon = renglon.Renglon,

                ParteID = renglon.ParteID,
                NumeroParte = renglon.NumeroParte,
                ReferenciaSAP = renglon.ReferenciaSAP,
                DesignacionDescripcionSAP = renglon.DesignacionDescripcionSAP,

                FechaCarga = entrega.FechaCarga,
                FechaRequerida = entrega.FechaRequerida,
                CantidadRequerida = entrega.CantidadRequerida,

                EstatusID = PlaneacionReleaseEstatus.Capturado
            };
        }

        private async Task<int> InsertarReleaseRenglonAsync(
    int releaseId,
    PlaneacionReleaseRenglonCrearVm r,
    int usuarioId,
    SqlConnection cn,
    SqlTransaction tx)
        {
            const string sql = @"
INSERT INTO dbo.Planeacion_ReleaseRenglones
(
    ReleaseID,
    Renglon,
    ParteID,
    NumeroParte,
    ReferenciaSAP,
    DesignacionDescripcionSAP,
    UnidadMedidaCliente,
    ContratoCliente,
    Observaciones,
    UsuarioCreacionID,
    FechaCreacion,
    Activo
)
OUTPUT INSERTED.ReleaseRenglonID
VALUES
(
    @ReleaseID,
    @Renglon,
    @ParteID,
    @NumeroParte,
    @ReferenciaSAP,
    @DesignacionDescripcionSAP,
    @UnidadMedidaCliente,
    @ContratoCliente,
    @Observaciones,
    @UsuarioCreacionID,
    GETDATE(),
    1
);";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@ReleaseID", SqlDbType.Int).Value = releaseId;
            cmd.Parameters.Add("@Renglon", SqlDbType.Int).Value = r.Renglon;
            cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value = (object?)r.ParteID ?? DBNull.Value;
            cmd.Parameters.Add("@NumeroParte", SqlDbType.NVarChar, 120).Value = (object?)r.NumeroParte ?? DBNull.Value;
            cmd.Parameters.Add("@ReferenciaSAP", SqlDbType.NVarChar, 150).Value = (object?)r.ReferenciaSAP ?? DBNull.Value;
            cmd.Parameters.Add("@DesignacionDescripcionSAP", SqlDbType.NVarChar, 300).Value = (object?)r.DesignacionDescripcionSAP ?? DBNull.Value;
            cmd.Parameters.Add("@UnidadMedidaCliente", SqlDbType.NVarChar, 30).Value = (object?)r.UnidadMedidaCliente ?? DBNull.Value;
            cmd.Parameters.Add("@ContratoCliente", SqlDbType.NVarChar, 100).Value = (object?)r.ContratoCliente ?? DBNull.Value;
            cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 500).Value = (object?)r.Observaciones ?? DBNull.Value;
            cmd.Parameters.Add("@UsuarioCreacionID", SqlDbType.Int).Value = usuarioId;

            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }





        [HttpGet]
        public async Task<IActionResult> ObtenerPartesPorCliente(int clienteId)
        {
            if (clienteId <= 0)
            {
                return BadRequest(new
                {
                    ok = false,
                    mensaje = "El cliente es obligatorio."
                });
            }

            var lista = new List<object>();

            const string sql = @"
SELECT
    p.ParteID,
    p.NumeroParte,
    p.ReferenciaSAP,
    p.Descripcion,
    p.Designacion
FROM dbo.ERP_Partes p
WHERE p.Activo = 1
  AND p.ClienteID = @ClienteID
ORDER BY
    p.NumeroParte,
    p.ReferenciaSAP;";

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@ClienteID", SqlDbType.Int).Value = clienteId;

            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                var parteId = Convert.ToInt32(rd["ParteID"]);
                var numeroParte = rd["NumeroParte"] as string ?? "";
                var referencia = rd["ReferenciaSAP"] as string;
                var descripcion = rd["Descripcion"] as string;
                var designacion = rd["Designacion"] as string;

                var texto =
                    numeroParte
                    + " | "
                    + (!string.IsNullOrWhiteSpace(referencia) ? referencia : numeroParte)
                    + " | "
                    + (!string.IsNullOrWhiteSpace(designacion) ? designacion : descripcion);

                lista.Add(new
                {
                    value = parteId,
                    text = texto
                });
            }

            return Json(new
            {
                ok = true,
                partes = lista
            });
        }

        // VERITAS_IMPORT_HELPERS_V1_2
        private static async Task<int?> ObtenerClienteVeritasParaImportacionAsync(
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
SELECT TOP (1) ClienteID
FROM dbo.ERP_Clientes
WHERE Activo = 1
  AND UPPER(Nombre) LIKE '%VERITAS%'
ORDER BY
    CASE
        WHEN UPPER(LTRIM(RTRIM(Nombre))) IN ('VERITAS', 'AUTOMOTIVE VERITAS DE MEXICO') THEN 0
        ELSE 1
    END,
    ClienteID;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            var result = await cmd.ExecuteScalarAsync();
            return result == null || result == DBNull.Value ? null : Convert.ToInt32(result);
        }

        private static async Task<int?> ObtenerParteClienteParaImportacionAsync(
            string referencia,
            int clienteId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
DECLARE @Normalizada NVARCHAR(150) = UPPER(
    REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(@Referencia)), '-', ''), '.', ''), ' ', ''), '/', '')
);

SELECT TOP (1) ParteID
FROM dbo.ERP_Partes
WHERE Activo = 1
  AND ClienteID = @ClienteID
  AND
  (
        UPPER(LTRIM(RTRIM(ISNULL(NumeroParte, '')))) = UPPER(LTRIM(RTRIM(@Referencia)))
     OR UPPER(LTRIM(RTRIM(ISNULL(ReferenciaSAP, '')))) = UPPER(LTRIM(RTRIM(@Referencia)))
     OR UPPER(REPLACE(REPLACE(REPLACE(REPLACE(ISNULL(NumeroParte, ''), '-', ''), '.', ''), ' ', ''), '/', '')) = @Normalizada
     OR UPPER(REPLACE(REPLACE(REPLACE(REPLACE(ISNULL(ReferenciaSAP, ''), '-', ''), '.', ''), ' ', ''), '/', '')) = @Normalizada
  )
ORDER BY
    CASE
        WHEN UPPER(LTRIM(RTRIM(ISNULL(NumeroParte, '')))) = UPPER(LTRIM(RTRIM(@Referencia))) THEN 0
        WHEN UPPER(LTRIM(RTRIM(ISNULL(ReferenciaSAP, '')))) = UPPER(LTRIM(RTRIM(@Referencia))) THEN 1
        ELSE 2
    END,
    ParteID;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@Referencia", SqlDbType.NVarChar, 150).Value = referencia.Trim();
            cmd.Parameters.Add("@ClienteID", SqlDbType.Int).Value = clienteId;

            var result = await cmd.ExecuteScalarAsync();
            return result == null || result == DBNull.Value ? null : Convert.ToInt32(result);
        }

        private static async Task<bool> ExisteDocumentoImportadoAsync(
            string plantilla,
            string sha256,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
SELECT TOP (1) 1
FROM dbo.Planeacion_Releases
WHERE Activo = 1
  AND PlantillaImportacion = @Plantilla
  AND CHARINDEX('SHA256:' + @Sha256, ISNULL(Observaciones, '')) > 0;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@Plantilla", SqlDbType.NVarChar, 100).Value = plantilla;
            cmd.Parameters.Add("@Sha256", SqlDbType.NVarChar, 64).Value = sha256;

            var result = await cmd.ExecuteScalarAsync();
            return result != null && result != DBNull.Value;
        }

        private static async Task<DateTime?> ObtenerFechaVersionVeritasActivaAsync(
            int clienteId,
            string contractNumber,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
SELECT TOP (1) r.VersionRelease
FROM dbo.Planeacion_Releases r
WHERE r.Activo = 1
  AND r.ClienteID = @ClienteID
  AND r.PlantillaImportacion = 'VERITAS_SCHEDULE'
  AND r.FolioCliente = @Contrato
  AND r.EstatusID NOT IN (@Cerrado, @Cancelado)
ORDER BY ISNULL(r.FechaModificacion, r.FechaCreacion) DESC, r.ReleaseID DESC;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@ClienteID", SqlDbType.Int).Value = clienteId;
            cmd.Parameters.Add("@Contrato", SqlDbType.NVarChar, 100).Value = contractNumber;
            cmd.Parameters.Add("@Cerrado", SqlDbType.Int).Value = PlaneacionReleaseEstatus.Cerrado;
            cmd.Parameters.Add("@Cancelado", SqlDbType.Int).Value = PlaneacionReleaseEstatus.Cancelado;

            var value = await cmd.ExecuteScalarAsync();
            var version = value == null || value == DBNull.Value ? null : value.ToString();
            if (string.IsNullOrWhiteSpace(version))
                return null;

            var match = Regex.Match(version, @"(?<date>\d{2}\.\d{2}\.\d{4})");
            if (!match.Success)
                return null;

            return DateTime.TryParseExact(
                match.Groups["date"].Value,
                "dd.MM.yyyy",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date)
                    ? date.Date
                    : null;
        }

        private static async Task<int> CerrarVersionesVeritasAnterioresAsync(
            int clienteId,
            string contractNumber,
            int usuarioId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
UPDATE dbo.Planeacion_Releases
SET
    EstatusID = @Cerrado,
    UsuarioModificacionID = @UsuarioID,
    FechaModificacion = GETDATE()
WHERE Activo = 1
  AND ClienteID = @ClienteID
  AND PlantillaImportacion = 'VERITAS_SCHEDULE'
  AND FolioCliente = @Contrato
  AND EstatusID NOT IN (@Cerrado, @Cancelado);

SELECT @@ROWCOUNT;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@Cerrado", SqlDbType.Int).Value = PlaneacionReleaseEstatus.Cerrado;
            cmd.Parameters.Add("@Cancelado", SqlDbType.Int).Value = PlaneacionReleaseEstatus.Cancelado;
            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
            cmd.Parameters.Add("@ClienteID", SqlDbType.Int).Value = clienteId;
            cmd.Parameters.Add("@Contrato", SqlDbType.NVarChar, 100).Value = contractNumber;

            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }
        // HUF_MULTI_IMPORT_HELPERS_V1
        private static async Task<int?> ObtenerClienteHufParaImportacionAsync(
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
SELECT TOP (1) ClienteID
FROM dbo.ERP_Clientes
WHERE Activo = 1
  AND UPPER(Nombre) LIKE '%HUF%'
ORDER BY
    CASE WHEN UPPER(LTRIM(RTRIM(Nombre))) IN ('HUF', 'HUF MEXICO') THEN 0 ELSE 1 END,
    ClienteID;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            var result = await cmd.ExecuteScalarAsync();
            return result == null || result == DBNull.Value ? null : Convert.ToInt32(result);
        }

        private static async Task<int?> ObtenerParteHufParaImportacionAsync(
            string referencia,
            int clienteId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
DECLARE @Normalizada NVARCHAR(150) = UPPER(
    REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(@Referencia)), '-', ''), '.', ''), ' ', ''), '/', '')
);

SELECT TOP (1) ParteID
FROM dbo.ERP_Partes
WHERE Activo = 1
  AND ClienteID = @ClienteID
  AND
  (
        UPPER(LTRIM(RTRIM(ISNULL(NumeroParte, '')))) = UPPER(LTRIM(RTRIM(@Referencia)))
     OR UPPER(LTRIM(RTRIM(ISNULL(ReferenciaSAP, '')))) = UPPER(LTRIM(RTRIM(@Referencia)))
     OR UPPER(REPLACE(REPLACE(REPLACE(REPLACE(ISNULL(NumeroParte, ''), '-', ''), '.', ''), ' ', ''), '/', '')) = @Normalizada
     OR UPPER(REPLACE(REPLACE(REPLACE(REPLACE(ISNULL(ReferenciaSAP, ''), '-', ''), '.', ''), ' ', ''), '/', '')) = @Normalizada
  )
ORDER BY
    CASE
        WHEN UPPER(LTRIM(RTRIM(ISNULL(NumeroParte, '')))) = UPPER(LTRIM(RTRIM(@Referencia))) THEN 0
        WHEN UPPER(LTRIM(RTRIM(ISNULL(ReferenciaSAP, '')))) = UPPER(LTRIM(RTRIM(@Referencia))) THEN 1
        ELSE 2
    END,
    ParteID;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@Referencia", SqlDbType.NVarChar, 150).Value = referencia.Trim();
            cmd.Parameters.Add("@ClienteID", SqlDbType.Int).Value = clienteId;

            var result = await cmd.ExecuteScalarAsync();
            return result == null || result == DBNull.Value ? null : Convert.ToInt32(result);
        }

        private static async Task<bool> ExisteDocumentoHufImportadoAsync(
            string sha256,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
SELECT TOP (1) 1
FROM dbo.Planeacion_Releases
WHERE Activo = 1
  AND PlantillaImportacion = 'HUF_SUPPLIER_SCHEDULE'
  AND CHARINDEX('SHA256:' + @Sha256, ISNULL(Observaciones, '')) > 0;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@Sha256", SqlDbType.NVarChar, 64).Value = sha256;
            var result = await cmd.ExecuteScalarAsync();
            return result != null && result != DBNull.Value;
        }

        private static async Task<DateTime?> ObtenerFechaVersionHufActivaAsync(
            int clienteId,
            int parteId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
SELECT TOP (1) r.VersionRelease
FROM dbo.Planeacion_Releases r
WHERE r.Activo = 1
  AND r.ClienteID = @ClienteID
  AND r.PlantillaImportacion = 'HUF_SUPPLIER_SCHEDULE'
  AND r.EstatusID NOT IN (@Cerrado, @Cancelado)
  AND EXISTS
  (
      SELECT 1
      FROM dbo.Planeacion_ReleaseRenglones rr
      WHERE rr.ReleaseID = r.ReleaseID
        AND rr.Activo = 1
        AND rr.ParteID = @ParteID
  )
ORDER BY ISNULL(r.FechaModificacion, r.FechaCreacion) DESC, r.ReleaseID DESC;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@ClienteID", SqlDbType.Int).Value = clienteId;
            cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value = parteId;
            cmd.Parameters.Add("@Cerrado", SqlDbType.Int).Value = PlaneacionReleaseEstatus.Cerrado;
            cmd.Parameters.Add("@Cancelado", SqlDbType.Int).Value = PlaneacionReleaseEstatus.Cancelado;

            var value = await cmd.ExecuteScalarAsync();
            var version = value == null || value == DBNull.Value ? null : value.ToString();
            if (string.IsNullOrWhiteSpace(version))
                return null;

            var match = Regex.Match(version, @"(?<date>\d{2}\.\d{2}\.\d{4})");
            if (!match.Success)
                return null;

            return DateTime.TryParseExact(
                match.Groups["date"].Value,
                "dd.MM.yyyy",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date)
                    ? date.Date
                    : null;
        }

        private static async Task<int> CerrarVersionesHufAnterioresAsync(
            int clienteId,
            int parteId,
            int usuarioId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
UPDATE r
SET
    r.EstatusID = @Cerrado,
    r.UsuarioModificacionID = @UsuarioID,
    r.FechaModificacion = GETDATE()
FROM dbo.Planeacion_Releases r
WHERE r.Activo = 1
  AND r.ClienteID = @ClienteID
  AND r.PlantillaImportacion = 'HUF_SUPPLIER_SCHEDULE'
  AND r.EstatusID NOT IN (@Cerrado, @Cancelado)
  AND EXISTS
  (
      SELECT 1
      FROM dbo.Planeacion_ReleaseRenglones rr
      WHERE rr.ReleaseID = r.ReleaseID
        AND rr.Activo = 1
        AND rr.ParteID = @ParteID
  );

SELECT @@ROWCOUNT;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@Cerrado", SqlDbType.Int).Value = PlaneacionReleaseEstatus.Cerrado;
            cmd.Parameters.Add("@Cancelado", SqlDbType.Int).Value = PlaneacionReleaseEstatus.Cancelado;
            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
            cmd.Parameters.Add("@ClienteID", SqlDbType.Int).Value = clienteId;
            cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value = parteId;

            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }
        private async Task<PlaneacionReleaseCrearVm> LeerReleasePdfPorPlantillaAsync(
            PlaneacionReleaseCrearVm vm)
        {
            using var ms = new MemoryStream();
            await vm.ArchivoRelease!.CopyToAsync(ms);

            var bytes = ms.ToArray();
            var template = ReleasePdfDocumentDetector.Detect(bytes);
            var receptionDate = vm.FechaRecepcion == default
                ? DateTime.Today
                : vm.FechaRecepcion.Date;

            if (template == ReleasePdfTemplate.HufSupplierSchedule)
            {
                var document = HufReleasePdfParser.Parse(bytes, receptionDate);

                vm.ClienteNombre = document.ClienteNombre;
                vm.FolioCliente = document.ScheduleNumber;
                vm.VersionRelease = document.VersionText;
                vm.ImportadoDesdeArchivo = true;
                vm.PlantillaImportacion = "HUF_SUPPLIER_SCHEDULE";

                var clienteId = await ObtenerClienteIdPorNombreAsync("Huf");
                if (clienteId.HasValue)
                    vm.ClienteID = clienteId.Value;

                var parteId = await ObtenerParteIdPorReferenciaAsync(document.PartNumber, vm.ClienteID);
                var categorias = string.Join(", ", document.Deliveries
                    .Select(x => x.Category)
                    .Distinct(StringComparer.OrdinalIgnoreCase));

                vm.Observaciones =
                    $"Documento HUF leido. Orden:{document.OrderNumber}; Parte:{document.PartNumber}; " +
                    $"Categorias:{categorias}; SHA256:{document.Sha256}";

                vm.Renglones = new List<PlaneacionReleaseRenglonCrearVm>
                {
                    new()
                    {
                        Renglon = 1,
                        ParteID = parteId,
                        NumeroParte = document.PartNumber,
                        ReferenciaSAP = document.PartNumber,
                        DesignacionDescripcionSAP = document.PartDescription,
                        UnidadMedidaCliente = document.Uom,
                        ContratoCliente = document.OrderNumber,
                        Observaciones = $"Categorias HUF detectadas: {categorias}.",
                        Entregas = document.Deliveries.Select(x => new PlaneacionReleaseEntregaCrearVm
                        {
                            SecuenciaEntrega = x.Sequence,
                            FechaCarga = x.LoadingDate,
                            FechaRequerida = x.RequiredDate,
                            CantidadRequerida = x.RequiredQuantity
                        }).ToList()
                    }
                };

                return vm;
            }

            if (template == ReleasePdfTemplate.VeritasSchedule)
            {
                var document = VeritasReleasePdfParser.Parse(bytes);

                vm.ClienteNombre = document.ClienteNombre;
                vm.FolioCliente = document.ContractNumber;
                vm.VersionRelease = document.VersionText;
                vm.ImportadoDesdeArchivo = true;
                vm.PlantillaImportacion = "VERITAS_SCHEDULE";

                var clienteId = await ObtenerClienteIdPorNombreAsync("Veritas");
                if (clienteId.HasValue)
                    vm.ClienteID = clienteId.Value;

                vm.Observaciones =
                    $"Documento VERITAS leido. Schedule:{document.ScheduleNumber}; " +
                    $"Contrato:{document.ContractNumber}; Supplier:{document.SupplierNumber}; " +
                    $"SHA256:{document.Sha256}";

                vm.Renglones = new List<PlaneacionReleaseRenglonCrearVm>();

                var groups = document.Deliveries
                    .GroupBy(x => x.PartNumber, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x.Key)
                    .ToList();

                var rowNumber = 1;
                foreach (var group in groups)
                {
                    var first = group.First();
                    var partId = await ObtenerParteIdPorReferenciaAsync(group.Key, vm.ClienteID);

                    vm.Renglones.Add(new PlaneacionReleaseRenglonCrearVm
                    {
                        Renglon = rowNumber,
                        ParteID = partId,
                        NumeroParte = group.Key,
                        ReferenciaSAP = group.Key,
                        DesignacionDescripcionSAP = first.PartDescription,
                        UnidadMedidaCliente = first.Uom,
                        ContratoCliente = document.ContractNumber,
                        Observaciones = $"Schedule VERITAS {document.ScheduleNumber}.",
                        Entregas = group
                            .OrderBy(x => x.RequiredDate)
                            .ThenBy(x => x.ItemNumber)
                            .Select((x, deliveryIndex) => new PlaneacionReleaseEntregaCrearVm
                            {
                                SecuenciaEntrega = deliveryIndex + 1,
                                FechaCarga = null,
                                FechaRequerida = x.RequiredDate,
                                CantidadRequerida = x.RequiredQuantity
                            })
                            .ToList()
                    });

                    rowNumber++;
                }

                return vm;
            }

            throw new InvalidOperationException(
                "No se reconocio la plantilla del PDF. Actualmente se soportan HUF Supplier Schedule Report y VERITAS Schedule.");
        }

        private Task<PlaneacionReleaseCrearVm> LeerReleaseExcelPorPlantillaAsync(
    PlaneacionReleaseCrearVm vm)
        {
            throw new InvalidOperationException("La lectura de Excel se implementará después de terminar la plantilla HUF PDF.");
        }

        private static string ExtraerTextoBasicoPdf(byte[] bytes)
        {
            using var ms = new MemoryStream(bytes);
            using var document = PdfDocument.Open(ms);

            var partes = new List<string>();

            foreach (var page in document.GetPages())
            {
                partes.Add(page.Text);
            }

            return string.Join(Environment.NewLine, partes);
        }


        private async Task<PlaneacionReleaseCrearVm> LeerReleaseHufDesdeTextoAsync(
      PlaneacionReleaseCrearVm vm,
      string texto)
        {
            vm.ClienteNombre = "Huf Mexico";

            vm.FolioCliente = BuscarValor(texto, @"Schedule No:\s*(\S+)");

            var fechaPrint = BuscarValor(texto, @"Print\s+(\d{2}\.\d{2}\.\d{4})");

            if (!string.IsNullOrWhiteSpace(fechaPrint))
            {
                vm.VersionRelease = "Print " + fechaPrint;
            }

            var partNo = BuscarValor(texto, @"Part No:\s*([^\r\n\s]+)");
            var descripcion = BuscarValor(texto, @"Part Description:\s*(.+)");
            var unidad = BuscarValor(texto, @"UOM:\s*(\S+)");
            var contrato = BuscarValor(texto, @"Order Number:\s*(\S+)");

            var clienteId = await ObtenerClienteIdPorNombreAsync("Huf");

            if (clienteId.HasValue)
            {
                vm.ClienteID = clienteId.Value;
            }

            var parteId = await ObtenerParteIdPorReferenciaAsync(partNo, vm.ClienteID);

            var renglon = new PlaneacionReleaseRenglonCrearVm
            {
                Renglon = 1,
                ParteID = parteId,
                NumeroParte = partNo,
                ReferenciaSAP = partNo,
                DesignacionDescripcionSAP = descripcion,
                UnidadMedidaCliente = unidad,
                ContratoCliente = contrato,
                Entregas = new List<PlaneacionReleaseEntregaCrearVm>()
            };

            var entregas = ExtraerEntregasHuf(texto);

            foreach (var entrega in entregas)
            {
                renglon.Entregas.Add(entrega);
            }

            if (!renglon.Entregas.Any())
            {
                throw new InvalidOperationException("No se encontraron entregas en el archivo HUF.");
            }

            vm.Renglones = new List<PlaneacionReleaseRenglonCrearVm>
    {
        renglon
    };

            vm.ImportadoDesdeArchivo = true;
            vm.PlantillaImportacion = "HUF_SUPPLIER_SCHEDULE";

            return vm;
        }

        private static string? BuscarValor(string texto, string patron)
        {
            var match = Regex.Match(texto, patron, RegexOptions.IgnoreCase);

            if (!match.Success)
                return null;

            return match.Groups[1].Value.Trim();
        }

        private static List<PlaneacionReleaseEntregaCrearVm> ExtraerEntregasHuf(string texto)
        {
            var lista = new List<PlaneacionReleaseEntregaCrearVm>();

            var lineas = texto
                .Replace("\r", "\n")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => Regex.Replace(x.Trim(), @"\s+", " "))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            var secuencia = 1;

            foreach (var linea in lineas)
            {
                var fechas = Regex.Matches(linea, @"\d{2}\.\d{2}\.\d{4}")
                    .Select(x => x.Value)
                    .ToList();

                if (fechas.Count < 2)
                    continue;

                var cantidades = Regex.Matches(linea, @"(?<![\d.])\d{1,3}(?:,\d{3})+(?![\d.])|(?<![\d.])\d{1,9}(?![\d.])")
                    .Select(x => x.Value)
                    .ToList();

                if (!cantidades.Any())
                    continue;

                if (!DateTime.TryParseExact(
                        fechas[0],
                        "dd.MM.yyyy",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out var fechaCarga))
                {
                    continue;
                }

                if (!DateTime.TryParseExact(
                        fechas[1],
                        "dd.MM.yyyy",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out var fechaRequerida))
                {
                    continue;
                }

                var cantidadRaw = cantidades
                    .FirstOrDefault(x => x.Contains(","))
                    ?? cantidades.FirstOrDefault();

                if (string.IsNullOrWhiteSpace(cantidadRaw))
                    continue;

                cantidadRaw = cantidadRaw.Replace(",", "");

                if (!int.TryParse(cantidadRaw, out var cantidad))
                    continue;

                if (cantidad <= 0)
                    continue;

                lista.Add(new PlaneacionReleaseEntregaCrearVm
                {
                    SecuenciaEntrega = secuencia,
                    FechaCarga = fechaCarga,
                    FechaRequerida = fechaRequerida,
                    CantidadRequerida = cantidad
                });

                secuencia++;
            }

            return lista;
        }

        private async Task<int?> ObtenerClienteIdPorNombreAsync(string nombre)
        {
            const string sql = @"
SELECT TOP 1 ClienteID
FROM dbo.ERP_Clientes
WHERE Activo = 1
  AND Nombre LIKE @Nombre
ORDER BY Nombre;";

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@Nombre", SqlDbType.NVarChar, 200).Value = "%" + nombre + "%";

            var result = await cmd.ExecuteScalarAsync();

            return result == null || result == DBNull.Value
                ? null
                : Convert.ToInt32(result);
        }

        private async Task<int?> ObtenerParteIdPorReferenciaAsync(string? referencia, int? clienteId)
        {
            if (string.IsNullOrWhiteSpace(referencia))
                return null;

            const string sql = @"
SELECT TOP 1 ParteID
FROM dbo.ERP_Partes
WHERE Activo = 1
  AND (
        NumeroParte = @Referencia
        OR ReferenciaSAP = @Referencia
      )
  AND (@ClienteID IS NULL OR ClienteID = @ClienteID)
ORDER BY ParteID;";

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            await using var cmd = new SqlCommand(sql, cn);

            cmd.Parameters.Add("@Referencia", SqlDbType.NVarChar, 150).Value = referencia;

            cmd.Parameters.Add("@ClienteID", SqlDbType.Int).Value =
                (object?)clienteId ?? DBNull.Value;

            var result = await cmd.ExecuteScalarAsync();

            return result == null || result == DBNull.Value
                ? null
                : Convert.ToInt32(result);
        }
        private static List<PlaneacionNecesidadPeriodoVm> ConstruirResumenPeriodos(
    List<PlaneacionNecesidadVm> necesidades)
        {
            var hoy = DateTime.Today;

            var inicioSemana = hoy.AddDays(1 - (int)hoy.DayOfWeek);

            if (hoy.DayOfWeek == DayOfWeek.Sunday)
                inicioSemana = hoy.AddDays(-6);

            var finSemana = inicioSemana.AddDays(6);

            var inicioMes = new DateTime(hoy.Year, hoy.Month, 1);
            var finMes = inicioMes.AddMonths(1).AddDays(-1);

            var inicioAnio = new DateTime(hoy.Year, 1, 1);
            var finAnio = new DateTime(hoy.Year, 12, 31);

            return new List<PlaneacionNecesidadPeriodoVm>
    {
        ConstruirResumenPeriodo(
            "Hoy",
            hoy,
            hoy,
            necesidades.Where(x => x.FechaRequerida.Date == hoy.Date)),

        ConstruirResumenPeriodo(
            "Semana",
            inicioSemana,
            finSemana,
            necesidades.Where(x =>
                x.FechaRequerida.Date >= inicioSemana.Date &&
                x.FechaRequerida.Date <= finSemana.Date)),

        ConstruirResumenPeriodo(
            "Mes",
            inicioMes,
            finMes,
            necesidades.Where(x =>
                x.FechaRequerida.Date >= inicioMes.Date &&
                x.FechaRequerida.Date <= finMes.Date)),

        ConstruirResumenPeriodo(
            "Año",
            inicioAnio,
            finAnio,
            necesidades.Where(x =>
                x.FechaRequerida.Date >= inicioAnio.Date &&
                x.FechaRequerida.Date <= finAnio.Date))
    };
        }

        private static PlaneacionNecesidadPeriodoVm ConstruirResumenPeriodo(
            string periodo,
            DateTime fechaDesde,
            DateTime fechaHasta,
            IEnumerable<PlaneacionNecesidadVm> datos)
        {
            var lista = datos.ToList();

            return new PlaneacionNecesidadPeriodoVm
            {
                Periodo = periodo,
                FechaDesde = fechaDesde,
                FechaHasta = fechaHasta,

                Renglones = lista.Count,

                CantidadRequerida = lista.Sum(x => x.CantidadRequerida),
                PiezasDesdePT = lista.Sum(x => x.PiezasDesdePT ?? 0),
                ProduccionProgramadaPendiente = lista.Sum(x => x.ProduccionProgramadaPendiente ?? 0),
                PiezasAProducir = lista.Sum(x => x.PiezasAProducir ?? 0),

                MPRequeridaKg = lista.Sum(x => x.MPRequeridaKg ?? 0),
                HorasNecesarias = lista.Sum(x => x.HorasNecesarias ?? 0)
            };
        }



        // ============================================================
        // ORIGEN: PlaneacionReleaseEdicionController.cs
        // FUNCION: Editar Releases guardados y controlar el impacto sobre
        // programas de produccion vinculados.
        // ============================================================
        // RELEASE_EDICION_FLUJO_V1_0
        [HttpGet]
        public async Task<IActionResult> Editar(int id)
        {
            var detalle = await ObtenerReleaseDetalleAsync(id);
            if (detalle == null)
                return NotFound();

            var vm = new PlaneacionReleaseEditarVm
            {
                ReleaseID = detalle.ReleaseID,
                ClienteID = detalle.ClienteID,
                ClienteNombre = detalle.ClienteNombre,
                FolioRelease = detalle.FolioRelease,
                FolioCliente = detalle.FolioCliente,
                FechaRecepcion = detalle.FechaRecepcion,
                VersionRelease = detalle.VersionRelease,
                ArchivoOrigenNombre = detalle.ArchivoOrigenNombre,
                PlantillaImportacion = detalle.PlantillaImportacion,
                ImportadoDesdeArchivo = detalle.ImportadoDesdeArchivo,
                Observaciones = detalle.Observaciones,
                EstatusID = detalle.EstatusID
            };

            vm.Renglones = detalle.Detalles
                .GroupBy(x => new
                {
                    x.ReleaseRenglonID,
                    x.Renglon,
                    x.ParteID,
                    x.NumeroParte,
                    x.ReferenciaSAP,
                    x.DesignacionDescripcionSAP,
                    x.UnidadMedidaCliente,
                    x.ContratoCliente
                })
                .OrderBy(x => x.Key.Renglon)
                .Select(group => new PlaneacionReleaseRenglonCrearVm
                {
                    Renglon = group.Key.Renglon,
                    ParteID = group.Key.ParteID,
                    NumeroParte = group.Key.NumeroParte,
                    ReferenciaSAP = group.Key.ReferenciaSAP,
                    DesignacionDescripcionSAP =
                        group.Key.DesignacionDescripcionSAP,
                    UnidadMedidaCliente = group.Key.UnidadMedidaCliente,
                    ContratoCliente = group.Key.ContratoCliente,
                    Entregas = group
                        .OrderBy(x => x.SecuenciaEntrega ?? int.MaxValue)
                        .ThenBy(x => x.FechaRequerida)
                        .Select((x, index) =>
                            new PlaneacionReleaseEntregaCrearVm
                            {
                                SecuenciaEntrega =
                                    x.SecuenciaEntrega ?? index + 1,
                                FechaCarga = x.FechaCarga,
                                FechaRequerida = x.FechaRequerida,
                                CantidadRequerida = x.CantidadRequerida
                            })
                        .ToList()
                })
                .ToList();

            if (vm.Renglones.Count == 0)
            {
                vm.Renglones.Add(new PlaneacionReleaseRenglonCrearVm
                {
                    Renglon = 1,
                    Entregas = new List<PlaneacionReleaseEntregaCrearVm>
                    {
                        new()
                        {
                            SecuenciaEntrega = 1,
                            FechaRequerida = DateTime.Today
                        }
                    }
                });
            }

            var impacto =
                await ObtenerImpactoEdicionReleaseAsync(id);

            vm.TienePlaneacionVinculada =
                impacto.ProgramasVinculados > 0;

            vm.TieneProgramaBloqueado =
                impacto.ProgramasBloqueados > 0;

            vm.ProgramasVinculados =
                impacto.ProgramasVinculados;

            await CargarCatalogosAsync(vm);
            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Editar(
            PlaneacionReleaseEditarVm vm)
        {
            var usuarioId = ObtenerUsuarioID();

            LimpiarModeloEdicionRelease(vm);

            if (!vm.ConfirmarImpacto)
            {
                ModelState.AddModelError(
                    string.Empty,
                    "Debes confirmar que comprendes el impacto sobre la planeación.");
            }

            if (!vm.ClienteID.HasValue &&
                string.IsNullOrWhiteSpace(vm.ClienteNombre))
            {
                ModelState.AddModelError(
                    string.Empty,
                    "Selecciona el cliente del Release.");
            }

            if (vm.Renglones.Count == 0)
            {
                ModelState.AddModelError(
                    string.Empty,
                    "Debes conservar al menos un renglón.");
            }

            foreach (var renglon in vm.Renglones)
            {
                if (!renglon.ParteID.HasValue)
                {
                    ModelState.AddModelError(
                        string.Empty,
                        $"El renglón {renglon.Renglon} no tiene parte seleccionada.");
                }

                if (renglon.Entregas.Count == 0)
                {
                    ModelState.AddModelError(
                        string.Empty,
                        $"El renglón {renglon.Renglon} debe tener al menos una entrega.");
                }

                foreach (var entrega in renglon.Entregas)
                {
                    if (!entrega.FechaRequerida.HasValue)
                    {
                        ModelState.AddModelError(
                            string.Empty,
                            $"El renglón {renglon.Renglon} tiene una entrega sin fecha requerida.");
                    }

                    if (entrega.CantidadRequerida <= 0)
                    {
                        ModelState.AddModelError(
                            string.Empty,
                            $"El renglón {renglon.Renglon} tiene una cantidad menor o igual a cero.");
                    }
                }
            }

            var impacto =
                await ObtenerImpactoEdicionReleaseAsync(vm.ReleaseID);

            vm.TienePlaneacionVinculada =
                impacto.ProgramasVinculados > 0;

            vm.TieneProgramaBloqueado =
                impacto.ProgramasBloqueados > 0;

            vm.ProgramasVinculados =
                impacto.ProgramasVinculados;

            if (vm.TieneProgramaBloqueado)
            {
                ModelState.AddModelError(
                    string.Empty,
                    "Este Release tiene producción en proceso, terminada o cerrada. No puede editarse porque alteraría información productiva ya ejecutada.");
            }

            if (!ModelState.IsValid)
            {
                await CargarCatalogosAsync(vm);
                return View(vm);
            }

            await using var cn =
                new SqlConnection(ConnectionString);

            await cn.OpenAsync();

            await using var tx =
                (SqlTransaction)await cn.BeginTransactionAsync();

            try
            {
                const string sqlLock = @"
    SELECT EstatusID
    FROM dbo.Planeacion_Releases WITH (UPDLOCK, ROWLOCK)
    WHERE ReleaseID = @ReleaseID
      AND Activo = 1;";

                await using (var cmd =
                    new SqlCommand(sqlLock, cn, tx))
                {
                    cmd.Parameters.Add(
                        "@ReleaseID",
                        SqlDbType.Int).Value = vm.ReleaseID;

                    var exists = await cmd.ExecuteScalarAsync();
                    if (exists == null || exists == DBNull.Value)
                    {
                        await tx.RollbackAsync();
                        return NotFound();
                    }
                }

                var bloqueados =
                    await ContarProgramasBloqueadosEdicionAsync(
                        vm.ReleaseID,
                        cn,
                        tx);

                if (bloqueados > 0)
                {
                    await tx.RollbackAsync();

                    ModelState.AddModelError(
                        string.Empty,
                        "La edición fue cancelada porque el Release ya tiene producción en proceso, terminada o cerrada.");

                    vm.TieneProgramaBloqueado = true;
                    await CargarCatalogosAsync(vm);
                    return View(vm);
                }

                await InvalidarPlaneacionPendienteReleaseAsync(
                    vm.ReleaseID,
                    usuarioId,
                    cn,
                    tx);

                const string sqlDesactivarFuente = @"
    UPDATE dbo.Planeacion_ReleaseDetalle
    SET Activo = 0
    WHERE ReleaseID = @ReleaseID
      AND Activo = 1;

    UPDATE dbo.Planeacion_ReleaseRenglones
    SET Activo = 0
    WHERE ReleaseID = @ReleaseID
      AND Activo = 1;";

                await using (var cmd =
                    new SqlCommand(sqlDesactivarFuente, cn, tx))
                {
                    cmd.Parameters.Add(
                        "@ReleaseID",
                        SqlDbType.Int).Value = vm.ReleaseID;

                    await cmd.ExecuteNonQueryAsync();
                }

                var clienteNombre = vm.ClienteNombre;

                if (vm.ClienteID.HasValue)
                {
                    clienteNombre =
                        await ObtenerClienteNombreAsync(
                            vm.ClienteID.Value,
                            cn,
                            tx);
                }

                const string sqlActualizarRelease = @"
    UPDATE dbo.Planeacion_Releases
    SET
        FolioRelease = @FolioRelease,
        FolioCliente = @FolioCliente,
        ClienteID = @ClienteID,
        ClienteNombre = @ClienteNombre,
        FechaRecepcion = @FechaRecepcion,
        VersionRelease = @VersionRelease,
        Observaciones = @Observaciones,
        EstatusID = @EstatusID,
        UsuarioModificacionID = @UsuarioID,
        FechaModificacion = GETDATE()
    WHERE ReleaseID = @ReleaseID
      AND Activo = 1;";

                await using (var cmd =
                    new SqlCommand(sqlActualizarRelease, cn, tx))
                {
                    cmd.Parameters.Add(
                        "@ReleaseID",
                        SqlDbType.Int).Value = vm.ReleaseID;

                    cmd.Parameters.Add(
                        "@FolioRelease",
                        SqlDbType.NVarChar,
                        40).Value =
                        (object?)vm.FolioRelease ?? DBNull.Value;

                    cmd.Parameters.Add(
                        "@FolioCliente",
                        SqlDbType.NVarChar,
                        100).Value =
                        (object?)vm.FolioCliente ?? DBNull.Value;

                    cmd.Parameters.Add(
                        "@ClienteID",
                        SqlDbType.Int).Value =
                        (object?)vm.ClienteID ?? DBNull.Value;

                    cmd.Parameters.Add(
                        "@ClienteNombre",
                        SqlDbType.NVarChar,
                        200).Value =
                        (object?)clienteNombre ?? DBNull.Value;

                    cmd.Parameters.Add(
                        "@FechaRecepcion",
                        SqlDbType.Date).Value =
                        vm.FechaRecepcion.Date;

                    cmd.Parameters.Add(
                        "@VersionRelease",
                        SqlDbType.NVarChar,
                        50).Value =
                        (object?)vm.VersionRelease ?? DBNull.Value;

                    cmd.Parameters.Add(
                        "@Observaciones",
                        SqlDbType.NVarChar,
                        500).Value =
                        (object?)vm.Observaciones ?? DBNull.Value;

                    cmd.Parameters.Add(
                        "@EstatusID",
                        SqlDbType.Int).Value =
                        PlaneacionReleaseEstatus.Calculado;

                    cmd.Parameters.Add(
                        "@UsuarioID",
                        SqlDbType.Int).Value = usuarioId;

                    await cmd.ExecuteNonQueryAsync();
                }

                var renglonNumero = 1;

                foreach (var renglon in vm.Renglones)
                {
                    renglon.Renglon = renglonNumero;

                    await CompletarRenglonDesdeParteAsync(
                        renglon,
                        cn,
                        tx);

                    var releaseRenglonId =
                        await InsertarReleaseRenglonAsync(
                            vm.ReleaseID,
                            renglon,
                            usuarioId,
                            cn,
                            tx);

                    var secuencia = 1;

                    foreach (var entrega in renglon.Entregas)
                    {
                        // RELEASE_FECHA_CARGA_AUTO_EDICION_V1_0
                        if (vm.ImportadoDesdeArchivo &&
                            !entrega.FechaCarga.HasValue &&
                            entrega.FechaRequerida.HasValue)
                        {
                            entrega.FechaCarga =
                                entrega.FechaRequerida.Value.Date.AddDays(-1);
                        }
                        entrega.SecuenciaEntrega = secuencia;

                        var detalle =
                            CrearDetalleDesdeRenglonEntrega(
                                renglon,
                                entrega);

                        await CompletarDetalleDesdeParteAsync(
                            detalle,
                            cn,
                            tx);

                        await CalcularNecesidadAsync(
                            detalle,
                            cn,
                            tx);

                        await InsertarReleaseDetalleAsync(
                            vm.ReleaseID,
                            releaseRenglonId,
                            secuencia,
                            detalle,
                            usuarioId,
                            cn,
                            tx);

                        secuencia++;
                    }

                    renglonNumero++;
                }

                await ActualizarEstatusReleaseAsync(
                    vm.ReleaseID,
                    PlaneacionReleaseEstatus.Calculado,
                    usuarioId,
                    cn,
                    tx);

                await tx.CommitAsync();

                TempData["Success"] =
                    impacto.ProgramasVinculados > 0
                        ? "Release actualizado. La programación pendiente relacionada fue cancelada para que el flujo se genere nuevamente con los datos corregidos."
                        : "Release actualizado y recalculado correctamente.";

                return RedirectToAction(
                    nameof(Detalle),
                    new { id = vm.ReleaseID });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();

                ModelState.AddModelError(
                    string.Empty,
                    "No fue posible editar el Release: " +
                    ex.Message);

                await CargarCatalogosAsync(vm);
                return View(vm);
            }
        }

        private static void LimpiarModeloEdicionRelease(
            PlaneacionReleaseEditarVm vm)
        {
            vm.Renglones ??=
                new List<PlaneacionReleaseRenglonCrearVm>();

            vm.Renglones = vm.Renglones
                .Where(x =>
                    x.ParteID.HasValue ||
                    !string.IsNullOrWhiteSpace(x.NumeroParte) ||
                    !string.IsNullOrWhiteSpace(x.ReferenciaSAP))
                .ToList();

            foreach (var renglon in vm.Renglones)
            {
                renglon.Entregas ??=
                    new List<PlaneacionReleaseEntregaCrearVm>();

                renglon.Entregas = renglon.Entregas
                    .Where(x =>
                        x.FechaRequerida.HasValue ||
                        x.CantidadRequerida > 0)
                    .ToList();
            }

            vm.Renglones = vm.Renglones
                .Where(x => x.Entregas.Count > 0)
                .ToList();

            for (var i = 0; i < vm.Renglones.Count; i++)
            {
                vm.Renglones[i].Renglon = i + 1;

                for (var j = 0;
                     j < vm.Renglones[i].Entregas.Count;
                     j++)
                {
                    vm.Renglones[i]
                        .Entregas[j]
                        .SecuenciaEntrega = j + 1;
                }
            }
        }

        private async Task<(
            int ProgramasVinculados,
            int ProgramasBloqueados)>
            ObtenerImpactoEdicionReleaseAsync(int releaseId)
        {
            await using var cn =
                new SqlConnection(ConnectionString);

            await cn.OpenAsync();

            const string sql = @"
    SELECT
        COUNT(DISTINCT pp.ProgramaProduccionID) AS TotalProgramas,
        COUNT(DISTINCT CASE
            WHEN pp.EstatusID IN (
                @EnProduccion,
                @Terminado,
                @Cerrado
            )
            THEN pp.ProgramaProduccionID
        END) AS Bloqueados
    FROM dbo.Planeacion_ReleaseDetalle d
    LEFT JOIN dbo.Planeacion_ProgramaProduccion pp
        ON pp.ReleaseDetalleID = d.ReleaseDetalleID
       AND pp.Activo = 1
    WHERE d.ReleaseID = @ReleaseID
      AND d.Activo = 1;";

            await using var cmd =
                new SqlCommand(sql, cn);

            cmd.Parameters.Add(
                "@ReleaseID",
                SqlDbType.Int).Value = releaseId;

            cmd.Parameters.Add(
                "@EnProduccion",
                SqlDbType.Int).Value =
                PlaneacionProgramaEstatus.EnProduccion;

            cmd.Parameters.Add(
                "@Terminado",
                SqlDbType.Int).Value =
                PlaneacionProgramaEstatus.Terminado;

            cmd.Parameters.Add(
                "@Cerrado",
                SqlDbType.Int).Value =
                PlaneacionProgramaEstatus.Cerrado;

            await using var rd =
                await cmd.ExecuteReaderAsync();

            if (!await rd.ReadAsync())
                return (0, 0);

            return (
                rd["TotalProgramas"] == DBNull.Value
                    ? 0
                    : Convert.ToInt32(rd["TotalProgramas"]),

                rd["Bloqueados"] == DBNull.Value
                    ? 0
                    : Convert.ToInt32(rd["Bloqueados"]));
        }

        private static async Task<int>
            ContarProgramasBloqueadosEdicionAsync(
                int releaseId,
                SqlConnection cn,
                SqlTransaction tx)
        {
            const string sql = @"
    SELECT COUNT(DISTINCT pp.ProgramaProduccionID)
    FROM dbo.Planeacion_ReleaseDetalle d
    INNER JOIN dbo.Planeacion_ProgramaProduccion pp
        ON pp.ReleaseDetalleID = d.ReleaseDetalleID
       AND pp.Activo = 1
    WHERE d.ReleaseID = @ReleaseID
      AND d.Activo = 1
      AND pp.EstatusID IN (
          @EnProduccion,
          @Terminado,
          @Cerrado
      );";

            await using var cmd =
                new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add(
                "@ReleaseID",
                SqlDbType.Int).Value = releaseId;

            cmd.Parameters.Add(
                "@EnProduccion",
                SqlDbType.Int).Value =
                PlaneacionProgramaEstatus.EnProduccion;

            cmd.Parameters.Add(
                "@Terminado",
                SqlDbType.Int).Value =
                PlaneacionProgramaEstatus.Terminado;

            cmd.Parameters.Add(
                "@Cerrado",
                SqlDbType.Int).Value =
                PlaneacionProgramaEstatus.Cerrado;

            return Convert.ToInt32(
                await cmd.ExecuteScalarAsync());
        }

        private static async Task
            InvalidarPlaneacionPendienteReleaseAsync(
                int releaseId,
                int usuarioId,
                SqlConnection cn,
                SqlTransaction tx)
        {
            const string sql = @"
    DECLARE @SolicitudesDetalle TABLE
    (
        SolicitudProduccionDetalleID INT PRIMARY KEY
    );

    INSERT INTO @SolicitudesDetalle
    (
        SolicitudProduccionDetalleID
    )
    SELECT DISTINCT pp.SolicitudProduccionDetalleID
    FROM dbo.Planeacion_ProgramaProduccion pp
    INNER JOIN dbo.Planeacion_ReleaseDetalle d
        ON d.ReleaseDetalleID = pp.ReleaseDetalleID
    WHERE d.ReleaseID = @ReleaseID
      AND d.Activo = 1
      AND pp.Activo = 1
      AND pp.SolicitudProduccionDetalleID IS NOT NULL
      AND pp.EstatusID NOT IN (
          @EnProduccion,
          @Terminado,
          @Cerrado
      );

    UPDATE pp
    SET
        pp.EstatusID = @Cancelado,
        pp.Activo = 0,
        pp.UsuarioModificacionID = @UsuarioID,
        pp.FechaModificacion = GETDATE()
    FROM dbo.Planeacion_ProgramaProduccion pp
    INNER JOIN dbo.Planeacion_ReleaseDetalle d
        ON d.ReleaseDetalleID = pp.ReleaseDetalleID
    WHERE d.ReleaseID = @ReleaseID
      AND d.Activo = 1
      AND pp.Activo = 1
      AND pp.EstatusID NOT IN (
          @EnProduccion,
          @Terminado,
          @Cerrado
      );

    IF OBJECT_ID(
        'dbo.SolicitudesProduccionAsignacionMaquina',
        'U') IS NOT NULL
    BEGIN
        UPDATE asignacion
        SET asignacion.Activo = 0
        FROM dbo.SolicitudesProduccionAsignacionMaquina asignacion
        INNER JOIN @SolicitudesDetalle ids
            ON ids.SolicitudProduccionDetalleID =
               asignacion.SolicitudProduccionDetalleID
        WHERE asignacion.Activo = 1;
    END;

    IF OBJECT_ID(
        'dbo.SolicitudesProduccionDetalle',
        'U') IS NOT NULL
    BEGIN
        UPDATE detalle
        SET detalle.Activo = 0
        FROM dbo.SolicitudesProduccionDetalle detalle
        INNER JOIN @SolicitudesDetalle ids
            ON ids.SolicitudProduccionDetalleID =
               detalle.SolicitudProduccionDetalleID
        WHERE detalle.Activo = 1;
    END;";

            await using var cmd =
                new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add(
                "@ReleaseID",
                SqlDbType.Int).Value = releaseId;

            cmd.Parameters.Add(
                "@UsuarioID",
                SqlDbType.Int).Value = usuarioId;

            cmd.Parameters.Add(
                "@EnProduccion",
                SqlDbType.Int).Value =
                PlaneacionProgramaEstatus.EnProduccion;

            cmd.Parameters.Add(
                "@Terminado",
                SqlDbType.Int).Value =
                PlaneacionProgramaEstatus.Terminado;

            cmd.Parameters.Add(
                "@Cerrado",
                SqlDbType.Int).Value =
                PlaneacionProgramaEstatus.Cerrado;

            cmd.Parameters.Add(
                "@Cancelado",
                SqlDbType.Int).Value =
                PlaneacionProgramaEstatus.Cancelado;

            await cmd.ExecuteNonQueryAsync();
        }


        // ============================================================
        // ORIGEN: PlaneacionReleaseValidacionEdicionController.cs
        // FUNCION: Modificar cliente, partes y entregas de un documento
        // preparado antes de su importacion definitiva.
        // ============================================================

        [HttpGet]
        public async Task<IActionResult> ObtenerClientesValidacionEdicion()
        {
            const string sql = @"
SELECT ClienteID, Nombre
FROM dbo.ERP_Clientes
WHERE Activo = 1
ORDER BY Nombre;";

            var clientes = new List<object>();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            await using var cmd = new SqlCommand(sql, cn);
            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                clientes.Add(new
                {
                    id = Convert.ToInt32(rd["ClienteID"]),
                    text = rd["Nombre"] as string ?? "Cliente"
                });
            }

            return Json(new { ok = true, clientes });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GuardarEdicionDocumentoPreparado(
            ReleaseDocumentoPreparadoEditarRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.LoteID) ||
                string.IsNullOrWhiteSpace(request.DocumentoID))
            {
                TempData["Error"] = "No se recibio el lote o documento.";
                return RedirectToAction(nameof(PendientesValidar));
            }

            var lote = await CargarLoteValidacionAsync(request.LoteID);
            if (lote == null)
                return NotFound();

            var documento = lote.Documentos.FirstOrDefault(x =>
                string.Equals(x.DocumentoID, request.DocumentoID, StringComparison.OrdinalIgnoreCase));

            if (documento == null)
                return NotFound();

            if (documento.Estado == ReleaseValidacionEstados.Guardado)
            {
                TempData["Error"] = "El documento ya fue guardado y no puede editarse.";
                return RedirectToAction(nameof(ValidacionLote), new { loteId = request.LoteID });
            }

            if (!request.ClienteID.HasValue || request.ClienteID.Value <= 0)
            {
                TempData["Error"] = "Selecciona un cliente valido.";
                return RedirectToAction(nameof(ValidacionLote), new { loteId = request.LoteID });
            }

            if (request.Renglones == null || request.Renglones.Count == 0)
            {
                TempData["Error"] = "El documento debe conservar al menos una parte.";
                return RedirectToAction(nameof(ValidacionLote), new { loteId = request.LoteID });
            }

            const string sqlCliente = @"
SELECT TOP (1) Nombre
FROM dbo.ERP_Clientes
WHERE ClienteID = @ClienteID
  AND Activo = 1;";

            string? clienteNombre;

            await using (var cn = new SqlConnection(ConnectionString))
            {
                await cn.OpenAsync();
                await using var cmd = new SqlCommand(sqlCliente, cn);
                cmd.Parameters.Add("@ClienteID", SqlDbType.Int).Value = request.ClienteID.Value;
                clienteNombre = await cmd.ExecuteScalarAsync() as string;
            }

            if (string.IsNullOrWhiteSpace(clienteNombre))
            {
                TempData["Error"] = "El cliente seleccionado no existe o esta inactivo.";
                return RedirectToAction(nameof(ValidacionLote), new { loteId = request.LoteID });
            }

            var nuevosRenglones = new List<PlaneacionReleaseRenglonCrearVm>();

            for (var i = 0; i < request.Renglones.Count; i++)
            {
                var source = request.Renglones[i];

                if (source.Entregas == null || source.Entregas.Count == 0)
                {
                    TempData["Error"] = $"La parte {i + 1} debe conservar al menos una entrega.";
                    return RedirectToAction(nameof(ValidacionLote), new { loteId = request.LoteID });
                }

                var referenciaBusqueda =
                    !string.IsNullOrWhiteSpace(source.NumeroParte)
                        ? source.NumeroParte
                        : source.ReferenciaSAP;

                var match = await BuscarParteActivaValidacionAsync(
                    referenciaBusqueda ?? string.Empty,
                    request.ClienteID.Value);

                var entregas = new List<PlaneacionReleaseEntregaCrearVm>();

                for (var j = 0; j < source.Entregas.Count; j++)
                {
                    var e = source.Entregas[j];

                    if (!e.FechaRequerida.HasValue || e.CantidadRequerida <= 0)
                    {
                        TempData["Error"] =
                            $"Revisa fecha y cantidad de la entrega {j + 1} en la parte {i + 1}.";

                        return RedirectToAction(nameof(ValidacionLote), new { loteId = request.LoteID });
                    }

                    entregas.Add(new PlaneacionReleaseEntregaCrearVm
                    {
                        SecuenciaEntrega = j + 1,
                        FechaRequerida = e.FechaRequerida.Value.Date,
                        FechaCarga = e.FechaCarga?.Date,
                        CantidadRequerida = e.CantidadRequerida
                    });
                }

                var numero = TextoValidacionEdicion(source.NumeroParte, 150);
                var referencia = TextoValidacionEdicion(source.ReferenciaSAP, 150);
                var descripcion = TextoValidacionEdicion(source.DesignacionDescripcionSAP, 300);
                int? parteId = null;

                if (match.HasValue)
                {
                    parteId = match.Value.ParteID;
                    numero ??= match.Value.NumeroParte;
                    referencia ??= match.Value.ReferenciaSAP;
                    descripcion ??= match.Value.Descripcion;
                }

                if (string.IsNullOrWhiteSpace(numero) && string.IsNullOrWhiteSpace(referencia))
                {
                    TempData["Error"] = $"La parte {i + 1} necesita numero de parte o referencia SAP.";
                    return RedirectToAction(nameof(ValidacionLote), new { loteId = request.LoteID });
                }

                nuevosRenglones.Add(new PlaneacionReleaseRenglonCrearVm
                {
                    Renglon = i + 1,
                    ParteID = parteId,
                    NumeroParte = numero,
                    ReferenciaSAP = referencia,
                    DesignacionDescripcionSAP = descripcion,
                    UnidadMedidaCliente = TextoValidacionEdicion(source.UnidadMedidaCliente, 100),
                    ContratoCliente = TextoValidacionEdicion(source.ContratoCliente, 150),
                    Observaciones = TextoValidacionEdicion(source.Observaciones, 1000),
                    Entregas = entregas
                });
            }

            var preparado = documento.ReleasePreparado ?? new PlaneacionReleaseCrearVm();

            preparado.ClienteID = request.ClienteID.Value;
            preparado.ClienteNombre = clienteNombre;
            preparado.FolioCliente = TextoValidacionEdicion(request.FolioCliente, 150);
            preparado.VersionRelease = TextoValidacionEdicion(request.Version, 100);
            preparado.FechaRecepcion =
                request.FechaRecepcion == default ? DateTime.Today : request.FechaRecepcion.Date;
            preparado.Observaciones = TextoValidacionEdicion(request.Observaciones, 2000);
            preparado.ArchivoOrigenNombre = documento.Archivo;
            preparado.PlantillaImportacion = documento.Plantilla;
            preparado.ImportadoDesdeArchivo = true;
            preparado.EstatusID = PlaneacionReleaseEstatus.Capturado;
            preparado.Renglones = nuevosRenglones;

            documento.ReleasePreparado = preparado;
            documento.ClienteID = request.ClienteID.Value;
            documento.Cliente = clienteNombre;
            documento.FolioCliente = preparado.FolioCliente;
            documento.Version = preparado.VersionRelease;
            documento.FechaDocumento = request.FechaDocumento?.Date;
            documento.TotalEntregas = nuevosRenglones.Sum(x => x.Entregas.Count);
            documento.TotalPiezas = nuevosRenglones.Sum(x => x.Entregas.Sum(e => e.CantidadRequerida));

            DefinirEstadoDocumentoPreparado(documento);
            await GuardarLoteValidacionAsync(lote);

            TempData["Success"] = "Cambios guardados en el documento preparado.";
            return RedirectToAction(nameof(ValidacionLote), new { loteId = request.LoteID });
        }

        private static string? TextoValidacionEdicion(string? value, int max)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var text = value.Trim();
            return text.Length <= max ? text : text[..max];
        }



        // ============================================================
        // ORIGEN: PlaneacionReleaseValidacionController.cs
        // VERSION: RELEASE_VALIDACION_LOTES_V2_0
        // FUNCION: Crear lotes temporales, analizar PDF/Excel, validar partes,
        // conservar pendientes y confirmar la importacion definitiva.
        // ============================================================

        private static readonly JsonSerializerOptions ReleaseValidacionJsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(268435456)]
        public async Task<IActionResult> ValidarDocumentosPreparados(List<IFormFile>? archivos)
        {
            var usuarioId = ObtenerUsuarioID();
            var lote = new ReleaseValidacionLoteVm
            {
                LoteID = Guid.NewGuid().ToString("N"),
                FechaProceso = DateTime.Now,
                UsuarioID = usuarioId
            };

            if (usuarioId <= 0)
            {
                lote.ErrorGeneral = "No se pudo identificar el usuario de la sesión.";
                return View("Validacion", lote);
            }

            if (archivos == null || archivos.Count == 0)
            {
                lote.ErrorGeneral = "Selecciona al menos un documento.";
                return View("Validacion", lote);
            }

            const int maxFiles = 25;
            const long maxFileBytes = 10L * 1024L * 1024L;

            if (archivos.Count > maxFiles)
                lote.NotaGeneral = $"Solo se analizaron los primeros {maxFiles} archivos.";

            var loteRoot = ObtenerRutaLoteValidacion(lote.LoteID);
            Directory.CreateDirectory(loteRoot);

            foreach (var archivo in archivos.Take(maxFiles))
            {
                var documento = new ReleaseValidacionDocumentoVm
                {
                    Archivo = Path.GetFileName(archivo.FileName)
                };
                lote.Documentos.Add(documento);

                try
                {
                    if (archivo.Length <= 0)
                        throw new InvalidOperationException("El archivo está vacío.");

                    if (archivo.Length > maxFileBytes)
                        throw new InvalidOperationException("El archivo supera el límite de 10 MB.");

                    using var memory = new MemoryStream();
                    await archivo.CopyToAsync(memory);
                    var bytes = memory.ToArray();

                    var extension = Path.GetExtension(documento.Archivo).ToLowerInvariant();
                    var temporalName = $"{documento.DocumentoID}{extension}";
                    var temporalPath = Path.Combine(loteRoot, temporalName);

                    await System.IO.File.WriteAllBytesAsync(temporalPath, bytes);
                    documento.ArchivoTemporal = temporalName;
                    documento.Sha256 = Convert.ToHexString(SHA256.HashData(bytes));

                    await AnalizarDocumentoValidacionAsync(documento, bytes);
                }
                catch (Exception ex)
                {
                    documento.Estado = ReleaseValidacionEstados.Error;
                    documento.Mensaje = ex.Message;
                }
            }

            await GuardarLoteValidacionAsync(lote);
            return View("Validacion", lote);
        }

        [HttpGet]
        public async Task<IActionResult> ValidacionLote(string loteId)
        {
            var lote = await CargarLoteValidacionAsync(loteId);
            if (lote == null)
                return NotFound();

            return View("Validacion", lote);
        }

        [HttpGet]
        public async Task<IActionResult> PendientesValidar()
        {
            var vm = new ReleasePendientesValidarVm();
            var root = ObtenerRutaRaizValidaciones();

            if (Directory.Exists(root))
            {
                foreach (var loteDirectory in Directory.GetDirectories(root))
                {
                    var loteId = Path.GetFileName(loteDirectory);
                    var lote = await CargarLoteValidacionAsync(loteId);

                    if (lote != null && lote.Documentos.Any(x =>
                        x.Estado == ReleaseValidacionEstados.Pendiente))
                    {
                        vm.Lotes.Add(lote);
                    }
                }
            }

            vm.Lotes = vm.Lotes.OrderByDescending(x => x.FechaProceso).ToList();
            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VincularPartePendiente(
            string loteId,
            string documentoId,
            int renglonIndex,
            string numeroParte)
        {
            var lote = await CargarLoteValidacionAsync(loteId);
            if (lote == null)
                return NotFound();

            var documento = lote.Documentos.FirstOrDefault(x => x.DocumentoID == documentoId);
            if (documento == null)
                return NotFound();

            if (renglonIndex < 0 || renglonIndex >= documento.ReleasePreparado.Renglones.Count)
            {
                TempData["Error"] = "El renglón seleccionado no es válido.";
                return RedirectToAction(nameof(ValidacionLote), new { loteId });
            }

            if (!documento.ClienteID.HasValue)
            {
                TempData["Error"] = "El documento no tiene un cliente válido.";
                return RedirectToAction(nameof(ValidacionLote), new { loteId });
            }

            var parte = await BuscarParteActivaValidacionAsync(numeroParte, documento.ClienteID.Value);
            if (parte == null)
            {
                TempData["Error"] = $"No se encontró una parte activa para '{numeroParte}' dentro del cliente detectado.";
                return RedirectToAction(nameof(ValidacionLote), new { loteId });
            }

            var renglon = documento.ReleasePreparado.Renglones[renglonIndex];
            renglon.ParteID = parte.Value.ParteID;
            renglon.NumeroParte = parte.Value.NumeroParte;
            renglon.ReferenciaSAP = parte.Value.ReferenciaSAP;
            renglon.DesignacionDescripcionSAP = parte.Value.Descripcion;

            DefinirEstadoDocumentoPreparado(documento);
            await GuardarLoteValidacionAsync(lote);

            TempData["Success"] = "Parte vinculada correctamente.";
            return RedirectToAction(nameof(ValidacionLote), new { loteId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmarImportacionValidada(
            string loteId,
            List<string>? documentosSeleccionados)
        {
            var lote = await CargarLoteValidacionAsync(loteId);
            if (lote == null)
                return NotFound();

            documentosSeleccionados ??= new List<string>();

            var seleccionados = lote.Documentos
                .Where(x => documentosSeleccionados.Contains(x.DocumentoID) &&
                            x.Estado == ReleaseValidacionEstados.Validado)
                .ToList();

            if (seleccionados.Count == 0)
            {
                lote.ErrorGeneral = "Selecciona al menos un documento validado.";
                return View("Validacion", lote);
            }

            var usuarioId = ObtenerUsuarioID();
            var resultado = new PlaneacionReleaseImportacionResultadoVm();

            foreach (var documento in seleccionados)
            {
                var item = new PlaneacionReleaseImportacionArchivoVm
                {
                    Archivo = documento.Archivo,
                    ClienteID = documento.ClienteID,
                    Cliente = documento.Cliente,
                    Parte = documento.PartesTexto,
                    Descripcion = documento.ReleasePreparado.Renglones.Count == 1
                        ? documento.ReleasePreparado.Renglones[0].DesignacionDescripcionSAP
                        : $"{documento.ReleasePreparado.Renglones.Count} partes",
                    Schedule = documento.Version,
                    OrdenCliente = documento.FolioCliente,
                    Version = documento.Version,
                    TotalEntregas = documento.TotalEntregas,
                    TotalPiezas = documento.TotalPiezas,
                    Advertencias = documento.Advertencias.ToList()
                };
                resultado.Archivos.Add(item);

                await using var cn = new SqlConnection(ConnectionString);
                await cn.OpenAsync();
                await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();

                try
                {
                    var temporalPath = Path.Combine(
                        ObtenerRutaLoteValidacion(lote.LoteID),
                        documento.ArchivoTemporal);

                    if (!System.IO.File.Exists(temporalPath))
                        throw new FileNotFoundException("No se encontró el archivo temporal.", temporalPath);

                    var bytes = await System.IO.File.ReadAllBytesAsync(temporalPath);

                    var duplicado = documento.Plantilla == "HUF_SUPPLIER_SCHEDULE"
                        ? await ExisteDocumentoHufImportadoAsync(documento.Sha256, cn, tx)
                        : await ExisteDocumentoImportadoAsync(documento.Plantilla, documento.Sha256, cn, tx);

                    if (duplicado)
                    {
                        await tx.RollbackAsync();
                        documento.Estado = ReleaseValidacionEstados.Omitido;
                        documento.Mensaje = "El documento ya había sido importado.";
                        item.Estado = "OMITIDO";
                        item.Mensaje = documento.Mensaje;
                        continue;
                    }

                    if (documento.ReleasePreparado.Renglones.Any(x => !x.ParteID.HasValue))
                        throw new InvalidOperationException("El documento todavía contiene partes sin validar.");

                    var archivoGuardado = await GuardarDocumentoOriginalReleaseAsync(bytes, documento.Archivo);
                    var vm = documento.ReleasePreparado;
                    // RELEASE_UPDATE_SAVE_V1_0
                    if (documento.ReleaseID.HasValue)
                    {
                        var resumenActualizacion =
                            await ActualizarReleaseExistenteDesdeDocumentoAsync(
                                documento,
                                archivoGuardado,
                                usuarioId,
                                cn,
                                tx);

                        await tx.CommitAsync();

                        documento.Estado = ReleaseValidacionEstados.Guardado;
                        documento.FolioRelease = resumenActualizacion.FolioRelease;
                        documento.Mensaje = resumenActualizacion.ConstruirMensaje();

                        item.Estado = "ACTUALIZADO";
                        item.ReleaseID = resumenActualizacion.ReleaseID;
                        item.FolioRelease = resumenActualizacion.FolioRelease;
                        item.ArchivoGuardado = archivoGuardado;
                        item.Mensaje = documento.Mensaje;

                        continue;
                    }

                    vm.FolioRelease = await GenerarFolioReleaseAsync(cn, tx);
                    vm.ArchivoOrigenNombre = documento.Archivo;
                    vm.PlantillaImportacion = documento.Plantilla;
                    vm.ImportadoDesdeArchivo = true;
                    vm.FechaRecepcion = DateTime.Today;
                    vm.EstatusID = PlaneacionReleaseEstatus.Capturado;
                    vm.Observaciones =
                        $"IMPORTACION_VALIDADA;PLANTILLA:{documento.Plantilla};" +
                        $"SHA256:{documento.Sha256};ARCHIVO_GUARDADO:{archivoGuardado};LOTE:{lote.LoteID}";

                    var versionesCerradas = 0;

                    if (documento.Plantilla == "HUF_SUPPLIER_SCHEDULE")
                    {
                        versionesCerradas = await CerrarVersionesHufAnterioresAsync(
                            vm.ClienteID!.Value,
                            vm.Renglones[0].ParteID!.Value,
                            usuarioId,
                            cn,
                            tx);
                    }
                    else if (documento.Plantilla == "VERITAS_SCHEDULE")
                    {
                        versionesCerradas = await CerrarVersionesVeritasAnterioresAsync(
                            vm.ClienteID!.Value,
                            vm.FolioCliente ?? string.Empty,
                            usuarioId,
                            cn,
                            tx);
                    }
                    else if (documento.Plantilla == "GOLDE_MEXICO_WEEKLY_RELEASE" ||
                             documento.Plantilla == "GOLDEN_WEEKLY_RELEASE" ||
                             documento.Plantilla == "NORMA_WEEKLY_RELEASE" ||
                             documento.Plantilla == "AIR_THERMAL_MATERIAL_RELEASE")
                    {
                        versionesCerradas = await CerrarVersionesExcelAnterioresAsync(
                            vm.ClienteID!.Value,
                            documento.Plantilla,
                            usuarioId,
                            cn,
                            tx);
                    }

                    var clienteNombre = await ObtenerClienteNombreAsync(vm.ClienteID!.Value, cn, tx)
                        ?? documento.Cliente
                        ?? "Cliente";

                    var releaseId = await GuardarReleaseImportadoFlexibleAsync(
                        vm,
                        clienteNombre,
                        usuarioId,
                        cn,
                        tx,
                        false);

                    await tx.CommitAsync();

                    documento.Estado = ReleaseValidacionEstados.Guardado;
                    documento.ReleaseID = releaseId;
                    documento.FolioRelease = vm.FolioRelease;
                    documento.Mensaje = "Release guardado correctamente.";

                    item.Estado = "CREADO";
                    item.ReleaseID = releaseId;
                    item.FolioRelease = vm.FolioRelease;
                    item.ArchivoGuardado = archivoGuardado;
                    item.VersionesAnterioresCerradas = versionesCerradas;
                    item.Mensaje = documento.Mensaje;
                }
                catch (Exception ex)
                {
                    await tx.RollbackAsync();
                    item.Estado = "ERROR";
                    item.Mensaje = ex.Message;
                    documento.Mensaje = ex.Message;
                }
            }

            await GuardarLoteValidacionAsync(lote);
            return View("Importacion", resultado);
        }

        private async Task AnalizarDocumentoValidacionAsync(
            ReleaseValidacionDocumentoVm documento,
            byte[] bytes)
        {
            // GENERIC_RELEASE_ANALYZER_V1_1
            // Flujo:
            // 1) parser especializado conocido
            // 2) GOLDE MEXICO directo para no depender de Detect()
            // 3) fallback generico contra ERP_Partes
            // 4) hidratar siempre los datos maestros antes de mostrar vista previa
            var extension = Path.GetExtension(documento.Archivo).ToLowerInvariant();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();

            try
            {
                if (extension == ".pdf")
                {
                    var template = ReleasePdfDocumentDetector.Detect(bytes);

                    if (template == ReleasePdfTemplate.HufSupplierSchedule)
                    {
                        await PrepararHufValidacionAsync(documento, bytes, cn, tx);
                    }
                    else if (template == ReleasePdfTemplate.VeritasSchedule)
                    {
                        await PrepararVeritasValidacionAsync(documento, bytes, cn, tx);
                    }
                    else
                    {
                        await PrepararGenericoValidacionAsync(
                            documento,
                            bytes,
                            extension,
                            cn,
                            tx);
                    }
                }
                else if (extension == ".xlsx" ||
                         extension == ".xls" ||
                         extension == ".xlsm")
                {
                    ReleaseExcelDocument? parsed = null;

                    try
                    {
                        parsed = ReleaseExcelDocumentDetector.ParseGoldeMexico(
                            bytes,
                            documento.Archivo);
                    }
                    catch
                    {
                        parsed = null;
                    }

                    if (parsed == null)
                    {
                        var template = ReleaseExcelDocumentDetector.Detect(bytes);

                        parsed = template switch
                        {
                            ReleaseExcelTemplate.GoldeWeeklyMatrix =>
                                ReleaseExcelDocumentDetector.ParseGolde(
                                    bytes,
                                    documento.Archivo),

                            ReleaseExcelTemplate.NormaWeeklyMatrix =>
                                ReleaseExcelDocumentDetector.ParseNorma(bytes),

                            ReleaseExcelTemplate.AirThermalMaterialRelease =>
                                ReleaseExcelDocumentDetector.ParseAirThermal(
                                    bytes,
                                    documento.Archivo),

                            _ => null
                        };
                    }

                    if (parsed != null)
                    {
                        await PrepararExcelValidacionAsync(
                            documento,
                            parsed,
                            cn,
                            tx);
                    }
                    else
                    {
                        await PrepararGenericoValidacionAsync(
                            documento,
                            bytes,
                            extension,
                            cn,
                            tx);
                    }
                }
                else if (extension == ".csv")
                {
                    await PrepararGenericoValidacionAsync(
                        documento,
                        bytes,
                        extension,
                        cn,
                        tx);
                }
                else
                {
                    documento.Estado = ReleaseValidacionEstados.NoSoportado;
                    documento.Mensaje =
                        "Formato no soportado. Usa PDF, XLSX, XLS, XLSM o CSV.";
                }

                if (documento.ReleasePreparado != null &&
                    documento.ReleasePreparado.Renglones != null &&
                    documento.ReleasePreparado.Renglones.Count > 0)
                {
                    await HidratarVistaPreviaDesdeERPAsync(
                        documento.ReleasePreparado,
                        cn,
                        tx);
                }

                                // RELEASE_UPDATE_DETECT_V1_0
                await DetectarActualizacionExistenteValidacionAsync(
                    documento,
                    cn,
                    tx);

// Analizar nunca guarda cambios en SQL.
                await tx.RollbackAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }


        // GENERIC_RELEASE_ANALYZER_V1_1_END
private async Task PrepararHufValidacionAsync(
            ReleaseValidacionDocumentoVm documento,
            byte[] bytes,
            SqlConnection cn,
            SqlTransaction tx)
        {
            var parsed = HufReleasePdfParser.Parse(bytes, DateTime.Today);
            var clienteId = await ObtenerClienteHufParaImportacionAsync(cn, tx);

            if (!clienteId.HasValue)
                throw new InvalidOperationException("No existe un cliente activo compatible con HUF.");

            var clienteNombre = await ObtenerClienteNombreAsync(clienteId.Value, cn, tx)
                ?? parsed.ClienteNombre;

            var match = await BuscarParteImportacionIncluyendoInactivasAsync(
                parsed.PartNumber!, clienteId.Value, cn, tx);

            var parteId = match?.Activa == true ? match.ParteID : (int?)null;

            documento.Plantilla = "HUF_SUPPLIER_SCHEDULE";
            documento.FechaDocumento = parsed.DocumentDate;
            documento.ClienteID = clienteId.Value;
            documento.Cliente = clienteNombre;
            documento.FolioCliente = parsed.ScheduleNumber;
            documento.Version = parsed.VersionText;
            documento.TotalEntregas = parsed.Deliveries.Count;
            documento.TotalPiezas = parsed.Deliveries.Sum(x => x.RequiredQuantity);
            documento.Advertencias.AddRange(parsed.Warnings);

            documento.ReleasePreparado = new PlaneacionReleaseCrearVm
            {
                ClienteID = clienteId.Value,
                ClienteNombre = clienteNombre,
                FolioCliente = parsed.ScheduleNumber,
                FechaRecepcion = DateTime.Today,
                VersionRelease = parsed.VersionText,
                ArchivoOrigenNombre = documento.Archivo,
                PlantillaImportacion = documento.Plantilla,
                ImportadoDesdeArchivo = true,
                EstatusID = PlaneacionReleaseEstatus.Capturado,
                Renglones = new List<PlaneacionReleaseRenglonCrearVm>
                    {
                        new()
                        {
                            Renglon = 1,
                            ParteID = parteId,
                            NumeroParte = parsed.PartNumber,
                            ReferenciaSAP = parsed.PartNumber,
                            DesignacionDescripcionSAP = parsed.PartDescription,
                            UnidadMedidaCliente = parsed.Uom,
                            ContratoCliente = parsed.OrderNumber,
                            Entregas = parsed.Deliveries.Select(x => new PlaneacionReleaseEntregaCrearVm
                            {
                                SecuenciaEntrega = x.Sequence,
                                FechaCarga = x.LoadingDate,
                                FechaRequerida = x.RequiredDate,
                                CantidadRequerida = x.RequiredQuantity
                            }).ToList()
                        }
                    }
            };

            if (await ExisteDocumentoHufImportadoAsync(documento.Sha256, cn, tx))
            {
                documento.Estado = ReleaseValidacionEstados.Omitido;
                documento.Mensaje = "El mismo documento HUF ya fue importado.";
                return;
            }

            if (parteId.HasValue)
            {
                var fechaActiva = await ObtenerFechaVersionHufActivaAsync(
                    clienteId.Value, parteId.Value, cn, tx);

                if (parsed.DocumentDate.HasValue &&
                    fechaActiva.HasValue &&
                    parsed.DocumentDate.Value.Date < fechaActiva.Value.Date)
                {
                    documento.Estado = ReleaseValidacionEstados.Omitido;
                    documento.Mensaje = $"Documento anterior a la versión activa del {fechaActiva.Value:dd/MM/yyyy}.";
                    return;
                }
            }

            DefinirEstadoDocumentoPreparado(documento);
        }

        private async Task PrepararVeritasValidacionAsync(
            ReleaseValidacionDocumentoVm documento,
            byte[] bytes,
            SqlConnection cn,
            SqlTransaction tx)
        {
            var parsed = VeritasReleasePdfParser.Parse(bytes);
            var clienteId = await ObtenerClienteVeritasParaImportacionAsync(cn, tx);

            if (!clienteId.HasValue)
                throw new InvalidOperationException("No existe un cliente activo compatible con VERITAS.");

            var clienteNombre = await ObtenerClienteNombreAsync(clienteId.Value, cn, tx)
                ?? parsed.ClienteNombre;

            var groups = parsed.Deliveries
                .GroupBy(x => x.PartNumber, StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x.Key)
                .ToList();

            var vm = new PlaneacionReleaseCrearVm
            {
                ClienteID = clienteId.Value,
                ClienteNombre = clienteNombre,
                FolioCliente = parsed.ContractNumber,
                FechaRecepcion = DateTime.Today,
                VersionRelease = parsed.VersionText,
                ArchivoOrigenNombre = documento.Archivo,
                PlantillaImportacion = "VERITAS_SCHEDULE",
                ImportadoDesdeArchivo = true,
                EstatusID = PlaneacionReleaseEstatus.Capturado
            };

            var rowNumber = 1;
            foreach (var group in groups)
            {
                var first = group.First();
                var match = await BuscarParteImportacionIncluyendoInactivasAsync(
                    group.Key, clienteId.Value, cn, tx);

                vm.Renglones.Add(new PlaneacionReleaseRenglonCrearVm
                {
                    Renglon = rowNumber++,
                    ParteID = match?.Activa == true ? match.ParteID : null,
                    NumeroParte = group.Key,
                    ReferenciaSAP = group.Key,
                    DesignacionDescripcionSAP = first.PartDescription,
                    UnidadMedidaCliente = first.Uom,
                    ContratoCliente = parsed.ContractNumber,
                    Entregas = group
                        .OrderBy(x => x.RequiredDate)
                        .ThenBy(x => x.ItemNumber)
                        .Select((x, index) => new PlaneacionReleaseEntregaCrearVm
                        {
                            SecuenciaEntrega = index + 1,
                            FechaCarga = null,
                            FechaRequerida = x.RequiredDate,
                            CantidadRequerida = x.RequiredQuantity
                        }).ToList()
                });
            }

            documento.Plantilla = "VERITAS_SCHEDULE";
            documento.FechaDocumento = parsed.DocumentDate;
            documento.ClienteID = clienteId.Value;
            documento.Cliente = clienteNombre;
            documento.FolioCliente = parsed.ContractNumber;
            documento.Version = parsed.VersionText;
            documento.TotalEntregas = parsed.Deliveries.Count;
            documento.TotalPiezas = parsed.Deliveries.Sum(x => x.RequiredQuantity);
            documento.Advertencias.AddRange(parsed.Warnings);
            documento.ReleasePreparado = vm;

            if (await ExisteDocumentoImportadoAsync(documento.Plantilla, documento.Sha256, cn, tx))
            {
                documento.Estado = ReleaseValidacionEstados.Omitido;
                documento.Mensaje = "El mismo documento VERITAS ya fue importado.";
                return;
            }

            if (vm.Renglones.All(x => x.ParteID.HasValue))
            {
                var fechaActiva = await ObtenerFechaVersionVeritasActivaAsync(
                    clienteId.Value, parsed.ContractNumber!, cn, tx);

                if (parsed.DocumentDate.HasValue &&
                    fechaActiva.HasValue &&
                    parsed.DocumentDate.Value.Date < fechaActiva.Value.Date)
                {
                    documento.Estado = ReleaseValidacionEstados.Omitido;
                    documento.Mensaje = $"Documento anterior a la versión activa del {fechaActiva.Value:dd/MM/yyyy}.";
                    return;
                }
            }

            DefinirEstadoDocumentoPreparado(documento);
        }

        private async Task PrepararExcelValidacionAsync(
            ReleaseValidacionDocumentoVm documento,
            ReleaseExcelDocument parsed,
            SqlConnection cn,
            SqlTransaction tx)
        {
            var clienteId = await ObtenerClienteExcelParaImportacionAsync(parsed.TemplateCode, cn, tx);
            if (!clienteId.HasValue)
                throw new InvalidOperationException($"No existe un cliente activo compatible con {parsed.ClienteNombre}.");

            var clienteNombre = await ObtenerClienteNombreAsync(clienteId.Value, cn, tx)
                ?? parsed.ClienteNombre;

            var vm = new PlaneacionReleaseCrearVm
            {
                ClienteID = clienteId.Value,
                ClienteNombre = clienteNombre,
                FolioCliente = parsed.FolioCliente,
                FechaRecepcion = DateTime.Today,
                VersionRelease = parsed.VersionText,
                ArchivoOrigenNombre = documento.Archivo,
                PlantillaImportacion = parsed.TemplateCode,
                ImportadoDesdeArchivo = true,
                EstatusID = PlaneacionReleaseEstatus.Capturado
            };

            var rowNumber = 1;
            foreach (var sourceRow in parsed.Rows)
            {
                var match = parsed.TemplateCode == "AIR_THERMAL_MATERIAL_RELEASE"
                    ? await BuscarParteAirThermalIncluyendoRevisionesAsync(
                        sourceRow.PartNumber, clienteId.Value, cn, tx)
                    : EsPlantillaGolde(parsed.TemplateCode)
                        ? await BuscarParteGoldeIncluyendoRevisionesAsync(
                            sourceRow.PartNumber, clienteId.Value, cn, tx)
                        : await BuscarParteImportacionIncluyendoInactivasAsync(
                            sourceRow.PartNumber, clienteId.Value, cn, tx);

                vm.Renglones.Add(new PlaneacionReleaseRenglonCrearVm
                {
                    Renglon = rowNumber++,
                    ParteID = match?.Activa == true ? match.ParteID : null,
                    NumeroParte = sourceRow.PartNumber,
                    ReferenciaSAP = sourceRow.PartNumber,
                    DesignacionDescripcionSAP = sourceRow.PartDescription,
                    UnidadMedidaCliente = sourceRow.Uom,
                    ContratoCliente = sourceRow.SourceReference,
                    Entregas = sourceRow.Deliveries
                        .OrderBy(x => x.RequiredDate)
                        .ThenBy(x => x.Sequence)
                        .Select((x, index) => new PlaneacionReleaseEntregaCrearVm
                        {
                            SecuenciaEntrega = index + 1,
                            FechaCarga = null,
                            FechaRequerida = x.RequiredDate,
                            CantidadRequerida = x.RequiredQuantity
                        }).ToList()
                });
            }

            documento.Plantilla = parsed.TemplateCode;
            documento.FechaDocumento = parsed.DocumentDate;
            documento.ClienteID = clienteId.Value;
            documento.Cliente = clienteNombre;
            documento.FolioCliente = parsed.FolioCliente;
            documento.Version = parsed.VersionText;
            documento.TotalEntregas = parsed.Rows.Sum(x => x.Deliveries.Count);
            documento.TotalPiezas = parsed.Rows.Sum(x => x.Deliveries.Sum(d => d.RequiredQuantity));
            documento.Advertencias.AddRange(parsed.Warnings);
            documento.ReleasePreparado = vm;

            if (await ExisteDocumentoImportadoAsync(documento.Plantilla, documento.Sha256, cn, tx))
            {
                documento.Estado = ReleaseValidacionEstados.Omitido;
                documento.Mensaje = "El mismo documento Excel ya fue importado.";
                return;
            }

            DefinirEstadoDocumentoPreparado(documento);
        }

        private static void DefinirEstadoDocumentoPreparado(ReleaseValidacionDocumentoVm documento)
        {
            var pendientes = documento.ReleasePreparado.Renglones.Count(x => !x.ParteID.HasValue);

            if (pendientes > 0)
            {
                documento.Estado = ReleaseValidacionEstados.Pendiente;
                documento.Mensaje = $"Falta validar {pendientes} número(s) de parte.";
            }
            else
            {
                documento.Estado = ReleaseValidacionEstados.Validado;
                documento.Mensaje = "Documento validado y listo para guardar.";
            }
        }

        private async Task<(int ParteID, string NumeroParte, string ReferenciaSAP, string Descripcion)?>
            BuscarParteActivaValidacionAsync(string numeroParte, int clienteId)
        {
            if (string.IsNullOrWhiteSpace(numeroParte))
                return null;

            const string sql = @"
        DECLARE @Normalizada NVARCHAR(150) = UPPER(
            REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(@Referencia)), '-', ''), '.', ''), ' ', ''), '/', '')
        );

        SELECT TOP (1)
            ParteID,
            NumeroParte,
            ISNULL(ReferenciaSAP, NumeroParte) AS ReferenciaSAP,
            COALESCE(NULLIF(Designacion, ''), NULLIF(Descripcion, ''), NumeroParte) AS Descripcion
        FROM dbo.ERP_Partes
        WHERE Activo = 1
          AND ClienteID = @ClienteID
          AND
          (
                UPPER(LTRIM(RTRIM(ISNULL(NumeroParte, '')))) = UPPER(LTRIM(RTRIM(@Referencia)))
             OR UPPER(LTRIM(RTRIM(ISNULL(ReferenciaSAP, '')))) = UPPER(LTRIM(RTRIM(@Referencia)))
             OR UPPER(REPLACE(REPLACE(REPLACE(REPLACE(ISNULL(NumeroParte, ''), '-', ''), '.', ''), ' ', ''), '/', '')) = @Normalizada
             OR UPPER(REPLACE(REPLACE(REPLACE(REPLACE(ISNULL(ReferenciaSAP, ''), '-', ''), '.', ''), ' ', ''), '/', '')) = @Normalizada
          )
        ORDER BY
            CASE
                WHEN UPPER(LTRIM(RTRIM(ISNULL(NumeroParte, '')))) = UPPER(LTRIM(RTRIM(@Referencia))) THEN 0
                WHEN UPPER(LTRIM(RTRIM(ISNULL(ReferenciaSAP, '')))) = UPPER(LTRIM(RTRIM(@Referencia))) THEN 1
                ELSE 2
            END,
            ParteID;";

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@Referencia", SqlDbType.NVarChar, 150).Value = numeroParte.Trim();
            cmd.Parameters.Add("@ClienteID", SqlDbType.Int).Value = clienteId;

            await using var rd = await cmd.ExecuteReaderAsync();
            if (!await rd.ReadAsync())
                return null;

            return (
                Convert.ToInt32(rd["ParteID"]),
                rd["NumeroParte"] as string ?? numeroParte.Trim(),
                rd["ReferenciaSAP"] as string ?? numeroParte.Trim(),
                rd["Descripcion"] as string ?? numeroParte.Trim());
        }

        private string ObtenerRutaRaizValidaciones()
        {
            return Path.Combine(
                _environment.ContentRootPath,
                "App_Data",
                "Releases",
                "PendientesValidar");
        }

        private string ObtenerRutaLoteValidacion(string loteId)
        {
            var safe = new string((loteId ?? string.Empty)
                .Where(char.IsLetterOrDigit)
                .Take(64)
                .ToArray());

            if (string.IsNullOrWhiteSpace(safe))
                throw new InvalidOperationException("Identificador de lote inválido.");

            return Path.Combine(ObtenerRutaRaizValidaciones(), safe);
        }

        private async Task GuardarLoteValidacionAsync(ReleaseValidacionLoteVm lote)
        {
            var root = ObtenerRutaLoteValidacion(lote.LoteID);
            Directory.CreateDirectory(root);

            var json = JsonSerializer.Serialize(lote, ReleaseValidacionJsonOptions);
            await System.IO.File.WriteAllTextAsync(Path.Combine(root, "lote.json"), json);
        }

        private async Task<ReleaseValidacionLoteVm?> CargarLoteValidacionAsync(string loteId)
        {
            if (string.IsNullOrWhiteSpace(loteId))
                return null;

            var path = Path.Combine(ObtenerRutaLoteValidacion(loteId), "lote.json");
            if (!System.IO.File.Exists(path))
                return null;

            var json = await System.IO.File.ReadAllTextAsync(path);
            return JsonSerializer.Deserialize<ReleaseValidacionLoteVm>(
                json,
                ReleaseValidacionJsonOptions);
        }



        // ============================================================
        // ORIGEN: PlaneacionReleaseFechaCargaController.cs
        // VERSION: RELEASE_FECHA_CARGA_AUTO_V1_0
        // FUNCION: Completar FechaCarga en entregas importadas cuando
        // no existe, usando un dia calendario antes de FechaRequerida.
        // ============================================================

        private async Task<int> AutocompletarFechasCargaImportadasAsync()
        {
            const string sql = @"
UPDATE detalle
SET detalle.FechaCarga =
    DATEADD(DAY, -1, CONVERT(DATE, detalle.FechaRequerida))
FROM dbo.Planeacion_ReleaseDetalle detalle
INNER JOIN dbo.Planeacion_Releases release
    ON release.ReleaseID = detalle.ReleaseID
WHERE release.Activo = 1
  AND detalle.Activo = 1
  AND ISNULL(release.ImportadoDesdeArchivo, 0) = 1
  AND detalle.FechaCarga IS NULL
  AND detalle.FechaRequerida IS NOT NULL;

SELECT @@ROWCOUNT;";

            await using var cn =
                new SqlConnection(ConnectionString);

            await cn.OpenAsync();

            await using var cmd =
                new SqlCommand(sql, cn);

            return Convert.ToInt32(
                await cmd.ExecuteScalarAsync());
        }


    }
}
