using MiGestorBDAccesible.Core.Conexiones;
using MiGestorBDAccesible.Core.Estructura;
using Oracle.ManagedDataAccess.Client;

namespace MiGestorBDAccesible.Core.Proveedores.Oracle
{
    public partial class ProveedorOracle
    {
        private const string FiltroTablaBusqueda = """
            t.nested = 'NO' AND t.secondary = 'N' AND t.dropped = 'NO' AND t.table_name NOT LIKE 'BIN$%'
            """;

        private static readonly string ConsultaBusqueda = $"""
            SELECT 0 tipo, t.owner esquema, t.table_name nombre, CAST(NULL AS VARCHAR2(128)) tabla_esquema,
                   CAST(NULL AS VARCHAR2(128)) tabla_nombre, CAST(NULL AS VARCHAR2(400)) detalle
            FROM all_tables t WHERE t.owner = :esquema AND {FiltroTablaBusqueda}
            UNION ALL
            SELECT 1, c.owner, c.column_name, c.owner, c.table_name, c.data_type
            FROM all_tab_columns c JOIN all_tables t ON t.owner = c.owner AND t.table_name = c.table_name
            WHERE c.owner = :esquema AND {FiltroTablaBusqueda}
            UNION ALL
            SELECT 2, owner, object_name, NULL, NULL, NULL
            FROM all_objects WHERE owner = :esquema AND object_type IN ('VIEW', 'MATERIALIZED VIEW')
            UNION ALL
            SELECT CASE object_type WHEN 'PROCEDURE' THEN 3 WHEN 'FUNCTION' THEN 4 ELSE 5 END, owner, object_name, NULL, NULL, NULL
            FROM all_objects WHERE owner = :esquema AND object_type IN ('PROCEDURE', 'FUNCTION', 'PACKAGE')
            UNION ALL
            SELECT DISTINCT 6, owner, procedure_name, NULL, NULL, object_name
            FROM all_procedures WHERE owner = :esquema AND object_type = 'PACKAGE' AND procedure_name IS NOT NULL
            UNION ALL
            SELECT 7, i.owner, i.index_name, i.table_owner, i.table_name, NULL
            FROM all_indexes i JOIN all_tables t ON t.owner = i.table_owner AND t.table_name = i.table_name
            WHERE i.table_owner = :esquema AND i.index_type <> 'LOB' AND {FiltroTablaBusqueda}
              AND NOT EXISTS (SELECT 1 FROM all_constraints k
                              WHERE k.owner = i.table_owner AND k.index_name = i.index_name AND k.constraint_type IN ('P', 'U'))
            UNION ALL
            SELECT CASE k.constraint_type WHEN 'P' THEN 8 WHEN 'R' THEN 9 WHEN 'U' THEN 10 ELSE 11 END,
                   k.owner, k.constraint_name, k.owner, k.table_name, NULL
            FROM all_constraints k JOIN all_tables t ON t.owner = k.owner AND t.table_name = k.table_name
            WHERE k.owner = :esquema AND k.constraint_type IN ('P', 'R', 'U', 'C') AND {FiltroTablaBusqueda}
              AND NOT (k.constraint_type = 'C' AND REGEXP_LIKE(NVL(k.search_condition_vc, ' '), '^\s*"[^"]+"\s+IS\s+NOT\s+NULL\s*$', 'i'))
            UNION ALL
            SELECT 12, owner, trigger_name, CASE WHEN base_object_type = 'TABLE' THEN table_owner END,
                   CASE WHEN base_object_type = 'TABLE' THEN table_name END, NULL
            FROM all_triggers WHERE owner = :esquema AND base_object_type IN ('TABLE', 'SCHEMA', 'DATABASE')
            UNION ALL
            SELECT 13, sequence_owner, sequence_name, NULL, NULL, NULL
            FROM all_sequences WHERE sequence_owner = :esquema AND sequence_name NOT LIKE 'ISEQ$$%'
            """;

        public async Task<List<ResultadoBusquedaObjetoModelo>> BuscarObjetosAsync(
            ConexionActiva conexion,
            string baseDatos,
            string texto,
            CategoriaBusqueda categoria,
            CancellationToken cancelacion = default)
        {
            await using var conexionOracle = await AbrirAsync(conexion, cancelacion);
            await using var comando = new OracleCommand($"""
                SELECT tipo, esquema, nombre, tabla_esquema, tabla_nombre, detalle
                FROM ({ConsultaBusqueda})
                WHERE UPPER(nombre) LIKE UPPER(:contiene) ESCAPE '\' AND tipo IN ({BuscadorObjetosService.ListaTipos(categoria)})
                ORDER BY CASE WHEN UPPER(nombre) = UPPER(:texto) THEN 0 WHEN UPPER(nombre) LIKE UPPER(:empieza) ESCAPE '\' THEN 1 ELSE 2 END,
                         tipo, esquema, tabla_nombre, nombre
                FETCH FIRST {BuscadorObjetosService.Maximo + 1} ROWS ONLY
                """, conexionOracle) { BindByName = true };
            comando.Parameters.Add("esquema", baseDatos);
            comando.Parameters.Add("contiene", BuscadorObjetosService.Contiene(texto));
            comando.Parameters.Add("texto", texto);
            comando.Parameters.Add("empieza", BuscadorObjetosService.EmpiezaPor(texto));

            await using var lector = await comando.ExecuteReaderAsync(cancelacion);
            var resultados = new List<ResultadoBusquedaObjetoModelo>();

            while (await lector.ReadAsync(cancelacion))
            {
                resultados.Add(new ResultadoBusquedaObjetoModelo
                {
                    Tipo = (TipoResultadoBusqueda)lector.GetInt32(0),
                    Esquema = lector.GetString(1),
                    Nombre = lector.GetString(2),
                    Tabla = lector.IsDBNull(4) ? null : new TablaModelo { Esquema = lector.GetString(3), Nombre = lector.GetString(4), EsquemaPredeterminado = baseDatos },
                    Detalle = lector.IsDBNull(5) ? null : lector.GetString(5)
                });
            }

            return resultados;
        }
    }
}
