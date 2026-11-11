using MiGestorBDAccesible.Core.Autocompletado;
using MiGestorBDAccesible.Core.Conexiones;
using Npgsql;

namespace MiGestorBDAccesible.Core.Proveedores.PostgreSql
{
    public partial class ProveedorPostgreSql
    {
        private static readonly string ConsultaCatalogo = $"""
            SELECT CASE WHEN o.relkind IN ('r', 'p') THEN 0 ELSE 1 END, n.nspname::text, o.relname::text,
                   a.attname::text, format_type(a.atttypid, a.atttypmod), a.attnum
            FROM pg_class o
            JOIN pg_namespace n ON n.oid = o.relnamespace
            LEFT JOIN pg_attribute a ON a.attrelid = o.oid AND a.attnum > 0 AND NOT a.attisdropped
            WHERE o.relkind IN ('r', 'p', 'v', 'm') AND NOT o.relispartition AND {FiltroEsquemas} AND {FiltroExtension}
            UNION ALL
            SELECT CASE o.prokind WHEN 'p' THEN 2 ELSE 3 END, n.nspname::text, o.proname::text, NULL, NULL, NULL
            FROM pg_proc o
            JOIN pg_namespace n ON n.oid = o.pronamespace
            WHERE o.prokind IN ('p', 'f') AND {FiltroEsquemas} AND {FiltroExtension}
            ORDER BY 2, 3, 6
            """;

        public async Task<CatalogoModelo> ObtenerCatalogoAsync(ConexionActiva conexion, string? baseDatos, CancellationToken cancelacion = default)
        {
            await using var conexionPg = await AbrirAsync(conexion, baseDatos, cancelacion);
            await using var comando = new NpgsqlCommand(ConsultaCatalogo, conexionPg);
            await using var lector = await comando.ExecuteReaderAsync(cancelacion);

            return await CatalogoModelo.LeerAsync(lector, EsquemaPredeterminado(conexion), l => FormatearTipo(l.GetString(4)), cancelacion);
        }
    }
}
