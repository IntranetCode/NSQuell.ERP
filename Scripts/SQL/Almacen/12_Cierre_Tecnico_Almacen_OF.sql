USE [ERP_QUELL];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO
IF DB_NAME() <> N'ERP_QUELL'
    THROW 51900, 'Ejecuta este script dentro de ERP_QUELL.', 1;
GO
BEGIN TRY
    BEGIN TRAN;

    IF EXISTS (
        SELECT ReferenciaOperacion
        FROM dbo.AlmacenMP_Movimientos
        WHERE NULLIF(LTRIM(RTRIM(ReferenciaOperacion)), N'') IS NOT NULL
        GROUP BY ReferenciaOperacion
        HAVING COUNT_BIG(1) > 1)
        THROW 51901, 'Existen referencias duplicadas en AlmacenMP_Movimientos.', 1;

    IF EXISTS (
        SELECT ReferenciaOperacion
        FROM dbo.AlmacenEmbalajes_Movimientos
        WHERE NULLIF(LTRIM(RTRIM(ReferenciaOperacion)), N'') IS NOT NULL
        GROUP BY ReferenciaOperacion
        HAVING COUNT_BIG(1) > 1)
        THROW 51902, 'Existen referencias duplicadas en AlmacenEmbalajes_Movimientos.', 1;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.AlmacenMP_Movimientos') AND name=N'UX_AlmacenMP_ReferenciaOperacion')
        CREATE UNIQUE INDEX UX_AlmacenMP_ReferenciaOperacion
            ON dbo.AlmacenMP_Movimientos(ReferenciaOperacion)
            WHERE ReferenciaOperacion IS NOT NULL AND ReferenciaOperacion <> N'';

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.AlmacenEmbalajes_Movimientos') AND name=N'UX_AlmacenEmbalajes_ReferenciaOperacion')
        CREATE UNIQUE INDEX UX_AlmacenEmbalajes_ReferenciaOperacion
            ON dbo.AlmacenEmbalajes_Movimientos(ReferenciaOperacion)
            WHERE ReferenciaOperacion IS NOT NULL AND ReferenciaOperacion <> N'';

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.AlmacenMP_Movimientos') AND name=N'IX_AlmacenMP_OF_Material_Activo_Fecha')
        CREATE INDEX IX_AlmacenMP_OF_Material_Activo_Fecha
            ON dbo.AlmacenMP_Movimientos(NumeroOF, MaterialID, Activo, FechaMovimiento)
            INCLUDE(TipoMovimiento, Cantidad, Unidad, ReferenciaOperacion);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.AlmacenEmbalajes_Movimientos') AND name=N'IX_AlmacenEmbalajes_OF_Catalogo_Activo_Fecha')
        CREATE INDEX IX_AlmacenEmbalajes_OF_Catalogo_Activo_Fecha
            ON dbo.AlmacenEmbalajes_Movimientos(NumeroOF, EmbalajeID, Activo, FechaMovimiento)
            INCLUDE(TipoMovimiento, Cantidad, Unidad, ReferenciaOperacion);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.SolicitudesProduccionDetalle') AND name=N'IX_SolicitudesProduccionDetalle_OF_MP')
        CREATE INDEX IX_SolicitudesProduccionDetalle_OF_MP
            ON dbo.SolicitudesProduccionDetalle(SolicitudProduccionID, Activo, MaterialCodigo)
            INCLUDE(CantidadMpKg, MaterialDescripcion, Renglon);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.SolicitudesProduccionDetalle') AND name=N'IX_SolicitudesProduccionDetalle_OF_Embalaje')
        CREATE INDEX IX_SolicitudesProduccionDetalle_OF_Embalaje
            ON dbo.SolicitudesProduccionDetalle(SolicitudProduccionID, Activo, EmbalajeCodigo)
            INCLUDE(CantidadEmbalajes, EmbalajeDescripcion, Renglon);

    UPDATE m
    SET Descripcion=N'Órdenes de fabricación: consulta y entrega controlada de MP y embalajes.'
    FROM dbo.Menus m
    WHERE EXISTS (SELECT 1 FROM dbo.SubMenus sm WHERE sm.MenuID=m.MenuID AND sm.UrlEnlace=N'/AlmacenOF/Index');

    COMMIT;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    THROW;
END CATCH;
GO

