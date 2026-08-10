USE [ERP_QUELL];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

/*
    Elimina únicamente las cajas y movimientos creados por
    07_Cargar_Stock_PT_Pruebas.sql.

    No modifica otros movimientos ni la configuración de niveles de stock.
*/

DECLARE @Confirmar BIT = 0;

IF OBJECT_ID(N'dbo.AlmacenPT_Cajas', N'U') IS NULL
    THROW 51000, 'No existe dbo.AlmacenPT_Cajas.', 1;

IF OBJECT_ID(N'dbo.AlmacenPT_Movimientos', N'U') IS NULL
    THROW 51001, 'No existe dbo.AlmacenPT_Movimientos.', 1;

SELECT
    MovimientosPrueba = COUNT_BIG(*)
FROM dbo.AlmacenPT_Movimientos m
INNER JOIN dbo.AlmacenPT_Cajas c ON c.CajaID = m.CajaID
WHERE c.Etiqueta LIKE N'TEST-PT-20260714-%'
  AND c.CreadoPor = N'script-prueba-pt';

SELECT
    CajasPrueba = COUNT_BIG(*)
FROM dbo.AlmacenPT_Cajas
WHERE Etiqueta LIKE N'TEST-PT-20260714-%'
  AND CreadoPor = N'script-prueba-pt';

IF @Confirmar = 0
BEGIN
    PRINT 'PREVISUALIZACIÓN: no se eliminó información. Cambia @Confirmar a 1 para limpiar el stock de prueba.';
    RETURN;
END;

BEGIN TRY
    BEGIN TRAN;

    DELETE m
    FROM dbo.AlmacenPT_Movimientos m
    INNER JOIN dbo.AlmacenPT_Cajas c ON c.CajaID = m.CajaID
    WHERE c.Etiqueta LIKE N'TEST-PT-20260714-%'
      AND c.CreadoPor = N'script-prueba-pt';

    DELETE c
    FROM dbo.AlmacenPT_Cajas c
    WHERE c.Etiqueta LIKE N'TEST-PT-20260714-%'
      AND c.CreadoPor = N'script-prueba-pt'
      AND NOT EXISTS
      (
          SELECT 1
          FROM dbo.AlmacenPT_Movimientos m
          WHERE m.CajaID = c.CajaID
      );

    COMMIT;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK;
    THROW;
END CATCH;

PRINT 'Stock PT de prueba eliminado correctamente.';
GO
