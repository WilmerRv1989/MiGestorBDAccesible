using MiGestorBDAccesible.Core.Autocompletado;
using MiGestorBDAccesible.Core.Conexiones;
using Microsoft.Data.SqlClient;

namespace MiGestorBDAccesible.Core.Proveedores.SqlServer
{
    public partial class ProveedorSqlServer
    {
        private const string ConsultaCatalogo = """
            SELECT CASE o.type WHEN 'U' THEN 0 WHEN 'V' THEN 1 WHEN 'P' THEN 2 ELSE 3 END,
                   s.name, o.name, c.name, ty.name, c.max_length, c.precision, c.scale
            FROM sys.objects o
            JOIN sys.schemas s ON s.schema_id = o.schema_id
            LEFT JOIN sys.columns c ON c.object_id = o.object_id AND o.type IN ('U', 'V')
            LEFT JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            WHERE o.type IN ('U', 'V', 'P', 'FN', 'IF', 'TF') AND o.is_ms_shipped = 0
            ORDER BY s.name, o.name, c.column_id
            """;

        public async Task<CatalogoModelo> ObtenerCatalogoAsync(ConexionActiva conexion, string? baseDatos, CancellationToken cancelacion = default)
        {
            await using var conexionSql = await AbrirAsync(conexion, baseDatos, cancelacion);
            await using var comando = new SqlCommand(ConsultaCatalogo, conexionSql);
            await using var lector = await comando.ExecuteReaderAsync(cancelacion);

            return await CatalogoModelo.LeerAsync(lector, EsquemaPredeterminado(conexion),
                l => FormatearTipo(l.GetString(4), l.GetInt16(5), l.GetByte(6), l.GetByte(7)), cancelacion);
        }
    }
}
