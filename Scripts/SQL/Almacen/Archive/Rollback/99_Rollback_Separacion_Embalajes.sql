USE [ERP_QUELL];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

/* Rollback controlado. No ejecutar salvo que sea necesario revertir la separación. */
DECLARE @Confirmar BIT = 0;
IF @Confirmar=0
BEGIN
    SELECT N'PREVISUALIZACION' AS Estado,
           (SELECT COUNT(*) FROM dbo.ERP_Embalajes) AS Embalajes,
           (SELECT COUNT(*) FROM dbo.AlmacenEmbalajes_Movimientos) AS Movimientos;
    RETURN;
END;

BEGIN TRY
    BEGIN TRAN;
    IF COL_LENGTH(N'dbo.ERP_Materiales',N'TipoMaterial') IS NULL
        EXEC(N'ALTER TABLE dbo.ERP_Materiales ADD TipoMaterial NVARCHAR(80) NULL;');

    EXEC sys.sp_executesql N'
    INSERT dbo.ERP_Materiales
    (Codigo,Nombre,TipoMaterial,UnidadDefault,Proveedor,RequiereLote,StockMinimo,StockAviso,
     StockConfigurado,FechaCreacion,CreadoPor,FechaModificacion,ActualizadoPor,Activo)
    SELECT e.Codigo,e.Nombre,N''EMBALAJE'',e.UnidadDefault,e.Proveedor,e.RequiereLote,e.StockMinimo,e.StockAviso,
           e.StockConfigurado,e.FechaCreacion,e.CreadoPor,e.FechaModificacion,e.ActualizadoPor,e.Activo
    FROM dbo.ERP_Embalajes e
    WHERE NOT EXISTS(SELECT 1 FROM dbo.ERP_Materiales m WHERE UPPER(m.Codigo)=UPPER(e.Codigo));';

    INSERT dbo.AlmacenMP_Movimientos
    (FechaMovimiento,MaterialID,OrdenFabricacionLegacyID,NumeroOF,TipoMovimiento,Lote,Cantidad,Unidad,
     ResponsableUsuarioID,ResponsableLegacyID,TurnoLegacyID,UbicacionID,EstatusCalidad,Seguimiento,EvidenciaUrl,
     FechaCreacion,CreadoPor,FechaModificacion,ActualizadoPor,Activo,EntregadoPorNombre,
     RequiereValidacionProduccion,ValidadoProduccion,ValidadoProduccionEn,
     ValidadoProduccionEmpleadoLegacyID,ValidadoProduccionNombre,ComentarioValidacionProduccion,ReferenciaOperacion)
    SELECT em.FechaMovimiento,m.MaterialID,em.OrdenFabricacionLegacyID,em.NumeroOF,em.TipoMovimiento,em.Lote,em.Cantidad,em.Unidad,
           em.ResponsableUsuarioID,em.ResponsableLegacyID,em.TurnoLegacyID,em.UbicacionID,em.EstatusCalidad,em.Seguimiento,em.EvidenciaUrl,
           em.FechaCreacion,em.CreadoPor,em.FechaModificacion,em.ActualizadoPor,em.Activo,em.EntregadoPorNombre,
           em.RequiereValidacionProduccion,em.ValidadoProduccion,em.ValidadoProduccionEn,
           em.ValidadoProduccionEmpleadoLegacyID,em.ValidadoProduccionNombre,em.ComentarioValidacionProduccion,em.ReferenciaOperacion
    FROM dbo.AlmacenEmbalajes_Movimientos em
    INNER JOIN dbo.ERP_Embalajes e ON e.EmbalajeID=em.EmbalajeID
    INNER JOIN dbo.ERP_Materiales m ON UPPER(m.Codigo)=UPPER(e.Codigo)
    WHERE NOT EXISTS
    (SELECT 1 FROM dbo.AlmacenMP_Movimientos mm WHERE mm.ReferenciaOperacion=em.ReferenciaOperacion AND em.ReferenciaOperacion IS NOT NULL);

    DELETE FROM dbo.AlmacenEmbalajes_Movimientos;
    DELETE FROM dbo.ERP_Embalajes;
    COMMIT;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT>0 ROLLBACK;
    THROW;
END CATCH;
GO
