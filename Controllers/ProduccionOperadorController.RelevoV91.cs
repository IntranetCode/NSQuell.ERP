using ERP.NSQuell.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Linq;

namespace ERP.NSQuell.Controllers;

// NSQ_PRODUCCION_RELEVO_OPERADOR_V9_1_TRAMOS
public sealed partial class ProduccionOperadorController
{
    // NSQ_PRODUCCION_RELEVO_V9_2_ESTADO_Y_MOTIVOS
    private static readonly HashSet<string> MotivosRelevoV92 =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "AUSENCIA_O_RETIRO",
            "INCIDENCIA_O_EMERGENCIA",
            "CAMBIO_OPERATIVO",
            "APOYO_OTRA_MAQUINA",
            "COMIDA_O_DESCANSO",
            "FIN_TURNO_ANTICIPADO",
            "INDICACION_SUPERVISION",
            "OTRO_OPERATIVO"
        };
    private static DateTime AlMinutoV91(DateTime value)
    {
        return new DateTime(
            value.Year,
            value.Month,
            value.Day,
            value.Hour,
            value.Minute,
            0,
            value.Kind);
    }

    private static DateTime InicioFilaV91(
        ProduccionCapturaHoraFilaVm fila)
    {
        return fila.FechaProduccion.Date
            .Add(fila.HoraInicio);
    }

    private static DateTime FinFilaV91(
        ProduccionCapturaHoraFilaVm fila)
    {
        var inicio =
            InicioFilaV91(fila);

        var fin =
            fila.FechaProduccion.Date
                .Add(fila.HoraFin);

        if (fin <= inicio)
            fin = fin.AddDays(1);

        return fin;
    }

    // NSQ_PRODUCCION_RELEVO_V9_5_PENDIENTES_POR_TURNO
    private static bool FilaCruzaSegmentoV95(
        ProduccionCapturaHoraFilaVm fila,
        DateTime segmentoInicio,
        DateTime segmentoFin)
    {
        var inicioFila =
            InicioFilaV91(fila);

        var finFila =
            FinFilaV91(fila);

        return
            inicioFila < segmentoFin &&
            finFila > segmentoInicio;
    }

    private static DateTime InicioFilaSegmentoV95(
        ProduccionCapturaHoraFilaVm fila,
        DateTime segmentoInicio)
    {
        var inicioFila =
            InicioFilaV91(fila);

        return
            inicioFila < segmentoInicio
                ? segmentoInicio
                : inicioFila;
    }

    private static DateTime FinFilaSegmentoV95(
        ProduccionCapturaHoraFilaVm fila,
        DateTime segmentoFin)
    {
        var finFila =
            FinFilaV91(fila);

        return
            finFila > segmentoFin
                ? segmentoFin
                : finFila;
    }

    private static string NombrePersonaV91(
        string? nombre,
        string? paterno,
        string? materno)
    {
        return string.Join(
            " ",
            new[]
            {
                nombre,
                paterno,
                materno
            }
            .Where(x =>
                !string.IsNullOrWhiteSpace(x))
            .Select(x =>
                x!.Trim()));
    }

    private static async Task<bool>
        ExisteTablaTramosV91Async(
            SqlConnection cn,
            SqlTransaction? tx)
    {
        const string sql = @"
SELECT CASE
    WHEN OBJECT_ID(
        N'dbo.Produccion_OperadorTramos',
        N'U') IS NULL
    THEN 0 ELSE 1 END;";

        await using var cmd =
            tx == null
                ? new SqlCommand(sql,cn)
                : new SqlCommand(sql,cn,tx);

        return Convert.ToInt32(
            await cmd.ExecuteScalarAsync()) == 1;
    }

    private static async Task<
        (bool Valido,string Mensaje,string Nombre)>
        ValidarEntranteV91Async(
            int personaId,
            int? parteId,
            SqlConnection cn,
            SqlTransaction? tx)
    {
        const string personaSql = @"
SELECT TOP(1)
    ISNULL(EsColaboradorActivo,1) Activo,
    ISNULL(Nombre,N'') Nombre,
    ISNULL(ApellidoPaterno,N'') ApellidoPaterno,
    ISNULL(ApellidoMaterno,N'') ApellidoMaterno
FROM dbo.Persona
WHERE PersonaID=@PersonaID;";

        bool activo;
        string nombre;

        await using (
            var cmd =
                tx == null
                    ? new SqlCommand(
                        personaSql,
                        cn)
                    : new SqlCommand(
                        personaSql,
                        cn,
                        tx))
        {
            cmd.Parameters.Add(
                "@PersonaID",
                SqlDbType.Int).Value =
                personaId;

            await using var rd =
                await cmd.ExecuteReaderAsync();

            if (!await rd.ReadAsync())
            {
                return (
                    false,
                    "El operador entrante ya no existe.",
                    string.Empty);
            }

            activo =
                Convert.ToBoolean(
                    rd["Activo"]);

            nombre =
                NombrePersonaV91(
                    rd["Nombre"]?.ToString(),
                    rd["ApellidoPaterno"]?.ToString(),
                    rd["ApellidoMaterno"]?.ToString());
        }

        if (!activo)
        {
            return (
                false,
                "El operador entrante se encuentra inactivo.",
                nombre);
        }

        if (parteId.HasValue &&
            parteId.Value > 0)
        {
            const string matrizSql = @"
IF OBJECT_ID(
    N'dbo.vw_RRHH_PolivalenciaOperadoresParte',
    N'V') IS NULL
BEGIN
    SELECT -1;
END
ELSE
BEGIN
    SELECT
        CASE
            WHEN NOT EXISTS
            (
                SELECT 1
                FROM dbo.vw_RRHH_PolivalenciaOperadoresParte
                WHERE ParteID=@ParteID
            )
            THEN 1

            WHEN EXISTS
            (
                SELECT 1
                FROM dbo.vw_RRHH_PolivalenciaOperadoresParte
                WHERE ParteID=@ParteID
                  AND PersonalID=@PersonaID
                  AND Nivel BETWEEN 1 AND 4
            )
            THEN 1

            ELSE 0
        END;
END;";

            await using var cmd =
                tx == null
                    ? new SqlCommand(
                        matrizSql,
                        cn)
                    : new SqlCommand(
                        matrizSql,
                        cn,
                        tx);

            cmd.Parameters.Add(
                "@ParteID",
                SqlDbType.Int).Value =
                parteId.Value;

            cmd.Parameters.Add(
                "@PersonaID",
                SqlDbType.Int).Value =
                personaId;

            var resultado =
                Convert.ToInt32(
                    await cmd.ExecuteScalarAsync());

            if (resultado < 0)
            {
                return (
                    false,
                    "No existe la vista de polivalencia requerida. Ejecuta primero SQL 48.",
                    nombre);
            }

            if (resultado == 0)
            {
                return (
                    false,
                    "El operador entrante no tiene polivalencia N1-N4 para esta pieza.",
                    nombre);
            }
        }

        return (
            true,
            string.Empty,
            nombre);
    }

    private static async Task<long?>
        ObtenerContadorAntesV91Async(
            int ejecucionProduccionId,
            DateTime fecha,
            SqlConnection cn,
            SqlTransaction tx)
    {
        const string sql = @"
SELECT TOP(1)
    ValorContador
FROM dbo.Produccion_ContadorMaquinaLecturas
WHERE EjecucionProduccionID=@Ejecucion
  AND Activo=1
  AND FechaLectura<=@Fecha
ORDER BY
    FechaLectura DESC,
    LecturaContadorID DESC;";

        await using var cmd =
            new SqlCommand(
                sql,
                cn,
                tx);

        cmd.Parameters.Add(
            "@Ejecucion",
            SqlDbType.Int).Value =
            ejecucionProduccionId;

        cmd.Parameters.Add(
            "@Fecha",
            SqlDbType.DateTime2).Value =
            fecha;

        var raw =
            await cmd.ExecuteScalarAsync();

        return raw == null ||
               raw == DBNull.Value
            ? null
            : Convert.ToInt64(raw);
    }

    [HttpGet]
    public async Task<IActionResult>
        PrepararRelevoV91(
            int ejecucionProduccionId,
            int operadorEntranteId,
            DateTime segmentoInicio,
            DateTime segmentoFin)
    {
        if (!UsuarioEnSesion())
            return Unauthorized();

        if (ejecucionProduccionId <= 0 ||
            operadorEntranteId <= 0 ||
            segmentoInicio == DateTime.MinValue ||
            segmentoFin <= segmentoInicio)
        {
            return Json(new
            {
                ok=false,
                mensaje=
                    "Los datos del relevo no son válidos."
            });
        }

        await using var cn =
            new SqlConnection(
                ConnectionString);

        await cn.OpenAsync();

        if (!await ExisteTablaTramosV91Async(
                cn,
                null))
        {
            return Json(new
            {
                ok=false,
                mensaje=
                    "Falta dbo.Produccion_OperadorTramos. Ejecuta primero el SQL 48 con @Aplicar=1."
            });
        }

        const string sql = @"
SELECT TOP(1)
    e.EjecucionProduccionID,
    e.ProgramaProduccionID,
    e.ParteID,
    e.MaquinaID,
    e.OperadorID,
    ISNULL(e.OperadorNombre,N'') OperadorNombre,
    e.EstatusID,
    e.FechaInicioReal
FROM dbo.Produccion_Ejecucion e
WHERE e.EjecucionProduccionID=@Ejecucion
  AND e.Activo=1;";

        int programaId;
        int? parteId;
        int? salienteId;
        string salienteNombre;
        int estatusId;

        await using (
            var cmd =
                new SqlCommand(
                    sql,
                    cn))
        {
            cmd.Parameters.Add(
                "@Ejecucion",
                SqlDbType.Int).Value =
                ejecucionProduccionId;

            await using var rd =
                await cmd.ExecuteReaderAsync();

            if (!await rd.ReadAsync())
            {
                return Json(new
                {
                    ok=false,
                    mensaje=
                        "La ejecución de Producción ya no existe."
                });
            }

            programaId =
                Convert.ToInt32(
                    rd["ProgramaProduccionID"]);

            parteId =
                rd["ParteID"] == DBNull.Value
                    ? null
                    : Convert.ToInt32(
                        rd["ParteID"]);

            salienteId =
                rd["OperadorID"] == DBNull.Value
                    ? null
                    : Convert.ToInt32(
                        rd["OperadorID"]);

            salienteNombre =
                rd["OperadorNombre"]?.ToString()
                    ?.Trim() ??
                string.Empty;

            estatusId =
                Convert.ToInt32(
                    rd["EstatusID"]);
        }

        if (estatusId !=
            ProduccionEstatus.EnProduccion)
        {
            return Json(new
            {
                ok=false,
                mensaje=
                    "La OF ya no se encuentra en producción activa."
            });
        }

        if (!salienteId.HasValue ||
            salienteId.Value <= 0)
        {
            return Json(new
            {
                ok=false,
                mensaje=
                    "La ejecución no tiene un operador actual válido."
            });
        }

        if (salienteId.Value ==
            operadorEntranteId)
        {
            return Json(new
            {
                ok=false,
                mensaje=
                    "El operador seleccionado ya es el operador actual."
            });
        }

        var ahora =
            AlMinutoV91(
                DateTime.Now);

        if (ahora < segmentoInicio ||
            ahora >= segmentoFin)
        {
            return Json(new
            {
                ok=false,
                mensaje=
                    "El horario seleccionado ya no corresponde al tramo actualmente activo."
            });
        }

        var entrante =
            await ValidarEntranteV91Async(
                operadorEntranteId,
                parteId,
                cn,
                null);

        if (!entrante.Valido)
        {
            return Json(new
            {
                ok=false,
                mensaje=
                    entrante.Mensaje
            });
        }

        var filas =
            await ObtenerFilasCapturaHoraAsync(
                ejecucionProduccionId,
                programaId,
                cn);

        // NSQ_PRODUCCION_RELEVO_V9_5_PENDIENTES_POR_TURNO
        //
        // ObtenerFilasCapturaHoraAsync devuelve las filas de TODA la
        // ejecución. Para un relevo del turno 15:00-22:30 no debemos
        // bloquear por una fila 11:12-12:12 perteneciente al turno anterior.
        var filasSegmentoV95 =
            filas
                .Where(x =>
                    FilaCruzaSegmentoV95(
                        x,
                        segmentoInicio,
                        segmentoFin))
                .ToList();

        // Una fila completamente anterior al inicio de ESTE turno no
        // pertenece al operador que estamos intentando relevar.
        var vencida =
            filasSegmentoV95
                .Where(x =>
                    !x.Capturada &&
                    InicioFilaV91(x) >= segmentoInicio &&
                    InicioFilaV91(x) < segmentoFin &&
                    FinFilaV91(x) <= ahora)
                .OrderBy(
                    InicioFilaV91)
                .FirstOrDefault();

        if (vencida != null)
        {
            var pendienteInicio =
                InicioFilaSegmentoV95(
                    vencida,
                    segmentoInicio);

            var pendienteFin =
                FinFilaSegmentoV95(
                    vencida,
                    segmentoFin);

            return Json(new
            {
                ok=false,
                mensaje=
                    $"Antes de cambiar al operador falta capturar " +
                    $"{pendienteInicio:HH:mm}–{pendienteFin:HH:mm} " +
                    $"de este mismo turno ({segmentoInicio:HH:mm}–{segmentoFin:HH:mm}). " +
                    "Ese bloque debe quedar con el operador saliente."
            });
        }

        var filaActual =
            filasSegmentoV95
                .Where(x =>
                    !x.Capturada)
                .FirstOrDefault(x =>
                    InicioFilaV91(x) < ahora &&
                    FinFilaV91(x) > ahora);

        var ultimoContador =
            await ObtenerUltimaLecturaContadorMaquinaAsync(
                ejecucionProduccionId,
                cn);

        var defectos =
            (await CargarCatalogoDefectosAsync(cn))
                .Select(x => new
                {
                    catalogoDefectoID=
                        x.CatalogoDefectoID,

                    texto=
                        x.Texto
                })
                .ToList();

        return Json(new
        {
            ok=true,
            ejecucionProduccionId,
            programaProduccionId=programaId,
            operadorSalienteID=salienteId.Value,
            operadorSalienteNombre=salienteNombre,
            operadorEntranteID=operadorEntranteId,
            operadorEntranteNombre=entrante.Nombre,
            corte=ahora,
            finLimite=segmentoFin,
            requiereContador=filaActual != null,
            bloqueInicio=
                filaActual == null
                    ? (DateTime?)null
                    : InicioFilaSegmentoV95(
                        filaActual,
                        segmentoInicio),
            bloqueFin=
                filaActual == null
                    ? (DateTime?)null
                    : FinFilaSegmentoV95(
                        filaActual,
                        segmentoFin),
            ultimoContador,
            defectos
        });
    }
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AplicarRelevoV91(ProduccionRelevoV91PostVm vm)
    {
        if (!UsuarioEnSesion()) return RedirectToAction("Login", "Login");

        IActionResult Volver()
        {
            return RedirectToAction("Index", "ProduccionPersonal", new
            {
                vista = string.IsNullOrWhiteSpace(vm.Vista) ? "dia" : vm.Vista,
                fechaDesde = vm.FechaDesde?.ToString("yyyy-MM-dd") ?? vm.FechaTrabajo.ToString("yyyy-MM-dd"),
                fechaHasta = vm.FechaHasta?.ToString("yyyy-MM-dd"),
                panel = string.IsNullOrWhiteSpace(vm.Panel) ? "planner" : vm.Panel
            });
        }

        if (vm.EjecucionProduccionID <= 0 || vm.ProgramaProduccionID <= 0 || vm.OperadorEntranteID <= 0 || vm.TurnoID <= 0 || vm.SegmentoInicio == DateTime.MinValue || vm.SegmentoFin <= vm.SegmentoInicio)
        {
            TempData["Error"] = "No se recibieron datos válidos para el relevo.";
            return Volver();
        }

        var motivo = (vm.Motivo ?? string.Empty).Trim();
        var justificacion = (vm.Justificacion ?? string.Empty).Trim();

        if (motivo.Length < 3 || justificacion.Length < 5)
        {
            TempData["Error"] = "Motivo y justificación son obligatorios.";
            return Volver();
        }

        if (!MotivosRelevoV92.Contains(motivo))
        {
            TempData["Error"] = "El motivo de relevo no pertenece al catálogo autorizado.";
            return Volver();
        }

        if (vm.CantidadScrap < 0)
        {
            TempData["Error"] = "La cantidad de piezas rojas/Scrap no puede ser negativa.";
            return Volver();
        }

        vm.CantidadSospechosa = 0;

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);

        try
        {
            if (!await ExisteTablaTramosV91Async(cn, tx))
                throw new InvalidOperationException("Falta dbo.Produccion_OperadorTramos. Ejecuta primero SQL 48 con @Aplicar=1.");

            var usuarioId = ObtenerUsuarioID();
            if (usuarioId <= 0) throw new InvalidOperationException("No se pudo identificar al usuario que realiza el relevo.");

            const string lockSql = @"
SELECT TOP(1)
    e.ProgramaProduccionID,
    e.ParteID,
    e.MaquinaID,
    e.OperadorID,
    ISNULL(e.OperadorNombre,N'') OperadorNombre,
    e.EstatusID,
    e.FechaInicioReal
FROM dbo.Produccion_Ejecucion e WITH(UPDLOCK,HOLDLOCK)
WHERE e.EjecucionProduccionID=@Ejecucion
  AND e.Activo=1;";

            int programaId;
            int? parteId;
            int? salienteId;
            string salienteNombre;
            int estatusId;
            DateTime? inicioReal;

            await using (var cmd = new SqlCommand(lockSql, cn, tx))
            {
                cmd.Parameters.Add("@Ejecucion", SqlDbType.Int).Value = vm.EjecucionProduccionID;
                await using var rd = await cmd.ExecuteReaderAsync();

                if (!await rd.ReadAsync())
                    throw new InvalidOperationException("La ejecución de Producción ya no existe.");

                programaId = Convert.ToInt32(rd["ProgramaProduccionID"]);
                parteId = rd["ParteID"] == DBNull.Value ? null : Convert.ToInt32(rd["ParteID"]);
                salienteId = rd["OperadorID"] == DBNull.Value ? null : Convert.ToInt32(rd["OperadorID"]);
                salienteNombre = rd["OperadorNombre"]?.ToString()?.Trim() ?? string.Empty;
                estatusId = Convert.ToInt32(rd["EstatusID"]);
                inicioReal = rd["FechaInicioReal"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaInicioReal"]);
            }

            if (programaId != vm.ProgramaProduccionID)
                throw new InvalidOperationException("La OF cambió mientras se preparaba el relevo. Actualiza la pantalla.");

            if (estatusId != ProduccionEstatus.EnProduccion)
                throw new InvalidOperationException("La OF ya no está en producción activa.");

            if (!salienteId.HasValue || salienteId.Value <= 0)
                throw new InvalidOperationException("La ejecución no tiene operador actual.");

            if (salienteId.Value == vm.OperadorEntranteID)
                throw new InvalidOperationException("El operador seleccionado ya es el operador actual.");

            var ahora = AlMinutoV91(DateTime.Now);

            if (ahora < vm.SegmentoInicio || ahora >= vm.SegmentoFin)
                throw new InvalidOperationException("El horario seleccionado ya no corresponde al tramo activo. Actualiza la pantalla.");

            var entrante = await ValidarEntranteV91Async(vm.OperadorEntranteID, parteId, cn, tx);
            if (!entrante.Valido) throw new InvalidOperationException(entrante.Mensaje);

            if (await TieneParoAbiertoAsync(vm.EjecucionProduccionID, cn, tx))
                throw new InvalidOperationException("Existe un paro abierto. Finalízalo antes de realizar el relevo para no mezclar tiempos.");

            var tiempoExtra = await ObtenerTiempoExtraActivoAsync(vm.EjecucionProduccionID, cn, tx, true);
            if (tiempoExtra != null)
                throw new InvalidOperationException("Hay una sesión de tiempo extra activa. Finalízala antes de cambiar al operador.");

            var ejecucion = await ObtenerEjecucionOperadorAsync(vm.EjecucionProduccionID, cn, tx);
            if (ejecucion == null)
                throw new InvalidOperationException("No fue posible cargar la ejecución para realizar el corte.");

            var filas = await ObtenerFilasCapturaHoraAsync(vm.EjecucionProduccionID, vm.ProgramaProduccionID, cn, tx);
            var filasSegmentoV95 = filas
                .Where(x => FilaCruzaSegmentoV95(x, vm.SegmentoInicio, vm.SegmentoFin))
                .ToList();

            var vencida = filasSegmentoV95
                .Where(x => !x.Capturada && InicioFilaV91(x) >= vm.SegmentoInicio && InicioFilaV91(x) < vm.SegmentoFin && FinFilaV91(x) <= ahora)
                .OrderBy(InicioFilaV91)
                .FirstOrDefault();

            if (vencida != null)
            {
                var pendienteInicio = InicioFilaSegmentoV95(vencida, vm.SegmentoInicio);
                var pendienteFin = FinFilaSegmentoV95(vencida, vm.SegmentoFin);
                throw new InvalidOperationException($"Antes de cambiar al operador falta capturar {pendienteInicio:HH:mm}–{pendienteFin:HH:mm} de este mismo turno ({vm.SegmentoInicio:HH:mm}–{vm.SegmentoFin:HH:mm}). Ese bloque debe quedar con el operador saliente.");
            }

            var filaActual = filasSegmentoV95
                .Where(x => !x.Capturada)
                .FirstOrDefault(x => InicioFilaV91(x) < ahora && FinFilaV91(x) > ahora);

            var contadorAntes = await ObtenerUltimaLecturaContadorMaquinaAsync(vm.EjecucionProduccionID, cn);
            int? registroParcialId = null;
            long? contadorCorte = contadorAntes;
            var okParcial = 0;
            var piezasFisicasParcial = 0;
            var scrapParcial = 0;

            if (filaActual != null)
            {
                if (!vm.ContadorMaquinaActual.HasValue)
                    throw new InvalidOperationException("El relevo ocurre dentro de un bloque productivo. Captura el contador actual.");

                if (vm.ContadorMaquinaActual.Value < 0)
                    throw new InvalidOperationException("El contador de la máquina no puede ser negativo.");

                var inicioParcial = InicioFilaSegmentoV95(filaActual, vm.SegmentoInicio);

                if (ahora <= inicioParcial)
                    throw new InvalidOperationException("El corte no genera minutos productivos para el operador saliente.");

                var calculo = await CalcularProduccionContadorHoraAsync(
                    vm.EjecucionProduccionID,
                    inicioParcial,
                    ahora,
                    vm.ContadorMaquinaActual.Value,
                    cn,
                    tx);

                scrapParcial = vm.CantidadScrap;

                if ((long)scrapParcial > calculo.PiezasCalculadas)
                    throw new InvalidOperationException($"El contador indica {calculo.PiezasCalculadas:N0} pieza(s) físicas, pero capturaste {scrapParcial:N0} pieza(s) rojas/Scrap.");

                var defectos = await ValidarYNormalizarDefectosScrapAsync(scrapParcial, vm.DefectosScrap, cn, tx);
                if (!defectos.Valido) throw new InvalidOperationException(defectos.Mensaje);

                vm.DefectosScrap = defectos.Defectos;
                piezasFisicasParcial = calculo.PiezasCalculadas;
                okParcial = calculo.PiezasCalculadas - scrapParcial;

                var registroVm = new ProduccionRegistroHoraPostVm
                {
                    EjecucionProduccionID = vm.EjecucionProduccionID,
                    FechaProduccion = inicioParcial.Date,
                    HoraInicio = inicioParcial.TimeOfDay.ToString(@"hh\:mm"),
                    HoraFin = ahora.TimeOfDay.ToString(@"hh\:mm"),
                    ContadorMaquinaActual = vm.ContadorMaquinaActual,
                    CantidadOK = okParcial,
                    OkModificadoManual = false,
                    CantidadSospechosa = 0,
                    CantidadScrap = scrapParcial,
                    DefectosScrap = vm.DefectosScrap,
                    Observaciones = $"RELEVO V9.1 | {motivo} | {justificacion}"
                };

                registroParcialId = await InsertarRegistroHoraAsync(
                    ejecucion,
                    registroVm,
                    inicioParcial.TimeOfDay,
                    ahora.TimeOfDay,
                    salienteId.Value,
                    usuarioId,
                    calculo,
                    cn,
                    tx);

                await GuardarDefectosScrapAsync(
                    registroParcialId.Value,
                    registroVm.DefectosScrap,
                    usuarioId,
                    cn,
                    tx);

                await InsertarSegmentosRegistroHoraAsync(
                    registroParcialId.Value,
                    vm.EjecucionProduccionID,
                    calculo,
                    usuarioId,
                    cn,
                    tx);

                await RegistrarLecturaContadorHoraAsync(
                    ejecucion,
                    registroParcialId.Value,
                    salienteId.Value,
                    usuarioId,
                    ahora,
                    vm.ContadorMaquinaActual.Value,
                    calculo,
                    cn,
                    tx);

                await RegistrarBonusProduccionHoraAsync(
                    salienteId.Value,
                    vm.EjecucionProduccionID,
                    registroParcialId.Value,
                    okParcial,
                    calculo.PiezasCalculadas,
                    ahora,
                    usuarioId,
                    cn,
                    tx);

                if (calculo.PiezasCalculadas > 0)
                {
                    await VincularRegistroHoraConCalidadAsync(
                        ejecucion,
                        registroVm,
                        inicioParcial.TimeOfDay,
                        ahora.TimeOfDay,
                        registroParcialId.Value,
                        usuarioId,
                        cn,
                        tx);
                }

                await RecalcularTotalesEjecucionAsync(vm.EjecucionProduccionID, usuarioId, cn, tx);
                contadorCorte = vm.ContadorMaquinaActual.Value;
            }

            const string cerrarExpiradosSql = @"
UPDATE dbo.Produccion_OperadorTramos
SET FechaHoraFinReal=FechaHoraFinLimite,
    UsuarioCierreID=@Usuario,
    FechaCierre=SYSDATETIME()
WHERE EjecucionProduccionID=@Ejecucion
  AND Activo=1
  AND FechaHoraFinReal IS NULL
  AND FechaHoraFinLimite<=@Ahora;";

            await using (var cmd = new SqlCommand(cerrarExpiradosSql, cn, tx))
            {
                cmd.Parameters.Add("@Usuario", SqlDbType.Int).Value = usuarioId;
                cmd.Parameters.Add("@Ejecucion", SqlDbType.Int).Value = vm.EjecucionProduccionID;
                cmd.Parameters.Add("@Ahora", SqlDbType.DateTime2).Value = ahora;
                await cmd.ExecuteNonQueryAsync();
            }

            const string tramoAbiertoSql = @"
SELECT TOP(1)
    TramoOperadorID,
    OperadorID,
    FechaHoraInicio,
    ContadorInicio
FROM dbo.Produccion_OperadorTramos WITH(UPDLOCK,HOLDLOCK)
WHERE EjecucionProduccionID=@Ejecucion
  AND Activo=1
  AND FechaHoraFinReal IS NULL
ORDER BY TramoOperadorID DESC;";

            long? tramoAbiertoId = null;
            int? operadorTramoAbierto = null;
            DateTime? inicioTramoAbierto = null;

            await using (var cmd = new SqlCommand(tramoAbiertoSql, cn, tx))
            {
                cmd.Parameters.Add("@Ejecucion", SqlDbType.Int).Value = vm.EjecucionProduccionID;
                await using var rd = await cmd.ExecuteReaderAsync();

                if (await rd.ReadAsync())
                {
                    tramoAbiertoId = Convert.ToInt64(rd["TramoOperadorID"]);
                    operadorTramoAbierto = Convert.ToInt32(rd["OperadorID"]);
                    inicioTramoAbierto = Convert.ToDateTime(rd["FechaHoraInicio"]);
                }
            }

            if (tramoAbiertoId.HasValue)
            {
                if (operadorTramoAbierto != salienteId.Value)
                    throw new InvalidOperationException("El tramo abierto pertenece a otro operador. Actualiza la pantalla antes de continuar.");

                if (!inicioTramoAbierto.HasValue || ahora <= inicioTramoAbierto.Value)
                    throw new InvalidOperationException("No se puede realizar dos relevos en el mismo minuto. Intenta nuevamente en el siguiente minuto.");

                const string cerrarSql = @"
UPDATE dbo.Produccion_OperadorTramos
SET FechaHoraFinReal=@Corte,
    ContadorFin=@ContadorFin,
    RegistroHoraCorteID=@RegistroHoraCorteID,
    UsuarioCierreID=@Usuario,
    FechaCierre=SYSDATETIME()
WHERE TramoOperadorID=@Tramo
  AND Activo=1
  AND FechaHoraFinReal IS NULL;

IF @@ROWCOUNT<>1
    THROW 51131,'El tramo actual cambió mientras se realizaba el relevo.',1;";

                await using var cmd = new SqlCommand(cerrarSql, cn, tx);
                cmd.Parameters.Add("@Corte", SqlDbType.DateTime2).Value = ahora;
                cmd.Parameters.Add("@ContadorFin", SqlDbType.BigInt).Value = (object?)contadorCorte ?? DBNull.Value;
                cmd.Parameters.Add("@RegistroHoraCorteID", SqlDbType.Int).Value = (object?)registroParcialId ?? DBNull.Value;
                cmd.Parameters.Add("@Usuario", SqlDbType.Int).Value = usuarioId;
                cmd.Parameters.Add("@Tramo", SqlDbType.BigInt).Value = tramoAbiertoId.Value;
                await cmd.ExecuteNonQueryAsync();
            }
            else
            {
                var inicioSaliente = inicioReal.HasValue && inicioReal.Value > vm.SegmentoInicio
                    ? inicioReal.Value
                    : vm.SegmentoInicio;

                inicioSaliente = AlMinutoV91(inicioSaliente);

                if (inicioSaliente < ahora)
                {
                    var contadorInicioSaliente = await ObtenerContadorAntesV91Async(
                        vm.EjecucionProduccionID,
                        inicioSaliente,
                        cn,
                        tx);

                    const string seedSql = @"
INSERT dbo.Produccion_OperadorTramos
(
    EjecucionProduccionID,
    ProgramaProduccionID,
    TurnoID,
    FechaTrabajo,
    OperadorID,
    FechaHoraInicio,
    FechaHoraFinLimite,
    FechaHoraFinReal,
    ContadorInicio,
    ContadorFin,
    RegistroHoraCorteID,
    Motivo,
    Justificacion,
    Origen,
    UsuarioCreacionID,
    FechaCreacion,
    UsuarioCierreID,
    FechaCierre,
    Activo
)
VALUES
(
    @Ejecucion,
    @Programa,
    @TurnoID,
    @FechaTrabajo,
    @Operador,
    @Inicio,
    @FinLimite,
    @Corte,
    @ContadorInicio,
    @ContadorFin,
    @Registro,
    @Motivo,
    @Justificacion,
    N'BOOTSTRAP_RELEVO_V9_1',
    @Usuario,
    SYSDATETIME(),
    @Usuario,
    SYSDATETIME(),
    1
);";

                    await using var cmd = new SqlCommand(seedSql, cn, tx);
                    cmd.Parameters.Add("@Ejecucion", SqlDbType.Int).Value = vm.EjecucionProduccionID;
                    cmd.Parameters.Add("@Programa", SqlDbType.Int).Value = vm.ProgramaProduccionID;
                    cmd.Parameters.Add("@TurnoID", SqlDbType.Int).Value = vm.TurnoID;
                    cmd.Parameters.Add("@FechaTrabajo", SqlDbType.Date).Value = vm.FechaTrabajo.Date;
                    cmd.Parameters.Add("@Operador", SqlDbType.Int).Value = salienteId.Value;
                    cmd.Parameters.Add("@Inicio", SqlDbType.DateTime2).Value = inicioSaliente;
                    cmd.Parameters.Add("@FinLimite", SqlDbType.DateTime2).Value = vm.SegmentoFin;
                    cmd.Parameters.Add("@Corte", SqlDbType.DateTime2).Value = ahora;
                    cmd.Parameters.Add("@ContadorInicio", SqlDbType.BigInt).Value = (object?)contadorInicioSaliente ?? DBNull.Value;
                    cmd.Parameters.Add("@ContadorFin", SqlDbType.BigInt).Value = (object?)contadorCorte ?? DBNull.Value;
                    cmd.Parameters.Add("@Registro", SqlDbType.Int).Value = (object?)registroParcialId ?? DBNull.Value;
                    cmd.Parameters.Add("@Motivo", SqlDbType.NVarChar, 150).Value = motivo[..Math.Min(150, motivo.Length)];
                    cmd.Parameters.Add("@Justificacion", SqlDbType.NVarChar, 500).Value = justificacion[..Math.Min(500, justificacion.Length)];
                    cmd.Parameters.Add("@Usuario", SqlDbType.Int).Value = usuarioId;
                    await cmd.ExecuteNonQueryAsync();
                }
            }

            const string nuevoTramoSql = @"
INSERT dbo.Produccion_OperadorTramos
(
    EjecucionProduccionID,
    ProgramaProduccionID,
    TurnoID,
    FechaTrabajo,
    OperadorID,
    FechaHoraInicio,
    FechaHoraFinLimite,
    FechaHoraFinReal,
    ContadorInicio,
    ContadorFin,
    RegistroHoraCorteID,
    Motivo,
    Justificacion,
    Origen,
    UsuarioCreacionID,
    FechaCreacion,
    Activo
)
VALUES
(
    @Ejecucion,
    @Programa,
    @TurnoID,
    @FechaTrabajo,
    @Entrante,
    @Corte,
    @FinLimite,
    NULL,
    @ContadorInicio,
    NULL,
    NULL,
    @Motivo,
    @Justificacion,
    N'RELEVO_MANUAL_V9_1',
    @Usuario,
    SYSDATETIME(),
    1
);";

            await using (var cmd = new SqlCommand(nuevoTramoSql, cn, tx))
            {
                cmd.Parameters.Add("@Ejecucion", SqlDbType.Int).Value = vm.EjecucionProduccionID;
                cmd.Parameters.Add("@Programa", SqlDbType.Int).Value = vm.ProgramaProduccionID;
                cmd.Parameters.Add("@TurnoID", SqlDbType.Int).Value = vm.TurnoID;
                cmd.Parameters.Add("@FechaTrabajo", SqlDbType.Date).Value = vm.FechaTrabajo.Date;
                cmd.Parameters.Add("@Entrante", SqlDbType.Int).Value = vm.OperadorEntranteID;
                cmd.Parameters.Add("@Corte", SqlDbType.DateTime2).Value = ahora;
                cmd.Parameters.Add("@FinLimite", SqlDbType.DateTime2).Value = vm.SegmentoFin;
                cmd.Parameters.Add("@ContadorInicio", SqlDbType.BigInt).Value = (object?)contadorCorte ?? DBNull.Value;
                cmd.Parameters.Add("@Motivo", SqlDbType.NVarChar, 150).Value = motivo[..Math.Min(150, motivo.Length)];
                cmd.Parameters.Add("@Justificacion", SqlDbType.NVarChar, 500).Value = justificacion[..Math.Min(500, justificacion.Length)];
                cmd.Parameters.Add("@Usuario", SqlDbType.Int).Value = usuarioId;
                await cmd.ExecuteNonQueryAsync();
            }

            const string updateEjecucionSql = @"
UPDATE e
SET OperadorID=@Entrante,
    OperadorNombre=LTRIM(RTRIM(CONCAT(
        ISNULL(p.Nombre,N''),
        N' ',
        ISNULL(p.ApellidoPaterno,N''),
        N' ',
        ISNULL(p.ApellidoMaterno,N'')))),
    OperadoresModificadosManual=1,
    MotivoCambioOperadores=@MotivoCambio,
    UsuarioModificacionID=@Usuario,
    FechaModificacion=SYSDATETIME()
FROM dbo.Produccion_Ejecucion e
INNER JOIN dbo.Persona p
    ON p.PersonaID=@Entrante
WHERE e.EjecucionProduccionID=@Ejecucion
  AND e.Activo=1
  AND e.EstatusID=3
  AND e.OperadorID=@Saliente;

IF @@ROWCOUNT<>1
    THROW 51132,'El operador actual cambió mientras se confirmaba el relevo.',1;";

            await using (var cmd = new SqlCommand(updateEjecucionSql, cn, tx))
            {
                cmd.Parameters.Add("@Entrante", SqlDbType.Int).Value = vm.OperadorEntranteID;
                cmd.Parameters.Add("@MotivoCambio", SqlDbType.NVarChar, 500).Value = $"Relevo V9.1 {ahora:dd/MM HH:mm}: {motivo}. {justificacion}";
                cmd.Parameters.Add("@Usuario", SqlDbType.Int).Value = usuarioId;
                cmd.Parameters.Add("@Ejecucion", SqlDbType.Int).Value = vm.EjecucionProduccionID;
                cmd.Parameters.Add("@Saliente", SqlDbType.Int).Value = salienteId.Value;
                await cmd.ExecuteNonQueryAsync();
            }

            const string historialSql = @"
INSERT dbo.Produccion_PersonalAsignacionHistorial
(
    AsignacionPersonalID,
    ProgramaProduccionID,
    FechaTrabajo,
    TurnoID,
    TurnoNombre,
    Inicio,
    Fin,
    Rol,
    PersonaAnteriorID,
    PersonaNuevaID,
    Motivo,
    Justificacion,
    Origen,
    ProduccionActiva,
    UsuarioID,
    FechaMovimiento
)
VALUES
(
    NULL,
    @Programa,
    @Fecha,
    @TurnoID,
    @TurnoNombre,
    @Corte,
    @Fin,
    N'OPERADOR',
    @Anterior,
    @Nuevo,
    @Motivo,
    @Justificacion,
    N'PRODUCCION_RELEVO_V9_1_TRAMOS',
    1,
    @Usuario,
    SYSDATETIME()
);";

            await using (var cmd = new SqlCommand(historialSql, cn, tx))
            {
                cmd.Parameters.Add("@Programa", SqlDbType.Int).Value = vm.ProgramaProduccionID;
                cmd.Parameters.Add("@Fecha", SqlDbType.Date).Value = vm.FechaTrabajo.Date;
                cmd.Parameters.Add("@TurnoID", SqlDbType.Int).Value = vm.TurnoID;
                cmd.Parameters.Add("@TurnoNombre", SqlDbType.NVarChar, 100).Value = string.IsNullOrWhiteSpace(vm.TurnoNombre) ? $"Turno {vm.TurnoID}" : vm.TurnoNombre.Trim();
                cmd.Parameters.Add("@Corte", SqlDbType.DateTime2).Value = ahora;
                cmd.Parameters.Add("@Fin", SqlDbType.DateTime2).Value = vm.SegmentoFin;
                cmd.Parameters.Add("@Anterior", SqlDbType.Int).Value = salienteId.Value;
                cmd.Parameters.Add("@Nuevo", SqlDbType.Int).Value = vm.OperadorEntranteID;
                cmd.Parameters.Add("@Motivo", SqlDbType.NVarChar, 150).Value = motivo[..Math.Min(150, motivo.Length)];
                cmd.Parameters.Add("@Justificacion", SqlDbType.NVarChar, 500).Value = justificacion[..Math.Min(500, justificacion.Length)];
                cmd.Parameters.Add("@Usuario", SqlDbType.Int).Value = usuarioId;
                await cmd.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();

            TempData["Success"] = filaActual != null
                ? $"Relevo aplicado a las {ahora:HH:mm}. {salienteNombre} conserva el bloque parcial con {okParcial:N0} verdes/OK y {scrapParcial:N0} rojas/Scrap de {piezasFisicasParcial:N0} pieza(s) físicas. {entrante.Nombre} continúa hasta máximo {vm.SegmentoFin:HH:mm}."
                : $"Relevo aplicado a las {ahora:HH:mm}. {entrante.Nombre} continúa hasta máximo {vm.SegmentoFin:HH:mm}.";

            return Volver();
        }
        catch (Exception ex)
        {
            try { await tx.RollbackAsync(); } catch { }
            TempData["Error"] = "No fue posible realizar el relevo: " + ex.Message;
            return Volver();
        }
    
}
}