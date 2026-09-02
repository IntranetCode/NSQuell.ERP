using Microsoft.Data.SqlClient;
using System.Data;

namespace ERP.NSQuell.Servicios.Planeacion;

public interface IPlaneacionSecuenciaService
{
    Task<int> ReacomodarPorInterrupcionAsync(int programaProduccionId, int ejecucionProduccionId, DateTime fechaInicioInterrupcion, DateTime fechaReinicioReal, int usuarioId, SqlConnection cn, SqlTransaction tx, bool trabajarDomingo);

    Task<PlaneacionProyeccionInterrupcionesResultado> ProyectarInterrupcionesActivasAsync(DateTime fechaProyeccion, bool trabajarDomingo, SqlConnection cn);

    Task<int> ReacomodarPorCambioDuracionAsync(int programaProduccionId, int ejecucionProduccionId, DateTime nuevoFinProgramado, decimal horasProgramadasNuevas, int usuarioId, string motivo, SqlConnection cn, SqlTransaction tx, bool trabajarDomingo);

    DateTime AjustarFechaFinOperativa(DateTime fechaFinActual, decimal deltaHoras, bool trabajarDomingo);
}
public sealed class PlaneacionProyeccionInterrupcionesResultado
{
    public DateTime FechaCalculo { get; set; }
    public int TotalInterrupcionesActivas { get; set; }
    public bool HayInterrupcionesActivas =>
        TotalInterrupcionesActivas > 0;
    public List<PlaneacionProyeccionProgramaResultado> Programas { get; set; }
        = new();
}

public sealed class PlaneacionProyeccionProgramaResultado
{
    public int ProgramaProduccionID { get; set; }
    public int? MaquinaID { get; set; }
    public int? MoldeID { get; set; }
    public int? EjecucionProduccionID { get; set; }
    public int? ParoID { get; set; }

    public bool EsProgramaRaizInterrupcion { get; set; }

    public string TipoInterrupcion { get; set; } = string.Empty;
    public string? MotivoParo { get; set; }

    public DateTime InicioOriginal { get; set; }
    public DateTime FinOriginal { get; set; }

    public DateTime InicioProyectado { get; set; }
    public DateTime FinProyectado { get; set; }

    public TimeSpan? CambioOriginal { get; set; }
    public TimeSpan? ArranqueOriginal { get; set; }

    public TimeSpan? CambioProyectado { get; set; }
    public TimeSpan? ArranqueProyectado { get; set; }

    public int MinutosImpactoInterrupcion { get; set; }
    public int MinutosDesplazamiento { get; set; }
}
public sealed class PlaneacionSecuenciaService : IPlaneacionSecuenciaService
{
    public async Task<int> ReacomodarPorInterrupcionAsync(int programaProduccionId, int ejecucionProduccionId, DateTime fechaInicioInterrupcion, DateTime fechaReinicioReal, int usuarioId, SqlConnection cn, SqlTransaction tx, bool trabajarDomingo)
    {
        if (programaProduccionId <= 0)
            return 0;
        if (ejecucionProduccionId <= 0)
            throw new InvalidOperationException("La ejecución de Producción no es válida.");
        if (usuarioId <= 0)
            throw new InvalidOperationException("No fue posible identificar al usuario que realiza el reacomodo.");

        fechaInicioInterrupcion = NormalizarFechaMinuto(fechaInicioInterrupcion);
        fechaReinicioReal = NormalizarFechaMinuto(fechaReinicioReal);

        if (fechaReinicioReal <= fechaInicioInterrupcion)
            return 0;

        await TomarCandadoCalendarioAsync(cn, tx);
        await ActivarReacomodoPlaneacionAsync(cn, tx);

        try
        {
            var programaRaiz = await ObtenerProgramaReacomodoAsync(programaProduccionId, cn, tx, true);
            if (programaRaiz == null)
                throw new InvalidOperationException("No se encontró el programa de Planeación relacionado con la ejecución.");
            if (!programaRaiz.MaquinaID.HasValue)
                throw new InvalidOperationException("El programa que se desea reanudar no tiene máquina asignada.");

            var finAnteriorRaiz = programaRaiz.Fin;
            var minutosProductivosPerdidos = CalcularMinutosOperativosEntre(fechaInicioInterrupcion, fechaReinicioReal, trabajarDomingo);

            if (minutosProductivosPerdidos <= 0)
                return 0;

            var nuevoFinRaiz = SumarHorasOperativas(finAnteriorRaiz, minutosProductivosPerdidos / 60m, trabajarDomingo);
            nuevoFinRaiz = NormalizarFechaMinuto(nuevoFinRaiz);

            await ActualizarFinProgramaRaizAsync(programaRaiz, ejecucionProduccionId, nuevoFinRaiz, usuarioId, cn, tx);
            await InsertarHistorialReacomodoAutomaticoAsync(
                programaRaiz,
                programaRaiz.Inicio,
                nuevoFinRaiz,
                programaRaiz.Cambio,
                programaRaiz.Arranque,
                usuarioId,
                programaProduccionId,
                "RECORRIDO_POR_PARO",
                "La OF se extendió por una interrupción de Producción. Inicio de interrupción: " +
                fechaInicioInterrupcion.ToString("dd/MM/yyyy HH:mm") +
                ". Reinicio real: " +
                fechaReinicioReal.ToString("dd/MM/yyyy HH:mm") +
                ". Minutos productivos recuperados: " +
                minutosProductivosPerdidos + ".",
                cn,
                tx);

            var programas = await CargarProgramasReacomodoGlobalAsync(finAnteriorRaiz, programaProduccionId, cn, tx);
            var raizEnMemoria = programas.FirstOrDefault(x => x.ProgramaProduccionID == programaProduccionId);

            if (raizEnMemoria != null)
            {
                raizEnMemoria.Inicio = programaRaiz.Inicio;
                raizEnMemoria.Fin = nuevoFinRaiz;
                raizEnMemoria.EsMovible = false;
            }

            var reservados = programas
                .Where(x => !x.EsMovible)
                .OrderBy(x => x.Inicio)
                .ThenBy(x => x.ProgramaProduccionID)
                .ToList();

            var movibles = programas
                .Where(x => x.EsMovible)
                .OrderBy(x => x.InicioOriginal)
                .ThenBy(x => x.SecuenciaMaquina)
                .ThenBy(x => x.ProgramaProduccionID)
                .ToList();

            var reacomodados = 0;

            foreach (var programa in movibles)
            {
                var posicion = CalcularPosicionGlobalSinCruces(programa, reservados, trabajarDomingo);
                var cambioDiferente = programa.Inicio != posicion.Cambio;
                var arranqueDiferente = programa.ArranqueFecha != posicion.Arranque;
                var finDiferente = programa.Fin != posicion.Fin;

                if (cambioDiferente || arranqueDiferente || finDiferente)
                {
                    var inicioAnterior = programa.Inicio;
                    var finAnterior = programa.Fin;

                    await ActualizarProgramaReacomodoGlobalAsync(programa, posicion.Cambio, posicion.Arranque, posicion.Fin, usuarioId, cn, tx);

                    await InsertarHistorialReacomodoAutomaticoAsync(
                        programa,
                        posicion.Cambio,
                        posicion.Fin,
                        posicion.Cambio.TimeOfDay,
                        posicion.Arranque.TimeOfDay,
                        usuarioId,
                        programaProduccionId,
                        posicion.MovidoPorMolde ? "RECORRIDO_POR_MOLDE" : "RECORRIDO_POR_COLA",
                        posicion.MovidoPorMolde
                            ? "Programa recorrido automáticamente porque el molde " +
                              (programa.MoldeCodigo ?? programa.MoldeID?.ToString() ?? "-") +
                              " quedó ocupado por una reprogramación previa. Anterior: " +
                              inicioAnterior.ToString("dd/MM/yyyy HH:mm") + " - " +
                              finAnterior.ToString("dd/MM/yyyy HH:mm") + ". Nuevo: " +
                              posicion.Cambio.ToString("dd/MM/yyyy HH:mm") + " - " +
                              posicion.Fin.ToString("dd/MM/yyyy HH:mm") + "."
                            : "Programa recorrido automáticamente por la cola de su máquina. Anterior: " +
                              inicioAnterior.ToString("dd/MM/yyyy HH:mm") + " - " +
                              finAnterior.ToString("dd/MM/yyyy HH:mm") + ". Nuevo: " +
                              posicion.Cambio.ToString("dd/MM/yyyy HH:mm") + " - " +
                              posicion.Fin.ToString("dd/MM/yyyy HH:mm") + ".",
                        cn,
                        tx);

                    programa.Inicio = posicion.Cambio;
                    programa.Fin = posicion.Fin;
                    programa.Cambio = posicion.Cambio.TimeOfDay;
                    programa.Arranque = posicion.Arranque.TimeOfDay;
                    programa.ArranqueFecha = posicion.Arranque;
                    reacomodados++;
                }

                reservados.Add(programa);
            }

            await ReordenarSecuenciasAsync(
                programas
                    .Where(x => x.MaquinaID.HasValue)
                    .Select(x => x.MaquinaID!.Value)
                    .Distinct()
                    .ToList(),
                cn,
                tx);

            return reacomodados;
        }
        finally
        {
            await DesactivarReacomodoPlaneacionAsync(cn, tx);
        }
    }

    public async Task<int> ReacomodarPorCambioDuracionAsync(int programaProduccionId, int ejecucionProduccionId, DateTime nuevoFinProgramado, decimal horasProgramadasNuevas, int usuarioId, string motivo, SqlConnection cn, SqlTransaction tx, bool trabajarDomingo)
    {
        if (programaProduccionId <= 0)
            throw new InvalidOperationException("El programa de Producción no es válido.");

        if (ejecucionProduccionId <= 0)
            throw new InvalidOperationException("La ejecución de Producción no es válida.");

        if (usuarioId <= 0)
            throw new InvalidOperationException("No fue posible identificar al usuario.");

        if (horasProgramadasNuevas <= 0)
            throw new InvalidOperationException("Las nuevas horas programadas deben ser mayores a cero.");

        nuevoFinProgramado = NormalizarFechaMinuto(nuevoFinProgramado);
        horasProgramadasNuevas = Math.Round(horasProgramadasNuevas, 4, MidpointRounding.AwayFromZero);

        await TomarCandadoCalendarioAsync(cn, tx);
        await ActivarReacomodoPlaneacionAsync(cn, tx);

        try
        {
            var programaRaiz = await ObtenerProgramaReacomodoAsync(programaProduccionId, cn, tx, true);

            if (programaRaiz == null)
                throw new InvalidOperationException("No se encontró el programa relacionado con la ejecución.");

            if (!programaRaiz.MaquinaID.HasValue)
                throw new InvalidOperationException("El programa no tiene máquina asignada.");

            if (nuevoFinProgramado <= programaRaiz.Inicio)
                throw new InvalidOperationException("El nuevo fin calculado no puede ser anterior al inicio del programa.");

            var finAnterior = programaRaiz.Fin;
            var horasAnteriores = programaRaiz.HorasProgramadas;

            if (nuevoFinProgramado == finAnterior && Math.Abs(horasProgramadasNuevas - horasAnteriores) < 0.0001m)
                return 0;

            await ActualizarProgramaRaizPorCambioDuracionAsync(programaRaiz, ejecucionProduccionId, nuevoFinProgramado, horasProgramadasNuevas, usuarioId, cn, tx);

            await InsertarHistorialReacomodoAutomaticoAsync(
                programaRaiz,
                programaRaiz.Inicio,
                nuevoFinProgramado,
                programaRaiz.Cambio,
                programaRaiz.Arranque,
                usuarioId,
                programaProduccionId,
                "RECALCULO_POR_CONFIGURACION",
                string.IsNullOrWhiteSpace(motivo)
                    ? $"La duración de la OF cambió por modificación de cavidades/ciclo. Fin anterior: {finAnterior:dd/MM/yyyy HH:mm}. Nuevo fin: {nuevoFinProgramado:dd/MM/yyyy HH:mm}. Horas anteriores: {horasAnteriores:0.####}. Horas nuevas: {horasProgramadasNuevas:0.####}."
                    : motivo,
                cn,
                tx,
                horasProgramadasNuevas);

            /*
             * Si la nueva configuración termina antes, NO jalamos las OF
             * posteriores hacia atrás. Planeación decidirá qué hacer con
             * el hueco disponible.
             */
            if (nuevoFinProgramado <= finAnterior)
                return 0;

            var programas = await CargarProgramasReacomodoGlobalAsync(finAnterior, programaProduccionId, cn, tx);
            var raizEnMemoria = programas.FirstOrDefault(x => x.ProgramaProduccionID == programaProduccionId);

            if (raizEnMemoria != null)
            {
                raizEnMemoria.Inicio = programaRaiz.Inicio;
                raizEnMemoria.Fin = nuevoFinProgramado;
                raizEnMemoria.HorasProgramadas = horasProgramadasNuevas;
                raizEnMemoria.EsMovible = false;
            }

            var reservados = programas
                .Where(x => !x.EsMovible)
                .OrderBy(x => x.Inicio)
                .ThenBy(x => x.ProgramaProduccionID)
                .ToList();

            var movibles = programas
                .Where(x => x.EsMovible)
                .OrderBy(x => x.InicioOriginal)
                .ThenBy(x => x.SecuenciaMaquina)
                .ThenBy(x => x.ProgramaProduccionID)
                .ToList();

            var reacomodados = 0;

            foreach (var programa in movibles)
            {
                var posicion = CalcularPosicionGlobalSinCruces(programa, reservados, trabajarDomingo);

                var cambioDiferente = programa.Inicio != posicion.Cambio;
                var arranqueDiferente = programa.ArranqueFecha != posicion.Arranque;
                var finDiferente = programa.Fin != posicion.Fin;

                if (cambioDiferente || arranqueDiferente || finDiferente)
                {
                    var inicioAnteriorPrograma = programa.Inicio;
                    var finAnteriorPrograma = programa.Fin;

                    await ActualizarProgramaReacomodoGlobalAsync(programa, posicion.Cambio, posicion.Arranque, posicion.Fin, usuarioId, cn, tx);

                    await InsertarHistorialReacomodoAutomaticoAsync(
                        programa,
                        posicion.Cambio,
                        posicion.Fin,
                        posicion.Cambio.TimeOfDay,
                        posicion.Arranque.TimeOfDay,
                        usuarioId,
                        programaProduccionId,
                        posicion.MovidoPorMolde ? "RECORRIDO_POR_MOLDE" : "RECORRIDO_POR_COLA",
                        posicion.MovidoPorMolde
                            ? $"Programa recorrido porque el cambio de cavidades/ciclo de la OF {programaProduccionId} provocó un conflicto con el molde {(programa.MoldeCodigo ?? programa.MoldeID?.ToString() ?? "-")}. Anterior: {inicioAnteriorPrograma:dd/MM/yyyy HH:mm} - {finAnteriorPrograma:dd/MM/yyyy HH:mm}. Nuevo: {posicion.Cambio:dd/MM/yyyy HH:mm} - {posicion.Fin:dd/MM/yyyy HH:mm}."
                            : $"Programa recorrido porque cambió la duración de la OF {programaProduccionId}. Anterior: {inicioAnteriorPrograma:dd/MM/yyyy HH:mm} - {finAnteriorPrograma:dd/MM/yyyy HH:mm}. Nuevo: {posicion.Cambio:dd/MM/yyyy HH:mm} - {posicion.Fin:dd/MM/yyyy HH:mm}.",
                        cn,
                        tx);

                    programa.Inicio = posicion.Cambio;
                    programa.Fin = posicion.Fin;
                    programa.Cambio = posicion.Cambio.TimeOfDay;
                    programa.Arranque = posicion.Arranque.TimeOfDay;
                    programa.ArranqueFecha = posicion.Arranque;
                    reacomodados++;
                }

                reservados.Add(programa);
            }

            await ReordenarSecuenciasAsync(
                programas
                    .Where(x => x.MaquinaID.HasValue)
                    .Select(x => x.MaquinaID!.Value)
                    .Distinct()
                    .ToList(),
                cn,
                tx);

            return reacomodados;
        }
        finally
        {
            await DesactivarReacomodoPlaneacionAsync(cn, tx);
        }
    }

    public async Task<PlaneacionProyeccionInterrupcionesResultado> ProyectarInterrupcionesActivasAsync(DateTime fechaProyeccion, bool trabajarDomingo, SqlConnection cn)
    {
        fechaProyeccion = NormalizarFechaMinuto(fechaProyeccion);
        if (cn.State != ConnectionState.Open) await cn.OpenAsync();

        var resultado = new PlaneacionProyeccionInterrupcionesResultado
        {
            FechaCalculo = fechaProyeccion
        };

        var interrupciones = await CargarInterrupcionesActivasProyeccionAsync(cn);
        if (interrupciones.Count == 0) return resultado;

        var programas = await CargarProgramasReacomodoLecturaAsync(cn);
        if (programas.Count == 0) return resultado;

        var programasPorId = programas.ToDictionary(x => x.ProgramaProduccionID);

        /*
         * Un paro LH/RH son dos registros de Produccion_Paros,
         * pero físicamente es UNA sola interrupción de máquina.
         */
        var gruposFisicos = interrupciones
            .GroupBy(x =>
                x.EsParoLhRh && x.GrupoParoLhRh.HasValue
                    ? $"LHRH:{x.GrupoParoLhRh.Value:N}"
                    : $"PARO:{x.ParoID}")
            .Select(g => new
            {
                FechaInicioFisica = g.Min(x => x.FechaInicioParo),
                Interrupciones = g
                    .Where(x => x.ProgramaProduccionID > 0 && programasPorId.ContainsKey(x.ProgramaProduccionID))
                    .GroupBy(x => x.ProgramaProduccionID)
                    .Select(x => x.OrderBy(y => y.FechaInicioParo).ThenBy(y => y.ParoID).First())
                    .ToList()
            })
            .Where(x => x.Interrupciones.Count > 0)
            .ToList();

        resultado.TotalInterrupcionesActivas = gruposFisicos.Count;
        if (resultado.TotalInterrupcionesActivas == 0) return resultado;

        var interrupcionesRaiz = new List<InterrupcionActivaProyeccion>();

        foreach (var grupo in gruposFisicos)
        {
            foreach (var interrupcion in grupo.Interrupciones)
            {
                interrupcion.FechaInicioFisica = grupo.FechaInicioFisica;
                interrupcionesRaiz.Add(interrupcion);
            }
        }

        var programasRaizIds = interrupcionesRaiz
            .Select(x => x.ProgramaProduccionID)
            .ToHashSet();

        var proyecciones = new Dictionary<int, PlaneacionProyeccionProgramaResultado>();

        /*
         * Extender en memoria las OF realmente interrumpidas.
         * Para LH/RH las dos reciben exactamente el mismo impacto físico.
         */
        foreach (var interrupcion in interrupcionesRaiz)
        {
            var programa = programasPorId[interrupcion.ProgramaProduccionID];
            programa.EsMovible = false;

            var minutosImpacto = CalcularMinutosOperativosEntre(
                interrupcion.FechaInicioFisica,
                fechaProyeccion,
                trabajarDomingo);

            if (minutosImpacto < 0) minutosImpacto = 0;

            var finProyectado = SumarHorasOperativas(
                programa.FinOriginal,
                minutosImpacto / 60m,
                trabajarDomingo);

            finProyectado = NormalizarFechaMinuto(finProyectado);

            programa.Inicio = programa.InicioOriginal;
            programa.Fin = finProyectado;

            var tipoInterrupcion = interrupcion.EsInterrupcionUrgente
                ? interrupcion.FechaFinParo.HasValue
                    ? "INTERRUPCION_URGENTE_PENDIENTE_REINICIO"
                    : "INTERRUPCION_URGENTE"
                : interrupcion.FechaFinParo.HasValue
                    ? "PARO_MAYOR_15_PENDIENTE_REINICIO"
                    : "PARO_ABIERTO";

            proyecciones[programa.ProgramaProduccionID] = new PlaneacionProyeccionProgramaResultado
            {
                ProgramaProduccionID = programa.ProgramaProduccionID,
                MaquinaID = programa.MaquinaID,
                MoldeID = programa.MoldeID,
                EjecucionProduccionID = interrupcion.EjecucionProduccionID,
                ParoID = interrupcion.ParoID,
                EsProgramaRaizInterrupcion = true,
                TipoInterrupcion = tipoInterrupcion,
                MotivoParo = interrupcion.MotivoParo,
                InicioOriginal = programa.InicioOriginal,
                FinOriginal = programa.FinOriginal,
                InicioProyectado = programa.InicioOriginal,
                FinProyectado = finProyectado,
                CambioOriginal = programa.Cambio,
                ArranqueOriginal = programa.Arranque,
                CambioProyectado = programa.Cambio,
                ArranqueProyectado = programa.Arranque,
                MinutosImpactoInterrupcion = minutosImpacto,
                MinutosDesplazamiento = minutosImpacto
            };
        }

        var desdeImpacto = interrupcionesRaiz
            .Select(x => programasPorId[x.ProgramaProduccionID].FinOriginal)
            .Min();

        var reservados = programas
            .Where(x =>
                !x.EsMovible ||
                programasRaizIds.Contains(x.ProgramaProduccionID) ||
                x.FinOriginal < desdeImpacto)
            .OrderBy(x => x.Inicio)
            .ThenBy(x => x.ProgramaProduccionID)
            .ToList();

        var movibles = programas
            .Where(x =>
                x.EsMovible &&
                !programasRaizIds.Contains(x.ProgramaProduccionID) &&
                x.FinOriginal >= desdeImpacto)
            .OrderBy(x => x.InicioOriginal)
            .ThenBy(x => x.SecuenciaMaquina)
            .ThenBy(x => x.ProgramaProduccionID)
            .ToList();

        foreach (var programa in movibles)
        {
            var cambioOriginal = programa.Cambio;
            var arranqueOriginal = programa.Arranque;

            var posicion = CalcularPosicionGlobalSinCruces(
                programa,
                reservados,
                trabajarDomingo);

            var cambioDiferente = programa.InicioOriginal != posicion.Cambio;
            var arranqueDiferente = programa.ArranqueFecha != posicion.Arranque;
            var finDiferente = programa.FinOriginal != posicion.Fin;

            if (cambioDiferente || arranqueDiferente || finDiferente)
            {
                var minutosDesplazamiento = (int)Math.Max(
                    0,
                    Math.Round((posicion.Cambio - programa.InicioOriginal).TotalMinutes));

                programa.Inicio = posicion.Cambio;
                programa.Fin = posicion.Fin;
                programa.Cambio = posicion.Cambio.TimeOfDay;
                programa.Arranque = posicion.Arranque.TimeOfDay;
                programa.ArranqueFecha = posicion.Arranque;

                proyecciones[programa.ProgramaProduccionID] = new PlaneacionProyeccionProgramaResultado
                {
                    ProgramaProduccionID = programa.ProgramaProduccionID,
                    MaquinaID = programa.MaquinaID,
                    MoldeID = programa.MoldeID,
                    EjecucionProduccionID = null,
                    ParoID = null,
                    EsProgramaRaizInterrupcion = false,
                    TipoInterrupcion = posicion.MovidoPorMolde ? "IMPACTO_POR_MOLDE" : "IMPACTO_POR_COLA",
                    InicioOriginal = programa.InicioOriginal,
                    FinOriginal = programa.FinOriginal,
                    InicioProyectado = posicion.Cambio,
                    FinProyectado = posicion.Fin,
                    CambioOriginal = cambioOriginal ?? programa.InicioOriginal.TimeOfDay,
                    ArranqueOriginal = arranqueOriginal,
                    CambioProyectado = posicion.Cambio.TimeOfDay,
                    ArranqueProyectado = posicion.Arranque.TimeOfDay,
                    MinutosImpactoInterrupcion = 0,
                    MinutosDesplazamiento = minutosDesplazamiento
                };
            }

            reservados.Add(programa);
        }

        resultado.Programas = proyecciones.Values
            .OrderBy(x => x.InicioProyectado)
            .ThenBy(x => x.MaquinaID)
            .ThenBy(x => x.ProgramaProduccionID)
            .ToList();

        return resultado;
    }

    private static async Task<List<InterrupcionActivaProyeccion>> CargarInterrupcionesActivasProyeccionAsync(SqlConnection cn)
    {
        var lista = new List<InterrupcionActivaProyeccion>();

        const string sql = @"
SELECT
    p.ParoID,
    p.EjecucionProduccionID,
    COALESCE(p.ProgramaProduccionID,e.ProgramaProduccionID) AS ProgramaProduccionID,
    p.FechaInicioParo,
    p.FechaFinParo,
    ISNULL(NULLIF(LTRIM(RTRIM(p.MotivoParoTexto)),N''),N'Paro de Producción') AS MotivoParoTexto,
    ISNULL(p.EsParoLhRh,0) AS EsParoLhRh,
    p.GrupoParoLhRh,
    ISNULL(p.EsInterrupcionUrgente,0) AS EsInterrupcionUrgente,
    p.ProgramaUrgenteID,
    CAST(CASE WHEN p.FechaFinParo IS NULL THEN 1 ELSE 0 END AS BIT) AS EstaAbierto,
    CAST
    (
        CASE
            WHEN ISNULL(p.EsMayorA15Minutos,0)=1 THEN 1
            WHEN p.FechaFinParo IS NOT NULL
             AND DATEDIFF(SECOND,p.FechaInicioParo,p.FechaFinParo)>900 THEN 1
            ELSE 0
        END
        AS BIT
    ) AS EsMayorA15Minutos
FROM dbo.Produccion_Paros p
INNER JOIN dbo.Produccion_Ejecucion e
    ON e.EjecucionProduccionID=p.EjecucionProduccionID
WHERE p.Activo=1
  AND e.Activo=1
  AND ISNULL(p.TerminacionParcialEjecutada,0)=0
  AND
  (
      p.FechaFinParo IS NULL
      OR
      (
          p.FechaFinParo IS NOT NULL
          AND
          (
              ISNULL(p.EsInterrupcionUrgente,0)=1
              OR ISNULL(p.EsMayorA15Minutos,0)=1
              OR DATEDIFF(SECOND,p.FechaInicioParo,p.FechaFinParo)>900
          )
          AND NOT EXISTS
          (
              SELECT 1
              FROM dbo.Calidad_InspeccionHistorial h
              INNER JOIN dbo.Calidad_Inspecciones ci
                  ON ci.InspeccionID=h.InspeccionID
              WHERE ci.EjecucionProduccionID=p.EjecucionProduccionID
                AND h.Movimiento=N'CONFIRMACION_INICIO_SERIE_PRODUCCION'
                AND h.FechaMovimiento>=p.FechaFinParo
          )
      )
  )
ORDER BY p.FechaInicioParo,p.ParoID;";

        await using var cmd = new SqlCommand(sql, cn);
        await using var rd = await cmd.ExecuteReaderAsync();

        while (await rd.ReadAsync())
        {
            lista.Add(new InterrupcionActivaProyeccion
            {
                ParoID = Convert.ToInt32(rd["ParoID"]),
                EjecucionProduccionID = Convert.ToInt32(rd["EjecucionProduccionID"]),
                ProgramaProduccionID = Convert.ToInt32(rd["ProgramaProduccionID"]),
                FechaInicioParo = Convert.ToDateTime(rd["FechaInicioParo"]),
                FechaInicioFisica = Convert.ToDateTime(rd["FechaInicioParo"]),
                FechaFinParo = rd["FechaFinParo"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaFinParo"]),
                MotivoParo = rd["MotivoParoTexto"] == DBNull.Value ? null : rd["MotivoParoTexto"]?.ToString(),
                EsParoLhRh = rd["EsParoLhRh"] != DBNull.Value && Convert.ToBoolean(rd["EsParoLhRh"]),
                GrupoParoLhRh = rd["GrupoParoLhRh"] == DBNull.Value ? null : (Guid?)rd["GrupoParoLhRh"],
                EsInterrupcionUrgente = rd["EsInterrupcionUrgente"] != DBNull.Value && Convert.ToBoolean(rd["EsInterrupcionUrgente"]),
                ProgramaUrgenteID = rd["ProgramaUrgenteID"] == DBNull.Value ? null : Convert.ToInt32(rd["ProgramaUrgenteID"]),
                EstaAbierto = rd["EstaAbierto"] != DBNull.Value && Convert.ToBoolean(rd["EstaAbierto"]),
                EsMayorA15Minutos = rd["EsMayorA15Minutos"] != DBNull.Value && Convert.ToBoolean(rd["EsMayorA15Minutos"])
            });
        }

        return lista;
    }

    private static async Task<List<ProgramaReacomodoGlobal>> CargarProgramasReacomodoLecturaAsync(SqlConnection cn)
    {
        var lista = new List<ProgramaReacomodoGlobal>();

        const string sql = @"
SELECT
    pp.ProgramaProduccionID,
    pp.MaquinaID,
    pp.ParteID,
    pp.MoldeID,
    pp.MoldeCodigo,
    pp.Observaciones,
    pp.ReleaseDetalleID,
    pp.SolicitudProduccionID,
    pp.SolicitudProduccionDetalleID,
    pp.FechaInicioProgramada,
    ISNULL
    (
        pp.FechaFinProgramada,
        DATEADD(MINUTE,CAST(CEILING(ISNULL(pp.HorasProgramadas,1)*60) AS INT),pp.FechaInicioProgramada)
    ) AS FechaFinProgramada,
    ISNULL(pp.HorasProgramadas,0) AS HorasProgramadas,
    pp.Cambio,
    pp.Arranque,
    ISNULL(pp.SecuenciaMaquina,999999) AS SecuenciaMaquina,
    ISNULL(pp.EstatusID,1) AS EstatusID,
    CASE
        WHEN ISNULL(pp.EstatusID,1)<>1 THEN CAST(0 AS BIT)
        WHEN EXISTS
        (
            SELECT 1
            FROM dbo.Produccion_Ejecucion e
            WHERE e.ProgramaProduccionID=pp.ProgramaProduccionID
              AND e.Activo=1
        ) THEN CAST(0 AS BIT)
        WHEN EXISTS
        (
            SELECT 1
            FROM dbo.Calidad_Inspecciones ci
            WHERE ci.ProgramaProduccionID=pp.ProgramaProduccionID
        ) THEN CAST(0 AS BIT)
        ELSE CAST(1 AS BIT)
    END AS EsMovible
FROM dbo.Planeacion_ProgramaProduccion pp
WHERE pp.Activo=1
  AND pp.MaquinaID IS NOT NULL
  AND pp.FechaInicioProgramada IS NOT NULL
  AND ISNULL(pp.EstatusID,1) NOT IN(5,6,9,99)
ORDER BY pp.FechaInicioProgramada,ISNULL(pp.SecuenciaMaquina,999999),pp.ProgramaProduccionID;";

        await using var cmd = new SqlCommand(sql, cn);
        await using var rd = await cmd.ExecuteReaderAsync();

        while (await rd.ReadAsync())
        {
            var inicio = Convert.ToDateTime(rd["FechaInicioProgramada"]);
            var fin = Convert.ToDateTime(rd["FechaFinProgramada"]);
            var cambio = rd["Cambio"] == DBNull.Value ? (TimeSpan?)null : (TimeSpan)rd["Cambio"];
            var arranque = rd["Arranque"] == DBNull.Value ? (TimeSpan?)null : (TimeSpan)rd["Arranque"];
            var observaciones = rd["Observaciones"] == DBNull.Value ? null : rd["Observaciones"]?.ToString();

            lista.Add(new ProgramaReacomodoGlobal
            {
                ProgramaProduccionID = Convert.ToInt32(rd["ProgramaProduccionID"]),
                MaquinaID = rd["MaquinaID"] == DBNull.Value ? null : Convert.ToInt32(rd["MaquinaID"]),
                ParteID = rd["ParteID"] == DBNull.Value ? null : Convert.ToInt32(rd["ParteID"]),
                MoldeID = rd["MoldeID"] == DBNull.Value ? null : Convert.ToInt32(rd["MoldeID"]),
                MoldeCodigo = rd["MoldeCodigo"] == DBNull.Value ? null : rd["MoldeCodigo"]?.ToString(),
                GrupoLhRh = ExtraerGrupoLhRh(observaciones),
                ReleaseDetalleID = rd["ReleaseDetalleID"] == DBNull.Value ? null : Convert.ToInt32(rd["ReleaseDetalleID"]),
                SolicitudProduccionID = rd["SolicitudProduccionID"] == DBNull.Value ? null : Convert.ToInt32(rd["SolicitudProduccionID"]),
                SolicitudProduccionDetalleID = rd["SolicitudProduccionDetalleID"] == DBNull.Value ? null : Convert.ToInt32(rd["SolicitudProduccionDetalleID"]),
                InicioOriginal = inicio,
                FinOriginal = fin,
                Inicio = inicio,
                Fin = fin,
                HorasProgramadas = Convert.ToDecimal(rd["HorasProgramadas"]),
                Cambio = cambio,
                Arranque = arranque,
                ArranqueFecha = ConstruirFechaHoraDesdeTimeSpan(inicio, arranque),
                SecuenciaMaquina = Convert.ToInt32(rd["SecuenciaMaquina"]),
                EstatusID = Convert.ToInt32(rd["EstatusID"]),
                EsMovible = Convert.ToBoolean(rd["EsMovible"])
            });
        }

        return lista;
    }
    private static async Task<ProgramaReacomodoGlobal?> ObtenerProgramaReacomodoAsync(int programaProduccionId, SqlConnection cn, SqlTransaction tx, bool bloquear)
    {
        var hint = bloquear ? " WITH (UPDLOCK, HOLDLOCK)" : string.Empty;

        var sql = $@"
SELECT TOP (1)
    pp.ProgramaProduccionID,
    pp.MaquinaID,
    pp.ParteID,
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
            CAST(CEILING(ISNULL(pp.HorasProgramadas,1)*60) AS INT),
            pp.FechaInicioProgramada
        )
    ) AS FechaFinProgramada,
    ISNULL(pp.HorasProgramadas,0) AS HorasProgramadas,
    pp.Cambio,
    pp.Arranque,
    ISNULL(pp.SecuenciaMaquina,999999) AS SecuenciaMaquina,
    ISNULL(pp.EstatusID,1) AS EstatusID
FROM dbo.Planeacion_ProgramaProduccion pp{hint}
WHERE pp.ProgramaProduccionID=@ProgramaProduccionID
  AND pp.Activo=1;";

        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaProduccionId;

        await using var rd = await cmd.ExecuteReaderAsync();

        if (!await rd.ReadAsync())
            return null;

        var inicio = Convert.ToDateTime(rd["FechaInicioProgramada"]);
        var fin = Convert.ToDateTime(rd["FechaFinProgramada"]);
        var cambio = rd["Cambio"] == DBNull.Value ? (TimeSpan?)null : (TimeSpan)rd["Cambio"];
        var arranque = rd["Arranque"] == DBNull.Value ? (TimeSpan?)null : (TimeSpan)rd["Arranque"];

        return new ProgramaReacomodoGlobal
        {
            ProgramaProduccionID = Convert.ToInt32(rd["ProgramaProduccionID"]),
            MaquinaID = rd["MaquinaID"] == DBNull.Value ? null : Convert.ToInt32(rd["MaquinaID"]),
            ParteID = rd["ParteID"] == DBNull.Value ? null : Convert.ToInt32(rd["ParteID"]),
            MoldeID = rd["MoldeID"] == DBNull.Value ? null : Convert.ToInt32(rd["MoldeID"]),
            MoldeCodigo = rd["MoldeCodigo"] == DBNull.Value ? null : rd["MoldeCodigo"]?.ToString(),
            ReleaseDetalleID = rd["ReleaseDetalleID"] == DBNull.Value ? null : Convert.ToInt32(rd["ReleaseDetalleID"]),
            SolicitudProduccionID = rd["SolicitudProduccionID"] == DBNull.Value ? null : Convert.ToInt32(rd["SolicitudProduccionID"]),
            SolicitudProduccionDetalleID = rd["SolicitudProduccionDetalleID"] == DBNull.Value ? null : Convert.ToInt32(rd["SolicitudProduccionDetalleID"]),
            InicioOriginal = inicio,
            FinOriginal = fin,
            Inicio = inicio,
            Fin = fin,
            HorasProgramadas = Convert.ToDecimal(rd["HorasProgramadas"]),
            Cambio = cambio,
            Arranque = arranque,
            ArranqueFecha = ConstruirFechaHoraDesdeTimeSpan(inicio, arranque),
            SecuenciaMaquina = Convert.ToInt32(rd["SecuenciaMaquina"]),
            EstatusID = Convert.ToInt32(rd["EstatusID"])
        };
    }

    private static async Task<List<ProgramaReacomodoGlobal>> CargarProgramasReacomodoGlobalAsync(DateTime desdeImpacto, int programaRaizId, SqlConnection cn, SqlTransaction tx)
    {
        var lista = new List<ProgramaReacomodoGlobal>();

        const string sql = @"
SELECT
    pp.ProgramaProduccionID,
    pp.MaquinaID,
    pp.ParteID,
    pp.MoldeID,
    pp.MoldeCodigo,
    pp.Observaciones,
    pp.ReleaseDetalleID,
    pp.SolicitudProduccionID,
    pp.SolicitudProduccionDetalleID,
    pp.FechaInicioProgramada,
    ISNULL
    (
        pp.FechaFinProgramada,
        DATEADD(MINUTE,CAST(CEILING(ISNULL(pp.HorasProgramadas,1)*60) AS INT),pp.FechaInicioProgramada)
    ) AS FechaFinProgramada,
    ISNULL(pp.HorasProgramadas,0) AS HorasProgramadas,
    pp.Cambio,
    pp.Arranque,
    ISNULL(pp.SecuenciaMaquina,999999) AS SecuenciaMaquina,
    ISNULL(pp.EstatusID,1) AS EstatusID,
    CASE
        WHEN pp.ProgramaProduccionID=@ProgramaRaizID THEN CAST(0 AS BIT)
        WHEN ISNULL(pp.EstatusID,1)<>1 THEN CAST(0 AS BIT)
        WHEN EXISTS
        (
            SELECT 1
            FROM dbo.Produccion_Ejecucion e
            WHERE e.ProgramaProduccionID=pp.ProgramaProduccionID
              AND e.Activo=1
        ) THEN CAST(0 AS BIT)
        WHEN EXISTS
        (
            SELECT 1
            FROM dbo.Calidad_Inspecciones ci
            WHERE ci.ProgramaProduccionID=pp.ProgramaProduccionID
        ) THEN CAST(0 AS BIT)
        ELSE CAST(1 AS BIT)
    END AS EsMovible
FROM dbo.Planeacion_ProgramaProduccion pp WITH(UPDLOCK,HOLDLOCK)
WHERE pp.Activo=1
  AND pp.MaquinaID IS NOT NULL
  AND pp.FechaInicioProgramada IS NOT NULL
  AND ISNULL(pp.EstatusID,1) NOT IN(5,6,9,99)
  AND ISNULL
  (
      pp.FechaFinProgramada,
      DATEADD(MINUTE,CAST(CEILING(ISNULL(pp.HorasProgramadas,1)*60) AS INT),pp.FechaInicioProgramada)
  )>=@DesdeImpacto
ORDER BY pp.FechaInicioProgramada,ISNULL(pp.SecuenciaMaquina,999999),pp.ProgramaProduccionID;";

        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@ProgramaRaizID", SqlDbType.Int).Value = programaRaizId;
        cmd.Parameters.Add("@DesdeImpacto", SqlDbType.DateTime).Value = desdeImpacto;
        await using var rd = await cmd.ExecuteReaderAsync();

        while (await rd.ReadAsync())
        {
            var inicio = Convert.ToDateTime(rd["FechaInicioProgramada"]);
            var fin = Convert.ToDateTime(rd["FechaFinProgramada"]);
            var cambio = rd["Cambio"] == DBNull.Value ? (TimeSpan?)null : (TimeSpan)rd["Cambio"];
            var arranque = rd["Arranque"] == DBNull.Value ? (TimeSpan?)null : (TimeSpan)rd["Arranque"];
            var observaciones = rd["Observaciones"] == DBNull.Value ? null : rd["Observaciones"]?.ToString();

            lista.Add(new ProgramaReacomodoGlobal
            {
                ProgramaProduccionID = Convert.ToInt32(rd["ProgramaProduccionID"]),
                MaquinaID = rd["MaquinaID"] == DBNull.Value ? null : Convert.ToInt32(rd["MaquinaID"]),
                ParteID = rd["ParteID"] == DBNull.Value ? null : Convert.ToInt32(rd["ParteID"]),
                MoldeID = rd["MoldeID"] == DBNull.Value ? null : Convert.ToInt32(rd["MoldeID"]),
                MoldeCodigo = rd["MoldeCodigo"] == DBNull.Value ? null : rd["MoldeCodigo"]?.ToString(),
                GrupoLhRh = ExtraerGrupoLhRh(observaciones),
                ReleaseDetalleID = rd["ReleaseDetalleID"] == DBNull.Value ? null : Convert.ToInt32(rd["ReleaseDetalleID"]),
                SolicitudProduccionID = rd["SolicitudProduccionID"] == DBNull.Value ? null : Convert.ToInt32(rd["SolicitudProduccionID"]),
                SolicitudProduccionDetalleID = rd["SolicitudProduccionDetalleID"] == DBNull.Value ? null : Convert.ToInt32(rd["SolicitudProduccionDetalleID"]),
                InicioOriginal = inicio,
                FinOriginal = fin,
                Inicio = inicio,
                Fin = fin,
                HorasProgramadas = Convert.ToDecimal(rd["HorasProgramadas"]),
                Cambio = cambio,
                Arranque = arranque,
                ArranqueFecha = ConstruirFechaHoraDesdeTimeSpan(inicio, arranque),
                SecuenciaMaquina = Convert.ToInt32(rd["SecuenciaMaquina"]),
                EstatusID = Convert.ToInt32(rd["EstatusID"]),
                EsMovible = Convert.ToBoolean(rd["EsMovible"])
            });
        }

        return lista;
    }
    private static PosicionReacomodoGlobal CalcularPosicionGlobalSinCruces(ProgramaReacomodoGlobal programa, List<ProgramaReacomodoGlobal> reservados, bool trabajarDomingo)
    {
        var cursor = programa.InicioOriginal;
        var movidoPorMolde = false;

        bool EsMismaParejaLhRh(ProgramaReacomodoGlobal otro)
        {
            return !string.IsNullOrWhiteSpace(programa.GrupoLhRh) &&
                   !string.IsNullOrWhiteSpace(otro.GrupoLhRh) &&
                   string.Equals(programa.GrupoLhRh, otro.GrupoLhRh, StringComparison.OrdinalIgnoreCase);
        }

        for (var intento = 0; intento < 2000; intento++)
        {
            cursor = SiguienteAperturaOperativa(cursor, trabajarDomingo);
            cursor = RedondearSiguienteBloque(cursor, 15);

            var anteriorMaquina = reservados
                .Where(x =>
                    x.ProgramaProduccionID != programa.ProgramaProduccionID &&
                    !EsMismaParejaLhRh(x) &&
                    x.MaquinaID.HasValue &&
                    programa.MaquinaID.HasValue &&
                    x.MaquinaID.Value == programa.MaquinaID.Value &&
                    x.Fin <= cursor)
                .OrderByDescending(x => x.Fin)
                .ThenByDescending(x => x.ProgramaProduccionID)
                .FirstOrDefault();

            var mismaParte =
                programa.ParteID.HasValue &&
                anteriorMaquina?.ParteID.HasValue == true &&
                programa.ParteID.Value == anteriorMaquina.ParteID.Value;

            var mismoMolde =
                programa.MoldeID.HasValue &&
                anteriorMaquina?.MoldeID.HasValue == true &&
                programa.MoldeID.Value == anteriorMaquina.MoldeID.Value;

            var horasCambio = anteriorMaquina != null && !mismaParte && !mismoMolde ? 1m : 0m;
            var cambio = cursor;
            var arranque = SumarHorasOperativas(cambio, horasCambio, trabajarDomingo);
            var horasProduccion = programa.HorasProgramadas > 0 ? programa.HorasProgramadas : 1m;
            var fin = SumarHorasOperativas(arranque, horasProduccion, trabajarDomingo);

            var conflictos = reservados
                .Where(x =>
                    x.ProgramaProduccionID != programa.ProgramaProduccionID &&
                    !EsMismaParejaLhRh(x) &&
                    IntervalosSeCruzan(cambio, fin, x.Inicio, x.Fin) &&
                    (
                        (x.MaquinaID.HasValue && programa.MaquinaID.HasValue && x.MaquinaID.Value == programa.MaquinaID.Value)
                        ||
                        (x.MoldeID.HasValue && programa.MoldeID.HasValue && x.MoldeID.Value == programa.MoldeID.Value)
                    ))
                .ToList();

            if (!conflictos.Any())
            {
                return new PosicionReacomodoGlobal
                {
                    Cambio = cambio,
                    Arranque = arranque,
                    Fin = fin,
                    HorasCambio = horasCambio,
                    MovidoPorMolde = movidoPorMolde
                };
            }

            if (conflictos.Any(x =>
                x.MoldeID.HasValue &&
                programa.MoldeID.HasValue &&
                x.MoldeID.Value == programa.MoldeID.Value &&
                (!x.MaquinaID.HasValue || !programa.MaquinaID.HasValue || x.MaquinaID.Value != programa.MaquinaID.Value)))
            {
                movidoPorMolde = true;
            }

            cursor = conflictos.Max(x => x.Fin);
        }

        throw new InvalidOperationException("No fue posible reacomodar automáticamente la programación sin cruces de máquina o molde.");
    }
    private static async Task ActualizarFinProgramaRaizAsync(ProgramaReacomodoGlobal programa, int ejecucionProduccionId, DateTime nuevoFin, int usuarioId, SqlConnection cn, SqlTransaction tx)
    {
        const string sql = @"
UPDATE dbo.Planeacion_ProgramaProduccion
SET
    FechaFinProgramada=@FechaFin,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=GETDATE()
WHERE ProgramaProduccionID=@ProgramaProduccionID
  AND Activo=1;

IF @@ROWCOUNT<>1
BEGIN
    THROW 51021,'No fue posible extender el programa que sufrió el paro.',1;
END;

UPDATE dbo.Calidad_Inspecciones
SET
    FechaFinProgramada=@FechaFin,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=GETDATE()
WHERE EjecucionProduccionID=@EjecucionProduccionID
  AND ISNULL(Estado,N'')<>N'CERRADA';

UPDATE s
SET s.FechaFinPlaneada=@FechaFin
FROM dbo.SolicitudesProduccion s
INNER JOIN dbo.Planeacion_ProgramaProduccion pp
    ON pp.SolicitudProduccionID=s.SolicitudProduccionID
WHERE pp.ProgramaProduccionID=@ProgramaProduccionID;

UPDATE am
SET
    am.FechaProgramadaTentativa=CAST(pp.FechaInicioProgramada AS date),
    am.HoraInicioTentativa=CAST(pp.FechaInicioProgramada AS time),
    am.HoraFinTentativa=CAST(@FechaFin AS time),
    am.HorasEstimadas=pp.HorasProgramadas
FROM dbo.SolicitudesProduccionAsignacionMaquina am
INNER JOIN dbo.Planeacion_ProgramaProduccion pp
    ON pp.SolicitudProduccionDetalleID=am.SolicitudProduccionDetalleID
WHERE pp.ProgramaProduccionID=@ProgramaProduccionID
  AND am.Activo=1;

UPDATE rd
SET
    rd.FechaFinEstimada=@FechaFin,
    rd.DaTiempo=
        CASE
            WHEN rd.FechaRequerida IS NULL THEN NULL
            WHEN CONVERT(date,@FechaFin)<=CONVERT(date,rd.FechaRequerida) THEN 1
            ELSE 0
        END,
    rd.MensajeCapacidad=
        CASE
            WHEN rd.FechaRequerida IS NULL THEN N'Programa extendido por paro. Sin fecha requerida del cliente.'
            WHEN CONVERT(date,@FechaFin)<=CONVERT(date,rd.FechaRequerida) THEN N'Programa extendido por paro dentro de la fecha requerida.'
            ELSE N'Programa extendido por paro posterior a la fecha requerida.'
        END,
    rd.FechaModificacion=GETDATE()
FROM dbo.Planeacion_ReleaseDetalle rd
INNER JOIN dbo.Planeacion_ProgramaProduccion pp
    ON pp.ReleaseDetalleID=rd.ReleaseDetalleID
WHERE pp.ProgramaProduccionID=@ProgramaProduccionID
  AND rd.Activo=1;";

        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programa.ProgramaProduccionID;
        cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
        cmd.Parameters.Add("@FechaFin", SqlDbType.DateTime).Value = nuevoFin;
        cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task ActualizarProgramaReacomodoGlobalAsync(ProgramaReacomodoGlobal programa, DateTime cambio, DateTime arranque, DateTime fin, int usuarioId, SqlConnection cn, SqlTransaction tx)
    {
        const string sql = @"
UPDATE dbo.Planeacion_ProgramaProduccion
SET
    FechaInicioProgramada=@FechaInicio,
    FechaFinProgramada=@FechaFin,
    Cambio=@Cambio,
    Arranque=@Arranque,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=GETDATE()
WHERE ProgramaProduccionID=@ProgramaProduccionID
  AND Activo=1
  AND ISNULL(EstatusID,1)=1
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.Produccion_Ejecucion e
      WHERE e.ProgramaProduccionID=@ProgramaProduccionID
        AND e.Activo=1
  )
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.Calidad_Inspecciones ci
      WHERE ci.ProgramaProduccionID=@ProgramaProduccionID
  );

IF @@ROWCOUNT<>1
BEGIN
    THROW 51022,'Uno de los programas que debía recorrerse ya inició Producción o Calidad.',1;
END;

UPDATE d
SET
    d.HorasPlaneadas=pp.HorasProgramadas,
    d.Cambio=@Cambio,
    d.Arranque=@Arranque
FROM dbo.SolicitudesProduccionDetalle d
INNER JOIN dbo.Planeacion_ProgramaProduccion pp
    ON pp.SolicitudProduccionDetalleID=d.SolicitudProduccionDetalleID
WHERE pp.ProgramaProduccionID=@ProgramaProduccionID;

UPDATE am
SET
    am.FechaProgramadaTentativa=CAST(@FechaInicio AS date),
    am.HoraInicioTentativa=CAST(@FechaInicio AS time),
    am.HoraFinTentativa=CAST(@FechaFin AS time),
    am.HorasEstimadas=pp.HorasProgramadas
FROM dbo.SolicitudesProduccionAsignacionMaquina am
INNER JOIN dbo.Planeacion_ProgramaProduccion pp
    ON pp.SolicitudProduccionDetalleID=am.SolicitudProduccionDetalleID
WHERE pp.ProgramaProduccionID=@ProgramaProduccionID
  AND am.Activo=1;

UPDATE s
SET
    s.FechaInicioPlaneada=@FechaInicio,
    s.FechaFinPlaneada=@FechaFin
FROM dbo.SolicitudesProduccion s
INNER JOIN dbo.Planeacion_ProgramaProduccion pp
    ON pp.SolicitudProduccionID=s.SolicitudProduccionID
WHERE pp.ProgramaProduccionID=@ProgramaProduccionID;

UPDATE rd
SET
    rd.FechaInicioSugerida=@FechaInicio,
    rd.FechaFinEstimada=@FechaFin,
    rd.DaTiempo=
        CASE
            WHEN rd.FechaRequerida IS NULL THEN NULL
            WHEN CONVERT(date,@FechaFin)<=CONVERT(date,rd.FechaRequerida) THEN 1
            ELSE 0
        END,
    rd.MensajeCapacidad=
        CASE
            WHEN rd.FechaRequerida IS NULL THEN N'Programa recorrido automáticamente. Sin fecha requerida del cliente.'
            WHEN CONVERT(date,@FechaFin)<=CONVERT(date,rd.FechaRequerida) THEN N'Programa recorrido automáticamente dentro de la fecha requerida.'
            ELSE N'Programa recorrido automáticamente posterior a la fecha requerida.'
        END,
    rd.FechaModificacion=GETDATE()
FROM dbo.Planeacion_ReleaseDetalle rd
INNER JOIN dbo.Planeacion_ProgramaProduccion pp
    ON pp.ReleaseDetalleID=rd.ReleaseDetalleID
WHERE pp.ProgramaProduccionID=@ProgramaProduccionID
  AND rd.Activo=1;";

        await using (var cmd = new SqlCommand(sql, cn, tx))
        {
            cmd.Parameters.Add("@FechaInicio", SqlDbType.DateTime).Value = cambio;
            cmd.Parameters.Add("@FechaFin", SqlDbType.DateTime).Value = fin;
            cmd.Parameters.Add("@Cambio", SqlDbType.Time).Value = cambio.TimeOfDay;
            cmd.Parameters.Add("@Arranque", SqlDbType.Time).Value = arranque.TimeOfDay;
            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programa.ProgramaProduccionID;
            await cmd.ExecuteNonQueryAsync();
        }

        if (programa.MaquinaID.HasValue)
            await RecalcularOperadoresProgramaAsync(programa.ProgramaProduccionID, programa.MaquinaID.Value, cambio, usuarioId, cn, tx);
    }

    private static async Task InsertarHistorialReacomodoAutomaticoAsync(ProgramaReacomodoGlobal programa, DateTime inicioNuevo, DateTime finNuevo, TimeSpan? cambioNuevo, TimeSpan? arranqueNuevo, int usuarioId, int programaOrigenMovimientoId, string tipoMovimiento, string motivo, SqlConnection cn, SqlTransaction tx, decimal? horasNuevas = null)
    {
        const string sql = @"
IF OBJECT_ID(N'dbo.Planeacion_ProgramaReprogramacionHistorial',N'U') IS NOT NULL
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
        TipoMovimiento,
        EsMovimientoAutomatico,
        ProgramaOrigenMovimientoID,
        UsuarioID,
        FechaCambio,
        Motivo
    )
    VALUES
    (
        @ProgramaProduccionID,
        @MaquinaID,
        @MaquinaID,
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
        @TipoMovimiento,
        1,
        @ProgramaOrigenMovimientoID,
        @UsuarioID,
        GETDATE(),
        @Motivo
    );
END;";

        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programa.ProgramaProduccionID;
        cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = (object?)programa.MaquinaID ?? DBNull.Value;
        cmd.Parameters.Add("@InicioAnterior", SqlDbType.DateTime).Value = programa.InicioOriginal;
        cmd.Parameters.Add("@InicioNuevo", SqlDbType.DateTime).Value = inicioNuevo;
        cmd.Parameters.Add("@FinAnterior", SqlDbType.DateTime).Value = programa.FinOriginal;
        cmd.Parameters.Add("@FinNuevo", SqlDbType.DateTime).Value = finNuevo;

        var horasAnteriores = cmd.Parameters.Add("@HorasAnteriores", SqlDbType.Decimal);
        horasAnteriores.Precision = 18;
        horasAnteriores.Scale = 4;
        horasAnteriores.Value = programa.HorasProgramadas;

        var horasNuevasParametro = cmd.Parameters.Add("@HorasNuevas", SqlDbType.Decimal);
        horasNuevasParametro.Precision = 18;
        horasNuevasParametro.Scale = 4;
        horasNuevasParametro.Value = horasNuevas ?? programa.HorasProgramadas;

        cmd.Parameters.Add("@CambioAnterior", SqlDbType.Time).Value = (object?)programa.Cambio ?? DBNull.Value;
        cmd.Parameters.Add("@CambioNuevo", SqlDbType.Time).Value = (object?)cambioNuevo ?? DBNull.Value;
        cmd.Parameters.Add("@ArranqueAnterior", SqlDbType.Time).Value = (object?)programa.Arranque ?? DBNull.Value;
        cmd.Parameters.Add("@ArranqueNuevo", SqlDbType.Time).Value = (object?)arranqueNuevo ?? DBNull.Value;
        cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value = (object?)programa.ReleaseDetalleID ?? DBNull.Value;
        cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = (object?)programa.SolicitudProduccionID ?? DBNull.Value;
        cmd.Parameters.Add("@SolicitudProduccionDetalleID", SqlDbType.Int).Value = (object?)programa.SolicitudProduccionDetalleID ?? DBNull.Value;
        cmd.Parameters.Add("@TipoMovimiento", SqlDbType.NVarChar, 60).Value = string.IsNullOrWhiteSpace(tipoMovimiento) ? "RECORRIDO_POR_COLA" : tipoMovimiento.Trim();
        cmd.Parameters.Add("@ProgramaOrigenMovimientoID", SqlDbType.Int).Value = programaOrigenMovimientoId;
        cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
        cmd.Parameters.Add("@Motivo", SqlDbType.NVarChar, 500).Value =
            string.IsNullOrWhiteSpace(motivo)
                ? "Programa recorrido automáticamente."
                : motivo.Length > 500
                    ? motivo[..500]
                    : motivo;

        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task RecalcularOperadoresProgramaAsync(int programaProduccionId, int maquinaId, DateTime fechaHora, int usuarioId, SqlConnection cn, SqlTransaction tx)
    {
        var operadores = new List<(int PersonaID, int EscalaAsignacionID)>();

        const string sqlOperadores = @"
SELECT TOP (2)
    a.AsignacionID AS EscalaAsignacionID,
    a.PersonalID AS PersonaID
FROM dbo.RRHH_EscalaAsignaciones a
INNER JOIN dbo.RRHH_EscalasPersonal esc
    ON esc.EscalaID=a.EscalaID
   AND esc.Activo=1
   AND esc.Estado=N'Publicada'
INNER JOIN dbo.Persona p
    ON p.PersonaID=a.PersonalID
INNER JOIN dbo.RRHH_EscalaTurnos et
    ON et.EscalaID=a.EscalaID
   AND et.EscalaTurnoID=a.EscalaTurnoID
WHERE a.Activo=1
  AND a.MaquinaID=@MaquinaID
  AND CAST(@FechaHora AS date)>=CAST(a.FechaInicio AS date)
  AND CAST(@FechaHora AS date)<=CAST(a.FechaFin AS date)
  AND ISNULL(p.EsColaboradorActivo,1)=1
  AND
  (
        ISNULL(et.EsFlexible,0)=1
     OR et.HoraInicio IS NULL
     OR et.HoraFin IS NULL
     OR
     (
            ISNULL(et.CruzaDiaSiguiente,0)=0
        AND CAST(@FechaHora AS time)>=et.HoraInicio
        AND CAST(@FechaHora AS time)<et.HoraFin
     )
     OR
     (
            ISNULL(et.CruzaDiaSiguiente,0)=1
        AND
        (
               CAST(@FechaHora AS time)>=et.HoraInicio
            OR CAST(@FechaHora AS time)<et.HoraFin
        )
     )
  )
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.RRHH_NovedadesPersonal n
      WHERE n.EscalaID=a.EscalaID
        AND n.PersonalID=a.PersonalID
        AND n.Activo=1
        AND n.TipoNovedad IN(N'Baja',N'Incapacidad',N'Vacaciones')
        AND CAST(@FechaHora AS date)>=CAST(n.FechaInicio AS date)
        AND CAST(@FechaHora AS date)<=CAST(ISNULL(n.FechaFin,n.FechaInicio) AS date)
  )
ORDER BY
    et.Orden,
    a.AsignacionID DESC;";

        await using (var cmd = new SqlCommand(sqlOperadores, cn, tx))
        {
            cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = maquinaId;
            cmd.Parameters.Add("@FechaHora", SqlDbType.DateTime).Value = fechaHora;

            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                operadores.Add((
                    Convert.ToInt32(rd["PersonaID"]),
                    Convert.ToInt32(rd["EscalaAsignacionID"])));
            }
        }

        const string sqlDesactivar = @"
UPDATE dbo.Planeacion_ProgramaOperadores
SET
    Activo=0,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=GETDATE()
WHERE ProgramaProduccionID=@ProgramaProduccionID
  AND Activo=1;";

        await using (var cmd = new SqlCommand(sqlDesactivar, cn, tx))
        {
            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaProduccionId;
            await cmd.ExecuteNonQueryAsync();
        }

        const string sqlInsertar = @"
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
    @UsuarioID,
    GETDATE()
);";

        for (var i = 0; i < operadores.Count; i++)
        {
            await using var cmd = new SqlCommand(sqlInsertar, cn, tx);
            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaProduccionId;
            cmd.Parameters.Add("@PersonaID", SqlDbType.Int).Value = operadores[i].PersonaID;
            cmd.Parameters.Add("@RolOperador", SqlDbType.NVarChar, 30).Value = i == 0 ? "PRINCIPAL" : "AUXILIAR";
            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private static async Task ReordenarSecuenciasAsync(List<int> maquinas, SqlConnection cn, SqlTransaction tx)
    {
        if (maquinas == null || maquinas.Count == 0)
            return;

        foreach (var maquinaId in maquinas.Distinct())
        {
            const string sql = @"
;WITH Orden AS
(
    SELECT
        ProgramaProduccionID,
        ROW_NUMBER() OVER
        (
            ORDER BY
                FechaInicioProgramada,
                ProgramaProduccionID
        ) AS NuevaSecuencia
    FROM dbo.Planeacion_ProgramaProduccion
    WHERE Activo=1
      AND MaquinaID=@MaquinaID
      AND ISNULL(EstatusID,1) NOT IN(5,6,9,99)
)
UPDATE pp
SET SecuenciaMaquina=o.NuevaSecuencia
FROM dbo.Planeacion_ProgramaProduccion pp
INNER JOIN Orden o
    ON o.ProgramaProduccionID=pp.ProgramaProduccionID;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = maquinaId;
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private static async Task TomarCandadoCalendarioAsync(SqlConnection cn, SqlTransaction tx)
    {
        const string sql = @"
DECLARE @Resultado INT;

EXEC @Resultado=sys.sp_getapplock
    @Resource=N'ERP_PLANEACION_CALENDARIO_MAQUINAS',
    @LockMode=N'Exclusive',
    @LockOwner=N'Transaction',
    @LockTimeout=15000;

IF @Resultado<0
BEGIN
    THROW 51010,'El calendario está siendo actualizado. Intenta nuevamente en unos segundos.',1;
END;";

        await using var cmd = new SqlCommand(sql, cn, tx);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task ActivarReacomodoPlaneacionAsync(SqlConnection cn, SqlTransaction tx)
    {
        const string sql = @"
EXEC sys.sp_set_session_context
    @key=N'PlaneacionPermitirReacomodo',
    @value=1;";

        await using var cmd = new SqlCommand(sql, cn, tx);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task DesactivarReacomodoPlaneacionAsync(SqlConnection cn, SqlTransaction tx)
    {
        const string sql = @"
EXEC sys.sp_set_session_context
    @key=N'PlaneacionPermitirReacomodo',
    @value=NULL;";

        await using var cmd = new SqlCommand(sql, cn, tx);
        await cmd.ExecuteNonQueryAsync();
    }

    private static bool IntervalosSeCruzan(DateTime inicioA, DateTime finA, DateTime inicioB, DateTime finB)
    {
        return inicioA < finB && finA > inicioB;
    }

    private static DateTime ConstruirFechaHoraDesdeTimeSpan(DateTime fechaBase, TimeSpan? hora)
    {
        if (!hora.HasValue)
            return fechaBase;

        var result = fechaBase.Date.Add(hora.Value);

        if (result < fechaBase)
            result = result.AddDays(1);

        return result;
    }

    private static int CalcularMinutosOperativosEntre(DateTime inicio, DateTime fin, bool trabajarDomingo)
    {
        if (fin <= inicio)
            return 0;

        var cursor = inicio;
        var totalMinutos = 0d;
        var guard = 0;

        while (cursor < fin)
        {
            guard++;

            if (guard > 5000)
                throw new InvalidOperationException("No fue posible calcular los minutos operativos de la interrupción.");

            cursor = SiguienteAperturaOperativa(cursor, trabajarDomingo);

            if (cursor >= fin)
                break;

            var cierre = FinVentanaOperativa(cursor, trabajarDomingo);
            var hasta = cierre < fin ? cierre : fin;

            if (hasta > cursor)
                totalMinutos += (hasta - cursor).TotalMinutes;

            if (hasta >= fin)
                break;

            cursor = SiguienteAperturaOperativa(cierre.AddMinutes(1), trabajarDomingo);
        }

        return (int)Math.Round(totalMinutos);
    }

    private static async Task ActualizarProgramaRaizPorCambioDuracionAsync(ProgramaReacomodoGlobal programa, int ejecucionProduccionId, DateTime nuevoFin, decimal horasProgramadasNuevas, int usuarioId, SqlConnection cn, SqlTransaction tx)
    {
        const string sql = @"
UPDATE dbo.Planeacion_ProgramaProduccion
SET
    FechaFinProgramada=@FechaFin,
    HorasProgramadas=@HorasProgramadas,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=GETDATE()
WHERE ProgramaProduccionID=@ProgramaProduccionID
  AND Activo=1;

IF @@ROWCOUNT<>1
BEGIN
    THROW 51031,'No fue posible actualizar la duración del programa.',1;
END;

UPDATE dbo.Calidad_Inspecciones
SET
    FechaFinProgramada=@FechaFin,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=GETDATE()
WHERE EjecucionProduccionID=@EjecucionProduccionID
  AND ISNULL(Estado,N'')<>N'CERRADA';

UPDATE d
SET
    d.HorasPlaneadas=@HorasProgramadas
FROM dbo.SolicitudesProduccionDetalle d
INNER JOIN dbo.Planeacion_ProgramaProduccion pp
    ON pp.SolicitudProduccionDetalleID=d.SolicitudProduccionDetalleID
WHERE pp.ProgramaProduccionID=@ProgramaProduccionID;

UPDATE s
SET
    s.FechaFinPlaneada=@FechaFin
FROM dbo.SolicitudesProduccion s
INNER JOIN dbo.Planeacion_ProgramaProduccion pp
    ON pp.SolicitudProduccionID=s.SolicitudProduccionID
WHERE pp.ProgramaProduccionID=@ProgramaProduccionID;

UPDATE am
SET
    am.FechaProgramadaTentativa=CAST(pp.FechaInicioProgramada AS date),
    am.HoraInicioTentativa=CAST(pp.FechaInicioProgramada AS time),
    am.HoraFinTentativa=CAST(@FechaFin AS time),
    am.HorasEstimadas=@HorasProgramadas
FROM dbo.SolicitudesProduccionAsignacionMaquina am
INNER JOIN dbo.Planeacion_ProgramaProduccion pp
    ON pp.SolicitudProduccionDetalleID=am.SolicitudProduccionDetalleID
WHERE pp.ProgramaProduccionID=@ProgramaProduccionID
  AND am.Activo=1;

UPDATE rd
SET
    rd.FechaFinEstimada=@FechaFin,
    rd.DaTiempo=
        CASE
            WHEN rd.FechaRequerida IS NULL THEN NULL
            WHEN CONVERT(date,@FechaFin)<=CONVERT(date,rd.FechaRequerida) THEN 1
            ELSE 0
        END,
    rd.MensajeCapacidad=
        CASE
            WHEN rd.FechaRequerida IS NULL
                THEN N'Programa recalculado por cambio de cavidades/ciclo. Sin fecha requerida del cliente.'
            WHEN CONVERT(date,@FechaFin)<=CONVERT(date,rd.FechaRequerida)
                THEN N'Programa recalculado por cambio de cavidades/ciclo dentro de la fecha requerida.'
            ELSE N'Programa recalculado por cambio de cavidades/ciclo posterior a la fecha requerida.'
        END,
    rd.FechaModificacion=GETDATE()
FROM dbo.Planeacion_ReleaseDetalle rd
INNER JOIN dbo.Planeacion_ProgramaProduccion pp
    ON pp.ReleaseDetalleID=rd.ReleaseDetalleID
WHERE pp.ProgramaProduccionID=@ProgramaProduccionID
  AND rd.Activo=1;";

        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programa.ProgramaProduccionID;
        cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
        cmd.Parameters.Add("@FechaFin", SqlDbType.DateTime).Value = nuevoFin;
        cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;

        var horas = cmd.Parameters.Add("@HorasProgramadas", SqlDbType.Decimal);
        horas.Precision = 18;
        horas.Scale = 4;
        horas.Value = horasProgramadasNuevas;

        await cmd.ExecuteNonQueryAsync();
    }

   
    private static DateTime SiguienteAperturaOperativa(DateTime fecha, bool trabajarDomingo)
    {
        var value = fecha;

        while (true)
        {
            if (value.DayOfWeek == DayOfWeek.Sunday)
            {
                if (trabajarDomingo)
                    return value;

                value = value.Date.AddDays(1).AddHours(7);
                continue;
            }

            if (value.DayOfWeek == DayOfWeek.Monday && value.TimeOfDay < TimeSpan.FromHours(7))
                return value.Date.AddHours(7);

            if (value.DayOfWeek == DayOfWeek.Saturday && value.TimeOfDay >= new TimeSpan(22, 30, 0))
            {
                value = value.Date.AddDays(2).AddHours(7);
                continue;
            }

            return value;
        }
    }

    private static DateTime FinVentanaOperativa(DateTime fecha, bool trabajarDomingo)
    {
        if (fecha.DayOfWeek == DayOfWeek.Saturday)
            return fecha.Date.AddHours(22).AddMinutes(30);

        if (fecha.DayOfWeek == DayOfWeek.Sunday)
            return trabajarDomingo ? fecha.Date.AddDays(1) : fecha.Date;

        return fecha.Date.AddDays(1);
    }

    private static DateTime SumarHorasOperativas(DateTime inicio, decimal horas, bool trabajarDomingo)
    {
        if (horas <= 0)
            return SiguienteAperturaOperativa(inicio, trabajarDomingo);

        var cursor = SiguienteAperturaOperativa(inicio, trabajarDomingo);
        var restante = horas;
        var guard = 0;

        while (restante > 0.0001m)
        {
            guard++;

            if (guard > 5000)
                throw new InvalidOperationException("No fue posible calcular el horario operativo del reacomodo.");

            cursor = SiguienteAperturaOperativa(cursor, trabajarDomingo);
            var finVentana = FinVentanaOperativa(cursor, trabajarDomingo);
            var disponible = (decimal)(finVentana - cursor).TotalHours;

            if (disponible <= 0)
            {
                cursor = SiguienteAperturaOperativa(finVentana.AddMinutes(1), trabajarDomingo);
                continue;
            }

            if (restante <= disponible)
                return cursor.AddHours((double)restante);

            restante -= disponible;
            cursor = SiguienteAperturaOperativa(finVentana.AddMinutes(1), trabajarDomingo);
        }

        return cursor;
    }

    public DateTime AjustarFechaFinOperativa(DateTime fechaFinActual, decimal deltaHoras, bool trabajarDomingo)
    {
        fechaFinActual = NormalizarFechaMinuto(fechaFinActual);
        if (Math.Abs(deltaHoras) < 0.0001m)
            return fechaFinActual;
        var resultado = deltaHoras > 0
            ? SumarHorasOperativas(fechaFinActual, deltaHoras, trabajarDomingo)
            : RestarHorasOperativas(fechaFinActual, Math.Abs(deltaHoras), trabajarDomingo);
        return NormalizarFechaMinuto(resultado);
    }

    private static DateTime RestarHorasOperativas(DateTime fin, decimal horas, bool trabajarDomingo)
    {
        if (horas <= 0)
            return fin;
        var restante = horas;
        var ventana = ObtenerVentanaOperativaHaciaAtras(fin, trabajarDomingo);
        var cursor = fin;
        var guard = 0;
        while (restante > 0.0001m)
        {
            guard++;
            if (guard > 5000)
                throw new InvalidOperationException("No fue posible calcular el horario operativo hacia atrás.");
            if (cursor > ventana.Fin)
                cursor = ventana.Fin;
            if (cursor < ventana.Inicio)
            {
                ventana = ObtenerVentanaOperativaAnterior(ventana.Inicio, trabajarDomingo);
                cursor = ventana.Fin;
                continue;
            }
            var disponible = (decimal)(cursor - ventana.Inicio).TotalHours;
            if (restante <= disponible)
                return cursor.AddHours(-(double)restante);
            restante -= disponible;
            ventana = ObtenerVentanaOperativaAnterior(ventana.Inicio, trabajarDomingo);
            cursor = ventana.Fin;
        }
        return cursor;
    }

    private static (DateTime Inicio, DateTime Fin) ObtenerVentanaOperativaHaciaAtras(DateTime fecha, bool trabajarDomingo)
    {
        if (fecha.DayOfWeek == DayOfWeek.Sunday && !trabajarDomingo)
        {
            var sabado = fecha.Date.AddDays(-1);
            return (sabado, sabado.AddHours(22).AddMinutes(30));
        }
        if (fecha.DayOfWeek == DayOfWeek.Monday && fecha.TimeOfDay < TimeSpan.FromHours(7))
        {
            var sabado = fecha.Date.AddDays(-2);
            return (sabado, sabado.AddHours(22).AddMinutes(30));
        }
        if (fecha.DayOfWeek == DayOfWeek.Saturday && fecha.TimeOfDay > new TimeSpan(22, 30, 0))
            return (fecha.Date, fecha.Date.AddHours(22).AddMinutes(30));
        if (fecha.DayOfWeek == DayOfWeek.Monday)
            return (fecha.Date.AddHours(7), fecha.Date.AddDays(1));
        if (fecha.DayOfWeek == DayOfWeek.Saturday)
            return (fecha.Date, fecha.Date.AddHours(22).AddMinutes(30));
        if (fecha.DayOfWeek == DayOfWeek.Sunday)
            return (fecha.Date, fecha.Date.AddDays(1));
        return (fecha.Date, fecha.Date.AddDays(1));
    }

    private static (DateTime Inicio, DateTime Fin) ObtenerVentanaOperativaAnterior(DateTime inicioVentanaActual, bool trabajarDomingo)
    {
        if (inicioVentanaActual.DayOfWeek == DayOfWeek.Monday)
        {
            if (trabajarDomingo)
            {
                var domingo = inicioVentanaActual.Date.AddDays(-1);
                return (domingo, inicioVentanaActual.Date);
            }
            var sabado = inicioVentanaActual.Date.AddDays(-2);
            return (sabado, sabado.AddHours(22).AddMinutes(30));
        }
        if (inicioVentanaActual.DayOfWeek == DayOfWeek.Sunday)
        {
            var sabado = inicioVentanaActual.Date.AddDays(-1);
            return (sabado, sabado.AddHours(22).AddMinutes(30));
        }
        var diaAnterior = inicioVentanaActual.Date.AddDays(-1);
        if (diaAnterior.DayOfWeek == DayOfWeek.Monday)
            return (diaAnterior.AddHours(7), diaAnterior.AddDays(1));
        if (diaAnterior.DayOfWeek == DayOfWeek.Saturday)
            return (diaAnterior, diaAnterior.AddHours(22).AddMinutes(30));
        if (diaAnterior.DayOfWeek == DayOfWeek.Sunday)
        {
            if (trabajarDomingo)
                return (diaAnterior, diaAnterior.AddDays(1));
            var sabado = diaAnterior.AddDays(-1);
            return (sabado, sabado.AddHours(22).AddMinutes(30));
        }
        return (diaAnterior, diaAnterior.AddDays(1));
    }

    private static string? ExtraerGrupoLhRh(string? observaciones)
    {
        if (string.IsNullOrWhiteSpace(observaciones)) return null;
        const string marca = "NSQ_LHRH_PAIR:";
        var posicion = observaciones.IndexOf(marca, StringComparison.OrdinalIgnoreCase);
        if (posicion < 0) return null;
        var inicio = posicion + marca.Length;
        var fin = observaciones.IndexOf(';', inicio);
        var grupo = fin >= 0 ? observaciones[inicio..fin] : observaciones[inicio..];
        grupo = grupo.Trim();
        return string.IsNullOrWhiteSpace(grupo) ? null : grupo;
    }
    private static DateTime RedondearSiguienteBloque(DateTime fecha, int minutos)
    {
        if (minutos <= 0)
            minutos = 15;

        var bloqueTicks = TimeSpan.FromMinutes(minutos).Ticks;
        var resto = fecha.Ticks % bloqueTicks;
        var ticks = resto == 0 ? fecha.Ticks : fecha.Ticks + (bloqueTicks - resto);
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

    private static DateTime NormalizarFechaMinuto(DateTime value)
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

    private sealed class InterrupcionActivaProyeccion
    {
        public int ParoID { get; set; }
        public int EjecucionProduccionID { get; set; }
        public int ProgramaProduccionID { get; set; }
        public DateTime FechaInicioParo { get; set; }
        public DateTime FechaInicioFisica { get; set; }
        public DateTime? FechaFinParo { get; set; }
        public string? MotivoParo { get; set; }
        public bool EstaAbierto { get; set; }
        public bool EsMayorA15Minutos { get; set; }
        public bool EsParoLhRh { get; set; }
        public Guid? GrupoParoLhRh { get; set; }
        public bool EsInterrupcionUrgente { get; set; }
        public int? ProgramaUrgenteID { get; set; }
    }
    private sealed class ProgramaReacomodoGlobal
    {
        public int ProgramaProduccionID { get; set; }
        public int? MaquinaID { get; set; }
        public int? ParteID { get; set; }
        public int? MoldeID { get; set; }
        public string? MoldeCodigo { get; set; }
        public string? GrupoLhRh { get; set; }
        public int? ReleaseDetalleID { get; set; }
        public int? SolicitudProduccionID { get; set; }
        public int? SolicitudProduccionDetalleID { get; set; }
        public DateTime InicioOriginal { get; set; }
        public DateTime FinOriginal { get; set; }
        public DateTime Inicio { get; set; }
        public DateTime Fin { get; set; }
        public decimal HorasProgramadas { get; set; }
        public TimeSpan? Cambio { get; set; }
        public TimeSpan? Arranque { get; set; }
        public DateTime ArranqueFecha { get; set; }
        public int SecuenciaMaquina { get; set; }
        public int EstatusID { get; set; }
        public bool EsMovible { get; set; }
    }

    private sealed class PosicionReacomodoGlobal
    {
        public DateTime Cambio { get; set; }
        public DateTime Arranque { get; set; }
        public DateTime Fin { get; set; }
        public decimal HorasCambio { get; set; }
        public bool MovidoPorMolde { get; set; }
    }
}