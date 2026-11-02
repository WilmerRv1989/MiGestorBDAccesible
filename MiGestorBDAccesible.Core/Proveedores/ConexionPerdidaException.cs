namespace MiGestorBDAccesible.Core.Proveedores
{
    public class ConexionPerdidaException(string mensaje, Exception interna) : Exception(mensaje, interna);
}
