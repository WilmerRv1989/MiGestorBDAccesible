using System.Text.Json.Serialization;

namespace MiGestorBDAccesible.Core.Conexiones
{
    public class ConexionModelo
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public string Nombre { get; set; } = "";

        public TipoMotor Motor { get; set; } = TipoMotor.SqlServer;

        public string Servidor { get; set; } = "localhost";

        public int? Puerto { get; set; }

        public TipoAutenticacion Autenticacion { get; set; } = TipoAutenticacion.UsuarioYContrasena;

        public string? Usuario { get; set; }

        public string? ContrasenaCifrada { get; set; }

        public bool ConfiarCertificado { get; set; }

        public string? BaseDatosInicial { get; set; }

        public string? NombreServicio { get; set; }

        [JsonIgnore]
        public string Direccion
        {
            get
            {
                var direccion = Puerto.HasValue ? $"{Servidor}, puerto {Puerto}" : Servidor;
                return string.IsNullOrWhiteSpace(NombreServicio) ? direccion : $"{direccion}, servicio {NombreServicio}";
            }
        }

        [JsonIgnore]
        public string Resumen
        {
            get
            {
                var acceso = Autenticacion == TipoAutenticacion.Windows
                    ? "autenticación de Windows"
                    : $"usuario {Usuario}";

                return $"{Motor.Nombre()}, {Direccion}, {acceso}";
            }
        }
    }
}
