using ERP.NSQuell.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;

namespace ERP.NSQuell.Controllers
{
    public sealed partial class ProduccionController
    {

        private const int PreparacionMinutosAvisoCambioMolde = 30;
        private const int PreparacionMinutosAnticipacionEmbalaje = 120;
        private const int PreparacionDiasHorizonte = 7;

       
        [HttpGet]
        public async Task<IActionResult> Preparacion(
            string? filtro = null,
            int? maquinaId = null)
        {
            if (!UsuarioEnSesion())
            {
                return RedirectToAction(
                    "Login",
                    "Login");
            }

            filtro =
                string.IsNullOrWhiteSpace(filtro)
                    ? null
                    : filtro.Trim();

            if (maquinaId.HasValue &&
                maquinaId.Value <= 0)
            {
                maquinaId = null;
            }

            var usuarioId =
                ObtenerUsuarioID();

            await using var cn =
                new SqlConnection(
                    ConnectionString);

            await cn.OpenAsync();

            
            await using (
                var tx =
                    (SqlTransaction)
                    await cn.BeginTransactionAsync(
                        IsolationLevel.Serializable))
            {
                try
                {
                    await SincronizarPreparacionAnticipadaAsync(
                        usuarioId,
                        cn,
                        tx);

                    await tx.CommitAsync();
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
                        "No fue posible sincronizar la preparación " +
                        "anticipada de Producción: " +
                        ex.Message;
                }
            }

            var ahora =
                DateTime.Now;

            var vm =
                new ProduccionPreparacionIndexVm
                {
                    FechaConsulta = ahora,
                    Filtro = filtro,
                    MaquinaID = maquinaId
                };

            vm.Maquinas =
                await CargarMaquinasPreparacionAsync(
                    cn);

            vm.Tareas =
                await CargarPreparacionAnticipadaAsync(
                    filtro,
                    maquinaId,
                    ahora,
                    cn);

            return View(
                "Preparacion/Index",
                vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmarPreparacion(
            ProduccionPreparacionConfirmarVm vm)
        {
            if (!UsuarioEnSesion())
            {
                return RedirectToAction(
                    "Login",
                    "Login");
            }

            if (vm.PreparacionAnticipadaID <= 0)
            {
                TempData["Error"] =
                    "No se recibió correctamente la tarea de preparación.";

                return RedirectToAction(
                    nameof(Preparacion));
            }

            var observaciones =
                string.IsNullOrWhiteSpace(
                    vm.Observaciones)
                    ? null
                    : vm.Observaciones.Trim();

            if (observaciones?.Length > 500)
            {
                TempData["Error"] =
                    "Las observaciones no pueden superar 500 caracteres.";

                return RedirectToAction(
                    nameof(Preparacion));
            }

            var usuarioId =
                ObtenerUsuarioID();

            await using var cn =
                new SqlConnection(
                    ConnectionString);

            await cn.OpenAsync();

            await using var tx =
                (SqlTransaction)
                await cn.BeginTransactionAsync(
                    IsolationLevel.Serializable);

            try
            {
                const string sql = @"
UPDATE dbo.Produccion_PreparacionAnticipada
SET
    Estado = N'CONFIRMADA',
    UsuarioConfirmacionID = @UsuarioID,
    FechaConfirmacion = GETDATE(),

    Observaciones =
        CASE
            WHEN @Observaciones IS NULL
                THEN Observaciones

            WHEN Observaciones IS NULL
                 OR LTRIM(RTRIM(Observaciones)) = N''
                THEN @Observaciones

            ELSE
                Observaciones
                + CHAR(13)
                + CHAR(10)
                + @Observaciones
        END,

    UsuarioModificacionID = @UsuarioID,
    FechaModificacion = GETDATE()

WHERE PreparacionAnticipadaID =
      @PreparacionAnticipadaID

  AND Activo = 1

  AND Estado = N'PENDIENTE';";

                await using var cmd =
                    new SqlCommand(
                        sql,
                        cn,
                        tx);

                cmd.Parameters.Add(
                    "@PreparacionAnticipadaID",
                    SqlDbType.Int).Value =
                    vm.PreparacionAnticipadaID;

                cmd.Parameters.Add(
                    "@UsuarioID",
                    SqlDbType.Int).Value =
                    usuarioId;

                cmd.Parameters.Add(
                    "@Observaciones",
                    SqlDbType.NVarChar,
                    500).Value =
                    string.IsNullOrWhiteSpace(
                        observaciones)
                        ? DBNull.Value
                        : observaciones;

                var filas =
                    await cmd.ExecuteNonQueryAsync();

                if (filas <= 0)
                {
                    await tx.RollbackAsync();

                    TempData["Error"] =
                        "La tarea ya fue atendida, cancelada " +
                        "o ya no se encuentra disponible.";

                    return RedirectToAction(
                        nameof(Preparacion));
                }

                await tx.CommitAsync();

                TempData["Success"] =
                    "Preparación confirmada correctamente.";

                return RedirectToAction(
                    nameof(Preparacion));
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
                    "No fue posible confirmar la preparación: " +
                    ex.Message;

                return RedirectToAction(
                    nameof(Preparacion));
            }
        }

     
        private async Task SincronizarPreparacionAnticipadaAsync(
            int usuarioId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            var ahora =
                DateTime.Now;

            /*
             * Incluimos un día hacia atrás para conservar visibles
             * preparaciones vencidas recientes.
             *
             * Hacia adelante manejamos inicialmente 7 días.
             */
            var desde =
                ahora.Date.AddDays(-1);

            var hasta =
                ahora.Date
                    .AddDays(
                        PreparacionDiasHorizonte + 1);

            var programas =
                await CargarProgramasParaPreparacionAsync(
                    desde,
                    hasta,
                    cn,
                    tx);

            foreach (var programa in programas)
            {
                var fechaArranque =
                    programa.FechaArranque;

                var requiereSecado =
                    fechaArranque.HasValue &&
                    programa.HorasSecado.HasValue &&
                    programa.HorasSecado.Value > 0 &&
                    (
                        !string.IsNullOrWhiteSpace(
                            programa.MaterialCodigo)
                        ||
                        !string.IsNullOrWhiteSpace(
                            programa.MaterialDescripcion)
                    );

                if (requiereSecado)
                {
                    var horas =
                        Convert.ToDouble(
                            programa.HorasSecado!.Value);

                    var fechaObjetivo =
                        fechaArranque!.Value;

                    var fechaAviso =
                        fechaObjetivo.AddHours(
                            -horas);

                    await SincronizarTareaPreparacionAsync(
                        programa.ProgramaProduccionID,
                        ProduccionPreparacionTipo.SecadoMaterial,
                        fechaObjetivo,
                        fechaAviso,
                        usuarioId,
                        cn,
                        tx);
                }
                else
                {
                    await CancelarTareaPreparacionNoAplicableAsync(
                        programa.ProgramaProduccionID,
                        ProduccionPreparacionTipo.SecadoMaterial,
                        usuarioId,
                        cn,
                        tx);
                }

                
                var requiereEmbalaje =
                    fechaArranque.HasValue &&
                    (
                        !string.IsNullOrWhiteSpace(
                            programa.EmbalajeCodigo)
                        ||
                        !string.IsNullOrWhiteSpace(
                            programa.EmbalajeDescripcion)
                        ||
                        (
                            programa.CantidadEmbalajes.HasValue &&
                            programa.CantidadEmbalajes.Value > 0
                        )
                    );

                if (requiereEmbalaje)
                {
                    var fechaObjetivo =
                        fechaArranque!.Value;

                    var fechaAviso =
                        fechaObjetivo.AddMinutes(
                            -PreparacionMinutosAnticipacionEmbalaje);

                    await SincronizarTareaPreparacionAsync(
                        programa.ProgramaProduccionID,
                        ProduccionPreparacionTipo.PrepararEmbalaje,
                        fechaObjetivo,
                        fechaAviso,
                        usuarioId,
                        cn,
                        tx);
                }
                else
                {
                    await CancelarTareaPreparacionNoAplicableAsync(
                        programa.ProgramaProduccionID,
                        ProduccionPreparacionTipo.PrepararEmbalaje,
                        usuarioId,
                        cn,
                        tx);
                }

                if (programa.RequiereCambioMolde &&
                    programa.FechaCambioMolde.HasValue)
                {
                    var fechaObjetivo =
                        programa.FechaCambioMolde.Value;

                    var fechaAviso =
                        fechaObjetivo.AddMinutes(
                            -PreparacionMinutosAvisoCambioMolde);

                    await SincronizarTareaPreparacionAsync(
                        programa.ProgramaProduccionID,
                        ProduccionPreparacionTipo.CambioMolde,
                        fechaObjetivo,
                        fechaAviso,
                        usuarioId,
                        cn,
                        tx);
                }
                else
                {
                    await CancelarTareaPreparacionNoAplicableAsync(
                        programa.ProgramaProduccionID,
                        ProduccionPreparacionTipo.CambioMolde,
                        usuarioId,
                        cn,
                        tx);
                }
            }
        }


        private async Task<List<ProgramaPreparacionInterno>>
            CargarProgramasParaPreparacionAsync(
                DateTime desde,
                DateTime hasta,
                SqlConnection cn,
                SqlTransaction tx)
        {
            var lista =
                new List<ProgramaPreparacionInterno>();

            const string sql = @"
SELECT
    pp.ProgramaProduccionID,

    pp.SolicitudProduccionID,
    pp.SolicitudProduccionDetalleID,

    s.NumeroOFRecibida,

    pp.MaquinaID,

    COALESCE
    (
        NULLIF(
            LTRIM(
                RTRIM(
                    pp.MaquinaCodigo
                )
            ),
            N''
        ),
        maq.Codigo
    ) AS MaquinaCodigo,

    COALESCE
    (
        NULLIF(
            LTRIM(
                RTRIM(
                    pp.MaquinaNombre
                )
            ),
            N''
        ),
        maq.Nombre
    ) AS MaquinaNombre,

    pp.ParteID,

    pp.NumeroParte,

    pp.ReferenciaSAP,

    pp.DesignacionDescripcionSAP
        AS DescripcionParte,

    pp.MoldeID,

    pp.MoldeCodigo,

    CONVERT
    (
        INT,
        ISNULL(
            pp.CantidadProgramada,
            0
        )
    ) AS CantidadProgramada,

    pp.FechaInicioProgramada,
    pp.FechaFinProgramada,
    pp.Cambio,
    pp.Arranque,

    d.TipoSecado,
    d.HorasSecado,

    d.MaterialCodigo,
    d.MaterialDescripcion,

    d.EmbalajeCodigo,
    d.EmbalajeDescripcion,
    d.PiezasPorEmbalaje,
    d.CantidadEmbalajes,

    anterior.ProgramaProduccionID
        AS ProgramaAnteriorID,

    anterior.MoldeID
        AS MoldeAnteriorID,

    anterior.MoldeCodigo
        AS MoldeAnteriorCodigo

FROM dbo.Planeacion_ProgramaProduccion pp

LEFT JOIN dbo.SolicitudesProduccion s
    ON s.SolicitudProduccionID =
       pp.SolicitudProduccionID

LEFT JOIN dbo.SolicitudesProduccionDetalle d
    ON d.SolicitudProduccionDetalleID =
       pp.SolicitudProduccionDetalleID
   AND d.Activo = 1

LEFT JOIN dbo.ERP_Maquinas maq
    ON maq.MaquinaID =
       pp.MaquinaID

OUTER APPLY
(
    SELECT TOP (1)
        ant.ProgramaProduccionID,
        ant.MoldeID,
        ant.MoldeCodigo

    FROM dbo.Planeacion_ProgramaProduccion ant

    WHERE ant.Activo = 1

      AND ant.ProgramaProduccionID <>
          pp.ProgramaProduccionID

      AND ant.MaquinaID =
          pp.MaquinaID

      AND ant.FechaInicioProgramada <
          pp.FechaInicioProgramada

      AND ISNULL(
              ant.EstatusID,
              1
          ) NOT IN
          (
              5,
              6,
              9,
              99
          )

    ORDER BY
        ant.FechaInicioProgramada DESC,
        ant.ProgramaProduccionID DESC
) anterior

WHERE pp.Activo = 1

  AND pp.MaquinaID IS NOT NULL

  AND pp.FechaInicioProgramada
      IS NOT NULL

  AND pp.FechaInicioProgramada >=
      @Desde

  AND pp.FechaInicioProgramada <
      @Hasta

  AND ISNULL(
          pp.EstatusID,
          1
      ) NOT IN
      (
          5,
          6,
          9,
          99
      )

ORDER BY
    pp.FechaInicioProgramada,
    ISNULL(
        pp.SecuenciaMaquina,
        999999
    ),
    pp.ProgramaProduccionID;";

            await using var cmd =
                new SqlCommand(
                    sql,
                    cn,
                    tx);

            cmd.Parameters.Add(
                "@Desde",
                SqlDbType.DateTime).Value =
                desde;

            cmd.Parameters.Add(
                "@Hasta",
                SqlDbType.DateTime).Value =
                hasta;

            await using var rd =
                await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                var inicio =
                    Convert.ToDateTime(
                        rd["FechaInicioProgramada"]);

                var cambio =
                    rd["Cambio"] == DBNull.Value
                        ? (TimeSpan?)null
                        : (TimeSpan)rd["Cambio"];

                var arranque =
                    rd["Arranque"] == DBNull.Value
                        ? (TimeSpan?)null
                        : (TimeSpan)rd["Arranque"];

                var fechaCambio =
                    ConstruirFechaPreparacion(
                        inicio,
                        cambio);

                var fechaArranque =
                    ConstruirFechaPreparacion(
                        inicio,
                        arranque);

                var moldeActualId =
                    PreparacionNullableInt(
                        rd,
                        "MoldeID");

                var moldeAnteriorId =
                    PreparacionNullableInt(
                        rd,
                        "MoldeAnteriorID");

                var moldeActualCodigo =
                    PreparacionTexto(
                        rd,
                        "MoldeCodigo");

                var moldeAnteriorCodigo =
                    PreparacionTexto(
                        rd,
                        "MoldeAnteriorCodigo");

                var requiereCambioMolde =
                    DeterminarCambioMoldePreparacion(
                        moldeAnteriorId,
                        moldeAnteriorCodigo,
                        moldeActualId,
                        moldeActualCodigo);

                lista.Add(
                    new ProgramaPreparacionInterno
                    {
                        ProgramaProduccionID =
                            Convert.ToInt32(
                                rd[
                                    "ProgramaProduccionID"]),

                        SolicitudProduccionID =
                            PreparacionNullableInt(
                                rd,
                                "SolicitudProduccionID"),

                        SolicitudProduccionDetalleID =
                            PreparacionNullableInt(
                                rd,
                                "SolicitudProduccionDetalleID"),

                        NumeroOF =
                            PreparacionTexto(
                                rd,
                                "NumeroOFRecibida"),

                        MaquinaID =
                            PreparacionNullableInt(
                                rd,
                                "MaquinaID"),

                        MaquinaCodigo =
                            PreparacionTexto(
                                rd,
                                "MaquinaCodigo"),

                        MaquinaNombre =
                            PreparacionTexto(
                                rd,
                                "MaquinaNombre"),

                        ParteID =
                            PreparacionNullableInt(
                                rd,
                                "ParteID"),

                        NumeroParte =
                            PreparacionTexto(
                                rd,
                                "NumeroParte"),

                        ReferenciaSAP =
                            PreparacionTexto(
                                rd,
                                "ReferenciaSAP"),

                        DescripcionParte =
                            PreparacionTexto(
                                rd,
                                "DescripcionParte"),

                        MoldeID =
                            moldeActualId,

                        MoldeCodigo =
                            moldeActualCodigo,

                        MoldeAnteriorID =
                            moldeAnteriorId,

                        MoldeAnteriorCodigo =
                            moldeAnteriorCodigo,

                        CantidadProgramada =
                            rd["CantidadProgramada"]
                                == DBNull.Value
                                    ? 0
                                    : Convert.ToInt32(
                                        rd[
                                            "CantidadProgramada"]),

                        FechaInicioProgramada =
                            inicio,

                        FechaFinProgramada =
                            rd["FechaFinProgramada"]
                                == DBNull.Value
                                    ? null
                                    : Convert.ToDateTime(
                                        rd[
                                            "FechaFinProgramada"]),

                        Cambio =
                            cambio,

                        Arranque =
                            arranque,

                        FechaCambioMolde =
                            fechaCambio,

                        FechaArranque =
                            fechaArranque,

                        TipoSecado =
                            PreparacionTexto(
                                rd,
                                "TipoSecado"),

                        HorasSecado =
                            PreparacionNullableDecimal(
                                rd,
                                "HorasSecado"),

                        MaterialCodigo =
                            PreparacionTexto(
                                rd,
                                "MaterialCodigo"),

                        MaterialDescripcion =
                            PreparacionTexto(
                                rd,
                                "MaterialDescripcion"),

                        EmbalajeCodigo =
                            PreparacionTexto(
                                rd,
                                "EmbalajeCodigo"),

                        EmbalajeDescripcion =
                            PreparacionTexto(
                                rd,
                                "EmbalajeDescripcion"),

                        PiezasPorEmbalaje =
                            PreparacionNullableDecimal(
                                rd,
                                "PiezasPorEmbalaje"),

                        CantidadEmbalajes =
                            PreparacionNullableDecimal(
                                rd,
                                "CantidadEmbalajes"),

                        RequiereCambioMolde =
                            requiereCambioMolde
                    });
            }

            return lista;
        }

        private static async Task SincronizarTareaPreparacionAsync(
            int programaProduccionId,
            string tipoTarea,
            DateTime fechaObjetivo,
            DateTime fechaAviso,
            int usuarioId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            fechaObjetivo =
                NormalizarFechaPreparacion(
                    fechaObjetivo);

            fechaAviso =
                NormalizarFechaPreparacion(
                    fechaAviso);

         
            const string sql = @"
DECLARE @PreparacionAnticipadaID INT;
DECLARE @EstadoActual NVARCHAR(30);

SELECT TOP (1)
    @PreparacionAnticipadaID =
        PreparacionAnticipadaID,

    @EstadoActual =
        Estado

FROM dbo.Produccion_PreparacionAnticipada
    WITH (UPDLOCK, HOLDLOCK)

WHERE ProgramaProduccionID =
      @ProgramaProduccionID

  AND TipoTarea =
      @TipoTarea

ORDER BY
    PreparacionAnticipadaID DESC;

IF @PreparacionAnticipadaID IS NULL
BEGIN

    INSERT INTO dbo.Produccion_PreparacionAnticipada
    (
        ProgramaProduccionID,
        TipoTarea,
        FechaObjetivo,
        FechaAviso,
        Estado,
        UsuarioConfirmacionID,
        FechaConfirmacion,
        Observaciones,
        Activo,
        UsuarioCreacionID,
        FechaCreacion
    )
    VALUES
    (
        @ProgramaProduccionID,
        @TipoTarea,
        @FechaObjetivo,
        @FechaAviso,
        N'PENDIENTE',
        NULL,
        NULL,
        NULL,
        1,
        @UsuarioID,
        GETDATE()
    );

END
ELSE IF @EstadoActual <> N'CONFIRMADA'
BEGIN

    UPDATE dbo.Produccion_PreparacionAnticipada
    SET
        FechaObjetivo =
            @FechaObjetivo,

        FechaAviso =
            @FechaAviso,

        Estado =
            N'PENDIENTE',

        UsuarioConfirmacionID =
            NULL,

        FechaConfirmacion =
            NULL,

        Activo =
            1,

        UsuarioModificacionID =
            @UsuarioID,

        FechaModificacion =
            GETDATE()

    WHERE PreparacionAnticipadaID =
          @PreparacionAnticipadaID;

END;";

            await using var cmd =
                new SqlCommand(
                    sql,
                    cn,
                    tx);

            cmd.Parameters.Add(
                "@ProgramaProduccionID",
                SqlDbType.Int).Value =
                programaProduccionId;

            cmd.Parameters.Add(
                "@TipoTarea",
                SqlDbType.NVarChar,
                40).Value =
                tipoTarea;

            cmd.Parameters.Add(
                "@FechaObjetivo",
                SqlDbType.DateTime).Value =
                fechaObjetivo;

            cmd.Parameters.Add(
                "@FechaAviso",
                SqlDbType.DateTime).Value =
                fechaAviso;

            cmd.Parameters.Add(
                "@UsuarioID",
                SqlDbType.Int).Value =
                usuarioId;

            await cmd.ExecuteNonQueryAsync();
        }

        // ============================================================
        // CANCELAR SI YA NO APLICA
        // ============================================================

        private static async Task
            CancelarTareaPreparacionNoAplicableAsync(
                int programaProduccionId,
                string tipoTarea,
                int usuarioId,
                SqlConnection cn,
                SqlTransaction tx)
        {
           
            const string sql = @"
UPDATE dbo.Produccion_PreparacionAnticipada
SET
    Estado =
        N'CANCELADA',

    Activo =
        0,

    UsuarioModificacionID =
        @UsuarioID,

    FechaModificacion =
        GETDATE()

WHERE ProgramaProduccionID =
      @ProgramaProduccionID

  AND TipoTarea =
      @TipoTarea

  AND Estado =
      N'PENDIENTE'

  AND Activo = 1;";

            await using var cmd =
                new SqlCommand(
                    sql,
                    cn,
                    tx);

            cmd.Parameters.Add(
                "@ProgramaProduccionID",
                SqlDbType.Int).Value =
                programaProduccionId;

            cmd.Parameters.Add(
                "@TipoTarea",
                SqlDbType.NVarChar,
                40).Value =
                tipoTarea;

            cmd.Parameters.Add(
                "@UsuarioID",
                SqlDbType.Int).Value =
                usuarioId;

            await cmd.ExecuteNonQueryAsync();
        }

        // ============================================================
        // CARGAR COLA PARA LA VISTA
        // ============================================================

        private async Task<List<ProduccionPreparacionTareaVm>>
            CargarPreparacionAnticipadaAsync(
                string? filtro,
                int? maquinaId,
                DateTime ahora,
                SqlConnection cn)
        {
            var lista =
                new List<ProduccionPreparacionTareaVm>();

            const string sql = @"
SELECT
    pa.PreparacionAnticipadaID,
    pa.ProgramaProduccionID,
    pa.TipoTarea,
    pa.FechaObjetivo,
    pa.FechaAviso,
    pa.Estado,

    pa.UsuarioConfirmacionID,
    pa.FechaConfirmacion,
    pa.Observaciones,

    confirmador.NombreCompleto
        AS UsuarioConfirmacionNombre,

    ejecucion.EjecucionProduccionID,

    pp.SolicitudProduccionID,
    pp.SolicitudProduccionDetalleID,

    s.NumeroOFRecibida,

    pp.MaquinaID,

    COALESCE
    (
        NULLIF(
            LTRIM(
                RTRIM(
                    pp.MaquinaCodigo
                )
            ),
            N''
        ),
        maq.Codigo
    ) AS MaquinaCodigo,

    COALESCE
    (
        NULLIF(
            LTRIM(
                RTRIM(
                    pp.MaquinaNombre
                )
            ),
            N''
        ),
        maq.Nombre
    ) AS MaquinaNombre,

    pp.ParteID,
    pp.NumeroParte,
    pp.ReferenciaSAP,

    pp.DesignacionDescripcionSAP
        AS DescripcionParte,

    pp.MoldeID,
    pp.MoldeCodigo,

    anterior.MoldeID
        AS MoldeAnteriorID,

    anterior.MoldeCodigo
        AS MoldeAnteriorCodigo,

    CONVERT
    (
        INT,
        ISNULL(
            pp.CantidadProgramada,
            0
        )
    ) AS CantidadProgramada,

    pp.FechaInicioProgramada,
    pp.FechaFinProgramada,
    pp.Cambio,
    pp.Arranque,

    d.TipoSecado,
    d.HorasSecado,

    d.MaterialCodigo,
    d.MaterialDescripcion,

    d.EmbalajeCodigo,
    d.EmbalajeDescripcion,

    d.PiezasPorEmbalaje,
    d.CantidadEmbalajes,

    opPrincipal.PersonaID
        AS OperadorPrincipalID,

    opPrincipal.NombreCompleto
        AS OperadorPrincipalNombre,

    opAuxiliar.PersonaID
        AS OperadorAuxiliarID,

    opAuxiliar.NombreCompleto
        AS OperadorAuxiliarNombre

FROM dbo.Produccion_PreparacionAnticipada pa

INNER JOIN dbo.Planeacion_ProgramaProduccion pp
    ON pp.ProgramaProduccionID =
       pa.ProgramaProduccionID

LEFT JOIN dbo.SolicitudesProduccion s
    ON s.SolicitudProduccionID =
       pp.SolicitudProduccionID

LEFT JOIN dbo.SolicitudesProduccionDetalle d
    ON d.SolicitudProduccionDetalleID =
       pp.SolicitudProduccionDetalleID
   AND d.Activo = 1

LEFT JOIN dbo.ERP_Maquinas maq
    ON maq.MaquinaID =
       pp.MaquinaID

OUTER APPLY
(
    SELECT TOP (1)
        e.EjecucionProduccionID

    FROM dbo.Produccion_Ejecucion e

    WHERE e.ProgramaProduccionID =
          pp.ProgramaProduccionID

      AND e.Activo = 1

    ORDER BY
        e.EjecucionProduccionID DESC
) ejecucion

OUTER APPLY
(
    SELECT TOP (1)
        ant.MoldeID,
        ant.MoldeCodigo

    FROM dbo.Planeacion_ProgramaProduccion ant

    WHERE ant.Activo = 1

      AND ant.ProgramaProduccionID <>
          pp.ProgramaProduccionID

      AND ant.MaquinaID =
          pp.MaquinaID

      AND ant.FechaInicioProgramada <
          pp.FechaInicioProgramada

      AND ISNULL(
              ant.EstatusID,
              1
          ) NOT IN
          (
              5,
              6,
              9,
              99
          )

    ORDER BY
        ant.FechaInicioProgramada DESC,
        ant.ProgramaProduccionID DESC
) anterior

OUTER APPLY
(
    SELECT TOP (1)
        po.PersonaID,

        LTRIM
        (
            RTRIM
            (
                ISNULL(
                    p.Nombre,
                    N''
                )
                + N' '
                + ISNULL(
                    p.ApellidoPaterno,
                    N''
                )
                + N' '
                + ISNULL(
                    p.ApellidoMaterno,
                    N''
                )
            )
        ) AS NombreCompleto

    FROM dbo.Planeacion_ProgramaOperadores po

    LEFT JOIN dbo.Persona p
        ON p.PersonaID =
           po.PersonaID

    WHERE po.ProgramaProduccionID =
          pp.ProgramaProduccionID

      AND po.Activo = 1

      AND UPPER(
              LTRIM(
                  RTRIM(
                      ISNULL(
                          po.RolOperador,
                          N''
                      )
                  )
              )
          ) = N'PRINCIPAL'

    ORDER BY
        po.ProgramaOperadorID
) opPrincipal

OUTER APPLY
(
    SELECT TOP (1)
        po.PersonaID,

        LTRIM
        (
            RTRIM
            (
                ISNULL(
                    p.Nombre,
                    N''
                )
                + N' '
                + ISNULL(
                    p.ApellidoPaterno,
                    N''
                )
                + N' '
                + ISNULL(
                    p.ApellidoMaterno,
                    N''
                )
            )
        ) AS NombreCompleto

    FROM dbo.Planeacion_ProgramaOperadores po

    LEFT JOIN dbo.Persona p
        ON p.PersonaID =
           po.PersonaID

    WHERE po.ProgramaProduccionID =
          pp.ProgramaProduccionID

      AND po.Activo = 1

      AND UPPER(
              LTRIM(
                  RTRIM(
                      ISNULL(
                          po.RolOperador,
                          N''
                      )
                  )
              )
          ) = N'AUXILIAR'

    ORDER BY
        po.ProgramaOperadorID
) opAuxiliar

OUTER APPLY
(
    SELECT
        LTRIM
        (
            RTRIM
            (
                ISNULL(
                    p.Nombre,
                    N''
                )
                + N' '
                + ISNULL(
                    p.ApellidoPaterno,
                    N''
                )
                + N' '
                + ISNULL(
                    p.ApellidoMaterno,
                    N''
                )
            )
        ) AS NombreCompleto

    FROM dbo.Persona p

    WHERE p.PersonaID =
          pa.UsuarioConfirmacionID
) confirmador

WHERE pa.Activo = 1

  AND pp.Activo = 1

  AND pa.Estado IN
      (
          N'PENDIENTE',
          N'CONFIRMADA'
      )

  AND
  (
        pa.Estado = N'PENDIENTE'

        OR

        pa.FechaConfirmacion >=
            DATEADD(
                DAY,
                -7,
                GETDATE()
            )
  )

  AND
  (
        @MaquinaID IS NULL
        OR pp.MaquinaID =
           @MaquinaID
  )

  AND
  (
        @Filtro IS NULL

        OR pp.NumeroParte
            LIKE N'%'
                 + @Filtro
                 + N'%'

        OR pp.ReferenciaSAP
            LIKE N'%'
                 + @Filtro
                 + N'%'

        OR pp.DesignacionDescripcionSAP
            LIKE N'%'
                 + @Filtro
                 + N'%'

        OR pp.MaquinaCodigo
            LIKE N'%'
                 + @Filtro
                 + N'%'

        OR pp.MaquinaNombre
            LIKE N'%'
                 + @Filtro
                 + N'%'

        OR pp.MoldeCodigo
            LIKE N'%'
                 + @Filtro
                 + N'%'

        OR s.NumeroOFRecibida
            LIKE N'%'
                 + @Filtro
                 + N'%'

        OR d.MaterialCodigo
            LIKE N'%'
                 + @Filtro
                 + N'%'

        OR d.MaterialDescripcion
            LIKE N'%'
                 + @Filtro
                 + N'%'

        OR d.EmbalajeCodigo
            LIKE N'%'
                 + @Filtro
                 + N'%'
  )

ORDER BY
    CASE
        WHEN pa.Estado =
             N'PENDIENTE'
         AND GETDATE() >
             pa.FechaObjetivo
            THEN 1

        WHEN pa.Estado =
             N'PENDIENTE'
         AND GETDATE() >=
             pa.FechaAviso
            THEN 2

        WHEN pa.Estado =
             N'PENDIENTE'
            THEN 3

        ELSE 4
    END,

    pa.FechaAviso,
    pa.FechaObjetivo,
    pa.PreparacionAnticipadaID;";

            await using var cmd =
                new SqlCommand(
                    sql,
                    cn);

            cmd.Parameters.Add(
                "@MaquinaID",
                SqlDbType.Int).Value =
                maquinaId.HasValue &&
                maquinaId.Value > 0
                    ? maquinaId.Value
                    : DBNull.Value;

            cmd.Parameters.Add(
                "@Filtro",
                SqlDbType.NVarChar,
                200).Value =
                string.IsNullOrWhiteSpace(
                    filtro)
                    ? DBNull.Value
                    : filtro.Trim();

            await using var rd =
                await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                var inicio =
                    PreparacionNullableDateTime(
                        rd,
                        "FechaInicioProgramada");

                var cambio =
                    PreparacionNullableTimeSpan(
                        rd,
                        "Cambio");

                var arranque =
                    PreparacionNullableTimeSpan(
                        rd,
                        "Arranque");

                DateTime? fechaCambio =
                    null;

                DateTime? fechaArranque =
                    null;

                if (inicio.HasValue)
                {
                    fechaCambio =
                        ConstruirFechaPreparacion(
                            inicio.Value,
                            cambio);

                    fechaArranque =
                        ConstruirFechaPreparacion(
                            inicio.Value,
                            arranque);
                }

                lista.Add(
                    new ProduccionPreparacionTareaVm
                    {
                        PreparacionAnticipadaID =
                            Convert.ToInt32(
                                rd[
                                    "PreparacionAnticipadaID"]),

                        ProgramaProduccionID =
                            Convert.ToInt32(
                                rd[
                                    "ProgramaProduccionID"]),

                        EjecucionProduccionID =
                            PreparacionNullableInt(
                                rd,
                                "EjecucionProduccionID"),

                        TipoTarea =
                            PreparacionTexto(
                                rd,
                                "TipoTarea")
                            ?? string.Empty,

                        Estado =
                            PreparacionTexto(
                                rd,
                                "Estado")
                            ?? ProduccionPreparacionEstado
                                .Pendiente,

                        FechaObjetivo =
                            Convert.ToDateTime(
                                rd["FechaObjetivo"]),

                        FechaAviso =
                            Convert.ToDateTime(
                                rd["FechaAviso"]),

                        UsuarioConfirmacionID =
                            PreparacionNullableInt(
                                rd,
                                "UsuarioConfirmacionID"),

                        UsuarioConfirmacionNombre =
                            PreparacionTexto(
                                rd,
                                "UsuarioConfirmacionNombre"),

                        FechaConfirmacion =
                            PreparacionNullableDateTime(
                                rd,
                                "FechaConfirmacion"),

                        Observaciones =
                            PreparacionTexto(
                                rd,
                                "Observaciones"),

                        SolicitudProduccionID =
                            PreparacionNullableInt(
                                rd,
                                "SolicitudProduccionID"),

                        SolicitudProduccionDetalleID =
                            PreparacionNullableInt(
                                rd,
                                "SolicitudProduccionDetalleID"),

                        NumeroOF =
                            PreparacionTexto(
                                rd,
                                "NumeroOFRecibida"),

                        MaquinaID =
                            PreparacionNullableInt(
                                rd,
                                "MaquinaID"),

                        MaquinaCodigo =
                            PreparacionTexto(
                                rd,
                                "MaquinaCodigo"),

                        MaquinaNombre =
                            PreparacionTexto(
                                rd,
                                "MaquinaNombre"),

                        ParteID =
                            PreparacionNullableInt(
                                rd,
                                "ParteID"),

                        NumeroParte =
                            PreparacionTexto(
                                rd,
                                "NumeroParte"),

                        ReferenciaSAP =
                            PreparacionTexto(
                                rd,
                                "ReferenciaSAP"),

                        DescripcionParte =
                            PreparacionTexto(
                                rd,
                                "DescripcionParte"),

                        MoldeID =
                            PreparacionNullableInt(
                                rd,
                                "MoldeID"),

                        MoldeCodigo =
                            PreparacionTexto(
                                rd,
                                "MoldeCodigo"),

                        MoldeAnteriorID =
                            PreparacionNullableInt(
                                rd,
                                "MoldeAnteriorID"),

                        MoldeAnteriorCodigo =
                            PreparacionTexto(
                                rd,
                                "MoldeAnteriorCodigo"),

                        CantidadProgramada =
                            rd["CantidadProgramada"]
                                == DBNull.Value
                                    ? 0
                                    : Convert.ToInt32(
                                        rd[
                                            "CantidadProgramada"]),

                        FechaInicioProgramada =
                            inicio,

                        FechaFinProgramada =
                            PreparacionNullableDateTime(
                                rd,
                                "FechaFinProgramada"),

                        FechaCambioMolde =
                            fechaCambio,

                        FechaArranque =
                            fechaArranque,

                        TipoSecado =
                            PreparacionTexto(
                                rd,
                                "TipoSecado"),

                        HorasSecado =
                            PreparacionNullableDecimal(
                                rd,
                                "HorasSecado"),

                        MaterialCodigo =
                            PreparacionTexto(
                                rd,
                                "MaterialCodigo"),

                        MaterialDescripcion =
                            PreparacionTexto(
                                rd,
                                "MaterialDescripcion"),

                        EmbalajeCodigo =
                            PreparacionTexto(
                                rd,
                                "EmbalajeCodigo"),

                        EmbalajeDescripcion =
                            PreparacionTexto(
                                rd,
                                "EmbalajeDescripcion"),

                        PiezasPorEmbalaje =
                            PreparacionNullableDecimal(
                                rd,
                                "PiezasPorEmbalaje"),

                        CantidadEmbalajes =
                            PreparacionNullableDecimal(
                                rd,
                                "CantidadEmbalajes"),

                        OperadorPrincipalID =
                            PreparacionNullableInt(
                                rd,
                                "OperadorPrincipalID"),

                        OperadorPrincipalNombre =
                            PreparacionTexto(
                                rd,
                                "OperadorPrincipalNombre"),

                        OperadorAuxiliarID =
                            PreparacionNullableInt(
                                rd,
                                "OperadorAuxiliarID"),

                        OperadorAuxiliarNombre =
                            PreparacionTexto(
                                rd,
                                "OperadorAuxiliarNombre"),

                        Ahora =
                            ahora
                    });
            }

            return lista;
        }

       

        private static async Task<List<ProduccionPreparacionMaquinaVm>>
            CargarMaquinasPreparacionAsync(
                SqlConnection cn)
        {
            var lista =
                new List<ProduccionPreparacionMaquinaVm>();

            const string sql = @"
SELECT
    MaquinaID,
    Codigo,
    Nombre
FROM dbo.ERP_Maquinas
WHERE Activo = 1
ORDER BY
    Codigo,
    Nombre;";

            await using var cmd =
                new SqlCommand(
                    sql,
                    cn);

            await using var rd =
                await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                lista.Add(
                    new ProduccionPreparacionMaquinaVm
                    {
                        MaquinaID =
                            Convert.ToInt32(
                                rd["MaquinaID"]),

                        Codigo =
                            PreparacionTexto(
                                rd,
                                "Codigo")
                            ?? string.Empty,

                        Nombre =
                            PreparacionTexto(
                                rd,
                                "Nombre")
                    });
            }

            return lista;
        }

        // ============================================================
        // DETERMINAR CAMBIO REAL DE MOLDE
        // ============================================================

        private static bool DeterminarCambioMoldePreparacion(
            int? moldeAnteriorId,
            string? moldeAnteriorCodigo,
            int? moldeActualId,
            string? moldeActualCodigo)
        {
            if (moldeAnteriorId.HasValue &&
                moldeActualId.HasValue)
            {
                return moldeAnteriorId.Value !=
                       moldeActualId.Value;
            }

            /*
             * Si no tenemos ambos IDs pero sí códigos,
             * usamos código como respaldo.
             */
            var anterior =
                string.IsNullOrWhiteSpace(
                    moldeAnteriorCodigo)
                    ? null
                    : moldeAnteriorCodigo.Trim();

            var actual =
                string.IsNullOrWhiteSpace(
                    moldeActualCodigo)
                    ? null
                    : moldeActualCodigo.Trim();

            if (anterior == null ||
                actual == null)
            {
               
                return false;
            }

            return !string.Equals(
                anterior,
                actual,
                StringComparison.OrdinalIgnoreCase);
        }

    
        private static DateTime ConstruirFechaPreparacion(
            DateTime fechaInicioPrograma,
            TimeSpan? hora)
        {
            if (!hora.HasValue)
            {
                return NormalizarFechaPreparacion(
                    fechaInicioPrograma);
            }

            var fecha =
                fechaInicioPrograma
                    .Date
                    .Add(
                        hora.Value);

            /*
             * Ejemplo:
             *
             * programa inicia 23:30
             * arranque = 00:30
             *
             * Entonces el arranque pertenece al día siguiente.
             */
            if (fecha <
                fechaInicioPrograma)
            {
                fecha =
                    fecha.AddDays(1);
            }

            return NormalizarFechaPreparacion(
                fecha);
        }

        private static DateTime NormalizarFechaPreparacion(
            DateTime value)
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

      
        private static string? PreparacionTexto(
            SqlDataReader rd,
            string columna)
        {
            return rd[columna] == DBNull.Value
                ? null
                : rd[columna]
                    .ToString()?
                    .Trim();
        }

        private static int? PreparacionNullableInt(
            SqlDataReader rd,
            string columna)
        {
            return rd[columna] == DBNull.Value
                ? null
                : Convert.ToInt32(
                    rd[columna]);
        }

        private static decimal? PreparacionNullableDecimal(
            SqlDataReader rd,
            string columna)
        {
            return rd[columna] == DBNull.Value
                ? null
                : Convert.ToDecimal(
                    rd[columna]);
        }

        private static DateTime? PreparacionNullableDateTime(
            SqlDataReader rd,
            string columna)
        {
            return rd[columna] == DBNull.Value
                ? null
                : Convert.ToDateTime(
                    rd[columna]);
        }

        private static TimeSpan? PreparacionNullableTimeSpan(
            SqlDataReader rd,
            string columna)
        {
            return rd[columna] == DBNull.Value
                ? null
                : (TimeSpan)rd[columna];
        }


        private sealed class ProgramaPreparacionInterno
        {
            public int ProgramaProduccionID { get; set; }

            public int? SolicitudProduccionID { get; set; }

            public int? SolicitudProduccionDetalleID { get; set; }

            public string? NumeroOF { get; set; }

            public int? MaquinaID { get; set; }

            public string? MaquinaCodigo { get; set; }

            public string? MaquinaNombre { get; set; }

            public int? ParteID { get; set; }

            public string? NumeroParte { get; set; }

            public string? ReferenciaSAP { get; set; }

            public string? DescripcionParte { get; set; }

            public int? MoldeID { get; set; }

            public string? MoldeCodigo { get; set; }

            public int? MoldeAnteriorID { get; set; }

            public string? MoldeAnteriorCodigo { get; set; }

            public int CantidadProgramada { get; set; }

            public DateTime FechaInicioProgramada { get; set; }

            public DateTime? FechaFinProgramada { get; set; }

            public TimeSpan? Cambio { get; set; }

            public TimeSpan? Arranque { get; set; }

            public DateTime? FechaCambioMolde { get; set; }

            public DateTime? FechaArranque { get; set; }

            public string? TipoSecado { get; set; }

            public decimal? HorasSecado { get; set; }

            public string? MaterialCodigo { get; set; }

            public string? MaterialDescripcion { get; set; }

            public string? EmbalajeCodigo { get; set; }

            public string? EmbalajeDescripcion { get; set; }

            public decimal? PiezasPorEmbalaje { get; set; }

            public decimal? CantidadEmbalajes { get; set; }

            public bool RequiereCambioMolde { get; set; }
        }
    }
}