namespace MiGestorBDAccesible.Core.Ejecucion
{
    public enum TipoMensaje
    {
        Informacion,
        FilasAfectadas,
        Error
    }

    public class MensajeEjecucionModelo
    {
        public TipoMensaje Tipo { get; init; }

        public string Texto { get; init; } = "";

        public int? Linea { get; init; }

        public int? Numero { get; init; }
    }

    public class ColumnaResultadoModelo
    {
        public string Nombre { get; init; } = "";

        public string TipoDatos { get; init; } = "";
    }

    public class ConjuntoResultadosModelo
    {
        public List<ColumnaResultadoModelo> Columnas { get; init; } = [];

        public List<string?[]> Filas { get; init; } = [];

        public long TotalFilas { get; set; }

        public bool Recortado => TotalFilas > Filas.Count;
    }

    public class ResultadoEjecucionModelo
    {
        public List<ConjuntoResultadosModelo> Conjuntos { get; } = [];

        public List<MensajeEjecucionModelo> Mensajes { get; } = [];

        public TimeSpan Duracion { get; set; }

        public bool Cancelado { get; set; }

        public string? BaseDatosFinal { get; set; }

        public IEnumerable<MensajeEjecucionModelo> Errores => Mensajes.Where(m => m.Tipo == TipoMensaje.Error);

        public long FilasAfectadas { get; set; }

        public bool CambiosPendientes { get; set; }
    }
}
