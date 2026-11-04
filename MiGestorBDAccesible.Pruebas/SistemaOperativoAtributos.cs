namespace MiGestorBDAccesible.Pruebas
{
    public sealed class FactSoloWindowsAttribute : FactAttribute
    {
        public FactSoloWindowsAttribute()
        {
            if (!OperatingSystem.IsWindows())
            {
                Skip = "Solo se ejecuta en Windows.";
            }
        }
    }

    public sealed class FactSoloMacOSAttribute : FactAttribute
    {
        public FactSoloMacOSAttribute()
        {
            if (!OperatingSystem.IsMacOS())
            {
                Skip = "Solo se ejecuta en macOS.";
            }
        }
    }
}
