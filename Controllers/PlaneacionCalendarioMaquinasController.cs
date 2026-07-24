using ERP.NSQuell.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;

namespace ERP.NSQuell.Controllers;

public sealed class PlaneacionCalendarioMaquinasController : Controller
{
    private readonly IConfiguration _configuration;

    public PlaneacionCalendarioMaquinasController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    private string ConnectionString =>
        _configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException(
            "No se encontro la cadena DefaultConnection.");

    [HttpGet]
    public async Task<IActionResult> Index(DateTime? semana)
    {
        var fechaBase = (semana ?? DateTime.Today).Date;
        var diasDesdeLunes = ((int)fechaBase.DayOfWeek + 6) % 7;
        var inicioSemana = fechaBase.AddDays(-diasDesdeLunes);
        var finSemana = inicioSemana.AddDays(7);

        var vm = new PlaneacionCalendarioMaquinasVm
        {
            InicioSemana = inicioSemana,
            FinSemana = finSemana,
            Ahora = DateTime.Now
        };

        const string sql = @"
SELECT
    m.MaquinaID,
    m.Codigo AS MaquinaCodigo,
    m.Nombre AS MaquinaNombre,

    pp.ProgramaProduccionID,
    pp.SolicitudProduccionID,
    pp.ClienteNombre,
    pp.NumeroParte,
    pp.ReferenciaSAP,
    pp.DesignacionDescripcionSAP,
    pp.MoldeCodigo,
    ISNULL(pp.CantidadProgramada, 0) AS CantidadProgramada,
    ISNULL(pp.CantidadProducida, 0) AS CantidadProducida,
    pp.FechaInicioProgramada,
    ISNULL(
        pp.FechaFinProgramada,
        DATEADD(
            MINUTE,
            CONVERT(INT, ISNULL(pp.HorasProgramadas, 1) * 60),
            pp.FechaInicioProgramada
        )
    ) AS FechaFinProgramada,
    ISNULL(pp.HorasProgramadas, 0) AS HorasProgramadas,
    pp.Cambio,
    pp.Arranque,
    ISNULL(pp.EstatusID, 1) AS EstatusID
FROM dbo.ERP_Maquinas m
LEFT JOIN dbo.Planeacion_ProgramaProduccion pp
    ON pp.MaquinaID = m.MaquinaID
   AND pp.Activo = 1
   AND ISNULL(pp.EstatusID, 1) <> 99
   AND pp.FechaInicioProgramada IS NOT NULL
   AND pp.FechaInicioProgramada < @FinSemana
   AND ISNULL(
        pp.FechaFinProgramada,
        DATEADD(
            MINUTE,
            CONVERT(INT, ISNULL(pp.HorasProgramadas, 1) * 60),
            pp.FechaInicioProgramada
        )
   ) > @InicioSemana
WHERE m.Activo = 1
ORDER BY
    m.Codigo,
    pp.FechaInicioProgramada,
    pp.ProgramaProduccionID;";

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync();

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add("@InicioSemana", SqlDbType.DateTime).Value = inicioSemana;
        cmd.Parameters.Add("@FinSemana", SqlDbType.DateTime).Value = finSemana;

        await using var rd = await cmd.ExecuteReaderAsync();

        var maquinas = new Dictionary<int, PlaneacionCalendarioMaquinaVm>();

        while (await rd.ReadAsync())
        {
            var maquinaId = Convert.ToInt32(rd["MaquinaID"]);

            if (!maquinas.TryGetValue(maquinaId, out var maquina))
            {
                maquina = new PlaneacionCalendarioMaquinaVm
                {
                    MaquinaID = maquinaId,
                    Codigo = rd["MaquinaCodigo"] as string ?? maquinaId.ToString(),
                    Nombre = rd["MaquinaNombre"] as string ?? string.Empty
                };

                maquinas.Add(maquinaId, maquina);
            }

            if (rd["ProgramaProduccionID"] == DBNull.Value)
                continue;

            var inicio = Convert.ToDateTime(rd["FechaInicioProgramada"]);
            var fin = Convert.ToDateTime(rd["FechaFinProgramada"]);

            if (fin <= inicio)
                fin = inicio.AddHours(1);

            maquina.Bloques.Add(new PlaneacionCalendarioBloqueVm
            {
                ProgramaProduccionID =
                    Convert.ToInt32(rd["ProgramaProduccionID"]),
                SolicitudProduccionID =
                    rd["SolicitudProduccionID"] == DBNull.Value
                        ? null
                        : Convert.ToInt32(rd["SolicitudProduccionID"]),
                MaquinaID = maquinaId,
                MaquinaCodigo = maquina.Codigo,
                ClienteNombre = rd["ClienteNombre"] as string,
                NumeroParte = rd["NumeroParte"] as string,
                ReferenciaSAP = rd["ReferenciaSAP"] as string,
                Descripcion = rd["DesignacionDescripcionSAP"] as string,
                MoldeCodigo = rd["MoldeCodigo"] as string,
                CantidadProgramada =
                    Convert.ToInt32(rd["CantidadProgramada"]),
                CantidadProducida =
                    Convert.ToInt32(rd["CantidadProducida"]),
                Inicio = inicio,
                Fin = fin,
                HorasProgramadas =
                    Convert.ToDecimal(rd["HorasProgramadas"]),
                Cambio =
                    rd["Cambio"] == DBNull.Value
                        ? null
                        : (TimeSpan)rd["Cambio"],
                Arranque =
                    rd["Arranque"] == DBNull.Value
                        ? null
                        : (TimeSpan)rd["Arranque"],
                EstatusID =
                    Convert.ToInt32(rd["EstatusID"])
            });
        }

        foreach (var maquina in maquinas.Values)
        {
            AsignarCarriles(maquina);
        }

        vm.Maquinas = maquinas.Values
            .OrderBy(x => x.Codigo)
            .ToList();

        return View(vm);
    }

    private static void AsignarCarriles(
        PlaneacionCalendarioMaquinaVm maquina)
    {
        var finales = new List<DateTime>();

        foreach (var bloque in maquina.Bloques
            .OrderBy(x => x.Inicio)
            .ThenBy(x => x.Fin)
            .ThenBy(x => x.ProgramaProduccionID))
        {
            var carril = -1;

            for (var i = 0; i < finales.Count; i++)
            {
                if (finales[i] <= bloque.Inicio)
                {
                    carril = i;
                    break;
                }
            }

            if (carril < 0)
            {
                carril = finales.Count;
                finales.Add(bloque.Fin);
            }
            else
            {
                finales[carril] = bloque.Fin;
            }

            bloque.Carril = carril;
        }

        maquina.Carriles = Math.Max(1, finales.Count);
    }
}