namespace MiGestorBDAccesible.Core.Ejecucion
{
    internal sealed class OperacionesBaseDatos
    {
        internal const string MensajeCierre = "Espera a que termine la decisión de cierre antes de ejecutar o editar filas.";

        internal static readonly TimeSpan EsperaCierre = TimeSpan.FromSeconds(5);

        private readonly Lock _cerrojo = new();
        private readonly HashSet<Operacion> _activas = [];
        private TaskCompletionSource _terminadas = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _bloqueos;

        public bool HayOperaciones
        {
            get { lock (_cerrojo) return _activas.Count > 0; }
        }

        public Operacion? Iniciar(CancellationToken cancelacion)
        {
            lock (_cerrojo)
            {
                if (_bloqueos > 0) return null;
                if (_activas.Count == 0) _terminadas = new(TaskCreationOptions.RunContinuationsAsynchronously);
                var operacion = new Operacion(this, cancelacion);
                _activas.Add(operacion);
                return operacion;
            }
        }

        public IDisposable Bloquear()
        {
            lock (_cerrojo) _bloqueos++;
            return new Bloqueo(this);
        }

        public void CancelarActivas()
        {
            List<Operacion> activas;
            lock (_cerrojo) activas = [.. _activas];
            foreach (var operacion in activas) operacion.Cancelar();
        }

        public async Task<bool> EsperarAsync()
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

        private sealed class Bloqueo(OperacionesBaseDatos propietario) : IDisposable
        {
            private bool _liberado;
            public void Dispose()
            {
                lock (propietario._cerrojo)
                {
                    if (_liberado) return;
                    _liberado = true;
                    propietario._bloqueos--;
                }
            }
        }

        internal sealed class Operacion(OperacionesBaseDatos propietario, CancellationToken cancelacion) : IDisposable
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
                lock (propietario._cerrojo)
                {
                    if (!propietario._activas.Remove(this)) return;
                    if (propietario._activas.Count == 0) propietario._terminadas.TrySetResult();
                }
                _cancelacion.Dispose();
            }
        }
    }
}
