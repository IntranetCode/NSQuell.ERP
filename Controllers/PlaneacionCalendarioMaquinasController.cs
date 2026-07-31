using ERP.NSQuell.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Globalization;

namespace ERP.NSQuell.Controllers
{
    [Route("PlaneacionCalendarioMaquinas")]
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
                "No se encontró la cadena de conexión DefaultConnection.");

        private static class EstatusPrograma
        {
            public const int Programado = 1;
            public const int EnPreparacion = 2;
            public const int EnProduccion = 3;
            public const int Pausado = 4;
            public const int Terminado = 5;
            public const int Cerrado = 9;
            public const int Cancelado = 99;
        }

        [HttpGet("")]
        [HttpGet("Index")]
        public async Task<IActionResult> Index(
            string? vista,
            DateTime? fecha,
            DateTime? rangoInicio,
            DateTime? rangoFin)
        {
            if (!UsuarioEnSesion())
                return RedirectToAction("Login", "Login");

            var periodo = ResolverPeriodo(vista, fecha, rangoInicio, rangoFin);

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            var maquinas = await ObtenerMaquinasCalendarioAsync(
                periodo.Inicio,
                periodo.Fin,
                cn);

            var vm = new PlaneacionCalendarioMaquinasVm
            {
                Vista = periodo.Vista,
                InicioPeriodo = periodo.Inicio,
                FinPeriodo = periodo.Fin,
                FechaReferencia = fecha,
                RangoInicio = periodo.RangoInicio,
                RangoFin = periodo.RangoFin,
                Ahora = DateTime.Now,
                Maquinas = maquinas
            };

            return View(vm);
        }

        [HttpGet("MaquinasCompatibles")]
        public async Task<IActionResult> MaquinasCompatibles(int programaProduccionId)
        {
            if (programaProduccionId <= 0)
            {
                return Json(new
                {
                    ok = false,
                    mensaje = "No se recibió el programa de producción."
                });
            }

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            try
            {
                var programa = await ObtenerProgramaBaseAsync(
                    programaProduccionId,
                    cn,
                    null,
                    bloquear: false);

                if (programa == null)
                {
                    return Json(new
                    {
                        ok = false,
                        mensaje = "No se encontró el programa."
                    });
                }

                var maquinas = await ObtenerMaquinasCompatiblesAsync(
                    programa,
                    cn,
                    null);

                return Json(new
                {
                    ok = true,
                    maquinas = maquinas.Select(x => new
                    {
                        maquinaID = x.MaquinaID,
                        codigo = x.Codigo,
                        nombre = x.Nombre
                    })
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    ok = false,
                    mensaje = "No fue posible consultar máquinas compatibles: " + ex.Message
                });
            }
        }

        [HttpPost("ReprogramarCalendario")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReprogramarCalendario(
            [FromBody] CalendarioMaquinasMoverRequest request)
        {
            if (!UsuarioEnSesion())
            {
                return Json(new
                {
                    ok = false,
                    mensaje = "La sesión terminó. Vuelve a iniciar sesión para continuar."
                });
            }

            if (request == null ||
                request.ProgramaProduccionID <= 0 ||
                request.MaquinaID <= 0)
            {
                return Json(new
                {
                    ok = false,
                    mensaje = "Los datos del movimiento están incompletos."
                });
            }

            var usuarioId = ObtenerUsuarioID();

            if (usuarioId <= 0)
            {
                return Json(new
                {
                    ok = false,
                    mensaje = "No se pudo identificar al usuario."
                });
            }

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            await using var tx =
                (SqlTransaction)await cn.BeginTransactionAsync(
                    IsolationLevel.Serializable);

            try
            {
                await TomarCandadoCalendarioAsync(cn, tx);
                await ActivarReacomodoPlaneacionAsync(cn, tx);

                var programa = await ObtenerProgramaBaseAsync(
                    request.ProgramaProduccionID,
                    cn,
                    tx,
                    bloquear: true);

                if (programa == null)
                {
                    await tx.RollbackAsync();

                    return Json(new
                    {
                        ok = false,
                        mensaje = "No se encontró el programa."
                    });
                }

                if (!PuedeMover(programa.EstatusID))
                {
                    await tx.RollbackAsync();

                    return Json(new
                    {
                        ok = false,
                        mensaje =
                            "Este programa ya no se puede mover. Está en producción, terminado, cerrado o cancelado."
                    });
                }

                var compatibles = await ObtenerMaquinasCompatiblesAsync(
                    programa,
                    cn,
                    tx);

                var maquinaDestino = compatibles.FirstOrDefault(
                    x => x.MaquinaID == request.MaquinaID);

                if (maquinaDestino == null)
                {
                    await tx.RollbackAsync();

                    var maquinasPermitidas = compatibles.Any()
                        ? string.Join(", ", compatibles.Select(x => x.Codigo))
                        : "sin máquinas compatibles configuradas";

                    return Json(new
                    {
                        ok = false,
                        mensaje =
                            "No se puede mover este programa a esa máquina. " +
                            "La máquina destino no está configurada como principal ni sustituta directa para esta parte. " +
                            "Máquinas permitidas: " + maquinasPermitidas + ".",
                        maquinasPermitidas = compatibles.Select(x => new
                        {
                            maquinaID = x.MaquinaID,
                            codigo = x.Codigo,
                            nombre = x.Nombre
                        })
                    });
                }

                /*
                    REGLA:
                    El punto donde se suelta el bloque NO es una hora exacta.
                    Es una posición de cola.

                    Si lo sueltas entre Programa 1 y Programa 2:
                    - se inserta después del Programa 1,
                    - se calcula cambio / arranque / fin,
                    - se reacomodan los posteriores desde la posición original de inserción,
                    - pero el cursor de acomodo empieza en el fin del programa insertado.
                */
                var puntoCola = NormalizarFecha(request.Inicio);

                if (puntoCola < DateTime.Now)
                    puntoCola = DateTime.Now;

                puntoCola = SiguienteAperturaOperativa(
                    puntoCola,
                    request.TrabajarDomingo);

                var horasProduccion = programa.HorasProgramadas > 0
                    ? programa.HorasProgramadas
                    : 1m;

                var anteriorCola = await ObtenerProgramaAnteriorPorPuntoAsync(
                    maquinaDestino.MaquinaID,
                    programa.ProgramaProduccionID,
                    puntoCola,
                    cn,
                    tx);

                var desdeReacomodo = anteriorCola == null
                    ? puntoCola
                    : anteriorCola.Fin;

                if (desdeReacomodo < DateTime.Now)
                    desdeReacomodo = DateTime.Now;

                desdeReacomodo = SiguienteAperturaOperativa(
                    desdeReacomodo,
                    request.TrabajarDomingo);

                var calculoCola = await CalcularPosicionCompactaAsync(
                    maquinaDestino.MaquinaID,
                    programa.ProgramaProduccionID,
                    programa.ParteID,
                    programa.MoldeID,
                    anteriorCola == null ? null : anteriorCola.ParteID,
                    anteriorCola == null ? null : anteriorCola.MoldeID,
                    desdeReacomodo,
                    horasProduccion,
                    cn,
                    tx,
                    request.TrabajarDomingo);

                var fechaCambio = calculoCola.Cambio;
                var fechaArranque = calculoCola.Arranque;
                var fechaFin = calculoCola.Fin;
                var horasCambio = calculoCola.HorasCambio;

                var nuevaSecuencia = await ObtenerSiguienteSecuenciaAsync(
                    maquinaDestino.MaquinaID,
                    programa.ProgramaProduccionID,
                    cn,
                    tx);

                var programasQueSeRecorreran =
                    await ContarProgramasPosterioresReacomodablesAsync(
                        maquinaDestino.MaquinaID,
                        programa.ProgramaProduccionID,
                        desdeReacomodo,
                        cn,
                        tx);

                var resumen =
                    ConstruirResumenMovimientoCompacto(
                        programa,
                        maquinaDestino,
                        anteriorCola,
                        fechaCambio,
                        fechaArranque,
                        fechaFin,
                        calculoCola.MoldeLiberado,
                        horasCambio,
                        programasQueSeRecorreran);

                var anteriorOrigenCola = programa.MaquinaID.HasValue
                    ? await ObtenerProgramaAnteriorPorPuntoAsync(
                        programa.MaquinaID.Value,
                        programa.ProgramaProduccionID,
                        programa.FechaInicioProgramada,
                        cn,
                        tx)
                    : null;

                if (!request.ConfirmarMovimiento)
                {
                    await tx.RollbackAsync();

                    return Json(new
                    {
                        ok = true,
                        requiereConfirmacion = true,
                        mensaje = "Confirma el movimiento calculado.",
                        resumen,
                        maquinaID = maquinaDestino.MaquinaID,
                        maquinaCodigo = maquinaDestino.Codigo,
                        maquinaNombre = maquinaDestino.Nombre,
                        cambio = fechaCambio.ToString(
                            "yyyy-MM-ddTHH:mm:ss",
                            CultureInfo.InvariantCulture),
                        cambioTexto = fechaCambio.ToString("dd/MM/yyyy HH:mm"),
                        arranque = fechaArranque.ToString(
                            "yyyy-MM-ddTHH:mm:ss",
                            CultureInfo.InvariantCulture),
                        arranqueTexto = fechaArranque.ToString("dd/MM/yyyy HH:mm"),
                        fin = fechaFin.ToString(
                            "yyyy-MM-ddTHH:mm:ss",
                            CultureInfo.InvariantCulture),
                        finTexto = fechaFin.ToString("dd/MM/yyyy HH:mm"),
                        horasProgramadas = Math.Round(horasProduccion, 2)
                    });
                }

                await ActualizarProgramaAsync(
                    programa,
                    maquinaDestino,
                    fechaCambio,
                    fechaArranque,
                    fechaFin,
                    horasProduccion,
                    nuevaSecuencia,
                    usuarioId,
                    cn,
                    tx);

                /*
                    CORRECCIÓN IMPORTANTE:

                    Antes se buscaban los posteriores desde fechaFin.
                    Eso podía brincar programas que estaban entre desdeReacomodo y fechaFin.

                    Ahora:
                    - desdeSeleccion = desdeReacomodo  -> desde dónde buscar posteriores.
                    - cursorInicial  = fechaFin        -> desde dónde empezar a acomodarlos.
                */
                var programasReacomodados = await ReacomodarColaPosteriorAsync(
                    maquinaDestino.MaquinaID,
                    programa.ProgramaProduccionID,
                    desdeReacomodo,
                    fechaFin,
                    programa.ParteID,
                    programa.MoldeID,
                    usuarioId,
                    cn,
                    tx,
                    request.TrabajarDomingo);

                /*
                    Si cambió de máquina, también compactamos la cola de la máquina origen
                    para cerrar el hueco que deja el programa movido.
                */
                if (programa.MaquinaID.HasValue &&
                    programa.MaquinaID.Value != maquinaDestino.MaquinaID)
                {
                    var desdeSeleccionOrigen = programa.FechaInicioProgramada;

                    var cursorOrigen = anteriorOrigenCola == null
                        ? programa.FechaInicioProgramada
                        : anteriorOrigenCola.Fin;

                    programasReacomodados += await ReacomodarColaPosteriorAsync(
                        programa.MaquinaID.Value,
                        programa.ProgramaProduccionID,
                        desdeSeleccionOrigen,
                        cursorOrigen,
                        anteriorOrigenCola == null ? null : anteriorOrigenCola.ParteID,
                        anteriorOrigenCola == null ? null : anteriorOrigenCola.MoldeID,
                        usuarioId,
                        cn,
                        tx,
                        request.TrabajarDomingo);
                }

                await SincronizarDocumentosRelacionadosAsync(
                    programa,
                    maquinaDestino,
                    fechaCambio,
                    fechaArranque,
                    fechaFin,
                    horasProduccion,
                    cn,
                    tx);

                await InsertarHistorialMovimientoAsync(
                    programa,
                    maquinaDestino,
                    fechaCambio,
                    fechaArranque,
                    fechaFin,
                    horasProduccion,
                    usuarioId,
                    resumen,
                    cn,
                    tx);

                await ReordenarSecuenciasAsync(
                    programa.MaquinaID,
                    maquinaDestino.MaquinaID,
                    cn,
                    tx);

                // Temporalmente no validamos cruces globales viejos.
                // REACTIVAR_CANDADO_CRUCES:
                // await ValidarProgramaSinCrucesAsync(cn, tx);

                await DesactivarReacomodoPlaneacionAsync(cn, tx);

                await tx.CommitAsync();

                return Json(new
                {
                    ok = true,
                    requiereConfirmacion = false,
                    mensaje = programasReacomodados > 0
                        ? "Programa movido correctamente. Se reacomodaron " +
                          programasReacomodados +
                          " programa(s) posterior(es) para cerrar espacios de cola."
                        : "Programa movido correctamente a la cola sin dejar espacios.",
                    resumen,
                    maquinaID = maquinaDestino.MaquinaID,
                    maquinaCodigo = maquinaDestino.Codigo,
                    maquinaNombre = maquinaDestino.Nombre,
                    cambioTexto = fechaCambio.ToString("dd/MM/yyyy HH:mm"),
                    arranqueTexto = fechaArranque.ToString("dd/MM/yyyy HH:mm"),
                    finTexto = fechaFin.ToString("dd/MM/yyyy HH:mm"),
                    horasProgramadas = Math.Round(horasProduccion, 2)
                });
            }
            catch (SqlException ex) when (
                ex.Number == 51001 ||
                ex.Number == 51002 ||
                ex.Number == 51003 ||
                ex.Number == 51010)
            {
                await RollbackSeguroAsync(tx);

                return Json(new
                {
                    ok = false,
                    mensaje = ex.Message
                });
            }
            catch (InvalidOperationException ex)
            {
                await RollbackSeguroAsync(tx);

                return Json(new
                {
                    ok = false,
                    mensaje = ex.Message
                });
            }
            catch (Exception ex)
            {
                await RollbackSeguroAsync(tx);

                return Json(new
                {
                    ok = false,
                    mensaje = "No fue posible mover el programa: " + ex.Message
                });
            }
            finally
            {
                await LimpiarContextoSinTransaccionAsync(cn);
            }
        }


        private async Task<List<PlaneacionCalendarioMaquinaVm>>
            ObtenerMaquinasCalendarioAsync(
                DateTime inicio,
                DateTime fin,
                SqlConnection cn)
        {
            var maquinas = new List<PlaneacionCalendarioMaquinaVm>();

            const string sqlMaquinas = @"
SELECT
    MaquinaID,
    Codigo,
    Nombre
FROM dbo.ERP_Maquinas
WHERE Activo = 1
ORDER BY Codigo, Nombre;";

            await using (var cmd = new SqlCommand(sqlMaquinas, cn))
            await using (var rd = await cmd.ExecuteReaderAsync())
            {
                while (await rd.ReadAsync())
                {
                    maquinas.Add(new PlaneacionCalendarioMaquinaVm
                    {
                        MaquinaID = Entero(rd, "MaquinaID"),
                        Codigo = Texto(rd, "Codigo") ?? "-",
                        Nombre = Texto(rd, "Nombre") ?? "-",
                        Bloques = new List<PlaneacionCalendarioBloqueVm>()
                    });
                }
            }

            const string sqlProgramas = @"
SELECT
    pp.ProgramaProduccionID,
    pp.MaquinaID,
    pp.MaquinaCodigo,
    pp.MaquinaNombre,

    pp.ParteID,
    pp.NumeroParte,
    pp.ReferenciaSAP,
    pp.DesignacionDescripcionSAP AS DescripcionParte,

    pp.MoldeID,
    pp.MoldeCodigo,

    pp.ReleaseDetalleID,
    pp.SolicitudProduccionID,
    pp.SolicitudProduccionDetalleID,

    pp.FechaInicioProgramada,
    ISNULL
    (
        pp.FechaFinProgramada,
        DATEADD
        (
            MINUTE,
            CAST(CEILING(ISNULL(pp.HorasProgramadas, 1) * 60) AS INT),
            pp.FechaInicioProgramada
        )
    ) AS FechaFinProgramada,

    ISNULL(pp.HorasProgramadas, 0) AS HorasProgramadas,
    pp.Cambio,
    pp.Arranque,

    ISNULL(pp.CantidadProgramada, 0) AS CantidadProgramada,
    ISNULL(pp.CantidadProducida, 0) AS CantidadProducida,
    ISNULL(pp.EstatusID, 1) AS EstatusID,

    ISNULL(c.Nombre, r.ClienteNombre) AS ClienteNombre,
    ISNULL(NULLIF(r.FolioRelease, ''), 'Programa') AS FolioRelease,

    t.MaquinaPrincipalID,
    mp.Codigo AS MaquinaPrincipalCodigo,
    mp.Nombre AS MaquinaPrincipalNombre,

    t.MaquinaSustitutaID,
    ms.Codigo AS MaquinaSustitutaCodigo,
    ms.Nombre AS MaquinaSustitutaNombre,

    pe.EjecucionProduccionID,
    pe.EstatusID AS EstatusProduccionID,
    pe.OperadorNombre,

    CAST(NULL AS INT) AS EscalaAsignacionID,
    CAST(NULL AS NVARCHAR(200)) AS TurnoProgramadoNombre

FROM dbo.Planeacion_ProgramaProduccion pp

LEFT JOIN dbo.Planeacion_ReleaseDetalle rd
    ON rd.ReleaseDetalleID = pp.ReleaseDetalleID

LEFT JOIN dbo.Planeacion_Releases r
    ON r.ReleaseID = rd.ReleaseID

LEFT JOIN dbo.ERP_Clientes c
    ON c.ClienteID = r.ClienteID

LEFT JOIN dbo.ERP_ParteDatosTecnicos t
    ON t.ParteID = pp.ParteID
   AND t.Activo = 1

LEFT JOIN dbo.ERP_Maquinas mp
    ON mp.MaquinaID = t.MaquinaPrincipalID

LEFT JOIN dbo.ERP_Maquinas ms
    ON ms.MaquinaID = t.MaquinaSustitutaID

OUTER APPLY
(
    SELECT TOP (1)
        e.EjecucionProduccionID,
        e.EstatusID,
        e.OperadorNombre
    FROM dbo.Produccion_Ejecucion e
    WHERE e.ProgramaProduccionID = pp.ProgramaProduccionID
      AND e.Activo = 1
    ORDER BY e.EjecucionProduccionID DESC
) pe

WHERE pp.Activo = 1
  AND pp.MaquinaID IS NOT NULL
  AND pp.FechaInicioProgramada IS NOT NULL
  AND pp.FechaInicioProgramada < @Fin
  AND ISNULL
      (
          pp.FechaFinProgramada,
          DATEADD
          (
              MINUTE,
              CAST(CEILING(ISNULL(pp.HorasProgramadas, 1) * 60) AS INT),
              pp.FechaInicioProgramada
          )
      ) > @Inicio
ORDER BY
    pp.MaquinaID,
    pp.FechaInicioProgramada,
    pp.SecuenciaMaquina,
    pp.ProgramaProduccionID;";

            var bloques = new List<PlaneacionCalendarioBloqueVm>();

            await using (var cmd = new SqlCommand(sqlProgramas, cn))
            {
                cmd.Parameters.Add("@Inicio", SqlDbType.DateTime).Value = inicio;
                cmd.Parameters.Add("@Fin", SqlDbType.DateTime).Value = fin;

                await using var rd = await cmd.ExecuteReaderAsync();

                while (await rd.ReadAsync())
                {
                    var estatusId = Entero(rd, "EstatusID");
                    var estatusProduccionId =
                        NullableEntero(rd, "EstatusProduccionID");

                    var inicioPrograma = Fecha(rd, "FechaInicioProgramada");
                    var finPrograma = Fecha(rd, "FechaFinProgramada");

                    var cantidadProgramada =
                        Entero(rd, "CantidadProgramada");

                    var cantidadProducida =
                        Entero(rd, "CantidadProducida");

                    var bloque = new PlaneacionCalendarioBloqueVm
                    {
                        ProgramaProduccionID =
                            Entero(rd, "ProgramaProduccionID"),

                        SolicitudProduccionID =
                            NullableEntero(rd, "SolicitudProduccionID"),

                        MaquinaID =
                            NullableEntero(rd, "MaquinaID") ?? 0,

                        MaquinaCodigo =
                            Texto(rd, "MaquinaCodigo") ?? string.Empty,

                        ClienteNombre =
                            Texto(rd, "ClienteNombre") ?? string.Empty,

                        NumeroParte =
                            Texto(rd, "NumeroParte") ?? string.Empty,

                        ReferenciaSAP =
                            Texto(rd, "ReferenciaSAP") ?? string.Empty,

                        Descripcion =
                            Texto(rd, "DescripcionParte") ?? string.Empty,

                        MoldeCodigo =
                            Texto(rd, "MoldeCodigo") ?? string.Empty,

                        CantidadProgramada = cantidadProgramada,
                        CantidadProducida = cantidadProducida,

                        Inicio = inicioPrograma,
                        Fin = finPrograma,

                        HorasProgramadas =
                            Decimal(rd, "HorasProgramadas"),

                        Cambio =
                            NullableTiempo(rd, "Cambio"),

                        Arranque =
                            NullableTiempo(rd, "Arranque"),

                        EstatusID = estatusId,

                        EstaEnLinea =
                            estatusProduccionId == EstatusPrograma.EnProduccion ||
                            (DateTime.Now >= inicioPrograma &&
                             DateTime.Now < finPrograma &&
                             estatusId != EstatusPrograma.Terminado &&
                             estatusId != EstatusPrograma.Cerrado &&
                             estatusId != EstatusPrograma.Cancelado),

                        MaquinaPrincipalID =
                            NullableEntero(rd, "MaquinaPrincipalID"),

                        MaquinaPrincipalCodigo =
                            Texto(rd, "MaquinaPrincipalCodigo") ?? string.Empty,

                        MaquinaPrincipalNombre =
                            Texto(rd, "MaquinaPrincipalNombre") ?? string.Empty,

                        MaquinaSustitutaID =
                            NullableEntero(rd, "MaquinaSustitutaID"),

                        MaquinaSustitutaCodigo =
                            Texto(rd, "MaquinaSustitutaCodigo") ?? string.Empty,

                        MaquinaSustitutaNombre =
                            Texto(rd, "MaquinaSustitutaNombre") ?? string.Empty,

                        EstatusProduccionID =
                            estatusProduccionId,

                        EstatusProduccionNombre =
                            NombreEstatusProduccion(estatusProduccionId),

                        EjecucionProduccionID =
                            NullableEntero(rd, "EjecucionProduccionID"),

                        OperadorProgramadoNombre =
                            Texto(rd, "OperadorNombre") ?? string.Empty,

                        TurnoProgramadoNombre =
                            Texto(rd, "TurnoProgramadoNombre") ?? string.Empty,

                        EscalaAsignacionID =
                            NullableEntero(rd, "EscalaAsignacionID")
                    };

                    bloques.Add(bloque);
                }
            }

            foreach (var maquina in maquinas)
            {
                maquina.Bloques = bloques
                    .Where(x => x.MaquinaID == maquina.MaquinaID)
                    .OrderBy(x => x.Inicio)
                    .ThenBy(x => x.ProgramaProduccionID)
                    .ToList();

                AsignarCarriles(maquina);
            }

            return maquinas;
        }

        private static void AsignarCarriles(
            PlaneacionCalendarioMaquinaVm maquina)
        {
            var finPorCarril = new List<DateTime>();

            foreach (var bloque in maquina.Bloques.OrderBy(x => x.Inicio))
            {
                var carril = -1;

                for (var i = 0; i < finPorCarril.Count; i++)
                {
                    if (finPorCarril[i] <= bloque.Inicio)
                    {
                        carril = i;
                        break;
                    }
                }

                if (carril < 0)
                {
                    carril = finPorCarril.Count;
                    finPorCarril.Add(bloque.Fin);
                }
                else
                {
                    finPorCarril[carril] = bloque.Fin;
                }

                bloque.Carril = carril;
            }

            maquina.Carriles = Math.Max(1, finPorCarril.Count);
        }

        // ============================================================
        // DATOS DEL PROGRAMA Y COMPATIBILIDAD
        // ============================================================

        private async Task<ProgramaBase?> ObtenerProgramaBaseAsync(
            int programaProduccionId,
            SqlConnection cn,
            SqlTransaction? tx,
            bool bloquear)
        {
            var lockSql = bloquear
                ? " WITH (UPDLOCK, HOLDLOCK)"
                : string.Empty;

            var sql = $@"
SELECT
    pp.ProgramaProduccionID,
    pp.MaquinaID,
    pp.MaquinaCodigo,
    pp.MaquinaNombre,

    pp.ParteID,
    pp.NumeroParte,
    pp.ReferenciaSAP,
    pp.DesignacionDescripcionSAP AS DescripcionParte,

    pp.MoldeID,
    pp.MoldeCodigo,

    pp.ReleaseDetalleID,
    pp.SolicitudProduccionID,
    pp.SolicitudProduccionDetalleID,

    pp.FechaInicioProgramada,
    ISNULL
    (
        pp.FechaFinProgramada,
        DATEADD
        (
            MINUTE,
            CAST(CEILING(ISNULL(pp.HorasProgramadas, 1) * 60) AS INT),
            pp.FechaInicioProgramada
        )
    ) AS FechaFinProgramada,

    ISNULL(pp.HorasProgramadas, 0) AS HorasProgramadas,
    pp.Cambio,
    pp.Arranque,
    ISNULL(pp.EstatusID, 1) AS EstatusID,

    t.MaquinaPrincipalID,
    t.MaquinaSustitutaID

FROM dbo.Planeacion_ProgramaProduccion pp{lockSql}

LEFT JOIN dbo.ERP_ParteDatosTecnicos t
    ON t.ParteID = pp.ParteID
   AND t.Activo = 1

WHERE pp.ProgramaProduccionID = @ProgramaProduccionID
  AND pp.Activo = 1;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add(
                "@ProgramaProduccionID",
                SqlDbType.Int).Value = programaProduccionId;

            await using var rd = await cmd.ExecuteReaderAsync();

            if (!await rd.ReadAsync())
                return null;

            return new ProgramaBase
            {
                ProgramaProduccionID =
                    Entero(rd, "ProgramaProduccionID"),

                MaquinaID =
                    NullableEntero(rd, "MaquinaID"),

                MaquinaCodigo =
                    Texto(rd, "MaquinaCodigo"),

                MaquinaNombre =
                    Texto(rd, "MaquinaNombre"),

                ParteID =
                    NullableEntero(rd, "ParteID"),

                NumeroParte =
                    Texto(rd, "NumeroParte"),

                ReferenciaSAP =
                    Texto(rd, "ReferenciaSAP"),

                DescripcionParte =
                    Texto(rd, "DescripcionParte"),

                MoldeID =
                    NullableEntero(rd, "MoldeID"),

                MoldeCodigo =
                    Texto(rd, "MoldeCodigo"),

                ReleaseDetalleID =
                    NullableEntero(rd, "ReleaseDetalleID"),

                SolicitudProduccionID =
                    NullableEntero(rd, "SolicitudProduccionID"),

                SolicitudProduccionDetalleID =
                    NullableEntero(rd, "SolicitudProduccionDetalleID"),

                FechaInicioProgramada =
                    Fecha(rd, "FechaInicioProgramada"),

                FechaFinProgramada =
                    Fecha(rd, "FechaFinProgramada"),

                HorasProgramadas =
                    Decimal(rd, "HorasProgramadas"),

                Cambio =
                    NullableTiempo(rd, "Cambio"),

                Arranque =
                    NullableTiempo(rd, "Arranque"),

                EstatusID =
                    Entero(rd, "EstatusID"),

                MaquinaPrincipalID =
                    NullableEntero(rd, "MaquinaPrincipalID"),

                MaquinaSustitutaID =
                    NullableEntero(rd, "MaquinaSustitutaID")
            };
        }

        private async Task<List<MaquinaCompatible>> ObtenerMaquinasCompatiblesAsync(
    ProgramaBase programa,
    SqlConnection cn,
    SqlTransaction? tx)
        {
            var lista = new List<MaquinaCompatible>();

            const string sql = @"
;WITH CompatiblesRaw AS
(
    -- Máquina actual del programa
    SELECT
        @MaquinaActualID AS MaquinaID,
        0 AS Prioridad
    WHERE @MaquinaActualID IS NOT NULL

    UNION ALL

    -- Máquina principal de datos técnicos
    SELECT
        @MaquinaPrincipalID AS MaquinaID,
        1 AS Prioridad
    WHERE @MaquinaPrincipalID IS NOT NULL

    UNION ALL

    -- Sustituta directa vieja de datos técnicos
    SELECT
        @MaquinaSustitutaID AS MaquinaID,
        2 AS Prioridad
    WHERE @MaquinaSustitutaID IS NOT NULL

    UNION ALL

    -- Relación directa: principal -> sustituta
    SELECT
        ms.MaquinaSustitutaID AS MaquinaID,
        ISNULL(ms.Prioridad, 999) + 10 AS Prioridad
    FROM dbo.ERP_MaquinasSustitutas ms
    WHERE ms.Activo = 1
      AND @MaquinaPrincipalID IS NOT NULL
      AND ms.MaquinaPrincipalID = @MaquinaPrincipalID

    UNION ALL

    -- Relación inversa directa: sustituta -> principal
    SELECT
        ms.MaquinaPrincipalID AS MaquinaID,
        ISNULL(ms.Prioridad, 999) + 20 AS Prioridad
    FROM dbo.ERP_MaquinasSustitutas ms
    WHERE ms.Activo = 1
      AND @MaquinaPrincipalID IS NOT NULL
      AND ms.MaquinaSustitutaID = @MaquinaPrincipalID
),
Compatibles AS
(
    SELECT
        MaquinaID,
        MIN(Prioridad) AS Prioridad
    FROM CompatiblesRaw
    WHERE MaquinaID IS NOT NULL
    GROUP BY MaquinaID
)
SELECT
    m.MaquinaID,
    ISNULL(m.Codigo, CONVERT(NVARCHAR(30), m.MaquinaID)) AS Codigo,
    ISNULL(m.Nombre, N'') AS Nombre,
    c.Prioridad
FROM Compatibles c
INNER JOIN dbo.ERP_Maquinas m
    ON m.MaquinaID = c.MaquinaID
   AND m.Activo = 1
ORDER BY
    c.Prioridad,
    m.Codigo,
    m.Nombre;";

            await using var cmd = tx == null
                ? new SqlCommand(sql, cn)
                : new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@MaquinaActualID", SqlDbType.Int).Value =
                programa.MaquinaID.HasValue
                    ? programa.MaquinaID.Value
                    : DBNull.Value;

            cmd.Parameters.Add("@MaquinaPrincipalID", SqlDbType.Int).Value =
                programa.MaquinaPrincipalID.HasValue
                    ? programa.MaquinaPrincipalID.Value
                    : DBNull.Value;

            cmd.Parameters.Add("@MaquinaSustitutaID", SqlDbType.Int).Value =
                programa.MaquinaSustitutaID.HasValue
                    ? programa.MaquinaSustitutaID.Value
                    : DBNull.Value;

            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                lista.Add(new MaquinaCompatible
                {
                    MaquinaID = Convert.ToInt32(rd["MaquinaID"]),
                    Codigo = rd["Codigo"] as string ?? "",
                    Nombre = rd["Nombre"] as string ?? ""
                });
            }

            return lista;
        }

        private async Task<DateTime?> ObtenerFinColaMaquinaAsync(
            int maquinaId,
            int programaExcluirId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
SELECT MAX
(
    ISNULL
    (
        FechaFinProgramada,
        DATEADD
        (
            MINUTE,
            CAST(CEILING(ISNULL(HorasProgramadas, 1) * 60) AS INT),
            FechaInicioProgramada
        )
    )
)
FROM dbo.Planeacion_ProgramaProduccion WITH (UPDLOCK, HOLDLOCK)
WHERE MaquinaID = @MaquinaID
  AND ProgramaProduccionID <> @ProgramaProduccionID
  AND Activo = 1
  AND ISNULL(EstatusID, 1) NOT IN (5, 9, 99)
  AND FechaInicioProgramada IS NOT NULL;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = maquinaId;
            cmd.Parameters.Add(
                "@ProgramaProduccionID",
                SqlDbType.Int).Value = programaExcluirId;

            var result = await cmd.ExecuteScalarAsync();

            return result == null || result == DBNull.Value
                ? null
                : Convert.ToDateTime(result);
        }

        private async Task<DateTime?> ObtenerFinOcupacionMoldeAsync(
            int moldeId,
            int programaExcluirId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
SELECT MAX
(
    ISNULL
    (
        FechaFinProgramada,
        DATEADD
        (
            MINUTE,
            CAST(CEILING(ISNULL(HorasProgramadas, 1) * 60) AS INT),
            FechaInicioProgramada
        )
    )
)
FROM dbo.Planeacion_ProgramaProduccion WITH (UPDLOCK, HOLDLOCK)
WHERE MoldeID = @MoldeID
  AND ProgramaProduccionID <> @ProgramaProduccionID
  AND Activo = 1
  AND ISNULL(EstatusID, 1) NOT IN (5, 9, 99)
  AND FechaInicioProgramada IS NOT NULL;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@MoldeID", SqlDbType.Int).Value = moldeId;
            cmd.Parameters.Add(
                "@ProgramaProduccionID",
                SqlDbType.Int).Value = programaExcluirId;

            var result = await cmd.ExecuteScalarAsync();

            return result == null || result == DBNull.Value
                ? null
                : Convert.ToDateTime(result);
        }

        private async Task<int> ObtenerSiguienteSecuenciaAsync(
            int maquinaId,
            int programaExcluirId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
SELECT ISNULL(MAX(SecuenciaMaquina), 0) + 1
FROM dbo.Planeacion_ProgramaProduccion WITH (UPDLOCK, HOLDLOCK)
WHERE MaquinaID = @MaquinaID
  AND ProgramaProduccionID <> @ProgramaProduccionID
  AND Activo = 1
  AND ISNULL(EstatusID, 1) NOT IN (5, 9, 99);";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = maquinaId;
            cmd.Parameters.Add(
                "@ProgramaProduccionID",
                SqlDbType.Int).Value = programaExcluirId;

            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        private static decimal CalcularHorasCambio(ProgramaBase programa)
        {
            if (!programa.Cambio.HasValue ||
                !programa.Arranque.HasValue)
            {
                return 1m;
            }

            var cambio = programa.FechaInicioProgramada.Date
                .Add(programa.Cambio.Value);

            var arranque = programa.FechaInicioProgramada.Date
                .Add(programa.Arranque.Value);

            if (arranque < cambio)
                arranque = arranque.AddDays(1);

            var horas = (decimal)(arranque - cambio).TotalHours;

            return horas < 0
                ? 0
                : Math.Round(horas, 4);
        }


        private async Task<ProgramaCola?> ObtenerProgramaAnteriorPorPuntoAsync(
            int maquinaId,
            int programaExcluirId,
            DateTime puntoCola,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
SELECT TOP (1)
    pp.ProgramaProduccionID,
    pp.ParteID,
    pp.NumeroParte,
    pp.ReferenciaSAP,
    pp.MoldeID,
    pp.MoldeCodigo,
    pp.FechaInicioProgramada,
    ISNULL
    (
        pp.FechaFinProgramada,
        DATEADD(MINUTE, CAST(CEILING(ISNULL(pp.HorasProgramadas, 1) * 60) AS INT), pp.FechaInicioProgramada)
    ) AS FechaFinProgramada,
    ISNULL(pp.HorasProgramadas, 0) AS HorasProgramadas
FROM dbo.Planeacion_ProgramaProduccion pp WITH (UPDLOCK, HOLDLOCK)
WHERE pp.Activo = 1
  AND pp.MaquinaID = @MaquinaID
  AND pp.ProgramaProduccionID <> @ProgramaProduccionID
  AND pp.FechaInicioProgramada IS NOT NULL
  AND ISNULL(pp.EstatusID, 1) NOT IN (5, 9, 99)
  AND pp.FechaInicioProgramada <= @PuntoCola
ORDER BY
    pp.FechaInicioProgramada DESC,
    pp.ProgramaProduccionID DESC;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = maquinaId;
            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaExcluirId;
            cmd.Parameters.Add("@PuntoCola", SqlDbType.DateTime).Value = puntoCola;

            await using var rd = await cmd.ExecuteReaderAsync();

            if (!await rd.ReadAsync())
                return null;

            return MapearProgramaCola(rd);
        }

        private static async Task<CalculoCola> CalcularPosicionCompactaAsync(
            int maquinaId,
            int programaExcluirId,
            int? parteId,
            int? moldeId,
            int? parteAnteriorId,
            int? moldeAnteriorId,
            DateTime cursorInicial,
            decimal horasProduccion,
            SqlConnection cn,
            SqlTransaction tx,
            bool trabajarDomingo)
        {
            var cursor = SiguienteAperturaOperativa(cursorInicial, trabajarDomingo);
            cursor = RedondearSiguienteBloqueLocal(cursor, 15);

            if (horasProduccion <= 0)
                horasProduccion = 1m;

            DateTime? moldeLiberado = null;

            for (var intento = 0; intento < 500; intento++)
            {
                cursor = SiguienteAperturaOperativa(cursor, trabajarDomingo);
                cursor = RedondearSiguienteBloqueLocal(cursor, 15);

                var mismaParte = parteId.HasValue && parteAnteriorId.HasValue && parteId.Value == parteAnteriorId.Value;
                var mismoMolde = moldeId.HasValue && moldeAnteriorId.HasValue && moldeId.Value == moldeAnteriorId.Value;

                var horasCambio = (moldeLiberado.HasValue || (!mismaParte && !mismoMolde))
                    ? 1m
                    : 0m;

                var arranque = SumarHorasOperativas(cursor, horasCambio, trabajarDomingo);
                var fin = SumarHorasOperativas(arranque, horasProduccion, trabajarDomingo);

                var finBloqueDuro = await ObtenerFinCruceMaquinaBloqueadaAsync(
                    maquinaId,
                    programaExcluirId,
                    cursor,
                    fin,
                    cn,
                    tx);

                if (finBloqueDuro.HasValue && finBloqueDuro.Value > cursor)
                {
                    cursor = finBloqueDuro.Value;
                    continue;
                }

                if (moldeId.HasValue)
                {
                    var finMolde = await ObtenerFinCruceMoldeIntervaloAsync(
                        moldeId.Value,
                        programaExcluirId,
                        cursor,
                        fin,
                        cn,
                        tx);

                    if (finMolde.HasValue && finMolde.Value > cursor)
                    {
                        moldeLiberado = finMolde.Value;
                        cursor = finMolde.Value;
                        continue;
                    }
                }

                return new CalculoCola
                {
                    Cambio = cursor,
                    Arranque = arranque,
                    Fin = fin,
                    HorasCambio = horasCambio,
                    MoldeLiberado = moldeLiberado
                };
            }

            throw new InvalidOperationException("No fue posible encontrar una posición válida de cola para este movimiento.");
        }

        private static async Task<DateTime?> ObtenerFinCruceMaquinaBloqueadaAsync(
            int maquinaId,
            int programaExcluirId,
            DateTime inicio,
            DateTime fin,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
SELECT TOP (1)
    ISNULL
    (
        pp.FechaFinProgramada,
        DATEADD(MINUTE, CAST(CEILING(ISNULL(pp.HorasProgramadas, 1) * 60) AS INT), pp.FechaInicioProgramada)
    ) AS FechaFinProgramada
FROM dbo.Planeacion_ProgramaProduccion pp WITH (UPDLOCK, HOLDLOCK)
OUTER APPLY
(
    SELECT TOP (1)
        e.EstatusID
    FROM dbo.Produccion_Ejecucion e
    WHERE e.ProgramaProduccionID = pp.ProgramaProduccionID
      AND e.Activo = 1
    ORDER BY e.EjecucionProduccionID DESC
) pe
WHERE pp.Activo = 1
  AND pp.MaquinaID = @MaquinaID
  AND pp.ProgramaProduccionID <> @ProgramaProduccionID
  AND pp.FechaInicioProgramada IS NOT NULL
  AND
  (
        ISNULL(pp.EstatusID, 1) IN (2, 3, 4, 5, 9, 99)
     OR ISNULL(pe.EstatusID, 0) IN (2, 3, 4, 5, 9, 99)
     OR
        (
            GETDATE() >= pp.FechaInicioProgramada
            AND GETDATE() < ISNULL
            (
                pp.FechaFinProgramada,
                DATEADD(MINUTE, CAST(CEILING(ISNULL(pp.HorasProgramadas, 1) * 60) AS INT), pp.FechaInicioProgramada)
            )
        )
  )
  AND pp.FechaInicioProgramada < @Fin
  AND ISNULL
      (
          pp.FechaFinProgramada,
          DATEADD(MINUTE, CAST(CEILING(ISNULL(pp.HorasProgramadas, 1) * 60) AS INT), pp.FechaInicioProgramada)
      ) > @Inicio
ORDER BY
    ISNULL
    (
        pp.FechaFinProgramada,
        DATEADD(MINUTE, CAST(CEILING(ISNULL(pp.HorasProgramadas, 1) * 60) AS INT), pp.FechaInicioProgramada)
    ) DESC;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = maquinaId;
            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaExcluirId;
            cmd.Parameters.Add("@Inicio", SqlDbType.DateTime).Value = inicio;
            cmd.Parameters.Add("@Fin", SqlDbType.DateTime).Value = fin;

            var result = await cmd.ExecuteScalarAsync();

            return result == null || result == DBNull.Value
                ? null
                : Convert.ToDateTime(result);
        }

        private static async Task<DateTime?> ObtenerFinCruceMoldeIntervaloAsync(
            int moldeId,
            int programaExcluirId,
            DateTime inicio,
            DateTime fin,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
SELECT TOP (1)
    ISNULL
    (
        pp.FechaFinProgramada,
        DATEADD(MINUTE, CAST(CEILING(ISNULL(pp.HorasProgramadas, 1) * 60) AS INT), pp.FechaInicioProgramada)
    ) AS FechaFinProgramada
FROM dbo.Planeacion_ProgramaProduccion pp WITH (UPDLOCK, HOLDLOCK)
WHERE pp.Activo = 1
  AND pp.MoldeID = @MoldeID
  AND pp.ProgramaProduccionID <> @ProgramaProduccionID
  AND pp.FechaInicioProgramada IS NOT NULL
  AND ISNULL(pp.EstatusID, 1) NOT IN (5, 9, 99)
  AND pp.FechaInicioProgramada < @Fin
  AND ISNULL
      (
          pp.FechaFinProgramada,
          DATEADD(MINUTE, CAST(CEILING(ISNULL(pp.HorasProgramadas, 1) * 60) AS INT), pp.FechaInicioProgramada)
      ) > @Inicio
ORDER BY
    pp.FechaInicioProgramada;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@MoldeID", SqlDbType.Int).Value = moldeId;
            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaExcluirId;
            cmd.Parameters.Add("@Inicio", SqlDbType.DateTime).Value = inicio;
            cmd.Parameters.Add("@Fin", SqlDbType.DateTime).Value = fin;

            var result = await cmd.ExecuteScalarAsync();

            return result == null || result == DBNull.Value
                ? null
                : Convert.ToDateTime(result);
        }

        private static async Task<int> ContarProgramasPosterioresReacomodablesAsync(
            int maquinaId,
            int programaExcluirId,
            DateTime desde,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
SELECT COUNT(1)
FROM dbo.Planeacion_ProgramaProduccion pp
OUTER APPLY
(
    SELECT TOP (1)
        e.EstatusID
    FROM dbo.Produccion_Ejecucion e
    WHERE e.ProgramaProduccionID = pp.ProgramaProduccionID
      AND e.Activo = 1
    ORDER BY e.EjecucionProduccionID DESC
) pe
WHERE pp.Activo = 1
  AND pp.MaquinaID = @MaquinaID
  AND pp.ProgramaProduccionID <> @ProgramaProduccionID
  AND pp.FechaInicioProgramada IS NOT NULL
  AND pp.FechaInicioProgramada >= @Desde
  AND ISNULL(pp.EstatusID, 1) NOT IN (2, 3, 4, 5, 9, 99)
  AND ISNULL(pe.EstatusID, 0) NOT IN (2, 3, 4, 5, 9, 99);";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = maquinaId;
            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaExcluirId;
            cmd.Parameters.Add("@Desde", SqlDbType.DateTime).Value = desde;

            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        private async Task<int> ReacomodarColaPosteriorAsync(
       int maquinaId,
       int programaInsertadoId,
       DateTime desdeSeleccion,
       DateTime cursorInicial,
       int? parteAnteriorId,
       int? moldeAnteriorId,
       int usuarioId,
       SqlConnection cn,
       SqlTransaction tx,
       bool trabajarDomingo)
        {
            /*
                desdeSeleccion:
                    Desde qué punto se buscan los programas posteriores que deben moverse.

                cursorInicial:
                    Desde qué hora se empieza a compactar la cola.

                Ejemplo:
                    Programa 1 termina 10:00.
                    Inserto Programa X entre Programa 1 y Programa 2.
                    Programa X termina 15:00.

                    desdeSeleccion = 10:00  -> busca Programa 2 y posteriores.
                    cursorInicial  = 15:00  -> acomoda Programa 2 después de X.
            */

            var programas = await ObtenerProgramasPosterioresReacomodablesAsync(
                maquinaId,
                programaInsertadoId,
                desdeSeleccion,
                cn,
                tx);

            var cursor = cursorInicial;
            var reacomodados = 0;

            foreach (var programa in programas)
            {
                var calculo = await CalcularPosicionCompactaAsync(
                    maquinaId,
                    programa.ProgramaProduccionID,
                    programa.ParteID,
                    programa.MoldeID,
                    parteAnteriorId,
                    moldeAnteriorId,
                    cursor,
                    programa.HorasProgramadas <= 0 ? 1m : programa.HorasProgramadas,
                    cn,
                    tx,
                    trabajarDomingo);

                var cambioDiferente =
                    programa.Inicio != calculo.Cambio;

                var finDiferente =
                    programa.Fin != calculo.Fin;

                if (cambioDiferente || finDiferente)
                {
                    await ActualizarProgramaReacomodadoAsync(
                        programa,
                        calculo.Cambio,
                        calculo.Arranque,
                        calculo.Fin,
                        usuarioId,
                        cn,
                        tx);

                    reacomodados++;
                }

                cursor = calculo.Fin;
                parteAnteriorId = programa.ParteID;
                moldeAnteriorId = programa.MoldeID;
            }

            return reacomodados;
        }


        private static async Task<List<ProgramaCola>> ObtenerProgramasPosterioresReacomodablesAsync(
            int maquinaId,
            int programaExcluirId,
            DateTime desde,
            SqlConnection cn,
            SqlTransaction tx)
        {
            var lista = new List<ProgramaCola>();

            const string sql = @"
SELECT
    pp.ProgramaProduccionID,
    pp.ParteID,
    pp.NumeroParte,
    pp.ReferenciaSAP,
    pp.MoldeID,
    pp.MoldeCodigo,
    pp.FechaInicioProgramada,
    ISNULL
    (
        pp.FechaFinProgramada,
        DATEADD(MINUTE, CAST(CEILING(ISNULL(pp.HorasProgramadas, 1) * 60) AS INT), pp.FechaInicioProgramada)
    ) AS FechaFinProgramada,
    ISNULL(pp.HorasProgramadas, 0) AS HorasProgramadas
FROM dbo.Planeacion_ProgramaProduccion pp
OUTER APPLY
(
    SELECT TOP (1)
        e.EstatusID
    FROM dbo.Produccion_Ejecucion e
    WHERE e.ProgramaProduccionID = pp.ProgramaProduccionID
      AND e.Activo = 1
    ORDER BY e.EjecucionProduccionID DESC
) pe
WHERE pp.Activo = 1
  AND pp.MaquinaID = @MaquinaID
  AND pp.ProgramaProduccionID <> @ProgramaProduccionID
  AND pp.FechaInicioProgramada IS NOT NULL
  AND pp.FechaInicioProgramada >= @Desde
  AND ISNULL(pp.EstatusID, 1) NOT IN (2, 3, 4, 5, 9, 99)
  AND ISNULL(pe.EstatusID, 0) NOT IN (2, 3, 4, 5, 9, 99)
ORDER BY
    pp.FechaInicioProgramada,
    pp.ProgramaProduccionID;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = maquinaId;
            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaExcluirId;
            cmd.Parameters.Add("@Desde", SqlDbType.DateTime).Value = desde;

            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
                lista.Add(MapearProgramaCola(rd));

            return lista;
        }

        private static async Task ActualizarProgramaReacomodadoAsync(
            ProgramaCola programa,
            DateTime cambio,
            DateTime arranque,
            DateTime fin,
            int usuarioId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
UPDATE dbo.Planeacion_ProgramaProduccion
SET
    FechaInicioProgramada = @FechaInicio,
    FechaFinProgramada = @FechaFin,
    Cambio = @Cambio,
    Arranque = @Arranque,
    UsuarioModificacionID = @UsuarioID,
    FechaModificacion = GETDATE()
WHERE ProgramaProduccionID = @ProgramaProduccionID
  AND Activo = 1;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@FechaInicio", SqlDbType.DateTime).Value = cambio;
            cmd.Parameters.Add("@FechaFin", SqlDbType.DateTime).Value = fin;
            cmd.Parameters.Add("@Cambio", SqlDbType.Time).Value = cambio.TimeOfDay;
            cmd.Parameters.Add("@Arranque", SqlDbType.Time).Value = arranque.TimeOfDay;
            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programa.ProgramaProduccionID;

            await cmd.ExecuteNonQueryAsync();
        }

        private static ProgramaCola MapearProgramaCola(SqlDataReader rd)
        {
            return new ProgramaCola
            {
                ProgramaProduccionID = Entero(rd, "ProgramaProduccionID"),
                ParteID = NullableEntero(rd, "ParteID"),
                ParteTexto = Texto(rd, "ReferenciaSAP") ?? Texto(rd, "NumeroParte") ?? "la pieza",
                MoldeID = NullableEntero(rd, "MoldeID"),
                MoldeTexto = Texto(rd, "MoldeCodigo") ?? "el molde",
                Inicio = Fecha(rd, "FechaInicioProgramada"),
                Fin = Fecha(rd, "FechaFinProgramada"),
                HorasProgramadas = Decimal(rd, "HorasProgramadas")
            };
        }

        private static DateTime RedondearSiguienteBloqueLocal(DateTime fecha, int minutos)
        {
            if (minutos <= 0)
                minutos = 15;

            var bloqueTicks = TimeSpan.FromMinutes(minutos).Ticks;

            var ticks = fecha.Ticks % bloqueTicks == 0
                ? fecha.Ticks
                : fecha.Ticks + (bloqueTicks - fecha.Ticks % bloqueTicks);

            var redondeada = new DateTime(ticks, DateTimeKind.Unspecified);

            return new DateTime(
                redondeada.Year,
                redondeada.Month,
                redondeada.Day,
                redondeada.Hour,
                redondeada.Minute,
                0,
                DateTimeKind.Unspecified);
        }

        private static string ConstruirResumenMovimientoCompacto(
            ProgramaBase programa,
            MaquinaCompatible destino,
            ProgramaCola? anteriorCola,
            DateTime cambio,
            DateTime arranque,
            DateTime fin,
            DateTime? moldeLiberado,
            decimal horasCambio,
            int programasQueSeRecorreran)
        {
            var motivos = new List<string>
            {
                $"Se moverá de {programa.MaquinaCodigo ?? "sin máquina"} a {destino.Codigo}.",
                anteriorCola == null
                    ? "Se insertará al primer punto operativo disponible de la máquina destino."
                    : $"Se insertará en cola después de {anteriorCola.ParteTexto}, que termina el {anteriorCola.Fin:dd/MM/yyyy HH:mm}."
            };

            if (moldeLiberado.HasValue)
                motivos.Add($"El molde estaba ocupado y queda libre el {moldeLiberado:dd/MM/yyyy HH:mm}.");

            if (programasQueSeRecorreran > 0)
                motivos.Add($"Se compactará la cola y se recorrerán {programasQueSeRecorreran} programa(s) posterior(es) sin dejar huecos.");

            motivos.Add($"Cambio: {cambio:dd/MM/yyyy HH:mm}.");
            motivos.Add($"Arranque: {arranque:dd/MM/yyyy HH:mm}.");
            motivos.Add($"Tiempo considerado para cambio: {horasCambio:N2} h.");
            motivos.Add($"Fin estimado: {fin:dd/MM/yyyy HH:mm}.");

            return string.Join(" ", motivos);
        }

        // ============================================================
        // ESCRITURAS
        // ============================================================

        private static async Task ActualizarProgramaAsync(
            ProgramaBase programa,
            MaquinaCompatible maquinaDestino,
            DateTime fechaCambio,
            DateTime fechaArranque,
            DateTime fechaFin,
            decimal horasProduccion,
            int secuencia,
            int usuarioId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
UPDATE dbo.Planeacion_ProgramaProduccion
SET
    MaquinaID = @MaquinaID,
    MaquinaCodigo = @MaquinaCodigo,
    MaquinaNombre = @MaquinaNombre,

    FechaInicioProgramada = @FechaCambio,
    FechaFinProgramada = @FechaFin,

    Cambio = @Cambio,
    Arranque = @Arranque,

    HorasProgramadas = @HorasProgramadas,
    SecuenciaMaquina = @SecuenciaMaquina,

    UsuarioModificacionID = @UsuarioID,
    FechaModificacion = GETDATE()
WHERE ProgramaProduccionID = @ProgramaProduccionID
  AND Activo = 1;";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value =
                maquinaDestino.MaquinaID;

            cmd.Parameters.Add("@MaquinaCodigo", SqlDbType.NVarChar, 100).Value =
                maquinaDestino.Codigo;

            cmd.Parameters.Add("@MaquinaNombre", SqlDbType.NVarChar, 200).Value =
                maquinaDestino.Nombre;

            cmd.Parameters.Add("@FechaCambio", SqlDbType.DateTime).Value =
                fechaCambio;

            cmd.Parameters.Add("@FechaFin", SqlDbType.DateTime).Value =
                fechaFin;

            cmd.Parameters.Add("@Cambio", SqlDbType.Time).Value =
                fechaCambio.TimeOfDay;

            cmd.Parameters.Add("@Arranque", SqlDbType.Time).Value =
                fechaArranque.TimeOfDay;

            var horasParam =
                cmd.Parameters.Add("@HorasProgramadas", SqlDbType.Decimal);

            horasParam.Precision = 18;
            horasParam.Scale = 4;
            horasParam.Value = horasProduccion;

            cmd.Parameters.Add("@SecuenciaMaquina", SqlDbType.Int).Value =
                secuencia;

            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value =
                usuarioId;

            cmd.Parameters.Add(
                "@ProgramaProduccionID",
                SqlDbType.Int).Value = programa.ProgramaProduccionID;

            var afectados = await cmd.ExecuteNonQueryAsync();

            if (afectados != 1)
            {
                throw new InvalidOperationException(
                    "El programa cambió o dejó de estar disponible.");
            }
        }

        private static async Task SincronizarDocumentosRelacionadosAsync(
            ProgramaBase programa,
            MaquinaCompatible maquinaDestino,
            DateTime fechaCambio,
            DateTime fechaArranque,
            DateTime fechaFin,
            decimal horasProduccion,
            SqlConnection cn,
            SqlTransaction tx)
        {
            if (programa.SolicitudProduccionDetalleID.HasValue)
            {
                const string sqlDetalle = @"
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

                await using var cmd = new SqlCommand(sqlDetalle, cn, tx);

                cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value =
                    maquinaDestino.MaquinaID;

                var horasParam =
                    cmd.Parameters.Add("@HorasProgramadas", SqlDbType.Decimal);

                horasParam.Precision = 18;
                horasParam.Scale = 4;
                horasParam.Value = horasProduccion;

                cmd.Parameters.Add("@Cambio", SqlDbType.Time).Value =
                    fechaCambio.TimeOfDay;

                cmd.Parameters.Add("@Arranque", SqlDbType.Time).Value =
                    fechaArranque.TimeOfDay;

                cmd.Parameters.Add("@FechaProgramada", SqlDbType.Date).Value =
                    fechaCambio.Date;

                cmd.Parameters.Add("@HoraInicio", SqlDbType.Time).Value =
                    fechaCambio.TimeOfDay;

                cmd.Parameters.Add("@HoraFin", SqlDbType.Time).Value =
                    fechaFin.TimeOfDay;

                cmd.Parameters.Add(
                    "@SolicitudProduccionDetalleID",
                    SqlDbType.Int).Value =
                    programa.SolicitudProduccionDetalleID.Value;

                await cmd.ExecuteNonQueryAsync();
            }

            if (programa.SolicitudProduccionID.HasValue)
            {
                const string sqlOf = @"
UPDATE dbo.SolicitudesProduccion
SET
    FechaInicioPlaneada = @FechaInicio,
    FechaFinPlaneada = @FechaFin
WHERE SolicitudProduccionID = @SolicitudProduccionID;";

                await using var cmd = new SqlCommand(sqlOf, cn, tx);

                cmd.Parameters.Add("@FechaInicio", SqlDbType.DateTime).Value =
                    fechaCambio;

                cmd.Parameters.Add("@FechaFin", SqlDbType.DateTime).Value =
                    fechaFin;

                cmd.Parameters.Add(
                    "@SolicitudProduccionID",
                    SqlDbType.Int).Value =
                    programa.SolicitudProduccionID.Value;

                await cmd.ExecuteNonQueryAsync();
            }

            if (programa.ReleaseDetalleID.HasValue)
            {
                const string sqlRelease = @"
UPDATE dbo.Planeacion_ReleaseDetalle
SET
    FechaInicioSugerida = @FechaInicio,
    FechaFinEstimada = @FechaFin,
    DaTiempo =
        CASE
            WHEN FechaRequerida IS NULL THEN NULL
            WHEN CONVERT(date, @FechaFin) <= CONVERT(date, FechaRequerida)
                THEN 1
            ELSE 0
        END,
    MensajeCapacidad =
        CASE
            WHEN FechaRequerida IS NULL
                THEN 'Programa reacomodado. Sin fecha requerida del cliente.'
            WHEN CONVERT(date, @FechaFin) <= CONVERT(date, FechaRequerida)
                THEN 'Programa reacomodado dentro de la fecha requerida.'
            ELSE 'Programa reacomodado posterior a la fecha requerida.'
        END,
    FechaModificacion = GETDATE()
WHERE ReleaseDetalleID = @ReleaseDetalleID
  AND Activo = 1;";

                await using var cmd = new SqlCommand(sqlRelease, cn, tx);

                cmd.Parameters.Add("@FechaInicio", SqlDbType.DateTime).Value =
                    fechaCambio;

                cmd.Parameters.Add("@FechaFin", SqlDbType.DateTime).Value =
                    fechaFin;

                cmd.Parameters.Add(
                    "@ReleaseDetalleID",
                    SqlDbType.Int).Value =
                    programa.ReleaseDetalleID.Value;

                await cmd.ExecuteNonQueryAsync();
            }
        }

        private static async Task InsertarHistorialMovimientoAsync(
            ProgramaBase programa,
            MaquinaCompatible maquinaDestino,
            DateTime fechaCambio,
            DateTime fechaArranque,
            DateTime fechaFin,
            decimal horasProduccion,
            int usuarioId,
            string motivo,
            SqlConnection cn,
            SqlTransaction tx)
        {
            // El historial es opcional para no romper instalaciones
            // donde la tabla todavía no existe.
            const string sql = @"
IF OBJECT_ID
(
    N'dbo.Planeacion_ProgramaReprogramacionHistorial',
    N'U'
) IS NOT NULL
BEGIN
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
        @UsuarioID,
        GETDATE(),
        @Motivo
    );
END;";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value =
                programa.ProgramaProduccionID;

            cmd.Parameters.Add("@MaquinaAnteriorID", SqlDbType.Int).Value =
                (object?)programa.MaquinaID ?? DBNull.Value;

            cmd.Parameters.Add("@MaquinaNuevaID", SqlDbType.Int).Value =
                maquinaDestino.MaquinaID;

            cmd.Parameters.Add("@InicioAnterior", SqlDbType.DateTime).Value =
                programa.FechaInicioProgramada;

            cmd.Parameters.Add("@InicioNuevo", SqlDbType.DateTime).Value =
                fechaCambio;

            cmd.Parameters.Add("@FinAnterior", SqlDbType.DateTime).Value =
                programa.FechaFinProgramada;

            cmd.Parameters.Add("@FinNuevo", SqlDbType.DateTime).Value =
                fechaFin;

            var horasAntes =
                cmd.Parameters.Add("@HorasAnteriores", SqlDbType.Decimal);

            horasAntes.Precision = 18;
            horasAntes.Scale = 4;
            horasAntes.Value = programa.HorasProgramadas;

            var horasNuevas =
                cmd.Parameters.Add("@HorasNuevas", SqlDbType.Decimal);

            horasNuevas.Precision = 18;
            horasNuevas.Scale = 4;
            horasNuevas.Value = horasProduccion;

            cmd.Parameters.Add("@CambioAnterior", SqlDbType.Time).Value =
                (object?)programa.Cambio ?? DBNull.Value;

            cmd.Parameters.Add("@CambioNuevo", SqlDbType.Time).Value =
                fechaCambio.TimeOfDay;

            cmd.Parameters.Add("@ArranqueAnterior", SqlDbType.Time).Value =
                (object?)programa.Arranque ?? DBNull.Value;

            cmd.Parameters.Add("@ArranqueNuevo", SqlDbType.Time).Value =
                fechaArranque.TimeOfDay;

            cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value =
                (object?)programa.ReleaseDetalleID ?? DBNull.Value;

            cmd.Parameters.Add(
                "@SolicitudProduccionID",
                SqlDbType.Int).Value =
                (object?)programa.SolicitudProduccionID ?? DBNull.Value;

            cmd.Parameters.Add(
                "@SolicitudProduccionDetalleID",
                SqlDbType.Int).Value =
                (object?)programa.SolicitudProduccionDetalleID ?? DBNull.Value;

            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value =
                usuarioId;

            cmd.Parameters.Add("@Motivo", SqlDbType.NVarChar, 500).Value =
                motivo.Length > 500
                    ? motivo[..500]
                    : motivo;

            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task ReordenarSecuenciasAsync(
            int? maquinaAnteriorId,
            int maquinaNuevaId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
;WITH Orden AS
(
    SELECT
        ProgramaProduccionID,
        ROW_NUMBER() OVER
        (
            PARTITION BY MaquinaID
            ORDER BY
                FechaInicioProgramada,
                ProgramaProduccionID
        ) AS NuevaSecuencia
    FROM dbo.Planeacion_ProgramaProduccion
    WHERE Activo = 1
      AND ISNULL(EstatusID, 1) NOT IN (5, 9, 99)
      AND
      (
          MaquinaID = @MaquinaNuevaID
          OR
          (
              @MaquinaAnteriorID IS NOT NULL
              AND MaquinaID = @MaquinaAnteriorID
          )
      )
)
UPDATE pp
SET
    SecuenciaMaquina = o.NuevaSecuencia
FROM dbo.Planeacion_ProgramaProduccion pp
INNER JOIN Orden o
    ON o.ProgramaProduccionID = pp.ProgramaProduccionID;";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@MaquinaNuevaID", SqlDbType.Int).Value =
                maquinaNuevaId;

            cmd.Parameters.Add("@MaquinaAnteriorID", SqlDbType.Int).Value =
                (object?)maquinaAnteriorId ?? DBNull.Value;

            await cmd.ExecuteNonQueryAsync();
        }

        // ============================================================
        // CANDADOS SQL
        // ============================================================

        private static async Task TomarCandadoCalendarioAsync(
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
DECLARE @Resultado INT;

EXEC @Resultado = sys.sp_getapplock
    @Resource = N'ERP_PLANEACION_CALENDARIO_MAQUINAS',
    @LockMode = N'Exclusive',
    @LockOwner = N'Transaction',
    @LockTimeout = 15000;

IF @Resultado < 0
BEGIN
    THROW 51010,
        'El calendario está siendo actualizado. Intenta nuevamente en unos segundos.',
        1;
END;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task ActivarReacomodoPlaneacionAsync(
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
EXEC sys.sp_set_session_context
    @key = N'PlaneacionPermitirReacomodo',
    @value = 1;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task DesactivarReacomodoPlaneacionAsync(
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
EXEC sys.sp_set_session_context
    @key = N'PlaneacionPermitirReacomodo',
    @value = NULL;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task ValidarProgramaSinCrucesAsync(
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
EXEC dbo.Planeacion_ValidarProgramaSinCruces;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task LimpiarContextoSinTransaccionAsync(
            SqlConnection cn)
        {
            if (cn.State != ConnectionState.Open)
                return;

            try
            {
                const string sql = @"
EXEC sys.sp_set_session_context
    @key = N'PlaneacionPermitirReacomodo',
    @value = NULL;";

                await using var cmd = new SqlCommand(sql, cn);
                await cmd.ExecuteNonQueryAsync();
            }
            catch
            {
                // La conexión se cerrará al salir del método.
            }
        }

        private static async Task RollbackSeguroAsync(
            SqlTransaction tx)
        {
            try
            {
                await tx.RollbackAsync();
            }
            catch
            {
                // La transacción puede haber sido abortada por SQL Server.
            }
        }

        // ============================================================
        // HORARIO OPERATIVO
        //
        // Lunes 07:00 a sábado 18:00.
        // El domingo solo se usa cuando el switch de la vista está activo.
        // ============================================================

        private static DateTime SiguienteAperturaOperativa(
            DateTime fecha,
            bool trabajarDomingo)
        {
            var value = fecha;

            while (true)
            {
                if (value.DayOfWeek == DayOfWeek.Sunday)
                {
                    if (trabajarDomingo)
                        return value;

                    value = value.Date
                        .AddDays(1)
                        .AddHours(7);

                    continue;
                }

                if (value.DayOfWeek == DayOfWeek.Monday &&
                    value.TimeOfDay < TimeSpan.FromHours(7))
                {
                    return value.Date.AddHours(7);
                }

                if (value.DayOfWeek == DayOfWeek.Saturday &&
     value.TimeOfDay >= new TimeSpan(22, 30, 0))
                {
                    value = value.Date
                        .AddDays(2)
                        .AddHours(7);

                    continue;
                }

                return value;
            }
        }

        private static DateTime FinVentanaOperativa(
            DateTime fecha,
            bool trabajarDomingo)
        {
            if (fecha.DayOfWeek == DayOfWeek.Saturday)
                return fecha.Date.AddHours(22).AddMinutes(30);

            if (fecha.DayOfWeek == DayOfWeek.Sunday)
            {
                return trabajarDomingo
                    ? fecha.Date.AddDays(1)
                    : fecha.Date;
            }

            return fecha.Date.AddDays(1);
        }

        private static DateTime SumarHorasOperativas(
            DateTime inicio,
            decimal horas,
            bool trabajarDomingo)
        {
            if (horas <= 0)
                return SiguienteAperturaOperativa(
                    inicio,
                    trabajarDomingo);

            var cursor = SiguienteAperturaOperativa(
                inicio,
                trabajarDomingo);

            var restante = horas;
            var guard = 0;

            while (restante > 0.0001m)
            {
                guard++;

                if (guard > 2000)
                {
                    throw new InvalidOperationException(
                        "No fue posible calcular el horario operativo.");
                }

                cursor = SiguienteAperturaOperativa(
                    cursor,
                    trabajarDomingo);

                var finVentana = FinVentanaOperativa(
                    cursor,
                    trabajarDomingo);

                var disponible =
                    (decimal)(finVentana - cursor).TotalHours;

                if (disponible <= 0)
                {
                    cursor = SiguienteAperturaOperativa(
                        finVentana.AddMinutes(1),
                        trabajarDomingo);

                    continue;
                }

                if (restante <= disponible)
                    return cursor.AddHours((double)restante);

                restante -= disponible;

                cursor = SiguienteAperturaOperativa(
                    finVentana.AddMinutes(1),
                    trabajarDomingo);
            }

            return cursor;
        }

        // ============================================================
        // PERIODO DE LA VISTA
        // ============================================================

        private static PeriodoCalendario ResolverPeriodo(
            string? vista,
            DateTime? fecha,
            DateTime? rangoInicio,
            DateTime? rangoFin)
        {
            var vistaNormalizada =
                (vista ?? "semana").Trim().ToLowerInvariant();

            var fechaBase = (fecha ?? DateTime.Today).Date;

            if (vistaNormalizada == "dia")
            {
                return new PeriodoCalendario
                {
                    Vista = "dia",
                    TextoVista = "Día",
                    Titulo = fechaBase.ToString(
                        "dddd dd 'de' MMMM 'de' yyyy",
                        new CultureInfo("es-MX")),
                    Inicio = fechaBase,
                    Fin = fechaBase.AddDays(1),
                    Anterior = fechaBase.AddDays(-1),
                    Siguiente = fechaBase.AddDays(1)
                };
            }

            if (vistaNormalizada == "mes")
            {
                var inicioMes =
                    new DateTime(fechaBase.Year, fechaBase.Month, 1);

                var finMes = inicioMes.AddMonths(1);

                return new PeriodoCalendario
                {
                    Vista = "mes",
                    TextoVista = "Mes",
                    Titulo = inicioMes.ToString(
                        "MMMM yyyy",
                        new CultureInfo("es-MX")),
                    Inicio = inicioMes,
                    Fin = finMes,
                    Anterior = inicioMes.AddMonths(-1),
                    Siguiente = inicioMes.AddMonths(1)
                };
            }

            if (vistaNormalizada == "rango")
            {
                var inicio = (rangoInicio ?? fechaBase).Date;
                var finInclusive = (rangoFin ?? inicio.AddDays(6)).Date;

                if (finInclusive < inicio)
                    finInclusive = inicio;

                if ((finInclusive - inicio).TotalDays > 30)
                    finInclusive = inicio.AddDays(30);

                return new PeriodoCalendario
                {
                    Vista = "rango",
                    TextoVista = "Rango",
                    Titulo =
                        $"{inicio:dd/MM/yyyy} - {finInclusive:dd/MM/yyyy}",
                    Inicio = inicio,
                    Fin = finInclusive.AddDays(1),
                    Anterior = inicio.AddDays(
                        -(finInclusive - inicio).Days - 1),
                    Siguiente = inicio.AddDays(
                        (finInclusive - inicio).Days + 1),
                    RangoInicio = inicio,
                    RangoFin = finInclusive
                };
            }

            var diasDesdeLunes =
                ((int)fechaBase.DayOfWeek + 6) % 7;

            var inicioSemana =
                fechaBase.AddDays(-diasDesdeLunes);

            return new PeriodoCalendario
            {
                Vista = "semana",
                TextoVista = "Semana",
                Titulo =
                    $"{inicioSemana:dd/MM/yyyy} - {inicioSemana.AddDays(6):dd/MM/yyyy}",
                Inicio = inicioSemana,
                Fin = inicioSemana.AddDays(7),
                Anterior = inicioSemana.AddDays(-7),
                Siguiente = inicioSemana.AddDays(7)
            };
        }

        // ============================================================
        // HELPERS
        // ============================================================

        private bool UsuarioEnSesion()
        {
            return HttpContext.Session
                .GetInt32("UsuarioID")
                .HasValue;
        }

        private int ObtenerUsuarioID()
        {
            return HttpContext.Session
                .GetInt32("UsuarioID") ?? 0;
        }

        private static bool PuedeMover(int estatusId)
        {
            return estatusId != EstatusPrograma.EnProduccion &&
                   estatusId != EstatusPrograma.Terminado &&
                   estatusId != EstatusPrograma.Cerrado &&
                   estatusId != EstatusPrograma.Cancelado;
        }

        private static DateTime NormalizarFecha(DateTime fecha)
        {
            return new DateTime(
                fecha.Year,
                fecha.Month,
                fecha.Day,
                fecha.Hour,
                fecha.Minute,
                0,
                DateTimeKind.Unspecified);
        }

        private static DateTime Maximo(
            DateTime baseFecha,
            params DateTime?[] fechas)
        {
            var resultado = baseFecha;

            foreach (var fecha in fechas)
            {
                if (fecha.HasValue && fecha.Value > resultado)
                    resultado = fecha.Value;
            }

            return resultado;
        }

        private static string ConstruirResumenMovimiento(
            ProgramaBase programa,
            MaquinaCompatible destino,
            DateTime cambio,
            DateTime arranque,
            DateTime fin,
            DateTime? finCola,
            DateTime? finMolde,
            decimal horasCambio)
        {
            var motivos = new List<string>
            {
                $"Se moverá de {programa.MaquinaCodigo ?? "sin máquina"} a {destino.Codigo}.",
                "Se colocará al final de la cola de la máquina destino."
            };

            if (finCola.HasValue)
            {
                motivos.Add(
                    $"La cola destino queda libre el {finCola:dd/MM/yyyy HH:mm}.");
            }

            if (finMolde.HasValue)
            {
                motivos.Add(
                    $"El molde queda libre el {finMolde:dd/MM/yyyy HH:mm}.");
            }

            motivos.Add(
                $"Cambio: {cambio:dd/MM/yyyy HH:mm}.");

            motivos.Add(
                $"Arranque: {arranque:dd/MM/yyyy HH:mm}.");

            motivos.Add(
                $"Tiempo considerado para cambio: {horasCambio:N2} h.");

            motivos.Add(
                $"Fin estimado: {fin:dd/MM/yyyy HH:mm}.");

            return string.Join(" ", motivos);
        }

        private static string CrearTextoOF(
            int? solicitudProduccionId,
            string? folioRelease)
        {
            if (solicitudProduccionId.HasValue)
                return $"OF {solicitudProduccionId.Value}";

            return string.IsNullOrWhiteSpace(folioRelease)
                ? "Programa"
                : folioRelease;
        }

        private static string CrearMaquinaSugeridaTexto(
            string? principal,
            string? sustituta)
        {
            var valores = new[]
            {
                principal,
                sustituta
            }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct()
            .ToList();

            return valores.Any()
                ? string.Join(" / ", valores)
                : "Sin máquina configurada";
        }

        private static string NombreEstatusPrograma(int estatusId)
        {
            return estatusId switch
            {
                1 => "Programado",
                2 => "En preparación",
                3 => "En producción",
                4 => "Pausado",
                5 => "Terminado",
                9 => "Cerrado",
                99 => "Cancelado",
                _ => "Sin estatus"
            };
        }

        private static string NombreEstatusProduccion(
            int? estatusId)
        {
            return estatusId switch
            {
                2 => "En preparación",
                3 => "En producción",
                4 => "Pausado",
                5 => "Terminado",
                9 => "Cerrado",
                99 => "Cancelado",
                _ => "Sin ejecución"
            };
        }

        private static string CrearSemaforoTexto(
            int estatusPrograma,
            int? estatusProduccion)
        {
            if (estatusProduccion == 3)
                return "Produciendo";

            if (estatusProduccion == 5 ||
                estatusPrograma == 5)
                return "Producido";

            if (estatusProduccion == 9 ||
                estatusProduccion == 99 ||
                estatusPrograma == 9 ||
                estatusPrograma == 99)
                return "Cerrado";

            return "Timeline";
        }

        private static string CrearSemaforoClase(
            int estatusPrograma,
            int? estatusProduccion)
        {
            if (estatusProduccion == 3)
                return "bloque-produciendo";

            if (estatusProduccion == 5 ||
                estatusPrograma == 5)
                return "bloque-producido";

            if (estatusProduccion == 9 ||
                estatusProduccion == 99 ||
                estatusPrograma == 9 ||
                estatusPrograma == 99)
                return "bloque-cerrado";

            return "bloque-timeline";
        }

        private static int Entero(
            SqlDataReader rd,
            string columna)
        {
            var ordinal = rd.GetOrdinal(columna);

            return rd.IsDBNull(ordinal)
                ? 0
                : Convert.ToInt32(rd.GetValue(ordinal));
        }

        private static int? NullableEntero(
            SqlDataReader rd,
            string columna)
        {
            var ordinal = rd.GetOrdinal(columna);

            return rd.IsDBNull(ordinal)
                ? null
                : Convert.ToInt32(rd.GetValue(ordinal));
        }

        private static decimal Decimal(
            SqlDataReader rd,
            string columna)
        {
            var ordinal = rd.GetOrdinal(columna);

            return rd.IsDBNull(ordinal)
                ? 0
                : Convert.ToDecimal(rd.GetValue(ordinal));
        }

        private static DateTime Fecha(
            SqlDataReader rd,
            string columna)
        {
            var ordinal = rd.GetOrdinal(columna);

            return rd.IsDBNull(ordinal)
                ? DateTime.MinValue
                : Convert.ToDateTime(rd.GetValue(ordinal));
        }

        private static TimeSpan? NullableTiempo(
            SqlDataReader rd,
            string columna)
        {
            var ordinal = rd.GetOrdinal(columna);

            return rd.IsDBNull(ordinal)
                ? null
                : (TimeSpan)rd.GetValue(ordinal);
        }

        private static string? Texto(
            SqlDataReader rd,
            string columna)
        {
            var ordinal = rd.GetOrdinal(columna);

            return rd.IsDBNull(ordinal)
                ? null
                : rd.GetValue(ordinal)?.ToString()?.Trim();
        }

        // ============================================================
        // CLASES INTERNAS
        // ============================================================

        private sealed class ProgramaBase
        {
            public int ProgramaProduccionID { get; set; }

            public int? MaquinaID { get; set; }
            public string? MaquinaCodigo { get; set; }
            public string? MaquinaNombre { get; set; }

            public int? ParteID { get; set; }
            public string? NumeroParte { get; set; }
            public string? ReferenciaSAP { get; set; }
            public string? DescripcionParte { get; set; }

            public int? MoldeID { get; set; }
            public string? MoldeCodigo { get; set; }

            public int? ReleaseDetalleID { get; set; }
            public int? SolicitudProduccionID { get; set; }
            public int? SolicitudProduccionDetalleID { get; set; }

            public DateTime FechaInicioProgramada { get; set; }
            public DateTime FechaFinProgramada { get; set; }

            public decimal HorasProgramadas { get; set; }
            public TimeSpan? Cambio { get; set; }
            public TimeSpan? Arranque { get; set; }

            public int EstatusID { get; set; }

            public int? MaquinaPrincipalID { get; set; }
            public int? MaquinaSustitutaID { get; set; }
        }

        private sealed class MaquinaCompatible
        {
            public int MaquinaID { get; set; }
            public string Codigo { get; set; } = string.Empty;
            public string Nombre { get; set; } = string.Empty;
        }

        private sealed class ProgramaCola
        {
            public int ProgramaProduccionID { get; set; }
            public int? ParteID { get; set; }
            public string ParteTexto { get; set; } = "la pieza";
            public int? MoldeID { get; set; }
            public string MoldeTexto { get; set; } = "el molde";
            public DateTime Inicio { get; set; }
            public DateTime Fin { get; set; }
            public decimal HorasProgramadas { get; set; }
        }

        private sealed class CalculoCola
        {
            public DateTime Cambio { get; set; }
            public DateTime Arranque { get; set; }
            public DateTime Fin { get; set; }
            public decimal HorasCambio { get; set; }
            public DateTime? MoldeLiberado { get; set; }
        }

        private sealed class PeriodoCalendario
        {
            public string Vista { get; set; } = "semana";
            public string TextoVista { get; set; } = "Semana";
            public string Titulo { get; set; } = string.Empty;

            public DateTime Inicio { get; set; }
            public DateTime Fin { get; set; }

            public DateTime Anterior { get; set; }
            public DateTime Siguiente { get; set; }

            public DateTime? RangoInicio { get; set; }
            public DateTime? RangoFin { get; set; }
        }
    }

    // Se deja aquí para que el controlador sea autocontenido.
    // El JSON de la vista ya usa estos mismos nombres.
    public sealed class CalendarioMaquinasMoverRequest
    {
        public int ProgramaProduccionID { get; set; }
        public int MaquinaID { get; set; }
        public DateTime Inicio { get; set; }

        public decimal DuracionBloqueHoras { get; set; }
        public bool Redimensionado { get; set; }

        public bool ForzarMaquina { get; set; }
        public bool ConfirmarMovimiento { get; set; }
        public bool TrabajarDomingo { get; set; }
    }
}
