using System.Data;
using System.Data.SqlClient;
using System.Threading.Tasks;
using System.Threading;

public sealed class BitacoraService
{
    private readonly string _cadenaConexion;

    public BitacoraService(IConfiguration config)
    {
        _cadenaConexion = config.GetConnectionString("DefaultConnection");
    }


    public async Task RegistrarAsync(
        int? idUsuario,
      
        string accion,
        string mensaje = null,
        string resultado = null,   // NULL = usa default 'OK'
        byte? severidad = null ,    // NULL = usa default 1
        string?  modulo = null,
        string? entidad = null,
        string? entidadId = null,
        string? solicitudId = null,
        string?  ip = null,
        string? AgenteUsuario = null,
        CancellationToken ct = default


    )
    {
        using var cn = new SqlConnection(_cadenaConexion);
        using var cmd = new SqlCommand("bitacora.InsertarRegistro", cn)
        { CommandType = CommandType.StoredProcedure };

        cmd.Parameters.AddWithValue("@IdUsuario", (object?)idUsuario ?? DBNull.Value);
      
        cmd.Parameters.AddWithValue("@Accion", accion);
        cmd.Parameters.AddWithValue("@Mensaje", (object?)mensaje ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Resultado", (object?)resultado ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Severidad", (object?)severidad ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Modulo", (object?) modulo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Entidad", (object?) entidad ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@EntidadId", (object?) entidadId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@SolicitudId", (object?) solicitudId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Ip", (object?) ip ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@AgenteUsuario", (object?) AgenteUsuario ?? DBNull.Value);

        await cn.OpenAsync();
        await cmd.ExecuteNonQueryAsync();
    }
}
