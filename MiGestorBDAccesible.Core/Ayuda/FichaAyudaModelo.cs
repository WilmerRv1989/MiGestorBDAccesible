namespace MiGestorBDAccesible.Core.Ayuda
{
    public class SeccionAyudaModelo
    {
        public string Titulo { get; init; } = "";

        public List<string> Parrafos { get; init; } = [];

        public string? Ejemplo { get; init; }
    }

    public class NotaMotorModelo
    {
        public string Motor { get; init; } = "";

        public string Texto { get; init; } = "";
    }

    public class FuenteAyudaModelo
    {
        public string Titulo { get; init; } = "";

        public string Url { get; init; } = "";
    }

    public class FichaAyudaModelo
    {
        public string Id { get; init; } = "";

        public string Categoria { get; init; } = "";

        public string Titulo { get; init; } = "";

        public List<string> Alias { get; init; } = [];

        public string Resumen { get; init; } = "";

        public List<SeccionAyudaModelo> Secciones { get; init; } = [];

        public List<NotaMotorModelo> OtrosMotores { get; init; } = [];

        public List<string> Relacionadas { get; init; } = [];

        public List<FuenteAyudaModelo> Fuentes { get; init; } = [];
    }

    public class ContenidoAyudaModelo
    {
        public string Motor { get; init; } = "";

        public string TituloProgramacion { get; init; } = "PL/SQL";

        public List<FichaAyudaModelo> Fichas { get; init; } = [];
    }
}
