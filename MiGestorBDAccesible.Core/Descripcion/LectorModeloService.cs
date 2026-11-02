using MiGestorBDAccesible.Core.Conexiones;
using MiGestorBDAccesible.Core.Estructura;
using MiGestorBDAccesible.Core.Proveedores;

namespace MiGestorBDAccesible.Core.Descripcion
{
    public record AvanceLecturaModelo(int Leidos, int Total);

    public static class LectorModeloService
    {
        private static readonly TipoObjeto[] _tipos = [TipoObjeto.Vista, TipoObjeto.Procedimiento, TipoObjeto.Funcion, TipoObjeto.Paquete];

        public static async Task<ModeloBaseDatosModelo> LeerAsync(
            IProveedorBaseDatos proveedor,
            ConexionActiva conexion,
            string baseDatos,
            IProgress<AvanceLecturaModelo>? avance = null,
            CancellationToken cancelacion = default)
        {
            var tablas = await proveedor.ObtenerTablasAsync(conexion, baseDatos, cancelacion);
            var cantidades = await proveedor.ContarObjetosAsync(conexion, baseDatos, cancelacion);
            var objetos = new List<ObjetoBaseDatosModelo>();

            foreach (var tipo in _tipos.Where(t => cantidades.GetValueOrDefault(t) > 0))
            {
                objetos.AddRange(await proveedor.ObtenerObjetosAsync(conexion, baseDatos, tipo, cancelacion));
            }

            var total = tablas.Count + objetos.Count;
            var leidos = 0;
            var modelo = new ModeloBaseDatosModelo { BaseDatos = baseDatos };
            avance?.Report(new(0, total));

            foreach (var tabla in tablas)
            {
                modelo.Tablas.Add(await proveedor.ObtenerDetalleTablaAsync(conexion, baseDatos, tabla, cancelacion));
                avance?.Report(new(++leidos, total));
            }

            foreach (var objeto in objetos)
            {
                modelo.Objetos.Add(await proveedor.ObtenerDetalleObjetoAsync(conexion, baseDatos, objeto, cancelacion));
                avance?.Report(new(++leidos, total));
            }

            return modelo;
        }
    }
}
