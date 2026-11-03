using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace MiGestorBDAccesible.Core.Conexiones
{
    [SupportedOSPlatform("windows")]
    public class ProtectorDpapi : IProtectorContrasenas
    {
        public string Proteger(string texto)
        {
            var datos = ProtectedData.Protect(Encoding.UTF8.GetBytes(texto), null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(datos);
        }

        public ContrasenaGuardada Desproteger(string? cifrada)
        {
            if (string.IsNullOrEmpty(cifrada))
            {
                return ContrasenaGuardada.SinContrasena;
            }

            try
            {
                var datos = ProtectedData.Unprotect(Convert.FromBase64String(cifrada), null, DataProtectionScope.CurrentUser);
                return ContrasenaGuardada.Disponible(Encoding.UTF8.GetString(datos));
            }
            catch (Exception error) when (error is CryptographicException or FormatException)
            {
                return ContrasenaGuardada.NoDescifrable;
            }
        }
    }
}
