using MiGestorBDAccesible.Core.Conexiones;
using MiGestorBDAccesible.Core.Estructura;
using Oracle.ManagedDataAccess.Client;
using System.Data.Common;
using System.Text;
using System.Text.RegularExpressions;

namespace MiGestorBDAccesible.Core.Proveedores.Oracle
{
    public partial class ProveedorOracle
    {
        private const string FiltroValido = """
            NOT EXISTS (SELECT 1 FROM all_objects o2
                        WHERE o2.owner = o.owner AND o2.object_name = o.object_name
                          AND o2.object_type IN (o.object_type, o.object_type || ' BODY')
                          AND o2.status <> 'VALID')
            """;

        private static readonly Regex _fecha = new(@"^\d{4}-\d{2}-\d{2}$");

        private static readonly Regex _fechaHora = new(@"^\d{4}-\d{2}-\d{2}[ T]\d{2}:\d{2}(:\d{2}(\.\d+)?)?$");

        private static readonly HashSet<string> _tiposTextoOracle = new(StringComparer.Ordinal)
        {
            "VARCHAR2", "NVARCHAR2", "CHAR", "NCHAR", "CLOB", "NCLOB", "LONG", "ROWID", "UROWID"
        };

        public async Task<Dictionary<TipoObjeto, int>> ContarObjetosAsync(
            ConexionActiva conexion,
            string baseDatos,
            CancellationToken cancelacion = default)
        {
            await using var conexionOracle = await AbrirAsync(conexion, cancelacion);
            await using var comando = Comando(conexionOracle, """
                SELECT
                    (SELECT COUNT(*) FROM all_objects WHERE owner = :esquema AND object_type IN ('VIEW', 'MATERIALIZED VIEW')),
                    (SELECT COUNT(*) FROM all_objects WHERE owner = :esquema AND object_type = 'PROCEDURE'),
                    (SELECT COUNT(*) FROM all_objects WHERE owner = :esquema AND object_type = 'FUNCTION'),
                    (SELECT COUNT(*) FROM all_objects WHERE owner = :esquema AND object_type = 'PACKAGE'),
                    (SELECT COUNT(*) FROM all_sequences WHERE sequence_owner = :esquema AND sequence_name NOT LIKE 'ISEQ$$%'),
                    (SELECT COUNT(*) FROM all_triggers WHERE owner = :esquema AND base_object_type IN ('SCHEMA', 'DATABASE'))
                FROM dual
                """, null);
            comando.Parameters.Add("esquema", baseDatos);

            await using var lector = await comando.ExecuteReaderAsync(cancelacion);
            await lector.ReadAsync(cancelacion);

            return new Dictionary<TipoObjeto, int>
            {
                [TipoObjeto.Vista] = Convert.ToInt32(lector.GetValue(0)),
                [TipoObjeto.Procedimiento] = Convert.ToInt32(lector.GetValue(1)),
                [TipoObjeto.Funcion] = Convert.ToInt32(lector.GetValue(2)),
                [TipoObjeto.Paquete] = Convert.ToInt32(lector.GetValue(3)),
                [TipoObjeto.Secuencia] = Convert.ToInt32(lector.GetValue(4)),
                [TipoObjeto.Disparador] = Convert.ToInt32(lector.GetValue(5))
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
                TipoObjeto.Vista => $"SELECT o.owner, o.object_name, CASE WHEN {FiltroValido} THEN 1 ELSE 0 END, CASE o.object_type WHEN 'VIEW' THEN 0 ELSE 1 END FROM all_objects o WHERE o.owner = :esquema AND o.object_type IN ('VIEW', 'MATERIALIZED VIEW') ORDER BY o.object_name",
                TipoObjeto.Procedimiento => $"SELECT o.owner, o.object_name, CASE WHEN {FiltroValido} THEN 1 ELSE 0 END, 0 FROM all_objects o WHERE o.owner = :esquema AND o.object_type = 'PROCEDURE' ORDER BY o.object_name",
                TipoObjeto.Funcion => $"SELECT o.owner, o.object_name, CASE WHEN {FiltroValido} THEN 1 ELSE 0 END, 0 FROM all_objects o WHERE o.owner = :esquema AND o.object_type = 'FUNCTION' ORDER BY o.object_name",
                TipoObjeto.Paquete => $"SELECT o.owner, o.object_name, CASE WHEN {FiltroValido} THEN 1 ELSE 0 END, 0 FROM all_objects o WHERE o.owner = :esquema AND o.object_type = 'PACKAGE' ORDER BY o.object_name",
                TipoObjeto.Secuencia => "SELECT sequence_owner, sequence_name, 1, 0 FROM all_sequences WHERE sequence_owner = :esquema AND sequence_name NOT LIKE 'ISEQ$$%' ORDER BY sequence_name",
                TipoObjeto.Disparador => $"SELECT o.owner, o.object_name, CASE WHEN {FiltroValido} THEN 1 ELSE 0 END, 0 FROM all_triggers t JOIN all_objects o ON o.owner = t.owner AND o.object_name = t.trigger_name AND o.object_type = 'TRIGGER' WHERE t.owner = :esquema AND t.base_object_type IN ('SCHEMA', 'DATABASE') ORDER BY o.object_name",
                _ => null
            };

            if (consulta is null)
            {
                return [];
            }

            await using var conexionOracle = await AbrirAsync(conexion, cancelacion);
            await using var comando = Comando(conexionOracle, consulta, null);
            comando.Parameters.Add("esquema", baseDatos);

            return await LeerObjetosAsync(comando, tipo, null, cancelacion);
        }

        public async Task<List<ObjetoBaseDatosModelo>> ObtenerDisparadoresTablaAsync(
            ConexionActiva conexion,
            string baseDatos,
            TablaModelo tabla,
            CancellationToken cancelacion = default)
        {
            await using var conexionOracle = await AbrirAsync(conexion, cancelacion);
            await using var comando = Comando(conexionOracle, $"""
                SELECT o.owner, o.object_name, CASE WHEN {FiltroValido} THEN 1 ELSE 0 END, 0
                FROM all_triggers t
                JOIN all_objects o ON o.owner = t.owner AND o.object_name = t.trigger_name AND o.object_type = 'TRIGGER'
                WHERE t.table_owner = :esquema AND t.table_name = :tabla AND t.base_object_type = 'TABLE'
                ORDER BY o.object_name
                """, tabla);

            return await LeerObjetosAsync(comando, TipoObjeto.Disparador, tabla, cancelacion);
        }

        public async Task<DetalleObjetoModelo> ObtenerDetalleObjetoAsync(
            ConexionActiva conexion,
            string baseDatos,
            ObjetoBaseDatosModelo objeto,
            CancellationToken cancelacion = default)
        {
            await using var conexionOracle = await AbrirAsync(conexion, cancelacion);

            return objeto.Tipo switch
            {
                TipoObjeto.Vista => new DetalleObjetoModelo
                {
                    Objeto = objeto,
                    Columnas = await LeerObjetoAsync(conexionOracle, ConsultaColumnas, objeto, cancelacion, LeerColumna),
                    Errores = await LeerErroresAsync(conexionOracle, objeto, cancelacion)
                },
                TipoObjeto.Procedimiento or TipoObjeto.Funcion => await LeerRutinaAsync(conexionOracle, objeto, cancelacion),
                TipoObjeto.Paquete => await LeerPaqueteAsync(conexionOracle, objeto, cancelacion),
                TipoObjeto.Disparador => await LeerDisparadorAsync(conexionOracle, objeto, cancelacion),
                TipoObjeto.Secuencia => await LeerSecuenciaAsync(conexionOracle, objeto, cancelacion),
                _ => new DetalleObjetoModelo { Objeto = objeto }
            };
        }

        public async Task<string> GenerarScriptObjetoAsync(
            ConexionActiva conexion,
            string baseDatos,
            ObjetoBaseDatosModelo objeto,
            CancellationToken cancelacion = default)
        {
            await using var conexionOracle = await AbrirAsync(conexion, cancelacion);
            var nombre = NombreObjeto(objeto);
            var script = new StringBuilder();

            switch (objeto.Tipo)
            {
                case TipoObjeto.Vista when objeto.Materializada:
                    var consultaVista = await LeerObjetoAsync(conexionOracle, "SELECT query FROM all_mviews WHERE owner = :esquema AND mview_name = :tabla", objeto, cancelacion, l => l.GetString(0));
                    script.AppendLine($"CREATE MATERIALIZED VIEW {nombre} AS");
                    script.AppendLine($"{consultaVista.Single().Trim().TrimEnd(';')};");
                    break;
                case TipoObjeto.Vista:
                    var texto = await LeerObjetoAsync(conexionOracle, "SELECT text FROM all_views WHERE owner = :esquema AND view_name = :tabla", objeto, cancelacion, l => l.GetString(0));
                    script.AppendLine($"CREATE OR REPLACE VIEW {nombre} AS");
                    script.AppendLine($"{texto.Single().Trim().TrimEnd(';')};");
                    break;
                case TipoObjeto.Secuencia:
                    var detalle = await LeerSecuenciaAsync(conexionOracle, objeto, cancelacion);
                    script.AppendLine($"CREATE SEQUENCE {nombre}");
                    var ultimo = await LeerObjetoAsync(conexionOracle, "SELECT TO_CHAR(last_number) FROM all_sequences WHERE sequence_owner = :esquema AND sequence_name = :tabla", objeto, cancelacion, l => l.GetString(0));
                    script.AppendLine($"    START WITH {ultimo.Single()}");
                    script.AppendLine($"    INCREMENT BY {detalle.Incremento}");
                    script.AppendLine($"    MINVALUE {detalle.Minimo}");
                    script.AppendLine($"    MAXVALUE {detalle.Maximo}");
                    script.AppendLine(detalle.Ciclo == true ? "    CYCLE;" : "    NOCYCLE;");
                    break;
                case TipoObjeto.Paquete:
                    EscribirFuente(script, await LeerFuenteAsync(conexionOracle, objeto, "PACKAGE", cancelacion));
                    var cuerpo = await LeerFuenteAsync(conexionOracle, objeto, "PACKAGE BODY", cancelacion);

                    if (cuerpo.Length > 0)
                    {
                        script.AppendLine();
                        EscribirFuente(script, cuerpo);
                    }

                    break;
                default:
                    EscribirFuente(script, await LeerFuenteAsync(conexionOracle, objeto, TipoFuente(objeto.Tipo), cancelacion));
                    break;
            }

            return script.ToString();
        }

        public string ScriptEliminarObjeto(ObjetoBaseDatosModelo objeto) => objeto.Tipo switch
        {
            TipoObjeto.Vista when objeto.Materializada => $"DROP MATERIALIZED VIEW {NombreObjeto(objeto)};",
            TipoObjeto.Vista => $"DROP VIEW {NombreObjeto(objeto)};",
            TipoObjeto.Procedimiento => $"DROP PROCEDURE {NombreObjeto(objeto)};",
            TipoObjeto.Funcion => $"DROP FUNCTION {NombreObjeto(objeto)};",
            TipoObjeto.Paquete => $"DROP PACKAGE {NombreObjeto(objeto)};",
            TipoObjeto.Secuencia => $"DROP SEQUENCE {NombreObjeto(objeto)};",
            _ => $"DROP TRIGGER {NombreObjeto(objeto)};"
        };

        public string ScriptHabilitarDisparador(ObjetoBaseDatosModelo disparador, bool habilitar) =>
            $"ALTER TRIGGER {NombreObjeto(disparador)} {(habilitar ? "ENABLE" : "DISABLE")};";

        public string ScriptReiniciarSecuencia(ObjetoBaseDatosModelo secuencia, long valor) =>
            $"ALTER SEQUENCE {NombreObjeto(secuencia)} RESTART START WITH {valor};";

        public string ScriptLlamada(DetalleObjetoModelo detalle, IReadOnlyDictionary<string, string?> valores)
        {
            var objeto = detalle.Objeto;
            var nombre = NombreObjeto(objeto);

            if (objeto.Tipo == TipoObjeto.Funcion && detalle.Parametros.All(p => p.Direccion == DireccionParametro.Entrada))
            {
                var argumentos = detalle.Parametros.Select(p => LiteralParametro(p, valores));
                return $"SELECT {nombre}({string.Join(", ", argumentos)}) AS resultado{Environment.NewLine}FROM dual;";
            }

            var salidas = detalle.Parametros.Where(p => p.Direccion != DireccionParametro.Entrada).ToList();
            var variables = salidas.ToDictionary(p => p.Nombre, p => Identificador("V_" + (p.Nombre.StartsWith("P_", StringComparison.Ordinal) ? p.Nombre[2..] : p.Nombre)));
            var script = new StringBuilder();

            if (salidas.Count > 0 || objeto.Tipo == TipoObjeto.Funcion)
            {
                script.AppendLine("DECLARE");

                foreach (var salida in salidas)
                {
                    var valor = salida.Direccion == DireccionParametro.EntradaSalida ? $" := {LiteralParametro(salida, valores)}" : "";
                    script.AppendLine($"    {variables[salida.Nombre]} {TipoVariable(salida.TipoDatos)}{valor};");
                }

                if (objeto.Tipo == TipoObjeto.Funcion)
                {
                    script.AppendLine($"    v_resultado {TipoVariable(detalle.Retorno ?? "VARCHAR2")};");
                }
            }

            var asignaciones = detalle.Parametros.Select(p => p.Direccion == DireccionParametro.Entrada
                ? $"{Identificador(p.Nombre)} => {LiteralParametro(p, valores)}"
                : $"{Identificador(p.Nombre)} => {variables[p.Nombre]}");
            var llamada = detalle.Parametros.Count == 0
                ? nombre
                : $"{nombre}({string.Join(", ", asignaciones)})";

            script.AppendLine("BEGIN");
            script.AppendLine(objeto.Tipo == TipoObjeto.Funcion ? $"    v_resultado := {llamada};" : $"    {llamada};");

            if (objeto.Tipo == TipoObjeto.Funcion)
            {
                script.AppendLine(Imprimir("resultado", "v_resultado", detalle.Retorno));
            }

            foreach (var salida in salidas)
            {
                script.AppendLine(Imprimir(salida.Nombre, variables[salida.Nombre], salida.TipoDatos));
            }

            script.AppendLine("END;");
            script.Append('/');
            return script.ToString();
        }

        public string ScriptSeleccionarPrimeras(DetalleObjetoModelo vista) =>
            $"""
            SELECT
            {string.Join($",{Environment.NewLine}", vista.Columnas.Select(c => $"    {Identificador(c.Nombre)}"))}
            FROM {NombreObjeto(vista.Objeto)}
            FETCH FIRST 100 ROWS ONLY;
            """;

        private static string NombreObjeto(ObjetoBaseDatosModelo objeto) =>
            $"{Identificador(objeto.Esquema)}.{Identificador(objeto.Nombre)}";

        private static string Imprimir(string etiqueta, string variable, string? tipo) => tipo switch
        {
            "REF CURSOR" => $"    DBMS_OUTPUT.PUT_LINE('{etiqueta}: es un cursor; ábrelo con FETCH para ver sus filas');",
            "PL/SQL BOOLEAN" or "BOOLEAN" => $"    DBMS_OUTPUT.PUT_LINE('{etiqueta} = ' || CASE WHEN {variable} THEN 'TRUE' WHEN NOT {variable} THEN 'FALSE' ELSE 'NULL' END);",
            _ => $"    DBMS_OUTPUT.PUT_LINE('{etiqueta} = ' || NVL(TO_CHAR({variable}), 'NULL'));"
        };

        private static string TipoVariable(string tipo) => tipo switch
        {
            "VARCHAR2" or "NVARCHAR2" => $"{tipo}(32767)",
            "CHAR" or "NCHAR" => $"{tipo}(2000)",
            "RAW" => "RAW(32767)",
            "REF CURSOR" => "SYS_REFCURSOR",
            "PL/SQL BOOLEAN" => "BOOLEAN",
            _ => tipo
        };

        private static string LiteralParametro(ParametroModelo parametro, IReadOnlyDictionary<string, string?> valores)
        {
            var valor = valores.TryGetValue(parametro.Nombre, out var escrito) ? escrito?.Trim() : null;

            if (string.IsNullOrEmpty(valor) || valor.Equals("NULL", StringComparison.OrdinalIgnoreCase))
            {
                return "NULL";
            }

            if (parametro.TipoDatos == "DATE" && _fecha.IsMatch(valor))
            {
                return $"DATE '{valor}'";
            }

            if (parametro.TipoDatos.StartsWith("TIMESTAMP", StringComparison.Ordinal) && _fechaHora.IsMatch(valor))
            {
                return $"TIMESTAMP '{valor.Replace('T', ' ')}'";
            }

            if (parametro.TipoDatos is "PL/SQL BOOLEAN" or "BOOLEAN")
            {
                return valor.ToLowerInvariant() switch
                {
                    "sí" or "si" or "true" or "verdadero" or "1" => "TRUE",
                    "no" or "false" or "falso" or "0" => "FALSE",
                    _ => valor
                };
            }

            return _tiposTextoOracle.Contains(parametro.TipoDatos) ? $"'{valor.Replace("'", "''")}'" : valor;
        }

        private static string TipoFuente(TipoObjeto tipo) => tipo switch
        {
            TipoObjeto.Procedimiento => "PROCEDURE",
            TipoObjeto.Funcion => "FUNCTION",
            TipoObjeto.Paquete => "PACKAGE",
            _ => "TRIGGER"
        };

        private static void EscribirFuente(StringBuilder script, string fuente)
        {
            script.AppendLine($"CREATE OR REPLACE {fuente.Trim()}");
            script.AppendLine("/");
        }

        private static async Task<string> LeerFuenteAsync(OracleConnection conexion, ObjetoBaseDatosModelo objeto, string tipo, CancellationToken cancelacion)
        {
            await using var comando = Comando(conexion, "SELECT text FROM all_source WHERE owner = :esquema AND name = :tabla AND type = :tipo ORDER BY line", null);
            comando.Parameters.Add("esquema", objeto.Esquema);
            comando.Parameters.Add("tabla", objeto.Nombre);
            comando.Parameters.Add("tipo", tipo);

            await using var lector = await comando.ExecuteReaderAsync(cancelacion);
            var fuente = new StringBuilder();

            while (await lector.ReadAsync(cancelacion))
            {
                fuente.Append(lector.IsDBNull(0) ? "" : lector.GetString(0));
            }

            return fuente.ToString().Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
        }

        private static async Task<List<ObjetoBaseDatosModelo>> LeerObjetosAsync(
            OracleCommand comando,
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
                    Valido = Convert.ToInt32(lector.GetValue(2)) == 1,
                    Materializada = Convert.ToInt32(lector.GetValue(3)) == 1,
                    Tabla = tabla
                });
            }

            return objetos;
        }

        private static Task<List<T>> LeerObjetoAsync<T>(
            OracleConnection conexion,
            string consulta,
            ObjetoBaseDatosModelo objeto,
            CancellationToken cancelacion,
            Func<DbDataReader, T> leer) =>
            LeerAsync(conexion, consulta, new TablaModelo { Esquema = objeto.Esquema, Nombre = objeto.Nombre }, cancelacion, leer);

        private static async Task<List<ErrorCompilacionModelo>> LeerErroresAsync(OracleConnection conexion, ObjetoBaseDatosModelo objeto, CancellationToken cancelacion)
        {
            if (objeto.Valido)
            {
                return [];
            }

            return await LeerObjetoAsync(conexion, """
                SELECT line, position, text
                FROM all_errors
                WHERE owner = :esquema AND name = :tabla AND attribute = 'ERROR'
                ORDER BY CASE WHEN type LIKE '% BODY' THEN 1 ELSE 0 END, sequence
                """, objeto, cancelacion, l => new ErrorCompilacionModelo
            {
                Linea = Convert.ToInt32(l.GetValue(0)),
                Columna = Convert.ToInt32(l.GetValue(1)),
                Texto = l.GetString(2).Trim()
            });
        }

        private static async Task<DetalleObjetoModelo> LeerRutinaAsync(OracleConnection conexion, ObjetoBaseDatosModelo objeto, CancellationToken cancelacion)
        {
            var argumentos = await LeerObjetoAsync(conexion, """
                SELECT argument_name, data_type, in_out, position, type_name, pls_type
                FROM all_arguments
                WHERE owner = :esquema AND object_name = :tabla AND package_name IS NULL
                  AND data_level = 0 AND (argument_name IS NOT NULL OR position = 0) AND data_type IS NOT NULL
                ORDER BY position
                """, objeto, cancelacion, l => (
                    Nombre: l.IsDBNull(0) ? null : l.GetString(0),
                    Tipo: TipoArgumento(l),
                    Direccion: l.GetString(2),
                    Posicion: Convert.ToInt32(l.GetValue(3))));

            return new DetalleObjetoModelo
            {
                Objeto = objeto,
                Parametros = argumentos.Where(a => a.Posicion > 0).Select(a => new ParametroModelo
                {
                    Nombre = a.Nombre!,
                    TipoDatos = a.Tipo,
                    Direccion = a.Direccion switch
                    {
                        "OUT" => DireccionParametro.Salida,
                        "IN/OUT" => DireccionParametro.EntradaSalida,
                        _ => DireccionParametro.Entrada
                    }
                }).ToList(),
                Retorno = argumentos.FirstOrDefault(a => a.Posicion == 0).Tipo,
                Errores = await LeerErroresAsync(conexion, objeto, cancelacion)
            };
        }

        private static string TipoArgumento(DbDataReader lector)
        {
            var tipo = lector.GetString(1);

            if (tipo is "PL/SQL RECORD" or "TABLE" or "VARRAY" or "OBJECT" or "PL/SQL TABLE" && !lector.IsDBNull(4))
            {
                return lector.GetString(4);
            }

            return tipo;
        }

        private static async Task<DetalleObjetoModelo> LeerPaqueteAsync(OracleConnection conexion, ObjetoBaseDatosModelo objeto, CancellationToken cancelacion)
        {
            var subprogramas = await LeerObjetoAsync(conexion, """
                SELECT p.procedure_name,
                       CASE WHEN EXISTS (SELECT 1 FROM all_arguments a
                                         WHERE a.owner = p.owner AND a.package_name = p.object_name
                                           AND a.object_name = p.procedure_name
                                           AND NVL(a.overload, '0') = NVL(p.overload, '0')
                                           AND a.position = 0 AND a.data_level = 0)
                            THEN 'función' ELSE 'procedimiento' END
                FROM all_procedures p
                WHERE p.owner = :esquema AND p.object_name = :tabla AND p.procedure_name IS NOT NULL
                ORDER BY p.subprogram_id
                """, objeto, cancelacion, l => $"{l.GetString(1)} {l.GetString(0)}");

            return new DetalleObjetoModelo
            {
                Objeto = objeto,
                Subprogramas = subprogramas,
                Errores = await LeerErroresAsync(conexion, objeto, cancelacion)
            };
        }

        private static async Task<DetalleObjetoModelo> LeerDisparadorAsync(OracleConnection conexion, ObjetoBaseDatosModelo objeto, CancellationToken cancelacion)
        {
            var datos = (await LeerObjetoAsync(conexion, """
                SELECT trigger_type, triggering_event, status
                FROM all_triggers
                WHERE owner = :esquema AND trigger_name = :tabla
                """, objeto, cancelacion, l => (Tipo: l.GetString(0), Evento: l.GetString(1), Estado: l.GetString(2)))).Single();

            var momento = datos.Tipo switch
            {
                var t when t.StartsWith("BEFORE", StringComparison.Ordinal) => "BEFORE",
                var t when t.StartsWith("AFTER", StringComparison.Ordinal) => "AFTER",
                var t when t.StartsWith("INSTEAD OF", StringComparison.Ordinal) => "INSTEAD OF",
                _ => "COMPOUND"
            };

            return new DetalleObjetoModelo
            {
                Objeto = objeto,
                Momento = momento,
                Eventos = datos.Evento.Split(" OR ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
                PorFila = datos.Tipo.Contains("EACH ROW", StringComparison.Ordinal) ? true
                    : datos.Tipo.Contains("STATEMENT", StringComparison.Ordinal) ? false
                    : null,
                Habilitado = datos.Estado == "ENABLED",
                Errores = await LeerErroresAsync(conexion, objeto, cancelacion)
            };
        }

        private static async Task<DetalleObjetoModelo> LeerSecuenciaAsync(OracleConnection conexion, ObjetoBaseDatosModelo objeto, CancellationToken cancelacion)
        {
            var datos = (await LeerObjetoAsync(conexion, """
                SELECT TO_CHAR(min_value), TO_CHAR(max_value), TO_CHAR(increment_by), cycle_flag, cache_size, TO_CHAR(last_number)
                FROM all_sequences
                WHERE sequence_owner = :esquema AND sequence_name = :tabla
                """, objeto, cancelacion, l => (
                    Minimo: l.GetString(0), Maximo: l.GetString(1), Incremento: l.GetString(2),
                    Ciclo: l.GetString(3) == "Y", Cache: Convert.ToInt64(l.GetValue(4)), Ultimo: l.GetString(5)))).Single();

            var notas = new List<string>
            {
                datos.Cache > 0
                    ? $"Oracle no guarda el valor actual exacto: el último número reservado (LAST_NUMBER) es {datos.Ultimo} y, como guarda {datos.Cache} números en caché (CACHE), puede ir por delante del valor real. Para saberlo en tu sesión, usa NEXTVAL y después CURRVAL."
                    : $"El próximo valor que entregará (LAST_NUMBER) es {datos.Ultimo}."
            };

            return new DetalleObjetoModelo
            {
                Objeto = objeto,
                Retorno = "NUMBER",
                Incremento = datos.Incremento,
                Minimo = datos.Minimo,
                Maximo = datos.Maximo,
                Ciclo = datos.Ciclo,
                Notas = notas
            };
        }
    }
}
