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

        private async Task<int> CrearOActualizarSolicitudCalidadAsync(
            int checklistArranqueId,
            int ejecucionProduccionId,
            int usuarioId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            var origen = await ObtenerOrigenSolicitudCalidadAsync(
                checklistArranqueId,
                ejecucionProduccionId,
                cn,
                tx);

            if (origen == null)
                throw new InvalidOperationException("No se encontro la ejecucion o el checklist que se enviara a Calidad.");

            var faltantes = new List<string>();

            if (!origen.SolicitudProduccionID.HasValue) faltantes.Add("Orden de Fabricacion");
            if (!origen.SolicitudProduccionDetalleID.HasValue) faltantes.Add("renglon de la OF");
            if (string.IsNullOrWhiteSpace(origen.NumeroOF)) faltantes.Add("numero de OF");
            if (!origen.ParteID.HasValue) faltantes.Add("parte");
            if (!origen.MaquinaID.HasValue) faltantes.Add("maquina");
            if (!origen.MoldeID.HasValue) faltantes.Add("molde");
            if (!origen.MaterialID.HasValue) faltantes.Add("material");
            if (!origen.OperadorPrincipalPersonaID.HasValue) faltantes.Add("operador principal");
            if (origen.CantidadPlaneada <= 0) faltantes.Add("cantidad programada");

            if (faltantes.Count > 0)
            {
                throw new InvalidOperationException(
                    "La corrida no puede enviarse a Calidad. Faltan: " +
                    string.Join(", ", faltantes) + ".");
            }

            const string sqlExistente = @"
SELECT TOP (1)
    InspeccionID,
    Estado
FROM dbo.Calidad_Inspecciones
WHERE ChecklistArranqueID = @ChecklistArranqueID
   OR EjecucionProduccionID = @EjecucionProduccionID
ORDER BY InspeccionID DESC;";

            int? inspeccionId = null;
            string? estadoAnterior = null;

            await using (var cmd = new SqlCommand(sqlExistente, cn, tx))
            {
                cmd.Parameters.Add("@ChecklistArranqueID", SqlDbType.Int).Value = checklistArranqueId;
                cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;

                await using var rd = await cmd.ExecuteReaderAsync();
                if (await rd.ReadAsync())
                {
                    inspeccionId = Convert.ToInt32(rd["InspeccionID"]);
                    estadoAnterior = rd["Estado"] as string;
                }
            }

            if (inspeccionId.HasValue &&
                (estadoAnterior == CalidadEstados.ProduccionLiberada ||
                 estadoAnterior == CalidadEstados.MonitoreoActivo ||
                 estadoAnterior == CalidadEstados.PendienteLiberacionCaja ||
                 estadoAnterior == CalidadEstados.CajaLiberada ||
                 estadoAnterior == CalidadEstados.EnGP12 ||
                 estadoAnterior == CalidadEstados.MaterialLiberado ||
                 estadoAnterior == CalidadEstados.Cerrada))
            {
                throw new InvalidOperationException(
                    "La corrida ya fue liberada por Calidad y no puede reenviarse como prearranque.");
            }

            if (inspeccionId.HasValue)
            {
                const string sqlUpdate = @"
UPDATE dbo.Calidad_Inspecciones
SET
    ProgramaProduccionID = @ProgramaProduccionID,
    EjecucionProduccionID = @EjecucionProduccionID,
    ChecklistArranqueID = @ChecklistArranqueID,
    SolicitudProduccionID = @SolicitudProduccionID,
    SolicitudProduccionDetalleID = @SolicitudProduccionDetalleID,
    ReleaseID = @ReleaseID,
    ReleaseDetalleID = @ReleaseDetalleID,
    ClienteID = @ClienteID,
    ClienteNombre = @ClienteNombre,
    ParteID = @ParteID,
    MaquinaID = @MaquinaID,
    MoldeID = @MoldeID,
    MaterialID = @MaterialID,
    OrdenTrabajo = @OrdenTrabajo,
    NumeroParte = @NumeroParte,
    Material = @Material,
    Proceso = N'LIBERACION DE CORRIDA',
    Maquina = @Maquina,
    Molde = @Molde,
    FechaInicioProgramada = @FechaInicioProgramada,
    FechaFinProgramada = @FechaFinProgramada,
    OperadorPrincipalPersonaID = @OperadorPrincipalPersonaID,
    OperadorPrincipalNombre = @OperadorPrincipalNombre,
    OperadorAuxiliarPersonaID = @OperadorAuxiliarPersonaID,
    OperadorAuxiliarNombre = @OperadorAuxiliarNombre,
    CantidadTotal = @CantidadTotal,
    CantidadRevisada = 0,
    CantidadPendiente = @CantidadTotal,
    ChecklistValidado = 0,
    HojaInspeccionProducto = 0,
    HojaValidacionCalidad = 0,
    AyudaVisualColocada = 0,
    AlertaCalidadAplica = NULL,
    AlertaCalidadColocada = NULL,
    HIPColocada = 0,
    HCCColocada = 0,
    MatrizPolivalenciaValidada = 0,
    FechaNotificacionCalidad = GETDATE(),
    UsuarioNotificoID = @UsuarioID,
    FechaInicioValidacionPrearranque = NULL,
    FechaFinValidacionPrearranque = NULL,
    MinutosLiberacionInicial = NULL,
    CumplioTiempoObjetivoInicial = NULL,
    FechaAutorizacionPrearranque = NULL,
    UsuarioAutorizacionPrearranqueID = NULL,
    MotivoDevolucion = NULL,
    CincoDisparosSegregados = 0,
    CantidadDisparosConformes = 0,
    ValidacionDimensional = NULL,
    ValidacionApariencia = NULL,
    ValidacionGauge = NULL,
    ValidacionConductividad = NULL,
    FechaValidacionPrimerasPiezas = NULL,
    UsuarioValidacionPrimerasPiezasID = NULL,
    ResultadoCalidad = NULL,
    Etiqueta = NULL,
    Liberado = 0,
    RequiereGP12 = 0,
    EnContencion = 0,
    EsScrap = 0,
    FechaLiberacionProduccion = NULL,
    UsuarioLiberacionProduccionID = NULL,
    RequiereReliberacion = 0,
    ConfiguracionInvalidada = 0,
    FechaInvalidacion = NULL,
    UsuarioInvalidacionID = NULL,
    MotivoInvalidacion = NULL,
    Observaciones = N'Checklist corregido y reenviado por Produccion para revision de prearranque.',
    Estado = 'PENDIENTE_PREARRANQUE',
    UsuarioModificacionID = @UsuarioID,
    FechaModificacion = GETDATE()
WHERE InspeccionID = @InspeccionID;";

                await using var cmd = new SqlCommand(sqlUpdate, cn, tx);
                AgregarParametrosOrigenCalidad(cmd, origen, usuarioId);
                cmd.Parameters.Add("@InspeccionID", SqlDbType.Int).Value = inspeccionId.Value;
                await cmd.ExecuteNonQueryAsync();
            }
            else
            {
                const string sqlInsert = @"
INSERT INTO dbo.Calidad_Inspecciones
(
    ProgramaProduccionID,
    EjecucionProduccionID,
    ChecklistArranqueID,
    SolicitudProduccionID,
    SolicitudProduccionDetalleID,
    ReleaseID,
    ReleaseDetalleID,
    ClienteID,
    ClienteNombre,
    ParteID,
    MaquinaID,
    MoldeID,
    MaterialID,
    OrdenTrabajo,
    NumeroParte,
    Material,
    Proceso,
    Maquina,
    Molde,
    FechaInicioProgramada,
    FechaFinProgramada,
    OperadorPrincipalPersonaID,
    OperadorPrincipalNombre,
    OperadorAuxiliarPersonaID,
    OperadorAuxiliarNombre,
    CantidadTotal,
    CantidadRevisada,
    CantidadPendiente,
    ChecklistValidado,
    HojaInspeccionProducto,
    HojaValidacionCalidad,
    AyudaVisualColocada,
    AlertaCalidadAplica,
    AlertaCalidadColocada,
    HIPColocada,
    HCCColocada,
    MatrizPolivalenciaValidada,
    FechaNotificacionCalidad,
    UsuarioNotificoID,
    CincoDisparosSegregados,
    CantidadDisparosConformes,
    Liberado,
    RequiereGP12,
    EnContencion,
    EsScrap,
    RequiereReliberacion,
    ConfiguracionInvalidada,
    Observaciones,
    Estado,
    UsuarioCreacionID,
    FechaCreacion
)
OUTPUT INSERTED.InspeccionID
VALUES
(
    @ProgramaProduccionID,
    @EjecucionProduccionID,
    @ChecklistArranqueID,
    @SolicitudProduccionID,
    @SolicitudProduccionDetalleID,
    @ReleaseID,
    @ReleaseDetalleID,
    @ClienteID,
    @ClienteNombre,
    @ParteID,
    @MaquinaID,
    @MoldeID,
    @MaterialID,
    @OrdenTrabajo,
    @NumeroParte,
    @Material,
    N'LIBERACION DE CORRIDA',
    @Maquina,
    @Molde,
    @FechaInicioProgramada,
    @FechaFinProgramada,
    @OperadorPrincipalPersonaID,
    @OperadorPrincipalNombre,
    @OperadorAuxiliarPersonaID,
    @OperadorAuxiliarNombre,
    @CantidadTotal,
    0,
    @CantidadTotal,
    0,
    0,
    0,
    0,
    NULL,
    NULL,
    0,
    0,
    0,
    GETDATE(),
    @UsuarioID,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    0,
    N'Solicitud recibida desde Produccion para revision de prearranque.',
    'PENDIENTE_PREARRANQUE',
    @UsuarioID,
    GETDATE()
);";

                await using var cmd = new SqlCommand(sqlInsert, cn, tx);
                AgregarParametrosOrigenCalidad(cmd, origen, usuarioId);
                inspeccionId = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            }

            const string sqlHistorial = @"
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
    'RECIBIDO_DESDE_PRODUCCION',
    @EstadoAnterior,
    'PENDIENTE_PREARRANQUE',
    NULL,
    NULL,
    @Comentario,
    @UsuarioID,
    GETDATE()
);";

            await using (var cmd = new SqlCommand(sqlHistorial, cn, tx))
            {
                cmd.Parameters.Add("@InspeccionID", SqlDbType.Int).Value = inspeccionId!.Value;
                cmd.Parameters.Add("@EstadoAnterior", SqlDbType.NVarChar, 50).Value =
                    (object?)estadoAnterior ?? DBNull.Value;
                cmd.Parameters.Add("@Comentario", SqlDbType.NVarChar, 1000).Value =
                    estadoAnterior == CalidadEstados.DevueltoPrearranque
                        ? "Produccion corrigio el checklist y lo reenvio a Calidad."
                        : "Produccion envio el checklist a Calidad para revision de prearranque.";
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                await cmd.ExecuteNonQueryAsync();
            }

            return inspeccionId.Value;
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
    e.SolicitudProduccionID,
    e.SolicitudProduccionDetalleID,
    e.ReleaseID,
    e.ReleaseDetalleID,
    COALESCE(NULLIF(s.NumeroOFRecibida, ''), NULLIF(s.FolioSolicitud, '')) AS NumeroOF,
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
    COALESCE(pp.MaterialID, dt.MaterialID) AS MaterialID,
    COALESCE(NULLIF(pp.MaterialCodigo, ''), dt.MaterialCodigo) AS MaterialCodigo,
    COALESCE(NULLIF(pp.MaterialDescripcion, ''), dt.MaterialDescripcion) AS MaterialDescripcion,
    CONVERT(INT, ISNULL(e.CantidadPlaneada, pp.CantidadProgramada)) AS CantidadPlaneada,
    pp.FechaInicioProgramada,
    pp.FechaFinProgramada,
    COALESCE(e.OperadorID, opPrincipal.PersonaID) AS OperadorPrincipalPersonaID,
    COALESCE(NULLIF(e.OperadorNombre, ''), NULLIF(opPrincipal.NombreCompleto, '')) AS OperadorPrincipalNombre,
    opAuxiliar.PersonaID AS OperadorAuxiliarPersonaID,
    opAuxiliar.NombreCompleto AS OperadorAuxiliarNombre
FROM dbo.Produccion_Ejecucion e
INNER JOIN dbo.Produccion_ChecklistArranque c
    ON c.EjecucionProduccionID = e.EjecucionProduccionID
   AND c.ChecklistArranqueID = @ChecklistArranqueID
   AND c.Activo = 1
INNER JOIN dbo.Planeacion_ProgramaProduccion pp
    ON pp.ProgramaProduccionID = e.ProgramaProduccionID
   AND pp.Activo = 1
LEFT JOIN dbo.SolicitudesProduccion s
    ON s.SolicitudProduccionID = e.SolicitudProduccionID
LEFT JOIN dbo.ERP_ParteDatosTecnicos dt
    ON dt.ParteID = e.ParteID
   AND dt.Activo = 1
OUTER APPLY
(
    SELECT TOP (1)
        po.PersonaID,
        LTRIM(RTRIM(
            ISNULL(p.Nombre, '') + ' ' +
            ISNULL(p.ApellidoPaterno, '') + ' ' +
            ISNULL(p.ApellidoMaterno, '')
        )) AS NombreCompleto
    FROM dbo.Planeacion_ProgramaOperadores po
    LEFT JOIN dbo.Persona p
        ON p.PersonaID = po.PersonaID
    WHERE po.ProgramaProduccionID = e.ProgramaProduccionID
      AND po.Activo = 1
      AND UPPER(ISNULL(po.RolOperador, '')) = 'PRINCIPAL'
    ORDER BY po.ProgramaOperadorID
) opPrincipal
OUTER APPLY
(
    SELECT TOP (1)
        po.PersonaID,
        LTRIM(RTRIM(
            ISNULL(p.Nombre, '') + ' ' +
            ISNULL(p.ApellidoPaterno, '') + ' ' +
            ISNULL(p.ApellidoMaterno, '')
        )) AS NombreCompleto
    FROM dbo.Planeacion_ProgramaOperadores po
    LEFT JOIN dbo.Persona p
        ON p.PersonaID = po.PersonaID
    WHERE po.ProgramaProduccionID = e.ProgramaProduccionID
      AND po.Activo = 1
      AND UPPER(ISNULL(po.RolOperador, '')) = 'AUXILIAR'
    ORDER BY po.ProgramaOperadorID
) opAuxiliar
WHERE e.EjecucionProduccionID = @EjecucionProduccionID
  AND e.Activo = 1;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@ChecklistArranqueID", SqlDbType.Int).Value = checklistArranqueId;
            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;

            await using var rd = await cmd.ExecuteReaderAsync();
            if (!await rd.ReadAsync())
                return null;

            return new OrigenSolicitudCalidad
            {
                EjecucionProduccionID = Convert.ToInt32(rd["EjecucionProduccionID"]),
                ProgramaProduccionID = Convert.ToInt32(rd["ProgramaProduccionID"]),
                ChecklistArranqueID = Convert.ToInt32(rd["ChecklistArranqueID"]),
                SolicitudProduccionID = rd["SolicitudProduccionID"] == DBNull.Value ? null : Convert.ToInt32(rd["SolicitudProduccionID"]),
                SolicitudProduccionDetalleID = rd["SolicitudProduccionDetalleID"] == DBNull.Value ? null : Convert.ToInt32(rd["SolicitudProduccionDetalleID"]),
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
DECLARE @Estado VARCHAR(50);
DECLARE @FechaInicioProgramada DATETIME2(0);
DECLARE @FechaFinProgramada DATETIME2(0);
DECLARE @Ahora DATETIME2(0) = SYSDATETIME();
DECLARE @Horas INT;

SELECT TOP (1)
    @InspeccionID = ci.InspeccionID,
    @Estado = ci.Estado,
    @FechaInicioProgramada = ci.FechaInicioProgramada,
    @FechaFinProgramada = ci.FechaFinProgramada
FROM dbo.Calidad_Inspecciones ci WITH (UPDLOCK, HOLDLOCK)
WHERE ci.EjecucionProduccionID = @EjecucionProduccionID
ORDER BY ci.InspeccionID DESC;

IF @InspeccionID IS NULL
    THROW 51040, 'No existe una inspeccion de Calidad relacionada con la ejecucion.', 1;

IF @Estado NOT IN ('PRODUCCION_LIBERADA', 'MONITOREO_ACTIVO')
    THROW 51041, 'Calidad aun no ha liberado la produccion para iniciar el monitoreo horario.', 1;

SET @Horas =
    CASE
        WHEN @FechaInicioProgramada IS NOT NULL
         AND @FechaFinProgramada IS NOT NULL
         AND @FechaFinProgramada > @FechaInicioProgramada
            THEN CONVERT(INT, CEILING(DATEDIFF(MINUTE, @FechaInicioProgramada, @FechaFinProgramada) / 60.0))
        ELSE 9
    END;

IF @Horas < 1 SET @Horas = 1;
IF @Horas > 9 SET @Horas = 9;

IF @Estado = 'PRODUCCION_LIBERADA'
BEGIN
    UPDATE dbo.Calidad_Inspecciones
    SET
        Estado = 'MONITOREO_ACTIVO',
        UsuarioModificacionID = @UsuarioID,
        FechaModificacion = @Ahora
    WHERE InspeccionID = @InspeccionID;

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
        'INICIO_PRODUCCION_SERIE',
        'PRODUCCION_LIBERADA',
        'MONITOREO_ACTIVO',
        'VERDE',
        'VERDE',
        N'Produccion inicio la serie. Se programaron revisiones de Calidad cada hora.',
        @UsuarioID,
        @Ahora
    );
END;

;WITH Numeros AS
(
    SELECT NumeroHora
    FROM (VALUES (1),(2),(3),(4),(5),(6),(7),(8),(9)) n(NumeroHora)
    WHERE NumeroHora <= @Horas
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
    n.NumeroHora,
    DATEADD(HOUR, n.NumeroHora, @Ahora),
    0,
    0,
    'PENDIENTE',
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
    FROM dbo.Calidad_MonitoreosProceso m
    WHERE m.InspeccionID = @InspeccionID
      AND m.NumeroHora = n.NumeroHora
      AND m.Activo = 1
);";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
            await cmd.ExecuteNonQueryAsync();
        }

        private async Task VincularRegistroHoraConMonitoreoAsync(
            ProduccionEjecucionVm ejecucion,
            ProduccionRegistroHoraPostVm vm,
            TimeSpan horaInicio,
            TimeSpan horaFin,
            int registroHoraId,
            int usuarioId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            var fechaHoraFin = vm.FechaProduccion.Date.Add(horaFin);
            var cantidadPeriodo = vm.CantidadOK + vm.CantidadSospechosa + vm.CantidadScrap;

            const string sql = @"
;WITH Candidato AS
(
    SELECT TOP (1)
        m.MonitoreoID
    FROM dbo.Calidad_MonitoreosProceso m WITH (UPDLOCK, READPAST)
    WHERE m.EjecucionProduccionID = @EjecucionProduccionID
      AND m.Activo = 1
      AND m.RegistroHoraID IS NULL
    ORDER BY
        CASE WHEN m.Resultado = 'PENDIENTE' THEN 0 ELSE 1 END,
        ABS(DATEDIFF(MINUTE, m.FechaHoraProgramada, @FechaHoraFin)),
        m.NumeroHora
)
UPDATE m
SET
    m.RegistroHoraID = @RegistroHoraID,
    m.CantidadProducidaPeriodo = @CantidadProducidaPeriodo,
    m.UsuarioModificacionID = @UsuarioID,
    m.FechaModificacion = SYSDATETIME()
FROM dbo.Calidad_MonitoreosProceso m
INNER JOIN Candidato c
    ON c.MonitoreoID = m.MonitoreoID;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucion.EjecucionProduccionID;
            cmd.Parameters.Add("@RegistroHoraID", SqlDbType.Int).Value = registroHoraId;
            cmd.Parameters.Add("@CantidadProducidaPeriodo", SqlDbType.Int).Value = cantidadPeriodo;
            cmd.Parameters.Add("@FechaHoraFin", SqlDbType.DateTime2).Value = fechaHoraFin;
            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
