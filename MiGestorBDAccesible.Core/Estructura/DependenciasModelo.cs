namespace MiGestorBDAccesible.Core.Estructura
{
    public class DependenciaModelo
    {
        public ResultadoBusquedaObjetoModelo? Objeto { get; init; }

        public string? Inexistente { get; init; }

        public string? Nota { get; init; }

        public bool Posible { get; init; }

        public bool OtroEsquema { get; init; }
    }

    public class DependenciasModelo
    {
        public List<DependenciaModelo> Usa { get; init; } = [];

        public List<DependenciaModelo> UsadoPor { get; init; } = [];
    }
}
