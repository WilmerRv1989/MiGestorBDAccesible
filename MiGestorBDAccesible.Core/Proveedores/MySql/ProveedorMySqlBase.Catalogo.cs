using MiGestorBDAccesible.Core.Autocompletado;
using MiGestorBDAccesible.Core.Conexiones;
using MySqlConnector;

namespace MiGestorBDAccesible.Core.Proveedores.MySql
{
    public abstract partial class ProveedorMySqlBase
    {
        private const string ConsultaCatalogo = """
            SELECT CASE t.table_type WHEN 'VIEW' THEN 1 ELSE 0 END, '', t.table_name, c.column_name, c.column_type, c.ordinal_position
            FROM information_schema.tables t
            LEFT JOIN information_schema.columns c ON c.table_schema = t.table_schema AND c.table_name = t.table_name
            WHERE t.table_schema = @base AND t.table_type IN ('BASE TABLE', 'VIEW')
            UNION ALL
            SELECT CASE routine_type WHEN 'PROCEDURE' THEN 2 ELSE 3 END, '', routine_name, NULL, NULL, NULL
            FROM information_schema.routines
            WHERE routine_schema = @base
            ORDER BY 3, 6
            """;

        public async Task<CatalogoModelo> ObtenerCatalogoAsync(ConexionActiva conexion, string? baseDatos, CancellationToken cancelacion = default)
        {
            if (string.IsNullOrWhiteSpace(baseDatos))
            {
                return new CatalogoModelo();
            }

            await using var conexionMy = await AbrirAsync(conexion, cancelacion);
            await using var comando = new MySqlCommand(ConsultaCatalogo, conexionMy);
            comando.Parameters.AddWithValue("@base", baseDatos);
            await using var lector = await comando.ExecuteReaderAsync(cancelacion);

            return await CatalogoModelo.LeerAsync(lector, "", l => FormatearTipo(l.GetString(4)), cancelacion);
        }
    }
}
