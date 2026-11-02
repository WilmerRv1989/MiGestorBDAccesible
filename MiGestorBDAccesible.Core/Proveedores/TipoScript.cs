namespace MiGestorBDAccesible.Core.Proveedores
{
    public enum TipoScript
    {
        Crear,
        Seleccionar,
        SeleccionarPrimeras,
        Insertar,
        Actualizar,
        Eliminar,
        EliminarTabla
    }

    public static class TipoScriptExtensiones
    {
        public static string Nombre(this TipoScript tipo) => tipo switch
        {
            TipoScript.Crear => "CREATE",
            TipoScript.Seleccionar => "SELECT",
            TipoScript.SeleccionarPrimeras => "SELECT TOP (100)",
            TipoScript.Insertar => "INSERT",
            TipoScript.Actualizar => "UPDATE",
            TipoScript.Eliminar => "DELETE",
            _ => "DROP"
        };
    }
}
