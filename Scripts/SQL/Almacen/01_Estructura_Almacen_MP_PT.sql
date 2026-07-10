USE [ERP_QUELL];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

/*
    Módulo de Almacén MP/PT - estructura corregida e idempotente.

    Objetivo:
    - Reparar una ejecución parcial del script anterior sin borrar datos.
    - Crear únicamente objetos faltantes.
    - Agregar columnas faltantes a objetos del módulo.
    - Crear las vistas al final mediante SQL dinámico para evitar errores de compilación anticipada.

    No modifica usuarios, roles, menús, permisos, compras, solicitudes ni Planeación.
*/

IF DB_NAME() <> N'ERP_QUELL'
    THROW 51000, 'La consulta debe ejecutarse dentro de ERP_QUELL.', 1;

IF OBJECT_ID(N'dbo.ERP_Partes', N'U') IS NULL
    THROW 51001, 'No existe dbo.ERP_Partes. Se canceló la instalación de Almacén.', 1;

IF OBJECT_ID(N'dbo.ERP_Clientes', N'U') IS NULL
    THROW 51002, 'No existe dbo.ERP_Clientes. Se canceló la instalación de Almacén.', 1;

IF OBJECT_ID(N'dbo.Usuarios', N'U') IS NULL
    THROW 51003, 'No existe dbo.Usuarios. Se canceló la instalación de Almacén.', 1;
GO

BEGIN TRY
    BEGIN TRAN;

    /* ================================================================
       1. CATÁLOGO DE UBICACIONES
       ================================================================ */
    IF OBJECT_ID(N'dbo.ERP_Ubicaciones', N'U') IS NULL
    BEGIN
        EXEC(N'
        CREATE TABLE dbo.ERP_Ubicaciones
        (
            UbicacionID INT IDENTITY(1,1) NOT NULL
                CONSTRAINT PK_ERP_Ubicaciones PRIMARY KEY,
            Almacen NVARCHAR(60) NOT NULL,
            Rack NVARCHAR(120) NOT NULL,
            Nivel NVARCHAR(40) NULL,
            Posicion NVARCHAR(40) NULL,
            FechaCreacion DATETIME2(7) NOT NULL
                CONSTRAINT DF_ERP_Ubicaciones_FechaCreacion DEFAULT SYSUTCDATETIME(),
            CreadoPor NVARCHAR(120) NULL,
            FechaModificacion DATETIME2(7) NULL,
            ActualizadoPor NVARCHAR(120) NULL,
            Activo BIT NOT NULL
                CONSTRAINT DF_ERP_Ubicaciones_Activo DEFAULT 1
        );');
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.ERP_Ubicaciones')
          AND name = N'UX_ERP_Ubicaciones_Detalle'
    )
    BEGIN
        IF NOT EXISTS
        (
            SELECT Almacen, Rack, ISNULL(Nivel, N''), ISNULL(Posicion, N'')
            FROM dbo.ERP_Ubicaciones
            GROUP BY Almacen, Rack, ISNULL(Nivel, N''), ISNULL(Posicion, N'')
            HAVING COUNT(*) > 1
        )
        BEGIN
            EXEC(N'
            CREATE UNIQUE INDEX UX_ERP_Ubicaciones_Detalle
            ON dbo.ERP_Ubicaciones(Almacen, Rack, Nivel, Posicion);');
        END;
    END;

    /* ================================================================
       2. CATÁLOGO DE MATERIALES MP
       ================================================================ */
    IF OBJECT_ID(N'dbo.ERP_Materiales', N'U') IS NULL
    BEGIN
        EXEC(N'
        CREATE TABLE dbo.ERP_Materiales
        (
            MaterialID INT IDENTITY(1,1) NOT NULL
                CONSTRAINT PK_ERP_Materiales PRIMARY KEY,
            Codigo NVARCHAR(80) NOT NULL,
            Nombre NVARCHAR(250) NOT NULL,
            TipoMaterial NVARCHAR(80) NULL,
            UnidadDefault NVARCHAR(20) NOT NULL,
            Proveedor NVARCHAR(200) NULL,
            RequiereLote BIT NOT NULL
                CONSTRAINT DF_ERP_Materiales_RequiereLote DEFAULT 1,
            StockMinimo DECIMAL(18,3) NOT NULL
                CONSTRAINT DF_ERP_Materiales_StockMinimo DEFAULT 0,
            StockAviso DECIMAL(18,3) NOT NULL
                CONSTRAINT DF_ERP_Materiales_StockAviso DEFAULT 0,
            FechaCreacion DATETIME2(7) NOT NULL
                CONSTRAINT DF_ERP_Materiales_FechaCreacion DEFAULT SYSUTCDATETIME(),
            CreadoPor NVARCHAR(120) NULL,
            FechaModificacion DATETIME2(7) NULL,
            ActualizadoPor NVARCHAR(120) NULL,
            Activo BIT NOT NULL
                CONSTRAINT DF_ERP_Materiales_Activo DEFAULT 1
        );');
    END;
    ELSE
    BEGIN
        IF COL_LENGTH(N'dbo.ERP_Materiales', N'StockMinimo') IS NULL
            EXEC(N'ALTER TABLE dbo.ERP_Materiales ADD StockMinimo DECIMAL(18,3) NOT NULL CONSTRAINT DF_ERP_Materiales_StockMinimo DEFAULT 0 WITH VALUES;');

        IF COL_LENGTH(N'dbo.ERP_Materiales', N'StockAviso') IS NULL
            EXEC(N'ALTER TABLE dbo.ERP_Materiales ADD StockAviso DECIMAL(18,3) NOT NULL CONSTRAINT DF_ERP_Materiales_StockAviso DEFAULT 0 WITH VALUES;');
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'dbo.ERP_Materiales')
          AND name = N'CK_ERP_Materiales_Niveles'
    )
    BEGIN
        EXEC(N'
        ALTER TABLE dbo.ERP_Materiales WITH CHECK
        ADD CONSTRAINT CK_ERP_Materiales_Niveles
        CHECK (StockMinimo >= 0 AND StockAviso >= StockMinimo);');
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.ERP_Materiales')
          AND name = N'UX_ERP_Materiales_Codigo'
    )
    BEGIN
        IF NOT EXISTS
        (
            SELECT Codigo
            FROM dbo.ERP_Materiales
            GROUP BY Codigo
            HAVING COUNT(*) > 1
        )
        BEGIN
            EXEC(N'CREATE UNIQUE INDEX UX_ERP_Materiales_Codigo ON dbo.ERP_Materiales(Codigo);');
        END;
    END;

    /* ================================================================
       3. MOVIMIENTOS DE ALMACÉN MP
       ================================================================ */
    IF OBJECT_ID(N'dbo.AlmacenMP_Movimientos', N'U') IS NULL
    BEGIN
        EXEC(N'
        CREATE TABLE dbo.AlmacenMP_Movimientos
        (
            MovimientoID BIGINT IDENTITY(1,1) NOT NULL
                CONSTRAINT PK_AlmacenMP_Movimientos PRIMARY KEY,
            FechaMovimiento DATETIME2(7) NOT NULL
                CONSTRAINT DF_AlmacenMP_Movimientos_Fecha DEFAULT SYSDATETIME(),
            MaterialID INT NOT NULL,
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
            FechaCreacion DATETIME2(7) NOT NULL
                CONSTRAINT DF_AlmacenMP_Movimientos_Creacion DEFAULT SYSUTCDATETIME(),
            CreadoPor NVARCHAR(120) NULL,
            FechaModificacion DATETIME2(7) NULL,
            ActualizadoPor NVARCHAR(120) NULL,
            Activo BIT NOT NULL
                CONSTRAINT DF_AlmacenMP_Movimientos_Activo DEFAULT 1,
            EntregadoPorNombre NVARCHAR(180) NULL,
            RequiereValidacionProduccion BIT NOT NULL
                CONSTRAINT DF_AlmacenMP_Movimientos_RequiereValidacion DEFAULT 0,
            ValidadoProduccion BIT NOT NULL
                CONSTRAINT DF_AlmacenMP_Movimientos_Validado DEFAULT 1,
            ValidadoProduccionEn DATETIME2(7) NULL,
            ValidadoProduccionEmpleadoLegacyID INT NULL,
            ValidadoProduccionNombre NVARCHAR(180) NULL,
            ComentarioValidacionProduccion NVARCHAR(500) NULL,
            CONSTRAINT FK_AlmacenMP_Movimientos_Material
                FOREIGN KEY(MaterialID) REFERENCES dbo.ERP_Materiales(MaterialID),
            CONSTRAINT FK_AlmacenMP_Movimientos_Ubicacion
                FOREIGN KEY(UbicacionID) REFERENCES dbo.ERP_Ubicaciones(UbicacionID),
            CONSTRAINT FK_AlmacenMP_Movimientos_Usuario
                FOREIGN KEY(ResponsableUsuarioID) REFERENCES dbo.Usuarios(UsuarioID),
            CONSTRAINT CK_AlmacenMP_Movimientos_Cantidad CHECK(Cantidad > 0),
            CONSTRAINT CK_AlmacenMP_Movimientos_Tipo CHECK
            (
                TipoMovimiento IN
                (N''Entrada'', N''Salida'', N''Retorno'', N''Consumo'', N''Scrap'',
                 N''Ajuste'', N''AjustePositivo'', N''AjusteNegativo'')
            )
        );');
    END;
    ELSE
    BEGIN
        IF COL_LENGTH(N'dbo.AlmacenMP_Movimientos', N'OrdenFabricacionLegacyID') IS NULL
            EXEC(N'ALTER TABLE dbo.AlmacenMP_Movimientos ADD OrdenFabricacionLegacyID INT NULL;');

        IF COL_LENGTH(N'dbo.AlmacenMP_Movimientos', N'NumeroOF') IS NULL
            EXEC(N'ALTER TABLE dbo.AlmacenMP_Movimientos ADD NumeroOF NVARCHAR(80) NULL;');

        IF COL_LENGTH(N'dbo.AlmacenMP_Movimientos', N'ResponsableUsuarioID') IS NULL
            EXEC(N'ALTER TABLE dbo.AlmacenMP_Movimientos ADD ResponsableUsuarioID INT NULL;');

        IF COL_LENGTH(N'dbo.AlmacenMP_Movimientos', N'UbicacionID') IS NULL
            EXEC(N'ALTER TABLE dbo.AlmacenMP_Movimientos ADD UbicacionID INT NULL;');

        IF COL_LENGTH(N'dbo.AlmacenMP_Movimientos', N'EntregadoPorNombre') IS NULL
            EXEC(N'ALTER TABLE dbo.AlmacenMP_Movimientos ADD EntregadoPorNombre NVARCHAR(180) NULL;');

        IF COL_LENGTH(N'dbo.AlmacenMP_Movimientos', N'Seguimiento') IS NULL
            EXEC(N'ALTER TABLE dbo.AlmacenMP_Movimientos ADD Seguimiento NVARCHAR(800) NULL;');

        IF COL_LENGTH(N'dbo.AlmacenMP_Movimientos', N'RequiereValidacionProduccion') IS NULL
            EXEC(N'ALTER TABLE dbo.AlmacenMP_Movimientos ADD RequiereValidacionProduccion BIT NOT NULL CONSTRAINT DF_AlmacenMP_Movimientos_RequiereValidacion DEFAULT 0 WITH VALUES;');

        IF COL_LENGTH(N'dbo.AlmacenMP_Movimientos', N'ValidadoProduccion') IS NULL
            EXEC(N'ALTER TABLE dbo.AlmacenMP_Movimientos ADD ValidadoProduccion BIT NOT NULL CONSTRAINT DF_AlmacenMP_Movimientos_Validado DEFAULT 1 WITH VALUES;');
    END;

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.AlmacenMP_Movimientos')
          AND name = N'IX_AlmacenMP_Movimientos_MaterialFecha'
    )
        EXEC(N'CREATE INDEX IX_AlmacenMP_Movimientos_MaterialFecha ON dbo.AlmacenMP_Movimientos(MaterialID, FechaMovimiento DESC);');

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.AlmacenMP_Movimientos')
          AND name = N'IX_AlmacenMP_Movimientos_OF'
    )
        EXEC(N'CREATE INDEX IX_AlmacenMP_Movimientos_OF ON dbo.AlmacenMP_Movimientos(NumeroOF) WHERE NumeroOF IS NOT NULL;');

    /* ================================================================
       4. NIVELES DE STOCK EN EL CATÁLOGO EXISTENTE DE PARTES
       ================================================================ */
    IF COL_LENGTH(N'dbo.ERP_Partes', N'StockMinimo') IS NULL
        EXEC(N'ALTER TABLE dbo.ERP_Partes ADD StockMinimo INT NOT NULL CONSTRAINT DF_ERP_Partes_StockMinimo DEFAULT 0 WITH VALUES;');

    IF COL_LENGTH(N'dbo.ERP_Partes', N'StockAviso') IS NULL
        EXEC(N'ALTER TABLE dbo.ERP_Partes ADD StockAviso INT NOT NULL CONSTRAINT DF_ERP_Partes_StockAviso DEFAULT 0 WITH VALUES;');

    /* ================================================================
       5. CAJAS DE PRODUCTO TERMINADO
       ================================================================ */
    IF OBJECT_ID(N'dbo.AlmacenPT_Cajas', N'U') IS NULL
    BEGIN
        EXEC(N'
        CREATE TABLE dbo.AlmacenPT_Cajas
        (
            CajaID INT IDENTITY(1,1) NOT NULL
                CONSTRAINT PK_AlmacenPT_Cajas PRIMARY KEY,
            LegacyCajaID INT NULL,
            ParteID INT NOT NULL,
            OrdenFabricacionLegacyID INT NULL,
            NumeroOF NVARCHAR(80) NULL,
            Etiqueta NVARCHAR(120) NOT NULL,
            NumeroCaja INT NOT NULL,
            CantidadInicial INT NOT NULL,
            LoteEtiqueta NVARCHAR(120) NULL,
            EstadoCalidad NVARCHAR(30) NOT NULL,
            UbicacionID INT NULL,
            FechaEntrada DATETIME2(7) NOT NULL
                CONSTRAINT DF_AlmacenPT_Cajas_Fecha DEFAULT SYSDATETIME(),
            FechaCreacion DATETIME2(7) NOT NULL
                CONSTRAINT DF_AlmacenPT_Cajas_Creacion DEFAULT SYSUTCDATETIME(),
            CreadoPor NVARCHAR(120) NULL,
            FechaModificacion DATETIME2(7) NULL,
            ActualizadoPor NVARCHAR(120) NULL,
            Activo BIT NOT NULL
                CONSTRAINT DF_AlmacenPT_Cajas_Activo DEFAULT 1,
            CONSTRAINT FK_AlmacenPT_Cajas_Parte
                FOREIGN KEY(ParteID) REFERENCES dbo.ERP_Partes(ParteID),
            CONSTRAINT FK_AlmacenPT_Cajas_Ubicacion
                FOREIGN KEY(UbicacionID) REFERENCES dbo.ERP_Ubicaciones(UbicacionID),
            CONSTRAINT CK_AlmacenPT_Cajas_Cantidad CHECK(CantidadInicial > 0)
        );');
    END;

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.AlmacenPT_Cajas')
          AND name = N'UX_AlmacenPT_Cajas_Etiqueta'
    )
        EXEC(N'CREATE UNIQUE INDEX UX_AlmacenPT_Cajas_Etiqueta ON dbo.AlmacenPT_Cajas(Etiqueta);');

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.AlmacenPT_Cajas')
          AND name = N'UX_AlmacenPT_Cajas_Legacy'
    )
        EXEC(N'CREATE UNIQUE INDEX UX_AlmacenPT_Cajas_Legacy ON dbo.AlmacenPT_Cajas(LegacyCajaID) WHERE LegacyCajaID IS NOT NULL;');

    /* ================================================================
       6. MOVIMIENTOS DE PRODUCTO TERMINADO
       ================================================================ */
    IF OBJECT_ID(N'dbo.AlmacenPT_Movimientos', N'U') IS NULL
    BEGIN
        EXEC(N'
        CREATE TABLE dbo.AlmacenPT_Movimientos
        (
            MovimientoID BIGINT IDENTITY(1,1) NOT NULL
                CONSTRAINT PK_AlmacenPT_Movimientos PRIMARY KEY,
            LegacyMovimientoID INT NULL,
            CajaID INT NULL,
            ParteID INT NOT NULL,
            OrdenFabricacionLegacyID INT NULL,
            NumeroOF NVARCHAR(80) NULL,
            TipoMovimiento NVARCHAR(30) NOT NULL,
            Cantidad INT NOT NULL,
            UbicacionID INT NULL,
            Rack NVARCHAR(120) NULL,
            EstadoCalidad NVARCHAR(30) NULL,
            CertificadoUrl NVARCHAR(500) NULL,
            ResponsableUsuarioID INT NULL,
            ResponsableLegacyID INT NULL,
            Observaciones NVARCHAR(800) NULL,
            FechaMovimiento DATETIME2(7) NOT NULL
                CONSTRAINT DF_AlmacenPT_Movimientos_Fecha DEFAULT SYSDATETIME(),
            FechaCreacion DATETIME2(7) NOT NULL
                CONSTRAINT DF_AlmacenPT_Movimientos_Creacion DEFAULT SYSUTCDATETIME(),
            CreadoPor NVARCHAR(120) NULL,
            FechaModificacion DATETIME2(7) NULL,
            ActualizadoPor NVARCHAR(120) NULL,
            Activo BIT NOT NULL
                CONSTRAINT DF_AlmacenPT_Movimientos_Activo DEFAULT 1,
            CONSTRAINT FK_AlmacenPT_Movimientos_Caja
                FOREIGN KEY(CajaID) REFERENCES dbo.AlmacenPT_Cajas(CajaID),
            CONSTRAINT FK_AlmacenPT_Movimientos_Parte
                FOREIGN KEY(ParteID) REFERENCES dbo.ERP_Partes(ParteID),
            CONSTRAINT FK_AlmacenPT_Movimientos_Ubicacion
                FOREIGN KEY(UbicacionID) REFERENCES dbo.ERP_Ubicaciones(UbicacionID),
            CONSTRAINT FK_AlmacenPT_Movimientos_Usuario
                FOREIGN KEY(ResponsableUsuarioID) REFERENCES dbo.Usuarios(UsuarioID),
            CONSTRAINT CK_AlmacenPT_Movimientos_Cantidad CHECK(Cantidad > 0),
            CONSTRAINT CK_AlmacenPT_Movimientos_Tipo CHECK
            (
                TipoMovimiento IN
                (N''Entrada'', N''Salida'', N''Embarque'', N''Retencion'',
                 N''Liberacion'', N''Retorno'', N''Scrap'',
                 N''AjustePositivo'', N''AjusteNegativo'')
            )
        );');
    END;

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.AlmacenPT_Movimientos')
          AND name = N'IX_AlmacenPT_Movimientos_ParteFecha'
    )
        EXEC(N'CREATE INDEX IX_AlmacenPT_Movimientos_ParteFecha ON dbo.AlmacenPT_Movimientos(ParteID, FechaMovimiento DESC);');

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.AlmacenPT_Movimientos')
          AND name = N'IX_AlmacenPT_Movimientos_CajaFecha'
    )
        EXEC(N'CREATE INDEX IX_AlmacenPT_Movimientos_CajaFecha ON dbo.AlmacenPT_Movimientos(CajaID, FechaMovimiento DESC);');

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.AlmacenPT_Movimientos')
          AND name = N'UX_AlmacenPT_Movimientos_Legacy'
    )
        EXEC(N'CREATE UNIQUE INDEX UX_AlmacenPT_Movimientos_Legacy ON dbo.AlmacenPT_Movimientos(LegacyMovimientoID) WHERE LegacyMovimientoID IS NOT NULL;');

    /* ================================================================
       7. UBICACIONES BASE - SIN FORZAR IDENTIDADES
       ================================================================ */
    IF NOT EXISTS
    (
        SELECT 1 FROM dbo.ERP_Ubicaciones
        WHERE Almacen = N'CUARENTENA' AND Rack = N'GP12-RETENCION'
          AND Nivel IS NULL AND Posicion IS NULL
    )
        INSERT dbo.ERP_Ubicaciones(Almacen, Rack, FechaCreacion, CreadoPor, Activo)
        VALUES(N'CUARENTENA', N'GP12-RETENCION', SYSUTCDATETIME(), N'script-almacen', 1);

    IF NOT EXISTS
    (
        SELECT 1 FROM dbo.ERP_Ubicaciones
        WHERE Almacen = N'MP' AND Rack = N'MP-SIN-UBICAR'
          AND Nivel IS NULL AND Posicion IS NULL
    )
        INSERT dbo.ERP_Ubicaciones(Almacen, Rack, FechaCreacion, CreadoPor, Activo)
        VALUES(N'MP', N'MP-SIN-UBICAR', SYSUTCDATETIME(), N'script-almacen', 1);

    IF NOT EXISTS
    (
        SELECT 1 FROM dbo.ERP_Ubicaciones
        WHERE Almacen = N'PT' AND Rack = N'PT-SIN-UBICAR'
          AND Nivel IS NULL AND Posicion IS NULL
    )
        INSERT dbo.ERP_Ubicaciones(Almacen, Rack, FechaCreacion, CreadoPor, Activo)
        VALUES(N'PT', N'PT-SIN-UBICAR', SYSUTCDATETIME(), N'script-almacen', 1);

    IF NOT EXISTS
    (
        SELECT 1 FROM dbo.ERP_Ubicaciones
        WHERE Almacen = N'SCRAP' AND Rack = N'SCRAP-MOLIENDA'
          AND Nivel IS NULL AND Posicion IS NULL
    )
        INSERT dbo.ERP_Ubicaciones(Almacen, Rack, FechaCreacion, CreadoPor, Activo)
        VALUES(N'SCRAP', N'SCRAP-MOLIENDA', SYSUTCDATETIME(), N'script-almacen', 1);

    COMMIT;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK;
    THROW;
END CATCH;
GO

/* ================================================================
   8. VISTAS DE EXISTENCIAS
   Se crean mediante EXEC para compilar después de reparar las tablas.
   ================================================================ */
EXEC(N'
CREATE OR ALTER VIEW dbo.vw_AlmacenMPInventario
AS
WITH Mov AS
(
    SELECT
        MaterialID,
        SUM(CASE
                WHEN TipoMovimiento IN
                     (N''Entrada'', N''Retorno'', N''Ajuste'', N''AjustePositivo'')
                    THEN Cantidad
                ELSE 0
            END) AS Entradas,
        SUM(CASE
                WHEN TipoMovimiento IN
                     (N''Salida'', N''Consumo'', N''Scrap'', N''AjusteNegativo'')
                    THEN Cantidad
                ELSE 0
            END) AS Salidas,
        MAX(FechaMovimiento) AS UltimoMovimiento
    FROM dbo.AlmacenMP_Movimientos
    WHERE Activo = 1
    GROUP BY MaterialID
)
SELECT
    m.MaterialID,
    m.Codigo,
    m.Nombre,
    ISNULL(m.TipoMaterial, N'''') AS TipoMaterial,
    m.UnidadDefault AS Unidad,
    ISNULL(v.Entradas, 0) AS Entradas,
    ISNULL(v.Salidas, 0) AS Salidas,
    ISNULL(v.Entradas, 0) - ISNULL(v.Salidas, 0) AS Saldo,
    m.StockMinimo,
    m.StockAviso,
    CASE
        WHEN ISNULL(v.Entradas, 0) - ISNULL(v.Salidas, 0) <= m.StockMinimo
            THEN N''ROJO''
        WHEN ISNULL(v.Entradas, 0) - ISNULL(v.Salidas, 0) <= m.StockAviso
            THEN N''AMARILLO''
        ELSE N''VERDE''
    END AS Semaforo,
    v.UltimoMovimiento
FROM dbo.ERP_Materiales AS m
LEFT JOIN Mov AS v
    ON v.MaterialID = m.MaterialID
WHERE m.Activo = 1;
');
GO

EXEC(N'
CREATE OR ALTER VIEW dbo.vw_AlmacenPTInventarioCaja
AS
WITH M AS
(
    SELECT
        CajaID,
        SUM(CASE
                WHEN TipoMovimiento IN
                     (N''Entrada'', N''Retorno'', N''AjustePositivo'')
                    THEN Cantidad
                ELSE 0
            END) AS Entradas,
        SUM(CASE
                WHEN TipoMovimiento IN
                     (N''Salida'', N''Embarque'', N''Scrap'', N''AjusteNegativo'')
                    THEN Cantidad
                ELSE 0
            END) AS Salidas,
        SUM(CASE
                WHEN TipoMovimiento = N''Retencion'' THEN Cantidad
                WHEN TipoMovimiento = N''Liberacion'' THEN -Cantidad
                ELSE 0
            END) AS Retenido,
        MAX(FechaMovimiento) AS UltimoMovimiento
    FROM dbo.AlmacenPT_Movimientos
    WHERE Activo = 1
      AND CajaID IS NOT NULL
    GROUP BY CajaID
)
SELECT
    c.CajaID,
    c.ParteID,
    c.Etiqueta,
    c.NumeroCaja,
    c.NumeroOF,
    c.LoteEtiqueta,
    c.EstadoCalidad,
    c.UbicacionID,
    ISNULL(m.Entradas, 0) AS Entradas,
    ISNULL(m.Salidas, 0) AS Salidas,
    ISNULL(m.Entradas, 0) - ISNULL(m.Salidas, 0) AS SaldoFisico,
    CASE
        WHEN ISNULL(m.Retenido, 0) < 0 THEN 0
        ELSE ISNULL(m.Retenido, 0)
    END AS Retenido,
    CASE
        WHEN ISNULL(m.Entradas, 0) - ISNULL(m.Salidas, 0)
             - CASE WHEN ISNULL(m.Retenido, 0) < 0 THEN 0 ELSE ISNULL(m.Retenido, 0) END < 0
            THEN 0
        ELSE ISNULL(m.Entradas, 0) - ISNULL(m.Salidas, 0)
             - CASE WHEN ISNULL(m.Retenido, 0) < 0 THEN 0 ELSE ISNULL(m.Retenido, 0) END
    END AS Disponible,
    m.UltimoMovimiento
FROM dbo.AlmacenPT_Cajas AS c
LEFT JOIN M AS m
    ON m.CajaID = c.CajaID
WHERE c.Activo = 1;
');
GO

EXEC(N'
CREATE OR ALTER VIEW dbo.vw_AlmacenPTInventario
AS
WITH M AS
(
    SELECT
        ParteID,
        COUNT(DISTINCT CajaID) AS Cajas,
        SUM(CASE
                WHEN TipoMovimiento IN
                     (N''Entrada'', N''Retorno'', N''AjustePositivo'')
                    THEN Cantidad
                ELSE 0
            END) AS Entradas,
        SUM(CASE
                WHEN TipoMovimiento IN
                     (N''Salida'', N''Embarque'', N''Scrap'', N''AjusteNegativo'')
                    THEN Cantidad
                ELSE 0
            END) AS Salidas,
        SUM(CASE
                WHEN TipoMovimiento = N''Retencion'' THEN Cantidad
                WHEN TipoMovimiento = N''Liberacion'' THEN -Cantidad
                ELSE 0
            END) AS Retenido,
        MAX(FechaMovimiento) AS UltimoMovimiento
    FROM dbo.AlmacenPT_Movimientos
    WHERE Activo = 1
    GROUP BY ParteID
), Existencia AS
(
    SELECT
        p.ParteID,
        ISNULL(m.Cajas, 0) AS Cajas,
        ISNULL(m.Entradas, 0) AS Entradas,
        ISNULL(m.Salidas, 0) AS Salidas,
        ISNULL(m.Entradas, 0) - ISNULL(m.Salidas, 0) AS SaldoFisico,
        CASE
            WHEN ISNULL(m.Retenido, 0) < 0 THEN 0
            ELSE ISNULL(m.Retenido, 0)
        END AS Retenido,
        CASE
            WHEN ISNULL(m.Entradas, 0) - ISNULL(m.Salidas, 0)
                 - CASE WHEN ISNULL(m.Retenido, 0) < 0 THEN 0 ELSE ISNULL(m.Retenido, 0) END < 0
                THEN 0
            ELSE ISNULL(m.Entradas, 0) - ISNULL(m.Salidas, 0)
                 - CASE WHEN ISNULL(m.Retenido, 0) < 0 THEN 0 ELSE ISNULL(m.Retenido, 0) END
        END AS Disponible,
        m.UltimoMovimiento
    FROM dbo.ERP_Partes AS p
    LEFT JOIN M AS m
        ON m.ParteID = p.ParteID
)
SELECT
    p.ParteID,
    p.NumeroParte,
    p.Descripcion,
    ISNULL(c.Nombre, N'''') AS Cliente,
    e.Cajas,
    e.Entradas,
    e.Salidas,
    e.SaldoFisico,
    e.Retenido,
    e.Disponible,
    p.StockMinimo,
    p.StockAviso,
    CASE
        WHEN e.Disponible <= p.StockMinimo THEN N''ROJO''
        WHEN e.Disponible <= p.StockAviso THEN N''AMARILLO''
        ELSE N''VERDE''
    END AS Semaforo,
    e.UltimoMovimiento
FROM dbo.ERP_Partes AS p
LEFT JOIN dbo.ERP_Clientes AS c
    ON c.ClienteID = p.ClienteID
INNER JOIN Existencia AS e
    ON e.ParteID = p.ParteID
WHERE p.Activo = 1;
');
GO

/* ================================================================
   9. VALIDACIÓN FINAL
   ================================================================ */
SELECT
    DB_NAME() AS BaseDatos,
    OBJECT_ID(N'dbo.ERP_Ubicaciones', N'U') AS ERP_Ubicaciones,
    OBJECT_ID(N'dbo.ERP_Materiales', N'U') AS ERP_Materiales,
    OBJECT_ID(N'dbo.AlmacenMP_Movimientos', N'U') AS AlmacenMP_Movimientos,
    OBJECT_ID(N'dbo.AlmacenPT_Cajas', N'U') AS AlmacenPT_Cajas,
    OBJECT_ID(N'dbo.AlmacenPT_Movimientos', N'U') AS AlmacenPT_Movimientos,
    OBJECT_ID(N'dbo.vw_AlmacenMPInventario', N'V') AS vw_AlmacenMPInventario,
    OBJECT_ID(N'dbo.vw_AlmacenPTInventarioCaja', N'V') AS vw_AlmacenPTInventarioCaja,
    OBJECT_ID(N'dbo.vw_AlmacenPTInventario', N'V') AS vw_AlmacenPTInventario,
    COL_LENGTH(N'dbo.ERP_Materiales', N'StockMinimo') AS ERP_Materiales_StockMinimo,
    COL_LENGTH(N'dbo.ERP_Materiales', N'StockAviso') AS ERP_Materiales_StockAviso,
    COL_LENGTH(N'dbo.AlmacenMP_Movimientos', N'NumeroOF') AS AlmacenMP_NumeroOF,
    COL_LENGTH(N'dbo.ERP_Partes', N'StockMinimo') AS ERP_Partes_StockMinimo,
    COL_LENGTH(N'dbo.ERP_Partes', N'StockAviso') AS ERP_Partes_StockAviso;
GO

SELECT N'Estructura de Almacén creada o reparada correctamente.' AS Resultado;
GO
