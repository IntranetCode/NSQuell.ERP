using ERP.NSQuell.Models;
using Microsoft.Data.SqlClient;
using System.Data;

namespace ERP.NSQuell.Controllers;

// NSQ_PRODUCCION_OPERADORES_CANONICOS_LHRH_V2
public sealed partial class ProduccionController
{
    private static async Task<bool> PersonaTieneCuentaOperadorActivaV2Async(
        int personaId,
        SqlConnection cn,
        SqlTransaction tx)
    {
        if (personaId <= 0)
            return false;

        const string sql = @"
SELECT CONVERT(bit, CASE WHEN EXISTS
(
    SELECT 1
    FROM dbo.Persona p WITH(UPDLOCK,HOLDLOCK)
    INNER JOIN dbo.Usuarios u
        ON u.PersonaID=p.PersonaID
       AND ISNULL(u.Activo,0)=1
       AND u.RolID=4
    WHERE p.PersonaID=@PersonaID
      AND ISNULL(p.EsColaboradorActivo,1)=1
      AND UPPER(LTRIM(RTRIM(ISNULL(p.Puesto,N''))))=N'OPERADOR'
)
THEN 1 ELSE 0 END);";

        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@PersonaID", SqlDbType.Int).Value = personaId;
        return Convert.ToBoolean(await cmd.ExecuteScalarAsync() ?? false);
    }

    private async Task<bool> SincronizarCambioOperadorParejaLhRhV2Async(
        int ejecucionOrigenId,
        int programaOrigenId,
        string tipoCambio,
        int personaNuevaId,
        string personaNuevaNombre,
        string motivoCambio,
        int usuarioId,
        SqlConnection cn,
        SqlTransaction tx)
    {
        if (ejecucionOrigenId <= 0 || programaOrigenId <= 0)
            return false;

        tipoCambio = (tipoCambio ?? string.Empty).Trim().ToUpperInvariant();
        if (tipoCambio != "PRINCIPAL" && tipoCambio != "AUXILIAR")
            throw new InvalidOperationException("El tipo de cambio LH/RH no es valido.");

        var pareja = await ObtenerParejaLhRhProduccionAsync(programaOrigenId, cn, tx);
        if (pareja == null)
            return false;

        if (!pareja.MismaMaquina || !pareja.MismoMolde || !pareja.MismaVentanaProgramada)
        {
            throw new InvalidOperationException(
                "La pareja LH/RH no conserva la misma maquina, molde y ventana programada. No se cambio el operador para evitar desincronizar Produccion.");
        }

        if (!pareja.EjecucionParejaID.HasValue || pareja.EjecucionParejaID.Value <= 0)
        {
            throw new InvalidOperationException(
                $"La OF pareja {pareja.OFParejaTexto} no tiene una ejecucion activa. Corrige la pareja LH/RH antes de cambiar el operador.");
        }

        if (pareja.EjecucionParejaID.Value == ejecucionOrigenId)
            throw new InvalidOperationException("La ejecucion pareja LH/RH coincide con la ejecucion origen.");

        const string sqlPareja = @"
SELECT TOP (1)
    e.ProgramaProduccionID,
    e.EstatusID,
    e.OperadorID,
    e.OperadorNombre,
    e.OperadorAuxiliarID,
    e.OperadorAuxiliarNombre
FROM dbo.Produccion_Ejecucion e WITH(UPDLOCK,HOLDLOCK)
WHERE e.EjecucionProduccionID=@EjecucionProduccionID
  AND e.ProgramaProduccionID=@ProgramaProduccionID
  AND e.Activo=1;";

        int estatusPareja;
        int? principalParejaId;
        int? auxiliarParejaId;
        string principalParejaNombre;
        string auxiliarParejaNombre;

        await using (var cmd = new SqlCommand(sqlPareja, cn, tx))
        {
            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = pareja.EjecucionParejaID.Value;
            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = pareja.ProgramaParejaID;

            await using var rd = await cmd.ExecuteReaderAsync();
            if (!await rd.ReadAsync())
                throw new InvalidOperationException($"No se encontro la ejecucion activa de {pareja.OFParejaTexto}.");

            estatusPareja = Convert.ToInt32(rd["EstatusID"]);
            principalParejaId = rd["OperadorID"] == DBNull.Value ? null : Convert.ToInt32(rd["OperadorID"]);
            auxiliarParejaId = rd["OperadorAuxiliarID"] == DBNull.Value ? null : Convert.ToInt32(rd["OperadorAuxiliarID"]);
            principalParejaNombre = rd["OperadorNombre"] == DBNull.Value ? "Sin asignar" : rd["OperadorNombre"]?.ToString()?.Trim() ?? "Sin asignar";
            auxiliarParejaNombre = rd["OperadorAuxiliarNombre"] == DBNull.Value ? "Sin asignar" : rd["OperadorAuxiliarNombre"]?.ToString()?.Trim() ?? "Sin asignar";
        }

        if (estatusPareja != ProduccionEstatus.EnPreparacion &&
            estatusPareja != ProduccionEstatus.EnProduccion &&
            estatusPareja != ProduccionEstatus.Pausado)
        {
            throw new InvalidOperationException(
                $"La OF pareja {pareja.OFParejaTexto} ya no esta en preparacion, produccion o pausa. No se puede aplicar un cambio fisico conjunto.");
        }

        int? principalNuevoId = principalParejaId;
        string principalNuevoNombre = principalParejaNombre;
        int? auxiliarNuevoId = auxiliarParejaId;
        string? auxiliarNuevoNombre = auxiliarParejaId.HasValue ? auxiliarParejaNombre : null;

        if (tipoCambio == "PRINCIPAL")
        {
            if (auxiliarParejaId.HasValue && auxiliarParejaId.Value == personaNuevaId)
            {
                throw new InvalidOperationException(
                    $"En {pareja.OFParejaTexto}, la persona seleccionada esta asignada como auxiliar. Principal y auxiliar no pueden ser la misma persona.");
            }

            if (principalParejaId == personaNuevaId)
                return true;

            principalNuevoId = personaNuevaId;
            principalNuevoNombre = personaNuevaNombre.Trim();
        }
        else
        {
            if (principalParejaId.HasValue && principalParejaId.Value == personaNuevaId)
            {
                throw new InvalidOperationException(
                    $"En {pareja.OFParejaTexto}, la persona seleccionada es el operador principal. Principal y auxiliar no pueden ser la misma persona.");
            }

            if (auxiliarParejaId == personaNuevaId)
                return true;

            auxiliarNuevoId = personaNuevaId;
            auxiliarNuevoNombre = personaNuevaNombre.Trim();
        }

        var observacionPareja = tipoCambio == "PRINCIPAL"
            ? $"Cambio manual LH/RH de operador principal. {principalParejaNombre} -> {principalNuevoNombre}. Motivo: {motivoCambio}"
            : $"Cambio manual LH/RH de auxiliar. {auxiliarParejaNombre} -> {auxiliarNuevoNombre}. Motivo: {motivoCambio}";

        if (observacionPareja.Length > 1000)
            observacionPareja = observacionPareja[..1000];

        const string sqlActualizarPareja = @"
UPDATE dbo.Produccion_Ejecucion
SET OperadorID=@OperadorPrincipalID,
    OperadorNombre=@OperadorPrincipalNombre,
    OperadorAuxiliarID=@OperadorAuxiliarID,
    OperadorAuxiliarNombre=@OperadorAuxiliarNombre,
    OperadoresModificadosManual=1,
    MotivoCambioOperadores=@Motivo,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=GETDATE(),
    Observaciones=CASE
        WHEN Observaciones IS NULL OR LTRIM(RTRIM(Observaciones))=N'' THEN @Observacion
        ELSE LEFT(Observaciones+CHAR(13)+CHAR(10)+@Observacion,500)
    END
WHERE EjecucionProduccionID=@EjecucionProduccionID
  AND ProgramaProduccionID=@ProgramaProduccionID
  AND Activo=1
  AND EstatusID=@EstatusID;

IF @@ROWCOUNT<>1
    THROW 51691,'La ejecucion LH/RH pareja cambio mientras se actualizaba el operador.',1;";

        await using (var cmd = new SqlCommand(sqlActualizarPareja, cn, tx))
        {
            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = pareja.EjecucionParejaID.Value;
            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = pareja.ProgramaParejaID;
            cmd.Parameters.Add("@EstatusID", SqlDbType.Int).Value = estatusPareja;
            cmd.Parameters.Add("@OperadorPrincipalID", SqlDbType.Int).Value = (object?)principalNuevoId ?? DBNull.Value;
            cmd.Parameters.Add("@OperadorPrincipalNombre", SqlDbType.NVarChar, 200).Value = string.IsNullOrWhiteSpace(principalNuevoNombre) ? DBNull.Value : principalNuevoNombre.Trim();
            cmd.Parameters.Add("@OperadorAuxiliarID", SqlDbType.Int).Value = (object?)auxiliarNuevoId ?? DBNull.Value;
            cmd.Parameters.Add("@OperadorAuxiliarNombre", SqlDbType.NVarChar, 200).Value = string.IsNullOrWhiteSpace(auxiliarNuevoNombre) ? DBNull.Value : auxiliarNuevoNombre.Trim();
            cmd.Parameters.Add("@Motivo", SqlDbType.NVarChar, 1000).Value = motivoCambio;
            cmd.Parameters.Add("@Observacion", SqlDbType.NVarChar, 1000).Value = observacionPareja;
            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
            await cmd.ExecuteNonQueryAsync();
        }

        await SincronizarOperadorProgramaAsync(
            pareja.ProgramaParejaID,
            personaNuevaId,
            tipoCambio,
            usuarioId,
            cn,
            tx);

        const string sqlCalidadPareja = @"
DECLARE @InspeccionID INT;
DECLARE @EstadoActual NVARCHAR(50);

SELECT TOP (1)
    @InspeccionID=i.InspeccionID,
    @EstadoActual=UPPER(LTRIM(RTRIM(ISNULL(i.Estado,N''))))
FROM dbo.Calidad_Inspecciones i WITH(UPDLOCK,HOLDLOCK)
WHERE i.EjecucionProduccionID=@EjecucionProduccionID
  AND ISNULL(i.Estado,N'')<>N'CERRADA'
ORDER BY i.InspeccionID DESC;

IF @InspeccionID IS NOT NULL
BEGIN
    UPDATE dbo.Calidad_Inspecciones
    SET OperadorPrincipalPersonaID=@OperadorPrincipalID,
        OperadorPrincipalNombre=@OperadorPrincipalNombre,
        OperadorAuxiliarPersonaID=@OperadorAuxiliarID,
        OperadorAuxiliarNombre=@OperadorAuxiliarNombre,
        UsuarioModificacionID=@UsuarioID,
        FechaModificacion=GETDATE()
    WHERE InspeccionID=@InspeccionID;

    INSERT INTO dbo.Calidad_InspeccionHistorial
    (
        InspeccionID,Movimiento,EstadoAnterior,EstadoNuevo,
        ResultadoCalidad,Etiqueta,Comentario,UsuarioID,FechaMovimiento
    )
    VALUES
    (
        @InspeccionID,N'OPERADORES_ACTUALIZADOS_PRODUCCION_LHRH',
        @EstadoActual,@EstadoActual,N'ACTUALIZADO',NULL,
        @Comentario,@UsuarioID,GETDATE()
    );
END;";

        await using (var cmd = new SqlCommand(sqlCalidadPareja, cn, tx))
        {
            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = pareja.EjecucionParejaID.Value;
            cmd.Parameters.Add("@OperadorPrincipalID", SqlDbType.Int).Value = (object?)principalNuevoId ?? DBNull.Value;
            cmd.Parameters.Add("@OperadorPrincipalNombre", SqlDbType.NVarChar, 200).Value = string.IsNullOrWhiteSpace(principalNuevoNombre) ? DBNull.Value : principalNuevoNombre.Trim();
            cmd.Parameters.Add("@OperadorAuxiliarID", SqlDbType.Int).Value = (object?)auxiliarNuevoId ?? DBNull.Value;
            cmd.Parameters.Add("@OperadorAuxiliarNombre", SqlDbType.NVarChar, 200).Value = string.IsNullOrWhiteSpace(auxiliarNuevoNombre) ? DBNull.Value : auxiliarNuevoNombre.Trim();
            cmd.Parameters.Add("@Comentario", SqlDbType.NVarChar, 1000).Value = observacionPareja;
            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
            await cmd.ExecuteNonQueryAsync();
        }

        return true;
    }
}