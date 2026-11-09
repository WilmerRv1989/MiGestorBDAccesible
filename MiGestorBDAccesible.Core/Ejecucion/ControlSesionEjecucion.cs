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
            Ejecucion?.Cancelar();
            return Turno.WaitAsync(OperacionesBaseDatos.EsperaCierre);
        }

        public async Task DescartarAsync(Func<Task> cerrar)
        {
            Cerrada = true;
            if (await TomarParaFinalizarAsync())
            {
                try { await cerrar(); }
                finally { Turno.Release(); }
            }
            else
            {
                // La salida no espera indefinidamente; la conexión se libera cuando termine su uso exclusivo.
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
