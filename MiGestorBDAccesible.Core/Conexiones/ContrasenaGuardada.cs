namespace MiGestorBDAccesible.Core.Conexiones
{
    public enum EstadoContrasena
    {
        SinContrasena,
        Disponible,
        NoDescifrable
    }

    public sealed record ContrasenaGuardada
    {
        public const string AvisoNoDescifrable = "La contraseña guardada no se puede usar en este equipo. Vuelve a escribirla.";

        public static ContrasenaGuardada SinContrasena { get; } = new(EstadoContrasena.SinContrasena, null);

        public static ContrasenaGuardada NoDescifrable { get; } = new(EstadoContrasena.NoDescifrable, null);

        public static ContrasenaGuardada Disponible(string texto) => new(EstadoContrasena.Disponible, texto);

        private ContrasenaGuardada(EstadoContrasena estado, string? texto)
        {
            Estado = estado;
            Texto = texto;
        }

        public EstadoContrasena Estado { get; }

        public string? Texto { get; }
    }
}
