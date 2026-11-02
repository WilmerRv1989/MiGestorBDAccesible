using System.Data.Common;
using System.Globalization;

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

        public static async Task TerminarTodasAsync(bool confirmar)
        {
            foreach (var sesion in _abiertas.Keys.ToList())
            {
                if (confirmar)
                {
                    await sesion.ConfirmarAsync();
                }

                await sesion.DisposeAsync();
            }
        }
    }

    public sealed class SesionEdicionAdo : ISesionEdicion
    {
        private readonly DbConnection conexion;

        private readonly Func<Exception, string> mensaje;

        public SesionEdicionAdo(DbConnection conexion, Func<Exception, string> mensaje)
        {
            this.conexion = conexion;
            this.mensaje = mensaje;
            RegistroSesionesEdicion.Agregar(this);
        }

        private const int SegundosEspera = 15;

        private readonly SemaphoreSlim _turno = new(1, 1);

        private DbTransaction? _transaccion;

        public int CambiosPendientes { get; private set; }

        public async Task<FilasLeidasModelo> LeerAsync(string consulta, CancellationToken cancelacion = default)
        {
            await _turno.WaitAsync(cancelacion);

            try
            {
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
                _turno.Release();
            }
        }

        public async Task<ResultadoCambioFilaModelo> CambiarAsync(string sentencia, CancellationToken cancelacion = default)
        {
            await _turno.WaitAsync(cancelacion);

            try
            {
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
                catch (DbException ex)
                {
                    await _transaccion.RollbackAsync("fila", CancellationToken.None);
                    return new ResultadoCambioFilaModelo { Error = Explicar(ex) };
                }
            }
            finally
            {
                _turno.Release();
            }
        }

        public Task<string?> ConfirmarAsync() => TerminarAsync(true);

        public Task<string?> DeshacerAsync() => TerminarAsync(false);

        public async ValueTask DisposeAsync()
        {
            RegistroSesionesEdicion.Quitar(this);

            if (_transaccion is not null)
            {
                try
                {
                    await _transaccion.RollbackAsync();
                }
                catch (Exception)
                {
                }

                await _transaccion.DisposeAsync();
                _transaccion = null;
            }

            CambiosPendientes = 0;
            await conexion.DisposeAsync();
        }

        private async Task<string?> TerminarAsync(bool confirmar)
        {
            await _turno.WaitAsync();

            try
            {
                if (_transaccion is null)
                {
                    return null;
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
            catch (DbException ex)
            {
                return Explicar(ex);
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
