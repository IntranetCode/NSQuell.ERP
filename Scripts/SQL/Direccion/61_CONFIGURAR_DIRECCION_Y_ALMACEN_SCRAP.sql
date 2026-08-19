USE [ERP_QUELL];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

/*
    NSQuell.ERP
    61_CONFIGURAR_DIRECCION_Y_ALMACEN_SCRAP.sql

    Objetivo:
    1) Validar el flujo directo Calidad -> Almacen Scrap usando dbo.Calidad_ScrapEntregas.
    2) Apuntar el acceso Scrap de Almacen a /Almacen/Scrap.
    3) Crear un modulo principal independiente "Direccion" con acceso de solo lectura.

    Seguridad:
    - @Aplicar=0: diagnostico, NO modifica datos ni estructura.
    - @Aplicar=1: cambios idempotentes dentro de una transaccion.
    - NO crea tablas BK.
    - NO modifica CalidadController.Flujo ni datos de Calidad.
*/

DECLARE @Aplicar BIT = 0;

IF DB_NAME() <> N'ERP_QUELL'
    THROW 56100, N'Ejecuta este script en ERP_QUELL.', 1;

DECLARE @Bloqueos INT = 0;

DROP TABLE IF EXISTS #ObjetosRequeridos;
CREATE TABLE #ObjetosRequeridos
(
    Objeto SYSNAME NOT NULL,
    Tipo CHAR(2) NOT NULL
);

INSERT #ObjetosRequeridos(Objeto,Tipo)
VALUES
(N'dbo.Calidad_ScrapEntregas','U'),
(N'dbo.AlmacenMP_Movimientos','U'),
(N'dbo.ERP_ParteDatosTecnicos','U'),
(N'dbo.ERP_Materiales','U'),
(N'dbo.ERP_Partes','U'),
(N'dbo.ERP_Ubicaciones','U'),
(N'dbo.Produccion_RegistroHora','U'),
(N'dbo.Produccion_Paros','U'),
(N'dbo.Produccion_Ejecucion','U'),
(N'dbo.Persona','U'),
(N'dbo.Usuarios','U'),
(N'dbo.Departamentos','U'),
(N'dbo.ERP_Maquinas','U'),
(N'dbo.Planeacion_ProgramaProduccion','U'),
(N'dbo.Calidad_Inspecciones','U'),
(N'dbo.GP12_Solicitudes','U'),
(N'dbo.GP12_Inspecciones','U'),
(N'dbo.AlmacenPT_Movimientos','U'),
(N'dbo.Logistica_Embarques','U'),
(N'dbo.ComprasSolicitudes','U'),
(N'dbo.ComprasOrdenes','U'),
(N'dbo.MenuGrupo','U'),
(N'dbo.Menus','U'),
(N'dbo.SubMenus','U'),
(N'dbo.Acciones','U'),
(N'dbo.SubMenuAcciones','U'),
(N'dbo.PermisosPorRol','U'),
(N'dbo.Roles','U');

SELECT
    N'A_OBJETOS_REQUERIDOS' AS Seccion,
    r.Objeto,
    r.Tipo,
    CASE WHEN OBJECT_ID(r.Objeto,r.Tipo) IS NULL THEN 0 ELSE 1 END AS Existe
FROM #ObjetosRequeridos r
ORDER BY r.Objeto;

SELECT @Bloqueos += COUNT(*)
FROM #ObjetosRequeridos
WHERE OBJECT_ID(Objeto,Tipo) IS NULL;

DROP TABLE IF EXISTS #ColumnasRequeridas;
CREATE TABLE #ColumnasRequeridas
(
    Objeto SYSNAME NOT NULL,
    Columna SYSNAME NOT NULL
);

INSERT #ColumnasRequeridas(Objeto,Columna)
VALUES
-- Fuente directa Calidad -> Almacen Scrap
(N'dbo.Calidad_ScrapEntregas',N'ScrapEntregaID'),
(N'dbo.Calidad_ScrapEntregas',N'InspeccionID'),
(N'dbo.Calidad_ScrapEntregas',N'DisposicionID'),
(N'dbo.Calidad_ScrapEntregas',N'EjecucionProduccionID'),
(N'dbo.Calidad_ScrapEntregas',N'SolicitudProduccionID'),
(N'dbo.Calidad_ScrapEntregas',N'SolicitudProduccionDetalleID'),
(N'dbo.Calidad_ScrapEntregas',N'ParteID'),
(N'dbo.Calidad_ScrapEntregas',N'NumeroParte'),
(N'dbo.Calidad_ScrapEntregas',N'OrdenFabricacion'),
(N'dbo.Calidad_ScrapEntregas',N'CantidadScrap'),
(N'dbo.Calidad_ScrapEntregas',N'Estado'),
(N'dbo.Calidad_ScrapEntregas',N'UsuarioEntregaID'),
(N'dbo.Calidad_ScrapEntregas',N'FechaEntrega'),
(N'dbo.Calidad_ScrapEntregas',N'UsuarioRecepcionID'),
(N'dbo.Calidad_ScrapEntregas',N'FechaRecepcion'),
(N'dbo.Calidad_ScrapEntregas',N'UbicacionScrap'),
(N'dbo.Calidad_ScrapEntregas',N'UsuarioMoliendaID'),
(N'dbo.Calidad_ScrapEntregas',N'FechaMolienda'),
(N'dbo.Calidad_ScrapEntregas',N'CantidadMolida'),
(N'dbo.Calidad_ScrapEntregas',N'Observaciones'),
(N'dbo.Calidad_ScrapEntregas',N'UsuarioCreacionID'),
(N'dbo.Calidad_ScrapEntregas',N'UsuarioModificacionID'),
(N'dbo.Calidad_ScrapEntregas',N'FechaModificacion'),
(N'dbo.Calidad_ScrapEntregas',N'FechaCreacion'),
(N'dbo.Calidad_ScrapEntregas',N'Origen'),
(N'dbo.Calidad_ScrapEntregas',N'GP12SolicitudID'),
(N'dbo.Calidad_ScrapEntregas',N'GP12InspeccionID'),
(N'dbo.Calidad_ScrapEntregas',N'Activo'),
-- Usuarios / ubicaciones que usa la bandeja
(N'dbo.Usuarios',N'UsuarioID'),(N'dbo.Usuarios',N'PersonaID'),(N'dbo.Usuarios',N'RolID'),(N'dbo.Usuarios',N'DepartamentoID'),(N'dbo.Usuarios',N'Activo'),
(N'dbo.Departamentos',N'DepartamentoID'),(N'dbo.Departamentos',N'NombreDepartamento'),
(N'dbo.Persona',N'PersonaID'),(N'dbo.Persona',N'Nombre'),(N'dbo.Persona',N'ApellidoPaterno'),(N'dbo.Persona',N'ApellidoMaterno'),(N'dbo.Persona',N'NumeroControl'),
(N'dbo.ERP_Ubicaciones',N'UbicacionID'),(N'dbo.ERP_Ubicaciones',N'Almacen'),(N'dbo.ERP_Ubicaciones',N'Rack'),(N'dbo.ERP_Ubicaciones',N'Nivel'),(N'dbo.ERP_Ubicaciones',N'Posicion'),(N'dbo.ERP_Ubicaciones',N'Activo'),
-- Alta real de MP Molido
(N'dbo.AlmacenMP_Movimientos',N'MovimientoID'),(N'dbo.AlmacenMP_Movimientos',N'FechaMovimiento'),(N'dbo.AlmacenMP_Movimientos',N'MaterialID'),
(N'dbo.AlmacenMP_Movimientos',N'MaterialSolicitadoID'),(N'dbo.AlmacenMP_Movimientos',N'TipoMovimiento'),(N'dbo.AlmacenMP_Movimientos',N'TipoMP'),
(N'dbo.AlmacenMP_Movimientos',N'Lote'),(N'dbo.AlmacenMP_Movimientos',N'Cantidad'),(N'dbo.AlmacenMP_Movimientos',N'Unidad'),(N'dbo.AlmacenMP_Movimientos',N'UbicacionID'),
(N'dbo.AlmacenMP_Movimientos',N'NumeroOF'),(N'dbo.AlmacenMP_Movimientos',N'FolioCompra'),(N'dbo.AlmacenMP_Movimientos',N'ResponsableUsuarioID'),
(N'dbo.AlmacenMP_Movimientos',N'EntregadoPorNombre'),(N'dbo.AlmacenMP_Movimientos',N'Seguimiento'),(N'dbo.AlmacenMP_Movimientos',N'FechaCreacion'),
(N'dbo.AlmacenMP_Movimientos',N'CreadoPor'),(N'dbo.AlmacenMP_Movimientos',N'Activo'),(N'dbo.AlmacenMP_Movimientos',N'RequiereValidacionProduccion'),
(N'dbo.AlmacenMP_Movimientos',N'ValidadoProduccion'),(N'dbo.AlmacenMP_Movimientos',N'ReferenciaOperacion'),(N'dbo.AlmacenMP_Movimientos',N'SolicitudProduccionID'),
(N'dbo.AlmacenMP_Movimientos',N'SolicitudProduccionDetalleID'),
(N'dbo.ERP_ParteDatosTecnicos',N'ParteID'),(N'dbo.ERP_ParteDatosTecnicos',N'MaterialID'),(N'dbo.ERP_ParteDatosTecnicos',N'Activo'),
(N'dbo.ERP_Materiales',N'MaterialID'),(N'dbo.ERP_Materiales',N'Activo'),
(N'dbo.ERP_Partes',N'ParteID'),(N'dbo.ERP_Partes',N'NumeroParte'),(N'dbo.ERP_Partes',N'Activo'),
-- Direccion / Produccion
(N'dbo.Produccion_RegistroHora',N'EjecucionProduccionID'),(N'dbo.Produccion_RegistroHora',N'OperadorID'),(N'dbo.Produccion_RegistroHora',N'MaquinaID'),
(N'dbo.Produccion_RegistroHora',N'FechaProduccion'),(N'dbo.Produccion_RegistroHora',N'HoraInicio'),(N'dbo.Produccion_RegistroHora',N'HoraFin'),
(N'dbo.Produccion_RegistroHora',N'CantidadOK'),(N'dbo.Produccion_RegistroHora',N'CantidadSospechosa'),(N'dbo.Produccion_RegistroHora',N'CantidadScrap'),
(N'dbo.Produccion_RegistroHora',N'ObjetivoHora'),(N'dbo.Produccion_RegistroHora',N'ObjetivoBloque'),(N'dbo.Produccion_RegistroHora',N'Activo'),
(N'dbo.Produccion_Paros',N'OperadorID'),(N'dbo.Produccion_Paros',N'MaquinaID'),(N'dbo.Produccion_Paros',N'FechaInicioParo'),
(N'dbo.Produccion_Paros',N'FechaFinParo'),(N'dbo.Produccion_Paros',N'DuracionMinutos'),(N'dbo.Produccion_Paros',N'Activo'),
(N'dbo.Produccion_Ejecucion',N'EjecucionProduccionID'),(N'dbo.Produccion_Ejecucion',N'OperadorNombre'),(N'dbo.Produccion_Ejecucion',N'MaquinaNombre'),
(N'dbo.ERP_Maquinas',N'MaquinaID'),(N'dbo.ERP_Maquinas',N'Codigo'),
-- Termometro por departamento
(N'dbo.Planeacion_ProgramaProduccion',N'CantidadProgramada'),(N'dbo.Planeacion_ProgramaProduccion',N'CantidadProducida'),(N'dbo.Planeacion_ProgramaProduccion',N'FechaInicioProgramada'),(N'dbo.Planeacion_ProgramaProduccion',N'Activo'),
(N'dbo.Calidad_Inspecciones',N'FechaCreacion'),(N'dbo.Calidad_Inspecciones',N'Liberado'),(N'dbo.Calidad_Inspecciones',N'EnContencion'),(N'dbo.Calidad_Inspecciones',N'EsScrap'),
(N'dbo.GP12_Solicitudes',N'FechaSolicitud'),(N'dbo.GP12_Solicitudes',N'CantidadPendiente'),(N'dbo.GP12_Solicitudes',N'Activo'),
(N'dbo.GP12_Inspecciones',N'FechaCreacion'),(N'dbo.GP12_Inspecciones',N'CantidadRevisada'),(N'dbo.GP12_Inspecciones',N'CantidadOK'),(N'dbo.GP12_Inspecciones',N'CantidadNOK'),(N'dbo.GP12_Inspecciones',N'CantidadScrap'),(N'dbo.GP12_Inspecciones',N'Activo'),
(N'dbo.AlmacenPT_Movimientos',N'FechaMovimiento'),(N'dbo.AlmacenPT_Movimientos',N'Activo'),
(N'dbo.Logistica_Embarques',N'FechaProgramada'),(N'dbo.Logistica_Embarques',N'FechaEntrega'),(N'dbo.Logistica_Embarques',N'TieneIncidencia'),(N'dbo.Logistica_Embarques',N'Estatus'),(N'dbo.Logistica_Embarques',N'Activo'),
(N'dbo.ComprasSolicitudes',N'FechaSolicitud'),(N'dbo.ComprasSolicitudes',N'EstatusID'),(N'dbo.ComprasSolicitudes',N'Activo'),
(N'dbo.ComprasOrdenes',N'FechaOrden'),(N'dbo.ComprasOrdenes',N'Total'),(N'dbo.ComprasOrdenes',N'Activo');

SELECT
    N'B_COLUMNAS_REQUERIDAS' AS Seccion,
    c.Objeto,
    c.Columna,
    CASE
        WHEN OBJECT_ID(c.Objeto,N'U') IS NULL THEN 0
        WHEN COL_LENGTH(c.Objeto,c.Columna) IS NULL THEN 0
        ELSE 1
    END AS Existe
FROM #ColumnasRequeridas c
ORDER BY c.Objeto,c.Columna;

SELECT @Bloqueos += COUNT(*)
FROM #ColumnasRequeridas c
WHERE OBJECT_ID(c.Objeto,N'U') IS NULL
   OR COL_LENGTH(c.Objeto,c.Columna) IS NULL;

DECLARE @RolDireccionID INT = NULL;
DECLARE @RolAlmacenID INT = NULL;
DECLARE @AccionVerID INT = NULL;

IF OBJECT_ID(N'dbo.Roles',N'U') IS NOT NULL
BEGIN
    SELECT TOP(1) @RolDireccionID=RolID
    FROM dbo.Roles
    WHERE Activo=1
      AND (RolID=3 OR UPPER(NombreRol) LIKE N'%DIRECCI%' OR UPPER(NombreRol) LIKE N'%GERENCIA%')
    ORDER BY CASE WHEN RolID=3 THEN 0 ELSE 1 END,RolID;

    SELECT TOP(1) @RolAlmacenID=RolID
    FROM dbo.Roles
    WHERE Activo=1
      AND (RolID=10 OR UPPER(NombreRol) LIKE N'%ALMAC%')
    ORDER BY CASE WHEN RolID=10 THEN 0 ELSE 1 END,RolID;
END;

IF OBJECT_ID(N'dbo.Acciones',N'U') IS NOT NULL
BEGIN
    SELECT TOP(1) @AccionVerID=AccionID
    FROM dbo.Acciones
    WHERE UPPER(LTRIM(RTRIM(Nombre)))=N'VER'
    ORDER BY AccionID;
END;

IF @RolDireccionID IS NULL SET @Bloqueos += 1;
IF @RolAlmacenID IS NULL SET @Bloqueos += 1;
IF @AccionVerID IS NULL SET @Bloqueos += 1;

SELECT
    N'C_ROLES_Y_ACCION' AS Seccion,
    @RolDireccionID AS RolDireccionID,
    @RolAlmacenID AS RolAlmacenID,
    @AccionVerID AS AccionVerID,
    CASE WHEN @RolDireccionID IS NOT NULL AND @RolAlmacenID IS NOT NULL AND @AccionVerID IS NOT NULL THEN N'OK' ELSE N'REVISAR' END AS Resultado;

IF OBJECT_ID(N'dbo.Calidad_ScrapEntregas',N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.Calidad_ScrapEntregas',N'Estado') IS NOT NULL
   AND COL_LENGTH(N'dbo.Calidad_ScrapEntregas',N'CantidadScrap') IS NOT NULL
   AND COL_LENGTH(N'dbo.Calidad_ScrapEntregas',N'CantidadMolida') IS NOT NULL
   AND COL_LENGTH(N'dbo.Calidad_ScrapEntregas',N'Activo') IS NOT NULL
BEGIN
    SELECT
        N'D_SCRAP_ESTADO_ACTUAL' AS Seccion,
        Estado,
        COUNT(*) AS Entregas,
        SUM(CONVERT(BIGINT,ISNULL(CantidadScrap,0))) AS Piezas,
        SUM(CONVERT(DECIMAL(18,4),ISNULL(CantidadMolida,0))) AS KgMolidos
    FROM dbo.Calidad_ScrapEntregas
    WHERE Activo=1
    GROUP BY Estado
    ORDER BY Estado;
END
ELSE
BEGIN
    SELECT N'D_SCRAP_ESTADO_ACTUAL' AS Seccion,N'NO DISPONIBLE: falta tabla o columnas requeridas.' AS Diagnostico;
END;

IF OBJECT_ID(N'dbo.MenuGrupo',N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.Menus',N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.SubMenus',N'U') IS NOT NULL
BEGIN
    SELECT
        N'E_MENU_ACTUAL' AS Seccion,
        g.MenuGrupoID,
        g.Nombre AS Grupo,
        m.MenuID,
        m.Nombre AS Menu,
        sm.SubMenuID,
        sm.Nombre AS SubMenu,
        sm.UrlEnlace,
        sm.Activo
    FROM dbo.MenuGrupo g
    LEFT JOIN dbo.Menus m ON m.MenuGrupoID=g.MenuGrupoID
    LEFT JOIN dbo.SubMenus sm ON sm.MenuID=m.MenuID
    WHERE UPPER(g.Nombre) LIKE N'%ALMAC%'
       OR UPPER(g.Nombre) LIKE N'%DIRECCI%'
       OR sm.UrlEnlace IN(N'/AlmacenScrap/Index',N'/Almacen/Scrap',N'/Direccion/Index')
    ORDER BY g.Orden,m.Orden,sm.SubMenuID;
END
ELSE
BEGIN
    SELECT N'E_MENU_ACTUAL' AS Seccion,N'NO DISPONIBLE: faltan tablas de menu.' AS Diagnostico;
END;

SELECT
    N'F_RESUMEN' AS Seccion,
    @Bloqueos AS Bloqueos,
    @Aplicar AS Aplicar,
    CASE
        WHEN @Bloqueos>0 THEN N'BLOQUEADO'
        WHEN @Aplicar=0 THEN N'LISTO_PARA_APLICAR'
        ELSE N'APLICANDO'
    END AS Resultado;

IF @Aplicar=0
BEGIN
    PRINT N'DIAGNOSTICO TERMINADO. No se modifico informacion.';
    PRINT N'Si Bloqueos=0 cambia solamente @Aplicar a 1 y ejecuta el archivo completo.';
    RETURN;
END;

IF @Bloqueos>0
    THROW 56101,N'Hay bloqueos de estructura. No se aplicaron cambios.',1;

BEGIN TRY
    BEGIN TRANSACTION;

    ---------------------------------------------------------------------------
    -- 1. Indice de bandeja. No altera datos.
    ---------------------------------------------------------------------------
    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id=OBJECT_ID(N'dbo.Calidad_ScrapEntregas')
          AND name=N'IX_Calidad_ScrapEntregas_Estado_Fecha'
    )
    BEGIN
        CREATE INDEX IX_Calidad_ScrapEntregas_Estado_Fecha
            ON dbo.Calidad_ScrapEntregas(Estado,FechaCreacion DESC)
            INCLUDE(CantidadScrap,NumeroParte,OrdenFabricacion,FechaRecepcion,UbicacionScrap,CantidadMolida,Activo);
    END;

    ---------------------------------------------------------------------------
    -- 2. Almacen Scrap: reutiliza el menu existente si lo hay.
    ---------------------------------------------------------------------------
    DECLARE @GrupoAlmacenID INT =
    (
        SELECT TOP(1) MenuGrupoID
        FROM dbo.MenuGrupo
        WHERE Activo=1
          AND (MenuGrupoID=1 OR UPPER(Nombre) LIKE N'%ALMAC%')
        ORDER BY CASE WHEN MenuGrupoID=1 THEN 0 ELSE 1 END,MenuGrupoID
    );

    IF @GrupoAlmacenID IS NULL
        THROW 56110,N'No se encontro el grupo principal de Almacen.',1;

    DECLARE @MenuScrapID INT =
    (
        SELECT TOP(1) m.MenuID
        FROM dbo.Menus m
        LEFT JOIN dbo.SubMenus sm ON sm.MenuID=m.MenuID
        WHERE m.MenuGrupoID=@GrupoAlmacenID
          AND
          (
              UPPER(LTRIM(RTRIM(m.Nombre)))=N'SCRAP'
              OR sm.UrlEnlace IN(N'/AlmacenScrap/Index',N'/Almacen/Scrap')
          )
        ORDER BY m.MenuID
    );

    IF @MenuScrapID IS NULL
    BEGIN
        INSERT dbo.Menus(MenuGrupoID,Nombre,Activo,IconoCss,Orden,Descripcion)
        VALUES
        (
            @GrupoAlmacenID,N'Scrap',1,N'fa-solid fa-recycle',
            ISNULL((SELECT MAX(Orden)+1 FROM dbo.Menus WHERE MenuGrupoID=@GrupoAlmacenID),1),
            N'Recepción física y molienda de Scrap originado por Calidad.'
        );
        SET @MenuScrapID=CONVERT(INT,SCOPE_IDENTITY());
    END
    ELSE
    BEGIN
        UPDATE dbo.Menus
        SET Nombre=N'Scrap',Activo=1,IconoCss=N'fa-solid fa-recycle',
            Descripcion=N'Recepción física y molienda de Scrap originado por Calidad.'
        WHERE MenuID=@MenuScrapID;
    END;

    DECLARE @SubMenuScrapID INT =
    (
        SELECT TOP(1) SubMenuID
        FROM dbo.SubMenus
        WHERE MenuID=@MenuScrapID
          AND UrlEnlace IN(N'/Almacen/Scrap',N'/AlmacenScrap/Index')
        ORDER BY CASE WHEN UrlEnlace=N'/Almacen/Scrap' THEN 0 ELSE 1 END,SubMenuID
    );

    IF @SubMenuScrapID IS NULL
    BEGIN
        INSERT dbo.SubMenus(MenuID,Nombre,UrlEnlace,Descripcion,Activo,FechaCreacion,IconoCSS)
        VALUES(@MenuScrapID,N'Ver Scrap',N'/Almacen/Scrap',N'Bandeja directa de dbo.Calidad_ScrapEntregas.',1,GETDATE(),N'fa-solid fa-recycle');
        SET @SubMenuScrapID=CONVERT(INT,SCOPE_IDENTITY());
    END
    ELSE
    BEGIN
        UPDATE dbo.SubMenus
        SET Nombre=N'Ver Scrap',UrlEnlace=N'/Almacen/Scrap',
            Descripcion=N'Bandeja directa de dbo.Calidad_ScrapEntregas.',Activo=1,
            FechaModificacion=GETDATE(),IconoCSS=N'fa-solid fa-recycle'
        WHERE SubMenuID=@SubMenuScrapID;
    END;

    DECLARE @SubMenuAccionScrapID INT =
    (
        SELECT TOP(1) SubMenuAccionID
        FROM dbo.SubMenuAcciones
        WHERE SubMenuID=@SubMenuScrapID AND AccionID=@AccionVerID
        ORDER BY SubMenuAccionID
    );

    IF @SubMenuAccionScrapID IS NULL
    BEGIN
        INSERT dbo.SubMenuAcciones(SubMenuID,AccionID,Activo,FechaCreacion)
        VALUES(@SubMenuScrapID,@AccionVerID,1,GETDATE());
        SET @SubMenuAccionScrapID=CONVERT(INT,SCOPE_IDENTITY());
    END
    ELSE
        UPDATE dbo.SubMenuAcciones SET Activo=1,FechaModificacion=GETDATE() WHERE SubMenuAccionID=@SubMenuAccionScrapID;

    UPDATE p
    SET p.Activo=1,p.FechaModificacion=GETDATE()
    FROM dbo.PermisosPorRol p
    WHERE p.SubMenuAccionID=@SubMenuAccionScrapID
      AND p.RolID IN(1,2,@RolAlmacenID)
      AND p.Activo=0;

    INSERT dbo.PermisosPorRol(RolID,SubMenuAccionID,Activo,FechaCreacion)
    SELECT r.RolID,@SubMenuAccionScrapID,1,GETDATE()
    FROM dbo.Roles r
    WHERE r.Activo=1 AND r.RolID IN(1,2,@RolAlmacenID)
      AND NOT EXISTS
      (
          SELECT 1 FROM dbo.PermisosPorRol p
          WHERE p.RolID=r.RolID AND p.SubMenuAccionID=@SubMenuAccionScrapID
      );

    ---------------------------------------------------------------------------
    -- 3. Direccion: modulo principal independiente, solo Ver.
    ---------------------------------------------------------------------------
    DECLARE @GrupoDireccionID INT =
    (
        SELECT TOP(1) MenuGrupoID
        FROM dbo.MenuGrupo
        WHERE UPPER(LTRIM(RTRIM(Nombre))) IN(N'DIRECCION',N'DIRECCIÓN')
        ORDER BY MenuGrupoID
    );

    IF @GrupoDireccionID IS NULL
    BEGIN
        INSERT dbo.MenuGrupo(Nombre,Descripcion,IconoCss,Orden,Activo)
        VALUES
        (
            'Dirección',
            'Indicadores ejecutivos y estado general de la operación.',
            N'fa-solid fa-chart-line',
            ISNULL((SELECT MAX(Orden)+1 FROM dbo.MenuGrupo),1),
            1
        );
        SET @GrupoDireccionID=CONVERT(INT,SCOPE_IDENTITY());
    END
    ELSE
    BEGIN
        UPDATE dbo.MenuGrupo
        SET Nombre='Dirección',Descripcion='Indicadores ejecutivos y estado general de la operación.',
            IconoCss=N'fa-solid fa-chart-line',Activo=1
        WHERE MenuGrupoID=@GrupoDireccionID;
    END;

    DECLARE @MenuDireccionID INT =
    (
        SELECT TOP(1) MenuID
        FROM dbo.Menus
        WHERE MenuGrupoID=@GrupoDireccionID
          AND UPPER(LTRIM(RTRIM(Nombre))) IN(N'DIRECCION',N'DIRECCIÓN',N'RESUMEN EJECUTIVO')
        ORDER BY MenuID
    );

    IF @MenuDireccionID IS NULL
    BEGIN
        INSERT dbo.Menus(MenuGrupoID,Nombre,Activo,IconoCss,Orden,Descripcion)
        VALUES(@GrupoDireccionID,N'Resumen ejecutivo',1,N'fa-solid fa-chart-pie',1,N'KPIs de Producción y termómetro ejecutivo por departamento.');
        SET @MenuDireccionID=CONVERT(INT,SCOPE_IDENTITY());
    END
    ELSE
    BEGIN
        UPDATE dbo.Menus
        SET Nombre=N'Resumen ejecutivo',Activo=1,IconoCss=N'fa-solid fa-chart-pie',Orden=1,
            Descripcion=N'KPIs de Producción y termómetro ejecutivo por departamento.'
        WHERE MenuID=@MenuDireccionID;
    END;

    DECLARE @SubMenuDireccionID INT =
    (
        SELECT TOP(1) SubMenuID
        FROM dbo.SubMenus
        WHERE MenuID=@MenuDireccionID
          AND (UrlEnlace=N'/Direccion/Index' OR UPPER(Nombre) LIKE N'%EJECUT%')
        ORDER BY SubMenuID
    );

    IF @SubMenuDireccionID IS NULL
    BEGIN
        INSERT dbo.SubMenus(MenuID,Nombre,UrlEnlace,Descripcion,Activo,FechaCreacion,IconoCSS)
        VALUES(@MenuDireccionID,N'Panel ejecutivo',N'/Direccion/Index',N'Consulta ejecutiva de KPIs y excepciones.',1,GETDATE(),N'fa-solid fa-chart-line');
        SET @SubMenuDireccionID=CONVERT(INT,SCOPE_IDENTITY());
    END
    ELSE
    BEGIN
        UPDATE dbo.SubMenus
        SET Nombre=N'Panel ejecutivo',UrlEnlace=N'/Direccion/Index',Descripcion=N'Consulta ejecutiva de KPIs y excepciones.',
            Activo=1,FechaModificacion=GETDATE(),IconoCSS=N'fa-solid fa-chart-line'
        WHERE SubMenuID=@SubMenuDireccionID;
    END;

    DECLARE @SubMenuAccionDireccionID INT =
    (
        SELECT TOP(1) SubMenuAccionID
        FROM dbo.SubMenuAcciones
        WHERE SubMenuID=@SubMenuDireccionID AND AccionID=@AccionVerID
        ORDER BY SubMenuAccionID
    );

    IF @SubMenuAccionDireccionID IS NULL
    BEGIN
        INSERT dbo.SubMenuAcciones(SubMenuID,AccionID,Activo,FechaCreacion)
        VALUES(@SubMenuDireccionID,@AccionVerID,1,GETDATE());
        SET @SubMenuAccionDireccionID=CONVERT(INT,SCOPE_IDENTITY());
    END
    ELSE
        UPDATE dbo.SubMenuAcciones SET Activo=1,FechaModificacion=GETDATE() WHERE SubMenuAccionID=@SubMenuAccionDireccionID;

    UPDATE p
    SET p.Activo=1,p.FechaModificacion=GETDATE()
    FROM dbo.PermisosPorRol p
    WHERE p.SubMenuAccionID=@SubMenuAccionDireccionID
      AND p.RolID IN(1,2,@RolDireccionID)
      AND p.Activo=0;

    INSERT dbo.PermisosPorRol(RolID,SubMenuAccionID,Activo,FechaCreacion)
    SELECT r.RolID,@SubMenuAccionDireccionID,1,GETDATE()
    FROM dbo.Roles r
    WHERE r.Activo=1 AND r.RolID IN(1,2,@RolDireccionID)
      AND NOT EXISTS
      (
          SELECT 1 FROM dbo.PermisosPorRol p
          WHERE p.RolID=r.RolID AND p.SubMenuAccionID=@SubMenuAccionDireccionID
      );

    COMMIT TRANSACTION;

    SELECT
        N'POST_ALMACEN_SCRAP' AS Seccion,
        g.MenuGrupoID,g.Nombre AS Grupo,m.MenuID,m.Nombre AS Menu,
        sm.SubMenuID,sm.Nombre AS SubMenu,sm.UrlEnlace,sm.Activo
    FROM dbo.MenuGrupo g
    INNER JOIN dbo.Menus m ON m.MenuGrupoID=g.MenuGrupoID
    INNER JOIN dbo.SubMenus sm ON sm.MenuID=m.MenuID
    WHERE sm.SubMenuID=@SubMenuScrapID;

    SELECT
        N'POST_DIRECCION' AS Seccion,
        g.MenuGrupoID,g.Nombre AS Grupo,m.MenuID,m.Nombre AS Menu,
        sm.SubMenuID,sm.Nombre AS SubMenu,sm.UrlEnlace,sm.Activo,
        COUNT(DISTINCT CASE WHEN p.Activo=1 THEN p.RolID END) AS RolesConPermiso
    FROM dbo.MenuGrupo g
    INNER JOIN dbo.Menus m ON m.MenuGrupoID=g.MenuGrupoID
    INNER JOIN dbo.SubMenus sm ON sm.MenuID=m.MenuID
    LEFT JOIN dbo.SubMenuAcciones sma ON sma.SubMenuID=sm.SubMenuID AND sma.AccionID=@AccionVerID AND sma.Activo=1
    LEFT JOIN dbo.PermisosPorRol p ON p.SubMenuAccionID=sma.SubMenuAccionID AND p.Activo=1
    WHERE sm.SubMenuID=@SubMenuDireccionID
    GROUP BY g.MenuGrupoID,g.Nombre,m.MenuID,m.Nombre,sm.SubMenuID,sm.Nombre,sm.UrlEnlace,sm.Activo;

    SELECT
        N'RESULTADO_FINAL_61' AS Seccion,
        CASE
            WHEN EXISTS(SELECT 1 FROM dbo.SubMenus WHERE SubMenuID=@SubMenuScrapID AND UrlEnlace=N'/Almacen/Scrap' AND Activo=1)
             AND EXISTS(SELECT 1 FROM dbo.SubMenus WHERE SubMenuID=@SubMenuDireccionID AND UrlEnlace=N'/Direccion/Index' AND Activo=1)
             AND EXISTS
                (
                    SELECT 1
                    FROM dbo.SubMenuAcciones sma
                    INNER JOIN dbo.PermisosPorRol p ON p.SubMenuAccionID=sma.SubMenuAccionID AND p.RolID=@RolDireccionID AND p.Activo=1
                    WHERE sma.SubMenuID=@SubMenuDireccionID AND sma.AccionID=@AccionVerID AND sma.Activo=1
                )
            THEN N'OK'
            ELSE N'REVISAR'
        END AS Resultado;
END TRY
BEGIN CATCH
    IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
    DECLARE @Error NVARCHAR(2048)=CONCAT(N'Error ',ERROR_NUMBER(),N' linea ',ERROR_LINE(),N': ',ERROR_MESSAGE());
    THROW 56199,@Error,1;
END CATCH;
GO
