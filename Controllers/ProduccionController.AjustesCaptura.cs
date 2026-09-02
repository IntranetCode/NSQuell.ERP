using ERP.NSQuell.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;

namespace ERP.NSQuell.Controllers
{
    public sealed partial class ProduccionController
    {
        private sealed class ContextoAjusteRegistroHoraProduccion
        {
            public int RegistroHoraID { get; set; }
            public int EjecucionProduccionID { get; set; }
            public int? OperadorID { get; set; }
            public int EstatusEjecucionID { get; set; }
            public int CantidadOK { get; set; }
            public int CantidadSospechosa { get; set; }
            public int CantidadScrap { get; set; }
            public int ObjetivoBloque { get; set; }
            public int PiezasReferencia { get; set; }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CorregirCapturaHora(ProduccionAjustarRegistroHoraPostVm vm)
        {
            if (!UsuarioEnSesion()) return RedirectToAction("Login", "Login");
            if (vm.EjecucionProduccionID <= 0 || vm.RegistroHoraID <= 0)
            {
                TempData["Error"] = "No se recibió una captura horaria válida.";
                return vm.EjecucionProduccionID > 0 ? RedirectToAction(nameof(Detalle), new { id = vm.EjecucionProduccionID }) : RedirectToAction(nameof(Index));
            }
            if (vm.CantidadOK < 0 || vm.CantidadScrap < 0)
            {
                TempData["Error"] = "Las cantidades verdes y rojas no pueden ser negativas.";
                return RedirectToAction(nameof(Detalle), new { id = vm.EjecucionProduccionID });
            }
            var motivo = (vm.Motivo ?? string.Empty).Trim();
            if (motivo.Length < 5)
            {
                TempData["Error"] = "Debes indicar el motivo de la corrección con al menos 5 caracteres.";
                return RedirectToAction(nameof(Detalle), new { id = vm.EjecucionProduccionID });
            }
            if (motivo.Length > 1000)
            {
                TempData["Error"] = "El motivo de la corrección no puede superar 1000 caracteres.";
                return RedirectToAction(nameof(Detalle), new { id = vm.EjecucionProduccionID });
            }
            var usuarioId = ObtenerUsuarioID();
            if (usuarioId <= 0)
            {
                TempData["Error"] = "No fue posible identificar al usuario que realiza la corrección.";
                return RedirectToAction(nameof(Detalle), new { id = vm.EjecucionProduccionID });
            }
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                var permisos = await ObtenerPermisosProduccionUsuarioAsync(usuarioId, cn, tx);
                if (!permisos.PuedeCorregirCapturasHora)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "No tienes permisos para corregir capturas horarias. Esta acción corresponde al Auxiliar, Encargado de Producción o Administrador.";
                    return RedirectToAction(nameof(Detalle), new { id = vm.EjecucionProduccionID });
                }
                if (!await ExisteTablaAjustesProduccionAsync(cn, tx))
                    throw new InvalidOperationException("No existe dbo.Produccion_RegistroHoraAjustesProduccion. Ejecuta primero el script de BD para habilitar la auditoría.");
                var registro = await ObtenerRegistroHoraParaAjusteProduccionAsync(vm.RegistroHoraID, cn, tx);
                if (registro == null)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "La captura horaria ya no existe o fue desactivada.";
                    return RedirectToAction(nameof(Detalle), new { id = vm.EjecucionProduccionID });
                }
                if (registro.EjecucionProduccionID != vm.EjecucionProduccionID)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "La captura seleccionada no pertenece a esta ejecución de Producción.";
                    return RedirectToAction(nameof(Detalle), new { id = vm.EjecucionProduccionID });
                }
                if (registro.EstatusEjecucionID != ProduccionEstatus.EnProduccion && registro.EstatusEjecucionID != ProduccionEstatus.Pausado && registro.EstatusEjecucionID != ProduccionEstatus.TerminadoParcial)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = "La captura ya no puede corregirse porque la ejecución no se encuentra activa, pausada o terminada parcialmente.";
                    return RedirectToAction(nameof(Detalle), new { id = vm.EjecucionProduccionID });
                }
                if (vm.CantidadOK == registro.CantidadOK && vm.CantidadScrap == registro.CantidadScrap)
                {
                    await tx.RollbackAsync();
                    TempData["Info"] = "No se detectaron cambios en las cantidades.";
                    return RedirectToAction(nameof(Detalle), new { id = vm.EjecucionProduccionID });
                }
                var totalNuevo = (long)vm.CantidadOK + registro.CantidadSospechosa + vm.CantidadScrap;
                if (totalNuevo > registro.PiezasReferencia)
                {
                    await tx.RollbackAsync();
                    TempData["Error"] = $"La corrección suma {totalNuevo:N0} pieza(s), pero el contador de máquina conserva una referencia máxima de {registro.PiezasReferencia:N0}. Puedes reducir la cantidad por pérdidas físicas, pero no superar lo registrado por la máquina.";
                    return RedirectToAction(nameof(Detalle), new { id = vm.EjecucionProduccionID });
                }
                await ValidarCambioScrapContraDefectosAsync(registro.RegistroHoraID, registro.CantidadScrap, vm.CantidadScrap, cn, tx);
                bool? cumplioObjetivo = null;
                int? diferenciaObjetivo = null;
                decimal? porcentajeCumplimiento = null;
                if (registro.ObjetivoBloque > 0)
                {
                    diferenciaObjetivo = vm.CantidadOK - registro.ObjetivoBloque;
                    cumplioObjetivo = vm.CantidadOK >= registro.ObjetivoBloque;
                    porcentajeCumplimiento = Math.Round((decimal)vm.CantidadOK * 100m / registro.ObjetivoBloque, 2);
                }
                await ActualizarRegistroHoraAjusteProduccionAsync(registro, vm.CantidadOK, vm.CantidadScrap, cumplioObjetivo, diferenciaObjetivo, porcentajeCumplimiento, usuarioId, cn, tx);
                var ajusteProduccionId = await InsertarHistorialAjusteProduccionAsync(registro, vm.CantidadOK, vm.CantidadScrap, motivo, usuarioId, cn, tx);
                var diferenciaOK = vm.CantidadOK - registro.CantidadOK;
                long? movimientoBonusId = null;
                if (diferenciaOK != 0)
                {
                    if (!registro.OperadorID.HasValue || registro.OperadorID.Value <= 0)
                        throw new InvalidOperationException("La captura no tiene un operador identificable. No es seguro modificar las piezas OK porque no podría ajustarse correctamente su bonus.");
                    movimientoBonusId = await RegistrarMovimientoBonusAjusteProduccionAsync(ajusteProduccionId, registro.OperadorID.Value, registro.EjecucionProduccionID, registro.RegistroHoraID, diferenciaOK, registro.PiezasReferencia, motivo, usuarioId, cn, tx);
                    if (movimientoBonusId.HasValue)
                        await VincularMovimientoBonusAjusteProduccionAsync(ajusteProduccionId, movimientoBonusId.Value, cn, tx);
                }
                await RecalcularTotalesEjecucionAsync(registro.EjecucionProduccionID, usuarioId, cn, tx);
                await tx.CommitAsync();
                var mensajeBonus = diferenciaOK switch
                {
                    > 0 => $"Se abonaron +{diferenciaOK:N0} pieza(s) al bonus del operador.",
                    < 0 => $"Se descontaron {Math.Abs(diferenciaOK):N0} pieza(s) del bonus del operador.",
                    _ => "El bonus del operador no cambió."
                };
                TempData["Success"] = $"Captura corregida correctamente. Verdes/OK: {registro.CantidadOK:N0} → {vm.CantidadOK:N0}. Rojas/Scrap: {registro.CantidadScrap:N0} → {vm.CantidadScrap:N0}. {mensajeBonus} El valor anterior quedó conservado en el historial.";
                return RedirectToAction(nameof(Detalle), new { id = registro.EjecucionProduccionID });
            }
            catch (Exception ex)
            {
                try { await tx.RollbackAsync(); } catch { }
                TempData["Error"] = "No fue posible corregir la captura horaria: " + ex.Message;
                return RedirectToAction(nameof(Detalle), new { id = vm.EjecucionProduccionID });
            }
        }

        private async Task<bool> UsuarioPuedeCorregirCapturasHoraAsync(int usuarioId, SqlConnection cn, SqlTransaction? tx = null)
        {
            var permisos = await ObtenerPermisosProduccionUsuarioAsync(usuarioId, cn, tx);
            return permisos.PuedeCorregirCapturasHora;
        }

        private static async Task<bool> ExisteTablaAjustesProduccionAsync(SqlConnection cn, SqlTransaction tx)
        {
            const string sql = @"SELECT CASE WHEN OBJECT_ID(N'dbo.Produccion_RegistroHoraAjustesProduccion',N'U') IS NULL THEN 0 ELSE 1 END;";
            await using var cmd = new SqlCommand(sql, cn, tx);
            return Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 1;
        }

        private static async Task<ContextoAjusteRegistroHoraProduccion?> ObtenerRegistroHoraParaAjusteProduccionAsync(int registroHoraId, SqlConnection cn, SqlTransaction tx)
        {
            const string sql = @"
SELECT TOP(1)
    rh.RegistroHoraID,
    rh.EjecucionProduccionID,
    rh.OperadorID,
    e.EstatusID AS EstatusEjecucionID,
    ISNULL(rh.CantidadOK,0) AS CantidadOK,
    ISNULL(rh.CantidadSospechosa,0) AS CantidadSospechosa,
    ISNULL(rh.CantidadScrap,0) AS CantidadScrap,
    ISNULL(rh.ObjetivoBloque,0) AS ObjetivoBloque,
    ISNULL(rh.PiezasCalculadasContador,ISNULL(rh.CantidadOK,0)+ISNULL(rh.CantidadSospechosa,0)+ISNULL(rh.CantidadScrap,0)) AS PiezasReferencia
FROM dbo.Produccion_RegistroHora rh WITH(UPDLOCK,HOLDLOCK)
INNER JOIN dbo.Produccion_Ejecucion e WITH(UPDLOCK,HOLDLOCK)
    ON e.EjecucionProduccionID=rh.EjecucionProduccionID
   AND e.Activo=1
WHERE rh.RegistroHoraID=@RegistroHoraID
  AND rh.Activo=1;";
            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@RegistroHoraID", SqlDbType.Int).Value = registroHoraId;
            await using var rd = await cmd.ExecuteReaderAsync();
            if (!await rd.ReadAsync()) return null;
            return new ContextoAjusteRegistroHoraProduccion
            {
                RegistroHoraID = Convert.ToInt32(rd["RegistroHoraID"]),
                EjecucionProduccionID = Convert.ToInt32(rd["EjecucionProduccionID"]),
                OperadorID = rd["OperadorID"] == DBNull.Value ? null : Convert.ToInt32(rd["OperadorID"]),
                EstatusEjecucionID = Convert.ToInt32(rd["EstatusEjecucionID"]),
                CantidadOK = Convert.ToInt32(rd["CantidadOK"]),
                CantidadSospechosa = Convert.ToInt32(rd["CantidadSospechosa"]),
                CantidadScrap = Convert.ToInt32(rd["CantidadScrap"]),
                ObjetivoBloque = Convert.ToInt32(rd["ObjetivoBloque"]),
                PiezasReferencia = Convert.ToInt32(rd["PiezasReferencia"])
            };
        }

        private static async Task ValidarCambioScrapContraDefectosAsync(int registroHoraId, int scrapActual, int scrapNuevo, SqlConnection cn, SqlTransaction tx)
        {
            if (scrapActual == scrapNuevo) return;
            const string sql = @"
IF OBJECT_ID(N'dbo.Produccion_RegistroHoraDefectos',N'U') IS NULL
BEGIN
    SELECT CAST(0 AS INT);
    RETURN;
END;
SELECT ISNULL(SUM(ISNULL(CantidadScrap,0)),0)
FROM dbo.Produccion_RegistroHoraDefectos WITH(UPDLOCK,HOLDLOCK)
WHERE RegistroHoraID=@RegistroHoraID
  AND Activo=1;";
            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@RegistroHoraID", SqlDbType.Int).Value = registroHoraId;
            var resultado = await cmd.ExecuteScalarAsync();
            var scrapClasificado = resultado == null || resultado == DBNull.Value ? 0 : Convert.ToInt32(resultado);
            if (scrapClasificado > 0 && scrapClasificado != scrapNuevo)
                throw new InvalidOperationException($"Este registro tiene {scrapClasificado:N0} pieza(s) rojas clasificadas por defecto. No se cambiará el total de Scrap a {scrapNuevo:N0} sin modificar también su clasificación, porque dejaríamos datos inconsistentes. Puedes corregir las piezas verdes; para cambiar las rojas agregaremos el ajuste de defectos en el siguiente bloque.");
        }

        private static async Task ActualizarRegistroHoraAjusteProduccionAsync(ContextoAjusteRegistroHoraProduccion registro, int cantidadOK, int cantidadScrap, bool? cumplioObjetivo, int? diferenciaObjetivo, decimal? porcentajeCumplimiento, int usuarioId, SqlConnection cn, SqlTransaction tx)
        {
            const string sql = @"
UPDATE dbo.Produccion_RegistroHora
SET CantidadOK=@CantidadOK,
    CantidadScrap=@CantidadScrap,
    CumplioObjetivo=@CumplioObjetivo,
    DiferenciaObjetivo=@DiferenciaObjetivo,
    PorcentajeCumplimiento=@PorcentajeCumplimiento,
    UsuarioModificacionID=@UsuarioID,
    FechaModificacion=SYSDATETIME()
WHERE RegistroHoraID=@RegistroHoraID
  AND EjecucionProduccionID=@EjecucionProduccionID
  AND Activo=1;
IF @@ROWCOUNT<>1
    THROW 51600,'La captura horaria cambió mientras se realizaba la corrección.',1;";
            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@RegistroHoraID", SqlDbType.Int).Value = registro.RegistroHoraID;
            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = registro.EjecucionProduccionID;
            cmd.Parameters.Add("@CantidadOK", SqlDbType.Int).Value = cantidadOK;
            cmd.Parameters.Add("@CantidadScrap", SqlDbType.Int).Value = cantidadScrap;
            cmd.Parameters.Add("@CumplioObjetivo", SqlDbType.Bit).Value = (object?)cumplioObjetivo ?? DBNull.Value;
            cmd.Parameters.Add("@DiferenciaObjetivo", SqlDbType.Int).Value = (object?)diferenciaObjetivo ?? DBNull.Value;
            var pPorcentaje = cmd.Parameters.Add("@PorcentajeCumplimiento", SqlDbType.Decimal);
            pPorcentaje.Precision = 8;
            pPorcentaje.Scale = 2;
            pPorcentaje.Value = (object?)porcentajeCumplimiento ?? DBNull.Value;
            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task<long> InsertarHistorialAjusteProduccionAsync(ContextoAjusteRegistroHoraProduccion registro, int okDespues, int scrapDespues, string motivo, int usuarioId, SqlConnection cn, SqlTransaction tx)
        {
            const string sql = @"
IF COL_LENGTH(N'dbo.Produccion_RegistroHoraAjustesProduccion',N'SospechosoAntes') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.Produccion_RegistroHoraAjustesProduccion',N'PiezasCalculadasContador') IS NOT NULL
    BEGIN
        INSERT dbo.Produccion_RegistroHoraAjustesProduccion
        (
            RegistroHoraID,EjecucionProduccionID,OperadorID,
            OKAntes,SospechosoAntes,ScrapAntes,
            OKDespues,SospechosoDespues,ScrapDespues,
            PiezasCalculadasContador,Motivo,UsuarioAjusteID,FechaAjuste,Activo
        )
        OUTPUT INSERTED.AjusteProduccionID
        VALUES
        (
            @RegistroHoraID,@EjecucionProduccionID,@OperadorID,
            @OKAntes,@SospechosoAntes,@ScrapAntes,
            @OKDespues,@SospechosoDespues,@ScrapDespues,
            @PiezasReferencia,@Motivo,@UsuarioID,SYSDATETIME(),1
        );
    END
    ELSE
    BEGIN
        INSERT dbo.Produccion_RegistroHoraAjustesProduccion
        (
            RegistroHoraID,EjecucionProduccionID,OperadorID,
            OKAntes,SospechosoAntes,ScrapAntes,
            OKDespues,SospechosoDespues,ScrapDespues,
            Motivo,UsuarioAjusteID,FechaAjuste,Activo
        )
        OUTPUT INSERTED.AjusteProduccionID
        VALUES
        (
            @RegistroHoraID,@EjecucionProduccionID,@OperadorID,
            @OKAntes,@SospechosoAntes,@ScrapAntes,
            @OKDespues,@SospechosoDespues,@ScrapDespues,
            @Motivo,@UsuarioID,SYSDATETIME(),1
        );
    END;
END
ELSE IF COL_LENGTH(N'dbo.Produccion_RegistroHoraAjustesProduccion',N'PiezasFisicasContador') IS NOT NULL
BEGIN
    INSERT dbo.Produccion_RegistroHoraAjustesProduccion
    (
        RegistroHoraID,EjecucionProduccionID,OperadorID,PiezasFisicasContador,
        OKAntes,ScrapAntes,OKDespues,ScrapDespues,
        Motivo,UsuarioAjusteID,FechaAjuste,Activo
    )
    OUTPUT INSERTED.AjusteProduccionID
    VALUES
    (
        @RegistroHoraID,@EjecucionProduccionID,@OperadorID,@PiezasReferencia,
        @OKAntes,@ScrapAntes,@OKDespues,@ScrapDespues,
        @Motivo,@UsuarioID,SYSDATETIME(),1
    );
END
ELSE
BEGIN
    INSERT dbo.Produccion_RegistroHoraAjustesProduccion
    (
        RegistroHoraID,EjecucionProduccionID,OperadorID,
        OKAntes,ScrapAntes,OKDespues,ScrapDespues,
        Motivo,UsuarioAjusteID,FechaAjuste,Activo
    )
    OUTPUT INSERTED.AjusteProduccionID
    VALUES
    (
        @RegistroHoraID,@EjecucionProduccionID,@OperadorID,
        @OKAntes,@ScrapAntes,@OKDespues,@ScrapDespues,
        @Motivo,@UsuarioID,SYSDATETIME(),1
    );
END;";
            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@RegistroHoraID", SqlDbType.Int).Value = registro.RegistroHoraID;
            cmd.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = registro.EjecucionProduccionID;
            cmd.Parameters.Add("@OperadorID", SqlDbType.Int).Value = (object?)registro.OperadorID ?? DBNull.Value;
            cmd.Parameters.Add("@OKAntes", SqlDbType.Int).Value = registro.CantidadOK;
            cmd.Parameters.Add("@SospechosoAntes", SqlDbType.Int).Value = registro.CantidadSospechosa;
            cmd.Parameters.Add("@ScrapAntes", SqlDbType.Int).Value = registro.CantidadScrap;
            cmd.Parameters.Add("@OKDespues", SqlDbType.Int).Value = okDespues;
            cmd.Parameters.Add("@SospechosoDespues", SqlDbType.Int).Value = registro.CantidadSospechosa;
            cmd.Parameters.Add("@ScrapDespues", SqlDbType.Int).Value = scrapDespues;
            cmd.Parameters.Add("@PiezasReferencia", SqlDbType.Int).Value = registro.PiezasReferencia;
            cmd.Parameters.Add("@Motivo", SqlDbType.NVarChar, 1000).Value = motivo;
            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
            var resultado = await cmd.ExecuteScalarAsync();
            if (resultado == null || resultado == DBNull.Value) throw new InvalidOperationException("No fue posible crear el historial de la corrección.");
            return Convert.ToInt64(resultado);
        }

        private static async Task<long?> RegistrarMovimientoBonusAjusteProduccionAsync(long ajusteProduccionId, int operadorId, int ejecucionProduccionId, int registroHoraId, int diferenciaOK, int piezasReferencia, string motivo, int usuarioId, SqlConnection cn, SqlTransaction tx)
        {
            if (diferenciaOK == 0) return null;
            if (operadorId <= 0) throw new InvalidOperationException("El registro no tiene un operador válido para afectar el bonus.");
            var referenciaEvento = $"AJUSTE_PRODUCCION_REGISTRO:{ajusteProduccionId}";
            const string sqlExiste = @"
SELECT TOP(1) MovimientoBonusID
FROM dbo.Produccion_BonusOperadorMovimientos WITH(UPDLOCK,HOLDLOCK)
WHERE ReferenciaEvento=@ReferenciaEvento
  AND Activo=1;";
            await using (var cmd = new SqlCommand(sqlExiste, cn, tx))
            {
                cmd.Parameters.Add("@ReferenciaEvento", SqlDbType.NVarChar, 200).Value = referenciaEvento;
                var existente = await cmd.ExecuteScalarAsync();
                if (existente != null && existente != DBNull.Value) return Convert.ToInt64(existente);
            }
            if (diferenciaOK < 0)
            {
                const string sqlSaldo = @"
SELECT ISNULL(SUM(CONVERT(BIGINT,PiezasMovimiento)),0)
FROM dbo.Produccion_BonusOperadorMovimientos WITH(UPDLOCK,HOLDLOCK)
WHERE OperadorID=@OperadorID
  AND RegistroHoraID=@RegistroHoraID
  AND Activo=1;";
                await using var cmdSaldo = new SqlCommand(sqlSaldo, cn, tx);
                cmdSaldo.Parameters.Add("@OperadorID", SqlDbType.Int).Value = operadorId;
                cmdSaldo.Parameters.Add("@RegistroHoraID", SqlDbType.Int).Value = registroHoraId;
                var resultadoSaldo = await cmdSaldo.ExecuteScalarAsync();
                var saldo = resultadoSaldo == null || resultadoSaldo == DBNull.Value ? 0L : Convert.ToInt64(resultadoSaldo);
                var descuento = Math.Abs((long)diferenciaOK);
                if (saldo < descuento)
                    throw new InvalidOperationException($"No se pueden descontar {descuento:N0} pieza(s) del bonus porque el registro solamente conserva {saldo:N0} pieza(s) abonadas. Se evitó un descuento doble.");
            }
            var tipoMovimiento = diferenciaOK > 0 ? ProduccionTipoMovimientoBonus.CorreccionPositiva : ProduccionTipoMovimientoBonus.CorreccionNegativa;
            var motivoBonus = $"Corrección operativa de Producción sobre RegistroHoraID {registroHoraId}. Variación de verdes/OK: {(diferenciaOK > 0 ? "+" : "")}{diferenciaOK:N0}. Motivo: {motivo}";
            if (motivoBonus.Length > 1000) motivoBonus = motivoBonus[..1000];
            const string sqlInsertar = @"
INSERT dbo.Produccion_BonusOperadorMovimientos
(
    OperadorID,EjecucionProduccionID,RegistroHoraID,MonitoreoID,DisposicionID,
    TipoMovimiento,PiezasMovimiento,PiezasReferencia,Motivo,ReferenciaEvento,
    UsuarioCreacionID,FechaMovimiento,Activo
)
OUTPUT INSERTED.MovimientoBonusID
VALUES
(
    @OperadorID,@EjecucionProduccionID,@RegistroHoraID,NULL,NULL,
    @TipoMovimiento,@PiezasMovimiento,@PiezasReferencia,@Motivo,@ReferenciaEvento,
    @UsuarioID,SYSDATETIME(),1
);";
            await using var cmdInsertar = new SqlCommand(sqlInsertar, cn, tx);
            cmdInsertar.Parameters.Add("@OperadorID", SqlDbType.Int).Value = operadorId;
            cmdInsertar.Parameters.Add("@EjecucionProduccionID", SqlDbType.Int).Value = ejecucionProduccionId;
            cmdInsertar.Parameters.Add("@RegistroHoraID", SqlDbType.Int).Value = registroHoraId;
            cmdInsertar.Parameters.Add("@TipoMovimiento", SqlDbType.NVarChar, 60).Value = tipoMovimiento;
            cmdInsertar.Parameters.Add("@PiezasMovimiento", SqlDbType.Int).Value = diferenciaOK;
            cmdInsertar.Parameters.Add("@PiezasReferencia", SqlDbType.Int).Value = piezasReferencia;
            cmdInsertar.Parameters.Add("@Motivo", SqlDbType.NVarChar, 1000).Value = motivoBonus;
            cmdInsertar.Parameters.Add("@ReferenciaEvento", SqlDbType.NVarChar, 200).Value = referenciaEvento;
            cmdInsertar.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
            var resultado = await cmdInsertar.ExecuteScalarAsync();
            if (resultado == null || resultado == DBNull.Value) throw new InvalidOperationException("No fue posible registrar el movimiento de bonus de la corrección.");
            return Convert.ToInt64(resultado);
        }

        private static async Task VincularMovimientoBonusAjusteProduccionAsync(long ajusteProduccionId, long movimientoBonusId, SqlConnection cn, SqlTransaction tx)
        {
            const string sql = @"
UPDATE dbo.Produccion_RegistroHoraAjustesProduccion
SET MovimientoBonusID=@MovimientoBonusID
WHERE AjusteProduccionID=@AjusteProduccionID
  AND Activo=1
  AND MovimientoBonusID IS NULL;
IF @@ROWCOUNT<>1
    THROW 51601,'No fue posible vincular el movimiento de bonus con el historial del ajuste.',1;";
            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@AjusteProduccionID", SqlDbType.BigInt).Value = ajusteProduccionId;
            cmd.Parameters.Add("@MovimientoBonusID", SqlDbType.BigInt).Value = movimientoBonusId;
            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task<List<ProduccionRegistroHoraAjusteProduccionVm>> ObtenerAjustesProduccionRegistroHoraAsync(int registroHoraId, SqlConnection cn)
        {
            var lista = new List<ProduccionRegistroHoraAjusteProduccionVm>();
            if (registroHoraId <= 0) return lista;
            await using (var cmdExiste = new SqlCommand(@"SELECT CASE WHEN OBJECT_ID(N'dbo.Produccion_RegistroHoraAjustesProduccion',N'U') IS NULL THEN 0 ELSE 1 END;", cn))
            {
                if (Convert.ToInt32(await cmdExiste.ExecuteScalarAsync()) == 0) return lista;
            }
            const string sql = @"
SELECT
    a.AjusteProduccionID,
    a.RegistroHoraID,
    a.EjecucionProduccionID,
    a.OperadorID,
    a.OKAntes,
    a.ScrapAntes,
    a.OKDespues,
    a.ScrapDespues,
    a.MovimientoBonusID,
    a.Motivo,
    a.UsuarioAjusteID,
    LTRIM(RTRIM(CONCAT(ISNULL(p.Nombre,N''),N' ',ISNULL(p.ApellidoPaterno,N''),N' ',ISNULL(p.ApellidoMaterno,N'')))) AS UsuarioAjusteNombre,
    a.FechaAjuste,
    a.Activo
FROM dbo.Produccion_RegistroHoraAjustesProduccion a
LEFT JOIN dbo.Usuarios u ON u.UsuarioID=a.UsuarioAjusteID
LEFT JOIN dbo.Persona p ON p.PersonaID=u.PersonaID
WHERE a.RegistroHoraID=@RegistroHoraID
  AND a.Activo=1
ORDER BY a.FechaAjuste DESC,a.AjusteProduccionID DESC;";
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@RegistroHoraID", SqlDbType.Int).Value = registroHoraId;
            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                var usuarioNombre = rd["UsuarioAjusteNombre"] == DBNull.Value ? string.Empty : rd["UsuarioAjusteNombre"]?.ToString()?.Trim() ?? string.Empty;
                var usuarioAjusteId = Convert.ToInt32(rd["UsuarioAjusteID"]);
                lista.Add(new ProduccionRegistroHoraAjusteProduccionVm
                {
                    AjusteProduccionID = Convert.ToInt64(rd["AjusteProduccionID"]),
                    RegistroHoraID = Convert.ToInt32(rd["RegistroHoraID"]),
                    EjecucionProduccionID = Convert.ToInt32(rd["EjecucionProduccionID"]),
                    OperadorID = rd["OperadorID"] == DBNull.Value ? null : Convert.ToInt32(rd["OperadorID"]),
                    OKAntes = Convert.ToInt32(rd["OKAntes"]),
                    ScrapAntes = Convert.ToInt32(rd["ScrapAntes"]),
                    OKDespues = Convert.ToInt32(rd["OKDespues"]),
                    ScrapDespues = Convert.ToInt32(rd["ScrapDespues"]),
                    MovimientoBonusID = rd["MovimientoBonusID"] == DBNull.Value ? null : Convert.ToInt64(rd["MovimientoBonusID"]),
                    Motivo = rd["Motivo"] == DBNull.Value ? string.Empty : rd["Motivo"]?.ToString()?.Trim() ?? string.Empty,
                    UsuarioAjusteID = usuarioAjusteId,
                    UsuarioAjusteNombre = string.IsNullOrWhiteSpace(usuarioNombre) ? $"Usuario #{usuarioAjusteId}" : usuarioNombre,
                    FechaAjuste = Convert.ToDateTime(rd["FechaAjuste"]),
                    Activo = rd["Activo"] != DBNull.Value && Convert.ToBoolean(rd["Activo"])
                });
            }
            return lista;
        }
    }
}