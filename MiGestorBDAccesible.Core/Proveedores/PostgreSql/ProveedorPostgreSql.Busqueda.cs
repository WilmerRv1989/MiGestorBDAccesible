using MiGestorBDAccesible.Core.Conexiones;
using MiGestorBDAccesible.Core.Estructura;
using Npgsql;

namespace MiGestorBDAccesible.Core.Proveedores.PostgreSql
{
    public partial class ProveedorPostgreSql
    {
        private const string FiltroTablaBusqueda = """
            c.relkind IN ('r', 'p') AND NOT c.relispartition
            """;

        private static readonly string ConsultaBusqueda = $"""
            SELECT 0 tipo, n.nspname::text esquema, c.relname::text nombre, NULL::text tabla_esquema, NULL::text tabla_nombre, NULL::text detalle, c.oid::bigint id
            FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE {FiltroTablaBusqueda} AND {FiltroEsquemas}
            UNION ALL
            SELECT 1, n.nspname, a.attname, n.nspname, c.relname, format_type(a.atttypid, a.atttypmod), NULL
            FROM pg_attribute a JOIN pg_class c ON c.oid = a.attrelid JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE {FiltroTablaBusqueda} AND a.attnum > 0 AND NOT a.attisdropped AND {FiltroEsquemas}
            UNION ALL
            SELECT 2, n.nspname, o.relname, NULL, NULL, NULL, o.oid::bigint
            FROM pg_class o JOIN pg_namespace n ON n.oid = o.relnamespace
            WHERE o.relkind IN ('v', 'm') AND {FiltroEsquemas} AND {FiltroExtension}
            UNION ALL
            SELECT CASE o.prokind WHEN 'p' THEN 3 ELSE 4 END, n.nspname, o.proname,
                   NULL, NULL,
                   CASE WHEN count(*) OVER (PARTITION BY n.nspname, o.proname) > 1 THEN pg_get_function_identity_arguments(o.oid) END,
                   o.oid::bigint
            FROM pg_proc o JOIN pg_namespace n ON n.oid = o.pronamespace
            WHERE o.prokind IN ('p', 'f') AND {FiltroEsquemas} AND {FiltroExtension}
            UNION ALL
            SELECT 7, n.nspname, i.relname, n.nspname, c.relname, NULL, NULL
            FROM pg_index x JOIN pg_class i ON i.oid = x.indexrelid JOIN pg_class c ON c.oid = x.indrelid JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE {FiltroTablaBusqueda} AND {FiltroEsquemas}
              AND NOT EXISTS (SELECT 1 FROM pg_constraint k WHERE k.conindid = x.indexrelid AND k.conrelid = x.indrelid AND k.contype IN ('p', 'u', 'x'))
            UNION ALL
            SELECT CASE k.contype WHEN 'p' THEN 8 WHEN 'f' THEN 9 WHEN 'u' THEN 10 ELSE 11 END, n.nspname, k.conname, n.nspname, c.relname, NULL, NULL
            FROM pg_constraint k JOIN pg_class c ON c.oid = k.conrelid JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE k.contype IN ('p', 'f', 'u', 'c') AND {FiltroTablaBusqueda} AND {FiltroEsquemas}
            UNION ALL
            SELECT 12, n.nspname, t.tgname, n.nspname, c.relname, NULL, t.oid::bigint
            FROM pg_trigger t JOIN pg_class c ON c.oid = t.tgrelid JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE NOT t.tgisinternal AND {FiltroTablaBusqueda} AND {FiltroEsquemas}
            UNION ALL
            SELECT 12, '', o.evtname, NULL, NULL, NULL, o.oid::bigint
            FROM pg_event_trigger o
            WHERE {FiltroExtension}
            UNION ALL
            SELECT 13, n.nspname, o.relname, NULL, NULL, NULL, o.oid::bigint
            FROM pg_class o JOIN pg_namespace n ON n.oid = o.relnamespace
            WHERE o.relkind = 'S' AND {FiltroEsquemas} AND {FiltroExtension} AND {FiltroIdentidad}
            """;

        public async Task<List<ResultadoBusquedaObjetoModelo>> BuscarObjetosAsync(
            ConexionActiva conexion,
            string baseDatos,
            string texto,
            CategoriaBusqueda categoria,
            CancellationToken cancelacion = default)
        {
            await using var conexionPg = await AbrirAsync(conexion, baseDatos, cancelacion);
            await using var comando = new NpgsqlCommand($"""
                SELECT tipo, esquema, nombre, tabla_esquema, tabla_nombre, detalle, id
                FROM ({ConsultaBusqueda}) x
                WHERE upper(nombre) LIKE upper(@contiene) ESCAPE '\' AND tipo IN ({BuscadorObjetosService.ListaTipos(categoria)})
                ORDER BY CASE WHEN upper(nombre) = upper(@texto) THEN 0 WHEN upper(nombre) LIKE upper(@empieza) ESCAPE '\' THEN 1 ELSE 2 END,
                         tipo, esquema, tabla_nombre, nombre
                LIMIT {BuscadorObjetosService.Maximo + 1}
                """, conexionPg);
            comando.Parameters.AddWithValue("contiene", BuscadorObjetosService.Contiene(texto));
            comando.Parameters.AddWithValue("empieza", BuscadorObjetosService.EmpiezaPor(texto));
            comando.Parameters.AddWithValue("texto", texto);

            await using var lector = await comando.ExecuteReaderAsync(cancelacion);
            var resultados = new List<ResultadoBusquedaObjetoModelo>();

            while (await lector.ReadAsync(cancelacion))
            {
                resultados.Add(new ResultadoBusquedaObjetoModelo
                {
                    Tipo = (TipoResultadoBusqueda)lector.GetInt32(0),
                    Esquema = lector.GetString(1),
                    Nombre = lector.GetString(2),
                    Tabla = lector.IsDBNull(4) ? null : new TablaModelo { Esquema = lector.GetString(3), Nombre = lector.GetString(4), EsquemaPredeterminado = "public" },
                    Detalle = lector.IsDBNull(5) ? null : lector.GetString(5),
                    Id = lector.IsDBNull(6) ? null : lector.GetInt64(6)
                });
            }

            return resultados;
        }
    }
}
