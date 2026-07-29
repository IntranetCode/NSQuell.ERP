using ERP.NSQuell.Models;
using ERP.NSQuell.Models.ViewModels.Almacen;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;

namespace ERP.NSQuell.Controllers;

public sealed partial class AlmacenOFController
{
    [HttpGet]
    public async Task<IActionResult> Detalle(
        int id,
        CancellationToken cancellationToken = default)
    {
        var sesion = ValidarSesion();
        if (sesion != null)
            return sesion;

        if (id <= 0)
            return NotFound();

        await using var connection = await AbrirConexionAsync(cancellationToken);

        var vm = await CargarEncabezadoAsync(connection, id, cancellationToken);
        if (vm == null)
            return NotFound();

        await CargarRenglonesAsync(connection, vm, cancellationToken);
        await CargarEntregasAsync(connection, vm, cancellationToken);

        return View("~/Views/AlmacenOF/Detalle.cshtml", vm);
    }

    private static async Task<AlmacenOFDetalleVm?> CargarEncabezadoAsync(
        SqlConnection connection,
        int id,
        CancellationToken cancellationToken)
    {
        var machineHeader = await PrimeraColumnaExistenteAsync(
            connection,
            "dbo.SolicitudesProduccion",
            new[] { "MaquinaNombre", "NombreMaquina", "MaquinaCodigo", "Maquina", "MaquinaID" },
            cancellationToken);

        var machineDetail = machineHeader == null
            ? await PrimeraColumnaExistenteAsync(
                connection,
                "dbo.SolicitudesProduccionDetalle",
                new[] { "MaquinaNombre", "NombreMaquina", "MaquinaCodigo", "Maquina", "MaquinaID" },
                cancellationToken)
            : null;

        var machineExpression = "N''";
        if (machineHeader != null)
        {
            machineExpression =
                $"ISNULL(NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(200), s.{OfIdentificador(machineHeader)}))), N''), N'')";
        }
        else if (machineDetail != null)
        {
            machineExpression = $@"
ISNULL
(
    (
        SELECT TOP (1)
            NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(200), dx.{OfIdentificador(machineDetail)}))), N'')
        FROM dbo.SolicitudesProduccionDetalle dx
        WHERE dx.SolicitudProduccionID = s.SolicitudProduccionID
          AND dx.Activo = 1
          AND dx.{OfIdentificador(machineDetail)} IS NOT NULL
        ORDER BY dx.Renglon
    ),
    N''
)";
        }

        var sql = $@"
SELECT TOP (1)
    s.SolicitudProduccionID,
    ISNULL(s.FolioSolicitud, N'') AS FolioSolicitud,
    ISNULL(s.NumeroOFRecibida, N'') AS NumeroOFRecibida,
    COALESCE
    (
        NULLIF(LTRIM(RTRIM(s.NumeroOFRecibida)), N''),
        NULLIF(LTRIM(RTRIM(s.FolioSolicitud)), N''),
        CONCAT(N'OF-ID-', s.SolicitudProduccionID)
    ) AS NumeroOFClave,
    ISNULL(NULLIF(LTRIM(RTRIM(c.Nombre)), N''), ISNULL(s.ClienteNombre, N'')) AS Cliente,
    {machineExpression} AS Maquina,
    ISNULL(NULLIF(LTRIM(RTRIM(s.Prioridad)), N''), N'Normal') AS Prioridad,
    s.EstatusID,
    ISNULL(s.ResponsablePlaneacionNombre, N'') AS ResponsablePlaneacion,
    s.FechaSolicitud,
    s.FechaRequerida,
    s.FechaInicioPlaneada,
    s.FechaFinPlaneada
FROM dbo.SolicitudesProduccion s
LEFT JOIN dbo.ERP_Clientes c
    ON c.ClienteID = s.ClienteID
WHERE s.SolicitudProduccionID = @Id
  AND s.Activo = 1;";

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Id", SqlDbType.Int).Value = id;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        var estatusId = LeerEnteroOf(reader, "EstatusID");
        return new AlmacenOFDetalleVm
        {
            SolicitudProduccionID = LeerEnteroOf(reader, "SolicitudProduccionID"),
            FolioSolicitud = LeerTextoOf(reader, "FolioSolicitud"),
            NumeroOFRecibida = LeerTextoOf(reader, "NumeroOFRecibida"),
            NumeroOFClave = LeerTextoOf(reader, "NumeroOFClave"),
            Cliente = LeerTextoOf(reader, "Cliente"),
            Maquina = LeerTextoOf(reader, "Maquina"),
            Prioridad = LeerTextoOf(reader, "Prioridad"),
            EstatusID = estatusId,
            EstatusNombre = PlaneacionOFEstatus.Nombre(estatusId),
            ResponsablePlaneacion = LeerTextoOf(reader, "ResponsablePlaneacion"),
            FechaSolicitud = LeerFechaOf(reader, "FechaSolicitud") ?? DateTime.MinValue,
            FechaRequerida = LeerFechaOf(reader, "FechaRequerida"),
            FechaInicioPlaneada = LeerFechaOf(reader, "FechaInicioPlaneada"),
            FechaFinPlaneada = LeerFechaOf(reader, "FechaFinPlaneada")
        };
    }

    private static async Task CargarRenglonesAsync(
        SqlConnection connection,
        AlmacenOFDetalleVm vm,
        CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT
    d.Renglon,
    ISNULL(d.ReferenciaSAP, N'') AS NumeroParte,
    ISNULL(d.DesignacionDescripcionSAP, N'') AS DescripcionParte,
    CONVERT(decimal(18,4), ISNULL(d.CantidadPiezas, 0)) AS CantidadPiezas,
    ISNULL(m.MaterialID, 0) AS MaterialID,
    ISNULL(d.MaterialCodigo, N'') AS MaterialCodigo,
    ISNULL(d.MaterialDescripcion, N'') AS MaterialDescripcion,
    CONVERT(decimal(18,4), ISNULL(d.CantidadMpKg, 0)) AS MpRequerida,
    ISNULL(e.EmbalajeID, 0) AS EmbalajeID,
    ISNULL(d.EmbalajeCodigo, N'') AS EmbalajeCodigo,
    ISNULL(d.EmbalajeDescripcion, N'') AS EmbalajeDescripcion,
    CONVERT(decimal(18,4), ISNULL(d.CantidadEmbalajes, 0)) AS EmbalajeRequerido
FROM dbo.SolicitudesProduccionDetalle d
LEFT JOIN dbo.ERP_Materiales m
    ON UPPER(LTRIM(RTRIM(m.Codigo))) = UPPER(LTRIM(RTRIM(d.MaterialCodigo)))
   AND m.Activo = 1
LEFT JOIN dbo.ERP_Embalajes e
    ON UPPER(LTRIM(RTRIM(e.Codigo))) = UPPER(LTRIM(RTRIM(d.EmbalajeCodigo)))
   AND e.Activo = 1
WHERE d.SolicitudProduccionID = @Id
  AND d.Activo = 1
ORDER BY d.Renglon;";

        await using (var command = new SqlCommand(sql, connection))
        {
            command.Parameters.Add("@Id", SqlDbType.Int).Value = vm.SolicitudProduccionID;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                vm.Renglones.Add(new AlmacenOFDetalleRenglonVm
                {
                    Renglon = LeerEnteroOf(reader, "Renglon"),
                    NumeroParte = LeerTextoOf(reader, "NumeroParte"),
                    DescripcionParte = LeerTextoOf(reader, "DescripcionParte"),
                    CantidadPiezas = LeerDecimalOf(reader, "CantidadPiezas"),
                    MaterialID = LeerEnteroOf(reader, "MaterialID"),
                    MaterialCodigo = LeerTextoOf(reader, "MaterialCodigo"),
                    MaterialDescripcion = LeerTextoOf(reader, "MaterialDescripcion"),
                    MpRequerida = LeerDecimalOf(reader, "MpRequerida"),
                    EmbalajeID = LeerEnteroOf(reader, "EmbalajeID"),
                    EmbalajeCodigo = LeerTextoOf(reader, "EmbalajeCodigo"),
                    EmbalajeDescripcion = LeerTextoOf(reader, "EmbalajeDescripcion"),
                    EmbalajeRequerido = LeerDecimalOf(reader, "EmbalajeRequerido")
                });
            }
        }

        var mp = await CargarEntregadoPorCodigoAsync(
            connection,
            "dbo.AlmacenMP_Movimientos",
            "dbo.ERP_Materiales",
            "MaterialID",
            vm,
            cancellationToken);

        var packaging = await CargarEntregadoPorCodigoAsync(
            connection,
            "dbo.AlmacenEmbalajes_Movimientos",
            "dbo.ERP_Embalajes",
            "EmbalajeID",
            vm,
            cancellationToken);

        foreach (var row in vm.Renglones)
        {
            if (!string.IsNullOrWhiteSpace(row.MaterialCodigo)
                && mp.TryGetValue(row.MaterialCodigo.Trim(), out var mpDelivered))
            {
                row.MpEntregada = mpDelivered;
            }

            if (!string.IsNullOrWhiteSpace(row.EmbalajeCodigo)
                && packaging.TryGetValue(row.EmbalajeCodigo.Trim(), out var packagingDelivered))
            {
                row.EmbalajeEntregado = packagingDelivered;
            }
        }
    }

    private static async Task<Dictionary<string, decimal>> CargarEntregadoPorCodigoAsync(
        SqlConnection connection,
        string movementTable,
        string catalogTable,
        string idColumn,
        AlmacenOFDetalleVm vm,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var catalogCode = idColumn == "MaterialID" ? "Codigo" : "Codigo";

        var sql = $@"
SELECT
    LTRIM(RTRIM(c.{OfIdentificador(catalogCode)})) AS Codigo,
    CONVERT
    (
        decimal(18,4),
        SUM
        (
            CASE
                WHEN x.TipoMovimiento IN (N'Salida', N'Consumo') THEN x.Cantidad
                WHEN x.TipoMovimiento = N'Retorno' THEN -x.Cantidad
                ELSE 0
            END
        )
    ) AS Entregado
FROM {movementTable} x
INNER JOIN {catalogTable} c
    ON c.{OfIdentificador(idColumn)} = x.{OfIdentificador(idColumn)}
WHERE x.Activo = 1
  AND
  (
      (@Folio <> N'' AND LTRIM(RTRIM(x.NumeroOF)) = @Folio)
      OR
      (@NumeroOF <> N'' AND LTRIM(RTRIM(x.NumeroOF)) = @NumeroOF)
  )
GROUP BY LTRIM(RTRIM(c.{OfIdentificador(catalogCode)}));";

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Folio", SqlDbType.NVarChar, 100).Value = vm.FolioSolicitud.Trim();
        command.Parameters.Add("@NumeroOF", SqlDbType.NVarChar, 100).Value = vm.NumeroOFRecibida.Trim();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var code = LeerTextoOf(reader, "Codigo");
            if (!string.IsNullOrWhiteSpace(code))
                result[code] = LeerDecimalOf(reader, "Entregado");
        }

        return result;
    }

    private static async Task CargarEntregasAsync(
        SqlConnection connection,
        AlmacenOFDetalleVm vm,
        CancellationToken cancellationToken)
    {
        await CargarHistorialAreaAsync(
            connection,
            vm,
            "MP",
            "dbo.AlmacenMP_Movimientos",
            "dbo.ERP_Materiales",
            "MaterialID",
            cancellationToken);

        await CargarHistorialAreaAsync(
            connection,
            vm,
            "EMBALAJE",
            "dbo.AlmacenEmbalajes_Movimientos",
            "dbo.ERP_Embalajes",
            "EmbalajeID",
            cancellationToken);

        vm.Entregas = vm.Entregas
            .OrderByDescending(x => x.Fecha)
            .ToList();
    }

    private static async Task CargarHistorialAreaAsync(
        SqlConnection connection,
        AlmacenOFDetalleVm vm,
        string area,
        string movementTable,
        string catalogTable,
        string idColumn,
        CancellationToken cancellationToken)
    {
        var responsibleColumn = await PrimeraColumnaExistenteAsync(
            connection,
            movementTable,
            new[] { "UsuarioNombre", "ResponsableNombre", "Responsable", "CreadoPorNombre", "UsuarioRegistro" },
            cancellationToken);

        var receiverColumn = await PrimeraColumnaExistenteAsync(
            connection,
            movementTable,
            new[] { "RecibidoPorNombre", "PersonaRecibeNombre", "EntregadoA", "ReceptorNombre", "RecibeNombre" },
            cancellationToken);

        var observationsColumn = await PrimeraColumnaExistenteAsync(
            connection,
            movementTable,
            new[] { "Observaciones", "Observacion", "Notas" },
            cancellationToken);

        var responsibleExpression = responsibleColumn == null
            ? "N''"
            : $"ISNULL(CONVERT(nvarchar(250), x.{OfIdentificador(responsibleColumn)}), N'')";

        var receiverExpression = receiverColumn == null
            ? "N''"
            : $"ISNULL(CONVERT(nvarchar(250), x.{OfIdentificador(receiverColumn)}), N'')";

        var observationsExpression = observationsColumn == null
            ? "N''"
            : $"ISNULL(CONVERT(nvarchar(500), x.{OfIdentificador(observationsColumn)}), N'')";

        var sql = $@"
SELECT TOP (300)
    ISNULL(c.Codigo, N'') AS Codigo,
    ISNULL(c.Nombre, N'') AS Descripcion,
    ISNULL(x.TipoMovimiento, N'') AS TipoMovimiento,
    CONVERT(decimal(18,4), ISNULL(x.Cantidad, 0)) AS Cantidad,
    ISNULL(x.Unidad, N'') AS Unidad,
    x.FechaMovimiento,
    {responsibleExpression} AS Responsable,
    {receiverExpression} AS Recibio,
    {observationsExpression} AS Observaciones
FROM {movementTable} x
INNER JOIN {catalogTable} c
    ON c.{OfIdentificador(idColumn)} = x.{OfIdentificador(idColumn)}
WHERE x.Activo = 1
  AND
  (
      (@Folio <> N'' AND LTRIM(RTRIM(x.NumeroOF)) = @Folio)
      OR
      (@NumeroOF <> N'' AND LTRIM(RTRIM(x.NumeroOF)) = @NumeroOF)
  )
ORDER BY x.FechaMovimiento DESC;";

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Folio", SqlDbType.NVarChar, 100).Value = vm.FolioSolicitud.Trim();
        command.Parameters.Add("@NumeroOF", SqlDbType.NVarChar, 100).Value = vm.NumeroOFRecibida.Trim();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            vm.Entregas.Add(new AlmacenOFEntregaHistorialVm
            {
                Area = area,
                Codigo = LeerTextoOf(reader, "Codigo"),
                Descripcion = LeerTextoOf(reader, "Descripcion"),
                TipoMovimiento = LeerTextoOf(reader, "TipoMovimiento"),
                Cantidad = LeerDecimalOf(reader, "Cantidad"),
                Unidad = LeerTextoOf(reader, "Unidad"),
                Fecha = LeerFechaOf(reader, "FechaMovimiento") ?? DateTime.MinValue,
                Responsable = LeerTextoOf(reader, "Responsable"),
                Recibio = LeerTextoOf(reader, "Recibio"),
                Observaciones = LeerTextoOf(reader, "Observaciones")
            });
        }
    }

    private static async Task<string?> PrimeraColumnaExistenteAsync(
        SqlConnection connection,
        string objectName,
        IEnumerable<string> candidates,
        CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT CASE WHEN COL_LENGTH(@ObjectName, @ColumnName) IS NULL THEN 0 ELSE 1 END;";

        foreach (var candidate in candidates)
        {
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@ObjectName", SqlDbType.NVarChar, 300).Value = objectName;
            command.Parameters.Add("@ColumnName", SqlDbType.NVarChar, 300).Value = candidate;

            var exists = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
            if (exists)
                return candidate;
        }

        return null;
    }

    private static string OfIdentificador(string value)
        => $"[{value.Replace("]", "]]", StringComparison.Ordinal)}]";

    private static string LeerTextoOf(SqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? string.Empty : Convert.ToString(reader.GetValue(ordinal))?.Trim() ?? string.Empty;
    }

    private static int LeerEnteroOf(SqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? 0 : Convert.ToInt32(reader.GetValue(ordinal));
    }

    private static decimal LeerDecimalOf(SqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? 0m : Convert.ToDecimal(reader.GetValue(ordinal));
    }

    private static DateTime? LeerFechaOf(SqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : Convert.ToDateTime(reader.GetValue(ordinal));
    }
}
