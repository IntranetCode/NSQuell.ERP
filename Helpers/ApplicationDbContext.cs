using Microsoft.EntityFrameworkCore;
using ERP.NSQuell.Models;
using ERP.NSQuell.Models.ModelUsuarios;

// Este es el namespace donde residen tus modelos y el DbContext
namespace ERP.NSQuell.Models
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
        {
        }

        public DbSet<UsuarioPerfilViewModel> PerfilUsuarioResults => Set<UsuarioPerfilViewModel>();



        // --- DbSets para el módulo de Usuarios y Auditoría ---
        public DbSet<Usuario> Usuarios { get; set; }
        public DbSet<Persona> Personas { get; set; }
        public DbSet<Rol> Roles { get; set; }
        public DbSet<ERPMaquina> ERPMaquinas { get; set; } = null!;
        public DbSet<ERPParte> ERPPartes { get; set; } = null!;
        public DbSet<ERPCliente> ERPClientes { get; set; } = null!;
        public DbSet<ERPMolde> ERPMoldes { get; set; } = null!;
        public bool TieneDatosTecnicos { get; set; }
        public DbSet<ERPParteDatoTecnico> ERPParteDatosTecnicos { get; set; } = null!;
        public DbSet<ERPMaterial> ERPMateriales { get; set; } = null!;

        public DbSet<CalidadInspeccion> CalidadInspecciones { get; set; } = null!;
        public DbSet<CalidadInspeccionHistorial> CalidadInspeccionHistorial { get; set; } = null!;

        public DbSet<AuditoriaUsuario> AuditoriasUsuarios { get; set; }
        public DbSet<V_InformacionUsuarioCompleta> InformacionUsuariosCompletos { get; set; }

        // --- AÑADIDOS PARA EL MÓDULO DE PERMISOS ---
        public DbSet<Menu> Menus { get; set; }
        public DbSet<SubMenu> SubMenus { get; set; }
        public DbSet<Permiso> Permisos { get; set; }
        // ---------------------------------------------


        // DbSets para el módulo de Notificaciones
        public DbSet<Notificacion> Notificaciones { get; set; }
        public DbSet<NotificacionLectura> NotificacionLecturas { get; set; }
        public DbSet<NotificacionEmpresas> NotificacionEmpresas { get; set; }
        public DbSet<PermisosPorRol> PermisosPorRol { get; set; }
        public DbSet<SubMenuAcciones> SubMenuAcciones { get; set; }
        public DbSet<Departamento> Departamentos { get; set; } 
       

        
       
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // --- CONFIGURACIONES MÓDULO USUARIOS ---
            modelBuilder.Entity<AuditoriaUsuario>().HasNoKey();
            modelBuilder.Entity<Persona>().ToTable("Persona");

            modelBuilder.Entity<V_InformacionUsuarioCompleta>(entity =>
            {
                entity.HasNoKey();
                entity.ToView("V_InformacionUsuarioCompleta");
            });

           

            // --- CONFIGURACIONES MÓDULO NOTIFICACIONES ---
            modelBuilder.Entity<NotificacionLectura>()
                .HasIndex(x => new { x.NotificacionId, x.UsuarioId }).IsUnique();

            modelBuilder.Entity<NotificacionLectura>()
                .HasOne(x => x.Notificacion)
                .WithMany()
                .HasForeignKey(x => x.NotificacionId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<NotificacionEmpresas>()
                .HasIndex(x => new { x.EmpresaId, x.NotificacionId });

            modelBuilder.Entity<NotificacionEmpresas>()
                .HasOne(x => x.Notificacion)
                .WithMany()
                .HasForeignKey(x => x.NotificacionId)
                .OnDelete(DeleteBehavior.Cascade);

            // Configurar el ViewModel como sin clave (keyless)
            modelBuilder.Entity<UsuarioPerfilViewModel>().HasNoKey();
        }
    }
}