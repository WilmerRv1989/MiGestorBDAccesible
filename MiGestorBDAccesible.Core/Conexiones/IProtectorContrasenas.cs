namespace MiGestorBDAccesible.Core.Conexiones
{
    public interface IProtectorContrasenas
    {
        string Proteger(string texto);

        ContrasenaGuardada Desproteger(string? cifrada);
    }
}
