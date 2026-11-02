namespace MiGestorBDAccesible.Core.Estructura
{
    public class TablaModelo
    {
        public string Esquema { get; init; } = "dbo";

        public string Nombre { get; init; } = "";

        public string EsquemaPredeterminado { get; init; } = "dbo";

        public string NombreCompleto => $"{Esquema}.{Nombre}";

        public string NombreCorto(string? esquemaPredeterminado = null) =>
            Esquema == (esquemaPredeterminado ?? EsquemaPredeterminado) ? Nombre : NombreCompleto;
    }
}
