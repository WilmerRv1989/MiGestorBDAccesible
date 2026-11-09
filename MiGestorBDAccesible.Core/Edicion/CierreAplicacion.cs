using MiGestorBDAccesible.Core.Proveedores;

namespace MiGestorBDAccesible.Core.Edicion
{
    public static class CierreAplicacion
    {
        public static bool HayCambiosPendientes(IEnumerable<IFinalizadorSesiones> proveedores) => throw new NotImplementedException();

        public static Task<ResultadoCierreModelo> TerminarAsync(IEnumerable<IFinalizadorSesiones> proveedores, bool confirmar) => throw new NotImplementedException();

        public static Task SalirSinGuardarAsync(IEnumerable<IFinalizadorSesiones> proveedores) => throw new NotImplementedException();

        public static bool OfreceReintentar(ResultadoCierreModelo resultado, bool confirmar) => throw new NotImplementedException();

        public static string TextoFallo(ResultadoCierreModelo resultado, bool confirmar) => throw new NotImplementedException();
    }

    public class ResultadoCierreModelo
    {
        public List<string> Errores { get; init; } = [];

        public bool QuedanCambios { get; init; }

        public bool Correcto => Errores.Count == 0;
    }
}
