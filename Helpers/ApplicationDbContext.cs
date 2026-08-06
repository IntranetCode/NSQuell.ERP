using ERP.NSQuell.Models;
using ERP.NSQuell.Models.ModelUsuarios;
using Microsoft.EntityFrameworkCore;

// El archivo se encuentra fisicamente en Helpers, pero conserva este namespace
// para no romper los controladores y servicios que ya usan ERP.NSQuell.Models.
namespace ERP.NSQuell.Models
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<UsuarioPerfilViewModel> PerfilUsuarioResults => Set<UsuarioPerfilViewModel>();

        // --- DbSets para el modulo de Usuarios y Auditoria ---
        public DbSet<Usuario> Usuarios { get; set; } = null!;
        public DbSet<Persona> Personas { get; set; } = null!;
        public DbSet<Rol> Roles { get; set; } = null!;

        // --- DbSets ERP ---
        public DbSet<ERPMaquina> ERPMaquinas { get; set; } = null!;
        public DbSet<ERPMaquinaSustituta> ERPMaquinasSustitutas { get; set; } = null!;
        public DbSet<ERPParte> ERPPartes { get; set; } = null!;
        public DbSet<ERPCliente> ERPClientes { get; set; } = null!;
        public DbSet<ERPMolde> ERPMoldes { get; set; } = null!;
        public DbSet<ERPParteDatoTecnico> ERPParteDatosTecnicos { get; set; } = null!;
        public DbSet<ERPMaterial> ERPMateriales { get; set; } = null!;

        public bool TieneDatosTecnicos { get; set; }

        // --- DbSets Calidad ---
        public DbSet<CalidadInspeccion> CalidadInspecciones { get; set; } = null!;
        public DbSet<CalidadInspeccionHistorial> CalidadInspeccionHistorial { get; set; } = null!;
        public DbSet<CalidadPrimeraPiezaIntento> CalidadPrimerasPiezasIntentos { get; set; } = null!;
        public DbSet<CalidadMonitoreoProceso> CalidadMonitoreosProceso { get; set; } = null!;
        public DbSet<CalidadDisposicionMaterial> CalidadDisposicionesMaterial { get; set; } = null!;
        public DbSet<CalidadCajaLiberada> CalidadCajasLiberadas { get; set; } = null!;
        public DbSet<CalidadMuestraResguardo> CalidadMuestrasResguardo { get; set; } = null!;
        public DbSet<CalidadReliberacion> CalidadReliberaciones { get; set; } = null!;
        public DbSet<CalidadCatalogoDefecto> CalidadCatalogoDefectos { get; set; } = null!;
        public DbSet<CalidadGP12> CalidadGP12 { get; set; } = null!;
        public DbSet<CalidadGP12Revision> CalidadGP12Revisiones { get; set; } = null!;
        public DbSet<CalidadGP12Defecto> CalidadGP12Defectos { get; set; } = null!;

        public DbSet<AuditoriaUsuario> AuditoriasUsuarios { get; set; } = null!;
        public DbSet<V_InformacionUsuarioCompleta> InformacionUsuariosCompletos { get; set; } = null!;

        // --- Modulo de permisos ---
        public DbSet<Menu> Menus { get; set; } = null!;
        public DbSet<SubMenu> SubMenus { get; set; } = null!;
        public DbSet<Permiso> Permisos { get; set; } = null!;

        // --- Modulo de notificaciones ---
        public DbSet<Notificacion> Notificaciones { get; set; } = null!;
        public DbSet<NotificacionLectura> NotificacionLecturas { get; set; } = null!;
        public DbSet<NotificacionEmpresas> NotificacionEmpresas { get; set; } = null!;
        public DbSet<PermisosPorRol> PermisosPorRol { get; set; } = null!;
        public DbSet<SubMenuAcciones> SubMenuAcciones { get; set; } = null!;
        public DbSet<Departamento> Departamentos { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // --- Configuraciones modulo Usuarios ---
            modelBuilder.Entity<AuditoriaUsuario>().HasNoKey();
            modelBuilder.Entity<Persona>().ToTable("Persona");

            modelBuilder.Entity<V_InformacionUsuarioCompleta>(entity =>
            {
                entity.HasNoKey();
                entity.ToView("V_InformacionUsuarioCompleta");
            });

            // --- Configuracion maquinas sustitutas ---
            modelBuilder.Entity<ERPMaquinaSustituta>(entity =>
            {
                entity.ToTable("ERP_MaquinasSustitutas");
                entity.HasKey(e => e.MaquinaSustitutaRelacionID);

                entity.Property(e => e.Observaciones)
                    .HasMaxLength(500);

                entity.Property(e => e.Activo)
                    .HasDefaultValue(true);

                entity.Property(e => e.FechaCreacion)
                    .HasColumnType("datetime2(0)")
                    .HasDefaultValueSql("SYSDATETIME()");

                entity.HasOne(e => e.MaquinaPrincipal)
                    .WithMany()
                    .HasForeignKey(e => e.MaquinaPrincipalID)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.MaquinaSustituta)
                    .WithMany()
                    .HasForeignKey(e => e.MaquinaSustitutaID)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // --- Configuraciones modulo Notificaciones ---
            modelBuilder.Entity<NotificacionLectura>()
                .HasIndex(x => new { x.NotificacionId, x.UsuarioId })
                .IsUnique();

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

            // --- Configuracion modulo Calidad ---
            ConfigurarCalidad(modelBuilder);

            // ViewModel sin clave
            modelBuilder.Entity<UsuarioPerfilViewModel>().HasNoKey();
        }

        private static void ConfigurarCalidad(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<CalidadInspeccion>(entity =>
            {
                entity.ToTable("Calidad_Inspecciones");
                entity.HasKey(x => x.InspeccionID);

                entity.Property(x => x.CantidadTotal)
                    .HasColumnType("decimal(18,3)");

                entity.Property(x => x.CantidadRevisada)
                    .HasColumnType("decimal(18,3)");

                entity.Property(x => x.CantidadPendiente)
                    .HasColumnType("decimal(18,3)");

                entity.HasIndex(x => x.EjecucionProduccionID)
                    .IsUnique()
                    .HasFilter("[EjecucionProduccionID] IS NOT NULL");

                entity.HasIndex(x => x.ChecklistArranqueID)
                    .IsUnique()
                    .HasFilter("[ChecklistArranqueID] IS NOT NULL");
            });

            modelBuilder.Entity<CalidadInspeccionHistorial>(entity =>
            {
                entity.ToTable("Calidad_InspeccionHistorial");
                entity.HasKey(x => x.HistorialID);

                entity.HasOne(x => x.Inspeccion)
                    .WithMany(x => x.Historial)
                    .HasForeignKey(x => x.InspeccionID)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<CalidadPrimeraPiezaIntento>(entity =>
            {
                entity.ToTable("Calidad_PrimerasPiezasIntentos");
                entity.HasKey(x => x.IntentoID);

                entity.HasIndex(x => new { x.InspeccionID, x.NumeroIntento })
                    .IsUnique()
                    .HasFilter("[Activo] = 1");

                entity.HasOne(x => x.Inspeccion)
                    .WithMany(x => x.PrimerasPiezasIntentos)
                    .HasForeignKey(x => x.InspeccionID)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<CalidadMonitoreoProceso>(entity =>
            {
                entity.ToTable("Calidad_MonitoreosProceso");
                entity.HasKey(x => x.MonitoreoID);

                entity.HasIndex(x => new { x.InspeccionID, x.NumeroHora })
                    .IsUnique()
                    .HasFilter("[Activo] = 1");

                entity.HasOne(x => x.Inspeccion)
                    .WithMany(x => x.MonitoreosProceso)
                    .HasForeignKey(x => x.InspeccionID)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<CalidadDisposicionMaterial>(entity =>
            {
                entity.ToTable("Calidad_DisposicionesMaterial");
                entity.HasKey(x => x.DisposicionID);

                entity.HasOne(x => x.Inspeccion)
                    .WithMany(x => x.DisposicionesMaterial)
                    .HasForeignKey(x => x.InspeccionID)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(x => x.Monitoreo)
                    .WithMany(x => x.Disposiciones)
                    .HasForeignKey(x => x.MonitoreoID)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<CalidadCajaLiberada>(entity =>
            {
                entity.ToTable("Calidad_CajasLiberadas");
                entity.HasKey(x => x.CajaLiberadaID);

                entity.HasIndex(x => x.FolioCaja)
                    .IsUnique()
                    .HasFilter("[Activo] = 1");

                entity.HasOne(x => x.Inspeccion)
                    .WithMany(x => x.CajasLiberadas)
                    .HasForeignKey(x => x.InspeccionID)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<CalidadMuestraResguardo>(entity =>
            {
                entity.ToTable("Calidad_MuestrasResguardo");
                entity.HasKey(x => x.MuestraResguardoID);

                entity.HasOne(x => x.Inspeccion)
                    .WithMany(x => x.MuestrasResguardo)
                    .HasForeignKey(x => x.InspeccionID)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<CalidadReliberacion>(entity =>
            {
                entity.ToTable("Calidad_Reliberaciones");
                entity.HasKey(x => x.ReliberacionID);

                entity.HasIndex(x => x.ParoID)
                    .IsUnique()
                    .HasFilter("[Activo] = 1");

                entity.HasOne(x => x.Inspeccion)
                    .WithMany(x => x.Reliberaciones)
                    .HasForeignKey(x => x.InspeccionID)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<CalidadCatalogoDefecto>(entity =>
            {
                entity.ToTable("Calidad_CatalogoDefectos");
                entity.HasKey(x => x.CatalogoDefectoID);
                entity.HasIndex(x => x.Codigo).IsUnique();
            });

            modelBuilder.Entity<CalidadGP12>(entity =>
            {
                entity.ToTable("Calidad_GP12");
                entity.HasKey(x => x.GP12ID);

                entity.HasIndex(x => x.CajaLiberadaID)
                    .IsUnique()
                    .HasFilter("[Activo] = 1");

                entity.HasOne(x => x.Inspeccion)
                    .WithMany(x => x.RegistrosGP12)
                    .HasForeignKey(x => x.InspeccionID)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(x => x.CajaLiberada)
                    .WithOne(x => x.RegistroGP12)
                    .HasForeignKey<CalidadGP12>(x => x.CajaLiberadaID)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<CalidadGP12Revision>(entity =>
            {
                entity.ToTable("Calidad_GP12_Revisiones");
                entity.HasKey(x => x.RevisionGP12ID);

                entity.HasIndex(x => new { x.GP12ID, x.NumeroRevision })
                    .IsUnique()
                    .HasFilter("[Activo] = 1");

                entity.HasOne(x => x.GP12)
                    .WithMany(x => x.Revisiones)
                    .HasForeignKey(x => x.GP12ID)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<CalidadGP12Defecto>(entity =>
            {
                entity.ToTable("Calidad_GP12_Defectos");
                entity.HasKey(x => x.DefectoGP12ID);

                entity.HasOne(x => x.Revision)
                    .WithMany(x => x.Defectos)
                    .HasForeignKey(x => x.RevisionGP12ID)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(x => x.CatalogoDefecto)
                    .WithMany(x => x.DefectosGP12)
                    .HasForeignKey(x => x.CatalogoDefectoID)
                    .OnDelete(DeleteBehavior.Restrict);
            });
        }
    }
}
