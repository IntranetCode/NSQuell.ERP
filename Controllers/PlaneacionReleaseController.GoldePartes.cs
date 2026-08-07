using Microsoft.Data.SqlClient;
using System.Text.RegularExpressions;

namespace ERP.NSQuell.Controllers;

/// <summary>
/// Reglas exclusivas para vincular referencias del formato semanal GOLDE.
///
/// El Excel entrega referencias con revision final, por ejemplo:
/// 579.714490_01, 754.734023_00 y 803.744431_02.
/// El catalogo canonico puede conservar la parte base sin esa revision:
/// 579.714490, 754.734023 y 803 744 431.
///
/// Primero se intenta la coincidencia exacta existente. Solo cuando no hay
/// una parte activa exacta se elimina una revision terminal de dos digitos y
/// se reutiliza el buscador normalizado actual. No modifica HUF, VERITAS,
/// NORMA ni AIR THERMAL.
/// </summary>
public partial class PlaneacionReleaseController
{
    private static bool EsPlantillaGolde(string? templateCode)
    {
        return string.Equals(
                   templateCode,
                   "GOLDEN_WEEKLY_RELEASE",
                   StringComparison.OrdinalIgnoreCase) ||
               string.Equals(
                   templateCode,
                   "GOLDE_WEEKLY_RELEASE",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<ParteImportacionMatch?>
        BuscarParteGoldeIncluyendoRevisionesAsync(
            string referencia,
            int clienteId,
            SqlConnection cn,
            SqlTransaction tx)
    {
        if (string.IsNullOrWhiteSpace(referencia))
            return null;

        var referenciaOriginal = referencia.Trim();

        // Conserva prioridad absoluta para una referencia exacta o alias
        // maestro ya configurado.
        var coincidenciaExacta =
            await BuscarParteImportacionIncluyendoInactivasAsync(
                referenciaOriginal,
                clienteId,
                cn,
                tx);

        if (coincidenciaExacta?.Activa == true)
            return coincidenciaExacta;

        // GOLDE agrega una revision de dos digitos al final del No. SAP.
        // Ejemplos: _00, _01, _02. Se toleran tambien -01 y ?01 por datos
        // historicos ya observados en el catalogo.
        var referenciaBase = Regex.Replace(
            referenciaOriginal,
            @"(?:_|-|\?)\d{2}$",
            string.Empty,
            RegexOptions.CultureInvariant);

        if (string.Equals(
                referenciaBase,
                referenciaOriginal,
                StringComparison.OrdinalIgnoreCase))
        {
            return coincidenciaExacta;
        }

        var coincidenciaBase =
            await BuscarParteImportacionIncluyendoInactivasAsync(
                referenciaBase,
                clienteId,
                cn,
                tx);

        // La vinculacion automatica solo se permite contra una parte activa.
        // Una coincidencia inactiva permanece pendiente para revision manual.
        if (coincidenciaBase?.Activa == true)
            return coincidenciaBase;

        return coincidenciaExacta ?? coincidenciaBase;
    }
}