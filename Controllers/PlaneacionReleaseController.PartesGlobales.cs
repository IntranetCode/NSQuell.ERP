using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;

namespace ERP.NSQuell.Controllers
{
    public partial class PlaneacionReleaseController
    {
        // NSQ_RELEASE_PARTES_GLOBALES_V1
        // Captura manual:
        // - El cliente sigue perteneciendo al Release.
        // - La Parte NO queda restringida al ClienteID del Release.
        // - Las partes del cliente seleccionado se muestran primero.
        [HttpGet]
        public async Task<IActionResult> ObtenerPartesDisponibles(int? clienteId = null)
        {
            var partes = new List<object>();

            const string sql = @"
SELECT
    p.ParteID,
    p.ClienteID,
    ISNULL(c.Nombre, N'SIN CLIENTE') AS ClienteNombre,
    ISNULL(NULLIF(LTRIM(RTRIM(p.NumeroParte)), N''), CONVERT(NVARCHAR(30), p.ParteID)) AS NumeroParte,
    NULLIF(LTRIM(RTRIM(p.ReferenciaSAP)), N'') AS ReferenciaSAP,
    COALESCE(
        NULLIF(LTRIM(RTRIM(p.Designacion)), N''),
        NULLIF(LTRIM(RTRIM(p.Descripcion)), N''),
        N'Sin descripción'
    ) AS DescripcionParte
FROM dbo.ERP_Partes p
LEFT JOIN dbo.ERP_Clientes c
    ON c.ClienteID = p.ClienteID
WHERE p.Activo = 1
ORDER BY
    CASE
        WHEN @ClienteID IS NOT NULL AND p.ClienteID = @ClienteID THEN 0
        ELSE 1
    END,
    ISNULL(c.Nombre, N''),
    ISNULL(NULLIF(p.NumeroParte, N''), p.ReferenciaSAP),
    p.ParteID;";

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@ClienteID", SqlDbType.Int).Value =
                (object?)clienteId ?? DBNull.Value;

            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                var parteId = Convert.ToInt32(rd["ParteID"]);
                var parteClienteId = rd["ClienteID"] == DBNull.Value
                    ? (int?)null
                    : Convert.ToInt32(rd["ClienteID"]);

                var numeroParte = Convert.ToString(rd["NumeroParte"]) ?? parteId.ToString();
                var referencia = rd["ReferenciaSAP"] == DBNull.Value
                    ? null
                    : Convert.ToString(rd["ReferenciaSAP"]);
                var descripcion = Convert.ToString(rd["DescripcionParte"]) ?? "Sin descripción";
                var clienteNombre = Convert.ToString(rd["ClienteNombre"]) ?? "SIN CLIENTE";

                var texto = numeroParte;

                if (!string.IsNullOrWhiteSpace(referencia) &&
                    !string.Equals(numeroParte, referencia, StringComparison.OrdinalIgnoreCase))
                {
                    texto += " | " + referencia;
                }

                texto += " | " + descripcion;
                texto += " | Cliente: " + clienteNombre;

                partes.Add(new
                {
                    value = parteId,
                    text = texto,
                    clienteId = parteClienteId,
                    clienteNombre,
                    mismoCliente = clienteId.HasValue &&
                                   parteClienteId.HasValue &&
                                   clienteId.Value == parteClienteId.Value
                });
            }

            return Json(new
            {
                ok = true,
                partes,
                total = partes.Count,
                clientePrioritarioId = clienteId
            });
        }
    }
}
