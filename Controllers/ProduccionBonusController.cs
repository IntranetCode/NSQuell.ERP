using ERP.NSQuell.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace ERP.NSQuell.Controllers
{
    public sealed class ProduccionBonusController : Controller
    {
        private readonly IConfiguration _configuration;

        public ProduccionBonusController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        private string ConnectionString =>
            _configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("No se encontró la cadena de conexión DefaultConnection.");

        [HttpGet]
        public async Task<IActionResult> Index(DateTime? fecha = null)
        {
            if (!UsuarioEnSesion()) return RedirectToAction("Login", "Login");

            var fechaReferencia = (fecha ?? DateTime.Today).Date;
            var semanaInicio = ObtenerInicioSemana(fechaReferencia);
            var semanaFinExclusiva = semanaInicio.AddDays(7);
            var semanaActualInicio = ObtenerInicioSemana(DateTime.Today);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            var filasRanking = await ObtenerRankingPorSemanasAsync(semanaInicio, semanaFinExclusiva, cn);
            var ranking = filasRanking
                .Where(x => x.SemanaInicio.Date == semanaInicio.Date)
                .Select(x => x.Ranking)
                .OrderBy(x => x.Posicion)
                .ToList();

            var vm = new ProduccionBonusIndexVm
            {
                FechaReferencia = fechaReferencia,
                SemanaInicio = semanaInicio,
                SemanaFin = semanaInicio.AddDays(6),
                EsSemanaActual = semanaInicio == semanaActualInicio,
                Ranking = ranking,
                TotalOperadores = ranking.Count,
                TotalMovimientos = ranking.Sum(x => x.TotalMovimientos),
                TotalPiezasAbonadas = ranking.Sum(x => x.PiezasAbonadas),
                TotalPiezasDescontadas = ranking.Sum(x => x.PiezasDescontadas),
                BonusNetoSemana = ranking.Sum(x => x.BonusNeto)
            };

            return View(vm);
        }

        [HttpGet]
        public async Task<IActionResult> Historial(DateTime? fecha = null, int semanas = 12)
        {
            if (!UsuarioEnSesion()) return RedirectToAction("Login", "Login");

            if (semanas < 1) semanas = 12;
            if (semanas > 104) semanas = 104;

            var fechaReferencia = (fecha ?? DateTime.Today).Date;
            var semanaReferencia = ObtenerInicioSemana(fechaReferencia);
            var semanaActual = ObtenerInicioSemana(DateTime.Today);
            var fechaDesde = semanaReferencia.AddDays(-7 * (semanas - 1));
            var fechaHastaExclusiva = semanaReferencia.AddDays(7);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            var filas = await ObtenerRankingPorSemanasAsync(fechaDesde, fechaHastaExclusiva, cn);
            var vm = new ProduccionBonusHistorialVm
            {
                FechaReferencia = fechaReferencia,
                NumeroSemanas = semanas
            };

            for (var i = 0; i < semanas; i++)
            {
                var inicio = semanaReferencia.AddDays(-7 * i);
                var fin = inicio.AddDays(6);
                var rankingSemana = filas
                    .Where(x => x.SemanaInicio.Date == inicio.Date)
                    .Select(x => x.Ranking)
                    .OrderBy(x => x.Posicion)
                    .ToList();

                var primerLugar = rankingSemana.FirstOrDefault();

                vm.Semanas.Add(new ProduccionBonusSemanaResumenVm
                {
                    SemanaInicio = inicio,
                    SemanaFin = fin,
                    EsSemanaActual = inicio == semanaActual,
                    TotalOperadores = rankingSemana.Count,
                    TotalMovimientos = rankingSemana.Sum(x => x.TotalMovimientos),
                    PiezasAbonadas = rankingSemana.Sum(x => x.PiezasAbonadas),
                    PiezasDescontadas = rankingSemana.Sum(x => x.PiezasDescontadas),
                    BonusNeto = rankingSemana.Sum(x => x.BonusNeto),
                    PrimerLugarOperadorID = primerLugar?.OperadorID,
                    PrimerLugarOperadorNombre = primerLugar?.OperadorNombre,
                    PrimerLugarBonus = primerLugar?.BonusNeto ?? 0
                });
            }

            return View(vm);
        }

        [HttpGet]
        public async Task<IActionResult> DetalleOperador(int operadorId, DateTime? fecha = null)
        {
            if (!UsuarioEnSesion()) return RedirectToAction("Login", "Login");
            if (operadorId <= 0) return NotFound();

            var fechaReferencia = (fecha ?? DateTime.Today).Date;
            var semanaInicio = ObtenerInicioSemana(fechaReferencia);
            var semanaFinExclusiva = semanaInicio.AddDays(7);
            var semanaActual = ObtenerInicioSemana(DateTime.Today);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            var operadorNombre = await ObtenerNombreOperadorAsync(operadorId, cn);
            if (string.IsNullOrWhiteSpace(operadorNombre)) return NotFound();

            var filasRanking = await ObtenerRankingPorSemanasAsync(semanaInicio, semanaFinExclusiva, cn);
            var rankingSemana = filasRanking
                .Where(x => x.SemanaInicio.Date == semanaInicio.Date)
                .Select(x => x.Ranking)
                .OrderBy(x => x.Posicion)
                .ToList();

            var posicion = rankingSemana
                .FirstOrDefault(x => x.OperadorID == operadorId)
                ?.Posicion ?? 0;

            var ordenes = await ObtenerOrdenesFabricacionOperadorSemanaAsync(
                operadorId,
                semanaInicio,
                semanaFinExclusiva,
                cn);

            var movimientos = await ObtenerMovimientosOperadorSemanaAsync(
                operadorId,
                semanaInicio,
                semanaFinExclusiva,
                cn);

            var diccionarioOf = ordenes.ToDictionary(x => x.EjecucionProduccionID);

            foreach (var movimiento in movimientos)
            {
                var clave = movimiento.EjecucionProduccionID ?? 0;

                if (!diccionarioOf.TryGetValue(clave, out var orden))
                {
                    orden = new ProduccionBonusOfResumenVm
                    {
                        EjecucionProduccionID = clave,
                        NumeroOF = clave > 0 ? null : "AJUSTES SIN OF"
                    };
                    ordenes.Add(orden);
                    diccionarioOf[clave] = orden;
                }

                orden.Movimientos.Add(movimiento);
            }

            foreach (var orden in ordenes)
            {
                orden.Movimientos = orden.Movimientos
                    .OrderByDescending(x => x.FechaMovimiento)
                    .ThenByDescending(x => x.MovimientoBonusID)
                    .ToList();
            }

            ordenes = ordenes
                .OrderByDescending(x => x.BonusNeto)
                .ThenByDescending(x => x.EjecucionProduccionID)
                .ToList();

            var vm = new ProduccionBonusDetalleOperadorVm
            {
                OperadorID = operadorId,
                OperadorNombre = operadorNombre,
                FechaReferencia = fechaReferencia,
                SemanaInicio = semanaInicio,
                SemanaFin = semanaInicio.AddDays(6),
                EsSemanaActual = semanaInicio == semanaActual,
                PosicionRanking = posicion,
                TotalOF = ordenes.Count(x => x.EjecucionProduccionID > 0),
                TotalMovimientos = movimientos.Count,
                PiezasAbonadas = movimientos.Where(x => x.PiezasMovimiento > 0).Sum(x => (long)x.PiezasMovimiento),
                PiezasDescontadas = movimientos.Where(x => x.PiezasMovimiento < 0).Sum(x => Math.Abs((long)x.PiezasMovimiento)),
                BonusNeto = movimientos.Sum(x => (long)x.PiezasMovimiento),
                OrdenesFabricacion = ordenes
            };

            return View(vm);
        }

        private static async Task<List<ProduccionBonusRankingSemanaFila>> ObtenerRankingPorSemanasAsync(
            DateTime fechaDesde,
            DateTime fechaHastaExclusiva,
            SqlConnection cn)
        {
            var lista = new List<ProduccionBonusRankingSemanaFila>();

            const string sql = @"
SELECT
    CONVERT(date,DATEADD(DAY,-(DATEDIFF(DAY,0,CONVERT(date,m.FechaMovimiento))%7),CONVERT(date,m.FechaMovimiento))) AS SemanaInicio,
    m.OperadorID,
    LTRIM(RTRIM(CONCAT(
        ISNULL(p.Nombre,N''),
        N' ',
        ISNULL(p.ApellidoPaterno,N''),
        N' ',
        ISNULL(p.ApellidoMaterno,N'')
    ))) AS OperadorNombre,
    SUM(CASE WHEN m.PiezasMovimiento>0 THEN CONVERT(BIGINT,m.PiezasMovimiento) ELSE CONVERT(BIGINT,0) END) AS PiezasAbonadas,
    SUM(CASE WHEN m.PiezasMovimiento<0 THEN -CONVERT(BIGINT,m.PiezasMovimiento) ELSE CONVERT(BIGINT,0) END) AS PiezasDescontadas,
    SUM(CONVERT(BIGINT,m.PiezasMovimiento)) AS BonusNeto,
    COUNT(1) AS TotalMovimientos,
    COUNT(DISTINCT m.EjecucionProduccionID) AS TotalOF,
    MAX(m.FechaMovimiento) AS UltimoMovimiento
FROM dbo.Produccion_BonusOperadorMovimientos m
INNER JOIN dbo.Persona p
    ON p.PersonaID=m.OperadorID
WHERE m.Activo=1
  AND m.FechaMovimiento>=@FechaDesde
  AND m.FechaMovimiento<@FechaHasta
GROUP BY
    CONVERT(date,DATEADD(DAY,-(DATEDIFF(DAY,0,CONVERT(date,m.FechaMovimiento))%7),CONVERT(date,m.FechaMovimiento))),
    m.OperadorID,
    p.Nombre,
    p.ApellidoPaterno,
    p.ApellidoMaterno
ORDER BY
    SemanaInicio DESC,
    BonusNeto DESC,
    PiezasAbonadas DESC,
    OperadorNombre;";

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@FechaDesde", SqlDbType.DateTime2).Value = fechaDesde;
            cmd.Parameters.Add("@FechaHasta", SqlDbType.DateTime2).Value = fechaHastaExclusiva;

            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                lista.Add(new ProduccionBonusRankingSemanaFila
                {
                    SemanaInicio = Convert.ToDateTime(rd["SemanaInicio"]).Date,
                    Ranking = new ProduccionBonusRankingItemVm
                    {
                        OperadorID = Convert.ToInt32(rd["OperadorID"]),
                        OperadorNombre = rd["OperadorNombre"] == DBNull.Value
                            ? $"Operador #{Convert.ToInt32(rd["OperadorID"])}"
                            : rd["OperadorNombre"]?.ToString()?.Trim() ?? string.Empty,
                        PiezasAbonadas = Convert.ToInt64(rd["PiezasAbonadas"]),
                        PiezasDescontadas = Convert.ToInt64(rd["PiezasDescontadas"]),
                        BonusNeto = Convert.ToInt64(rd["BonusNeto"]),
                        TotalMovimientos = Convert.ToInt32(rd["TotalMovimientos"]),
                        TotalOF = Convert.ToInt32(rd["TotalOF"]),
                        UltimoMovimiento = rd["UltimoMovimiento"] == DBNull.Value
                            ? null
                            : Convert.ToDateTime(rd["UltimoMovimiento"])
                    }
                });
            }

            foreach (var grupo in lista.GroupBy(x => x.SemanaInicio))
            {
                var ordenados = grupo
                    .OrderByDescending(x => x.Ranking.BonusNeto)
                    .ThenByDescending(x => x.Ranking.PiezasAbonadas)
                    .ThenBy(x => x.Ranking.OperadorNombre)
                    .ToList();

                for (var i = 0; i < ordenados.Count; i++)
                    ordenados[i].Ranking.Posicion = i + 1;
            }

            return lista;
        }

        private static async Task<List<ProduccionBonusOfResumenVm>> ObtenerOrdenesFabricacionOperadorSemanaAsync(
            int operadorId,
            DateTime semanaInicio,
            DateTime semanaFinExclusiva,
            SqlConnection cn)
        {
            var lista = new List<ProduccionBonusOfResumenVm>();

            const string sql = @"
SELECT
    ISNULL(m.EjecucionProduccionID,0) AS EjecucionProduccionID,
    MAX(e.ProgramaProduccionID) AS ProgramaProduccionID,
    MAX(e.SolicitudProduccionID) AS SolicitudProduccionID,
    MAX(e.SolicitudProduccionDetalleID) AS SolicitudProduccionDetalleID,
    COALESCE(
        NULLIF(LTRIM(RTRIM(MAX(s.NumeroOFRecibida))),N''),
        NULLIF(LTRIM(RTRIM(MAX(s.FolioSolicitud))),N''),
        CASE
            WHEN MAX(e.SolicitudProduccionID) IS NOT NULL
                THEN CONCAT(N'OF-ID-',MAX(e.SolicitudProduccionID))
            WHEN m.EjecucionProduccionID IS NULL
                THEN N'AJUSTES SIN OF'
            ELSE CONCAT(N'EJECUCIÓN-',m.EjecucionProduccionID)
        END
    ) AS NumeroOF,
    MAX(e.ParteID) AS ParteID,
    MAX(e.NumeroParte) AS NumeroParte,
    MAX(e.ReferenciaSAP) AS ReferenciaSAP,
    MAX(e.DescripcionParte) AS DescripcionParte,
    MAX(e.MaquinaID) AS MaquinaID,
    MAX(e.MaquinaCodigo) AS MaquinaCodigo,
    MAX(e.MaquinaNombre) AS MaquinaNombre,
    MAX(e.FechaInicioReal) AS FechaInicioReal,
    MAX(e.FechaFinReal) AS FechaFinReal,
    SUM(CASE WHEN m.PiezasMovimiento>0 THEN CONVERT(BIGINT,m.PiezasMovimiento) ELSE CONVERT(BIGINT,0) END) AS PiezasAbonadas,
    SUM(CASE WHEN m.PiezasMovimiento<0 THEN -CONVERT(BIGINT,m.PiezasMovimiento) ELSE CONVERT(BIGINT,0) END) AS PiezasDescontadas,
    SUM(CONVERT(BIGINT,m.PiezasMovimiento)) AS BonusNeto,
    COUNT(1) AS TotalMovimientos,
    COUNT(DISTINCT m.RegistroHoraID) AS TotalCapturas,
    COUNT(DISTINCT CASE WHEN ISNULL(rh.EsTiempoExtra,0)=1 THEN m.RegistroHoraID END) AS TotalCapturasTiempoExtra
FROM dbo.Produccion_BonusOperadorMovimientos m
LEFT JOIN dbo.Produccion_Ejecucion e
    ON e.EjecucionProduccionID=m.EjecucionProduccionID
LEFT JOIN dbo.SolicitudesProduccion s
    ON s.SolicitudProduccionID=e.SolicitudProduccionID
   AND s.Activo=1
LEFT JOIN dbo.Produccion_RegistroHora rh
    ON rh.RegistroHoraID=m.RegistroHoraID
   AND rh.Activo=1
WHERE m.OperadorID=@OperadorID
  AND m.Activo=1
  AND m.FechaMovimiento>=@SemanaInicio
  AND m.FechaMovimiento<@SemanaFin
GROUP BY
    m.EjecucionProduccionID
ORDER BY
    BonusNeto DESC,
    EjecucionProduccionID DESC;";

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@OperadorID", SqlDbType.Int).Value = operadorId;
            cmd.Parameters.Add("@SemanaInicio", SqlDbType.DateTime2).Value = semanaInicio;
            cmd.Parameters.Add("@SemanaFin", SqlDbType.DateTime2).Value = semanaFinExclusiva;

            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                lista.Add(new ProduccionBonusOfResumenVm
                {
                    EjecucionProduccionID = Convert.ToInt32(rd["EjecucionProduccionID"]),
                    ProgramaProduccionID = NullableEntero(rd, "ProgramaProduccionID"),
                    SolicitudProduccionID = NullableEntero(rd, "SolicitudProduccionID"),
                    SolicitudProduccionDetalleID = NullableEntero(rd, "SolicitudProduccionDetalleID"),
                    NumeroOF = TextoNullable(rd, "NumeroOF"),
                    ParteID = NullableEntero(rd, "ParteID"),
                    NumeroParte = TextoNullable(rd, "NumeroParte"),
                    ReferenciaSAP = TextoNullable(rd, "ReferenciaSAP"),
                    DescripcionParte = TextoNullable(rd, "DescripcionParte"),
                    MaquinaID = NullableEntero(rd, "MaquinaID"),
                    MaquinaCodigo = TextoNullable(rd, "MaquinaCodigo"),
                    MaquinaNombre = TextoNullable(rd, "MaquinaNombre"),
                    FechaInicioReal = NullableFecha(rd, "FechaInicioReal"),
                    FechaFinReal = NullableFecha(rd, "FechaFinReal"),
                    PiezasAbonadas = Convert.ToInt64(rd["PiezasAbonadas"]),
                    PiezasDescontadas = Convert.ToInt64(rd["PiezasDescontadas"]),
                    BonusNeto = Convert.ToInt64(rd["BonusNeto"]),
                    TotalMovimientos = Convert.ToInt32(rd["TotalMovimientos"]),
                    TotalCapturas = Convert.ToInt32(rd["TotalCapturas"]),
                    TotalCapturasTiempoExtra = Convert.ToInt32(rd["TotalCapturasTiempoExtra"])
                });
            }

            return lista;
        }

        private static async Task<List<ProduccionBonusMovimientoDetalleVm>> ObtenerMovimientosOperadorSemanaAsync(int operadorId, DateTime semanaInicio, DateTime semanaFinExclusiva, SqlConnection cn)
        {
            var lista = new List<ProduccionBonusMovimientoDetalleVm>();

            const string sql = @"
SELECT
    m.MovimientoBonusID,
    m.OperadorID,
    LTRIM(RTRIM(CONCAT(ISNULL(p.Nombre,N''),N' ',ISNULL(p.ApellidoPaterno,N''),N' ',ISNULL(p.ApellidoMaterno,N'')))) AS OperadorNombre,
    m.EjecucionProduccionID,
    m.RegistroHoraID,
    m.MonitoreoID,
    m.DisposicionID,
    m.TipoMovimiento,
    m.PiezasMovimiento,
    m.PiezasReferencia,
    m.Motivo,
    m.ReferenciaEvento,
    m.UsuarioCreacionID,
    m.FechaMovimiento,
    m.Activo,
    rh.FechaProduccion,
    rh.HoraInicio,
    rh.HoraFin,
    ISNULL(rh.EsTiempoExtra,0) AS EsTiempoExtra,
    rh.NumeroCorteTiempoExtra,
    rh.TipoBloque,
    rh.CantidadOK,
    rh.CantidadSospechosa,
    rh.CantidadScrap,
    ap.AjusteProduccionID,
    ap.OKAntes AS AjusteOKAntes,
    ap.ScrapAntes AS AjusteScrapAntes,
    ap.OKDespues AS AjusteOKDespues,
    ap.ScrapDespues AS AjusteScrapDespues,
    ap.Motivo AS AjusteMotivo,
    ap.UsuarioAjusteID,
    LTRIM(RTRIM(CONCAT(ISNULL(pa.Nombre,N''),N' ',ISNULL(pa.ApellidoPaterno,N''),N' ',ISNULL(pa.ApellidoMaterno,N'')))) AS UsuarioAjusteNombre,
    ap.FechaAjuste
FROM dbo.Produccion_BonusOperadorMovimientos m
INNER JOIN dbo.Persona p
    ON p.PersonaID=m.OperadorID
LEFT JOIN dbo.Produccion_RegistroHora rh
    ON rh.RegistroHoraID=m.RegistroHoraID
   AND rh.Activo=1
LEFT JOIN dbo.Produccion_RegistroHoraAjustesProduccion ap
    ON ap.MovimientoBonusID=m.MovimientoBonusID
   AND ap.Activo=1
LEFT JOIN dbo.Usuarios ua
    ON ua.UsuarioID=ap.UsuarioAjusteID
LEFT JOIN dbo.Persona pa
    ON pa.PersonaID=ua.PersonaID
WHERE m.OperadorID=@OperadorID
  AND m.Activo=1
  AND m.FechaMovimiento>=@SemanaInicio
  AND m.FechaMovimiento<@SemanaFin
ORDER BY
    m.FechaMovimiento DESC,
    m.MovimientoBonusID DESC;";

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@OperadorID", SqlDbType.Int).Value = operadorId;
            cmd.Parameters.Add("@SemanaInicio", SqlDbType.DateTime2).Value = semanaInicio;
            cmd.Parameters.Add("@SemanaFin", SqlDbType.DateTime2).Value = semanaFinExclusiva;

            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                var usuarioAjusteId = NullableEntero(rd, "UsuarioAjusteID");
                var usuarioAjusteNombre = TextoNullable(rd, "UsuarioAjusteNombre");

                lista.Add(new ProduccionBonusMovimientoDetalleVm
                {
                    MovimientoBonusID = Convert.ToInt64(rd["MovimientoBonusID"]),
                    OperadorID = Convert.ToInt32(rd["OperadorID"]),
                    OperadorNombre = TextoNullable(rd, "OperadorNombre"),
                    EjecucionProduccionID = NullableEntero(rd, "EjecucionProduccionID"),
                    RegistroHoraID = NullableEntero(rd, "RegistroHoraID"),
                    MonitoreoID = NullableEntero(rd, "MonitoreoID"),
                    DisposicionID = NullableEntero(rd, "DisposicionID"),
                    TipoMovimiento = TextoNullable(rd, "TipoMovimiento") ?? string.Empty,
                    PiezasMovimiento = Convert.ToInt32(rd["PiezasMovimiento"]),
                    PiezasReferencia = NullableEntero(rd, "PiezasReferencia"),
                    Motivo = TextoNullable(rd, "Motivo") ?? string.Empty,
                    ReferenciaEvento = TextoNullable(rd, "ReferenciaEvento"),
                    UsuarioCreacionID = NullableEntero(rd, "UsuarioCreacionID"),
                    FechaMovimiento = Convert.ToDateTime(rd["FechaMovimiento"]),
                    Activo = Convert.ToBoolean(rd["Activo"]),
                    FechaProduccion = NullableFecha(rd, "FechaProduccion"),
                    HoraInicio = NullableTiempo(rd, "HoraInicio"),
                    HoraFin = NullableTiempo(rd, "HoraFin"),
                    EsTiempoExtra = rd["EsTiempoExtra"] != DBNull.Value && Convert.ToBoolean(rd["EsTiempoExtra"]),
                    NumeroCorteTiempoExtra = NullableEntero(rd, "NumeroCorteTiempoExtra"),
                    TipoBloque = TextoNullable(rd, "TipoBloque"),
                    CantidadOK = NullableEntero(rd, "CantidadOK"),
                    CantidadSospechosa = NullableEntero(rd, "CantidadSospechosa"),
                    CantidadScrap = NullableEntero(rd, "CantidadScrap"),
                    AjusteProduccionID = rd["AjusteProduccionID"] == DBNull.Value ? null : Convert.ToInt64(rd["AjusteProduccionID"]),
                    AjusteOKAntes = NullableEntero(rd, "AjusteOKAntes"),
                    AjusteScrapAntes = NullableEntero(rd, "AjusteScrapAntes"),
                    AjusteOKDespues = NullableEntero(rd, "AjusteOKDespues"),
                    AjusteScrapDespues = NullableEntero(rd, "AjusteScrapDespues"),
                    AjusteMotivo = TextoNullable(rd, "AjusteMotivo"),
                    UsuarioAjusteID = usuarioAjusteId,
                    UsuarioAjusteNombre = usuarioAjusteId.HasValue
                        ? string.IsNullOrWhiteSpace(usuarioAjusteNombre)
                            ? $"Usuario #{usuarioAjusteId.Value}"
                            : usuarioAjusteNombre
                        : null,
                    FechaAjuste = NullableFecha(rd, "FechaAjuste")
                });
            }

            return lista;
        }
        private static async Task<string?> ObtenerNombreOperadorAsync(int operadorId, SqlConnection cn)
        {
            if (operadorId <= 0) return null;

            const string sql = @"
SELECT TOP(1)
    LTRIM(RTRIM(CONCAT(
        ISNULL(Nombre,N''),
        N' ',
        ISNULL(ApellidoPaterno,N''),
        N' ',
        ISNULL(ApellidoMaterno,N'')
    ))) AS NombreCompleto
FROM dbo.Persona
WHERE PersonaID=@OperadorID;";

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@OperadorID", SqlDbType.Int).Value = operadorId;

            var resultado = await cmd.ExecuteScalarAsync();
            if (resultado == null || resultado == DBNull.Value) return null;

            var nombre = resultado.ToString()?.Trim();
            return string.IsNullOrWhiteSpace(nombre) ? $"Operador #{operadorId}" : nombre;
        }

        private static DateTime ObtenerInicioSemana(DateTime fecha)
        {
            fecha = fecha.Date;
            var diferencia = ((int)fecha.DayOfWeek + 6) % 7;
            return fecha.AddDays(-diferencia);
        }

        private bool UsuarioEnSesion()
        {
            return HttpContext.Session.GetInt32("UsuarioID").HasValue;
        }

        private static int? NullableEntero(SqlDataReader rd, string columna)
        {
            var ordinal = rd.GetOrdinal(columna);
            return rd.IsDBNull(ordinal) ? null : Convert.ToInt32(rd.GetValue(ordinal));
        }

        private static DateTime? NullableFecha(SqlDataReader rd, string columna)
        {
            var ordinal = rd.GetOrdinal(columna);
            return rd.IsDBNull(ordinal) ? null : Convert.ToDateTime(rd.GetValue(ordinal));
        }

        private static TimeSpan? NullableTiempo(SqlDataReader rd, string columna)
        {
            var ordinal = rd.GetOrdinal(columna);
            if (rd.IsDBNull(ordinal)) return null;

            var valor = rd.GetValue(ordinal);

            if (valor is TimeSpan tiempo)
                return tiempo;

            if (TimeSpan.TryParse(valor.ToString(), out var convertido))
                return convertido;

            return null;
        }

        private static string? TextoNullable(SqlDataReader rd, string columna)
        {
            var ordinal = rd.GetOrdinal(columna);
            return rd.IsDBNull(ordinal)
                ? null
                : rd.GetValue(ordinal)?.ToString()?.Trim();
        }

        private sealed class ProduccionBonusRankingSemanaFila
        {
            public DateTime SemanaInicio { get; set; }
            public ProduccionBonusRankingItemVm Ranking { get; set; } = new();
        }
    }
}