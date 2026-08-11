using ERP.NSQuell.Models.ViewModels.Almacen;
using ERP.NSQuell.Servicios.Almacen;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;

namespace ERP.NSQuell.Controllers;

public sealed class AlmacenScrapController : AlmacenBaseController
{
    private static readonly string[] EstatusPermitidos =
    {
        "PENDIENTE_RECEPCION",
        "RECIBIDO",
        "MOLIDO"
    };

    private static readonly string[] OrigenesPermitidos =
    {
        "ENTRADA_SCRAP",
        "CALIDAD",
        "GP12"
    };

    public AlmacenScrapController(IConfiguration configuration)
        : base(configuration)
    {
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        string? q = null,
        string? origen = null,
        string? estatus = null,
        CancellationToken cancellationToken = default)
    {
        if (ValidarSesion() is IActionResult sesion)
            return sesion;

        var vm = new AlmacenScrapIndexVm
        {
            Busqueda = q?.Trim(),
            Origen = NormalizarFiltro(origen, OrigenesPermitidos),
            Estatus = NormalizarFiltro(estatus, EstatusPermitidos)
        };

        await using var connection = await AbrirConexionAsync(cancellationToken);

        if (!await ModuloConfiguradoAsync(connection, cancellationToken))
        {
            vm.Configurado = false;
            vm.MensajeConfiguracion =
                "Falta ejecutar Scripts/SQL/Almacen/14_Modulo_Almacen_Scrap.sql en ERP_QUELL TEST.";
            return View(vm);
        }

        const string resumenSql = @"
SELECT
    COUNT_BIG(*) AS TotalRegistros,
    SUM(CASE WHEN Estatus = N'PENDIENTE_RECEPCION' THEN 1 ELSE 0 END) AS PendientesRecepcion,
    SUM(CASE WHEN Estatus = N'RECIBIDO' THEN 1 ELSE 0 END) AS Recibidos,
    SUM(CASE WHEN Estatus = N'MOLIDO' THEN 1 ELSE 0 END) AS Molidos,
    SUM(CASE WHEN Estatus = N'MOLIDO' THEN ISNULL(PesoMolidoKg, 0) ELSE 0 END) AS KgMolidos
FROM dbo.AlmacenScrap_Registros
WHERE Activo = 1;";

        await using (var resumen = new SqlCommand(resumenSql, connection))
        await using (var reader = await resumen.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                vm.TotalRegistros = Convert.ToInt32(EnteroLargo(reader, "TotalRegistros"));
                vm.PendientesRecepcion = Entero(reader, "PendientesRecepcion");
                vm.Recibidos = Entero(reader, "Recibidos");
                vm.Molidos = Entero(reader, "Molidos");
                vm.KgMolidos = DecimalValor(reader, "KgMolidos");
            }
        }

        const string sql = @"
SELECT TOP (500)
    ScrapRegistroID,
    Origen,
    ISNULL(OrigenReferencia, N'') AS OrigenReferencia,
    CodigoBarras,
    NumeroOF,
    SolicitudProduccionID,
    ParteID,
    NumeroParte,
    Designacion,
    CantidadPiezas,
    Lote,
    Estatus,
    FechaCreacion,
    FechaRecepcion,
    ISNULL(RecibidoPorNombre, N'') AS RecibidoPorNombre,
    MaterialIDMolido,
    ISNULL(MaterialMolido, N'') AS MaterialMolido,
    PesoMolidoKg,
    MPMovimientoID,
    FechaMolido,
    ISNULL(Observaciones, N'') AS Observaciones
FROM dbo.vw_AlmacenScrap_Registros
WHERE Activo = 1
  AND (@Origen IS NULL OR Origen = @Origen)
  AND (@Estatus IS NULL OR Estatus = @Estatus)
  AND
  (
      @Busqueda IS NULL
      OR CodigoBarras LIKE N'%' + @Busqueda + N'%'
      OR NumeroOF LIKE N'%' + @Busqueda + N'%'
      OR NumeroParte LIKE N'%' + @Busqueda + N'%'
      OR Designacion LIKE N'%' + @Busqueda + N'%'
      OR Lote LIKE N'%' + @Busqueda + N'%'
      OR OrigenReferencia LIKE N'%' + @Busqueda + N'%'
  )
ORDER BY
    CASE Estatus
        WHEN N'PENDIENTE_RECEPCION' THEN 1
        WHEN N'RECIBIDO' THEN 2
        WHEN N'MOLIDO' THEN 3
        ELSE 4
    END,
    ScrapRegistroID DESC;";

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Busqueda", SqlDbType.NVarChar, 250).Value =
            string.IsNullOrWhiteSpace(vm.Busqueda) ? DBNull.Value : vm.Busqueda;
        command.Parameters.Add("@Origen", SqlDbType.NVarChar, 30).Value =
            string.IsNullOrWhiteSpace(vm.Origen) ? DBNull.Value : vm.Origen;
        command.Parameters.Add("@Estatus", SqlDbType.NVarChar, 30).Value =
            string.IsNullOrWhiteSpace(vm.Estatus) ? DBNull.Value : vm.Estatus;

        await using var rows = await command.ExecuteReaderAsync(cancellationToken);

        while (await rows.ReadAsync(cancellationToken))
            vm.Registros.Add(MapearRegistro(rows));

        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> Recepciones(
        string? q = null,
        string? origen = null,
        CancellationToken cancellationToken = default)
    {
        if (ValidarSesion() is IActionResult sesion)
            return sesion;

        var vm = new AlmacenScrapRecepcionesVm
        {
            Busqueda = q?.Trim(),
            Origen = NormalizarFiltro(origen, new[] { "CALIDAD", "GP12" })
        };

        await using var connection = await AbrirConexionAsync(cancellationToken);

        if (!await ModuloConfiguradoAsync(connection, cancellationToken))
        {
            vm.Configurado = false;
            vm.MensajeConfiguracion =
                "El módulo Scrap no está instalado en la base.";
            return View(vm);
        }

        const string resumenSql = @"
SELECT
    COUNT(*) AS TotalPendientes,
    SUM(CASE WHEN Origen = N'CALIDAD' THEN 1 ELSE 0 END) AS PendientesCalidad,
    SUM(CASE WHEN Origen = N'GP12' THEN 1 ELSE 0 END) AS PendientesGP12
FROM dbo.AlmacenScrap_Registros
WHERE Activo = 1
  AND Estatus = N'PENDIENTE_RECEPCION'
  AND Origen IN (N'CALIDAD', N'GP12');";

        await using (var resumen = new SqlCommand(resumenSql, connection))
        await using (var reader = await resumen.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                vm.TotalPendientes = Entero(reader, "TotalPendientes");
                vm.PendientesCalidad = Entero(reader, "PendientesCalidad");
                vm.PendientesGP12 = Entero(reader, "PendientesGP12");
            }
        }

        const string sql = @"
SELECT TOP (500)
    ScrapRegistroID,
    Origen,
    ISNULL(OrigenReferencia, N'') AS OrigenReferencia,
    CodigoBarras,
    NumeroOF,
    SolicitudProduccionID,
    ParteID,
    NumeroParte,
    Designacion,
    CantidadPiezas,
    Lote,
    Estatus,
    FechaCreacion,
    FechaRecepcion,
    ISNULL(RecibidoPorNombre, N'') AS RecibidoPorNombre,
    MaterialIDMolido,
    ISNULL(MaterialMolido, N'') AS MaterialMolido,
    PesoMolidoKg,
    MPMovimientoID,
    FechaMolido,
    ISNULL(Observaciones, N'') AS Observaciones
FROM dbo.vw_AlmacenScrap_Registros
WHERE Activo = 1
  AND Estatus = N'PENDIENTE_RECEPCION'
  AND Origen IN (N'CALIDAD', N'GP12')
  AND (@Origen IS NULL OR Origen = @Origen)
  AND
  (
      @Busqueda IS NULL
      OR CodigoBarras LIKE N'%' + @Busqueda + N'%'
      OR NumeroOF LIKE N'%' + @Busqueda + N'%'
      OR NumeroParte LIKE N'%' + @Busqueda + N'%'
      OR Designacion LIKE N'%' + @Busqueda + N'%'
      OR Lote LIKE N'%' + @Busqueda + N'%'
      OR OrigenReferencia LIKE N'%' + @Busqueda + N'%'
  )
ORDER BY ScrapRegistroID DESC;";

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Busqueda", SqlDbType.NVarChar, 250).Value =
            string.IsNullOrWhiteSpace(vm.Busqueda) ? DBNull.Value : vm.Busqueda;
        command.Parameters.Add("@Origen", SqlDbType.NVarChar, 30).Value =
            string.IsNullOrWhiteSpace(vm.Origen) ? DBNull.Value : vm.Origen;

        await using var rows = await command.ExecuteReaderAsync(cancellationToken);
        while (await rows.ReadAsync(cancellationToken))
            vm.Registros.Add(MapearRegistro(rows));

        return View(vm);
    }

    [HttpGet]
    public IActionResult PorMoler(
        string? q = null,
        string? origen = null)
    {
        // SCRAP_V14_COMPAT_POR_MOLER
        // La cola independiente fue retirada. Cualquier URL anterior
        // redirige al listado principal filtrado en Scrap recibido.
        return RedirectToAction(
            nameof(Index),
            new
            {
                q,
                origen,
                estatus = "RECIBIDO"
            });
    }

    [HttpGet]
    public async Task<IActionResult> Historial(
        string? q = null,
        string? origen = null,
        string? evento = null,
        CancellationToken cancellationToken = default)
    {
        if (ValidarSesion() is IActionResult sesion)
            return sesion;

        var eventosPermitidos = new[]
        {
            "RECEPCION_ESCANER",
            "ENVIADO_A_ALMACEN",
            "RECEPCION_CONFIRMADA",
            "MP_MOLIDO_GENERADO"
        };

        var vm = new AlmacenScrapHistorialIndexVm
        {
            Busqueda = q?.Trim(),
            Origen = NormalizarFiltro(origen, OrigenesPermitidos),
            Evento = NormalizarFiltro(evento, eventosPermitidos)
        };

        await using var connection = await AbrirConexionAsync(cancellationToken);

        if (!await ModuloConfiguradoAsync(connection, cancellationToken))
        {
            vm.Configurado = false;
            vm.MensajeConfiguracion =
                "El módulo Scrap no está instalado en la base.";
            return View(vm);
        }

        const string sql = @"
SELECT TOP (1000)
    h.ScrapHistorialID,
    h.ScrapRegistroID,
    h.FechaEvento,
    h.Evento,
    ISNULL(h.EstatusAnterior, N'') AS EstatusAnterior,
    ISNULL(h.EstatusNuevo, N'') AS EstatusNuevo,
    ISNULL(h.Detalle, N'') AS Detalle,
    ISNULL(h.UsuarioNombre, N'') AS UsuarioNombre,
    s.Origen,
    s.NumeroOF,
    s.NumeroParte,
    s.Lote,
    s.CodigoBarras
FROM dbo.AlmacenScrap_Historial h
INNER JOIN dbo.AlmacenScrap_Registros s
    ON s.ScrapRegistroID = h.ScrapRegistroID
WHERE s.Activo = 1
  AND (@Origen IS NULL OR s.Origen = @Origen)
  AND (@Evento IS NULL OR h.Evento = @Evento)
  AND
  (
      @Busqueda IS NULL
      OR CONVERT(NVARCHAR(30), h.ScrapRegistroID) LIKE N'%' + @Busqueda + N'%'
      OR s.CodigoBarras LIKE N'%' + @Busqueda + N'%'
      OR s.NumeroOF LIKE N'%' + @Busqueda + N'%'
      OR s.NumeroParte LIKE N'%' + @Busqueda + N'%'
      OR s.Lote LIKE N'%' + @Busqueda + N'%'
      OR h.Detalle LIKE N'%' + @Busqueda + N'%'
      OR h.UsuarioNombre LIKE N'%' + @Busqueda + N'%'
  )
ORDER BY h.ScrapHistorialID DESC;";

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Busqueda", SqlDbType.NVarChar, 250).Value =
            string.IsNullOrWhiteSpace(vm.Busqueda) ? DBNull.Value : vm.Busqueda;
        command.Parameters.Add("@Origen", SqlDbType.NVarChar, 30).Value =
            string.IsNullOrWhiteSpace(vm.Origen) ? DBNull.Value : vm.Origen;
        command.Parameters.Add("@Evento", SqlDbType.NVarChar, 60).Value =
            string.IsNullOrWhiteSpace(vm.Evento) ? DBNull.Value : vm.Evento;

        await using var rows = await command.ExecuteReaderAsync(cancellationToken);

        while (await rows.ReadAsync(cancellationToken))
        {
            vm.Eventos.Add(
                new AlmacenScrapHistorialListaVm
                {
                    ScrapHistorialID = EnteroLargo(rows, "ScrapHistorialID"),
                    ScrapRegistroID = EnteroLargo(rows, "ScrapRegistroID"),
                    FechaEvento = Fecha(rows, "FechaEvento") ?? DateTime.MinValue,
                    Evento = Texto(rows, "Evento"),
                    EstatusAnterior = Texto(rows, "EstatusAnterior"),
                    EstatusNuevo = Texto(rows, "EstatusNuevo"),
                    Detalle = Texto(rows, "Detalle"),
                    UsuarioNombre = Texto(rows, "UsuarioNombre"),
                    Origen = Texto(rows, "Origen"),
                    NumeroOF = Texto(rows, "NumeroOF"),
                    NumeroParte = Texto(rows, "NumeroParte"),
                    Lote = Texto(rows, "Lote"),
                    CodigoBarras = Texto(rows, "CodigoBarras")
                });
        }

        vm.TotalEventos = vm.Eventos.Count;
        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> Entrada(
        CancellationToken cancellationToken = default)
    {
        if (ValidarSesion() is IActionResult sesion)
            return sesion;

        await using var connection = await AbrirConexionAsync(cancellationToken);

        if (!await ModuloConfiguradoAsync(connection, cancellationToken))
        {
            Mensaje(
                "warning",
                "Primero ejecuta el SQL del módulo Scrap en ERP_QUELL TEST.");
            return RedirectToAction(nameof(Index));
        }

        return View(new AlmacenScrapEntradaVm());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Entrada(
        AlmacenScrapEntradaVm model,
        string? accion,
        CancellationToken cancellationToken = default)
    {
        if (ValidarSesion() is IActionResult sesion)
            return sesion;

        accion = accion?.Trim().ToLowerInvariant();

        var codigos = SepararCodigos(model.CodigosEscaneados);

        if (codigos.Count == 0)
        {
            ModelState.AddModelError(
                nameof(model.CodigosEscaneados),
                "Escanea al menos un código de Scrap.");
            return View(model);
        }

        if (codigos.Count > 200)
        {
            ModelState.AddModelError(
                nameof(model.CodigosEscaneados),
                "Máximo 200 códigos por operación.");
            return View(model);
        }

        await using var connection = await AbrirConexionAsync(cancellationToken);

        if (!await ModuloConfiguradoAsync(connection, cancellationToken))
        {
            ModelState.AddModelError(
                string.Empty,
                "Falta ejecutar el SQL del módulo Scrap.");
            return View(model);
        }

        var repetidosLote = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var previews = new List<AlmacenScrapCodigoPreviewVm>();

        foreach (var codigo in codigos)
        {
            var preview = new AlmacenScrapCodigoPreviewVm
            {
                CodigoOriginal = codigo
            };

            if (!repetidosLote.Add(codigo))
            {
                preview.Error = "Código repetido dentro de esta captura.";
                previews.Add(preview);
                continue;
            }

            if (!AlmacenPTCodigoBarrasService.TryParse(
                    codigo,
                    out var parseado,
                    out var error)
                || parseado is null)
            {
                preview.Error = error;
                previews.Add(preview);
                continue;
            }

            preview.NumeroOF = NormalizarNumeroOF(parseado.NumeroOF);
            preview.NumeroParte = parseado.NumeroParte.Trim();
            preview.Designacion = parseado.Designacion.Trim();
            preview.CantidadPiezas = parseado.Cantidad;
            preview.Lote = parseado.Lote.Trim();

            var scrapExistente = await BuscarScrapExistenteAsync(
                connection,
                preview.CodigoOriginal,
                cancellationToken);

            if (scrapExistente.HasValue)
            {
                preview.Error =
                    $"El código ya está registrado como Scrap #{scrapExistente.Value}.";
                previews.Add(preview);
                continue;
            }

            var catalogo = await ResolverCatalogoAsync(
                connection,
                preview.NumeroParte,
                cancellationToken);

            preview.ParteID = catalogo.ParteID;
            preview.ParteDescripcion = catalogo.ParteDescripcion;
            preview.MaterialID = catalogo.MaterialID;
            preview.MaterialCodigo = catalogo.MaterialCodigo;
            preview.MaterialNombre = catalogo.MaterialNombre;
            preview.PuedeRegistrar = true;

            if (!catalogo.ParteID.HasValue)
            {
                preview.Advertencia =
                    catalogo.ParteError.Length > 0
                        ? catalogo.ParteError
                        : "La parte no pudo vincularse al catálogo. El Scrap se puede recibir, pero no podrá convertirse a MP hasta resolver la parte.";
            }
            else if (!catalogo.MaterialID.HasValue)
            {
                preview.Advertencia =
                    catalogo.MaterialError.Length > 0
                        ? catalogo.MaterialError
                        : "La parte no tiene un material MP único configurado. El registro se conservará, pero MP Molido quedará bloqueado.";
            }

            previews.Add(preview);
        }

        model.Codigos = previews;

        if (accion == "convertir" || string.IsNullOrWhiteSpace(accion))
            return View(model);

        if (accion != "registrar")
        {
            ModelState.AddModelError(string.Empty, "Acción no reconocida.");
            return View(model);
        }

        var invalidos = previews.Where(x => !x.PuedeRegistrar).ToList();

        if (invalidos.Count > 0)
        {
            ModelState.AddModelError(
                string.Empty,
                $"Hay {invalidos.Count} código(s) inválido(s) o duplicado(s). Corrígelos antes de registrar.");
            return View(model);
        }

        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            foreach (var item in previews)
            {
                await RegistrarEntradaEscanerAsync(
                    connection,
                    transaction,
                    item,
                    model.Observaciones,
                    cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);

            Mensaje(
                "success",
                $"Recepción de Scrap registrada: {previews.Count} registro(s). No se afectó inventario.");
            return RedirectToAction(nameof(Index), new { estatus = "RECIBIDO" });
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmarRecepcion(
        long id,
        string? returnTo = null,
        CancellationToken cancellationToken = default)
    {
        if (ValidarSesion() is IActionResult sesion)
            return sesion;

        if (id <= 0)
            return NotFound();

        await using var connection = await AbrirConexionAsync(cancellationToken);

        if (!await ModuloConfiguradoAsync(connection, cancellationToken))
        {
            Mensaje("warning", "El módulo Scrap no está instalado en la base.");
            return RedirectToAction(nameof(Index));
        }

        await using var command =
            new SqlCommand("dbo.usp_AlmacenScrap_ConfirmarRecepcion", connection)
            {
                CommandType = CommandType.StoredProcedure
            };

        command.Parameters.Add("@ScrapRegistroID", SqlDbType.BigInt).Value = id;
        command.Parameters.Add("@UsuarioID", SqlDbType.Int).Value =
            UsuarioID.HasValue ? UsuarioID.Value : DBNull.Value;
        command.Parameters.Add("@UsuarioNombre", SqlDbType.NVarChar, 180).Value =
            UsuarioNombre;

        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
            Mensaje("success", $"Scrap #{id} confirmado como recibido.");
        }
        catch (SqlException ex)
        {
            Mensaje("danger", ex.Message);
        }

        var destino =
            string.Equals(returnTo, "recepciones", StringComparison.OrdinalIgnoreCase)
                ? nameof(Recepciones)
                : nameof(Index);

        return RedirectToAction(destino);
    }

    [HttpGet]
    public async Task<IActionResult> Molido(
        long id,
        CancellationToken cancellationToken = default)
    {
        if (ValidarSesion() is IActionResult sesion)
            return sesion;

        if (id <= 0)
            return NotFound();

        await using var connection = await AbrirConexionAsync(cancellationToken);

        if (!await ModuloConfiguradoAsync(connection, cancellationToken))
        {
            Mensaje("warning", "El módulo Scrap no está instalado en la base.");
            return RedirectToAction(nameof(Index));
        }

        var registro = await CargarRegistroAsync(connection, id, cancellationToken);

        if (registro is null)
            return NotFound();

        if (!registro.Estatus.Equals("RECIBIDO", StringComparison.OrdinalIgnoreCase)
            || registro.MPMovimientoID.HasValue)
        {
            Mensaje(
                "warning",
                "Solo un registro de Scrap recibido y sin conversión previa puede generar MP Molido.");
            return RedirectToAction(nameof(Detalle), new { id });
        }

        var vm = new AlmacenScrapMolidoVm();
        await PrepararMolidoAsync(
            vm,
            registro,
            connection,
            cancellationToken);

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Molido(
        AlmacenScrapMolidoVm model,
        CancellationToken cancellationToken = default)
    {
        if (ValidarSesion() is IActionResult sesion)
            return sesion;

        await using var connection = await AbrirConexionAsync(cancellationToken);

        if (!await ModuloConfiguradoAsync(connection, cancellationToken))
        {
            ModelState.AddModelError(
                string.Empty,
                "El módulo Scrap no está instalado en la base.");
            return View(model);
        }

        var registro = await CargarRegistroAsync(
            connection,
            model.ScrapRegistroID,
            cancellationToken);

        if (registro is null)
            return NotFound();

        await PrepararMolidoAsync(
            model,
            registro,
            connection,
            cancellationToken);

        if (!registro.Estatus.Equals("RECIBIDO", StringComparison.OrdinalIgnoreCase)
            || registro.MPMovimientoID.HasValue)
        {
            ModelState.AddModelError(
                string.Empty,
                "Este Scrap ya no está disponible para generar MP Molido.");
        }

        if (!model.MaterialID.HasValue)
        {
            ModelState.AddModelError(
                string.Empty,
                string.IsNullOrWhiteSpace(model.MaterialError)
                    ? "No fue posible resolver un material MP único."
                    : model.MaterialError);
        }

        if (!model.UbicacionID.HasValue
            || !await EsUbicacionMPValidaAsync(
                connection,
                model.UbicacionID.Value,
                cancellationToken))
        {
            ModelState.AddModelError(
                nameof(model.UbicacionID),
                "Selecciona una ubicación activa del almacén MP.");
        }

        if (!ModelState.IsValid || !model.PuedeGuardar)
            return View(model);

        await using var command =
            new SqlCommand("dbo.usp_AlmacenScrap_RealizarMPMolido", connection)
            {
                CommandType = CommandType.StoredProcedure
            };

        command.Parameters.Add("@ScrapRegistroID", SqlDbType.BigInt).Value =
            model.ScrapRegistroID;
        command.Parameters.Add("@MaterialID", SqlDbType.Int).Value =
            model.MaterialID!.Value;

        var peso = command.Parameters.Add("@PesoMolidoKg", SqlDbType.Decimal);
        peso.Precision = 18;
        peso.Scale = 4;
        peso.Value = model.PesoMolidoKg;

        command.Parameters.Add("@UbicacionID", SqlDbType.Int).Value =
            model.UbicacionID!.Value;
        command.Parameters.Add("@UsuarioID", SqlDbType.Int).Value =
            UsuarioID.HasValue ? UsuarioID.Value : DBNull.Value;
        command.Parameters.Add("@UsuarioNombre", SqlDbType.NVarChar, 180).Value =
            UsuarioNombre;
        command.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 800).Value =
            string.IsNullOrWhiteSpace(model.ObservacionesMolido)
                ? DBNull.Value
                : model.ObservacionesMolido.Trim();

        try
        {
            var movimientoId =
                Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));

            Mensaje(
                "success",
                $"MP Molido generado correctamente. Movimiento MP #{movimientoId}.");
            return RedirectToAction(nameof(Detalle), new { id = model.ScrapRegistroID });
        }
        catch (SqlException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(model);
        }
    }

    [HttpGet]
    public async Task<IActionResult> Detalle(
        long id,
        CancellationToken cancellationToken = default)
    {
        if (ValidarSesion() is IActionResult sesion)
            return sesion;

        if (id <= 0)
            return NotFound();

        await using var connection = await AbrirConexionAsync(cancellationToken);

        if (!await ModuloConfiguradoAsync(connection, cancellationToken))
            return NotFound();

        var registro = await CargarRegistroAsync(connection, id, cancellationToken);

        if (registro is null)
            return NotFound();

        var vm = new AlmacenScrapDetalleVm
        {
            Registro = registro
        };

        const string sql = @"
SELECT
    ScrapHistorialID,
    FechaEvento,
    Evento,
    ISNULL(EstatusAnterior, N'') AS EstatusAnterior,
    ISNULL(EstatusNuevo, N'') AS EstatusNuevo,
    ISNULL(Detalle, N'') AS Detalle,
    ISNULL(UsuarioNombre, N'') AS UsuarioNombre
FROM dbo.AlmacenScrap_Historial
WHERE ScrapRegistroID = @ScrapRegistroID
ORDER BY ScrapHistorialID DESC;";

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@ScrapRegistroID", SqlDbType.BigInt).Value = id;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            vm.Historial.Add(
                new AlmacenScrapHistorialVm
                {
                    ScrapHistorialID = EnteroLargo(reader, "ScrapHistorialID"),
                    FechaEvento = Fecha(reader, "FechaEvento") ?? DateTime.MinValue,
                    Evento = Texto(reader, "Evento"),
                    EstatusAnterior = Texto(reader, "EstatusAnterior"),
                    EstatusNuevo = Texto(reader, "EstatusNuevo"),
                    Detalle = Texto(reader, "Detalle"),
                    UsuarioNombre = Texto(reader, "UsuarioNombre")
                });
        }

        return View(vm);
    }

    private static async Task<bool> ModuloConfiguradoAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        return
            await ExisteObjetoAsync(
                connection,
                "dbo.AlmacenScrap_Registros",
                "U",
                cancellationToken)
            && await ExisteObjetoAsync(
                connection,
                "dbo.AlmacenScrap_Historial",
                "U",
                cancellationToken)
            && await ExisteObjetoAsync(
                connection,
                "dbo.vw_AlmacenScrap_Registros",
                "V",
                cancellationToken)
            && await ExisteObjetoAsync(
                connection,
                "dbo.usp_AlmacenScrap_RealizarMPMolido",
                "P",
                cancellationToken);
    }

    private static AlmacenScrapRegistroListaVm MapearRegistro(
        SqlDataReader reader)
    {
        return new AlmacenScrapRegistroListaVm
        {
            ScrapRegistroID = EnteroLargo(reader, "ScrapRegistroID"),
            Origen = Texto(reader, "Origen"),
            OrigenReferencia = Texto(reader, "OrigenReferencia"),
            CodigoBarras = Texto(reader, "CodigoBarras"),
            NumeroOF = Texto(reader, "NumeroOF"),
            SolicitudProduccionID = NullableInt(reader, "SolicitudProduccionID"),
            ParteID = NullableInt(reader, "ParteID"),
            NumeroParte = Texto(reader, "NumeroParte"),
            Designacion = Texto(reader, "Designacion"),
            CantidadPiezas = Entero(reader, "CantidadPiezas"),
            Lote = Texto(reader, "Lote"),
            Estatus = Texto(reader, "Estatus"),
            FechaCreacion = Fecha(reader, "FechaCreacion") ?? DateTime.MinValue,
            FechaRecepcion = Fecha(reader, "FechaRecepcion"),
            RecibidoPorNombre = Texto(reader, "RecibidoPorNombre"),
            MaterialIDMolido = NullableInt(reader, "MaterialIDMolido"),
            MaterialMolido = Texto(reader, "MaterialMolido"),
            PesoMolidoKg = NullableDecimal(reader, "PesoMolidoKg"),
            MPMovimientoID = NullableLong(reader, "MPMovimientoID"),
            FechaMolido = Fecha(reader, "FechaMolido"),
            Observaciones = Texto(reader, "Observaciones")
        };
    }

    private static async Task<AlmacenScrapRegistroListaVm?> CargarRegistroAsync(
        SqlConnection connection,
        long id,
        CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT TOP (1)
    ScrapRegistroID,
    Origen,
    ISNULL(OrigenReferencia, N'') AS OrigenReferencia,
    CodigoBarras,
    NumeroOF,
    SolicitudProduccionID,
    ParteID,
    NumeroParte,
    Designacion,
    CantidadPiezas,
    Lote,
    Estatus,
    FechaCreacion,
    FechaRecepcion,
    ISNULL(RecibidoPorNombre, N'') AS RecibidoPorNombre,
    MaterialIDMolido,
    ISNULL(MaterialMolido, N'') AS MaterialMolido,
    PesoMolidoKg,
    MPMovimientoID,
    FechaMolido,
    ISNULL(Observaciones, N'') AS Observaciones
FROM dbo.vw_AlmacenScrap_Registros
WHERE ScrapRegistroID = @ScrapRegistroID
  AND Activo = 1;";

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@ScrapRegistroID", SqlDbType.BigInt).Value = id;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return MapearRegistro(reader);
    }

    private static async Task PrepararMolidoAsync(
        AlmacenScrapMolidoVm vm,
        AlmacenScrapRegistroListaVm registro,
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        vm.ScrapRegistroID = registro.ScrapRegistroID;
        vm.Origen = registro.Origen;
        vm.CodigoBarras = registro.CodigoBarras;
        vm.NumeroOF = registro.NumeroOF;
        vm.NumeroParte = registro.NumeroParte;
        vm.Designacion = registro.Designacion;
        vm.CantidadPiezas = registro.CantidadPiezas;
        vm.Lote = registro.Lote;
        vm.Estatus = registro.Estatus;
        vm.ParteID = registro.ParteID;

        var catalogo = await ResolverCatalogoAsync(
            connection,
            registro.NumeroParte,
            cancellationToken);

        if (registro.ParteID.HasValue)
        {
            var material =
                await ResolverMaterialPorParteAsync(
                    connection,
                    registro.ParteID.Value,
                    cancellationToken);

            vm.MaterialID = material.MaterialID;
            vm.MaterialCodigo = material.MaterialCodigo;
            vm.MaterialNombre = material.MaterialNombre;
            vm.MaterialError = material.MaterialError;
        }
        else
        {
            vm.ParteID = catalogo.ParteID;
            vm.MaterialID = catalogo.MaterialID;
            vm.MaterialCodigo = catalogo.MaterialCodigo;
            vm.MaterialNombre = catalogo.MaterialNombre;
            vm.MaterialError =
                catalogo.ParteID.HasValue
                    ? catalogo.MaterialError
                    : catalogo.ParteError;
        }

        vm.Ubicaciones = await CargarUbicacionesMPAsync(
            connection,
            cancellationToken);
    }

    private async Task RegistrarEntradaEscanerAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        AlmacenScrapCodigoPreviewVm item,
        string? observaciones,
        CancellationToken cancellationToken)
    {
        await using var command =
            new SqlCommand(
                "dbo.usp_AlmacenScrap_RegistrarEntradaEscaner",
                connection,
                transaction)
            {
                CommandType = CommandType.StoredProcedure
            };

        command.Parameters.Add("@CodigoBarras", SqlDbType.NVarChar, 500).Value =
            item.CodigoOriginal;
        command.Parameters.Add("@NumeroOF", SqlDbType.NVarChar, 80).Value =
            item.NumeroOF;
        command.Parameters.Add("@NumeroParte", SqlDbType.NVarChar, 120).Value =
            item.NumeroParte;
        command.Parameters.Add("@Designacion", SqlDbType.NVarChar, 300).Value =
            item.Designacion;
        command.Parameters.Add("@CantidadPiezas", SqlDbType.Int).Value =
            item.CantidadPiezas;
        command.Parameters.Add("@Lote", SqlDbType.NVarChar, 120).Value =
            item.Lote;
        command.Parameters.Add("@ParteID", SqlDbType.Int).Value =
            item.ParteID.HasValue ? item.ParteID.Value : DBNull.Value;
        command.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 800).Value =
            string.IsNullOrWhiteSpace(observaciones)
                ? DBNull.Value
                : observaciones.Trim();
        command.Parameters.Add("@UsuarioID", SqlDbType.Int).Value =
            UsuarioID.HasValue ? UsuarioID.Value : DBNull.Value;
        command.Parameters.Add("@UsuarioNombre", SqlDbType.NVarChar, 180).Value =
            UsuarioNombre;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<long?> BuscarScrapExistenteAsync(
        SqlConnection connection,
        string codigo,
        CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT TOP (1) ScrapRegistroID
FROM dbo.AlmacenScrap_Registros
WHERE Activo = 1
  AND CodigoBarras = @CodigoBarras
ORDER BY ScrapRegistroID DESC;";

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@CodigoBarras", SqlDbType.NVarChar, 500).Value =
            codigo.Trim();

        var value = await command.ExecuteScalarAsync(cancellationToken);

        if (value is null || value == DBNull.Value)
            return null;

        return Convert.ToInt64(value);
    }

    private static async Task<CatalogoResolucion> ResolverCatalogoAsync(
        SqlConnection connection,
        string numeroParte,
        CancellationToken cancellationToken)
    {
        var result = new CatalogoResolucion();

        const string sql = @"
DECLARE @Numero NVARCHAR(120) = LTRIM(RTRIM(@NumeroParte));
DECLARE @Normalizado NVARCHAR(120) =
    UPPER
    (
        REPLACE
        (
            REPLACE
            (
                REPLACE
                (
                    REPLACE(@Numero, N'.', N''),
                    N'-', N''
                ),
                N'_', N''
            ),
            N' ', N''
        )
    );

SELECT TOP (10)
    ParteID,
    NumeroParte,
    Descripcion
FROM dbo.ERP_Partes
WHERE Activo = 1
  AND
  (
      UPPER(LTRIM(RTRIM(NumeroParte))) = UPPER(@Numero)
      OR
      UPPER
      (
          REPLACE
          (
              REPLACE
              (
                  REPLACE
                  (
                      REPLACE(LTRIM(RTRIM(NumeroParte)), N'.', N''),
                      N'-', N''
                  ),
                  N'_', N''
              ),
              N' ', N''
          )
      ) = @Normalizado
  )
ORDER BY
    CASE
        WHEN UPPER(LTRIM(RTRIM(NumeroParte))) = UPPER(@Numero) THEN 0
        ELSE 1
    END,
    ParteID;";

        var partes = new List<ParteCandidata>();

        await using (var command = new SqlCommand(sql, connection))
        {
            command.Parameters.Add("@NumeroParte", SqlDbType.NVarChar, 120).Value =
                numeroParte.Trim();

            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                partes.Add(
                    new ParteCandidata
                    {
                        ParteID = Entero(reader, "ParteID"),
                        NumeroParte = Texto(reader, "NumeroParte"),
                        Descripcion = Texto(reader, "Descripcion")
                    });
            }
        }

        var exactas =
            partes.Where(
                    x => x.NumeroParte.Equals(
                        numeroParte.Trim(),
                        StringComparison.OrdinalIgnoreCase))
                .ToList();

        ParteCandidata? seleccionada = null;

        if (exactas.Count == 1)
        {
            seleccionada = exactas[0];
        }
        else if (exactas.Count > 1)
        {
            result.ParteError =
                "Hay más de una parte activa con el mismo número exacto.";
            return result;
        }
        else if (partes.Count == 1)
        {
            seleccionada = partes[0];
        }
        else if (partes.Count > 1)
        {
            result.ParteError =
                "La comparación normalizada encontró más de una parte activa. Requiere revisión.";
            return result;
        }
        else
        {
            result.ParteError =
                "No se encontró una parte activa para el número de parte del código.";
            return result;
        }

        result.ParteID = seleccionada.ParteID;
        result.ParteDescripcion = seleccionada.Descripcion;

        var material =
            await ResolverMaterialPorParteAsync(
                connection,
                seleccionada.ParteID,
                cancellationToken);

        result.MaterialID = material.MaterialID;
        result.MaterialCodigo = material.MaterialCodigo;
        result.MaterialNombre = material.MaterialNombre;
        result.MaterialError = material.MaterialError;

        return result;
    }

    private static async Task<CatalogoResolucion> ResolverMaterialPorParteAsync(
        SqlConnection connection,
        int parteId,
        CancellationToken cancellationToken)
    {
        var result = new CatalogoResolucion
        {
            ParteID = parteId
        };

        const string sql = @"
SELECT DISTINCT TOP (5)
    m.MaterialID,
    m.Codigo,
    m.Nombre
FROM dbo.ERP_ParteDatosTecnicos d
INNER JOIN dbo.ERP_Materiales m
    ON m.MaterialID = d.MaterialID
   AND m.Activo = 1
WHERE d.ParteID = @ParteID
  AND d.Activo = 1
  AND d.MaterialID IS NOT NULL
ORDER BY m.MaterialID;";

        var materiales = new List<MaterialCandidato>();

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@ParteID", SqlDbType.Int).Value = parteId;

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            materiales.Add(
                new MaterialCandidato
                {
                    MaterialID = Entero(reader, "MaterialID"),
                    Codigo = Texto(reader, "Codigo"),
                    Nombre = Texto(reader, "Nombre")
                });
        }

        if (materiales.Count == 1)
        {
            result.MaterialID = materiales[0].MaterialID;
            result.MaterialCodigo = materiales[0].Codigo;
            result.MaterialNombre = materiales[0].Nombre;
        }
        else if (materiales.Count == 0)
        {
            result.MaterialError =
                "La parte no tiene un MaterialID activo configurado en ERP_ParteDatosTecnicos.";
        }
        else
        {
            result.MaterialError =
                "La parte tiene más de un material activo asociado. No se generará MP Molido automáticamente.";
        }

        return result;
    }

    private static async Task<List<AlmacenSelectVm>> CargarUbicacionesMPAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        var rows = new List<AlmacenSelectVm>();

        const string sql = @"
SELECT
    UbicacionID,
    Almacen,
    Rack,
    ISNULL(Nivel, N'') AS Nivel,
    ISNULL(Posicion, N'') AS Posicion
FROM dbo.ERP_Ubicaciones
WHERE Activo = 1
  AND UPPER(LTRIM(RTRIM(Almacen))) = N'MP'
ORDER BY Rack, Nivel, Posicion, UbicacionID;";

        await using var command = new SqlCommand(sql, connection);
        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var texto = string.Join(
                " · ",
                new[]
                {
                    Texto(reader, "Almacen"),
                    Texto(reader, "Rack"),
                    Texto(reader, "Nivel"),
                    Texto(reader, "Posicion")
                }.Where(x => !string.IsNullOrWhiteSpace(x)));

            rows.Add(
                new AlmacenSelectVm
                {
                    Id = Entero(reader, "UbicacionID"),
                    Texto = texto
                });
        }

        return rows;
    }

    private static async Task<bool> EsUbicacionMPValidaAsync(
        SqlConnection connection,
        int ubicacionId,
        CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT CASE WHEN EXISTS
(
    SELECT 1
    FROM dbo.ERP_Ubicaciones
    WHERE UbicacionID = @UbicacionID
      AND Activo = 1
      AND UPPER(LTRIM(RTRIM(Almacen))) = N'MP'
) THEN 1 ELSE 0 END;";

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@UbicacionID", SqlDbType.Int).Value = ubicacionId;

        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    private static List<string> SepararCodigos(string? valor)
    {
        return (valor ?? string.Empty)
            .Replace("\r\n", "\n")
            .Replace("\r", "\n")
            .Split(
                new[] { '\n', ';', ',' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();
    }

    private static string NormalizarNumeroOF(string? valor)
    {
        return (valor ?? string.Empty)
            .Trim()
            .Replace('’', '/')
            .Replace('‘', '/')
            .Replace('`', '/')
            .Replace('´', '/')
            .Replace('\'', '/');
    }

    private static string? NormalizarFiltro(
        string? valor,
        IEnumerable<string> permitidos)
    {
        if (string.IsNullOrWhiteSpace(valor))
            return null;

        var normalizado = valor.Trim().ToUpperInvariant();

        return permitidos.Contains(
            normalizado,
            StringComparer.OrdinalIgnoreCase)
                ? normalizado
                : null;
    }

    private static int? NullableInt(
        SqlDataReader reader,
        string columna)
    {
        var ordinal = reader.GetOrdinal(columna);
        return reader.IsDBNull(ordinal)
            ? null
            : Convert.ToInt32(reader.GetValue(ordinal));
    }

    private static long? NullableLong(
        SqlDataReader reader,
        string columna)
    {
        var ordinal = reader.GetOrdinal(columna);
        return reader.IsDBNull(ordinal)
            ? null
            : Convert.ToInt64(reader.GetValue(ordinal));
    }

    private static decimal? NullableDecimal(
        SqlDataReader reader,
        string columna)
    {
        var ordinal = reader.GetOrdinal(columna);
        return reader.IsDBNull(ordinal)
            ? null
            : Convert.ToDecimal(reader.GetValue(ordinal));
    }

    private sealed class ParteCandidata
    {
        public int ParteID { get; set; }
        public string NumeroParte { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
    }

    private sealed class MaterialCandidato
    {
        public int MaterialID { get; set; }
        public string Codigo { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
    }

    private sealed class CatalogoResolucion
    {
        public int? ParteID { get; set; }
        public string ParteDescripcion { get; set; } = string.Empty;
        public string ParteError { get; set; } = string.Empty;

        public int? MaterialID { get; set; }
        public string MaterialCodigo { get; set; } = string.Empty;
        public string MaterialNombre { get; set; } = string.Empty;
        public string MaterialError { get; set; } = string.Empty;
    }
}
