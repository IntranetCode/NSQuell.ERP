USE [ERP_QUELL];
GO
SET NOCOUNT ON;

SELECT N'ERP_Embalajes' AS Objeto, CASE WHEN OBJECT_ID(N'dbo.ERP_Embalajes',N'U') IS NULL THEN N'FALTA' ELSE N'OK' END AS Estado
UNION ALL SELECT N'AlmacenEmbalajes_Movimientos', CASE WHEN OBJECT_ID(N'dbo.AlmacenEmbalajes_Movimientos',N'U') IS NULL THEN N'FALTA' ELSE N'OK' END
UNION ALL SELECT N'vw_AlmacenEmbalajesInventario', CASE WHEN OBJECT_ID(N'dbo.vw_AlmacenEmbalajesInventario',N'V') IS NULL THEN N'FALTA' ELSE N'OK' END
UNION ALL SELECT N'TipoMaterial retirado de ERP_Materiales', CASE WHEN COL_LENGTH(N'dbo.ERP_Materiales',N'TipoMaterial') IS NULL THEN N'OK' ELSE N'PENDIENTE' END;

IF OBJECT_ID(N'dbo.vw_AlmacenEmbalajesInventario',N'V') IS NOT NULL
BEGIN
    EXEC sys.sp_executesql N'
    SELECT COUNT(*) AS TotalEmbalajes,
        SUM(CASE WHEN Semaforo=N''ROJO'' THEN 1 ELSE 0 END) AS Rojos,
        SUM(CASE WHEN Semaforo=N''AMARILLO'' THEN 1 ELSE 0 END) AS Amarillos,
        SUM(CASE WHEN Semaforo=N''VERDE'' THEN 1 ELSE 0 END) AS Verdes,
        SUM(CASE WHEN Semaforo=N''SIN_CONFIGURAR'' THEN 1 ELSE 0 END) AS SinConfigurar
    FROM dbo.vw_AlmacenEmbalajesInventario;';
END;

IF OBJECT_ID(N'dbo.AlmacenEmbalajes_Movimientos',N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.vw_AlmacenEmbalajesInventario',N'V') IS NOT NULL
BEGIN
    EXEC sys.sp_executesql N'
    SELECT ReferenciaOperacion,COUNT(*) AS Repeticiones
    FROM dbo.AlmacenEmbalajes_Movimientos
    WHERE ReferenciaOperacion IS NOT NULL
    GROUP BY ReferenciaOperacion HAVING COUNT(*)>1;

    SELECT e.Codigo,e.Nombre,v.Saldo,v.StockMinimo,v.StockAviso,v.Semaforo,v.UltimoMovimiento
    FROM dbo.vw_AlmacenEmbalajesInventario v
    INNER JOIN dbo.ERP_Embalajes e ON e.EmbalajeID=v.EmbalajeID
    ORDER BY e.Codigo;';
END;
GO
