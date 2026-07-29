USE [ERP_QUELL];
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

/*
    Habilita y optimiza la entrega de Producto Terminado desde la
    bandeja de Órdenes de Fabricación de Almacén.

    La OF se considera validada para PT cuando:
    - SolicitudesProduccionDetalle.ParteID tiene valor.
    - Ese ParteID existe y está activo en ERP_Partes.
    - CantidadPiezas es mayor que cero.

    Este script es idempotente.
*/

IF DB_NAME() <> N'ERP_QUELL'
    THROW 52000, 'Ejecuta este script dentro de ERP_QUELL.', 1;
GO

IF OBJECT_ID(N'dbo.SolicitudesProduccionDetalle', N'U') IS NULL
    THROW 52001, 'No existe dbo.SolicitudesProduccionDetalle.', 1;

IF OBJECT_ID(N'dbo.ERP_Partes', N'U') IS NULL
    THROW 52002, 'No existe dbo.ERP_Partes.', 1;

IF OBJECT_ID(N'dbo.AlmacenPT_Movimientos', N'U') IS NULL
    THROW 52003, 'No existe dbo.AlmacenPT_Movimientos.', 1;
GO

BEGIN TRY
    BEGIN TRAN;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id =
              OBJECT_ID(N'dbo.SolicitudesProduccionDetalle')
          AND name =
              N'IX_SolicitudesProduccionDetalle_OF_Parte'
    )
    BEGIN
        CREATE INDEX
            IX_SolicitudesProduccionDetalle_OF_Parte
        ON dbo.SolicitudesProduccionDetalle
        (
            SolicitudProduccionID,
            Activo,
            ParteID
        )
        INCLUDE
        (
            CantidadPiezas,
            Renglon,
            ReferenciaSAP,
            DesignacionDescripcionSAP
        );
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id =
              OBJECT_ID(N'dbo.AlmacenPT_Movimientos')
          AND name =
              N'IX_AlmacenPT_OF_Parte_Activo_Tipo'
    )
    BEGIN
        CREATE INDEX
            IX_AlmacenPT_OF_Parte_Activo_Tipo
        ON dbo.AlmacenPT_Movimientos
        (
            NumeroOF,
            ParteID,
            Activo,
            TipoMovimiento
        )
        INCLUDE
        (
            Cantidad,
            FechaMovimiento,
            ReferenciaOperacion
        );
    END;

    UPDATE menuOF
    SET menuOF.Descripcion =
        N'Órdenes de fabricación: entrega controlada de MP, embalajes y producto terminado.'
    FROM dbo.Menus menuOF
    WHERE EXISTS
    (
        SELECT 1
        FROM dbo.SubMenus submenuOF
        WHERE submenuOF.MenuID = menuOF.MenuID
          AND submenuOF.UrlEnlace =
              N'/AlmacenOF/Index'
    );

    COMMIT;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK;

    THROW;
END CATCH;
GO

SELECT
    d.SolicitudProduccionID,
    d.ParteID,
    p.NumeroParte,
    p.Descripcion,
    SUM(ISNULL(d.CantidadPiezas, 0)) AS PiezasPlaneadas
FROM dbo.SolicitudesProduccionDetalle d
INNER JOIN dbo.ERP_Partes p
    ON p.ParteID = d.ParteID
   AND p.Activo = 1
WHERE d.Activo = 1
  AND d.ParteID IS NOT NULL
  AND ISNULL(d.CantidadPiezas, 0) > 0
GROUP BY
    d.SolicitudProduccionID,
    d.ParteID,
    p.NumeroParte,
    p.Descripcion
ORDER BY
    d.SolicitudProduccionID DESC,
    p.NumeroParte;
GO

