using ERP.NSQuell.Models;
using ERP.NSQuell.Models.ViewModels.Almacen;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;

namespace ERP.NSQuell.Controllers;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed partial class AlmacenOFController : AlmacenBaseController
{
    private static readonly string[] AreasPermitidas =
    {
        "MP", "EMBALAJE", "PENDIENTES", "CON_MOVIMIENTOS"
    };

    public AlmacenOFController(IConfiguration configuration) : base(configuration) { }

    [HttpGet]
    public async Task<IActionResult> Index(
        string? q,
        int? estatus,
        string? area,
        DateTime? desde,
        DateTime? hasta,
        int pagina = 1,
        CancellationToken cancellationToken = default)
    {
        var sesion = ValidarSesion();
        if (sesion != null) return sesion;

        if (desde.HasValue && hasta.HasValue && hasta.Value.Date < desde.Value.Date)
            (desde, hasta) = (hasta, desde);

        var areaNormalizada = area?.Trim().ToUpperInvariant();

        var vm = new AlmacenOFIndexVm
        {
            Busqueda = q?.Trim(),
            EstatusID = estatus is > 0 and <= 11 ? estatus : null,
            Area = AreasPermitidas.Contains(areaNormalizada ?? string.Empty)
                ? areaNormalizada
                : null,
            Desde = desde?.Date,
            Hasta = hasta?.Date,
            Pagina = Math.Max(1, pagina),
            // ALMACEN_OF_CARGA_AMPLIA_V4_2
            TamanoPagina = 500
        };

        vm.EstatusDisponibles = Enumerable.Range(1, 11)
            .Select(id => new AlmacenOFEstatusFiltroVm
            {
                EstatusID = id,
                Nombre = PlaneacionOFEstatus.Nombre(id)
            })
            .ToList();

        await using var connection = await AbrirConexionAsync(cancellationToken);

        var objetosRequeridos = new (string Nombre, string Tipo)[]
        {
            ("dbo.SolicitudesProduccion", "U"),
            ("dbo.SolicitudesProduccionDetalle", "U"),
            ("dbo.AlmacenMP_Movimientos", "U"),
            ("dbo.AlmacenEmbalajes_Movimientos", "U"),
            ("dbo.AlmacenPT_Movimientos", "U"),
            ("dbo.ERP_Partes", "U"),
            // NSQ_DEVOLUCION_MATERIALES_V1_2
            ("dbo.Produccion_RecepcionMateriales", "U"),
            ("dbo.Produccion_DevolucionesMateriales", "U")
        };

        foreach (var objeto in objetosRequeridos)
        {
            if (!await ExisteObjetoAsync(
                    connection,
                    objeto.Nombre,
                    objeto.Tipo,
                    cancellationToken))
            {
                vm.Configurado = false;
                vm.MensajeConfiguracion =
                    $"No está disponible {objeto.Nombre}. Verifica la instalación de Planeación y Almacén.";
                return View(vm);
            }
        }

        if (!await ExisteColumnaAsync(
                connection,
                "dbo.AlmacenMP_Movimientos",
                "MaterialSolicitadoID",
                cancellationToken))
        {
            vm.Configurado = false;
            vm.MensajeConfiguracion =
                "Falta ejecutar el SQL de sustitución de materia prima v5.0.4.";
            return View(vm);
        }

        if (!await ExisteColumnaAsync(
                connection,
                "dbo.AlmacenEmbalajes_Movimientos",
                "EmbalajeSolicitadoID",
                cancellationToken))
        {
            vm.Configurado = false;
            vm.MensajeConfiguracion =
                "Falta ejecutar el SQL de sustitución de embalajes v5.0.6.";
            return View(vm);
        }

        const string sql = @"
WITH Detalle AS
(
    SELECT
        d.SolicitudProduccionID,
        COUNT_BIG(1) AS TotalRenglones,
        SUM(ISNULL(d.CantidadPiezas, 0)) AS TotalPiezas,
        SUM(ISNULL(d.CantidadMpKg, 0)) AS MpRequerida,
        SUM(ISNULL(d.CantidadEmbalajes, 0)) AS EmbalajeRequerido
    FROM dbo.SolicitudesProduccionDetalle d
    WHERE d.Activo = 1
    GROUP BY d.SolicitudProduccionID
),
Base AS
(
    SELECT
        s.SolicitudProduccionID,
        s.FolioSolicitud,
        s.NumeroOFRecibida,
        COALESCE
        (
            NULLIF(LTRIM(RTRIM(s.NumeroOFRecibida)), ''),
            NULLIF(LTRIM(RTRIM(s.FolioSolicitud)), ''),
            CONCAT('OF-ID-', s.SolicitudProduccionID)
        ) AS NumeroOFClave,
        s.FechaSolicitud,
        s.FechaRequerida,
        s.FechaInicioPlaneada,
        s.FechaFinPlaneada,
        s.FechaCreacion,
        ISNULL(NULLIF(LTRIM(RTRIM(c.Nombre)), ''), s.ClienteNombre) AS Cliente,
        ISNULL(NULLIF(LTRIM(RTRIM(s.Prioridad)), ''), 'Normal') AS Prioridad,
        s.EstatusID,
        ISNULL(NULLIF(LTRIM(RTRIM(s.ResponsablePlaneacionNombre)), ''), '') AS ResponsablePlaneacionNombre,

        CONVERT(INT, ISNULL(d.TotalRenglones, 0)) AS TotalRenglones,
        CONVERT(INT, ISNULL(d.TotalPiezas, 0)) AS TotalPiezas,
        ISNULL(resumen.MaterialResumen, N'') AS MaterialResumen,
        ISNULL(resumen.EmbalajeResumen, N'') AS EmbalajeResumen,
        CONVERT(DECIMAL(18,4), ISNULL(d.MpRequerida, 0)) AS MpRequerida,
        CONVERT(DECIMAL(18,4), ISNULL(d.EmbalajeRequerido, 0)) AS EmbalajeRequerido,

        CONVERT
        (
            DECIMAL(18,4),
            CASE
                WHEN ISNULL(mp.MpEntregada, 0) < 0 THEN 0
                ELSE ISNULL(mp.MpEntregada, 0)
            END
        ) AS MpEntregada,

        CONVERT
        (
            DECIMAL(18,4),
            CASE
                WHEN ISNULL(em.EmbalajeEntregado, 0) < 0 THEN 0
                ELSE ISNULL(em.EmbalajeEntregado, 0)
            END
        ) AS EmbalajeEntregado,

        CONVERT(BIGINT, ISNULL(mp.MovimientosMP, 0)) AS MovimientosMP,
        CONVERT(BIGINT, ISNULL(em.MovimientosEmbalaje, 0)) AS MovimientosEmbalaje
    FROM dbo.SolicitudesProduccion s
    LEFT JOIN dbo.ERP_Clientes c
        ON c.ClienteID = s.ClienteID
    LEFT JOIN Detalle d
        ON d.SolicitudProduccionID = s.SolicitudProduccionID

    OUTER APPLY
    (
        SELECT
            MaterialResumen =
                STUFF
                (
                    (
                        SELECT DISTINCT
                            N' | '
                            + COALESCE
                              (
                                  NULLIF(LTRIM(RTRIM(dx.MaterialCodigo)), N''),
                                  N'Sin código'
                              )
                            + CASE
                                  WHEN NULLIF(LTRIM(RTRIM(dx.MaterialDescripcion)), N'') IS NULL
                                      THEN N''
                                  ELSE N' · ' + LTRIM(RTRIM(dx.MaterialDescripcion))
                              END
                        FROM dbo.SolicitudesProduccionDetalle dx
                        WHERE dx.SolicitudProduccionID = s.SolicitudProduccionID
                          AND dx.Activo = 1
                          AND
                          (
                              NULLIF(LTRIM(RTRIM(dx.MaterialCodigo)), N'') IS NOT NULL
                              OR NULLIF(LTRIM(RTRIM(dx.MaterialDescripcion)), N'') IS NOT NULL
                          )
                        FOR XML PATH(N''), TYPE
                    ).value(N'.', N'nvarchar(max)'),
                    1,
                    3,
                    N''
                ),

            EmbalajeResumen =
                STUFF
                (
                    (
                        SELECT DISTINCT
                            N' | '
                            + COALESCE
                              (
                                  NULLIF(LTRIM(RTRIM(dx.EmbalajeCodigo)), N''),
                                  N'Sin código'
                              )
                            + CASE
                                  WHEN NULLIF(LTRIM(RTRIM(dx.EmbalajeDescripcion)), N'') IS NULL
                                      THEN N''
                                  ELSE N' · ' + LTRIM(RTRIM(dx.EmbalajeDescripcion))
                              END
                        FROM dbo.SolicitudesProduccionDetalle dx
                        WHERE dx.SolicitudProduccionID = s.SolicitudProduccionID
                          AND dx.Activo = 1
                          AND
                          (
                              NULLIF(LTRIM(RTRIM(dx.EmbalajeCodigo)), N'') IS NOT NULL
                              OR NULLIF(LTRIM(RTRIM(dx.EmbalajeDescripcion)), N'') IS NOT NULL
                          )
                        FOR XML PATH(N''), TYPE
                    ).value(N'.', N'nvarchar(max)'),
                    1,
                    3,
                    N''
                )
    ) resumen

    OUTER APPLY
    (
        SELECT
            SUM
            (
                CASE
                    WHEN mm.TipoMovimiento IN (N'Salida', N'Consumo')
                        THEN mm.Cantidad
                    WHEN mm.TipoMovimiento = N'Retorno'
                        THEN -mm.Cantidad
                    ELSE 0
                END
            ) AS MpEntregada,
            COUNT_BIG(1) AS MovimientosMP
        FROM dbo.AlmacenMP_Movimientos mm
        WHERE mm.Activo = 1
          AND
          (
              mm.SolicitudProduccionID = s.SolicitudProduccionID
              OR
              (
                  mm.SolicitudProduccionID IS NULL
                  AND
                  (
                      (
                          NULLIF(LTRIM(RTRIM(s.FolioSolicitud)), '') IS NOT NULL
                          AND LTRIM(RTRIM(mm.NumeroOF)) = LTRIM(RTRIM(s.FolioSolicitud))
                      )
                      OR
                      (
                          NULLIF(LTRIM(RTRIM(s.NumeroOFRecibida)), '') IS NOT NULL
                          AND LTRIM(RTRIM(mm.NumeroOF)) = LTRIM(RTRIM(s.NumeroOFRecibida))
                      )
                  )
              )
          )
    ) mp

    OUTER APPLY
    (
        SELECT
            SUM
            (
                CASE
                    WHEN me.TipoMovimiento IN (N'Salida', N'Consumo')
                        THEN me.Cantidad
                    WHEN me.TipoMovimiento = N'Retorno'
                        THEN -me.Cantidad
                    ELSE 0
                END
            ) AS EmbalajeEntregado,
            COUNT_BIG(1) AS MovimientosEmbalaje
        FROM dbo.AlmacenEmbalajes_Movimientos me
        WHERE me.Activo = 1
          AND
          (
              (
                  NULLIF(LTRIM(RTRIM(s.FolioSolicitud)), '') IS NOT NULL
                  AND LTRIM(RTRIM(me.NumeroOF)) = LTRIM(RTRIM(s.FolioSolicitud))
              )
              OR
              (
                  NULLIF(LTRIM(RTRIM(s.NumeroOFRecibida)), '') IS NOT NULL
                  AND LTRIM(RTRIM(me.NumeroOF)) = LTRIM(RTRIM(s.NumeroOFRecibida))
              )
          )
    ) em


    WHERE s.Activo = 1
      AND (@EstatusID IS NULL OR s.EstatusID = @EstatusID)
      AND (@Desde IS NULL OR s.FechaSolicitud >= @Desde)
      AND (@Hasta IS NULL OR s.FechaSolicitud < DATEADD(DAY, 1, @Hasta))
      AND
      (
          @Q IS NULL
          OR s.FolioSolicitud LIKE '%' + @Q + '%'
          OR s.NumeroOFRecibida LIKE '%' + @Q + '%'
          OR ISNULL(c.Nombre, s.ClienteNombre) LIKE '%' + @Q + '%'
          OR EXISTS
          (
              SELECT 1
              FROM dbo.SolicitudesProduccionDetalle dx
              WHERE dx.SolicitudProduccionID = s.SolicitudProduccionID
                AND dx.Activo = 1
                AND
                (
                    dx.ReferenciaSAP LIKE '%' + @Q + '%'
                    OR dx.DesignacionDescripcionSAP LIKE '%' + @Q + '%'
                    OR dx.MaterialCodigo LIKE '%' + @Q + '%'
                    OR dx.MaterialDescripcion LIKE '%' + @Q + '%'
                    OR dx.EmbalajeCodigo LIKE '%' + @Q + '%'
                    OR dx.EmbalajeDescripcion LIKE '%' + @Q + '%'
                )
          )
      )
),
Filtrada AS
(
    SELECT *
    FROM Base
    WHERE
        @Area IS NULL
        OR (@Area = N'MP' AND MpRequerida > 0)
        OR (@Area = N'EMBALAJE' AND EmbalajeRequerido > 0)
        OR
        (
            @Area = N'CON_MOVIMIENTOS'
            AND (MovimientosMP + MovimientosEmbalaje) > 0
        )
        OR
        (
            @Area = N'PENDIENTES'
            AND
            (
                (MpRequerida > 0 AND MpEntregada + 0.0005 < MpRequerida)
                OR
                (
                    EmbalajeRequerido > 0
                    AND EmbalajeEntregado + 0.0005 < EmbalajeRequerido
                )

            )
        )
)
SELECT
    *,
    COUNT_BIG(1) OVER() AS TotalRegistros,
    SUM(CONVERT(BIGINT, TotalPiezas)) OVER() AS TotalPiezasFiltradas,

    SUM
    (
        CASE
            WHEN MpRequerida > 0
             AND MpEntregada + 0.0005 < MpRequerida
            THEN 1 ELSE 0
        END
    ) OVER() AS PendientesMP,

    SUM
    (
        CASE
            WHEN EmbalajeRequerido > 0
             AND EmbalajeEntregado + 0.0005 < EmbalajeRequerido
            THEN 1 ELSE 0
        END
    ) OVER() AS PendientesEmbalaje,


    SUM
    (
        CASE
            WHEN (MovimientosMP + MovimientosEmbalaje) > 0
            THEN 1 ELSE 0
        END
    ) OVER() AS OFConMovimientos
FROM Filtrada
ORDER BY FechaCreacion DESC, SolicitudProduccionID DESC
OFFSET @Offset ROWS
FETCH NEXT @TamanoPagina ROWS ONLY;";

        await using var command = new SqlCommand(sql, connection);

        command.Parameters.Add("@Q", SqlDbType.NVarChar, 300).Value =
            string.IsNullOrWhiteSpace(vm.Busqueda)
                ? DBNull.Value
                : vm.Busqueda;

        command.Parameters.Add("@EstatusID", SqlDbType.Int).Value =
            vm.EstatusID.HasValue
                ? vm.EstatusID.Value
                : DBNull.Value;

        // NSQ_DEVOLUCION_MATERIALES_V1_2
        // PENDIENTES se filtra despues de sustituir "entregado" por
        // la cantidad realmente aceptada por Produccion.
        command.Parameters.Add("@Area", SqlDbType.NVarChar, 30).Value =
            string.IsNullOrWhiteSpace(vm.Area)
            || string.Equals(vm.Area, "PENDIENTES", StringComparison.OrdinalIgnoreCase)
                ? DBNull.Value
                : vm.Area;

        command.Parameters.Add("@Desde", SqlDbType.Date).Value =
            vm.Desde.HasValue
                ? vm.Desde.Value
                : DBNull.Value;

        command.Parameters.Add("@Hasta", SqlDbType.Date).Value =
            vm.Hasta.HasValue
                ? vm.Hasta.Value
                : DBNull.Value;

        command.Parameters.Add("@Offset", SqlDbType.Int).Value =
            (vm.Pagina - 1) * vm.TamanoPagina;

        command.Parameters.Add("@TamanoPagina", SqlDbType.Int).Value =
            vm.TamanoPagina;

        var primeraFila = true;

        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var estatusId = Entero(reader, "EstatusID");

                var item = new AlmacenOFItemVm
                {
                    SolicitudProduccionID = Entero(reader, "SolicitudProduccionID"),
                    FolioSolicitud = Texto(reader, "FolioSolicitud"),
                    NumeroOFRecibida = Texto(reader, "NumeroOFRecibida"),
                    NumeroOFClave = Texto(reader, "NumeroOFClave"),
                    FechaSolicitud = Fecha(reader, "FechaSolicitud") ?? DateTime.MinValue,
                    FechaRequerida = Fecha(reader, "FechaRequerida"),
                    FechaInicioPlaneada = Fecha(reader, "FechaInicioPlaneada"),
                    FechaFinPlaneada = Fecha(reader, "FechaFinPlaneada"),
                    Cliente = Texto(reader, "Cliente"),
                    Prioridad = Texto(reader, "Prioridad"),
                    EstatusID = estatusId,
                    EstatusNombre = PlaneacionOFEstatus.Nombre(estatusId),
                    ResponsablePlaneacionNombre = Texto(reader, "ResponsablePlaneacionNombre"),
                    TotalRenglones = Entero(reader, "TotalRenglones"),
                    TotalPiezas = Entero(reader, "TotalPiezas"),
                    MaterialResumen = Texto(reader, "MaterialResumen"),
                    EmbalajeResumen = Texto(reader, "EmbalajeResumen"),
                    MpRequerida = DecimalValor(reader, "MpRequerida"),
                    MpEntregada = DecimalValor(reader, "MpEntregada"),
                    EmbalajeRequerido = DecimalValor(reader, "EmbalajeRequerido"),
                    EmbalajeEntregado = DecimalValor(reader, "EmbalajeEntregado"),
                    MovimientosMP = EnteroLargo(reader, "MovimientosMP"),
                    MovimientosEmbalaje = EnteroLargo(reader, "MovimientosEmbalaje")
                };

                vm.Ordenes.Add(item);

                if (primeraFila)
                {
                    vm.TotalRegistros = Convert.ToInt32(
                        Math.Min(int.MaxValue, EnteroLargo(reader, "TotalRegistros")));

                    vm.TotalPiezas = EnteroLargo(reader, "TotalPiezasFiltradas");
                    vm.PendientesMP = Entero(reader, "PendientesMP");
                    vm.PendientesEmbalaje = Entero(reader, "PendientesEmbalaje");
                    vm.OFConMovimientos = Entero(reader, "OFConMovimientos");
                    primeraFila = false;
                }
            }
        }

        await CargarEntregablesAsync(connection, vm.Ordenes, cancellationToken);
        // ALMACEN_OF_MAQUINAS_PT_V4_1
        await CargarMaquinasAlmacenAsync(connection, vm.Ordenes, cancellationToken);
        await CargarProductoTerminadoSolicitadoAsync(connection, vm.Ordenes, cancellationToken);

        // NSQ_DEVOLUCION_MATERIALES_V1_2
        await AplicarValidacionProduccionYDevolucionesAsync(
            connection,
            vm.Ordenes,
            cancellationToken);

        if (string.Equals(
                vm.Area,
                "PENDIENTES",
                StringComparison.OrdinalIgnoreCase))
        {
            vm.Ordenes = vm.Ordenes
                .Where(x => x.TienePendientesAlmacen)
                .ToList();
        }

        vm.Ordenes = vm.Ordenes
            .OrderByDescending(x => x.TieneDevolucionPendiente)
            .ThenByDescending(x => x.UltimaDevolucionFecha)
            .ThenByDescending(x => x.FechaSolicitud)
            .ThenByDescending(x => x.SolicitudProduccionID)
            .ToList();

        vm.PendientesMP =
            vm.Ordenes.Count(
                x => x.MaterialesEntrega.Any(
                    y => y.Pendiente > 0.0005m));

        vm.PendientesEmbalaje =
            vm.Ordenes.Count(
                x => x.EmbalajesEntrega.Any(
                    y => y.Pendiente > 0.0005m));

        vm.TotalRegistros = vm.Ordenes.Count;

        return View(vm);
    }

    // ============================================================
    // NSQ_DEVOLUCION_MATERIALES_V1_2
    // "Entregado" para cerrar una OF ya no significa solo Salida de Almacen.
    // Para MP/Embalaje significa cantidad ACEPTADA por Produccion.
    // ============================================================
    private static async Task AplicarValidacionProduccionYDevolucionesAsync(
        SqlConnection connection,
        List<AlmacenOFItemVm> ordenes,
        CancellationToken cancellationToken)
    {
        if (ordenes.Count == 0)
            return;

        var parametros =
            ordenes
                .Select(
                    (x, i) =>
                        new
                        {
                            x.SolicitudProduccionID,
                            Nombre = $"@DevOf{i}"
                        })
                .ToList();

        var inSql =
            string.Join(
                ",",
                parametros.Select(x => x.Nombre));

        var aceptacion =
            new Dictionary<
                (int SolicitudID,string Tipo,int CatalogoID),
                (decimal Aceptado,decimal EnValidacion)>();

        var sqlAceptacion = $@"
SELECT
    r.SolicitudProduccionID,
    r.TipoOrigen,
    CASE
        WHEN r.TipoOrigen=N'MP'
            THEN r.MaterialSolicitadoID
        ELSE r.EmbalajeSolicitadoID
    END AS CatalogoID,
    CONVERT(
        decimal(18,4),
        ISNULL(
            SUM(
                CASE
                    WHEN r.EstadoRecepcion IN(
                        N'RECIBIDO_COMPLETO',
                        N'RECIBIDO_PARCIAL')
                    THEN ISNULL(r.CantidadRecibidaProduccion,0)
                    ELSE 0
                END),
            0)
    ) AS AceptadoProduccion,
    CONVERT(
        decimal(18,4),
        ISNULL(
            SUM(
                CASE
                    WHEN r.EstadoRecepcion=N'PENDIENTE'
                    THEN r.CantidadEntregadaAlmacen
                    ELSE 0
                END),
            0)
    ) AS EnValidacionProduccion
FROM dbo.Produccion_RecepcionMateriales r
WHERE r.Activo=1
  AND r.SolicitudProduccionID IN ({inSql})
  AND r.TipoOrigen IN(N'MP',N'EMBALAJE')
  AND
  (
      (r.TipoOrigen=N'MP' AND r.MaterialSolicitadoID IS NOT NULL)
      OR
      (r.TipoOrigen=N'EMBALAJE' AND r.EmbalajeSolicitadoID IS NOT NULL)
  )
GROUP BY
    r.SolicitudProduccionID,
    r.TipoOrigen,
    CASE
        WHEN r.TipoOrigen=N'MP'
            THEN r.MaterialSolicitadoID
        ELSE r.EmbalajeSolicitadoID
    END;";

        await using (var command =
            new SqlCommand(sqlAceptacion, connection))
        {
            foreach (var parametro in parametros)
            {
                command.Parameters.Add(
                    parametro.Nombre,
                    SqlDbType.Int).Value =
                    parametro.SolicitudProduccionID;
            }

            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                var catalogoId =
                    reader["CatalogoID"] == DBNull.Value
                        ? 0
                        : Convert.ToInt32(reader["CatalogoID"]);

                if (catalogoId <= 0)
                    continue;

                var key =
                    (
                        Convert.ToInt32(reader["SolicitudProduccionID"]),
                        reader["TipoOrigen"]?.ToString()?.Trim()
                            ?? string.Empty,
                        catalogoId
                    );

                aceptacion[key] =
                    (
                        Convert.ToDecimal(reader["AceptadoProduccion"]),
                        Convert.ToDecimal(reader["EnValidacionProduccion"])
                    );
            }
        }

        var devoluciones =
            // NSQ_DEVOLUCION_MATERIALES_V1_3
            // NSQ_DEVOLUCION_MATERIALES_V1_4
            new Dictionary<
                (int SolicitudID,string Tipo,int CatalogoID),
                (long Id,decimal Cantidad,string Motivo,string? Comentario,string Usuario,DateTime Fecha)>();

        var sqlDevoluciones = $@"
;WITH D AS
(
    SELECT
        d.DevolucionMaterialID,
        d.SolicitudProduccionID,
        d.TipoOrigen,
        CASE
            WHEN d.TipoOrigen=N'MP'
                THEN d.MaterialSolicitadoID
            ELSE d.EmbalajeSolicitadoID
        END AS CatalogoID,
        d.CantidadDevuelta,
        d.Motivo,
        d.Comentario,
        COALESCE
        (
            NULLIF
            (
                LTRIM(RTRIM
                (
                    ISNULL(pDev.Nombre,N'')+N' '+
                    ISNULL(pDev.ApellidoPaterno,N'')+N' '+
                    ISNULL(pDev.ApellidoMaterno,N'')
                )),
                N''
            ),
            N'Usuario #'+CONVERT(nvarchar(20),d.UsuarioDevolucionID)
        ) AS UsuarioDevolucionNombre,
        d.FechaDevolucion,
        ROW_NUMBER() OVER
        (
            PARTITION BY
                d.SolicitudProduccionID,
                d.TipoOrigen,
                CASE
                    WHEN d.TipoOrigen=N'MP'
                        THEN d.MaterialSolicitadoID
                    ELSE d.EmbalajeSolicitadoID
                END
            ORDER BY
                d.FechaDevolucion DESC,
                d.DevolucionMaterialID DESC
        ) AS rn
    FROM dbo.Produccion_DevolucionesMateriales d
    LEFT JOIN dbo.Usuarios uDev
        ON uDev.UsuarioID=d.UsuarioDevolucionID
    LEFT JOIN dbo.Persona pDev
        ON pDev.PersonaID=uDev.PersonaID
    WHERE d.Activo=1
      AND d.Estado=N'PENDIENTE_REPOSICION'
      AND d.SolicitudProduccionID IN ({inSql})
)
SELECT
    DevolucionMaterialID,
    SolicitudProduccionID,
    TipoOrigen,
    CatalogoID,
    CantidadDevuelta,
    Motivo,
    Comentario,
    UsuarioDevolucionNombre,
    FechaDevolucion
FROM D
WHERE rn=1;";

        await using (var command =
            new SqlCommand(sqlDevoluciones, connection))
        {
            foreach (var parametro in parametros)
            {
                command.Parameters.Add(
                    parametro.Nombre,
                    SqlDbType.Int).Value =
                    parametro.SolicitudProduccionID;
            }

            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                if (reader["CatalogoID"] == DBNull.Value)
                    continue;

                var key =
                    (
                        Convert.ToInt32(reader["SolicitudProduccionID"]),
                        reader["TipoOrigen"]?.ToString()?.Trim()
                            ?? string.Empty,
                        Convert.ToInt32(reader["CatalogoID"])
                    );

                devoluciones[key] =
                    (
                        Convert.ToInt64(reader["DevolucionMaterialID"]),
                        Convert.ToDecimal(reader["CantidadDevuelta"]),
                        reader["Motivo"]?.ToString()?.Trim()
                            ?? string.Empty,
                        reader["Comentario"] == DBNull.Value
                            ? null
                            : reader["Comentario"]?.ToString()?.Trim(),
                        reader["UsuarioDevolucionNombre"]?.ToString()?.Trim()
                            ?? "Usuario de Produccion",
                        Convert.ToDateTime(reader["FechaDevolucion"])
                    );
            }
        }

        foreach (var orden in ordenes)
        {
            foreach (var item in orden.MaterialesEntrega)
            {
                item.EntregadoFisico = item.Entregado;

                var key =
                    (
                        orden.SolicitudProduccionID,
                        "MP",
                        item.CatalogoID
                    );

                if (aceptacion.TryGetValue(key, out var a))
                {
                    item.Entregado = Math.Max(0m, a.Aceptado);
                    item.EnValidacionProduccion =
                        Math.Max(0m, a.EnValidacion);
                }
                else
                {
                    item.Entregado = 0m;
                    item.EnValidacionProduccion = 0m;
                }

                if (devoluciones.TryGetValue(key, out var d))
                {
                    item.DevolucionMaterialID = d.Id;
                    item.CantidadDevuelta = d.Cantidad;
                    item.MotivoDevolucion = d.Motivo;
                    item.ComentarioDevolucion = d.Comentario;
                    item.UsuarioDevolucionNombre = d.Usuario;
                    item.FechaDevolucion = d.Fecha;
                }
            }

            foreach (var item in orden.EmbalajesEntrega)
            {
                item.EntregadoFisico = item.Entregado;

                var key =
                    (
                        orden.SolicitudProduccionID,
                        "EMBALAJE",
                        item.CatalogoID
                    );

                if (aceptacion.TryGetValue(key, out var a))
                {
                    item.Entregado = Math.Max(0m, a.Aceptado);
                    item.EnValidacionProduccion =
                        Math.Max(0m, a.EnValidacion);
                }
                else
                {
                    item.Entregado = 0m;
                    item.EnValidacionProduccion = 0m;
                }

                if (devoluciones.TryGetValue(key, out var d))
                {
                    item.DevolucionMaterialID = d.Id;
                    item.CantidadDevuelta = d.Cantidad;
                    item.MotivoDevolucion = d.Motivo;
                    item.ComentarioDevolucion = d.Comentario;
                    item.UsuarioDevolucionNombre = d.Usuario;
                    item.FechaDevolucion = d.Fecha;
                }
            }

            orden.MpEntregada =
                orden.MaterialesEntrega.Sum(x => x.Entregado);

            orden.EmbalajeEntregado =
                orden.EmbalajesEntrega.Sum(x => x.Entregado);
        }
    }
    private static async Task CargarEntregablesAsync(
        SqlConnection connection,
        List<AlmacenOFItemVm> ordenes,
        CancellationToken cancellationToken)
    {
        if (ordenes.Count == 0) return;

        var parametros = ordenes
            .Select((x, i) => new { x.SolicitudProduccionID, Nombre = $"@Of{i}" })
            .ToList();

        var inSql = string.Join(",", parametros.Select(x => x.Nombre));
        var sql = $@"
WITH Materiales AS
(
    SELECT
        s.SolicitudProduccionID,
        ISNULL(m.MaterialID, 0) AS CatalogoID,
        COALESCE(NULLIF(LTRIM(RTRIM(d.MaterialCodigo)), ''), 'SIN-CODIGO') AS Codigo,
        MAX(ISNULL(d.MaterialDescripcion, '')) AS Descripcion,
        COALESCE(NULLIF(LTRIM(RTRIM(m.UnidadDefault)), ''), 'KG') AS Unidad,
        SUM(CONVERT(DECIMAL(18,4), ISNULL(d.CantidadMpKg, 0))) AS Requerido,
        MIN(d.Renglon) AS Orden
    FROM dbo.SolicitudesProduccion s
    INNER JOIN dbo.SolicitudesProduccionDetalle d
        ON d.SolicitudProduccionID = s.SolicitudProduccionID
       AND d.Activo = 1
    LEFT JOIN dbo.ERP_Materiales m
        ON UPPER(LTRIM(RTRIM(m.Codigo))) = UPPER(LTRIM(RTRIM(d.MaterialCodigo)))
       AND m.Activo = 1
    WHERE s.SolicitudProduccionID IN ({inSql})
      AND s.Activo = 1
      AND
      (
          NULLIF(LTRIM(RTRIM(d.MaterialCodigo)), '') IS NOT NULL
          OR NULLIF(LTRIM(RTRIM(d.MaterialDescripcion)), '') IS NOT NULL
      )
    GROUP BY s.SolicitudProduccionID, m.MaterialID, d.MaterialCodigo, m.UnidadDefault
)
SELECT
    x.SolicitudProduccionID, x.CatalogoID, x.Codigo, x.Descripcion, x.Unidad, x.Requerido,
    CONVERT
    (
        DECIMAL(18,4),
        ISNULL
        (
            (
                SELECT SUM
                (
                    CASE
                        WHEN mm.TipoMovimiento IN (N'Salida', N'Consumo') THEN mm.Cantidad
                        WHEN mm.TipoMovimiento = N'Retorno' THEN -mm.Cantidad
                        ELSE 0
                    END
                )
                FROM dbo.AlmacenMP_Movimientos mm
                INNER JOIN dbo.SolicitudesProduccion so
                    ON so.SolicitudProduccionID = x.SolicitudProduccionID
                WHERE mm.Activo = 1
                  AND COALESCE(mm.MaterialSolicitadoID, mm.MaterialID) = x.CatalogoID
                  AND
                  (
                      mm.SolicitudProduccionID = x.SolicitudProduccionID
                      OR
                      (
                          mm.SolicitudProduccionID IS NULL
                          AND
                          (
                              (NULLIF(LTRIM(RTRIM(so.FolioSolicitud)), '') IS NOT NULL
                               AND LTRIM(RTRIM(mm.NumeroOF)) = LTRIM(RTRIM(so.FolioSolicitud)))
                              OR
                              (NULLIF(LTRIM(RTRIM(so.NumeroOFRecibida)), '') IS NOT NULL
                               AND LTRIM(RTRIM(mm.NumeroOF)) = LTRIM(RTRIM(so.NumeroOFRecibida)))
                          )
                      )
                  )
            ),
            0
        )
    ) AS Entregado
FROM Materiales x
ORDER BY x.SolicitudProduccionID, x.Orden;

WITH Embalajes AS
(
    SELECT
        s.SolicitudProduccionID,
        ISNULL(e.EmbalajeID, 0) AS CatalogoID,
        COALESCE(NULLIF(LTRIM(RTRIM(d.EmbalajeCodigo)), ''), 'SIN-CODIGO') AS Codigo,
        MAX(ISNULL(d.EmbalajeDescripcion, '')) AS Descripcion,
        COALESCE(NULLIF(LTRIM(RTRIM(e.UnidadDefault)), ''), 'PZS') AS Unidad,
        SUM(CONVERT(DECIMAL(18,4), ISNULL(d.CantidadEmbalajes, 0))) AS Requerido,
        MIN(d.Renglon) AS Orden
    FROM dbo.SolicitudesProduccion s
    INNER JOIN dbo.SolicitudesProduccionDetalle d
        ON d.SolicitudProduccionID = s.SolicitudProduccionID
       AND d.Activo = 1
    LEFT JOIN dbo.ERP_Embalajes e
        ON UPPER(LTRIM(RTRIM(e.Codigo))) = UPPER(LTRIM(RTRIM(d.EmbalajeCodigo)))
       AND e.Activo = 1
    WHERE s.SolicitudProduccionID IN ({inSql})
      AND s.Activo = 1
      AND
      (
          NULLIF(LTRIM(RTRIM(d.EmbalajeCodigo)), '') IS NOT NULL
          OR NULLIF(LTRIM(RTRIM(d.EmbalajeDescripcion)), '') IS NOT NULL
      )
    GROUP BY s.SolicitudProduccionID, e.EmbalajeID, d.EmbalajeCodigo, e.UnidadDefault
)
SELECT
    x.SolicitudProduccionID, x.CatalogoID, x.Codigo, x.Descripcion, x.Unidad, x.Requerido,
    CONVERT
    (
        DECIMAL(18,4),
        ISNULL
        (
            (
                SELECT SUM
                (
                    CASE
                        WHEN mm.TipoMovimiento IN (N'Salida', N'Consumo') THEN mm.Cantidad
                        WHEN mm.TipoMovimiento = N'Retorno' THEN -mm.Cantidad
                        ELSE 0
                    END
                )
                FROM dbo.AlmacenEmbalajes_Movimientos mm
                INNER JOIN dbo.SolicitudesProduccion so
                    ON so.SolicitudProduccionID = x.SolicitudProduccionID
                WHERE mm.Activo = 1
                  AND COALESCE(mm.EmbalajeSolicitadoID, mm.EmbalajeID) = x.CatalogoID
                  AND
                  (
                      mm.SolicitudProduccionID = x.SolicitudProduccionID
                      OR
                      (
                          mm.SolicitudProduccionID IS NULL
                          AND
                          (
                              (NULLIF(LTRIM(RTRIM(so.FolioSolicitud)), '') IS NOT NULL
                               AND LTRIM(RTRIM(mm.NumeroOF)) = LTRIM(RTRIM(so.FolioSolicitud)))
                              OR
                              (NULLIF(LTRIM(RTRIM(so.NumeroOFRecibida)), '') IS NOT NULL
                               AND LTRIM(RTRIM(mm.NumeroOF)) = LTRIM(RTRIM(so.NumeroOFRecibida)))
                          )
                      )
                  )
            ),
            0
        )
    ) AS Entregado
FROM Embalajes x
ORDER BY x.SolicitudProduccionID, x.Orden;

WITH Partes AS
(
    SELECT
        s.SolicitudProduccionID,
        p.ParteID AS CatalogoID,
        p.NumeroParte AS Codigo,
        MAX
        (
            COALESCE
            (
                NULLIF(LTRIM(RTRIM(p.Designacion)), ''),
                NULLIF(LTRIM(RTRIM(p.Descripcion)), ''),
                NULLIF
                (
                    LTRIM
                    (
                        RTRIM
                        (
                            d.DesignacionDescripcionSAP
                        )
                    ),
                    ''
                ),
                ''
            )
        ) AS Descripcion,
        N'PZS' AS Unidad,
        SUM
        (
            CONVERT
            (
                DECIMAL(18,4),
                ISNULL(d.CantidadPiezas, 0)
            )
        ) AS Requerido,
        MIN(d.Renglon) AS Orden
    FROM dbo.SolicitudesProduccion s
    INNER JOIN dbo.SolicitudesProduccionDetalle d
        ON d.SolicitudProduccionID =
           s.SolicitudProduccionID
       AND d.Activo = 1
       AND ISNULL(d.CantidadPiezas, 0) > 0
    INNER JOIN dbo.ERP_Partes p
        ON p.ParteID = d.ParteID
       AND p.Activo = 1
    WHERE s.SolicitudProduccionID IN ({inSql})
      AND s.Activo = 1
    GROUP BY
        s.SolicitudProduccionID,
        p.ParteID,
        p.NumeroParte
)
SELECT
    x.SolicitudProduccionID,
    x.CatalogoID,
    x.Codigo,
    x.Descripcion,
    x.Unidad,
    x.Requerido,
    CONVERT
    (
        DECIMAL(18,4),
        ISNULL
        (
            (
                SELECT SUM
                (
                    CASE
                        WHEN movimiento.TipoMovimiento =
                             N'Entrada'
                            THEN movimiento.Cantidad
                        ELSE 0
                    END
                )
                FROM dbo.AlmacenPT_Movimientos movimiento
                INNER JOIN dbo.SolicitudesProduccion so
                    ON so.SolicitudProduccionID =
                       x.SolicitudProduccionID
                WHERE movimiento.Activo = 1
                  AND movimiento.ParteID =
                      x.CatalogoID
                  AND
                  (
                      (
                          NULLIF
                          (
                              LTRIM
                              (
                                  RTRIM
                                  (
                                      so.FolioSolicitud
                                  )
                              ),
                              ''
                          ) IS NOT NULL
                          AND LTRIM
                              (
                                  RTRIM
                                  (
                                      movimiento.NumeroOF
                                  )
                              ) =
                              LTRIM
                              (
                                  RTRIM
                                  (
                                      so.FolioSolicitud
                                  )
                              )
                      )
                      OR
                      (
                          NULLIF
                          (
                              LTRIM
                              (
                                  RTRIM
                                  (
                                      so.NumeroOFRecibida
                                  )
                              ),
                              ''
                          ) IS NOT NULL
                          AND LTRIM
                              (
                                  RTRIM
                                  (
                                      movimiento.NumeroOF
                                  )
                              ) =
                              LTRIM
                              (
                                  RTRIM
                                  (
                                      so.NumeroOFRecibida
                                  )
                              )
                      )
                  )
            ),
            0
        )
    ) AS Entregado
FROM Partes x
ORDER BY
    x.SolicitudProduccionID,
    x.Orden;
;";

        await using var command = new SqlCommand(sql, connection);
        foreach (var parametro in parametros)
            command.Parameters.Add(parametro.Nombre, SqlDbType.Int).Value = parametro.SolicitudProduccionID;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await LeerEntregablesAsync(reader, ordenes, tipo: "MP", cancellationToken);

        if (await reader.NextResultAsync(cancellationToken))
            await LeerEntregablesAsync(reader, ordenes, tipo: "EMBALAJE", cancellationToken);

        if (await reader.NextResultAsync(cancellationToken))
            await LeerEntregablesAsync(reader, ordenes, tipo: "PT", cancellationToken);
    }

    private static async Task LeerEntregablesAsync(
        SqlDataReader reader,
        List<AlmacenOFItemVm> ordenes,
        string tipo,
        CancellationToken cancellationToken)
    {
        while (await reader.ReadAsync(cancellationToken))
        {
            var solicitudID = Entero(reader, "SolicitudProduccionID");
            var orden = ordenes.FirstOrDefault(x => x.SolicitudProduccionID == solicitudID);
            if (orden == null) continue;

            var item = new AlmacenOFEntregableVm
            {
                SolicitudProduccionID = solicitudID,
                CatalogoID = Entero(reader, "CatalogoID"),
                Codigo = Texto(reader, "Codigo"),
                Descripcion = Texto(reader, "Descripcion"),
                Unidad = Texto(reader, "Unidad"),
                Requerido = DecimalValor(reader, "Requerido"),
                Entregado = DecimalValor(reader, "Entregado")
            };

            if (tipo == "MP")
                orden.MaterialesEntrega.Add(item);
            else if (tipo == "EMBALAJE")
                orden.EmbalajesEntrega.Add(item);
            else if (tipo == "PT")
                orden.PartesEntrega.Add(item);
        }
    }
}


