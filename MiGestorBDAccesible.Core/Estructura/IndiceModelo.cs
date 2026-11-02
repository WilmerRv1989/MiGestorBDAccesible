namespace MiGestorBDAccesible.Core.Estructura
{
    public class ColumnaIndiceModelo
    {
        public string Nombre { get; init; } = "";

        public bool Descendente { get; init; }

        public bool Incluida { get; init; }
    }

    public class IndiceModelo
    {
        public string Nombre { get; init; } = "";

        public bool EsAgrupado { get; init; }

        public bool EsUnico { get; init; }

        public bool EsClavePrincipal { get; init; }

        public bool EsRestriccionUnica { get; init; }

        public string? Metodo { get; init; }

        public List<ColumnaIndiceModelo> Columnas { get; init; } = [];

        public IEnumerable<string> ColumnasClave => Columnas.Where(c => !c.Incluida).Select(c => c.Nombre);
    }
}
