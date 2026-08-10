USE [ERP_QUELL];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

/*
    Carga controlada de stock PT para pruebas funcionales.

    - Selecciona de forma determinista las primeras partes activas.
    - Prioriza números de parte sin stock disponible.
    - Crea una caja liberada y un movimiento de Entrada por parte.
    - Es idempotente: no duplica etiquetas ni referencias al ejecutarlo otra vez.
    - No toca Planeación ni genera descuentos.

    Para ejecutar la carga cambia @Confirmar a 1.
    Para retirar únicamente estos datos utiliza 97_Limpiar_Stock_PT_Pruebas.sql.
*/

DECLARE @Confirmar BIT = 0;
DECLARE @NumeroPartes INT = 5;
DECLARE @CantidadPorCaja INT = 100;
DECLARE @StockMinimo INT = 20;
DECLARE @StockAviso INT = 50;

IF @NumeroPartes <= 0
    THROW 51000, 'El número de partes para prueba debe ser mayor que cero.', 1;

IF @CantidadPorCaja <= 0
    THROW 51001, 'La cantidad por caja debe ser mayor que cero.', 1;

IF @StockMinimo < 0 OR @StockAviso < @StockMinimo
    THROW 51002, 'Los niveles de stock de prueba no son válidos.', 1;

IF OBJECT_ID(N'dbo.ERP_Partes', N'U') IS NULL
    THROW 51003, 'No existe dbo.ERP_Partes.', 1;

IF OBJECT_ID(N'dbo.AlmacenPT_Cajas', N'U') IS NULL
    THROW 51004, 'No existe dbo.AlmacenPT_Cajas. Ejecuta primero la estructura de Almacén.', 1;

IF OBJECT_ID(N'dbo.AlmacenPT_Movimientos', N'U') IS NULL
    THROW 51005, 'No existe dbo.AlmacenPT_Movimientos. Ejecuta primero la estructura de Almacén.', 1;

IF OBJECT_ID(N'dbo.ERP_Ubicaciones', N'U') IS NULL
    THROW 51006, 'No existe dbo.ERP_Ubicaciones.', 1;

IF OBJECT_ID(N'dbo.vw_AlmacenPTInventario', N'V') IS NULL
    THROW 51007, 'No existe dbo.vw_AlmacenPTInventario. Ejecuta el script 04 corregido.', 1;

IF COL_LENGTH(N'dbo.AlmacenPT_Movimientos', N'ReferenciaOperacion') IS NULL
    THROW 51008, 'Falta ReferenciaOperacion en AlmacenPT_Movimientos. Ejecuta el script 04 corregido.', 1;

CREATE TABLE #PartesPrueba
(
    ParteID INT NOT NULL PRIMARY KEY,
    NumeroParte NVARCHAR(120) NOT NULL,
    Descripcion NVARCHAR(250) NULL,
    DisponibleActual INT NOT NULL,
    CantidadPrueba INT NOT NULL,
    Etiqueta NVARCHAR(120) NOT NULL,
    ReferenciaOperacion NVARCHAR(120) NOT NULL,
    YaCargado BIT NOT NULL
);

INSERT #PartesPrueba
(
    ParteID,
    NumeroParte,
    Descripcion,
    DisponibleActual,
    CantidadPrueba,
    Etiqueta,
    ReferenciaOperacion,
    YaCargado
)
SELECT TOP (@NumeroPartes)
    p.ParteID,
    p.NumeroParte,
    COALESCE(NULLIF(p.Designacion, N''), p.Descripcion),
    ISNULL(v.Disponible, 0),
    @CantidadPorCaja,
    CONCAT(N'TEST-PT-20260714-', p.ParteID),
    CONCAT(N'TEST-PT-STOCK-', p.ParteID),
    CASE
        WHEN EXISTS
        (
            SELECT 1
            FROM dbo.AlmacenPT_Cajas c
            WHERE c.Etiqueta = CONCAT(N'TEST-PT-20260714-', p.ParteID)
        ) THEN 1
        ELSE 0
    END
FROM dbo.ERP_Partes p
LEFT JOIN dbo.vw_AlmacenPTInventario v
    ON v.ParteID = p.ParteID
WHERE ISNULL(p.Activo, 1) = 1
ORDER BY
    CASE WHEN ISNULL(v.Disponible, 0) = 0 THEN 0 ELSE 1 END,
    p.ParteID;

IF NOT EXISTS (SELECT 1 FROM #PartesPrueba)
    THROW 51009, 'No hay números de parte activos disponibles para la prueba.', 1;

SELECT
    ParteID,
    NumeroParte,
    Descripcion,
    DisponibleActual,
    CantidadPrueba,
    DisponibleDespues = DisponibleActual + CASE WHEN YaCargado = 1 THEN 0 ELSE CantidadPrueba END,
    Etiqueta,
    ReferenciaOperacion,
    Estado = CASE WHEN YaCargado = 1 THEN N'YA EXISTE; SE VALIDARÁ' ELSE N'LISTO PARA INSERTAR' END
FROM #PartesPrueba
ORDER BY ParteID;

IF @Confirmar = 0
BEGIN
    PRINT 'PREVISUALIZACIÓN: no se insertó stock PT. Cambia @Confirmar a 1 para ejecutar.';
    RETURN;
END;

BEGIN TRY
    BEGIN TRAN;

    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.ERP_Ubicaciones
        WHERE Activo = 1
          AND Almacen = N'PT'
          AND Rack = N'PT-SIN-UBICAR'
    )
    BEGIN
        INSERT dbo.ERP_Ubicaciones
        (
            Almacen, Rack, Nivel, Posicion,
            FechaCreacion, CreadoPor, Activo
        )
        VALUES
        (
            N'PT', N'PT-SIN-UBICAR', NULL, NULL,
            SYSUTCDATETIME(), N'script-prueba-pt', 1
        );
    END;

    DECLARE @UbicacionID INT;
    SELECT TOP (1) @UbicacionID = UbicacionID
    FROM dbo.ERP_Ubicaciones
    WHERE Activo = 1
      AND Almacen = N'PT'
    ORDER BY CASE WHEN Rack = N'PT-SIN-UBICAR' THEN 0 ELSE 1 END, UbicacionID;

    DECLARE
        @ParteID INT,
        @NumeroParte NVARCHAR(120),
        @Cantidad INT,
        @Etiqueta NVARCHAR(120),
        @Referencia NVARCHAR(120),
        @CajaID INT,
        @NumeroCaja INT;

    DECLARE partes CURSOR LOCAL FAST_FORWARD FOR
        SELECT ParteID, NumeroParte, CantidadPrueba, Etiqueta, ReferenciaOperacion
        FROM #PartesPrueba
        ORDER BY ParteID;

    OPEN partes;
    FETCH NEXT FROM partes INTO @ParteID, @NumeroParte, @Cantidad, @Etiqueta, @Referencia;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        SET @CajaID = NULL;

        SELECT TOP (1) @CajaID = CajaID
        FROM dbo.AlmacenPT_Cajas
        WHERE Etiqueta = @Etiqueta;

        IF @CajaID IS NULL
        BEGIN
            SET @NumeroCaja = 900000 + @ParteID;

            INSERT dbo.AlmacenPT_Cajas
            (
                ParteID,
                NumeroOF,
                Etiqueta,
                NumeroCaja,
                CantidadInicial,
                LoteEtiqueta,
                EstadoCalidad,
                UbicacionID,
                FechaEntrada,
                FechaCreacion,
                CreadoPor,
                Activo
            )
            VALUES
            (
                @ParteID,
                N'OF-PRUEBA-PT',
                @Etiqueta,
                @NumeroCaja,
                @Cantidad,
                N'LOTE-PRUEBA-PT',
                N'Liberado',
                @UbicacionID,
                SYSDATETIME(),
                SYSUTCDATETIME(),
                N'script-prueba-pt',
                1
            );

            SET @CajaID = CONVERT(INT, SCOPE_IDENTITY());
        END;

        IF NOT EXISTS
        (
            SELECT 1
            FROM dbo.AlmacenPT_Movimientos
            WHERE CajaID = @CajaID
              AND CreadoPor = N'script-prueba-pt'
              AND Observaciones = N'Stock PT temporal para pruebas funcionales'
        )
        BEGIN
            EXEC sys.sp_executesql
                N'
                INSERT dbo.AlmacenPT_Movimientos
                (
                    CajaID, ParteID, NumeroOF, TipoMovimiento, Cantidad,
                    UbicacionID, EstadoCalidad, ResponsableUsuarioID,
                    Observaciones, ReferenciaOperacion,
                    FechaMovimiento, FechaCreacion, CreadoPor, Activo
                )
                VALUES
                (
                    @CajaID, @ParteID, N''OF-PRUEBA-PT'', N''Entrada'', @Cantidad,
                    @UbicacionID, N''Liberado'', NULL,
                    N''Stock PT temporal para pruebas funcionales'', @Referencia,
                    SYSDATETIME(), SYSUTCDATETIME(), N''script-prueba-pt'', 1
                );',
                N'@CajaID INT, @ParteID INT, @Cantidad INT, @UbicacionID INT, @Referencia NVARCHAR(120)',
                @CajaID = @CajaID,
                @ParteID = @ParteID,
                @Cantidad = @Cantidad,
                @UbicacionID = @UbicacionID,
                @Referencia = @Referencia;
        END;

        FETCH NEXT FROM partes INTO @ParteID, @NumeroParte, @Cantidad, @Etiqueta, @Referencia;
    END;

    CLOSE partes;
    DEALLOCATE partes;

    IF COL_LENGTH(N'dbo.ERP_Partes', N'StockConfigurado') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql
            N'
            UPDATE p
            SET
                StockMinimo = CASE WHEN ISNULL(p.StockConfigurado, 0) = 0 THEN @Minimo ELSE p.StockMinimo END,
                StockAviso = CASE WHEN ISNULL(p.StockConfigurado, 0) = 0 THEN @Aviso ELSE p.StockAviso END,
                StockConfigurado = 1
            FROM dbo.ERP_Partes p
            INNER JOIN #PartesPrueba t ON t.ParteID = p.ParteID;',
            N'@Minimo INT, @Aviso INT',
            @Minimo = @StockMinimo,
            @Aviso = @StockAviso;
    END;

    COMMIT;
END TRY
BEGIN CATCH
    IF CURSOR_STATUS('local', 'partes') >= 0
    BEGIN
        CLOSE partes;
        DEALLOCATE partes;
    END;

    IF @@TRANCOUNT > 0
        ROLLBACK;

    THROW;
END CATCH;

SELECT
    t.ParteID,
    t.NumeroParte,
    t.Etiqueta,
    v.Cajas,
    v.SaldoFisico,
    v.Retenido,
    v.Disponible,
    v.StockMinimo,
    v.StockAviso,
    v.Semaforo
FROM #PartesPrueba t
LEFT JOIN dbo.vw_AlmacenPTInventario v
    ON v.ParteID = t.ParteID
ORDER BY t.ParteID;

PRINT 'Stock PT de prueba cargado correctamente.';
GO
