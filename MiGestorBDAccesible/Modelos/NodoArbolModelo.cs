using MiGestorBDAccesible.Core.Estructura;

namespace MiGestorBDAccesible.Modelos
{
    public enum TipoNodo
    {
        Servidor,
        Carpeta,
        BaseDatos,
        Tabla,
        Elemento,
        Objeto,
        Mensaje
    }

    public class NodoArbolModelo
    {
        private static int _contador;

        public string Id { get; } = $"nodo-{Interlocked.Increment(ref _contador)}";

        public string Texto { get; set; } = "";

        public string? Nombre { get; init; }

        public TipoNodo Tipo { get; init; }

        public NodoArbolModelo? Padre { get; init; }

        public string? BaseDatos { get; init; }

        public TablaModelo? Tabla { get; init; }

        public ObjetoBaseDatosModelo? Objeto { get; init; }

        public TipoObjeto? TipoCarpeta { get; init; }

        public IndiceModelo? Indice { get; init; }

        public RestriccionCheckModelo? Check { get; init; }

        public bool EsSistema { get; init; }

        public List<string> TemasAyuda { get; init; } = [];

        public Func<NodoArbolModelo, Task<List<NodoArbolModelo>>>? CargarHijos { get; init; }

        public List<NodoArbolModelo>? Hijos { get; set; }

        public bool Expandido { get; set; }

        public bool Cargando { get; set; }

        public bool PuedeExpandirse => CargarHijos is not null || Hijos is { Count: > 0 };

        public int Nivel => Padre is null ? 1 : Padre.Nivel + 1;
    }
}
