USE [ERP_QUELL];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

/*
    Normaliza referencias de auditoría para movimientos históricos.
    No cambia cantidades, tipos de movimiento, lotes, cajas ni existencias.
*/

DECLARE @Confirmar BIT = 0;

IF OBJECT_ID(N'dbo.AlmacenMP_Movimientos',N'U') IS NULL
    THROW 51000, 'No existe dbo.AlmacenMP_Movimientos.', 1;
IF OBJECT_ID(N'dbo.AlmacenPT_Movimientos',N'U') IS NULL
    THROW 51001, 'No existe dbo.AlmacenPT_Movimientos.', 1;
IF COL_LENGTH(N'dbo.AlmacenMP_Movimientos',N'ReferenciaOperacion') IS NULL
    THROW 51002, 'Falta ReferenciaOperacion en MP. Ejecuta el script 04 corregido.', 1;
IF COL_LENGTH(N'dbo.AlmacenPT_Movimientos',N'ReferenciaOperacion') IS NULL
    THROW 51003, 'Falta ReferenciaOperacion en PT. Ejecuta el script 04 corregido.', 1;

DECLARE @PendientesMP INT =
(
    SELECT COUNT(*)
    FROM dbo.AlmacenMP_Movimientos
    WHERE NULLIF(LTRIM(RTRIM(ReferenciaOperacion)),N'') IS NULL
);
DECLARE @PendientesPT INT =
(
    SELECT COUNT(*)
    FROM dbo.AlmacenPT_Movimientos
    WHERE NULLIF(LTRIM(RTRIM(ReferenciaOperacion)),N'') IS NULL
);

SELECT N'MP' AS Modulo, @PendientesMP AS MovimientosSinReferencia
UNION ALL
SELECT N'PT', @PendientesPT;

IF @PendientesMP = 0 AND @PendientesPT = 0
BEGIN
    SELECT N'SIN CAMBIOS' AS Estado, N'Todos los movimientos ya tienen referencia de auditoría.' AS Mensaje;
    RETURN;
END;

IF EXISTS
(
    SELECT 1
    FROM dbo.AlmacenMP_Movimientos pendiente
    INNER JOIN dbo.AlmacenMP_Movimientos existente
        ON existente.ReferenciaOperacion = N'LEG-MP-' + CONVERT(NVARCHAR(30), pendiente.MovimientoID)
       AND existente.MovimientoID <> pendiente.MovimientoID
    WHERE NULLIF(LTRIM(RTRIM(pendiente.ReferenciaOperacion)),N'') IS NULL
)
    THROW 51010, 'Existe una colisión con referencias LEG-MP. No se realizaron cambios.', 1;

IF EXISTS
(
    SELECT 1
    FROM dbo.AlmacenPT_Movimientos pendiente
    INNER JOIN dbo.AlmacenPT_Movimientos existente
        ON existente.ReferenciaOperacion = N'LEG-PT-' + CONVERT(NVARCHAR(30), pendiente.MovimientoID)
       AND existente.MovimientoID <> pendiente.MovimientoID
    WHERE NULLIF(LTRIM(RTRIM(pendiente.ReferenciaOperacion)),N'') IS NULL
)
    THROW 51011, 'Existe una colisión con referencias LEG-PT. No se realizaron cambios.', 1;

IF @Confirmar = 0
BEGIN
    SELECT
        N'PREVISUALIZACIÓN' AS Estado,
        N'Cambia @Confirmar a 1 para asignar referencias determinísticas a los movimientos históricos.' AS Mensaje;
    RETURN;
END;

BEGIN TRY
    BEGIN TRAN;

    UPDATE dbo.AlmacenMP_Movimientos
    SET ReferenciaOperacion = N'LEG-MP-' + CONVERT(NVARCHAR(30), MovimientoID),
        FechaModificacion = COALESCE(FechaModificacion, SYSUTCDATETIME()),
        ActualizadoPor = COALESCE(NULLIF(ActualizadoPor,N''), N'normalizacion-auditoria')
    WHERE NULLIF(LTRIM(RTRIM(ReferenciaOperacion)),N'') IS NULL;

    UPDATE dbo.AlmacenPT_Movimientos
    SET ReferenciaOperacion = N'LEG-PT-' + CONVERT(NVARCHAR(30), MovimientoID),
        FechaModificacion = COALESCE(FechaModificacion, SYSUTCDATETIME()),
        ActualizadoPor = COALESCE(NULLIF(ActualizadoPor,N''), N'normalizacion-auditoria')
    WHERE NULLIF(LTRIM(RTRIM(ReferenciaOperacion)),N'') IS NULL;

    COMMIT;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    THROW;
END CATCH;

SELECT
    N'COMPLETADO' AS Estado,
    @PendientesMP AS ReferenciasMPAsignadas,
    @PendientesPT AS ReferenciasPTAsignadas,
    N'No se modificaron cantidades ni existencias.' AS Mensaje;
GO
