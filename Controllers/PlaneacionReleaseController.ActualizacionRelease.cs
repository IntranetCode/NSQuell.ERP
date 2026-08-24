using ERP.NSQuell.Models;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Collections.Generic;
using System.Linq;

namespace ERP.NSQuell.Controllers;

// RELEASE_UPDATE_SAME_ID_V1_0
// Actualiza el MISMO ReleaseID.
// Regla de corte:
//   FechaRequerida < DateTime.Today  => historico intocable.
//   FechaRequerida >= DateTime.Today => se sincroniza con el nuevo documento.
//
// Seguridad:
// - La identidad de la parte siempre es ParteID / ERP_Partes.
// - El match de demanda es ParteID + FechaRequerida.
// - No se toca historico.
// - No se retira automaticamente una demanda generica que no venga en el archivo.
// - Si ya existe produccion ejecutada o SolicitudProduccionID, no se retira esa demanda.
// - Programacion sin produccion se ajusta automaticamente solo cuando existe un unico
//   programa activo para ese detalle.
public partial class PlaneacionReleaseController
{
    private sealed class ReleaseActualizacionEntrada
    {
        public int ParteID { get; init; }
        public DateTime FechaRequerida { get; init; }
        public int CantidadRequerida { get; init; }
        public PlaneacionReleaseRenglonCrearVm Renglon { get; init; } = new();
        public PlaneacionReleaseEntregaCrearVm Entrega { get; init; } = new();
    }

    private sealed class ReleaseActualizacionExistente
    {
        public int ReleaseDetalleID { get; init; }
        public int ReleaseRenglonID { get; init; }
        public int Renglon { get; init; }
        public int ParteID { get; init; }
        public string NumeroParte { get; init; } = string.Empty;
        public DateTime FechaRequerida { get; init; }
        public int CantidadRequerida { get; init; }
        public int? SolicitudProduccionID { get; init; }
        public int ProgramasActivos { get; init; }
        public int CantidadProducida { get; init; }
    }

    private sealed class ReleaseActualizacionResumen
    {
        public int ReleaseID { get; init; }
        public string? FolioRelease { get; init; }
        public int Modificadas { get; set; }
        public int Nuevas { get; set; }
        public int Retiradas { get; set; }
        public int SinCambio { get; set; }
        public int HistoricasProtegidas { get; set; }
        public int ProtegidasPorProduccion { get; set; }
        public int ProgramacionesAjustadas { get; set; }
        public List<string> Advertencias { get; } = new();

        public string ConstruirMensaje()
        {
            var texto =
                $"Release #{ReleaseID} ACTUALIZADO. " +
                $"{Modificadas} modificada(s), {Nuevas} nueva(s), " +
                $"{Retiradas} retirada(s), {SinCambio} sin cambio. " +
                $"Historico protegido: {HistoricasProtegidas}. " +
                $"Programacion ajustada: {ProgramacionesAjustadas}.";

            if (ProtegidasPorProduccion > 0)
            {
                texto +=
                    $" {ProtegidasPorProduccion} entrega(s) se conservaron " +
                    "porque ya tienen produccion/solicitud relacionada.";
            }

            return texto;
        }
    }

    private static string NormalizarClaveRelease(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return new string(value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());
    }

    private static List<ReleaseActualizacionEntrada> ConstruirEntradasActualizacion(
        ReleaseValidacionDocumentoVm documento)
    {
        if (documento.ReleasePreparado?.Renglones == null)
            return new List<ReleaseActualizacionEntrada>();

        return documento.ReleasePreparado.Renglones
            .Where(r => r.ParteID.HasValue)
            .SelectMany(r => r.Entregas
                .Where(e =>
                    e.FechaRequerida.HasValue &&
                    e.CantidadRequerida > 0)
                .Select(e => new ReleaseActualizacionEntrada
                {
                    ParteID = r.ParteID!.Value,
                    FechaRequerida = e.FechaRequerida!.Value.Date,
                    CantidadRequerida = e.CantidadRequerida,
                    Renglon = r,
                    Entrega = e
                }))
            .GroupBy(x => new
            {
                x.ParteID,
                Fecha = x.FechaRequerida.Date
            })
            .Select(g =>
            {
                var first = g.First();
                return new ReleaseActualizacionEntrada
                {
                    ParteID = g.Key.ParteID,
                    FechaRequerida = g.Key.Fecha,
                    CantidadRequerida = g.Sum(x => x.CantidadRequerida),
                    Renglon = first.Renglon,
                    Entrega = new PlaneacionReleaseEntregaCrearVm
                    {
                        SecuenciaEntrega = first.Entrega.SecuenciaEntrega,
                        FechaCarga = first.Entrega.FechaCarga,
                        FechaRequerida = g.Key.Fecha,
                        CantidadRequerida = g.Sum(x => x.CantidadRequerida)
                    }
                };
            })
            .OrderBy(x => x.ParteID)
            .ThenBy(x => x.FechaRequerida)
            .ToList();
    }

    private static string[] PlantillasCompatiblesActualizacion(string plantilla)
    {
        return plantilla switch
        {
            "GOLDE_MEXICO_WEEKLY_RELEASE" =>
                new[] { "GOLDE_MEXICO_WEEKLY_RELEASE", "GOLDEN_WEEKLY_RELEASE" },

            "GOLDEN_WEEKLY_RELEASE" =>
                new[] { "GOLDEN_WEEKLY_RELEASE", "GOLDE_USA_WEEKLY_RELEASE" },

            "GOLDE_USA_WEEKLY_RELEASE" =>
                new[] { "GOLDE_USA_WEEKLY_RELEASE", "GOLDEN_WEEKLY_RELEASE" },

            _ => new[] { plantilla }
        };
    }

    private static bool PermiteRetiroAutomaticoActualizacion(string plantilla)
    {
        return plantilla is
            "HUF_SUPPLIER_SCHEDULE" or
            "VERITAS_SCHEDULE" or
            "GOLDE_MEXICO_WEEKLY_RELEASE" or
            "GOLDEN_WEEKLY_RELEASE" or
            "GOLDE_USA_WEEKLY_RELEASE" or
            "NORMA_WEEKLY_RELEASE" or
            "AIR_THERMAL_MATERIAL_RELEASE";
    }

    private async Task DetectarActualizacionExistenteValidacionAsync(
        ReleaseValidacionDocumentoVm documento,
        SqlConnection cn,
        SqlTransaction tx)
    {
        if (documento.Estado != ReleaseValidacionEstados.Validado ||
            !documento.ClienteID.HasValue ||
            documento.ReleasePreparado?.Renglones == null ||
            documento.ReleasePreparado.Renglones.Count == 0)
        {
            return;
        }

        var entradas = ConstruirEntradasActualizacion(documento);
        if (entradas.Count == 0)
            return;

        var parteIds = entradas
            .Select(x => x.ParteID)
            .Distinct()
            .ToList();

        var releaseId = await BuscarReleaseExistenteParaActualizacionAsync(
            documento,
            parteIds,
            cn,
            tx);

        if (!releaseId.HasValue)
            return;

        var cabecera = await ObtenerCabeceraReleaseActualizacionAsync(
            releaseId.Value,
            cn,
            tx);

        if (cabecera == null)
            return;

        var existentes = await CargarDemandasExistentesActualizacionAsync(
            releaseId.Value,
            cn,
            tx);

        var duplicados = existentes
            .Where(x => x.FechaRequerida.Date >= DateTime.Today)
            .GroupBy(x => new { x.ParteID, Fecha = x.FechaRequerida.Date })
            .Where(g => g.Count() > 1)
            .ToList();

        if (duplicados.Count > 0)
        {
            documento.Advertencias.Add(
                "Se encontro mas de un detalle activo para la misma ParteID + FechaRequerida. " +
                "La actualizacion automatica se bloqueara hasta corregir esa ambiguedad.");
            return;
        }

        var incoming = entradas
            .Where(x => x.FechaRequerida.Date >= DateTime.Today)
            .ToDictionary(
                x => (x.ParteID, x.FechaRequerida.Date),
                x => x);

        var current = existentes
            .Where(x => x.FechaRequerida.Date >= DateTime.Today)
            .ToDictionary(
                x => (x.ParteID, x.FechaRequerida.Date),
                x => x);

        var historicas = existentes.Count(
            x => x.FechaRequerida.Date < DateTime.Today);

        var modificadas = incoming.Count(x =>
            current.TryGetValue(x.Key, out var old) &&
            old.CantidadRequerida != x.Value.CantidadRequerida);

        var sinCambio = incoming.Count(x =>
            current.TryGetValue(x.Key, out var old) &&
            old.CantidadRequerida == x.Value.CantidadRequerida);

        var nuevas = incoming.Count(x => !current.ContainsKey(x.Key));

        var faltantes = current
            .Where(x => !incoming.ContainsKey(x.Key))
            .Select(x => x.Value)
            .ToList();

        var retirables = PermiteRetiroAutomaticoActualizacion(documento.Plantilla)
            ? faltantes.Count
            : 0;

        documento.ReleaseID = releaseId.Value;
        documento.FolioRelease = cabecera.Value.FolioRelease;

        documento.Mensaje =
            $"ACTUALIZACION DETECTADA - Release #{releaseId.Value} " +
            $"({cabecera.Value.FolioRelease ?? "sin folio"}). " +
            $"Corte por FechaRequerida: {DateTime.Today:dd/MM/yyyy}. " +
            $"Historico intocable: {historicas}. " +
            $"Cambios: {modificadas}; nuevas: {nuevas}; " +
            $"retiros futuros: {retirables}; sin cambio: {sinCambio}.";

        documento.Advertencias.Insert(
            0,
            $"Se actualizara el MISMO ReleaseID {releaseId.Value}; no se creara otro Release.");

        if (cabecera.Value.ClienteID != documento.ClienteID.Value)
        {
            documento.Advertencias.Add(
                $"El Release existente tiene ClienteID {cabecera.Value.ClienteID} y el documento fue " +
                $"reconocido como ClienteID {documento.ClienteID.Value}. Al actualizar se corregira el encabezado.");
        }

        foreach (var item in incoming
            .Where(x =>
                current.TryGetValue(x.Key, out var old) &&
                old.CantidadRequerida != x.Value.CantidadRequerida)
            .Take(8))
        {
            var old = current[item.Key];
            documento.Advertencias.Add(
                $"MODIFICAR {item.Value.Renglon.NumeroParte} " +
                $"{item.Key.Item2:dd/MM/yyyy}: " +
                $"{old.CantidadRequerida:N0} -> {item.Value.CantidadRequerida:N0}.");
        }

        foreach (var item in incoming
            .Where(x => !current.ContainsKey(x.Key))
            .Take(6))
        {
            documento.Advertencias.Add(
                $"NUEVA {item.Value.Renglon.NumeroParte} " +
                $"{item.Key.Item2:dd/MM/yyyy}: {item.Value.CantidadRequerida:N0}.");
        }

        if (PermiteRetiroAutomaticoActualizacion(documento.Plantilla))
        {
            foreach (var item in faltantes.Take(6))
            {
                var proteccion = item.CantidadProducida > 0 ||
                                 item.SolicitudProduccionID.HasValue;

                documento.Advertencias.Add(
                    proteccion
                        ? $"CONSERVAR POR PRODUCCION {item.NumeroParte} {item.FechaRequerida:dd/MM/yyyy}: " +
                          $"{item.CantidadRequerida:N0}."
                        : $"RETIRAR FUTURO {item.NumeroParte} {item.FechaRequerida:dd/MM/yyyy}: " +
                          $"{item.CantidadRequerida:N0}.");
            }
        }
        else if (faltantes.Count > 0)
        {
            documento.Advertencias.Add(
                $"{faltantes.Count} entrega(s) futuras existentes no aparecen en el lector generico. " +
                "Por seguridad NO se retiraran automaticamente.");
        }
    }

    private async Task<int?> BuscarReleaseExistenteParaActualizacionAsync(
        ReleaseValidacionDocumentoVm documento,
        List<int> parteIds,
        SqlConnection cn,
        SqlTransaction tx)
    {
        if (!documento.ClienteID.HasValue || parteIds.Count == 0)
            return null;

        var plantillas = PlantillasCompatiblesActualizacion(documento.Plantilla);
        var folioNormalizado = NormalizarClaveRelease(documento.FolioCliente);

        if (documento.Plantilla == "VERITAS_SCHEDULE" &&
            !string.IsNullOrWhiteSpace(folioNormalizado))
        {
            const string sqlVeritas = @"
SELECT TOP (1)
    r.ReleaseID
FROM dbo.Planeacion_Releases r
WHERE r.Activo = 1
  AND r.EstatusID NOT IN (9, 99)
  AND r.PlantillaImportacion = N'VERITAS_SCHEDULE'
  AND UPPER(
        REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
            ISNULL(r.FolioCliente, N''),
            N' ', N''), N'/', N''), N'-', N''), N'.', N''), N'_', N'')
      ) = @Folio
ORDER BY
    CASE WHEN r.ClienteID = @ClienteID THEN 0 ELSE 1 END,
    CASE WHEN r.EstatusID = 9 THEN 1 ELSE 0 END,
    r.ReleaseID DESC;";

            await using var cmd = new SqlCommand(sqlVeritas, cn, tx);
            cmd.Parameters.Add("@ClienteID", SqlDbType.Int).Value = documento.ClienteID.Value;
            cmd.Parameters.Add("@Folio", SqlDbType.NVarChar, 150).Value = folioNormalizado;

            var result = await cmd.ExecuteScalarAsync();
            return result == null || result == DBNull.Value
                ? null
                : Convert.ToInt32(result);
        }

        if (documento.Plantilla == "HUF_SUPPLIER_SCHEDULE")
        {
            const string sqlHuf = @"
SELECT TOP (1)
    r.ReleaseID
FROM dbo.Planeacion_Releases r
INNER JOIN dbo.Planeacion_ReleaseDetalle d
    ON d.ReleaseID = r.ReleaseID
   AND d.Activo = 1
WHERE r.Activo = 1
  AND r.EstatusID NOT IN (9, 99)
  AND r.PlantillaImportacion = N'HUF_SUPPLIER_SCHEDULE'
  AND r.ClienteID = @ClienteID
  AND d.ParteID = @ParteID
ORDER BY
    CASE WHEN r.EstatusID = 9 THEN 1 ELSE 0 END,
    r.ReleaseID DESC;";

            await using var cmd = new SqlCommand(sqlHuf, cn, tx);
            cmd.Parameters.Add("@ClienteID", SqlDbType.Int).Value = documento.ClienteID.Value;
            cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value = parteIds[0];

            var result = await cmd.ExecuteScalarAsync();
            return result == null || result == DBNull.Value
                ? null
                : Convert.ToInt32(result);
        }

        var parameters = new List<string>();
        for (var i = 0; i < parteIds.Count; i++)
            parameters.Add($"@Parte{i}");

        var templateParameters = new List<string>();
        for (var i = 0; i < plantillas.Length; i++)
            templateParameters.Add($"@Plantilla{i}");

        var minCoincidencias = Math.Min(
            parteIds.Count,
            Math.Max(1, Math.Min(3, parteIds.Count)));

        var sql = $@"
SELECT TOP (1)
    r.ReleaseID
FROM dbo.Planeacion_Releases r
INNER JOIN dbo.Planeacion_ReleaseDetalle d
    ON d.ReleaseID = r.ReleaseID
   AND d.Activo = 1
WHERE r.Activo = 1
  AND r.EstatusID NOT IN (9, 99)
  AND r.PlantillaImportacion IN ({string.Join(", ", templateParameters)})
  AND d.ParteID IN ({string.Join(", ", parameters)})
GROUP BY
    r.ReleaseID,
    r.ClienteID,
    r.EstatusID
HAVING COUNT(DISTINCT d.ParteID) >= @MinCoincidencias
ORDER BY
    CASE WHEN r.ClienteID = @ClienteID THEN 0 ELSE 1 END,
    CASE WHEN r.EstatusID = 9 THEN 1 ELSE 0 END,
    COUNT(DISTINCT d.ParteID) DESC,
    r.ReleaseID DESC;";

        await using var generic = new SqlCommand(sql, cn, tx);
        generic.Parameters.Add("@ClienteID", SqlDbType.Int).Value = documento.ClienteID.Value;
        generic.Parameters.Add("@MinCoincidencias", SqlDbType.Int).Value = minCoincidencias;

        for (var i = 0; i < parteIds.Count; i++)
            generic.Parameters.Add(parameters[i], SqlDbType.Int).Value = parteIds[i];

        for (var i = 0; i < plantillas.Length; i++)
            generic.Parameters.Add(templateParameters[i], SqlDbType.NVarChar, 100).Value = plantillas[i];

        var candidate = await generic.ExecuteScalarAsync();
        return candidate == null || candidate == DBNull.Value
            ? null
            : Convert.ToInt32(candidate);
    }

    private static async Task<(string? FolioRelease, int ClienteID, int EstatusID)?>
        ObtenerCabeceraReleaseActualizacionAsync(
            int releaseId,
            SqlConnection cn,
            SqlTransaction tx)
    {
        const string sql = @"
SELECT
    FolioRelease,
    ClienteID,
    EstatusID
FROM dbo.Planeacion_Releases
WHERE ReleaseID = @ReleaseID
  AND Activo = 1;";

        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@ReleaseID", SqlDbType.Int).Value = releaseId;

        await using var rd = await cmd.ExecuteReaderAsync();
        if (!await rd.ReadAsync())
            return null;

        return (
            rd["FolioRelease"] as string,
            rd["ClienteID"] == DBNull.Value ? 0 : Convert.ToInt32(rd["ClienteID"]),
            Convert.ToInt32(rd["EstatusID"]));
    }

    private static async Task<List<ReleaseActualizacionExistente>>
        CargarDemandasExistentesActualizacionAsync(
            int releaseId,
            SqlConnection cn,
            SqlTransaction tx)
    {
        const string sql = @"
SELECT
    d.ReleaseDetalleID,
    d.ReleaseRenglonID,
    d.Renglon,
    d.ParteID,
    d.NumeroParte,
    d.FechaRequerida,
    d.CantidadRequerida,
    d.SolicitudProduccionID,
    ISNULL((
        SELECT COUNT(1)
        FROM dbo.Planeacion_ProgramaProduccion pp
        WHERE pp.ReleaseDetalleID = d.ReleaseDetalleID
          AND pp.Activo = 1
          AND ISNULL(pp.EstatusID, 1) NOT IN (9, 99)
    ), 0) AS ProgramasActivos,
    ISNULL((
        SELECT SUM(ISNULL(pp.CantidadProducida, 0))
        FROM dbo.Planeacion_ProgramaProduccion pp
        WHERE pp.ReleaseDetalleID = d.ReleaseDetalleID
          AND pp.Activo = 1
    ), 0) AS CantidadProducida
FROM dbo.Planeacion_ReleaseDetalle d
WHERE d.ReleaseID = @ReleaseID
  AND d.Activo = 1
  AND d.ParteID IS NOT NULL
ORDER BY d.FechaRequerida, d.ParteID, d.ReleaseDetalleID;";

        var result = new List<ReleaseActualizacionExistente>();

        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@ReleaseID", SqlDbType.Int).Value = releaseId;

        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
        {
            result.Add(new ReleaseActualizacionExistente
            {
                ReleaseDetalleID = Convert.ToInt32(rd["ReleaseDetalleID"]),
                ReleaseRenglonID = rd["ReleaseRenglonID"] == DBNull.Value
                    ? 0
                    : Convert.ToInt32(rd["ReleaseRenglonID"]),
                Renglon = Convert.ToInt32(rd["Renglon"]),
                ParteID = Convert.ToInt32(rd["ParteID"]),
                NumeroParte = rd["NumeroParte"] as string ?? string.Empty,
                FechaRequerida = Convert.ToDateTime(rd["FechaRequerida"]).Date,
                CantidadRequerida = Convert.ToInt32(rd["CantidadRequerida"]),
                SolicitudProduccionID = rd["SolicitudProduccionID"] == DBNull.Value
                    ? null
                    : Convert.ToInt32(rd["SolicitudProduccionID"]),
                ProgramasActivos = Convert.ToInt32(rd["ProgramasActivos"]),
                CantidadProducida = Convert.ToInt32(rd["CantidadProducida"])
            });
        }

        return result;
    }

    private async Task<ReleaseActualizacionResumen>
        ActualizarReleaseExistenteDesdeDocumentoAsync(
            ReleaseValidacionDocumentoVm documento,
            string? archivoGuardado,
            int usuarioId,
            SqlConnection cn,
            SqlTransaction tx)
    {
        if (!documento.ReleaseID.HasValue)
            throw new InvalidOperationException("No existe ReleaseID objetivo para actualizar.");

        if (!documento.ClienteID.HasValue)
            throw new InvalidOperationException("El documento no tiene ClienteID para actualizar.");

        if (documento.ReleasePreparado.Renglones.Any(x => !x.ParteID.HasValue))
            throw new InvalidOperationException("No se puede actualizar mientras existan partes sin vincular.");

        var releaseId = documento.ReleaseID.Value;
        var cabecera = await ObtenerCabeceraReleaseActualizacionAsync(releaseId, cn, tx)
            ?? throw new InvalidOperationException($"El Release #{releaseId} ya no existe o no esta activo.");

        if (cabecera.EstatusID == PlaneacionReleaseEstatus.Cancelado)
            throw new InvalidOperationException($"El Release #{releaseId} esta cancelado.");

        var entradas = ConstruirEntradasActualizacion(documento);
        var existentes = await CargarDemandasExistentesActualizacionAsync(releaseId, cn, tx);

        var duplicados = existentes
            .Where(x => x.FechaRequerida.Date >= DateTime.Today)
            .GroupBy(x => new { x.ParteID, Fecha = x.FechaRequerida.Date })
            .Where(g => g.Count() > 1)
            .ToList();

        if (duplicados.Count > 0)
        {
            throw new InvalidOperationException(
                "No se puede actualizar automaticamente porque el Release tiene " +
                "mas de un detalle activo para la misma ParteID + FechaRequerida.");
        }

        var resumen = new ReleaseActualizacionResumen
        {
            ReleaseID = releaseId,
            FolioRelease = cabecera.FolioRelease,
            HistoricasProtegidas = existentes.Count(x =>
                x.FechaRequerida.Date < DateTime.Today)
        };

        await ActualizarCabeceraReleaseDesdeDocumentoAsync(
            releaseId,
            documento,
            archivoGuardado,
            usuarioId,
            cn,
            tx);

        var rowNumberByPart = await PrepararRenglonesActualizacionAsync(
            releaseId,
            documento,
            usuarioId,
            cn,
            tx);

        var existingMap = existentes
            .Where(x => x.FechaRequerida.Date >= DateTime.Today)
            .ToDictionary(
                x => (x.ParteID, x.FechaRequerida.Date),
                x => x);

        var incomingFuture = entradas
            .Where(x => x.FechaRequerida.Date >= DateTime.Today)
            .ToDictionary(
                x => (x.ParteID, x.FechaRequerida.Date),
                x => x);

        foreach (var item in incomingFuture.Values
            .OrderBy(x => x.FechaRequerida)
            .ThenBy(x => x.ParteID))
        {
            var key = (item.ParteID, item.FechaRequerida.Date);

            if (existingMap.TryGetValue(key, out var old))
            {
                if (old.CantidadRequerida == item.CantidadRequerida)
                {
                    resumen.SinCambio++;
                    continue;
                }

                await ActualizarCantidadDetalleReleaseAsync(
                    old.ReleaseDetalleID,
                    item,
                    rowNumberByPart[item.ParteID],
                    usuarioId,
                    cn,
                    tx);

                var delta = item.CantidadRequerida - old.CantidadRequerida;
                var programacion = await AjustarProgramacionPorDeltaAsync(
                    old.ReleaseDetalleID,
                    delta,
                    cn,
                    tx);

                if (programacion.Ajustada)
                    resumen.ProgramacionesAjustadas++;

                if (!string.IsNullOrWhiteSpace(programacion.Advertencia))
                    resumen.Advertencias.Add(programacion.Advertencia);

                var detalle = new PlaneacionReleaseDetalleCrearVm
                {
                    ReleaseDetalleID = old.ReleaseDetalleID,
                    Renglon = rowNumberByPart[item.ParteID].Renglon,
                    ParteID = item.ParteID,
                    NumeroParte = item.Renglon.NumeroParte,
                    ReferenciaSAP = item.Renglon.ReferenciaSAP,
                    DesignacionDescripcionSAP = item.Renglon.DesignacionDescripcionSAP,
                    FechaRequerida = item.FechaRequerida,
                    CantidadRequerida = item.CantidadRequerida,
                    EstatusID = PlaneacionReleaseEstatus.Calculado
                };

                await CompletarDetalleDesdeParteAsync(detalle, cn, tx);
                await CalcularNecesidadAsync(detalle, cn, tx);
                await ActualizarReleaseDetalleCalculoAsync(
                    detalle,
                    usuarioId,
                    cn,
                    tx);

                resumen.Modificadas++;
                continue;
            }

            var rowInfo = rowNumberByPart[item.ParteID];
            var secuencia = await ObtenerSiguienteSecuenciaEntregaAsync(
                releaseId,
                rowInfo.ReleaseRenglonID,
                cn,
                tx);

            var entrega = new PlaneacionReleaseEntregaCrearVm
            {
                SecuenciaEntrega = secuencia,
                FechaCarga = item.Entrega.FechaCarga,
                FechaRequerida = item.FechaRequerida,
                CantidadRequerida = item.CantidadRequerida
            };

            var detalleNuevo = CrearDetalleDesdeRenglonEntrega(
                item.Renglon,
                entrega);

            detalleNuevo.Renglon = rowInfo.Renglon;
            detalleNuevo.ParteID = item.ParteID;

            await CompletarDetalleDesdeParteAsync(detalleNuevo, cn, tx);
            await CalcularNecesidadAsync(detalleNuevo, cn, tx);

            await InsertarReleaseDetalleAsync(
                releaseId,
                rowInfo.ReleaseRenglonID,
                secuencia,
                detalleNuevo,
                usuarioId,
                cn,
                tx);

            resumen.Nuevas++;
        }

        var missing = existingMap
            .Where(x => !incomingFuture.ContainsKey(x.Key))
            .Select(x => x.Value)
            .ToList();

        if (PermiteRetiroAutomaticoActualizacion(documento.Plantilla))
        {
            foreach (var old in missing)
            {
                if (old.CantidadProducida > 0 || old.SolicitudProduccionID.HasValue)
                {
                    resumen.ProtegidasPorProduccion++;
                    resumen.Advertencias.Add(
                        $"Se conservo {old.NumeroParte} {old.FechaRequerida:dd/MM/yyyy} " +
                        $"({old.CantidadRequerida:N0}) porque ya tiene produccion/solicitud relacionada.");
                    continue;
                }

                await RetirarDetalleFuturoYProgramacionAsync(
                    old.ReleaseDetalleID,
                    usuarioId,
                    cn,
                    tx);

                resumen.Retiradas++;
            }
        }
        else if (missing.Count > 0)
        {
            resumen.Advertencias.Add(
                $"{missing.Count} entrega(s) futuras no aparecen en el documento generico. " +
                "Se conservaron por seguridad.");
        }

        if (cabecera.EstatusID == PlaneacionReleaseEstatus.Capturado)
        {
            await ActualizarEstatusReleaseAsync(
                releaseId,
                PlaneacionReleaseEstatus.Calculado,
                usuarioId,
                cn,
                tx);
        }

        foreach (var warning in resumen.Advertencias.Take(10))
        {
            if (!documento.Advertencias.Contains(warning))
                documento.Advertencias.Add(warning);
        }

        return resumen;
    }

    private static async Task ActualizarCabeceraReleaseDesdeDocumentoAsync(
        int releaseId,
        ReleaseValidacionDocumentoVm documento,
        string? archivoGuardado,
        int usuarioId,
        SqlConnection cn,
        SqlTransaction tx)
    {
        const string sql = @"
UPDATE dbo.Planeacion_Releases
SET
    FolioCliente = @FolioCliente,
    ClienteID = @ClienteID,
    ClienteNombre = @ClienteNombre,
    FechaRecepcion = @FechaRecepcion,
    VersionRelease = @VersionRelease,
    ArchivoOrigenNombre = @ArchivoOrigenNombre,
    PlantillaImportacion = @PlantillaImportacion,
    ImportadoDesdeArchivo = 1,
    Observaciones = LEFT(
        CONCAT(
            N'ACTUALIZACION_RELEASE;',
            N'SHA256:', @Sha256, N';',
            N'ARCHIVO_GUARDADO:', ISNULL(@ArchivoGuardado, N''), N';',
            ISNULL(Observaciones, N'')
        ),
        500
    ),
    UsuarioModificacionID = @UsuarioID,
    FechaModificacion = GETDATE()
WHERE ReleaseID = @ReleaseID
  AND Activo = 1;";

        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@ReleaseID", SqlDbType.Int).Value = releaseId;
        cmd.Parameters.Add("@FolioCliente", SqlDbType.NVarChar, 100).Value =
            (object?)documento.FolioCliente ?? DBNull.Value;
        cmd.Parameters.Add("@ClienteID", SqlDbType.Int).Value = documento.ClienteID!.Value;
        cmd.Parameters.Add("@ClienteNombre", SqlDbType.NVarChar, 200).Value =
            (object?)documento.Cliente ?? DBNull.Value;
        cmd.Parameters.Add("@FechaRecepcion", SqlDbType.Date).Value =
            documento.ReleasePreparado.FechaRecepcion.Date;
        cmd.Parameters.Add("@VersionRelease", SqlDbType.NVarChar, 100).Value =
            (object?)documento.Version ?? DBNull.Value;
        cmd.Parameters.Add("@ArchivoOrigenNombre", SqlDbType.NVarChar, 255).Value =
            documento.Archivo;
        cmd.Parameters.Add("@PlantillaImportacion", SqlDbType.NVarChar, 100).Value =
            documento.Plantilla;
        cmd.Parameters.Add("@Sha256", SqlDbType.NVarChar, 128).Value =
            documento.Sha256 ?? string.Empty;
        cmd.Parameters.Add("@ArchivoGuardado", SqlDbType.NVarChar, 500).Value =
            (object?)archivoGuardado ?? DBNull.Value;
        cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;

        var affected = await cmd.ExecuteNonQueryAsync();
        if (affected != 1)
            throw new InvalidOperationException($"No fue posible actualizar el encabezado del Release #{releaseId}.");
    }

    private sealed class ReleaseRenglonActualizacionInfo
    {
        public int ReleaseRenglonID { get; init; }
        public int Renglon { get; init; }
    }

    private async Task<Dictionary<int, ReleaseRenglonActualizacionInfo>>
        PrepararRenglonesActualizacionAsync(
            int releaseId,
            ReleaseValidacionDocumentoVm documento,
            int usuarioId,
            SqlConnection cn,
            SqlTransaction tx)
    {
        var result = new Dictionary<int, ReleaseRenglonActualizacionInfo>();

        var maxRenglon = 0;
        const string sqlMax = @"
SELECT ISNULL(MAX(Renglon), 0)
FROM dbo.Planeacion_ReleaseRenglones
WHERE ReleaseID = @ReleaseID
  AND Activo = 1;";

        await using (var cmd = new SqlCommand(sqlMax, cn, tx))
        {
            cmd.Parameters.Add("@ReleaseID", SqlDbType.Int).Value = releaseId;
            maxRenglon = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        foreach (var row in documento.ReleasePreparado.Renglones
            .Where(x => x.ParteID.HasValue)
            .GroupBy(x => x.ParteID!.Value)
            .Select(x => x.First()))
        {
            const string sqlExisting = @"
SELECT TOP (1)
    ReleaseRenglonID,
    Renglon
FROM dbo.Planeacion_ReleaseRenglones
WHERE ReleaseID = @ReleaseID
  AND ParteID = @ParteID
  AND Activo = 1
ORDER BY Renglon, ReleaseRenglonID;";

            int? releaseRenglonId = null;
            int? renglon = null;

            await using (var cmd = new SqlCommand(sqlExisting, cn, tx))
            {
                cmd.Parameters.Add("@ReleaseID", SqlDbType.Int).Value = releaseId;
                cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value = row.ParteID!.Value;

                await using var rd = await cmd.ExecuteReaderAsync();
                if (await rd.ReadAsync())
                {
                    releaseRenglonId = Convert.ToInt32(rd["ReleaseRenglonID"]);
                    renglon = Convert.ToInt32(rd["Renglon"]);
                }
            }

            await CompletarRenglonDesdeParteAsync(row, cn, tx);

            if (!releaseRenglonId.HasValue)
            {
                maxRenglon++;
                row.Renglon = maxRenglon;

                releaseRenglonId = await InsertarReleaseRenglonAsync(
                    releaseId,
                    row,
                    usuarioId,
                    cn,
                    tx);

                renglon = row.Renglon;
            }
            else
            {
                row.Renglon = renglon!.Value;

                const string sqlUpdate = @"
UPDATE dbo.Planeacion_ReleaseRenglones
SET
    NumeroParte = @NumeroParte,
    ReferenciaSAP = @ReferenciaSAP,
    DesignacionDescripcionSAP = @Descripcion,
    UnidadMedidaCliente = @Unidad,
    ContratoCliente = @Contrato,
    Observaciones = @Observaciones
WHERE ReleaseRenglonID = @ReleaseRenglonID
  AND ReleaseID = @ReleaseID
  AND Activo = 1;";

                await using var update = new SqlCommand(sqlUpdate, cn, tx);
                update.Parameters.Add("@NumeroParte", SqlDbType.NVarChar, 120).Value =
                    (object?)row.NumeroParte ?? DBNull.Value;
                update.Parameters.Add("@ReferenciaSAP", SqlDbType.NVarChar, 150).Value =
                    (object?)row.ReferenciaSAP ?? DBNull.Value;
                update.Parameters.Add("@Descripcion", SqlDbType.NVarChar, 300).Value =
                    (object?)row.DesignacionDescripcionSAP ?? DBNull.Value;
                update.Parameters.Add("@Unidad", SqlDbType.NVarChar, 50).Value =
                    (object?)row.UnidadMedidaCliente ?? DBNull.Value;
                update.Parameters.Add("@Contrato", SqlDbType.NVarChar, 150).Value =
                    (object?)row.ContratoCliente ?? DBNull.Value;
                update.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 500).Value =
                    (object?)row.Observaciones ?? DBNull.Value;
                update.Parameters.Add("@ReleaseRenglonID", SqlDbType.Int).Value =
                    releaseRenglonId.Value;
                update.Parameters.Add("@ReleaseID", SqlDbType.Int).Value = releaseId;
                await update.ExecuteNonQueryAsync();
            }

            result[row.ParteID!.Value] = new ReleaseRenglonActualizacionInfo
            {
                ReleaseRenglonID = releaseRenglonId!.Value,
                Renglon = renglon!.Value
            };
        }

        return result;
    }

    private static async Task ActualizarCantidadDetalleReleaseAsync(
        int releaseDetalleId,
        ReleaseActualizacionEntrada entrada,
        ReleaseRenglonActualizacionInfo renglon,
        int usuarioId,
        SqlConnection cn,
        SqlTransaction tx)
    {
        const string sql = @"
UPDATE dbo.Planeacion_ReleaseDetalle
SET
    ReleaseRenglonID = @ReleaseRenglonID,
    Renglon = @Renglon,
    ParteID = @ParteID,
    NumeroParte = @NumeroParte,
    ReferenciaSAP = @ReferenciaSAP,
    DesignacionDescripcionSAP = @Descripcion,
    FechaRequerida = @FechaRequerida,
    CantidadRequerida = @CantidadRequerida,
    UsuarioModificacionID = @UsuarioID,
    FechaModificacion = GETDATE()
WHERE ReleaseDetalleID = @ReleaseDetalleID
  AND Activo = 1;";

        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value = releaseDetalleId;
        cmd.Parameters.Add("@ReleaseRenglonID", SqlDbType.Int).Value = renglon.ReleaseRenglonID;
        cmd.Parameters.Add("@Renglon", SqlDbType.Int).Value = renglon.Renglon;
        cmd.Parameters.Add("@ParteID", SqlDbType.Int).Value = entrada.ParteID;
        cmd.Parameters.Add("@NumeroParte", SqlDbType.NVarChar, 120).Value =
            (object?)entrada.Renglon.NumeroParte ?? DBNull.Value;
        cmd.Parameters.Add("@ReferenciaSAP", SqlDbType.NVarChar, 150).Value =
            (object?)entrada.Renglon.ReferenciaSAP ?? DBNull.Value;
        cmd.Parameters.Add("@Descripcion", SqlDbType.NVarChar, 300).Value =
            (object?)entrada.Renglon.DesignacionDescripcionSAP ?? DBNull.Value;
        cmd.Parameters.Add("@FechaRequerida", SqlDbType.Date).Value = entrada.FechaRequerida.Date;
        cmd.Parameters.Add("@CantidadRequerida", SqlDbType.Int).Value = entrada.CantidadRequerida;
        cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;

        var affected = await cmd.ExecuteNonQueryAsync();
        if (affected != 1)
            throw new InvalidOperationException(
                $"No se pudo actualizar ReleaseDetalleID {releaseDetalleId}.");
    }

    private sealed class ProgramacionAjusteResultado
    {
        public bool Ajustada { get; init; }
        public string? Advertencia { get; init; }
    }

    private static async Task<ProgramacionAjusteResultado>
        AjustarProgramacionPorDeltaAsync(
            int releaseDetalleId,
            int delta,
            SqlConnection cn,
            SqlTransaction tx)
    {
        const string sql = @"
SELECT
    pp.ProgramaProduccionID,
    ISNULL(pp.CantidadProgramada, 0) AS CantidadProgramada,
    ISNULL(pp.CantidadProducida, 0) AS CantidadProducida
FROM dbo.Planeacion_ProgramaProduccion pp
WHERE pp.ReleaseDetalleID = @ReleaseDetalleID
  AND pp.Activo = 1
  AND ISNULL(pp.EstatusID, 1) NOT IN (9, 99)
ORDER BY pp.ProgramaProduccionID;";

        var programs = new List<(int Id, int Programada, int Producida)>();

        await using (var cmd = new SqlCommand(sql, cn, tx))
        {
            cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value = releaseDetalleId;

            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                programs.Add((
                    Convert.ToInt32(rd["ProgramaProduccionID"]),
                    Convert.ToInt32(rd["CantidadProgramada"]),
                    Convert.ToInt32(rd["CantidadProducida"])));
            }
        }

        if (programs.Count == 0 || delta == 0)
            return new ProgramacionAjusteResultado();

        if (programs.Any(x => x.Producida > 0))
        {
            return new ProgramacionAjusteResultado
            {
                Advertencia =
                    $"ReleaseDetalleID {releaseDetalleId}: la demanda cambio, pero la programacion " +
                    "no se modifico porque ya existe cantidad producida."
            };
        }

        if (programs.Count != 1)
        {
            return new ProgramacionAjusteResultado
            {
                Advertencia =
                    $"ReleaseDetalleID {releaseDetalleId}: hay {programs.Count} programas activos. " +
                    "La demanda se actualizo, pero la programacion debe revisarse manualmente."
            };
        }

        var program = programs[0];
        var nuevaCantidad = Math.Max(0, program.Programada + delta);

        const string updateSql = @"
UPDATE dbo.Planeacion_ProgramaProduccion
SET CantidadProgramada = @CantidadProgramada
WHERE ProgramaProduccionID = @ProgramaProduccionID
  AND Activo = 1;";

        await using var update = new SqlCommand(updateSql, cn, tx);
        update.Parameters.Add("@CantidadProgramada", SqlDbType.Int).Value = nuevaCantidad;
        update.Parameters.Add("@ProgramaProduccionID", SqlDbType.Int).Value = program.Id;
        await update.ExecuteNonQueryAsync();

        return new ProgramacionAjusteResultado
        {
            Ajustada = true
        };
    }

    private static async Task<int> ObtenerSiguienteSecuenciaEntregaAsync(
        int releaseId,
        int releaseRenglonId,
        SqlConnection cn,
        SqlTransaction tx)
    {
        const string sql = @"
SELECT ISNULL(MAX(SecuenciaEntrega), 0) + 1
FROM dbo.Planeacion_ReleaseDetalle
WHERE ReleaseID = @ReleaseID
  AND ReleaseRenglonID = @ReleaseRenglonID;";

        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@ReleaseID", SqlDbType.Int).Value = releaseId;
        cmd.Parameters.Add("@ReleaseRenglonID", SqlDbType.Int).Value = releaseRenglonId;

        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private static async Task RetirarDetalleFuturoYProgramacionAsync(
        int releaseDetalleId,
        int usuarioId,
        SqlConnection cn,
        SqlTransaction tx)
    {
        const string sql = @"
UPDATE dbo.Planeacion_ProgramaProduccion
SET
    Activo = 0,
    EstatusID = 99
WHERE ReleaseDetalleID = @ReleaseDetalleID
  AND Activo = 1
  AND ISNULL(CantidadProducida, 0) = 0;

UPDATE dbo.Planeacion_ReleaseDetalle
SET
    Activo = 0,
    EstatusID = 99,
    UsuarioModificacionID = @UsuarioID,
    FechaModificacion = GETDATE()
WHERE ReleaseDetalleID = @ReleaseDetalleID
  AND Activo = 1;";

        await using var cmd = new SqlCommand(sql, cn, tx);
        cmd.Parameters.Add("@ReleaseDetalleID", SqlDbType.Int).Value = releaseDetalleId;
        cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioId;
        await cmd.ExecuteNonQueryAsync();
    }
}
