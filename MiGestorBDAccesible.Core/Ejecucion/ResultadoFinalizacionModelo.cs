namespace MiGestorBDAccesible.Core.Ejecucion
{
    public class ResultadoFinalizacionModelo
    {
        public string? Mensaje { get; init; }

        public List<string> Errores { get; init; } = [];

        public bool Correcto => Errores.Count == 0;
    }
}
