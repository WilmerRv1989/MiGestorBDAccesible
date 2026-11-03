using System.Security.Cryptography;
using System.Xml;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace MiGestorBDAccesible.Core.Conexiones
{
    public class ProtectorDataProtection : IProtectorContrasenas
    {
        private const string NombreAplicacion = "MiGestorBDAccesible";
        private const string Proposito = "MiGestorBDAccesible.Conexiones.Contrasenas.v1";
        private const UnixFileMode PermisosArchivo = UnixFileMode.UserRead | UnixFileMode.UserWrite;

        private readonly string _carpetaClaves;

        public ProtectorDataProtection(string carpetaClaves)
        {
            _carpetaClaves = Path.GetFullPath(carpetaClaves);
        }

        public string Proteger(string texto)
        {
            try
            {
                var cifrada = CrearProtector().Protect(texto);
                CorregirPermisosArchivos();
                return cifrada;
            }
            catch (Exception error) when (error is CryptographicException or IOException or UnauthorizedAccessException)
            {
                var causa = error;
                for (Exception? interna = error; interna is not null; interna = interna.InnerException)
                {
                    if (interna is XmlException)
                    {
                        throw new IOException($"No se pudo cifrar la contraseña con las claves guardadas en «{_carpetaClaves}». " +
                            "Antes de volver a guardar, restaura un anillo de claves válido o guarda una copia y aparta la carpeta «claves» dañada para crear un anillo nuevo. " +
                            "No se ha borrado ninguna clave.", error);
                    }

                    causa = interna;
                }

                throw new IOException($"No se pudo cifrar la contraseña con las claves de la carpeta «{_carpetaClaves}». " +
                    $"Comprueba el acceso a la carpeta y el espacio disponible, y vuelve a intentarlo. Detalle: {causa.Message}", error);
            }
        }

        public ContrasenaGuardada Desproteger(string? cifrada)
        {
            if (string.IsNullOrEmpty(cifrada))
            {
                return ContrasenaGuardada.SinContrasena;
            }

            try
            {
                return ContrasenaGuardada.Disponible(CrearProtector().Unprotect(cifrada));
            }
            catch (Exception error) when (error is CryptographicException or FormatException
                or IOException or UnauthorizedAccessException or XmlException or InvalidOperationException)
            {
                return ContrasenaGuardada.NoDescifrable;
            }
        }

        private IDataProtector CrearProtector()
        {
            if (OperatingSystem.IsMacOS())
            {
                var permisos = PermisosArchivo | UnixFileMode.UserExecute;
                Directory.CreateDirectory(_carpetaClaves, permisos);
                File.SetUnixFileMode(_carpetaClaves, permisos);
            }
            else
            {
                Directory.CreateDirectory(_carpetaClaves);
            }

            CorregirPermisosArchivos();

            // Cada operación consulta el anillo en disco, aunque se hayan perdido sus claves.
            return DataProtectionProvider.Create(new DirectoryInfo(_carpetaClaves),
                opciones => opciones.SetApplicationName(NombreAplicacion)).CreateProtector(Proposito);
        }

        private void CorregirPermisosArchivos()
        {
            if (OperatingSystem.IsMacOS())
            {
                foreach (var archivo in Directory.EnumerateFiles(_carpetaClaves))
                {
                    File.SetUnixFileMode(archivo, PermisosArchivo);
                }
            }
        }
    }
}
