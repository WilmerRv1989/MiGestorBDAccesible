using System.Reflection;

namespace MiGestorBDAccesible.Core
{
    public static class InformacionAplicacion
    {
        public const string Nombre = "Mi Gestor BD Accesible";

        public static string VersionCompleta(Assembly ensamblado) =>
            ensamblado.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
            ?? ensamblado.GetName().Version?.ToString(3)
            ?? "1.0.0";

        public static string VersionCorta(string version)
        {
            var partes = version.Split('-')[0].Split('.');

            return partes.Length >= 3 && partes[2] == "0"
                ? $"{partes[0]}.{partes[1]}"
                : string.Join('.', partes.Take(3));
        }
    }
}
