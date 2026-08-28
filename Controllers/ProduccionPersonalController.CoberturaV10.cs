using ERP.NSQuell.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;

namespace ERP.NSQuell.Controllers;

// NSQ_PRODUCCION_PERSONAL_V10_COBERTURA_PERIODOS_HEADER_PEOPLE
public sealed partial class ProduccionPersonalController
{
    private sealed class CoberturaDiaV10
    {
        public int CoberturaDiaID { get; set; }

        public DateTime FechaTrabajo { get; set; }

        public DateTime SemanaInicio { get; set; }

        public int TurnoID { get; set; }

        public int? TecnicoID { get; set; }

        public int? SmedID { get; set; }

        public int? AuxiliarID { get; set; }

        public string Fuente { get; set; } = "DIA_MANUAL";
    }

    private static async Task<bool>
        ExisteCoberturaDiaV10Async(
            SqlConnection cn,
            SqlTransaction? tx)
    {
        const string sql = @"
SELECT CONVERT(bit,
    CASE
        WHEN OBJECT_ID(
            N'dbo.Produccion_PersonalTurnoCoberturaDia',
            N'U') IS NULL
        THEN 0
        ELSE 1
    END);";

        await using var cmd =
            tx == null
                ? new SqlCommand(sql,cn)
                : new SqlCommand(sql,cn,tx);

        return Convert.ToBoolean(
            await cmd.ExecuteScalarAsync() ??
            false);
    }

    private async Task
        CargarCoberturaPanelV10Async(
            ProduccionPersonalV7IndexVm vm,
            SqlConnection cn)
    {
        vm.CoberturasSoporteV10.Clear();

        vm.CoberturaDiaV10Configurada =
            await ExisteCoberturaDiaV10Async(
                cn,
                null);

        var turnos =
            await CargarTurnosV2Async(
                cn,
                null);

        if (vm.Vista == "dia")
        {
            vm.CoberturasSoporteV10.Add(
                await ConstruirCoberturaDiaV10Async(
                    vm.FechaReferencia.Date,
                    turnos,
                    vm.CoberturaDiaV10Configurada,
                    cn));

            return;
        }

        var primeraSemana =
            InicioSemanaV2(
                vm.InicioPeriodo.Date);

        var ultimaSemana =
            InicioSemanaV2(
                vm.FinPeriodoVisible.Date);

        for (
            var semana = primeraSemana;
            semana <= ultimaSemana;
            semana = semana.AddDays(7))
        {
            vm.CoberturasSoporteV10.Add(
                await ConstruirCoberturaSemanaV10Async(
                    semana,
                    turnos,
                    vm.CoberturaDiaV10Configurada,
                    cn));
        }
    }

    private async Task<
        ProduccionPersonalCoberturaPeriodoV10Vm>
        ConstruirCoberturaSemanaV10Async(
            DateTime semana,
            List<TurnoV2> turnos,
            bool tieneCoberturaDia,
            SqlConnection cn)
    {
        semana =
            InicioSemanaV2(
                semana);

        var guardadas =
            await CargarCoberturasV2Async(
                semana,
                cn,
                null);

        var escala =
            await CargarEscalaSemanaV2Async(
                semana,
                cn,
                null);

        var periodo =
            new ProduccionPersonalCoberturaPeriodoV10Vm
            {
                Alcance = "SEMANA",
                FechaClave = semana,
                SemanaInicio = semana,
                FechaInicio = semana,
                FechaFin = semana.AddDays(6),

                EscalaID =
                    escala?.EscalaID,

                EscalaFolio =
                    escala?.Folio ??
                    string.Empty,

                EscalaEstado =
                    escala?.Estado ??
                    string.Empty,

                AjustesDiarios =
                    tieneCoberturaDia
                        ? await ContarAjustesDiariosSemanaV10Async(
                            semana,
                            cn)
                        : 0
            };

        foreach (var t in turnos)
        {
            var g =
                guardadas.FirstOrDefault(
                    x =>
                        x.TurnoID ==
                        t.TurnoID);

            int? tecnico =
                g?.TecnicoID;

            int? smed =
                g?.SmedID;

            int? auxiliar =
                g?.AuxiliarID;

            var fuente =
                g?.Fuente ??
                "SIN_CONFIGURAR";

            if (g == null &&
                escala != null)
            {
                tecnico =
                    await SugerirApoyoEscalaV2Async(
                        escala.EscalaID,
                        t.TurnoID,
                        "TECNICO",
                        cn,
                        null);

                smed =
                    await SugerirApoyoEscalaV2Async(
                        escala.EscalaID,
                        t.TurnoID,
                        "SMED",
                        cn,
                        null);

                auxiliar =
                    await SugerirApoyoEscalaV2Async(
                        escala.EscalaID,
                        t.TurnoID,
                        "AUXILIAR",
                        cn,
                        null);

                if (tecnico.HasValue ||
                    smed.HasValue ||
                    auxiliar.HasValue)
                {
                    fuente =
                        "ESCALA_RRHH";
                }
            }

            periodo.Turnos.Add(
                CrearTurnoApoyoV10(
                    t,
                    tecnico,
                    smed,
                    auxiliar,
                    fuente));
        }

        return periodo;
    }

    private async Task<
        ProduccionPersonalCoberturaPeriodoV10Vm>
        ConstruirCoberturaDiaV10Async(
            DateTime fecha,
            List<TurnoV2> turnos,
            bool tieneCoberturaDia,
            SqlConnection cn)
    {
        fecha =
            fecha.Date;

        var semana =
            InicioSemanaV2(
                fecha);

        var semanales =
            await CargarCoberturasV2Async(
                semana,
                cn,
                null);

        var diarias =
            tieneCoberturaDia
                ? await CargarCoberturasDiaV10Async(
                    fecha,
                    cn,
                    null)
                : new List<CoberturaDiaV10>();

        var escala =
            await CargarEscalaSemanaV2Async(
                semana,
                cn,
                null);

        var periodo =
            new ProduccionPersonalCoberturaPeriodoV10Vm
            {
                Alcance = "DIA",
                FechaClave = fecha,
                SemanaInicio = semana,
                FechaInicio = fecha,
                FechaFin = fecha,

                EscalaID =
                    escala?.EscalaID,

                EscalaFolio =
                    escala?.Folio ??
                    string.Empty,

                EscalaEstado =
                    escala?.Estado ??
                    string.Empty,

                TieneAjusteDia =
                    diarias.Count > 0,

                AjustesDiarios =
                    diarias.Count > 0
                        ? 1
                        : 0
            };

        foreach (var t in turnos)
        {
            var diaria =
                diarias.FirstOrDefault(
                    x =>
                        x.TurnoID ==
                        t.TurnoID);

            if (diaria != null)
            {
                periodo.Turnos.Add(
                    CrearTurnoApoyoV10(
                        t,
                        diaria.TecnicoID,
                        diaria.SmedID,
                        diaria.AuxiliarID,
                        "DIA_MANUAL"));

                continue;
            }

            var semanal =
                semanales.FirstOrDefault(
                    x =>
                        x.TurnoID ==
                        t.TurnoID);

            int? tecnico =
                semanal?.TecnicoID;

            int? smed =
                semanal?.SmedID;

            int? auxiliar =
                semanal?.AuxiliarID;

            var fuente =
                semanal != null
                    ? "SEMANA_HEREDADA"
                    : "SIN_CONFIGURAR";

            if (semanal == null &&
                escala != null)
            {
                tecnico =
                    await SugerirApoyoEscalaV2Async(
                        escala.EscalaID,
                        t.TurnoID,
                        "TECNICO",
                        cn,
                        null);

                smed =
                    await SugerirApoyoEscalaV2Async(
                        escala.EscalaID,
                        t.TurnoID,
                        "SMED",
                        cn,
                        null);

                auxiliar =
                    await SugerirApoyoEscalaV2Async(
                        escala.EscalaID,
                        t.TurnoID,
                        "AUXILIAR",
                        cn,
                        null);

                if (tecnico.HasValue ||
                    smed.HasValue ||
                    auxiliar.HasValue)
                {
                    fuente =
                        "ESCALA_RRHH";
                }
            }

            periodo.Turnos.Add(
                CrearTurnoApoyoV10(
                    t,
                    tecnico,
                    smed,
                    auxiliar,
                    fuente));
        }

        return periodo;
    }

    private static
        ProduccionPersonalTurnoApoyoVm
        CrearTurnoApoyoV10(
            TurnoV2 t,
            int? tecnico,
            int? smed,
            int? auxiliar,
            string fuente)
    {
        return
            new ProduccionPersonalTurnoApoyoVm
            {
                TurnoID =
                    t.TurnoID,

                Nombre =
                    t.Nombre,

                TipoTurno =
                    t.TipoTurno,

                Color =
                    t.Color,

                HoraInicio =
                    t.Inicio,

                HoraFin =
                    t.Fin,

                CruzaDiaSiguiente =
                    t.Cruza,

                Orden =
                    t.Orden,

                TecnicoProduccionID =
                    tecnico,

                SmedID =
                    smed,

                AuxiliarID =
                    auxiliar,

                Fuente =
                    fuente
            };
    }

    private static async Task<
        List<CoberturaDiaV10>>
        CargarCoberturasDiaV10Async(
            DateTime fecha,
            SqlConnection cn,
            SqlTransaction? tx)
    {
        var lista =
            new List<CoberturaDiaV10>();

        const string sql = @"
SELECT
    CoberturaDiaID,
    FechaTrabajo,
    SemanaInicio,
    TurnoID,
    TecnicoProduccionID,
    SmedID,
    AuxiliarID,
    ISNULL(Fuente,N'DIA_MANUAL') Fuente
FROM dbo.Produccion_PersonalTurnoCoberturaDia
WHERE FechaTrabajo=@Fecha
  AND Activo=1
ORDER BY TurnoID,CoberturaDiaID;";

        await using var cmd =
            tx == null
                ? new SqlCommand(
                    sql,
                    cn)
                : new SqlCommand(
                    sql,
                    cn,
                    tx);

        cmd.Parameters.Add(
            "@Fecha",
            SqlDbType.Date).Value =
            fecha.Date;

        await using var rd =
            await cmd.ExecuteReaderAsync();

        while (await rd.ReadAsync())
        {
            lista.Add(
                new CoberturaDiaV10
                {
                    CoberturaDiaID =
                        Convert.ToInt32(
                            rd["CoberturaDiaID"]),

                    FechaTrabajo =
                        Convert.ToDateTime(
                            rd["FechaTrabajo"]),

                    SemanaInicio =
                        Convert.ToDateTime(
                            rd["SemanaInicio"]),

                    TurnoID =
                        Convert.ToInt32(
                            rd["TurnoID"]),

                    TecnicoID =
                        rd["TecnicoProduccionID"] ==
                        DBNull.Value
                            ? null
                            : Convert.ToInt32(
                                rd["TecnicoProduccionID"]),

                    SmedID =
                        rd["SmedID"] ==
                        DBNull.Value
                            ? null
                            : Convert.ToInt32(
                                rd["SmedID"]),

                    AuxiliarID =
                        rd["AuxiliarID"] ==
                        DBNull.Value
                            ? null
                            : Convert.ToInt32(
                                rd["AuxiliarID"]),

                    Fuente =
                        rd["Fuente"]?.ToString()
                            ?.Trim() ??
                        "DIA_MANUAL"
                });
        }

        return lista;
    }

    private static async Task<int>
        ContarAjustesDiariosSemanaV10Async(
            DateTime semana,
            SqlConnection cn)
    {
        const string sql = @"
SELECT COUNT(DISTINCT FechaTrabajo)
FROM dbo.Produccion_PersonalTurnoCoberturaDia
WHERE SemanaInicio=@Semana
  AND Activo=1;";

        await using var cmd =
            new SqlCommand(
                sql,
                cn);

        cmd.Parameters.Add(
            "@Semana",
            SqlDbType.Date).Value =
            InicioSemanaV2(
                semana);

        return Convert.ToInt32(
            await cmd.ExecuteScalarAsync() ??
            0);
    }

    private static async Task
        UpsertCoberturaDiaV10Async(
            DateTime fecha,
            ProduccionPersonalTurnoGuardarVm cobertura,
            int usuarioId,
            SqlConnection cn,
            SqlTransaction tx)
    {
        var semana =
            InicioSemanaV2(
                fecha);

        const string sql = @"
DECLARE @ID int=
(
    SELECT TOP(1)
        CoberturaDiaID
    FROM dbo.Produccion_PersonalTurnoCoberturaDia
        WITH(UPDLOCK,HOLDLOCK)
    WHERE FechaTrabajo=@Fecha
      AND TurnoID=@TurnoID
    ORDER BY
        Activo DESC,
        CoberturaDiaID DESC
);

IF @ID IS NULL
BEGIN
    INSERT dbo.Produccion_PersonalTurnoCoberturaDia
    (
        FechaTrabajo,
        SemanaInicio,
        TurnoID,
        TecnicoProduccionID,
        SmedID,
        AuxiliarID,
        Fuente,
        UsuarioCreacionID,
        FechaCreacion,
        Activo
    )
    VALUES
    (
        @Fecha,
        @Semana,
        @TurnoID,
        @Tecnico,
        @Smed,
        @Auxiliar,
        N'DIA_MANUAL',
        @Usuario,
        SYSDATETIME(),
        1
    );
END
ELSE
BEGIN
    UPDATE dbo.Produccion_PersonalTurnoCoberturaDia
    SET SemanaInicio=@Semana,
        TecnicoProduccionID=@Tecnico,
        SmedID=@Smed,
        AuxiliarID=@Auxiliar,
        Fuente=N'DIA_MANUAL',
        UsuarioModificacionID=@Usuario,
        FechaModificacion=SYSDATETIME(),
        Activo=1
    WHERE CoberturaDiaID=@ID;
END;";

        await using var cmd =
            new SqlCommand(
                sql,
                cn,
                tx);

        cmd.Parameters.Add(
            "@Fecha",
            SqlDbType.Date).Value =
            fecha.Date;

        cmd.Parameters.Add(
            "@Semana",
            SqlDbType.Date).Value =
            semana.Date;

        cmd.Parameters.Add(
            "@TurnoID",
            SqlDbType.Int).Value =
            cobertura.TurnoID;

        cmd.Parameters.Add(
            "@Tecnico",
            SqlDbType.Int).Value =
            (object?)cobertura
                .TecnicoProduccionID ??
            DBNull.Value;

        cmd.Parameters.Add(
            "@Smed",
            SqlDbType.Int).Value =
            (object?)cobertura
                .SmedID ??
            DBNull.Value;

        cmd.Parameters.Add(
            "@Auxiliar",
            SqlDbType.Int).Value =
            (object?)cobertura
                .AuxiliarID ??
            DBNull.Value;

        cmd.Parameters.Add(
            "@Usuario",
            SqlDbType.Int).Value =
            usuarioId;

        await cmd.ExecuteNonQueryAsync();
    }

    [HttpPost("GuardarCoberturaDiaV10")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult>
        GuardarCoberturaDiaV10(
            ProduccionPersonalCoberturaDiaV10PostVm vm)
    {
        if (!UsuarioEnSesion())
        {
            return RedirectToAction(
                "Login",
                "Login");
        }

        var fecha =
            vm.FechaTrabajo.Date;

        await using var cn =
            new SqlConnection(
                ConnectionString);

        await cn.OpenAsync();

        if (!await ExisteCoberturaDiaV10Async(
                cn,
                null))
        {
            TempData["Error"] =
                "Falta ejecutar SQL 49 para habilitar cobertura diaria.";

            return RedirectToAction(
                nameof(Index),
                new
                {
                    vista="dia",
                    fechaDesde=
                        fecha.ToString(
                            "yyyy-MM-dd"),
                    panel="support"
                });
        }

        await using var tx =
            (SqlTransaction)
            await cn.BeginTransactionAsync(
                IsolationLevel.Serializable);

        try
        {
            var turnos =
                await CargarTurnosV2Async(
                    cn,
                    tx);

            foreach (
                var c in
                vm.Coberturas ??
                new())
            {
                var turno =
                    turnos.FirstOrDefault(
                        x =>
                            x.TurnoID ==
                            c.TurnoID);

                if (turno == null)
                    continue;

                if (!c.TecnicoProduccionID.HasValue &&
                    !c.SmedID.HasValue)
                {
                    throw new InvalidOperationException(
                        $"Turno {turno.Nombre}: debe existir al menos un Técnico o un SMED.");
                }

                await ValidarPersonaApoyoV2Async(
                    c.TecnicoProduccionID,
                    "TECNICO",
                    cn,
                    tx);

                await ValidarPersonaApoyoV2Async(
                    c.SmedID,
                    "SMED_O_TECNICO",
                    cn,
                    tx);

                await ValidarPersonaApoyoV2Async(
                    c.AuxiliarID,
                    "AUXILIAR",
                    cn,
                    tx);

                await UpsertCoberturaDiaV10Async(
                    fecha,
                    c,
                    UsuarioID(),
                    cn,
                    tx);
            }

            await tx.CommitAsync();

            TempData["Success"] =
                $"Cobertura diaria guardada para {fecha:dd/MM/yyyy}. La cobertura semanal no fue modificada.";
        }
        catch (Exception ex)
        {
            try
            {
                await tx.RollbackAsync();
            }
            catch
            {
            }

            TempData["Error"] =
                "No fue posible guardar la cobertura diaria: " +
                ex.Message;
        }

        return RedirectToAction(
            nameof(Index),
            new
            {
                vista="dia",
                fechaDesde=
                    fecha.ToString(
                        "yyyy-MM-dd"),
                panel="support"
            });
    }

    [HttpPost("RestablecerCoberturaDiaV10")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult>
        RestablecerCoberturaDiaV10(
            DateTime fechaTrabajo)
    {
        if (!UsuarioEnSesion())
        {
            return RedirectToAction(
                "Login",
                "Login");
        }

        var fecha =
            fechaTrabajo.Date;

        await using var cn =
            new SqlConnection(
                ConnectionString);

        await cn.OpenAsync();

        if (!await ExisteCoberturaDiaV10Async(
                cn,
                null))
        {
            TempData["Error"] =
                "No existe la tabla de cobertura diaria.";

            return RedirectToAction(
                nameof(Index),
                new
                {
                    vista="dia",
                    fechaDesde=
                        fecha.ToString(
                            "yyyy-MM-dd"),
                    panel="support"
                });
        }

        await using var tx =
            (SqlTransaction)
            await cn.BeginTransactionAsync(
                IsolationLevel.Serializable);

        try
        {
            const string sql = @"
UPDATE dbo.Produccion_PersonalTurnoCoberturaDia
SET Activo=0,
    UsuarioModificacionID=@Usuario,
    FechaModificacion=SYSDATETIME()
WHERE FechaTrabajo=@Fecha
  AND Activo=1;";

            await using var cmd =
                new SqlCommand(
                    sql,
                    cn,
                    tx);

            cmd.Parameters.Add(
                "@Usuario",
                SqlDbType.Int).Value =
                UsuarioID();

            cmd.Parameters.Add(
                "@Fecha",
                SqlDbType.Date).Value =
                fecha;

            var afectados =
                await cmd.ExecuteNonQueryAsync();

            await tx.CommitAsync();

            TempData["Success"] =
                afectados > 0
                    ? $"Se retiró la excepción del {fecha:dd/MM/yyyy}. El día vuelve a heredar la cobertura semanal."
                    : $"El {fecha:dd/MM/yyyy} ya estaba utilizando la cobertura semanal.";
        }
        catch (Exception ex)
        {
            try
            {
                await tx.RollbackAsync();
            }
            catch
            {
            }

            TempData["Error"] =
                "No fue posible restablecer la cobertura semanal: " +
                ex.Message;
        }

        return RedirectToAction(
            nameof(Index),
            new
            {
                vista="dia",
                fechaDesde=
                    fecha.ToString(
                        "yyyy-MM-dd"),
                panel="support"
            });
    }
}