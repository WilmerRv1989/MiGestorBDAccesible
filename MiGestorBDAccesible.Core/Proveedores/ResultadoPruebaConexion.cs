namespace MiGestorBDAccesible.Core.Proveedores
{
    public record ResultadoPruebaConexion(
        bool Exito,
        TimeSpan Duracion,
        string? Producto = null,
        string? Edicion = null,
        string? Version = null,
        string? Error = null,
        string? Advertencia = null);
}
