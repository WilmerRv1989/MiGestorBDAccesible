namespace MiGestorBDAccesible.Core.Estructura
{
    public class RelacionMuchosAMuchosModelo
    {
        public TablaModelo TablaIntermedia { get; init; } = new();

        public TablaModelo OtraTabla { get; init; } = new();
    }

    public class DetalleTablaModelo
    {
        public TablaModelo Tabla { get; init; } = new();

        public long Filas { get; init; }

        public List<ColumnaModelo> Columnas { get; init; } = [];

        public List<IndiceModelo> Indices { get; init; } = [];

        public List<LlaveForaneaModelo> LlavesForaneas { get; init; } = [];

        public List<LlaveForaneaModelo> Referencias { get; init; } = [];

        public List<RestriccionCheckModelo> Restricciones { get; init; } = [];

        public List<RelacionMuchosAMuchosModelo> RelacionesMuchosAMuchos { get; init; } = [];

        public IndiceModelo? ClavePrincipal => Indices.FirstOrDefault(i => i.EsClavePrincipal);

        public bool EsTablaIntermedia =>
            ClavePrincipal is { } clave
            && clave.ColumnasClave.Count() >= 2
            && LlavesForaneas.Select(l => l.TablaDestino.NombreCompleto).Distinct().Count() >= 2
            && clave.ColumnasClave.ToHashSet(StringComparer.OrdinalIgnoreCase)
                .SetEquals(LlavesForaneas.SelectMany(l => l.ColumnasOrigen));
    }
}
