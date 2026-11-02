using MiGestorBDAccesible.Core.Conexiones;
using MiGestorBDAccesible.Core.Estructura;
using Microsoft.Data.SqlClient;

namespace MiGestorBDAccesible.Core.Proveedores.SqlServer
{
    public partial class ProveedorSqlServer
    {
        private const string TipoDependencia = """
            CASE RTRIM(o.type) WHEN 'U' THEN 0 WHEN 'V' THEN 2 WHEN 'P' THEN 3 WHEN 'PC' THEN 3
                 WHEN 'FN' THEN 4 WHEN 'IF' THEN 4 WHEN 'TF' THEN 4 WHEN 'FS' THEN 4 WHEN 'FT' THEN 4
                 WHEN 'TR' THEN 12 WHEN 'SO' THEN 13 END
            """;

        public async Task<DependenciasModelo> ObtenerDependenciasAsync(
            ConexionActiva conexion,
            string baseDatos,
            ResultadoBusquedaObjetoModelo objetivo,
            CancellationToken cancelacion = default)
        {
            await using var conexionSql = await AbrirAsync(conexion, baseDatos, cancelacion);
            var id = objetivo.Id;

            if (id is null)
            {
                await using var buscar = new SqlCommand("SELECT CAST(OBJECT_ID(@nombre) AS bigint)", conexionSql);
                buscar.Parameters.AddWithValue("@nombre", $"[{objetivo.Esquema.Replace("]", "]]")}].[{objetivo.Nombre.Replace("]", "]]")}]");
                id = await buscar.ExecuteScalarAsync(cancelacion) as long?;
            }

            var dependencias = new DependenciasModelo();

            if (id is null)
            {
                return dependencias;
            }

            await using (var usadoPor = new SqlCommand($"""
                SELECT DISTINCT {TipoDependencia}, SCHEMA_NAME(o.schema_id), o.name,
                       OBJECT_SCHEMA_NAME(o.parent_object_id), OBJECT_NAME(o.parent_object_id), CAST(o.object_id AS bigint)
                FROM sys.sql_expression_dependencies d JOIN sys.objects o ON o.object_id = d.referencing_id
                WHERE d.referenced_id = @id AND d.referencing_id <> @id AND d.referencing_class = 1
                """, conexionSql))
            {
                usadoPor.Parameters.AddWithValue("@id", (int)id.Value);
                await using var lector = await usadoPor.ExecuteReaderAsync(cancelacion);

                while (await lector.ReadAsync(cancelacion))
                {
                    if (!lector.IsDBNull(0))
                    {
                        dependencias.UsadoPor.Add(new DependenciaModelo { Objeto = LeerObjetoDependencia(lector, 0) });
                    }
                }
            }

            await using (var usa = new SqlCommand($"""
                SELECT DISTINCT {TipoDependencia}, SCHEMA_NAME(o.schema_id), o.name,
                       OBJECT_SCHEMA_NAME(o.parent_object_id), OBJECT_NAME(o.parent_object_id), CAST(o.object_id AS bigint),
                       d.referenced_schema_name, d.referenced_entity_name
                FROM sys.sql_expression_dependencies d LEFT JOIN sys.objects o ON o.object_id = d.referenced_id
                WHERE d.referencing_id = @id AND d.referenced_class = 1
                  AND d.referenced_server_name IS NULL
                  AND (d.referenced_database_name IS NULL OR d.referenced_database_name = DB_NAME())
                  AND ISNULL(d.referenced_id, 0) <> @id
                """, conexionSql))
            {
                usa.Parameters.AddWithValue("@id", (int)id.Value);
                await using var lector = await usa.ExecuteReaderAsync(cancelacion);

                while (await lector.ReadAsync(cancelacion))
                {
                    if (!lector.IsDBNull(5) && !lector.IsDBNull(0))
                    {
                        dependencias.Usa.Add(new DependenciaModelo { Objeto = LeerObjetoDependencia(lector, 0) });
                    }
                    else if (lector.IsDBNull(5))
                    {
                        var esquema = lector.IsDBNull(6) ? "" : $"{lector.GetString(6)}.";
                        dependencias.Usa.Add(new DependenciaModelo { Inexistente = esquema + lector.GetString(7) });
                    }
                }
            }

            return dependencias;
        }

        private static ResultadoBusquedaObjetoModelo LeerObjetoDependencia(SqlDataReader lector, int inicio)
        {
            var tipo = (TipoResultadoBusqueda)lector.GetInt32(inicio);
            var conTabla = tipo == TipoResultadoBusqueda.Disparador && !lector.IsDBNull(inicio + 4);

            return new ResultadoBusquedaObjetoModelo
            {
                Tipo = tipo,
                Esquema = lector.GetString(inicio + 1),
                Nombre = lector.GetString(inicio + 2),
                Tabla = conTabla ? new TablaModelo { Esquema = lector.GetString(inicio + 3), Nombre = lector.GetString(inicio + 4) } : null,
                Id = lector.GetInt64(inicio + 5)
            };
        }
    }
}
