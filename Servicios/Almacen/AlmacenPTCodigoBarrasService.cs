using System.Globalization;

namespace ERP.NSQuell.Servicios.Almacen;

public sealed record AlmacenPTCodigoBarrasParseado(
    string CodigoOriginal,
    string NumeroOF,
    string NumeroParte,
    string Designacion,
    int Cantidad,
    string Lote);

public static class AlmacenPTCodigoBarrasService
{
    public static bool TryParse(
        string? codigo,
        out AlmacenPTCodigoBarrasParseado? resultado,
        out string error)
    {
        resultado = null;
        error = string.Empty;

        var valor = codigo?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(valor))
        {
            error = "El código está vacío.";
            return false;
        }

        if (valor.Length > 120)
        {
            error = "El código excede los 120 caracteres permitidos.";
            return false;
        }

        var primerGuion = valor.IndexOf('-');
        var segundoGuion =
            primerGuion >= 0
                ? valor.IndexOf('-', primerGuion + 1)
                : -1;

        var ultimoGuion = valor.LastIndexOf('-');
        var penultimoGuion =
            ultimoGuion > 0
                ? valor.LastIndexOf('-', ultimoGuion - 1)
                : -1;

        if (primerGuion <= 0
            || segundoGuion <= primerGuion + 1
            || penultimoGuion <= segundoGuion + 1
            || ultimoGuion <= penultimoGuion + 1
            || ultimoGuion >= valor.Length - 1)
        {
            error =
                "Formato inválido. Se esperaba OF-NO.PARTE-DESIGNACIÓN-PIEZAS-NO.CAJA.";
            return false;
        }

        var numeroOF =
            valor[..primerGuion].Trim();

        var numeroParte =
            valor[(primerGuion + 1)..segundoGuion].Trim();

        var designacion =
            valor[(segundoGuion + 1)..penultimoGuion].Trim();

        var cantidadTexto =
            valor[(penultimoGuion + 1)..ultimoGuion].Trim();

        var lote =
            valor[(ultimoGuion + 1)..].Trim();

        if (string.IsNullOrWhiteSpace(numeroOF))
        {
            error = "La OF está vacía.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(numeroParte))
        {
            error = "El número de parte está vacío.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(designacion))
        {
            error = "La designación está vacía.";
            return false;
        }

        if (!int.TryParse(
                cantidadTexto,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var cantidad)
            || cantidad <= 0)
        {
            error = "La cantidad de piezas no es un entero positivo.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(lote))
        {
            error = "El número de caja está vacío.";
            return false;
        }

        resultado = new AlmacenPTCodigoBarrasParseado(
            CodigoOriginal: valor,
            NumeroOF: numeroOF,
            NumeroParte: numeroParte,
            Designacion: designacion,
            Cantidad: cantidad,
            Lote: lote);

        return true;
    }
}
