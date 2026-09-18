using ERP.NSQuell.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace ERP.NSQuell.Controllers;

public sealed partial class ProduccionPersonalController
{
    [HttpGet("OperativaDatos")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> OperativaDatos(int programaProduccionId)
    {
        if (!UsuarioEnSesion()) return Unauthorized(new { ok = false, sesionExpirada = true, mensaje = "La sesión terminó. Vuelve a iniciar sesión." });
        if (programaProduccionId <= 0) return BadRequest(new { ok = false, mensaje = "Programa de Producción no válido." });

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync();

        if (!await TablaPersonalConfiguradaAsync(cn, null))
            return StatusCode(StatusCodes.Status409Conflict, new { ok = false, mensaje = "La programación de personal todavía no está configurada." });

        var programa = await CargarProgramaBaseAsync(programaProduccionId, cn, null, false);
        if (programa == null) return NotFound(new { ok = false, mensaje = "No se encontró el programa de Producción." });

        var turnos = await CargarTurnosAsync(cn);
        var asignaciones = await CargarAsignacionesOperativasAsync(programaProduccionId, cn);

        var asignacionInicial = asignaciones
            .OrderBy(x => Math.Abs((x.Inicio - programa.Inicio).TotalMinutes))
            .ThenBy(x => x.Inicio)
            .FirstOrDefault();

        var fechaSugerida = asignacionInicial?.FechaTrabajo ?? programa.Inicio.Date;
        var turnoSugeridoId = asignacionInicial?.TurnoID ?? ResolverTurnoSugeridoOperativo(programa, turnos, fechaSugerida);

        return Json(new
        {
            ok = true,
            programa = new
            {
                programaProduccionID = programa.ProgramaProduccionID,
                of = programa.FolioOF,
                maquina = programa.MaquinaCodigo,
                parte = programa.ParteVisible,
                inicio = programa.Inicio,
                fin = programa.Fin
            },
            fechaSugerida = fechaSugerida.ToString("yyyy-MM-dd"),
            turnoSugeridoID = turnoSugeridoId,
            turnos = turnos.Select(x => new
            {
                turnoID = x.TurnoID,
                nombre = x.NombreVisible,
                horario = x.HorarioTexto,
                etiqueta = x.EtiquetaDropdown,
                principal = x.EsTurnoPrincipal
            }),
            asignacionInicial = asignacionInicial == null ? null : new
            {
                asignacionPersonalID = asignacionInicial.AsignacionPersonalID,
                fechaTrabajo = asignacionInicial.FechaTrabajo.ToString("yyyy-MM-dd"),
                turnoID = asignacionInicial.TurnoID,
                operadorID = asignacionInicial.OperadorID,
                operadorNombre = asignacionInicial.OperadorNombre,
                auxiliarID = asignacionInicial.AuxiliarID,
                auxiliarNombre = asignacionInicial.AuxiliarNombre,
                tecnicoProduccionID = asignacionInicial.TecnicoProduccionID,
                tecnicoProduccionNombre = asignacionInicial.TecnicoProduccionNombre,
                observaciones = asignacionInicial.Observaciones,
                inicio = asignacionInicial.Inicio,
                fin = asignacionInicial.Fin
            },
            asignaciones = asignaciones.Select(x => new
            {
                asignacionPersonalID = x.AsignacionPersonalID,
                fechaTrabajo = x.FechaTrabajo.ToString("yyyy-MM-dd"),
                turnoID = x.TurnoID,
                turno = x.TurnoNombre,
                inicio = x.Inicio,
                fin = x.Fin,
                operadorID = x.OperadorID,
                operador = x.OperadorNombre,
                auxiliarID = x.AuxiliarID,
                auxiliar = x.AuxiliarNombre,
                tecnicoProduccionID = x.TecnicoProduccionID,
                tecnico = x.TecnicoProduccionNombre
            })
        });
    }

    [HttpPost("GuardarOperativa")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GuardarOperativa(ProduccionPersonalGuardarVm vm)
    {
        if (!UsuarioEnSesion()) return Unauthorized(new { ok = false, sesionExpirada = true, mensaje = "La sesión terminó. Vuelve a iniciar sesión." });
        if (vm.ProgramaProduccionID <= 0 || vm.TurnoID <= 0) return BadRequest(new { ok = false, mensaje = "Programa o turno no válido." });
        if (!vm.OperadorID.HasValue || vm.OperadorID.Value <= 0) return BadRequest(new { ok = false, mensaje = "Selecciona el operador principal para continuar." });

        vm.Observaciones = string.IsNullOrWhiteSpace(vm.Observaciones) ? null : vm.Observaciones.Trim();
        if (vm.Observaciones?.Length > 500) return BadRequest(new { ok = false, mensaje = "Las observaciones no pueden superar 500 caracteres." });

        var ids = new[] { vm.OperadorID, vm.AuxiliarID, vm.TecnicoProduccionID }
            .Where(x => x.HasValue && x.Value > 0)
            .Select(x => x!.Value)
            .ToList();

        if (ids.Count != ids.Distinct().Count())
            return BadRequest(new { ok = false, mensaje = "Operador, auxiliar y técnico deben ser personas diferentes." });

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync();

        if (!await TablaPersonalConfiguradaAsync(cn, null))
            return StatusCode(StatusCodes.Status409Conflict, new { ok = false, mensaje = "Falta configurar la tabla de programación de personal." });

        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var programa = await CargarProgramaBaseAsync(vm.ProgramaProduccionID, cn, tx, true);
            if (programa == null) throw new InvalidOperationException("El programa ya no está disponible.");

            var turno = await CargarTurnoAsync(vm.TurnoID, cn, tx);
            if (turno == null) throw new InvalidOperationException("El turno ya no está disponible.");

            var ventana = ConstruirVentana(programa, turno, vm.FechaTrabajo.Date);
            if (ventana == null) throw new InvalidOperationException("El turno seleccionado no cruza con el horario programado de la OF.");

            var operadorNombre = await ValidarPersonaRolAsync(vm.OperadorID.Value, "OPERADOR", programa.ParteID, cn, tx);
            if (string.IsNullOrWhiteSpace(operadorNombre))
                throw new InvalidOperationException("El operador seleccionado no está activo o no cumple la polivalencia requerida para esta parte.");

            string? auxiliarNombre = null;
            if (vm.AuxiliarID.HasValue && vm.AuxiliarID.Value > 0)
            {
                auxiliarNombre = await ValidarPersonaRolAsync(vm.AuxiliarID.Value, "AUXILIAR", programa.ParteID, cn, tx);
                if (string.IsNullOrWhiteSpace(auxiliarNombre))
                    throw new InvalidOperationException("El auxiliar seleccionado no pertenece al catálogo activo de auxiliares.");
            }

            string? tecnicoNombre = null;
            if (vm.TecnicoProduccionID.HasValue && vm.TecnicoProduccionID.Value > 0)
            {
                tecnicoNombre = await ValidarPersonaRolAsync(vm.TecnicoProduccionID.Value, "TECNICO", programa.ParteID, cn, tx);
                if (string.IsNullOrWhiteSpace(tecnicoNombre))
                    throw new InvalidOperationException("El técnico seleccionado no pertenece al catálogo activo de Técnicos de Producción.");
            }

            foreach (var personaId in ids)
            {
                var conflicto = await BuscarConflictoPersonaAsync(personaId, ventana.Value.Inicio, ventana.Value.Fin, vm.AsignacionPersonalID, cn, tx);
                if (conflicto != null)
                {
                    throw new InvalidOperationException(
                        "La persona " + conflicto.PersonaNombre +
                        " ya está asignada al Programa " + conflicto.ProgramaProduccionID +
                        " de " + conflicto.Inicio.ToString("dd/MM HH:mm") +
                        " a " + conflicto.Fin.ToString("dd/MM HH:mm") + ".");
                }
            }

            var existente = await ResolverAsignacionExistenteAsync(vm, cn, tx);
            var usuarioId = UsuarioID();
            int asignacionPersonalId;

            if (existente.HasValue)
            {
                const string actualizar = @"
UPDATE dbo.Produccion_ProgramaPersonalAsignaciones
SET FechaTrabajo=@FechaTrabajo,
    TurnoID=@TurnoID,
    TurnoNombre=@TurnoNombre,
    Inicio=@Inicio,
    Fin=@Fin,
    OperadorID=@OperadorID,
    AuxiliarID=@AuxiliarID,
    TecnicoProduccionID=@TecnicoProduccionID,
    Observaciones=@Observaciones,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=SYSDATETIME(),
    Activo=1
WHERE AsignacionPersonalID=@AsignacionPersonalID;";
                await using var cmd = new SqlCommand(actualizar, cn, tx);
                AgregarParametrosGuardar(cmd, existente.Value, vm, turno, ventana.Value, usuarioId);
                await cmd.ExecuteNonQueryAsync();
                asignacionPersonalId = existente.Value;
            }
            else
            {
                const string insertar = @"
INSERT INTO dbo.Produccion_ProgramaPersonalAsignaciones
(
    ProgramaProduccionID,
    FechaTrabajo,
    TurnoID,
    TurnoNombre,
    Inicio,
    Fin,
    OperadorID,
    AuxiliarID,
    TecnicoProduccionID,
    Observaciones,
    UsuarioCreacionID,
    FechaCreacion,
    Activo
)
OUTPUT INSERTED.AsignacionPersonalID
VALUES
(
    @ProgramaProduccionID,
    @FechaTrabajo,
    @TurnoID,
    @TurnoNombre,
    @Inicio,
    @Fin,
    @OperadorID,
    @AuxiliarID,
    @TecnicoProduccionID,
    @Observaciones,
    @UsuarioID,
    SYSDATETIME(),
    1
);";
                await using var cmd = new SqlCommand(insertar, cn, tx);
                AgregarParametrosGuardar(cmd, null, vm, turno, ventana.Value, usuarioId);
                asignacionPersonalId = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            }

            /*
             * Agenda Operativa y otras vistas pre-arranque todavía consultan
             * Planeacion_ProgramaOperadores para PRINCIPAL/AUXILIAR.
             * Sincronizamos dentro de la misma transacción para que al guardar
             * desaparezca inmediatamente el bloqueo "Personal asignado".
             */
            await SincronizarOperadorProgramaOperativoAsync(
                vm.ProgramaProduccionID,
                vm.OperadorID,
                "PRINCIPAL",
                usuarioId,
                cn,
                tx);

            await SincronizarOperadorProgramaOperativoAsync(
                vm.ProgramaProduccionID,
                vm.AuxiliarID,
                "AUXILIAR",
                usuarioId,
                cn,
                tx);

            await tx.CommitAsync();

            return Json(new
            {
                ok = true,
                programaProduccionId = vm.ProgramaProduccionID,
                asignacionPersonalID = asignacionPersonalId,
                operador = operadorNombre,
                auxiliar = auxiliarNombre,
                tecnico = tecnicoNombre,
                ventanaInicio = ventana.Value.Inicio,
                ventanaFin = ventana.Value.Fin,
                mensaje = "Personal asignado correctamente."
            });
        }
        catch (Exception ex)
        {
            try { await tx.RollbackAsync(); } catch { }
            return BadRequest(new { ok = false, mensaje = "No fue posible guardar el personal: " + ex.Message });
        }
    }

    private static int? ResolverTurnoSugeridoOperativo(
        ProduccionPersonalProgramaVm programa,
        List<ProduccionPersonalTurnoVm> turnos,
        DateTime fechaTrabajo)
    {
        foreach (var turno in turnos.OrderBy(x => x.EsTurnoPrincipal ? 0 : 1).ThenBy(x => x.Orden))
        {
            var ventana = ConstruirVentana(programa, turno, fechaTrabajo.Date);
            if (!ventana.HasValue) continue;
            if (programa.Inicio >= ventana.Value.Inicio && programa.Inicio < ventana.Value.Fin)
                return turno.TurnoID;
        }

        return turnos
            .OrderBy(x => x.EsTurnoPrincipal ? 0 : 1)
            .ThenBy(x => x.Orden)
            .FirstOrDefault(x => ConstruirVentana(programa, x, fechaTrabajo.Date).HasValue)
            ?.TurnoID;
    }

    private static async Task<List<ProduccionPersonalAsignacionVm>> CargarAsignacionesOperativasAsync(
        int programaProduccionId,
        SqlConnection cn)
    {
        const string sql = @"
SELECT
    a.AsignacionPersonalID,
    a.ProgramaProduccionID,
    a.FechaTrabajo,
    a.TurnoID,
    ISNULL(a.TurnoNombre,N'') AS TurnoNombre,
    a.Inicio,
    a.Fin,
    a.OperadorID,
    LTRIM(RTRIM(CONCAT(ISNULL(op.Nombre,N''),N' ',ISNULL(op.ApellidoPaterno,N''),N' ',ISNULL(op.ApellidoMaterno,N'')))) AS OperadorNombre,
    a.AuxiliarID,
    LTRIM(RTRIM(CONCAT(ISNULL(aux.Nombre,N''),N' ',ISNULL(aux.ApellidoPaterno,N''),N' ',ISNULL(aux.ApellidoMaterno,N'')))) AS AuxiliarNombre,
    a.TecnicoProduccionID,
    LTRIM(RTRIM(CONCAT(ISNULL(tec.Nombre,N''),N' ',ISNULL(tec.ApellidoPaterno,N''),N' ',ISNULL(tec.ApellidoMaterno,N'')))) AS TecnicoProduccionNombre,
    ISNULL(a.Observaciones,N'') AS Observaciones
FROM dbo.Produccion_ProgramaPersonalAsignaciones a
LEFT JOIN dbo.Persona op ON op.PersonaID=a.OperadorID
LEFT JOIN dbo.Persona aux ON aux.PersonaID=a.AuxiliarID
LEFT JOIN dbo.Persona tec ON tec.PersonaID=a.TecnicoProduccionID
WHERE a.Activo=1
  AND a.ProgramaProduccionID=@ProgramaProduccionID
ORDER BY a.Inicio,a.AsignacionPersonalID;";

        var lista = new List<ProduccionPersonalAsignacionVm>();
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaProduccionId;
        await using var rd = await cmd.ExecuteReaderAsync();

        while (await rd.ReadAsync())
        {
            lista.Add(new ProduccionPersonalAsignacionVm
            {
                AsignacionPersonalID = Convert.ToInt32(rd["AsignacionPersonalID"]),
                ProgramaProduccionID = Convert.ToInt32(rd["ProgramaProduccionID"]),
                FechaTrabajo = Convert.ToDateTime(rd["FechaTrabajo"]),
                TurnoID = rd["TurnoID"] == DBNull.Value ? null : Convert.ToInt32(rd["TurnoID"]),
                TurnoNombre = rd["TurnoNombre"]?.ToString()?.Trim() ?? string.Empty,
                Inicio = Convert.ToDateTime(rd["Inicio"]),
                Fin = Convert.ToDateTime(rd["Fin"]),
                OperadorID = rd["OperadorID"] == DBNull.Value ? null : Convert.ToInt32(rd["OperadorID"]),
                OperadorNombre = rd["OperadorNombre"]?.ToString()?.Trim() ?? string.Empty,
                AuxiliarID = rd["AuxiliarID"] == DBNull.Value ? null : Convert.ToInt32(rd["AuxiliarID"]),
                AuxiliarNombre = rd["AuxiliarNombre"]?.ToString()?.Trim() ?? string.Empty,
                TecnicoProduccionID = rd["TecnicoProduccionID"] == DBNull.Value ? null : Convert.ToInt32(rd["TecnicoProduccionID"]),
                TecnicoProduccionNombre = rd["TecnicoProduccionNombre"]?.ToString()?.Trim() ?? string.Empty,
                Observaciones = rd["Observaciones"]?.ToString()?.Trim() ?? string.Empty
            });
        }

        return lista;
    }

    private static async Task SincronizarOperadorProgramaOperativoAsync(
        int programaProduccionId,
        int? personaId,
        string rolOperador,
        int usuarioId,
        SqlConnection cn,
        SqlTransaction tx)
    {
        if (programaProduccionId <= 0)
            throw new ArgumentException("El programa de Producción no es válido.", nameof(programaProduccionId));

        rolOperador = (rolOperador ?? string.Empty).Trim().ToUpperInvariant();
        if (rolOperador != "PRINCIPAL" && rolOperador != "AUXILIAR")
            throw new ArgumentException("El rol del operador no es válido.", nameof(rolOperador));

        const string sql = @"
UPDATE dbo.Planeacion_ProgramaOperadores
SET Activo=0,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=GETDATE()
WHERE ProgramaProduccionID=@ProgramaProduccionID
  AND UPPER(LTRIM(RTRIM(ISNULL(RolOperador,N''))))=@RolOperador
  AND Activo=1
  AND
  (
      @PersonaID IS NULL
      OR PersonaID<>@PersonaID
  );

IF @PersonaID IS NOT NULL
BEGIN
    IF EXISTS
    (
        SELECT 1
        FROM dbo.Planeacion_ProgramaOperadores WITH (UPDLOCK,HOLDLOCK)
        WHERE ProgramaProduccionID=@ProgramaProduccionID
          AND PersonaID=@PersonaID
          AND UPPER(LTRIM(RTRIM(ISNULL(RolOperador,N''))))=@RolOperador
    )
    BEGIN
        UPDATE dbo.Planeacion_ProgramaOperadores
        SET Activo=1,
            UsuarioModificacionID=@UsuarioID,
            FechaModificacion=GETDATE()
        WHERE ProgramaProduccionID=@ProgramaProduccionID
          AND PersonaID=@PersonaID
          AND UPPER(LTRIM(RTRIM(ISNULL(RolOperador,N''))))=@RolOperador;
    END
    ELSE
    BEGIN
        INSERT INTO dbo.Planeacion_ProgramaOperadores
        (
            ProgramaProduccionID,
            PersonaID,
            RolOperador,
            UsuarioCreacionID,
            FechaCreacion,
            Activo
        )
        VALUES
        (
            @ProgramaProduccionID,
            @PersonaID,
            @RolOperador,
            @UsuarioID,
            GETDATE(),
            1
        );
    END;
END;";

        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaProduccionId;
        cmd.Parameters.Add("@PersonaID", SqlDbType.Int).Value = personaId.HasValue ? personaId.Value : DBNull.Value;
        cmd.Parameters.Add("@RolOperador", SqlDbType.NVarChar, 30).Value = rolOperador;
        cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
        await cmd.ExecuteNonQueryAsync();
    }
}
