using System.Text.Json.Serialization;

namespace ERP.NSQuell.Models
{
    public class VisualizacionModel
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }
    }
}
