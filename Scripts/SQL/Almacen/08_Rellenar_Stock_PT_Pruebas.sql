USE [ERP_QUELL];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

/*
    RELLENO MASIVO DE STOCK PT PARA PRUEBAS

    Objetivo:
    - Dar existencia temporal variada a todos los números de parte activos cuyo Disponible sea 0.
    - Crear una caja liberada y un movimiento de Entrada por cada parte seleccionada.
    - Generar existencias rojas, amarillas y verdes para validar el semáforo.
    - No modifica Planeación ni genera consumos o embarques.

    Esta carga queda identificada con el prefijo TEST-PT-MASIVO- y puede retirarse
    mediante 96_Limpiar_Stock_PT_Masivo_Pruebas.sql.

    IMPORTANTE:
    - @Confirmar se entrega en 1 porque esta corrección fue solicitada para cargar
      stock de prueba inmediatamente.
    - El script solo actúa sobre partes con Disponible <= 0 al momento de ejecutarse.
*/

DECLARE @Confirmar BIT = 1;
DECLARE @StockMinimo INT = 20;
DECLARE @StockAviso INT = 50;
DECLARE @SoloPartesSinStock BIT = 1;

IF @StockMinimo < 0 OR @StockAviso < @StockMinimo
    THROW 51201, 'Los niveles de stock de prueba no son válidos.', 1;

IF OBJECT_ID(N'dbo.ERP_Partes', N'U') IS NULL
    THROW 51202, 'No existe dbo.ERP_Partes.', 1;

IF OBJECT_ID(N'dbo.AlmacenPT_Cajas', N'U') IS NULL
    THROW 51203, 'No existe dbo.AlmacenPT_Cajas.', 1;

IF OBJECT_ID(N'dbo.AlmacenPT_Movimientos', N'U') IS NULL
    THROW 51204, 'No existe dbo.AlmacenPT_Movimientos.', 1;

IF OBJECT_ID(N'dbo.ERP_Ubicaciones', N'U') IS NULL
    THROW 51205, 'No existe dbo.ERP_Ubicaciones.', 1;

IF OBJECT_ID(N'dbo.vw_AlmacenPTInventario', N'V') IS NULL
    THROW 51206, 'No existe dbo.vw_AlmacenPTInventario. Ejecuta primero el script 04 corregido.', 1;

IF COL_LENGTH(N'dbo.AlmacenPT_Movimientos', N'ReferenciaOperacion') IS NULL
    THROW 51207, 'Falta ReferenciaOperacion en AlmacenPT_Movimientos. Ejecuta primero el script 04 corregido.', 1;

DECLARE @MarcaEjecucion NVARCHAR(20) =
    CONVERT(CHAR(8), SYSDATETIME(), 112)
    + REPLACE(CONVERT(CHAR(8), SYSDATETIME(), 108), ':', '');

CREATE TABLE #PartesObjetivo
(
    ParteID INT NOT NULL PRIMARY KEY,
    NumeroParte NVARCHAR(120) NOT NULL,
    Descripcion NVARCHAR(250) NULL,
    DisponibleAntes INT NOT NULL,
    CantidadObjetivo INT NOT NULL,
    Etiqueta NVARCHAR(120) NOT NULL,
    ReferenciaOperacion NVARCHAR(120) NOT NULL,
    CajaID INT NULL
);

;WITH PartesBase AS
(
    SELECT
        p.ParteID,
        p.NumeroParte,
        COALESCE(NULLIF(p.Designacion, N''), p.Descripcion) AS Descripcion,
        ISNULL(v.Disponible, 0) AS DisponibleAntes,
        ROW_NUMBER() OVER (ORDER BY p.ParteID) AS OrdenPrueba
    FROM dbo.ERP_Partes p
    LEFT JOIN dbo.vw_AlmacenPTInventario v
        ON v.ParteID = p.ParteID
    WHERE ISNULL(p.Activo, 1) = 1
      AND (@SoloPartesSinStock = 0 OR ISNULL(v.Disponible, 0) <= 0)
),
PartesConCantidad AS
(
    SELECT
        ParteID,
        NumeroParte,
        Descripcion,
        DisponibleAntes,
        CantidadObjetivo = CASE (OrdenPrueba - 1) % 10
            WHEN 0 THEN 5
            WHEN 1 THEN 12
            WHEN 2 THEN 20
            WHEN 3 THEN 25
            WHEN 4 THEN 35
            WHEN 5 THEN 50
            WHEN 6 THEN 60
            WHEN 7 THEN 75
            WHEN 8 THEN 90
            ELSE 120
        END
    FROM PartesBase
)
INSERT #PartesObjetivo
(
    ParteID,
    NumeroParte,
    Descripcion,
    DisponibleAntes,
    CantidadObjetivo,
    Etiqueta,
    ReferenciaOperacion
)
SELECT
    ParteID,
    NumeroParte,
    Descripcion,
    DisponibleAntes,
    CantidadObjetivo,
    CONCAT(N'TEST-PT-MASIVO-', @MarcaEjecucion, N'-', ParteID),
    CONCAT(N'TEST-PT-MASIVO-', @MarcaEjecucion, N'-', ParteID)
FROM PartesConCantidad;

SELECT
    TotalPartesActivas = COUNT(*),
    PartesARecargar = SUM(CASE WHEN DisponibleAntes <= 0 THEN 1 ELSE 0 END),
    CantidadMinima = MIN(CantidadObjetivo),
    CantidadMaxima = MAX(CantidadObjetivo),
    TotalPiezasAInsertar = SUM(CantidadObjetivo)
FROM #PartesObjetivo;

SELECT TOP (100)
    ParteID,
    NumeroParte,
    Descripcion,
    DisponibleAntes,
    CantidadAInsertar = CantidadObjetivo,
    DisponibleEstimado = DisponibleAntes + CantidadObjetivo,
    Etiqueta
FROM #PartesObjetivo
ORDER BY ParteID;

IF NOT EXISTS (SELECT 1 FROM #PartesObjetivo)
BEGIN
    PRINT 'No hay partes activas con stock en cero. No se requiere carga.';
    RETURN;
END;

IF @Confirmar = 0
BEGIN
    PRINT 'PREVISUALIZACIÓN: no se insertó stock. Cambia @Confirmar a 1 para ejecutar.';
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
            Almacen,
            Rack,
            Nivel,
            Posicion,
            FechaCreacion,
            CreadoPor,
            Activo
        )
        VALUES
        (
            N'PT',
            N'PT-SIN-UBICAR',
            NULL,
            NULL,
            SYSUTCDATETIME(),
            N'script-prueba-pt-masivo',
            1
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
        @Etiqueta NVARCHAR(120),
        @Referencia NVARCHAR(120),
        @CajaID INT,
        @NumeroCaja INT,
        @CantidadObjetivo INT;

    DECLARE partes CURSOR LOCAL FAST_FORWARD FOR
        SELECT ParteID, Etiqueta, ReferenciaOperacion, CantidadObjetivo
        FROM #PartesObjetivo
        ORDER BY ParteID;

    OPEN partes;
    FETCH NEXT FROM partes INTO @ParteID, @Etiqueta, @Referencia, @CantidadObjetivo;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        SELECT @NumeroCaja = ISNULL(MAX(NumeroCaja), 0) + 1
        FROM dbo.AlmacenPT_Cajas WITH (UPDLOCK, HOLDLOCK)
        WHERE ParteID = @ParteID;

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
            N'OF-PRUEBA-PT-MASIVO',
            @Etiqueta,
            @NumeroCaja,
            @CantidadObjetivo,
            CONCAT(N'LOTE-PRUEBA-', @MarcaEjecucion),
            N'Liberado',
            @UbicacionID,
            SYSDATETIME(),
            SYSUTCDATETIME(),
            N'script-prueba-pt-masivo',
            1
        );

        SET @CajaID = CONVERT(INT, SCOPE_IDENTITY());

        INSERT dbo.AlmacenPT_Movimientos
        (
            CajaID,
            ParteID,
            NumeroOF,
            TipoMovimiento,
            Cantidad,
            UbicacionID,
            EstadoCalidad,
            ResponsableUsuarioID,
            Observaciones,
            ReferenciaOperacion,
            FechaMovimiento,
            FechaCreacion,
            CreadoPor,
            Activo
        )
        VALUES
        (
            @CajaID,
            @ParteID,
            N'OF-PRUEBA-PT-MASIVO',
            N'Entrada',
            @CantidadObjetivo,
            @UbicacionID,
            N'Liberado',
            NULL,
            N'Stock PT masivo temporal para pruebas funcionales',
            @Referencia,
            SYSDATETIME(),
            SYSUTCDATETIME(),
            N'script-prueba-pt-masivo',
            1
        );

        UPDATE #PartesObjetivo
        SET CajaID = @CajaID
        WHERE ParteID = @ParteID;

        FETCH NEXT FROM partes INTO @ParteID, @Etiqueta, @Referencia, @CantidadObjetivo;
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
            INNER JOIN #PartesObjetivo t ON t.ParteID = p.ParteID;',
            N'@Minimo INT, @Aviso INT',
            @Minimo = @StockMinimo,
            @Aviso = @StockAviso;
    END;
    ELSE
    BEGIN
        UPDATE p
        SET
            StockMinimo = CASE WHEN ISNULL(p.StockMinimo, 0) = 0 THEN @StockMinimo ELSE p.StockMinimo END,
            StockAviso = CASE WHEN ISNULL(p.StockAviso, 0) = 0 THEN @StockAviso ELSE p.StockAviso END
        FROM dbo.ERP_Partes p
        INNER JOIN #PartesObjetivo t ON t.ParteID = p.ParteID;
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

EXEC sys.sp_refreshview N'dbo.vw_AlmacenPTInventarioCaja';
EXEC sys.sp_refreshview N'dbo.vw_AlmacenPTInventario';

SELECT
    t.ParteID,
    t.NumeroParte,
    t.Descripcion,
    t.DisponibleAntes,
    t.CajaID,
    t.Etiqueta,
    v.SaldoFisico,
    v.Retenido,
    v.Disponible,
    v.StockMinimo,
    v.StockAviso,
    v.Semaforo
FROM #PartesObjetivo t
LEFT JOIN dbo.vw_AlmacenPTInventario v
    ON v.ParteID = t.ParteID
ORDER BY t.ParteID;

DECLARE @PartesAunEnCero INT;
SELECT @PartesAunEnCero = COUNT(*)
FROM #PartesObjetivo t
LEFT JOIN dbo.vw_AlmacenPTInventario v
    ON v.ParteID = t.ParteID
WHERE ISNULL(v.Disponible, 0) <= 0;

SELECT
    PartesProcesadas = COUNT(*),
    PiezasInsertadas = SUM(t.CantidadObjetivo),
    PartesConStockDespues = SUM(CASE WHEN ISNULL(v.Disponible, 0) > 0 THEN 1 ELSE 0 END),
    PartesAunEnCero = @PartesAunEnCero
FROM #PartesObjetivo t
LEFT JOIN dbo.vw_AlmacenPTInventario v
    ON v.ParteID = t.ParteID;

IF @PartesAunEnCero > 0
    THROW 51220, 'La carga terminó, pero algunas partes continúan en cero. Revisa la salida detallada y las vistas de inventario.', 1;

PRINT 'Stock PT masivo variado de prueba cargado correctamente.';
GO
