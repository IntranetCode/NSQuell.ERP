/* ============================================================================
   71_CONFIGURAR_INDICADORES_SEMANAL_Y_MENU.sql

   Objetivo:
     1) Renombrar MenuGrupoID = 14 a "Indicadores de Gestión".
     2) Convertir su acceso actual en la tarjeta "General".
     3) Agregar accesos rápidos por área en /Menu/Grupo/14:
          General, Producción, Planeación, Calidad, GP12,
          Almacén, Logística y Compras.
     4) Cada tarjeta abre /Indicadores/Index?area=<area>.
     5) Conservar y clonar los mismos permisos del acceso actual del módulo.
     6) NO eliminar registros.

   El filtro semanal se implementa en código. Este SQL solo configura menú/nombre.

   MODO SEGURO:
     DECLARE @Aplicar BIT = 0; -- diagnóstico
     Cambiar a 1 únicamente después de revisar la salida.
   ============================================================================ */

USE [ERP_QUELL];
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @Aplicar BIT = 0;
DECLARE @GrupoID INT = 14;
DECLARE @MenuGeneralID INT = NULL;
DECLARE @SubMenuGeneralID INT = NULL;
DECLARE @Bloqueos INT = 0;
DECLARE @AccionesBase INT = 0;
DECLARE @PermisosBase INT = 0;
DECLARE @NombreGrupoActual NVARCHAR(100) = NULL;

IF OBJECT_ID(N'dbo.MenuGrupo', N'U') IS NULL SET @Bloqueos += 1;
IF OBJECT_ID(N'dbo.Menus', N'U') IS NULL SET @Bloqueos += 1;
IF OBJECT_ID(N'dbo.SubMenus', N'U') IS NULL SET @Bloqueos += 1;
IF OBJECT_ID(N'dbo.SubMenuAcciones', N'U') IS NULL SET @Bloqueos += 1;
IF OBJECT_ID(N'dbo.PermisosPorRol', N'U') IS NULL SET @Bloqueos += 1;

IF @Bloqueos = 0
BEGIN
    SELECT @NombreGrupoActual = g.Nombre
    FROM dbo.MenuGrupo g
    WHERE g.MenuGrupoID = @GrupoID;

    IF @NombreGrupoActual IS NULL
        SET @Bloqueos += 1;

    IF @NombreGrupoActual IS NOT NULL
       AND UPPER(LTRIM(RTRIM(@NombreGrupoActual))) NOT IN
       (
           N'DIRECCION', N'DIRECCIÓN',
           N'INDICADORES', N'INDICADORES DE GESTION', N'INDICADORES DE GESTIÓN'
       )
    BEGIN
        -- Protección: no reutilizar por accidente un grupo 14 que pertenezca a otro módulo.
        SET @Bloqueos += 1;
    END;

    SELECT TOP (1)
        @MenuGeneralID = m.MenuID,
        @SubMenuGeneralID = sm.SubMenuID
    FROM dbo.Menus m
    INNER JOIN dbo.SubMenus sm
        ON sm.MenuID = m.MenuID
    WHERE m.MenuGrupoID = @GrupoID
      AND ISNULL(m.Activo, 1) = 1
      AND ISNULL(sm.Activo, 1) = 1
      AND
      (
          sm.UrlEnlace IN
          (
              N'/Direccion', N'/Direccion/Index',
              N'/Indicadores', N'/Indicadores/Index',
              N'/Indicadores/Index?area=general'
          )
          OR UPPER(LTRIM(RTRIM(m.Nombre))) IN
          (
              N'DIRECCION', N'DIRECCIÓN', N'RESUMEN EJECUTIVO',
              N'INDICADORES', N'INDICADORES DE GESTION', N'INDICADORES DE GESTIÓN',
              N'GENERAL'
          )
      )
    ORDER BY
        CASE WHEN sm.UrlEnlace IN(N'/Indicadores/Index', N'/Indicadores/Index?area=general') THEN 0 ELSE 1 END,
        m.MenuID,
        sm.SubMenuID;

    IF @MenuGeneralID IS NULL OR @SubMenuGeneralID IS NULL
        SET @Bloqueos += 1;

    IF @SubMenuGeneralID IS NOT NULL
    BEGIN
        SELECT @AccionesBase = COUNT(*)
        FROM dbo.SubMenuAcciones sma
        WHERE sma.SubMenuID = @SubMenuGeneralID
          AND ISNULL(sma.Activo, 1) = 1;

        SELECT @PermisosBase = COUNT(*)
        FROM dbo.SubMenuAcciones sma
        INNER JOIN dbo.PermisosPorRol p
            ON p.SubMenuAccionID = sma.SubMenuAccionID
        WHERE sma.SubMenuID = @SubMenuGeneralID
          AND ISNULL(sma.Activo, 1) = 1
          AND ISNULL(p.Activo, 1) = 1;

        IF @AccionesBase = 0 OR @PermisosBase = 0
            SET @Bloqueos += 1;
    END;
END;

DECLARE @Areas TABLE
(
    Orden INT NOT NULL PRIMARY KEY,
    Clave NVARCHAR(30) NOT NULL,
    MenuNombre NVARCHAR(100) NOT NULL,
    SubMenuNombre NVARCHAR(100) NOT NULL,
    Url NVARCHAR(500) NOT NULL,
    Icono NVARCHAR(100) NOT NULL,
    Descripcion NVARCHAR(300) NOT NULL
);

INSERT @Areas(Orden, Clave, MenuNombre, SubMenuNombre, Url, Icono, Descripcion)
VALUES
(1, N'general',    N'General',    N'Resumen general',       N'/Indicadores/Index?area=general',    N'fa-solid fa-chart-pie',              N'Resumen transversal de los principales indicadores de la operación.'),
(2, N'produccion', N'Producción', N'Indicadores Producción',N'/Indicadores/Index?area=produccion', N'fa-solid fa-industry',               N'OEE, cumplimiento, scrap, operadores, máquinas y tendencia semanal.'),
(3, N'planeacion', N'Planeación', N'Indicadores Planeación',N'/Indicadores/Index?area=planeacion', N'fa-solid fa-calendar-check',         N'Programación, cumplimiento, pendientes, arranques y reprogramaciones.'),
(4, N'calidad',    N'Calidad',    N'Indicadores Calidad',   N'/Indicadores/Index?area=calidad',    N'fa-solid fa-shield-halved',          N'Liberación, cobertura, contenciones, scrap y tiempos de respuesta.'),
(5, N'gp12',       N'GP12',       N'Indicadores GP12',      N'/Indicadores/Index?area=gp12',       N'fa-solid fa-magnifying-glass-chart', N'Avance, conformidad, NOK, retrabajo, scrap y solicitudes pendientes.'),
(6, N'almacen',    N'Almacén',    N'Indicadores Almacén',   N'/Indicadores/Index?area=almacen',    N'fa-solid fa-warehouse',              N'Movimientos, inventarios críticos y seguimiento operativo de Scrap.'),
(7, N'logistica',  N'Logística',  N'Indicadores Logística', N'/Indicadores/Index?area=logistica',  N'fa-solid fa-truck-fast',             N'Embarques, entregas, puntualidad, atrasos e incidencias.'),
(8, N'compras',    N'Compras',    N'Indicadores Compras',   N'/Indicadores/Index?area=compras',    N'fa-solid fa-cart-shopping',          N'Solicitudes, prioridades, órdenes, recepción y antigüedad del flujo.');

SELECT
    N'DIAGNOSTICO_71' AS Seccion,
    @Aplicar AS Aplicar,
    @GrupoID AS MenuGrupoID,
    @NombreGrupoActual AS NombreGrupoActual,
    @MenuGeneralID AS MenuGeneralID,
    @SubMenuGeneralID AS SubMenuGeneralID,
    @AccionesBase AS AccionesBase,
    @PermisosBase AS PermisosBase,
    @Bloqueos AS Bloqueos,
    CASE WHEN @Bloqueos = 0 THEN N'LISTO_PARA_APLICAR' ELSE N'REVISAR' END AS Resultado;

IF OBJECT_ID(N'dbo.MenuGrupo', N'U') IS NOT NULL
BEGIN
    SELECT
        N'GRUPO_14_ACTUAL' AS Seccion,
        g.MenuGrupoID,
        g.Nombre,
        g.Descripcion,
        g.IconoCss,
        g.Orden,
        g.Activo
    FROM dbo.MenuGrupo g
    WHERE g.MenuGrupoID = @GrupoID;
END;

IF OBJECT_ID(N'dbo.Menus', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.SubMenus', N'U') IS NOT NULL
BEGIN
    SELECT
        N'ACCESOS_ACTUALES_GRUPO_14' AS Seccion,
        m.MenuID,
        m.Nombre AS Menu,
        m.Orden,
        m.Activo AS MenuActivo,
        sm.SubMenuID,
        sm.Nombre AS SubMenu,
        sm.UrlEnlace,
        sm.Activo AS SubMenuActivo
    FROM dbo.Menus m
    LEFT JOIN dbo.SubMenus sm
        ON sm.MenuID = m.MenuID
    WHERE m.MenuGrupoID = @GrupoID
    ORDER BY m.Orden, m.MenuID, sm.SubMenuID;
END;

SELECT
    N'ACCESOS_PROPUESTOS' AS Seccion,
    Orden,
    MenuNombre,
    SubMenuNombre,
    Url,
    Icono,
    Descripcion
FROM @Areas
ORDER BY Orden;

IF @Aplicar = 0
BEGIN
    PRINT N'MODO DIAGNOSTICO: no se realizaron cambios.';
    RETURN;
END;

IF @Bloqueos <> 0
    THROW 57101, N'No se puede aplicar: revisar DIAGNOSTICO_71 antes de continuar.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    UPDATE dbo.MenuGrupo
    SET Nombre = N'Indicadores de Gestión',
        Descripcion = N'Indicadores generales y por área para seguimiento semanal de la operación.',
        IconoCss = N'fa-solid fa-chart-line',
        Activo = 1
    WHERE MenuGrupoID = @GrupoID;

    -- El acceso existente conserva sus IDs y permisos; pasa a ser la tarjeta General.
    UPDATE dbo.Menus
    SET Nombre = N'General',
        Descripcion = N'Resumen transversal de los principales indicadores de la operación.',
        IconoCss = N'fa-solid fa-chart-pie',
        Orden = 1,
        Activo = 1
    WHERE MenuID = @MenuGeneralID;

    UPDATE dbo.SubMenus
    SET Nombre = N'Resumen general',
        UrlEnlace = N'/Indicadores/Index?area=general',
        Descripcion = N'Resumen general semanal de Indicadores de Gestión.',
        IconoCSS = N'fa-solid fa-chart-pie',
        Activo = 1,
        FechaModificacion = GETDATE()
    WHERE SubMenuID = @SubMenuGeneralID;

    DECLARE @Orden INT = 2;
    DECLARE @MenuID INT;
    DECLARE @SubMenuID INT;
    DECLARE @MenuNombre NVARCHAR(100);
    DECLARE @SubMenuNombre NVARCHAR(100);
    DECLARE @Url NVARCHAR(500);
    DECLARE @Icono NVARCHAR(100);
    DECLARE @Descripcion NVARCHAR(300);

    WHILE @Orden <= 8
    BEGIN
        SELECT
            @MenuNombre = a.MenuNombre,
            @SubMenuNombre = a.SubMenuNombre,
            @Url = a.Url,
            @Icono = a.Icono,
            @Descripcion = a.Descripcion
        FROM @Areas a
        WHERE a.Orden = @Orden;

        SET @MenuID = NULL;
        SET @SubMenuID = NULL;

        SELECT TOP (1) @MenuID = m.MenuID
        FROM dbo.Menus m
        WHERE m.MenuGrupoID = @GrupoID
          AND UPPER(LTRIM(RTRIM(m.Nombre))) = UPPER(LTRIM(RTRIM(@MenuNombre)))
        ORDER BY m.MenuID;

        IF @MenuID IS NULL
        BEGIN
            INSERT dbo.Menus(MenuGrupoID, Nombre, Activo, IconoCss, Orden, Descripcion)
            VALUES(@GrupoID, @MenuNombre, 1, @Icono, @Orden, @Descripcion);

            SET @MenuID = CONVERT(INT, SCOPE_IDENTITY());
        END
        ELSE
        BEGIN
            UPDATE dbo.Menus
            SET Nombre = @MenuNombre,
                Activo = 1,
                IconoCss = @Icono,
                Orden = @Orden,
                Descripcion = @Descripcion
            WHERE MenuID = @MenuID;
        END;

        SELECT TOP (1) @SubMenuID = sm.SubMenuID
        FROM dbo.SubMenus sm
        WHERE sm.MenuID = @MenuID
          AND
          (
              sm.UrlEnlace = @Url
              OR UPPER(LTRIM(RTRIM(sm.Nombre))) = UPPER(LTRIM(RTRIM(@SubMenuNombre)))
          )
        ORDER BY CASE WHEN sm.UrlEnlace = @Url THEN 0 ELSE 1 END, sm.SubMenuID;

        IF @SubMenuID IS NULL
        BEGIN
            INSERT dbo.SubMenus
            (
                MenuID, Nombre, UrlEnlace, Descripcion,
                Activo, FechaCreacion, FechaModificacion, IconoCSS
            )
            VALUES
            (
                @MenuID, @SubMenuNombre, @Url, @Descripcion,
                1, GETDATE(), NULL, @Icono
            );

            SET @SubMenuID = CONVERT(INT, SCOPE_IDENTITY());
        END
        ELSE
        BEGIN
            UPDATE dbo.SubMenus
            SET Nombre = @SubMenuNombre,
                UrlEnlace = @Url,
                Descripcion = @Descripcion,
                Activo = 1,
                FechaModificacion = GETDATE(),
                IconoCSS = @Icono
            WHERE SubMenuID = @SubMenuID;
        END;

        -- Replicar acciones del acceso General.
        INSERT dbo.SubMenuAcciones(SubMenuID, AccionID, Activo, FechaCreacion, FechaModificacion)
        SELECT
            @SubMenuID,
            src.AccionID,
            1,
            GETDATE(),
            NULL
        FROM dbo.SubMenuAcciones src
        WHERE src.SubMenuID = @SubMenuGeneralID
          AND ISNULL(src.Activo, 1) = 1
          AND NOT EXISTS
          (
              SELECT 1
              FROM dbo.SubMenuAcciones dst
              WHERE dst.SubMenuID = @SubMenuID
                AND dst.AccionID = src.AccionID
          );

        UPDATE dst
        SET dst.Activo = 1,
            dst.FechaModificacion = GETDATE()
        FROM dbo.SubMenuAcciones dst
        INNER JOIN dbo.SubMenuAcciones src
            ON src.SubMenuID = @SubMenuGeneralID
           AND src.AccionID = dst.AccionID
           AND ISNULL(src.Activo, 1) = 1
        WHERE dst.SubMenuID = @SubMenuID
          AND ISNULL(dst.Activo, 1) = 0;

        -- Replicar exactamente los permisos activos del acceso General.
        INSERT dbo.PermisosPorRol(RolID, SubMenuAccionID, Activo, FechaCreacion, FechaModificacion)
        SELECT DISTINCT
            p.RolID,
            dst.SubMenuAccionID,
            1,
            GETDATE(),
            NULL
        FROM dbo.SubMenuAcciones src
        INNER JOIN dbo.PermisosPorRol p
            ON p.SubMenuAccionID = src.SubMenuAccionID
           AND ISNULL(p.Activo, 1) = 1
        INNER JOIN dbo.SubMenuAcciones dst
            ON dst.SubMenuID = @SubMenuID
           AND dst.AccionID = src.AccionID
           AND ISNULL(dst.Activo, 1) = 1
        WHERE src.SubMenuID = @SubMenuGeneralID
          AND ISNULL(src.Activo, 1) = 1
          AND NOT EXISTS
          (
              SELECT 1
              FROM dbo.PermisosPorRol px
              WHERE px.RolID = p.RolID
                AND px.SubMenuAccionID = dst.SubMenuAccionID
          );

        UPDATE px
        SET px.Activo = 1,
            px.FechaModificacion = GETDATE()
        FROM dbo.PermisosPorRol px
        INNER JOIN dbo.SubMenuAcciones dst
            ON dst.SubMenuAccionID = px.SubMenuAccionID
           AND dst.SubMenuID = @SubMenuID
        INNER JOIN dbo.SubMenuAcciones src
            ON src.SubMenuID = @SubMenuGeneralID
           AND src.AccionID = dst.AccionID
           AND ISNULL(src.Activo, 1) = 1
        INNER JOIN dbo.PermisosPorRol pbase
            ON pbase.SubMenuAccionID = src.SubMenuAccionID
           AND pbase.RolID = px.RolID
           AND ISNULL(pbase.Activo, 1) = 1
        WHERE ISNULL(px.Activo, 1) = 0;

        SET @Orden += 1;
    END;

    COMMIT TRANSACTION;

    SELECT
        N'POST_71_GRUPO' AS Seccion,
        g.MenuGrupoID,
        g.Nombre,
        g.Descripcion,
        g.IconoCss,
        g.Orden,
        g.Activo
    FROM dbo.MenuGrupo g
    WHERE g.MenuGrupoID = @GrupoID;

    SELECT
        N'POST_71_ACCESOS' AS Seccion,
        m.MenuID,
        m.Nombre AS Menu,
        m.Orden,
        m.IconoCss,
        m.Activo AS MenuActivo,
        sm.SubMenuID,
        sm.Nombre AS SubMenu,
        sm.UrlEnlace,
        sm.Activo AS SubMenuActivo,
        COUNT(DISTINCT CASE WHEN ISNULL(sma.Activo,1)=1 THEN sma.SubMenuAccionID END) AS AccionesActivas,
        COUNT(DISTINCT CASE WHEN ISNULL(p.Activo,1)=1 THEN p.PermisoRolID END) AS PermisosActivos
    FROM dbo.Menus m
    INNER JOIN dbo.SubMenus sm
        ON sm.MenuID = m.MenuID
    LEFT JOIN dbo.SubMenuAcciones sma
        ON sma.SubMenuID = sm.SubMenuID
    LEFT JOIN dbo.PermisosPorRol p
        ON p.SubMenuAccionID = sma.SubMenuAccionID
    WHERE m.MenuGrupoID = @GrupoID
    GROUP BY
        m.MenuID, m.Nombre, m.Orden, m.IconoCss, m.Activo,
        sm.SubMenuID, sm.Nombre, sm.UrlEnlace, sm.Activo
    ORDER BY m.Orden, m.MenuID, sm.SubMenuID;

    SELECT N'RESULTADO_71' AS Seccion, N'OK' AS Resultado;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
