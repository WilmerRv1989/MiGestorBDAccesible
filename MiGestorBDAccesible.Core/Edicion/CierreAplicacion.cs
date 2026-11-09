using MiGestorBDAccesible.Core.Proveedores;
using MiGestorBDAccesible.Core.Conexiones;
using MiGestorBDAccesible.Core.Ejecucion;

namespace MiGestorBDAccesible.Core.Edicion
{
    public static class CierreAplicacion
    {
        public static event Action? EstadoActualizado;

        public static bool HayOperacionesEnCurso => ContextoCierre.Actual.HayOperacionesEnCurso;

        private static List<ContextoCierre> Contextos(IEnumerable<IFinalizadorSesiones> proveedores) =>
            [.. proveedores.Select(p => p.Contexto).Append(ContextoCierre.Actual).Distinct()];

        public static bool TieneOperacionesEnCurso(IEnumerable<IFinalizadorSesiones> proveedores) =>
            Contextos(proveedores).Any(c => c.HayOperacionesEnCurso);

        public static IDisposable BloquearOperaciones() => BloquearOperaciones([]);

        public static IDisposable BloquearOperaciones(IEnumerable<IFinalizadorSesiones> proveedores) =>
            new Bloqueos(Contextos(proveedores).Select(c => c.BloquearOperaciones()).ToList());

        private sealed class Bloqueos(List<IDisposable> bloqueos) : IDisposable
        {
            public void Dispose()
            {
                foreach (var bloqueo in bloqueos) bloqueo.Dispose();
            }
        }

        public static bool HayCambiosPendientes(IEnumerable<IFinalizadorSesiones> proveedores)
        {
            var todos = proveedores.ToList();
            return Contextos(todos).Any(c => c.HayCambiosPendientes) || todos.Any(p => p.TieneCambiosPendientes());
        }

        public static bool PuedeConfirmarCambios(IEnumerable<IFinalizadorSesiones> proveedores)
        {
            var todos = proveedores.ToList();
            return Contextos(todos).Any(c => c.HayCambiosConfirmables)
                || todos.Any(p => p.TieneCambiosPendientes() && p.PuedeConfirmar());
        }

        public static async Task<ResultadoCierreModelo> TerminarAsync(IEnumerable<IFinalizadorSesiones> proveedores, bool confirmar)
        {
            var todos = proveedores.ToList();
            var contextos = Contextos(todos);
            using var bloqueo = BloquearOperaciones(todos);
            var errores = new List<string>();

            if (contextos.Any(c => c.HayOperacionesEnCurso))
            {
                return new ResultadoCierreModelo
                {
                    Errores = ["Hay una ejecución en curso. Espera a que termine o cancélala antes de confirmar el cierre."],
                    QuedanCambios = HayCambiosPendientes(todos),
                    PuedeReintentar = false
                };
            }

            foreach (var proveedor in todos)
            {
                try
                {
                    var resultado = await proveedor.FinalizarSesionAsync(null, confirmar);
                    errores.AddRange(resultado.Errores.Select(error => $"{proveedor.Motor.Nombre()}: {error}"));
                }
                catch (Exception ex)
                {
                    errores.Add($"{proveedor.Motor.Nombre()}: {ex.Message}");
                }
            }

            foreach (var contexto in contextos)
            {
                using var ambito = contexto.Activar();
                errores.AddRange((await RegistroSesionesEdicion.TerminarTodasAsync(confirmar)).Select(error => $"Edición de filas: {error}"));
            }
            EstadoActualizado?.Invoke();
            var quedanCambios = HayCambiosPendientes(todos);
            if (quedanCambios && errores.Count == 0)
            {
                errores.Add("Siguen quedando cambios pendientes. Vuelve a decidir si quieres confirmarlos o salir sin guardar.");
            }
            return new ResultadoCierreModelo
            {
                Errores = errores,
                QuedanCambios = quedanCambios,
                PuedeReintentar = PuedeConfirmarCambios(todos)
            };
        }

        public static async Task SalirSinGuardarAsync(IEnumerable<IFinalizadorSesiones> proveedores)
        {
            var todos = proveedores.ToList();
            var contextos = Contextos(todos);
            using var bloqueo = BloquearOperaciones(todos);
            foreach (var contexto in contextos) contexto.Operaciones.CancelarActivas();

            foreach (var contexto in contextos)
            {
                using var ambito = contexto.Activar();
                await RegistroSesionesEdicion.TerminarTodasAsync(false);
                await RegistroSesionesEdicion.DescartarTodasAsync();
                await contexto.Operaciones.EsperarAsync();
            }

            foreach (var proveedor in todos)
            {
                try
                {
                    await proveedor.FinalizarSesionAsync(null, false);
                }
                catch (Exception)
                {
                }
                finally
                {
                    try { await proveedor.DescartarSesionesAsync(null); }
                    catch (Exception) { }
                }
            }
        }

        public static bool OfreceReintentar(ResultadoCierreModelo resultado, bool confirmar) =>
            confirmar && resultado.QuedanCambios && resultado.PuedeReintentar;

        public static string TextoFallo(ResultadoCierreModelo resultado, bool confirmar) =>
            $"No se pudo terminar el cierre.\n\n{string.Join("\n\n", resultado.Errores)}\n\n" +
            (OfreceReintentar(resultado, confirmar)
                ? "Sí: volver a intentar confirmar los cambios pendientes (COMMIT).\nNo: salir sin guardar, deshaciendo los cambios pendientes (ROLLBACK).\nCancelar: seguir en la aplicación."
                : "Aceptar: salir sin guardar los cambios que queden (ROLLBACK).\nCancelar: seguir en la aplicación.");
    }

    public class ResultadoCierreModelo
    {
        public List<string> Errores { get; init; } = [];

        public bool QuedanCambios { get; init; }

        public bool PuedeReintentar { get; init; } = true;

        public bool Correcto => Errores.Count == 0 && !QuedanCambios;
    }
}
