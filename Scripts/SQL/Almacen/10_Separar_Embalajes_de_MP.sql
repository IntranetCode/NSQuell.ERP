USE [ERP_QUELL];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

/*
    SEPARACIÓN CONTROLADA DE EMBALAJES DESDE ALMACÉN MP
    ---------------------------------------------------
    - Crea un catálogo independiente de embalajes.
    - Crea movimientos independientes de embalajes.
    - Migra los materiales cuyo TipoMaterial sea EMBALAJE o EMBALAJES.
    - Conserva movimientos, lotes, cantidades, ubicaciones, OF y auditoría.
    - Elimina TipoMaterial de ERP_Materiales después de validar la migración.

    Ejecutar primero con @Confirmar = 0. Revisar la previsualización.
    Después cambiar a @Confirmar = 1 y ejecutar nuevamente.
*/

IF DB_NAME() <> N'ERP_QUELL'
    THROW 51200, 'Ejecuta el script dentro de ERP_QUELL.', 1;
IF OBJECT_ID(N'dbo.ERP_Materiales', N'U') IS NULL
    THROW 51201, 'No existe dbo.ERP_Materiales.', 1;
IF OBJECT_ID(N'dbo.AlmacenMP_Movimientos', N'U') IS NULL
    THROW 51202, 'No existe dbo.AlmacenMP_Movimientos.', 1;
IF OBJECT_ID(N'dbo.ERP_Ubicaciones', N'U') IS NULL
    THROW 51203, 'No existe dbo.ERP_Ubicaciones.', 1;
IF OBJECT_ID(N'dbo.Usuarios', N'U') IS NULL
    THROW 51204, 'No existe dbo.Usuarios.', 1;
GO

IF OBJECT_ID(N'dbo.ERP_Embalajes', N'U') IS NULL
BEGIN
    EXEC(N'
    CREATE TABLE dbo.ERP_Embalajes
    (
        EmbalajeID INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ERP_Embalajes PRIMARY KEY,
        Codigo NVARCHAR(80) NOT NULL,
        Nombre NVARCHAR(250) NOT NULL,
        UnidadDefault NVARCHAR(20) NOT NULL,
        Proveedor NVARCHAR(200) NULL,
        RequiereLote BIT NOT NULL CONSTRAINT DF_ERP_Embalajes_RequiereLote DEFAULT 0,
        StockMinimo DECIMAL(18,3) NOT NULL CONSTRAINT DF_ERP_Embalajes_StockMinimo DEFAULT 0,
        StockAviso DECIMAL(18,3) NOT NULL CONSTRAINT DF_ERP_Embalajes_StockAviso DEFAULT 0,
        StockConfigurado BIT NOT NULL CONSTRAINT DF_ERP_Embalajes_StockConfigurado DEFAULT 0,
        FechaCreacion DATETIME2(7) NOT NULL CONSTRAINT DF_ERP_Embalajes_FechaCreacion DEFAULT SYSUTCDATETIME(),
        CreadoPor NVARCHAR(120) NULL,
        FechaModificacion DATETIME2(7) NULL,
        ActualizadoPor NVARCHAR(120) NULL,
        Activo BIT NOT NULL CONSTRAINT DF_ERP_Embalajes_Activo DEFAULT 1,
        CONSTRAINT CK_ERP_Embalajes_Niveles CHECK(StockMinimo>=0 AND StockAviso>=StockMinimo)
    );');
END;
GO

IF OBJECT_ID(N'dbo.AlmacenEmbalajes_Movimientos', N'U') IS NULL
BEGIN
    EXEC(N'
    CREATE TABLE dbo.AlmacenEmbalajes_Movimientos
    (
        MovimientoID BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AlmacenEmbalajes_Movimientos PRIMARY KEY,
        EmbalajeID INT NOT NULL,
        MovimientoOrigenMPID BIGINT NULL,
        FechaMovimiento DATETIME2(7) NOT NULL CONSTRAINT DF_AlmacenEmbalajes_Movimientos_Fecha DEFAULT SYSDATETIME(),
        OrdenFabricacionLegacyID INT NULL,
        NumeroOF NVARCHAR(80) NULL,
        TipoMovimiento NVARCHAR(30) NOT NULL,
        Lote NVARCHAR(120) NOT NULL,
        Cantidad DECIMAL(18,3) NOT NULL,
        Unidad NVARCHAR(20) NOT NULL,
        ResponsableUsuarioID INT NULL,
        ResponsableLegacyID INT NULL,
        TurnoLegacyID INT NULL,
        UbicacionID INT NULL,
        EstatusCalidad NVARCHAR(30) NULL,
        Seguimiento NVARCHAR(800) NULL,
        EvidenciaUrl NVARCHAR(500) NULL,
        FechaCreacion DATETIME2(7) NOT NULL CONSTRAINT DF_AlmacenEmbalajes_Movimientos_Creacion DEFAULT SYSUTCDATETIME(),
        CreadoPor NVARCHAR(120) NULL,
        FechaModificacion DATETIME2(7) NULL,
        ActualizadoPor NVARCHAR(120) NULL,
        Activo BIT NOT NULL CONSTRAINT DF_AlmacenEmbalajes_Movimientos_Activo DEFAULT 1,
        EntregadoPorNombre NVARCHAR(180) NULL,
        RequiereValidacionProduccion BIT NOT NULL CONSTRAINT DF_AlmacenEmbalajes_Movimientos_RequiereValidacion DEFAULT 0,
        ValidadoProduccion BIT NOT NULL CONSTRAINT DF_AlmacenEmbalajes_Movimientos_Validado DEFAULT 1,
        ValidadoProduccionEn DATETIME2(7) NULL,
        ValidadoProduccionEmpleadoLegacyID INT NULL,
        ValidadoProduccionNombre NVARCHAR(180) NULL,
        ComentarioValidacionProduccion NVARCHAR(500) NULL,
        ReferenciaOperacion NVARCHAR(120) NULL,
        CONSTRAINT FK_AlmacenEmbalajes_Movimientos_Embalaje FOREIGN KEY(EmbalajeID) REFERENCES dbo.ERP_Embalajes(EmbalajeID),
        CONSTRAINT FK_AlmacenEmbalajes_Movimientos_Ubicacion FOREIGN KEY(UbicacionID) REFERENCES dbo.ERP_Ubicaciones(UbicacionID),
        CONSTRAINT FK_AlmacenEmbalajes_Movimientos_Usuario FOREIGN KEY(ResponsableUsuarioID) REFERENCES dbo.Usuarios(UsuarioID),
        CONSTRAINT CK_AlmacenEmbalajes_Movimientos_Cantidad CHECK(Cantidad>0),
        CONSTRAINT CK_AlmacenEmbalajes_Movimientos_Tipo CHECK
        (TipoMovimiento IN(N''Entrada'',N''Salida'',N''Retorno'',N''Consumo'',N''Scrap'',N''Ajuste'',N''AjustePositivo'',N''AjusteNegativo''))
    );');
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.ERP_Embalajes') AND name=N'UX_ERP_Embalajes_Codigo')
    EXEC(N'CREATE UNIQUE INDEX UX_ERP_Embalajes_Codigo ON dbo.ERP_Embalajes(Codigo);');
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.AlmacenEmbalajes_Movimientos') AND name=N'UX_AlmacenEmbalajes_Movimientos_OrigenMP')
    EXEC(N'CREATE UNIQUE INDEX UX_AlmacenEmbalajes_Movimientos_OrigenMP ON dbo.AlmacenEmbalajes_Movimientos(MovimientoOrigenMPID) WHERE MovimientoOrigenMPID IS NOT NULL;');
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.AlmacenEmbalajes_Movimientos') AND name=N'UX_AlmacenEmbalajes_Movimientos_Referencia')
    EXEC(N'CREATE UNIQUE INDEX UX_AlmacenEmbalajes_Movimientos_Referencia ON dbo.AlmacenEmbalajes_Movimientos(ReferenciaOperacion) WHERE ReferenciaOperacion IS NOT NULL;');
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.AlmacenEmbalajes_Movimientos') AND name=N'IX_AlmacenEmbalajes_Movimientos_EmbalajeFecha')
    EXEC(N'CREATE INDEX IX_AlmacenEmbalajes_Movimientos_EmbalajeFecha ON dbo.AlmacenEmbalajes_Movimientos(EmbalajeID,FechaMovimiento DESC);');
GO

IF NOT EXISTS
(
    SELECT 1 FROM dbo.ERP_Ubicaciones
    WHERE Almacen=N'EMBALAJES' AND Rack=N'EMBALAJES-SIN-UBICAR'
      AND ISNULL(Nivel,N'')=N'' AND ISNULL(Posicion,N'')=N''
)
BEGIN
    INSERT dbo.ERP_Ubicaciones
    (Almacen,Rack,Nivel,Posicion,FechaCreacion,CreadoPor,Activo)
    VALUES(N'EMBALAJES',N'EMBALAJES-SIN-UBICAR',NULL,NULL,SYSUTCDATETIME(),N'script-separacion-embalajes',1);
END;
GO

CREATE OR ALTER VIEW dbo.vw_AlmacenEmbalajesInventario
AS
WITH Mov AS
(
    SELECT EmbalajeID,
        SUM(CASE WHEN TipoMovimiento IN(N'Entrada',N'Retorno',N'Ajuste',N'AjustePositivo') THEN Cantidad ELSE 0 END) AS Entradas,
        SUM(CASE WHEN TipoMovimiento IN(N'Salida',N'Consumo',N'Scrap',N'AjusteNegativo') THEN Cantidad ELSE 0 END) AS Salidas,
        MAX(FechaMovimiento) AS UltimoMovimiento
    FROM dbo.AlmacenEmbalajes_Movimientos
    WHERE Activo=1
    GROUP BY EmbalajeID
), Base AS
(
    SELECT e.EmbalajeID,e.Codigo,e.Nombre,e.UnidadDefault AS Unidad,
        ISNULL(v.Entradas,0) AS Entradas,ISNULL(v.Salidas,0) AS Salidas,
        ISNULL(v.Entradas,0)-ISNULL(v.Salidas,0) AS Saldo,
        e.StockMinimo,e.StockAviso,e.StockConfigurado,v.UltimoMovimiento
    FROM dbo.ERP_Embalajes e
    LEFT JOIN Mov v ON v.EmbalajeID=e.EmbalajeID
    WHERE e.Activo=1
)
SELECT EmbalajeID,Codigo,Nombre,Unidad,Entradas,Salidas,Saldo,StockMinimo,StockAviso,StockConfigurado,
    CASE WHEN StockConfigurado=0 THEN N'SIN_CONFIGURAR' WHEN Saldo<=StockMinimo THEN N'ROJO'
         WHEN Saldo<=StockAviso THEN N'AMARILLO' ELSE N'VERDE' END AS Semaforo,
    UltimoMovimiento
FROM Base;
GO

DECLARE @Confirmar BIT = 0; -- CAMBIAR A 1 DESPUÉS DE REVISAR
DECLARE @CantidadEmbalajes INT = 0;
DECLARE @CantidadMovimientos BIGINT = 0;

IF COL_LENGTH(N'dbo.ERP_Materiales', N'TipoMaterial') IS NOT NULL
BEGIN
    EXEC sys.sp_executesql
        N'SELECT @C=COUNT(*) FROM dbo.ERP_Materiales WHERE UPPER(LTRIM(RTRIM(ISNULL(TipoMaterial,N'''')))) IN(N''EMBALAJE'',N''EMBALAJES'');',
        N'@C INT OUTPUT', @C=@CantidadEmbalajes OUTPUT;
    EXEC sys.sp_executesql
        N'SELECT @C=COUNT_BIG(*) FROM dbo.AlmacenMP_Movimientos mm INNER JOIN dbo.ERP_Materiales m ON m.MaterialID=mm.MaterialID WHERE UPPER(LTRIM(RTRIM(ISNULL(m.TipoMaterial,N'''')))) IN(N''EMBALAJE'',N''EMBALAJES'');',
        N'@C BIGINT OUTPUT', @C=@CantidadMovimientos OUTPUT;
END;

SELECT
    @CantidadEmbalajes AS EmbalajesDetectados,
    @CantidadMovimientos AS MovimientosPorMigrar,
    CASE WHEN COL_LENGTH(N'dbo.ERP_Materiales',N'TipoMaterial') IS NULL THEN N'YA_SEPARADO'
         WHEN @Confirmar=0 THEN N'PREVISUALIZACION'
         ELSE N'CONFIRMADO' END AS Estado;

IF COL_LENGTH(N'dbo.ERP_Materiales', N'TipoMaterial') IS NOT NULL
BEGIN
    EXEC sys.sp_executesql N'
    SELECT m.MaterialID,m.Codigo,m.Nombre,m.TipoMaterial,m.UnidadDefault,
           ISNULL(x.Entradas,0) AS Entradas,ISNULL(x.Salidas,0) AS Salidas,
           ISNULL(x.Entradas,0)-ISNULL(x.Salidas,0) AS SaldoActual,
           m.StockMinimo,m.StockAviso,m.StockConfigurado,m.Activo
    FROM dbo.ERP_Materiales m
    OUTER APPLY
    (
        SELECT
            SUM(CASE WHEN mm.TipoMovimiento IN(N''Entrada'',N''Retorno'',N''Ajuste'',N''AjustePositivo'') THEN mm.Cantidad ELSE 0 END) AS Entradas,
            SUM(CASE WHEN mm.TipoMovimiento IN(N''Salida'',N''Consumo'',N''Scrap'',N''AjusteNegativo'') THEN mm.Cantidad ELSE 0 END) AS Salidas
        FROM dbo.AlmacenMP_Movimientos mm
        WHERE mm.MaterialID=m.MaterialID AND mm.Activo=1
    ) x
    WHERE UPPER(LTRIM(RTRIM(ISNULL(m.TipoMaterial,N'''')))) IN(N''EMBALAJE'',N''EMBALAJES'')
    ORDER BY m.Codigo;';
END;

IF COL_LENGTH(N'dbo.ERP_Materiales', N'TipoMaterial') IS NULL
BEGIN
    PRINT N'ERP_Materiales ya no contiene TipoMaterial. La separación ya fue aplicada.';
    RETURN;
END;

IF @Confirmar=0
BEGIN
    PRINT N'No se migraron datos. Cambia @Confirmar a 1 en este bloque y ejecuta nuevamente.';
    RETURN;
END;

BEGIN TRY
    BEGIN TRAN;

    CREATE TABLE #Mapa(MaterialID INT NOT NULL PRIMARY KEY, EmbalajeID INT NOT NULL, Codigo NVARCHAR(80) NOT NULL);

    EXEC(N'
    INSERT dbo.ERP_Embalajes
    (Codigo,Nombre,UnidadDefault,Proveedor,RequiereLote,StockMinimo,StockAviso,StockConfigurado,
     FechaCreacion,CreadoPor,FechaModificacion,ActualizadoPor,Activo)
    SELECT m.Codigo,m.Nombre,m.UnidadDefault,m.Proveedor,m.RequiereLote,m.StockMinimo,m.StockAviso,m.StockConfigurado,
           m.FechaCreacion,m.CreadoPor,m.FechaModificacion,m.ActualizadoPor,m.Activo
    FROM dbo.ERP_Materiales m
    WHERE UPPER(LTRIM(RTRIM(ISNULL(m.TipoMaterial,N'''')))) IN(N''EMBALAJE'',N''EMBALAJES'')
      AND NOT EXISTS(SELECT 1 FROM dbo.ERP_Embalajes e WHERE UPPER(e.Codigo)=UPPER(m.Codigo));

    INSERT #Mapa(MaterialID,EmbalajeID,Codigo)
    SELECT m.MaterialID,e.EmbalajeID,m.Codigo
    FROM dbo.ERP_Materiales m
    INNER JOIN dbo.ERP_Embalajes e ON UPPER(e.Codigo)=UPPER(m.Codigo)
    WHERE UPPER(LTRIM(RTRIM(ISNULL(m.TipoMaterial,N'''')))) IN(N''EMBALAJE'',N''EMBALAJES'');');

    IF (SELECT COUNT(*) FROM #Mapa)<>@CantidadEmbalajes
        THROW 51209, 'No todos los embalajes fueron relacionados con el nuevo catálogo. Se canceló la migración.', 1;

    INSERT dbo.AlmacenEmbalajes_Movimientos
    (EmbalajeID,MovimientoOrigenMPID,FechaMovimiento,OrdenFabricacionLegacyID,NumeroOF,TipoMovimiento,Lote,Cantidad,Unidad,
     ResponsableUsuarioID,ResponsableLegacyID,TurnoLegacyID,UbicacionID,EstatusCalidad,Seguimiento,EvidenciaUrl,
     FechaCreacion,CreadoPor,FechaModificacion,ActualizadoPor,Activo,EntregadoPorNombre,
     RequiereValidacionProduccion,ValidadoProduccion,ValidadoProduccionEn,
     ValidadoProduccionEmpleadoLegacyID,ValidadoProduccionNombre,ComentarioValidacionProduccion,ReferenciaOperacion)
    SELECT mp.EmbalajeID,mm.MovimientoID,mm.FechaMovimiento,mm.OrdenFabricacionLegacyID,mm.NumeroOF,mm.TipoMovimiento,
           mm.Lote,mm.Cantidad,mm.Unidad,mm.ResponsableUsuarioID,mm.ResponsableLegacyID,mm.TurnoLegacyID,
           mm.UbicacionID,mm.EstatusCalidad,mm.Seguimiento,mm.EvidenciaUrl,mm.FechaCreacion,mm.CreadoPor,
           mm.FechaModificacion,mm.ActualizadoPor,mm.Activo,mm.EntregadoPorNombre,
           mm.RequiereValidacionProduccion,mm.ValidadoProduccion,mm.ValidadoProduccionEn,
           mm.ValidadoProduccionEmpleadoLegacyID,mm.ValidadoProduccionNombre,mm.ComentarioValidacionProduccion,
           COALESCE(mm.ReferenciaOperacion,CONCAT(N'MIG-EMB-MP-',mm.MovimientoID))
    FROM dbo.AlmacenMP_Movimientos mm
    INNER JOIN #Mapa mp ON mp.MaterialID=mm.MaterialID
    WHERE NOT EXISTS
    (SELECT 1 FROM dbo.AlmacenEmbalajes_Movimientos em WHERE em.MovimientoOrigenMPID=mm.MovimientoID);

    IF EXISTS
    (
        SELECT 1
        FROM dbo.AlmacenMP_Movimientos mm
        INNER JOIN #Mapa mp ON mp.MaterialID=mm.MaterialID
        LEFT JOIN dbo.AlmacenEmbalajes_Movimientos em ON em.MovimientoOrigenMPID=mm.MovimientoID
        WHERE em.MovimientoID IS NULL
    )
        THROW 51210, 'No todos los movimientos de embalaje fueron copiados. Se canceló la migración.', 1;

    DELETE mm
    FROM dbo.AlmacenMP_Movimientos mm
    INNER JOIN #Mapa mp ON mp.MaterialID=mm.MaterialID;

    DELETE m
    FROM dbo.ERP_Materiales m
    INNER JOIN #Mapa mp ON mp.MaterialID=m.MaterialID;

    EXEC sys.sp_executesql N'
    CREATE OR ALTER VIEW dbo.vw_AlmacenMPInventario
    AS
    WITH Mov AS
    (
        SELECT MaterialID,
            SUM(CASE WHEN TipoMovimiento IN(N''Entrada'',N''Retorno'',N''Ajuste'',N''AjustePositivo'') THEN Cantidad ELSE 0 END) AS Entradas,
            SUM(CASE WHEN TipoMovimiento IN(N''Salida'',N''Consumo'',N''Scrap'',N''AjusteNegativo'') THEN Cantidad ELSE 0 END) AS Salidas,
            MAX(FechaMovimiento) AS UltimoMovimiento
        FROM dbo.AlmacenMP_Movimientos WHERE Activo=1 GROUP BY MaterialID
    ), Base AS
    (
        SELECT m.MaterialID,m.Codigo,m.Nombre,m.UnidadDefault AS Unidad,
            ISNULL(v.Entradas,0) AS Entradas,ISNULL(v.Salidas,0) AS Salidas,
            ISNULL(v.Entradas,0)-ISNULL(v.Salidas,0) AS Saldo,
            m.StockMinimo,m.StockAviso,m.StockConfigurado,v.UltimoMovimiento
        FROM dbo.ERP_Materiales m LEFT JOIN Mov v ON v.MaterialID=m.MaterialID WHERE m.Activo=1
    )
    SELECT MaterialID,Codigo,Nombre,Unidad,Entradas,Salidas,Saldo,StockMinimo,StockAviso,StockConfigurado,
        CASE WHEN StockConfigurado=0 THEN N''SIN_CONFIGURAR'' WHEN Saldo<=StockMinimo THEN N''ROJO''
             WHEN Saldo<=StockAviso THEN N''AMARILLO'' ELSE N''VERDE'' END AS Semaforo,
        UltimoMovimiento
    FROM Base;';

    EXEC sys.sp_executesql N'
    CREATE OR ALTER VIEW dbo.vw_AlmacenEmbalajesInventario
    AS
    WITH Mov AS
    (
        SELECT EmbalajeID,
            SUM(CASE WHEN TipoMovimiento IN(N''Entrada'',N''Retorno'',N''Ajuste'',N''AjustePositivo'') THEN Cantidad ELSE 0 END) AS Entradas,
            SUM(CASE WHEN TipoMovimiento IN(N''Salida'',N''Consumo'',N''Scrap'',N''AjusteNegativo'') THEN Cantidad ELSE 0 END) AS Salidas,
            MAX(FechaMovimiento) AS UltimoMovimiento
        FROM dbo.AlmacenEmbalajes_Movimientos WHERE Activo=1 GROUP BY EmbalajeID
    ), Base AS
    (
        SELECT e.EmbalajeID,e.Codigo,e.Nombre,e.UnidadDefault AS Unidad,
            ISNULL(v.Entradas,0) AS Entradas,ISNULL(v.Salidas,0) AS Salidas,
            ISNULL(v.Entradas,0)-ISNULL(v.Salidas,0) AS Saldo,
            e.StockMinimo,e.StockAviso,e.StockConfigurado,v.UltimoMovimiento
        FROM dbo.ERP_Embalajes e LEFT JOIN Mov v ON v.EmbalajeID=e.EmbalajeID WHERE e.Activo=1
    )
    SELECT EmbalajeID,Codigo,Nombre,Unidad,Entradas,Salidas,Saldo,StockMinimo,StockAviso,StockConfigurado,
        CASE WHEN StockConfigurado=0 THEN N''SIN_CONFIGURAR'' WHEN Saldo<=StockMinimo THEN N''ROJO''
             WHEN Saldo<=StockAviso THEN N''AMARILLO'' ELSE N''VERDE'' END AS Semaforo,
        UltimoMovimiento
    FROM Base;';

    DECLARE @Dependencias NVARCHAR(MAX)=N'';
    SELECT @Dependencias += N'DROP INDEX ' + QUOTENAME(i.name) + N' ON dbo.ERP_Materiales;'
    FROM sys.indexes i
    INNER JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
    INNER JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
    WHERE i.object_id=OBJECT_ID(N'dbo.ERP_Materiales') AND c.name=N'TipoMaterial'
      AND i.is_primary_key=0 AND i.is_unique_constraint=0;
    IF @Dependencias<>N'' EXEC sys.sp_executesql @Dependencias;

    DECLARE @Restriccion SYSNAME;
    SELECT @Restriccion=dc.name
    FROM sys.default_constraints dc
    INNER JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id
    WHERE dc.parent_object_id=OBJECT_ID(N'dbo.ERP_Materiales') AND c.name=N'TipoMaterial';
    IF @Restriccion IS NOT NULL EXEC(N'ALTER TABLE dbo.ERP_Materiales DROP CONSTRAINT '+QUOTENAME(@Restriccion)+N';');

    EXEC(N'ALTER TABLE dbo.ERP_Materiales DROP COLUMN TipoMaterial;');

    COMMIT;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT>0 ROLLBACK;
    THROW;
END CATCH;
GO

SELECT N'MP' AS Almacen, COUNT(*) AS Registros FROM dbo.ERP_Materiales
UNION ALL SELECT N'EMBALAJES', COUNT(*) FROM dbo.ERP_Embalajes;
SELECT TOP (500) * FROM dbo.vw_AlmacenEmbalajesInventario ORDER BY Codigo;
GO
