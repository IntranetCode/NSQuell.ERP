// RELEASE_GOLDE_VALIDACION_V1_0_4
using ERP.NSQuell.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;

namespace ERP.NSQuell.Controllers;

public partial class PlaneacionReleaseController
{
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RevalidarLoteGolde(
        string loteId)
    {
        var lote =
            await CargarLoteValidacionAsync(loteId);

        if (lote == null)
        {
            return NotFound(new
            {
                ok = false,
                mensaje = "No se encontró el lote."
            });
        }

        var actualizadas =
            await RevalidarPendientesGoldeAsync(lote);

        if (actualizadas > 0)
        {
            await GuardarLoteValidacionAsync(lote);
        }

        return Json(new
        {
            ok = true,
            actualizadas,
            pendientes = lote.Pendientes
        });
    }

    [HttpGet]
    public async Task<IActionResult> ObtenerPartesValidacion(
        int clienteId)
    {
        if (clienteId <= 0)
        {
            return BadRequest(new
            {
                ok = false,
                mensaje = "Cliente inválido."
            });
        }

        const string sql = @"
SELECT
    ParteID,
    NumeroParte,
    ISNULL(
        NULLIF(ReferenciaSAP, ''),
        NumeroParte
    ) AS ReferenciaSAP,
    COALESCE(
        NULLIF(Designacion, ''),
        NULLIF(Descripcion, ''),
        NumeroParte
    ) AS Designacion
FROM dbo.ERP_Partes
WHERE ClienteID = @ClienteID
  AND Activo = 1
ORDER BY
    NumeroParte,
    ParteID;";

        await using var cn =
            new SqlConnection(ConnectionString);

        await cn.OpenAsync();

        await using var cmd =
            new SqlCommand(sql, cn);

        cmd.Parameters.Add(
            "@ClienteID",
            SqlDbType.Int).Value =
            clienteId;

        var partes =
            new List<object>();

        await using var rd =
            await cmd.ExecuteReaderAsync();

        while (await rd.ReadAsync())
        {
            var numero =
                rd["NumeroParte"] as string ??
                string.Empty;

            var referencia =
                rd["ReferenciaSAP"] as string ??
                numero;

            var designacion =
                rd["Designacion"] as string ??
                numero;

            var texto = numero;

            if (!string.Equals(
                referencia,
                numero,
                StringComparison.OrdinalIgnoreCase))
            {
                texto += " | " + referencia;
            }

            texto += " | " + designacion;

            partes.Add(new
            {
                value = numero,
                text = texto
            });
        }

        return Json(new
        {
            ok = true,
            partes
        });
    }

    private async Task<int> RevalidarPendientesGoldeAsync(
        ReleaseValidacionLoteVm lote)
    {
        var documentos =
            lote.Documentos
                .Where(x =>
                    x.Estado ==
                        ReleaseValidacionEstados.Pendiente &&
                    (
                        string.Equals(
                            x.Plantilla,
                            "GOLDEN_WEEKLY_RELEASE",
                            StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(
                            x.Plantilla,
                            "GOLDE_MEXICO_WEEKLY_RELEASE",
                            StringComparison.OrdinalIgnoreCase)
                    ) &&
                    x.ClienteID.HasValue)
                .ToList();

        if (documentos.Count == 0)
            return 0;

        var total = 0;

        await using var cn =
            new SqlConnection(ConnectionString);

        await cn.OpenAsync();

        await using var tx =
            (SqlTransaction)
            await cn.BeginTransactionAsync();

        try
        {
            foreach (var documento in documentos)
            {
                foreach (var renglon in
                    documento.ReleasePreparado.Renglones
                        .Where(x => !x.ParteID.HasValue))
                {
                    var referencia =
                        renglon.NumeroParte ??
                        renglon.ReferenciaSAP;

                    if (string.IsNullOrWhiteSpace(referencia))
                        continue;

                    var match =
                        await BuscarParteGoldeSeguraAsync( // NSQ_GOLDE_SAFE_MATCH_CALL_V1
                            referencia,
                            renglon.DesignacionDescripcionSAP,
                            documento.ClienteID!.Value,
                            cn,
                            tx);

                    if (match?.Activa != true)
                        continue;

                    const string sqlParte = @"
SELECT TOP (1)
    ParteID,
    NumeroParte,
    ISNULL(
        NULLIF(ReferenciaSAP, ''),
        NumeroParte
    ) AS ReferenciaSAP,
    COALESCE(
        NULLIF(Designacion, ''),
        NULLIF(Descripcion, ''),
        NumeroParte
    ) AS Descripcion
FROM dbo.ERP_Partes
WHERE ParteID = @ParteID
  AND ClienteID = @ClienteID
  AND Activo = 1;";

                    await using var cmd =
                        new SqlCommand(
                            sqlParte,
                            cn,
                            tx);

                    cmd.Parameters.Add(
                        "@ParteID",
                        SqlDbType.Int).Value =
                        match.ParteID;

                    cmd.Parameters.Add(
                        "@ClienteID",
                        SqlDbType.Int).Value =
                        documento.ClienteID.Value;

                    await using var rd =
                        await cmd.ExecuteReaderAsync();

                    if (!await rd.ReadAsync())
                        continue;

                    renglon.ParteID =
                        Convert.ToInt32(
                            rd["ParteID"]);

                    renglon.NumeroParte =
                        rd["NumeroParte"] as string ??
                        referencia;

                    renglon.ReferenciaSAP =
                        rd["ReferenciaSAP"] as string ??
                        renglon.NumeroParte;

                    renglon.DesignacionDescripcionSAP =
                        rd["Descripcion"] as string ??
                        renglon.ReferenciaSAP;

                    total++;
                }

                DefinirEstadoDocumentoPreparado(
                    documento);
            }

            await tx.RollbackAsync();
            return total;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    private async Task<ParteImportacionMatch?>
        BuscarParteGoldeAsync(
            string referencia,
            int clienteId,
            SqlConnection cn,
            SqlTransaction tx)
    {
        if (string.IsNullOrWhiteSpace(referencia))
            return null;

        var exacta =
            await BuscarParteImportacionIncluyendoInactivasAsync(
                referencia,
                clienteId,
                cn,
                tx);

        if (exacta?.Activa == true)
            return exacta;

        var referenciaBase =
            QuitarRevisionGolde(referencia);

        if (string.Equals(
            referenciaBase,
            referencia.Trim(),
            StringComparison.OrdinalIgnoreCase))
        {
            return exacta;
        }

        var porBase =
            await BuscarParteImportacionIncluyendoInactivasAsync(
                referenciaBase,
                clienteId,
                cn,
                tx);

        if (porBase?.Activa == true)
            return porBase;

        return exacta ?? porBase;
    }

    private static string QuitarRevisionGolde(
        string referencia)
    {
        var value = referencia.Trim();
        var position = value.LastIndexOf('_');

        if (position <= 0 ||
            position >= value.Length - 1)
        {
            return value;
        }

        var suffix =
            value[(position + 1)..];

        if (suffix.Length is < 1 or > 3 ||
            !suffix.All(char.IsDigit))
        {
            return value;
        }

        return value[..position];
    }
}