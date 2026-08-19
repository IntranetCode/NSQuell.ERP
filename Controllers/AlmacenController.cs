using ERP.NSQuell.Models.ViewModels.Almacen;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;

namespace ERP.NSQuell.Controllers;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AlmacenController : AlmacenBaseController
{
    public AlmacenController(IConfiguration configuration) : base(configuration) { }

    [HttpGet]
    public IActionResult Index()
    {
        var sesion = ValidarSesion();
        if (sesion != null) return sesion;

        // /Menu/Grupo/1 es el menú real de Almacén.
        // Esta ruta se conserva solamente como redirección para enlaces antiguos.
        return RedirectToAction("Grupo", "Menu", new { id = 1 });
    }


    [HttpGet]
    public async Task<IActionResult> Scrap(
        string? q = null,
        string? estado = null,
        CancellationToken cancellationToken = default)
    {
        var sesion = ValidarSesion();
        if (sesion != null) return sesion;

        var vm = new AlmacenCalidadScrapBandejaVm
        {
            Busqueda = string.IsNullOrWhiteSpace(q) ? null : q.Trim(),
            Estado = NormalizarEstadoScrap(estado)
        };

        await using var connection = await AbrirConexionAsync(cancellationToken);

        if (!await ModuloCalidadScrapConfiguradoAsync(connection, cancellationToken))
        {
            vm.Configurado = false;
            vm.MensajeConfiguracion =
                "No existe dbo.Calidad_ScrapEntregas con la estructura esperada. Calidad debe originar las entregas antes de usar esta bandeja.";
            return View(vm);
        }

        const string resumenSql = @"
SELECT
    COUNT(*) AS Total,
    SUM(CASE WHEN Estado=N'PENDIENTE_RECEPCION' THEN 1 ELSE 0 END) AS PendientesRecepcion,
    SUM(CASE WHEN Estado IN(N'RECIBIDO_ALMACEN',N'PENDIENTE_MOLIENDA') THEN 1 ELSE 0 END) AS RecibidosAlmacen,
    SUM(CASE WHEN Estado=N'MOLIDO' THEN 1 ELSE 0 END) AS Molidos,
    SUM(CASE WHEN Estado=N'PENDIENTE_RECEPCION' THEN CantidadScrap ELSE 0 END) AS PiezasPendientes,
    SUM(CASE WHEN Estado=N'MOLIDO' THEN ISNULL(CantidadMolida,0) ELSE 0 END) AS KgMolidos
FROM dbo.Calidad_ScrapEntregas
WHERE Activo=1
  AND Estado<>N'CANCELADO';";

        await using (var resumen = new SqlCommand(resumenSql, connection))
        await using (var rd = await resumen.ExecuteReaderAsync(cancellationToken))
        {
            if (await rd.ReadAsync(cancellationToken))
            {
                vm.Total = Entero(rd, "Total");
                vm.PendientesRecepcion = Entero(rd, "PendientesRecepcion");
                vm.RecibidosAlmacen = Entero(rd, "RecibidosAlmacen");
                vm.Molidos = Entero(rd, "Molidos");
                vm.PiezasPendientes = Entero(rd, "PiezasPendientes");
                vm.KgMolidos = DecimalValor(rd, "KgMolidos");
            }
        }

        const string sql = @"
SELECT TOP (500)
    s.ScrapEntregaID,
    s.InspeccionID,
    s.DisposicionID,
    s.EjecucionProduccionID,
    s.SolicitudProduccionID,
    s.SolicitudProduccionDetalleID,
    s.ParteID,
    ISNULL(s.NumeroParte,N'') AS NumeroParte,
    ISNULL(s.OrdenFabricacion,N'') AS OrdenFabricacion,
    s.CantidadScrap,
    s.Estado,
    ISNULL(s.Origen,N'CALIDAD') AS Origen,
    s.GP12SolicitudID,
    s.GP12InspeccionID,
    s.FechaCreacion,
    s.FechaEntrega,
    s.FechaRecepcion,
    ISNULL(s.UbicacionScrap,N'') AS UbicacionScrap,
    s.FechaMolienda,
    s.CantidadMolida,
    ISNULL(s.Observaciones,N'') AS Observaciones,
    COALESCE(NULLIF(LTRIM(RTRIM(CONCAT(pe.Nombre,N' ',pe.ApellidoPaterno,N' ',pe.ApellidoMaterno))),N''),ue.Username,N'') AS EntregadoPor,
    COALESCE(NULLIF(LTRIM(RTRIM(CONCAT(pr.Nombre,N' ',pr.ApellidoPaterno,N' ',pr.ApellidoMaterno))),N''),ur.Username,N'') AS RecibidoPor,
    COALESCE(NULLIF(LTRIM(RTRIM(CONCAT(pm.Nombre,N' ',pm.ApellidoPaterno,N' ',pm.ApellidoMaterno))),N''),um.Username,N'') AS MolidoPor
FROM dbo.Calidad_ScrapEntregas s
LEFT JOIN dbo.Usuarios ue ON ue.UsuarioID=COALESCE(s.UsuarioEntregaID,s.UsuarioCreacionID)
LEFT JOIN dbo.Persona pe ON pe.PersonaID=ue.PersonaID
LEFT JOIN dbo.Usuarios ur ON ur.UsuarioID=s.UsuarioRecepcionID
LEFT JOIN dbo.Persona pr ON pr.PersonaID=ur.PersonaID
LEFT JOIN dbo.Usuarios um ON um.UsuarioID=s.UsuarioMoliendaID
LEFT JOIN dbo.Persona pm ON pm.PersonaID=um.PersonaID
WHERE s.Activo=1
  AND s.Estado<>N'CANCELADO'
  AND (@Estado IS NULL OR s.Estado=@Estado)
  AND
  (
      @Busqueda IS NULL
      OR CONVERT(NVARCHAR(30),s.ScrapEntregaID) LIKE N'%' + @Busqueda + N'%'
      OR ISNULL(s.OrdenFabricacion,N'') LIKE N'%' + @Busqueda + N'%'
      OR ISNULL(s.NumeroParte,N'') LIKE N'%' + @Busqueda + N'%'
      OR ISNULL(s.UbicacionScrap,N'') LIKE N'%' + @Busqueda + N'%'
      OR ISNULL(s.Observaciones,N'') LIKE N'%' + @Busqueda + N'%'
  )
ORDER BY
    CASE s.Estado
        WHEN N'PENDIENTE_RECEPCION' THEN 1
        WHEN N'RECIBIDO_ALMACEN' THEN 2
        WHEN N'PENDIENTE_MOLIENDA' THEN 3
        WHEN N'MOLIDO' THEN 4
        ELSE 5
    END,
    s.FechaCreacion DESC,
    s.ScrapEntregaID DESC;";

        await using (var command = new SqlCommand(sql, connection))
        {
            command.Parameters.Add("@Busqueda", SqlDbType.NVarChar, 250).Value =
                string.IsNullOrWhiteSpace(vm.Busqueda) ? DBNull.Value : vm.Busqueda;
            command.Parameters.Add("@Estado", SqlDbType.NVarChar, 30).Value =
                string.IsNullOrWhiteSpace(vm.Estado) ? DBNull.Value : vm.Estado;

            await using var rd = await command.ExecuteReaderAsync(cancellationToken);
            while (await rd.ReadAsync(cancellationToken))
            {
                vm.Registros.Add(new AlmacenCalidadScrapItemVm
                {
                    ScrapEntregaID = EnteroLargo(rd, "ScrapEntregaID"),
                    InspeccionID = Entero(rd, "InspeccionID"),
                    DisposicionID = NullableIntScrap(rd, "DisposicionID"),
                    EjecucionProduccionID = NullableIntScrap(rd, "EjecucionProduccionID"),
                    SolicitudProduccionID = NullableIntScrap(rd, "SolicitudProduccionID"),
                    SolicitudProduccionDetalleID = NullableIntScrap(rd, "SolicitudProduccionDetalleID"),
                    ParteID = NullableIntScrap(rd, "ParteID"),
                    NumeroParte = Texto(rd, "NumeroParte"),
                    OrdenFabricacion = Texto(rd, "OrdenFabricacion"),
                    CantidadScrap = Entero(rd, "CantidadScrap"),
                    Estado = Texto(rd, "Estado"),
                    Origen = Texto(rd, "Origen"),
                    GP12SolicitudID = NullableIntScrap(rd, "GP12SolicitudID"),
                    GP12InspeccionID = NullableIntScrap(rd, "GP12InspeccionID"),
                    FechaCreacion = Fecha(rd, "FechaCreacion") ?? DateTime.MinValue,
                    FechaEntrega = Fecha(rd, "FechaEntrega"),
                    FechaRecepcion = Fecha(rd, "FechaRecepcion"),
                    UbicacionScrap = Texto(rd, "UbicacionScrap"),
                    FechaMolienda = Fecha(rd, "FechaMolienda"),
                    CantidadMolida = NullableDecimalScrap(rd, "CantidadMolida"),
                    Observaciones = Texto(rd, "Observaciones"),
                    EntregadoPor = Texto(rd, "EntregadoPor"),
                    RecibidoPor = Texto(rd, "RecibidoPor"),
                    MolidoPor = Texto(rd, "MolidoPor")
                });
            }
        }

        vm.UbicacionesScrap = await CargarUbicacionesPorAlmacenAsync(connection, "SCRAP", cancellationToken);
        vm.UbicacionesMP = await CargarUbicacionesPorAlmacenAsync(connection, "MP", cancellationToken);
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RecibirScrap(
        AlmacenRecibirScrapPostVm model,
        CancellationToken cancellationToken = default)
    {
        var sesion = ValidarSesion();
        if (sesion != null) return sesion;

        if (!ModelState.IsValid
            || model.ScrapEntregaID <= 0
            || !model.UbicacionScrapID.HasValue
            || model.UbicacionScrapID.Value <= 0)
        {
            Mensaje("danger", "Selecciona una ubicación activa del almacén SCRAP para confirmar la recepción.");
            return RedirectToAction(nameof(Scrap));
        }

        await using var connection = await AbrirConexionAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            const string sql = @"
DECLARE
    @Estado NVARCHAR(30),
    @UbicacionScrap NVARCHAR(150);

SELECT @UbicacionScrap = LEFT(
    CONCAT(
        LTRIM(RTRIM(Almacen)), N' · ',
        LTRIM(RTRIM(Rack)),
        CASE WHEN NULLIF(LTRIM(RTRIM(ISNULL(Nivel,N''))),N'') IS NULL THEN N'' ELSE N' · ' + LTRIM(RTRIM(Nivel)) END,
        CASE WHEN NULLIF(LTRIM(RTRIM(ISNULL(Posicion,N''))),N'') IS NULL THEN N'' ELSE N' · ' + LTRIM(RTRIM(Posicion)) END
    ),
    150)
FROM dbo.ERP_Ubicaciones WITH(UPDLOCK,HOLDLOCK)
WHERE UbicacionID=@UbicacionScrapID
  AND Activo=1
  AND UPPER(LTRIM(RTRIM(Almacen)))=N'SCRAP';

IF @UbicacionScrap IS NULL
    THROW 54600,N'La ubicación seleccionada no pertenece al almacén SCRAP o está inactiva.',1;

SELECT @Estado=Estado
FROM dbo.Calidad_ScrapEntregas WITH(UPDLOCK,HOLDLOCK)
WHERE ScrapEntregaID=@ScrapEntregaID
  AND Activo=1;

IF @Estado IS NULL
    THROW 54601,N'No existe la entrega de Scrap indicada.',1;

IF @Estado<>N'PENDIENTE_RECEPCION'
    THROW 54602,N'La entrega ya no está pendiente de recepción.',1;

UPDATE dbo.Calidad_ScrapEntregas
SET
    UsuarioRecepcionID=@UsuarioID,
    FechaRecepcion=SYSDATETIME(),
    UbicacionScrap=@UbicacionScrap,
    Estado=N'RECIBIDO_ALMACEN',
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=SYSDATETIME()
WHERE ScrapEntregaID=@ScrapEntregaID
  AND Activo=1
  AND Estado=N'PENDIENTE_RECEPCION';

IF @@ROWCOUNT<>1
    THROW 54603,N'No fue posible confirmar la recepción física del Scrap.',1;

SELECT @UbicacionScrap;";

            await using var command = new SqlCommand(sql, connection, transaction);
            command.Parameters.Add("@ScrapEntregaID", SqlDbType.BigInt).Value = model.ScrapEntregaID;
            command.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = UsuarioID!.Value;
            command.Parameters.Add("@UbicacionScrapID", SqlDbType.Int).Value = model.UbicacionScrapID!.Value;
            var ubicacionTexto = Convert.ToString(await command.ExecuteScalarAsync(cancellationToken)) ?? "SCRAP";

            await transaction.CommitAsync(cancellationToken);
            Mensaje("success", $"Scrap #{model.ScrapEntregaID} recibido físicamente en {ubicacionTexto}.");
        }
        catch (SqlException ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            Mensaje("danger", ex.Message);
        }

        return RedirectToAction(nameof(Scrap));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RegistrarMoliendaScrap(
        AlmacenMoliendaScrapPostVm model,
        CancellationToken cancellationToken = default)
    {
        var sesion = ValidarSesion();
        if (sesion != null) return sesion;

        if (!ModelState.IsValid || model.ScrapEntregaID <= 0 || !model.UbicacionMPID.HasValue)
        {
            Mensaje("danger", "Revisa el peso molido y la ubicación MP antes de registrar la molienda.");
            return RedirectToAction(nameof(Scrap), new { estado = "RECIBIDO_ALMACEN" });
        }

        await using var connection = await AbrirConexionAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            const string sql = @"
IF NOT EXISTS
(
    SELECT 1
    FROM dbo.ERP_Ubicaciones
    WHERE UbicacionID=@UbicacionID
      AND Activo=1
      AND UPPER(LTRIM(RTRIM(Almacen)))=N'MP'
)
    THROW 54610,N'La ubicación seleccionada no pertenece a MP.',1;

DECLARE
    @Estado NVARCHAR(30),
    @ParteID INT,
    @NumeroParte NVARCHAR(150),
    @NumeroOF NVARCHAR(120),
    @SolicitudProduccionID INT,
    @SolicitudProduccionDetalleID INT,
    @Origen NVARCHAR(20),
    @MaterialID INT,
    @Materiales INT,
    @MovimientoID INT;

SELECT
    @Estado=Estado,
    @ParteID=ParteID,
    @NumeroParte=NumeroParte,
    @NumeroOF=OrdenFabricacion,
    @SolicitudProduccionID=SolicitudProduccionID,
    @SolicitudProduccionDetalleID=SolicitudProduccionDetalleID,
    @Origen=ISNULL(Origen,N'CALIDAD')
FROM dbo.Calidad_ScrapEntregas WITH(UPDLOCK,HOLDLOCK)
WHERE ScrapEntregaID=@ScrapEntregaID
  AND Activo=1;

IF @Estado IS NULL
    THROW 54611,N'No existe la entrega de Scrap.',1;

IF @Estado NOT IN(N'RECIBIDO_ALMACEN',N'PENDIENTE_MOLIENDA')
    THROW 54612,N'El Scrap debe estar recibido en Almacén antes de registrar molienda.',1;

IF @ParteID IS NULL AND NULLIF(LTRIM(RTRIM(ISNULL(@NumeroParte,N''))),N'') IS NOT NULL
BEGIN
    ;WITH P AS
    (
        SELECT p.ParteID
        FROM dbo.ERP_Partes p
        WHERE p.Activo=1
          AND UPPER(REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(p.NumeroParte,N''))),N'.',N''),N'-',N''),N'_',N''),N' ',N''))=
              UPPER(REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(@NumeroParte)),N'.',N''),N'-',N''),N'_',N''),N' ',N''))
    )
    SELECT @ParteID=CASE WHEN COUNT(*)=1 THEN MAX(ParteID) END FROM P;
END;

IF @ParteID IS NULL
    THROW 54613,N'No fue posible resolver una ParteID única para el Scrap.',1;

SELECT
    @Materiales=COUNT(DISTINCT d.MaterialID),
    @MaterialID=CASE WHEN COUNT(DISTINCT d.MaterialID)=1 THEN MAX(d.MaterialID) END
FROM dbo.ERP_ParteDatosTecnicos d
INNER JOIN dbo.ERP_Materiales m
    ON m.MaterialID=d.MaterialID
   AND m.Activo=1
WHERE d.ParteID=@ParteID
  AND d.Activo=1
  AND d.MaterialID IS NOT NULL;

IF ISNULL(@Materiales,0)<>1 OR @MaterialID IS NULL
    THROW 54614,N'La parte no tiene un material activo y único para generar MP Molido.',1;

DECLARE @Referencia NVARCHAR(120)=CONCAT(N'CALIDAD-SCRAP-MOLIDO:',@ScrapEntregaID);
DECLARE @Lote NVARCHAR(120)=LEFT(CONCAT(N'SCRAP-',UPPER(ISNULL(@Origen,N'CALIDAD')),N'-',@ScrapEntregaID),120);
DECLARE @Seguimiento NVARCHAR(800)=LEFT(CONCAT(
    N'MP Molido desde Calidad_ScrapEntregas #',@ScrapEntregaID,
    N' | Parte=',ISNULL(@NumeroParte,N''),
    N' | OF=',ISNULL(@NumeroOF,N''),
    CASE WHEN NULLIF(LTRIM(RTRIM(ISNULL(@Observaciones,N''))),N'') IS NULL THEN N'' ELSE CONCAT(N' | ',LTRIM(RTRIM(@Observaciones))) END
),800);

IF EXISTS
(
    SELECT 1
    FROM dbo.AlmacenMP_Movimientos WITH(UPDLOCK,HOLDLOCK)
    WHERE Activo=1
      AND ReferenciaOperacion=@Referencia
)
    THROW 54615,N'Esta entrega de Scrap ya generó una entrada de MP Molido.',1;

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
    SolicitudProduccionID,
    SolicitudProduccionDetalleID
)
VALUES
(
    SYSDATETIME(),
    @MaterialID,
    NULL,
    N'Entrada',
    N'M',
    @Lote,
    @CantidadMolida,
    N'KG',
    @UbicacionID,
    @NumeroOF,
    NULL,
    @UsuarioID,
    @UsuarioNombre,
    @Seguimiento,
    SYSUTCDATETIME(),
    @UsuarioNombre,
    1,
    0,
    1,
    @Referencia,
    @SolicitudProduccionID,
    @SolicitudProduccionDetalleID
);

SET @MovimientoID=CONVERT(INT,SCOPE_IDENTITY());

UPDATE dbo.Calidad_ScrapEntregas
SET
    ParteID=COALESCE(ParteID,@ParteID),
    UsuarioMoliendaID=@UsuarioID,
    FechaMolienda=SYSDATETIME(),
    CantidadMolida=@CantidadMolida,
    Estado=N'MOLIDO',
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=SYSDATETIME()
WHERE ScrapEntregaID=@ScrapEntregaID
  AND Activo=1
  AND Estado IN(N'RECIBIDO_ALMACEN',N'PENDIENTE_MOLIENDA');

IF @@ROWCOUNT<>1
    THROW 54616,N'No fue posible cerrar la entrega como MOLIDO.',1;

SELECT @MovimientoID;";

            await using var command = new SqlCommand(sql, connection, transaction);
            command.Parameters.Add("@ScrapEntregaID", SqlDbType.BigInt).Value = model.ScrapEntregaID;
            var cantidad = command.Parameters.Add("@CantidadMolida", SqlDbType.Decimal);
            cantidad.Precision = 18;
            cantidad.Scale = 4;
            cantidad.Value = model.CantidadMolida;
            command.Parameters.Add("@UbicacionID", SqlDbType.Int).Value = model.UbicacionMPID.Value;
            command.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = UsuarioID!.Value;
            command.Parameters.Add("@UsuarioNombre", SqlDbType.NVarChar, 180).Value = UsuarioNombre;
            command.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 500).Value =
                string.IsNullOrWhiteSpace(model.Observaciones) ? DBNull.Value : model.Observaciones.Trim();

            var movimiento = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
            await transaction.CommitAsync(cancellationToken);

            Mensaje(
                "success",
                $"Molienda registrada. Scrap #{model.ScrapEntregaID} generó Entrada MP Molido #{movimiento} por {model.CantidadMolida:N3} KG.");
        }
        catch (SqlException ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            Mensaje("danger", ex.Message);
        }

        return RedirectToAction(nameof(Scrap));
    }

    private static string? NormalizarEstadoScrap(string? estado)
    {
        if (string.IsNullOrWhiteSpace(estado)) return null;
        var value = estado.Trim().ToUpperInvariant();
        return value is "PENDIENTE_RECEPCION" or "RECIBIDO_ALMACEN" or "PENDIENTE_MOLIENDA" or "MOLIDO"
            ? value
            : null;
    }

    private static int? NullableIntScrap(SqlDataReader reader, string columna)
    {
        var ordinal = reader.GetOrdinal(columna);
        return reader.IsDBNull(ordinal) ? null : Convert.ToInt32(reader.GetValue(ordinal));
    }

    private static decimal? NullableDecimalScrap(SqlDataReader reader, string columna)
    {
        var ordinal = reader.GetOrdinal(columna);
        return reader.IsDBNull(ordinal) ? null : Convert.ToDecimal(reader.GetValue(ordinal));
    }

    private static async Task<bool> ModuloCalidadScrapConfiguradoAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        if (!await ExisteObjetoAsync(connection, "dbo.Calidad_ScrapEntregas", "U", cancellationToken))
            return false;

        var columnas = new[]
        {
            "ScrapEntregaID", "CantidadScrap", "Estado", "UsuarioRecepcionID",
            "FechaRecepcion", "UbicacionScrap", "UsuarioMoliendaID", "FechaMolienda",
            "CantidadMolida", "Activo"
        };

        foreach (var columna in columnas)
        {
            if (!await ExisteColumnaAsync(connection, "dbo.Calidad_ScrapEntregas", columna, cancellationToken))
                return false;
        }

        return true;
    }

    private static async Task<List<AlmacenSelectVm>> CargarUbicacionesPorAlmacenAsync(
        SqlConnection connection,
        string almacen,
        CancellationToken cancellationToken)
    {
        var lista = new List<AlmacenSelectVm>();

        if (!await ExisteObjetoAsync(connection, "dbo.ERP_Ubicaciones", "U", cancellationToken))
            return lista;

        const string sql = @"
SELECT UbicacionID,Almacen,Rack,ISNULL(Nivel,N'') AS Nivel,ISNULL(Posicion,N'') AS Posicion
FROM dbo.ERP_Ubicaciones
WHERE Activo=1
  AND UPPER(LTRIM(RTRIM(Almacen)))=@Almacen
ORDER BY Rack,Nivel,Posicion,UbicacionID;";

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Almacen", SqlDbType.NVarChar, 60).Value = almacen.Trim().ToUpperInvariant();
        await using var rd = await command.ExecuteReaderAsync(cancellationToken);
        while (await rd.ReadAsync(cancellationToken))
        {
            var texto = string.Join(
                " · ",
                new[] { Texto(rd, "Almacen"), Texto(rd, "Rack"), Texto(rd, "Nivel"), Texto(rd, "Posicion") }
                    .Where(x => !string.IsNullOrWhiteSpace(x)));

            lista.Add(new AlmacenSelectVm
            {
                Id = Entero(rd, "UbicacionID"),
                Texto = texto
            });
        }

        return lista;
    }

}
