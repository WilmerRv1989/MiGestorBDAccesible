using MiGestorBDAccesible.Core.Conexiones;
using MiGestorBDAccesible.Core.Estructura;
using Npgsql;
using System.Data.Common;
using System.Text;
using System.Text.RegularExpressions;

namespace MiGestorBDAccesible.Core.Proveedores.PostgreSql
{
    public partial class ProveedorPostgreSql
    {
        private const string FiltroEsquemas = """
            n.nspname NOT IN ('pg_catalog', 'information_schema')
            AND n.nspname NOT LIKE 'pg_toast%'
            AND n.nspname NOT LIKE 'pg_temp%'
            """;

        private const string FiltroExtension = """
            NOT EXISTS (SELECT 1 FROM pg_depend d
                        WHERE d.objid = o.oid AND d.deptype = 'e')
            """;

        private const string FiltroIdentidad = """
            NOT EXISTS (SELECT 1 FROM pg_depend d
                        WHERE d.classid = 'pg_class'::regclass AND d.objid = o.oid AND d.deptype = 'i')
            """;

        private static readonly Regex _crearDisparador = new(@"^CREATE TRIGGER\b");

        private static readonly Regex _partesDisparador = new(@" (?=(BEFORE|AFTER|INSTEAD OF|FOR EACH|WHEN \(|EXECUTE FUNCTION) )");

        private static readonly HashSet<string> _tiposSinComillas = new(StringComparer.Ordinal)
        {
            "smallint", "integer", "bigint", "numeric", "decimal", "real", "double precision", "oid"
        };

        public async Task<Dictionary<TipoObjeto, int>> ContarObjetosAsync(
            ConexionActiva conexion,
            string baseDatos,
            CancellationToken cancelacion = default)
        {
            await using var conexionPg = await AbrirAsync(conexion, baseDatos, cancelacion);
            await using var comando = new NpgsqlCommand($"""
                SELECT
                    (SELECT count(*)::int FROM pg_class o JOIN pg_namespace n ON n.oid = o.relnamespace
                     WHERE o.relkind IN ('v', 'm') AND {FiltroEsquemas} AND {FiltroExtension}),
                    (SELECT count(*)::int FROM pg_proc o JOIN pg_namespace n ON n.oid = o.pronamespace
                     WHERE o.prokind = 'p' AND {FiltroEsquemas} AND {FiltroExtension}),
                    (SELECT count(*)::int FROM pg_proc o JOIN pg_namespace n ON n.oid = o.pronamespace
                     WHERE o.prokind = 'f' AND {FiltroEsquemas} AND {FiltroExtension}),
                    (SELECT count(*)::int FROM pg_class o JOIN pg_namespace n ON n.oid = o.relnamespace
                     WHERE o.relkind = 'S' AND {FiltroEsquemas} AND {FiltroExtension} AND {FiltroIdentidad}),
                    (SELECT count(*)::int FROM pg_event_trigger o WHERE {FiltroExtension})
                """, conexionPg);
            await using var lector = await comando.ExecuteReaderAsync(cancelacion);
            await lector.ReadAsync(cancelacion);

            return new Dictionary<TipoObjeto, int>
            {
                [TipoObjeto.Vista] = lector.GetInt32(0),
                [TipoObjeto.Procedimiento] = lector.GetInt32(1),
                [TipoObjeto.Funcion] = lector.GetInt32(2),
                [TipoObjeto.Secuencia] = lector.GetInt32(3),
                [TipoObjeto.Disparador] = lector.GetInt32(4)
            };
        }

        public async Task<List<ObjetoBaseDatosModelo>> ObtenerObjetosAsync(
            ConexionActiva conexion,
            string baseDatos,
            TipoObjeto tipo,
            CancellationToken cancelacion = default)
        {
            var consulta = tipo switch
            {
                TipoObjeto.Vista => $"""
                    SELECT n.nspname, o.relname, o.oid::bigint, o.relkind = 'm', NULL::text
                    FROM pg_class o JOIN pg_namespace n ON n.oid = o.relnamespace
                    WHERE o.relkind IN ('v', 'm') AND {FiltroEsquemas} AND {FiltroExtension}
                    ORDER BY 1, 2
                    """,
                TipoObjeto.Procedimiento or TipoObjeto.Funcion => $"""
                    SELECT n.nspname, o.proname, o.oid::bigint, false,
                           CASE WHEN count(*) OVER (PARTITION BY n.nspname, o.proname) > 1
                                THEN pg_get_function_identity_arguments(o.oid) END
                    FROM pg_proc o JOIN pg_namespace n ON n.oid = o.pronamespace
                    WHERE o.prokind = '{(tipo == TipoObjeto.Procedimiento ? "p" : "f")}' AND {FiltroEsquemas} AND {FiltroExtension}
                    ORDER BY 1, 2, 5
                    """,
                TipoObjeto.Secuencia => $"""
                    SELECT n.nspname, o.relname, o.oid::bigint, false, NULL::text
                    FROM pg_class o JOIN pg_namespace n ON n.oid = o.relnamespace
                    WHERE o.relkind = 'S' AND {FiltroEsquemas} AND {FiltroExtension} AND {FiltroIdentidad}
                    ORDER BY 1, 2
                    """,
                TipoObjeto.Disparador => $"""
                    SELECT '', o.evtname, o.oid::bigint, false, NULL::text
                    FROM pg_event_trigger o
                    WHERE {FiltroExtension}
                    ORDER BY 2
                    """,
                _ => null
            };

            if (consulta is null)
            {
                return [];
            }

            await using var conexionPg = await AbrirAsync(conexion, baseDatos, cancelacion);
            await using var comando = new NpgsqlCommand(consulta, conexionPg);

            return await LeerObjetosAsync(comando, tipo, null, cancelacion);
        }

        public async Task<List<ObjetoBaseDatosModelo>> ObtenerDisparadoresTablaAsync(
            ConexionActiva conexion,
            string baseDatos,
            TablaModelo tabla,
            CancellationToken cancelacion = default)
        {
            await using var conexionPg = await AbrirAsync(conexion, baseDatos, cancelacion);
            await using var comando = new NpgsqlCommand("""
                SELECT @esquema, t.tgname, t.oid::bigint, false, NULL::text
                FROM pg_trigger t
                WHERE t.tgrelid = to_regclass(quote_ident(@esquema) || '.' || quote_ident(@nombre))
                  AND NOT t.tgisinternal
                ORDER BY t.tgname
                """, conexionPg);
            comando.Parameters.AddWithValue("esquema", tabla.Esquema);
            comando.Parameters.AddWithValue("nombre", tabla.Nombre);

            return await LeerObjetosAsync(comando, TipoObjeto.Disparador, tabla, cancelacion);
        }

        public async Task<DetalleObjetoModelo> ObtenerDetalleObjetoAsync(
            ConexionActiva conexion,
            string baseDatos,
            ObjetoBaseDatosModelo objeto,
            CancellationToken cancelacion = default)
        {
            await using var conexionPg = await AbrirAsync(conexion, baseDatos, cancelacion);
            var id = await IdObjetoAsync(conexionPg, objeto, cancelacion);

            return objeto.Tipo switch
            {
                TipoObjeto.Vista => new DetalleObjetoModelo { Objeto = objeto, Columnas = await LeerColumnasObjetoAsync(conexionPg, id, cancelacion) },
                TipoObjeto.Procedimiento or TipoObjeto.Funcion => await LeerRutinaAsync(conexionPg, objeto, id, cancelacion),
                TipoObjeto.Disparador when objeto.Tabla is null => await LeerDisparadorEventoAsync(conexionPg, objeto, id, cancelacion),
                TipoObjeto.Disparador => await LeerDisparadorAsync(conexionPg, objeto, id, cancelacion),
                TipoObjeto.Secuencia => await LeerSecuenciaAsync(conexionPg, objeto, cancelacion),
                _ => new DetalleObjetoModelo { Objeto = objeto }
            };
        }

        public async Task<string> GenerarScriptObjetoAsync(
            ConexionActiva conexion,
            string baseDatos,
            ObjetoBaseDatosModelo objeto,
            CancellationToken cancelacion = default)
        {
            await using var conexionPg = await AbrirAsync(conexion, baseDatos, cancelacion);
            var id = await IdObjetoAsync(conexionPg, objeto, cancelacion);
            var nombre = NombreObjeto(objeto);

            switch (objeto.Tipo)
            {
                case TipoObjeto.Vista:
                    var definicion = (string)(await EscalarIdAsync(conexionPg, "SELECT pg_get_viewdef(@id, true)", id, cancelacion))!;
                    var cabecera = objeto.Materializada ? $"CREATE MATERIALIZED VIEW {nombre} AS" : $"CREATE OR REPLACE VIEW {nombre} AS";
                    return $"{cabecera}{Environment.NewLine}{AjustarLineas(definicion).Trim().TrimEnd(';')};{Environment.NewLine}";
                case TipoObjeto.Procedimiento or TipoObjeto.Funcion:
                    var codigo = (string)(await EscalarIdAsync(conexionPg, "SELECT pg_get_functiondef(@id)", id, cancelacion))!;
                    return $"{AjustarLineas(codigo).TrimEnd()};{Environment.NewLine}";
                case TipoObjeto.Disparador when objeto.Tabla is null:
                    var evento = await LeerDisparadorEventoAsync(conexionPg, objeto, id, cancelacion);
                    var etiquetas = evento.Etiquetas.Count > 0 ? $"{Environment.NewLine}    WHEN TAG IN ({string.Join(", ", evento.Etiquetas.Select(n => $"'{n.Replace("'", "''")}'"))})" : "";
                    return $"CREATE EVENT TRIGGER {Identificador(objeto.Nombre)}{Environment.NewLine}    ON {evento.Eventos.Single()}{etiquetas}{Environment.NewLine}    EXECUTE FUNCTION {evento.FuncionDisparador}();{Environment.NewLine}";
                case TipoObjeto.Disparador:
                    var disparador = (string)(await EscalarIdAsync(conexionPg, "SELECT pg_get_triggerdef(@id, true)", id, cancelacion))!;
                    var lineas = _partesDisparador.Replace(_crearDisparador.Replace(disparador, "CREATE OR REPLACE TRIGGER", 1), Environment.NewLine + "    ", 4);
                    return $"{lineas};{Environment.NewLine}";
                default:
                    var secuencia = await LeerSecuenciaAsync(conexionPg, objeto, cancelacion);
                    return $"""
                        CREATE SEQUENCE {nombre}
                            AS {secuencia.Retorno}
                            START WITH {secuencia.Inicio}
                            INCREMENT BY {secuencia.Incremento}
                            MINVALUE {secuencia.Minimo}
                            MAXVALUE {secuencia.Maximo}
                            {(secuencia.Ciclo == true ? "CYCLE" : "NO CYCLE")};

                        """;
            }
        }

        public string ScriptEliminarObjeto(ObjetoBaseDatosModelo objeto) => objeto.Tipo switch
        {
            TipoObjeto.Vista when objeto.Materializada => $"DROP MATERIALIZED VIEW {NombreObjeto(objeto)};",
            TipoObjeto.Vista => $"DROP VIEW {NombreObjeto(objeto)};",
            TipoObjeto.Procedimiento => $"DROP PROCEDURE {NombreObjeto(objeto)}{(objeto.Firma is null ? "" : $"({objeto.Firma})")};",
            TipoObjeto.Funcion => $"DROP FUNCTION {NombreObjeto(objeto)}{(objeto.Firma is null ? "" : $"({objeto.Firma})")};",
            TipoObjeto.Secuencia => $"DROP SEQUENCE {NombreObjeto(objeto)};",
            TipoObjeto.Disparador when objeto.Tabla is null => $"DROP EVENT TRIGGER {Identificador(objeto.Nombre)};",
            _ => $"DROP TRIGGER {Identificador(objeto.Nombre)} ON {NombreTabla(objeto.Tabla!)};"
        };

        public string ScriptHabilitarDisparador(ObjetoBaseDatosModelo disparador, bool habilitar) =>
            disparador.Tabla is null
                ? $"ALTER EVENT TRIGGER {Identificador(disparador.Nombre)} {(habilitar ? "ENABLE" : "DISABLE")};"
                : $"ALTER TABLE {NombreTabla(disparador.Tabla)} {(habilitar ? "ENABLE" : "DISABLE")} TRIGGER {Identificador(disparador.Nombre)};";

        public string ScriptReiniciarSecuencia(ObjetoBaseDatosModelo secuencia, long valor) =>
            $"ALTER SEQUENCE {NombreObjeto(secuencia)} RESTART WITH {valor};";

        public string ScriptLlamada(DetalleObjetoModelo detalle, IReadOnlyDictionary<string, string?> valores)
        {
            var objeto = detalle.Objeto;
            var argumentos = string.Join(", ", detalle.Parametros.Select(p => p.Direccion == DireccionParametro.Salida
                ? objeto.Tipo == TipoObjeto.Procedimiento ? "NULL" : null
                : LiteralParametro(p, valores)).OfType<string>());
            var llamada = $"{NombreObjeto(objeto)}({argumentos})";

            if (objeto.Tipo == TipoObjeto.Procedimiento)
            {
                return $"CALL {llamada};";
            }

            var devuelveFilas = detalle.DevuelveTabla || detalle.Parametros.Any(p => p.Direccion != DireccionParametro.Entrada);
            return devuelveFilas ? $"SELECT *{Environment.NewLine}FROM {llamada};" : $"SELECT {llamada} AS resultado;";
        }

        public string ScriptSeleccionarPrimeras(DetalleObjetoModelo vista) =>
            $"""
            SELECT
            {string.Join($",{Environment.NewLine}", vista.Columnas.Select(c => $"    {Identificador(c.Nombre)}"))}
            FROM {NombreObjeto(vista.Objeto)}
            LIMIT 100;
            """;

        private static string NombreObjeto(ObjetoBaseDatosModelo objeto) =>
            objeto.Esquema is "public" or "" ? Identificador(objeto.Nombre) : $"{Identificador(objeto.Esquema)}.{Identificador(objeto.Nombre)}";

        private static string AjustarLineas(string texto) => texto.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);

        private static string LiteralParametro(ParametroModelo parametro, IReadOnlyDictionary<string, string?> valores)
        {
            var valor = valores.TryGetValue(parametro.Nombre, out var escrito) ? escrito?.Trim() : null;

            if (string.IsNullOrEmpty(valor) || valor.Equals("NULL", StringComparison.OrdinalIgnoreCase))
            {
                return "NULL";
            }

            if (valor.Equals("DEFAULT", StringComparison.OrdinalIgnoreCase))
            {
                return "DEFAULT";
            }

            var tipo = parametro.TipoDatos.Split('(')[0];

            if (tipo == "boolean")
            {
                return valor.ToLowerInvariant() switch
                {
                    "sí" or "si" or "true" or "verdadero" or "1" => "true",
                    "no" or "false" or "falso" or "0" => "false",
                    _ => valor
                };
            }

            return _tiposSinComillas.Contains(tipo) ? valor : $"'{valor.Replace("'", "''")}'";
        }

        private static async Task<List<ObjetoBaseDatosModelo>> LeerObjetosAsync(
            NpgsqlCommand comando,
            TipoObjeto tipo,
            TablaModelo? tabla,
            CancellationToken cancelacion)
        {
            await using var lector = await comando.ExecuteReaderAsync(cancelacion);
            var objetos = new List<ObjetoBaseDatosModelo>();

            while (await lector.ReadAsync(cancelacion))
            {
                objetos.Add(new ObjetoBaseDatosModelo
                {
                    Tipo = tipo,
                    Esquema = lector.GetString(0),
                    Nombre = lector.GetString(1),
                    Id = lector.GetInt64(2),
                    Materializada = lector.GetBoolean(3),
                    Firma = lector.IsDBNull(4) ? null : lector.GetString(4),
                    Tabla = tabla
                });
            }

            return objetos;
        }

        private static async Task<long> IdObjetoAsync(NpgsqlConnection conexion, ObjetoBaseDatosModelo objeto, CancellationToken cancelacion)
        {
            var consulta = objeto.Tipo switch
            {
                TipoObjeto.Procedimiento or TipoObjeto.Funcion when objeto.Firma is not null =>
                    "SELECT to_regprocedure(quote_ident(@esquema) || '.' || quote_ident(@nombre) || '(' || @firma || ')')::oid::bigint",
                TipoObjeto.Procedimiento or TipoObjeto.Funcion =>
                    "SELECT p.oid::bigint FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace WHERE n.nspname = @esquema AND p.proname = @nombre",
                TipoObjeto.Disparador when objeto.Tabla is null => "SELECT oid::bigint FROM pg_event_trigger WHERE evtname = @nombre",
                TipoObjeto.Disparador =>
                    "SELECT oid::bigint FROM pg_trigger WHERE tgname = @nombre AND tgrelid = to_regclass(quote_ident(@esquema) || '.' || quote_ident(@tabla))",
                _ => "SELECT to_regclass(quote_ident(@esquema) || '.' || quote_ident(@nombre))::oid::bigint"
            };

            await using var comando = new NpgsqlCommand(consulta, conexion);
            comando.Parameters.AddWithValue("esquema", objeto.Tabla?.Esquema ?? objeto.Esquema);
            comando.Parameters.AddWithValue("nombre", objeto.Nombre);
            comando.Parameters.AddWithValue("firma", objeto.Firma ?? "");
            comando.Parameters.AddWithValue("tabla", objeto.Tabla?.Nombre ?? "");

            return await comando.ExecuteScalarAsync(cancelacion) is long id
                ? id
                : throw new InvalidOperationException($"{objeto.NombreCompleto} ya no existe en la base de datos.");
        }

        private static async Task<object?> EscalarIdAsync(NpgsqlConnection conexion, string consulta, long id, CancellationToken cancelacion)
        {
            await using var comando = new NpgsqlCommand(consulta, conexion);
            comando.Parameters.AddWithValue("id", NpgsqlTypes.NpgsqlDbType.Oid, (uint)id);
            return await comando.ExecuteScalarAsync(cancelacion);
        }

        private static Task<List<T>> LeerIdAsync<T>(NpgsqlConnection conexion, string consulta, long id, CancellationToken cancelacion, Func<DbDataReader, T> leer) =>
            LeerAsync(conexion, consulta.Replace("@id", "@tabla"), id, cancelacion, leer);

        private static Task<List<ColumnaModelo>> LeerColumnasObjetoAsync(NpgsqlConnection conexion, long id, CancellationToken cancelacion) =>
            LeerIdAsync(conexion, """
                SELECT a.attname, format_type(a.atttypid, a.atttypmod), NOT a.attnotnull
                FROM pg_attribute a
                WHERE a.attrelid = @id AND a.attnum > 0 AND NOT a.attisdropped
                ORDER BY a.attnum
                """, id, cancelacion, l => new ColumnaModelo
            {
                Nombre = l.GetString(0),
                TipoDatos = FormatearTipo(l.GetString(1)),
                PermiteNulos = l.GetBoolean(2)
            });

        private static async Task<DetalleObjetoModelo> LeerRutinaAsync(NpgsqlConnection conexion, ObjetoBaseDatosModelo objeto, long id, CancellationToken cancelacion)
        {
            var argumentos = await LeerIdAsync(conexion, """
                SELECT COALESCE(a.nombre, ''), format_type(a.tipo, NULL), COALESCE(a.modo, 'i'), a.n::int
                FROM pg_proc p
                CROSS JOIN LATERAL unnest(COALESCE(p.proallargtypes, p.proargtypes::oid[]), p.proargmodes, p.proargnames)
                    WITH ORDINALITY AS a(tipo, modo, nombre, n)
                WHERE p.oid = @id AND a.tipo IS NOT NULL
                ORDER BY a.n
                """, id, cancelacion, l => (Nombre: l.GetString(0), Tipo: FormatearTipo(l.GetString(1)), Modo: l.GetChar(2), Posicion: l.GetInt32(3)));

            var datos = (await LeerIdAsync(conexion, """
                SELECT pg_get_function_result(p.oid), p.proretset
                FROM pg_proc p
                WHERE p.oid = @id
                """, id, cancelacion, l => (Retorno: l.IsDBNull(0) ? null : l.GetString(0), Conjunto: l.GetBoolean(1)))).Single();

            var parametros = argumentos.Where(a => a.Modo != 't').Select(a => new ParametroModelo
            {
                Nombre = a.Nombre.Length > 0 ? a.Nombre : $"${a.Posicion}",
                TipoDatos = a.Tipo,
                Direccion = a.Modo switch
                {
                    'o' => DireccionParametro.Salida,
                    'b' => DireccionParametro.EntradaSalida,
                    _ => DireccionParametro.Entrada
                }
            }).ToList();

            var devuelveTabla = datos.Conjunto || argumentos.Any(a => a.Modo == 't');

            return new DetalleObjetoModelo
            {
                Objeto = objeto,
                Parametros = parametros,
                Retorno = objeto.Tipo == TipoObjeto.Procedimiento ? null : datos.Retorno,
                DevuelveTabla = devuelveTabla,
                Columnas = argumentos.Where(a => a.Modo == 't').Select(a => new ColumnaModelo { Nombre = a.Nombre, TipoDatos = a.Tipo, PermiteNulos = true }).ToList()
            };
        }

        private static async Task<DetalleObjetoModelo> LeerDisparadorAsync(NpgsqlConnection conexion, ObjetoBaseDatosModelo objeto, long id, CancellationToken cancelacion)
        {
            var datos = (await LeerIdAsync(conexion, """
                SELECT t.tgtype::int, t.tgenabled, t.tgfoid::regproc::text
                FROM pg_trigger t
                WHERE t.oid = @id
                """, id, cancelacion, l => (Tipo: l.GetInt32(0), Estado: l.GetChar(1), Funcion: l.GetString(2)))).Single();

            var eventos = new List<string>();
            if ((datos.Tipo & 4) != 0) eventos.Add("INSERT");
            if ((datos.Tipo & 16) != 0) eventos.Add("UPDATE");
            if ((datos.Tipo & 8) != 0) eventos.Add("DELETE");
            if ((datos.Tipo & 32) != 0) eventos.Add("TRUNCATE");

            return new DetalleObjetoModelo
            {
                Objeto = objeto,
                Momento = (datos.Tipo & 64) != 0 ? "INSTEAD OF" : (datos.Tipo & 2) != 0 ? "BEFORE" : "AFTER",
                Eventos = eventos,
                PorFila = (datos.Tipo & 1) != 0,
                Habilitado = datos.Estado != 'D',
                FuncionDisparador = datos.Funcion
            };
        }

        private static async Task<DetalleObjetoModelo> LeerDisparadorEventoAsync(NpgsqlConnection conexion, ObjetoBaseDatosModelo objeto, long id, CancellationToken cancelacion)
        {
            var datos = (await LeerIdAsync(conexion, """
                SELECT e.evtevent, e.evtenabled, e.evtfoid::regproc::text, COALESCE(e.evttags, '{}'::text[])
                FROM pg_event_trigger e
                WHERE e.oid = @id
                """, id, cancelacion, l => (Evento: l.GetString(0), Estado: l.GetChar(1), Funcion: l.GetString(2), Etiquetas: l.GetFieldValue<string[]>(3)))).Single();

            return new DetalleObjetoModelo
            {
                Objeto = objeto,
                Eventos = [datos.Evento],
                Habilitado = datos.Estado != 'D',
                FuncionDisparador = datos.Funcion,
                Etiquetas = [.. datos.Etiquetas]
            };
        }

        private static async Task<DetalleObjetoModelo> LeerSecuenciaAsync(NpgsqlConnection conexion, ObjetoBaseDatosModelo objeto, CancellationToken cancelacion)
        {
            await using var comando = new NpgsqlCommand("""
                SELECT format_type(s.seqtypid, NULL), s.seqstart::text, s.seqincrement::text, s.seqmin::text, s.seqmax::text, s.seqcycle,
                       (SELECT q.last_value::text FROM pg_sequences q WHERE q.schemaname = @esquema AND q.sequencename = @nombre)
                FROM pg_sequence s
                WHERE s.seqrelid = to_regclass(quote_ident(@esquema) || '.' || quote_ident(@nombre))
                """, conexion);
            comando.Parameters.AddWithValue("esquema", objeto.Esquema);
            comando.Parameters.AddWithValue("nombre", objeto.Nombre);

            await using var lector = await comando.ExecuteReaderAsync(cancelacion);

            if (!await lector.ReadAsync(cancelacion))
            {
                throw new InvalidOperationException($"{objeto.NombreCompleto} ya no existe en la base de datos.");
            }

            return new DetalleObjetoModelo
            {
                Objeto = objeto,
                Retorno = lector.GetString(0),
                Inicio = lector.GetString(1),
                Incremento = lector.GetString(2),
                Minimo = lector.GetString(3),
                Maximo = lector.GetString(4),
                Ciclo = lector.GetBoolean(5),
                ValorActual = lector.IsDBNull(6) ? null : lector.GetString(6)
            };
        }
    }
}
