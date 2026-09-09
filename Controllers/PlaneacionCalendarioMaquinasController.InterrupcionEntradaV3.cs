using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace ERP.NSQuell.Controllers;

// NSQ_CALENDARIOS_INTERRUPCION_V4
public sealed partial class PlaneacionCalendarioMaquinasController
{
    private sealed class CandidatoInterrupcionUrgenteSeleccionVm
    {
        public int ProgramaProduccionID { get; set; }
        public int? SolicitudProduccionID { get; set; }
        public string OFTexto { get; set; } = string.Empty;
        public string Parte { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public string Molde { get; set; } = string.Empty;
        public string MaquinaActual { get; set; } = string.Empty;
        public DateTime FechaInicioProgramada { get; set; }
        public decimal HorasProgramadas { get; set; }
    }

    [HttpGet("CandidatosInterrupcionUrgente")]
    public async Task<IActionResult> CandidatosInterrupcionUrgente(
        int maquinaId,
        int programaInterrumpidoId)
    {
        if (!UsuarioEnSesion())
        {
            return Unauthorized(new
            {
                ok = false,
                sesionExpirada = true,
                mensaje = "La sesion termino. Vuelve a iniciar sesion."
            });
        }

        if (maquinaId <= 0 || programaInterrumpidoId <= 0)
        {
            return BadRequest(new
            {
                ok = false,
                mensaje = "La maquina y el programa producido son obligatorios."
            });
        }

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync();

        var actual =
            await ObtenerProduccionActivaParaInterrupcionUrgenteAsync(
                maquinaId,
                cn);

        if (actual == null)
        {
            return BadRequest(new
            {
                ok = false,
                mensaje = "La maquina ya no tiene una OF en produccion que pueda interrumpirse."
            });
        }

        if (actual.ProgramaProduccionID != programaInterrumpidoId)
        {
            return BadRequest(new
            {
                ok = false,
                mensaje =
                    "La produccion activa de la maquina cambio. Recarga el calendario antes de continuar."
            });
        }

        const string sql = @"
SELECT TOP(150)
    pp.ProgramaProduccionID
FROM dbo.Planeacion_ProgramaProduccion pp
WHERE pp.Activo = 1
  AND ISNULL(pp.EstatusID,1) = 1
  AND pp.ProgramaProduccionID <> @ProgramaInterrumpidoID
  AND pp.FechaInicioProgramada IS NOT NULL
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.Produccion_Ejecucion e
      WHERE e.ProgramaProduccionID = pp.ProgramaProduccionID
        AND e.Activo = 1
  )
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.Calidad_Inspecciones ci
      WHERE ci.ProgramaProduccionID = pp.ProgramaProduccionID
  )
ORDER BY
    pp.FechaInicioProgramada,
    pp.ProgramaProduccionID;";

        var ids = new List<int>();

        await using (var cmd = new SqlCommand(sql, cn))
        {
            cmd.Parameters.Add(
                "@ProgramaInterrumpidoID",
                SqlDbType.Int).Value = programaInterrumpidoId;

            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
                ids.Add(Convert.ToInt32(rd["ProgramaProduccionID"]));
        }

        var candidatos =
            new List<CandidatoInterrupcionUrgenteSeleccionVm>();

        foreach (var id in ids)
        {
            var programa =
                await ObtenerProgramaBaseAsync(
                    id,
                    cn,
                    null,
                    bloquear: false);

            if (programa == null)
                continue;

            var bloqueo =
                await ObtenerMotivoBloqueoMovimientoAsync(
                    id,
                    cn,
                    null,
                    bloquear: false);

            if (!string.IsNullOrWhiteSpace(bloqueo))
                continue;

            var compatibles =
                await ObtenerMaquinasCompatiblesAsync(
                    programa,
                    cn,
                    null);

            if (!compatibles.Any(x => x.MaquinaID == maquinaId))
                continue;

            candidatos.Add(
                new CandidatoInterrupcionUrgenteSeleccionVm
                {
                    ProgramaProduccionID = programa.ProgramaProduccionID,
                    SolicitudProduccionID = programa.SolicitudProduccionID,
                    OFTexto = programa.SolicitudProduccionID.HasValue
                        ? $"OF {programa.SolicitudProduccionID.Value}"
                        : $"Programa {programa.ProgramaProduccionID}",
                    Parte =
                        !string.IsNullOrWhiteSpace(programa.NumeroParte)
                            ? programa.NumeroParte
                            : !string.IsNullOrWhiteSpace(programa.ReferenciaSAP)
                                ? programa.ReferenciaSAP
                                : "Sin parte",
                    Descripcion = programa.DescripcionParte ?? string.Empty,
                    Molde = programa.MoldeCodigo ?? "Sin molde",
                    MaquinaActual =
                        string.Join(
                            " - ",
                            new[]
                            {
                                programa.MaquinaCodigo,
                                programa.MaquinaNombre
                            }
                            .Where(x => !string.IsNullOrWhiteSpace(x))
                            .Distinct()),
                    FechaInicioProgramada = programa.FechaInicioProgramada,
                    HorasProgramadas = programa.HorasProgramadas
                });
        }

        return Json(new
        {
            ok = true,
            maquinaID = maquinaId,
            programaInterrumpidoID = programaInterrumpidoId,
            candidatos = candidatos.Select(x => new
            {
                programaProduccionID = x.ProgramaProduccionID,
                solicitudProduccionID = x.SolicitudProduccionID,
                of = x.OFTexto,
                parte = x.Parte,
                descripcion = x.Descripcion,
                molde = x.Molde,
                maquinaActual = x.MaquinaActual,
                fechaInicioProgramada = x.FechaInicioProgramada,
                horasProgramadas = x.HorasProgramadas
            })
        });
    }
}