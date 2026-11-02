namespace MiGestorBDAccesible.Core.Descripcion
{
    public class SeccionDescripcionModelo
    {
        public string Titulo { get; init; } = "";

        public List<string> Frases { get; init; } = [];
    }

    public class DescripcionTablaModelo
    {
        public string Titulo { get; init; } = "";

        public List<string> Resumen { get; init; } = [];

        public List<SeccionDescripcionModelo> Secciones { get; init; } = [];
    }
}
