using ERP.NSQuell.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using System.Data;

namespace ERP.NSQuell.Controllers
{
    public class PlaneacionProgramaController : Controller
    {
        private readonly IConfiguration _configuration;

        public PlaneacionProgramaController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        private string ConnectionString =>
            _configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("No se encontró la cadena de conexión DefaultConnection.");


        [HttpGet]
        public async Task<IActionResult> Index(
    int? clienteId,
    int? parteId,
    DateTime? fechaDesde,
    DateTime? fechaHasta,
    bool soloListos = false,
    bool soloPendienteAbasto = false,
    bool soloPendienteDatosTecnicos = false)
        {
            var vm = new PlaneacionProgramaNecesidadFiltroVm
            {
                ClienteID = clienteId,
                ParteID = parteId,
                FechaDesde = fechaDesde,
                FechaHasta = fechaHasta,
                SoloPendientes = soloListos,
                SoloSinMP = soloPendienteAbasto,
                SoloSinCapacidad = soloPendienteDatosTecnicos
            };

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            // Reserva automática de PT al abrir Programa de Planeación.
            // Esto evita que varios releases usen el mismo stock disponible.
            await SincronizarApartadosPTAsync(cn);

            vm.Clientes = await CargarSelectAsync(
                cn,
                "SELECT ClienteID AS Id, Nombre AS Texto FROM dbo.ERP_Clientes WHERE Activo = 1 ORDER BY Nombre;"
            );

            vm.Partes = await CargarSelectAsync(
                cn,
                @"SELECT
              ParteID AS Id,
              ISNULL(NULLIF(NumeroParte, ''), ISNULL(NULLIF(ReferenciaSAP, ''), CONVERT(NVARCHAR(30), ParteID)))
              + ' | ' +
              ISNULL(NULLIF(ReferenciaSAP, ''), ISNULL(NULLIF(NumeroParte, ''), 'Sin referencia'))
              + ' | ' +
              ISNULL(NULLIF(Designacion, ''), ISNULL(NULLIF(Descripcion, ''), 'Sin descripción')) AS Texto
          FROM dbo.ERP_Partes
          WHERE Activo = 1
          ORDER BY NumeroParte, ReferenciaSAP;"
            );

            const string sql = @"
SELECT
    r.ReleaseID,
    r.FolioRelease,
    r.ClienteID,
    ISNULL(c.Nombre, r.ClienteNombre) AS ClienteNombre,
    r.FechaRecepcion,

    d.ReleaseDetalleID,
    d.Renglon,
    d.ParteID,
    d.NumeroParte,
    d.ReferenciaSAP,
    d.DesignacionDescripcionSAP,
    d.FechaCarga,
    d.FechaRequerida,
    d.CantidadRequerida,

    d.ProgramaProduccionID,
    d.SolicitudProduccionID,
    d.EstatusID,

    t.MaterialID,
    t.MaterialCodigo,
    t.MaterialDescripcion,
    t.PesoBrutoPieza,
    t.PesoNetoPieza,

    t.EmbalajeCodigo,
    t.EmbalajeDescripcion,
    t.PiezasPorEmbalaje,
    t.PiezasPorCaja,

    t.MoldePrincipalID AS MoldeID,
    mol.CodigoMolde AS MoldeCodigo,

    t.MaquinaPrincipalID AS MaquinaSugeridaID,
    maq.Codigo AS MaquinaSugeridaCodigo,
    maq.Nombre AS MaquinaSugeridaNombre,

    t.MaquinaSustitutaID AS MaquinaSustitutaID,
    maq2.Codigo AS MaquinaSustitutaCodigo,
    maq2.Nombre AS MaquinaSustitutaNombre,

    t.Ciclo,
    t.Cavidades,
    t.ObjetivoHora,
    t.Color,
    t.TipoSecado,
    t.HorasSecado,
    t.HorasSecadoTexto,

    ISNULL(pt.Disponible, 0) AS PTDisponible,
    ISNULL(mp.Saldo, 0) AS MPDisponible,
    ISNULL(emb.Saldo, 0) AS EmbalajeDisponible,

    ISNULL(prog.ProgramadoPendiente, 0) AS ProgramadoPendiente,
    ISNULL(aptPropio.CantidadApartada, 0) AS PTApartadoPropio,
    ISNULL(aptOtros.CantidadApartada, 0) AS PTApartadoOtros

FROM dbo.Planeacion_ReleaseDetalle d
INNER JOIN dbo.Planeacion_Releases r
    ON r.ReleaseID = d.ReleaseID
LEFT JOIN dbo.ERP_Clientes c
    ON c.ClienteID = r.ClienteID

LEFT JOIN dbo.ERP_ParteDatosTecnicos t
    ON t.ParteID = d.ParteID
   AND t.Activo = 1

LEFT JOIN dbo.ERP_Moldes mol
    ON mol.MoldeID = t.MoldePrincipalID

LEFT JOIN dbo.ERP_Maquinas maq
    ON maq.MaquinaID = t.MaquinaPrincipalID

LEFT JOIN dbo.ERP_Maquinas maq2
    ON maq2.MaquinaID = t.MaquinaSustitutaID

OUTER APPLY
(
    SELECT TOP 1 ISNULL(Disponible, 0) AS Disponible
    FROM dbo.vw_AlmacenPTInventario
    WHERE ParteID = d.ParteID
) pt

OUTER APPLY
(
    SELECT TOP 1 ISNULL(Saldo, 0) AS Saldo
    FROM dbo.vw_AlmacenMPInventario
    WHERE MaterialID = t.MaterialID
) mp

OUTER APPLY
(
    SELECT TOP 1 ISNULL(Saldo, 0) AS Saldo
    FROM dbo.vw_AlmacenEmbalajesInventario
    WHERE Codigo = t.EmbalajeCodigo
) emb

-- IMPORTANTE:
-- Solo descuenta producción ya programada para EL MISMO ReleaseDetalleID.
-- No descuenta por ParteID global, porque eso podía dejar A Producir en 0 incorrectamente.
OUTER APPLY
(
    SELECT ISNULL(SUM(ISNULL(pp.CantidadProgramada, 0) - ISNULL(pp.CantidadProducida, 0)), 0) AS ProgramadoPendiente
    FROM dbo.Planeacion_ProgramaProduccion pp
    WHERE pp.ReleaseDetalleID = d.ReleaseDetalleID
      AND pp.Activo = 1
      AND ISNULL(pp.EstatusID, 1) NOT IN (5, 9, 99)
) prog

OUTER APPLY
(
    SELECT ISNULL(SUM(a.CantidadApartada), 0) AS CantidadApartada
    FROM dbo.Planeacion_PT_Apartado a
    WHERE a.ReleaseDetalleID = d.ReleaseDetalleID
      AND a.Activo = 1
      AND a.EstatusID = 1
) aptPropio

OUTER APPLY
(
    SELECT ISNULL(SUM(a.CantidadApartada), 0) AS CantidadApartada
    FROM dbo.Planeacion_PT_Apartado a
    WHERE a.ParteID = d.ParteID
      AND a.ReleaseDetalleID <> d.ReleaseDetalleID
      AND a.Activo = 1
      AND a.EstatusID = 1
) aptOtros

WHERE r.Activo = 1
  AND d.Activo = 1
  AND (@ClienteID IS NULL OR r.ClienteID = @ClienteID)
  AND (@ParteID IS NULL OR d.ParteID = @ParteID)
  AND (@FechaDesde IS NULL OR d.FechaRequerida >= @FechaDesde)
  AND (@FechaHasta IS NULL OR d.FechaRequerida <= @FechaHasta)

ORDER BY
    ISNULL(c.Nombre, r.ClienteNombre),
    d.FechaRequerida,
    d.NumeroParte,
    d.Renglon;";

            await using var cmd = new SqlCommand(sql, cn);

            cmd.Parameters.Add("@ClienteID", SqlDbType.Int).Value =
                (object?)clienteId ?? DBNull.Value;

            cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value =
                (object?)parteId ?? DBNull.Value;

            cmd.Parameters.Add("@FechaDesde", SqlDbType.Date).Value =
                (object?)fechaDesde?.Date ?? DBNull.Value;

            cmd.Parameters.Add("@FechaHasta", SqlDbType.Date).Value =
                (object?)fechaHasta?.Date ?? DBNull.Value;

            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                var cantidadRequerida = rd["CantidadRequerida"] == DBNull.Value ? 0 : Convert.ToInt32(rd["CantidadRequerida"]);
                var stockDisponible = rd["PTDisponible"] == DBNull.Value ? 0 : Convert.ToInt32(rd["PTDisponible"]);
                var ptApartadoPropio = rd["PTApartadoPropio"] == DBNull.Value ? 0 : Convert.ToInt32(rd["PTApartadoPropio"]);
                var ptApartadoOtros = rd["PTApartadoOtros"] == DBNull.Value ? 0 : Convert.ToInt32(rd["PTApartadoOtros"]);
                var programadoPendiente = rd["ProgramadoPendiente"] == DBNull.Value ? 0 : Convert.ToInt32(rd["ProgramadoPendiente"]);

                var ptDisponibleNeto = Math.Max(0, stockDisponible - ptApartadoOtros);

                // El PT usado por este release ya viene apartado por FIFO.
                // Si por alguna razón aún no hay apartado, se calcula contra el neto disponible.
                var piezasDesdeStock = ptApartadoPropio > 0
                    ? Math.Min(ptApartadoPropio, cantidadRequerida)
                    : Math.Min(ptDisponibleNeto, cantidadRequerida);

                var piezasAProducir = Math.Max(0, cantidadRequerida - piezasDesdeStock - programadoPendiente);

                var pesoBrutoPieza = rd["PesoBrutoPieza"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(rd["PesoBrutoPieza"]);
                var piezasPorEmbalaje = rd["PiezasPorEmbalaje"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(rd["PiezasPorEmbalaje"]);
                var objetivoHora = rd["ObjetivoHora"] == DBNull.Value ? (int?)null : Convert.ToInt32(rd["ObjetivoHora"]);

                decimal mpRequeridaKg = 0;
                if (piezasAProducir > 0 && pesoBrutoPieza.HasValue && pesoBrutoPieza.Value > 0)
                    mpRequeridaKg = Math.Round((piezasAProducir * pesoBrutoPieza.Value) / 1000m, 4); // PesoBrutoPieza está en gramos; resultado en kg.

                decimal embalajeRequerido = 0;
                if (piezasAProducir > 0 && piezasPorEmbalaje.HasValue && piezasPorEmbalaje.Value > 0)
                    embalajeRequerido = Math.Ceiling(piezasAProducir / piezasPorEmbalaje.Value);

                decimal horasProgramadas = 0;
                if (piezasAProducir > 0 && objetivoHora.HasValue && objetivoHora.Value > 0)
                    horasProgramadas = Math.Ceiling(piezasAProducir / (decimal)objetivoHora.Value);

                int? qtyPorDia = null;
                if (objetivoHora.HasValue && objetivoHora.Value > 0)
                    qtyPorDia = objetivoHora.Value * 24; // Capacidad teórica diaria. Si trabajan por turnos, cambia 24 por horas operativas.

                var fechaRequerida = rd["FechaRequerida"] == DBNull.Value
                    ? DateTime.Today
                    : Convert.ToDateTime(rd["FechaRequerida"]);

                var fechaInicioSugerida = DateTime.Now;

                DateTime? fechaFinEstimada = null;
                if (horasProgramadas > 0)
                    fechaFinEstimada = fechaInicioSugerida.AddHours((double)horasProgramadas);

                bool? daTiempo = null;
                if (piezasAProducir <= 0)
                    daTiempo = true;
                else if (fechaFinEstimada.HasValue)
                    daTiempo = fechaFinEstimada.Value.Date <= fechaRequerida.Date;

                var mpDisponible = rd["MPDisponible"] == DBNull.Value ? 0 : Convert.ToDecimal(rd["MPDisponible"]);
                var embalajeDisponible = rd["EmbalajeDisponible"] == DBNull.Value ? 0 : Convert.ToDecimal(rd["EmbalajeDisponible"]);

                var materialId = rd["MaterialID"] == DBNull.Value ? (int?)null : Convert.ToInt32(rd["MaterialID"]);
                var maquinaId = rd["MaquinaSugeridaID"] == DBNull.Value ? (int?)null : Convert.ToInt32(rd["MaquinaSugeridaID"]);
                var moldeId = rd["MoldeID"] == DBNull.Value ? (int?)null : Convert.ToInt32(rd["MoldeID"]);
                var cavidades = rd["Cavidades"] == DBNull.Value ? (int?)null : Convert.ToInt32(rd["Cavidades"]);

                var faltaMaterial = !materialId.HasValue;
                var faltaMaquina = !maquinaId.HasValue;
                var faltaMolde = !moldeId.HasValue;
                var faltaCavidades = !cavidades.HasValue || cavidades.Value <= 0;
                var faltaCiclo = rd["Ciclo"] == DBNull.Value || string.IsNullOrWhiteSpace(rd["Ciclo"].ToString());
                var faltaObjetivo = !objetivoHora.HasValue || objetivoHora.Value <= 0;
                var faltaPeso = !pesoBrutoPieza.HasValue || pesoBrutoPieza.Value <= 0;
                var faltaEmbalaje = piezasAProducir > 0 && (!piezasPorEmbalaje.HasValue || piezasPorEmbalaje.Value <= 0);

                string mensaje;
                if (piezasAProducir <= 0)
                    mensaje = "Cubierto con stock y/o producción ya programada.";
                else if (faltaMaterial)
                    mensaje = "Falta material o resina en datos técnicos.";
                else if (mpDisponible < mpRequeridaKg)
                    mensaje = "Falta materia prima.";
                else if (embalajeRequerido > 0 && embalajeDisponible < embalajeRequerido)
                    mensaje = "Falta embalaje.";
                else if (faltaMolde)
                    mensaje = "Falta molde en datos técnicos.";
                else if (faltaMaquina)
                    mensaje = "Falta máquina asignada en datos técnicos.";
                else if (faltaCavidades)
                    mensaje = "Faltan cavidades en datos técnicos.";
                else if (faltaCiclo)
                    mensaje = "Falta ciclo en datos técnicos.";
                else if (faltaObjetivo)
                    mensaje = "Falta objetivo por hora en datos técnicos.";
                else if (faltaPeso)
                    mensaje = "Falta peso bruto de pieza en datos técnicos.";
                else if (faltaEmbalaje)
                    mensaje = "Faltan piezas por embalaje en datos técnicos.";
                else if (daTiempo == false)
                    mensaje = "No da tiempo contra la fecha requerida.";
                else
                    mensaje = "Listo para enviar a Programa Cambio de Molde.";

                var necesidad = new PlaneacionProgramaNecesidadVm
                {
                    ReleaseID = Convert.ToInt32(rd["ReleaseID"]),
                    ReleaseDetalleID = Convert.ToInt32(rd["ReleaseDetalleID"]),
                    FolioRelease = rd["FolioRelease"] as string,

                    ClienteID = rd["ClienteID"] == DBNull.Value ? null : Convert.ToInt32(rd["ClienteID"]),
                    ClienteNombre = rd["ClienteNombre"] as string,

                    FechaRecepcion = rd["FechaRecepcion"] == DBNull.Value
                        ? DateTime.Today
                        : Convert.ToDateTime(rd["FechaRecepcion"]),

                    Renglon = rd["Renglon"] == DBNull.Value ? 0 : Convert.ToInt32(rd["Renglon"]),

                    ParteID = rd["ParteID"] == DBNull.Value ? null : Convert.ToInt32(rd["ParteID"]),
                    NumeroParte = rd["NumeroParte"] as string,
                    ReferenciaSAP = rd["ReferenciaSAP"] as string,
                    DesignacionDescripcionSAP = rd["DesignacionDescripcionSAP"] as string,

                    FechaRequerida = fechaRequerida,
                    CantidadRequerida = cantidadRequerida,

                    PTDisponibleAlCalcular = stockDisponible,
                    PTApartadoOtros = ptApartadoOtros,
                    PTDisponibleNeto = ptDisponibleNeto,
                    ProduccionProgramadaPendiente = programadoPendiente,
                    PiezasDesdePT = piezasDesdeStock,
                    PiezasAProducir = piezasAProducir,

                    MaterialID = materialId,
                    MaterialCodigo = rd["MaterialCodigo"] as string,
                    MaterialDescripcion = rd["MaterialDescripcion"] as string,
                    MPRequeridaKg = mpRequeridaKg,
                    MPDisponibleKg = mpDisponible,

                    EmbalajeCodigo = rd["EmbalajeCodigo"] as string,
                    EmbalajeDescripcion = rd["EmbalajeDescripcion"] as string,
                    EmbalajeRequerido = embalajeRequerido,
                    EmbalajeDisponible = embalajeDisponible,

                    MaquinaSugeridaID = maquinaId,
                    MaquinaSugeridaCodigo = rd["MaquinaSugeridaCodigo"] as string,
                    MaquinaSugeridaNombre = rd["MaquinaSugeridaNombre"] as string,

                    MoldeID = moldeId,
                    MoldeCodigo = rd["MoldeCodigo"] as string,

                    ObjetivoHora = objetivoHora,
                    HorasNecesarias = horasProgramadas,
                    FechaInicioSugerida = fechaInicioSugerida,
                    FechaFinEstimada = fechaFinEstimada,
                    DaTiempo = daTiempo,
                    MensajeCapacidad = mensaje,

                    ProgramaProduccionID = rd["ProgramaProduccionID"] == DBNull.Value ? null : Convert.ToInt32(rd["ProgramaProduccionID"]),
                    SolicitudProduccionID = rd["SolicitudProduccionID"] == DBNull.Value ? null : Convert.ToInt32(rd["SolicitudProduccionID"]),
                    EstatusID = rd["EstatusID"] == DBNull.Value ? 0 : Convert.ToInt32(rd["EstatusID"]),

                    Ciclo = rd["Ciclo"] == DBNull.Value ? null : rd["Ciclo"].ToString(),
                    Cavidades = cavidades,
                    PesoBrutoPieza = pesoBrutoPieza,
                    PiezasPorCaja = rd["PiezasPorCaja"] == DBNull.Value ? null : Convert.ToInt32(rd["PiezasPorCaja"]),
                    QtyPorDia = qtyPorDia,

                    MaquinaSustitutaID = rd["MaquinaSustitutaID"] == DBNull.Value ? null : Convert.ToInt32(rd["MaquinaSustitutaID"]),
                    MaquinaSustitutaCodigo = rd["MaquinaSustitutaCodigo"] as string,
                    MaquinaSustitutaNombre = rd["MaquinaSustitutaNombre"] as string,

                    Color = rd["Color"] as string,
                    TipoSecado = rd["TipoSecado"] as string,
                    HorasSecado = rd["HorasSecado"] == DBNull.Value ? null : Convert.ToDecimal(rd["HorasSecado"]),
                    HorasSecadoTexto = rd["HorasSecadoTexto"] as string
                };

                if (soloListos && !(
                        !necesidad.ProgramaProduccionID.HasValue &&
                        (necesidad.PiezasAProducir ?? 0) > 0 &&
                        !faltaMaterial &&
                        !faltaMaquina &&
                        !faltaMolde &&
                        !faltaCavidades &&
                        !faltaCiclo &&
                        !faltaObjetivo &&
                        !faltaPeso &&
                        !faltaEmbalaje &&
                        (necesidad.MPDisponibleKg ?? 0) >= (necesidad.MPRequeridaKg ?? 0) &&
                        (necesidad.EmbalajeDisponible ?? 0) >= (necesidad.EmbalajeRequerido ?? 0)
                    ))
                    continue;

                if (soloPendienteAbasto &&
                    !((necesidad.PiezasAProducir ?? 0) > 0 &&
                      ((necesidad.MPDisponibleKg ?? 0) < (necesidad.MPRequeridaKg ?? 0) ||
                       (necesidad.EmbalajeDisponible ?? 0) < (necesidad.EmbalajeRequerido ?? 0))))
                    continue;

                if (soloPendienteDatosTecnicos &&
                    !((necesidad.PiezasAProducir ?? 0) > 0 &&
                      (faltaMaterial || faltaMaquina || faltaMolde || faltaCavidades || faltaCiclo || faltaObjetivo || faltaPeso || faltaEmbalaje)))
                    continue;

                vm.Necesidades.Add(necesidad);
            }

            return View(vm);
        }


        private async Task SincronizarApartadosPTAsync(SqlConnection cn)
        {
            const string sql = @"
IF OBJECT_ID('dbo.Planeacion_PT_Apartado', 'U') IS NULL
    RETURN;

DECLARE @Reservas TABLE
(
    ReleaseID INT NULL,
    ReleaseDetalleID INT NOT NULL PRIMARY KEY,
    ClienteID INT NULL,
    ParteID INT NOT NULL,
    CantidadRequerida INT NOT NULL,
    StockFisico INT NOT NULL,
    RequeridoAnterior INT NOT NULL,
    CantidadApartadaNueva INT NOT NULL
);

;WITH Base AS
(
    SELECT
        r.ReleaseID,
        d.ReleaseDetalleID,
        r.ClienteID,
        d.ParteID,
        ISNULL(d.CantidadRequerida, 0) AS CantidadRequerida,
        ISNULL(pt.Disponible, 0) AS StockFisico,
        d.FechaRequerida
    FROM dbo.Planeacion_ReleaseDetalle d
    INNER JOIN dbo.Planeacion_Releases r
        ON r.ReleaseID = d.ReleaseID
    OUTER APPLY
    (
        SELECT TOP 1 ISNULL(Disponible, 0) AS Disponible
        FROM dbo.vw_AlmacenPTInventario
        WHERE ParteID = d.ParteID
    ) pt
    WHERE r.Activo = 1
      AND d.Activo = 1
      AND d.ParteID IS NOT NULL
      AND ISNULL(d.CantidadRequerida, 0) > 0
      AND ISNULL(d.EstatusID, 1) NOT IN (9, 99)
), Calc AS
(
    SELECT
        ReleaseID,
        ReleaseDetalleID,
        ClienteID,
        ParteID,
        CantidadRequerida,
        StockFisico,
        ISNULL
        (
            SUM(CantidadRequerida) OVER
            (
                PARTITION BY ParteID
                ORDER BY FechaRequerida, ReleaseDetalleID
                ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING
            ),
            0
        ) AS RequeridoAnterior
    FROM Base
)
INSERT INTO @Reservas
(
    ReleaseID,
    ReleaseDetalleID,
    ClienteID,
    ParteID,
    CantidadRequerida,
    StockFisico,
    RequeridoAnterior,
    CantidadApartadaNueva
)
SELECT
    ReleaseID,
    ReleaseDetalleID,
    ClienteID,
    ParteID,
    CantidadRequerida,
    StockFisico,
    RequeridoAnterior,
    CASE
        WHEN StockFisico - RequeridoAnterior <= 0 THEN 0
        WHEN StockFisico - RequeridoAnterior >= CantidadRequerida THEN CantidadRequerida
        ELSE StockFisico - RequeridoAnterior
    END AS CantidadApartadaNueva
FROM Calc;

MERGE dbo.Planeacion_PT_Apartado AS tgt
USING
(
    SELECT *
    FROM @Reservas
    WHERE CantidadApartadaNueva > 0
) AS src
ON tgt.ReleaseDetalleID = src.ReleaseDetalleID
   AND tgt.Activo = 1
   AND tgt.EstatusID = 1
WHEN MATCHED THEN
    UPDATE SET
        tgt.ReleaseID = src.ReleaseID,
        tgt.ClienteID = src.ClienteID,
        tgt.ParteID = src.ParteID,
        tgt.CantidadApartada = src.CantidadApartadaNueva,
        tgt.FechaModificacion = GETDATE(),
        tgt.Observaciones = 'Apartado recalculado automáticamente desde Programa de Planeación.'
WHEN NOT MATCHED BY TARGET THEN
    INSERT
    (
        ReleaseID,
        ReleaseDetalleID,
        ClienteID,
        ParteID,
        CantidadApartada,
        FechaApartado,
        EstatusID,
        Activo,
        Observaciones
    )
    VALUES
    (
        src.ReleaseID,
        src.ReleaseDetalleID,
        src.ClienteID,
        src.ParteID,
        src.CantidadApartadaNueva,
        GETDATE(),
        1,
        1,
        'Apartado creado automáticamente desde Programa de Planeación.'
    );

UPDATE a
SET
    a.Activo = 0,
    a.EstatusID = 9,
    a.FechaModificacion = GETDATE(),
    a.Observaciones = 'Apartado liberado automáticamente: ya no hay PT disponible o el release dejó de aplicar.'
FROM dbo.Planeacion_PT_Apartado a
LEFT JOIN @Reservas r
    ON r.ReleaseDetalleID = a.ReleaseDetalleID
WHERE a.Activo = 1
  AND a.EstatusID = 1
  AND
  (
        r.ReleaseDetalleID IS NULL
     OR r.CantidadApartadaNueva <= 0
  );";

            await using var cmd = new SqlCommand(sql, cn);
            cmd.CommandTimeout = 120;
            await cmd.ExecuteNonQueryAsync();
        }

        // CALENDARIO_MAQUINAS_V1_0
        [HttpGet]
        public async Task<IActionResult> CalendarioMaquinas(DateTime? semana)
        {
            var fechaBase = (semana ?? DateTime.Today).Date;
            var diasDesdeLunes = ((int)fechaBase.DayOfWeek + 6) % 7;
            var inicioSemana = fechaBase.AddDays(-diasDesdeLunes).AddHours(7);
            var finSemana = inicioSemana.AddDays(7);

            var programasConsulta = await ObtenerProgramasPorRangoAsync(
                inicioSemana.Date,
                finSemana.Date);

            var programas = programasConsulta
                .Where(x =>
                    x.FechaInicioProgramada.HasValue &&
                    x.FechaInicioProgramada.Value < finSemana &&
                    (x.FechaFinProgramada ?? x.FechaInicioProgramada.Value.AddHours(1)) >= inicioSemana)
                .ToList();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            var maquinas = await ObtenerMaquinasAsync(cn);

            foreach (var maquina in maquinas)
            {
                maquina.Programas = programas
                    .Where(x => x.MaquinaID == maquina.MaquinaID)
                    .OrderBy(x => x.FechaInicioProgramada)
                    .ThenBy(x => x.SecuenciaMaquina)
                    .ThenBy(x => x.ProgramaProduccionID)
                    .ToList();
            }

            var sinAsignar = programas
                .Where(x => !x.MaquinaID.HasValue)
                .OrderBy(x => x.FechaInicioProgramada)
                .ThenBy(x => x.ProgramaProduccionID)
                .ToList();

            if (sinAsignar.Any())
            {
                maquinas.Insert(0, new PlaneacionProgramaMaquinaVm
                {
                    MaquinaID = null,
                    MaquinaCodigo = "SIN ASIGNAR",
                    MaquinaNombre = "Programas pendientes de máquina",
                    Programas = sinAsignar
                });
            }

            var vm = new PlaneacionProgramaMaquinasVm
            {
                FechaDesde = inicioSemana,
                FechaHasta = finSemana,
                Maquinas = maquinas
            };

            ViewBag.SemanaInicio = inicioSemana;
            ViewBag.SemanaFin = finSemana;

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReprogramarCalendario(
      [FromBody] CalendarioMaquinasMoverRequest request)
        {
            if (request == null ||
                request.ProgramaProduccionID <= 0 ||
                request.MaquinaID <= 0)
            {
                return Json(new
                {
                    ok = false,
                    mensaje = "Los datos recibidos para reprogramar son incompletos."
                });
            }

            var usuarioId = ObtenerUsuarioID();

            if (usuarioId <= 0)
            {
                return Json(new
                {
                    ok = false,
                    mensaje = "No se pudo identificar el usuario de la sesión."
                });
            }

            var inicioSolicitado = DateTime.SpecifyKind(
                request.Inicio,
                DateTimeKind.Unspecified
            );

            inicioSolicitado = new DateTime(
                inicioSolicitado.Year,
                inicioSolicitado.Month,
                inicioSolicitado.Day,
                inicioSolicitado.Hour,
                inicioSolicitado.Minute,
                0
            );

            if (!EsInstanteOperativoCalendario(inicioSolicitado))
            {
                return Json(new
                {
                    ok = false,
                    mensaje = "El inicio seleccionado está dentro del cierre semanal. La operación inicia el lunes a las 07:00 y termina el sábado a las 15:00."
                });
            }

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();

            try
            {
                int? maquinaAnteriorId;
                string? maquinaAnteriorCodigo;
                int? parteId;
                int? moldeId;
                int? solicitudProduccionId;
                int? solicitudProduccionDetalleId;
                int? releaseDetalleId;
                int estatusId;
                DateTime inicioAnterior;
                DateTime finAnterior;
                decimal horasProduccionAnteriores;
                TimeSpan? cambioAnterior;
                TimeSpan? arranqueAnterior;
                int? maquinaPrincipalId;
                int? maquinaSustitutaId;

                const string sqlPrograma = @"
SELECT
    pp.MaquinaID,
    pp.MaquinaCodigo,
    pp.ParteID,
    pp.MoldeID,
    pp.ReleaseDetalleID,
    pp.SolicitudProduccionID,
    pp.SolicitudProduccionDetalleID,
    pp.EstatusID,
    pp.FechaInicioProgramada,
    ISNULL(pp.FechaFinProgramada, DATEADD(HOUR, 1, pp.FechaInicioProgramada)) AS FechaFinProgramada,
    ISNULL(pp.HorasProgramadas, 0) AS HorasProgramadas,
    pp.Cambio,
    pp.Arranque,
    t.MaquinaPrincipalID,
    t.MaquinaSustitutaID
FROM dbo.Planeacion_ProgramaProduccion pp
LEFT JOIN dbo.ERP_ParteDatosTecnicos t
    ON t.ParteID = pp.ParteID
   AND t.Activo = 1
WHERE pp.ProgramaProduccionID = @ProgramaProduccionID
  AND pp.Activo = 1;";

                await using (var cmd = new SqlCommand(sqlPrograma, cn, tx))
                {
                    cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = request.ProgramaProduccionID;

                    await using var rd = await cmd.ExecuteReaderAsync();

                    if (!await rd.ReadAsync())
                    {
                        await tx.RollbackAsync();

                        return Json(new
                        {
                            ok = false,
                            mensaje = "No se encontró el programa de producción."
                        });
                    }

                    maquinaAnteriorId = rd["MaquinaID"] == DBNull.Value
                        ? null
                        : Convert.ToInt32(rd["MaquinaID"]);

                    maquinaAnteriorCodigo = rd["MaquinaCodigo"] as string;

                    parteId = rd["ParteID"] == DBNull.Value
                        ? null
                        : Convert.ToInt32(rd["ParteID"]);

                    moldeId = rd["MoldeID"] == DBNull.Value
                        ? null
                        : Convert.ToInt32(rd["MoldeID"]);

                    releaseDetalleId = rd["ReleaseDetalleID"] == DBNull.Value
                        ? null
                        : Convert.ToInt32(rd["ReleaseDetalleID"]);

                    solicitudProduccionId = rd["SolicitudProduccionID"] == DBNull.Value
                        ? null
                        : Convert.ToInt32(rd["SolicitudProduccionID"]);

                    solicitudProduccionDetalleId = rd["SolicitudProduccionDetalleID"] == DBNull.Value
                        ? null
                        : Convert.ToInt32(rd["SolicitudProduccionDetalleID"]);

                    estatusId = Convert.ToInt32(rd["EstatusID"]);

                    inicioAnterior = Convert.ToDateTime(rd["FechaInicioProgramada"]);
                    finAnterior = Convert.ToDateTime(rd["FechaFinProgramada"]);
                    horasProduccionAnteriores = Convert.ToDecimal(rd["HorasProgramadas"]);

                    cambioAnterior = rd["Cambio"] == DBNull.Value
                        ? null
                        : (TimeSpan)rd["Cambio"];

                    arranqueAnterior = rd["Arranque"] == DBNull.Value
                        ? null
                        : (TimeSpan)rd["Arranque"];

                    maquinaPrincipalId = rd["MaquinaPrincipalID"] == DBNull.Value
                        ? null
                        : Convert.ToInt32(rd["MaquinaPrincipalID"]);

                    maquinaSustitutaId = rd["MaquinaSustitutaID"] == DBNull.Value
                        ? null
                        : Convert.ToInt32(rd["MaquinaSustitutaID"]);
                }

                if (estatusId == PlaneacionProgramaEstatus.EnProduccion ||
                    estatusId == PlaneacionProgramaEstatus.Terminado ||
                    estatusId == PlaneacionProgramaEstatus.Cerrado ||
                    estatusId == 99)
                {
                    await tx.RollbackAsync();

                    return Json(new
                    {
                        ok = false,
                        mensaje = "El programa ya está en producción, terminado o cerrado y no puede moverse desde el calendario."
                    });
                }

                string maquinaNuevaCodigo;
                string maquinaNuevaNombre;

                const string sqlMaquina = @"
SELECT TOP (1)
    Codigo,
    Nombre
FROM dbo.ERP_Maquinas
WHERE MaquinaID = @MaquinaID
  AND Activo = 1;";

                await using (var cmd = new SqlCommand(sqlMaquina, cn, tx))
                {
                    cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = request.MaquinaID;

                    await using var rd = await cmd.ExecuteReaderAsync();

                    if (!await rd.ReadAsync())
                    {
                        await tx.RollbackAsync();

                        return Json(new
                        {
                            ok = false,
                            mensaje = "La máquina seleccionada no existe o está inactiva."
                        });
                    }

                    maquinaNuevaCodigo = rd["Codigo"] as string ?? request.MaquinaID.ToString();
                    maquinaNuevaNombre = rd["Nombre"] as string ?? maquinaNuevaCodigo;
                }

                var maquinaCompatible =
                    !parteId.HasValue ||
                    maquinaPrincipalId == request.MaquinaID ||
                    maquinaSustitutaId == request.MaquinaID;

                if (!maquinaCompatible && !request.ForzarMaquina)
                {
                    await tx.RollbackAsync();

                    return Json(new
                    {
                        ok = false,
                        requiereConfirmacion = true,
                        mensaje = $"La máquina {maquinaNuevaCodigo} no está configurada como principal ni sustituta para esta parte. ¿Deseas conservar el cambio de todas formas?"
                    });
                }

                var horasPreparacion = 0m;

                if (cambioAnterior.HasValue && arranqueAnterior.HasValue)
                {
                    var fechaCambioAnterior = inicioAnterior.Date.Add(cambioAnterior.Value);

                    if (fechaCambioAnterior < inicioAnterior.AddDays(-1) ||
                        fechaCambioAnterior > inicioAnterior.AddDays(1))
                    {
                        fechaCambioAnterior = inicioAnterior;
                    }

                    var fechaArranqueAnterior = fechaCambioAnterior.Date.Add(arranqueAnterior.Value);

                    if (fechaArranqueAnterior <= fechaCambioAnterior)
                        fechaArranqueAnterior = fechaArranqueAnterior.AddDays(1);

                    horasPreparacion = CalcularHorasOperativasCalendario(
                        fechaCambioAnterior,
                        fechaArranqueAnterior
                    );
                }

                if (horasPreparacion < 0)
                    horasPreparacion = 0;

                var duracionBloqueAnterior = CalcularHorasOperativasCalendario(
                    inicioAnterior,
                    finAnterior
                );

                if (duracionBloqueAnterior <= 0)
                    duracionBloqueAnterior = horasProduccionAnteriores + horasPreparacion;

                var duracionBloqueNueva = request.Redimensionado
                    ? request.DuracionBloqueHoras
                    : duracionBloqueAnterior;

                if (duracionBloqueNueva <= 0 || duracionBloqueNueva > 744)
                {
                    await tx.RollbackAsync();

                    return Json(new
                    {
                        ok = false,
                        mensaje = "La duración seleccionada no es válida."
                    });
                }

                var horasProduccionNuevas = request.Redimensionado
                    ? Math.Max(0.25m, duracionBloqueNueva - horasPreparacion)
                    : horasProduccionAnteriores;

                var finNuevo = SumarHorasOperativasCalendario(
                    inicioSolicitado,
                    duracionBloqueNueva
                );

                var arranqueNuevo = SumarHorasOperativasCalendario(
                    inicioSolicitado,
                    horasPreparacion
                );

                const string sqlCruceMaquina = @"
SELECT TOP (1)
    ProgramaProduccionID,
    ISNULL(ReferenciaSAP, NumeroParte) AS Parte
FROM dbo.Planeacion_ProgramaProduccion
WHERE MaquinaID = @MaquinaID
  AND ProgramaProduccionID <> @ProgramaProduccionID
  AND Activo = 1
  AND ISNULL(EstatusID, 1) NOT IN (5, 9, 99)
  AND FechaInicioProgramada < @FechaFin
  AND ISNULL(FechaFinProgramada, DATEADD(HOUR, 1, FechaInicioProgramada)) > @FechaInicio
ORDER BY FechaInicioProgramada;";

                await using (var cmd = new SqlCommand(sqlCruceMaquina, cn, tx))
                {
                    cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = request.MaquinaID;
                    cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = request.ProgramaProduccionID;
                    cmd.Parameters.Add("@FechaInicio", SqlDbType.DateTime).Value = inicioSolicitado;
                    cmd.Parameters.Add("@FechaFin", SqlDbType.DateTime).Value = finNuevo;

                    await using var rd = await cmd.ExecuteReaderAsync();

                    if (await rd.ReadAsync())
                    {
                        var parteCruce = rd["Parte"] as string ?? "otro programa";

                        await tx.RollbackAsync();

                        return Json(new
                        {
                            ok = false,
                            mensaje = $"La máquina {maquinaNuevaCodigo} ya está ocupada por {parteCruce} dentro de ese horario."
                        });
                    }
                }

                if (moldeId.HasValue)
                {
                    const string sqlCruceMolde = @"
SELECT TOP (1)
    ProgramaProduccionID,
    MaquinaCodigo,
    ISNULL(ReferenciaSAP, NumeroParte) AS Parte
FROM dbo.Planeacion_ProgramaProduccion
WHERE MoldeID = @MoldeID
  AND ProgramaProduccionID <> @ProgramaProduccionID
  AND Activo = 1
  AND ISNULL(EstatusID, 1) NOT IN (5, 9, 99)
  AND FechaInicioProgramada < @FechaFin
  AND ISNULL(FechaFinProgramada, DATEADD(HOUR, 1, FechaInicioProgramada)) > @FechaInicio
ORDER BY FechaInicioProgramada;";

                    await using var cmd = new SqlCommand(sqlCruceMolde, cn, tx);

                    cmd.Parameters.Add("@MoldeID", SqlDbType.Int).Value = moldeId.Value;
                    cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = request.ProgramaProduccionID;
                    cmd.Parameters.Add("@FechaInicio", SqlDbType.DateTime).Value = inicioSolicitado;
                    cmd.Parameters.Add("@FechaFin", SqlDbType.DateTime).Value = finNuevo;

                    await using var rd = await cmd.ExecuteReaderAsync();

                    if (await rd.ReadAsync())
                    {
                        var maquinaCruce = rd["MaquinaCodigo"] as string ?? "otra máquina";
                        var parteCruce = rd["Parte"] as string ?? "otro programa";

                        await tx.RollbackAsync();

                        return Json(new
                        {
                            ok = false,
                            mensaje = $"El molde ya está programado en {maquinaCruce} para {parteCruce} dentro de ese horario."
                        });
                    }
                }

                const string sqlUpdatePrograma = @"
UPDATE dbo.Planeacion_ProgramaProduccion
SET
    MaquinaID = @MaquinaID,
    MaquinaCodigo = @MaquinaCodigo,
    MaquinaNombre = @MaquinaNombre,
    FechaInicioProgramada = @FechaInicio,
    FechaFinProgramada = @FechaFin,
    HorasProgramadas = @HorasProgramadas,
    Cambio = @Cambio,
    Arranque = @Arranque,
    UsuarioModificacionID = @UsuarioID,
    FechaModificacion = GETDATE()
WHERE ProgramaProduccionID = @ProgramaProduccionID
  AND Activo = 1;";

                await using (var cmd = new SqlCommand(sqlUpdatePrograma, cn, tx))
                {
                    cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = request.MaquinaID;
                    cmd.Parameters.Add("@MaquinaCodigo", SqlDbType.NVarChar, 100).Value = maquinaNuevaCodigo;
                    cmd.Parameters.Add("@MaquinaNombre", SqlDbType.NVarChar, 200).Value = maquinaNuevaNombre;
                    cmd.Parameters.Add("@FechaInicio", SqlDbType.DateTime).Value = inicioSolicitado;
                    cmd.Parameters.Add("@FechaFin", SqlDbType.DateTime).Value = finNuevo;

                    var horasParam = cmd.Parameters.Add("@HorasProgramadas", SqlDbType.Decimal);
                    horasParam.Precision = 18;
                    horasParam.Scale = 2;
                    horasParam.Value = Math.Round(horasProduccionNuevas, 2);

                    cmd.Parameters.Add("@Cambio", SqlDbType.Time).Value = inicioSolicitado.TimeOfDay;
                    cmd.Parameters.Add("@Arranque", SqlDbType.Time).Value = arranqueNuevo.TimeOfDay;
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                    cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = request.ProgramaProduccionID;

                    await cmd.ExecuteNonQueryAsync();
                }

                if (solicitudProduccionDetalleId.HasValue)
                {
                    const string sqlSincronizarDetalle = @"
UPDATE dbo.SolicitudesProduccionDetalle
SET
    MaquinaSugeridaID = @MaquinaID,
    HorasPlaneadas = @HorasProgramadas,
    Cambio = @Cambio,
    Arranque = @Arranque
WHERE SolicitudProduccionDetalleID = @SolicitudProduccionDetalleID;

UPDATE dbo.SolicitudesProduccionAsignacionMaquina
SET
    MaquinaID = @MaquinaID,
    FechaProgramadaTentativa = @FechaProgramada,
    HoraInicioTentativa = @HoraInicio,
    HoraFinTentativa = @HoraFin,
    HorasEstimadas = @HorasProgramadas
WHERE SolicitudProduccionDetalleID = @SolicitudProduccionDetalleID
  AND Activo = 1;";

                    await using var cmd = new SqlCommand(sqlSincronizarDetalle, cn, tx);

                    cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = request.MaquinaID;

                    var horasParam = cmd.Parameters.Add("@HorasProgramadas", SqlDbType.Decimal);
                    horasParam.Precision = 18;
                    horasParam.Scale = 2;
                    horasParam.Value = Math.Round(horasProduccionNuevas, 2);

                    cmd.Parameters.Add("@Cambio", SqlDbType.Time).Value = inicioSolicitado.TimeOfDay;
                    cmd.Parameters.Add("@Arranque", SqlDbType.Time).Value = arranqueNuevo.TimeOfDay;
                    cmd.Parameters.Add("@FechaProgramada", SqlDbType.Date).Value = inicioSolicitado.Date;
                    cmd.Parameters.Add("@HoraInicio", SqlDbType.Time).Value = inicioSolicitado.TimeOfDay;
                    cmd.Parameters.Add("@HoraFin", SqlDbType.Time).Value = finNuevo.TimeOfDay;
                    cmd.Parameters.Add("@SolicitudProduccionDetalleID", SqlDbType.Int).Value = solicitudProduccionDetalleId.Value;

                    await cmd.ExecuteNonQueryAsync();
                }

                if (solicitudProduccionId.HasValue)
                {
                    const string sqlSincronizarOF = @"
UPDATE dbo.SolicitudesProduccion
SET
    FechaInicioPlaneada = @FechaInicio,
    FechaFinPlaneada = @FechaFin
WHERE SolicitudProduccionID = @SolicitudProduccionID;";

                    await using var cmd = new SqlCommand(sqlSincronizarOF, cn, tx);

                    cmd.Parameters.Add("@FechaInicio", SqlDbType.DateTime).Value = inicioSolicitado;
                    cmd.Parameters.Add("@FechaFin", SqlDbType.DateTime).Value = finNuevo;
                    cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = solicitudProduccionId.Value;

                    await cmd.ExecuteNonQueryAsync();
                }

                await SincronizarReleaseDesdeReprogramacionAsync(
                    request.ProgramaProduccionID,
                    releaseDetalleId,
                    inicioSolicitado,
                    finNuevo,
                    usuarioId,
                    cn,
                    (SqlTransaction)tx
                );

                await InsertarHistorialReprogramacionProgramaAsync(
                    request.ProgramaProduccionID,
                    maquinaAnteriorId,
                    request.MaquinaID,
                    inicioAnterior,
                    inicioSolicitado,
                    finAnterior,
                    finNuevo,
                    horasProduccionAnteriores,
                    horasProduccionNuevas,
                    cambioAnterior,
                    inicioSolicitado.TimeOfDay,
                    arranqueAnterior,
                    arranqueNuevo.TimeOfDay,
                    releaseDetalleId,
                    solicitudProduccionId,
                    solicitudProduccionDetalleId,
                    usuarioId,
                    request.Redimensionado
                        ? "Reprogramación desde calendario semanal con ajuste de duración."
                        : $"Reprogramación desde calendario semanal. Máquina anterior: {maquinaAnteriorCodigo ?? "sin máquina"}, nueva máquina: {maquinaNuevaCodigo}.",
                    cn,
                    (SqlTransaction)tx
                );

                const string sqlReordenar = @"
;WITH OrdenMaquinas AS
(
    SELECT
        ProgramaProduccionID,
        ROW_NUMBER() OVER
        (
            PARTITION BY MaquinaID
            ORDER BY FechaInicioProgramada, ProgramaProduccionID
        ) AS NuevaSecuencia
    FROM dbo.Planeacion_ProgramaProduccion
    WHERE Activo = 1
      AND ISNULL(EstatusID, 1) NOT IN (5, 9, 99)
      AND
      (
            MaquinaID = @MaquinaNuevaID
         OR (@MaquinaAnteriorID IS NOT NULL AND MaquinaID = @MaquinaAnteriorID)
      )
)
UPDATE pp
SET SecuenciaMaquina = om.NuevaSecuencia
FROM dbo.Planeacion_ProgramaProduccion pp
INNER JOIN OrdenMaquinas om
    ON om.ProgramaProduccionID = pp.ProgramaProduccionID;";

                await using (var cmd = new SqlCommand(sqlReordenar, cn, tx))
                {
                    cmd.Parameters.Add("@MaquinaNuevaID", SqlDbType.Int).Value = request.MaquinaID;

                    cmd.Parameters.Add("@MaquinaAnteriorID", SqlDbType.Int).Value =
                        (object?)maquinaAnteriorId ?? DBNull.Value;

                    await cmd.ExecuteNonQueryAsync();
                }

                await tx.CommitAsync();

                return Json(new
                {
                    ok = true,
                    mensaje = "Programa actualizado correctamente.",
                    programaProduccionID = request.ProgramaProduccionID,
                    maquinaID = request.MaquinaID,
                    maquinaCodigo = maquinaNuevaCodigo,
                    inicio = inicioSolicitado.ToString("yyyy-MM-ddTHH:mm:ss"),
                    fin = finNuevo.ToString("yyyy-MM-ddTHH:mm:ss"),
                    horasProgramadas = Math.Round(horasProduccionNuevas, 2)
                });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();

                return Json(new
                {
                    ok = false,
                    mensaje = "No fue posible reprogramar: " + ex.Message
                });
            }
        }

        private async Task SincronizarReleaseDesdeReprogramacionAsync(
    int programaProduccionId,
    int? releaseDetalleId,
    DateTime inicioNuevo,
    DateTime finNuevo,
    int usuarioId,
    SqlConnection cn,
    SqlTransaction tx)
        {
            if (!releaseDetalleId.HasValue || releaseDetalleId.Value <= 0)
                return;

            DateTime? fechaRequeridaCliente = null;

            const string sqlObtenerReleaseDetalle = @"
SELECT TOP 1
    FechaRequerida
FROM dbo.Planeacion_ReleaseDetalle
WHERE ReleaseDetalleID = @ReleaseDetalleID
  AND Activo = 1;";

            await using (var cmd = new SqlCommand(sqlObtenerReleaseDetalle, cn, tx))
            {
                cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value = releaseDetalleId.Value;

                var result = await cmd.ExecuteScalarAsync();

                if (result != null && result != DBNull.Value)
                    fechaRequeridaCliente = Convert.ToDateTime(result).Date;
            }

            bool? daTiempo = null;
            string mensajeCapacidad;

            if (fechaRequeridaCliente.HasValue)
            {
                daTiempo = finNuevo.Date <= fechaRequeridaCliente.Value.Date;

                mensajeCapacidad = daTiempo.Value
                    ? $"Programación actualizada: termina el {finNuevo:dd/MM/yyyy HH:mm}, dentro de la fecha requerida del cliente ({fechaRequeridaCliente:dd/MM/yyyy})."
                    : $"Programación actualizada: termina el {finNuevo:dd/MM/yyyy HH:mm}, posterior a la fecha requerida del cliente ({fechaRequeridaCliente:dd/MM/yyyy}).";
            }
            else
            {
                mensajeCapacidad = $"Programación actualizada: inicio {inicioNuevo:dd/MM/yyyy HH:mm}, fin {finNuevo:dd/MM/yyyy HH:mm}.";
            }

            const string sqlActualizarReleaseDetalle = @"
UPDATE dbo.Planeacion_ReleaseDetalle
SET
    DaTiempo = @DaTiempo,
    FechaInicioSugerida = @FechaInicioProgramada,
    FechaFinEstimada = @FechaFinProgramada,
    MensajeCapacidad = @MensajeCapacidad,
    FechaModificacion = GETDATE(),
    UsuarioModificacionID = @UsuarioModificacionID
WHERE ReleaseDetalleID = @ReleaseDetalleID
  AND Activo = 1;";

            await using (var cmd = new SqlCommand(sqlActualizarReleaseDetalle, cn, tx))
            {
                cmd.Parameters.Add("@DaTiempo", SqlDbType.Bit).Value =
                    daTiempo.HasValue ? daTiempo.Value : DBNull.Value;

                cmd.Parameters.Add("@FechaInicioProgramada", SqlDbType.DateTime).Value = inicioNuevo;
                cmd.Parameters.Add("@FechaFinProgramada", SqlDbType.DateTime).Value = finNuevo;
                cmd.Parameters.Add("@MensajeCapacidad", SqlDbType.NVarChar, 500).Value = mensajeCapacidad;
                cmd.Parameters.Add("@UsuarioModificacionID", SqlDbType.Int).Value = usuarioId;
                cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value = releaseDetalleId.Value;

                await cmd.ExecuteNonQueryAsync();
            }

            const string sqlActualizarReleasePadre = @"
UPDATE r
SET
    r.FechaModificacion = GETDATE(),
    r.UsuarioModificacionID = @UsuarioModificacionID
FROM dbo.Planeacion_Releases r
INNER JOIN dbo.Planeacion_ReleaseDetalle d
    ON d.ReleaseID = r.ReleaseID
WHERE d.ReleaseDetalleID = @ReleaseDetalleID
  AND r.Activo = 1;";

            await using (var cmd = new SqlCommand(sqlActualizarReleasePadre, cn, tx))
            {
                cmd.Parameters.Add("@UsuarioModificacionID", SqlDbType.Int).Value = usuarioId;
                cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value = releaseDetalleId.Value;

                await cmd.ExecuteNonQueryAsync();
            }
        }

        private async Task InsertarHistorialReprogramacionProgramaAsync(
    int programaProduccionId,
    int? maquinaAnteriorId,
    int? maquinaNuevaId,
    DateTime inicioAnterior,
    DateTime inicioNuevo,
    DateTime finAnterior,
    DateTime finNuevo,
    decimal horasAnteriores,
    decimal horasNuevas,
    TimeSpan? cambioAnterior,
    TimeSpan? cambioNuevo,
    TimeSpan? arranqueAnterior,
    TimeSpan? arranqueNuevo,
    int? releaseDetalleId,
    int? solicitudProduccionId,
    int? solicitudProduccionDetalleId,
    int usuarioId,
    string? motivo,
    SqlConnection cn,
    SqlTransaction tx)
        {
            DateTime? fechaRequeridaCliente = null;
            bool? daTiempoDespues = null;

            if (releaseDetalleId.HasValue && releaseDetalleId.Value > 0)
            {
                const string sqlFechaRequerida = @"
SELECT TOP 1
    FechaRequerida
FROM dbo.Planeacion_ReleaseDetalle
WHERE ReleaseDetalleID = @ReleaseDetalleID
  AND Activo = 1;";

                await using var cmdFecha = new SqlCommand(sqlFechaRequerida, cn, tx);
                cmdFecha.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value = releaseDetalleId.Value;

                var result = await cmdFecha.ExecuteScalarAsync();

                if (result != null && result != DBNull.Value)
                {
                    fechaRequeridaCliente = Convert.ToDateTime(result).Date;
                    daTiempoDespues = finNuevo.Date <= fechaRequeridaCliente.Value.Date;
                }
            }

            const string sql = @"
INSERT INTO dbo.Planeacion_ProgramaReprogramacionHistorial
(
    ProgramaProduccionID,

    MaquinaAnteriorID,
    MaquinaNuevaID,

    InicioAnterior,
    InicioNuevo,

    FinAnterior,
    FinNuevo,

    HorasAnteriores,
    HorasNuevas,

    CambioAnterior,
    CambioNuevo,

    ArranqueAnterior,
    ArranqueNuevo,

    ReleaseDetalleID,
    SolicitudProduccionID,
    SolicitudProduccionDetalleID,

    DaTiempoDespues,
    FechaRequeridaCliente,

    UsuarioID,
    FechaCambio,

    Motivo
)
VALUES
(
    @ProgramaProduccionID,

    @MaquinaAnteriorID,
    @MaquinaNuevaID,

    @InicioAnterior,
    @InicioNuevo,

    @FinAnterior,
    @FinNuevo,

    @HorasAnteriores,
    @HorasNuevas,

    @CambioAnterior,
    @CambioNuevo,

    @ArranqueAnterior,
    @ArranqueNuevo,

    @ReleaseDetalleID,
    @SolicitudProduccionID,
    @SolicitudProduccionDetalleID,

    @DaTiempoDespues,
    @FechaRequeridaCliente,

    @UsuarioID,
    GETDATE(),

    @Motivo
);";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaProduccionId;

            cmd.Parameters.Add("@MaquinaAnteriorID", SqlDbType.Int).Value =
                (object?)maquinaAnteriorId ?? DBNull.Value;

            cmd.Parameters.Add("@MaquinaNuevaID", SqlDbType.Int).Value =
                (object?)maquinaNuevaId ?? DBNull.Value;

            cmd.Parameters.Add("@InicioAnterior", SqlDbType.DateTime).Value = inicioAnterior;
            cmd.Parameters.Add("@InicioNuevo", SqlDbType.DateTime).Value = inicioNuevo;

            cmd.Parameters.Add("@FinAnterior", SqlDbType.DateTime).Value = finAnterior;
            cmd.Parameters.Add("@FinNuevo", SqlDbType.DateTime).Value = finNuevo;

            var horasAnterioresParam = cmd.Parameters.Add("@HorasAnteriores", SqlDbType.Decimal);
            horasAnterioresParam.Precision = 18;
            horasAnterioresParam.Scale = 2;
            horasAnterioresParam.Value = Math.Round(horasAnteriores, 2);

            var horasNuevasParam = cmd.Parameters.Add("@HorasNuevas", SqlDbType.Decimal);
            horasNuevasParam.Precision = 18;
            horasNuevasParam.Scale = 2;
            horasNuevasParam.Value = Math.Round(horasNuevas, 2);

            cmd.Parameters.Add("@CambioAnterior", SqlDbType.Time).Value =
                (object?)cambioAnterior ?? DBNull.Value;

            cmd.Parameters.Add("@CambioNuevo", SqlDbType.Time).Value =
                (object?)cambioNuevo ?? DBNull.Value;

            cmd.Parameters.Add("@ArranqueAnterior", SqlDbType.Time).Value =
                (object?)arranqueAnterior ?? DBNull.Value;

            cmd.Parameters.Add("@ArranqueNuevo", SqlDbType.Time).Value =
                (object?)arranqueNuevo ?? DBNull.Value;

            cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value =
                (object?)releaseDetalleId ?? DBNull.Value;

            cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value =
                (object?)solicitudProduccionId ?? DBNull.Value;

            cmd.Parameters.Add("@SolicitudProduccionDetalleID", SqlDbType.Int).Value =
                (object?)solicitudProduccionDetalleId ?? DBNull.Value;

            cmd.Parameters.Add("@DaTiempoDespues", SqlDbType.Bit).Value =
                daTiempoDespues.HasValue ? daTiempoDespues.Value : DBNull.Value;

            cmd.Parameters.Add("@FechaRequeridaCliente", SqlDbType.Date).Value =
                fechaRequeridaCliente.HasValue ? fechaRequeridaCliente.Value.Date : DBNull.Value;

            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;

            cmd.Parameters.Add("@Motivo", SqlDbType.NVarChar, 500).Value =
                string.IsNullOrWhiteSpace(motivo)
                    ? DBNull.Value
                    : motivo.Trim();

            await cmd.ExecuteNonQueryAsync();
        }


        [HttpGet]
        public async Task<IActionResult> Maquinas(DateTime? fechaDesde, DateTime? fechaHasta)
        {
            var desde = fechaDesde?.Date ?? DateTime.Today;
            var hasta = fechaHasta?.Date ?? desde.AddDays(7);

            if (hasta < desde)
                hasta = desde;

            var programas = await ObtenerProgramasPorRangoAsync(desde, hasta);

            var maquinas = programas
                .GroupBy(x => new
                {
                    x.MaquinaID,
                    x.MaquinaCodigo,
                    x.MaquinaNombre
                })
                .Select(g => new PlaneacionProgramaMaquinaVm
                {
                    MaquinaID = g.Key.MaquinaID,
                    MaquinaCodigo = g.Key.MaquinaCodigo,
                    MaquinaNombre = g.Key.MaquinaNombre,
                    Programas = g
                        .OrderBy(x => x.FechaInicioProgramada)
                        .ThenBy(x => x.SecuenciaMaquina)
                        .ThenBy(x => x.ProgramaProduccionID)
                        .ToList()
                })
                .OrderBy(x => x.MaquinaCodigo)
                .ToList();

            var vm = new PlaneacionProgramaMaquinasVm
            {
                FechaDesde = desde,
                FechaHasta = hasta,
                Maquinas = maquinas
            };

            return View(vm);
        }
        [HttpGet]
        public async Task<IActionResult> CrearDesdeNecesidad(int releaseDetalleId)
        {
            if (releaseDetalleId <= 0)
                return BadRequest();

            var vm = await ObtenerNecesidadParaProgramaAsync(releaseDetalleId);

            if (vm == null)
            {
                TempData["Error"] = "No se encontró la necesidad seleccionada.";
                return RedirectToAction(nameof(Index));
            }

            if (vm.PiezasAProducir <= 0)
            {
                TempData["Error"] = "La necesidad seleccionada no tiene piezas pendientes por producir.";
                return RedirectToAction(nameof(Index));
            }

            vm.CantidadProgramada = vm.PiezasAProducir;

            var horaBase = RedondearSiguienteHora(DateTime.Now);
            vm.FechaInicioProgramada = horaBase;

            if (vm.MaquinaID.HasValue)
            {
                var sugerencia = await ObtenerSiguienteCambioDisponibleAsync(
                    vm.MaquinaID.Value,
                    horaBase,
                    vm.ParteID,
                    vm.MoldeID
                );

                vm.FechaInicioProgramada = sugerencia.Cambio;
                vm.Cambio = sugerencia.Cambio.TimeOfDay;
                vm.Arranque = sugerencia.Arranque.TimeOfDay;

                if (vm.HorasProgramadas.HasValue && vm.HorasProgramadas.Value > 0)
                    vm.FechaFinProgramada = sugerencia.Arranque.AddHours((double)vm.HorasProgramadas.Value);

                if (sugerencia.OmiteHoraCambio)
                {
                    vm.Observaciones = string.IsNullOrWhiteSpace(vm.Observaciones)
                        ? sugerencia.Motivo
                        : vm.Observaciones + Environment.NewLine + sugerencia.Motivo;
                }
            }
            else
            {
                vm.Cambio = horaBase.TimeOfDay;
                vm.Arranque = horaBase.AddHours(1).TimeOfDay;

                if (vm.HorasProgramadas.HasValue && vm.HorasProgramadas.Value > 0)
                    vm.FechaFinProgramada = horaBase.AddHours(1).AddHours((double)vm.HorasProgramadas.Value);
            }

            await CargarCatalogosAsync(vm);

            return View("Crear", vm);
        }




        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Crear(PlaneacionProgramaCrearDesdeNecesidadVm vm)
        {
            var usuarioId = ObtenerUsuarioID();

            vm.TipoOF = "RELEASE";
            vm.MotivoTipoOF = null;

            if (usuarioId <= 0)
            {
                ModelState.AddModelError("", "No se pudo identificar el usuario de la sesión.");
            }

            if (vm.ReleaseDetalleID <= 0)
                ModelState.AddModelError("", "No se recibió el renglón de release.");

            if (vm.CantidadProgramada <= 0)
                ModelState.AddModelError(nameof(vm.CantidadProgramada), "La cantidad programada debe ser mayor a cero.");

            if (!vm.MaquinaID.HasValue)
                ModelState.AddModelError(nameof(vm.MaquinaID), "Selecciona la máquina.");

            if (!vm.MoldeID.HasValue)
                ModelState.AddModelError(nameof(vm.MoldeID), "Selecciona el molde.");

            if (string.IsNullOrWhiteSpace(vm.CondicionProduccion))
                ModelState.AddModelError(nameof(vm.CondicionProduccion), "Selecciona la condición de producción.");

            if (!vm.FechaInicioProgramada.HasValue)
                ModelState.AddModelError(nameof(vm.FechaInicioProgramada), "Captura la fecha y hora de cambio.");

            if (!vm.Cambio.HasValue)
                ModelState.AddModelError(nameof(vm.Cambio), "Captura la hora de cambio de molde.");

            if (!vm.Arranque.HasValue)
                ModelState.AddModelError(nameof(vm.Arranque), "Captura la hora de arranque.");

            if (!vm.HorasProgramadas.HasValue || vm.HorasProgramadas.Value <= 0)
                ModelState.AddModelError(nameof(vm.HorasProgramadas), "Las horas programadas deben ser mayores a cero.");

            if (!vm.OperadorPrincipalID.HasValue || vm.OperadorPrincipalID.Value <= 0)
            {
                ModelState.AddModelError(nameof(vm.OperadorPrincipalID), "Selecciona el operador principal.");
            }

            if (vm.OperadorAuxiliarID.HasValue &&
                vm.OperadorAuxiliarID.Value > 0 &&
                vm.OperadorPrincipalID.HasValue &&
                vm.OperadorAuxiliarID.Value == vm.OperadorPrincipalID.Value)
            {
                ModelState.AddModelError(nameof(vm.OperadorAuxiliarID), "El operador auxiliar debe ser diferente al operador principal.");
            }

            if (vm.FechaInicioProgramada.HasValue && vm.Cambio.HasValue)
            {
                // La fecha/hora de inicio del programa representa la hora de cambio.
                vm.FechaInicioProgramada = CalcularFechaHoraDesdeHora(
                    vm.FechaInicioProgramada.Value.Date,
                    vm.Cambio
                );
            }

            var minimoPermitido = RedondearSiguienteBloque(DateTime.Now, 15);

            if (vm.FechaInicioProgramada.HasValue && vm.FechaInicioProgramada.Value < minimoPermitido)
            {
                ModelState.AddModelError(
                    nameof(vm.FechaInicioProgramada),
                    $"No puedes seleccionar una hora que ya pasó. Selecciona {minimoPermitido:dd/MM/yyyy HH:mm} o posterior."
                );
            }

            DateTime? fechaArranque = null;

            if (vm.FechaInicioProgramada.HasValue && vm.Arranque.HasValue)
            {
                fechaArranque = CalcularFechaHoraDesdeHora(
                    vm.FechaInicioProgramada.Value.Date,
                    vm.Arranque
                );

                // Si el arranque quedó menor al cambio, se entiende como día siguiente.
                // Si es igual, se permite: significa que se ahorró la hora de cambio.
                if (fechaArranque.Value < vm.FechaInicioProgramada.Value)
                    fechaArranque = fechaArranque.Value.AddDays(1);
            }

            if (fechaArranque.HasValue &&
                vm.HorasProgramadas.HasValue &&
                vm.HorasProgramadas.Value > 0)
            {
                // El fin se calcula desde el arranque, no desde el cambio.
                vm.FechaFinProgramada = fechaArranque.Value.AddHours((double)vm.HorasProgramadas.Value);
            }

            if (!ModelState.IsValid)
            {
                await CargarCatalogosAsync(vm);
                return View(vm);
            }

            var esInterrupcion = string.Equals(
                vm.CondicionProduccion,
                PlaneacionProgramaCondicion.InterrumpirProduccion,
                StringComparison.OrdinalIgnoreCase
            );

            if (!esInterrupcion)
            {
                var maquinaOcupada = await MaquinaTieneCruceAsync(
                    vm.MaquinaID!.Value,
                    vm.FechaInicioProgramada!.Value,
                    vm.FechaFinProgramada!.Value
                );

                if (maquinaOcupada)
                {
                    var sugerida = await ObtenerSiguienteCambioDisponibleAsync(
                        vm.MaquinaID.Value,
                        vm.FechaInicioProgramada.Value,
                        vm.ParteID,
                        vm.MoldeID
                    );

                    vm.FechaInicioProgramada = sugerida.Cambio;
                    vm.Cambio = sugerida.Cambio.TimeOfDay;
                    vm.Arranque = sugerida.Arranque.TimeOfDay;

                    if (vm.HorasProgramadas.HasValue && vm.HorasProgramadas.Value > 0)
                        vm.FechaFinProgramada = sugerida.Arranque.AddHours((double)vm.HorasProgramadas.Value);

                    ModelState.AddModelError(
                        nameof(vm.MaquinaID),
                        "Seleccionaste T.P. Para terminar producción, la máquina debe respetar la cola actual. " +
                        $"La siguiente hora disponible sugerida es {sugerida.Cambio:dd/MM/yyyy HH:mm}. " +
                        sugerida.Motivo + " También puedes seleccionar I.P si realmente se va a interrumpir la producción actual."
                    );

                    await CargarCatalogosAsync(vm);
                    return View(vm);
                }
            }

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            await using var tx = await cn.BeginTransactionAsync();

            try
            {
                var existe = await ReleaseDetalleYaProgramadoAsync(
                    vm.ReleaseDetalleID,
                    cn,
                    (SqlTransaction)tx
                );

                if (existe)
                {
                    await tx.RollbackAsync();

                    TempData["Error"] = "Ese renglón de release ya fue programado.";
                    return RedirectToAction(nameof(Index));
                }

                if (!esInterrupcion)
                {
                    var cruceDentroTx = await MaquinaTieneCruceAsync(
                        vm.MaquinaID!.Value,
                        vm.FechaInicioProgramada!.Value,
                        vm.FechaFinProgramada!.Value,
                        cn,
                        (SqlTransaction)tx
                    );

                    if (cruceDentroTx)
                    {
                        await tx.RollbackAsync();

                        var sugerida = await ObtenerSiguienteCambioDisponibleAsync(
                            vm.MaquinaID.Value,
                            vm.FechaInicioProgramada.Value,
                            vm.ParteID,
                            vm.MoldeID
                        );

                        vm.FechaInicioProgramada = sugerida.Cambio;
                        vm.Cambio = sugerida.Cambio.TimeOfDay;
                        vm.Arranque = sugerida.Arranque.TimeOfDay;

                        if (vm.HorasProgramadas.HasValue && vm.HorasProgramadas.Value > 0)
                            vm.FechaFinProgramada = sugerida.Arranque.AddHours((double)vm.HorasProgramadas.Value);

                        ModelState.AddModelError(
                            nameof(vm.MaquinaID),
                            $"La máquina se ocupó mientras estabas programando. La siguiente hora disponible sugerida es {sugerida.Cambio:dd/MM/yyyy HH:mm}. {sugerida.Motivo}"
                        );

                        await CargarCatalogosAsync(vm);
                        return View(vm);
                    }

                    if (vm.MoldeID.HasValue)
                    {
                        var moldeOcupado = await MoldeTieneCruceAsync(
                            vm.MoldeID.Value,
                            vm.FechaInicioProgramada!.Value,
                            vm.FechaFinProgramada!.Value,
                            vm.MaquinaID,
                            cn,
                            (SqlTransaction)tx
                        );

                        if (moldeOcupado)
                        {
                            await tx.RollbackAsync();

                            ModelState.AddModelError(
                                nameof(vm.MoldeID),
                                "Ese molde ya está programado en otra máquina dentro del horario seleccionado."
                            );

                            await CargarCatalogosAsync(vm);
                            return View(vm);
                        }
                    }
                }
                else
                {
                    await InterrumpirProgramasCruzadosAsync(
                        vm.MaquinaID!.Value,
                        vm.FechaInicioProgramada!.Value,
                        vm.FechaFinProgramada!.Value,
                        usuarioId,
                        cn,
                        (SqlTransaction)tx
                    );
                }

                var requiereRecursoCambio = vm.Cambio.HasValue &&
                                            vm.Arranque.HasValue &&
                                            vm.Cambio.Value != vm.Arranque.Value;

                if (requiereRecursoCambio)
                {
                    var cambioOcupado = await CambioMoldeTieneCruceAsync(
                        vm.FechaInicioProgramada!.Value,
                        cn,
                        (SqlTransaction)tx
                    );

                    if (cambioOcupado)
                    {
                        await tx.RollbackAsync();

                        ModelState.AddModelError(
                            nameof(vm.Cambio),
                            "Ya existe un cambio de molde programado en esa misma hora. Solo se cuenta con un recurso para cambio de molde."
                        );

                        await CargarCatalogosAsync(vm);
                        return View(vm);
                    }
                }

                await CompletarDatosProgramaAsync(vm, cn, (SqlTransaction)tx);

                var programaId = await InsertarProgramaAsync(
                    vm,
                    usuarioId,
                    cn,
                    (SqlTransaction)tx
                );

                await InsertarOperadoresProgramaAsync(
                    programaId,
                    vm,
                    usuarioId,
                    cn,
                    (SqlTransaction)tx
                );

                await MarcarReleaseDetalleProgramadoAsync(
                    vm.ReleaseDetalleID,
                    programaId,
                    usuarioId,
                    cn,
                    (SqlTransaction)tx
                );

                await VincularApartadoPTAProgramaAsync(
                    vm.ReleaseDetalleID,
                    programaId,
                    cn,
                    (SqlTransaction)tx
                );

                await tx.CommitAsync();

                TempData["Success"] = esInterrupcion
                    ? "Cambio de molde programado correctamente. Se registró como I.P y se interrumpió la producción cruzada."
                    : "Cambio de molde programado correctamente.";

                return RedirectToAction(nameof(Maquinas));
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();

                ModelState.AddModelError("", "Error al programar cambio de molde: " + ex.Message);
                await CargarCatalogosAsync(vm);
                return View(vm);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GenerarOF(int programaProduccionId)
        {
            if (programaProduccionId <= 0)
            {
                TempData["Error"] = "No se recibió el programa de producción.";
                return RedirectToAction(nameof(Index));
            }

            var usuarioId = ObtenerUsuarioID();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            await using var tx = await cn.BeginTransactionAsync();

            try
            {
                var programa = await ObtenerProgramaParaGenerarOFAsync(
                    programaProduccionId,
                    cn,
                    (SqlTransaction)tx
                );

                if (programa == null)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "No se encontró el programa seleccionado.";
                    return RedirectToAction(nameof(Index));
                }

                if (programa.SolicitudProduccionID.HasValue)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "Este programa ya tiene una OF generada.";
                    return RedirectToAction(nameof(Index));
                }

                if (programa.CantidadProgramada <= 0)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "El programa no tiene cantidad programada válida.";
                    return RedirectToAction(nameof(Index));
                }

                var folioOF = await GenerarFolioOFAsync(cn, (SqlTransaction)tx);

                var solicitudProduccionId = await InsertarOFDedeProgramaAsync(
                    programa,
                    folioOF,
                    usuarioId,
                    cn,
                    (SqlTransaction)tx
                );

                var solicitudProduccionDetalleId = await InsertarDetalleOFDedeProgramaAsync(
                    solicitudProduccionId,
                    programa,
                    usuarioId,
                    cn,
                    (SqlTransaction)tx
                );

                await InsertarAsignacionMaquinaOFDedeProgramaAsync(
                    solicitudProduccionDetalleId,
                    programa,
                    usuarioId,
                    cn,
                    (SqlTransaction)tx
                );

                await MarcarProgramaConOFAsync(
                    programaProduccionId,
                    solicitudProduccionId,
                    solicitudProduccionDetalleId,
                    usuarioId,
                    cn,
                    (SqlTransaction)tx
                );

                if (programa.ReleaseDetalleID.HasValue)
                {
                    await MarcarReleaseDetalleConOFAsync(
                        programa.ReleaseDetalleID.Value,
                        solicitudProduccionId,
                        cn,
                        (SqlTransaction)tx
                    );

                    await VincularApartadoPTAOFAsync(
                        programa.ReleaseDetalleID.Value,
                        programaProduccionId,
                        solicitudProduccionId,
                        cn,
                        (SqlTransaction)tx
                    );
                }

                await tx.CommitAsync();

                TempData["Success"] = "OF generada correctamente desde el programa de producción.";
                return RedirectToAction("Detalle", "Planeacion", new { id = solicitudProduccionId });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();

                TempData["Error"] = "Error al generar la OF: " + ex.Message;
                return RedirectToAction(nameof(Index));
            }
        }





        private async Task<PlaneacionProgramaCrearDesdeNecesidadVm?> ObtenerNecesidadParaProgramaAsync(int releaseDetalleId)
        {
            const string sql = @"
SELECT
    r.ReleaseID,
    r.FolioRelease,

    r.ClienteID,
    ISNULL(c.Nombre, r.ClienteNombre) AS ClienteNombre,

    d.ReleaseDetalleID,
    d.ParteID,
    d.NumeroParte,
    d.ReferenciaSAP,
    d.DesignacionDescripcionSAP,
    d.FechaRequerida,
    d.CantidadRequerida,
    d.ProgramaProduccionID,

    t.Color,

    t.MaterialID,
    t.MaterialCodigo,
    t.MaterialDescripcion,
    t.PesoBrutoPieza,
    t.PesoNetoPieza,

    t.EmbalajeCodigo,
    t.EmbalajeDescripcion,
    t.PiezasPorEmbalaje,
    t.PiezasPorCaja,

    t.MoldePrincipalID AS MoldeID,
    mol.CodigoMolde AS MoldeCodigo,

    t.MaquinaPrincipalID AS MaquinaSugeridaID,
    maq.Codigo AS MaquinaSugeridaCodigo,
    maq.Nombre AS MaquinaSugeridaNombre,

    t.MaquinaSustitutaID,
    maq2.Codigo AS MaquinaSustitutaCodigo,
    maq2.Nombre AS MaquinaSustitutaNombre,

    t.ObjetivoHora,
    t.Ciclo,
    t.Cavidades,
    t.TipoSecado,
    t.HorasSecado,
    t.HorasSecadoTexto,

    ISNULL(pt.Disponible, 0) AS PTDisponible,
    ISNULL(mp.Saldo, 0) AS MPDisponible,
    ISNULL(emb.Saldo, 0) AS EmbalajeDisponible,
    ISNULL(prog.ProgramadoPendiente, 0) AS ProgramadoPendiente,
    ISNULL(aptPropio.CantidadApartada, 0) AS PTApartadoPropio,
    ISNULL(aptOtros.CantidadApartada, 0) AS PTApartadoOtros

FROM dbo.Planeacion_ReleaseDetalle d
INNER JOIN dbo.Planeacion_Releases r
    ON r.ReleaseID = d.ReleaseID
LEFT JOIN dbo.ERP_Clientes c
    ON c.ClienteID = r.ClienteID

LEFT JOIN dbo.ERP_ParteDatosTecnicos t
    ON t.ParteID = d.ParteID
   AND t.Activo = 1

LEFT JOIN dbo.ERP_Moldes mol
    ON mol.MoldeID = t.MoldePrincipalID

LEFT JOIN dbo.ERP_Maquinas maq
    ON maq.MaquinaID = t.MaquinaPrincipalID

LEFT JOIN dbo.ERP_Maquinas maq2
    ON maq2.MaquinaID = t.MaquinaSustitutaID

OUTER APPLY
(
    SELECT TOP 1 ISNULL(Disponible, 0) AS Disponible
    FROM dbo.vw_AlmacenPTInventario
    WHERE ParteID = d.ParteID
) pt

OUTER APPLY
(
    SELECT TOP 1 ISNULL(Saldo, 0) AS Saldo
    FROM dbo.vw_AlmacenMPInventario
    WHERE MaterialID = t.MaterialID
) mp

OUTER APPLY
(
    SELECT TOP 1 ISNULL(Saldo, 0) AS Saldo
    FROM dbo.vw_AlmacenEmbalajesInventario
    WHERE Codigo = t.EmbalajeCodigo
) emb

OUTER APPLY
(
    SELECT ISNULL(SUM(ISNULL(pp.CantidadProgramada, 0) - ISNULL(pp.CantidadProducida, 0)), 0) AS ProgramadoPendiente
    FROM dbo.Planeacion_ProgramaProduccion pp
    WHERE pp.ReleaseDetalleID = d.ReleaseDetalleID
      AND pp.Activo = 1
      AND ISNULL(pp.EstatusID, 1) NOT IN (5, 9, 99)
) prog

OUTER APPLY
(
    SELECT ISNULL(SUM(a.CantidadApartada), 0) AS CantidadApartada
    FROM dbo.Planeacion_PT_Apartado a
    WHERE a.ReleaseDetalleID = d.ReleaseDetalleID
      AND a.Activo = 1
      AND a.EstatusID = 1
) aptPropio

OUTER APPLY
(
    SELECT ISNULL(SUM(a.CantidadApartada), 0) AS CantidadApartada
    FROM dbo.Planeacion_PT_Apartado a
    WHERE a.ParteID = d.ParteID
      AND a.ReleaseDetalleID <> d.ReleaseDetalleID
      AND a.Activo = 1
      AND a.EstatusID = 1
) aptOtros

WHERE d.ReleaseDetalleID = @ReleaseDetalleID
  AND d.Activo = 1
  AND r.Activo = 1;";

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            await SincronizarApartadosPTAsync(cn);

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value = releaseDetalleId;

            await using var rd = await cmd.ExecuteReaderAsync();

            if (!await rd.ReadAsync())
                return null;

            if (rd["ProgramaProduccionID"] != DBNull.Value)
                return null;

            var cantidadRequerida = rd["CantidadRequerida"] == DBNull.Value ? 0 : Convert.ToInt32(rd["CantidadRequerida"]);
            var stockDisponible = rd["PTDisponible"] == DBNull.Value ? 0 : Convert.ToInt32(rd["PTDisponible"]);
            var ptApartadoPropio = rd["PTApartadoPropio"] == DBNull.Value ? 0 : Convert.ToInt32(rd["PTApartadoPropio"]);
            var ptApartadoOtros = rd["PTApartadoOtros"] == DBNull.Value ? 0 : Convert.ToInt32(rd["PTApartadoOtros"]);
            var programadoPendiente = rd["ProgramadoPendiente"] == DBNull.Value ? 0 : Convert.ToInt32(rd["ProgramadoPendiente"]);

            var ptDisponibleNeto = Math.Max(0, stockDisponible - ptApartadoOtros);

            var piezasDesdeStock = ptApartadoPropio > 0
                ? Math.Min(ptApartadoPropio, cantidadRequerida)
                : Math.Min(ptDisponibleNeto, cantidadRequerida);

            var piezasAProducir = Math.Max(0, cantidadRequerida - piezasDesdeStock - programadoPendiente);

            var pesoBrutoPieza = rd["PesoBrutoPieza"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(rd["PesoBrutoPieza"]);
            var piezasPorEmbalaje = rd["PiezasPorEmbalaje"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(rd["PiezasPorEmbalaje"]);
            var objetivoHora = rd["ObjetivoHora"] == DBNull.Value ? (int?)null : Convert.ToInt32(rd["ObjetivoHora"]);

            decimal cantidadMpKg = 0;
            if (piezasAProducir > 0 && pesoBrutoPieza.HasValue && pesoBrutoPieza.Value > 0)
                cantidadMpKg = Math.Round((piezasAProducir * pesoBrutoPieza.Value) / 1000m, 4); // PesoBrutoPieza está en gramos; resultado en kg.

            decimal cantidadEmbalajes = 0;
            if (piezasAProducir > 0 && piezasPorEmbalaje.HasValue && piezasPorEmbalaje.Value > 0)
                cantidadEmbalajes = Math.Ceiling(piezasAProducir / piezasPorEmbalaje.Value);

            decimal horasProgramadas = 0;
            if (piezasAProducir > 0 && objetivoHora.HasValue && objetivoHora.Value > 0)
                horasProgramadas = Math.Ceiling(piezasAProducir / (decimal)objetivoHora.Value);

           var fechaInicio = RedondearSiguienteHora(DateTime.Now);

            DateTime? fechaFin = null;
            if (horasProgramadas > 0)
                fechaFin = fechaInicio.AddHours((double)horasProgramadas);

            return new PlaneacionProgramaCrearDesdeNecesidadVm
            {
                ReleaseDetalleID = Convert.ToInt32(rd["ReleaseDetalleID"]),
                ReleaseID = rd["ReleaseID"] == DBNull.Value ? null : Convert.ToInt32(rd["ReleaseID"]),
                FolioRelease = rd["FolioRelease"] as string,

                TipoOF = "RELEASE",
                MotivoTipoOF = null,

                ClienteID = rd["ClienteID"] == DBNull.Value ? null : Convert.ToInt32(rd["ClienteID"]),
                ClienteNombre = rd["ClienteNombre"] as string,

                ParteID = rd["ParteID"] == DBNull.Value ? null : Convert.ToInt32(rd["ParteID"]),
                NumeroParte = rd["NumeroParte"] as string,
                ReferenciaSAP = rd["ReferenciaSAP"] as string,
                DesignacionDescripcionSAP = rd["DesignacionDescripcionSAP"] as string,

                Color = rd["Color"] as string,

                CantidadRequerida = cantidadRequerida,
                PiezasDesdePT = piezasDesdeStock,
                PTDisponibleAlCalcular = stockDisponible,
                PTApartadoOtros = ptApartadoOtros,
                PTDisponibleNeto = ptDisponibleNeto,
                PiezasAProducir = piezasAProducir,

                MaterialID = rd["MaterialID"] == DBNull.Value ? null : Convert.ToInt32(rd["MaterialID"]),
                MaterialCodigo = rd["MaterialCodigo"] as string,
                MaterialDescripcion = rd["MaterialDescripcion"] as string,
                PesoBrutoPieza = pesoBrutoPieza,

                CantidadMpKg = cantidadMpKg,

                EmbalajeCodigo = rd["EmbalajeCodigo"] as string,
                EmbalajeDescripcion = rd["EmbalajeDescripcion"] as string,
                PiezasPorEmbalaje = piezasPorEmbalaje,
                CantidadEmbalajes = cantidadEmbalajes,

                MoldeID = rd["MoldeID"] == DBNull.Value ? null : Convert.ToInt32(rd["MoldeID"]),
                MoldeCodigo = rd["MoldeCodigo"] as string,

                MaquinaID = rd["MaquinaSugeridaID"] == DBNull.Value ? null : Convert.ToInt32(rd["MaquinaSugeridaID"]),
                MaquinaCodigo = rd["MaquinaSugeridaCodigo"] as string,
                MaquinaNombre = rd["MaquinaSugeridaNombre"] as string,

                MaquinaSustitutaID = rd["MaquinaSustitutaID"] == DBNull.Value ? null : Convert.ToInt32(rd["MaquinaSustitutaID"]),
                MaquinaSustitutaCodigo = rd["MaquinaSustitutaCodigo"] as string,
                MaquinaSustitutaNombre = rd["MaquinaSustitutaNombre"] as string,

                ObjetivoHora = objetivoHora,
                Ciclo = rd["Ciclo"] == DBNull.Value ? null : rd["Ciclo"].ToString(),
                Cavidades = rd["Cavidades"] == DBNull.Value ? null : Convert.ToInt32(rd["Cavidades"]),
                PiezasPorCaja = rd["PiezasPorCaja"] == DBNull.Value ? null : Convert.ToInt32(rd["PiezasPorCaja"]),
                TipoSecado = rd["TipoSecado"] as string,
                HorasSecado = rd["HorasSecado"] == DBNull.Value ? null : Convert.ToDecimal(rd["HorasSecado"]),
                HorasSecadoTexto = rd["HorasSecadoTexto"] as string,

                HorasProgramadas = horasProgramadas,
                FechaInicioProgramada = fechaInicio,
                FechaFinProgramada = fechaFin
            };
        }

        private async Task CompletarDatosProgramaAsync(
            PlaneacionProgramaCrearDesdeNecesidadVm vm,
            SqlConnection cn,
            SqlTransaction tx)
        {
            if (vm.MaquinaID.HasValue)
            {
                const string sqlMaq = @"
SELECT TOP 1
    Codigo,
    Nombre
FROM dbo.ERP_Maquinas
WHERE MaquinaID = @MaquinaID;";

                await using var cmd = new SqlCommand(sqlMaq, cn, tx);
                cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = vm.MaquinaID.Value;

                await using var rd = await cmd.ExecuteReaderAsync();

                if (await rd.ReadAsync())
                {
                    vm.MaquinaCodigo = rd["Codigo"] as string;
                    vm.MaquinaNombre = rd["Nombre"] as string;
                }
            }

            if (vm.MoldeID.HasValue)
            {
                const string sqlMolde = @"
SELECT TOP 1
    CodigoMolde
FROM dbo.ERP_Moldes
WHERE MoldeID = @MoldeID;";

                await using var cmd = new SqlCommand(sqlMolde, cn, tx);
                cmd.Parameters.Add("@MoldeID", SqlDbType.Int).Value = vm.MoldeID.Value;

                await using var rd = await cmd.ExecuteReaderAsync();

                if (await rd.ReadAsync())
                {
                    vm.MoldeCodigo = rd["CodigoMolde"] as string;
                }
            }

            if (vm.FechaInicioProgramada.HasValue &&
                vm.Arranque.HasValue &&
                vm.HorasProgramadas.HasValue &&
                vm.HorasProgramadas.Value > 0)
            {
                var fechaArranque = CalcularFechaHoraDesdeHora(
                    vm.FechaInicioProgramada.Value.Date,
                    vm.Arranque
                );

                if (fechaArranque < vm.FechaInicioProgramada.Value)
                    fechaArranque = fechaArranque.AddDays(1);

                vm.FechaFinProgramada = fechaArranque.AddHours((double)vm.HorasProgramadas.Value);
            }
        }

        private async Task<int> InsertarProgramaAsync(  PlaneacionProgramaCrearDesdeNecesidadVm vm, int usuarioId,SqlConnection cn, SqlTransaction tx)
        {
            var secuencia = await ObtenerSiguienteSecuenciaMaquinaAsync(
                vm.MaquinaID,
                cn,
                tx
            );

            const string sql = @"
INSERT INTO dbo.Planeacion_ProgramaProduccion
(
    ReleaseID,
    ReleaseDetalleID,

    ClienteID,
    ClienteNombre,

    ParteID,
    NumeroParte,
    ReferenciaSAP,
    DesignacionDescripcionSAP,

Color,

    CantidadRequerida,
    PiezasDesdePT,
    CantidadProgramada,
    CantidadProducida,

    MaquinaID,
    MaquinaCodigo,
    MaquinaNombre,

    MoldeID,
    MoldeCodigo,

    CondicionProduccion,
TipoOF,
MotivoTipoOF,
    SecuenciaMaquina,

    FechaInicioProgramada,
    FechaFinProgramada,
    HorasProgramadas,
Cambio,
Arranque,

    ObjetivoHora,
    Ciclo,
    Cavidades,
    PesoBrutoPieza,

    MaterialID,
    MaterialCodigo,
    MaterialDescripcion,
    CantidadMpKg,

    EmbalajeCodigo,
    EmbalajeDescripcion,
    PiezasPorEmbalaje,
    CantidadEmbalajes,

    EstatusID,
    Observaciones,
    UsuarioCreacionID,
    FechaCreacion,
    Activo
)
OUTPUT INSERTED.ProgramaProduccionID
VALUES
(
    @ReleaseID,
    @ReleaseDetalleID,

    @ClienteID,
    @ClienteNombre,

    @ParteID,
    @NumeroParte,
    @ReferenciaSAP,
    @DesignacionDescripcionSAP,

@Color,

    @CantidadRequerida,
    @PiezasDesdePT,
    @CantidadProgramada,
    0,

    @MaquinaID,
    @MaquinaCodigo,
    @MaquinaNombre,

    @MoldeID,
    @MoldeCodigo,

    @CondicionProduccion,
@TipoOF,
@MotivoTipoOF,
    @SecuenciaMaquina,

    @FechaInicioProgramada,
    @FechaFinProgramada,
    @HorasProgramadas,
@Cambio,
@Arranque,

    @ObjetivoHora,
    @Ciclo,
    @Cavidades,
    @PesoBrutoPieza,

    @MaterialID,
    @MaterialCodigo,
    @MaterialDescripcion,
    @CantidadMpKg,

    @EmbalajeCodigo,
    @EmbalajeDescripcion,
    @PiezasPorEmbalaje,
    @CantidadEmbalajes,

    @EstatusID,
    @Observaciones,
    @UsuarioCreacionID,
    GETDATE(),
    1
);";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@ReleaseID", SqlDbType.Int).Value = (object?)vm.ReleaseID ?? DBNull.Value;
            cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value = vm.ReleaseDetalleID;

            cmd.Parameters.Add("@ClienteID", SqlDbType.Int).Value = (object?)vm.ClienteID ?? DBNull.Value;
            cmd.Parameters.Add("@ClienteNombre", SqlDbType.NVarChar, 200).Value = (object?)vm.ClienteNombre ?? DBNull.Value;

            cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value = (object?)vm.ParteID ?? DBNull.Value;
            cmd.Parameters.Add("@NumeroParte", SqlDbType.NVarChar, 120).Value = (object?)vm.NumeroParte ?? DBNull.Value;
            cmd.Parameters.Add("@ReferenciaSAP", SqlDbType.NVarChar, 150).Value = (object?)vm.ReferenciaSAP ?? DBNull.Value;
            cmd.Parameters.Add("@DesignacionDescripcionSAP", SqlDbType.NVarChar, 300).Value = (object?)vm.DesignacionDescripcionSAP ?? DBNull.Value;

            cmd.Parameters.Add("@Color", SqlDbType.NVarChar, 100).Value =
    (object?)vm.Color ?? DBNull.Value;

            cmd.Parameters.Add("@CantidadRequerida", SqlDbType.Int).Value = vm.CantidadRequerida;
            cmd.Parameters.Add("@PiezasDesdePT", SqlDbType.Int).Value = vm.PiezasDesdePT;
            cmd.Parameters.Add("@CantidadProgramada", SqlDbType.Int).Value = vm.CantidadProgramada;

            cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = (object?)vm.MaquinaID ?? DBNull.Value;
            cmd.Parameters.Add("@MaquinaCodigo", SqlDbType.NVarChar, 100).Value = (object?)vm.MaquinaCodigo ?? DBNull.Value;
            cmd.Parameters.Add("@MaquinaNombre", SqlDbType.NVarChar, 200).Value = (object?)vm.MaquinaNombre ?? DBNull.Value;

            cmd.Parameters.Add("@MoldeID", SqlDbType.Int).Value = (object?)vm.MoldeID ?? DBNull.Value;
            cmd.Parameters.Add("@MoldeCodigo", SqlDbType.NVarChar, 100).Value = (object?)vm.MoldeCodigo ?? DBNull.Value;

            cmd.Parameters.Add("@CondicionProduccion", SqlDbType.NVarChar, 20).Value = (object?)vm.CondicionProduccion ?? DBNull.Value;
            cmd.Parameters.Add("@TipoOF", SqlDbType.NVarChar, 30).Value = "RELEASE";

            cmd.Parameters.Add("@MotivoTipoOF", SqlDbType.NVarChar, 500).Value = DBNull.Value;
            cmd.Parameters.Add("@SecuenciaMaquina", SqlDbType.Int).Value = (object?)secuencia ?? DBNull.Value;

            cmd.Parameters.Add("@FechaInicioProgramada", SqlDbType.DateTime).Value = (object?)vm.FechaInicioProgramada ?? DBNull.Value;
            cmd.Parameters.Add("@FechaFinProgramada", SqlDbType.DateTime).Value = (object?)vm.FechaFinProgramada ?? DBNull.Value;

            AddDecimal(cmd, "@HorasProgramadas", vm.HorasProgramadas, 18, 2);
            cmd.Parameters.Add("@Cambio", SqlDbType.Time).Value =
    (object?)vm.Cambio ?? DBNull.Value;

            cmd.Parameters.Add("@Arranque", SqlDbType.Time).Value =
                (object?)vm.Arranque ?? DBNull.Value;

            cmd.Parameters.Add("@ObjetivoHora", SqlDbType.Int).Value = (object?)vm.ObjetivoHora ?? DBNull.Value;
            cmd.Parameters.Add("@Ciclo", SqlDbType.NVarChar, 50).Value = (object?)vm.Ciclo ?? DBNull.Value;
            cmd.Parameters.Add("@Cavidades", SqlDbType.Int).Value = (object?)vm.Cavidades ?? DBNull.Value;

            AddDecimal(cmd, "@PesoBrutoPieza", vm.PesoBrutoPieza, 18, 6);

            cmd.Parameters.Add("@MaterialID", SqlDbType.Int).Value = (object?)vm.MaterialID ?? DBNull.Value;
            cmd.Parameters.Add("@MaterialCodigo", SqlDbType.NVarChar, 100).Value = (object?)vm.MaterialCodigo ?? DBNull.Value;
            cmd.Parameters.Add("@MaterialDescripcion", SqlDbType.NVarChar, 250).Value = (object?)vm.MaterialDescripcion ?? DBNull.Value;

            AddDecimal(cmd, "@CantidadMpKg", vm.CantidadMpKg, 18, 4);

            cmd.Parameters.Add("@EmbalajeCodigo", SqlDbType.NVarChar, 100).Value = (object?)vm.EmbalajeCodigo ?? DBNull.Value;
            cmd.Parameters.Add("@EmbalajeDescripcion", SqlDbType.NVarChar, 250).Value = (object?)vm.EmbalajeDescripcion ?? DBNull.Value;

            AddDecimal(cmd, "@PiezasPorEmbalaje", vm.PiezasPorEmbalaje, 18, 4);
            AddDecimal(cmd, "@CantidadEmbalajes", vm.CantidadEmbalajes, 18, 4);

            cmd.Parameters.Add("@EstatusID", SqlDbType.Int).Value = PlaneacionProgramaEstatus.Programado;
            cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 500).Value = (object?)vm.Observaciones ?? DBNull.Value;
            cmd.Parameters.Add("@UsuarioCreacionID", SqlDbType.Int).Value = usuarioId;

            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        private async Task<int?> ObtenerSiguienteSecuenciaMaquinaAsync(
            int? maquinaId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            if (!maquinaId.HasValue)
                return null;

            const string sql = @"
SELECT ISNULL(MAX(SecuenciaMaquina), 0) + 1
FROM dbo.Planeacion_ProgramaProduccion
WHERE MaquinaID = @MaquinaID
  AND Activo = 1
  AND EstatusID NOT IN (5, 9, 99);";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = maquinaId.Value;

            var result = await cmd.ExecuteScalarAsync();

            return result == null || result == DBNull.Value
                ? 1
                : Convert.ToInt32(result);
        }

        private async Task<bool> ReleaseDetalleYaProgramadoAsync(
            int releaseDetalleId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
SELECT COUNT(1)
FROM dbo.Planeacion_ProgramaProduccion
WHERE ReleaseDetalleID = @ReleaseDetalleID
  AND Activo = 1
  AND EstatusID NOT IN (99);";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value = releaseDetalleId;

            var result = Convert.ToInt32(await cmd.ExecuteScalarAsync());

            return result > 0;
        }

        private async Task MarcarReleaseDetalleProgramadoAsync(
            int releaseDetalleId,
            int programaId,
            int usuarioId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
UPDATE dbo.Planeacion_ReleaseDetalle
SET
    ProgramaProduccionID = @ProgramaProduccionID,
    FechaProgramado = GETDATE(),
    UsuarioProgramoID = @UsuarioProgramoID,
    EstatusID = 3,
    UsuarioModificacionID = @UsuarioProgramoID,
    FechaModificacion = GETDATE()
WHERE ReleaseDetalleID = @ReleaseDetalleID;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaId;
            cmd.Parameters.Add("@UsuarioProgramoID", SqlDbType.Int).Value = usuarioId;
            cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value = releaseDetalleId;

            await cmd.ExecuteNonQueryAsync();
        }


        private async Task VincularApartadoPTAProgramaAsync(
            int releaseDetalleId,
            int programaProduccionId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
IF OBJECT_ID('dbo.Planeacion_PT_Apartado', 'U') IS NULL
    RETURN;

UPDATE dbo.Planeacion_PT_Apartado
SET
    ProgramaProduccionID = @ProgramaProduccionID,
    FechaModificacion = GETDATE(),
    Observaciones = 'Apartado vinculado al Programa de Producción.'
WHERE ReleaseDetalleID = @ReleaseDetalleID
  AND Activo = 1
  AND EstatusID = 1;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaProduccionId;
            cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value = releaseDetalleId;
            await cmd.ExecuteNonQueryAsync();
        }

        private async Task VincularApartadoPTAOFAsync(
            int releaseDetalleId,
            int programaProduccionId,
            int solicitudProduccionId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
IF OBJECT_ID('dbo.Planeacion_PT_Apartado', 'U') IS NULL
    RETURN;

UPDATE dbo.Planeacion_PT_Apartado
SET
    ProgramaProduccionID = @ProgramaProduccionID,
    SolicitudProduccionID = @SolicitudProduccionID,
    FechaModificacion = GETDATE(),
    Observaciones = 'Apartado vinculado a la OF. Sigue reservado hasta que Almacén confirme salida/consumo.'
WHERE ReleaseDetalleID = @ReleaseDetalleID
  AND Activo = 1
  AND EstatusID = 1;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaProduccionId;
            cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = solicitudProduccionId;
            cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value = releaseDetalleId;
            await cmd.ExecuteNonQueryAsync();
        }

        private async Task<List<PlaneacionProgramaMaquinaVm>> ObtenerMaquinasAsync(SqlConnection cn)
        {
            var lista = new List<PlaneacionProgramaMaquinaVm>();

            const string sql = @"
SELECT
    MaquinaID,
    Codigo,
    Nombre
FROM dbo.ERP_Maquinas
WHERE Activo = 1
ORDER BY Codigo;";

            await using var cmd = new SqlCommand(sql, cn);
            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                lista.Add(new PlaneacionProgramaMaquinaVm
                {
                    MaquinaID = rd["MaquinaID"] == DBNull.Value ? null : Convert.ToInt32(rd["MaquinaID"]),
                    MaquinaCodigo = rd["Codigo"] as string ?? "",
                    MaquinaNombre = rd["Nombre"] as string ?? ""
                });
            }

            return lista;
        }

        private async Task<List<PlaneacionProgramaIndexVm>> ObtenerProgramasPorRangoAsync(
    DateTime fechaDesde,
    DateTime fechaHasta)
        {
            var lista = new List<PlaneacionProgramaIndexVm>();

            const string sql = @"
SELECT
    pp.ProgramaProduccionID,
    pp.ReleaseID,
pp.ReleaseDetalleID,
pp.SolicitudProduccionID,
pp.SolicitudProduccionDetalleID,
r.FolioRelease,

    pp.ClienteID,
    ISNULL(c.Nombre, pp.ClienteNombre) AS ClienteNombre,

    pp.ParteID,
    pp.NumeroParte,
    pp.ReferenciaSAP,
    pp.DesignacionDescripcionSAP,

pp.Color,

    pp.CantidadRequerida,
    pp.PiezasDesdePT,
    pp.CantidadProgramada,
    pp.CantidadProducida,
    pp.CantidadPendiente,

    pp.MaquinaID,
    pp.MaquinaCodigo,
    pp.MaquinaNombre,

    pp.MoldeID,
    pp.MoldeCodigo,

    pp.CondicionProduccion,
    pp.SecuenciaMaquina,

    pp.FechaInicioProgramada,
    pp.FechaFinProgramada,
    pp.HorasProgramadas,
pp.Cambio,
pp.Arranque,

    pp.EstatusID,
    pp.Observaciones,
    pp.FechaCreacion
FROM dbo.Planeacion_ProgramaProduccion pp
LEFT JOIN dbo.Planeacion_Releases r
    ON r.ReleaseID = pp.ReleaseID
LEFT JOIN dbo.ERP_Clientes c
    ON c.ClienteID = pp.ClienteID
WHERE pp.Activo = 1
  AND (
        pp.FechaInicioProgramada < DATEADD(DAY, 1, @FechaHasta)
    AND ISNULL(pp.FechaFinProgramada, pp.FechaInicioProgramada) >= @FechaDesde
)
ORDER BY
    pp.MaquinaCodigo,
    pp.FechaInicioProgramada,
    pp.SecuenciaMaquina;";

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@FechaDesde", SqlDbType.Date).Value = fechaDesde.Date;
            cmd.Parameters.Add("@FechaHasta", SqlDbType.Date).Value = fechaHasta.Date;

            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                lista.Add(MapPrograma(rd));
            }

            return lista;
        }

        private static PlaneacionProgramaIndexVm MapPrograma(SqlDataReader rd)
        {
            return new PlaneacionProgramaIndexVm
            {
                ProgramaProduccionID = Convert.ToInt32(rd["ProgramaProduccionID"]),

                ReleaseID = rd["ReleaseID"] == DBNull.Value ? null : Convert.ToInt32(rd["ReleaseID"]),
                ReleaseDetalleID = rd["ReleaseDetalleID"] == DBNull.Value ? null : Convert.ToInt32(rd["ReleaseDetalleID"]),

                SolicitudProduccionID = rd["SolicitudProduccionID"] == DBNull.Value ? null : Convert.ToInt32(rd["SolicitudProduccionID"]),
                SolicitudProduccionDetalleID = rd["SolicitudProduccionDetalleID"] == DBNull.Value ? null : Convert.ToInt32(rd["SolicitudProduccionDetalleID"]),

                FolioRelease = rd["FolioRelease"] as string,

                ClienteID = rd["ClienteID"] == DBNull.Value ? null : Convert.ToInt32(rd["ClienteID"]),
                ClienteNombre = rd["ClienteNombre"] as string,

                ParteID = rd["ParteID"] == DBNull.Value ? null : Convert.ToInt32(rd["ParteID"]),
                NumeroParte = rd["NumeroParte"] as string,
                ReferenciaSAP = rd["ReferenciaSAP"] as string,
                DesignacionDescripcionSAP = rd["DesignacionDescripcionSAP"] as string,

                Color = rd["Color"] as string,

                CantidadRequerida = Convert.ToInt32(rd["CantidadRequerida"]),
                PiezasDesdePT = Convert.ToInt32(rd["PiezasDesdePT"]),
                CantidadProgramada = Convert.ToInt32(rd["CantidadProgramada"]),
                CantidadProducida = Convert.ToInt32(rd["CantidadProducida"]),
                CantidadPendiente = Convert.ToInt32(rd["CantidadPendiente"]),

                MaquinaID = rd["MaquinaID"] == DBNull.Value ? null : Convert.ToInt32(rd["MaquinaID"]),
                MaquinaCodigo = rd["MaquinaCodigo"] as string,
                MaquinaNombre = rd["MaquinaNombre"] as string,

                MoldeID = rd["MoldeID"] == DBNull.Value ? null : Convert.ToInt32(rd["MoldeID"]),
                MoldeCodigo = rd["MoldeCodigo"] as string,

                CondicionProduccion = rd["CondicionProduccion"] as string,
                SecuenciaMaquina = rd["SecuenciaMaquina"] == DBNull.Value ? null : Convert.ToInt32(rd["SecuenciaMaquina"]),

                FechaInicioProgramada = rd["FechaInicioProgramada"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaInicioProgramada"]),
                FechaFinProgramada = rd["FechaFinProgramada"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaFinProgramada"]),
                HorasProgramadas = rd["HorasProgramadas"] == DBNull.Value ? null : Convert.ToDecimal(rd["HorasProgramadas"]),
                Cambio = rd["Cambio"] == DBNull.Value ? null : (TimeSpan)rd["Cambio"],
                Arranque = rd["Arranque"] == DBNull.Value ? null : (TimeSpan)rd["Arranque"],

                EstatusID = Convert.ToInt32(rd["EstatusID"]),
                Observaciones = rd["Observaciones"] as string,
                FechaCreacion = Convert.ToDateTime(rd["FechaCreacion"])
            };
        }


        private async Task CargarCatalogosAsync(PlaneacionProgramaCrearDesdeNecesidadVm vm)
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            var inicio = vm.FechaInicioProgramada ?? RedondearSiguienteBloque(DateTime.Now, 15);

            DateTime fin;

            if (vm.FechaFinProgramada.HasValue && vm.FechaFinProgramada.Value > inicio)
            {
                fin = vm.FechaFinProgramada.Value;
            }
            else if (vm.HorasProgramadas.HasValue && vm.HorasProgramadas.Value > 0)
            {
                fin = inicio.AddHours((double)vm.HorasProgramadas.Value);
            }
            else
            {
                fin = inicio.AddMinutes(1);
            }

            // Ya no deshabilitamos máquinas ocupadas de forma absoluta,
            // porque con I.P sí debe poder seleccionarse una máquina ocupada.
            // Solo se etiqueta como ocupada para que el usuario decida T.P o I.P.
            vm.Maquinas = await CargarMaquinasConEstadoAsync(
                cn,
                inicio,
                fin,
                vm.MaquinaID
            );

            vm.Moldes = await CargarSelectAsync(
                cn,
                @"SELECT 
              MoldeID AS Id,
              CodigoMolde AS Texto
          FROM dbo.ERP_Moldes
          WHERE Activo = 1
          ORDER BY CodigoMolde;"
            );

            vm.Operadores = await CargarSelectAsync(
                cn,
                @"SELECT 
              PersonaID AS Id,
              LTRIM(RTRIM(
                  ISNULL(Nombre, '') + ' ' +
                  ISNULL(ApellidoPaterno, '') + ' ' +
                  ISNULL(ApellidoMaterno, '')
              )) AS Texto
          FROM dbo.Persona
          WHERE EsColaboradorActivo = 1
            AND UPPER(LTRIM(RTRIM(Puesto))) = 'OPERADOR'
          ORDER BY Nombre, ApellidoPaterno, ApellidoMaterno;"
            );

            vm.Condiciones = PlaneacionProgramaCondicion.SelectList();
        }

        private async Task InsertarOperadoresProgramaAsync(
    int programaProduccionId,
    PlaneacionProgramaCrearDesdeNecesidadVm vm,
    int usuarioId,
    SqlConnection cn,
    SqlTransaction tx)
        {
            const string sqlDesactivar = @"
UPDATE dbo.Planeacion_ProgramaOperadores
SET
    Activo = 0,
    UsuarioModificacionID = @UsuarioModificacionID,
    FechaModificacion = GETDATE()
WHERE ProgramaProduccionID = @ProgramaProduccionID
  AND Activo = 1;";

            await using (var cmd = new SqlCommand(sqlDesactivar, cn, tx))
            {
                cmd.Parameters.Add("@UsuarioModificacionID", SqlDbType.Int).Value = usuarioId;
                cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaProduccionId;

                await cmd.ExecuteNonQueryAsync();
            }

            const string sqlInsert = @"
INSERT INTO dbo.Planeacion_ProgramaOperadores
(
    ProgramaProduccionID,
    PersonaID,
    RolOperador,
    Activo,
    UsuarioCreacionID,
    FechaCreacion
)
VALUES
(
    @ProgramaProduccionID,
    @PersonaID,
    @RolOperador,
    1,
    @UsuarioCreacionID,
    GETDATE()
);";

            async Task InsertarUnoAsync(int? personaId, string rol)
            {
                if (!personaId.HasValue || personaId.Value <= 0)
                    return;

                await using var cmd = new SqlCommand(sqlInsert, cn, tx);

                cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaProduccionId;
                cmd.Parameters.Add("@PersonaID", SqlDbType.Int).Value = personaId.Value;
                cmd.Parameters.Add("@RolOperador", SqlDbType.NVarChar, 30).Value = rol;
                cmd.Parameters.Add("@UsuarioCreacionID", SqlDbType.Int).Value = usuarioId;

                await cmd.ExecuteNonQueryAsync();
            }

            await InsertarUnoAsync(vm.OperadorPrincipalID, "PRINCIPAL");
            await InsertarUnoAsync(vm.OperadorAuxiliarID, "AUXILIAR");
        }

        private async Task<List<SelectListItem>> CargarMaquinasConEstadoAsync(
            SqlConnection cn,
            DateTime inicio,
            DateTime fin,
            int? maquinaSeleccionadaId)
        {
            const string sql = @"
SELECT
    m.MaquinaID AS Id,
    m.Codigo + ' | ' + ISNULL(m.Nombre, '') AS Texto,
    CAST(
        CASE
            WHEN EXISTS
            (
                SELECT 1
                FROM dbo.Planeacion_ProgramaProduccion pp
                WHERE pp.Activo = 1
                  AND pp.MaquinaID = m.MaquinaID
                  AND ISNULL(pp.EstatusID, 1) NOT IN (5, 9, 99)
                  AND pp.FechaInicioProgramada < @Fin
                  AND ISNULL(
                        pp.FechaFinProgramada,
                        DATEADD(MINUTE, CAST(CEILING(ISNULL(pp.HorasProgramadas, 1) * 60) AS INT), pp.FechaInicioProgramada)
                      ) > @Inicio
            )
            THEN 1
            ELSE 0
        END AS bit
    ) AS Ocupada
FROM dbo.ERP_Maquinas m
WHERE m.Activo = 1
ORDER BY m.Codigo;";

            var lista = new List<SelectListItem>();

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@Inicio", SqlDbType.DateTime).Value = inicio;
            cmd.Parameters.Add("@Fin", SqlDbType.DateTime).Value = fin;

            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                var id = Convert.ToInt32(rd["Id"]);
                var ocupada = rd["Ocupada"] != DBNull.Value && Convert.ToBoolean(rd["Ocupada"]);
                var texto = rd["Texto"]?.ToString() ?? id.ToString();

                lista.Add(new SelectListItem
                {
                    Value = id.ToString(),
                    Text = ocupada
                        ? texto + "  — OCUPADA: usar I.P si se va a interrumpir"
                        : texto + "  — LIBRE",
                    Disabled = false,
                    Selected = maquinaSeleccionadaId.HasValue &&
                               maquinaSeleccionadaId.Value == id
                });
            }

            return lista;
        }

        private static async Task<bool> CambioMoldeTieneCruceAsync(
    DateTime fechaCambio,
    SqlConnection cn,
    SqlTransaction? tx)
{
    const string sql = @"
SELECT TOP 1 1
FROM dbo.Planeacion_ProgramaProduccion pp
WHERE pp.Activo = 1
  AND ISNULL(pp.EstatusID, 1) NOT IN (5, 9, 99)
  AND pp.Cambio IS NOT NULL
  AND pp.Arranque IS NOT NULL
  AND pp.Cambio <> pp.Arranque
  AND CAST(pp.FechaInicioProgramada AS DATE) = CAST(@FechaCambio AS DATE)
  AND DATEPART(HOUR, pp.FechaInicioProgramada) = DATEPART(HOUR, @FechaCambio);";

    await using var cmd = new SqlCommand(sql, cn, tx);
    cmd.Parameters.Add("@FechaCambio", SqlDbType.DateTime).Value = fechaCambio;

    var result = await cmd.ExecuteScalarAsync();
    return result != null && result != DBNull.Value;
}



        private static async Task<bool> MoldeTieneCruceAsync(
            int moldeId,
            DateTime inicio,
            DateTime fin,
            int? maquinaActualId,
            SqlConnection cn,
            SqlTransaction? tx)
        {
            const string sql = @"
SELECT TOP 1 1
FROM dbo.Planeacion_ProgramaProduccion pp
WHERE pp.Activo = 1
  AND pp.MoldeID = @MoldeID
  AND (@MaquinaActualID IS NULL OR ISNULL(pp.MaquinaID, 0) <> @MaquinaActualID)
  AND ISNULL(pp.EstatusID, 1) NOT IN (5, 9, 99)
  AND pp.FechaInicioProgramada < @Fin
  AND ISNULL(
        pp.FechaFinProgramada,
        DATEADD(MINUTE, CAST(CEILING(ISNULL(pp.HorasProgramadas, 1) * 60) AS INT), pp.FechaInicioProgramada)
      ) > @Inicio;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@MoldeID", SqlDbType.Int).Value = moldeId;
            cmd.Parameters.Add("@MaquinaActualID", SqlDbType.Int).Value =
                (object?)maquinaActualId ?? DBNull.Value;
            cmd.Parameters.Add("@Inicio", SqlDbType.DateTime).Value = inicio;
            cmd.Parameters.Add("@Fin", SqlDbType.DateTime).Value = fin;

            var result = await cmd.ExecuteScalarAsync();
            return result != null && result != DBNull.Value;
        }

        private async Task<bool> MaquinaTieneCruceAsync(
            int maquinaId,
            DateTime inicio,
            DateTime fin)
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            return await MaquinaTieneCruceAsync(maquinaId, inicio, fin, cn, null);
        }

        private static async Task<bool> MaquinaTieneCruceAsync(
            int maquinaId,
            DateTime inicio,
            DateTime fin,
            SqlConnection cn,
            SqlTransaction? tx)
        {
            const string sql = @"
SELECT TOP 1 1
FROM dbo.Planeacion_ProgramaProduccion pp
WHERE pp.Activo = 1
  AND pp.MaquinaID = @MaquinaID
  AND ISNULL(pp.EstatusID, 1) NOT IN (5, 9, 99)
  AND pp.FechaInicioProgramada < @Fin
  AND ISNULL(
        pp.FechaFinProgramada,
        DATEADD(MINUTE, CAST(CEILING(ISNULL(pp.HorasProgramadas, 1) * 60) AS INT), pp.FechaInicioProgramada)
      ) > @Inicio;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = maquinaId;
            cmd.Parameters.Add("@Inicio", SqlDbType.DateTime).Value = inicio;
            cmd.Parameters.Add("@Fin", SqlDbType.DateTime).Value = fin;

            var result = await cmd.ExecuteScalarAsync();
            return result != null && result != DBNull.Value;
        }

        private static async Task InterrumpirProgramasCruzadosAsync(
            int maquinaId,
            DateTime nuevoInicio,
            DateTime nuevoFin,
            int usuarioId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
UPDATE pp
SET
    FechaFinProgramada =
        CASE
            WHEN pp.FechaInicioProgramada < @NuevoInicio THEN @NuevoInicio
            ELSE DATEADD(MINUTE, 1, pp.FechaInicioProgramada)
        END,
    EstatusID = @EstatusPausado,
    Observaciones =
        LEFT(
            ISNULL(pp.Observaciones, '') +
            CASE
                WHEN ISNULL(pp.Observaciones, '') = '' THEN ''
                ELSE CHAR(13) + CHAR(10)
            END +
            'Interrumpido por nuevo cambio de molde I.P el ' +
            CONVERT(NVARCHAR(16), GETDATE(), 120) +
            '. Nuevo inicio: ' +
            CONVERT(NVARCHAR(16), @NuevoInicio, 120) + '.',
            500
        ),
    UsuarioModificacionID = @UsuarioID,
    FechaModificacion = GETDATE()
FROM dbo.Planeacion_ProgramaProduccion pp
WHERE pp.Activo = 1
  AND pp.MaquinaID = @MaquinaID
  AND ISNULL(pp.EstatusID, 1) NOT IN (5, 9, 99)
  AND pp.FechaInicioProgramada < @NuevoFin
  AND ISNULL(
        pp.FechaFinProgramada,
        DATEADD(MINUTE, CAST(CEILING(ISNULL(pp.HorasProgramadas, 1) * 60) AS INT), pp.FechaInicioProgramada)
      ) > @NuevoInicio;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = maquinaId;
            cmd.Parameters.Add("@NuevoInicio", SqlDbType.DateTime).Value = nuevoInicio;
            cmd.Parameters.Add("@NuevoFin", SqlDbType.DateTime).Value = nuevoFin;
            cmd.Parameters.Add("@EstatusPausado", SqlDbType.Int).Value = PlaneacionProgramaEstatus.Pausado;
            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;

            await cmd.ExecuteNonQueryAsync();
        }




        private static async Task<List<SelectListItem>> CargarSelectAsync(SqlConnection cn, string sql)
        {
            var lista = new List<SelectListItem>();

            await using var cmd = new SqlCommand(sql, cn);
            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                lista.Add(new SelectListItem
                {
                    Value = rd["Id"].ToString(),
                    Text = rd["Texto"].ToString()
                });
            }

            return lista;
        }

        private int ObtenerUsuarioID()
        {
            return HttpContext.Session.GetInt32("UsuarioID") ?? 0;
        }

        private static void AddDecimal(
            SqlCommand cmd,
            string name,
            decimal? value,
            byte precision,
            byte scale)
        {
            var p = cmd.Parameters.Add(name, SqlDbType.Decimal);
            p.Precision = precision;
            p.Scale = scale;
            p.Value = value.HasValue ? value.Value : DBNull.Value;
        }


        private async Task<ProgramaParaOFVm?> ObtenerProgramaParaGenerarOFAsync(
     int programaProduccionId,
     SqlConnection cn,
     SqlTransaction tx)
        {
            const string sql = @"
SELECT
    pp.ProgramaProduccionID,

    pp.ReleaseID,
    pp.ReleaseDetalleID,

    pp.SolicitudProduccionID,
    pp.SolicitudProduccionDetalleID,

    pp.ClienteID,
    pp.ClienteNombre,

    pp.ParteID,
    pp.NumeroParte,
    pp.ReferenciaSAP,
    pp.DesignacionDescripcionSAP,

    ISNULL(NULLIF(pp.TipoOF, ''), 'RELEASE') AS TipoOF,
    pp.MotivoTipoOF,

    pp.CantidadRequerida,
    pp.PiezasDesdePT,
    pp.CantidadProgramada,

    pp.MaquinaID,
    pp.MaquinaCodigo,
    pp.MaquinaNombre,

    pp.MoldeID,
    pp.MoldeCodigo,

    pp.CondicionProduccion,
    pp.SecuenciaMaquina,

    pp.FechaInicioProgramada,
    pp.FechaFinProgramada,
    pp.HorasProgramadas,
    pp.Cambio,
    pp.Arranque,

    COALESCE(pp.ObjetivoHora, t.ObjetivoHora) AS ObjetivoHora,
    COALESCE(pp.Ciclo, t.Ciclo) AS Ciclo,
    COALESCE(pp.Cavidades, t.Cavidades) AS Cavidades,
    COALESCE(pp.PesoBrutoPieza, t.PesoBrutoPieza) AS PesoBrutoPieza,

    COALESCE(NULLIF(pp.Color, ''), t.Color) AS Color,
    t.PiezasPorCaja,
    t.TipoSecado,
    t.HorasSecado,

    COALESCE(pp.MaterialID, t.MaterialID) AS MaterialID,
    COALESCE(pp.MaterialCodigo, t.MaterialCodigo) AS MaterialCodigo,
    COALESCE(pp.MaterialDescripcion, t.MaterialDescripcion) AS MaterialDescripcion,
    pp.CantidadMpKg,

    COALESCE(pp.EmbalajeCodigo, t.EmbalajeCodigo) AS EmbalajeCodigo,
    COALESCE(pp.EmbalajeDescripcion, t.EmbalajeDescripcion) AS EmbalajeDescripcion,
    COALESCE(pp.PiezasPorEmbalaje, t.PiezasPorEmbalaje) AS PiezasPorEmbalaje,
    pp.CantidadEmbalajes,

    pp.Observaciones
FROM dbo.Planeacion_ProgramaProduccion pp
LEFT JOIN dbo.ERP_ParteDatosTecnicos t
    ON t.ParteID = pp.ParteID
   AND t.Activo = 1
WHERE pp.ProgramaProduccionID = @ProgramaProduccionID
  AND pp.Activo = 1;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaProduccionId;

            await using var rd = await cmd.ExecuteReaderAsync();

            if (!await rd.ReadAsync())
                return null;

            return new ProgramaParaOFVm
            {
                ProgramaProduccionID = Convert.ToInt32(rd["ProgramaProduccionID"]),

                ReleaseID = rd["ReleaseID"] == DBNull.Value ? null : Convert.ToInt32(rd["ReleaseID"]),
                ReleaseDetalleID = rd["ReleaseDetalleID"] == DBNull.Value ? null : Convert.ToInt32(rd["ReleaseDetalleID"]),

                SolicitudProduccionID = rd["SolicitudProduccionID"] == DBNull.Value ? null : Convert.ToInt32(rd["SolicitudProduccionID"]),
                SolicitudProduccionDetalleID = rd["SolicitudProduccionDetalleID"] == DBNull.Value ? null : Convert.ToInt32(rd["SolicitudProduccionDetalleID"]),

                ClienteID = rd["ClienteID"] == DBNull.Value ? null : Convert.ToInt32(rd["ClienteID"]),
                ClienteNombre = rd["ClienteNombre"] as string,

                ParteID = rd["ParteID"] == DBNull.Value ? null : Convert.ToInt32(rd["ParteID"]),
                NumeroParte = rd["NumeroParte"] as string,
                ReferenciaSAP = rd["ReferenciaSAP"] as string,
                DesignacionDescripcionSAP = rd["DesignacionDescripcionSAP"] as string,

                TipoOF = rd["TipoOF"] == DBNull.Value
                    ? "RELEASE"
                    : NormalizarTipoOF(rd["TipoOF"] as string),

                MotivoTipoOF = rd["MotivoTipoOF"] as string,

                CantidadRequerida = Convert.ToInt32(rd["CantidadRequerida"]),
                PiezasDesdePT = Convert.ToInt32(rd["PiezasDesdePT"]),
                CantidadProgramada = Convert.ToInt32(rd["CantidadProgramada"]),

                MaquinaID = rd["MaquinaID"] == DBNull.Value ? null : Convert.ToInt32(rd["MaquinaID"]),
                MaquinaCodigo = rd["MaquinaCodigo"] as string,
                MaquinaNombre = rd["MaquinaNombre"] as string,

                MoldeID = rd["MoldeID"] == DBNull.Value ? null : Convert.ToInt32(rd["MoldeID"]),
                MoldeCodigo = rd["MoldeCodigo"] as string,

                CondicionProduccion = rd["CondicionProduccion"] as string,
                SecuenciaMaquina = rd["SecuenciaMaquina"] == DBNull.Value ? null : Convert.ToInt32(rd["SecuenciaMaquina"]),

                FechaInicioProgramada = rd["FechaInicioProgramada"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaInicioProgramada"]),
                FechaFinProgramada = rd["FechaFinProgramada"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaFinProgramada"]),
                HorasProgramadas = rd["HorasProgramadas"] == DBNull.Value ? null : Convert.ToDecimal(rd["HorasProgramadas"]),
                Cambio = rd["Cambio"] == DBNull.Value ? null : (TimeSpan)rd["Cambio"],
                Arranque = rd["Arranque"] == DBNull.Value ? null : (TimeSpan)rd["Arranque"],

                ObjetivoHora = rd["ObjetivoHora"] == DBNull.Value ? null : Convert.ToInt32(rd["ObjetivoHora"]),
                Ciclo = rd["Ciclo"] == DBNull.Value ? null : rd["Ciclo"].ToString(),
                Cavidades = rd["Cavidades"] == DBNull.Value ? null : Convert.ToInt32(rd["Cavidades"]),
                PesoBrutoPieza = rd["PesoBrutoPieza"] == DBNull.Value ? null : Convert.ToDecimal(rd["PesoBrutoPieza"]),

                MaterialID = rd["MaterialID"] == DBNull.Value ? null : Convert.ToInt32(rd["MaterialID"]),
                MaterialCodigo = rd["MaterialCodigo"] as string,
                MaterialDescripcion = rd["MaterialDescripcion"] as string,
                CantidadMpKg = rd["CantidadMpKg"] == DBNull.Value ? null : Convert.ToDecimal(rd["CantidadMpKg"]),

                EmbalajeCodigo = rd["EmbalajeCodigo"] as string,
                EmbalajeDescripcion = rd["EmbalajeDescripcion"] as string,
                PiezasPorEmbalaje = rd["PiezasPorEmbalaje"] == DBNull.Value ? null : Convert.ToDecimal(rd["PiezasPorEmbalaje"]),
                CantidadEmbalajes = rd["CantidadEmbalajes"] == DBNull.Value ? null : Convert.ToDecimal(rd["CantidadEmbalajes"]),

                Observaciones = rd["Observaciones"] as string,

                Color = rd["Color"] as string,

                PiezasPorCaja = rd["PiezasPorCaja"] == DBNull.Value
                    ? null
                    : Convert.ToInt32(rd["PiezasPorCaja"]),

                TipoSecado = rd["TipoSecado"] as string,

                HorasSecado = rd["HorasSecado"] == DBNull.Value
                    ? null
                    : Convert.ToDecimal(rd["HorasSecado"])
            };
        }





        private async Task<string> GenerarFolioOFAsync(SqlConnection cn, SqlTransaction tx)
        {
            var yy = DateTime.Today.ToString("yy");

            const string sql = @"
SELECT ISNULL(MAX(TRY_CONVERT(INT, SUBSTRING(NumeroOFRecibida, 4, 4))), 0) + 1
FROM dbo.SolicitudesProduccion
WHERE NumeroOFRecibida LIKE 'OF-[0-9][0-9][0-9][0-9]/' + @YY;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@YY", SqlDbType.VarChar, 2).Value = yy;

            var consecutivo = Convert.ToInt32(await cmd.ExecuteScalarAsync());

            return $"OF-{consecutivo:0000}/{yy}";
        }


        private async Task<int> InsertarOFDedeProgramaAsync(
    ProgramaParaOFVm p,
    string folioOF,
    int usuarioId,
    SqlConnection cn,
    SqlTransaction tx)
        {
            const string sql = @"
INSERT INTO dbo.SolicitudesProduccion
(
    FolioSolicitud,
    NumeroOFRecibida,
    FechaSolicitud,
    FechaRequerida,
    ClienteID,
    ClienteNombre,
    OrigenSolicitud,
    Prioridad,
    TipoOF,
    MotivoTipoOF,
    EstatusID,
    NotasGenerales,
    UsuarioCreacionID,
    FechaCreacion,
    Activo,
    FechaInicioPlaneada,
    FechaFinPlaneada,
    ResponsablePlaneacionUsuarioID,
    ResponsablePlaneacionNombre,
    CostoMPTotal,
    CostoEmbalajeTotal,
    CostoTotalOF,
    VentaTotalOF,
    UtilidadEstimadaOF,
    MonedaCosto,
    ReleaseID,
    ReleaseDetalleID,
    ProgramaProduccionID,
    OrigenOF
)
OUTPUT INSERTED.SolicitudProduccionID
VALUES
(
    @FolioSolicitud,
    @NumeroOFRecibida,
    GETDATE(),
    @FechaRequerida,
    @ClienteID,
    @ClienteNombre,
    @OrigenSolicitud,
    @Prioridad,
    @TipoOF,
    @MotivoTipoOF,
    @EstatusID,
    @NotasGenerales,
    @UsuarioCreacionID,
    GETDATE(),
    1,
    @FechaInicioPlaneada,
    @FechaFinPlaneada,
    @ResponsablePlaneacionUsuarioID,
    @ResponsablePlaneacionNombre,
    0,
    0,
    0,
    0,
    0,
    @MonedaCosto,
    @ReleaseID,
    @ReleaseDetalleID,
    @ProgramaProduccionID,
    @OrigenOF
);";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@FolioSolicitud", SqlDbType.NVarChar, 40).Value = folioOF;
            cmd.Parameters.Add("@NumeroOFRecibida", SqlDbType.NVarChar, 80).Value = folioOF;

            cmd.Parameters.Add("@FechaRequerida", SqlDbType.Date).Value =
                (object?)p.FechaFinProgramada?.Date ?? DBNull.Value;

            cmd.Parameters.Add("@ClienteID", SqlDbType.Int).Value =
                (object?)p.ClienteID ?? DBNull.Value;

            cmd.Parameters.Add("@ClienteNombre", SqlDbType.NVarChar, 200).Value =
                (object?)p.ClienteNombre ?? DBNull.Value;

            cmd.Parameters.Add("@OrigenSolicitud", SqlDbType.NVarChar, 50).Value = "Planeación Programa";
            cmd.Parameters.Add("@Prioridad", SqlDbType.NVarChar, 30).Value = "Normal";

            cmd.Parameters.Add("@TipoOF", SqlDbType.NVarChar, 30).Value = "RELEASE";

            cmd.Parameters.Add("@MotivoTipoOF", SqlDbType.NVarChar, 500).Value = DBNull.Value;

            cmd.Parameters.Add("@EstatusID", SqlDbType.Int).Value = PlaneacionOFEstatus.PendienteValidacionMP;

            cmd.Parameters.Add("@NotasGenerales", SqlDbType.NVarChar, 500).Value =
                (object?)$"OF generada desde Programa de Producción ID {p.ProgramaProduccionID}. {p.Observaciones}" ?? DBNull.Value;

            cmd.Parameters.Add("@UsuarioCreacionID", SqlDbType.Int).Value = usuarioId;

            cmd.Parameters.Add("@FechaInicioPlaneada", SqlDbType.DateTime).Value =
                (object?)p.FechaInicioProgramada ?? DBNull.Value;

            cmd.Parameters.Add("@FechaFinPlaneada", SqlDbType.DateTime).Value =
                (object?)p.FechaFinProgramada ?? DBNull.Value;

            cmd.Parameters.Add("@ResponsablePlaneacionUsuarioID", SqlDbType.Int).Value = usuarioId;

            cmd.Parameters.Add("@ResponsablePlaneacionNombre", SqlDbType.NVarChar, 200).Value =
                User?.Identity?.Name ?? "Sistema";

            cmd.Parameters.Add("@MonedaCosto", SqlDbType.NVarChar, 10).Value = "MXN";

            cmd.Parameters.Add("@ReleaseID", SqlDbType.Int).Value =
                (object?)p.ReleaseID ?? DBNull.Value;

            cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value =
                (object?)p.ReleaseDetalleID ?? DBNull.Value;

            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = p.ProgramaProduccionID;

            cmd.Parameters.Add("@OrigenOF", SqlDbType.NVarChar, 30).Value = "PROGRAMA";

            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }


        private async Task<int> InsertarDetalleOFDedeProgramaAsync(
    int solicitudProduccionId,
    ProgramaParaOFVm p,
    int usuarioId,
    SqlConnection cn,
    SqlTransaction tx)
        {
            const string sql = @"
INSERT INTO dbo.SolicitudesProduccionDetalle
(
    SolicitudProduccionID,
    Renglon,
    ParteID,
    MoldeID,
    MaquinaSugeridaID,
    DesignacionDescripcionSAP,
    ReferenciaSAP,
    CantidadPiezas,
    HorasPlaneadas,
    NumeroMoldeTexto,
    MaquinaSugeridaTexto,
    Color,
    Cavidades,
    ObjetivoHora,
    PiezasPorCaja,
    Notas,
    EstatusID,
    Activo,
    MaterialID,
    OrigenSurtido,
    PTDisponibleAlCrear,
    MPDisponibleKgAlCrear,
    AlmacenValidado,
    MensajeAlmacen,
    Ciclo,
    TipoSecado,
    HorasSecado,
    PesoBrutoPieza,
    MaterialCodigo,
    MaterialDescripcion,
    EmbalajeCodigo,
    EmbalajeDescripcion,
    PiezasPorEmbalaje,
    CantidadEmbalajes,
    CantidadMpKg,
    Cambio,
    Arranque,
    CostoMPUnitario,
    CostoMPTotal,
    MonedaCostoMP,
    UnidadCostoMP,
    CostoEmbalajeUnitario,
    CostoEmbalajeTotal,
    MonedaCostoEmbalaje,
    UnidadCostoEmbalaje,
    CostoTotalRenglon,
    PrecioVentaUnitario,
    VentaTotalRenglon,
    UtilidadEstimadaRenglon
)
OUTPUT INSERTED.SolicitudProduccionDetalleID
VALUES
(
    @SolicitudProduccionID,
    1,
    @ParteID,
    @MoldeID,
    @MaquinaSugeridaID,
    @DesignacionDescripcionSAP,
    @ReferenciaSAP,
    @CantidadPiezas,
    @HorasPlaneadas,
    @NumeroMoldeTexto,
    @MaquinaSugeridaTexto,
   @Color,
@Cavidades,
@ObjetivoHora,
@PiezasPorCaja,
    @Notas,
    @EstatusID,
    1,
    @MaterialID,
    @OrigenSurtido,
    @PTDisponibleAlCrear,
    @MPDisponibleKgAlCrear,
    0,
    @MensajeAlmacen,
    @Ciclo,
@TipoSecado,
@HorasSecado,
@PesoBrutoPieza,
    @MaterialCodigo,
    @MaterialDescripcion,
    @EmbalajeCodigo,
    @EmbalajeDescripcion,
    @PiezasPorEmbalaje,
    @CantidadEmbalajes,
    @CantidadMpKg,
    @Cambio,
@Arranque,
    0,
    0,
    'MXN',
    NULL,
    0,
    0,
    'MXN',
    NULL,
    0,
    0,
    0,
    0
);";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = solicitudProduccionId;

            cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value =
                (object?)p.ParteID ?? DBNull.Value;

            cmd.Parameters.Add("@MoldeID", SqlDbType.Int).Value =
                (object?)p.MoldeID ?? DBNull.Value;

            cmd.Parameters.Add("@MaquinaSugeridaID", SqlDbType.Int).Value =
                (object?)p.MaquinaID ?? DBNull.Value;

            cmd.Parameters.Add("@DesignacionDescripcionSAP", SqlDbType.NVarChar, 300).Value =
                (object?)p.DesignacionDescripcionSAP ?? DBNull.Value;

            cmd.Parameters.Add("@ReferenciaSAP", SqlDbType.NVarChar, 150).Value =
    !string.IsNullOrWhiteSpace(p.ReferenciaSAP)
        ? (object)p.ReferenciaSAP
        : !string.IsNullOrWhiteSpace(p.NumeroParte)
            ? (object)p.NumeroParte
            : DBNull.Value;

            cmd.Parameters.Add("@CantidadPiezas", SqlDbType.Int).Value = p.CantidadProgramada;

            AddDecimal(cmd, "@HorasPlaneadas", p.HorasProgramadas, 18, 2);

            cmd.Parameters.Add("@NumeroMoldeTexto", SqlDbType.NVarChar, 100).Value =
                (object?)p.MoldeCodigo ?? DBNull.Value;

            cmd.Parameters.Add("@MaquinaSugeridaTexto", SqlDbType.NVarChar, 200).Value =
                (object?)($"{p.MaquinaCodigo} {p.MaquinaNombre}".Trim()) ?? DBNull.Value;

            cmd.Parameters.Add("@Cavidades", SqlDbType.Int).Value =
                (object?)p.Cavidades ?? DBNull.Value;

            cmd.Parameters.Add("@ObjetivoHora", SqlDbType.Int).Value =
                (object?)p.ObjetivoHora ?? DBNull.Value;

            cmd.Parameters.Add("@Notas", SqlDbType.NVarChar, 500).Value =
                (object?)$"Generado desde programa ID {p.ProgramaProduccionID}. Condición: {p.CondicionProduccion}. {p.Observaciones}" ?? DBNull.Value;

            cmd.Parameters.Add("@EstatusID", SqlDbType.Int).Value = PlaneacionOFEstatus.PendienteValidacionMP;

            cmd.Parameters.Add("@MaterialID", SqlDbType.Int).Value =
                (object?)p.MaterialID ?? DBNull.Value;

            cmd.Parameters.Add("@OrigenSurtido", SqlDbType.NVarChar, 30).Value =
                p.PiezasDesdePT > 0 ? "MIXTO" : "MP";

            cmd.Parameters.Add("@PTDisponibleAlCrear", SqlDbType.Int).Value = p.PiezasDesdePT;

            AddDecimal(cmd, "@MPDisponibleKgAlCrear", null, 18, 4);

            cmd.Parameters.Add("@MensajeAlmacen", SqlDbType.NVarChar, 500).Value =
                "OF generada desde programa. Validar surtido de MP/PT en almacén.";

            cmd.Parameters.Add("@Ciclo", SqlDbType.NVarChar, 50).Value =
                (object?)p.Ciclo ?? DBNull.Value;

            AddDecimal(cmd, "@PesoBrutoPieza", p.PesoBrutoPieza, 18, 6);

            cmd.Parameters.Add("@MaterialCodigo", SqlDbType.NVarChar, 100).Value =
                (object?)p.MaterialCodigo ?? DBNull.Value;

            cmd.Parameters.Add("@MaterialDescripcion", SqlDbType.NVarChar, 250).Value =
                (object?)p.MaterialDescripcion ?? DBNull.Value;

            cmd.Parameters.Add("@EmbalajeCodigo", SqlDbType.NVarChar, 100).Value =
                (object?)p.EmbalajeCodigo ?? DBNull.Value;

            cmd.Parameters.Add("@EmbalajeDescripcion", SqlDbType.NVarChar, 250).Value =
                (object?)p.EmbalajeDescripcion ?? DBNull.Value;

            cmd.Parameters.Add("@Color", SqlDbType.NVarChar, 100).Value =
    (object?)p.Color ?? DBNull.Value;

            cmd.Parameters.Add("@PiezasPorCaja", SqlDbType.Int).Value =
                (object?)p.PiezasPorCaja ?? DBNull.Value;

            cmd.Parameters.Add("@TipoSecado", SqlDbType.NVarChar, 100).Value =
                (object?)p.TipoSecado ?? DBNull.Value;

            AddDecimal(cmd, "@HorasSecado", p.HorasSecado, 18, 2);

            AddDecimal(cmd, "@PiezasPorEmbalaje", p.PiezasPorEmbalaje, 18, 4);
            AddDecimal(cmd, "@CantidadEmbalajes", p.CantidadEmbalajes, 18, 4);
            AddDecimal(cmd, "@CantidadMpKg", p.CantidadMpKg, 18, 4);

            cmd.Parameters.Add("@Cambio", SqlDbType.Time).Value =
    (object?)p.Cambio ?? DBNull.Value;

            cmd.Parameters.Add("@Arranque", SqlDbType.Time).Value =
                (object?)p.Arranque ?? DBNull.Value;


            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }



        private sealed class CambioMoldeSugerencia
        {
            public DateTime Cambio { get; set; }
            public DateTime Arranque { get; set; }
            public bool OmiteHoraCambio { get; set; }
            public string Motivo { get; set; } = "Se considera 1 hora de preparación entre cambio y arranque.";
        }

        private async Task<CambioMoldeSugerencia> ObtenerSiguienteCambioDisponibleAsync(
            int maquinaId,
            DateTime fechaBase,
            int? parteId,
            int? moldeId)
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            return await ObtenerSiguienteCambioDisponibleAsync(
                maquinaId,
                fechaBase,
                parteId,
                moldeId,
                cn,
                null
            );
        }

        private static async Task<CambioMoldeSugerencia> ObtenerSiguienteCambioDisponibleAsync(
            int maquinaId,
            DateTime fechaBase,
            int? parteId,
            int? moldeId,
            SqlConnection cn,
            SqlTransaction? tx)
        {
            var baseRedondeada = RedondearSiguienteBloque(fechaBase, 15);

            const string sql = @"
SELECT TOP (1)
    pp.ProgramaProduccionID,
    pp.ParteID,
    pp.NumeroParte,
    pp.ReferenciaSAP,
    pp.MoldeID,
    pp.MoldeCodigo,
    ISNULL(
        pp.FechaFinProgramada,
        DATEADD(MINUTE, CAST(CEILING(ISNULL(pp.HorasProgramadas, 1) * 60) AS INT), pp.FechaInicioProgramada)
    ) AS FechaFinProgramada
FROM dbo.Planeacion_ProgramaProduccion pp
WHERE pp.Activo = 1
  AND pp.MaquinaID = @MaquinaID
  AND ISNULL(pp.EstatusID, 1) NOT IN (5, 9, 99)
  AND ISNULL(
        pp.FechaFinProgramada,
        DATEADD(MINUTE, CAST(CEILING(ISNULL(pp.HorasProgramadas, 1) * 60) AS INT), pp.FechaInicioProgramada)
      ) >= @FechaBase
ORDER BY
    ISNULL(
        pp.FechaFinProgramada,
        DATEADD(MINUTE, CAST(CEILING(ISNULL(pp.HorasProgramadas, 1) * 60) AS INT), pp.FechaInicioProgramada)
    ) DESC,
    pp.ProgramaProduccionID DESC;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = maquinaId;
            cmd.Parameters.Add("@FechaBase", SqlDbType.DateTime).Value = baseRedondeada;

            await using var rd = await cmd.ExecuteReaderAsync();

            if (!await rd.ReadAsync())
            {
                var cambioLibre = baseRedondeada;
                return new CambioMoldeSugerencia
                {
                    Cambio = cambioLibre,
                    Arranque = cambioLibre.AddHours(1),
                    OmiteHoraCambio = false,
                    Motivo = "Máquina libre. Se considera 1 hora de preparación antes del arranque."
                };
            }

            var finCola = Convert.ToDateTime(rd["FechaFinProgramada"]);
            var cambio = RedondearSiguienteBloque(finCola > baseRedondeada ? finCola : baseRedondeada, 15);

            var parteAnteriorId = rd["ParteID"] == DBNull.Value ? (int?)null : Convert.ToInt32(rd["ParteID"]);
            var moldeAnteriorId = rd["MoldeID"] == DBNull.Value ? (int?)null : Convert.ToInt32(rd["MoldeID"]);
            var parteAnterior = (rd["ReferenciaSAP"] as string) ?? (rd["NumeroParte"] as string) ?? "la pieza anterior";
            var moldeAnterior = (rd["MoldeCodigo"] as string) ?? "el molde anterior";

            var mismaParte = parteId.HasValue && parteAnteriorId.HasValue && parteId.Value == parteAnteriorId.Value;
            var mismoMolde = moldeId.HasValue && moldeAnteriorId.HasValue && moldeId.Value == moldeAnteriorId.Value;

            if (mismaParte)
            {
                return new CambioMoldeSugerencia
                {
                    Cambio = cambio,
                    Arranque = cambio,
                    OmiteHoraCambio = true,
                    Motivo = $"La máquina continúa con la misma pieza ({parteAnterior}); se omite la hora de cambio."
                };
            }

            if (mismoMolde)
            {
                return new CambioMoldeSugerencia
                {
                    Cambio = cambio,
                    Arranque = cambio,
                    OmiteHoraCambio = true,
                    Motivo = $"La máquina conserva el mismo molde ({moldeAnterior}); se omite la hora de cambio."
                };
            }

            return new CambioMoldeSugerencia
            {
                Cambio = cambio,
                Arranque = cambio.AddHours(1),
                OmiteHoraCambio = false,
                Motivo = "La máquina tiene cola o requiere preparación; se considera 1 hora entre cambio y arranque."
            };
        }


        private static string NormalizarTipoOF(string? tipoOF)
        {
            if (string.IsNullOrWhiteSpace(tipoOF))
                return "RELEASE";

            var valor = tipoOF.Trim().ToUpperInvariant();

            valor = valor
                .Replace("Á", "A")
                .Replace("É", "E")
                .Replace("Í", "I")
                .Replace("Ó", "O")
                .Replace("Ú", "U");

            return valor switch
            {
                "RELEASE" => "RELEASE",
                "ENSAMBLE" => "ENSAMBLE",
                "PRUEBA" => "PRUEBA",
                "MP EXTRA" => "MP EXTRA",
                "MPEXTRA" => "MP EXTRA",
                "MP_EXTRA" => "MP EXTRA",
                _ => "RELEASE"
            };
        }

        private static bool TipoOFRequiereMotivo(string? tipoOF)
        {
            var valor = NormalizarTipoOF(tipoOF);

            return valor == "PRUEBA" ||
                   valor == "MP EXTRA";
        }

        private void ValidarTipoOF(string? tipoOF, string? motivoTipoOF, string campoMotivo)
        {
            var valor = NormalizarTipoOF(tipoOF);

            if (valor != "RELEASE" &&
                valor != "ENSAMBLE" &&
                valor != "PRUEBA" &&
                valor != "MP EXTRA")
            {
                ModelState.AddModelError(nameof(PlaneacionProgramaCrearDesdeNecesidadVm.TipoOF), "Selecciona un tipo de OF válido.");
            }

            if (TipoOFRequiereMotivo(valor) &&
                string.IsNullOrWhiteSpace(motivoTipoOF))
            {
                ModelState.AddModelError(campoMotivo, "Captura el motivo para este tipo de OF.");
            }
        }



        private static DateTime RedondearSiguienteHora(DateTime fecha)
{
    var redondeada = new DateTime(
        fecha.Year,
        fecha.Month,
        fecha.Day,
        fecha.Hour,
        0,
        0
    );

    if (fecha.Minute == 0 &&
        fecha.Second == 0 &&
        fecha.Millisecond == 0)
    {
        return redondeada;
    }

    return redondeada.AddHours(1);
}

private static DateTime RedondearSiguienteBloque(DateTime fecha, int minutosBloque)
{
    
    // hora completa, sin minutos.
    return RedondearSiguienteHora(fecha);
}

private static bool EsHoraCompleta(TimeSpan hora)
{
    return hora.Minutes == 0 &&
           hora.Seconds == 0 &&
           hora.Milliseconds == 0;
}

        private static DateTime CalcularFechaHoraDesdeHora(DateTime fechaBase, TimeSpan? hora)
        {
            if (!hora.HasValue)
                return fechaBase;

            return fechaBase.Date.Add(hora.Value);
        }


        private async Task InsertarAsignacionMaquinaOFDedeProgramaAsync(
    int solicitudProduccionDetalleId,
    ProgramaParaOFVm p,
    int usuarioId,
    SqlConnection cn,
    SqlTransaction tx)
        {
            if (!p.MaquinaID.HasValue)
                return;

            const string sql = @"
INSERT INTO dbo.SolicitudesProduccionAsignacionMaquina
(
    SolicitudProduccionDetalleID,
    MaquinaID,
    MoldeID,
    CantidadAsignada,
    HorasEstimadas,
    Secuencia,
    CondicionProduccion,
    FechaProgramadaTentativa,
    HoraInicioTentativa,
    HoraFinTentativa,
    EstatusID,
    Observaciones,
    UsuarioCreacionID,
    FechaCreacion,
    Activo
)
VALUES
(
    @SolicitudProduccionDetalleID,
    @MaquinaID,
    @MoldeID,
    @CantidadAsignada,
    @HorasEstimadas,
    @Secuencia,
    @CondicionProduccion,
    @FechaProgramadaTentativa,
    @HoraInicioTentativa,
    @HoraFinTentativa,
    @EstatusID,
    @Observaciones,
    @UsuarioCreacionID,
    GETDATE(),
    1
);";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@SolicitudProduccionDetalleID", SqlDbType.Int).Value = solicitudProduccionDetalleId;
            cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = p.MaquinaID.Value;

            cmd.Parameters.Add("@MoldeID", SqlDbType.Int).Value =
                (object?)p.MoldeID ?? DBNull.Value;

            cmd.Parameters.Add("@CantidadAsignada", SqlDbType.Int).Value = p.CantidadProgramada;

            AddDecimal(cmd, "@HorasEstimadas", p.HorasProgramadas, 18, 2);

            cmd.Parameters.Add("@Secuencia", SqlDbType.Int).Value =
                (object?)p.SecuenciaMaquina ?? 1;

            cmd.Parameters.Add("@CondicionProduccion", SqlDbType.NVarChar, 20).Value =
                (object?)p.CondicionProduccion ?? DBNull.Value;

            cmd.Parameters.Add("@FechaProgramadaTentativa", SqlDbType.Date).Value =
                (object?)p.FechaInicioProgramada?.Date ?? DBNull.Value;

            cmd.Parameters.Add("@HoraInicioTentativa", SqlDbType.Time).Value =
                (object?)p.FechaInicioProgramada?.TimeOfDay ?? DBNull.Value;

            cmd.Parameters.Add("@HoraFinTentativa", SqlDbType.Time).Value =
                (object?)p.FechaFinProgramada?.TimeOfDay ?? DBNull.Value;

            cmd.Parameters.Add("@EstatusID", SqlDbType.Int).Value = PlaneacionOFEstatus.PendienteValidacionMP;

            cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 500).Value =
                (object?)$"Asignación generada desde programa ID {p.ProgramaProduccionID}." ?? DBNull.Value;

            cmd.Parameters.Add("@UsuarioCreacionID", SqlDbType.Int).Value = usuarioId;

            await cmd.ExecuteNonQueryAsync();
        }

        private async Task MarcarProgramaConOFAsync(
    int programaProduccionId,
    int solicitudProduccionId,
    int solicitudProduccionDetalleId,
    int usuarioId,
    SqlConnection cn,
    SqlTransaction tx)
        {
            const string sql = @"
UPDATE dbo.Planeacion_ProgramaProduccion
SET
    SolicitudProduccionID = @SolicitudProduccionID,
    SolicitudProduccionDetalleID = @SolicitudProduccionDetalleID,
    FechaGeneracionOF = GETDATE(),
    UsuarioGeneroOFID = @UsuarioGeneroOFID,
    UsuarioModificacionID = @UsuarioGeneroOFID,
    FechaModificacion = GETDATE()
WHERE ProgramaProduccionID = @ProgramaProduccionID;";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = solicitudProduccionId;
            cmd.Parameters.Add("@SolicitudProduccionDetalleID", SqlDbType.Int).Value = solicitudProduccionDetalleId;
            cmd.Parameters.Add("@UsuarioGeneroOFID", SqlDbType.Int).Value = usuarioId;
            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaProduccionId;

            await cmd.ExecuteNonQueryAsync();
        }

        private async Task MarcarReleaseDetalleConOFAsync(
    int releaseDetalleId,
    int solicitudProduccionId,
    SqlConnection cn,
    SqlTransaction tx)
        {
            const string sql = @"
UPDATE dbo.Planeacion_ReleaseDetalle
SET
    SolicitudProduccionID = @SolicitudProduccionID,
    EstatusID = 4,
    FechaModificacion = GETDATE()
WHERE ReleaseDetalleID = @ReleaseDetalleID;";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = solicitudProduccionId;
            cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value = releaseDetalleId;

            await cmd.ExecuteNonQueryAsync();
        }



        // CALENDARIO_MAQUINAS_HELPERS_V1_0
        private static bool EsInstanteOperativoCalendario(DateTime fecha)
        {
            return fecha.DayOfWeek switch
            {
                DayOfWeek.Monday => fecha.TimeOfDay >= TimeSpan.FromHours(7),
                DayOfWeek.Tuesday => true,
                DayOfWeek.Wednesday => true,
                DayOfWeek.Thursday => true,
                DayOfWeek.Friday => true,
                DayOfWeek.Saturday => fecha.TimeOfDay < TimeSpan.FromHours(15),
                _ => false
            };
        }

        private static DateTime SiguienteAperturaCalendario(DateTime fecha)
        {
            var value = fecha;

            if (value.DayOfWeek == DayOfWeek.Monday &&
                value.TimeOfDay < TimeSpan.FromHours(7))
            {
                return value.Date.AddHours(7);
            }

            if (value.DayOfWeek == DayOfWeek.Saturday &&
                value.TimeOfDay >= TimeSpan.FromHours(15))
            {
                return value.Date.AddDays(2).AddHours(7);
            }

            if (value.DayOfWeek == DayOfWeek.Sunday)
            {
                return value.Date.AddDays(1).AddHours(7);
            }

            return value;
        }

        private static DateTime FinVentanaOperativaCalendario(DateTime fecha)
        {
            return fecha.DayOfWeek switch
            {
                DayOfWeek.Monday => fecha.Date.AddDays(1),
                DayOfWeek.Tuesday => fecha.Date.AddDays(1),
                DayOfWeek.Wednesday => fecha.Date.AddDays(1),
                DayOfWeek.Thursday => fecha.Date.AddDays(1),
                DayOfWeek.Friday => fecha.Date.AddDays(1),
                DayOfWeek.Saturday => fecha.Date.AddHours(15),
                _ => SiguienteAperturaCalendario(fecha)
            };
        }

        private static DateTime SumarHorasOperativasCalendario(
            DateTime inicio,
            decimal horas)
        {
            if (horas <= 0)
                return inicio;

            var cursor = SiguienteAperturaCalendario(inicio);
            var restantes = horas;
            var guard = 0;

            while (restantes > 0.0001m)
            {
                guard++;
                if (guard > 1000)
                    throw new InvalidOperationException("No fue posible calcular el fin operativo del programa.");

                cursor = SiguienteAperturaCalendario(cursor);
                var finVentana = FinVentanaOperativaCalendario(cursor);
                var disponibles = (decimal)(finVentana - cursor).TotalHours;

                if (disponibles <= 0)
                {
                    cursor = SiguienteAperturaCalendario(finVentana.AddMinutes(1));
                    continue;
                }

                if (restantes <= disponibles)
                    return cursor.AddHours((double)restantes);

                restantes -= disponibles;
                cursor = SiguienteAperturaCalendario(finVentana);
            }

            return cursor;
        }

        private static decimal CalcularHorasOperativasCalendario(
            DateTime inicio,
            DateTime fin)
        {
            if (fin <= inicio)
                return 0;

            decimal total = 0;
            var date = inicio.Date;
            var lastDate = fin.Date;

            while (date <= lastDate)
            {
                DateTime? apertura = date.DayOfWeek switch
                {
                    DayOfWeek.Monday => date.AddHours(7),
                    DayOfWeek.Tuesday => date,
                    DayOfWeek.Wednesday => date,
                    DayOfWeek.Thursday => date,
                    DayOfWeek.Friday => date,
                    DayOfWeek.Saturday => date,
                    _ => null
                };

                DateTime? cierre = date.DayOfWeek switch
                {
                    DayOfWeek.Monday => date.AddDays(1),
                    DayOfWeek.Tuesday => date.AddDays(1),
                    DayOfWeek.Wednesday => date.AddDays(1),
                    DayOfWeek.Thursday => date.AddDays(1),
                    DayOfWeek.Friday => date.AddDays(1),
                    DayOfWeek.Saturday => date.AddHours(15),
                    _ => null
                };

                if (apertura.HasValue && cierre.HasValue)
                {
                    var desde = inicio > apertura.Value ? inicio : apertura.Value;
                    var hasta = fin < cierre.Value ? fin : cierre.Value;

                    if (hasta > desde)
                        total += (decimal)(hasta - desde).TotalHours;
                }

                date = date.AddDays(1);
            }

            return Math.Round(total, 4);
        }

        public sealed class CalendarioMaquinasMoverRequest
        {
            public int ProgramaProduccionID { get; set; }
            public int MaquinaID { get; set; }
            public DateTime Inicio { get; set; }
            public decimal DuracionBloqueHoras { get; set; }
            public bool Redimensionado { get; set; }
            public bool ForzarMaquina { get; set; }
        }
        private class ProgramaParaOFVm
        {
            public int ProgramaProduccionID { get; set; }

            public int? ReleaseID { get; set; }
            public int? ReleaseDetalleID { get; set; }

            public int? SolicitudProduccionID { get; set; }
            public int? SolicitudProduccionDetalleID { get; set; }

            public int? ClienteID { get; set; }
            public string? ClienteNombre { get; set; }

            public int? ParteID { get; set; }
            public string? NumeroParte { get; set; }
            public string? ReferenciaSAP { get; set; }
            public string? DesignacionDescripcionSAP { get; set; }

            public int CantidadRequerida { get; set; }
            public int PiezasDesdePT { get; set; }
            public int CantidadProgramada { get; set; }

            public int? MaquinaID { get; set; }
            public string? MaquinaCodigo { get; set; }
            public string? MaquinaNombre { get; set; }

            public int? MoldeID { get; set; }
            public string? MoldeCodigo { get; set; }

            public string? CondicionProduccion { get; set; }
            public int? SecuenciaMaquina { get; set; }

            public DateTime? FechaInicioProgramada { get; set; }
            public DateTime? FechaFinProgramada { get; set; }
            public decimal? HorasProgramadas { get; set; }

            public int? ObjetivoHora { get; set; }
            public string? Ciclo { get; set; }
            public int? Cavidades { get; set; }
            public decimal? PesoBrutoPieza { get; set; }

            public int? MaterialID { get; set; }
            public string? MaterialCodigo { get; set; }
            public string? MaterialDescripcion { get; set; }
            public decimal? CantidadMpKg { get; set; }

            public string? EmbalajeCodigo { get; set; }
            public string? EmbalajeDescripcion { get; set; }
            public decimal? PiezasPorEmbalaje { get; set; }
            public decimal? CantidadEmbalajes { get; set; }

            public string? Observaciones { get; set; }

            public string? Color { get; set; }
            public int? PiezasPorCaja { get; set; }

            public string? TipoSecado { get; set; }
            public decimal? HorasSecado { get; set; }

            public TimeSpan? Cambio { get; set; }
            public TimeSpan? Arranque { get; set; }

            public string? TipoOF { get; set; } = "RELEASE";
            public string? MotivoTipoOF { get; set; }


        }
    }





}