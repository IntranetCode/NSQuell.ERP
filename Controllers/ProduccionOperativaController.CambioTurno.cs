using ERP.NSQuell.Models;
using Microsoft.Data.SqlClient;
using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace ERP.NSQuell.Controllers;

public sealed partial class ProduccionOperativaController
{
    private const int VentanaPreviaCambioTurnoMinutos = 15;

    private sealed class DisponibilidadCambioTurnoOperativo
    {
        public bool Mostrar { get; set; }
        public bool TieneEntregaPendiente { get; set; }
        public DateTime? FechaObjetivo { get; set; }
        public bool YaInicio { get; set; }
        public int MinutosDesfase { get; set; }
        public string Descripcion { get; set; } = string.Empty;
        public string TextoBoton { get; set; } = "Ver cambio de turno";
    }

    private async Task<DisponibilidadCambioTurnoOperativo> ObtenerDisponibilidadCambioTurnoOperativoAsync(
        ProduccionCentroOperativoVm vm,
        SqlConnection cn,
        CancellationToken cancellationToken)
    {
        var resultado = new DisponibilidadCambioTurnoOperativo();
        if (!vm.EjecucionProduccionID.HasValue || vm.EjecucionProduccionID.Value <= 0)
            return resultado;

        int? operadorActualId = null;
        int estatusId = 0;

        const string sqlEjecucion = @"
SELECT TOP(1)
    e.OperadorID,
    e.EstatusID
FROM dbo.Produccion_Ejecucion e
WHERE e.EjecucionProduccionID=@EjecucionProduccionID
  AND e.Activo=1;";

        await using (var cmd = new SqlCommand(sqlEjecucion, cn))
        {
            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = vm.EjecucionProduccionID.Value;
            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
            if (!await rd.ReadAsync(cancellationToken)) return resultado;
            operadorActualId = rd["OperadorID"] == DBNull.Value ? null : Convert.ToInt32(rd["OperadorID"]);
            estatusId = Convert.ToInt32(rd["EstatusID"]);
        }

        const string sqlPendiente = @"
SELECT TOP(1) ct.FechaEntrega
FROM dbo.Produccion_CambiosTurno ct
WHERE ct.EjecucionProduccionID=@EjecucionProduccionID
  AND ct.EstadoCambioTurno=N'PENDIENTE_RECEPCION'
  AND ct.Activo=1
ORDER BY ct.FechaEntrega DESC,ct.CambioTurnoID DESC;";

        DateTime? fechaEntregaPendiente = null;
        await using (var cmd = new SqlCommand(sqlPendiente, cn))
        {
            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = vm.EjecucionProduccionID.Value;
            var valor = await cmd.ExecuteScalarAsync(cancellationToken);
            if (valor != null && valor != DBNull.Value) fechaEntregaPendiente = Convert.ToDateTime(valor);
        }

        if (fechaEntregaPendiente.HasValue)
        {
            resultado.Mostrar = true;
            resultado.TieneEntregaPendiente = true;
            resultado.FechaObjetivo = fechaEntregaPendiente;
            resultado.YaInicio = true;
            resultado.MinutosDesfase = Math.Max(0, (int)Math.Floor((DateTime.Now - fechaEntregaPendiente.Value).TotalMinutes));
            resultado.Descripcion = "Existe una entrega de turno pendiente de recepción. La responsabilidad de la OF no cambia hasta que el operador entrante confirme la recepción.";
            resultado.TextoBoton = "Ver / recibir turno";
            return resultado;
        }

        if (estatusId != ProduccionEstatus.EnProduccion)
            return resultado;

        const string sqlExisteAsignaciones = @"
SELECT CONVERT(bit,CASE WHEN OBJECT_ID(N'dbo.Produccion_ProgramaPersonalAsignaciones',N'U') IS NULL THEN 0 ELSE 1 END);";

        await using (var cmd = new SqlCommand(sqlExisteAsignaciones, cn))
        {
            var existe = Convert.ToBoolean(await cmd.ExecuteScalarAsync(cancellationToken) ?? false);
            if (!existe) return resultado;
        }

        var ahora = DateTime.Now;
        const string sqlCambio = @"
SELECT TOP(1)
    a.Inicio,
    a.Fin,
    a.TurnoID,
    a.TurnoNombre,
    a.OperadorID,
    LTRIM(RTRIM(CONCAT(ISNULL(p.Nombre,N''),N' ',ISNULL(p.ApellidoPaterno,N''),N' ',ISNULL(p.ApellidoMaterno,N'')))) AS OperadorNombre
FROM dbo.Produccion_ProgramaPersonalAsignaciones a
LEFT JOIN dbo.Persona p
    ON p.PersonaID=a.OperadorID
WHERE a.ProgramaProduccionID=@ProgramaProduccionID
  AND a.Activo=1
  AND a.Fin>@Ahora
  AND a.Inicio<=DATEADD(MINUTE,@VentanaMinutos,@Ahora)
  AND
  (
      a.OperadorID IS NULL
      OR @OperadorActualID IS NULL
      OR a.OperadorID<>@OperadorActualID
  )
ORDER BY
    CASE WHEN a.Inicio<=@Ahora THEN 0 ELSE 1 END,
    a.Inicio,
    a.AsignacionPersonalID;";

        DateTime? inicio = null;
        DateTime? fin = null;
        string? turno = null;
        string? operadorProgramado = null;

        await using (var cmd = new SqlCommand(sqlCambio, cn))
        {
            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = vm.ProgramaProduccionID;
            cmd.Parameters.Add("@Ahora", SqlDbType.DateTime2).Value = ahora;
            cmd.Parameters.Add("@VentanaMinutos", SqlDbType.Int).Value = VentanaPreviaCambioTurnoMinutos;
            cmd.Parameters.Add("@OperadorActualID", SqlDbType.Int).Value = operadorActualId.HasValue ? (object)operadorActualId.Value : DBNull.Value;

            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
            if (!await rd.ReadAsync(cancellationToken)) return resultado;

            inicio = Convert.ToDateTime(rd["Inicio"]);
            fin = Convert.ToDateTime(rd["Fin"]);
            turno = rd["TurnoNombre"] == DBNull.Value ? null : rd["TurnoNombre"]?.ToString()?.Trim();
            operadorProgramado = rd["OperadorNombre"] == DBNull.Value ? null : rd["OperadorNombre"]?.ToString()?.Trim();
        }

        if (!inicio.HasValue) return resultado;

        resultado.Mostrar = true;
        resultado.FechaObjetivo = inicio;
        resultado.YaInicio = inicio.Value <= ahora;
        resultado.MinutosDesfase = resultado.YaInicio
            ? Math.Max(0, (int)Math.Floor((ahora - inicio.Value).TotalMinutes))
            : 0;
        resultado.TextoBoton = resultado.YaInicio ? "Atender cambio de turno" : "Preparar cambio de turno";

        var turnoTexto = string.IsNullOrWhiteSpace(turno) ? "el siguiente turno" : turno;
        var operadorTexto = string.IsNullOrWhiteSpace(operadorProgramado) ? "aún no tiene operador asignado" : $"está programado para {operadorProgramado}";
        var rango = fin.HasValue ? $" ({inicio:HH:mm}–{fin:HH:mm})" : string.Empty;
        resultado.Descripcion = resultado.YaInicio
            ? $"El tramo de {turnoTexto}{rango} ya comenzó y {operadorTexto}. Debe realizarse la entrega/recepción antes de continuar con la responsabilidad del nuevo operador."
            : $"El siguiente tramo de {turnoTexto}{rango} inicia en {Math.Max(0, (int)Math.Ceiling((inicio.Value - ahora).TotalMinutes))} min y {operadorTexto}.";

        return resultado;
    }
}
