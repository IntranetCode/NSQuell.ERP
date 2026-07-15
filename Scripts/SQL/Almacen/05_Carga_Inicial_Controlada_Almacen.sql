USE [ERP_QUELL];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

/*
    CARGA INICIAL CONTROLADA DE ALMACÉN MP Y PT
    -------------------------------------------------------------
    1. Respaldar ERP_QUELL.
    2. Capturar los datos reales en las tablas temporales.
    3. Ejecutar con @Confirmar = 0 y revisar las vistas previas.
    4. Cambiar @Confirmar = 1 únicamente cuando los datos sean correctos.

    Este script NO modifica Planeación ni genera consumos por OF.
    Las existencias se crean mediante movimientos de entrada auditables.
*/

DECLARE @Confirmar BIT = 0;
DECLARE @Usuario NVARCHAR(120) = N'carga-inicial-almacen-14-07-2026';

IF OBJECT_ID(N'dbo.ERP_Materiales', N'U') IS NULL
    THROW 51000, 'No existe dbo.ERP_Materiales.', 1;
IF OBJECT_ID(N'dbo.AlmacenMP_Movimientos', N'U') IS NULL
    THROW 51001, 'No existe dbo.AlmacenMP_Movimientos.', 1;
IF OBJECT_ID(N'dbo.ERP_Partes', N'U') IS NULL
    THROW 51002, 'No existe dbo.ERP_Partes.', 1;
IF OBJECT_ID(N'dbo.AlmacenPT_Cajas', N'U') IS NULL
    THROW 51003, 'No existe dbo.AlmacenPT_Cajas.', 1;
IF OBJECT_ID(N'dbo.AlmacenPT_Movimientos', N'U') IS NULL
    THROW 51004, 'No existe dbo.AlmacenPT_Movimientos.', 1;
IF COL_LENGTH(N'dbo.AlmacenMP_Movimientos', N'ReferenciaOperacion') IS NULL
    THROW 51005, 'Falta ReferenciaOperacion en AlmacenMP_Movimientos. Ejecuta el script 04 corregido.', 1;
IF COL_LENGTH(N'dbo.AlmacenPT_Movimientos', N'ReferenciaOperacion') IS NULL
    THROW 51006, 'Falta ReferenciaOperacion en AlmacenPT_Movimientos. Ejecuta el script 04 corregido.', 1;

CREATE TABLE #StockInicialMP
(
    FilaID INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    Codigo NVARCHAR(80) NOT NULL,
    Cantidad DECIMAL(18,3) NOT NULL,
    Unidad NVARCHAR(20) NULL,
    Lote NVARCHAR(120) NULL,
    UbicacionID INT NULL,
    ReferenciaOperacion NVARCHAR(120) NOT NULL,
    Observaciones NVARCHAR(800) NULL
);

CREATE TABLE #StockInicialPT
(
    FilaID INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    NumeroParte NVARCHAR(120) NOT NULL,
    Etiqueta NVARCHAR(120) NOT NULL,
    NumeroCaja INT NOT NULL,
    Cantidad INT NOT NULL,
    Lote NVARCHAR(120) NULL,
    EstadoCalidad NVARCHAR(30) NOT NULL,
    UbicacionID INT NULL,
    ReferenciaOperacion NVARCHAR(120) NOT NULL,
    Observaciones NVARCHAR(800) NULL
);

/* =============================================================
   CAPTURA DE DATOS REALES
   Descomenta y reemplaza los ejemplos. No uses cantidades ficticias.
   ============================================================= */

-- INSERT #StockInicialMP
-- (Codigo, Cantidad, Unidad, Lote, UbicacionID, ReferenciaOperacion, Observaciones)
-- VALUES
-- (N'02-10-003-12', 125.500, N'KG', N'LOTE-REAL-001', NULL, N'INV-140726-MP-001', N'Conteo físico inicial');

-- INSERT #StockInicialPT
-- (NumeroParte, Etiqueta, NumeroCaja, Cantidad, Lote, EstadoCalidad, UbicacionID, ReferenciaOperacion, Observaciones)
-- VALUES
-- (N'NUMERO-PARTE-REAL', N'ETIQUETA-REAL-001', 1, 200, N'LOTE-PT-001', N'Liberado', NULL, N'INV-140726-PT-001', N'Conteo físico inicial');

IF NOT EXISTS (SELECT 1 FROM #StockInicialMP)
   AND NOT EXISTS (SELECT 1 FROM #StockInicialPT)
BEGIN
    SELECT
        N'SIN DATOS' AS Estado,
        N'Captura los conteos reales en #StockInicialMP y/o #StockInicialPT. El script no insertó movimientos.' AS Mensaje;
    RETURN;
END;

CREATE TABLE #Errores
(
    Modulo NVARCHAR(10) NOT NULL,
    FilaID INT NULL,
    Referencia NVARCHAR(120) NULL,
    Error NVARCHAR(500) NOT NULL
);

/* Validaciones MP */
INSERT #Errores(Modulo, FilaID, Referencia, Error)
SELECT N'MP', s.FilaID, s.ReferenciaOperacion, N'El código y la referencia de operación son obligatorios.'
FROM #StockInicialMP s
WHERE NULLIF(LTRIM(RTRIM(s.Codigo)),N'') IS NULL
   OR NULLIF(LTRIM(RTRIM(s.ReferenciaOperacion)),N'') IS NULL;

INSERT #Errores(Modulo, FilaID, Referencia, Error)
SELECT N'MP', s.FilaID, s.ReferenciaOperacion, N'La cantidad debe ser mayor que cero.'
FROM #StockInicialMP s WHERE s.Cantidad <= 0;

INSERT #Errores(Modulo, FilaID, Referencia, Error)
SELECT N'MP', s.FilaID, s.ReferenciaOperacion, N'No existe un material activo con el código indicado.'
FROM #StockInicialMP s
LEFT JOIN dbo.ERP_Materiales m ON UPPER(m.Codigo) = UPPER(LTRIM(RTRIM(s.Codigo))) AND m.Activo = 1
WHERE m.MaterialID IS NULL;

INSERT #Errores(Modulo, FilaID, Referencia, Error)
SELECT N'MP', s.FilaID, s.ReferenciaOperacion, N'El material requiere lote y no puede cargarse como S/L.'
FROM #StockInicialMP s
INNER JOIN dbo.ERP_Materiales m ON UPPER(m.Codigo) = UPPER(LTRIM(RTRIM(s.Codigo))) AND m.Activo = 1
WHERE m.RequiereLote = 1
  AND NULLIF(LTRIM(RTRIM(COALESCE(s.Lote,N''))),N'') IS NULL;

INSERT #Errores(Modulo, FilaID, Referencia, Error)
SELECT N'MP', s.FilaID, s.ReferenciaOperacion, N'La ubicación no existe o no está activa.'
FROM #StockInicialMP s
LEFT JOIN dbo.ERP_Ubicaciones u ON u.UbicacionID = s.UbicacionID AND u.Activo = 1
WHERE s.UbicacionID IS NOT NULL AND u.UbicacionID IS NULL;

INSERT #Errores(Modulo, FilaID, Referencia, Error)
SELECT N'MP', MIN(s.FilaID), s.ReferenciaOperacion, N'Referencia duplicada dentro de la carga.'
FROM #StockInicialMP s
GROUP BY s.ReferenciaOperacion HAVING COUNT(*) > 1;

INSERT #Errores(Modulo, FilaID, Referencia, Error)
SELECT N'MP', s.FilaID, s.ReferenciaOperacion, N'La referencia ya existe en movimientos MP.'
FROM #StockInicialMP s
INNER JOIN dbo.AlmacenMP_Movimientos m ON m.ReferenciaOperacion = s.ReferenciaOperacion;

/* Validaciones PT */
INSERT #Errores(Modulo, FilaID, Referencia, Error)
SELECT N'PT', s.FilaID, s.ReferenciaOperacion, N'Número de parte, etiqueta y referencia de operación son obligatorios.'
FROM #StockInicialPT s
WHERE NULLIF(LTRIM(RTRIM(s.NumeroParte)),N'') IS NULL
   OR NULLIF(LTRIM(RTRIM(s.Etiqueta)),N'') IS NULL
   OR NULLIF(LTRIM(RTRIM(s.ReferenciaOperacion)),N'') IS NULL;

INSERT #Errores(Modulo, FilaID, Referencia, Error)
SELECT N'PT', s.FilaID, s.ReferenciaOperacion, N'La cantidad y el número de caja deben ser mayores que cero.'
FROM #StockInicialPT s WHERE s.Cantidad <= 0 OR s.NumeroCaja <= 0;

INSERT #Errores(Modulo, FilaID, Referencia, Error)
SELECT N'PT', s.FilaID, s.ReferenciaOperacion, N'Estado de calidad inválido.'
FROM #StockInicialPT s
WHERE s.EstadoCalidad NOT IN (N'Liberado', N'Retenido', N'GP12', N'Cuarentena', N'Rechazado');

INSERT #Errores(Modulo, FilaID, Referencia, Error)
SELECT N'PT', s.FilaID, s.ReferenciaOperacion, N'No existe un número de parte activo.'
FROM #StockInicialPT s
LEFT JOIN dbo.ERP_Partes p ON UPPER(p.NumeroParte) = UPPER(LTRIM(RTRIM(s.NumeroParte))) AND p.Activo = 1
WHERE p.ParteID IS NULL;

INSERT #Errores(Modulo, FilaID, Referencia, Error)
SELECT N'PT', s.FilaID, s.ReferenciaOperacion, N'La ubicación no existe o no está activa.'
FROM #StockInicialPT s
LEFT JOIN dbo.ERP_Ubicaciones u ON u.UbicacionID = s.UbicacionID AND u.Activo = 1
WHERE s.UbicacionID IS NOT NULL AND u.UbicacionID IS NULL;

INSERT #Errores(Modulo, FilaID, Referencia, Error)
SELECT N'PT', MIN(s.FilaID), s.ReferenciaOperacion, N'Referencia duplicada dentro de la carga.'
FROM #StockInicialPT s
GROUP BY s.ReferenciaOperacion HAVING COUNT(*) > 1;

INSERT #Errores(Modulo, FilaID, Referencia, Error)
SELECT N'PT', MIN(s.FilaID), s.Etiqueta, N'Etiqueta duplicada dentro de la carga.'
FROM #StockInicialPT s
GROUP BY s.Etiqueta HAVING COUNT(*) > 1;

INSERT #Errores(Modulo, FilaID, Referencia, Error)
SELECT N'PT', s.FilaID, s.ReferenciaOperacion, N'La etiqueta ya existe en AlmacenPT_Cajas.'
FROM #StockInicialPT s
INNER JOIN dbo.AlmacenPT_Cajas c ON c.Etiqueta = s.Etiqueta;

INSERT #Errores(Modulo, FilaID, Referencia, Error)
SELECT N'PT', s.FilaID, s.ReferenciaOperacion, N'La referencia ya existe en movimientos PT.'
FROM #StockInicialPT s
INNER JOIN dbo.AlmacenPT_Movimientos m ON m.ReferenciaOperacion = s.ReferenciaOperacion;

INSERT #Errores(Modulo, FilaID, Referencia, Error)
SELECT N'PT', MIN(s.FilaID), LEFT(s.ReferenciaOperacion,116)+N'-RET', N'Las referencias generadas para retención se duplican dentro de la carga.'
FROM #StockInicialPT s
WHERE s.EstadoCalidad <> N'Liberado'
GROUP BY LEFT(s.ReferenciaOperacion,116)+N'-RET'
HAVING COUNT(*) > 1;

INSERT #Errores(Modulo, FilaID, Referencia, Error)
SELECT N'PT', s.FilaID, LEFT(s.ReferenciaOperacion,116)+N'-RET', N'La referencia generada para retención ya existe en movimientos PT.'
FROM #StockInicialPT s
INNER JOIN dbo.AlmacenPT_Movimientos m ON m.ReferenciaOperacion = LEFT(s.ReferenciaOperacion,116)+N'-RET'
WHERE s.EstadoCalidad <> N'Liberado';

IF EXISTS (SELECT 1 FROM #Errores)
BEGIN
    SELECT Modulo, FilaID, Referencia, Error FROM #Errores ORDER BY Modulo, FilaID;
    THROW 51020, 'La carga inicial contiene errores. No se insertó información.', 1;
END;

DECLARE @UbicacionMPDefault INT =
(
    SELECT TOP (1) UbicacionID FROM dbo.ERP_Ubicaciones
    WHERE Activo = 1 AND Almacen IN (N'MP', N'GENERAL')
    ORDER BY CASE WHEN Rack = N'MP-SIN-UBICAR' THEN 0 ELSE 1 END, UbicacionID
);
DECLARE @UbicacionPTDefault INT =
(
    SELECT TOP (1) UbicacionID FROM dbo.ERP_Ubicaciones
    WHERE Activo = 1 AND Almacen IN (N'PT', N'GENERAL')
    ORDER BY CASE WHEN Rack = N'PT-SIN-UBICAR' THEN 0 ELSE 1 END, UbicacionID
);

UPDATE #StockInicialMP SET UbicacionID = COALESCE(UbicacionID, @UbicacionMPDefault);
UPDATE #StockInicialPT SET UbicacionID = COALESCE(UbicacionID, @UbicacionPTDefault);

SELECT
    N'MP' AS Modulo, s.FilaID, m.MaterialID, m.Codigo, m.Nombre,
    s.Cantidad, COALESCE(NULLIF(s.Unidad,N''), m.UnidadDefault) AS Unidad,
    COALESCE(NULLIF(s.Lote,N''), N'S/L') AS Lote,
    s.UbicacionID, s.ReferenciaOperacion, s.Observaciones
FROM #StockInicialMP s
INNER JOIN dbo.ERP_Materiales m ON UPPER(m.Codigo) = UPPER(LTRIM(RTRIM(s.Codigo))) AND m.Activo = 1
ORDER BY s.FilaID;

SELECT
    N'PT' AS Modulo, s.FilaID, p.ParteID, p.NumeroParte, p.Descripcion,
    s.Etiqueta, s.NumeroCaja, s.Cantidad, s.Lote, s.EstadoCalidad,
    s.UbicacionID, s.ReferenciaOperacion, s.Observaciones
FROM #StockInicialPT s
INNER JOIN dbo.ERP_Partes p ON UPPER(p.NumeroParte) = UPPER(LTRIM(RTRIM(s.NumeroParte))) AND p.Activo = 1
ORDER BY s.FilaID;

IF @Confirmar = 0
BEGIN
    SELECT
        N'PREVISUALIZACIÓN' AS Estado,
        N'Los datos son válidos. Cambia @Confirmar a 1 para generar las entradas iniciales.' AS Mensaje;
    RETURN;
END;

BEGIN TRY
    BEGIN TRAN;

    INSERT dbo.AlmacenMP_Movimientos
    (
        FechaMovimiento, MaterialID, TipoMovimiento, Lote, Cantidad, Unidad,
        UbicacionID, NumeroOF, ResponsableUsuarioID, EntregadoPorNombre,
        Seguimiento, FechaCreacion, CreadoPor, Activo,
        RequiereValidacionProduccion, ValidadoProduccion, ReferenciaOperacion
    )
    SELECT
        SYSDATETIME(), m.MaterialID, N'Entrada', COALESCE(NULLIF(s.Lote,N''), N'S/L'),
        s.Cantidad, COALESCE(NULLIF(s.Unidad,N''), m.UnidadDefault), s.UbicacionID,
        NULL, NULL, @Usuario, COALESCE(NULLIF(s.Observaciones,N''), N'Carga inicial por conteo físico'),
        SYSUTCDATETIME(), @Usuario, 1, 0, 1, s.ReferenciaOperacion
    FROM #StockInicialMP s
    INNER JOIN dbo.ERP_Materiales m ON UPPER(m.Codigo) = UPPER(LTRIM(RTRIM(s.Codigo))) AND m.Activo = 1;

    CREATE TABLE #CajasInsertadas(FilaID INT NOT NULL PRIMARY KEY, CajaID INT NOT NULL);

    MERGE dbo.AlmacenPT_Cajas AS destino
    USING
    (
        SELECT s.FilaID, p.ParteID, s.Etiqueta, s.NumeroCaja, s.Cantidad,
               s.Lote, s.EstadoCalidad, s.UbicacionID
        FROM #StockInicialPT s
        INNER JOIN dbo.ERP_Partes p ON UPPER(p.NumeroParte) = UPPER(LTRIM(RTRIM(s.NumeroParte))) AND p.Activo = 1
    ) AS origen
    ON 1 = 0
    WHEN NOT MATCHED THEN
        INSERT
        (
            ParteID, NumeroOF, Etiqueta, NumeroCaja, CantidadInicial, LoteEtiqueta,
            EstadoCalidad, UbicacionID, FechaEntrada, FechaCreacion, CreadoPor, Activo
        )
        VALUES
        (
            origen.ParteID, NULL, origen.Etiqueta, origen.NumeroCaja, origen.Cantidad, origen.Lote,
            origen.EstadoCalidad, origen.UbicacionID, SYSDATETIME(), SYSUTCDATETIME(), @Usuario, 1
        )
    OUTPUT origen.FilaID, inserted.CajaID INTO #CajasInsertadas(FilaID, CajaID);

    INSERT dbo.AlmacenPT_Movimientos
    (
        CajaID, ParteID, NumeroOF, TipoMovimiento, Cantidad, UbicacionID,
        EstadoCalidad, ResponsableUsuarioID, Observaciones, FechaMovimiento,
        FechaCreacion, CreadoPor, Activo, ReferenciaOperacion
    )
    SELECT
        ci.CajaID, p.ParteID, NULL, N'Entrada', s.Cantidad, s.UbicacionID,
        s.EstadoCalidad, NULL, COALESCE(NULLIF(s.Observaciones,N''), N'Carga inicial por conteo físico'),
        SYSDATETIME(), SYSUTCDATETIME(), @Usuario, 1, s.ReferenciaOperacion
    FROM #StockInicialPT s
    INNER JOIN #CajasInsertadas ci ON ci.FilaID = s.FilaID
    INNER JOIN dbo.ERP_Partes p ON UPPER(p.NumeroParte) = UPPER(LTRIM(RTRIM(s.NumeroParte))) AND p.Activo = 1;

    INSERT dbo.AlmacenPT_Movimientos
    (
        CajaID, ParteID, NumeroOF, TipoMovimiento, Cantidad, UbicacionID,
        EstadoCalidad, ResponsableUsuarioID, Observaciones, FechaMovimiento,
        FechaCreacion, CreadoPor, Activo, ReferenciaOperacion
    )
    SELECT
        ci.CajaID, p.ParteID, NULL, N'Retencion', s.Cantidad, s.UbicacionID,
        s.EstadoCalidad, NULL,
        COALESCE(NULLIF(s.Observaciones,N''), N'Entrada inicial bloqueada por calidad: ' + s.EstadoCalidad),
        SYSDATETIME(), SYSUTCDATETIME(), @Usuario, 1,
        LEFT(s.ReferenciaOperacion, 116) + N'-RET'
    FROM #StockInicialPT s
    INNER JOIN #CajasInsertadas ci ON ci.FilaID = s.FilaID
    INNER JOIN dbo.ERP_Partes p ON UPPER(p.NumeroParte) = UPPER(LTRIM(RTRIM(s.NumeroParte))) AND p.Activo = 1
    WHERE s.EstadoCalidad <> N'Liberado';

    COMMIT;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    THROW;
END CATCH;

SELECT
    N'COMPLETADO' AS Estado,
    (SELECT COUNT(*) FROM #StockInicialMP) AS EntradasMP,
    (SELECT COUNT(*) FROM #StockInicialPT) AS CajasPT,
    N'La carga inicial fue registrada mediante movimientos auditables.' AS Mensaje;
GO
