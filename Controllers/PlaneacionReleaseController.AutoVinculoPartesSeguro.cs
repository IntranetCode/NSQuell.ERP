using Microsoft.Data.SqlClient;
using System.Data;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ERP.NSQuell.Controllers;

public partial class PlaneacionReleaseController
{
    // NSQ_GOLDE_AUTO_MATCH_SAFE_V1
    private sealed class CandidatoGoldeSeguro
    {
        public int ParteID { get; init; }
        public string NumeroParte { get; init; } = string.Empty;
        public string ReferenciaSAP { get; init; } = string.Empty;
        public string Designacion { get; init; } = string.Empty;
        public double Puntaje { get; set; }
        public int DistanciaCodigo { get; set; }
        public double SimilitudCodigo { get; set; }
        public double SimilitudDesignacion { get; set; }
    }

    private async Task<ParteImportacionMatch?> BuscarParteGoldeSeguraAsync(
        string referencia,
        string? designacionRecibida,
        int clienteId,
        SqlConnection cn,
        SqlTransaction tx)
    {
        if (string.IsNullOrWhiteSpace(referencia) || clienteId <= 0)
            return null;

        // 1) Se conserva primero la lógica exacta ya existente.
        //    Esto cubre número exacto y revisión _NN sobre el mismo número base.
        var exacta = await BuscarParteGoldeAsync(
            referencia,
            clienteId,
            cn,
            tx);

        if (exacta?.Activa == true)
            return exacta;

        var codigoRecibidoBase = AutoGoldeQuitarRevision(referencia);
        var codigoRecibido = AutoGoldeNormalizarCodigo(codigoRecibidoBase);
        var designacionNormalizada = AutoGoldeNormalizarTexto(designacionRecibida);
        var ladoRecibido = AutoGoldeObtenerLado(designacionRecibida);

        // Sin designación no hacemos fuzzy matching. Un número parecido por sí solo
        // no es suficiente para cambiar de ParteID.
        if (codigoRecibido.Length < 5 || designacionNormalizada.Length < 3)
            return null;

        const string sql = @"
SELECT
    ParteID,
    NumeroParte,
    ISNULL(ReferenciaSAP, N'') AS ReferenciaSAP,
    COALESCE(NULLIF(Designacion, N''), NULLIF(Descripcion, N''), N'') AS Designacion
FROM dbo.ERP_Partes
WHERE ClienteID = @ClienteID
  AND Activo = 1
ORDER BY ParteID;";

        var candidatos = new List<CandidatoGoldeSeguro>();

        await using (var cmd = new SqlCommand(sql, cn, tx))
        {
            cmd.Parameters.Add("@ClienteID", SqlDbType.Int).Value = clienteId;

            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                var parteId = Convert.ToInt32(rd["ParteID"]);
                var numeroParte = rd["NumeroParte"] as string ?? string.Empty;
                var referenciaSap = rd["ReferenciaSAP"] as string ?? string.Empty;
                var designacion = rd["Designacion"] as string ?? string.Empty;

                var ladoCandidato = AutoGoldeObtenerLado(designacion);

                // LH y RH son piezas distintas. Nunca cruzamos lados.
                if (!string.IsNullOrWhiteSpace(ladoRecibido) &&
                    !string.IsNullOrWhiteSpace(ladoCandidato) &&
                    !string.Equals(ladoRecibido, ladoCandidato, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var codigos = new[] { numeroParte, referenciaSap }
                    .Where(x => !string.IsNullOrWhiteSpace(x) &&
                                !string.Equals(x.Trim(), "N/A", StringComparison.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (codigos.Count == 0)
                    continue;

                var mejorDistancia = int.MaxValue;
                var mejorSimilitudCodigo = 0d;
                var conflictoDigitoFinal = false;

                foreach (var codigo in codigos)
                {
                    var baseCandidato = AutoGoldeQuitarRevision(codigo);
                    var normalizado = AutoGoldeNormalizarCodigo(baseCandidato);

                    if (normalizado.Length < 5)
                        continue;

                    var distancia = AutoGoldeLevenshtein(codigoRecibido, normalizado);
                    var maxLen = Math.Max(codigoRecibido.Length, normalizado.Length);
                    var similitud = maxLen == 0
                        ? 0d
                        : 1d - ((double)distancia / maxLen);

                    if (distancia < mejorDistancia ||
                        (distancia == mejorDistancia && similitud > mejorSimilitudCodigo))
                    {
                        mejorDistancia = distancia;
                        mejorSimilitudCodigo = similitud;
                        conflictoDigitoFinal = AutoGoldeSoloCambiaUltimoCaracter(
                            codigoRecibido,
                            normalizado);
                    }
                }

                if (mejorDistancia == int.MaxValue)
                    continue;

                // Un cambio en el último dígito significativo NO se considera revisión.
                // Es precisamente el caso que puede representar otra pieza.
                if (conflictoDigitoFinal)
                    continue;

                var similitudDesignacion = AutoGoldeSimilitudDesignacion(
                    designacionNormalizada,
                    AutoGoldeNormalizarTexto(designacion));

                // Fuzzy solo si el código está realmente cerca y la designación respalda.
                var codigoCercano =
                    mejorDistancia <= 1 ||
                    (mejorDistancia <= 2 && mejorSimilitudCodigo >= 0.88d);

                if (!codigoCercano || similitudDesignacion < 0.90d)
                    continue;

                var puntaje =
                    (mejorSimilitudCodigo * 65d) +
                    (similitudDesignacion * 35d);

                if (mejorDistancia == 1)
                    puntaje += 2d;

                if (similitudDesignacion >= 0.999d)
                    puntaje += 3d;

                if (!string.IsNullOrWhiteSpace(ladoRecibido) &&
                    string.Equals(ladoRecibido, ladoCandidato, StringComparison.OrdinalIgnoreCase))
                {
                    puntaje += 2d;
                }

                candidatos.Add(new CandidatoGoldeSeguro
                {
                    ParteID = parteId,
                    NumeroParte = numeroParte,
                    ReferenciaSAP = referenciaSap,
                    Designacion = designacion,
                    Puntaje = Math.Min(100d, puntaje),
                    DistanciaCodigo = mejorDistancia,
                    SimilitudCodigo = mejorSimilitudCodigo,
                    SimilitudDesignacion = similitudDesignacion
                });
            }
        }

        var ordenados = candidatos
            .OrderByDescending(x => x.Puntaje)
            .ThenBy(x => x.DistanciaCodigo)
            .ThenBy(x => x.ParteID)
            .ToList();

        if (ordenados.Count == 0)
            return null;

        var mejor = ordenados[0];

        // Umbral alto: número y designación tienen que coincidir de forma fuerte.
        if (mejor.Puntaje < 93d)
            return null;

        // Si hay dos candidatos casi igual de buenos, no adivinamos.
        if (ordenados.Count > 1 &&
            (mejor.Puntaje - ordenados[1].Puntaje) < 7d)
        {
            return null;
        }

        return new ParteImportacionMatch
        {
            ParteID = mejor.ParteID,
            Activa = true,
            NumeroParte = mejor.NumeroParte,
            ReferenciaSAP = string.IsNullOrWhiteSpace(mejor.ReferenciaSAP)
                ? mejor.NumeroParte
                : mejor.ReferenciaSAP
        };
    }

    private static string AutoGoldeQuitarRevision(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var text = value.Trim();

        // Sólo tratamos como revisión un sufijo explícitamente separado:
        // _01, -01 o ?01. Un último dígito del número base sigue siendo significativo.
        return Regex.Replace(
            text,
            @"[_\-\?]\d{1,3}$",
            string.Empty,
            RegexOptions.CultureInvariant);
    }

    private static string AutoGoldeNormalizarCodigo(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var sb = new StringBuilder(value.Length);

        foreach (var ch in value.ToUpperInvariant())
        {
            if (char.IsLetterOrDigit(ch))
                sb.Append(ch);
        }

        return sb.ToString();
    }

    private static string AutoGoldeNormalizarTexto(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value
            .Normalize(NormalizationForm.FormD)
            .ToUpperInvariant();

        var sb = new StringBuilder(normalized.Length);
        var previousSpace = false;

        foreach (var ch in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);

            if (category == UnicodeCategory.NonSpacingMark)
                continue;

            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
                previousSpace = false;
            }
            else if (!previousSpace)
            {
                sb.Append(' ');
                previousSpace = true;
            }
        }

        return Regex.Replace(sb.ToString().Trim(), @"\s+", " ");
    }

    private static string? AutoGoldeObtenerLado(string? designacion)
    {
        var normal = AutoGoldeNormalizarTexto(designacion);

        if (Regex.IsMatch(normal, @"(^|\s)LH(\s|$)"))
            return "LH";

        if (Regex.IsMatch(normal, @"(^|\s)RH(\s|$)"))
            return "RH";

        return null;
    }

    private static double AutoGoldeSimilitudDesignacion(
        string izquierdaNormalizada,
        string derechaNormalizada)
    {
        if (string.IsNullOrWhiteSpace(izquierdaNormalizada) ||
            string.IsNullOrWhiteSpace(derechaNormalizada))
        {
            return 0d;
        }

        if (string.Equals(
            izquierdaNormalizada,
            derechaNormalizada,
            StringComparison.OrdinalIgnoreCase))
        {
            return 1d;
        }

        var tokensA = izquierdaNormalizada
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var tokensB = derechaNormalizada
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (tokensA.Count == 0 || tokensB.Count == 0)
            return 0d;

        var interseccion = tokensA.Intersect(tokensB, StringComparer.OrdinalIgnoreCase).Count();
        var union = tokensA.Union(tokensB, StringComparer.OrdinalIgnoreCase).Count();
        var jaccard = union == 0 ? 0d : (double)interseccion / union;

        var menor = tokensA.Count <= tokensB.Count ? tokensA : tokensB;
        var mayor = ReferenceEquals(menor, tokensA) ? tokensB : tokensA;

        var contencion =
            menor.Count >= 2 && menor.All(x => mayor.Contains(x))
                ? 0.96d
                : 0d;

        var compactA = izquierdaNormalizada.Replace(" ", string.Empty);
        var compactB = derechaNormalizada.Replace(" ", string.Empty);
        var distancia = AutoGoldeLevenshtein(compactA, compactB);
        var maxLen = Math.Max(compactA.Length, compactB.Length);
        var caracteres = maxLen == 0
            ? 0d
            : 1d - ((double)distancia / maxLen);

        return Math.Max(contencion, Math.Max(jaccard, caracteres));
    }

    private static bool AutoGoldeSoloCambiaUltimoCaracter(
        string a,
        string b)
    {
        if (string.IsNullOrEmpty(a) ||
            string.IsNullOrEmpty(b) ||
            a.Length != b.Length ||
            a.Length < 2)
        {
            return false;
        }

        if (a[^1] == b[^1])
            return false;

        return string.Equals(
            a[..^1],
            b[..^1],
            StringComparison.OrdinalIgnoreCase);
    }

    private static int AutoGoldeLevenshtein(string a, string b)
    {
        a ??= string.Empty;
        b ??= string.Empty;

        if (a.Length == 0)
            return b.Length;

        if (b.Length == 0)
            return a.Length;

        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];

        for (var j = 0; j <= b.Length; j++)
            previous[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;

            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;

                current[j] = Math.Min(
                    Math.Min(
                        current[j - 1] + 1,
                        previous[j] + 1),
                    previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}
