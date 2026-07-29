using ERP.NSQuell.Models.ViewModels.Almacen;
using Microsoft.Data.SqlClient;
using System.Data;

namespace ERP.NSQuell.Controllers;

// ALMACEN_OF_MAQUINAS_PT_V4_1
public sealed partial class AlmacenOFController
{
    private static async Task CargarMaquinasAlmacenAsync(
        SqlConnection connection,
        List<AlmacenOFItemVm> ordenes,
        CancellationToken cancellationToken)
    {
        if (ordenes.Count == 0)
            return;

        var parametros = ordenes
            .Select((x, i) => new { x.SolicitudProduccionID, Nombre = $"@OfM{i}" })
            .ToList();

        var inSql = string.Join(",", parametros.Select(x => x.Nombre));
        var sql = $@"
SELECT
    s.SolicitudProduccionID,
    COALESCE(asignada.MaquinaID, programa.MaquinaID, sugerida.MaquinaID, 0) AS MaquinaID,
    COALESCE
    (
        NULLIF(LTRIM(RTRIM(asignada.Codigo)), N''),
        NULLIF(LTRIM(RTRIM(programa.Codigo)), N''),
        NULLIF(LTRIM(RTRIM(sugerida.Codigo)), N''),
        N''
    ) AS MaquinaCodigo,
    COALESCE
    (
        NULLIF(LTRIM(RTRIM(asignada.Nombre)), N''),
        NULLIF(LTRIM(RTRIM(programa.Nombre)), N''),
        NULLIF(LTRIM(RTRIM(sugerida.Nombre)), N''),
        N''
    ) AS MaquinaNombre
FROM dbo.SolicitudesProduccion s
OUTER APPLY
(
    SELECT TOP (1)
        a.MaquinaID,
        ISNULL(m.Codigo, N'') AS Codigo,
        ISNULL(m.Nombre, N'') AS Nombre
    FROM dbo.SolicitudesProduccionDetalle d
    INNER JOIN dbo.SolicitudesProduccionAsignacionMaquina a
        ON a.SolicitudProduccionDetalleID = d.SolicitudProduccionDetalleID
       AND a.Activo = 1
    LEFT JOIN dbo.ERP_Maquinas m
        ON m.MaquinaID = a.MaquinaID
       AND m.Activo = 1
    WHERE d.SolicitudProduccionID = s.SolicitudProduccionID
      AND d.Activo = 1
    ORDER BY
        CASE WHEN a.EstatusID IN (2, 3) THEN 0 ELSE 1 END,
        a.Secuencia,
        a.AsignacionMaquinaID DESC
) asignada
OUTER APPLY
(
    SELECT TOP (1)
        p.MaquinaID,
        ISNULL(p.MaquinaCodigo, N'') AS Codigo,
        ISNULL(p.MaquinaNombre, N'') AS Nombre
    FROM dbo.Planeacion_ProgramaProduccion p
    WHERE p.SolicitudProduccionID = s.SolicitudProduccionID
      AND p.Activo = 1
      AND
      (
          p.MaquinaID IS NOT NULL
          OR NULLIF(LTRIM(RTRIM(p.MaquinaCodigo)), N'') IS NOT NULL
          OR NULLIF(LTRIM(RTRIM(p.MaquinaNombre)), N'') IS NOT NULL
      )
    ORDER BY
        CASE WHEN p.FechaInicioProgramada IS NULL THEN 1 ELSE 0 END,
        p.FechaInicioProgramada,
        p.SecuenciaMaquina,
        p.ProgramaProduccionID
) programa
OUTER APPLY
(
    SELECT TOP (1)
        d.MaquinaSugeridaID AS MaquinaID,
        ISNULL(m.Codigo, N'') AS Codigo,
        COALESCE
        (
            NULLIF(LTRIM(RTRIM(m.Nombre)), N''),
            NULLIF(LTRIM(RTRIM(d.MaquinaSugeridaTexto)), N''),
            N''
        ) AS Nombre
    FROM dbo.SolicitudesProduccionDetalle d
    LEFT JOIN dbo.ERP_Maquinas m
        ON m.MaquinaID = d.MaquinaSugeridaID
       AND m.Activo = 1
    WHERE d.SolicitudProduccionID = s.SolicitudProduccionID
      AND d.Activo = 1
      AND
      (
          d.MaquinaSugeridaID IS NOT NULL
          OR NULLIF(LTRIM(RTRIM(d.MaquinaSugeridaTexto)), N'') IS NOT NULL
      )
    ORDER BY d.Renglon, d.SolicitudProduccionDetalleID
) sugerida
WHERE s.SolicitudProduccionID IN ({inSql});";

        await using var command = new SqlCommand(sql, connection);
        foreach (var parametro in parametros)
        {
            command.Parameters.Add(parametro.Nombre, SqlDbType.Int).Value =
                parametro.SolicitudProduccionID;
        }

        var porId = ordenes.ToDictionary(x => x.SolicitudProduccionID);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var solicitudId = Convert.ToInt32(reader["SolicitudProduccionID"]);
            if (!porId.TryGetValue(solicitudId, out var item))
                continue;

            var codigo = reader["MaquinaCodigo"]?.ToString()?.Trim() ?? string.Empty;
            var nombre = reader["MaquinaNombre"]?.ToString()?.Trim() ?? string.Empty;
            item.Maquina = FormatearMaquinaAlmacen(codigo, nombre);
        }
    }

    private static async Task<string> CargarNombreMaquinaAlmacenAsync(
        SqlConnection connection,
        int solicitudProduccionId,
        CancellationToken cancellationToken)
    {
        var temporal = new List<AlmacenOFItemVm>
        {
            new() { SolicitudProduccionID = solicitudProduccionId }
        };

        await CargarMaquinasAlmacenAsync(connection, temporal, cancellationToken);
        return temporal[0].Maquina;
    }

    private static string FormatearMaquinaAlmacen(string codigo, string nombre)
    {
        codigo = codigo.Trim();
        nombre = nombre.Trim();

        if (string.IsNullOrWhiteSpace(codigo) && string.IsNullOrWhiteSpace(nombre))
            return "Sin maquina asignada";

        if (string.IsNullOrWhiteSpace(codigo))
            return nombre;

        if (string.IsNullOrWhiteSpace(nombre)
            || string.Equals(codigo, nombre, StringComparison.OrdinalIgnoreCase))
        {
            return codigo;
        }

        return $"{codigo} - {nombre}";
    }

    private static async Task CargarProductoTerminadoSolicitadoAsync(
        SqlConnection connection,
        List<AlmacenOFItemVm> ordenes,
        CancellationToken cancellationToken)
    {
        foreach (var orden in ordenes)
            orden.PartesEntrega.Clear();

        if (ordenes.Count == 0)
            return;

        var parametros = ordenes
            .Select((x, i) => new { x.SolicitudProduccionID, Nombre = $"@OfP{i}" })
            .ToList();

        var inSql = string.Join(",", parametros.Select(x => x.Nombre));
        var sql = $@"
WITH ProgramaPT AS
(
    SELECT
        p.SolicitudProduccionDetalleID,
        SUM
        (
            CASE
                WHEN ISNULL(p.PiezasDesdePT, 0) > 0
                    THEN ISNULL(p.PiezasDesdePT, 0)
                ELSE 0
            END
        ) AS PiezasDesdePT
    FROM dbo.Planeacion_ProgramaProduccion p
    WHERE p.Activo = 1
      AND p.SolicitudProduccionID IN ({inSql})
    GROUP BY p.SolicitudProduccionDetalleID
), Solicitado AS
(
    SELECT
        d.SolicitudProduccionID,
        d.ParteID,
        MAX(ISNULL(p.NumeroParte, d.ReferenciaSAP)) AS NumeroParte,
        MAX
        (
            COALESCE
            (
                NULLIF(LTRIM(RTRIM(p.Designacion)), N''),
                NULLIF(LTRIM(RTRIM(p.Descripcion)), N''),
                NULLIF(LTRIM(RTRIM(d.DesignacionDescripcionSAP)), N''),
                N''
            )
        ) AS Descripcion,
        SUM
        (
            CONVERT
            (
                decimal(18,4),
                CASE
                    WHEN ISNULL(programa.PiezasDesdePT, 0) > 0
                        THEN
                            CASE
                                WHEN programa.PiezasDesdePT > ISNULL(d.CantidadPiezas, 0)
                                    THEN ISNULL(d.CantidadPiezas, 0)
                                ELSE programa.PiezasDesdePT
                            END
                    WHEN UPPER(LTRIM(RTRIM(ISNULL(d.OrigenSurtido, N'')))) = N'PT'
                        THEN ISNULL(d.CantidadPiezas, 0)
                    WHEN UPPER(LTRIM(RTRIM(ISNULL(d.OrigenSurtido, N'')))) = N'MIXTO'
                        THEN
                            CASE
                                WHEN ISNULL(d.PTDisponibleAlCrear, 0) > ISNULL(d.CantidadPiezas, 0)
                                    THEN ISNULL(d.CantidadPiezas, 0)
                                ELSE ISNULL(d.PTDisponibleAlCrear, 0)
                            END
                    ELSE 0
                END
            )
        ) AS Requerido
    FROM dbo.SolicitudesProduccionDetalle d
    INNER JOIN dbo.ERP_Partes p
        ON p.ParteID = d.ParteID
       AND p.Activo = 1
    LEFT JOIN ProgramaPT programa
        ON programa.SolicitudProduccionDetalleID = d.SolicitudProduccionDetalleID
    WHERE d.Activo = 1
      AND d.SolicitudProduccionID IN ({inSql})
      AND d.ParteID IS NOT NULL
    GROUP BY d.SolicitudProduccionID, d.ParteID
), Datos AS
(
    SELECT
        solicitado.SolicitudProduccionID,
        solicitado.ParteID,
        solicitado.NumeroParte,
        solicitado.Descripcion,
        solicitado.Requerido,
        CONVERT(decimal(18,4), ISNULL(inventario.Disponible, 0)) AS Disponible,
        CONVERT
        (
            decimal(18,4),
            ISNULL
            (
                (
                    SELECT SUM
                    (
                        CASE
                            WHEN movimiento.TipoMovimiento IN (N'Salida', N'Embarque')
                                THEN movimiento.Cantidad
                            WHEN movimiento.TipoMovimiento = N'Retorno'
                                THEN -movimiento.Cantidad
                            ELSE 0
                        END
                    )
                    FROM dbo.AlmacenPT_Movimientos movimiento
                    INNER JOIN dbo.SolicitudesProduccion so
                        ON so.SolicitudProduccionID = solicitado.SolicitudProduccionID
                    WHERE movimiento.Activo = 1
                      AND movimiento.ParteID = solicitado.ParteID
                      AND
                      (
                          (
                              NULLIF(LTRIM(RTRIM(so.FolioSolicitud)), N'') IS NOT NULL
                              AND LTRIM(RTRIM(movimiento.NumeroOF)) = LTRIM(RTRIM(so.FolioSolicitud))
                          )
                          OR
                          (
                              NULLIF(LTRIM(RTRIM(so.NumeroOFRecibida)), N'') IS NOT NULL
                              AND LTRIM(RTRIM(movimiento.NumeroOF)) = LTRIM(RTRIM(so.NumeroOFRecibida))
                          )
                      )
                ),
                0
            )
        ) AS Entregado
    FROM Solicitado solicitado
    LEFT JOIN dbo.vw_AlmacenPTInventario inventario
        ON inventario.ParteID = solicitado.ParteID
    WHERE solicitado.Requerido > 0
)
SELECT
    SolicitudProduccionID,
    ParteID,
    NumeroParte,
    Descripcion,
    Requerido,
    Disponible,
    Entregado
FROM Datos
ORDER BY SolicitudProduccionID, NumeroParte;";

        await using var command = new SqlCommand(sql, connection);
        foreach (var parametro in parametros)
        {
            command.Parameters.Add(parametro.Nombre, SqlDbType.Int).Value =
                parametro.SolicitudProduccionID;
        }

        var porId = ordenes.ToDictionary(x => x.SolicitudProduccionID);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var solicitudId = Convert.ToInt32(reader["SolicitudProduccionID"]);
            if (!porId.TryGetValue(solicitudId, out var orden))
                continue;

            orden.PartesEntrega.Add(new AlmacenOFEntregableVm
            {
                SolicitudProduccionID = solicitudId,
                CatalogoID = Convert.ToInt32(reader["ParteID"]),
                Codigo = reader["NumeroParte"]?.ToString()?.Trim() ?? string.Empty,
                Descripcion = reader["Descripcion"]?.ToString()?.Trim() ?? string.Empty,
                Unidad = "PZS",
                Requerido = Convert.ToDecimal(reader["Requerido"]),
                Entregado = Convert.ToDecimal(reader["Entregado"]),
                DisponibleInventario = Convert.ToDecimal(reader["Disponible"]),
                RequiereInventarioDisponible = true
            });
        }
    }

    private static async Task CargarProductoTerminadoDetalleAsync(
        SqlConnection connection,
        AlmacenOFDetalleVm vm,
        CancellationToken cancellationToken)
    {
        const string sql = @"
WITH ProgramaPT AS
(
    SELECT
        p.SolicitudProduccionDetalleID,
        SUM(CASE WHEN ISNULL(p.PiezasDesdePT, 0) > 0 THEN p.PiezasDesdePT ELSE 0 END) AS PiezasDesdePT
    FROM dbo.Planeacion_ProgramaProduccion p
    WHERE p.Activo = 1
      AND p.SolicitudProduccionID = @Id
    GROUP BY p.SolicitudProduccionDetalleID
)
SELECT
    d.Renglon,
    ISNULL(d.ParteID, 0) AS ParteID,
    CONVERT
    (
        decimal(18,4),
        CASE
            WHEN ISNULL(programa.PiezasDesdePT, 0) > 0
                THEN
                    CASE
                        WHEN programa.PiezasDesdePT > ISNULL(d.CantidadPiezas, 0)
                            THEN ISNULL(d.CantidadPiezas, 0)
                        ELSE programa.PiezasDesdePT
                    END
            WHEN UPPER(LTRIM(RTRIM(ISNULL(d.OrigenSurtido, N'')))) = N'PT'
                THEN ISNULL(d.CantidadPiezas, 0)
            WHEN UPPER(LTRIM(RTRIM(ISNULL(d.OrigenSurtido, N'')))) = N'MIXTO'
                THEN
                    CASE
                        WHEN ISNULL(d.PTDisponibleAlCrear, 0) > ISNULL(d.CantidadPiezas, 0)
                            THEN ISNULL(d.CantidadPiezas, 0)
                        ELSE ISNULL(d.PTDisponibleAlCrear, 0)
                    END
            ELSE 0
        END
    ) AS PtRequerida,
    CONVERT(decimal(18,4), ISNULL(inventario.Disponible, 0)) AS PtDisponible,
    CONVERT
    (
        decimal(18,4),
        ISNULL
        (
            (
                SELECT SUM
                (
                    CASE
                        WHEN movimiento.TipoMovimiento IN (N'Salida', N'Embarque')
                            THEN movimiento.Cantidad
                        WHEN movimiento.TipoMovimiento = N'Retorno'
                            THEN -movimiento.Cantidad
                        ELSE 0
                    END
                )
                FROM dbo.AlmacenPT_Movimientos movimiento
                INNER JOIN dbo.SolicitudesProduccion so
                    ON so.SolicitudProduccionID = d.SolicitudProduccionID
                WHERE movimiento.Activo = 1
                  AND movimiento.ParteID = d.ParteID
                  AND
                  (
                      (
                          NULLIF(LTRIM(RTRIM(so.FolioSolicitud)), N'') IS NOT NULL
                          AND LTRIM(RTRIM(movimiento.NumeroOF)) = LTRIM(RTRIM(so.FolioSolicitud))
                      )
                      OR
                      (
                          NULLIF(LTRIM(RTRIM(so.NumeroOFRecibida)), N'') IS NOT NULL
                          AND LTRIM(RTRIM(movimiento.NumeroOF)) = LTRIM(RTRIM(so.NumeroOFRecibida))
                      )
                  )
            ),
            0
        )
    ) AS PtEntregada
FROM dbo.SolicitudesProduccionDetalle d
LEFT JOIN ProgramaPT programa
    ON programa.SolicitudProduccionDetalleID = d.SolicitudProduccionDetalleID
LEFT JOIN dbo.vw_AlmacenPTInventario inventario
    ON inventario.ParteID = d.ParteID
WHERE d.SolicitudProduccionID = @Id
  AND d.Activo = 1
ORDER BY d.Renglon;";

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Id", SqlDbType.Int).Value = vm.SolicitudProduccionID;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var renglon = Convert.ToInt32(reader["Renglon"]);
            var row = vm.Renglones.FirstOrDefault(x => x.Renglon == renglon);
            if (row == null)
                continue;

            row.ParteID = Convert.ToInt32(reader["ParteID"]);
            row.PtRequerida = Convert.ToDecimal(reader["PtRequerida"]);
            row.PtDisponible = Convert.ToDecimal(reader["PtDisponible"]);
            row.PtEntregada = Convert.ToDecimal(reader["PtEntregada"]);
        }
    }

    private static async Task CargarHistorialPTAlmacenAsync(
        SqlConnection connection,
        AlmacenOFDetalleVm vm,
        CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT TOP (300)
    ISNULL(p.NumeroParte, N'') AS Codigo,
    COALESCE(NULLIF(LTRIM(RTRIM(p.Designacion)), N''), NULLIF(LTRIM(RTRIM(p.Descripcion)), N''), N'') AS Descripcion,
    ISNULL(m.TipoMovimiento, N'') AS TipoMovimiento,
    CONVERT(decimal(18,4), ISNULL(m.Cantidad, 0)) AS Cantidad,
    N'PZS' AS Unidad,
    m.FechaMovimiento,
    COALESCE
    (
        NULLIF(LTRIM(RTRIM(CONCAT(persona.Nombre, N' ', persona.ApellidoPaterno))), N''),
        NULLIF(LTRIM(RTRIM(m.CreadoPor)), N''),
        N''
    ) AS Responsable,
    N'' AS Recibio,
    ISNULL(m.Observaciones, N'') AS Observaciones
FROM dbo.AlmacenPT_Movimientos m
INNER JOIN dbo.ERP_Partes p
    ON p.ParteID = m.ParteID
LEFT JOIN dbo.Usuarios usuario
    ON usuario.UsuarioID = m.ResponsableUsuarioID
LEFT JOIN dbo.Persona persona
    ON persona.PersonaID = usuario.PersonaID
WHERE m.Activo = 1
  AND
  (
      (@Folio <> N'' AND LTRIM(RTRIM(m.NumeroOF)) = @Folio)
      OR
      (@NumeroOF <> N'' AND LTRIM(RTRIM(m.NumeroOF)) = @NumeroOF)
  )
ORDER BY m.FechaMovimiento DESC, m.MovimientoID DESC;";

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Folio", SqlDbType.NVarChar, 100).Value = vm.FolioSolicitud.Trim();
        command.Parameters.Add("@NumeroOF", SqlDbType.NVarChar, 100).Value = vm.NumeroOFRecibida.Trim();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            vm.Entregas.Add(new AlmacenOFEntregaHistorialVm
            {
                Area = "PT",
                Codigo = reader["Codigo"]?.ToString()?.Trim() ?? string.Empty,
                Descripcion = reader["Descripcion"]?.ToString()?.Trim() ?? string.Empty,
                TipoMovimiento = reader["TipoMovimiento"]?.ToString()?.Trim() ?? string.Empty,
                Cantidad = Convert.ToDecimal(reader["Cantidad"]),
                Unidad = "PZS",
                Fecha = Convert.ToDateTime(reader["FechaMovimiento"]),
                Responsable = reader["Responsable"]?.ToString()?.Trim() ?? string.Empty,
                Recibio = string.Empty,
                Observaciones = reader["Observaciones"]?.ToString()?.Trim() ?? string.Empty
            });
        }
    }
}
