using ERP.NSQuell.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;

namespace ERP.NSQuell.Controllers;

public partial class PlaneacionReleaseController
{
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

            var numero = Texto(source.NumeroParte, 150);
            var referencia = Texto(source.ReferenciaSAP, 150);
            var descripcion = Texto(source.DesignacionDescripcionSAP, 300);
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
                UnidadMedidaCliente = Texto(source.UnidadMedidaCliente, 100),
                ContratoCliente = Texto(source.ContratoCliente, 150),
                Observaciones = Texto(source.Observaciones, 1000),
                Entregas = entregas
            });
        }

        var preparado = documento.ReleasePreparado ?? new PlaneacionReleaseCrearVm();

        preparado.ClienteID = request.ClienteID.Value;
        preparado.ClienteNombre = clienteNombre;
        preparado.FolioCliente = Texto(request.FolioCliente, 150);
        preparado.VersionRelease = Texto(request.Version, 100);
        preparado.FechaRecepcion =
            request.FechaRecepcion == default ? DateTime.Today : request.FechaRecepcion.Date;
        preparado.Observaciones = Texto(request.Observaciones, 2000);
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

    private static string? Texto(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var text = value.Trim();
        return text.Length <= max ? text : text[..max];
    }
}