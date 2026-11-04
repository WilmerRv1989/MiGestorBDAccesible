using MiGestorBDAccesible.Core.Conexiones;
using MiGestorBDAccesible.Core.Ejecucion;
using MySqlConnector;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace MiGestorBDAccesible.Core.Proveedores.MySql
{
    public abstract partial class ProveedorMySqlBase
    {
        private const int FilasMostradasMaximas = 1000;

        private const int CaracteresTextoMaximos = 2000;

        private static readonly Regex _lineaError = new(@"at line (\d+)", RegexOptions.IgnoreCase);

        private static readonly Regex _palabras = new(@"[A-Za-z_][A-Za-z0-9_]*");

        public class EstadoTransaccionMySql
        {
            public bool EnTransaccion { get; set; }

            public bool SinAutocommit { get; set; }

            public bool CambiosPendientes { get; set; }
        }

        private sealed class SesionMySql(MySqlConnection conexion) : EstadoTransaccionMySql
        {
            public MySqlConnection Conexion { get; } = conexion;

            public string? BaseActual { get; set; }

            public SemaphoreSlim Turno { get; } = new(1, 1);

            public List<(string Nivel, int Numero, string Texto)> Avisos { get; } = [];

            public string UltimasAdvertencias { get; set; } = "";
        }

        private readonly Dictionary<ConexionActiva, SesionMySql> _sesiones = [];

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
            List<SesionMySql> sesiones;

            lock (_cerrojoSesiones)
            {
                var claves = _sesiones.Keys.Where(k => conexion is null || conexion.Incluye(k)).ToList();
                sesiones = claves.Select(k => _sesiones[k]).ToList();

                foreach (var clave in claves)
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
                        await using var comando = new MySqlCommand(confirmar ? "COMMIT" : "ROLLBACK", sesion.Conexion);
                        await comando.ExecuteNonQueryAsync();
                        mensaje = confirmar ? "Cambios confirmados (COMMIT)." : "Cambios deshechos (ROLLBACK).";
                    }
                }
                catch (MySqlException ex)
                {
                    mensaje = $"No se pudieron {(confirmar ? "confirmar" : "deshacer")} los cambios. {ex.Message}";
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
            SesionMySql sesion;

            try
            {
                sesion = await ObtenerSesionAsync(conexion, cancelacion);
            }
            catch (Exception ex) when (ex is ConexionPerdidaException or MySqlException)
            {
                resultado.Mensajes.Add(new MensajeEjecucionModelo
                {
                    Tipo = TipoMensaje.Error,
                    Texto = ex is MySqlException mysql ? MensajeError(conexion.Conexion, mysql) : ex.Message
                });
                resultado.Duracion = cronometro.Elapsed;
                return resultado;
            }

            await sesion.Turno.WaitAsync(cancelacion);

            try
            {
                if (!string.IsNullOrWhiteSpace(baseDatos) && !string.Equals(baseDatos, sesion.BaseActual, StringComparison.Ordinal))
                {
                    try
                    {
                        await using var usar = new MySqlCommand($"USE {Delimitar(baseDatos)}", sesion.Conexion);
                        await usar.ExecuteNonQueryAsync(cancelacion);
                        sesion.BaseActual = baseDatos;
                    }
                    catch (MySqlException ex) when (!EsErrorDeConexion(ex))
                    {
                        resultado.Mensajes.Add(new MensajeEjecucionModelo { Tipo = TipoMensaje.Error, Numero = ex.Number, Texto = TextoError(ex) });
                        resultado.Duracion = cronometro.Elapsed;
                        return resultado;
                    }
                }

                foreach (var sentencia in DivisorMySql.Dividir(script))
                {
                    var linea = lineaInicial + sentencia.LineaInicial - 1;

                    if (!await EjecutarSentenciaAsync(sesion, sentencia, linea, resultado, cancelacion))
                    {
                        break;
                    }
                }

                await using var actual = new MySqlCommand("SELECT DATABASE()", sesion.Conexion);
                sesion.BaseActual = await actual.ExecuteScalarAsync(cancelacion) as string;
                resultado.CambiosPendientes = TieneCambiosPendientes(conexion);
            }
            catch (Exception ex) when (EsErrorDeConexion(ex) || ex is MySqlException { ErrorCode: MySqlErrorCode.QueryInterrupted } && !cancelacion.IsCancellationRequested)
            {
                await DescartarSesionAsync(conexion, sesion);
                resultado.Cancelado = true;
                resultado.Mensajes.Add(new MensajeEjecucionModelo
                {
                    Tipo = TipoMensaje.Error,
                    Texto = $"Se perdió la conexión con {Motor.Nombre()} durante la ejecución; si había una transacción abierta, se deshizo. Comprueba que el servidor siga encendido y vuelve a ejecutar. Detalle: {ex.Message}"
                });
            }
            finally
            {
                sesion.Turno.Release();
            }

            resultado.BaseDatosFinal = sesion.BaseActual ?? baseDatos;
            resultado.Duracion = cronometro.Elapsed;
            return resultado;
        }

        private async Task<SesionMySql> ObtenerSesionAsync(ConexionActiva conexion, CancellationToken cancelacion)
        {
            lock (_cerrojoSesiones)
            {
                if (_sesiones.TryGetValue(conexion, out var existente) && existente.Conexion.State == System.Data.ConnectionState.Open)
                {
                    return existente;
                }
            }

            var conexionMy = await AbrirAsync(conexion, cancelacion);
            var sesion = new SesionMySql(conexionMy) { BaseActual = conexionMy.Database is { Length: > 0 } inicial ? inicial : null };

            conexionMy.InfoMessage += (_, e) =>
            {
                lock (sesion.Avisos)
                {
                    sesion.Avisos.AddRange(e.Errors.Select(a => (a.Level, (int)a.ErrorCode, a.Message)));
                }
            };

            if (conexion.Principal is null)
            {
                await using var espera = new MySqlCommand("SET SESSION innodb_lock_wait_timeout = 5", conexionMy);
                await espera.ExecuteNonQueryAsync(cancelacion);
            }

            lock (_cerrojoSesiones)
            {
                _sesiones[conexion] = sesion;
            }

            return sesion;
        }

        private async Task DescartarSesionAsync(ConexionActiva conexion, SesionMySql sesion)
        {
            lock (_cerrojoSesiones)
            {
                _sesiones.Remove(conexion);
            }

            MySqlConnection.ClearPool(sesion.Conexion);
            await sesion.Conexion.DisposeAsync();
        }

        private async Task<bool> EjecutarSentenciaAsync(
            SesionMySql sesion,
            SentenciaMySql sentencia,
            int linea,
            ResultadoEjecucionModelo resultado,
            CancellationToken cancelacion)
        {
            await using var comando = new MySqlCommand(sentencia.Texto, sesion.Conexion) { CommandTimeout = 0 };
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

            var palabras = _palabras.Matches(sentencia.Texto).Take(12).Select(m => m.Value.ToUpperInvariant()).ToList();
            var verbo = palabras.FirstOrDefault() ?? "";

            try
            {
                await using var lector = await comando.ExecuteReaderAsync(cancelacion);
                var huboConjunto = false;

                do
                {
                    if (lector.FieldCount > 0)
                    {
                        huboConjunto = true;
                        resultado.Conjuntos.Add(await LeerConjuntoAsync(lector, cancelacion));
                    }
                }
                while (await lector.NextResultAsync(cancelacion));

                var filas = lector.RecordsAffected;
                await lector.CloseAsync();

                var confirmoPendientes = sesion.CambiosPendientes && ConfirmaImplicitamente(palabras);
                ActualizarTransaccionPara(sesion, palabras, sentencia.Texto);

                if (verbo is "INSERT" or "UPDATE" or "DELETE" or "REPLACE" && filas >= 0)
                {
                    resultado.FilasAfectadas += filas;
                    resultado.Mensajes.Add(new MensajeEjecucionModelo { Tipo = TipoMensaje.FilasAfectadas, Texto = TextoFilas(verbo, filas), Linea = linea });
                }
                else if (!huboConjunto)
                {
                    resultado.Mensajes.Add(new MensajeEjecucionModelo { Tipo = TipoMensaje.Informacion, Texto = TextoSentencia(palabras), Linea = linea });
                }

                if (confirmoPendientes)
                {
                    resultado.Mensajes.Add(new MensajeEjecucionModelo
                    {
                        Tipo = TipoMensaje.Informacion,
                        Linea = linea,
                        Texto = $"Atención: {verbo} confirmó también los cambios que tenías pendientes (COMMIT implícito); ya no se pueden deshacer con ROLLBACK."
                    });
                }

                await AgregarAdvertenciasAsync(sesion, resultado, linea, huboConjunto, cancelacion);
            }
            catch (Exception) when (cancelacion.IsCancellationRequested)
            {
                resultado.Cancelado = true;
                resultado.Mensajes.Add(new MensajeEjecucionModelo { Tipo = TipoMensaje.Informacion, Texto = "La ejecución se canceló." });
                return false;
            }
            catch (MySqlException ex) when (!EsErrorDeConexion(ex))
            {
                resultado.Mensajes.Add(new MensajeEjecucionModelo
                {
                    Tipo = TipoMensaje.Error,
                    Numero = ex.Number,
                    Texto = TextoError(ex),
                    Linea = LineaError(ex, linea)
                });
            }

            return true;
        }

        private static async Task AgregarAdvertenciasAsync(SesionMySql sesion, ResultadoEjecucionModelo resultado, int linea, bool huboConjunto, CancellationToken cancelacion)
        {
            List<(string Nivel, int Numero, string Texto)> avisos;

            lock (sesion.Avisos)
            {
                avisos = [.. sesion.Avisos];
                sesion.Avisos.Clear();
            }

            if (huboConjunto)
            {
                await using var comando = new MySqlCommand("SHOW WARNINGS", sesion.Conexion);
                await using var lector = await comando.ExecuteReaderAsync(cancelacion);
                var leidas = new List<(string Nivel, int Numero, string Texto)>();

                while (await lector.ReadAsync(cancelacion))
                {
                    leidas.Add((lector.GetString(0), Convert.ToInt32(lector.GetValue(1), CultureInfo.InvariantCulture), lector.GetString(2)));
                }

                var huella = string.Join('\n', leidas);
                avisos = huella == sesion.UltimasAdvertencias ? [] : leidas;
                sesion.UltimasAdvertencias = huella;
            }
            else
            {
                sesion.UltimasAdvertencias = string.Join('\n', avisos);
            }

            foreach (var (nivel, numero, texto) in avisos.Where(a => !a.Nivel.Equals("Error", StringComparison.OrdinalIgnoreCase)))
            {
                var tipo = nivel.Equals("Note", StringComparison.OrdinalIgnoreCase) ? "Nota" : "Advertencia";
                resultado.Mensajes.Add(new MensajeEjecucionModelo { Tipo = TipoMensaje.Informacion, Linea = linea, Texto = $"{tipo} {numero}: {texto}" });
            }
        }

        public static bool ConfirmaImplicitamente(IReadOnlyList<string> palabras) => palabras switch
        {
            ["CREATE", "TEMPORARY", ..] or ["DROP", "TEMPORARY", ..] => false,
            ["CREATE" or "ALTER" or "DROP" or "RENAME" or "TRUNCATE" or "GRANT" or "REVOKE", ..] => true,
            ["BEGIN", ..] or ["START", "TRANSACTION", ..] or ["LOCK", "TABLES", ..] => true,
            ["SET", "AUTOCOMMIT", ..] => true,
            _ => false
        };

        public static void ActualizarTransaccionPara(EstadoTransaccionMySql estado, IReadOnlyList<string> palabras, string texto)
        {
            switch (palabras)
            {
                case ["BEGIN", ..] or ["START", "TRANSACTION", ..]:
                    estado.EnTransaccion = true;
                    estado.CambiosPendientes = false;
                    return;
                case ["COMMIT", ..] or ["ROLLBACK", ..] when !(palabras is [_, "TO", ..]):
                    estado.EnTransaccion = false;
                    estado.CambiosPendientes = false;
                    return;
                case ["SET", "AUTOCOMMIT", ..]:
                    estado.SinAutocommit = Regex.IsMatch(texto, @"autocommit\s*=?\s*(0|off|false)", RegexOptions.IgnoreCase);
                    estado.EnTransaccion = false;
                    estado.CambiosPendientes = false;
                    return;
            }

            if (ConfirmaImplicitamente(palabras))
            {
                estado.EnTransaccion = false;
                estado.CambiosPendientes = false;
                return;
            }

            if ((estado.EnTransaccion || estado.SinAutocommit) && palabras.FirstOrDefault() is "INSERT" or "UPDATE" or "DELETE" or "REPLACE" or "CALL" or "LOAD")
            {
                estado.CambiosPendientes = true;
            }
        }

        private static int LineaError(MySqlException ex, int linea) =>
            _lineaError.Match(ex.Message) is { Success: true } relativa
                ? linea + int.Parse(relativa.Groups[1].Value, CultureInfo.InvariantCulture) - 1
                : linea;

        public string TextoError(MySqlException ex)
        {
            var explicacion = ex.Number switch
            {
                1146 => $"La tabla o vista no existe en esta base. Revisa el nombre; en Linux, {Motor.Nombre()} distingue mayúsculas y minúsculas en los nombres de tabla.",
                1054 => "Esa columna no existe en la tabla. Revisa el nombre.",
                1064 => "Hay un error de sintaxis cerca de lo que indica el mensaje.",
                1046 => "No hay una base de datos elegida. Escribe USE y el nombre de la base, o abre la consulta desde una base en el explorador.",
                1049 => "Esa base de datos no existe.",
                1050 => "Ya existe una tabla con ese nombre.",
                1062 => "El valor se repite y la columna no lo permite (UNIQUE o PRIMARY KEY).",
                1452 => "La llave foránea no lo permite: el valor no existe en la tabla destino.",
                1451 => "La llave foránea no lo permite: hay filas en otra tabla que apuntan a la que intentas borrar o cambiar.",
                1048 => "Falta un valor obligatorio (NOT NULL).",
                1364 => "Falta un valor obligatorio: la columna no tiene valor predeterminado (DEFAULT).",
                3819 or 4025 => "El valor no cumple una restricción CHECK.",
                1406 => "El texto es más largo de lo que permite la columna.",
                1264 => "El número no cabe en el tipo de la columna.",
                1366 or 1292 => "Un valor no tiene el formato del tipo de datos esperado.",
                1365 => "División entre cero.",
                1305 => "No existe un procedimiento o una función con ese nombre.",
                1304 => "Ya existe un procedimiento o una función con ese nombre.",
                1359 => "Ya existe un disparador con ese nombre.",
                1419 => "Con el registro binario activado, el servidor no deja crear funciones ni disparadores a un usuario sin el permiso SUPER, salvo que el administrador active log_bin_trust_function_creators o la función se declare DETERMINISTIC, NO SQL o READS SQL DATA.",
                1044 or 1142 or 1227 or 1370 => "No tienes permiso para hacer esto.",
                1205 => "Otra sesión tiene bloqueadas esas filas y se agotó la espera. Si es una de tus pestañas, termina allí su transacción con COMMIT o ROLLBACK y vuelve a intentarlo.",
                1213 => "Dos sesiones se bloquearon mutuamente (deadlock) y el servidor canceló esta. Vuelve a intentarlo.",
                1215 or 1822 or 3780 => "No se pudo crear la llave foránea: revisa que las columnas tengan el mismo tipo y que la columna destino tenga un índice o sea la clave principal.",
                1061 => "Ya existe un índice con ese nombre en la tabla.",
                1091 => "No existe ese índice, columna o restricción para borrar.",
                1060 => "Ya existe una columna con ese nombre.",
                1136 => "La cantidad de valores no coincide con la cantidad de columnas.",
                _ => null
            };

            var detalle = $"{ex.SqlState}: {ex.Message}";
            return explicacion is null ? detalle : $"{explicacion} {detalle}";
        }

        private static async Task<ConjuntoResultadosModelo> LeerConjuntoAsync(MySqlDataReader lector, CancellationToken cancelacion)
        {
            var conjunto = new ConjuntoResultadosModelo();

            for (var i = 0; i < lector.FieldCount; i++)
            {
                conjunto.Columnas.Add(new ColumnaResultadoModelo { Nombre = lector.GetName(i), TipoDatos = lector.GetDataTypeName(i).ToLowerInvariant() });
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
                    fila[i] = lector.IsDBNull(i) ? null : FormatearValor(lector.GetValue(i), lector.GetDataTypeName(i));
                }

                conjunto.Filas.Add(fila);
            }

            return conjunto;
        }

        public static string FormatearValor(object valor, string tipo) => valor switch
        {
            MySqlDateTime fecha when !fecha.IsValidDateTime => tipo.Equals("DATE", StringComparison.OrdinalIgnoreCase) ? "0000-00-00" : "0000-00-00 00:00:00",
            MySqlDateTime fecha => FormatearValor(fecha.GetDateTime(), tipo),
            DateTime fecha when tipo.Equals("DATE", StringComparison.OrdinalIgnoreCase) => fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTime fecha => fecha.ToString("yyyy-MM-dd HH:mm:ss.FFFFFF", CultureInfo.InvariantCulture).TrimEnd('.'),
            DateOnly fecha => fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            TimeSpan lapso => (lapso < TimeSpan.Zero ? "-" : "") + $"{(int)lapso.Duration().TotalHours:00}:{lapso.Duration():mm\\:ss}",
            bool logico => logico ? "1" : "0",
            byte[] bytes => "0x" + Convert.ToHexString(bytes, 0, Math.Min(bytes.Length, 32)) + (bytes.Length > 32 ? "…" : ""),
            string texto when texto.Length > CaracteresTextoMaximos => texto[..CaracteresTextoMaximos] + "…",
            string texto => texto,
            IFormattable formateable => formateable.ToString(null, CultureInfo.InvariantCulture),
            var otro => otro.ToString() ?? ""
        };

        private static string TextoFilas(string verbo, int filas)
        {
            var (singular, plural) = verbo switch
            {
                "INSERT" or "REPLACE" => ("insertada", "insertadas"),
                "UPDATE" => ("actualizada", "actualizadas"),
                "DELETE" => ("eliminada", "eliminadas"),
                _ => ("afectada", "afectadas")
            };

            return filas == 1 ? $"1 fila {singular}." : $"{filas:N0} filas {plural}.";
        }

        public static string TextoSentencia(IReadOnlyList<string> palabras) => palabras switch
        {
            ["BEGIN", ..] or ["START", "TRANSACTION", ..] => "Transacción iniciada (START TRANSACTION): los cambios quedan pendientes hasta COMMIT o ROLLBACK.",
            ["COMMIT", ..] => "Cambios confirmados (COMMIT).",
            ["ROLLBACK", "TO", ..] => "Cambios deshechos hasta el punto de guardado (ROLLBACK TO SAVEPOINT).",
            ["ROLLBACK", ..] => "Cambios deshechos (ROLLBACK).",
            ["SAVEPOINT", ..] => "Punto de guardado creado (SAVEPOINT).",
            ["USE", ..] => "Base de datos cambiada (USE).",
            ["CALL", ..] => "Procedimiento ejecutado (CALL).",
            ["TRUNCATE", ..] => "Tabla vaciada (TRUNCATE).",
            ["RENAME", "TABLE", ..] => "Tabla renombrada (RENAME TABLE).",
            ["GRANT", ..] => "Permiso concedido (GRANT).",
            ["REVOKE", ..] => "Permiso retirado (REVOKE).",
            ["SET", "AUTOCOMMIT", ..] => "Confirmación automática cambiada (SET autocommit).",
            ["SET", ..] => "Variable cambiada (SET).",
            ["CREATE" or "ALTER" or "DROP", ..] => TextoDefinicion(palabras),
            _ => $"Sentencia {string.Join(" ", palabras.Take(2))} ejecutada correctamente."
        };

        private static string TextoDefinicion(IReadOnlyList<string> palabras)
        {
            var objeto = palabras.Skip(1)
                .FirstOrDefault(p => p is "TABLE" or "INDEX" or "VIEW" or "SEQUENCE" or "FUNCTION" or "PROCEDURE" or "TRIGGER" or "EVENT" or "DATABASE" or "SCHEMA" or "USER" or "ROLE") ?? "";
            var (nombre, femenino) = objeto switch
            {
                "TABLE" => ("Tabla", true),
                "INDEX" => ("Índice", false),
                "VIEW" => ("Vista", true),
                "SEQUENCE" => ("Secuencia", true),
                "FUNCTION" => ("Función", true),
                "PROCEDURE" => ("Procedimiento", false),
                "TRIGGER" => ("Disparador", false),
                "EVENT" => ("Evento", false),
                "DATABASE" or "SCHEMA" => ("Base de datos", true),
                "USER" => ("Usuario", false),
                "ROLE" => ("Rol", false),
                _ => ("Objeto", false)
            };

            var participio = palabras[0] switch
            {
                "CREATE" => femenino ? "creada" : "creado",
                "ALTER" => femenino ? "modificada" : "modificado",
                _ => femenino ? "eliminada" : "eliminado"
            };

            return $"{nombre} {participio} ({palabras[0]} {objeto}).".Replace(" )", ")");
        }
    }
}
