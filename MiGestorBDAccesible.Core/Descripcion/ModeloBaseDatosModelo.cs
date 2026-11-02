using MiGestorBDAccesible.Core.Estructura;

namespace MiGestorBDAccesible.Core.Descripcion
{
    public class ModeloBaseDatosModelo
    {
        public string BaseDatos { get; init; } = "";

        public List<DetalleTablaModelo> Tablas { get; init; } = [];

        public List<DetalleObjetoModelo> Objetos { get; init; } = [];

        public DetalleTablaModelo? Buscar(TablaModelo tabla) =>
            Tablas.FirstOrDefault(t => t.Tabla.NombreCompleto == tabla.NombreCompleto);
    }

    public class OrdenCargaModelo
    {
        public List<List<DetalleTablaModelo>> Niveles { get; init; } = [];

        public List<List<DetalleTablaModelo>> Ciclos { get; init; } = [];
    }

    public class PasoJoinModelo
    {
        public TablaModelo Anterior { get; init; } = new();

        public TablaModelo Nueva { get; init; } = new();

        public LlaveForaneaModelo Llave { get; init; } = new();

        public bool NuevaEsOrigen => Llave.TablaOrigen.NombreCompleto == Nueva.NombreCompleto;

        public IReadOnlyList<string> ColumnasNueva => NuevaEsOrigen ? Llave.ColumnasOrigen : Llave.ColumnasDestino;

        public IReadOnlyList<string> ColumnasAnterior => NuevaEsOrigen ? Llave.ColumnasDestino : Llave.ColumnasOrigen;
    }

    public class TablaDescritaModelo
    {
        public DetalleTablaModelo Detalle { get; init; } = new();

        public string Texto { get; init; } = "";
    }

    public class DescripcionModeloModelo
    {
        public string Titulo { get; init; } = "";

        public List<string> Resumen { get; init; } = [];

        public List<TablaDescritaModelo> Tablas { get; init; } = [];

        public List<SeccionDescripcionModelo> Secciones { get; init; } = [];
    }
}
