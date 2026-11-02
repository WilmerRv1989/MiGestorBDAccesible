using MiGestorBDAccesible.Core.Conexiones;
using MiGestorBDAccesible.Core.Estructura;
using Microsoft.Data.SqlClient;

namespace MiGestorBDAccesible.Core.Proveedores.SqlServer
{
    public partial class ProveedorSqlServer
    {
        private const string ConsultaBusqueda = """
            SELECT CAST(0 AS int) tipo, SCHEMA_NAME(t.schema_id) esquema, t.name nombre,
                   CAST(NULL AS sysname) tabla_esquema, CAST(NULL AS sysname) tabla_nombre, CAST(NULL AS nvarchar(400)) detalle, CAST(t.object_id AS bigint) id
            FROM sys.tables t WHERE t.is_ms_shipped = 0
            UNION ALL
            SELECT 1, SCHEMA_NAME(t.schema_id), c.name, SCHEMA_NAME(t.schema_id), t.name, TYPE_NAME(c.user_type_id), NULL
            FROM sys.columns c JOIN sys.tables t ON t.object_id = c.object_id WHERE t.is_ms_shipped = 0
            UNION ALL
            SELECT 2, SCHEMA_NAME(schema_id), name, NULL, NULL, NULL, object_id FROM sys.views WHERE is_ms_shipped = 0
            UNION ALL
            SELECT 3, SCHEMA_NAME(schema_id), name, NULL, NULL, NULL, object_id FROM sys.procedures WHERE is_ms_shipped = 0
            UNION ALL
            SELECT 4, SCHEMA_NAME(schema_id), name, NULL, NULL, NULL, object_id FROM sys.objects WHERE type IN ('FN', 'IF', 'TF', 'FS', 'FT') AND is_ms_shipped = 0
            UNION ALL
            SELECT 7, SCHEMA_NAME(t.schema_id), i.name, SCHEMA_NAME(t.schema_id), t.name, NULL, NULL
            FROM sys.indexes i JOIN sys.tables t ON t.object_id = i.object_id
            WHERE i.name IS NOT NULL AND i.is_primary_key = 0 AND i.is_unique_constraint = 0 AND t.is_ms_shipped = 0
            UNION ALL
            SELECT CASE k.type WHEN 'PK' THEN 8 ELSE 10 END, SCHEMA_NAME(t.schema_id), k.name, SCHEMA_NAME(t.schema_id), t.name, NULL, NULL
            FROM sys.key_constraints k JOIN sys.tables t ON t.object_id = k.parent_object_id WHERE t.is_ms_shipped = 0
            UNION ALL
            SELECT 9, SCHEMA_NAME(t.schema_id), f.name, SCHEMA_NAME(t.schema_id), t.name, NULL, NULL
            FROM sys.foreign_keys f JOIN sys.tables t ON t.object_id = f.parent_object_id WHERE t.is_ms_shipped = 0
            UNION ALL
            SELECT 11, SCHEMA_NAME(t.schema_id), k.name, SCHEMA_NAME(t.schema_id), t.name, NULL, NULL
            FROM sys.check_constraints k JOIN sys.tables t ON t.object_id = k.parent_object_id WHERE t.is_ms_shipped = 0
            UNION ALL
            SELECT 12, CASE WHEN t.object_id IS NULL THEN N'' ELSE SCHEMA_NAME(t.schema_id) END, tr.name, SCHEMA_NAME(t.schema_id), t.name, NULL, tr.object_id
            FROM sys.triggers tr LEFT JOIN sys.tables t ON t.object_id = tr.parent_id
            WHERE tr.is_ms_shipped = 0 AND (tr.parent_class = 0 OR t.object_id IS NOT NULL)
            UNION ALL
            SELECT 13, SCHEMA_NAME(schema_id), name, NULL, NULL, NULL, object_id FROM sys.sequences
            """;

        public async Task<List<ResultadoBusquedaObjetoModelo>> BuscarObjetosAsync(
            ConexionActiva conexion,
            string baseDatos,
            string texto,
            CategoriaBusqueda categoria,
            CancellationToken cancelacion = default)
        {
            await using var conexionSql = await AbrirAsync(conexion, baseDatos, cancelacion);
            await using var comando = new SqlCommand($"""
                SELECT TOP ({BuscadorObjetosService.Maximo + 1}) tipo, esquema, nombre, tabla_esquema, tabla_nombre, detalle, id
                FROM ({ConsultaBusqueda}) x
                WHERE UPPER(nombre) LIKE UPPER(@contiene) ESCAPE '\' AND tipo IN ({BuscadorObjetosService.ListaTipos(categoria)})
                ORDER BY CASE WHEN UPPER(nombre) = UPPER(@texto) THEN 0 WHEN UPPER(nombre) LIKE UPPER(@empieza) ESCAPE '\' THEN 1 ELSE 2 END,
                         tipo, esquema, tabla_nombre, nombre
                """, conexionSql);
            comando.Parameters.AddWithValue("@contiene", BuscadorObjetosService.Contiene(texto));
            comando.Parameters.AddWithValue("@empieza", BuscadorObjetosService.EmpiezaPor(texto));
            comando.Parameters.AddWithValue("@texto", texto);

            await using var lector = await comando.ExecuteReaderAsync(cancelacion);
            var resultados = new List<ResultadoBusquedaObjetoModelo>();

            while (await lector.ReadAsync(cancelacion))
            {
                resultados.Add(new ResultadoBusquedaObjetoModelo
                {
                    Tipo = (TipoResultadoBusqueda)lector.GetInt32(0),
                    Esquema = lector.GetString(1),
                    Nombre = lector.GetString(2),
                    Tabla = lector.IsDBNull(4) ? null : new TablaModelo { Esquema = lector.GetString(3), Nombre = lector.GetString(4) },
                    Detalle = lector.IsDBNull(5) ? null : lector.GetString(5),
                    Id = lector.IsDBNull(6) ? null : lector.GetInt64(6)
                });
            }

            return resultados;
        }
    }
}
