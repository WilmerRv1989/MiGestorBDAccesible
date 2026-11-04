namespace MiGestorBDAccesible.Core.Ejecucion
{
    public class ScriptNoDivisibleException(string mensaje, int linea) : Exception(mensaje)
    {
        public int Linea { get; } = linea;
    }
}
