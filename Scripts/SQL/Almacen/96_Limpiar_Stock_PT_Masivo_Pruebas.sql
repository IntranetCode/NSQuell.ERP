USE [ERP_QUELL];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

/*
    Elimina únicamente las cajas y movimientos generados por
    08_Rellenar_Stock_PT_Pruebas.sql.

    No modifica cajas reales ni movimientos ajenos al prefijo TEST-PT-MASIVO-.
*/

DECLARE @Confirmar BIT = 0;

IF OBJECT_ID(N'dbo.AlmacenPT_Cajas', N'U') IS NULL
    THROW 51300, 'No existe dbo.AlmacenPT_Cajas.', 1;

IF OBJECT_ID(N'dbo.AlmacenPT_Movimientos', N'U') IS NULL
    THROW 51301, 'No existe dbo.AlmacenPT_Movimientos.', 1;

SELECT
    CajasPrueba = COUNT(DISTINCT c.CajaID),
    MovimientosPrueba = COUNT(m.MovimientoID),
    PiezasEntrada = ISNULL(SUM(CASE WHEN m.TipoMovimiento = N'Entrada' THEN m.Cantidad ELSE 0 END), 0)
FROM dbo.AlmacenPT_Cajas c
LEFT JOIN dbo.AlmacenPT_Movimientos m
    ON m.CajaID = c.CajaID
WHERE c.Etiqueta LIKE N'TEST-PT-MASIVO-%';

SELECT TOP (200)
    c.CajaID,
    c.ParteID,
    c.Etiqueta,
    c.CantidadInicial,
    c.FechaEntrada
FROM dbo.AlmacenPT_Cajas c
WHERE c.Etiqueta LIKE N'TEST-PT-MASIVO-%'
ORDER BY c.CajaID DESC;

IF @Confirmar = 0
BEGIN
    PRINT 'PREVISUALIZACIÓN: no se eliminó información. Cambia @Confirmar a 1 para limpiar el stock de prueba masivo.';
    RETURN;
END;

BEGIN TRY
    BEGIN TRAN;

    DELETE m
    FROM dbo.AlmacenPT_Movimientos m
    INNER JOIN dbo.AlmacenPT_Cajas c
        ON c.CajaID = m.CajaID
    WHERE c.Etiqueta LIKE N'TEST-PT-MASIVO-%';

    DELETE FROM dbo.AlmacenPT_Cajas
    WHERE Etiqueta LIKE N'TEST-PT-MASIVO-%';

    COMMIT;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK;
    THROW;
END CATCH;

PRINT 'Stock PT masivo de prueba eliminado correctamente.';
GO
