using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using ERP.NSQuell.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace ERP.NSQuell.Controllers;

// NSQ_CALENDARIOS_INTERRUPCION_V4
public sealed partial class PlaneacionCalendarioMaquinasController
{
    private sealed class CandidatoInterrupcionUrgenteSeleccionVm
    {
        public int ProgramaProduccionID { get; set; }
        public int? SolicitudProduccionID { get; set; }
        public string OFTexto { get; set; } = string.Empty;
        public string Parte { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public string Molde { get; set; } = string.Empty;
        public string MaquinaActual { get; set; } = string.Empty;
        public DateTime FechaInicioProgramada { get; set; }
        public decimal HorasProgramadas { get; set; }
    }

    // NSQ_LAURA_URGENTE_CALIDAD_COMPATIBLE_V1
    // Calidad iniciada no oculta automaticamente una OF urgente. Solo se
    // permite si la configuracion fisica ya validada sigue en la misma maquina.
    private static async Task<string?> ObtenerMotivoBloqueoInterrupcionUrgenteAsync(
        int programaProduccionId,
        int maquinaDestinoId,
        SqlConnection cn,
        SqlTransaction? tx,
        bool bloquear)
    {
        var hint=bloquear?" WITH (UPDLOCK,HOLDLOCK)":string.Empty;
        var sql=$@"
SELECT TOP(1)
 ISNULL(pp.EstatusID,1) EstatusProgramaID,pp.MaquinaID MaquinaProgramaID,pp.MoldeID MoldeProgramaID,pp.MaterialID MaterialProgramaID,
 ejec.EjecucionProduccionID,ci.InspeccionID,ci.Estado EstadoCalidad,
 ISNULL(ci.ConfiguracionInvalidada,0) ConfiguracionInvalidada,ISNULL(ci.RequiereReliberacion,0) RequiereReliberacion,
 ci.MaquinaID MaquinaCalidadID,ci.MoldeID MoldeCalidadID,ci.MaterialID MaterialCalidadID
FROM dbo.Planeacion_ProgramaProduccion pp{hint}
OUTER APPLY(SELECT TOP(1)e.EjecucionProduccionID FROM dbo.Produccion_Ejecucion e WHERE e.ProgramaProduccionID=pp.ProgramaProduccionID AND e.Activo=1 ORDER BY e.EjecucionProduccionID DESC) ejec
OUTER APPLY(SELECT TOP(1)c.InspeccionID,c.Estado,c.ConfiguracionInvalidada,c.RequiereReliberacion,c.MaquinaID,c.MoldeID,c.MaterialID FROM dbo.Calidad_Inspecciones c WHERE c.ProgramaProduccionID=pp.ProgramaProduccionID ORDER BY c.InspeccionID DESC) ci
WHERE pp.ProgramaProduccionID=@ProgramaProduccionID AND pp.Activo=1;";
        await using var cmd=tx==null?new SqlCommand(sql,cn):new SqlCommand(sql,cn,tx);
        cmd.Parameters.Add("@ProgramaProduccionID",SqlDbType.Int).Value=programaProduccionId;
        await using var rd=await cmd.ExecuteReaderAsync();
        if(!await rd.ReadAsync()) return "No se encontro el programa de produccion.";

        var estatus=rd["EstatusProgramaID"]==DBNull.Value?EstatusPrograma.Programado:Convert.ToInt32(rd["EstatusProgramaID"]);
        if(rd["EjecucionProduccionID"]!=DBNull.Value) return "La OF ya tiene una ejecucion activa de Produccion.";
        if(estatus!=EstatusPrograma.Programado) return $"La OF urgente debe estar Programada. Estado actual: {NombreEstatusPrograma(estatus)}.";
        if(rd["InspeccionID"]==DBNull.Value) return null;

        var inspeccionId=Convert.ToInt32(rd["InspeccionID"]);
        if(rd["ConfiguracionInvalidada"]!=DBNull.Value&&Convert.ToBoolean(rd["ConfiguracionInvalidada"])) return $"La inspeccion de Calidad {inspeccionId} tiene su configuracion invalidada.";
        if(rd["RequiereReliberacion"]!=DBNull.Value&&Convert.ToBoolean(rd["RequiereReliberacion"])) return $"La inspeccion de Calidad {inspeccionId} tiene una reliberacion pendiente.";

        int? mi(string n)=>rd[n]==DBNull.Value?(int?)null:Convert.ToInt32(rd[n]);
        var maquinaPrograma=mi("MaquinaProgramaID"); var maquinaCalidad=mi("MaquinaCalidadID");
        if(maquinaPrograma.HasValue&&maquinaPrograma.Value!=maquinaDestinoId) return $"Calidad ya inicio la inspeccion {inspeccionId} para otra maquina. No se movera esa trazabilidad.";
        if(maquinaCalidad.HasValue&&maquinaCalidad.Value!=maquinaDestinoId) return $"La inspeccion de Calidad {inspeccionId} pertenece a otra maquina.";
        var moldePrograma=mi("MoldeProgramaID"); var moldeCalidad=mi("MoldeCalidadID");
        if(moldePrograma.HasValue&&moldeCalidad.HasValue&&moldePrograma.Value!=moldeCalidad.Value) return $"La inspeccion de Calidad {inspeccionId} ya no coincide con el molde programado.";
        var materialPrograma=mi("MaterialProgramaID"); var materialCalidad=mi("MaterialCalidadID");
        if(materialPrograma.HasValue&&materialCalidad.HasValue&&materialPrograma.Value!=materialCalidad.Value) return $"La inspeccion de Calidad {inspeccionId} ya no coincide con el material programado.";
        return null;
    }
    // NSQ_PRECOMMIT_URGENTE_REFERENCIA_MANUAL_V2
    private async Task<IActionResult> PrevisualizarInterrupcionUrgenteReferenciaAsync(
        string numeroParteUrgente,
        int maquinaId,
        bool trabajarDomingo,
        bool autorizaTerminacionParcial)
    {
        numeroParteUrgente = (numeroParteUrgente ?? string.Empty).Trim();

        try
        {
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync();

            var actual =
                await ObtenerProduccionActivaParaInterrupcionUrgenteAsync(
                    maquinaId,
                    cn);

            if (actual == null)
                return BadRequest(new { ok=false, mensaje="La máquina ya no tiene una OF activa en Producción." });

            if (actual.EstatusProduccionID != EstatusPrograma.EnProduccion)
                return BadRequest(new { ok=false, mensaje="La OF actual no se encuentra produciendo en serie." });

            var parejaActualId =
                await ObtenerProgramaParejaLhRhAsync(
                    actual.ProgramaProduccionID,
                    cn);

            if (autorizaTerminacionParcial && parejaActualId.HasValue)
                return BadRequest(new { ok=false, mensaje="No se permite terminación parcial sobre una pareja LH/RH." });

            var fechaInterrupcion =
                SiguienteAperturaOperativa(
                    NormalizarFecha(DateTime.Now),
                    trabajarDomingo);

            return Json(new
            {
                ok=true,
                modoReferencia=true,
                programaInterrumpido=new
                {
                    programaProduccionID=actual.ProgramaProduccionID,
                    solicitudProduccionID=actual.SolicitudProduccionID,
                    parte=actual.NumeroParte,
                    descripcion=actual.DescripcionParte,
                    molde=actual.MoldeCodigo,
                    finProgramado=actual.FechaFinProgramada,
                    finProyectado=actual.FechaFinProgramada
                },
                programaUrgente=new
                {
                    programaProduccionID=0,
                    solicitudProduccionID=(int?)null,
                    parte=numeroParteUrgente,
                    descripcion="Referencia manual todavía sin OF programada",
                    molde=(string?)null
                },
                maquina=new
                {
                    maquinaID=actual.MaquinaID,
                    codigo=actual.MaquinaCodigo,
                    nombre=actual.MaquinaNombre
                },
                interrupcion=new
                {
                    fechaInterrupcion,
                    fechaArranqueUrgente=(DateTime?)null,
                    fechaFinUrgente=(DateTime?)null,
                    fechaReinicioOriginal=(DateTime?)null,
                    cambiaMolde=false,
                    minutosImpactoTotal=0,
                    programasPosterioresImpactados=0
                },
                terminacionParcialAutorizada=autorizaTerminacionParcial,
                advertencias=new[]
                {
                    "Se registrará el número de parte como referencia. No se inventará una OF ni una duración programada.",
                    "Cuando exista una programación real, Planeación deberá programarla normalmente."
                },
                mensaje=$"Referencia urgente {numeroParteUrgente}. La interrupción se registrará sin exigir una OF futura."
            });
        }
        catch(Exception ex)
        {
            return StatusCode(500,new {ok=false,mensaje="No fue posible validar la referencia urgente: "+ex.Message});
        }
    }

    private async Task<IActionResult> ConfirmarInterrupcionUrgenteReferenciaAsync(
        PlaneacionInterrupcionUrgenteRequest request,
        int usuarioId)
    {
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync();
        await using var tx =
            (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);

        try
        {
            await TomarCandadoCalendarioAsync(cn,tx);

            var actual =
                await ObtenerProduccionActivaParaInterrupcionUrgenteAsync(
                    request.MaquinaID,
                    cn,
                    tx,
                    bloquear:true);

            if(actual==null)
                throw new InvalidOperationException(
                    "La máquina ya no tiene una OF activa en Producción.");

            if(actual.EstatusProduccionID!=EstatusPrograma.EnProduccion)
                throw new InvalidOperationException(
                    "La OF actual ya no se encuentra produciendo.");

            if(await ExisteParoAbiertoEjecucionAsync(
                actual.EjecucionProduccionID,cn,tx))
                throw new InvalidOperationException(
                    "La ejecución actual ya tiene un paro abierto.");

            var parejaId =
                await ObtenerProgramaParejaLhRhAsync(
                    actual.ProgramaProduccionID,
                    cn,
                    tx);

            ProgramaActivoInterrupcionUrgente? pareja=null;

            if(request.AutorizaTerminacionParcial && parejaId.HasValue)
                throw new InvalidOperationException(
                    "No se permite terminación parcial sobre una pareja LH/RH.");

            if(parejaId.HasValue)
            {
                pareja =
                    await ObtenerProduccionActivaProgramaInterrupcionUrgenteAsync(
                        parejaId.Value,
                        request.MaquinaID,
                        cn,
                        tx,
                        bloquear:true);

                if(pareja==null ||
                   pareja.EstatusProduccionID!=EstatusPrograma.EnProduccion)
                    throw new InvalidOperationException(
                        "La contraparte LH/RH no tiene una ejecución activa. No se realizará una interrupción parcial.");

                if(pareja.MaquinaID!=actual.MaquinaID)
                    throw new InvalidOperationException(
                        "Las contrapartes LH/RH ya no están en la misma máquina.");

                if(!CoincidenMoldesInterrupcionUrgente(
                    actual.MoldeID,
                    actual.MoldeCodigo,
                    pareja.MoldeID,
                    pareja.MoldeCodigo))
                    throw new InvalidOperationException(
                        "Las contrapartes LH/RH ya no conservan el mismo molde.");

                if(actual.FechaInicioProgramada!=pareja.FechaInicioProgramada ||
                   actual.FechaFinProgramada!=pareja.FechaFinProgramada)
                    throw new InvalidOperationException(
                        "Las contrapartes LH/RH ya no conservan la misma ventana programada.");

                if(await ExisteParoAbiertoEjecucionAsync(
                    pareja.EjecucionProduccionID,cn,tx))
                    throw new InvalidOperationException(
                        "La contraparte LH/RH ya tiene un paro abierto.");
            }

            var fecha =
                SiguienteAperturaOperativa(
                    NormalizarFecha(DateTime.Now),
                    request.TrabajarDomingo);

            var esLhRh=pareja!=null;
            Guid? grupo=esLhRh?Guid.NewGuid():null;
            var parte=(request.NumeroParteUrgente??string.Empty).Trim();
            var descripcion=
                $"{request.Motivo} | Parte urgente prevista: {parte}";

            var paroId =
                await CrearParoInterrupcionUrgenteAsync(
                    actual,
                    null,
                    fecha,
                    descripcion,
                    esLhRh,
                    grupo,
                    request.AutorizaTerminacionParcial,
                    request.MotivoTerminacionParcial,
                    usuarioId,
                    cn,
                    tx);

            int? paroParejaId=null;

            if(pareja!=null)
            {
                paroParejaId =
                    await CrearParoInterrupcionUrgenteAsync(
                        pareja,
                        null,
                        fecha,
                        descripcion,
                        true,
                        grupo,
                        false,
                        null,
                        usuarioId,
                        cn,
                        tx);
            }

            await PausarProduccionPorInterrupcionUrgenteAsync(
                actual,usuarioId,cn,tx);

            if(pareja!=null)
                await PausarProduccionPorInterrupcionUrgenteAsync(
                    pareja,usuarioId,cn,tx);

            await tx.CommitAsync();

            return Json(new
            {
                ok=true,
                modoReferencia=true,
                paroID=paroId,
                paroParejaID=paroParejaId,
                grupoParoLhRh=grupo,
                programaInterrumpidoID=actual.ProgramaProduccionID,
                programaInterrumpidoParejaID=pareja?.ProgramaProduccionID,
                programaUrgenteID=(int?)null,
                numeroParteUrgente=parte,
                fechaInterrupcion=fecha,
                mensaje=pareja!=null
                    ? $"Interrupción registrada para LH/RH. Referencia urgente: {parte}."
                    : $"Interrupción registrada. Referencia urgente: {parte}."
            });
        }
        catch(SqlException ex) when (
            ex.Number==51010 ||
            ex.Number==51620 ||
            ex.Number==51621 ||
            ex.Number==51622)
        {
            await RollbackSeguroAsync(tx);
            return BadRequest(new {ok=false,mensaje=ex.Message});
        }
        catch(InvalidOperationException ex)
        {
            await RollbackSeguroAsync(tx);
            return BadRequest(new {ok=false,mensaje=ex.Message});
        }
        catch(Exception ex)
        {
            await RollbackSeguroAsync(tx);
            return StatusCode(
                500,
                new {ok=false,mensaje="No fue posible registrar la interrupción por referencia: "+ex.Message});
        }
    }
    [HttpGet("CandidatosInterrupcionUrgente")]
    public async Task<IActionResult> CandidatosInterrupcionUrgente(
        int maquinaId,
        int programaInterrumpidoId)
    {
        if (!UsuarioEnSesion())
        {
            return Unauthorized(new
            {
                ok = false,
                sesionExpirada = true,
                mensaje = "La sesion termino. Vuelve a iniciar sesion."
            });
        }

        if (maquinaId <= 0 || programaInterrumpidoId <= 0)
        {
            return BadRequest(new
            {
                ok = false,
                mensaje = "La maquina y el programa producido son obligatorios."
            });
        }

        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync();

        var actual =
            await ObtenerProduccionActivaParaInterrupcionUrgenteAsync(
                maquinaId,
                cn);

        if (actual == null)
        {
            return BadRequest(new
            {
                ok = false,
                mensaje = "La maquina ya no tiene una OF en produccion que pueda interrumpirse."
            });
        }

        if (actual.ProgramaProduccionID != programaInterrumpidoId)
        {
            return BadRequest(new
            {
                ok = false,
                mensaje =
                    "La produccion activa de la maquina cambio. Recarga el calendario antes de continuar."
            });
        }

        const string sql = @"
SELECT TOP(150)
    pp.ProgramaProduccionID
FROM dbo.Planeacion_ProgramaProduccion pp
WHERE pp.Activo = 1
  AND ISNULL(pp.EstatusID,1) = 1
  AND pp.ProgramaProduccionID <> @ProgramaInterrumpidoID
  AND pp.FechaInicioProgramada IS NOT NULL
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.Produccion_Ejecucion e
      WHERE e.ProgramaProduccionID = pp.ProgramaProduccionID
        AND e.Activo = 1
  )
ORDER BY
    pp.FechaInicioProgramada,
    pp.ProgramaProduccionID;";

        var ids = new List<int>();

        await using (var cmd = new SqlCommand(sql, cn))
        {
            cmd.Parameters.Add(
                "@ProgramaInterrumpidoID",
                SqlDbType.Int).Value = programaInterrumpidoId;

            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
                ids.Add(Convert.ToInt32(rd["ProgramaProduccionID"]));
        }

        var candidatos =
            new List<CandidatoInterrupcionUrgenteSeleccionVm>();

        foreach (var id in ids)
        {
            var programa =
                await ObtenerProgramaBaseAsync(
                    id,
                    cn,
                    null,
                    bloquear: false);

            if (programa == null)
                continue;

            var bloqueo =
                await ObtenerMotivoBloqueoInterrupcionUrgenteAsync(
                    id,
                    maquinaId,
                    cn,
                    null,
                    bloquear: false);

            if (!string.IsNullOrWhiteSpace(bloqueo))
                continue;

            var compatibles =
                await ObtenerMaquinasCompatiblesAsync(
                    programa,
                    cn,
                    null);

            if (!compatibles.Any(x => x.MaquinaID == maquinaId))
                continue;

            candidatos.Add(
                new CandidatoInterrupcionUrgenteSeleccionVm
                {
                    ProgramaProduccionID = programa.ProgramaProduccionID,
                    SolicitudProduccionID = programa.SolicitudProduccionID,
                    OFTexto = programa.SolicitudProduccionID.HasValue
                        ? $"OF {programa.SolicitudProduccionID.Value}"
                        : $"Programa {programa.ProgramaProduccionID}",
                    Parte =
                        !string.IsNullOrWhiteSpace(programa.NumeroParte)
                            ? programa.NumeroParte
                            : !string.IsNullOrWhiteSpace(programa.ReferenciaSAP)
                                ? programa.ReferenciaSAP
                                : "Sin parte",
                    Descripcion = programa.DescripcionParte ?? string.Empty,
                    Molde = programa.MoldeCodigo ?? "Sin molde",
                    MaquinaActual =
                        string.Join(
                            " - ",
                            new[]
                            {
                                programa.MaquinaCodigo,
                                programa.MaquinaNombre
                            }
                            .Where(x => !string.IsNullOrWhiteSpace(x))
                            .Distinct()),
                    FechaInicioProgramada = programa.FechaInicioProgramada,
                    HorasProgramadas = programa.HorasProgramadas
                });
        }

        return Json(new
        {
            ok = true,
            maquinaID = maquinaId,
            programaInterrumpidoID = programaInterrumpidoId,
            candidatos = candidatos.Select(x => new
            {
                programaProduccionID = x.ProgramaProduccionID,
                solicitudProduccionID = x.SolicitudProduccionID,
                of = x.OFTexto,
                parte = x.Parte,
                descripcion = x.Descripcion,
                molde = x.Molde,
                maquinaActual = x.MaquinaActual,
                fechaInicioProgramada = x.FechaInicioProgramada,
                horasProgramadas = x.HorasProgramadas
            })
        });
    }
}