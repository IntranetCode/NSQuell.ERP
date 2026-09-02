using ERP.NSQuell.Models.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Globalization;

namespace ERP.NSQuell.Controllers;

public partial class CalidadController
{
    private const string HccPendiente = "PENDIENTE";
    private const string HccCompletada = "COMPLETADA";

    [HttpGet]
    public async Task<IActionResult> HojasControl(string? busqueda, string? estado)
    {
        var usuarioId = ObtenerUsuarioIdActual();
        if (!usuarioId.HasValue || usuarioId.Value <= 0) return Unauthorized();

        await SincronizarRequerimientosHccAsync();

        busqueda = string.IsNullOrWhiteSpace(busqueda) ? null : busqueda.Trim();
        estado = string.IsNullOrWhiteSpace(estado) ? "PENDIENTE" : estado.Trim().ToUpperInvariant();
        if (estado is not ("PENDIENTE" or "COMPLETADA" or "TODOS")) estado = "PENDIENTE";

        var vm = new CalidadHCCBandejaViewModel
        {
            Busqueda = busqueda,
            Estado = estado
        };

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync();

        const string sqlResumen = @"
SELECT
    SUM(CASE WHEN Activo=1 AND Estado IN(N'PENDIENTE',N'EN_CAPTURA') THEN 1 ELSE 0 END) Pendientes,
    SUM(CASE WHEN Activo=1 AND Estado=N'COMPLETADA' AND CONVERT(date,FechaModificacion)=CONVERT(date,GETDATE()) THEN 1 ELSE 0 END) CompletadasHoy
FROM dbo.Calidad_HCC_Requerimientos;";

        await using (var cmd = new SqlCommand(sqlResumen, cn))
        await using (var rd = await cmd.ExecuteReaderAsync())
        {
            if (await rd.ReadAsync())
            {
                vm.Pendientes = rd["Pendientes"] == DBNull.Value ? 0 : Convert.ToInt32(rd["Pendientes"]);
                vm.CompletadasHoy = rd["CompletadasHoy"] == DBNull.Value ? 0 : Convert.ToInt32(rd["CompletadasHoy"]);
            }
        }

        const string sql = @"
SELECT
    r.RequerimientoHCCID,r.PlantillaHCCID,r.ParteID,r.InspeccionID,r.EjecucionProduccionID,r.CambioTurnoID,
    r.TipoOrigen,r.TipoEventoSugerido,r.OrdenFabricacion,r.ClienteNombre,r.NumeroParte,r.DescripcionParte,
    r.MaquinaTexto,r.Turno,r.OperadorTexto,r.FechaHoraRequerida,r.Estado,r.RegistroHCCID,
    ISNULL(h.NumeroHCC,N'') NumeroHCC,ISNULL(h.VersionFormato,N'') VersionFormato,
    (SELECT COUNT(*) FROM dbo.Calidad_HCC_Caracteristicas c WHERE c.PlantillaHCCID=r.PlantillaHCCID AND c.Activo=1) Caracteristicas,
    (SELECT COUNT(*) FROM dbo.Calidad_HCC_Checklist k WHERE k.PlantillaHCCID=r.PlantillaHCCID AND k.Activo=1) ChecklistItems
FROM dbo.Calidad_HCC_Requerimientos r
INNER JOIN dbo.Calidad_HCC_Plantillas h ON h.PlantillaHCCID=r.PlantillaHCCID
WHERE r.Activo=1
  AND
  (
      @Estado=N'TODOS'
      OR (@Estado=N'PENDIENTE' AND r.Estado IN(N'PENDIENTE',N'EN_CAPTURA'))
      OR (@Estado=N'COMPLETADA' AND r.Estado=N'COMPLETADA')
  )
  AND
  (
      @Busqueda IS NULL
      OR r.OrdenFabricacion LIKE N'%'+@Busqueda+N'%'
      OR r.NumeroParte LIKE N'%'+@Busqueda+N'%'
      OR r.DescripcionParte LIKE N'%'+@Busqueda+N'%'
      OR r.ClienteNombre LIKE N'%'+@Busqueda+N'%'
      OR r.MaquinaTexto LIKE N'%'+@Busqueda+N'%'
      OR h.NumeroHCC LIKE N'%'+@Busqueda+N'%'
  )
ORDER BY
    CASE WHEN r.Estado IN(N'PENDIENTE',N'EN_CAPTURA') THEN 0 ELSE 1 END,
    CASE r.TipoOrigen WHEN N'RELIBERACION' THEN 0 WHEN N'ARRANQUE' THEN 1 WHEN N'CAMBIO_TURNO' THEN 2 ELSE 3 END,
    r.FechaHoraRequerida DESC,r.RequerimientoHCCID DESC;";

        await using (var cmd = new SqlCommand(sql, cn))
        {
            cmd.Parameters.Add("@Estado", SqlDbType.NVarChar, 20).Value = estado;
            cmd.Parameters.Add("@Busqueda", SqlDbType.NVarChar, 300).Value = (object?)busqueda ?? DBNull.Value;
            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                vm.Requerimientos.Add(new CalidadHCCRequerimientoItemViewModel
                {
                    RequerimientoHCCID = Convert.ToInt64(rd["RequerimientoHCCID"]),
                    PlantillaHCCID = Convert.ToInt32(rd["PlantillaHCCID"]),
                    ParteID = Convert.ToInt32(rd["ParteID"]),
                    InspeccionID = HccNullableInt(rd["InspeccionID"]),
                    EjecucionProduccionID = HccNullableInt(rd["EjecucionProduccionID"]),
                    CambioTurnoID = HccNullableInt(rd["CambioTurnoID"]),
                    TipoOrigen = HccTexto(rd["TipoOrigen"]),
                    TipoEventoSugerido = HccTexto(rd["TipoEventoSugerido"]),
                    OrdenFabricacion = HccTexto(rd["OrdenFabricacion"]),
                    ClienteNombre = HccTexto(rd["ClienteNombre"]),
                    NumeroParte = HccTexto(rd["NumeroParte"]),
                    DescripcionParte = HccTexto(rd["DescripcionParte"]),
                    MaquinaTexto = HccTexto(rd["MaquinaTexto"]),
                    Turno = HccTexto(rd["Turno"]),
                    OperadorTexto = HccTexto(rd["OperadorTexto"]),
                    FechaHoraRequerida = Convert.ToDateTime(rd["FechaHoraRequerida"]),
                    Estado = HccTexto(rd["Estado"]),
                    RegistroHCCID = rd["RegistroHCCID"] == DBNull.Value ? null : Convert.ToInt64(rd["RegistroHCCID"]),
                    NumeroHCC = HccTexto(rd["NumeroHCC"]),
                    VersionFormato = HccTexto(rd["VersionFormato"]),
                    Caracteristicas = Convert.ToInt32(rd["Caracteristicas"]),
                    ChecklistItems = Convert.ToInt32(rd["ChecklistItems"])
                });
            }
        }

        const string sqlSinPlantilla = @"
SELECT TOP(30)
    i.InspeccionID,i.ParteID,i.OrdenTrabajo,i.NumeroParte,
    ISNULL(p.Descripcion,N'') DescripcionParte,ISNULL(i.ClienteNombre,N'') ClienteNombre,ISNULL(i.Maquina,N'') Maquina
FROM dbo.Calidad_Inspecciones i
LEFT JOIN dbo.ERP_Partes p ON p.ParteID=i.ParteID
WHERE UPPER(LTRIM(RTRIM(ISNULL(i.Estado,N''))))<>N'CERRADA'
  AND i.ParteID IS NOT NULL
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.Calidad_HCC_PlantillaPartes pp
      INNER JOIN dbo.Calidad_HCC_Plantillas h ON h.PlantillaHCCID=pp.PlantillaHCCID
      WHERE pp.ParteID=i.ParteID AND pp.Activo=1 AND h.Activo=1
  )
ORDER BY i.InspeccionID DESC;";

        await using (var cmd = new SqlCommand(sqlSinPlantilla, cn))
        await using (var rd = await cmd.ExecuteReaderAsync())
        {
            while (await rd.ReadAsync())
            {
                vm.InspeccionesSinPlantilla.Add(new CalidadHCCSinPlantillaViewModel
                {
                    InspeccionID = Convert.ToInt32(rd["InspeccionID"]),
                    ParteID = HccNullableInt(rd["ParteID"]),
                    OrdenFabricacion = HccTexto(rd["OrdenTrabajo"]),
                    NumeroParte = HccTexto(rd["NumeroParte"]),
                    DescripcionParte = HccTexto(rd["DescripcionParte"]),
                    ClienteNombre = HccTexto(rd["ClienteNombre"]),
                    Maquina = HccTexto(rd["Maquina"])
                });
            }
        }
        vm.SinPlantilla = vm.InspeccionesSinPlantilla.Count;
        return View("HojasControl", vm);
    }

    [HttpGet]
    public async Task<IActionResult> HCCPlantillas(string? busqueda)
    {
        busqueda = string.IsNullOrWhiteSpace(busqueda) ? null : busqueda.Trim();
        var vm = new CalidadHCCPlantillasIndexViewModel { Busqueda = busqueda };
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync();

        const string sql = @"
SELECT
    h.PlantillaHCCID,pp.ParteID,ISNULL(p.NumeroParte,N'') NumeroParte,ISNULL(p.Descripcion,N'') DescripcionParte,
    ISNULL(cli.Nombre,N'') Cliente,ISNULL(h.NumeroHCC,N'') NumeroHCC,ISNULL(h.VersionFormato,N'') VersionFormato,
    h.FechaModificacionFormato,h.EsVigente,
    (SELECT COUNT(*) FROM dbo.Calidad_HCC_Caracteristicas c WHERE c.PlantillaHCCID=h.PlantillaHCCID AND c.Activo=1) Caracteristicas,
    (SELECT COUNT(DISTINCT cc.NumeroCavidad) FROM dbo.Calidad_HCC_Caracteristicas c INNER JOIN dbo.Calidad_HCC_CaracteristicaCavidades cc ON cc.CaracteristicaHCCID=c.CaracteristicaHCCID AND cc.Activo=1 WHERE c.PlantillaHCCID=h.PlantillaHCCID AND c.Activo=1) Cavidades,
    (SELECT COUNT(*) FROM dbo.Calidad_HCC_Checklist k WHERE k.PlantillaHCCID=h.PlantillaHCCID AND k.Activo=1) ChecklistItems
FROM dbo.Calidad_HCC_PlantillaPartes pp
INNER JOIN dbo.Calidad_HCC_Plantillas h ON h.PlantillaHCCID=pp.PlantillaHCCID AND h.Activo=1
INNER JOIN dbo.ERP_Partes p ON p.ParteID=pp.ParteID
LEFT JOIN dbo.ERP_Clientes cli ON cli.ClienteID=p.ClienteID
WHERE pp.Activo=1
  AND (@Busqueda IS NULL OR p.NumeroParte LIKE N'%'+@Busqueda+N'%' OR p.ReferenciaSAP LIKE N'%'+@Busqueda+N'%' OR p.Descripcion LIKE N'%'+@Busqueda+N'%' OR cli.Nombre LIKE N'%'+@Busqueda+N'%' OR h.NumeroHCC LIKE N'%'+@Busqueda+N'%')
ORDER BY h.EsVigente DESC,cli.Nombre,p.NumeroParte,h.FechaModificacionFormato DESC,h.PlantillaHCCID DESC;";

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add("@Busqueda", SqlDbType.NVarChar, 300).Value = (object?)busqueda ?? DBNull.Value;
        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
        {
            vm.Plantillas.Add(new CalidadHCCPlantillaResumenViewModel
            {
                PlantillaHCCID = Convert.ToInt32(rd["PlantillaHCCID"]),
                ParteID = Convert.ToInt32(rd["ParteID"]),
                NumeroParte = HccTexto(rd["NumeroParte"]),
                DescripcionParte = HccTexto(rd["DescripcionParte"]),
                Cliente = HccTexto(rd["Cliente"]),
                NumeroHCC = HccTexto(rd["NumeroHCC"]),
                VersionFormato = HccTexto(rd["VersionFormato"]),
                FechaRevision = rd["FechaModificacionFormato"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaModificacionFormato"]),
                EsVigente = Convert.ToBoolean(rd["EsVigente"]),
                Caracteristicas = Convert.ToInt32(rd["Caracteristicas"]),
                Cavidades = rd["Cavidades"] == DBNull.Value ? 0 : Convert.ToInt32(rd["Cavidades"]),
                ChecklistItems = Convert.ToInt32(rd["ChecklistItems"])
            });
        }
        return View("HCCPlantillas", vm);
    }

    [HttpGet]
    public async Task<IActionResult> HojaControl(int id, int? parteId)
    {
        if (id <= 0) return NotFound();
        var plantilla = await CargarPlantillaHccAsync(id, parteId);
        return plantilla == null ? NotFound() : View("HojaControl", plantilla);
    }

    [HttpGet]
    public async Task<IActionResult> CapturarHCC(long id)
    {
        if (id <= 0) return NotFound();
        var usuarioId = ObtenerUsuarioIdActual();
        if (!usuarioId.HasValue || usuarioId.Value <= 0) return Unauthorized();

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync();

        const string sql = @"
SELECT TOP(1)
    r.RequerimientoHCCID,r.PlantillaHCCID,r.ParteID,r.InspeccionID,r.EjecucionProduccionID,r.ProgramaProduccionID,
    r.SolicitudProduccionID,r.SolicitudProduccionDetalleID,r.TipoOrigen,r.TipoEventoSugerido,r.OrdenFabricacion,
    r.ClienteNombre,r.NumeroParte,r.DescripcionParte,r.MaquinaID,r.MaquinaTexto,r.Turno,r.OperadorTexto,
    r.FechaHoraRequerida,r.Estado,r.RegistroHCCID
FROM dbo.Calidad_HCC_Requerimientos r
WHERE r.RequerimientoHCCID=@ID AND r.Activo=1;";

        CalidadHCCCapturaViewModel? vm = null;
        await using (var cmd = new SqlCommand(sql, cn))
        {
            cmd.Parameters.Add("@ID", SqlDbType.BigInt).Value = id;
            await using var rd = await cmd.ExecuteReaderAsync();
            if (await rd.ReadAsync())
            {
                var estado = HccTexto(rd["Estado"]);
                var registro = rd["RegistroHCCID"] == DBNull.Value ? (long?)null : Convert.ToInt64(rd["RegistroHCCID"]);
                if (estado == HccCompletada && registro.HasValue)
                    return RedirectToAction(nameof(RegistroHCC), new { id = registro.Value });

                vm = new CalidadHCCCapturaViewModel
                {
                    RequerimientoHCCID = Convert.ToInt64(rd["RequerimientoHCCID"]),
                    PlantillaHCCID = Convert.ToInt32(rd["PlantillaHCCID"]),
                    ParteID = Convert.ToInt32(rd["ParteID"]),
                    InspeccionID = HccNullableInt(rd["InspeccionID"]),
                    EjecucionProduccionID = HccNullableInt(rd["EjecucionProduccionID"]),
                    ProgramaProduccionID = HccNullableInt(rd["ProgramaProduccionID"]),
                    SolicitudProduccionID = HccNullableInt(rd["SolicitudProduccionID"]),
                    SolicitudProduccionDetalleID = HccNullableInt(rd["SolicitudProduccionDetalleID"]),
                    TipoOrigen = HccTexto(rd["TipoOrigen"]),
                    TipoEvento = HccTexto(rd["TipoEventoSugerido"]),
                    OrdenFabricacion = HccTexto(rd["OrdenFabricacion"]),
                    ClienteNombre = HccTexto(rd["ClienteNombre"]),
                    NumeroParte = HccTexto(rd["NumeroParte"]),
                    DescripcionParte = HccTexto(rd["DescripcionParte"]),
                    MaquinaID = HccNullableInt(rd["MaquinaID"]),
                    MaquinaTexto = HccTexto(rd["MaquinaTexto"]),
                    Turno = HccTexto(rd["Turno"]),
                    OperadorTexto = HccTexto(rd["OperadorTexto"]),
                    FechaHoraRequerida = Convert.ToDateTime(rd["FechaHoraRequerida"])
                };
            }
        }
        if (vm == null) return NotFound();

        vm.Plantilla = await CargarPlantillaHccAsync(vm.PlantillaHCCID, vm.ParteID, cn) ?? new CalidadHCCPlantillaViewModel();
        vm.AuditorTexto = await ObtenerNombreUsuarioHccAsync(usuarioId.Value, cn);

        var cavidades = vm.Plantilla.CavidadesDisponibles.Count > 0
            ? vm.Plantilla.CavidadesDisponibles
            : Enumerable.Range(1, Math.Max(1, vm.Plantilla.CavidadesDeclaradas ?? 1)).ToList();
        vm.CavidadesSeleccionadas.AddRange(cavidades);

        return View("CapturarHCC", vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GuardarHCC(CalidadHCCCapturaPostViewModel model)
    {
        var usuarioId = ObtenerUsuarioIdActual();
        if (!usuarioId.HasValue || usuarioId.Value <= 0) return Unauthorized();

        model.TipoEvento = (model.TipoEvento ?? string.Empty).Trim().ToUpperInvariant();
        if (model.TipoEvento is not ("L" or "M" or "RL"))
            ModelState.AddModelError(nameof(model.TipoEvento), "El tipo de evento debe ser L, M o RL.");

        var cavidades = CalidadHCCParsing.ParsearCavidades(model.CavidadesConfiguradas);
        if (cavidades.Count == 0) ModelState.AddModelError(nameof(model.CavidadesConfiguradas), "Selecciona al menos una cavidad/posición configurada en la máquina.");
        if (cavidades.Count > 64) ModelState.AddModelError(nameof(model.CavidadesConfiguradas), "No se permiten más de 64 cavidades/posiciones en una captura.");

        if (!ModelState.IsValid)
        {
            TempData["Error"] = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(CapturarHCC), new { id = model.RequerimientoHCCID });
        }

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);

        try
        {
            const string sqlReq = @"
SELECT TOP(1)
    RequerimientoHCCID,PlantillaHCCID,ParteID,InspeccionID,EjecucionProduccionID,ProgramaProduccionID,
    SolicitudProduccionID,SolicitudProduccionDetalleID,OrdenFabricacion,MaquinaID,MaquinaTexto,Turno,OperadorTexto,
    FechaHoraRequerida,Estado,RegistroHCCID,TipoOrigen
FROM dbo.Calidad_HCC_Requerimientos WITH(UPDLOCK,HOLDLOCK)
WHERE RequerimientoHCCID=@ID AND Activo=1;";

            int plantillaId, parteId;
            int? inspeccionId, ejecucionId, programaId, solicitudId, solicitudDetalleId, maquinaId;
            string of, maquina, turno, operador, estado, tipoOrigen;
            DateTime fechaEvento;
            long? registroExistente;

            await using (var cmd = new SqlCommand(sqlReq, cn, tx))
            {
                cmd.Parameters.Add("@ID", SqlDbType.BigInt).Value = model.RequerimientoHCCID;
                await using var rd = await cmd.ExecuteReaderAsync();
                if (!await rd.ReadAsync()) return NotFound();
                plantillaId = Convert.ToInt32(rd["PlantillaHCCID"]);
                parteId = Convert.ToInt32(rd["ParteID"]);
                inspeccionId = HccNullableInt(rd["InspeccionID"]);
                ejecucionId = HccNullableInt(rd["EjecucionProduccionID"]);
                programaId = HccNullableInt(rd["ProgramaProduccionID"]);
                solicitudId = HccNullableInt(rd["SolicitudProduccionID"]);
                solicitudDetalleId = HccNullableInt(rd["SolicitudProduccionDetalleID"]);
                maquinaId = HccNullableInt(rd["MaquinaID"]);
                of = HccTexto(rd["OrdenFabricacion"]);
                maquina = HccTexto(rd["MaquinaTexto"]);
                turno = HccTexto(rd["Turno"]);
                operador = HccTexto(rd["OperadorTexto"]);
                fechaEvento = Convert.ToDateTime(rd["FechaHoraRequerida"]);
                estado = HccTexto(rd["Estado"]);
                registroExistente = rd["RegistroHCCID"] == DBNull.Value ? null : Convert.ToInt64(rd["RegistroHCCID"]);
                tipoOrigen = HccTexto(rd["TipoOrigen"]);
            }

            if (estado == HccCompletada || registroExistente.HasValue)
            {
                await tx.RollbackAsync();
                TempData["Info"] = "Esta HCC ya fue capturada.";
                return registroExistente.HasValue
                    ? RedirectToAction(nameof(RegistroHCC), new { id = registroExistente.Value })
                    : RedirectToAction(nameof(HojasControl));
            }

            var plantilla = await CargarPlantillaHccAsync(plantillaId, parteId, cn, tx)
                ?? throw new InvalidOperationException("La plantilla HCC ya no está disponible.");

            var idsCaracteristica = plantilla.Caracteristicas.Select(x => x.CaracteristicaHCCID).ToHashSet();
            var idsChecklist = plantilla.Checklist.Select(x => x.ChecklistHCCID).ToHashSet();

            var medicionesPost = model.Mediciones
                .Where(x => idsCaracteristica.Contains(x.CaracteristicaHCCID) && cavidades.Contains(x.NumeroCavidad) && x.NumeroTiro is >= 1 and <= 3)
                .GroupBy(x => new { x.CaracteristicaHCCID, x.NumeroCavidad, x.NumeroTiro })
                .ToDictionary(g => (g.Key.CaracteristicaHCCID, g.Key.NumeroCavidad, g.Key.NumeroTiro), g => g.First());

            var resultados = new List<(int carId, int cav, int tiro, decimal? numero, string? texto, string resultado, string? obs)>();
            var hayNok = false;

            foreach (var car in plantilla.Caracteristicas)
            {
                // NSQ_HCC_CAVIDADES_DINAMICAS_V3
                // Las cavidades originales conservan su aplicabilidad. Una
                // cavidad agregada manualmente es una posicion real adicional
                // y aplica a todas las caracteristicas de la HCC.
                var cavidadesPlantilla = plantilla.CavidadesDisponibles.ToHashSet();
                var cavidadesAdicionales = cavidades
                    .Where(c => !cavidadesPlantilla.Contains(c))
                    .ToHashSet();

                var aplicables = car.Cavidades.Count > 0
                    ? cavidades
                        .Where(c => car.Cavidades.Contains(c) || cavidadesAdicionales.Contains(c))
                        .ToList()
                    : cavidades;
                if (aplicables.Count == 0) continue;

                foreach (var cav in aplicables)
                foreach (var tiro in Enumerable.Range(1, 3))
                {
                    if (!medicionesPost.TryGetValue((car.CaracteristicaHCCID, cav, tiro), out var post))
                        throw new InvalidOperationException($"Falta capturar {car.Nombre}, cavidad/posición {cav}, tiro {tiro}.");

                    decimal? numero = null;
                    string? texto = string.IsNullOrWhiteSpace(post.Valor) ? null : post.Valor.Trim();
                    string resultado;

                    if (car.EsNumerica)
                    {
                        if (!CalidadHCCParsing.TryDecimalFlexible(post.Valor, out var valor))
                            throw new InvalidOperationException($"Captura un valor numérico válido en {car.Nombre}, cavidad/posición {cav}, tiro {tiro}.");
                        numero = valor;
                        if (car.TieneLimites)
                        {
                            var okInf = !car.LimiteInferior.HasValue || valor >= car.LimiteInferior.Value;
                            var okSup = !car.LimiteSuperior.HasValue || valor <= car.LimiteSuperior.Value;
                            resultado = okInf && okSup ? "OK" : "NOK";
                        }
                        else
                        {
                            resultado = HccNormalizarResultado(post.Resultado, false);
                        }
                    }
                    else
                    {
                        resultado = HccNormalizarResultado(post.Resultado, true);
                        if (string.IsNullOrWhiteSpace(texto)) texto = resultado;
                    }

                    if (resultado == "NOK") hayNok = true;
                    resultados.Add((car.CaracteristicaHCCID, cav, tiro, numero, texto, resultado, post.Observaciones?.Trim()));
                }
            }

            var checklistPost = model.Checklist
                .Where(x => idsChecklist.Contains(x.ChecklistHCCID))
                .GroupBy(x => x.ChecklistHCCID)
                .ToDictionary(g => g.Key, g => g.First());

            var checklistResultados = new List<(int id, string resultado, string? obs)>();
            foreach (var item in plantilla.Checklist)
            {
                if (!checklistPost.TryGetValue(item.ChecklistHCCID, out var post))
                    throw new InvalidOperationException($"Falta responder el checklist: {item.Descripcion}.");
                var resultado = HccNormalizarResultado(post.Resultado, item.PermiteNA);
                if (resultado == "NOK") hayNok = true;
                checklistResultados.Add((item.ChecklistHCCID, resultado, post.Observaciones?.Trim()));
            }

            if (hayNok && string.IsNullOrWhiteSpace(model.Observaciones))
                throw new InvalidOperationException("Existe al menos un resultado NOK. Describe el defecto y la acción/solución en Observaciones.");

            var auditor = await ObtenerNombreUsuarioHccAsync(usuarioId.Value, cn, tx);
            var resultadoGeneral = hayNok ? "NOK" : "OK";

            const string sqlRegistro = @"
INSERT dbo.Calidad_HCC_Registros
(
    PlantillaHCCID,ParteID,InspeccionID,ProgramaProduccionID,EjecucionProduccionID,SolicitudProduccionID,SolicitudProduccionDetalleID,
    OrdenFabricacion,Fecha,Turno,Hora,MaquinaID,MaquinaTexto,TipoEvento,OperadorTexto,AuditorUsuarioID,AuditorTexto,Observaciones,
    Estado,VersionFormatoSnapshot,UsuarioCreacionID,Activo,RequerimientoHCCID,CantidadCavidadesConfiguradas,CavidadesConfiguradas,ResultadoGeneral
)
OUTPUT INSERTED.RegistroHCCID
VALUES
(
    @Plantilla,@Parte,@Inspeccion,@Programa,@Ejecucion,@Solicitud,@SolicitudDetalle,
    @OF,@Fecha,@Turno,@Hora,@MaquinaID,@Maquina,@Evento,@Operador,@AuditorID,@Auditor,@Observaciones,
    N'CAPTURADO',@Version,@Usuario,1,@Requerimiento,@CantidadCavidades,@Cavidades,@Resultado
);";

            long registroId;
            await using (var cmd = new SqlCommand(sqlRegistro, cn, tx))
            {
                cmd.Parameters.Add("@Plantilla", SqlDbType.Int).Value = plantillaId;
                cmd.Parameters.Add("@Parte", SqlDbType.Int).Value = parteId;
                AddHccNullableInt(cmd, "@Inspeccion", inspeccionId);
                AddHccNullableInt(cmd, "@Programa", programaId);
                AddHccNullableInt(cmd, "@Ejecucion", ejecucionId);
                AddHccNullableInt(cmd, "@Solicitud", solicitudId);
                AddHccNullableInt(cmd, "@SolicitudDetalle", solicitudDetalleId);
                HccAddNullableText(cmd, "@OF", 150, of);
                cmd.Parameters.Add("@Fecha", SqlDbType.Date).Value = fechaEvento.Date;
                HccAddNullableText(cmd, "@Turno", 50, turno);
                cmd.Parameters.Add("@Hora", SqlDbType.Time).Value = fechaEvento.TimeOfDay;
                AddHccNullableInt(cmd, "@MaquinaID", maquinaId);
                HccAddNullableText(cmd, "@Maquina", 150, maquina);
                cmd.Parameters.Add("@Evento", SqlDbType.VarChar, 2).Value = model.TipoEvento;
                HccAddNullableText(cmd, "@Operador", 250, operador);
                cmd.Parameters.Add("@AuditorID", SqlDbType.Int).Value = usuarioId.Value;
                HccAddNullableText(cmd, "@Auditor", 250, auditor);
                cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, -1).Value = string.IsNullOrWhiteSpace(model.Observaciones) ? DBNull.Value : model.Observaciones.Trim();
                HccAddNullableText(cmd, "@Version", 80, plantilla.VersionFormato);
                cmd.Parameters.Add("@Usuario", SqlDbType.Int).Value = usuarioId.Value;
                cmd.Parameters.Add("@Requerimiento", SqlDbType.BigInt).Value = model.RequerimientoHCCID;
                cmd.Parameters.Add("@CantidadCavidades", SqlDbType.Int).Value = cavidades.Count;
                cmd.Parameters.Add("@Cavidades", SqlDbType.NVarChar, 500).Value = string.Join(",", cavidades);
                cmd.Parameters.Add("@Resultado", SqlDbType.VarChar, 10).Value = resultadoGeneral;
                registroId = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            }

            const string sqlMed = @"
INSERT dbo.Calidad_HCC_Mediciones
(RegistroHCCID,CaracteristicaHCCID,NumeroTiro,NumeroCavidad,ValorNumerico,ValorTexto,Resultado,Observaciones,UsuarioCreacionID,Activo)
VALUES(@Registro,@Caracteristica,@Tiro,@Cavidad,@Numero,@Texto,@Resultado,@Observaciones,@Usuario,1);";
            foreach (var x in resultados)
            {
                await using var cmd = new SqlCommand(sqlMed, cn, tx);
                cmd.Parameters.Add("@Registro", SqlDbType.BigInt).Value = registroId;
                cmd.Parameters.Add("@Caracteristica", SqlDbType.Int).Value = x.carId;
                cmd.Parameters.Add("@Tiro", SqlDbType.Int).Value = x.tiro;
                cmd.Parameters.Add("@Cavidad", SqlDbType.Int).Value = x.cav;
                var pn = cmd.Parameters.Add("@Numero", SqlDbType.Decimal); pn.Precision = 18; pn.Scale = 6; pn.Value = (object?)x.numero ?? DBNull.Value;
                HccAddNullableText(cmd, "@Texto", 250, x.texto);
                cmd.Parameters.Add("@Resultado", SqlDbType.VarChar, 10).Value = x.resultado;
                HccAddNullableText(cmd, "@Observaciones", 1000, x.obs);
                cmd.Parameters.Add("@Usuario", SqlDbType.Int).Value = usuarioId.Value;
                await cmd.ExecuteNonQueryAsync();
            }

            const string sqlCheck = @"
INSERT dbo.Calidad_HCC_ChecklistResultados
(RegistroHCCID,ChecklistHCCID,Resultado,Observaciones,UsuarioCreacionID,Activo)
VALUES(@Registro,@Checklist,@Resultado,@Observaciones,@Usuario,1);";
            foreach (var x in checklistResultados)
            {
                await using var cmd = new SqlCommand(sqlCheck, cn, tx);
                cmd.Parameters.Add("@Registro", SqlDbType.BigInt).Value = registroId;
                cmd.Parameters.Add("@Checklist", SqlDbType.Int).Value = x.id;
                cmd.Parameters.Add("@Resultado", SqlDbType.VarChar, 10).Value = x.resultado;
                HccAddNullableText(cmd, "@Observaciones", 1000, x.obs);
                cmd.Parameters.Add("@Usuario", SqlDbType.Int).Value = usuarioId.Value;
                await cmd.ExecuteNonQueryAsync();
            }

            const string sqlCerrar = @"
UPDATE dbo.Calidad_HCC_Requerimientos
SET Estado=N'COMPLETADA',RegistroHCCID=@Registro,UsuarioModificacionID=@Usuario,FechaModificacion=SYSDATETIME()
WHERE RequerimientoHCCID=@ID AND Activo=1 AND Estado IN(N'PENDIENTE',N'EN_CAPTURA') AND RegistroHCCID IS NULL;
IF @@ROWCOUNT<>1 THROW 55931,'El requerimiento HCC cambió o ya fue capturado.',1;

INSERT dbo.Calidad_HCC_Historial(RegistroHCCID,PlantillaHCCID,Movimiento,Comentario,UsuarioID)
VALUES(@Registro,@Plantilla,N'CAPTURA_OPERATIVA',@Comentario,@Usuario);";
            await using (var cmd = new SqlCommand(sqlCerrar, cn, tx))
            {
                cmd.Parameters.Add("@Registro", SqlDbType.BigInt).Value = registroId;
                cmd.Parameters.Add("@Usuario", SqlDbType.Int).Value = usuarioId.Value;
                cmd.Parameters.Add("@ID", SqlDbType.BigInt).Value = model.RequerimientoHCCID;
                cmd.Parameters.Add("@Plantilla", SqlDbType.Int).Value = plantillaId;
                cmd.Parameters.Add("@Comentario", SqlDbType.NVarChar, 2000).Value = $"HCC {tipoOrigen} capturada. Evento {model.TipoEvento}. Cavidades/posiciones: {string.Join(",", cavidades)}. Resultado: {resultadoGeneral}.";
                await cmd.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();
            TempData["Mensaje"] = $"Hoja de Control de Calidad guardada. Resultado general: {resultadoGeneral}.";
            return RedirectToAction(nameof(RegistroHCC), new { id = registroId });
        }
        catch (Exception ex)
        {
            try { await tx.RollbackAsync(); } catch { }
            TempData["Error"] = "No fue posible guardar la HCC: " + ex.Message;
            return RedirectToAction(nameof(CapturarHCC), new { id = model.RequerimientoHCCID });
        }
    }

    [HttpGet]
    public async Task<IActionResult> RegistroHCC(long id)
    {
        if (id <= 0) return NotFound();
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync();
        const string sql = @"
SELECT TOP(1) RegistroHCCID,RequerimientoHCCID,PlantillaHCCID,ParteID,OrdenFabricacion,Fecha,Turno,Hora,MaquinaTexto,TipoEvento,
       OperadorTexto,AuditorTexto,Observaciones,CavidadesConfiguradas,CantidadCavidadesConfiguradas,ResultadoGeneral
FROM dbo.Calidad_HCC_Registros WHERE RegistroHCCID=@ID AND Activo=1;";
        CalidadHCCRegistroDetalleViewModel? vm = null;
        int plantillaId=0, parteId=0;
        await using (var cmd = new SqlCommand(sql, cn))
        {
            cmd.Parameters.Add("@ID", SqlDbType.BigInt).Value = id;
            await using var rd = await cmd.ExecuteReaderAsync();
            if (await rd.ReadAsync())
            {
                plantillaId = Convert.ToInt32(rd["PlantillaHCCID"]); parteId = Convert.ToInt32(rd["ParteID"]);
                vm = new CalidadHCCRegistroDetalleViewModel
                {
                    RegistroHCCID = Convert.ToInt64(rd["RegistroHCCID"]),
                    RequerimientoHCCID = rd["RequerimientoHCCID"] == DBNull.Value ? null : Convert.ToInt64(rd["RequerimientoHCCID"]),
                    Fecha = Convert.ToDateTime(rd["Fecha"]),
                    Hora = rd["Hora"] == DBNull.Value ? null : (TimeSpan?)rd["Hora"],
                    Turno = HccTexto(rd["Turno"]), TipoEvento = HccTexto(rd["TipoEvento"]), OrdenFabricacion = HccTexto(rd["OrdenFabricacion"]),
                    MaquinaTexto = HccTexto(rd["MaquinaTexto"]), OperadorTexto = HccTexto(rd["OperadorTexto"]), AuditorTexto = HccTexto(rd["AuditorTexto"]),
                    CavidadesConfiguradas = HccTexto(rd["CavidadesConfiguradas"]),
                    CantidadCavidadesConfiguradas = rd["CantidadCavidadesConfiguradas"] == DBNull.Value ? 0 : Convert.ToInt32(rd["CantidadCavidadesConfiguradas"]),
                    ResultadoGeneral = HccTexto(rd["ResultadoGeneral"]), Observaciones = HccTexto(rd["Observaciones"])
                };
            }
        }
        if (vm == null) return NotFound();
        vm.Plantilla = await CargarPlantillaHccAsync(plantillaId, parteId, cn) ?? new CalidadHCCPlantillaViewModel();

        const string sqlMed = @"SELECT CaracteristicaHCCID,NumeroTiro,NumeroCavidad,ValorNumerico,ValorTexto,Resultado FROM dbo.Calidad_HCC_Mediciones WHERE RegistroHCCID=@ID AND Activo=1 ORDER BY CaracteristicaHCCID,NumeroCavidad,NumeroTiro;";
        await using (var cmd = new SqlCommand(sqlMed, cn))
        {
            cmd.Parameters.Add("@ID", SqlDbType.BigInt).Value=id;
            await using var rd=await cmd.ExecuteReaderAsync();
            while(await rd.ReadAsync()) vm.Mediciones.Add(new CalidadHCCMedicionGuardadaViewModel
            {
                CaracteristicaHCCID=Convert.ToInt32(rd["CaracteristicaHCCID"]), NumeroTiro=Convert.ToInt32(rd["NumeroTiro"]), NumeroCavidad=Convert.ToInt32(rd["NumeroCavidad"]),
                ValorNumerico=rd["ValorNumerico"]==DBNull.Value?null:Convert.ToDecimal(rd["ValorNumerico"]), ValorTexto=HccTexto(rd["ValorTexto"]), Resultado=HccTexto(rd["Resultado"])
            });
        }
        const string sqlChk = @"SELECT ChecklistHCCID,Resultado,Observaciones FROM dbo.Calidad_HCC_ChecklistResultados WHERE RegistroHCCID=@ID AND Activo=1 ORDER BY ChecklistHCCID;";
        await using (var cmd = new SqlCommand(sqlChk, cn))
        {
            cmd.Parameters.Add("@ID", SqlDbType.BigInt).Value=id;
            await using var rd=await cmd.ExecuteReaderAsync();
            while(await rd.ReadAsync()) vm.Checklist.Add(new CalidadHCCChecklistGuardadoViewModel { ChecklistHCCID=Convert.ToInt32(rd["ChecklistHCCID"]), Resultado=HccTexto(rd["Resultado"]), Observaciones=HccTexto(rd["Observaciones"]) });
        }
        return View("RegistroHCC", vm);
    }

    private async Task SincronizarRequerimientosHccAsync()
    {
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            const string sql = @"
-- ARRANQUE / CAMBIO DE PRODUCCION: una HCC por inspeccion/corrida.
INSERT dbo.Calidad_HCC_Requerimientos
(ClaveOrigen,TipoOrigen,TipoEventoSugerido,PlantillaHCCID,ParteID,InspeccionID,EjecucionProduccionID,ProgramaProduccionID,SolicitudProduccionID,SolicitudProduccionDetalleID,
 OrdenFabricacion,ClienteNombre,NumeroParte,DescripcionParte,MaquinaID,MaquinaTexto,Turno,OperadorTexto,FechaHoraRequerida,Estado,Activo)
SELECT
 CONCAT(N'ARRANQUE:I:',i.InspeccionID),N'ARRANQUE',N'L',tpl.PlantillaHCCID,i.ParteID,i.InspeccionID,i.EjecucionProduccionID,i.ProgramaProduccionID,i.SolicitudProduccionID,i.SolicitudProduccionDetalleID,
 i.OrdenTrabajo,i.ClienteNombre,i.NumeroParte,ISNULL(p.Descripcion,N''),i.MaquinaID,i.Maquina,NULL,i.OperadorPrincipalNombre,
 COALESCE(i.FechaAutorizacionPrearranque,i.FechaNotificacionCalidad,i.FechaCreacion,SYSDATETIME()),N'PENDIENTE',1
FROM dbo.Calidad_Inspecciones i
INNER JOIN dbo.ERP_Partes p ON p.ParteID=i.ParteID
CROSS APPLY
(
 SELECT TOP(1) h.PlantillaHCCID
 FROM dbo.Calidad_HCC_PlantillaPartes pp
 INNER JOIN dbo.Calidad_HCC_Plantillas h ON h.PlantillaHCCID=pp.PlantillaHCCID AND h.Activo=1
 WHERE pp.ParteID=i.ParteID AND pp.Activo=1
 ORDER BY h.EsVigente DESC,pp.EsPrincipal DESC,h.FechaModificacionFormato DESC,h.PlantillaHCCID DESC
) tpl
WHERE i.ParteID IS NOT NULL
  AND UPPER(LTRIM(RTRIM(ISNULL(i.Estado,N''))))<>N'CERRADA'
  AND NOT EXISTS(SELECT 1 FROM dbo.Calidad_HCC_Requerimientos r WHERE r.ClaveOrigen=CONCAT(N'ARRANQUE:I:',i.InspeccionID));

-- CAMBIO DE TURNO: una HCC por cambio real de operador/turno en Produccion.
INSERT dbo.Calidad_HCC_Requerimientos
(ClaveOrigen,TipoOrigen,TipoEventoSugerido,PlantillaHCCID,ParteID,InspeccionID,EjecucionProduccionID,ProgramaProduccionID,SolicitudProduccionID,SolicitudProduccionDetalleID,CambioTurnoID,
 OrdenFabricacion,ClienteNombre,NumeroParte,DescripcionParte,MaquinaID,MaquinaTexto,Turno,OperadorTexto,FechaHoraRequerida,Estado,Activo)
SELECT
 CONCAT(N'TURNO:C:',ct.CambioTurnoID),N'CAMBIO_TURNO',N'M',tpl.PlantillaHCCID,i.ParteID,i.InspeccionID,i.EjecucionProduccionID,i.ProgramaProduccionID,i.SolicitudProduccionID,i.SolicitudProduccionDetalleID,ct.CambioTurnoID,
 i.OrdenTrabajo,i.ClienteNombre,i.NumeroParte,ISNULL(p.Descripcion,N''),i.MaquinaID,i.Maquina,ct.TurnoEntranteNombre,
 LTRIM(RTRIM(CONCAT(ISNULL(pe.Nombre,N''),N' ',ISNULL(pe.ApellidoPaterno,N''),N' ',ISNULL(pe.ApellidoMaterno,N'')))),
 COALESCE(ct.FechaRecepcion,ct.FechaEntrega,SYSDATETIME()),N'PENDIENTE',1
FROM dbo.Produccion_CambiosTurno ct
CROSS APPLY
(
 SELECT TOP(1) x.* FROM dbo.Calidad_Inspecciones x
 WHERE x.EjecucionProduccionID=ct.EjecucionProduccionID
 ORDER BY CASE WHEN UPPER(LTRIM(RTRIM(ISNULL(x.Estado,N''))))<>N'CERRADA' THEN 0 ELSE 1 END,x.InspeccionID DESC
) i
INNER JOIN dbo.ERP_Partes p ON p.ParteID=i.ParteID
LEFT JOIN dbo.Persona pe ON pe.PersonaID=ct.OperadorEntranteID
CROSS APPLY
(
 SELECT TOP(1) h.PlantillaHCCID
 FROM dbo.Calidad_HCC_PlantillaPartes pp
 INNER JOIN dbo.Calidad_HCC_Plantillas h ON h.PlantillaHCCID=pp.PlantillaHCCID AND h.Activo=1
 WHERE pp.ParteID=i.ParteID AND pp.Activo=1
 ORDER BY h.EsVigente DESC,pp.EsPrincipal DESC,h.FechaModificacionFormato DESC,h.PlantillaHCCID DESC
) tpl
WHERE ct.Activo=1 AND i.ParteID IS NOT NULL
  AND NOT EXISTS(SELECT 1 FROM dbo.Calidad_HCC_Requerimientos r WHERE r.ClaveOrigen=CONCAT(N'TURNO:C:',ct.CambioTurnoID));

-- RELIBERACION: se genera cuando Calidad invalida configuracion o solicita reliberacion.
INSERT dbo.Calidad_HCC_Requerimientos
(ClaveOrigen,TipoOrigen,TipoEventoSugerido,PlantillaHCCID,ParteID,InspeccionID,EjecucionProduccionID,ProgramaProduccionID,SolicitudProduccionID,SolicitudProduccionDetalleID,
 OrdenFabricacion,ClienteNombre,NumeroParte,DescripcionParte,MaquinaID,MaquinaTexto,Turno,OperadorTexto,FechaHoraRequerida,Estado,Activo)
SELECT
 CONCAT(N'RELIB:I:',i.InspeccionID,N':',CONVERT(varchar(19),COALESCE(i.FechaInvalidacion,i.FechaModificacion,i.FechaCreacion),126)),N'RELIBERACION',N'RL',tpl.PlantillaHCCID,i.ParteID,i.InspeccionID,i.EjecucionProduccionID,i.ProgramaProduccionID,i.SolicitudProduccionID,i.SolicitudProduccionDetalleID,
 i.OrdenTrabajo,i.ClienteNombre,i.NumeroParte,ISNULL(p.Descripcion,N''),i.MaquinaID,i.Maquina,NULL,i.OperadorPrincipalNombre,
 COALESCE(i.FechaInvalidacion,i.FechaModificacion,i.FechaCreacion,SYSDATETIME()),N'PENDIENTE',1
FROM dbo.Calidad_Inspecciones i
INNER JOIN dbo.ERP_Partes p ON p.ParteID=i.ParteID
CROSS APPLY
(
 SELECT TOP(1) h.PlantillaHCCID
 FROM dbo.Calidad_HCC_PlantillaPartes pp
 INNER JOIN dbo.Calidad_HCC_Plantillas h ON h.PlantillaHCCID=pp.PlantillaHCCID AND h.Activo=1
 WHERE pp.ParteID=i.ParteID AND pp.Activo=1
 ORDER BY h.EsVigente DESC,pp.EsPrincipal DESC,h.FechaModificacionFormato DESC,h.PlantillaHCCID DESC
) tpl
WHERE i.ParteID IS NOT NULL
  AND UPPER(LTRIM(RTRIM(ISNULL(i.Estado,N''))))<>N'CERRADA'
  AND (ISNULL(i.RequiereReliberacion,0)=1 OR ISNULL(i.ConfiguracionInvalidada,0)=1)
  AND NOT EXISTS
  (
    SELECT 1 FROM dbo.Calidad_HCC_Requerimientos r
    WHERE r.ClaveOrigen=CONCAT(N'RELIB:I:',i.InspeccionID,N':',CONVERT(varchar(19),COALESCE(i.FechaInvalidacion,i.FechaModificacion,i.FechaCreacion),126))
  );";
            await using var cmd = new SqlCommand(sql, cn, tx);
            await cmd.ExecuteNonQueryAsync();
            await tx.CommitAsync();
        }
        catch
        {
            try { await tx.RollbackAsync(); } catch { }
            throw;
        }
    }

    private async Task<CalidadHCCPlantillaViewModel?> CargarPlantillaHccAsync(int plantillaId, int? parteId, SqlConnection? conexion = null, SqlTransaction? tx = null)
    {
        var propia = conexion == null;
        await using var cnLocal = propia ? new SqlConnection(ConnectionString) : null;
        var cn = conexion ?? cnLocal!;
        if (propia) await cn.OpenAsync();

        const string sql = @"
SELECT TOP(1)
 h.PlantillaHCCID,COALESCE(@ParteID,pp.ParteID,h.ParteID) ParteID,h.CodigoFormato,h.NumeroHCC,h.VersionFormato,h.FechaModificacionFormato,
 COALESCE(NULLIF(cli.Nombre,N''),NULLIF(h.ClienteFuente,N''),N'') Cliente,
 ISNULL(p.NumeroParte,h.NumeroParteFuente) NumeroParte,ISNULL(p.ReferenciaSAP,N'') ReferenciaSAP,
 COALESCE(NULLIF(p.Designacion,N''),NULLIF(h.DesignacionFuente,N''),N'') Designacion,
 COALESCE(NULLIF(p.Descripcion,N''),NULLIF(h.DescripcionFuente,N''),N'') Descripcion,
 h.NumeroDibujo,h.Proceso,h.ReferenciaPlanControl,h.CodigoResina,h.MateriaPrima,h.TiempoSecadoTexto,h.TipoSecado,h.HorasSecado,h.TemperaturaSecado,h.UnidadTemperatura,
 h.NumeroTirosDefault,h.CavidadesDeclaradas,h.ArchivoOrigen,h.HojaOrigen,h.EsVigente
FROM dbo.Calidad_HCC_Plantillas h
OUTER APPLY(SELECT TOP(1) x.ParteID FROM dbo.Calidad_HCC_PlantillaPartes x WHERE x.PlantillaHCCID=h.PlantillaHCCID AND x.Activo=1 ORDER BY CASE WHEN @ParteID IS NOT NULL AND x.ParteID=@ParteID THEN 0 WHEN x.EsPrincipal=1 THEN 1 ELSE 2 END,x.PlantillaParteHCCID) pp
LEFT JOIN dbo.ERP_Partes p ON p.ParteID=COALESCE(@ParteID,pp.ParteID,h.ParteID)
LEFT JOIN dbo.ERP_Clientes cli ON cli.ClienteID=p.ClienteID
WHERE h.PlantillaHCCID=@Plantilla AND h.Activo=1
  AND (@ParteID IS NULL OR EXISTS(SELECT 1 FROM dbo.Calidad_HCC_PlantillaPartes z WHERE z.PlantillaHCCID=h.PlantillaHCCID AND z.ParteID=@ParteID AND z.Activo=1));";
        CalidadHCCPlantillaViewModel? vm = null;
        await using (var cmd = tx == null ? new SqlCommand(sql, cn) : new SqlCommand(sql, cn, tx))
        {
            cmd.Parameters.Add("@Plantilla", SqlDbType.Int).Value=plantillaId;
            cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value=(object?)parteId ?? DBNull.Value;
            await using var rd=await cmd.ExecuteReaderAsync();
            if(await rd.ReadAsync()) vm=new CalidadHCCPlantillaViewModel
            {
                PlantillaHCCID=Convert.ToInt32(rd["PlantillaHCCID"]),ParteID=Convert.ToInt32(rd["ParteID"]),CodigoFormato=HccTexto(rd["CodigoFormato"]),NumeroHCC=HccTexto(rd["NumeroHCC"]),VersionFormato=HccTexto(rd["VersionFormato"]),
                FechaModificacionFormato=rd["FechaModificacionFormato"]==DBNull.Value?null:Convert.ToDateTime(rd["FechaModificacionFormato"]),Cliente=HccTexto(rd["Cliente"]),NumeroParte=HccTexto(rd["NumeroParte"]),ReferenciaSAP=HccTexto(rd["ReferenciaSAP"]),Designacion=HccTexto(rd["Designacion"]),Descripcion=HccTexto(rd["Descripcion"]),
                NumeroDibujo=HccTexto(rd["NumeroDibujo"]),Proceso=HccTexto(rd["Proceso"]),ReferenciaPlanControl=HccTexto(rd["ReferenciaPlanControl"]),CodigoResina=HccTexto(rd["CodigoResina"]),MateriaPrima=HccTexto(rd["MateriaPrima"]),TiempoSecadoTexto=HccTexto(rd["TiempoSecadoTexto"]),TipoSecado=HccTexto(rd["TipoSecado"]),
                HorasSecado=rd["HorasSecado"]==DBNull.Value?null:Convert.ToDecimal(rd["HorasSecado"]),TemperaturaSecado=rd["TemperaturaSecado"]==DBNull.Value?null:Convert.ToDecimal(rd["TemperaturaSecado"]),UnidadTemperatura=HccTexto(rd["UnidadTemperatura"]),NumeroTirosDefault=Convert.ToInt32(rd["NumeroTirosDefault"]),CavidadesDeclaradas=HccNullableInt(rd["CavidadesDeclaradas"]),ArchivoOrigen=HccTexto(rd["ArchivoOrigen"]),HojaOrigen=HccTexto(rd["HojaOrigen"]),EsVigente=Convert.ToBoolean(rd["EsVigente"])
            };
        }
        if(vm==null) return null;

        const string sqlCar=@"SELECT CaracteristicaHCCID,Orden,TipoCaracteristica,Nombre,EspecificacionTexto,ValorNominal,ToleranciaMas,ToleranciaMenos,LimiteInferior,LimiteSuperior,Unidad,Instrumento,CodigoGauge FROM dbo.Calidad_HCC_Caracteristicas WHERE PlantillaHCCID=@P AND Activo=1 ORDER BY Orden,CaracteristicaHCCID;";
        await using (var cmd=tx==null?new SqlCommand(sqlCar,cn):new SqlCommand(sqlCar,cn,tx))
        {
            cmd.Parameters.Add("@P",SqlDbType.Int).Value=plantillaId; await using var rd=await cmd.ExecuteReaderAsync();
            while(await rd.ReadAsync()) vm.Caracteristicas.Add(new CalidadHCCCaracteristicaViewModel
            {
                CaracteristicaHCCID=Convert.ToInt32(rd["CaracteristicaHCCID"]),Orden=Convert.ToInt32(rd["Orden"]),TipoCaracteristica=HccTexto(rd["TipoCaracteristica"]),Nombre=HccTexto(rd["Nombre"]),EspecificacionTexto=HccTexto(rd["EspecificacionTexto"]),
                ValorNominal=HccNullableDecimal(rd["ValorNominal"]),ToleranciaMas=HccNullableDecimal(rd["ToleranciaMas"]),ToleranciaMenos=HccNullableDecimal(rd["ToleranciaMenos"]),LimiteInferior=HccNullableDecimal(rd["LimiteInferior"]),LimiteSuperior=HccNullableDecimal(rd["LimiteSuperior"]),Unidad=HccTexto(rd["Unidad"]),Instrumento=HccTexto(rd["Instrumento"]),CodigoGauge=HccTexto(rd["CodigoGauge"])
            });
        }
        const string sqlCav=@"SELECT cc.CaracteristicaHCCID,cc.NumeroCavidad FROM dbo.Calidad_HCC_CaracteristicaCavidades cc INNER JOIN dbo.Calidad_HCC_Caracteristicas c ON c.CaracteristicaHCCID=cc.CaracteristicaHCCID WHERE c.PlantillaHCCID=@P AND c.Activo=1 AND cc.Activo=1 ORDER BY cc.NumeroCavidad;";
        await using (var cmd=tx==null?new SqlCommand(sqlCav,cn):new SqlCommand(sqlCav,cn,tx))
        {
            cmd.Parameters.Add("@P",SqlDbType.Int).Value=plantillaId; await using var rd=await cmd.ExecuteReaderAsync();
            while(await rd.ReadAsync())
            {
                var car=vm.Caracteristicas.FirstOrDefault(x=>x.CaracteristicaHCCID==Convert.ToInt32(rd["CaracteristicaHCCID"]));
                if(car!=null) car.Cavidades.Add(Convert.ToInt32(rd["NumeroCavidad"]));
            }
        }
        foreach(var cav in vm.Caracteristicas.SelectMany(x=>x.Cavidades).Distinct().OrderBy(x=>x)) vm.CavidadesDisponibles.Add(cav);

        const string sqlCheck=@"SELECT ChecklistHCCID,Orden,Descripcion,PermiteNA FROM dbo.Calidad_HCC_Checklist WHERE PlantillaHCCID=@P AND Activo=1 ORDER BY Orden,ChecklistHCCID;";
        await using (var cmd=tx==null?new SqlCommand(sqlCheck,cn):new SqlCommand(sqlCheck,cn,tx))
        {
            cmd.Parameters.Add("@P",SqlDbType.Int).Value=plantillaId; await using var rd=await cmd.ExecuteReaderAsync();
            while(await rd.ReadAsync()) vm.Checklist.Add(new CalidadHCCChecklistItemViewModel { ChecklistHCCID=Convert.ToInt32(rd["ChecklistHCCID"]),Orden=Convert.ToInt32(rd["Orden"]),Descripcion=HccTexto(rd["Descripcion"]),PermiteNA=Convert.ToBoolean(rd["PermiteNA"]) });
        }
        return vm;
    }

    private async Task<string> ObtenerNombreUsuarioHccAsync(int usuarioId, SqlConnection cn, SqlTransaction? tx = null)
    {
        const string sql=@"SELECT TOP(1) LTRIM(RTRIM(CONCAT(ISNULL(p.Nombre,N''),N' ',ISNULL(p.ApellidoPaterno,N''),N' ',ISNULL(p.ApellidoMaterno,N'')))) FROM dbo.Usuarios u LEFT JOIN dbo.Persona p ON p.PersonaID=u.PersonaID WHERE u.UsuarioID=@U;";
        await using var cmd=tx==null?new SqlCommand(sql,cn):new SqlCommand(sql,cn,tx); cmd.Parameters.Add("@U",SqlDbType.Int).Value=usuarioId;
        var value=await cmd.ExecuteScalarAsync(); var nombre=value==null||value==DBNull.Value?string.Empty:value.ToString()?.Trim()??string.Empty;
        return string.IsNullOrWhiteSpace(nombre)?$"Usuario #{usuarioId}":nombre;
    }

    private static string HccNormalizarResultado(string? valor, bool permiteNa)
    {
        var r=(valor??string.Empty).Trim().ToUpperInvariant();
        if(r=="NA" && permiteNa) return r;
        if(r is "OK" or "NOK") return r;
        throw new InvalidOperationException("Resultado inválido. Usa OK, NOK"+(permiteNa?" o NA.":"."));
    }

    private static int? HccNullableInt(object value)=>value==DBNull.Value?null:Convert.ToInt32(value);
    private static decimal? HccNullableDecimal(object value)=>value==DBNull.Value?null:Convert.ToDecimal(value);
    private static string HccTexto(object value)=>value==DBNull.Value?string.Empty:value?.ToString()?.Trim()??string.Empty;
    private static void AddHccNullableInt(SqlCommand cmd,string nombre,int? valor)=>cmd.Parameters.Add(nombre,SqlDbType.Int).Value=(object?)valor??DBNull.Value;
    private static void HccAddNullableText(SqlCommand cmd,string nombre,int tamano,string? valor)=>cmd.Parameters.Add(nombre,SqlDbType.NVarChar,tamano).Value=string.IsNullOrWhiteSpace(valor)?DBNull.Value:valor.Trim();
}
