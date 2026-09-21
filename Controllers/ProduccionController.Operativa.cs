using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
namespace ERP.NSQuell.Controllers;

public sealed partial class ProduccionController
{
    private sealed class PersonalInicioOperativo
    {
        public int? OperadorID { get; set; }
        public int? AuxiliarID { get; set; }
        public int? TecnicoProduccionID { get; set; }
        public int? SmedID { get; set; }
        public bool TieneAsignacionOperativa { get; set; }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> IniciarOperativa(int programaProduccionId, string? observaciones = null, List<long>? etiquetasBlancasSeleccionadas = null)
    {
        if (!UsuarioEnSesion())
            return Unauthorized(new { ok = false, sesionExpirada = true, mensaje = "La sesión terminó. Vuelve a iniciar sesión." });

        if (programaProduccionId <= 0)
            return BadRequest(new { ok = false, mensaje = "No se recibió correctamente el programa de Producción." });

        TempData.Remove("Error");
        TempData.Remove("Success");
        TempData.Remove("Info");

        IActionResult resultado;
        try
        {
            var personal = await ObtenerPersonalInicioOperativoAsync(programaProduccionId);
            resultado = await Iniciar(
                programaProduccionId,
                operadorId: personal.OperadorID,
                operadorNombre: null,
                operadorAuxiliarId: personal.AuxiliarID,
                operadorAuxiliarNombre: null,
                tecnicoProduccionId: personal.TecnicoProduccionID,
                smedId: personal.SmedID,
                personalInicioConfirmado: personal.TieneAsignacionOperativa && personal.OperadorID.HasValue,
                observaciones: observaciones,
                etiquetasBlancasSeleccionadas: etiquetasBlancasSeleccionadas
               );
        }
        catch (Exception ex)
        {
            return BadRequest(new { ok = false, mensaje = "No fue posible iniciar la preparación: " + ex.Message });
        }

        var error = TempData["Error"]?.ToString();
        var success = TempData["Success"]?.ToString();
        var info = TempData["Info"]?.ToString();

        TempData.Remove("Error");
        TempData.Remove("Success");
        TempData.Remove("Info");

        if (!string.IsNullOrWhiteSpace(error))
            return BadRequest(new { ok = false, mensaje = error });

        if (resultado is RedirectToActionResult redirect)
        {
            int? ejecucionProduccionId = null;
            if (redirect.RouteValues != null && redirect.RouteValues.TryGetValue("id", out var rawId) && rawId != null && int.TryParse(Convert.ToString(rawId), out var id) && id > 0)
                ejecucionProduccionId = id;

            return Json(new
            {
                ok = true,
                programaProduccionId,
                ejecucionProduccionId,
                mensaje = !string.IsNullOrWhiteSpace(success) ? success : !string.IsNullOrWhiteSpace(info) ? info : "Preparación iniciada correctamente.",
                refrescarCentro = true,
                refrescarCalendario = true
            });
        }

        if (resultado is UnauthorizedResult)
            return Unauthorized(new { ok = false, sesionExpirada = true, mensaje = "La sesión terminó. Vuelve a iniciar sesión." });

        if (resultado is NotFoundResult)
            return NotFound(new { ok = false, mensaje = "La OF ya no está disponible para iniciar preparación." });

        return Json(new
        {
            ok = true,
            programaProduccionId,
            mensaje = !string.IsNullOrWhiteSpace(success) ? success : !string.IsNullOrWhiteSpace(info) ? info : "Operación completada.",
            refrescarCentro = true,
            refrescarCalendario = true
        });
    }
    private async Task<PersonalInicioOperativo> ObtenerPersonalInicioOperativoAsync(int programaProduccionId)
    {
        var resultado = new PersonalInicioOperativo();
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync();

        const string sqlExiste = @"
SELECT CONVERT(bit,CASE
    WHEN OBJECT_ID(N'dbo.Produccion_ProgramaPersonalAsignaciones',N'U') IS NULL THEN 0
    ELSE 1
END);";

        bool existeTabla;
        await using (var cmd = new SqlCommand(sqlExiste, cn))
            existeTabla = Convert.ToBoolean(await cmd.ExecuteScalarAsync() ?? false);

        if (existeTabla)
        {
            const string sqlPersonal = @"
SELECT TOP(1)
    a.AsignacionPersonalID,
    a.OperadorID,
    a.AuxiliarID,
    a.TecnicoProduccionID
FROM dbo.Produccion_ProgramaPersonalAsignaciones a
WHERE a.Activo=1
  AND a.ProgramaProduccionID=@ProgramaProduccionID
ORDER BY a.AsignacionPersonalID DESC;";

            await using var cmd = new SqlCommand(sqlPersonal, cn);
            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaProduccionId;

            await using var rd = await cmd.ExecuteReaderAsync();
            if (await rd.ReadAsync())
            {
                resultado.TieneAsignacionOperativa = true;
                resultado.OperadorID = rd["OperadorID"] == DBNull.Value ? null : Convert.ToInt32(rd["OperadorID"]);
                resultado.AuxiliarID = rd["AuxiliarID"] == DBNull.Value ? null : Convert.ToInt32(rd["AuxiliarID"]);
                resultado.TecnicoProduccionID = rd["TecnicoProduccionID"] == DBNull.Value ? null : Convert.ToInt32(rd["TecnicoProduccionID"]);
            }
        }

        var personalGeneral = await ObtenerPersonalProgramadoProduccionAsync(
            programaProduccionId,
            DateTime.Now,
            null,
            cn,
            null);

        if (!resultado.OperadorID.HasValue)
            resultado.OperadorID = personalGeneral?.OperadorID;

        if (!resultado.AuxiliarID.HasValue)
            resultado.AuxiliarID = personalGeneral?.AuxiliarID;

        if (!resultado.TecnicoProduccionID.HasValue)
            resultado.TecnicoProduccionID = personalGeneral?.TecnicoID;

        resultado.SmedID = personalGeneral?.SmedID;
        return resultado;
    }
}
