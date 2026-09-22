using ERP.NSQuell.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace ERP.NSQuell.Controllers
{
    // NSQ_PRODUCCION_RECEPCIONES_PENDIENTES_AGRUPADAS_V1_2
    public sealed partial class ProduccionPreparacionController
    {
        private static string NormalizarClaveRecepcionAgrupada(string? valor) =>
            (valor ?? string.Empty).Trim().ToUpperInvariant();

        private static List<ProduccionRecepcionMaterialVm>
            AgruparRecepcionesPendientesParaVista(
                List<ProduccionRecepcionMaterialVm> recepciones)
        {
            if (recepciones == null || recepciones.Count == 0)
                return recepciones ?? new List<ProduccionRecepcionMaterialVm>();

            var historicas =
                recepciones
                    .Where(x => !x.EstaPendiente)
                    .ToList();

            var pendientes =
                recepciones
                    .Where(x => x.EstaPendiente)
                    .GroupBy(
                        x => new
                        {
                            x.SolicitudProduccionID,
                            x.SolicitudProduccionDetalleID,
                            TipoOrigen = NormalizarClaveRecepcionAgrupada(x.TipoOrigen),
                            x.MaterialSolicitadoID,
                            x.MaterialEntregadoID,
                            x.EmbalajeSolicitadoID,
                            x.EmbalajeEntregadoID,
                            CodigoEntregado = NormalizarClaveRecepcionAgrupada(x.CodigoEntregado),
                            TipoMP = NormalizarClaveRecepcionAgrupada(x.TipoMP),
                            Lote = NormalizarClaveRecepcionAgrupada(x.Lote),
                            Unidad = NormalizarClaveRecepcionAgrupada(x.Unidad),
                            x.MaquinaID,
                            x.ParteID
                        })
                    .Select(
                        grupo =>
                        {
                            var entregas =
                                grupo
                                    .OrderBy(x => x.FechaEntregaAlmacen)
                                    .ThenBy(x => x.RecepcionMaterialID)
                                    .ToList();

                            var principal = entregas[0];

                            principal.EntregasAgrupadas =
                                entregas
                                    .Select(
                                        x => new ProduccionRecepcionMaterialEntregaAgrupadaVm
                                        {
                                            RecepcionMaterialID = x.RecepcionMaterialID,
                                            CantidadEntregadaAlmacen = x.CantidadEntregadaAlmacen,
                                            FechaEntregaAlmacen = x.FechaEntregaAlmacen,
                                            UsuarioEntregaAlmacenNombre = x.UsuarioEntregaAlmacenNombre,
                                            ReferenciaOperacion = x.ReferenciaOperacion
                                        })
                                    .ToList();

                            principal.CantidadEntregadaAlmacen =
                                entregas.Sum(x => x.CantidadEntregadaAlmacen);

                            principal.FechaEntregaAlmacen =
                                entregas.Min(x => x.FechaEntregaAlmacen);

                            principal.FechaUltimaEntregaAlmacen =
                                entregas.Max(x => x.FechaEntregaAlmacen);

                            return principal;
                        })
                    .OrderBy(x => x.FechaEntregaAlmacen)
                    .ThenBy(x => x.NumeroOF)
                    .ThenBy(x => x.EsMateriaPrima ? 0 : 1)
                    .ThenBy(x => x.CodigoEntregado)
                    .ToList();

            return pendientes.Concat(historicas).ToList();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmarRecepcionesMaterial(
            string? RecepcionMaterialIDs,
            string? Decision,
            decimal? CantidadRecibida,
            string? MotivoDiferencia,
            string? Observaciones)
        {
            if (!UsuarioEnSesion())
                return RedirectToAction("Login", "Login");

            var ids =
                (RecepcionMaterialIDs ?? string.Empty)
                    .Split(
                        ',',
                        StringSplitOptions.RemoveEmptyEntries |
                        StringSplitOptions.TrimEntries)
                    .Select(x => long.TryParse(x, out var id) ? id : 0L)
                    .Where(x => x > 0)
                    .Distinct()
                    .ToList();

            if (ids.Count == 0)
            {
                TempData["Error"] =
                    "No se recibieron correctamente las entregas que deseas confirmar.";
                return RedirectToAction(nameof(Materiales));
            }

            var decision =
                string.IsNullOrWhiteSpace(Decision)
                    ? string.Empty
                    : Decision.Trim().ToUpperInvariant();

            var motivo =
                string.IsNullOrWhiteSpace(MotivoDiferencia)
                    ? null
                    : MotivoDiferencia.Trim();

            var observaciones =
                string.IsNullOrWhiteSpace(Observaciones)
                    ? null
                    : Observaciones.Trim();

            // Para una sola recepcion reutiliza la Action canonica existente.
            if (ids.Count == 1)
            {
                return await ConfirmarRecepcionMaterial(
                    new ProduccionConfirmarRecepcionMaterialVm
                    {
                        RecepcionMaterialID = ids[0],
                        Decision = decision,
                        CantidadRecibida = CantidadRecibida,
                        MotivoDiferencia = motivo,
                        Observaciones = observaciones
                    });
            }

            if (decision != ProduccionRecepcionMaterialDecision.Completo &&
                decision != ProduccionRecepcionMaterialDecision.Parcial &&
                decision != ProduccionRecepcionMaterialDecision.NoRecibido)
            {
                TempData["Error"] =
                    "Selecciona si recibiste completo, parcialmente o no recibiste el material.";
                return RedirectToAction(nameof(Materiales));
            }

            if (motivo?.Length > 500)
            {
                TempData["Error"] =
                    "El motivo de la diferencia no puede superar 500 caracteres.";
                return RedirectToAction(nameof(Materiales));
            }

            if (observaciones?.Length > 800)
            {
                TempData["Error"] =
                    "Las observaciones no pueden superar 800 caracteres.";
                return RedirectToAction(nameof(Materiales));
            }

            var usuarioId = ObtenerUsuarioID();

            await using var cn =
                new SqlConnection(ConnectionString);

            await cn.OpenAsync();

            var permisos =
                await ObtenerPermisosPreparacionUsuarioAsync(
                    usuarioId,
                    cn);

            if (!permisos.PuedeGestionarEmbalaje)
                return StatusCode(StatusCodes.Status403Forbidden);

            await using var tx =
                (SqlTransaction)await cn.BeginTransactionAsync(
                    IsolationLevel.Serializable);

            try
            {
                var filas =
                    await CargarRecepcionesAgrupadasParaConfirmacionAsync(
                        ids,
                        cn,
                        tx);

                if (filas.Count != ids.Count)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] =
                        "Una o mas entregas del grupo ya no existen.";
                    return RedirectToAction(nameof(Materiales));
                }

                if (filas.Any(
                        x => !string.Equals(
                            x.EstadoRecepcion,
                            ProduccionRecepcionMaterialEstado.Pendiente,
                            StringComparison.OrdinalIgnoreCase)))
                {
                    await tx.RollbackAsync();
                    TempData["Warning"] =
                        "Una entrega del grupo ya fue confirmada. Actualiza la pantalla.";
                    return RedirectToAction(nameof(Materiales));
                }

                var primera = filas[0];

                if (filas.Any(x => !MismaClaveRecepcionAgrupada(primera, x)))
                {
                    await tx.RollbackAsync();
                    TempData["Error"] =
                        "Las entregas ya no corresponden al mismo material, lote u OF.";
                    return RedirectToAction(nameof(Materiales));
                }

                var totalEntregado =
                    filas.Sum(x => x.CantidadEntregadaAlmacen);

                if (totalEntregado <= 0.0005m)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] =
                        "La cantidad acumulada de Almacen no es valida.";
                    return RedirectToAction(nameof(Materiales));
                }

                decimal totalRecibido;

                if (decision == ProduccionRecepcionMaterialDecision.Completo)
                {
                    totalRecibido = totalEntregado;
                    motivo = null;
                }
                else if (decision == ProduccionRecepcionMaterialDecision.Parcial)
                {
                    if (!CantidadRecibida.HasValue ||
                        CantidadRecibida.Value <= 0.0005m)
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] =
                            "Indica una cantidad recibida mayor a cero.";
                        return RedirectToAction(nameof(Materiales));
                    }

                    totalRecibido = CantidadRecibida.Value;

                    if (totalRecibido >= totalEntregado)
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] =
                            $"Para recepcion parcial indica menos de {totalEntregado:0.####} {primera.Unidad}.";
                        return RedirectToAction(nameof(Materiales));
                    }

                    if (string.IsNullOrWhiteSpace(motivo))
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] =
                            "Indica el motivo de la diferencia.";
                        return RedirectToAction(nameof(Materiales));
                    }
                }
                else
                {
                    totalRecibido = 0m;

                    if (string.IsNullOrWhiteSpace(motivo))
                    {
                        await tx.RollbackAsync();
                        TempData["Error"] =
                            "Indica por que no recibiste el material.";
                        return RedirectToAction(nameof(Materiales));
                    }
                }

                var restante = totalRecibido;
                var materialEnviadoASecado = false;
                var embalajeAutoConfirmado = false;

                foreach (var fila in filas)
                {
                    decimal cantidadFila;
                    string nuevoEstado;
                    string nuevoEstadoAclaracion;

                    if (decision == ProduccionRecepcionMaterialDecision.Completo)
                    {
                        cantidadFila = fila.CantidadEntregadaAlmacen;
                        nuevoEstado =
                            ProduccionRecepcionMaterialEstado.RecibidoCompleto;
                        nuevoEstadoAclaracion =
                            ProduccionRecepcionMaterialEstadoAclaracion.NoAplica;
                    }
                    else if (decision == ProduccionRecepcionMaterialDecision.NoRecibido)
                    {
                        cantidadFila = 0m;
                        nuevoEstado =
                            ProduccionRecepcionMaterialEstado.NoRecibido;
                        nuevoEstadoAclaracion =
                            ProduccionRecepcionMaterialEstadoAclaracion.Pendiente;
                    }
                    else if (restante >= fila.CantidadEntregadaAlmacen)
                    {
                        cantidadFila = fila.CantidadEntregadaAlmacen;
                        restante -= fila.CantidadEntregadaAlmacen;
                        nuevoEstado =
                            ProduccionRecepcionMaterialEstado.RecibidoCompleto;
                        nuevoEstadoAclaracion =
                            ProduccionRecepcionMaterialEstadoAclaracion.NoAplica;
                    }
                    else if (restante > 0.0005m)
                    {
                        cantidadFila = restante;
                        restante = 0m;
                        nuevoEstado =
                            ProduccionRecepcionMaterialEstado.RecibidoParcial;
                        nuevoEstadoAclaracion =
                            ProduccionRecepcionMaterialEstadoAclaracion.Pendiente;
                    }
                    else
                    {
                        cantidadFila = 0m;
                        nuevoEstado =
                            ProduccionRecepcionMaterialEstado.NoRecibido;
                        nuevoEstadoAclaracion =
                            ProduccionRecepcionMaterialEstadoAclaracion.Pendiente;
                    }

                    var programaProduccionId =
                        fila.ProgramaProduccionID;

                    if (!programaProduccionId.HasValue ||
                        programaProduccionId.Value <= 0)
                    {
                        programaProduccionId =
                            await ResolverProgramaRecepcionMaterialAsync(
                                fila.SolicitudProduccionID,
                                fila.SolicitudProduccionDetalleID,
                                cn,
                                tx);
                    }

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
  AND EstadoRecepcion=N'PENDIENTE';

IF @@ROWCOUNT<>1
    THROW 51261,N'Una entrega agrupada cambio de estado mientras se confirmaba.',1;";

                    await using (var cmd =
                        new SqlCommand(sqlActualizar, cn, tx))
                    {
                        cmd.Parameters.Add(
                            "@ProgramaProduccionID",
                            SqlDbType.Int).Value =
                            programaProduccionId.HasValue
                                ? programaProduccionId.Value
                                : DBNull.Value;

                        cmd.Parameters.Add(
                            "@EstadoRecepcion",
                            SqlDbType.NVarChar,
                            30).Value = nuevoEstado;

                        var pCantidad =
                            cmd.Parameters.Add(
                                "@CantidadRecibidaProduccion",
                                SqlDbType.Decimal);

                        pCantidad.Precision = 18;
                        pCantidad.Scale = 4;
                        pCantidad.Value = cantidadFila;

                        cmd.Parameters.Add(
                            "@MotivoDiferencia",
                            SqlDbType.NVarChar,
                            500).Value =
                            string.Equals(
                                nuevoEstado,
                                ProduccionRecepcionMaterialEstado.RecibidoCompleto,
                                StringComparison.OrdinalIgnoreCase)
                                ? DBNull.Value
                                : string.IsNullOrWhiteSpace(motivo)
                                    ? DBNull.Value
                                    : motivo;

                        cmd.Parameters.Add(
                            "@ObservacionesRecepcion",
                            SqlDbType.NVarChar,
                            800).Value =
                            string.IsNullOrWhiteSpace(observaciones)
                                ? DBNull.Value
                                : observaciones;

                        cmd.Parameters.Add(
                            "@UsuarioID",
                            SqlDbType.Int).Value = usuarioId;

                        cmd.Parameters.Add(
                            "@EstadoAclaracion",
                            SqlDbType.NVarChar,
                            20).Value = nuevoEstadoAclaracion;

                        cmd.Parameters.Add(
                            "@RecepcionMaterialID",
                            SqlDbType.BigInt).Value = fila.RecepcionMaterialID;

                        await cmd.ExecuteNonQueryAsync();
                    }

                    var validacionCompleta =
                        string.Equals(
                            nuevoEstado,
                            ProduccionRecepcionMaterialEstado.RecibidoCompleto,
                            StringComparison.OrdinalIgnoreCase);

                    await ActualizarValidacionMovimientoAlmacenAsync(
                        fila.TipoOrigen,
                        fila.MovimientoAlmacenID,
                        fila.SolicitudProduccionID,
                        validacionCompleta,
                        cn,
                        tx);

                    var comentario =
                        ConstruirComentarioRecepcionMaterial(
                            fila.TipoOrigen,
                            fila.CodigoEntregado,
                            fila.CantidadEntregadaAlmacen,
                            cantidadFila,
                            fila.Unidad,
                            nuevoEstado,
                            validacionCompleta ? null : motivo);

                    await AgregarHistorialRecepcionMaterialAsync(
                        fila.RecepcionMaterialID,
                        "CONFIRMACION_PRODUCCION_AGRUPADA",
                        fila.EstadoRecepcion,
                        nuevoEstado,
                        null,
                        cantidadFila,
                        fila.EstadoAclaracion,
                        nuevoEstadoAclaracion,
                        comentario,
                        usuarioId,
                        cn,
                        tx);

                    if (string.Equals(
                            fila.TipoOrigen,
                            ProduccionRecepcionMaterialTipo.MP,
                            StringComparison.OrdinalIgnoreCase) &&
                        cantidadFila > 0.0005m)
                    {
                        materialEnviadoASecado |=
                            await RegistrarMaterialPendienteSecadoDesdeRecepcionAsync(
                                fila.RecepcionMaterialID,
                                usuarioId,
                                cn,
                                tx);
                    }

                    if (string.Equals(
                            fila.TipoOrigen,
                            ProduccionRecepcionMaterialTipo.Embalaje,
                            StringComparison.OrdinalIgnoreCase) &&
                        fila.EmbalajeSolicitadoID.HasValue &&
                        fila.EmbalajeSolicitadoID.Value > 0 &&
                        programaProduccionId.HasValue &&
                        programaProduccionId.Value > 0)
                    {
                        embalajeAutoConfirmado |=
                            await AutoConfirmarPreparacionEmbalajeAsync(
                                programaProduccionId.Value,
                                fila.SolicitudProduccionID,
                                fila.SolicitudProduccionDetalleID,
                                fila.EmbalajeSolicitadoID.Value,
                                usuarioId,
                                cn,
                                tx);
                    }
                }

                await ResolverDevolucionesPendientesAsync(
                    primera.TipoOrigen,
                    primera.SolicitudProduccionID,
                    primera.MaterialSolicitadoID,
                    primera.EmbalajeSolicitadoID,
                    usuarioId,
                    cn,
                    tx);

                await tx.CommitAsync();

                if (decision == ProduccionRecepcionMaterialDecision.Completo)
                {
                    TempData["Success"] =
                        $"Recepcion confirmada: {filas.Count} entregas acumuladas, {totalRecibido:0.####} {primera.Unidad}.";

                    if (materialEnviadoASecado)
                        TempData["Success"] +=
                            " El material quedo disponible en Secado.";

                    if (embalajeAutoConfirmado)
                        TempData["Success"] +=
                            " El embalaje requerido para la OF quedo completo.";
                }
                else if (decision == ProduccionRecepcionMaterialDecision.Parcial)
                {
                    TempData["Warning"] =
                        $"Recepcion parcial agrupada. Almacen reporto {totalEntregado:0.####} {primera.Unidad}; Produccion confirmo {totalRecibido:0.####} {primera.Unidad}.";
                }
                else
                {
                    TempData["Warning"] =
                        $"Produccion registro que no recibio las {filas.Count} entregas agrupadas.";
                }

                return RedirectToAction(nameof(Materiales));
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
                    "No fue posible confirmar las recepciones agrupadas: " +
                    ex.Message;

                return RedirectToAction(nameof(Materiales));
            }
        }

        // NSQ_PRODUCCION_RECEPCIONES_AGRUPADAS_DEVOLVER_TODO_V1_3
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(12000000)]
        public async Task<IActionResult> DevolverRecepcionesMaterial(
            string? RecepcionMaterialIDs,
            string? MotivoDevolucion,
            string? ComentarioDevolucion,
            IFormFile? EvidenciaDevolucion)
        {
            if (!UsuarioEnSesion())
                return RedirectToAction("Login", "Login");

            var ids =
                (RecepcionMaterialIDs ?? string.Empty)
                    .Split(
                        ',',
                        StringSplitOptions.RemoveEmptyEntries |
                        StringSplitOptions.TrimEntries)
                    .Select(x => long.TryParse(x, out var id) ? id : 0L)
                    .Where(x => x > 0)
                    .Distinct()
                    .ToList();

            if (ids.Count < 2)
            {
                TempData["Error"] =
                    "La devolucion total agrupada requiere al menos dos entregas pendientes.";

                return RedirectToAction(nameof(Materiales));
            }

            var motivoSolicitado =
                string.IsNullOrWhiteSpace(MotivoDevolucion)
                    ? null
                    : MotivoDevolucion.Trim();

            var detalleComentario =
                string.IsNullOrWhiteSpace(ComentarioDevolucion)
                    ? null
                    : ComentarioDevolucion.Trim();

            if (string.IsNullOrWhiteSpace(motivoSolicitado))
            {
                TempData["Error"] =
                    "Selecciona el motivo de la devolucion.";

                return RedirectToAction(nameof(Materiales));
            }

            if (string.IsNullOrWhiteSpace(detalleComentario))
            {
                TempData["Error"] =
                    "Escribe el comentario o detalle del problema.";

                return RedirectToAction(nameof(Materiales));
            }

            if (detalleComentario.Length > 500)
            {
                TempData["Error"] =
                    "El comentario de la devolucion no puede superar 500 caracteres.";

                return RedirectToAction(nameof(Materiales));
            }

            if (EvidenciaDevolucion == null ||
                EvidenciaDevolucion.Length <= 0)
            {
                TempData["Error"] =
                    "La fotografia de evidencia es obligatoria para una devolucion.";

                return RedirectToAction(nameof(Materiales));
            }

            const long maxEvidenceBytes =
                8L * 1024L * 1024L;

            if (EvidenciaDevolucion.Length > maxEvidenceBytes)
            {
                TempData["Error"] =
                    "La evidencia no puede superar 8 MB.";

                return RedirectToAction(nameof(Materiales));
            }

            var extension =
                System.IO.Path
                    .GetExtension(EvidenciaDevolucion.FileName)
                    .ToLowerInvariant();

            var extensionesPermitidas =
                new HashSet<string>(
                    new[] { ".jpg", ".jpeg", ".png", ".webp" },
                    StringComparer.OrdinalIgnoreCase);

            var contentType =
                EvidenciaDevolucion.ContentType?.Trim()
                ?? string.Empty;

            if (!extensionesPermitidas.Contains(extension) ||
                !contentType.StartsWith(
                    "image/",
                    StringComparison.OrdinalIgnoreCase))
            {
                TempData["Error"] =
                    "La evidencia debe ser una imagen JPG, PNG o WEBP.";

                return RedirectToAction(nameof(Materiales));
            }

            var usuarioId =
                ObtenerUsuarioID();

            await using var cn =
                new SqlConnection(ConnectionString);

            await cn.OpenAsync();

            var permisos =
                await ObtenerPermisosPreparacionUsuarioAsync(
                    usuarioId,
                    cn);

            if (!permisos.PuedeGestionarEmbalaje)
                return StatusCode(
                    StatusCodes.Status403Forbidden);

            await using (var existe =
                new SqlCommand(
                    "SELECT CASE WHEN OBJECT_ID(N'dbo.Produccion_DevolucionesMateriales',N'U') IS NULL THEN 0 ELSE 1 END;",
                    cn))
            {
                if (Convert.ToInt32(
                        await existe.ExecuteScalarAsync()) != 1)
                {
                    TempData["Error"] =
                        "Falta instalar NSQ_DEVOLUCION_MATERIALES_V1.sql en ERP_QUELL.";

                    return RedirectToAction(nameof(Materiales));
                }
            }

            string? evidenciaFisica = null;

            await using var tx =
                (SqlTransaction)await cn.BeginTransactionAsync(
                    IsolationLevel.Serializable);

            try
            {
                var filas =
                    await CargarRecepcionesDevolucionAgrupadaAsync(
                        ids,
                        cn,
                        tx);

                if (filas.Count != ids.Count)
                {
                    await tx.RollbackAsync();

                    TempData["Error"] =
                        "Una o mas entregas del grupo ya no existen.";

                    return RedirectToAction(nameof(Materiales));
                }

                if (filas.Any(
                        x => !string.Equals(
                            x.EstadoRecepcion,
                            ProduccionRecepcionMaterialEstado.Pendiente,
                            StringComparison.OrdinalIgnoreCase)))
                {
                    await tx.RollbackAsync();

                    TempData["Warning"] =
                        "Una entrega del grupo ya fue confirmada. Actualiza la pantalla.";

                    return RedirectToAction(nameof(Materiales));
                }

                var primera = filas[0];

                if (filas.Any(
                        x => !MismaClaveDevolucionAgrupada(
                            primera,
                            x)))
                {
                    await tx.RollbackAsync();

                    TempData["Error"] =
                        "Las entregas ya no corresponden al mismo material, lote u OF.";

                    return RedirectToAction(nameof(Materiales));
                }

                var motivo =
                    NormalizarMotivoDevolucion(
                        primera.TipoOrigen,
                        motivoSolicitado);

                if (string.IsNullOrWhiteSpace(motivo))
                {
                    await tx.RollbackAsync();

                    TempData["Error"] =
                        "El motivo seleccionado no es valido para este tipo de material.";

                    return RedirectToAction(nameof(Materiales));
                }

                var detalleDevolucion =
                    $"{motivo}. {detalleComentario}".Trim();

                var nombresParametros =
                    filas
                        .Select(
                            (x, index) =>
                                new
                                {
                                    x.RecepcionMaterialID,
                                    Nombre = "@DevId" + index
                                })
                        .ToList();

                var inSql =
                    string.Join(
                        ",",
                        nombresParametros.Select(x => x.Nombre));

                var sqlDuplicada = $@"
SELECT COUNT(1)
FROM dbo.Produccion_DevolucionesMateriales WITH(UPDLOCK,HOLDLOCK)
WHERE Activo=1
  AND RecepcionMaterialID IN ({inSql});";

                await using (var cmd =
                    new SqlCommand(
                        sqlDuplicada,
                        cn,
                        tx))
                {
                    foreach (var parametro in nombresParametros)
                    {
                        cmd.Parameters.Add(
                            parametro.Nombre,
                            SqlDbType.BigInt).Value =
                            parametro.RecepcionMaterialID;
                    }

                    if (Convert.ToInt32(
                            await cmd.ExecuteScalarAsync()) > 0)
                    {
                        await tx.RollbackAsync();

                        TempData["Error"] =
                            "Una entrega del grupo ya tiene una devolucion registrada.";

                        return RedirectToAction(nameof(Materiales));
                    }
                }

                var usuarioNombre =
                    await ObtenerNombreUsuarioDevolucionAsync(
                        usuarioId,
                        cn,
                        tx);

                var archivoId =
                    Guid.NewGuid().ToString("N");

                var rutaRelativa =
                    $"agrupadas/{archivoId}{extension}";

                evidenciaFisica =
                    ObtenerRutaEvidenciaDevolucionLocal(
                        rutaRelativa);

                var directorio =
                    System.IO.Path.GetDirectoryName(
                        evidenciaFisica);

                if (string.IsNullOrWhiteSpace(directorio))
                {
                    throw new InvalidOperationException(
                        "No fue posible determinar el directorio de evidencia.");
                }

                System.IO.Directory.CreateDirectory(
                    directorio);

                await using (var origen =
                    EvidenciaDevolucion.OpenReadStream())
                await using (var destino =
                    new System.IO.FileStream(
                        evidenciaFisica,
                        System.IO.FileMode.CreateNew,
                        System.IO.FileAccess.Write,
                        System.IO.FileShare.None,
                        81920,
                        useAsync: true))
                {
                    await origen.CopyToAsync(destino);
                    await destino.FlushAsync();
                }

                var totalDevuelto = 0m;

                foreach (var fila in filas)
                {
                    var referenciaRetorno =
                        "DEV-PROD-" +
                        Guid.NewGuid()
                            .ToString("N")
                            .ToUpperInvariant();

                    var movimientoRetornoId =
                        await RegistrarRetornoAlmacenDevolucionAsync(
                            fila.TipoOrigen,
                            fila.MaterialSolicitadoID,
                            fila.MaterialEntregadoID,
                            fila.EmbalajeSolicitadoID,
                            fila.EmbalajeEntregadoID,
                            fila.TipoMP,
                            fila.Lote,
                            fila.Unidad,
                            fila.CantidadEntregadaAlmacen,
                            fila.NumeroOF,
                            fila.SolicitudProduccionID,
                            fila.SolicitudProduccionDetalleID,
                            referenciaRetorno,
                            detalleDevolucion,
                            usuarioId,
                            usuarioNombre,
                            cn,
                            tx);

                    var motivoRecepcion =
                        ("DEVOLUCION: " +
                         detalleDevolucion).Trim();

                    if (motivoRecepcion.Length > 500)
                        motivoRecepcion =
                            motivoRecepcion[..500];

                    var observacionesRecepcion =
                        $"Produccion devolvio totalmente {fila.CantidadEntregadaAlmacen:0.####} {fila.Unidad} a Almacen como parte de una devolucion agrupada. Evidencia fotografica registrada.";

                    const string sqlActualizar = @"
UPDATE dbo.Produccion_RecepcionMateriales
SET
    EstadoRecepcion=N'NO_RECIBIDO',
    CantidadRecibidaProduccion=0,
    MotivoDiferencia=@Motivo,
    ObservacionesRecepcion=@Observaciones,
    UsuarioRecepcionID=@UsuarioID,
    FechaRecepcion=SYSDATETIME(),
    EstadoAclaracion=N'PENDIENTE',
    ResolucionAclaracion=NULL,
    UsuarioResolucionID=NULL,
    FechaResolucion=NULL,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=SYSDATETIME()
WHERE RecepcionMaterialID=@RecepcionMaterialID
  AND Activo=1
  AND EstadoRecepcion=N'PENDIENTE';

IF @@ROWCOUNT<>1
    THROW 51271,N'Una recepcion del grupo cambio de estado durante la devolucion.',1;";

                    await using (var cmd =
                        new SqlCommand(
                            sqlActualizar,
                            cn,
                            tx))
                    {
                        cmd.Parameters.Add(
                            "@Motivo",
                            SqlDbType.NVarChar,
                            500).Value =
                            motivoRecepcion;

                        cmd.Parameters.Add(
                            "@Observaciones",
                            SqlDbType.NVarChar,
                            800).Value =
                            observacionesRecepcion;

                        cmd.Parameters.Add(
                            "@UsuarioID",
                            SqlDbType.Int).Value =
                            usuarioId;

                        cmd.Parameters.Add(
                            "@RecepcionMaterialID",
                            SqlDbType.BigInt).Value =
                            fila.RecepcionMaterialID;

                        await cmd.ExecuteNonQueryAsync();
                    }

                    await ActualizarValidacionMovimientoAlmacenAsync(
                        fila.TipoOrigen,
                        fila.MovimientoAlmacenID,
                        fila.SolicitudProduccionID,
                        false,
                        cn,
                        tx);

                    const string sqlInsertDevolucion = @"
INSERT dbo.Produccion_DevolucionesMateriales
(
    RecepcionMaterialID,
    SolicitudProduccionID,
    TipoOrigen,
    MaterialSolicitadoID,
    MaterialEntregadoID,
    EmbalajeSolicitadoID,
    EmbalajeEntregadoID,
    MovimientoRetornoAlmacenID,
    CantidadDevuelta,
    Motivo,
    Comentario,
    EvidenciaRuta,
    EvidenciaContentType,
    EvidenciaNombreOriginal,
    Estado,
    UsuarioDevolucionID,
    FechaDevolucion,
    Activo,
    FechaCreacion
)
VALUES
(
    @RecepcionMaterialID,
    @SolicitudProduccionID,
    @TipoOrigen,
    @MaterialSolicitadoID,
    @MaterialEntregadoID,
    @EmbalajeSolicitadoID,
    @EmbalajeEntregadoID,
    @MovimientoRetornoAlmacenID,
    @CantidadDevuelta,
    @Motivo,
    @Comentario,
    @EvidenciaRuta,
    @EvidenciaContentType,
    @EvidenciaNombreOriginal,
    N'PENDIENTE_REPOSICION',
    @UsuarioID,
    SYSDATETIME(),
    1,
    SYSDATETIME()
);";

                    await using (var cmd =
                        new SqlCommand(
                            sqlInsertDevolucion,
                            cn,
                            tx))
                    {
                        cmd.Parameters.Add(
                            "@RecepcionMaterialID",
                            SqlDbType.BigInt).Value =
                            fila.RecepcionMaterialID;

                        cmd.Parameters.Add(
                            "@SolicitudProduccionID",
                            SqlDbType.Int).Value =
                            fila.SolicitudProduccionID;

                        cmd.Parameters.Add(
                            "@TipoOrigen",
                            SqlDbType.NVarChar,
                            20).Value =
                            fila.TipoOrigen;

                        cmd.Parameters.Add(
                            "@MaterialSolicitadoID",
                            SqlDbType.Int).Value =
                            fila.MaterialSolicitadoID.HasValue
                                ? fila.MaterialSolicitadoID.Value
                                : DBNull.Value;

                        cmd.Parameters.Add(
                            "@MaterialEntregadoID",
                            SqlDbType.Int).Value =
                            fila.MaterialEntregadoID.HasValue
                                ? fila.MaterialEntregadoID.Value
                                : DBNull.Value;

                        cmd.Parameters.Add(
                            "@EmbalajeSolicitadoID",
                            SqlDbType.Int).Value =
                            fila.EmbalajeSolicitadoID.HasValue
                                ? fila.EmbalajeSolicitadoID.Value
                                : DBNull.Value;

                        cmd.Parameters.Add(
                            "@EmbalajeEntregadoID",
                            SqlDbType.Int).Value =
                            fila.EmbalajeEntregadoID.HasValue
                                ? fila.EmbalajeEntregadoID.Value
                                : DBNull.Value;

                        cmd.Parameters.Add(
                            "@MovimientoRetornoAlmacenID",
                            SqlDbType.BigInt).Value =
                            movimientoRetornoId;

                        var pCantidad =
                            cmd.Parameters.Add(
                                "@CantidadDevuelta",
                                SqlDbType.Decimal);

                        pCantidad.Precision = 18;
                        pCantidad.Scale = 4;
                        pCantidad.Value =
                            fila.CantidadEntregadaAlmacen;

                        cmd.Parameters.Add(
                            "@Motivo",
                            SqlDbType.NVarChar,
                            500).Value =
                            motivo;

                        cmd.Parameters.Add(
                            "@Comentario",
                            SqlDbType.NVarChar,
                            500).Value =
                            detalleComentario;

                        cmd.Parameters.Add(
                            "@EvidenciaRuta",
                            SqlDbType.NVarChar,
                            500).Value =
                            rutaRelativa;

                        cmd.Parameters.Add(
                            "@EvidenciaContentType",
                            SqlDbType.NVarChar,
                            100).Value =
                            contentType;

                        cmd.Parameters.Add(
                            "@EvidenciaNombreOriginal",
                            SqlDbType.NVarChar,
                            255).Value =
                            string.IsNullOrWhiteSpace(
                                EvidenciaDevolucion.FileName)
                                ? DBNull.Value
                                : EvidenciaDevolucion.FileName[
                                    ..Math.Min(
                                        255,
                                        EvidenciaDevolucion.FileName.Length)];

                        cmd.Parameters.Add(
                            "@UsuarioID",
                            SqlDbType.Int).Value =
                            usuarioId;

                        await cmd.ExecuteNonQueryAsync();
                    }

                    var comentario =
                        $"Produccion devolvio totalmente {fila.CantidadEntregadaAlmacen:0.####} {fila.Unidad} de {fila.CodigoEntregado} dentro de una devolucion agrupada. Motivo: {motivo}. Comentario: {detalleComentario}";

                    if (comentario.Length > 1000)
                        comentario =
                            comentario[..1000];

                    await AgregarHistorialRecepcionMaterialAsync(
                        fila.RecepcionMaterialID,
                        "DEVOLUCION_PRODUCCION_AGRUPADA",
                        fila.EstadoRecepcion,
                        ProduccionRecepcionMaterialEstado.NoRecibido,
                        null,
                        0m,
                        fila.EstadoAclaracion,
                        ProduccionRecepcionMaterialEstadoAclaracion.Pendiente,
                        comentario,
                        usuarioId,
                        cn,
                        tx);

                    totalDevuelto +=
                        fila.CantidadEntregadaAlmacen;
                }

                const string sqlSync = @"
IF OBJECT_ID(N'dbo.sp_Almacen_SincronizarReservas',N'P') IS NOT NULL
BEGIN
    EXEC dbo.sp_Almacen_SincronizarReservas @Usuario=@Usuario;
END;";

                await using (var cmd =
                    new SqlCommand(
                        sqlSync,
                        cn,
                        tx))
                {
                    cmd.Parameters.Add(
                        "@Usuario",
                        SqlDbType.NVarChar,
                        120).Value =
                        usuarioNombre;

                    await cmd.ExecuteNonQueryAsync();
                }

                await tx.CommitAsync();

                TempData["Success"] =
                    $"Devolucion total agrupada registrada. {filas.Count} entregas fisicas ({totalDevuelto:0.####} {primera.Unidad}) regresaron a Almacen y la OF quedo pendiente de reposicion.";

                return RedirectToAction(nameof(Materiales));
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

                if (!string.IsNullOrWhiteSpace(evidenciaFisica))
                {
                    try
                    {
                        if (System.IO.File.Exists(evidenciaFisica))
                            System.IO.File.Delete(
                                evidenciaFisica);
                    }
                    catch
                    {
                    }
                }

                TempData["Error"] =
                    "No fue posible registrar la devolucion total agrupada: " +
                    ex.Message;

                return RedirectToAction(nameof(Materiales));
            }
        }

        private static bool MismaClaveDevolucionAgrupada(
            RecepcionDevolucionAgrupadaInterna a,
            RecepcionDevolucionAgrupadaInterna b)
        {
            return
                a.SolicitudProduccionID ==
                    b.SolicitudProduccionID &&
                string.Equals(
                    NormalizarClaveRecepcionAgrupada(a.TipoOrigen),
                    NormalizarClaveRecepcionAgrupada(b.TipoOrigen),
                    StringComparison.Ordinal) &&
                a.MaterialSolicitadoID ==
                    b.MaterialSolicitadoID &&
                a.MaterialEntregadoID ==
                    b.MaterialEntregadoID &&
                a.EmbalajeSolicitadoID ==
                    b.EmbalajeSolicitadoID &&
                a.EmbalajeEntregadoID ==
                    b.EmbalajeEntregadoID &&
                string.Equals(
                    NormalizarClaveRecepcionAgrupada(a.CodigoEntregado),
                    NormalizarClaveRecepcionAgrupada(b.CodigoEntregado),
                    StringComparison.Ordinal) &&
                string.Equals(
                    NormalizarClaveRecepcionAgrupada(a.TipoMP),
                    NormalizarClaveRecepcionAgrupada(b.TipoMP),
                    StringComparison.Ordinal) &&
                string.Equals(
                    NormalizarClaveRecepcionAgrupada(a.Lote),
                    NormalizarClaveRecepcionAgrupada(b.Lote),
                    StringComparison.Ordinal) &&
                string.Equals(
                    NormalizarClaveRecepcionAgrupada(a.Unidad),
                    NormalizarClaveRecepcionAgrupada(b.Unidad),
                    StringComparison.Ordinal);
        }

        private static async Task<List<RecepcionDevolucionAgrupadaInterna>>
            CargarRecepcionesDevolucionAgrupadaAsync(
                List<long> ids,
                SqlConnection cn,
                SqlTransaction tx)
        {
            var lista =
                new List<RecepcionDevolucionAgrupadaInterna>();

            var parametros =
                ids
                    .Select(
                        (id, index) =>
                            new
                            {
                                Id = id,
                                Nombre = "@RetId" + index
                            })
                    .ToList();

            var inSql =
                string.Join(
                    ",",
                    parametros.Select(x => x.Nombre));

            var sql = $@"
SELECT
    r.RecepcionMaterialID,
    r.TipoOrigen,
    r.MovimientoAlmacenID,
    r.SolicitudProduccionID,
    r.SolicitudProduccionDetalleID,
    r.NumeroOFSnapshot,
    r.MaterialSolicitadoID,
    r.MaterialEntregadoID,
    r.EmbalajeSolicitadoID,
    r.EmbalajeEntregadoID,
    r.CodigoEntregadoSnapshot,
    r.TipoMP,
    r.Lote,
    r.Unidad,
    r.CantidadEntregadaAlmacen,
    r.EstadoRecepcion,
    r.EstadoAclaracion,
    r.FechaEntregaAlmacen
FROM dbo.Produccion_RecepcionMateriales r WITH(UPDLOCK,HOLDLOCK)
WHERE r.Activo=1
  AND r.RecepcionMaterialID IN ({inSql})
ORDER BY r.FechaEntregaAlmacen,r.RecepcionMaterialID;";

            await using var cmd =
                new SqlCommand(
                    sql,
                    cn,
                    tx);

            foreach (var parametro in parametros)
            {
                cmd.Parameters.Add(
                    parametro.Nombre,
                    SqlDbType.BigInt).Value =
                    parametro.Id;
            }

            await using var rd =
                await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                lista.Add(
                    new RecepcionDevolucionAgrupadaInterna
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

                        NumeroOF =
                            rd["NumeroOFSnapshot"]?.ToString()?.Trim()
                            ?? string.Empty,

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

                        CodigoEntregado =
                            rd["CodigoEntregadoSnapshot"]?.ToString()?.Trim()
                            ?? string.Empty,

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

                        EstadoRecepcion =
                            rd["EstadoRecepcion"]?.ToString()?.Trim()
                            ?? string.Empty,

                        EstadoAclaracion =
                            rd["EstadoAclaracion"]?.ToString()?.Trim()
                            ?? ProduccionRecepcionMaterialEstadoAclaracion.NoAplica,

                        FechaEntregaAlmacen =
                            Convert.ToDateTime(
                                rd["FechaEntregaAlmacen"])
                    });
            }

            return lista;
        }

        private sealed class RecepcionDevolucionAgrupadaInterna
        {
            public long RecepcionMaterialID { get; set; }
            public string TipoOrigen { get; set; } = string.Empty;
            public long MovimientoAlmacenID { get; set; }

            public int SolicitudProduccionID { get; set; }
            public int? SolicitudProduccionDetalleID { get; set; }
            public string NumeroOF { get; set; } = string.Empty;

            public int? MaterialSolicitadoID { get; set; }
            public int? MaterialEntregadoID { get; set; }
            public int? EmbalajeSolicitadoID { get; set; }
            public int? EmbalajeEntregadoID { get; set; }

            public string CodigoEntregado { get; set; } = string.Empty;
            public string? TipoMP { get; set; }
            public string? Lote { get; set; }
            public string Unidad { get; set; } = string.Empty;

            public decimal CantidadEntregadaAlmacen { get; set; }
            public string EstadoRecepcion { get; set; } = string.Empty;
            public string EstadoAclaracion { get; set; } =
                ProduccionRecepcionMaterialEstadoAclaracion.NoAplica;

            public DateTime FechaEntregaAlmacen { get; set; }
        }

        private static bool MismaClaveRecepcionAgrupada(
            RecepcionAgrupadaInterna a,
            RecepcionAgrupadaInterna b)
        {
            return
                a.SolicitudProduccionID == b.SolicitudProduccionID &&
                a.SolicitudProduccionDetalleID == b.SolicitudProduccionDetalleID &&
                string.Equals(
                    NormalizarClaveRecepcionAgrupada(a.TipoOrigen),
                    NormalizarClaveRecepcionAgrupada(b.TipoOrigen),
                    StringComparison.Ordinal) &&
                a.MaterialSolicitadoID == b.MaterialSolicitadoID &&
                a.MaterialEntregadoID == b.MaterialEntregadoID &&
                a.EmbalajeSolicitadoID == b.EmbalajeSolicitadoID &&
                a.EmbalajeEntregadoID == b.EmbalajeEntregadoID &&
                string.Equals(
                    NormalizarClaveRecepcionAgrupada(a.CodigoEntregado),
                    NormalizarClaveRecepcionAgrupada(b.CodigoEntregado),
                    StringComparison.Ordinal) &&
                string.Equals(
                    NormalizarClaveRecepcionAgrupada(a.TipoMP),
                    NormalizarClaveRecepcionAgrupada(b.TipoMP),
                    StringComparison.Ordinal) &&
                string.Equals(
                    NormalizarClaveRecepcionAgrupada(a.Lote),
                    NormalizarClaveRecepcionAgrupada(b.Lote),
                    StringComparison.Ordinal) &&
                string.Equals(
                    NormalizarClaveRecepcionAgrupada(a.Unidad),
                    NormalizarClaveRecepcionAgrupada(b.Unidad),
                    StringComparison.Ordinal);
        }

        private static async Task<List<RecepcionAgrupadaInterna>>
            CargarRecepcionesAgrupadasParaConfirmacionAsync(
                List<long> ids,
                SqlConnection cn,
                SqlTransaction tx)
        {
            var lista =
                new List<RecepcionAgrupadaInterna>();

            if (ids == null || ids.Count == 0)
                return lista;

            var parametros =
                ids
                    .Select(
                        (id, index) =>
                            new
                            {
                                Id = id,
                                Nombre = "@AgrId" + index
                            })
                    .ToList();

            var inSql =
                string.Join(
                    ",",
                    parametros.Select(x => x.Nombre));

            var sql = $@"
SELECT
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
    r.TipoMP,
    r.Lote,
    r.Unidad,
    r.CantidadEntregadaAlmacen,
    r.EstadoRecepcion,
    r.EstadoAclaracion,
    r.FechaEntregaAlmacen
FROM dbo.Produccion_RecepcionMateriales r WITH(UPDLOCK,HOLDLOCK)
WHERE r.Activo=1
  AND r.RecepcionMaterialID IN ({inSql})
ORDER BY r.FechaEntregaAlmacen,r.RecepcionMaterialID;";

            await using var cmd =
                new SqlCommand(sql, cn, tx);

            foreach (var parametro in parametros)
            {
                cmd.Parameters.Add(
                    parametro.Nombre,
                    SqlDbType.BigInt).Value = parametro.Id;
            }

            await using var rd =
                await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                lista.Add(
                    new RecepcionAgrupadaInterna
                    {
                        RecepcionMaterialID =
                            Convert.ToInt64(rd["RecepcionMaterialID"]),

                        TipoOrigen =
                            rd["TipoOrigen"]?.ToString()?.Trim()
                            ?? string.Empty,

                        MovimientoAlmacenID =
                            Convert.ToInt64(rd["MovimientoAlmacenID"]),

                        SolicitudProduccionID =
                            Convert.ToInt32(rd["SolicitudProduccionID"]),

                        SolicitudProduccionDetalleID =
                            rd["SolicitudProduccionDetalleID"] == DBNull.Value
                                ? null
                                : Convert.ToInt32(rd["SolicitudProduccionDetalleID"]),

                        ProgramaProduccionID =
                            rd["ProgramaProduccionID"] == DBNull.Value
                                ? null
                                : Convert.ToInt32(rd["ProgramaProduccionID"]),

                        EjecucionProduccionID =
                            rd["EjecucionProduccionID"] == DBNull.Value
                                ? null
                                : Convert.ToInt32(rd["EjecucionProduccionID"]),

                        MaterialSolicitadoID =
                            rd["MaterialSolicitadoID"] == DBNull.Value
                                ? null
                                : Convert.ToInt32(rd["MaterialSolicitadoID"]),

                        MaterialEntregadoID =
                            rd["MaterialEntregadoID"] == DBNull.Value
                                ? null
                                : Convert.ToInt32(rd["MaterialEntregadoID"]),

                        EmbalajeSolicitadoID =
                            rd["EmbalajeSolicitadoID"] == DBNull.Value
                                ? null
                                : Convert.ToInt32(rd["EmbalajeSolicitadoID"]),

                        EmbalajeEntregadoID =
                            rd["EmbalajeEntregadoID"] == DBNull.Value
                                ? null
                                : Convert.ToInt32(rd["EmbalajeEntregadoID"]),

                        CodigoEntregado =
                            rd["CodigoEntregadoSnapshot"]?.ToString()?.Trim()
                            ?? string.Empty,

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
                            Convert.ToDecimal(rd["CantidadEntregadaAlmacen"]),

                        EstadoRecepcion =
                            rd["EstadoRecepcion"]?.ToString()?.Trim()
                            ?? string.Empty,

                        EstadoAclaracion =
                            rd["EstadoAclaracion"]?.ToString()?.Trim()
                            ?? ProduccionRecepcionMaterialEstadoAclaracion.NoAplica,

                        FechaEntregaAlmacen =
                            Convert.ToDateTime(rd["FechaEntregaAlmacen"])
                    });
            }

            return lista;
        }

        private sealed class RecepcionAgrupadaInterna
        {
            public long RecepcionMaterialID { get; set; }
            public string TipoOrigen { get; set; } = string.Empty;
            public long MovimientoAlmacenID { get; set; }

            public int SolicitudProduccionID { get; set; }
            public int? SolicitudProduccionDetalleID { get; set; }
            public int? ProgramaProduccionID { get; set; }
            public int? EjecucionProduccionID { get; set; }

            public int? MaterialSolicitadoID { get; set; }
            public int? MaterialEntregadoID { get; set; }
            public int? EmbalajeSolicitadoID { get; set; }
            public int? EmbalajeEntregadoID { get; set; }

            public string CodigoEntregado { get; set; } = string.Empty;
            public string? TipoMP { get; set; }
            public string? Lote { get; set; }
            public string Unidad { get; set; } = string.Empty;

            public decimal CantidadEntregadaAlmacen { get; set; }
            public string EstadoRecepcion { get; set; } = string.Empty;
            public string EstadoAclaracion { get; set; } =
                ProduccionRecepcionMaterialEstadoAclaracion.NoAplica;

            public DateTime FechaEntregaAlmacen { get; set; }
        }
    }
}