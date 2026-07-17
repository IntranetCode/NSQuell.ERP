USE [ERP_QUELL];
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

/*
    Registrar "ORDENES DE FABRICACION (OF)" dentro de Menu/Grupo/1.

    - Crea el Menu.
    - Crea el SubMenu que abre /AlmacenOF/Index.
    - Copia acciones y permisos desde el acceso principal de MP.
    - Es idempotente.
*/

IF DB_NAME() <> N'ERP_QUELL'
    THROW 51700, 'Ejecuta este script dentro de ERP_QUELL.', 1;

IF OBJECT_ID(N'dbo.Menus', N'U') IS NULL
    THROW 51701, 'No existe dbo.Menus.', 1;

IF OBJECT_ID(N'dbo.SubMenus', N'U') IS NULL
    THROW 51702, 'No existe dbo.SubMenus.', 1;
GO

BEGIN TRY
    BEGIN TRAN;

    DECLARE @MenuGrupoID INT = 1;
    DECLARE @MenuOFID INT =
    (
        SELECT TOP (1) m.MenuID
        FROM dbo.Menus m
        WHERE m.MenuGrupoID = @MenuGrupoID
          AND
          (
              UPPER(LTRIM(RTRIM(m.Nombre))) IN
              (
                  N'OF',
                  N'ORDENES DE FABRICACION (OF)',
                  N'ÓRDENES DE FABRICACIÓN (OF)',
                  N'ORDENES DE FABRICACION',
                  N'ÓRDENES DE FABRICACIÓN'
              )
              OR EXISTS
              (
                  SELECT 1
                  FROM dbo.SubMenus sm
                  WHERE sm.MenuID = m.MenuID
                    AND sm.UrlEnlace = N'/AlmacenOF/Index'
              )
          )
        ORDER BY m.MenuID
    );

    IF @MenuOFID IS NULL
    BEGIN
        INSERT dbo.Menus
        (
            MenuGrupoID,
            Nombre,
            Descripcion,
            IconoCss,
            Orden,
            Activo
        )
        VALUES
        (
            @MenuGrupoID,
            N'OF',
            N'Órdenes de fabricación: consulta y entrega controlada de MP y embalajes.',
            N'fa-solid fa-clipboard-list',
            ISNULL
            (
                (
                    SELECT MAX(ISNULL(Orden, 0)) + 1
                    FROM dbo.Menus
                    WHERE MenuGrupoID = @MenuGrupoID
                ),
                1
            ),
            1
        );

        SET @MenuOFID = CONVERT(INT, SCOPE_IDENTITY());
    END
    ELSE
    BEGIN
        UPDATE dbo.Menus
        SET
            Nombre = N'OF',
            Descripcion = N'Órdenes de fabricación: consulta y entrega controlada de MP y embalajes.',
            IconoCss = N'fa-solid fa-clipboard-list',
            Activo = 1
        WHERE MenuID = @MenuOFID;
    END;

    DECLARE @SubMenuOFID INT =
    (
        SELECT TOP (1) SubMenuID
        FROM dbo.SubMenus
        WHERE MenuID = @MenuOFID
          AND UrlEnlace = N'/AlmacenOF/Index'
        ORDER BY SubMenuID
    );

    IF @SubMenuOFID IS NULL
    BEGIN
        DECLARE @SqlSubMenu NVARCHAR(MAX) =
            N'INSERT dbo.SubMenus(MenuID, Nombre, UrlEnlace, Activo';

        IF COL_LENGTH(N'dbo.SubMenus', N'Descripcion') IS NOT NULL
            SET @SqlSubMenu += N', Descripcion';

        IF COL_LENGTH(N'dbo.SubMenus', N'IconoCss') IS NOT NULL
            SET @SqlSubMenu += N', IconoCss';

        IF COL_LENGTH(N'dbo.SubMenus', N'Orden') IS NOT NULL
            SET @SqlSubMenu += N', Orden';

        SET @SqlSubMenu += N') VALUES(@MenuID, @Nombre, @Url, 1';

        IF COL_LENGTH(N'dbo.SubMenus', N'Descripcion') IS NOT NULL
            SET @SqlSubMenu += N', @Descripcion';

        IF COL_LENGTH(N'dbo.SubMenus', N'IconoCss') IS NOT NULL
            SET @SqlSubMenu += N', @Icono';

        IF COL_LENGTH(N'dbo.SubMenus', N'Orden') IS NOT NULL
            SET @SqlSubMenu += N', 1';

        SET @SqlSubMenu +=
            N'); SET @NuevoID = CONVERT(INT, SCOPE_IDENTITY());';

        EXEC sys.sp_executesql
            @SqlSubMenu,
            N'@MenuID INT,
              @Nombre NVARCHAR(200),
              @Url NVARCHAR(500),
              @Descripcion NVARCHAR(500),
              @Icono NVARCHAR(200),
              @NuevoID INT OUTPUT',
            @MenuID = @MenuOFID,
            @Nombre = N'Ver órdenes de fabricación',
            @Url = N'/AlmacenOF/Index',
            @Descripcion = N'Bandeja de OF para consulta operativa de Almacén.',
            @Icono = N'fa-solid fa-clipboard-list',
            @NuevoID = @SubMenuOFID OUTPUT;
    END
    ELSE
    BEGIN
        UPDATE dbo.SubMenus
        SET
            Nombre = N'Ver órdenes de fabricación',
            UrlEnlace = N'/AlmacenOF/Index',
            Activo = 1
        WHERE SubMenuID = @SubMenuOFID;
    END;

    DECLARE @SubMenuMPID INT =
    (
        SELECT TOP (1) sm.SubMenuID
        FROM dbo.SubMenus sm
        INNER JOIN dbo.Menus m
            ON m.MenuID = sm.MenuID
        WHERE m.MenuGrupoID = @MenuGrupoID
          AND sm.Activo = 1
          AND sm.UrlEnlace IN
          (
              N'/AlmacenMP/Index',
              N'/AlmacenMP'
          )
        ORDER BY
            CASE WHEN sm.UrlEnlace = N'/AlmacenMP/Index' THEN 0 ELSE 1 END,
            sm.SubMenuID
    );

    IF @SubMenuMPID IS NOT NULL
       AND OBJECT_ID(N'dbo.SubMenuAcciones', N'U') IS NOT NULL
    BEGIN
        INSERT dbo.SubMenuAcciones(SubMenuID, AccionID)
        SELECT
            @SubMenuOFID,
            origen.AccionID
        FROM dbo.SubMenuAcciones origen
        WHERE origen.SubMenuID = @SubMenuMPID
          AND NOT EXISTS
          (
              SELECT 1
              FROM dbo.SubMenuAcciones destino
              WHERE destino.SubMenuID = @SubMenuOFID
                AND destino.AccionID = origen.AccionID
          );

        IF OBJECT_ID(N'dbo.PermisosPorRol', N'U') IS NOT NULL
        BEGIN
            IF COL_LENGTH(N'dbo.PermisosPorRol', N'EmpresaID') IS NULL
            BEGIN
                INSERT dbo.PermisosPorRol
                (
                    RolID,
                    SubMenuAccionID,
                    Activo
                )
                SELECT DISTINCT
                    permiso.RolID,
                    destino.SubMenuAccionID,
                    1
                FROM dbo.SubMenuAcciones origen
                INNER JOIN dbo.SubMenuAcciones destino
                    ON destino.SubMenuID = @SubMenuOFID
                   AND destino.AccionID = origen.AccionID
                INNER JOIN dbo.PermisosPorRol permiso
                    ON permiso.SubMenuAccionID = origen.SubMenuAccionID
                   AND permiso.Activo = 1
                WHERE origen.SubMenuID = @SubMenuMPID
                  AND NOT EXISTS
                  (
                      SELECT 1
                      FROM dbo.PermisosPorRol actual
                      WHERE actual.RolID = permiso.RolID
                        AND actual.SubMenuAccionID = destino.SubMenuAccionID
                        AND actual.Activo = 1
                  );
            END
            ELSE
            BEGIN
                EXEC sys.sp_executesql
                N'
                INSERT dbo.PermisosPorRol
                (
                    RolID,
                    EmpresaID,
                    SubMenuAccionID,
                    Activo
                )
                SELECT DISTINCT
                    permiso.RolID,
                    permiso.EmpresaID,
                    destino.SubMenuAccionID,
                    1
                FROM dbo.SubMenuAcciones origen
                INNER JOIN dbo.SubMenuAcciones destino
                    ON destino.SubMenuID = @SubMenuOFID
                   AND destino.AccionID = origen.AccionID
                INNER JOIN dbo.PermisosPorRol permiso
                    ON permiso.SubMenuAccionID = origen.SubMenuAccionID
                   AND permiso.Activo = 1
                WHERE origen.SubMenuID = @SubMenuMPID
                  AND NOT EXISTS
                  (
                      SELECT 1
                      FROM dbo.PermisosPorRol actual
                      WHERE actual.RolID = permiso.RolID
                        AND ISNULL(actual.EmpresaID, -1) =
                            ISNULL(permiso.EmpresaID, -1)
                        AND actual.SubMenuAccionID =
                            destino.SubMenuAccionID
                        AND actual.Activo = 1
                  );',
                N'@SubMenuOFID INT, @SubMenuMPID INT',
                @SubMenuOFID = @SubMenuOFID,
                @SubMenuMPID = @SubMenuMPID;
            END;
        END;
    END;

    COMMIT;

    SELECT
        m.MenuID,
        m.MenuGrupoID,
        m.Nombre,
        m.Descripcion,
        m.IconoCss,
        m.Orden,
        m.Activo,
        sm.SubMenuID,
        sm.Nombre AS SubMenu,
        sm.UrlEnlace,
        sm.Activo AS SubMenuActivo
    FROM dbo.Menus m
    INNER JOIN dbo.SubMenus sm
        ON sm.MenuID = m.MenuID
    WHERE m.MenuID = @MenuOFID;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK;

    THROW;
END CATCH;
GO


