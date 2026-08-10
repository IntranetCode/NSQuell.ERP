USE [ERP_QUELL];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

/*
    Rollback exclusivo de 04_Actualizar_Stock_e_Integracion_Almacen.sql.
    No elimina catálogos, cajas, movimientos ni niveles de stock.
    Sí elimina ReferenciaOperacion y, por tanto, sus valores registrados.
*/
DECLARE @Confirmar BIT = 0;
IF @Confirmar <> 1
    THROW 51000, 'Rollback cancelado. Revisa el archivo y cambia @Confirmar a 1.', 1;
GO

BEGIN TRY
    BEGIN TRAN;

    IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.AlmacenMP_Movimientos') AND name=N'UX_AlmacenMP_Movimientos_ReferenciaOperacion')
        DROP INDEX UX_AlmacenMP_Movimientos_ReferenciaOperacion ON dbo.AlmacenMP_Movimientos;

    IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.AlmacenPT_Movimientos') AND name=N'UX_AlmacenPT_Movimientos_ReferenciaOperacion')
        DROP INDEX UX_AlmacenPT_Movimientos_ReferenciaOperacion ON dbo.AlmacenPT_Movimientos;

    IF COL_LENGTH(N'dbo.AlmacenMP_Movimientos',N'ReferenciaOperacion') IS NOT NULL
        ALTER TABLE dbo.AlmacenMP_Movimientos DROP COLUMN ReferenciaOperacion;

    IF COL_LENGTH(N'dbo.AlmacenPT_Movimientos',N'ReferenciaOperacion') IS NOT NULL
        ALTER TABLE dbo.AlmacenPT_Movimientos DROP COLUMN ReferenciaOperacion;

    COMMIT;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT>0 ROLLBACK;
    THROW;
END CATCH;
GO

/* Las columnas StockConfigurado y los niveles permanecen; son aditivos y no alteran movimientos. */
SELECT N'Rollback incremental aplicado. No se eliminaron inventarios ni niveles de stock.' AS Resultado;
GO
