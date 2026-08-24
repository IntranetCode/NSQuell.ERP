using ERP.NSQuell.Models;

namespace ERP.NSQuell.Controllers;

// NSQ_CALENDARIO_AJUSTES_V19
public sealed partial class PlaneacionCalendarioMaquinasController
{
    private static List<PlaneacionCalendarioMaquinaVm> Excluir1200TCalendario(
        List<PlaneacionCalendarioMaquinaVm> maquinas)
    {
        return maquinas
            .Where(x => !Es1200TCalendario(x.Codigo, x.Nombre))
            .ToList();
    }

    private static bool Es1200TCalendario(string? codigo, string? nombre)
    {
        static string Normalizar(string? value) =>
            (value ?? string.Empty)
                .Replace("-", string.Empty, StringComparison.Ordinal)
                .Replace(" ", string.Empty, StringComparison.Ordinal)
                .Trim()
                .ToUpperInvariant();

        return Normalizar(codigo) == "1200T" ||
               Normalizar(nombre).Contains("1200T", StringComparison.Ordinal);
    }
}
