// RELEASE_VALIDACION_LOTES_V2_0
using ERP.NSQuell.Models;
using ERP.NSQuell.Servicios.Releases;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Security.Cryptography;
using System.Text.Json;

namespace ERP.NSQuell.Controllers;

public partial class PlaneacionReleaseController
{
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

                vm.FolioRelease = await GenerarFolioReleaseAsync(cn, tx);
                vm.ArchivoOrigenNombre = documento.Archivo;
                vm.PlantillaImportacion = documento.Plantilla;
                vm.ImportadoDesdeArchivo = true;
                vm.FechaRecepcion = DateTime.Today;
                vm.EstatusID = PlaneacionReleaseEstatus.Capturado;
                vm.Observaciones =
                    $"IMPORTACION_VALIDADA;PLANTILLA:{documento.Plantilla};" +
                    $"SHA256:{documento.Sha256};ARCHIVO:{archivoGuardado};LOTE:{lote.LoteID}";

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
                else if (documento.Plantilla == "GOLDEN_WEEKLY_RELEASE" ||
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
                    await PrepararHufValidacionAsync(documento, bytes, cn, tx);
                else if (template == ReleasePdfTemplate.VeritasSchedule)
                    await PrepararVeritasValidacionAsync(documento, bytes, cn, tx);
                else
                {
                    documento.Estado = ReleaseValidacionEstados.NoSoportado;
                    documento.Mensaje = "PDF no soportado. Se reconocen HUF y VERITAS.";
                }
            }
            else if (extension == ".xlsx" || extension == ".xls" || extension == ".xlsm")
            {
                var template = ReleaseExcelDocumentDetector.Detect(bytes);

                ReleaseExcelDocument? parsed = template switch
                {
                    ReleaseExcelTemplate.GoldenWeeklyMatrix => ReleaseExcelDocumentDetector.ParseGolden(bytes),
                    ReleaseExcelTemplate.NormaWeeklyMatrix => ReleaseExcelDocumentDetector.ParseNorma(bytes),
                    ReleaseExcelTemplate.AirThermalMaterialRelease => ReleaseExcelDocumentDetector.ParseAirThermal(bytes, documento.Archivo),
                    _ => null
                };

                if (parsed == null)
                {
                    documento.Estado = ReleaseValidacionEstados.NoSoportado;
                    documento.Mensaje = "Excel no soportado. Se reconocen GOLDEN, NORMA y AIR THERMAL.";
                }
                else
                {
                    await PrepararExcelValidacionAsync(documento, parsed, cn, tx);
                }
            }
            else
            {
                documento.Estado = ReleaseValidacionEstados.NoSoportado;
                documento.Mensaje = "Formato no soportado. Usa PDF, XLSX, XLS o XLSM.";
            }

            await tx.RollbackAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

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
}