using ERP.NSQuell.Models;
using ERP.NSQuell.Models.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace ERP.NSQuell.Controllers
{
    public partial class CalidadController
    {
        private async Task<CalidadDetalleViewModel?> ConstruirDetalleFlujoAsync(int id)
        {
            var inspeccion = await _context.CalidadInspecciones
                .AsNoTracking()
                .Include(x => x.Historial)
                .FirstOrDefaultAsync(x => x.InspeccionID == id);

            if (inspeccion == null)
                return null;

            var model = new CalidadDetalleViewModel
            {
                InspeccionID = inspeccion.InspeccionID,
                ProgramaProduccionID = inspeccion.ProgramaProduccionID,
                EjecucionProduccionID = inspeccion.EjecucionProduccionID,
                ChecklistArranqueID = inspeccion.ChecklistArranqueID,
                SolicitudProduccionID = inspeccion.SolicitudProduccionID,
                SolicitudProduccionDetalleID = inspeccion.SolicitudProduccionDetalleID,
                ReleaseID = inspeccion.ReleaseID,
                ReleaseDetalleID = inspeccion.ReleaseDetalleID,
                ClienteID = inspeccion.ClienteID,
                ParteID = inspeccion.ParteID,
                MaquinaID = inspeccion.MaquinaID,
                MoldeID = inspeccion.MoldeID,
                MaterialID = inspeccion.MaterialID,
                CodigoBarras = inspeccion.CodigoBarras,
                OrdenTrabajo = inspeccion.OrdenTrabajo,
                ClienteNombre = inspeccion.ClienteNombre,
                NumeroParte = inspeccion.NumeroParte,
                Material = inspeccion.Material,
                Proceso = inspeccion.Proceso,
                Maquina = inspeccion.Maquina,
                Molde = inspeccion.Molde,
                OperadorPrincipalPersonaID = inspeccion.OperadorPrincipalPersonaID,
                OperadorPrincipalNombre = inspeccion.OperadorPrincipalNombre,
                OperadorAuxiliarPersonaID = inspeccion.OperadorAuxiliarPersonaID,
                OperadorAuxiliarNombre = inspeccion.OperadorAuxiliarNombre,
                TecnicoInyeccionPersonaID = inspeccion.TecnicoInyeccionPersonaID,
                TecnicoInyeccionNombre = inspeccion.TecnicoInyeccionNombre,
                FechaInicioProgramada = inspeccion.FechaInicioProgramada,
                FechaFinProgramada = inspeccion.FechaFinProgramada,
                CantidadTotal = inspeccion.CantidadTotal,
                CantidadRevisada = inspeccion.CantidadRevisada,
                CantidadPendiente = inspeccion.CantidadPendiente,
                ChecklistValidado = inspeccion.ChecklistValidado,
                HojaInspeccionProducto = inspeccion.HojaInspeccionProducto,
                HojaValidacionCalidad = inspeccion.HojaValidacionCalidad,
                AyudaVisualColocada = inspeccion.AyudaVisualColocada,
                AlertaCalidadAplica = inspeccion.AlertaCalidadAplica,
                AlertaCalidadColocada = inspeccion.AlertaCalidadColocada,
                HIPColocada = inspeccion.HIPColocada,
                HCCColocada = inspeccion.HCCColocada,
                MatrizPolivalenciaValidada = inspeccion.MatrizPolivalenciaValidada,
                FechaNotificacionCalidad = inspeccion.FechaNotificacionCalidad,
                UsuarioNotificoID = inspeccion.UsuarioNotificoID,
                FechaInicioValidacionPrearranque = inspeccion.FechaInicioValidacionPrearranque,
                FechaFinValidacionPrearranque = inspeccion.FechaFinValidacionPrearranque,
                MinutosLiberacionInicial = inspeccion.MinutosLiberacionInicial,
                CumplioTiempoObjetivoInicial = inspeccion.CumplioTiempoObjetivoInicial,
                FechaAutorizacionPrearranque = inspeccion.FechaAutorizacionPrearranque,
                UsuarioAutorizacionPrearranqueID = inspeccion.UsuarioAutorizacionPrearranqueID,
                MotivoDevolucion = inspeccion.MotivoDevolucion,
                CincoDisparosSegregados = inspeccion.CincoDisparosSegregados,
                CantidadDisparosConformes = inspeccion.CantidadDisparosConformes,
                ValidacionDimensional = inspeccion.ValidacionDimensional,
                ValidacionApariencia = inspeccion.ValidacionApariencia,
                ValidacionGauge = inspeccion.ValidacionGauge,
                ValidacionConductividad = inspeccion.ValidacionConductividad,
                FechaValidacionPrimerasPiezas = inspeccion.FechaValidacionPrimerasPiezas,
                UsuarioValidacionPrimerasPiezasID = inspeccion.UsuarioValidacionPrimerasPiezasID,
                ResultadoCalidad = inspeccion.ResultadoCalidad,
                Etiqueta = inspeccion.Etiqueta,
                Liberado = inspeccion.Liberado,
                RequiereGP12 = inspeccion.RequiereGP12,
                EnContencion = inspeccion.EnContencion,
                EsScrap = inspeccion.EsScrap,
                FechaLiberacionProduccion = inspeccion.FechaLiberacionProduccion,
                UsuarioLiberacionProduccionID = inspeccion.UsuarioLiberacionProduccionID,
                RequiereReliberacion = inspeccion.RequiereReliberacion,
                ConfiguracionInvalidada = inspeccion.ConfiguracionInvalidada,
                FechaInvalidacion = inspeccion.FechaInvalidacion,
                UsuarioInvalidacionID = inspeccion.UsuarioInvalidacionID,
                MotivoInvalidacion = inspeccion.MotivoInvalidacion,
                Observaciones = inspeccion.Observaciones,
                Estado = inspeccion.Estado,
                UsuarioCreacionID = inspeccion.UsuarioCreacionID,
                FechaCreacion = inspeccion.FechaCreacion,
                UsuarioModificacionID = inspeccion.UsuarioModificacionID,
                FechaModificacion = inspeccion.FechaModificacion,
                Historial = inspeccion.Historial
                    .OrderByDescending(x => x.FechaMovimiento)
                    .Select(x => new CalidadHistorialItemViewModel
                    {
                        HistorialID = x.HistorialID,
                        Movimiento = x.Movimiento,
                        EstadoAnterior = x.EstadoAnterior,
                        EstadoNuevo = x.EstadoNuevo,
                        ResultadoCalidad = x.ResultadoCalidad,
                        Etiqueta = x.Etiqueta,
                        Comentario = x.Comentario,
                        UsuarioID = x.UsuarioID,
                        FechaMovimiento = x.FechaMovimiento
                    })
                    .ToList()
            };

            model.IntentosPrimerasPiezas = await _context.CalidadPrimerasPiezasIntentos
                .AsNoTracking()
                .Where(x => x.InspeccionID == id && x.Activo)
                .OrderByDescending(x => x.NumeroIntento)
                .Select(x => new CalidadPrimeraPiezaIntentoItemViewModel
                {
                    IntentoID = x.IntentoID,
                    NumeroIntento = x.NumeroIntento,
                    FechaInicio = x.FechaInicio,
                    FechaFin = x.FechaFin,
                    CincoDisparosSegregados = x.CincoDisparosSegregados,
                    CantidadDisparosPresentados = x.CantidadDisparosPresentados,
                    ValidacionDimensional = x.ValidacionDimensional,
                    ValidacionApariencia = x.ValidacionApariencia,
                    ValidacionGauge = x.ValidacionGauge,
                    ValidacionConductividad = x.ValidacionConductividad,
                    Resultado = x.Resultado,
                    AjusteSolicitado = x.AjusteSolicitado,
                    Observaciones = x.Observaciones,
                    UsuarioCalidadID = x.UsuarioCalidadID
                })
                .ToListAsync();

            model.Monitoreos = await _context.CalidadMonitoreosProceso
                .AsNoTracking()
                .Where(x => x.InspeccionID == id && x.Activo)
                .OrderBy(x => x.NumeroHora)
                .Select(x => new CalidadMonitoreoItemViewModel
                {
                    MonitoreoID = x.MonitoreoID,
                    NumeroHora = x.NumeroHora,
                    FechaHoraProgramada = x.FechaHoraProgramada,
                    FechaHoraRevision = x.FechaHoraRevision,
                    CantidadProducidaPeriodo = x.CantidadProducidaPeriodo,
                    CantidadRevisadaMuestra = x.CantidadRevisadaMuestra,
                    Resultado = x.Resultado,
                    DefectoCodigo = x.DefectoCodigo,
                    DefectoDescripcion = x.DefectoDescripcion,
                    CantidadSospechosa = x.CantidadSospechosa,
                    CantidadNoRecuperable = x.CantidadNoRecuperable,
                    RequiereSeleccion = x.RequiereSeleccion,
                    RequiereRetrabajo = x.RequiereRetrabajo,
                    ResponsableRetrabajo = x.ResponsableRetrabajo,
                    Observaciones = x.Observaciones
                })
                .ToListAsync();

            model.Disposiciones = await _context.CalidadDisposicionesMaterial
                .AsNoTracking()
                .Where(x => x.InspeccionID == id && x.Activo)
                .OrderByDescending(x => x.FechaInicio)
                .Select(x => new CalidadDisposicionItemViewModel
                {
                    DisposicionID = x.DisposicionID,
                    MonitoreoID = x.MonitoreoID,
                    TipoMaterial = x.TipoMaterial,
                    CantidadAfectada = x.CantidadAfectada,
                    Etiqueta = x.Etiqueta,
                    Disposicion = x.Disposicion,
                    Responsable = x.Responsable,
                    FechaInicio = x.FechaInicio,
                    FechaFin = x.FechaFin,
                    CantidadLiberada = x.CantidadLiberada,
                    CantidadScrap = x.CantidadScrap,
                    ResultadoFinal = x.ResultadoFinal,
                    Observaciones = x.Observaciones
                })
                .ToListAsync();

            model.Cajas = await _context.CalidadCajasLiberadas
                .AsNoTracking()
                .Where(x => x.InspeccionID == id && x.Activo)
                .OrderByDescending(x => x.FechaCreacion)
                .Select(x => new CalidadCajaItemViewModel
                {
                    CajaLiberadaID = x.CajaLiberadaID,
                    FolioCaja = x.FolioCaja,
                    CantidadPiezas = x.CantidadPiezas,
                    EstandarPackCumple = x.EstandarPackCumple,
                    EtiquetaProductoCorrecta = x.EtiquetaProductoCorrecta,
                    NumeroOperadorEtiqueta = x.NumeroOperadorEtiqueta,
                    TecnicoConfirmoInformacion = x.TecnicoConfirmoInformacion,
                    FechaValidacionCalidad = x.FechaValidacionCalidad,
                    Tarima = x.Tarima,
                    Destino = x.Destino,
                    Estado = x.Estado
                })
                .ToListAsync();

            model.Reliberaciones = await _context.CalidadReliberaciones
                .AsNoTracking()
                .Where(x => x.InspeccionID == id && x.Activo)
                .OrderByDescending(x => x.NumeroReliberacion)
                .Select(x => new CalidadReliberacionItemViewModel
                {
                    ReliberacionID = x.ReliberacionID,
                    ParoID = x.ParoID,
                    NumeroReliberacion = x.NumeroReliberacion,
                    Motivo = x.Motivo,
                    FechaSolicitud = x.FechaSolicitud,
                    FechaValidacion = x.FechaValidacion,
                    Resultado = x.Resultado,
                    Observaciones = x.Observaciones
                })
                .ToListAsync();

            model.PreguntasChecklistCalidad = await ObtenerPreguntasChecklistCalidadAsync(
                inspeccion.ChecklistArranqueID);

            return model;
        }

        private async Task<List<CalidadChecklistPreguntaViewModel>> ObtenerPreguntasChecklistCalidadAsync(
            int? checklistArranqueId)
        {
            var lista = new List<CalidadChecklistPreguntaViewModel>();
            if (!checklistArranqueId.HasValue) return lista;

            const string sql = @"
SELECT
    d.ChecklistArranqueDetalleID,
    d.PreguntaID,
    p.Seccion,
    p.OrdenSeccion,
    p.OrdenPregunta,
    p.TextoPregunta,
    p.RequiereObservacionSiNOK,
    d.Resultado,
    d.Observaciones
FROM dbo.Produccion_ChecklistArranqueDetalle d
INNER JOIN dbo.ERP_ChecklistArranquePreguntas p
    ON p.PreguntaID = d.PreguntaID
WHERE d.ChecklistArranqueID = @ChecklistArranqueID
  AND d.Activo = 1
  AND p.Activo = 1
  AND
  (
      UPPER(ISNULL(p.Seccion, '')) LIKE '%CALIDAD%'
      OR UPPER(ISNULL(p.ResponsableSugerido, '')) LIKE '%CALIDAD%'
  )
ORDER BY p.OrdenSeccion, p.OrdenPregunta;";

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@ChecklistArranqueID", SqlDbType.Int).Value = checklistArranqueId.Value;
            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                lista.Add(new CalidadChecklistPreguntaViewModel
                {
                    ChecklistArranqueDetalleID = Convert.ToInt32(rd["ChecklistArranqueDetalleID"]),
                    PreguntaID = Convert.ToInt32(rd["PreguntaID"]),
                    Seccion = rd["Seccion"] as string ?? string.Empty,
                    OrdenSeccion = Convert.ToInt32(rd["OrdenSeccion"]),
                    OrdenPregunta = Convert.ToInt32(rd["OrdenPregunta"]),
                    TextoPregunta = rd["TextoPregunta"] as string ?? string.Empty,
                    RequiereObservacionSiNOK = rd["RequiereObservacionSiNOK"] != DBNull.Value && Convert.ToBoolean(rd["RequiereObservacionSiNOK"]),
                    Resultado = rd["Resultado"] as string,
                    Observaciones = rd["Observaciones"] as string
                });
            }

            return lista;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GuardarChecklistCalidad(
            CalidadChecklistGuardarViewModel model)
        {
            if (!ModelState.IsValid)
            {
                TempData["Error"] = "No se recibio correctamente el checklist de Calidad.";
                return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
            }

            var usuarioId = ObtenerUsuarioIdActual();
            if (!usuarioId.HasValue)
                return Unauthorized();

            var inspeccion = await _context.CalidadInspecciones
                .FirstOrDefaultAsync(x =>
                    x.InspeccionID == model.InspeccionID &&
                    x.ChecklistArranqueID == model.ChecklistArranqueID);

            if (inspeccion == null)
                return NotFound();

            if (!CalidadEstados.PuedeAutorizarPrearranque(inspeccion.Estado))
            {
                TempData["Error"] = "El checklist ya no se encuentra disponible para revision de prearranque.";
                return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
            }

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();

            try
            {
                foreach (var respuesta in model.Respuestas ?? new List<CalidadChecklistRespuestaViewModel>())
                {
                    var resultado = NormalizarResultadoCalidad(respuesta.Resultado);
                    if (resultado == "__INVALIDO__")
                        throw new InvalidOperationException("Se recibio una respuesta invalida en el checklist de Calidad.");

                    if (resultado == "NOK" && string.IsNullOrWhiteSpace(respuesta.Observaciones))
                        throw new InvalidOperationException("Toda respuesta NOK del auditor de Calidad requiere observacion.");

                    const string sqlUpdate = @"
UPDATE d
SET
    d.Resultado = @Resultado,
    d.Observaciones = @Observaciones,
    d.UsuarioRespuestaID = @UsuarioID,
    d.FechaRespuesta = GETDATE(),
    d.UsuarioModificacionID = @UsuarioID,
    d.FechaModificacion = GETDATE()
FROM dbo.Produccion_ChecklistArranqueDetalle d
INNER JOIN dbo.ERP_ChecklistArranquePreguntas p
    ON p.PreguntaID = d.PreguntaID
WHERE d.ChecklistArranqueDetalleID = @DetalleID
  AND d.ChecklistArranqueID = @ChecklistArranqueID
  AND d.Activo = 1
  AND p.Activo = 1
  AND
  (
      UPPER(ISNULL(p.Seccion, '')) LIKE '%CALIDAD%'
      OR UPPER(ISNULL(p.ResponsableSugerido, '')) LIKE '%CALIDAD%'
  );";

                    await using var cmd = new SqlCommand(sqlUpdate, cn, tx);
                    cmd.Parameters.Add("@Resultado", SqlDbType.NVarChar, 10).Value = (object?)resultado ?? DBNull.Value;
                    cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 500).Value =
                        string.IsNullOrWhiteSpace(respuesta.Observaciones)
                            ? DBNull.Value
                            : respuesta.Observaciones.Trim();
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId.Value;
                    cmd.Parameters.Add("@DetalleID", SqlDbType.Int).Value = respuesta.ChecklistArranqueDetalleID;
                    cmd.Parameters.Add("@ChecklistArranqueID", SqlDbType.Int).Value = model.ChecklistArranqueID;
                    await cmd.ExecuteNonQueryAsync();
                }

                const string sqlHeader = @"
UPDATE dbo.Produccion_ChecklistArranque
SET
    UsuarioCalidadID = @UsuarioID,
    FechaValidacionCalidad = GETDATE(),
    ObservacionesCalidad = @ObservacionesCalidad,
    UsuarioModificacionID = @UsuarioID,
    FechaModificacion = GETDATE()
WHERE ChecklistArranqueID = @ChecklistArranqueID
  AND Activo = 1;";

                await using (var cmd = new SqlCommand(sqlHeader, cn, tx))
                {
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId.Value;
                    cmd.Parameters.Add("@ObservacionesCalidad", SqlDbType.NVarChar, 1000).Value =
                        string.IsNullOrWhiteSpace(model.ObservacionesCalidad)
                            ? DBNull.Value
                            : model.ObservacionesCalidad.Trim();
                    cmd.Parameters.Add("@ChecklistArranqueID", SqlDbType.Int).Value = model.ChecklistArranqueID;
                    await cmd.ExecuteNonQueryAsync();
                }

                await tx.CommitAsync();

                inspeccion.FechaInicioValidacionPrearranque ??= DateTime.Now;
                MarcarModificacion(inspeccion, usuarioId);
                AgregarHistorial(
                    inspeccion,
                    CalidadMovimientos.ChecklistCalidadCapturado,
                    inspeccion.Estado,
                    inspeccion.Estado,
                    inspeccion.ResultadoCalidad,
                    inspeccion.Etiqueta,
                    "El auditor guardo su seccion del checklist de arranque.",
                    usuarioId);
                await _context.SaveChangesAsync();

                TempData["Mensaje"] = "Seccion de Calidad guardada correctamente.";
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                TempData["Error"] = "No fue posible guardar el checklist de Calidad: " + ex.Message;
            }

            return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AutorizarPrearranqueFlujo(
            CalidadPrearranqueViewModel model)
        {
            if (!ModelState.IsValid)
            {
                TempData["Error"] = "No se recibio una inspeccion valida.";
                return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
            }

            if (!model.AyudaVisualColocada ||
                !model.HIPColocada ||
                !model.HCCColocada ||
                !model.MatrizPolivalenciaValidada)
            {
                TempData["Error"] = "Confirma ayuda visual, HIP, HCC y matriz de polivalencia antes de autorizar.";
                return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
            }

            if (model.AlertaCalidadAplica == true && model.AlertaCalidadColocada != true)
            {
                TempData["Error"] = "La alerta de Calidad aplica y debe confirmarse como colocada.";
                return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
            }

            var inspeccion = await _context.CalidadInspecciones
                .FirstOrDefaultAsync(x => x.InspeccionID == model.InspeccionID);

            if (inspeccion == null)
                return NotFound();

            if (!CalidadEstados.PuedeAutorizarPrearranque(inspeccion.Estado))
            {
                TempData["Error"] = "La inspeccion ya no esta pendiente de prearranque.";
                return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
            }

            if (!inspeccion.ChecklistArranqueID.HasValue)
            {
                TempData["Error"] = "La inspeccion no tiene un checklist de Produccion relacionado.";
                return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
            }

            var validacionConfiguracion = await ValidarConfiguracionActualAsync(inspeccion);
            if (!validacionConfiguracion.Valida)
            {
                await InvalidarConfiguracionAsync(inspeccion, validacionConfiguracion.Motivo);
                TempData["Error"] = validacionConfiguracion.Motivo;
                return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
            }

            var validacionChecklist = await ValidarChecklistCalidadCompletoAsync(inspeccion.ChecklistArranqueID.Value);
            if (!validacionChecklist.Valido)
            {
                TempData["Error"] = validacionChecklist.Mensaje;
                return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
            }

            var usuarioId = ObtenerUsuarioIdActual();
            if (!usuarioId.HasValue) return Unauthorized();

            await using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                var estadoAnterior = inspeccion.Estado;
                var ahora = DateTime.Now;

                inspeccion.AyudaVisualColocada = model.AyudaVisualColocada;
                inspeccion.AlertaCalidadAplica = model.AlertaCalidadAplica;
                inspeccion.AlertaCalidadColocada = model.AlertaCalidadColocada;
                inspeccion.HIPColocada = model.HIPColocada;
                inspeccion.HCCColocada = model.HCCColocada;
                inspeccion.MatrizPolivalenciaValidada = model.MatrizPolivalenciaValidada;
                inspeccion.ChecklistValidado = true;
                inspeccion.HojaInspeccionProducto = true;
                inspeccion.HojaValidacionCalidad = true;
                inspeccion.FechaInicioValidacionPrearranque ??= ahora;
                inspeccion.FechaFinValidacionPrearranque = ahora;
                inspeccion.FechaAutorizacionPrearranque = ahora;
                inspeccion.UsuarioAutorizacionPrearranqueID = usuarioId;
                inspeccion.MotivoDevolucion = null;
                inspeccion.Estado = CalidadEstados.ArranqueAutorizado;
                MarcarModificacion(inspeccion, usuarioId);

                AgregarHistorial(
                    inspeccion,
                    CalidadMovimientos.PrearranqueAutorizado,
                    estadoAnterior,
                    inspeccion.Estado,
                    inspeccion.ResultadoCalidad,
                    inspeccion.Etiqueta,
                    string.IsNullOrWhiteSpace(model.Motivo)
                        ? "Calidad autorizo el arranque controlado."
                        : model.Motivo.Trim(),
                    usuarioId);

                await _context.SaveChangesAsync();

                await _context.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE dbo.Produccion_ChecklistArranque
SET
    EstatusID = {ProduccionChecklistEstatus.ValidadoPorCalidad},
    UsuarioCalidadID = {usuarioId.Value},
    FechaValidacionCalidad = {ahora},
    ObservacionesCalidad = {model.Motivo},
    UsuarioModificacionID = {usuarioId.Value},
    FechaModificacion = {ahora}
WHERE ChecklistArranqueID = {inspeccion.ChecklistArranqueID.Value}
  AND Activo = 1;");

                await tx.CommitAsync();
                TempData["Mensaje"] = "Prearranque autorizado. Produccion puede generar las primeras piezas, pero aun no iniciar la serie.";
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                TempData["Error"] = "No fue posible autorizar el prearranque: " + ex.Message;
            }

            return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DevolverPrearranqueFlujo(
            CalidadPrearranqueViewModel model)
        {
            model.Motivo = model.Motivo?.Trim();
            if (string.IsNullOrWhiteSpace(model.Motivo))
            {
                TempData["Error"] = "Captura el motivo de la devolucion.";
                return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
            }

            var inspeccion = await _context.CalidadInspecciones
                .FirstOrDefaultAsync(x => x.InspeccionID == model.InspeccionID);

            if (inspeccion == null) return NotFound();
            if (!CalidadEstados.PuedeAutorizarPrearranque(inspeccion.Estado))
            {
                TempData["Error"] = "La inspeccion ya no esta pendiente de prearranque.";
                return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
            }

            var usuarioId = ObtenerUsuarioIdActual();
            if (!usuarioId.HasValue) return Unauthorized();

            await using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                var estadoAnterior = inspeccion.Estado;
                inspeccion.Estado = CalidadEstados.DevueltoPrearranque;
                inspeccion.MotivoDevolucion = model.Motivo;
                inspeccion.ChecklistValidado = false;
                inspeccion.Liberado = false;
                MarcarModificacion(inspeccion, usuarioId);

                AgregarHistorial(
                    inspeccion,
                    CalidadMovimientos.PrearranqueDevuelto,
                    estadoAnterior,
                    inspeccion.Estado,
                    inspeccion.ResultadoCalidad,
                    inspeccion.Etiqueta,
                    model.Motivo,
                    usuarioId);

                await _context.SaveChangesAsync();

                if (inspeccion.ChecklistArranqueID.HasValue)
                {
                    await _context.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE dbo.Produccion_ChecklistArranque
SET
    EstatusID = {ProduccionChecklistEstatus.RechazadoRequiereAjuste},
    UsuarioCalidadID = {usuarioId.Value},
    FechaValidacionCalidad = {DateTime.Now},
    ObservacionesCalidad = {model.Motivo},
    UsuarioModificacionID = {usuarioId.Value},
    FechaModificacion = {DateTime.Now}
WHERE ChecklistArranqueID = {inspeccion.ChecklistArranqueID.Value}
  AND Activo = 1;");
                }

                await tx.CommitAsync();
                TempData["Mensaje"] = "La revision fue devuelta a Produccion para correccion.";
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                TempData["Error"] = "No fue posible devolver la revision: " + ex.Message;
            }

            return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RegistrarPrimerasPiezasFlujo(
            CalidadPrimerasPiezasViewModel model)
        {
            if (!ModelState.IsValid || !model.CincoDisparosSegregados)
            {
                TempData["Error"] = "Confirma la segregacion de los primeros cinco disparos y revisa los datos.";
                return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
            }

            var inspeccion = await _context.CalidadInspecciones
                .FirstOrDefaultAsync(x => x.InspeccionID == model.InspeccionID);
            if (inspeccion == null) return NotFound();

            if (!CalidadEstados.PuedeValidarPrimerasPiezas(inspeccion.Estado))
            {
                TempData["Error"] = "La inspeccion no permite registrar primeras piezas en su estado actual.";
                return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
            }

            var usuarioId = ObtenerUsuarioIdActual();
            if (!usuarioId.HasValue) return Unauthorized();

            var intento = await ObtenerOCrearIntentoPendienteAsync(inspeccion.InspeccionID, usuarioId.Value);
            AplicarDatosIntento(intento, model, usuarioId.Value);

            AplicarResumenPrimerasPiezas(inspeccion, model, usuarioId.Value);
            var estadoAnterior = inspeccion.Estado;
            inspeccion.Estado = CalidadEstados.PendientePrimerasPiezas;
            MarcarModificacion(inspeccion, usuarioId);

            AgregarHistorial(
                inspeccion,
                CalidadMovimientos.PrimerasPiezasRecibidas,
                estadoAnterior,
                inspeccion.Estado,
                inspeccion.ResultadoCalidad,
                inspeccion.Etiqueta,
                string.IsNullOrWhiteSpace(model.Observaciones)
                    ? $"Se registro el intento {intento.NumeroIntento} de primeras piezas."
                    : model.Observaciones.Trim(),
                usuarioId);

            await _context.SaveChangesAsync();
            TempData["Mensaje"] = $"Intento {intento.NumeroIntento} de primeras piezas guardado.";
            return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SolicitarAjustesFlujo(
            CalidadPrimerasPiezasViewModel model)
        {
            model.Observaciones = model.Observaciones?.Trim();
            if (string.IsNullOrWhiteSpace(model.Observaciones))
            {
                TempData["Error"] = "Describe los ajustes requeridos.";
                return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
            }

            var inspeccion = await _context.CalidadInspecciones
                .FirstOrDefaultAsync(x => x.InspeccionID == model.InspeccionID);
            if (inspeccion == null) return NotFound();

            if (!CalidadEstados.PuedeValidarPrimerasPiezas(inspeccion.Estado))
            {
                TempData["Error"] = "La inspeccion no permite solicitar ajustes en su estado actual.";
                return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
            }

            var usuarioId = ObtenerUsuarioIdActual();
            if (!usuarioId.HasValue) return Unauthorized();

            var intento = await ObtenerOCrearIntentoPendienteAsync(inspeccion.InspeccionID, usuarioId.Value);
            AplicarDatosIntento(intento, model, usuarioId.Value);
            intento.Resultado = CalidadResultadoIntento.Nok;
            intento.AjusteSolicitado = true;
            intento.FechaFin = DateTime.Now;

            AplicarResumenPrimerasPiezas(inspeccion, model, usuarioId.Value);
            var estadoAnterior = inspeccion.Estado;
            inspeccion.ResultadoCalidad = "NOK";
            inspeccion.Etiqueta = null;
            inspeccion.Liberado = false;
            inspeccion.Estado = CalidadEstados.AjustesSolicitados;
            inspeccion.Observaciones = model.Observaciones;
            MarcarModificacion(inspeccion, usuarioId);

            AgregarHistorial(
                inspeccion,
                CalidadMovimientos.AjustesSolicitados,
                estadoAnterior,
                inspeccion.Estado,
                inspeccion.ResultadoCalidad,
                inspeccion.Etiqueta,
                $"Intento {intento.NumeroIntento} NOK. {model.Observaciones}",
                usuarioId);

            await _context.SaveChangesAsync();
            TempData["Mensaje"] = "Ajustes solicitados a Produccion. El intento quedo registrado como NOK.";
            return RedirectToAction(nameof(Detalle), new { id = model.InspeccionID });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LiberarProduccionFlujo(int id)
        {
            var inspeccion = await _context.CalidadInspecciones
                .FirstOrDefaultAsync(x => x.InspeccionID == id);
            if (inspeccion == null) return NotFound();

            if (inspeccion.ConfiguracionInvalidada)
            {
                TempData["Error"] = "La configuracion fue invalidada y no puede liberarse.";
                return RedirectToAction(nameof(Detalle), new { id });
            }

            var validacionConfiguracion = await ValidarConfiguracionActualAsync(inspeccion);
            if (!validacionConfiguracion.Valida)
            {
                await InvalidarConfiguracionAsync(inspeccion, validacionConfiguracion.Motivo);
                TempData["Error"] = validacionConfiguracion.Motivo;
                return RedirectToAction(nameof(Detalle), new { id });
            }

            var intento = await _context.CalidadPrimerasPiezasIntentos
                .Where(x => x.InspeccionID == id && x.Activo)
                .OrderByDescending(x => x.NumeroIntento)
                .FirstOrDefaultAsync();

            if (intento == null)
            {
                TempData["Error"] = "Primero registra la validacion de las primeras piezas.";
                return RedirectToAction(nameof(Detalle), new { id });
            }

            if (!intento.CincoDisparosSegregados ||
                intento.CantidadDisparosPresentados < 3 ||
                intento.ValidacionDimensional != true ||
                intento.ValidacionApariencia != true ||
                intento.ValidacionGauge == false ||
                intento.ValidacionConductividad == false)
            {
                TempData["Error"] = "El ultimo intento no cumple los requisitos para liberar la produccion.";
                return RedirectToAction(nameof(Detalle), new { id });
            }

            var usuarioId = ObtenerUsuarioIdActual();
            if (!usuarioId.HasValue) return Unauthorized();

            await using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                var ahora = DateTime.Now;
                var estadoAnterior = inspeccion.Estado;

                intento.Resultado = CalidadResultadoIntento.Ok;
                intento.AjusteSolicitado = false;
                intento.FechaFin = ahora;
                intento.UsuarioModificacionID = usuarioId;
                intento.FechaModificacion = ahora;

                inspeccion.CincoDisparosSegregados = intento.CincoDisparosSegregados;
                inspeccion.CantidadDisparosConformes = intento.CantidadDisparosPresentados;
                inspeccion.ValidacionDimensional = intento.ValidacionDimensional;
                inspeccion.ValidacionApariencia = intento.ValidacionApariencia;
                inspeccion.ValidacionGauge = intento.ValidacionGauge;
                inspeccion.ValidacionConductividad = intento.ValidacionConductividad;
                inspeccion.ResultadoCalidad = "VERDE";
                inspeccion.Etiqueta = "VERDE";
                inspeccion.Liberado = true;
                inspeccion.RequiereGP12 = false;
                inspeccion.EnContencion = false;
                inspeccion.EsScrap = false;
                inspeccion.Estado = CalidadEstados.ProduccionLiberada;
                inspeccion.FechaLiberacionProduccion = ahora;
                inspeccion.UsuarioLiberacionProduccionID = usuarioId;
                inspeccion.FechaValidacionPrimerasPiezas = ahora;
                inspeccion.UsuarioValidacionPrimerasPiezasID = usuarioId;

                if (inspeccion.FechaNotificacionCalidad.HasValue)
                {
                    var minutos = (int)Math.Max(0, Math.Round((ahora - inspeccion.FechaNotificacionCalidad.Value).TotalMinutes));
                    inspeccion.MinutosLiberacionInicial = minutos;
                    inspeccion.CumplioTiempoObjetivoInicial = minutos >= 10 && minutos <= 20;
                }

                MarcarModificacion(inspeccion, usuarioId);

                await GenerarMonitoreosHorariosAsync(inspeccion, ahora, usuarioId.Value);

                AgregarHistorial(
                    inspeccion,
                    CalidadMovimientos.ProduccionLiberada,
                    estadoAnterior,
                    inspeccion.Estado,
                    inspeccion.ResultadoCalidad,
                    inspeccion.Etiqueta,
                    $"Intento {intento.NumeroIntento} conforme. Calidad asigno etiqueta verde.",
                    usuarioId);

                await _context.SaveChangesAsync();
                await tx.CommitAsync();
                TempData["Mensaje"] = "Produccion liberada con etiqueta verde. Ya puede iniciarse la serie.";
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                TempData["Error"] = "No fue posible liberar la produccion: " + ex.Message;
            }

            return RedirectToAction(nameof(Detalle), new { id });
        }

        private async Task<(bool Valido, string Mensaje)> ValidarChecklistCalidadCompletoAsync(
            int checklistArranqueId)
        {
            const string sql = @"
SELECT
    SUM(CASE WHEN d.Resultado IS NULL OR LTRIM(RTRIM(d.Resultado)) = '' THEN 1 ELSE 0 END) AS SinRespuesta,
    SUM(CASE WHEN d.Resultado = 'NOK' THEN 1 ELSE 0 END) AS TotalNOK,
    SUM(CASE WHEN d.Resultado = 'NOK' AND (d.Observaciones IS NULL OR LTRIM(RTRIM(d.Observaciones)) = '') THEN 1 ELSE 0 END) AS NokSinObservacion,
    COUNT(1) AS TotalPreguntas
FROM dbo.Produccion_ChecklistArranqueDetalle d
INNER JOIN dbo.ERP_ChecklistArranquePreguntas p
    ON p.PreguntaID = d.PreguntaID
WHERE d.ChecklistArranqueID = @ChecklistArranqueID
  AND d.Activo = 1
  AND p.Activo = 1
  AND
  (
      UPPER(ISNULL(p.Seccion, '')) LIKE '%CALIDAD%'
      OR UPPER(ISNULL(p.ResponsableSugerido, '')) LIKE '%CALIDAD%'
  );";

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@ChecklistArranqueID", SqlDbType.Int).Value = checklistArranqueId;
            await using var rd = await cmd.ExecuteReaderAsync();

            if (!await rd.ReadAsync() || Convert.ToInt32(rd["TotalPreguntas"]) == 0)
                return (false, "No se encontraron preguntas asignadas al auditor de Calidad.");

            if (Convert.ToInt32(rd["SinRespuesta"]) > 0)
                return (false, "Responde todas las preguntas del auditor de Calidad antes de autorizar.");

            if (Convert.ToInt32(rd["NokSinObservacion"]) > 0)
                return (false, "Existen respuestas NOK sin observacion.");

            if (Convert.ToInt32(rd["TotalNOK"]) > 0)
                return (false, "El checklist contiene resultados NOK. Debe devolverse a Produccion.");

            return (true, string.Empty);
        }

        private async Task<CalidadPrimeraPiezaIntento> ObtenerOCrearIntentoPendienteAsync(
            int inspeccionId,
            int usuarioId)
        {
            var existente = await _context.CalidadPrimerasPiezasIntentos
                .Where(x =>
                    x.InspeccionID == inspeccionId &&
                    x.Activo &&
                    x.Resultado == CalidadResultadoIntento.Pendiente)
                .OrderByDescending(x => x.NumeroIntento)
                .FirstOrDefaultAsync();

            if (existente != null)
                return existente;

            var numero = (await _context.CalidadPrimerasPiezasIntentos
                .Where(x => x.InspeccionID == inspeccionId)
                .MaxAsync(x => (int?)x.NumeroIntento) ?? 0) + 1;

            var intento = new CalidadPrimeraPiezaIntento
            {
                InspeccionID = inspeccionId,
                NumeroIntento = numero,
                FechaInicio = DateTime.Now,
                Resultado = CalidadResultadoIntento.Pendiente,
                UsuarioCalidadID = usuarioId,
                UsuarioCreacionID = usuarioId,
                FechaCreacion = DateTime.Now,
                Activo = true
            };

            _context.CalidadPrimerasPiezasIntentos.Add(intento);
            return intento;
        }

        private static void AplicarDatosIntento(
            CalidadPrimeraPiezaIntento intento,
            CalidadPrimerasPiezasViewModel model,
            int usuarioId)
        {
            intento.CincoDisparosSegregados = model.CincoDisparosSegregados;
            intento.CantidadDisparosPresentados = model.CantidadDisparosConformes;
            intento.ValidacionDimensional = model.ValidacionDimensional;
            intento.ValidacionApariencia = model.ValidacionApariencia;
            intento.ValidacionGauge = model.ValidacionGauge;
            intento.ValidacionConductividad = model.ValidacionConductividad;
            intento.Observaciones = model.Observaciones?.Trim();
            intento.UsuarioCalidadID = usuarioId;
            intento.UsuarioModificacionID = usuarioId;
            intento.FechaModificacion = DateTime.Now;
        }

        private static void AplicarResumenPrimerasPiezas(
            CalidadInspeccion inspeccion,
            CalidadPrimerasPiezasViewModel model,
            int usuarioId)
        {
            inspeccion.CincoDisparosSegregados = model.CincoDisparosSegregados;
            inspeccion.CantidadDisparosConformes = model.CantidadDisparosConformes;
            inspeccion.ValidacionDimensional = model.ValidacionDimensional;
            inspeccion.ValidacionApariencia = model.ValidacionApariencia;
            inspeccion.ValidacionGauge = model.ValidacionGauge;
            inspeccion.ValidacionConductividad = model.ValidacionConductividad;
            inspeccion.FechaValidacionPrimerasPiezas = DateTime.Now;
            inspeccion.UsuarioValidacionPrimerasPiezasID = usuarioId;
            if (!string.IsNullOrWhiteSpace(model.Observaciones))
                inspeccion.Observaciones = model.Observaciones.Trim();
        }

        private async Task GenerarMonitoreosHorariosAsync(
            CalidadInspeccion inspeccion,
            DateTime fechaLiberacion,
            int usuarioId)
        {
            if (!inspeccion.EjecucionProduccionID.HasValue)
                throw new InvalidOperationException("La inspeccion no tiene ejecucion de Produccion relacionada.");

            var yaExisten = await _context.CalidadMonitoreosProceso
                .AnyAsync(x => x.InspeccionID == inspeccion.InspeccionID && x.Activo);
            if (yaExisten) return;

            var horas = 9;
            if (inspeccion.FechaInicioProgramada.HasValue && inspeccion.FechaFinProgramada.HasValue)
            {
                var duracion = (inspeccion.FechaFinProgramada.Value - inspeccion.FechaInicioProgramada.Value).TotalHours;
                if (duracion > 0)
                    horas = Math.Clamp((int)Math.Ceiling(duracion), 1, 9);
            }

            for (var numeroHora = 1; numeroHora <= horas; numeroHora++)
            {
                _context.CalidadMonitoreosProceso.Add(new CalidadMonitoreoProceso
                {
                    InspeccionID = inspeccion.InspeccionID,
                    EjecucionProduccionID = inspeccion.EjecucionProduccionID.Value,
                    NumeroHora = numeroHora,
                    FechaHoraProgramada = fechaLiberacion.AddHours(numeroHora),
                    Resultado = CalidadResultadoMonitoreo.Pendiente,
                    UsuarioCreacionID = usuarioId,
                    FechaCreacion = DateTime.Now,
                    Activo = true
                });
            }
        }

        private static string? NormalizarResultadoCalidad(string? resultado)
        {
            if (string.IsNullOrWhiteSpace(resultado)) return null;
            var valor = resultado.Trim().ToUpperInvariant();
            if (valor == "OK") return "OK";
            if (valor == "NOK") return "NOK";
            if (valor == "NA" || valor == "N/A") return "NA";
            return "__INVALIDO__";
        }
    }
}
