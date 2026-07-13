USE [ERP_QUELL];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

/*
    DIVERSIFICAR STOCK PT DE PRUEBA YA CARGADO

    Corrige las cajas creadas por los scripts de prueba anteriores para que
    el inventario no quede con 100 piezas en todos los números de parte.

    Patrón de existencias por parte:
      5, 12 y 20       -> ROJO    (mínimo 20)
      25, 35 y 50      -> AMARILLO (aviso 50)
      60, 75, 90 y 120 -> VERDE

    Solo modifica cajas y movimientos identificados como datos de prueba:
      TEST-PT-MASIVO-*
      TEST-PT-20260714-*

    No modifica producto terminado real ni objetos de Planeación.
*/

DECLARE @Confirmar BIT = 1;
DECLARE @StockMinimo INT = 20;
DECLARE @StockAviso INT = 50;

IF @StockMinimo < 0 OR @StockAviso < @StockMinimo
    THROW 51300, 'Los niveles de stock de prueba no son válidos.', 1;

IF OBJECT_ID(N'dbo.ERP_Partes', N'U') IS NULL
    THROW 51301, 'No existe dbo.ERP_Partes.', 1;

IF OBJECT_ID(N'dbo.AlmacenPT_Cajas', N'U') IS NULL
    THROW 51302, 'No existe dbo.AlmacenPT_Cajas.', 1;

IF OBJECT_ID(N'dbo.AlmacenPT_Movimientos', N'U') IS NULL
    THROW 51303, 'No existe dbo.AlmacenPT_Movimientos.', 1;

IF OBJECT_ID(N'dbo.vw_AlmacenPTInventario', N'V') IS NULL
    THROW 51304, 'No existe dbo.vw_AlmacenPTInventario.', 1;

CREATE TABLE #PartesPrueba
(
    ParteID INT NOT NULL PRIMARY KEY,
    OrdenPrueba INT NOT NULL,
    TotalCajas INT NOT NULL,
    CantidadObjetivo INT NOT NULL
);

;WITH Partes AS
(
    SELECT
        c.ParteID,
        TotalCajas = COUNT(*)
    FROM dbo.AlmacenPT_Cajas c
    WHERE c.Activo = 1
      AND
      (
          c.Etiqueta LIKE N'TEST-PT-MASIVO-%'
          OR c.Etiqueta LIKE N'TEST-PT-20260714-%'
      )
    GROUP BY c.ParteID
),
Ordenadas AS
(
    SELECT
        ParteID,
        TotalCajas,
        OrdenPrueba = ROW_NUMBER() OVER (ORDER BY ParteID)
    FROM Partes
)
INSERT #PartesPrueba(ParteID, OrdenPrueba, TotalCajas, CantidadObjetivo)
SELECT
    ParteID,
    OrdenPrueba,
    TotalCajas,
    CASE (OrdenPrueba - 1) % 10
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
FROM Ordenadas;

IF NOT EXISTS (SELECT 1 FROM #PartesPrueba)
    THROW 51305, 'No se encontraron cajas de prueba TEST-PT para diversificar.', 1;

IF EXISTS
(
    SELECT 1
    FROM #PartesPrueba
    WHERE TotalCajas > CantidadObjetivo
)
    THROW 51306, 'Una parte tiene más cajas de prueba que la cantidad objetivo mínima. Revisa las cajas TEST-PT duplicadas.', 1;

CREATE TABLE #CajasPrueba
(
    CajaID INT NOT NULL PRIMARY KEY,
    ParteID INT NOT NULL,
    OrdenCaja INT NOT NULL,
    TotalCajas INT NOT NULL,
    CantidadObjetivoParte INT NOT NULL,
    CantidadObjetivoCaja INT NOT NULL,
    MovimientoEntradaID BIGINT NULL
);

;WITH Cajas AS
(
    SELECT
        c.CajaID,
        c.ParteID,
        OrdenCaja = ROW_NUMBER() OVER (PARTITION BY c.ParteID ORDER BY c.CajaID),
        TotalCajas = COUNT(*) OVER (PARTITION BY c.ParteID)
    FROM dbo.AlmacenPT_Cajas c
    WHERE c.Activo = 1
      AND
      (
          c.Etiqueta LIKE N'TEST-PT-MASIVO-%'
          OR c.Etiqueta LIKE N'TEST-PT-20260714-%'
      )
)
INSERT #CajasPrueba
(
    CajaID,
    ParteID,
    OrdenCaja,
    TotalCajas,
    CantidadObjetivoParte,
    CantidadObjetivoCaja
)
SELECT
    c.CajaID,
    c.ParteID,
    c.OrdenCaja,
    c.TotalCajas,
    p.CantidadObjetivo,
    CASE
        WHEN c.OrdenCaja = 1 THEN p.CantidadObjetivo - (c.TotalCajas - 1)
        ELSE 1
    END
FROM Cajas c
INNER JOIN #PartesPrueba p
    ON p.ParteID = c.ParteID;

UPDATE cp
SET MovimientoEntradaID = m.MovimientoID
FROM #CajasPrueba cp
OUTER APPLY
(
    SELECT TOP (1) m.MovimientoID
    FROM dbo.AlmacenPT_Movimientos m
    WHERE m.CajaID = cp.CajaID
      AND m.Activo = 1
      AND m.TipoMovimiento = N'Entrada'
      AND m.CreadoPor IN (N'script-prueba-pt', N'script-prueba-pt-masivo')
    ORDER BY m.MovimientoID
) m;

IF EXISTS (SELECT 1 FROM #CajasPrueba WHERE MovimientoEntradaID IS NULL)
    THROW 51307, 'Existe una caja TEST-PT sin su movimiento de entrada de prueba.', 1;

IF EXISTS
(
    SELECT 1
    FROM #CajasPrueba cp
    INNER JOIN dbo.AlmacenPT_Movimientos m
        ON m.CajaID = cp.CajaID
       AND m.Activo = 1
    WHERE m.MovimientoID <> cp.MovimientoEntradaID
)
    THROW 51308, 'Una caja TEST-PT ya tiene movimientos adicionales. No se modificó para evitar alterar pruebas realizadas.', 1;

SELECT
    p.ParteID,
    ep.NumeroParte,
    ep.Descripcion,
    DisponibleAntes = ISNULL(v.Disponible, 0),
    p.CantidadObjetivo,
    SemaforoObjetivo = CASE
        WHEN p.CantidadObjetivo <= @StockMinimo THEN N'ROJO'
        WHEN p.CantidadObjetivo <= @StockAviso THEN N'AMARILLO'
        ELSE N'VERDE'
    END,
    p.TotalCajas
FROM #PartesPrueba p
INNER JOIN dbo.ERP_Partes ep
    ON ep.ParteID = p.ParteID
LEFT JOIN dbo.vw_AlmacenPTInventario v
    ON v.ParteID = p.ParteID
ORDER BY p.OrdenPrueba;

IF @Confirmar = 0
BEGIN
    PRINT 'PREVISUALIZACIÓN: no se modificó el stock. Cambia @Confirmar a 1 para ejecutar.';
    RETURN;
END;

BEGIN TRY
    BEGIN TRAN;

    UPDATE c
    SET
        c.CantidadInicial = cp.CantidadObjetivoCaja,
        c.FechaModificacion = SYSUTCDATETIME(),
        c.ActualizadoPor = N'script-prueba-pt-variado'
    FROM dbo.AlmacenPT_Cajas c
    INNER JOIN #CajasPrueba cp
        ON cp.CajaID = c.CajaID;

    UPDATE m
    SET
        m.Cantidad = cp.CantidadObjetivoCaja,
        m.Observaciones = N'Stock PT temporal variado para validar semáforo',
        m.FechaModificacion = SYSUTCDATETIME(),
        m.ActualizadoPor = N'script-prueba-pt-variado'
    FROM dbo.AlmacenPT_Movimientos m
    INNER JOIN #CajasPrueba cp
        ON cp.MovimientoEntradaID = m.MovimientoID;

    IF COL_LENGTH(N'dbo.ERP_Partes', N'StockConfigurado') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql
            N'
            UPDATE p
            SET
                StockMinimo = @Minimo,
                StockAviso = @Aviso,
                StockConfigurado = 1,
                FechaModificacion = GETDATE()
            FROM dbo.ERP_Partes p
            INNER JOIN #PartesPrueba t ON t.ParteID = p.ParteID;',
            N'@Minimo INT, @Aviso INT',
            @Minimo = @StockMinimo,
            @Aviso = @StockAviso;
    END;
    ELSE
    BEGIN
        UPDATE p
        SET
            StockMinimo = @StockMinimo,
            StockAviso = @StockAviso,
            FechaModificacion = GETDATE()
        FROM dbo.ERP_Partes p
        INNER JOIN #PartesPrueba t
            ON t.ParteID = p.ParteID;
    END;

    COMMIT;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK;
    THROW;
END CATCH;

EXEC sys.sp_refreshview N'dbo.vw_AlmacenPTInventarioCaja';
EXEC sys.sp_refreshview N'dbo.vw_AlmacenPTInventario';

SELECT
    p.ParteID,
    ep.NumeroParte,
    Disponible = ISNULL(v.Disponible, 0),
    v.StockMinimo,
    v.StockAviso,
    v.Semaforo
FROM #PartesPrueba p
INNER JOIN dbo.ERP_Partes ep
    ON ep.ParteID = p.ParteID
LEFT JOIN dbo.vw_AlmacenPTInventario v
    ON v.ParteID = p.ParteID
ORDER BY p.OrdenPrueba;

SELECT
    TotalPartes = COUNT(*),
    Rojos = SUM(CASE WHEN v.Semaforo = N'ROJO' THEN 1 ELSE 0 END),
    Amarillos = SUM(CASE WHEN v.Semaforo = N'AMARILLO' THEN 1 ELSE 0 END),
    Verdes = SUM(CASE WHEN v.Semaforo = N'VERDE' THEN 1 ELSE 0 END),
    MinimoDisponible = MIN(v.Disponible),
    MaximoDisponible = MAX(v.Disponible)
FROM #PartesPrueba p
INNER JOIN dbo.vw_AlmacenPTInventario v
    ON v.ParteID = p.ParteID;

PRINT 'Stock PT de prueba diversificado correctamente.';
GO
