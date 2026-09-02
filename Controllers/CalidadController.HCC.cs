using ERP.NSQuell.Models.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace ERP.NSQuell.Controllers
{
    public partial class CalidadController
    {
        [HttpGet]
        public async Task<IActionResult> HojasControl(string? busqueda)
        {
            var model = new CalidadHCCIndexViewModel { Busqueda = busqueda?.Trim() };

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            const string sql = @"
SELECT
    h.PlantillaHCCID,
    pp.ParteID,
    p.NumeroParte,
    COALESCE(NULLIF(p.Designacion,N''),p.Descripcion) AS Designacion,
    c.Nombre AS Cliente,
    h.NumeroHCC,
    h.VersionFormato,
    h.FechaModificacionFormato,
    h.MateriaPrima,
    h.TiempoSecadoTexto,
    h.EsVigente,
    p.Activo AS ParteActiva,
    pp.EsPrincipal,
    pp.MetodoMapeo,
    pp.Confianza,
    (SELECT COUNT(*) FROM dbo.Calidad_HCC_Caracteristicas x
      WHERE x.PlantillaHCCID=h.PlantillaHCCID AND x.Activo=1) AS Caracteristicas,
    (SELECT COUNT(*) FROM dbo.Calidad_HCC_Checklist x
      WHERE x.PlantillaHCCID=h.PlantillaHCCID AND x.Activo=1) AS Checklist
FROM dbo.Calidad_HCC_PlantillaPartes pp
JOIN dbo.Calidad_HCC_Plantillas h ON h.PlantillaHCCID=pp.PlantillaHCCID
JOIN dbo.ERP_Partes p ON p.ParteID=pp.ParteID
LEFT JOIN dbo.ERP_Clientes c ON c.ClienteID=p.ClienteID
WHERE h.Activo=1
  AND pp.Activo=1
  AND
  (
      @q IS NULL
      OR p.NumeroParte LIKE N'%'+@q+N'%'
      OR p.ReferenciaSAP LIKE N'%'+@q+N'%'
      OR p.Designacion LIKE N'%'+@q+N'%'
      OR p.Descripcion LIKE N'%'+@q+N'%'
      OR h.NumeroParteFuente LIKE N'%'+@q+N'%'
      OR h.DesignacionFuente LIKE N'%'+@q+N'%'
      OR c.Nombre LIKE N'%'+@q+N'%'
  )
ORDER BY
    p.Activo DESC,
    h.EsVigente DESC,
    pp.EsPrincipal DESC,
    c.Nombre,
    p.NumeroParte,
    h.FechaModificacionFormato DESC,
    h.PlantillaHCCID DESC;";

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@q", SqlDbType.NVarChar, 300).Value =
                string.IsNullOrWhiteSpace(model.Busqueda) ? DBNull.Value : model.Busqueda;

            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                model.Plantillas.Add(new CalidadHCCPlantillaItemViewModel
                {
                    PlantillaHCCID = Convert.ToInt32(rd["PlantillaHCCID"]),
                    ParteID = Convert.ToInt32(rd["ParteID"]),
                    NumeroParte = rd["NumeroParte"].ToString() ?? "",
                    Designacion = rd["Designacion"] as string,
                    Cliente = rd["Cliente"] as string,
                    NumeroHCC = rd["NumeroHCC"] as string,
                    VersionFormato = rd["VersionFormato"] as string,
                    FechaModificacionFormato = rd["FechaModificacionFormato"] == DBNull.Value
                        ? null : Convert.ToDateTime(rd["FechaModificacionFormato"]),
                    MateriaPrima = rd["MateriaPrima"] as string,
                    TiempoSecadoTexto = rd["TiempoSecadoTexto"] as string,
                    EsVigente = Convert.ToBoolean(rd["EsVigente"]),
                    ParteActiva = Convert.ToBoolean(rd["ParteActiva"]),
                    EsRelacionPrincipal = Convert.ToBoolean(rd["EsPrincipal"]),
                    MetodoMapeo = rd["MetodoMapeo"] as string,
                    ConfianzaMapeo = rd["Confianza"] == DBNull.Value ? null : Convert.ToDecimal(rd["Confianza"]),
                    Caracteristicas = Convert.ToInt32(rd["Caracteristicas"]),
                    Checklist = Convert.ToInt32(rd["Checklist"])
                });
            }

            return View("HojasControl", model);
        }

        [HttpGet]
        public async Task<IActionResult> HojaControl(int id, int? parteId = null)
        {
            var model = await CargarPlantillaHCCAsync(id, parteId);
            if (model == null) return NotFound();
            return View("HojaControl", model);
        }

        [HttpGet]
        public async Task<IActionResult> CapturarHCC(
            int id,
            int? parteId = null,
            int? inspeccionID = null,
            string? of = null)
        {
            var plantilla = await CargarPlantillaHCCAsync(id, parteId);
            if (plantilla == null) return NotFound();

            var model = new CalidadHCCCapturaViewModel
            {
                PlantillaHCCID = id,
                ParteID = plantilla.ParteID,
                InspeccionID = inspeccionID,
                OrdenFabricacion = of,
                Plantilla = plantilla,
                Fecha = DateTime.Today,
                Hora = DateTime.Now.TimeOfDay,
                TipoEvento = "M"
            };

            foreach (var c in plantilla.Caracteristicas)
            {
                var cavidades = c.Cavidades.Count > 0 ? c.Cavidades : new List<int> { 1 };
                foreach (var cavidad in cavidades)
                {
                    for (var tiro = 1; tiro <= Math.Max(1, plantilla.NumeroTiros); tiro++)
                    {
                        model.Mediciones.Add(new CalidadHCCMedicionPostViewModel
                        {
                            CaracteristicaHCCID = c.CaracteristicaHCCID,
                            NumeroCavidad = cavidad,
                            NumeroTiro = tiro,
                            Resultado = "OK"
                        });
                    }
                }
            }

            foreach (var x in plantilla.Checklist)
            {
                model.Checklist.Add(new CalidadHCCChecklistPostViewModel
                {
                    ChecklistHCCID = x.ChecklistHCCID,
                    Resultado = "OK"
                });
            }

            return View("CapturarHCC", model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GuardarHCC(CalidadHCCCapturaViewModel model)
        {
            var usuarioId = ObtenerUsuarioIdActual();
            if (!usuarioId.HasValue || usuarioId.Value <= 0) return Unauthorized();

            model.TipoEvento = (model.TipoEvento ?? "").Trim().ToUpperInvariant();
            if (!new[] { "L", "M", "RL" }.Contains(model.TipoEvento))
                ModelState.AddModelError(nameof(model.TipoEvento), "Evento inválido.");

            var plantilla = await CargarPlantillaHCCAsync(model.PlantillaHCCID, model.ParteID);
            if (plantilla == null)
            {
                ModelState.AddModelError(nameof(model.ParteID),
                    "La pieza indicada no está vinculada a esta HCC.");
                return NotFound();
            }

            if (!ModelState.IsValid)
            {
                model.Plantilla = plantilla;
                return View("CapturarHCC", model);
            }

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);

            try
            {
                long registroHccId;
                const string insertRegistro = @"
INSERT dbo.Calidad_HCC_Registros
(
    PlantillaHCCID,ParteID,InspeccionID,OrdenFabricacion,Fecha,Turno,Hora,
    MaquinaID,MaquinaTexto,TipoEvento,OperadorTexto,AuditorTexto,Observaciones,
    Estado,VersionFormatoSnapshot,UsuarioCreacionID,Activo
)
OUTPUT INSERTED.RegistroHCCID
VALUES
(
    @plantilla,@parte,@inspeccion,@of,@fecha,@turno,@hora,@maquina,@maquinaTexto,
    @evento,@operador,@auditor,@observaciones,N'CAPTURADO',@version,@usuario,1
);";

                await using (var cmd = new SqlCommand(insertRegistro, cn, tx))
                {
                    cmd.Parameters.AddWithValue("@plantilla", model.PlantillaHCCID);
                    cmd.Parameters.AddWithValue("@parte", plantilla.ParteID);
                    cmd.Parameters.AddWithValue("@inspeccion", (object?)model.InspeccionID ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@of", (object?)model.OrdenFabricacion ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@fecha", model.Fecha.Date);
                    cmd.Parameters.AddWithValue("@turno", (object?)model.Turno ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@hora", (object?)model.Hora ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@maquina", (object?)model.MaquinaID ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@maquinaTexto", (object?)model.MaquinaTexto ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@evento", model.TipoEvento);
                    cmd.Parameters.AddWithValue("@operador", (object?)model.OperadorTexto ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@auditor", (object?)model.AuditorTexto ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@observaciones", (object?)model.Observaciones ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@version", (object?)plantilla.VersionFormato ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@usuario", usuarioId.Value);
                    registroHccId = Convert.ToInt64(await cmd.ExecuteScalarAsync());
                }

                foreach (var x in model.Mediciones)
                {
                    if (!plantilla.Caracteristicas.Any(c => c.CaracteristicaHCCID == x.CaracteristicaHCCID))
                        throw new InvalidOperationException("Característica ajena a la plantilla HCC.");

                    var resultado = (x.Resultado ?? "OK").Trim().ToUpperInvariant();
                    if (!new[] { "OK", "NOK", "NA" }.Contains(resultado))
                        throw new InvalidOperationException("Resultado de medición inválido.");

                    const string insertMedicion = @"
INSERT dbo.Calidad_HCC_Mediciones
(RegistroHCCID,CaracteristicaHCCID,NumeroTiro,NumeroCavidad,ValorNumerico,ValorTexto,Resultado,Observaciones,UsuarioCreacionID,Activo)
VALUES(@registro,@caracteristica,@tiro,@cavidad,@numero,@texto,@resultado,@observaciones,@usuario,1);";

                    await using var cmd = new SqlCommand(insertMedicion, cn, tx);
                    cmd.Parameters.AddWithValue("@registro", registroHccId);
                    cmd.Parameters.AddWithValue("@caracteristica", x.CaracteristicaHCCID);
                    cmd.Parameters.AddWithValue("@tiro", x.NumeroTiro);
                    cmd.Parameters.AddWithValue("@cavidad", x.NumeroCavidad);
                    cmd.Parameters.AddWithValue("@numero", (object?)x.ValorNumerico ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@texto", (object?)x.ValorTexto ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@resultado", resultado);
                    cmd.Parameters.AddWithValue("@observaciones", (object?)x.Observaciones ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@usuario", usuarioId.Value);
                    await cmd.ExecuteNonQueryAsync();
                }

                foreach (var x in model.Checklist)
                {
                    if (!plantilla.Checklist.Any(c => c.ChecklistHCCID == x.ChecklistHCCID))
                        throw new InvalidOperationException("Checklist ajeno a la plantilla HCC.");

                    var resultado = (x.Resultado ?? "OK").Trim().ToUpperInvariant();
                    if (!new[] { "OK", "NOK", "NA" }.Contains(resultado))
                        throw new InvalidOperationException("Resultado de checklist inválido.");

                    const string insertChecklist = @"
INSERT dbo.Calidad_HCC_ChecklistResultados
(RegistroHCCID,ChecklistHCCID,Resultado,Observaciones,UsuarioCreacionID,Activo)
VALUES(@registro,@checklist,@resultado,@observaciones,@usuario,1);";

                    await using var cmd = new SqlCommand(insertChecklist, cn, tx);
                    cmd.Parameters.AddWithValue("@registro", registroHccId);
                    cmd.Parameters.AddWithValue("@checklist", x.ChecklistHCCID);
                    cmd.Parameters.AddWithValue("@resultado", resultado);
                    cmd.Parameters.AddWithValue("@observaciones", (object?)x.Observaciones ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@usuario", usuarioId.Value);
                    await cmd.ExecuteNonQueryAsync();
                }

                await tx.CommitAsync();
                TempData["SuccessMessage"] = $"HCC #{registroHccId} guardada correctamente.";
                return RedirectToAction(nameof(HojaControl),
                    new { id = model.PlantillaHCCID, parteId = model.ParteID });
            }
            catch (Exception ex)
            {
                try { await tx.RollbackAsync(); } catch { }
                ModelState.AddModelError("", "No fue posible guardar HCC: " + ex.Message);
                model.Plantilla = plantilla;
                return View("CapturarHCC", model);
            }
        }

        private async Task<CalidadHCCPlantillaDetalleViewModel?> CargarPlantillaHCCAsync(
            int id,
            int? parteId = null)
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            const string queryPlantilla = @"
SELECT TOP(1)
    h.*,
    pp.ParteID AS ParteRelacionadaID,
    pp.EsPrincipal,
    pp.MetodoMapeo,
    pp.Confianza,
    p.NumeroParte,
    p.Activo AS ParteActiva,
    COALESCE(NULLIF(p.Designacion,N''),p.Descripcion) AS Designacion,
    c.Nombre AS Cliente
FROM dbo.Calidad_HCC_PlantillaPartes pp
JOIN dbo.Calidad_HCC_Plantillas h ON h.PlantillaHCCID=pp.PlantillaHCCID
JOIN dbo.ERP_Partes p ON p.ParteID=pp.ParteID
LEFT JOIN dbo.ERP_Clientes c ON c.ClienteID=p.ClienteID
WHERE h.PlantillaHCCID=@id
  AND h.Activo=1
  AND pp.Activo=1
  AND (@parte IS NULL OR pp.ParteID=@parte)
ORDER BY
    CASE WHEN @parte IS NOT NULL AND pp.ParteID=@parte THEN 0 ELSE 1 END,
    pp.EsPrincipal DESC,
    p.Activo DESC,
    pp.ParteID;";

            CalidadHCCPlantillaDetalleViewModel? model = null;
            await using (var cmd = new SqlCommand(queryPlantilla, cn))
            {
                cmd.Parameters.AddWithValue("@id", id);
                cmd.Parameters.Add("@parte", SqlDbType.Int).Value = (object?)parteId ?? DBNull.Value;

                await using var rd = await cmd.ExecuteReaderAsync();
                if (await rd.ReadAsync())
                {
                    model = new CalidadHCCPlantillaDetalleViewModel
                    {
                        PlantillaHCCID = id,
                        ParteID = Convert.ToInt32(rd["ParteRelacionadaID"]),
                        NumeroParte = rd["NumeroParte"].ToString() ?? "",
                        ParteActiva = Convert.ToBoolean(rd["ParteActiva"]),
                        EsRelacionPrincipal = Convert.ToBoolean(rd["EsPrincipal"]),
                        MetodoMapeo = rd["MetodoMapeo"] as string,
                        ConfianzaMapeo = rd["Confianza"] == DBNull.Value ? null : Convert.ToDecimal(rd["Confianza"]),
                        Designacion = rd["Designacion"] as string,
                        Cliente = rd["Cliente"] as string,
                        NumeroHCC = rd["NumeroHCC"] as string,
                        VersionFormato = rd["VersionFormato"] as string,
                        FechaRevision = rd["FechaModificacionFormato"] == DBNull.Value
                            ? null : Convert.ToDateTime(rd["FechaModificacionFormato"]),
                        NumeroDibujo = rd["NumeroDibujo"] as string,
                        Proceso = rd["Proceso"] as string,
                        PlanControl = rd["ReferenciaPlanControl"] as string,
                        CodigoResina = rd["CodigoResina"] as string,
                        MateriaPrima = rd["MateriaPrima"] as string,
                        TiempoSecado = rd["TiempoSecadoTexto"] as string,
                        NumeroTiros = Convert.ToInt32(rd["NumeroTirosDefault"]),
                        Cavidades = rd["CavidadesDeclaradas"] == DBNull.Value
                            ? null : Convert.ToInt32(rd["CavidadesDeclaradas"]),
                        ArchivoOrigen = rd["ArchivoOrigen"] as string,
                        HojaOrigen = rd["HojaOrigen"] as string
                    };
                }
            }

            if (model == null) return null;

            const string queryCaracteristicas = @"
SELECT
    c.CaracteristicaHCCID,c.Orden,c.TipoCaracteristica,c.Nombre,c.ValorNominal,
    c.ToleranciaMas,c.ToleranciaMenos,c.LimiteInferior,c.LimiteSuperior,c.Unidad,
    c.Instrumento,c.CodigoGauge,cc.NumeroCavidad
FROM dbo.Calidad_HCC_Caracteristicas c
LEFT JOIN dbo.Calidad_HCC_CaracteristicaCavidades cc
    ON cc.CaracteristicaHCCID=c.CaracteristicaHCCID AND cc.Activo=1
WHERE c.PlantillaHCCID=@id AND c.Activo=1
ORDER BY c.Orden,cc.NumeroCavidad;";

            var caracteristicas = new Dictionary<int, CalidadHCCCaracteristicaViewModel>();
            await using (var cmd = new SqlCommand(queryCaracteristicas, cn))
            {
                cmd.Parameters.AddWithValue("@id", id);
                await using var rd = await cmd.ExecuteReaderAsync();
                while (await rd.ReadAsync())
                {
                    var caracteristicaId = Convert.ToInt32(rd["CaracteristicaHCCID"]);
                    if (!caracteristicas.TryGetValue(caracteristicaId, out var c))
                    {
                        c = new CalidadHCCCaracteristicaViewModel
                        {
                            CaracteristicaHCCID = caracteristicaId,
                            Orden = Convert.ToInt32(rd["Orden"]),
                            TipoCaracteristica = rd["TipoCaracteristica"].ToString() ?? "",
                            Nombre = rd["Nombre"].ToString() ?? "",
                            ValorNominal = rd["ValorNominal"] == DBNull.Value ? null : Convert.ToDecimal(rd["ValorNominal"]),
                            ToleranciaMas = rd["ToleranciaMas"] == DBNull.Value ? null : Convert.ToDecimal(rd["ToleranciaMas"]),
                            ToleranciaMenos = rd["ToleranciaMenos"] == DBNull.Value ? null : Convert.ToDecimal(rd["ToleranciaMenos"]),
                            LimiteInferior = rd["LimiteInferior"] == DBNull.Value ? null : Convert.ToDecimal(rd["LimiteInferior"]),
                            LimiteSuperior = rd["LimiteSuperior"] == DBNull.Value ? null : Convert.ToDecimal(rd["LimiteSuperior"]),
                            Unidad = rd["Unidad"] as string,
                            Instrumento = rd["Instrumento"] as string,
                            CodigoGauge = rd["CodigoGauge"] as string
                        };
                        caracteristicas[caracteristicaId] = c;
                    }

                    if (rd["NumeroCavidad"] != DBNull.Value)
                        c.Cavidades.Add(Convert.ToInt32(rd["NumeroCavidad"]));
                }
            }
            model.Caracteristicas = caracteristicas.Values.OrderBy(x => x.Orden).ToList();

            const string queryChecklist = @"
SELECT ChecklistHCCID,Orden,Descripcion,PermiteNA
FROM dbo.Calidad_HCC_Checklist
WHERE PlantillaHCCID=@id AND Activo=1
ORDER BY Orden;";

            await using (var cmd = new SqlCommand(queryChecklist, cn))
            {
                cmd.Parameters.AddWithValue("@id", id);
                await using var rd = await cmd.ExecuteReaderAsync();
                while (await rd.ReadAsync())
                {
                    model.Checklist.Add(new CalidadHCCChecklistViewModel
                    {
                        ChecklistHCCID = Convert.ToInt32(rd["ChecklistHCCID"]),
                        Orden = Convert.ToInt32(rd["Orden"]),
                        Descripcion = rd["Descripcion"].ToString() ?? "",
                        PermiteNA = Convert.ToBoolean(rd["PermiteNA"])
                    });
                }
            }

            return model;
        }
    }
}
