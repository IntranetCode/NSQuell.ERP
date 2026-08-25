using ERP.NSQuell.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using System.Data;

namespace ERP.NSQuell.Controllers;

[Route("ProduccionIndicadoresParos")]
public sealed class ProduccionIndicadoresParosController : Controller
{
    private readonly IConfiguration _configuration;

    public ProduccionIndicadoresParosController(
        IConfiguration configuration)
    {
        _configuration = configuration;
    }

    private string ConnectionString =>
        _configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException(
            "No se encontró la cadena de conexión DefaultConnection.");

    private bool UsuarioEnSesion() =>
        HttpContext.Session.GetInt32("UsuarioID").HasValue;

    [HttpGet("")]
    [HttpGet("Index")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Index(
        DateTime? fecha,
        string? periodo = "diario",
        int? maquinaId = null,
        int? operadorId = null,
        string? motivo = null,
        string? of = null)
    {
        if (!UsuarioEnSesion())
            return RedirectToAction("Login", "Login");

        var fechaBase = (fecha ?? DateTime.Today).Date;
        var periodoNormalizado = NormalizarPeriodo(periodo);

        var (desde, hastaExclusiva) =
            ResolverRango(fechaBase, periodoNormalizado);

        motivo =
            string.IsNullOrWhiteSpace(motivo)
                ? null
                : motivo.Trim();

        of =
            string.IsNullOrWhiteSpace(of)
                ? null
                : of.Trim();

        await using var cn =
            new SqlConnection(ConnectionString);

        await cn.OpenAsync();

        var vm = new ProduccionIndicadoresParosIndexVm
        {
            Fecha = fechaBase,
            Periodo = periodoNormalizado,
            FechaDesde = desde,
            FechaHastaExclusiva = hastaExclusiva,
            MaquinaID = maquinaId,
            OperadorID = operadorId,
            Motivo = motivo,
            OF = of,
            FechaActualizacion = DateTime.Now
        };

        vm.Maquinas =
            await CargarMaquinasAsync(
                maquinaId,
                cn);

        vm.Operadores =
            await CargarOperadoresAsync(
                operadorId,
                cn);

        vm.Motivos =
            await CargarMotivosAsync(
                motivo,
                cn);

        vm.Paros =
            await ObtenerParosAsync(
                desde,
                hastaExclusiva,
                maquinaId,
                operadorId,
                motivo,
                of,
                cn);

        return View(vm);
    }

    private static string NormalizarPeriodo(string? periodo)
    {
        var value =
            (periodo ?? "diario")
                .Trim()
                .ToLowerInvariant();

        return value switch
        {
            "semanal" => "semanal",
            "mensual" => "mensual",
            _ => "diario"
        };
    }

    private static (DateTime Desde, DateTime HastaExclusiva)
        ResolverRango(
            DateTime fecha,
            string periodo)
    {
        if (periodo == "mensual")
        {
            var desde =
                new DateTime(
                    fecha.Year,
                    fecha.Month,
                    1);

            return (
                desde,
                desde.AddMonths(1));
        }

        if (periodo == "semanal")
        {
            var diasDesdeLunes =
                ((int)fecha.DayOfWeek + 6) % 7;

            var desde =
                fecha.AddDays(-diasDesdeLunes);

            return (
                desde,
                desde.AddDays(7));
        }

        return (
            fecha.Date,
            fecha.Date.AddDays(1));
    }

    private static async Task<List<ProduccionIndicadorParoFilaVm>>
        ObtenerParosAsync(
            DateTime desde,
            DateTime hastaExclusiva,
            int? maquinaId,
            int? operadorId,
            string? motivo,
            string? of,
            SqlConnection cn)
    {
        var lista =
            new List<ProduccionIndicadorParoFilaVm>();

        const string sql = @"
SELECT
    p.ParoID,
    p.EjecucionProduccionID,
    p.ProgramaProduccionID,

    COALESCE
    (
        p.SolicitudProduccionID,
        e.SolicitudProduccionID,
        pp.SolicitudProduccionID
    ) AS SolicitudProduccionID,

    COALESCE
    (
        p.MaquinaID,
        e.MaquinaID,
        pp.MaquinaID
    ) AS MaquinaID,

    COALESCE
    (
        NULLIF(LTRIM(RTRIM(m.Codigo)), N''),
        NULLIF(LTRIM(RTRIM(e.MaquinaCodigo)), N''),
        NULLIF(LTRIM(RTRIM(pp.MaquinaCodigo)), N''),
        N''
    ) AS MaquinaCodigo,

    COALESCE
    (
        NULLIF(LTRIM(RTRIM(m.Nombre)), N''),
        NULLIF(LTRIM(RTRIM(e.MaquinaNombre)), N''),
        NULLIF(LTRIM(RTRIM(pp.MaquinaNombre)), N''),
        N''
    ) AS MaquinaNombre,

    COALESCE
    (
        p.OperadorID,
        e.OperadorID
    ) AS OperadorID,

    COALESCE
    (
        NULLIF
        (
            LTRIM
            (
                RTRIM
                (
                    CONCAT
                    (
                        ISNULL(per.Nombre, N''),
                        N' ',
                        ISNULL(per.ApellidoPaterno, N''),
                        N' ',
                        ISNULL(per.ApellidoMaterno, N'')
                    )
                )
            ),
            N''
        ),
        NULLIF(LTRIM(RTRIM(e.OperadorNombre)), N''),
        N''
    ) AS OperadorNombre,

    COALESCE
    (
        NULLIF(LTRIM(RTRIM(s.NumeroOFRecibida)), N''),
        NULLIF(LTRIM(RTRIM(s.FolioSolicitud)), N''),
        CASE
            WHEN s.SolicitudProduccionID IS NOT NULL
                THEN N'OF #' +
                     CONVERT
                     (
                         NVARCHAR(30),
                         s.SolicitudProduccionID
                     )
            ELSE N''
        END
    ) AS FolioOF,

    p.FechaInicioParo,
    p.FechaFinParo,

    CASE
        WHEN p.FechaFinParo IS NULL
        THEN
            CASE
                WHEN p.FechaInicioParo >= GETDATE()
                    THEN 0
                ELSE DATEDIFF
                     (
                         MINUTE,
                         p.FechaInicioParo,
                         GETDATE()
                     )
            END
        ELSE
            ISNULL
            (
                p.DuracionMinutos,
                CASE
                    WHEN p.FechaFinParo < p.FechaInicioParo
                        THEN 0
                    ELSE DATEDIFF
                         (
                             MINUTE,
                             p.FechaInicioParo,
                             p.FechaFinParo
                         )
                END
            )
    END AS DuracionMinutos,

    CAST
    (
        CASE
            WHEN p.FechaFinParo IS NULL
                 AND
                 DATEDIFF
                 (
                     MINUTE,
                     p.FechaInicioParo,
                     GETDATE()
                 ) > 15
                THEN 1

            WHEN ISNULL(p.EsMayorA15Minutos, 0) = 1
                THEN 1

            WHEN p.FechaFinParo IS NOT NULL
                 AND
                 ISNULL
                 (
                     p.DuracionMinutos,
                     DATEDIFF
                     (
                         MINUTE,
                         p.FechaInicioParo,
                         p.FechaFinParo
                     )
                 ) > 15
                THEN 1

            ELSE 0
        END
        AS BIT
    ) AS EsMayorA15Minutos,

    CAST
    (
        CASE
            WHEN p.FechaFinParo IS NULL
                THEN 1
            ELSE 0
        END
        AS BIT
    ) AS EnCurso,

    ISNULL(p.MotivoParoTexto, N'')
        AS MotivoParoTexto,

    ISNULL(p.Descripcion, N'')
        AS Descripcion

FROM dbo.Produccion_Paros p

LEFT JOIN dbo.Produccion_Ejecucion e
    ON e.EjecucionProduccionID =
       p.EjecucionProduccionID

LEFT JOIN dbo.Planeacion_ProgramaProduccion pp
    ON pp.ProgramaProduccionID =
       p.ProgramaProduccionID

LEFT JOIN dbo.SolicitudesProduccion s
    ON s.SolicitudProduccionID =
       COALESCE
       (
           p.SolicitudProduccionID,
           e.SolicitudProduccionID,
           pp.SolicitudProduccionID
       )

LEFT JOIN dbo.ERP_Maquinas m
    ON m.MaquinaID =
       COALESCE
       (
           p.MaquinaID,
           e.MaquinaID,
           pp.MaquinaID
       )

LEFT JOIN dbo.Persona per
    ON per.PersonaID =
       COALESCE
       (
           p.OperadorID,
           e.OperadorID
       )

WHERE p.Activo = 1

  AND p.FechaInicioParo >= @Desde
  AND p.FechaInicioParo < @HastaExclusiva

  AND
  (
      @MaquinaID IS NULL
      OR
      COALESCE
      (
          p.MaquinaID,
          e.MaquinaID,
          pp.MaquinaID
      ) = @MaquinaID
  )

  AND
  (
      @OperadorID IS NULL
      OR
      COALESCE
      (
          p.OperadorID,
          e.OperadorID
      ) = @OperadorID
  )

  AND
  (
      @Motivo IS NULL
      OR
      LTRIM
      (
          RTRIM
          (
              ISNULL
              (
                  p.MotivoParoTexto,
                  N''
              )
          )
      ) = @Motivo
  )

  AND
  (
      @OF IS NULL

      OR
      ISNULL(s.FolioSolicitud, N'')
          LIKE N'%' + @OF + N'%'

      OR
      ISNULL(s.NumeroOFRecibida, N'')
          LIKE N'%' + @OF + N'%'

      OR
      CONVERT
      (
          NVARCHAR(30),
          s.SolicitudProduccionID
      ) LIKE N'%' + @OF + N'%'
  )

ORDER BY
    CASE
        WHEN p.FechaFinParo IS NULL
            THEN 0
        ELSE 1
    END,
    p.FechaInicioParo DESC,
    p.ParoID DESC;";

        await using var cmd =
            new SqlCommand(
                sql,
                cn);

        cmd.Parameters.Add(
            "@Desde",
            SqlDbType.DateTime2).Value =
            desde;

        cmd.Parameters.Add(
            "@HastaExclusiva",
            SqlDbType.DateTime2).Value =
            hastaExclusiva;

        cmd.Parameters.Add(
            "@MaquinaID",
            SqlDbType.Int).Value =
            (object?)maquinaId
            ?? DBNull.Value;

        cmd.Parameters.Add(
            "@OperadorID",
            SqlDbType.Int).Value =
            (object?)operadorId
            ?? DBNull.Value;

        cmd.Parameters.Add(
            "@Motivo",
            SqlDbType.NVarChar,
            200).Value =
            string.IsNullOrWhiteSpace(motivo)
                ? DBNull.Value
                : motivo.Trim();

        cmd.Parameters.Add(
            "@OF",
            SqlDbType.NVarChar,
            100).Value =
            string.IsNullOrWhiteSpace(of)
                ? DBNull.Value
                : of.Trim();

        await using var rd =
            await cmd.ExecuteReaderAsync();

        while (await rd.ReadAsync())
        {
            lista.Add(
                new ProduccionIndicadorParoFilaVm
                {
                    ParoID =
                        Convert.ToInt32(
                            rd["ParoID"]),

                    EjecucionProduccionID =
                        Convert.ToInt32(
                            rd["EjecucionProduccionID"]),

                    ProgramaProduccionID =
                        Convert.ToInt32(
                            rd["ProgramaProduccionID"]),

                    SolicitudProduccionID =
                        rd["SolicitudProduccionID"] ==
                        DBNull.Value
                            ? null
                            : Convert.ToInt32(
                                rd["SolicitudProduccionID"]),

                    MaquinaID =
                        rd["MaquinaID"] ==
                        DBNull.Value
                            ? null
                            : Convert.ToInt32(
                                rd["MaquinaID"]),

                    MaquinaCodigo =
                        rd["MaquinaCodigo"]?
                            .ToString()?
                            .Trim()
                        ?? string.Empty,

                    MaquinaNombre =
                        rd["MaquinaNombre"]?
                            .ToString()?
                            .Trim()
                        ?? string.Empty,

                    OperadorID =
                        rd["OperadorID"] ==
                        DBNull.Value
                            ? null
                            : Convert.ToInt32(
                                rd["OperadorID"]),

                    OperadorNombre =
                        rd["OperadorNombre"]?
                            .ToString()?
                            .Trim()
                        ?? string.Empty,

                    FolioOF =
                        rd["FolioOF"]?
                            .ToString()?
                            .Trim()
                        ?? string.Empty,

                    FechaInicioParo =
                        Convert.ToDateTime(
                            rd["FechaInicioParo"]),

                    FechaFinParo =
                        rd["FechaFinParo"] ==
                        DBNull.Value
                            ? null
                            : Convert.ToDateTime(
                                rd["FechaFinParo"]),

                    DuracionMinutos =
                        rd["DuracionMinutos"] ==
                        DBNull.Value
                            ? 0
                            : Math.Max(
                                0,
                                Convert.ToInt32(
                                    rd["DuracionMinutos"])),

                    EsMayorA15Minutos =
                        rd["EsMayorA15Minutos"] !=
                        DBNull.Value
                        &&
                        Convert.ToBoolean(
                            rd["EsMayorA15Minutos"]),

                    EnCurso =
                        rd["EnCurso"] !=
                        DBNull.Value
                        &&
                        Convert.ToBoolean(
                            rd["EnCurso"]),

                    MotivoParoTexto =
                        rd["MotivoParoTexto"]?
                            .ToString()?
                            .Trim()
                        ?? string.Empty,

                    Descripcion =
                        rd["Descripcion"]?
                            .ToString()?
                            .Trim()
                        ?? string.Empty
                });
        }

        return lista;
    }

    private static async Task<List<SelectListItem>>
        CargarMaquinasAsync(
            int? selectedId,
            SqlConnection cn)
    {
        var lista =
            new List<SelectListItem>
            {
                new()
                {
                    Value = "",
                    Text = "Todas las máquinas",
                    Selected = !selectedId.HasValue
                }
            };

        const string sql = @"
SELECT DISTINCT
    m.MaquinaID,
    ISNULL(NULLIF(LTRIM(RTRIM(m.Codigo)), N''), N'S/C') AS Codigo,
    ISNULL(NULLIF(LTRIM(RTRIM(m.Nombre)), N''), N'') AS Nombre
FROM dbo.Produccion_Paros p
INNER JOIN dbo.ERP_Maquinas m
    ON m.MaquinaID = p.MaquinaID
WHERE p.Activo = 1
ORDER BY Codigo, Nombre;";

        await using var cmd =
            new SqlCommand(
                sql,
                cn);

        await using var rd =
            await cmd.ExecuteReaderAsync();

        while (await rd.ReadAsync())
        {
            var id =
                Convert.ToInt32(
                    rd["MaquinaID"]);

            var codigo =
                rd["Codigo"]?
                    .ToString()?
                    .Trim()
                ?? string.Empty;

            var nombre =
                rd["Nombre"]?
                    .ToString()?
                    .Trim()
                ?? string.Empty;

            lista.Add(
                new SelectListItem
                {
                    Value = id.ToString(),
                    Text =
                        string.IsNullOrWhiteSpace(nombre)
                            ? codigo
                            : $"{codigo} · {nombre}",
                    Selected =
                        selectedId == id
                });
        }

        return lista;
    }

    private static async Task<List<SelectListItem>>
        CargarOperadoresAsync(
            int? selectedId,
            SqlConnection cn)
    {
        var lista =
            new List<SelectListItem>
            {
                new()
                {
                    Value = "",
                    Text = "Todos los operadores",
                    Selected = !selectedId.HasValue
                }
            };

        const string sql = @"
SELECT DISTINCT
    p.OperadorID,
    LTRIM
    (
        RTRIM
        (
            CONCAT
            (
                ISNULL(per.Nombre, N''),
                N' ',
                ISNULL(per.ApellidoPaterno, N''),
                N' ',
                ISNULL(per.ApellidoMaterno, N'')
            )
        )
    ) AS OperadorNombre
FROM dbo.Produccion_Paros p
INNER JOIN dbo.Persona per
    ON per.PersonaID = p.OperadorID
WHERE p.Activo = 1
  AND p.OperadorID IS NOT NULL
ORDER BY OperadorNombre;";

        await using var cmd =
            new SqlCommand(
                sql,
                cn);

        await using var rd =
            await cmd.ExecuteReaderAsync();

        while (await rd.ReadAsync())
        {
            var id =
                Convert.ToInt32(
                    rd["OperadorID"]);

            var nombre =
                rd["OperadorNombre"]?
                    .ToString()?
                    .Trim()
                ?? $"Persona #{id}";

            lista.Add(
                new SelectListItem
                {
                    Value = id.ToString(),
                    Text = nombre,
                    Selected =
                        selectedId == id
                });
        }

        return lista;
    }

    private static async Task<List<SelectListItem>>
        CargarMotivosAsync(
            string? selected,
            SqlConnection cn)
    {
        var lista =
            new List<SelectListItem>
            {
                new()
                {
                    Value = "",
                    Text = "Todos los motivos",
                    Selected =
                        string.IsNullOrWhiteSpace(
                            selected)
                }
            };

        const string sql = @"
SELECT DISTINCT
    LTRIM
    (
        RTRIM
        (
            MotivoParoTexto
        )
    ) AS Motivo
FROM dbo.Produccion_Paros
WHERE Activo = 1
  AND NULLIF
      (
          LTRIM
          (
              RTRIM
              (
                  ISNULL
                  (
                      MotivoParoTexto,
                      N''
                  )
              )
          ),
          N''
      ) IS NOT NULL
ORDER BY Motivo;";

        await using var cmd =
            new SqlCommand(
                sql,
                cn);

        await using var rd =
            await cmd.ExecuteReaderAsync();

        while (await rd.ReadAsync())
        {
            var motivo =
                rd["Motivo"]?
                    .ToString()?
                    .Trim();

            if (string.IsNullOrWhiteSpace(motivo))
                continue;

            lista.Add(
                new SelectListItem
                {
                    Value = motivo,
                    Text = motivo,
                    Selected =
                        string.Equals(
                            selected,
                            motivo,
                            StringComparison.OrdinalIgnoreCase)
                });
        }

        return lista;
    }
}
