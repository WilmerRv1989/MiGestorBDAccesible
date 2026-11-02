namespace MiGestorBDAccesible.Core.Estructura
{
    public enum ReglaReferencial
    {
        SinAccion,
        Cascada,
        EstablecerNulo,
        EstablecerPredeterminado,
        Restringir
    }

    public class LlaveForaneaModelo
    {
        public string Nombre { get; init; } = "";

        public TablaModelo TablaOrigen { get; init; } = new();

        public List<string> ColumnasOrigen { get; init; } = [];

        public TablaModelo TablaDestino { get; init; } = new();

        public List<string> ColumnasDestino { get; init; } = [];

        public ReglaReferencial AlEliminar { get; init; }

        public ReglaReferencial AlActualizar { get; init; }

        public bool Habilitada { get; init; } = true;
    }
}
