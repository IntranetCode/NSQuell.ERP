using ERP.NSQuell.Models.ViewModels.Almacen;
using ERP.NSQuell.Servicios.Almacen;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Text;

namespace ERP.NSQuell.Controllers;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed partial class AlmacenMPController : AlmacenBaseController
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
                cancellationToken)
            || !await ExisteColumnaAsync(
                connection,
                "dbo.AlmacenMP_Movimientos",
                "MaterialSolicitadoID",
                cancellationToken))
        {
            vm.Configurado = false;
            vm.MensajeConfiguracion =
                "Falta ejecutar la instalacion completa de Almacen MP y el SQL de sustitucion v5.0.4.";

            return View(vm);
        }

        // NSQ_ALMACEN_MP_RESERVAS_REINTEGRADAS_V1
        await SincronizarReservasAlmacenAsync(connection, cancellationToken);

        // ALMACEN_RESERVAS_V5_0
        const string sql = @"
SELECT TOP (1000)
    MaterialID,
    Codigo,
    Nombre,
    Unidad,
    TipoMP,
    Entradas,
    Salidas,
    Disponible AS Saldo,
    Reservado AS Solicitado,
    StockMinimo,
    StockAviso,
    StockConfigurado,
    Semaforo,
    UltimoMovimiento
FROM dbo.vw_AlmacenMPInventario
WHERE
    (
        @Q IS NULL
        OR Codigo LIKE N'%' + @Q + N'%'
        OR Nombre LIKE N'%' + @Q + N'%'
    )
    AND (@TipoMP IS NULL OR TipoMP = @TipoMP)
ORDER BY Codigo, OrdenTipo, Nombre;";

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

        // ALMACEN_MP_BUSQUEDA_RECIENTES_V9_0
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
        CASE WHEN ubicacion.Nivel IS NULL THEN N'' ELSE N' / ' + ubicacion.Nivel END,
        CASE WHEN ubicacion.Posicion IS NULL THEN N'' ELSE N' / ' + ubicacion.Posicion END
    ) AS Ubicacion,
    ISNULL(movimiento.NumeroOF, N'') AS NumeroOF,
    COALESCE
    (
        movimiento.EntregadoPorNombre,
        persona.Nombre + N' ' + ISNULL(persona.ApellidoPaterno, N''),
        movimiento.CreadoPor,
        N''
    ) AS Responsable,
    ISNULL(movimiento.Seguimiento, N'') AS Observaciones,
    ISNULL(movimiento.ReferenciaOperacion, N'') AS ReferenciaOperacion
FROM dbo.AlmacenMP_Movimientos movimiento
INNER JOIN dbo.ERP_Materiales material
    ON material.MaterialID = movimiento.MaterialID
LEFT JOIN dbo.ERP_Ubicaciones ubicacion
    ON ubicacion.UbicacionID = movimiento.UbicacionID
LEFT JOIN dbo.Usuarios usuario
    ON usuario.UsuarioID = movimiento.ResponsableUsuarioID
LEFT JOIN dbo.Persona persona
    ON persona.PersonaID = usuario.PersonaID
WHERE movimiento.Activo = 1
  AND
  (
      @Q IS NULL
      OR material.Codigo LIKE N'%' + @Q + N'%'
      OR material.Nombre LIKE N'%' + @Q + N'%'
  )
ORDER BY movimiento.FechaMovimiento DESC, movimiento.MovimientoID DESC;";

        await using (var command = new SqlCommand(movimientosSql, connection))
        {
            command.Parameters.Add("@Q", SqlDbType.NVarChar, 250).Value =
                string.IsNullOrWhiteSpace(vm.Busqueda)
                    ? DBNull.Value
                    : vm.Busqueda;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                vm.Movimientos.Add(
                    new AlmacenMPMovimientoListaVm
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
                    });
            }
        }
        // ALMACEN_RESERVAS_V5_0: Reservado ya proviene de la vista de inventario.
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
          AND COALESCE
              (
                  movimiento.MaterialSolicitadoID,
                  movimiento.MaterialID
              ) = requerido.MaterialID
          AND
          (
              movimiento.SolicitudProduccionID = requerido.SolicitudProduccionID
              OR
              (
                  movimiento.SolicitudProduccionID IS NULL
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
                    Texto = $"{Texto(reader, "Codigo")} Â· {Texto(reader, "Nombre")}",
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
            Mensaje("warning", "Primero ejecuta el script de estructura de AlmacÃ©n.");
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
            ModelState.AddModelError(nameof(model.StockAviso), "El nivel de aviso debe ser igual o mayor al stock mÃ­nimo.");

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
                ModelState.AddModelError(nameof(model.Codigo), "El cÃ³digo ya existe en MP o en Embalajes.");
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
                ModelState.AddModelError($"Items[{i}].StockMinimo", "El stock mÃ­nimo no puede ser negativo.");
            if (item.StockAviso < item.StockMinimo)
                ModelState.AddModelError($"Items[{i}].StockAviso", "El stock de aviso debe ser igual o mayor al mÃ­nimo.");
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

        var materialSolicitadoID = materialId.GetValueOrDefault();

        var vm = new AlmacenMPMovimientoFormVm
        {
            MaterialID = materialSolicitadoID,
            TipoMovimiento = entregaOF
                ? "Salida"
                : TiposPermitidos.Contains(tipo ?? string.Empty)
                    ? tipo!
                    : "Entrada",
            TipoMP = NormalizarTipoMP(tipoMP),
            NumeroOF = entregaOF ? null : numeroOF?.Trim(),
            Cantidad = entregaOF ? 0m : cantidad.GetValueOrDefault(),
            CantidadVirgen = 0m,
            CantidadMolido = 0m,
            Unidad = "KG",
            EsEntregaOF = entregaOF,
            SolicitudProduccionID = solicitudProduccionId,
            FechaMovimiento = DateTime.Now,
            Lote = "S/L",
            OperacionToken = AlmacenOFEntregaService.CrearToken()
        };

        AlmacenOFEntregaContexto? contexto = null;

        if (entregaOF)
        {
            if (!solicitudProduccionId.HasValue
                || solicitudProduccionId.Value <= 0
                || materialSolicitadoID <= 0)
            {
                Mensaje("warning", "No se recibio una OF y un material validos.");
                return RedirectToAction("Index", "AlmacenOF");
            }

            await using var connection =
                await AbrirConexionAsync(cancellationToken);

            contexto = await AlmacenOFEntregaService.CargarMateriaPrimaAsync(
                connection,
                null,
                solicitudProduccionId.Value,
                materialSolicitadoID,
                cancellationToken);

            if (contexto == null)
            {
                Mensaje("warning", "El material solicitado no pertenece a la OF seleccionada.");
                return RedirectToAction("Index", "AlmacenOF");
            }

            if (string.IsNullOrWhiteSpace(contexto.NumeroOF))
            {
                Mensaje("warning", "Planeacion todavia no asigna un numero de OF valido.");
                return RedirectToAction("Index", "AlmacenOF");
            }

            if (contexto.Pendiente <= 0.0005m)
            {
                Mensaje("warning", "La materia prima solicitada ya fue entregada completamente.");
                return RedirectToAction("Index", "AlmacenOF");
            }

            vm.NumeroOF = contexto.NumeroOF;
            vm.Unidad = "KG";
            vm.CantidadPendienteOF = contexto.Pendiente;
            vm.Cantidad = 0m;
            vm.CantidadVirgen = 0m;
            vm.CantidadMolido = 0m;
            vm.Observaciones = $"Entrega de materia prima para {contexto.NumeroOF}.";
        }

        await CargarMovimientoAsync(vm, cancellationToken, materialSolicitadoID);
        PrepararVistaEntregaMP(vm, materialSolicitadoID, contexto?.Codigo);

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Movimiento(
        AlmacenMPMovimientoFormVm model,
        int? materialSolicitadoId,
        CancellationToken cancellationToken)
    {
        var sesion = ValidarSesion();
        if (sesion != null) return sesion;

        model.TipoMP = NormalizarTipoMP(model.TipoMP);

        // SCRAP_V15_MP_LOTE
        var loteCapturado = model.Lote?.Trim();
        var codigoScrapEscaneado = string.Empty;

        if (!model.EsEntregaOF
            && model.TipoMovimiento == "Entrada"
            && model.TipoMP == "M"
            && !string.IsNullOrWhiteSpace(loteCapturado)
            && !loteCapturado.Equals("S/L", StringComparison.OrdinalIgnoreCase))
        {
            if (AlmacenPTCodigoBarrasService.TryParse(
                    loteCapturado,
                    out var codigoScrap,
                    out _)
                && codigoScrap != null)
            {
                codigoScrapEscaneado = loteCapturado;
                model.Lote = codigoScrap.Lote.Trim();
            }
            else
            {
                model.Lote = loteCapturado;
            }
        }
        else
        {
            model.Lote = "S/L";
        }

        model.Unidad = "KG";
        model.NumeroOF = model.NumeroOF?.Trim();
        model.FolioCompra = model.FolioCompra?.Trim();
        model.Observaciones = model.Observaciones?.Trim();
        model.OperacionToken = model.OperacionToken?.Trim() ?? string.Empty;
        model.FechaMovimiento = DateTime.Now;

        var materialSolicitadoID = model.EsEntregaOF
            ? materialSolicitadoId.GetValueOrDefault()
            : model.MaterialID;

        ModelState.Remove(nameof(model.Unidad));
        ModelState.Remove(nameof(model.Lote));

        if (!AlmacenOFEntregaService.TokenValido(model.OperacionToken))
        {
            ModelState.AddModelError(
                nameof(model.OperacionToken),
                "La operacion expiro. Regresa al formulario e intentalo nuevamente.");
        }

        if (model.MaterialID <= 0)
        {
            ModelState.AddModelError(
                nameof(model.MaterialID),
                "Selecciona la materia prima del movimiento.");
        }

        if (model.EsEntregaOF)
        {
            model.TipoMovimiento = "Salida";
            model.Lote = "S/L";
            model.UbicacionID = null;
            model.Unidad = "KG";
            model.FolioCompra = null;

            ModelState.Remove(nameof(model.TipoMovimiento));
            ModelState.Remove(nameof(model.Lote));
            ModelState.Remove(nameof(model.UbicacionID));
            ModelState.Remove(nameof(model.NumeroOF));
            ModelState.Remove(nameof(model.Cantidad));
            ModelState.Remove(nameof(model.TipoMP));
            ModelState.Remove(nameof(model.CantidadPendienteOF));

            if (!model.SolicitudProduccionID.HasValue
                || model.SolicitudProduccionID.Value <= 0)
            {
                ModelState.AddModelError(
                    nameof(model.SolicitudProduccionID),
                    "La orden de fabricacion es obligatoria.");
            }

            if (materialSolicitadoID <= 0)
            {
                ModelState.AddModelError(
                    string.Empty,
                    "No se pudo identificar la materia prima solicitada originalmente.");
            }

            if (model.CantidadVirgen < 0m)
            {
                ModelState.AddModelError(
                    nameof(model.CantidadVirgen),
                    "La cantidad Virgen no puede ser negativa.");
            }

            if (model.CantidadMolido < 0m)
            {
                ModelState.AddModelError(
                    nameof(model.CantidadMolido),
                    "La cantidad Molido no puede ser negativa.");
            }

            model.Cantidad =
                Math.Max(0m, model.CantidadVirgen)
                + Math.Max(0m, model.CantidadMolido);

            if (model.Cantidad <= 0.0005m)
            {
                ModelState.AddModelError(
                    nameof(model.CantidadVirgen),
                    "Captura una cantidad mayor que 0.0000 en Virgen o en Molido. No pueden quedar ambos en 0.0000.");
            }

            if (string.IsNullOrWhiteSpace(model.Observaciones))
            {
                ModelState.AddModelError(
                    nameof(model.Observaciones),
                    "Las observaciones son obligatorias para la entrega a una OF.");
            }
        }
        else
        {
            if (model.TipoMP is not ("V" or "M"))
            {
                ModelState.AddModelError(
                    nameof(model.TipoMP),
                    "Selecciona Virgen o Molido.");
            }

            // ALMACEN_MP_ENTRADA_MOLIDO_LIBRE_V9_0
            var entradaMolidoLibre =
                model.TipoMovimiento == "Entrada"
                && model.TipoMP == "M";

            if (!entradaMolidoLibre
                && (!model.SolicitudProduccionID.HasValue
                    || model.SolicitudProduccionID.Value <= 0))
            {
                ModelState.AddModelError(
                    nameof(model.SolicitudProduccionID),
                    "Selecciona una orden de fabricacion. Solo la Entrada de Molido puede registrarse sin OF.");
            }

            if (model.TipoMovimiento == "Entrada" && model.TipoMP == "V")
            {
                if (string.IsNullOrWhiteSpace(model.FolioCompra))
                {
                    ModelState.AddModelError(
                        nameof(model.FolioCompra),
                        "El folio de compra es obligatorio para una entrada de MP Virgen.");
                }
            }
            else
            {
                model.FolioCompra = null;
                ModelState.Remove(nameof(model.FolioCompra));
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
            await CargarMovimientoAsync(model, cancellationToken, materialSolicitadoID);
            PrepararVistaEntregaMP(model, materialSolicitadoID);
            return View(model);
        }

        await using var connection =
            await AbrirConexionAsync(cancellationToken);

        if (!await ExisteColumnaAsync(
                connection,
                "dbo.AlmacenMP_Movimientos",
                "FolioCompra",
                cancellationToken)
            || !await ExisteColumnaAsync(
                connection,
                "dbo.AlmacenMP_Movimientos",
                "SolicitudProduccionID",
                cancellationToken))
        {
            ModelState.AddModelError(
                string.Empty,
                "Falta ejecutar 20_Almacen_MP_EMB_OF_FolioCompra_v1.0.sql.");
            await CargarMovimientoAsync(model, cancellationToken, materialSolicitadoID);
            PrepararVistaEntregaMP(model, materialSolicitadoID);
            return View(model);
        }

        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

        var referenciaBase =
            AlmacenOFEntregaService.CrearReferencia(
                "WEB-MP",
                model.OperacionToken);

        try
        {
            if (!model.EsEntregaOF
                && model.SolicitudProduccionID.HasValue
                && model.SolicitudProduccionID.Value > 0)
            {
                var numeroOFVinculado = await ResolverNumeroOFAsync(
                    connection,
                    transaction,
                    model.SolicitudProduccionID.Value,
                    cancellationToken);

                if (string.IsNullOrWhiteSpace(numeroOFVinculado))
                {
                    ModelState.AddModelError(
                        nameof(model.SolicitudProduccionID),
                        "La orden de fabricacion seleccionada no existe o ya no esta activa.");
                    await transaction.RollbackAsync(cancellationToken);
                    await CargarMovimientoAsync(model, cancellationToken, materialSolicitadoID);
                    PrepararVistaEntregaMP(model, materialSolicitadoID);
                    return View(model);
                }

                model.NumeroOF = numeroOFVinculado;
            }
            else if (!model.EsEntregaOF)
            {
                model.NumeroOF = null;
            }

            AlmacenOFEntregaContexto? contexto = null;
            var codigoSolicitado = string.Empty;

            if (model.EsEntregaOF)
            {
                contexto = await AlmacenOFEntregaService.CargarMateriaPrimaAsync(
                    connection,
                    transaction,
                    model.SolicitudProduccionID!.Value,
                    materialSolicitadoID,
                    cancellationToken);

                if (contexto == null)
                {
                    ModelState.AddModelError(
                        string.Empty,
                        "La materia prima solicitada originalmente no pertenece a la OF.");
                    await transaction.RollbackAsync(cancellationToken);
                    await CargarMovimientoAsync(model, cancellationToken, materialSolicitadoID);
                    PrepararVistaEntregaMP(model, materialSolicitadoID);
                    return View(model);
                }

                model.NumeroOF = contexto.NumeroOF;
                model.Unidad = "KG";
                model.CantidadPendienteOF = contexto.Pendiente;
                codigoSolicitado = contexto.Codigo;

                if (string.IsNullOrWhiteSpace(contexto.NumeroOF))
                {
                    ModelState.AddModelError(
                        nameof(model.SolicitudProduccionID),
                        "Planeacion todavia no asigna un numero de OF valido.");
                    await transaction.RollbackAsync(cancellationToken);
                    await CargarMovimientoAsync(model, cancellationToken, materialSolicitadoID);
                    PrepararVistaEntregaMP(model, materialSolicitadoID, codigoSolicitado);
                    return View(model);
                }

                if (contexto.Pendiente <= 0.0005m)
                {
                    ModelState.AddModelError(
                        nameof(model.CantidadVirgen),
                        "La entrega de la materia prima solicitada ya esta completa.");
                    await transaction.RollbackAsync(cancellationToken);
                    await CargarMovimientoAsync(model, cancellationToken, materialSolicitadoID);
                    PrepararVistaEntregaMP(model, materialSolicitadoID, codigoSolicitado);
                    return View(model);
                }

                if (model.Cantidad - contexto.Pendiente > 0.0005m)
                {
                    ModelState.AddModelError(
                        nameof(model.CantidadVirgen),
                        $"La suma Virgen + Molido excede lo pendiente. Maximo permitido: {contexto.Pendiente:0.###} KG.");
                    await transaction.RollbackAsync(cancellationToken);
                    await CargarMovimientoAsync(model, cancellationToken, materialSolicitadoID);
                    PrepararVistaEntregaMP(model, materialSolicitadoID, codigoSolicitado);
                    return View(model);
                }
            }

            const string materialSql = @"
SELECT Codigo, Nombre
FROM dbo.ERP_Materiales WITH (UPDLOCK, HOLDLOCK)
WHERE MaterialID = @Id
  AND Activo = 1;";

            string codigoEntregado;
            string nombreEntregado;

            await using (var materialCommand =
                new SqlCommand(materialSql, connection, transaction))
            {
                materialCommand.Parameters.Add("@Id", SqlDbType.Int).Value = model.MaterialID;

                await using var materialReader =
                    await materialCommand.ExecuteReaderAsync(cancellationToken);

                if (!await materialReader.ReadAsync(cancellationToken))
                {
                    ModelState.AddModelError(
                        nameof(model.MaterialID),
                        "La materia prima seleccionada no existe o esta inactiva.");
                    await transaction.RollbackAsync(cancellationToken);
                    await CargarMovimientoAsync(model, cancellationToken, materialSolicitadoID);
                    PrepararVistaEntregaMP(model, materialSolicitadoID, codigoSolicitado);
                    return View(model);
                }

                codigoEntregado = Texto(materialReader, "Codigo");
                nombreEntregado = Texto(materialReader, "Nombre");
            }

            var mismoMaterial = !model.EsEntregaOF
                || model.MaterialID == materialSolicitadoID;

            var movimientos = new List<(string TipoMP, decimal Cantidad, string Referencia)>();

            if (model.EsEntregaOF)
            {
                if (model.CantidadVirgen > 0.0005m)
                    movimientos.Add(("V", model.CantidadVirgen, referenciaBase + "-V"));

                if (model.CantidadMolido > 0.0005m)
                    movimientos.Add(("M", model.CantidadMolido, referenciaBase + "-M"));
            }
            else
            {
                movimientos.Add((model.TipoMP, model.Cantidad, referenciaBase));
            }

            foreach (var movimiento in movimientos)
            {
                if (await AlmacenOFEntregaService.ExisteReferenciaMPAsync(
                        connection,
                        transaction,
                        movimiento.Referencia,
                        cancellationToken))
                {
                    await transaction.RollbackAsync(cancellationToken);
                    Mensaje("warning", "Este movimiento ya habia sido registrado. No se creo un duplicado.");
                    return model.EsEntregaOF
                        ? RedirectToAction("Index", "AlmacenOF")
                        : RedirectToAction(nameof(Index));
                }
            }

            if (model.EsEntregaOF
                && mismoMaterial
                && movimientos.Count == 1)
            {
                const string asignarTipoSql = @"
IF OBJECT_ID(N'dbo.sp_AlmacenMP_AsignarTipoReserva', N'P') IS NOT NULL
BEGIN
    EXEC dbo.sp_AlmacenMP_AsignarTipoReserva
        @SolicitudProduccionID = @SolicitudProduccionID,
        @MaterialID = @MaterialID,
        @TipoMP = @TipoMP,
        @Usuario = @Usuario;
END;";

                await using var asignarTipo =
                    new SqlCommand(asignarTipoSql, connection, transaction);
                asignarTipo.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value =
                    model.SolicitudProduccionID!.Value;
                asignarTipo.Parameters.Add("@MaterialID", SqlDbType.Int).Value = materialSolicitadoID;
                asignarTipo.Parameters.Add("@TipoMP", SqlDbType.NChar, 1).Value = movimientos[0].TipoMP;
                asignarTipo.Parameters.Add("@Usuario", SqlDbType.NVarChar, 120).Value = UsuarioNombre;
                await asignarTipo.ExecuteNonQueryAsync(cancellationToken);
            }

            if (EsSalidaMP(model.TipoMovimiento))
            {
                const string saldoSql = @"
WITH ReservaPropia AS
(
    SELECT
        TipoMP,
        SUM(CantidadReservada) AS CantidadReservada
    FROM dbo.AlmacenMP_Reservas
    WHERE Activo = 1
      AND @UsarReservaPropia = 1
      AND SolicitudProduccionID = @SolicitudProduccionID
      AND MaterialID = @MaterialSolicitadoID
    GROUP BY TipoMP
)
SELECT
    inventario.TipoMP,
    inventario.Disponible + ISNULL(reserva.CantidadReservada, 0) AS SaldoOperacion
FROM dbo.vw_AlmacenMPInventario inventario
LEFT JOIN ReservaPropia reserva
    ON reserva.TipoMP = inventario.TipoMP
WHERE inventario.MaterialID = @MaterialID
  AND inventario.TipoMP IN (N'V', N'M');";

                var saldos = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
                {
                    ["V"] = 0m,
                    ["M"] = 0m
                };

                await using (var saldoCommand =
                    new SqlCommand(saldoSql, connection, transaction))
                {
                    saldoCommand.Parameters.Add("@MaterialID", SqlDbType.Int).Value = model.MaterialID;
                    saldoCommand.Parameters.Add("@UsarReservaPropia", SqlDbType.Bit).Value =
                        model.EsEntregaOF && mismoMaterial;
                    saldoCommand.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value =
                        model.SolicitudProduccionID.HasValue
                            ? model.SolicitudProduccionID.Value
                            : DBNull.Value;
                    saldoCommand.Parameters.Add("@MaterialSolicitadoID", SqlDbType.Int).Value =
                        materialSolicitadoID > 0
                            ? materialSolicitadoID
                            : DBNull.Value;

                    await using var saldoReader =
                        await saldoCommand.ExecuteReaderAsync(cancellationToken);

                    while (await saldoReader.ReadAsync(cancellationToken))
                    {
                        saldos[Texto(saldoReader, "TipoMP")] =
                            DecimalValor(saldoReader, "SaldoOperacion");
                    }
                }

                foreach (var movimiento in movimientos)
                {
                    var saldo = saldos.TryGetValue(movimiento.TipoMP, out var disponible)
                        ? disponible
                        : 0m;

                    if (saldo + 0.0005m < movimiento.Cantidad)
                    {
                        var campoCantidad = model.EsEntregaOF
                            ? movimiento.TipoMP == "V"
                                ? nameof(model.CantidadVirgen)
                                : nameof(model.CantidadMolido)
                            : nameof(model.Cantidad);

                        ModelState.AddModelError(
                            campoCantidad,
                            $"Stock insuficiente para {codigoEntregado} ({(movimiento.TipoMP == "V" ? "Virgen" : "Molido")}). Disponible para esta operacion: {saldo:0.###} KG.");
                        await transaction.RollbackAsync(cancellationToken);
                        await CargarMovimientoAsync(model, cancellationToken, materialSolicitadoID);
                        PrepararVistaEntregaMP(model, materialSolicitadoID, codigoSolicitado);
                        return View(model);
                    }
                }
            }

            const string insertSql = @"
INSERT dbo.AlmacenMP_Movimientos
(
    FechaMovimiento, MaterialID, MaterialSolicitadoID,
    TipoMovimiento, TipoMP,
    Lote, Cantidad, Unidad, UbicacionID, NumeroOF,
    FolioCompra,
    ResponsableUsuarioID, EntregadoPorNombre, Seguimiento,
    FechaCreacion, CreadoPor, Activo,
    RequiereValidacionProduccion, ValidadoProduccion,
    ReferenciaOperacion, SolicitudProduccionID
)
VALUES
(
    SYSDATETIME(), @MaterialID, @MaterialSolicitadoID,
    @Tipo, @TipoMP,
    @Lote, @Cantidad, N'KG', @UbicacionID, @NumeroOF,
    @FolioCompra,
    @UsuarioID, @Responsable, @Observaciones,
    SYSUTCDATETIME(), @Responsable, 1,
    0, 1, @Referencia, @SolicitudProduccionID
);";

            foreach (var movimiento in movimientos)
            {
                var observacionesGuardar = model.Observaciones ?? string.Empty;

                if (model.EsEntregaOF)
                {
                    var tipoTexto = movimiento.TipoMP == "V" ? "Virgen" : "Molido";
                    var encabezado = mismoMaterial
                        ? $"[ENTREGA MP] Solicitado y entregado: {codigoEntregado}. Tipo {tipoTexto}."
                        : $"[SUSTITUCION MP] Solicitado: {codigoSolicitado}. Entregado: {codigoEntregado} - {nombreEntregado}. Tipo {tipoTexto}.";

                    observacionesGuardar = $"{encabezado} {observacionesGuardar}".Trim();
                }
                else if (model.TipoMovimiento == "Entrada"
                         && movimiento.TipoMP == "V"
                         && !string.IsNullOrWhiteSpace(model.FolioCompra))
                {
                    observacionesGuardar =
                        $"[COMPRA MP VIRGEN] Folio {model.FolioCompra}. {observacionesGuardar}".Trim();
                }
                else if (model.TipoMovimiento == "Entrada"
                         && movimiento.TipoMP == "M"
                         && !string.IsNullOrWhiteSpace(codigoScrapEscaneado))
                {
                    observacionesGuardar =
                        $"[CODIGO SCRAP] {codigoScrapEscaneado}. Lote {model.Lote}. {observacionesGuardar}".Trim();
                }

                if (observacionesGuardar.Length > 800)
                    observacionesGuardar = observacionesGuardar[..800];

                await using var insert =
                    new SqlCommand(insertSql, connection, transaction);

                insert.Parameters.Add("@MaterialID", SqlDbType.Int).Value = model.MaterialID;
                insert.Parameters.Add("@MaterialSolicitadoID", SqlDbType.Int).Value =
                    model.EsEntregaOF ? materialSolicitadoID : DBNull.Value;
                insert.Parameters.Add("@Tipo", SqlDbType.NVarChar, 30).Value = model.TipoMovimiento;
                insert.Parameters.Add("@TipoMP", SqlDbType.NVarChar, 20).Value = movimiento.TipoMP;
                insert.Parameters.Add("@Lote", SqlDbType.NVarChar, 120).Value =
                    string.IsNullOrWhiteSpace(model.Lote) ? "S/L" : model.Lote;

                var cantidadParametro = insert.Parameters.Add("@Cantidad", SqlDbType.Decimal);
                cantidadParametro.Precision = 18;
                cantidadParametro.Scale = 3;
                cantidadParametro.Value = movimiento.Cantidad;

                insert.Parameters.Add("@UbicacionID", SqlDbType.Int).Value =
                    model.UbicacionID.HasValue
                        ? model.UbicacionID.Value
                        : DBNull.Value;

                insert.Parameters.Add("@NumeroOF", SqlDbType.NVarChar, 80).Value =
                    string.IsNullOrWhiteSpace(model.NumeroOF)
                        ? DBNull.Value
                        : model.NumeroOF;

                insert.Parameters.Add("@FolioCompra", SqlDbType.NVarChar, 120).Value =
                    !model.EsEntregaOF
                    && model.TipoMovimiento == "Entrada"
                    && movimiento.TipoMP == "V"
                    && !string.IsNullOrWhiteSpace(model.FolioCompra)
                        ? model.FolioCompra
                        : DBNull.Value;

                insert.Parameters.Add("@UsuarioID", SqlDbType.Int).Value =
                    UsuarioID.HasValue
                        ? UsuarioID.Value
                        : DBNull.Value;

                insert.Parameters.Add("@Responsable", SqlDbType.NVarChar, 180).Value = UsuarioNombre;
                insert.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 800).Value =
                    string.IsNullOrWhiteSpace(observacionesGuardar)
                        ? DBNull.Value
                        : observacionesGuardar;
                insert.Parameters.Add("@Referencia", SqlDbType.NVarChar, 120).Value = movimiento.Referencia;
                insert.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value =
                    model.SolicitudProduccionID.HasValue && model.SolicitudProduccionID.Value > 0
                        ? model.SolicitudProduccionID.Value
                        : DBNull.Value;

                await insert.ExecuteNonQueryAsync(cancellationToken);
            }

            if (model.EsEntregaOF)
            {
                const string sincronizarSql = @"
IF OBJECT_ID(N'dbo.sp_Almacen_SincronizarReservas', N'P') IS NOT NULL
BEGIN
    EXEC dbo.sp_Almacen_SincronizarReservas
        @Usuario = @Usuario;
END;";

                await using var sincronizar =
                    new SqlCommand(sincronizarSql, connection, transaction);
                sincronizar.Parameters.Add("@Usuario", SqlDbType.NVarChar, 120).Value = UsuarioNombre;
                await sincronizar.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);

            // SCRAP_V15_MP_LOTE: una Entrada Molido con lote puede cerrar
            // automaticamente el registro Scrap enlazado, sin borrar historial.
            if (!model.EsEntregaOF
                && model.TipoMovimiento == "Entrada"
                && model.TipoMP == "M"
                && !string.IsNullOrWhiteSpace(model.Lote)
                && !model.Lote.Equals("S/L", StringComparison.OrdinalIgnoreCase)
                && await ExisteObjetoAsync(
                    connection,
                    "dbo.usp_AlmacenScrap_SincronizarOrigenes",
                    "P",
                    cancellationToken))
            {
                await using var syncScrap =
                    new SqlCommand(
                        "dbo.usp_AlmacenScrap_SincronizarOrigenes",
                        connection)
                    {
                        CommandType = CommandType.StoredProcedure
                    };

                syncScrap.Parameters.Add("@UsuarioID", SqlDbType.Int).Value =
                    UsuarioID.HasValue ? UsuarioID.Value : DBNull.Value;
                syncScrap.Parameters.Add("@UsuarioNombre", SqlDbType.NVarChar, 180).Value =
                    UsuarioNombre;

                try
                {
                    await syncScrap.ExecuteNonQueryAsync(cancellationToken);
                }
                catch (SqlException)
                {
                    // El movimiento MP ya quedo confirmado. El modulo Scrap
                    // volvera a intentar la conciliacion al abrir Index/Recepciones.
                }
            }
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
                ? $"Entrega de materia prima registrada. Virgen: {model.CantidadVirgen:0.###} KG; Molido: {model.CantidadMolido:0.###} KG."
                : model.TipoMovimiento == "Entrada" && model.TipoMP == "V"
                    ? $"Entrada MP Virgen registrada con folio de compra {model.FolioCompra}."
                    : $"Movimiento de Almacen MP {(model.TipoMP == "V" ? "Virgen" : "Molido")} registrado correctamente.");

        return model.EsEntregaOF
            ? RedirectToAction("Index", "AlmacenOF")
            : RedirectToAction(nameof(Index));
    }

    private async Task CargarMovimientoAsync(
        AlmacenMPMovimientoFormVm vm,
        CancellationToken cancellationToken,
        int? materialSolicitadoID = null)
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
                        Texto = $"{Texto(reader, "Codigo")} Â· {Texto(reader, "Nombre")}",
                        Extra = "KG"
                    });
            }
        }

        const string stockSql = @"
WITH ReservaPropia AS
(
    SELECT
        MaterialID,
        TipoMP,
        SUM(CantidadReservada) AS CantidadReservada
    FROM dbo.AlmacenMP_Reservas
    WHERE Activo = 1
      AND @SolicitudProduccionID IS NOT NULL
      AND SolicitudProduccionID = @SolicitudProduccionID
      AND MaterialID = @MaterialSolicitadoID
    GROUP BY MaterialID, TipoMP
)
SELECT
    inventario.MaterialID,
    SUM
    (
        CASE
            WHEN inventario.TipoMP = N'V'
                THEN inventario.Disponible + ISNULL(reserva.CantidadReservada, 0)
            ELSE 0
        END
    ) AS StockV,
    SUM
    (
        CASE
            WHEN inventario.TipoMP = N'M'
                THEN inventario.Disponible + ISNULL(reserva.CantidadReservada, 0)
            ELSE 0
        END
    ) AS StockM
FROM dbo.vw_AlmacenMPInventario inventario
LEFT JOIN ReservaPropia reserva
    ON reserva.MaterialID = inventario.MaterialID
   AND reserva.TipoMP = inventario.TipoMP
GROUP BY inventario.MaterialID;";

        var stockPorMaterial = new Dictionary<int, AlmacenMPStockSelectorVm>();

        await using (var stockCommand = new SqlCommand(stockSql, connection))
        {
            stockCommand.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value =
                vm.EsEntregaOF && vm.SolicitudProduccionID.HasValue
                    ? vm.SolicitudProduccionID.Value
                    : DBNull.Value;
            stockCommand.Parameters.Add("@MaterialSolicitadoID", SqlDbType.Int).Value =
                vm.EsEntregaOF && materialSolicitadoID.GetValueOrDefault() > 0
                    ? materialSolicitadoID.Value
                    : DBNull.Value;

        await using (var stockReader =
            await stockCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await stockReader.ReadAsync(cancellationToken))
            {
                stockPorMaterial[Entero(stockReader, "MaterialID")] =
                    new AlmacenMPStockSelectorVm
                    {
                        StockV = Math.Max(0m, DecimalValor(stockReader, "StockV")),
                        StockM = Math.Max(0m, DecimalValor(stockReader, "StockM"))
                    };
            }
        }
        }

        ViewData["StockPorMaterialMP"] = stockPorMaterial;

        ViewData["OrdenesFabricacion"] =
            await CargarOrdenesFabricacionAsync(
                connection,
                cancellationToken);

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
        if (string.IsNullOrWhiteSpace(vm.Lote))
            vm.Lote = "S/L";
        vm.TipoMP = NormalizarTipoMP(vm.TipoMP);
    }

    private void PrepararVistaEntregaMP(
        AlmacenMPMovimientoFormVm vm,
        int materialSolicitadoID,
        string? codigoSolicitado = null)
    {
        ViewData["MaterialSolicitadoID"] = materialSolicitadoID;

        var texto = vm.Materiales
            .FirstOrDefault(x => x.Id == materialSolicitadoID)
            ?.Texto;

        ViewData["MaterialSolicitadoTexto"] =
            !string.IsNullOrWhiteSpace(texto)
                ? texto
                : string.IsNullOrWhiteSpace(codigoSolicitado)
                    ? "Material solicitado no encontrado"
                    : codigoSolicitado;
    }

    private static string NormalizarTipoMP(string? tipoMP)
    {
        var valor = tipoMP?.Trim().ToUpperInvariant();
        return valor is "M" or "MOLIDO" ? "M" : "V";
    }

    private static string? NormalizarTipoMPFiltro(string? tipoMP)
    {
        if (string.IsNullOrWhiteSpace(tipoMP))
            return null;

        var valor = tipoMP.Trim().ToUpperInvariant();
        return valor switch
        {
            "V" or "VIRGEN" => "V",
            "M" or "MOLIDO" => "M",
            _ => null
        };
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
                Texto = string.Join(" Â· ", new[] { Texto(reader, "Almacen"), Texto(reader, "Rack"), Texto(reader, "Nivel"), Texto(reader, "Posicion") }.Where(x => !string.IsNullOrWhiteSpace(x)))
            });
        }
        return rows;
    }
}
