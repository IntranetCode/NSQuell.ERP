using ERP.NSQuell.Models.ViewModels.Almacen;
using ERP.NSQuell.Servicios.Almacen;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Text;

namespace ERP.NSQuell.Controllers;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AlmacenPTController : AlmacenBaseController
{
    private static readonly string[] TiposPermitidos =
    {
        "Salida", "Embarque", "Retencion", "Liberacion", "Retorno", "Scrap", "AjustePositivo", "AjusteNegativo"
    };

    private static readonly string[] EstadosCalidad =
    {
        "Liberado", "Retenido", "GP12", "Cuarentena", "Rechazado"
    };

    public AlmacenPTController(IConfiguration configuration) : base(configuration) { }

    [HttpGet]
    public async Task<IActionResult> Index(string? q, string? estado, CancellationToken cancellationToken)
    {
        var sesion = ValidarSesion();
        if (sesion != null) return sesion;

        var vm = new AlmacenPTIndexVm
        {
            Busqueda = q?.Trim(),
            Estado = estado?.Trim().ToUpperInvariant()
        };

        await using var connection = await AbrirConexionAsync(cancellationToken);
        if (!await ExisteObjetoAsync(connection, "dbo.vw_AlmacenPTInventario", "V", cancellationToken)
            || !await ExisteColumnaAsync(connection, "dbo.vw_AlmacenPTInventario", "StockConfigurado", cancellationToken)
            || !await ExisteColumnaAsync(connection, "dbo.vw_AlmacenPTInventario", "PrecioVentaUnitario", cancellationToken)
            || !await ExisteColumnaAsync(connection, "dbo.AlmacenPT_Movimientos", "ReferenciaOperacion", cancellationToken))
        {
            vm.Configurado = false;
            vm.MensajeConfiguracion = "Falta ejecutar Scripts/SQL/Almacen/04_Actualizar_Stock_e_Integracion_Almacen.sql.";
            return View(vm);
        }
        // ALMACEN_PT_SOLICITADO_QUERY_V1_0
        const string sql = @"
WITH RequeridoPT AS
(
    SELECT
        s.SolicitudProduccionID,
        d.ParteID,
        COALESCE
        (
            NULLIF(LTRIM(RTRIM(s.NumeroOFRecibida)), N''),
            NULLIF(LTRIM(RTRIM(s.FolioSolicitud)), N''),
            CONCAT(N'OF-ID-', s.SolicitudProduccionID)
        ) AS NumeroOF,
        SUM(CONVERT(BIGINT, ISNULL(d.CantidadPiezas, 0))) AS Requerido
    FROM dbo.SolicitudesProduccion s
    INNER JOIN dbo.SolicitudesProduccionDetalle d
        ON d.SolicitudProduccionID = s.SolicitudProduccionID
       AND d.Activo = 1
       AND d.ParteID IS NOT NULL
       AND ISNULL(d.CantidadPiezas, 0) > 0
    WHERE s.Activo = 1
      AND s.EstatusID IN (8, 9)
    GROUP BY
        s.SolicitudProduccionID,
        d.ParteID,
        s.NumeroOFRecibida,
        s.FolioSolicitud
),
PendientePT AS
(
    SELECT
        r.ParteID,
        SUM
        (
            CASE
                WHEN r.Requerido - ISNULL(recibido.Entregado, 0) > 0
                    THEN r.Requerido - ISNULL(recibido.Entregado, 0)
                ELSE 0
            END
        ) AS Solicitado
    FROM RequeridoPT r
    OUTER APPLY
    (
        SELECT
            SUM
            (
                CASE
                    WHEN m.TipoMovimiento = N'Entrada'
                        THEN CONVERT(BIGINT, m.Cantidad)
                    ELSE 0
                END
            ) AS Entregado
        FROM dbo.AlmacenPT_Movimientos m
        WHERE m.Activo = 1
          AND m.ParteID = r.ParteID
          AND LTRIM(RTRIM(ISNULL(m.NumeroOF, N''))) =
              LTRIM(RTRIM(r.NumeroOF))
    ) recibido
    GROUP BY r.ParteID
)
SELECT TOP (500)
    inventario.ParteID,
    inventario.NumeroParte,
    inventario.Descripcion,
    inventario.Cliente,
    inventario.Cajas,
    inventario.Entradas,
    inventario.Salidas,
    inventario.SaldoFisico,
    inventario.Retenido,
    inventario.Disponible,
    CONVERT(BIGINT, ISNULL(pendiente.Solicitado, 0)) AS Solicitado,
    inventario.StockMinimo,
    inventario.StockAviso,
    inventario.StockConfigurado,
    CASE
        WHEN inventario.PrecioVentaUnitario IS NULL THEN 0
        ELSE 1
    END AS TienePrecioVenta,
    inventario.PrecioVentaUnitario,
    inventario.MonedaPrecioVenta,
    inventario.UnidadPrecioVenta,
    inventario.FuentePrecioVenta,
    inventario.FechaPrecioVenta,
    inventario.Semaforo,
    inventario.UltimoMovimiento
FROM dbo.vw_AlmacenPTInventario inventario
LEFT JOIN PendientePT pendiente
    ON pendiente.ParteID = inventario.ParteID
WHERE
    (
        @Q IS NULL
        OR inventario.NumeroParte LIKE N'%' + @Q + N'%'
        OR inventario.Descripcion LIKE N'%' + @Q + N'%'
        OR inventario.Cliente LIKE N'%' + @Q + N'%'
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
    inventario.NumeroParte;";

await using (var command = new SqlCommand(sql, connection))
        {
            command.Parameters.Add("@Q", SqlDbType.NVarChar, 250).Value = string.IsNullOrWhiteSpace(vm.Busqueda) ? DBNull.Value : vm.Busqueda;
            command.Parameters.Add("@Estado", SqlDbType.NVarChar, 20).Value = string.IsNullOrWhiteSpace(vm.Estado) ? DBNull.Value : vm.Estado;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                vm.Existencias.Add(new AlmacenPTExistenciaVm
                {
                    ParteID = Entero(reader, "ParteID"),
                    NumeroParte = Texto(reader, "NumeroParte"),
                    Descripcion = Texto(reader, "Descripcion"),
                    Cliente = Texto(reader, "Cliente"),
                    Cajas = Entero(reader, "Cajas"),
                    Entradas = Entero(reader, "Entradas"),
                    Salidas = Entero(reader, "Salidas"),
                    SaldoFisico = Entero(reader, "SaldoFisico"),
                    Retenido = Entero(reader, "Retenido"),
                    Disponible = Entero(reader, "Disponible"),

                    Solicitado = EnteroLargo(reader, "Solicitado"),
                    StockMinimo = Entero(reader, "StockMinimo"),
                    StockAviso = Entero(reader, "StockAviso"),
                    Semaforo = Texto(reader, "Semaforo"),
                    StockConfigurado = Convert.ToBoolean(reader["StockConfigurado"]),
                    TienePrecioVenta = Convert.ToBoolean(reader["TienePrecioVenta"]),
                    PrecioVentaUnitario = DecimalValor(reader, "PrecioVentaUnitario"),
                    MonedaPrecioVenta = Texto(reader, "MonedaPrecioVenta"),
                    UnidadPrecioVenta = Texto(reader, "UnidadPrecioVenta"),
                    FuentePrecioVenta = Texto(reader, "FuentePrecioVenta"),
                    FechaPrecioVenta = Fecha(reader, "FechaPrecioVenta"),
                    UltimoMovimiento = Fecha(reader, "UltimoMovimiento")
                });
            }
        }

        const string movimientosSql = @"
SELECT TOP (5)
    m.MovimientoID, m.FechaMovimiento, m.ParteID,
    p.NumeroParte, p.Descripcion, ISNULL(c.Etiqueta,'') AS Etiqueta,
    m.TipoMovimiento, m.Cantidad,
    CONCAT(u.Almacen, ' / ', u.Rack,
        CASE WHEN u.Nivel IS NULL THEN '' ELSE ' / ' + u.Nivel END,
        CASE WHEN u.Posicion IS NULL THEN '' ELSE ' / ' + u.Posicion END) AS Ubicacion,
    ISNULL(m.EstadoCalidad,'') AS EstadoCalidad,
    ISNULL(m.NumeroOF,'') AS NumeroOF,
    COALESCE(NULLIF(LTRIM(RTRIM(CONCAT(pers.Nombre,' ',pers.ApellidoPaterno))),''), m.CreadoPor, '') AS Responsable,
    ISNULL(m.Observaciones,'') AS Observaciones,
    ISNULL(m.ReferenciaOperacion,'') AS ReferenciaOperacion,
    ISNULL(c.LoteEtiqueta,'') AS LoteEtiqueta,
    ISNULL(c.NumeroCaja,0) AS NumeroCaja
FROM dbo.AlmacenPT_Movimientos m
INNER JOIN dbo.ERP_Partes p ON p.ParteID = m.ParteID
LEFT JOIN dbo.AlmacenPT_Cajas c ON c.CajaID = m.CajaID
LEFT JOIN dbo.ERP_Ubicaciones u ON u.UbicacionID = m.UbicacionID
LEFT JOIN dbo.Usuarios us ON us.UsuarioID = m.ResponsableUsuarioID
LEFT JOIN dbo.Persona pers ON pers.PersonaID = us.PersonaID
WHERE m.Activo=1
ORDER BY m.FechaMovimiento DESC, m.MovimientoID DESC;";

        await using (var command = new SqlCommand(movimientosSql, connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                vm.Movimientos.Add(new AlmacenPTMovimientoListaVm
                {
                    MovimientoID = EnteroLargo(reader, "MovimientoID"),
                    FechaMovimiento = Fecha(reader, "FechaMovimiento") ?? DateTime.MinValue,
                    ParteID = Entero(reader, "ParteID"),
                    NumeroParte = Texto(reader, "NumeroParte"),
                    Descripcion = Texto(reader, "Descripcion"),
                    Etiqueta = Texto(reader, "Etiqueta"),
                    TipoMovimiento = Texto(reader, "TipoMovimiento"),
                    Cantidad = Entero(reader, "Cantidad"),
                    Ubicacion = Texto(reader, "Ubicacion"),
                    EstadoCalidad = Texto(reader, "EstadoCalidad"),
                    NumeroOF = Texto(reader, "NumeroOF"),
                    Responsable = Texto(reader, "Responsable"),
                    Observaciones = Texto(reader, "Observaciones"),
                    ReferenciaOperacion = Texto(reader, "ReferenciaOperacion"),
                    LoteEtiqueta = Texto(reader, "LoteEtiqueta"),
                    NumeroCaja = Entero(reader, "NumeroCaja")
                });
            }
        }
        // ALMACEN_PT_KPI_OPERATIVO_V1_0
        const string resumenOperativoSql = @"
WITH RequeridoPT AS
(
    SELECT
        s.SolicitudProduccionID,
        d.ParteID,
        COALESCE
        (
            NULLIF(LTRIM(RTRIM(s.NumeroOFRecibida)), N''),
            NULLIF(LTRIM(RTRIM(s.FolioSolicitud)), N''),
            CONCAT(N'OF-ID-', s.SolicitudProduccionID)
        ) AS NumeroOF,
        SUM(CONVERT(BIGINT, ISNULL(d.CantidadPiezas, 0))) AS Requerido
    FROM dbo.SolicitudesProduccion s
    INNER JOIN dbo.SolicitudesProduccionDetalle d
        ON d.SolicitudProduccionID = s.SolicitudProduccionID
       AND d.Activo = 1
       AND d.ParteID IS NOT NULL
       AND ISNULL(d.CantidadPiezas, 0) > 0
    WHERE s.Activo = 1
      AND s.EstatusID IN (8, 9)
    GROUP BY
        s.SolicitudProduccionID,
        d.ParteID,
        s.NumeroOFRecibida,
        s.FolioSolicitud
),
Pendientes AS
(
    SELECT
        r.SolicitudProduccionID,
        SUM
        (
            CASE
                WHEN r.Requerido - ISNULL(recibido.Entregado, 0) > 0
                    THEN r.Requerido - ISNULL(recibido.Entregado, 0)
                ELSE 0
            END
        ) AS Pendiente
    FROM RequeridoPT r
    OUTER APPLY
    (
        SELECT
            SUM
            (
                CASE
                    WHEN m.TipoMovimiento = N'Entrada'
                        THEN CONVERT(BIGINT, m.Cantidad)
                    ELSE 0
                END
            ) AS Entregado
        FROM dbo.AlmacenPT_Movimientos m
        WHERE m.Activo = 1
          AND m.ParteID = r.ParteID
          AND LTRIM(RTRIM(ISNULL(m.NumeroOF, N''))) =
              LTRIM(RTRIM(r.NumeroOF))
    ) recibido
    GROUP BY r.SolicitudProduccionID
)
SELECT
    COUNT_BIG(CASE WHEN Pendiente > 0 THEN 1 END) AS OFPendientesRecepcion,
    ISNULL
    (
        SUM
        (
            CASE
                WHEN Pendiente > 0 THEN Pendiente
                ELSE 0
            END
        ),
        0
    ) AS PiezasSolicitadasPendientes
FROM Pendientes;

SELECT
    COUNT_BIG
    (
        DISTINCT
        CASE
            WHEN TipoMovimiento = N'Entrada'
             AND CONVERT(DATE, FechaMovimiento) = CONVERT(DATE, GETDATE())
                THEN CajaID
            ELSE NULL
        END
    ) AS CajasRecibidasHoy,
    ISNULL
    (
        SUM
        (
            CASE
                WHEN TipoMovimiento = N'Entrada'
                 AND CONVERT(DATE, FechaMovimiento) = CONVERT(DATE, GETDATE())
                    THEN CONVERT(BIGINT, Cantidad)
                ELSE 0
            END
        ),
        0
    ) AS PiezasRecibidasHoy,
    ISNULL
    (
        SUM
        (
            CASE
                WHEN TipoMovimiento IN (N'Salida', N'Embarque')
                 AND CONVERT(DATE, FechaMovimiento) = CONVERT(DATE, GETDATE())
                    THEN CONVERT(BIGINT, Cantidad)
                ELSE 0
            END
        ),
        0
    ) AS PiezasSalidasHoy
FROM dbo.AlmacenPT_Movimientos
WHERE Activo = 1;";

        await using (var resumenCommand =
            new SqlCommand(resumenOperativoSql, connection))
        await using (var resumenReader =
            await resumenCommand.ExecuteReaderAsync(cancellationToken))
        {
            if (await resumenReader.ReadAsync(cancellationToken))
            {
                vm.OFPendientesRecepcion =
                    Convert.ToInt32(
                        resumenReader["OFPendientesRecepcion"]);

                vm.PiezasSolicitadasPendientes =
                    Convert.ToInt64(
                        resumenReader["PiezasSolicitadasPendientes"]);
            }

            if (await resumenReader.NextResultAsync(cancellationToken)
                && await resumenReader.ReadAsync(cancellationToken))
            {
                vm.CajasRecibidasHoy =
                    Convert.ToInt32(
                        resumenReader["CajasRecibidasHoy"]);

                vm.PiezasRecibidasHoy =
                    Convert.ToInt64(
                        resumenReader["PiezasRecibidasHoy"]);

                vm.PiezasSalidasHoy =
                    Convert.ToInt64(
                        resumenReader["PiezasSalidasHoy"]);
            }
        }

vm.TotalPartes = vm.Existencias.Count;
        vm.Criticos = vm.Existencias.Count(x => x.Semaforo == "ROJO");
        vm.Advertencias = vm.Existencias.Count(x => x.Semaforo == "AMARILLO");
        vm.Disponibles = vm.Existencias.Count(x => x.Semaforo == "VERDE");
        vm.PendientesConfiguracion = vm.Existencias.Count(x => !x.StockConfigurado);
        vm.PiezasFisicas = vm.Existencias.Sum(x => x.SaldoFisico);
        vm.PiezasRetenidas = vm.Existencias.Sum(x => x.Retenido);
        vm.PiezasDisponibles = vm.Existencias.Sum(x => x.Disponible);
        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> Historial(
        string? parte,
        string? q,
        string? tipo,
        string? numeroOF,
        string? responsable,
        string? etiquetaLote,
        string? periodo,
        DateTime? desde,
        DateTime? hasta,
        int pagina = 1,
        CancellationToken cancellationToken = default)
    {
        var sesion = ValidarSesion();
        if (sesion != null) return sesion;

        // ALMACEN_PT_HISTORIAL_PERIODOS_V2_0
        var periodoNormalizado =
            NormalizarPeriodoHistorialPT(
                periodo,
                desde,
                hasta);

        AplicarPeriodoHistorialPT(
            periodoNormalizado,
            ref desde,
            ref hasta);

        ViewData["PeriodoHistorialPT"] =
            periodoNormalizado;

        if (desde.HasValue && hasta.HasValue && hasta.Value.Date < desde.Value.Date)
            (desde, hasta) = (hasta, desde);

        var vm = new AlmacenPTHistorialVm
        {
            FiltroParte = parte?.Trim(),
            Busqueda = q?.Trim(),
            TipoMovimiento = TiposPermitidos.Concat(new[] { "Entrada" }).Contains(tipo ?? string.Empty) ? tipo : null,
            NumeroOF = numeroOF?.Trim(),
            Responsable = responsable?.Trim(),
            EtiquetaLote = etiquetaLote?.Trim(),
            Desde = desde?.Date,
            Hasta = hasta?.Date,
            Pagina = Math.Max(1, pagina),
            TamanoPagina = 50,
            TiposMovimiento = new[] { "Entrada" }.Concat(TiposPermitidos).Distinct().ToList()
        };

        await using var connection = await AbrirConexionAsync(cancellationToken);
        if (!await ExisteObjetoAsync(connection, "dbo.AlmacenPT_Movimientos", "U", cancellationToken))
        {
            Mensaje("warning", "No existe la tabla de movimientos PT.");
            return RedirectToAction(nameof(Index));
        }
        if (!await ExisteColumnaAsync(connection, "dbo.AlmacenPT_Movimientos", "ReferenciaOperacion", cancellationToken))
        {
            Mensaje("warning", "Ejecuta el script 04 corregido antes de consultar el historial PT.");
            return RedirectToAction(nameof(Index));
        }

        await CargarFiltrosHistorialPTAsync(connection, vm, cancellationToken);

        const string fromWhere = @"
FROM dbo.AlmacenPT_Movimientos m
INNER JOIN dbo.ERP_Partes p ON p.ParteID = m.ParteID
LEFT JOIN dbo.ERP_Clientes cl ON cl.ClienteID = p.ClienteID
LEFT JOIN dbo.AlmacenPT_Cajas c ON c.CajaID = m.CajaID
LEFT JOIN dbo.ERP_Ubicaciones u ON u.UbicacionID = m.UbicacionID
LEFT JOIN dbo.Usuarios us ON us.UsuarioID = m.ResponsableUsuarioID
LEFT JOIN dbo.Persona pers ON pers.PersonaID = us.PersonaID
WHERE m.Activo = 1
  AND
  (
      @Parte IS NULL
      OR
      (
          EXISTS (SELECT 1 FROM dbo.ERP_Partes fp WHERE fp.NumeroParte = @Parte)
          AND p.NumeroParte = @Parte
      )
      OR
      (
          NOT EXISTS (SELECT 1 FROM dbo.ERP_Partes fp WHERE fp.NumeroParte = @Parte)
          AND (p.NumeroParte LIKE '%' + @Parte + '%' OR p.Descripcion LIKE '%' + @Parte + '%')
      )
  )
  AND (@Q IS NULL OR ISNULL(cl.Nombre,'') LIKE '%' + @Q + '%' OR ISNULL(m.Observaciones,'') LIKE '%' + @Q + '%' OR ISNULL(m.ReferenciaOperacion,'') LIKE '%' + @Q + '%')
  AND (@Tipo IS NULL OR m.TipoMovimiento = @Tipo)
  AND (@NumeroOF IS NULL OR m.NumeroOF LIKE '%' + @NumeroOF + '%')
  AND (@Responsable IS NULL OR COALESCE(NULLIF(LTRIM(RTRIM(CONCAT(pers.Nombre,' ',pers.ApellidoPaterno))),''), m.CreadoPor, '') LIKE '%' + @Responsable + '%')
  AND (@EtiquetaLote IS NULL OR ISNULL(c.Etiqueta,'') LIKE '%' + @EtiquetaLote + '%' OR ISNULL(c.LoteEtiqueta,'') LIKE '%' + @EtiquetaLote + '%')
  AND (@Desde IS NULL OR m.FechaMovimiento >= @Desde)
  AND (@Hasta IS NULL OR m.FechaMovimiento < DATEADD(DAY,1,@Hasta))";

        await using (var count = new SqlCommand("SELECT COUNT_BIG(1) " + fromWhere, connection))
        {
            AgregarParametrosHistorialPT(count, vm);
            vm.TotalRegistros = Convert.ToInt32(await count.ExecuteScalarAsync(cancellationToken) ?? 0L);
        }

        vm.Pagina = Math.Min(vm.Pagina, vm.TotalPaginas);
        const string select = @"
SELECT
    m.MovimientoID, m.FechaMovimiento, m.ParteID,
    p.NumeroParte, p.Descripcion, ISNULL(c.Etiqueta,'') AS Etiqueta,
    ISNULL(c.NumeroCaja,0) AS NumeroCaja, ISNULL(c.LoteEtiqueta,'') AS LoteEtiqueta,
    m.TipoMovimiento, m.Cantidad,
    CONCAT(u.Almacen, ' / ', u.Rack,
        CASE WHEN u.Nivel IS NULL THEN '' ELSE ' / ' + u.Nivel END,
        CASE WHEN u.Posicion IS NULL THEN '' ELSE ' / ' + u.Posicion END) AS Ubicacion,
    ISNULL(m.EstadoCalidad,'') AS EstadoCalidad,
    ISNULL(m.NumeroOF,'') AS NumeroOF,
    COALESCE(NULLIF(LTRIM(RTRIM(CONCAT(pers.Nombre,' ',pers.ApellidoPaterno))),''), m.CreadoPor, '') AS Responsable,
    ISNULL(m.Observaciones,'') AS Observaciones,
    ISNULL(m.ReferenciaOperacion,'') AS ReferenciaOperacion ";

        var dataSql = select + fromWhere + @"
ORDER BY m.FechaMovimiento DESC, m.MovimientoID DESC
OFFSET @Offset ROWS FETCH NEXT @Tamano ROWS ONLY;";
        await using var command = new SqlCommand(dataSql, connection);
        AgregarParametrosHistorialPT(command, vm);
        command.Parameters.Add("@Offset", SqlDbType.Int).Value = (vm.Pagina - 1) * vm.TamanoPagina;
        command.Parameters.Add("@Tamano", SqlDbType.Int).Value = vm.TamanoPagina;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            vm.Movimientos.Add(LeerMovimientoPT(reader));

        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> ExportarHistorialCsv(
        string? parte,
        string? q,
        string? tipo,
        string? numeroOF,
        string? responsable,
        string? etiquetaLote,
        string? periodo,
        DateTime? desde,
        DateTime? hasta,
        CancellationToken cancellationToken = default)
    {
        var sesion = ValidarSesion();
        if (sesion != null) return sesion;

        var periodoNormalizado =
            NormalizarPeriodoHistorialPT(
                periodo,
                desde,
                hasta);

        AplicarPeriodoHistorialPT(
            periodoNormalizado,
            ref desde,
            ref hasta);

        if (desde.HasValue && hasta.HasValue && hasta.Value.Date < desde.Value.Date)
            (desde, hasta) = (hasta, desde);

        var filtro = new AlmacenPTHistorialVm
        {
            FiltroParte = parte?.Trim(),
            Busqueda = q?.Trim(),
            TipoMovimiento = new[] { "Entrada" }.Concat(TiposPermitidos).Contains(tipo ?? string.Empty) ? tipo : null,
            NumeroOF = numeroOF?.Trim(),
            Responsable = responsable?.Trim(),
            EtiquetaLote = etiquetaLote?.Trim(),
            Desde = desde?.Date,
            Hasta = hasta?.Date
        };

        await using var connection = await AbrirConexionAsync(cancellationToken);
        if (!await ExisteColumnaAsync(connection, "dbo.AlmacenPT_Movimientos", "ReferenciaOperacion", cancellationToken))
        {
            Mensaje("warning", "Ejecuta el script 04 corregido antes de exportar el historial PT.");
            return RedirectToAction(nameof(Index));
        }

        const string sql = @"
SELECT TOP (10000)
    m.MovimientoID, m.FechaMovimiento, m.ParteID,
    p.NumeroParte, p.Descripcion, ISNULL(c.Etiqueta,'') AS Etiqueta,
    ISNULL(c.NumeroCaja,0) AS NumeroCaja, ISNULL(c.LoteEtiqueta,'') AS LoteEtiqueta,
    m.TipoMovimiento, m.Cantidad,
    CONCAT(u.Almacen, ' / ', u.Rack,
        CASE WHEN u.Nivel IS NULL THEN '' ELSE ' / ' + u.Nivel END,
        CASE WHEN u.Posicion IS NULL THEN '' ELSE ' / ' + u.Posicion END) AS Ubicacion,
    ISNULL(m.EstadoCalidad,'') AS EstadoCalidad,
    ISNULL(m.NumeroOF,'') AS NumeroOF,
    COALESCE(NULLIF(LTRIM(RTRIM(CONCAT(pers.Nombre,' ',pers.ApellidoPaterno))),''), m.CreadoPor, '') AS Responsable,
    ISNULL(m.Observaciones,'') AS Observaciones,
    ISNULL(m.ReferenciaOperacion,'') AS ReferenciaOperacion
FROM dbo.AlmacenPT_Movimientos m
INNER JOIN dbo.ERP_Partes p ON p.ParteID = m.ParteID
LEFT JOIN dbo.ERP_Clientes cl ON cl.ClienteID = p.ClienteID
LEFT JOIN dbo.AlmacenPT_Cajas c ON c.CajaID = m.CajaID
LEFT JOIN dbo.ERP_Ubicaciones u ON u.UbicacionID = m.UbicacionID
LEFT JOIN dbo.Usuarios us ON us.UsuarioID = m.ResponsableUsuarioID
LEFT JOIN dbo.Persona pers ON pers.PersonaID = us.PersonaID
WHERE m.Activo = 1
  AND
  (
      @Parte IS NULL
      OR
      (
          EXISTS (SELECT 1 FROM dbo.ERP_Partes fp WHERE fp.NumeroParte = @Parte)
          AND p.NumeroParte = @Parte
      )
      OR
      (
          NOT EXISTS (SELECT 1 FROM dbo.ERP_Partes fp WHERE fp.NumeroParte = @Parte)
          AND (p.NumeroParte LIKE '%' + @Parte + '%' OR p.Descripcion LIKE '%' + @Parte + '%')
      )
  )
  AND (@Q IS NULL OR ISNULL(cl.Nombre,'') LIKE '%' + @Q + '%' OR ISNULL(m.Observaciones,'') LIKE '%' + @Q + '%' OR ISNULL(m.ReferenciaOperacion,'') LIKE '%' + @Q + '%')
  AND (@Tipo IS NULL OR m.TipoMovimiento = @Tipo)
  AND (@NumeroOF IS NULL OR m.NumeroOF LIKE '%' + @NumeroOF + '%')
  AND (@Responsable IS NULL OR COALESCE(NULLIF(LTRIM(RTRIM(CONCAT(pers.Nombre,' ',pers.ApellidoPaterno))),''), m.CreadoPor, '') LIKE '%' + @Responsable + '%')
  AND (@EtiquetaLote IS NULL OR ISNULL(c.Etiqueta,'') LIKE '%' + @EtiquetaLote + '%' OR ISNULL(c.LoteEtiqueta,'') LIKE '%' + @EtiquetaLote + '%')
  AND (@Desde IS NULL OR m.FechaMovimiento >= @Desde)
  AND (@Hasta IS NULL OR m.FechaMovimiento < DATEADD(DAY,1,@Hasta))
ORDER BY m.FechaMovimiento DESC, m.MovimientoID DESC;";

        await using var command = new SqlCommand(sql, connection);
        AgregarParametrosHistorialPT(command, filtro);
        var csv = new StringBuilder();
        csv.AppendLine("MovimientoID;Fecha;NumeroParte;Descripcion;Etiqueta;Caja;Lote;Tipo;Cantidad;Calidad;Ubicacion;OF;Responsable;Referencia;Observaciones");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var x = LeerMovimientoPT(reader);
            csv.AppendLine(string.Join(";", new[]
            {
                Csv(x.MovimientoID.ToString()), Csv(x.FechaMovimiento.ToString("dd/MM/yyyy HH:mm:ss")),
                Csv(x.NumeroParte), Csv(x.Descripcion), Csv(x.Etiqueta), Csv(x.NumeroCaja.ToString()),
                Csv(x.LoteEtiqueta), Csv(x.TipoMovimiento), Csv(x.Cantidad.ToString()), Csv(x.EstadoCalidad),
                Csv(x.Ubicacion), Csv(x.NumeroOF), Csv(x.Responsable), Csv(x.ReferenciaOperacion), Csv(x.Observaciones)
            }));
        }

        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        return File(encoding.GetPreamble().Concat(encoding.GetBytes(csv.ToString())).ToArray(),
            "text/csv; charset=utf-8", $"Historial_PT_{DateTime.Now:yyyyMMdd_HHmm}.csv");
    }

    private static string NormalizarPeriodoHistorialPT(
        string? periodo,
        DateTime? desde,
        DateTime? hasta)
    {
        if (string.IsNullOrWhiteSpace(periodo))
        {
            return desde.HasValue || hasta.HasValue
                ? "PERSONALIZADO"
                : "TODO";
        }

        var valor =
            periodo.Trim().ToUpperInvariant();

        return valor is
            "SEMANA_ACTUAL"
            or "SEMANA_ANTERIOR"
            or "ULTIMOS_7_DIAS"
            or "ULTIMOS_30_DIAS"
            or "TODO"
            or "PERSONALIZADO"
                ? valor
                : "TODO";
    }

    private static void AplicarPeriodoHistorialPT(
        string periodo,
        ref DateTime? desde,
        ref DateTime? hasta)
    {
        var hoy = DateTime.Today;
        var diasDesdeLunes =
            ((int)hoy.DayOfWeek + 6) % 7;
        var inicioSemana =
            hoy.AddDays(-diasDesdeLunes);

        switch (periodo)
        {
            case "SEMANA_ANTERIOR":
                desde = inicioSemana.AddDays(-7);
                hasta = inicioSemana.AddDays(-1);
                break;

            case "ULTIMOS_7_DIAS":
                desde = hoy.AddDays(-6);
                hasta = hoy;
                break;

            case "ULTIMOS_30_DIAS":
                desde = hoy.AddDays(-29);
                hasta = hoy;
                break;

            case "TODO":
                desde = null;
                hasta = null;
                break;

            case "PERSONALIZADO":
                if (!desde.HasValue && !hasta.HasValue)
                {
                    desde = inicioSemana;
                    hasta = inicioSemana.AddDays(6);
                }
                break;

            default:
                desde = inicioSemana;
                hasta = inicioSemana.AddDays(6);
                break;
        }

        if (desde.HasValue
            && hasta.HasValue
            && hasta.Value.Date < desde.Value.Date)
        {
            (desde, hasta) = (hasta, desde);
        }
    }
    private static void AgregarParametrosHistorialPT(SqlCommand command, AlmacenPTHistorialVm filtro)
    {
        command.Parameters.Add("@Parte", SqlDbType.NVarChar, 250).Value = string.IsNullOrWhiteSpace(filtro.FiltroParte) ? DBNull.Value : filtro.FiltroParte;
        command.Parameters.Add("@Q", SqlDbType.NVarChar, 250).Value = string.IsNullOrWhiteSpace(filtro.Busqueda) ? DBNull.Value : filtro.Busqueda;
        command.Parameters.Add("@Tipo", SqlDbType.NVarChar, 30).Value = string.IsNullOrWhiteSpace(filtro.TipoMovimiento) ? DBNull.Value : filtro.TipoMovimiento;
        command.Parameters.Add("@NumeroOF", SqlDbType.NVarChar, 80).Value = string.IsNullOrWhiteSpace(filtro.NumeroOF) ? DBNull.Value : filtro.NumeroOF;
        command.Parameters.Add("@Responsable", SqlDbType.NVarChar, 180).Value = string.IsNullOrWhiteSpace(filtro.Responsable) ? DBNull.Value : filtro.Responsable;
        command.Parameters.Add("@EtiquetaLote", SqlDbType.NVarChar, 120).Value = string.IsNullOrWhiteSpace(filtro.EtiquetaLote) ? DBNull.Value : filtro.EtiquetaLote;
        command.Parameters.Add("@Desde", SqlDbType.Date).Value = filtro.Desde.HasValue ? filtro.Desde.Value.Date : DBNull.Value;
        command.Parameters.Add("@Hasta", SqlDbType.Date).Value = filtro.Hasta.HasValue ? filtro.Hasta.Value.Date : DBNull.Value;
    }

    private static async Task CargarFiltrosHistorialPTAsync(
        SqlConnection connection,
        AlmacenPTHistorialVm vm,
        CancellationToken cancellationToken)
    {
        const string partesSql = @"
SELECT TOP (2000) ParteID, NumeroParte, COALESCE(NULLIF(Designacion,''), Descripcion) AS Texto
FROM dbo.ERP_Partes
WHERE Activo = 1
ORDER BY NumeroParte;";
        await using (var command = new SqlCommand(partesSql, connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                vm.PartesFiltro.Add(new AlmacenSelectVm
                {
                    Id = Entero(reader, "ParteID"),
                    Texto = $"{Texto(reader, "NumeroParte")} · {Texto(reader, "Texto")}",
                    Extra = Texto(reader, "NumeroParte")
                });
            }
        }

        const string opcionesSql = @"
SELECT DISTINCT TOP (500) LTRIM(RTRIM(NumeroOF)) AS Valor
FROM dbo.AlmacenPT_Movimientos
WHERE Activo = 1 AND NULLIF(LTRIM(RTRIM(NumeroOF)), '') IS NOT NULL
ORDER BY Valor;

SELECT DISTINCT TOP (500)
    COALESCE(NULLIF(LTRIM(RTRIM(CONCAT(pers.Nombre,' ',pers.ApellidoPaterno))),''), m.CreadoPor, '') AS Valor
FROM dbo.AlmacenPT_Movimientos m
LEFT JOIN dbo.Usuarios us ON us.UsuarioID = m.ResponsableUsuarioID
LEFT JOIN dbo.Persona pers ON pers.PersonaID = us.PersonaID
WHERE m.Activo = 1
  AND NULLIF(COALESCE(NULLIF(LTRIM(RTRIM(CONCAT(pers.Nombre,' ',pers.ApellidoPaterno))),''), m.CreadoPor, ''), '') IS NOT NULL
ORDER BY Valor;

SELECT DISTINCT TOP (500) Valor
FROM
(
    SELECT NULLIF(LTRIM(RTRIM(Etiqueta)), '') AS Valor
    FROM dbo.AlmacenPT_Cajas
    WHERE Activo = 1
    UNION
    SELECT NULLIF(LTRIM(RTRIM(LoteEtiqueta)), '') AS Valor
    FROM dbo.AlmacenPT_Cajas
    WHERE Activo = 1
) opciones
WHERE Valor IS NOT NULL
ORDER BY Valor;";
        await using var options = new SqlCommand(opcionesSql, connection);
        await using var optionReader = await options.ExecuteReaderAsync(cancellationToken);
        vm.OrdenesFiltro = await LeerListaTextoAsync(optionReader, cancellationToken);
        if (await optionReader.NextResultAsync(cancellationToken))
            vm.ResponsablesFiltro = await LeerListaTextoAsync(optionReader, cancellationToken);
        if (await optionReader.NextResultAsync(cancellationToken))
            vm.EtiquetasLotesFiltro = await LeerListaTextoAsync(optionReader, cancellationToken);
    }

    private static async Task<List<string>> LeerListaTextoAsync(
        SqlDataReader reader,
        CancellationToken cancellationToken)
    {
        var valores = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var valor = Texto(reader, "Valor").Trim();
            if (!string.IsNullOrWhiteSpace(valor)) valores.Add(valor);
        }
        return valores;
    }

    private static AlmacenPTMovimientoListaVm LeerMovimientoPT(SqlDataReader reader) => new()
    {
        MovimientoID = EnteroLargo(reader, "MovimientoID"),
        FechaMovimiento = Fecha(reader, "FechaMovimiento") ?? DateTime.MinValue,
        ParteID = Entero(reader, "ParteID"),
        NumeroParte = Texto(reader, "NumeroParte"),
        Descripcion = Texto(reader, "Descripcion"),
        Etiqueta = Texto(reader, "Etiqueta"),
        NumeroCaja = Entero(reader, "NumeroCaja"),
        LoteEtiqueta = Texto(reader, "LoteEtiqueta"),
        TipoMovimiento = Texto(reader, "TipoMovimiento"),
        Cantidad = Entero(reader, "Cantidad"),
        Ubicacion = Texto(reader, "Ubicacion"),
        EstadoCalidad = Texto(reader, "EstadoCalidad"),
        NumeroOF = Texto(reader, "NumeroOF"),
        Responsable = Texto(reader, "Responsable"),
        Observaciones = Texto(reader, "Observaciones"),
        ReferenciaOperacion = Texto(reader, "ReferenciaOperacion")
    };

    [HttpGet]
    public async Task<IActionResult> Entrada(
        int? parteId,
        string? numeroOF,
        int? solicitudProduccionId,
        bool entregaOF = false,
        CancellationToken cancellationToken = default)
    {
        var sesion = ValidarSesion();
        if (sesion != null) return sesion;

        var vm = new AlmacenPTEntradaLoteVm
        {
            EsEntregaOF = entregaOF,
            SolicitudProduccionID = solicitudProduccionId,
            ParteIDEsperada = parteId,
            NumeroOFEsperada = numeroOF?.Trim(),
            EstadoCalidad = "Liberado",
            Observaciones = entregaOF
                ? "Recepción de producto terminado validado por OF."
                : "Recepción masiva de producto terminado por escáner.",
            OperacionToken =
                AlmacenOFEntregaService.CrearToken()
        };

        if (entregaOF)
        {
            if (!solicitudProduccionId.HasValue
                || solicitudProduccionId.Value <= 0
                || !parteId.HasValue
                || parteId.Value <= 0)
            {
                Mensaje(
                    "warning",
                    "No se recibió una OF y un número de parte válidos.");

                return RedirectToAction(
                    "Index",
                    "AlmacenOF");
            }

            await using var connection =
                await AbrirConexionAsync(cancellationToken);

            var contexto =
                await AlmacenOFEntregaService
                    .CargarProductoTerminadoAsync(
                        connection,
                        transaction: null,
                        solicitudProduccionId.Value,
                        parteId.Value,
                        cancellationToken);

            if (contexto == null)
            {
                Mensaje(
                    "warning",
                    "El número de parte no está validado dentro de la OF.");

                return RedirectToAction(
                    "Index",
                    "AlmacenOF");
            }

            if (string.IsNullOrWhiteSpace(contexto.NumeroOF))
            {
                Mensaje(
                    "warning",
                    "Planeación todavía no asigna un número de OF válido.");

                return RedirectToAction(
                    "Index",
                    "AlmacenOF");
            }

            if (contexto.Pendiente < 1m)
            {
                Mensaje(
                    "warning",
                    "El producto terminado de esta OF ya fue entregado completamente.");

                return RedirectToAction(
                    "Index",
                    "AlmacenOF");
            }

            vm.NumeroOFEsperada = contexto.NumeroOF;
            vm.NumeroParteEsperada = contexto.Codigo;
            vm.CantidadPendienteOF = contexto.Pendiente;
        }

        await CargarEntradaLoteAsync(
            vm,
            cancellationToken);

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Entrada(
        AlmacenPTEntradaLoteVm model,
        string? accion,
        CancellationToken cancellationToken)
    {
        var sesion = ValidarSesion();
        if (sesion != null) return sesion;

        model.CodigosEscaneados =
            NormalizarCodigosEscaneados(
                model.CodigosEscaneados);

        model.EstadoCalidad = "Liberado";

        model.Observaciones =
            model.Observaciones?.Trim();

        model.OperacionToken =
            model.OperacionToken?.Trim()
            ?? string.Empty;

        accion =
            string.IsNullOrWhiteSpace(accion)
                ? "convertir"
                : accion.Trim().ToLowerInvariant();

        ViewData["AccionEntradaPT"] = accion;

        if (!AlmacenOFEntregaService.TokenValido(
                model.OperacionToken))
        {
            ModelState.AddModelError(
                nameof(model.OperacionToken),
                "La operación expiró. Recarga la pantalla e inténtalo nuevamente.");
        }

        var codigos =
            SepararCodigosEscaneados(
                model.CodigosEscaneados);

        if (codigos.Count == 0)
        {
            ModelState.AddModelError(
                nameof(model.CodigosEscaneados),
                "Escanea al menos una etiqueta.");
        }

        if (model.EsEntregaOF
            && (!model.SolicitudProduccionID.HasValue
                || model.SolicitudProduccionID.Value <= 0
                || !model.ParteIDEsperada.HasValue
                || model.ParteIDEsperada.Value <= 0))
        {
            ModelState.AddModelError(
                nameof(model.SolicitudProduccionID),
                "La OF de origen no es válida.");
        }

        if (accion is not "convertir" and not "registrar")
        {
            ModelState.AddModelError(
                string.Empty,
                "Acción de entrada no válida.");
        }

        if (!ModelState.IsValid)
        {
            await CargarEntradaLoteAsync(
                model,
                cancellationToken);

            return View(model);
        }

        if (accion == "convertir")
        {
            await using var connection =
                await AbrirConexionAsync(cancellationToken);

            model.Resultados =
                await ConvertirCodigosEntradaAsync(
                    connection,
                    transaction: null,
                    model,
                    codigos,
                    cancellationToken);

            await CargarEntradaLoteAsync(
                model,
                cancellationToken);

            ViewData["MostrarResultadosEntradaPT"] = true;
            ViewData["MensajeConversionEntradaPT"] =
                $"Conversión terminada: {model.Resultados.Count(x => x.Valido)} válida(s) y {model.Resultados.Count(x => !x.Valido)} por revisar.";

            ModelState.Remove(nameof(model.CodigosEscaneados));
            model.CodigosEscaneados =
                NormalizarCodigosEscaneados(
                    model.CodigosEscaneados);

            return View(model);
        }

        if (!model.UbicacionID.HasValue
            || model.UbicacionID.Value <= 0)
        {
            ModelState.AddModelError(
                nameof(model.UbicacionID),
                "Selecciona la ubicación donde se recibirán las cajas.");
        }

        if (!ModelState.IsValid)
        {
            await using var connection =
                await AbrirConexionAsync(cancellationToken);

            model.Resultados =
                await ConvertirCodigosEntradaAsync(
                    connection,
                    transaction: null,
                    model,
                    codigos,
                    cancellationToken);

            await CargarEntradaLoteAsync(
                model,
                cancellationToken);

            return View(model);
        }

        await using var db =
            await AbrirConexionAsync(cancellationToken);

        await using var transaction =
            (SqlTransaction)await db.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

        try
        {
            model.Resultados =
                await ConvertirCodigosEntradaAsync(
                    db,
                    transaction,
                    model,
                    codigos,
                    cancellationToken);

            if (!model.Resultados.Any()
                || model.Resultados.Any(x => !x.Valido))
            {
                await transaction.RollbackAsync(
                    cancellationToken);

                ModelState.AddModelError(
                    string.Empty,
                    "Hay códigos inválidos, repetidos o que no corresponden a la OF. Corrige la lista y vuelve a convertir.");

                await CargarEntradaLoteAsync(
                    model,
                    cancellationToken);

                return View(model);
            }

            var prefijoReferencia =
                $"WEB-PT-LOTE-{model.OperacionToken.ToUpperInvariant()}";

            const string batchExistsSql = @"
SELECT CASE WHEN EXISTS
(
    SELECT 1
    FROM dbo.AlmacenPT_Movimientos
        WITH (UPDLOCK, HOLDLOCK)
    WHERE ReferenciaOperacion LIKE @Prefijo + N'%'
) THEN 1 ELSE 0 END;";

            await using (var batchExists =
                new SqlCommand(
                    batchExistsSql,
                    db,
                    transaction))
            {
                batchExists.Parameters.Add(
                    "@Prefijo",
                    SqlDbType.NVarChar,
                    120).Value = prefijoReferencia;

                if (Convert.ToInt32(
                        await batchExists.ExecuteScalarAsync(
                            cancellationToken)
                        ?? 0) == 1)
                {
                    await transaction.RollbackAsync(
                        cancellationToken);

                    Mensaje(
                        "warning",
                        "Este lote de escaneos ya había sido registrado. No se generaron duplicados.");

                    return model.EsEntregaOF
                        ? RedirectToAction(
                            "Index",
                            "AlmacenOF")
                        : RedirectToAction(nameof(Entrada));
                }
            }

            var consecutivos =
                new Dictionary<string, int>(
                    StringComparer.OrdinalIgnoreCase);

            var totalPiezas = 0;
            var numeroRegistro = 0;

            foreach (var item in model.Resultados)
            {
                numeroRegistro++;

                var claveConsecutivo =
                    $"{item.ParteID}|{item.NumeroOF}";

                if (!consecutivos.TryGetValue(
                        claveConsecutivo,
                        out var numeroCaja))
                {
                    const string maxCajaSql = @"
SELECT ISNULL(MAX(NumeroCaja), 0)
FROM dbo.AlmacenPT_Cajas WITH (UPDLOCK, HOLDLOCK)
WHERE ParteID = @ParteID
  AND
  (
      (@NumeroOF IS NULL AND NumeroOF IS NULL)
      OR LTRIM(RTRIM(ISNULL(NumeroOF, N''))) =
         LTRIM(RTRIM(ISNULL(@NumeroOF, N'')))
  );";

                    await using var maxCaja =
                        new SqlCommand(
                            maxCajaSql,
                            db,
                            transaction);

                    maxCaja.Parameters.Add(
                        "@ParteID",
                        SqlDbType.Int).Value =
                        item.ParteID;

                    maxCaja.Parameters.Add(
                        "@NumeroOF",
                        SqlDbType.NVarChar,
                        80).Value =
                        string.IsNullOrWhiteSpace(item.NumeroOF)
                            ? DBNull.Value
                            : item.NumeroOF;

                    numeroCaja =
                        Convert.ToInt32(
                            await maxCaja.ExecuteScalarAsync(
                                cancellationToken)
                            ?? 0);
                }

                numeroCaja++;
                consecutivos[claveConsecutivo] =
                    numeroCaja;

                const string cajaSql = @"
INSERT dbo.AlmacenPT_Cajas
(
    ParteID,
    NumeroOF,
    Etiqueta,
    NumeroCaja,
    CantidadInicial,
    LoteEtiqueta,
    EstadoCalidad,
    UbicacionID,
    FechaEntrada,
    FechaCreacion,
    CreadoPor,
    Activo
)
OUTPUT INSERTED.CajaID
VALUES
(
    @ParteID,
    @NumeroOF,
    @Etiqueta,
    @NumeroCaja,
    @Cantidad,
    @Lote,
    @Estado,
    @UbicacionID,
    SYSDATETIME(),
    SYSUTCDATETIME(),
    @Usuario,
    1
);";

                int cajaID;

                await using (var caja =
                    new SqlCommand(
                        cajaSql,
                        db,
                        transaction))
                {
                    caja.Parameters.Add(
                        "@ParteID",
                        SqlDbType.Int).Value =
                        item.ParteID;

                    caja.Parameters.Add(
                        "@NumeroOF",
                        SqlDbType.NVarChar,
                        80).Value =
                        item.NumeroOF;

                    caja.Parameters.Add(
                        "@Etiqueta",
                        SqlDbType.NVarChar,
                        120).Value =
                        item.CodigoOriginal;

                    caja.Parameters.Add(
                        "@NumeroCaja",
                        SqlDbType.Int).Value =
                        numeroCaja;

                    caja.Parameters.Add(
                        "@Cantidad",
                        SqlDbType.Int).Value =
                        item.Cantidad;

                    caja.Parameters.Add(
                        "@Lote",
                        SqlDbType.NVarChar,
                        120).Value =
                        item.Lote;

                    caja.Parameters.Add(
                        "@Estado",
                        SqlDbType.NVarChar,
                        30).Value =
                        model.EstadoCalidad;

                    caja.Parameters.Add(
                        "@UbicacionID",
                        SqlDbType.Int).Value =
                        model.UbicacionID!.Value;

                    caja.Parameters.Add(
                        "@Usuario",
                        SqlDbType.NVarChar,
                        120).Value =
                        UsuarioNombre;

                    cajaID =
                        Convert.ToInt32(
                            await caja.ExecuteScalarAsync(
                                cancellationToken));
                }

                var observacionMovimiento =
                    ConstruirObservacionEntradaEscaneada(
                        model.Observaciones,
                        item.Designacion);

                var referencia =
                    $"{prefijoReferencia}-{numeroRegistro:D4}";

                await InsertarMovimientoEntradaPTAsync(
                    db,
                    transaction,
                    cajaID,
                    item,
                    model,
                    observacionMovimiento,
                    referencia,
                    tipoMovimiento: "Entrada",
                    cancellationToken);

                if (!string.Equals(
                        model.EstadoCalidad,
                        "Liberado",
                        StringComparison.OrdinalIgnoreCase))
                {
                    await InsertarMovimientoEntradaPTAsync(
                        db,
                        transaction,
                        cajaID,
                        item,
                        model,
                        observacionMovimiento,
                        $"RET-PT-LOTE-{model.OperacionToken.ToUpperInvariant()}-{numeroRegistro:D4}",
                        tipoMovimiento: "Retencion",
                        cancellationToken);
                }

                totalPiezas += item.Cantidad;
            }

            await transaction.CommitAsync(
                cancellationToken);

            Mensaje(
                "success",
                $"Se registraron {model.Resultados.Count} caja(s) y {totalPiezas:N0} pieza(s) mediante escáner.");

            return model.EsEntregaOF
                ? RedirectToAction(
                    "Index",
                    "AlmacenOF")
                : RedirectToAction(nameof(Entrada));
        }
        catch (SqlException ex)
            when (ex.Number is 2601 or 2627)
        {
            await transaction.RollbackAsync(
                cancellationToken);

            ModelState.AddModelError(
                string.Empty,
                "Una de las etiquetas o referencias ya fue registrada. No se guardó ninguna caja del lote.");

            await CargarEntradaLoteAsync(
                model,
                cancellationToken);

            return View(model);
        }
        catch
        {
            await transaction.RollbackAsync(
                cancellationToken);

            throw;
        }
    }

    private async Task<List<AlmacenPTCodigoBarrasVm>>
        ConvertirCodigosEntradaAsync(
            SqlConnection connection,
            SqlTransaction? transaction,
            AlmacenPTEntradaLoteVm model,
            IReadOnlyList<string> codigos,
            CancellationToken cancellationToken)
    {
        var resultados =
            new List<AlmacenPTCodigoBarrasVm>();

        var catalogoPartes =
            await CargarCatalogoPartesNormalizadoAsync(
                connection,
                transaction,
                cancellationToken);

        AlmacenOFEntregaContexto? contextoOF = null;

        if (model.EsEntregaOF
            && model.SolicitudProduccionID.HasValue
            && model.ParteIDEsperada.HasValue)
        {
            contextoOF =
                await AlmacenOFEntregaService
                    .CargarProductoTerminadoAsync(
                        connection,
                        transaction,
                        model.SolicitudProduccionID.Value,
                        model.ParteIDEsperada.Value,
                        cancellationToken);

            if (contextoOF != null)
            {
                model.NumeroOFEsperada =
                    contextoOF.NumeroOF;
                model.NumeroParteEsperada =
                    contextoOF.Codigo;
                model.CantidadPendienteOF =
                    contextoOF.Pendiente;
            }
        }

        var etiquetasDelLote =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        var totalParseado = 0;

        for (var index = 0; index < codigos.Count; index++)
        {
            var codigo = codigos[index];

            var item =
                new AlmacenPTCodigoBarrasVm
                {
                    Renglon = index + 1,
                    CodigoOriginal = codigo
                };

            if (!AlmacenPTCodigoBarrasService.TryParse(
                    codigo,
                    out var parseado,
                    out var error)
                || parseado == null)
            {
                item.Mensaje = error;
                resultados.Add(item);
                continue;
            }

            item.Parseado = true;
            item.NumeroOF = parseado.NumeroOF;
            item.NumeroParte = parseado.NumeroParte;
            item.Designacion = parseado.Designacion;
            item.Cantidad = parseado.Cantidad;
            item.Lote = parseado.Lote;

            item.RepetidoEnLote =
                !etiquetasDelLote.Add(
                    parseado.CodigoOriginal);

            if (item.RepetidoEnLote)
            {
                item.Mensaje =
                    "Código repetido dentro del lote de escaneo.";
            }

            var claveEscaneada =
                NormalizarNumeroParte(
                    parseado.NumeroParte);

            if (string.IsNullOrWhiteSpace(
                    claveEscaneada))
            {
                item.Mensaje =
                    "El número de parte no contiene letras o números válidos.";
            }
            else if (!catalogoPartes.TryGetValue(
                claveEscaneada,
                out var candidatos))
            {
                item.Mensaje =
                    $"No existe un registro activo en NumeroParte ni ReferenciaSAP con la secuencia: {claveEscaneada}.";
            }
            else
            {
                var exactos =
                    candidatos
                        .Where(x =>
                            string.Equals(
                                x.NumeroParte.Trim(),
                                parseado.NumeroParte.Trim(),
                                StringComparison.OrdinalIgnoreCase)
                            || string.Equals(
                                x.ReferenciaSAP.Trim(),
                                parseado.NumeroParte.Trim(),
                                StringComparison.OrdinalIgnoreCase))
                        .ToList();

                ParteCatalogoEscaneo? candidato = null;

                if (exactos.Count == 1)
                {
                    candidato = exactos[0];
                }
                else if (candidatos.Count == 1)
                {
                    candidato = candidatos[0];
                }

                if (candidato == null)
                {
                    var opciones =
                        string.Join(
                            ", ",
                            candidatos
                                .Select(x => x.NumeroParte)
                                .Distinct(
                                    StringComparer.OrdinalIgnoreCase)
                                .Take(10));

                    item.Mensaje =
                        $"Coincidencia ambigua. Estos catálogos tienen la misma secuencia alfanumérica: {opciones}.";
                }
                else
                {
                    item.ExisteEnCatalogo = true;
                    item.ParteID = candidato.ParteID;
                    item.NumeroParteCatalogo =
                        string.IsNullOrWhiteSpace(
                            candidato.NumeroParte)
                            ? candidato.ReferenciaSAP
                            : candidato.NumeroParte;

                    item.DescripcionCatalogo =
                        candidato.Descripcion;

                    item.CoincidenciaNormalizada =
                        !string.Equals(
                            candidato.NumeroParte.Trim(),
                            parseado.NumeroParte.Trim(),
                            StringComparison.OrdinalIgnoreCase);
                }
            }

            const string etiquetaSql = @"
SELECT CASE WHEN EXISTS
(
    SELECT 1
    FROM dbo.AlmacenPT_Cajas
    WHERE Etiqueta = @Etiqueta
) THEN 1 ELSE 0 END;";

            await using (var etiqueta =
                new SqlCommand(
                    etiquetaSql,
                    connection))
            {
                if (transaction != null)
                    etiqueta.Transaction = transaction;

                etiqueta.Parameters.Add(
                    "@Etiqueta",
                    SqlDbType.NVarChar,
                    120).Value =
                    parseado.CodigoOriginal;

                item.YaRegistrado =
                    Convert.ToInt32(
                        await etiqueta.ExecuteScalarAsync(
                            cancellationToken)
                        ?? 0) == 1;

                if (item.YaRegistrado)
                {
                    item.Mensaje =
                        "La etiqueta ya existe en Almacén PT.";
                }
            }

            if (model.EsEntregaOF)
            {
                if (contextoOF == null)
                {
                    item.CoincideConOF = false;
                    item.CoincideConParte = false;
                    item.Mensaje =
                        "La OF o el número de parte ya no están disponibles.";
                }
                else
                {
                    item.CoincideConOF =
                        string.Equals(
                            NormalizarNumeroParte(
                                parseado.NumeroOF),
                            NormalizarNumeroParte(
                                contextoOF.NumeroOF),
                            StringComparison.OrdinalIgnoreCase);

                    item.CoincideConParte =
                        item.ExisteEnCatalogo
                        && item.ParteID ==
                           model.ParteIDEsperada;

                    if (!item.CoincideConOF)
                    {
                        item.Mensaje =
                            $"La etiqueta pertenece a la OF {parseado.NumeroOF}, no a {contextoOF.NumeroOF}.";
                    }
                    else if (!item.CoincideConParte)
                    {
                        item.Mensaje =
                            $"La etiqueta pertenece a la parte {parseado.NumeroParte}, no a {contextoOF.Codigo}.";
                    }
                }
            }

            if (item.Parseado)
                totalParseado += item.Cantidad;

            if (item.Valido)
            {
                var mensajeParte =
                    item.CoincidenciaNormalizada
                        ? $"Enlazado al catálogo {item.NumeroParteCatalogo} mediante NumeroParte o ReferenciaSAP."
                        : "Número de parte exacto.";

                item.Mensaje =
                    DesignacionCoincide(
                        item.Designacion,
                        item.DescripcionCatalogo)
                        ? $"{mensajeParte} Listo para registrar."
                        : $"{mensajeParte} La designación difiere del catálogo; se conservará la del catálogo.";
            }

            resultados.Add(item);
        }

        if (model.EsEntregaOF
            && contextoOF != null
            && totalParseado > contextoOF.Pendiente)
        {
            foreach (var item in resultados
                .Where(x => x.Parseado))
            {
                item.CoincideConOF = false;
                item.Mensaje =
                    $"El lote suma {totalParseado:N0} PZS y la OF solo tiene {contextoOF.Pendiente:N0} PZS pendientes.";
            }
        }

        return resultados;
    }
    private async Task InsertarMovimientoEntradaPTAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int cajaID,
        AlmacenPTCodigoBarrasVm item,
        AlmacenPTEntradaLoteVm model,
        string observaciones,
        string referencia,
        string tipoMovimiento,
        CancellationToken cancellationToken)
    {
        const string sql = @"
INSERT dbo.AlmacenPT_Movimientos
(
    CajaID,
    ParteID,
    NumeroOF,
    TipoMovimiento,
    Cantidad,
    UbicacionID,
    EstadoCalidad,
    ResponsableUsuarioID,
    Observaciones,
    FechaMovimiento,
    FechaCreacion,
    CreadoPor,
    Activo,
    ReferenciaOperacion
)
VALUES
(
    @CajaID,
    @ParteID,
    @NumeroOF,
    @Tipo,
    @Cantidad,
    @UbicacionID,
    @Estado,
    @UsuarioID,
    @Observaciones,
    SYSDATETIME(),
    SYSUTCDATETIME(),
    @Usuario,
    1,
    @Referencia
);";

        await using var command =
            new SqlCommand(
                sql,
                connection,
                transaction);

        command.Parameters.Add(
            "@CajaID",
            SqlDbType.Int).Value =
            cajaID;

        command.Parameters.Add(
            "@ParteID",
            SqlDbType.Int).Value =
            item.ParteID;

        command.Parameters.Add(
            "@NumeroOF",
            SqlDbType.NVarChar,
            80).Value =
            item.NumeroOF;

        command.Parameters.Add(
            "@Tipo",
            SqlDbType.NVarChar,
            30).Value =
            tipoMovimiento;

        command.Parameters.Add(
            "@Cantidad",
            SqlDbType.Int).Value =
            item.Cantidad;

        command.Parameters.Add(
            "@UbicacionID",
            SqlDbType.Int).Value =
            model.UbicacionID!.Value;

        command.Parameters.Add(
            "@Estado",
            SqlDbType.NVarChar,
            30).Value =
            model.EstadoCalidad;

        command.Parameters.Add(
            "@UsuarioID",
            SqlDbType.Int).Value =
            UsuarioID.HasValue
                ? UsuarioID.Value
                : DBNull.Value;

        command.Parameters.Add(
            "@Observaciones",
            SqlDbType.NVarChar,
            800).Value =
            observaciones;

        command.Parameters.Add(
            "@Usuario",
            SqlDbType.NVarChar,
            120).Value =
            UsuarioNombre;

        command.Parameters.Add(
            "@Referencia",
            SqlDbType.NVarChar,
            120).Value =
            referencia;

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    private async Task CargarEntradaLoteAsync(
        AlmacenPTEntradaLoteVm vm,
        CancellationToken cancellationToken)
    {
        await using var connection =
            await AbrirConexionAsync(cancellationToken);

        vm.Ubicaciones =
            await CargarUbicacionesAsync(
                connection,
                cancellationToken);

        vm.EstadosCalidad =
            EstadosCalidad
                .Select(
                    (x, i) =>
                        new AlmacenSelectVm
                        {
                            Id = i + 1,
                            Texto = x,
                            Extra = x
                        })
                .ToList();

        if (vm.ParteIDEsperada.HasValue
            && vm.ParteIDEsperada.Value > 0)
        {
            const string sql = @"
SELECT
    NumeroParte,
    COALESCE
    (
        NULLIF(LTRIM(RTRIM(Designacion)), N''),
        NULLIF(LTRIM(RTRIM(Descripcion)), N''),
        N''
    ) AS Descripcion
FROM dbo.ERP_Partes
WHERE ParteID = @ParteID
  AND Activo = 1;";

            await using var command =
                new SqlCommand(
                    sql,
                    connection);

            command.Parameters.Add(
                "@ParteID",
                SqlDbType.Int).Value =
                vm.ParteIDEsperada.Value;

            await using var reader =
                await command.ExecuteReaderAsync(
                    cancellationToken);

            if (await reader.ReadAsync(
                    cancellationToken))
            {
                vm.NumeroParteEsperada =
                    Texto(reader, "NumeroParte");

                vm.DescripcionParteEsperada =
                    Texto(reader, "Descripcion");
            }
        }
    }

    private static List<string> SepararCodigosEscaneados(
        string? valor) =>
        (valor ?? string.Empty)
            .Split(
                new[] { "\r\n", "\n", "\r" },
                StringSplitOptions.RemoveEmptyEntries
                | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

    private static string NormalizarCodigosEscaneados(
        string? valor) =>
        string.Join(
            Environment.NewLine,
            SepararCodigosEscaneados(valor));

    private static string ConstruirObservacionEntradaEscaneada(
        string? observaciones,
        string designacion)
    {
        var baseTexto =
            string.IsNullOrWhiteSpace(observaciones)
                ? "Recepción masiva de PT por escáner de código de barras."
                : observaciones.Trim();

        var resultado =
            $"{baseTexto} Designación etiqueta: {designacion}.";

        return resultado.Length <= 800
            ? resultado
            : resultado[..800];
    }

    private sealed record ParteCatalogoEscaneo(
        int ParteID,
        string NumeroParte,
        string ReferenciaSAP,
        string Descripcion);

    private static async Task<
        Dictionary<string, List<ParteCatalogoEscaneo>>>
        CargarCatalogoPartesNormalizadoAsync(
            SqlConnection connection,
            SqlTransaction? transaction,
            CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT
    ParteID,
    COALESCE
    (
        NULLIF(LTRIM(RTRIM(NumeroParte)), N''),
        N''
    ) AS NumeroParte,
    COALESCE
    (
        NULLIF(LTRIM(RTRIM(ReferenciaSAP)), N''),
        N''
    ) AS ReferenciaSAP,
    COALESCE
    (
        NULLIF(LTRIM(RTRIM(Designacion)), N''),
        NULLIF(LTRIM(RTRIM(Descripcion)), N''),
        N''
    ) AS Descripcion
FROM dbo.ERP_Partes
WHERE Activo = 1
  AND
  (
      NULLIF(LTRIM(RTRIM(NumeroParte)), N'') IS NOT NULL
      OR NULLIF(LTRIM(RTRIM(ReferenciaSAP)), N'') IS NOT NULL
  )
ORDER BY ParteID;";

        await using var command =
            new SqlCommand(
                sql,
                connection);

        if (transaction != null)
            command.Transaction = transaction;

        var resultado =
            new Dictionary<
                string,
                List<ParteCatalogoEscaneo>>(
                    StringComparer.OrdinalIgnoreCase);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        while (await reader.ReadAsync(
            cancellationToken))
        {
            var candidato =
                new ParteCatalogoEscaneo(
                    Entero(reader, "ParteID"),
                    Texto(reader, "NumeroParte"),
                    Texto(reader, "ReferenciaSAP"),
                    Texto(reader, "Descripcion"));

            AgregarCandidatoCatalogo(
                resultado,
                candidato.NumeroParte,
                candidato);

            AgregarCandidatoCatalogo(
                resultado,
                candidato.ReferenciaSAP,
                candidato);
        }

        return resultado;
    }

    private static void AgregarCandidatoCatalogo(
        Dictionary<string, List<ParteCatalogoEscaneo>> resultado,
        string identificador,
        ParteCatalogoEscaneo candidato)
    {
        var clave =
            NormalizarNumeroParte(
                identificador);

        if (string.IsNullOrWhiteSpace(clave))
            return;

        if (!resultado.TryGetValue(
                clave,
                out var lista))
        {
            lista =
                new List<ParteCatalogoEscaneo>();

            resultado[clave] = lista;
        }

        if (!lista.Any(x =>
            x.ParteID == candidato.ParteID))
        {
            lista.Add(candidato);
        }
    }
    private static string NormalizarNumeroParte(
        string? numeroParte)
    {
        if (string.IsNullOrWhiteSpace(numeroParte))
            return string.Empty;

        return new string(
            numeroParte
                .Where(char.IsLetterOrDigit)
                .Select(char.ToUpperInvariant)
                .ToArray());
    }
    private static bool DesignacionCoincide(
        string etiqueta,
        string catalogo)
    {
        static string Normalizar(string valor) =>
            new(
                valor
                    .Where(char.IsLetterOrDigit)
                    .Select(char.ToUpperInvariant)
                    .ToArray());

        var a = Normalizar(etiqueta);
        var b = Normalizar(catalogo);

        return string.IsNullOrWhiteSpace(a)
            || string.IsNullOrWhiteSpace(b)
            || a == b
            || a.Contains(b, StringComparison.Ordinal)
            || b.Contains(a, StringComparison.Ordinal);
    }

    [HttpGet]
    public async Task<IActionResult> Movimiento(int? parteId, int? cajaId, string? tipo, CancellationToken cancellationToken)
    {
        var sesion = ValidarSesion();
        if (sesion != null) return sesion;
        var vm = new AlmacenPTMovimientoFormVm
        {
            ParteID = parteId.GetValueOrDefault(),
            CajaID = cajaId,
            TipoMovimiento = TiposPermitidos.Contains(tipo ?? string.Empty) ? tipo! : "Salida"
        };
        await CargarMovimientoAsync(vm, cancellationToken);
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Movimiento(AlmacenPTMovimientoFormVm model, CancellationToken cancellationToken)
    {
        var sesion = ValidarSesion();
        if (sesion != null) return sesion;
        model.NumeroOF = model.NumeroOF?.Trim();
        model.Observaciones = model.Observaciones?.Trim();

        // REVISION_ALMACEN_V8_AJUSTES_PT
        if ((model.TipoMovimiento == "AjustePositivo" || model.TipoMovimiento == "AjusteNegativo")
            && string.IsNullOrWhiteSpace(model.Observaciones))
        {
            ModelState.AddModelError(
                nameof(model.Observaciones),
                "El motivo del ajuste es obligatorio.");
        }

        if (!TiposPermitidos.Contains(model.TipoMovimiento))
            ModelState.AddModelError(nameof(model.TipoMovimiento), "Tipo de movimiento inválido.");
        if (!string.IsNullOrWhiteSpace(model.EstadoCalidad) && !EstadosCalidad.Contains(model.EstadoCalidad))
            ModelState.AddModelError(nameof(model.EstadoCalidad), "Estado de calidad inválido.");
        if (!ModelState.IsValid)
        {
            await CargarMovimientoAsync(model, cancellationToken);
            return View(model);
        }

        await using var connection = await AbrirConexionAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            if (model.CajaID.HasValue)
            {
                const string cajaSql = "SELECT ParteID FROM dbo.AlmacenPT_Cajas WITH (UPDLOCK,HOLDLOCK) WHERE CajaID=@CajaID AND Activo=1;";
                await using var caja = new SqlCommand(cajaSql, connection, transaction);
                caja.Parameters.Add("@CajaID", SqlDbType.Int).Value = model.CajaID.Value;
                var parteCaja = await caja.ExecuteScalarAsync(cancellationToken);
                if (parteCaja == null || Convert.ToInt32(parteCaja) != model.ParteID)
                {
                    ModelState.AddModelError(nameof(model.CajaID), "La caja no corresponde al número de parte seleccionado.");
                    await transaction.RollbackAsync(cancellationToken);
                    await CargarMovimientoAsync(model, cancellationToken);
                    return View(model);
                }
            }

            if (EsSalidaPT(model.TipoMovimiento) || model.TipoMovimiento == "Retencion")
            {
                var saldoSql = model.CajaID.HasValue
                    ? @"SELECT ISNULL(Disponible,0) FROM dbo.vw_AlmacenPTInventarioCaja WHERE CajaID=@CajaID;"
                    : @"SELECT ISNULL(Disponible,0) FROM dbo.vw_AlmacenPTInventario WHERE ParteID=@ParteID;";
                await using var saldo = new SqlCommand(saldoSql, connection, transaction);
                saldo.Parameters.Add("@CajaID", SqlDbType.Int).Value = model.CajaID.HasValue ? model.CajaID.Value : DBNull.Value;
                saldo.Parameters.Add("@ParteID", SqlDbType.Int).Value = model.ParteID;
                var disponible = Convert.ToInt32(await saldo.ExecuteScalarAsync(cancellationToken) ?? 0);
                if (disponible < model.Cantidad)
                {
                    ModelState.AddModelError(nameof(model.Cantidad), $"Existencia insuficiente. Disponible: {disponible} piezas.");
                    await transaction.RollbackAsync(cancellationToken);
                    await CargarMovimientoAsync(model, cancellationToken);
                    return View(model);
                }
            }

            if (model.TipoMovimiento == "Liberacion")
            {
                var retenidoSql = model.CajaID.HasValue
                    ? @"SELECT ISNULL(Retenido,0) FROM dbo.vw_AlmacenPTInventarioCaja WHERE CajaID=@CajaID;"
                    : @"SELECT ISNULL(Retenido,0) FROM dbo.vw_AlmacenPTInventario WHERE ParteID=@ParteID;";
                await using var retenidoCommand = new SqlCommand(retenidoSql, connection, transaction);
                retenidoCommand.Parameters.Add("@CajaID", SqlDbType.Int).Value = model.CajaID.HasValue ? model.CajaID.Value : DBNull.Value;
                retenidoCommand.Parameters.Add("@ParteID", SqlDbType.Int).Value = model.ParteID;
                var retenido = Convert.ToInt32(await retenidoCommand.ExecuteScalarAsync(cancellationToken) ?? 0);
                if (retenido < model.Cantidad)
                {
                    ModelState.AddModelError(nameof(model.Cantidad), $"No se pueden liberar {model.Cantidad} piezas. Retenido actual: {retenido}.");
                    await transaction.RollbackAsync(cancellationToken);
                    await CargarMovimientoAsync(model, cancellationToken);
                    return View(model);
                }
            }

            const string insertSql = @"
INSERT dbo.AlmacenPT_Movimientos
(CajaID, ParteID, NumeroOF, TipoMovimiento, Cantidad, UbicacionID,
 EstadoCalidad, ResponsableUsuarioID, Observaciones, FechaMovimiento,
 FechaCreacion, CreadoPor, Activo, ReferenciaOperacion)
VALUES
(@CajaID,@ParteID,@NumeroOF,@Tipo,@Cantidad,@UbicacionID,
 @Estado,@UsuarioID,@Observaciones,SYSDATETIME(),SYSUTCDATETIME(),@Usuario,1,@Referencia);";
            await using var insert = new SqlCommand(insertSql, connection, transaction);
            insert.Parameters.Add("@CajaID", SqlDbType.Int).Value = model.CajaID.HasValue ? model.CajaID.Value : DBNull.Value;
            insert.Parameters.Add("@ParteID", SqlDbType.Int).Value = model.ParteID;
            insert.Parameters.Add("@NumeroOF", SqlDbType.NVarChar, 80).Value = string.IsNullOrWhiteSpace(model.NumeroOF) ? DBNull.Value : model.NumeroOF;
            insert.Parameters.Add("@Tipo", SqlDbType.NVarChar, 30).Value = model.TipoMovimiento;
            insert.Parameters.Add("@Cantidad", SqlDbType.Int).Value = model.Cantidad;
            insert.Parameters.Add("@UbicacionID", SqlDbType.Int).Value = model.UbicacionID.HasValue ? model.UbicacionID.Value : DBNull.Value;
            insert.Parameters.Add("@Estado", SqlDbType.NVarChar, 30).Value = string.IsNullOrWhiteSpace(model.EstadoCalidad) ? DBNull.Value : model.EstadoCalidad;
            insert.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = UsuarioID.HasValue ? UsuarioID.Value : DBNull.Value;
            insert.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 800).Value = string.IsNullOrWhiteSpace(model.Observaciones) ? DBNull.Value : model.Observaciones.Trim();
            insert.Parameters.Add("@Usuario", SqlDbType.NVarChar, 120).Value = UsuarioNombre;
            insert.Parameters.Add("@Referencia", SqlDbType.NVarChar, 120).Value = $"MAN-PT-{Guid.NewGuid():N}";
            await insert.ExecuteNonQueryAsync(cancellationToken);

            if (model.CajaID.HasValue && !string.IsNullOrWhiteSpace(model.EstadoCalidad))
            {
                const string updateCaja = @"
UPDATE dbo.AlmacenPT_Cajas
SET EstadoCalidad=@Estado, UbicacionID=COALESCE(@UbicacionID,UbicacionID),
    FechaModificacion=SYSUTCDATETIME(), ActualizadoPor=@Usuario
WHERE CajaID=@CajaID;";
                await using var update = new SqlCommand(updateCaja, connection, transaction);
                update.Parameters.Add("@Estado", SqlDbType.NVarChar, 30).Value = model.EstadoCalidad;
                update.Parameters.Add("@UbicacionID", SqlDbType.Int).Value = model.UbicacionID.HasValue ? model.UbicacionID.Value : DBNull.Value;
                update.Parameters.Add("@Usuario", SqlDbType.NVarChar, 120).Value = UsuarioNombre;
                update.Parameters.Add("@CajaID", SqlDbType.Int).Value = model.CajaID.Value;
                await update.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        Mensaje("success", "Movimiento de Almacén PT registrado correctamente.");
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> NivelesStock(string? q, bool soloSinConfigurar = false, CancellationToken cancellationToken = default)
    {
        var sesion = ValidarSesion();
        if (sesion != null) return sesion;

        var vm = new AlmacenStockNivelesVm
        {
            Modulo = "PT",
            Busqueda = q?.Trim(),
            SoloSinConfigurar = soloSinConfigurar
        };

        await using var connection = await AbrirConexionAsync(cancellationToken);
        if (!await ExisteColumnaAsync(connection, "dbo.vw_AlmacenPTInventario", "StockConfigurado", cancellationToken))
        {
            Mensaje("warning", "Ejecuta 04_Actualizar_Stock_e_Integracion_Almacen.sql antes de configurar niveles.");
            return RedirectToAction(nameof(Index));
        }

        const string sql = @"
SELECT TOP (100)
    ParteID AS CatalogoID, NumeroParte AS Codigo, Descripcion, N'PZA' AS Unidad,
    Disponible, StockMinimo, StockAviso, StockConfigurado
FROM dbo.vw_AlmacenPTInventario
WHERE (@Q IS NULL OR NumeroParte LIKE '%' + @Q + '%' OR Descripcion LIKE '%' + @Q + '%' OR Cliente LIKE '%' + @Q + '%')
  AND (@SoloPendientes = 0 OR StockConfigurado = 0)
ORDER BY CASE WHEN StockConfigurado = 0 THEN 0 ELSE 1 END, NumeroParte;";

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Q", SqlDbType.NVarChar, 250).Value = string.IsNullOrWhiteSpace(vm.Busqueda) ? DBNull.Value : vm.Busqueda;
        command.Parameters.Add("@SoloPendientes", SqlDbType.Bit).Value = vm.SoloSinConfigurar;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            vm.Items.Add(new AlmacenStockNivelItemVm
            {
                CatalogoID = Entero(reader, "CatalogoID"),
                Codigo = Texto(reader, "Codigo"),
                Descripcion = Texto(reader, "Descripcion"),
                Unidad = Texto(reader, "Unidad"),
                Disponible = DecimalValor(reader, "Disponible"),
                StockMinimo = DecimalValor(reader, "StockMinimo"),
                StockAviso = DecimalValor(reader, "StockAviso"),
                Configurado = Convert.ToBoolean(reader["StockConfigurado"])
            });
        }

        vm.Total = vm.Items.Count;
        vm.Configurados = vm.Items.Count(x => x.Configurado);
        vm.Pendientes = vm.Items.Count(x => !x.Configurado);
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> NivelesStock(AlmacenStockNivelesVm model, CancellationToken cancellationToken = default)
    {
        var sesion = ValidarSesion();
        if (sesion != null) return sesion;

        model.Modulo = "PT";
        if (model.Items == null || model.Items.Count == 0)
        {
            Mensaje("warning", "No se recibieron números de parte para actualizar.");
            return RedirectToAction(nameof(NivelesStock));
        }

        for (var i = 0; i < model.Items.Count; i++)
        {
            var item = model.Items[i];
            if (item.StockMinimo < 0 || item.StockMinimo != decimal.Truncate(item.StockMinimo))
                ModelState.AddModelError($"Items[{i}].StockMinimo", "El stock mínimo PT debe ser un número entero no negativo.");
            if (item.StockAviso < item.StockMinimo || item.StockAviso != decimal.Truncate(item.StockAviso))
                ModelState.AddModelError($"Items[{i}].StockAviso", "El stock de aviso PT debe ser entero e igual o mayor al mínimo.");
        }

        if (!ModelState.IsValid)
        {
            model.Total = model.Items.Count;
            model.Configurados = model.Items.Count(x => x.Configurado);
            model.Pendientes = model.Total - model.Configurados;
            return View(model);
        }

        await using var connection = await AbrirConexionAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            const string sql = @"
UPDATE dbo.ERP_Partes
SET StockMinimo=@Minimo, StockAviso=@Aviso, StockConfigurado=1,
    FechaModificacion=GETDATE(), UsuarioModificacionID=@UsuarioID
WHERE ParteID=@Id AND Activo=1;";

            foreach (var item in model.Items)
            {
                await using var command = new SqlCommand(sql, connection, transaction);
                command.Parameters.Add("@Minimo", SqlDbType.Int).Value = decimal.ToInt32(item.StockMinimo);
                command.Parameters.Add("@Aviso", SqlDbType.Int).Value = decimal.ToInt32(item.StockAviso);
                command.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = UsuarioID.HasValue ? UsuarioID.Value : DBNull.Value;
                command.Parameters.Add("@Id", SqlDbType.Int).Value = item.CatalogoID;
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        Mensaje("success", $"Se actualizaron los niveles de stock de {model.Items.Count} números de parte PT.");
        return RedirectToAction(nameof(NivelesStock), new { q = model.Busqueda, soloSinConfigurar = model.SoloSinConfigurar });
    }

    [HttpGet]
    public async Task<IActionResult> Configurar(int id, CancellationToken cancellationToken)
    {
        var sesion = ValidarSesion();
        if (sesion != null) return sesion;
        await using var connection = await AbrirConexionAsync(cancellationToken);
        const string sql = "SELECT ParteID,NumeroParte,Descripcion,StockMinimo,StockAviso FROM dbo.ERP_Partes WHERE ParteID=@Id;";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Id", SqlDbType.Int).Value = id;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return NotFound();
        return View(new AlmacenPTStockFormVm
        {
            ParteID = Entero(reader, "ParteID"),
            NumeroParte = Texto(reader, "NumeroParte"),
            Descripcion = Texto(reader, "Descripcion"),
            StockMinimo = Entero(reader, "StockMinimo"),
            StockAviso = Entero(reader, "StockAviso")
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Configurar(AlmacenPTStockFormVm model, CancellationToken cancellationToken)
    {
        var sesion = ValidarSesion();
        if (sesion != null) return sesion;
        if (model.StockAviso < model.StockMinimo)
            ModelState.AddModelError(nameof(model.StockAviso), "El nivel de aviso debe ser igual o mayor al stock mínimo.");
        if (!ModelState.IsValid) return View(model);

        await using var connection = await AbrirConexionAsync(cancellationToken);
        const string sql = @"
UPDATE dbo.ERP_Partes
SET StockMinimo=@Minimo, StockAviso=@Aviso, StockConfigurado=1,
    FechaModificacion=GETDATE(), UsuarioModificacionID=@UsuarioID
WHERE ParteID=@Id;";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Minimo", SqlDbType.Int).Value = model.StockMinimo;
        command.Parameters.Add("@Aviso", SqlDbType.Int).Value = model.StockAviso;
        command.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = UsuarioID.HasValue ? UsuarioID.Value : DBNull.Value;
        command.Parameters.Add("@Id", SqlDbType.Int).Value = model.ParteID;
        await command.ExecuteNonQueryAsync(cancellationToken);
        Mensaje("success", "Niveles de stock PT actualizados.");
        return RedirectToAction(nameof(Index));
    }

    private async Task CargarEntradaAsync(AlmacenPTEntradaFormVm vm, CancellationToken cancellationToken)
    {
        await using var connection = await AbrirConexionAsync(cancellationToken);
        vm.Partes = await CargarPartesAsync(connection, cancellationToken);
        vm.Ubicaciones = await CargarUbicacionesAsync(connection, cancellationToken);
        vm.EstadosCalidad = EstadosCalidad.Select((x, i) => new AlmacenSelectVm { Id = i + 1, Texto = x, Extra = x }).ToList();
    }

    private async Task CargarMovimientoAsync(AlmacenPTMovimientoFormVm vm, CancellationToken cancellationToken)
    {
        await using var connection = await AbrirConexionAsync(cancellationToken);
        vm.Partes = await CargarPartesAsync(connection, cancellationToken);
        vm.Ubicaciones = await CargarUbicacionesAsync(connection, cancellationToken);
        vm.TiposMovimiento = TiposPermitidos.Select((x, i) => new AlmacenSelectVm { Id = i + 1, Texto = x, Extra = x }).ToList();
        vm.EstadosCalidad = EstadosCalidad.Select((x, i) => new AlmacenSelectVm { Id = i + 1, Texto = x, Extra = x }).ToList();

        const string cajasSql = @"
SELECT c.CajaID, c.ParteID, c.Etiqueta, c.NumeroCaja,
       ISNULL(v.Disponible,0) AS Disponible
FROM dbo.AlmacenPT_Cajas c
LEFT JOIN dbo.vw_AlmacenPTInventarioCaja v ON v.CajaID=c.CajaID
WHERE c.Activo=1 AND (@ParteID=0 OR c.ParteID=@ParteID)
ORDER BY c.FechaEntrada DESC, c.CajaID DESC;";
        await using var command = new SqlCommand(cajasSql, connection);
        command.Parameters.Add("@ParteID", SqlDbType.Int).Value = vm.ParteID;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            vm.Cajas.Add(new AlmacenSelectVm
            {
                Id = Entero(reader, "CajaID"),
                Texto = $"{Texto(reader, "Etiqueta")} · Caja {Entero(reader, "NumeroCaja")} · Disponible {Entero(reader, "Disponible")}",
                Extra = Entero(reader, "ParteID").ToString()
            });
        }
    }

    private static async Task<List<AlmacenSelectVm>> CargarPartesAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var rows = new List<AlmacenSelectVm>();
        const string sql = @"
SELECT TOP (1500) ParteID, NumeroParte, COALESCE(NULLIF(Designacion,''),Descripcion) AS Texto
FROM dbo.ERP_Partes WHERE Activo=1 ORDER BY NumeroParte;";
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new AlmacenSelectVm
            {
                Id = Entero(reader, "ParteID"),
                Texto = $"{Texto(reader, "NumeroParte")} · {Texto(reader, "Texto")}"
            });
        }
        return rows;
    }

    private static async Task<List<AlmacenSelectVm>> CargarUbicacionesAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var rows = new List<AlmacenSelectVm>();
        const string sql = @"
SELECT UbicacionID, Almacen, Rack, Nivel, Posicion
FROM dbo.ERP_Ubicaciones
WHERE Activo=1 AND (Almacen='PT' OR Almacen='GENERAL')
ORDER BY Almacen,Rack,Nivel,Posicion;";
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new AlmacenSelectVm
            {
                Id = Entero(reader, "UbicacionID"),
                Texto = string.Join(" · ", new[] { Texto(reader, "Almacen"), Texto(reader, "Rack"), Texto(reader, "Nivel"), Texto(reader, "Posicion") }.Where(x => !string.IsNullOrWhiteSpace(x)))
            });
        }
        return rows;
    }
}
















