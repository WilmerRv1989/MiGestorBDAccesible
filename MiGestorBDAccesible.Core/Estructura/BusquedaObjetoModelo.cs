namespace MiGestorBDAccesible.Core.Estructura
{
    public enum TipoResultadoBusqueda
    {
        Tabla,
        Columna,
        Vista,
        Procedimiento,
        Funcion,
        Paquete,
        Subprograma,
        Indice,
        ClavePrincipal,
        LlaveForanea,
        Unica,
        Check,
        Disparador,
        Secuencia
    }

    public enum CategoriaBusqueda
    {
        Todo,
        Tablas,
        Columnas,
        Vistas,
        Rutinas,
        IndicesRestricciones,
        Disparadores,
        Secuencias
    }

    public class ResultadoBusquedaObjetoModelo
    {
        public TipoResultadoBusqueda Tipo { get; init; }

        public string Esquema { get; init; } = "";

        public string Nombre { get; init; } = "";

        public TablaModelo? Tabla { get; init; }

        public string? Detalle { get; init; }

        public long? Id { get; init; }

        public string NombreCompleto => Esquema == "" ? Nombre : $"{Esquema}.{Nombre}";
    }
}
