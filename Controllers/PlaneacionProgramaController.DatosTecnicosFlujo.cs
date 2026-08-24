using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;

namespace ERP.NSQuell.Controllers;

public partial class PlaneacionProgramaController
{
    public sealed class DatosTecnicosParaProgramarRequest
    {
        public int ParteID { get; set; }
        public int ReleaseDetalleID { get; set; }

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

    // NSQ_DT_FLUJO_OPERATIVO_V2
    [HttpGet]
    public async Task<IActionResult> DatosTecnicosParaProgramar(
        int parteId,
        int releaseDetalleId)
    {
        if (parteId <= 0 || releaseDetalleId <= 0)
        {
            return BadRequest(new
            {
                ok = false,
                mensaje = "Parte o necesidad inválida."
            });
        }

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync();

        const string sql = @"
SELECT
    p.ParteID,
    p.NumeroParte,
    COALESCE(NULLIF(p.Designacion,N''),NULLIF(p.Descripcion,N''),p.NumeroParte) AS Descripcion,

    COALESCE(d.MaterialID,t.MaterialID) AS MaterialID,
    COALESCE(d.MaquinaSugeridaID,t.MaquinaPrincipalID) AS MaquinaPrincipalID,
    t.MaquinaSustitutaID,
    COALESCE(d.MoldeID,t.MoldePrincipalID) AS MoldePrincipalID,

    t.Color,
    t.Cavidades,
    t.ObjetivoHora,
    t.PiezasPorCaja,
    t.Ciclo,
    t.TipoSecado,
    t.HorasSecado,

    COALESCE(d.PesoBrutoPieza,t.PesoBrutoPieza) AS PesoBrutoPieza,
    t.PesoNetoPieza,

    COALESCE(NULLIF(d.EmbalajeCodigo,N''),t.EmbalajeCodigo) AS EmbalajeCodigo,
    COALESCE(NULLIF(d.EmbalajeDescripcion,N''),t.EmbalajeDescripcion) AS EmbalajeDescripcion,
    COALESCE(d.PiezasPorEmbalaje,t.PiezasPorEmbalaje) AS PiezasPorEmbalaje

FROM dbo.Planeacion_ReleaseDetalle d
INNER JOIN dbo.ERP_Partes p
    ON p.ParteID = d.ParteID
   AND p.Activo = 1
LEFT JOIN dbo.ERP_ParteDatosTecnicos t
    ON t.ParteID = p.ParteID
   AND t.Activo = 1
WHERE d.ReleaseDetalleID = @ReleaseDetalleID
  AND d.ParteID = @ParteID
  AND d.Activo = 1;";

        int? materialId = null;
        int? maquinaPrincipalId = null;
        int? maquinaSustitutaId = null;
        int? moldePrincipalId = null;
        string? color = null;
        int? cavidades = null;
        int? objetivoHora = null;
        int? piezasPorCaja = null;
        string? ciclo = null;
        string? tipoSecado = null;
        decimal? horasSecado = null;
        decimal? pesoBruto = null;
        decimal? pesoNeto = null;
        string? embalajeCodigo = null;
        string? embalajeDescripcion = null;
        decimal? piezasPorEmbalaje = null;
        string numeroParte;
        string descripcion;

        await using (var cmd = new SqlCommand(sql, cn))
        {
            cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value = parteId;
            cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value = releaseDetalleId;

            await using var rd = await cmd.ExecuteReaderAsync();

            if (!await rd.ReadAsync())
            {
                return NotFound(new
                {
                    ok = false,
                    mensaje = "La necesidad ya no está disponible."
                });
            }

            numeroParte = TextoDt(rd, "NumeroParte") ?? parteId.ToString();
            descripcion = TextoDt(rd, "Descripcion") ?? numeroParte;

            materialId = EnteroDt(rd, "MaterialID");
            maquinaPrincipalId = EnteroDt(rd, "MaquinaPrincipalID");
            maquinaSustitutaId = EnteroDt(rd, "MaquinaSustitutaID");
            moldePrincipalId = EnteroDt(rd, "MoldePrincipalID");

            color = TextoDt(rd, "Color");
            cavidades = EnteroDt(rd, "Cavidades");
            objetivoHora = EnteroDt(rd, "ObjetivoHora");
            piezasPorCaja = EnteroDt(rd, "PiezasPorCaja");
            ciclo = TextoDt(rd, "Ciclo");
            tipoSecado = TextoDt(rd, "TipoSecado");
            horasSecado = DecimalDt(rd, "HorasSecado");
            pesoBruto = DecimalDt(rd, "PesoBrutoPieza");
            pesoNeto = DecimalDt(rd, "PesoNetoPieza");
            embalajeCodigo = TextoDt(rd, "EmbalajeCodigo");
            embalajeDescripcion = TextoDt(rd, "EmbalajeDescripcion");
            piezasPorEmbalaje = DecimalDt(rd, "PiezasPorEmbalaje");
        }

        var faltantes = ObtenerFaltantesDt(
            materialId,
            maquinaPrincipalId,
            moldePrincipalId,
            cavidades,
            objetivoHora,
            ciclo,
            pesoBruto,
            embalajeCodigo,
            piezasPorEmbalaje);

        var maquinas = new List<object>();
        const string sqlMaquinas = @"
SELECT MaquinaID,Codigo,Nombre
FROM dbo.ERP_Maquinas
WHERE Activo = 1
  AND UPPER(REPLACE(ISNULL(Codigo,N''),N' ',N'')) <> N'1200T'
  AND UPPER(REPLACE(ISNULL(Nombre,N''),N' ',N'')) NOT LIKE N'%1200T%'
ORDER BY Codigo,Nombre;";

        await using (var cmd = new SqlCommand(sqlMaquinas, cn))
        await using (var rd = await cmd.ExecuteReaderAsync())
        {
            while (await rd.ReadAsync())
            {
                maquinas.Add(new
                {
                    id = Convert.ToInt32(rd["MaquinaID"]),
                    codigo = TextoDt(rd, "Codigo"),
                    nombre = TextoDt(rd, "Nombre")
                });
            }
        }

        var moldes = new List<object>();
        const string sqlMoldes = @"
SELECT MoldeID,CodigoMolde,NombreMolde
FROM dbo.ERP_Moldes
WHERE Activo = 1
ORDER BY CodigoMolde,NombreMolde;";

        await using (var cmd = new SqlCommand(sqlMoldes, cn))
        await using (var rd = await cmd.ExecuteReaderAsync())
        {
            while (await rd.ReadAsync())
            {
                moldes.Add(new
                {
                    id = Convert.ToInt32(rd["MoldeID"]),
                    codigo = TextoDt(rd, "CodigoMolde"),
                    nombre = TextoDt(rd, "NombreMolde")
                });
            }
        }

        var materiales = new List<object>();
        const string sqlMateriales = @"
SELECT MaterialID,Codigo,Nombre
FROM dbo.ERP_Materiales
WHERE Activo = 1
ORDER BY Codigo,Nombre;";

        await using (var cmd = new SqlCommand(sqlMateriales, cn))
        await using (var rd = await cmd.ExecuteReaderAsync())
        {
            while (await rd.ReadAsync())
            {
                materiales.Add(new
                {
                    id = Convert.ToInt32(rd["MaterialID"]),
                    codigo = TextoDt(rd, "Codigo"),
                    nombre = TextoDt(rd, "Nombre")
                });
            }
        }

        return Json(new
        {
            ok = true,
            faltantes,
            data = new
            {
                parteID = parteId,
                numeroParte,
                descripcion,
                materialID = materialId,
                maquinaPrincipalID = maquinaPrincipalId,
                maquinaSustitutaID = maquinaSustitutaId,
                moldePrincipalID = moldePrincipalId,
                color,
                cavidades,
                objetivoHora,
                piezasPorCaja,
                ciclo,
                tipoSecado,
                horasSecado,
                pesoBrutoPieza = pesoBruto,
                pesoNetoPieza = pesoNeto,
                embalajeCodigo,
                embalajeDescripcion,
                piezasPorEmbalaje
            },
            maquinas,
            moldes,
            materiales
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GuardarDatosTecnicosParaProgramar(
        [FromBody] DatosTecnicosParaProgramarRequest request)
    {
        if (request == null ||
            request.ParteID <= 0 ||
            request.ReleaseDetalleID <= 0)
        {
            return BadRequest(new
            {
                ok = false,
                mensaje = "No se recibió la parte o la necesidad."
            });
        }

        var faltantes = ObtenerFaltantesDt(
            request.MaterialID,
            request.MaquinaPrincipalID,
            request.MoldePrincipalID,
            request.Cavidades,
            request.ObjetivoHora,
            request.Ciclo,
            request.PesoBrutoPieza,
            request.EmbalajeCodigo,
            request.PiezasPorEmbalaje);

        if (faltantes.Count > 0)
        {
            return BadRequest(new
            {
                ok = false,
                mensaje = "Faltan datos para liberar la programación: " +
                          string.Join(", ", faltantes) + ".",
                faltantes
            });
        }

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync();

        await using var tx =
            (SqlTransaction)await cn.BeginTransactionAsync(
                IsolationLevel.Serializable);

        try
        {
            const string sqlDetalle = @"
SELECT COUNT(1)
FROM dbo.Planeacion_ReleaseDetalle WITH (UPDLOCK,HOLDLOCK)
WHERE ReleaseDetalleID = @ReleaseDetalleID
  AND ParteID = @ParteID
  AND Activo = 1
  AND ProgramaProduccionID IS NULL;";

            await using (var cmd = new SqlCommand(sqlDetalle, cn, tx))
            {
                cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value =
                    request.ReleaseDetalleID;
                cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value =
                    request.ParteID;

                if (Convert.ToInt32(await cmd.ExecuteScalarAsync()) != 1)
                {
                    throw new InvalidOperationException(
                        "La necesidad ya fue programada o ya no está activa.");
                }
            }

            string materialCodigo;
            string materialNombre;

            const string sqlMaterial = @"
SELECT Codigo,Nombre
FROM dbo.ERP_Materiales
WHERE MaterialID = @MaterialID
  AND Activo = 1;";

            await using (var cmd = new SqlCommand(sqlMaterial, cn, tx))
            {
                cmd.Parameters.Add("@MaterialID", SqlDbType.Int).Value =
                    request.MaterialID!.Value;

                await using var rd = await cmd.ExecuteReaderAsync();

                if (!await rd.ReadAsync())
                    throw new InvalidOperationException(
                        "El material seleccionado no está activo.");

                materialCodigo = TextoDt(rd, "Codigo") ?? string.Empty;
                materialNombre = TextoDt(rd, "Nombre") ?? materialCodigo;
            }

            string maquinaCodigo;
            string maquinaNombre;

            const string sqlMaquina = @"
SELECT Codigo,Nombre
FROM dbo.ERP_Maquinas
WHERE MaquinaID = @MaquinaID
  AND Activo = 1
  AND UPPER(REPLACE(ISNULL(Codigo,N''),N' ',N'')) <> N'1200T'
  AND UPPER(REPLACE(ISNULL(Nombre,N''),N' ',N'')) NOT LIKE N'%1200T%';";

            await using (var cmd = new SqlCommand(sqlMaquina, cn, tx))
            {
                cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value =
                    request.MaquinaPrincipalID!.Value;

                await using var rd = await cmd.ExecuteReaderAsync();

                if (!await rd.ReadAsync())
                    throw new InvalidOperationException(
                        "La máquina principal no está activa o corresponde a 1200T.");

                maquinaCodigo = TextoDt(rd, "Codigo") ?? string.Empty;
                maquinaNombre = TextoDt(rd, "Nombre") ?? maquinaCodigo;
            }

            if (request.MaquinaSustitutaID.HasValue)
            {
                await using var cmd = new SqlCommand(sqlMaquina, cn, tx);
                cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value =
                    request.MaquinaSustitutaID.Value;

                await using var rd = await cmd.ExecuteReaderAsync();

                if (!await rd.ReadAsync())
                    throw new InvalidOperationException(
                        "La máquina sustituta no está activa o corresponde a 1200T.");
            }

            string moldeCodigo;

            const string sqlMolde = @"
SELECT CodigoMolde
FROM dbo.ERP_Moldes
WHERE MoldeID = @MoldeID
  AND Activo = 1;";

            await using (var cmd = new SqlCommand(sqlMolde, cn, tx))
            {
                cmd.Parameters.Add("@MoldeID", SqlDbType.Int).Value =
                    request.MoldePrincipalID!.Value;

                var result = await cmd.ExecuteScalarAsync();

                if (result == null || result == DBNull.Value)
                    throw new InvalidOperationException(
                        "El molde seleccionado no está activo.");

                moldeCodigo = result.ToString() ?? string.Empty;
            }

            const string sqlGuardarMaestro = @"
IF EXISTS
(
    SELECT 1
    FROM dbo.ERP_ParteDatosTecnicos WITH (UPDLOCK,HOLDLOCK)
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

            await using (var cmd = new SqlCommand(sqlGuardarMaestro, cn, tx))
            {
                AgregarParametrosDt(
                    cmd,
                    request,
                    materialCodigo,
                    materialNombre);

                await cmd.ExecuteNonQueryAsync();
            }

            const string sqlSincronizarDetalle = @"
UPDATE dbo.Planeacion_ReleaseDetalle
SET
    MaterialID = @MaterialID,
    MaterialCodigo = @MaterialCodigo,
    MaterialDescripcion = @MaterialDescripcion,
    PesoBrutoPieza = @PesoBrutoPieza,
    EmbalajeCodigo = @EmbalajeCodigo,
    EmbalajeDescripcion = @EmbalajeDescripcion,
    PiezasPorEmbalaje = @PiezasPorEmbalaje,
    MoldeID = @MoldePrincipalID,
    MoldeCodigo = @MoldeCodigo,
    MaquinaSugeridaID = @MaquinaPrincipalID,
    MaquinaSugeridaCodigo = @MaquinaCodigo,
    MaquinaSugeridaNombre = @MaquinaNombre
WHERE ReleaseDetalleID = @ReleaseDetalleID
  AND ParteID = @ParteID
  AND Activo = 1
  AND ProgramaProduccionID IS NULL;";

            await using (var cmd =
                new SqlCommand(sqlSincronizarDetalle, cn, tx))
            {
                AgregarParametrosDt(
                    cmd,
                    request,
                    materialCodigo,
                    materialNombre);

                cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value =
                    request.ReleaseDetalleID;

                cmd.Parameters.Add("@MoldeCodigo", SqlDbType.NVarChar, 100).Value =
                    DbDt(moldeCodigo);

                cmd.Parameters.Add("@MaquinaCodigo", SqlDbType.NVarChar, 100).Value =
                    DbDt(maquinaCodigo);

                cmd.Parameters.Add("@MaquinaNombre", SqlDbType.NVarChar, 250).Value =
                    DbDt(maquinaNombre);

                await cmd.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();

            return Json(new
            {
                ok = true,
                mensaje =
                    "Datos técnicos guardados y necesidad actualizada. " +
                    "Continuando a Programar molde."
            });
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();

            return BadRequest(new
            {
                ok = false,
                mensaje =
                    "No fue posible guardar los datos técnicos: " +
                    ex.Message
            });
        }
    }

    private static List<string> ObtenerFaltantesDt(
        int? materialId,
        int? maquinaPrincipalId,
        int? moldePrincipalId,
        int? cavidades,
        int? objetivoHora,
        string? ciclo,
        decimal? pesoBruto,
        string? embalajeCodigo,
        decimal? piezasPorEmbalaje)
    {
        var faltantes = new List<string>();

        if (!materialId.HasValue)
            faltantes.Add("material");

        if (!maquinaPrincipalId.HasValue)
            faltantes.Add("máquina principal");

        if (!moldePrincipalId.HasValue)
            faltantes.Add("molde principal");

        if (!cavidades.HasValue || cavidades.Value <= 0)
            faltantes.Add("cavidades");

        if (!objetivoHora.HasValue || objetivoHora.Value <= 0)
            faltantes.Add("objetivo por hora");

        if (string.IsNullOrWhiteSpace(ciclo))
            faltantes.Add("ciclo");

        if (!pesoBruto.HasValue || pesoBruto.Value <= 0)
            faltantes.Add("peso bruto por pieza");

        if (string.IsNullOrWhiteSpace(embalajeCodigo))
            faltantes.Add("código de embalaje");

        if (!piezasPorEmbalaje.HasValue ||
            piezasPorEmbalaje.Value <= 0)
        {
            faltantes.Add("piezas por embalaje");
        }

        return faltantes;
    }

    private static void AgregarParametrosDt(
        SqlCommand cmd,
        DatosTecnicosParaProgramarRequest request,
        string materialCodigo,
        string materialNombre)
    {
        cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value =
            request.ParteID;

        cmd.Parameters.Add("@MaterialID", SqlDbType.Int).Value =
            request.MaterialID!.Value;

        cmd.Parameters.Add("@MaterialCodigo", SqlDbType.NVarChar, 100).Value =
            DbDt(materialCodigo);

        cmd.Parameters.Add("@MaterialDescripcion", SqlDbType.NVarChar, 250).Value =
            DbDt(materialNombre);

        cmd.Parameters.Add("@MaquinaPrincipalID", SqlDbType.Int).Value =
            request.MaquinaPrincipalID!.Value;

        cmd.Parameters.Add("@MaquinaSustitutaID", SqlDbType.Int).Value =
            (object?)request.MaquinaSustitutaID ?? DBNull.Value;

        cmd.Parameters.Add("@MoldePrincipalID", SqlDbType.Int).Value =
            request.MoldePrincipalID!.Value;

        cmd.Parameters.Add("@Color", SqlDbType.NVarChar, 100).Value =
            DbDt(request.Color);

        cmd.Parameters.Add("@Cavidades", SqlDbType.Int).Value =
            request.Cavidades!.Value;

        cmd.Parameters.Add("@ObjetivoHora", SqlDbType.Int).Value =
            request.ObjetivoHora!.Value;

        cmd.Parameters.Add("@PiezasPorCaja", SqlDbType.Int).Value =
            (object?)request.PiezasPorCaja ?? DBNull.Value;

        cmd.Parameters.Add("@Ciclo", SqlDbType.NVarChar, 100).Value =
            DbDt(request.Ciclo);

        cmd.Parameters.Add("@TipoSecado", SqlDbType.NVarChar, 100).Value =
            DbDt(request.TipoSecado);

        var horasSecado =
            cmd.Parameters.Add("@HorasSecado", SqlDbType.Decimal);

        horasSecado.Precision = 18;
        horasSecado.Scale = 4;
        horasSecado.Value =
            (object?)request.HorasSecado ?? DBNull.Value;

        var pesoBruto =
            cmd.Parameters.Add("@PesoBrutoPieza", SqlDbType.Decimal);

        pesoBruto.Precision = 18;
        pesoBruto.Scale = 6;
        pesoBruto.Value = request.PesoBrutoPieza!.Value;

        var pesoNeto =
            cmd.Parameters.Add("@PesoNetoPieza", SqlDbType.Decimal);

        pesoNeto.Precision = 18;
        pesoNeto.Scale = 6;
        pesoNeto.Value =
            (object?)request.PesoNetoPieza ?? DBNull.Value;

        cmd.Parameters.Add("@EmbalajeCodigo", SqlDbType.NVarChar, 100).Value =
            DbDt(request.EmbalajeCodigo);

        cmd.Parameters.Add("@EmbalajeDescripcion", SqlDbType.NVarChar, 250).Value =
            DbDt(request.EmbalajeDescripcion);

        var piezas =
            cmd.Parameters.Add("@PiezasPorEmbalaje", SqlDbType.Decimal);

        piezas.Precision = 18;
        piezas.Scale = 4;
        piezas.Value = request.PiezasPorEmbalaje!.Value;
    }

    private static object DbDt(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? DBNull.Value
            : value.Trim();

    private static string? TextoDt(
        SqlDataReader rd,
        string column)
        => rd[column] == DBNull.Value
            ? null
            : rd[column].ToString()?.Trim();

    private static int? EnteroDt(
        SqlDataReader rd,
        string column)
        => rd[column] == DBNull.Value
            ? null
            : Convert.ToInt32(rd[column]);

    private static decimal? DecimalDt(
        SqlDataReader rd,
        string column)
        => rd[column] == DBNull.Value
            ? null
            : Convert.ToDecimal(rd[column]);
}
