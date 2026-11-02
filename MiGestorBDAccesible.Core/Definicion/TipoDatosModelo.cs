namespace MiGestorBDAccesible.Core.Definicion
{
    public class TipoDatosModelo
    {
        public string Nombre { get; init; } = "";

        public string Descripcion { get; init; } = "";

        public string Categoria { get; init; } = "";

        public bool UsaLongitud { get; init; }

        public bool AdmiteMax { get; init; }

        public int LongitudPredeterminada { get; init; }

        public int LongitudMaxima { get; init; }

        public bool UsaPrecision { get; init; }

        public bool UsaFraccionSegundos { get; init; }

        public int FraccionMaxima { get; init; } = 7;

        public int FraccionPredeterminada { get; init; } = 7;

        public bool PrecisionOpcional { get; init; }

        public bool AdmiteIdentidad { get; init; }
    }
}
