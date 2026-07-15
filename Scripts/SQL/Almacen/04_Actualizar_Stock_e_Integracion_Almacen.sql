USE [ERP_QUELL];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

/*
    Actualización del módulo de Almacén hasta el 13/07/2026.

    Incluye:
    - Niveles de stock configurables para MP y PT.
    - Estado SIN_CONFIGURAR hasta que el usuario confirme los niveles.
    - Referencia de operación única para evitar descuentos duplicados.
    - Vistas de inventario actualizadas.

    Este script es idempotente. Puede ejecutarse nuevamente si una ejecución
    anterior falló antes de crear las columnas.

    No modifica tablas ni código de Planeación.
*/

IF OBJECT_ID(N'dbo.ERP_Materiales', N'U') IS NULL
    THROW 51000, 'No existe dbo.ERP_Materiales. Ejecuta primero 01_Estructura_Almacen_MP_PT.sql.', 1;
IF OBJECT_ID(N'dbo.AlmacenMP_Movimientos', N'U') IS NULL
    THROW 51001, 'No existe dbo.AlmacenMP_Movimientos.', 1;
IF OBJECT_ID(N'dbo.AlmacenPT_Movimientos', N'U') IS NULL
    THROW 51002, 'No existe dbo.AlmacenPT_Movimientos.', 1;
IF OBJECT_ID(N'dbo.ERP_Partes', N'U') IS NULL
    THROW 51003, 'No existe dbo.ERP_Partes.', 1;
IF OBJECT_ID(N'dbo.AlmacenPT_Cajas', N'U') IS NULL
    THROW 51004, 'No existe dbo.AlmacenPT_Cajas.', 1;
GO

BEGIN TRY
    BEGIN TRAN;

    /* Se utiliza SQL dinámico porque SQL Server compila el lote completo antes
       de ejecutar los ALTER TABLE. Así se evita el error "columna no válida". */

    IF COL_LENGTH(N'dbo.ERP_Materiales', N'StockMinimo') IS NULL
        EXEC(N'ALTER TABLE dbo.ERP_Materiales
               ADD StockMinimo DECIMAL(18,3) NOT NULL
                   CONSTRAINT DF_ERP_Materiales_StockMinimo DEFAULT 0 WITH VALUES;');

    IF COL_LENGTH(N'dbo.ERP_Materiales', N'StockAviso') IS NULL
        EXEC(N'ALTER TABLE dbo.ERP_Materiales
               ADD StockAviso DECIMAL(18,3) NOT NULL
                   CONSTRAINT DF_ERP_Materiales_StockAviso DEFAULT 0 WITH VALUES;');

    IF COL_LENGTH(N'dbo.ERP_Partes', N'StockMinimo') IS NULL
        EXEC(N'ALTER TABLE dbo.ERP_Partes
               ADD StockMinimo INT NOT NULL
                   CONSTRAINT DF_ERP_Partes_StockMinimo DEFAULT 0 WITH VALUES;');

    IF COL_LENGTH(N'dbo.ERP_Partes', N'StockAviso') IS NULL
        EXEC(N'ALTER TABLE dbo.ERP_Partes
               ADD StockAviso INT NOT NULL
                   CONSTRAINT DF_ERP_Partes_StockAviso DEFAULT 0 WITH VALUES;');

    IF COL_LENGTH(N'dbo.ERP_Materiales', N'StockConfigurado') IS NULL
        EXEC(N'ALTER TABLE dbo.ERP_Materiales
               ADD StockConfigurado BIT NOT NULL
                   CONSTRAINT DF_ERP_Materiales_StockConfigurado DEFAULT 0 WITH VALUES;');

    IF COL_LENGTH(N'dbo.ERP_Partes', N'StockConfigurado') IS NULL
        EXEC(N'ALTER TABLE dbo.ERP_Partes
               ADD StockConfigurado BIT NOT NULL
                   CONSTRAINT DF_ERP_Partes_StockConfigurado DEFAULT 0 WITH VALUES;');

    IF COL_LENGTH(N'dbo.AlmacenMP_Movimientos', N'ReferenciaOperacion') IS NULL
        EXEC(N'ALTER TABLE dbo.AlmacenMP_Movimientos
               ADD ReferenciaOperacion NVARCHAR(120) NULL;');

    IF COL_LENGTH(N'dbo.AlmacenPT_Movimientos', N'ReferenciaOperacion') IS NULL
        EXEC(N'ALTER TABLE dbo.AlmacenPT_Movimientos
               ADD ReferenciaOperacion NVARCHAR(120) NULL;');

    /* Las columnas ya existen en este punto, pero se consultan mediante otro
       lote dinámico para que SQL Server vuelva a resolver el metadato. */
    EXEC sys.sp_executesql N'
        UPDATE dbo.ERP_Materiales
        SET StockConfigurado = 1
        WHERE StockConfigurado = 0
          AND (StockMinimo > 0 OR StockAviso > 0);

        UPDATE dbo.ERP_Partes
        SET StockConfigurado = 1
        WHERE StockConfigurado = 0
          AND (StockMinimo > 0 OR StockAviso > 0);';

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.AlmacenMP_Movimientos')
          AND name = N'UX_AlmacenMP_Movimientos_ReferenciaOperacion'
    )
    BEGIN
        EXEC(N'CREATE UNIQUE INDEX UX_AlmacenMP_Movimientos_ReferenciaOperacion
               ON dbo.AlmacenMP_Movimientos(ReferenciaOperacion)
               WHERE ReferenciaOperacion IS NOT NULL;');
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.AlmacenPT_Movimientos')
          AND name = N'UX_AlmacenPT_Movimientos_ReferenciaOperacion'
    )
    BEGIN
        EXEC(N'CREATE UNIQUE INDEX UX_AlmacenPT_Movimientos_ReferenciaOperacion
               ON dbo.AlmacenPT_Movimientos(ReferenciaOperacion)
               WHERE ReferenciaOperacion IS NOT NULL;');
    END;

    COMMIT;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    THROW;
END CATCH;
GO

CREATE OR ALTER VIEW dbo.vw_AlmacenMPInventario
AS
WITH Mov AS
(
    SELECT
        MaterialID,
        SUM(CASE WHEN TipoMovimiento IN(N'Entrada',N'Retorno',N'Ajuste',N'AjustePositivo') THEN Cantidad ELSE 0 END) AS Entradas,
        SUM(CASE WHEN TipoMovimiento IN(N'Salida',N'Consumo',N'Scrap',N'AjusteNegativo') THEN Cantidad ELSE 0 END) AS Salidas,
        MAX(FechaMovimiento) AS UltimoMovimiento
    FROM dbo.AlmacenMP_Movimientos
    WHERE Activo = 1
    GROUP BY MaterialID
), Base AS
(
    SELECT
        m.MaterialID,
        m.Codigo,
        m.Nombre,
        m.UnidadDefault AS Unidad,
        ISNULL(v.Entradas,0) AS Entradas,
        ISNULL(v.Salidas,0) AS Salidas,
        ISNULL(v.Entradas,0)-ISNULL(v.Salidas,0) AS Saldo,
        m.StockMinimo,
        m.StockAviso,
        m.StockConfigurado,
        v.UltimoMovimiento
    FROM dbo.ERP_Materiales m
    LEFT JOIN Mov v ON v.MaterialID=m.MaterialID
    WHERE m.Activo=1
)
SELECT
    MaterialID,Codigo,Nombre,Unidad,Entradas,Salidas,Saldo,
    StockMinimo,StockAviso,StockConfigurado,
    CASE
        WHEN StockConfigurado=0 THEN N'SIN_CONFIGURAR'
        WHEN Saldo<=StockMinimo THEN N'ROJO'
        WHEN Saldo<=StockAviso THEN N'AMARILLO'
        ELSE N'VERDE'
    END AS Semaforo,
    UltimoMovimiento
FROM Base;
GO

CREATE OR ALTER VIEW dbo.vw_AlmacenPTInventarioCaja
AS
WITH Mov AS
(
    SELECT
        CajaID,
        SUM(CASE WHEN TipoMovimiento IN(N'Entrada',N'Retorno',N'AjustePositivo') THEN Cantidad ELSE 0 END) AS Entradas,
        SUM(CASE WHEN TipoMovimiento IN(N'Salida',N'Embarque',N'Scrap',N'AjusteNegativo') THEN Cantidad ELSE 0 END) AS Salidas,
        SUM(CASE WHEN TipoMovimiento=N'Retencion' THEN Cantidad WHEN TipoMovimiento=N'Liberacion' THEN -Cantidad ELSE 0 END) AS RetenidoMovimiento,
        MAX(FechaMovimiento) AS UltimoMovimiento
    FROM dbo.AlmacenPT_Movimientos
    WHERE Activo=1 AND CajaID IS NOT NULL
    GROUP BY CajaID
), Base AS
(
    SELECT
        c.CajaID,c.ParteID,c.Etiqueta,c.NumeroCaja,c.NumeroOF,c.LoteEtiqueta,
        c.EstadoCalidad,c.UbicacionID,
        ISNULL(m.Entradas,0) AS Entradas,
        ISNULL(m.Salidas,0) AS Salidas,
        ISNULL(m.Entradas,0)-ISNULL(m.Salidas,0) AS SaldoFisico,
        ISNULL(m.RetenidoMovimiento,0) AS RetenidoMovimiento,
        m.UltimoMovimiento
    FROM dbo.AlmacenPT_Cajas c
    LEFT JOIN Mov m ON m.CajaID=c.CajaID
    WHERE c.Activo=1
), Calidad AS
(
    SELECT *,
        CASE
            WHEN SaldoFisico<=0 THEN 0
            WHEN EstadoCalidad<>N'Liberado' AND RetenidoMovimiento<=0 THEN SaldoFisico
            WHEN RetenidoMovimiento<0 THEN 0
            WHEN RetenidoMovimiento>SaldoFisico THEN SaldoFisico
            ELSE RetenidoMovimiento
        END AS Retenido
    FROM Base
)
SELECT
    CajaID,ParteID,Etiqueta,NumeroCaja,NumeroOF,LoteEtiqueta,EstadoCalidad,UbicacionID,
    Entradas,Salidas,SaldoFisico,Retenido,
    CASE WHEN SaldoFisico-Retenido<0 THEN 0 ELSE SaldoFisico-Retenido END AS Disponible,
    UltimoMovimiento
FROM Calidad;
GO

CREATE OR ALTER VIEW dbo.vw_AlmacenPTInventario
AS
WITH Caja AS
(
    SELECT
        ParteID,
        SUM(CASE WHEN SaldoFisico>0 THEN 1 ELSE 0 END) AS Cajas,
        SUM(Entradas) AS Entradas,
        SUM(Salidas) AS Salidas,
        SUM(SaldoFisico) AS SaldoFisico,
        SUM(Retenido) AS Retenido,
        SUM(Disponible) AS Disponible,
        MAX(UltimoMovimiento) AS UltimoMovimiento
    FROM dbo.vw_AlmacenPTInventarioCaja
    GROUP BY ParteID
), GlobalMov AS
(
    SELECT
        ParteID,
        SUM(CASE WHEN TipoMovimiento IN(N'Entrada',N'Retorno',N'AjustePositivo') THEN Cantidad ELSE 0 END) AS Entradas,
        SUM(CASE WHEN TipoMovimiento IN(N'Salida',N'Embarque',N'Scrap',N'AjusteNegativo') THEN Cantidad ELSE 0 END) AS Salidas,
        SUM(CASE WHEN TipoMovimiento=N'Retencion' THEN Cantidad WHEN TipoMovimiento=N'Liberacion' THEN -Cantidad ELSE 0 END) AS Retenido,
        MAX(FechaMovimiento) AS UltimoMovimiento
    FROM dbo.AlmacenPT_Movimientos
    WHERE Activo=1 AND CajaID IS NULL
    GROUP BY ParteID
), Base AS
(
    SELECT
        p.ParteID,p.NumeroParte,p.Descripcion,ISNULL(cli.Nombre,N'') AS Cliente,
        ISNULL(c.Cajas,0) AS Cajas,
        ISNULL(c.Entradas,0)+ISNULL(g.Entradas,0) AS Entradas,
        ISNULL(c.Salidas,0)+ISNULL(g.Salidas,0) AS Salidas,
        ISNULL(c.SaldoFisico,0)+ISNULL(g.Entradas,0)-ISNULL(g.Salidas,0) AS SaldoFisico,
        ISNULL(c.Retenido,0)+
            CASE WHEN ISNULL(g.Retenido,0)<0 THEN 0 ELSE ISNULL(g.Retenido,0) END AS RetenidoCalculado,
        p.StockMinimo,p.StockAviso,p.StockConfigurado,
        CASE
            WHEN c.UltimoMovimiento IS NULL THEN g.UltimoMovimiento
            WHEN g.UltimoMovimiento IS NULL THEN c.UltimoMovimiento
            WHEN c.UltimoMovimiento>=g.UltimoMovimiento THEN c.UltimoMovimiento
            ELSE g.UltimoMovimiento
        END AS UltimoMovimiento
    FROM dbo.ERP_Partes p
    LEFT JOIN dbo.ERP_Clientes cli ON cli.ClienteID=p.ClienteID
    LEFT JOIN Caja c ON c.ParteID=p.ParteID
    LEFT JOIN GlobalMov g ON g.ParteID=p.ParteID
    WHERE p.Activo=1
), Normalizado AS
(
    SELECT
        ParteID,NumeroParte,Descripcion,Cliente,Cajas,Entradas,Salidas,SaldoFisico,
        CASE
            WHEN SaldoFisico<=0 THEN 0
            WHEN RetenidoCalculado<0 THEN 0
            WHEN RetenidoCalculado>SaldoFisico THEN SaldoFisico
            ELSE RetenidoCalculado
        END AS Retenido,
        StockMinimo,StockAviso,StockConfigurado,UltimoMovimiento
    FROM Base
), DisponibleCalc AS
(
    SELECT *,
        CASE WHEN SaldoFisico-Retenido<0 THEN 0 ELSE SaldoFisico-Retenido END AS Disponible
    FROM Normalizado
)
SELECT
    ParteID,NumeroParte,Descripcion,Cliente,Cajas,Entradas,Salidas,SaldoFisico,Retenido,Disponible,
    StockMinimo,StockAviso,StockConfigurado,
    CASE
        WHEN StockConfigurado=0 THEN N'SIN_CONFIGURAR'
        WHEN Disponible<=StockMinimo THEN N'ROJO'
        WHEN Disponible<=StockAviso THEN N'AMARILLO'
        ELSE N'VERDE'
    END AS Semaforo,
    UltimoMovimiento
FROM DisponibleCalc;
GO

SELECT
    OBJECT_ID(N'dbo.vw_AlmacenMPInventario',N'V') AS VistaMP,
    OBJECT_ID(N'dbo.vw_AlmacenPTInventario',N'V') AS VistaPT,
    COL_LENGTH(N'dbo.ERP_Materiales',N'StockConfigurado') AS StockConfiguradoMP,
    COL_LENGTH(N'dbo.ERP_Partes',N'StockConfigurado') AS StockConfiguradoPT,
    COL_LENGTH(N'dbo.AlmacenMP_Movimientos',N'ReferenciaOperacion') AS RefMP,
    COL_LENGTH(N'dbo.AlmacenPT_Movimientos',N'ReferenciaOperacion') AS RefPT;
GO

SELECT N'Actualización de stock e integración de Almacén aplicada correctamente.' AS Resultado;
GO
