using ERP.NSQuell.Models.ViewModels;
using ERP.NSQuell.Servicios.Almacen;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ERP.NSQuell.Controllers
{
    public partial class CalidadController
    {
        private const string EstadoCajaLibreCalidad = "EN_CALIDAD";
        private const string EstadoCajaLibreAlmacen = "ENVIADA_ALMACEN";

        private sealed record ParteCajaLibreCatalogo(
            int ParteID,
            string NumeroParte,
            string ReferenciaSAP,
            string Descripcion);

        private static List<string> SepararCodigosCajaLibre(
            string? valor) =>
            (valor ?? string.Empty)
                .Split(
                    new[] { "\r\n", "\n", "\r" },
                    StringSplitOptions.RemoveEmptyEntries
                    | StringSplitOptions.TrimEntries)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        private static string NormalizarParteCajaLibre(
            string? valor)
        {
            if (string.IsNullOrWhiteSpace(valor))
                return string.Empty;

            return new string(
                valor
                    .Where(char.IsLetterOrDigit)
                    .Select(char.ToUpperInvariant)
                    .ToArray());
        }

        private static void AgregarAliasCajaLibre(
            Dictionary<string,List<ParteCajaLibreCatalogo>> resultado,
            string identificador,
            ParteCajaLibreCatalogo candidato)
        {
            var clave =
                NormalizarParteCajaLibre(
                    identificador);

            if (string.IsNullOrWhiteSpace(clave))
                return;

            static void Agregar(
                Dictionary<string,List<ParteCajaLibreCatalogo>> destino,
                string llave,
                ParteCajaLibreCatalogo item)
            {
                if (!destino.TryGetValue(
                        llave,
                        out var lista))
                {
                    lista =
                        new List<ParteCajaLibreCatalogo>();

                    destino[llave] = lista;
                }

                if (!lista.Any(x =>
                    x.ParteID == item.ParteID))
                {
                    lista.Add(item);
                }
            }

            Agregar(
                resultado,
                clave,
                candidato);

            // Mismos aliases controlados que AlmacenPT/Entrada.
            if (clave.Length >= 6)
            {
                Agregar(
                    resultado,
                    clave + "00",
                    candidato);

                if (clave.Length >= 9)
                {
                    Agregar(
                        resultado,
                        clave.Substring(1),
                        candidato);
                }
            }
        }

        private static async Task<
            Dictionary<string,List<ParteCajaLibreCatalogo>>>
            CargarCatalogoCajaLibreAsync(
                SqlConnection cn,
                SqlTransaction? tx,
                CancellationToken cancellationToken)
        {
            const string sql = @"
SELECT
    ParteID,
    COALESCE(NULLIF(LTRIM(RTRIM(NumeroParte)),N''),N'') AS NumeroParte,
    COALESCE(NULLIF(LTRIM(RTRIM(ReferenciaSAP)),N''),N'') AS ReferenciaSAP,
    COALESCE
    (
        NULLIF(LTRIM(RTRIM(Designacion)),N''),
        NULLIF(LTRIM(RTRIM(Descripcion)),N''),
        N''
    ) AS Descripcion
FROM dbo.ERP_Partes
WHERE Activo=1
  AND
  (
      NULLIF(LTRIM(RTRIM(NumeroParte)),N'') IS NOT NULL
      OR NULLIF(LTRIM(RTRIM(ReferenciaSAP)),N'') IS NOT NULL
  )
ORDER BY ParteID;";

            await using var cmd =
                tx == null
                    ? new SqlCommand(sql,cn)
                    : new SqlCommand(sql,cn,tx);

            var resultado =
                new Dictionary<
                    string,
                    List<ParteCajaLibreCatalogo>>(
                    StringComparer.OrdinalIgnoreCase);

            await using var rd =
                await cmd.ExecuteReaderAsync(
                    cancellationToken);

            while (await rd.ReadAsync(
                cancellationToken))
            {
                var item =
                    new ParteCajaLibreCatalogo(
                        Convert.ToInt32(rd["ParteID"]),
                        rd["NumeroParte"]?.ToString()?.Trim()
                            ?? string.Empty,
                        rd["ReferenciaSAP"]?.ToString()?.Trim()
                            ?? string.Empty,
                        rd["Descripcion"]?.ToString()?.Trim()
                            ?? string.Empty);

                AgregarAliasCajaLibre(
                    resultado,
                    item.NumeroParte,
                    item);

                AgregarAliasCajaLibre(
                    resultado,
                    item.ReferenciaSAP,
                    item);
            }

            return resultado;
        }

        private async Task<
            List<CalidadCajaConversionLibreVm>>
            ConvertirCajasInventarioLibreAsync(
                SqlConnection cn,
                SqlTransaction? tx,
                IReadOnlyList<string> codigos,
                CancellationToken cancellationToken)
        {
            var resultado =
                new List<CalidadCajaConversionLibreVm>();

            var catalogo =
                await CargarCatalogoCajaLibreAsync(
                    cn,
                    tx,
                    cancellationToken);

            var lote =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);

            for (var i=0;i<codigos.Count;i++)
            {
                var codigo =
                    codigos[i];

                var item =
                    new CalidadCajaConversionLibreVm
                    {
                        Renglon=i+1,
                        CodigoOriginal=codigo
                    };

                if (!AlmacenPTCodigoBarrasService.TryParse(
                        codigo,
                        out var parseado,
                        out var error)
                    || parseado == null)
                {
                    item.Mensaje=error;
                    resultado.Add(item);
                    continue;
                }

                if (!lote.Add(
                        parseado.CodigoOriginal))
                {
                    item.Mensaje=
                        "Etiqueta repetida dentro del lote.";
                    resultado.Add(item);
                    continue;
                }

                var clave =
                    NormalizarParteCajaLibre(
                        parseado.NumeroParte);

                if (string.IsNullOrWhiteSpace(clave)
                    || !catalogo.TryGetValue(
                        clave,
                        out var candidatos)
                    || candidatos.Count == 0)
                {
                    item.Mensaje=
                        "El numero de parte de la etiqueta no existe en ERP_Partes.";
                    resultado.Add(item);
                    continue;
                }

                if (candidatos.Count != 1)
                {
                    item.Mensaje=
                        "El numero de parte coincide con mas de un registro del catalogo. Requiere depuracion.";
                    resultado.Add(item);
                    continue;
                }

                var parte =
                    candidatos[0];

                var duplicadaCalidad =
                    false;

                var duplicadaAlmacen =
                    false;

                const string sqlDup = @"
SELECT
    CASE WHEN EXISTS
    (
        SELECT 1
        FROM dbo.Calidad_CajasInventarioLibre
        WHERE CodigoBarras=@Codigo
    ) THEN 1 ELSE 0 END AS EnCalidad,

    CASE WHEN EXISTS
    (
        SELECT 1
        FROM dbo.AlmacenPT_Cajas
        WHERE Activo=1
          AND LTRIM(RTRIM(ISNULL(Etiqueta,N'')))=@Codigo
    ) THEN 1 ELSE 0 END AS EnAlmacen;";

                await using (var dup =
                    tx == null
                        ? new SqlCommand(sqlDup,cn)
                        : new SqlCommand(sqlDup,cn,tx))
                {
                    dup.Parameters.Add(
                        "@Codigo",
                        SqlDbType.NVarChar,
                        120).Value =
                        parseado.CodigoOriginal;

                    await using var rd =
                        await dup.ExecuteReaderAsync(
                            cancellationToken);

                    if (await rd.ReadAsync(
                        cancellationToken))
                    {
                        duplicadaCalidad=
                            Convert.ToInt32(
                                rd["EnCalidad"])==1;

                        duplicadaAlmacen=
                            Convert.ToInt32(
                                rd["EnAlmacen"])==1;
                    }
                }

                if (duplicadaCalidad)
                {
                    item.Mensaje=
                        "La etiqueta ya existe en el inventario de Calidad.";
                    resultado.Add(item);
                    continue;
                }

                if (duplicadaAlmacen)
                {
                    item.Mensaje=
                        "La etiqueta ya existe en Almacen PT.";
                    resultado.Add(item);
                    continue;
                }

                item.Valido=true;
                item.ParteID=parte.ParteID;

                // La etiqueta puede contener OF, pero no se exige
                // relacion vigente con SolicitudesProduccion.
                item.NumeroOF=
                    AlmacenPTCodigoBarrasService
                        .NormalizarNumeroOFEtiqueta(
                            parseado.NumeroOF);

                item.NumeroParte=
                    parseado.NumeroParte;

                item.Designacion=
                    parseado.Designacion;

                item.Cantidad=
                    parseado.Cantidad;

                item.Lote=
                    parseado.Lote;

                item.NumeroParteCatalogo=
                    parte.NumeroParte;

                item.DescripcionCatalogo=
                    parte.Descripcion;

                item.Mensaje=
                    "Etiqueta valida para inventario de Calidad.";

                resultado.Add(item);
            }

            return resultado;
        }

        private async Task<
            CalidadCajasInventarioLibreVm>
            CargarCajasInventarioLibreAsync(
                string? busqueda,
                CancellationToken cancellationToken=default)
        {
            var vm =
                new CalidadCajasInventarioLibreVm();

            await using var cn =
                new SqlConnection(
                    ConnectionString);

            await cn.OpenAsync(
                cancellationToken);

            const string sql = @"
SELECT TOP(500)
    c.CalidadCajaInventarioID,
    c.CodigoBarras,
    c.ParteID,
    c.NumeroOF,
    c.NumeroParteEtiqueta,
    c.DesignacionEtiqueta,
    c.Cantidad,
    c.LoteEtiqueta,
    c.Estado,
    c.FechaEscaneoCalidad,
    c.FechaEnvioAlmacen,
    c.AlmacenPTCajaID,
    p.NumeroParte AS NumeroParteCatalogo,
    COALESCE
    (
        NULLIF(LTRIM(RTRIM(p.Designacion)),N''),
        NULLIF(LTRIM(RTRIM(p.Descripcion)),N''),
        N''
    ) AS DescripcionCatalogo
FROM dbo.Calidad_CajasInventarioLibre c
INNER JOIN dbo.ERP_Partes p
    ON p.ParteID=c.ParteID
WHERE c.Activo=1
  AND
  (
      @Busqueda IS NULL
      OR c.CodigoBarras LIKE N'%'+@Busqueda+N'%'
      OR ISNULL(c.NumeroOF,N'') LIKE N'%'+@Busqueda+N'%'
      OR c.NumeroParteEtiqueta LIKE N'%'+@Busqueda+N'%'
      OR ISNULL(c.DesignacionEtiqueta,N'') LIKE N'%'+@Busqueda+N'%'
      OR ISNULL(c.LoteEtiqueta,N'') LIKE N'%'+@Busqueda+N'%'
      OR p.NumeroParte LIKE N'%'+@Busqueda+N'%'
  )
ORDER BY
    CASE WHEN c.Estado=N'EN_CALIDAD' THEN 0 ELSE 1 END,
    c.FechaEscaneoCalidad DESC,
    c.CalidadCajaInventarioID DESC;";

            await using (var cmd =
                new SqlCommand(
                    sql,
                    cn))
            {
                cmd.Parameters.Add(
                    "@Busqueda",
                    SqlDbType.NVarChar,
                    250).Value =
                    string.IsNullOrWhiteSpace(busqueda)
                        ? DBNull.Value
                        : busqueda.Trim();

                await using var rd =
                    await cmd.ExecuteReaderAsync(
                        cancellationToken);

                while (await rd.ReadAsync(
                    cancellationToken))
                {
                    vm.Items.Add(
                        new CalidadCajaInventarioLibreItemVm
                        {
                            CalidadCajaInventarioID=
                                Convert.ToInt64(
                                    rd["CalidadCajaInventarioID"]),

                            CodigoBarras=
                                rd["CodigoBarras"]?.ToString()?.Trim()
                                ?? string.Empty,

                            ParteID=
                                Convert.ToInt32(
                                    rd["ParteID"]),

                            NumeroOF=
                                rd["NumeroOF"]==DBNull.Value
                                    ? null
                                    : rd["NumeroOF"]?.ToString()?.Trim(),

                            NumeroParteEtiqueta=
                                rd["NumeroParteEtiqueta"]?.ToString()?.Trim()
                                ?? string.Empty,

                            DesignacionEtiqueta=
                                rd["DesignacionEtiqueta"]==DBNull.Value
                                    ? null
                                    : rd["DesignacionEtiqueta"]?.ToString()?.Trim(),

                            Cantidad=
                                Convert.ToInt32(
                                    rd["Cantidad"]),

                            LoteEtiqueta=
                                rd["LoteEtiqueta"]==DBNull.Value
                                    ? null
                                    : rd["LoteEtiqueta"]?.ToString()?.Trim(),

                            Estado=
                                rd["Estado"]?.ToString()?.Trim()
                                ?? string.Empty,

                            FechaEscaneoCalidad=
                                Convert.ToDateTime(
                                    rd["FechaEscaneoCalidad"]),

                            FechaEnvioAlmacen=
                                rd["FechaEnvioAlmacen"]==DBNull.Value
                                    ? null
                                    : Convert.ToDateTime(
                                        rd["FechaEnvioAlmacen"]),

                            AlmacenPTCajaID=
                                rd["AlmacenPTCajaID"]==DBNull.Value
                                    ? null
                                    : Convert.ToInt32(
                                        rd["AlmacenPTCajaID"]),

                            NumeroParteCatalogo=
                                rd["NumeroParteCatalogo"]?.ToString()?.Trim(),

                            DescripcionCatalogo=
                                rd["DescripcionCatalogo"]?.ToString()?.Trim()
                        });
                }
            }

            const string sqlUbicaciones = @"
SELECT
    UbicacionID,
    Almacen,
    Rack,
    Nivel,
    Posicion
FROM dbo.ERP_Ubicaciones
WHERE Activo=1
  AND
  (
      Almacen=N'PT'
      OR Almacen=N'GENERAL'
  )
ORDER BY
    Almacen,
    Rack,
    Nivel,
    Posicion;";

            await using (var cmd =
                new SqlCommand(
                    sqlUbicaciones,
                    cn))
            {
                await using var rd =
                    await cmd.ExecuteReaderAsync(
                        cancellationToken);

                while (await rd.ReadAsync(
                    cancellationToken))
                {
                    var partes =
                        new[]
                        {
                            rd["Almacen"]?.ToString()?.Trim(),
                            rd["Rack"]?.ToString()?.Trim(),
                            rd["Nivel"]?.ToString()?.Trim(),
                            rd["Posicion"]?.ToString()?.Trim()
                        }
                        .Where(x =>
                            !string.IsNullOrWhiteSpace(x));

                    vm.Ubicaciones.Add(
                        new CalidadCajaUbicacionVm
                        {
                            UbicacionID=
                                Convert.ToInt32(
                                    rd["UbicacionID"]),

                            Texto=
                                string.Join(
                                    " · ",
                                    partes)
                        });
                }
            }

            vm.TotalCajas=
                vm.Items.Count(x =>
                    x.Estado==EstadoCajaLibreCalidad);

            vm.TotalPiezas=
                vm.Items
                    .Where(x =>
                        x.Estado==EstadoCajaLibreCalidad)
                    .Sum(x =>
                        (long)x.Cantidad);

            return vm;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult>
            ConvertirCajasInventarioLibre(
                string? codigosEscaneados,
                CancellationToken cancellationToken)
        {
            var usuarioId=
                ObtenerUsuarioIdActual();

            if (!usuarioId.HasValue
                || usuarioId.Value<=0)
            {
                return Unauthorized(
                    new
                    {
                        ok=false,
                        mensaje=
                            "No se pudo identificar el usuario."
                    });
            }

            var codigos=
                SepararCodigosCajaLibre(
                    codigosEscaneados);

            if (codigos.Count==0)
            {
                return Json(
                    new
                    {
                        ok=false,
                        mensaje=
                            "Escanea al menos una etiqueta.",
                        items=
                            Array.Empty<object>()
                    });
            }

            await using var cn=
                new SqlConnection(
                    ConnectionString);

            await cn.OpenAsync(
                cancellationToken);

            var items=
                await ConvertirCajasInventarioLibreAsync(
                    cn,
                    tx:null,
                    codigos,
                    cancellationToken);

            return Json(
                new
                {
                    ok=true,
                    validas=
                        items.Count(x=>x.Valido),
                    invalidas=
                        items.Count(x=>!x.Valido),
                    items
                });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult>
            RegistrarCajasInventarioLibre(
                string? codigosEscaneados,
                CancellationToken cancellationToken)
        {
            var usuarioId=
                ObtenerUsuarioIdActual();

            if (!usuarioId.HasValue
                || usuarioId.Value<=0)
            {
                TempData["Error"]=
                    "No se pudo identificar el usuario.";

                return RedirectToAction(
                    nameof(Index),
                    new { grupo="CAJAS" });
            }

            var codigos=
                SepararCodigosCajaLibre(
                    codigosEscaneados);

            if (codigos.Count==0)
            {
                TempData["Error"]=
                    "Escanea al menos una etiqueta.";

                return RedirectToAction(
                    nameof(Index),
                    new { grupo="CAJAS" });
            }

            await using var cn=
                new SqlConnection(
                    ConnectionString);

            await cn.OpenAsync(
                cancellationToken);

            await using var tx=
                (SqlTransaction)
                await cn.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken);

            try
            {
                var items=
                    await ConvertirCajasInventarioLibreAsync(
                        cn,
                        tx,
                        codigos,
                        cancellationToken);

                if (!items.Any()
                    || items.Any(x=>!x.Valido))
                {
                    await tx.RollbackAsync(
                        cancellationToken);

                    TempData["Error"]=
                        "Hay etiquetas invalidas, repetidas o ya registradas. Usa Convertir etiquetas y corrige el lote antes de guardarlo.";

                    return RedirectToAction(
                        nameof(Index),
                        new { grupo="CAJAS" });
                }

                const string insertSql = @"
INSERT dbo.Calidad_CajasInventarioLibre
(
    CodigoBarras,
    ParteID,
    NumeroOF,
    NumeroParteEtiqueta,
    DesignacionEtiqueta,
    Cantidad,
    LoteEtiqueta,
    Estado,
    Observaciones,
    FechaEscaneoCalidad,
    UsuarioCalidadID,
    FechaCreacion,
    Activo
)
VALUES
(
    @Codigo,
    @ParteID,
    @NumeroOF,
    @NumeroParte,
    @Designacion,
    @Cantidad,
    @Lote,
    N'EN_CALIDAD',
    N'Caja registrada por escaneo masivo en Calidad. Sin requerir relacion activa con OF ni estandar de caja completa.',
    SYSDATETIME(),
    @UsuarioID,
    SYSDATETIME(),
    1
);";

                foreach (var item in items)
                {
                    await using var cmd=
                        new SqlCommand(
                            insertSql,
                            cn,
                            tx);

                    cmd.Parameters.Add(
                        "@Codigo",
                        SqlDbType.NVarChar,
                        120).Value=
                        item.CodigoOriginal;

                    cmd.Parameters.Add(
                        "@ParteID",
                        SqlDbType.Int).Value=
                        item.ParteID;

                    cmd.Parameters.Add(
                        "@NumeroOF",
                        SqlDbType.NVarChar,
                        80).Value=
                        string.IsNullOrWhiteSpace(
                            item.NumeroOF)
                            ? DBNull.Value
                            : item.NumeroOF;

                    cmd.Parameters.Add(
                        "@NumeroParte",
                        SqlDbType.NVarChar,
                        120).Value=
                        item.NumeroParte;

                    cmd.Parameters.Add(
                        "@Designacion",
                        SqlDbType.NVarChar,
                        300).Value=
                        string.IsNullOrWhiteSpace(
                            item.Designacion)
                            ? DBNull.Value
                            : item.Designacion;

                    cmd.Parameters.Add(
                        "@Cantidad",
                        SqlDbType.Int).Value=
                        item.Cantidad;

                    cmd.Parameters.Add(
                        "@Lote",
                        SqlDbType.NVarChar,
                        120).Value=
                        string.IsNullOrWhiteSpace(
                            item.Lote)
                            ? DBNull.Value
                            : item.Lote;

                    cmd.Parameters.Add(
                        "@UsuarioID",
                        SqlDbType.Int).Value=
                        usuarioId.Value;

                    await cmd.ExecuteNonQueryAsync(
                        cancellationToken);
                }

                await tx.CommitAsync(
                    cancellationToken);

                TempData["Mensaje"]=
                    $"Se registraron {items.Count} caja(s) en el inventario de Calidad.";
            }
            catch (SqlException ex)
                when (ex.Number is 2601 or 2627)
            {
                try {
                    await tx.RollbackAsync(
                        cancellationToken);
                }
                catch { }

                TempData["Error"]=
                    "Una etiqueta ya estaba registrada. No se guardo el lote.";
            }
            catch (Exception ex)
            {
                try {
                    await tx.RollbackAsync(
                        cancellationToken);
                }
                catch { }

                TempData["Error"]=
                    "No fue posible registrar el inventario de cajas: "
                    + ex.Message;
            }

            return RedirectToAction(
                nameof(Index),
                new { grupo="CAJAS" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult>
            EnviarCajasInventarioLibreAlmacen(
                long[]? cajaInventarioIds,
                int ubicacionId,
                CancellationToken cancellationToken)
        {
            var usuarioId=
                ObtenerUsuarioIdActual();

            if (!usuarioId.HasValue
                || usuarioId.Value<=0)
            {
                TempData["Error"]=
                    "No se pudo identificar el usuario.";

                return RedirectToAction(
                    nameof(Index),
                    new { grupo="CAJAS" });
            }

            var ids=
                (cajaInventarioIds
                    ?? Array.Empty<long>())
                .Where(x=>x>0)
                .Distinct()
                .ToArray();

            if (ids.Length==0)
            {
                TempData["Error"]=
                    "Selecciona al menos una caja.";

                return RedirectToAction(
                    nameof(Index),
                    new { grupo="CAJAS" });
            }

            if (ubicacionId<=0)
            {
                TempData["Error"]=
                    "Selecciona la ubicacion de Almacen PT.";

                return RedirectToAction(
                    nameof(Index),
                    new { grupo="CAJAS" });
            }

            await using var cn=
                new SqlConnection(
                    ConnectionString);

            await cn.OpenAsync(
                cancellationToken);

            await using var tx=
                (SqlTransaction)
                await cn.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken);

            try
            {
                const string ubicacionSql = @"
SELECT COUNT(*)
FROM dbo.ERP_Ubicaciones WITH(UPDLOCK,HOLDLOCK)
WHERE UbicacionID=@UbicacionID
  AND Activo=1
  AND
  (
      Almacen=N'PT'
      OR Almacen=N'GENERAL'
  );";

                await using (var cmd=
                    new SqlCommand(
                        ubicacionSql,
                        cn,
                        tx))
                {
                    cmd.Parameters.Add(
                        "@UbicacionID",
                        SqlDbType.Int).Value=
                        ubicacionId;

                    if (Convert.ToInt32(
                            await cmd.ExecuteScalarAsync(
                                cancellationToken)
                            ?? 0)!=1)
                    {
                        await tx.RollbackAsync(
                            cancellationToken);

                        TempData["Error"]=
                            "La ubicacion seleccionada no es una ubicacion activa de PT/GENERAL.";

                        return RedirectToAction(
                            nameof(Index),
                            new { grupo="CAJAS" });
                    }
                }

                var usuarioTexto=
                    User.Identity?.Name?.Trim();

                if (string.IsNullOrWhiteSpace(
                        usuarioTexto))
                {
                    usuarioTexto=
                        $"UsuarioID:{usuarioId.Value}";
                }

                var enviados=0;
                long piezas=0;

                foreach (var inventarioId in ids)
                {
                    const string cajaLibreSql = @"
SELECT TOP(1)
    CalidadCajaInventarioID,
    CodigoBarras,
    ParteID,
    NumeroOF,
    NumeroParteEtiqueta,
    DesignacionEtiqueta,
    Cantidad,
    LoteEtiqueta,
    Estado,
    AlmacenPTCajaID
FROM dbo.Calidad_CajasInventarioLibre
    WITH(UPDLOCK,HOLDLOCK)
WHERE CalidadCajaInventarioID=@ID
  AND Activo=1;";

                    long id;
                    string codigo;
                    int parteId;
                    string? numeroOF;
                    int cantidad;
                    string? lote;
                    string estado;
                    int? almacenCajaExistente;

                    await using (var cmd=
                        new SqlCommand(
                            cajaLibreSql,
                            cn,
                            tx))
                    {
                        cmd.Parameters.Add(
                            "@ID",
                            SqlDbType.BigInt).Value=
                            inventarioId;

                        await using var rd=
                            await cmd.ExecuteReaderAsync(
                                cancellationToken);

                        if (!await rd.ReadAsync(
                                cancellationToken))
                        {
                            throw new InvalidOperationException(
                                $"No existe la caja de inventario {inventarioId}.");
                        }

                        id=
                            Convert.ToInt64(
                                rd["CalidadCajaInventarioID"]);

                        codigo=
                            rd["CodigoBarras"]?.ToString()?.Trim()
                            ?? string.Empty;

                        parteId=
                            Convert.ToInt32(
                                rd["ParteID"]);

                        numeroOF=
                            rd["NumeroOF"]==DBNull.Value
                                ? null
                                : rd["NumeroOF"]?.ToString()?.Trim();

                        cantidad=
                            Convert.ToInt32(
                                rd["Cantidad"]);

                        lote=
                            rd["LoteEtiqueta"]==DBNull.Value
                                ? null
                                : rd["LoteEtiqueta"]?.ToString()?.Trim();

                        estado=
                            rd["Estado"]?.ToString()?.Trim()
                            ?? string.Empty;

                        almacenCajaExistente=
                            rd["AlmacenPTCajaID"]==DBNull.Value
                                ? null
                                : Convert.ToInt32(
                                    rd["AlmacenPTCajaID"]);
                    }

                    if (!string.Equals(
                            estado,
                            EstadoCajaLibreCalidad,
                            StringComparison.OrdinalIgnoreCase)
                        || almacenCajaExistente.HasValue)
                    {
                        throw new InvalidOperationException(
                            $"La caja {id} ya no esta disponible para envio.");
                    }

                    const string dupSql = @"
SELECT COUNT(*)
FROM dbo.AlmacenPT_Cajas WITH(UPDLOCK,HOLDLOCK)
WHERE Activo=1
  AND LTRIM(RTRIM(ISNULL(Etiqueta,N'')))=@Codigo;";

                    await using (var cmd=
                        new SqlCommand(
                            dupSql,
                            cn,
                            tx))
                    {
                        cmd.Parameters.Add(
                            "@Codigo",
                            SqlDbType.NVarChar,
                            120).Value=
                            codigo;

                        if (Convert.ToInt32(
                                await cmd.ExecuteScalarAsync(
                                    cancellationToken)
                                ?? 0)>0)
                        {
                            throw new InvalidOperationException(
                                $"La etiqueta {codigo} ya existe en Almacen PT.");
                        }
                    }

                    const string numeroCajaSql = @"
SELECT ISNULL(MAX(NumeroCaja),0)+1
FROM dbo.AlmacenPT_Cajas WITH(UPDLOCK,HOLDLOCK)
WHERE ParteID=@ParteID
  AND
  (
      (@NumeroOF IS NULL AND NumeroOF IS NULL)
      OR LTRIM(RTRIM(ISNULL(NumeroOF,N'')))=
         LTRIM(RTRIM(ISNULL(@NumeroOF,N'')))
  );";

                    int numeroCaja;

                    await using (var cmd=
                        new SqlCommand(
                            numeroCajaSql,
                            cn,
                            tx))
                    {
                        cmd.Parameters.Add(
                            "@ParteID",
                            SqlDbType.Int).Value=
                            parteId;

                        cmd.Parameters.Add(
                            "@NumeroOF",
                            SqlDbType.NVarChar,
                            80).Value=
                            string.IsNullOrWhiteSpace(
                                numeroOF)
                                ? DBNull.Value
                                : numeroOF;

                        numeroCaja=
                            Convert.ToInt32(
                                await cmd.ExecuteScalarAsync(
                                    cancellationToken)
                                ?? 1);
                    }

                    const string insertCajaSql = @"
INSERT dbo.AlmacenPT_Cajas
(
    ParteID,
    SolicitudProduccionID,
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
    NULL,
    @NumeroOF,
    @Etiqueta,
    @NumeroCaja,
    @Cantidad,
    @Lote,
    N'Liberado',
    @UbicacionID,
    SYSDATETIME(),
    SYSUTCDATETIME(),
    @Usuario,
    1
);";

                    int cajaAlmacenId;

                    await using (var cmd=
                        new SqlCommand(
                            insertCajaSql,
                            cn,
                            tx))
                    {
                        cmd.Parameters.Add(
                            "@ParteID",
                            SqlDbType.Int).Value=
                            parteId;

                        cmd.Parameters.Add(
                            "@NumeroOF",
                            SqlDbType.NVarChar,
                            80).Value=
                            string.IsNullOrWhiteSpace(
                                numeroOF)
                                ? DBNull.Value
                                : numeroOF;

                        cmd.Parameters.Add(
                            "@Etiqueta",
                            SqlDbType.NVarChar,
                            120).Value=
                            codigo;

                        cmd.Parameters.Add(
                            "@NumeroCaja",
                            SqlDbType.Int).Value=
                            numeroCaja;

                        cmd.Parameters.Add(
                            "@Cantidad",
                            SqlDbType.Int).Value=
                            cantidad;

                        cmd.Parameters.Add(
                            "@Lote",
                            SqlDbType.NVarChar,
                            120).Value=
                            string.IsNullOrWhiteSpace(
                                lote)
                                ? DBNull.Value
                                : lote;

                        cmd.Parameters.Add(
                            "@UbicacionID",
                            SqlDbType.Int).Value=
                            ubicacionId;

                        cmd.Parameters.Add(
                            "@Usuario",
                            SqlDbType.NVarChar,
                            120).Value=
                            usuarioTexto;

                        cajaAlmacenId=
                            Convert.ToInt32(
                                await cmd.ExecuteScalarAsync(
                                    cancellationToken));
                    }

                    const string movimientoSql = @"
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
    N'Entrada',
    @Cantidad,
    @UbicacionID,
    N'Liberado',
    @UsuarioID,
    N'Entrada de caja enviada desde inventario libre de Calidad.',
    SYSDATETIME(),
    SYSUTCDATETIME(),
    @Usuario,
    1,
    @Referencia
);";

                    await using (var cmd=
                        new SqlCommand(
                            movimientoSql,
                            cn,
                            tx))
                    {
                        cmd.Parameters.Add(
                            "@CajaID",
                            SqlDbType.Int).Value=
                            cajaAlmacenId;

                        cmd.Parameters.Add(
                            "@ParteID",
                            SqlDbType.Int).Value=
                            parteId;

                        cmd.Parameters.Add(
                            "@NumeroOF",
                            SqlDbType.NVarChar,
                            80).Value=
                            string.IsNullOrWhiteSpace(
                                numeroOF)
                                ? DBNull.Value
                                : numeroOF;

                        cmd.Parameters.Add(
                            "@Cantidad",
                            SqlDbType.Int).Value=
                            cantidad;

                        cmd.Parameters.Add(
                            "@UbicacionID",
                            SqlDbType.Int).Value=
                            ubicacionId;

                        cmd.Parameters.Add(
                            "@UsuarioID",
                            SqlDbType.Int).Value=
                            usuarioId.Value;

                        cmd.Parameters.Add(
                            "@Usuario",
                            SqlDbType.NVarChar,
                            120).Value=
                            usuarioTexto;

                        cmd.Parameters.Add(
                            "@Referencia",
                            SqlDbType.NVarChar,
                            120).Value=
                            $"CALIDAD-LIBRE-{id}";

                        await cmd.ExecuteNonQueryAsync(
                            cancellationToken);
                    }

                    const string updateLibreSql = @"
UPDATE dbo.Calidad_CajasInventarioLibre
SET Estado=N'ENVIADA_ALMACEN',
    FechaEnvioAlmacen=SYSDATETIME(),
    UsuarioEnvioAlmacenID=@UsuarioID,
    AlmacenPTCajaID=@CajaAlmacenID,
    UbicacionAlmacenID=@UbicacionID,
    FechaModificacion=SYSDATETIME()
WHERE CalidadCajaInventarioID=@ID
  AND Activo=1
  AND Estado=N'EN_CALIDAD'
  AND AlmacenPTCajaID IS NULL;";

                    await using (var cmd=
                        new SqlCommand(
                            updateLibreSql,
                            cn,
                            tx))
                    {
                        cmd.Parameters.Add(
                            "@UsuarioID",
                            SqlDbType.Int).Value=
                            usuarioId.Value;

                        cmd.Parameters.Add(
                            "@CajaAlmacenID",
                            SqlDbType.Int).Value=
                            cajaAlmacenId;

                        cmd.Parameters.Add(
                            "@UbicacionID",
                            SqlDbType.Int).Value=
                            ubicacionId;

                        cmd.Parameters.Add(
                            "@ID",
                            SqlDbType.BigInt).Value=
                            id;

                        if (await cmd.ExecuteNonQueryAsync(
                                cancellationToken)!=1)
                        {
                            throw new InvalidOperationException(
                                $"La caja {id} cambio de estado durante el envio.");
                        }
                    }

                    enviados++;
                    piezas+=cantidad;
                }

                await tx.CommitAsync(
                    cancellationToken);

                TempData["Mensaje"]=
                    $"Se enviaron {enviados} caja(s) / {piezas:N0} pieza(s) a Almacen PT.";
            }
            catch (Exception ex)
            {
                try {
                    await tx.RollbackAsync(
                        cancellationToken);
                }
                catch { }

                TempData["Error"]=
                    "No fue posible enviar las cajas a Almacen PT: "
                    + ex.Message;
            }

            return RedirectToAction(
                nameof(Index),
                new { grupo="CAJAS" });
        }
    }
}