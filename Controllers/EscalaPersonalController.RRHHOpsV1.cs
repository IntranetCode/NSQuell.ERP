using ERP.NSQuell.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EscalaVM = ERP.NSQuell.Models.EscalaPersonal;

namespace ERP.NSQuell.Controllers
{
    // NSQ_RRHH_ESCALA_OPERADORES_INTEGRACION_V1_0
    public partial class EscalaPersonalController : IAsyncActionFilter
    {
        private const string NombreFuncionOperadorRRHHV1 = "OPERADOR";

        public async Task OnActionExecutionAsync(
            ActionExecutingContext context,
            ActionExecutionDelegate next)
        {
            if (!await PuedeGestionarOperadoresRRHHV1Async())
            {
                context.Result = new ForbidResult();
                return;
            }

            await next();
        }

        [HttpGet]
        public async Task<IActionResult> Operadores()
        {
            if (!await PuedeGestionarOperadoresRRHHV1Async())
                return StatusCode(403);

            var modelo = new EscalaPersonalOperadoresRhVm
            {
                PuedeAdministrar = true,
                Operadores = await CargarOperadoresRRHHV1Async()
            };

            return View("Operadores", modelo);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AltaOperador(EscalaPersonalAltaOperadorRhVm modelo)
        {
            if (!await PuedeGestionarOperadoresRRHHV1Async())
                return StatusCode(403);

            modelo.NumeroControl = (modelo.NumeroControl ?? string.Empty).Trim();
            modelo.Nombre = (modelo.Nombre ?? string.Empty).Trim();
            modelo.ApellidoPaterno = (modelo.ApellidoPaterno ?? string.Empty).Trim();
            modelo.ApellidoMaterno = string.IsNullOrWhiteSpace(modelo.ApellidoMaterno)
                ? null : modelo.ApellidoMaterno.Trim();
            modelo.Correo = string.IsNullOrWhiteSpace(modelo.Correo)
                ? null : modelo.Correo.Trim();
            modelo.PasswordTemporal = string.IsNullOrWhiteSpace(modelo.PasswordTemporal)
                ? null : modelo.PasswordTemporal.Trim();

            if (!ModelState.IsValid)
            {
                TempData["Error"] = ModelState.Values
                    .SelectMany(x => x.Errors)
                    .Select(x => x.ErrorMessage)
                    .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))
                    ?? "Revisa los datos del operador.";
                return RedirectToAction(nameof(Operadores));
            }

            await using var cn = new SqlConnection(_connectionString);
            await cn.OpenAsync();
            await using var tx =
                (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);

            try
            {
                await ValidarEstructuraOperadoresRRHHV1Async(cn, tx);

                var rolOperadorId = await ObtenerRolOperadorRRHHV1Async(cn, tx);
                var departamentoProduccionId =
                    await ObtenerDepartamentoProduccionRRHHV1Async(cn, tx);

                var personaId = await BuscarPersonaPorControlRRHHV1Async(
                    modelo.NumeroControl,
                    cn,
                    tx);

                if (personaId.HasValue)
                {
                    const string sqlPersona = @"
UPDATE dbo.Persona
SET Nombre=@Nombre,
    ApellidoPaterno=@ApellidoPaterno,
    ApellidoMaterno=@ApellidoMaterno,
    Correo=@Correo,
    FechaIngreso=COALESCE(@FechaIngreso,FechaIngreso),
    Puesto=N'OPERADOR',
    EsColaboradorActivo=1,
    FechaBaja=NULL
WHERE PersonaID=@PersonaID;";

                    await using var cmd = new SqlCommand(sqlPersona, cn, tx);
                    cmd.Parameters.Add("@Nombre", SqlDbType.NVarChar, 150).Value = modelo.Nombre;
                    cmd.Parameters.Add("@ApellidoPaterno", SqlDbType.NVarChar, 150).Value = modelo.ApellidoPaterno;
                    cmd.Parameters.Add("@ApellidoMaterno", SqlDbType.NVarChar, 150).Value =
                        (object?)modelo.ApellidoMaterno ?? DBNull.Value;
                    cmd.Parameters.Add("@Correo", SqlDbType.NVarChar, 150).Value =
                        (object?)modelo.Correo ?? DBNull.Value;
                    cmd.Parameters.Add("@FechaIngreso", SqlDbType.Date).Value =
                        (object?)modelo.FechaIngreso?.Date ?? DBNull.Value;
                    cmd.Parameters.Add("@PersonaID", SqlDbType.Int).Value = personaId.Value;
                    await cmd.ExecuteNonQueryAsync();
                }
                else
                {
                    var sqlInsertPersona = @"
INSERT INTO dbo.Persona
(
    Nombre,ApellidoPaterno,ApellidoMaterno,Correo,FechaIngreso,
    Puesto,EsColaboradorActivo,FechaBaja,NumeroControl
)
VALUES
(
    @Nombre,@ApellidoPaterno,@ApellidoMaterno,@Correo,@FechaIngreso,
    N'OPERADOR',1,NULL,@NumeroControl
);
SELECT CONVERT(INT,SCOPE_IDENTITY());";

                    await using var cmd = new SqlCommand(sqlInsertPersona, cn, tx);
                    cmd.Parameters.Add("@Nombre", SqlDbType.NVarChar, 150).Value = modelo.Nombre;
                    cmd.Parameters.Add("@ApellidoPaterno", SqlDbType.NVarChar, 150).Value = modelo.ApellidoPaterno;
                    cmd.Parameters.Add("@ApellidoMaterno", SqlDbType.NVarChar, 150).Value =
                        (object?)modelo.ApellidoMaterno ?? DBNull.Value;
                    cmd.Parameters.Add("@Correo", SqlDbType.NVarChar, 150).Value =
                        (object?)modelo.Correo ?? DBNull.Value;
                    cmd.Parameters.Add("@FechaIngreso", SqlDbType.Date).Value =
                        (object?)modelo.FechaIngreso?.Date ?? DateTime.Today;
                    cmd.Parameters.Add("@NumeroControl", SqlDbType.NVarChar, 50).Value = modelo.NumeroControl;

                    personaId = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                }

                if (!personaId.HasValue || personaId.Value <= 0)
                    throw new InvalidOperationException("No fue posible resolver la Persona del operador.");

                var username = ConstruirUsernameOperadorRRHHV1(
                    modelo.Nombre,
                    modelo.ApellidoPaterno,
                    modelo.ApellidoMaterno);

                await ValidarUsernameLibreRRHHV1Async(
                    username,
                    personaId.Value,
                    cn,
                    tx);

                var usuario = await ObtenerCuentaPersonaRRHHV1Async(
                    personaId.Value,
                    cn,
                    tx);

                var passwordTemporal =
                    modelo.PasswordTemporal
                    ?? ConstruirPasswordTemporalOperadorRRHHV1(modelo.NumeroControl);

                var tieneMarcador = await TieneColumnaGestionMatrizRRHHV1Async(cn, tx);

                if (usuario.UsuarioID.HasValue)
                {
                    if (usuario.RolID.HasValue && usuario.RolID.Value != rolOperadorId)
                    {
                        throw new InvalidOperationException(
                            $"La persona ya tiene la cuenta ERP {usuario.Username} con un rol distinto a Operador / Capturista. No se modificó para evitar afectar otro acceso.");
                    }

                    var sqlUpdateUsuario = tieneMarcador
                        ? @"
UPDATE dbo.Usuarios
SET Username=@Username,
    Contrasena=@Password,
    RolID=@RolID,
    DepartamentoID=@DepartamentoID,
    Activo=1,
    DebeCambiarPassword=1,
    FechaUltimoCambioPassword=NULL,
    GestionadoPorMatrizPolivalencia=0
WHERE UsuarioID=@UsuarioID;"
                        : @"
UPDATE dbo.Usuarios
SET Username=@Username,
    Contrasena=@Password,
    RolID=@RolID,
    DepartamentoID=@DepartamentoID,
    Activo=1,
    DebeCambiarPassword=1,
    FechaUltimoCambioPassword=NULL
WHERE UsuarioID=@UsuarioID;";

                    await using var cmd = new SqlCommand(sqlUpdateUsuario, cn, tx);
                    cmd.Parameters.Add("@Username", SqlDbType.NVarChar, 100).Value = username;
                    cmd.Parameters.Add("@Password", SqlDbType.NVarChar, 250).Value = passwordTemporal;
                    cmd.Parameters.Add("@RolID", SqlDbType.Int).Value = rolOperadorId;
                    cmd.Parameters.Add("@DepartamentoID", SqlDbType.Int).Value = departamentoProduccionId;
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuario.UsuarioID.Value;
                    await cmd.ExecuteNonQueryAsync();
                }
                else
                {
                    var sqlInsertUsuario = tieneMarcador
                        ? @"
INSERT INTO dbo.Usuarios
(
    PersonaID,Username,Contrasena,RolID,FechaCreacion,Activo,
    DebeCambiarPassword,FechaUltimoCambioPassword,DepartamentoID,
    GestionadoPorMatrizPolivalencia
)
VALUES
(
    @PersonaID,@Username,@Password,@RolID,GETDATE(),1,
    1,NULL,@DepartamentoID,0
);"
                        : @"
INSERT INTO dbo.Usuarios
(
    PersonaID,Username,Contrasena,RolID,FechaCreacion,Activo,
    DebeCambiarPassword,FechaUltimoCambioPassword,DepartamentoID
)
VALUES
(
    @PersonaID,@Username,@Password,@RolID,GETDATE(),1,
    1,NULL,@DepartamentoID
);";

                    await using var cmd = new SqlCommand(sqlInsertUsuario, cn, tx);
                    cmd.Parameters.Add("@PersonaID", SqlDbType.Int).Value = personaId.Value;
                    cmd.Parameters.Add("@Username", SqlDbType.NVarChar, 100).Value = username;
                    cmd.Parameters.Add("@Password", SqlDbType.NVarChar, 250).Value = passwordTemporal;
                    cmd.Parameters.Add("@RolID", SqlDbType.Int).Value = rolOperadorId;
                    cmd.Parameters.Add("@DepartamentoID", SqlDbType.Int).Value = departamentoProduccionId;
                    await cmd.ExecuteNonQueryAsync();
                }

                await tx.CommitAsync();

                TempData["Exito"] =
                    $"Operador {modelo.Nombre} {modelo.ApellidoPaterno} habilitado. " +
                    $"Usuario: {username} · Contraseña temporal: {passwordTemporal} · " +
                    "debe cambiarla al iniciar sesión.";

                return RedirectToAction(nameof(Operadores));
            }
            catch (Exception ex)
            {
                try { await tx.RollbackAsync(); } catch { }
                TempData["Error"] = "No fue posible dar de alta al operador: " + ex.Message;
                return RedirectToAction(nameof(Operadores));
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CambiarEstadoOperador(
            int personaID,
            bool activar)
        {
            if (!await PuedeGestionarOperadoresRRHHV1Async())
                return StatusCode(403);

            if (personaID <= 0)
            {
                TempData["Error"] = "Operador inválido.";
                return RedirectToAction(nameof(Operadores));
            }

            await using var cn = new SqlConnection(_connectionString);
            await cn.OpenAsync();
            await using var tx =
                (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);

            try
            {
                await ValidarEstructuraOperadoresRRHHV1Async(cn, tx);

                var rolOperadorId = await ObtenerRolOperadorRRHHV1Async(cn, tx);
                var departamentoProduccionId =
                    await ObtenerDepartamentoProduccionRRHHV1Async(cn, tx);
                var tieneMarcador = await TieneColumnaGestionMatrizRRHHV1Async(cn, tx);

                const string sqlValidar = @"
SELECT COUNT(1)
FROM dbo.Persona p
WHERE p.PersonaID=@PersonaID
  AND
  (
      UPPER(LTRIM(RTRIM(ISNULL(p.Puesto,N'')))) COLLATE Modern_Spanish_CI_AI LIKE N'%OPERADOR%'
      OR EXISTS
      (
          SELECT 1
          FROM dbo.Usuarios u
          WHERE u.PersonaID=p.PersonaID
            AND u.RolID=@RolID
      )
      OR EXISTS
      (
          SELECT 1
          FROM dbo.RRHH_PolivalenciaCompetencias c
          WHERE c.PersonalID=p.PersonaID
            AND c.Activo=1
            AND UPPER(LTRIM(RTRIM(ISNULL(c.PuestoMatriz,N''))))=N'OPERADOR'
      )
  );";

                await using (var cmd = new SqlCommand(sqlValidar, cn, tx))
                {
                    cmd.Parameters.Add("@PersonaID", SqlDbType.Int).Value = personaID;
                    cmd.Parameters.Add("@RolID", SqlDbType.Int).Value = rolOperadorId;
                    if (Convert.ToInt32(await cmd.ExecuteScalarAsync()) != 1)
                        throw new InvalidOperationException("La persona indicada no corresponde a un operador.");
                }

                const string sqlPersona = @"
UPDATE dbo.Persona
SET EsColaboradorActivo=@Activo,
    FechaBaja=CASE WHEN @Activo=1 THEN NULL ELSE CONVERT(date,GETDATE()) END,
    Puesto=CASE WHEN @Activo=1 THEN N'OPERADOR' ELSE Puesto END
WHERE PersonaID=@PersonaID;";

                await using (var cmd = new SqlCommand(sqlPersona, cn, tx))
                {
                    cmd.Parameters.Add("@Activo", SqlDbType.Bit).Value = activar;
                    cmd.Parameters.Add("@PersonaID", SqlDbType.Int).Value = personaID;
                    await cmd.ExecuteNonQueryAsync();
                }

                var sqlUsuario = tieneMarcador
                    ? @"
UPDATE dbo.Usuarios
SET Activo=@Activo,
    RolID=CASE WHEN @Activo=1 THEN @RolID ELSE RolID END,
    DepartamentoID=CASE WHEN @Activo=1 THEN @DepartamentoID ELSE DepartamentoID END,
    GestionadoPorMatrizPolivalencia=0
WHERE PersonaID=@PersonaID
  AND RolID=@RolID;"
                    : @"
UPDATE dbo.Usuarios
SET Activo=@Activo,
    RolID=CASE WHEN @Activo=1 THEN @RolID ELSE RolID END,
    DepartamentoID=CASE WHEN @Activo=1 THEN @DepartamentoID ELSE DepartamentoID END
WHERE PersonaID=@PersonaID
  AND RolID=@RolID;";

                await using (var cmd = new SqlCommand(sqlUsuario, cn, tx))
                {
                    cmd.Parameters.Add("@Activo", SqlDbType.Bit).Value = activar;
                    cmd.Parameters.Add("@RolID", SqlDbType.Int).Value = rolOperadorId;
                    cmd.Parameters.Add("@DepartamentoID", SqlDbType.Int).Value = departamentoProduccionId;
                    cmd.Parameters.Add("@PersonaID", SqlDbType.Int).Value = personaID;
                    await cmd.ExecuteNonQueryAsync();
                }

                await tx.CommitAsync();

                TempData["Exito"] = activar
                    ? "El operador quedó activo nuevamente."
                    : "El operador fue dado de baja. Se conserva su historial y ya no aparecerá como disponible.";

                return RedirectToAction(nameof(Operadores));
            }
            catch (Exception ex)
            {
                try { await tx.RollbackAsync(); } catch { }
                TempData["Error"] = "No fue posible actualizar al operador: " + ex.Message;
                return RedirectToAction(nameof(Operadores));
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RestablecerPasswordOperador(int personaID)
        {
            if (!await PuedeGestionarOperadoresRRHHV1Async())
                return StatusCode(403);

            await using var cn = new SqlConnection(_connectionString);
            await cn.OpenAsync();
            await using var tx =
                (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.Serializable);

            try
            {
                const string sqlDatos = @"
SELECT TOP(1)
    ISNULL(p.NumeroControl,N'') AS NumeroControl,
    ISNULL(p.EsColaboradorActivo,0) AS PersonaActiva,
    u.UsuarioID,
    ISNULL(u.Username,N'') AS Username
FROM dbo.Persona p
LEFT JOIN dbo.Usuarios u
  ON u.PersonaID=p.PersonaID
 AND u.RolID=4
WHERE p.PersonaID=@PersonaID
ORDER BY u.UsuarioID;";

                string numeroControl;
                bool activa;
                int? usuarioID;
                string username;

                await using (var cmd = new SqlCommand(sqlDatos, cn, tx))
                {
                    cmd.Parameters.Add("@PersonaID", SqlDbType.Int).Value = personaID;
                    await using var rd = await cmd.ExecuteReaderAsync();

                    if (!await rd.ReadAsync())
                        throw new InvalidOperationException("No se encontró el operador.");

                    numeroControl = rd["NumeroControl"]?.ToString()?.Trim() ?? string.Empty;
                    activa = Convert.ToBoolean(rd["PersonaActiva"]);
                    usuarioID = rd["UsuarioID"] == DBNull.Value
                        ? null : Convert.ToInt32(rd["UsuarioID"]);
                    username = rd["Username"]?.ToString()?.Trim() ?? string.Empty;
                }

                if (!activa)
                    throw new InvalidOperationException("No se puede restablecer la contraseña de un operador dado de baja.");

                if (!usuarioID.HasValue)
                    throw new InvalidOperationException("El operador no tiene una cuenta ERP de Operador / Capturista.");

                var password = ConstruirPasswordTemporalOperadorRRHHV1(numeroControl);

                const string sql = @"
UPDATE dbo.Usuarios
SET Contrasena=@Password,
    DebeCambiarPassword=1,
    FechaUltimoCambioPassword=NULL,
    Activo=1
WHERE UsuarioID=@UsuarioID;";

                await using (var cmd = new SqlCommand(sql, cn, tx))
                {
                    cmd.Parameters.Add("@Password", SqlDbType.NVarChar, 250).Value = password;
                    cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioID.Value;
                    await cmd.ExecuteNonQueryAsync();
                }

                await tx.CommitAsync();

                TempData["Exito"] =
                    $"Contraseña restablecida para {username}. Temporal: {password}. Debe cambiarla al iniciar sesión.";

                return RedirectToAction(nameof(Operadores));
            }
            catch (Exception ex)
            {
                try { await tx.RollbackAsync(); } catch { }
                TempData["Error"] = "No fue posible restablecer la contraseña: " + ex.Message;
                return RedirectToAction(nameof(Operadores));
            }
        }

        private async Task<bool> PuedeGestionarOperadoresRRHHV1Async()
        {
            var usuarioID = ObtenerUsuarioID();
            if (!usuarioID.HasValue || usuarioID.Value <= 0)
                return false;

            const string sql = @"
SELECT TOP(1)
    ISNULL(r.NombreRol,N'') AS Rol,
    ISNULL(d.NombreDepartamento,N'') AS Departamento
FROM dbo.Usuarios u
LEFT JOIN dbo.Roles r ON r.RolID=u.RolID
LEFT JOIN dbo.Departamentos d ON d.DepartamentoID=u.DepartamentoID
WHERE u.UsuarioID=@UsuarioID
  AND ISNULL(u.Activo,0)=1;";

            await using var cn = new SqlConnection(_connectionString);
            await cn.OpenAsync();
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add("@UsuarioID", SqlDbType.Int).Value = usuarioID.Value;
            await using var rd = await cmd.ExecuteReaderAsync();

            if (!await rd.ReadAsync())
                return false;

            var rol = NormalizarCuentaRRHHV1(rd["Rol"]?.ToString());
            var departamento = NormalizarCuentaRRHHV1(rd["Departamento"]?.ToString());

            return rol is "ADMINISTRADORDEERP"
                or "ADMINISTRADORDETI"
                or "MODULOOPERADORES"
                or "RECURSOSHUMANOS"
                || departamento == "RECURSOSHUMANOS";
        }

        private async Task<List<EscalaPersonalOperadorRhVm>> CargarOperadoresRRHHV1Async()
        {
            const string sql = @"
DECLARE @RolOperadorID INT =
(
    SELECT TOP(1) RolID
    FROM dbo.Roles
    WHERE Activo=1
      AND UPPER(LTRIM(RTRIM(NombreRol))) COLLATE Modern_Spanish_CI_AI=N'OPERADOR / CAPTURISTA'
    ORDER BY RolID
);

SELECT
    p.PersonaID,
    ISNULL(p.NumeroControl,N'') AS NumeroControl,
    LTRIM(RTRIM(CONCAT(
        ISNULL(p.Nombre,N''),N' ',
        ISNULL(p.ApellidoPaterno,N''),N' ',
        ISNULL(p.ApellidoMaterno,N'')))) AS NombreCompleto,
    ISNULL(p.Puesto,N'') AS Puesto,
    p.FechaIngreso,
    p.FechaBaja,
    ISNULL(p.EsColaboradorActivo,0) AS PersonaActiva,
    u.UsuarioID,
    ISNULL(u.Username,N'') AS Username,
    ISNULL(u.Activo,0) AS UsuarioActivo,
    ISNULL(u.DebeCambiarPassword,0) AS DebeCambiarPassword,
    ISNULL(pol.Partes,0) AS PartesPolivalencia
FROM dbo.Persona p
OUTER APPLY
(
    SELECT TOP(1)
        ux.UsuarioID,
        ux.Username,
        ux.Activo,
        ux.DebeCambiarPassword
    FROM dbo.Usuarios ux
    WHERE ux.PersonaID=p.PersonaID
      AND (@RolOperadorID IS NULL OR ux.RolID=@RolOperadorID)
    ORDER BY CASE WHEN ISNULL(ux.Activo,0)=1 THEN 0 ELSE 1 END,ux.UsuarioID
) u
OUTER APPLY
(
    SELECT COUNT(DISTINCT v.ParteID) AS Partes
    FROM dbo.vw_RRHH_PolivalenciaOperadoresParte v
    WHERE v.PersonalID=p.PersonaID
      AND v.Nivel BETWEEN 1 AND 4
      AND UPPER(LTRIM(RTRIM(ISNULL(v.PuestoMatriz,N''))))=N'OPERADOR'
) pol
WHERE
    UPPER(LTRIM(RTRIM(ISNULL(p.Puesto,N'')))) COLLATE Modern_Spanish_CI_AI LIKE N'%OPERADOR%'
    OR u.UsuarioID IS NOT NULL
    OR EXISTS
    (
        SELECT 1
        FROM dbo.RRHH_PolivalenciaCompetencias c
        WHERE c.PersonalID=p.PersonaID
          AND c.Activo=1
          AND UPPER(LTRIM(RTRIM(ISNULL(c.PuestoMatriz,N''))))=N'OPERADOR'
    )
ORDER BY
    ISNULL(p.EsColaboradorActivo,0) DESC,
    p.ApellidoPaterno,
    p.ApellidoMaterno,
    p.Nombre;";

            var lista = new List<EscalaPersonalOperadorRhVm>();

            await using var cn = new SqlConnection(_connectionString);
            await cn.OpenAsync();
            await using var cmd = new SqlCommand(sql, cn);
            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                lista.Add(new EscalaPersonalOperadorRhVm
                {
                    PersonaID = Convert.ToInt32(rd["PersonaID"]),
                    NumeroControl = rd["NumeroControl"]?.ToString()?.Trim() ?? string.Empty,
                    NombreCompleto = rd["NombreCompleto"]?.ToString()?.Trim() ?? string.Empty,
                    Puesto = rd["Puesto"]?.ToString()?.Trim() ?? string.Empty,
                    FechaIngreso = rd["FechaIngreso"] == DBNull.Value
                        ? null : Convert.ToDateTime(rd["FechaIngreso"]),
                    FechaBaja = rd["FechaBaja"] == DBNull.Value
                        ? null : Convert.ToDateTime(rd["FechaBaja"]),
                    PersonaActiva = Convert.ToBoolean(rd["PersonaActiva"]),
                    UsuarioID = rd["UsuarioID"] == DBNull.Value
                        ? null : Convert.ToInt32(rd["UsuarioID"]),
                    Username = rd["Username"]?.ToString()?.Trim() ?? string.Empty,
                    UsuarioActivo = Convert.ToBoolean(rd["UsuarioActivo"]),
                    DebeCambiarPassword = Convert.ToBoolean(rd["DebeCambiarPassword"]),
                    PartesPolivalencia = Convert.ToInt32(rd["PartesPolivalencia"])
                });
            }

            return lista;
        }

        private async Task<List<EscalaVM.PersonaOpcion>> ObtenerPersonasOperadoresRRHHV1Async()
        {
            const string sql = @"
SELECT DISTINCT
    p.PersonaID AS PersonalID,
    CAST(NULL AS INT) AS DepartamentoID,
    CAST(N'Producción' AS NVARCHAR(200)) AS NombreDepartamento,
    ISNULL(NULLIF(LTRIM(RTRIM(p.NumeroControl)),N''),CONVERT(VARCHAR(20),p.PersonaID)) AS NumeroEmpleado,
    LTRIM(RTRIM(CONCAT(
        ISNULL(p.Nombre,N''),N' ',
        ISNULL(p.ApellidoPaterno,N''),N' ',
        ISNULL(p.ApellidoMaterno,N'')))) AS NombreCompleto,
    N'OPERADOR' AS Puesto,
    p.FechaIngreso,
    p.FechaBaja,
    CONVERT(bit,ISNULL(p.EsColaboradorActivo,0)) AS Activo
FROM dbo.Persona p
WHERE ISNULL(p.EsColaboradorActivo,0)=1
  AND
  (
      UPPER(LTRIM(RTRIM(ISNULL(p.Puesto,N'')))) COLLATE Modern_Spanish_CI_AI LIKE N'%OPERADOR%'
      OR EXISTS
      (
          SELECT 1
          FROM dbo.RRHH_PolivalenciaCompetencias c
          WHERE c.PersonalID=p.PersonaID
            AND c.Activo=1
            AND UPPER(LTRIM(RTRIM(ISNULL(c.PuestoMatriz,N''))))=N'OPERADOR'
      )
  )
  AND EXISTS
  (
      SELECT 1
      FROM dbo.Usuarios u
      INNER JOIN dbo.Roles r ON r.RolID=u.RolID
      WHERE u.PersonaID=p.PersonaID
        AND ISNULL(u.Activo,0)=1
        AND r.Activo=1
        AND UPPER(LTRIM(RTRIM(r.NombreRol))) COLLATE Modern_Spanish_CI_AI=N'OPERADOR / CAPTURISTA'
  )
ORDER BY NombreCompleto;";

            var personas = new List<EscalaVM.PersonaOpcion>();

            await using var cn = new SqlConnection(_connectionString);
            await cn.OpenAsync();
            await using var cmd = new SqlCommand(sql, cn);
            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                personas.Add(new EscalaVM.PersonaOpcion
                {
                    PersonalID = Convert.ToInt32(rd["PersonalID"]),
                    DepartamentoID = null,
                    NombreDepartamento = rd["NombreDepartamento"]?.ToString() ?? string.Empty,
                    NumeroEmpleado = rd["NumeroEmpleado"]?.ToString()?.Trim() ?? string.Empty,
                    NombreCompleto = rd["NombreCompleto"]?.ToString()?.Trim() ?? string.Empty,
                    Puesto = "OPERADOR",
                    FechaIngreso = rd["FechaIngreso"] == DBNull.Value
                        ? null : Convert.ToDateTime(rd["FechaIngreso"]),
                    FechaBaja = rd["FechaBaja"] == DBNull.Value
                        ? null : Convert.ToDateTime(rd["FechaBaja"]),
                    Activo = Convert.ToBoolean(rd["Activo"])
                });
            }

            return personas;
        }

        private async Task<List<EscalaVM.Funcion>> ObtenerFuncionesOperadorRRHHV1Async()
        {
            const string sql = @"
SELECT TOP(1)
    f.FuncionID,
    f.Nombre,
    f.Descripcion,
    f.DepartamentoID,
    f.RequiereMaquina,
    f.Orden,
    f.Activo,
    f.FechaRegistro,
    f.CreadoPor,
    f.FechaModificacion,
    f.ActualizadoPor,
    ISNULL(d.NombreDepartamento,N'Producción') AS NombreDepartamento
FROM dbo.RRHH_FuncionesPersonal f
LEFT JOIN dbo.Departamentos d ON d.DepartamentoID=f.DepartamentoID
WHERE f.Activo=1
  AND UPPER(LTRIM(RTRIM(f.Nombre))) COLLATE Modern_Spanish_CI_AI=N'OPERADOR'
ORDER BY f.FuncionID;";

            var lista = new List<EscalaVM.Funcion>();

            await using var cn = new SqlConnection(_connectionString);
            await cn.OpenAsync();
            await using var cmd = new SqlCommand(sql, cn);
            await using var rd = await cmd.ExecuteReaderAsync();

            if (await rd.ReadAsync())
            {
                lista.Add(new EscalaVM.Funcion
                {
                    FuncionID = Convert.ToInt32(rd["FuncionID"]),
                    Nombre = rd["Nombre"]?.ToString()?.Trim() ?? "OPERADOR",
                    Descripcion = rd["Descripcion"] == DBNull.Value
                        ? null : rd["Descripcion"]?.ToString()?.Trim(),
                    DepartamentoID = rd["DepartamentoID"] == DBNull.Value
                        ? null : Convert.ToInt32(rd["DepartamentoID"]),
                    RequiereMaquina = Convert.ToBoolean(rd["RequiereMaquina"]),
                    Orden = Convert.ToInt32(rd["Orden"]),
                    Activo = Convert.ToBoolean(rd["Activo"]),
                    FechaRegistro = Convert.ToDateTime(rd["FechaRegistro"]),
                    CreadoPor = rd["CreadoPor"] == DBNull.Value
                        ? null : Convert.ToInt32(rd["CreadoPor"]),
                    FechaModificacion = rd["FechaModificacion"] == DBNull.Value
                        ? null : Convert.ToDateTime(rd["FechaModificacion"]),
                    ActualizadoPor = rd["ActualizadoPor"] == DBNull.Value
                        ? null : Convert.ToInt32(rd["ActualizadoPor"]),
                    NombreDepartamento = rd["NombreDepartamento"]?.ToString()?.Trim() ?? "Producción"
                });
            }

            return lista;
        }

        private static async Task<int?> ObtenerFuncionOperadorIdRRHHV1Async(
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
SELECT TOP(1) FuncionID
FROM dbo.RRHH_FuncionesPersonal
WHERE Activo=1
  AND UPPER(LTRIM(RTRIM(Nombre))) COLLATE Modern_Spanish_CI_AI=N'OPERADOR'
ORDER BY FuncionID;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            var valor = await cmd.ExecuteScalarAsync();
            return valor == null || valor == DBNull.Value
                ? null : Convert.ToInt32(valor);
        }

        private static async Task ValidarEstructuraOperadoresRRHHV1Async(
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
SELECT
    CONVERT(bit,CASE WHEN COL_LENGTH(N'dbo.Persona',N'NumeroControl') IS NOT NULL THEN 1 ELSE 0 END) AS TieneNumeroControl,
    CONVERT(bit,CASE WHEN OBJECT_ID(N'dbo.RRHH_PolivalenciaCompetencias',N'U') IS NOT NULL THEN 1 ELSE 0 END) AS TienePolivalencia,
    CONVERT(bit,CASE WHEN OBJECT_ID(N'dbo.vw_RRHH_PolivalenciaOperadoresParte',N'V') IS NOT NULL THEN 1 ELSE 0 END) AS TieneVista;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            await using var rd = await cmd.ExecuteReaderAsync();
            await rd.ReadAsync();

            if (!Convert.ToBoolean(rd["TieneNumeroControl"]))
                throw new InvalidOperationException("Falta dbo.Persona.NumeroControl.");

            if (!Convert.ToBoolean(rd["TienePolivalencia"]))
                throw new InvalidOperationException("Falta dbo.RRHH_PolivalenciaCompetencias.");

            if (!Convert.ToBoolean(rd["TieneVista"]))
                throw new InvalidOperationException("Falta dbo.vw_RRHH_PolivalenciaOperadoresParte.");
        }

        private static async Task<int> ObtenerRolOperadorRRHHV1Async(
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
SELECT TOP(1) RolID
FROM dbo.Roles
WHERE Activo=1
  AND UPPER(LTRIM(RTRIM(NombreRol))) COLLATE Modern_Spanish_CI_AI=N'OPERADOR / CAPTURISTA'
ORDER BY RolID;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            var valor = await cmd.ExecuteScalarAsync();

            if (valor == null || valor == DBNull.Value)
                throw new InvalidOperationException("No existe el rol activo Operador / Capturista.");

            return Convert.ToInt32(valor);
        }

        private static async Task<int> ObtenerDepartamentoProduccionRRHHV1Async(
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
SELECT TOP(1) DepartamentoID
FROM dbo.Departamentos
WHERE Activo=1
  AND UPPER(LTRIM(RTRIM(NombreDepartamento))) COLLATE Modern_Spanish_CI_AI=N'PRODUCCION'
ORDER BY DepartamentoID;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            var valor = await cmd.ExecuteScalarAsync();

            if (valor == null || valor == DBNull.Value)
                throw new InvalidOperationException("No existe el departamento activo Producción.");

            return Convert.ToInt32(valor);
        }

        private static async Task<int?> BuscarPersonaPorControlRRHHV1Async(
            string numeroControl,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
SELECT TOP(1) PersonaID
FROM dbo.Persona
WHERE UPPER(LTRIM(RTRIM(ISNULL(NumeroControl,N''))))=UPPER(@NumeroControl)
ORDER BY PersonaID;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@NumeroControl", SqlDbType.NVarChar, 50).Value = numeroControl;
            var valor = await cmd.ExecuteScalarAsync();
            return valor == null || valor == DBNull.Value
                ? null : Convert.ToInt32(valor);
        }

        private sealed class CuentaPersonaRRHHV1
        {
            public int? UsuarioID { get; set; }
            public int? RolID { get; set; }
            public string Username { get; set; } = string.Empty;
        }

        private static async Task<CuentaPersonaRRHHV1> ObtenerCuentaPersonaRRHHV1Async(
            int personaId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
SELECT UsuarioID,RolID,ISNULL(Username,N'') AS Username
FROM dbo.Usuarios
WHERE PersonaID=@PersonaID
ORDER BY UsuarioID;";

            var cuentas = new List<CuentaPersonaRRHHV1>();
            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@PersonaID", SqlDbType.Int).Value = personaId;
            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                cuentas.Add(new CuentaPersonaRRHHV1
                {
                    UsuarioID = Convert.ToInt32(rd["UsuarioID"]),
                    RolID = rd["RolID"] == DBNull.Value
                        ? null : Convert.ToInt32(rd["RolID"]),
                    Username = rd["Username"]?.ToString()?.Trim() ?? string.Empty
                });
            }

            if (cuentas.Count > 1)
                throw new InvalidOperationException("La persona tiene más de una cuenta ERP. Debe depurarse antes de administrarla desde RH.");

            return cuentas.FirstOrDefault() ?? new CuentaPersonaRRHHV1();
        }

        private static async Task ValidarUsernameLibreRRHHV1Async(
            string username,
            int personaId,
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql = @"
SELECT TOP(1) UsuarioID,PersonaID
FROM dbo.Usuarios
WHERE UPPER(LTRIM(RTRIM(Username)))=UPPER(@Username)
  AND PersonaID<>@PersonaID
ORDER BY UsuarioID;";

            await using var cmd = new SqlCommand(sql, cn, tx);
            cmd.Parameters.Add("@Username", SqlDbType.NVarChar, 100).Value = username;
            cmd.Parameters.Add("@PersonaID", SqlDbType.Int).Value = personaId;
            await using var rd = await cmd.ExecuteReaderAsync();

            if (await rd.ReadAsync())
                throw new InvalidOperationException(
                    $"El username {username} ya pertenece a otra persona. No se creó una cuenta duplicada.");
        }

        private static async Task<bool> TieneColumnaGestionMatrizRRHHV1Async(
            SqlConnection cn,
            SqlTransaction tx)
        {
            const string sql =
                "SELECT CONVERT(bit,CASE WHEN COL_LENGTH(N'dbo.Usuarios',N'GestionadoPorMatrizPolivalencia') IS NULL THEN 0 ELSE 1 END);";

            await using var cmd = new SqlCommand(sql, cn, tx);
            return Convert.ToBoolean(await cmd.ExecuteScalarAsync() ?? false);
        }

        private static string NormalizarCuentaRRHHV1(string? valor)
        {
            if (string.IsNullOrWhiteSpace(valor))
                return string.Empty;

            var normalizado = valor.Trim().Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(normalizado.Length);

            foreach (var ch in normalizado)
            {
                var categoria = CharUnicodeInfo.GetUnicodeCategory(ch);
                if (categoria == UnicodeCategory.NonSpacingMark)
                    continue;

                if (char.IsLetterOrDigit(ch))
                    sb.Append(char.ToUpperInvariant(ch));
            }

            return sb.ToString();
        }

        private static string ConstruirUsernameOperadorRRHHV1(
            string nombre,
            string apellidoPaterno,
            string? apellidoMaterno)
        {
            var n = NormalizarCuentaRRHHV1(nombre);
            var ap = NormalizarCuentaRRHHV1(apellidoPaterno);
            var am = NormalizarCuentaRRHHV1(apellidoMaterno);

            if (string.IsNullOrWhiteSpace(n))
                throw new InvalidOperationException("No se puede generar username sin nombre.");

            if (string.IsNullOrWhiteSpace(ap))
                throw new InvalidOperationException("No se puede generar username sin apellido paterno.");

            var username = n[..1] + ap + (string.IsNullOrWhiteSpace(am) ? string.Empty : am[..1]);
            return username.Length <= 100 ? username : username[..100];
        }

        private static string ConstruirPasswordTemporalOperadorRRHHV1(string numeroControl)
        {
            var control = NormalizarCuentaRRHHV1(numeroControl);
            if (string.IsNullOrWhiteSpace(control))
                throw new InvalidOperationException("No se puede generar contraseña sin número de control.");

            return $"Quell{control}!";
        }
    }
}