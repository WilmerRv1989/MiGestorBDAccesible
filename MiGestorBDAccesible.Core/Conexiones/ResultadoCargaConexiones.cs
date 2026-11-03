namespace MiGestorBDAccesible.Core.Conexiones
{
    public enum EstadoCargaConexiones
    {
        Correcta,
        Danado,
        Parcial,
        ErrorLectura
    }

    public class ResultadoCargaConexiones
    {
        public List<ConexionModelo> Conexiones { get; init; } = [];

        public EstadoCargaConexiones Estado { get; init; }

        public int Descartadas { get; init; }

        public string? RutaCopia { get; init; }

        public Exception? Error { get; init; }
    }
}
