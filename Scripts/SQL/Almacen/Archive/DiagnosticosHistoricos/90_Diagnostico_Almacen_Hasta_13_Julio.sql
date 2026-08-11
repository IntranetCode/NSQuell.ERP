USE [ERP_QUELL];
GO
SET NOCOUNT ON;
GO

/* Diagnóstico de solo lectura del módulo de Almacén. No modifica datos. */

SELECT
    DB_NAME() AS BaseDatos,
    OBJECT_ID(N'dbo.ERP_Materiales',N'U') AS ERP_Materiales,
    OBJECT_ID(N'dbo.AlmacenMP_Movimientos',N'U') AS MovimientosMP,
    OBJECT_ID(N'dbo.AlmacenPT_Cajas',N'U') AS CajasPT,
    OBJECT_ID(N'dbo.AlmacenPT_Movimientos',N'U') AS MovimientosPT,
    OBJECT_ID(N'dbo.vw_AlmacenMPInventario',N'V') AS VistaMP,
    OBJECT_ID(N'dbo.vw_AlmacenPTInventario',N'V') AS VistaPT,
    COL_LENGTH(N'dbo.ERP_Materiales',N'StockConfigurado') AS StockConfiguradoMP,
    COL_LENGTH(N'dbo.ERP_Partes',N'StockConfigurado') AS StockConfiguradoPT,
    COL_LENGTH(N'dbo.AlmacenMP_Movimientos',N'ReferenciaOperacion') AS ReferenciaMP,
    COL_LENGTH(N'dbo.AlmacenPT_Movimientos',N'ReferenciaOperacion') AS ReferenciaPT;
GO

IF OBJECT_ID(N'dbo.vw_AlmacenMPInventario',N'V') IS NOT NULL
   AND EXISTS
   (
       SELECT 1
       FROM sys.columns
       WHERE object_id=OBJECT_ID(N'dbo.vw_AlmacenMPInventario')
         AND name=N'StockConfigurado'
   )
BEGIN
    EXEC sys.sp_executesql N'
        SELECT
            COUNT(*) AS MaterialesMP,
            SUM(CASE WHEN StockConfigurado=1 THEN 1 ELSE 0 END) AS Configurados,
            SUM(CASE WHEN StockConfigurado=0 THEN 1 ELSE 0 END) AS SinConfigurar,
            SUM(CASE WHEN Semaforo=N''ROJO'' THEN 1 ELSE 0 END) AS Rojos,
            SUM(CASE WHEN Semaforo=N''AMARILLO'' THEN 1 ELSE 0 END) AS Amarillos,
            SUM(CASE WHEN Semaforo=N''VERDE'' THEN 1 ELSE 0 END) AS Verdes
        FROM dbo.vw_AlmacenMPInventario;

        SELECT TOP (50)
            Codigo,Nombre,Unidad,Saldo,StockMinimo,StockAviso,StockConfigurado,Semaforo
        FROM dbo.vw_AlmacenMPInventario
        ORDER BY CASE Semaforo
                     WHEN N''SIN_CONFIGURAR'' THEN 0
                     WHEN N''ROJO'' THEN 1
                     WHEN N''AMARILLO'' THEN 2
                     ELSE 3
                 END,
                 Codigo;';
END
ELSE
BEGIN
    SELECT N'La vista MP todavía no contiene StockConfigurado. Ejecuta primero 04_Actualizar_Stock_e_Integracion_Almacen.sql.' AS DiagnosticoMP;
END;
GO

IF OBJECT_ID(N'dbo.vw_AlmacenPTInventario',N'V') IS NOT NULL
   AND EXISTS
   (
       SELECT 1
       FROM sys.columns
       WHERE object_id=OBJECT_ID(N'dbo.vw_AlmacenPTInventario')
         AND name=N'StockConfigurado'
   )
BEGIN
    EXEC sys.sp_executesql N'
        SELECT
            COUNT(*) AS PartesPT,
            SUM(CASE WHEN StockConfigurado=1 THEN 1 ELSE 0 END) AS Configurados,
            SUM(CASE WHEN StockConfigurado=0 THEN 1 ELSE 0 END) AS SinConfigurar,
            SUM(CASE WHEN Semaforo=N''ROJO'' THEN 1 ELSE 0 END) AS Rojos,
            SUM(CASE WHEN Semaforo=N''AMARILLO'' THEN 1 ELSE 0 END) AS Amarillos,
            SUM(CASE WHEN Semaforo=N''VERDE'' THEN 1 ELSE 0 END) AS Verdes,
            SUM(Disponible) AS PiezasDisponibles,
            SUM(Retenido) AS PiezasRetenidas
        FROM dbo.vw_AlmacenPTInventario;

        SELECT TOP (50)
            NumeroParte,Descripcion,Disponible,Retenido,
            StockMinimo,StockAviso,StockConfigurado,Semaforo
        FROM dbo.vw_AlmacenPTInventario
        ORDER BY CASE Semaforo
                     WHEN N''SIN_CONFIGURAR'' THEN 0
                     WHEN N''ROJO'' THEN 1
                     WHEN N''AMARILLO'' THEN 2
                     ELSE 3
                 END,
                 NumeroParte;';
END
ELSE
BEGIN
    SELECT N'La vista PT todavía no contiene StockConfigurado. Ejecuta primero 04_Actualizar_Stock_e_Integracion_Almacen.sql.' AS DiagnosticoPT;
END;
GO

IF OBJECT_ID(N'dbo.AlmacenPT_Cajas',N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.AlmacenPT_Movimientos',N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.vw_AlmacenPTInventarioCaja',N'V') IS NOT NULL
BEGIN
    SELECT TOP (50)
        c.CajaID,c.Etiqueta,c.EstadoCalidad,v.SaldoFisico,v.Retenido,v.Disponible,
        ISNULL(m.RetencionMovimiento,0) AS RetencionRegistradaPorMovimiento
    FROM dbo.AlmacenPT_Cajas c
    INNER JOIN dbo.vw_AlmacenPTInventarioCaja v ON v.CajaID=c.CajaID
    OUTER APPLY
    (
        SELECT SUM(CASE WHEN TipoMovimiento=N'Retencion' THEN Cantidad
                        WHEN TipoMovimiento=N'Liberacion' THEN -Cantidad ELSE 0 END) AS RetencionMovimiento
        FROM dbo.AlmacenPT_Movimientos
        WHERE CajaID=c.CajaID AND Activo=1
    ) m
    WHERE c.Activo=1
      AND c.EstadoCalidad<>N'Liberado'
      AND v.SaldoFisico>0
      AND ISNULL(m.RetencionMovimiento,0)<=0
    ORDER BY c.FechaEntrada,c.CajaID;
END;
GO

IF COL_LENGTH(N'dbo.AlmacenMP_Movimientos',N'ReferenciaOperacion') IS NOT NULL
BEGIN
    EXEC sys.sp_executesql N'
        SELECT ReferenciaOperacion,COUNT(*) AS Repeticiones
        FROM dbo.AlmacenMP_Movimientos
        WHERE ReferenciaOperacion IS NOT NULL
        GROUP BY ReferenciaOperacion
        HAVING COUNT(*)>1;';
END
ELSE
BEGIN
    SELECT N'No existe ReferenciaOperacion en AlmacenMP_Movimientos.' AS DiagnosticoReferenciaMP;
END;
GO

IF COL_LENGTH(N'dbo.AlmacenPT_Movimientos',N'ReferenciaOperacion') IS NOT NULL
BEGIN
    EXEC sys.sp_executesql N'
        SELECT ReferenciaOperacion,COUNT(*) AS Repeticiones
        FROM dbo.AlmacenPT_Movimientos
        WHERE ReferenciaOperacion IS NOT NULL
        GROUP BY ReferenciaOperacion
        HAVING COUNT(*)>1;';
END
ELSE
BEGIN
    SELECT N'No existe ReferenciaOperacion en AlmacenPT_Movimientos.' AS DiagnosticoReferenciaPT;
END;
GO

SELECT N'Diagnóstico finalizado. Las consultas de referencias duplicadas deben regresar cero filas.' AS Resultado;
GO
