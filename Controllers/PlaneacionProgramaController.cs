using ERP.NSQuell.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using System.Data;

namespace ERP.NSQuell.Controllers
{
    public partial class PlaneacionProgramaController : Controller // NSQ_TODO_PLANEACION_PRODUCCION_V1
    {
        private readonly IConfiguration _configuration;

        public PlaneacionProgramaController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        private string ConnectionString =>
            _configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("No se encontró la cadena de conexión DefaultConnection.");


        [HttpGet]
        public async Task<IActionResult> Index(int? clienteId, int? parteId, DateTime? fechaDesde, DateTime? fechaHasta, bool soloListos = false, bool soloPendienteAbasto = false, bool soloPendienteDatosTecnicos = false)
        {
            var vm = new PlaneacionProgramaNecesidadFiltroVm
            {
                ClienteID = clienteId,
                ParteID = parteId,
                FechaDesde = fechaDesde,
                FechaHasta = fechaHasta,
                SoloPendientes = soloListos,
                SoloSinMP = soloPendienteAbasto,
                SoloSinCapacidad = false // NSQ_FILTRO_DT_REMOVIDO_V1
            };
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            await SincronizarReservasAlmacenPlaneacionAsync(cn);
            vm.Clientes = await CargarSelectAsync(cn, "SELECT ClienteID AS Id, Nombre AS Texto FROM dbo.ERP_Clientes WHERE Activo=1 ORDER BY Nombre;");
            vm.Partes = await CargarSelectAsync(cn, @"SELECT ParteID AS Id,
ISNULL(NULLIF(NumeroParte,''),ISNULL(NULLIF(ReferenciaSAP,''),CONVERT(NVARCHAR(30),ParteID)))+' | '+
ISNULL(NULLIF(ReferenciaSAP,''),ISNULL(NULLIF(NumeroParte,''),'Sin referencia'))+' | '+
ISNULL(NULLIF(Designacion,''),ISNULL(NULLIF(Descripcion,''),'Sin descripción')) AS Texto
FROM dbo.ERP_Partes
WHERE Activo=1
ORDER BY NumeroParte,ReferenciaSAP;");
            const string sql = @"
SELECT
    r.ReleaseID,r.FolioRelease,r.ClienteID,ISNULL(c.Nombre,r.ClienteNombre) AS ClienteNombre,r.FechaRecepcion,
    d.ReleaseDetalleID,d.Renglon,d.ParteID,d.NumeroParte,d.ReferenciaSAP,d.DesignacionDescripcionSAP,d.FechaCarga,d.FechaRequerida,d.CantidadRequerida,
    d.ProgramaProduccionID,d.SolicitudProduccionID,
    (SELECT TOP(1)sd.SolicitudProduccionDetalleID
     FROM dbo.SolicitudesProduccionDetalle sd
     WHERE sd.SolicitudProduccionID=d.SolicitudProduccionID AND sd.Activo=1 AND sd.Renglon=d.Renglon
       AND(sd.ParteID=d.ParteID OR(sd.ParteID IS NULL AND d.ParteID IS NULL))
     ORDER BY sd.SolicitudProduccionDetalleID) AS SolicitudProduccionDetalleID,
    d.EstatusID,
    COALESCE(d.MaterialID,t.MaterialID) AS MaterialID,
    COALESCE(NULLIF(d.MaterialCodigo,''),t.MaterialCodigo) AS MaterialCodigo,
    COALESCE(NULLIF(d.MaterialDescripcion,''),t.MaterialDescripcion) AS MaterialDescripcion,
    COALESCE(d.PesoBrutoPieza,t.PesoBrutoPieza) AS PesoBrutoPieza,
    t.PesoNetoPieza,
    COALESCE(NULLIF(d.EmbalajeCodigo,''),t.EmbalajeCodigo) AS EmbalajeCodigo,
    COALESCE(NULLIF(d.EmbalajeDescripcion,''),t.EmbalajeDescripcion) AS EmbalajeDescripcion,
    COALESCE(d.PiezasPorEmbalaje,t.PiezasPorEmbalaje) AS PiezasPorEmbalaje,
    t.PiezasPorCaja,
    COALESCE(d.MoldeID,t.MoldePrincipalID) AS MoldeID,
    COALESCE(NULLIF(d.MoldeCodigo,''),mol.CodigoMolde) AS MoldeCodigo,
    COALESCE(d.MaquinaSugeridaID,t.MaquinaPrincipalID) AS MaquinaSugeridaID,
    COALESCE(NULLIF(d.MaquinaSugeridaCodigo,''),maq.Codigo) AS MaquinaSugeridaCodigo,
    COALESCE(NULLIF(d.MaquinaSugeridaNombre,''),maq.Nombre) AS MaquinaSugeridaNombre,
    sust.MaquinaSustitutaID,sust.MaquinaSustitutaCodigo,sust.MaquinaSustitutaNombre,
    t.Ciclo,t.Cavidades,t.ObjetivoHora,t.Color,t.TipoSecado,t.HorasSecado,t.HorasSecadoTexto,
    ISNULL(pt.Disponible,0) AS PTDisponible,
    ISNULL(mp.Disponible,0) AS MPDisponible,
    ISNULL(emb.Disponible,0) AS EmbalajeDisponible,
    ISNULL(prog.ProgramadoPendiente,0) AS ProgramadoPendiente,
    ISNULL(aptPropio.CantidadApartada,0) AS PTApartadoPropio,
    ISNULL(aptOtros.CantidadApartada,0) AS PTApartadoOtros,
    ISNULL(blancaApartada.CantidadApartada,0) AS ProductoIncompletoApartado,
    ISNULL(blancaDisponible.CantidadDisponible,0) AS ProductoIncompletoDisponible
FROM dbo.Planeacion_ReleaseDetalle d
INNER JOIN dbo.Planeacion_Releases r ON r.ReleaseID=d.ReleaseID
LEFT JOIN dbo.ERP_Clientes c ON c.ClienteID=r.ClienteID
LEFT JOIN dbo.ERP_ParteDatosTecnicos t ON t.ParteID=d.ParteID AND t.Activo=1
LEFT JOIN dbo.ERP_Moldes mol ON mol.MoldeID=COALESCE(d.MoldeID,t.MoldePrincipalID)
LEFT JOIN dbo.ERP_Maquinas maq ON maq.MaquinaID=COALESCE(d.MaquinaSugeridaID,t.MaquinaPrincipalID)
OUTER APPLY
(
    SELECT TOP(1)x.MaquinaID AS MaquinaSustitutaID
    FROM
    (
        SELECT t.MaquinaSustitutaID AS MaquinaID,0 AS Prioridad WHERE t.MaquinaSustitutaID IS NOT NULL
        UNION
        SELECT ms.MaquinaSustitutaID,ISNULL(ms.Prioridad,999)
        FROM dbo.ERP_MaquinasSustitutas ms
        WHERE ms.Activo=1 AND ms.MaquinaPrincipalID=t.MaquinaPrincipalID
        UNION
        SELECT ms.MaquinaPrincipalID,ISNULL(ms.Prioridad,999)
        FROM dbo.ERP_MaquinasSustitutas ms
        WHERE ms.Activo=1 AND ms.MaquinaSustitutaID=t.MaquinaPrincipalID
    )x
    INNER JOIN dbo.ERP_Maquinas m ON m.MaquinaID=x.MaquinaID AND m.Activo=1
    WHERE x.MaquinaID IS NOT NULL AND(t.MaquinaPrincipalID IS NULL OR x.MaquinaID<>t.MaquinaPrincipalID)
    ORDER BY x.Prioridad,x.MaquinaID
)sustTop
OUTER APPLY
(
    SELECT sustTop.MaquinaSustitutaID,
    STUFF((
        SELECT ', '+m2.Codigo
        FROM
        (
            SELECT t.MaquinaSustitutaID AS MaquinaID,0 AS Prioridad WHERE t.MaquinaSustitutaID IS NOT NULL
            UNION
            SELECT ms.MaquinaSustitutaID,ISNULL(ms.Prioridad,999)
            FROM dbo.ERP_MaquinasSustitutas ms
            WHERE ms.Activo=1 AND ms.MaquinaPrincipalID=t.MaquinaPrincipalID
            UNION
            SELECT ms.MaquinaPrincipalID,ISNULL(ms.Prioridad,999)
            FROM dbo.ERP_MaquinasSustitutas ms
            WHERE ms.Activo=1 AND ms.MaquinaSustitutaID=t.MaquinaPrincipalID
        )x2
        INNER JOIN dbo.ERP_Maquinas m2 ON m2.MaquinaID=x2.MaquinaID AND m2.Activo=1
        WHERE x2.MaquinaID IS NOT NULL AND(t.MaquinaPrincipalID IS NULL OR x2.MaquinaID<>t.MaquinaPrincipalID)
        ORDER BY x2.Prioridad,m2.Codigo
        FOR XML PATH(''),TYPE
    ).value('.','NVARCHAR(MAX)'),1,2,'') AS MaquinaSustitutaCodigo,
    STUFF((
        SELECT ', '+m2.Codigo+' - '+ISNULL(m2.Nombre,'')
        FROM
        (
            SELECT t.MaquinaSustitutaID AS MaquinaID,0 AS Prioridad WHERE t.MaquinaSustitutaID IS NOT NULL
            UNION
            SELECT ms.MaquinaSustitutaID,ISNULL(ms.Prioridad,999)
            FROM dbo.ERP_MaquinasSustitutas ms
            WHERE ms.Activo=1 AND ms.MaquinaPrincipalID=t.MaquinaPrincipalID
            UNION
            SELECT ms.MaquinaPrincipalID,ISNULL(ms.Prioridad,999)
            FROM dbo.ERP_MaquinasSustitutas ms
            WHERE ms.Activo=1 AND ms.MaquinaSustitutaID=t.MaquinaPrincipalID
        )x3
        INNER JOIN dbo.ERP_Maquinas m2 ON m2.MaquinaID=x3.MaquinaID AND m2.Activo=1
        WHERE x3.MaquinaID IS NOT NULL AND(t.MaquinaPrincipalID IS NULL OR x3.MaquinaID<>t.MaquinaPrincipalID)
        ORDER BY x3.Prioridad,m2.Codigo
        FOR XML PATH(''),TYPE
    ).value('.','NVARCHAR(MAX)'),1,2,'') AS MaquinaSustitutaNombre
)sust
OUTER APPLY
(
    SELECT TOP(1)ISNULL(Disponible,0) AS Disponible
    FROM dbo.vw_AlmacenPTInventario
    WHERE ParteID=d.ParteID
)pt
OUTER APPLY
(
    SELECT TOP(1)ISNULL(Disponible,0) AS Disponible
    FROM dbo.vw_AlmacenMPInventario
    WHERE MaterialID=t.MaterialID AND TipoMP=N'V'
    ORDER BY OrdenTipo
)mp
OUTER APPLY
(
    SELECT TOP(1)ISNULL(Disponible,0) AS Disponible
    FROM dbo.vw_AlmacenEmbalajesInventario
    WHERE Codigo=t.EmbalajeCodigo
)emb
OUTER APPLY
(
    SELECT ISNULL(SUM(ISNULL(pp.CantidadProgramada,0)-ISNULL(pp.CantidadProducida,0)),0) AS ProgramadoPendiente
    FROM dbo.Planeacion_ProgramaProduccion pp
    WHERE pp.ReleaseDetalleID=d.ReleaseDetalleID AND pp.Activo=1 AND ISNULL(pp.EstatusID,1) NOT IN(5,9,99)
)prog
OUTER APPLY
(
    SELECT ISNULL(SUM(a.CantidadApartada),0) AS CantidadApartada
    FROM dbo.Planeacion_PT_Apartado a
    WHERE a.ReleaseDetalleID=d.ReleaseDetalleID AND a.Activo=1 AND a.EstatusID=1
)aptPropio
OUTER APPLY
(
    SELECT ISNULL(SUM(a.CantidadApartada),0) AS CantidadApartada
    FROM dbo.Planeacion_PT_Apartado a
    WHERE a.ParteID=d.ParteID AND a.ReleaseDetalleID<>d.ReleaseDetalleID AND a.Activo=1 AND a.EstatusID=1
)aptOtros
OUTER APPLY
(
    SELECT ISNULL(SUM(a.CantidadApartada),0) AS CantidadApartada
    FROM dbo.Planeacion_ProductoIncompletoApartado a
    WHERE a.ReleaseDetalleID=d.ReleaseDetalleID AND a.Activo=1 AND a.EstatusID IN(1,2,3,4)
)blancaApartada
OUTER APPLY
(
    SELECT ISNULL(SUM(ISNULL(pc.CantidadPiezas,ISNULL(pc.Cantidad,0))),0) AS CantidadDisponible
    FROM dbo.Produccion_Cajas pc
    INNER JOIN dbo.Produccion_Ejecucion pe ON pe.EjecucionProduccionID=pc.EjecucionProduccionID
    WHERE pc.Activo=1
      AND ISNULL(pc.EsProductoIncompleto,0)=1
      AND UPPER(LTRIM(RTRIM(ISNULL(pc.EstadoProductoIncompleto,N''))))=N'DISPONIBLE'
      AND pe.ParteID=d.ParteID
      AND ISNULL(pc.CantidadPiezas,ISNULL(pc.Cantidad,0))>0
      AND NOT EXISTS
      (
          SELECT 1
          FROM dbo.Planeacion_ProductoIncompletoApartado a
          WHERE a.CajaProduccionID=pc.CajaProduccionID
            AND a.Activo=1
            AND a.EstatusID IN(1,2,3,4)
      )
)blancaDisponible
WHERE r.Activo=1
  AND d.Activo=1
  AND(@ClienteID IS NULL OR r.ClienteID=@ClienteID)
  AND(@ParteID IS NULL OR d.ParteID=@ParteID)
  AND(@FechaDesde IS NULL OR d.FechaRequerida>=@FechaDesde)
  AND(@FechaHasta IS NULL OR d.FechaRequerida<=@FechaHasta)
ORDER BY ISNULL(c.Nombre,r.ClienteNombre),d.FechaRequerida,d.NumeroParte,d.Renglon;";
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@ClienteID", SqlDbType.Int).Value = (object?)clienteId ?? DBNull.Value;
            cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value = (object?)parteId ?? DBNull.Value;
            cmd.Parameters.Add("@FechaDesde", SqlDbType.Date).Value = (object?)fechaDesde?.Date ?? DBNull.Value;
            cmd.Parameters.Add("@FechaHasta", SqlDbType.Date).Value = (object?)fechaHasta?.Date ?? DBNull.Value;
            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                var cantidadRequerida = rd["CantidadRequerida"] == DBNull.Value ? 0 : Convert.ToInt32(rd["CantidadRequerida"]);
                var stockDisponible = rd["PTDisponible"] == DBNull.Value ? 0 : Convert.ToInt32(rd["PTDisponible"]);
                var ptApartadoPropio = rd["PTApartadoPropio"] == DBNull.Value ? 0 : Convert.ToInt32(rd["PTApartadoPropio"]);
                var ptApartadoOtros = rd["PTApartadoOtros"] == DBNull.Value ? 0 : Convert.ToInt32(rd["PTApartadoOtros"]);
                var programadoPendiente = rd["ProgramadoPendiente"] == DBNull.Value ? 0 : Convert.ToInt32(rd["ProgramadoPendiente"]);
                var productoIncompletoApartado = rd["ProductoIncompletoApartado"] == DBNull.Value ? 0 : Convert.ToInt32(rd["ProductoIncompletoApartado"]);
                var productoIncompletoDisponible = rd["ProductoIncompletoDisponible"] == DBNull.Value ? 0 : Convert.ToInt32(rd["ProductoIncompletoDisponible"]);
                var ptDisponibleNeto = Math.Max(0, stockDisponible - ptApartadoOtros);
                var piezasDesdeStock = 0;
                var cantidadOriginalAProducir = Math.Max(0, cantidadRequerida - programadoPendiente);
                var piezasAProducir = Math.Max(0, cantidadOriginalAProducir - productoIncompletoApartado);
                var pesoBrutoPieza = rd["PesoBrutoPieza"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(rd["PesoBrutoPieza"]);
                var piezasPorEmbalaje = rd["PiezasPorEmbalaje"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(rd["PiezasPorEmbalaje"]);
                var objetivoHora = rd["ObjetivoHora"] == DBNull.Value ? (int?)null : Convert.ToInt32(rd["ObjetivoHora"]);
                decimal mpRequeridaKg = 0;
                if (piezasAProducir > 0 && pesoBrutoPieza.HasValue && pesoBrutoPieza.Value > 0) mpRequeridaKg = Math.Round((piezasAProducir * pesoBrutoPieza.Value) / 1000m, 4);
                decimal embalajeRequerido = 0;
                if (piezasAProducir > 0 && piezasPorEmbalaje.HasValue && piezasPorEmbalaje.Value > 0) embalajeRequerido = Math.Ceiling(piezasAProducir / piezasPorEmbalaje.Value);
                decimal horasProgramadas = 0;
                if (piezasAProducir > 0 && objetivoHora.HasValue && objetivoHora.Value > 0) horasProgramadas = Math.Ceiling(piezasAProducir / (decimal)objetivoHora.Value);
                int? qtyPorDia = null;
                if (objetivoHora.HasValue && objetivoHora.Value > 0) qtyPorDia = objetivoHora.Value * 24;
                var fechaRequerida = rd["FechaRequerida"] == DBNull.Value ? DateTime.Today : Convert.ToDateTime(rd["FechaRequerida"]);
                var fechaInicioSugerida = DateTime.Now;
                DateTime? fechaFinEstimada = null;
                if (horasProgramadas > 0) fechaFinEstimada = fechaInicioSugerida.AddHours((double)horasProgramadas);
                bool? daTiempo = null;
                if (piezasAProducir <= 0) daTiempo = true;
                else if (fechaFinEstimada.HasValue) daTiempo = fechaFinEstimada.Value.Date <= fechaRequerida.Date;
                var mpDisponible = rd["MPDisponible"] == DBNull.Value ? 0 : Convert.ToDecimal(rd["MPDisponible"]);
                var embalajeDisponible = rd["EmbalajeDisponible"] == DBNull.Value ? 0 : Convert.ToDecimal(rd["EmbalajeDisponible"]);
                var materialId = rd["MaterialID"] == DBNull.Value ? (int?)null : Convert.ToInt32(rd["MaterialID"]);
                var maquinaId = rd["MaquinaSugeridaID"] == DBNull.Value ? (int?)null : Convert.ToInt32(rd["MaquinaSugeridaID"]);
                var moldeId = rd["MoldeID"] == DBNull.Value ? (int?)null : Convert.ToInt32(rd["MoldeID"]);
                var cavidades = rd["Cavidades"] == DBNull.Value ? (int?)null : Convert.ToInt32(rd["Cavidades"]);
                var faltaMaterial = !materialId.HasValue;
                var faltaMaquina = !maquinaId.HasValue;
                var faltaMolde = !moldeId.HasValue;
                var faltaCavidades = !cavidades.HasValue || cavidades.Value <= 0;
                var faltaCiclo = rd["Ciclo"] == DBNull.Value || string.IsNullOrWhiteSpace(rd["Ciclo"].ToString());
                var faltaObjetivo = !objetivoHora.HasValue || objetivoHora.Value <= 0;
                var faltaPeso = !pesoBrutoPieza.HasValue || pesoBrutoPieza.Value <= 0;
                var faltaEmbalaje = piezasAProducir > 0 && (!piezasPorEmbalaje.HasValue || piezasPorEmbalaje.Value <= 0);
                var faltaMPStock = piezasAProducir > 0 && mpRequeridaKg > mpDisponible + 0.0005m;
                var faltaEmbalajeStock = piezasAProducir > 0 && embalajeRequerido > embalajeDisponible + 0.0005m;
                string mensaje;
                if (piezasAProducir <= 0 && productoIncompletoApartado > 0) mensaje = "Necesidad cubierta con producto incompleto apartado y/o producción ya programada.";
                else if (piezasAProducir <= 0) mensaje = "Cubierto con producción ya programada.";
                else if (faltaMaterial) mensaje = "Falta material o resina en datos técnicos.";
                else if (faltaMPStock) mensaje = $"MP insuficiente: requiere {mpRequeridaKg:N4} kg y hay {mpDisponible:N4} kg disponibles sin reservar.";
                else if (faltaEmbalajeStock) mensaje = $"Embalaje insuficiente: requiere {embalajeRequerido:N0} y hay {embalajeDisponible:N4} disponibles sin reservar.";
                else if (faltaMolde) mensaje = "Falta molde en datos técnicos.";
                else if (faltaMaquina) mensaje = "Falta máquina asignada en datos técnicos.";
                else if (faltaCavidades) mensaje = "Faltan cavidades en datos técnicos.";
                else if (faltaCiclo) mensaje = "Falta ciclo en datos técnicos.";
                else if (faltaObjetivo) mensaje = "Falta objetivo por hora en datos técnicos.";
                else if (faltaPeso) mensaje = "Falta peso bruto de pieza en datos técnicos.";
                else if (faltaEmbalaje) mensaje = "Faltan piezas por embalaje en datos técnicos.";
                else if (daTiempo == false) mensaje = "No da tiempo contra la fecha requerida.";
                else if (productoIncompletoApartado > 0) mensaje = $"Listo para programar. Se usarán {productoIncompletoApartado:N0} pieza(s) de etiqueta blanca y se producirán {piezasAProducir:N0}.";
                else if (productoIncompletoDisponible > 0) mensaje = $"Listo para programar. Hay {productoIncompletoDisponible:N0} pieza(s) de etiqueta blanca disponibles; Planeación puede decidir utilizarlas.";
                else mensaje = "Listo para enviar a Programa Cambio de Molde.";
                var necesidad = new PlaneacionProgramaNecesidadVm
                {
                    ReleaseID = Convert.ToInt32(rd["ReleaseID"]),
                    ReleaseDetalleID = Convert.ToInt32(rd["ReleaseDetalleID"]),
                    FolioRelease = rd["FolioRelease"] as string,
                    ClienteID = rd["ClienteID"] == DBNull.Value ? null : Convert.ToInt32(rd["ClienteID"]),
                    ClienteNombre = rd["ClienteNombre"] as string,
                    FechaRecepcion = rd["FechaRecepcion"] == DBNull.Value ? DateTime.Today : Convert.ToDateTime(rd["FechaRecepcion"]),
                    Renglon = rd["Renglon"] == DBNull.Value ? 0 : Convert.ToInt32(rd["Renglon"]),
                    ParteID = rd["ParteID"] == DBNull.Value ? null : Convert.ToInt32(rd["ParteID"]),
                    NumeroParte = rd["NumeroParte"] as string,
                    ReferenciaSAP = rd["ReferenciaSAP"] as string,
                    DesignacionDescripcionSAP = rd["DesignacionDescripcionSAP"] as string,
                    FechaRequerida = fechaRequerida,
                    CantidadRequerida = cantidadRequerida,
                    PTDisponibleAlCalcular = stockDisponible,
                    PTApartadoOtros = ptApartadoOtros,
                    PTDisponibleNeto = ptDisponibleNeto,
                    ProduccionProgramadaPendiente = programadoPendiente,
                    PiezasDesdePT = piezasDesdeStock,
                    ProductoIncompletoDisponible = productoIncompletoDisponible,
                    ProductoIncompletoApartado = productoIncompletoApartado,
                    PiezasAProducir = piezasAProducir,
                    MaterialID = materialId,
                    MaterialCodigo = rd["MaterialCodigo"] as string,
                    MaterialDescripcion = rd["MaterialDescripcion"] as string,
                    MPRequeridaKg = mpRequeridaKg,
                    MPDisponibleKg = mpDisponible,
                    EmbalajeCodigo = rd["EmbalajeCodigo"] as string,
                    EmbalajeDescripcion = rd["EmbalajeDescripcion"] as string,
                    EmbalajeRequerido = embalajeRequerido,
                    EmbalajeDisponible = embalajeDisponible,
                    MaquinaSugeridaID = maquinaId,
                    MaquinaSugeridaCodigo = rd["MaquinaSugeridaCodigo"] as string,
                    MaquinaSugeridaNombre = rd["MaquinaSugeridaNombre"] as string,
                    MoldeID = moldeId,
                    MoldeCodigo = rd["MoldeCodigo"] as string,
                    ObjetivoHora = objetivoHora,
                    HorasNecesarias = horasProgramadas,
                    FechaInicioSugerida = fechaInicioSugerida,
                    FechaFinEstimada = fechaFinEstimada,
                    DaTiempo = daTiempo,
                    MensajeCapacidad = mensaje,
                    ProgramaProduccionID = rd["ProgramaProduccionID"] == DBNull.Value ? null : Convert.ToInt32(rd["ProgramaProduccionID"]),
                    SolicitudProduccionID = rd["SolicitudProduccionID"] == DBNull.Value ? null : Convert.ToInt32(rd["SolicitudProduccionID"]),
                    SolicitudProduccionDetalleID = rd["SolicitudProduccionDetalleID"] == DBNull.Value ? null : Convert.ToInt32(rd["SolicitudProduccionDetalleID"]),
                    EstatusID = rd["EstatusID"] == DBNull.Value ? 0 : Convert.ToInt32(rd["EstatusID"]),
                    Ciclo = rd["Ciclo"] == DBNull.Value ? null : rd["Ciclo"].ToString(),
                    Cavidades = cavidades,
                    PesoBrutoPieza = pesoBrutoPieza,
                    PiezasPorCaja = rd["PiezasPorCaja"] == DBNull.Value ? null : Convert.ToInt32(rd["PiezasPorCaja"]),
                    QtyPorDia = qtyPorDia,
                    MaquinaSustitutaID = rd["MaquinaSustitutaID"] == DBNull.Value ? null : Convert.ToInt32(rd["MaquinaSustitutaID"]),
                    MaquinaSustitutaCodigo = rd["MaquinaSustitutaCodigo"] as string,
                    MaquinaSustitutaNombre = rd["MaquinaSustitutaNombre"] as string,
                    Color = rd["Color"] as string,
                    TipoSecado = rd["TipoSecado"] as string,
                    HorasSecado = rd["HorasSecado"] == DBNull.Value ? null : Convert.ToDecimal(rd["HorasSecado"]),
                    HorasSecadoTexto = rd["HorasSecadoTexto"] as string
                };
                if (soloListos && !(!necesidad.ProgramaProduccionID.HasValue && (necesidad.PiezasAProducir ?? 0) > 0 && !faltaMaterial && !faltaMaquina && !faltaMolde && !faltaCavidades && !faltaCiclo && !faltaObjetivo && !faltaPeso && !faltaEmbalaje && !faltaMPStock && !faltaEmbalajeStock)) continue;
                if (soloPendienteAbasto && !(!necesidad.ProgramaProduccionID.HasValue && (necesidad.PiezasAProducir ?? 0) > 0 && !faltaMaterial && !faltaMaquina && !faltaMolde && !faltaCavidades && !faltaCiclo && !faltaObjetivo && !faltaPeso && !faltaEmbalaje && (faltaMPStock || faltaEmbalajeStock))) continue;
                if (soloPendienteDatosTecnicos && !((necesidad.PiezasAProducir ?? 0) > 0 && (faltaMaterial || faltaMaquina || faltaMolde || faltaCavidades || faltaCiclo || faltaObjetivo || faltaPeso || faltaEmbalaje))) continue;
                vm.Necesidades.Add(necesidad);
            }
            return View(vm);
        }

        /* =====================================================================
         * MODO SIN ALMACEN ACTIVO (2026-07-30)
         * Este metodo queda comentado temporalmente porque el modulo de
         * Planeacion se lanzara sin descontar/apartar PT desde Almacen.
         * Cuando Almacen este listo, quitar este comentario y reactivar
         * tambien las llamadas marcadas como REACTIVAR_ALMACEN.
         * =====================================================================
        private async Task SincronizarApartadosPTAsync(SqlConnection cn)
        {
            const string sql = @"
IF OBJECT_ID('dbo.Planeacion_PT_Apartado', 'U') IS NULL
    RETURN;

DECLARE @Reservas TABLE
(
    ReleaseID INT NULL,
    ReleaseDetalleID INT NOT NULL PRIMARY KEY,
    ClienteID INT NULL,
    ParteID INT NOT NULL,
    CantidadRequerida INT NOT NULL,
    StockFisico INT NOT NULL,
    RequeridoAnterior INT NOT NULL,
    CantidadApartadaNueva INT NOT NULL
);

;WITH Base AS
(
    SELECT
        r.ReleaseID,
        d.ReleaseDetalleID,
        r.ClienteID,
        d.ParteID,
        ISNULL(d.CantidadRequerida, 0) AS CantidadRequerida,
        ISNULL(pt.Disponible, 0) AS StockFisico,
        d.FechaRequerida
    FROM dbo.Planeacion_ReleaseDetalle d
    INNER JOIN dbo.Planeacion_Releases r
        ON r.ReleaseID = d.ReleaseID
    OUTER APPLY
    (
        SELECT TOP 1 ISNULL(Disponible, 0) AS Disponible
        FROM dbo.vw_AlmacenPTInventario
        WHERE ParteID = d.ParteID
    ) pt
    WHERE r.Activo = 1
      AND d.Activo = 1
      AND d.ParteID IS NOT NULL
      AND ISNULL(d.CantidadRequerida, 0) > 0
      AND ISNULL(d.EstatusID, 1) NOT IN (9, 99)
), Calc AS
(
    SELECT
        ReleaseID,
        ReleaseDetalleID,
        ClienteID,
        ParteID,
        CantidadRequerida,
        StockFisico,
        ISNULL
        (
            SUM(CantidadRequerida) OVER
            (
                PARTITION BY ParteID
                ORDER BY FechaRequerida, ReleaseDetalleID
                ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING
            ),
            0
        ) AS RequeridoAnterior
    FROM Base
)
INSERT INTO @Reservas
(
    ReleaseID,
    ReleaseDetalleID,
    ClienteID,
    ParteID,
    CantidadRequerida,
    StockFisico,
    RequeridoAnterior,
    CantidadApartadaNueva
)
SELECT
    ReleaseID,
    ReleaseDetalleID,
    ClienteID,
    ParteID,
    CantidadRequerida,
    StockFisico,
    RequeridoAnterior,
    CASE
        WHEN StockFisico - RequeridoAnterior <= 0 THEN 0
        WHEN StockFisico - RequeridoAnterior >= CantidadRequerida THEN CantidadRequerida
        ELSE StockFisico - RequeridoAnterior
    END AS CantidadApartadaNueva
FROM Calc;

MERGE dbo.Planeacion_PT_Apartado AS tgt
USING
(
    SELECT *
    FROM @Reservas
    WHERE CantidadApartadaNueva > 0
) AS src
ON tgt.ReleaseDetalleID = src.ReleaseDetalleID
   AND tgt.Activo = 1
   AND tgt.EstatusID = 1
WHEN MATCHED THEN
    UPDATE SET
        tgt.ReleaseID = src.ReleaseID,
        tgt.ClienteID = src.ClienteID,
        tgt.ParteID = src.ParteID,
        tgt.CantidadApartada = src.CantidadApartadaNueva,
        tgt.FechaModificacion = GETDATE(),
        tgt.Observaciones = 'Apartado recalculado automáticamente desde Programa de Planeación.'
WHEN NOT MATCHED BY TARGET THEN
    INSERT
    (
        ReleaseID,
        ReleaseDetalleID,
        ClienteID,
        ParteID,
        CantidadApartada,
        FechaApartado,
        EstatusID,
        Activo,
        Observaciones
    )
    VALUES
    (
        src.ReleaseID,
        src.ReleaseDetalleID,
        src.ClienteID,
        src.ParteID,
        src.CantidadApartadaNueva,
        GETDATE(),
        1,
        1,
        'Apartado creado automáticamente desde Programa de Planeación.'
    );

UPDATE a
SET
    a.Activo = 0,
    a.EstatusID = 9,
    a.FechaModificacion = GETDATE(),
    a.Observaciones = 'Apartado liberado automáticamente: ya no hay PT disponible o el release dejó de aplicar.'
FROM dbo.Planeacion_PT_Apartado a
LEFT JOIN @Reservas r
    ON r.ReleaseDetalleID = a.ReleaseDetalleID
WHERE a.Activo = 1
  AND a.EstatusID = 1
  AND
  (
        r.ReleaseDetalleID IS NULL
     OR r.CantidadApartadaNueva <= 0
  );";

            await using var cmd = new SqlCommand(sql, cn);
            cmd.CommandTimeout = 120;
            await cmd.ExecuteNonQueryAsync();
        }
         */

        // CALENDARIO_MAQUINAS_V1_0
        [HttpGet]
        public async Task<IActionResult> CalendarioMaquinas(DateTime? semana)
        {
            var fechaBase = (semana ?? DateTime.Today).Date;
            var diasDesdeLunes = ((int)fechaBase.DayOfWeek + 6) % 7;
            var inicioSemana = fechaBase.AddDays(-diasDesdeLunes).AddHours(7);
            var finSemana = inicioSemana.AddDays(7);

            var programasConsulta = await ObtenerProgramasPorRangoAsync(
                inicioSemana.Date,
                finSemana.Date);

            var programas = programasConsulta
                .Where(x =>
                    x.FechaInicioProgramada.HasValue &&
                    x.FechaInicioProgramada.Value < finSemana &&
                    (x.FechaFinProgramada ?? x.FechaInicioProgramada.Value.AddHours(1)) >= inicioSemana)
                .ToList();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            var maquinas = await ObtenerMaquinasAsync(cn);

            foreach (var maquina in maquinas)
            {
                maquina.Programas = programas
                    .Where(x => x.MaquinaID == maquina.MaquinaID)
                    .OrderBy(x => x.FechaInicioProgramada)
                    .ThenBy(x => x.SecuenciaMaquina)
                    .ThenBy(x => x.ProgramaProduccionID)
                    .ToList();
            }

            var sinAsignar = programas
                .Where(x => !x.MaquinaID.HasValue)
                .OrderBy(x => x.FechaInicioProgramada)
                .ThenBy(x => x.ProgramaProduccionID)
                .ToList();

            if (sinAsignar.Any())
            {
                maquinas.Insert(0, new PlaneacionProgramaMaquinaVm
                {
                    MaquinaID = null,
                    MaquinaCodigo = "SIN ASIGNAR",
                    MaquinaNombre = "Programas pendientes de máquina",
                    Programas = sinAsignar
                });
            }

            var vm = new PlaneacionProgramaMaquinasVm
            {
                FechaDesde = inicioSemana,
                FechaHasta = finSemana,
                Maquinas = maquinas
            };

            ViewBag.SemanaInicio = inicioSemana;
            ViewBag.SemanaFin = finSemana;

            return View(vm);
        }

        // La reprogramación oficial se realiza únicamente desde
        // PlaneacionCalendarioMaquinasController. Se conserva esta acción
        // para no romper llamadas antiguas, pero ya no modifica la base.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ReprogramarCalendario(
            [FromBody] CalendarioMaquinasMoverRequest request)
        {
            return Json(new
            {
                ok = false,
                redireccion = Url.Action(
                    "Index",
                    "PlaneacionCalendarioMaquinas"),
                mensaje =
                    "Esta ruta de reprogramación fue reemplazada. " +
                    "Utiliza el Calendario de Máquinas para mover el programa."
            });
        }


        private async Task SincronizarReleaseDesdeReprogramacionAsync(
    int programaProduccionId,
    int? releaseDetalleId,
    DateTime inicioNuevo,
    DateTime finNuevo,
    int usuarioId,
    SqlConnection cn,
    SqlTransaction tx)
        {
            if (!releaseDetalleId.HasValue || releaseDetalleId.Value <= 0)
                return;

            DateTime? fechaRequeridaCliente = null;

            const string sqlObtenerReleaseDetalle = @"
SELECT TOP 1
    FechaRequerida
FROM dbo.Planeacion_ReleaseDetalle
WHERE ReleaseDetalleID = @ReleaseDetalleID
  AND Activo = 1;";

            await using (var cmd = new SqlCommand(sqlObtenerReleaseDetalle, cn, tx))
            {
                cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value = releaseDetalleId.Value;

                var result = await cmd.ExecuteScalarAsync();

                if (result != null && result != DBNull.Value)
                    fechaRequeridaCliente = Convert.ToDateTime(result).Date;
            }

            bool? daTiempo = null;
            string mensajeCapacidad;

            if (fechaRequeridaCliente.HasValue)
            {
                daTiempo = finNuevo.Date <= fechaRequeridaCliente.Value.Date;

                mensajeCapacidad = daTiempo.Value
                    ? $"Programación actualizada: termina el {finNuevo:dd/MM/yyyy HH:mm}, dentro de la fecha requerida del cliente ({fechaRequeridaCliente:dd/MM/yyyy})."
                    : $"Programación actualizada: termina el {finNuevo:dd/MM/yyyy HH:mm}, posterior a la fecha requerida del cliente ({fechaRequeridaCliente:dd/MM/yyyy}).";
            }
            else
            {
                mensajeCapacidad = $"Programación actualizada: inicio {inicioNuevo:dd/MM/yyyy HH:mm}, fin {finNuevo:dd/MM/yyyy HH:mm}.";
            }

            const string sqlActualizarReleaseDetalle = @"
UPDATE dbo.Planeacion_ReleaseDetalle
SET
    DaTiempo = @DaTiempo,
    FechaInicioSugerida = @FechaInicioProgramada,
    FechaFinEstimada = @FechaFinProgramada,
    MensajeCapacidad = @MensajeCapacidad,
    FechaModificacion = GETDATE(),
    UsuarioModificacionID = @UsuarioModificacionID
WHERE ReleaseDetalleID = @ReleaseDetalleID
  AND Activo = 1;";

            await using (var cmd = new SqlCommand(sqlActualizarReleaseDetalle, cn, tx))
            {
                cmd.Parameters.Add("@DaTiempo", SqlDbType.Bit).Value =
                    daTiempo.HasValue ? daTiempo.Value : DBNull.Value;

                cmd.Parameters.Add("@FechaInicioProgramada", SqlDbType.DateTime).Value = inicioNuevo;
                cmd.Parameters.Add("@FechaFinProgramada", SqlDbType.DateTime).Value = finNuevo;
                cmd.Parameters.Add("@MensajeCapacidad", SqlDbType.NVarChar, 500).Value = mensajeCapacidad;
                cmd.Parameters.Add("@UsuarioModificacionID", SqlDbType.Int).Value = usuarioId;
                cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value = releaseDetalleId.Value;

                await cmd.ExecuteNonQueryAsync();
            }

            const string sqlActualizarReleasePadre = @"
UPDATE r
SET
    r.FechaModificacion = GETDATE(),
    r.UsuarioModificacionID = @UsuarioModificacionID
FROM dbo.Planeacion_Releases r
INNER JOIN dbo.Planeacion_ReleaseDetalle d
    ON d.ReleaseID = r.ReleaseID
WHERE d.ReleaseDetalleID = @ReleaseDetalleID
  AND r.Activo = 1;";

            await using (var cmd = new SqlCommand(sqlActualizarReleasePadre, cn, tx))
            {
                cmd.Parameters.Add("@UsuarioModificacionID", SqlDbType.Int).Value = usuarioId;
                cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value = releaseDetalleId.Value;

                await cmd.ExecuteNonQueryAsync();
            }
        }

        private async Task InsertarHistorialReprogramacionProgramaAsync(
    int programaProduccionId,
    int? maquinaAnteriorId,
    int? maquinaNuevaId,
    DateTime inicioAnterior,
    DateTime inicioNuevo,
    DateTime finAnterior,
    DateTime finNuevo,
    decimal horasAnteriores,
    decimal horasNuevas,
    TimeSpan? cambioAnterior,
    TimeSpan? cambioNuevo,
    TimeSpan? arranqueAnterior,
    TimeSpan? arranqueNuevo,
    int? releaseDetalleId,
    int? solicitudProduccionId,
    int? solicitudProduccionDetalleId,
    int usuarioId,
    string? motivo,
    SqlConnection cn,
    SqlTransaction tx)
        {
            DateTime? fechaRequeridaCliente = null;
            bool? daTiempoDespues = null;

            if (releaseDetalleId.HasValue && releaseDetalleId.Value > 0)
            {
                const string sqlFechaRequerida = @"
SELECT TOP 1
    FechaRequerida
FROM dbo.Planeacion_ReleaseDetalle
WHERE ReleaseDetalleID = @ReleaseDetalleID
  AND Activo = 1;";

                await using var cmdFecha = new SqlCommand(sqlFechaRequerida, cn, tx);
                cmdFecha.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value = releaseDetalleId.Value;

                var result = await cmdFecha.ExecuteScalarAsync();

                if (result != null && result != DBNull.Value)
                {
                    fechaRequeridaCliente = Convert.ToDateTime(result).Date;
                    daTiempoDespues = finNuevo.Date <= fechaRequeridaCliente.Value.Date;
                }
            }

            const string sql = @"
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

    DaTiempoDespues,
    FechaRequeridaCliente,

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

    @DaTiempoDespues,
    @FechaRequeridaCliente,

    @UsuarioID,
    GETDATE(),

    @Motivo
);";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaProduccionId;

            cmd.Parameters.Add("@MaquinaAnteriorID", SqlDbType.Int).Value =
                (object?)maquinaAnteriorId ?? DBNull.Value;

            cmd.Parameters.Add("@MaquinaNuevaID", SqlDbType.Int).Value =
                (object?)maquinaNuevaId ?? DBNull.Value;

            cmd.Parameters.Add("@InicioAnterior", SqlDbType.DateTime).Value = inicioAnterior;
            cmd.Parameters.Add("@InicioNuevo", SqlDbType.DateTime).Value = inicioNuevo;

            cmd.Parameters.Add("@FinAnterior", SqlDbType.DateTime).Value = finAnterior;
            cmd.Parameters.Add("@FinNuevo", SqlDbType.DateTime).Value = finNuevo;

            var horasAnterioresParam = cmd.Parameters.Add("@HorasAnteriores", SqlDbType.Decimal);
            horasAnterioresParam.Precision = 18;
            horasAnterioresParam.Scale = 2;
            horasAnterioresParam.Value = Math.Round(horasAnteriores, 2);

            var horasNuevasParam = cmd.Parameters.Add("@HorasNuevas", SqlDbType.Decimal);
            horasNuevasParam.Precision = 18;
            horasNuevasParam.Scale = 2;
            horasNuevasParam.Value = Math.Round(horasNuevas, 2);

            cmd.Parameters.Add("@CambioAnterior", SqlDbType.Time).Value =
                (object?)cambioAnterior ?? DBNull.Value;

            cmd.Parameters.Add("@CambioNuevo", SqlDbType.Time).Value =
                (object?)cambioNuevo ?? DBNull.Value;

            cmd.Parameters.Add("@ArranqueAnterior", SqlDbType.Time).Value =
                (object?)arranqueAnterior ?? DBNull.Value;

            cmd.Parameters.Add("@ArranqueNuevo", SqlDbType.Time).Value =
                (object?)arranqueNuevo ?? DBNull.Value;

            cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value =
                (object?)releaseDetalleId ?? DBNull.Value;

            cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value =
                (object?)solicitudProduccionId ?? DBNull.Value;

            cmd.Parameters.Add("@SolicitudProduccionDetalleID", SqlDbType.Int).Value =
                (object?)solicitudProduccionDetalleId ?? DBNull.Value;

            cmd.Parameters.Add("@DaTiempoDespues", SqlDbType.Bit).Value =
                daTiempoDespues.HasValue ? daTiempoDespues.Value : DBNull.Value;

            cmd.Parameters.Add("@FechaRequeridaCliente", SqlDbType.Date).Value =
                fechaRequeridaCliente.HasValue ? fechaRequeridaCliente.Value.Date : DBNull.Value;

            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;

            cmd.Parameters.Add("@Motivo", SqlDbType.NVarChar, 500).Value =
                string.IsNullOrWhiteSpace(motivo)
                    ? DBNull.Value
                    : motivo.Trim();

            await cmd.ExecuteNonQueryAsync();
        }


        [HttpGet]
        public async Task<IActionResult> Maquinas(DateTime? fechaDesde, DateTime? fechaHasta)
        {
            var desde = fechaDesde?.Date ?? DateTime.Today;
            var hasta = fechaHasta?.Date ?? desde.AddDays(7);

            if (hasta < desde)
                hasta = desde;

            var programas = await ObtenerProgramasPorRangoAsync(desde, hasta);

            var maquinas = programas
                .GroupBy(x => new
                {
                    x.MaquinaID,
                    x.MaquinaCodigo,
                    x.MaquinaNombre
                })
                .Select(g => new PlaneacionProgramaMaquinaVm
                {
                    MaquinaID = g.Key.MaquinaID,
                    MaquinaCodigo = g.Key.MaquinaCodigo,
                    MaquinaNombre = g.Key.MaquinaNombre,
                    Programas = g
                        .OrderBy(x => x.FechaInicioProgramada)
                        .ThenBy(x => x.SecuenciaMaquina)
                        .ThenBy(x => x.ProgramaProduccionID)
                        .ToList()
                })
                .OrderBy(x => x.MaquinaCodigo)
                .ToList();

            var vm = new PlaneacionProgramaMaquinasVm
            {
                FechaDesde = desde,
                FechaHasta = hasta,
                Maquinas = maquinas
            };

            return View(vm);
        }

        [HttpGet]
        public async Task<IActionResult> CrearDesdeNecesidad(int releaseDetalleId)
        {
            if (releaseDetalleId <= 0) return BadRequest();
            var vm = await ObtenerNecesidadParaProgramaAsync(releaseDetalleId);
            if (vm == null) { TempData["Error"] = "No se encontró la necesidad seleccionada."; return RedirectToAction(nameof(Index)); }
            vm.ProductoIncompletoDisponible = await ObtenerProductoIncompletoDisponibleAsync(releaseDetalleId);
            if (vm.PiezasAProducir <= 0) { TempData["Error"] = "La necesidad seleccionada ya no tiene piezas pendientes por producir."; return RedirectToAction(nameof(Index)); }
            vm.CantidadProgramada = vm.PiezasAProducir;
            var horaBase = RedondearSiguienteHora(DateTime.Now);
            vm.FechaInicioProgramada = horaBase;
            if (vm.MaquinaID.HasValue)
            {
                var sugerencia = await ObtenerSiguienteCambioDisponibleAsync(vm.MaquinaID.Value, horaBase, vm.ParteID, vm.MoldeID);
                vm.FechaInicioProgramada = sugerencia.Cambio;
                vm.Cambio = sugerencia.Cambio.TimeOfDay;
                vm.Arranque = sugerencia.Arranque.TimeOfDay;
                if (vm.HorasProgramadas.HasValue && vm.HorasProgramadas.Value > 0) vm.FechaFinProgramada = SumarHorasOperativasPlaneacion(sugerencia.Arranque, vm.HorasProgramadas.Value, false);
                if (sugerencia.OmiteHoraCambio) vm.Observaciones = string.IsNullOrWhiteSpace(vm.Observaciones) ? sugerencia.Motivo : vm.Observaciones + Environment.NewLine + sugerencia.Motivo;
            }
            else
            {
                vm.Cambio = horaBase.TimeOfDay;
                vm.Arranque = horaBase.AddHours(1).TimeOfDay;
                if (vm.HorasProgramadas.HasValue && vm.HorasProgramadas.Value > 0) vm.FechaFinProgramada = SumarHorasOperativasPlaneacion(horaBase.AddHours(1), vm.HorasProgramadas.Value, false);
            }
            await CargarCatalogosAsync(vm);
            return View("Crear", vm);
        }

        private async Task<List<PlaneacionProductoIncompletoDisponibleVm>> ObtenerProductoIncompletoDisponibleAsync(int releaseDetalleId)
        {
            var lista = new List<PlaneacionProductoIncompletoDisponibleVm>();
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            const string sql = @"
DECLARE @ParteID INT,@CapacidadEsperada INT;
SELECT @ParteID=d.ParteID,@CapacidadEsperada=TRY_CONVERT(INT,COALESCE(d.PiezasPorEmbalaje,t.PiezasPorEmbalaje,t.PiezasPorCaja))
FROM dbo.Planeacion_ReleaseDetalle d
LEFT JOIN dbo.ERP_ParteDatosTecnicos t ON t.ParteID=d.ParteID AND t.Activo=1
WHERE d.ReleaseDetalleID=@ReleaseDetalleID AND d.Activo=1;
IF @ParteID IS NULL RETURN;
SELECT c.CajaProduccionID,c.EtiquetaBlanca,c.FolioCaja,ISNULL(c.CantidadPiezas,ISNULL(c.Cantidad,0)) AS CantidadPiezas,
ISNULL(c.CapacidadObjetivoCaja,0) AS CapacidadObjetivoCaja,
ISNULL(c.CantidadPendienteCompletar,CASE WHEN ISNULL(c.CapacidadObjetivoCaja,0)>ISNULL(c.CantidadPiezas,ISNULL(c.Cantidad,0)) THEN ISNULL(c.CapacidadObjetivoCaja,0)-ISNULL(c.CantidadPiezas,ISNULL(c.Cantidad,0)) ELSE 0 END) AS CantidadPendienteCompletar,
ISNULL(c.FechaFormacion,c.FechaCreacion) AS FechaFormacion,
CASE WHEN propio.ProductoIncompletoApartadoID IS NULL THEN CAST(0 AS BIT) ELSE CAST(1 AS BIT) END AS ApartadaEstaNecesidad
FROM dbo.Produccion_Cajas c
INNER JOIN dbo.Produccion_Ejecucion e ON e.EjecucionProduccionID=c.EjecucionProduccionID
OUTER APPLY(SELECT TOP(1)a.ProductoIncompletoApartadoID FROM dbo.Planeacion_ProductoIncompletoApartado a WHERE a.CajaProduccionID=c.CajaProduccionID AND a.ReleaseDetalleID=@ReleaseDetalleID AND a.Activo=1 AND a.EstatusID IN(1,2,3,4))propio
OUTER APPLY(SELECT TOP(1)a.ProductoIncompletoApartadoID FROM dbo.Planeacion_ProductoIncompletoApartado a WHERE a.CajaProduccionID=c.CajaProduccionID AND a.ReleaseDetalleID<>@ReleaseDetalleID AND a.Activo=1 AND a.EstatusID IN(1,2,3,4))otro
WHERE c.Activo=1 AND ISNULL(c.EsProductoIncompleto,0)=1 AND e.ParteID=@ParteID
AND UPPER(LTRIM(RTRIM(ISNULL(c.EstadoProductoIncompleto,N'')))) IN(N'DISPONIBLE',N'RESERVADA')
AND otro.ProductoIncompletoApartadoID IS NULL
AND(@CapacidadEsperada IS NULL OR @CapacidadEsperada<=0 OR ISNULL(c.CapacidadObjetivoCaja,0)=@CapacidadEsperada)
ORDER BY CASE WHEN propio.ProductoIncompletoApartadoID IS NOT NULL THEN 0 ELSE 1 END,ISNULL(c.FechaFormacion,c.FechaCreacion),c.CajaProduccionID;";
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value = releaseDetalleId;
            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                lista.Add(new PlaneacionProductoIncompletoDisponibleVm
                {
                    CajaProduccionID = Convert.ToInt64(rd["CajaProduccionID"]),
                    EtiquetaBlanca = rd["EtiquetaBlanca"] == DBNull.Value ? null : rd["EtiquetaBlanca"].ToString(),
                    FolioCaja = rd["FolioCaja"] == DBNull.Value ? null : rd["FolioCaja"].ToString(),
                    CantidadPiezas = Convert.ToInt32(rd["CantidadPiezas"]),
                    CapacidadObjetivoCaja = Convert.ToInt32(rd["CapacidadObjetivoCaja"]),
                    CantidadPendienteCompletar = Convert.ToInt32(rd["CantidadPendienteCompletar"]),
                    FechaFormacion = rd["FechaFormacion"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaFormacion"]),
                    ApartadaEstaNecesidad = Convert.ToBoolean(rd["ApartadaEstaNecesidad"])
                });
            }
            return lista;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AplicarProductoIncompleto(PlaneacionProductoIncompletoSeleccionVm vm)
        {
            if (vm.ReleaseDetalleID <= 0) { TempData["Error"] = "No se recibió la necesidad."; return RedirectToAction(nameof(Index)); }
            var usuarioId = ObtenerUsuarioID();
            if (usuarioId <= 0) { TempData["Error"] = "No se pudo identificar al usuario."; return RedirectToAction(nameof(Index)); }
            var seleccionadas = (vm.CajasProduccionID ?? new List<long>()).Where(x => x > 0).Distinct().ToList();
            if (!seleccionadas.Any()) { TempData["Info"] = "No seleccionaste producto incompleto."; return RedirectToAction(nameof(CrearDesdeNecesidad), new { releaseDetalleId = vm.ReleaseDetalleID }); }
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                int parteId, cantidadRequerida;
                int? programaId, capacidadEsperada;
                const string sqlNecesidad = @"
SELECT TOP(1)d.ParteID,ISNULL(d.CantidadRequerida,0) AS CantidadRequerida,d.ProgramaProduccionID,
TRY_CONVERT(INT,COALESCE(d.PiezasPorEmbalaje,t.PiezasPorEmbalaje,t.PiezasPorCaja)) AS CapacidadEsperada
FROM dbo.Planeacion_ReleaseDetalle d WITH(UPDLOCK,HOLDLOCK)
LEFT JOIN dbo.ERP_ParteDatosTecnicos t ON t.ParteID=d.ParteID AND t.Activo=1
WHERE d.ReleaseDetalleID=@ReleaseDetalleID AND d.Activo=1;";
                await using (var cmd = new SqlCommand(sqlNecesidad, cn, tx))
                {
                    cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value = vm.ReleaseDetalleID;
                    await using var rd = await cmd.ExecuteReaderAsync();
                    if (!await rd.ReadAsync()) { await tx.RollbackAsync(); TempData["Error"] = "No se encontró la necesidad."; return RedirectToAction(nameof(Index)); }
                    parteId = rd["ParteID"] == DBNull.Value ? 0 : Convert.ToInt32(rd["ParteID"]);
                    cantidadRequerida = Convert.ToInt32(rd["CantidadRequerida"]);
                    programaId = rd["ProgramaProduccionID"] == DBNull.Value ? null : Convert.ToInt32(rd["ProgramaProduccionID"]);
                    capacidadEsperada = rd["CapacidadEsperada"] == DBNull.Value ? null : Convert.ToInt32(rd["CapacidadEsperada"]);
                }
                if (programaId.HasValue) { await tx.RollbackAsync(); TempData["Error"] = "La necesidad ya fue programada. Ya no es posible cambiar el producto incompleto apartado."; return RedirectToAction(nameof(Index)); }
                if (parteId <= 0) { await tx.RollbackAsync(); TempData["Error"] = "La necesidad no tiene una parte válida."; return RedirectToAction(nameof(Index)); }
                const string sqlProgramado = @"SELECT ISNULL(SUM(ISNULL(CantidadProgramada,0)-ISNULL(CantidadProducida,0)),0) FROM dbo.Planeacion_ProgramaProduccion WHERE ReleaseDetalleID=@ReleaseDetalleID AND Activo=1 AND ISNULL(EstatusID,1) NOT IN(5,9,99);";
                int programadoPendiente;
                await using (var cmd = new SqlCommand(sqlProgramado, cn, tx))
                {
                    cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value = vm.ReleaseDetalleID;
                    programadoPendiente = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                }
                var pendienteMaximo = Math.Max(0, cantidadRequerida - programadoPendiente);
                if (pendienteMaximo <= 0) { await tx.RollbackAsync(); TempData["Error"] = "La necesidad ya está cubierta por producción programada."; return RedirectToAction(nameof(Index)); }
                var seleccionValidada = new List<(long CajaProduccionID, string Etiqueta, int Cantidad)>();
                foreach (var cajaId in seleccionadas)
                {
                    const string sqlCaja = @"
SELECT TOP(1)c.CajaProduccionID,c.EtiquetaBlanca,ISNULL(c.CantidadPiezas,ISNULL(c.Cantidad,0)) AS CantidadPiezas,
ISNULL(c.CapacidadObjetivoCaja,0) AS CapacidadObjetivoCaja,ISNULL(c.EsProductoIncompleto,0) AS EsProductoIncompleto,
ISNULL(c.EstadoProductoIncompleto,N'') AS EstadoProductoIncompleto,e.ParteID,
(SELECT TOP(1)a.ReleaseDetalleID FROM dbo.Planeacion_ProductoIncompletoApartado a WHERE a.CajaProduccionID=c.CajaProduccionID AND a.Activo=1 AND a.EstatusID IN(1,2,3,4)) AS ReleaseApartado
FROM dbo.Produccion_Cajas c WITH(UPDLOCK,HOLDLOCK)
INNER JOIN dbo.Produccion_Ejecucion e ON e.EjecucionProduccionID=c.EjecucionProduccionID
WHERE c.CajaProduccionID=@CajaProduccionID AND c.Activo=1;";
                    await using var cmd = new SqlCommand(sqlCaja, cn, tx);
                    cmd.Parameters.Add("@CajaProduccionID", SqlDbType.BigInt).Value = cajaId;
                    await using var rd = await cmd.ExecuteReaderAsync();
                    if (!await rd.ReadAsync()) { await tx.RollbackAsync(); TempData["Error"] = $"No se encontró la caja {cajaId}."; return RedirectToAction(nameof(CrearDesdeNecesidad), new { releaseDetalleId = vm.ReleaseDetalleID }); }
                    var parteCaja = rd["ParteID"] == DBNull.Value ? 0 : Convert.ToInt32(rd["ParteID"]);
                    var cantidad = Convert.ToInt32(rd["CantidadPiezas"]);
                    var capacidadCaja = Convert.ToInt32(rd["CapacidadObjetivoCaja"]);
                    var etiqueta = rd["EtiquetaBlanca"] == DBNull.Value ? $"Caja {cajaId}" : rd["EtiquetaBlanca"].ToString()!;
                    var esIncompleto = Convert.ToBoolean(rd["EsProductoIncompleto"]);
                    var estado = rd["EstadoProductoIncompleto"]?.ToString()?.Trim().ToUpperInvariant() ?? string.Empty;
                    var releaseApartado = rd["ReleaseApartado"] == DBNull.Value ? (int?)null : Convert.ToInt32(rd["ReleaseApartado"]);
                    if (parteCaja != parteId) { await tx.RollbackAsync(); TempData["Error"] = $"{etiqueta} corresponde a una pieza diferente."; return RedirectToAction(nameof(CrearDesdeNecesidad), new { releaseDetalleId = vm.ReleaseDetalleID }); }
                    if (capacidadEsperada.HasValue && capacidadEsperada.Value > 0 && capacidadCaja != capacidadEsperada.Value) { await tx.RollbackAsync(); TempData["Error"] = $"{etiqueta} tiene capacidad de {capacidadCaja:N0} piezas y esta necesidad requiere caja de {capacidadEsperada.Value:N0}."; return RedirectToAction(nameof(CrearDesdeNecesidad), new { releaseDetalleId = vm.ReleaseDetalleID }); }
                    if (!esIncompleto || cantidad <= 0) { await tx.RollbackAsync(); TempData["Error"] = $"{etiqueta} ya no contiene producto incompleto válido."; return RedirectToAction(nameof(CrearDesdeNecesidad), new { releaseDetalleId = vm.ReleaseDetalleID }); }
                    if (estado != "DISPONIBLE" && !(estado == "RESERVADA" && releaseApartado == vm.ReleaseDetalleID)) { await tx.RollbackAsync(); TempData["Error"] = $"{etiqueta} ya no está disponible."; return RedirectToAction(nameof(CrearDesdeNecesidad), new { releaseDetalleId = vm.ReleaseDetalleID }); }
                    if (releaseApartado.HasValue && releaseApartado.Value != vm.ReleaseDetalleID) { await tx.RollbackAsync(); TempData["Error"] = $"{etiqueta} ya fue apartada para otra necesidad."; return RedirectToAction(nameof(CrearDesdeNecesidad), new { releaseDetalleId = vm.ReleaseDetalleID }); }
                    seleccionValidada.Add((cajaId, etiqueta, cantidad));
                }
                var totalSeleccionado = seleccionValidada.Sum(x => x.Cantidad);
                if (totalSeleccionado > pendienteMaximo) { await tx.RollbackAsync(); TempData["Error"] = $"Seleccionaste {totalSeleccionado:N0} piezas de producto incompleto, pero la necesidad pendiente es de {pendienteMaximo:N0}. No se permite dividir una etiqueta blanca entre necesidades."; return RedirectToAction(nameof(CrearDesdeNecesidad), new { releaseDetalleId = vm.ReleaseDetalleID }); }
                var idsSeleccionados = seleccionValidada.Select(x => x.CajaProduccionID).ToHashSet();
                const string sqlApartadosActuales = @"SELECT CajaProduccionID FROM dbo.Planeacion_ProductoIncompletoApartado WHERE ReleaseDetalleID=@ReleaseDetalleID AND Activo=1 AND EstatusID=1;";
                var apartadasActuales = new List<long>();
                await using (var cmd = new SqlCommand(sqlApartadosActuales, cn, tx))
                {
                    cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value = vm.ReleaseDetalleID;
                    await using var rd = await cmd.ExecuteReaderAsync();
                    while (await rd.ReadAsync()) apartadasActuales.Add(Convert.ToInt64(rd["CajaProduccionID"]));
                }
                foreach (var cajaId in apartadasActuales.Where(x => !idsSeleccionados.Contains(x)))
                {
                    const string sqlLiberar = @"
UPDATE dbo.Planeacion_ProductoIncompletoApartado SET EstatusID=6,Activo=0,Observaciones=LEFT(COALESCE(NULLIF(Observaciones,N'')+N' | ',N'')+N'Liberada por cambio de selección en Planeación.',500)
WHERE CajaProduccionID=@CajaProduccionID AND ReleaseDetalleID=@ReleaseDetalleID AND Activo=1 AND EstatusID=1;
UPDATE dbo.Produccion_Cajas SET EstadoProductoIncompleto=N'DISPONIBLE',ProgramaReservaID=NULL,SolicitudReservaID=NULL,SolicitudDetalleReservaID=NULL,EjecucionReservaID=NULL,
FechaReservaIncompleto=NULL,UsuarioReservaIncompletoID=NULL,UsuarioModificacionID=@UsuarioID,FechaModificacion=SYSDATETIME()
WHERE CajaProduccionID=@CajaProduccionID AND Activo=1 AND EsProductoIncompleto=1;";
                    await using var cmd = new SqlCommand(sqlLiberar, cn, tx);
                    cmd.Parameters.Add("@CajaProduccionID", SqlDbType.BigInt).Value = cajaId;
                    cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value = vm.ReleaseDetalleID;
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                    await cmd.ExecuteNonQueryAsync();
                }
                foreach (var caja in seleccionValidada)
                {
                    const string sqlExiste = @"SELECT COUNT(1) FROM dbo.Planeacion_ProductoIncompletoApartado WHERE CajaProduccionID=@CajaProduccionID AND ReleaseDetalleID=@ReleaseDetalleID AND Activo=1 AND EstatusID=1;";
                    int existe;
                    await using (var cmd = new SqlCommand(sqlExiste, cn, tx))
                    {
                        cmd.Parameters.Add("@CajaProduccionID", SqlDbType.BigInt).Value = caja.CajaProduccionID;
                        cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value = vm.ReleaseDetalleID;
                        existe = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                    }
                    if (existe == 0)
                    {
                        const string sqlInsert = @"
INSERT INTO dbo.Planeacion_ProductoIncompletoApartado
(CajaProduccionID,ParteID,ReleaseDetalleID,ProgramaProduccionID,SolicitudProduccionID,SolicitudProduccionDetalleID,EjecucionProduccionID,CantidadApartada,EstatusID,UsuarioApartadoID,FechaApartado,Observaciones,Activo)
VALUES(@CajaProduccionID,@ParteID,@ReleaseDetalleID,NULL,NULL,NULL,NULL,@CantidadApartada,1,@UsuarioID,SYSDATETIME(),@Observaciones,1);";
                        await using var cmd = new SqlCommand(sqlInsert, cn, tx);
                        cmd.Parameters.Add("@CajaProduccionID", SqlDbType.BigInt).Value = caja.CajaProduccionID;
                        cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value = parteId;
                        cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value = vm.ReleaseDetalleID;
                        cmd.Parameters.Add("@CantidadApartada", SqlDbType.Int).Value = caja.Cantidad;
                        cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                        cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 500).Value = $"Etiqueta blanca {caja.Etiqueta} apartada voluntariamente por Planeación.";
                        await cmd.ExecuteNonQueryAsync();
                    }
                    const string sqlReservar = @"
UPDATE dbo.Produccion_Cajas SET EstadoProductoIncompleto=N'RESERVADA',FechaReservaIncompleto=COALESCE(FechaReservaIncompleto,SYSDATETIME()),
UsuarioReservaIncompletoID=@UsuarioID,UsuarioModificacionID=@UsuarioID,FechaModificacion=SYSDATETIME()
WHERE CajaProduccionID=@CajaProduccionID AND Activo=1 AND EsProductoIncompleto=1;";
                    await using var cmdReserva = new SqlCommand(sqlReservar, cn, tx);
                    cmdReserva.Parameters.Add("@CajaProduccionID", SqlDbType.BigInt).Value = caja.CajaProduccionID;
                    cmdReserva.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                    await cmdReserva.ExecuteNonQueryAsync();
                }
                await tx.CommitAsync();
                TempData["Success"] = $"Planeación apartó {totalSeleccionado:N0} pieza(s) de producto incompleto. La producción requerida se recalculó.";
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                TempData["Error"] = "No fue posible aplicar el producto incompleto: " + ex.Message;
            }
            return RedirectToAction(nameof(CrearDesdeNecesidad), new { releaseDetalleId = vm.ReleaseDetalleID });
        }

        private async Task RecalcularCantidadesProgramaAsync(PlaneacionProgramaCrearDesdeNecesidadVm vm, SqlConnection cn, SqlTransaction tx)
        {
            const string sql = @"
SELECT d.CantidadRequerida,
COALESCE(d.PesoBrutoPieza,t.PesoBrutoPieza) AS PesoBrutoPieza,
COALESCE(d.PiezasPorEmbalaje,t.PiezasPorEmbalaje) AS PiezasPorEmbalaje,
COALESCE(t.ObjetivoHora,0) AS ObjetivoHora,
ISNULL(prog.ProgramadoPendiente,0) AS ProgramadoPendiente,
ISNULL(blanca.CantidadApartada,0) AS ProductoIncompletoApartado,
ISNULL(blanca.CantidadCajas,0) AS CantidadCajasProductoIncompleto
FROM dbo.Planeacion_ReleaseDetalle d WITH(UPDLOCK,HOLDLOCK)
LEFT JOIN dbo.ERP_ParteDatosTecnicos t ON t.ParteID=d.ParteID AND t.Activo=1
OUTER APPLY
(
    SELECT ISNULL(SUM(ISNULL(pp.CantidadProgramada,0)-ISNULL(pp.CantidadProducida,0)),0) AS ProgramadoPendiente
    FROM dbo.Planeacion_ProgramaProduccion pp
    WHERE pp.ReleaseDetalleID=d.ReleaseDetalleID AND pp.Activo=1 AND ISNULL(pp.EstatusID,1) NOT IN(5,9,99)
)prog
OUTER APPLY
(
    SELECT ISNULL(SUM(a.CantidadApartada),0) AS CantidadApartada,COUNT_BIG(*) AS CantidadCajas
    FROM dbo.Planeacion_ProductoIncompletoApartado a
    WHERE a.ReleaseDetalleID=d.ReleaseDetalleID AND a.Activo=1 AND a.EstatusID=1
)blanca
WHERE d.ReleaseDetalleID=@ReleaseDetalleID AND d.Activo=1;";
            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value = vm.ReleaseDetalleID;
            await using var rd = await cmd.ExecuteReaderAsync();
            if (!await rd.ReadAsync()) throw new InvalidOperationException("No se encontró el renglón de Release para recalcular la programación.");
            var cantidadRequerida = rd["CantidadRequerida"] == DBNull.Value ? 0 : Convert.ToInt32(rd["CantidadRequerida"]);
            var programadoPendiente = rd["ProgramadoPendiente"] == DBNull.Value ? 0 : Convert.ToInt32(rd["ProgramadoPendiente"]);
            var productoIncompleto = rd["ProductoIncompletoApartado"] == DBNull.Value ? 0 : Convert.ToInt32(rd["ProductoIncompletoApartado"]);
            var cantidadCajasBlancas = rd["CantidadCajasProductoIncompleto"] == DBNull.Value ? 0 : Convert.ToInt32(rd["CantidadCajasProductoIncompleto"]);
            var pesoBruto = rd["PesoBrutoPieza"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(rd["PesoBrutoPieza"]);
            var piezasPorEmbalaje = rd["PiezasPorEmbalaje"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(rd["PiezasPorEmbalaje"]);
            var objetivoHora = rd["ObjetivoHora"] == DBNull.Value ? 0 : Convert.ToInt32(rd["ObjetivoHora"]);
            var cantidadOriginalAProducir = Math.Max(0, cantidadRequerida - programadoPendiente);
            var cantidadBaseProgramar =
                Math.Max(
                    0,
                    cantidadOriginalAProducir -
                    productoIncompleto);

            var cantidadObjetivoEmpaque =
                RedondearCantidadPorEmbalaje(
                    cantidadOriginalAProducir,
                    piezasPorEmbalaje);

            var cantidadProgramada =
                Math.Max(
                    0,
                    cantidadObjetivoEmpaque -
                    productoIncompleto); // NSQ_REDONDEO_EMBALAJE_POST_V1
            if (productoIncompleto > cantidadOriginalAProducir) throw new InvalidOperationException($"El producto incompleto apartado ({productoIncompleto:N0}) supera la cantidad pendiente ({cantidadOriginalAProducir:N0}).");
            if (cantidadProgramada <= 0) throw new InvalidOperationException("Después de considerar el producto incompleto apartado ya no existe cantidad nueva por producir.");
            vm.CantidadRequerida = cantidadRequerida;
            vm.CantidadOriginalAProducir = cantidadOriginalAProducir;
            vm.ProductoIncompletoApartado = productoIncompleto;
            vm.PiezasAProducir = cantidadProgramada;
            vm.CantidadProgramada = cantidadProgramada;
            vm.PesoBrutoPieza = pesoBruto;
            vm.PiezasPorEmbalaje = piezasPorEmbalaje;
            vm.ObjetivoHora = objetivoHora > 0 ? objetivoHora : null;
            vm.CantidadMpKg = pesoBruto.HasValue && pesoBruto.Value > 0 ? Math.Round((cantidadProgramada * pesoBruto.Value) / 1000m, 4) : 0;
            if (piezasPorEmbalaje.HasValue && piezasPorEmbalaje.Value > 0)
            {
                var embalajesFisicos = Math.Ceiling(cantidadOriginalAProducir / piezasPorEmbalaje.Value);
                vm.CantidadEmbalajes = Math.Max(0, embalajesFisicos - cantidadCajasBlancas);
            }
            else vm.CantidadEmbalajes = 0;
            vm.HorasProgramadas = objetivoHora > 0 ? Math.Ceiling(cantidadProgramada / (decimal)objetivoHora) : 0;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ProducirCantidadCompleta(int releaseDetalleId)
        {
            if (releaseDetalleId <= 0) { TempData["Error"] = "No se recibió la necesidad."; return RedirectToAction(nameof(Index)); }
            var usuarioId = ObtenerUsuarioID();
            if (usuarioId <= 0) { TempData["Error"] = "No se pudo identificar al usuario."; return RedirectToAction(nameof(Index)); }
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                const string sqlValidar = @"
SELECT ProgramaProduccionID FROM dbo.Planeacion_ReleaseDetalle WITH(UPDLOCK,HOLDLOCK)
WHERE ReleaseDetalleID=@ReleaseDetalleID AND Activo=1;";
                int? programaId;
                await using (var cmd = new SqlCommand(sqlValidar, cn, tx))
                {
                    cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value = releaseDetalleId;
                    var result = await cmd.ExecuteScalarAsync();
                    if (result == null) { await tx.RollbackAsync(); TempData["Error"] = "No se encontró la necesidad."; return RedirectToAction(nameof(Index)); }
                    programaId = result == DBNull.Value ? null : Convert.ToInt32(result);
                }
                if (programaId.HasValue) { await tx.RollbackAsync(); TempData["Error"] = "La necesidad ya fue programada; la decisión de producto incompleto ya quedó cerrada."; return RedirectToAction(nameof(Index)); }
                const string sqlCajas = @"
SELECT CajaProduccionID FROM dbo.Planeacion_ProductoIncompletoApartado
WHERE ReleaseDetalleID=@ReleaseDetalleID AND Activo=1 AND EstatusID=1;";
                var cajas = new List<long>();
                await using (var cmd = new SqlCommand(sqlCajas, cn, tx))
                {
                    cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value = releaseDetalleId;
                    await using var rd = await cmd.ExecuteReaderAsync();
                    while (await rd.ReadAsync()) cajas.Add(Convert.ToInt64(rd["CajaProduccionID"]));
                }
                foreach (var cajaId in cajas)
                {
                    const string sqlLiberar = @"
UPDATE dbo.Planeacion_ProductoIncompletoApartado SET EstatusID=6,Activo=0,
Observaciones=LEFT(COALESCE(NULLIF(Observaciones,N'')+N' | ',N'')+N'Planeación decidió producir la cantidad completa.',500)
WHERE CajaProduccionID=@CajaProduccionID AND ReleaseDetalleID=@ReleaseDetalleID AND Activo=1 AND EstatusID=1;
UPDATE dbo.Produccion_Cajas SET EstadoProductoIncompleto=N'DISPONIBLE',ProgramaReservaID=NULL,SolicitudReservaID=NULL,SolicitudDetalleReservaID=NULL,EjecucionReservaID=NULL,
FechaReservaIncompleto=NULL,UsuarioReservaIncompletoID=NULL,UsuarioModificacionID=@UsuarioID,FechaModificacion=SYSDATETIME()
WHERE CajaProduccionID=@CajaProduccionID AND Activo=1 AND EsProductoIncompleto=1;";
                    await using var cmd = new SqlCommand(sqlLiberar, cn, tx);
                    cmd.Parameters.Add("@CajaProduccionID", SqlDbType.BigInt).Value = cajaId;
                    cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value = releaseDetalleId;
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                    await cmd.ExecuteNonQueryAsync();
                }
                await tx.CommitAsync();
                TempData["Success"] = "Planeación decidió no utilizar producto incompleto. La OF se calculará por la cantidad completa pendiente.";
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                TempData["Error"] = "No fue posible actualizar la decisión: " + ex.Message;
            }
            return RedirectToAction(nameof(CrearDesdeNecesidad), new { releaseDetalleId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Crear(PlaneacionProgramaCrearDesdeNecesidadVm vm)
        {
            var usuarioId = ObtenerUsuarioID();
            vm.TipoOF = "RELEASE";
            vm.MotivoTipoOF = null;
            if (usuarioId <= 0) ModelState.AddModelError("", "No se pudo identificar el usuario de la sesión.");
            if (vm.ReleaseDetalleID <= 0) ModelState.AddModelError("", "No se recibió el renglón de release.");
            if (!vm.MaquinaID.HasValue) ModelState.AddModelError(nameof(vm.MaquinaID), "Selecciona la máquina.");
            if (!vm.MoldeID.HasValue) ModelState.AddModelError(nameof(vm.MoldeID), "Selecciona el molde.");
            if (string.IsNullOrWhiteSpace(vm.CondicionProduccion)) ModelState.AddModelError(nameof(vm.CondicionProduccion), "Selecciona la condición de producción.");
            if (!vm.FechaInicioProgramada.HasValue) ModelState.AddModelError(nameof(vm.FechaInicioProgramada), "Captura la fecha y hora de cambio.");
            if (!vm.Cambio.HasValue) ModelState.AddModelError(nameof(vm.Cambio), "Captura la hora de cambio de molde.");
            if (!vm.Arranque.HasValue) ModelState.AddModelError(nameof(vm.Arranque), "Captura la hora de arranque.");
            ModelState.Remove(nameof(vm.CantidadProgramada));
            ModelState.Remove(nameof(vm.HorasProgramadas));
            ModelState.Remove(nameof(vm.OperadorPrincipalID));
            ModelState.Remove(nameof(vm.OperadorAuxiliarID));
            vm.OperadorPrincipalID = null;
            vm.OperadorAuxiliarID = null;
            if (string.Equals(vm.CondicionProduccion, PlaneacionProgramaCondicion.InterrumpirProduccion, StringComparison.OrdinalIgnoreCase))
                ModelState.AddModelError(nameof(vm.CondicionProduccion), "La opción I.P. / Interrumpir producción estará disponible cuando el módulo de Producción esté terminado y contabilizando avance real.");
            if (vm.FechaInicioProgramada.HasValue && vm.Cambio.HasValue) vm.FechaInicioProgramada = CalcularFechaHoraDesdeHora(vm.FechaInicioProgramada.Value.Date, vm.Cambio);
            var minimoPermitido = RedondearSiguienteBloque(DateTime.Now, 15);
            if (vm.FechaInicioProgramada.HasValue && vm.FechaInicioProgramada.Value < minimoPermitido)
            {
                vm.FechaInicioProgramada = minimoPermitido;
                vm.Cambio = minimoPermitido.TimeOfDay;
            }
            var trabajarDomingo = string.Equals(Request.Form["TrabajarDomingo"].ToString(), "true", StringComparison.OrdinalIgnoreCase) || string.Equals(Request.Form["TrabajarDomingo"].ToString(), "on", StringComparison.OrdinalIgnoreCase);
            if (!ModelState.IsValid)
            {
                vm.ProductoIncompletoDisponible = await ObtenerProductoIncompletoDisponibleAsync(vm.ReleaseDetalleID);
                await CargarCatalogosAsync(vm);
                return View(vm);
            }
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            await using var tx = await cn.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                var sqlTx = (SqlTransaction)tx;
                var existe = await ReleaseDetalleYaProgramadoAsync(vm.ReleaseDetalleID, cn, sqlTx);
                if (existe)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "Ese renglón de release ya fue programado.";
                    return RedirectToAction(nameof(Index));
                }
                await RecalcularCantidadesProgramaAsync(vm, cn, sqlTx);
                if (vm.CantidadProgramada <= 0 || !vm.HorasProgramadas.HasValue || vm.HorasProgramadas.Value <= 0)
                    throw new InvalidOperationException("La cantidad u horas de producción recalculadas no son válidas.");
                if (vm.MaquinaID.HasValue)
                {
                    var maquinaCompatible = await MaquinaCompatibleConParteAsync(vm.ParteID, vm.MaquinaID.Value, cn, sqlTx);
                    if (!maquinaCompatible)
                    {
                        await tx.RollbackAsync();
                        ModelState.AddModelError(nameof(vm.MaquinaID), "La máquina seleccionada no está configurada como principal ni sustituta directa para esta parte. No se permiten sustitutas de sustitutas.");
                        vm.ProductoIncompletoDisponible = await ObtenerProductoIncompletoDisponibleAsync(vm.ReleaseDetalleID);
                        await CargarCatalogosAsync(vm);
                        return View(vm);
                    }
                }
                await ActivarReacomodoPlaneacionAsync(cn, sqlTx);
                var sugeridaTx = await ObtenerSiguienteCambioDisponibleAsync(vm.MaquinaID!.Value, vm.FechaInicioProgramada!.Value, vm.ParteID, vm.MoldeID, cn, sqlTx, trabajarDomingo);
                vm.FechaInicioProgramada = sugeridaTx.Cambio;
                vm.Cambio = sugeridaTx.Cambio.TimeOfDay;
                vm.Arranque = sugeridaTx.Arranque.TimeOfDay;
                vm.FechaFinProgramada = SumarHorasOperativasPlaneacion(sugeridaTx.Arranque, vm.HorasProgramadas.Value, trabajarDomingo);
                var textoSugerencia = $"Programación colocada automáticamente en cola. Cambio: {sugeridaTx.Cambio:dd/MM/yyyy HH:mm}. Arranque: {sugeridaTx.Arranque:dd/MM/yyyy HH:mm}. {sugeridaTx.Motivo}";
                vm.Observaciones = string.IsNullOrWhiteSpace(vm.Observaciones) ? textoSugerencia : vm.Observaciones.Trim() + Environment.NewLine + textoSugerencia;
                // NSQ_OPERADORES_SOLO_PRODUCCION_V1
                // Planeacion no decide personal. Fecha + Turno + Maquina se resuelve
                // en Produccion mediante DDP.
                vm.OperadorPrincipalID = null;
                vm.OperadorAuxiliarID = null;
await CompletarDatosProgramaAsync(vm, cn, sqlTx);
                await CompletarVinculoOFExistenteAsync(vm, cn, sqlTx);
                var programaId = await InsertarProgramaAsync(vm, usuarioId, cn, sqlTx);
                // NSQ_OPERADORES_SOLO_PRODUCCION_V1 - no persistir personal desde Planeacion.
                await MarcarReleaseDetalleProgramadoAsync(vm.ReleaseDetalleID, programaId, usuarioId, cn, sqlTx);
                // NSQ_LHRH_AUTO_V1
                var programaParejaLhRhId =
                    await ProgramarParejaLhRhAsync(
                        programaId,
                        vm,
                        usuarioId,
                        cn,
                        sqlTx);
                if (vm.SolicitudProduccionID.HasValue && vm.SolicitudProduccionDetalleID.HasValue)
                    await VincularOFManualConProgramaAsync(programaId, vm, usuarioId, cn, sqlTx);
                await DesactivarReacomodoPlaneacionAsync(cn, sqlTx);
                await tx.CommitAsync();
                TempData["Success"] = vm.ProductoIncompletoApartado > 0
                    ? $"Cambio de molde programado correctamente. Se usarán {vm.ProductoIncompletoApartado:N0} pieza(s) de etiqueta blanca y se producirán {vm.CantidadProgramada:N0}."
                    : "Cambio de molde programado correctamente.";
                return RedirectToAction(nameof(Index)); // NSQ_REDIRECT_INDEX_V1
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                ModelState.AddModelError("", "Error al programar cambio de molde: " + ex.Message);
                vm.ProductoIncompletoDisponible = await ObtenerProductoIncompletoDisponibleAsync(vm.ReleaseDetalleID);
                await CargarCatalogosAsync(vm);
                return View(vm);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GenerarOF(int programaProduccionId)
        {
            if (programaProduccionId <= 0)
            {
                TempData["Error"] = "No se recibió el programa de producción.";
                return RedirectToAction(nameof(Index));
            }

            var usuarioId = ObtenerUsuarioID();

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            await using var tx = await cn.BeginTransactionAsync();

            try
            {
                var programa = await ObtenerProgramaParaGenerarOFAsync(
                    programaProduccionId,
                    cn,
                    (SqlTransaction)tx
                );

                if (programa == null)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "No se encontró el programa seleccionado.";
                    return RedirectToAction(nameof(Index));
                }

                if (programa.SolicitudProduccionID.HasValue)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "Este programa ya tiene una OF generada.";
                    return RedirectToAction(nameof(Index));
                }

                if (programa.CantidadProgramada <= 0)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "El programa no tiene cantidad programada válida.";
                    return RedirectToAction(nameof(Index));
                }

                var folioOF = await GenerarFolioOFAsync(cn, (SqlTransaction)tx);

                var solicitudProduccionId = await InsertarOFDedeProgramaAsync(
                    programa,
                    folioOF,
                    usuarioId,
                    cn,
                    (SqlTransaction)tx
                );

                var solicitudProduccionDetalleId = await InsertarDetalleOFDedeProgramaAsync(
                    solicitudProduccionId,
                    programa,
                    usuarioId,
                    cn,
                    (SqlTransaction)tx
                );

                await InsertarAsignacionMaquinaOFDedeProgramaAsync(
                    solicitudProduccionDetalleId,
                    programa,
                    usuarioId,
                    cn,
                    (SqlTransaction)tx
                );

                await MarcarProgramaConOFAsync(
                    programaProduccionId,
                    solicitudProduccionId,
                    solicitudProduccionDetalleId,
                    usuarioId,
                    cn,
                    (SqlTransaction)tx
                );

                if (programa.ReleaseDetalleID.HasValue)
                {
                    await MarcarReleaseDetalleConOFAsync(
                        programa.ReleaseDetalleID.Value,
                        solicitudProduccionId,
                        cn,
                        (SqlTransaction)tx
                    );

                    // LANZAMIENTO TEMPORAL SIN DESCUENTO DE ALMACÉN:
                    // No vinculamos apartado PT a la OF. Almacén entregará de forma manual.
                    // REACTIVAR_ALMACEN: await VincularApartadoPTAOFAsync(programa.ReleaseDetalleID.Value, programaProduccionId, solicitudProduccionId, cn, (SqlTransaction)tx);
                }

                await tx.CommitAsync();

                TempData["Success"] = "OF generada correctamente desde el programa de producción.";
                return RedirectToAction("Detalle", "Planeacion", new { id = solicitudProduccionId });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();

                TempData["Error"] = "Error al generar la OF: " + ex.Message;
                return RedirectToAction(nameof(Index));
            }
        }


        private async Task<PlaneacionProgramaCrearDesdeNecesidadVm?> ObtenerNecesidadParaProgramaAsync(int releaseDetalleId)
        {
            const string sql = @"
SELECT r.ReleaseID,r.FolioRelease,r.ClienteID,ISNULL(c.Nombre,r.ClienteNombre) AS ClienteNombre,d.ReleaseDetalleID,d.ParteID,d.NumeroParte,d.ReferenciaSAP,d.DesignacionDescripcionSAP,d.FechaRequerida,d.CantidadRequerida,d.ProgramaProduccionID,d.SolicitudProduccionID,
(SELECT TOP(1)sd.SolicitudProduccionDetalleID FROM dbo.SolicitudesProduccionDetalle sd WHERE sd.SolicitudProduccionID=d.SolicitudProduccionID AND sd.Activo=1 AND sd.Renglon=d.Renglon AND(sd.ParteID=d.ParteID OR(sd.ParteID IS NULL AND d.ParteID IS NULL)) ORDER BY sd.SolicitudProduccionDetalleID) AS SolicitudProduccionDetalleID,
t.Color,COALESCE(d.MaterialID,t.MaterialID) AS MaterialID,COALESCE(NULLIF(d.MaterialCodigo,''),t.MaterialCodigo) AS MaterialCodigo,
COALESCE(NULLIF(d.MaterialDescripcion,''),t.MaterialDescripcion) AS MaterialDescripcion,COALESCE(d.PesoBrutoPieza,t.PesoBrutoPieza) AS PesoBrutoPieza,t.PesoNetoPieza,
COALESCE(NULLIF(d.EmbalajeCodigo,''),t.EmbalajeCodigo) AS EmbalajeCodigo,COALESCE(NULLIF(d.EmbalajeDescripcion,''),t.EmbalajeDescripcion) AS EmbalajeDescripcion,
COALESCE(d.PiezasPorEmbalaje,t.PiezasPorEmbalaje) AS PiezasPorEmbalaje,t.PiezasPorCaja,
COALESCE(d.MoldeID,t.MoldePrincipalID) AS MoldeID,COALESCE(NULLIF(d.MoldeCodigo,''),mol.CodigoMolde) AS MoldeCodigo,
COALESCE(d.MaquinaSugeridaID,t.MaquinaPrincipalID) AS MaquinaSugeridaID,COALESCE(NULLIF(d.MaquinaSugeridaCodigo,''),maq.Codigo) AS MaquinaSugeridaCodigo,
COALESCE(NULLIF(d.MaquinaSugeridaNombre,''),maq.Nombre) AS MaquinaSugeridaNombre,
sust.MaquinaSustitutaID,sust.MaquinaSustitutaCodigo,sust.MaquinaSustitutaNombre,t.ObjetivoHora,t.Ciclo,t.Cavidades,t.TipoSecado,t.HorasSecado,t.HorasSecadoTexto,
ISNULL(pt.Disponible,0) AS PTDisponible,ISNULL(mp.Disponible,0) AS MPDisponible,ISNULL(emb.Disponible,0) AS EmbalajeDisponible,
ISNULL(prog.ProgramadoPendiente,0) AS ProgramadoPendiente,ISNULL(aptOtros.CantidadApartada,0) AS PTApartadoOtros,
ISNULL(blanca.CantidadApartada,0) AS ProductoIncompletoApartado,ISNULL(blanca.CantidadCajas,0) AS CantidadCajasProductoIncompleto
FROM dbo.Planeacion_ReleaseDetalle d
INNER JOIN dbo.Planeacion_Releases r ON r.ReleaseID=d.ReleaseID
LEFT JOIN dbo.ERP_Clientes c ON c.ClienteID=r.ClienteID
LEFT JOIN dbo.ERP_ParteDatosTecnicos t ON t.ParteID=d.ParteID AND t.Activo=1
LEFT JOIN dbo.ERP_Moldes mol ON mol.MoldeID=COALESCE(d.MoldeID,t.MoldePrincipalID)
LEFT JOIN dbo.ERP_Maquinas maq ON maq.MaquinaID=COALESCE(d.MaquinaSugeridaID,t.MaquinaPrincipalID)
OUTER APPLY
(
    SELECT TOP(1)x.MaquinaID AS MaquinaSustitutaID
    FROM
    (
        SELECT t.MaquinaSustitutaID AS MaquinaID,0 AS Prioridad WHERE t.MaquinaSustitutaID IS NOT NULL
        UNION SELECT ms.MaquinaSustitutaID,ISNULL(ms.Prioridad,999) FROM dbo.ERP_MaquinasSustitutas ms WHERE ms.Activo=1 AND ms.MaquinaPrincipalID=t.MaquinaPrincipalID
        UNION SELECT ms.MaquinaPrincipalID,ISNULL(ms.Prioridad,999) FROM dbo.ERP_MaquinasSustitutas ms WHERE ms.Activo=1 AND ms.MaquinaSustitutaID=t.MaquinaPrincipalID
    )x
    INNER JOIN dbo.ERP_Maquinas m ON m.MaquinaID=x.MaquinaID AND m.Activo=1
    WHERE x.MaquinaID IS NOT NULL AND(t.MaquinaPrincipalID IS NULL OR x.MaquinaID<>t.MaquinaPrincipalID)
    ORDER BY x.Prioridad,x.MaquinaID
)sustTop
OUTER APPLY
(
    SELECT sustTop.MaquinaSustitutaID,
    STUFF((SELECT ', '+m2.Codigo FROM(SELECT t.MaquinaSustitutaID AS MaquinaID,0 AS Prioridad WHERE t.MaquinaSustitutaID IS NOT NULL UNION SELECT ms.MaquinaSustitutaID,ISNULL(ms.Prioridad,999) FROM dbo.ERP_MaquinasSustitutas ms WHERE ms.Activo=1 AND ms.MaquinaPrincipalID=t.MaquinaPrincipalID UNION SELECT ms.MaquinaPrincipalID,ISNULL(ms.Prioridad,999) FROM dbo.ERP_MaquinasSustitutas ms WHERE ms.Activo=1 AND ms.MaquinaSustitutaID=t.MaquinaPrincipalID)x2 INNER JOIN dbo.ERP_Maquinas m2 ON m2.MaquinaID=x2.MaquinaID AND m2.Activo=1 WHERE x2.MaquinaID IS NOT NULL AND(t.MaquinaPrincipalID IS NULL OR x2.MaquinaID<>t.MaquinaPrincipalID) ORDER BY x2.Prioridad,m2.Codigo FOR XML PATH(''),TYPE).value('.','NVARCHAR(MAX)'),1,2,'') AS MaquinaSustitutaCodigo,
    STUFF((SELECT ', '+m2.Codigo+' - '+ISNULL(m2.Nombre,'') FROM(SELECT t.MaquinaSustitutaID AS MaquinaID,0 AS Prioridad WHERE t.MaquinaSustitutaID IS NOT NULL UNION SELECT ms.MaquinaSustitutaID,ISNULL(ms.Prioridad,999) FROM dbo.ERP_MaquinasSustitutas ms WHERE ms.Activo=1 AND ms.MaquinaPrincipalID=t.MaquinaPrincipalID UNION SELECT ms.MaquinaPrincipalID,ISNULL(ms.Prioridad,999) FROM dbo.ERP_MaquinasSustitutas ms WHERE ms.Activo=1 AND ms.MaquinaSustitutaID=t.MaquinaPrincipalID)x3 INNER JOIN dbo.ERP_Maquinas m2 ON m2.MaquinaID=x3.MaquinaID AND m2.Activo=1 WHERE x3.MaquinaID IS NOT NULL AND(t.MaquinaPrincipalID IS NULL OR x3.MaquinaID<>t.MaquinaPrincipalID) ORDER BY x3.Prioridad,m2.Codigo FOR XML PATH(''),TYPE).value('.','NVARCHAR(MAX)'),1,2,'') AS MaquinaSustitutaNombre
)sust
OUTER APPLY(SELECT TOP(1)ISNULL(Disponible,0) AS Disponible FROM dbo.vw_AlmacenPTInventario WHERE ParteID=d.ParteID)pt
OUTER APPLY(SELECT TOP(1)ISNULL(Disponible,0) AS Disponible FROM dbo.vw_AlmacenMPInventario WHERE MaterialID=t.MaterialID AND TipoMP=N'V' ORDER BY OrdenTipo)mp
OUTER APPLY(SELECT TOP(1)ISNULL(Disponible,0) AS Disponible FROM dbo.vw_AlmacenEmbalajesInventario WHERE Codigo=t.EmbalajeCodigo)emb
OUTER APPLY(SELECT ISNULL(SUM(ISNULL(pp.CantidadProgramada,0)-ISNULL(pp.CantidadProducida,0)),0) AS ProgramadoPendiente FROM dbo.Planeacion_ProgramaProduccion pp WHERE pp.ReleaseDetalleID=d.ReleaseDetalleID AND pp.Activo=1 AND ISNULL(pp.EstatusID,1) NOT IN(5,9,99))prog
OUTER APPLY(SELECT ISNULL(SUM(a.CantidadApartada),0) AS CantidadApartada FROM dbo.Planeacion_PT_Apartado a WHERE a.ParteID=d.ParteID AND a.ReleaseDetalleID<>d.ReleaseDetalleID AND a.Activo=1 AND a.EstatusID=1)aptOtros
OUTER APPLY(SELECT ISNULL(SUM(a.CantidadApartada),0) AS CantidadApartada,COUNT_BIG(*) AS CantidadCajas FROM dbo.Planeacion_ProductoIncompletoApartado a WHERE a.ReleaseDetalleID=d.ReleaseDetalleID AND a.Activo=1 AND a.EstatusID IN(1,2,3,4))blanca
WHERE d.ReleaseDetalleID=@ReleaseDetalleID AND d.Activo=1 AND r.Activo=1;";
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value = releaseDetalleId;
            await using var rd = await cmd.ExecuteReaderAsync();
            if (!await rd.ReadAsync()) return null;
            if (rd["ProgramaProduccionID"] != DBNull.Value) return null;
            var cantidadRequerida = rd["CantidadRequerida"] == DBNull.Value ? 0 : Convert.ToInt32(rd["CantidadRequerida"]);
            var stockDisponible = rd["PTDisponible"] == DBNull.Value ? 0 : Convert.ToInt32(rd["PTDisponible"]);
            var ptApartadoOtros = rd["PTApartadoOtros"] == DBNull.Value ? 0 : Convert.ToInt32(rd["PTApartadoOtros"]);
            var programadoPendiente = rd["ProgramadoPendiente"] == DBNull.Value ? 0 : Convert.ToInt32(rd["ProgramadoPendiente"]);
            var productoIncompletoApartado = rd["ProductoIncompletoApartado"] == DBNull.Value ? 0 : Convert.ToInt32(rd["ProductoIncompletoApartado"]);
            var cantidadCajasBlancas = rd["CantidadCajasProductoIncompleto"] == DBNull.Value ? 0 : Convert.ToInt32(rd["CantidadCajasProductoIncompleto"]);
            var cantidadOriginalAProducir = Math.Max(0, cantidadRequerida - programadoPendiente);
            var piezasAProducir = Math.Max(0, cantidadOriginalAProducir - productoIncompletoApartado);
            var ptDisponibleNeto = Math.Max(0, stockDisponible - ptApartadoOtros);
            var piezasDesdeStock = 0;
            var pesoBrutoPieza = rd["PesoBrutoPieza"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(rd["PesoBrutoPieza"]);
            var piezasPorEmbalaje = rd["PiezasPorEmbalaje"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(rd["PiezasPorEmbalaje"]);
            var objetivoHora = rd["ObjetivoHora"] == DBNull.Value ? (int?)null : Convert.ToInt32(rd["ObjetivoHora"]);
            decimal cantidadMpKg = 0;
            if (piezasAProducir > 0 && pesoBrutoPieza.HasValue && pesoBrutoPieza.Value > 0) cantidadMpKg = Math.Round((piezasAProducir * pesoBrutoPieza.Value) / 1000m, 4);
            decimal cantidadEmbalajes = 0;
            if (cantidadOriginalAProducir > 0 && piezasPorEmbalaje.HasValue && piezasPorEmbalaje.Value > 0)
            {
                var embalajesFisicos = Math.Ceiling(cantidadOriginalAProducir / piezasPorEmbalaje.Value);
                cantidadEmbalajes = Math.Max(0, embalajesFisicos - cantidadCajasBlancas);
            }
            decimal horasProgramadas = 0;
            if (piezasAProducir > 0 && objetivoHora.HasValue && objetivoHora.Value > 0) horasProgramadas = Math.Ceiling(piezasAProducir / (decimal)objetivoHora.Value);
            var fechaInicio = RedondearSiguienteHora(DateTime.Now);
            DateTime? fechaFin = horasProgramadas > 0 ? fechaInicio.AddHours((double)horasProgramadas) : null;
            return new PlaneacionProgramaCrearDesdeNecesidadVm
            {
                ReleaseDetalleID = Convert.ToInt32(rd["ReleaseDetalleID"]),
                ReleaseID = rd["ReleaseID"] == DBNull.Value ? null : Convert.ToInt32(rd["ReleaseID"]),
                FolioRelease = rd["FolioRelease"] as string,
                SolicitudProduccionID = rd["SolicitudProduccionID"] == DBNull.Value ? null : Convert.ToInt32(rd["SolicitudProduccionID"]),
                SolicitudProduccionDetalleID = rd["SolicitudProduccionDetalleID"] == DBNull.Value ? null : Convert.ToInt32(rd["SolicitudProduccionDetalleID"]),
                TipoOF = "RELEASE",
                MotivoTipoOF = null,
                ClienteID = rd["ClienteID"] == DBNull.Value ? null : Convert.ToInt32(rd["ClienteID"]),
                ClienteNombre = rd["ClienteNombre"] as string,
                ParteID = rd["ParteID"] == DBNull.Value ? null : Convert.ToInt32(rd["ParteID"]),
                NumeroParte = rd["NumeroParte"] as string,
                ReferenciaSAP = rd["ReferenciaSAP"] as string,
                DesignacionDescripcionSAP = rd["DesignacionDescripcionSAP"] as string,
                Color = rd["Color"] as string,
                CantidadRequerida = cantidadRequerida,
                PiezasDesdePT = piezasDesdeStock,
                PTDisponibleAlCalcular = stockDisponible,
                PTApartadoOtros = ptApartadoOtros,
                PTDisponibleNeto = ptDisponibleNeto,
                CantidadOriginalAProducir = cantidadOriginalAProducir,
                ProductoIncompletoApartado = productoIncompletoApartado,
                PiezasAProducir = piezasAProducir,
                MaterialID = rd["MaterialID"] == DBNull.Value ? null : Convert.ToInt32(rd["MaterialID"]),
                MaterialCodigo = rd["MaterialCodigo"] as string,
                MaterialDescripcion = rd["MaterialDescripcion"] as string,
                PesoBrutoPieza = pesoBrutoPieza,
                CantidadMpKg = cantidadMpKg,
                EmbalajeCodigo = rd["EmbalajeCodigo"] as string,
                EmbalajeDescripcion = rd["EmbalajeDescripcion"] as string,
                PiezasPorEmbalaje = piezasPorEmbalaje,
                CantidadEmbalajes = cantidadEmbalajes,
                MoldeID = rd["MoldeID"] == DBNull.Value ? null : Convert.ToInt32(rd["MoldeID"]),
                MoldeCodigo = rd["MoldeCodigo"] as string,
                MaquinaID = rd["MaquinaSugeridaID"] == DBNull.Value ? null : Convert.ToInt32(rd["MaquinaSugeridaID"]),
                MaquinaCodigo = rd["MaquinaSugeridaCodigo"] as string,
                MaquinaNombre = rd["MaquinaSugeridaNombre"] as string,
                MaquinaSustitutaID = rd["MaquinaSustitutaID"] == DBNull.Value ? null : Convert.ToInt32(rd["MaquinaSustitutaID"]),
                MaquinaSustitutaCodigo = rd["MaquinaSustitutaCodigo"] as string,
                MaquinaSustitutaNombre = rd["MaquinaSustitutaNombre"] as string,
                ObjetivoHora = objetivoHora,
                Ciclo = rd["Ciclo"] == DBNull.Value ? null : rd["Ciclo"].ToString(),
                Cavidades = rd["Cavidades"] == DBNull.Value ? null : Convert.ToInt32(rd["Cavidades"]),
                PiezasPorCaja = rd["PiezasPorCaja"] == DBNull.Value ? null : Convert.ToInt32(rd["PiezasPorCaja"]),
                TipoSecado = rd["TipoSecado"] as string,
                HorasSecado = rd["HorasSecado"] == DBNull.Value ? null : Convert.ToDecimal(rd["HorasSecado"]),
                HorasSecadoTexto = rd["HorasSecadoTexto"] as string,
                HorasProgramadas = horasProgramadas,
                FechaInicioProgramada = fechaInicio,
                FechaFinProgramada = fechaFin
            };
        }


        private async Task CompletarDatosProgramaAsync(
            PlaneacionProgramaCrearDesdeNecesidadVm vm,
            SqlConnection cn,
            SqlTransaction tx)
        {
            if (vm.MaquinaID.HasValue)
            {
                const string sqlMaq = @"
SELECT TOP 1
    Codigo,
    Nombre
FROM dbo.ERP_Maquinas
WHERE MaquinaID = @MaquinaID;";

                await using var cmd = new SqlCommand(sqlMaq, cn, tx);
                cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = vm.MaquinaID.Value;

                await using var rd = await cmd.ExecuteReaderAsync();

                if (await rd.ReadAsync())
                {
                    vm.MaquinaCodigo = rd["Codigo"] as string;
                    vm.MaquinaNombre = rd["Nombre"] as string;
                }
            }

            if (vm.MoldeID.HasValue)
            {
                const string sqlMolde = @"
SELECT TOP 1
    CodigoMolde
FROM dbo.ERP_Moldes
WHERE MoldeID = @MoldeID;";

                await using var cmd = new SqlCommand(sqlMolde, cn, tx);
                cmd.Parameters.Add("@MoldeID", SqlDbType.Int).Value = vm.MoldeID.Value;

                await using var rd = await cmd.ExecuteReaderAsync();

                if (await rd.ReadAsync())
                {
                    vm.MoldeCodigo = rd["CodigoMolde"] as string;
                }
            }

            if (vm.FechaInicioProgramada.HasValue &&
                vm.Arranque.HasValue &&
                vm.HorasProgramadas.HasValue &&
                vm.HorasProgramadas.Value > 0)
            {
                var fechaArranque = CalcularFechaHoraDesdeHora(
                    vm.FechaInicioProgramada.Value.Date,
                    vm.Arranque
                );

                if (fechaArranque < vm.FechaInicioProgramada.Value)
                    fechaArranque = fechaArranque.AddDays(1);

                vm.FechaFinProgramada = SumarHorasOperativasPlaneacion(
                    fechaArranque,
                    vm.HorasProgramadas.Value
                );
            }
        }

        private static async Task CompletarVinculoOFExistenteAsync(
            PlaneacionProgramaCrearDesdeNecesidadVm vm,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
SELECT TOP (1)
    d.SolicitudProduccionID,
    sd.SolicitudProduccionDetalleID
FROM dbo.Planeacion_ReleaseDetalle d
OUTER APPLY
(
    SELECT TOP (1)
        x.SolicitudProduccionDetalleID
    FROM dbo.SolicitudesProduccionDetalle x
    WHERE x.SolicitudProduccionID = d.SolicitudProduccionID
      AND x.Activo = 1
      AND x.Renglon = d.Renglon
      AND (x.ParteID = d.ParteID OR (x.ParteID IS NULL AND d.ParteID IS NULL))
    ORDER BY x.SolicitudProduccionDetalleID
) sd
WHERE d.ReleaseDetalleID = @ReleaseDetalleID
  AND d.Activo = 1;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value = vm.ReleaseDetalleID;

            await using var rd = await cmd.ExecuteReaderAsync();
            if (!await rd.ReadAsync())
                return;

            vm.SolicitudProduccionID = rd["SolicitudProduccionID"] == DBNull.Value
                ? null
                : Convert.ToInt32(rd["SolicitudProduccionID"]);

            vm.SolicitudProduccionDetalleID = rd["SolicitudProduccionDetalleID"] == DBNull.Value
                ? null
                : Convert.ToInt32(rd["SolicitudProduccionDetalleID"]);
        }

        private static async Task VincularOFManualConProgramaAsync(int programaProduccionId, PlaneacionProgramaCrearDesdeNecesidadVm vm, int usuarioId, SqlConnection cn, SqlTransaction tx)
        {
            if (!vm.SolicitudProduccionID.HasValue || !vm.SolicitudProduccionDetalleID.HasValue) return;
            int? estatusAnterior = null;
            const string sqlEstatus = @"SELECT EstatusID FROM dbo.SolicitudesProduccion WHERE SolicitudProduccionID=@SolicitudProduccionID AND Activo=1;";
            await using (var cmd = new SqlCommand(sqlEstatus, cn, tx))
            {
                cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = vm.SolicitudProduccionID.Value;
                var result = await cmd.ExecuteScalarAsync();
                if (result != null && result != DBNull.Value) estatusAnterior = Convert.ToInt32(result);
            }
            const string sqlSolicitud = @"
UPDATE dbo.SolicitudesProduccion SET
ReleaseID=COALESCE(ReleaseID,@ReleaseID),ReleaseDetalleID=COALESCE(ReleaseDetalleID,@ReleaseDetalleID),ProgramaProduccionID=COALESCE(ProgramaProduccionID,@ProgramaProduccionID),
OrigenOF=N'MANUAL',TipoOF=COALESCE(NULLIF(TipoOF,N''),N'RELEASE'),
FechaInicioPlaneada=CASE WHEN FechaInicioPlaneada IS NULL OR FechaInicioPlaneada>@FechaInicio THEN @FechaInicio ELSE FechaInicioPlaneada END,
FechaFinPlaneada=CASE WHEN FechaFinPlaneada IS NULL OR FechaFinPlaneada<@FechaFin THEN @FechaFin ELSE FechaFinPlaneada END,
ResponsablePlaneacionUsuarioID=@UsuarioID,ResponsablePlaneacionNombre=COALESCE(NULLIF(ResponsablePlaneacionNombre,N''),N'Planeación'),
EstatusID=CASE WHEN ISNULL(EstatusID,1)<@EstatusPlaneado THEN @EstatusPlaneado ELSE EstatusID END,
UsuarioModificacionID=@UsuarioID,FechaModificacion=GETDATE()
WHERE SolicitudProduccionID=@SolicitudProduccionID AND Activo=1;";
            await using (var cmd = new SqlCommand(sqlSolicitud, cn, tx))
            {
                cmd.Parameters.Add("@ReleaseID", SqlDbType.Int).Value = (object?)vm.ReleaseID ?? DBNull.Value;
                cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value = vm.ReleaseDetalleID;
                cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaProduccionId;
                cmd.Parameters.Add("@FechaInicio", SqlDbType.DateTime).Value = (object?)vm.FechaInicioProgramada ?? DateTime.Now;
                cmd.Parameters.Add("@FechaFin", SqlDbType.DateTime).Value = (object?)vm.FechaFinProgramada ?? (object?)vm.FechaInicioProgramada ?? DateTime.Now;
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                cmd.Parameters.Add("@EstatusPlaneado", SqlDbType.Int).Value = PlaneacionOFEstatus.PendienteValidacionMP;
                cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = vm.SolicitudProduccionID.Value;
                await cmd.ExecuteNonQueryAsync();
            }
            const string sqlDetalle = @"
UPDATE dbo.SolicitudesProduccionDetalle SET
CantidadPiezas=@CantidadProgramada,
MoldeID=COALESCE(@MoldeID,MoldeID),MaquinaSugeridaID=COALESCE(@MaquinaID,MaquinaSugeridaID),
NumeroMoldeTexto=COALESCE(NULLIF(@MoldeCodigo,N''),NumeroMoldeTexto),MaquinaSugeridaTexto=COALESCE(NULLIF(@MaquinaTexto,N''),MaquinaSugeridaTexto),
HorasPlaneadas=@HorasProgramadas,MaterialID=COALESCE(@MaterialID,MaterialID),MaterialCodigo=COALESCE(NULLIF(@MaterialCodigo,N''),MaterialCodigo),
MaterialDescripcion=COALESCE(NULLIF(@MaterialDescripcion,N''),MaterialDescripcion),EmbalajeCodigo=COALESCE(NULLIF(@EmbalajeCodigo,N''),EmbalajeCodigo),
EmbalajeDescripcion=COALESCE(NULLIF(@EmbalajeDescripcion,N''),EmbalajeDescripcion),PiezasPorEmbalaje=COALESCE(@PiezasPorEmbalaje,PiezasPorEmbalaje),
CantidadEmbalajes=@CantidadEmbalajes,CantidadMpKg=@CantidadMpKg,Cambio=COALESCE(@Cambio,Cambio),Arranque=COALESCE(@Arranque,Arranque),
EstatusID=CASE WHEN ISNULL(EstatusID,1)<@EstatusPlaneado THEN @EstatusPlaneado ELSE EstatusID END
WHERE SolicitudProduccionDetalleID=@SolicitudProduccionDetalleID AND Activo=1;";
            await using (var cmd = new SqlCommand(sqlDetalle, cn, tx))
            {
                cmd.Parameters.Add("@CantidadProgramada", SqlDbType.Int).Value = vm.CantidadProgramada;
                cmd.Parameters.Add("@MoldeID", SqlDbType.Int).Value = (object?)vm.MoldeID ?? DBNull.Value;
                cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = (object?)vm.MaquinaID ?? DBNull.Value;
                cmd.Parameters.Add("@MoldeCodigo", SqlDbType.NVarChar, 100).Value = (object?)vm.MoldeCodigo ?? DBNull.Value;
                cmd.Parameters.Add("@MaquinaTexto", SqlDbType.NVarChar, 200).Value = (object?)($"{vm.MaquinaCodigo} - {vm.MaquinaNombre}".Trim(' ', '-')) ?? DBNull.Value;
                var horas = cmd.Parameters.Add("@HorasProgramadas", SqlDbType.Decimal); horas.Precision = 18; horas.Scale = 2; horas.Value = (object?)vm.HorasProgramadas ?? DBNull.Value;
                cmd.Parameters.Add("@MaterialID", SqlDbType.Int).Value = (object?)vm.MaterialID ?? DBNull.Value;
                cmd.Parameters.Add("@MaterialCodigo", SqlDbType.NVarChar, 100).Value = (object?)vm.MaterialCodigo ?? DBNull.Value;
                cmd.Parameters.Add("@MaterialDescripcion", SqlDbType.NVarChar, 250).Value = (object?)vm.MaterialDescripcion ?? DBNull.Value;
                cmd.Parameters.Add("@EmbalajeCodigo", SqlDbType.NVarChar, 100).Value = (object?)vm.EmbalajeCodigo ?? DBNull.Value;
                cmd.Parameters.Add("@EmbalajeDescripcion", SqlDbType.NVarChar, 250).Value = (object?)vm.EmbalajeDescripcion ?? DBNull.Value;
                var ppe = cmd.Parameters.Add("@PiezasPorEmbalaje", SqlDbType.Decimal); ppe.Precision = 18; ppe.Scale = 4; ppe.Value = (object?)vm.PiezasPorEmbalaje ?? DBNull.Value;
                var ce = cmd.Parameters.Add("@CantidadEmbalajes", SqlDbType.Decimal); ce.Precision = 18; ce.Scale = 4; ce.Value = (object?)vm.CantidadEmbalajes ?? DBNull.Value;
                var mp = cmd.Parameters.Add("@CantidadMpKg", SqlDbType.Decimal); mp.Precision = 18; mp.Scale = 4; mp.Value = (object?)vm.CantidadMpKg ?? DBNull.Value;
                cmd.Parameters.Add("@Cambio", SqlDbType.Time).Value = (object?)vm.Cambio ?? DBNull.Value;
                cmd.Parameters.Add("@Arranque", SqlDbType.Time).Value = (object?)vm.Arranque ?? DBNull.Value;
                cmd.Parameters.Add("@EstatusPlaneado", SqlDbType.Int).Value = PlaneacionOFEstatus.PendienteValidacionMP;
                cmd.Parameters.Add("@SolicitudProduccionDetalleID", SqlDbType.Int).Value = vm.SolicitudProduccionDetalleID.Value;
                await cmd.ExecuteNonQueryAsync();
            }
            const string sqlAsignacion = @"
UPDATE dbo.SolicitudesProduccionAsignacionMaquina SET
MaquinaID=COALESCE(@MaquinaID,MaquinaID),MoldeID=COALESCE(@MoldeID,MoldeID),CantidadAsignada=@CantidadProgramada,HorasEstimadas=@HorasProgramadas,
FechaProgramadaTentativa=CAST(@FechaInicio AS date),HoraInicioTentativa=CAST(@FechaInicio AS time),HoraFinTentativa=CAST(@FechaFin AS time),
CondicionProduccion=@CondicionProduccion,EstatusID=@EstatusPlaneado,
Observaciones=LEFT(COALESCE(NULLIF(Observaciones,N'')+CHAR(13)+CHAR(10),N'')+N'Vinculada al Programa de Producción ID '+CONVERT(NVARCHAR(20),@ProgramaProduccionID)+N'.',500)
WHERE SolicitudProduccionDetalleID=@SolicitudProduccionDetalleID AND Activo=1 AND(@MaquinaID IS NULL OR MaquinaID=@MaquinaID);";
            await using (var cmd = new SqlCommand(sqlAsignacion, cn, tx))
            {
                cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = (object?)vm.MaquinaID ?? DBNull.Value;
                cmd.Parameters.Add("@MoldeID", SqlDbType.Int).Value = (object?)vm.MoldeID ?? DBNull.Value;
                cmd.Parameters.Add("@CantidadProgramada", SqlDbType.Int).Value = vm.CantidadProgramada;
                var horas = cmd.Parameters.Add("@HorasProgramadas", SqlDbType.Decimal); horas.Precision = 18; horas.Scale = 2; horas.Value = (object?)vm.HorasProgramadas ?? DBNull.Value;
                cmd.Parameters.Add("@FechaInicio", SqlDbType.DateTime).Value = (object?)vm.FechaInicioProgramada ?? DateTime.Now;
                cmd.Parameters.Add("@FechaFin", SqlDbType.DateTime).Value = (object?)vm.FechaFinProgramada ?? (object?)vm.FechaInicioProgramada ?? DateTime.Now;
                cmd.Parameters.Add("@CondicionProduccion", SqlDbType.NVarChar, 20).Value = (object?)vm.CondicionProduccion ?? DBNull.Value;
                cmd.Parameters.Add("@EstatusPlaneado", SqlDbType.Int).Value = PlaneacionOFEstatus.PendienteValidacionMP;
                cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaProduccionId;
                cmd.Parameters.Add("@SolicitudProduccionDetalleID", SqlDbType.Int).Value = vm.SolicitudProduccionDetalleID.Value;
                await cmd.ExecuteNonQueryAsync();
            }
            const string sqlBlanca = @"
UPDATE dbo.Planeacion_ProductoIncompletoApartado
SET ProgramaProduccionID=@ProgramaProduccionID,SolicitudProduccionID=@SolicitudProduccionID,SolicitudProduccionDetalleID=@SolicitudProduccionDetalleID,EstatusID=3,
Observaciones=LEFT(COALESCE(NULLIF(Observaciones,N'')+N' | ',N'')+N'Asignada a OF manual '+CONVERT(NVARCHAR(20),@SolicitudProduccionID)+N'.',500)
WHERE ReleaseDetalleID=@ReleaseDetalleID AND Activo=1 AND EstatusID IN(1,2);
UPDATE c SET c.ProgramaReservaID=@ProgramaProduccionID,c.SolicitudReservaID=@SolicitudProduccionID,c.SolicitudDetalleReservaID=@SolicitudProduccionDetalleID,
c.UsuarioModificacionID=@UsuarioID,c.FechaModificacion=SYSDATETIME()
FROM dbo.Produccion_Cajas c
INNER JOIN dbo.Planeacion_ProductoIncompletoApartado a ON a.CajaProduccionID=c.CajaProduccionID
WHERE a.ReleaseDetalleID=@ReleaseDetalleID AND a.ProgramaProduccionID=@ProgramaProduccionID AND a.Activo=1 AND a.EstatusID=3 AND c.Activo=1;";
            await using (var cmd = new SqlCommand(sqlBlanca, cn, tx))
            {
                cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaProduccionId;
                cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = vm.SolicitudProduccionID.Value;
                cmd.Parameters.Add("@SolicitudProduccionDetalleID", SqlDbType.Int).Value = vm.SolicitudProduccionDetalleID.Value;
                cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value = vm.ReleaseDetalleID;
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                await cmd.ExecuteNonQueryAsync();
            }
            const string sqlHistorial = @"
INSERT INTO dbo.SolicitudProduccionHistorial(SolicitudProduccionID,EstatusAnteriorID,EstatusNuevoID,Movimiento,Comentario,UsuarioID,FechaMovimiento)
VALUES(@SolicitudProduccionID,@EstatusAnteriorID,@EstatusNuevoID,N'Programación de OF manual',
N'La OF manual fue vinculada al Programa de Producción ID '+CONVERT(NVARCHAR(20),@ProgramaProduccionID)+
CASE WHEN @ProductoIncompleto>0 THEN N'. Producto incompleto asignado: '+CONVERT(NVARCHAR(20),@ProductoIncompleto)+N' pieza(s). Cantidad real a producir: '+CONVERT(NVARCHAR(20),@CantidadProgramada)+N'.' ELSE N'.' END,
@UsuarioID,GETDATE());";
            await using (var cmd = new SqlCommand(sqlHistorial, cn, tx))
            {
                cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = vm.SolicitudProduccionID.Value;
                cmd.Parameters.Add("@EstatusAnteriorID", SqlDbType.Int).Value = (object?)estatusAnterior ?? DBNull.Value;
                cmd.Parameters.Add("@EstatusNuevoID", SqlDbType.Int).Value = PlaneacionOFEstatus.PendienteValidacionMP;
                cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaProduccionId;
                cmd.Parameters.Add("@ProductoIncompleto", SqlDbType.Int).Value = vm.ProductoIncompletoApartado;
                cmd.Parameters.Add("@CantidadProgramada", SqlDbType.Int).Value = vm.CantidadProgramada;
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                await cmd.ExecuteNonQueryAsync();
            }
        }

        private async Task<int> InsertarProgramaAsync(PlaneacionProgramaCrearDesdeNecesidadVm vm, int usuarioId, SqlConnection cn, SqlTransaction tx)
        {
            var secuencia = await ObtenerSiguienteSecuenciaMaquinaAsync(vm.MaquinaID, cn, tx);
            const string sql = @"
DECLARE @NuevoPrograma TABLE(ProgramaProduccionID INT NOT NULL);
INSERT INTO dbo.Planeacion_ProgramaProduccion
(ReleaseID,ReleaseDetalleID,SolicitudProduccionID,SolicitudProduccionDetalleID,ClienteID,ClienteNombre,ParteID,NumeroParte,ReferenciaSAP,DesignacionDescripcionSAP,Color,CantidadRequerida,PiezasDesdePT,CantidadProgramada,CantidadProducida,MaquinaID,MaquinaCodigo,MaquinaNombre,MoldeID,MoldeCodigo,CondicionProduccion,TipoOF,MotivoTipoOF,SecuenciaMaquina,FechaInicioProgramada,FechaFinProgramada,HorasProgramadas,Cambio,Arranque,ObjetivoHora,Ciclo,Cavidades,PesoBrutoPieza,MaterialID,MaterialCodigo,MaterialDescripcion,CantidadMpKg,EmbalajeCodigo,EmbalajeDescripcion,PiezasPorEmbalaje,CantidadEmbalajes,EstatusID,Observaciones,UsuarioCreacionID,FechaCreacion,Activo)
OUTPUT INSERTED.ProgramaProduccionID INTO @NuevoPrograma
VALUES(@ReleaseID,@ReleaseDetalleID,@SolicitudProduccionID,@SolicitudProduccionDetalleID,@ClienteID,@ClienteNombre,@ParteID,@NumeroParte,@ReferenciaSAP,@DesignacionDescripcionSAP,@Color,@CantidadRequerida,@PiezasDesdePT,@CantidadProgramada,0,@MaquinaID,@MaquinaCodigo,@MaquinaNombre,@MoldeID,@MoldeCodigo,@CondicionProduccion,@TipoOF,@MotivoTipoOF,@SecuenciaMaquina,@FechaInicioProgramada,@FechaFinProgramada,@HorasProgramadas,@Cambio,@Arranque,@ObjetivoHora,@Ciclo,@Cavidades,@PesoBrutoPieza,@MaterialID,@MaterialCodigo,@MaterialDescripcion,@CantidadMpKg,@EmbalajeCodigo,@EmbalajeDescripcion,@PiezasPorEmbalaje,@CantidadEmbalajes,@EstatusID,@Observaciones,@UsuarioCreacionID,GETDATE(),1);
DECLARE @ProgramaProduccionID INT=(SELECT TOP(1)ProgramaProduccionID FROM @NuevoPrograma);
UPDATE dbo.Planeacion_ProductoIncompletoApartado
SET ProgramaProduccionID=@ProgramaProduccionID,EstatusID=2,
Observaciones=LEFT(COALESCE(NULLIF(Observaciones,N'')+N' | ',N'')+N'Asignada definitivamente al Programa '+CONVERT(NVARCHAR(20),@ProgramaProduccionID)+N'.',500)
WHERE ReleaseDetalleID=@ReleaseDetalleID AND Activo=1 AND EstatusID=1;
UPDATE c SET c.ProgramaReservaID=@ProgramaProduccionID,c.UsuarioModificacionID=@UsuarioCreacionID,c.FechaModificacion=SYSDATETIME()
FROM dbo.Produccion_Cajas c
INNER JOIN dbo.Planeacion_ProductoIncompletoApartado a ON a.CajaProduccionID=c.CajaProduccionID
WHERE a.ReleaseDetalleID=@ReleaseDetalleID AND a.ProgramaProduccionID=@ProgramaProduccionID AND a.Activo=1 AND a.EstatusID=2 AND c.Activo=1;
SELECT @ProgramaProduccionID;";
            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@ReleaseID", SqlDbType.Int).Value = (object?)vm.ReleaseID ?? DBNull.Value;
            cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value = vm.ReleaseDetalleID;
            cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = (object?)vm.SolicitudProduccionID ?? DBNull.Value;
            cmd.Parameters.Add("@SolicitudProduccionDetalleID", SqlDbType.Int).Value = (object?)vm.SolicitudProduccionDetalleID ?? DBNull.Value;
            cmd.Parameters.Add("@ClienteID", SqlDbType.Int).Value = (object?)vm.ClienteID ?? DBNull.Value;
            cmd.Parameters.Add("@ClienteNombre", SqlDbType.NVarChar, 200).Value = (object?)vm.ClienteNombre ?? DBNull.Value;
            cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value = (object?)vm.ParteID ?? DBNull.Value;
            cmd.Parameters.Add("@NumeroParte", SqlDbType.NVarChar, 120).Value = (object?)vm.NumeroParte ?? DBNull.Value;
            cmd.Parameters.Add("@ReferenciaSAP", SqlDbType.NVarChar, 150).Value = (object?)vm.ReferenciaSAP ?? DBNull.Value;
            cmd.Parameters.Add("@DesignacionDescripcionSAP", SqlDbType.NVarChar, 300).Value = (object?)vm.DesignacionDescripcionSAP ?? DBNull.Value;
            cmd.Parameters.Add("@Color", SqlDbType.NVarChar, 100).Value = (object?)vm.Color ?? DBNull.Value;
            cmd.Parameters.Add("@CantidadRequerida", SqlDbType.Int).Value = vm.CantidadRequerida;
            cmd.Parameters.Add("@PiezasDesdePT", SqlDbType.Int).Value = vm.PiezasDesdePT;
            cmd.Parameters.Add("@CantidadProgramada", SqlDbType.Int).Value = vm.CantidadProgramada;
            cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = (object?)vm.MaquinaID ?? DBNull.Value;
            cmd.Parameters.Add("@MaquinaCodigo", SqlDbType.NVarChar, 100).Value = (object?)vm.MaquinaCodigo ?? DBNull.Value;
            cmd.Parameters.Add("@MaquinaNombre", SqlDbType.NVarChar, 200).Value = (object?)vm.MaquinaNombre ?? DBNull.Value;
            cmd.Parameters.Add("@MoldeID", SqlDbType.Int).Value = (object?)vm.MoldeID ?? DBNull.Value;
            cmd.Parameters.Add("@MoldeCodigo", SqlDbType.NVarChar, 100).Value = (object?)vm.MoldeCodigo ?? DBNull.Value;
            cmd.Parameters.Add("@CondicionProduccion", SqlDbType.NVarChar, 20).Value = (object?)vm.CondicionProduccion ?? DBNull.Value;
            cmd.Parameters.Add("@TipoOF", SqlDbType.NVarChar, 30).Value = "RELEASE";
            cmd.Parameters.Add("@MotivoTipoOF", SqlDbType.NVarChar, 500).Value = DBNull.Value;
            cmd.Parameters.Add("@SecuenciaMaquina", SqlDbType.Int).Value = (object?)secuencia ?? DBNull.Value;
            cmd.Parameters.Add("@FechaInicioProgramada", SqlDbType.DateTime).Value = (object?)vm.FechaInicioProgramada ?? DBNull.Value;
            cmd.Parameters.Add("@FechaFinProgramada", SqlDbType.DateTime).Value = (object?)vm.FechaFinProgramada ?? DBNull.Value;
            AddDecimal(cmd, "@HorasProgramadas", vm.HorasProgramadas, 18, 2);
            cmd.Parameters.Add("@Cambio", SqlDbType.Time).Value = (object?)vm.Cambio ?? DBNull.Value;
            cmd.Parameters.Add("@Arranque", SqlDbType.Time).Value = (object?)vm.Arranque ?? DBNull.Value;
            cmd.Parameters.Add("@ObjetivoHora", SqlDbType.Int).Value = (object?)vm.ObjetivoHora ?? DBNull.Value;
            cmd.Parameters.Add("@Ciclo", SqlDbType.NVarChar, 50).Value = (object?)vm.Ciclo ?? DBNull.Value;
            cmd.Parameters.Add("@Cavidades", SqlDbType.Int).Value = (object?)vm.Cavidades ?? DBNull.Value;
            AddDecimal(cmd, "@PesoBrutoPieza", vm.PesoBrutoPieza, 18, 6);
            cmd.Parameters.Add("@MaterialID", SqlDbType.Int).Value = (object?)vm.MaterialID ?? DBNull.Value;
            cmd.Parameters.Add("@MaterialCodigo", SqlDbType.NVarChar, 100).Value = (object?)vm.MaterialCodigo ?? DBNull.Value;
            cmd.Parameters.Add("@MaterialDescripcion", SqlDbType.NVarChar, 250).Value = (object?)vm.MaterialDescripcion ?? DBNull.Value;
            AddDecimal(cmd, "@CantidadMpKg", vm.CantidadMpKg, 18, 4);
            cmd.Parameters.Add("@EmbalajeCodigo", SqlDbType.NVarChar, 100).Value = (object?)vm.EmbalajeCodigo ?? DBNull.Value;
            cmd.Parameters.Add("@EmbalajeDescripcion", SqlDbType.NVarChar, 250).Value = (object?)vm.EmbalajeDescripcion ?? DBNull.Value;
            AddDecimal(cmd, "@PiezasPorEmbalaje", vm.PiezasPorEmbalaje, 18, 4);
            AddDecimal(cmd, "@CantidadEmbalajes", vm.CantidadEmbalajes, 18, 4);
            cmd.Parameters.Add("@EstatusID", SqlDbType.Int).Value = PlaneacionProgramaEstatus.Programado;
            cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 500).Value = (object?)vm.Observaciones ?? DBNull.Value;
            cmd.Parameters.Add("@UsuarioCreacionID", SqlDbType.Int).Value = usuarioId;
            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }


        private async Task<int?> ObtenerSiguienteSecuenciaMaquinaAsync(
            int? maquinaId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            if (!maquinaId.HasValue)
                return null;

            const string sql = @"
SELECT ISNULL(MAX(SecuenciaMaquina), 0) + 1
FROM dbo.Planeacion_ProgramaProduccion
WHERE MaquinaID = @MaquinaID
  AND Activo = 1
  AND EstatusID NOT IN (5, 9, 99);";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = maquinaId.Value;

            var result = await cmd.ExecuteScalarAsync();

            return result == null || result == DBNull.Value
                ? 1
                : Convert.ToInt32(result);
        }

        private async Task<bool> ReleaseDetalleYaProgramadoAsync(
            int releaseDetalleId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
SELECT COUNT(1)
FROM dbo.Planeacion_ProgramaProduccion
WHERE ReleaseDetalleID = @ReleaseDetalleID
  AND Activo = 1
  AND EstatusID NOT IN (99);";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value = releaseDetalleId;

            var result = Convert.ToInt32(await cmd.ExecuteScalarAsync());

            return result > 0;
        }

        private async Task MarcarReleaseDetalleProgramadoAsync(
            int releaseDetalleId,
            int programaId,
            int usuarioId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
UPDATE dbo.Planeacion_ReleaseDetalle
SET
    ProgramaProduccionID = @ProgramaProduccionID,
    FechaProgramado = GETDATE(),
    UsuarioProgramoID = @UsuarioProgramoID,
    EstatusID = 3,
    UsuarioModificacionID = @UsuarioProgramoID,
    FechaModificacion = GETDATE()
WHERE ReleaseDetalleID = @ReleaseDetalleID;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaId;
            cmd.Parameters.Add("@UsuarioProgramoID", SqlDbType.Int).Value = usuarioId;
            cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value = releaseDetalleId;

            await cmd.ExecuteNonQueryAsync();
        }


        /* =====================================================================
         * MODO SIN ALMACEN ACTIVO (2026-07-30)
         * Este metodo queda comentado temporalmente porque el modulo de
         * Planeacion se lanzara sin descontar/apartar PT desde Almacen.
         * Cuando Almacen este listo, quitar este comentario y reactivar
         * tambien las llamadas marcadas como REACTIVAR_ALMACEN.
         * =====================================================================
        private async Task VincularApartadoPTAProgramaAsync(
            int releaseDetalleId,
            int programaProduccionId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
IF OBJECT_ID('dbo.Planeacion_PT_Apartado', 'U') IS NULL
    RETURN;

UPDATE dbo.Planeacion_PT_Apartado
SET
    ProgramaProduccionID = @ProgramaProduccionID,
    FechaModificacion = GETDATE(),
    Observaciones = 'Apartado vinculado al Programa de Producción.'
WHERE ReleaseDetalleID = @ReleaseDetalleID
  AND Activo = 1
  AND EstatusID = 1;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaProduccionId;
            cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value = releaseDetalleId;
            await cmd.ExecuteNonQueryAsync();
        }
         */

        /* =====================================================================
         * MODO SIN ALMACEN ACTIVO (2026-07-30)
         * Este metodo queda comentado temporalmente porque el modulo de
         * Planeacion se lanzara sin descontar/apartar PT desde Almacen.
         * Cuando Almacen este listo, quitar este comentario y reactivar
         * tambien las llamadas marcadas como REACTIVAR_ALMACEN.
         * =====================================================================
        private async Task VincularApartadoPTAOFAsync(
            int releaseDetalleId,
            int programaProduccionId,
            int solicitudProduccionId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
IF OBJECT_ID('dbo.Planeacion_PT_Apartado', 'U') IS NULL
    RETURN;

UPDATE dbo.Planeacion_PT_Apartado
SET
    ProgramaProduccionID = @ProgramaProduccionID,
    SolicitudProduccionID = @SolicitudProduccionID,
    FechaModificacion = GETDATE(),
    Observaciones = 'Apartado vinculado a la OF. Sigue reservado hasta que Almacén confirme salida/consumo.'
WHERE ReleaseDetalleID = @ReleaseDetalleID
  AND Activo = 1
  AND EstatusID = 1;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaProduccionId;
            cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = solicitudProduccionId;
            cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value = releaseDetalleId;
            await cmd.ExecuteNonQueryAsync();
        }
         */

        private async Task<List<PlaneacionProgramaMaquinaVm>> ObtenerMaquinasAsync(SqlConnection cn)
        {
            var lista = new List<PlaneacionProgramaMaquinaVm>();

            const string sql = @"
SELECT
    MaquinaID,
    Codigo,
    Nombre
FROM dbo.ERP_Maquinas
WHERE Activo = 1
  AND UPPER(REPLACE(ISNULL(Codigo,N''),N' ',N'')) <> N'1200T'
  AND UPPER(REPLACE(ISNULL(Nombre,N''),N' ',N'')) NOT LIKE N'%1200T%'
ORDER BY Codigo;";

            await using var cmd = new SqlCommand(sql, cn);
            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                lista.Add(new PlaneacionProgramaMaquinaVm
                {
                    MaquinaID = rd["MaquinaID"] == DBNull.Value ? null : Convert.ToInt32(rd["MaquinaID"]),
                    MaquinaCodigo = rd["Codigo"] as string ?? "",
                    MaquinaNombre = rd["Nombre"] as string ?? ""
                });
            }

            return lista;
        }

        private async Task<List<PlaneacionProgramaIndexVm>> ObtenerProgramasPorRangoAsync(
    DateTime fechaDesde,
    DateTime fechaHasta)
        {
            var lista = new List<PlaneacionProgramaIndexVm>();

            const string sql = @"
SELECT
    pp.ProgramaProduccionID,
    pp.ReleaseID,
pp.ReleaseDetalleID,
pp.SolicitudProduccionID,
pp.SolicitudProduccionDetalleID,
r.FolioRelease,

    pp.ClienteID,
    ISNULL(c.Nombre, pp.ClienteNombre) AS ClienteNombre,

    pp.ParteID,
    pp.NumeroParte,
    pp.ReferenciaSAP,
    pp.DesignacionDescripcionSAP,

pp.Color,

    pp.CantidadRequerida,
    pp.PiezasDesdePT,
    pp.CantidadProgramada,
    pp.CantidadProducida,
    pp.CantidadPendiente,

    pp.MaquinaID,
    pp.MaquinaCodigo,
    pp.MaquinaNombre,

    pp.MoldeID,
    pp.MoldeCodigo,

    pp.CondicionProduccion,
    pp.SecuenciaMaquina,

    pp.FechaInicioProgramada,
    pp.FechaFinProgramada,
    pp.HorasProgramadas,
pp.Cambio,
pp.Arranque,

    pp.EstatusID,
    pp.Observaciones,
    pp.FechaCreacion
FROM dbo.Planeacion_ProgramaProduccion pp
LEFT JOIN dbo.Planeacion_Releases r
    ON r.ReleaseID = pp.ReleaseID
LEFT JOIN dbo.ERP_Clientes c
    ON c.ClienteID = pp.ClienteID
WHERE pp.Activo = 1
  AND (
        pp.FechaInicioProgramada < DATEADD(DAY, 1, @FechaHasta)
    AND ISNULL(pp.FechaFinProgramada, pp.FechaInicioProgramada) >= @FechaDesde
)
ORDER BY
    pp.MaquinaCodigo,
    pp.FechaInicioProgramada,
    pp.SecuenciaMaquina;";

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@FechaDesde", SqlDbType.Date).Value = fechaDesde.Date;
            cmd.Parameters.Add("@FechaHasta", SqlDbType.Date).Value = fechaHasta.Date;

            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                lista.Add(MapPrograma(rd));
            }

            return lista;
        }

        private static PlaneacionProgramaIndexVm MapPrograma(SqlDataReader rd)
        {
            return new PlaneacionProgramaIndexVm
            {
                ProgramaProduccionID = Convert.ToInt32(rd["ProgramaProduccionID"]),

                ReleaseID = rd["ReleaseID"] == DBNull.Value ? null : Convert.ToInt32(rd["ReleaseID"]),
                ReleaseDetalleID = rd["ReleaseDetalleID"] == DBNull.Value ? null : Convert.ToInt32(rd["ReleaseDetalleID"]),

                SolicitudProduccionID = rd["SolicitudProduccionID"] == DBNull.Value ? null : Convert.ToInt32(rd["SolicitudProduccionID"]),
                SolicitudProduccionDetalleID = rd["SolicitudProduccionDetalleID"] == DBNull.Value ? null : Convert.ToInt32(rd["SolicitudProduccionDetalleID"]),

                FolioRelease = rd["FolioRelease"] as string,

                ClienteID = rd["ClienteID"] == DBNull.Value ? null : Convert.ToInt32(rd["ClienteID"]),
                ClienteNombre = rd["ClienteNombre"] as string,

                ParteID = rd["ParteID"] == DBNull.Value ? null : Convert.ToInt32(rd["ParteID"]),
                NumeroParte = rd["NumeroParte"] as string,
                ReferenciaSAP = rd["ReferenciaSAP"] as string,
                DesignacionDescripcionSAP = rd["DesignacionDescripcionSAP"] as string,

                Color = rd["Color"] as string,

                CantidadRequerida = Convert.ToInt32(rd["CantidadRequerida"]),
                PiezasDesdePT = Convert.ToInt32(rd["PiezasDesdePT"]),
                CantidadProgramada = Convert.ToInt32(rd["CantidadProgramada"]),
                CantidadProducida = Convert.ToInt32(rd["CantidadProducida"]),
                CantidadPendiente = Convert.ToInt32(rd["CantidadPendiente"]),

                MaquinaID = rd["MaquinaID"] == DBNull.Value ? null : Convert.ToInt32(rd["MaquinaID"]),
                MaquinaCodigo = rd["MaquinaCodigo"] as string,
                MaquinaNombre = rd["MaquinaNombre"] as string,

                MoldeID = rd["MoldeID"] == DBNull.Value ? null : Convert.ToInt32(rd["MoldeID"]),
                MoldeCodigo = rd["MoldeCodigo"] as string,

                CondicionProduccion = rd["CondicionProduccion"] as string,
                SecuenciaMaquina = rd["SecuenciaMaquina"] == DBNull.Value ? null : Convert.ToInt32(rd["SecuenciaMaquina"]),

                FechaInicioProgramada = rd["FechaInicioProgramada"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaInicioProgramada"]),
                FechaFinProgramada = rd["FechaFinProgramada"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaFinProgramada"]),
                HorasProgramadas = rd["HorasProgramadas"] == DBNull.Value ? null : Convert.ToDecimal(rd["HorasProgramadas"]),
                Cambio = rd["Cambio"] == DBNull.Value ? null : (TimeSpan)rd["Cambio"],
                Arranque = rd["Arranque"] == DBNull.Value ? null : (TimeSpan)rd["Arranque"],

                EstatusID = Convert.ToInt32(rd["EstatusID"]),
                Observaciones = rd["Observaciones"] as string,
                FechaCreacion = Convert.ToDateTime(rd["FechaCreacion"])
            };
        }



        private static DateTime SumarHorasOperativasPlaneacion(
            DateTime inicio,
            decimal horas,
            bool trabajarDomingo = false)
        {
            if (horas <= 0)
                return inicio;

            var actual = inicio;
            var restante = horas;

            while (restante > 0)
            {
                if (!ObtenerIntervaloOperativoPlaneacion(actual, trabajarDomingo, out var apertura, out var cierre))
                {
                    actual = SiguienteAperturaOperativaPlaneacion(actual, trabajarDomingo);
                    continue;
                }

                if (actual < apertura)
                    actual = apertura;

                if (actual >= cierre)
                {
                    actual = SiguienteAperturaOperativaPlaneacion(cierre.AddMinutes(1), trabajarDomingo);
                    continue;
                }

                var disponibles = (decimal)(cierre - actual).TotalHours;

                if (disponibles >= restante)
                    return actual.AddHours((double)restante);

                restante -= disponibles;
                actual = SiguienteAperturaOperativaPlaneacion(cierre.AddMinutes(1), trabajarDomingo);
            }

            return actual;
        }

        private static bool EsInstanteOperativoPlaneacion(
            DateTime fecha,
            bool trabajarDomingo = false)
        {
            return ObtenerIntervaloOperativoPlaneacion(fecha, trabajarDomingo, out var apertura, out var cierre) &&
                   fecha >= apertura &&
                   fecha < cierre;
        }

        private static DateTime SiguienteAperturaOperativaPlaneacion(
            DateTime fecha,
            bool trabajarDomingo = false)
        {
            var actual = fecha;

            for (var i = 0; i < 21; i++)
            {
                if (ObtenerIntervaloOperativoPlaneacion(actual, trabajarDomingo, out var apertura, out var cierre))
                {
                    if (actual < apertura)
                        return apertura;

                    if (actual >= apertura && actual < cierre)
                        return actual;
                }

                actual = actual.Date.AddDays(1);
            }

            return fecha;
        }

        private static bool ObtenerIntervaloOperativoPlaneacion(
            DateTime fecha,
            bool trabajarDomingo,
            out DateTime apertura,
            out DateTime cierre)
        {
            var dia = fecha.Date;

            apertura = dia;
            cierre = dia;

            if (fecha.DayOfWeek == DayOfWeek.Sunday)
            {
                if (!trabajarDomingo)
                    return false;

                apertura = dia;
                cierre = dia.AddDays(1);
                return true;
            }

            if (fecha.DayOfWeek == DayOfWeek.Monday)
            {
                apertura = dia.AddHours(7);
                cierre = dia.AddDays(1);
                return true;
            }

            if (fecha.DayOfWeek == DayOfWeek.Tuesday ||
                fecha.DayOfWeek == DayOfWeek.Wednesday ||
                fecha.DayOfWeek == DayOfWeek.Thursday ||
                fecha.DayOfWeek == DayOfWeek.Friday)
            {
                apertura = dia;
                cierre = dia.AddDays(1);
                return true;
            }

            if (fecha.DayOfWeek == DayOfWeek.Saturday)
            {
                apertura = dia;

                // Producción termina los sábados a las 22:30.
                cierre = dia.AddHours(22).AddMinutes(30);

                return true;
            }

            return false;
        }

        private static async Task ActivarReacomodoPlaneacionAsync(SqlConnection cn, SqlTransaction tx)
        {
            const string sql = @"EXEC sys.sp_set_session_context @key = N'PlaneacionPermitirReacomodo', @value = 1;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task ValidarSinCrucesPlaneacionAsync(SqlConnection cn, SqlTransaction tx)
        {
            const string sql = @"
IF OBJECT_ID('dbo.Planeacion_ValidarProgramaSinCruces', 'P') IS NOT NULL
BEGIN
    EXEC dbo.Planeacion_ValidarProgramaSinCruces;
END;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            await cmd.ExecuteNonQueryAsync();
        }


        private async Task CargarCatalogosAsync(PlaneacionProgramaCrearDesdeNecesidadVm vm)
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            var inicio = vm.FechaInicioProgramada ?? RedondearSiguienteBloque(DateTime.Now, 15);

            DateTime fin;

            if (vm.FechaFinProgramada.HasValue && vm.FechaFinProgramada.Value > inicio)
            {
                fin = vm.FechaFinProgramada.Value;
            }
            else if (vm.HorasProgramadas.HasValue && vm.HorasProgramadas.Value > 0)
            {
                fin = inicio.AddHours((double)vm.HorasProgramadas.Value);
            }
            else
            {
                fin = inicio.AddMinutes(1);
            }

            vm.Maquinas = await CargarMaquinasConEstadoAsync(
                cn,
                inicio,
                fin,
                vm.MaquinaID,
                vm.ParteID
            );

            vm.Moldes = await CargarSelectAsync(
                cn,
                @"SELECT 
              MoldeID AS Id,
              CodigoMolde AS Texto
          FROM dbo.ERP_Moldes
          WHERE Activo = 1
          ORDER BY CodigoMolde;"
            );

            vm.Operadores = await CargarSelectAsync(
                cn,
                @"SELECT 
              PersonaID AS Id,
              LTRIM(RTRIM(
                  ISNULL(Nombre, '') + ' ' +
                  ISNULL(ApellidoPaterno, '') + ' ' +
                  ISNULL(ApellidoMaterno, '')
              )) AS Texto
          FROM dbo.Persona
          WHERE EsColaboradorActivo = 1
            AND UPPER(LTRIM(RTRIM(Puesto))) = 'OPERADOR'
          ORDER BY Nombre, ApellidoPaterno, ApellidoMaterno;"
            );

            vm.Condiciones = PlaneacionProgramaCondicion
                .SelectList()
                .Where(x => !string.Equals(
                    x.Value,
                    PlaneacionProgramaCondicion.InterrumpirProduccion,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();
        }


        private async Task InsertarOperadoresProgramaAsync(
    int programaProduccionId,
    PlaneacionProgramaCrearDesdeNecesidadVm vm,
    int usuarioId,
    SqlConnection cn,
    SqlTransaction tx)
        {
            const string sqlDesactivar = @"
UPDATE dbo.Planeacion_ProgramaOperadores
SET
    Activo = 0,
    UsuarioModificacionID = @UsuarioModificacionID,
    FechaModificacion = GETDATE()
WHERE ProgramaProduccionID = @ProgramaProduccionID
  AND Activo = 1;";

            await using (var cmd = new SqlCommand(sqlDesactivar, cn, tx))
            {
                cmd.Parameters.Add("@UsuarioModificacionID", SqlDbType.Int).Value = usuarioId;
                cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaProduccionId;

                await cmd.ExecuteNonQueryAsync();
            }

            const string sqlInsert = @"
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
    @UsuarioCreacionID,
    GETDATE()
);";

            async Task InsertarUnoAsync(int? personaId, string rol)
            {
                if (!personaId.HasValue || personaId.Value <= 0)
                    return;

                await using var cmd = new SqlCommand(sqlInsert, cn, tx);

                cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaProduccionId;
                cmd.Parameters.Add("@PersonaID", SqlDbType.Int).Value = personaId.Value;
                cmd.Parameters.Add("@RolOperador", SqlDbType.NVarChar, 30).Value = rol;
                cmd.Parameters.Add("@UsuarioCreacionID", SqlDbType.Int).Value = usuarioId;

                await cmd.ExecuteNonQueryAsync();
            }

            await InsertarUnoAsync(vm.OperadorPrincipalID, "PRINCIPAL");
            await InsertarUnoAsync(vm.OperadorAuxiliarID, "AUXILIAR");
        }


        private async Task<List<SelectListItem>> CargarMaquinasConEstadoAsync(
    SqlConnection cn,
    DateTime inicio,
    DateTime fin,
    int? maquinaSeleccionadaId,
    int? parteId)
        {
            const string sql = @"
;WITH DatosTecnicos AS
(
    SELECT TOP (1)
        t.MaquinaPrincipalID,
        t.MaquinaSustitutaID
    FROM dbo.ERP_ParteDatosTecnicos t
    WHERE t.ParteID = @ParteID
      AND t.Activo = 1
),
Compatibles AS
(
    SELECT
        dt.MaquinaPrincipalID AS MaquinaID
    FROM DatosTecnicos dt
    WHERE dt.MaquinaPrincipalID IS NOT NULL

    UNION

    SELECT
        dt.MaquinaSustitutaID AS MaquinaID
    FROM DatosTecnicos dt
    WHERE dt.MaquinaSustitutaID IS NOT NULL

    UNION

    SELECT
        ms.MaquinaSustitutaID AS MaquinaID
    FROM DatosTecnicos dt
    INNER JOIN dbo.ERP_MaquinasSustitutas ms
        ON ms.MaquinaPrincipalID = dt.MaquinaPrincipalID
       AND ms.Activo = 1
    WHERE dt.MaquinaPrincipalID IS NOT NULL

    UNION

    SELECT
        ms.MaquinaPrincipalID AS MaquinaID
    FROM DatosTecnicos dt
    INNER JOIN dbo.ERP_MaquinasSustitutas ms
        ON ms.MaquinaSustitutaID = dt.MaquinaPrincipalID
       AND ms.Activo = 1
    WHERE dt.MaquinaPrincipalID IS NOT NULL
)
SELECT
    m.MaquinaID AS Id,
    m.Codigo + ' | ' + ISNULL(m.Nombre, '') AS Texto,
    CAST(
        CASE
            WHEN EXISTS
            (
                SELECT 1
                FROM dbo.Planeacion_ProgramaProduccion pp
                WHERE pp.Activo = 1
                  AND pp.MaquinaID = m.MaquinaID
                  AND ISNULL(pp.EstatusID, 1) NOT IN (5, 9, 99)
                  AND pp.FechaInicioProgramada < @Fin
                  AND ISNULL(
                        pp.FechaFinProgramada,
                        DATEADD(MINUTE, CAST(CEILING(ISNULL(pp.HorasProgramadas, 1) * 60) AS INT), pp.FechaInicioProgramada)
                      ) > @Inicio
            )
            THEN 1
            ELSE 0
        END AS bit
    ) AS Ocupada
FROM dbo.ERP_Maquinas m
WHERE m.Activo = 1
  AND UPPER(REPLACE(ISNULL(m.Codigo,N''),N' ',N'')) <> N'1200T'
  AND UPPER(REPLACE(ISNULL(m.Nombre,N''),N' ',N'')) NOT LIKE N'%1200T%'
  AND
  (
        @ParteID IS NULL
     OR EXISTS
        (
            SELECT 1
            FROM Compatibles c
            WHERE c.MaquinaID = m.MaquinaID
        )
  )
ORDER BY
    m.Codigo;";

            var lista = new List<SelectListItem>();

            await using var cmd = new SqlCommand(sql, cn);

            cmd.Parameters.Add("@Inicio", SqlDbType.DateTime).Value = inicio;
            cmd.Parameters.Add("@Fin", SqlDbType.DateTime).Value = fin;
            cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value =
                (object?)parteId ?? DBNull.Value;

            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                var id = Convert.ToInt32(rd["Id"]);
                var ocupada = rd["Ocupada"] != DBNull.Value && Convert.ToBoolean(rd["Ocupada"]);
                var texto = rd["Texto"]?.ToString() ?? id.ToString();

                lista.Add(new SelectListItem
                {
                    Value = id.ToString(),
                    Text = ocupada
                        ? texto + "  — OCUPADA: se respetará la cola"
                        : texto + "  — LIBRE",
                    Disabled = false,
                    Selected = maquinaSeleccionadaId.HasValue &&
                               maquinaSeleccionadaId.Value == id
                });
            }

            return lista;
        }


     

        private static async Task<bool> CambioMoldeTieneCruceAsync(
    DateTime fechaCambio,
    SqlConnection cn,
    SqlTransaction? tx)
        {
            const string sql = @"
SELECT TOP 1 1
FROM dbo.Planeacion_ProgramaProduccion pp
WHERE pp.Activo = 1
  AND ISNULL(pp.EstatusID, 1) NOT IN (5, 9, 99)
  AND pp.Cambio IS NOT NULL
  AND pp.Arranque IS NOT NULL
  AND pp.Cambio <> pp.Arranque
  AND CAST(pp.FechaInicioProgramada AS DATE) = CAST(@FechaCambio AS DATE)
  AND DATEPART(HOUR, pp.FechaInicioProgramada) = DATEPART(HOUR, @FechaCambio);";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@FechaCambio", SqlDbType.DateTime).Value = fechaCambio;

            var result = await cmd.ExecuteScalarAsync();
            return result != null && result != DBNull.Value;
        }



        private static async Task<bool> MoldeTieneCruceAsync(
            int moldeId,
            DateTime inicio,
            DateTime fin,
            int? maquinaActualId,
            SqlConnection cn,
            SqlTransaction? tx)
        {
            const string sql = @"
SELECT TOP 1 1
FROM dbo.Planeacion_ProgramaProduccion pp
WHERE pp.Activo = 1
  AND pp.MoldeID = @MoldeID
  AND (@MaquinaActualID IS NULL OR ISNULL(pp.MaquinaID, 0) <> @MaquinaActualID)
  AND ISNULL(pp.EstatusID, 1) NOT IN (5, 9, 99)
  AND pp.FechaInicioProgramada < @Fin
  AND ISNULL(
        pp.FechaFinProgramada,
        DATEADD(MINUTE, CAST(CEILING(ISNULL(pp.HorasProgramadas, 1) * 60) AS INT), pp.FechaInicioProgramada)
      ) > @Inicio;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@MoldeID", SqlDbType.Int).Value = moldeId;
            cmd.Parameters.Add("@MaquinaActualID", SqlDbType.Int).Value =
                (object?)maquinaActualId ?? DBNull.Value;
            cmd.Parameters.Add("@Inicio", SqlDbType.DateTime).Value = inicio;
            cmd.Parameters.Add("@Fin", SqlDbType.DateTime).Value = fin;

            var result = await cmd.ExecuteScalarAsync();
            return result != null && result != DBNull.Value;
        }

        private async Task<bool> MaquinaTieneCruceAsync(
            int maquinaId,
            DateTime inicio,
            DateTime fin)
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            return await MaquinaTieneCruceAsync(maquinaId, inicio, fin, cn, null);
        }

        private static async Task<bool> MaquinaTieneCruceAsync(
            int maquinaId,
            DateTime inicio,
            DateTime fin,
            SqlConnection cn,
            SqlTransaction? tx)
        {
            const string sql = @"
SELECT TOP 1 1
FROM dbo.Planeacion_ProgramaProduccion pp
WHERE pp.Activo = 1
  AND pp.MaquinaID = @MaquinaID
  AND ISNULL(pp.EstatusID, 1) NOT IN (5, 9, 99)
  AND pp.FechaInicioProgramada < @Fin
  AND ISNULL(
        pp.FechaFinProgramada,
        DATEADD(MINUTE, CAST(CEILING(ISNULL(pp.HorasProgramadas, 1) * 60) AS INT), pp.FechaInicioProgramada)
      ) > @Inicio;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = maquinaId;
            cmd.Parameters.Add("@Inicio", SqlDbType.DateTime).Value = inicio;
            cmd.Parameters.Add("@Fin", SqlDbType.DateTime).Value = fin;

            var result = await cmd.ExecuteScalarAsync();
            return result != null && result != DBNull.Value;
        }

        private static async Task InterrumpirProgramasCruzadosAsync(
            int maquinaId,
            DateTime nuevoInicio,
            DateTime nuevoFin,
            int usuarioId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
UPDATE pp
SET
    FechaFinProgramada =
        CASE
            WHEN pp.FechaInicioProgramada < @NuevoInicio THEN @NuevoInicio
            ELSE DATEADD(MINUTE, 1, pp.FechaInicioProgramada)
        END,
    EstatusID = @EstatusPausado,
    Observaciones =
        LEFT(
            ISNULL(pp.Observaciones, '') +
            CASE
                WHEN ISNULL(pp.Observaciones, '') = '' THEN ''
                ELSE CHAR(13) + CHAR(10)
            END +
            'Interrumpido por nuevo cambio de molde I.P el ' +
            CONVERT(NVARCHAR(16), GETDATE(), 120) +
            '. Nuevo inicio: ' +
            CONVERT(NVARCHAR(16), @NuevoInicio, 120) + '.',
            500
        ),
    UsuarioModificacionID = @UsuarioID,
    FechaModificacion = GETDATE()
FROM dbo.Planeacion_ProgramaProduccion pp
WHERE pp.Activo = 1
  AND pp.MaquinaID = @MaquinaID
  AND ISNULL(pp.EstatusID, 1) NOT IN (5, 9, 99)
  AND pp.FechaInicioProgramada < @NuevoFin
  AND ISNULL(
        pp.FechaFinProgramada,
        DATEADD(MINUTE, CAST(CEILING(ISNULL(pp.HorasProgramadas, 1) * 60) AS INT), pp.FechaInicioProgramada)
      ) > @NuevoInicio;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = maquinaId;
            cmd.Parameters.Add("@NuevoInicio", SqlDbType.DateTime).Value = nuevoInicio;
            cmd.Parameters.Add("@NuevoFin", SqlDbType.DateTime).Value = nuevoFin;
            cmd.Parameters.Add("@EstatusPausado", SqlDbType.Int).Value = PlaneacionProgramaEstatus.Pausado;
            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;

            await cmd.ExecuteNonQueryAsync();
        }


        private static async Task DesactivarReacomodoPlaneacionAsync(SqlConnection cn, SqlTransaction tx)
        {
            const string sql = @"EXEC sys.sp_set_session_context @key = N'PlaneacionPermitirReacomodo', @value = NULL;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            await cmd.ExecuteNonQueryAsync();
        }


        private static async Task<List<SelectListItem>> CargarSelectAsync(SqlConnection cn, string sql)
        {
            var lista = new List<SelectListItem>();

            await using var cmd = new SqlCommand(sql, cn);
            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                lista.Add(new SelectListItem
                {
                    Value = rd["Id"].ToString(),
                    Text = rd["Texto"].ToString()
                });
            }

            return lista;
        }

        private int ObtenerUsuarioID()
        {
            return HttpContext.Session.GetInt32("UsuarioID") ?? 0;
        }

        private static void AddDecimal(
            SqlCommand cmd,
            string name,
            decimal? value,
            byte precision,
            byte scale)
        {
            var p = cmd.Parameters.Add(name, SqlDbType.Decimal);
            p.Precision = precision;
            p.Scale = scale;
            p.Value = value.HasValue ? value.Value : DBNull.Value;
        }


        private async Task<ProgramaParaOFVm?> ObtenerProgramaParaGenerarOFAsync(
     int programaProduccionId,
     SqlConnection cn,
     SqlTransaction tx)
        {
            const string sql = @"
SELECT
    pp.ProgramaProduccionID,

    pp.ReleaseID,
    pp.ReleaseDetalleID,

    pp.SolicitudProduccionID,
    pp.SolicitudProduccionDetalleID,

    pp.ClienteID,
    pp.ClienteNombre,

    pp.ParteID,
    pp.NumeroParte,
    pp.ReferenciaSAP,
    pp.DesignacionDescripcionSAP,

    ISNULL(NULLIF(pp.TipoOF, ''), 'RELEASE') AS TipoOF,
    pp.MotivoTipoOF,

    pp.CantidadRequerida,
    pp.PiezasDesdePT,
    pp.CantidadProgramada,

    pp.MaquinaID,
    pp.MaquinaCodigo,
    pp.MaquinaNombre,

    pp.MoldeID,
    pp.MoldeCodigo,

    pp.CondicionProduccion,
    pp.SecuenciaMaquina,

    pp.FechaInicioProgramada,
    pp.FechaFinProgramada,
    pp.HorasProgramadas,
    pp.Cambio,
    pp.Arranque,

    COALESCE(pp.ObjetivoHora, t.ObjetivoHora) AS ObjetivoHora,
    COALESCE(pp.Ciclo, t.Ciclo) AS Ciclo,
    COALESCE(pp.Cavidades, t.Cavidades) AS Cavidades,
    COALESCE(pp.PesoBrutoPieza, t.PesoBrutoPieza) AS PesoBrutoPieza,

    COALESCE(NULLIF(pp.Color, ''), t.Color) AS Color,
    t.PiezasPorCaja,
    t.TipoSecado,
    t.HorasSecado,

    COALESCE(pp.MaterialID, t.MaterialID) AS MaterialID,
    COALESCE(pp.MaterialCodigo, t.MaterialCodigo) AS MaterialCodigo,
    COALESCE(pp.MaterialDescripcion, t.MaterialDescripcion) AS MaterialDescripcion,
    pp.CantidadMpKg,

    COALESCE(pp.EmbalajeCodigo, t.EmbalajeCodigo) AS EmbalajeCodigo,
    COALESCE(pp.EmbalajeDescripcion, t.EmbalajeDescripcion) AS EmbalajeDescripcion,
    COALESCE(pp.PiezasPorEmbalaje, t.PiezasPorEmbalaje) AS PiezasPorEmbalaje,
    pp.CantidadEmbalajes,

    pp.Observaciones
FROM dbo.Planeacion_ProgramaProduccion pp
LEFT JOIN dbo.ERP_ParteDatosTecnicos t
    ON t.ParteID = pp.ParteID
   AND t.Activo = 1
WHERE pp.ProgramaProduccionID = @ProgramaProduccionID
  AND pp.Activo = 1;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaProduccionId;

            await using var rd = await cmd.ExecuteReaderAsync();

            if (!await rd.ReadAsync())
                return null;

            return new ProgramaParaOFVm
            {
                ProgramaProduccionID = Convert.ToInt32(rd["ProgramaProduccionID"]),

                ReleaseID = rd["ReleaseID"] == DBNull.Value ? null : Convert.ToInt32(rd["ReleaseID"]),
                ReleaseDetalleID = rd["ReleaseDetalleID"] == DBNull.Value ? null : Convert.ToInt32(rd["ReleaseDetalleID"]),

                SolicitudProduccionID = rd["SolicitudProduccionID"] == DBNull.Value ? null : Convert.ToInt32(rd["SolicitudProduccionID"]),
                SolicitudProduccionDetalleID = rd["SolicitudProduccionDetalleID"] == DBNull.Value ? null : Convert.ToInt32(rd["SolicitudProduccionDetalleID"]),

                ClienteID = rd["ClienteID"] == DBNull.Value ? null : Convert.ToInt32(rd["ClienteID"]),
                ClienteNombre = rd["ClienteNombre"] as string,

                ParteID = rd["ParteID"] == DBNull.Value ? null : Convert.ToInt32(rd["ParteID"]),
                NumeroParte = rd["NumeroParte"] as string,
                ReferenciaSAP = rd["ReferenciaSAP"] as string,
                DesignacionDescripcionSAP = rd["DesignacionDescripcionSAP"] as string,

                TipoOF = rd["TipoOF"] == DBNull.Value
                    ? "RELEASE"
                    : NormalizarTipoOF(rd["TipoOF"] as string),

                MotivoTipoOF = rd["MotivoTipoOF"] as string,

                CantidadRequerida = Convert.ToInt32(rd["CantidadRequerida"]),
                PiezasDesdePT = Convert.ToInt32(rd["PiezasDesdePT"]),
                CantidadProgramada = Convert.ToInt32(rd["CantidadProgramada"]),

                MaquinaID = rd["MaquinaID"] == DBNull.Value ? null : Convert.ToInt32(rd["MaquinaID"]),
                MaquinaCodigo = rd["MaquinaCodigo"] as string,
                MaquinaNombre = rd["MaquinaNombre"] as string,

                MoldeID = rd["MoldeID"] == DBNull.Value ? null : Convert.ToInt32(rd["MoldeID"]),
                MoldeCodigo = rd["MoldeCodigo"] as string,

                CondicionProduccion = rd["CondicionProduccion"] as string,
                SecuenciaMaquina = rd["SecuenciaMaquina"] == DBNull.Value ? null : Convert.ToInt32(rd["SecuenciaMaquina"]),

                FechaInicioProgramada = rd["FechaInicioProgramada"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaInicioProgramada"]),
                FechaFinProgramada = rd["FechaFinProgramada"] == DBNull.Value ? null : Convert.ToDateTime(rd["FechaFinProgramada"]),
                HorasProgramadas = rd["HorasProgramadas"] == DBNull.Value ? null : Convert.ToDecimal(rd["HorasProgramadas"]),
                Cambio = rd["Cambio"] == DBNull.Value ? null : (TimeSpan)rd["Cambio"],
                Arranque = rd["Arranque"] == DBNull.Value ? null : (TimeSpan)rd["Arranque"],

                ObjetivoHora = rd["ObjetivoHora"] == DBNull.Value ? null : Convert.ToInt32(rd["ObjetivoHora"]),
                Ciclo = rd["Ciclo"] == DBNull.Value ? null : rd["Ciclo"].ToString(),
                Cavidades = rd["Cavidades"] == DBNull.Value ? null : Convert.ToInt32(rd["Cavidades"]),
                PesoBrutoPieza = rd["PesoBrutoPieza"] == DBNull.Value ? null : Convert.ToDecimal(rd["PesoBrutoPieza"]),

                MaterialID = rd["MaterialID"] == DBNull.Value ? null : Convert.ToInt32(rd["MaterialID"]),
                MaterialCodigo = rd["MaterialCodigo"] as string,
                MaterialDescripcion = rd["MaterialDescripcion"] as string,
                CantidadMpKg = rd["CantidadMpKg"] == DBNull.Value ? null : Convert.ToDecimal(rd["CantidadMpKg"]),

                EmbalajeCodigo = rd["EmbalajeCodigo"] as string,
                EmbalajeDescripcion = rd["EmbalajeDescripcion"] as string,
                PiezasPorEmbalaje = rd["PiezasPorEmbalaje"] == DBNull.Value ? null : Convert.ToDecimal(rd["PiezasPorEmbalaje"]),
                CantidadEmbalajes = rd["CantidadEmbalajes"] == DBNull.Value ? null : Convert.ToDecimal(rd["CantidadEmbalajes"]),

                Observaciones = rd["Observaciones"] as string,

                Color = rd["Color"] as string,

                PiezasPorCaja = rd["PiezasPorCaja"] == DBNull.Value
                    ? null
                    : Convert.ToInt32(rd["PiezasPorCaja"]),

                TipoSecado = rd["TipoSecado"] as string,

                HorasSecado = rd["HorasSecado"] == DBNull.Value
                    ? null
                    : Convert.ToDecimal(rd["HorasSecado"])
            };
        }





        private async Task<string> GenerarFolioOFAsync(SqlConnection cn, SqlTransaction tx)
        {
            var yy = DateTime.Today.ToString("yy");

            const string sql = @"
SELECT ISNULL(MAX(TRY_CONVERT(INT, SUBSTRING(NumeroOFRecibida, 4, 4))), 0) + 1
FROM dbo.SolicitudesProduccion
WHERE NumeroOFRecibida LIKE 'OF-[0-9][0-9][0-9][0-9]/' + @YY;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@YY", SqlDbType.VarChar, 2).Value = yy;

            var consecutivo = Convert.ToInt32(await cmd.ExecuteScalarAsync());

            return $"OF-{consecutivo:0000}/{yy}";
        }

        private async Task<int> InsertarOFDedeProgramaAsync(ProgramaParaOFVm p, string folioOF, int usuarioId, SqlConnection cn, SqlTransaction tx)
        {
            const string sql = @"
DECLARE @Ids TABLE
(
    SolicitudProduccionID INT NOT NULL
);

INSERT INTO dbo.SolicitudesProduccion
(
    FolioSolicitud,
    NumeroOFRecibida,
    FechaSolicitud,
    FechaRequerida,
    ClienteID,
    ClienteNombre,
    OrigenSolicitud,
    Prioridad,
    TipoOF,
    MotivoTipoOF,
    EstatusID,
    NotasGenerales,
    UsuarioCreacionID,
    FechaCreacion,
    Activo,
    FechaInicioPlaneada,
    FechaFinPlaneada,
    ResponsablePlaneacionUsuarioID,
    ResponsablePlaneacionNombre,
    CostoMPTotal,
    CostoEmbalajeTotal,
    CostoTotalOF,
    VentaTotalOF,
    UtilidadEstimadaOF,
    MonedaCosto,
    ReleaseID,
    ReleaseDetalleID,
    ProgramaProduccionID,
    OrigenOF
)
OUTPUT INSERTED.SolicitudProduccionID
INTO @Ids(SolicitudProduccionID)
VALUES
(
    @FolioSolicitud,
    @NumeroOFRecibida,
    GETDATE(),
    @FechaRequerida,
    @ClienteID,
    @ClienteNombre,
    @OrigenSolicitud,
    @Prioridad,
    @TipoOF,
    @MotivoTipoOF,
    @EstatusID,
    @NotasGenerales,
    @UsuarioCreacionID,
    GETDATE(),
    1,
    @FechaInicioPlaneada,
    @FechaFinPlaneada,
    @ResponsablePlaneacionUsuarioID,
    @ResponsablePlaneacionNombre,
    0,
    0,
    0,
    0,
    0,
    @MonedaCosto,
    @ReleaseID,
    @ReleaseDetalleID,
    @ProgramaProduccionID,
    @OrigenOF
);

SELECT TOP (1) SolicitudProduccionID
FROM @Ids;";
            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@FolioSolicitud", SqlDbType.NVarChar, 40).Value = folioOF;
            cmd.Parameters.Add("@NumeroOFRecibida", SqlDbType.NVarChar, 80).Value = folioOF;
            cmd.Parameters.Add("@FechaRequerida", SqlDbType.Date).Value = (object?)p.FechaFinProgramada?.Date ?? DBNull.Value;
            cmd.Parameters.Add("@ClienteID", SqlDbType.Int).Value = (object?)p.ClienteID ?? DBNull.Value;
            cmd.Parameters.Add("@ClienteNombre", SqlDbType.NVarChar, 200).Value = (object?)p.ClienteNombre ?? DBNull.Value;
            cmd.Parameters.Add("@OrigenSolicitud", SqlDbType.NVarChar, 50).Value = "Planeación Programa";
            cmd.Parameters.Add("@Prioridad", SqlDbType.NVarChar, 30).Value = "Normal";
            cmd.Parameters.Add("@TipoOF", SqlDbType.NVarChar, 30).Value = "RELEASE";
            cmd.Parameters.Add("@MotivoTipoOF", SqlDbType.NVarChar, 500).Value = DBNull.Value;
            cmd.Parameters.Add("@EstatusID", SqlDbType.Int).Value = PlaneacionOFEstatus.PendienteValidacionMP;
            cmd.Parameters.Add("@NotasGenerales", SqlDbType.NVarChar, 500).Value = (object?)$"OF generada desde Programa de Producción ID {p.ProgramaProduccionID}. {p.Observaciones}" ?? DBNull.Value;
            cmd.Parameters.Add("@UsuarioCreacionID", SqlDbType.Int).Value = usuarioId;
            cmd.Parameters.Add("@FechaInicioPlaneada", SqlDbType.DateTime).Value = (object?)p.FechaInicioProgramada ?? DBNull.Value;
            cmd.Parameters.Add("@FechaFinPlaneada", SqlDbType.DateTime).Value = (object?)p.FechaFinProgramada ?? DBNull.Value;
            cmd.Parameters.Add("@ResponsablePlaneacionUsuarioID", SqlDbType.Int).Value = usuarioId;
            cmd.Parameters.Add("@ResponsablePlaneacionNombre", SqlDbType.NVarChar, 200).Value = User?.Identity?.Name ?? "Sistema";
            cmd.Parameters.Add("@MonedaCosto", SqlDbType.NVarChar, 10).Value = "MXN";
            cmd.Parameters.Add("@ReleaseID", SqlDbType.Int).Value = (object?)p.ReleaseID ?? DBNull.Value;
            cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value = (object?)p.ReleaseDetalleID ?? DBNull.Value;
            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = p.ProgramaProduccionID;
            cmd.Parameters.Add("@OrigenOF", SqlDbType.NVarChar, 30).Value = "PROGRAMA";
            var resultado = await cmd.ExecuteScalarAsync();
            if (resultado == null || resultado == DBNull.Value) throw new InvalidOperationException("La OF fue insertada, pero no fue posible recuperar su identificador.");
            return Convert.ToInt32(resultado);
        }


        private async Task<int> InsertarDetalleOFDedeProgramaAsync(int solicitudProduccionId, ProgramaParaOFVm p, int usuarioId, SqlConnection cn, SqlTransaction tx)
        {
            const string sql = @"
DECLARE @Ids TABLE
(
    SolicitudProduccionDetalleID INT NOT NULL
);

INSERT INTO dbo.SolicitudesProduccionDetalle
(
    SolicitudProduccionID,
    Renglon,
    ParteID,
    MoldeID,
    MaquinaSugeridaID,
    DesignacionDescripcionSAP,
    ReferenciaSAP,
    CantidadPiezas,
    HorasPlaneadas,
    NumeroMoldeTexto,
    MaquinaSugeridaTexto,
    Color,
    Cavidades,
    ObjetivoHora,
    PiezasPorCaja,
    Notas,
    EstatusID,
    Activo,
    MaterialID,
    OrigenSurtido,
    PTDisponibleAlCrear,
    MPDisponibleKgAlCrear,
    AlmacenValidado,
    MensajeAlmacen,
    Ciclo,
    TipoSecado,
    HorasSecado,
    PesoBrutoPieza,
    MaterialCodigo,
    MaterialDescripcion,
    EmbalajeCodigo,
    EmbalajeDescripcion,
    PiezasPorEmbalaje,
    CantidadEmbalajes,
    CantidadMpKg,
    Cambio,
    Arranque,
    CostoMPUnitario,
    CostoMPTotal,
    MonedaCostoMP,
    UnidadCostoMP,
    CostoEmbalajeUnitario,
    CostoEmbalajeTotal,
    MonedaCostoEmbalaje,
    UnidadCostoEmbalaje,
    CostoTotalRenglon,
    PrecioVentaUnitario,
    VentaTotalRenglon,
    UtilidadEstimadaRenglon
)
OUTPUT INSERTED.SolicitudProduccionDetalleID
INTO @Ids(SolicitudProduccionDetalleID)
VALUES
(
    @SolicitudProduccionID,
    1,
    @ParteID,
    @MoldeID,
    @MaquinaSugeridaID,
    @DesignacionDescripcionSAP,
    @ReferenciaSAP,
    @CantidadPiezas,
    @HorasPlaneadas,
    @NumeroMoldeTexto,
    @MaquinaSugeridaTexto,
    @Color,
    @Cavidades,
    @ObjetivoHora,
    @PiezasPorCaja,
    @Notas,
    @EstatusID,
    1,
    @MaterialID,
    @OrigenSurtido,
    @PTDisponibleAlCrear,
    @MPDisponibleKgAlCrear,
    0,
    @MensajeAlmacen,
    @Ciclo,
    @TipoSecado,
    @HorasSecado,
    @PesoBrutoPieza,
    @MaterialCodigo,
    @MaterialDescripcion,
    @EmbalajeCodigo,
    @EmbalajeDescripcion,
    @PiezasPorEmbalaje,
    @CantidadEmbalajes,
    @CantidadMpKg,
    @Cambio,
    @Arranque,
    0,
    0,
    'MXN',
    NULL,
    0,
    0,
    'MXN',
    NULL,
    0,
    0,
    0,
    0
);

SELECT TOP (1) SolicitudProduccionDetalleID
FROM @Ids;";
            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = solicitudProduccionId;
            cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value = (object?)p.ParteID ?? DBNull.Value;
            cmd.Parameters.Add("@MoldeID", SqlDbType.Int).Value = (object?)p.MoldeID ?? DBNull.Value;
            cmd.Parameters.Add("@MaquinaSugeridaID", SqlDbType.Int).Value = (object?)p.MaquinaID ?? DBNull.Value;
            cmd.Parameters.Add("@DesignacionDescripcionSAP", SqlDbType.NVarChar, 300).Value = (object?)p.DesignacionDescripcionSAP ?? DBNull.Value;
            cmd.Parameters.Add("@ReferenciaSAP", SqlDbType.NVarChar, 150).Value = !string.IsNullOrWhiteSpace(p.ReferenciaSAP) ? (object)p.ReferenciaSAP : !string.IsNullOrWhiteSpace(p.NumeroParte) ? (object)p.NumeroParte : DBNull.Value;
            cmd.Parameters.Add("@CantidadPiezas", SqlDbType.Int).Value = p.CantidadProgramada;
            AddDecimal(cmd, "@HorasPlaneadas", p.HorasProgramadas, 18, 2);
            cmd.Parameters.Add("@NumeroMoldeTexto", SqlDbType.NVarChar, 100).Value = (object?)p.MoldeCodigo ?? DBNull.Value;
            cmd.Parameters.Add("@MaquinaSugeridaTexto", SqlDbType.NVarChar, 200).Value = (object?)($"{p.MaquinaCodigo} {p.MaquinaNombre}".Trim()) ?? DBNull.Value;
            cmd.Parameters.Add("@Cavidades", SqlDbType.Int).Value = (object?)p.Cavidades ?? DBNull.Value;
            cmd.Parameters.Add("@ObjetivoHora", SqlDbType.Int).Value = (object?)p.ObjetivoHora ?? DBNull.Value;
            cmd.Parameters.Add("@Notas", SqlDbType.NVarChar, 500).Value = (object?)$"Generado desde programa ID {p.ProgramaProduccionID}. Condición: {p.CondicionProduccion}. {p.Observaciones}" ?? DBNull.Value;
            cmd.Parameters.Add("@EstatusID", SqlDbType.Int).Value = PlaneacionOFEstatus.PendienteValidacionMP;
            cmd.Parameters.Add("@MaterialID", SqlDbType.Int).Value = (object?)p.MaterialID ?? DBNull.Value;
            cmd.Parameters.Add("@OrigenSurtido", SqlDbType.NVarChar, 30).Value = p.PiezasDesdePT > 0 ? "MIXTO" : "MP";
            cmd.Parameters.Add("@PTDisponibleAlCrear", SqlDbType.Int).Value = p.PiezasDesdePT;
            AddDecimal(cmd, "@MPDisponibleKgAlCrear", null, 18, 4);
            cmd.Parameters.Add("@MensajeAlmacen", SqlDbType.NVarChar, 500).Value = "OF generada desde programa. Validar surtido de MP/PT en almacén.";
            cmd.Parameters.Add("@Ciclo", SqlDbType.NVarChar, 50).Value = (object?)p.Ciclo ?? DBNull.Value;
            AddDecimal(cmd, "@PesoBrutoPieza", p.PesoBrutoPieza, 18, 6);
            cmd.Parameters.Add("@MaterialCodigo", SqlDbType.NVarChar, 100).Value = (object?)p.MaterialCodigo ?? DBNull.Value;
            cmd.Parameters.Add("@MaterialDescripcion", SqlDbType.NVarChar, 250).Value = (object?)p.MaterialDescripcion ?? DBNull.Value;
            cmd.Parameters.Add("@EmbalajeCodigo", SqlDbType.NVarChar, 100).Value = (object?)p.EmbalajeCodigo ?? DBNull.Value;
            cmd.Parameters.Add("@EmbalajeDescripcion", SqlDbType.NVarChar, 250).Value = (object?)p.EmbalajeDescripcion ?? DBNull.Value;
            cmd.Parameters.Add("@Color", SqlDbType.NVarChar, 100).Value = (object?)p.Color ?? DBNull.Value;
            cmd.Parameters.Add("@PiezasPorCaja", SqlDbType.Int).Value = (object?)p.PiezasPorCaja ?? DBNull.Value;
            cmd.Parameters.Add("@TipoSecado", SqlDbType.NVarChar, 100).Value = (object?)p.TipoSecado ?? DBNull.Value;
            AddDecimal(cmd, "@HorasSecado", p.HorasSecado, 18, 2);
            AddDecimal(cmd, "@PiezasPorEmbalaje", p.PiezasPorEmbalaje, 18, 4);
            AddDecimal(cmd, "@CantidadEmbalajes", p.CantidadEmbalajes, 18, 4);
            AddDecimal(cmd, "@CantidadMpKg", p.CantidadMpKg, 18, 4);
            cmd.Parameters.Add("@Cambio", SqlDbType.Time).Value = (object?)p.Cambio ?? DBNull.Value;
            cmd.Parameters.Add("@Arranque", SqlDbType.Time).Value = (object?)p.Arranque ?? DBNull.Value;
            var resultado = await cmd.ExecuteScalarAsync();
            if (resultado == null || resultado == DBNull.Value) throw new InvalidOperationException("El detalle de la OF fue insertado, pero no fue posible recuperar SolicitudProduccionDetalleID.");
            return Convert.ToInt32(resultado);
        }



        private sealed class CambioMoldeSugerencia
        {
            public DateTime Cambio { get; set; }
            public DateTime Arranque { get; set; }
            public bool OmiteHoraCambio { get; set; }
            public string Motivo { get; set; } = "Se considera 1 hora de preparación entre cambio y arranque.";
        }

        private async Task<CambioMoldeSugerencia> ObtenerSiguienteCambioDisponibleAsync(
      int maquinaId,
      DateTime fechaBase,
      int? parteId,
      int? moldeId,
      bool trabajarDomingo = false)
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            return await ObtenerSiguienteCambioDisponibleAsync(
                maquinaId,
                fechaBase,
                parteId,
                moldeId,
                cn,
                null,
                trabajarDomingo
            );
        }

        private static async Task<CambioMoldeSugerencia> ObtenerSiguienteCambioDisponibleAsync(
        int maquinaId,
        DateTime fechaBase,
        int? parteId,
        int? moldeId,
        SqlConnection cn,
        SqlTransaction? tx,
        bool trabajarDomingo = false)
        {
            var baseRedondeada = RedondearSiguienteBloque(fechaBase, 15);

            if (!EsInstanteOperativoPlaneacion(baseRedondeada, trabajarDomingo))
                baseRedondeada = SiguienteAperturaOperativaPlaneacion(baseRedondeada, trabajarDomingo);

            if (baseRedondeada < DateTime.Now)
                baseRedondeada = RedondearSiguienteBloque(DateTime.Now, 15);

            ProgramaColaPlaneacion? ultimoMaquina = null;
            DateTime? finColaMaquina = null;

            const string sqlUltimoMaquina = @"
SELECT TOP (1)
    pp.ProgramaProduccionID,
    pp.ParteID,
    pp.NumeroParte,
    pp.ReferenciaSAP,
    pp.MoldeID,
    pp.MoldeCodigo,
    ISNULL(
        pp.FechaFinProgramada,
        DATEADD(MINUTE, CAST(CEILING(ISNULL(pp.HorasProgramadas, 1) * 60) AS INT), pp.FechaInicioProgramada)
    ) AS FechaFinProgramada
FROM dbo.Planeacion_ProgramaProduccion pp WITH (UPDLOCK, HOLDLOCK)
WHERE pp.Activo = 1
  AND pp.MaquinaID = @MaquinaID
  AND ISNULL(pp.EstatusID, 1) NOT IN (5, 9, 99)
  AND pp.FechaInicioProgramada IS NOT NULL
ORDER BY
    ISNULL(
        pp.FechaFinProgramada,
        DATEADD(MINUTE, CAST(CEILING(ISNULL(pp.HorasProgramadas, 1) * 60) AS INT), pp.FechaInicioProgramada)
    ) DESC,
    pp.ProgramaProduccionID DESC;";

            await using (var cmd = new SqlCommand(sqlUltimoMaquina, cn, tx))
            {
                cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = maquinaId;

                await using var rd = await cmd.ExecuteReaderAsync();

                if (await rd.ReadAsync())
                {
                    ultimoMaquina = new ProgramaColaPlaneacion
                    {
                        ProgramaProduccionID = Convert.ToInt32(rd["ProgramaProduccionID"]),
                        ParteID = rd["ParteID"] == DBNull.Value ? null : Convert.ToInt32(rd["ParteID"]),
                        MoldeID = rd["MoldeID"] == DBNull.Value ? null : Convert.ToInt32(rd["MoldeID"]),
                        ParteTexto = (rd["ReferenciaSAP"] as string) ?? (rd["NumeroParte"] as string) ?? "la pieza anterior",
                        MoldeTexto = (rd["MoldeCodigo"] as string) ?? "el molde anterior",
                        Fin = Convert.ToDateTime(rd["FechaFinProgramada"])
                    };

                    finColaMaquina = ultimoMaquina.Fin;
                }
            }

            var cursor = baseRedondeada;

            if (finColaMaquina.HasValue && finColaMaquina.Value > cursor)
                cursor = finColaMaquina.Value;

            cursor = RedondearSiguienteBloque(cursor, 15);

            if (!EsInstanteOperativoPlaneacion(cursor, trabajarDomingo))
                cursor = SiguienteAperturaOperativaPlaneacion(cursor, trabajarDomingo);

            var mismaParte =
                ultimoMaquina != null &&
                parteId.HasValue &&
                ultimoMaquina.ParteID.HasValue &&
                parteId.Value == ultimoMaquina.ParteID.Value;

            var mismoMolde =
                ultimoMaquina != null &&
                moldeId.HasValue &&
                ultimoMaquina.MoldeID.HasValue &&
                moldeId.Value == ultimoMaquina.MoldeID.Value;

            var motivoBase = new List<string>();

            if (finColaMaquina.HasValue && finColaMaquina.Value > baseRedondeada)
            {
                motivoBase.Add(
                    $"La máquina tiene cola activa. Se colocó después del último programa de la máquina ({finColaMaquina.Value:dd/MM/yyyy HH:mm})."
                );
            }
            else
            {
                motivoBase.Add("La máquina no tenía cola posterior; se colocó en el primer punto operativo disponible.");
            }

            DateTime? moldeLiberado = null;
            string maquinaMolde = string.Empty;
            string parteMolde = string.Empty;

            for (var intento = 0; intento < 500; intento++)
            {
                cursor = RedondearSiguienteBloque(cursor, 15);

                if (!EsInstanteOperativoPlaneacion(cursor, trabajarDomingo))
                    cursor = SiguienteAperturaOperativaPlaneacion(cursor, trabajarDomingo);

                var omiteCambio =
                    !moldeLiberado.HasValue &&
                    (mismaParte || mismoMolde);

                var horasCambio = omiteCambio ? 0m : 1m;

                var arranque = horasCambio <= 0
                    ? cursor
                    : SumarHorasOperativasPlaneacion(cursor, horasCambio, trabajarDomingo);

                var finEstimado = SumarHorasOperativasPlaneacion(
                    arranque,
                    0.25m,
                    trabajarDomingo
                );

                if (moldeId.HasValue)
                {
                    var finCruceMolde = await ObtenerFinCruceMoldePlaneacionAsync(
                        moldeId.Value,
                        cursor,
                        arranque > cursor ? arranque : finEstimado,
                        cn,
                        tx
                    );

                    if (finCruceMolde.HasValue && finCruceMolde.Value > cursor)
                    {
                        moldeLiberado = finCruceMolde.Value;
                        cursor = finCruceMolde.Value;

                        await ObtenerDetalleUltimoUsoMoldeAsync(
                            moldeId.Value,
                            cn,
                            tx,
                            detalle =>
                            {
                                maquinaMolde = detalle.MaquinaCodigo;
                                parteMolde = detalle.Parte;
                            }
                        );

                        continue;
                    }
                }

                if (horasCambio > 0)
                {
                    var finCruceCambio = await ObtenerFinCruceCambioMoldeAsync(
                        cursor,
                        arranque,
                        cn,
                        tx
                    );

                    if (finCruceCambio.HasValue && finCruceCambio.Value > cursor)
                    {
                        cursor = finCruceCambio.Value;
                        continue;
                    }
                }

                var motivo = string.Join(" ", motivoBase);

                if (moldeLiberado.HasValue)
                {
                    motivo +=
                        $" El molde estaba ocupado" +
                        (string.IsNullOrWhiteSpace(maquinaMolde) ? "" : $" en {maquinaMolde}") +
                        (string.IsNullOrWhiteSpace(parteMolde) ? "" : $" para {parteMolde}") +
                        $"; queda libre el {moldeLiberado.Value:dd/MM/yyyy HH:mm}.";
                }

                if (omiteCambio && mismaParte)
                {
                    motivo += $" La máquina continúa con la misma pieza ({ultimoMaquina!.ParteTexto}); se omite la hora de cambio.";

                    return new CambioMoldeSugerencia
                    {
                        Cambio = cursor,
                        Arranque = arranque,
                        OmiteHoraCambio = true,
                        Motivo = motivo
                    };
                }

                if (omiteCambio && mismoMolde)
                {
                    motivo += $" La máquina conserva el mismo molde ({ultimoMaquina!.MoldeTexto}); se omite la hora de cambio.";

                    return new CambioMoldeSugerencia
                    {
                        Cambio = cursor,
                        Arranque = arranque,
                        OmiteHoraCambio = true,
                        Motivo = motivo
                    };
                }

                motivo += " Se considera 1 hora de cambio/preparación antes del arranque.";

                return new CambioMoldeSugerencia
                {
                    Cambio = cursor,
                    Arranque = arranque,
                    OmiteHoraCambio = false,
                    Motivo = motivo
                };
            }

            throw new InvalidOperationException(
                "No fue posible encontrar una posición válida en la cola de la máquina. Revisa la cola, el molde y los cambios de molde programados."
            );
        }

        private static async Task<DateTime?> ObtenerFinCruceMoldePlaneacionAsync(
    int moldeId,
    DateTime inicio,
    DateTime fin,
    SqlConnection cn,
    SqlTransaction? tx)
        {
            const string sql = @"
SELECT TOP (1)
    ISNULL(
        pp.FechaFinProgramada,
        DATEADD(MINUTE, CAST(CEILING(ISNULL(pp.HorasProgramadas, 1) * 60) AS INT), pp.FechaInicioProgramada)
    ) AS FechaFinProgramada
FROM dbo.Planeacion_ProgramaProduccion pp WITH (UPDLOCK, HOLDLOCK)
WHERE pp.Activo = 1
  AND pp.MoldeID = @MoldeID
  AND ISNULL(pp.EstatusID, 1) NOT IN (5, 9, 99)
  AND pp.FechaInicioProgramada IS NOT NULL
  AND pp.FechaInicioProgramada < @Fin
  AND ISNULL(
        pp.FechaFinProgramada,
        DATEADD(MINUTE, CAST(CEILING(ISNULL(pp.HorasProgramadas, 1) * 60) AS INT), pp.FechaInicioProgramada)
      ) > @Inicio
ORDER BY
    ISNULL(
        pp.FechaFinProgramada,
        DATEADD(MINUTE, CAST(CEILING(ISNULL(pp.HorasProgramadas, 1) * 60) AS INT), pp.FechaInicioProgramada)
    ) DESC;";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@MoldeID", SqlDbType.Int).Value = moldeId;
            cmd.Parameters.Add("@Inicio", SqlDbType.DateTime).Value = inicio;
            cmd.Parameters.Add("@Fin", SqlDbType.DateTime).Value = fin;

            var result = await cmd.ExecuteScalarAsync();

            return result == null || result == DBNull.Value
                ? null
                : Convert.ToDateTime(result);
        }

        private static async Task<DateTime?> ObtenerFinCruceCambioMoldeAsync(
    DateTime inicioCambio,
    DateTime finCambio,
    SqlConnection cn,
    SqlTransaction? tx)
        {
            if (finCambio <= inicioCambio)
                return null;

            const string sql = @"
;WITH Cambios AS
(
    SELECT
        pp.ProgramaProduccionID,
        pp.MaquinaCodigo,
        pp.ReferenciaSAP,
        pp.NumeroParte,
        pp.FechaInicioProgramada AS InicioCambio,
        CASE
            WHEN DATEADD(
                    SECOND,
                    DATEDIFF(SECOND, CAST('00:00:00' AS time), pp.Arranque),
                    CAST(CONVERT(date, pp.FechaInicioProgramada) AS datetime)
                 ) <= pp.FechaInicioProgramada
            THEN DATEADD(
                    DAY,
                    1,
                    DATEADD(
                        SECOND,
                        DATEDIFF(SECOND, CAST('00:00:00' AS time), pp.Arranque),
                        CAST(CONVERT(date, pp.FechaInicioProgramada) AS datetime)
                    )
                 )
            ELSE DATEADD(
                    SECOND,
                    DATEDIFF(SECOND, CAST('00:00:00' AS time), pp.Arranque),
                    CAST(CONVERT(date, pp.FechaInicioProgramada) AS datetime)
                 )
        END AS FinCambio
    FROM dbo.Planeacion_ProgramaProduccion pp WITH (UPDLOCK, HOLDLOCK)
    WHERE pp.Activo = 1
      AND ISNULL(pp.EstatusID, 1) NOT IN (5, 9, 99)
      AND pp.FechaInicioProgramada IS NOT NULL
      AND pp.Cambio IS NOT NULL
      AND pp.Arranque IS NOT NULL
      AND pp.Cambio <> pp.Arranque
)
SELECT TOP (1)
    FinCambio
FROM Cambios
WHERE InicioCambio < @FinCambio
  AND FinCambio > @InicioCambio
ORDER BY
    FinCambio DESC;";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@InicioCambio", SqlDbType.DateTime).Value = inicioCambio;
            cmd.Parameters.Add("@FinCambio", SqlDbType.DateTime).Value = finCambio;

            var result = await cmd.ExecuteScalarAsync();

            return result == null || result == DBNull.Value
                ? null
                : Convert.ToDateTime(result);
        }

        private static async Task ObtenerDetalleUltimoUsoMoldeAsync(
    int moldeId,
    SqlConnection cn,
    SqlTransaction? tx,
    Action<(string MaquinaCodigo, string Parte)> asignar)
        {
            const string sql = @"
SELECT TOP (1)
    ISNULL(pp.MaquinaCodigo, N'otra máquina') AS MaquinaCodigo,
    ISNULL(NULLIF(pp.ReferenciaSAP, N''), ISNULL(NULLIF(pp.NumeroParte, N''), N'otro programa')) AS Parte
FROM dbo.Planeacion_ProgramaProduccion pp
WHERE pp.Activo = 1
  AND pp.MoldeID = @MoldeID
  AND ISNULL(pp.EstatusID, 1) NOT IN (5, 9, 99)
  AND pp.FechaInicioProgramada IS NOT NULL
ORDER BY
    ISNULL(
        pp.FechaFinProgramada,
        DATEADD(MINUTE, CAST(CEILING(ISNULL(pp.HorasProgramadas, 1) * 60) AS INT), pp.FechaInicioProgramada)
    ) DESC,
    pp.ProgramaProduccionID DESC;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@MoldeID", SqlDbType.Int).Value = moldeId;

            await using var rd = await cmd.ExecuteReaderAsync();

            if (await rd.ReadAsync())
            {
                asignar((
                    rd["MaquinaCodigo"] as string ?? "otra máquina",
                    rd["Parte"] as string ?? "otro programa"
                ));
            }
        }

        private static async Task<bool> MaquinaCompatibleConParteAsync(
    int? parteId,
    int maquinaId,
    SqlConnection cn,
    SqlTransaction tx)
        {
            if (!parteId.HasValue || parteId.Value <= 0)
                return true;

            const string sql = @"
;WITH DatosTecnicos AS
(
    SELECT TOP (1)
        t.MaquinaPrincipalID,
        t.MaquinaSustitutaID
    FROM dbo.ERP_ParteDatosTecnicos t
    WHERE t.ParteID = @ParteID
      AND t.Activo = 1
),
Compatibles AS
(
    SELECT
        dt.MaquinaPrincipalID AS MaquinaID
    FROM DatosTecnicos dt
    WHERE dt.MaquinaPrincipalID IS NOT NULL

    UNION

    SELECT
        dt.MaquinaSustitutaID AS MaquinaID
    FROM DatosTecnicos dt
    WHERE dt.MaquinaSustitutaID IS NOT NULL

    UNION

    SELECT
        ms.MaquinaSustitutaID AS MaquinaID
    FROM DatosTecnicos dt
    INNER JOIN dbo.ERP_MaquinasSustitutas ms
        ON ms.MaquinaPrincipalID = dt.MaquinaPrincipalID
       AND ms.Activo = 1
    WHERE dt.MaquinaPrincipalID IS NOT NULL

    UNION

    SELECT
        ms.MaquinaPrincipalID AS MaquinaID
    FROM DatosTecnicos dt
    INNER JOIN dbo.ERP_MaquinasSustitutas ms
        ON ms.MaquinaSustitutaID = dt.MaquinaPrincipalID
       AND ms.Activo = 1
    WHERE dt.MaquinaPrincipalID IS NOT NULL
)
SELECT COUNT(1)
FROM Compatibles
WHERE MaquinaID = @MaquinaID;";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value = parteId.Value;
            cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = maquinaId;

            return Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
        }



        private sealed class ProgramaColaPlaneacion
        {
            public int ProgramaProduccionID { get; set; }
            public int? ParteID { get; set; }
            public string ParteTexto { get; set; } = "la pieza anterior";
            public int? MoldeID { get; set; }
            public string MoldeTexto { get; set; } = "el molde anterior";
            public DateTime Fin { get; set; }
        }

        private static string NormalizarTipoOF(string? tipoOF)
        {
            if (string.IsNullOrWhiteSpace(tipoOF))
                return "RELEASE";

            var valor = tipoOF.Trim().ToUpperInvariant();

            valor = valor
                .Replace("Á", "A")
                .Replace("É", "E")
                .Replace("Í", "I")
                .Replace("Ó", "O")
                .Replace("Ú", "U");

            return valor switch
            {
                "RELEASE" => "RELEASE",
                "ENSAMBLE" => "ENSAMBLE",
                "PRUEBA" => "PRUEBA",
                "MP EXTRA" => "MP EXTRA",
                "MPEXTRA" => "MP EXTRA",
                "MP_EXTRA" => "MP EXTRA",
                _ => "RELEASE"
            };
        }

        private static bool TipoOFRequiereMotivo(string? tipoOF)
        {
            var valor = NormalizarTipoOF(tipoOF);

            return valor == "PRUEBA" ||
                   valor == "MP EXTRA";
        }

        private void ValidarTipoOF(string? tipoOF, string? motivoTipoOF, string campoMotivo)
        {
            var valor = NormalizarTipoOF(tipoOF);

            if (valor != "RELEASE" &&
                valor != "ENSAMBLE" &&
                valor != "PRUEBA" &&
                valor != "MP EXTRA")
            {
                ModelState.AddModelError(nameof(PlaneacionProgramaCrearDesdeNecesidadVm.TipoOF), "Selecciona un tipo de OF válido.");
            }

            if (TipoOFRequiereMotivo(valor) &&
                string.IsNullOrWhiteSpace(motivoTipoOF))
            {
                ModelState.AddModelError(campoMotivo, "Captura el motivo para este tipo de OF.");
            }
        }



        private static DateTime RedondearSiguienteHora(DateTime fecha)
        {
            var redondeada = new DateTime(
                fecha.Year,
                fecha.Month,
                fecha.Day,
                fecha.Hour,
                0,
                0
            );

            if (fecha.Minute == 0 &&
                fecha.Second == 0 &&
                fecha.Millisecond == 0)
            {
                return redondeada;
            }

            return redondeada.AddHours(1);
        }

        private static DateTime RedondearSiguienteBloque(DateTime fecha, int minutosBloque)
        {

            // hora completa, sin minutos.
            return RedondearSiguienteHora(fecha);
        }

        private static bool EsHoraCompleta(TimeSpan hora)
        {
            return hora.Minutes == 0 &&
                   hora.Seconds == 0 &&
                   hora.Milliseconds == 0;
        }

        private static DateTime CalcularFechaHoraDesdeHora(DateTime fechaBase, TimeSpan? hora)
        {
            if (!hora.HasValue)
                return fechaBase;

            return fechaBase.Date.Add(hora.Value);
        }


        private async Task InsertarAsignacionMaquinaOFDedeProgramaAsync(
    int solicitudProduccionDetalleId,
    ProgramaParaOFVm p,
    int usuarioId,
    SqlConnection cn,
    SqlTransaction tx)
        {
            if (!p.MaquinaID.HasValue)
                return;

            const string sql = @"
INSERT INTO dbo.SolicitudesProduccionAsignacionMaquina
(
    SolicitudProduccionDetalleID,
    MaquinaID,
    MoldeID,
    CantidadAsignada,
    HorasEstimadas,
    Secuencia,
    CondicionProduccion,
    FechaProgramadaTentativa,
    HoraInicioTentativa,
    HoraFinTentativa,
    EstatusID,
    Observaciones,
    UsuarioCreacionID,
    FechaCreacion,
    Activo
)
VALUES
(
    @SolicitudProduccionDetalleID,
    @MaquinaID,
    @MoldeID,
    @CantidadAsignada,
    @HorasEstimadas,
    @Secuencia,
    @CondicionProduccion,
    @FechaProgramadaTentativa,
    @HoraInicioTentativa,
    @HoraFinTentativa,
    @EstatusID,
    @Observaciones,
    @UsuarioCreacionID,
    GETDATE(),
    1
);";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@SolicitudProduccionDetalleID", SqlDbType.Int).Value = solicitudProduccionDetalleId;
            cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = p.MaquinaID.Value;

            cmd.Parameters.Add("@MoldeID", SqlDbType.Int).Value =
                (object?)p.MoldeID ?? DBNull.Value;

            cmd.Parameters.Add("@CantidadAsignada", SqlDbType.Int).Value = p.CantidadProgramada;

            AddDecimal(cmd, "@HorasEstimadas", p.HorasProgramadas, 18, 2);

            cmd.Parameters.Add("@Secuencia", SqlDbType.Int).Value =
                (object?)p.SecuenciaMaquina ?? 1;

            cmd.Parameters.Add("@CondicionProduccion", SqlDbType.NVarChar, 20).Value =
                (object?)p.CondicionProduccion ?? DBNull.Value;

            cmd.Parameters.Add("@FechaProgramadaTentativa", SqlDbType.Date).Value =
                (object?)p.FechaInicioProgramada?.Date ?? DBNull.Value;

            cmd.Parameters.Add("@HoraInicioTentativa", SqlDbType.Time).Value =
                (object?)p.FechaInicioProgramada?.TimeOfDay ?? DBNull.Value;

            cmd.Parameters.Add("@HoraFinTentativa", SqlDbType.Time).Value =
                (object?)p.FechaFinProgramada?.TimeOfDay ?? DBNull.Value;

            cmd.Parameters.Add("@EstatusID", SqlDbType.Int).Value = PlaneacionOFEstatus.PendienteValidacionMP;

            cmd.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 500).Value =
                (object?)$"Asignación generada desde programa ID {p.ProgramaProduccionID}." ?? DBNull.Value;

            cmd.Parameters.Add("@UsuarioCreacionID", SqlDbType.Int).Value = usuarioId;

            await cmd.ExecuteNonQueryAsync();
        }

        [HttpGet]
        public async Task<IActionResult> OperadoresPorMaquinaFecha(
    int maquinaId,
    DateTime fechaHora)
        {
            if (maquinaId <= 0 || fechaHora == default)
            {
                return Json(new
                {
                    ok = false,
                    permiteProgramar = true,
                    mensaje = "Selecciona máquina y fecha/hora de cambio."
                });
            }

            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            var operadores = await ObtenerOperadoresEscalaPorMaquinaFechaAsync(
                maquinaId,
                fechaHora,
                cn
            );

            if (!operadores.Any())
            {
                return Json(new
                {
                    ok = false,
                    permiteProgramar = true,
                    operadorPrincipalID = (int?)null,
                    operadorPrincipalNombre = (string?)null,
                    operadorAuxiliarID = (int?)null,
                    operadorAuxiliarNombre = (string?)null,
                    turnoNombre = (string?)null,
                    turnoColor = (string?)null,
                    escalaAsignacionID = (int?)null,
                    mensaje =
                        "No hay operador asignado en RRHH para esta máquina y horario. " +
                        "Puedes programar, pero quedará pendiente la asignación del operador."
                });
            }

            var principal = operadores[0];
            var auxiliar = operadores.Skip(1).FirstOrDefault();

            return Json(new
            {
                ok = true,
                permiteProgramar = true,

                operadorPrincipalID = principal.PersonaID,
                operadorPrincipalNombre = principal.NombreCompleto,

                operadorAuxiliarID = auxiliar?.PersonaID,
                operadorAuxiliarNombre = auxiliar?.NombreCompleto,

                turnoNombre = principal.TurnoNombre,
                turnoColor = principal.TurnoColor,
                escalaAsignacionID = principal.EscalaAsignacionID,

                mensaje =
                    $"Operador: {principal.NombreCompleto}" +
                    (auxiliar != null ? $" | Auxiliar: {auxiliar.NombreCompleto}" : "") +
                    $" | Turno: {principal.TurnoNombre}"
            });
        }

        private async Task MarcarProgramaConOFAsync(int programaProduccionId, int solicitudProduccionId, int solicitudProduccionDetalleId, int usuarioId, SqlConnection cn, SqlTransaction tx)
        {
            const string sql = @"
UPDATE dbo.Planeacion_ProgramaProduccion
SET SolicitudProduccionID=@SolicitudProduccionID,SolicitudProduccionDetalleID=@SolicitudProduccionDetalleID,FechaGeneracionOF=GETDATE(),
UsuarioGeneroOFID=@UsuarioGeneroOFID,UsuarioModificacionID=@UsuarioGeneroOFID,FechaModificacion=GETDATE()
WHERE ProgramaProduccionID=@ProgramaProduccionID;
UPDATE dbo.Planeacion_ProductoIncompletoApartado
SET SolicitudProduccionID=@SolicitudProduccionID,SolicitudProduccionDetalleID=@SolicitudProduccionDetalleID,EstatusID=3,
Observaciones=LEFT(COALESCE(NULLIF(Observaciones,N'')+N' | ',N'')+N'Asignada a OF '+CONVERT(NVARCHAR(20),@SolicitudProduccionID)+N'.',500)
WHERE ProgramaProduccionID=@ProgramaProduccionID AND Activo=1 AND EstatusID=2;
UPDATE c SET c.SolicitudReservaID=@SolicitudProduccionID,c.SolicitudDetalleReservaID=@SolicitudProduccionDetalleID,
c.UsuarioModificacionID=@UsuarioGeneroOFID,c.FechaModificacion=SYSDATETIME()
FROM dbo.Produccion_Cajas c
INNER JOIN dbo.Planeacion_ProductoIncompletoApartado a ON a.CajaProduccionID=c.CajaProduccionID
WHERE a.ProgramaProduccionID=@ProgramaProduccionID AND a.Activo=1 AND a.EstatusID=3 AND c.Activo=1;";
            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = solicitudProduccionId;
            cmd.Parameters.Add("@SolicitudProduccionDetalleID", SqlDbType.Int).Value = solicitudProduccionDetalleId;
            cmd.Parameters.Add("@UsuarioGeneroOFID", SqlDbType.Int).Value = usuarioId;
            cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaProduccionId;
            await cmd.ExecuteNonQueryAsync();
        }

        private async Task MarcarReleaseDetalleConOFAsync(
    int releaseDetalleId,
    int solicitudProduccionId,
    SqlConnection cn,
    SqlTransaction tx)
        {
            const string sql = @"
UPDATE dbo.Planeacion_ReleaseDetalle
SET
    SolicitudProduccionID = @SolicitudProduccionID,
    EstatusID = 4,
    FechaModificacion = GETDATE()
WHERE ReleaseDetalleID = @ReleaseDetalleID;";

            await using var cmd = new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@SolicitudProduccionID", SqlDbType.Int).Value = solicitudProduccionId;
            cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value = releaseDetalleId;

            await cmd.ExecuteNonQueryAsync();
        }



        // CALENDARIO_MAQUINAS_HELPERS_V1_0
        private static bool EsInstanteOperativoCalendario(DateTime fecha)
        {
            return fecha.DayOfWeek switch
            {
                DayOfWeek.Monday => fecha.TimeOfDay >= TimeSpan.FromHours(7),
                DayOfWeek.Tuesday => true,
                DayOfWeek.Wednesday => true,
                DayOfWeek.Thursday => true,
                DayOfWeek.Friday => true,
                DayOfWeek.Saturday => fecha.TimeOfDay < TimeSpan.FromHours(15),
                _ => false
            };
        }

        private static DateTime SiguienteAperturaCalendario(DateTime fecha)
        {
            var value = fecha;

            if (value.DayOfWeek == DayOfWeek.Monday &&
                value.TimeOfDay < TimeSpan.FromHours(7))
            {
                return value.Date.AddHours(7);
            }

            if (value.DayOfWeek == DayOfWeek.Saturday &&
                value.TimeOfDay >= TimeSpan.FromHours(15))
            {
                return value.Date.AddDays(2).AddHours(7);
            }

            if (value.DayOfWeek == DayOfWeek.Sunday)
            {
                return value.Date.AddDays(1).AddHours(7);
            }

            return value;
        }

        private static DateTime FinVentanaOperativaCalendario(DateTime fecha)
        {
            return fecha.DayOfWeek switch
            {
                DayOfWeek.Monday => fecha.Date.AddDays(1),
                DayOfWeek.Tuesday => fecha.Date.AddDays(1),
                DayOfWeek.Wednesday => fecha.Date.AddDays(1),
                DayOfWeek.Thursday => fecha.Date.AddDays(1),
                DayOfWeek.Friday => fecha.Date.AddDays(1),
                DayOfWeek.Saturday => fecha.Date.AddHours(15),
                _ => SiguienteAperturaCalendario(fecha)
            };
        }

        private static DateTime SumarHorasOperativasCalendario(
            DateTime inicio,
            decimal horas)
        {
            if (horas <= 0)
                return inicio;

            var cursor = SiguienteAperturaCalendario(inicio);
            var restantes = horas;
            var guard = 0;

            while (restantes > 0.0001m)
            {
                guard++;
                if (guard > 1000)
                    throw new InvalidOperationException("No fue posible calcular el fin operativo del programa.");

                cursor = SiguienteAperturaCalendario(cursor);
                var finVentana = FinVentanaOperativaCalendario(cursor);
                var disponibles = (decimal)(finVentana - cursor).TotalHours;

                if (disponibles <= 0)
                {
                    cursor = SiguienteAperturaCalendario(finVentana.AddMinutes(1));
                    continue;
                }

                if (restantes <= disponibles)
                    return cursor.AddHours((double)restantes);

                restantes -= disponibles;
                cursor = SiguienteAperturaCalendario(finVentana);
            }

            return cursor;
        }

        private static async Task<List<OperadorEscalaProgramaVm>> ObtenerOperadoresEscalaPorMaquinaFechaAsync(
    int maquinaId,
    DateTime fechaHora,
    SqlConnection cn,
    SqlTransaction? tx = null)
        {
            var operadores = new List<OperadorEscalaProgramaVm>();

            const string sql = @"
SELECT TOP (2)
    a.AsignacionID AS EscalaAsignacionID,
    a.PersonalID AS PersonaID,

    LTRIM(RTRIM(
        ISNULL(p.Nombre, '') + ' ' +
        ISNULL(p.ApellidoPaterno, '') + ' ' +
        ISNULL(p.ApellidoMaterno, '')
    )) AS NombreCompleto,

    et.EscalaTurnoID,
    et.Nombre AS TurnoNombre,
    et.Color AS TurnoColor,

    a.MaquinaID

FROM dbo.RRHH_EscalaAsignaciones a

INNER JOIN dbo.RRHH_EscalasPersonal esc
    ON esc.EscalaID = a.EscalaID
   AND esc.Activo = 1
   AND esc.Estado = N'Publicada'

INNER JOIN dbo.Persona p
    ON p.PersonaID = a.PersonalID

INNER JOIN dbo.RRHH_EscalaTurnos et
    ON et.EscalaID = a.EscalaID
   AND et.EscalaTurnoID = a.EscalaTurnoID

WHERE a.Activo = 1
  AND a.MaquinaID = @MaquinaID

  AND CAST(@FechaHora AS date) >= CAST(a.FechaInicio AS date)
  AND CAST(@FechaHora AS date) <= CAST(a.FechaFin AS date)

  AND ISNULL(p.EsColaboradorActivo, 1) = 1

  AND
  (
        ISNULL(et.EsFlexible, 0) = 1
     OR et.HoraInicio IS NULL
     OR et.HoraFin IS NULL

     OR
     (
            ISNULL(et.CruzaDiaSiguiente, 0) = 0
        AND CAST(@FechaHora AS time) >= et.HoraInicio
        AND CAST(@FechaHora AS time) < et.HoraFin
     )

     OR
     (
            ISNULL(et.CruzaDiaSiguiente, 0) = 1
        AND
        (
               CAST(@FechaHora AS time) >= et.HoraInicio
            OR CAST(@FechaHora AS time) < et.HoraFin
        )
     )
  )

  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.RRHH_NovedadesPersonal n
      WHERE n.EscalaID = a.EscalaID
        AND n.PersonalID = a.PersonalID
        AND n.Activo = 1
        AND n.TipoNovedad IN (N'Baja', N'Incapacidad', N'Vacaciones')
        AND CAST(@FechaHora AS date) >= CAST(n.FechaInicio AS date)
        AND CAST(@FechaHora AS date) <= CAST(ISNULL(n.FechaFin, n.FechaInicio) AS date)
  )

ORDER BY
    et.Orden,
    a.AsignacionID DESC;";

            await using var cmd = tx == null
                ? new SqlCommand(sql, cn)
                : new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = maquinaId;
            cmd.Parameters.Add("@FechaHora", SqlDbType.DateTime).Value = fechaHora;

            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                operadores.Add(new OperadorEscalaProgramaVm
                {
                    EscalaAsignacionID = Convert.ToInt32(rd["EscalaAsignacionID"]),
                    PersonaID = Convert.ToInt32(rd["PersonaID"]),

                    NombreCompleto =
                        rd["NombreCompleto"] == DBNull.Value
                            ? string.Empty
                            : rd["NombreCompleto"].ToString() ?? string.Empty,

                    EscalaTurnoID = Convert.ToInt32(rd["EscalaTurnoID"]),

                    TurnoNombre =
                        rd["TurnoNombre"] == DBNull.Value
                            ? "Turno sin nombre"
                            : rd["TurnoNombre"].ToString() ?? "Turno sin nombre",

                    TurnoColor =
                        rd["TurnoColor"] == DBNull.Value
                            ? null
                            : rd["TurnoColor"].ToString(),

                    MaquinaID = Convert.ToInt32(rd["MaquinaID"])
                });
            }

            return operadores;
        }
        private static decimal CalcularHorasOperativasCalendario(
            DateTime inicio,
            DateTime fin)
        {
            if (fin <= inicio)
                return 0;

            decimal total = 0;
            var date = inicio.Date;
            var lastDate = fin.Date;

            while (date <= lastDate)
            {
                DateTime? apertura = date.DayOfWeek switch
                {
                    DayOfWeek.Monday => date.AddHours(7),
                    DayOfWeek.Tuesday => date,
                    DayOfWeek.Wednesday => date,
                    DayOfWeek.Thursday => date,
                    DayOfWeek.Friday => date,
                    DayOfWeek.Saturday => date,
                    _ => null
                };

                DateTime? cierre = date.DayOfWeek switch
                {
                    DayOfWeek.Monday => date.AddDays(1),
                    DayOfWeek.Tuesday => date.AddDays(1),
                    DayOfWeek.Wednesday => date.AddDays(1),
                    DayOfWeek.Thursday => date.AddDays(1),
                    DayOfWeek.Friday => date.AddDays(1),
                    DayOfWeek.Saturday => date.AddHours(15),
                    _ => null
                };

                if (apertura.HasValue && cierre.HasValue)
                {
                    var desde = inicio > apertura.Value ? inicio : apertura.Value;
                    var hasta = fin < cierre.Value ? fin : cierre.Value;

                    if (hasta > desde)
                        total += (decimal)(hasta - desde).TotalHours;
                }

                date = date.AddDays(1);
            }

            return Math.Round(total, 4);
        }


        // NSQ_PLANEACION_RESERVAS_SYNC_HELPER_V1
        private async Task SincronizarReservasAlmacenPlaneacionAsync(SqlConnection cn)
        {
            const string sql = @"
IF OBJECT_ID(N'dbo.sp_Almacen_SincronizarReservas', N'P') IS NOT NULL
BEGIN
    EXEC dbo.sp_Almacen_SincronizarReservas @Usuario = @Usuario;
END;";

            await using var cmd = new SqlCommand(sql, cn);
            var usuario =
                HttpContext.Session.GetString("Username")
                ?? User?.Identity?.Name
                ?? "PLANEACION";
            cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 120).Value = usuario;
            await cmd.ExecuteNonQueryAsync();
        }
        private sealed class OperadorEscalaProgramaVm
        {
            public int EscalaAsignacionID { get; set; }
            public int PersonaID { get; set; }
            public string NombreCompleto { get; set; } = string.Empty;

            public int EscalaTurnoID { get; set; }
            public string TurnoNombre { get; set; } = string.Empty;
            public string? TurnoColor { get; set; }

            public int MaquinaID { get; set; }
        }

        public sealed class CalendarioMaquinasMoverRequest
        {
            public int ProgramaProduccionID { get; set; }
            public int MaquinaID { get; set; }
            public DateTime Inicio { get; set; }
            public decimal DuracionBloqueHoras { get; set; }
            public bool Redimensionado { get; set; }
            public bool ForzarMaquina { get; set; }
        }
        private class ProgramaParaOFVm
        {
            public int ProgramaProduccionID { get; set; }

            public int? ReleaseID { get; set; }
            public int? ReleaseDetalleID { get; set; }

            public int? SolicitudProduccionID { get; set; }
            public int? SolicitudProduccionDetalleID { get; set; }

            public int? ClienteID { get; set; }
            public string? ClienteNombre { get; set; }

            public int? ParteID { get; set; }
            public string? NumeroParte { get; set; }
            public string? ReferenciaSAP { get; set; }
            public string? DesignacionDescripcionSAP { get; set; }

            public int CantidadRequerida { get; set; }
            public int PiezasDesdePT { get; set; }
            public int CantidadProgramada { get; set; }

            public int? MaquinaID { get; set; }
            public string? MaquinaCodigo { get; set; }
            public string? MaquinaNombre { get; set; }

            public int? MoldeID { get; set; }
            public string? MoldeCodigo { get; set; }

            public string? CondicionProduccion { get; set; }
            public int? SecuenciaMaquina { get; set; }

            public DateTime? FechaInicioProgramada { get; set; }
            public DateTime? FechaFinProgramada { get; set; }
            public decimal? HorasProgramadas { get; set; }

            public int? ObjetivoHora { get; set; }
            public string? Ciclo { get; set; }
            public int? Cavidades { get; set; }
            public decimal? PesoBrutoPieza { get; set; }

            public int? MaterialID { get; set; }
            public string? MaterialCodigo { get; set; }
            public string? MaterialDescripcion { get; set; }
            public decimal? CantidadMpKg { get; set; }

            public string? EmbalajeCodigo { get; set; }
            public string? EmbalajeDescripcion { get; set; }
            public decimal? PiezasPorEmbalaje { get; set; }
            public decimal? CantidadEmbalajes { get; set; }

            public string? Observaciones { get; set; }

            public string? Color { get; set; }
            public int? PiezasPorCaja { get; set; }

            public string? TipoSecado { get; set; }
            public decimal? HorasSecado { get; set; }

            public TimeSpan? Cambio { get; set; }
            public TimeSpan? Arranque { get; set; }

            public string? TipoOF { get; set; } = "RELEASE";
            public string? MotivoTipoOF { get; set; }


        }
    }





}