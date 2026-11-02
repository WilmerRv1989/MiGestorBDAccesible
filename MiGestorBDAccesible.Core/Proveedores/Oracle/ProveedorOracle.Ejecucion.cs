using MiGestorBDAccesible.Core.Conexiones;
using MiGestorBDAccesible.Core.Ejecucion;
using Oracle.ManagedDataAccess.Client;
using Oracle.ManagedDataAccess.Types;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace MiGestorBDAccesible.Core.Proveedores.Oracle
{
    public partial class ProveedorOracle
    {
        private const int FilasMostradasMaximas = 1000;

        private const int CaracteresTextoMaximos = 2000;

        private const string ConsultaSalida = """
            DECLARE
                l VARCHAR2(32767);
                s INTEGER;
                r CLOB;
            BEGIN
                LOOP
                    DBMS_OUTPUT.GET_LINE(l, s);
                    EXIT WHEN s = 1;
                    r := r || l || CHR(10);
                END LOOP;
                :salida := r;
            END;
            """;

        private static readonly Regex _lineaError = new(@"(?:line|línea)\s+(\d+)", RegexOptions.IgnoreCase);

        private static readonly Regex _objetoPlSql = new(
            """^\s*CREATE\s+(?:OR\s+REPLACE\s+)?(?:(?:NON)?EDITIONABLE\s+)?(PACKAGE\s+BODY|TYPE\s+BODY|PROCEDURE|FUNCTION|PACKAGE|TRIGGER|TYPE)\s+(?:(?:"([^"]+)"|([\w$#]+))\s*\.\s*)?(?:"([^"]+)"|([\w$#]+))""",
            RegexOptions.IgnoreCase);

        private sealed class SesionOracle(OracleConnection conexion)
        {
            public OracleConnection Conexion { get; } = conexion;

            public OracleTransaction? Transaccion { get; set; }

            public string? Esquema { get; set; }

            public bool CambiosPendientes { get; set; }

            public SemaphoreSlim Turno { get; } = new(1, 1);
        }

        private readonly Dictionary<ConexionActiva, SesionOracle> _sesiones = new(ReferenceEqualityComparer.Instance);

        private readonly Lock _cerrojoSesiones = new();

        public bool TieneCambiosPendientes(ConexionActiva? conexion = null)
        {
            lock (_cerrojoSesiones)
            {
                return _sesiones.Any(p => (conexion is null || conexion.Incluye(p.Key)) && p.Value.CambiosPendientes);
            }
        }

        public async Task<string?> FinalizarSesionAsync(ConexionActiva? conexion, bool confirmar)
        {
            List<SesionOracle> sesiones;

            lock (_cerrojoSesiones)
            {
                sesiones = [.. _sesiones.Where(p => conexion is null || conexion.Incluye(p.Key)).Select(p => p.Value)];

                foreach (var clave in _sesiones.Where(p => sesiones.Contains(p.Value)).Select(p => p.Key).ToList())
                {
                    _sesiones.Remove(clave);
                }
            }

            string? mensaje = null;

            foreach (var sesion in sesiones)
            {
                try
                {
                    if (sesion.CambiosPendientes)
                    {
                        await using var comando = sesion.Conexion.CreateCommand();
                        comando.CommandText = confirmar ? "COMMIT" : "ROLLBACK";
                        await comando.ExecuteNonQueryAsync();
                        mensaje = confirmar ? "Cambios confirmados (COMMIT)." : "Cambios deshechos (ROLLBACK).";
                    }
                }
                catch (OracleException ex)
                {
                    mensaje = $"No se pudieron {(confirmar ? "confirmar" : "deshacer")} los cambios. {LimpiarMensaje(ex.Message)}";
                }
                finally
                {
                    await sesion.Conexion.DisposeAsync();
                }
            }

            return mensaje;
        }

        public async Task<ResultadoEjecucionModelo> EjecutarAsync(
            ConexionActiva conexion,
            string? baseDatos,
            string script,
            int lineaInicial,
            CancellationToken cancelacion = default)
        {
            var resultado = new ResultadoEjecucionModelo();
            var cronometro = Stopwatch.StartNew();
            SesionOracle sesion;

            try
            {
                sesion = await ObtenerSesionAsync(conexion, cancelacion);
            }
            catch (Exception ex) when (ex is ConexionPerdidaException or OracleException)
            {
                resultado.Mensajes.Add(new MensajeEjecucionModelo
                {
                    Tipo = TipoMensaje.Error,
                    Texto = ex is OracleException oracle ? MensajeError(conexion.Conexion, oracle) : ex.Message,
                    Numero = (ex as OracleException)?.Number
                });
                resultado.Duracion = cronometro.Elapsed;
                return resultado;
            }

            await sesion.Turno.WaitAsync(cancelacion);

            try
            {
                if (!string.IsNullOrEmpty(baseDatos) && !string.Equals(baseDatos, sesion.Esquema, StringComparison.Ordinal))
                {
                    await EjecutarSinResultadoAsync(sesion, $"ALTER SESSION SET CURRENT_SCHEMA = {Delimitar(baseDatos)}", cancelacion);
                    sesion.Esquema = baseDatos;
                }

                foreach (var sentencia in DivisorOracle.Dividir(script))
                {
                    var linea = lineaInicial + sentencia.LineaInicial - 1;

                    if (!await EjecutarSentenciaAsync(sesion, sentencia, linea, resultado, cancelacion))
                    {
                        break;
                    }
                }

                await LeerEstadoAsync(sesion, resultado);
            }
            catch (OracleException ex) when (_erroresConexion.Contains(ex.Number))
            {
                await DescartarSesionAsync(conexion, sesion);
                resultado.Cancelado = true;
                resultado.Mensajes.Add(new MensajeEjecucionModelo
                {
                    Tipo = TipoMensaje.Error,
                    Texto = $"Se perdió la conexión con Oracle durante la ejecución; los cambios sin confirmar se perdieron. Comprueba que el contenedor siga encendido y vuelve a ejecutar. Detalle: {LimpiarMensaje(ex.Message)}"
                });
            }
            catch (OracleException ex)
            {
                resultado.Mensajes.Add(new MensajeEjecucionModelo { Tipo = TipoMensaje.Error, Texto = LimpiarMensaje(ex.Message), Numero = ex.Number });
            }
            finally
            {
                sesion.Turno.Release();
            }

            resultado.Duracion = cronometro.Elapsed;
            return resultado;
        }

        private async Task<SesionOracle> ObtenerSesionAsync(ConexionActiva conexion, CancellationToken cancelacion)
        {
            lock (_cerrojoSesiones)
            {
                if (_sesiones.TryGetValue(conexion, out var existente) && existente.Conexion.State == ConnectionState.Open)
                {
                    return existente;
                }
            }

            var conexionOracle = await AbrirAsync(conexion, cancelacion);
            var sesion = new SesionOracle(conexionOracle) { Transaccion = conexionOracle.BeginTransaction() };

            await EjecutarSinResultadoAsync(sesion, "BEGIN DBMS_OUTPUT.ENABLE(NULL); END;", cancelacion);

            lock (_cerrojoSesiones)
            {
                _sesiones[conexion] = sesion;
            }

            return sesion;
        }

        private async Task DescartarSesionAsync(ConexionActiva conexion, SesionOracle sesion)
        {
            lock (_cerrojoSesiones)
            {
                _sesiones.Remove(conexion);
            }

            OracleConnection.ClearPool(sesion.Conexion);
            await sesion.Conexion.DisposeAsync();
        }

        private static async Task<bool> EjecutarSentenciaAsync(
            SesionOracle sesion,
            SentenciaOracle sentencia,
            int linea,
            ResultadoEjecucionModelo resultado,
            CancellationToken cancelacion)
        {
            await using var comando = sesion.Conexion.CreateCommand();
            comando.CommandText = sentencia.Texto;
            comando.InitialLONGFetchSize = -1;
            comando.BindByName = true;

            await using var registro = cancelacion.Register(() =>
            {
                try
                {
                    comando.Cancel();
                }
                catch (Exception)
                {
                }
            });

            var verbo = Verbo(sentencia.Texto);

            try
            {
                if (verbo is "SELECT" or "WITH" or "(")
                {
                    await using var lector = await comando.ExecuteReaderAsync(cancelacion);
                    resultado.Conjuntos.Add(await LeerConjuntoAsync(lector, cancelacion));
                }
                else
                {
                    var filas = await comando.ExecuteNonQueryAsync(cancelacion);
                    var texto = TextoFilas(verbo, filas);

                    if (texto is not null)
                    {
                        resultado.FilasAfectadas += filas;
                        resultado.Mensajes.Add(new MensajeEjecucionModelo { Tipo = TipoMensaje.FilasAfectadas, Texto = texto, Linea = linea });
                    }
                    else if (_objetoPlSql.Match(sentencia.Texto) is { Success: true } objeto)
                    {
                        await RevisarCompilacionAsync(sesion, sentencia.Texto, objeto, linea, resultado);
                    }
                    else
                    {
                        resultado.Mensajes.Add(new MensajeEjecucionModelo { Tipo = TipoMensaje.Informacion, Texto = TextoSentencia(sentencia), Linea = linea });
                    }
                }
            }
            catch (Exception ex) when (cancelacion.IsCancellationRequested || ex is OracleException { Number: 1013 })
            {
                resultado.Cancelado = true;
                resultado.Mensajes.Add(new MensajeEjecucionModelo { Tipo = TipoMensaje.Informacion, Texto = "La ejecución se canceló." });
                return false;
            }
            catch (OracleException ex) when (!_erroresConexion.Contains(ex.Number))
            {
                resultado.Mensajes.Add(new MensajeEjecucionModelo
                {
                    Tipo = TipoMensaje.Error,
                    Texto = LimpiarMensaje(ex.Message),
                    Numero = ex.Number,
                    Linea = sentencia.EsPlSql && _lineaError.Match(ex.Message) is { Success: true } relativa
                        ? linea + int.Parse(relativa.Groups[1].Value) - 1
                        : linea
                });
            }

            await LeerSalidaAsync(sesion, resultado);
            return true;
        }

        private static async Task RevisarCompilacionAsync(
            SesionOracle sesion,
            string texto,
            Match objeto,
            int linea,
            ResultadoEjecucionModelo resultado)
        {
            var tipo = Regex.Replace(objeto.Groups[1].Value.ToUpperInvariant(), @"\s+", " ");
            var nombre = objeto.Groups[4].Success ? objeto.Groups[4].Value : objeto.Groups[5].Value.ToUpperInvariant();
            var propietario = objeto.Groups[2].Success ? objeto.Groups[2].Value
                : objeto.Groups[3].Success ? objeto.Groups[3].Value.ToUpperInvariant()
                : null;
            var lineaObjeto = linea + texto[..objeto.Groups[1].Index].Count(c => c == '\n');

            await using var comando = sesion.Conexion.CreateCommand();
            comando.BindByName = true;
            comando.CommandText = """
                SELECT line, position, text, attribute
                FROM all_errors
                WHERE owner = NVL(:propietario, SYS_CONTEXT('USERENV', 'CURRENT_SCHEMA'))
                  AND name = :nombre AND type = :tipo
                ORDER BY sequence
                """;
            comando.Parameters.Add("propietario", propietario);
            comando.Parameters.Add("nombre", nombre);
            comando.Parameters.Add("tipo", tipo);

            var errores = 0;
            await using var lector = await comando.ExecuteReaderAsync();

            while (await lector.ReadAsync())
            {
                var esError = lector.GetString(3) == "ERROR";
                errores += esError ? 1 : 0;
                resultado.Mensajes.Add(new MensajeEjecucionModelo
                {
                    Tipo = esError ? TipoMensaje.Error : TipoMensaje.Informacion,
                    Texto = $"columna {Convert.ToInt32(lector.GetValue(1))}: {lector.GetString(2).Trim().ReplaceLineEndings(" ")}",
                    Linea = lineaObjeto + Convert.ToInt32(lector.GetValue(0)) - 1
                });
            }

            var descripcion = NombreObjeto(tipo);
            resultado.Mensajes.Add(new MensajeEjecucionModelo
            {
                Tipo = TipoMensaje.Informacion,
                Texto = errores == 0
                    ? $"{descripcion} {nombre} compilado correctamente."
                    : $"{descripcion} {nombre} creado con errores de compilación; no se podrá usar hasta corregirlos.",
                Linea = linea
            });
        }

        private static async Task LeerSalidaAsync(SesionOracle sesion, ResultadoEjecucionModelo resultado)
        {
            await using var comando = sesion.Conexion.CreateCommand();
            comando.CommandText = ConsultaSalida;
            var parametro = comando.Parameters.Add("salida", OracleDbType.Clob, ParameterDirection.Output);
            await comando.ExecuteNonQueryAsync();

            if (parametro.Value is OracleClob { IsNull: false } clob)
            {
                foreach (var linea in clob.Value.TrimEnd('\n').Split('\n'))
                {
                    resultado.Mensajes.Add(new MensajeEjecucionModelo { Tipo = TipoMensaje.Informacion, Texto = linea });
                }

                clob.Dispose();
            }
        }

        private static async Task LeerEstadoAsync(SesionOracle sesion, ResultadoEjecucionModelo resultado)
        {
            await using var comando = sesion.Conexion.CreateCommand();
            comando.CommandText = "SELECT DBMS_TRANSACTION.LOCAL_TRANSACTION_ID, SYS_CONTEXT('USERENV', 'CURRENT_SCHEMA') FROM dual";
            await using var lector = await comando.ExecuteReaderAsync();
            await lector.ReadAsync();

            sesion.CambiosPendientes = !lector.IsDBNull(0);
            sesion.Esquema = lector.GetString(1);
            resultado.CambiosPendientes = sesion.CambiosPendientes;
            resultado.BaseDatosFinal = sesion.Esquema;
        }

        private static async Task EjecutarSinResultadoAsync(SesionOracle sesion, string sql, CancellationToken cancelacion)
        {
            await using var comando = sesion.Conexion.CreateCommand();
            comando.CommandText = sql;
            await comando.ExecuteNonQueryAsync(cancelacion);
        }

        private static async Task<ConjuntoResultadosModelo> LeerConjuntoAsync(DbDataReader lector, CancellationToken cancelacion)
        {
            var oracle = (OracleDataReader)lector;
            var conjunto = new ConjuntoResultadosModelo();

            for (var i = 0; i < lector.FieldCount; i++)
            {
                conjunto.Columnas.Add(new ColumnaResultadoModelo { Nombre = lector.GetName(i), TipoDatos = lector.GetDataTypeName(i) });
            }

            while (await lector.ReadAsync(cancelacion))
            {
                conjunto.TotalFilas++;

                if (conjunto.Filas.Count >= FilasMostradasMaximas)
                {
                    continue;
                }

                var fila = new string?[lector.FieldCount];

                for (var i = 0; i < lector.FieldCount; i++)
                {
                    fila[i] = lector.IsDBNull(i) ? null : Formatear(oracle, i);
                }

                conjunto.Filas.Add(fila);
            }

            return conjunto;
        }

        private static string Formatear(OracleDataReader lector, int indice)
        {
            if (lector.GetProviderSpecificValue(indice) is OracleDecimal numero)
            {
                return numero.ToString().Replace(',', '.');
            }

            return lector.GetValue(indice) switch
            {
                DateTime fecha when lector.GetDataTypeName(indice) == "Date" => fecha.TimeOfDay == TimeSpan.Zero
                    ? fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    : fecha.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                DateTime fecha => fecha.ToString("yyyy-MM-dd HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture).TrimEnd('.'),
                DateTimeOffset fecha => fecha.ToString("yyyy-MM-dd HH:mm:ss.FFFFFFF zzz", CultureInfo.InvariantCulture),
                bool logico => logico ? "TRUE" : "FALSE",
                byte[] bytes => Convert.ToHexString(bytes, 0, Math.Min(bytes.Length, 32)) + (bytes.Length > 32 ? "…" : ""),
                string texto when texto.Length > CaracteresTextoMaximos => texto[..CaracteresTextoMaximos] + "…",
                IFormattable formateable => formateable.ToString(null, CultureInfo.InvariantCulture),
                var valor => valor.ToString() ?? ""
            };
        }

        private static string Verbo(string texto)
        {
            var limpio = texto.TrimStart();
            var fin = 0;

            while (fin < limpio.Length && char.IsLetter(limpio[fin]))
            {
                fin++;
            }

            return fin == 0 ? limpio[..Math.Min(1, limpio.Length)] : limpio[..fin].ToUpperInvariant();
        }

        private static string? TextoFilas(string verbo, int filas)
        {
            var accion = verbo switch
            {
                "INSERT" => ("insertada", "insertadas"),
                "UPDATE" => ("actualizada", "actualizadas"),
                "DELETE" => ("eliminada", "eliminadas"),
                "MERGE" => ("combinada", "combinadas"),
                _ => default((string, string)?)
            };

            return accion is var (singular, plural)
                ? filas == 1 ? $"1 fila {singular}." : $"{filas:N0} filas {plural}."
                : null;
        }

        private static string TextoSentencia(SentenciaOracle sentencia)
        {
            if (sentencia.EsPlSql)
            {
                return "Bloque PL/SQL terminado correctamente.";
            }

            var palabras = Regex.Matches(sentencia.Texto, @"[A-Za-z_][A-Za-z0-9_$#]*").Take(4).Select(m => m.Value.ToUpperInvariant()).ToList();
            var inicio = string.Join(" ", palabras);

            return palabras switch
            {
                ["COMMIT", ..] => "Cambios confirmados (COMMIT).",
                ["ROLLBACK", "TO", ..] => "Cambios deshechos hasta el punto de guardado (ROLLBACK TO SAVEPOINT).",
                ["ROLLBACK", ..] => "Cambios deshechos (ROLLBACK).",
                ["SAVEPOINT", ..] => "Punto de guardado creado (SAVEPOINT).",
                ["TRUNCATE", ..] => "Tabla vaciada (TRUNCATE).",
                ["PURGE", "RECYCLEBIN" or "USER_RECYCLEBIN" or "DBA_RECYCLEBIN", ..] => "Papelera de reciclaje vaciada (PURGE RECYCLEBIN).",
                ["PURGE", ..] => "Borrado definitivo de la papelera de reciclaje (PURGE).",
                ["FLASHBACK", "TABLE", ..] => "Tabla recuperada de la papelera de reciclaje (FLASHBACK TABLE).",
                ["GRANT", ..] => "Permiso concedido (GRANT).",
                ["REVOKE", ..] => "Permiso retirado (REVOKE).",
                ["ALTER", "SESSION", ..] => "Sesión modificada (ALTER SESSION).",
                ["COMMENT", ..] => "Comentario guardado (COMMENT).",
                ["CREATE" or "ALTER" or "DROP" or "RENAME", ..] => TextoDefinicion(palabras),
                _ => $"Sentencia {inicio} ejecutada correctamente."
            };
        }

        private static string TextoDefinicion(List<string> palabras)
        {
            var objeto = palabras.Skip(1).SkipWhile(p => p is "OR" or "REPLACE" or "UNIQUE" or "BITMAP" or "GLOBAL" or "TEMPORARY" or "PRIVATE" or "FORCE" or "NOFORCE" or "PUBLIC").FirstOrDefault() ?? "";
            var (nombre, femenino) = objeto switch
            {
                "TABLE" => ("Tabla", true),
                "INDEX" => ("Índice", false),
                "VIEW" => ("Vista", true),
                "SEQUENCE" => ("Secuencia", true),
                "SYNONYM" => ("Sinónimo", false),
                "USER" => ("Usuario", false),
                "ROLE" => ("Rol", false),
                "MATERIALIZED" => ("Vista materializada", true),
                "PROCEDURE" => ("Procedimiento", false),
                "FUNCTION" => ("Función", true),
                "PACKAGE" => ("Paquete", false),
                "TRIGGER" => ("Disparador", false),
                "TYPE" => ("Tipo", false),
                _ => ("Objeto", false)
            };

            var participio = palabras[0] switch
            {
                "CREATE" => femenino ? "creada" : "creado",
                "ALTER" => femenino ? "modificada" : "modificado",
                "DROP" => femenino ? "eliminada" : "eliminado",
                _ => femenino ? "renombrada" : "renombrado"
            };

            return $"{nombre} {participio} ({palabras[0]} {objeto}).".Replace(" )", ")");
        }

        private static string NombreObjeto(string tipo) => tipo switch
        {
            "PROCEDURE" => "Procedimiento",
            "FUNCTION" => "Función",
            "PACKAGE" => "Paquete",
            "PACKAGE BODY" => "Cuerpo del paquete",
            "TRIGGER" => "Disparador",
            "TYPE BODY" => "Cuerpo del tipo",
            _ => "Tipo"
        };

        private static string LimpiarMensaje(string mensaje) =>
            string.Join(" ", mensaje.Split('\n')
                .Select(l => l.Trim())
                .Where(l => l.Length > 0 && !l.StartsWith("https://docs.oracle.com/error-help", StringComparison.OrdinalIgnoreCase)));
    }
}
