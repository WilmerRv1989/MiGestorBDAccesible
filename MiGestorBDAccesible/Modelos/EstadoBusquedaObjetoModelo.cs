using MiGestorBDAccesible.Core.Estructura;

namespace MiGestorBDAccesible.Modelos
{
    public class EstadoBusquedaObjetoModelo
    {
        public string Texto { get; set; } = "";

        public CategoriaBusqueda Categoria { get; set; }

        public string BaseDatos { get; set; } = "";

        public string? TextoBuscado { get; set; }

        public List<ResultadoBusquedaObjetoModelo>? Resultados { get; set; }

        public bool HayMas { get; set; }

        public int Elegido { get; set; }
    }
}
