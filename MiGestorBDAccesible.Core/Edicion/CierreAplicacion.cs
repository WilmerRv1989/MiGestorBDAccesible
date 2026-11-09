using MiGestorBDAccesible.Core.Proveedores;
using MiGestorBDAccesible.Core.Conexiones;
using MiGestorBDAccesible.Core.Ejecucion;

namespace MiGestorBDAccesible.Core.Edicion
{
    public static class CierreAplicacion
    {
        public static event Action? EstadoActualizado;

        public static bool HayOperacionesEnCurso => OperacionesBaseDatos.HayOperaciones;

        public static IDisposable BloquearOperaciones() => OperacionesBaseDatos.Bloquear();

        public static bool HayCambiosPendientes(IEnumerable<IFinalizadorSesiones> proveedores) =>
            RegistroSesionesEdicion.HayCambiosPendientes || proveedores.Any(p => p.TieneCambiosPendientes());

        public static async Task<ResultadoCierreModelo> TerminarAsync(IEnumerable<IFinalizadorSesiones> proveedores, bool confirmar)
        {
            using var bloqueo = BloquearOperaciones();
            var todos = proveedores.ToList();
            var errores = new List<string>();

            if (!await OperacionesBaseDatos.EsperarAsync())
            {
                errores.Add("Hay operaciones que todavía se están cancelando. La aplicación sigue abierta; vuelve a decidir cuando terminen.");
                return new ResultadoCierreModelo { Errores = errores, QuedanCambios = HayCambiosPendientes(todos) };
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

            errores.AddRange((await RegistroSesionesEdicion.TerminarTodasAsync(confirmar)).Select(error => $"Edición de filas: {error}"));
            EstadoActualizado?.Invoke();
            var quedanCambios = HayCambiosPendientes(todos);
            if (quedanCambios && errores.Count == 0)
            {
                errores.Add("Siguen quedando cambios pendientes. Vuelve a decidir si quieres confirmarlos o salir sin guardar.");
            }
            return new ResultadoCierreModelo { Errores = errores, QuedanCambios = quedanCambios };
        }

        public static async Task SalirSinGuardarAsync(IEnumerable<IFinalizadorSesiones> proveedores)
        {
            using var bloqueo = BloquearOperaciones();
            await RegistroSesionesEdicion.TerminarTodasAsync(false);
            await RegistroSesionesEdicion.DescartarTodasAsync();
            await OperacionesBaseDatos.EsperarAsync();

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

        public bool Correcto => Errores.Count == 0 && !QuedanCambios;
    }
}
