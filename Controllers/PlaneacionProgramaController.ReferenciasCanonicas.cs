using ERP.NSQuell.Models;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;

namespace ERP.NSQuell.Controllers;

public partial class PlaneacionProgramaController
{
    // NSQ_SLIDING_CAM_DATOS_CANONICOS_V1
    private sealed class DatosCanonicosParteV1
    {
        public string? NumeroParte { get; init; }
        public string? ReferenciaSapMaestro { get; init; }
        public string? ReferenciaSapHistorica { get; init; }
        public string? ReferenciaCliente { get; init; }
        public string? TipoSecado { get; init; }
        public decimal? HorasSecado { get; init; }
        public string? HorasSecadoTexto { get; init; }
    }

    private async Task AplicarDatosCanonicosProgramaAsync(PlaneacionProgramaCrearDesdeNecesidadVm vm)
    {
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync();
        await AplicarDatosCanonicosProgramaAsync(vm, cn, null);
    }

    private async Task AplicarDatosCanonicosProgramaAsync(PlaneacionProgramaCrearDesdeNecesidadVm vm, SqlConnection cn, SqlTransaction? tx)
    {
        if (!vm.ParteID.HasValue || vm.ParteID.Value <= 0) return;
        var d = await CargarDatosCanonicosParteV1Async(vm.ParteID.Value, cn, tx);
        if (d == null) return;

        if (ReferenciaValidaV1(d.NumeroParte)) vm.NumeroParte = d.NumeroParte!.Trim();
        vm.ReferenciaSAP = ResolverReferenciaCanonicaV1(vm.ReferenciaSAP,d.ReferenciaSapHistorica,d.ReferenciaSapMaestro,vm.NumeroParte);
        if (ReferenciaValidaV1(d.ReferenciaCliente)) vm.ReferenciaCliente = d.ReferenciaCliente!.Trim();

        var texto = !string.IsNullOrWhiteSpace(vm.HorasSecadoTexto) ? vm.HorasSecadoTexto : d.HorasSecadoTexto;
        vm.HorasSecadoTexto = texto;
        vm.TipoSecado = ResolverTipoSecadoCanonicoV1(ReferenciaValidaV1(vm.TipoSecado) ? vm.TipoSecado : d.TipoSecado,texto);
        vm.HorasSecado = ResolverHorasSecadoCanonicasV1(vm.HorasSecado ?? d.HorasSecado,texto);
    }

    private async Task<DatosCanonicosParteV1?> CargarDatosCanonicosParteV1Async(int parteId, SqlConnection cn, SqlTransaction? tx)
    {
        const string sql = @"
SELECT TOP(1)
    p.NumeroParte,
    p.ReferenciaSAP AS ReferenciaSapMaestro,
    hist.ReferenciaSAP AS ReferenciaSapHistorica,
    hcc.NumeroParteFuente AS ReferenciaCliente,
    COALESCE(NULLIF(LTRIM(RTRIM(dt.TipoSecado)),N''),NULLIF(LTRIM(RTRIM(hcc.TipoSecado)),N'')) AS TipoSecado,
    COALESCE(dt.HorasSecado,hcc.HorasSecado) AS HorasSecado,
    COALESCE(NULLIF(LTRIM(RTRIM(dt.HorasSecadoTexto)),N''),NULLIF(LTRIM(RTRIM(hcc.TiempoSecadoTexto)),N'')) AS HorasSecadoTexto
FROM dbo.ERP_Partes p
OUTER APPLY
(
    SELECT TOP(1) rr.ReferenciaSAP
    FROM dbo.Planeacion_ReleaseRenglones rr
    WHERE rr.ParteID=p.ParteID
      AND rr.ReferenciaSAP IS NOT NULL
      AND LTRIM(RTRIM(rr.ReferenciaSAP))<>N''
      AND UPPER(REPLACE(REPLACE(LTRIM(RTRIM(rr.ReferenciaSAP)),N' ',N''),N'.',N'')) NOT IN(N'N/A',N'NA',N'-')
    ORDER BY CASE WHEN rr.Activo=1 THEN 0 ELSE 1 END,
             COALESCE(rr.FechaModificacion,rr.FechaCreacion) DESC,
             rr.ReleaseRenglonID DESC
) hist
OUTER APPLY
(
    SELECT TOP(1) h.NumeroParteFuente,h.TipoSecado,h.HorasSecado,h.TiempoSecadoTexto
    FROM dbo.Calidad_HCC_PlantillaPartes hp
    INNER JOIN dbo.Calidad_HCC_Plantillas h ON h.PlantillaHCCID=hp.PlantillaHCCID AND h.Activo=1
    WHERE hp.ParteID=p.ParteID AND hp.Activo=1
    ORDER BY h.EsVigente DESC,hp.EsPrincipal DESC,h.FechaModificacionFormato DESC,h.PlantillaHCCID DESC
) hcc
OUTER APPLY
(
    SELECT TOP(1) x.TipoSecado,x.HorasSecado,x.HorasSecadoTexto
    FROM dbo.ERP_ParteDatosTecnicos x
    WHERE x.ParteID=p.ParteID AND x.Activo=1
    ORDER BY CASE WHEN x.FechaModificacion IS NULL THEN 1 ELSE 0 END,x.FechaModificacion DESC,x.ParteDatoTecnicoID DESC
) dt
WHERE p.ParteID=@ParteID AND p.Activo=1;";

        await using var cmd = tx == null ? new SqlCommand(sql,cn) : new SqlCommand(sql,cn,tx);
        cmd.Parameters.Add("@ParteID",SqlDbType.Int).Value=parteId;
        await using var rd=await cmd.ExecuteReaderAsync();
        if (!await rd.ReadAsync()) return null;
        return new DatosCanonicosParteV1
        {
            NumeroParte=TextoCanonicoV1(rd,"NumeroParte"),
            ReferenciaSapMaestro=TextoCanonicoV1(rd,"ReferenciaSapMaestro"),
            ReferenciaSapHistorica=TextoCanonicoV1(rd,"ReferenciaSapHistorica"),
            ReferenciaCliente=TextoCanonicoV1(rd,"ReferenciaCliente"),
            TipoSecado=TextoCanonicoV1(rd,"TipoSecado"),
            HorasSecado=rd["HorasSecado"]==DBNull.Value ? null : Convert.ToDecimal(rd["HorasSecado"]),
            HorasSecadoTexto=TextoCanonicoV1(rd,"HorasSecadoTexto")
        };
    }

    private static string? TextoCanonicoV1(SqlDataReader rd,string campo) => rd[campo]==DBNull.Value ? null : rd[campo]?.ToString()?.Trim();

    private static bool ReferenciaValidaV1(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var n=value.Trim().ToUpperInvariant().Replace(" ",string.Empty).Replace(".",string.Empty);
        return n is not "N/A" and not "NA" and not "-";
    }

    private static string? ResolverReferenciaCanonicaV1(params string?[] values)
    {
        foreach(var value in values) if (ReferenciaValidaV1(value)) return value!.Trim();
        return null;
    }

    private static string? ResolverTipoSecadoCanonicoV1(string? tipo,string? texto)
    {
        if (ReferenciaValidaV1(tipo)) return tipo!.Trim();
        if (string.IsNullOrWhiteSpace(texto)) return null;
        var t=texto.ToUpperInvariant();
        if (t.Contains("DESHUM") || t.Contains("DESUM")) return "DESHUMIFICADOR";
        if (t.Contains("SECADOR")) return "SECADOR";
        // No inferir el equipo solo porque el texto tenga horas.
        // Sliding Cam se sanea en SQL usando la evidencia historica del mismo molde/material.
        return null;
    }

    private static decimal? ResolverHorasSecadoCanonicasV1(decimal? raw,string? texto)
    {
        // 6±2 HORAS => nominal 6; 2 es tolerancia.
        if (!string.IsNullOrWhiteSpace(texto))
        {
            var m=Regex.Match(texto,@"(?<!\d)(?<nom>\d+(?:[\.,]\d+)?)\s*(?:±|\+)\s*\d+",RegexOptions.CultureInvariant|RegexOptions.IgnoreCase);
            if (m.Success)
            {
                var s=m.Groups["nom"].Value.Replace(',','.');
                if (decimal.TryParse(s,NumberStyles.Number,CultureInfo.InvariantCulture,out var nominal) && nominal>0) return nominal;
            }
        }
        return raw.HasValue && raw.Value>0 ? raw : null;
    }
}