using System.Data.Common;
using System.Data;
using System.Globalization;
using MiGestorBDAccesible.Core.Ejecucion;

namespace MiGestorBDAccesible.Core.Edicion
{
    public class FilasLeidasModelo
    {
        public List<string> Columnas { get; init; } = [];

        public List<string?[]> Filas { get; init; } = [];
    }

    public class ResultadoCambioFilaModelo
    {
        public int FilasAfectadas { get; init; }

        public string? Error { get; init; }

        public bool Correcto => Error is null;
    }

    public interface ISesionEdicion : IAsyncDisposable
    {
        int CambiosPendientes { get; }

        bool Cerrada => false;

        Task<FilasLeidasModelo> LeerAsync(string consulta, CancellationToken cancelacion = default);

        Task<ResultadoCambioFilaModelo> CambiarAsync(string sentencia, CancellationToken cancelacion = default);

        Task<string?> ConfirmarAsync();

        Task<string?> DeshacerAsync();
    }

    public static class RegistroSesionesEdicion
    {
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<ISesionEdicion, byte> _abiertas = new();

        public static bool HayCambiosPendientes => _abiertas.Keys.Any(s => s.CambiosPendientes > 0);

        public static void Agregar(ISesionEdicion sesion) => _abiertas.TryAdd(sesion, 0);

        public static void Quitar(ISesionEdicion sesion) => _abiertas.TryRemove(sesion, out _);

        public static async Task<IReadOnlyList<string>> TerminarTodasAsync(bool confirmar)
        {
            var errores = new List<string>();

            foreach (var sesion in _abiertas.Keys.ToList())
            {
                try
                {
                    var error = confirmar ? await sesion.ConfirmarAsync() : await sesion.DeshacerAsync();

                    if (error is not null)
                    {
                        errores.Add(error);
                        continue;
                    }

                    await sesion.DisposeAsync();
                    Quitar(sesion);
                }
                catch (Exception ex)
                {
                    errores.Add(ex.Message);
                }
            }

            return errores;
        }

        public static async Task DescartarTodasAsync()
        {
            foreach (var sesion in _abiertas.Keys.ToList())
            {
                Quitar(sesion);

                try
                {
                    await sesion.DisposeAsync();
                }
                catch (Exception)
                {
                }
            }
        }
    }

    public sealed class SesionEdicionAdo : ISesionEdicion
    {
        private readonly DbConnection conexion;

        private readonly Func<Exception, string> mensaje;

        private readonly Func<DbTransaction, Task<bool>>? leerEstado;

        public SesionEdicionAdo(DbConnection conexion, Func<Exception, string> mensaje, Func<DbTransaction, Task<bool>>? leerEstado = null)
        {
            this.conexion = conexion;
            this.mensaje = mensaje;
            this.leerEstado = leerEstado;
            RegistroSesionesEdicion.Agregar(this);
        }

        private const int SegundosEspera = 15;

        private readonly ControlSesionEjecucion _control = new();

        private SemaphoreSlim _turno => _control.Turno;

        private DbTransaction? _transaccion;

        public int CambiosPendientes { get; private set; }

        public bool Cerrada => _control.Cerrada || conexion.State != ConnectionState.Open;

        public async Task<FilasLeidasModelo> LeerAsync(string consulta, CancellationToken cancelacion = default)
        {
            using var operacion = OperacionesBaseDatos.Iniciar(cancelacion)
                ?? throw new InvalidOperationException(OperacionesBaseDatos.MensajeCierre);
            cancelacion = operacion.Cancelacion;
            await _turno.WaitAsync(cancelacion);

            try
            {
                _control.Ejecucion = operacion;
                if (Cerrada) throw new InvalidOperationException("La sesión de edición está cerrada; vuelve a abrir el panel.");
                await using var comando = await ComandoAsync(consulta, cancelacion);
                await using var lector = await comando.ExecuteReaderAsync(cancelacion);
                var resultado = new FilasLeidasModelo();

                for (var i = 0; i < lector.FieldCount; i++)
                {
                    resultado.Columnas.Add(lector.GetName(i));
                }

                while (await lector.ReadAsync(cancelacion))
                {
                    var fila = new string?[lector.FieldCount];

                    for (var i = 0; i < lector.FieldCount; i++)
                    {
                        fila[i] = lector.IsDBNull(i) ? null : Texto(lector.GetValue(i));
                    }

                    resultado.Filas.Add(fila);
                }

                return resultado;
            }
            catch (DbException ex)
            {
                throw new InvalidOperationException(Explicar(ex), ex);
            }
            finally
            {
                _control.Ejecucion = null;
                _turno.Release();
            }
        }

        public async Task<ResultadoCambioFilaModelo> CambiarAsync(string sentencia, CancellationToken cancelacion = default)
        {
            using var operacion = OperacionesBaseDatos.Iniciar(cancelacion)
                ?? throw new InvalidOperationException(OperacionesBaseDatos.MensajeCierre);
            cancelacion = operacion.Cancelacion;
            await _turno.WaitAsync(cancelacion);

            try
            {
                _control.Ejecucion = operacion;
                if (Cerrada) throw new InvalidOperationException("La sesión de edición está cerrada; vuelve a abrir el panel.");
                await using var comando = await ComandoAsync(sentencia, cancelacion);
                _transaccion!.Save("fila");

                try
                {
                    var filas = await comando.ExecuteNonQueryAsync(cancelacion);

                    if (filas > 0)
                    {
                        CambiosPendientes++;
                    }

                    return new ResultadoCambioFilaModelo { FilasAfectadas = filas };
                }
                catch (Exception ex) when (ex is DbException or OperationCanceledException)
                {
                    await _transaccion.RollbackAsync("fila", CancellationToken.None);
                    return new ResultadoCambioFilaModelo { Error = ex is DbException baseDatos ? Explicar(baseDatos) : "La edición se canceló; no se aplicó este cambio." };
                }
            }
            finally
            {
                _control.Ejecucion = null;
                _turno.Release();
            }
        }

        public Task<string?> ConfirmarAsync() => TerminarAsync(true);

        public Task<string?> DeshacerAsync() => TerminarAsync(false);

        public async ValueTask DisposeAsync()
        {
            RegistroSesionesEdicion.Quitar(this);
            await _control.DescartarAsync(LiberarRecursosAsync);
        }

        private async Task LiberarRecursosAsync()
        {
            var transaccion = _transaccion;
            _transaccion = null;
            CambiosPendientes = 0;

            try
            {
                if (transaccion is not null)
                {
                    try { await transaccion.RollbackAsync(); }
                    catch (Exception) { }
                    try { await transaccion.DisposeAsync(); }
                    catch (Exception) { }
                }
            }
            finally
            {
                await _control.CerrarConexionAsync(conexion);
            }
        }

        private async Task<string?> TerminarAsync(bool confirmar)
        {
            if (!await _control.TomarParaFinalizarAsync())
            {
                return "La edición sigue trabajando y se está cancelando. Vuelve a decidir cuando termine.";
            }

            try
            {
                if (_transaccion is null)
                {
                    return null;
                }

                if (confirmar || leerEstado is not null)
                {
                    // Impide que COMMIT se acepte como ROLLBACK si una lectura abortó la transacción.
                    _transaccion.Save("migestor_estado_cierre");
                }

                if (confirmar)
                {
                    await _transaccion.CommitAsync();
                }
                else
                {
                    await _transaccion.RollbackAsync();
                }

                await _transaccion.DisposeAsync();
                _transaccion = null;
                CambiosPendientes = 0;
                return null;
            }
            catch (Exception ex) when (ex is DbException or InvalidOperationException)
            {
                var detalle = ex is DbException baseDatos ? Explicar(baseDatos) : mensaje(ex);
                var perdida = conexion.State != ConnectionState.Open;
                var activa = !perdida && _transaccion?.Connection is not null;

                if (activa)
                {
                    try
                    {
                        if (leerEstado is not null)
                        {
                            activa = await leerEstado(_transaccion!);
                        }
                        else
                        {
                            // Connection puede seguir asignada aunque el controlador ya haya terminado la transacción.
                            _transaccion!.Save("migestor_estado_cierre");
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        activa = false;
                    }
                    catch (DbException)
                    {
                        perdida = conexion.State != ConnectionState.Open;
                        activa = !perdida;
                    }
                }

                if (activa)
                {
                    return $"{detalle} Los cambios siguen pendientes; puedes volver a decidir si confirmarlos o deshacerlos.";
                }

                var transaccion = _transaccion;
                _transaccion = null;
                CambiosPendientes = 0;

                try
                {
                    if (transaccion is not null)
                    {
                        await transaccion.DisposeAsync();
                    }
                }
                catch (Exception)
                {
                }

                if (perdida && !confirmar)
                {
                    return null;
                }

                return perdida
                    ? $"{detalle} Se perdió la conexión. No se sabe si los cambios se guardaron; compruébalo antes de volver a editarlos."
                    : $"{detalle} El servidor terminó la transacción; los cambios no se guardaron.";
            }
            finally
            {
                _turno.Release();
            }
        }

        private async Task<DbCommand> ComandoAsync(string texto, CancellationToken cancelacion)
        {
            _transaccion ??= await conexion.BeginTransactionAsync(cancelacion);
            var comando = conexion.CreateCommand();
            comando.CommandText = texto;
            comando.Transaction = _transaccion;
            comando.CommandTimeout = SegundosEspera;
            return comando;
        }

        private string Explicar(DbException ex)
        {
            var texto = mensaje(ex);
            var bloqueo = ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("tiempo de espera", StringComparison.OrdinalIgnoreCase)
                || ex.InnerException is TimeoutException;

            return bloqueo
                ? $"La fila está bloqueada por otra transacción, quizá una pestaña de consulta con cambios sin confirmar. Termínala con COMMIT o ROLLBACK y vuelve a intentarlo. {texto}"
                : texto;
        }

        public static string Texto(object valor) => valor switch
        {
            DateTime fecha when fecha.TimeOfDay == TimeSpan.Zero => fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTime fecha => fecha.ToString("yyyy-MM-dd HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture).TrimEnd('.'),
            DateTimeOffset fecha => fecha.ToString("yyyy-MM-dd HH:mm:ss.FFFFFFF zzz", CultureInfo.InvariantCulture),
            DateOnly fecha => fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            TimeOnly hora => hora.ToString("HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture).TrimEnd('.'),
            TimeSpan hora => hora.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture),
            bool logico => logico ? "1" : "0",
            byte[] bytes => "0x" + Convert.ToHexString(bytes.Length > 32 ? bytes[..32] : bytes) + (bytes.Length > 32 ? "…" : ""),
            IFormattable formateable => formateable.ToString(null, CultureInfo.InvariantCulture),
            _ => valor.ToString() ?? ""
        };
    }
}
