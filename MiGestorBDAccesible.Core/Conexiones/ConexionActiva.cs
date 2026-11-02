using MiGestorBDAccesible.Core.Proveedores;

namespace MiGestorBDAccesible.Core.Conexiones
{
    public class ConexionActiva(ConexionModelo conexion, string? contrasena, ResultadoPruebaConexion servidor)
    {
        public ConexionModelo Conexion { get; } = conexion;

        public string? Contrasena { get; } = contrasena;

        public ResultadoPruebaConexion Servidor { get; } = servidor;

        public ConexionActiva? Principal { get; private init; }

        public ConexionActiva NuevaSesion() => new(Conexion, Contrasena, Servidor) { Principal = this };

        public bool Incluye(ConexionActiva otra) => ReferenceEquals(this, otra) || ReferenceEquals(otra.Principal, this);
    }
}
