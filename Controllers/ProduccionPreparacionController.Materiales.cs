using ERP.NSQuell.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

namespace ERP.NSQuell.Controllers
{
    public sealed partial class ProduccionPreparacionController
    {

        [HttpGet]
        public async Task<IActionResult> Materiales(string? filtro = null, int? maquinaId = null)
        {
            if (!UsuarioEnSesion()) return RedirectToAction("Login", "Login");
            filtro = string.IsNullOrWhiteSpace(filtro) ? null : filtro.Trim();
            if (maquinaId.HasValue && maquinaId.Value <= 0) maquinaId = null;
            var usuarioId = ObtenerUsuarioID();
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            var permisos = await ObtenerPermisosPreparacionUsuarioAsync(usuarioId, cn);
            if (!permisos.PuedeVerModulo) return StatusCode(StatusCodes.Status403Forbidden);
            await SincronizarRecepcionesFaltantesAsync(usuarioId, cn);
            var ahora = DateTime.Now;
            var materialesEsperados = await CargarMaterialesEsperadosAsync(filtro, maquinaId, cn);
            var recepciones = await CargarRecepcionesMaterialesAsync(filtro, maquinaId, cn);
            var relacionesLhRh = await CargarRelacionesMaterialesLhRhAsync(cn);
            AplicarRelacionesLhRhMaterialesEsperados(materialesEsperados, relacionesLhRh);
            AplicarRelacionesLhRhRecepciones(recepciones, relacionesLhRh);
            var vm = new ProduccionPreparacionMaterialesVm
            {
                FechaConsulta = ahora,
                Filtro = filtro,
                MaquinaID = maquinaId,
                PuedeGestionarMateriales = permisos.PuedeGestionarEmbalaje,
                Maquinas = await CargarMaquinasPreparacionAsync(cn),
                MaterialesEsperados = materialesEsperados,
                Recepciones = recepciones
            };
            return View("Materiales", vm);
        }

        private static async Task<Dictionary<int, RelacionMaterialLhRhInterna>> CargarRelacionesMaterialesLhRhAsync(SqlConnection cn)
        {
            var resultado = new Dictionary<int, RelacionMaterialLhRhInterna>();
            const string sql = @"
SELECT
    origen.ProgramaProduccionID,
    grupo.GrupoLhRh,
    pareja.ProgramaProduccionID AS ProgramaParejaID,
    ejecucionPareja.EjecucionProduccionID AS EjecucionParejaID,
    COALESCE(NULLIF(LTRIM(RTRIM(sPareja.NumeroOFRecibida)),N''),NULLIF(LTRIM(RTRIM(sPareja.FolioSolicitud)),N''),N'') AS NumeroOFPareja,
    pareja.ParteID AS ParteParejaID,
    pareja.NumeroParte AS NumeroPartePareja,
    pareja.ReferenciaSAP AS ReferenciaSAPPareja,
    pareja.DesignacionDescripcionSAP AS DescripcionPartePareja
FROM dbo.Planeacion_ProgramaProduccion origen
OUTER APPLY
(
    SELECT CHARINDEX(N'NSQ_LHRH_PAIR:',ISNULL(origen.Observaciones,N'')) AS PosGrupo
) marca
OUTER APPLY
(
    SELECT TRY_CONVERT
    (
        INT,
        LEFT
        (
            SUBSTRING(origen.Observaciones,marca.PosGrupo+LEN(N'NSQ_LHRH_PAIR:'),50),
            CHARINDEX(N';',SUBSTRING(origen.Observaciones,marca.PosGrupo+LEN(N'NSQ_LHRH_PAIR:'),50)+N';')-1
        )
    ) AS GrupoLhRh
    WHERE marca.PosGrupo>0
) grupo
INNER JOIN dbo.Planeacion_ProgramaProduccion pareja
    ON pareja.Activo=1
   AND pareja.ProgramaProduccionID<>origen.ProgramaProduccionID
   AND grupo.GrupoLhRh IS NOT NULL
   AND pareja.Observaciones LIKE N'%NSQ_LHRH_PAIR:'+CONVERT(NVARCHAR(20),grupo.GrupoLhRh)+N';%'
LEFT JOIN dbo.SolicitudesProduccion sPareja
    ON sPareja.SolicitudProduccionID=pareja.SolicitudProduccionID
   AND sPareja.Activo=1
OUTER APPLY
(
    SELECT TOP(1) e.EjecucionProduccionID
    FROM dbo.Produccion_Ejecucion e
    WHERE e.ProgramaProduccionID=pareja.ProgramaProduccionID
      AND e.Activo=1
    ORDER BY e.EjecucionProduccionID DESC
) ejecucionPareja
WHERE origen.Activo=1
  AND grupo.GrupoLhRh IS NOT NULL
ORDER BY origen.ProgramaProduccionID,pareja.ProgramaProduccionID;";
            await using var cmd = new SqlCommand(sql, cn);
            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                var programaId = Convert.ToInt32(rd["ProgramaProduccionID"]);
                if (resultado.ContainsKey(programaId)) continue;
                resultado[programaId] = new RelacionMaterialLhRhInterna
                {
                    GrupoLhRh = rd["GrupoLhRh"] == DBNull.Value ? null : Convert.ToInt32(rd["GrupoLhRh"]),
                    ProgramaParejaID = Convert.ToInt32(rd["ProgramaParejaID"]),
                    EjecucionParejaID = rd["EjecucionParejaID"] == DBNull.Value ? null : Convert.ToInt32(rd["EjecucionParejaID"]),
                    NumeroOFPareja = rd["NumeroOFPareja"] == DBNull.Value ? null : rd["NumeroOFPareja"]?.ToString()?.Trim(),
                    ParteParejaID = rd["ParteParejaID"] == DBNull.Value ? null : Convert.ToInt32(rd["ParteParejaID"]),
                    NumeroPartePareja = rd["NumeroPartePareja"] == DBNull.Value ? null : rd["NumeroPartePareja"]?.ToString()?.Trim(),
                    ReferenciaSAPPareja = rd["ReferenciaSAPPareja"] == DBNull.Value ? null : rd["ReferenciaSAPPareja"]?.ToString()?.Trim(),
                    DescripcionPartePareja = rd["DescripcionPartePareja"] == DBNull.Value ? null : rd["DescripcionPartePareja"]?.ToString()?.Trim()
                };
            }
            return resultado;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmarRecepcionMaterial(ProduccionConfirmarRecepcionMaterialVm vm)
        {
            if (!UsuarioEnSesion()) return RedirectToAction("Login", "Login");
            if (vm.RecepcionMaterialID <= 0)
            {
                TempData["Error"] = "No se recibió correctamente la entrega que deseas confirmar.";
                return RedirectToAction(nameof(Materiales));
            }

            var decision = string.IsNullOrWhiteSpace(vm.Decision) ? string.Empty : vm.Decision.Trim().ToUpperInvariant();
            var motivo = string.IsNullOrWhiteSpace(vm.MotivoDiferencia) ? null : vm.MotivoDiferencia.Trim();
            var observaciones = string.IsNullOrWhiteSpace(vm.Observaciones) ? null : vm.Observaciones.Trim();

            if (decision != ProduccionRecepcionMaterialDecision.Completo && decision != ProduccionRecepcionMaterialDecision.Parcial && decision != ProduccionRecepcionMaterialDecision.NoRecibido)
            {
                TempData["Error"] = "Selecciona si recibiste completo, parcialmente o no recibiste el material.";
                return RedirectToAction(nameof(Materiales));
            }

            if (motivo?.Length > 500)
            {
                TempData["Error"] = "El motivo de la diferencia no puede superar 500 caracteres.";
                return RedirectToAction(nameof(Materiales));
            }

            if (observaciones?.Length > 800)
            {
                TempData["Error"] = "Las observaciones no pueden superar 800 caracteres.";
                return RedirectToAction(nameof(Materiales));
            }

            var usuarioId = ObtenerUsuarioID();
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            var permisos = await ObtenerPermisosPreparacionUsuarioAsync(usuarioId, cn);
            if (!permisos.PuedeGestionarEmbalaje) return StatusCode(StatusCodes.Status403Forbidden);

            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);

            try
            {
                const string sqlRecepcion = @"
SELECT TOP(1)
    r.RecepcionMaterialID,
    r.TipoOrigen,
    r.MovimientoAlmacenID,
    r.SolicitudProduccionID,
    r.SolicitudProduccionDetalleID,
    r.ProgramaProduccionID,
    r.EjecucionProduccionID,
    r.MaterialSolicitadoID,
    r.MaterialEntregadoID,
    r.EmbalajeSolicitadoID,
    r.EmbalajeEntregadoID,
    r.CodigoEntregadoSnapshot,
    r.Unidad,
    r.CantidadEntregadaAlmacen,
    r.EstadoRecepcion,
    r.EstadoAclaracion
FROM dbo.Produccion_RecepcionMateriales r WITH(UPDLOCK,HOLDLOCK)
WHERE r.RecepcionMaterialID=@RecepcionMaterialID
  AND r.Activo=1;";

                string tipoOrigen;
                long movimientoAlmacenId;
                int solicitudProduccionId;
                int? solicitudProduccionDetalleId;
                int? programaProduccionId;
                int? ejecucionProduccionId;
                int? materialSolicitadoId;
                int? materialEntregadoId;
                int? embalajeSolicitadoId;
                int? embalajeEntregadoId;
                string codigoEntregado;
                string unidad;
                decimal cantidadEntregada;
                string estadoAnterior;
                string aclaracionAnterior;

                await using (var cmd = new SqlCommand(sqlRecepcion, cn, tx))
                {
                    cmd.Parameters.Add("@RecepcionMaterialID", SqlDbType.BigInt).Value = vm.RecepcionMaterialID;
                    await using var rd = await cmd.ExecuteReaderAsync();

                    if (!await rd.ReadAsync())
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] = "La entrega ya no existe o dejó de estar disponible.";
                        return RedirectToAction(nameof(Materiales));
                    }

                    tipoOrigen = rd["TipoOrigen"]?.ToString()?.Trim() ?? string.Empty;
                    movimientoAlmacenId = Convert.ToInt64(rd["MovimientoAlmacenID"]);
                    solicitudProduccionId = Convert.ToInt32(rd["SolicitudProduccionID"]);
                    solicitudProduccionDetalleId = rd["SolicitudProduccionDetalleID"] == DBNull.Value ? null : Convert.ToInt32(rd["SolicitudProduccionDetalleID"]);
                    programaProduccionId = rd["ProgramaProduccionID"] == DBNull.Value ? null : Convert.ToInt32(rd["ProgramaProduccionID"]);
                    ejecucionProduccionId = rd["EjecucionProduccionID"] == DBNull.Value ? null : Convert.ToInt32(rd["EjecucionProduccionID"]);
                    materialSolicitadoId = rd["MaterialSolicitadoID"] == DBNull.Value ? null : Convert.ToInt32(rd["MaterialSolicitadoID"]);
                    materialEntregadoId = rd["MaterialEntregadoID"] == DBNull.Value ? null : Convert.ToInt32(rd["MaterialEntregadoID"]);
                    embalajeSolicitadoId = rd["EmbalajeSolicitadoID"] == DBNull.Value ? null : Convert.ToInt32(rd["EmbalajeSolicitadoID"]);
                    embalajeEntregadoId = rd["EmbalajeEntregadoID"] == DBNull.Value ? null : Convert.ToInt32(rd["EmbalajeEntregadoID"]);
                    codigoEntregado = rd["CodigoEntregadoSnapshot"]?.ToString()?.Trim() ?? string.Empty;
                    unidad = rd["Unidad"]?.ToString()?.Trim() ?? string.Empty;
                    cantidadEntregada = Convert.ToDecimal(rd["CantidadEntregadaAlmacen"]);
                    estadoAnterior = rd["EstadoRecepcion"]?.ToString()?.Trim() ?? string.Empty;
                    aclaracionAnterior = rd["EstadoAclaracion"]?.ToString()?.Trim() ?? ProduccionRecepcionMaterialEstadoAclaracion.NoAplica;
                }

                if (!string.Equals(estadoAnterior, ProduccionRecepcionMaterialEstado.Pendiente, StringComparison.OrdinalIgnoreCase))
                {
                    await tx.RollbackAsync();
                    TempData["Warning"] = "Esta entrega ya fue confirmada anteriormente.";
                    return RedirectToAction(nameof(Materiales));
                }

                if (cantidadEntregada <= 0)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "La cantidad reportada por Almacén no es válida.";
                    return RedirectToAction(nameof(Materiales));
                }

                decimal cantidadRecibida;
                string nuevoEstado;
                string nuevoEstadoAclaracion;

                if (decision == ProduccionRecepcionMaterialDecision.Completo)
                {
                    cantidadRecibida = cantidadEntregada;
                    nuevoEstado = ProduccionRecepcionMaterialEstado.RecibidoCompleto;
                    nuevoEstadoAclaracion = ProduccionRecepcionMaterialEstadoAclaracion.NoAplica;
                    motivo = null;
                }
                else if (decision == ProduccionRecepcionMaterialDecision.Parcial)
                {
                    if (!vm.CantidadRecibida.HasValue)
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] = "Indica la cantidad que realmente recibiste.";
                        return RedirectToAction(nameof(Materiales));
                    }

                    cantidadRecibida = vm.CantidadRecibida.Value;

                    if (cantidadRecibida <= 0)
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] = "Para recepción parcial, la cantidad recibida debe ser mayor a cero.";
                        return RedirectToAction(nameof(Materiales));
                    }

                    if (cantidadRecibida >= cantidadEntregada)
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] = $"Para recepción parcial debes indicar una cantidad menor a la reportada por Almacén ({cantidadEntregada:0.####} {unidad}).";
                        return RedirectToAction(nameof(Materiales));
                    }

                    if (string.IsNullOrWhiteSpace(motivo))
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] = "Debes indicar por qué recibiste una cantidad menor a la reportada por Almacén.";
                        return RedirectToAction(nameof(Materiales));
                    }

                    nuevoEstado = ProduccionRecepcionMaterialEstado.RecibidoParcial;
                    nuevoEstadoAclaracion = ProduccionRecepcionMaterialEstadoAclaracion.Pendiente;
                }
                else
                {
                    cantidadRecibida = 0m;

                    if (string.IsNullOrWhiteSpace(motivo))
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] = "Debes indicar por qué no recibiste el material reportado por Almacén.";
                        return RedirectToAction(nameof(Materiales));
                    }

                    nuevoEstado = ProduccionRecepcionMaterialEstado.NoRecibido;
                    nuevoEstadoAclaracion = ProduccionRecepcionMaterialEstadoAclaracion.Pendiente;
                }

                if (!programaProduccionId.HasValue || programaProduccionId.Value <= 0)
                    programaProduccionId = await ResolverProgramaRecepcionMaterialAsync(solicitudProduccionId, solicitudProduccionDetalleId, cn, tx);

                const string sqlActualizar = @"
UPDATE dbo.Produccion_RecepcionMateriales
SET
    ProgramaProduccionID=COALESCE(ProgramaProduccionID,@ProgramaProduccionID),
    EstadoRecepcion=@EstadoRecepcion,
    CantidadRecibidaProduccion=@CantidadRecibidaProduccion,
    MotivoDiferencia=@MotivoDiferencia,
    ObservacionesRecepcion=@ObservacionesRecepcion,
    UsuarioRecepcionID=@UsuarioID,
    FechaRecepcion=SYSDATETIME(),
    EstadoAclaracion=@EstadoAclaracion,
    ResolucionAclaracion=NULL,
    UsuarioResolucionID=NULL,
    FechaResolucion=NULL,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=SYSDATETIME()
WHERE RecepcionMaterialID=@RecepcionMaterialID
  AND Activo=1
  AND EstadoRecepcion=@EstadoPendiente;

IF @@ROWCOUNT<>1
    THROW 51201,'La recepción cambió de estado mientras se confirmaba.',1;";

                await using (var cmd = new SqlCommand(sqlActualizar, cn, tx))
                {
                    cmd.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = programaProduccionId.HasValue ? programaProduccionId.Value : DBNull.Value;
                    cmd.Parameters.Add("@EstadoRecepcion", SqlDbType.NVarChar, 30).Value = nuevoEstado;
                    var pCantidad = cmd.Parameters.Add("@CantidadRecibidaProduccion", SqlDbType.Decimal);
                    pCantidad.Precision = 18;
                    pCantidad.Scale = 4;
                    pCantidad.Value = cantidadRecibida;
                    cmd.Parameters.Add("@MotivoDiferencia", SqlDbType.NVarChar, 500).Value = string.IsNullOrWhiteSpace(motivo) ? DBNull.Value : motivo;
                    cmd.Parameters.Add("@ObservacionesRecepcion", SqlDbType.NVarChar, 800).Value = string.IsNullOrWhiteSpace(observaciones) ? DBNull.Value : observaciones;
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
                    cmd.Parameters.Add("@EstadoAclaracion", SqlDbType.NVarChar, 20).Value = nuevoEstadoAclaracion;
                    cmd.Parameters.Add("@RecepcionMaterialID", SqlDbType.BigInt).Value = vm.RecepcionMaterialID;
                    cmd.Parameters.Add("@EstadoPendiente", SqlDbType.NVarChar, 30).Value = ProduccionRecepcionMaterialEstado.Pendiente;
                    await cmd.ExecuteNonQueryAsync();
                }

                var validacionCompleta = nuevoEstado == ProduccionRecepcionMaterialEstado.RecibidoCompleto;
                await ActualizarValidacionMovimientoAlmacenAsync(tipoOrigen, movimientoAlmacenId, solicitudProduccionId, validacionCompleta, cn, tx);

                var comentarioHistorial = ConstruirComentarioRecepcionMaterial(tipoOrigen, codigoEntregado, cantidadEntregada, cantidadRecibida, unidad, nuevoEstado, motivo);
                await AgregarHistorialRecepcionMaterialAsync(vm.RecepcionMaterialID, "CONFIRMACION_PRODUCCION", estadoAnterior, nuevoEstado, null, cantidadRecibida, aclaracionAnterior, nuevoEstadoAclaracion, comentarioHistorial, usuarioId, cn, tx);

                var materialEnviadoASecado = false;
                if (string.Equals(tipoOrigen, ProduccionRecepcionMaterialTipo.MP, StringComparison.OrdinalIgnoreCase) && cantidadRecibida > 0.0005m)
                    materialEnviadoASecado = await RegistrarMaterialPendienteSecadoDesdeRecepcionAsync(vm.RecepcionMaterialID, usuarioId, cn, tx);

                var embalajeAutoConfirmado = false;
                if (string.Equals(tipoOrigen, ProduccionRecepcionMaterialTipo.Embalaje, StringComparison.OrdinalIgnoreCase) && embalajeSolicitadoId.HasValue && embalajeSolicitadoId.Value > 0 && programaProduccionId.HasValue && programaProduccionId.Value > 0)
                    embalajeAutoConfirmado = await AutoConfirmarPreparacionEmbalajeAsync(programaProduccionId.Value, solicitudProduccionId, solicitudProduccionDetalleId, embalajeSolicitadoId.Value, usuarioId, cn, tx);

                await tx.CommitAsync();

                if (nuevoEstado == ProduccionRecepcionMaterialEstado.RecibidoCompleto)
                {
                    TempData["Success"] = $"Recepción confirmada: {cantidadRecibida:0.####} {unidad} de {codigoEntregado}.";
                    if (materialEnviadoASecado) TempData["Success"] += " El material quedó disponible en Secado.";
                    if (embalajeAutoConfirmado) TempData["Success"] += " El embalaje requerido para la OF quedó completo y su preparación se confirmó automáticamente.";
                }
                else if (nuevoEstado == ProduccionRecepcionMaterialEstado.RecibidoParcial)
                {
                    var diferencia = cantidadEntregada - cantidadRecibida;
                    TempData["Warning"] = $"Recepción parcial registrada. Almacén reportó {cantidadEntregada:0.####} {unidad}; Producción confirmó {cantidadRecibida:0.####} {unidad}. Diferencia: {diferencia:0.####} {unidad}.";
                    if (materialEnviadoASecado) TempData["Warning"] += " La cantidad realmente recibida quedó disponible en Secado.";
                }
                else
                {
                    TempData["Warning"] = $"Se registró que Producción no recibió la entrega de {cantidadEntregada:0.####} {unidad} de {codigoEntregado}. La diferencia quedó pendiente de aclaración.";
                }

                return RedirectToAction(nameof(Materiales));
            }
            catch (Exception ex)
            {
                try { await tx.RollbackAsync(); } catch { }
                TempData["Error"] = "No fue posible confirmar la recepción del material: " + ex.Message;
                return RedirectToAction(nameof(Materiales));
            }
        }

        private async Task<List<ProduccionRecepcionMaterialVm>>
            CargarRecepcionesMaterialesAsync(
                string? filtro,
                int? maquinaId,
                SqlConnection cn)
        {
            var lista =
                new List<ProduccionRecepcionMaterialVm>();

            const string sql = @"
SELECT
    r.RecepcionMaterialID,
    r.TipoOrigen,
    r.MovimientoAlmacenID,

    r.SolicitudProduccionID,
    r.SolicitudProduccionDetalleID,

    COALESCE
    (
        r.ProgramaProduccionID,
        programa.ProgramaProduccionID
    ) AS ProgramaProduccionID,

    COALESCE
    (
        r.EjecucionProduccionID,
        ejecucion.EjecucionProduccionID
    ) AS EjecucionProduccionID,

    ISNULL(NULLIF(r.NumeroOFSnapshot,N''),s.NumeroOFRecibida) AS NumeroOF,

    programa.MaquinaID,
    programa.MaquinaCodigo,
    programa.MaquinaNombre,

    programa.ParteID,
    programa.NumeroParte,
    programa.DescripcionParte,

    r.MaterialSolicitadoID,
    r.MaterialEntregadoID,
    r.EmbalajeSolicitadoID,
    r.EmbalajeEntregadoID,

    r.CodigoSolicitadoSnapshot,
    r.DescripcionSolicitadaSnapshot,
    r.CodigoEntregadoSnapshot,
    r.DescripcionEntregadaSnapshot,

    r.TipoMP,
    r.Lote,
    r.Unidad,

    r.CantidadEntregadaAlmacen,
    r.CantidadRecibidaProduccion,
    r.CantidadDiferencia,

    ISNULL(requerido.CantidadRequerida,0) AS CantidadRequeridaOF,
    ISNULL(acumulado.CantidadEntregadaAlmacen,0) AS CantidadEntregadaAcumuladaAlmacen,
    ISNULL(acumulado.CantidadRecibidaProduccion,0) AS CantidadRecibidaAcumuladaProduccion,

    r.FechaEntregaAlmacen,
    r.UsuarioEntregaAlmacenID,

    COALESCE
    (
        NULLIF(LTRIM(RTRIM(r.UsuarioEntregaAlmacenNombre)),N''),
        NULLIF(LTRIM(RTRIM(
            ISNULL(pEntrega.Nombre,N'')+N' '+
            ISNULL(pEntrega.ApellidoPaterno,N'')+N' '+
            ISNULL(pEntrega.ApellidoMaterno,N'')
        )),N'')
    ) AS UsuarioEntregaAlmacenNombre,

    r.ReferenciaOperacion,
    r.ObservacionesAlmacen,

    r.EstadoRecepcion,
    r.MotivoDiferencia,
    r.ObservacionesRecepcion,

    r.UsuarioRecepcionID,

    NULLIF(LTRIM(RTRIM(
        ISNULL(pRecepcion.Nombre,N'')+N' '+
        ISNULL(pRecepcion.ApellidoPaterno,N'')+N' '+
        ISNULL(pRecepcion.ApellidoMaterno,N'')
    )),N'') AS UsuarioRecepcionNombre,

    r.FechaRecepcion,
    r.EstadoAclaracion,
    r.ResolucionAclaracion,
    r.FechaResolucion

FROM dbo.Produccion_RecepcionMateriales r

LEFT JOIN dbo.SolicitudesProduccion s
    ON s.SolicitudProduccionID=r.SolicitudProduccionID

OUTER APPLY
(
    SELECT TOP(1)
        pp.ProgramaProduccionID,
        pp.SolicitudProduccionDetalleID,
        pp.MaquinaID,
        COALESCE
        (
            NULLIF(LTRIM(RTRIM(pp.MaquinaCodigo)),N''),
            maq.Codigo
        ) AS MaquinaCodigo,
        COALESCE
        (
            NULLIF(LTRIM(RTRIM(pp.MaquinaNombre)),N''),
            maq.Nombre
        ) AS MaquinaNombre,
        pp.ParteID,
        COALESCE
        (
            NULLIF(LTRIM(RTRIM(pp.ReferenciaSAP)),N''),
            NULLIF(LTRIM(RTRIM(pp.NumeroParte)),N'')
        ) AS NumeroParte,
        pp.DesignacionDescripcionSAP AS DescripcionParte
    FROM dbo.Planeacion_ProgramaProduccion pp
    LEFT JOIN dbo.ERP_Maquinas maq
        ON maq.MaquinaID=pp.MaquinaID
    WHERE pp.Activo=1
      AND
      (
          (
              r.ProgramaProduccionID IS NOT NULL
              AND pp.ProgramaProduccionID=r.ProgramaProduccionID
          )
          OR
          (
              r.ProgramaProduccionID IS NULL
              AND pp.SolicitudProduccionID=r.SolicitudProduccionID
              AND
              (
                  r.SolicitudProduccionDetalleID IS NULL
                  OR pp.SolicitudProduccionDetalleID=r.SolicitudProduccionDetalleID
              )
          )
      )
    ORDER BY
        CASE
            WHEN r.ProgramaProduccionID IS NOT NULL
             AND pp.ProgramaProduccionID=r.ProgramaProduccionID
                THEN 0
            ELSE 1
        END,
        pp.ProgramaProduccionID DESC
) programa

OUTER APPLY
(
    SELECT TOP(1)
        e.EjecucionProduccionID
    FROM dbo.Produccion_Ejecucion e
    WHERE e.Activo=1
      AND e.ProgramaProduccionID=
          COALESCE
          (
              r.ProgramaProduccionID,
              programa.ProgramaProduccionID
          )
    ORDER BY e.EjecucionProduccionID DESC
) ejecucion
OUTER APPLY
(
    SELECT
        CONVERT
        (
            DECIMAL(18,4),
            CASE
                WHEN r.TipoOrigen=N'MP'
                THEN
                (
                    SELECT ISNULL(SUM(CONVERT(DECIMAL(18,4),ISNULL(d.CantidadMpKg,0))),0)
                    FROM dbo.SolicitudesProduccionDetalle d
                    WHERE d.SolicitudProduccionID=r.SolicitudProduccionID
                      AND d.Activo=1
                      AND
                      (
                          (
                              r.SolicitudProduccionDetalleID IS NOT NULL
                              AND d.SolicitudProduccionDetalleID=r.SolicitudProduccionDetalleID
                          )
                          OR
                          (
                              r.SolicitudProduccionDetalleID IS NULL
                              AND EXISTS
                              (
                                  SELECT 1
                                  FROM dbo.ERP_Materiales mReq
                                  WHERE mReq.MaterialID=r.MaterialSolicitadoID
                                    AND mReq.Activo=1
                                    AND UPPER(LTRIM(RTRIM(ISNULL(d.MaterialCodigo,N''))))=
                                        UPPER(LTRIM(RTRIM(ISNULL(mReq.Codigo,N''))))
                              )
                          )
                      )
                )
                WHEN r.TipoOrigen=N'EMBALAJE'
                THEN
                (
                    SELECT ISNULL(SUM(CONVERT(DECIMAL(18,4),ISNULL(d.CantidadEmbalajes,0))),0)
                    FROM dbo.SolicitudesProduccionDetalle d
                    WHERE d.SolicitudProduccionID=r.SolicitudProduccionID
                      AND d.Activo=1
                      AND
                      (
                          (
                              r.SolicitudProduccionDetalleID IS NOT NULL
                              AND d.SolicitudProduccionDetalleID=r.SolicitudProduccionDetalleID
                          )
                          OR
                          (
                              r.SolicitudProduccionDetalleID IS NULL
                              AND EXISTS
                              (
                                  SELECT 1
                                  FROM dbo.ERP_Embalajes eReq
                                  WHERE eReq.EmbalajeID=r.EmbalajeSolicitadoID
                                    AND eReq.Activo=1
                                    AND UPPER(LTRIM(RTRIM(ISNULL(d.EmbalajeCodigo,N''))))=
                                        UPPER(LTRIM(RTRIM(ISNULL(eReq.Codigo,N''))))
                              )
                          )
                      )
                )
                ELSE 0
            END
        ) AS CantidadRequerida
) requerido

OUTER APPLY
(
    SELECT
        CONVERT
        (
            DECIMAL(18,4),
            ISNULL(SUM(r2.CantidadEntregadaAlmacen),0)
        ) AS CantidadEntregadaAlmacen,

        CONVERT
        (
            DECIMAL(18,4),
            ISNULL(SUM(ISNULL(r2.CantidadRecibidaProduccion,0)),0)
        ) AS CantidadRecibidaProduccion

    FROM dbo.Produccion_RecepcionMateriales r2

    WHERE r2.Activo=1
      AND r2.SolicitudProduccionID=r.SolicitudProduccionID
      AND r2.TipoOrigen=r.TipoOrigen

      AND ISNULL(r2.MaterialSolicitadoID,0)=
          ISNULL(r.MaterialSolicitadoID,0)

      AND ISNULL(r2.EmbalajeSolicitadoID,0)=
          ISNULL(r.EmbalajeSolicitadoID,0)

      AND
      (
          r.SolicitudProduccionDetalleID IS NULL
          OR r2.SolicitudProduccionDetalleID=r.SolicitudProduccionDetalleID
          OR r2.SolicitudProduccionDetalleID IS NULL
      )
) acumulado

LEFT JOIN dbo.Usuarios uEntrega
    ON uEntrega.UsuarioID=r.UsuarioEntregaAlmacenID

LEFT JOIN dbo.Persona pEntrega
    ON pEntrega.PersonaID=uEntrega.PersonaID

LEFT JOIN dbo.Usuarios uRecepcion
    ON uRecepcion.UsuarioID=r.UsuarioRecepcionID

LEFT JOIN dbo.Persona pRecepcion
    ON pRecepcion.PersonaID=uRecepcion.PersonaID

WHERE r.Activo=1

  AND
  (
      r.EstadoRecepcion=N'PENDIENTE'
      OR COALESCE
         (
             r.FechaRecepcion,
             r.FechaEntregaAlmacen
         )>=DATEADD(DAY,-30,GETDATE())
  )

  AND
  (
      @MaquinaID IS NULL
      OR programa.MaquinaID=@MaquinaID
  )

  AND
  (
      @Filtro IS NULL

      OR r.NumeroOFSnapshot LIKE N'%'+@Filtro+N'%'
      OR s.NumeroOFRecibida LIKE N'%'+@Filtro+N'%'

      OR r.CodigoSolicitadoSnapshot LIKE N'%'+@Filtro+N'%'
      OR r.DescripcionSolicitadaSnapshot LIKE N'%'+@Filtro+N'%'

      OR r.CodigoEntregadoSnapshot LIKE N'%'+@Filtro+N'%'
      OR r.DescripcionEntregadaSnapshot LIKE N'%'+@Filtro+N'%'

      OR programa.NumeroParte LIKE N'%'+@Filtro+N'%'
      OR programa.DescripcionParte LIKE N'%'+@Filtro+N'%'

      OR programa.MaquinaCodigo LIKE N'%'+@Filtro+N'%'
      OR programa.MaquinaNombre LIKE N'%'+@Filtro+N'%'

      OR r.Lote LIKE N'%'+@Filtro+N'%'
      OR r.ReferenciaOperacion LIKE N'%'+@Filtro+N'%'
  )

ORDER BY
    CASE r.EstadoRecepcion
        WHEN N'PENDIENTE' THEN 1
        WHEN N'RECIBIDO_PARCIAL' THEN 2
        WHEN N'NO_RECIBIDO' THEN 3
        WHEN N'RECIBIDO_COMPLETO' THEN 4
        ELSE 5
    END,

    CASE r.EstadoAclaracion
        WHEN N'PENDIENTE' THEN 0
        ELSE 1
    END,

    r.FechaEntregaAlmacen DESC,
    r.RecepcionMaterialID DESC;";

            await using var cmd =
                new SqlCommand(sql, cn);

            cmd.Parameters.Add(
                "@Filtro",
                SqlDbType.NVarChar,
                200).Value =
                string.IsNullOrWhiteSpace(filtro)
                    ? DBNull.Value
                    : filtro.Trim();

            cmd.Parameters.Add(
                "@MaquinaID",
                SqlDbType.Int).Value =
                maquinaId.HasValue && maquinaId.Value > 0
                    ? maquinaId.Value
                    : DBNull.Value;

            await using var rd =
                await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                lista.Add(
                    new ProduccionRecepcionMaterialVm
                    {
                        RecepcionMaterialID =
                            Convert.ToInt64(
                                rd["RecepcionMaterialID"]),

                        TipoOrigen =
                            rd["TipoOrigen"]?.ToString()?.Trim()
                            ?? string.Empty,

                        MovimientoAlmacenID =
                            Convert.ToInt64(
                                rd["MovimientoAlmacenID"]),

                        SolicitudProduccionID =
                            Convert.ToInt32(
                                rd["SolicitudProduccionID"]),

                        SolicitudProduccionDetalleID =
                            rd["SolicitudProduccionDetalleID"] == DBNull.Value
                                ? null
                                : Convert.ToInt32(
                                    rd["SolicitudProduccionDetalleID"]),

                        ProgramaProduccionID =
                            rd["ProgramaProduccionID"] == DBNull.Value
                                ? null
                                : Convert.ToInt32(
                                    rd["ProgramaProduccionID"]),

                        EjecucionProduccionID =
                            rd["EjecucionProduccionID"] == DBNull.Value
                                ? null
                                : Convert.ToInt32(
                                    rd["EjecucionProduccionID"]),

                        NumeroOF =
                            rd["NumeroOF"]?.ToString()?.Trim()
                            ?? string.Empty,

                        MaquinaID =
                            rd["MaquinaID"] == DBNull.Value
                                ? null
                                : Convert.ToInt32(
                                    rd["MaquinaID"]),

                        MaquinaCodigo =
                            rd["MaquinaCodigo"] == DBNull.Value
                                ? null
                                : rd["MaquinaCodigo"]?.ToString()?.Trim(),

                        MaquinaNombre =
                            rd["MaquinaNombre"] == DBNull.Value
                                ? null
                                : rd["MaquinaNombre"]?.ToString()?.Trim(),

                        ParteID =
                            rd["ParteID"] == DBNull.Value
                                ? null
                                : Convert.ToInt32(
                                    rd["ParteID"]),

                        NumeroParte =
                            rd["NumeroParte"] == DBNull.Value
                                ? null
                                : rd["NumeroParte"]?.ToString()?.Trim(),

                        DescripcionParte =
                            rd["DescripcionParte"] == DBNull.Value
                                ? null
                                : rd["DescripcionParte"]?.ToString()?.Trim(),

                        MaterialSolicitadoID =
                            rd["MaterialSolicitadoID"] == DBNull.Value
                                ? null
                                : Convert.ToInt32(
                                    rd["MaterialSolicitadoID"]),

                        MaterialEntregadoID =
                            rd["MaterialEntregadoID"] == DBNull.Value
                                ? null
                                : Convert.ToInt32(
                                    rd["MaterialEntregadoID"]),

                        EmbalajeSolicitadoID =
                            rd["EmbalajeSolicitadoID"] == DBNull.Value
                                ? null
                                : Convert.ToInt32(
                                    rd["EmbalajeSolicitadoID"]),

                        EmbalajeEntregadoID =
                            rd["EmbalajeEntregadoID"] == DBNull.Value
                                ? null
                                : Convert.ToInt32(
                                    rd["EmbalajeEntregadoID"]),

                        CodigoSolicitado =
                            rd["CodigoSolicitadoSnapshot"] == DBNull.Value
                                ? null
                                : rd["CodigoSolicitadoSnapshot"]?.ToString()?.Trim(),

                        DescripcionSolicitada =
                            rd["DescripcionSolicitadaSnapshot"] == DBNull.Value
                                ? null
                                : rd["DescripcionSolicitadaSnapshot"]?.ToString()?.Trim(),

                        CodigoEntregado =
                            rd["CodigoEntregadoSnapshot"]?.ToString()?.Trim()
                            ?? string.Empty,

                        DescripcionEntregada =
                            rd["DescripcionEntregadaSnapshot"] == DBNull.Value
                                ? null
                                : rd["DescripcionEntregadaSnapshot"]?.ToString()?.Trim(),

                        TipoMP =
                            rd["TipoMP"] == DBNull.Value
                                ? null
                                : rd["TipoMP"]?.ToString()?.Trim(),

                        Lote =
                            rd["Lote"] == DBNull.Value
                                ? null
                                : rd["Lote"]?.ToString()?.Trim(),

                        Unidad =
                            rd["Unidad"]?.ToString()?.Trim()
                            ?? string.Empty,

                        CantidadEntregadaAlmacen =
                            Convert.ToDecimal(
                                rd["CantidadEntregadaAlmacen"]),

                        CantidadRecibidaProduccion =
                            rd["CantidadRecibidaProduccion"] == DBNull.Value
                                ? null
                                : Convert.ToDecimal(
                                    rd["CantidadRecibidaProduccion"]),

                        CantidadDiferencia =
                            rd["CantidadDiferencia"] == DBNull.Value
                                ? null
                                : Convert.ToDecimal(
                                    rd["CantidadDiferencia"]),

                        CantidadRequeridaOF =
                            Convert.ToDecimal(
                                rd["CantidadRequeridaOF"]),

                        CantidadEntregadaAcumuladaAlmacen =
                            Convert.ToDecimal(
                                rd["CantidadEntregadaAcumuladaAlmacen"]),

                        CantidadRecibidaAcumuladaProduccion =
                            Convert.ToDecimal(
                                rd["CantidadRecibidaAcumuladaProduccion"]),

                        FechaEntregaAlmacen =
                            Convert.ToDateTime(
                                rd["FechaEntregaAlmacen"]),

                        UsuarioEntregaAlmacenID =
                            rd["UsuarioEntregaAlmacenID"] == DBNull.Value
                                ? null
                                : Convert.ToInt32(
                                    rd["UsuarioEntregaAlmacenID"]),

                        UsuarioEntregaAlmacenNombre =
                            rd["UsuarioEntregaAlmacenNombre"] == DBNull.Value
                                ? null
                                : rd["UsuarioEntregaAlmacenNombre"]?.ToString()?.Trim(),

                        ReferenciaOperacion =
                            rd["ReferenciaOperacion"] == DBNull.Value
                                ? null
                                : rd["ReferenciaOperacion"]?.ToString()?.Trim(),

                        ObservacionesAlmacen =
                            rd["ObservacionesAlmacen"] == DBNull.Value
                                ? null
                                : rd["ObservacionesAlmacen"]?.ToString()?.Trim(),

                        EstadoRecepcion =
                            rd["EstadoRecepcion"]?.ToString()?.Trim()
                            ?? ProduccionRecepcionMaterialEstado.Pendiente,

                        MotivoDiferencia =
                            rd["MotivoDiferencia"] == DBNull.Value
                                ? null
                                : rd["MotivoDiferencia"]?.ToString()?.Trim(),

                        ObservacionesRecepcion =
                            rd["ObservacionesRecepcion"] == DBNull.Value
                                ? null
                                : rd["ObservacionesRecepcion"]?.ToString()?.Trim(),

                        UsuarioRecepcionID =
                            rd["UsuarioRecepcionID"] == DBNull.Value
                                ? null
                                : Convert.ToInt32(
                                    rd["UsuarioRecepcionID"]),

                        UsuarioRecepcionNombre =
                            rd["UsuarioRecepcionNombre"] == DBNull.Value
                                ? null
                                : rd["UsuarioRecepcionNombre"]?.ToString()?.Trim(),

                        FechaRecepcion =
                            rd["FechaRecepcion"] == DBNull.Value
                                ? null
                                : Convert.ToDateTime(
                                    rd["FechaRecepcion"]),

                        EstadoAclaracion =
                            rd["EstadoAclaracion"]?.ToString()?.Trim()
                            ?? ProduccionRecepcionMaterialEstadoAclaracion.NoAplica,

                        ResolucionAclaracion =
                            rd["ResolucionAclaracion"] == DBNull.Value
                                ? null
                                : rd["ResolucionAclaracion"]?.ToString()?.Trim(),

                        FechaResolucion =
                            rd["FechaResolucion"] == DBNull.Value
                                ? null
                                : Convert.ToDateTime(
                                    rd["FechaResolucion"])
                    });
            }

            return lista;
        }

        private async Task<List<ProduccionMaterialEsperadoVm>> CargarMaterialesEsperadosAsync(string? filtro, int? maquinaId, SqlConnection cn)
        {
            var lista = new List<ProduccionMaterialEsperadoVm>();
            const string sql = @"
WITH MaterialesRequeridos AS
(
    SELECT
        d.SolicitudProduccionID,
        m.MaterialID AS CatalogoID,
        m.Codigo,
        m.Nombre AS Descripcion,
        COALESCE(NULLIF(LTRIM(RTRIM(m.UnidadDefault)),N''),N'KG') AS Unidad,
        CONVERT(DECIMAL(18,4),SUM(ISNULL(d.CantidadMpKg,0))) AS CantidadRequerida
    FROM dbo.SolicitudesProduccionDetalle d
    INNER JOIN dbo.ERP_Materiales m
        ON m.Activo=1
       AND UPPER(LTRIM(RTRIM(m.Codigo)))=UPPER(LTRIM(RTRIM(ISNULL(d.MaterialCodigo,N''))))
    WHERE d.Activo=1
      AND ISNULL(d.CantidadMpKg,0)>0
    GROUP BY d.SolicitudProduccionID,m.MaterialID,m.Codigo,m.Nombre,m.UnidadDefault
),
EmbalajesRequeridos AS
(
    SELECT
        d.SolicitudProduccionID,
        e.EmbalajeID AS CatalogoID,
        e.Codigo,
        e.Nombre AS Descripcion,
        COALESCE(NULLIF(LTRIM(RTRIM(e.UnidadDefault)),N''),N'PZS') AS Unidad,
        CONVERT(DECIMAL(18,4),SUM(ISNULL(d.CantidadEmbalajes,0))) AS CantidadRequerida
    FROM dbo.SolicitudesProduccionDetalle d
    INNER JOIN dbo.ERP_Embalajes e
        ON e.Activo=1
       AND UPPER(LTRIM(RTRIM(e.Codigo)))=UPPER(LTRIM(RTRIM(ISNULL(d.EmbalajeCodigo,N''))))
    WHERE d.Activo=1
      AND ISNULL(d.CantidadEmbalajes,0)>0
    GROUP BY d.SolicitudProduccionID,e.EmbalajeID,e.Codigo,e.Nombre,e.UnidadDefault
),
Esperados AS
(
    SELECT
        N'MP' AS TipoOrigen,
        r.SolicitudProduccionID,
        programa.SolicitudProduccionDetalleID,
        programa.ProgramaProduccionID,
        COALESCE(NULLIF(LTRIM(RTRIM(s.NumeroOFRecibida)),N''),NULLIF(LTRIM(RTRIM(s.FolioSolicitud)),N''),N'') AS NumeroOF,
        programa.MaquinaID,
        programa.MaquinaCodigo,
        programa.MaquinaNombre,
        programa.ParteID,
        programa.NumeroParte,
        programa.DescripcionParte,
        r.CatalogoID,
        r.Codigo,
        r.Descripcion,
        r.Unidad,
        r.CantidadRequerida,
        CONVERT(DECIMAL(18,4),ISNULL(entregado.CantidadEntregada,0)) AS CantidadEntregadaAlmacen,
        CONVERT(DECIMAL(18,4),ISNULL(confirmado.CantidadConfirmada,0)) AS CantidadConfirmadaProduccion,
        programa.FechaInicioProgramada,
        programa.Arranque
    FROM MaterialesRequeridos r
    INNER JOIN dbo.SolicitudesProduccion s
        ON s.SolicitudProduccionID=r.SolicitudProduccionID
       AND s.Activo=1
    OUTER APPLY
    (
        SELECT TOP(1)
            pp.ProgramaProduccionID,
            pp.SolicitudProduccionDetalleID,
            pp.MaquinaID,
            COALESCE(NULLIF(LTRIM(RTRIM(pp.MaquinaCodigo)),N''),maq.Codigo) AS MaquinaCodigo,
            COALESCE(NULLIF(LTRIM(RTRIM(pp.MaquinaNombre)),N''),maq.Nombre) AS MaquinaNombre,
            pp.ParteID,
            COALESCE(NULLIF(LTRIM(RTRIM(pp.ReferenciaSAP)),N''),NULLIF(LTRIM(RTRIM(pp.NumeroParte)),N'')) AS NumeroParte,
            pp.DesignacionDescripcionSAP AS DescripcionParte,
            pp.FechaInicioProgramada,
            pp.Arranque
        FROM dbo.Planeacion_ProgramaProduccion pp
        INNER JOIN dbo.SolicitudesProduccionDetalle dPrograma
            ON dPrograma.SolicitudProduccionDetalleID=pp.SolicitudProduccionDetalleID
           AND dPrograma.Activo=1
        LEFT JOIN dbo.ERP_Maquinas maq
            ON maq.MaquinaID=pp.MaquinaID
        WHERE pp.Activo=1
          AND pp.SolicitudProduccionID=r.SolicitudProduccionID
          AND pp.MaquinaID IS NOT NULL
          AND pp.FechaInicioProgramada IS NOT NULL
          AND ISNULL(pp.EstatusID,1) NOT IN(5,6,9,99)
          AND UPPER(LTRIM(RTRIM(ISNULL(dPrograma.MaterialCodigo,N''))))=UPPER(LTRIM(RTRIM(r.Codigo)))
        ORDER BY pp.FechaInicioProgramada,ISNULL(pp.SecuenciaMaquina,999999),pp.ProgramaProduccionID
    ) programa
    OUTER APPLY
    (
        SELECT SUM(CASE WHEN m.TipoMovimiento IN(N'Salida',N'Consumo') THEN m.Cantidad WHEN m.TipoMovimiento=N'Retorno' THEN -m.Cantidad ELSE 0 END) AS CantidadEntregada
        FROM dbo.AlmacenMP_Movimientos m
        WHERE m.Activo=1
          AND m.SolicitudProduccionID=r.SolicitudProduccionID
          AND COALESCE(m.MaterialSolicitadoID,m.MaterialID)=r.CatalogoID
    ) entregado
    OUTER APPLY
    (
        SELECT SUM(ISNULL(pr.CantidadRecibidaProduccion,0)) AS CantidadConfirmada
        FROM dbo.Produccion_RecepcionMateriales pr
        WHERE pr.Activo=1
          AND pr.TipoOrigen=N'MP'
          AND pr.SolicitudProduccionID=r.SolicitudProduccionID
          AND pr.MaterialSolicitadoID=r.CatalogoID
    ) confirmado
    WHERE programa.ProgramaProduccionID IS NOT NULL

    UNION ALL

    SELECT
        N'EMBALAJE',
        r.SolicitudProduccionID,
        programa.SolicitudProduccionDetalleID,
        programa.ProgramaProduccionID,
        COALESCE(NULLIF(LTRIM(RTRIM(s.NumeroOFRecibida)),N''),NULLIF(LTRIM(RTRIM(s.FolioSolicitud)),N''),N''),
        programa.MaquinaID,
        programa.MaquinaCodigo,
        programa.MaquinaNombre,
        programa.ParteID,
        programa.NumeroParte,
        programa.DescripcionParte,
        r.CatalogoID,
        r.Codigo,
        r.Descripcion,
        r.Unidad,
        r.CantidadRequerida,
        CONVERT(DECIMAL(18,4),ISNULL(entregado.CantidadEntregada,0)),
        CONVERT(DECIMAL(18,4),ISNULL(confirmado.CantidadConfirmada,0)),
        programa.FechaInicioProgramada,
        programa.Arranque
    FROM EmbalajesRequeridos r
    INNER JOIN dbo.SolicitudesProduccion s
        ON s.SolicitudProduccionID=r.SolicitudProduccionID
       AND s.Activo=1
    OUTER APPLY
    (
        SELECT TOP(1)
            pp.ProgramaProduccionID,
            pp.SolicitudProduccionDetalleID,
            pp.MaquinaID,
            COALESCE(NULLIF(LTRIM(RTRIM(pp.MaquinaCodigo)),N''),maq.Codigo) AS MaquinaCodigo,
            COALESCE(NULLIF(LTRIM(RTRIM(pp.MaquinaNombre)),N''),maq.Nombre) AS MaquinaNombre,
            pp.ParteID,
            COALESCE(NULLIF(LTRIM(RTRIM(pp.ReferenciaSAP)),N''),NULLIF(LTRIM(RTRIM(pp.NumeroParte)),N'')) AS NumeroParte,
            pp.DesignacionDescripcionSAP AS DescripcionParte,
            pp.FechaInicioProgramada,
            pp.Arranque
        FROM dbo.Planeacion_ProgramaProduccion pp
        INNER JOIN dbo.SolicitudesProduccionDetalle dPrograma
            ON dPrograma.SolicitudProduccionDetalleID=pp.SolicitudProduccionDetalleID
           AND dPrograma.Activo=1
        LEFT JOIN dbo.ERP_Maquinas maq
            ON maq.MaquinaID=pp.MaquinaID
        WHERE pp.Activo=1
          AND pp.SolicitudProduccionID=r.SolicitudProduccionID
          AND pp.MaquinaID IS NOT NULL
          AND pp.FechaInicioProgramada IS NOT NULL
          AND ISNULL(pp.EstatusID,1) NOT IN(5,6,9,99)
          AND UPPER(LTRIM(RTRIM(ISNULL(dPrograma.EmbalajeCodigo,N''))))=UPPER(LTRIM(RTRIM(r.Codigo)))
        ORDER BY pp.FechaInicioProgramada,ISNULL(pp.SecuenciaMaquina,999999),pp.ProgramaProduccionID
    ) programa
    OUTER APPLY
    (
        SELECT SUM(CASE WHEN m.TipoMovimiento IN(N'Salida',N'Consumo') THEN m.Cantidad WHEN m.TipoMovimiento=N'Retorno' THEN -m.Cantidad ELSE 0 END) AS CantidadEntregada
        FROM dbo.AlmacenEmbalajes_Movimientos m
        WHERE m.Activo=1
          AND m.SolicitudProduccionID=r.SolicitudProduccionID
          AND COALESCE(m.EmbalajeSolicitadoID,m.EmbalajeID)=r.CatalogoID
    ) entregado
    OUTER APPLY
    (
        SELECT SUM(ISNULL(pr.CantidadRecibidaProduccion,0)) AS CantidadConfirmada
        FROM dbo.Produccion_RecepcionMateriales pr
        WHERE pr.Activo=1
          AND pr.TipoOrigen=N'EMBALAJE'
          AND pr.SolicitudProduccionID=r.SolicitudProduccionID
          AND pr.EmbalajeSolicitadoID=r.CatalogoID
    ) confirmado
    WHERE programa.ProgramaProduccionID IS NOT NULL
)
SELECT
    TipoOrigen,
    SolicitudProduccionID,
    SolicitudProduccionDetalleID,
    ProgramaProduccionID,
    NumeroOF,
    MaquinaID,
    MaquinaCodigo,
    MaquinaNombre,
    ParteID,
    NumeroParte,
    DescripcionParte,
    CatalogoID,
    Codigo,
    Descripcion,
    Unidad,
    CantidadRequerida,
    CantidadEntregadaAlmacen,
    CantidadConfirmadaProduccion,
    FechaInicioProgramada,
    Arranque
FROM Esperados
WHERE FechaInicioProgramada>=DATEADD(DAY,-1,CONVERT(DATE,GETDATE()))
  AND FechaInicioProgramada<DATEADD(DAY,@DiasHorizonte+1,CONVERT(DATE,GETDATE()))
  AND (@MaquinaID IS NULL OR MaquinaID=@MaquinaID)
  AND
  (
      @Filtro IS NULL
      OR NumeroOF LIKE N'%'+@Filtro+N'%'
      OR Codigo LIKE N'%'+@Filtro+N'%'
      OR Descripcion LIKE N'%'+@Filtro+N'%'
      OR NumeroParte LIKE N'%'+@Filtro+N'%'
      OR DescripcionParte LIKE N'%'+@Filtro+N'%'
      OR MaquinaCodigo LIKE N'%'+@Filtro+N'%'
      OR MaquinaNombre LIKE N'%'+@Filtro+N'%'
  )
  AND CantidadRequerida>0.0005
  AND CantidadEntregadaAlmacen+0.0005<CantidadRequerida
ORDER BY FechaInicioProgramada,MaquinaCodigo,NumeroOF,CASE WHEN TipoOrigen=N'MP' THEN 1 ELSE 2 END,Codigo;";

            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@Filtro", SqlDbType.NVarChar, 200).Value = string.IsNullOrWhiteSpace(filtro) ? DBNull.Value : filtro.Trim();
            cmd.Parameters.Add("@MaquinaID", SqlDbType.Int).Value = maquinaId.HasValue && maquinaId.Value > 0 ? maquinaId.Value : DBNull.Value;
            cmd.Parameters.Add("@DiasHorizonte", SqlDbType.Int).Value = PreparacionDiasHorizonte;

            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                var fechaInicio = rd["FechaInicioProgramada"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(rd["FechaInicioProgramada"]);
                var arranque = rd["Arranque"] == DBNull.Value ? (TimeSpan?)null : (TimeSpan)rd["Arranque"];
                DateTime? fechaArranque = null;
                if (fechaInicio.HasValue) fechaArranque = ConstruirFechaPreparacion(fechaInicio.Value, arranque);

                lista.Add(new ProduccionMaterialEsperadoVm
                {
                    TipoOrigen = rd["TipoOrigen"]?.ToString()?.Trim() ?? string.Empty,
                    SolicitudProduccionID = Convert.ToInt32(rd["SolicitudProduccionID"]),
                    SolicitudProduccionDetalleID = rd["SolicitudProduccionDetalleID"] == DBNull.Value ? null : Convert.ToInt32(rd["SolicitudProduccionDetalleID"]),
                    ProgramaProduccionID = rd["ProgramaProduccionID"] == DBNull.Value ? null : Convert.ToInt32(rd["ProgramaProduccionID"]),
                    NumeroOF = rd["NumeroOF"]?.ToString()?.Trim() ?? string.Empty,
                    MaquinaID = rd["MaquinaID"] == DBNull.Value ? null : Convert.ToInt32(rd["MaquinaID"]),
                    MaquinaCodigo = rd["MaquinaCodigo"] == DBNull.Value ? null : rd["MaquinaCodigo"]?.ToString()?.Trim(),
                    MaquinaNombre = rd["MaquinaNombre"] == DBNull.Value ? null : rd["MaquinaNombre"]?.ToString()?.Trim(),
                    ParteID = rd["ParteID"] == DBNull.Value ? null : Convert.ToInt32(rd["ParteID"]),
                    NumeroParte = rd["NumeroParte"] == DBNull.Value ? null : rd["NumeroParte"]?.ToString()?.Trim(),
                    DescripcionParte = rd["DescripcionParte"] == DBNull.Value ? null : rd["DescripcionParte"]?.ToString()?.Trim(),
                    CatalogoID = Convert.ToInt32(rd["CatalogoID"]),
                    Codigo = rd["Codigo"]?.ToString()?.Trim() ?? string.Empty,
                    Descripcion = rd["Descripcion"] == DBNull.Value ? null : rd["Descripcion"]?.ToString()?.Trim(),
                    Unidad = rd["Unidad"]?.ToString()?.Trim() ?? string.Empty,
                    CantidadRequerida = Convert.ToDecimal(rd["CantidadRequerida"]),
                    CantidadEntregadaAlmacen = Convert.ToDecimal(rd["CantidadEntregadaAlmacen"]),
                    CantidadConfirmadaProduccion = Convert.ToDecimal(rd["CantidadConfirmadaProduccion"]),
                    FechaArranque = fechaArranque
                });
            }

            return lista;
        }

        private static async Task<int?>
            ResolverProgramaRecepcionMaterialAsync(
                int solicitudProduccionId,
                int? solicitudProduccionDetalleId,
                SqlConnection cn,
                SqlTransaction tx)
        {
            const string sql = @"
SELECT
    CASE
        WHEN COUNT(1)=1
            THEN MAX(ProgramaProduccionID)
        ELSE NULL
    END
FROM dbo.Planeacion_ProgramaProduccion WITH(UPDLOCK,HOLDLOCK)
WHERE Activo=1
  AND SolicitudProduccionID=@SolicitudProduccionID
  AND
  (
      @SolicitudProduccionDetalleID IS NULL
      OR SolicitudProduccionDetalleID=@SolicitudProduccionDetalleID
  )
  AND ISNULL(EstatusID,1) NOT IN(5,6,9,99);";

            await using var cmd =
                new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add(
                "@SolicitudProduccionID",
                SqlDbType.Int).Value =
                solicitudProduccionId;

            cmd.Parameters.Add(
                "@SolicitudProduccionDetalleID",
                SqlDbType.Int).Value =
                solicitudProduccionDetalleId.HasValue
                    ? solicitudProduccionDetalleId.Value
                    : DBNull.Value;

            var valor =
                await cmd.ExecuteScalarAsync();

            if (valor == null ||
                valor == DBNull.Value)
                return null;

            return Convert.ToInt32(valor);
        }

   
        private static async Task
            ActualizarValidacionMovimientoAlmacenAsync(
                string tipoOrigen,
                long movimientoAlmacenId,
                int solicitudProduccionId,
                bool validadoCompleto,
                SqlConnection cn,
                SqlTransaction tx)
        {
            string sql;

            if (string.Equals(
                    tipoOrigen,
                    ProduccionRecepcionMaterialTipo.MP,
                    StringComparison.OrdinalIgnoreCase))
            {
                sql = @"
UPDATE dbo.AlmacenMP_Movimientos
SET
    RequiereValidacionProduccion=1,
    ValidadoProduccion=@ValidadoProduccion
WHERE MovimientoID=@MovimientoID
  AND Activo=1
  AND SolicitudProduccionID=@SolicitudProduccionID;

IF @@ROWCOUNT<>1
    THROW 51202,'No fue posible localizar el movimiento de materia prima relacionado.',1;";
            }
            else if (string.Equals(
                         tipoOrigen,
                         ProduccionRecepcionMaterialTipo.Embalaje,
                         StringComparison.OrdinalIgnoreCase))
            {
                sql = @"
UPDATE dbo.AlmacenEmbalajes_Movimientos
SET
    RequiereValidacionProduccion=1,
    ValidadoProduccion=@ValidadoProduccion
WHERE MovimientoID=@MovimientoID
  AND Activo=1
  AND SolicitudProduccionID=@SolicitudProduccionID;

IF @@ROWCOUNT<>1
    THROW 51203,'No fue posible localizar el movimiento de embalaje relacionado.',1;";
            }
            else
            {
                throw new InvalidOperationException(
                    "El tipo de material recibido no es válido.");
            }

            await using var cmd =
                new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add(
                "@MovimientoID",
                SqlDbType.BigInt).Value =
                movimientoAlmacenId;

            cmd.Parameters.Add(
                "@SolicitudProduccionID",
                SqlDbType.Int).Value =
                solicitudProduccionId;

            cmd.Parameters.Add(
                "@ValidadoProduccion",
                SqlDbType.Bit).Value =
                validadoCompleto;

            await cmd.ExecuteNonQueryAsync();
        }

        private static void AplicarRelacionesLhRhMaterialesEsperados(List<ProduccionMaterialEsperadoVm> materiales, Dictionary<int, RelacionMaterialLhRhInterna> relaciones)
        {
            if (materiales == null || materiales.Count == 0 || relaciones.Count == 0) return;
            foreach (var item in materiales)
            {
                if (!item.ProgramaProduccionID.HasValue || item.ProgramaProduccionID.Value <= 0) continue;
                if (!relaciones.TryGetValue(item.ProgramaProduccionID.Value, out var relacion)) continue;
                item.GrupoLhRh = relacion.GrupoLhRh;
                item.ProgramaParejaID = relacion.ProgramaParejaID;
                item.EjecucionParejaID = relacion.EjecucionParejaID;
                item.NumeroOFPareja = relacion.NumeroOFPareja;
                item.ParteParejaID = relacion.ParteParejaID;
                item.NumeroPartePareja = relacion.NumeroPartePareja;
                item.ReferenciaSAPPareja = relacion.ReferenciaSAPPareja;
                item.DescripcionPartePareja = relacion.DescripcionPartePareja;
                var ladoActual = DeterminarLadoLhRhMaterial(item.NumeroParte, item.DescripcionParte);
                var ladoPareja = DeterminarLadoLhRhMaterial(relacion.NumeroPartePareja, relacion.ReferenciaSAPPareja, relacion.DescripcionPartePareja);
                if (string.IsNullOrWhiteSpace(ladoActual) && string.Equals(ladoPareja, "LH", StringComparison.OrdinalIgnoreCase)) ladoActual = "RH";
                else if (string.IsNullOrWhiteSpace(ladoActual) && string.Equals(ladoPareja, "RH", StringComparison.OrdinalIgnoreCase)) ladoActual = "LH";
                item.LadoLhRh = ladoActual;
            }
        }

        private static void AplicarRelacionesLhRhRecepciones(List<ProduccionRecepcionMaterialVm> recepciones, Dictionary<int, RelacionMaterialLhRhInterna> relaciones)
        {
            if (recepciones == null || recepciones.Count == 0 || relaciones.Count == 0) return;
            foreach (var item in recepciones)
            {
                if (!item.ProgramaProduccionID.HasValue || item.ProgramaProduccionID.Value <= 0) continue;
                if (!relaciones.TryGetValue(item.ProgramaProduccionID.Value, out var relacion)) continue;
                item.GrupoLhRh = relacion.GrupoLhRh;
                item.ProgramaParejaID = relacion.ProgramaParejaID;
                item.EjecucionParejaID = relacion.EjecucionParejaID;
                item.NumeroOFPareja = relacion.NumeroOFPareja;
                item.ParteParejaID = relacion.ParteParejaID;
                item.NumeroPartePareja = relacion.NumeroPartePareja;
                item.ReferenciaSAPPareja = relacion.ReferenciaSAPPareja;
                item.DescripcionPartePareja = relacion.DescripcionPartePareja;
                var ladoActual = DeterminarLadoLhRhMaterial(item.NumeroParte, item.DescripcionParte);
                var ladoPareja = DeterminarLadoLhRhMaterial(relacion.NumeroPartePareja, relacion.ReferenciaSAPPareja, relacion.DescripcionPartePareja);
                if (string.IsNullOrWhiteSpace(ladoActual) && string.Equals(ladoPareja, "LH", StringComparison.OrdinalIgnoreCase)) ladoActual = "RH";
                else if (string.IsNullOrWhiteSpace(ladoActual) && string.Equals(ladoPareja, "RH", StringComparison.OrdinalIgnoreCase)) ladoActual = "LH";
                item.LadoLhRh = ladoActual;
            }
        }

        private static string? DeterminarLadoLhRhMaterial(params string?[] valores)
        {
            var tieneLh = false;
            var tieneRh = false;
            foreach (var valor in valores)
            {
                if (string.IsNullOrWhiteSpace(valor)) continue;
                var texto = valor.Trim().ToUpperInvariant();
                if (texto.Contains("LH/RH", StringComparison.Ordinal) || texto.Contains("RH/LH", StringComparison.Ordinal)) continue;
                if (System.Text.RegularExpressions.Regex.IsMatch(texto, @"(?<![A-Z0-9])LH(?![A-Z0-9])", System.Text.RegularExpressions.RegexOptions.CultureInvariant)) tieneLh = true;
                if (System.Text.RegularExpressions.Regex.IsMatch(texto, @"(?<![A-Z0-9])RH(?![A-Z0-9])", System.Text.RegularExpressions.RegexOptions.CultureInvariant)) tieneRh = true;
            }
            if (tieneLh == tieneRh) return null;
            return tieneLh ? "LH" : "RH";
        }

        private static async Task<bool>
            AutoConfirmarPreparacionEmbalajeAsync(
                int programaProduccionId,
                int solicitudProduccionId,
                int? solicitudProduccionDetalleId,
                int embalajeSolicitadoId,
                int usuarioId,
                SqlConnection cn,
                SqlTransaction tx)
        {
            const string sqlTotales = @"
DECLARE @Codigo NVARCHAR(80);
DECLARE @Requerido DECIMAL(18,4)=0;
DECLARE @Recibido DECIMAL(18,4)=0;

SELECT
    @Codigo=Codigo
FROM dbo.ERP_Embalajes
WHERE EmbalajeID=@EmbalajeSolicitadoID
  AND Activo=1;

IF @Codigo IS NULL
BEGIN
    SELECT
        CONVERT(DECIMAL(18,4),0) AS Requerido,
        CONVERT(DECIMAL(18,4),0) AS Recibido;
    RETURN;
END;

SELECT
    @Requerido=
        CONVERT
        (
            DECIMAL(18,4),
            ISNULL
            (
                SUM(ISNULL(d.CantidadEmbalajes,0)),
                0
            )
        )
FROM dbo.SolicitudesProduccionDetalle d
WHERE d.SolicitudProduccionID=@SolicitudProduccionID
  AND d.Activo=1
  AND
  (
      @SolicitudProduccionDetalleID IS NULL
      OR d.SolicitudProduccionDetalleID=@SolicitudProduccionDetalleID
  )
  AND UPPER(LTRIM(RTRIM(ISNULL(d.EmbalajeCodigo,N''))))=
      UPPER(LTRIM(RTRIM(@Codigo)));

SELECT
    @Recibido=
        CONVERT
        (
            DECIMAL(18,4),
            ISNULL
            (
                SUM(ISNULL(r.CantidadRecibidaProduccion,0)),
                0
            )
        )
FROM dbo.Produccion_RecepcionMateriales r
WHERE r.Activo=1
  AND r.TipoOrigen=N'EMBALAJE'
  AND r.SolicitudProduccionID=@SolicitudProduccionID
  AND r.EmbalajeSolicitadoID=@EmbalajeSolicitadoID
  AND r.EstadoRecepcion IN
      (
          N'RECIBIDO_COMPLETO',
          N'RECIBIDO_PARCIAL'
      )
  AND
  (
      @SolicitudProduccionDetalleID IS NULL
      OR r.SolicitudProduccionDetalleID=@SolicitudProduccionDetalleID
      OR r.SolicitudProduccionDetalleID IS NULL
  );

SELECT
    @Requerido AS Requerido,
    @Recibido AS Recibido;";

            decimal requerido;
            decimal recibido;

            await using (var cmd =
                new SqlCommand(sqlTotales, cn, tx))
            {
                cmd.Parameters.Add(
                    "@SolicitudProduccionID",
                    SqlDbType.Int).Value =
                    solicitudProduccionId;

                cmd.Parameters.Add(
                    "@SolicitudProduccionDetalleID",
                    SqlDbType.Int).Value =
                    solicitudProduccionDetalleId.HasValue
                        ? solicitudProduccionDetalleId.Value
                        : DBNull.Value;

                cmd.Parameters.Add(
                    "@EmbalajeSolicitadoID",
                    SqlDbType.Int).Value =
                    embalajeSolicitadoId;

                await using var rd =
                    await cmd.ExecuteReaderAsync();

                if (!await rd.ReadAsync())
                    return false;

                requerido =
                    Convert.ToDecimal(
                        rd["Requerido"]);

                recibido =
                    Convert.ToDecimal(
                        rd["Recibido"]);
            }

            if (requerido <= 0)
                return false;

            if (recibido + 0.0005m < requerido)
                return false;

            const string sqlConfirmar = @"
UPDATE dbo.Produccion_PreparacionAnticipada
SET
    Estado=@EstadoConfirmada,
    UsuarioConfirmacionID=@UsuarioID,
    FechaConfirmacion=SYSDATETIME(),

    Observaciones=
        LEFT
        (
            CASE
                WHEN Observaciones IS NULL
                  OR LTRIM(RTRIM(Observaciones))=N''
                    THEN @Observaciones
                ELSE
                    Observaciones+
                    CHAR(13)+CHAR(10)+
                    @Observaciones
            END,
            500
        ),

    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=SYSDATETIME()

WHERE ProgramaProduccionID=@ProgramaProduccionID
  AND TipoTarea=@TipoTarea
  AND Estado=@EstadoPendiente
  AND Activo=1;";

            await using var cmdConfirmar =
                new SqlCommand(
                    sqlConfirmar,
                    cn,
                    tx);

            cmdConfirmar.Parameters.Add(
                "@EstadoConfirmada",
                SqlDbType.NVarChar,
                30).Value =
                ProduccionPreparacionEstado.Confirmada;

            cmdConfirmar.Parameters.Add(
                "@UsuarioID",
                SqlDbType.Int).Value =
                usuarioId;

            cmdConfirmar.Parameters.Add(
                "@Observaciones",
                SqlDbType.NVarChar,
                500).Value =
                $"Embalaje confirmado automáticamente por recepción física en Producción. Requerido: {requerido:0.####}; recibido confirmado: {recibido:0.####}.";

            cmdConfirmar.Parameters.Add(
                "@ProgramaProduccionID",
                SqlDbType.Int).Value =
                programaProduccionId;

            cmdConfirmar.Parameters.Add(
                "@TipoTarea",
                SqlDbType.NVarChar,
                40).Value =
                ProduccionPreparacionTipo.PrepararEmbalaje;

            cmdConfirmar.Parameters.Add(
                "@EstadoPendiente",
                SqlDbType.NVarChar,
                30).Value =
                ProduccionPreparacionEstado.Pendiente;

            var filas =
                await cmdConfirmar.ExecuteNonQueryAsync();

            return filas > 0;
        }

       

        private static async Task
            AgregarHistorialRecepcionMaterialAsync(
                long recepcionMaterialId,
                string evento,
                string? estadoAnterior,
                string? estadoNuevo,
                decimal? cantidadAnterior,
                decimal? cantidadNueva,
                string? aclaracionAnterior,
                string? aclaracionNueva,
                string? comentario,
                int usuarioId,
                SqlConnection cn,
                SqlTransaction tx)
        {
            if (string.IsNullOrWhiteSpace(evento))
                throw new ArgumentException(
                    "El evento del historial es obligatorio.",
                    nameof(evento));

            evento =
                evento.Trim();

            if (evento.Length > 50)
                evento = evento[..50];

            comentario =
                string.IsNullOrWhiteSpace(comentario)
                    ? null
                    : comentario.Trim();

            if (comentario?.Length > 1000)
                comentario =
                    comentario[..1000];

            const string sql = @"
INSERT INTO dbo.Produccion_RecepcionMaterialesHistorial
(
    RecepcionMaterialID,
    Evento,
    EstadoRecepcionAnterior,
    EstadoRecepcionNuevo,
    CantidadRecibidaAnterior,
    CantidadRecibidaNueva,
    EstadoAclaracionAnterior,
    EstadoAclaracionNuevo,
    Comentario,
    UsuarioID,
    FechaEvento
)
VALUES
(
    @RecepcionMaterialID,
    @Evento,
    @EstadoRecepcionAnterior,
    @EstadoRecepcionNuevo,
    @CantidadRecibidaAnterior,
    @CantidadRecibidaNueva,
    @EstadoAclaracionAnterior,
    @EstadoAclaracionNuevo,
    @Comentario,
    @UsuarioID,
    SYSDATETIME()
);";

            await using var cmd =
                new SqlCommand(sql, cn, tx);

            cmd.Parameters.Add(
                "@RecepcionMaterialID",
                SqlDbType.BigInt).Value =
                recepcionMaterialId;

            cmd.Parameters.Add(
                "@Evento",
                SqlDbType.NVarChar,
                50).Value =
                evento;

            cmd.Parameters.Add(
                "@EstadoRecepcionAnterior",
                SqlDbType.NVarChar,
                30).Value =
                string.IsNullOrWhiteSpace(estadoAnterior)
                    ? DBNull.Value
                    : estadoAnterior;

            cmd.Parameters.Add(
                "@EstadoRecepcionNuevo",
                SqlDbType.NVarChar,
                30).Value =
                string.IsNullOrWhiteSpace(estadoNuevo)
                    ? DBNull.Value
                    : estadoNuevo;

            var pCantidadAnterior =
                cmd.Parameters.Add(
                    "@CantidadRecibidaAnterior",
                    SqlDbType.Decimal);

            pCantidadAnterior.Precision = 18;
            pCantidadAnterior.Scale = 4;
            pCantidadAnterior.Value =
                cantidadAnterior.HasValue
                    ? cantidadAnterior.Value
                    : DBNull.Value;

            var pCantidadNueva =
                cmd.Parameters.Add(
                    "@CantidadRecibidaNueva",
                    SqlDbType.Decimal);

            pCantidadNueva.Precision = 18;
            pCantidadNueva.Scale = 4;
            pCantidadNueva.Value =
                cantidadNueva.HasValue
                    ? cantidadNueva.Value
                    : DBNull.Value;

            cmd.Parameters.Add(
                "@EstadoAclaracionAnterior",
                SqlDbType.NVarChar,
                20).Value =
                string.IsNullOrWhiteSpace(aclaracionAnterior)
                    ? DBNull.Value
                    : aclaracionAnterior;

            cmd.Parameters.Add(
                "@EstadoAclaracionNuevo",
                SqlDbType.NVarChar,
                20).Value =
                string.IsNullOrWhiteSpace(aclaracionNueva)
                    ? DBNull.Value
                    : aclaracionNueva;

            cmd.Parameters.Add(
                "@Comentario",
                SqlDbType.NVarChar,
                1000).Value =
                string.IsNullOrWhiteSpace(comentario)
                    ? DBNull.Value
                    : comentario;

            cmd.Parameters.Add(
                "@UsuarioID",
                SqlDbType.Int).Value =
                usuarioId;

            await cmd.ExecuteNonQueryAsync();
        }

        private static string
            ConstruirComentarioRecepcionMaterial(
                string tipoOrigen,
                string codigo,
                decimal cantidadEntregada,
                decimal cantidadRecibida,
                string unidad,
                string estado,
                string? motivo)
        {
            var tipo =
                string.Equals(
                    tipoOrigen,
                    ProduccionRecepcionMaterialTipo.MP,
                    StringComparison.OrdinalIgnoreCase)
                    ? "Materia prima"
                    : "Embalaje";

            var comentario =
                $"{tipo} {codigo}. " +
                $"Almacén reportó {cantidadEntregada:0.####} {unidad}; " +
                $"Producción confirmó {cantidadRecibida:0.####} {unidad}. " +
                $"Resultado: {estado}.";

            if (!string.IsNullOrWhiteSpace(motivo))
                comentario +=
                    $" Motivo de diferencia: {motivo.Trim()}.";

            if (comentario.Length > 1000)
                comentario =
                    comentario[..1000];

            return comentario;
        }

        private sealed class RelacionMaterialLhRhInterna
        {
            public int? GrupoLhRh { get; set; }
            public int ProgramaParejaID { get; set; }
            public int? EjecucionParejaID { get; set; }
            public string? NumeroOFPareja { get; set; }
            public int? ParteParejaID { get; set; }
            public string? NumeroPartePareja { get; set; }
            public string? ReferenciaSAPPareja { get; set; }
            public string? DescripcionPartePareja { get; set; }
        }
    }
}