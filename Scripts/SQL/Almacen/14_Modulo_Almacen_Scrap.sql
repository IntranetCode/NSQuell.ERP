USE [ERP_QUELL];
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @Aplicar BIT = 0; -- 0 = DIAGNOSTICO | 1 = INSTALAR EN TEST

DECLARE @Servidor NVARCHAR(256) =
    CONVERT(NVARCHAR(256), SERVERPROPERTY('ServerName'));

IF DB_NAME() <> N'ERP_QUELL'
    THROW 54500, N'Base incorrecta. Ejecuta este script en ERP_QUELL.', 1;

IF UPPER(@Servidor) LIKE N'%ERP_PROD%'
    THROW 54501, N'BLOQUEADO: este instalador es exclusivamente para TEST.', 1;

PRINT REPLICATE('=', 110);
PRINT N'MODULO ALMACEN SCRAP v1.1';
PRINT REPLICATE('=', 110);
PRINT CONCAT(N'Servidor : ', @Servidor);
PRINT CONCAT(N'Base     : ', DB_NAME());
PRINT CONCAT(N'Aplicar  : ', @Aplicar);
PRINT N'';
PRINT N'Arquitectura: Scrap = trazabilidad, NO inventario.';
PRINT N'El inventario MP solo cambia al ejecutar Realizar MP Molido.';
PRINT N'No modifica controladores, vistas ni tablas de Calidad o GP12.';
PRINT N'Hotfix v1.1: MPMovimientoID alineado a INT con dbo.AlmacenMP_Movimientos.MovimientoID.';
PRINT N'';

DECLARE @Faltantes TABLE
(
    Objeto NVARCHAR(256) NOT NULL
);

INSERT INTO @Faltantes(Objeto)
SELECT N'dbo.ERP_Partes' WHERE OBJECT_ID(N'dbo.ERP_Partes', N'U') IS NULL
UNION ALL SELECT N'dbo.ERP_ParteDatosTecnicos' WHERE OBJECT_ID(N'dbo.ERP_ParteDatosTecnicos', N'U') IS NULL
UNION ALL SELECT N'dbo.ERP_Materiales' WHERE OBJECT_ID(N'dbo.ERP_Materiales', N'U') IS NULL
UNION ALL SELECT N'dbo.ERP_Ubicaciones' WHERE OBJECT_ID(N'dbo.ERP_Ubicaciones', N'U') IS NULL
UNION ALL SELECT N'dbo.SolicitudesProduccion' WHERE OBJECT_ID(N'dbo.SolicitudesProduccion', N'U') IS NULL
UNION ALL SELECT N'dbo.Usuarios' WHERE OBJECT_ID(N'dbo.Usuarios', N'U') IS NULL
UNION ALL SELECT N'dbo.AlmacenMP_Movimientos' WHERE OBJECT_ID(N'dbo.AlmacenMP_Movimientos', N'U') IS NULL
UNION ALL SELECT N'dbo.Menus' WHERE OBJECT_ID(N'dbo.Menus', N'U') IS NULL
UNION ALL SELECT N'dbo.SubMenus' WHERE OBJECT_ID(N'dbo.SubMenus', N'U') IS NULL;

INSERT INTO @Faltantes(Objeto)
SELECT N'dbo.AlmacenMP_Movimientos.TipoMP'
WHERE COL_LENGTH(N'dbo.AlmacenMP_Movimientos', N'TipoMP') IS NULL
UNION ALL
SELECT N'dbo.AlmacenMP_Movimientos.MaterialSolicitadoID'
WHERE COL_LENGTH(N'dbo.AlmacenMP_Movimientos', N'MaterialSolicitadoID') IS NULL
UNION ALL
SELECT N'dbo.AlmacenMP_Movimientos.ReferenciaOperacion'
WHERE COL_LENGTH(N'dbo.AlmacenMP_Movimientos', N'ReferenciaOperacion') IS NULL
UNION ALL
SELECT N'dbo.AlmacenMP_Movimientos.SolicitudProduccionID'
WHERE COL_LENGTH(N'dbo.AlmacenMP_Movimientos', N'SolicitudProduccionID') IS NULL
UNION ALL
SELECT N'dbo.AlmacenMP_Movimientos.FolioCompra'
WHERE COL_LENGTH(N'dbo.AlmacenMP_Movimientos', N'FolioCompra') IS NULL;

SELECT N'PRERREQUISITO' AS Tipo, Objeto
FROM @Faltantes
ORDER BY Objeto;

IF EXISTS (SELECT 1 FROM @Faltantes)
BEGIN
    THROW 54502, N'Faltan prerrequisitos del módulo Almacén/MP. No se instaló Scrap.', 1;
END;


PRINT N'';
PRINT N'--- VALIDACION DE TIPOS PARA CLAVES FORANEAS ---';

DECLARE @TiposFK TABLE
(
    Objeto NVARCHAR(256) NOT NULL,
    Columna SYSNAME NOT NULL,
    Tipo SYSNAME NULL,
    Longitud SMALLINT NULL,
    Precision TINYINT NULL,
    Escala TINYINT NULL,
    Esperado SYSNAME NOT NULL
);

INSERT INTO @TiposFK
(
    Objeto,
    Columna,
    Tipo,
    Longitud,
    Precision,
    Escala,
    Esperado
)
SELECT N'dbo.ERP_Partes', N'ParteID',
       TYPE_NAME(c.user_type_id), c.max_length, c.precision, c.scale, N'int'
FROM sys.columns c
WHERE c.object_id = OBJECT_ID(N'dbo.ERP_Partes')
  AND c.name = N'ParteID'
UNION ALL
SELECT N'dbo.SolicitudesProduccion', N'SolicitudProduccionID',
       TYPE_NAME(c.user_type_id), c.max_length, c.precision, c.scale, N'int'
FROM sys.columns c
WHERE c.object_id = OBJECT_ID(N'dbo.SolicitudesProduccion')
  AND c.name = N'SolicitudProduccionID'
UNION ALL
SELECT N'dbo.Usuarios', N'UsuarioID',
       TYPE_NAME(c.user_type_id), c.max_length, c.precision, c.scale, N'int'
FROM sys.columns c
WHERE c.object_id = OBJECT_ID(N'dbo.Usuarios')
  AND c.name = N'UsuarioID'
UNION ALL
SELECT N'dbo.ERP_Materiales', N'MaterialID',
       TYPE_NAME(c.user_type_id), c.max_length, c.precision, c.scale, N'int'
FROM sys.columns c
WHERE c.object_id = OBJECT_ID(N'dbo.ERP_Materiales')
  AND c.name = N'MaterialID'
UNION ALL
SELECT N'dbo.ERP_Ubicaciones', N'UbicacionID',
       TYPE_NAME(c.user_type_id), c.max_length, c.precision, c.scale, N'int'
FROM sys.columns c
WHERE c.object_id = OBJECT_ID(N'dbo.ERP_Ubicaciones')
  AND c.name = N'UbicacionID'
UNION ALL
SELECT N'dbo.AlmacenMP_Movimientos', N'MovimientoID',
       TYPE_NAME(c.user_type_id), c.max_length, c.precision, c.scale, N'int'
FROM sys.columns c
WHERE c.object_id = OBJECT_ID(N'dbo.AlmacenMP_Movimientos')
  AND c.name = N'MovimientoID';

SELECT
    Objeto,
    Columna,
    Tipo,
    Longitud,
    Precision,
    Escala,
    Esperado,
    Estado =
        CASE WHEN Tipo = Esperado THEN N'OK' ELSE N'REVISAR' END
FROM @TiposFK
ORDER BY Objeto, Columna;

IF (SELECT COUNT(*) FROM @TiposFK) <> 6
    THROW 54503, N'No fue posible leer todos los tipos de las claves foráneas requeridas.', 1;

IF EXISTS
(
    SELECT 1
    FROM @TiposFK
    WHERE Tipo <> Esperado
)
    THROW 54504, N'Existe al menos una clave primaria con tipo distinto de INT. No se instaló Scrap.', 1;

PRINT N'TIPOS_FK_SCRAP_OK';
PRINT N'';

SELECT
    Objeto,
    Existe
FROM
(
    SELECT N'dbo.AlmacenScrap_Registros' AS Objeto,
           CASE WHEN OBJECT_ID(N'dbo.AlmacenScrap_Registros', N'U') IS NULL THEN 0 ELSE 1 END AS Existe
    UNION ALL
    SELECT N'dbo.AlmacenScrap_Historial',
           CASE WHEN OBJECT_ID(N'dbo.AlmacenScrap_Historial', N'U') IS NULL THEN 0 ELSE 1 END
    UNION ALL
    SELECT N'dbo.vw_AlmacenScrap_Registros',
           CASE WHEN OBJECT_ID(N'dbo.vw_AlmacenScrap_Registros', N'V') IS NULL THEN 0 ELSE 1 END
    UNION ALL
    SELECT N'dbo.usp_AlmacenScrap_RegistrarEntradaEscaner',
           CASE WHEN OBJECT_ID(N'dbo.usp_AlmacenScrap_RegistrarEntradaEscaner', N'P') IS NULL THEN 0 ELSE 1 END
    UNION ALL
    SELECT N'dbo.usp_AlmacenScrap_RegistrarOrigen',
           CASE WHEN OBJECT_ID(N'dbo.usp_AlmacenScrap_RegistrarOrigen', N'P') IS NULL THEN 0 ELSE 1 END
    UNION ALL
    SELECT N'dbo.usp_AlmacenScrap_ConfirmarRecepcion',
           CASE WHEN OBJECT_ID(N'dbo.usp_AlmacenScrap_ConfirmarRecepcion', N'P') IS NULL THEN 0 ELSE 1 END
    UNION ALL
    SELECT N'dbo.usp_AlmacenScrap_RealizarMPMolido',
           CASE WHEN OBJECT_ID(N'dbo.usp_AlmacenScrap_RealizarMPMolido', N'P') IS NULL THEN 0 ELSE 1 END
) d
ORDER BY Objeto;

IF @Aplicar = 0
BEGIN
    PRINT N'';
    PRINT N'MODULO_ALMACEN_SCRAP_V11_DIAGNOSTICO_OK';
    PRINT N'NO SE REALIZARON CAMBIOS.';
    PRINT N'Para instalar en TEST cambia @Aplicar = 1.';
    RETURN;
END;

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.AlmacenScrap_Registros', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.AlmacenScrap_Registros
        (
            ScrapRegistroID BIGINT IDENTITY(1,1) NOT NULL
                CONSTRAINT PK_AlmacenScrap_Registros PRIMARY KEY,

            Origen NVARCHAR(30) NOT NULL,
            OrigenReferenciaID BIGINT NULL,
            OrigenReferencia NVARCHAR(120) NULL,

            CodigoBarras NVARCHAR(500) NOT NULL,
            NumeroOF NVARCHAR(80) NOT NULL,
            SolicitudProduccionID INT NULL,
            ParteID INT NULL,
            NumeroParte NVARCHAR(120) NOT NULL,
            Designacion NVARCHAR(300) NOT NULL,
            CantidadPiezas INT NOT NULL,
            Lote NVARCHAR(120) NOT NULL,

            Estatus NVARCHAR(30) NOT NULL,
            FechaOrigen DATETIME2(7) NULL,
            FechaRecepcion DATETIME2(7) NULL,
            RecibidoPorUsuarioID INT NULL,
            RecibidoPorNombre NVARCHAR(180) NULL,

            MaterialIDMolido INT NULL,
            PesoMolidoKg DECIMAL(18,4) NULL,
            UbicacionIDMolido INT NULL,
            MPMovimientoID INT NULL,
            FechaMolido DATETIME2(7) NULL,
            MolidoPorUsuarioID INT NULL,
            MolidoPorNombre NVARCHAR(180) NULL,

            Observaciones NVARCHAR(800) NULL,

            FechaCreacion DATETIME2(7) NOT NULL
                CONSTRAINT DF_AlmacenScrap_Registros_FechaCreacion DEFAULT SYSUTCDATETIME(),
            CreadoPor NVARCHAR(180) NULL,
            FechaModificacion DATETIME2(7) NULL,
            ActualizadoPor NVARCHAR(180) NULL,
            Activo BIT NOT NULL
                CONSTRAINT DF_AlmacenScrap_Registros_Activo DEFAULT 1,

            CONSTRAINT CK_AlmacenScrap_Registros_Origen
                CHECK (Origen IN (N'ENTRADA_SCRAP', N'CALIDAD', N'GP12')),

            CONSTRAINT CK_AlmacenScrap_Registros_Estatus
                CHECK (Estatus IN (N'PENDIENTE_RECEPCION', N'RECIBIDO', N'MOLIDO')),

            CONSTRAINT CK_AlmacenScrap_Registros_CantidadPiezas
                CHECK (CantidadPiezas > 0),

            CONSTRAINT CK_AlmacenScrap_Registros_PesoMolido
                CHECK (PesoMolidoKg IS NULL OR PesoMolidoKg > 0),

            CONSTRAINT FK_AlmacenScrap_Registros_Parte
                FOREIGN KEY (ParteID) REFERENCES dbo.ERP_Partes(ParteID),

            CONSTRAINT FK_AlmacenScrap_Registros_SolicitudProduccion
                FOREIGN KEY (SolicitudProduccionID)
                REFERENCES dbo.SolicitudesProduccion(SolicitudProduccionID),

            CONSTRAINT FK_AlmacenScrap_Registros_RecibidoPor
                FOREIGN KEY (RecibidoPorUsuarioID) REFERENCES dbo.Usuarios(UsuarioID),

            CONSTRAINT FK_AlmacenScrap_Registros_MaterialMolido
                FOREIGN KEY (MaterialIDMolido) REFERENCES dbo.ERP_Materiales(MaterialID),

            CONSTRAINT FK_AlmacenScrap_Registros_UbicacionMolido
                FOREIGN KEY (UbicacionIDMolido) REFERENCES dbo.ERP_Ubicaciones(UbicacionID),

            CONSTRAINT FK_AlmacenScrap_Registros_MPMovimiento
                FOREIGN KEY (MPMovimientoID)
                REFERENCES dbo.AlmacenMP_Movimientos(MovimientoID),

            CONSTRAINT FK_AlmacenScrap_Registros_MolidoPor
                FOREIGN KEY (MolidoPorUsuarioID) REFERENCES dbo.Usuarios(UsuarioID)
        );
    END;

    IF OBJECT_ID(N'dbo.AlmacenScrap_Historial', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.AlmacenScrap_Historial
        (
            ScrapHistorialID BIGINT IDENTITY(1,1) NOT NULL
                CONSTRAINT PK_AlmacenScrap_Historial PRIMARY KEY,
            ScrapRegistroID BIGINT NOT NULL,
            Evento NVARCHAR(60) NOT NULL,
            EstatusAnterior NVARCHAR(30) NULL,
            EstatusNuevo NVARCHAR(30) NULL,
            Detalle NVARCHAR(1200) NULL,
            UsuarioID INT NULL,
            UsuarioNombre NVARCHAR(180) NULL,
            FechaEvento DATETIME2(7) NOT NULL
                CONSTRAINT DF_AlmacenScrap_Historial_FechaEvento DEFAULT SYSDATETIME(),

            CONSTRAINT FK_AlmacenScrap_Historial_Registro
                FOREIGN KEY (ScrapRegistroID)
                REFERENCES dbo.AlmacenScrap_Registros(ScrapRegistroID),

            CONSTRAINT FK_AlmacenScrap_Historial_Usuario
                FOREIGN KEY (UsuarioID)
                REFERENCES dbo.Usuarios(UsuarioID)
        );
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.AlmacenScrap_Registros')
          AND name = N'IX_AlmacenScrap_Registros_EstatusFecha'
    )
    BEGIN
        CREATE INDEX IX_AlmacenScrap_Registros_EstatusFecha
        ON dbo.AlmacenScrap_Registros(Estatus, FechaCreacion DESC)
        INCLUDE (Origen, NumeroOF, NumeroParte, Lote, CantidadPiezas);
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.AlmacenScrap_Registros')
          AND name = N'IX_AlmacenScrap_Registros_CodigoBarras'
    )
    BEGIN
        CREATE INDEX IX_AlmacenScrap_Registros_CodigoBarras
        ON dbo.AlmacenScrap_Registros(CodigoBarras);
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.AlmacenScrap_Registros')
          AND name = N'UX_AlmacenScrap_Registros_OrigenReferenciaID'
    )
    BEGIN
        CREATE UNIQUE INDEX UX_AlmacenScrap_Registros_OrigenReferenciaID
        ON dbo.AlmacenScrap_Registros(Origen, OrigenReferenciaID)
        WHERE OrigenReferenciaID IS NOT NULL AND Activo = 1;
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.AlmacenScrap_Registros')
          AND name = N'UX_AlmacenScrap_Registros_MPMovimiento'
    )
    BEGIN
        CREATE UNIQUE INDEX UX_AlmacenScrap_Registros_MPMovimiento
        ON dbo.AlmacenScrap_Registros(MPMovimientoID)
        WHERE MPMovimientoID IS NOT NULL;
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.AlmacenScrap_Historial')
          AND name = N'IX_AlmacenScrap_Historial_RegistroFecha'
    )
    BEGIN
        CREATE INDEX IX_AlmacenScrap_Historial_RegistroFecha
        ON dbo.AlmacenScrap_Historial(ScrapRegistroID, FechaEvento DESC);
    END;

    EXEC(N'CREATE OR ALTER VIEW dbo.vw_AlmacenScrap_Registros
AS
SELECT
    s.ScrapRegistroID,
    s.Origen,
    s.OrigenReferenciaID,
    s.OrigenReferencia,
    s.CodigoBarras,
    s.NumeroOF,
    s.SolicitudProduccionID,
    s.ParteID,
    s.NumeroParte,
    s.Designacion,
    s.CantidadPiezas,
    s.Lote,
    s.Estatus,
    s.FechaOrigen,
    s.FechaRecepcion,
    s.RecibidoPorUsuarioID,
    s.RecibidoPorNombre,
    s.MaterialIDMolido,
    CASE
        WHEN m.MaterialID IS NULL THEN N''''
        ELSE CONCAT(m.Codigo, N'' · '', m.Nombre)
    END AS MaterialMolido,
    s.PesoMolidoKg,
    s.UbicacionIDMolido,
    s.MPMovimientoID,
    s.FechaMolido,
    s.MolidoPorUsuarioID,
    s.MolidoPorNombre,
    s.Observaciones,
    s.FechaCreacion,
    s.CreadoPor,
    s.FechaModificacion,
    s.ActualizadoPor,
    s.Activo
FROM dbo.AlmacenScrap_Registros s
LEFT JOIN dbo.ERP_Materiales m
    ON m.MaterialID = s.MaterialIDMolido;');

    EXEC(N'CREATE OR ALTER PROCEDURE dbo.usp_AlmacenScrap_RegistrarEntradaEscaner
    @CodigoBarras NVARCHAR(500),
    @NumeroOF NVARCHAR(80),
    @NumeroParte NVARCHAR(120),
    @Designacion NVARCHAR(300),
    @CantidadPiezas INT,
    @Lote NVARCHAR(120),
    @ParteID INT = NULL,
    @Observaciones NVARCHAR(800) = NULL,
    @UsuarioID INT = NULL,
    @UsuarioNombre NVARCHAR(180) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @CodigoBarras = LTRIM(RTRIM(ISNULL(@CodigoBarras, N'''')));
    SET @NumeroOF = LTRIM(RTRIM(ISNULL(@NumeroOF, N'''')));
    SET @NumeroParte = LTRIM(RTRIM(ISNULL(@NumeroParte, N'''')));
    SET @Designacion = LTRIM(RTRIM(ISNULL(@Designacion, N'''')));
    SET @Lote = LTRIM(RTRIM(ISNULL(@Lote, N'''')));
    SET @UsuarioNombre = NULLIF(LTRIM(RTRIM(ISNULL(@UsuarioNombre, N''''))), N'''');

    SET @NumeroOF =
        REPLACE(
        REPLACE(
        REPLACE(
        REPLACE(
        REPLACE(@NumeroOF, NCHAR(39), N''/''),
            N''’'', N''/''),
            N''‘'', N''/''),
            N''`'', N''/''),
            N''´'', N''/'');

    IF @CodigoBarras = N''''
        THROW 54510, N''El código de barras de Scrap es obligatorio.'', 1;

    IF @NumeroOF = N'''' OR @NumeroParte = N'''' OR @Designacion = N'''' OR @Lote = N''''
        THROW 54511, N''El código de Scrap debe conservar OF, número de parte, designación y lote.'', 1;

    IF ISNULL(@CantidadPiezas, 0) <= 0
        THROW 54512, N''La cantidad de piezas de Scrap debe ser mayor que cero.'', 1;

    IF EXISTS
    (
        SELECT 1
        FROM dbo.AlmacenScrap_Registros WITH (UPDLOCK, HOLDLOCK)
        WHERE Activo = 1
          AND CodigoBarras = @CodigoBarras
    )
        THROW 54513, N''El código de barras ya está registrado en Scrap.'', 1;

    IF @ParteID IS NOT NULL
       AND NOT EXISTS
       (
           SELECT 1
           FROM dbo.ERP_Partes
           WHERE ParteID = @ParteID
             AND Activo = 1
       )
        SET @ParteID = NULL;

    IF @ParteID IS NULL
    BEGIN
        DECLARE @ParteCount INT;

        ;WITH Coincidencias AS
        (
            SELECT p.ParteID
            FROM dbo.ERP_Partes p
            WHERE p.Activo = 1
              AND UPPER(
                    REPLACE(
                    REPLACE(
                    REPLACE(
                    REPLACE(LTRIM(RTRIM(p.NumeroParte)), N''.'', N''''),
                        N''-'', N''''),
                        N''_'', N''''),
                        N'' '', N'''')
                  ) =
                  UPPER(
                    REPLACE(
                    REPLACE(
                    REPLACE(
                    REPLACE(@NumeroParte, N''.'', N''''),
                        N''-'', N''''),
                        N''_'', N''''),
                        N'' '', N'''')
                  )
        )
        SELECT
            @ParteCount = COUNT(*),
            @ParteID = CASE WHEN COUNT(*) = 1 THEN MAX(ParteID) ELSE NULL END
        FROM Coincidencias;
    END;

    DECLARE @SolicitudProduccionID INT = NULL;
    DECLARE @SolicitudCount INT = 0;

    ;WITH OFCoincidencias AS
    (
        SELECT DISTINCT s.SolicitudProduccionID
        FROM dbo.SolicitudesProduccion s
        CROSS APPLY
        (
            SELECT
                NumeroOFRecibidaCanon =
                    REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                        LTRIM(RTRIM(ISNULL(s.NumeroOFRecibida, N''''))),
                        NCHAR(39), N''/''), N''’'', N''/''), N''‘'', N''/''), N''`'', N''/''), N''´'', N''/''),
                FolioCanon =
                    REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                        LTRIM(RTRIM(ISNULL(s.FolioSolicitud, N''''))),
                        NCHAR(39), N''/''), N''’'', N''/''), N''‘'', N''/''), N''`'', N''/''), N''´'', N''/'')
        ) canon
        WHERE s.Activo = 1
          AND
          (
              canon.NumeroOFRecibidaCanon = @NumeroOF
              OR canon.FolioCanon = @NumeroOF
          )
    )
    SELECT
        @SolicitudCount = COUNT(*),
        @SolicitudProduccionID =
            CASE WHEN COUNT(*) = 1 THEN MAX(SolicitudProduccionID) ELSE NULL END
    FROM OFCoincidencias;

    INSERT dbo.AlmacenScrap_Registros
    (
        Origen,
        CodigoBarras,
        NumeroOF,
        SolicitudProduccionID,
        ParteID,
        NumeroParte,
        Designacion,
        CantidadPiezas,
        Lote,
        Estatus,
        FechaOrigen,
        FechaRecepcion,
        RecibidoPorUsuarioID,
        RecibidoPorNombre,
        Observaciones,
        FechaCreacion,
        CreadoPor,
        Activo
    )
    VALUES
    (
        N''ENTRADA_SCRAP'',
        @CodigoBarras,
        @NumeroOF,
        @SolicitudProduccionID,
        @ParteID,
        @NumeroParte,
        @Designacion,
        @CantidadPiezas,
        @Lote,
        N''RECIBIDO'',
        SYSDATETIME(),
        SYSDATETIME(),
        @UsuarioID,
        @UsuarioNombre,
        NULLIF(LTRIM(RTRIM(ISNULL(@Observaciones, N''''))), N''''),
        SYSUTCDATETIME(),
        @UsuarioNombre,
        1
    );

    DECLARE @ScrapRegistroID BIGINT = CONVERT(BIGINT, SCOPE_IDENTITY());

    INSERT dbo.AlmacenScrap_Historial
    (
        ScrapRegistroID,
        Evento,
        EstatusAnterior,
        EstatusNuevo,
        Detalle,
        UsuarioID,
        UsuarioNombre,
        FechaEvento
    )
    VALUES
    (
        @ScrapRegistroID,
        N''RECEPCION_ESCANER'',
        NULL,
        N''RECIBIDO'',
        CONCAT(
            N''Recepción directa por escáner. OF='', @NumeroOF,
            N''; Parte='', @NumeroParte,
            N''; Designación='', @Designacion,
            N''; Piezas='', @CantidadPiezas,
            N''; Lote='', @Lote,
            CASE
                WHEN @SolicitudProduccionID IS NULL
                    THEN N''; OF sin vínculo único a SolicitudesProduccion''
                ELSE CONCAT(N''; SolicitudProduccionID='', @SolicitudProduccionID)
            END
        ),
        @UsuarioID,
        @UsuarioNombre,
        SYSDATETIME()
    );

    SELECT @ScrapRegistroID AS ScrapRegistroID;
END');

    EXEC(N'CREATE OR ALTER PROCEDURE dbo.usp_AlmacenScrap_RegistrarOrigen
    @Origen NVARCHAR(30),
    @OrigenReferenciaID BIGINT = NULL,
    @OrigenReferencia NVARCHAR(120) = NULL,
    @CodigoBarras NVARCHAR(500),
    @NumeroOF NVARCHAR(80),
    @NumeroParte NVARCHAR(120),
    @Designacion NVARCHAR(300),
    @CantidadPiezas INT,
    @Lote NVARCHAR(120),
    @ParteID INT = NULL,
    @Observaciones NVARCHAR(800) = NULL,
    @UsuarioID INT = NULL,
    @UsuarioNombre NVARCHAR(180) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @Origen = UPPER(LTRIM(RTRIM(ISNULL(@Origen, N''''))));
    SET @OrigenReferencia = NULLIF(LTRIM(RTRIM(ISNULL(@OrigenReferencia, N''''))), N'''');
    SET @CodigoBarras = LTRIM(RTRIM(ISNULL(@CodigoBarras, N'''')));
    SET @NumeroOF = LTRIM(RTRIM(ISNULL(@NumeroOF, N'''')));
    SET @NumeroParte = LTRIM(RTRIM(ISNULL(@NumeroParte, N'''')));
    SET @Designacion = LTRIM(RTRIM(ISNULL(@Designacion, N'''')));
    SET @Lote = LTRIM(RTRIM(ISNULL(@Lote, N'''')));
    SET @UsuarioNombre = NULLIF(LTRIM(RTRIM(ISNULL(@UsuarioNombre, N''''))), N'''');

    IF @Origen NOT IN (N''CALIDAD'', N''GP12'')
        THROW 54520, N''Este contrato de integración acepta únicamente CALIDAD o GP12.'', 1;

    IF @OrigenReferenciaID IS NULL AND @OrigenReferencia IS NULL
        THROW 54521, N''Calidad/GP12 debe enviar una referencia de origen.'', 1;

    IF @CodigoBarras = N'''' OR @NumeroOF = N'''' OR @NumeroParte = N''''
       OR @Designacion = N'''' OR @Lote = N''''
        THROW 54522, N''Faltan snapshots obligatorios del Scrap.'', 1;

    IF ISNULL(@CantidadPiezas, 0) <= 0
        THROW 54523, N''La cantidad de piezas debe ser mayor que cero.'', 1;

    SET @NumeroOF =
        REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
            @NumeroOF,
            NCHAR(39), N''/''), N''’'', N''/''), N''‘'', N''/''), N''`'', N''/''), N''´'', N''/'');

    DECLARE @Existente BIGINT = NULL;

    SELECT TOP (1)
        @Existente = ScrapRegistroID
    FROM dbo.AlmacenScrap_Registros WITH (UPDLOCK, HOLDLOCK)
    WHERE Activo = 1
      AND Origen = @Origen
      AND
      (
          (@OrigenReferenciaID IS NOT NULL AND OrigenReferenciaID = @OrigenReferenciaID)
          OR
          (@OrigenReferenciaID IS NULL
           AND @OrigenReferencia IS NOT NULL
           AND OrigenReferencia = @OrigenReferencia)
      )
    ORDER BY ScrapRegistroID DESC;

    IF @Existente IS NOT NULL
    BEGIN
        SELECT @Existente AS ScrapRegistroID;
        RETURN;
    END;

    IF @ParteID IS NOT NULL
       AND NOT EXISTS
       (
           SELECT 1 FROM dbo.ERP_Partes
           WHERE ParteID = @ParteID
             AND Activo = 1
       )
        SET @ParteID = NULL;

    IF @ParteID IS NULL
    BEGIN
        ;WITH Coincidencias AS
        (
            SELECT p.ParteID
            FROM dbo.ERP_Partes p
            WHERE p.Activo = 1
              AND UPPER(REPLACE(REPLACE(REPLACE(REPLACE(
                    LTRIM(RTRIM(p.NumeroParte)),
                    N''.'', N''''), N''-'', N''''), N''_'', N''''), N'' '', N'''')) =
                  UPPER(REPLACE(REPLACE(REPLACE(REPLACE(
                    @NumeroParte,
                    N''.'', N''''), N''-'', N''''), N''_'', N''''), N'' '', N''''))
        )
        SELECT @ParteID =
            CASE WHEN COUNT(*) = 1 THEN MAX(ParteID) ELSE NULL END
        FROM Coincidencias;
    END;

    DECLARE @SolicitudProduccionID INT = NULL;

    ;WITH OFCoincidencias AS
    (
        SELECT DISTINCT s.SolicitudProduccionID
        FROM dbo.SolicitudesProduccion s
        WHERE s.Activo = 1
          AND
          (
              REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                    LTRIM(RTRIM(ISNULL(s.NumeroOFRecibida, N''''))),
                    NCHAR(39), N''/''), N''’'', N''/''), N''‘'', N''/''), N''`'', N''/''), N''´'', N''/'') = @NumeroOF
              OR
              REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                    LTRIM(RTRIM(ISNULL(s.FolioSolicitud, N''''))),
                    NCHAR(39), N''/''), N''’'', N''/''), N''‘'', N''/''), N''`'', N''/''), N''´'', N''/'') = @NumeroOF
          )
    )
    SELECT @SolicitudProduccionID =
        CASE WHEN COUNT(*) = 1 THEN MAX(SolicitudProduccionID) ELSE NULL END
    FROM OFCoincidencias;

    INSERT dbo.AlmacenScrap_Registros
    (
        Origen,
        OrigenReferenciaID,
        OrigenReferencia,
        CodigoBarras,
        NumeroOF,
        SolicitudProduccionID,
        ParteID,
        NumeroParte,
        Designacion,
        CantidadPiezas,
        Lote,
        Estatus,
        FechaOrigen,
        Observaciones,
        FechaCreacion,
        CreadoPor,
        Activo
    )
    VALUES
    (
        @Origen,
        @OrigenReferenciaID,
        @OrigenReferencia,
        @CodigoBarras,
        @NumeroOF,
        @SolicitudProduccionID,
        @ParteID,
        @NumeroParte,
        @Designacion,
        @CantidadPiezas,
        @Lote,
        N''PENDIENTE_RECEPCION'',
        SYSDATETIME(),
        NULLIF(LTRIM(RTRIM(ISNULL(@Observaciones, N''''))), N''''),
        SYSUTCDATETIME(),
        @UsuarioNombre,
        1
    );

    DECLARE @ScrapRegistroID BIGINT = CONVERT(BIGINT, SCOPE_IDENTITY());

    INSERT dbo.AlmacenScrap_Historial
    (
        ScrapRegistroID,
        Evento,
        EstatusAnterior,
        EstatusNuevo,
        Detalle,
        UsuarioID,
        UsuarioNombre,
        FechaEvento
    )
    VALUES
    (
        @ScrapRegistroID,
        N''ENVIADO_A_ALMACEN'',
        NULL,
        N''PENDIENTE_RECEPCION'',
        CONCAT(
            N''Origen='', @Origen,
            N''; Referencia='', COALESCE(CONVERT(NVARCHAR(40), @OrigenReferenciaID), @OrigenReferencia, N''S/R''),
            N''; OF='', @NumeroOF,
            N''; Parte='', @NumeroParte,
            N''; Lote='', @Lote
        ),
        @UsuarioID,
        @UsuarioNombre,
        SYSDATETIME()
    );

    SELECT @ScrapRegistroID AS ScrapRegistroID;
END');

    EXEC(N'CREATE OR ALTER PROCEDURE dbo.usp_AlmacenScrap_ConfirmarRecepcion
    @ScrapRegistroID BIGINT,
    @UsuarioID INT = NULL,
    @UsuarioNombre NVARCHAR(180) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRANSACTION;

        DECLARE @Estatus NVARCHAR(30);

        SELECT
            @Estatus = Estatus
        FROM dbo.AlmacenScrap_Registros WITH (UPDLOCK, HOLDLOCK)
        WHERE ScrapRegistroID = @ScrapRegistroID
          AND Activo = 1;

        IF @Estatus IS NULL
            THROW 54530, N''No existe el registro de Scrap indicado.'', 1;

        IF @Estatus = N''RECIBIDO''
        BEGIN
            COMMIT TRANSACTION;
            RETURN;
        END;

        IF @Estatus <> N''PENDIENTE_RECEPCION''
            THROW 54531, N''El registro ya no está pendiente de recepción.'', 1;

        UPDATE dbo.AlmacenScrap_Registros
        SET
            Estatus = N''RECIBIDO'',
            FechaRecepcion = SYSDATETIME(),
            RecibidoPorUsuarioID = @UsuarioID,
            RecibidoPorNombre = NULLIF(LTRIM(RTRIM(ISNULL(@UsuarioNombre, N''''))), N''''),
            FechaModificacion = SYSUTCDATETIME(),
            ActualizadoPor = NULLIF(LTRIM(RTRIM(ISNULL(@UsuarioNombre, N''''))), N'''')
        WHERE ScrapRegistroID = @ScrapRegistroID;

        INSERT dbo.AlmacenScrap_Historial
        (
            ScrapRegistroID,
            Evento,
            EstatusAnterior,
            EstatusNuevo,
            Detalle,
            UsuarioID,
            UsuarioNombre,
            FechaEvento
        )
        VALUES
        (
            @ScrapRegistroID,
            N''RECEPCION_CONFIRMADA'',
            N''PENDIENTE_RECEPCION'',
            N''RECIBIDO'',
            N''Almacén confirmó la recepción física del Scrap.'',
            @UsuarioID,
            NULLIF(LTRIM(RTRIM(ISNULL(@UsuarioNombre, N''''))), N''''),
            SYSDATETIME()
        );

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END');

    EXEC(N'CREATE OR ALTER PROCEDURE dbo.usp_AlmacenScrap_RealizarMPMolido
    @ScrapRegistroID BIGINT,
    @MaterialID INT,
    @PesoMolidoKg DECIMAL(18,4),
    @UbicacionID INT,
    @UsuarioID INT = NULL,
    @UsuarioNombre NVARCHAR(180) = NULL,
    @Observaciones NVARCHAR(800) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF ISNULL(@PesoMolidoKg, 0) <= 0
        THROW 54540, N''El peso de MP Molido debe ser mayor que cero.'', 1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.ERP_Ubicaciones
        WHERE UbicacionID = @UbicacionID
          AND Activo = 1
          AND UPPER(LTRIM(RTRIM(Almacen))) = N''MP''
    )
        THROW 54541, N''La ubicación seleccionada no corresponde a una ubicación MP activa.'', 1;

    BEGIN TRY
        BEGIN TRANSACTION;

        DECLARE
            @Estatus NVARCHAR(30),
            @MPMovimientoID INT,
            @ParteID INT,
            @NumeroParte NVARCHAR(120),
            @NumeroOF NVARCHAR(80),
            @Designacion NVARCHAR(300),
            @Lote NVARCHAR(120),
            @SolicitudProduccionID INT,
            @CodigoBarras NVARCHAR(500);

        SELECT
            @Estatus = Estatus,
            @MPMovimientoID = MPMovimientoID,
            @ParteID = ParteID,
            @NumeroParte = NumeroParte,
            @NumeroOF = NumeroOF,
            @Designacion = Designacion,
            @Lote = Lote,
            @SolicitudProduccionID = SolicitudProduccionID,
            @CodigoBarras = CodigoBarras
        FROM dbo.AlmacenScrap_Registros WITH (UPDLOCK, HOLDLOCK)
        WHERE ScrapRegistroID = @ScrapRegistroID
          AND Activo = 1;

        IF @Estatus IS NULL
            THROW 54542, N''No existe el registro de Scrap.'', 1;

        IF @Estatus <> N''RECIBIDO''
            THROW 54543, N''El Scrap debe estar RECIBIDO antes de generar MP Molido.'', 1;

        IF @MPMovimientoID IS NOT NULL
            THROW 54544, N''Este Scrap ya generó un movimiento de MP.'', 1;

        IF @ParteID IS NULL
        BEGIN
            ;WITH Coincidencias AS
            (
                SELECT p.ParteID
                FROM dbo.ERP_Partes p
                WHERE p.Activo = 1
                  AND UPPER(REPLACE(REPLACE(REPLACE(REPLACE(
                        LTRIM(RTRIM(p.NumeroParte)),
                        N''.'', N''''), N''-'', N''''), N''_'', N''''), N'' '', N'''')) =
                      UPPER(REPLACE(REPLACE(REPLACE(REPLACE(
                        @NumeroParte,
                        N''.'', N''''), N''-'', N''''), N''_'', N''''), N'' '', N''''))
            )
            SELECT @ParteID =
                CASE WHEN COUNT(*) = 1 THEN MAX(ParteID) ELSE NULL END
            FROM Coincidencias;

            IF @ParteID IS NULL
                THROW 54545, N''No se pudo resolver una parte única para este Scrap.'', 1;
        END;

        DECLARE @MaterialCount INT;
        DECLARE @MaterialTecnicoID INT;

        SELECT
            @MaterialCount = COUNT(DISTINCT d.MaterialID),
            @MaterialTecnicoID =
                CASE
                    WHEN COUNT(DISTINCT d.MaterialID) = 1 THEN MAX(d.MaterialID)
                    ELSE NULL
                END
        FROM dbo.ERP_ParteDatosTecnicos d
        INNER JOIN dbo.ERP_Materiales m
            ON m.MaterialID = d.MaterialID
           AND m.Activo = 1
        WHERE d.ParteID = @ParteID
          AND d.Activo = 1
          AND d.MaterialID IS NOT NULL;

        IF ISNULL(@MaterialCount, 0) <> 1 OR @MaterialTecnicoID IS NULL
            THROW 54546, N''La parte no tiene un MaterialID activo y único en ERP_ParteDatosTecnicos.'', 1;

        IF @MaterialTecnicoID <> @MaterialID
            THROW 54547, N''El material recibido no coincide con el material técnico vigente de la parte.'', 1;

        IF @SolicitudProduccionID IS NULL
        BEGIN
            ;WITH OFCoincidencias AS
            (
                SELECT DISTINCT s.SolicitudProduccionID
                FROM dbo.SolicitudesProduccion s
                WHERE s.Activo = 1
                  AND
                  (
                      REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                            LTRIM(RTRIM(ISNULL(s.NumeroOFRecibida, N''''))),
                            NCHAR(39), N''/''), N''’'', N''/''), N''‘'', N''/''), N''`'', N''/''), N''´'', N''/'') = @NumeroOF
                      OR
                      REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                            LTRIM(RTRIM(ISNULL(s.FolioSolicitud, N''''))),
                            NCHAR(39), N''/''), N''’'', N''/''), N''‘'', N''/''), N''`'', N''/''), N''´'', N''/'') = @NumeroOF
                  )
            )
            SELECT @SolicitudProduccionID =
                CASE WHEN COUNT(*) = 1 THEN MAX(SolicitudProduccionID) ELSE NULL END
            FROM OFCoincidencias;
        END;

        DECLARE @Referencia NVARCHAR(120) =
            CONCAT(N''SCRAP-MOLIDO:'', CONVERT(NVARCHAR(30), @ScrapRegistroID));

        IF EXISTS
        (
            SELECT 1
            FROM dbo.AlmacenMP_Movimientos WITH (UPDLOCK, HOLDLOCK)
            WHERE Activo = 1
              AND ReferenciaOperacion = @Referencia
        )
            THROW 54548, N''Ya existe una Entrada MP vinculada a este Scrap.'', 1;

        DECLARE @Seguimiento NVARCHAR(800) =
            LEFT(
                CONCAT(
                    N''MP Molido desde Scrap #'', @ScrapRegistroID,
                    N'' | Código='', @CodigoBarras,
                    N'' | Parte='', @NumeroParte,
                    N'' | Designación='', @Designacion,
                    N'' | Lote='', @Lote,
                    CASE
                        WHEN NULLIF(LTRIM(RTRIM(ISNULL(@Observaciones, N''''))), N'''') IS NULL
                            THEN N''''
                        ELSE CONCAT(N'' | '', LTRIM(RTRIM(@Observaciones)))
                    END
                ),
                800
            );

        INSERT dbo.AlmacenMP_Movimientos
        (
            FechaMovimiento,
            MaterialID,
            MaterialSolicitadoID,
            TipoMovimiento,
            TipoMP,
            Lote,
            Cantidad,
            Unidad,
            UbicacionID,
            NumeroOF,
            FolioCompra,
            ResponsableUsuarioID,
            EntregadoPorNombre,
            Seguimiento,
            FechaCreacion,
            CreadoPor,
            Activo,
            RequiereValidacionProduccion,
            ValidadoProduccion,
            ReferenciaOperacion,
            SolicitudProduccionID
        )
        VALUES
        (
            SYSDATETIME(),
            @MaterialID,
            NULL,
            N''Entrada'',
            N''M'',
            @Lote,
            @PesoMolidoKg,
            N''KG'',
            @UbicacionID,
            @NumeroOF,
            NULL,
            @UsuarioID,
            NULLIF(LTRIM(RTRIM(ISNULL(@UsuarioNombre, N''''))), N''''),
            @Seguimiento,
            SYSUTCDATETIME(),
            NULLIF(LTRIM(RTRIM(ISNULL(@UsuarioNombre, N''''))), N''''),
            1,
            0,
            1,
            @Referencia,
            @SolicitudProduccionID
        );

        SET @MPMovimientoID = CONVERT(INT, SCOPE_IDENTITY());

        UPDATE dbo.AlmacenScrap_Registros
        SET
            ParteID = @ParteID,
            SolicitudProduccionID = COALESCE(SolicitudProduccionID, @SolicitudProduccionID),
            Estatus = N''MOLIDO'',
            MaterialIDMolido = @MaterialID,
            PesoMolidoKg = @PesoMolidoKg,
            UbicacionIDMolido = @UbicacionID,
            MPMovimientoID = @MPMovimientoID,
            FechaMolido = SYSDATETIME(),
            MolidoPorUsuarioID = @UsuarioID,
            MolidoPorNombre = NULLIF(LTRIM(RTRIM(ISNULL(@UsuarioNombre, N''''))), N''''),
            FechaModificacion = SYSUTCDATETIME(),
            ActualizadoPor = NULLIF(LTRIM(RTRIM(ISNULL(@UsuarioNombre, N''''))), N'''')
        WHERE ScrapRegistroID = @ScrapRegistroID;

        INSERT dbo.AlmacenScrap_Historial
        (
            ScrapRegistroID,
            Evento,
            EstatusAnterior,
            EstatusNuevo,
            Detalle,
            UsuarioID,
            UsuarioNombre,
            FechaEvento
        )
        VALUES
        (
            @ScrapRegistroID,
            N''MP_MOLIDO_GENERADO'',
            N''RECIBIDO'',
            N''MOLIDO'',
            CONCAT(
                N''Movimiento MP #'', @MPMovimientoID,
                N''; MaterialID='', @MaterialID,
                N''; Peso='', CONVERT(NVARCHAR(50), @PesoMolidoKg), N'' KG'',
                N''; Lote='', @Lote,
                N''; Referencia='', @Referencia
            ),
            @UsuarioID,
            NULLIF(LTRIM(RTRIM(ISNULL(@UsuarioNombre, N''''))), N''''),
            SYSDATETIME()
        );

        COMMIT TRANSACTION;

        SELECT @MPMovimientoID AS MPMovimientoID;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END');

    ---------------------------------------------------------------------------
    -- MENU / PERMISOS: se integra a MenuGrupoID = 1.
    -- Se copian acciones y permisos desde Almacen MP.
    ---------------------------------------------------------------------------

    DECLARE @MenuGrupoID INT = 1;
    DECLARE @MenuScrapID INT =
    (
        SELECT TOP (1) m.MenuID
        FROM dbo.Menus m
        WHERE m.MenuGrupoID = @MenuGrupoID
          AND
          (
              UPPER(LTRIM(RTRIM(m.Nombre))) = N'SCRAP'
              OR EXISTS
              (
                  SELECT 1
                  FROM dbo.SubMenus sm
                  WHERE sm.MenuID = m.MenuID
                    AND sm.UrlEnlace = N'/AlmacenScrap/Index'
              )
          )
        ORDER BY m.MenuID
    );

    IF @MenuScrapID IS NULL
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
            N'Scrap',
            N'Recepción, trazabilidad y transformación de Scrap a materia prima molida.',
            N'fa-solid fa-recycle',
            ISNULL(
                (
                    SELECT MAX(ISNULL(Orden, 0)) + 1
                    FROM dbo.Menus
                    WHERE MenuGrupoID = @MenuGrupoID
                ),
                1
            ),
            1
        );

        SET @MenuScrapID = CONVERT(INT, SCOPE_IDENTITY());
    END
    ELSE
    BEGIN
        UPDATE dbo.Menus
        SET
            Nombre = N'Scrap',
            Descripcion = N'Recepción, trazabilidad y transformación de Scrap a materia prima molida.',
            IconoCss = N'fa-solid fa-recycle',
            Activo = 1
        WHERE MenuID = @MenuScrapID;
    END;

    DECLARE @SubMenuScrapID INT =
    (
        SELECT TOP (1) SubMenuID
        FROM dbo.SubMenus
        WHERE MenuID = @MenuScrapID
          AND UrlEnlace = N'/AlmacenScrap/Index'
        ORDER BY SubMenuID
    );

    IF @SubMenuScrapID IS NULL
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
            @MenuID = @MenuScrapID,
            @Nombre = N'Ver Scrap',
            @Url = N'/AlmacenScrap/Index',
            @Descripcion = N'Recepción e historial de Scrap y generación de MP Molido.',
            @Icono = N'fa-solid fa-recycle',
            @NuevoID = @SubMenuScrapID OUTPUT;
    END
    ELSE
    BEGIN
        UPDATE dbo.SubMenus
        SET
            Nombre = N'Ver Scrap',
            UrlEnlace = N'/AlmacenScrap/Index',
            Activo = 1
        WHERE SubMenuID = @SubMenuScrapID;
    END;

    DECLARE @SubMenuMPID INT =
    (
        SELECT TOP (1) sm.SubMenuID
        FROM dbo.SubMenus sm
        INNER JOIN dbo.Menus m
            ON m.MenuID = sm.MenuID
        WHERE m.MenuGrupoID = @MenuGrupoID
          AND sm.Activo = 1
          AND sm.UrlEnlace IN (N'/AlmacenMP/Index', N'/AlmacenMP')
        ORDER BY
            CASE WHEN sm.UrlEnlace = N'/AlmacenMP/Index' THEN 0 ELSE 1 END,
            sm.SubMenuID
    );

    IF @SubMenuMPID IS NOT NULL
       AND OBJECT_ID(N'dbo.SubMenuAcciones', N'U') IS NOT NULL
    BEGIN
        INSERT dbo.SubMenuAcciones(SubMenuID, AccionID)
        SELECT
            @SubMenuScrapID,
            origen.AccionID
        FROM dbo.SubMenuAcciones origen
        WHERE origen.SubMenuID = @SubMenuMPID
          AND NOT EXISTS
          (
              SELECT 1
              FROM dbo.SubMenuAcciones destino
              WHERE destino.SubMenuID = @SubMenuScrapID
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
                    ON destino.SubMenuID = @SubMenuScrapID
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
                    ON destino.SubMenuID = @SubMenuScrapID
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
                        AND actual.SubMenuAccionID = destino.SubMenuAccionID
                        AND actual.Activo = 1
                  );',
                N'@SubMenuScrapID INT, @SubMenuMPID INT',
                @SubMenuScrapID = @SubMenuScrapID,
                @SubMenuMPID = @SubMenuMPID;
            END;
        END;
    END;

    COMMIT TRANSACTION;

    PRINT N'';
    PRINT N'MODULO_ALMACEN_SCRAP_V11_TEST_INSTALADO_OK';
    PRINT N'No se modificó Calidad ni GP12.';
    PRINT N'Scrap no afecta inventario hasta Realizar MP Molido.';

    SELECT
        m.MenuID,
        m.Nombre AS MenuNombre,
        sm.SubMenuID,
        sm.Nombre AS SubMenuNombre,
        sm.UrlEnlace,
        sm.Activo
    FROM dbo.Menus m
    INNER JOIN dbo.SubMenus sm
        ON sm.MenuID = m.MenuID
    WHERE m.MenuID = @MenuScrapID;

    SELECT
        Objeto,
        Tipo
    FROM
    (
        SELECT N'dbo.AlmacenScrap_Registros' AS Objeto, N'TABLA' AS Tipo
        UNION ALL SELECT N'dbo.AlmacenScrap_Historial', N'TABLA'
        UNION ALL SELECT N'dbo.vw_AlmacenScrap_Registros', N'VISTA'
        UNION ALL SELECT N'dbo.usp_AlmacenScrap_RegistrarEntradaEscaner', N'PROCEDIMIENTO'
        UNION ALL SELECT N'dbo.usp_AlmacenScrap_RegistrarOrigen', N'PROCEDIMIENTO'
        UNION ALL SELECT N'dbo.usp_AlmacenScrap_ConfirmarRecepcion', N'PROCEDIMIENTO'
        UNION ALL SELECT N'dbo.usp_AlmacenScrap_RealizarMPMolido', N'PROCEDIMIENTO'
    ) x
    ORDER BY Tipo, Objeto;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    PRINT N'';
    PRINT N'MODULO_ALMACEN_SCRAP_V11_TEST_ERROR_ROLLBACK_OK';
    PRINT CONCAT(N'Error ', ERROR_NUMBER(), N': ', ERROR_MESSAGE());
    THROW;
END CATCH;
GO
