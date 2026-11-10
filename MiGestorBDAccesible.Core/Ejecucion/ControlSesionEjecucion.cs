using System.Data.Common;

namespace MiGestorBDAccesible.Core.Ejecucion
{
    internal sealed class ControlSesionEjecucion
    {
        public SemaphoreSlim Turno { get; } = new(1, 1);
        public OperacionesBaseDatos.Operacion? Ejecucion { get; set; }
        public bool Cerrada { get; private set; }
        private int _liberada;

        public Task<bool> TomarParaFinalizarAsync()
        {
            return Task.FromResult(Turno.Wait(0));
        }

        public async Task DescartarAsync(Func<Task> cerrar)
        {
            Cerrada = true;
            Ejecucion?.Cancelar();
            if (await Turno.WaitAsync(OperacionesBaseDatos.EsperaCierre))
            {
                try { await cerrar(); }
                finally { Turno.Release(); }
            }
            else
            {
                _ = CerrarAlTerminarAsync(cerrar);
            }
        }

        private async Task CerrarAlTerminarAsync(Func<Task> cerrar)
        {
            await Turno.WaitAsync();
            try { await cerrar(); }
            catch (Exception) { }
            finally { Turno.Release(); }
        }

        public async Task CerrarConexionAsync(DbConnection conexion)
        {
            Cerrada = true;
            if (Interlocked.Exchange(ref _liberada, 1) != 0) return;
            try { await conexion.DisposeAsync(); }
            catch (Exception) { }
        }
    }
}
