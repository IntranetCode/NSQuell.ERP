using ERP.NSQuell.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
namespace ERP.NSQuell.Servicios.Produccion
{
    public sealed class AgendaOperativaService
    {
        private const decimal ToleranciaCantidad = 0.0005m;
        private readonly IConfiguration _configuration;
        public AgendaOperativaService(IConfiguration configuration) { _configuration = configuration; }
        private string ConnectionString => _configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("No se encontró la cadena de conexión DefaultConnection.");
        public async Task<AgendaOperativaVm> ObtenerAgendaAsync( AgendaOperativaFiltroVm? filtros,    int usuarioId,
    DateTime? desdeForzado = null,
    DateTime? hastaForzado = null)
        {
            _ = usuarioId;
            filtros ??= new AgendaOperativaFiltroVm();
            filtros.Busqueda = string.IsNullOrWhiteSpace(filtros.Busqueda) ? null : filtros.Busqueda.Trim();
            filtros.Area = string.IsNullOrWhiteSpace(filtros.Area) ? null : filtros.Area.Trim().ToUpperInvariant();
            filtros.Estado = string.IsNullOrWhiteSpace(filtros.Estado) ? null : filtros.Estado.Trim().ToUpperInvariant();
            if (filtros.MaquinaID.HasValue && filtros.MaquinaID.Value <= 0) filtros.MaquinaID = null;
            filtros.VentanaHoras = Math.Clamp(filtros.VentanaHoras <= 0 ? 8 : filtros.VentanaHoras, 1, 72);
            var ahora = DateTime.Now;
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            var programas = await CargarProgramasBaseAsync(    filtros,   ahora,    cn,    desdeForzado,    hastaForzado);
            var vm = new AgendaOperativaVm { FechaConsulta = ahora, Filtros = filtros };
            if (programas.Count == 0) { vm.Areas = ConstruirOpcionesAreas(filtros.Area); return vm; }
            await CrearTablaTemporalProgramasAsync(programas, cn);
            var preparaciones = await CargarPreparacionesAsync(cn);
            var insumos = await CargarInsumosAsync(cn);
            var secados = await CargarSecadosAsync(cn);
            var checklists = await CargarChecklistsAsync(cn);
            var configuraciones = await CargarConfiguracionesAsync(cn);
            var calidades = await CargarCalidadesAsync(cn);
            var paros = await CargarParosAsync(cn);
            var cierres = await CargarCierresAsync(cn);
            var parejas = await CargarParejasLhRhAsync(cn);
            var items = new List<AgendaOperativaItemVm>();
            foreach (var programa in programas)
            {
                preparaciones.TryGetValue(programa.ProgramaProduccionID, out var preparacionPrograma);
                insumos.TryGetValue(programa.ProgramaProduccionID, out var insumo);
                secados.TryGetValue(programa.ProgramaProduccionID, out var secado);
                checklists.TryGetValue(programa.ProgramaProduccionID, out var checklist);
                configuraciones.TryGetValue(programa.ProgramaProduccionID, out var configuracion);
                calidades.TryGetValue(programa.ProgramaProduccionID, out var calidad);
                paros.TryGetValue(programa.ProgramaProduccionID, out var paro);
                cierres.TryGetValue(programa.ProgramaProduccionID, out var cierre);
                parejas.TryGetValue(programa.ProgramaProduccionID, out var pareja);
                items.Add(ConstruirItem(programa, preparacionPrograma, insumo, secado, checklist, configuracion, calidad, paro, cierre, pareja, ahora));
            }
            items = AplicarFiltrosDerivados(items, filtros);
            vm.Items = items.OrderBy(x => x.OrdenPrioridad).ThenByDescending(x => x.RequiereAtencionInmediata).ThenBy(x => x.FechaOrden).ThenBy(x => x.MaquinaCodigo).ThenBy(x => x.ProgramaProduccionID).ToList();
            vm.Resumen = ConstruirResumen(vm.Items);
            vm.Maquinas = programas.Where(x => x.MaquinaID.HasValue).GroupBy(x => new { x.MaquinaID, x.MaquinaCodigo, x.MaquinaNombre }).OrderBy(x => x.Key.MaquinaCodigo).Select(x => new AgendaOperativaOpcionVm { Valor = x.Key.MaquinaID!.Value.ToString(), Texto = string.IsNullOrWhiteSpace(x.Key.MaquinaNombre) ? x.Key.MaquinaCodigo ?? $"Máquina {x.Key.MaquinaID}" : $"{x.Key.MaquinaCodigo} - {x.Key.MaquinaNombre}", Seleccionado = filtros.MaquinaID == x.Key.MaquinaID }).ToList();
            vm.Areas = ConstruirOpcionesAreas(filtros.Area);
            return vm;
        }
        private static async Task<List<ProgramaBaseDto>> CargarProgramasBaseAsync(
    AgendaOperativaFiltroVm filtros,
    DateTime ahora,
    SqlConnection cn,
    DateTime? desdeForzado = null,
    DateTime? hastaForzado = null)
        {
            var desde = desdeForzado ?? ahora.AddHours(-24);
            var hasta = hastaForzado ?? ahora.AddHours(filtros.VentanaHoras);

            if (hasta <= desde)
                hasta = desde.AddDays(1);

            const string sql = @"
SELECT TOP(500)
    pp.ProgramaProduccionID,pp.SolicitudProduccionID,pp.SolicitudProduccionDetalleID,pp.ReleaseDetalleID,
    COALESCE(NULLIF(LTRIM(RTRIM(s.NumeroOFRecibida)),N''),NULLIF(LTRIM(RTRIM(s.FolioSolicitud)),N'')) AS NumeroOF,
    pp.ParteID,pp.NumeroParte,pp.ReferenciaSAP,pp.DesignacionDescripcionSAP AS DescripcionParte,
    pp.MaquinaID,COALESCE(NULLIF(LTRIM(RTRIM(pp.MaquinaCodigo)),N''),m.Codigo) AS MaquinaCodigo,
    COALESCE(NULLIF(LTRIM(RTRIM(pp.MaquinaNombre)),N''),m.Nombre) AS MaquinaNombre,
    pp.MoldeID,pp.MoldeCodigo,CONVERT(INT,ISNULL(pp.CantidadProgramada,0)) AS CantidadProgramada,
    CONVERT(INT,ISNULL(e.CantidadOKTotal,ISNULL(pp.CantidadProducida,0))) AS CantidadProducida,
    pp.FechaInicioProgramada,ISNULL(pp.FechaFinProgramada,DATEADD(MINUTE,CONVERT(INT,CEILING(ISNULL(pp.HorasProgramadas,1)*60)),pp.FechaInicioProgramada)) AS FechaFinProgramada,
    ISNULL(pp.EstatusID,1) AS EstatusProgramaID,pp.Observaciones,
    grupo.GrupoLhRh,CONVERT(bit,CASE WHEN anterior.ProgramaProduccionID IS NULL THEN 0 WHEN anterior.MoldeID IS NOT NULL AND pp.MoldeID IS NOT NULL THEN CASE WHEN anterior.MoldeID<>pp.MoldeID THEN 1 ELSE 0 END WHEN NULLIF(LTRIM(RTRIM(ISNULL(anterior.MoldeCodigo,N''))),N'') IS NULL OR NULLIF(LTRIM(RTRIM(ISNULL(pp.MoldeCodigo,N''))),N'') IS NULL THEN 0 WHEN UPPER(LTRIM(RTRIM(anterior.MoldeCodigo)))<>UPPER(LTRIM(RTRIM(pp.MoldeCodigo))) THEN 1 ELSE 0 END) AS RequiereCambioMolde,
    COALESCE(pp.MaterialID,d.MaterialID) AS MaterialID,COALESCE(NULLIF(pp.MaterialCodigo,N''),d.MaterialCodigo) AS MaterialCodigo,
    COALESCE(NULLIF(pp.MaterialDescripcion,N''),d.MaterialDescripcion) AS MaterialDescripcion,d.CantidadMpKg,d.TipoSecado,d.HorasSecado,d.EmbalajeCodigo,d.EmbalajeDescripcion,d.CantidadEmbalajes,
    e.EjecucionProduccionID,e.EstatusID AS EstatusEjecucionID,e.FechaInicioReal,e.FechaFinReal,e.FechaLiberacionMaquina,
    COALESCE(e.OperadorID,opPrincipal.PersonaID) AS OperadorPrincipalID,COALESCE(NULLIF(e.OperadorNombre,N''),opPrincipal.NombreCompleto) AS OperadorPrincipalNombre,
    COALESCE(e.OperadorAuxiliarID,opAuxiliar.PersonaID) AS OperadorAuxiliarID,COALESCE(NULLIF(e.OperadorAuxiliarNombre,N''),opAuxiliar.NombreCompleto) AS OperadorAuxiliarNombre,
    e.TecnicoProduccionID,e.TecnicoProduccionNombre,
    CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM dbo.Produccion_Paros pu WHERE pu.Activo=1 AND pu.FechaFinParo IS NULL AND ISNULL(pu.EsInterrupcionUrgente,0)=1 AND pu.ProgramaUrgenteID=pp.ProgramaProduccionID) THEN 1 ELSE 0 END) AS EsUrgente
FROM dbo.Planeacion_ProgramaProduccion pp
LEFT JOIN dbo.SolicitudesProduccion s ON s.SolicitudProduccionID=pp.SolicitudProduccionID AND s.Activo=1
LEFT JOIN dbo.SolicitudesProduccionDetalle d ON d.SolicitudProduccionDetalleID=pp.SolicitudProduccionDetalleID AND d.Activo=1
LEFT JOIN dbo.ERP_Maquinas m ON m.MaquinaID=pp.MaquinaID
OUTER APPLY(SELECT CHARINDEX(N'NSQ_LHRH_PAIR:',ISNULL(pp.Observaciones,N'')) AS PosGrupo) pos
OUTER APPLY(SELECT CASE WHEN pos.PosGrupo>0 THEN TRY_CONVERT(INT,LEFT(SUBSTRING(pp.Observaciones,pos.PosGrupo+LEN(N'NSQ_LHRH_PAIR:'),50),CHARINDEX(N';',SUBSTRING(pp.Observaciones,pos.PosGrupo+LEN(N'NSQ_LHRH_PAIR:'),50)+N';')-1)) ELSE NULL END AS GrupoLhRh) grupo
OUTER APPLY
(
    SELECT TOP(1) ant.ProgramaProduccionID,ant.MoldeID,ant.MoldeCodigo
    FROM dbo.Planeacion_ProgramaProduccion ant
    WHERE ant.Activo=1 AND ant.ProgramaProduccionID<>pp.ProgramaProduccionID AND ant.MaquinaID=pp.MaquinaID AND ant.FechaInicioProgramada<pp.FechaInicioProgramada AND ISNULL(ant.EstatusID,1)<>99
      AND (grupo.GrupoLhRh IS NULL OR ISNULL(ant.Observaciones,N'') NOT LIKE N'%NSQ_LHRH_PAIR:'+CONVERT(NVARCHAR(20),grupo.GrupoLhRh)+N';%')
    ORDER BY ant.FechaInicioProgramada DESC,ant.ProgramaProduccionID DESC
) anterior
OUTER APPLY
(
    SELECT TOP(1) ex.EjecucionProduccionID,ex.EstatusID,ex.FechaInicioReal,ex.FechaFinReal,ex.FechaLiberacionMaquina,ex.CantidadOKTotal,
        ex.OperadorID,ex.OperadorNombre,ex.OperadorAuxiliarID,ex.OperadorAuxiliarNombre,ex.TecnicoProduccionID,ex.TecnicoProduccionNombre
    FROM dbo.Produccion_Ejecucion ex
    WHERE ex.ProgramaProduccionID=pp.ProgramaProduccionID AND ex.Activo=1 AND ex.EstatusID IN(2,3,4,5)
    ORDER BY ex.EjecucionProduccionID DESC
) e
OUTER APPLY
(
    SELECT TOP(1) po.PersonaID,LTRIM(RTRIM(CONCAT(ISNULL(p.Nombre,N''),N' ',ISNULL(p.ApellidoPaterno,N''),N' ',ISNULL(p.ApellidoMaterno,N'')))) AS NombreCompleto
    FROM dbo.Planeacion_ProgramaOperadores po LEFT JOIN dbo.Persona p ON p.PersonaID=po.PersonaID
    WHERE po.ProgramaProduccionID=pp.ProgramaProduccionID AND po.Activo=1 AND UPPER(LTRIM(RTRIM(ISNULL(po.RolOperador,N''))))=N'PRINCIPAL'
    ORDER BY po.ProgramaOperadorID DESC
) opPrincipal
OUTER APPLY
(
    SELECT TOP(1) po.PersonaID,LTRIM(RTRIM(CONCAT(ISNULL(p.Nombre,N''),N' ',ISNULL(p.ApellidoPaterno,N''),N' ',ISNULL(p.ApellidoMaterno,N'')))) AS NombreCompleto
    FROM dbo.Planeacion_ProgramaOperadores po LEFT JOIN dbo.Persona p ON p.PersonaID=po.PersonaID
    WHERE po.ProgramaProduccionID=pp.ProgramaProduccionID AND po.Activo=1 AND UPPER(LTRIM(RTRIM(ISNULL(po.RolOperador,N''))))=N'AUXILIAR'
    ORDER BY po.ProgramaOperadorID DESC
) opAuxiliar
WHERE pp.Activo=1 AND pp.MaquinaID IS NOT NULL AND pp.FechaInicioProgramada IS NOT NULL AND ISNULL(pp.EstatusID,1) NOT IN(6,9,99)
  AND (@MaquinaID IS NULL OR pp.MaquinaID=@MaquinaID)
  AND
  (
      (e.EjecucionProduccionID IS NOT NULL AND e.EstatusID IN(2,3,4,5))
      OR (pp.FechaInicioProgramada<=@Hasta AND ISNULL(pp.FechaFinProgramada,DATEADD(MINUTE,CONVERT(INT,CEILING(ISNULL(pp.HorasProgramadas,1)*60)),pp.FechaInicioProgramada))>=@Desde)
      OR (e.EjecucionProduccionID IS NULL AND ISNULL(pp.EstatusID,1)=1 AND pp.FechaInicioProgramada<=@Ahora)
  )
  AND
  (
      @Busqueda IS NULL OR pp.NumeroParte LIKE N'%'+@Busqueda+N'%' OR pp.ReferenciaSAP LIKE N'%'+@Busqueda+N'%' OR pp.DesignacionDescripcionSAP LIKE N'%'+@Busqueda+N'%'
      OR pp.MaquinaCodigo LIKE N'%'+@Busqueda+N'%' OR pp.MaquinaNombre LIKE N'%'+@Busqueda+N'%' OR pp.MoldeCodigo LIKE N'%'+@Busqueda+N'%'
      OR s.NumeroOFRecibida LIKE N'%'+@Busqueda+N'%' OR s.FolioSolicitud LIKE N'%'+@Busqueda+N'%'
  )
ORDER BY CASE WHEN e.EjecucionProduccionID IS NOT NULL THEN 0 ELSE 1 END,pp.FechaInicioProgramada,pp.MaquinaID,pp.ProgramaProduccionID;";
            var lista = new List<ProgramaBaseDto>();
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@Desde", SqlDbType.DateTime2).Value = desde;
            cmd.Parameters.Add("@Hasta", SqlDbType.DateTime2).Value = hasta;
            cmd.Parameters.Add("@Ahora", SqlDbType.DateTime2).Value = ahora;
            cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = (object?)filtros.MaquinaID ?? DBNull.Value;
            cmd.Parameters.Add("@Busqueda", SqlDbType.NVarChar, 200).Value = (object?)filtros.Busqueda ?? DBNull.Value;
            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                lista.Add(new ProgramaBaseDto
                {
                    ProgramaProduccionID = Int(rd, "ProgramaProduccionID"),
                    SolicitudProduccionID = NInt(rd, "SolicitudProduccionID"),
                    SolicitudProduccionDetalleID = NInt(rd, "SolicitudProduccionDetalleID"),
                    ReleaseDetalleID = NInt(rd, "ReleaseDetalleID"),
                    NumeroOF = Txt(rd, "NumeroOF"),
                    ParteID = NInt(rd, "ParteID"),
                    NumeroParte = Txt(rd, "NumeroParte"),
                    ReferenciaSAP = Txt(rd, "ReferenciaSAP"),
                    DescripcionParte = Txt(rd, "DescripcionParte"),
                    MaquinaID = NInt(rd, "MaquinaID"),
                    MaquinaCodigo = Txt(rd, "MaquinaCodigo"),
                    MaquinaNombre = Txt(rd, "MaquinaNombre"),
                    MoldeID = NInt(rd, "MoldeID"),
                    MoldeCodigo = Txt(rd, "MoldeCodigo"),
                    CantidadProgramada = Int(rd, "CantidadProgramada"),
                    CantidadProducida = Int(rd, "CantidadProducida"),
                    FechaInicioProgramada = NDate(rd, "FechaInicioProgramada"),
                    FechaFinProgramada = NDate(rd, "FechaFinProgramada"),
                    EstatusProgramaID = Int(rd, "EstatusProgramaID"),
                    Observaciones = Txt(rd, "Observaciones"),
                    GrupoLhRh = NInt(rd, "GrupoLhRh"),
                    RequiereCambioMolde = Bool(rd, "RequiereCambioMolde"),
                    MaterialID = NInt(rd, "MaterialID"),
                    MaterialCodigo = Txt(rd, "MaterialCodigo"),
                    MaterialDescripcion = Txt(rd, "MaterialDescripcion"),
                    CantidadMpKg = NDec(rd, "CantidadMpKg"),
                    TipoSecado = Txt(rd, "TipoSecado"),
                    HorasSecado = NDec(rd, "HorasSecado"),
                    EmbalajeCodigo = Txt(rd, "EmbalajeCodigo"),
                    EmbalajeDescripcion = Txt(rd, "EmbalajeDescripcion"),
                    CantidadEmbalajes = NDec(rd, "CantidadEmbalajes"),
                    EjecucionProduccionID = NInt(rd, "EjecucionProduccionID"),
                    EstatusEjecucionID = NInt(rd, "EstatusEjecucionID"),
                    FechaInicioReal = NDate(rd, "FechaInicioReal"),
                    FechaFinReal = NDate(rd, "FechaFinReal"),
                    FechaLiberacionMaquina = NDate(rd, "FechaLiberacionMaquina"),
                    OperadorPrincipalID = NInt(rd, "OperadorPrincipalID"),
                    OperadorPrincipalNombre = Txt(rd, "OperadorPrincipalNombre"),
                    OperadorAuxiliarID = NInt(rd, "OperadorAuxiliarID"),
                    OperadorAuxiliarNombre = Txt(rd, "OperadorAuxiliarNombre"),
                    TecnicoProduccionID = NInt(rd, "TecnicoProduccionID"),
                    TecnicoProduccionNombre = Txt(rd, "TecnicoProduccionNombre"),
                    EsUrgente = Bool(rd, "EsUrgente")
                });
            }
            return lista;
        }
        private static async Task CrearTablaTemporalProgramasAsync(List<ProgramaBaseDto> programas, SqlConnection cn)
        {
            if (programas == null || programas.Count == 0) return;

            var sb = new StringBuilder();

            sb.AppendLine("IF OBJECT_ID('tempdb..#AgendaProgramas') IS NOT NULL DROP TABLE #AgendaProgramas;");
            sb.AppendLine("CREATE TABLE #AgendaProgramas");
            sb.AppendLine("(");
            sb.AppendLine("    ProgramaProduccionID INT NOT NULL PRIMARY KEY,");
            sb.AppendLine("    EjecucionProduccionID INT NULL");
            sb.AppendLine(");");
            sb.AppendLine("INSERT INTO #AgendaProgramas(ProgramaProduccionID,EjecucionProduccionID)");
            sb.AppendLine("VALUES");

            for (var i = 0; i < programas.Count; i++)
            {
                var programa = programas[i];

                sb.Append("(");
                sb.Append(programa.ProgramaProduccionID);
                sb.Append(",");

                if (programa.EjecucionProduccionID.HasValue)
                    sb.Append(programa.EjecucionProduccionID.Value);
                else
                    sb.Append("NULL");

                sb.Append(i == programas.Count - 1 ? ");" : "),");
                sb.AppendLine();
            }

            await using var cmd = new SqlCommand(sb.ToString(), cn);
            cmd.CommandType = CommandType.Text;
            await cmd.ExecuteNonQueryAsync();
        }
        private static async Task<Dictionary<int, Dictionary<string, PreparacionDto>>> CargarPreparacionesAsync(SqlConnection cn)
        {
            const string sql = @"
;WITH X AS
(
    SELECT pa.ProgramaProduccionID,pa.PreparacionAnticipadaID,UPPER(LTRIM(RTRIM(pa.TipoTarea))) AS TipoTarea,UPPER(LTRIM(RTRIM(ISNULL(pa.Estado,N'PENDIENTE')))) AS Estado,
           pa.FechaObjetivo,pa.FechaAviso,pa.FechaInicioReal,pa.FechaFinReal,pa.FechaConfirmacion,pa.Observaciones,
           ROW_NUMBER() OVER(PARTITION BY pa.ProgramaProduccionID,UPPER(LTRIM(RTRIM(pa.TipoTarea))) ORDER BY CASE WHEN pa.Activo=1 THEN 0 ELSE 1 END,pa.PreparacionAnticipadaID DESC) AS rn
    FROM dbo.Produccion_PreparacionAnticipada pa INNER JOIN #AgendaProgramas a ON a.ProgramaProduccionID=pa.ProgramaProduccionID
    WHERE pa.TipoTarea IN(N'CAMBIO_MOLDE',N'PREPARAR_EMBALAJE',N'SECADO_MATERIAL')
)
SELECT ProgramaProduccionID,PreparacionAnticipadaID,TipoTarea,Estado,FechaObjetivo,FechaAviso,FechaInicioReal,FechaFinReal,FechaConfirmacion,Observaciones FROM X WHERE rn=1;";
            var result = new Dictionary<int, Dictionary<string, PreparacionDto>>();
            await using var cmd = new SqlCommand(sql, cn);
            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                var id = Int(rd, "ProgramaProduccionID");
                var tipo = Txt(rd, "TipoTarea") ?? string.Empty;
                if (!result.TryGetValue(id, out var dic)) { dic = new Dictionary<string, PreparacionDto>(StringComparer.OrdinalIgnoreCase); result[id] = dic; }
                dic[tipo] = new PreparacionDto { PreparacionAnticipadaID = Int(rd, "PreparacionAnticipadaID"), TipoTarea = tipo, Estado = Txt(rd, "Estado") ?? "PENDIENTE", FechaObjetivo = NDate(rd, "FechaObjetivo"), FechaAviso = NDate(rd, "FechaAviso"), FechaInicioReal = NDate(rd, "FechaInicioReal"), FechaFinReal = NDate(rd, "FechaFinReal"), FechaConfirmacion = NDate(rd, "FechaConfirmacion"), Observaciones = Txt(rd, "Observaciones") };
            }
            return result;
        }
        private static async Task<Dictionary<int, InsumoDto>> CargarInsumosAsync(SqlConnection cn)
        {
            const string sql = @"
SELECT a.ProgramaProduccionID,
       CONVERT(DECIMAL(18,4),ISNULL(d.CantidadMpKg,0)) AS CantidadMpRequerida,
       CONVERT(DECIMAL(18,4),ISNULL(mp.Recibido,0)) AS CantidadMpRecibida,
       CONVERT(DECIMAL(18,4),ISNULL(d.CantidadEmbalajes,0)) AS CantidadEmbalajeRequerida,
       CONVERT(DECIMAL(18,4),ISNULL(emb.Recibido,0)) AS CantidadEmbalajeRecibida
FROM #AgendaProgramas a
INNER JOIN dbo.Planeacion_ProgramaProduccion pp ON pp.ProgramaProduccionID=a.ProgramaProduccionID
LEFT JOIN dbo.SolicitudesProduccionDetalle d ON d.SolicitudProduccionDetalleID=pp.SolicitudProduccionDetalleID AND d.Activo=1
OUTER APPLY
(
    SELECT SUM(ISNULL(r.CantidadRecibidaProduccion,0)) AS Recibido
    FROM dbo.Produccion_RecepcionMateriales r
    WHERE r.Activo=1 AND r.TipoOrigen=N'MP' AND r.EstadoRecepcion IN(N'RECIBIDO_COMPLETO',N'RECIBIDO_PARCIAL') AND r.SolicitudProduccionID=pp.SolicitudProduccionID
      AND
      (
          r.ProgramaProduccionID=pp.ProgramaProduccionID
          OR (r.ProgramaProduccionID IS NULL AND r.SolicitudProduccionDetalleID=pp.SolicitudProduccionDetalleID)
          OR (r.ProgramaProduccionID IS NULL AND r.SolicitudProduccionDetalleID IS NULL AND (r.MaterialSolicitadoID=d.MaterialID OR (d.MaterialID IS NULL AND UPPER(LTRIM(RTRIM(ISNULL(r.CodigoSolicitadoSnapshot,N''))))=UPPER(LTRIM(RTRIM(ISNULL(d.MaterialCodigo,N'')))))))
      )
) mp
OUTER APPLY
(
    SELECT SUM(ISNULL(r.CantidadRecibidaProduccion,0)) AS Recibido
    FROM dbo.Produccion_RecepcionMateriales r
    LEFT JOIN dbo.ERP_Embalajes er ON er.EmbalajeID=r.EmbalajeSolicitadoID
    WHERE r.Activo=1 AND r.TipoOrigen=N'EMBALAJE' AND r.EstadoRecepcion IN(N'RECIBIDO_COMPLETO',N'RECIBIDO_PARCIAL') AND r.SolicitudProduccionID=pp.SolicitudProduccionID
      AND
      (
          r.ProgramaProduccionID=pp.ProgramaProduccionID
          OR (r.ProgramaProduccionID IS NULL AND r.SolicitudProduccionDetalleID=pp.SolicitudProduccionDetalleID)
          OR (r.ProgramaProduccionID IS NULL AND r.SolicitudProduccionDetalleID IS NULL AND UPPER(LTRIM(RTRIM(ISNULL(er.Codigo,r.CodigoSolicitadoSnapshot))))=UPPER(LTRIM(RTRIM(ISNULL(d.EmbalajeCodigo,N'')))))
      )
) emb;";
            var result = new Dictionary<int, InsumoDto>();
            await using var cmd = new SqlCommand(sql, cn);
            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync()) result[Int(rd, "ProgramaProduccionID")] = new InsumoDto { CantidadMpRequerida = Dec(rd, "CantidadMpRequerida"), CantidadMpRecibida = Dec(rd, "CantidadMpRecibida"), CantidadEmbalajeRequerida = Dec(rd, "CantidadEmbalajeRequerida"), CantidadEmbalajeRecibida = Dec(rd, "CantidadEmbalajeRecibida") };
            return result;
        }
        private static async Task<Dictionary<int, SecadoDto>> CargarSecadosAsync(SqlConnection cn)
        {
            const string sql = @"
SELECT a.ProgramaProduccionID,COUNT(1) AS TotalRegistros,
       SUM(CASE WHEN UPPER(LTRIM(RTRIM(ISNULL(sm.Estado,N'PENDIENTE'))))=N'FINALIZADO' THEN 1 ELSE 0 END) AS Finalizados,
       SUM(CASE WHEN UPPER(LTRIM(RTRIM(ISNULL(sm.Estado,N'PENDIENTE')))) IN(N'EN_PROCESO',N'PARCIAL') THEN 1 ELSE 0 END) AS EnProceso,
       SUM(CASE WHEN UPPER(LTRIM(RTRIM(ISNULL(sm.Estado,N'PENDIENTE'))))=N'PENDIENTE' THEN 1 ELSE 0 END) AS Pendientes,
       MIN(sm.FechaInicioSecadoObjetivo) AS FechaInicioObjetivo,MAX(sm.FechaObjetivoFinSecado) AS FechaFinObjetivo,
       MIN(sm.FechaPrimerInicioSecado) AS FechaPrimerInicio,MAX(sm.FechaUltimoFinSecado) AS FechaUltimoFin
FROM #AgendaProgramas a
INNER JOIN dbo.Produccion_SecadoMaterial sm
    ON sm.ProgramaProduccionID=a.ProgramaProduccionID
    OR (sm.ProgramaProduccionID IS NULL AND a.EjecucionProduccionID IS NOT NULL AND sm.EjecucionProduccionID=a.EjecucionProduccionID)
WHERE sm.Activo=1 AND UPPER(LTRIM(RTRIM(ISNULL(sm.Estado,N''))))<>N'CANCELADO'
GROUP BY a.ProgramaProduccionID;";
            var result = new Dictionary<int, SecadoDto>();
            await using var cmd = new SqlCommand(sql, cn);
            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync()) result[Int(rd, "ProgramaProduccionID")] = new SecadoDto { Total = Int(rd, "TotalRegistros"), Finalizados = Int(rd, "Finalizados"), EnProceso = Int(rd, "EnProceso"), Pendientes = Int(rd, "Pendientes"), FechaInicioObjetivo = NDate(rd, "FechaInicioObjetivo"), FechaFinObjetivo = NDate(rd, "FechaFinObjetivo"), FechaPrimerInicio = NDate(rd, "FechaPrimerInicio"), FechaUltimoFin = NDate(rd, "FechaUltimoFin") };
            return result;
        }
        private static async Task<Dictionary<int, ChecklistDto>> CargarChecklistsAsync(SqlConnection cn)
        {
            const string sql = @"
;WITH X AS
(
    SELECT c.ProgramaProduccionID,c.ChecklistArranqueID,c.EjecucionProduccionID,c.EstatusID,c.FechaChecklist,c.FechaCapturaProduccion,c.FechaValidacionCalidad,c.ObservacionesCalidad,
           ROW_NUMBER() OVER(PARTITION BY c.ProgramaProduccionID ORDER BY c.NumeroAplicacion DESC,c.ChecklistArranqueID DESC) AS rn
    FROM dbo.Produccion_ChecklistArranque c INNER JOIN #AgendaProgramas a ON a.ProgramaProduccionID=c.ProgramaProduccionID
    WHERE c.Activo=1 AND c.CodigoFormato=N'GQ-F-PR01-06' AND c.TipoChecklist=N'ARRANQUE_LIBERACION'
)
SELECT ProgramaProduccionID,ChecklistArranqueID,EjecucionProduccionID,EstatusID,FechaChecklist,FechaCapturaProduccion,FechaValidacionCalidad,ObservacionesCalidad FROM X WHERE rn=1;";
            var result = new Dictionary<int, ChecklistDto>();
            await using var cmd = new SqlCommand(sql, cn);
            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync()) result[Int(rd, "ProgramaProduccionID")] = new ChecklistDto { ChecklistArranqueID = Int(rd, "ChecklistArranqueID"), EjecucionProduccionID = Int(rd, "EjecucionProduccionID"), EstatusID = Int(rd, "EstatusID"), FechaChecklist = NDate(rd, "FechaChecklist"), FechaCapturaProduccion = NDate(rd, "FechaCapturaProduccion"), FechaValidacionCalidad = NDate(rd, "FechaValidacionCalidad"), ObservacionesCalidad = Txt(rd, "ObservacionesCalidad") };
            return result;
        }
        private static async Task<Dictionary<int, ConfiguracionDto>> CargarConfiguracionesAsync(SqlConnection cn)
        {
            const string sql = @"
SELECT a.ProgramaProduccionID,c.ConfiguracionCorridaID,c.EjecucionProduccionID,c.CavidadesUsadas,c.TiempoCicloSegundos,c.ObjetivoHoraCalculado,c.ContadorInicioVigencia,c.FechaInicioVigencia,c.TecnicoProduccionID,
       NULLIF(LTRIM(RTRIM(CONCAT(ISNULL(p.Nombre,N''),N' ',ISNULL(p.ApellidoPaterno,N''),N' ',ISNULL(p.ApellidoMaterno,N'')))),N'') AS TecnicoNombre
FROM #AgendaProgramas a
INNER JOIN dbo.Produccion_ConfiguracionCorrida c ON c.EjecucionProduccionID=a.EjecucionProduccionID AND c.Activo=1 AND c.FechaFinVigencia IS NULL
LEFT JOIN dbo.Persona p ON p.PersonaID=c.TecnicoProduccionID;";
            var result = new Dictionary<int, ConfiguracionDto>();
            await using var cmd = new SqlCommand(sql, cn);
            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync()) result[Int(rd, "ProgramaProduccionID")] = new ConfiguracionDto { ConfiguracionCorridaID = Int(rd, "ConfiguracionCorridaID"), EjecucionProduccionID = Int(rd, "EjecucionProduccionID"), CavidadesUsadas = Int(rd, "CavidadesUsadas"), TiempoCicloSegundos = Dec(rd, "TiempoCicloSegundos"), ObjetivoHoraCalculado = NDec(rd, "ObjetivoHoraCalculado"), ContadorInicioVigencia = NLong(rd, "ContadorInicioVigencia"), FechaInicioVigencia = NDate(rd, "FechaInicioVigencia"), TecnicoProduccionID = NInt(rd, "TecnicoProduccionID"), TecnicoNombre = Txt(rd, "TecnicoNombre") };
            return result;
        }
        private static async Task<Dictionary<int, CalidadDto>> CargarCalidadesAsync(SqlConnection cn)
        {
            const string sql = @"
SELECT a.ProgramaProduccionID,ci.InspeccionID,ci.EjecucionProduccionID,ci.Estado,ci.ResultadoCalidad,ci.Etiqueta,ISNULL(ci.Liberado,0) AS Liberado,
       ISNULL(ci.ConfiguracionInvalidada,0) AS ConfiguracionInvalidada,ISNULL(ci.RequiereReliberacion,0) AS RequiereReliberacion,
       ISNULL(ci.CincoDisparosSegregados,0) AS CincoDisparosSegregados,ISNULL(ci.CantidadDisparosConformes,0) AS CantidadDisparosConformes,
       ci.MotivoDevolucion,ci.FechaNotificacionCalidad,rel.ReliberacionID,rel.Resultado AS ResultadoReliberacion,rel.FechaValidacion AS FechaValidacionReliberacion
FROM #AgendaProgramas a
OUTER APPLY
(
    SELECT TOP(1) q.* FROM dbo.Calidad_Inspecciones q
    WHERE q.EjecucionProduccionID=a.EjecucionProduccionID AND ISNULL(q.Estado,N'')<>N'CERRADA'
    ORDER BY q.InspeccionID DESC
) ci
OUTER APPLY
(
    SELECT TOP(1) r.ReliberacionID,r.Resultado,r.FechaValidacion FROM dbo.Calidad_Reliberaciones r
    WHERE ci.InspeccionID IS NOT NULL AND r.InspeccionID=ci.InspeccionID AND r.EjecucionProduccionID=ci.EjecucionProduccionID AND r.Activo=1
    ORDER BY r.NumeroReliberacion DESC,r.ReliberacionID DESC
) rel
WHERE ci.InspeccionID IS NOT NULL;";
            var result = new Dictionary<int, CalidadDto>();
            await using var cmd = new SqlCommand(sql, cn);
            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync()) result[Int(rd, "ProgramaProduccionID")] = new CalidadDto { InspeccionID = Int(rd, "InspeccionID"), EjecucionProduccionID = Int(rd, "EjecucionProduccionID"), Estado = Txt(rd, "Estado"), ResultadoCalidad = Txt(rd, "ResultadoCalidad"), Etiqueta = Txt(rd, "Etiqueta"), Liberado = Bool(rd, "Liberado"), ConfiguracionInvalidada = Bool(rd, "ConfiguracionInvalidada"), RequiereReliberacion = Bool(rd, "RequiereReliberacion"), CincoDisparosSegregados = Bool(rd, "CincoDisparosSegregados"), CantidadDisparosConformes = Int(rd, "CantidadDisparosConformes"), MotivoDevolucion = Txt(rd, "MotivoDevolucion"), FechaNotificacionCalidad = NDate(rd, "FechaNotificacionCalidad"), ReliberacionID = NInt(rd, "ReliberacionID"), ResultadoReliberacion = Txt(rd, "ResultadoReliberacion"), FechaValidacionReliberacion = NDate(rd, "FechaValidacionReliberacion") };
            return result;
        }
        private static async Task<Dictionary<int, ParoDto>> CargarParosAsync(SqlConnection cn)
        {
            const string sql = @"
;WITH X AS
(
    SELECT p.ProgramaProduccionID,p.ParoID,p.EjecucionProduccionID,p.FechaInicioParo,p.FechaFinParo,p.MotivoParoTexto,p.Descripcion,
           ISNULL(p.EsMayorA15Minutos,0) AS EsMayorA15Minutos,ISNULL(p.EsInterrupcionUrgente,0) AS EsInterrupcionUrgente,p.ProgramaUrgenteID,
           ISNULL(p.EsParoLhRh,0) AS EsParoLhRh,p.GrupoParoLhRh,
           ROW_NUMBER() OVER(PARTITION BY p.ProgramaProduccionID ORDER BY CASE WHEN p.FechaFinParo IS NULL THEN 0 ELSE 1 END,p.FechaInicioParo DESC,p.ParoID DESC) AS rn
    FROM dbo.Produccion_Paros p INNER JOIN #AgendaProgramas a ON a.ProgramaProduccionID=p.ProgramaProduccionID
    WHERE p.Activo=1 AND
    (
        p.FechaFinParo IS NULL OR
        (
            p.FechaFinParo IS NOT NULL AND (ISNULL(p.EsMayorA15Minutos,0)=1 OR ISNULL(p.EsInterrupcionUrgente,0)=1)
            AND NOT EXISTS
            (
                SELECT 1 FROM dbo.Calidad_InspeccionHistorial h INNER JOIN dbo.Calidad_Inspecciones ci ON ci.InspeccionID=h.InspeccionID
                WHERE ci.EjecucionProduccionID=p.EjecucionProduccionID AND h.Movimiento=N'CONFIRMACION_INICIO_SERIE_PRODUCCION' AND h.FechaMovimiento>=p.FechaFinParo
            )
        )
    )
)
SELECT x.*,su.NumeroOFRecibida AS OFUrgente
FROM X x LEFT JOIN dbo.Planeacion_ProgramaProduccion pu ON pu.ProgramaProduccionID=x.ProgramaUrgenteID
LEFT JOIN dbo.SolicitudesProduccion su ON su.SolicitudProduccionID=pu.SolicitudProduccionID AND su.Activo=1
WHERE x.rn=1;";
            var result = new Dictionary<int, ParoDto>();
            await using var cmd = new SqlCommand(sql, cn);
            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync()) result[Int(rd, "ProgramaProduccionID")] = new ParoDto { ParoID = Int(rd, "ParoID"), EjecucionProduccionID = Int(rd, "EjecucionProduccionID"), FechaInicio = NDate(rd, "FechaInicioParo") ?? DateTime.Now, FechaFin = NDate(rd, "FechaFinParo"), Motivo = Txt(rd, "MotivoParoTexto") ?? Txt(rd, "Descripcion"), EsMayorA15 = Bool(rd, "EsMayorA15Minutos"), EsInterrupcionUrgente = Bool(rd, "EsInterrupcionUrgente"), ProgramaUrgenteID = NInt(rd, "ProgramaUrgenteID"), OFUrgente = Txt(rd, "OFUrgente"), EsParoLhRh = Bool(rd, "EsParoLhRh"), GrupoParoLhRh = NGuid(rd, "GrupoParoLhRh") };
            return result;
        }
        private static async Task<Dictionary<int, CierreDto>> CargarCierresAsync(SqlConnection cn)
        {
            const string sql = @"
SELECT a.ProgramaProduccionID,e.EjecucionProduccionID,
       ISNULL(e.CantidadOKTotal,0) AS CantidadOK,ISNULL(e.CantidadSospechosaTotal,0) AS CantidadSospechosa,ISNULL(e.CantidadScrapTotal,0) AS CantidadScrap,
       ISNULL(cajas.OkEnCajas,0)+ISNULL(detalle.OkDetalle,0) AS OkEnCajas,ISNULL(cajas.SospechosoEnCajas,0) AS SospechosoEnCajas,ISNULL(cajas.RetencionEnCajas,0) AS RetencionEnCajas,ISNULL(cajas.ScrapEnCajas,0) AS ScrapEnCajas,
       ISNULL(cajas.CajasFormadasPendientes,0) AS CajasFormadasPendientes,ISNULL(cajas.CajasPendientesCalidad,0) AS CajasPendientesCalidad,
       ISNULL(reg.RegistrosNormales,0) AS RegistrosNormales,ISNULL(reg.MinutosNormalesCapturados,0) AS MinutosNormalesCapturados,TRY_CONVERT(DECIMAL(18,4),dt.ObjetivoHora) AS ObjetivoHora,
       CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM dbo.Produccion_TiempoExtra te WHERE te.EjecucionProduccionID=e.EjecucionProduccionID AND te.Activo=1 AND te.FechaHoraFin IS NULL AND UPPER(LTRIM(RTRIM(ISNULL(te.Estado,N'')))) IN(N'EN_CURSO',N'PAUSADO')) THEN 1 ELSE 0 END) AS TieneTiempoExtraActivo,
       ISNULL(cal.MonitoreosPendientes,0) AS MonitoreosPendientes,ISNULL(cal.DisposicionesPendientes,0) AS DisposicionesPendientes,ISNULL(cal.ReliberacionesPendientes,0) AS ReliberacionesPendientes
FROM #AgendaProgramas a INNER JOIN dbo.Produccion_Ejecucion e ON e.EjecucionProduccionID=a.EjecucionProduccionID AND e.Activo=1
OUTER APPLY
(
    SELECT
      SUM(CASE WHEN UPPER(LTRIM(RTRIM(ISNULL(c.TipoCaja,N'OK'))))=N'OK' AND odx.CajaProduccionID IS NULL THEN ISNULL(c.CantidadPiezas,ISNULL(c.Cantidad,0)) ELSE 0 END) AS OkEnCajas,
      SUM(CASE WHEN UPPER(LTRIM(RTRIM(ISNULL(c.TipoCaja,N''))))=N'SOSPECHOSO' THEN ISNULL(c.CantidadPiezas,ISNULL(c.Cantidad,0)) ELSE 0 END) AS SospechosoEnCajas,
      SUM(CASE WHEN UPPER(LTRIM(RTRIM(ISNULL(c.TipoCaja,N''))))=N'RETENCION' THEN ISNULL(c.CantidadPiezas,ISNULL(c.Cantidad,0)) ELSE 0 END) AS RetencionEnCajas,
      SUM(CASE WHEN UPPER(LTRIM(RTRIM(ISNULL(c.TipoCaja,N''))))=N'SCRAP' THEN ISNULL(c.CantidadPiezas,ISNULL(c.Cantidad,0)) ELSE 0 END) AS ScrapEnCajas,
      SUM(CASE WHEN c.EstadoCajaID=@CajaFormada THEN 1 ELSE 0 END) AS CajasFormadasPendientes,
      SUM(CASE WHEN c.EstadoCajaID=@CajaPendienteCalidad THEN 1 ELSE 0 END) AS CajasPendientesCalidad
    FROM dbo.Produccion_Cajas c
    LEFT JOIN (SELECT od0.CajaProduccionID FROM dbo.Produccion_CajaOrigenDetalle od0 WHERE od0.Activo=1 GROUP BY od0.CajaProduccionID) odx ON odx.CajaProduccionID=c.CajaProduccionID
    WHERE c.EjecucionProduccionID=e.EjecucionProduccionID AND c.Activo=1
) cajas
OUTER APPLY(SELECT SUM(od.CantidadPiezas) AS OkDetalle FROM dbo.Produccion_CajaOrigenDetalle od WHERE od.EjecucionProduccionID=e.EjecucionProduccionID AND od.Activo=1) detalle
OUTER APPLY
(
    SELECT COUNT(1) AS RegistrosNormales,ISNULL(SUM(CASE WHEN r.MinutosProductivos IS NOT NULL AND r.MinutosProductivos>0 THEN r.MinutosProductivos WHEN r.HoraFin>=r.HoraInicio THEN CONVERT(DECIMAL(18,2),DATEDIFF(MINUTE,r.HoraInicio,r.HoraFin)) ELSE CONVERT(DECIMAL(18,2),1440+DATEDIFF(MINUTE,r.HoraInicio,r.HoraFin)) END),0) AS MinutosNormalesCapturados
    FROM dbo.Produccion_RegistroHora r WHERE r.EjecucionProduccionID=e.EjecucionProduccionID AND r.Activo=1 AND ISNULL(r.EsTiempoExtra,0)=0
) reg
OUTER APPLY(SELECT TOP(1) d.ObjetivoHora FROM dbo.ERP_ParteDatosTecnicos d WHERE d.ParteID=e.ParteID AND d.Activo=1 ORDER BY d.ParteDatoTecnicoID DESC) dt
OUTER APPLY
(
    SELECT TOP(1) ci.InspeccionID FROM dbo.Calidad_Inspecciones ci WHERE ci.EjecucionProduccionID=e.EjecucionProduccionID ORDER BY ci.InspeccionID DESC
) ciActual
OUTER APPLY
(
    SELECT
      (SELECT COUNT(1) FROM dbo.Calidad_MonitoreosProceso m WHERE m.InspeccionID=ciActual.InspeccionID AND m.Activo=1 AND UPPER(LTRIM(RTRIM(ISNULL(m.Resultado,N'PENDIENTE'))))=N'PENDIENTE') AS MonitoreosPendientes,
      (SELECT COUNT(1) FROM dbo.Calidad_DisposicionesMaterial d WHERE d.InspeccionID=ciActual.InspeccionID AND d.Activo=1 AND UPPER(LTRIM(RTRIM(ISNULL(d.ResultadoFinal,N'PENDIENTE'))))=N'PENDIENTE') AS DisposicionesPendientes,
      (SELECT COUNT(1) FROM dbo.Calidad_Reliberaciones r WHERE r.InspeccionID=ciActual.InspeccionID AND r.Activo=1 AND UPPER(LTRIM(RTRIM(ISNULL(r.Resultado,N'PENDIENTE'))))<>N'AUTORIZADA') AS ReliberacionesPendientes
) cal;";
            var result = new Dictionary<int, CierreDto>();
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@CajaFormada", SqlDbType.Int).Value = ProduccionCajaEstatus.FormadaProduccion;
            cmd.Parameters.Add("@CajaPendienteCalidad", SqlDbType.Int).Value = ProduccionCajaEstatus.PendienteCalidad;
            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync()) result[Int(rd, "ProgramaProduccionID")] = new CierreDto { EjecucionProduccionID = Int(rd, "EjecucionProduccionID"), CantidadOK = Int(rd, "CantidadOK"), CantidadSospechosa = Int(rd, "CantidadSospechosa"), CantidadScrap = Int(rd, "CantidadScrap"), OkEnCajas = Int(rd, "OkEnCajas"), SospechosoEnCajas = Int(rd, "SospechosoEnCajas"), RetencionEnCajas = Int(rd, "RetencionEnCajas"), ScrapEnCajas = Int(rd, "ScrapEnCajas"), CajasFormadasPendientes = Int(rd, "CajasFormadasPendientes"), CajasPendientesCalidad = Int(rd, "CajasPendientesCalidad"), RegistrosNormales = Int(rd, "RegistrosNormales"), MinutosNormalesCapturados = Dec(rd, "MinutosNormalesCapturados"), ObjetivoHora = NDec(rd, "ObjetivoHora"), TieneTiempoExtraActivo = Bool(rd, "TieneTiempoExtraActivo"), MonitoreosPendientes = Int(rd, "MonitoreosPendientes"), DisposicionesPendientes = Int(rd, "DisposicionesPendientes"), ReliberacionesPendientes = Int(rd, "ReliberacionesPendientes") };
            return result;
        }
        private static async Task<Dictionary<int, ParejaDto>> CargarParejasLhRhAsync(SqlConnection cn)
        {
            const string sql = @"
SELECT a.ProgramaProduccionID,grupo.GrupoLhRh,pareja.ProgramaProduccionID AS ProgramaParejaID,pareja.SolicitudProduccionID AS SolicitudParejaID,
       COALESCE(NULLIF(sp.NumeroOFRecibida,N''),NULLIF(sp.FolioSolicitud,N'')) AS OFPareja,pareja.NumeroParte AS NumeroPartePareja,pareja.ReferenciaSAP AS ReferenciaSAPPareja,
       ISNULL(pareja.EstatusID,1) AS EstatusProgramaParejaID,ep.EjecucionProduccionID AS EjecucionParejaID,ep.EstatusID AS EstatusEjecucionParejaID,
       CONVERT(INT,ISNULL(pareja.CantidadProgramada,0)) AS CantidadProgramadaPareja,CONVERT(INT,ISNULL(ep.CantidadOKTotal,ISNULL(pareja.CantidadProducida,0))) AS CantidadProducidaPareja,
       CONVERT(bit,CASE WHEN origen.MaquinaID=pareja.MaquinaID THEN 1 ELSE 0 END) AS MismaMaquina,
       CONVERT(bit,CASE WHEN origen.MoldeID IS NOT NULL AND pareja.MoldeID IS NOT NULL THEN CASE WHEN origen.MoldeID=pareja.MoldeID THEN 1 ELSE 0 END WHEN UPPER(LTRIM(RTRIM(ISNULL(origen.MoldeCodigo,N''))))=UPPER(LTRIM(RTRIM(ISNULL(pareja.MoldeCodigo,N'')))) THEN 1 ELSE 0 END) AS MismoMolde,
       CONVERT(bit,CASE WHEN origen.FechaInicioProgramada=pareja.FechaInicioProgramada AND ISNULL(origen.FechaFinProgramada,'19000101')=ISNULL(pareja.FechaFinProgramada,'19000101') THEN 1 ELSE 0 END) AS MismaVentana
FROM #AgendaProgramas a
INNER JOIN dbo.Planeacion_ProgramaProduccion origen ON origen.ProgramaProduccionID=a.ProgramaProduccionID AND origen.Activo=1
OUTER APPLY(SELECT CHARINDEX(N'NSQ_LHRH_PAIR:',ISNULL(origen.Observaciones,N'')) AS PosGrupo) pos
OUTER APPLY(SELECT CASE WHEN pos.PosGrupo>0 THEN TRY_CONVERT(INT,LEFT(SUBSTRING(origen.Observaciones,pos.PosGrupo+LEN(N'NSQ_LHRH_PAIR:'),50),CHARINDEX(N';',SUBSTRING(origen.Observaciones,pos.PosGrupo+LEN(N'NSQ_LHRH_PAIR:'),50)+N';')-1)) ELSE NULL END AS GrupoLhRh) grupo
INNER JOIN dbo.Planeacion_ProgramaProduccion pareja ON pareja.Activo=1 AND pareja.ProgramaProduccionID<>origen.ProgramaProduccionID AND grupo.GrupoLhRh IS NOT NULL AND pareja.Observaciones LIKE N'%NSQ_LHRH_PAIR:'+CONVERT(NVARCHAR(20),grupo.GrupoLhRh)+N';%'
LEFT JOIN dbo.SolicitudesProduccion sp ON sp.SolicitudProduccionID=pareja.SolicitudProduccionID AND sp.Activo=1
OUTER APPLY(SELECT TOP(1) e.EjecucionProduccionID,e.EstatusID,e.CantidadOKTotal FROM dbo.Produccion_Ejecucion e WHERE e.ProgramaProduccionID=pareja.ProgramaProduccionID AND e.Activo=1 ORDER BY e.EjecucionProduccionID DESC) ep;";
            var result = new Dictionary<int, ParejaDto>();
            await using var cmd = new SqlCommand(sql, cn);
            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                var id = Int(rd, "ProgramaProduccionID");
                if (result.ContainsKey(id)) continue;
                result[id] = new ParejaDto { GrupoLhRh = NInt(rd, "GrupoLhRh"), ProgramaParejaID = Int(rd, "ProgramaParejaID"), SolicitudParejaID = NInt(rd, "SolicitudParejaID"), OFPareja = Txt(rd, "OFPareja"), NumeroPartePareja = Txt(rd, "NumeroPartePareja"), ReferenciaSAPPareja = Txt(rd, "ReferenciaSAPPareja"), EstatusProgramaParejaID = Int(rd, "EstatusProgramaParejaID"), EjecucionParejaID = NInt(rd, "EjecucionParejaID"), EstatusEjecucionParejaID = NInt(rd, "EstatusEjecucionParejaID"), CantidadProgramadaPareja = Int(rd, "CantidadProgramadaPareja"), CantidadProducidaPareja = Int(rd, "CantidadProducidaPareja"), MismaMaquina = Bool(rd, "MismaMaquina"), MismoMolde = Bool(rd, "MismoMolde"), MismaVentana = Bool(rd, "MismaVentana") };
            }
            return result;
        }
        private static AgendaOperativaItemVm ConstruirItem(ProgramaBaseDto p, Dictionary<string, PreparacionDto>? preparaciones, InsumoDto? insumo, SecadoDto? secado, ChecklistDto? checklist, ConfiguracionDto? configuracion, CalidadDto? calidad, ParoDto? paro, CierreDto? cierre, ParejaDto? pareja, DateTime ahora)
        {
            var item = new AgendaOperativaItemVm
            {
                ProgramaProduccionID = p.ProgramaProduccionID,
                EjecucionProduccionID = p.EjecucionProduccionID,
                SolicitudProduccionID = p.SolicitudProduccionID,
                SolicitudProduccionDetalleID = p.SolicitudProduccionDetalleID,
                ReleaseDetalleID = p.ReleaseDetalleID,
                FolioSolicitud = p.NumeroOF,
                NumeroOF = p.NumeroOF,
                ParteID = p.ParteID,
                NumeroParte = p.NumeroParte,
                ReferenciaSAP = p.ReferenciaSAP,
                DescripcionParte = p.DescripcionParte,
                MaquinaID = p.MaquinaID,
                MaquinaCodigo = p.MaquinaCodigo,
                MaquinaNombre = p.MaquinaNombre,
                MoldeID = p.MoldeID,
                MoldeCodigo = p.MoldeCodigo,
                CantidadProgramada = p.CantidadProgramada,
                CantidadProducida = p.CantidadProducida,
                FechaInicioProgramada = p.FechaInicioProgramada,
                FechaFinProgramada = p.FechaFinProgramada,
                FechaInicioReal = p.FechaInicioReal,
                FechaFinReal = p.FechaFinReal,
                EstatusProgramaID = p.EstatusProgramaID,
                EstatusEjecucionID = p.EstatusEjecucionID,
                MaquinaLiberada = p.FechaLiberacionMaquina.HasValue,
                EsUrgente = p.EsUrgente
            };
            AgregarResponsables(item, p, configuracion);
            if (pareja != null)
            {
                var consistente = pareja.MismaMaquina && pareja.MismoMolde && pareja.MismaVentana;
                item.ProduccionLhRh = new AgendaOperativaLhRhVm { EsPareja = true, GrupoLhRh = pareja.GrupoLhRh, ProgramaActualID = p.ProgramaProduccionID, ProgramaParejaID = pareja.ProgramaParejaID, EjecucionActualID = p.EjecucionProduccionID, EjecucionParejaID = pareja.EjecucionParejaID, SolicitudParejaID = pareja.SolicitudParejaID, OFPareja = pareja.OFPareja, NumeroPartePareja = pareja.NumeroPartePareja, ReferenciaSAPPareja = pareja.ReferenciaSAPPareja, EstadoPareja = pareja.EstatusEjecucionParejaID.HasValue ? ProduccionEstatus.Nombre(pareja.EstatusEjecucionParejaID.Value) : ProgramaProduccionEstatus.Nombre(pareja.EstatusProgramaParejaID), CantidadProgramadaPareja = pareja.CantidadProgramadaPareja, CantidadProducidaPareja = pareja.CantidadProducidaPareja, ParejaConsistente = consistente, MotivoInconsistencia = consistente ? null : "La pareja LH/RH ya no conserva la misma máquina, molde y ventana programada." };
            }
            else if (p.GrupoLhRh.HasValue)
            {
                item.ProduccionLhRh = new AgendaOperativaLhRhVm { EsPareja = true, GrupoLhRh = p.GrupoLhRh, ProgramaActualID = p.ProgramaProduccionID, ProgramaParejaID = 0, EjecucionActualID = p.EjecucionProduccionID, ParejaConsistente = false, MotivoInconsistencia = $"El Programa {p.ProgramaProduccionID} tiene la marca LH/RH grupo {p.GrupoLhRh.Value}, pero no se encontró una contraparte activa." };
            }
            if (paro != null)
            {
                var duracion = (int)Math.Max(0, Math.Floor(((paro.FechaFin ?? ahora) - paro.FechaInicio).TotalMinutes));
                item.Interrupcion = new AgendaOperativaInterrupcionVm { ParoID = paro.ParoID, EsInterrupcionUrgente = paro.EsInterrupcionUrgente, EsParoLhRh = paro.EsParoLhRh, GrupoParoLhRh = paro.GrupoParoLhRh, ProgramaUrgenteID = paro.ProgramaUrgenteID, OFUrgente = paro.OFUrgente, FechaInicio = paro.FechaInicio, FechaFin = paro.FechaFin, DuracionMinutos = duracion, Motivo = paro.Motivo, PendienteReinicio = paro.FechaFin.HasValue, RequiereReliberacion = paro.EsMayorA15 || calidad?.RequiereReliberacion == true, RequiereCambioMoldeRetorno = paro.EsInterrupcionUrgente && preparaciones != null && preparaciones.TryGetValue("CAMBIO_MOLDE", out var cambioRetorno) && cambioRetorno.Observaciones?.Contains("NSQ_RETORNO_URGENTE:", StringComparison.OrdinalIgnoreCase) == true && cambioRetorno.Estado != "CONFIRMADA" };
            }
            item.Pasos = ConstruirPasos(p, preparaciones, insumo, secado, checklist, configuracion, calidad, paro, cierre, item, ahora);
            DeterminarEstadoYAcciones(item, p, calidad, paro, cierre, ahora);
            return item;
        }
        private static List<AgendaOperativaPasoVm> ConstruirPasos(ProgramaBaseDto p, Dictionary<string, PreparacionDto>? preparaciones, InsumoDto? insumo, SecadoDto? secado, ChecklistDto? checklist, ConfiguracionDto? configuracion, CalidadDto? calidad, ParoDto? paro, CierreDto? cierre, AgendaOperativaItemVm item, DateTime ahora)
        {
            var pasos = new List<AgendaOperativaPasoVm>();
            pasos.Add(EvaluarPlaneacion(p));
            pasos.Add(EvaluarPersonal(p));
            pasos.Add(EvaluarMaterial(p, insumo));
            pasos.Add(EvaluarSecado(p, secado));
            pasos.Add(EvaluarEmbalaje(p, preparaciones, insumo));
            pasos.Add(EvaluarCambioMolde(p, preparaciones));
            pasos.Add(EvaluarInicioPreparacion(p, item));
            pasos.Add(EvaluarChecklist(p, checklist, item));
            pasos.Add(EvaluarConfiguracion(configuracion, item));
            pasos.Add(EvaluarPrimerasPiezas(calidad, item));
            pasos.Add(EvaluarCalidad(calidad, item));
            pasos.Add(EvaluarInicioSerie(p, calidad, configuracion, item));
            pasos.Add(EvaluarProduccion(p, item));
            pasos.Add(EvaluarParo(paro, item));
            pasos.Add(EvaluarCapturas(cierre, item));
            pasos.Add(EvaluarCajas(cierre, item));
            pasos.Add(EvaluarCalidadFinal(calidad, cierre, item));
            pasos.Add(EvaluarLiberacion(p, paro, cierre, item));
            pasos.Add(EvaluarCierre(p, cierre, item));
            foreach (var paso in pasos) CompletarTiempoPaso(paso, ahora);
            return pasos;
        }
        private static AgendaOperativaPasoVm EvaluarPlaneacion(ProgramaBaseDto p)
        {
            var completo = p.MaquinaID.HasValue && p.FechaInicioProgramada.HasValue;
            return Paso(10, AgendaOperativaPasoClave.Planeacion, "Planeación de la OF", AgendaOperativaArea.Planeacion, completo ? AgendaOperativaEstadoPaso.Completado : AgendaOperativaEstadoPaso.Bloqueado, true, completo, false, !completo, "La OF debe tener máquina y fecha programadas antes de continuar.", p.FechaInicioProgramada, "PlaneacionCalendarioMaquinas", "Index", p.ProgramaProduccionID);
        }
        private static AgendaOperativaPasoVm EvaluarPersonal(ProgramaBaseDto p)
        {
            var completo = p.OperadorPrincipalID.HasValue || !string.IsNullOrWhiteSpace(p.OperadorPrincipalNombre);
            var detalle = completo ? $"Operador principal: {p.OperadorPrincipalNombre ?? $"Persona {p.OperadorPrincipalID}"}." : "Todavía no existe un operador principal asignado a la OF.";
            return Paso(20, AgendaOperativaPasoClave.Personal, "Personal asignado", AgendaOperativaArea.Produccion, completo ? AgendaOperativaEstadoPaso.Completado : AgendaOperativaEstadoPaso.Pendiente, true, completo, false, !completo, detalle, p.FechaInicioProgramada?.AddMinutes(-15), "ProduccionPersonal", "Index", p.ProgramaProduccionID);
        }
        private static AgendaOperativaPasoVm EvaluarMaterial(ProgramaBaseDto p, InsumoDto? i)
        {
            var requerido = i?.CantidadMpRequerida ?? p.CantidadMpKg.GetValueOrDefault();
            if (requerido <= ToleranciaCantidad) return PasoNoAplica(30, AgendaOperativaPasoClave.Material, "Materia prima", AgendaOperativaArea.Materiales, "La OF no tiene cantidad de MP requerida configurada.");
            var recibido = i?.CantidadMpRecibida ?? 0m;
            var completo = recibido + ToleranciaCantidad >= requerido;
            var estado = completo ? AgendaOperativaEstadoPaso.Completado : recibido > ToleranciaCantidad ? AgendaOperativaEstadoPaso.EnProceso : AgendaOperativaEstadoPaso.Pendiente;
            return Paso(30, AgendaOperativaPasoClave.Material, "Materia prima", AgendaOperativaArea.Materiales, estado, true, completo, recibido > ToleranciaCantidad && !completo, false, $"Recibido en Producción: {recibido:0.####} de {requerido:0.####} kg. Actualmente es informativo y no se convierte en bloqueo automático de arranque.", p.FechaInicioProgramada?.AddHours(-Math.Max(1, (double)p.HorasSecado.GetValueOrDefault())), "ProduccionPreparacion", "Materiales", p.ProgramaProduccionID);
        }
        private static AgendaOperativaPasoVm EvaluarSecado(ProgramaBaseDto p, SecadoDto? s)
        {
            var aplica = !string.IsNullOrWhiteSpace(p.TipoSecado) && p.HorasSecado.GetValueOrDefault() > 0m;
            if (!aplica) return PasoNoAplica(40, AgendaOperativaPasoClave.Secado, "Secado de material", AgendaOperativaArea.Secado, "La OF no tiene secado configurado.");
            if (s == null || s.Total <= 0) return Paso(40, AgendaOperativaPasoClave.Secado, "Secado de material", AgendaOperativaArea.Secado, AgendaOperativaEstadoPaso.Pendiente, true, false, false, false, $"Secado requerido: {p.HorasSecado.GetValueOrDefault():0.##} h. Aún no existe carga de secado registrada. Actualmente es informativo y no bloquea el arranque por sí solo.", p.FechaInicioProgramada?.AddHours(-(double)p.HorasSecado.GetValueOrDefault()), "ProduccionPreparacion", "Secado", p.ProgramaProduccionID);
            var completo = s.Finalizados == s.Total;
            var enProceso = s.EnProceso > 0;
            var estado = completo ? AgendaOperativaEstadoPaso.Completado : enProceso ? AgendaOperativaEstadoPaso.EnProceso : AgendaOperativaEstadoPaso.Pendiente;
            return Paso(40, AgendaOperativaPasoClave.Secado, "Secado de material", AgendaOperativaArea.Secado, estado, true, completo, enProceso, false, completo ? "Todas las cargas de secado relacionadas con la OF están finalizadas." : $"Secado: {s.Finalizados}/{s.Total} registro(s) finalizado(s). Actualmente es informativo y no bloquea el arranque por sí solo.", s.FechaFinObjetivo ?? p.FechaInicioProgramada, "ProduccionPreparacion", "Secado", p.ProgramaProduccionID);
        }
        private static AgendaOperativaPasoVm EvaluarEmbalaje(ProgramaBaseDto p, Dictionary<string, PreparacionDto>? preparaciones, InsumoDto? i)
        {
            var requerido = i?.CantidadEmbalajeRequerida ?? p.CantidadEmbalajes.GetValueOrDefault();
            if (requerido <= ToleranciaCantidad) return PasoNoAplica(50, AgendaOperativaPasoClave.Embalaje, "Preparación de embalaje", AgendaOperativaArea.Embalaje, "La OF no requiere embalaje anticipado configurado.");
            PreparacionDto? tarea = null;
            if (preparaciones != null) preparaciones.TryGetValue("PREPARAR_EMBALAJE", out tarea);
            var recibido = i?.CantidadEmbalajeRecibida ?? 0m;
            var confirmado = string.Equals(tarea?.Estado, "CONFIRMADA", StringComparison.OrdinalIgnoreCase);
            var enProceso = string.Equals(tarea?.Estado, "EN_PROCESO", StringComparison.OrdinalIgnoreCase);
            var estado = confirmado ? AgendaOperativaEstadoPaso.Completado : enProceso ? AgendaOperativaEstadoPaso.EnProceso : AgendaOperativaEstadoPaso.Pendiente;
            var detalle = $"Embalaje recibido: {recibido:0.####} de {requerido:0.####}. Preparación: {tarea?.Estado ?? "PENDIENTE"}. Actualmente no se usa como bloqueo automático de inicio de Producción.";
            return Paso(50, AgendaOperativaPasoClave.Embalaje, "Preparación de embalaje", AgendaOperativaArea.Embalaje, estado, true, confirmado, enProceso, false, detalle, tarea?.FechaObjetivo ?? p.FechaInicioProgramada?.AddHours(-2), "ProduccionPreparacion", "Embalajes", p.ProgramaProduccionID);
        }
        private static AgendaOperativaPasoVm EvaluarCambioMolde(ProgramaBaseDto p, Dictionary<string, PreparacionDto>? preparaciones)
        {
            if (!p.RequiereCambioMolde) return PasoNoAplica(60, AgendaOperativaPasoClave.CambioMolde, "Cambio de molde", AgendaOperativaArea.Smed, "La secuencia no requiere cambio de molde para esta OF.");
            PreparacionDto? tarea = null;
            if (preparaciones != null) preparaciones.TryGetValue("CAMBIO_MOLDE", out tarea);
            var confirmado = string.Equals(tarea?.Estado, "CONFIRMADA", StringComparison.OrdinalIgnoreCase);
            var enProceso = string.Equals(tarea?.Estado, "EN_PROCESO", StringComparison.OrdinalIgnoreCase);
            var estado = confirmado ? AgendaOperativaEstadoPaso.Completado : enProceso ? AgendaOperativaEstadoPaso.EnProceso : AgendaOperativaEstadoPaso.Pendiente;
            var detalle = enProceso ? "El cambio de molde ya está en proceso." : confirmado ? "Cambio de molde confirmado." : "El cambio de molde es obligatorio y debe confirmarse antes de iniciar preparación.";
            return Paso(60, AgendaOperativaPasoClave.CambioMolde, "Cambio de molde", AgendaOperativaArea.Smed, estado, true, confirmado, enProceso, !confirmado, detalle, tarea?.FechaObjetivo ?? p.FechaInicioProgramada, "ProduccionPreparacion", "CambioMolde", p.ProgramaProduccionID);
        }
        private static AgendaOperativaPasoVm EvaluarInicioPreparacion(ProgramaBaseDto p, AgendaOperativaItemVm item)
        {
            var completo = p.EjecucionProduccionID.HasValue;
            return Paso(70, "INICIAR_PREPARACION", "Iniciar preparación de Producción", AgendaOperativaArea.Produccion, completo ? AgendaOperativaEstadoPaso.Completado : AgendaOperativaEstadoPaso.Pendiente, true, completo, false, !completo, completo ? "La ejecución de Producción ya fue creada." : "La OF todavía no tiene una ejecución de Producción. Inicia preparación cuando los bloqueos obligatorios estén resueltos.", p.FechaInicioProgramada, "Produccion", "Index", item.ProgramaProduccionID);
        }
        private static AgendaOperativaPasoVm EvaluarChecklist(ProgramaBaseDto p, ChecklistDto? c, AgendaOperativaItemVm item)
        {
            if (!p.EjecucionProduccionID.HasValue) return PasoEsperando(80, AgendaOperativaPasoClave.ChecklistArranque, "Checklist de arranque", AgendaOperativaArea.TecnicoProduccion, "Se habilita al iniciar la preparación.");
            if (c == null) return Paso(80, AgendaOperativaPasoClave.ChecklistArranque, "Checklist de arranque", AgendaOperativaArea.TecnicoProduccion, AgendaOperativaEstadoPaso.Pendiente, true, false, false, true, "No existe todavía el checklist de arranque/liberación GQ-F-PR01-06.", p.FechaInicioProgramada, "Produccion", "Detalle", item.EjecucionProduccionID);
            var validado = c.EstatusID == ProduccionChecklistEstatus.ValidadoPorCalidad;
            var pendienteCalidad = c.EstatusID == ProduccionChecklistEstatus.PendienteValidacionCalidad;
            var estado = validado ? AgendaOperativaEstadoPaso.Completado : pendienteCalidad ? AgendaOperativaEstadoPaso.Esperando : AgendaOperativaEstadoPaso.EnProceso;
            var area = pendienteCalidad ? AgendaOperativaArea.Calidad : AgendaOperativaArea.TecnicoProduccion;
            return Paso(80, AgendaOperativaPasoClave.ChecklistArranque, "Checklist de arranque", area, estado, true, validado, !validado && !pendienteCalidad, !validado, validado ? "Checklist validado por Calidad." : pendingText(c), p.FechaInicioProgramada, "Produccion", "Detalle", item.EjecucionProduccionID);
            static string pendingText(ChecklistDto x) => x.EstatusID == ProduccionChecklistEstatus.PendienteValidacionCalidad ? "Producción terminó la captura; falta validación de Calidad." : "El checklist todavía requiere captura o corrección antes de la validación.";
        }
        private static AgendaOperativaPasoVm EvaluarConfiguracion(ConfiguracionDto? c, AgendaOperativaItemVm item)
        {
            if (!item.EjecucionProduccionID.HasValue) return PasoEsperando(90, AgendaOperativaPasoClave.ConfiguracionCorrida, "Configuración técnica de corrida", AgendaOperativaArea.TecnicoProduccion, "Se habilita al iniciar la preparación.");
            var completo = c != null && c.CavidadesUsadas > 0 && c.TiempoCicloSegundos > 0 && c.ContadorInicioVigencia.HasValue;
            var detalle = completo ? $"{c!.CavidadesUsadas} cavidad(es), {c.TiempoCicloSegundos:0.####} s, contador base {c.ContadorInicioVigencia:N0}." : "Falta confirmar cavidades reales, tiempo de ciclo y contador base de la máquina.";
            return Paso(90, AgendaOperativaPasoClave.ConfiguracionCorrida, "Configuración técnica de corrida", AgendaOperativaArea.TecnicoProduccion, completo ? AgendaOperativaEstadoPaso.Completado : AgendaOperativaEstadoPaso.Pendiente, true, completo, false, !completo, detalle, item.FechaInicioProgramada, "Produccion", "Detalle", item.EjecucionProduccionID);
        }
        private static AgendaOperativaPasoVm EvaluarPrimerasPiezas(CalidadDto? c, AgendaOperativaItemVm item)
        {
            if (!item.EjecucionProduccionID.HasValue) return PasoEsperando(100, AgendaOperativaPasoClave.PrimerasPiezas, "Primeras piezas", AgendaOperativaArea.Calidad, "Se habilita después de iniciar preparación y generar la inspección.");
            if (c == null) return Paso(100, AgendaOperativaPasoClave.PrimerasPiezas, "Primeras piezas", AgendaOperativaArea.Calidad, AgendaOperativaEstadoPaso.Esperando, true, false, false, true, "Todavía no existe una inspección activa de Calidad.", item.FechaInicioProgramada, "Produccion", "Detalle", item.EjecucionProduccionID);
            var completo = c.CincoDisparosSegregados || c.CantidadDisparosConformes >= 5 || c.EstaLiberada;
            var enProceso = !completo && c.CantidadDisparosConformes > 0;
            return Paso(100, AgendaOperativaPasoClave.PrimerasPiezas, "Primeras piezas", AgendaOperativaArea.Calidad, completo ? AgendaOperativaEstadoPaso.Completado : enProceso ? AgendaOperativaEstadoPaso.EnProceso : AgendaOperativaEstadoPaso.Pendiente, true, completo, enProceso, !completo, completo ? "Primeras piezas registradas para la inspección." : $"Disparos conformes registrados: {c.CantidadDisparosConformes}. Calidad debe completar la validación de primeras piezas.", item.FechaInicioProgramada, "Calidad", "Detalle", c.InspeccionID);
        }
        private static AgendaOperativaPasoVm EvaluarCalidad(CalidadDto? c, AgendaOperativaItemVm item)
        {
            if (!item.EjecucionProduccionID.HasValue) return PasoEsperando(110, AgendaOperativaPasoClave.Calidad, "Liberación de Calidad", AgendaOperativaArea.Calidad, "Se habilita después del checklist y las primeras piezas.");
            if (c == null) return Paso(110, AgendaOperativaPasoClave.Calidad, "Liberación de Calidad", AgendaOperativaArea.Calidad, AgendaOperativaEstadoPaso.Esperando, true, false, false, true, "No existe una inspección activa de Calidad.", item.FechaInicioProgramada, "Produccion", "Detalle", item.EjecucionProduccionID);
            if (c.ConfiguracionInvalidada) return Paso(110, AgendaOperativaPasoClave.Calidad, "Liberación de Calidad", AgendaOperativaArea.Calidad, AgendaOperativaEstadoPaso.Bloqueado, true, false, false, true, "Calidad invalidó la configuración autorizada. Producción debe corregirla antes de continuar.", item.FechaInicioProgramada, "Calidad", "Detalle", c.InspeccionID);
            if (c.RequiereReliberacion)
            {
                var autorizada = string.Equals(c.ResultadoReliberacion, "AUTORIZADA", StringComparison.OrdinalIgnoreCase);
                var rechazada = string.Equals(c.ResultadoReliberacion, "RECHAZADA", StringComparison.OrdinalIgnoreCase);
                return Paso(110, AgendaOperativaPasoClave.Calidad, "Reliberación de Calidad", AgendaOperativaArea.Calidad, autorizada ? AgendaOperativaEstadoPaso.Completado : rechazada ? AgendaOperativaEstadoPaso.Bloqueado : AgendaOperativaEstadoPaso.Esperando, true, autorizada, false, !autorizada, autorizada ? "Calidad autorizó la reliberación." : rechazada ? "La reliberación fue rechazada; Producción debe corregir y presentar nuevamente las piezas." : "La reliberación de Calidad continúa pendiente.", item.FechaInicioProgramada, "Calidad", "Detalle", c.InspeccionID);
            }
            return Paso(110, AgendaOperativaPasoClave.Calidad, "Liberación de Calidad", AgendaOperativaArea.Calidad, c.EstaLiberada ? AgendaOperativaEstadoPaso.Completado : AgendaOperativaEstadoPaso.Esperando, true, c.EstaLiberada, false, !c.EstaLiberada, c.EstaLiberada ? "Calidad liberó la producción con resultado y etiqueta verde." : !string.IsNullOrWhiteSpace(c.MotivoDevolucion) ? c.MotivoDevolucion : "Calidad todavía no ha liberado la producción.", item.FechaInicioProgramada, "Calidad", "Detalle", c.InspeccionID);
        }
        private static AgendaOperativaPasoVm EvaluarInicioSerie(ProgramaBaseDto p, CalidadDto? c, ConfiguracionDto? config, AgendaOperativaItemVm item)
        {
            var inicioReal = p.EstatusEjecucionID is ProduccionEstatus.EnProduccion or ProduccionEstatus.Pausado or ProduccionEstatus.TerminadoParcial or ProduccionEstatus.Terminado;
            if (inicioReal) return Paso(120, AgendaOperativaPasoClave.InicioSerie, "Inicio de serie", AgendaOperativaArea.Produccion, AgendaOperativaEstadoPaso.Completado, true, true, false, false, "La producción en serie ya fue iniciada.", p.FechaInicioReal, "Produccion", "Detalle", item.EjecucionProduccionID);
            if (!item.EjecucionProduccionID.HasValue) return PasoEsperando(120, AgendaOperativaPasoClave.InicioSerie, "Inicio de serie", AgendaOperativaArea.Produccion, "Se habilita después de iniciar preparación.");
            var lista = c?.EstaLiberada == true && config != null && config.CavidadesUsadas > 0 && config.TiempoCicloSegundos > 0 && config.ContadorInicioVigencia.HasValue;
            return Paso(120, AgendaOperativaPasoClave.InicioSerie, "Iniciar producción en serie", AgendaOperativaArea.Produccion, lista ? AgendaOperativaEstadoPaso.Listo : AgendaOperativaEstadoPaso.Esperando, true, false, false, !lista, lista ? "Calidad y configuración técnica están listas. Producción puede confirmar el inicio o reinicio de serie." : "Falta completar Calidad y/o la configuración técnica antes de iniciar serie.", p.FechaInicioProgramada, "Produccion", "Detalle", item.EjecucionProduccionID);
        }
        private static AgendaOperativaPasoVm EvaluarProduccion(ProgramaBaseDto p, AgendaOperativaItemVm item)
        {
            if (item.MaquinaLiberada || p.EstatusEjecucionID is ProduccionEstatus.TerminadoParcial or ProduccionEstatus.Terminado) return Paso(130, AgendaOperativaPasoClave.Produccion, "Producción en serie", AgendaOperativaArea.Produccion, AgendaOperativaEstadoPaso.Completado, true, true, false, false, "La operación física de la máquina para esta ejecución ya concluyó.", p.FechaFinReal, "Produccion", "Detalle", item.EjecucionProduccionID);
            if (p.EstatusEjecucionID == ProduccionEstatus.EnProduccion) return Paso(130, AgendaOperativaPasoClave.Produccion, "Producción en serie", AgendaOperativaArea.Produccion, AgendaOperativaEstadoPaso.EnProceso, true, false, true, false, $"Producción activa. Avance OK registrado: {item.CantidadProducida:N0} de {item.CantidadProgramada:N0} pieza(s) programadas.", p.FechaFinProgramada, "Produccion", "Detalle", item.EjecucionProduccionID);
            return PasoEsperando(130, AgendaOperativaPasoClave.Produccion, "Producción en serie", AgendaOperativaArea.Produccion, "La serie todavía no está activa.");
        }
        private static AgendaOperativaPasoVm EvaluarParo(ParoDto? p, AgendaOperativaItemVm item)
        {
            if (p == null || p.FechaFin.HasValue) return PasoNoAplica(140, AgendaOperativaPasoClave.Paro, "Paro / interrupción", AgendaOperativaArea.Produccion, "No existe un paro abierto.");
            var area = p.EsInterrupcionUrgente ? AgendaOperativaArea.Planeacion : AgendaOperativaArea.Produccion;
            var detalle = p.EsInterrupcionUrgente ? $"Producción interrumpida por prioridad de Planeación. OF urgente: {p.OFUrgente ?? p.ProgramaUrgenteID?.ToString() ?? "sin referencia"}. {p.Motivo}" : p.Motivo ?? "Existe un paro abierto.";
            return Paso(140, AgendaOperativaPasoClave.Paro, "Paro / interrupción activa", area, AgendaOperativaEstadoPaso.Bloqueado, true, false, true, true, detalle, p.FechaInicio, "Produccion", "Detalle", item.EjecucionProduccionID);
        }
        private static AgendaOperativaPasoVm EvaluarCapturas(CierreDto? c, AgendaOperativaItemVm item)
        {
            if (!item.EjecucionProduccionID.HasValue) return PasoEsperando(150, AgendaOperativaPasoClave.Capturas, "Capturas de producción", AgendaOperativaArea.Operador, "Se habilitan cuando la serie está activa.");
            if (item.MaquinaLiberada) return Paso(150, AgendaOperativaPasoClave.Capturas, "Capturas de producción", AgendaOperativaArea.Operador, AgendaOperativaEstadoPaso.Completado, true, true, false, false, $"Se registraron {c?.RegistrosNormales ?? 0} captura(s) normal(es).", item.FechaFinReal, "Produccion", "Detalle", item.EjecucionProduccionID);
            if (item.EstatusEjecucionID == ProduccionEstatus.EnProduccion) return Paso(150, AgendaOperativaPasoClave.Capturas, "Capturas de producción", AgendaOperativaArea.Operador, c?.RegistrosNormales > 0 ? AgendaOperativaEstadoPaso.EnProceso : AgendaOperativaEstadoPaso.Pendiente, true, false, c?.RegistrosNormales > 0, false, $"Capturas normales registradas: {c?.RegistrosNormales ?? 0}. Minutos productivos acumulados: {(c?.MinutosNormalesCapturados ?? 0m):0.##}.", DateTime.Now, "Produccion", "Detalle", item.EjecucionProduccionID);
            return PasoEsperando(150, AgendaOperativaPasoClave.Capturas, "Capturas de producción", AgendaOperativaArea.Operador, "La producción en serie todavía no está activa.");
        }
        private static AgendaOperativaPasoVm EvaluarCajas(CierreDto? c, AgendaOperativaItemVm item)
        {
            if (!item.EjecucionProduccionID.HasValue) return PasoEsperando(160, AgendaOperativaPasoClave.Cajas, "Cajas de producción", AgendaOperativaArea.Produccion, "Se habilitan conforme se genera producto durante la corrida.");
            if (c == null) return PasoEsperando(160, AgendaOperativaPasoClave.Cajas, "Cajas de producción", AgendaOperativaArea.Produccion, "Todavía no hay información de cajas para la ejecución.");
            var pendientesOk = Math.Max(0, c.CantidadOK - c.OkEnCajas);
            var pendientesSos = Math.Max(0, c.CantidadSospechosa - c.SospechosoEnCajas - c.RetencionEnCajas);
            var pendientesScrap = Math.Max(0, c.CantidadScrap - c.ScrapEnCajas);
            var completo = pendientesOk == 0 && pendientesSos == 0 && pendientesScrap == 0 && c.CajasFormadasPendientes == 0 && c.CajasPendientesCalidad == 0 && (item.MaquinaLiberada || item.EstatusEjecucionID is ProduccionEstatus.TerminadoParcial or ProduccionEstatus.Terminado);
            var detalle = $"Pendiente por asignar: OK {pendientesOk:N0}, retención/sospechoso {pendientesSos:N0}, scrap {pendientesScrap:N0}. Cajas sin enviar a Calidad: {c.CajasFormadasPendientes}. Pendientes de Calidad: {c.CajasPendientesCalidad}.";
            return Paso(160, AgendaOperativaPasoClave.Cajas, "Cajas de producción", AgendaOperativaArea.Produccion, completo ? AgendaOperativaEstadoPaso.Completado : AgendaOperativaEstadoPaso.EnProceso, true, completo, !completo, false, detalle, item.FechaFinProgramada, "Produccion", "Detalle", item.EjecucionProduccionID);
        }
        private static AgendaOperativaPasoVm EvaluarCalidadFinal(CalidadDto? calidad, CierreDto? c, AgendaOperativaItemVm item)
        {
            if (!item.EjecucionProduccionID.HasValue) return PasoEsperando(170, AgendaOperativaPasoClave.CalidadFinal, "Pendientes finales de Calidad", AgendaOperativaArea.Calidad, "Se evalúan durante y al cierre de la producción.");
            if (calidad == null) return Paso(170, AgendaOperativaPasoClave.CalidadFinal, "Pendientes finales de Calidad", AgendaOperativaArea.Calidad, AgendaOperativaEstadoPaso.Esperando, true, false, false, true, "No existe inspección de Calidad relacionada con la ejecución.", item.FechaFinProgramada, "Produccion", "Detalle", item.EjecucionProduccionID);
            var pendientes = (c?.MonitoreosPendientes ?? 0) + (c?.DisposicionesPendientes ?? 0) + (c?.ReliberacionesPendientes ?? 0);
            var completo = pendientes == 0 && !calidad.ConfiguracionInvalidada && !calidad.RequiereReliberacion;
            var detalle = completo ? "No existen monitoreos, disposiciones o reliberaciones pendientes en el estado actual." : $"Monitoreos pendientes: {c?.MonitoreosPendientes ?? 0}; disposiciones: {c?.DisposicionesPendientes ?? 0}; reliberaciones: {c?.ReliberacionesPendientes ?? 0}.";
            var bloquea = item.MaquinaLiberada && !completo;
            return Paso(170, AgendaOperativaPasoClave.CalidadFinal, "Pendientes finales de Calidad", AgendaOperativaArea.Calidad, completo ? AgendaOperativaEstadoPaso.Completado : AgendaOperativaEstadoPaso.Esperando, true, completo, false, bloquea, detalle, item.FechaFinProgramada, "Calidad", "Detalle", calidad.InspeccionID);
        }
        private static AgendaOperativaPasoVm EvaluarLiberacion(ProgramaBaseDto p, ParoDto? paro, CierreDto? c, AgendaOperativaItemVm item)
        {
            if (item.MaquinaLiberada) return Paso(180, AgendaOperativaPasoClave.LiberacionMaquina, "Liberación de máquina", AgendaOperativaArea.Produccion, AgendaOperativaEstadoPaso.Completado, true, true, false, false, "La máquina ya fue liberada para esta ejecución.", p.FechaLiberacionMaquina, "Produccion", "Detalle", item.EjecucionProduccionID);
            if (p.EstatusEjecucionID != ProduccionEstatus.EnProduccion) return PasoEsperando(180, AgendaOperativaPasoClave.LiberacionMaquina, "Liberación de máquina", AgendaOperativaArea.Produccion, "Solo se habilita cuando la ejecución está en producción.");
            var tieneParo = paro != null && !paro.FechaFin.HasValue;
            var minutosRequeridos = p.CantidadProgramada > 0 && c?.ObjetivoHora > 0 ? Math.Ceiling(p.CantidadProgramada * 60m / c.ObjetivoHora.Value) : 0m;
            var cumpleTiempo = c != null && c.RegistrosNormales > 0 && (p.CantidadProgramada <= 0 || (c.ObjetivoHora > 0 && c.MinutosNormalesCapturados + 0.01m >= minutosRequeridos));
            var puede = !tieneParo && c?.TieneTiempoExtraActivo != true && cumpleTiempo;
            var detalle = puede ? "La ejecución cumple las condiciones de captura para liberar físicamente la máquina." : tieneParo ? "No puede liberarse mientras exista un paro abierto." : c?.TieneTiempoExtraActivo == true ? "Existe una sesión de tiempo extra abierta." : c?.RegistrosNormales <= 0 ? "Todavía no existe ninguna captura normal de producción." : c?.ObjetivoHora <= 0 ? "No existe un objetivo por hora válido para calcular la liberación." : $"Minutos normales capturados: {(c?.MinutosNormalesCapturados ?? 0m):0.##} de {minutosRequeridos:0.##} requeridos.";
            return Paso(180, AgendaOperativaPasoClave.LiberacionMaquina, "Liberación de máquina", AgendaOperativaArea.Produccion, puede ? AgendaOperativaEstadoPaso.Listo : AgendaOperativaEstadoPaso.Esperando, true, false, false, false, detalle, p.FechaFinProgramada, "Produccion", "Detalle", item.EjecucionProduccionID);
        }
        private static AgendaOperativaPasoVm EvaluarCierre(ProgramaBaseDto p, CierreDto? c, AgendaOperativaItemVm item)
        {
            var terminado =
                p.EstatusEjecucionID is ProduccionEstatus.TerminadoParcial or ProduccionEstatus.Terminado
                || p.EstatusProgramaID == ProgramaProduccionEstatus.Terminado;

            if (terminado)
            {
                return Paso(
                    190,
                    AgendaOperativaPasoClave.Cierre,
                    "Cierre de Producción",
                    AgendaOperativaArea.Produccion,
                    AgendaOperativaEstadoPaso.Completado,
                    true,
                    true,
                    false,
                    false,
                    "La ejecución ya está terminada.",
                    p.FechaFinReal,
                    "Produccion",
                    "Detalle",
                    item.EjecucionProduccionID);
            }

            if (!item.MaquinaLiberada)
            {
                return PasoEsperando(
                    190,
                    AgendaOperativaPasoClave.Cierre,
                    "Cierre de Producción",
                    AgendaOperativaArea.Produccion,
                    "El cierre queda disponible después de la liberación física de la máquina y la resolución de pendientes.");
            }

            var pendientes =
                (c?.MonitoreosPendientes ?? 0) +
                (c?.DisposicionesPendientes ?? 0) +
                (c?.ReliberacionesPendientes ?? 0) +
                (c?.CajasFormadasPendientes ?? 0) +
                (c?.CajasPendientesCalidad ?? 0);

            var listo = pendientes == 0;

            return Paso(
                190,
                AgendaOperativaPasoClave.Cierre,
                "Cierre de Producción",
                AgendaOperativaArea.Produccion,
                listo ? AgendaOperativaEstadoPaso.Listo : AgendaOperativaEstadoPaso.Esperando,
                true,
                false,
                false,
                false,
                listo
                    ? "La máquina está liberada y no se detectan pendientes operativos en el resumen actual."
                    : $"Todavía existen {pendientes} pendiente(s) de cajas/Calidad antes del cierre.",
                p.FechaFinProgramada,
                "Produccion",
                "Detalle",
                item.EjecucionProduccionID);
        }
        private static void DeterminarEstadoYAcciones(AgendaOperativaItemVm item, ProgramaBaseDto p, CalidadDto? calidad, ParoDto? paro, CierreDto? cierre, DateTime ahora)
        {
            if (item.ProduccionLhRh?.ParejaConsistente == false)
            {
                item.EstadoGeneral = AgendaOperativaEstadoGeneral.Bloqueada;
                item.EstadoGeneralDetalle = item.ProduccionLhRh.MotivoInconsistencia;
                item.EstaBloqueada = true;
            }
            else if (paro?.EsInterrupcionUrgente == true && !paro.FechaFin.HasValue)
            {
                item.EstadoGeneral = AgendaOperativaEstadoGeneral.InterrumpidaUrgente;
                item.EstadoGeneralDetalle = "Producción pausada por una prioridad urgente de Planeación.";
            }
            else if (paro != null && !paro.FechaFin.HasValue)
            {
                item.EstadoGeneral = AgendaOperativaEstadoGeneral.Pausada;
                item.EstadoGeneralDetalle = paro.Motivo;
            }
            else if (item.MaquinaLiberada)
            {
                item.EstadoGeneral = AgendaOperativaEstadoGeneral.MaquinaLiberada;
                item.EstadoGeneralDetalle = "La operación física concluyó; la ejecución puede conservar pendientes posteriores.";
            }
            else if (p.EstatusEjecucionID == ProduccionEstatus.EnProduccion)
            {
                item.EstadoGeneral = AgendaOperativaEstadoGeneral.Produciendo;
                item.EstadoGeneralDetalle = "Producción en serie activa.";
            }
            else if (p.EstatusEjecucionID == ProduccionEstatus.EnPreparacion)
            {
                if (calidad?.RequiereReliberacion == true || paro?.FechaFin.HasValue == true)
                {
                    item.EstadoGeneral = AgendaOperativaEstadoGeneral.Reliberacion;
                    item.EstadoGeneralDetalle = "La OF está preparando el reinicio después de una interrupción o paro.";
                }
                else if (calidad?.EstaLiberada == true)
                {
                    item.EstadoGeneral = AgendaOperativaEstadoGeneral.ListaParaSerie;
                    item.EstadoGeneralDetalle = "Calidad liberó y la OF puede avanzar al inicio de serie cuando la configuración esté lista.";
                }
                else
                {
                    var checklistListo = item.Pasos.FirstOrDefault(x => x.Clave == AgendaOperativaPasoClave.ChecklistArranque)?.Completado == true;
                    var configuracionLista = item.Pasos.FirstOrDefault(x => x.Clave == AgendaOperativaPasoClave.ConfiguracionCorrida)?.Completado == true;
                    if (checklistListo && configuracionLista)
                    {
                        item.EstadoGeneral = AgendaOperativaEstadoGeneral.EsperandoCalidad;
                        item.EstadoGeneralDetalle = "La preparación técnica está lista y la OF espera la liberación de Calidad.";
                    }
                    else
                    {
                        item.EstadoGeneral = AgendaOperativaEstadoGeneral.Preparacion;
                        item.EstadoGeneralDetalle = "La OF está en preparación.";
                    }
                }
            }
            else if (p.EstatusEjecucionID is ProduccionEstatus.TerminadoParcial or ProduccionEstatus.Terminado)
            {
                item.EstadoGeneral = AgendaOperativaEstadoGeneral.Terminada;
                item.EstadoGeneralDetalle = "La ejecución está terminada.";
            }
            else
            {
                item.EstadoGeneral = AgendaOperativaEstadoGeneral.Programada;
                item.EstadoGeneralDetalle = "La OF está programada y todavía no inicia ejecución.";
            }
            AgendaOperativaPasoVm? actual = null;
            if (item.ProduccionLhRh?.ParejaConsistente == false)
            {
                actual = new AgendaOperativaPasoVm { Orden = 0, Clave = "LHRH_INCONSISTENTE", Nombre = "Corregir pareja LH/RH", AreaResponsable = AgendaOperativaArea.Planeacion, Estado = AgendaOperativaEstadoPaso.Bloqueado, Aplica = true, Bloqueado = true, BloqueaFlujo = true, MotivoBloqueo = item.ProduccionLhRh.MotivoInconsistencia, Detalle = item.ProduccionLhRh.MotivoInconsistencia, FechaObjetivo = ahora, EstaVencido = true };
            }
            else if (paro != null && !paro.FechaFin.HasValue)
            {
                actual = item.Pasos.FirstOrDefault(x => x.Clave == AgendaOperativaPasoClave.Paro);
            }
            else if (cierre != null && p.EstatusEjecucionID == ProduccionEstatus.EnProduccion && !item.MaquinaLiberada)
            {
                var liberacion = item.Pasos.FirstOrDefault(x => x.Clave == AgendaOperativaPasoClave.LiberacionMaquina);
                if (liberacion?.Estado == AgendaOperativaEstadoPaso.Listo) actual = liberacion;
            }
            actual ??= item.Pasos.Where(x => x.Aplica && !x.Completado && !string.Equals(x.Estado, AgendaOperativaEstadoPaso.NoAplica, StringComparison.OrdinalIgnoreCase) && x.Clave != AgendaOperativaPasoClave.Paro).OrderBy(x => x.BloqueaFlujo ? 0 : 1).ThenBy(x => x.Orden).FirstOrDefault();
            var pendientesOrdenados = item.Pasos.Where(x => x.Aplica && !x.Completado && !string.Equals(x.Estado, AgendaOperativaEstadoPaso.NoAplica, StringComparison.OrdinalIgnoreCase) && x != actual).OrderBy(x => x.BloqueaFlujo ? 0 : 1).ThenBy(x => x.Orden).ToList();
            item.AccionActual = actual == null ? null : ConstruirAccion(item, actual, ahora);
            item.SiguienteAccion = pendientesOrdenados.FirstOrDefault() is { } siguiente ? ConstruirAccion(item, siguiente, ahora) : null;
            item.EstaBloqueada = item.EstaBloqueada || actual?.Bloqueado == true || actual?.Estado == AgendaOperativaEstadoPaso.Bloqueado;
            item.MotivoBloqueo = item.EstaBloqueada ? (actual?.MotivoBloqueo ?? actual?.Detalle ?? item.MotivoBloqueo) : null;
            item.MinutosDesfase = item.AccionActual?.MinutosDesfase ?? 0;
            var inicio = item.FechaInicioProgramada;
            item.EsProxima = !item.EjecucionProduccionID.HasValue && inicio.HasValue && inicio.Value > ahora && inicio.Value <= ahora.AddHours(2);
            item.Prioridad = CalcularPrioridad(item, actual, paro, ahora);
            item.RequiereAtencionInmediata = item.EstaBloqueada || item.Prioridad == AgendaOperativaPrioridad.Critica || item.Prioridad == AgendaOperativaPrioridad.Alta || (item.AccionActual?.FechaDisponibleDesde.HasValue == true && item.AccionActual.FechaDisponibleDesde.Value <= ahora);
        }
        private static AgendaOperativaAccionVm ConstruirAccion(AgendaOperativaItemVm item, AgendaOperativaPasoVm paso, DateTime ahora)
        {
            var fechaObjetivo = paso.FechaObjetivo;
            var minutos = fechaObjetivo.HasValue && fechaObjetivo.Value < ahora ? (int)Math.Floor((ahora - fechaObjetivo.Value).TotalMinutes) : 0;
            var responsable = item.Responsables.FirstOrDefault(x => string.Equals(x.Area, paso.AreaResponsable, StringComparison.OrdinalIgnoreCase));
            var accion = new AgendaOperativaAccionVm { Clave = paso.Clave, Titulo = TituloAccion(paso), Descripcion = paso.Detalle ?? paso.MotivoBloqueo, AreaResponsable = paso.AreaResponsable, ResponsableUsuarioID = responsable?.UsuarioID, ResponsableNombre = responsable?.Nombre, Prioridad = paso.Bloqueado ? AgendaOperativaPrioridad.Critica : minutos > 0 ? AgendaOperativaPrioridad.Alta : AgendaOperativaPrioridad.Normal, FechaObjetivo = fechaObjetivo, FechaDisponibleDesde = fechaObjetivo, EstaVencida = minutos > 0, MinutosDesfase = Math.Max(0, minutos), BloqueaFlujo = paso.BloqueaFlujo, EsEjecutable = !string.Equals(paso.Estado, AgendaOperativaEstadoPaso.Esperando, StringComparison.OrdinalIgnoreCase) || paso.Controlador != null, TextoBoton = TextoBoton(paso), Icono = IconoPaso(paso.Clave), Controlador = paso.Controlador, Accion = paso.Accion, IdDestino = paso.IdDestino };
            if (paso.Controlador == "Produccion" && paso.Accion == "Index")
            {
                accion.ParametrosRuta["busqueda"] = item.OFTexto;
                if (item.MaquinaID.HasValue) accion.ParametrosRuta["maquinaId"] = item.MaquinaID.Value.ToString();
            }
            else if (paso.Controlador == "ProduccionPreparacion")
            {
                accion.ParametrosRuta["filtro"] = item.OFTexto;
                if (item.MaquinaID.HasValue) accion.ParametrosRuta["maquinaId"] = item.MaquinaID.Value.ToString();
            }
            else if (paso.Controlador == "Produccion" && paso.Accion == "Detalle" && item.EjecucionProduccionID.HasValue)
            {
                accion.ParametroId = "id";
                accion.IdDestino = item.EjecucionProduccionID;
            }
            else if (paso.Controlador == "Calidad" && paso.Accion == "Detalle") accion.ParametroId = "id";
            return accion;
        }
        private static string CalcularPrioridad(AgendaOperativaItemVm item, AgendaOperativaPasoVm? actual, ParoDto? paro, DateTime ahora)
        {
            if (paro?.EsInterrupcionUrgente == true && !paro.FechaFin.HasValue) return AgendaOperativaPrioridad.Critica;
            if (item.ProduccionLhRh?.ParejaConsistente == false || actual?.Estado == AgendaOperativaEstadoPaso.Bloqueado) return AgendaOperativaPrioridad.Critica;
            if (paro != null && !paro.FechaFin.HasValue) return AgendaOperativaPrioridad.Alta;
            if (item.EsUrgente) return AgendaOperativaPrioridad.Alta;
            if (actual?.FechaObjetivo.HasValue == true && actual.FechaObjetivo.Value < ahora) return (ahora - actual.FechaObjetivo.Value).TotalMinutes >= 60 ? AgendaOperativaPrioridad.Critica : AgendaOperativaPrioridad.Alta;
            if (item.EstadoGeneral == AgendaOperativaEstadoGeneral.EsperandoCalidad || item.EstadoGeneral == AgendaOperativaEstadoGeneral.Reliberacion) return AgendaOperativaPrioridad.Alta;
            if (item.FechaInicioProgramada.HasValue && item.FechaInicioProgramada.Value <= ahora.AddMinutes(60)) return AgendaOperativaPrioridad.Media;
            return AgendaOperativaPrioridad.Normal;
        }
        private static List<AgendaOperativaItemVm> AplicarFiltrosDerivados(List<AgendaOperativaItemVm> items, AgendaOperativaFiltroVm filtros)
        {
            IEnumerable<AgendaOperativaItemVm> q = items;
            if (!string.IsNullOrWhiteSpace(filtros.Area)) q = q.Where(x => string.Equals(x.AccionActual?.AreaResponsable, filtros.Area, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(filtros.Estado)) q = q.Where(x => string.Equals(x.EstadoGeneral, filtros.Estado, StringComparison.OrdinalIgnoreCase));
            if (filtros.SoloAtencion) q = q.Where(x => x.RequiereAtencionInmediata);
            if (filtros.SoloBloqueadas) q = q.Where(x => x.EstaBloqueada);
            if (!filtros.IncluirProduciendo) q = q.Where(x => !x.EstaProduciendo);
            return q.ToList();
        }
        private static AgendaOperativaResumenVm ConstruirResumen(List<AgendaOperativaItemVm> items)
        {
            return new AgendaOperativaResumenVm { Total = items.Count, AtencionInmediata = items.Count(x => x.RequiereAtencionInmediata), Bloqueadas = items.Count(x => x.EstaBloqueada), Proximas = items.Count(x => x.EsProxima && !x.RequiereAtencionInmediata), EnPreparacion = items.Count(x => x.EstadoGeneral == AgendaOperativaEstadoGeneral.Preparacion || x.EstadoGeneral == AgendaOperativaEstadoGeneral.ListaParaSerie), EsperandoCalidad = items.Count(x => x.EstadoGeneral == AgendaOperativaEstadoGeneral.EsperandoCalidad), Produciendo = items.Count(x => x.EstadoGeneral == AgendaOperativaEstadoGeneral.Produciendo), Pausadas = items.Count(x => x.EstadoGeneral == AgendaOperativaEstadoGeneral.Pausada), InterrumpidasUrgente = items.Count(x => x.EstadoGeneral == AgendaOperativaEstadoGeneral.InterrumpidaUrgente), Reliberaciones = items.Count(x => x.EstadoGeneral == AgendaOperativaEstadoGeneral.Reliberacion), MaquinasLiberadas = items.Count(x => x.MaquinaLiberada) };
        }
        private static void AgregarResponsables(AgendaOperativaItemVm item, ProgramaBaseDto p, ConfiguracionDto? c)
        {
            if (p.OperadorPrincipalID.HasValue || !string.IsNullOrWhiteSpace(p.OperadorPrincipalNombre)) item.Responsables.Add(new AgendaOperativaResponsableVm { UsuarioID = p.OperadorPrincipalID, Area = AgendaOperativaArea.Operador, Rol = "Operador principal", Nombre = p.OperadorPrincipalNombre, Asignado = true, Disponible = true });
            if (p.OperadorAuxiliarID.HasValue || !string.IsNullOrWhiteSpace(p.OperadorAuxiliarNombre)) item.Responsables.Add(new AgendaOperativaResponsableVm { UsuarioID = p.OperadorAuxiliarID, Area = AgendaOperativaArea.Produccion, Rol = "Auxiliar de Producción", Nombre = p.OperadorAuxiliarNombre, Asignado = true, Disponible = true });
            var tecnicoId = c?.TecnicoProduccionID ?? p.TecnicoProduccionID;
            var tecnicoNombre = c?.TecnicoNombre ?? p.TecnicoProduccionNombre;
            if (tecnicoId.HasValue || !string.IsNullOrWhiteSpace(tecnicoNombre)) item.Responsables.Add(new AgendaOperativaResponsableVm { UsuarioID = tecnicoId, Area = AgendaOperativaArea.TecnicoProduccion, Rol = "Técnico de Producción", Nombre = tecnicoNombre, Asignado = true, Disponible = true });
        }
        private static AgendaOperativaPasoVm Paso(int orden, string clave, string nombre, string area, string estado, bool aplica, bool completado, bool enProceso, bool bloquea, string? detalle, DateTime? fechaObjetivo, string? controlador, string? accion, int? idDestino)
        {
            return new AgendaOperativaPasoVm { Orden = orden, Clave = clave, Nombre = nombre, AreaResponsable = area, Estado = estado, Aplica = aplica, Completado = completado, EnProceso = enProceso, Bloqueado = estado == AgendaOperativaEstadoPaso.Bloqueado, BloqueaFlujo = bloquea, MotivoBloqueo = bloquea && !completado ? detalle : null, Detalle = detalle, FechaObjetivo = fechaObjetivo, Controlador = controlador, Accion = accion, IdDestino = idDestino };
        }
        private static AgendaOperativaPasoVm PasoNoAplica(int orden, string clave, string nombre, string area, string detalle) => Paso(orden, clave, nombre, area, AgendaOperativaEstadoPaso.NoAplica, false, true, false, false, detalle, null, null, null, null);
        private static AgendaOperativaPasoVm PasoEsperando(int orden, string clave, string nombre, string area, string detalle) => Paso(orden, clave, nombre, area, AgendaOperativaEstadoPaso.Esperando, true, false, false, false, detalle, null, null, null, null);
        private static void CompletarTiempoPaso(AgendaOperativaPasoVm p, DateTime ahora) { if (!p.Aplica || p.Completado || !p.FechaObjetivo.HasValue) return; p.MinutosDesfase = p.FechaObjetivo.Value < ahora ? (int)Math.Floor((ahora - p.FechaObjetivo.Value).TotalMinutes) : 0; p.EstaVencido = p.MinutosDesfase > 0; if (p.EstaVencido && p.Estado == AgendaOperativaEstadoPaso.Pendiente) p.Estado = AgendaOperativaEstadoPaso.Vencido; }
        private static string TituloAccion(AgendaOperativaPasoVm p) => p.Clave switch { AgendaOperativaPasoClave.Personal => "Asignar personal a la OF", AgendaOperativaPasoClave.Material => "Revisar materia prima", AgendaOperativaPasoClave.Secado => "Atender secado de material", AgendaOperativaPasoClave.Embalaje => "Preparar embalaje", AgendaOperativaPasoClave.CambioMolde => "Atender cambio de molde", "INICIAR_PREPARACION" => "Iniciar preparación de Producción", AgendaOperativaPasoClave.ChecklistArranque => "Completar checklist de arranque", AgendaOperativaPasoClave.ConfiguracionCorrida => "Confirmar configuración técnica", AgendaOperativaPasoClave.PrimerasPiezas => "Validar primeras piezas", AgendaOperativaPasoClave.Calidad => p.Nombre, AgendaOperativaPasoClave.InicioSerie => "Iniciar o reiniciar serie", AgendaOperativaPasoClave.Paro => "Atender interrupción activa", AgendaOperativaPasoClave.Capturas => "Registrar producción", AgendaOperativaPasoClave.Cajas => "Atender cajas de producción", AgendaOperativaPasoClave.CalidadFinal => "Resolver pendientes de Calidad", AgendaOperativaPasoClave.LiberacionMaquina => "Liberar máquina", AgendaOperativaPasoClave.Cierre => "Cerrar Producción", _ => p.Nombre };
        private static string TextoBoton(AgendaOperativaPasoVm p) => p.Clave switch { AgendaOperativaPasoClave.Material => "Ver materiales", AgendaOperativaPasoClave.Secado => "Ir a secado", AgendaOperativaPasoClave.Embalaje => "Ver embalaje", AgendaOperativaPasoClave.CambioMolde => "Atender molde", "INICIAR_PREPARACION" => "Ir a Producción", AgendaOperativaPasoClave.PrimerasPiezas or AgendaOperativaPasoClave.Calidad or AgendaOperativaPasoClave.CalidadFinal => "Ir a Calidad", AgendaOperativaPasoClave.InicioSerie => "Ir al arranque", AgendaOperativaPasoClave.LiberacionMaquina => "Revisar liberación", AgendaOperativaPasoClave.Cierre => "Revisar cierre", _ => "Atender" };
        private static string IconoPaso(string clave) => clave switch { AgendaOperativaPasoClave.Material => "bi-box-seam", AgendaOperativaPasoClave.Secado => "bi-thermometer-half", AgendaOperativaPasoClave.Embalaje => "bi-box2", AgendaOperativaPasoClave.CambioMolde => "bi-tools", AgendaOperativaPasoClave.ChecklistArranque => "bi-ui-checks", AgendaOperativaPasoClave.ConfiguracionCorrida => "bi-sliders", AgendaOperativaPasoClave.PrimerasPiezas => "bi-patch-check", AgendaOperativaPasoClave.Calidad => "bi-shield-check", AgendaOperativaPasoClave.InicioSerie => "bi-play-circle", AgendaOperativaPasoClave.Paro => "bi-pause-circle", AgendaOperativaPasoClave.Capturas => "bi-speedometer2", AgendaOperativaPasoClave.Cajas => "bi-boxes", AgendaOperativaPasoClave.LiberacionMaquina => "bi-unlock", AgendaOperativaPasoClave.Cierre => "bi-check2-circle", _ => "bi-arrow-right-circle" };
        private static List<AgendaOperativaOpcionVm> ConstruirOpcionesAreas(string? seleccion) => new() { Opcion(AgendaOperativaArea.Planeacion, seleccion), Opcion(AgendaOperativaArea.Produccion, seleccion), Opcion(AgendaOperativaArea.TecnicoProduccion, seleccion), Opcion(AgendaOperativaArea.Smed, seleccion), Opcion(AgendaOperativaArea.Calidad, seleccion), Opcion(AgendaOperativaArea.Materiales, seleccion), Opcion(AgendaOperativaArea.Secado, seleccion), Opcion(AgendaOperativaArea.Embalaje, seleccion), Opcion(AgendaOperativaArea.Operador, seleccion) };
        private static AgendaOperativaOpcionVm Opcion(string area, string? seleccion) => new() { Valor = area, Texto = AgendaOperativaArea.Nombre(area), Seleccionado = string.Equals(area, seleccion, StringComparison.OrdinalIgnoreCase) };
        private static int Int(SqlDataReader rd, string c) => rd[c] == DBNull.Value ? 0 : Convert.ToInt32(rd[c]);
        private static int? NInt(SqlDataReader rd, string c) => rd[c] == DBNull.Value ? null : Convert.ToInt32(rd[c]);
        private static long? NLong(SqlDataReader rd, string c) => rd[c] == DBNull.Value ? null : Convert.ToInt64(rd[c]);
        private static decimal Dec(SqlDataReader rd, string c) => rd[c] == DBNull.Value ? 0m : Convert.ToDecimal(rd[c]);
        private static decimal? NDec(SqlDataReader rd, string c) => rd[c] == DBNull.Value ? null : Convert.ToDecimal(rd[c]);
        private static DateTime? NDate(SqlDataReader rd, string c) => rd[c] == DBNull.Value ? null : Convert.ToDateTime(rd[c]);
        private static string? Txt(SqlDataReader rd, string c) => rd[c] == DBNull.Value ? null : rd[c]?.ToString()?.Trim();
        private static bool Bool(SqlDataReader rd, string c) => rd[c] != DBNull.Value && Convert.ToBoolean(rd[c]);
        private static Guid? NGuid(SqlDataReader rd, string c) => rd[c] == DBNull.Value ? null : (Guid?)rd[c];
        private sealed class ProgramaBaseDto
        {
            public int ProgramaProduccionID { get; set; }
            public int? SolicitudProduccionID { get; set; }
            public int? SolicitudProduccionDetalleID { get; set; }
            public int? ReleaseDetalleID { get; set; }
            public string? NumeroOF { get; set; }
            public int? ParteID { get; set; }
            public string? NumeroParte { get; set; }
            public string? ReferenciaSAP { get; set; }
            public string? DescripcionParte { get; set; }
            public int? MaquinaID { get; set; }
            public string? MaquinaCodigo { get; set; }
            public string? MaquinaNombre { get; set; }
            public int? MoldeID { get; set; }
            public string? MoldeCodigo { get; set; }
            public int CantidadProgramada { get; set; }
            public int CantidadProducida { get; set; }
            public DateTime? FechaInicioProgramada { get; set; }
            public DateTime? FechaFinProgramada { get; set; }
            public int EstatusProgramaID { get; set; }
            public string? Observaciones { get; set; }
            public int? GrupoLhRh { get; set; }
            public bool RequiereCambioMolde { get; set; }
            public int? MaterialID { get; set; }
            public string? MaterialCodigo { get; set; }
            public string? MaterialDescripcion { get; set; }
            public decimal? CantidadMpKg { get; set; }
            public string? TipoSecado { get; set; }
            public decimal? HorasSecado { get; set; }
            public string? EmbalajeCodigo { get; set; }
            public string? EmbalajeDescripcion { get; set; }
            public decimal? CantidadEmbalajes { get; set; }
            public int? EjecucionProduccionID { get; set; }
            public int? EstatusEjecucionID { get; set; }
            public DateTime? FechaInicioReal { get; set; }
            public DateTime? FechaFinReal { get; set; }
            public DateTime? FechaLiberacionMaquina { get; set; }
            public int? OperadorPrincipalID { get; set; }
            public string? OperadorPrincipalNombre { get; set; }
            public int? OperadorAuxiliarID { get; set; }
            public string? OperadorAuxiliarNombre { get; set; }
            public int? TecnicoProduccionID { get; set; }
            public string? TecnicoProduccionNombre { get; set; }
            public bool EsUrgente { get; set; }
        }
        private sealed class PreparacionDto { public int PreparacionAnticipadaID { get; set; } public string TipoTarea { get; set; } = string.Empty; public string Estado { get; set; } = "PENDIENTE"; public DateTime? FechaObjetivo { get; set; } public DateTime? FechaAviso { get; set; } public DateTime? FechaInicioReal { get; set; } public DateTime? FechaFinReal { get; set; } public DateTime? FechaConfirmacion { get; set; } public string? Observaciones { get; set; } }
        private sealed class InsumoDto { public decimal CantidadMpRequerida { get; set; } public decimal CantidadMpRecibida { get; set; } public decimal CantidadEmbalajeRequerida { get; set; } public decimal CantidadEmbalajeRecibida { get; set; } }
        private sealed class SecadoDto { public int Total { get; set; } public int Finalizados { get; set; } public int EnProceso { get; set; } public int Pendientes { get; set; } public DateTime? FechaInicioObjetivo { get; set; } public DateTime? FechaFinObjetivo { get; set; } public DateTime? FechaPrimerInicio { get; set; } public DateTime? FechaUltimoFin { get; set; } }
        private sealed class ChecklistDto { public int ChecklistArranqueID { get; set; } public int EjecucionProduccionID { get; set; } public int EstatusID { get; set; } public DateTime? FechaChecklist { get; set; } public DateTime? FechaCapturaProduccion { get; set; } public DateTime? FechaValidacionCalidad { get; set; } public string? ObservacionesCalidad { get; set; } }
        private sealed class ConfiguracionDto { public int ConfiguracionCorridaID { get; set; } public int EjecucionProduccionID { get; set; } public int CavidadesUsadas { get; set; } public decimal TiempoCicloSegundos { get; set; } public decimal? ObjetivoHoraCalculado { get; set; } public long? ContadorInicioVigencia { get; set; } public DateTime? FechaInicioVigencia { get; set; } public int? TecnicoProduccionID { get; set; } public string? TecnicoNombre { get; set; } }
        private sealed class CalidadDto
        {
            public int InspeccionID { get; set; }
            public int EjecucionProduccionID { get; set; }
            public string? Estado { get; set; }
            public string? ResultadoCalidad { get; set; }
            public string? Etiqueta { get; set; }
            public bool Liberado { get; set; }
            public bool ConfiguracionInvalidada { get; set; }
            public bool RequiereReliberacion { get; set; }
            public bool CincoDisparosSegregados { get; set; }
            public int CantidadDisparosConformes { get; set; }
            public string? MotivoDevolucion { get; set; }
            public DateTime? FechaNotificacionCalidad { get; set; }
            public int? ReliberacionID { get; set; }
            public string? ResultadoReliberacion { get; set; }
            public DateTime? FechaValidacionReliberacion { get; set; }
            public bool EstaLiberada => Liberado && !ConfiguracionInvalidada && !RequiereReliberacion && string.Equals(Estado, "PRODUCCION_LIBERADA", StringComparison.OrdinalIgnoreCase) && string.Equals(ResultadoCalidad, "VERDE", StringComparison.OrdinalIgnoreCase) && string.Equals(Etiqueta, "VERDE", StringComparison.OrdinalIgnoreCase);
        }
        private sealed class ParoDto { public int ParoID { get; set; } public int EjecucionProduccionID { get; set; } public DateTime FechaInicio { get; set; } public DateTime? FechaFin { get; set; } public string? Motivo { get; set; } public bool EsMayorA15 { get; set; } public bool EsInterrupcionUrgente { get; set; } public int? ProgramaUrgenteID { get; set; } public string? OFUrgente { get; set; } public bool EsParoLhRh { get; set; } public Guid? GrupoParoLhRh { get; set; } }
        private sealed class CierreDto { public int EjecucionProduccionID { get; set; } public int CantidadOK { get; set; } public int CantidadSospechosa { get; set; } public int CantidadScrap { get; set; } public int OkEnCajas { get; set; } public int SospechosoEnCajas { get; set; } public int RetencionEnCajas { get; set; } public int ScrapEnCajas { get; set; } public int CajasFormadasPendientes { get; set; } public int CajasPendientesCalidad { get; set; } public int RegistrosNormales { get; set; } public decimal MinutosNormalesCapturados { get; set; } public decimal? ObjetivoHora { get; set; } public bool TieneTiempoExtraActivo { get; set; } public int MonitoreosPendientes { get; set; } public int DisposicionesPendientes { get; set; } public int ReliberacionesPendientes { get; set; } }
        private sealed class ParejaDto { public int? GrupoLhRh { get; set; } public int ProgramaParejaID { get; set; } public int? SolicitudParejaID { get; set; } public string? OFPareja { get; set; } public string? NumeroPartePareja { get; set; } public string? ReferenciaSAPPareja { get; set; } public int EstatusProgramaParejaID { get; set; } public int? EjecucionParejaID { get; set; } public int? EstatusEjecucionParejaID { get; set; } public int CantidadProgramadaPareja { get; set; } public int CantidadProducidaPareja { get; set; } public bool MismaMaquina { get; set; } public bool MismoMolde { get; set; } public bool MismaVentana { get; set; } }
    }
}
