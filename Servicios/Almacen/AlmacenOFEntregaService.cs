using Microsoft.Data.SqlClient;
using System.Data;

namespace ERP.NSQuell.Servicios.Almacen;

public sealed record AlmacenOFEntregaContexto(
    int SolicitudProduccionID,
    string NumeroOF,
    string Codigo,
    string Unidad,
    decimal Requerido,
    decimal Entregado)
{
    public decimal Pendiente =>
        Math.Max(0m, Requerido - Math.Max(0m, Entregado));
}

public static class AlmacenOFEntregaService
{
    public static string CrearToken() => Guid.NewGuid().ToString("N");

    public static bool TokenValido(string? token) =>
        !string.IsNullOrWhiteSpace(token)
        && Guid.TryParseExact(token.Trim(), "N", out _);

    public static string CrearReferencia(string prefijo, string token) =>
        $"{prefijo.Trim().ToUpperInvariant()}-{token.Trim().ToUpperInvariant()}";

    public static Task<AlmacenOFEntregaContexto?> CargarMateriaPrimaAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        int solicitudProduccionID,
        int materialID,
        CancellationToken cancellationToken) =>
        CargarContextoInsumoAsync(
            connection,
            transaction,
            solicitudProduccionID,
            materialID,
            esMateriaPrima: true,
            cancellationToken);

    public static Task<AlmacenOFEntregaContexto?> CargarEmbalajeAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        int solicitudProduccionID,
        int embalajeID,
        CancellationToken cancellationToken) =>
        CargarContextoInsumoAsync(
            connection,
            transaction,
            solicitudProduccionID,
            embalajeID,
            esMateriaPrima: false,
            cancellationToken);

    public static async Task<AlmacenOFEntregaContexto?>
        CargarProductoTerminadoAsync(
            SqlConnection connection,
            SqlTransaction? transaction,
            int solicitudProduccionID,
            int parteID,
            CancellationToken cancellationToken)
    {
        await using var command =
            new SqlCommand(SqlProductoTerminado, connection);

        if (transaction != null)
            command.Transaction = transaction;

        command.Parameters.Add("@SolicitudID", SqlDbType.Int).Value =
            solicitudProduccionID;
        command.Parameters.Add("@CatalogoID", SqlDbType.Int).Value =
            parteID;

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return LeerContexto(reader);
    }

    public static Task<bool> ExisteReferenciaMPAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string referencia,
        CancellationToken cancellationToken) =>
        ExisteReferenciaAsync(
            connection,
            transaction,
            "dbo.AlmacenMP_Movimientos",
            referencia,
            cancellationToken);

    public static Task<bool> ExisteReferenciaEmbalajeAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string referencia,
        CancellationToken cancellationToken) =>
        ExisteReferenciaAsync(
            connection,
            transaction,
            "dbo.AlmacenEmbalajes_Movimientos",
            referencia,
            cancellationToken);

    public static Task<bool> ExisteReferenciaPTAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string referencia,
        CancellationToken cancellationToken) =>
        ExisteReferenciaAsync(
            connection,
            transaction,
            "dbo.AlmacenPT_Movimientos",
            referencia,
            cancellationToken);

    private static async Task<AlmacenOFEntregaContexto?>
        CargarContextoInsumoAsync(
            SqlConnection connection,
            SqlTransaction? transaction,
            int solicitudProduccionID,
            int catalogoID,
            bool esMateriaPrima,
            CancellationToken cancellationToken)
    {
        var sql = esMateriaPrima
            ? SqlMateriaPrima
            : SqlEmbalaje;

        await using var command = new SqlCommand(sql, connection);

        if (transaction != null)
            command.Transaction = transaction;

        command.Parameters.Add("@SolicitudID", SqlDbType.Int).Value =
            solicitudProduccionID;
        command.Parameters.Add("@CatalogoID", SqlDbType.Int).Value =
            catalogoID;

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return LeerContexto(reader);
    }

    private static AlmacenOFEntregaContexto LeerContexto(
        SqlDataReader reader) =>
        new(
            Entero(reader, "SolicitudProduccionID"),
            Texto(reader, "NumeroOF"),
            Texto(reader, "Codigo"),
            Texto(reader, "Unidad").Trim().ToUpperInvariant(),
            DecimalValor(reader, "Requerido"),
            Math.Max(0m, DecimalValor(reader, "Entregado")));

    private static async Task<bool> ExisteReferenciaAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string tabla,
        string referencia,
        CancellationToken cancellationToken)
    {
        if (tabla is not "dbo.AlmacenMP_Movimientos"
            and not "dbo.AlmacenEmbalajes_Movimientos"
            and not "dbo.AlmacenPT_Movimientos")
        {
            throw new InvalidOperationException(
                "Tabla de movimientos no permitida.");
        }

        var sql = $@"
SELECT CASE WHEN EXISTS
(
    SELECT 1
    FROM {tabla} WITH (UPDLOCK, HOLDLOCK)
    WHERE ReferenciaOperacion = @Referencia
) THEN 1 ELSE 0 END;";

        await using var command =
            new SqlCommand(sql, connection, transaction);

        command.Parameters.Add(
            "@Referencia",
            SqlDbType.NVarChar,
            120).Value = referencia;

        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken)
            ?? 0) == 1;
    }

    private static string Texto(
        SqlDataReader reader,
        string columna)
    {
        var ordinal = reader.GetOrdinal(columna);

        return reader.IsDBNull(ordinal)
            ? string.Empty
            : reader.GetValue(ordinal)?.ToString()
              ?? string.Empty;
    }

    private static int Entero(
        SqlDataReader reader,
        string columna)
    {
        var ordinal = reader.GetOrdinal(columna);

        return reader.IsDBNull(ordinal)
            ? 0
            : Convert.ToInt32(reader.GetValue(ordinal));
    }

    private static decimal DecimalValor(
        SqlDataReader reader,
        string columna)
    {
        var ordinal = reader.GetOrdinal(columna);

        return reader.IsDBNull(ordinal)
            ? 0m
            : Convert.ToDecimal(reader.GetValue(ordinal));
    }

    private const string SqlMateriaPrima = @"
WITH Requerido AS
(
    SELECT
        d.SolicitudProduccionID,
        SUM
        (
            CONVERT
            (
                DECIMAL(18,4),
                ISNULL(d.CantidadMpKg, 0)
            )
        ) AS Requerido
    FROM dbo.SolicitudesProduccionDetalle d
        WITH (UPDLOCK, HOLDLOCK)
    INNER JOIN dbo.ERP_Materiales catalogo
        WITH (UPDLOCK, HOLDLOCK)
        ON catalogo.MaterialID = @CatalogoID
       AND catalogo.Activo = 1
       AND UPPER(LTRIM(RTRIM(ISNULL(d.MaterialCodigo, '')))) =
           UPPER(LTRIM(RTRIM(catalogo.Codigo)))
    WHERE d.SolicitudProduccionID = @SolicitudID
      AND d.Activo = 1
    GROUP BY d.SolicitudProduccionID
)
SELECT
    s.SolicitudProduccionID,
    COALESCE
    (
        NULLIF(LTRIM(RTRIM(s.NumeroOFRecibida)), ''),
        NULLIF(LTRIM(RTRIM(s.FolioSolicitud)), ''),
        ''
    ) AS NumeroOF,
    catalogo.Codigo,
    COALESCE
    (
        NULLIF(LTRIM(RTRIM(catalogo.UnidadDefault)), ''),
        'KG'
    ) AS Unidad,
    requerido.Requerido,
    CONVERT
    (
        DECIMAL(18,4),
        ISNULL
        (
            (
                SELECT SUM
                (
                    CASE
                        WHEN movimiento.TipoMovimiento IN
                             (N'Salida', N'Consumo')
                            THEN movimiento.Cantidad
                        WHEN movimiento.TipoMovimiento = N'Retorno'
                            THEN -movimiento.Cantidad
                        ELSE 0
                    END
                )
                FROM dbo.AlmacenMP_Movimientos movimiento
                    WITH (UPDLOCK, HOLDLOCK)
                WHERE movimiento.Activo = 1
                  AND COALESCE
                      (
                          movimiento.MaterialSolicitadoID,
                          movimiento.MaterialID
                      ) = catalogo.MaterialID
                  AND
                  (
                      movimiento.SolicitudProduccionID =
                          s.SolicitudProduccionID
                      OR
                      (
                          movimiento.SolicitudProduccionID IS NULL
                          AND
                          (
                              (
                                  NULLIF
                                  (
                                      LTRIM(RTRIM(s.FolioSolicitud)),
                                      ''
                                  ) IS NOT NULL
                                  AND LTRIM(RTRIM(movimiento.NumeroOF)) =
                                      LTRIM(RTRIM(s.FolioSolicitud))
                              )
                              OR
                              (
                                  NULLIF
                                  (
                                      LTRIM(RTRIM(s.NumeroOFRecibida)),
                                      ''
                                  ) IS NOT NULL
                                  AND LTRIM(RTRIM(movimiento.NumeroOF)) =
                                      LTRIM(RTRIM(s.NumeroOFRecibida))
                              )
                          )
                      )
                  )
            ),
            0
        )
    ) AS Entregado
FROM dbo.SolicitudesProduccion s
    WITH (UPDLOCK, HOLDLOCK)
INNER JOIN Requerido requerido
    ON requerido.SolicitudProduccionID =
       s.SolicitudProduccionID
INNER JOIN dbo.ERP_Materiales catalogo
    WITH (UPDLOCK, HOLDLOCK)
    ON catalogo.MaterialID = @CatalogoID
   AND catalogo.Activo = 1
WHERE s.SolicitudProduccionID = @SolicitudID
  AND s.Activo = 1;";

    private const string SqlEmbalaje = @"
WITH Requerido AS
(
    SELECT
        d.SolicitudProduccionID,
        SUM
        (
            CONVERT
            (
                DECIMAL(18,4),
                ISNULL(d.CantidadEmbalajes, 0)
            )
        ) AS Requerido
    FROM dbo.SolicitudesProduccionDetalle d
        WITH (UPDLOCK, HOLDLOCK)
    INNER JOIN dbo.ERP_Embalajes catalogo
        WITH (UPDLOCK, HOLDLOCK)
        ON catalogo.EmbalajeID = @CatalogoID
       AND catalogo.Activo = 1
       AND UPPER(LTRIM(RTRIM(ISNULL(d.EmbalajeCodigo, '')))) =
           UPPER(LTRIM(RTRIM(catalogo.Codigo)))
    WHERE d.SolicitudProduccionID = @SolicitudID
      AND d.Activo = 1
    GROUP BY d.SolicitudProduccionID
)
SELECT
    s.SolicitudProduccionID,
    COALESCE
    (
        NULLIF(LTRIM(RTRIM(s.NumeroOFRecibida)), ''),
        NULLIF(LTRIM(RTRIM(s.FolioSolicitud)), ''),
        ''
    ) AS NumeroOF,
    catalogo.Codigo,
    COALESCE
    (
        NULLIF(LTRIM(RTRIM(catalogo.UnidadDefault)), ''),
        'PZS'
    ) AS Unidad,
    requerido.Requerido,
    CONVERT
    (
        DECIMAL(18,4),
        ISNULL
        (
            (
                SELECT SUM
                (
                    CASE
                        WHEN movimiento.TipoMovimiento IN
                             (N'Salida', N'Consumo')
                            THEN movimiento.Cantidad
                        WHEN movimiento.TipoMovimiento = N'Retorno'
                            THEN -movimiento.Cantidad
                        ELSE 0
                    END
                )
                FROM dbo.AlmacenEmbalajes_Movimientos movimiento
                    WITH (UPDLOCK, HOLDLOCK)
                WHERE movimiento.Activo = 1
                  AND movimiento.EmbalajeID =
                      catalogo.EmbalajeID
                  AND
                  (
                      (
                          NULLIF
                          (
                              LTRIM(RTRIM(s.FolioSolicitud)),
                              ''
                          ) IS NOT NULL
                          AND LTRIM(RTRIM(movimiento.NumeroOF)) =
                              LTRIM(RTRIM(s.FolioSolicitud))
                      )
                      OR
                      (
                          NULLIF
                          (
                              LTRIM(RTRIM(s.NumeroOFRecibida)),
                              ''
                          ) IS NOT NULL
                          AND LTRIM(RTRIM(movimiento.NumeroOF)) =
                              LTRIM(RTRIM(s.NumeroOFRecibida))
                      )
                  )
            ),
            0
        )
    ) AS Entregado
FROM dbo.SolicitudesProduccion s
    WITH (UPDLOCK, HOLDLOCK)
INNER JOIN Requerido requerido
    ON requerido.SolicitudProduccionID =
       s.SolicitudProduccionID
INNER JOIN dbo.ERP_Embalajes catalogo
    WITH (UPDLOCK, HOLDLOCK)
    ON catalogo.EmbalajeID = @CatalogoID
   AND catalogo.Activo = 1
WHERE s.SolicitudProduccionID = @SolicitudID
  AND s.Activo = 1;";

    private const string SqlProductoTerminado = @"
WITH Requerido AS
(
    SELECT
        d.SolicitudProduccionID,
        SUM
        (
            CONVERT
            (
                DECIMAL(18,4),
                ISNULL(d.CantidadPiezas, 0)
            )
        ) AS Requerido
    FROM dbo.SolicitudesProduccionDetalle d
        WITH (UPDLOCK, HOLDLOCK)
    INNER JOIN dbo.ERP_Partes catalogo
        WITH (UPDLOCK, HOLDLOCK)
        ON catalogo.ParteID = @CatalogoID
       AND catalogo.Activo = 1
       AND d.ParteID = catalogo.ParteID
    WHERE d.SolicitudProduccionID = @SolicitudID
      AND d.Activo = 1
      AND ISNULL(d.CantidadPiezas, 0) > 0
    GROUP BY d.SolicitudProduccionID
)
SELECT
    s.SolicitudProduccionID,
    COALESCE
    (
        NULLIF(LTRIM(RTRIM(s.NumeroOFRecibida)), ''),
        NULLIF(LTRIM(RTRIM(s.FolioSolicitud)), ''),
        ''
    ) AS NumeroOF,
    catalogo.NumeroParte AS Codigo,
    N'PZS' AS Unidad,
    requerido.Requerido,
    CONVERT
    (
        DECIMAL(18,4),
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
                    WITH (UPDLOCK, HOLDLOCK)
                WHERE movimiento.Activo = 1
                  AND movimiento.ParteID =
                      catalogo.ParteID
                  AND
                  (
                      (
                          NULLIF
                          (
                              LTRIM(RTRIM(s.FolioSolicitud)),
                              ''
                          ) IS NOT NULL
                          AND LTRIM(RTRIM(movimiento.NumeroOF)) =
                              LTRIM(RTRIM(s.FolioSolicitud))
                      )
                      OR
                      (
                          NULLIF
                          (
                              LTRIM(RTRIM(s.NumeroOFRecibida)),
                              ''
                          ) IS NOT NULL
                          AND LTRIM(RTRIM(movimiento.NumeroOF)) =
                              LTRIM(RTRIM(s.NumeroOFRecibida))
                      )
                  )
            ),
            0
        )
    ) AS Entregado
FROM dbo.SolicitudesProduccion s
    WITH (UPDLOCK, HOLDLOCK)
INNER JOIN Requerido requerido
    ON requerido.SolicitudProduccionID =
       s.SolicitudProduccionID
INNER JOIN dbo.ERP_Partes catalogo
    WITH (UPDLOCK, HOLDLOCK)
    ON catalogo.ParteID = @CatalogoID
   AND catalogo.Activo = 1
WHERE s.SolicitudProduccionID = @SolicitudID
  AND s.Activo = 1;";
}
