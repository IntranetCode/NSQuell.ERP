using ERP.NSQuell.Models.ViewModels.Almacen;
using ERP.NSQuell.Servicios.Almacen;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Text;

namespace ERP.NSQuell.Controllers;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AlmacenEmbalajesController : AlmacenBaseController
{
    private static readonly string[] TiposPermitidos =
    {
        "Entrada", "Salida", "Retorno", "Consumo", "Scrap", "AjustePositivo", "AjusteNegativo"
    };

    public AlmacenEmbalajesController(IConfiguration configuration) : base(configuration) { }
    [HttpGet]
    public async Task<IActionResult> Index(
        string? q,
        string? estado,
        CancellationToken cancellationToken)
    {
        var sesion = ValidarSesion();
        if (sesion != null)
            return sesion;

        var vm = new AlmacenMPIndexVm
        {
            Busqueda = q?.Trim(),
            Estado = estado?.Trim().ToUpperInvariant()
        };

        await using var connection =
            await AbrirConexionAsync(cancellationToken);

        if (!await ExisteObjetoAsync(
                connection,
                "dbo.ERP_Embalajes",
                "U",
                cancellationToken)
            || !await ExisteObjetoAsync(
                connection,
                "dbo.AlmacenEmbalajes_Movimientos",
                "U",
                cancellationToken)
            || !await ExisteObjetoAsync(
                connection,
                "dbo.vw_AlmacenEmbalajesInventario",
                "V",
                cancellationToken)
            || !await ExisteColumnaAsync(
                connection,
                "dbo.vw_AlmacenEmbalajesInventario",
                "StockConfigurado",
                cancellationToken))
        {
            vm.Configurado = false;
            vm.MensajeConfiguracion =
                "No está completa la instalación del almacén de embalajes.";

            return View(vm);
        }

        // SOLICITADO EMBALAJE:
        // requerido por Planeación menos lo ya entregado a la misma OF.
        const string sql = @"
WITH Requerido AS
(
    SELECT
        s.SolicitudProduccionID,
        s.FolioSolicitud,
        s.NumeroOFRecibida,
        embalaje.EmbalajeID,
        SUM
        (
            CONVERT
            (
                DECIMAL(18,4),
                ISNULL(detalle.CantidadEmbalajes, 0)
            )
        ) AS CantidadRequerida
    FROM dbo.SolicitudesProduccion s
    INNER JOIN dbo.SolicitudesProduccionDetalle detalle
        ON detalle.SolicitudProduccionID =
           s.SolicitudProduccionID
       AND detalle.Activo = 1
    INNER JOIN dbo.ERP_Embalajes embalaje
        ON embalaje.Activo = 1
       AND UPPER
           (
               LTRIM
               (
                   RTRIM
                   (
                       ISNULL(detalle.EmbalajeCodigo, N'')
                   )
               )
           ) =
           UPPER(LTRIM(RTRIM(embalaje.Codigo)))
    WHERE s.Activo = 1
      AND ISNULL(detalle.CantidadEmbalajes, 0) > 0
    GROUP BY
        s.SolicitudProduccionID,
        s.FolioSolicitud,
        s.NumeroOFRecibida,
        embalaje.EmbalajeID
),
PendientePorEmbalaje AS
(
    SELECT
        requerido.EmbalajeID,
        SUM
        (
            CASE
                WHEN requerido.CantidadRequerida
                     - ISNULL(entregado.CantidadEntregada, 0) > 0
                    THEN requerido.CantidadRequerida
                         - ISNULL(entregado.CantidadEntregada, 0)
                ELSE 0
            END
        ) AS Solicitado
    FROM Requerido requerido
    OUTER APPLY
    (
        SELECT
            SUM
            (
                CASE
                    WHEN movimiento.TipoMovimiento IN
                         (
                             N'Salida',
                             N'Consumo'
                         )
                        THEN movimiento.Cantidad
                    WHEN movimiento.TipoMovimiento = N'Retorno'
                        THEN -movimiento.Cantidad
                    ELSE 0
                END
            ) AS CantidadEntregada
        FROM dbo.AlmacenEmbalajes_Movimientos movimiento
        WHERE movimiento.Activo = 1
          AND movimiento.EmbalajeID =
              requerido.EmbalajeID
          AND
          (
              (
                  NULLIF
                  (
                      LTRIM(RTRIM(requerido.FolioSolicitud)),
                      N''
                  ) IS NOT NULL
                  AND LTRIM(RTRIM(movimiento.NumeroOF)) =
                      LTRIM(RTRIM(requerido.FolioSolicitud))
              )
              OR
              (
                  NULLIF
                  (
                      LTRIM(RTRIM(requerido.NumeroOFRecibida)),
                      N''
                  ) IS NOT NULL
                  AND LTRIM(RTRIM(movimiento.NumeroOF)) =
                      LTRIM(RTRIM(requerido.NumeroOFRecibida))
              )
          )
    ) entregado
    GROUP BY requerido.EmbalajeID
)
SELECT TOP (500)
    inventario.EmbalajeID AS MaterialID,
    inventario.Codigo,
    inventario.Nombre,
    inventario.Unidad,
    inventario.Entradas,
    inventario.Salidas,
    inventario.Saldo,
    CONVERT
    (
        DECIMAL(18,4),
        ISNULL(pendiente.Solicitado, 0)
    ) AS Solicitado,
    inventario.StockMinimo,
    inventario.StockAviso,
    inventario.StockConfigurado,
    inventario.Semaforo,
    inventario.UltimoMovimiento
FROM dbo.vw_AlmacenEmbalajesInventario inventario
LEFT JOIN PendientePorEmbalaje pendiente
    ON pendiente.EmbalajeID =
       inventario.EmbalajeID
WHERE
    (
        @Q IS NULL
        OR inventario.Codigo LIKE N'%' + @Q + N'%'
        OR inventario.Nombre LIKE N'%' + @Q + N'%'
    )
    AND
    (
        @Estado IS NULL
        OR inventario.Semaforo = @Estado
    )
ORDER BY
    CASE inventario.Semaforo
        WHEN N'SIN_CONFIGURAR' THEN 0
        WHEN N'ROJO' THEN 1
        WHEN N'AMARILLO' THEN 2
        ELSE 3
    END,
    inventario.Nombre;";

        await using (var command =
            new SqlCommand(sql, connection))
        {
            command.Parameters.Add(
                "@Q",
                SqlDbType.NVarChar,
                250).Value =
                string.IsNullOrWhiteSpace(vm.Busqueda)
                    ? DBNull.Value
                    : vm.Busqueda;

            command.Parameters.Add(
                "@Estado",
                SqlDbType.NVarChar,
                20).Value =
                string.IsNullOrWhiteSpace(vm.Estado)
                    ? DBNull.Value
                    : vm.Estado;

            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                vm.Existencias.Add(
                    new AlmacenMPExistenciaVm
                    {
                        MaterialID =
                            Entero(reader, "MaterialID"),
                        Codigo =
                            Texto(reader, "Codigo"),
                        Nombre =
                            Texto(reader, "Nombre"),
                        Unidad =
                            Texto(reader, "Unidad"),
                        Entradas =
                            DecimalValor(reader, "Entradas"),
                        Salidas =
                            DecimalValor(reader, "Salidas"),
                        Saldo =
                            DecimalValor(reader, "Saldo"),
                        Solicitado =
                            DecimalValor(reader, "Solicitado"),
                        StockMinimo =
                            DecimalValor(reader, "StockMinimo"),
                        StockAviso =
                            DecimalValor(reader, "StockAviso"),
                        Semaforo =
                            Texto(reader, "Semaforo"),
                        StockConfigurado =
                            Convert.ToBoolean(
                                reader["StockConfigurado"]),
                        UltimoMovimiento =
                            Fecha(reader, "UltimoMovimiento")
                    });
            }
        }

        const string movimientosSql = @"
SELECT TOP (5)
    movimiento.MovimientoID,
    movimiento.FechaMovimiento,
    movimiento.EmbalajeID AS MaterialID,
    embalaje.Codigo,
    embalaje.Nombre AS Material,
    movimiento.TipoMovimiento,
    movimiento.Cantidad,
    movimiento.Unidad,
    movimiento.Lote,
    CONCAT
    (
        ubicacion.Almacen,
        N' / ',
        ubicacion.Rack,
        CASE
            WHEN ubicacion.Nivel IS NULL
                THEN N''
            ELSE N' / ' + ubicacion.Nivel
        END,
        CASE
            WHEN ubicacion.Posicion IS NULL
                THEN N''
            ELSE N' / ' + ubicacion.Posicion
        END
    ) AS Ubicacion,
    ISNULL(movimiento.NumeroOF, N'') AS NumeroOF,
    COALESCE
    (
        movimiento.EntregadoPorNombre,
        persona.Nombre + N' '
            + ISNULL(persona.ApellidoPaterno, N''),
        movimiento.CreadoPor,
        N''
    ) AS Responsable,
    ISNULL(movimiento.Seguimiento, N'') AS Observaciones,
    ISNULL
    (
        movimiento.ReferenciaOperacion,
        N''
    ) AS ReferenciaOperacion
FROM dbo.AlmacenEmbalajes_Movimientos movimiento
INNER JOIN dbo.ERP_Embalajes embalaje
    ON embalaje.EmbalajeID =
       movimiento.EmbalajeID
LEFT JOIN dbo.ERP_Ubicaciones ubicacion
    ON ubicacion.UbicacionID =
       movimiento.UbicacionID
LEFT JOIN dbo.Usuarios usuario
    ON usuario.UsuarioID =
       movimiento.ResponsableUsuarioID
LEFT JOIN dbo.Persona persona
    ON persona.PersonaID =
       usuario.PersonaID
WHERE movimiento.Activo = 1
ORDER BY
    movimiento.FechaMovimiento DESC,
    movimiento.MovimientoID DESC;";

        await using (var command =
            new SqlCommand(movimientosSql, connection))
        await using (var reader =
            await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                vm.Movimientos.Add(
                    new AlmacenMPMovimientoListaVm
                    {
                        MovimientoID =
                            EnteroLargo(reader, "MovimientoID"),
                        FechaMovimiento =
                            Fecha(reader, "FechaMovimiento")
                            ?? DateTime.MinValue,
                        MaterialID =
                            Entero(reader, "MaterialID"),
                        Codigo =
                            Texto(reader, "Codigo"),
                        Material =
                            Texto(reader, "Material"),
                        TipoMovimiento =
                            Texto(reader, "TipoMovimiento"),
                        Cantidad =
                            DecimalValor(reader, "Cantidad"),
                        Unidad =
                            Texto(reader, "Unidad"),
                        Lote =
                            Texto(reader, "Lote"),
                        Ubicacion =
                            Texto(reader, "Ubicacion"),
                        NumeroOF =
                            Texto(reader, "NumeroOF"),
                        Responsable =
                            Texto(reader, "Responsable"),
                        Observaciones =
                            Texto(reader, "Observaciones"),
                        ReferenciaOperacion =
                            Texto(reader, "ReferenciaOperacion")
                    });
            }
        }

        // Misma estructura operativa utilizada en Inventario PT.
        const string resumenSql = @"
WITH Requerido AS
(
    SELECT
        s.SolicitudProduccionID,
        s.FolioSolicitud,
        s.NumeroOFRecibida,
        embalaje.EmbalajeID,
        SUM
        (
            CONVERT
            (
                DECIMAL(18,4),
                ISNULL(detalle.CantidadEmbalajes, 0)
            )
        ) AS CantidadRequerida
    FROM dbo.SolicitudesProduccion s
    INNER JOIN dbo.SolicitudesProduccionDetalle detalle
        ON detalle.SolicitudProduccionID =
           s.SolicitudProduccionID
       AND detalle.Activo = 1
    INNER JOIN dbo.ERP_Embalajes embalaje
        ON embalaje.Activo = 1
       AND UPPER
           (
               LTRIM
               (
                   RTRIM
                   (
                       ISNULL(detalle.EmbalajeCodigo, N'')
                   )
               )
           ) =
           UPPER(LTRIM(RTRIM(embalaje.Codigo)))
    WHERE s.Activo = 1
      AND ISNULL(detalle.CantidadEmbalajes, 0) > 0
    GROUP BY
        s.SolicitudProduccionID,
        s.FolioSolicitud,
        s.NumeroOFRecibida,
        embalaje.EmbalajeID
),
Pendiente AS
(
    SELECT
        requerido.SolicitudProduccionID,
        requerido.EmbalajeID,
        CASE
            WHEN requerido.CantidadRequerida
                 - ISNULL(entregado.CantidadEntregada, 0) > 0
                THEN requerido.CantidadRequerida
                     - ISNULL(entregado.CantidadEntregada, 0)
            ELSE 0
        END AS CantidadPendiente
    FROM Requerido requerido
    OUTER APPLY
    (
        SELECT
            SUM
            (
                CASE
                    WHEN movimiento.TipoMovimiento IN
                         (
                             N'Salida',
                             N'Consumo'
                         )
                        THEN movimiento.Cantidad
                    WHEN movimiento.TipoMovimiento = N'Retorno'
                        THEN -movimiento.Cantidad
                    ELSE 0
                END
            ) AS CantidadEntregada
        FROM dbo.AlmacenEmbalajes_Movimientos movimiento
        WHERE movimiento.Activo = 1
          AND movimiento.EmbalajeID =
              requerido.EmbalajeID
          AND
          (
              (
                  NULLIF
                  (
                      LTRIM(RTRIM(requerido.FolioSolicitud)),
                      N''
                  ) IS NOT NULL
                  AND LTRIM(RTRIM(movimiento.NumeroOF)) =
                      LTRIM(RTRIM(requerido.FolioSolicitud))
              )
              OR
              (
                  NULLIF
                  (
                      LTRIM(RTRIM(requerido.NumeroOFRecibida)),
                      N''
                  ) IS NOT NULL
                  AND LTRIM(RTRIM(movimiento.NumeroOF)) =
                      LTRIM(RTRIM(requerido.NumeroOFRecibida))
              )
          )
    ) entregado
)
SELECT
    CONVERT
    (
        DECIMAL(18,4),
        ISNULL
        (
            SUM(CantidadPendiente),
            0
        )
    ) AS SolicitadoPendiente,
    CONVERT
    (
        INT,
        COUNT(DISTINCT SolicitudProduccionID)
    ) AS OFPendientes
FROM Pendiente
WHERE CantidadPendiente > 0.0005;

SELECT
    CONVERT
    (
        INT,
        COUNT
        (
            CASE
                WHEN TipoMovimiento = N'Entrada'
                    THEN 1
                ELSE NULL
            END
        )
    ) AS RecepcionesHoy,
    CONVERT
    (
        DECIMAL(18,4),
        ISNULL
        (
            SUM
            (
                CASE
                    WHEN TipoMovimiento = N'Entrada'
                        THEN Cantidad
                    ELSE 0
                END
            ),
            0
        )
    ) AS CantidadRecibidaHoy,
    CONVERT
    (
        DECIMAL(18,4),
        ISNULL
        (
            SUM
            (
                CASE
                    WHEN TipoMovimiento IN
                         (
                             N'Salida',
                             N'Consumo',
                             N'Scrap',
                             N'AjusteNegativo'
                         )
                        THEN Cantidad
                    ELSE 0
                END
            ),
            0
        )
    ) AS SalidasHoy
FROM dbo.AlmacenEmbalajes_Movimientos
WHERE Activo = 1
  AND FechaMovimiento >= CONVERT(DATE, GETDATE())
  AND FechaMovimiento <
      DATEADD
      (
          DAY,
          1,
          CONVERT(DATE, GETDATE())
      );";

        await using (var command =
            new SqlCommand(resumenSql, connection))
        await using (var reader =
            await command.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                vm.SolicitadoPendiente =
                    DecimalValor(
                        reader,
                        "SolicitadoPendiente");

                vm.OFPendientes =
                    Entero(reader, "OFPendientes");
            }

            if (await reader.NextResultAsync(cancellationToken)
                && await reader.ReadAsync(cancellationToken))
            {
                vm.RecepcionesHoy =
                    Entero(reader, "RecepcionesHoy");

                vm.CantidadRecibidaHoy =
                    DecimalValor(
                        reader,
                        "CantidadRecibidaHoy");

                vm.SalidasHoy =
                    DecimalValor(reader, "SalidasHoy");
            }
        }

        vm.TotalMateriales =
            vm.Existencias.Count;

        vm.Criticos =
            vm.Existencias.Count(
                item => item.Semaforo == "ROJO");

        vm.Advertencias =
            vm.Existencias.Count(
                item => item.Semaforo == "AMARILLO");

        vm.Disponibles =
            vm.Existencias.Count(
                item => item.Semaforo == "VERDE");

        vm.PendientesConfiguracion =
            vm.Existencias.Count(
                item => !item.StockConfigurado);

        vm.SaldoTotal =
            vm.Existencias.Sum(item => item.Saldo);

        return View(vm);
    }
[HttpGet]
    public async Task<IActionResult> Historial(string? embalaje, string? q, string? tipo,
        string? numeroOF, string? responsable, string? lote, DateTime? desde, DateTime? hasta,
        int pagina = 1, CancellationToken cancellationToken = default)
    {
        var sesion = ValidarSesion();
        if (sesion != null) return sesion;
        if (desde.HasValue && hasta.HasValue && hasta.Value.Date < desde.Value.Date)
            (desde, hasta) = (hasta, desde);

        var vm = new AlmacenMPHistorialVm
        {
            FiltroMaterial = embalaje?.Trim(), Busqueda = q?.Trim(),
            TipoMovimiento = TiposPermitidos.Contains(tipo ?? string.Empty) ? tipo : null,
            NumeroOF = numeroOF?.Trim(), Responsable = responsable?.Trim(), Lote = lote?.Trim(),
            Desde = desde?.Date, Hasta = hasta?.Date, Pagina = Math.Max(1, pagina), TamanoPagina = 50,
            TiposMovimiento = TiposPermitidos.ToList()
        };

        await using var connection = await AbrirConexionAsync(cancellationToken);
        if (!await ExisteObjetoAsync(connection, "dbo.AlmacenEmbalajes_Movimientos", "U", cancellationToken))
        {
            Mensaje("warning", "No existe la estructura del almacén de embalajes.");
            return RedirectToAction(nameof(Index));
        }
        await CargarFiltrosHistorialAsync(connection, vm, cancellationToken);

        const string fromWhere = @"
FROM dbo.AlmacenEmbalajes_Movimientos mm
INNER JOIN dbo.ERP_Embalajes e ON e.EmbalajeID = mm.EmbalajeID
LEFT JOIN dbo.ERP_Ubicaciones u ON u.UbicacionID = mm.UbicacionID
LEFT JOIN dbo.Usuarios us ON us.UsuarioID = mm.ResponsableUsuarioID
LEFT JOIN dbo.Persona p ON p.PersonaID = us.PersonaID
WHERE mm.Activo = 1
  AND (@Embalaje IS NULL OR e.Codigo = @Embalaje OR e.Codigo LIKE '%' + @Embalaje + '%' OR e.Nombre LIKE '%' + @Embalaje + '%')
  AND (@Q IS NULL OR ISNULL(mm.Seguimiento,'') LIKE '%' + @Q + '%' OR ISNULL(mm.ReferenciaOperacion,'') LIKE '%' + @Q + '%')
  AND (@Tipo IS NULL OR mm.TipoMovimiento = @Tipo)
  AND (@NumeroOF IS NULL OR mm.NumeroOF LIKE '%' + @NumeroOF + '%')
  AND (@Responsable IS NULL OR COALESCE(NULLIF(LTRIM(RTRIM(mm.EntregadoPorNombre)),''), NULLIF(LTRIM(RTRIM(CONCAT(p.Nombre,' ',p.ApellidoPaterno))),''), mm.CreadoPor, '') LIKE '%' + @Responsable + '%')
  AND (@Lote IS NULL OR mm.Lote LIKE '%' + @Lote + '%')
  AND (@Desde IS NULL OR mm.FechaMovimiento >= @Desde)
  AND (@Hasta IS NULL OR mm.FechaMovimiento < DATEADD(DAY,1,@Hasta))";

        await using (var count = new SqlCommand("SELECT COUNT_BIG(1) " + fromWhere, connection))
        {
            AgregarParametrosHistorial(count, vm);
            vm.TotalRegistros = Convert.ToInt32(await count.ExecuteScalarAsync(cancellationToken) ?? 0L);
        }
        vm.Pagina = Math.Min(vm.Pagina, vm.TotalPaginas);

        var dataSql = @"
SELECT mm.MovimientoID, mm.FechaMovimiento, mm.EmbalajeID AS MaterialID,
    e.Codigo, e.Nombre AS Material, mm.TipoMovimiento, mm.Cantidad, mm.Unidad, mm.Lote,
    CONCAT(u.Almacen, ' / ', u.Rack,
        CASE WHEN u.Nivel IS NULL THEN '' ELSE ' / ' + u.Nivel END,
        CASE WHEN u.Posicion IS NULL THEN '' ELSE ' / ' + u.Posicion END) AS Ubicacion,
    ISNULL(mm.NumeroOF,'') AS NumeroOF,
    COALESCE(NULLIF(LTRIM(RTRIM(mm.EntregadoPorNombre)),''), NULLIF(LTRIM(RTRIM(CONCAT(p.Nombre,' ',p.ApellidoPaterno))),''), mm.CreadoPor, '') AS Responsable,
    ISNULL(mm.Seguimiento,'') AS Observaciones,
    ISNULL(mm.ReferenciaOperacion,'') AS ReferenciaOperacion " + fromWhere + @"
ORDER BY mm.FechaMovimiento DESC, mm.MovimientoID DESC
OFFSET @Offset ROWS FETCH NEXT @Tamano ROWS ONLY;";

        await using var command = new SqlCommand(dataSql, connection);
        AgregarParametrosHistorial(command, vm);
        command.Parameters.Add("@Offset", SqlDbType.Int).Value = (vm.Pagina - 1) * vm.TamanoPagina;
        command.Parameters.Add("@Tamano", SqlDbType.Int).Value = vm.TamanoPagina;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) vm.Movimientos.Add(LeerMovimiento(reader));
        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> ExportarHistorialCsv(string? embalaje, string? q, string? tipo,
        string? numeroOF, string? responsable, string? lote, DateTime? desde, DateTime? hasta,
        CancellationToken cancellationToken = default)
    {
        var sesion = ValidarSesion();
        if (sesion != null) return sesion;
        if (desde.HasValue && hasta.HasValue && hasta.Value.Date < desde.Value.Date)
            (desde, hasta) = (hasta, desde);
        var filtro = new AlmacenMPHistorialVm
        {
            FiltroMaterial = embalaje?.Trim(), Busqueda = q?.Trim(),
            TipoMovimiento = TiposPermitidos.Contains(tipo ?? string.Empty) ? tipo : null,
            NumeroOF = numeroOF?.Trim(), Responsable = responsable?.Trim(), Lote = lote?.Trim(),
            Desde = desde?.Date, Hasta = hasta?.Date
        };

        await using var connection = await AbrirConexionAsync(cancellationToken);
        const string sql = @"
SELECT TOP (10000) mm.MovimientoID, mm.FechaMovimiento, mm.EmbalajeID AS MaterialID,
    e.Codigo, e.Nombre AS Material, mm.TipoMovimiento, mm.Cantidad, mm.Unidad, mm.Lote,
    CONCAT(u.Almacen, ' / ', u.Rack,
        CASE WHEN u.Nivel IS NULL THEN '' ELSE ' / ' + u.Nivel END,
        CASE WHEN u.Posicion IS NULL THEN '' ELSE ' / ' + u.Posicion END) AS Ubicacion,
    ISNULL(mm.NumeroOF,'') AS NumeroOF,
    COALESCE(NULLIF(LTRIM(RTRIM(mm.EntregadoPorNombre)),''), NULLIF(LTRIM(RTRIM(CONCAT(p.Nombre,' ',p.ApellidoPaterno))),''), mm.CreadoPor, '') AS Responsable,
    ISNULL(mm.Seguimiento,'') AS Observaciones,
    ISNULL(mm.ReferenciaOperacion,'') AS ReferenciaOperacion
FROM dbo.AlmacenEmbalajes_Movimientos mm
INNER JOIN dbo.ERP_Embalajes e ON e.EmbalajeID=mm.EmbalajeID
LEFT JOIN dbo.ERP_Ubicaciones u ON u.UbicacionID=mm.UbicacionID
LEFT JOIN dbo.Usuarios us ON us.UsuarioID=mm.ResponsableUsuarioID
LEFT JOIN dbo.Persona p ON p.PersonaID=us.PersonaID
WHERE mm.Activo=1
  AND (@Embalaje IS NULL OR e.Codigo=@Embalaje OR e.Codigo LIKE '%' + @Embalaje + '%' OR e.Nombre LIKE '%' + @Embalaje + '%')
  AND (@Q IS NULL OR ISNULL(mm.Seguimiento,'') LIKE '%' + @Q + '%' OR ISNULL(mm.ReferenciaOperacion,'') LIKE '%' + @Q + '%')
  AND (@Tipo IS NULL OR mm.TipoMovimiento=@Tipo)
  AND (@NumeroOF IS NULL OR mm.NumeroOF LIKE '%' + @NumeroOF + '%')
  AND (@Responsable IS NULL OR COALESCE(NULLIF(LTRIM(RTRIM(mm.EntregadoPorNombre)),''),NULLIF(LTRIM(RTRIM(CONCAT(p.Nombre,' ',p.ApellidoPaterno))),''),mm.CreadoPor,'') LIKE '%' + @Responsable + '%')
  AND (@Lote IS NULL OR mm.Lote LIKE '%' + @Lote + '%')
  AND (@Desde IS NULL OR mm.FechaMovimiento>=@Desde)
  AND (@Hasta IS NULL OR mm.FechaMovimiento<DATEADD(DAY,1,@Hasta))
ORDER BY mm.FechaMovimiento DESC,mm.MovimientoID DESC;";
        await using var command = new SqlCommand(sql, connection);
        AgregarParametrosHistorial(command, filtro);
        var csv = new StringBuilder();
        csv.AppendLine("MovimientoID;Fecha;Codigo;Embalaje;Tipo;Cantidad;Unidad;Lote;Ubicacion;OF;Responsable;Referencia;Observaciones");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var x = LeerMovimiento(reader);
            csv.AppendLine(string.Join(";", new[]
            {
                Csv(x.MovimientoID.ToString()), Csv(x.FechaMovimiento.ToString("dd/MM/yyyy HH:mm:ss")),
                Csv(x.Codigo), Csv(x.Material), Csv(x.TipoMovimiento),
                Csv(x.Cantidad.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)),
                Csv(x.Unidad), Csv(x.Lote), Csv(x.Ubicacion), Csv(x.NumeroOF),
                Csv(x.Responsable), Csv(x.ReferenciaOperacion), Csv(x.Observaciones)
            }));
        }
        var encoding = new UTF8Encoding(true);
        return File(encoding.GetPreamble().Concat(encoding.GetBytes(csv.ToString())).ToArray(),
            "text/csv; charset=utf-8", $"Historial_Embalajes_{DateTime.Now:yyyyMMdd_HHmm}.csv");
    }

    [HttpGet]
    public async Task<IActionResult> Embalajes(CancellationToken cancellationToken)
    {
        var sesion = ValidarSesion();
        if (sesion != null) return sesion;
        var rows = new List<AlmacenMaterialFormVm>();
        await using var connection = await AbrirConexionAsync(cancellationToken);
        if (!await ExisteObjetoAsync(connection, "dbo.ERP_Embalajes", "U", cancellationToken))
        {
            Mensaje("warning", "Ejecuta el script de separación de embalajes.");
            return View(rows);
        }
        const string sql = @"
SELECT EmbalajeID,Codigo,Nombre,UnidadDefault,Proveedor,RequiereLote,StockMinimo,StockAviso,Activo
FROM dbo.ERP_Embalajes ORDER BY Activo DESC,Codigo;";
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new AlmacenMaterialFormVm
            {
                MaterialID=Entero(reader,"EmbalajeID"), Codigo=Texto(reader,"Codigo"), Nombre=Texto(reader,"Nombre"),
                UnidadDefault=Texto(reader,"UnidadDefault"), Proveedor=Texto(reader,"Proveedor"),
                RequiereLote=Convert.ToBoolean(reader["RequiereLote"]), StockMinimo=DecimalValor(reader,"StockMinimo"),
                StockAviso=DecimalValor(reader,"StockAviso"), Activo=Convert.ToBoolean(reader["Activo"])
            });
        }
        return View(rows);
    }

    [HttpGet]
    public async Task<IActionResult> Embalaje(int? id, CancellationToken cancellationToken)
    {
        var sesion = ValidarSesion();
        if (sesion != null) return sesion;
        if (!id.HasValue) return View(new AlmacenMaterialFormVm());
        await using var connection = await AbrirConexionAsync(cancellationToken);
        const string sql = @"
SELECT EmbalajeID,Codigo,Nombre,UnidadDefault,Proveedor,RequiereLote,StockMinimo,StockAviso,Activo
FROM dbo.ERP_Embalajes WHERE EmbalajeID=@Id;";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Id", SqlDbType.Int).Value = id.Value;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return NotFound();
        return View(new AlmacenMaterialFormVm
        {
            MaterialID=Entero(reader,"EmbalajeID"), Codigo=Texto(reader,"Codigo"), Nombre=Texto(reader,"Nombre"),
            UnidadDefault=Texto(reader,"UnidadDefault"), Proveedor=Texto(reader,"Proveedor"),
            RequiereLote=Convert.ToBoolean(reader["RequiereLote"]), StockMinimo=DecimalValor(reader,"StockMinimo"),
            StockAviso=DecimalValor(reader,"StockAviso"), Activo=Convert.ToBoolean(reader["Activo"])
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Embalaje(AlmacenMaterialFormVm model, CancellationToken cancellationToken)
    {
        var sesion = ValidarSesion();
        if (sesion != null) return sesion;
        model.Codigo=model.Codigo?.Trim()??string.Empty;
        model.Nombre=model.Nombre?.Trim()??string.Empty;
        model.UnidadDefault=model.UnidadDefault?.Trim().ToUpperInvariant()??string.Empty;
        if (model.StockAviso<model.StockMinimo)
            ModelState.AddModelError(nameof(model.StockAviso),"El nivel de aviso debe ser igual o mayor al stock mínimo.");
        if (!ModelState.IsValid) return View(model);

        await using var connection = await AbrirConexionAsync(cancellationToken);
        const string duplicateSql = @"
SELECT (SELECT COUNT(*) FROM dbo.ERP_Embalajes WHERE UPPER(Codigo)=UPPER(@Codigo) AND (@Id IS NULL OR EmbalajeID<>@Id))
     + (SELECT COUNT(*) FROM dbo.ERP_Materiales WHERE UPPER(Codigo)=UPPER(@Codigo));";
        await using (var duplicate = new SqlCommand(duplicateSql, connection))
        {
            duplicate.Parameters.Add("@Codigo",SqlDbType.NVarChar,80).Value=model.Codigo;
            duplicate.Parameters.Add("@Id",SqlDbType.Int).Value=model.MaterialID.HasValue?model.MaterialID.Value:DBNull.Value;
            if (Convert.ToInt32(await duplicate.ExecuteScalarAsync(cancellationToken))>0)
            {
                ModelState.AddModelError(nameof(model.Codigo),"El código ya existe en MP o en Embalajes.");
                return View(model);
            }
        }

        var sql = model.MaterialID.HasValue
            ? @"UPDATE dbo.ERP_Embalajes SET Codigo=@Codigo,Nombre=@Nombre,UnidadDefault=@Unidad,Proveedor=@Proveedor,
RequiereLote=@RequiereLote,StockMinimo=@Minimo,StockAviso=@Aviso,StockConfigurado=1,Activo=@Activo,
FechaModificacion=SYSUTCDATETIME(),ActualizadoPor=@Usuario WHERE EmbalajeID=@Id;"
            : @"INSERT dbo.ERP_Embalajes(Codigo,Nombre,UnidadDefault,Proveedor,RequiereLote,StockMinimo,StockAviso,
StockConfigurado,FechaCreacion,CreadoPor,Activo) VALUES(@Codigo,@Nombre,@Unidad,@Proveedor,@RequiereLote,@Minimo,@Aviso,
1,SYSUTCDATETIME(),@Usuario,@Activo);";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Codigo",SqlDbType.NVarChar,80).Value=model.Codigo;
        command.Parameters.Add("@Nombre",SqlDbType.NVarChar,250).Value=model.Nombre;
        command.Parameters.Add("@Unidad",SqlDbType.NVarChar,20).Value=model.UnidadDefault;
        command.Parameters.Add("@Proveedor",SqlDbType.NVarChar,200).Value=string.IsNullOrWhiteSpace(model.Proveedor)?DBNull.Value:model.Proveedor.Trim();
        command.Parameters.Add("@RequiereLote",SqlDbType.Bit).Value=model.RequiereLote;
        var minimo=command.Parameters.Add("@Minimo",SqlDbType.Decimal); minimo.Precision=18; minimo.Scale=3; minimo.Value=model.StockMinimo;
        var aviso=command.Parameters.Add("@Aviso",SqlDbType.Decimal); aviso.Precision=18; aviso.Scale=3; aviso.Value=model.StockAviso;
        command.Parameters.Add("@Activo",SqlDbType.Bit).Value=model.Activo;
        command.Parameters.Add("@Usuario",SqlDbType.NVarChar,120).Value=UsuarioNombre;
        command.Parameters.Add("@Id",SqlDbType.Int).Value=model.MaterialID.HasValue?model.MaterialID.Value:DBNull.Value;
        await command.ExecuteNonQueryAsync(cancellationToken);
        Mensaje("success",model.MaterialID.HasValue?"Embalaje actualizado.":"Embalaje registrado.");
        return RedirectToAction(nameof(Embalajes));
    }

    [HttpGet]
    public async Task<IActionResult> NivelesStock(string? q, bool soloSinConfigurar=false, CancellationToken cancellationToken=default)
    {
        var sesion=ValidarSesion();
        if (sesion!=null) return sesion;
        var vm=new AlmacenStockNivelesVm{Modulo="EMBALAJES",Busqueda=q?.Trim(),SoloSinConfigurar=soloSinConfigurar};
        await using var connection=await AbrirConexionAsync(cancellationToken);
        if (!await ExisteObjetoAsync(connection,"dbo.vw_AlmacenEmbalajesInventario","V",cancellationToken))
        {
            Mensaje("warning","Ejecuta el script 10 antes de configurar niveles de embalajes.");
            return RedirectToAction(nameof(Index));
        }
        const string sql=@"
SELECT TOP (100) EmbalajeID AS CatalogoID,Codigo,Nombre AS Descripcion,Unidad,Saldo AS Disponible,
StockMinimo,StockAviso,StockConfigurado FROM dbo.vw_AlmacenEmbalajesInventario
WHERE (@Q IS NULL OR Codigo LIKE '%' + @Q + '%' OR Nombre LIKE '%' + @Q + '%')
AND (@SoloPendientes=0 OR StockConfigurado=0)
ORDER BY CASE WHEN StockConfigurado=0 THEN 0 ELSE 1 END,Nombre;";
        await using var command=new SqlCommand(sql,connection);
        command.Parameters.Add("@Q",SqlDbType.NVarChar,250).Value=string.IsNullOrWhiteSpace(vm.Busqueda)?DBNull.Value:vm.Busqueda;
        command.Parameters.Add("@SoloPendientes",SqlDbType.Bit).Value=vm.SoloSinConfigurar;
        await using var reader=await command.ExecuteReaderAsync(cancellationToken);
        while(await reader.ReadAsync(cancellationToken))
        {
            vm.Items.Add(new AlmacenStockNivelItemVm
            {
                CatalogoID=Entero(reader,"CatalogoID"),Codigo=Texto(reader,"Codigo"),Descripcion=Texto(reader,"Descripcion"),
                Unidad=Texto(reader,"Unidad"),Disponible=DecimalValor(reader,"Disponible"),StockMinimo=DecimalValor(reader,"StockMinimo"),
                StockAviso=DecimalValor(reader,"StockAviso"),Configurado=Convert.ToBoolean(reader["StockConfigurado"])
            });
        }
        vm.Total=vm.Items.Count; vm.Configurados=vm.Items.Count(x=>x.Configurado); vm.Pendientes=vm.Items.Count(x=>!x.Configurado);
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> NivelesStock(AlmacenStockNivelesVm model,CancellationToken cancellationToken=default)
    {
        var sesion=ValidarSesion(); if(sesion!=null)return sesion;
        model.Modulo="EMBALAJES";
        if(model.Items==null||model.Items.Count==0){Mensaje("warning","No se recibieron embalajes para actualizar.");return RedirectToAction(nameof(NivelesStock));}
        for(var i=0;i<model.Items.Count;i++)
        {
            if(model.Items[i].StockMinimo<0)ModelState.AddModelError($"Items[{i}].StockMinimo","El stock mínimo no puede ser negativo.");
            if(model.Items[i].StockAviso<model.Items[i].StockMinimo)ModelState.AddModelError($"Items[{i}].StockAviso","El aviso debe ser igual o mayor al mínimo.");
        }
        if(!ModelState.IsValid){model.Total=model.Items.Count;model.Configurados=model.Items.Count(x=>x.Configurado);model.Pendientes=model.Total-model.Configurados;return View(model);}
        await using var connection=await AbrirConexionAsync(cancellationToken);
        await using var transaction=(SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            const string sql=@"UPDATE dbo.ERP_Embalajes SET StockMinimo=@Minimo,StockAviso=@Aviso,StockConfigurado=1,
FechaModificacion=SYSUTCDATETIME(),ActualizadoPor=@Usuario WHERE EmbalajeID=@Id AND Activo=1;";
            foreach(var item in model.Items)
            {
                await using var command=new SqlCommand(sql,connection,transaction);
                var minimo=command.Parameters.Add("@Minimo",SqlDbType.Decimal);minimo.Precision=18;minimo.Scale=3;minimo.Value=item.StockMinimo;
                var aviso=command.Parameters.Add("@Aviso",SqlDbType.Decimal);aviso.Precision=18;aviso.Scale=3;aviso.Value=item.StockAviso;
                command.Parameters.Add("@Usuario",SqlDbType.NVarChar,120).Value=UsuarioNombre;
                command.Parameters.Add("@Id",SqlDbType.Int).Value=item.CatalogoID;
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
        }
        catch{await transaction.RollbackAsync(cancellationToken);throw;}
        Mensaje("success",$"Se actualizaron los niveles de {model.Items.Count} embalajes.");
        return RedirectToAction(nameof(NivelesStock),new{q=model.Busqueda,soloSinConfigurar=model.SoloSinConfigurar});
    }

    [HttpGet]
    public async Task<IActionResult> Movimiento(
        int? embalajeId,
        string? tipo,
        string? numeroOF,
        decimal? cantidad,
        string? unidad,
        int? solicitudProduccionId,
        bool entregaOF = false,
        CancellationToken cancellationToken = default)
    {
        var sesion = ValidarSesion();
        if (sesion != null) return sesion;

        var vm = new AlmacenMPMovimientoFormVm
        {
            MaterialID = embalajeId.GetValueOrDefault(),
            TipoMovimiento = entregaOF ? "Salida" : TiposPermitidos.Contains(tipo ?? string.Empty) ? tipo! : "Entrada",
            NumeroOF = entregaOF ? null : numeroOF?.Trim(),
            Cantidad = entregaOF ? 0m : cantidad.GetValueOrDefault(),
            Unidad = entregaOF ? string.Empty : unidad?.Trim().ToUpperInvariant() ?? string.Empty,
            EsEntregaOF = entregaOF,
            SolicitudProduccionID = solicitudProduccionId,
            FechaMovimiento = DateTime.Now,
            Lote = "S/L",
            OperacionToken = AlmacenOFEntregaService.CrearToken()
        };

        if (entregaOF)
        {
            if (!solicitudProduccionId.HasValue || solicitudProduccionId.Value <= 0 || vm.MaterialID <= 0)
            {
                Mensaje("warning", "No se recibió una OF y un embalaje válidos.");
                return RedirectToAction("Index", "AlmacenOF");
            }

            await using var connection = await AbrirConexionAsync(cancellationToken);
            var contexto = await AlmacenOFEntregaService.CargarEmbalajeAsync(
                connection, null, solicitudProduccionId.Value, vm.MaterialID, cancellationToken);

            if (contexto == null)
            {
                Mensaje("warning", "El embalaje no pertenece a la OF seleccionada.");
                return RedirectToAction("Index", "AlmacenOF");
            }

            if (string.IsNullOrWhiteSpace(contexto.NumeroOF))
            {
                Mensaje("warning", "Planeación todavía no asigna un número de OF válido.");
                return RedirectToAction("Index", "AlmacenOF");
            }

            if (contexto.Pendiente <= 0.0005m)
            {
                Mensaje("warning", "El embalaje seleccionado ya fue entregado completamente.");
                return RedirectToAction("Index", "AlmacenOF");
            }

            vm.NumeroOF = contexto.NumeroOF;
            vm.Unidad = contexto.Unidad;
            vm.CantidadPendienteOF = contexto.Pendiente;
            vm.Cantidad = contexto.Pendiente;
            vm.Observaciones = $"Entrega de embalaje para {contexto.NumeroOF}.";
        }

        await CargarMovimientoAsync(vm, cancellationToken);
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Movimiento(AlmacenMPMovimientoFormVm model, CancellationToken cancellationToken)
    {
        var sesion = ValidarSesion();
        if (sesion != null) return sesion;

        model.Lote = string.IsNullOrWhiteSpace(model.Lote) ? "S/L" : model.Lote.Trim();
        model.NumeroOF = model.NumeroOF?.Trim();
        model.Observaciones = model.Observaciones?.Trim();
        model.OperacionToken = model.OperacionToken?.Trim() ?? string.Empty;
        model.FechaMovimiento = DateTime.Now;
        model.EsEntregaOF = model.EsEntregaOF || model.SolicitudProduccionID.HasValue;
        ModelState.Remove(nameof(model.Unidad));

        if (!AlmacenOFEntregaService.TokenValido(model.OperacionToken))
            ModelState.AddModelError(nameof(model.OperacionToken), "La operación expiró. Regresa al formulario e inténtalo nuevamente.");

        if (model.EsEntregaOF)
        {
            model.TipoMovimiento = "Salida";
            model.Lote = "S/L";
            model.UbicacionID = null;
            model.Unidad = string.Empty;
            ModelState.Remove(nameof(model.TipoMovimiento));
            ModelState.Remove(nameof(model.Lote));
            ModelState.Remove(nameof(model.UbicacionID));
            ModelState.Remove(nameof(model.NumeroOF));
            ModelState.Remove(nameof(model.CantidadPendienteOF));

            if (!model.SolicitudProduccionID.HasValue || model.SolicitudProduccionID.Value <= 0)
                ModelState.AddModelError(nameof(model.SolicitudProduccionID), "La orden de fabricación es obligatoria.");
            if (model.MaterialID <= 0)
                ModelState.AddModelError(nameof(model.MaterialID), "El embalaje es obligatorio.");
            if (string.IsNullOrWhiteSpace(model.Observaciones))
                ModelState.AddModelError(nameof(model.Observaciones), "Las observaciones son obligatorias.");
        }

        if ((model.TipoMovimiento == "AjustePositivo" || model.TipoMovimiento == "AjusteNegativo")
            && string.IsNullOrWhiteSpace(model.Observaciones))
            ModelState.AddModelError(nameof(model.Observaciones), "El motivo del ajuste es obligatorio.");

        if (!TiposPermitidos.Contains(model.TipoMovimiento))
            ModelState.AddModelError(nameof(model.TipoMovimiento), "Tipo de movimiento inválido.");

        if (!ModelState.IsValid)
        {
            await CargarMovimientoAsync(model, cancellationToken);
            return View(model);
        }

        await using var connection = await AbrirConexionAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var referencia = AlmacenOFEntregaService.CrearReferencia("WEB-EMB", model.OperacionToken);

        try
        {
            if (await AlmacenOFEntregaService.ExisteReferenciaEmbalajeAsync(connection, transaction, referencia, cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                Mensaje("warning", "Este movimiento ya había sido registrado. No se creó un duplicado.");
                return model.EsEntregaOF ? RedirectToAction("Index", "AlmacenOF") : RedirectToAction(nameof(Index));
            }

            string codigo;
            var requiereLote = false;

            if (model.EsEntregaOF)
            {
                var contexto = await AlmacenOFEntregaService.CargarEmbalajeAsync(
                    connection, transaction, model.SolicitudProduccionID!.Value, model.MaterialID, cancellationToken);

                if (contexto == null)
                {
                    ModelState.AddModelError(nameof(model.MaterialID), "El embalaje no pertenece a la OF seleccionada.");
                    await transaction.RollbackAsync(cancellationToken);
                    await CargarMovimientoAsync(model, cancellationToken);
                    return View(model);
                }

                model.NumeroOF = contexto.NumeroOF;
                model.Unidad = contexto.Unidad;
                model.CantidadPendienteOF = contexto.Pendiente;
                codigo = contexto.Codigo;

                if (string.IsNullOrWhiteSpace(contexto.NumeroOF))
                {
                    ModelState.AddModelError(nameof(model.SolicitudProduccionID), "Planeación todavía no asigna un número de OF válido.");
                    await transaction.RollbackAsync(cancellationToken);
                    await CargarMovimientoAsync(model, cancellationToken);
                    return View(model);
                }

                if (contexto.Pendiente <= 0.0005m)
                {
                    ModelState.AddModelError(nameof(model.Cantidad), "La entrega de este embalaje ya está completa.");
                    await transaction.RollbackAsync(cancellationToken);
                    await CargarMovimientoAsync(model, cancellationToken);
                    return View(model);
                }

                if (model.Cantidad - contexto.Pendiente > 0.0005m)
                {
                    ModelState.AddModelError(nameof(model.Cantidad), $"La cantidad excede lo pendiente. Máximo permitido: {contexto.Pendiente:0.###} {contexto.Unidad}.");
                    await transaction.RollbackAsync(cancellationToken);
                    await CargarMovimientoAsync(model, cancellationToken);
                    return View(model);
                }
            }
            else
            {
                const string materialSql = @"
SELECT Codigo, Nombre, UnidadDefault, RequiereLote
FROM dbo.ERP_Embalajes WITH (UPDLOCK, HOLDLOCK)
WHERE EmbalajeID=@Id AND Activo=1;";
                await using var materialCommand = new SqlCommand(materialSql, connection, transaction);
                materialCommand.Parameters.Add("@Id", SqlDbType.Int).Value = model.MaterialID;
                await using var reader = await materialCommand.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                {
                    ModelState.AddModelError(nameof(model.MaterialID), "El embalaje no existe o está inactivo.");
                    await transaction.RollbackAsync(cancellationToken);
                    await CargarMovimientoAsync(model, cancellationToken);
                    return View(model);
                }
                codigo = Texto(reader, "Codigo");
                requiereLote = Convert.ToBoolean(reader["RequiereLote"]);
                model.Unidad = Texto(reader, "UnidadDefault").Trim().ToUpperInvariant();
            }

            if (!model.EsEntregaOF && requiereLote && model.Lote == "S/L")
            {
                ModelState.AddModelError(nameof(model.Lote), "Este embalaje requiere lote.");
                await transaction.RollbackAsync(cancellationToken);
                await CargarMovimientoAsync(model, cancellationToken);
                return View(model);
            }

            if (EsSalidaMP(model.TipoMovimiento))
            {
                const string saldoSql = "SELECT ISNULL(Saldo,0) FROM dbo.vw_AlmacenEmbalajesInventario WHERE EmbalajeID=@MaterialID;";
                await using var saldoCommand = new SqlCommand(saldoSql, connection, transaction);
                saldoCommand.Parameters.Add("@MaterialID", SqlDbType.Int).Value = model.MaterialID;
                var saldo = Convert.ToDecimal(await saldoCommand.ExecuteScalarAsync(cancellationToken) ?? 0m);
                if (saldo < model.Cantidad)
                {
                    ModelState.AddModelError(nameof(model.Cantidad), $"Stock insuficiente para {codigo}. Disponible: {saldo:0.###} {model.Unidad}.");
                    await transaction.RollbackAsync(cancellationToken);
                    await CargarMovimientoAsync(model, cancellationToken);
                    return View(model);
                }
            }

            const string insertSql = @"
INSERT dbo.AlmacenEmbalajes_Movimientos
(FechaMovimiento, EmbalajeID, TipoMovimiento, Lote, Cantidad, Unidad, UbicacionID, NumeroOF,
 ResponsableUsuarioID, EntregadoPorNombre, Seguimiento, FechaCreacion, CreadoPor, Activo,
 RequiereValidacionProduccion, ValidadoProduccion, ReferenciaOperacion)
VALUES
(SYSDATETIME(), @EmbalajeID, @Tipo, @Lote, @Cantidad, @Unidad, @UbicacionID, @NumeroOF,
 @UsuarioID, @Responsable, @Observaciones, SYSUTCDATETIME(), @Responsable, 1, 0, 1, @Referencia);";
            await using var insert = new SqlCommand(insertSql, connection, transaction);
            insert.Parameters.Add("@EmbalajeID", SqlDbType.Int).Value = model.MaterialID;
            insert.Parameters.Add("@Tipo", SqlDbType.NVarChar, 30).Value = model.TipoMovimiento;
            insert.Parameters.Add("@Lote", SqlDbType.NVarChar, 120).Value = model.Lote;
            var cantidadParametro = insert.Parameters.Add("@Cantidad", SqlDbType.Decimal);
            cantidadParametro.Precision = 18;
            cantidadParametro.Scale = 3;
            cantidadParametro.Value = model.Cantidad;
            insert.Parameters.Add("@Unidad", SqlDbType.NVarChar, 20).Value = model.Unidad;
            insert.Parameters.Add("@UbicacionID", SqlDbType.Int).Value = model.UbicacionID.HasValue ? model.UbicacionID.Value : DBNull.Value;
            insert.Parameters.Add("@NumeroOF", SqlDbType.NVarChar, 80).Value = string.IsNullOrWhiteSpace(model.NumeroOF) ? DBNull.Value : model.NumeroOF;
            insert.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = UsuarioID.HasValue ? UsuarioID.Value : DBNull.Value;
            insert.Parameters.Add("@Responsable", SqlDbType.NVarChar, 180).Value = UsuarioNombre;
            insert.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 800).Value = string.IsNullOrWhiteSpace(model.Observaciones) ? DBNull.Value : model.Observaciones;
            insert.Parameters.Add("@Referencia", SqlDbType.NVarChar, 120).Value = referencia;
            await insert.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            await transaction.RollbackAsync(cancellationToken);
            Mensaje("warning", "La operación ya fue procesada. No se creó un movimiento duplicado.");
            return model.EsEntregaOF ? RedirectToAction("Index", "AlmacenOF") : RedirectToAction(nameof(Index));
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        Mensaje("success", model.EsEntregaOF ? "Entrega de embalaje registrada para la OF." : "Movimiento de embalajes registrado correctamente.");
        return model.EsEntregaOF ? RedirectToAction("Index", "AlmacenOF") : RedirectToAction(nameof(Index));
    }

    private async Task CargarMovimientoAsync(AlmacenMPMovimientoFormVm vm,CancellationToken cancellationToken)
    {
        await using var connection=await AbrirConexionAsync(cancellationToken);
        const string catalogoSql="SELECT EmbalajeID,Codigo,Nombre,UnidadDefault FROM dbo.ERP_Embalajes WHERE Activo=1 ORDER BY Codigo;";
        await using(var command=new SqlCommand(catalogoSql,connection))
        await using(var reader=await command.ExecuteReaderAsync(cancellationToken))
            while(await reader.ReadAsync(cancellationToken))vm.Materiales.Add(new AlmacenSelectVm{Id=Entero(reader,"EmbalajeID"),Texto=$"{Texto(reader,"Codigo")} · {Texto(reader,"Nombre")}",Extra=Texto(reader,"UnidadDefault")});
        if(!vm.EsEntregaOF)
        {
            const string ubicacionesSql=@"SELECT UbicacionID,Almacen,Rack,Nivel,Posicion FROM dbo.ERP_Ubicaciones WHERE Activo=1 AND Almacen IN(N'EMBALAJES',N'GENERAL') ORDER BY Almacen,Rack,Nivel,Posicion;";
            await using(var command=new SqlCommand(ubicacionesSql,connection))
            await using(var reader=await command.ExecuteReaderAsync(cancellationToken))
                while(await reader.ReadAsync(cancellationToken))vm.Ubicaciones.Add(new AlmacenSelectVm{Id=Entero(reader,"UbicacionID"),Texto=string.Join(" · ",new[]{Texto(reader,"Almacen"),Texto(reader,"Rack"),Texto(reader,"Nivel"),Texto(reader,"Posicion")}.Where(x=>!string.IsNullOrWhiteSpace(x)))});
        }
        vm.TiposMovimiento=TiposPermitidos.Select(x=>new AlmacenSelectVm{Texto=x,Extra=x}).ToList();
        if(vm.MaterialID>0&&(vm.EsEntregaOF||string.IsNullOrWhiteSpace(vm.Unidad)))
            vm.Unidad=vm.Materiales.FirstOrDefault(x=>x.Id==vm.MaterialID)?.Extra??"PZS";
    }

    private static void AgregarParametrosHistorial(SqlCommand command,AlmacenMPHistorialVm filtro)
    {
        command.Parameters.Add("@Embalaje",SqlDbType.NVarChar,250).Value=string.IsNullOrWhiteSpace(filtro.FiltroMaterial)?DBNull.Value:filtro.FiltroMaterial;
        command.Parameters.Add("@Q",SqlDbType.NVarChar,250).Value=string.IsNullOrWhiteSpace(filtro.Busqueda)?DBNull.Value:filtro.Busqueda;
        command.Parameters.Add("@Tipo",SqlDbType.NVarChar,30).Value=string.IsNullOrWhiteSpace(filtro.TipoMovimiento)?DBNull.Value:filtro.TipoMovimiento;
        command.Parameters.Add("@NumeroOF",SqlDbType.NVarChar,80).Value=string.IsNullOrWhiteSpace(filtro.NumeroOF)?DBNull.Value:filtro.NumeroOF;
        command.Parameters.Add("@Responsable",SqlDbType.NVarChar,180).Value=string.IsNullOrWhiteSpace(filtro.Responsable)?DBNull.Value:filtro.Responsable;
        command.Parameters.Add("@Lote",SqlDbType.NVarChar,120).Value=string.IsNullOrWhiteSpace(filtro.Lote)?DBNull.Value:filtro.Lote;
        command.Parameters.Add("@Desde",SqlDbType.Date).Value=filtro.Desde.HasValue?filtro.Desde.Value.Date:DBNull.Value;
        command.Parameters.Add("@Hasta",SqlDbType.Date).Value=filtro.Hasta.HasValue?filtro.Hasta.Value.Date:DBNull.Value;
    }

    private static async Task CargarFiltrosHistorialAsync(SqlConnection connection,AlmacenMPHistorialVm vm,CancellationToken cancellationToken)
    {
        const string catalogoSql="SELECT TOP(2000) EmbalajeID,Codigo,Nombre FROM dbo.ERP_Embalajes WHERE Activo=1 ORDER BY Codigo;";
        await using(var command=new SqlCommand(catalogoSql,connection))
        await using(var reader=await command.ExecuteReaderAsync(cancellationToken))
            while(await reader.ReadAsync(cancellationToken))vm.MaterialesFiltro.Add(new AlmacenSelectVm{Id=Entero(reader,"EmbalajeID"),Texto=$"{Texto(reader,"Codigo")} · {Texto(reader,"Nombre")}",Extra=Texto(reader,"Codigo")});
        const string opcionesSql=@"
SELECT DISTINCT TOP(500) LTRIM(RTRIM(NumeroOF)) AS Valor FROM dbo.AlmacenEmbalajes_Movimientos WHERE Activo=1 AND NULLIF(LTRIM(RTRIM(NumeroOF)),'') IS NOT NULL ORDER BY Valor;
SELECT DISTINCT TOP(500) COALESCE(NULLIF(LTRIM(RTRIM(mm.EntregadoPorNombre)),''),NULLIF(LTRIM(RTRIM(CONCAT(p.Nombre,' ',p.ApellidoPaterno))),''),mm.CreadoPor,'') AS Valor
FROM dbo.AlmacenEmbalajes_Movimientos mm LEFT JOIN dbo.Usuarios us ON us.UsuarioID=mm.ResponsableUsuarioID LEFT JOIN dbo.Persona p ON p.PersonaID=us.PersonaID
WHERE mm.Activo=1 AND NULLIF(COALESCE(NULLIF(LTRIM(RTRIM(mm.EntregadoPorNombre)),''),NULLIF(LTRIM(RTRIM(CONCAT(p.Nombre,' ',p.ApellidoPaterno))),''),mm.CreadoPor,''),'') IS NOT NULL ORDER BY Valor;
SELECT DISTINCT TOP(500) LTRIM(RTRIM(Lote)) AS Valor FROM dbo.AlmacenEmbalajes_Movimientos WHERE Activo=1 AND NULLIF(LTRIM(RTRIM(Lote)),'') IS NOT NULL ORDER BY Valor;";
        await using var options=new SqlCommand(opcionesSql,connection);await using var optionReader=await options.ExecuteReaderAsync(cancellationToken);
        vm.OrdenesFiltro=await LeerListaTextoAsync(optionReader,cancellationToken);if(await optionReader.NextResultAsync(cancellationToken))vm.ResponsablesFiltro=await LeerListaTextoAsync(optionReader,cancellationToken);if(await optionReader.NextResultAsync(cancellationToken))vm.LotesFiltro=await LeerListaTextoAsync(optionReader,cancellationToken);
    }

    private static async Task<List<string>> LeerListaTextoAsync(SqlDataReader reader,CancellationToken cancellationToken)
    {
        var valores=new List<string>();while(await reader.ReadAsync(cancellationToken)){var valor=Texto(reader,"Valor").Trim();if(!string.IsNullOrWhiteSpace(valor))valores.Add(valor);}return valores;
    }

    private static AlmacenMPMovimientoListaVm LeerMovimiento(SqlDataReader reader)=>new()
    {
        MovimientoID=EnteroLargo(reader,"MovimientoID"),FechaMovimiento=Fecha(reader,"FechaMovimiento")??DateTime.MinValue,
        MaterialID=Entero(reader,"MaterialID"),Codigo=Texto(reader,"Codigo"),Material=Texto(reader,"Material"),
        TipoMovimiento=Texto(reader,"TipoMovimiento"),Cantidad=DecimalValor(reader,"Cantidad"),Unidad=Texto(reader,"Unidad"),
        Lote=Texto(reader,"Lote"),Ubicacion=Texto(reader,"Ubicacion"),NumeroOF=Texto(reader,"NumeroOF"),
        Responsable=Texto(reader,"Responsable"),Observaciones=Texto(reader,"Observaciones"),ReferenciaOperacion=Texto(reader,"ReferenciaOperacion")
    };
}



