namespace ERP.NSQuell.Models;

// NSQ_NOTIFICACIONES_V3E_DEPARTAMENTOS
// El destinatario se resuelve UNICAMENTE por Usuario.Activo + DepartamentoID,
// salvo los eventos globales que llegan a todos los usuarios activos.
public sealed class NotificacionEvento
{
    public string CodigoEvento { get; init; } = string.Empty;
    public string Tipo { get; init; } = string.Empty;
    public string Titulo { get; init; } = string.Empty;
    public string? Mensaje { get; init; }
    public int IdOrigen { get; init; }
    public string TablaOrigen { get; init; } = string.Empty;
    public string? UrlDestino { get; init; }
    public bool EnviarNavbar { get; init; } = true;
    public bool TodosUsuariosActivos { get; init; }
    public IReadOnlyCollection<int> DepartamentosDestinoIds { get; init; } = Array.Empty<int>();
    public IReadOnlyCollection<int> UsuariosDestinoIds { get; init; } = Array.Empty<int>();
    public int? ActorUsuarioID { get; init; }
    public DateTime FechaEvento { get; init; } = DateTime.Now;
}
