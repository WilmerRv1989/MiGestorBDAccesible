using System.Runtime.Versioning;

namespace MiGestorBDAccesible.Core.Conexiones
{
    [SupportedOSPlatform("windows")]
    public class ProtectorDpapi : IProtectorContrasenas
    {
        public string Proteger(string texto)
        {
            throw new NotImplementedException();
        }

        public ContrasenaGuardada Desproteger(string? cifrada)
        {
            throw new NotImplementedException();
        }
    }
}
