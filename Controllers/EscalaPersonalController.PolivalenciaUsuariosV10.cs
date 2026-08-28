using Microsoft.Data.SqlClient;
using System.Data;
using System.Globalization;
using System.Text;

namespace ERP.NSQuell.Controllers;

public partial class EscalaPersonalController
{
    // NSQ_POLIVALENCIA_USUARIOS_AUTO_V10
    // Solo administra OPERADOR y AUXILIAR DE PRODUCCION provenientes de Polivalencia.
    // Nunca da de baja cuentas ajenas: requiere GestionadoPorMatrizPolivalencia=1.

    private sealed class PoliUsuarioSyncV10
    {
        public int CuentasCreadas { get; set; }
        public int CuentasReactivadas { get; set; }
        public int CuentasNormalizadas { get; set; }
        public int CuentasDesactivadas { get; set; }
        public int PersonasDesactivadas { get; set; }

        // NSQ_POLIVALENCIA_V10_1_AVISO_DETALLE_USUARIOS
        public List<string> CreadosDetalle { get; } = new();
        public List<string> ReactivadosDetalle { get; } = new();
        public List<string> DesactivadosDetalle { get; } = new();
    }

    private sealed class PoliPersonaCuentaV10
    {
        public int PersonaID { get; set; }
        public string NumeroControl { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public string ApellidoPaterno { get; set; } = string.Empty;
        public string ApellidoMaterno { get; set; } = string.Empty;
        public int? UsuarioID { get; set; }
        public string UsernameActual { get; set; } = string.Empty;
        public int? RolIDActual { get; set; }
        public int? DepartamentoIDActual { get; set; }
        public bool UsuarioActivo { get; set; }
        public bool GestionadoPorMatriz { get; set; }
    }

    // NSQ_POLIVALENCIA_V10_1_AVISO_DETALLE_USUARIOS
    private static string DetalleCuentaPoliV101(
        string nombre,
        string apellidoPaterno,
        string apellidoMaterno,
        string username)
    {
        var nombreCompleto = string.Join(" ",
            new[] { nombre, apellidoPaterno, apellidoMaterno }
                .Where(x => !string.IsNullOrWhiteSpace(x)))
            .Trim();

        return string.IsNullOrWhiteSpace(nombreCompleto)
            ? username
            : $"{nombreCompleto.ToUpperInvariant()} — {username}";
    }

    private static string ConstruirDetalleUsuariosPoliV101(PoliUsuarioSyncV10 sync)
    {
        var lineas = new List<string>();

        if (sync.CreadosDetalle.Count > 0)
        {
            lineas.Add("CREADOS:");
            lineas.AddRange(sync.CreadosDetalle.Select(x => "• " + x));
        }

        if (sync.ReactivadosDetalle.Count > 0)
        {
            if (lineas.Count > 0) lineas.Add(string.Empty);
            lineas.Add("REACTIVADOS:");
            lineas.AddRange(sync.ReactivadosDetalle.Select(x => "• " + x));
        }

        if (sync.DesactivadosDetalle.Count > 0)
        {
            if (lineas.Count > 0) lineas.Add(string.Empty);
            lineas.Add("DADOS DE BAJA:");
            lineas.AddRange(sync.DesactivadosDetalle.Select(x => "• " + x));
        }

        if (lineas.Count == 0)
            lineas.Add("Sin altas, reactivaciones ni bajas de cuentas ERP.");

        return string.Join(Environment.NewLine, lineas);
    }

    private static string NormalizarTextoCuentaPoliV10(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return string.Empty;

        var normalizado = valor.Trim().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalizado.Length);

        foreach (var ch in normalizado)
        {
            var categoria = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (categoria == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(ch)) sb.Append(char.ToUpperInvariant(ch));
        }

        return sb.ToString();
    }

    private static bool EsPuestoCuentaPoliV10(string? puesto)
    {
        var p = NormalizarTextoCuentaPoliV10(puesto);
        return p.Contains("OPERADOR", StringComparison.OrdinalIgnoreCase)
            || p.Contains("AUXILIARDEPRODU", StringComparison.OrdinalIgnoreCase);
    }

    private static string ConstruirUsernamePoliV10(
        string nombre,
        string apellidoPaterno,
        string apellidoMaterno)
    {
        var n = NormalizarTextoCuentaPoliV10(nombre);
        var ap = NormalizarTextoCuentaPoliV10(apellidoPaterno);
        var am = NormalizarTextoCuentaPoliV10(apellidoMaterno);

        if (string.IsNullOrWhiteSpace(n))
            throw new InvalidOperationException("No se puede generar username: Persona.Nombre esta vacio.");

        if (string.IsNullOrWhiteSpace(ap))
            throw new InvalidOperationException("No se puede generar username: Persona.ApellidoPaterno esta vacio.");

        var username = n[..1] + ap + (string.IsNullOrWhiteSpace(am) ? string.Empty : am[..1]);

        if (username.Length > 100)
            username = username[..100];

        return username;
    }

    private static string ConstruirPasswordTemporalPoliV10(string numeroControl)
    {
        var control = NormalizarTextoCuentaPoliV10(numeroControl);

        if (string.IsNullOrWhiteSpace(control))
            throw new InvalidOperationException("No se puede generar password temporal sin numero de control.");

        // Ejemplo: control 1514 -> Quell1514!
        return $"Quell{control}!";
    }

    private static async Task<PoliUsuarioSyncV10> SincronizarUsuariosPolivalenciaV10Async(
        IReadOnlyCollection<PoliPersonaV7> personasActuales,
        SqlConnection cn,
        SqlTransaction tx)
    {
        if (personasActuales == null || personasActuales.Count == 0)
            throw new InvalidOperationException("No hay operadores/auxiliares de Produccion para sincronizar.");

        if (personasActuales.Any(x => !EsPuestoCuentaPoliV10(x.Puesto)))
            throw new InvalidOperationException(
                "La sincronizacion de cuentas V10 solo admite OPERADOR y AUXILIAR DE PRODUCCION.");

        const string estructuraSql = @"
SELECT
    CONVERT(bit,CASE WHEN COL_LENGTH(N'dbo.Usuarios',N'GestionadoPorMatrizPolivalencia') IS NULL THEN 0 ELSE 1 END) AS TieneMarcador,
    (SELECT COUNT(1) FROM dbo.Roles WHERE RolID=4 AND Activo=1) AS RolOperadorActivo,
    (SELECT TOP(1) DepartamentoID
       FROM dbo.Departamentos
      WHERE Activo=1
        AND UPPER(LTRIM(RTRIM(NombreDepartamento))) COLLATE Modern_Spanish_CI_AI=N'PRODUCCION'
      ORDER BY DepartamentoID) AS DepartamentoProduccionID;";

        int departamentoProduccionId;

        await using (var cmd = new SqlCommand(estructuraSql, cn, tx))
        await using (var rd = await cmd.ExecuteReaderAsync())
        {
            await rd.ReadAsync();

            if (!Convert.ToBoolean(rd["TieneMarcador"]))
                throw new InvalidOperationException(
                    "Falta dbo.Usuarios.GestionadoPorMatrizPolivalencia. Ejecuta primero el SQL 46_POLIVALENCIA_USUARIOS_AUTO_V10.sql.");

            if (Convert.ToInt32(rd["RolOperadorActivo"]) != 1)
                throw new InvalidOperationException("RolID 4 (Operador / Capturista) no existe o esta inactivo.");

            if (rd["DepartamentoProduccionID"] == DBNull.Value)
                throw new InvalidOperationException("No existe un departamento activo llamado Produccion.");

            departamentoProduccionId = Convert.ToInt32(rd["DepartamentoProduccionID"]);
        }

        var actualesPorId = personasActuales
            .GroupBy(x => x.PersonaID)
            .Select(g => g.OrderBy(x => x.Row).First())
            .ToDictionary(x => x.PersonaID);

        var ids = actualesPorId.Keys.OrderBy(x => x).ToList();
        var parametros = new List<string>();

        await using var personasCmd = new SqlCommand
        {
            Connection = cn,
            Transaction = tx
        };

        for (var i = 0; i < ids.Count; i++)
        {
            var p = $"@P{i}";
            parametros.Add(p);
            personasCmd.Parameters.Add(p, SqlDbType.Int).Value = ids[i];
        }

        personasCmd.CommandText = $@"
SELECT
    p.PersonaID,
    ISNULL(p.NumeroControl,N'') AS NumeroControl,
    ISNULL(p.Nombre,N'') AS Nombre,
    ISNULL(p.ApellidoPaterno,N'') AS ApellidoPaterno,
    ISNULL(p.ApellidoMaterno,N'') AS ApellidoMaterno,
    u.UsuarioID,
    ISNULL(u.Username,N'') AS Username,
    u.RolID,
    u.DepartamentoID,
    ISNULL(u.Activo,0) AS UsuarioActivo,
    ISNULL(u.GestionadoPorMatrizPolivalencia,0) AS GestionadoPorMatriz
FROM dbo.Persona p
LEFT JOIN dbo.Usuarios u ON u.PersonaID=p.PersonaID
WHERE p.PersonaID IN ({string.Join(",", parametros)})
ORDER BY p.PersonaID,u.UsuarioID;";

        var filas = new List<PoliPersonaCuentaV10>();

        await using (var rd = await personasCmd.ExecuteReaderAsync())
        {
            while (await rd.ReadAsync())
            {
                filas.Add(new PoliPersonaCuentaV10
                {
                    PersonaID = Convert.ToInt32(rd["PersonaID"]),
                    NumeroControl = rd["NumeroControl"]?.ToString()?.Trim() ?? string.Empty,
                    Nombre = rd["Nombre"]?.ToString()?.Trim() ?? string.Empty,
                    ApellidoPaterno = rd["ApellidoPaterno"]?.ToString()?.Trim() ?? string.Empty,
                    ApellidoMaterno = rd["ApellidoMaterno"]?.ToString()?.Trim() ?? string.Empty,
                    UsuarioID = rd["UsuarioID"] == DBNull.Value ? null : Convert.ToInt32(rd["UsuarioID"]),
                    UsernameActual = rd["Username"]?.ToString()?.Trim() ?? string.Empty,
                    RolIDActual = rd["RolID"] == DBNull.Value ? null : Convert.ToInt32(rd["RolID"]),
                    DepartamentoIDActual = rd["DepartamentoID"] == DBNull.Value ? null : Convert.ToInt32(rd["DepartamentoID"]),
                    UsuarioActivo = Convert.ToBoolean(rd["UsuarioActivo"]),
                    GestionadoPorMatriz = Convert.ToBoolean(rd["GestionadoPorMatriz"])
                });
            }
        }

        var personasFaltantes = ids.Where(id => !filas.Any(x => x.PersonaID == id)).ToList();

        if (personasFaltantes.Count > 0)
            throw new InvalidOperationException(
                $"Hay PersonaID de la matriz que ya no existen: {string.Join(", ", personasFaltantes)}.");

        var multiples = filas
            .Where(x => x.UsuarioID.HasValue)
            .GroupBy(x => x.PersonaID)
            .Where(g => g.Select(x => x.UsuarioID).Distinct().Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (multiples.Count > 0)
            throw new InvalidOperationException(
                $"Hay personas de Polivalencia con multiples cuentas ERP. PersonaID: {string.Join(", ", multiples)}.");

        var personasDb = filas
            .GroupBy(x => x.PersonaID)
            .ToDictionary(g => g.Key, g => g.First());

        var usernames = new Dictionary<int,string>();

        foreach (var id in ids)
        {
            var persona = personasDb[id];
            var controlMatriz = actualesPorId[id].Control?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(persona.NumeroControl)
                || !string.Equals(persona.NumeroControl.Trim(), controlMatriz, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"PersonaID {id}: NumeroControl de Persona ({persona.NumeroControl}) no coincide con la matriz ({controlMatriz}).");
            }

            usernames[id] = ConstruirUsernamePoliV10(
                persona.Nombre,
                persona.ApellidoPaterno,
                persona.ApellidoMaterno);
        }

        var duplicados = usernames
            .GroupBy(x => x.Value, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key} -> PersonaID {string.Join(",",g.Select(x => x.Key))}")
            .ToList();

        if (duplicados.Count > 0)
            throw new InvalidOperationException(
                "La nomenclatura genera usernames duplicados: " + string.Join(" | ", duplicados));

        foreach (var par in usernames)
        {
            const string conflictoSql = @"
SELECT TOP(1) UsuarioID,PersonaID
FROM dbo.Usuarios
WHERE UPPER(LTRIM(RTRIM(Username)))=UPPER(@Username)
  AND PersonaID<>@PersonaID
ORDER BY UsuarioID;";

            await using var conflictoCmd = new SqlCommand(conflictoSql, cn, tx);
            conflictoCmd.Parameters.Add("@Username", SqlDbType.NVarChar, 100).Value = par.Value;
            conflictoCmd.Parameters.Add("@PersonaID", SqlDbType.Int).Value = par.Key;

            await using var conflictoRd = await conflictoCmd.ExecuteReaderAsync();

            if (await conflictoRd.ReadAsync())
                throw new InvalidOperationException(
                    $"Username {par.Value} ya pertenece a PersonaID {conflictoRd["PersonaID"]} (UsuarioID {conflictoRd["UsuarioID"]}).");
        }

        var resultado = new PoliUsuarioSyncV10();

        // 1) Personal que SI esta en la matriz vigente.
        foreach (var id in ids)
        {
            var matriz = actualesPorId[id];
            var persona = personasDb[id];
            var username = usernames[id];

            const string personaOnSql = @"
UPDATE dbo.Persona
SET Puesto=@Puesto,
    EsColaboradorActivo=1,
    FechaBaja=NULL
WHERE PersonaID=@PersonaID;";

            await using (var cmd = new SqlCommand(personaOnSql, cn, tx))
            {
                cmd.Parameters.Add("@Puesto", SqlDbType.NVarChar, 100).Value =
                    matriz.Puesto[..Math.Min(100, matriz.Puesto.Length)];
                cmd.Parameters.Add("@PersonaID", SqlDbType.Int).Value = id;
                await cmd.ExecuteNonQueryAsync();
            }

            if (!persona.UsuarioID.HasValue)
            {
                var passwordTemporal = ConstruirPasswordTemporalPoliV10(matriz.Control);

                const string insertSql = @"
INSERT dbo.Usuarios
(
    PersonaID,Username,Contrasena,RolID,FechaCreacion,Activo,
    DebeCambiarPassword,FechaUltimoCambioPassword,DepartamentoID,
    GestionadoPorMatrizPolivalencia
)
VALUES
(
    @PersonaID,@Username,@Password,4,GETDATE(),1,
    1,NULL,@DepartamentoID,1
);";

                await using var cmd = new SqlCommand(insertSql, cn, tx);
                cmd.Parameters.Add("@PersonaID", SqlDbType.Int).Value = id;
                cmd.Parameters.Add("@Username", SqlDbType.NVarChar, 100).Value = username;
                cmd.Parameters.Add("@Password", SqlDbType.NVarChar, 250).Value = passwordTemporal;
                cmd.Parameters.Add("@DepartamentoID", SqlDbType.Int).Value = departamentoProduccionId;
                await cmd.ExecuteNonQueryAsync();

                resultado.CuentasCreadas++;
                resultado.CreadosDetalle.Add(
                    DetalleCuentaPoliV101(
                        persona.Nombre,
                        persona.ApellidoPaterno,
                        persona.ApellidoMaterno,
                        username));
                continue;
            }

            var requiereNormalizacion =
                !string.Equals(persona.UsernameActual, username, StringComparison.OrdinalIgnoreCase)
                || persona.RolIDActual != 4
                || persona.DepartamentoIDActual != departamentoProduccionId
                || !persona.GestionadoPorMatriz;

            if (!persona.UsuarioActivo)
            {
                resultado.CuentasReactivadas++;
                resultado.ReactivadosDetalle.Add(
                    DetalleCuentaPoliV101(
                        persona.Nombre,
                        persona.ApellidoPaterno,
                        persona.ApellidoMaterno,
                        username));
            }
            else if (requiereNormalizacion)
            {
                resultado.CuentasNormalizadas++;
            }

            // IMPORTANTE: no se toca Contrasena/DebeCambiarPassword de cuentas existentes.
            const string updateSql = @"
UPDATE dbo.Usuarios
SET Username=@Username,
    RolID=4,
    Activo=1,
    DepartamentoID=@DepartamentoID,
    GestionadoPorMatrizPolivalencia=1
WHERE UsuarioID=@UsuarioID;";

            await using var updateCmd = new SqlCommand(updateSql, cn, tx);
            updateCmd.Parameters.Add("@Username", SqlDbType.NVarChar, 100).Value = username;
            updateCmd.Parameters.Add("@DepartamentoID", SqlDbType.Int).Value = departamentoProduccionId;
            updateCmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = persona.UsuarioID.Value;
            await updateCmd.ExecuteNonQueryAsync();
        }

        // 2) Personal administrado por Polivalencia que YA NO esta en la vigente.
        const string gestionadosSql = @"
SELECT
    u.UsuarioID,
    u.PersonaID,
    ISNULL(u.Activo,0) AS Activo,
    ISNULL(u.Username,N'') AS Username,
    ISNULL(p.Nombre,N'') AS Nombre,
    ISNULL(p.ApellidoPaterno,N'') AS ApellidoPaterno,
    ISNULL(p.ApellidoMaterno,N'') AS ApellidoMaterno
FROM dbo.Usuarios u
INNER JOIN dbo.Persona p ON p.PersonaID=u.PersonaID
WHERE u.GestionadoPorMatrizPolivalencia=1;";

        var ausentes = new List<(
            int UsuarioID,
            int PersonaID,
            bool Activo,
            string Detalle)>();

        await using (var cmd = new SqlCommand(gestionadosSql, cn, tx))
        await using (var rd = await cmd.ExecuteReaderAsync())
        {
            while (await rd.ReadAsync())
            {
                var personaId = Convert.ToInt32(rd["PersonaID"]);
                if (actualesPorId.ContainsKey(personaId)) continue;

                ausentes.Add((
                    Convert.ToInt32(rd["UsuarioID"]),
                    personaId,
                    Convert.ToBoolean(rd["Activo"]),
                    DetalleCuentaPoliV101(
                        rd["Nombre"]?.ToString() ?? string.Empty,
                        rd["ApellidoPaterno"]?.ToString() ?? string.Empty,
                        rd["ApellidoMaterno"]?.ToString() ?? string.Empty,
                        rd["Username"]?.ToString() ?? string.Empty)));
            }
        }

        foreach (var ausente in ausentes)
        {
            const string bajaUsuarioSql = @"
UPDATE dbo.Usuarios
SET Activo=0
WHERE UsuarioID=@UsuarioID
  AND GestionadoPorMatrizPolivalencia=1
  AND ISNULL(Activo,0)<>0;";

            await using (var cmd = new SqlCommand(bajaUsuarioSql, cn, tx))
            {
                cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = ausente.UsuarioID;
                var afectadas = await cmd.ExecuteNonQueryAsync();
                resultado.CuentasDesactivadas += afectadas;

                if (afectadas > 0)
                    resultado.DesactivadosDetalle.Add(ausente.Detalle);
            }

            const string bajaPersonaSql = @"
UPDATE dbo.Persona
SET EsColaboradorActivo=0,
    FechaBaja=COALESCE(FechaBaja,CONVERT(date,GETDATE()))
WHERE PersonaID=@PersonaID
  AND (ISNULL(EsColaboradorActivo,1)<>0 OR FechaBaja IS NULL);";

            await using (var cmd = new SqlCommand(bajaPersonaSql, cn, tx))
            {
                cmd.Parameters.Add("@PersonaID", SqlDbType.Int).Value = ausente.PersonaID;
                resultado.PersonasDesactivadas += await cmd.ExecuteNonQueryAsync();
            }
        }

        return resultado;
    }
}