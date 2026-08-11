USE [ERP_QUELL];
GO
SET NOCOUNT ON;
GO

/*
    VALIDACIÓN DE CIERRE DEL MÓDULO DE ALMACÉN AL 14/07/2026
    Solo lectura. No modifica datos ni objetos.
    Planeación y la integración con OF quedan fuera de esta validación.
*/

CREATE TABLE #Validacion
(
    Orden INT NOT NULL,
    Categoria NVARCHAR(80) NOT NULL,
    Estado NVARCHAR(20) NOT NULL,
    Hallazgos INT NOT NULL,
    Detalle NVARCHAR(500) NOT NULL
);

INSERT #Validacion VALUES
(1, N'Estructura', CASE WHEN OBJECT_ID(N'dbo.ERP_Materiales',N'U') IS NOT NULL THEN N'OK' ELSE N'ERROR' END,
 CASE WHEN OBJECT_ID(N'dbo.ERP_Materiales',N'U') IS NOT NULL THEN 0 ELSE 1 END, N'Tabla ERP_Materiales'),
(2, N'Estructura', CASE WHEN OBJECT_ID(N'dbo.AlmacenMP_Movimientos',N'U') IS NOT NULL THEN N'OK' ELSE N'ERROR' END,
 CASE WHEN OBJECT_ID(N'dbo.AlmacenMP_Movimientos',N'U') IS NOT NULL THEN 0 ELSE 1 END, N'Tabla AlmacenMP_Movimientos'),
(3, N'Estructura', CASE WHEN OBJECT_ID(N'dbo.AlmacenPT_Cajas',N'U') IS NOT NULL THEN N'OK' ELSE N'ERROR' END,
 CASE WHEN OBJECT_ID(N'dbo.AlmacenPT_Cajas',N'U') IS NOT NULL THEN 0 ELSE 1 END, N'Tabla AlmacenPT_Cajas'),
(4, N'Estructura', CASE WHEN OBJECT_ID(N'dbo.AlmacenPT_Movimientos',N'U') IS NOT NULL THEN N'OK' ELSE N'ERROR' END,
 CASE WHEN OBJECT_ID(N'dbo.AlmacenPT_Movimientos',N'U') IS NOT NULL THEN 0 ELSE 1 END, N'Tabla AlmacenPT_Movimientos'),
(5, N'Estructura', CASE WHEN OBJECT_ID(N'dbo.vw_AlmacenMPInventario',N'V') IS NOT NULL THEN N'OK' ELSE N'ERROR' END,
 CASE WHEN OBJECT_ID(N'dbo.vw_AlmacenMPInventario',N'V') IS NOT NULL THEN 0 ELSE 1 END, N'Vista vw_AlmacenMPInventario'),
(6, N'Estructura', CASE WHEN OBJECT_ID(N'dbo.vw_AlmacenPTInventario',N'V') IS NOT NULL THEN N'OK' ELSE N'ERROR' END,
 CASE WHEN OBJECT_ID(N'dbo.vw_AlmacenPTInventario',N'V') IS NOT NULL THEN 0 ELSE 1 END, N'Vista vw_AlmacenPTInventario'),
(7, N'Estructura', CASE WHEN OBJECT_ID(N'dbo.vw_AlmacenPTInventarioCaja',N'V') IS NOT NULL THEN N'OK' ELSE N'ERROR' END,
 CASE WHEN OBJECT_ID(N'dbo.vw_AlmacenPTInventarioCaja',N'V') IS NOT NULL THEN 0 ELSE 1 END, N'Vista vw_AlmacenPTInventarioCaja');

IF OBJECT_ID(N'dbo.vw_AlmacenMPInventario',N'V') IS NOT NULL
BEGIN
    DECLARE @MpNegativos INT = (SELECT COUNT(*) FROM dbo.vw_AlmacenMPInventario WHERE Saldo < 0);
    INSERT #Validacion VALUES
    (10, N'Inventario MP', CASE WHEN @MpNegativos = 0 THEN N'OK' ELSE N'ERROR' END, @MpNegativos, N'Materiales con saldo negativo');

    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.vw_AlmacenMPInventario') AND name=N'StockConfigurado')
        EXEC(N'DECLARE @N INT=(SELECT COUNT(*) FROM dbo.vw_AlmacenMPInventario WHERE StockConfigurado=0);
               INSERT #Validacion VALUES(11,N''Inventario MP'',CASE WHEN @N=0 THEN N''OK'' ELSE N''PENDIENTE'' END,@N,N''Materiales activos sin niveles de stock configurados'');');
    ELSE
        INSERT #Validacion VALUES(11,N'Inventario MP',N'ERROR',1,N'La vista MP no contiene StockConfigurado; ejecutar script 04 corregido.');
END;

IF OBJECT_ID(N'dbo.vw_AlmacenPTInventario',N'V') IS NOT NULL
BEGIN
    DECLARE @PtNegativos INT = (SELECT COUNT(*) FROM dbo.vw_AlmacenPTInventario WHERE SaldoFisico < 0 OR Disponible < 0 OR Retenido < 0);
    INSERT #Validacion VALUES
    (20, N'Inventario PT', CASE WHEN @PtNegativos = 0 THEN N'OK' ELSE N'ERROR' END, @PtNegativos, N'Partes con saldos negativos');

    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.vw_AlmacenPTInventario') AND name=N'StockConfigurado')
        EXEC(N'DECLARE @N INT=(SELECT COUNT(*) FROM dbo.vw_AlmacenPTInventario WHERE StockConfigurado=0);
               INSERT #Validacion VALUES(21,N''Inventario PT'',CASE WHEN @N=0 THEN N''OK'' ELSE N''PENDIENTE'' END,@N,N''Números de parte activos sin niveles de stock configurados'');');
    ELSE
        INSERT #Validacion VALUES(21,N'Inventario PT',N'ERROR',1,N'La vista PT no contiene StockConfigurado; ejecutar script 04 corregido.');
END;

IF OBJECT_ID(N'dbo.vw_AlmacenPTInventarioCaja',N'V') IS NOT NULL
BEGIN
    DECLARE @CajaNegativa INT = (SELECT COUNT(*) FROM dbo.vw_AlmacenPTInventarioCaja WHERE SaldoFisico < 0 OR Disponible < 0 OR Retenido < 0);
    DECLARE @CajaSobreRetenida INT = (SELECT COUNT(*) FROM dbo.vw_AlmacenPTInventarioCaja WHERE Retenido > SaldoFisico);
    INSERT #Validacion VALUES
    (22, N'Inventario PT', CASE WHEN @CajaNegativa = 0 THEN N'OK' ELSE N'ERROR' END, @CajaNegativa, N'Cajas con saldos negativos'),
    (23, N'Inventario PT', CASE WHEN @CajaSobreRetenida = 0 THEN N'OK' ELSE N'ERROR' END, @CajaSobreRetenida, N'Cajas con retenido superior a la existencia física');
END;

IF OBJECT_ID(N'dbo.AlmacenMP_Movimientos',N'U') IS NOT NULL
BEGIN
    DECLARE @MpCantidad INT = (SELECT COUNT(*) FROM dbo.AlmacenMP_Movimientos WHERE Cantidad <= 0);
    DECLARE @MpSinResponsable INT = (SELECT COUNT(*) FROM dbo.AlmacenMP_Movimientos WHERE Activo = 1 AND NULLIF(LTRIM(RTRIM(COALESCE(EntregadoPorNombre,CreadoPor,N''))),N'') IS NULL);
    INSERT #Validacion VALUES
    (30, N'Movimientos MP', CASE WHEN @MpCantidad = 0 THEN N'OK' ELSE N'ERROR' END, @MpCantidad, N'Movimientos con cantidad menor o igual a cero'),
    (31, N'Movimientos MP', CASE WHEN @MpSinResponsable = 0 THEN N'OK' ELSE N'ADVERTENCIA' END, @MpSinResponsable, N'Movimientos activos sin responsable legible');

    IF COL_LENGTH(N'dbo.AlmacenMP_Movimientos',N'ReferenciaOperacion') IS NOT NULL
    BEGIN
        EXEC(N'DECLARE @N INT=(SELECT COUNT(*) FROM (SELECT ReferenciaOperacion FROM dbo.AlmacenMP_Movimientos WHERE ReferenciaOperacion IS NOT NULL GROUP BY ReferenciaOperacion HAVING COUNT(*)>1) d);
               INSERT #Validacion VALUES(32,N''Movimientos MP'',CASE WHEN @N=0 THEN N''OK'' ELSE N''ERROR'' END,@N,N''Referencias de operación duplicadas'');');
        EXEC(N'DECLARE @N INT=(SELECT COUNT(*) FROM dbo.AlmacenMP_Movimientos WHERE Activo=1 AND NULLIF(LTRIM(RTRIM(ReferenciaOperacion)),N'''') IS NULL);
               INSERT #Validacion VALUES(33,N''Movimientos MP'',CASE WHEN @N=0 THEN N''OK'' ELSE N''PENDIENTE'' END,@N,N''Movimientos activos sin referencia de auditoría'');');
    END
    ELSE
    BEGIN
        INSERT #Validacion VALUES(32,N'Movimientos MP',N'ERROR',1,N'Falta ReferenciaOperacion; ejecutar script 04 corregido.');
        INSERT #Validacion VALUES(33,N'Movimientos MP',N'ERROR',1,N'No se puede validar auditoría sin ReferenciaOperacion.');
    END;
END;

IF OBJECT_ID(N'dbo.AlmacenPT_Movimientos',N'U') IS NOT NULL
BEGIN
    DECLARE @PtCantidad INT = (SELECT COUNT(*) FROM dbo.AlmacenPT_Movimientos WHERE Cantidad <= 0);
    DECLARE @PtSinCaja INT = (SELECT COUNT(*) FROM dbo.AlmacenPT_Movimientos WHERE Activo = 1 AND CajaID IS NULL);
    DECLARE @PtCajaParte INT =
    (
        SELECT COUNT(*)
        FROM dbo.AlmacenPT_Movimientos m
        INNER JOIN dbo.AlmacenPT_Cajas c ON c.CajaID = m.CajaID
        WHERE m.ParteID <> c.ParteID
    );
    INSERT #Validacion VALUES
    (40, N'Movimientos PT', CASE WHEN @PtCantidad = 0 THEN N'OK' ELSE N'ERROR' END, @PtCantidad, N'Movimientos con cantidad menor o igual a cero'),
    (41, N'Movimientos PT', CASE WHEN @PtSinCaja = 0 THEN N'OK' ELSE N'ADVERTENCIA' END, @PtSinCaja, N'Movimientos activos sin caja física'),
    (43, N'Movimientos PT', CASE WHEN @PtCajaParte = 0 THEN N'OK' ELSE N'ERROR' END, @PtCajaParte, N'Movimientos cuya caja pertenece a otra parte');

    IF COL_LENGTH(N'dbo.AlmacenPT_Movimientos',N'ReferenciaOperacion') IS NOT NULL
    BEGIN
        EXEC(N'DECLARE @N INT=(SELECT COUNT(*) FROM (SELECT ReferenciaOperacion FROM dbo.AlmacenPT_Movimientos WHERE ReferenciaOperacion IS NOT NULL GROUP BY ReferenciaOperacion HAVING COUNT(*)>1) d);
               INSERT #Validacion VALUES(42,N''Movimientos PT'',CASE WHEN @N=0 THEN N''OK'' ELSE N''ERROR'' END,@N,N''Referencias de operación duplicadas'');');
        EXEC(N'DECLARE @N INT=(SELECT COUNT(*) FROM dbo.AlmacenPT_Movimientos WHERE Activo=1 AND NULLIF(LTRIM(RTRIM(ReferenciaOperacion)),N'''') IS NULL);
               INSERT #Validacion VALUES(44,N''Movimientos PT'',CASE WHEN @N=0 THEN N''OK'' ELSE N''PENDIENTE'' END,@N,N''Movimientos activos sin referencia de auditoría'');');
    END
    ELSE
    BEGIN
        INSERT #Validacion VALUES(42,N'Movimientos PT',N'ERROR',1,N'Falta ReferenciaOperacion; ejecutar script 04 corregido.');
        INSERT #Validacion VALUES(44,N'Movimientos PT',N'ERROR',1,N'No se puede validar auditoría sin ReferenciaOperacion.');
    END;
END;

IF OBJECT_ID(N'dbo.ERP_Ubicaciones',N'U') IS NOT NULL
BEGIN
    DECLARE @SinMP INT = (SELECT COUNT(*) FROM dbo.ERP_Ubicaciones WHERE Activo = 1 AND Almacen IN (N'MP',N'GENERAL'));
    DECLARE @SinPT INT = (SELECT COUNT(*) FROM dbo.ERP_Ubicaciones WHERE Activo = 1 AND Almacen IN (N'PT',N'GENERAL'));
    INSERT #Validacion VALUES
    (50, N'Ubicaciones', CASE WHEN @SinMP > 0 THEN N'OK' ELSE N'ERROR' END, CASE WHEN @SinMP > 0 THEN 0 ELSE 1 END, N'Existencia de ubicaciones activas para MP'),
    (51, N'Ubicaciones', CASE WHEN @SinPT > 0 THEN N'OK' ELSE N'ERROR' END, CASE WHEN @SinPT > 0 THEN 0 ELSE 1 END, N'Existencia de ubicaciones activas para PT');
END;

SELECT Orden, Categoria, Estado, Hallazgos, Detalle
FROM #Validacion
ORDER BY Orden;

SELECT
    SUM(CASE WHEN Estado = N'ERROR' THEN 1 ELSE 0 END) AS Errores,
    SUM(CASE WHEN Estado = N'ADVERTENCIA' THEN 1 ELSE 0 END) AS Advertencias,
    SUM(CASE WHEN Estado = N'PENDIENTE' THEN 1 ELSE 0 END) AS Pendientes,
    SUM(CASE WHEN Estado = N'OK' THEN 1 ELSE 0 END) AS Correctos,
    CASE
        WHEN SUM(CASE WHEN Estado = N'ERROR' THEN 1 ELSE 0 END) > 0 THEN N'REQUIERE CORRECCIÓN'
        WHEN SUM(CASE WHEN Estado IN (N'ADVERTENCIA',N'PENDIENTE') THEN 1 ELSE 0 END) > 0 THEN N'FUNCIONAL CON PENDIENTES'
        ELSE N'LISTO PARA PRUEBAS DE USUARIO'
    END AS ResultadoGeneral
FROM #Validacion;

IF OBJECT_ID(N'dbo.AlmacenMP_Movimientos',N'U') IS NOT NULL
    SELECT N'MP' AS Modulo, TipoMovimiento, COUNT(*) AS Movimientos, SUM(Cantidad) AS Cantidad
    FROM dbo.AlmacenMP_Movimientos WHERE Activo = 1 GROUP BY TipoMovimiento ORDER BY TipoMovimiento;

IF OBJECT_ID(N'dbo.AlmacenPT_Movimientos',N'U') IS NOT NULL
    SELECT N'PT' AS Modulo, TipoMovimiento, COUNT(*) AS Movimientos, SUM(Cantidad) AS Cantidad
    FROM dbo.AlmacenPT_Movimientos WHERE Activo = 1 GROUP BY TipoMovimiento ORDER BY TipoMovimiento;
GO
