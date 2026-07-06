using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ERP.NSQuell.ViewModels.Formularios
{
    public class FormularioPlantillaViewModel
    {
        public int IdFormulario { get; set; }

        public string Nombre { get; set; } = string.Empty;
        public string? Descripcion { get; set; }

        /*
            Categoria se usa como aplicacion del cuestionario:
            Transporte, Guias o Ambos.
        */
        public string? Categoria { get; set; }

        public string Modulo { get; set; } = "Logistica";

        public bool Activo { get; set; } = true;
        public bool EsPlantillaBase { get; set; }

        public DateTime FechaCreacion { get; set; }
        public DateTime? FechaActualizacion { get; set; }

        /*
            Guarda el encabezado oficial del formato:
            area, elaboradoPor, liberadoPor, version, fechaEmision, pagina, codigo.
        */
        public string? DatosFijosPdfJson { get; set; }

        public FormularioDatosOficialesViewModel DatosOficiales { get; set; } = new();

        public List<FormularioCampoViewModel> Campos { get; set; } = new();
    }

    public class FormularioDatosOficialesViewModel
    {
        [JsonPropertyName("area")]
        public string? Area { get; set; }

        [JsonPropertyName("elaboradoPor")]
        public string? ElaboradoPor { get; set; }

        [JsonPropertyName("liberadoPor")]
        public string? LiberadoPor { get; set; }

        [JsonPropertyName("version")]
        public string? Version { get; set; }

        [JsonPropertyName("fechaEmision")]
        public string? FechaEmision { get; set; }

        [JsonPropertyName("pagina")]
        public string? Pagina { get; set; }

        [JsonPropertyName("codigo")]
        public string? Codigo { get; set; }

        public bool TieneDatos()
        {
            return !string.IsNullOrWhiteSpace(Area)
                || !string.IsNullOrWhiteSpace(ElaboradoPor)
                || !string.IsNullOrWhiteSpace(LiberadoPor)
                || !string.IsNullOrWhiteSpace(Version)
                || !string.IsNullOrWhiteSpace(FechaEmision)
                || !string.IsNullOrWhiteSpace(Pagina)
                || !string.IsNullOrWhiteSpace(Codigo);
        }

        public static FormularioDatosOficialesViewModel FromJson(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new FormularioDatosOficialesViewModel();

            try
            {
                return JsonSerializer.Deserialize<FormularioDatosOficialesViewModel>(
                    json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                ) ?? new FormularioDatosOficialesViewModel();
            }
            catch
            {
                return new FormularioDatosOficialesViewModel();
            }
        }
    }

    public class FormularioCampoViewModel
    {
        [JsonPropertyName("clave")]
        public string Clave { get; set; } = string.Empty;

        [JsonPropertyName("etiqueta")]
        public string Etiqueta { get; set; } = string.Empty;

        [JsonPropertyName("tipo")]
        public string Tipo { get; set; } = "texto";

        [JsonPropertyName("obligatorio")]
        public bool Obligatorio { get; set; }

        [JsonPropertyName("copiable")]
        public bool Copiable { get; set; } = true;

        [JsonPropertyName("placeholder")]
        public string? Placeholder { get; set; }

        [JsonPropertyName("opciones")]
        public List<string> Opciones { get; set; } = new();

        /*
            Solo se usa para capturar opciones desde la vista Crear/Editar.
            No se guarda tal cual en JSON; se convierte a Opciones.
        */
        [JsonIgnore]
        public string? OpcionesTexto { get; set; }
    }

    public class FormularioRespuestaViewModel
    {
        public int IdRespuesta { get; set; }
        public int IdFormulario { get; set; }

        public string NombreFormulario { get; set; } = string.Empty;

        public int UsuarioID { get; set; }

        public string Estado { get; set; } = "Registrado";

        public Dictionary<string, string?> Valores { get; set; } = new();

        public int? RespuestaOrigenID { get; set; }

        public string? OrigenTipo { get; set; }
        public int? OrigenID { get; set; }

        public DateTime FechaRegistro { get; set; }
    }

    public class FormularioLlenadoViewModel
    {
        public int IdFormulario { get; set; }

        public string NombreFormulario { get; set; } = string.Empty;
        public string? Descripcion { get; set; }
        public string? Categoria { get; set; }

        public string? DatosFijosPdfJson { get; set; }
        public FormularioDatosOficialesViewModel DatosOficiales { get; set; } = new();

        public List<FormularioCampoViewModel> Campos { get; set; } = new();

        public Dictionary<string, string?> Valores { get; set; } = new();

        public int? RespuestaOrigenID { get; set; }

        public string? OrigenTipo { get; set; }
        public int? OrigenID { get; set; }
    }

    public class FormularioRespuestaResumenViewModel
    {
        public int IdRespuesta { get; set; }
        public int IdFormulario { get; set; }

        public string NombreFormulario { get; set; } = string.Empty;
        public string? Categoria { get; set; }
        public string Modulo { get; set; } = "Logistica";

        public int UsuarioID { get; set; }

        public string? Folio { get; set; }
        public string? Factura { get; set; }
        public string? Cliente { get; set; }
        public string? Proyecto { get; set; }
        public string? Solicitante { get; set; }
        public string? CompaniaSolicitante { get; set; }
        public string? Departamento { get; set; }
        public string? CentroCosto { get; set; }
        public string? FechaCarga { get; set; }
        public string? HorarioCarga { get; set; }
        public string? DireccionRecoleccion { get; set; }
        public string? DestinoPrincipal { get; set; }
        public string? Fletero { get; set; }
        public string? CostoFlete { get; set; }

        public string Estado { get; set; } = "Registrado";

        public string? OrigenTipo { get; set; }
        public int? OrigenID { get; set; }
        public int? RespuestaOrigenID { get; set; }

        public DateTime FechaRegistro { get; set; }
        public DateTime? FechaActualizacion { get; set; }
    }

    public class FormularioRespuestaDetalleViewModel
    {
        public int IdRespuesta { get; set; }

        public int IdFormulario { get; set; }

        public string NombreFormulario { get; set; } = string.Empty;

        public string? Descripcion { get; set; }

        public string? Categoria { get; set; }

        public bool EsPlantillaBase { get; set; }

        public string? DatosFijosPdfJson { get; set; }

        public FormularioDatosOficialesViewModel DatosOficiales { get; set; } = new();

        public DateTime FechaRegistro { get; set; }

        public List<FormularioRespuestaCampoValorViewModel> Campos { get; set; } = new();
    }

    public class FormularioRespuestaCampoValorViewModel
    {
        public string Clave { get; set; } = string.Empty;

        public string Etiqueta { get; set; } = string.Empty;

        public string Tipo { get; set; } = "texto";

        public string? Valor { get; set; }
    }
}
