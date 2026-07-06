using System;
using System.IO;
using System.Linq;                  
using Microsoft.Extensions.Configuration;

namespace ERP.NSQuell.Helpers
{

    //Helper para construir y crear rutas físicas en el NAS a partir de una sola raíz configurada en .json 
    
    public class RutaNas
    {
        private readonly IConfiguration _config;

        public RutaNas(IConfiguration config)
        {
            _config = config;
        }

 
        //Lee la raíz UNC del NAS desde .json
        public string ObtenerRutaBaseNAS()
        {
            string? ruta = _config["Ruta:NAS"]?.Trim();
            if (string.IsNullOrWhiteSpace(ruta))
                throw new InvalidOperationException("Configura RUTAS en appsettings.json (Ruta:NAS).");

            if (!ruta.StartsWith(@"\\"))
               throw new InvalidOperationException($"La ruta NAS no es UNC: {ruta}");

            return ruta;
        }

        private static string CombinarRuta(params string[] partes)
            => Path.Combine(partes).Replace("\\", "/");

    
        // Devuelve ewl nombre de la carpeta con un nombre predefinifo
        public string ObtenerNombreCarpetaProyecto(int proyectoId, string nombreProyecto)
        {
            // Quitar caracteres inválidos para Windows
            var nombreLimpio = string.Concat(
                (nombreProyecto ?? string.Empty)
                .Where(ch => !Path.GetInvalidFileNameChars().Contains(ch)));

            // Reemplazar espacios por guiones y mayúsculas
            nombreLimpio = nombreLimpio.Replace(" ", "-").ToUpperInvariant().Trim('-');

            return $"PRY-{proyectoId}-{nombreLimpio}";
        }


        // Ruta física en NAS para la carpeta raíz del proyecto.
        public string ObtenerRutaProyecto(int proyectoId, string nombreProyecto)
        {
            var baseNas = ObtenerRutaBaseNAS();
            var nombreCarpeta = ObtenerNombreCarpetaProyecto(proyectoId, nombreProyecto);
            return CombinarRuta(baseNas, "Proyectos", nombreCarpeta);
        }


        // Si la carpeta raiz ne exite se crea
        public string CrearCarpetaRaizProyecto(int proyectoId, string nombreProyecto)
        {
            var rutaProyecto = ObtenerRutaProyecto(proyectoId, nombreProyecto)
                               .Replace("/", Path.DirectorySeparatorChar.ToString());

            if (!Directory.Exists(rutaProyecto))
                Directory.CreateDirectory(rutaProyecto);

            return rutaProyecto;
        }

        // Carpeta de Comunicados

        public string ObtenerRutaComunicado(string nombreCarpeta)
        {
            var baseNas = ObtenerRutaBaseNAS();
            return CombinarRuta(baseNas, "Comunicados", nombreCarpeta);
        }

        // Si se necesitan mas carpetas, en este apartado se agregan


        // Agregando carpeta de compras
        public string ObtenerRutaUnicaCompras()
        {
            // Esto devolverá siempre "Compras/Cotizaciones"
            // Sin importar el ID de la solicitud.
            return CombinarRuta("Compras", "Cotizaciones");
        }

        public string CrearCarpetaUnicaCompras()
        {
            var baseNas = ObtenerRutaBaseNAS();
            var rutaRelativa = ObtenerRutaUnicaCompras();

            var rutaFisica = Path.Combine(baseNas, rutaRelativa)
                                 .Replace("/", Path.DirectorySeparatorChar.ToString());

            if (!Directory.Exists(rutaFisica))
                Directory.CreateDirectory(rutaFisica);

            return rutaFisica;
        }

        // Carpeta para archivos de referencia de solicitudes de compra
        public string ObtenerRutaSolicitudesCompras()
        {
            return CombinarRuta("Compras", "Solicitudes");
        }

        public string CrearCarpetaSolicitudesCompras()
        {
            var baseNas = ObtenerRutaBaseNAS();
            var rutaRelativa = ObtenerRutaSolicitudesCompras();

            var rutaFisica = Path.Combine(baseNas, rutaRelativa)
                .Replace("/", Path.DirectorySeparatorChar.ToString());

            if (!Directory.Exists(rutaFisica))
                Directory.CreateDirectory(rutaFisica);

            return rutaFisica;
        }

        public string ObtenerRutaComprasBase()
        {
            return ObtenerRutaSolicitudesCompras();
        }

        public string ObtenerRutaComprasReferencias()
        {
            return $"{ObtenerRutaComprasBase()}/Referencias";
        }

        public string ObtenerRutaComprasCotizaciones()
        {
            return $"{ObtenerRutaComprasBase()}/Cotizaciones";
        }

        public string ObtenerRutaComprasDesviaciones()
        {
            return $"{ObtenerRutaComprasBase()}/Desviaciones";
        }

        public string ObtenerRutaComprasRequisiciones()
        {
            return $"{ObtenerRutaComprasBase()}/Requisiciones";
        }

        public string ObtenerRutaComprasOC()
        {
            return $"{ObtenerRutaComprasBase()}/OC";
        }

        public string ObtenerRutaComprasEvidenciaAlmacen()
        {
            return $"{ObtenerRutaComprasBase()}/EvidenciaAlmacen";
        }

        public string ObtenerRutaComprasEntregaUsuario()
        {
            return $"{ObtenerRutaComprasBase()}/EntregaUsuario";
        }
    }
}
