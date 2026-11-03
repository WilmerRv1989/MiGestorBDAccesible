namespace MiGestorBDAccesible.Core.Conexiones
{
    public class ProtectorDataProtection : IProtectorContrasenas
    {
        public ProtectorDataProtection(string carpetaClaves)
        {
            throw new NotImplementedException();
        }

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
