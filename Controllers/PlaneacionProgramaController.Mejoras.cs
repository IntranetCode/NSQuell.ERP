using ERP.NSQuell.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ERP.NSQuell.Controllers;

// NSQ_TODO_PLANEACION_PRODUCCION_V1
public partial class PlaneacionProgramaController
{
    public sealed class DatosTecnicosRapidosRequest
    {
        public int ParteID { get; set; }
        public int? MaterialID { get; set; }
        public int? MaquinaPrincipalID { get; set; }
        public int? MaquinaSustitutaID { get; set; }
        public int? MoldePrincipalID { get; set; }
        public string? Color { get; set; }
        public int? Cavidades { get; set; }
        public int? ObjetivoHora { get; set; }
        public int? PiezasPorCaja { get; set; }
        public string? Ciclo { get; set; }
        public string? TipoSecado { get; set; }
        public decimal? HorasSecado { get; set; }
        public decimal? PesoBrutoPieza { get; set; }
        public decimal? PesoNetoPieza { get; set; }
        public string? EmbalajeCodigo { get; set; }
        public string? EmbalajeDescripcion { get; set; }
        public decimal? PiezasPorEmbalaje { get; set; }
    }

    [HttpGet]
    public async Task<IActionResult> DatosTecnicosRapidos(int parteId)
    {
        if (parteId <= 0)
            return Json(new { ok = false, mensaje = "Parte inválida." });

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync();

        const string sql = @"
SELECT
    p.ParteID,
    p.NumeroParte,
    COALESCE(NULLIF(p.Designacion,N''), NULLIF(p.Descripcion,N''), p.NumeroParte) AS Descripcion,
    t.MaterialID,
    t.MaquinaPrincipalID,
    t.MaquinaSustitutaID,
    t.MoldePrincipalID,
    t.Color,
    t.Cavidades,
    t.ObjetivoHora,
    t.PiezasPorCaja,
    t.Ciclo,
    t.TipoSecado,
    t.HorasSecado,
    t.PesoBrutoPieza,
    t.PesoNetoPieza,
    t.EmbalajeCodigo,
    t.EmbalajeDescripcion,
    t.PiezasPorEmbalaje
FROM dbo.ERP_Partes p
LEFT JOIN dbo.ERP_ParteDatosTecnicos t
    ON t.ParteID = p.ParteID
   AND t.Activo = 1
WHERE p.ParteID = @ParteID
  AND p.Activo = 1;";

        object? data = null;

        await using (var cmd = new SqlCommand(sql, cn))
        {
            cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value = parteId;

            await using var rd = await cmd.ExecuteReaderAsync();
            if (!await rd.ReadAsync())
                return Json(new { ok = false, mensaje = "No se encontró la parte activa." });

            data = new
            {
                parteID = Convert.ToInt32(rd["ParteID"]),
                numeroParte = TextoNullableMejora(rd, "NumeroParte"),
                descripcion = TextoNullableMejora(rd, "Descripcion"),
                materialID = EnteroNullableMejora(rd, "MaterialID"),
                maquinaPrincipalID = EnteroNullableMejora(rd, "MaquinaPrincipalID"),
                maquinaSustitutaID = EnteroNullableMejora(rd, "MaquinaSustitutaID"),
                moldePrincipalID = EnteroNullableMejora(rd, "MoldePrincipalID"),
                color = TextoNullableMejora(rd, "Color"),
                cavidades = EnteroNullableMejora(rd, "Cavidades"),
                objetivoHora = EnteroNullableMejora(rd, "ObjetivoHora"),
                piezasPorCaja = EnteroNullableMejora(rd, "PiezasPorCaja"),
                ciclo = TextoNullableMejora(rd, "Ciclo"),
                tipoSecado = TextoNullableMejora(rd, "TipoSecado"),
                horasSecado = DecimalNullableMejora(rd, "HorasSecado"),
                pesoBrutoPieza = DecimalNullableMejora(rd, "PesoBrutoPieza"),
                pesoNetoPieza = DecimalNullableMejora(rd, "PesoNetoPieza"),
                embalajeCodigo = TextoNullableMejora(rd, "EmbalajeCodigo"),
                embalajeDescripcion = TextoNullableMejora(rd, "EmbalajeDescripcion"),
                piezasPorEmbalaje = DecimalNullableMejora(rd, "PiezasPorEmbalaje")
            };
        }

        var maquinas = new List<object>();
        const string sqlMaquinas = @"
SELECT MaquinaID, Codigo, Nombre
FROM dbo.ERP_Maquinas
WHERE Activo = 1
  AND UPPER(REPLACE(ISNULL(Codigo,N''),N' ',N'')) <> N'1200T'
  AND UPPER(REPLACE(ISNULL(Nombre,N''),N' ',N'')) NOT LIKE N'%1200T%'
ORDER BY Codigo, Nombre;";

        await using (var cmd = new SqlCommand(sqlMaquinas, cn))
        await using (var rd = await cmd.ExecuteReaderAsync())
        {
            while (await rd.ReadAsync())
            {
                maquinas.Add(new
                {
                    id = Convert.ToInt32(rd["MaquinaID"]),
                    codigo = TextoNullableMejora(rd, "Codigo"),
                    nombre = TextoNullableMejora(rd, "Nombre")
                });
            }
        }

        var moldes = new List<object>();
        const string sqlMoldes = @"
SELECT MoldeID, CodigoMolde, NombreMolde
FROM dbo.ERP_Moldes
WHERE Activo = 1
ORDER BY CodigoMolde;";

        await using (var cmd = new SqlCommand(sqlMoldes, cn))
        await using (var rd = await cmd.ExecuteReaderAsync())
        {
            while (await rd.ReadAsync())
            {
                moldes.Add(new
                {
                    id = Convert.ToInt32(rd["MoldeID"]),
                    codigo = TextoNullableMejora(rd, "CodigoMolde"),
                    nombre = TextoNullableMejora(rd, "NombreMolde")
                });
            }
        }

        var materiales = new List<object>();
        const string sqlMateriales = @"
SELECT MaterialID, Codigo, Nombre
FROM dbo.ERP_Materiales
WHERE Activo = 1
ORDER BY Codigo, Nombre;";

        await using (var cmd = new SqlCommand(sqlMateriales, cn))
        await using (var rd = await cmd.ExecuteReaderAsync())
        {
            while (await rd.ReadAsync())
            {
                materiales.Add(new
                {
                    id = Convert.ToInt32(rd["MaterialID"]),
                    codigo = TextoNullableMejora(rd, "Codigo"),
                    nombre = TextoNullableMejora(rd, "Nombre")
                });
            }
        }

        return Json(new
        {
            ok = true,
            data,
            maquinas,
            moldes,
            materiales
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GuardarDatosTecnicosRapidos(
        [FromBody] DatosTecnicosRapidosRequest request)
    {
        if (request == null || request.ParteID <= 0)
            return Json(new { ok = false, mensaje = "No se recibió la parte." });

        var errores = new List<string>();

        if (!request.MaterialID.HasValue) errores.Add("material");
        if (!request.MaquinaPrincipalID.HasValue) errores.Add("máquina principal");
        if (!request.MoldePrincipalID.HasValue) errores.Add("molde principal");
        if (!request.Cavidades.HasValue || request.Cavidades <= 0) errores.Add("cavidades");
        if (!request.ObjetivoHora.HasValue || request.ObjetivoHora <= 0) errores.Add("objetivo por hora");
        if (string.IsNullOrWhiteSpace(request.Ciclo)) errores.Add("ciclo");
        if (!request.PesoBrutoPieza.HasValue || request.PesoBrutoPieza <= 0) errores.Add("peso bruto");
        if (!request.PiezasPorEmbalaje.HasValue || request.PiezasPorEmbalaje <= 0) errores.Add("piezas por embalaje");

        if (errores.Count > 0)
        {
            return Json(new
            {
                ok = false,
                mensaje = "Completa: " + string.Join(", ", errores) + "."
            });
        }

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync();
        await using var tx =
            (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);

        try
        {
            const string sqlParte = @"
SELECT COUNT(1)
FROM dbo.ERP_Partes WITH (UPDLOCK, HOLDLOCK)
WHERE ParteID = @ParteID
  AND Activo = 1;";

            await using (var cmd = new SqlCommand(sqlParte, cn, tx))
            {
                cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value = request.ParteID;
                if (Convert.ToInt32(await cmd.ExecuteScalarAsync()) != 1)
                    throw new InvalidOperationException("La parte ya no está disponible.");
            }

            string? materialCodigo = null;
            string? materialNombre = null;

            const string sqlMaterial = @"
SELECT Codigo, Nombre
FROM dbo.ERP_Materiales
WHERE MaterialID = @MaterialID
  AND Activo = 1;";

            await using (var cmd = new SqlCommand(sqlMaterial, cn, tx))
            {
                cmd.Parameters.Add("@MaterialID", SqlDbType.Int).Value = request.MaterialID!.Value;
                await using var rd = await cmd.ExecuteReaderAsync();

                if (!await rd.ReadAsync())
                    throw new InvalidOperationException("El material seleccionado no está activo.");

                materialCodigo = TextoNullableMejora(rd, "Codigo");
                materialNombre = TextoNullableMejora(rd, "Nombre");
            }

            const string sqlValidarMaquina = @"
SELECT COUNT(1)
FROM dbo.ERP_Maquinas
WHERE MaquinaID = @MaquinaID
  AND Activo = 1
  AND UPPER(REPLACE(ISNULL(Codigo,N''),N' ',N'')) <> N'1200T'
  AND UPPER(REPLACE(ISNULL(Nombre,N''),N' ',N'')) NOT LIKE N'%1200T%';";

            foreach (var maquinaId in new[]
            {
                request.MaquinaPrincipalID,
                request.MaquinaSustitutaID
            }.Where(x => x.HasValue))
            {
                await using var cmd = new SqlCommand(sqlValidarMaquina, cn, tx);
                cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = maquinaId!.Value;

                if (Convert.ToInt32(await cmd.ExecuteScalarAsync()) != 1)
                    throw new InvalidOperationException(
                        "Una máquina seleccionada no está activa o corresponde a 1200T.");
            }

            const string sqlMolde = @"
SELECT COUNT(1)
FROM dbo.ERP_Moldes
WHERE MoldeID = @MoldeID
  AND Activo = 1;";

            await using (var cmd = new SqlCommand(sqlMolde, cn, tx))
            {
                cmd.Parameters.Add("@MoldeID", SqlDbType.Int).Value = request.MoldePrincipalID!.Value;

                if (Convert.ToInt32(await cmd.ExecuteScalarAsync()) != 1)
                    throw new InvalidOperationException("El molde seleccionado no está activo.");
            }

            const string sqlGuardar = @"
IF EXISTS
(
    SELECT 1
    FROM dbo.ERP_ParteDatosTecnicos WITH (UPDLOCK, HOLDLOCK)
    WHERE ParteID = @ParteID
)
BEGIN
    UPDATE dbo.ERP_ParteDatosTecnicos
    SET
        MaterialID = @MaterialID,
        MaterialCodigo = @MaterialCodigo,
        MaterialDescripcion = @MaterialDescripcion,
        MaquinaPrincipalID = @MaquinaPrincipalID,
        MaquinaSustitutaID = @MaquinaSustitutaID,
        MoldePrincipalID = @MoldePrincipalID,
        Color = @Color,
        Cavidades = @Cavidades,
        ObjetivoHora = @ObjetivoHora,
        PiezasPorCaja = @PiezasPorCaja,
        Ciclo = @Ciclo,
        TipoSecado = @TipoSecado,
        HorasSecado = @HorasSecado,
        PesoBrutoPieza = @PesoBrutoPieza,
        PesoNetoPieza = @PesoNetoPieza,
        EmbalajeCodigo = @EmbalajeCodigo,
        EmbalajeDescripcion = @EmbalajeDescripcion,
        PiezasPorEmbalaje = @PiezasPorEmbalaje,
        Activo = 1,
        FechaModificacion = GETDATE()
    WHERE ParteID = @ParteID;
END
ELSE
BEGIN
    INSERT INTO dbo.ERP_ParteDatosTecnicos
    (
        ParteID,
        MaterialID,
        MaterialCodigo,
        MaterialDescripcion,
        MaquinaPrincipalID,
        MaquinaSustitutaID,
        MoldePrincipalID,
        Color,
        Cavidades,
        ObjetivoHora,
        PiezasPorCaja,
        Ciclo,
        TipoSecado,
        HorasSecado,
        PesoBrutoPieza,
        PesoNetoPieza,
        EmbalajeCodigo,
        EmbalajeDescripcion,
        PiezasPorEmbalaje,
        Activo,
        FechaCreacion
    )
    VALUES
    (
        @ParteID,
        @MaterialID,
        @MaterialCodigo,
        @MaterialDescripcion,
        @MaquinaPrincipalID,
        @MaquinaSustitutaID,
        @MoldePrincipalID,
        @Color,
        @Cavidades,
        @ObjetivoHora,
        @PiezasPorCaja,
        @Ciclo,
        @TipoSecado,
        @HorasSecado,
        @PesoBrutoPieza,
        @PesoNetoPieza,
        @EmbalajeCodigo,
        @EmbalajeDescripcion,
        @PiezasPorEmbalaje,
        1,
        GETDATE()
    );
END;";

            await using (var cmd = new SqlCommand(sqlGuardar, cn, tx))
            {
                cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value = request.ParteID;
                cmd.Parameters.Add("@MaterialID", SqlDbType.Int).Value = request.MaterialID!.Value;
                cmd.Parameters.Add("@MaterialCodigo", SqlDbType.NVarChar, 100).Value =
                    (object?)materialCodigo ?? DBNull.Value;
                cmd.Parameters.Add("@MaterialDescripcion", SqlDbType.NVarChar, 250).Value =
                    (object?)materialNombre ?? DBNull.Value;
                cmd.Parameters.Add("@MaquinaPrincipalID", SqlDbType.Int).Value =
                    request.MaquinaPrincipalID!.Value;
                cmd.Parameters.Add("@MaquinaSustitutaID", SqlDbType.Int).Value =
                    (object?)request.MaquinaSustitutaID ?? DBNull.Value;
                cmd.Parameters.Add("@MoldePrincipalID", SqlDbType.Int).Value =
                    request.MoldePrincipalID!.Value;
                cmd.Parameters.Add("@Color", SqlDbType.NVarChar, 100).Value =
                    DbMejora(request.Color);
                cmd.Parameters.Add("@Cavidades", SqlDbType.Int).Value = request.Cavidades!.Value;
                cmd.Parameters.Add("@ObjetivoHora", SqlDbType.Int).Value = request.ObjetivoHora!.Value;
                cmd.Parameters.Add("@PiezasPorCaja", SqlDbType.Int).Value =
                    (object?)request.PiezasPorCaja ?? DBNull.Value;
                cmd.Parameters.Add("@Ciclo", SqlDbType.NVarChar, 100).Value =
                    DbMejora(request.Ciclo);
                cmd.Parameters.Add("@TipoSecado", SqlDbType.NVarChar, 100).Value =
                    DbMejora(request.TipoSecado);

                var horas = cmd.Parameters.Add("@HorasSecado", SqlDbType.Decimal);
                horas.Precision = 18;
                horas.Scale = 4;
                horas.Value = (object?)request.HorasSecado ?? DBNull.Value;

                var bruto = cmd.Parameters.Add("@PesoBrutoPieza", SqlDbType.Decimal);
                bruto.Precision = 18;
                bruto.Scale = 6;
                bruto.Value = request.PesoBrutoPieza!.Value;

                var neto = cmd.Parameters.Add("@PesoNetoPieza", SqlDbType.Decimal);
                neto.Precision = 18;
                neto.Scale = 6;
                neto.Value = (object?)request.PesoNetoPieza ?? DBNull.Value;

                cmd.Parameters.Add("@EmbalajeCodigo", SqlDbType.NVarChar, 100).Value =
                    DbMejora(request.EmbalajeCodigo);
                cmd.Parameters.Add("@EmbalajeDescripcion", SqlDbType.NVarChar, 250).Value =
                    DbMejora(request.EmbalajeDescripcion);

                var piezasEmb = cmd.Parameters.Add("@PiezasPorEmbalaje", SqlDbType.Decimal);
                piezasEmb.Precision = 18;
                piezasEmb.Scale = 4;
                piezasEmb.Value = request.PiezasPorEmbalaje!.Value;

                await cmd.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();

            return Json(new
            {
                ok = true,
                mensaje = "Datos técnicos guardados. Ya puedes programar la pieza."
            });
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            return Json(new
            {
                ok = false,
                mensaje = "No fue posible guardar datos técnicos: " + ex.Message
            });
        }
    }

    // Cantidad cliente NO se modifica. Solo redondea la cantidad a programar.
    private static int RedondearCantidadPorEmbalaje(
        int cantidad,
        decimal? piezasPorEmbalaje)
    {
        if (cantidad <= 0)
            return 0;

        if (!piezasPorEmbalaje.HasValue || piezasPorEmbalaje.Value <= 0)
            return cantidad;

        var paquetes = Math.Ceiling(cantidad / piezasPorEmbalaje.Value);
        var redondeada = paquetes * piezasPorEmbalaje.Value;

        if (redondeada > int.MaxValue)
            throw new InvalidOperationException(
                "La cantidad redondeada por embalaje supera el límite permitido.");

        return Convert.ToInt32(
            Math.Ceiling(redondeada));
    }

    // NSQ_LHRH_PROGRAMACION_CONJUNTA_V3
    private sealed class ParejaLhRhVistaCandidata
    {
        public int ReleaseDetalleID { get; set; }
        public int ParteID { get; set; }
        public string NumeroParte { get; set; } = string.Empty;
        public string TextoParte { get; set; } = string.Empty;
        public int CantidadRequerida { get; set; }
    }

    private async Task PrepararParejaLhRhVistaAsync(
        PlaneacionProgramaCrearDesdeNecesidadVm principal)
    {
        principal.ParejaLhRhDisponible = false;
        principal.ProgramarParejaLhRh = true;
        principal.ParejaLhRhReleaseDetalleID = null;
        principal.ParejaLhRhLado = null;
        principal.ParejaLhRhNumeroParte = null;
        principal.ParejaLhRhDescripcion = null;
        principal.ParejaLhRhCantidadRequerida = 0;

        if (!principal.ParteID.HasValue ||
            !principal.ClienteID.HasValue ||
            !principal.ReleaseID.HasValue ||
            principal.ReleaseDetalleID <= 0)
        {
            return;
        }

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync();

        var textoPrincipal = await ObtenerTextoParteLhRhAsync(
            principal.ParteID.Value,
            cn,
            null);

        if (!TrySepararLhRh(
                textoPrincipal,
                out var basePrincipal,
                out var ladoPrincipal))
        {
            return;
        }

        var ladoBuscado = ladoPrincipal == "LH" ? "RH" : "LH";

        const string sql = @"
DECLARE @FechaPrincipal DATE =
(
    SELECT TOP (1) FechaRequerida
    FROM dbo.Planeacion_ReleaseDetalle
    WHERE ReleaseDetalleID = @ReleaseDetallePrincipalID
      AND Activo = 1
);

SELECT
    d.ReleaseDetalleID,
    d.ParteID,
    p.NumeroParte,
    COALESCE(NULLIF(p.Designacion,N''), NULLIF(p.Descripcion,N''), d.DesignacionDescripcionSAP, p.NumeroParte) AS TextoParte,
    ISNULL(d.CantidadRequerida,0) AS CantidadRequerida
FROM dbo.Planeacion_ReleaseDetalle d
INNER JOIN dbo.ERP_Partes p
    ON p.ParteID = d.ParteID
   AND p.Activo = 1
WHERE d.ReleaseID = @ReleaseID
  AND d.ReleaseDetalleID <> @ReleaseDetallePrincipalID
  AND d.Activo = 1
  AND d.ProgramaProduccionID IS NULL
  AND d.EstatusID NOT IN (9,99)
  AND p.ClienteID = @ClienteID
ORDER BY
    CASE WHEN CONVERT(date,d.FechaRequerida) = @FechaPrincipal THEN 0 ELSE 1 END,
    ABS(DATEDIFF(DAY,@FechaPrincipal,d.FechaRequerida)),
    d.ReleaseDetalleID;";

        var candidatas = new List<ParejaLhRhVistaCandidata>();

        await using (var cmd = new SqlCommand(sql, cn))
        {
            cmd.Parameters.Add("@ReleaseDetallePrincipalID", SqlDbType.Int).Value =
                principal.ReleaseDetalleID;
            cmd.Parameters.Add("@ReleaseID", SqlDbType.Int).Value =
                principal.ReleaseID.Value;
            cmd.Parameters.Add("@ClienteID", SqlDbType.Int).Value =
                principal.ClienteID.Value;

            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                var texto = TextoNullableMejora(rd, "TextoParte");

                if (!TrySepararLhRh(texto, out var baseCandidata, out var lado))
                    continue;

                if (lado != ladoBuscado ||
                    !string.Equals(baseCandidata, basePrincipal, StringComparison.Ordinal))
                {
                    continue;
                }

                candidatas.Add(new ParejaLhRhVistaCandidata
                {
                    ReleaseDetalleID = Convert.ToInt32(rd["ReleaseDetalleID"]),
                    ParteID = Convert.ToInt32(rd["ParteID"]),
                    NumeroParte = TextoNullableMejora(rd, "NumeroParte") ?? string.Empty,
                    TextoParte = texto ?? string.Empty,
                    CantidadRequerida = Convert.ToInt32(rd["CantidadRequerida"])
                });
            }
        }

        var partes = candidatas
            .GroupBy(x => x.ParteID)
            .ToList();

        // Si hay mas de una parte maestra posible no se adivina.
        if (partes.Count != 1)
            return;

        // La consulta ya prioriza misma fecha de entrega y luego la mas cercana.
        var seleccionada = partes[0].First();

        principal.ParejaLhRhDisponible = true;
        principal.ProgramarParejaLhRh = true;
        principal.ParejaLhRhReleaseDetalleID = seleccionada.ReleaseDetalleID;
        principal.ParejaLhRhLado = ladoBuscado;
        principal.ParejaLhRhNumeroParte = seleccionada.NumeroParte;
        principal.ParejaLhRhDescripcion = seleccionada.TextoParte;
        principal.ParejaLhRhCantidadRequerida = seleccionada.CantidadRequerida;
    }

    private async Task<int?> ProgramarParejaLhRhAsync(
        int programaPrincipalId,
        PlaneacionProgramaCrearDesdeNecesidadVm principal,
        int usuarioId,
        SqlConnection cn,
        SqlTransaction tx)
    {
        if (!principal.ParteID.HasValue ||
            !principal.ClienteID.HasValue ||
            !principal.ReleaseID.HasValue ||
            principal.ReleaseID.Value <= 0 ||
            principal.ReleaseDetalleID <= 0)
        {
            return null;
        }

        var textoPrincipal = await ObtenerTextoParteLhRhAsync(
            principal.ParteID.Value,
            cn,
            tx);

        if (!TrySepararLhRh(
                textoPrincipal,
                out var basePrincipal,
                out var ladoPrincipal))
        {
            return null;
        }

        var ladoBuscado = ladoPrincipal == "LH" ? "RH" : "LH";
        int? parteParejaId = null;

        const string sqlPartes = @"
SELECT
    ParteID,
    COALESCE(NULLIF(Designacion,N''), NULLIF(Descripcion,N''), NumeroParte) AS TextoParte
FROM dbo.ERP_Partes
WHERE ClienteID = @ClienteID
  AND Activo = 1
  AND ParteID <> @ParteID
  AND EXISTS
  (
      SELECT 1
      FROM dbo.Planeacion_ReleaseDetalle d
      WHERE d.ReleaseID = @ReleaseID
        AND d.ParteID = dbo.ERP_Partes.ParteID
        AND d.Activo = 1
        AND d.ProgramaProduccionID IS NULL
        AND d.EstatusID NOT IN (9,99)
  )
ORDER BY ParteID;";

        await using (var cmd = new SqlCommand(sqlPartes, cn, tx))
        {
            cmd.Parameters.Add("@ClienteID", SqlDbType.Int).Value =
                principal.ClienteID.Value;
            cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value =
                principal.ParteID.Value;
            cmd.Parameters.Add("@ReleaseID", SqlDbType.Int).Value =
                principal.ReleaseID.Value;

            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                var texto = TextoNullableMejora(rd, "TextoParte");

                if (!TrySepararLhRh(texto, out var baseCandidata, out var lado))
                    continue;

                if (lado == ladoBuscado &&
                    string.Equals(
                        baseCandidata,
                        basePrincipal,
                        StringComparison.Ordinal))
                {
                    if (parteParejaId.HasValue)
                    {
                        throw new InvalidOperationException(
                            "Se encontraron varias candidatas LH/RH para la misma designación. " +
                            "Corrige el catálogo antes de programar.");
                    }

                    parteParejaId = Convert.ToInt32(rd["ParteID"]);
                }
            }
        }

        if (!parteParejaId.HasValue)
            return null;

        int? releaseDetalleParejaId = null;

        const string sqlDetallePareja = @"
DECLARE @FechaPrincipal DATE =
(
    SELECT TOP (1) FechaRequerida
    FROM dbo.Planeacion_ReleaseDetalle
    WHERE ReleaseDetalleID = @ReleaseDetallePrincipalID
      AND Activo = 1
);

SELECT TOP (1)
    d.ReleaseDetalleID
FROM dbo.Planeacion_ReleaseDetalle d
WHERE d.ReleaseID = @ReleaseID
  AND d.ParteID = @ParteParejaID
  AND d.Activo = 1
  AND d.ProgramaProduccionID IS NULL
  AND d.EstatusID NOT IN (9,99)
ORDER BY
    CASE WHEN CONVERT(date,d.FechaRequerida) = @FechaPrincipal THEN 0 ELSE 1 END,
    ABS(DATEDIFF(DAY,@FechaPrincipal,d.FechaRequerida)),
    d.ReleaseDetalleID;";

        await using (var cmd = new SqlCommand(sqlDetallePareja, cn, tx))
        {
            cmd.Parameters.Add("@ReleaseDetallePrincipalID", SqlDbType.Int).Value =
                principal.ReleaseDetalleID;
            cmd.Parameters.Add("@ReleaseID", SqlDbType.Int).Value =
                principal.ReleaseID.Value;
            cmd.Parameters.Add("@ParteParejaID", SqlDbType.Int).Value =
                parteParejaId.Value;

            var result = await cmd.ExecuteScalarAsync();

            if (result != null && result != DBNull.Value)
                releaseDetalleParejaId = Convert.ToInt32(result);
        }

        if (!releaseDetalleParejaId.HasValue)
            return null;

        var pareja = await ObtenerNecesidadParaProgramaAsync(
            releaseDetalleParejaId.Value,
            cn,
            tx);

        if (pareja == null || pareja.PiezasAProducir <= 0)
            return null;

        if (principal.MoldeID.HasValue &&
            pareja.MoldeID.HasValue &&
            principal.MoldeID.Value != pareja.MoldeID.Value)
        {
            throw new InvalidOperationException(
                "La contraparte LH/RH no tiene configurado el mismo molde. " +
                "Corrige los datos tecnicos antes de programarlas juntas.");
        }

        if (principal.MaquinaID.HasValue)
        {
            var maquinaCompatiblePareja = await MaquinaCompatibleConParteAsync(
                pareja.ParteID,
                principal.MaquinaID.Value,
                cn,
                tx);

            if (!maquinaCompatiblePareja)
            {
                throw new InvalidOperationException(
                    "La maquina seleccionada para la parte principal no esta configurada " +
                    "como principal o sustituta directa de la contraparte LH/RH.");
            }
        }

        var cantidadObjetivoPareja =
            RedondearCantidadPorEmbalaje(
                pareja.CantidadOriginalAProducir > 0
                    ? pareja.CantidadOriginalAProducir
                    : pareja.PiezasAProducir +
                      pareja.ProductoIncompletoApartado,
                pareja.PiezasPorEmbalaje);

        pareja.CantidadProgramada =
            Math.Max(
                0,
                cantidadObjetivoPareja -
                pareja.ProductoIncompletoApartado);

        if (pareja.PiezasPorEmbalaje.HasValue &&
            pareja.PiezasPorEmbalaje.Value > 0)
        {
            pareja.CantidadEmbalajes = Math.Ceiling(
                (pareja.CantidadProgramada +
                 pareja.ProductoIncompletoApartado) /
                pareja.PiezasPorEmbalaje.Value);
        }

        if (pareja.PesoBrutoPieza.HasValue &&
            pareja.PesoBrutoPieza.Value > 0)
        {
            pareja.CantidadMpKg = Math.Round(
                pareja.CantidadProgramada *
                pareja.PesoBrutoPieza.Value,
                4);
        }

        await CompletarDatosProgramaAsync(pareja, cn, tx);
        await CompletarVinculoOFExistenteAsync(pareja, cn, tx);

        // Pareja física: mismo molde, máquina y ventana de tiempo.
        pareja.MaquinaID = principal.MaquinaID;
        pareja.MaquinaCodigo = principal.MaquinaCodigo;
        pareja.MaquinaNombre = principal.MaquinaNombre;
        pareja.MoldeID = principal.MoldeID;
        pareja.MoldeCodigo = principal.MoldeCodigo;
        pareja.FechaInicioProgramada = principal.FechaInicioProgramada;
        pareja.FechaFinProgramada = principal.FechaFinProgramada;
        pareja.HorasProgramadas = principal.HorasProgramadas;
        pareja.Cambio = principal.Cambio;
        pareja.Arranque = principal.Arranque;
        pareja.CondicionProduccion = principal.CondicionProduccion;

        var programaParejaId =
            await InsertarProgramaAsync(
                pareja,
                usuarioId,
                cn,
                tx);

        // Intencionalmente NO asignamos operadores en Planeación.
        await MarcarReleaseDetalleProgramadoAsync(
            pareja.ReleaseDetalleID,
            programaParejaId,
            usuarioId,
            cn,
            tx);

        if (pareja.SolicitudProduccionID.HasValue &&
            pareja.SolicitudProduccionDetalleID.HasValue)
        {
            await VincularOFManualConProgramaAsync(
                programaParejaId,
                pareja,
                usuarioId,
                cn,
                tx);
        }

        var grupo = Math.Min(
            programaPrincipalId,
            programaParejaId);

        const string sqlMarca = @"
UPDATE dbo.Planeacion_ProgramaProduccion
SET
    Observaciones =
        LEFT(
            CONCAT(
                N'NSQ_LHRH_PAIR:', @Grupo, N'; ',
                ISNULL(Observaciones,N'')
            ),
            500
        ),
    UsuarioModificacionID = @UsuarioID,
    FechaModificacion = GETDATE()
WHERE ProgramaProduccionID IN
(
    @ProgramaPrincipalID,
    @ProgramaParejaID
);";

        await using (var cmd = new SqlCommand(sqlMarca, cn, tx))
        {
            cmd.Parameters.Add("@Grupo", SqlDbType.Int).Value = grupo;
            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
            cmd.Parameters.Add("@ProgramaPrincipalID", SqlDbType.Int).Value =
                programaPrincipalId;
            cmd.Parameters.Add("@ProgramaParejaID", SqlDbType.Int).Value =
                programaParejaId;

            await cmd.ExecuteNonQueryAsync();
        }

        principal.Observaciones =
            string.IsNullOrWhiteSpace(principal.Observaciones)
                ? $"Pareja LH/RH programada automáticamente: Programa #{programaParejaId}."
                : principal.Observaciones.Trim() +
                  Environment.NewLine +
                  $"Pareja LH/RH programada automáticamente: Programa #{programaParejaId}.";

        return programaParejaId;
    }

    private static async Task<string?> ObtenerTextoParteLhRhAsync(
        int parteId,
        SqlConnection cn,
        SqlTransaction? tx)
    {
        const string sql = @"
SELECT
    COALESCE(NULLIF(Designacion,N''), NULLIF(Descripcion,N''), NumeroParte)
FROM dbo.ERP_Partes
WHERE ParteID = @ParteID
  AND Activo = 1;";

        await using var cmd = tx == null
            ? new SqlCommand(sql, cn)
            : new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value = parteId;

        var result = await cmd.ExecuteScalarAsync();

        return result == null || result == DBNull.Value
            ? null
            : result.ToString();
    }

    private static bool TrySepararLhRh(
        string? value,
        out string baseKey,
        out string lado)
    {
        baseKey = string.Empty;
        lado = string.Empty;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        var upper = value.Trim().ToUpperInvariant();

        // Una referencia que literalmente indica LH/RH es una sola parte.
        if (upper.Contains("LH/RH", StringComparison.Ordinal) ||
            upper.Contains("RH/LH", StringComparison.Ordinal))
        {
            return false;
        }

        var matches = Regex.Matches(
            upper,
            @"(?<![A-Z0-9])(?<lado>LH|RH)(?![A-Z0-9])",
            RegexOptions.CultureInvariant);

        // Debe existir exactamente un lado aislado. Si trae ambos lados
        // o una expresión combinada, no se trata como pareja separada.
        if (matches.Count != 1)
            return false;

        var match = matches[0];
        lado = match.Groups["lado"].Value;

        var baseText =
            upper.Remove(match.Index, match.Length);

        baseKey = NormalizarTextoLhRh(baseText);

        return baseKey.Length >= 3;
    }

    private static string NormalizarTextoLhRh(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);

        foreach (var ch in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);

            if (category == UnicodeCategory.NonSpacingMark)
                continue;

            if (char.IsLetterOrDigit(ch))
                sb.Append(char.ToUpperInvariant(ch));
        }

        return sb.ToString();
    }

    private static object DbMejora(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? DBNull.Value
            : value.Trim();

    private static string? TextoNullableMejora(
        SqlDataReader rd,
        string column)
        => rd[column] == DBNull.Value
            ? null
            : rd[column].ToString()?.Trim();

    private static int? EnteroNullableMejora(
        SqlDataReader rd,
        string column)
        => rd[column] == DBNull.Value
            ? null
            : Convert.ToInt32(rd[column]);

    private static decimal? DecimalNullableMejora(
        SqlDataReader rd,
        string column)
        => rd[column] == DBNull.Value
            ? null
            : Convert.ToDecimal(rd[column]);
}
