using MiGestorBDAccesible.Core.Conexiones;
using System.ComponentModel.DataAnnotations;

namespace MiGestorBDAccesible.Modelos
{
    public class FormularioConexionModelo : IValidatableObject
    {
        [Required(ErrorMessage = "Escribe un nombre para la conexión.")]
        public string Nombre { get; set; } = "";

        public TipoMotor Motor { get; set; } = TipoMotor.SqlServer;

        [Required(ErrorMessage = "Escribe el servidor.")]
        public string Servidor { get; set; } = "localhost";

        [Range(1, 65535, ErrorMessage = "El puerto debe estar entre 1 y 65535.")]
        public int? Puerto { get; set; } = TipoMotor.SqlServer.PuertoPredeterminado();

        public TipoAutenticacion Autenticacion { get; set; } = TipoAutenticacion.UsuarioYContrasena;

        public string? Usuario { get; set; }

        public string? Contrasena { get; set; }

        public bool ConfiarCertificado { get; set; }

        public string? BaseDatosInicial { get; set; }

        public string? NombreServicio { get; set; }

        public bool TieneContrasenaGuardada { get; set; }

        public static FormularioConexionModelo Desde(ConexionModelo conexion) => new()
        {
            Nombre = conexion.Nombre,
            Motor = conexion.Motor,
            Servidor = conexion.Servidor,
            Puerto = conexion.Puerto,
            Autenticacion = conexion.Autenticacion,
            Usuario = conexion.Usuario,
            ConfiarCertificado = conexion.ConfiarCertificado,
            BaseDatosInicial = conexion.BaseDatosInicial,
            NombreServicio = conexion.NombreServicio,
            TieneContrasenaGuardada = !string.IsNullOrEmpty(conexion.ContrasenaCifrada)
        };

        public void AplicarA(ConexionModelo conexion)
        {
            conexion.Nombre = Nombre.Trim();
            conexion.Motor = Motor;
            conexion.Servidor = Servidor.Trim();
            conexion.Puerto = Puerto;
            conexion.Autenticacion = Autenticacion;
            conexion.Usuario = Usuario?.Trim();
            conexion.ConfiarCertificado = ConfiarCertificado;
            conexion.BaseDatosInicial = !Motor.AdmiteBaseInicial() || string.IsNullOrWhiteSpace(BaseDatosInicial) ? null : BaseDatosInicial.Trim();
            conexion.NombreServicio = Motor.UsaNombreServicio() && !string.IsNullOrWhiteSpace(NombreServicio) ? NombreServicio.Trim() : null;
        }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (Motor.UsaNombreServicio() && string.IsNullOrWhiteSpace(NombreServicio))
            {
                yield return new ValidationResult("Escribe el nombre de servicio.", [nameof(NombreServicio)]);
            }

            if (Autenticacion == TipoAutenticacion.UsuarioYContrasena && string.IsNullOrWhiteSpace(Usuario))
            {
                yield return new ValidationResult("Escribe el usuario.", [nameof(Usuario)]);
            }

            if (Autenticacion == TipoAutenticacion.UsuarioYContrasena
                && string.IsNullOrEmpty(Contrasena)
                && !TieneContrasenaGuardada)
            {
                yield return new ValidationResult("Escribe la contraseña.", [nameof(Contrasena)]);
            }
        }
    }
}
