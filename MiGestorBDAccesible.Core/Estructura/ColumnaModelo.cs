namespace MiGestorBDAccesible.Core.Estructura
{
    public class ColumnaModelo
    {
        public string Nombre { get; init; } = "";

        public string TipoDatos { get; init; } = "";

        public bool PermiteNulos { get; init; }

        public bool EsIdentidad { get; init; }

        public long? SemillaIdentidad { get; init; }

        public long? IncrementoIdentidad { get; init; }

        public bool? IdentidadSiempre { get; init; }

        public long? SiguienteIdentidad { get; init; }

        public string? ValorPredeterminado { get; init; }

        public string? NombrePredeterminado { get; init; }

        public string? Formula { get; init; }

        public bool EsPersistente { get; init; }

        public bool EsCalculada => Formula is not null;
    }
}
