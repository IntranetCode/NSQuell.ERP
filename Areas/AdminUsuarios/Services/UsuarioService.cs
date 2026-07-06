using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using ERP.NSQuell.Areas.AdminUsuarios.DTOs;
using ERP.NSQuell.Areas.AdminUsuarios.Interfaces;
using ERP.NSQuell.Models;
using ERP.NSQuell.Models.ModelUsuarios;
using System.Data;
using System.Linq;

namespace ERP.NSQuell.Areas.AdminUsuarios.Services
{
    public class UsuarioService : IUsuarioService
    {
        private readonly ApplicationDbContext _context;

        public UsuarioService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<V_InformacionUsuarioCompleta>> ObtenerTodosAsync(
            bool? activos,
            string? filtroCampo,
            string? terminoBusqueda)
        {
            var query = _context.InformacionUsuariosCompletos.AsQueryable();

            if (activos.HasValue)
            {
                query = query.Where(u => u.Activo == activos.Value);
            }

            if (!string.IsNullOrWhiteSpace(terminoBusqueda))
            {
                var busquedaLower = terminoBusqueda.Trim().ToLower();

                switch (filtroCampo)
                {
                    case "Nombre":
                        query = query.Where(u => u.Nombre.ToLower().Contains(busquedaLower));
                        break;

                    case "ApellidoPaterno":
                        query = query.Where(u => u.ApellidoPaterno.ToLower().Contains(busquedaLower));
                        break;

                    case "Correo":
                        query = query.Where(u => u.Correo != null && u.Correo.ToLower().Contains(busquedaLower));
                        break;

                    case "Username":
                        query = query.Where(u => u.Username.ToLower().Contains(busquedaLower));
                        break;

                    default:
                        query = query.Where(u =>
                            u.Username.ToLower().Contains(busquedaLower) ||
                            u.Nombre.ToLower().Contains(busquedaLower) ||
                            u.ApellidoPaterno.ToLower().Contains(busquedaLower) ||
                            (u.Correo != null && u.Correo.ToLower().Contains(busquedaLower))
                        );
                        break;
                }
            }

            return await query
                .OrderBy(u => u.Nombre)
                .ThenBy(u => u.ApellidoPaterno)
                .ThenBy(u => u.ApellidoMaterno)
                .ToListAsync();
        }

        private async Task<int?> ObtenerDepartamentoUsuarioAsync(int usuarioId)
{
    var conn = _context.Database.GetDbConnection();

    if (conn.State != ConnectionState.Open)
        await conn.OpenAsync();

    using var cmd = conn.CreateCommand();

    cmd.CommandText = @"
        SELECT TOP 1 DepartamentoID
        FROM dbo.Usuarios
        WHERE UsuarioID = @UsuarioID;";

    cmd.CommandType = CommandType.Text;

    var pUsuario = cmd.CreateParameter();
    pUsuario.ParameterName = "@UsuarioID";
    pUsuario.Value = usuarioId;
    cmd.Parameters.Add(pUsuario);

    var result = await cmd.ExecuteScalarAsync();

    if (result == null || result == DBNull.Value)
        return null;

    return Convert.ToInt32(result);
}

        public async Task<UsuarioEdicionDTO?> ObtenerParaEditarAsync(int usuarioId)
        {
            var usuario = await _context.Usuarios
                .Include(u => u.Persona)
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.UsuarioID == usuarioId);

            if (usuario == null || usuario.Persona == null)
                return null;

            var historial = await ObtenerHistorialAsync(usuarioId);
            var subMenusEfectivos = await ObtenerSubMenusEfectivosAsync(usuarioId);
            var departamentoId = await ObtenerDepartamentoUsuarioAsync(usuarioId);

            return new UsuarioEdicionDTO
            {
                UsuarioID = usuario.UsuarioID,
                Nombre = usuario.Persona.Nombre,
                ApellidoPaterno = usuario.Persona.ApellidoPaterno,
                ApellidoMaterno = usuario.Persona.ApellidoMaterno,
                Correo = usuario.Persona.Correo,
                Telefono = usuario.Persona.Telefono,
                RolID = usuario.RolID,
                Activo = usuario.Activo,

                SubMenuIDs = subMenusEfectivos,
                HistorialDeCambios = historial,

                NumeroEmpleado = usuario.Persona.NumeroEmpleado,
                ClaveEmpleadoNomina = usuario.Persona.ClaveEmpleadoNomina,
                FechaIngreso = usuario.Persona.FechaIngreso,
                Puesto = usuario.Persona.Puesto,
                FechaNacimiento = usuario.Persona.FechaNacimiento,
                JefeInmediatoPersonaID = usuario.Persona.JefeInmediatoPersonaID,
                DepartamentoID = departamentoId
            };
        }

        private async Task<List<int>> ObtenerSubMenusEfectivosAsync(int usuarioId)
        {
            var lista = new List<int>();

            var conn = _context.Database.GetDbConnection();

            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync();

            using var cmd = conn.CreateCommand();

            cmd.CommandText = @"
                SELECT fe.SubMenuID
                FROM dbo.fn_PermisosEfectivosUsuario(@UsuarioID) AS fe
                WHERE fe.TienePermiso = 1;";

            cmd.CommandType = CommandType.Text;

            var pU = cmd.CreateParameter();
            pU.ParameterName = "@UsuarioID";
            pU.Value = usuarioId;
            cmd.Parameters.Add(pU);

            using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                lista.Add(Convert.ToInt32(rd["SubMenuID"]));
            }

            return lista
                .Distinct()
                .OrderBy(x => x)
                .ToList();
        }

        public async Task RegistrarAsync(UsuarioRegistroDTO nuevoUsuario)
        {
            var passwordDirecta = nuevoUsuario.Password;

            var subMenusTable = new DataTable();
            subMenusTable.Columns.Add("ID", typeof(int));

            if (nuevoUsuario.SubMenuIDs != null)
            {
                foreach (var subMenuId in nuevoUsuario.SubMenuIDs.Distinct())
                {
                    subMenusTable.Rows.Add(subMenuId);
                }
            }

            var subMenusParam = new SqlParameter
            {
                ParameterName = "@SubMenuIDs",
                SqlDbType = SqlDbType.Structured,
                Value = subMenusTable,
                TypeName = "dbo.ListaDeEnteros"
            };

            var parameters = new object[]
            {
                new SqlParameter("@Nombre", nuevoUsuario.Nombre),
                new SqlParameter("@ApellidoPaterno", nuevoUsuario.ApellidoPaterno),
                new SqlParameter("@Correo", string.IsNullOrWhiteSpace(nuevoUsuario.Correo) ? DBNull.Value : nuevoUsuario.Correo.Trim()),
                new SqlParameter("@Username", nuevoUsuario.Username),
                new SqlParameter("@ContrasenaHash", passwordDirecta),
                new SqlParameter("@RolID", nuevoUsuario.RolID),
                new SqlParameter("@ApellidoMaterno", (object?)nuevoUsuario.ApellidoMaterno ?? DBNull.Value),
                new SqlParameter("@Telefono", (object?)nuevoUsuario.Telefono ?? DBNull.Value),
                subMenusParam,

                new SqlParameter("@FechaIngreso", SqlDbType.Date) { Value = (object?)nuevoUsuario.FechaIngreso ?? DBNull.Value },
                new SqlParameter("@JefeInmediatoPersonaID", (object?)nuevoUsuario.JefeInmediatoPersonaID ?? DBNull.Value),
                new SqlParameter("@NumeroEmpleado", (object?)nuevoUsuario.NumeroEmpleado ?? DBNull.Value),
                new SqlParameter("@Puesto", (object?)nuevoUsuario.Puesto ?? DBNull.Value),
                new SqlParameter("@FechaNacimiento", SqlDbType.Date) { Value = (object?)nuevoUsuario.FechaNacimiento ?? DBNull.Value },
                new SqlParameter("@ClaveEmpleadoNomina", (object?)nuevoUsuario.ClaveEmpleadoNomina ?? DBNull.Value),
                new SqlParameter("@DepartamentoID", (object?)nuevoUsuario.DepartamentoID ?? DBNull.Value)
            };

            await _context.Database.ExecuteSqlRawAsync(
                @"EXEC dbo.sp_RegistrarUsuario 
                    @Nombre,
                    @ApellidoPaterno,
                    @Correo,
                    @Username,
                    @ContrasenaHash,
                    @RolID,
                    @ApellidoMaterno,
                    @Telefono,
                    @SubMenuIDs,
                    @FechaIngreso,
                    @JefeInmediatoPersonaID,
                    @NumeroEmpleado,
                    @Puesto,
                    @FechaNacimiento,
                    @ClaveEmpleadoNomina,
                    @DepartamentoID",
                parameters
            );
        }

        public async Task ActualizarAsync(UsuarioEdicionDTO usuario)
        {
            var subMenusTable = new DataTable();
            subMenusTable.Columns.Add("ID", typeof(int));

            if (usuario.SubMenuIDs != null)
            {
                foreach (var subMenuId in usuario.SubMenuIDs.Distinct())
                {
                    subMenusTable.Rows.Add(subMenuId);
                }
            }

            var subMenusParam = new SqlParameter
            {
                ParameterName = "@SubMenuIDs",
                SqlDbType = SqlDbType.Structured,
                Value = subMenusTable,
                TypeName = "dbo.ListaDeEnteros"
            };

            var parameters = new object[]
            {
                new SqlParameter("@UsuarioID", usuario.UsuarioID),
                new SqlParameter("@Nombre", usuario.Nombre),
                new SqlParameter("@ApellidoPaterno", usuario.ApellidoPaterno),
                new SqlParameter("@Correo", string.IsNullOrWhiteSpace(usuario.Correo) ? DBNull.Value : usuario.Correo.Trim()),
                new SqlParameter("@RolID", usuario.RolID),
                new SqlParameter("@Activo", usuario.Activo),
                new SqlParameter("@ApellidoMaterno", (object?)usuario.ApellidoMaterno ?? DBNull.Value),
                new SqlParameter("@Telefono", (object?)usuario.Telefono ?? DBNull.Value),
                subMenusParam,

                new SqlParameter("@FechaIngreso", SqlDbType.Date) { Value = (object?)usuario.FechaIngreso ?? DBNull.Value },
                new SqlParameter("@JefeInmediatoPersonaID", (object?)usuario.JefeInmediatoPersonaID ?? DBNull.Value),
                new SqlParameter("@NumeroEmpleado", (object?)usuario.NumeroEmpleado ?? DBNull.Value),
                new SqlParameter("@Puesto", (object?)usuario.Puesto ?? DBNull.Value),
                new SqlParameter("@FechaNacimiento", SqlDbType.Date) { Value = (object?)usuario.FechaNacimiento ?? DBNull.Value },
                new SqlParameter("@ClaveEmpleadoNomina", (object?)usuario.ClaveEmpleadoNomina ?? DBNull.Value),
                new SqlParameter("@DepartamentoID", (object?)usuario.DepartamentoID ?? DBNull.Value)
            };

            await _context.Database.ExecuteSqlRawAsync(
                @"EXEC dbo.sp_ActualizarUsuario 
                    @UsuarioID,
                    @Nombre,
                    @ApellidoPaterno,
                    @Correo,
                    @RolID,
                    @Activo,
                    @ApellidoMaterno,
                    @Telefono,
                    @SubMenuIDs,
                    @FechaIngreso,
                    @JefeInmediatoPersonaID,
                    @NumeroEmpleado,
                    @Puesto,
                    @FechaNacimiento,
                    @ClaveEmpleadoNomina,
                    @DepartamentoID",
                parameters
            );
        }

        public async Task DarDeBajaAsync(int usuarioId)
        {
            var parameter = new SqlParameter("@UsuarioID", usuarioId);

            await _context.Database.ExecuteSqlRawAsync(
                "EXEC dbo.sp_DarDeBajaUsuario @UsuarioID",
                parameter
            );
        }

        public async Task<IEnumerable<AuditoriaUsuario>> ObtenerHistorialAsync(int usuarioId)
        {
            var parameter = new SqlParameter("@UsuarioID", usuarioId);

            var historial = await _context.AuditoriasUsuarios
                .FromSqlRaw("EXEC dbo.sp_ObtenerAuditoriaDeUsuario @UsuarioID", parameter)
                .ToListAsync();

            return historial ?? new List<AuditoriaUsuario>();
        }

        public async Task<bool> TienePermisoAsync(int usuarioId, string nombreAccion)
        {
            var usuarioIdParam = new SqlParameter("@UsuarioID", usuarioId);
            var accionParam = new SqlParameter("@NombreAccion", nombreAccion);

            var resultParam = new SqlParameter
            {
                ParameterName = "@Result",
                SqlDbType = SqlDbType.Bit,
                Direction = ParameterDirection.Output
            };

            await _context.Database.ExecuteSqlRawAsync(
                "SET @Result = dbo.FN_UsuarioTienePermiso(@UsuarioID, @NombreAccion)",
                resultParam,
                usuarioIdParam,
                accionParam
            );

            return resultParam.Value != DBNull.Value && Convert.ToBoolean(resultParam.Value);
        }

     public async Task<List<MenuViewModel>> ObtenerMenusConSubMenusAsync()
{
    return await _context.Menus
        .Include(m => m.SubMenus)
        .OrderBy(m => m.Nombre)
        .Select(m => new MenuViewModel
        {
            MenuID = m.MenuID,
            Nombre = m.Nombre,
            SubMenus = m.SubMenus
                .OrderBy(sm => sm.Nombre)
                .Select(sm => new SubMenuViewModel
                {
                    SubMenuID = sm.SubMenuID,
                    Nombre = sm.Nombre
                })
                .ToList()
        })
        .ToListAsync();
}

        public async Task<bool> VerificarPermisoAsync(int usuarioId, int subMenuId)
        {
            var conn = _context.Database.GetDbConnection();

            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync();

            using var cmd = conn.CreateCommand();

            cmd.CommandText = @"
                SELECT TOP 1 fe.TienePermiso
                FROM dbo.fn_PermisosEfectivosUsuario(@UsuarioID) AS fe
                WHERE fe.SubMenuID = @SubMenuID;";

            cmd.CommandType = CommandType.Text;

            var pU = cmd.CreateParameter();
            pU.ParameterName = "@UsuarioID";
            pU.Value = usuarioId;
            cmd.Parameters.Add(pU);

            var pS = cmd.CreateParameter();
            pS.ParameterName = "@SubMenuID";
            pS.Value = subMenuId;
            cmd.Parameters.Add(pS);

            var resultObj = await cmd.ExecuteScalarAsync();

            if (resultObj == null || resultObj == DBNull.Value)
                return false;

            return Convert.ToBoolean(resultObj);
        }

        public async Task<List<OverrideItemDto>> ListarOverridesAsync(int usuarioId, int? empresaId)
        {
            var result = new List<OverrideItemDto>();

            var conn = _context.Database.GetDbConnection();

            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync();

            using var cmd = conn.CreateCommand();

            cmd.CommandText = "dbo.sp_Overrides_ListarUsuario";
            cmd.CommandType = CommandType.StoredProcedure;

            var pU = cmd.CreateParameter();
            pU.ParameterName = "@UsuarioID";
            pU.Value = usuarioId;
            cmd.Parameters.Add(pU);

            /*
                ERP de una sola empresa:
                Se mantiene @EmpresaID = NULL solo por compatibilidad con el SP existente.
                Si ya eliminaste @EmpresaID del SP, borra este parámetro.
            */
            var pE = cmd.CreateParameter();
            pE.ParameterName = "@EmpresaID";
            pE.Value = DBNull.Value;
            cmd.Parameters.Add(pE);

            using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                result.Add(new OverrideItemDto
                {
                    SubMenuID = Convert.ToInt32(rd["SubMenuID"]),
                    Nombre = rd["Nombre"]?.ToString() ?? "",
                    Estado = Convert.ToInt32(rd["Estado"]),
                    PermisoEfectivo = Convert.ToBoolean(rd["PermisoEfectivo"])
                });
            }

            return result;
        }

        public async Task GuardarOverridesAsync(int usuarioId, int? empresaId, IEnumerable<OverrideItemDto> items)
        {
            var conn = _context.Database.GetDbConnection();

            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync();

            await using var tx = await conn.BeginTransactionAsync();

            try
            {
                foreach (var it in items ?? Enumerable.Empty<OverrideItemDto>())
                {
                    await using var cmd = conn.CreateCommand();

                    cmd.Transaction = tx;
                    cmd.CommandText = "dbo.sp_Override_Upsert";
                    cmd.CommandType = CommandType.StoredProcedure;

                    var pU = cmd.CreateParameter();
                    pU.ParameterName = "@UsuarioID";
                    pU.Value = usuarioId;
                    cmd.Parameters.Add(pU);

                    /*
                        ERP de una sola empresa:
                        Se mantiene @EmpresaID = NULL solo por compatibilidad con el SP existente.
                        Si ya eliminaste @EmpresaID del SP, borra este parámetro.
                    */
                    var pE = cmd.CreateParameter();
                    pE.ParameterName = "@EmpresaID";
                    pE.Value = DBNull.Value;
                    cmd.Parameters.Add(pE);

                    var pS = cmd.CreateParameter();
                    pS.ParameterName = "@SubMenuID";
                    pS.Value = it.SubMenuID;
                    cmd.Parameters.Add(pS);

                    var pT = cmd.CreateParameter();
                    pT.ParameterName = "@Estado";
                    pT.Value = it.Estado;
                    cmd.Parameters.Add(pT);

                    await cmd.ExecuteNonQueryAsync();
                }

                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        public async Task<bool> VerificarPermisoParaMenuAsync(int usuarioId, int menuId)
        {
            var subMenuIdsDelMenu = await _context.SubMenus
                .Where(sm => sm.MenuID == menuId && sm.Activo)
                .Select(sm => sm.SubMenuID)
                .ToListAsync();

            if (!subMenuIdsDelMenu.Any())
                return false;

            foreach (var subMenuId in subMenuIdsDelMenu)
            {
                if (await VerificarPermisoAsync(usuarioId, subMenuId))
                    return true;
            }

            return false;
        }

        public async Task<bool> VerificarPermisoParaMenuAsync(int usuarioId, int? empresaId, int menuId)
        {
            return await VerificarPermisoParaMenuAsync(usuarioId, menuId);
        }

        public async Task<string> GetMenuHomeUrlAsync(int menuId)
        {
            var url = await _context.SubMenus
                .Where(sm =>
                    sm.MenuID == menuId &&
                    sm.Activo &&
                    sm.Nombre.StartsWith("Ver"))
                .OrderBy(sm => sm.SubMenuID)
                .Select(sm => sm.UrlEnlace)
                .FirstOrDefaultAsync();

            if (string.IsNullOrWhiteSpace(url))
            {
                url = await _context.SubMenus
                    .Where(sm =>
                        sm.MenuID == menuId &&
                        sm.Activo &&
                        sm.UrlEnlace != null &&
                        (
                            sm.UrlEnlace.EndsWith("/Index") ||
                            sm.UrlEnlace.EndsWith("/Entrada")
                        ))
                    .OrderBy(sm => sm.SubMenuID)
                    .Select(sm => sm.UrlEnlace)
                    .FirstOrDefaultAsync();
            }

            return string.IsNullOrWhiteSpace(url) ? "/" : url;
        }
    }
}