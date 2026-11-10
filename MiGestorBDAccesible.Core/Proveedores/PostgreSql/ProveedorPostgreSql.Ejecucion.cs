using MiGestorBDAccesible.Core.Conexiones;
using MiGestorBDAccesible.Core.Ejecucion;
using MiGestorBDAccesible.Core.Edicion;
using Npgsql;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

namespace MiGestorBDAccesible.Core.Proveedores.PostgreSql
{
    public partial class ProveedorPostgreSql
    {
        public ContextoCierre Contexto { get; } = ContextoCierre.Actual;

        private const int FilasMostradasMaximas = 1000;

        private const int CaracteresTextoMaximos = 2000;

        private static readonly Regex _lineaFuncion = new(@"line (\d+)", RegexOptions.IgnoreCase);

        private static readonly Regex _palabras = new(@"[A-Za-z_][A-Za-z0-9_]*");

        private sealed class SesionPostgreSql(NpgsqlConnection conexion, string baseDatos)
        {
            public NpgsqlConnection Conexion { get; } = conexion;

            public string BaseDatos { get; } = baseDatos;

            public bool CambiosPendientes { get; set; }

            public bool SoloDeshacer { get; set; }

            public ControlSesionEjecucion Control { get; } = new();

            public SemaphoreSlim Turno => Control.Turno;

            public List<MensajeEjecucionModelo> Avisos { get; } = [];
        }

        private readonly Dictionary<(ConexionActiva Conexion, string Base), SesionPostgreSql> _sesiones = [];

        private readonly Lock _cerrojoSesiones = new();

        public bool TieneCambiosPendientes(ConexionActiva? conexion = null)
        {
            lock (_cerrojoSesiones)
            {
                return _sesiones.Any(p => (conexion is null || conexion.Incluye(p.Key.Conexion)) && p.Value.CambiosPendientes);
            }
        }

        public bool PuedeConfirmar(ConexionActiva? conexion = null)
        {
            lock (_cerrojoSesiones)
            {
                return _sesiones.Any(p => (conexion is null || conexion.Incluye(p.Key.Conexion)) && p.Value.CambiosPendientes && !p.Value.SoloDeshacer);
            }
        }

        private const string MensajeSoloDeshacer = "La transacción está abortada. Sus cambios no se pueden guardar; solo puedes deshacerlos con ROLLBACK.";

        public async Task<ResultadoFinalizacionModelo> FinalizarSesionAsync(ConexionActiva? conexion, bool confirmar)
        {
            using var bloqueo = Contexto.BloquearOperaciones();
            if (Contexto.HayOperacionesEnCurso)
            {
                return new ResultadoFinalizacionModelo { Errores = ["Hay una ejecución en curso. Espera a que termine o cancélala antes de terminar los cambios."] };
            }

            List<SesionPostgreSql> sesiones;

            lock (_cerrojoSesiones)
            {
                sesiones = [.. _sesiones.Where(p => conexion is null || conexion.Incluye(p.Key.Conexion)).Select(p => p.Value).Distinct()];
            }

            var errores = new List<string>();
            string? mensaje = null;

            foreach (var sesion in sesiones)
            {
                if (!await sesion.Control.TomarParaFinalizarAsync())
                {
                    errores.Add("Hay una ejecución en curso. Espera a que termine o cancélala antes de terminar los cambios.");
                    continue;
                }

                try
                {
                    if (sesion.Control.Cerrada)
                    {
                        await CerrarSesionAsync(sesion);
                        continue;
                    }

                    if (sesion.CambiosPendientes)
                    {
                        if (confirmar && sesion.SoloDeshacer)
                        {
                            errores.Add($"{sesion.BaseDatos}: {MensajeSoloDeshacer}");
                            continue;
                        }

                        if (confirmar)
                        {
                            await using var estado = new NpgsqlCommand("SELECT 1", sesion.Conexion);
                            await estado.ExecuteScalarAsync();
                        }

                        await using var comando = new NpgsqlCommand(confirmar ? "COMMIT" : "ROLLBACK", sesion.Conexion);
                        await comando.ExecuteNonQueryAsync();
                        sesion.CambiosPendientes = false;
                        mensaje = confirmar ? "Cambios confirmados (COMMIT)." : "Cambios deshechos (ROLLBACK).";
                    }

                    await CerrarSesionAsync(sesion);
                }
                catch (Exception ex) when (ex is System.Data.Common.DbException or InvalidOperationException or TimeoutException)
                {
                    var detalle = ex.Message;
                    if (ex is PostgresException { SqlState: "25P02" })
                    {
                        sesion.SoloDeshacer = true;
                        sesion.CambiosPendientes = sesion.Conexion.State == System.Data.ConnectionState.Open;
                        errores.Add($"{sesion.BaseDatos}: {detalle} {MensajeSoloDeshacer}");
                        if (!sesion.CambiosPendientes) await CerrarSesionAsync(sesion);
                        continue;
                    }
                    var perdida = sesion.Conexion.State != System.Data.ConnectionState.Open || EsErrorDeConexion(ex);

                    if (!perdida)
                    {
                        try
                        {
                            await LeerEstadoAsync(sesion);
                        }
                        catch (Exception estado) when (estado is System.Data.Common.DbException or InvalidOperationException or TimeoutException)
                        {
                            perdida = sesion.Conexion.State != System.Data.ConnectionState.Open || EsErrorDeConexion(estado);
                            detalle += $" No se pudo comprobar el estado de la transacción: {estado.Message}";
                        }
                    }

                    if (perdida)
                    {
                        sesion.CambiosPendientes = false;
                        await CerrarSesionAsync(sesion);

                        if (!confirmar)
                        {
                            mensaje = "Cambios deshechos (ROLLBACK): se perdió la conexión y el servidor descarta la transacción.";
                            continue;
                        }
                    }

                    var estadoCambios = perdida
                        ? "Se perdió la conexión. No se sabe si los cambios se guardaron; compruébalo antes de volver a ejecutarlos."
                        : sesion.CambiosPendientes
                            ? "Los cambios siguen pendientes; puedes volver a decidir si confirmarlos o deshacerlos."
                            : confirmar ? "El servidor terminó la transacción; los cambios no se guardaron." : "El servidor ya no tiene cambios pendientes en esta sesión.";
                    errores.Add($"{sesion.BaseDatos}: no se pudieron {(confirmar ? "confirmar" : "deshacer")} los cambios. {detalle} {estadoCambios}");
                }
                finally
                {
                    sesion.Turno.Release();
                }
            }

            return new ResultadoFinalizacionModelo { Mensaje = mensaje, Errores = errores };
        }

        public async Task DescartarSesionesAsync(ConexionActiva? conexion)
        {
            List<SesionPostgreSql> sesiones;

            lock (_cerrojoSesiones)
            {
                sesiones = [.. _sesiones.Where(p => conexion is null || conexion.Incluye(p.Key.Conexion)).Select(p => p.Value).Distinct()];
            }

            foreach (var sesion in sesiones)
            {
                RetirarSesion(sesion);
                await sesion.Control.DescartarAsync(() => CerrarSesionAsync(sesion));
            }
        }

        private void RetirarSesion(SesionPostgreSql sesion)
        {
            lock (_cerrojoSesiones)
            {
                foreach (var clave in _sesiones.Where(p => ReferenceEquals(p.Value, sesion)).Select(p => p.Key).ToList())
                {
                    _sesiones.Remove(clave);
                }
            }
        }

        private async Task CerrarSesionAsync(SesionPostgreSql sesion)
        {
            RetirarSesion(sesion);
            await sesion.Control.CerrarConexionAsync(sesion.Conexion);
        }

        public async Task<ResultadoEjecucionModelo> EjecutarAsync(
            ConexionActiva conexion,
            string? baseDatos,
            string script,
            int lineaInicial,
            CancellationToken cancelacion = default)
        {
            var resultado = new ResultadoEjecucionModelo();
            using var operacion = Contexto.Operaciones.Iniciar(cancelacion);
            if (operacion is null)
            {
                resultado.Mensajes.Add(new MensajeEjecucionModelo { Tipo = TipoMensaje.Error, Texto = OperacionesBaseDatos.MensajeCierre });
                resultado.CambiosPendientes = TieneCambiosPendientes(conexion);
                return resultado;
            }
            cancelacion = operacion.Cancelacion;
            var cronometro = Stopwatch.StartNew();
            List<SentenciaPostgreSql> sentencias;

            try
            {
                sentencias = DivisorPostgreSql.Dividir(script);
            }
            catch (ScriptNoDivisibleException ex)
            {
                resultado.Mensajes.Add(new MensajeEjecucionModelo
                {
                    Tipo = TipoMensaje.Error,
                    Texto = ex.Message,
                    Linea = lineaInicial + ex.Linea - 1
                });
                resultado.BaseDatosFinal = baseDatos;
                resultado.Duracion = cronometro.Elapsed;
                resultado.CambiosPendientes = TieneCambiosPendientes(conexion);
                return resultado;
            }

            var nombreBase = string.IsNullOrWhiteSpace(baseDatos)
                ? conexion.Conexion.BaseDatosInicial ?? BaseDatosAdministracion
                : baseDatos;
            SesionPostgreSql sesion;

            try
            {
                sesion = await ObtenerSesionAsync(conexion, nombreBase, cancelacion);
            }
            catch (OperationCanceledException) when (cancelacion.IsCancellationRequested)
            {
                resultado.Cancelado = true;
                resultado.CambiosPendientes = TieneCambiosPendientes(conexion);
                return resultado;
            }
            catch (Exception ex) when (ex is ConexionPerdidaException or NpgsqlException)
            {
                resultado.Mensajes.Add(new MensajeEjecucionModelo
                {
                    Tipo = TipoMensaje.Error,
                    Texto = ex is NpgsqlException npgsql ? MensajeError(conexion.Conexion, npgsql) : ex.Message
                });
                resultado.Duracion = cronometro.Elapsed;
                resultado.CambiosPendientes = TieneCambiosPendientes(conexion);
                return resultado;
            }

            try
            {
                await sesion.Turno.WaitAsync(cancelacion);
            }
            catch (OperationCanceledException)
            {
                resultado.Cancelado = true;
                resultado.CambiosPendientes = TieneCambiosPendientes(conexion);
                return resultado;
            }

            try
            {
                sesion.Control.Ejecucion = operacion;
                if (sesion.Control.Cerrada)
                {
                    resultado.Mensajes.Add(new MensajeEjecucionModelo { Tipo = TipoMensaje.Error, Texto = "La sesión se cerró. Vuelve a ejecutar cuando termine el cierre." });
                    resultado.CambiosPendientes = TieneCambiosPendientes(conexion);
                    return resultado;
                }
                foreach (var sentencia in sentencias)
                {
                    var linea = lineaInicial + sentencia.LineaInicial - 1;

                    if (!await EjecutarSentenciaAsync(sesion, sentencia, linea, resultado, cancelacion))
                    {
                        break;
                    }
                }

                await LeerEstadoAsync(sesion);
                resultado.CambiosPendientes = TieneCambiosPendientes(conexion);
            }
            catch (OperationCanceledException) when (cancelacion.IsCancellationRequested)
            {
                resultado.Cancelado = true;
                resultado.Mensajes.Add(new MensajeEjecucionModelo { Tipo = TipoMensaje.Informacion, Texto = "La ejecución se canceló." });
            }
            catch (Exception ex) when (sesion.Conexion.State != System.Data.ConnectionState.Open || EsErrorDeConexion(ex))
            {
                await DescartarSesionAsync(conexion, sesion);
                resultado.Cancelado = true;
                resultado.Mensajes.Add(new MensajeEjecucionModelo
                {
                    Tipo = TipoMensaje.Error,
                    Texto = $"Se perdió la conexión con PostgreSQL durante la ejecución; si había una transacción abierta, se deshizo. Comprueba que el servidor siga encendido y vuelve a ejecutar. Detalle: {ex.Message}"
                });
            }
            catch (Exception ex) when (ex is System.Data.Common.DbException or InvalidOperationException)
            {
                resultado.Mensajes.Add(new MensajeEjecucionModelo { Tipo = TipoMensaje.Error, Texto = ex.Message });
            }
            finally
            {
                sesion.Control.Ejecucion = null;
                sesion.Turno.Release();
            }

            resultado.BaseDatosFinal = nombreBase;
            resultado.Duracion = cronometro.Elapsed;
            resultado.CambiosPendientes = TieneCambiosPendientes(conexion);
            return resultado;
        }

        private async Task<SesionPostgreSql> ObtenerSesionAsync(ConexionActiva conexion, string baseDatos, CancellationToken cancelacion)
        {
            lock (_cerrojoSesiones)
            {
                if (_sesiones.TryGetValue((conexion, baseDatos), out var existente) && !existente.Control.Cerrada && existente.Conexion.FullState == System.Data.ConnectionState.Open)
                {
                    return existente;
                }
            }

            var conexionPg = await AbrirAsync(conexion, baseDatos, cancelacion);
            var sesion = new SesionPostgreSql(conexionPg, baseDatos);

            if (conexion.Principal is null)
            {
                await using var espera = new NpgsqlCommand("SET lock_timeout = '5s'", conexionPg);
                await espera.ExecuteNonQueryAsync(cancelacion);
            }

            conexionPg.Notice += (_, e) =>
            {
                lock (sesion.Avisos)
                {
                    sesion.Avisos.Add(new MensajeEjecucionModelo
                    {
                        Tipo = TipoMensaje.Informacion,
                        Texto = e.Notice.Severity is "NOTICE" or "INFO" or "LOG" ? e.Notice.MessageText : $"{e.Notice.Severity}: {e.Notice.MessageText}"
                    });
                }
            };

            lock (_cerrojoSesiones)
            {
                _sesiones[(conexion, baseDatos)] = sesion;
            }

            return sesion;
        }

        private async Task DescartarSesionAsync(ConexionActiva conexion, SesionPostgreSql sesion)
        {
            try { NpgsqlConnection.ClearPool(sesion.Conexion); }
            catch (Exception) { }
            await CerrarSesionAsync(sesion);
        }

        private static async Task<bool> EjecutarSentenciaAsync(
            SesionPostgreSql sesion,
            SentenciaPostgreSql sentencia,
            int linea,
            ResultadoEjecucionModelo resultado,
            CancellationToken cancelacion)
        {
            if (DivisorPostgreSql.EsComandoPsql(sentencia.Texto))
            {
                resultado.Mensajes.Add(new MensajeEjecucionModelo
                {
                    Tipo = TipoMensaje.Error,
                    Linea = linea,
                    Texto = $"{sentencia.Texto.Split(' ')[0]} no es SQL: es un comando de la consola psql y aquí no existe. Para ver una tabla, ponte sobre ella en el explorador y pulsa Intro; para cambiar de base, elige otra base y pulsa Control+N."
                });
                return true;
            }

            if (PrimeraPalabra(sentencia.Texto) is "COMMIT" or "END")
            {
                await LeerEstadoAsync(sesion);
                if (sesion.SoloDeshacer)
                {
                    resultado.Mensajes.Add(new MensajeEjecucionModelo { Tipo = TipoMensaje.Error, Texto = MensajeSoloDeshacer, Linea = linea });
                    return false;
                }
            }

            await using var comando = new NpgsqlCommand(sentencia.Texto, sesion.Conexion) { CommandTimeout = 0 };
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

            try
            {
                await using var lector = await comando.ExecuteReaderAsync(cancelacion);
                var columnas = lector.FieldCount;

                if (columnas > 0)
                {
                    resultado.Conjuntos.Add(await LeerConjuntoAsync(lector, cancelacion));
                }

                await lector.CloseAsync();
                AgregarAvisos(sesion, resultado);

                var verbo = PrimeraPalabra(sentencia.Texto);
                var filas = lector.RecordsAffected;

                if (verbo is "INSERT" or "UPDATE" or "DELETE" or "MERGE" && filas >= 0)
                {
                    resultado.FilasAfectadas += filas;
                    resultado.Mensajes.Add(new MensajeEjecucionModelo { Tipo = TipoMensaje.FilasAfectadas, Texto = TextoFilas(verbo, filas), Linea = linea });
                }
                else if (columnas == 0)
                {
                    resultado.Mensajes.Add(new MensajeEjecucionModelo { Tipo = TipoMensaje.Informacion, Texto = TextoSentencia(sentencia.Texto), Linea = linea });
                }
            }
            catch (Exception ex) when (cancelacion.IsCancellationRequested || ex is PostgresException { SqlState: "57014" })
            {
                AgregarAvisos(sesion, resultado);
                resultado.Cancelado = true;
                resultado.Mensajes.Add(new MensajeEjecucionModelo { Tipo = TipoMensaje.Informacion, Texto = "La ejecución se canceló." });
                return false;
            }
            catch (PostgresException ex)
            {
                AgregarAvisos(sesion, resultado);
                resultado.Mensajes.Add(new MensajeEjecucionModelo
                {
                    Tipo = TipoMensaje.Error,
                    Texto = TextoError(ex),
                    Linea = LineaError(ex, sentencia.Texto, linea)
                });
            }

            return true;
        }

        private static void AgregarAvisos(SesionPostgreSql sesion, ResultadoEjecucionModelo resultado)
        {
            lock (sesion.Avisos)
            {
                resultado.Mensajes.AddRange(sesion.Avisos);
                sesion.Avisos.Clear();
            }
        }

        private static async Task LeerEstadoAsync(SesionPostgreSql sesion)
        {
            try
            {
                await using var comando = new NpgsqlCommand("SELECT pg_current_xact_id_if_assigned() IS NOT NULL", sesion.Conexion);
                sesion.CambiosPendientes = (bool)(await comando.ExecuteScalarAsync())!;
                sesion.SoloDeshacer = false;
            }
            catch (PostgresException ex) when (ex.SqlState == "25P02")
            {
                sesion.CambiosPendientes = true;
                sesion.SoloDeshacer = true;
            }
        }

        private static int LineaError(PostgresException ex, string texto, int linea)
        {
            if (ex.Where is { } donde && _lineaFuncion.Match(donde) is { Success: true } relativa)
            {
                var inicioCuerpo = texto.IndexOf('$');

                if (inicioCuerpo >= 0)
                {
                    return linea + texto[..inicioCuerpo].Count(c => c == '\n') + int.Parse(relativa.Groups[1].Value) - 1;
                }
            }

            var posicion = ex.Position > 0 ? ex.Position : ex.InternalPosition;

            return posicion > 0 && posicion <= texto.Length
                ? linea + texto[..(posicion - 1)].Count(c => c == '\n')
                : linea;
        }

        private static string TextoError(PostgresException ex)
        {
            var explicacion = ex.SqlState switch
            {
                "42P01" => "La tabla o vista no existe en esta base. Revisa el nombre; recuerda que PostgreSQL guarda en minúsculas los nombres sin comillas.",
                "42703" => "Esa columna no existe en la tabla. Revisa el nombre; recuerda que PostgreSQL guarda en minúsculas los nombres sin comillas.",
                "42601" => "Hay un error de sintaxis cerca de lo que indica el mensaje.",
                "42883" => "No existe una función o un operador con esos tipos de datos; quizás falta convertir un valor con ::tipo.",
                "42P07" => "Ya existe un objeto con ese nombre.",
                "23505" => "El valor se repite y la columna no lo permite (UNIQUE o PRIMARY KEY).",
                "23503" => "La llave foránea no lo permite: el valor no existe en la tabla destino, o hay filas que apuntan a la que intentas borrar.",
                "23502" => "Falta un valor obligatorio (NOT NULL).",
                "23514" => "El valor no cumple una restricción CHECK.",
                "22P02" => "Un valor no tiene el formato del tipo de datos esperado.",
                "22003" => "El número es demasiado grande para el tipo de la columna.",
                "22001" => "El texto es más largo de lo que permite la columna.",
                "22012" => "División entre cero.",
                "25P02" => "La transacción quedó anulada por un error anterior: PostgreSQL rechaza todo hasta que ejecutes ROLLBACK.",
                "25001" => "Esta sentencia no se puede ejecutar dentro de una transacción; termina antes con COMMIT o ROLLBACK.",
                "42501" => "No tienes permiso para hacer esto.",
                "3D000" => "Esa base de datos no existe.",
                "55P03" => "Otra sesión tiene bloqueado ese objeto. Si es una de tus pestañas, termina allí su transacción con COMMIT o ROLLBACK y vuelve a intentarlo.",
                "40P01" => "Dos sesiones se bloquearon mutuamente (deadlock) y PostgreSQL canceló esta. Vuelve a intentarlo.",
                _ => null
            };

            var partes = new List<string>();

            if (explicacion is not null)
            {
                partes.Add(explicacion);
            }

            partes.Add($"{ex.SqlState}: {ex.MessageText}");

            if (!string.IsNullOrWhiteSpace(ex.Detail))
            {
                partes.Add(ex.Detail);
            }

            if (!string.IsNullOrWhiteSpace(ex.Hint))
            {
                partes.Add($"Sugerencia: {ex.Hint}");
            }

            return string.Join(" ", partes.Select(p => p.TrimEnd('.') + "."));
        }

        private static async Task<ConjuntoResultadosModelo> LeerConjuntoAsync(NpgsqlDataReader lector, CancellationToken cancelacion)
        {
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
                    fila[i] = lector.IsDBNull(i) ? null : Formatear(lector, i);
                }

                conjunto.Filas.Add(fila);
            }

            return conjunto;
        }

        private static string Formatear(NpgsqlDataReader lector, int indice)
        {
            object valor;

            if (lector.GetDataTypeName(indice) == "interval")
            {
                return FormatearIntervalo(lector.GetFieldValue<NpgsqlTypes.NpgsqlInterval>(indice));
            }

            try
            {
                valor = lector.GetValue(indice);
            }
            catch (Exception ex) when (ex is InvalidCastException or OverflowException)
            {
                try
                {
                    return lector.GetFieldValue<BigInteger>(indice).ToString(CultureInfo.InvariantCulture);
                }
                catch (Exception)
                {
                    return "(valor fuera del rango que la aplicación puede mostrar)";
                }
            }

            return FormatearValor(valor, lector.GetDataTypeName(indice));
        }

        private static string FormatearValor(object valor, string tipo) => valor switch
        {
            DateTime fecha when tipo == "date" => fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTime fecha when fecha.Kind == DateTimeKind.Utc => fecha.ToString("yyyy-MM-dd HH:mm:ss.FFFFFF", CultureInfo.InvariantCulture).TrimEnd('.') + " UTC",
            DateTime fecha => fecha.ToString("yyyy-MM-dd HH:mm:ss.FFFFFF", CultureInfo.InvariantCulture).TrimEnd('.'),
            DateOnly fecha => fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            TimeOnly hora => hora.ToString("HH:mm:ss.FFFFFF", CultureInfo.InvariantCulture).TrimEnd('.'),
            DateTimeOffset fecha => fecha.ToString("yyyy-MM-dd HH:mm:ss.FFFFFF zzz", CultureInfo.InvariantCulture),
            TimeSpan lapso => lapso.ToString("c", CultureInfo.InvariantCulture),
            bool logico => logico ? "true" : "false",
            byte[] bytes => "\\x" + Convert.ToHexString(bytes, 0, Math.Min(bytes.Length, 32)).ToLowerInvariant() + (bytes.Length > 32 ? "…" : ""),
            string texto when texto.Length > CaracteresTextoMaximos => texto[..CaracteresTextoMaximos] + "…",
            string texto => texto,
            Array arreglo => "{" + string.Join(",", arreglo.Cast<object?>().Select(e => e is null ? "NULL" : FormatearValor(e, ""))) + "}",
            IFormattable formateable => formateable.ToString(null, CultureInfo.InvariantCulture),
            var otro => otro.ToString() ?? ""
        };

        public static string FormatearIntervalo(NpgsqlTypes.NpgsqlInterval intervalo)
        {
            static string Contar(int cantidad, string singular, string plural) => Math.Abs(cantidad) == 1 ? $"{cantidad} {singular}" : $"{cantidad} {plural}";

            var partes = new List<string>();
            var anios = intervalo.Months / 12;
            var meses = intervalo.Months % 12;

            if (anios != 0) partes.Add(Contar(anios, "año", "años"));
            if (meses != 0) partes.Add(Contar(meses, "mes", "meses"));
            if (intervalo.Days != 0) partes.Add(Contar(intervalo.Days, "día", "días"));

            var tiempo = TimeSpan.FromTicks(intervalo.Time * 10);

            if (tiempo != TimeSpan.Zero || partes.Count == 0)
            {
                var signo = tiempo < TimeSpan.Zero ? "-" : "";
                var absoluto = tiempo.Duration();
                var texto = $"{signo}{(int)absoluto.TotalHours:00}:{absoluto.Minutes:00}:{absoluto.Seconds:00}";
                var fraccion = absoluto.Ticks % TimeSpan.TicksPerSecond;
                partes.Add(fraccion == 0 ? texto : $"{texto}.{fraccion / 10:000000}".TrimEnd('0'));
            }

            return string.Join(" ", partes);
        }

        private static string PrimeraPalabra(string texto) =>
            _palabras.Match(texto) is { Success: true } palabra ? palabra.Value.ToUpperInvariant() : "";

        private static string TextoFilas(string verbo, int filas)
        {
            var (singular, plural) = verbo switch
            {
                "INSERT" => ("insertada", "insertadas"),
                "UPDATE" => ("actualizada", "actualizadas"),
                "DELETE" => ("eliminada", "eliminadas"),
                _ => ("afectada", "afectadas")
            };

            return filas == 1 ? $"1 fila {singular}." : $"{filas:N0} filas {plural}.";
        }

        private static string TextoSentencia(string texto)
        {
            var palabras = _palabras.Matches(texto).Take(5).Select(m => m.Value.ToUpperInvariant()).ToList();

            return palabras switch
            {
                ["BEGIN", ..] or ["START", "TRANSACTION", ..] => "Transacción iniciada (BEGIN): los cambios quedan pendientes hasta COMMIT o ROLLBACK.",
                ["COMMIT", ..] or ["END", ..] => "Cambios confirmados (COMMIT).",
                ["ROLLBACK", "TO", ..] => "Cambios deshechos hasta el punto de guardado (ROLLBACK TO SAVEPOINT).",
                ["ROLLBACK", ..] or ["ABORT", ..] => "Cambios deshechos (ROLLBACK).",
                ["SAVEPOINT", ..] => "Punto de guardado creado (SAVEPOINT).",
                ["DO", ..] => "Bloque anónimo terminado correctamente (DO).",
                ["CALL", ..] => "Procedimiento ejecutado (CALL).",
                ["TRUNCATE", ..] => "Tabla vaciada (TRUNCATE).",
                ["GRANT", ..] => "Permiso concedido (GRANT).",
                ["REVOKE", ..] => "Permiso retirado (REVOKE).",
                ["COMMENT", ..] => "Comentario guardado (COMMENT).",
                ["SET", ..] or ["RESET", ..] => "Configuración de la sesión cambiada (SET).",
                ["VACUUM", ..] or ["ANALYZE", ..] => "Mantenimiento terminado (VACUUM).",
                ["CREATE" or "ALTER" or "DROP", ..] => TextoDefinicion(palabras),
                _ => $"Sentencia {string.Join(" ", palabras.Take(2))} ejecutada correctamente."
            };
        }

        private static string TextoDefinicion(List<string> palabras)
        {
            var objeto = palabras.Skip(1)
                .SkipWhile(p => p is "OR" or "REPLACE" or "UNIQUE" or "TEMPORARY" or "TEMP" or "UNLOGGED" or "MATERIALIZED" or "IF" or "NOT" or "EXISTS" or "CONCURRENTLY")
                .FirstOrDefault() ?? "";
            var (nombre, femenino) = objeto switch
            {
                "TABLE" => ("Tabla", true),
                "INDEX" => ("Índice", false),
                "VIEW" => ("Vista", true),
                "SEQUENCE" => ("Secuencia", true),
                "FUNCTION" => ("Función", true),
                "PROCEDURE" => ("Procedimiento", false),
                "TRIGGER" => ("Disparador", false),
                "SCHEMA" => ("Esquema", false),
                "DATABASE" => ("Base de datos", true),
                "EXTENSION" => ("Extensión", true),
                "TYPE" => ("Tipo", false),
                "ROLE" or "USER" => ("Rol", false),
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
