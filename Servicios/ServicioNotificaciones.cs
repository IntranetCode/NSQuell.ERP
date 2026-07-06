using ERP.NSQuell.Models;
using ERP.NSQuell.Models.Opciones;
using Microsoft.Extensions.Options;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using System.Data;
using System.Data.SqlClient;

namespace ERP.NSQuell.Servicios
{
    public class ServicioNotificaciones
    {
        private readonly ApplicationDbContext _context;
        private readonly CorreoOpciones _correoOpt;
        private readonly string _cs;
        private readonly IHostEnvironment _env;
        private readonly ILogger<ServicioNotificaciones> _logger; // ✅ AGREGADO

        public ServicioNotificaciones(
            ApplicationDbContext context,
            IOptions<CorreoOpciones> correoOpt,
            IConfiguration cfg,
            IHostEnvironment env,
            ILogger<ServicioNotificaciones> logger) // ✅ AGREGADO
        {
            _context = context;
            _correoOpt = correoOpt.Value;
            _correoOpt.Usuario = cfg["CorreoNotificaciones:Usuario"] ?? _correoOpt.Usuario;
            _correoOpt.Contrasena = cfg["CorreoNotificaciones:Contrasena"] ?? _correoOpt.Contrasena;
            _cs = cfg.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Falta ConnectionStrings:DefaultConnection");
            _env = env;
            _logger = logger; // ✅ AGREGADO

            // ✅ LOG INICIAL (una sola vez al construir el servicio)
            _logger.LogInformation("ServicioNotificaciones creado. Habilitado={Hab}, SoloPruebas={SP}, Host={Host}",
                _correoOpt.Habilitado, _correoOpt.SoloPruebas, _correoOpt.SmtpHost);
        }

        private SecureSocketOptions ToSecureOption(string? s) => s?.ToLower() switch
        {
            "starttls" => SecureSocketOptions.StartTls,
            "sslonconnect" => SecureSocketOptions.SslOnConnect,
            "auto" => SecureSocketOptions.Auto,
            _ => SecureSocketOptions.StartTls
        };

        // ✅ RESULTADO DE ENVÍO (en lugar de void)
        public class ResultadoEnvio
        {
            public int Encontrados { get; set; }
            public int FiltradosPorCandados { get; set; }
            public int Enviados { get; set; }
            public int Errores { get; set; }
            public List<string> Mensajes { get; set; } = new();
        }

        private async Task EnviarCorreoAsync(string para, string asunto, string html)
        {
            if (string.IsNullOrWhiteSpace(_correoOpt.SmtpHost) || string.IsNullOrWhiteSpace(_correoOpt.Remitente))
            {
                _logger.LogWarning("SmtpHost o Remitente vacíos. Correo NO enviado.");
                return;
            }

            if (!PuedeEnviarA(para))
            {
                _logger.LogWarning("Bloqueado por candados: {Para} (SoloPruebas={SP}, ListaBlanca={LB})",
                    para, _correoOpt.SoloPruebas, _correoOpt.ListaBlanca);
                return;
            }

            _logger.LogInformation("Enviando correo: Para={Para}, Host={Host}:{Port}, Security={Sec}",
                para, _correoOpt.SmtpHost, _correoOpt.SmtpPort, _correoOpt.Security);

            var msg = new MimeMessage();
            msg.From.Add(new MailboxAddress(_correoOpt.NombreRemitente ?? "", _correoOpt.Remitente));
            msg.To.Add(MailboxAddress.Parse(para));
            msg.Subject = asunto;
            msg.Body = new BodyBuilder { HtmlBody = html }.ToMessageBody();

            using var smtp = new SmtpClient
            {
                Timeout = 20000,
                ServerCertificateValidationCallback = (s, c, h, e) => true
            };

            try
            {
                smtp.LocalDomain = "mail.nutriservicios.com.mx";


                await smtp.ConnectAsync(_correoOpt.SmtpHost, _correoOpt.SmtpPort, ToSecureOption(_correoOpt.Security));
                smtp.AuthenticationMechanisms.Remove("XOAUTH2");

                if (!string.IsNullOrEmpty(_correoOpt.Usuario))
                    await smtp.AuthenticateAsync(_correoOpt.Usuario, _correoOpt.Contrasena);

                await smtp.SendAsync(msg);
                _logger.LogInformation("✅ Correo enviado exitosamente a {Para}", para);
                await smtp.DisconnectAsync(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error enviando correo a {Para}", para);
             //   throw;
             return;
            }
        }

        // ✅ VERSIÓN CON LOGGING Y RESULTADO
        public async Task<ResultadoEnvio> EnviarABccPersonasAsync(IEnumerable<int> personaIds, string asunto, string html)
        {
            var resultado = new ResultadoEnvio();
            var ids = (personaIds ?? Enumerable.Empty<int>()).Distinct().ToList();

            _logger.LogInformation("EnviarABccPersonasAsync: {Count} PersonaIDs recibidos", ids.Count);
            resultado.Encontrados = ids.Count;

            if (ids.Count == 0)
            {
                resultado.Mensajes.Add("No se recibieron PersonaIDs");
                return resultado;
            }

            var correos = await GetCorreosPersonasAsync(ids);
            _logger.LogInformation("GetCorreosPersonasAsync retornó {Count} correos: {Correos}",
                correos.Count, string.Join(", ", correos));

            if (correos.Count == 0)
            {
                resultado.Mensajes.Add("Ningún PersonaID tiene correo válido");
                return resultado;
            }

            // ✅ APLICAR CANDADOS
            if (!_correoOpt.Habilitado)
            {
                _logger.LogWarning("Correo DESHABILITADO (Habilitado=false). No se envía nada.");
                resultado.FiltradosPorCandados = correos.Count;
                resultado.Mensajes.Add("Correo deshabilitado globalmente");
                return resultado;
            }

            var correosFiltrados = correos.Where(PuedeEnviarA).ToList();
            resultado.FiltradosPorCandados = correos.Count - correosFiltrados.Count;

            _logger.LogInformation("Candados aplicados: {Original} correos → {Filtrados} permitidos (Bloqueados={Bloq})",
                correos.Count, correosFiltrados.Count, resultado.FiltradosPorCandados);

            if (correosFiltrados.Count == 0)
            {
                resultado.Mensajes.Add($"Todos los correos bloqueados por candados (SoloPruebas={_correoOpt.SoloPruebas}, ListaBlanca={_correoOpt.ListaBlanca})");
                return resultado;
            }

            // ✅ LIMITAR POR MaxDestinatariosEnPrueba
            if (_correoOpt.SoloPruebas && _correoOpt.MaxDestinatariosEnPrueba > 0 && correosFiltrados.Count > _correoOpt.MaxDestinatariosEnPrueba)
            {
                _logger.LogWarning("SoloPruebas activo: limitando de {Total} a {Max} correos",
                    correosFiltrados.Count, _correoOpt.MaxDestinatariosEnPrueba);
                correosFiltrados = correosFiltrados.Take(_correoOpt.MaxDestinatariosEnPrueba).ToList();
            }

            // ✅ ENVÍO BCC
            try
            {
                var msg = new MimeMessage();
                msg.From.Add(new MailboxAddress(_correoOpt.NombreRemitente ?? "", _correoOpt.Remitente));
                msg.To.Add(MailboxAddress.Parse(_correoOpt.Remitente)); // dummy To

                foreach (var c in correosFiltrados.Distinct())
                    msg.Bcc.Add(MailboxAddress.Parse(c));

                msg.Subject = asunto;
                msg.Body = new BodyBuilder { HtmlBody = html }.ToMessageBody();

                using var smtp = new SmtpClient
                {
                    Timeout = 20000,
                    ServerCertificateValidationCallback = (s, c, h, e) => true
                };

                smtp.LocalDomain = "mail.nutriservicios.com.mx";


                await smtp.ConnectAsync(_correoOpt.SmtpHost, _correoOpt.SmtpPort, ToSecureOption(_correoOpt.Security));
                smtp.AuthenticationMechanisms.Remove("XOAUTH2");

                if (!string.IsNullOrEmpty(_correoOpt.Usuario))
                    await smtp.AuthenticateAsync(_correoOpt.Usuario, _correoOpt.Contrasena);

                await smtp.SendAsync(msg);
                resultado.Enviados = correosFiltrados.Count;
                _logger.LogInformation("✅ Correo BCC enviado a {Count} destinatarios", resultado.Enviados);

                await smtp.DisconnectAsync(true);
            }
            catch (Exception ex)
            {
                resultado.Errores++;
                resultado.Mensajes.Add($"Error en SMTP: {ex.Message}");
                _logger.LogError(ex, "❌ Error enviando BCC");
            }

            return resultado;
        }

        // ✅ HELPER MEJORADO
        public async Task<ResultadoEnvio> EnviarCursosAUsuariosAsync(IEnumerable<int> usuarioIds, string asunto, string html, int batchSize = 40)
        {
            var resultado = new ResultadoEnvio();
            var idsUsuarios = (usuarioIds ?? Enumerable.Empty<int>()).Distinct().ToList();

            _logger.LogInformation("EnviarCursosAUsuariosAsync: {Count} UsuarioIDs recibidos", idsUsuarios.Count);
            resultado.Encontrados = idsUsuarios.Count;

            if (idsUsuarios.Count == 0)
            {
                resultado.Mensajes.Add("No se recibieron UsuarioIDs");
                return resultado;
            }

            // 🔁 Mapea UsuarioID → PersonaID
            var personaIds = await GetPersonaIdsPorUsuariosAsync(idsUsuarios);
            _logger.LogInformation("GetPersonaIdsPorUsuariosAsync: {UsuariosIn} usuarios → {PersonasOut} personas",
                idsUsuarios.Count, personaIds.Count);

            if (personaIds.Count == 0)
            {
                resultado.Mensajes.Add("Ningún UsuarioID tiene PersonaID con correo válido");
                return resultado;
            }

            // 📨 Caso 1: una sola persona → To directo
            if (personaIds.Count == 1)
            {
                try
                {
                    await EnviarAPersonaAsync(personaIds[0], asunto, html);
                    resultado.Enviados = 1;
                }
                catch (Exception ex)
                {
                    resultado.Errores++;
                    resultado.Mensajes.Add($"Error enviando a PersonaID={personaIds[0]}: {ex.Message}");
                }
                return resultado;
            }

            // 📨 Caso N: BCC por lotes
            foreach (var lote in personaIds.Chunk(batchSize))
            {
                var resLote = await EnviarABccPersonasAsync(lote, asunto, html);
                resultado.Enviados += resLote.Enviados;
                resultado.Errores += resLote.Errores;
                resultado.FiltradosPorCandados += resLote.FiltradosPorCandados;
                resultado.Mensajes.AddRange(resLote.Mensajes);
            }

            return resultado;
        }

        private bool PuedeEnviarA(string correo)
        {
            if (!_correoOpt.Habilitado) return false;

            if (_correoOpt.SoloPruebas)
            {
                var whitelist = (_correoOpt.ListaBlanca ?? "")
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(x => x.ToLowerInvariant())
                    .ToHashSet();

                if (whitelist.Count > 0)
                    return whitelist.Contains(correo.ToLowerInvariant());

                // Si no hay whitelist, bloquea TODO en pruebas (ajusta según tu lógica)
                return false;
            }

            return true; // producción real
        }

        // ✅ RESTO DE MÉTODOS (sin cambios, solo agrega logging donde veas necesario)
        public async Task<string?> GetCorreoPersonaAsync(int personaId)
        {
            const string sql = @"
SELECT LTRIM(RTRIM(Correo))
FROM Persona
WHERE PersonaID = @PersonaID
  AND Correo IS NOT NULL
  AND LTRIM(RTRIM(Correo)) <> ''";

            await using var conn = new SqlConnection(_cs);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add(new SqlParameter("@PersonaID", SqlDbType.Int) { Value = personaId });

            var result = await cmd.ExecuteScalarAsync();
            var correo = result as string;

            return EmailValido(correo) ? correo : null;
        }

        public async Task<List<string>> GetCorreosPersonasAsync(IEnumerable<int> personaIds)
        {
            var ids = (personaIds ?? Enumerable.Empty<int>()).Distinct().ToList();
            if (ids.Count == 0) return new();

            var paramNames = ids.Select((id, i) => "@p" + i).ToArray();
            var sql = $@"
SELECT DISTINCT LTRIM(RTRIM(Correo)) AS Correo
FROM Persona
WHERE PersonaID IN ({string.Join(",", paramNames)})
  AND Correo IS NOT NULL
  AND LTRIM(RTRIM(Correo)) <> ''";

            await using var conn = new SqlConnection(_cs);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            for (int i = 0; i < ids.Count; i++)
                cmd.Parameters.Add(new SqlParameter(paramNames[i], SqlDbType.Int) { Value = ids[i] });

            var correos = new List<string>();
            await using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    var c = reader.GetString(0);
                    if (EmailValido(c))
                        correos.Add(c);
                }
            }

            return correos.Select(c => c.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        public async Task<List<int>> GetPersonaIdsPorEmpresasAsync(IEnumerable<int> empresaIds)
        {
            var ids = (empresaIds ?? Enumerable.Empty<int>()).Distinct().ToList();
            if (ids.Count == 0) return new();

            var paramNames = ids.Select((id, i) => "@e" + i).ToArray();
            var sql = $@"
SELECT DISTINCT p.PersonaID
FROM Persona p
WHERE p.EmpresaID IN ({string.Join(",", paramNames)})
  AND p.Correo IS NOT NULL
  AND LTRIM(RTRIM(p.Correo)) <> ''";

            await using var conn = new SqlConnection(_cs);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            for (int i = 0; i < ids.Count; i++)
                cmd.Parameters.Add(new SqlParameter(paramNames[i], SqlDbType.Int) { Value = ids[i] });

            var result = new List<int>();
            await using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                    result.Add(reader.GetInt32(0));
            }
            return result;
        }

        public async Task<List<int>> GetPersonaIdsPorUsuariosAsync(IEnumerable<int> usuarioIds)
        {
            var ids = (usuarioIds ?? Enumerable.Empty<int>()).Distinct().ToList();
            if (ids.Count == 0) return new();

            var paramNames = ids.Select((id, i) => "@u" + i).ToArray();
            var sql = $@"
SELECT DISTINCT p.PersonaID
FROM Usuarios u
JOIN Persona p ON p.PersonaID = u.PersonaID
WHERE u.UsuarioID IN ({string.Join(",", paramNames)})
  AND p.Correo IS NOT NULL
  AND LTRIM(RTRIM(p.Correo)) <> ''";

            _logger.LogInformation("GetPersonaIdsPorUsuariosAsync ejecutando SQL con {Count} UsuarioIDs", ids.Count);

            var result = new List<int>();

            await using var conn = new SqlConnection(_cs);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            for (int i = 0; i < ids.Count; i++)
                cmd.Parameters.Add(new SqlParameter(paramNames[i], SqlDbType.Int) { Value = ids[i] });

            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
                result.Add(rd.GetInt32(0));

            _logger.LogInformation("GetPersonaIdsPorUsuariosAsync retornó {Count} PersonaIDs con correo válido", result.Count);

            return result;
        }

        public async Task EnviarAPersonaAsync(int personaId, string asunto, string html)
        {
            var correo = await GetCorreoPersonaAsync(personaId);
            if (!EmailValido(correo)) return;
            await EnviarCorreoAsync(correo!, asunto, html);
        }

        private bool EmailValido(string? email)
        {
            if (string.IsNullOrWhiteSpace(email)) return false;
            try { return MimeKit.MailboxAddress.TryParse(email, out _); }
            catch { return false; }
        }

        public Task EnviarCorreoAsync(string para) =>
            EnviarCorreoAsync(para, "🧪 Prueba de notificación", "<h2>Prueba OK</h2><p>Esto salió desde el sistema.</p>");

        // ✅ MÉTODOS DE NOTIFICACIÓN IN-APP (sin cambios)
        public async Task EmitirCursoAsignado(int idCurso, string nombreCurso, int usuarioId)
        {
            var notif = new Notificacion
            {
                Tipo = "CursoAsignado",
                Titulo = "Nuevo Curso Asignado",
                Mensaje = $"Se te ha asignado un nuevo curso con nombre: {nombreCurso}",
                IdOrigen = idCurso,
                TablaOrigen = "Cursos",
                UsuarioId = usuarioId,
                FechaExpiracion = DateTime.UtcNow.AddDays(30),
            };
            _context.Notificaciones.Add(notif);
            await _context.SaveChangesAsync();
        }

        public async Task EmitirWebinarAsignado(int idWebinar, string nombreWebinar, DateTime fecha, int usuarioId)
        {
            var notif = new Notificacion
            {
                Tipo = "WebinarAsignado",
                Titulo = "Nuevo Webinar Asignado",
                Mensaje = $"Se te ha asignado un nuevo webinar con nombre: {nombreWebinar}-{fecha:dd/MM/yyyy HH:mm}",
                IdOrigen = idWebinar,
                TablaOrigen = "Webinars",
                UsuarioId = usuarioId,
                FechaExpiracion = DateTime.UtcNow.AddDays(30),
            };
            _context.Notificaciones.Add(notif);
            await _context.SaveChangesAsync();
        }

        public async Task EmitirComunicadoEmpresa(int idComunicado, string tituloComunicado, int empresaId)
        {
            var notif = new Notificacion
            {
                Tipo = "Comunicado_Nuevo",
                Titulo = "Nuevo Comunicado",
                Mensaje = tituloComunicado,
                IdOrigen = idComunicado,
                TablaOrigen = "Comunicados",
                EmpresaId = empresaId,
                FechaExpiracion = DateTime.UtcNow.AddDays(30),
            };
            _context.Notificaciones.Add(notif);
            await _context.SaveChangesAsync();
        }

        public async Task EmitirComunicadoUsuarios(int idComunicado, string tituloComunicado, IEnumerable<int> usuarios)
        {
            foreach (var uid in usuarios)
            {
                _context.Notificaciones.Add(new Notificacion
                {
                    Tipo = "Comunicado_Nuevo",
                    Titulo = "Nuevo Comunicado",
                    Mensaje = tituloComunicado,
                    IdOrigen = idComunicado,
                    TablaOrigen = "Comunicados",
                    UsuarioId = uid,
                    FechaExpiracion = DateTime.UtcNow.AddDays(30),
                });
            }
            await _context.SaveChangesAsync();
        }

        public async Task EmitirGlobal(string tipo, string titulo, string mensaje, int idOrigen, string tablaOrigen)
        {
            var n = new Notificacion
            {
                Tipo = tipo,
                Titulo = titulo,
                Mensaje = mensaje,
                IdOrigen = idOrigen,
                TablaOrigen = tablaOrigen,
                UsuarioId = null,
                EmpresaId = null,
                FechaExpiracion = DateTime.UtcNow.AddDays(30),
                FechaCreacion = DateTime.UtcNow
            };
            _context.Notificaciones.Add(n);
            await _context.SaveChangesAsync();
        }

        public async Task EmitirParaEmpresas(string tipo, string titulo, string mensaje, int idOrigen, string tablaOrigen, IEnumerable<int> empresas)
        {
            var n = new Notificacion
            {
                Tipo = tipo,
                Titulo = titulo,
                Mensaje = mensaje,
                IdOrigen = idOrigen,
                TablaOrigen = tablaOrigen,
                UsuarioId = null,
                EmpresaId = null,
                FechaExpiracion = DateTime.UtcNow.AddDays(30),
                FechaCreacion = DateTime.UtcNow
            };
            _context.Notificaciones.Add(n);
            await _context.SaveChangesAsync();

            foreach (var eid in empresas.Distinct())
            {
                _context.NotificacionEmpresas.Add(new NotificacionEmpresas
                {
                    NotificacionId = n.Id,
                    EmpresaId = eid
                });
            }
            await _context.SaveChangesAsync();
        }

        public async Task EmitirUsuario(string tipo, string titulo, string mensaje, int idOrigen, string tablaOrigen, int usuarioId)
        {
            _context.Notificaciones.Add(new Notificacion
            {
                Tipo = tipo,
                Titulo = titulo,
                Mensaje = mensaje,
                IdOrigen = idOrigen,
                TablaOrigen = tablaOrigen,
                UsuarioId = usuarioId,
                EmpresaId = null,
                FechaExpiracion = DateTime.UtcNow.AddDays(30),
                FechaCreacion = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();
        }


        public async Task<List<int>> GetTodosPersonaIdsConCorreoAsync()
        {
            const string sql = @"
        SELECT DISTINCT p.PersonaID
        FROM Persona p
        INNER JOIN Usuarios u ON u.PersonaID = p.PersonaID
        WHERE p.Correo IS NOT NULL
          AND LTRIM(RTRIM(p.Correo)) <> ''
          AND u.Activo = 1;
    ";

            _logger.LogInformation("GetTodosPersonaIdsConCorreoAsync: obteniendo todos los usuarios activos");

            var result = new List<int>();

            await using var conn = new SqlConnection(_cs);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);

            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
                result.Add(rd.GetInt32(0));

            _logger.LogInformation("GetTodosPersonaIdsConCorreoAsync: {Count} usuarios activos con correo encontrados", result.Count);

            return result;
        }

      

    }
}