using ERP.NSQuell.Models.ViewModels.Almacen;
using ERP.NSQuell.Servicios.Almacen;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Text;

namespace ERP.NSQuell.Controllers;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AlmacenMPController : AlmacenBaseController
{
    private static readonly string[] TiposPermitidos =
    {
        "Entrada", "Salida", "Retorno", "Consumo", "Scrap", "AjustePositivo", "AjusteNegativo"
    };

    public AlmacenMPController(IConfiguration configuration) : base(configuration) { }
    [HttpGet]
    public async Task<IActionResult> Index(
        string? q,
        string? estado,
        string? tipoMP,
        CancellationToken cancellationToken)
    {
        var sesion = ValidarSesion();
        if (sesion != null)
            return sesion;

        var tipoMpFiltro = NormalizarTipoMPFiltro(tipoMP);
        ViewData["TipoMPFiltro"] = tipoMpFiltro ?? string.Empty;

        var vm = new AlmacenMPIndexVm
        {
            Busqueda = q?.Trim(),
            Estado = estado?.Trim().ToUpperInvariant()
        };

        await using var connection =
            await AbrirConexionAsync(cancellationToken);

        if (!await ExisteObjetoAsync(
                connection,
                "dbo.vw_AlmacenMPInventario",
                "V",
                cancellationToken)
            || !await ExisteColumnaAsync(
                connection,
                "dbo.vw_AlmacenMPInventario",
                "StockConfigurado",
                cancellationToken)
            || !await ExisteColumnaAsync(
                connection,
                "dbo.AlmacenMP_Movimientos",
                "ReferenciaOperacion",
                cancellationToken)
            || !await ExisteColumnaAsync(
                connection,
                "dbo.AlmacenMP_Movimientos",
                "TipoMP",
                cancellationToken))
        {
            vm.Configurado = false;
            vm.MensajeConfiguracion =
                "Falta ejecutar la instalacion completa del almacen MP y la migracion MOLIDO/VIRGEN.";

            return View(vm);
        }

        const string sql = @"
WITH Tipos AS
(
    SELECT N'VIRGEN' AS TipoMP, 0 AS OrdenTipo
    UNION ALL
    SELECT N'MOLIDO' AS TipoMP, 1 AS OrdenTipo
),
Movimientos AS
(
    SELECT
        movimiento.MaterialID,
        CASE
            WHEN UPPER(LTRIM(RTRIM(ISNULL(movimiento.TipoMP, N'')))) = N'MOLIDO'
                THEN N'MOLIDO'
            ELSE N'VIRGEN'
        END AS TipoMP,
        SUM
        (
            CASE
                WHEN movimiento.TipoMovimiento IN
                     (N'Entrada', N'Retorno', N'Ajuste', N'AjustePositivo')
                    THEN movimiento.Cantidad
                ELSE 0
            END
        ) AS Entradas,
        SUM
        (
            CASE
                WHEN movimiento.TipoMovimiento IN
                     (N'Salida', N'Consumo', N'Scrap', N'AjusteNegativo')
                    THEN movimiento.Cantidad
                ELSE 0
            END
        ) AS Salidas,
        MAX(movimiento.FechaMovimiento) AS UltimoMovimiento
    FROM dbo.AlmacenMP_Movimientos movimiento
    WHERE movimiento.Activo = 1
    GROUP BY
        movimiento.MaterialID,
        CASE
            WHEN UPPER(LTRIM(RTRIM(ISNULL(movimiento.TipoMP, N'')))) = N'MOLIDO'
                THEN N'MOLIDO'
            ELSE N'VIRGEN'
        END
),
Inventario AS
(
    SELECT
        material.MaterialID,
        material.Codigo,
        material.Nombre,
        N'KG' AS Unidad,
        tipo.TipoMP,
        tipo.OrdenTipo,
        ISNULL(movimiento.Entradas, 0) AS Entradas,
        ISNULL(movimiento.Salidas, 0) AS Salidas,
        ISNULL(movimiento.Entradas, 0) - ISNULL(movimiento.Salidas, 0) AS Saldo,
        CONVERT(DECIMAL(18,4), 0) AS Solicitado,
        material.StockMinimo,
        material.StockAviso,
        CASE
            WHEN movimiento.MaterialID IS NULL THEN CONVERT(BIT, 0)
            ELSE material.StockConfigurado
        END AS StockConfigurado,
        CASE
            WHEN movimiento.MaterialID IS NULL THEN N'SIN_CONFIGURAR'
            WHEN material.StockConfigurado = 0 THEN N'SIN_CONFIGURAR'
            WHEN ISNULL(movimiento.Entradas, 0) - ISNULL(movimiento.Salidas, 0) <= material.StockMinimo THEN N'ROJO'
            WHEN ISNULL(movimiento.Entradas, 0) - ISNULL(movimiento.Salidas, 0) <= material.StockAviso THEN N'AMARILLO'
            ELSE N'VERDE'
        END AS Semaforo,
        movimiento.UltimoMovimiento
    FROM dbo.ERP_Materiales material
    CROSS JOIN Tipos tipo
    LEFT JOIN Movimientos movimiento
        ON movimiento.MaterialID = material.MaterialID
       AND movimiento.TipoMP = tipo.TipoMP
    WHERE material.Activo = 1
)
SELECT TOP (1000)
    MaterialID, Codigo, Nombre, Unidad, TipoMP,
    Entradas, Salidas, Saldo, Solicitado,
    StockMinimo, StockAviso, StockConfigurado,
    Semaforo, UltimoMovimiento
FROM Inventario
WHERE
    (
        @Q IS NULL
        OR Codigo LIKE N'%' + @Q + N'%'
        OR Nombre LIKE N'%' + @Q + N'%'
    )
    AND (@Estado IS NULL OR Semaforo = @Estado)
    AND (@TipoMP IS NULL OR TipoMP = @TipoMP)
ORDER BY
    OrdenTipo,
    CASE Semaforo
        WHEN N'ROJO' THEN 0
        WHEN N'AMARILLO' THEN 1
        WHEN N'VERDE' THEN 2
        ELSE 3
    END,
    Nombre,
    Codigo;";

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

            command.Parameters.Add(
                "@TipoMP",
                SqlDbType.NVarChar,
                20).Value =
                string.IsNullOrWhiteSpace(tipoMpFiltro)
                    ? DBNull.Value
                    : tipoMpFiltro;

            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                vm.Existencias.Add(
                    new AlmacenMPExistenciaVm
                    {
                        MaterialID = Entero(reader, "MaterialID"),
                        Codigo = Texto(reader, "Codigo"),
                        Nombre = Texto(reader, "Nombre"),
                        Unidad = "KG",
                        TipoMP = Texto(reader, "TipoMP"),
                        Entradas = DecimalValor(reader, "Entradas"),
                        Salidas = DecimalValor(reader, "Salidas"),
                        Saldo = DecimalValor(reader, "Saldo"),
                        Solicitado = DecimalValor(reader, "Solicitado"),
                        StockMinimo = DecimalValor(reader, "StockMinimo"),
                        StockAviso = DecimalValor(reader, "StockAviso"),
                        Semaforo = Texto(reader, "Semaforo"),
                        StockConfigurado = Convert.ToBoolean(reader["StockConfigurado"]),
                        UltimoMovimiento = Fecha(reader, "UltimoMovimiento")
                    });
            }
        }

        const string movimientosSql = @"
SELECT TOP (5)
    movimiento.MovimientoID,
    movimiento.FechaMovimiento,
    movimiento.MaterialID,
    material.Codigo,
    material.Nombre AS Material,
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
FROM dbo.AlmacenMP_Movimientos movimiento
INNER JOIN dbo.ERP_Materiales material
    ON material.MaterialID =
       movimiento.MaterialID
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
        // ALMACEN_MP_SOLICITADO_POR_MATERIAL_V1_0
        // Mientras Planeacion no distinga VIRGEN y MOLIDO, la demanda de OF
        // se presenta solamente en VIRGEN para no duplicar el solicitado.
        const string solicitadoPorMaterialSql = @"
WITH Requerido AS
(
    SELECT
        solicitud.SolicitudProduccionID,
        solicitud.FolioSolicitud,
        solicitud.NumeroOFRecibida,
        material.MaterialID,
        SUM
        (
            CONVERT
            (
                DECIMAL(18,4),
                ISNULL(detalle.CantidadMpKg, 0)
            )
        ) AS CantidadRequerida
    FROM dbo.SolicitudesProduccion solicitud
    INNER JOIN dbo.SolicitudesProduccionDetalle detalle
        ON detalle.SolicitudProduccionID = solicitud.SolicitudProduccionID
       AND detalle.Activo = 1
    INNER JOIN dbo.ERP_Materiales material
        ON material.Activo = 1
       AND
       (
           detalle.MaterialID = material.MaterialID
           OR
           (
               detalle.MaterialID IS NULL
               AND UPPER(LTRIM(RTRIM(ISNULL(detalle.MaterialCodigo, N'')))) =
                   UPPER(LTRIM(RTRIM(material.Codigo)))
           )
       )
    WHERE solicitud.Activo = 1
      AND ISNULL(detalle.CantidadMpKg, 0) > 0
    GROUP BY
        solicitud.SolicitudProduccionID,
        solicitud.FolioSolicitud,
        solicitud.NumeroOFRecibida,
        material.MaterialID
),
Pendiente AS
(
    SELECT
        requerido.MaterialID,
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
                    WHEN movimiento.TipoMovimiento IN (N'Salida', N'Consumo')
                        THEN movimiento.Cantidad
                    WHEN movimiento.TipoMovimiento = N'Retorno'
                        THEN -movimiento.Cantidad
                    ELSE 0
                END
            ) AS CantidadEntregada
        FROM dbo.AlmacenMP_Movimientos movimiento
        WHERE movimiento.Activo = 1
          AND movimiento.MaterialID = requerido.MaterialID
          AND
          (
              (
                  NULLIF(LTRIM(RTRIM(requerido.FolioSolicitud)), N'') IS NOT NULL
                  AND LTRIM(RTRIM(movimiento.NumeroOF)) =
                      LTRIM(RTRIM(requerido.FolioSolicitud))
              )
              OR
              (
                  NULLIF(LTRIM(RTRIM(requerido.NumeroOFRecibida)), N'') IS NOT NULL
                  AND LTRIM(RTRIM(movimiento.NumeroOF)) =
                      LTRIM(RTRIM(requerido.NumeroOFRecibida))
              )
          )
    ) entregado
)
SELECT
    MaterialID,
    CONVERT(DECIMAL(18,4), SUM(CantidadPendiente)) AS Solicitado
FROM Pendiente
WHERE CantidadPendiente > 0.0005
GROUP BY MaterialID;";

        var solicitadoPorMaterial = new Dictionary<int, decimal>();

        await using (var solicitadoCommand =
            new SqlCommand(solicitadoPorMaterialSql, connection))
        await using (var solicitadoReader =
            await solicitadoCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await solicitadoReader.ReadAsync(cancellationToken))
            {
                solicitadoPorMaterial[
                    Entero(solicitadoReader, "MaterialID")] =
                    DecimalValor(solicitadoReader, "Solicitado");
            }
        }

        foreach (var existencia in vm.Existencias)
        {
            decimal solicitado;
            existencia.Solicitado =
                string.Equals(
                    existencia.TipoMP,
                    "VIRGEN",
                    StringComparison.OrdinalIgnoreCase)
                && solicitadoPorMaterial.TryGetValue(
                    existencia.MaterialID,
                    out solicitado)
                    ? solicitado
                    : 0m;
        }
        // Misma estructura operativa utilizada en Inventario PT.
        const string resumenSql = @"
WITH Requerido AS
(
    SELECT
        s.SolicitudProduccionID,
        s.FolioSolicitud,
        s.NumeroOFRecibida,
        material.MaterialID,
        SUM
        (
            CONVERT
            (
                DECIMAL(18,4),
                ISNULL(detalle.CantidadMpKg, 0)
            )
        ) AS CantidadRequerida
    FROM dbo.SolicitudesProduccion s
    INNER JOIN dbo.SolicitudesProduccionDetalle detalle
        ON detalle.SolicitudProduccionID =
           s.SolicitudProduccionID
       AND detalle.Activo = 1
    INNER JOIN dbo.ERP_Materiales material
        ON material.Activo = 1
       AND
       (
           detalle.MaterialID = material.MaterialID
           OR
           (
               detalle.MaterialID IS NULL
               AND UPPER
                   (
                       LTRIM
                       (
                           RTRIM
                           (
                               ISNULL(detalle.MaterialCodigo, N'')
                           )
                       )
                   ) =
                   UPPER(LTRIM(RTRIM(material.Codigo)))
           )
       )
    WHERE s.Activo = 1
      AND ISNULL(detalle.CantidadMpKg, 0) > 0
    GROUP BY
        s.SolicitudProduccionID,
        s.FolioSolicitud,
        s.NumeroOFRecibida,
        material.MaterialID
),
Pendiente AS
(
    SELECT
        requerido.SolicitudProduccionID,
        requerido.MaterialID,
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
        FROM dbo.AlmacenMP_Movimientos movimiento
        WHERE movimiento.Activo = 1
          AND movimiento.MaterialID =
              requerido.MaterialID
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
FROM dbo.AlmacenMP_Movimientos
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
    public async Task<IActionResult> Historial(
        string? material,
        string? q,
        string? tipo,
        string? numeroOF,
        string? responsable,
        string? lote,
        DateTime? desde,
        DateTime? hasta,
        int pagina = 1,
        CancellationToken cancellationToken = default)
    {
        var sesion = ValidarSesion();
        if (sesion != null) return sesion;

        if (desde.HasValue && hasta.HasValue && hasta.Value.Date < desde.Value.Date)
            (desde, hasta) = (hasta, desde);

        var vm = new AlmacenMPHistorialVm
        {
            FiltroMaterial = material?.Trim(),
            Busqueda = q?.Trim(),
            TipoMovimiento = TiposPermitidos.Contains(tipo ?? string.Empty) ? tipo : null,
            NumeroOF = numeroOF?.Trim(),
            Responsable = responsable?.Trim(),
            Lote = lote?.Trim(),
            Desde = desde?.Date,
            Hasta = hasta?.Date,
            Pagina = Math.Max(1, pagina),
            TamanoPagina = 50,
            TiposMovimiento = TiposPermitidos.ToList()
        };

        await using var connection = await AbrirConexionAsync(cancellationToken);
        if (!await ExisteObjetoAsync(connection, "dbo.AlmacenMP_Movimientos", "U", cancellationToken))
        {
            Mensaje("warning", "No existe la tabla de movimientos MP.");
            return RedirectToAction(nameof(Index));
        }
        if (!await ExisteColumnaAsync(connection, "dbo.AlmacenMP_Movimientos", "ReferenciaOperacion", cancellationToken))
        {
            Mensaje("warning", "Ejecuta el script 04 corregido antes de consultar el historial MP.");
            return RedirectToAction(nameof(Index));
        }

        await CargarFiltrosHistorialMPAsync(connection, vm, cancellationToken);

        const string fromWhere = @"
FROM dbo.AlmacenMP_Movimientos mm
INNER JOIN dbo.ERP_Materiales m ON m.MaterialID = mm.MaterialID
LEFT JOIN dbo.ERP_Ubicaciones u ON u.UbicacionID = mm.UbicacionID
LEFT JOIN dbo.Usuarios us ON us.UsuarioID = mm.ResponsableUsuarioID
LEFT JOIN dbo.Persona p ON p.PersonaID = us.PersonaID
WHERE mm.Activo = 1
  AND
  (
      @Material IS NULL
      OR
      (
          EXISTS (SELECT 1 FROM dbo.ERP_Materiales fm WHERE fm.Codigo = @Material)
          AND m.Codigo = @Material
      )
      OR
      (
          NOT EXISTS (SELECT 1 FROM dbo.ERP_Materiales fm WHERE fm.Codigo = @Material)
          AND (m.Codigo LIKE '%' + @Material + '%' OR m.Nombre LIKE '%' + @Material + '%')
      )
  )
  AND (@Q IS NULL OR ISNULL(mm.Seguimiento,'') LIKE '%' + @Q + '%' OR ISNULL(mm.ReferenciaOperacion,'') LIKE '%' + @Q + '%')
  AND (@Tipo IS NULL OR mm.TipoMovimiento = @Tipo)
  AND (@NumeroOF IS NULL OR mm.NumeroOF LIKE '%' + @NumeroOF + '%')
  AND (@Responsable IS NULL OR COALESCE(NULLIF(LTRIM(RTRIM(mm.EntregadoPorNombre)),''), NULLIF(LTRIM(RTRIM(CONCAT(p.Nombre,' ',p.ApellidoPaterno))),''), mm.CreadoPor, '') LIKE '%' + @Responsable + '%')
  AND (@Lote IS NULL OR mm.Lote LIKE '%' + @Lote + '%')
  AND (@Desde IS NULL OR mm.FechaMovimiento >= @Desde)
  AND (@Hasta IS NULL OR mm.FechaMovimiento < DATEADD(DAY,1,@Hasta))";

        await using (var count = new SqlCommand("SELECT COUNT_BIG(1) " + fromWhere, connection))
        {
            AgregarParametrosHistorialMP(count, vm);
            vm.TotalRegistros = Convert.ToInt32(await count.ExecuteScalarAsync(cancellationToken) ?? 0L);
        }

        vm.Pagina = Math.Min(vm.Pagina, vm.TotalPaginas);
        const string select = @"
SELECT
    mm.MovimientoID, mm.FechaMovimiento, mm.MaterialID,
    m.Codigo, m.Nombre AS Material, mm.TipoMovimiento,
    mm.Cantidad, mm.Unidad, mm.Lote,
    CONCAT(u.Almacen, ' / ', u.Rack,
        CASE WHEN u.Nivel IS NULL THEN '' ELSE ' / ' + u.Nivel END,
        CASE WHEN u.Posicion IS NULL THEN '' ELSE ' / ' + u.Posicion END) AS Ubicacion,
    ISNULL(mm.NumeroOF,'') AS NumeroOF,
    COALESCE(NULLIF(LTRIM(RTRIM(mm.EntregadoPorNombre)),''), NULLIF(LTRIM(RTRIM(CONCAT(p.Nombre,' ',p.ApellidoPaterno))),''), mm.CreadoPor, '') AS Responsable,
    ISNULL(mm.Seguimiento,'') AS Observaciones,
    ISNULL(mm.ReferenciaOperacion,'') AS ReferenciaOperacion ";

        var dataSql = select + fromWhere + @"
ORDER BY mm.FechaMovimiento DESC, mm.MovimientoID DESC
OFFSET @Offset ROWS FETCH NEXT @Tamano ROWS ONLY;";
        await using var command = new SqlCommand(dataSql, connection);
        AgregarParametrosHistorialMP(command, vm);
        command.Parameters.Add("@Offset", SqlDbType.Int).Value = (vm.Pagina - 1) * vm.TamanoPagina;
        command.Parameters.Add("@Tamano", SqlDbType.Int).Value = vm.TamanoPagina;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            vm.Movimientos.Add(LeerMovimientoMP(reader));

        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> ExportarHistorialCsv(
        string? material,
        string? q,
        string? tipo,
        string? numeroOF,
        string? responsable,
        string? lote,
        DateTime? desde,
        DateTime? hasta,
        CancellationToken cancellationToken = default)
    {
        var sesion = ValidarSesion();
        if (sesion != null) return sesion;

        if (desde.HasValue && hasta.HasValue && hasta.Value.Date < desde.Value.Date)
            (desde, hasta) = (hasta, desde);

        var filtro = new AlmacenMPHistorialVm
        {
            FiltroMaterial = material?.Trim(),
            Busqueda = q?.Trim(),
            TipoMovimiento = TiposPermitidos.Contains(tipo ?? string.Empty) ? tipo : null,
            NumeroOF = numeroOF?.Trim(),
            Responsable = responsable?.Trim(),
            Lote = lote?.Trim(),
            Desde = desde?.Date,
            Hasta = hasta?.Date
        };

        await using var connection = await AbrirConexionAsync(cancellationToken);
        if (!await ExisteColumnaAsync(connection, "dbo.AlmacenMP_Movimientos", "ReferenciaOperacion", cancellationToken))
        {
            Mensaje("warning", "Ejecuta el script 04 corregido antes de exportar el historial MP.");
            return RedirectToAction(nameof(Index));
        }

        const string sql = @"
SELECT TOP (10000)
    mm.MovimientoID, mm.FechaMovimiento, mm.MaterialID,
    m.Codigo, m.Nombre AS Material, mm.TipoMovimiento,
    mm.Cantidad, mm.Unidad, mm.Lote,
    CONCAT(u.Almacen, ' / ', u.Rack,
        CASE WHEN u.Nivel IS NULL THEN '' ELSE ' / ' + u.Nivel END,
        CASE WHEN u.Posicion IS NULL THEN '' ELSE ' / ' + u.Posicion END) AS Ubicacion,
    ISNULL(mm.NumeroOF,'') AS NumeroOF,
    COALESCE(NULLIF(LTRIM(RTRIM(mm.EntregadoPorNombre)),''), NULLIF(LTRIM(RTRIM(CONCAT(p.Nombre,' ',p.ApellidoPaterno))),''), mm.CreadoPor, '') AS Responsable,
    ISNULL(mm.Seguimiento,'') AS Observaciones,
    ISNULL(mm.ReferenciaOperacion,'') AS ReferenciaOperacion
FROM dbo.AlmacenMP_Movimientos mm
INNER JOIN dbo.ERP_Materiales m ON m.MaterialID = mm.MaterialID
LEFT JOIN dbo.ERP_Ubicaciones u ON u.UbicacionID = mm.UbicacionID
LEFT JOIN dbo.Usuarios us ON us.UsuarioID = mm.ResponsableUsuarioID
LEFT JOIN dbo.Persona p ON p.PersonaID = us.PersonaID
WHERE mm.Activo = 1
  AND
  (
      @Material IS NULL
      OR
      (
          EXISTS (SELECT 1 FROM dbo.ERP_Materiales fm WHERE fm.Codigo = @Material)
          AND m.Codigo = @Material
      )
      OR
      (
          NOT EXISTS (SELECT 1 FROM dbo.ERP_Materiales fm WHERE fm.Codigo = @Material)
          AND (m.Codigo LIKE '%' + @Material + '%' OR m.Nombre LIKE '%' + @Material + '%')
      )
  )
  AND (@Q IS NULL OR ISNULL(mm.Seguimiento,'') LIKE '%' + @Q + '%' OR ISNULL(mm.ReferenciaOperacion,'') LIKE '%' + @Q + '%')
  AND (@Tipo IS NULL OR mm.TipoMovimiento = @Tipo)
  AND (@NumeroOF IS NULL OR mm.NumeroOF LIKE '%' + @NumeroOF + '%')
  AND (@Responsable IS NULL OR COALESCE(NULLIF(LTRIM(RTRIM(mm.EntregadoPorNombre)),''), NULLIF(LTRIM(RTRIM(CONCAT(p.Nombre,' ',p.ApellidoPaterno))),''), mm.CreadoPor, '') LIKE '%' + @Responsable + '%')
  AND (@Lote IS NULL OR mm.Lote LIKE '%' + @Lote + '%')
  AND (@Desde IS NULL OR mm.FechaMovimiento >= @Desde)
  AND (@Hasta IS NULL OR mm.FechaMovimiento < DATEADD(DAY,1,@Hasta))
ORDER BY mm.FechaMovimiento DESC, mm.MovimientoID DESC;";

        await using var command = new SqlCommand(sql, connection);
        AgregarParametrosHistorialMP(command, filtro);
        var csv = new StringBuilder();
        csv.AppendLine("MovimientoID;Fecha;Codigo;Material;Tipo;Cantidad;Unidad;Lote;Ubicacion;OF;Responsable;Referencia;Observaciones");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var x = LeerMovimientoMP(reader);
            csv.AppendLine(string.Join(";", new[]
            {
                Csv(x.MovimientoID.ToString()),
                Csv(x.FechaMovimiento.ToString("dd/MM/yyyy HH:mm:ss")),
                Csv(x.Codigo), Csv(x.Material), Csv(x.TipoMovimiento),
                Csv(x.Cantidad.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)),
                Csv(x.Unidad), Csv(x.Lote), Csv(x.Ubicacion), Csv(x.NumeroOF),
                Csv(x.Responsable), Csv(x.ReferenciaOperacion), Csv(x.Observaciones)
            }));
        }

        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        return File(encoding.GetPreamble().Concat(encoding.GetBytes(csv.ToString())).ToArray(),
            "text/csv; charset=utf-8", $"Historial_MP_{DateTime.Now:yyyyMMdd_HHmm}.csv");
    }

    private static void AgregarParametrosHistorialMP(SqlCommand command, AlmacenMPHistorialVm filtro)
    {
        command.Parameters.Add("@Material", SqlDbType.NVarChar, 250).Value = string.IsNullOrWhiteSpace(filtro.FiltroMaterial) ? DBNull.Value : filtro.FiltroMaterial;
        command.Parameters.Add("@Q", SqlDbType.NVarChar, 250).Value = string.IsNullOrWhiteSpace(filtro.Busqueda) ? DBNull.Value : filtro.Busqueda;
        command.Parameters.Add("@Tipo", SqlDbType.NVarChar, 30).Value = string.IsNullOrWhiteSpace(filtro.TipoMovimiento) ? DBNull.Value : filtro.TipoMovimiento;
        command.Parameters.Add("@NumeroOF", SqlDbType.NVarChar, 80).Value = string.IsNullOrWhiteSpace(filtro.NumeroOF) ? DBNull.Value : filtro.NumeroOF;
        command.Parameters.Add("@Responsable", SqlDbType.NVarChar, 180).Value = string.IsNullOrWhiteSpace(filtro.Responsable) ? DBNull.Value : filtro.Responsable;
        command.Parameters.Add("@Lote", SqlDbType.NVarChar, 120).Value = string.IsNullOrWhiteSpace(filtro.Lote) ? DBNull.Value : filtro.Lote;
        command.Parameters.Add("@Desde", SqlDbType.Date).Value = filtro.Desde.HasValue ? filtro.Desde.Value.Date : DBNull.Value;
        command.Parameters.Add("@Hasta", SqlDbType.Date).Value = filtro.Hasta.HasValue ? filtro.Hasta.Value.Date : DBNull.Value;
    }

    private static async Task CargarFiltrosHistorialMPAsync(
        SqlConnection connection,
        AlmacenMPHistorialVm vm,
        CancellationToken cancellationToken)
    {
        const string materialesSql = @"
SELECT TOP (2000) MaterialID, Codigo, Nombre
FROM dbo.ERP_Materiales
WHERE Activo = 1
ORDER BY Codigo;";
        await using (var command = new SqlCommand(materialesSql, connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                vm.MaterialesFiltro.Add(new AlmacenSelectVm
                {
                    Id = Entero(reader, "MaterialID"),
                    Texto = $"{Texto(reader, "Codigo")} · {Texto(reader, "Nombre")}",
                    Extra = Texto(reader, "Codigo")
                });
            }
        }

        const string opcionesSql = @"
SELECT DISTINCT TOP (500) LTRIM(RTRIM(NumeroOF)) AS Valor
FROM dbo.AlmacenMP_Movimientos
WHERE Activo = 1 AND NULLIF(LTRIM(RTRIM(NumeroOF)), '') IS NOT NULL
ORDER BY Valor;

SELECT DISTINCT TOP (500)
    COALESCE(NULLIF(LTRIM(RTRIM(mm.EntregadoPorNombre)),''),
             NULLIF(LTRIM(RTRIM(CONCAT(p.Nombre,' ',p.ApellidoPaterno))),''),
             mm.CreadoPor, '') AS Valor
FROM dbo.AlmacenMP_Movimientos mm
LEFT JOIN dbo.Usuarios us ON us.UsuarioID = mm.ResponsableUsuarioID
LEFT JOIN dbo.Persona p ON p.PersonaID = us.PersonaID
WHERE mm.Activo = 1
  AND NULLIF(COALESCE(NULLIF(LTRIM(RTRIM(mm.EntregadoPorNombre)),''),
                      NULLIF(LTRIM(RTRIM(CONCAT(p.Nombre,' ',p.ApellidoPaterno))),''),
                      mm.CreadoPor, ''), '') IS NOT NULL
ORDER BY Valor;

SELECT DISTINCT TOP (500) LTRIM(RTRIM(Lote)) AS Valor
FROM dbo.AlmacenMP_Movimientos
WHERE Activo = 1 AND NULLIF(LTRIM(RTRIM(Lote)), '') IS NOT NULL
ORDER BY Valor;";
        await using var options = new SqlCommand(opcionesSql, connection);
        await using var optionReader = await options.ExecuteReaderAsync(cancellationToken);
        vm.OrdenesFiltro = await LeerListaTextoAsync(optionReader, cancellationToken);
        if (await optionReader.NextResultAsync(cancellationToken))
            vm.ResponsablesFiltro = await LeerListaTextoAsync(optionReader, cancellationToken);
        if (await optionReader.NextResultAsync(cancellationToken))
            vm.LotesFiltro = await LeerListaTextoAsync(optionReader, cancellationToken);
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

    private static AlmacenMPMovimientoListaVm LeerMovimientoMP(SqlDataReader reader) => new()
    {
        MovimientoID = EnteroLargo(reader, "MovimientoID"),
        FechaMovimiento = Fecha(reader, "FechaMovimiento") ?? DateTime.MinValue,
        MaterialID = Entero(reader, "MaterialID"),
        Codigo = Texto(reader, "Codigo"),
        Material = Texto(reader, "Material"),
        TipoMovimiento = Texto(reader, "TipoMovimiento"),
        Cantidad = DecimalValor(reader, "Cantidad"),
        Unidad = Texto(reader, "Unidad"),
        Lote = Texto(reader, "Lote"),
        Ubicacion = Texto(reader, "Ubicacion"),
        NumeroOF = Texto(reader, "NumeroOF"),
        Responsable = Texto(reader, "Responsable"),
        Observaciones = Texto(reader, "Observaciones"),
        ReferenciaOperacion = Texto(reader, "ReferenciaOperacion")
    };

    [HttpGet]
    public async Task<IActionResult> Materiales(CancellationToken cancellationToken)
    {
        var sesion = ValidarSesion();
        if (sesion != null) return sesion;

        var rows = new List<AlmacenMaterialFormVm>();
        await using var connection = await AbrirConexionAsync(cancellationToken);
        if (!await ExisteObjetoAsync(connection, "dbo.ERP_Materiales", "U", cancellationToken))
        {
            Mensaje("warning", "Primero ejecuta el script de estructura de Almacén.");
            return View(rows);
        }

        const string sql = @"
SELECT MaterialID, Codigo, Nombre, UnidadDefault, Proveedor,
       RequiereLote, StockMinimo, StockAviso, Activo
FROM dbo.ERP_Materiales
ORDER BY Activo DESC, Codigo;";
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new AlmacenMaterialFormVm
            {
                MaterialID = Entero(reader, "MaterialID"),
                Codigo = Texto(reader, "Codigo"),
                Nombre = Texto(reader, "Nombre"),
                UnidadDefault = Texto(reader, "UnidadDefault"),
                Proveedor = Texto(reader, "Proveedor"),
                RequiereLote = Convert.ToBoolean(reader["RequiereLote"]),
                StockMinimo = DecimalValor(reader, "StockMinimo"),
                StockAviso = DecimalValor(reader, "StockAviso"),
                Activo = Convert.ToBoolean(reader["Activo"])
            });
        }
        return View(rows);
    }

    [HttpGet]
    public async Task<IActionResult> Material(int? id, CancellationToken cancellationToken)
    {
        var sesion = ValidarSesion();
        if (sesion != null) return sesion;
        if (!id.HasValue) return View(new AlmacenMaterialFormVm());

        await using var connection = await AbrirConexionAsync(cancellationToken);
        const string sql = @"
SELECT MaterialID, Codigo, Nombre, UnidadDefault, Proveedor,
       RequiereLote, StockMinimo, StockAviso, Activo
FROM dbo.ERP_Materiales WHERE MaterialID = @Id;";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Id", SqlDbType.Int).Value = id.Value;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return NotFound();

        return View(new AlmacenMaterialFormVm
        {
            MaterialID = Entero(reader, "MaterialID"),
            Codigo = Texto(reader, "Codigo"),
            Nombre = Texto(reader, "Nombre"),
            UnidadDefault = Texto(reader, "UnidadDefault"),
            Proveedor = Texto(reader, "Proveedor"),
            RequiereLote = Convert.ToBoolean(reader["RequiereLote"]),
            StockMinimo = DecimalValor(reader, "StockMinimo"),
            StockAviso = DecimalValor(reader, "StockAviso"),
            Activo = Convert.ToBoolean(reader["Activo"])
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Material(AlmacenMaterialFormVm model, CancellationToken cancellationToken)
    {
        var sesion = ValidarSesion();
        if (sesion != null) return sesion;

        model.Codigo = model.Codigo?.Trim() ?? string.Empty;
        model.Nombre = model.Nombre?.Trim() ?? string.Empty;
        model.UnidadDefault = model.UnidadDefault?.Trim().ToUpperInvariant() ?? string.Empty;
        if (model.StockAviso < model.StockMinimo)
            ModelState.AddModelError(nameof(model.StockAviso), "El nivel de aviso debe ser igual o mayor al stock mínimo.");

        if (!ModelState.IsValid) return View(model);

        await using var connection = await AbrirConexionAsync(cancellationToken);
        const string duplicateSql = @"
SELECT
    (SELECT COUNT(*) FROM dbo.ERP_Materiales
     WHERE UPPER(Codigo)=UPPER(@Codigo) AND (@Id IS NULL OR MaterialID<>@Id))
  + (SELECT COUNT(*) FROM dbo.ERP_Embalajes
     WHERE UPPER(Codigo)=UPPER(@Codigo));";
        await using (var duplicate = new SqlCommand(duplicateSql, connection))
        {
            duplicate.Parameters.Add("@Codigo", SqlDbType.NVarChar, 80).Value = model.Codigo;
            duplicate.Parameters.Add("@Id", SqlDbType.Int).Value = model.MaterialID.HasValue ? model.MaterialID.Value : DBNull.Value;
            if (Convert.ToInt32(await duplicate.ExecuteScalarAsync(cancellationToken)) > 0)
            {
                ModelState.AddModelError(nameof(model.Codigo), "El código ya existe en MP o en Embalajes.");
                return View(model);
            }
        }

        var sql = model.MaterialID.HasValue
            ? @"UPDATE dbo.ERP_Materiales
SET Codigo=@Codigo, Nombre=@Nombre, UnidadDefault=@Unidad,
    Proveedor=@Proveedor, RequiereLote=@RequiereLote, StockMinimo=@Minimo,
    StockAviso=@Aviso, StockConfigurado=1, Activo=@Activo, FechaModificacion=SYSUTCDATETIME(),
    ActualizadoPor=@Usuario
WHERE MaterialID=@Id;"
            : @"INSERT dbo.ERP_Materiales
(Codigo, Nombre, UnidadDefault, Proveedor, RequiereLote,
 StockMinimo, StockAviso, StockConfigurado, FechaCreacion, CreadoPor, Activo)
VALUES (@Codigo,@Nombre,@Unidad,@Proveedor,@RequiereLote,
        @Minimo,@Aviso,1,SYSUTCDATETIME(),@Usuario,@Activo);";

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Codigo", SqlDbType.NVarChar, 80).Value = model.Codigo;
        command.Parameters.Add("@Nombre", SqlDbType.NVarChar, 250).Value = model.Nombre;
        command.Parameters.Add("@Unidad", SqlDbType.NVarChar, 20).Value = model.UnidadDefault;
        command.Parameters.Add("@Proveedor", SqlDbType.NVarChar, 200).Value = string.IsNullOrWhiteSpace(model.Proveedor) ? DBNull.Value : model.Proveedor.Trim();
        command.Parameters.Add("@RequiereLote", SqlDbType.Bit).Value = model.RequiereLote;
        command.Parameters.Add("@Minimo", SqlDbType.Decimal).Value = model.StockMinimo;
        command.Parameters.Add("@Aviso", SqlDbType.Decimal).Value = model.StockAviso;
        command.Parameters.Add("@Activo", SqlDbType.Bit).Value = model.Activo;
        command.Parameters.Add("@Usuario", SqlDbType.NVarChar, 120).Value = UsuarioNombre;
        command.Parameters.Add("@Id", SqlDbType.Int).Value = model.MaterialID.HasValue ? model.MaterialID.Value : DBNull.Value;
        await command.ExecuteNonQueryAsync(cancellationToken);
        Mensaje("success", model.MaterialID.HasValue ? "Material actualizado." : "Material registrado.");
        return RedirectToAction(nameof(Materiales));
    }

    [HttpGet]
    public async Task<IActionResult> NivelesStock(string? q, bool soloSinConfigurar = false, CancellationToken cancellationToken = default)
    {
        var sesion = ValidarSesion();
        if (sesion != null) return sesion;

        var vm = new AlmacenStockNivelesVm
        {
            Modulo = "MP",
            Busqueda = q?.Trim(),
            SoloSinConfigurar = soloSinConfigurar
        };

        await using var connection = await AbrirConexionAsync(cancellationToken);
        if (!await ExisteColumnaAsync(connection, "dbo.vw_AlmacenMPInventario", "StockConfigurado", cancellationToken))
        {
            Mensaje("warning", "Ejecuta 04_Actualizar_Stock_e_Integracion_Almacen.sql antes de configurar niveles.");
            return RedirectToAction(nameof(Index));
        }

        const string sql = @"
SELECT TOP (100)
    MaterialID AS CatalogoID, Codigo, Nombre AS Descripcion, Unidad,
    Saldo AS Disponible, StockMinimo, StockAviso, StockConfigurado
FROM dbo.vw_AlmacenMPInventario
WHERE (@Q IS NULL OR Codigo LIKE '%' + @Q + '%' OR Nombre LIKE '%' + @Q + '%')
  AND (@SoloPendientes = 0 OR StockConfigurado = 0)
ORDER BY CASE WHEN StockConfigurado = 0 THEN 0 ELSE 1 END, Nombre;";

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

        model.Modulo = "MP";
        if (model.Items == null || model.Items.Count == 0)
        {
            Mensaje("warning", "No se recibieron materiales para actualizar.");
            return RedirectToAction(nameof(NivelesStock));
        }

        for (var i = 0; i < model.Items.Count; i++)
        {
            var item = model.Items[i];
            if (item.StockMinimo < 0)
                ModelState.AddModelError($"Items[{i}].StockMinimo", "El stock mínimo no puede ser negativo.");
            if (item.StockAviso < item.StockMinimo)
                ModelState.AddModelError($"Items[{i}].StockAviso", "El stock de aviso debe ser igual o mayor al mínimo.");
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
UPDATE dbo.ERP_Materiales
SET StockMinimo=@Minimo, StockAviso=@Aviso, StockConfigurado=1,
    FechaModificacion=SYSUTCDATETIME(), ActualizadoPor=@Usuario
WHERE MaterialID=@Id AND Activo=1;";

            foreach (var item in model.Items)
            {
                await using var command = new SqlCommand(sql, connection, transaction);
                var minimo = command.Parameters.Add("@Minimo", SqlDbType.Decimal);
                minimo.Precision = 18; minimo.Scale = 3; minimo.Value = item.StockMinimo;
                var aviso = command.Parameters.Add("@Aviso", SqlDbType.Decimal);
                aviso.Precision = 18; aviso.Scale = 3; aviso.Value = item.StockAviso;
                command.Parameters.Add("@Usuario", SqlDbType.NVarChar, 120).Value = UsuarioNombre;
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

        Mensaje("success", $"Se actualizaron los niveles de stock de {model.Items.Count} materiales MP.");
        return RedirectToAction(nameof(NivelesStock), new { q = model.Busqueda, soloSinConfigurar = model.SoloSinConfigurar });
    }
    [HttpGet]
    public async Task<IActionResult> Movimiento(
        int? materialId,
        string? tipo,
        string? tipoMP,
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
            MaterialID = materialId.GetValueOrDefault(),
            TipoMovimiento = entregaOF
                ? "Salida"
                : TiposPermitidos.Contains(tipo ?? string.Empty)
                    ? tipo!
                    : "Entrada",
            TipoMP = NormalizarTipoMP(tipoMP),
            NumeroOF = entregaOF ? null : numeroOF?.Trim(),
            Cantidad = entregaOF ? 0m : cantidad.GetValueOrDefault(),
            Unidad = "KG",
            EsEntregaOF = entregaOF,
            SolicitudProduccionID = solicitudProduccionId,
            FechaMovimiento = DateTime.Now,
            Lote = "S/L",
            OperacionToken = AlmacenOFEntregaService.CrearToken()
        };

        if (entregaOF)
        {
            if (!solicitudProduccionId.HasValue
                || solicitudProduccionId.Value <= 0
                || vm.MaterialID <= 0)
            {
                Mensaje("warning", "No se recibio una OF y un material validos.");
                return RedirectToAction("Index", "AlmacenOF");
            }

            await using var connection =
                await AbrirConexionAsync(cancellationToken);

            var contexto = await AlmacenOFEntregaService.CargarMateriaPrimaAsync(
                connection,
                null,
                solicitudProduccionId.Value,
                vm.MaterialID,
                cancellationToken);

            if (contexto == null)
            {
                Mensaje("warning", "El material no pertenece a la OF seleccionada.");
                return RedirectToAction("Index", "AlmacenOF");
            }

            if (string.IsNullOrWhiteSpace(contexto.NumeroOF))
            {
                Mensaje("warning", "Planeacion todavia no asigna un numero de OF valido.");
                return RedirectToAction("Index", "AlmacenOF");
            }

            if (contexto.Pendiente <= 0.0005m)
            {
                Mensaje("warning", "La materia prima seleccionada ya fue entregada completamente.");
                return RedirectToAction("Index", "AlmacenOF");
            }

            vm.NumeroOF = contexto.NumeroOF;
            vm.Unidad = "KG";
            vm.CantidadPendienteOF = contexto.Pendiente;
            vm.Cantidad = contexto.Pendiente;
            vm.Observaciones = $"Entrega de materia prima para {contexto.NumeroOF}.";
        }

        await CargarMovimientoAsync(vm, cancellationToken);
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Movimiento(
        AlmacenMPMovimientoFormVm model,
        CancellationToken cancellationToken)
    {
        var sesion = ValidarSesion();
        if (sesion != null) return sesion;

        model.TipoMP = NormalizarTipoMP(model.TipoMP);
        model.Lote = "S/L";
        model.Unidad = "KG";
        model.NumeroOF = model.NumeroOF?.Trim();
        model.Observaciones = model.Observaciones?.Trim();
        model.OperacionToken = model.OperacionToken?.Trim() ?? string.Empty;
        model.FechaMovimiento = DateTime.Now;
        model.EsEntregaOF = model.EsEntregaOF || model.SolicitudProduccionID.HasValue;

        ModelState.Remove(nameof(model.Unidad));
        ModelState.Remove(nameof(model.Lote));
        ModelState.Remove(nameof(model.TipoMP));

        if (!AlmacenOFEntregaService.TokenValido(model.OperacionToken))
        {
            ModelState.AddModelError(
                nameof(model.OperacionToken),
                "La operacion expiro. Regresa al formulario e intentalo nuevamente.");
        }

        if (model.TipoMP is not ("VIRGEN" or "MOLIDO"))
        {
            ModelState.AddModelError(nameof(model.TipoMP), "Selecciona VIRGEN o MOLIDO.");
        }

        if (model.EsEntregaOF)
        {
            model.TipoMovimiento = "Salida";
            model.Lote = "S/L";
            model.UbicacionID = null;
            model.Unidad = "KG";

            ModelState.Remove(nameof(model.TipoMovimiento));
            ModelState.Remove(nameof(model.Lote));
            ModelState.Remove(nameof(model.UbicacionID));
            ModelState.Remove(nameof(model.NumeroOF));
            ModelState.Remove(nameof(model.CantidadPendienteOF));

            if (!model.SolicitudProduccionID.HasValue
                || model.SolicitudProduccionID.Value <= 0)
            {
                ModelState.AddModelError(
                    nameof(model.SolicitudProduccionID),
                    "La orden de fabricacion es obligatoria.");
            }

            if (model.MaterialID <= 0)
            {
                ModelState.AddModelError(
                    nameof(model.MaterialID),
                    "El material es obligatorio.");
            }

            if (string.IsNullOrWhiteSpace(model.Observaciones))
            {
                ModelState.AddModelError(
                    nameof(model.Observaciones),
                    "Las observaciones son obligatorias.");
            }
        }

        if ((model.TipoMovimiento == "AjustePositivo"
             || model.TipoMovimiento == "AjusteNegativo")
            && string.IsNullOrWhiteSpace(model.Observaciones))
        {
            ModelState.AddModelError(
                nameof(model.Observaciones),
                "El motivo del ajuste es obligatorio.");
        }

        if (!TiposPermitidos.Contains(model.TipoMovimiento))
        {
            ModelState.AddModelError(
                nameof(model.TipoMovimiento),
                "Tipo de movimiento invalido.");
        }

        if (!ModelState.IsValid)
        {
            await CargarMovimientoAsync(model, cancellationToken);
            return View(model);
        }

        await using var connection =
            await AbrirConexionAsync(cancellationToken);

        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

        var referencia =
            AlmacenOFEntregaService.CrearReferencia(
                "WEB-MP",
                model.OperacionToken);

        try
        {
            if (await AlmacenOFEntregaService.ExisteReferenciaMPAsync(
                    connection,
                    transaction,
                    referencia,
                    cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                Mensaje("warning", "Este movimiento ya habia sido registrado. No se creo un duplicado.");
                return model.EsEntregaOF
                    ? RedirectToAction("Index", "AlmacenOF")
                    : RedirectToAction(nameof(Index));
            }

            string codigo;

            if (model.EsEntregaOF)
            {
                var contexto = await AlmacenOFEntregaService.CargarMateriaPrimaAsync(
                    connection,
                    transaction,
                    model.SolicitudProduccionID!.Value,
                    model.MaterialID,
                    cancellationToken);

                if (contexto == null)
                {
                    ModelState.AddModelError(
                        nameof(model.MaterialID),
                        "El material no pertenece a la OF seleccionada.");
                    await transaction.RollbackAsync(cancellationToken);
                    await CargarMovimientoAsync(model, cancellationToken);
                    return View(model);
                }

                model.NumeroOF = contexto.NumeroOF;
                model.Unidad = "KG";
                model.CantidadPendienteOF = contexto.Pendiente;
                codigo = contexto.Codigo;

                if (string.IsNullOrWhiteSpace(contexto.NumeroOF))
                {
                    ModelState.AddModelError(
                        nameof(model.SolicitudProduccionID),
                        "Planeacion todavia no asigna un numero de OF valido.");
                    await transaction.RollbackAsync(cancellationToken);
                    await CargarMovimientoAsync(model, cancellationToken);
                    return View(model);
                }

                if (contexto.Pendiente <= 0.0005m)
                {
                    ModelState.AddModelError(
                        nameof(model.Cantidad),
                        "La entrega de este material ya esta completa.");
                    await transaction.RollbackAsync(cancellationToken);
                    await CargarMovimientoAsync(model, cancellationToken);
                    return View(model);
                }

                if (model.Cantidad - contexto.Pendiente > 0.0005m)
                {
                    ModelState.AddModelError(
                        nameof(model.Cantidad),
                        $"La cantidad excede lo pendiente. Maximo permitido: {contexto.Pendiente:0.###} KG.");
                    await transaction.RollbackAsync(cancellationToken);
                    await CargarMovimientoAsync(model, cancellationToken);
                    return View(model);
                }
            }
            else
            {
                const string materialSql = @"
SELECT Codigo, Nombre
FROM dbo.ERP_Materiales WITH (UPDLOCK, HOLDLOCK)
WHERE MaterialID = @Id
  AND Activo = 1;";

                await using var materialCommand =
                    new SqlCommand(materialSql, connection, transaction);
                materialCommand.Parameters.Add("@Id", SqlDbType.Int).Value = model.MaterialID;

                await using var reader =
                    await materialCommand.ExecuteReaderAsync(cancellationToken);

                if (!await reader.ReadAsync(cancellationToken))
                {
                    ModelState.AddModelError(
                        nameof(model.MaterialID),
                        "El material no existe o esta inactivo.");
                    await transaction.RollbackAsync(cancellationToken);
                    await CargarMovimientoAsync(model, cancellationToken);
                    return View(model);
                }

                codigo = Texto(reader, "Codigo");
            }

            if (EsSalidaMP(model.TipoMovimiento))
            {
                const string saldoSql = @"
SELECT ISNULL
(
    SUM
    (
        CASE
            WHEN TipoMovimiento IN
                 (N'Entrada', N'Retorno', N'Ajuste', N'AjustePositivo')
                THEN Cantidad
            WHEN TipoMovimiento IN
                 (N'Salida', N'Consumo', N'Scrap', N'AjusteNegativo')
                THEN -Cantidad
            ELSE 0
        END
    ),
    0
)
FROM dbo.AlmacenMP_Movimientos
WHERE Activo = 1
  AND MaterialID = @MaterialID
  AND CASE
          WHEN UPPER(LTRIM(RTRIM(ISNULL(TipoMP, N'')))) = N'MOLIDO'
              THEN N'MOLIDO'
          ELSE N'VIRGEN'
      END = @TipoMP;";

                await using var saldoCommand =
                    new SqlCommand(saldoSql, connection, transaction);
                saldoCommand.Parameters.Add("@MaterialID", SqlDbType.Int).Value = model.MaterialID;
                saldoCommand.Parameters.Add("@TipoMP", SqlDbType.NVarChar, 20).Value = model.TipoMP;

                var saldo = Convert.ToDecimal(
                    await saldoCommand.ExecuteScalarAsync(cancellationToken) ?? 0m);

                if (saldo < model.Cantidad)
                {
                    ModelState.AddModelError(
                        nameof(model.Cantidad),
                        $"Stock insuficiente para {codigo} ({model.TipoMP}). Disponible: {saldo:0.###} KG.");
                    await transaction.RollbackAsync(cancellationToken);
                    await CargarMovimientoAsync(model, cancellationToken);
                    return View(model);
                }
            }

            const string insertSql = @"
INSERT dbo.AlmacenMP_Movimientos
(
    FechaMovimiento, MaterialID, TipoMovimiento, TipoMP,
    Lote, Cantidad, Unidad, UbicacionID, NumeroOF,
    ResponsableUsuarioID, EntregadoPorNombre, Seguimiento,
    FechaCreacion, CreadoPor, Activo,
    RequiereValidacionProduccion, ValidadoProduccion,
    ReferenciaOperacion
)
VALUES
(
    SYSDATETIME(), @MaterialID, @Tipo, @TipoMP,
    N'S/L', @Cantidad, N'KG', @UbicacionID, @NumeroOF,
    @UsuarioID, @Responsable, @Observaciones,
    SYSUTCDATETIME(), @Responsable, 1,
    0, 1, @Referencia
);";

            await using var insert =
                new SqlCommand(insertSql, connection, transaction);

            insert.Parameters.Add("@MaterialID", SqlDbType.Int).Value = model.MaterialID;
            insert.Parameters.Add("@Tipo", SqlDbType.NVarChar, 30).Value = model.TipoMovimiento;
            insert.Parameters.Add("@TipoMP", SqlDbType.NVarChar, 20).Value = model.TipoMP;

            var cantidadParametro = insert.Parameters.Add("@Cantidad", SqlDbType.Decimal);
            cantidadParametro.Precision = 18;
            cantidadParametro.Scale = 3;
            cantidadParametro.Value = model.Cantidad;

            insert.Parameters.Add("@UbicacionID", SqlDbType.Int).Value =
                model.UbicacionID.HasValue
                    ? model.UbicacionID.Value
                    : DBNull.Value;

            insert.Parameters.Add("@NumeroOF", SqlDbType.NVarChar, 80).Value =
                string.IsNullOrWhiteSpace(model.NumeroOF)
                    ? DBNull.Value
                    : model.NumeroOF;

            insert.Parameters.Add("@UsuarioID", SqlDbType.Int).Value =
                UsuarioID.HasValue
                    ? UsuarioID.Value
                    : DBNull.Value;

            insert.Parameters.Add("@Responsable", SqlDbType.NVarChar, 180).Value = UsuarioNombre;
            insert.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 800).Value =
                string.IsNullOrWhiteSpace(model.Observaciones)
                    ? DBNull.Value
                    : model.Observaciones;
            insert.Parameters.Add("@Referencia", SqlDbType.NVarChar, 120).Value = referencia;

            await insert.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            await transaction.RollbackAsync(cancellationToken);
            Mensaje("warning", "La operacion ya fue procesada. No se creo un movimiento duplicado.");
            return model.EsEntregaOF
                ? RedirectToAction("Index", "AlmacenOF")
                : RedirectToAction(nameof(Index));
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        Mensaje(
            "success",
            model.EsEntregaOF
                ? $"Entrega de materia prima {model.TipoMP} registrada para la OF."
                : $"Movimiento de Almacen MP {model.TipoMP} registrado correctamente.");

        return model.EsEntregaOF
            ? RedirectToAction("Index", "AlmacenOF")
            : RedirectToAction(nameof(Index));
    }

    private async Task CargarMovimientoAsync(
        AlmacenMPMovimientoFormVm vm,
        CancellationToken cancellationToken)
    {
        await using var connection =
            await AbrirConexionAsync(cancellationToken);

        const string materialesSql = @"
SELECT MaterialID, Codigo, Nombre
FROM dbo.ERP_Materiales
WHERE Activo = 1
  AND UPPER(CONCAT(Codigo, N' ', Nombre)) NOT LIKE N'%BOLSA%'
  AND UPPER(CONCAT(Codigo, N' ', Nombre)) NOT LIKE N'%CAJA%'
  AND UPPER(CONCAT(Codigo, N' ', Nombre)) NOT LIKE N'%MIXTO%'
  AND UPPER(CONCAT(Codigo, N' ', Nombre)) NOT LIKE N'%BAG%'
  AND UPPER(CONCAT(Codigo, N' ', Nombre)) NOT LIKE N'%BOX%'
ORDER BY Codigo;";

        await using (var command =
            new SqlCommand(materialesSql, connection))
        await using (var reader =
            await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                vm.Materiales.Add(
                    new AlmacenSelectVm
                    {
                        Id = Entero(reader, "MaterialID"),
                        Texto = $"{Texto(reader, "Codigo")} · {Texto(reader, "Nombre")}",
                        Extra = "KG"
                    });
            }
        }

        var ordenes = new List<string>();
        if (await ExisteObjetoAsync(
                connection,
                "dbo.SolicitudesProduccion",
                "U",
                cancellationToken))
        {
            const string ordenesSql = @"
SELECT TOP (300) NumeroOF
FROM
(
    SELECT DISTINCT
        COALESCE
        (
            NULLIF(LTRIM(RTRIM(NumeroOFRecibida)), N''),
            NULLIF(LTRIM(RTRIM(FolioSolicitud)), N'')
        ) AS NumeroOF
    FROM dbo.SolicitudesProduccion
    WHERE Activo = 1
) origen
WHERE NumeroOF IS NOT NULL
ORDER BY NumeroOF DESC;";

            await using var ordenesCommand =
                new SqlCommand(ordenesSql, connection);
            await using var ordenesReader =
                await ordenesCommand.ExecuteReaderAsync(cancellationToken);

            while (await ordenesReader.ReadAsync(cancellationToken))
            {
                ordenes.Add(Texto(ordenesReader, "NumeroOF"));
            }
        }

        ViewData["OrdenesFabricacionMP"] = ordenes;

        if (!vm.EsEntregaOF)
        {
            vm.Ubicaciones =
                await CargarUbicacionesAsync(
                    connection,
                    "MP",
                    cancellationToken);
        }

        vm.TiposMovimiento =
            TiposPermitidos
                .Select((x, i) =>
                    new AlmacenSelectVm
                    {
                        Id = i + 1,
                        Texto = x,
                        Extra = x
                    })
                .ToList();

        vm.Unidad = "KG";
        vm.Lote = "S/L";
        vm.TipoMP = NormalizarTipoMP(vm.TipoMP);
    }

    private static string NormalizarTipoMP(string? tipoMP)
    {
        return string.Equals(
            tipoMP?.Trim(),
            "MOLIDO",
            StringComparison.OrdinalIgnoreCase)
                ? "MOLIDO"
                : "VIRGEN";
    }

    private static string? NormalizarTipoMPFiltro(string? tipoMP)
    {
        if (string.IsNullOrWhiteSpace(tipoMP))
            return null;

        var valor = tipoMP.Trim().ToUpperInvariant();
        return valor is "MOLIDO" or "VIRGEN"
            ? valor
            : null;
    }
    private static async Task<List<AlmacenSelectVm>> CargarUbicacionesAsync(SqlConnection connection, string almacen, CancellationToken cancellationToken)
    {
        var rows = new List<AlmacenSelectVm>();
        const string sql = @"
SELECT UbicacionID, Almacen, Rack, Nivel, Posicion
FROM dbo.ERP_Ubicaciones
WHERE Activo=1 AND (Almacen=@Almacen OR Almacen='GENERAL')
ORDER BY Almacen, Rack, Nivel, Posicion;";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Almacen", SqlDbType.NVarChar, 60).Value = almacen;
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
