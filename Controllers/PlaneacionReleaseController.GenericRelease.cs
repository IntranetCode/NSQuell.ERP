using ERP.NSQuell.Models;
using ERP.NSQuell.Servicios.Releases;
using Microsoft.Data.SqlClient;
using System.Data;

namespace ERP.NSQuell.Controllers;

// GENERIC_RELEASE_CONTROLLER_V1_0
public partial class PlaneacionReleaseController
{
    private async Task<List<GenericReleaseKnownPart>> CargarCatalogoPartesGenericoAsync(
        SqlConnection cn,
        SqlTransaction tx)
    {
        const string sql = @"
SELECT
    p.ParteID,
    p.ClienteID,
    c.Nombre AS ClienteNombre,
    p.NumeroParte,
    p.ReferenciaSAP,
    p.Descripcion,
    p.Designacion
FROM dbo.ERP_Partes p
INNER JOIN dbo.ERP_Clientes c
    ON c.ClienteID = p.ClienteID
WHERE p.Activo = 1
  AND c.Activo = 1
  AND
  (
       NULLIF(LTRIM(RTRIM(ISNULL(p.NumeroParte, ''))), '') IS NOT NULL
       OR NULLIF(LTRIM(RTRIM(ISNULL(p.ReferenciaSAP, ''))), '') IS NOT NULL
  )
ORDER BY p.ClienteID, p.ParteID;";

        var result = new List<GenericReleaseKnownPart>();

        await using var cmd = new SqlCommand(sql, cn, tx);
        await using var rd = await cmd.ExecuteReaderAsync();

        while (await rd.ReadAsync())
        {
            result.Add(new GenericReleaseKnownPart
            {
                ParteID = Convert.ToInt32(rd["ParteID"]),
                ClienteID = Convert.ToInt32(rd["ClienteID"]),
                ClienteNombre = rd["ClienteNombre"] as string ?? string.Empty,
                NumeroParte = rd["NumeroParte"] as string ?? string.Empty,
                ReferenciaSAP = rd["ReferenciaSAP"] as string,
                Descripcion = rd["Descripcion"] as string,
                Designacion = rd["Designacion"] as string
            });
        }

        return result;
    }

    private async Task PrepararGenericoValidacionAsync(
        ReleaseValidacionDocumentoVm documento,
        byte[] bytes,
        string extension,
        SqlConnection cn,
        SqlTransaction tx)
    {
        var catalog = await CargarCatalogoPartesGenericoAsync(cn, tx);

        var parsed = GenericReleaseParser.Parse(
            bytes,
            extension,
            documento.Archivo,
            catalog);

        var vm = new PlaneacionReleaseCrearVm
        {
            ClienteID = parsed.ClienteID,
            ClienteNombre = parsed.ClienteNombre,
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
            var masterDescription =
                !string.IsNullOrWhiteSpace(sourceRow.Part.Designacion)
                    ? sourceRow.Part.Designacion
                    : sourceRow.Part.Descripcion;

            vm.Renglones.Add(new PlaneacionReleaseRenglonCrearVm
            {
                Renglon = rowNumber++,
                ParteID = sourceRow.Part.ParteID,

                // IMPORTANTE:
                // La identidad de la parte sale del maestro ERP,
                // no de como la escribio el cliente en el documento.
                NumeroParte = sourceRow.Part.NumeroParte,
                ReferenciaSAP = !string.IsNullOrWhiteSpace(sourceRow.Part.ReferenciaSAP)
                    ? sourceRow.Part.ReferenciaSAP
                    : sourceRow.Part.NumeroParte,
                DesignacionDescripcionSAP = masterDescription,
                UnidadMedidaCliente = "PIEZA",
                ContratoCliente = sourceRow.SourceReference ?? parsed.FolioCliente,
                Observaciones =
                    $"Lector generico. Token fuente: {sourceRow.SourceToken}. Parte enlazada al maestro ERP.",
                Entregas = sourceRow.Deliveries
                    .OrderBy(x => x.RequiredDate)
                    .ThenBy(x => x.Sequence)
                    .Select((x, index) => new PlaneacionReleaseEntregaCrearVm
                    {
                        SecuenciaEntrega = index + 1,
                        FechaCarga = null,
                        FechaRequerida = x.RequiredDate.Date,
                        CantidadRequerida = x.RequiredQuantity
                    })
                    .ToList()
            });
        }

        documento.Plantilla = parsed.TemplateCode;
        documento.FechaDocumento = parsed.DocumentDate;
        documento.ClienteID = parsed.ClienteID;
        documento.Cliente = parsed.ClienteNombre;
        documento.FolioCliente = parsed.FolioCliente;
        documento.Version = parsed.VersionText;
        documento.TotalEntregas = parsed.Rows.Sum(x => x.Deliveries.Count);
        documento.TotalPiezas = parsed.Rows.Sum(x => x.Deliveries.Sum(d => d.RequiredQuantity));
        documento.Advertencias.AddRange(parsed.Warnings);
        documento.ReleasePreparado = vm;

        if (await ExisteDocumentoImportadoAsync(
                parsed.TemplateCode,
                documento.Sha256,
                cn,
                tx))
        {
            documento.Estado = ReleaseValidacionEstados.Omitido;
            documento.Mensaje = "El mismo documento generico ya fue importado.";
            return;
        }

        DefinirEstadoDocumentoPreparado(documento);
    }

    // Sobrescribe en la vista previa NumeroParte / SAP / Designacion con ERP_Partes.
    // Esto evita que un PDF diga F23388C-A y termine mostrando ese mismo texto
    // como si tambien fuera la ReferenciaSAP cuando en ERP es 5VER-SG-N.
    private static async Task HidratarVistaPreviaDesdeERPAsync(
        PlaneacionReleaseCrearVm vm,
        SqlConnection cn,
        SqlTransaction tx)
    {
        if (vm?.Renglones == null || vm.Renglones.Count == 0)
            return;

        const string sql = @"
SELECT
    ParteID,
    NumeroParte,
    COALESCE(NULLIF(ReferenciaSAP, ''), NumeroParte) AS ReferenciaSAP,
    COALESCE(NULLIF(Designacion, ''), NULLIF(Descripcion, ''), NumeroParte) AS Descripcion
FROM dbo.ERP_Partes
WHERE ParteID = @ParteID
  AND Activo = 1;";

        foreach (var row in vm.Renglones.Where(x => x.ParteID.HasValue))
        {
            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value = row.ParteID!.Value;

            await using var rd = await cmd.ExecuteReaderAsync();
            if (!await rd.ReadAsync())
                continue;

            row.NumeroParte = rd["NumeroParte"] as string ?? row.NumeroParte;
            row.ReferenciaSAP = rd["ReferenciaSAP"] as string ?? row.NumeroParte;
            row.DesignacionDescripcionSAP =
                rd["Descripcion"] as string ?? row.DesignacionDescripcionSAP;

            // La unidad operativa del ERP es PIEZA.
            row.UnidadMedidaCliente = "PIEZA";
        }
    }
}