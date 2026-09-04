// NSQ_NOTIFICACIONES_INTERNAS_V6
// Servicio de notificaciones internas:
// - Destinatarios por dbo.Usuarios.DepartamentoID.
// - Eventos detallados consultados desde el registro REAL ya confirmado.
// - Acceso rapido solo cuando existe una URL exacta.
// - Sin correo; correo se conectara despues sobre estos mismos eventos.
using Microsoft.Data.SqlClient;
using System.Data;
using System.Globalization;
using System.Text;

namespace ERP.NSQuell.Servicios;

public sealed class NotificacionDepartamentalService
{
    private readonly string _connectionString;
    private readonly ILogger<NotificacionDepartamentalService> _logger;
    private readonly NotificacionCorreoErpService _correoErp;

    public NotificacionDepartamentalService(
        IConfiguration configuration,
        NotificacionCorreoErpService correoErp,
        ILogger<NotificacionDepartamentalService> logger)
    {
        _connectionString =
            configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "Falta ConnectionStrings:DefaultConnection.");

        _logger = logger;
        _correoErp = correoErp;
    }

    private sealed record MovimientoMpDatos(
        long MovimientoID,
        int MaterialID,
        string CodigoEntregado,
        string NombreEntregado,
        string CodigoSolicitado,
        string TipoMovimiento,
        decimal CantidadTotal,
        decimal CantidadVirgen,
        decimal CantidadMolido,
        string NumeroOF,
        int? SolicitudProduccionID,
        string ReferenciaBase,
        string Observaciones,
        DateTime FechaMovimiento);

    private sealed record MovimientoEmbalajeDatos(
        long MovimientoID,
        int EmbalajeID,
        string CodigoEntregado,
        string NombreEntregado,
        string CodigoSolicitado,
        string TipoMovimiento,
        decimal Cantidad,
        string Unidad,
        string NumeroOF,
        int? SolicitudProduccionID,
        string Referencia,
        string Observaciones,
        DateTime FechaMovimiento);

    private sealed record ReprogramacionDatos(
        int ReprogramacionHistorialID,
        int ProgramaProduccionID,
        int? SolicitudProduccionID,
        string NumeroOF,
        string NumeroParte,
        string DescripcionParte,
        string MaquinaAnterior,
        string MaquinaNueva,
        DateTime? InicioAnterior,
        DateTime? InicioNuevo,
        DateTime? FinAnterior,
        DateTime? FinNuevo,
        string Motivo,
        DateTime FechaCambio);

    public async Task<string?> ResolverAreaAsync(
        string? controller)
    {
        controller = controller?.Trim();

        if (string.IsNullOrWhiteSpace(controller))
            return null;

        if (controller.StartsWith(
                "Planeacion",
                StringComparison.OrdinalIgnoreCase))
            return "Planeación";

        if (controller.StartsWith(
                "Produccion",
                StringComparison.OrdinalIgnoreCase)
            || controller.StartsWith(
                "AgendaOperativa",
                StringComparison.OrdinalIgnoreCase))
            return "Producción";

        if (controller.StartsWith(
                "Calidad",
                StringComparison.OrdinalIgnoreCase))
            return "Calidad";

        if (controller.StartsWith(
                "GP12",
                StringComparison.OrdinalIgnoreCase))
            return "GP12";

        if (controller.StartsWith(
                "Almacen",
                StringComparison.OrdinalIgnoreCase))
            return "Almacén";

        if (controller.StartsWith(
                "Logistica",
                StringComparison.OrdinalIgnoreCase))
            return "Logística";

        if (controller.StartsWith(
                "Compras",
                StringComparison.OrdinalIgnoreCase))
            return "Compras";

        if (controller.StartsWith(
                "Mantenimiento",
                StringComparison.OrdinalIgnoreCase))
            return "Mantenimiento";

        if (controller.StartsWith(
                "RRHH",
                StringComparison.OrdinalIgnoreCase)
            || controller.StartsWith(
                "RecursosHumanos",
                StringComparison.OrdinalIgnoreCase))
            return "Recursos Humanos";

        await using var cn =
            new SqlConnection(_connectionString);

        await cn.OpenAsync();

        const string sql = """
SELECT TOP(1)
    mg.Nombre
FROM dbo.SubMenus sm
INNER JOIN dbo.Menus m
    ON m.MenuID=sm.MenuID
INNER JOIN dbo.MenuGrupo mg
    ON mg.MenuGrupoID=m.MenuGrupoID
WHERE ISNULL(sm.Activo,1)=1
  AND ISNULL(m.Activo,1)=1
  AND ISNULL(mg.Activo,1)=1
  AND
  (
      REPLACE(
          LTRIM(RTRIM(ISNULL(sm.UrlEnlace,''))),
          '~',
          '') LIKE @Prefijo
      OR
      REPLACE(
          LTRIM(RTRIM(ISNULL(sm.UrlEnlace,''))),
          '~',
          '') = @Raiz
  )
ORDER BY
    CASE
        WHEN REPLACE(
                 LTRIM(RTRIM(ISNULL(sm.UrlEnlace,''))),
                 '~',
                 '') LIKE @Prefijo
        THEN 0 ELSE 1
    END,
    sm.SubMenuID;
""";

        await using var cmd =
            new SqlCommand(sql, cn);

        cmd.Parameters.Add(
            "@Prefijo",
            SqlDbType.VarChar,
            500).Value =
            $"/{controller}/%";

        cmd.Parameters.Add(
            "@Raiz",
            SqlDbType.VarChar,
            500).Value =
            $"/{controller}";

        var value =
            await cmd.ExecuteScalarAsync();

        return value == null
               || value == DBNull.Value
            ? null
            : value.ToString()?.Trim();
    }

    public async Task<bool> NotificarOperacionDetalladaAsync(
        string area,
        string controller,
        string action,
        int actorUsuarioId,
        string? actor,
        IReadOnlyDictionary<string, string?> datos)
    {
        var controllerKey =
            NormalizarCodigo(controller);

        var actionKey =
            NormalizarCodigo(action);

        if (controllerKey == "ALMACENMP"
            && actionKey == "MOVIMIENTO")
        {
            var token =
                Dato(datos, "OperacionToken");

            if (string.IsNullOrWhiteSpace(token))
                return false;

            var detalle =
                await ObtenerMovimientoMpAsync(token);

            if (detalle == null)
                return false;

            await PublicarMovimientoMpAsync(
                area,
                actor,
                detalle);

            return true;
        }

        if (controllerKey == "ALMACENEMBALAJES"
            && actionKey == "MOVIMIENTO")
        {
            var token =
                Dato(datos, "OperacionToken");

            if (string.IsNullOrWhiteSpace(token))
                return false;

            var detalle =
                await ObtenerMovimientoEmbalajeAsync(token);

            if (detalle == null)
                return false;

            await PublicarMovimientoEmbalajeAsync(
                area,
                actor,
                detalle);

            return true;
        }

        if (controllerKey == "PLANEACIONCALENDARIOMAQUINAS"
            && actionKey == "REPROGRAMARCALENDARIO")
        {
            /*
             * Si no viene ConfirmarMovimiento=true, es una previsualizacion.
             * Se considera manejada para impedir un aviso falso.
             */
            if (!TryDatoBool(
                    datos,
                    "ConfirmarMovimiento",
                    out var confirmar)
                || !confirmar)
            {
                return true;
            }

            if (!TryDatoInt(
                    datos,
                    "ProgramaProduccionID",
                    out var programaProduccionId))
            {
                return false;
            }

            var detalle =
                await ObtenerReprogramacionAsync(
                    programaProduccionId,
                    actorUsuarioId);

            if (detalle == null)
                return false;

            await PublicarReprogramacionAsync(
                area,
                actor,
                detalle);

            return true;
        }

        return false;
    }

    public async Task NotificarAsync(
        string area,
        string controller,
        string action,
        int idOrigen,
        string? actor,
        int? solicitudProduccionId = null,
        string? urlDestino = null,
        IReadOnlyDictionary<string, string?>? datos = null)
    {
        area =
            (area ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(area))
            return;

        controller =
            (controller ?? string.Empty).Trim();

        action =
            (action ?? string.Empty).Trim();

        actor =
            string.IsNullOrWhiteSpace(actor)
                ? "Usuario ERP"
                : actor.Trim();

        var titulo =
            Recortar(
                $"{area}: {Humanizar(action)}",
                200);

        var resumen =
            ConstruirResumenGenerico(datos);

        var mensaje =
            string.IsNullOrWhiteSpace(resumen)
                ? $"{actor} realizó {Humanizar(action)}."
                : $"{actor} realizó {Humanizar(action)}. {resumen}";

        var rutaDestino =
            NormalizarRutaLocal(urlDestino);

        if (rutaDestino == null
            && solicitudProduccionId.HasValue
            && solicitudProduccionId.Value > 0)
        {
            rutaDestino =
                $"/SolicitudesProduccion/Detalle/" +
                $"{solicitudProduccionId.Value}?soloLectura=1";
        }

        await InsertarDepartamentoAsync(
            area,
            codigoEvento:
                ConstruirCodigoEvento(
                    area,
                    action),
            tipo: "EVENTO_DEPARTAMENTO",
            titulo: titulo,
            mensaje: Recortar(mensaje, 500),
            idOrigen: Math.Max(0, idOrigen),
            tablaOrigen: Recortar(controller, 40),
            urlDestino: rutaDestino,
            fechaEvento: DateTime.Now,
            eventoUnico: false);
    }

    private async Task PublicarMovimientoMpAsync(
        string area,
        string? actor,
        MovimientoMpDatos dato)
    {
        actor =
            string.IsNullOrWhiteSpace(actor)
                ? "Usuario ERP"
                : actor.Trim();

        var esEntregaOf =
            dato.SolicitudProduccionID.HasValue
            && dato.SolicitudProduccionID.Value > 0;

        var titulo =
            esEntregaOf
                ? $"Almacén MP: Entrega a {Texto(dato.NumeroOF, "OF")}"
                : $"Almacén MP: {Humanizar(dato.TipoMovimiento)} · {dato.CodigoEntregado}";

        var mensaje =
            new StringBuilder();

        mensaje.Append(actor);

        mensaje.Append(
            esEntregaOf
                ? " entregó "
                : " registró ");

        if (!esEntregaOf)
        {
            mensaje.Append(
                Humanizar(dato.TipoMovimiento)
                    .ToLowerInvariant());

            mensaje.Append(" de ");
        }

        mensaje.Append(
            FormatoCantidad(
                dato.CantidadTotal));

        mensaje.Append(" KG de ");
        mensaje.Append(dato.CodigoEntregado);

        if (!string.IsNullOrWhiteSpace(
                dato.NombreEntregado))
        {
            mensaje.Append(" · ");
            mensaje.Append(dato.NombreEntregado);
        }

        if (esEntregaOf)
        {
            mensaje.Append(" para ");
            mensaje.Append(
                Texto(
                    dato.NumeroOF,
                    $"Solicitud #{dato.SolicitudProduccionID}"));
        }

        if (dato.CantidadVirgen > 0.0005m
            || dato.CantidadMolido > 0.0005m)
        {
            mensaje.Append(". Desglose: Virgen ");
            mensaje.Append(
                FormatoCantidad(
                    dato.CantidadVirgen));
            mensaje.Append(" KG; Molido ");
            mensaje.Append(
                FormatoCantidad(
                    dato.CantidadMolido));
            mensaje.Append(" KG");
        }

        if (!string.IsNullOrWhiteSpace(
                dato.CodigoSolicitado)
            && !dato.CodigoSolicitado.Equals(
                dato.CodigoEntregado,
                StringComparison.OrdinalIgnoreCase))
        {
            mensaje.Append(
                $". Sustitución: solicitado {dato.CodigoSolicitado}; " +
                $"entregado {dato.CodigoEntregado}");
        }

        if (!string.IsNullOrWhiteSpace(
                dato.Observaciones))
        {
            mensaje.Append(". Obs.: ");
            mensaje.Append(
                Recortar(
                    dato.Observaciones,
                    150));
        }

        mensaje.Append('.');

        var referenciaFiltro =
            Uri.EscapeDataString(
                dato.ReferenciaBase);

        var materialFiltro =
            Uri.EscapeDataString(
                dato.CodigoEntregado);

        var url =
            $"/AlmacenMP/Historial" +
            $"?material={materialFiltro}" +
            $"&q={referenciaFiltro}" +
            $"#movimiento-{dato.MovimientoID}";

        var codigo =
            esEntregaOf
                ? "ALMACEN_MP_ENTREGA_OF"
                : $"ALMACEN_MP_{NormalizarCodigo(dato.TipoMovimiento)}";

        await InsertarDepartamentoAsync(
            area,
            codigo,
            "EVENTO_DEPARTAMENTO",
            Recortar(titulo, 200),
            Recortar(mensaje.ToString(), 500),
            IdNotificacion(dato.MovimientoID, dato.MaterialID),
            "AlmacenMP",
            url,
            dato.FechaMovimiento,
            eventoUnico: true);
    }

    private async Task PublicarMovimientoEmbalajeAsync(
        string area,
        string? actor,
        MovimientoEmbalajeDatos dato)
    {
        actor =
            string.IsNullOrWhiteSpace(actor)
                ? "Usuario ERP"
                : actor.Trim();

        var esEntregaOf =
            dato.SolicitudProduccionID.HasValue
            && dato.SolicitudProduccionID.Value > 0;

        var titulo =
            esEntregaOf
                ? $"Almacén Embalajes: Entrega a {Texto(dato.NumeroOF, "OF")}"
                : $"Almacén Embalajes: {Humanizar(dato.TipoMovimiento)} · {dato.CodigoEntregado}";

        var mensaje =
            new StringBuilder();

        mensaje.Append(actor);

        mensaje.Append(
            esEntregaOf
                ? " entregó "
                : " registró ");

        if (!esEntregaOf)
        {
            mensaje.Append(
                Humanizar(dato.TipoMovimiento)
                    .ToLowerInvariant());

            mensaje.Append(" de ");
        }

        mensaje.Append(
            FormatoCantidad(
                dato.Cantidad));

        mensaje.Append(' ');
        mensaje.Append(
            Texto(dato.Unidad, "PZA"));

        mensaje.Append(" de ");
        mensaje.Append(dato.CodigoEntregado);

        if (!string.IsNullOrWhiteSpace(
                dato.NombreEntregado))
        {
            mensaje.Append(" · ");
            mensaje.Append(dato.NombreEntregado);
        }

        if (esEntregaOf)
        {
            mensaje.Append(" para ");
            mensaje.Append(
                Texto(
                    dato.NumeroOF,
                    $"Solicitud #{dato.SolicitudProduccionID}"));
        }

        if (!string.IsNullOrWhiteSpace(
                dato.CodigoSolicitado)
            && !dato.CodigoSolicitado.Equals(
                dato.CodigoEntregado,
                StringComparison.OrdinalIgnoreCase))
        {
            mensaje.Append(
                $". Sustitución: solicitado {dato.CodigoSolicitado}; " +
                $"entregado {dato.CodigoEntregado}");
        }

        if (!string.IsNullOrWhiteSpace(
                dato.Observaciones))
        {
            mensaje.Append(". Obs.: ");
            mensaje.Append(
                Recortar(
                    dato.Observaciones,
                    150));
        }

        mensaje.Append('.');

        var referenciaFiltro =
            Uri.EscapeDataString(
                dato.Referencia);

        var embalajeFiltro =
            Uri.EscapeDataString(
                dato.CodigoEntregado);

        var url =
            $"/AlmacenEmbalajes/Historial" +
            $"?embalaje={embalajeFiltro}" +
            $"&q={referenciaFiltro}" +
            $"#movimiento-{dato.MovimientoID}";

        var codigo =
            esEntregaOf
                ? "ALMACEN_EMBALAJE_ENTREGA_OF"
                : $"ALMACEN_EMBALAJE_{NormalizarCodigo(dato.TipoMovimiento)}";

        await InsertarDepartamentoAsync(
            area,
            codigo,
            "EVENTO_DEPARTAMENTO",
            Recortar(titulo, 200),
            Recortar(mensaje.ToString(), 500),
            IdNotificacion(dato.MovimientoID, dato.EmbalajeID),
            "AlmacenEmbalajes",
            url,
            dato.FechaMovimiento,
            eventoUnico: true);
    }

    private async Task PublicarReprogramacionAsync(
        string area,
        string? actor,
        ReprogramacionDatos dato)
    {
        actor =
            string.IsNullOrWhiteSpace(actor)
                ? "Usuario ERP"
                : actor.Trim();

        var identidad =
            !string.IsNullOrWhiteSpace(dato.NumeroOF)
                ? dato.NumeroOF
                : $"Programa {dato.ProgramaProduccionID}";

        var titulo =
            $"Planeación: {identidad} reprogramada";

        var cambios =
            new List<string>();

        if (!dato.MaquinaAnterior.Equals(
                dato.MaquinaNueva,
                StringComparison.OrdinalIgnoreCase))
        {
            cambios.Add(
                $"Máquina: {Texto(dato.MaquinaAnterior, "Sin máquina")} → " +
                $"{Texto(dato.MaquinaNueva, "Sin máquina")}");
        }

        if (dato.InicioAnterior != dato.InicioNuevo)
        {
            cambios.Add(
                $"Inicio: {FormatoFecha(dato.InicioAnterior)} → " +
                $"{FormatoFecha(dato.InicioNuevo)}");
        }

        if (dato.FinAnterior != dato.FinNuevo)
        {
            cambios.Add(
                $"Fin: {FormatoFecha(dato.FinAnterior)} → " +
                $"{FormatoFecha(dato.FinNuevo)}");
        }

        var mensaje =
            new StringBuilder();

        mensaje.Append(actor);
        mensaje.Append(" reprogramó ");
        mensaje.Append(identidad);

        if (!string.IsNullOrWhiteSpace(
                dato.NumeroParte))
        {
            mensaje.Append(" · Parte ");
            mensaje.Append(dato.NumeroParte);
        }

        if (cambios.Count > 0)
        {
            mensaje.Append(". ");
            mensaje.Append(
                string.Join(
                    ". ",
                    cambios));
        }

        if (!string.IsNullOrWhiteSpace(
                dato.Motivo))
        {
            mensaje.Append(". Motivo: ");
            mensaje.Append(
                Recortar(
                    dato.Motivo,
                    150));
        }

        mensaje.Append('.');

        var fechaFoco =
            dato.InicioNuevo
            ?? dato.FechaCambio;

        var url =
            "/PlaneacionCalendarioMaquinas/Index" +
            "?vista=dia" +
            $"&fecha={fechaFoco:yyyy-MM-dd}" +
            $"&focusPrograma={dato.ProgramaProduccionID}";

        await InsertarDepartamentoAsync(
            area,
            "PLANEACION_REPROGRAMACION",
            "EVENTO_DEPARTAMENTO",
            Recortar(titulo, 200),
            Recortar(mensaje.ToString(), 500),
            dato.ReprogramacionHistorialID,
            "PlaneacionCalendarioMaquinas",
            url,
            dato.FechaCambio,
            eventoUnico: true);
    }

    private async Task<MovimientoMpDatos?> ObtenerMovimientoMpAsync(
        string token)
    {
        token =
            token.Trim()
                .ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(token))
            return null;

        var referenciaBase =
            $"WEB-MP-{token}";

        const string sql = """
SELECT
    MAX(m.MovimientoID) AS MovimientoID,
    MAX(m.MaterialID) AS MaterialID,
    MAX(ISNULL(entregado.Codigo,N'')) AS CodigoEntregado,
    MAX(ISNULL(entregado.Nombre,N'')) AS NombreEntregado,
    MAX(ISNULL(solicitado.Codigo,N'')) AS CodigoSolicitado,
    MAX(ISNULL(m.TipoMovimiento,N'')) AS TipoMovimiento,
    SUM(CONVERT(decimal(18,4),ISNULL(m.Cantidad,0))) AS CantidadTotal,
    SUM
    (
        CASE
            WHEN UPPER(LTRIM(RTRIM(ISNULL(m.TipoMP,N'')))) IN(N'V',N'VIRGEN')
                THEN CONVERT(decimal(18,4),ISNULL(m.Cantidad,0))
            ELSE 0
        END
    ) AS CantidadVirgen,
    SUM
    (
        CASE
            WHEN UPPER(LTRIM(RTRIM(ISNULL(m.TipoMP,N'')))) IN(N'M',N'MOLIDO')
                THEN CONVERT(decimal(18,4),ISNULL(m.Cantidad,0))
            ELSE 0
        END
    ) AS CantidadMolido,
    MAX(ISNULL(m.NumeroOF,N'')) AS NumeroOF,
    MAX(m.SolicitudProduccionID) AS SolicitudProduccionID,
    MAX(ISNULL(m.Seguimiento,N'')) AS Observaciones,
    MAX(m.FechaMovimiento) AS FechaMovimiento
FROM dbo.AlmacenMP_Movimientos m
INNER JOIN dbo.ERP_Materiales entregado
    ON entregado.MaterialID=m.MaterialID
LEFT JOIN dbo.ERP_Materiales solicitado
    ON solicitado.MaterialID=m.MaterialSolicitadoID
WHERE m.Activo=1
  AND UPPER(ISNULL(m.ReferenciaOperacion,N'')) LIKE @ReferenciaBase + N'%';
""";

        await using var cn =
            new SqlConnection(_connectionString);

        await cn.OpenAsync();

        await using var cmd =
            new SqlCommand(sql, cn);

        cmd.Parameters.Add(
            "@ReferenciaBase",
            SqlDbType.NVarChar,
            120).Value =
            referenciaBase;

        await using var rd =
            await cmd.ExecuteReaderAsync();

        if (!await rd.ReadAsync()
            || rd["MovimientoID"] == DBNull.Value)
        {
            return null;
        }

        return new MovimientoMpDatos(
            Convert.ToInt64(rd["MovimientoID"]),
            Convert.ToInt32(rd["MaterialID"]),
            ValorTexto(rd, "CodigoEntregado"),
            ValorTexto(rd, "NombreEntregado"),
            ValorTexto(rd, "CodigoSolicitado"),
            ValorTexto(rd, "TipoMovimiento"),
            ValorDecimal(rd, "CantidadTotal"),
            ValorDecimal(rd, "CantidadVirgen"),
            ValorDecimal(rd, "CantidadMolido"),
            ValorTexto(rd, "NumeroOF"),
            ValorNullableInt(
                rd,
                "SolicitudProduccionID"),
            referenciaBase,
            ValorTexto(rd, "Observaciones"),
            ValorFecha(rd, "FechaMovimiento")
                ?? DateTime.Now);
    }

    private async Task<MovimientoEmbalajeDatos?>
        ObtenerMovimientoEmbalajeAsync(
            string token)
    {
        token =
            token.Trim()
                .ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(token))
            return null;

        var referencia =
            $"WEB-EMB-{token}";

        const string sql = """
SELECT TOP(1)
    m.MovimientoID,
    m.EmbalajeID,
    ISNULL(entregado.Codigo,N'') AS CodigoEntregado,
    ISNULL(entregado.Nombre,N'') AS NombreEntregado,
    ISNULL(solicitado.Codigo,N'') AS CodigoSolicitado,
    ISNULL(m.TipoMovimiento,N'') AS TipoMovimiento,
    CONVERT(decimal(18,4),ISNULL(m.Cantidad,0)) AS Cantidad,
    ISNULL(NULLIF(LTRIM(RTRIM(m.Unidad)),N''),N'PZA') AS Unidad,
    ISNULL(m.NumeroOF,N'') AS NumeroOF,
    m.SolicitudProduccionID,
    ISNULL(m.ReferenciaOperacion,N'') AS ReferenciaOperacion,
    ISNULL(m.Seguimiento,N'') AS Observaciones,
    m.FechaMovimiento
FROM dbo.AlmacenEmbalajes_Movimientos m
INNER JOIN dbo.ERP_Embalajes entregado
    ON entregado.EmbalajeID=m.EmbalajeID
LEFT JOIN dbo.ERP_Embalajes solicitado
    ON solicitado.EmbalajeID=m.EmbalajeSolicitadoID
WHERE m.Activo=1
  AND UPPER(ISNULL(m.ReferenciaOperacion,N''))=@Referencia
ORDER BY m.MovimientoID DESC;
""";

        await using var cn =
            new SqlConnection(_connectionString);

        await cn.OpenAsync();

        await using var cmd =
            new SqlCommand(sql, cn);

        cmd.Parameters.Add(
            "@Referencia",
            SqlDbType.NVarChar,
            120).Value =
            referencia;

        await using var rd =
            await cmd.ExecuteReaderAsync();

        if (!await rd.ReadAsync())
            return null;

        return new MovimientoEmbalajeDatos(
            Convert.ToInt64(rd["MovimientoID"]),
            Convert.ToInt32(rd["EmbalajeID"]),
            ValorTexto(rd, "CodigoEntregado"),
            ValorTexto(rd, "NombreEntregado"),
            ValorTexto(rd, "CodigoSolicitado"),
            ValorTexto(rd, "TipoMovimiento"),
            ValorDecimal(rd, "Cantidad"),
            ValorTexto(rd, "Unidad"),
            ValorTexto(rd, "NumeroOF"),
            ValorNullableInt(
                rd,
                "SolicitudProduccionID"),
            ValorTexto(
                rd,
                "ReferenciaOperacion"),
            ValorTexto(rd, "Observaciones"),
            ValorFecha(rd, "FechaMovimiento")
                ?? DateTime.Now);
    }

    private async Task<ReprogramacionDatos?>
        ObtenerReprogramacionAsync(
            int programaProduccionId,
            int actorUsuarioId)
    {
        const string sql = """
SELECT TOP(1)
    h.ReprogramacionHistorialID,
    h.ProgramaProduccionID,
    h.SolicitudProduccionID,
    COALESCE
    (
        NULLIF(LTRIM(RTRIM(s.NumeroOFRecibida)),N''),
        NULLIF(LTRIM(RTRIM(s.FolioSolicitud)),N''),
        N''
    ) AS NumeroOF,
    COALESCE
    (
        NULLIF(LTRIM(RTRIM(pp.ReferenciaSAP)),N''),
        NULLIF(LTRIM(RTRIM(pp.NumeroParte)),N''),
        N''
    ) AS NumeroParte,
    ISNULL(pp.DesignacionDescripcionSAP,N'') AS DescripcionParte,
    COALESCE(NULLIF(LTRIM(RTRIM(ma.Codigo)),N''),N'SIN MÁQUINA') AS MaquinaAnterior,
    COALESCE(NULLIF(LTRIM(RTRIM(mn.Codigo)),N''),N'SIN MÁQUINA') AS MaquinaNueva,
    h.InicioAnterior,
    h.InicioNuevo,
    h.FinAnterior,
    h.FinNuevo,
    ISNULL(h.Motivo,N'') AS Motivo,
    h.FechaCambio
FROM dbo.Planeacion_ProgramaReprogramacionHistorial h
INNER JOIN dbo.Planeacion_ProgramaProduccion pp
    ON pp.ProgramaProduccionID=h.ProgramaProduccionID
LEFT JOIN dbo.SolicitudesProduccion s
    ON s.SolicitudProduccionID=h.SolicitudProduccionID
LEFT JOIN dbo.ERP_Maquinas ma
    ON ma.MaquinaID=h.MaquinaAnteriorID
LEFT JOIN dbo.ERP_Maquinas mn
    ON mn.MaquinaID=h.MaquinaNuevaID
WHERE h.ProgramaProduccionID=@ProgramaProduccionID
  AND h.FechaCambio>=DATEADD(MINUTE,-10,SYSDATETIME())
  AND (@ActorUsuarioID<=0 OR h.UsuarioID=@ActorUsuarioID)
ORDER BY
    h.FechaCambio DESC,
    h.ReprogramacionHistorialID DESC;
""";

        await using var cn =
            new SqlConnection(_connectionString);

        await cn.OpenAsync();

        await using var cmd =
            new SqlCommand(sql, cn);

        cmd.Parameters.Add(
            "@ProgramaProduccionID",
            SqlDbType.Int).Value =
            programaProduccionId;

        cmd.Parameters.Add(
            "@ActorUsuarioID",
            SqlDbType.Int).Value =
            actorUsuarioId;

        await using var rd =
            await cmd.ExecuteReaderAsync();

        if (!await rd.ReadAsync())
            return null;

        return new ReprogramacionDatos(
            Convert.ToInt32(
                rd["ReprogramacionHistorialID"]),
            Convert.ToInt32(
                rd["ProgramaProduccionID"]),
            ValorNullableInt(
                rd,
                "SolicitudProduccionID"),
            ValorTexto(rd, "NumeroOF"),
            ValorTexto(rd, "NumeroParte"),
            ValorTexto(rd, "DescripcionParte"),
            ValorTexto(rd, "MaquinaAnterior"),
            ValorTexto(rd, "MaquinaNueva"),
            ValorFecha(rd, "InicioAnterior"),
            ValorFecha(rd, "InicioNuevo"),
            ValorFecha(rd, "FinAnterior"),
            ValorFecha(rd, "FinNuevo"),
            ValorTexto(rd, "Motivo"),
            ValorFecha(rd, "FechaCambio")
                ?? DateTime.Now);
    }

    private async Task InsertarDepartamentoAsync(
        string area,
        string codigoEvento,
        string tipo,
        string titulo,
        string mensaje,
        int idOrigen,
        string tablaOrigen,
        string? urlDestino,
        DateTime fechaEvento,
        bool eventoUnico)
    {
        area =
            (area ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(area))
            return;

        var destinatarios =
            await ObtenerDestinatariosDepartamentoAsync(
                area);

        if (destinatarios.Count == 0)
        {
            _logger.LogWarning(
                "Evento {CodigoEvento} sin usuarios activos del departamento {Area}.",
                codigoEvento,
                area);

            return;
        }

        codigoEvento =
            Recortar(
                codigoEvento,
                80);

        tipo =
            Recortar(
                tipo,
                30);

        titulo =
            Recortar(
                titulo,
                200);

        mensaje =
            Recortar(
                mensaje,
                500);

        tablaOrigen =
            Recortar(
                tablaOrigen,
                40);

        urlDestino =
            NormalizarRutaLocal(
                urlDestino);

        var ahora =
            fechaEvento == default
                ? DateTime.Now
                : fechaEvento;

        var expira =
            ahora.AddDays(30);

        await using var cn =
            new SqlConnection(_connectionString);

        await cn.OpenAsync();

        await using var tx =
            (SqlTransaction)await cn.BeginTransactionAsync(
                IsolationLevel.Serializable);

        try
        {
            var sql =
                eventoUnico
                    ? """
IF NOT EXISTS
(
    SELECT 1
    FROM dbo.Notificaciones WITH(UPDLOCK,HOLDLOCK)
    WHERE UsuarioId=@UsuarioID
      AND CodigoEvento=@CodigoEvento
      AND IdOrigen=@IdOrigen
      AND FechaEliminacion IS NULL
)
BEGIN
    INSERT dbo.Notificaciones
    (
        Tipo,Titulo,Mensaje,IdOrigen,TablaOrigen,
        UsuarioId,EmpresaId,FechaCreacion,FechaExpiracion,
        EsLeida,FechaEliminacion,EsArchivada,CodigoEvento,UrlDestino
    )
    VALUES
    (
        @Tipo,@Titulo,@Mensaje,@IdOrigen,@TablaOrigen,
        @UsuarioID,NULL,@Ahora,@Expira,
        0,NULL,0,@CodigoEvento,@UrlDestino
    );
END;
"""
                    : """
IF NOT EXISTS
(
    SELECT 1
    FROM dbo.Notificaciones WITH(UPDLOCK,HOLDLOCK)
    WHERE UsuarioId=@UsuarioID
      AND Tipo=@Tipo
      AND TablaOrigen=@TablaOrigen
      AND IdOrigen=@IdOrigen
      AND Titulo=@Titulo
      AND FechaCreacion>=DATEADD(SECOND,-5,@Ahora)
      AND FechaEliminacion IS NULL
)
BEGIN
    INSERT dbo.Notificaciones
    (
        Tipo,Titulo,Mensaje,IdOrigen,TablaOrigen,
        UsuarioId,EmpresaId,FechaCreacion,FechaExpiracion,
        EsLeida,FechaEliminacion,EsArchivada,CodigoEvento,UrlDestino
    )
    VALUES
    (
        @Tipo,@Titulo,@Mensaje,@IdOrigen,@TablaOrigen,
        @UsuarioID,NULL,@Ahora,@Expira,
        0,NULL,0,@CodigoEvento,@UrlDestino
    );
END;
""";

            foreach (var usuarioId in destinatarios)
            {
                await using var cmd =
                    new SqlCommand(
                        sql,
                        cn,
                        tx);

                cmd.Parameters.Add(
                    "@Tipo",
                    SqlDbType.NVarChar,
                    30).Value =
                    tipo;

                cmd.Parameters.Add(
                    "@Titulo",
                    SqlDbType.NVarChar,
                    200).Value =
                    titulo;

                cmd.Parameters.Add(
                    "@Mensaje",
                    SqlDbType.NVarChar,
                    500).Value =
                    mensaje;

                cmd.Parameters.Add(
                    "@IdOrigen",
                    SqlDbType.Int).Value =
                    Math.Max(0, idOrigen);

                cmd.Parameters.Add(
                    "@TablaOrigen",
                    SqlDbType.NVarChar,
                    40).Value =
                    tablaOrigen;

                cmd.Parameters.Add(
                    "@UsuarioID",
                    SqlDbType.Int).Value =
                    usuarioId;

                cmd.Parameters.Add(
                    "@Ahora",
                    SqlDbType.DateTime).Value =
                    ahora;

                cmd.Parameters.Add(
                    "@Expira",
                    SqlDbType.DateTime).Value =
                    expira;

                cmd.Parameters.Add(
                    "@CodigoEvento",
                    SqlDbType.NVarChar,
                    80).Value =
                    codigoEvento;

                cmd.Parameters.Add(
                    "@UrlDestino",
                    SqlDbType.NVarChar,
                    500).Value =
                    (object?)urlDestino
                    ?? DBNull.Value;

                await cmd.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();

            _logger.LogInformation(
                "Notificacion {CodigoEvento}: {Usuarios} usuarios del departamento {Area}; Url={Url}.",
                codigoEvento,
                destinatarios.Count,
                area,
                urlDestino ?? "(sin acceso directo exacto)");

            // NSQ_NOTIFICACIONES_CORREO_V10
            // El correo usa exactamente el titulo/mensaje/UrlDestino ya publicados en el navbar.
            // Un fallo SMTP nunca revierte la operacion de negocio ni la notificacion interna.
            try
            {
                var resultadoCorreo = await _correoErp.EnviarAUsuariosAsync(
                    destinatarios,
                    titulo,
                    mensaje,
                    urlDestino,
                    codigoEvento,
                    area);

                _logger.LogInformation(
                    "Correo {CodigoEvento}: encontrados={Encontrados}; enviados={Enviados}; bloqueados={Bloqueados}; errores={Errores}.",
                    codigoEvento,
                    resultadoCorreo.Encontrados,
                    resultadoCorreo.Enviados,
                    resultadoCorreo.FiltradosPorCandados,
                    resultadoCorreo.Errores);
            }
            catch (Exception exCorreo)
            {
                _logger.LogError(
                    exCorreo,
                    "La notificacion interna {CodigoEvento} se guardo, pero fallo su correo.",
                    codigoEvento);
            }
        }
        catch
        {
            try
            {
                await tx.RollbackAsync();
            }
            catch
            {
            }

            throw;
        }
    }

    public async Task<int?> ResolverSolicitudProduccionIdAsync(
        string tipoReferencia,
        int referenciaId)
    {
        if (referenciaId <= 0
            || string.IsNullOrWhiteSpace(
                tipoReferencia))
        {
            return null;
        }

        tipoReferencia =
            tipoReferencia.Trim()
                .ToUpperInvariant();

        string? sql =
            tipoReferencia switch
            {
                "PROGRAMA" =>
                    "SELECT SolicitudProduccionID " +
                    "FROM dbo.Planeacion_ProgramaProduccion " +
                    "WHERE ProgramaProduccionID=@ID " +
                    "AND SolicitudProduccionID IS NOT NULL;",

                "EJECUCION" =>
                    "SELECT SolicitudProduccionID " +
                    "FROM dbo.Produccion_Ejecucion " +
                    "WHERE EjecucionProduccionID=@ID " +
                    "AND SolicitudProduccionID IS NOT NULL;",

                "INSPECCION" =>
                    "SELECT SolicitudProduccionID " +
                    "FROM dbo.Calidad_Inspecciones " +
                    "WHERE InspeccionID=@ID " +
                    "AND SolicitudProduccionID IS NOT NULL;",

                "GP12" =>
                    "SELECT SolicitudProduccionID " +
                    "FROM dbo.GP12_Solicitudes " +
                    "WHERE SolicitudGP12ID=@ID " +
                    "AND SolicitudProduccionID IS NOT NULL;",

                "SCRAP" =>
                    "SELECT SolicitudProduccionID " +
                    "FROM dbo.AlmacenScrap_Registros " +
                    "WHERE ScrapRegistroID=@ID " +
                    "AND SolicitudProduccionID IS NOT NULL;",

                "EMBARQUE" =>
                    "SELECT CASE " +
                    "WHEN COUNT(DISTINCT SolicitudProduccionID)=1 " +
                    "THEN MIN(SolicitudProduccionID) ELSE NULL END " +
                    "FROM dbo.Logistica_EmbarqueDetalle " +
                    "WHERE EmbarqueID=@ID " +
                    "AND SolicitudProduccionID IS NOT NULL " +
                    "AND ISNULL(Activo,1)=1;",

                _ => null
            };

        if (sql == null)
            return null;

        await using var cn =
            new SqlConnection(_connectionString);

        await cn.OpenAsync();

        await using var cmd =
            new SqlCommand(sql, cn);

        cmd.Parameters.Add(
            "@ID",
            SqlDbType.Int).Value =
            referenciaId;

        var value =
            await cmd.ExecuteScalarAsync();

        return value == null
               || value == DBNull.Value
            ? null
            : Convert.ToInt32(value);
    }

    private async Task<List<int>>
        ObtenerDestinatariosDepartamentoAsync(
            string area)
    {
        const string sql = """
SELECT DISTINCT
    u.UsuarioID
FROM dbo.Usuarios u
INNER JOIN dbo.Departamentos d
    ON d.DepartamentoID=u.DepartamentoID
WHERE ISNULL(u.Activo,1)=1
  AND ISNULL(d.Activo,1)=1
  AND d.NombreDepartamento COLLATE Latin1_General_100_CI_AI
      = @Area COLLATE Latin1_General_100_CI_AI
ORDER BY u.UsuarioID;
""";

        var salida =
            new List<int>();

        await using var cn =
            new SqlConnection(_connectionString);

        await cn.OpenAsync();

        await using var cmd =
            new SqlCommand(sql, cn);

        cmd.Parameters.Add(
            "@Area",
            SqlDbType.NVarChar,
            150).Value =
            area;

        await using var rd =
            await cmd.ExecuteReaderAsync();

        while (await rd.ReadAsync())
        {
            salida.Add(
                Convert.ToInt32(
                    rd["UsuarioID"]));
        }

        return salida;
    }

    private static string ConstruirResumenGenerico(
        IReadOnlyDictionary<string, string?>? datos)
    {
        if (datos == null
            || datos.Count == 0)
        {
            return string.Empty;
        }

        var partes =
            new List<string>();

        var numeroOf =
            Dato(datos, "NumeroOF");

        if (!string.IsNullOrWhiteSpace(numeroOf))
            partes.Add($"OF: {numeroOf}");

        if (TryDatoInt(
                datos,
                "ProgramaProduccionID",
                out var programaId))
        {
            partes.Add(
                $"Programa: {programaId}");
        }

        var tipo =
            Dato(datos, "TipoMovimiento");

        if (!string.IsNullOrWhiteSpace(tipo))
            partes.Add($"Movimiento: {Humanizar(tipo)}");

        var cantidad =
            Dato(datos, "Cantidad");

        if (!string.IsNullOrWhiteSpace(cantidad)
            && decimal.TryParse(
                cantidad,
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out var cantidadDecimal))
        {
            partes.Add(
                $"Cantidad: {FormatoCantidad(cantidadDecimal)}");
        }

        var motivo =
            Dato(datos, "Motivo");

        if (!string.IsNullOrWhiteSpace(motivo))
            partes.Add($"Motivo: {Recortar(motivo, 120)}");

        var observaciones =
            Dato(datos, "Observaciones");

        if (!string.IsNullOrWhiteSpace(observaciones))
            partes.Add($"Obs.: {Recortar(observaciones, 120)}");

        return string.Join(". ", partes);
    }

    private static string ConstruirCodigoEvento(
        string area,
        string action)
    {
        var areaCode =
            NormalizarCodigo(area);

        var actionCode =
            NormalizarCodigo(action);

        if (string.IsNullOrWhiteSpace(areaCode))
            areaCode = "ERP";

        if (string.IsNullOrWhiteSpace(actionCode))
            actionCode = "ACTUALIZACION";

        return Recortar(
            $"{areaCode}_{actionCode}",
            80);
    }

    private static string NormalizarCodigo(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var texto =
            value.Trim()
                .ToUpperInvariant()
                .Replace("Á", "A")
                .Replace("É", "E")
                .Replace("Í", "I")
                .Replace("Ó", "O")
                .Replace("Ú", "U")
                .Replace("Ü", "U")
                .Replace("Ñ", "N");

        var sb =
            new StringBuilder(
                texto.Length);

        var anteriorGuion =
            false;

        foreach (var c in texto)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(c);
                anteriorGuion = false;
            }
            else if (!anteriorGuion)
            {
                sb.Append('_');
                anteriorGuion = true;
            }
        }

        return sb
            .ToString()
            .Trim('_');
    }

    private static string? NormalizarRutaLocal(
        string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        var value =
            url.Trim();

        if (!value.StartsWith(
                "/",
                StringComparison.Ordinal)
            || value.StartsWith(
                "//",
                StringComparison.Ordinal))
        {
            return null;
        }

        return Recortar(
            value,
            500);
    }

    private static string Humanizar(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "actualización";

        var s =
            value.Trim()
                .Replace("_", " ");

        var chars =
            new List<char>(
                s.Length + 8);

        for (var i = 0; i < s.Length; i++)
        {
            if (i > 0
                && char.IsUpper(s[i])
                && !char.IsUpper(s[i - 1])
                && s[i - 1] != ' ')
            {
                chars.Add(' ');
            }

            chars.Add(s[i]);
        }

        return new string(
            chars.ToArray());
    }

    private static string Dato(
        IReadOnlyDictionary<string, string?> datos,
        string key)
    {
        return datos.TryGetValue(
                key,
                out var value)
            ? value?.Trim() ?? string.Empty
            : string.Empty;
    }

    private static bool TryDatoBool(
        IReadOnlyDictionary<string, string?> datos,
        string key,
        out bool value)
    {
        value = false;

        var raw =
            Dato(
                datos,
                key);

        return bool.TryParse(
            raw,
            out value);
    }

    private static bool TryDatoInt(
        IReadOnlyDictionary<string, string?> datos,
        string key,
        out int value)
    {
        value = 0;

        var raw =
            Dato(
                datos,
                key);

        return int.TryParse(
            raw,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out value)
            && value > 0;
    }

    private static int IdNotificacion(
        long idLargo,
        int fallback)
    {
        if (idLargo > 0
            && idLargo <= int.MaxValue)
        {
            return Convert.ToInt32(
                idLargo);
        }

        return Math.Max(
            1,
            fallback);
    }

    private static string FormatoCantidad(
        decimal value)
    {
        return value.ToString(
            "0.###",
            CultureInfo.InvariantCulture);
    }

    private static string FormatoFecha(
        DateTime? value)
    {
        return value.HasValue
            ? value.Value.ToString(
                "dd/MM/yyyy HH:mm",
                CultureInfo.InvariantCulture)
            : "Sin fecha";
    }

    private static string Texto(
        string? value,
        string fallback)
    {
        return string.IsNullOrWhiteSpace(value)
            ? fallback
            : value.Trim();
    }

    private static string Recortar(
        string value,
        int max)
    {
        value ??= string.Empty;

        return value.Length <= max
            ? value
            : value[..max];
    }

    private static string ValorTexto(
        SqlDataReader reader,
        string columna)
    {
        var ordinal =
            reader.GetOrdinal(
                columna);

        return reader.IsDBNull(ordinal)
            ? string.Empty
            : reader.GetValue(ordinal)
                ?.ToString()
                ?.Trim()
              ?? string.Empty;
    }

    private static decimal ValorDecimal(
        SqlDataReader reader,
        string columna)
    {
        var ordinal =
            reader.GetOrdinal(
                columna);

        return reader.IsDBNull(ordinal)
            ? 0m
            : Convert.ToDecimal(
                reader.GetValue(ordinal),
                CultureInfo.InvariantCulture);
    }

    private static int? ValorNullableInt(
        SqlDataReader reader,
        string columna)
    {
        var ordinal =
            reader.GetOrdinal(
                columna);

        return reader.IsDBNull(ordinal)
            ? null
            : Convert.ToInt32(
                reader.GetValue(ordinal),
                CultureInfo.InvariantCulture);
    }

    private static DateTime? ValorFecha(
        SqlDataReader reader,
        string columna)
    {
        var ordinal =
            reader.GetOrdinal(
                columna);

        return reader.IsDBNull(ordinal)
            ? null
            : Convert.ToDateTime(
                reader.GetValue(ordinal),
                CultureInfo.InvariantCulture);
    }
}