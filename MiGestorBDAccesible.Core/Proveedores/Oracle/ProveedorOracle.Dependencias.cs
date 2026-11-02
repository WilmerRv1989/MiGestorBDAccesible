using MiGestorBDAccesible.Core.Conexiones;
using MiGestorBDAccesible.Core.Estructura;
using Oracle.ManagedDataAccess.Client;

namespace MiGestorBDAccesible.Core.Proveedores.Oracle
{
    public partial class ProveedorOracle
    {
        private static string TipoOracle(TipoResultadoBusqueda tipo) => tipo switch
        {
            TipoResultadoBusqueda.Tabla => "TABLE",
            TipoResultadoBusqueda.Vista => "VIEW",
            TipoResultadoBusqueda.Procedimiento => "PROCEDURE",
            TipoResultadoBusqueda.Funcion => "FUNCTION",
            TipoResultadoBusqueda.Paquete => "PACKAGE",
            TipoResultadoBusqueda.Secuencia => "SEQUENCE",
            _ => "TRIGGER"
        };

        private static string TipoDependenciaOracle(string columna) => $"""
            CASE {columna} WHEN 'TABLE' THEN 0 WHEN 'VIEW' THEN 2 WHEN 'MATERIALIZED VIEW' THEN 2 WHEN 'PROCEDURE' THEN 3
                 WHEN 'FUNCTION' THEN 4 WHEN 'PACKAGE' THEN 5 WHEN 'PACKAGE BODY' THEN 5 WHEN 'TRIGGER' THEN 12 WHEN 'SEQUENCE' THEN 13 END
            """;

        public async Task<DependenciasModelo> ObtenerDependenciasAsync(
            ConexionActiva conexion,
            string baseDatos,
            ResultadoBusquedaObjetoModelo objetivo,
            CancellationToken cancelacion = default)
        {
            await using var conexionOracle = await AbrirAsync(conexion, cancelacion);
            var tipo = TipoOracle(objetivo.Tipo);
            var tiposPropios = objetivo.Tipo == TipoResultadoBusqueda.Paquete ? "'PACKAGE', 'PACKAGE BODY'" : $"'{tipo}'";
            var dependencias = new DependenciasModelo();

            async Task LeerAsync(string consulta, List<DependenciaModelo> destino)
            {
                await using var comando = new OracleCommand(consulta, conexionOracle) { BindByName = true };
                comando.Parameters.Add("esquema", objetivo.Esquema);
                comando.Parameters.Add("nombre", objetivo.Nombre);
                comando.Parameters.Add("tipo", tipo);
                await using var lector = await comando.ExecuteReaderAsync(cancelacion);

                while (await lector.ReadAsync(cancelacion))
                {
                    if (lector.IsDBNull(0))
                    {
                        continue;
                    }

                    var esquema = lector.GetString(1);
                    destino.Add(new DependenciaModelo
                    {
                        Objeto = new ResultadoBusquedaObjetoModelo
                        {
                            Tipo = (TipoResultadoBusqueda)lector.GetInt32(0),
                            Esquema = esquema,
                            Nombre = lector.GetString(2),
                            Tabla = lector.IsDBNull(4) ? null : new TablaModelo { Esquema = lector.GetString(3), Nombre = lector.GetString(4), EsquemaPredeterminado = baseDatos }
                        },
                        OtroEsquema = !string.Equals(esquema, baseDatos, StringComparison.OrdinalIgnoreCase)
                    });
                }
            }

            await LeerAsync($"""
                SELECT DISTINCT {TipoDependenciaOracle("d.type")}, d.owner, d.name, t.table_owner, t.table_name
                FROM all_dependencies d
                LEFT JOIN all_triggers t ON d.type = 'TRIGGER' AND t.owner = d.owner AND t.trigger_name = d.name AND t.base_object_type = 'TABLE'
                WHERE d.referenced_owner = :esquema AND d.referenced_name = :nombre AND d.referenced_type = :tipo
                  AND NOT (d.owner = :esquema AND d.name = :nombre)
                """, dependencias.UsadoPor);

            await LeerAsync($"""
                SELECT DISTINCT {TipoDependenciaOracle("d.referenced_type")}, d.referenced_owner, d.referenced_name, t.table_owner, t.table_name
                FROM all_dependencies d
                LEFT JOIN all_triggers t ON d.referenced_type = 'TRIGGER' AND t.owner = d.referenced_owner AND t.trigger_name = d.referenced_name AND t.base_object_type = 'TABLE'
                WHERE d.owner = :esquema AND d.name = :nombre AND d.type IN ({tiposPropios})
                  AND d.referenced_owner NOT IN ('SYS', 'PUBLIC')
                  AND NOT (d.referenced_owner = :esquema AND d.referenced_name = :nombre)
                """, dependencias.Usa);

            return dependencias;
        }
    }
}
