using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ERP.NSQuell.Areas.AdminUsuarios.DTOs;
using ERP.NSQuell.Areas.AdminUsuarios.Interfaces;
using ERP.NSQuell.Models;
using ERP.NSQuell.Models.ModelUsuarios;
using ERP.NSQuell.Seguridad;
using ERP.NSQuell.Servicios;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using ERP.NSQuell.Models.ViewModels;

namespace ERP.NSQuell.Controllers
{
    public class UsuariosController : Controller
    {
        private readonly IUsuarioService _usuarioService;
        private readonly ApplicationDbContext _context;
        private readonly ServicioNotificaciones _servicioNotificaciones;

        public UsuariosController(
            IUsuarioService usuarioService,
            ApplicationDbContext context,
            ServicioNotificaciones servicioNotificaciones)
        {
            _usuarioService = usuarioService;
            _context = context;
            _servicioNotificaciones = servicioNotificaciones;
        }


        [AutorizarAccion("Ver Usuarios", "Ver")]
        public async Task<IActionResult> Index(bool? activos, string? filtroCampo, string? busqueda)
        {
            ViewData["BusquedaActual"] = busqueda;
            ViewData["FiltroCampoActual"] = filtroCampo ?? "Todos";

            List<V_InformacionUsuarioCompleta> usuarios;

            if (activos.HasValue)
            {
                ViewData["Title"] = activos.Value ? "Usuarios activos" : "Usuarios inactivos";

                usuarios = (await _usuarioService.ObtenerTodosAsync(
                        activos.Value,
                        filtroCampo,
                        busqueda))
                    .ToList();
            }
            else
            {
                ViewData["Title"] = "Todos los usuarios";

                var usuariosActivos = await _usuarioService.ObtenerTodosAsync(
                    true,
                    filtroCampo,
                    busqueda);

                var usuariosInactivos = await _usuarioService.ObtenerTodosAsync(
                    false,
                    filtroCampo,
                    busqueda);

                usuarios = usuariosActivos
                    .Concat(usuariosInactivos)
                    .GroupBy(u => u.UsuarioID)
                    .Select(g => g.First())
                    .OrderBy(u => u.Nombre)
                    .ThenBy(u => u.ApellidoPaterno)
                    .ThenBy(u => u.ApellidoMaterno)
                    .ToList();
            }

            return View(usuarios);
        }


        // GET: /Usuarios/Accesos
        // Usa el mismo Index.cshtml, pero activa el modo para mostrar el botón "Enviar accesos".
        // No tiene una validación extra: quien tenga permiso de Ver Usuarios puede usar esta vista.
        [HttpGet]
        [AutorizarAccion("Ver Usuarios", "Ver")]
        public async Task<IActionResult> Accesos(string? filtroCampo, string? busqueda)
        {
            ViewData["Title"] = "Enviar accesos por correo";
            ViewData["BusquedaActual"] = busqueda;
            ViewData["FiltroCampoActual"] = filtroCampo ?? "Todos";
            ViewData["ModoAccesos"] = true;

            var usuariosActivos = await _usuarioService.ObtenerTodosAsync(true, filtroCampo, busqueda);
            var usuariosInactivos = await _usuarioService.ObtenerTodosAsync(false, filtroCampo, busqueda);

            var usuarios = usuariosActivos
                .Concat(usuariosInactivos)
                .GroupBy(u => u.UsuarioID)
                .Select(g => g.First())
                .OrderBy(u => u.Nombre)
                .ThenBy(u => u.ApellidoPaterno)
                .ThenBy(u => u.ApellidoMaterno)
                .ToList();

            return View("Index", usuarios);
        }


        // GET: /Usuarios/Maquinaria
        // GET: /Usuarios/Maquinaria/Index
        [HttpGet]
        [Route("Usuarios/Maquinaria")]
        [Route("Usuarios/Maquinaria/Index")]
        [AutorizarAccion("Ver Usuarios", "Ver")]
        public IActionResult Maquinaria()
        {
            ViewData["Title"] = "Altas de Maquinaria";

            return View("~/Views/Usuarios/Maquinaria/Index.cshtml");
        }

        [HttpGet]
        [Route("Usuarios/Maquinaria/Crear")]
        [AutorizarAccion("Ver Usuarios", "Ver")]
        public IActionResult CrearMaquinaria()
        {
            ViewData["Title"] = "Agregar máquina";

            var model = new MaquinariaFormViewModel
            {
                Activo = true,
                Area = "Inyección",
                EstadoOperativo = "Operativa"
            };

            return View("~/Views/Usuarios/Maquinaria/Crear.cshtml", model);
        }

        // POST: /Usuarios/Maquinaria/Crear
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Route("Usuarios/Maquinaria/Crear")]
        [AutorizarAccion("Ver Usuarios", "Ver")]
        public async Task<IActionResult> CrearMaquinaria(MaquinariaFormViewModel model)
        {
            if (!ModelState.IsValid)
            {
                ViewData["Title"] = "Agregar máquina";
                return View("~/Views/Usuarios/Maquinaria/Crear.cshtml", model);
            }

            var codigo = model.Codigo.Trim();

            var existeCodigo = await _context.ERPMaquinas
                .AsNoTracking()
                .AnyAsync(x => x.Codigo != null && x.Codigo.Trim() == codigo);

            if (existeCodigo)
            {
                ModelState.AddModelError(nameof(model.Codigo), "Ya existe una máquina registrada con este código.");
                ViewData["Title"] = "Agregar máquina";
                return View("~/Views/Usuarios/Maquinaria/Crear.cshtml", model);
            }

            var maquina = new ERPMaquina
            {
                Codigo = codigo,
                Nombre = model.Nombre.Trim(),
                Area = model.Area.Trim(),
                EstadoOperativo = model.EstadoOperativo.Trim(),
                Descripcion = string.IsNullOrWhiteSpace(model.Descripcion) ? null : model.Descripcion.Trim(),
                Activo = model.Activo,
                FechaCreacion = DateTime.Now,
                FechaModificacion = null
            };

            _context.ERPMaquinas.Add(maquina);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "La máquina fue registrada correctamente.";
            return RedirectToAction(nameof(Maquinaria));
        }

        // GET: /Usuarios/Maquinaria/Editar/5
        [HttpGet]
        [Route("Usuarios/Maquinaria/Editar/{id:int}")]
        [AutorizarAccion("Ver Usuarios", "Ver")]
        public async Task<IActionResult> EditarMaquinaria(int id)
        {
            var maquina = await _context.ERPMaquinas
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.MaquinaID == id);

            if (maquina == null)
                return NotFound();

            var model = new MaquinariaFormViewModel
            {
                MaquinaID = maquina.MaquinaID,
                Codigo = maquina.Codigo ?? string.Empty,
                Nombre = maquina.Nombre ?? string.Empty,
                Area = maquina.Area ?? string.Empty,
                EstadoOperativo = maquina.EstadoOperativo ?? "Operativa",
                Descripcion = maquina.Descripcion,
                Activo = maquina.Activo,
                FechaCreacion = maquina.FechaCreacion,
                FechaModificacion = maquina.FechaModificacion
            };

            ViewData["Title"] = "Editar máquina";
            return View("~/Views/Usuarios/Maquinaria/Editar.cshtml", model);
        }

        // POST: /Usuarios/Maquinaria/Editar/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Route("Usuarios/Maquinaria/Editar/{id:int}")]
        [AutorizarAccion("Ver Usuarios", "Ver")]
        public async Task<IActionResult> EditarMaquinaria(int id, MaquinariaFormViewModel model)
        {
            if (!model.MaquinaID.HasValue || id != model.MaquinaID.Value)
                return BadRequest();

            if (!ModelState.IsValid)
            {
                ViewData["Title"] = "Editar máquina";
                return View("~/Views/Usuarios/Maquinaria/Editar.cshtml", model);
            }

            var maquina = await _context.ERPMaquinas
                .FirstOrDefaultAsync(x => x.MaquinaID == id);

            if (maquina == null)
                return NotFound();

            var codigo = model.Codigo.Trim();

            var existeCodigo = await _context.ERPMaquinas
                .AsNoTracking()
                .AnyAsync(x => x.MaquinaID != id && x.Codigo != null && x.Codigo.Trim() == codigo);

            if (existeCodigo)
            {
                ModelState.AddModelError(nameof(model.Codigo), "Ya existe otra máquina registrada con este código.");
                ViewData["Title"] = "Editar máquina";
                return View("~/Views/Usuarios/Maquinaria/Editar.cshtml", model);
            }

            maquina.Codigo = codigo;
            maquina.Nombre = model.Nombre.Trim();
            maquina.Area = model.Area.Trim();
            maquina.EstadoOperativo = model.EstadoOperativo.Trim();
            maquina.Descripcion = string.IsNullOrWhiteSpace(model.Descripcion) ? null : model.Descripcion.Trim();
            maquina.Activo = model.Activo;
            maquina.FechaModificacion = DateTime.Now;

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "La máquina fue actualizada correctamente.";
            return RedirectToAction(nameof(Maquinaria));
        }

        // POST: /Usuarios/Maquinaria/CambiarEstado/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Route("Usuarios/Maquinaria/CambiarEstado/{id:int}")]
        [AutorizarAccion("Ver Usuarios", "Ver")]
        public async Task<IActionResult> CambiarEstadoMaquinaria(int id, bool activo)
        {
            var maquina = await _context.ERPMaquinas.FirstOrDefaultAsync(x => x.MaquinaID == id);

            if (maquina == null)
                return NotFound();

            maquina.Activo = activo;
            maquina.FechaModificacion = DateTime.Now;

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = activo
                ? "La máquina fue activada correctamente."
                : "La máquina fue inactivada correctamente.";

            return RedirectToAction(nameof(Maquinaria));
        }

        // GET: /Usuarios/Operadores
        // GET: /Usuarios/Operadores/Index
        [HttpGet]
        [Route("Usuarios/Operadores")]
        [Route("Usuarios/Operadores/Index")]
        [AutorizarAccion("Ver Usuarios", "Ver")]
        public IActionResult Operadores()
        {
            ViewData["Title"] = "Altas de Operadores";

            return View("~/Views/Usuarios/Operadores/Index.cshtml");
        }

        // POST: /Usuarios/EnviarAccesosPorCorreo
        // La vista solo envía el UsuarioID. La contraseña se lee en servidor desde dbo.Usuarios.Contrasena.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [AutorizarAccion("Ver Usuarios", "Ver")]
        public async Task<IActionResult> EnviarAccesosPorCorreo(int id)
        {
            if (id <= 0)
            {
                Response.StatusCode = 400;
                return Json(new
                {
                    ok = false,
                    message = "El usuario seleccionado no es válido."
                });
            }

            var usuario = await _context.Usuarios
                .AsNoTracking()
                .Where(u => u.UsuarioID == id)
                .Select(u => new
                {
                    u.UsuarioID,
                    u.PersonaID,
                    u.Username,
                    u.Activo
                })
                .FirstOrDefaultAsync();

            if (usuario == null)
            {
                Response.StatusCode = 404;
                return Json(new
                {
                    ok = false,
                    message = "No se encontró el usuario seleccionado."
                });
            }

            if (!usuario.Activo)
            {
                Response.StatusCode = 400;
                return Json(new
                {
                    ok = false,
                    message = "No se pueden enviar accesos porque el usuario está inactivo."
                });
            }

            var persona = await _context.Personas
                .AsNoTracking()
                .Where(p => p.PersonaID == usuario.PersonaID)
                .Select(p => new
                {
                    p.Nombre,
                    p.ApellidoPaterno,
                    p.ApellidoMaterno,
                    p.Correo
                })
                .FirstOrDefaultAsync();

            var correo = persona?.Correo?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(correo))
            {
                Response.StatusCode = 400;
                return Json(new
                {
                    ok = false,
                    message = "El usuario no tiene correo electrónico registrado."
                });
            }

            if (string.IsNullOrWhiteSpace(usuario.Username))
            {
                Response.StatusCode = 400;
                return Json(new
                {
                    ok = false,
                    message = "El usuario no tiene nombre de usuario registrado."
                });
            }

            var contrasena = await ObtenerContrasenaUsuarioAsync(usuario.UsuarioID);

            if (string.IsNullOrWhiteSpace(contrasena))
            {
                Response.StatusCode = 400;
                return Json(new
                {
                    ok = false,
                    message = "El usuario no tiene contraseña registrada en dbo.Usuarios.Contrasena."
                });
            }

            var nombreCompleto = string.Join(" ", new[]
            {
                persona?.Nombre,
                persona?.ApellidoPaterno,
                persona?.ApellidoMaterno
            }.Where(x => !string.IsNullOrWhiteSpace(x))).Trim();

            if (string.IsNullOrWhiteSpace(nombreCompleto))
            {
                nombreCompleto = usuario.Username;
            }

            var resultadoEnvio = await EnviarCorreoAccesosAsync(
                usuario.PersonaID,
                correo,
                nombreCompleto,
                usuario.Username,
                contrasena
            );

            if (!resultadoEnvio.Ok)
            {
                Response.StatusCode = 500;
                return Json(new
                {
                    ok = false,
                    message = resultadoEnvio.Mensaje
                });
            }

            await MarcarCambioPasswordObligatorioAsync(usuario.UsuarioID);

            return Json(new
            {
                ok = true,
                message = resultadoEnvio.Mensaje + " Se marcó al usuario para cambio obligatorio de contraseña en su próximo inicio de sesión."
            });
        }

        // GET: /Usuarios/Crear
        [AutorizarAccion("Crear Usuarios", "Crear")]
        public async Task<IActionResult> Crear()
        {
          
            ViewBag.URol = await ObtenerListaRolesSQL();

            // 2. Cargamos los Departamentos usando SQL Puro
            var listaDeptos = new List<SelectListItem>();
            string cnn = _context.Database.GetConnectionString();

            using (var conn = new Microsoft.Data.SqlClient.SqlConnection(cnn))
            {
                await conn.OpenAsync();
                // Consulta SQL para traer solo departamentos activos
                const string sql = "SELECT DepartamentoID, NombreDepartamento FROM Departamentos WHERE Activo = 1 ORDER BY NombreDepartamento";

                using (var cmd = new Microsoft.Data.SqlClient.SqlCommand(sql, conn))
                {
                    using (var rd = await cmd.ExecuteReaderAsync())
                    {
                        while (await rd.ReadAsync())
                        {
                            listaDeptos.Add(new SelectListItem
                            {
                                Value = rd["DepartamentoID"].ToString(),
                                Text = rd["NombreDepartamento"].ToString()
                            });
                        }
                    }
                }
            }
            // Pasamos la lista a la vista mediante ViewBag
            ViewBag.Departamentos = listaDeptos;

            // 3. Inicializamos el ViewModel
            var viewModel = new UsuarioFormViewModel
            {
                EsModoCrear = true,
                MenusDisponibles = await _usuarioService.ObtenerMenusConSubMenusAsync()
            };

            // Evita NullReference en la vista parcial
            ViewBag.OverrideGrupos = new List<OverridesVm>();
            ViewBag.Overrides = new List<OverrideItemDto>();

            return PartialView("_UsuarioForm", viewModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [AutorizarAccion("Crear Usuarios", "Crear")]
        public async Task<IActionResult> Crear(UsuarioFormViewModel viewModel)
        {
            if (ModelState.IsValid)
            {
                var nuevoUsuarioDto = new UsuarioRegistroDTO
                {
                    Nombre = viewModel.Nombre,
                    ApellidoPaterno = viewModel.ApellidoPaterno,
                    ApellidoMaterno = viewModel.ApellidoMaterno,
                    Correo = viewModel.Correo,
                    Telefono = viewModel.Telefono,
                    Username = viewModel.Username,
                    Password = viewModel.Password,
                    RolID = viewModel.RolID,
                    SubMenuIDs = viewModel.SubMenuIDs ?? new List<int>(),

                    FechaIngreso = viewModel.FechaIngreso,
                    Puesto = viewModel.Puesto,
                    // AGREGAR ESTA LÍNEA:
                    DepartamentoID = viewModel.DepartamentoID
                };
                ViewBag.Departamentos = await ObtenerListaDepartamentosSQL(viewModel.DepartamentoID);

                await _usuarioService.RegistrarAsync(nuevoUsuarioDto);

                var usuarioCreado = await _context.Usuarios
                    .AsNoTracking()
                    .Where(u => u.Username == viewModel.Username)
                    .OrderByDescending(u => u.UsuarioID)
                    .Select(u => new { u.UsuarioID })
                    .FirstOrDefaultAsync();

                if (usuarioCreado != null)
                {
                    await MarcarCambioPasswordObligatorioAsync(usuarioCreado.UsuarioID);

                    await GuardarDepartamentoUsuarioAsync(
                        usuarioCreado.UsuarioID,
                        viewModel.DepartamentoID);
                }

                var credencialesEnviadas = await EnviarCredenciales(
                    usuarioCreado?.UsuarioID,
                    viewModel.Nombre,
                    viewModel.Username,
                    viewModel.Password);

                TempData["SuccessMessage"] = credencialesEnviadas
                    ? "Usuario creado exitosamente. Las credenciales fueron enviadas al correo registrado."
                    : "Usuario creado exitosamente. No se enviaron credenciales porque el usuario no tiene correo válido o el envío fue bloqueado por la configuración de notificaciones.";

                if (EsPeticionAjax())
                {
                    return Json(new
                    {
                        ok = true,
                        message = TempData["SuccessMessage"]?.ToString(),
                        redirectUrl = Url.Action(nameof(Index), new { activos = true })
                    });
                }

                return RedirectToAction(nameof(Index), new { activos = true });
            }

            // RECARGAR LISTA DE DEPARTAMENTOS SI HAY ERRORES (SQL Puro)
            ViewBag.Departamentos = await ObtenerListaDepartamentosSQL(viewModel.DepartamentoID);

        
            ViewBag.URol = await ObtenerListaRolesSQL(viewModel.RolID);
            viewModel.MenusDisponibles = await _usuarioService.ObtenerMenusConSubMenusAsync();
            ViewBag.OverrideGrupos = new List<OverridesVm>();
            ViewBag.Overrides = new List<OverrideItemDto>();
            return PartialView("_UsuarioForm", viewModel);
        }

        // GET: /Usuarios/Editar/5
        [AutorizarAccion("Editar Usuarios", "Editar")]
        public async Task<IActionResult> Editar(int id)
        {
            var usuarioDto = await _usuarioService.ObtenerParaEditarAsync(id);
            if (usuarioDto == null) return NotFound();

            var departamentoIdActual = usuarioDto.DepartamentoID 
                           ?? await ObtenerDepartamentoUsuarioAsync(id);

            string nombreJefe = "";
            string nombreDepto = "";
            var listaDeptos = new List<SelectListItem>(); // Nueva lista para el select
            string cnn = _context.Database.GetConnectionString();

            // --- BLOQUE SQL PURO PARA CARGAR DATOS Y LISTA ---
            using (var conn = new Microsoft.Data.SqlClient.SqlConnection(cnn))
            {
                await conn.OpenAsync();

                // 1. Obtener nombre del Jefe


                // 2. CARGAR LISTA COMPLETA DE DEPARTAMENTOS
                const string sqlLista = "SELECT DepartamentoID, NombreDepartamento FROM Departamentos WHERE Activo = 1 ORDER BY NombreDepartamento";
                using (var cmd = new Microsoft.Data.SqlClient.SqlCommand(sqlLista, conn))
                {
                    using (var rd = await cmd.ExecuteReaderAsync())
                    {
                        while (await rd.ReadAsync())
                        {
                            listaDeptos.Add(new SelectListItem
                            {
                                Value = rd["DepartamentoID"].ToString(),
                                Text = rd["NombreDepartamento"].ToString(),
                                // Pre-selecciona el depto actual del usuario
                                Selected = departamentoIdActual.HasValue && 
                                           (int)rd["DepartamentoID"] == departamentoIdActual.Value
                            });
                        }
                    }
                }

                // 3. Obtener nombre del Departamento actual (para el ViewModel)
                if (departamentoIdActual.HasValue)
                {
                    var sqlDepto = "SELECT NombreDepartamento FROM Departamentos WHERE DepartamentoID = @id";
                    using (var cmd = new Microsoft.Data.SqlClient.SqlCommand(sqlDepto, conn))
                    {
                        cmd.Parameters.AddWithValue("@id", departamentoIdActual.Value);
                        nombreDepto = (await cmd.ExecuteScalarAsync())?.ToString() ?? "";
                    }
                }
            }

            ViewBag.Departamentos = listaDeptos;
      
            ViewBag.URol = await ObtenerListaRolesSQL(usuarioDto.RolID);

            ViewBag.Departamentos = await ObtenerListaDepartamentosSQL(departamentoIdActual);

            var viewModel = new UsuarioFormViewModel
            {
                UsuarioID = usuarioDto.UsuarioID,
                Nombre = usuarioDto.Nombre,
                ApellidoPaterno = usuarioDto.ApellidoPaterno,
                ApellidoMaterno = usuarioDto.ApellidoMaterno,
                Correo = usuarioDto.Correo,
                Telefono = usuarioDto.Telefono,
                RolID = usuarioDto.RolID,
                Activo = usuarioDto.Activo,
                SubMenuIDs = usuarioDto.SubMenuIDs ?? new List<int>(),
                HistorialDeCambios = await _usuarioService.ObtenerHistorialAsync(id),
                MenusDisponibles = await _usuarioService.ObtenerMenusConSubMenusAsync(),
                FechaIngreso = usuarioDto.FechaIngreso,
                Puesto = usuarioDto.Puesto,
                DepartamentoID = departamentoIdActual,
                NombreDepartamento = nombreDepto
            };
            // --- LÓGICA DE OVERRIDES ---
            List<OverrideItemDto> overridesItems;
            try
            {
                overridesItems = await _usuarioService.ListarOverridesAsync(id, null);
            }
            catch (Exception ex)
            {
                TempData["WarningMessage"] = "No se pudieron cargar los overrides: " + ex.Message;
                overridesItems = new List<OverrideItemDto>();
            }

            if (overridesItems.Count == 0)
            {
                var menus = await _usuarioService.ObtenerMenusConSubMenusAsync();
                foreach (var menu in menus)
                {
                    foreach (var subMenu in menu.SubMenus)
                    {
                        bool permisoEfectivo = await _usuarioService.VerificarPermisoAsync(id, subMenu.SubMenuID);
                        overridesItems.Add(new OverrideItemDto
                        {
                            MenuID = menu.MenuID,
                            MenuNombre = menu.Nombre,
                            SubMenuID = subMenu.SubMenuID,
                            Nombre = subMenu.Nombre,
                            Estado = -1,
                            PermisoEfectivo = permisoEfectivo
                        });
                    }
                }
            }

            var grupos = overridesItems
                .GroupBy(x => new { x.MenuID, x.MenuNombre })
                .Select(g => new OverridesVm
                {
                    UsuarioID = id,
                    EmpresaID = null,
                    MenuID = g.Key.MenuID,
                    MenuNombre = g.Key.MenuNombre,
                    Items = g.OrderBy(it => it.Nombre).ToList()
                })
                .OrderBy(g => g.MenuNombre)
                .ToList();

            ViewBag.OverrideGrupos = grupos;
            ViewBag.Overrides = overridesItems;

            return PartialView("_UsuarioForm", viewModel);
        }

        // POST: /Usuarios/Editar/5
        // POST: /Usuarios/Editar/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [AutorizarAccion("Editar Usuarios", "Editar")]
        public async Task<IActionResult> Editar(int id, UsuarioFormViewModel viewModel)
        {
            var routeValues = new
            {
                activos = HttpContext.Request.Query["activos"],
                filtroCampo = HttpContext.Request.Query["filtroCampo"],
                busqueda = HttpContext.Request.Query["busqueda"]
            };

            if (id != viewModel.UsuarioID) return BadRequest();

            // No se editan en esta pantalla
            ModelState.Remove(nameof(viewModel.Username));
            ModelState.Remove(nameof(viewModel.Password));

            if (ModelState.IsValid)
            {
                var usuarioEditadoDto = new UsuarioEdicionDTO
                {
                    UsuarioID = viewModel.UsuarioID!.Value,
                    Nombre = viewModel.Nombre,
                    ApellidoPaterno = viewModel.ApellidoPaterno,
                    ApellidoMaterno = viewModel.ApellidoMaterno,
                    Correo = viewModel.Correo,
                    Telefono = viewModel.Telefono,
                    RolID = viewModel.RolID,
                    Activo = viewModel.Activo,
                    SubMenuIDs = viewModel.SubMenuIDs ?? new List<int>(),

                    FechaIngreso = viewModel.FechaIngreso,
                    Puesto = viewModel.Puesto,
                    // AGREGADO PARA GUARDAR:
                    DepartamentoID = viewModel.DepartamentoID
                };

                await _usuarioService.ActualizarAsync(usuarioEditadoDto);

                await GuardarDepartamentoUsuarioAsync(
                    viewModel.UsuarioID!.Value,
                    viewModel.DepartamentoID
                );

                if (!viewModel.Activo)
                {
                    await OcultarContenidoAsignadoAsync(viewModel.UsuarioID!.Value);
                }

                TempData["SuccessMessage"] = viewModel.Activo
                    ? "Usuario actualizado correctamente."
                    : "Usuario actualizado correctamente. Al quedar desactivado, se ocultó su contenido asignado.";

                if (EsPeticionAjax())
                {
                    return Json(new
                    {
                        ok = true,
                        message = TempData["SuccessMessage"]?.ToString(),
                        redirectUrl = Url.Action(nameof(Index), routeValues)
                    });
                }

                return RedirectToAction(nameof(Index), routeValues);
            }

            // AGREGADO PARA RECARGAR LA LISTA SI HAY ERROR DE VALIDACIÓN:
            ViewBag.Departamentos = await ObtenerListaDepartamentosSQL(viewModel.DepartamentoID);

            ViewBag.URol = await ObtenerListaRolesSQL(viewModel.RolID);
            viewModel.HistorialDeCambios = await _usuarioService.ObtenerHistorialAsync(id);
            viewModel.MenusDisponibles = await _usuarioService.ObtenerMenusConSubMenusAsync();

            // Recargar overrides
            ViewBag.OverrideGrupos = new List<OverridesVm>();
            ViewBag.Overrides = new List<OverrideItemDto>();

            if (EsPeticionAjax())
            {
                Response.StatusCode = 400;
            }

            return PartialView("_UsuarioForm", viewModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [AutorizarAccion("Editar Usuarios", "Editar")]
        public async Task<IActionResult> RestablecerPassword(
            int usuarioId,
            string nuevaPassword,
            string confirmarPassword,
            bool forzarCambio = false,
            bool enviarCorreo = false)
        {
            if (usuarioId <= 0)
            {
                Response.StatusCode = 400;
                return Json(new { ok = false, message = "El usuario seleccionado no es válido." });
            }

            nuevaPassword = (nuevaPassword ?? string.Empty).Trim();
            confirmarPassword = (confirmarPassword ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(nuevaPassword) || nuevaPassword.Length < 6)
            {
                Response.StatusCode = 400;
                return Json(new { ok = false, message = "La contraseña debe tener al menos 6 caracteres." });
            }

            if (!string.Equals(nuevaPassword, confirmarPassword, StringComparison.Ordinal))
            {
                Response.StatusCode = 400;
                return Json(new { ok = false, message = "La confirmación no coincide con la nueva contraseña." });
            }

            var usuario = await _context.Usuarios
                .AsNoTracking()
                .Where(u => u.UsuarioID == usuarioId)
                .Select(u => new
                {
                    u.UsuarioID,
                    u.PersonaID,
                    u.Username,
                    u.Activo
                })
                .FirstOrDefaultAsync();

            if (usuario == null)
            {
                Response.StatusCode = 404;
                return Json(new { ok = false, message = "No se encontró el usuario seleccionado." });
            }

            if (!usuario.Activo)
            {
                Response.StatusCode = 400;
                return Json(new { ok = false, message = "No se puede restablecer la contraseña de un usuario inactivo." });
            }

            await RestablecerPasswordUsuarioAsync(usuario.UsuarioID, nuevaPassword, forzarCambio);

            var mensajes = new List<string>
            {
                "Contraseña restablecida correctamente."
            };

            if (forzarCambio)
            {
                mensajes.Add("El usuario deberá cambiarla al iniciar sesión.");
            }
            else
            {
                mensajes.Add("El usuario podrá ingresar con esta contraseña sin cambio obligatorio.");
            }

            if (enviarCorreo)
            {
                var persona = await _context.Personas
                    .AsNoTracking()
                    .Where(p => p.PersonaID == usuario.PersonaID)
                    .Select(p => new
                    {
                        p.Nombre,
                        p.ApellidoPaterno,
                        p.ApellidoMaterno,
                        p.Correo
                    })
                    .FirstOrDefaultAsync();

                var correo = persona?.Correo?.Trim() ?? string.Empty;

                if (!string.IsNullOrWhiteSpace(correo) && !string.IsNullOrWhiteSpace(usuario.Username))
                {
                    var nombreCompleto = string.Join(" ", new[]
                    {
                        persona?.Nombre,
                        persona?.ApellidoPaterno,
                        persona?.ApellidoMaterno
                    }.Where(x => !string.IsNullOrWhiteSpace(x))).Trim();

                    if (string.IsNullOrWhiteSpace(nombreCompleto))
                    {
                        nombreCompleto = usuario.Username;
                    }

                    var resultadoEnvio = await EnviarCorreoAccesosAsync(
                        usuario.PersonaID,
                        correo,
                        nombreCompleto,
                        usuario.Username,
                        nuevaPassword);

                    if (resultadoEnvio.Ok)
                    {
                        mensajes.Add("La nueva contraseña fue enviada por correo.");
                    }
                    else
                    {
                        mensajes.Add("No se pudo enviar el correo: " + resultadoEnvio.Mensaje);
                    }
                }
                else
                {
                    mensajes.Add("No se envió correo porque el usuario no tiene correo registrado.");
                }
            }
            else
            {
                mensajes.Add("No se envió correo porque la opción fue desmarcada.");
            }

            return Json(new
            {
                ok = true,
                message = string.Join(" ", mensajes)
            });
        }

        // POST: /Usuarios/Eliminar/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [AutorizarAccion("Eliminar Usuarios", "Eliminar")]
        public async Task<IActionResult> Eliminar(int id)
        {
            var routeValues = new
            {
                activos = HttpContext.Request.Query["activos"],
                filtroCampo = HttpContext.Request.Query["filtroCampo"],
                busqueda = HttpContext.Request.Query["busqueda"]
            };

            await _usuarioService.DarDeBajaAsync(id);
            await OcultarContenidoAsignadoAsync(id);
            TempData["SuccessMessage"] = "Usuario dado de baja correctamente.";
            return RedirectToAction(nameof(Index), routeValues);
        }

        // VALIDACIÓN REMOTA
        [AcceptVerbs("GET", "POST")]
        [AutorizarAccion("Editar Usuarios", "Editar")]
        public async Task<IActionResult> VerificarUsername(string username, int? usuarioID)
        {
            var query = _context.Usuarios.AsQueryable();
            if (usuarioID.HasValue) query = query.Where(u => u.UsuarioID != usuarioID.Value);
            var existe = await query.AnyAsync(u => u.Username == username);
            return existe ? Json($"El username '{username}' ya está en uso.") : Json(true);
        }

        [AcceptVerbs("GET", "POST")]
        public async Task<IActionResult> VerificarCorreo(string? correo, int? usuarioID)
        {
            if (string.IsNullOrWhiteSpace(correo)) return Json(true);

            correo = correo.Trim();

            var query = _context.Personas.AsQueryable();
            if (usuarioID.HasValue)
            {
                var personaId = await _context.Usuarios
                    .Where(u => u.UsuarioID == usuarioID.Value)
                    .Select(u => u.PersonaID)
                    .FirstOrDefaultAsync();

                if (personaId > 0) query = query.Where(p => p.PersonaID != personaId);
            }

            var existe = await query.AnyAsync(p => p.Correo == correo);
            return existe ? Json($"El correo '{correo}' ya está en uso.") : Json(true);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GuardarOverrides(int UsuarioID, List<OverrideItemDto> Items)
        {
            // Solo consideramos AJAX cuando explícitamente venga este header.
            // No uses Accept: application/json para decidir, porque puede provocar JSON crudo en pantalla.
            bool isAjax = string.Equals(
                Request.Headers["X-Requested-With"].ToString(),
                "XMLHttpRequest",
                StringComparison.OrdinalIgnoreCase);

            try
            {
                Items ??= new List<OverrideItemDto>();

                System.Diagnostics.Debug.WriteLine($"GuardarOverrides llamado: UsuarioID={UsuarioID}, Items={Items.Count}");

                await _usuarioService.GuardarOverridesAsync(UsuarioID, null, Items);

                HttpContext.Session.Remove("MenuItems");
                HttpContext.Session.Remove("MenuUsuario");

                var usuarioActualId = HttpContext.Session.GetInt32("UsuarioID");
                string mensajeExtra = string.Empty;

                if (usuarioActualId.HasValue && usuarioActualId.Value == UsuarioID)
                {
                    var menuActualizado = await ObtenerMenuActualizadoAsync(UsuarioID);

                    HttpContext.Session.SetString(
                        "MenuUsuario",
                        System.Text.Json.JsonSerializer.Serialize(menuActualizado));

                    TempData["RefreshMenu"] = "true";
                    mensajeExtra = " Recarga la página para ver los cambios en el menú.";
                }

                var mensaje = "Permisos actualizados correctamente." + mensajeExtra;

                if (isAjax)
                {
                    return Json(new
                    {
                        ok = true,
                        message = mensaje
                    });
                }

                TempData["SuccessMessage"] = mensaje;

                // Fallback si por alguna razón el JS no intercepta el submit.
                // Evita que el navegador muestre JSON crudo.
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error en GuardarOverrides: {ex.Message}");
                System.Diagnostics.Debug.WriteLine(ex.StackTrace);

                var mensajeError = "Error al guardar permisos: " + ex.Message;

                if (isAjax)
                {
                    Response.StatusCode = 500;

                    return Json(new
                    {
                        ok = false,
                        message = mensajeError
                    });
                }

                TempData["ErrorMessage"] = mensajeError;
                return RedirectToAction(nameof(Index));
            }
        }

        //Metodoo que devuelve un jason para el buscador de jefes


        

        [HttpGet]
        public async Task<IActionResult> BuscarDepartamentos(string term)
        {
            if (string.IsNullOrWhiteSpace(term))
                return Json(new { results = new List<object>() });

            var resultados = new List<object>();
            string? cnn = _context.Database.GetConnectionString();

            if (string.IsNullOrWhiteSpace(cnn))
                return Json(new { results = resultados });

            await using var conn = new Microsoft.Data.SqlClient.SqlConnection(cnn);
            await conn.OpenAsync();

            const string sql = @"
                SELECT TOP 15 DepartamentoID, NombreDepartamento
                FROM dbo.Departamentos
                WHERE NombreDepartamento LIKE @t
                  AND Activo = 1
                ORDER BY NombreDepartamento;";

            await using var cmd = new Microsoft.Data.SqlClient.SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@t", $"%{term.Trim()}%");

            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                resultados.Add(new
                {
                    id = rd["DepartamentoID"],
                    text = rd["NombreDepartamento"]?.ToString()
                });
            }

            return Json(new { results = resultados });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetOverride(ERP.NSQuell.Areas.AdminUsuarios.DTOs.SetOverrideRequest req)
        {
            try
            {
                // Normaliza estado nulo a heredar
                var estado = req.Estado;

                var item = new ERP.NSQuell.Areas.AdminUsuarios.DTOs.OverrideItemDto
                {
                    SubMenuID = req.SubMenuID,
                    Estado = estado
                };

                // Guarda 1 solo override
                await _usuarioService.GuardarOverridesAsync(req.UsuarioID, null, new[] { item });

                // 🔥 Invalidar caché de menú en sesión
                HttpContext.Session.Remove("MenuItems");
                HttpContext.Session.Remove("MenuUsuario");

                // Recalcular permiso efectivo de esa fila (usa tu servicio)
                var efectivo = await _usuarioService.VerificarPermisoAsync(req.UsuarioID, req.SubMenuID);

                // Responder JSON para que la vista marque selección y ✔️/✖️
                return Json(new
                {
                    ok = true,
                    estado = estado,
                    efectivo = efectivo,
                    refreshMenu = true,
                    message = "Override guardado correctamente."
                });
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, message = ex.Message });
            }
        }



        private async Task<List<SelectListItem>> ObtenerListaRolesSQL(int? seleccionadoId = null)
        {
            var lista = new List<SelectListItem>();
            string? cnn = _context.Database.GetConnectionString();

            if (string.IsNullOrWhiteSpace(cnn))
            {
                return lista;
            }

            await using var conn = new Microsoft.Data.SqlClient.SqlConnection(cnn);
            await conn.OpenAsync();

            const string sql = @"
                SELECT RolID, NombreRol
                FROM dbo.Roles
                ORDER BY NombreRol";

            await using var cmd = new Microsoft.Data.SqlClient.SqlCommand(sql, conn);
            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                int idRol = Convert.ToInt32(rd["RolID"]);
                lista.Add(new SelectListItem
                {
                    Value = idRol.ToString(),
                    Text = rd["NombreRol"]?.ToString() ?? $"Rol {idRol}",
                    Selected = seleccionadoId.HasValue && idRol == seleccionadoId.Value
                });
            }

            return lista;
        }

        private async Task<string?> ObtenerContrasenaUsuarioAsync(int usuarioId)
        {
            string? cnn = _context.Database.GetConnectionString();

            if (string.IsNullOrWhiteSpace(cnn))
            {
                return null;
            }

            await using var conn = new Microsoft.Data.SqlClient.SqlConnection(cnn);
            await conn.OpenAsync();

            const string sql = @"
                SELECT TOP 1 Contrasena
                FROM dbo.Usuarios
                WHERE UsuarioID = @UsuarioID";

            await using var cmd = new Microsoft.Data.SqlClient.SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@UsuarioID", usuarioId);

            var result = await cmd.ExecuteScalarAsync();

            return result == null || result == DBNull.Value
                ? null
                : result.ToString();
        }

        private async Task<(bool Ok, string Mensaje)> EnviarCorreoAccesosAsync(
            int personaId,
            string correoDestino,
            string nombreCompleto,
            string username,
            string contrasena)
        {
            if (personaId <= 0)
            {
                return (false, "No se recibió un PersonaID válido para enviar accesos.");
            }

            if (string.IsNullOrWhiteSpace(correoDestino))
            {
                return (false, "No se recibió el correo destino.");
            }

            if (string.IsNullOrWhiteSpace(username))
            {
                return (false, "No se recibió el nombre de usuario.");
            }

            if (string.IsNullOrWhiteSpace(contrasena))
            {
                return (false, "No se recibió la contraseña del usuario.");
            }

            var nombreSeguro = WebUtility.HtmlEncode(nombreCompleto ?? "Usuario");
            var usernameSeguro = WebUtility.HtmlEncode(username);
            var contrasenaSeguro = WebUtility.HtmlEncode(contrasena);

            const string asunto = "Accesos al ERP";

            var html = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='UTF-8'>
</head>
<body style='font-family:Segoe UI,Arial; background:#f4f4f9; padding:20px;'>
  <div style='max-width:650px; margin:0 auto; background:#fff; border-radius:12px; overflow:hidden; box-shadow:0 4px 10px rgba(0,0,0,.08);'>
    <div style='padding:22px; background:#1a237e; color:#fff; text-align:center;'>
      <h2 style='margin:0;'>Accesos al ERP</h2>
    </div>

    <div style='padding:22px; color:#333;'>
      <p>Hola <strong>{nombreSeguro}</strong>,</p>
      <p>Te compartimos tus credenciales para ingresar al ERP.</p>

      <div style='background:#f8f9fa; border-left:4px solid #ff6d00; padding:14px 16px; border-radius:6px; margin:16px 0;'>
        <p style='margin:0 0 8px;'><strong>Usuario:</strong> {usernameSeguro}</p>
        <p style='margin:0;'><strong>Contraseña:</strong> {contrasenaSeguro}</p>
      </div>

      <p>Ingresa aquí:</p>
      <p><a href='https://erp.nsgroup.com.mx/'>https://erp.nsgroup.com.mx/</a></p>

      <p style='color:#666; font-size:12px; margin-top:18px;'>
        Mensaje generado automáticamente por el ERP NSQuell. No respondas a este correo.
      </p>
    </div>
  </div>
</body>
</html>";

            try
            {
                // Importante: seguimos el mismo patrón que ya usa VacacionesController:
                // el servicio recibe PersonaID, no correo directo ni UsuarioID.
                var resultado = await _servicioNotificaciones.EnviarABccPersonasAsync(
                    new List<int> { personaId },
                    asunto,
                    html);

                var detalleMensajes = ObtenerDetalleMensajesResultado(resultado);

                System.Diagnostics.Debug.WriteLine(
                    $"Correo accesos => PersonaID={personaId}, Correo={correoDestino}, Encontrados={resultado.Encontrados}, Enviados={resultado.Enviados}, Filtrados={resultado.FiltradosPorCandados}, Errores={resultado.Errores}, Detalle={detalleMensajes}");

                if (resultado.Enviados > 0 && resultado.Errores == 0)
                {
                    return (true, $"Los accesos fueron enviados correctamente a {correoDestino}.");
                }

                if (resultado.FiltradosPorCandados > 0)
                {
                    return (false,
                        $"El correo fue filtrado por los candados de notificaciones. Encontrados: {resultado.Encontrados}, Enviados: {resultado.Enviados}, Filtrados: {resultado.FiltradosPorCandados}, Errores: {resultado.Errores}.{detalleMensajes} Revisa Habilitado, SoloPruebas y ListaBlanca.");
                }

                if (resultado.Encontrados == 0)
                {
                    return (false,
                        $"El servicio de notificaciones no encontró un correo válido para PersonaID={personaId}.{detalleMensajes}");
                }

                return (false,
                    $"El servicio de notificaciones no reportó un envío exitoso. Encontrados: {resultado.Encontrados}, Enviados: {resultado.Enviados}, Filtrados: {resultado.FiltradosPorCandados}, Errores: {resultado.Errores}.{detalleMensajes} Si el detalle está vacío, revisa la excepción registrada dentro de ServicioNotificaciones/SMTP.");
            }
            catch (Exception ex)
            {
                return (false, $"Error al enviar el correo de accesos: {ex.Message}");
            }
        }

        private async Task<List<SelectListItem>> ObtenerListaDepartamentosSQL(int? seleccionadoId = null)
        {
            var lista = new List<SelectListItem>();
            string cnn = _context.Database.GetConnectionString();

            using (var conn = new Microsoft.Data.SqlClient.SqlConnection(cnn))
            {
                await conn.OpenAsync();
                const string sql = "SELECT DepartamentoID, NombreDepartamento FROM Departamentos WHERE Activo = 1 ORDER BY NombreDepartamento";
                using (var cmd = new Microsoft.Data.SqlClient.SqlCommand(sql, conn))
                {
                    using (var rd = await cmd.ExecuteReaderAsync())
                    {
                        while (await rd.ReadAsync())
                        {
                            int idDepto = (int)rd["DepartamentoID"];
                            lista.Add(new SelectListItem
                            {
                                Value = idDepto.ToString(),
                                Text = rd["NombreDepartamento"].ToString(),
                                Selected = seleccionadoId.HasValue && idDepto == seleccionadoId.Value
                            });
                        }
                    }
                }
            }
            return lista;
        }

        private bool EsPeticionAjax()
        {
            return Request.Headers["X-Requested-With"] == "XMLHttpRequest"
                   || Request.Headers.Accept.ToString().Contains("application/json");
        }

        private string ObtenerDetalleMensajesResultado(object resultado)
        {
            try
            {
                var propMensajes = resultado.GetType().GetProperty("Mensajes");
                var valorMensajes = propMensajes?.GetValue(resultado);

                if (valorMensajes is System.Collections.IEnumerable mensajesEnumerable && valorMensajes is not string)
                {
                    var partes = new List<string>();

                    foreach (var item in mensajesEnumerable)
                    {
                        var texto = item?.ToString();
                        if (!string.IsNullOrWhiteSpace(texto))
                        {
                            partes.Add(texto.Trim());
                        }
                    }

                    if (partes.Any())
                    {
                        return " Detalle: " + string.Join(" | ", partes);
                    }
                }
            }
            catch
            {
                // No rompemos el flujo solo por no poder leer mensajes de diagnóstico.
            }

            return string.Empty;
        }

        private async Task<bool> EnviarCredenciales(int? usuarioId, string? nombre, string? username, string? password)
        {
            if (!usuarioId.HasValue
                || string.IsNullOrWhiteSpace(username)
                || string.IsNullOrWhiteSpace(password))
            {
                return false;
            }

            try
            {
                var usuario = await _context.Usuarios
                    .AsNoTracking()
                    .Where(u => u.UsuarioID == usuarioId.Value)
                    .Select(u => new
                    {
                        u.PersonaID
                    })
                    .FirstOrDefaultAsync();

                if (usuario == null || usuario.PersonaID <= 0)
                {
                    return false;
                }

                var nombreSeguro = WebUtility.HtmlEncode(nombre ?? "Usuario");
                var usernameSeguro = WebUtility.HtmlEncode(username);
                var passwordSeguro = WebUtility.HtmlEncode(password);

                const string asunto = "Credenciales de acceso al ERP";

                var html = $@"
<!DOCTYPE html>
<html>
<head><meta charset='UTF-8'></head>
<body style='font-family:Segoe UI,Arial; background:#f4f4f9; padding:20px;'>
  <div style='max-width:650px; margin:0 auto; background:#fff; border-radius:12px; overflow:hidden; box-shadow:0 4px 10px rgba(0,0,0,.08);'>
    <div style='padding:22px; background:#1a237e; color:#fff; text-align:center;'>
      <h2 style='margin:0;'>Bienvenido(a) al ERP</h2>
    </div>
    <div style='padding:22px; color:#333;'>
      <p>Hola <strong>{nombreSeguro}</strong>,</p>
      <p>Tu usuario fue dado de alta en el ERP.</p>
      <p style='color:#0d47a1;'><strong>Importante:</strong> al iniciar sesión por primera vez se te pedirá cambiar esta contraseña temporal. Por seguridad, el sistema también solicitará cambio de contraseña cada 2 meses.</p>

      <div style='background:#f8f9fa; border-left:4px solid #ff6d00; padding:14px 16px; border-radius:6px; margin:16px 0;'>
        <p style='margin:0 0 8px;'><strong>Usuario:</strong> {usernameSeguro}</p>
        <p style='margin:0;'><strong>Contraseña:</strong> {passwordSeguro}</p>
      </div>

      <p>Ingresa aquí:</p>
      <p><a href='https://erp.nsgroup.com.mx/'>https://erp.nsgroup.com.mx/</a></p>

      <p style='color:#666; font-size:12px; margin-top:18px;'>Mensaje generado automáticamente por el ERP NSQuell. No respondas a este correo.</p>
    </div>
  </div>
</body>
</html>";

                var resultado = await _servicioNotificaciones.EnviarABccPersonasAsync(
                    new List<int> { usuario.PersonaID },
                    asunto,
                    html);

                return resultado.Enviados > 0 && resultado.Errores == 0;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"No se pudieron enviar credenciales al usuario: {ex.Message}");
                return false;
            }
        }



        private async Task MarcarCambioPasswordObligatorioAsync(int usuarioId)
        {
            if (usuarioId <= 0)
                return;

            string cnn = _context.Database.GetConnectionString();

            await using var conn = new Microsoft.Data.SqlClient.SqlConnection(cnn);
            await conn.OpenAsync();

            const string sql = @"
                UPDATE Usuarios
                SET DebeCambiarPassword = 1,
                    FechaUltimoCambioPassword = NULL
                WHERE UsuarioID = @UsuarioID;";

            await using var cmd = new Microsoft.Data.SqlClient.SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@UsuarioID", usuarioId);

            await cmd.ExecuteNonQueryAsync();
        }

        private async Task RestablecerPasswordUsuarioAsync(int usuarioId, string nuevaPassword, bool forzarCambio)
        {
            string cnn = _context.Database.GetConnectionString();

            await using var conn = new Microsoft.Data.SqlClient.SqlConnection(cnn);
            await conn.OpenAsync();

            const string sql = @"
                UPDATE Usuarios
                SET Contrasena = @NuevaPassword,
                    DebeCambiarPassword = @DebeCambiarPassword,
                    FechaUltimoCambioPassword = CASE
                        WHEN @DebeCambiarPassword = 1 THEN NULL
                        ELSE GETDATE()
                    END
                WHERE UsuarioID = @UsuarioID
                  AND Activo = 1;";

            await using var cmd = new Microsoft.Data.SqlClient.SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@UsuarioID", usuarioId);
            cmd.Parameters.AddWithValue("@NuevaPassword", nuevaPassword);
            cmd.Parameters.AddWithValue("@DebeCambiarPassword", forzarCambio ? 1 : 0);

            await cmd.ExecuteNonQueryAsync();
        }

        private async Task<int?> ObtenerDepartamentoUsuarioAsync(int usuarioId)
        {
            if (usuarioId <= 0)
                return null;

            string? cnn = _context.Database.GetConnectionString();

            if (string.IsNullOrWhiteSpace(cnn))
                return null;

            await using var conn = new Microsoft.Data.SqlClient.SqlConnection(cnn);
            await conn.OpenAsync();

            const string sql = @"
                SELECT TOP 1 DepartamentoID
                FROM dbo.Usuarios
                WHERE UsuarioID = @UsuarioID;";

            await using var cmd = new Microsoft.Data.SqlClient.SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@UsuarioID", usuarioId);

            var result = await cmd.ExecuteScalarAsync();

            if (result == null || result == DBNull.Value)
                return null;

            return Convert.ToInt32(result);
        }

        private async Task OcultarContenidoAsignadoAsync(int usuarioId)
        {
            var menus = await _usuarioService.ObtenerMenusConSubMenusAsync();
            var denegaciones = menus
                .SelectMany(menu => menu.SubMenus)
                .Select(subMenu => new OverrideItemDto
                {
                    SubMenuID = subMenu.SubMenuID,
                    Estado = 0
                })
                .ToList();

            if (denegaciones.Any())
            {
                await _usuarioService.GuardarOverridesAsync(usuarioId, null, denegaciones);
            }

            InvalidarCacheMenuSiAplica(usuarioId);
        }

        private void InvalidarCacheMenuSiAplica(int usuarioId)
        {
            HttpContext.Session.Remove("MenuItems");
            HttpContext.Session.Remove("MenuUsuario");

            var usuarioActualId = HttpContext.Session.GetInt32("UsuarioID");
            if (usuarioActualId.HasValue && usuarioActualId.Value == usuarioId)
            {
                TempData["RefreshMenu"] = "true";
            }
        }

        // Recalcula el menú del usuario sin filtrar por empresa.
        private async Task<List<MenuModel>> ObtenerMenuActualizadoAsync(int usuarioId)
        {
            var lista = new List<MenuModel>();
            string? cnn = _context.Database.GetConnectionString();

            if (string.IsNullOrWhiteSpace(cnn))
                return lista;

            await using var conn = new Microsoft.Data.SqlClient.SqlConnection(cnn);
            await conn.OpenAsync();

            const string sql = @"
                WITH Perms AS (
                    SELECT SubMenuID
                    FROM dbo.fn_PermisosEfectivosUsuario(@UsuarioID)
                    WHERE TienePermiso = 1
                )
                SELECT DISTINCT 
                    m.MenuID,
                    m.Nombre AS NombreMenu,
                    ISNULL(sm.UrlEnlace, '') AS UrlEnlace
                FROM dbo.Menus m
                INNER JOIN dbo.SubMenus sm ON sm.MenuID = m.MenuID
                INNER JOIN Perms p ON p.SubMenuID = sm.SubMenuID
                WHERE ISNULL(m.Activo, 1) = 1
                  AND ISNULL(sm.Activo, 1) = 1
                ORDER BY m.MenuID;";

            await using var cmd = new Microsoft.Data.SqlClient.SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@UsuarioID", usuarioId);

            await using var rd = await cmd.ExecuteReaderAsync();

            while (await rd.ReadAsync())
            {
                lista.Add(new MenuModel
                {
                    MenuID = Convert.ToInt32(rd["MenuID"]),
                    Nombre = rd["NombreMenu"]?.ToString() ?? "",
                    Url = rd["UrlEnlace"]?.ToString() ?? ""
                });
            }

            return lista;
        }

        private async Task GuardarDepartamentoUsuarioAsync(int usuarioId, int? departamentoId)
        {
            if (usuarioId <= 0)
                return;

            string? cnn = _context.Database.GetConnectionString();

            if (string.IsNullOrWhiteSpace(cnn))
                return;

            await using var conn = new Microsoft.Data.SqlClient.SqlConnection(cnn);
            await conn.OpenAsync();

            const string sql = @"
                UPDATE dbo.Usuarios
                SET DepartamentoID = @DepartamentoID
                WHERE UsuarioID = @UsuarioID;";

            await using var cmd = new Microsoft.Data.SqlClient.SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@UsuarioID", usuarioId);
            cmd.Parameters.AddWithValue("@DepartamentoID", departamentoId.HasValue && departamentoId.Value > 0
                ? departamentoId.Value
                : DBNull.Value);

            await cmd.ExecuteNonQueryAsync();
        }

        private string ResolverTipoAgregacionDefault(string? tipoCaptura, bool esLinea)
        {
            if (esLinea)
                return "ValorFijo";

            if (!string.IsNullOrWhiteSpace(tipoCaptura) &&
                tipoCaptura.Equals("Fijo", StringComparison.OrdinalIgnoreCase))
                return "ValorFijo";

            return "Promedio";
        }

      

    
    }
    
}