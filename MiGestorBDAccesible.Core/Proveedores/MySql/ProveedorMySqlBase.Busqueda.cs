using MiGestorBDAccesible.Core.Conexiones;
using MiGestorBDAccesible.Core.Estructura;
using MySqlConnector;

namespace MiGestorBDAccesible.Core.Proveedores.MySql
{
    public abstract partial class ProveedorMySqlBase
    {
        private const string ConsultaBusqueda = """
            SELECT 0 tipo, table_name nombre, NULL tabla_nombre, NULL detalle
            FROM information_schema.tables
            WHERE table_schema = @base AND table_type = 'BASE TABLE'
            UNION ALL
            SELECT 1, c.column_name, c.table_name, c.column_type
            FROM information_schema.columns c
            JOIN information_schema.tables t ON t.table_schema = c.table_schema AND t.table_name = c.table_name AND t.table_type = 'BASE TABLE'
            WHERE c.table_schema = @base
            UNION ALL
            SELECT DISTINCT 7, s.index_name, s.table_name, NULL
            FROM information_schema.statistics s
            WHERE s.table_schema = @base AND s.index_name <> 'PRIMARY'
              AND NOT EXISTS (SELECT 1 FROM information_schema.table_constraints tc
                              WHERE tc.table_schema = s.table_schema AND tc.table_name = s.table_name
                                AND tc.constraint_name = s.index_name AND tc.constraint_type = 'UNIQUE')
            UNION ALL
            SELECT CASE constraint_type WHEN 'PRIMARY KEY' THEN 8 WHEN 'FOREIGN KEY' THEN 9 WHEN 'UNIQUE' THEN 10 ELSE 11 END,
                   constraint_name, table_name, NULL
            FROM information_schema.table_constraints
            WHERE table_schema = @base AND constraint_type IN ('PRIMARY KEY', 'FOREIGN KEY', 'UNIQUE', 'CHECK')
            UNION ALL
            SELECT 2, table_name, NULL, NULL FROM information_schema.views WHERE table_schema = @base
            UNION ALL
            SELECT CASE routine_type WHEN 'PROCEDURE' THEN 3 ELSE 4 END, routine_name, NULL, NULL
            FROM information_schema.routines WHERE routine_schema = @base
            UNION ALL
            SELECT 12, trigger_name, event_object_table, NULL FROM information_schema.triggers WHERE trigger_schema = @base
            UNION ALL
            SELECT 13, table_name, NULL, NULL FROM information_schema.tables WHERE table_schema = @base AND table_type = 'SEQUENCE'
            """;

        public async Task<List<ResultadoBusquedaObjetoModelo>> BuscarObjetosAsync(
            ConexionActiva conexion,
            string baseDatos,
            string texto,
            CategoriaBusqueda categoria,
            CancellationToken cancelacion = default)
        {
            await using var conexionMy = await AbrirAsync(conexion, cancelacion);
            await using var comando = new MySqlCommand($"""
                SELECT tipo, nombre, tabla_nombre, detalle
                FROM ({ConsultaBusqueda}) x
                WHERE UPPER(nombre) LIKE UPPER(@contiene) ESCAPE '\\' AND tipo IN ({BuscadorObjetosService.ListaTipos(categoria)})
                ORDER BY CASE WHEN UPPER(nombre) = UPPER(@texto) THEN 0 WHEN UPPER(nombre) LIKE UPPER(@empieza) ESCAPE '\\' THEN 1 ELSE 2 END,
                         tipo, CAST(LOWER(tabla_nombre) AS BINARY), CAST(LOWER(nombre) AS BINARY)
                LIMIT {BuscadorObjetosService.Maximo + 1}
                """, conexionMy);
            comando.Parameters.AddWithValue("@base", baseDatos);
            comando.Parameters.AddWithValue("@contiene", BuscadorObjetosService.Contiene(texto));
            comando.Parameters.AddWithValue("@empieza", BuscadorObjetosService.EmpiezaPor(texto));
            comando.Parameters.AddWithValue("@texto", texto);

            await using var lector = await comando.ExecuteReaderAsync(cancelacion);
            var resultados = new List<ResultadoBusquedaObjetoModelo>();

            while (await lector.ReadAsync(cancelacion))
            {
                resultados.Add(new ResultadoBusquedaObjetoModelo
                {
                    Tipo = (TipoResultadoBusqueda)Convert.ToInt32(lector.GetValue(0)),
                    Esquema = "",
                    Nombre = lector.GetString(1),
                    Tabla = lector.IsDBNull(2) ? null : new TablaModelo { Esquema = "", Nombre = lector.GetString(2), EsquemaPredeterminado = "" },
                    Detalle = lector.IsDBNull(3) ? null : FormatearTipo(lector.GetString(3))
                });
            }

            return resultados;
        }
    }
}
