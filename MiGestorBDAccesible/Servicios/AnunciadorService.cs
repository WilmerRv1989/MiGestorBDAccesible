namespace MiGestorBDAccesible.Servicios
{
    public class AnunciadorService
    {
        public event Func<string, bool, Task>? AnuncioSolicitado;

        public Task AnunciarAsync(string mensaje) => EmitirAsync(mensaje, false);

        public Task AnunciarUrgenteAsync(string mensaje) => EmitirAsync(mensaje, true);

        private Task EmitirAsync(string mensaje, bool urgente)
        {
            return AnuncioSolicitado?.Invoke(mensaje, urgente) ?? Task.CompletedTask;
        }
    }
}
