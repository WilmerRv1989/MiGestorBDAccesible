namespace MiGestorBDAccesible.Modelos
{
    public class ElementoMenuModelo
    {
        public string Texto { get; init; } = "";

        public Func<Task>? Accion { get; init; }

        public bool EsSeparador { get; init; }

        public bool EsPeligro { get; init; }

        public static ElementoMenuModelo Separador() => new() { EsSeparador = true };
    }

    public record ScriptAbiertoModelo(string? BaseDatos, string Texto, bool Ejecutar, string Descripcion);

    public record SolicitudTablaModelo(string BaseDatos, MiGestorBDAccesible.Core.Definicion.DefinicionTablaModelo? Definicion = null, string? Mensaje = null, MiGestorBDAccesible.Core.Estructura.DetalleTablaModelo? Original = null);
}
