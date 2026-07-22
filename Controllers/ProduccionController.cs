using ERP.NSQuell.Models.ViewModels.Produccion;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;

namespace ERP.NSQuell.Controllers
{
    // PRODUCCION_MVP_DIA1
    public sealed class ProduccionController : Controller
    {
        private readonly IConfiguration _configuration;

        public ProduccionController(
            IConfiguration configuration)
        {
            _configuration = configuration;
        }

        private string ConnectionString =>
            _configuration.GetConnectionString(
                "DefaultConnection")
            ?? throw new InvalidOperationException(
                "No se encontró la cadena DefaultConnection.");

        [HttpGet]
        public async Task<IActionResult> Index(
            string? q,
            int? estatus,
            CancellationToken cancellationToken)
        {
            if (!HttpContext.Session
                .GetInt32("UsuarioID")
                .HasValue)
            {
                return RedirectToAction(
                    "Login",
                    "Login");
            }

            var vm =
                new ProduccionIndexVm
                {
                    Busqueda = q?.Trim(),
                    EstatusID = estatus
                };

            const string sql = @"
WITH Progreso AS
(
    SELECT
        pp.SolicitudProduccionID,
        pp.SolicitudProduccionDetalleID,
        pp.MaquinaID,
        SUM(CONVERT(BIGINT, ISNULL(pp.CantidadProducida, 0))) AS CantidadProducida,
        MIN(pp.FechaInicioReal) AS FechaInicioReal,
        MAX(pp.FechaFinReal) AS FechaFinReal,
        SUM(CONVERT(DECIMAL(18,4), ISNULL(pp.HorasReales, 0))) AS HorasReales
    FROM dbo.Planeacion_ProgramaProduccion pp
    WHERE pp.Activo = 1
      AND pp.SolicitudProduccionID IS NOT NULL
      AND pp.SolicitudProduccionDetalleID IS NOT NULL
    GROUP BY
        pp.SolicitudProduccionID,
        pp.SolicitudProduccionDetalleID,
        pp.MaquinaID
)
SELECT
    COALESCE(a.AsignacionMaquinaID, d.SolicitudProduccionDetalleID) AS ProgramaProduccionID,
    s.SolicitudProduccionID,
    d.SolicitudProduccionDetalleID,
    COALESCE
    (
        NULLIF(LTRIM(RTRIM(s.NumeroOFRecibida)), N''),
        NULLIF(LTRIM(RTRIM(s.FolioSolicitud)), N''),
        CONCAT(N'OF-ID-', s.SolicitudProduccionID)
    ) AS NumeroOF,
    s.FolioSolicitud,
    s.NumeroOFRecibida,
    ISNULL(NULLIF(LTRIM(RTRIM(c.Nombre)), N''), s.ClienteNombre) AS ClienteNombre,
    p.NumeroParte,
    d.ReferenciaSAP,
    d.DesignacionDescripcionSAP,
    d.CantidadPiezas AS CantidadRequerida,
    CONVERT(INT, 0) AS PiezasDesdePT,
    COALESCE(NULLIF(a.CantidadAsignada, 0), d.CantidadPiezas) AS CantidadProgramada,
    CONVERT(INT, ISNULL(pr.CantidadProducida, 0)) AS CantidadProducida,
    CASE
        WHEN COALESCE(NULLIF(a.CantidadAsignada, 0), d.CantidadPiezas)
             - CONVERT(INT, ISNULL(pr.CantidadProducida, 0)) < 0
            THEN 0
        ELSE COALESCE(NULLIF(a.CantidadAsignada, 0), d.CantidadPiezas)
             - CONVERT(INT, ISNULL(pr.CantidadProducida, 0))
    END AS CantidadPendiente,
    a.MaquinaID,
    maq.Codigo AS MaquinaCodigo,
    maq.Nombre AS MaquinaNombre,
    COALESCE(a.MoldeID, d.MoldeID) AS MoldeID,
    mol.CodigoMolde AS MoldeCodigo,
    a.CondicionProduccion,
    a.Secuencia AS SecuenciaMaquina,
    COALESCE
    (
        DATEADD
        (
            SECOND,
            DATEDIFF(SECOND, CAST('00:00:00' AS TIME), a.HoraInicioTentativa),
            CAST(a.FechaProgramadaTentativa AS DATETIME)
        ),
        s.FechaInicioPlaneada
    ) AS FechaInicioProgramada,
    COALESCE
    (
        DATEADD
        (
            SECOND,
            DATEDIFF(SECOND, CAST('00:00:00' AS TIME), a.HoraFinTentativa),
            CAST(a.FechaProgramadaTentativa AS DATETIME)
        ),
        s.FechaFinPlaneada
    ) AS FechaFinProgramada,
    COALESCE(a.HorasEstimadas, d.HorasPlaneadas) AS HorasProgramadas,
    pr.FechaInicioReal,
    pr.FechaFinReal,
    pr.HorasReales,
    d.ObjetivoHora,
    d.Ciclo,
    d.Cavidades,
    d.MaterialCodigo,
    d.MaterialDescripcion,
    d.CantidadMpKg,
    d.EmbalajeCodigo,
    d.EmbalajeDescripcion,
    d.CantidadEmbalajes,
    s.EstatusID,
    COALESCE(a.Observaciones, d.Notas, s.NotasGenerales) AS Observaciones,
    s.FechaCreacion AS FechaGeneracionOF
FROM dbo.SolicitudesProduccion s
LEFT JOIN dbo.ERP_Clientes c
    ON c.ClienteID = s.ClienteID
INNER JOIN dbo.SolicitudesProduccionDetalle d
    ON d.SolicitudProduccionID = s.SolicitudProduccionID
   AND d.Activo = 1
LEFT JOIN dbo.ERP_Partes p
    ON p.ParteID = d.ParteID
LEFT JOIN dbo.SolicitudesProduccionAsignacionMaquina a
    ON a.SolicitudProduccionDetalleID = d.SolicitudProduccionDetalleID
   AND a.Activo = 1
LEFT JOIN dbo.ERP_Maquinas maq
    ON maq.MaquinaID = a.MaquinaID
LEFT JOIN dbo.ERP_Moldes mol
    ON mol.MoldeID = COALESCE(a.MoldeID, d.MoldeID)
LEFT JOIN Progreso pr
    ON pr.SolicitudProduccionID = s.SolicitudProduccionID
   AND pr.SolicitudProduccionDetalleID = d.SolicitudProduccionDetalleID
   AND
   (
       pr.MaquinaID = a.MaquinaID
       OR (pr.MaquinaID IS NULL AND a.MaquinaID IS NULL)
   )
WHERE s.Activo = 1
  AND (@EstatusID IS NULL OR s.EstatusID = @EstatusID)
  AND
  (
      @Q IS NULL
      OR s.FolioSolicitud LIKE N'%' + @Q + N'%'
      OR s.NumeroOFRecibida LIKE N'%' + @Q + N'%'
      OR ISNULL(c.Nombre, s.ClienteNombre) LIKE N'%' + @Q + N'%'
      OR p.NumeroParte LIKE N'%' + @Q + N'%'
      OR d.ReferenciaSAP LIKE N'%' + @Q + N'%'
      OR d.DesignacionDescripcionSAP LIKE N'%' + @Q + N'%'
      OR maq.Codigo LIKE N'%' + @Q + N'%'
      OR maq.Nombre LIKE N'%' + @Q + N'%'
      OR mol.CodigoMolde LIKE N'%' + @Q + N'%'
  )
ORDER BY
    s.FechaCreacion DESC,
    s.SolicitudProduccionID DESC,
    d.Renglon,
    ISNULL(a.Secuencia, 999999);";

            await using var connection =
                new SqlConnection(
                    ConnectionString);

            await connection.OpenAsync(
                cancellationToken);

            await using var command =
                new SqlCommand(
                    sql,
                    connection);

            command.Parameters.Add(
                "@EstatusID",
                SqlDbType.Int).Value =
                estatus.HasValue
                    ? estatus.Value
                    : DBNull.Value;

            command.Parameters.Add(
                "@Q",
                SqlDbType.NVarChar,
                200).Value =
                string.IsNullOrWhiteSpace(q)
                    ? DBNull.Value
                    : q.Trim();

            await using var reader =
                await command.ExecuteReaderAsync(
                    cancellationToken);

            while (await reader.ReadAsync(
                cancellationToken))
            {
                var cantidadProgramada =
                    Entero(
                        reader,
                        "CantidadProgramada");

                var cantidadProducida =
                    Entero(
                        reader,
                        "CantidadProducida");

                var cantidadPendiente =
                    NullableEntero(
                        reader,
                        "CantidadPendiente")
                    ?? Math.Max(
                        cantidadProgramada
                        - cantidadProducida,
                        0);

                var item =
                    new ProduccionProgramaVm
                    {
                        ProgramaProduccionID =
                            Entero(
                                reader,
                                "ProgramaProduccionID"),

                        SolicitudProduccionID =
                            NullableEntero(
                                reader,
                                "SolicitudProduccionID"),

                        SolicitudProduccionDetalleID =
                            NullableEntero(
                                reader,
                                "SolicitudProduccionDetalleID"),

                                                NumeroOF =
                            Texto(
                                reader,
                                "NumeroOF"),

                        FolioSolicitud =
                            Texto(
                                reader,
                                "FolioSolicitud"),

                        NumeroOFRecibida =
                            Texto(
                                reader,
                                "NumeroOFRecibida"),

                        Cliente =
                            Texto(
                                reader,
                                "ClienteNombre"),

                        NumeroParte =
                            Texto(
                                reader,
                                "NumeroParte"),

                        ReferenciaSAP =
                            Texto(
                                reader,
                                "ReferenciaSAP"),

                        Designacion =
                            Texto(
                                reader,
                                "DesignacionDescripcionSAP"),

                        CantidadRequerida =
                            Entero(
                                reader,
                                "CantidadRequerida"),

                        PiezasDesdePT =
                            Entero(
                                reader,
                                "PiezasDesdePT"),

                        CantidadProgramada =
                            cantidadProgramada,

                        CantidadProducida =
                            cantidadProducida,

                        CantidadPendiente =
                            cantidadPendiente,

                        MaquinaID =
                            NullableEntero(
                                reader,
                                "MaquinaID"),

                        MaquinaCodigo =
                            Texto(
                                reader,
                                "MaquinaCodigo"),

                        MaquinaNombre =
                            Texto(
                                reader,
                                "MaquinaNombre"),

                        MoldeID =
                            NullableEntero(
                                reader,
                                "MoldeID"),

                        MoldeCodigo =
                            Texto(
                                reader,
                                "MoldeCodigo"),

                        CondicionProduccion =
                            Texto(
                                reader,
                                "CondicionProduccion"),

                        SecuenciaMaquina =
                            NullableEntero(
                                reader,
                                "SecuenciaMaquina"),

                        FechaInicioProgramada =
                            NullableFecha(
                                reader,
                                "FechaInicioProgramada"),

                        FechaFinProgramada =
                            NullableFecha(
                                reader,
                                "FechaFinProgramada"),

                        HorasProgramadas =
                            NullableDecimal(
                                reader,
                                "HorasProgramadas"),

                        FechaInicioReal =
                            NullableFecha(
                                reader,
                                "FechaInicioReal"),

                        FechaFinReal =
                            NullableFecha(
                                reader,
                                "FechaFinReal"),

                        HorasReales =
                            NullableDecimal(
                                reader,
                                "HorasReales"),

                        ObjetivoHora =
                            NullableEntero(
                                reader,
                                "ObjetivoHora"),

                        Ciclo =
                            Texto(
                                reader,
                                "Ciclo"),

                        Cavidades =
                            NullableEntero(
                                reader,
                                "Cavidades"),

                        MaterialCodigo =
                            Texto(
                                reader,
                                "MaterialCodigo"),

                        MaterialDescripcion =
                            Texto(
                                reader,
                                "MaterialDescripcion"),

                        CantidadMpKg =
                            NullableDecimal(
                                reader,
                                "CantidadMpKg"),

                        EmbalajeCodigo =
                            Texto(
                                reader,
                                "EmbalajeCodigo"),

                        EmbalajeDescripcion =
                            Texto(
                                reader,
                                "EmbalajeDescripcion"),

                        CantidadEmbalajes =
                            NullableDecimal(
                                reader,
                                "CantidadEmbalajes"),

                        EstatusID =
                            Entero(
                                reader,
                                "EstatusID"),

                        Observaciones =
                            Texto(
                                reader,
                                "Observaciones"),

                        FechaGeneracionOF =
                            NullableFecha(
                                reader,
                                "FechaGeneracionOF")
                    };

                vm.Programas.Add(item);
            }

            vm.TotalProgramas = vm.Programas
                .Where(x => x.SolicitudProduccionID.HasValue)
                .Select(x => x.SolicitudProduccionID!.Value)
                .Distinct()
                .Count();

            vm.TotalProgramado =
                vm.Programas.Sum(
                    x => x.CantidadProgramada);

            vm.TotalProducido =
                vm.Programas.Sum(
                    x => x.CantidadProducida);

            vm.TotalPendiente =
                vm.Programas.Sum(
                    x => x.CantidadPendiente);

            vm.ConProgramacion =
                vm.Programas.Count(
                    x =>
                        x.FechaInicioProgramada
                            .HasValue
                        && x.MaquinaID.HasValue);

            vm.Incompletos =
                vm.Programas.Count(
                    x =>
                        !x.MaquinaID.HasValue
                        || x.CantidadProgramada <= 0
                        || !x.SolicitudProduccionID
                            .HasValue);

            return View(vm);
        }

        private static int Entero(
            SqlDataReader reader,
            string columna)
        {
            var ordinal =
                reader.GetOrdinal(
                    columna);

            return reader.IsDBNull(
                    ordinal)
                ? 0
                : Convert.ToInt32(
                    reader.GetValue(
                        ordinal));
        }

        private static int? NullableEntero(
            SqlDataReader reader,
            string columna)
        {
            var ordinal =
                reader.GetOrdinal(
                    columna);

            return reader.IsDBNull(
                    ordinal)
                ? null
                : Convert.ToInt32(
                    reader.GetValue(
                        ordinal));
        }

        private static decimal? NullableDecimal(
            SqlDataReader reader,
            string columna)
        {
            var ordinal =
                reader.GetOrdinal(
                    columna);

            return reader.IsDBNull(
                    ordinal)
                ? null
                : Convert.ToDecimal(
                    reader.GetValue(
                        ordinal));
        }

        private static DateTime? NullableFecha(
            SqlDataReader reader,
            string columna)
        {
            var ordinal =
                reader.GetOrdinal(
                    columna);

            return reader.IsDBNull(
                    ordinal)
                ? null
                : Convert.ToDateTime(
                    reader.GetValue(
                        ordinal));
        }

        private static string Texto(
            SqlDataReader reader,
            string columna)
        {
            var ordinal =
                reader.GetOrdinal(
                    columna);

            return reader.IsDBNull(
                    ordinal)
                ? string.Empty
                : reader.GetValue(
                        ordinal)
                    ?.ToString()
                    ?.Trim()
                    ?? string.Empty;
        }
    }
}

