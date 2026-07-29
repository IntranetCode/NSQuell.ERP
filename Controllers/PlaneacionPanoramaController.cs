using ERP.NSQuell.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using System.Data;

namespace ERP.NSQuell.Controllers
{
    public class PlaneacionPanoramaController : Controller
    {
        private readonly IConfiguration _configuration;

        public PlaneacionPanoramaController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        private string ConnectionString =>
            _configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("No se encontró la cadena de conexión DefaultConnection.");

        [HttpGet]
        public async Task<IActionResult> Index(
            string? vista,
            DateTime? fechaDesde,
            DateTime? fechaHasta,
            int? clienteId,
            int? maquinaId,
            int? parteId,
            int? estatusId)
        {
            vista = NormalizarVista(vista);

            var rango = ResolverRango(vista, fechaDesde, fechaHasta);

            var renglones = await ObtenerPanoramaAsync(
                rango.Desde,
                rango.Hasta,
                clienteId,
                maquinaId,
                parteId,
                estatusId
            );

            var clientesPanorama = renglones
                .GroupBy(x => new
                {
                    x.ClienteID,
                    ClienteNombre = string.IsNullOrWhiteSpace(x.ClienteNombre)
                        ? "Sin cliente"
                        : x.ClienteNombre
                })
                .Select(gCliente => new PlaneacionPanoramaClienteVm
                {
                    ClienteID = gCliente.Key.ClienteID,
                    ClienteNombre = gCliente.Key.ClienteNombre,

                    Partes = gCliente
                        .GroupBy(x => new
                        {
                            x.ParteID,
                            x.NumeroParte,
                            x.ReferenciaSAP,
                            x.DesignacionDescripcionSAP
                        })
                        .Select(gParte =>
                        {
                            var primero = gParte
                                .OrderBy(x => x.FechaRequerida ?? x.FechaInicioProgramada ?? DateTime.MaxValue)
                                .First();

                            return new PlaneacionPanoramaParteVm
                            {
                                ClienteID = primero.ClienteID,
                                ClienteNombre = primero.ClienteNombre,

                                ParteID = gParte.Key.ParteID,
                                NumeroParte = gParte.Key.NumeroParte,
                                ReferenciaSAP = gParte.Key.ReferenciaSAP,
                                DesignacionDescripcionSAP = gParte.Key.DesignacionDescripcionSAP,

                                MaquinaID = primero.MaquinaID,
                                MaquinaCodigo = primero.MaquinaCodigo,
                                MaquinaNombre = primero.MaquinaNombre,

                                MoldeID = primero.MoldeID,
                                MoldeCodigo = primero.MoldeCodigo,

                                Color = primero.Color,

                                Cavidades = primero.Cavidades,
                                Ciclo = primero.Ciclo,
                                ObjetivoHora = primero.ObjetivoHora,
                                PesoBrutoPieza = primero.PesoBrutoPieza,

                                MaterialID = primero.MaterialID,
                                MaterialCodigo = primero.MaterialCodigo,
                                MaterialDescripcion = primero.MaterialDescripcion,

                                EmbalajeCodigo = primero.EmbalajeCodigo,
                                EmbalajeDescripcion = primero.EmbalajeDescripcion,
                                PiezasPorEmbalaje = primero.PiezasPorEmbalaje,

                                Renglones = gParte
                                    .OrderBy(x => x.FechaRequerida ?? x.FechaInicioProgramada ?? DateTime.MaxValue)
                                    .ThenBy(x => x.FechaInicioProgramada)
                                    .ThenBy(x => x.MaquinaCodigo)
                                    .ThenBy(x => x.SecuenciaMaquina)
                                    .ThenBy(x => x.ReleaseDetalleID)
                                    .ToList()
                            };
                        })
                        .OrderBy(x => x.ReferenciaSAP ?? x.NumeroParte)
                        .ToList()
                })
                .OrderBy(x => x.ClienteNombre)
                .ToList();

            var vm = new PlaneacionPanoramaVm
            {
                Vista = vista,
                FechaDesde = rango.Desde,
                FechaHasta = rango.Hasta,

                ClienteID = clienteId,
                MaquinaID = maquinaId,
                ParteID = parteId,
                EstatusID = estatusId,

                ClientesPanorama = clientesPanorama,

                Clientes = await CargarClientesAsync(clienteId),
                Maquinas = await CargarMaquinasAsync(maquinaId),
                Partes = await CargarPartesAsync(parteId),
                Estatus = CargarEstatus(estatusId)
            };

            return View(vm);
        }

        private static string NormalizarVista(string? vista)
        {
            if (string.IsNullOrWhiteSpace(vista))
                return PlaneacionPanoramaVista.Semana;

            vista = vista.Trim().ToUpper();

            return vista switch
            {
                PlaneacionPanoramaVista.Dia => PlaneacionPanoramaVista.Dia,
                PlaneacionPanoramaVista.Semana => PlaneacionPanoramaVista.Semana,
                PlaneacionPanoramaVista.Mes => PlaneacionPanoramaVista.Mes,
                PlaneacionPanoramaVista.Anio => PlaneacionPanoramaVista.Anio,
                PlaneacionPanoramaVista.LargoPlazo => PlaneacionPanoramaVista.LargoPlazo,
                _ => PlaneacionPanoramaVista.Semana
            };
        }

        private static (DateTime Desde, DateTime Hasta) ResolverRango(
            string vista,
            DateTime? fechaDesde,
            DateTime? fechaHasta)
        {
            var hoy = DateTime.Today;

            if (fechaDesde.HasValue || fechaHasta.HasValue)
            {
                var desdeManual = fechaDesde?.Date ?? hoy;
                var hastaManual = fechaHasta?.Date ?? desdeManual;

                if (hastaManual < desdeManual)
                    hastaManual = desdeManual;

                return (desdeManual, hastaManual);
            }

            DateTime desde;
            DateTime hasta;

            switch (vista)
            {
                case PlaneacionPanoramaVista.Dia:
                    desde = hoy;
                    hasta = hoy;
                    break;

                case PlaneacionPanoramaVista.Mes:
                    desde = new DateTime(hoy.Year, hoy.Month, 1);
                    hasta = desde.AddMonths(1).AddDays(-1);
                    break;

                case PlaneacionPanoramaVista.Anio:
                    desde = new DateTime(hoy.Year, 1, 1);
                    hasta = new DateTime(hoy.Year, 12, 31);
                    break;

                case PlaneacionPanoramaVista.LargoPlazo:
                    desde = hoy;
                    hasta = hoy.AddMonths(12);
                    break;

                case PlaneacionPanoramaVista.Semana:
                default:
                    var diferencia = ((int)hoy.DayOfWeek + 6) % 7;
                    desde = hoy.AddDays(-diferencia);
                    hasta = desde.AddDays(6);
                    break;
            }

            return (desde, hasta);
        }

        private async Task<List<PlaneacionPanoramaRenglonVm>> ObtenerPanoramaAsync(
            DateTime fechaDesde,
            DateTime fechaHasta,
            int? clienteId,
            int? maquinaId,
            int? parteId,
            int? estatusId)
        {
            var lista = new List<PlaneacionPanoramaRenglonVm>();

            const string sql = @"
SELECT
    d.ReleaseDetalleID,
    d.ReleaseID,
    r.FolioRelease,
    d.FechaCarga,
    d.FechaRequerida,

    d.ProgramaProduccionID,
    COALESCE(pp.SolicitudProduccionID, d.SolicitudProduccionID) AS SolicitudProduccionID,
    pp.SolicitudProduccionDetalleID,

    r.ClienteID,
    ISNULL(c.Nombre, r.ClienteNombre) AS ClienteNombre,

    d.ParteID,
    d.NumeroParte,
    d.ReferenciaSAP,
    d.DesignacionDescripcionSAP,

    d.CantidadRequerida,
    ISNULL(d.PiezasDesdePT, 0) AS PiezasDesdePT,

    CASE
        WHEN pp.ProgramaProduccionID IS NOT NULL THEN ISNULL(pp.CantidadProgramada, 0)
        ELSE ISNULL(d.PiezasAProducir, 0)
    END AS CantidadProgramada,

    ISNULL(pp.CantidadProducida, 0) AS CantidadProducida,

    CASE
        WHEN pp.ProgramaProduccionID IS NOT NULL THEN ISNULL(pp.CantidadPendiente, pp.CantidadProgramada)
        ELSE ISNULL(d.PiezasAProducir, 0)
    END AS CantidadPendiente,

    COALESCE(pp.MaquinaID, d.MaquinaSugeridaID) AS MaquinaID,
    COALESCE(pp.MaquinaCodigo, d.MaquinaSugeridaCodigo) AS MaquinaCodigo,
    COALESCE(pp.MaquinaNombre, d.MaquinaSugeridaNombre) AS MaquinaNombre,

    COALESCE(pp.MoldeID, d.MoldeID) AS MoldeID,
    COALESCE(pp.MoldeCodigo, d.MoldeCodigo) AS MoldeCodigo,

    COALESCE(NULLIF(pp.Color, ''), t.Color) AS Color,

    COALESCE(pp.Cavidades, t.Cavidades) AS Cavidades,
    COALESCE(pp.Ciclo, t.Ciclo) AS Ciclo,
    COALESCE(pp.ObjetivoHora, d.ObjetivoHora, t.ObjetivoHora) AS ObjetivoHora,
    COALESCE(pp.PesoBrutoPieza, d.PesoBrutoPieza, t.PesoBrutoPieza) AS PesoBrutoPieza,

    COALESCE(pp.MaterialID, d.MaterialID, t.MaterialID) AS MaterialID,
    COALESCE(pp.MaterialCodigo, d.MaterialCodigo, t.MaterialCodigo) AS MaterialCodigo,
    COALESCE(pp.MaterialDescripcion, d.MaterialDescripcion, t.MaterialDescripcion) AS MaterialDescripcion,

    COALESCE(pp.CantidadMpKg, d.MPRequeridaKg) AS CantidadMpKg,

    COALESCE(pp.EmbalajeCodigo, d.EmbalajeCodigo, t.EmbalajeCodigo) AS EmbalajeCodigo,
    COALESCE(pp.EmbalajeDescripcion, d.EmbalajeDescripcion, t.EmbalajeDescripcion) AS EmbalajeDescripcion,
    COALESCE(pp.PiezasPorEmbalaje, d.PiezasPorEmbalaje, t.PiezasPorEmbalaje) AS PiezasPorEmbalaje,
    COALESCE(pp.CantidadEmbalajes, d.EmbalajeRequerido) AS CantidadEmbalajes,

    pp.CondicionProduccion,
    pp.SecuenciaMaquina,

    COALESCE(pp.FechaInicioProgramada, d.FechaInicioSugerida) AS FechaInicioProgramada,
    COALESCE(pp.FechaFinProgramada, d.FechaFinEstimada) AS FechaFinProgramada,
    COALESCE(pp.HorasProgramadas, d.HorasNecesarias) AS HorasProgramadas,

    pp.Cambio,
    pp.Arranque,

    CASE
        WHEN pp.EstatusID IS NOT NULL THEN pp.EstatusID
        ELSE d.EstatusID
    END AS EstatusID,

    COALESCE(pp.Observaciones, d.MensajeCapacidad) AS Observaciones,
    d.FechaCreacion
FROM dbo.Planeacion_ReleaseDetalle d
INNER JOIN dbo.Planeacion_Releases r
    ON r.ReleaseID = d.ReleaseID
LEFT JOIN dbo.ERP_Clientes c
    ON c.ClienteID = r.ClienteID
LEFT JOIN dbo.Planeacion_ProgramaProduccion pp
    ON pp.ProgramaProduccionID = d.ProgramaProduccionID
   AND pp.Activo = 1
LEFT JOIN dbo.ERP_ParteDatosTecnicos t
    ON t.ParteID = d.ParteID
   AND t.Activo = 1
WHERE d.Activo = 1
  AND r.Activo = 1
  AND r.EstatusID NOT IN (9, 99)
  AND d.FechaRequerida >= @FechaDesde
  AND d.FechaRequerida <= @FechaHasta
  AND (@ClienteID IS NULL OR r.ClienteID = @ClienteID)
  AND (@MaquinaID IS NULL OR COALESCE(pp.MaquinaID, d.MaquinaSugeridaID) = @MaquinaID)
  AND (@ParteID IS NULL OR d.ParteID = @ParteID)
  AND (@EstatusID IS NULL OR COALESCE(pp.EstatusID, d.EstatusID) = @EstatusID)
ORDER BY
    ISNULL(c.Nombre, r.ClienteNombre),
    d.FechaRequerida,
    d.ReferenciaSAP,
    d.NumeroParte,
    pp.FechaInicioProgramada;";

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            await using var cmd = new SqlCommand(sql, cn);

            cmd.Parameters.Add("@FechaDesde", SqlDbType.Date).Value = fechaDesde.Date;
            cmd.Parameters.Add("@FechaHasta", SqlDbType.Date).Value = fechaHasta.Date;

            cmd.Parameters.Add("@ClienteID", SqlDbType.Int).Value =
                (object?)clienteId ?? DBNull.Value;

            cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value =
                (object?)maquinaId ?? DBNull.Value;

            cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value =
                (object?)parteId ?? DBNull.Value;

            cmd.Parameters.Add("@EstatusID", SqlDbType.Int).Value =
                (object?)estatusId ?? DBNull.Value;

            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                lista.Add(MapPanorama(rd));
            }

            return lista;
        }

        private static PlaneacionPanoramaRenglonVm MapPanorama(SqlDataReader rd)
        {
            return new PlaneacionPanoramaRenglonVm
            {
                ProgramaProduccionID = rd["ProgramaProduccionID"] == DBNull.Value
                    ? null
                    : Convert.ToInt32(rd["ProgramaProduccionID"]),

                ReleaseID = rd["ReleaseID"] == DBNull.Value ? null : Convert.ToInt32(rd["ReleaseID"]),
                ReleaseDetalleID = rd["ReleaseDetalleID"] == DBNull.Value ? null : Convert.ToInt32(rd["ReleaseDetalleID"]),
                FolioRelease = rd["FolioRelease"] as string,

                FechaCarga = rd["FechaCarga"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaCarga"]),
                FechaRequerida = rd["FechaRequerida"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaRequerida"]),

                SolicitudProduccionID = rd["SolicitudProduccionID"] == DBNull.Value ? null : Convert.ToInt32(rd["SolicitudProduccionID"]),
                SolicitudProduccionDetalleID = rd["SolicitudProduccionDetalleID"] == DBNull.Value ? null : Convert.ToInt32(rd["SolicitudProduccionDetalleID"]),

                ClienteID = rd["ClienteID"] == DBNull.Value ? null : Convert.ToInt32(rd["ClienteID"]),
                ClienteNombre = rd["ClienteNombre"] as string,

                ParteID = rd["ParteID"] == DBNull.Value ? null : Convert.ToInt32(rd["ParteID"]),
                NumeroParte = rd["NumeroParte"] as string,
                ReferenciaSAP = rd["ReferenciaSAP"] as string,
                DesignacionDescripcionSAP = rd["DesignacionDescripcionSAP"] as string,

                CantidadRequerida = rd["CantidadRequerida"] == DBNull.Value ? 0 : Convert.ToInt32(rd["CantidadRequerida"]),
                PiezasDesdePT = rd["PiezasDesdePT"] == DBNull.Value ? 0 : Convert.ToInt32(rd["PiezasDesdePT"]),
                CantidadProgramada = rd["CantidadProgramada"] == DBNull.Value ? 0 : Convert.ToInt32(rd["CantidadProgramada"]),
                CantidadProducida = rd["CantidadProducida"] == DBNull.Value ? 0 : Convert.ToInt32(rd["CantidadProducida"]),
                CantidadPendiente = rd["CantidadPendiente"] == DBNull.Value ? 0 : Convert.ToInt32(rd["CantidadPendiente"]),

                MaquinaID = rd["MaquinaID"] == DBNull.Value ? null : Convert.ToInt32(rd["MaquinaID"]),
                MaquinaCodigo = rd["MaquinaCodigo"] as string,
                MaquinaNombre = rd["MaquinaNombre"] as string,

                MoldeID = rd["MoldeID"] == DBNull.Value ? null : Convert.ToInt32(rd["MoldeID"]),
                MoldeCodigo = rd["MoldeCodigo"] as string,

                Color = rd["Color"] as string,

                Cavidades = rd["Cavidades"] == DBNull.Value ? null : Convert.ToInt32(rd["Cavidades"]),
                Ciclo = rd["Ciclo"] as string,
                ObjetivoHora = rd["ObjetivoHora"] == DBNull.Value ? null : Convert.ToInt32(rd["ObjetivoHora"]),
                PesoBrutoPieza = rd["PesoBrutoPieza"] == DBNull.Value ? null : Convert.ToDecimal(rd["PesoBrutoPieza"]),

                MaterialID = rd["MaterialID"] == DBNull.Value ? null : Convert.ToInt32(rd["MaterialID"]),
                MaterialCodigo = rd["MaterialCodigo"] as string,
                MaterialDescripcion = rd["MaterialDescripcion"] as string,
                CantidadMpKg = rd["CantidadMpKg"] == DBNull.Value ? null : Convert.ToDecimal(rd["CantidadMpKg"]),

                EmbalajeCodigo = rd["EmbalajeCodigo"] as string,
                EmbalajeDescripcion = rd["EmbalajeDescripcion"] as string,
                PiezasPorEmbalaje = rd["PiezasPorEmbalaje"] == DBNull.Value ? null : Convert.ToDecimal(rd["PiezasPorEmbalaje"]),
                CantidadEmbalajes = rd["CantidadEmbalajes"] == DBNull.Value ? null : Convert.ToDecimal(rd["CantidadEmbalajes"]),

                CondicionProduccion = rd["CondicionProduccion"] as string,
                SecuenciaMaquina = rd["SecuenciaMaquina"] == DBNull.Value ? null : Convert.ToInt32(rd["SecuenciaMaquina"]),

                FechaInicioProgramada = rd["FechaInicioProgramada"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaInicioProgramada"]),
                FechaFinProgramada = rd["FechaFinProgramada"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaFinProgramada"]),
                HorasProgramadas = rd["HorasProgramadas"] == DBNull.Value ? null : Convert.ToDecimal(rd["HorasProgramadas"]),

                Cambio = rd["Cambio"] == DBNull.Value ? null : (TimeSpan)rd["Cambio"],
                Arranque = rd["Arranque"] == DBNull.Value ? null : (TimeSpan)rd["Arranque"],

                EstatusID = rd["EstatusID"] == DBNull.Value ? 0 : Convert.ToInt32(rd["EstatusID"]),
                Observaciones = rd["Observaciones"] as string,
                FechaCreacion = rd["FechaCreacion"] == DBNull.Value ? DateTime.MinValue : Convert.ToDateTime(rd["FechaCreacion"])
            };
        }

        private async Task<List<SelectListItem>> CargarClientesAsync(int? seleccionado)
        {
            var lista = new List<SelectListItem>
            {
                new SelectListItem
                {
                    Value = "",
                    Text = "Todos los clientes",
                    Selected = !seleccionado.HasValue
                }
            };

            const string sql = @"
SELECT ClienteID, Nombre
FROM dbo.ERP_Clientes
WHERE Activo = 1
ORDER BY Nombre;";

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            await using var cmd = new SqlCommand(sql, cn);
            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                var id = Convert.ToInt32(rd["ClienteID"]);

                lista.Add(new SelectListItem
                {
                    Value = id.ToString(),
                    Text = rd["Nombre"]?.ToString() ?? "",
                    Selected = seleccionado.HasValue && seleccionado.Value == id
                });
            }

            return lista;
        }

        private async Task<List<SelectListItem>> CargarMaquinasAsync(int? seleccionado)
        {
            var lista = new List<SelectListItem>
            {
                new SelectListItem
                {
                    Value = "",
                    Text = "Todas las máquinas",
                    Selected = !seleccionado.HasValue
                }
            };

            const string sql = @"
SELECT MaquinaID, Codigo, Nombre
FROM dbo.ERP_Maquinas
WHERE Activo = 1
ORDER BY Codigo;";

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            await using var cmd = new SqlCommand(sql, cn);
            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                var id = Convert.ToInt32(rd["MaquinaID"]);
                var codigo = rd["Codigo"]?.ToString() ?? "";
                var nombre = rd["Nombre"]?.ToString() ?? "";

                lista.Add(new SelectListItem
                {
                    Value = id.ToString(),
                    Text = $"{codigo} | {nombre}",
                    Selected = seleccionado.HasValue && seleccionado.Value == id
                });
            }

            return lista;
        }

        private async Task<List<SelectListItem>> CargarPartesAsync(int? seleccionado)
        {
            var lista = new List<SelectListItem>
            {
                new SelectListItem
                {
                    Value = "",
                    Text = "Todas las partes",
                    Selected = !seleccionado.HasValue
                }
            };

            const string sql = @"
SELECT
    ParteID,
    ISNULL(NULLIF(ReferenciaSAP, ''), NumeroParte) AS Referencia,
    ISNULL(NULLIF(Designacion, ''), ISNULL(Descripcion, '')) AS Descripcion
FROM dbo.ERP_Partes
WHERE Activo = 1
ORDER BY ISNULL(NULLIF(ReferenciaSAP, ''), NumeroParte);";

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            await using var cmd = new SqlCommand(sql, cn);
            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                var id = Convert.ToInt32(rd["ParteID"]);
                var referencia = rd["Referencia"]?.ToString() ?? "";
                var descripcion = rd["Descripcion"]?.ToString() ?? "";

                lista.Add(new SelectListItem
                {
                    Value = id.ToString(),
                    Text = string.IsNullOrWhiteSpace(descripcion)
                        ? referencia
                        : $"{referencia} | {descripcion}",
                    Selected = seleccionado.HasValue && seleccionado.Value == id
                });
            }

            return lista;
        }

        private static List<SelectListItem> CargarEstatus(int? seleccionado)
        {
            return new List<SelectListItem>
            {
                new SelectListItem
                {
                    Value = "",
                    Text = "Todos los estatus",
                    Selected = !seleccionado.HasValue
                },
                new SelectListItem
                {
                    Value = PlaneacionProgramaEstatus.Programado.ToString(),
                    Text = PlaneacionProgramaEstatus.Nombre(PlaneacionProgramaEstatus.Programado),
                    Selected = seleccionado == PlaneacionProgramaEstatus.Programado
                },
                new SelectListItem
                {
                    Value = PlaneacionProgramaEstatus.EnPreparacion.ToString(),
                    Text = PlaneacionProgramaEstatus.Nombre(PlaneacionProgramaEstatus.EnPreparacion),
                    Selected = seleccionado == PlaneacionProgramaEstatus.EnPreparacion
                },
                new SelectListItem
                {
                    Value = PlaneacionProgramaEstatus.EnProduccion.ToString(),
                    Text = PlaneacionProgramaEstatus.Nombre(PlaneacionProgramaEstatus.EnProduccion),
                    Selected = seleccionado == PlaneacionProgramaEstatus.EnProduccion
                },
                new SelectListItem
                {
                    Value = PlaneacionProgramaEstatus.Pausado.ToString(),
                    Text = PlaneacionProgramaEstatus.Nombre(PlaneacionProgramaEstatus.Pausado),
                    Selected = seleccionado == PlaneacionProgramaEstatus.Pausado
                },
                new SelectListItem
                {
                    Value = PlaneacionProgramaEstatus.Terminado.ToString(),
                    Text = PlaneacionProgramaEstatus.Nombre(PlaneacionProgramaEstatus.Terminado),
                    Selected = seleccionado == PlaneacionProgramaEstatus.Terminado
                },
                new SelectListItem
                {
                    Value = PlaneacionProgramaEstatus.Cerrado.ToString(),
                    Text = PlaneacionProgramaEstatus.Nombre(PlaneacionProgramaEstatus.Cerrado),
                    Selected = seleccionado == PlaneacionProgramaEstatus.Cerrado
                }
            };
        }
    }
}