using MiGestorBDAccesible.Core.Proveedores;
using MiGestorBDAccesible.Core.Conexiones;

namespace MiGestorBDAccesible.Core.Edicion
{
    public static class CierreAplicacion
    {
        public static event Action? EstadoActualizado;

        public static bool HayCambiosPendientes(IEnumerable<IFinalizadorSesiones> proveedores) =>
            RegistroSesionesEdicion.HayCambiosPendientes || proveedores.Any(p => p.TieneCambiosPendientes());

        public static async Task<ResultadoCierreModelo> TerminarAsync(IEnumerable<IFinalizadorSesiones> proveedores, bool confirmar)
        {
            var todos = proveedores.ToList();
            var errores = new List<string>();

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

            errores.AddRange((await RegistroSesionesEdicion.TerminarTodasAsync(confirmar)).Select(error => $"Edición de filas: {error}"));
            EstadoActualizado?.Invoke();
            return new ResultadoCierreModelo { Errores = errores, QuedanCambios = HayCambiosPendientes(todos) };
        }

        public static async Task SalirSinGuardarAsync(IEnumerable<IFinalizadorSesiones> proveedores)
        {
            foreach (var proveedor in proveedores)
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
                    try
                    {
                        await proveedor.DescartarSesionesAsync(null);
                    }
                    catch (Exception)
                    {
                    }
                }
            }

            await RegistroSesionesEdicion.TerminarTodasAsync(false);
            await RegistroSesionesEdicion.DescartarTodasAsync();
        }

        public static bool OfreceReintentar(ResultadoCierreModelo resultado, bool confirmar) => confirmar && resultado.QuedanCambios;

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

        public bool Correcto => Errores.Count == 0;
    }
}
