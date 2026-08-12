/*
    NSQuell.ERP - Scrap V15
    Integracion Almacen Scrap <- GP12 / Calidad + conciliacion por lote con MP Molido.

    SEGURIDAD:
      @Aplicar = 0  => SOLO DIAGNOSTICO (valor por defecto).
      @Aplicar = 1  => crea objetos y ejecuta sincronizacion inicial.

    No elimina registros. Todo cambio funcional se realiza dentro de transaccion.
*/
USE [ERP_QUELL];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @Aplicar BIT = 0;

IF DB_NAME() <> N'ERP_QUELL'
    THROW 54690, N'Este script solo debe ejecutarse sobre ERP_QUELL.', 1;

PRINT N'=== SCRAP V15 - DIAGNOSTICO ===';

SELECT
    DB_NAME() AS BaseActual,
    OBJECT_ID(N'dbo.AlmacenScrap_Registros', N'U') AS ScrapRegistros,
    OBJECT_ID(N'dbo.AlmacenScrap_Historial', N'U') AS ScrapHistorial,
    OBJECT_ID(N'dbo.usp_AlmacenScrap_RegistrarOrigen', N'P') AS RegistrarOrigen,
    OBJECT_ID(N'dbo.GP12_SolicitudEtiquetas', N'U') AS GP12Etiquetas,
    OBJECT_ID(N'dbo.Calidad_DisposicionesMaterial', N'U') AS CalidadDisposiciones,
    OBJECT_ID(N'dbo.AlmacenMP_Movimientos', N'U') AS MovimientosMP;

IF OBJECT_ID(N'dbo.GP12_SolicitudEtiquetas', N'U') IS NOT NULL
BEGIN
    SELECT
        e.SolicitudEtiquetaID,
        e.SolicitudGP12ID,
        e.TipoEtiqueta,
        e.CantidadSolicitada,
        e.CantidadRecibida,
        e.CantidadProcesada,
        s.OrdenFabricacion,
        s.ParteID,
        s.NumeroParte,
        s.CajaProduccionID
    FROM dbo.GP12_SolicitudEtiquetas e
    INNER JOIN dbo.GP12_Solicitudes s
        ON s.SolicitudGP12ID = e.SolicitudGP12ID
       AND s.Activo = 1
    WHERE e.Activo = 1
      AND UPPER(LTRIM(RTRIM(e.TipoEtiqueta))) = N'ROJA'
    ORDER BY e.SolicitudEtiquetaID;
END;

IF OBJECT_ID(N'dbo.Calidad_DisposicionesMaterial', N'U') IS NOT NULL
BEGIN
    SELECT
        d.DisposicionID,
        d.InspeccionID,
        d.Etiqueta,
        d.Disposicion,
        d.CantidadAfectada,
        d.CantidadScrap,
        d.ResultadoFinal,
        i.OrdenTrabajo,
        i.ParteID,
        i.NumeroParte,
        i.EjecucionProduccionID
    FROM dbo.Calidad_DisposicionesMaterial d
    INNER JOIN dbo.Calidad_Inspecciones i
        ON i.InspeccionID = d.InspeccionID
    WHERE d.Activo = 1
      AND
      (
          UPPER(LTRIM(RTRIM(ISNULL(d.Etiqueta, N'')))) = N'ROJA'
          OR UPPER(LTRIM(RTRIM(ISNULL(d.Disposicion, N'')))) = N'SCRAP'
          OR UPPER(LTRIM(RTRIM(ISNULL(d.ResultadoFinal, N'')))) = N'SCRAP'
          OR ISNULL(d.CantidadScrap, 0) > 0
      )
    ORDER BY d.DisposicionID;
END;

IF @Aplicar = 0
BEGIN
    PRINT N'DIAGNOSTICO TERMINADO. No se modifico la base.';
    PRINT N'Para aplicar: cambia DECLARE @Aplicar BIT = 0 por = 1 y vuelve a ejecutar el archivo completo.';
    RETURN;
END;

IF OBJECT_ID(N'dbo.AlmacenScrap_Registros', N'U') IS NULL
   OR OBJECT_ID(N'dbo.AlmacenScrap_Historial', N'U') IS NULL
   OR OBJECT_ID(N'dbo.usp_AlmacenScrap_RegistrarOrigen', N'P') IS NULL
    THROW 54691, N'Primero debe estar instalado Scripts/SQL/Almacen/14_Modulo_Almacen_Scrap.sql.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.AlmacenScrap_Registros')
          AND name = N'UX_AlmacenScrap_Registros_OrigenReferenciaTexto'
    )
    BEGIN
        CREATE UNIQUE NONCLUSTERED INDEX UX_AlmacenScrap_Registros_OrigenReferenciaTexto
        ON dbo.AlmacenScrap_Registros (Origen, OrigenReferencia)
        WHERE OrigenReferenciaID IS NULL
          AND OrigenReferencia IS NOT NULL
          AND Activo = 1;
    END;

    EXEC sys.sp_executesql N'CREATE OR ALTER VIEW dbo.vw_AlmacenScrap_Inventario
AS
SELECT
    s.ParteID,
    s.NumeroParte,
    s.Designacion,
    s.Lote,
    COUNT_BIG(*) AS RegistrosRecibidos,
    SUM(CONVERT(BIGINT, s.CantidadPiezas)) AS PiezasScrapDisponibles,
    MIN(s.FechaRecepcion) AS PrimeraRecepcion,
    MAX(s.FechaRecepcion) AS UltimaRecepcion
FROM dbo.AlmacenScrap_Registros s
WHERE s.Activo = 1
  AND s.Estatus = N''RECIBIDO''
GROUP BY
    s.ParteID,
    s.NumeroParte,
    s.Designacion,
    s.Lote';
    EXEC sys.sp_executesql N'CREATE OR ALTER PROCEDURE dbo.usp_AlmacenScrap_SincronizarOrigenes
    @UsuarioID INT = NULL,
    @UsuarioNombre NVARCHAR(180) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @UsuarioNombre = COALESCE(NULLIF(LTRIM(RTRIM(@UsuarioNombre)), N''''), N''SINCRONIZADOR SCRAP V15'');

    IF OBJECT_ID(N''dbo.AlmacenScrap_Registros'', N''U'') IS NULL
       OR OBJECT_ID(N''dbo.AlmacenScrap_Historial'', N''U'') IS NULL
       OR OBJECT_ID(N''dbo.usp_AlmacenScrap_RegistrarOrigen'', N''P'') IS NULL
        THROW 54600, N''El modulo base de Scrap no esta instalado.'', 1;

    DECLARE @NuevosGP12 INT = 0;
    DECLARE @NuevosCalidad INT = 0;
    DECLARE @VinculadosMP INT = 0;

    CREATE TABLE #VinculosMP
    (
        ScrapRegistroID BIGINT NOT NULL,
        EstatusAnterior NVARCHAR(30) NULL,
        MPMovimientoID INT NULL,
        MaterialIDMolido INT NULL,
        FechaMolido DATETIME2(7) NULL
    );

    BEGIN TRY
        BEGIN TRANSACTION;

        ----------------------------------------------------------------------
        -- GP12: toda etiqueta ROJA activa genera una recepcion pendiente.
        ----------------------------------------------------------------------
        IF OBJECT_ID(N''dbo.GP12_SolicitudEtiquetas'', N''U'') IS NOT NULL
           AND OBJECT_ID(N''dbo.GP12_Solicitudes'', N''U'') IS NOT NULL
        BEGIN
            DECLARE
                @G_ReferenciaID BIGINT,
                @G_Referencia NVARCHAR(120),
                @G_Codigo NVARCHAR(500),
                @G_NumeroOF NVARCHAR(80),
                @G_NumeroParte NVARCHAR(120),
                @G_Designacion NVARCHAR(300),
                @G_Cantidad INT,
                @G_Lote NVARCHAR(120),
                @G_ParteID INT,
                @G_FechaOrigen DATETIME2(7),
                @G_Observaciones NVARCHAR(800);

            DECLARE @ResultadoGP12 TABLE (ScrapRegistroID BIGINT);

            DECLARE cur_gp12 CURSOR LOCAL FAST_FORWARD FOR
            SELECT
                CONVERT(BIGINT, e.SolicitudEtiquetaID) AS ReferenciaID,
                LEFT(CONCAT(N''GP12:'', s.SolicitudGP12ID, N'':ETIQUETA_ROJA:'', e.SolicitudEtiquetaID), 120) AS Referencia,
                LEFT(codigo.CodigoBarras, 500) AS CodigoBarras,
                LEFT(
                    COALESCE(
                        NULLIF(LTRIM(RTRIM(s.OrdenFabricacion)), N''''),
                        NULLIF(LTRIM(RTRIM(sp.NumeroOFRecibida)), N''''),
                        NULLIF(LTRIM(RTRIM(sp.FolioSolicitud)), N''''),
                        CONCAT(N''GP12-'', s.SolicitudGP12ID)
                    ),
                    80
                ) AS NumeroOF,
                LEFT(
                    COALESCE(
                        NULLIF(LTRIM(RTRIM(pcanon.NumeroParte)), N''''),
                        NULLIF(LTRIM(RTRIM(s.NumeroParte)), N''''),
                        N''SIN-PARTE''
                    ),
                    120
                ) AS NumeroParte,
                LEFT(
                    COALESCE(
                        NULLIF(LTRIM(RTRIM(pcanon.Designacion)), N''''),
                        NULLIF(LTRIM(RTRIM(pcanon.Descripcion)), N''''),
                        NULLIF(LTRIM(RTRIM(s.DescripcionParte)), N''''),
                        N''SCRAP GP12''
                    ),
                    300
                ) AS Designacion,
                CONVERT
                (
                    INT,
                    CEILING
                    (
                        CASE
                            WHEN ISNULL(e.CantidadProcesada, 0) > 0 THEN e.CantidadProcesada
                            WHEN ISNULL(e.CantidadRecibida, 0) > 0 THEN e.CantidadRecibida
                            ELSE e.CantidadSolicitada
                        END
                    )
                ) AS CantidadPiezas,
                LEFT(
                    COALESCE(
                        NULLIF(LTRIM(RTRIM(pc.LoteMaterial)), N''''),
                        NULLIF(LTRIM(RTRIM(lote.LoteExtraido)), N''''),
                        CONCAT(N''GP12-'', s.SolicitudGP12ID, N''-'', e.SolicitudEtiquetaID)
                    ),
                    120
                ) AS Lote,
                pcanon.ParteID,
                CONVERT(DATETIME2(7), COALESCE(e.FechaModificacion, e.FechaCreacion, s.FechaModificacion, s.FechaCreacion)) AS FechaOrigen,
                LEFT(
                    CONCAT(
                        N''Etiqueta ROJA generada en GP12. SolicitudGP12ID='', s.SolicitudGP12ID,
                        N''; SolicitudEtiquetaID='', e.SolicitudEtiquetaID,
                        N''; OrigenGP12='', ISNULL(s.Origen, N'''')
                    ),
                    800
                ) AS Observaciones
            FROM dbo.GP12_SolicitudEtiquetas e
            INNER JOIN dbo.GP12_Solicitudes s
                ON s.SolicitudGP12ID = e.SolicitudGP12ID
               AND s.Activo = 1
            LEFT JOIN dbo.SolicitudesProduccion sp
                ON sp.SolicitudProduccionID = s.SolicitudProduccionID
               AND sp.Activo = 1
            LEFT JOIN dbo.Produccion_Cajas pc
                ON pc.CajaProduccionID = s.CajaProduccionID
               AND pc.Activo = 1
            LEFT JOIN dbo.Calidad_Inspecciones ci
                ON ci.InspeccionID = s.CalidadInspeccionID
            OUTER APPLY
            (
                SELECT
                    CodigoCaja = CASE
                        WHEN COUNT_BIG(*) = 1
                            THEN MAX(COALESCE(NULLIF(LTRIM(RTRIM(gc.NumeroEtiqueta)), N''''), NULLIF(LTRIM(RTRIM(gc.FolioCaja)), N'''')))
                        ELSE NULL
                    END
                FROM dbo.GP12_Cajas gc
                WHERE gc.SolicitudGP12ID = s.SolicitudGP12ID
                  AND gc.Activo = 1
            ) gpcaja
            OUTER APPLY
            (
                SELECT CodigoBarras = COALESCE
                (
                    NULLIF(LTRIM(RTRIM(pc.EtiquetaFolio)), N''''),
                    NULLIF(LTRIM(RTRIM(pc.Etiqueta)), N''''),
                    NULLIF(LTRIM(RTRIM(gpcaja.CodigoCaja)), N''''),
                    NULLIF(LTRIM(RTRIM(ci.CodigoBarras)), N''''),
                    CONCAT(N''GP12-ROJA-'', s.SolicitudGP12ID, N''-'', e.SolicitudEtiquetaID)
                )
            ) codigo
            OUTER APPLY
            (
                SELECT LoteExtraido = CASE
                    WHEN codigo.CodigoBarras NOT LIKE N''GP12-%''
                         AND CHARINDEX(N''-'', REVERSE(codigo.CodigoBarras)) > 0
                        THEN RIGHT(codigo.CodigoBarras, CHARINDEX(N''-'', REVERSE(codigo.CodigoBarras)) - 1)
                    ELSE NULL
                END
            ) lote
            OUTER APPLY
            (
                SELECT NormalOrigen = UPPER(
                    REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                        LTRIM(RTRIM(ISNULL(s.NumeroParte, N''''))),
                        N''.'', N''''), N''-'', N''''), N''_'', N''''), N'' '', N''''), NCHAR(39), N''''), N''/'', N''''), N''\'', N'''')
                )
            ) norm
            OUTER APPLY
            (
                SELECT
                    ParteID = CASE WHEN COUNT(*) = 1 THEN MAX(p.ParteID) ELSE NULL END
                FROM dbo.ERP_Partes p
                WHERE p.Activo = 1
                  AND
                  (
                      (s.ParteID IS NOT NULL AND p.ParteID = s.ParteID)
                      OR UPPER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                            LTRIM(RTRIM(ISNULL(p.NumeroParte, N''''))),
                            N''.'', N''''), N''-'', N''''), N''_'', N''''), N'' '', N''''), NCHAR(39), N''''), N''/'', N''''), N''\'', N'''')) = norm.NormalOrigen
                      OR UPPER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                            LTRIM(RTRIM(ISNULL(p.ReferenciaSAP, N''''))),
                            N''.'', N''''), N''-'', N''''), N''_'', N''''), N'' '', N''''), NCHAR(39), N''''), N''/'', N''''), N''\'', N'''')) = norm.NormalOrigen
                  )
            ) parte_resuelta
            LEFT JOIN dbo.ERP_Partes pcanon
                ON pcanon.ParteID = parte_resuelta.ParteID
               AND pcanon.Activo = 1
            WHERE e.Activo = 1
              AND UPPER(LTRIM(RTRIM(e.TipoEtiqueta))) = N''ROJA''
              AND
              (
                  ISNULL(e.CantidadProcesada, 0) > 0
                  OR ISNULL(e.CantidadRecibida, 0) > 0
                  OR ISNULL(e.CantidadSolicitada, 0) > 0
              )
              AND NOT EXISTS
              (
                  SELECT 1
                  FROM dbo.AlmacenScrap_Registros sr WITH (UPDLOCK, HOLDLOCK)
                  WHERE sr.Activo = 1
                    AND sr.Origen = N''GP12''
                    AND sr.OrigenReferenciaID = CONVERT(BIGINT, e.SolicitudEtiquetaID)
              );

            OPEN cur_gp12;
            FETCH NEXT FROM cur_gp12 INTO
                @G_ReferenciaID, @G_Referencia, @G_Codigo, @G_NumeroOF,
                @G_NumeroParte, @G_Designacion, @G_Cantidad, @G_Lote,
                @G_ParteID, @G_FechaOrigen, @G_Observaciones;

            WHILE @@FETCH_STATUS = 0
            BEGIN
                DELETE FROM @ResultadoGP12;

                INSERT @ResultadoGP12 (ScrapRegistroID)
                EXEC dbo.usp_AlmacenScrap_RegistrarOrigen
                    @Origen = N''GP12'',
                    @OrigenReferenciaID = @G_ReferenciaID,
                    @OrigenReferencia = @G_Referencia,
                    @CodigoBarras = @G_Codigo,
                    @NumeroOF = @G_NumeroOF,
                    @NumeroParte = @G_NumeroParte,
                    @Designacion = @G_Designacion,
                    @CantidadPiezas = @G_Cantidad,
                    @Lote = @G_Lote,
                    @ParteID = @G_ParteID,
                    @Observaciones = @G_Observaciones,
                    @UsuarioID = @UsuarioID,
                    @UsuarioNombre = @UsuarioNombre;

                UPDATE sr
                SET FechaOrigen = COALESCE(@G_FechaOrigen, sr.FechaOrigen)
                FROM dbo.AlmacenScrap_Registros sr
                INNER JOIN @ResultadoGP12 r
                    ON r.ScrapRegistroID = sr.ScrapRegistroID;

                SET @NuevosGP12 += 1;

                FETCH NEXT FROM cur_gp12 INTO
                    @G_ReferenciaID, @G_Referencia, @G_Codigo, @G_NumeroOF,
                    @G_NumeroParte, @G_Designacion, @G_Cantidad, @G_Lote,
                    @G_ParteID, @G_FechaOrigen, @G_Observaciones;
            END;

            CLOSE cur_gp12;
            DEALLOCATE cur_gp12;
        END;

        ----------------------------------------------------------------------
        -- CALIDAD: disposiciones con etiqueta ROJA / resultado SCRAP.
        ----------------------------------------------------------------------
        IF OBJECT_ID(N''dbo.Calidad_DisposicionesMaterial'', N''U'') IS NOT NULL
           AND OBJECT_ID(N''dbo.Calidad_Inspecciones'', N''U'') IS NOT NULL
        BEGIN
            DECLARE
                @C_Referencia NVARCHAR(120),
                @C_Codigo NVARCHAR(500),
                @C_NumeroOF NVARCHAR(80),
                @C_NumeroParte NVARCHAR(120),
                @C_Designacion NVARCHAR(300),
                @C_Cantidad INT,
                @C_Lote NVARCHAR(120),
                @C_ParteID INT,
                @C_FechaOrigen DATETIME2(7),
                @C_Observaciones NVARCHAR(800);

            DECLARE @ResultadoCalidad TABLE (ScrapRegistroID BIGINT);

            DECLARE cur_calidad CURSOR LOCAL FAST_FORWARD FOR
            SELECT
                LEFT(CONCAT(N''DISPOSICION:'', d.DisposicionID), 120) AS Referencia,
                LEFT(codigo.CodigoBarras, 500) AS CodigoBarras,
                LEFT(
                    COALESCE(
                        NULLIF(LTRIM(RTRIM(ci.OrdenTrabajo)), N''''),
                        NULLIF(LTRIM(RTRIM(sp.NumeroOFRecibida)), N''''),
                        NULLIF(LTRIM(RTRIM(sp.FolioSolicitud)), N''''),
                        CONCAT(N''CALIDAD-'', ci.InspeccionID)
                    ),
                    80
                ) AS NumeroOF,
                LEFT(
                    COALESCE(
                        NULLIF(LTRIM(RTRIM(pcanon.NumeroParte)), N''''),
                        NULLIF(LTRIM(RTRIM(ci.NumeroParte)), N''''),
                        N''SIN-PARTE''
                    ),
                    120
                ) AS NumeroParte,
                LEFT(
                    COALESCE(
                        NULLIF(LTRIM(RTRIM(pcanon.Designacion)), N''''),
                        NULLIF(LTRIM(RTRIM(pcanon.Descripcion)), N''''),
                        NULLIF(LTRIM(RTRIM(ci.NumeroParte)), N''''),
                        N''SCRAP CALIDAD''
                    ),
                    300
                ) AS Designacion,
                CASE
                    WHEN ISNULL(d.CantidadScrap, 0) > 0 THEN d.CantidadScrap
                    ELSE d.CantidadAfectada
                END AS CantidadPiezas,
                LEFT(
                    COALESCE(
                        NULLIF(LTRIM(RTRIM(pc_unica.LoteMaterial)), N''''),
                        NULLIF(LTRIM(RTRIM(lote.LoteExtraido)), N''''),
                        CONCAT(N''CAL-'', ci.InspeccionID, N''-'', d.DisposicionID)
                    ),
                    120
                ) AS Lote,
                pcanon.ParteID,
                CONVERT(DATETIME2(7), COALESCE(d.FechaFin, d.FechaModificacion, d.FechaCreacion, d.FechaInicio)) AS FechaOrigen,
                LEFT(
                    CONCAT(
                        N''Etiqueta ROJA / Scrap de Calidad. InspeccionID='', ci.InspeccionID,
                        N''; DisposicionID='', d.DisposicionID,
                        N''; Resultado='', ISNULL(d.ResultadoFinal, N''''),
                        N''; Disposicion='', ISNULL(d.Disposicion, N'''')
                    ),
                    800
                ) AS Observaciones
            FROM dbo.Calidad_DisposicionesMaterial d
            INNER JOIN dbo.Calidad_Inspecciones ci
                ON ci.InspeccionID = d.InspeccionID
            LEFT JOIN dbo.SolicitudesProduccion sp
                ON sp.SolicitudProduccionID = ci.SolicitudProduccionID
               AND sp.Activo = 1
            OUTER APPLY
            (
                SELECT
                    CodigoBarras = CASE WHEN COUNT_BIG(*) = 1 THEN MAX(COALESCE(NULLIF(LTRIM(RTRIM(pc.EtiquetaFolio)), N''''), NULLIF(LTRIM(RTRIM(pc.Etiqueta)), N''''), NULLIF(LTRIM(RTRIM(pc.FolioCaja)), N''''))) ELSE NULL END,
                    LoteMaterial = CASE WHEN COUNT_BIG(*) = 1 THEN MAX(NULLIF(LTRIM(RTRIM(pc.LoteMaterial)), N'''')) ELSE NULL END
                FROM dbo.Produccion_Cajas pc
                WHERE ci.EjecucionProduccionID IS NOT NULL
                  AND pc.EjecucionProduccionID = ci.EjecucionProduccionID
                  AND pc.Activo = 1
            ) pc_unica
            OUTER APPLY
            (
                SELECT CodigoBarras = COALESCE
                (
                    NULLIF(LTRIM(RTRIM(ci.CodigoBarras)), N''''),
                    NULLIF(LTRIM(RTRIM(pc_unica.CodigoBarras)), N''''),
                    CONCAT(N''CALIDAD-ROJA-'', ci.InspeccionID, N''-'', d.DisposicionID)
                )
            ) codigo
            OUTER APPLY
            (
                SELECT LoteExtraido = CASE
                    WHEN codigo.CodigoBarras NOT LIKE N''CALIDAD-%''
                         AND CHARINDEX(N''-'', REVERSE(codigo.CodigoBarras)) > 0
                        THEN RIGHT(codigo.CodigoBarras, CHARINDEX(N''-'', REVERSE(codigo.CodigoBarras)) - 1)
                    ELSE NULL
                END
            ) lote
            OUTER APPLY
            (
                SELECT NormalOrigen = UPPER(
                    REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                        LTRIM(RTRIM(ISNULL(ci.NumeroParte, N''''))),
                        N''.'', N''''), N''-'', N''''), N''_'', N''''), N'' '', N''''), NCHAR(39), N''''), N''/'', N''''), N''\'', N'''')
                )
            ) norm
            OUTER APPLY
            (
                SELECT ParteID = CASE WHEN COUNT(*) = 1 THEN MAX(p.ParteID) ELSE NULL END
                FROM dbo.ERP_Partes p
                WHERE p.Activo = 1
                  AND
                  (
                      (ci.ParteID IS NOT NULL AND p.ParteID = ci.ParteID)
                      OR UPPER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                            LTRIM(RTRIM(ISNULL(p.NumeroParte, N''''))),
                            N''.'', N''''), N''-'', N''''), N''_'', N''''), N'' '', N''''), NCHAR(39), N''''), N''/'', N''''), N''\'', N'''')) = norm.NormalOrigen
                      OR UPPER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                            LTRIM(RTRIM(ISNULL(p.ReferenciaSAP, N''''))),
                            N''.'', N''''), N''-'', N''''), N''_'', N''''), N'' '', N''''), NCHAR(39), N''''), N''/'', N''''), N''\'', N'''')) = norm.NormalOrigen
                  )
            ) parte_resuelta
            LEFT JOIN dbo.ERP_Partes pcanon
                ON pcanon.ParteID = parte_resuelta.ParteID
               AND pcanon.Activo = 1
            WHERE d.Activo = 1
              AND ISNULL(d.CantidadAfectada, 0) > 0
              AND
              (
                  UPPER(LTRIM(RTRIM(ISNULL(d.Etiqueta, N'''')))) = N''ROJA''
                  OR UPPER(LTRIM(RTRIM(ISNULL(d.Disposicion, N'''')))) = N''SCRAP''
                  OR UPPER(LTRIM(RTRIM(ISNULL(d.ResultadoFinal, N'''')))) = N''SCRAP''
                  OR ISNULL(d.CantidadScrap, 0) > 0
              )
              AND NOT EXISTS
              (
                  SELECT 1
                  FROM dbo.AlmacenScrap_Registros sr WITH (UPDLOCK, HOLDLOCK)
                  WHERE sr.Activo = 1
                    AND sr.Origen = N''CALIDAD''
                    AND sr.OrigenReferenciaID IS NULL
                    AND sr.OrigenReferencia = CONCAT(N''DISPOSICION:'', d.DisposicionID)
              );

            OPEN cur_calidad;
            FETCH NEXT FROM cur_calidad INTO
                @C_Referencia, @C_Codigo, @C_NumeroOF, @C_NumeroParte,
                @C_Designacion, @C_Cantidad, @C_Lote, @C_ParteID,
                @C_FechaOrigen, @C_Observaciones;

            WHILE @@FETCH_STATUS = 0
            BEGIN
                DELETE FROM @ResultadoCalidad;

                INSERT @ResultadoCalidad (ScrapRegistroID)
                EXEC dbo.usp_AlmacenScrap_RegistrarOrigen
                    @Origen = N''CALIDAD'',
                    @OrigenReferenciaID = NULL,
                    @OrigenReferencia = @C_Referencia,
                    @CodigoBarras = @C_Codigo,
                    @NumeroOF = @C_NumeroOF,
                    @NumeroParte = @C_NumeroParte,
                    @Designacion = @C_Designacion,
                    @CantidadPiezas = @C_Cantidad,
                    @Lote = @C_Lote,
                    @ParteID = @C_ParteID,
                    @Observaciones = @C_Observaciones,
                    @UsuarioID = @UsuarioID,
                    @UsuarioNombre = @UsuarioNombre;

                UPDATE sr
                SET FechaOrigen = COALESCE(@C_FechaOrigen, sr.FechaOrigen)
                FROM dbo.AlmacenScrap_Registros sr
                INNER JOIN @ResultadoCalidad r
                    ON r.ScrapRegistroID = sr.ScrapRegistroID;

                SET @NuevosCalidad += 1;

                FETCH NEXT FROM cur_calidad INTO
                    @C_Referencia, @C_Codigo, @C_NumeroOF, @C_NumeroParte,
                    @C_Designacion, @C_Cantidad, @C_Lote, @C_ParteID,
                    @C_FechaOrigen, @C_Observaciones;
            END;

            CLOSE cur_calidad;
            DEALLOCATE cur_calidad;

            ------------------------------------------------------------------
            -- Compatibilidad: inspeccion ROJA + EsScrap sin disposicion.
            ------------------------------------------------------------------
            DECLARE cur_calidad_inspeccion CURSOR LOCAL FAST_FORWARD FOR
            SELECT
                LEFT(CONCAT(N''INSPECCION:'', ci.InspeccionID), 120) AS Referencia,
                LEFT(codigo_i.CodigoBarras, 500) AS CodigoBarras,
                LEFT(
                    COALESCE(
                        NULLIF(LTRIM(RTRIM(ci.OrdenTrabajo)), N''''),
                        NULLIF(LTRIM(RTRIM(spi.NumeroOFRecibida)), N''''),
                        NULLIF(LTRIM(RTRIM(spi.FolioSolicitud)), N''''),
                        CONCAT(N''CALIDAD-'', ci.InspeccionID)
                    ), 80) AS NumeroOF,
                LEFT(
                    COALESCE(
                        NULLIF(LTRIM(RTRIM(pcanon_i.NumeroParte)), N''''),
                        NULLIF(LTRIM(RTRIM(ci.NumeroParte)), N''''),
                        N''SIN-PARTE''
                    ), 120) AS NumeroParte,
                LEFT(
                    COALESCE(
                        NULLIF(LTRIM(RTRIM(pcanon_i.Designacion)), N''''),
                        NULLIF(LTRIM(RTRIM(pcanon_i.Descripcion)), N''''),
                        NULLIF(LTRIM(RTRIM(ci.NumeroParte)), N''''),
                        N''SCRAP CALIDAD''
                    ), 300) AS Designacion,
                CONVERT(INT, CEILING(ISNULL(ci.CantidadTotal, 0))) AS CantidadPiezas,
                LEFT(
                    COALESCE(
                        NULLIF(LTRIM(RTRIM(pc_unica_i.LoteMaterial)), N''''),
                        NULLIF(LTRIM(RTRIM(lote_i.LoteExtraido)), N''''),
                        CONCAT(N''CAL-INS-'', ci.InspeccionID)
                    ), 120) AS Lote,
                pcanon_i.ParteID,
                CONVERT(DATETIME2(7), COALESCE(ci.FechaModificacion, ci.FechaCreacion)) AS FechaOrigen,
                LEFT(CONCAT(
                    N''Inspeccion de Calidad marcada ROJA y EsScrap. InspeccionID='', ci.InspeccionID,
                    N''; ResultadoCalidad='', ISNULL(ci.ResultadoCalidad, N''''),
                    N''; Estado='', ISNULL(ci.Estado, N'''')), 800) AS Observaciones
            FROM dbo.Calidad_Inspecciones ci
            LEFT JOIN dbo.SolicitudesProduccion spi
                ON spi.SolicitudProduccionID = ci.SolicitudProduccionID
               AND spi.Activo = 1
            OUTER APPLY
            (
                SELECT
                    CodigoBarras = CASE WHEN COUNT_BIG(*) = 1 THEN MAX(COALESCE(NULLIF(LTRIM(RTRIM(pc.EtiquetaFolio)), N''''), NULLIF(LTRIM(RTRIM(pc.Etiqueta)), N''''), NULLIF(LTRIM(RTRIM(pc.FolioCaja)), N''''))) ELSE NULL END,
                    LoteMaterial = CASE WHEN COUNT_BIG(*) = 1 THEN MAX(NULLIF(LTRIM(RTRIM(pc.LoteMaterial)), N'''')) ELSE NULL END
                FROM dbo.Produccion_Cajas pc
                WHERE ci.EjecucionProduccionID IS NOT NULL
                  AND pc.EjecucionProduccionID = ci.EjecucionProduccionID
                  AND pc.Activo = 1
            ) pc_unica_i
            OUTER APPLY
            (
                SELECT CodigoBarras = COALESCE(
                    NULLIF(LTRIM(RTRIM(ci.CodigoBarras)), N''''),
                    NULLIF(LTRIM(RTRIM(pc_unica_i.CodigoBarras)), N''''),
                    CONCAT(N''CALIDAD-ROJA-INSPECCION-'', ci.InspeccionID))
            ) codigo_i
            OUTER APPLY
            (
                SELECT LoteExtraido = CASE
                    WHEN codigo_i.CodigoBarras NOT LIKE N''CALIDAD-%''
                         AND CHARINDEX(N''-'', REVERSE(codigo_i.CodigoBarras)) > 0
                        THEN RIGHT(codigo_i.CodigoBarras, CHARINDEX(N''-'', REVERSE(codigo_i.CodigoBarras)) - 1)
                    ELSE NULL END
            ) lote_i
            OUTER APPLY
            (
                SELECT NormalOrigen = UPPER(
                    REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                        LTRIM(RTRIM(ISNULL(ci.NumeroParte, N''''))),
                        N''.'', N''''), N''-'', N''''), N''_'', N''''), N'' '', N''''), NCHAR(39), N''''), N''/'', N''''), N''\'', N''''))
            ) norm_i
            OUTER APPLY
            (
                SELECT ParteID = CASE WHEN COUNT(*) = 1 THEN MAX(p.ParteID) ELSE NULL END
                FROM dbo.ERP_Partes p
                WHERE p.Activo = 1
                  AND (
                      (ci.ParteID IS NOT NULL AND p.ParteID = ci.ParteID)
                      OR UPPER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                            LTRIM(RTRIM(ISNULL(p.NumeroParte, N''''))),
                            N''.'', N''''), N''-'', N''''), N''_'', N''''), N'' '', N''''), NCHAR(39), N''''), N''/'', N''''), N''\'', N'''')) = norm_i.NormalOrigen
                      OR UPPER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                            LTRIM(RTRIM(ISNULL(p.ReferenciaSAP, N''''))),
                            N''.'', N''''), N''-'', N''''), N''_'', N''''), N'' '', N''''), NCHAR(39), N''''), N''/'', N''''), N''\'', N'''')) = norm_i.NormalOrigen)
            ) parte_resuelta_i
            LEFT JOIN dbo.ERP_Partes pcanon_i
                ON pcanon_i.ParteID = parte_resuelta_i.ParteID
               AND pcanon_i.Activo = 1
            WHERE UPPER(LTRIM(RTRIM(ISNULL(ci.Etiqueta, N'''')))) = N''ROJA''
              AND ISNULL(ci.EsScrap, 0) = 1
              AND ISNULL(ci.CantidadTotal, 0) > 0
              AND NOT EXISTS
              (
                  SELECT 1
                  FROM dbo.Calidad_DisposicionesMaterial d2
                  WHERE d2.InspeccionID = ci.InspeccionID
                    AND d2.Activo = 1
                    AND (
                        UPPER(LTRIM(RTRIM(ISNULL(d2.Etiqueta, N'''')))) = N''ROJA''
                        OR UPPER(LTRIM(RTRIM(ISNULL(d2.Disposicion, N'''')))) = N''SCRAP''
                        OR UPPER(LTRIM(RTRIM(ISNULL(d2.ResultadoFinal, N'''')))) = N''SCRAP''
                        OR ISNULL(d2.CantidadScrap, 0) > 0)
              )
              AND NOT EXISTS
              (
                  SELECT 1
                  FROM dbo.AlmacenScrap_Registros sr WITH (UPDLOCK, HOLDLOCK)
                  WHERE sr.Activo = 1
                    AND sr.Origen = N''CALIDAD''
                    AND sr.OrigenReferenciaID IS NULL
                    AND sr.OrigenReferencia = CONCAT(N''INSPECCION:'', ci.InspeccionID)
              );

            OPEN cur_calidad_inspeccion;
            FETCH NEXT FROM cur_calidad_inspeccion INTO
                @C_Referencia, @C_Codigo, @C_NumeroOF, @C_NumeroParte,
                @C_Designacion, @C_Cantidad, @C_Lote, @C_ParteID,
                @C_FechaOrigen, @C_Observaciones;

            WHILE @@FETCH_STATUS = 0
            BEGIN
                DELETE FROM @ResultadoCalidad;
                INSERT @ResultadoCalidad (ScrapRegistroID)
                EXEC dbo.usp_AlmacenScrap_RegistrarOrigen
                    @Origen = N''CALIDAD'',
                    @OrigenReferenciaID = NULL,
                    @OrigenReferencia = @C_Referencia,
                    @CodigoBarras = @C_Codigo,
                    @NumeroOF = @C_NumeroOF,
                    @NumeroParte = @C_NumeroParte,
                    @Designacion = @C_Designacion,
                    @CantidadPiezas = @C_Cantidad,
                    @Lote = @C_Lote,
                    @ParteID = @C_ParteID,
                    @Observaciones = @C_Observaciones,
                    @UsuarioID = @UsuarioID,
                    @UsuarioNombre = @UsuarioNombre;

                UPDATE sr
                SET FechaOrigen = COALESCE(@C_FechaOrigen, sr.FechaOrigen)
                FROM dbo.AlmacenScrap_Registros sr
                INNER JOIN @ResultadoCalidad r ON r.ScrapRegistroID = sr.ScrapRegistroID;

                SET @NuevosCalidad += 1;
                FETCH NEXT FROM cur_calidad_inspeccion INTO
                    @C_Referencia, @C_Codigo, @C_NumeroOF, @C_NumeroParte,
                    @C_Designacion, @C_Cantidad, @C_Lote, @C_ParteID,
                    @C_FechaOrigen, @C_Observaciones;
            END;

            CLOSE cur_calidad_inspeccion;
            DEALLOCATE cur_calidad_inspeccion;
        END;

        ----------------------------------------------------------------------
        -- Si el mismo lote entro directo como MP Molido, cierra Scrap.
        -- La vinculacion es conservadora: un solo Scrap y un solo movimiento.
        ----------------------------------------------------------------------
        IF OBJECT_ID(N''dbo.AlmacenMP_Movimientos'', N''U'') IS NOT NULL
        BEGIN
            ;WITH ScrapCandidato AS
            (
                SELECT
                    s.ScrapRegistroID,
                    s.Estatus,
                    s.ParteID,
                    s.SolicitudProduccionID,
                    s.Lote,
                    LoteNorm = UPPER(LTRIM(RTRIM(s.Lote))),
                    s.FechaOrigen,
                    MaterialEsperadoID = mat.MaterialID,
                    ScrapMismoLote = COUNT_BIG(*) OVER (PARTITION BY UPPER(LTRIM(RTRIM(s.Lote))))
                FROM dbo.AlmacenScrap_Registros s WITH (UPDLOCK, HOLDLOCK)
                OUTER APPLY
                (
                    SELECT MaterialID = CASE
                        WHEN COUNT(DISTINCT dt.MaterialID) = 1 THEN MAX(dt.MaterialID)
                        ELSE NULL
                    END
                    FROM dbo.ERP_ParteDatosTecnicos dt
                    INNER JOIN dbo.ERP_Materiales em
                        ON em.MaterialID = dt.MaterialID
                       AND em.Activo = 1
                    WHERE s.ParteID IS NOT NULL
                      AND dt.ParteID = s.ParteID
                      AND dt.Activo = 1
                      AND dt.MaterialID IS NOT NULL
                ) mat
                WHERE s.Activo = 1
                  AND s.Estatus IN (N''PENDIENTE_RECEPCION'', N''RECIBIDO'')
                  AND NULLIF(LTRIM(RTRIM(s.Lote)), N'''') IS NOT NULL
                  AND UPPER(LTRIM(RTRIM(s.Lote))) <> N''S/L''
                  AND s.MPMovimientoID IS NULL
            ),
            MPCandidato AS
            (
                SELECT
                    s.ScrapRegistroID,
                    s.Estatus,
                    m.MovimientoID,
                    m.MaterialID,
                    m.Cantidad,
                    m.Unidad,
                    m.UbicacionID,
                    m.FechaMovimiento,
                    MovimientoMismoLote = COUNT_BIG(*) OVER (PARTITION BY s.ScrapRegistroID)
                FROM ScrapCandidato s
                INNER JOIN dbo.AlmacenMP_Movimientos m WITH (UPDLOCK, HOLDLOCK)
                    ON m.Activo = 1
                   AND m.TipoMovimiento = N''Entrada''
                   AND UPPER(LTRIM(RTRIM(ISNULL(m.TipoMP, N'''')))) IN (N''M'', N''MOLIDO'')
                   AND UPPER(LTRIM(RTRIM(ISNULL(m.Lote, N'''')))) = s.LoteNorm
                   AND m.FechaMovimiento >= COALESCE(s.FechaOrigen, CONVERT(DATETIME2(7), ''19000101''))
                   AND (s.MaterialEsperadoID IS NULL OR s.MaterialEsperadoID = m.MaterialID)
                   AND (s.SolicitudProduccionID IS NULL OR m.SolicitudProduccionID IS NULL OR s.SolicitudProduccionID = m.SolicitudProduccionID)
                   AND NOT EXISTS
                   (
                       SELECT 1
                       FROM dbo.AlmacenScrap_Registros usado
                       WHERE usado.MPMovimientoID = m.MovimientoID
                   )
                WHERE s.ScrapMismoLote = 1
            ),
            Unicos AS
            (
                SELECT *
                FROM MPCandidato
                WHERE MovimientoMismoLote = 1
            )
            UPDATE s
            SET
                Estatus = N''MOLIDO'',
                MaterialIDMolido = u.MaterialID,
                PesoMolidoKg = CASE
                    WHEN UPPER(LTRIM(RTRIM(ISNULL(u.Unidad, N'''')))) IN (N''KG'', N''KGS'', N''KILOGRAMO'', N''KILOGRAMOS'')
                        THEN ABS(CONVERT(DECIMAL(18,4), u.Cantidad))
                    ELSE NULL
                END,
                UbicacionIDMolido = u.UbicacionID,
                MPMovimientoID = CONVERT(INT, u.MovimientoID),
                FechaMolido = u.FechaMovimiento,
                MolidoPorUsuarioID = @UsuarioID,
                MolidoPorNombre = @UsuarioNombre,
                FechaModificacion = SYSUTCDATETIME(),
                ActualizadoPor = @UsuarioNombre
            OUTPUT
                inserted.ScrapRegistroID,
                deleted.Estatus,
                inserted.MPMovimientoID,
                inserted.MaterialIDMolido,
                inserted.FechaMolido
            INTO #VinculosMP
                (ScrapRegistroID, EstatusAnterior, MPMovimientoID, MaterialIDMolido, FechaMolido)
            FROM dbo.AlmacenScrap_Registros s
            INNER JOIN Unicos u
                ON u.ScrapRegistroID = s.ScrapRegistroID;

            INSERT dbo.AlmacenScrap_Historial
            (
                ScrapRegistroID,
                Evento,
                EstatusAnterior,
                EstatusNuevo,
                Detalle,
                UsuarioID,
                UsuarioNombre,
                FechaEvento
            )
            SELECT
                v.ScrapRegistroID,
                N''MP_MOLIDO_DETECTADO'',
                v.EstatusAnterior,
                N''MOLIDO'',
                CONCAT(
                    N''Entrada MP Molido detectada por lote. Movimiento MP #'', v.MPMovimientoID,
                    N''; MaterialID='', v.MaterialIDMolido
                ),
                @UsuarioID,
                @UsuarioNombre,
                SYSDATETIME()
            FROM #VinculosMP v;

            SET @VinculadosMP = @@ROWCOUNT;
        END;

        COMMIT TRANSACTION;

        SELECT
            @NuevosGP12 AS NuevosGP12,
            @NuevosCalidad AS NuevosCalidad,
            @VinculadosMP AS VinculadosMP;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END';

    EXEC dbo.usp_AlmacenScrap_SincronizarOrigenes
        @UsuarioID = NULL,
        @UsuarioNombre = N'INSTALADOR SCRAP V15';

    COMMIT TRANSACTION;

    PRINT N'SCRAP V15 aplicado correctamente en la base.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
