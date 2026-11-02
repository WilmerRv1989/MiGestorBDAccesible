using MiGestorBDAccesible.Core.Conexiones;
using MiGestorBDAccesible.Core.Estructura;
using Npgsql;

namespace MiGestorBDAccesible.Core.Proveedores.PostgreSql
{
    public partial class ProveedorPostgreSql
    {
        private const string FiltroLenguajeTexto = """
            p.prolang NOT IN (SELECT l.oid FROM pg_language l WHERE l.lanname IN ('c', 'internal'))
            """;

        private const string ColumnasRelacion = """
            CASE c.relkind WHEN 'v' THEN 2 WHEN 'm' THEN 2 ELSE 0 END, n.nspname::text, c.relname::text, NULL::text, NULL::text, NULL::text, c.oid::bigint
            """;

        private const string ColumnasRutina = """
            CASE p.prokind WHEN 'p' THEN 3 ELSE 4 END, n.nspname::text, p.proname::text, NULL::text, NULL::text,
            CASE WHEN (SELECT count(*) FROM pg_proc q WHERE q.pronamespace = p.pronamespace AND q.proname = p.proname) > 1
                 THEN pg_get_function_identity_arguments(p.oid) END,
            p.oid::bigint
            """;

        private const string ColumnasDisparador = """
            12, n.nspname::text, t.tgname::text, n.nspname::text, c.relname::text, NULL::text, t.oid::bigint
            """;

        private static string Palabra(string expresion) =>
            $"'\\m' || regexp_replace({expresion}, '([^[:alnum:]_])', '\\\\\\1', 'g') || '\\M'";

        public async Task<DependenciasModelo> ObtenerDependenciasAsync(
            ConexionActiva conexion,
            string baseDatos,
            ResultadoBusquedaObjetoModelo objetivo,
            CancellationToken cancelacion = default)
        {
            await using var conexionPg = await AbrirAsync(conexion, baseDatos, cancelacion);
            var dependencias = new DependenciasModelo();

            async Task<long?> EscalarAsync(string consulta)
            {
                await using var comando = new NpgsqlCommand(consulta, conexionPg);
                comando.Parameters.AddWithValue("esquema", objetivo.Esquema);
                comando.Parameters.AddWithValue("nombre", objetivo.Nombre);
                comando.Parameters.AddWithValue("tabla_esquema", objetivo.Tabla?.Esquema ?? "");
                comando.Parameters.AddWithValue("tabla_nombre", objetivo.Tabla?.Nombre ?? "");
                return await comando.ExecuteScalarAsync(cancelacion) is long valor ? valor : null;
            }

            async Task LeerAsync(string consulta, long oid, List<DependenciaModelo> destino, bool posible = false)
            {
                await using var comando = new NpgsqlCommand(consulta, conexionPg);
                comando.Parameters.Add(new NpgsqlParameter("oid", NpgsqlTypes.NpgsqlDbType.Oid) { Value = (uint)oid });
                await using var lector = await comando.ExecuteReaderAsync(cancelacion);

                while (await lector.ReadAsync(cancelacion))
                {
                    var tipo = (TipoResultadoBusqueda)lector.GetInt32(0);
                    destino.Add(new DependenciaModelo
                    {
                        Objeto = new ResultadoBusquedaObjetoModelo
                        {
                            Tipo = tipo,
                            Esquema = lector.GetString(1),
                            Nombre = lector.GetString(2),
                            Tabla = lector.IsDBNull(4) ? null : new TablaModelo { Esquema = lector.GetString(3), Nombre = lector.GetString(4), EsquemaPredeterminado = "public" },
                            Detalle = lector.IsDBNull(5) ? null : lector.GetString(5),
                            Id = lector.GetInt64(6)
                        },
                        Posible = posible
                    });
                }
            }

            switch (objetivo.Tipo)
            {
                case TipoResultadoBusqueda.Tabla or TipoResultadoBusqueda.Vista:
                {
                    var oid = objetivo.Id ?? await EscalarAsync("SELECT to_regclass(format('%I.%I', @esquema, @nombre))::oid::bigint");

                    if (oid is not { } r)
                    {
                        break;
                    }

                    await LeerAsync($"""
                        SELECT DISTINCT {ColumnasRelacion}
                        FROM pg_depend d JOIN pg_rewrite w ON w.oid = d.objid JOIN pg_class c ON c.oid = w.ev_class JOIN pg_namespace n ON n.oid = c.relnamespace
                        WHERE d.classid = 'pg_rewrite'::regclass AND d.refclassid = 'pg_class'::regclass AND d.refobjid = @oid AND c.oid <> @oid
                        """, r, dependencias.UsadoPor);

                    await LeerAsync($"""
                        SELECT DISTINCT {ColumnasRutina}
                        FROM pg_depend d JOIN pg_proc p ON p.oid = d.objid JOIN pg_namespace n ON n.oid = p.pronamespace
                        WHERE d.classid = 'pg_proc'::regclass AND d.refclassid = 'pg_class'::regclass AND d.refobjid = @oid
                        """, r, dependencias.UsadoPor);

                    await LeerAsync($"""
                        SELECT DISTINCT {ColumnasRutina}
                        FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace, pg_class c
                        WHERE c.oid = @oid AND p.prosqlbody IS NULL AND {FiltroLenguajeTexto}
                          AND n.nspname NOT IN ('pg_catalog', 'information_schema')
                          AND NOT EXISTS (SELECT 1 FROM pg_depend e WHERE e.objid = p.oid AND e.deptype = 'e')
                          AND p.prosrc ~* ({Palabra("c.relname")})
                        """, r, dependencias.UsadoPor, posible: true);

                    if (objetivo.Tipo == TipoResultadoBusqueda.Tabla)
                    {
                        await LeerAsync($"""
                            SELECT DISTINCT {ColumnasRutina}
                            FROM pg_trigger t JOIN pg_proc p ON p.oid = t.tgfoid JOIN pg_namespace n ON n.oid = p.pronamespace
                            WHERE t.tgrelid = @oid AND NOT t.tgisinternal
                            """, r, dependencias.Usa);
                    }
                    else
                    {
                        await LeerAsync($"""
                            SELECT DISTINCT {ColumnasRelacion}
                            FROM pg_rewrite w JOIN pg_depend d ON d.objid = w.oid AND d.classid = 'pg_rewrite'::regclass
                            JOIN pg_class c ON c.oid = d.refobjid AND d.refclassid = 'pg_class'::regclass JOIN pg_namespace n ON n.oid = c.relnamespace
                            WHERE w.ev_class = @oid AND c.oid <> @oid AND c.relkind IN ('r', 'p', 'v', 'm')
                            """, r, dependencias.Usa);

                        await LeerAsync($"""
                            SELECT DISTINCT {ColumnasRutina}
                            FROM pg_rewrite w JOIN pg_depend d ON d.objid = w.oid AND d.classid = 'pg_rewrite'::regclass
                            JOIN pg_proc p ON p.oid = d.refobjid AND d.refclassid = 'pg_proc'::regclass JOIN pg_namespace n ON n.oid = p.pronamespace
                            WHERE w.ev_class = @oid AND n.nspname NOT IN ('pg_catalog', 'information_schema')
                            """, r, dependencias.Usa);
                    }

                    break;
                }

                case TipoResultadoBusqueda.Procedimiento or TipoResultadoBusqueda.Funcion:
                {
                    if (objetivo.Id is not { } f)
                    {
                        break;
                    }

                    await LeerAsync($"""
                        SELECT DISTINCT {ColumnasDisparador}
                        FROM pg_trigger t JOIN pg_class c ON c.oid = t.tgrelid JOIN pg_namespace n ON n.oid = c.relnamespace
                        WHERE t.tgfoid = @oid AND NOT t.tgisinternal
                        """, f, dependencias.UsadoPor);

                    await LeerAsync($"""
                        SELECT DISTINCT {ColumnasRelacion}
                        FROM pg_depend d JOIN pg_rewrite w ON w.oid = d.objid JOIN pg_class c ON c.oid = w.ev_class JOIN pg_namespace n ON n.oid = c.relnamespace
                        WHERE d.classid = 'pg_rewrite'::regclass AND d.refclassid = 'pg_proc'::regclass AND d.refobjid = @oid
                        """, f, dependencias.UsadoPor);

                    await LeerAsync($"""
                        SELECT DISTINCT {ColumnasRutina}
                        FROM pg_depend d JOIN pg_proc p ON p.oid = d.objid JOIN pg_namespace n ON n.oid = p.pronamespace
                        WHERE d.classid = 'pg_proc'::regclass AND d.refclassid = 'pg_proc'::regclass AND d.refobjid = @oid AND p.oid <> @oid
                        """, f, dependencias.UsadoPor);

                    await LeerAsync($"""
                        SELECT DISTINCT {ColumnasRutina}
                        FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace, pg_proc objetivo
                        WHERE objetivo.oid = @oid AND p.oid <> @oid AND p.prosqlbody IS NULL AND {FiltroLenguajeTexto}
                          AND n.nspname NOT IN ('pg_catalog', 'information_schema')
                          AND NOT EXISTS (SELECT 1 FROM pg_depend e WHERE e.objid = p.oid AND e.deptype = 'e')
                          AND p.prosrc ~* ({Palabra("objetivo.proname")})
                        """, f, dependencias.UsadoPor, posible: true);

                    await LeerAsync($"""
                        SELECT DISTINCT {ColumnasRelacion}
                        FROM pg_depend d JOIN pg_class c ON c.oid = d.refobjid JOIN pg_namespace n ON n.oid = c.relnamespace
                        WHERE d.classid = 'pg_proc'::regclass AND d.objid = @oid AND d.refclassid = 'pg_class'::regclass AND c.relkind IN ('r', 'p', 'v', 'm')
                        """, f, dependencias.Usa);

                    await LeerAsync($"""
                        SELECT DISTINCT {ColumnasRutina}
                        FROM pg_depend d JOIN pg_proc p ON p.oid = d.refobjid JOIN pg_namespace n ON n.oid = p.pronamespace
                        WHERE d.classid = 'pg_proc'::regclass AND d.objid = @oid AND d.refclassid = 'pg_proc'::regclass
                          AND p.oid <> @oid AND n.nspname NOT IN ('pg_catalog', 'information_schema')
                        """, f, dependencias.Usa);

                    await LeerAsync($"""
                        SELECT DISTINCT {ColumnasRelacion}
                        FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace, pg_proc objetivo
                        WHERE objetivo.oid = @oid AND objetivo.prosqlbody IS NULL AND c.relkind IN ('r', 'p', 'v', 'm') AND {FiltroEsquemas}
                          AND objetivo.prosrc ~* ({Palabra("c.relname")})
                        """, f, dependencias.Usa, posible: true);

                    await LeerAsync($"""
                        SELECT DISTINCT {ColumnasRutina}
                        FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace, pg_proc objetivo
                        WHERE objetivo.oid = @oid AND objetivo.prosqlbody IS NULL AND p.oid <> @oid AND {FiltroEsquemas}
                          AND NOT EXISTS (SELECT 1 FROM pg_depend e WHERE e.objid = p.oid AND e.deptype = 'e')
                          AND objetivo.prosrc ~* ({Palabra("p.proname")})
                        """, f, dependencias.Usa, posible: true);

                    break;
                }

                case TipoResultadoBusqueda.Disparador when objetivo.Tabla is not null:
                {
                    var oid = await EscalarAsync("""
                        SELECT t.oid::bigint FROM pg_trigger t
                        WHERE t.tgname = @nombre AND t.tgrelid = to_regclass(format('%I.%I', @tabla_esquema, @tabla_nombre))
                        """);

                    if (oid is not { } t)
                    {
                        break;
                    }

                    await LeerAsync($"""
                        SELECT DISTINCT {ColumnasRutina}
                        FROM pg_trigger t JOIN pg_proc p ON p.oid = t.tgfoid JOIN pg_namespace n ON n.oid = p.pronamespace
                        WHERE t.oid = @oid
                        """, t, dependencias.Usa);

                    break;
                }
            }

            var seguros = dependencias.UsadoPor.Concat(dependencias.Usa).Where(d => !d.Posible).Select(d => d.Objeto!.Id).ToHashSet();
            dependencias.UsadoPor.RemoveAll(d => d.Posible && seguros.Contains(d.Objeto!.Id));
            dependencias.Usa.RemoveAll(d => d.Posible && seguros.Contains(d.Objeto!.Id));
            return dependencias;
        }
    }
}
