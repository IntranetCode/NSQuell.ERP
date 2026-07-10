USE [ERP_QUELL];
GO
/* Rollback exclusivo de los objetos creados por el módulo de Almacén.
   Cambia @Confirmar a 1 únicamente después de respaldar ERP_QUELL. */
DECLARE @Confirmar BIT = 0;
IF @Confirmar <> 1
    THROW 51000, 'Rollback cancelado. Cambia @Confirmar a 1 para continuar.', 1;
GO
SET XACT_ABORT ON;
BEGIN TRY
 BEGIN TRAN;
 DROP VIEW IF EXISTS dbo.vw_AlmacenPTInventario;
 DROP VIEW IF EXISTS dbo.vw_AlmacenPTInventarioCaja;
 DROP VIEW IF EXISTS dbo.vw_AlmacenMPInventario;
 DROP TABLE IF EXISTS dbo.AlmacenPT_Movimientos;
 DROP TABLE IF EXISTS dbo.AlmacenPT_Cajas;
 DROP TABLE IF EXISTS dbo.AlmacenMP_Movimientos;
 DROP TABLE IF EXISTS dbo.ERP_Materiales;
 DROP TABLE IF EXISTS dbo.ERP_Ubicaciones;
 IF COL_LENGTH('dbo.ERP_Partes','StockAviso') IS NOT NULL
 BEGIN
   DECLARE @dfAviso sysname=(SELECT dc.name FROM sys.default_constraints dc JOIN sys.columns c ON c.default_object_id=dc.object_id WHERE dc.parent_object_id=OBJECT_ID('dbo.ERP_Partes') AND c.name='StockAviso');
   IF @dfAviso IS NOT NULL EXEC('ALTER TABLE dbo.ERP_Partes DROP CONSTRAINT '+QUOTENAME(@dfAviso));
   ALTER TABLE dbo.ERP_Partes DROP COLUMN StockAviso;
 END;
 IF COL_LENGTH('dbo.ERP_Partes','StockMinimo') IS NOT NULL
 BEGIN
   DECLARE @dfMin sysname=(SELECT dc.name FROM sys.default_constraints dc JOIN sys.columns c ON c.default_object_id=dc.object_id WHERE dc.parent_object_id=OBJECT_ID('dbo.ERP_Partes') AND c.name='StockMinimo');
   IF @dfMin IS NOT NULL EXEC('ALTER TABLE dbo.ERP_Partes DROP CONSTRAINT '+QUOTENAME(@dfMin));
   ALTER TABLE dbo.ERP_Partes DROP COLUMN StockMinimo;
 END;
 COMMIT;
END TRY
BEGIN CATCH
 IF @@TRANCOUNT>0 ROLLBACK;
 THROW;
END CATCH;
GO
