namespace MiGestorBDAccesible.Core.Ejecucion
{
    internal static class OperacionesBaseDatos
    {
        internal const string MensajeCierre = "Espera a que termine la decisión de cierre antes de ejecutar o editar filas.";

        internal static readonly TimeSpan EsperaCierre = TimeSpan.FromSeconds(5);

        private static readonly Lock _cerrojo = new();
        private static readonly HashSet<Operacion> _activas = [];
        private static TaskCompletionSource _terminadas = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private static int _bloqueos;

        public static bool HayOperaciones
        {
            get { lock (_cerrojo) return _activas.Count > 0; }
        }

        public static Operacion? Iniciar(CancellationToken cancelacion)
        {
            lock (_cerrojo)
            {
                if (_bloqueos > 0) return null;
                if (_activas.Count == 0) _terminadas = new(TaskCreationOptions.RunContinuationsAsynchronously);
                var operacion = new Operacion(cancelacion);
                _activas.Add(operacion);
                return operacion;
            }
        }

        public static IDisposable Bloquear()
        {
            List<Operacion> activas;
            lock (_cerrojo)
            {
                _bloqueos++;
                activas = [.. _activas];
            }

            foreach (var operacion in activas) operacion.Cancelar();
            return new Bloqueo();
        }

        public static async Task<bool> EsperarAsync()
        {
            Task terminadas;
            lock (_cerrojo) terminadas = _activas.Count == 0 ? Task.CompletedTask : _terminadas.Task;
            try
            {
                await terminadas.WaitAsync(EsperaCierre);
                return true;
            }
            catch (TimeoutException)
            {
                return false;
            }
        }

        private sealed class Bloqueo : IDisposable
        {
            private bool _liberado;
            public void Dispose()
            {
                lock (_cerrojo)
                {
                    if (_liberado) return;
                    _liberado = true;
                    _bloqueos--;
                }
            }
        }

        internal sealed class Operacion(CancellationToken cancelacion) : IDisposable
        {
            private readonly CancellationTokenSource _cancelacion = CancellationTokenSource.CreateLinkedTokenSource(cancelacion);
            public CancellationToken Cancelacion => _cancelacion.Token;

            public void Cancelar() => _ = CancelarAsync();

            private async Task CancelarAsync()
            {
                try { await _cancelacion.CancelAsync(); }
                catch (Exception) { }
            }

            public void Dispose()
            {
                lock (_cerrojo)
                {
                    if (!_activas.Remove(this)) return;
                    if (_activas.Count == 0) _terminadas.TrySetResult();
                }
                _cancelacion.Dispose();
            }
        }
    }
}
