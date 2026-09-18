using ERP.NSQuell.Models;
using Microsoft.Data.SqlClient;
using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace ERP.NSQuell.Controllers;

public sealed partial class ProduccionOperativaController
{
    private sealed class DisponibilidadTiempoExtraOperativo
    {
        public bool Mostrar { get; set; }
        public bool TieneSesionActiva { get; set; }
        public DateTime? FechaObjetivo { get; set; }
        public bool CorteVencido { get; set; }
        public int MinutosDesfase { get; set; }
        public string Descripcion { get; set; } = string.Empty;
        public string TextoBoton { get; set; } = "Ver tiempo extra";
    }

    private async Task<DisponibilidadTiempoExtraOperativo> ObtenerDisponibilidadTiempoExtraOperativoAsync(
        ProduccionCentroOperativoVm vm,
        SqlConnection cn,
        CancellationToken cancellationToken)
    {
        var resultado = new DisponibilidadTiempoExtraOperativo();
        if (!vm.EjecucionProduccionID.HasValue || vm.EjecucionProduccionID.Value <= 0)
            return resultado;

        const string sql = @"
SELECT TOP(1)
    e.EstatusID,
    e.FechaLiberacionMaquina,
    CASE WHEN EXISTS
    (
        SELECT 1
        FROM dbo.Produccion_Paros p
        WHERE p.EjecucionProduccionID=e.EjecucionProduccionID
          AND p.Activo=1
          AND p.FechaFinParo IS NULL
    ) THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS TieneParoAbierto,
    te.TiempoExtraID,
    te.FechaHoraInicio,
    te.FechaHoraUltimoCorte,
    te.Estado,
    te.Motivo
FROM dbo.Produccion_Ejecucion e
OUTER APPLY
(
    SELECT TOP(1)
        x.TiempoExtraID,
        x.FechaHoraInicio,
        x.FechaHoraUltimoCorte,
        x.Estado,
        x.Motivo
    FROM dbo.Produccion_TiempoExtra x
    WHERE x.EjecucionProduccionID=e.EjecucionProduccionID
      AND x.Activo=1
      AND x.FechaHoraFin IS NULL
      AND UPPER(LTRIM(RTRIM(x.Estado))) IN(N'EN_CURSO',N'PAUSADO')
    ORDER BY x.TiempoExtraID DESC
) te
WHERE e.EjecucionProduccionID=@EjecucionProduccionID
  AND e.Activo=1;";

        int estatusId;
        bool maquinaLiberada;
        bool tieneParoAbierto;
        int? tiempoExtraId;
        DateTime? ultimoCorte = null;
        string? estado = null;
        string? motivo = null;

        await using (var cmd = new SqlCommand(sql, cn))
        {
            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = vm.EjecucionProduccionID.Value;
            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
            if (!await rd.ReadAsync(cancellationToken)) return resultado;

            estatusId = Convert.ToInt32(rd["EstatusID"]);
            maquinaLiberada = rd["FechaLiberacionMaquina"] != DBNull.Value;
            tieneParoAbierto = Convert.ToBoolean(rd["TieneParoAbierto"]);
            tiempoExtraId = rd["TiempoExtraID"] == DBNull.Value ? null : Convert.ToInt32(rd["TiempoExtraID"]);
            ultimoCorte = rd["FechaHoraUltimoCorte"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaHoraUltimoCorte"]);
            estado = rd["Estado"] == DBNull.Value ? null : rd["Estado"]?.ToString()?.Trim();
            motivo = rd["Motivo"] == DBNull.Value ? null : rd["Motivo"]?.ToString()?.Trim();
        }

        var ahora = DateTime.Now;

        if (tiempoExtraId.HasValue)
        {
            resultado.Mostrar = true;
            resultado.TieneSesionActiva = true;
            resultado.TextoBoton = "Atender tiempo extra";
            resultado.Descripcion = string.Equals(estado, ProduccionTiempoExtraEstado.Pausado, StringComparison.OrdinalIgnoreCase)
                ? "Existe una sesion de tiempo extra abierta en estado Pausado. Consulta su estado antes de continuar."
                : $"Tiempo extra en curso{(string.IsNullOrWhiteSpace(motivo) ? string.Empty : $" · {ProduccionTiempoExtraMotivo.Nombre(motivo)}")}.";

            if (ultimoCorte.HasValue)
            {
                resultado.FechaObjetivo = ultimoCorte.Value.AddMinutes(60);
                resultado.CorteVencido = ahora >= resultado.FechaObjetivo.Value;
                resultado.MinutosDesfase = resultado.CorteVencido
                    ? Math.Max(0, (int)Math.Floor((ahora - resultado.FechaObjetivo.Value).TotalMinutes))
                    : 0;
            }

            return resultado;
        }

        if (estatusId != ProduccionEstatus.EnProduccion || maquinaLiberada || tieneParoAbierto)
            return resultado;

        if (!vm.FechaFinProgramada.HasValue || ahora < vm.FechaFinProgramada.Value)
            return resultado;

        resultado.Mostrar = true;
        resultado.TieneSesionActiva = false;
        resultado.FechaObjetivo = vm.FechaFinProgramada;
        resultado.TextoBoton = "Revisar tiempo extra";
        resultado.Descripcion = "El periodo normal programado ya concluyo. El operador puede iniciar tiempo extra cuando todas las capturas normales esten completas y las validaciones actuales lo permitan.";
        return resultado;
    }
}
