using ERP.NSQuell.Models.ViewModels.Logistica;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;

namespace ERP.NSQuell.Controllers;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class LogisticaCatalogosController : Controller
{
    private readonly IConfiguration _configuration;

    public LogisticaCatalogosController(
        IConfiguration configuration)
    {
        _configuration = configuration;
    }
    private string ConnectionString =>
        _configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException(
            "No se encontró ConnectionStrings:DefaultConnection.");

    private int? UsuarioID =>
        HttpContext.Session.GetInt32("UsuarioID");

    private string UsuarioNombre =>
        HttpContext.Session.GetString("NombreMostrar")
        ?? HttpContext.Session.GetString("Username")
        ?? User?.Identity?.Name
        ?? "Usuario";

    private bool UsuarioEnSesion()
    {
        return UsuarioID.HasValue &&
               UsuarioID.Value > 0;
    }

    private async Task<SqlConnection> AbrirAsync(
        CancellationToken cancellationToken)
    {
        var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync(cancellationToken);
        return cn;
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        CancellationToken cancellationToken)
    {

        if (!UsuarioEnSesion())
            return RedirectToAction("Login", "Login");


        await using var cn =
            await AbrirAsync(cancellationToken);

        var vm = new LogisticaCatalogosVm();

        const string sql = @"
SELECT
    RutaID,
    ISNULL(Codigo,N'') AS Codigo,
    ISNULL(Nombre,N'') AS Nombre,
    ISNULL(Descripcion,N'') AS Descripcion,
    ISNULL(Activo,0) AS Activo
FROM dbo.Logistica_Rutas
ORDER BY
    Activo DESC,
    Codigo,
    Nombre;

SELECT
    UnidadID,
    ISNULL(NumeroEconomico,N'') AS NumeroEconomico,
    ISNULL(Placas,N'') AS Placas,
    ISNULL(Marca,N'') AS Marca,
    ISNULL(Modelo,N'') AS Modelo,
    CapacidadPesoKg,
    ISNULL(Activo,0) AS Activo
FROM dbo.Logistica_Unidades
ORDER BY
    Activo DESC,
    NumeroEconomico;";

        await using var cmd =
            new SqlCommand(sql, cn);

        await using var rd =
            await cmd.ExecuteReaderAsync(cancellationToken);

        while (await rd.ReadAsync(cancellationToken))
        {
            vm.Rutas.Add(
                new LogisticaRutaVm
                {
                    RutaID = Entero(rd, "RutaID"),
                    Codigo = Texto(rd, "Codigo"),
                    Nombre = Texto(rd, "Nombre"),
                    Descripcion = Texto(rd, "Descripcion"),
                    Activo = Booleano(rd, "Activo")
                });
        }

        if (await rd.NextResultAsync(cancellationToken))
        {
            while (await rd.ReadAsync(cancellationToken))
            {
                vm.Unidades.Add(
                    new LogisticaUnidadVm
                    {
                        UnidadID = Entero(rd, "UnidadID"),
                        NumeroEconomico =
                            Texto(rd, "NumeroEconomico"),
                        Placas = Texto(rd, "Placas"),
                        Marca = Texto(rd, "Marca"),
                        Modelo = Texto(rd, "Modelo"),
                        CapacidadPesoKg = DecimalNullable(rd, "CapacidadPesoKg"),
                        Activo = Booleano(rd, "Activo")
                    });
            }
        }

        return View(vm);
    }

    // Compatibilidad temporal con enlaces anteriores:
    // /LogisticaCatalogos/Catalogos
    [HttpGet]
    public IActionResult Catalogos()
    {
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GuardarRuta(
        LogisticaRutaVm model,
        CancellationToken cancellationToken)
    {
        if (!UsuarioEnSesion())
            return RedirectToAction("Login", "Login");

        model.Codigo =
            model.Codigo?.Trim() ?? string.Empty;

        model.Nombre =
            model.Nombre?.Trim() ?? string.Empty;

        model.Descripcion =
            model.Descripcion?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(model.Codigo))
        {
            TempData["LogisticaError"] =
                "El código de la ruta es obligatorio.";

            return RedirectToAction(nameof(Index));
        }

        if (string.IsNullOrWhiteSpace(model.Nombre))
        {
            TempData["LogisticaError"] =
                "El nombre de la ruta es obligatorio.";

            return RedirectToAction(nameof(Index));
        }

        if (model.Codigo.Length > 30)
        {
            TempData["LogisticaError"] =
                "El código de la ruta no puede exceder 30 caracteres.";

            return RedirectToAction(nameof(Index));
        }

        if (model.Nombre.Length > 150)
        {
            TempData["LogisticaError"] =
                "El nombre de la ruta no puede exceder 150 caracteres.";

            return RedirectToAction(nameof(Index));
        }

        if (model.Descripcion.Length > 500)
        {
            TempData["LogisticaError"] =
                "La descripción no puede exceder 500 caracteres.";

            return RedirectToAction(nameof(Index));
        }

        await using var cn =
            await AbrirAsync(cancellationToken);

        await using var tx =
            (SqlTransaction)await cn.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

        try
        {
            const string sqlDuplicado = @"
SELECT COUNT_BIG(*)
FROM dbo.Logistica_Rutas WITH (UPDLOCK,HOLDLOCK)
WHERE
    Activo=1
    AND
    (
        UPPER(LTRIM(RTRIM(Codigo))) =
            UPPER(LTRIM(RTRIM(@Codigo)))
        OR
        UPPER(LTRIM(RTRIM(Nombre))) =
            UPPER(LTRIM(RTRIM(@Nombre)))
    )
    AND (@RutaID<=0 OR RutaID<>@RutaID);";

            await using (var cmd =
                new SqlCommand(
                    sqlDuplicado,
                    cn,
                    tx))
            {
                cmd.Parameters.Add(
                    "@Codigo",
                    SqlDbType.NVarChar,
                    30).Value = model.Codigo;

                cmd.Parameters.Add(
                    "@Nombre",
                    SqlDbType.NVarChar,
                    150).Value = model.Nombre;

                cmd.Parameters.Add(
                    "@RutaID",
                    SqlDbType.Int).Value =
                    model.RutaID;

                var duplicados =
                    Convert.ToInt64(
                        await cmd.ExecuteScalarAsync(
                            cancellationToken));

                if (duplicados > 0)
                {
                    throw new InvalidOperationException(
                        "Ya existe una ruta activa con el mismo código o nombre.");
                }
            }

            if (model.RutaID <= 0)
            {
                const string sqlInsert = @"
INSERT dbo.Logistica_Rutas
(
    Codigo,
    Nombre,
    Descripcion,
    Activo,
    FechaCreacion,
    CreadoPor
)
VALUES
(
    @Codigo,
    @Nombre,
    @Descripcion,
    1,
    SYSDATETIME(),
    @Usuario
);

SELECT CONVERT(int,SCOPE_IDENTITY());";

                int rutaId;

                await using (var cmd =
                    new SqlCommand(
                        sqlInsert,
                        cn,
                        tx))
                {
                    cmd.Parameters.Add(
                        "@Codigo",
                        SqlDbType.NVarChar,
                        30).Value = model.Codigo;

                    cmd.Parameters.Add(
                        "@Nombre",
                        SqlDbType.NVarChar,
                        150).Value = model.Nombre;

                    cmd.Parameters.Add(
                        "@Descripcion",
                        SqlDbType.NVarChar,
                        500).Value =
                        Db(
                            string.IsNullOrWhiteSpace(
                                model.Descripcion)
                                ? null
                                : model.Descripcion);

                    cmd.Parameters.Add(
                        "@Usuario",
                        SqlDbType.NVarChar,
                        120).Value = UsuarioNombre;

                    rutaId =
                        Convert.ToInt32(
                            await cmd.ExecuteScalarAsync(
                                cancellationToken));
                }

                await tx.CommitAsync(cancellationToken);

                TempData["LogisticaOk"] =
                    $"Ruta {model.Codigo} creada correctamente.";

                return RedirectToAction(nameof(Index));
            }

            const string sqlExiste = @"
SELECT COUNT_BIG(*)
FROM dbo.Logistica_Rutas WITH (UPDLOCK,HOLDLOCK)
WHERE RutaID=@RutaID;";

            await using (var cmd =
                new SqlCommand(
                    sqlExiste,
                    cn,
                    tx))
            {
                cmd.Parameters.Add(
                    "@RutaID",
                    SqlDbType.Int).Value =
                    model.RutaID;

                var existe =
                    Convert.ToInt64(
                        await cmd.ExecuteScalarAsync(
                            cancellationToken));

                if (existe <= 0)
                {
                    throw new InvalidOperationException(
                        "La ruta que intentas editar ya no existe.");
                }
            }

            const string sqlUpdate = @"
UPDATE dbo.Logistica_Rutas
SET
    Codigo=@Codigo,
    Nombre=@Nombre,
    Descripcion=@Descripcion
WHERE RutaID=@RutaID;

SELECT @@ROWCOUNT;";

            await using (var cmd =
                new SqlCommand(
                    sqlUpdate,
                    cn,
                    tx))
            {
                cmd.Parameters.Add(
                    "@Codigo",
                    SqlDbType.NVarChar,
                    30).Value = model.Codigo;

                cmd.Parameters.Add(
                    "@Nombre",
                    SqlDbType.NVarChar,
                    150).Value = model.Nombre;

                cmd.Parameters.Add(
                    "@Descripcion",
                    SqlDbType.NVarChar,
                    500).Value =
                    Db(
                        string.IsNullOrWhiteSpace(
                            model.Descripcion)
                            ? null
                            : model.Descripcion);

                cmd.Parameters.Add(
                    "@RutaID",
                    SqlDbType.Int).Value =
                    model.RutaID;

                var afectados =
                    Convert.ToInt32(
                        await cmd.ExecuteScalarAsync(
                            cancellationToken));

                if (afectados == 0)
                {
                    throw new InvalidOperationException(
                        "No fue posible actualizar la ruta.");
                }
            }

            await tx.CommitAsync(cancellationToken);

            TempData["LogisticaOk"] =
                $"Ruta {model.Codigo} actualizada correctamente.";
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);

            TempData["LogisticaError"] =
                $"No fue posible guardar la ruta: {ex.Message}";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CambiarEstadoRuta(
        int rutaId,
        bool activo,
        CancellationToken cancellationToken)
    {
        if (!UsuarioEnSesion())
            return RedirectToAction("Login", "Login");

        if (rutaId <= 0)
        {
            TempData["LogisticaError"] =
                "La ruta indicada no es válida.";

            return RedirectToAction(nameof(Index));
        }

        await using var cn =
            await AbrirAsync(cancellationToken);

        await using var tx =
            (SqlTransaction)await cn.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

        try
        {
            const string sqlActual = @"
SELECT
    ISNULL(Codigo,N'') AS Codigo,
    ISNULL(Nombre,N'') AS Nombre,
    ISNULL(Activo,0) AS Activo
FROM dbo.Logistica_Rutas WITH (UPDLOCK,HOLDLOCK)
WHERE RutaID=@RutaID;";

            string codigo;
            string nombre;
            bool estadoActual;

            await using (var cmd =
                new SqlCommand(
                    sqlActual,
                    cn,
                    tx))
            {
                cmd.Parameters.Add(
                    "@RutaID",
                    SqlDbType.Int).Value =
                    rutaId;

                await using var rd =
                    await cmd.ExecuteReaderAsync(
                        cancellationToken);

                if (!await rd.ReadAsync(cancellationToken))
                {
                    throw new InvalidOperationException(
                        "La ruta ya no existe.");
                }

                codigo = Texto(rd, "Codigo");
                nombre = Texto(rd, "Nombre");
                estadoActual =
                    Booleano(rd, "Activo");
            }

            if (estadoActual == activo)
            {
                await tx.RollbackAsync(cancellationToken);

                TempData["LogisticaOk"] =
                    activo
                        ? "La ruta ya se encuentra activa."
                        : "La ruta ya se encuentra inactiva.";

                return RedirectToAction(nameof(Index));
            }

            if (!activo)
            {
                const string sqlUso = @"
SELECT
(
    SELECT COUNT_BIG(*)
    FROM dbo.Logistica_Embarques
    WHERE
        RutaID=@RutaID
        AND Activo=1
        AND Estatus NOT IN(N'Entregado',N'Cancelado')
)
+
(
    SELECT COUNT_BIG(*)
    FROM dbo.Logistica_Viajes
    WHERE
        RutaID=@RutaID
        AND Activo=1
        AND Estatus NOT IN(N'Completado',N'Cancelado')
);";

                await using var cmd =
                    new SqlCommand(
                        sqlUso,
                        cn,
                        tx);

                cmd.Parameters.Add(
                    "@RutaID",
                    SqlDbType.Int).Value =
                    rutaId;

                var usosActivos =
                    Convert.ToInt64(
                        await cmd.ExecuteScalarAsync(
                            cancellationToken));

                if (usosActivos > 0)
                {
                    throw new InvalidOperationException(
                        $"La ruta {codigo} está asignada a {usosActivos:N0} operación(es) activa(s). No puede desactivarse todavía.");
                }
            }

            const string sqlUpdate = @"
UPDATE dbo.Logistica_Rutas
SET Activo=@Activo
WHERE RutaID=@RutaID;

SELECT @@ROWCOUNT;";

            await using (var cmd =
                new SqlCommand(
                    sqlUpdate,
                    cn,
                    tx))
            {
                cmd.Parameters.Add(
                    "@Activo",
                    SqlDbType.Bit).Value =
                    activo;

                cmd.Parameters.Add(
                    "@RutaID",
                    SqlDbType.Int).Value =
                    rutaId;

                if (Convert.ToInt32(
                        await cmd.ExecuteScalarAsync(
                            cancellationToken)) == 0)
                {
                    throw new InvalidOperationException(
                        "No fue posible cambiar el estado de la ruta.");
                }
            }

            await tx.CommitAsync(cancellationToken);

            TempData["LogisticaOk"] =
                activo
                    ? $"Ruta {codigo} - {nombre} activada."
                    : $"Ruta {codigo} - {nombre} desactivada.";
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);

            TempData["LogisticaError"] =
                $"No fue posible cambiar el estado de la ruta: {ex.Message}";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GuardarUnidad(LogisticaUnidadVm model, CancellationToken cancellationToken)
    {
        if (!UsuarioEnSesion()) return RedirectToAction("Login", "Login");
        model.NumeroEconomico = model.NumeroEconomico?.Trim() ?? string.Empty;
        model.Placas = model.Placas?.Trim() ?? string.Empty;
        model.Marca = model.Marca?.Trim() ?? string.Empty;
        model.Modelo = model.Modelo?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(model.NumeroEconomico))
        {
            TempData["LogisticaError"] = "El número económico es obligatorio.";
            return RedirectToAction(nameof(Index));
        }
        if (model.NumeroEconomico.Length > 50)
        {
            TempData["LogisticaError"] = "El número económico no puede exceder 50 caracteres.";
            return RedirectToAction(nameof(Index));
        }
        if (model.Placas.Length > 30)
        {
            TempData["LogisticaError"] = "Las placas no pueden exceder 30 caracteres.";
            return RedirectToAction(nameof(Index));
        }
        if (model.Marca.Length > 80)
        {
            TempData["LogisticaError"] = "La marca no puede exceder 80 caracteres.";
            return RedirectToAction(nameof(Index));
        }
        if (model.Modelo.Length > 80)
        {
            TempData["LogisticaError"] = "El modelo no puede exceder 80 caracteres.";
            return RedirectToAction(nameof(Index));
        }
        if (model.CapacidadPesoKg.HasValue && model.CapacidadPesoKg.Value <= 0)
        {
            TempData["LogisticaError"] = "La capacidad de carga debe ser mayor a cero.";
            return RedirectToAction(nameof(Index));
        }
        await using var cn = await AbrirAsync(cancellationToken);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            const string sqlDuplicado = @"
SELECT COUNT_BIG(*)
FROM dbo.Logistica_Unidades WITH(UPDLOCK,HOLDLOCK)
WHERE Activo=1
AND
(
    UPPER(LTRIM(RTRIM(NumeroEconomico)))=UPPER(LTRIM(RTRIM(@NumeroEconomico)))
    OR
    (
        NULLIF(LTRIM(RTRIM(@Placas)),N'') IS NOT NULL
        AND UPPER(LTRIM(RTRIM(ISNULL(Placas,N''))))=UPPER(LTRIM(RTRIM(@Placas)))
    )
)
AND (@UnidadID<=0 OR UnidadID<>@UnidadID);";
            await using (var cmd = new SqlCommand(sqlDuplicado, cn, tx))
            {
                cmd.Parameters.Add("@NumeroEconomico", SqlDbType.NVarChar, 50).Value = model.NumeroEconomico;
                cmd.Parameters.Add("@Placas", SqlDbType.NVarChar, 30).Value = model.Placas;
                cmd.Parameters.Add("@UnidadID", SqlDbType.Int).Value = model.UnidadID;
                var duplicados = Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken));
                if (duplicados > 0) throw new InvalidOperationException("Ya existe una unidad activa con el mismo número económico o placas.");
            }
            if (model.UnidadID <= 0)
            {
                const string sqlInsert = @"
INSERT dbo.Logistica_Unidades
(NumeroEconomico,Placas,Marca,Modelo,CapacidadPesoKg,Activo,FechaCreacion,CreadoPor)
VALUES
(@NumeroEconomico,@Placas,@Marca,@Modelo,@CapacidadPesoKg,1,SYSDATETIME(),@Usuario);
SELECT CONVERT(int,SCOPE_IDENTITY());";
                int unidadId;
                await using (var cmd = new SqlCommand(sqlInsert, cn, tx))
                {
                    cmd.Parameters.Add("@NumeroEconomico", SqlDbType.NVarChar, 50).Value = model.NumeroEconomico;
                    cmd.Parameters.Add("@Placas", SqlDbType.NVarChar, 30).Value = Db(string.IsNullOrWhiteSpace(model.Placas) ? null : model.Placas);
                    cmd.Parameters.Add("@Marca", SqlDbType.NVarChar, 80).Value = Db(string.IsNullOrWhiteSpace(model.Marca) ? null : model.Marca);
                    cmd.Parameters.Add("@Modelo", SqlDbType.NVarChar, 80).Value = Db(string.IsNullOrWhiteSpace(model.Modelo) ? null : model.Modelo);
                    var pCapacidad = cmd.Parameters.Add("@CapacidadPesoKg", SqlDbType.Decimal);
                    pCapacidad.Precision = 18;
                    pCapacidad.Scale = 2;
                    pCapacidad.Value = Db(model.CapacidadPesoKg);
                    cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 120).Value = UsuarioNombre;
                    unidadId = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
                }
                await tx.CommitAsync(cancellationToken);
                TempData["LogisticaOk"] = $"Unidad {model.NumeroEconomico} creada correctamente.";
                return RedirectToAction(nameof(Index));
            }
            const string sqlExiste = @"SELECT COUNT_BIG(*) FROM dbo.Logistica_Unidades WITH(UPDLOCK,HOLDLOCK) WHERE UnidadID=@UnidadID;";
            await using (var cmd = new SqlCommand(sqlExiste, cn, tx))
            {
                cmd.Parameters.Add("@UnidadID", SqlDbType.Int).Value = model.UnidadID;
                if (Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken)) <= 0) throw new InvalidOperationException("La unidad que intentas editar ya no existe.");
            }
            const string sqlUpdate = @"
UPDATE dbo.Logistica_Unidades
SET NumeroEconomico=@NumeroEconomico,
    Placas=@Placas,
    Marca=@Marca,
    Modelo=@Modelo,
    CapacidadPesoKg=@CapacidadPesoKg
WHERE UnidadID=@UnidadID;
SELECT @@ROWCOUNT;";
            await using (var cmd = new SqlCommand(sqlUpdate, cn, tx))
            {
                cmd.Parameters.Add("@NumeroEconomico", SqlDbType.NVarChar, 50).Value = model.NumeroEconomico;
                cmd.Parameters.Add("@Placas", SqlDbType.NVarChar, 30).Value = Db(string.IsNullOrWhiteSpace(model.Placas) ? null : model.Placas);
                cmd.Parameters.Add("@Marca", SqlDbType.NVarChar, 80).Value = Db(string.IsNullOrWhiteSpace(model.Marca) ? null : model.Marca);
                cmd.Parameters.Add("@Modelo", SqlDbType.NVarChar, 80).Value = Db(string.IsNullOrWhiteSpace(model.Modelo) ? null : model.Modelo);
                var pCapacidad = cmd.Parameters.Add("@CapacidadPesoKg", SqlDbType.Decimal);
                pCapacidad.Precision = 18;
                pCapacidad.Scale = 2;
                pCapacidad.Value = Db(model.CapacidadPesoKg);
                cmd.Parameters.Add("@UnidadID", SqlDbType.Int).Value = model.UnidadID;
                if (Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken)) == 0) throw new InvalidOperationException("No fue posible actualizar la unidad.");
            }
            await tx.CommitAsync(cancellationToken);
            TempData["LogisticaOk"] = $"Unidad {model.NumeroEconomico} actualizada correctamente.";
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
            TempData["LogisticaError"] = $"No fue posible guardar la unidad: {ex.Message}";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CambiarEstadoUnidad(
        int unidadId,
        bool activo,
        CancellationToken cancellationToken)
    {
        if (!UsuarioEnSesion())
            return RedirectToAction("Login", "Login");

        if (unidadId <= 0)
        {
            TempData["LogisticaError"] =
                "La unidad indicada no es válida.";

            return RedirectToAction(nameof(Index));
        }

        await using var cn =
            await AbrirAsync(cancellationToken);

        await using var tx =
            (SqlTransaction)await cn.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

        try
        {
            const string sqlActual = @"
SELECT
    ISNULL(NumeroEconomico,N'') AS NumeroEconomico,
    ISNULL(Placas,N'') AS Placas,
    ISNULL(Activo,0) AS Activo
FROM dbo.Logistica_Unidades WITH (UPDLOCK,HOLDLOCK)
WHERE UnidadID=@UnidadID;";

            string numeroEconomico;
            string placas;
            bool estadoActual;

            await using (var cmd =
                new SqlCommand(
                    sqlActual,
                    cn,
                    tx))
            {
                cmd.Parameters.Add(
                    "@UnidadID",
                    SqlDbType.Int).Value =
                    unidadId;

                await using var rd =
                    await cmd.ExecuteReaderAsync(
                        cancellationToken);

                if (!await rd.ReadAsync(cancellationToken))
                {
                    throw new InvalidOperationException(
                        "La unidad ya no existe.");
                }

                numeroEconomico =
                    Texto(rd, "NumeroEconomico");

                placas =
                    Texto(rd, "Placas");

                estadoActual =
                    Booleano(rd, "Activo");
            }

            if (estadoActual == activo)
            {
                await tx.RollbackAsync(cancellationToken);

                TempData["LogisticaOk"] =
                    activo
                        ? "La unidad ya se encuentra activa."
                        : "La unidad ya se encuentra inactiva.";

                return RedirectToAction(nameof(Index));
            }

            if (!activo)
            {
                const string sqlUso = @"
SELECT
(
    SELECT COUNT_BIG(*)
    FROM dbo.Logistica_Embarques
    WHERE
        UnidadID=@UnidadID
        AND Activo=1
        AND Estatus NOT IN(N'Entregado',N'Cancelado')
)
+
(
    SELECT COUNT_BIG(*)
    FROM dbo.Logistica_Viajes
    WHERE
        UnidadID=@UnidadID
        AND Activo=1
        AND Estatus NOT IN(N'Completado',N'Cancelado')
);";

                await using var cmd =
                    new SqlCommand(
                        sqlUso,
                        cn,
                        tx);

                cmd.Parameters.Add(
                    "@UnidadID",
                    SqlDbType.Int).Value =
                    unidadId;

                var usosActivos =
                    Convert.ToInt64(
                        await cmd.ExecuteScalarAsync(
                            cancellationToken));

                if (usosActivos > 0)
                {
                    throw new InvalidOperationException(
                        $"La unidad {numeroEconomico} está asignada a {usosActivos:N0} operación(es) activa(s). No puede desactivarse todavía.");
                }
            }

            const string sqlUpdate = @"
UPDATE dbo.Logistica_Unidades
SET Activo=@Activo
WHERE UnidadID=@UnidadID;

SELECT @@ROWCOUNT;";

            await using (var cmd =
                new SqlCommand(
                    sqlUpdate,
                    cn,
                    tx))
            {
                cmd.Parameters.Add(
                    "@Activo",
                    SqlDbType.Bit).Value =
                    activo;

                cmd.Parameters.Add(
                    "@UnidadID",
                    SqlDbType.Int).Value =
                    unidadId;

                if (Convert.ToInt32(
                        await cmd.ExecuteScalarAsync(
                            cancellationToken)) == 0)
                {
                    throw new InvalidOperationException(
                        "No fue posible cambiar el estado de la unidad.");
                }
            }

            await tx.CommitAsync(cancellationToken);

            var descripcion =
                string.IsNullOrWhiteSpace(placas)
                    ? numeroEconomico
                    : $"{numeroEconomico} - {placas}";

            TempData["LogisticaOk"] =
                activo
                    ? $"Unidad {descripcion} activada."
                    : $"Unidad {descripcion} desactivada.";
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);

            TempData["LogisticaError"] =
                $"No fue posible cambiar el estado de la unidad: {ex.Message}";
        }

        return RedirectToAction(nameof(Index));
    }

    private static object Db(object? value) =>
        value ?? DBNull.Value;

    private static string Texto(
        SqlDataReader rd,
        string columna)
    {
        var i = rd.GetOrdinal(columna);

        return rd.IsDBNull(i)
            ? string.Empty
            : rd.GetValue(i)?.ToString()
              ?? string.Empty;
    }

    private static int Entero(
        SqlDataReader rd,
        string columna)
    {
        var i = rd.GetOrdinal(columna);

        return rd.IsDBNull(i)
            ? 0
            : Convert.ToInt32(
                rd.GetValue(i));
    }

    private static int? EnteroNullable(
        SqlDataReader rd,
        string columna)
    {
        var i = rd.GetOrdinal(columna);

        return rd.IsDBNull(i)
            ? null
            : Convert.ToInt32(
                rd.GetValue(i));
    }

    private static decimal? DecimalNullable(SqlDataReader rd, string columna)
    {
        var i = rd.GetOrdinal(columna);
        return rd.IsDBNull(i) ? null : Convert.ToDecimal(rd.GetValue(i));
    }

    private static bool Booleano(
        SqlDataReader rd,
        string columna)
    {
        var i = rd.GetOrdinal(columna);

        return !rd.IsDBNull(i)
            && Convert.ToBoolean(
                rd.GetValue(i));
    }
}
