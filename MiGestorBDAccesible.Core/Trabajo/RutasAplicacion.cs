namespace MiGestorBDAccesible.Core.Trabajo
{
    public static class RutasAplicacion
    {
        public static string Carpeta { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MiGestorBDAccesible");

        public static string Archivo(string nombre) => Path.Combine(Carpeta, nombre);
    }
}
