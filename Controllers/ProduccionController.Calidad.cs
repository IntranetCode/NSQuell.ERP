using ERP.NSQuell.Models;
using Microsoft.Data.SqlClient;
using System.Data;

namespace ERP.NSQuell.Controllers
{
    public sealed partial class ProduccionController
    {
        private sealed class OrigenSolicitudCalidad
        {
            public int EjecucionProduccionID { get; set; }
            public int ProgramaProduccionID { get; set; }
            public int ChecklistArranqueID { get; set; }
            public int? SolicitudProduccionID { get; set; }
            public int? SolicitudProduccionDetalleID { get; set; }
            public int? ReleaseID { get; set; }
            public int? ReleaseDetalleID { get; set; }
            public string? NumeroOF { get; set; }
            public int? ClienteID { get; set; }
            public string? ClienteNombre { get; set; }
            public int? ParteID { get; set; }
            public string? NumeroParte { get; set; }
            public string? ReferenciaSAP { get; set; }
            public int? MaquinaID { get; set; }
            public string? MaquinaCodigo { get; set; }
            public string? MaquinaNombre { get; set; }
            public int? MoldeID { get; set; }
            public string? MoldeCodigo { get; set; }
            public int? MaterialID { get; set; }
            public string? MaterialCodigo { get; set; }
            public string? MaterialDescripcion { get; set; }
            public int CantidadPlaneada { get; set; }
            public DateTime? FechaInicioProgramada { get; set; }
            public DateTime? FechaFinProgramada { get; set; }
            public int? OperadorPrincipalPersonaID { get; set; }
            public string? OperadorPrincipalNombre { get; set; }
            public int? OperadorAuxiliarPersonaID { get; set; }
            public string? OperadorAuxiliarNombre { get; set; }
        }


        private async Task<OrigenSolicitudCalidad?> ObtenerOrigenSolicitudCalidadAsync(
      int checklistArranqueId,
      int ejecucionProduccionId,
      SqlConnection cn,
      SqlTransaction tx)
        {
            const string sql = @"
SELECT TOP (1)
    e.EjecucionProduccionID,
    e.ProgramaProduccionID,
    c.ChecklistArranqueID,
    COALESCE(e.SolicitudProduccionID,pp.SolicitudProduccionID) AS SolicitudProduccionID,
    COALESCE(e.SolicitudProduccionDetalleID,pp.SolicitudProduccionDetalleID) AS SolicitudProduccionDetalleID,
    COALESCE(e.ReleaseID,pp.ReleaseID,rd.ReleaseID) AS ReleaseID,
    COALESCE(e.ReleaseDetalleID,pp.ReleaseDetalleID) AS ReleaseDetalleID,
    COALESCE(NULLIF(s.NumeroOFRecibida,N''),NULLIF(s.FolioSolicitud,N'')) AS NumeroOF,
    pp.ClienteID,
    pp.ClienteNombre,
    e.ParteID,
    e.NumeroParte,
    e.ReferenciaSAP,
    e.MaquinaID,
    e.MaquinaCodigo,
    e.MaquinaNombre,
    e.MoldeID,
    e.MoldeCodigo,
    COALESCE(pp.MaterialID,dt.MaterialID) AS MaterialID,
    COALESCE(NULLIF(pp.MaterialCodigo,N''),dt.MaterialCodigo) AS MaterialCodigo,
    COALESCE(NULLIF(pp.MaterialDescripcion,N''),dt.MaterialDescripcion) AS MaterialDescripcion,
    CONVERT(INT,ISNULL(e.CantidadPlaneada,pp.CantidadProgramada)) AS CantidadPlaneada,
    pp.FechaInicioProgramada,
    pp.FechaFinProgramada,
    COALESCE(e.OperadorID,opPrincipal.PersonaID) AS OperadorPrincipalPersonaID,
    COALESCE(NULLIF(e.OperadorNombre,N''),NULLIF(opPrincipal.NombreCompleto,N'')) AS OperadorPrincipalNombre,
    COALESCE(e.OperadorAuxiliarID,opAuxiliar.PersonaID) AS OperadorAuxiliarPersonaID,
    COALESCE(NULLIF(e.OperadorAuxiliarNombre,N''),NULLIF(opAuxiliar.NombreCompleto,N'')) AS OperadorAuxiliarNombre
FROM dbo.Produccion_Ejecucion e
INNER JOIN dbo.Produccion_ChecklistArranque c
    ON c.EjecucionProduccionID=e.EjecucionProduccionID
   AND c.ChecklistArranqueID=@ChecklistArranqueID
   AND c.Activo=1
INNER JOIN dbo.Planeacion_ProgramaProduccion pp
    ON pp.ProgramaProduccionID=e.ProgramaProduccionID
   AND pp.Activo=1
LEFT JOIN dbo.Planeacion_ReleaseDetalle rd
    ON rd.ReleaseDetalleID=COALESCE(e.ReleaseDetalleID,pp.ReleaseDetalleID)
LEFT JOIN dbo.SolicitudesProduccion s
    ON s.SolicitudProduccionID=
       COALESCE(e.SolicitudProduccionID,pp.SolicitudProduccionID)
   AND s.Activo=1
LEFT JOIN dbo.ERP_ParteDatosTecnicos dt
    ON dt.ParteID=e.ParteID
   AND dt.Activo=1
OUTER APPLY
(
    SELECT TOP (1)
        po.PersonaID,
        LTRIM(RTRIM(
            ISNULL(p.Nombre,N'')+N' '+
            ISNULL(p.ApellidoPaterno,N'')+N' '+
            ISNULL(p.ApellidoMaterno,N'')
        )) AS NombreCompleto
    FROM dbo.Planeacion_ProgramaOperadores po
    LEFT JOIN dbo.Persona p
        ON p.PersonaID=po.PersonaID
    WHERE po.ProgramaProduccionID=e.ProgramaProduccionID
      AND po.Activo=1
      AND UPPER(LTRIM(RTRIM(ISNULL(po.RolOperador,N''))))=N'PRINCIPAL'
    ORDER BY po.ProgramaOperadorID
) opPrincipal
OUTER APPLY
(
    SELECT TOP (1)
        po.PersonaID,
        LTRIM(RTRIM(
            ISNULL(p.Nombre,N'')+N' '+
            ISNULL(p.ApellidoPaterno,N'')+N' '+
            ISNULL(p.ApellidoMaterno,N'')
        )) AS NombreCompleto
    FROM dbo.Planeacion_ProgramaOperadores po
    LEFT JOIN dbo.Persona p
        ON p.PersonaID=po.PersonaID
    WHERE po.ProgramaProduccionID=e.ProgramaProduccionID
      AND po.Activo=1
      AND UPPER(LTRIM(RTRIM(ISNULL(po.RolOperador,N''))))=N'AUXILIAR'
    ORDER BY po.ProgramaOperadorID
) opAuxiliar
WHERE e.EjecucionProduccionID=@EjecucionProduccionID
  AND e.Activo=1;";
            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@ChecklistArranqueID", SqlDbType.Int).Value = checklistArranqueId;
            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
            await using var rd = await cmd.ExecuteReaderAsync();
            if (!await rd.ReadAsync())
                return null;
            var solicitudProduccionId =
                rd["SolicitudProduccionID"] == DBNull.Value
                    ? (int?)null
                    : Convert.ToInt32(rd["SolicitudProduccionID"]);
            var solicitudProduccionDetalleId =
                rd["SolicitudProduccionDetalleID"] == DBNull.Value
                    ? (int?)null
                    : Convert.ToInt32(rd["SolicitudProduccionDetalleID"]);
            if (!solicitudProduccionId.HasValue ||
               solicitudProduccionId.Value <= 0)
            {
                throw new InvalidOperationException(
                    "No existe una OF relacionada con esta ejecución. Genera la OF desde Planeación antes de enviarla a Calidad.");
            }
            if (!solicitudProduccionDetalleId.HasValue ||
               solicitudProduccionDetalleId.Value <= 0)
            {
                throw new InvalidOperationException(
                    "La OF relacionada no tiene un detalle válido asociado al programa.");
            }
            return new OrigenSolicitudCalidad
            {
                EjecucionProduccionID = Convert.ToInt32(rd["EjecucionProduccionID"]),
                ProgramaProduccionID = Convert.ToInt32(rd["ProgramaProduccionID"]),
                ChecklistArranqueID = Convert.ToInt32(rd["ChecklistArranqueID"]),
                SolicitudProduccionID = solicitudProduccionId,
                SolicitudProduccionDetalleID = solicitudProduccionDetalleId,
                ReleaseID = rd["ReleaseID"] == DBNull.Value ? null : Convert.ToInt32(rd["ReleaseID"]),
                ReleaseDetalleID = rd["ReleaseDetalleID"] == DBNull.Value ? null : Convert.ToInt32(rd["ReleaseDetalleID"]),
                NumeroOF = rd["NumeroOF"] as string,
                ClienteID = rd["ClienteID"] == DBNull.Value ? null : Convert.ToInt32(rd["ClienteID"]),
                ClienteNombre = rd["ClienteNombre"] as string,
                ParteID = rd["ParteID"] == DBNull.Value ? null : Convert.ToInt32(rd["ParteID"]),
                NumeroParte = rd["NumeroParte"] as string,
                ReferenciaSAP = rd["ReferenciaSAP"] as string,
                MaquinaID = rd["MaquinaID"] == DBNull.Value ? null : Convert.ToInt32(rd["MaquinaID"]),
                MaquinaCodigo = rd["MaquinaCodigo"] as string,
                MaquinaNombre = rd["MaquinaNombre"] as string,
                MoldeID = rd["MoldeID"] == DBNull.Value ? null : Convert.ToInt32(rd["MoldeID"]),
                MoldeCodigo = rd["MoldeCodigo"] as string,
                MaterialID = rd["MaterialID"] == DBNull.Value ? null : Convert.ToInt32(rd["MaterialID"]),
                MaterialCodigo = rd["MaterialCodigo"] as string,
                MaterialDescripcion = rd["MaterialDescripcion"] as string,
                CantidadPlaneada = rd["CantidadPlaneada"] == DBNull.Value ? 0 : Convert.ToInt32(rd["CantidadPlaneada"]),
                FechaInicioProgramada = rd["FechaInicioProgramada"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaInicioProgramada"]),
                FechaFinProgramada = rd["FechaFinProgramada"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaFinProgramada"]),
                OperadorPrincipalPersonaID = rd["OperadorPrincipalPersonaID"] == DBNull.Value ? null : Convert.ToInt32(rd["OperadorPrincipalPersonaID"]),
                OperadorPrincipalNombre = rd["OperadorPrincipalNombre"] as string,
                OperadorAuxiliarPersonaID = rd["OperadorAuxiliarPersonaID"] == DBNull.Value ? null : Convert.ToInt32(rd["OperadorAuxiliarPersonaID"]),
                OperadorAuxiliarNombre = rd["OperadorAuxiliarNombre"] as string
            };
        }

        private static void AgregarParametrosOrigenCalidad(
            SqlCommand cmd,
            OrigenSolicitudCalidad origen,
            int usuarioId)
        {
            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = origen.ProgramaProduccionID;
            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = origen.EjecucionProduccionID;
            cmd.Parameters.Add("@ChecklistArranqueID", SqlDbType.Int).Value = origen.ChecklistArranqueID;
            cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = (object?)origen.SolicitudProduccionID ?? DBNull.Value;
            cmd.Parameters.Add("@SolicitudProduccionDetalleID", SqlDbType.Int).Value = (object?)origen.SolicitudProduccionDetalleID ?? DBNull.Value;
            cmd.Parameters.Add("@ReleaseID", SqlDbType.Int).Value = (object?)origen.ReleaseID ?? DBNull.Value;
            cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value = (object?)origen.ReleaseDetalleID ?? DBNull.Value;
            cmd.Parameters.Add("@ClienteID", SqlDbType.Int).Value = (object?)origen.ClienteID ?? DBNull.Value;
            cmd.Parameters.Add("@ClienteNombre", SqlDbType.NVarChar, 200).Value = (object?)origen.ClienteNombre ?? DBNull.Value;
            cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value = (object?)origen.ParteID ?? DBNull.Value;
            cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = (object?)origen.MaquinaID ?? DBNull.Value;
            cmd.Parameters.Add("@MoldeID", SqlDbType.Int).Value = (object?)origen.MoldeID ?? DBNull.Value;
            cmd.Parameters.Add("@MaterialID", SqlDbType.Int).Value = (object?)origen.MaterialID ?? DBNull.Value;
            cmd.Parameters.Add("@OrdenTrabajo", SqlDbType.NVarChar, 120).Value = (object?)origen.NumeroOF ?? DBNull.Value;
            cmd.Parameters.Add("@NumeroParte", SqlDbType.NVarChar, 120).Value =
                (object?)(string.IsNullOrWhiteSpace(origen.ReferenciaSAP) ? origen.NumeroParte : origen.ReferenciaSAP) ?? DBNull.Value;
            cmd.Parameters.Add("@Material", SqlDbType.NVarChar, 250).Value =
                (object?)UnirTexto(origen.MaterialCodigo, origen.MaterialDescripcion) ?? DBNull.Value;
            cmd.Parameters.Add("@Maquina", SqlDbType.NVarChar, 150).Value =
                (object?)UnirTexto(origen.MaquinaCodigo, origen.MaquinaNombre) ?? DBNull.Value;
            cmd.Parameters.Add("@Molde", SqlDbType.NVarChar, 150).Value = (object?)origen.MoldeCodigo ?? DBNull.Value;
            cmd.Parameters.Add("@FechaInicioProgramada", SqlDbType.DateTime2).Value = (object?)origen.FechaInicioProgramada ?? DBNull.Value;
            cmd.Parameters.Add("@FechaFinProgramada", SqlDbType.DateTime2).Value = (object?)origen.FechaFinProgramada ?? DBNull.Value;
            cmd.Parameters.Add("@OperadorPrincipalPersonaID", SqlDbType.Int).Value = (object?)origen.OperadorPrincipalPersonaID ?? DBNull.Value;
            cmd.Parameters.Add("@OperadorPrincipalNombre", SqlDbType.NVarChar, 250).Value = (object?)origen.OperadorPrincipalNombre ?? DBNull.Value;
            cmd.Parameters.Add("@OperadorAuxiliarPersonaID", SqlDbType.Int).Value = (object?)origen.OperadorAuxiliarPersonaID ?? DBNull.Value;
            cmd.Parameters.Add("@OperadorAuxiliarNombre", SqlDbType.NVarChar, 250).Value = (object?)origen.OperadorAuxiliarNombre ?? DBNull.Value;
            cmd.Parameters.Add("@CantidadTotal", SqlDbType.Decimal).Value = origen.CantidadPlaneada;
            cmd.Parameters["@CantidadTotal"].Precision = 18;
            cmd.Parameters["@CantidadTotal"].Scale = 3;
            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
        }

        private static string? UnirTexto(string? codigo, string? descripcion)
        {
            codigo = codigo?.Trim();
            descripcion = descripcion?.Trim();

            if (string.IsNullOrWhiteSpace(codigo)) return descripcion;
            if (string.IsNullOrWhiteSpace(descripcion)) return codigo;
            if (string.Equals(codigo, descripcion, StringComparison.OrdinalIgnoreCase)) return codigo;

            return codigo + " | " + descripcion;
        }

        private async Task<ProduccionCalidadResumenVm?> ObtenerResumenCalidadAsync(
            int ejecucionProduccionId,
            SqlConnection cn)
        {
            const string sql = @"
SELECT TOP (1)
    ci.InspeccionID,
    ci.EjecucionProduccionID,
    ci.ChecklistArranqueID,
    ci.Estado,
    ci.ResultadoCalidad,
    ci.Etiqueta,
    ci.MotivoDevolucion,
    ci.FechaNotificacionCalidad,
    ci.FechaAutorizacionPrearranque,
    ci.FechaLiberacionProduccion,
    ci.ConfiguracionInvalidada,
    ci.RequiereReliberacion,
    ci.Liberado,

    ISNULL(mon.TotalMonitoreos, 0) AS TotalMonitoreos,
    ISNULL(mon.MonitoreosPendientes, 0) AS MonitoreosPendientes,
    ISNULL(mon.MonitoreosVencidos, 0) AS MonitoreosVencidos,
    ISNULL(mon.MonitoreosConformes, 0) AS MonitoreosConformes,
    ISNULL(mon.MonitoreosConHallazgo, 0) AS MonitoreosConHallazgo,
    mon.ProximoMonitoreo,
    ISNULL(disp.DisposicionesPendientes, 0) AS DisposicionesPendientes
FROM dbo.Calidad_Inspecciones ci
OUTER APPLY
(
    SELECT
        COUNT(1) AS TotalMonitoreos,
        SUM(CASE WHEN m.Resultado = 'PENDIENTE' THEN 1 ELSE 0 END) AS MonitoreosPendientes,
        SUM(CASE WHEN m.Resultado = 'PENDIENTE' AND m.FechaHoraProgramada < GETDATE() THEN 1 ELSE 0 END) AS MonitoreosVencidos,
        SUM(CASE WHEN m.Resultado = 'CONFORME' THEN 1 ELSE 0 END) AS MonitoreosConformes,
        SUM(CASE WHEN m.Resultado IN ('SOSPECHOSO', 'NO_CONFORME') THEN 1 ELSE 0 END) AS MonitoreosConHallazgo,
        MIN(CASE WHEN m.Resultado = 'PENDIENTE' THEN m.FechaHoraProgramada END) AS ProximoMonitoreo
    FROM dbo.Calidad_MonitoreosProceso m
    WHERE m.InspeccionID = ci.InspeccionID
      AND m.Activo = 1
) mon
OUTER APPLY
(
    SELECT COUNT(1) AS DisposicionesPendientes
    FROM dbo.Calidad_DisposicionesMaterial d
    WHERE d.InspeccionID = ci.InspeccionID
      AND d.Activo = 1
      AND d.ResultadoFinal = 'PENDIENTE'
) disp
WHERE ci.EjecucionProduccionID = @EjecucionProduccionID
ORDER BY ci.InspeccionID DESC;";

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;

            await using var rd = await cmd.ExecuteReaderAsync();
            if (!await rd.ReadAsync())
                return null;

            return new ProduccionCalidadResumenVm
            {
                InspeccionID = Convert.ToInt32(rd["InspeccionID"]),
                EjecucionProduccionID = Convert.ToInt32(rd["EjecucionProduccionID"]),
                ChecklistArranqueID = rd["ChecklistArranqueID"] == DBNull.Value
                    ? 0
                    : Convert.ToInt32(rd["ChecklistArranqueID"]),
                Estado = rd["Estado"] as string ?? string.Empty,
                ResultadoCalidad = rd["ResultadoCalidad"] as string,
                Etiqueta = rd["Etiqueta"] as string,
                MotivoDevolucion = rd["MotivoDevolucion"] as string,
                FechaNotificacionCalidad = rd["FechaNotificacionCalidad"] == DBNull.Value
                    ? null
                    : Convert.ToDateTime(rd["FechaNotificacionCalidad"]),
                FechaAutorizacionPrearranque = rd["FechaAutorizacionPrearranque"] == DBNull.Value
                    ? null
                    : Convert.ToDateTime(rd["FechaAutorizacionPrearranque"]),
                FechaLiberacionProduccion = rd["FechaLiberacionProduccion"] == DBNull.Value
                    ? null
                    : Convert.ToDateTime(rd["FechaLiberacionProduccion"]),
                ConfiguracionInvalidada = rd["ConfiguracionInvalidada"] != DBNull.Value &&
                    Convert.ToBoolean(rd["ConfiguracionInvalidada"]),
                RequiereReliberacion = rd["RequiereReliberacion"] != DBNull.Value &&
                    Convert.ToBoolean(rd["RequiereReliberacion"]),
                Liberado = rd["Liberado"] != DBNull.Value &&
                    Convert.ToBoolean(rd["Liberado"]),
                TotalMonitoreos = Convert.ToInt32(rd["TotalMonitoreos"]),
                MonitoreosPendientes = Convert.ToInt32(rd["MonitoreosPendientes"]),
                MonitoreosVencidos = Convert.ToInt32(rd["MonitoreosVencidos"]),
                MonitoreosConformes = Convert.ToInt32(rd["MonitoreosConformes"]),
                MonitoreosConHallazgo = Convert.ToInt32(rd["MonitoreosConHallazgo"]),
                DisposicionesPendientes = Convert.ToInt32(rd["DisposicionesPendientes"]),
                ProximoMonitoreo = rd["ProximoMonitoreo"] == DBNull.Value
                    ? null
                    : Convert.ToDateTime(rd["ProximoMonitoreo"])
            };
        }

        private async Task CargarEstadoCalidadChecklistAsync(
            ProduccionChecklistArranqueVm checklist,
            SqlConnection cn)
        {
            var resumen = await ObtenerResumenCalidadAsync(checklist.EjecucionProduccionID, cn);
            if (resumen == null) return;

            checklist.CalidadInspeccionID = resumen.InspeccionID;
            checklist.CalidadEstado = resumen.Estado;
            checklist.CalidadMotivoDevolucion = resumen.MotivoDevolucion;
            checklist.FechaNotificacionCalidad = resumen.FechaNotificacionCalidad;
            checklist.FechaLiberacionCalidad = resumen.FechaLiberacionProduccion;
        }

        private async Task MarcarCalidadEnMonitoreoAsync(
     int ejecucionProduccionId,
     int usuarioId,
     SqlConnection cn,
     SqlTransaction tx)
        {
            const string sql = @"
DECLARE @InspeccionID INT;
DECLARE @Estado NVARCHAR(50);
DECLARE @Liberado BIT;
DECLARE @RequiereReliberacion BIT;
DECLARE @ConfiguracionInvalidada BIT;
DECLARE @FechaInicioProgramada DATETIME2(0);
DECLARE @FechaFinProgramada DATETIME2(0);
DECLARE @Ahora DATETIME2(0)=SYSDATETIME();
DECLARE @NumeroHoraInicial INT;
DECLARE @CantidadHoras INT;
DECLARE @EsReinicio BIT=0;

SELECT TOP (1)
    @InspeccionID=ci.InspeccionID,
    @Estado=UPPER(LTRIM(RTRIM(ISNULL(ci.Estado,N'')))),
    @Liberado=ISNULL(ci.Liberado,0),
    @RequiereReliberacion=ISNULL(ci.RequiereReliberacion,0),
    @ConfiguracionInvalidada=ISNULL(ci.ConfiguracionInvalidada,0),
    @FechaInicioProgramada=ci.FechaInicioProgramada,
    @FechaFinProgramada=ci.FechaFinProgramada
FROM dbo.Calidad_Inspecciones ci WITH (UPDLOCK,HOLDLOCK)
WHERE ci.EjecucionProduccionID=@EjecucionProduccionID
  AND ci.Estado<>N'CERRADA'
ORDER BY ci.InspeccionID DESC;

IF @InspeccionID IS NULL
BEGIN
    THROW 51040,
        'No existe una inspeccion activa de Calidad relacionada con la ejecucion.',
        1;
END;

IF @ConfiguracionInvalidada=1
BEGIN
    THROW 51041,
        'La configuracion de Calidad fue invalidada y no permite iniciar monitoreo.',
        1;
END;

IF @RequiereReliberacion=1
BEGIN
    THROW 51042,
        'La reliberacion de Calidad continua pendiente.',
        1;
END;

IF ISNULL(@Liberado,0)=0
BEGIN
    THROW 51043,
        'Calidad aun no ha liberado la produccion.',
        1;
END;

IF @Estado NOT IN
(
    N'PRODUCCION_LIBERADA',
    N'MONITOREO_ACTIVO'
)
BEGIN
    THROW 51044,
        'El estado de Calidad no permite iniciar el monitoreo horario.',
        1;
END;

-- Si ya está en monitoreo, la acción fue ejecutada previamente.
-- No se deben crear nuevamente los mismos periodos.
IF @Estado=N'MONITOREO_ACTIVO'
BEGIN
    RETURN;
END;

SELECT
    @NumeroHoraInicial=
        ISNULL(MAX(m.NumeroHora),0)+1
FROM dbo.Calidad_MonitoreosProceso m WITH (UPDLOCK,HOLDLOCK)
WHERE m.InspeccionID=@InspeccionID
  AND m.Activo=1;

IF @NumeroHoraInicial>1
    SET @EsReinicio=1;

-- Generar las horas restantes conforme a la programación.
-- Cuando la fecha programada ya venció, se generan nueve periodos
-- para continuar el seguimiento de una producción atrasada.
SET @CantidadHoras=
    CASE
        WHEN @FechaFinProgramada IS NOT NULL
         AND @FechaFinProgramada>@Ahora
            THEN CONVERT
            (
                INT,
                CEILING
                (
                    DATEDIFF
                    (
                        MINUTE,
                        @Ahora,
                        @FechaFinProgramada
                    )/60.0
                )
            )
        ELSE 9
    END;

IF @CantidadHoras<1
    SET @CantidadHoras=1;

-- Límite de seguridad para evitar una generación accidental excesiva.
IF @CantidadHoras>500
    SET @CantidadHoras=500;

UPDATE dbo.Calidad_Inspecciones
SET
    Estado=N'MONITOREO_ACTIVO',
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=@Ahora
WHERE InspeccionID=@InspeccionID
  AND Estado=N'PRODUCCION_LIBERADA'
  AND ISNULL(Liberado,0)=1
  AND ISNULL(RequiereReliberacion,0)=0
  AND ISNULL(ConfiguracionInvalidada,0)=0;

IF @@ROWCOUNT<>1
BEGIN
    THROW 51045,
        'La inspeccion de Calidad cambio de estado antes de iniciar el monitoreo.',
        1;
END;

INSERT INTO dbo.Calidad_InspeccionHistorial
(
    InspeccionID,
    Movimiento,
    EstadoAnterior,
    EstadoNuevo,
    ResultadoCalidad,
    Etiqueta,
    Comentario,
    UsuarioID,
    FechaMovimiento
)
VALUES
(
    @InspeccionID,
    CASE
        WHEN @EsReinicio=1
            THEN N'REINICIO_PRODUCCION_SERIE'
        ELSE N'INICIO_PRODUCCION_SERIE'
    END,
    N'PRODUCCION_LIBERADA',
    N'MONITOREO_ACTIVO',
    N'VERDE',
    N'VERDE',
    CASE
        WHEN @EsReinicio=1
            THEN
                N'Produccion reinicio la serie despues de una reliberacion autorizada. Se genero un nuevo ciclo de monitoreos horarios.'
        ELSE
            N'Produccion inicio la serie. Se generaron los monitoreos horarios de Calidad.'
    END,
    @UsuarioID,
    @Ahora
);

;WITH Numeros AS
(
    SELECT 0 AS Consecutivo

    UNION ALL

    SELECT Consecutivo+1
    FROM Numeros
    WHERE Consecutivo+1<@CantidadHoras
)
INSERT INTO dbo.Calidad_MonitoreosProceso
(
    InspeccionID,
    EjecucionProduccionID,
    NumeroHora,
    FechaHoraProgramada,
    CantidadProducidaPeriodo,
    CantidadRevisadaMuestra,
    Resultado,
    CantidadSospechosa,
    CantidadNoRecuperable,
    RequiereSeleccion,
    RequiereRetrabajo,
    UsuarioCreacionID,
    FechaCreacion,
    Activo
)
SELECT
    @InspeccionID,
    @EjecucionProduccionID,
    @NumeroHoraInicial+n.Consecutivo,
    DATEADD
    (
        HOUR,
        n.Consecutivo+1,
        @Ahora
    ),
    0,
    0,
    N'PENDIENTE',
    0,
    0,
    0,
    0,
    @UsuarioID,
    @Ahora,
    1
FROM Numeros n
WHERE NOT EXISTS
(
    SELECT 1
    FROM dbo.Calidad_MonitoreosProceso existente
    WHERE existente.InspeccionID=@InspeccionID
      AND existente.NumeroHora=
          @NumeroHoraInicial+n.Consecutivo
      AND existente.Activo=1
)
OPTION (MAXRECURSION 500);";

            await using var cmd =
                new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add(
                "@EjecucionProduccionID",
                SqlDbType.Int).Value =
                ejecucionProduccionId;

            cmd.Parameters.Add(
                "@UsuarioID",
                SqlDbType.Int).Value =
                usuarioId;

            await cmd.ExecuteNonQueryAsync();
        }
        private static async Task RegistrarHistorialEnvioCalidadAsync(
    int inspeccionId,
    string? estadoAnterior,
    string estadoNuevo,
    string comentario,
    int usuarioId,
    SqlConnection cn,
    SqlTransaction tx)
        {
            const string sql = @"
INSERT INTO dbo.Calidad_InspeccionHistorial
(
    InspeccionID,
    Movimiento,
    EstadoAnterior,
    EstadoNuevo,
    ResultadoCalidad,
    Etiqueta,
    Comentario,
    UsuarioID,
    FechaMovimiento
)
VALUES
(
    @InspeccionID,
    N'RECIBIDO_DESDE_PRODUCCION',
    @EstadoAnterior,
    @EstadoNuevo,
    NULL,
    NULL,
    @Comentario,
    @UsuarioID,
    GETDATE()
);";

            await using var cmd =
                new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add(
                "@InspeccionID",
                SqlDbType.Int).Value =
                inspeccionId;

            cmd.Parameters.Add(
                "@EstadoAnterior",
                SqlDbType.NVarChar,
                50).Value =
                string.IsNullOrWhiteSpace(estadoAnterior)
                    ? DBNull.Value
                    : estadoAnterior.Trim();

            cmd.Parameters.Add(
                "@EstadoNuevo",
                SqlDbType.NVarChar,
                50).Value =
                estadoNuevo;

            cmd.Parameters.Add(
                "@Comentario",
                SqlDbType.NVarChar,
                1000).Value =
                comentario;

            cmd.Parameters.Add(
                "@UsuarioID",
                SqlDbType.Int).Value =
                usuarioId;

            await cmd.ExecuteNonQueryAsync();
        }

        private async Task VincularRegistroHoraConMonitoreoAsync(ProduccionEjecucionVm ejecucion, ProduccionRegistroHoraPostVm vm, TimeSpan horaInicio, TimeSpan horaFin, int registroHoraId, int usuarioId, SqlConnection cn, SqlTransaction tx)
        {
            if (ejecucion == null) throw new ArgumentNullException(nameof(ejecucion), "No se recibió la ejecución de producción.");
            if (vm == null) throw new ArgumentNullException(nameof(vm), "No se recibió la captura horaria.");
            if (ejecucion.EjecucionProduccionID <= 0) throw new InvalidOperationException("La ejecución de producción no es válida.");
            if (registroHoraId <= 0) throw new InvalidOperationException("El registro horario no es válido.");
            if (usuarioId <= 0) throw new InvalidOperationException("No se pudo identificar al usuario que registró la producción.");

            var fechaHoraInicio = vm.FechaProduccion.Date.Add(horaInicio);
            var fechaHoraFin = vm.FechaProduccion.Date.Add(horaFin);
            if (fechaHoraFin <= fechaHoraInicio) fechaHoraFin = fechaHoraFin.AddDays(1);

            var cantidadPeriodo = vm.CantidadOK + vm.CantidadSospechosa + vm.CantidadScrap;
            var cantidadPendienteRevision = vm.CantidadSospechosa + vm.CantidadScrap;
            var observacionesProduccion = $"RegistroHoraID: {registroHoraId}. OK: {vm.CantidadOK}; sospechoso: {vm.CantidadSospechosa}; scrap reportado: {vm.CantidadScrap}.";
            if (!string.IsNullOrWhiteSpace(vm.Observaciones)) observacionesProduccion += " Observaciones: " + vm.Observaciones.Trim();
            if (observacionesProduccion.Length > 1000) observacionesProduccion = observacionesProduccion[..1000];

            const string sql = @"
DECLARE @InspeccionID INT;
DECLARE @EstadoInspeccion NVARCHAR(50);
DECLARE @MonitoreoID INT;
DECLARE @RegistroHoraVinculado INT;
DECLARE @DisposicionID INT;
DECLARE @Comentario NVARCHAR(1000);

SELECT TOP (1)
    @InspeccionID=ci.InspeccionID,
    @EstadoInspeccion=UPPER(LTRIM(RTRIM(ISNULL(ci.Estado,N''))))
FROM dbo.Calidad_Inspecciones ci WITH (UPDLOCK,HOLDLOCK)
WHERE ci.EjecucionProduccionID=@EjecucionProduccionID
  AND ISNULL(ci.Estado,N'')<>N'CERRADA'
ORDER BY ci.InspeccionID DESC;

IF @InspeccionID IS NULL
    THROW 51050,'No existe una inspección activa de Calidad para la ejecución.',1;

IF @EstadoInspeccion<>N'MONITOREO_ACTIVO'
    THROW 51051,'La inspección de Calidad no se encuentra en monitoreo activo.',1;

SELECT TOP (1)
    @MonitoreoID=m.MonitoreoID,
    @RegistroHoraVinculado=m.RegistroHoraID
FROM dbo.Calidad_MonitoreosProceso m WITH (UPDLOCK,HOLDLOCK)
WHERE m.InspeccionID=@InspeccionID
  AND m.EjecucionProduccionID=@EjecucionProduccionID
  AND m.RegistroHoraID=@RegistroHoraID
  AND m.Activo=1
ORDER BY m.MonitoreoID DESC;

IF @MonitoreoID IS NULL
BEGIN
    SELECT TOP (1)
        @MonitoreoID=m.MonitoreoID,
        @RegistroHoraVinculado=m.RegistroHoraID
    FROM dbo.Calidad_MonitoreosProceso m WITH (UPDLOCK,HOLDLOCK)
    WHERE m.InspeccionID=@InspeccionID
      AND m.EjecucionProduccionID=@EjecucionProduccionID
      AND m.Activo=1
      AND m.RegistroHoraID IS NULL
      AND UPPER(LTRIM(RTRIM(ISNULL(m.Resultado,N''))))=N'PENDIENTE'
    ORDER BY m.NumeroHora,m.FechaHoraProgramada,m.MonitoreoID;
END;

IF @MonitoreoID IS NULL
    THROW 51052,'No existe un monitoreo horario pendiente para vincular la captura de Producción.',1;

IF @RegistroHoraVinculado IS NOT NULL AND @RegistroHoraVinculado<>@RegistroHoraID
    THROW 51053,'El monitoreo seleccionado ya está vinculado con otra captura horaria.',1;

IF EXISTS
(
    SELECT 1
    FROM dbo.Calidad_MonitoreosProceso m WITH (UPDLOCK,HOLDLOCK)
    WHERE m.RegistroHoraID=@RegistroHoraID
      AND m.MonitoreoID<>@MonitoreoID
      AND m.Activo=1
)
    THROW 51054,'La captura horaria ya está vinculada con otro monitoreo de Calidad.',1;

UPDATE dbo.Calidad_MonitoreosProceso
SET RegistroHoraID=@RegistroHoraID,
    CantidadProducidaPeriodo=@CantidadProducidaPeriodo,
    CantidadSospechosa=@CantidadSospechosa,
    CantidadNoRecuperable=@CantidadScrap,
    Observaciones=CASE
        WHEN @ObservacionesProduccion IS NULL THEN Observaciones
        WHEN Observaciones IS NULL OR LTRIM(RTRIM(Observaciones))=N'' THEN @ObservacionesProduccion
        WHEN Observaciones LIKE N'%RegistroHoraID: '+CONVERT(NVARCHAR(20),@RegistroHoraID)+N'%' THEN Observaciones
        ELSE Observaciones+CHAR(13)+CHAR(10)+@ObservacionesProduccion
    END,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=SYSDATETIME()
WHERE MonitoreoID=@MonitoreoID
  AND InspeccionID=@InspeccionID
  AND EjecucionProduccionID=@EjecucionProduccionID
  AND Activo=1
  AND (RegistroHoraID IS NULL OR RegistroHoraID=@RegistroHoraID);

IF @@ROWCOUNT<>1
    THROW 51055,'El monitoreo cambió de estado mientras se vinculaba la captura horaria.',1;

IF @CantidadPendienteRevision>0
BEGIN
    SELECT TOP (1) @DisposicionID=d.DisposicionID
    FROM dbo.Calidad_DisposicionesMaterial d WITH (UPDLOCK,HOLDLOCK)
    WHERE d.InspeccionID=@InspeccionID
      AND d.MonitoreoID=@MonitoreoID
      AND d.Activo=1
      AND UPPER(LTRIM(RTRIM(ISNULL(d.ResultadoFinal,N''))))=N'PENDIENTE'
    ORDER BY d.DisposicionID DESC;

    SET @Comentario=CONCAT(
        N'Seguimiento automático generado desde Producción. RegistroHoraID: ',
        @RegistroHoraID,
        N'. Periodo: ',
        CONVERT(NVARCHAR(19),@FechaHoraInicio,120),
        N' a ',
        CONVERT(NVARCHAR(19),@FechaHoraFin,120),
        N'. Producción reportó ',
        @CantidadSospechosa,
        N' pieza(s) sospechosa(s) y ',
        @CantidadScrap,
        N' pieza(s) como scrap operativo. Calidad debe confirmar la disposición final.'
    );

    IF @DisposicionID IS NULL
    BEGIN
        INSERT INTO dbo.Calidad_DisposicionesMaterial
        (
            InspeccionID,
            MonitoreoID,
            TipoMaterial,
            CantidadAfectada,
            Etiqueta,
            Disposicion,
            Responsable,
            FechaInicio,
            ResultadoFinal,
            Observaciones,
            UsuarioCreacionID,
            FechaCreacion,
            Activo
        )
        VALUES
        (
            @InspeccionID,
            @MonitoreoID,
            CASE
                WHEN @CantidadSospechosa>0 AND @CantidadScrap>0 THEN N'SOSPECHOSO_Y_SCRAP'
                WHEN @CantidadScrap>0 THEN N'SCRAP_REPORTADO'
                ELSE N'SOSPECHOSO'
            END,
            @CantidadPendienteRevision,
            N'AMARILLA',
            N'PENDIENTE_REVISION',
            N'CALIDAD',
            SYSDATETIME(),
            N'PENDIENTE',
            @Comentario,
            @UsuarioID,
            SYSDATETIME(),
            1
        );

        INSERT INTO dbo.Calidad_InspeccionHistorial
        (
            InspeccionID,
            Movimiento,
            EstadoAnterior,
            EstadoNuevo,
            ResultadoCalidad,
            Etiqueta,
            Comentario,
            UsuarioID,
            FechaMovimiento
        )
        SELECT
            @InspeccionID,
            N'MATERIAL_REPORTADO_POR_PRODUCCION',
            N'MONITOREO_ACTIVO',
            N'MONITOREO_ACTIVO',
            N'PENDIENTE_REVISION',
            N'AMARILLA',
            @Comentario,
            @UsuarioID,
            SYSDATETIME()
        WHERE NOT EXISTS
        (
            SELECT 1
            FROM dbo.Calidad_InspeccionHistorial h
            WHERE h.InspeccionID=@InspeccionID
              AND h.Movimiento=N'MATERIAL_REPORTADO_POR_PRODUCCION'
              AND h.Comentario LIKE N'%RegistroHoraID: '+CONVERT(NVARCHAR(20),@RegistroHoraID)+N'%'
        );
    END
    ELSE
    BEGIN
        UPDATE dbo.Calidad_DisposicionesMaterial
        SET TipoMaterial=CASE
                WHEN @CantidadSospechosa>0 AND @CantidadScrap>0 THEN N'SOSPECHOSO_Y_SCRAP'
                WHEN @CantidadScrap>0 THEN N'SCRAP_REPORTADO'
                ELSE N'SOSPECHOSO'
            END,
            CantidadAfectada=@CantidadPendienteRevision,
            Etiqueta=N'AMARILLA',
            Disposicion=N'PENDIENTE_REVISION',
            Responsable=N'CALIDAD',
            ResultadoFinal=N'PENDIENTE',
            Observaciones=CASE
                WHEN Observaciones IS NULL OR LTRIM(RTRIM(Observaciones))=N'' THEN @Comentario
                WHEN Observaciones LIKE N'%RegistroHoraID: '+CONVERT(NVARCHAR(20),@RegistroHoraID)+N'%' THEN Observaciones
                ELSE Observaciones+CHAR(13)+CHAR(10)+@Comentario
            END,
            UsuarioModificacionID=@UsuarioID,
            FechaModificacion=SYSDATETIME()
        WHERE DisposicionID=@DisposicionID
          AND Activo=1;
    END;
END;

INSERT INTO dbo.Calidad_InspeccionHistorial
(
    InspeccionID,
    Movimiento,
    EstadoAnterior,
    EstadoNuevo,
    ResultadoCalidad,
    Etiqueta,
    Comentario,
    UsuarioID,
    FechaMovimiento
)
SELECT
    @InspeccionID,
    N'CAPTURA_HORARIA_RECIBIDA',
    N'MONITOREO_ACTIVO',
    N'MONITOREO_ACTIVO',
    CASE WHEN @CantidadPendienteRevision>0 THEN N'PENDIENTE_REVISION' ELSE NULL END,
    CASE WHEN @CantidadPendienteRevision>0 THEN N'AMARILLA' ELSE NULL END,
    CONCAT(
        N'Calidad recibió la captura horaria de Producción. RegistroHoraID: ',
        @RegistroHoraID,
        N'. Periodo: ',
        CONVERT(NVARCHAR(19),@FechaHoraInicio,120),
        N' a ',
        CONVERT(NVARCHAR(19),@FechaHoraFin,120),
        N'. OK: ',
        @CantidadOK,
        N'. Sospechoso: ',
        @CantidadSospechosa,
        N'. Scrap reportado: ',
        @CantidadScrap,
        N'.'
    ),
    @UsuarioID,
    SYSDATETIME()
WHERE NOT EXISTS
(
    SELECT 1
    FROM dbo.Calidad_InspeccionHistorial h
    WHERE h.InspeccionID=@InspeccionID
      AND h.Movimiento=N'CAPTURA_HORARIA_RECIBIDA'
      AND h.Comentario LIKE N'%RegistroHoraID: '+CONVERT(NVARCHAR(20),@RegistroHoraID)+N'%'
);";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucion.EjecucionProduccionID;
            cmd.Parameters.Add("@RegistroHoraID", SqlDbType.Int).Value = registroHoraId;
            cmd.Parameters.Add("@FechaHoraInicio", SqlDbType.DateTime2).Value = fechaHoraInicio;
            cmd.Parameters.Add("@FechaHoraFin", SqlDbType.DateTime2).Value = fechaHoraFin;
            cmd.Parameters.Add("@CantidadProducidaPeriodo", SqlDbType.Int).Value = cantidadPeriodo;
            cmd.Parameters.Add("@CantidadOK", SqlDbType.Int).Value = vm.CantidadOK;
            cmd.Parameters.Add("@CantidadSospechosa", SqlDbType.Int).Value = vm.CantidadSospechosa;
            cmd.Parameters.Add("@CantidadScrap", SqlDbType.Int).Value = vm.CantidadScrap;
            cmd.Parameters.Add("@CantidadPendienteRevision", SqlDbType.Int).Value = cantidadPendienteRevision;
            cmd.Parameters.Add("@ObservacionesProduccion", SqlDbType.NVarChar, 1000).Value = observacionesProduccion;
            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
            await cmd.ExecuteNonQueryAsync();
        }
    }
    }
