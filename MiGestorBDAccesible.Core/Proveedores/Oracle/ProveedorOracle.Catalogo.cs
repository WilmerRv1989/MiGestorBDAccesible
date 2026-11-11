using MiGestorBDAccesible.Core.Autocompletado;
using MiGestorBDAccesible.Core.Conexiones;
using System.Data.Common;

namespace MiGestorBDAccesible.Core.Proveedores.Oracle
{
    public partial class ProveedorOracle
    {
        private const string ConsultaCatalogo = """
            SELECT CASE o.object_type WHEN 'TABLE' THEN 0 WHEN 'VIEW' THEN 1 WHEN 'PROCEDURE' THEN 2 ELSE 3 END,
                   o.owner, o.object_name, c.column_name, c.data_type, c.data_length, c.char_length, c.char_used,
                   c.data_precision, c.data_scale
            FROM all_objects o
            LEFT JOIN all_tab_cols c
                   ON c.owner = o.owner AND c.table_name = o.object_name AND c.hidden_column = 'NO'
                  AND o.object_type IN ('TABLE', 'VIEW')
            WHERE o.owner = :esquema
              AND o.object_type IN ('TABLE', 'VIEW', 'PROCEDURE', 'FUNCTION')
              AND o.object_name NOT LIKE 'BIN$%'
            ORDER BY o.object_name, c.column_id
            """;

        public async Task<CatalogoModelo> ObtenerCatalogoAsync(ConexionActiva conexion, string? baseDatos, CancellationToken cancelacion = default)
        {
            await using var conexionOracle = await AbrirAsync(conexion, cancelacion);
            await using var comando = conexionOracle.CreateCommand();
            comando.BindByName = true;
            comando.CommandText = ConsultaCatalogo;
            comando.Parameters.Add("esquema", string.IsNullOrWhiteSpace(baseDatos) ? EsquemaPredeterminado(conexion) : baseDatos);
            await using var lector = await comando.ExecuteReaderAsync(cancelacion);

            return await CatalogoModelo.LeerAsync(lector, EsquemaPredeterminado(conexion),
                l => FormatearTipo(l.GetString(4), EnteroCatalogo(l, 5), EnteroCatalogo(l, 6), l.IsDBNull(7) ? null : l.GetString(7), EnteroCatalogo(l, 8), EnteroCatalogo(l, 9)),
                cancelacion);
        }

        private static int? EnteroCatalogo(DbDataReader lector, int indice) =>
            lector.IsDBNull(indice) ? null : Convert.ToInt32(lector.GetValue(indice));
    }
}
