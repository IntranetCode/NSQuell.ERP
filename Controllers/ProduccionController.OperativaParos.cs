using ERP.NSQuell.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace ERP.NSQuell.Controllers
{
    public sealed partial class ProduccionController
    {
        [HttpGet]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> OperativaParos(int programaProduccionId, bool soloLectura = false)
        {
            if (!UsuarioEnSesion())
                return StatusCode(401, new { ok = false, mensaje = "La sesión expiró. Vuelve a iniciar sesión." });

            if (programaProduccionId <= 0)
                return BadRequest(new { ok = false, mensaje = "El programa de Producción no es válido." });

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            var actual = await ObtenerContextoParosProgramaAsync(programaProduccionId, cn);
            if (actual == null)
                return NotFound(new { ok = false, mensaje = "No se encontró el programa de Producción solicitado." });

            var usuarioId = ObtenerUsuarioID();
            var permisos = await ObtenerPermisosProduccionUsuarioAsync(usuarioId, cn);
            var parejaRelacion = await ObtenerParejaLhRhProduccionAsync(programaProduccionId, cn);
            ContextoParosPrograma? pareja = null;

            if (parejaRelacion != null)
                pareja = await ObtenerContextoParosProgramaAsync(parejaRelacion.ProgramaParejaID, cn);

            var lados = ResolverLadosParosOperativos(actual, pareja);
            var vm = new ProduccionParosOperativosVm
            {
                FechaConsulta = DateTime.Now,
                ProgramaProduccionID = actual.ProgramaProduccionID,
                Actual = MapearLadoParosOperativos(actual, lados.LadoActual),
                Pareja = pareja == null ? null : MapearLadoParosOperativos(pareja, lados.LadoPareja),
                GrupoLhRh = parejaRelacion?.GrupoLhRh,
                ParejaConsistente = parejaRelacion == null ||
                    (parejaRelacion.EsCompatibleFisicamente &&
                     parejaRelacion.EjecucionParejaID.HasValue &&
                     parejaRelacion.EjecucionParejaID.Value > 0 &&
                     pareja != null &&
                     pareja.EjecucionProduccionID == parejaRelacion.EjecucionParejaID.Value),
                MotivoInconsistenciaPareja = ResolverInconsistenciaParejaParos(parejaRelacion, pareja),
                PuedeGestionar = !soloLectura && (permisos.PuedeVerTodo || permisos.EsOperadorProduccion),
                UsuarioNombre = permisos.Nombre,
                UsuarioPuesto = permisos.Puesto,
                Motivos = await CargarMotivosParoOperativosAsync(cn)
            };

            var referencias = new Dictionary<int, (string NumeroOF, string? Lado)>
            {
                [vm.Actual.ProgramaProduccionID] = (vm.Actual.NumeroOF, vm.Actual.LadoLhRh)
            };

            var idsEjecucion = new List<int>();
            if (vm.Actual.EjecucionProduccionID > 0)
                idsEjecucion.Add(vm.Actual.EjecucionProduccionID);

            if (vm.Pareja != null)
            {
                referencias[vm.Pareja.ProgramaProduccionID] = (vm.Pareja.NumeroOF, vm.Pareja.LadoLhRh);
                if (vm.Pareja.EjecucionProduccionID > 0)
                    idsEjecucion.Add(vm.Pareja.EjecucionProduccionID);
            }

            var paros = await CargarParosOperativosAsync(idsEjecucion, referencias, cn);
            vm.Paros = ConsolidarParosFisicos(paros, vm.Actual.ProgramaProduccionID);

            return PartialView("~/Views/ProduccionOperativa/Acciones/_ParosContenido.cshtml", vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> IniciarParoOperativa(ProduccionParoOperativoIniciarPostVm vm)
        {
            if (!UsuarioEnSesion())
                return StatusCode(401, new { ok = false, mensaje = "La sesión expiró. Vuelve a iniciar sesión." });

            if (vm == null || vm.ProgramaProduccionID <= 0 || vm.EjecucionProduccionID <= 0)
                return BadRequest(new { ok = false, mensaje = "La ejecución de Producción no es válida." });

            vm.Descripcion = string.IsNullOrWhiteSpace(vm.Descripcion) ? null : vm.Descripcion.Trim();
            vm.MotivoParoTexto = string.IsNullOrWhiteSpace(vm.MotivoParoTexto) ? null : vm.MotivoParoTexto.Trim();

            if (vm.Descripcion?.Length > 500)
                return BadRequest(new { ok = false, mensaje = "La descripción del paro no puede exceder 500 caracteres." });

            await using (var cn = new SqlConnection(ConnectionString))
            {
                await cn.OpenAsync();
                var permisos = await ObtenerPermisosProduccionUsuarioAsync(ObtenerUsuarioID(), cn);
                if (!(permisos.PuedeVerTodo || permisos.EsOperadorProduccion))
                    return StatusCode(403, new { ok = false, mensaje = "Tu perfil puede consultar el paro, pero no registrarlo." });

                if (!await EjecucionPerteneceProgramaOperativaAsync(vm.EjecucionProduccionID, vm.ProgramaProduccionID, cn))
                    return BadRequest(new { ok = false, mensaje = "La ejecución ya no corresponde al programa mostrado. Actualiza el Centro Operativo." });
            }

            TempData.Remove("Error");
            TempData.Remove("Success");

            var resultado = await IniciarParo(new ProduccionParoPostVm
            {
                EjecucionProduccionID = vm.EjecucionProduccionID,
                MotivoParoID = vm.MotivoParoID,
                MotivoParoTexto = vm.MotivoParoTexto,
                Descripcion = vm.Descripcion
            });

            var error = TempData["Error"]?.ToString();
            var exito = TempData["Success"]?.ToString();

            if (!string.IsNullOrWhiteSpace(error))
                return BadRequest(new { ok = false, mensaje = error });

            if (resultado is NotFoundResult)
                return NotFound(new { ok = false, mensaje = "La ejecución de Producción ya no existe." });

            return Json(new
            {
                ok = true,
                programaProduccionId = vm.ProgramaProduccionID,
                ejecucionProduccionId = vm.EjecucionProduccionID,
                mensaje = string.IsNullOrWhiteSpace(exito) ? "Paro iniciado correctamente." : exito
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CerrarParoOperativa(ProduccionParoOperativoCerrarPostVm vm)
        {
            if (!UsuarioEnSesion())
                return StatusCode(401, new { ok = false, mensaje = "La sesión expiró. Vuelve a iniciar sesión." });

            if (vm == null || vm.ProgramaProduccionID <= 0 || vm.ParoID <= 0)
                return BadRequest(new { ok = false, mensaje = "El paro de Producción no es válido." });

            vm.ObservacionesCierre = string.IsNullOrWhiteSpace(vm.ObservacionesCierre)
                ? null
                : vm.ObservacionesCierre.Trim();

            if (vm.ObservacionesCierre?.Length > 500)
                return BadRequest(new { ok = false, mensaje = "Las observaciones de cierre no pueden exceder 500 caracteres." });

            await using (var cn = new SqlConnection(ConnectionString))
            {
                await cn.OpenAsync();
                var permisos = await ObtenerPermisosProduccionUsuarioAsync(ObtenerUsuarioID(), cn);
                if (!(permisos.PuedeVerTodo || permisos.EsOperadorProduccion))
                    return StatusCode(403, new { ok = false, mensaje = "Tu perfil puede consultar el paro, pero no cerrarlo." });

                var estadoParo = await ObtenerEstadoParoParaCierreOperativaAsync(vm.ParoID, vm.ProgramaProduccionID, cn);
                if (estadoParo == null)
                    return BadRequest(new { ok = false, mensaje = "El paro ya no corresponde al programa mostrado, ya fue cerrado o dejó de estar disponible. Actualiza el Centro Operativo." });

                // La regla definitiva sigue viviendo en CerrarParo. Este guard solo evita que
                // el endpoint operativo exponga el mensaje heredado que usa SolicitudProduccionID
                // como si fuera una OF. La UI nunca permite el cierre manual de una urgente.
                if (estadoParo.EsInterrupcionUrgente)
                    return BadRequest(new
                    {
                        ok = false,
                        mensaje = "Esta interrupción urgente fue generada por Planeación y no puede cerrarse manualmente. Se cerrará automáticamente al finalizar la producción urgente."
                    });
            }

            TempData.Remove("Error");
            TempData.Remove("Success");

            var resultado = await CerrarParo(new ProduccionCerrarParoPostVm
            {
                ParoID = vm.ParoID,
                ObservacionesCierre = vm.ObservacionesCierre
            });

            var error = TempData["Error"]?.ToString();
            var exito = TempData["Success"]?.ToString();

            if (!string.IsNullOrWhiteSpace(error))
                return BadRequest(new { ok = false, mensaje = error });

            if (resultado is NotFoundResult)
                return NotFound(new { ok = false, mensaje = "El paro ya no existe o ya fue cerrado." });

            return Json(new
            {
                ok = true,
                programaProduccionId = vm.ProgramaProduccionID,
                paroId = vm.ParoID,
                mensaje = string.IsNullOrWhiteSpace(exito) ? "Paro cerrado correctamente." : exito
            });
        }

        private static async Task<ContextoParosPrograma?> ObtenerContextoParosProgramaAsync(int programaProduccionId, SqlConnection cn)
        {
            const string sql = @"
SELECT TOP(1)
    pp.ProgramaProduccionID,
    COALESCE(NULLIF(LTRIM(RTRIM(s.NumeroOFRecibida)),N''),NULLIF(LTRIM(RTRIM(s.FolioSolicitud)),N'')) AS NumeroOF,
    pp.MaquinaID,
    NULLIF(LTRIM(RTRIM(pp.MaquinaCodigo)),N'') AS MaquinaCodigo,
    NULLIF(LTRIM(RTRIM(pp.MaquinaNombre)),N'') AS MaquinaNombre,
    pp.ParteID,
    NULLIF(LTRIM(RTRIM(pp.NumeroParte)),N'') AS NumeroParte,
    NULLIF(LTRIM(RTRIM(pp.ReferenciaSAP)),N'') AS ReferenciaSAP,
    NULLIF(LTRIM(RTRIM(pp.DesignacionDescripcionSAP)),N'') AS DescripcionParte,
    pp.MoldeID,
    NULLIF(LTRIM(RTRIM(pp.MoldeCodigo)),N'') AS MoldeCodigo,
    e.EjecucionProduccionID,
    e.EstatusID,
    NULLIF(LTRIM(RTRIM(e.OperadorNombre)),N'') AS OperadorNombre,
    NULLIF(LTRIM(RTRIM(e.OperadorAuxiliarNombre)),N'') AS OperadorAuxiliarNombre
FROM dbo.Planeacion_ProgramaProduccion pp
LEFT JOIN dbo.SolicitudesProduccion s
    ON s.SolicitudProduccionID=pp.SolicitudProduccionID
   AND s.Activo=1
OUTER APPLY
(
    SELECT TOP(1)
        e0.EjecucionProduccionID,
        e0.EstatusID,
        e0.OperadorNombre,
        e0.OperadorAuxiliarNombre
    FROM dbo.Produccion_Ejecucion e0
    WHERE e0.ProgramaProduccionID=pp.ProgramaProduccionID
      AND e0.Activo=1
      AND e0.EstatusID<>@Cancelado
    ORDER BY e0.EjecucionProduccionID DESC
) e
WHERE pp.ProgramaProduccionID=@ProgramaProduccionID
  AND pp.Activo=1;";

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaProduccionId;
            cmd.Parameters.Add("@Cancelado", SqlDbType.Int).Value = ProduccionEstatus.Cancelado;

            await using var rd = await cmd.ExecuteReaderAsync();
            if (!await rd.ReadAsync()) return null;

            return new ContextoParosPrograma
            {
                ProgramaProduccionID = Convert.ToInt32(rd["ProgramaProduccionID"]),
                NumeroOF = rd["NumeroOF"] == DBNull.Value ? "Sin OF" : rd["NumeroOF"]?.ToString()?.Trim() ?? "Sin OF",
                MaquinaID = rd["MaquinaID"] == DBNull.Value ? null : Convert.ToInt32(rd["MaquinaID"]),
                MaquinaCodigo = rd["MaquinaCodigo"] == DBNull.Value ? null : rd["MaquinaCodigo"]?.ToString()?.Trim(),
                MaquinaNombre = rd["MaquinaNombre"] == DBNull.Value ? null : rd["MaquinaNombre"]?.ToString()?.Trim(),
                ParteID = rd["ParteID"] == DBNull.Value ? null : Convert.ToInt32(rd["ParteID"]),
                NumeroParte = rd["NumeroParte"] == DBNull.Value ? null : rd["NumeroParte"]?.ToString()?.Trim(),
                ReferenciaSAP = rd["ReferenciaSAP"] == DBNull.Value ? null : rd["ReferenciaSAP"]?.ToString()?.Trim(),
                DescripcionParte = rd["DescripcionParte"] == DBNull.Value ? null : rd["DescripcionParte"]?.ToString()?.Trim(),
                MoldeID = rd["MoldeID"] == DBNull.Value ? null : Convert.ToInt32(rd["MoldeID"]),
                MoldeCodigo = rd["MoldeCodigo"] == DBNull.Value ? null : rd["MoldeCodigo"]?.ToString()?.Trim(),
                EjecucionProduccionID = rd["EjecucionProduccionID"] == DBNull.Value ? 0 : Convert.ToInt32(rd["EjecucionProduccionID"]),
                EstatusID = rd["EstatusID"] == DBNull.Value ? 0 : Convert.ToInt32(rd["EstatusID"]),
                OperadorNombre = rd["OperadorNombre"] == DBNull.Value ? null : rd["OperadorNombre"]?.ToString()?.Trim(),
                OperadorAuxiliarNombre = rd["OperadorAuxiliarNombre"] == DBNull.Value ? null : rd["OperadorAuxiliarNombre"]?.ToString()?.Trim()
            };
        }

        private static ProduccionParoLadoOperativoVm MapearLadoParosOperativos(ContextoParosPrograma origen, string? lado)
        {
            return new ProduccionParoLadoOperativoVm
            {
                ProgramaProduccionID = origen.ProgramaProduccionID,
                EjecucionProduccionID = origen.EjecucionProduccionID,
                NumeroOF = origen.NumeroOF,
                LadoLhRh = lado,
                MaquinaID = origen.MaquinaID,
                MaquinaCodigo = origen.MaquinaCodigo,
                MaquinaNombre = origen.MaquinaNombre,
                ParteID = origen.ParteID,
                NumeroParte = origen.NumeroParte,
                ReferenciaSAP = origen.ReferenciaSAP,
                DescripcionParte = origen.DescripcionParte,
                MoldeID = origen.MoldeID,
                MoldeCodigo = origen.MoldeCodigo,
                OperadorNombre = origen.OperadorNombre,
                OperadorAuxiliarNombre = origen.OperadorAuxiliarNombre,
                EstatusID = origen.EstatusID
            };
        }

        private static (string? LadoActual, string? LadoPareja) ResolverLadosParosOperativos(ContextoParosPrograma actual, ContextoParosPrograma? pareja)
        {
            if (pareja == null) return (null, null);

            var ladoActual = DeterminarLadoLhRhConfiguracion(actual.ReferenciaSAP, actual.NumeroParte, actual.DescripcionParte);
            var ladoPareja = DeterminarLadoLhRhConfiguracion(pareja.ReferenciaSAP, pareja.NumeroParte, pareja.DescripcionParte);

            if (string.IsNullOrWhiteSpace(ladoActual) && string.Equals(ladoPareja, "LH", StringComparison.OrdinalIgnoreCase)) ladoActual = "RH";
            else if (string.IsNullOrWhiteSpace(ladoActual) && string.Equals(ladoPareja, "RH", StringComparison.OrdinalIgnoreCase)) ladoActual = "LH";

            if (string.IsNullOrWhiteSpace(ladoPareja) && string.Equals(ladoActual, "LH", StringComparison.OrdinalIgnoreCase)) ladoPareja = "RH";
            else if (string.IsNullOrWhiteSpace(ladoPareja) && string.Equals(ladoActual, "RH", StringComparison.OrdinalIgnoreCase)) ladoPareja = "LH";

            return (ladoActual, ladoPareja);
        }

        private static string? ResolverInconsistenciaParejaParos(ProduccionParejaLhRhVm? relacion, ContextoParosPrograma? pareja)
        {
            if (relacion == null) return null;
            if (!relacion.MismaMaquina) return "La pareja LH/RH ya no conserva la misma máquina.";
            if (!relacion.MismoMolde) return "La pareja LH/RH ya no conserva el mismo molde.";
            if (!relacion.MismaVentanaProgramada) return "La pareja LH/RH ya no conserva la misma ventana programada.";
            if (!relacion.EjecucionParejaID.HasValue || relacion.EjecucionParejaID.Value <= 0) return "No se encontró la ejecución activa de la OF pareja. Un paro físico LH/RH no puede gestionarse parcialmente.";
            if (pareja == null || pareja.EjecucionProduccionID != relacion.EjecucionParejaID.Value) return "La ejecución activa de la OF pareja cambió. Actualiza el Centro Operativo antes de gestionar el paro.";
            return null;
        }

        private static async Task<List<ProduccionParoMotivoOperativoVm>> CargarMotivosParoOperativosAsync(SqlConnection cn)
        {
            const string sql = @"
SELECT MotivoParoID,Nombre
FROM dbo.ERP_MotivosParoProduccion
WHERE Activo=1
ORDER BY Nombre;";

            var lista = new List<ProduccionParoMotivoOperativoVm>();
            await using var cmd = new SqlCommand(sql, cn);
            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                lista.Add(new ProduccionParoMotivoOperativoVm
                {
                    MotivoParoID = Convert.ToInt32(rd["MotivoParoID"]),
                    Nombre = rd["Nombre"]?.ToString()?.Trim() ?? string.Empty
                });
            }
            return lista;
        }

        private static async Task<List<ProduccionParoOperativoItemVm>> CargarParosOperativosAsync(
            IReadOnlyCollection<int> ejecuciones,
            IReadOnlyDictionary<int, (string NumeroOF, string? Lado)> referencias,
            SqlConnection cn)
        {
            var lista = new List<ProduccionParoOperativoItemVm>();
            if (ejecuciones == null || ejecuciones.Count == 0) return lista;

            var ids = ejecuciones.Where(x => x > 0).Distinct().ToList();
            if (ids.Count == 0) return lista;

            var parametros = ids.Select((_, i) => $"@E{i}").ToArray();
            var sql = $@"
SELECT
    p.ParoID,
    p.EjecucionProduccionID,
    p.ProgramaProduccionID,
    p.FechaInicioParo,
    p.FechaFinParo,
    CASE
        WHEN p.FechaFinParo IS NULL THEN DATEDIFF(MINUTE,p.FechaInicioParo,GETDATE())
        ELSE ISNULL(p.DuracionMinutos,DATEDIFF(MINUTE,p.FechaInicioParo,p.FechaFinParo))
    END AS DuracionMinutos,
    p.MotivoParoID,
    p.MotivoParoTexto,
    p.Descripcion,
    CAST(CASE
        WHEN ISNULL(p.EsMayorA15Minutos,0)=1 THEN 1
        WHEN DATEDIFF(MINUTE,p.FechaInicioParo,ISNULL(p.FechaFinParo,GETDATE()))>15 THEN 1
        ELSE 0
    END AS BIT) AS EsMayorA15Minutos,
    CAST(ISNULL(p.EsInterrupcionUrgente,0) AS BIT) AS EsInterrupcionUrgente,
    p.ProgramaUrgenteID,
    CAST(ISNULL(p.EsParoLhRh,0) AS BIT) AS EsParoLhRh,
    p.GrupoParoLhRh,
    COALESCE(NULLIF(LTRIM(RTRIM(su.NumeroOFRecibida)),N''),NULLIF(LTRIM(RTRIM(su.FolioSolicitud)),N'')) AS NumeroOFUrgente
FROM dbo.Produccion_Paros p
LEFT JOIN dbo.Planeacion_ProgramaProduccion pu
    ON pu.ProgramaProduccionID=p.ProgramaUrgenteID
   AND pu.Activo=1
LEFT JOIN dbo.SolicitudesProduccion su
    ON su.SolicitudProduccionID=pu.SolicitudProduccionID
   AND su.Activo=1
WHERE p.EjecucionProduccionID IN ({string.Join(",", parametros)})
  AND p.Activo=1
ORDER BY p.FechaInicioParo DESC,p.ParoID DESC;";

            await using var cmd = new SqlCommand(sql, cn);
            for (var i = 0; i < ids.Count; i++)
                cmd.Parameters.Add(parametros[i], SqlDbType.Int).Value = ids[i];

            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                var programaId = Convert.ToInt32(rd["ProgramaProduccionID"]);
                referencias.TryGetValue(programaId, out var referencia);

                lista.Add(new ProduccionParoOperativoItemVm
                {
                    ParoID = Convert.ToInt32(rd["ParoID"]),
                    EjecucionProduccionID = Convert.ToInt32(rd["EjecucionProduccionID"]),
                    ProgramaProduccionID = programaId,
                    NumeroOF = string.IsNullOrWhiteSpace(referencia.NumeroOF) ? "Sin OF" : referencia.NumeroOF,
                    LadoLhRh = referencia.Lado,
                    FechaInicioParo = Convert.ToDateTime(rd["FechaInicioParo"]),
                    FechaFinParo = rd["FechaFinParo"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaFinParo"]),
                    DuracionMinutos = Math.Max(0, rd["DuracionMinutos"] == DBNull.Value ? 0 : Convert.ToInt32(rd["DuracionMinutos"])),
                    MotivoParoID = rd["MotivoParoID"] == DBNull.Value ? null : Convert.ToInt32(rd["MotivoParoID"]),
                    MotivoParoTexto = rd["MotivoParoTexto"] == DBNull.Value ? null : rd["MotivoParoTexto"]?.ToString()?.Trim(),
                    Descripcion = rd["Descripcion"] == DBNull.Value ? null : rd["Descripcion"]?.ToString()?.Trim(),
                    EsMayorA15Minutos = rd["EsMayorA15Minutos"] != DBNull.Value && Convert.ToBoolean(rd["EsMayorA15Minutos"]),
                    EsInterrupcionUrgente = rd["EsInterrupcionUrgente"] != DBNull.Value && Convert.ToBoolean(rd["EsInterrupcionUrgente"]),
                    ProgramaUrgenteID = rd["ProgramaUrgenteID"] == DBNull.Value ? null : Convert.ToInt32(rd["ProgramaUrgenteID"]),
                    NumeroOFUrgente = rd["NumeroOFUrgente"] == DBNull.Value ? null : rd["NumeroOFUrgente"]?.ToString()?.Trim(),
                    EsParoLhRh = rd["EsParoLhRh"] != DBNull.Value && Convert.ToBoolean(rd["EsParoLhRh"]),
                    GrupoParoLhRh = rd["GrupoParoLhRh"] == DBNull.Value ? null : rd.GetGuid(rd.GetOrdinal("GrupoParoLhRh"))
                });
            }

            return lista;
        }

        private static List<ProduccionParoOperativoItemVm> ConsolidarParosFisicos(
            List<ProduccionParoOperativoItemVm> origen,
            int programaActualId)
        {
            var resultado = origen
                .Where(x => !x.EsParoLhRh || !x.GrupoParoLhRh.HasValue)
                .ToList();

            var grupos = origen
                .Where(x => x.EsParoLhRh && x.GrupoParoLhRh.HasValue)
                .GroupBy(x => x.GrupoParoLhRh!.Value);

            foreach (var grupo in grupos)
            {
                var filas = grupo.OrderBy(x => x.ParoID).ToList();
                var baseItem = filas.FirstOrDefault(x => x.ProgramaProduccionID == programaActualId) ?? filas[0];
                var pareja = filas.FirstOrDefault(x => x.ProgramaProduccionID != baseItem.ProgramaProduccionID);

                if (pareja != null)
                {
                    baseItem.EjecucionParejaID = pareja.EjecucionProduccionID;
                    baseItem.ProgramaParejaID = pareja.ProgramaProduccionID;
                    baseItem.NumeroOFPareja = pareja.NumeroOF;
                    baseItem.LadoParejaLhRh = pareja.LadoLhRh;
                    baseItem.EsMayorA15Minutos = filas.Any(x => x.EsMayorA15Minutos);
                    baseItem.EsInterrupcionUrgente = filas.Any(x => x.EsInterrupcionUrgente);
                    baseItem.FechaInicioParo = filas.Min(x => x.FechaInicioParo);
                    baseItem.DuracionMinutos = filas.Max(x => x.DuracionMinutos);
                    baseItem.FechaFinParo = filas.Any(x => !x.FechaFinParo.HasValue)
                        ? null
                        : filas.Max(x => x.FechaFinParo);
                    baseItem.NumeroOFUrgente = filas.Select(x => x.NumeroOFUrgente).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
                    baseItem.ProgramaUrgenteID = filas.Select(x => x.ProgramaUrgenteID).FirstOrDefault(x => x.HasValue);
                }

                resultado.Add(baseItem);
            }

            return resultado
                .OrderByDescending(x => x.FechaInicioParo)
                .ThenByDescending(x => x.ParoID)
                .ToList();
        }

        private static async Task<bool> EjecucionPerteneceProgramaOperativaAsync(int ejecucionProduccionId, int programaProduccionId, SqlConnection cn)
        {
            const string sql = @"
SELECT COUNT(1)
FROM dbo.Produccion_Ejecucion
WHERE EjecucionProduccionID=@EjecucionProduccionID
  AND ProgramaProduccionID=@ProgramaProduccionID
  AND Activo=1;";

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaProduccionId;
            return Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
        }

        private static async Task<EstadoParoParaCierreOperativa?> ObtenerEstadoParoParaCierreOperativaAsync(
            int paroId,
            int programaProduccionId,
            SqlConnection cn)
        {
            const string sql = @"
SELECT TOP(1)
    CAST(ISNULL(EsInterrupcionUrgente,0) AS BIT) AS EsInterrupcionUrgente
FROM dbo.Produccion_Paros
WHERE ParoID=@ParoID
  AND ProgramaProduccionID=@ProgramaProduccionID
  AND Activo=1
  AND FechaFinParo IS NULL;";

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@ParoID", SqlDbType.Int).Value = paroId;
            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaProduccionId;

            await using var rd = await cmd.ExecuteReaderAsync();
            if (!await rd.ReadAsync()) return null;

            return new EstadoParoParaCierreOperativa
            {
                EsInterrupcionUrgente = rd["EsInterrupcionUrgente"] != DBNull.Value && Convert.ToBoolean(rd["EsInterrupcionUrgente"])
            };
        }

        private sealed class EstadoParoParaCierreOperativa
        {
            public bool EsInterrupcionUrgente { get; set; }
        }

        private sealed class ContextoParosPrograma
        {
            public int ProgramaProduccionID { get; set; }
            public int EjecucionProduccionID { get; set; }
            public string NumeroOF { get; set; } = "Sin OF";
            public int? MaquinaID { get; set; }
            public string? MaquinaCodigo { get; set; }
            public string? MaquinaNombre { get; set; }
            public int? ParteID { get; set; }
            public string? NumeroParte { get; set; }
            public string? ReferenciaSAP { get; set; }
            public string? DescripcionParte { get; set; }
            public int? MoldeID { get; set; }
            public string? MoldeCodigo { get; set; }
            public string? OperadorNombre { get; set; }
            public string? OperadorAuxiliarNombre { get; set; }
            public int EstatusID { get; set; }
        }
    }
}
